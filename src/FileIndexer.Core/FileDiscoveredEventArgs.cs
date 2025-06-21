using System;

namespace FileIndexer.Core
{
    /// <summary>
    /// FileDiscovered 事件参数，封装 <see cref="IFileMetadata"/>。
    /// </summary>
    public class FileDiscoveredEventArgs : EventArgs
    {
        /// <summary>
        /// 被发现并待存储的文件元数据。
        /// </summary>
        public IFileMetadata Metadata { get; }

        /// <summary>
        /// 构造函数，初始化事件参数。
        /// </summary>
        /// <param name="metadata">被发现的文件元数据。</param>
        public FileDiscoveredEventArgs(IFileMetadata metadata)
        {
            Metadata = metadata;
        }
    }
}