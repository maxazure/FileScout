using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FileIndexer.Core
{
    /// <summary>
    /// 索引模块实现：多线程遍历目录，将每个文件通过事件推送给订阅者。
    /// </summary>
    public class IndexingModule : IIndexingModule
    {
        private CancellationTokenSource _cts;
        private bool _isIndexing;

        /// <inheritdoc/>
        public event EventHandler<FileDiscoveredEventArgs> FileDiscovered;

        /// <summary>
        /// 开始全量索引指定目录，使用指定的并行度。
        /// </summary>
        /// <param name="rootDirectory">要索引的根目录路径。</param>
        /// <param name="maxDegreeOfParallelism">最大并行线程数。</param>
        /// <param name="cancellationToken">取消令牌，可选。</param>
        public void StartIndexing(string rootDirectory, int maxDegreeOfParallelism, CancellationToken cancellationToken = default)
        {
            if (_isIndexing)
                throw new InvalidOperationException("Indexing is already in progress.");

            _isIndexing = true;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            Task.Run(() =>
            {
                try
                {
                    var files = SafeEnumerateFiles(rootDirectory);
                    Parallel.ForEach(files,
                        new ParallelOptions
                        {
                            MaxDegreeOfParallelism = maxDegreeOfParallelism,
                            CancellationToken = _cts.Token
                        },
                        file =>
                        {
                            var info = new FileInfo(file);
                            var metadata = new FileMetadata(
                                info.Name,
                                info.FullName,
                                info.Length,
                                info.LastWriteTimeUtc
                            );
                            FileDiscovered?.Invoke(this, new FileDiscoveredEventArgs(metadata));
                        });
                }
                catch (OperationCanceledException)
                {
                    // 索引被取消
                }
                catch (Exception ex)
                {
                    Console.WriteLine("索引目录时发生异常：" + ex);
                }
                finally
                {
                    _isIndexing = false;
                }
            }, _cts.Token);
        }

        /// <summary>
        /// 安全递归遍历目录，遇到拒绝访问等异常时自动跳过。
        /// </summary>
        private static IEnumerable<string> SafeEnumerateFiles(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string path = pending.Pop();
                string[] subDirs = null;
                try
                {
                    subDirs = Directory.GetDirectories(path);
                }
                catch (UnauthorizedAccessException) { }
                catch (DirectoryNotFoundException) { }
                catch (PathTooLongException) { }
                if (subDirs != null)
                {
                    foreach (var dir in subDirs)
                        pending.Push(dir);
                }
                string[] files = null;
                try
                {
                    files = Directory.GetFiles(path);
                }
                catch (UnauthorizedAccessException) { }
                catch (DirectoryNotFoundException) { }
                catch (PathTooLongException) { }
                if (files != null)
                {
                    foreach (var file in files)
                        yield return file;
                }
            }
        }

        /// <summary>
        /// 请求停止当前索引操作，并释放相关资源。
        /// </summary>
        public void StopIndexing()
        {
            _cts?.Cancel();
            _isIndexing = false;
        }

        /// <summary>
        /// 释放资源。
        /// </summary>
        public void Dispose()
        {
            StopIndexing();
            _cts?.Dispose();
        }
    }
}