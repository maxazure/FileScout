using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileIndexer.Core;

namespace FileIndexer.Storage
{
    /// <summary>
    /// 存储模块实现：单线程从队列消费并将文件元数据批量写入 SQLite。
    /// </summary>
    public class StorageModule : IStorageModule
    {
        private BlockingCollection<IFileMetadata> _queue;
        private string _dbPath;
        private Task _worker;
        private CancellationTokenSource _cts;
        private SQLiteConnection _connection;
        private volatile bool _started;
        private readonly int _batchSize;

        /// <summary>
        /// 构造函数，指定批量插入大小。
        /// </summary>
        /// <param name="batchSize">每次批量插入的记录数，默认1000条。</param>
        public StorageModule(int batchSize = 1000)
        {
            _batchSize = batchSize;
        }

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
            
            try
            {
                // 确保数据库文件的目录存在
                var dbDirectory = Path.GetDirectoryName(databasePath);
                if (!string.IsNullOrEmpty(dbDirectory) && !Directory.Exists(dbDirectory))
                {
                    Directory.CreateDirectory(dbDirectory);
                    Console.WriteLine($"创建数据库目录: {dbDirectory}");
                }

                _connection = new SQLiteConnection($"Data Source={databasePath};Version=3;");
                _connection.Open();
                
                if (newDb)
                {
                    Console.WriteLine($"创建新数据库: {databasePath}");
                }
                else
                {
                    Console.WriteLine($"连接到现有数据库: {databasePath}");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"无法初始化数据库 {databasePath}: {ex.Message}", ex);
            }

            // 优化SQLite设置以提高批量插入性能
            using (var cmd = _connection.CreateCommand())
            {
                // 设置同步模式为OFF，提高写入速度（在程序正常结束时数据仍然安全）
                cmd.CommandText = "PRAGMA synchronous = OFF;";
                cmd.ExecuteNonQuery();
                
                // 设置日志模式为WAL，提高并发性能
                cmd.CommandText = "PRAGMA journal_mode = WAL;";
                cmd.ExecuteNonQuery();
                
                // 增加缓存大小（以页为单位，每页通常4KB，这里设置为100MB）
                cmd.CommandText = "PRAGMA cache_size = 25000;";
                cmd.ExecuteNonQuery();
                
                // 设置临时存储在内存中
                cmd.CommandText = "PRAGMA temp_store = MEMORY;";
                cmd.ExecuteNonQuery();
            }

            // 建表与索引
            try
            {
                using (var cmd = _connection.CreateCommand())
                {
                    // 检查表是否存在
                    cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='files';";
                    var tableExists = cmd.ExecuteScalar() != null;
                    
                    if (!tableExists)
                    {
                        Console.WriteLine("创建文件索引表...");
                        // 文件表（添加路径唯一约束）
                        cmd.CommandText = @"
                        CREATE TABLE files (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            name TEXT,
                            path TEXT UNIQUE,
                            size INTEGER,
                            mtime INTEGER
                        );";
                        cmd.ExecuteNonQuery();
                        Console.WriteLine("文件索引表创建完成");
                    }
                    else
                    {
                        Console.WriteLine("使用现有文件索引表");
                    }
                    
                    // 创建索引（如果是新数据库，延迟创建索引直到所有数据插入完成）
                    if (!newDb)
                    {
                        Console.WriteLine("检查和创建数据库索引...");
                        cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_name ON files(name);";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_path ON files(path);";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_size ON files(size);";
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"创建数据库表时出错: {ex.Message}", ex);
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
                
            // 创建索引以提高查询性能
            CreateIndexes();
            
            _connection?.Close();
            _cts?.Cancel();
            _started = false;
        }

        /// <summary>
        /// 创建数据库索引以提高查询性能。
        /// </summary>
        private void CreateIndexes()
        {
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    CREATE INDEX IF NOT EXISTS idx_name ON files(name);
                    CREATE INDEX IF NOT EXISTS idx_path ON files(path);
                    CREATE INDEX IF NOT EXISTS idx_size ON files(size);";
                cmd.ExecuteNonQuery();
                Console.WriteLine("数据库索引创建完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"创建索引时出错: {ex.Message}");
            }
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
                var batch = new List<IFileMetadata>(_batchSize);
                
                foreach (var item in _queue.GetConsumingEnumerable(token))
                {
                    batch.Add(item);
                    
                    // 当批次达到指定大小时，执行批量插入
                    if (batch.Count >= _batchSize)
                    {
                        InsertBatch(batch);
                        batch.Clear();
                    }
                }
                
                // 处理剩余的项目
                if (batch.Count > 0)
                {
                    InsertBatch(batch);
                }
            }
            catch (OperationCanceledException)
            {
                // 取消
            }
        }

        /// <summary>
        /// 批量插入文件元数据到数据库。
        /// </summary>
        /// <param name="items">要插入的文件元数据列表。</param>
        private void InsertBatch(IList<IFileMetadata> items)
        {
            if (items.Count == 0) return;

            using var transaction = _connection.BeginTransaction();
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = transaction;
                // 使用INSERT OR REPLACE处理重复路径：如果路径存在则更新，不存在则插入
                cmd.CommandText = "INSERT OR REPLACE INTO files (name, path, size, mtime) VALUES (@name, @path, @size, @mtime);";
                
                // 预创建参数
                var nameParam = cmd.Parameters.Add("@name", System.Data.DbType.String);
                var pathParam = cmd.Parameters.Add("@path", System.Data.DbType.String);
                var sizeParam = cmd.Parameters.Add("@size", System.Data.DbType.Int64);
                var mtimeParam = cmd.Parameters.Add("@mtime", System.Data.DbType.Int64);
                
                foreach (var item in items)
                {
                    nameParam.Value = item.Name;
                    pathParam.Value = item.FullPath;
                    sizeParam.Value = item.Size;
                    mtimeParam.Value = ((DateTimeOffset)item.LastModifiedUtc).ToUnixTimeSeconds();
                    cmd.ExecuteNonQuery();
                }
                
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }
}