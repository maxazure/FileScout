# FileScout

高性能文件索引与元数据持久化系统，基于 C# .NET 8 构建，支持多线程目录遍历、SQLite 存储和批量数据处理。

[![.NET 8](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/download)
[![SQLite](https://img.shields.io/badge/SQLite-3.x-green.svg)](https://www.sqlite.org/)
[![License](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

## ✨ 核心特性

- 🚀 **高性能扫描**：两阶段分离扫描，支持处理数百万文件
- 💾 **SQLite 存储**：自动创建数据库和表，支持批量插入和重复处理
- 🔄 **增量更新**：支持重复扫描，自动更新已变化的文件
- 🚫 **智能过滤**：自动忽略开发工具缓存目录（node_modules、.git、bin 等）
- ⚡ **批量处理**：可配置批量大小（默认1000条/批次），优化数据库性能
- 🛡️ **数据完整性**：使用事务确保数据一致性，路径唯一约束防止重复

## 🚀 快速开始

### 基本使用

```bash
# 扫描默认目录 D:\，保存到 fileindex.db
dotnet run --project ./src/FileIndexer.Console/FileIndexer.Console.csproj

# 扫描指定目录，保存到指定数据库
dotnet run --project ./src/FileIndexer.Console/FileIndexer.Console.csproj -- "C:\MyFiles" "myindex.db"

# 指定批量大小（默认1000）
dotnet run --project ./src/FileIndexer.Console/FileIndexer.Console.csproj -- "C:\MyFiles" "myindex.db" 500
```

### 命令行参数

| 参数 | 描述 | 默认值 |
|------|------|--------|
| `扫描路径` | 要扫描的根目录路径 | `D:\` |
| `数据库路径` | SQLite 数据库文件路径 | `fileindex.db` |
| `批量大小` | 每批插入的记录数 | `1000` |

## 📊 性能表现

- **扫描速度**：约 19万文件/秒
- **数据库写入**：批量插入，支持事务回滚
- **内存使用**：动态 GC 优化，低内存占用
- **并发控制**：根据目录深度自动调整并发度

## 🗃️ 数据库结构

程序自动创建 SQLite 数据库和表结构：

```sql
CREATE TABLE files (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT,                    -- 文件名
    path TEXT UNIQUE,            -- 文件完整路径（唯一约束）
    size INTEGER,                -- 文件大小（字节）
    mtime INTEGER                -- 修改时间（Unix时间戳）
);

-- 自动创建的索引
CREATE INDEX idx_name ON files(name);
CREATE INDEX idx_path ON files(path);
CREATE INDEX idx_size ON files(size);
```

## 🔍 数据库查询示例

```sql
-- 查看文件总数和总大小
SELECT COUNT(*) as total_files, SUM(size) as total_size FROM files;

-- 查找大文件（大于100MB）
SELECT name, ROUND(size/1024.0/1024.0, 2) as size_mb 
FROM files WHERE size > 104857600 ORDER BY size DESC;

-- 按文件类型统计
SELECT 
    CASE 
        WHEN name LIKE '%.cs' THEN 'C# Source'
        WHEN name LIKE '%.js' THEN 'JavaScript'
        WHEN name LIKE '%.py' THEN 'Python'
        ELSE 'Other'
    END as file_type,
    COUNT(*) as count,
    SUM(size) as total_size
FROM files GROUP BY file_type ORDER BY count DESC;

-- 最近修改的文件
SELECT name, datetime(mtime, 'unixepoch') as modified 
FROM files ORDER BY mtime DESC LIMIT 10;
```

## 🛠️ 项目结构

```
FileScout/
├── src/
│   ├── FileIndexer.Console/     # 控制台应用程序
│   ├── FileIndexer.Core/        # 核心索引模块
│   └── FileIndexer.Storage/     # SQLite存储模块
├── tests/
│   ├── FileIndexer.Core.Tests/     # 核心模块测试
│   └── FileIndexer.Storage.Tests/  # 存储模块测试
├── docs/
│   └── api.md                   # API 文档
└── generate-api-docs.ps1        # 文档生成脚本
```

## 🧪 开发和测试

### 构建项目

```bash
# 构建整个解决方案
dotnet build

# 构建特定项目
dotnet build src/FileIndexer.Core/FileIndexer.Core.csproj
dotnet build src/FileIndexer.Storage/FileIndexer.Storage.csproj
```

### 运行测试

```bash
# 运行所有测试
dotnet test

# 运行特定项目测试
dotnet test tests/FileIndexer.Core.Tests/
dotnet test tests/FileIndexer.Storage.Tests/
```

### 生成 API 文档

1. **安装 DocFX（如需要）**
   ```bash
   dotnet tool install -g docfx
   ```

2. **生成文档**
   ```bash
   ./generate-api-docs.ps1
   ```

3. **查看文档**
   - Markdown: [`docs/api.md`](docs/api.md)
   - HTML: `docs/api/index.html`

## ⚙️ 忽略的目录

程序自动忽略以下常见的开发工具和缓存目录：

- **Node.js**: `node_modules`
- **Python**: `venv`, `.venv`, `env`, `__pycache__`, `.pytest_cache`, `.mypy_cache`
- **版本控制**: `.git`, `.svn`, `.hg`
- **IDE**: `.idea`, `.vscode`
- **构建输出**: `dist`, `build`, `target`, `bin`, `obj`
- **系统文件**: `.DS_Store`

## 🔧 高级配置

### 批量大小建议

| 场景 | 推荐批量大小 | 说明 |
|------|-------------|------|
| 小文件系统 (< 10万文件) | 500-1000 | 快速处理，低内存 |
| 中型文件系统 (10万-100万) | 1000-2000 | 平衡性能和内存 |
| 大型文件系统 (> 100万) | 2000-5000 | 最大化吞吐量 |
| SSD 存储 | 2000-5000 | 利用高速I/O |
| 机械硬盘 | 500-1000 | 减少I/O负载 |

### SQLite 性能优化

程序自动应用以下 SQLite 优化：

- `PRAGMA synchronous = OFF` - 提高写入速度
- `PRAGMA journal_mode = WAL` - 启用 WAL 模式
- `PRAGMA cache_size = 25000` - 100MB 缓存
- `PRAGMA temp_store = MEMORY` - 内存临时存储

## 📈 使用场景

- **文件备份管理**：快速索引文件系统，追踪文件变化
- **重复文件检测**：基于文件大小和路径分析重复文件
- **磁盘使用分析**：按文件类型、目录统计磁盘使用
- **文件监控**：定期扫描，监控文件系统变化
- **数据迁移**：文件清单生成，辅助数据迁移计划

## 🤝 贡献指南

1. Fork 本仓库
2. 创建功能分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 创建 Pull Request

## 📄 许可证

本项目采用 MIT 许可证 - 查看 [LICENSE](LICENSE) 文件了解详细信息。

## 🙋 支持

如果您有任何问题或建议，请：

1. 查看 [API 文档](docs/api.md)
2. 查看现有的 [Issues](../../issues)
3. 创建新的 [Issue](../../issues/new)

---

**FileScout** - 让文件索引变得简单高效！ 🚀