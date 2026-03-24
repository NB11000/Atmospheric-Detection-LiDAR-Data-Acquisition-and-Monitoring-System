using System;
using System.IO.MemoryMappedFiles;

namespace SharedMemoryFramework
{
    /// <summary>
    /// 共享内存客户端（子进程）
    /// 负责连接共享内存
    /// </summary>
    public class SharedMemoryClient : IDisposable
    {
        // 共享内存名称
        const string MapName = "Global\\DAQ_SHARED_MEMORY";
        // 数据区大小 (256MB)
        const int DataSize = 256 * 1024 * 1024;

        /// <summary>
        /// 内存映射文件
        /// </summary>
        private MemoryMappedFile mmf;

        /// <summary>
        /// 数据区访问器
        /// </summary>
        public MemoryMappedViewAccessor DataAccessor { get; private set; }

        /// <summary>
        /// 连接共享内存
        /// </summary>
        public void Connect()
        {
            mmf = MemoryMappedFile.OpenExisting(
                MapName,
                MemoryMappedFileRights.ReadWrite);

            // 数据区
            DataAccessor = mmf.CreateViewAccessor(
                0,
                DataSize,
                MemoryMappedFileAccess.ReadWrite);
        }

        /// <summary>
        /// 写入数据区
        /// </summary>
        public void WriteData(int offset, byte[] data)
        {
            DataAccessor.WriteArray(offset, data, 0, data.Length);
        }

        /// <summary>
        /// 读取数据区
        /// </summary>
        public void ReadData(int offset, byte[] buffer)
        {
            DataAccessor.ReadArray(offset, buffer, 0, buffer.Length);
        }

        public void Dispose()
        {
            DataAccessor?.Dispose();
            mmf?.Dispose();
        }
    }
}