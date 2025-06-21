using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FileIndexer.ConsoleApp
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("开始索引...");

            var sw = Stopwatch.StartNew();
            var fileQueue = new BlockingCollection<string>(boundedCapacity: 20000);
            int fileCount = 0;
            int firstFilePrinted = 0; // 0:未打印, 1:已打印
            int consumerCount = Environment.ProcessorCount;
            var cts = new CancellationTokenSource();

            var batchSw = Stopwatch.StartNew();
            object lockObj = new object();

            // 消费者线程
            Task[] consumers = new Task[consumerCount];
            for (int i = 0; i < consumerCount; i++)
            {
                consumers[i] = Task.Run(() =>
                {
                    foreach (var file in fileQueue.GetConsumingEnumerable())
                    {
                        int count = Interlocked.Increment(ref fileCount);
                        if (Interlocked.CompareExchange(ref firstFilePrinted, 1, 0) == 0)
                        {
                            Console.WriteLine($"首个文件：{file}");
                        }
                        if (count % 100000 == 0)
                        {
                            TimeSpan batchTime;
                            lock (lockObj)
                            {
                                batchTime = batchSw.Elapsed;
                                batchSw.Restart();
                            }
                            Console.WriteLine($"已发现 {count} 个文件... 本批耗时：{batchTime}");
                        }
                    }
                }, cts.Token);
            }

            // 生产者线程（多线程遍历子目录）
            var producer = Task.Run(() =>
            {
                try
                {
                    ParallelTraverseDirectory(@"D:\", fileQueue, cts.Token, maxDegreeOfParallelism: Math.Max(2, Environment.ProcessorCount / 2));
                }
                finally
                {
                    fileQueue.CompleteAdding();
                }
            }, cts.Token);

            Task.WaitAll(consumers);
            sw.Stop();

            Console.WriteLine($"索引完成，共发现 {fileCount} 个文件。");
            Console.WriteLine($"总耗时：{sw.Elapsed}");
        }

        // 多线程并行遍历目录，提升生产速度
        static void ParallelTraverseDirectory(string root, BlockingCollection<string> queue, CancellationToken token, int maxDegreeOfParallelism)
        {
            var dirStack = new ConcurrentStack<string>();
            dirStack.Push(root);

            Parallel.ForEach(
                Partitioner.Create(dirStack),
                new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = maxDegreeOfParallelism },
                () => new Stack<string>(),
                (dir, state, localStack) =>
                {
                    localStack.Push(dir);
                    while (localStack.Count > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        var current = localStack.Pop();
                        try
                        {
                            foreach (var file in Directory.EnumerateFiles(current))
                            {
                                queue.Add(file, token);
                            }
                        }
                        catch { /* 忽略文件枚举异常 */ }

                        try
                        {
                            foreach (var subdir in Directory.EnumerateDirectories(current))
                            {
                                localStack.Push(subdir);
                            }
                        }
                        catch { /* 忽略目录枚举异常 */ }
                    }
                    return localStack;
                },
                _ => { }
            );
        }
    }
}