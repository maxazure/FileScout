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


        static async Task Main(string[] args)
        {
            string rootPath = args.Length > 0 ? args[0] : @"D:\";
            string dbPath = args.Length > 1 ? args[1] : "fileindex.db";
            int batchSize = args.Length > 2 && int.TryParse(args[2], out int bs) ? bs : 1000;

            Console.WriteLine($"开始扫描目录: {rootPath}");
            Console.WriteLine($"数据库文件: {dbPath}");
            Console.WriteLine($"批量插入大小: {batchSize} 条/批次");
            Console.WriteLine("使用 IndexingModule 进行文件索引...");
            
            var sw = Stopwatch.StartNew();
            
            // 初始化存储模块（指定批量大小）
            using var storageModule = new StorageModule(batchSize);
            storageModule.Initialize(dbPath);
            storageModule.Start();
            
            // 使用 IndexingModule 进行文件索引
            int fileCount = await IndexDirectoryWithStorageModule(rootPath, storageModule);
            
            // 停止存储模块
            await storageModule.StopAsync();
            
            sw.Stop();
            Console.WriteLine($"索引完成，共发现 {fileCount} 个文件。");
            Console.WriteLine($"总耗时：{sw.Elapsed}");
            Console.WriteLine($"数据已保存到: {dbPath}");
        }

        // 使用 IndexingModule 进行文件索引并存储到数据库
        static async Task<int> IndexDirectoryWithStorageModule(string rootPath, IStorageModule storageModule)
        {
            Console.WriteLine("正在使用 IndexingModule 扫描文件...");
            
            var sw = Stopwatch.StartNew();
            int fileCount = 0;
            var lockObject = new object();
            
            // 创建取消令牌，支持优雅停止
            using var cts = new CancellationTokenSource();
            
            // 创建 IndexingModule 实例
            using var indexingModule = new IndexingModule();
            
            // 订阅文件发现事件
            indexingModule.FileDiscovered += (sender, e) =>
            {
                // 将发现的文件加入存储队列
                storageModule.Enqueue(e.Metadata);
                
                // 更新文件计数
                int currentCount = Interlocked.Increment(ref fileCount);
                
                // 每1000个文件报告一次进度
                if (currentCount % 50000 == 0)
                {
                    Console.WriteLine($"已发现 {currentCount} 个文件，耗时：{sw.Elapsed}，队列：{storageModule.PendingCount}");
                }
            };
            
            // 开始索引，传入忽略文件夹配置
            Console.WriteLine("开始索引目录...");
            indexingModule.StartIndexing(
                rootPath, 
                Environment.ProcessorCount, 
                IgnoredFolders.ToArray(), 
                cts.Token
            );
            
            // 等待索引完成（这里简化处理，实际应用中可能需要更复杂的同步逻辑）
            // 由于 IndexingModule.StartIndexing 是异步启动的，我们需要等待它完成
            await Task.Run(() =>
            {
                // 等待一段时间让索引开始
                Thread.Sleep(100);
                
                // 持续检查是否还有文件在被发现
                int lastFileCount = 0;
                int stableCount = 0;
                
                while (true)
                {
                    Thread.Sleep(1000); // 每秒检查一次
                    
                    int currentFileCount = fileCount;
                    if (currentFileCount == lastFileCount)
                    {
                        stableCount++;
                        // 如果文件数量连续3秒没有变化，且存储队列为空，认为索引完成
                        if (stableCount >= 3 && storageModule.PendingCount == 0)
                            break;
                    }
                    else
                    {
                        stableCount = 0;
                        lastFileCount = currentFileCount;
                    }
                }
            }, cts.Token);
            
            // 停止索引
            indexingModule.StopIndexing();
            
            Console.WriteLine($"文件发现完成，总共发现 {fileCount} 个文件");
            
            // 等待所有文件写入数据库
            Console.WriteLine("正在等待所有文件写入数据库...");
            while (storageModule.PendingCount > 0)
            {
                Console.WriteLine($"剩余队列：{storageModule.PendingCount}");
                await Task.Delay(1000);
            }
            
            sw.Stop();
            Console.WriteLine($"索引耗时：{sw.Elapsed}");
            
            return fileCount;
        }
    }
}