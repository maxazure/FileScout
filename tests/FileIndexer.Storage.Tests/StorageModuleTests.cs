using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FileIndexer.Core;
using FileIndexer.Storage;
using Xunit;

namespace FileIndexer.Storage.Tests
{
    /// <summary>
    /// IStorageModule 单元测试样例，采用 TDD 流程。
    /// </summary>
    public class StorageModuleTests
    {
        /// <summary>
        /// 测试：入队 N 条记录后，数据库应有 N 行，PendingCount 为 0。
        /// </summary>
        [Fact]
        public async Task Should_Store_All_Metadata()
        {
            int n = 5;
            string dbPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");

            try
            {
                using var storage = new StorageModule();
                storage.Initialize(dbPath);
                storage.Start();

                for (int i = 0; i < n; i++)
                {
                    var meta = new FileMetadata($"f{i}.txt", $"/tmp/f{i}.txt", 123, DateTime.UtcNow);
                    storage.Enqueue(meta);
                }

                await storage.StopAsync();

                // 检查数据库
                using var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM files;";
                long count = (long)cmd.ExecuteScalar();

                Assert.Equal(n, count);
                Assert.Equal(0, storage.PendingCount);
            }
            finally
            {
                if (File.Exists(dbPath))
                    File.Delete(dbPath);
            }
        }

        /// <summary>
        /// 测试：重复入队处理（INSERT OR REPLACE行为）。
        /// </summary>
        [Fact]
        public async Task Should_Handle_Duplicate_Paths_With_Replace()
        {
            string dbPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");

            try
            {
                using var storage = new StorageModule();
                storage.Initialize(dbPath);
                storage.Start();

                // 插入相同路径的文件，第二次应该替换第一次
                var meta1 = new FileMetadata("dup.txt", "/tmp/dup.txt", 100, DateTime.UtcNow.AddDays(-1));
                var meta2 = new FileMetadata("dup.txt", "/tmp/dup.txt", 200, DateTime.UtcNow); // 相同路径，不同大小和时间

                storage.Enqueue(meta1);
                storage.Enqueue(meta2); // 应该替换第一条记录

                await storage.StopAsync();

                // 检查数据库：应该只有一条记录，且是最新的数据
                using var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;");
                conn.Open();
                using var cmd = conn.CreateCommand();
                
                // 验证只有一条记录
                cmd.CommandText = "SELECT COUNT(*) FROM files WHERE path='/tmp/dup.txt';";
                long count = (long)cmd.ExecuteScalar();
                Assert.Equal(1, count);
                
                // 验证记录是最新的（大小为200）
                cmd.CommandText = "SELECT size FROM files WHERE path='/tmp/dup.txt';";
                long size = (long)cmd.ExecuteScalar();
                Assert.Equal(200, size);
            }
            finally
            {
                if (File.Exists(dbPath))
                    File.Delete(dbPath);
            }
        }
    }
}