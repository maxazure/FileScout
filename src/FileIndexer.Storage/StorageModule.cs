using System;
using System.Collections.Concurrent;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileIndexer.Core;

namespace FileIndexer.Storage
{
    /// <summary>
    /// 存储模块实现：单线程从队列消费并将文件元数据写入 SQLite。
    /// </summary>
    public class StorageModule : IStorageModule
    {
        private BlockingCollection<IFileMetadata> _queue;
        private string _dbPath;
        private Task _worker;
        private CancellationTokenSource _cts;
        private SQLiteConnection _connection;
        private volatile bool _started;

        /// <inheritdoc/>
        public int PendingCount => _queue?.Count ?? 0;

        /// <summary>
        /// 初始化存储模块，指定 SQLite 数据库文件路径。
        /// </summary>
        /// <param name="databasePath">.db 文件完整路径。</param>
        public void Initialize(string databasePath)
        {
            _dbPath = databasePath;
            _queue = new BlockingCollection<IFileMetadata>(new ConcurrentQueue<IFileMetadata>());
            _cts = new CancellationTokenSource();

            bool newDb = !File.Exists(databasePath);
            _connection = new SQLiteConnection($"Data Source={databasePath};Version=3;");
            _connection.Open();

            // 建表与索引
            using (var cmd = _connection.CreateCommand())
            {
                // 文件表
                cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS files (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT,
                    path TEXT,
                    size INTEGER,
                    mtime INTEGER
                );
                CREATE INDEX IF NOT EXISTS idx_name ON files(name);";
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 启动后台写入线程，开始消费队列。
        /// </summary>
        public void Start()
        {
            if (_started) throw new InvalidOperationException("Already started.");
            _started = true;
            _worker = Task.Run(() => WorkerLoop(_cts.Token), _cts.Token);
        }

        /// <summary>
        /// 将一个文件元数据入队，由后台线程顺序写入数据库。
        /// </summary>
        /// <param name="fileMetadata">待写入的文件元数据。</param>
        public void Enqueue(IFileMetadata fileMetadata)
        {
            if (!_started) throw new InvalidOperationException("StorageModule not started.");
            _queue.Add(fileMetadata);
        }

        /// <summary>
        /// 通知后台线程处理完当前队列并优雅退出，返回完成任务的异步 <see cref="Task"/>。
        /// </summary>
        public async Task StopAsync()
        {
            _queue.CompleteAdding();
            if (_worker != null)
                await _worker.ConfigureAwait(false);
            _connection?.Close();
            _cts?.Cancel();
            _started = false;
        }

        /// <summary>
        /// 释放资源。
        /// </summary>
        public void Dispose()
        {
            _cts?.Cancel();
            _queue?.Dispose();
            _connection?.Dispose();
        }

        /// <summary>
        /// 后台线程循环，批量写入数据库。
        /// </summary>
        /// <param name="token">取消令牌。</param>
        private void WorkerLoop(CancellationToken token)
        {
            try
            {
                foreach (var item in _queue.GetConsumingEnumerable(token))
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO files (name, path, size, mtime) VALUES (@name, @path, @size, @mtime);";
                        cmd.Parameters.AddWithValue("@name", item.Name);
                        cmd.Parameters.AddWithValue("@path", item.FullPath);
                        cmd.Parameters.AddWithValue("@size", item.Size);
                        cmd.Parameters.AddWithValue("@mtime", ((DateTimeOffset)item.LastModifiedUtc).ToUnixTimeSeconds());
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 取消
            }
        }
    }
}