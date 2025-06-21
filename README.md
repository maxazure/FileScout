# FileScout

## 项目简介

FileScout 提供高性能的文件索引与元数据持久化能力，基于接口驱动设计，支持多线程目录遍历与 SQLite 存储，便于扩展与集成。

---

## 目录结构

```
/src
  /FileIndexer.Core         # 索引接口与实现
  /FileIndexer.Storage      # 持久化接口与实现
/tests
  /FileIndexer.Core.Tests   # 索引模块单元测试
  /FileIndexer.Storage.Tests# 存储模块单元测试
/docs
  api.md                    # API 文档入口
generate-api-docs.ps1       # API 文档自动生成脚本
```

---

## 单元测试

- 使用 xUnit，测试用例见 `tests/` 目录。
- 运行所有测试：
  ```sh
  dotnet test
  ```

---

## API 文档自动生成

1. **确保已安装 [DocFX](https://dotnet.github.io/docfx/)**
   ```sh
   dotnet tool install -g docfx
   ```

2. **生成 XML 注释与 API 文档**
   ```sh
   ./generate-api-docs.ps1
   ```

3. **查看文档**
   - Markdown: [`docs/api.md`](docs/api.md)
   - HTML: `docs/api/index.html`

---

## 说明

- 所有接口、方法、事件、数据模型均带有完整 XML 注释，便于自动生成 API 文档。
- 支持 TDD 流程，先写测试再实现，保证高可维护性。