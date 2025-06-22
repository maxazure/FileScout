using System;
using System.Collections.Generic;
using System.Threading;

namespace FileIndexer.Core
{
    /// <summary>
    /// 索引模块接口：多线程遍历目录，将每个文件通过事件推送给订阅者。
    /// </summary>
    public interface IIndexingModule : IDisposable
    {
        /// <summary>
        /// 当发现一个文件时触发，携带该文件的 <see cref="IFileMetadata"/>。
        /// </summary>
        event EventHandler<FileDiscoveredEventArgs> FileDiscovered;

        /// <summary>
        /// 开始全量索引指定目录，使用指定的并行度。
        /// </summary>
        /// <param name="rootDirectory">要索引的根目录路径。</param>
        /// <param name="maxDegreeOfParallelism">最大并行线程数。</param>
        /// <param name="cancellationToken">取消令牌，可选。</param>
        void StartIndexing(string rootDirectory, int maxDegreeOfParallelism, CancellationToken cancellationToken = default);

        /// <summary>
        /// 开始全量索引指定目录，使用指定的并行度和忽略文件夹配置。
        /// </summary>
        /// <param name="rootDirectory">要索引的根目录路径。</param>
        /// <param name="maxDegreeOfParallelism">最大并行线程数。</param>
        /// <param name="ignoredFolders">要忽略的文件夹名称集合。</param>
        /// <param name="cancellationToken">取消令牌，可选。</param>
        void StartIndexing(string rootDirectory, int maxDegreeOfParallelism, IEnumerable<string>? ignoredFolders, CancellationToken cancellationToken = default);

        /// <summary>
        /// 请求停止当前索引操作，并释放相关资源。
        /// </summary>
        void StopIndexing();
    }
}