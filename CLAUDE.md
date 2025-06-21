# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

FileScout is a high-performance file indexing and metadata persistence system built in C# with .NET 8. It features a complete console application that scans directories, indexes files, and stores metadata in SQLite databases with advanced performance optimizations.

## Architecture

The solution follows a modular architecture with three main components:

- **FileIndexer.Console**: Complete console application for file indexing
  - High-performance two-phase directory scanning (structure collection + file processing)
  - Configurable batch processing for database operations
  - Intelligent directory filtering (ignores node_modules, .git, bin, obj, etc.)
  - Command-line interface with customizable scan paths, database paths, and batch sizes
  - Real-time progress monitoring and performance statistics

- **FileIndexer.Core**: Core indexing interfaces and implementations
  - `IIndexingModule`: Interface for directory indexing with event-driven file discovery
  - `IndexingModule`: Implementation using Parallel.ForEach with configurable thread count
  - `IFileMetadata`: File metadata interface with name, path, size, and modification time
  - `FileDiscoveredEventArgs`: Event args for file discovery notifications

- **FileIndexer.Storage**: Advanced SQLite persistence with batch processing
  - `IStorageModule`: Interface for queued file metadata storage with batch operations
  - `StorageModule`: Implementation using BlockingCollection with batch inserts and transactions
  - Automatic database and table creation with proper error handling
  - SQLite performance optimizations (WAL mode, increased cache, memory temp storage)
  - Support for INSERT OR REPLACE to handle duplicate paths gracefully
  - Configurable batch sizes (default 1000 records per transaction)

## Development Commands

### Build and Test
```bash
# Build the entire solution
dotnet build

# Run all unit tests
dotnet test

# Build specific project
dotnet build src/FileIndexer.Console/FileIndexer.Console.csproj
dotnet build src/FileIndexer.Core/FileIndexer.Core.csproj
dotnet build src/FileIndexer.Storage/FileIndexer.Storage.csproj

# Run tests for specific project
dotnet test tests/FileIndexer.Core.Tests/
dotnet test tests/FileIndexer.Storage.Tests/
```

### Console Application Usage
```bash
# Basic usage - scan D:\ to fileindex.db with 1000 batch size
dotnet run --project ./src/FileIndexer.Console/FileIndexer.Console.csproj

# Custom scan path and database
dotnet run --project ./src/FileIndexer.Console/FileIndexer.Console.csproj -- "C:\MyFiles" "myindex.db"

# Custom batch size for performance tuning
dotnet run --project ./src/FileIndexer.Console/FileIndexer.Console.csproj -- "C:\MyFiles" "myindex.db" 500

# Create database in subdirectory (auto-creates directory)
dotnet run --project ./src/FileIndexer.Console/FileIndexer.Console.csproj -- "C:\MyFiles" "data/backups/index.db" 2000
```

### Database Analysis with SQLite
```bash
# Query file count and total size
sqlite3 myindex.db "SELECT COUNT(*), SUM(size) FROM files;"

# View table structure
sqlite3 myindex.db ".schema files"

# Find large files
sqlite3 myindex.db "SELECT name, size FROM files WHERE size > 1000000 ORDER BY size DESC LIMIT 10;"

# File type analysis
sqlite3 myindex.db "SELECT substr(name, -4) as ext, COUNT(*) FROM files GROUP BY ext ORDER BY COUNT(*) DESC;"
```

### API Documentation Generation
```bash
# Generate XML documentation and API docs (requires DocFX)
./generate-api-docs.ps1

# Install DocFX if not available
dotnet tool install -g docfx
```

## Testing Patterns

The project uses xUnit with TDD approach:

- **Core Tests** (`IndexingModuleTests.cs`):
  - File discovery verification with temporary directories
  - Cancellation token testing for stopping indexing operations
  - Parallel processing validation

- **Storage Tests** (`StorageModuleTests.cs`):
  - Database persistence validation using temporary SQLite files
  - Queue processing verification (PendingCount assertions)
  - Batch processing and transaction testing
  - Duplicate handling and exception scenarios

## Key Implementation Details

### Console Application (Two-Phase Scanning)
- **Phase 1**: Multi-threaded directory structure collection with depth-based parallelism control
- **Phase 2**: Parallel file scanning with batch processing to storage module
- Dynamic concurrency adjustment based on directory depth (8 threads → 4 threads → 1 thread)
- Automatic memory management with periodic GC optimization
- Directory filtering to skip common development/cache directories

### Storage Module (Batch Processing)
- Configurable batch sizes (default 1000 records per transaction)
- Uses `INSERT OR REPLACE` to handle duplicate file paths gracefully
- Automatic SQLite performance optimizations applied on initialization
- Creates indexes after data insertion for optimal performance
- Thread-safe queuing with `BlockingCollection<IFileMetadata>`

### Ignored Directories
The application automatically skips these directory patterns:
- Development: `node_modules`, `venv`, `.venv`, `env`, `__pycache__`
- Version Control: `.git`, `.svn`, `.hg`
- IDE: `.idea`, `.vscode`
- Build Output: `dist`, `build`, `target`, `bin`, `obj`
- Cache: `.pytest_cache`, `.mypy_cache`, `.DS_Store`

### Database Schema
```sql
CREATE TABLE files (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT,                    -- File name only
    path TEXT UNIQUE,            -- Full file path (unique constraint)
    size INTEGER,                -- File size in bytes
    mtime INTEGER                -- Last modified time (Unix timestamp)
);

-- Indexes for performance
CREATE INDEX idx_name ON files(name);
CREATE INDEX idx_path ON files(path);
CREATE INDEX idx_size ON files(size);
```

### Performance Characteristics
- **Scan Rate**: ~190,000 files/second
- **Batch Processing**: 500-5000 records per database transaction
- **Memory Usage**: Dynamic with GC optimization, typically <100MB
- **Concurrency**: Auto-adjusting based on directory depth and system capabilities

## Dependencies
- System.Data.SQLite for database operations
- System.Collections.Concurrent for thread-safe collections
- xUnit for unit testing
- DocFX for API documentation generation

## File Naming Conventions
- Interfaces prefixed with `I` (e.g., `IIndexingModule`)
- Implementation classes match interface names without `I` prefix
- Test classes suffixed with `Tests`
- All classes include comprehensive XML documentation comments

## Important Notes for Development
- Always test batch processing with different batch sizes for performance optimization
- Use `INSERT OR REPLACE` for handling duplicate file paths in incremental scans
- Monitor SQLite WAL file growth during large operations
- Consider batch size recommendations based on storage type (SSD vs HDD)
- Ensure proper disposal of database connections and collections