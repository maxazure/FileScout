using System;

namespace FileIndexer.Core
{
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
    /// 文件元数据实现。
    /// </summary>
    public class FileMetadata : IFileMetadata
    {
        /// <inheritdoc/>
        public string Name { get; }
        /// <inheritdoc/>
        public string FullPath { get; }
        /// <inheritdoc/>
        public long Size { get; }
        /// <inheritdoc/>
        public DateTime LastModifiedUtc { get; }

        /// <summary>
        /// 构造函数，初始化文件元数据。
        /// </summary>
        /// <param name="name">文件名。</param>
        /// <param name="fullPath">完整路径。</param>
        /// <param name="size">文件大小。</param>
        /// <param name="lastModifiedUtc">最后修改时间（UTC）。</param>
        public FileMetadata(string name, string fullPath, long size, DateTime lastModifiedUtc)
        {
            Name = name;
            FullPath = fullPath;
            Size = size;
            LastModifiedUtc = lastModifiedUtc;
        }
    }
}