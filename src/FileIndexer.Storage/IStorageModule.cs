using System;
using System.Threading.Tasks;
using FileIndexer.Core;

namespace FileIndexer.Storage
{
    /// <summary>
    /// 存储模块接口：单线程从队列消费并将文件元数据写入 SQLite。
    /// </summary>
    public interface IStorageModule : IDisposable
    {
        /// <summary>
        /// 初始化存储模块，指定 SQLite 数据库文件路径。
        /// </summary>
        /// <param name="databasePath">.db 文件完整路径。</param>
        void Initialize(string databasePath);

        /// <summary>
        /// 启动后台写入线程，开始消费队列。
        /// </summary>
        void Start();

        /// <summary>
        /// 将一个文件元数据入队，由后台线程顺序写入数据库。
        /// </summary>
        /// <param name="fileMetadata">待写入的文件元数据。</param>
        void Enqueue(IFileMetadata fileMetadata);

        /// <summary>
        /// 通知后台线程处理完当前队列并优雅退出，返回完成任务的异步 <see cref="Task"/>。
        /// </summary>
        Task StopAsync();

        /// <summary>
        /// 当前待处理队列长度，可用于进度监控和测试断言。
        /// </summary>
        int PendingCount { get; }
    }
}