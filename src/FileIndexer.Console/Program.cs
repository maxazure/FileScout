using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileIndexer.Core;
using FileIndexer.Storage;

namespace FileIndexer.ConsoleApp
{
    internal class Program
    {
        private static readonly HashSet<string> IgnoredFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", "venv", ".venv", "env", "__pycache__", ".git", ".svn", ".hg",
            ".idea", ".vscode", "dist", "build", "target", "bin", "obj", ".DS_Store",
            ".pytest_cache", ".mypy_cache"
        };

        private static bool ShouldIgnoreDirectory(string directoryPath)
        {
            var dirName = Path.GetFileName(directoryPath);
            return IgnoredFolders.Contains(dirName);
        }

        static async Task Main(string[] args)
        {
            string rootPath = args.Length > 0 ? args[0] : @"D:\";
            string dbPath = args.Length > 1 ? args[1] : "fileindex.db";
            int batchSize = args.Length > 2 && int.TryParse(args[2], out int bs) ? bs : 1000;

            Console.WriteLine($"开始扫描目录: {rootPath}");
            Console.WriteLine($"数据库文件: {dbPath}");
            Console.WriteLine($"批量插入大小: {batchSize} 条/批次");
            Console.WriteLine("使用两阶段分离扫描，并将结果存储到数据库...");
            
            var sw = Stopwatch.StartNew();
            
            // 初始化存储模块（指定批量大小）
            using var storageModule = new StorageModule(batchSize);
            storageModule.Initialize(dbPath);
            storageModule.Start();
            
            int fileCount = await TwoPhaseDirectoryFilesScanWithStorage(rootPath, storageModule);
            
            // 停止存储模块
            await storageModule.StopAsync();
            
            sw.Stop();
            Console.WriteLine($"索引完成，共发现 {fileCount} 个文件。");
            Console.WriteLine($"总耗时：{sw.Elapsed}");
            Console.WriteLine($"数据已保存到: {dbPath}");
        }

        // 两阶段分离扫描并存储到数据库
        static async Task<int> TwoPhaseDirectoryFilesScanWithStorage(string rootPath, IStorageModule storageModule)
        {
            Console.WriteLine("阶段1：多线程收集所有目录结构");
            
            var sw = Stopwatch.StartNew();
            
            // 阶段1：逐层扫描目录结构，避免递归性能问题
            var allDirectories = new ConcurrentBag<string>();
            var currentLevelDirs = new HashSet<string> { rootPath };
            allDirectories.Add(rootPath);
            
            Console.WriteLine("正在收集目录结构...");
            var directoryScanTime = Stopwatch.StartNew();
            int depth = 1;
            
            while (currentLevelDirs.Count > 0)
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
                    new ParallelOptions { MaxDegreeOfParallelism = parallelism }, 
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
            
            // 阶段2：并行扫描每个目录的文件并存储到数据库
            Console.WriteLine("阶段2：正在扫描文件并存储到数据库...");
            var fileScanTime = Stopwatch.StartNew();
            
            int totalFiles = 0;
            int processedDirs = 0;
            var lockObject = new object();
            
            // 分批处理目录，避免内存压力
            const int batchSize = 1000;
            for (int i = 0; i < directoryList.Length; i += batchSize)
            {
                var batch = directoryList.Skip(i).Take(batchSize);
                
                Parallel.ForEach(batch, new ParallelOptions 
                { 
                    MaxDegreeOfParallelism = Environment.ProcessorCount 
                }, 
                directory =>
                {
                    try
                    {
                        // 只扫描当前目录的文件，不包括子目录
                        var files = Directory.GetFiles(directory);
                        
                        foreach (var filePath in files)
                        {
                            try
                            {
                                var fileInfo = new FileInfo(filePath);
                                var fileMetadata = new FileMetadata(
                                    fileInfo.Name,
                                    fileInfo.FullName,
                                    fileInfo.Length,
                                    fileInfo.LastWriteTimeUtc
                                );
                                
                                storageModule.Enqueue(fileMetadata);
                            }
                            catch
                            {
                                // 忽略无法访问的文件
                            }
                        }
                        
                        int fileCount = files.Length;
                        Interlocked.Add(ref totalFiles, fileCount);
                        int completed = Interlocked.Increment(ref processedDirs);
                        
                        // 每处理1000个目录报告一次进度
                        if (completed % 1000 == 0)
                        {
                            Console.WriteLine($"已处理 {completed}/{directoryList.Length} 个目录，发现 {totalFiles} 个文件，耗时：{fileScanTime.Elapsed}，队列：{storageModule.PendingCount}");
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
            Console.WriteLine($"阶段2完成：扫描完成，总共发现 {totalFiles} 个文件，文件扫描耗时：{fileScanTime.Elapsed}");
            
            // 等待所有文件写入数据库
            Console.WriteLine("正在等待所有文件写入数据库...");
            while (storageModule.PendingCount > 0)
            {
                Console.WriteLine($"剩余队列：{storageModule.PendingCount}");
                await Task.Delay(1000);
            }
            
            Console.WriteLine($"总耗时：{sw.Elapsed}");
            
            return totalFiles;
        }
    }
}