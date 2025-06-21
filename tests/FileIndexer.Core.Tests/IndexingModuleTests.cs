using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FileIndexer.Core;
using Xunit;

namespace FileIndexer.Core.Tests
{
    /// <summary>
    /// IIndexingModule 单元测试样例，采用 TDD 流程。
    /// </summary>
    public class IndexingModuleTests
    {
        /// <summary>
        /// 测试：索引模块能正确发现所有文件，并触发事件。
        /// </summary>
        [Fact]
        public void Should_Discover_All_Files()
        {
            int fileCount = 10;
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // 创建临时文件
                for (int i = 0; i < fileCount; i++)
                    File.WriteAllText(Path.Combine(tempDir, $"test_{i}.txt"), "abc");

                var discovered = new List<IFileMetadata>();
                using var module = new IndexingModule();
                module.FileDiscovered += (s, e) => discovered.Add(e.Metadata);

                module.StartIndexing(tempDir, maxDegreeOfParallelism: 4);

                // 等待最多3秒
                int wait = 0;
                while (discovered.Count < fileCount && wait++ < 30)
                    Thread.Sleep(100);

                Assert.Equal(fileCount, discovered.Count);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        /// <summary>
        /// 测试：取消令牌能中断索引操作。
        /// </summary>
        [Fact]
        public void Should_Cancel_Indexing()
        {
            int fileCount = 1000;
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                for (int i = 0; i < fileCount; i++)
                    File.WriteAllText(Path.Combine(tempDir, $"test_{i}.txt"), "abc");

                var discovered = new List<IFileMetadata>();
                using var module = new IndexingModule();
                module.FileDiscovered += (s, e) => discovered.Add(e.Metadata);

                using var cts = new CancellationTokenSource();
                module.StartIndexing(tempDir, 8, cts.Token);

                // 100ms 后取消
                Thread.Sleep(100);
                cts.Cancel();

                // 等待线程响应
                Thread.Sleep(300);

                Assert.True(discovered.Count < fileCount);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}