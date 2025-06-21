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
        /// 测试：重复入队和异常处理。
        /// </summary>
        [Fact]
        public async Task Should_Handle_Duplicate_And_Exception()
        {
            string dbPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");

            try
            {
                using var storage = new StorageModule();
                storage.Initialize(dbPath);
                storage.Start();

                var meta = new FileMetadata("dup.txt", "/tmp/dup.txt", 1, DateTime.UtcNow);
                storage.Enqueue(meta);
                storage.Enqueue(meta); // 重复

                await storage.StopAsync();

                // 检查数据库
                using var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM files WHERE name='dup.txt';";
                long count = (long)cmd.ExecuteScalar();

                Assert.Equal(2, count);
            }
            finally
            {
                if (File.Exists(dbPath))
                    File.Delete(dbPath);
            }
        }
    }
}