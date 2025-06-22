using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FileIndexer.Core
{
    /// <summary>
    /// 索引模块实现：多线程遍历目录，将每个文件通过事件推送给订阅者。
    /// </summary>
    public class IndexingModule : IIndexingModule
    {
        private CancellationTokenSource? _cts;
        private bool _isIndexing;
        private HashSet<string>? _ignoredFolders;

        /// <inheritdoc/>
        public event EventHandler<FileDiscoveredEventArgs>? FileDiscovered;

        /// <summary>
        /// 开始全量索引指定目录，使用指定的并行度。
        /// </summary>
        /// <param name="rootDirectory">要索引的根目录路径。</param>
        /// <param name="maxDegreeOfParallelism">最大并行线程数。</param>
        /// <param name="cancellationToken">取消令牌，可选。</param>
        public void StartIndexing(string rootDirectory, int maxDegreeOfParallelism, CancellationToken cancellationToken = default)
        {
            StartIndexing(rootDirectory, maxDegreeOfParallelism, null, cancellationToken);
        }

        /// <summary>
        /// 开始全量索引指定目录，使用指定的并行度和忽略文件夹配置。
        /// </summary>
        /// <param name="rootDirectory">要索引的根目录路径。</param>
        /// <param name="maxDegreeOfParallelism">最大并行线程数。</param>
        /// <param name="ignoredFolders">要忽略的文件夹名称集合。</param>
        /// <param name="cancellationToken">取消令牌，可选。</param>
        public void StartIndexing(string rootDirectory, int maxDegreeOfParallelism, IEnumerable<string>? ignoredFolders, CancellationToken cancellationToken = default)
        {
            if (_isIndexing)
                throw new InvalidOperationException("Indexing is already in progress.");

            _isIndexing = true;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            
            // 设置忽略文件夹，使用不区分大小写的比较
            _ignoredFolders = ignoredFolders != null 
                ? new HashSet<string>(ignoredFolders, StringComparer.OrdinalIgnoreCase) 
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Task.Run(() =>
            {
                try
                {
                    // 使用两阶段高性能扫描
                    TwoPhaseDirectoryScan(rootDirectory, _cts.Token);
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
        /// 检查目录是否应该被忽略。
        /// </summary>
        /// <param name="directoryPath">目录路径。</param>
        /// <returns>如果应该忽略返回 true，否则返回 false。</returns>
        private bool ShouldIgnoreDirectory(string directoryPath)
        {
            if (_ignoredFolders == null || !_ignoredFolders.Any())
                return false;
                
            var dirName = Path.GetFileName(directoryPath);
            return _ignoredFolders.Contains(dirName);
        }

        /// <summary>
        /// 两阶段高性能目录扫描：先收集所有目录结构，再并行扫描文件。
        /// </summary>
        private void TwoPhaseDirectoryScan(string rootPath, CancellationToken cancellationToken)
        {
            Console.WriteLine("阶段1：多线程收集所有目录结构");
            
            // 阶段1：逐层扫描目录结构，避免递归性能问题
            var allDirectories = new ConcurrentBag<string>();
            var currentLevelDirs = new HashSet<string> { rootPath };
            allDirectories.Add(rootPath);
            
            Console.WriteLine("正在收集目录结构...");
            var directoryScanTime = Stopwatch.StartNew();
            int depth = 1;
            
            while (currentLevelDirs.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                var levelStartTime = Stopwatch.StartNew();
                Console.WriteLine($"开始扫描深度 {depth}，当前层有 {currentLevelDirs.Count} 个目录");
                
                var nextLevelDirs = new ConcurrentBag<string>();
                
                // 根据深度动态调整并发度，深层降低I/O压力
                int parallelism = depth <= 10 ? Environment.ProcessorCount : 
                                 depth <= 20 ? Math.Max(2, Environment.ProcessorCount / 2) : 
                                 1;
                
                // 多线程扫描当前深度的所有目录，只获取直接子目录
                Parallel.ForEach(currentLevelDirs, 
                    new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = cancellationToken }, 
                    currentDir =>
                {
                    try
                    {
                        // 只获取当前目录的直接子目录，不递归（跳过忽略的目录）
                        var subdirs = Directory.GetDirectories(currentDir, "*", SearchOption.TopDirectoryOnly);
                        foreach (var subdir in subdirs)
                        {
                            if (!ShouldIgnoreDirectory(subdir))
                            {
                                allDirectories.Add(subdir);
                                nextLevelDirs.Add(subdir);
                            }
                        }
                    }
                    catch 
                    {
                        // 忽略访问被拒绝的目录
                    }
                });
                
                levelStartTime.Stop();
                
                // 转换为不重复的集合用于下一层
                currentLevelDirs = nextLevelDirs.Distinct().ToHashSet();
                
                double avgTimePerDir = levelStartTime.Elapsed.TotalMilliseconds / Math.Max(1, currentLevelDirs.Count);
                Console.WriteLine($"深度 {depth} 扫描完成，发现 {nextLevelDirs.Count} 个子目录，耗时：{levelStartTime.Elapsed}，总目录数：{allDirectories.Count}，平均：{avgTimePerDir:F2}ms/目录，并发度：{parallelism}");
                depth++;
                
                // 防止无限深度（安全机制）
                if (depth > 50)
                {
                    Console.WriteLine("已达到最大深度限制50层，停止扫描");
                    break;
                }
            }
            
            directoryScanTime.Stop();
            var directoryList = allDirectories.ToArray();
            Console.WriteLine($"阶段1完成：发现 {directoryList.Length} 个目录，耗时：{directoryScanTime.Elapsed}");
            
            if (cancellationToken.IsCancellationRequested) return;
            
            // 阶段2：并行扫描每个目录的文件并触发事件
            Console.WriteLine("阶段2：正在扫描文件...");
            var fileScanTime = Stopwatch.StartNew();
            
            int processedDirs = 0;
            
            // 分批处理目录，避免内存压力
            const int batchSize = 1000;
            for (int i = 0; i < directoryList.Length && !cancellationToken.IsCancellationRequested; i += batchSize)
            {
                var batch = directoryList.Skip(i).Take(batchSize);
                
                Parallel.ForEach(batch, new ParallelOptions 
                { 
                    MaxDegreeOfParallelism = Environment.ProcessorCount,
                    CancellationToken = cancellationToken
                }, 
                directory =>
                {
                    try
                    {
                        // 只扫描当前目录的文件，不包括子目录
                        var files = Directory.GetFiles(directory);
                        
                        foreach (var filePath in files)
                        {
                            // 检查是否已取消
                            if (cancellationToken.IsCancellationRequested)
                                return;
                                
                            try
                            {
                                var fileInfo = new FileInfo(filePath);
                                var fileMetadata = new FileMetadata(
                                    fileInfo.Name,
                                    fileInfo.FullName,
                                    fileInfo.Length,
                                    fileInfo.LastWriteTimeUtc
                                );
                                
                                // 触发文件发现事件
                                FileDiscovered?.Invoke(this, new FileDiscoveredEventArgs(fileMetadata));
                            }
                            catch
                            {
                                // 忽略无法访问的文件
                            }
                        }
                        
                        int completed = Interlocked.Increment(ref processedDirs);
                        
                        // 每处理1000个目录报告一次进度
                        if (completed % 1000 == 0)
                        {
                            Console.WriteLine($"已处理 {completed}/{directoryList.Length} 个目录，耗时：{fileScanTime.Elapsed}");
                        }
                    }
                    catch
                    {
                        // 忽略访问被拒绝的目录
                        Interlocked.Increment(ref processedDirs);
                    }
                });
                
                // 每批之间稍作休息，释放内存压力
                if (i % (batchSize * 10) == 0)
                {
                    GC.Collect(0, GCCollectionMode.Optimized);
                }
            }
            
            fileScanTime.Stop();
            Console.WriteLine($"阶段2完成：文件扫描完成，文件扫描耗时：{fileScanTime.Elapsed}");
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