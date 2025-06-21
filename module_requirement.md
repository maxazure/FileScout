You are a senior C# architect. 请帮我创建两个基于 .NET 的模块：  
1. 索引模块（IIndexingModule）  
2. 持久化存储模块（IStorageModule）  

### 一、总体要求
1. 使用接口驱动设计，接口定义如下，并为每个接口、方法、事件和数据模型**编写完整的 XML 注释**，以便生成 API 文档：  
   ```csharp
   /// <summary>
   /// 文件元数据接口，描述一个被索引文件的关键信息。
   /// </summary>
   public interface IFileMetadata
   {
       /// <summary>文件名（不包含路径）。</summary>
       string Name { get; }
       /// <summary>文件完整路径。</summary>
       string FullPath { get; }
       /// <summary>文件大小（字节）。</summary>
       long Size { get; }
       /// <summary>文件最后修改时间（UTC）。</summary>
       DateTime LastModifiedUtc { get; }
   }

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
       /// 请求停止当前索引操作，并释放相关资源。
       /// </summary>
       void StopIndexing();
   }

   /// <summary>
   /// FileDiscovered 事件参数，封装 <see cref="IFileMetadata"/>。
   /// </summary>
   public class FileDiscoveredEventArgs : EventArgs
   {
       /// <summary>被发现并待存储的文件元数据。</summary>
       public IFileMetadata Metadata { get; }
       public FileDiscoveredEventArgs(IFileMetadata metadata) => Metadata = metadata;
   }

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
       /// 启动后台写入线程，开始消费队列。</summary>
       void Start();

       /// <summary>
       /// 将一个文件元数据入队，由后台线程顺序写入数据库。</summary>
       /// <param name="fileMetadata">待写入的文件元数据。</param>
       void Enqueue(IFileMetadata fileMetadata);

       /// <summary>
       /// 通知后台线程处理完当前队列并优雅退出，返回完成任务的异步 <see cref="Task"/>。</summary>
       Task StopAsync();

       /// <summary>
       /// 当前待处理队列长度，可用于进度监控和测试断言。</summary>
       int PendingCount { get; }
   }
````

2. **实现要求**

   * 索引模块内部用 `Directory.EnumerateFiles` + `Parallel.ForEach` 或 `Task` 池并发遍历，线程数由 `maxDegreeOfParallelism` 决定。
   * 存储模块内部用 `BlockingCollection<IFileMetadata>`（或其他线程安全队列）缓存数据，单线程消费并批量插入 SQLite。
   * 初始化时在 SQLite 中执行下列建表脚本（XML 注释请写在代码前）：

     ```sql
     CREATE TABLE IF NOT EXISTS files (
         id INTEGER PRIMARY KEY AUTOINCREMENT,
         name TEXT,
         path TEXT,
         size INTEGER,
         mtime INTEGER
     );
     CREATE INDEX IF NOT EXISTS idx_name ON files(name);
     ```
   * 确保所有接口、类和方法都附带**XML 文档注释**，并提供一个 `docs/api.md`（或使用 DocFX/Swagger 风格）的**自动化生成脚本**示例。

3. **TDD 与单元测试**

   * 项目架构：

     ```
     /src
       /FileIndexer.Core
       /FileIndexer.Storage
     /tests
       /FileIndexer.Core.Tests
       /FileIndexer.Storage.Tests
     ```
   * 在测试项目中，**先编写失败的单元测试**：

     * 对 `IIndexingModule`：模拟临时目录，创建 N 个文件，断言 `FileDiscovered` 事件被触发 N 次；测试不同并行度、取消令牌能否停止。
     * 对 `IStorageModule`：在内存 SQLite 或临时文件中，`Enqueue` N 条记录后调用 `Start`→`StopAsync`，断言数据库中 `files` 表行数为 N 且 `PendingCount` 最终为 0；测试重复入队行为及异常处理。
   * 然后**逐步实现**代码，使测试由红转绿，最后进行重构（重构阶段保证测试全部通过）。

4. **生成 API 文档**

   * 在项目根添加 `generate-api-docs.ps1`（或 .sh）脚本，调用 `dotnet xml-doc` + DocFX 或 `Sandcastle Help File Builder` 将 XML 注释转换为 `docs/api.html` 或 `docs/api.md`。
   * 示例脚本内容及生成说明请在 `README.md` 中一并给出。


请根据以上完整提示，生成：

1. 两个模块的 C# 接口及实现代码（含 XML 注释）。
2. 单元测试项目及测试用例样例，采用 xUnit 或 NUnit 并遵循 TDD 流程。
3. API 文档生成脚本示例及 `docs/api.md` 模板。
