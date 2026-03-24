using System;
using System.IO.MemoryMappedFiles;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace SharedMemoryFramework
{
    /// 内存结构：
    /// ┌───────────────────────┐
    /// │ UIMemoryHeader        │RawMemoryHeader      │
    /// ├───────────────────────┤
    /// │ Channel0 DataBuffer   │Channel0 DataBuffer  │
    /// │ Channel1 DataBuffer   │Channel1 DataBuffer  │
    /// │ ...                   │ ...                 │
    /// └───────────────────────┘




    /// <summary>
    /// RAW数据共享内存 RingBuffer
    /// 
    /// 功能：
    /// 1. 存储全量采样数据
    /// 2. 子进程写入
    /// 3. 主进程读取
    /// 
    /// 特点：
    /// - 无锁设计
    /// - 无GC
    /// - 支持MHz级采样率
    /// </summary>
    public unsafe class RawRingBuffer : IDisposable
    {
        /// <summary>
        /// 共享内存名称
        /// Global 表示所有进程可访问
        /// </summary>
        const string MapName = "Global\\DAQ_RAW_BUFFER";

        /// <summary>
        /// 内存映射文件对象
        /// </summary>
        private static MemoryMappedFile mmf;

        /// <summary>
        /// 内存访问器
        /// </summary>
        private static MemoryMappedViewAccessor accessor;

        /// 共享内存基地址指针
        private static byte* basePtr;
        /// Header结构指针
        private static RawMemoryHeader* header;
        /// 数据区指针
        /// 指向double数组
        private static double* dataPtr;
        /// 通道数量
        private static int channelCount;
        /// 每个通道缓冲区大小
        private static int bufferLength;

        /// <summary>
        /// 主进程创建共享内存
        /// 只有主进程调用
        /// 子进程只负责连接
        /// </summary>
        public static void Create(int channel, int buffer, int sampleRate)
        {
            channelCount = channel;
            bufferLength = buffer;

            // Header结构大小
            int headerSize = sizeof(RawMemoryHeader);

            // 数据区大小
            long dataSize = (long)channel * buffer * sizeof(double);

            // 共享内存总大小
            long totalSize = headerSize + dataSize;

            // 创建或打开共享内存
            mmf = MemoryMappedFile.CreateOrOpen(
                MapName,
                totalSize,
                MemoryMappedFileAccess.ReadWrite);

            // 创建访问器
            accessor = mmf.CreateViewAccessor();

            // 获取共享内存指针
            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);

            // Header地址
            header = (RawMemoryHeader*)basePtr;

            // 数据区地址
            dataPtr = (double*)(basePtr + headerSize);

            // 初始化Header
            header->ChannelCount = channel;
            header->BufferLength = buffer;
            header->SampleRate = sampleRate;
            header->WriteIndex = 0;
        }

        /// <summary>
        /// 子进程连接共享内存
        /// </summary>
        public static void Open()
        {
            // 打开已存在的共享内存
            mmf = MemoryMappedFile.OpenExisting(
                MapName,
                MemoryMappedFileRights.ReadWrite);

            accessor = mmf.CreateViewAccessor();

            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);

            header = (RawMemoryHeader*)basePtr;

            dataPtr = (double*)(basePtr + sizeof(RawMemoryHeader));

            // 读取共享参数
            channelCount = header->ChannelCount;
            bufferLength = header->BufferLength;
        }

        /// <summary>
        /// 写入采样数据
        /// 
        /// 该方法由子进程调用
        /// </summary>
        public static void Write(double[] samples)
        {
            long index = header->WriteIndex;

            for (int i = 0; i < samples.Length; i++)
            {
                // 计算环形缓冲区位置
                long pos = (index + i) % bufferLength;

                // 写入数据
                dataPtr[pos] = samples[i];
            }

            // 更新写入位置
            header->WriteIndex = (index + samples.Length) % bufferLength;
        }

        /// <summary>
        /// 主进程读取最新数据
        /// </summary>
        public static void ReadLatest(double[] buffer, int length)
        {
            long index = header->WriteIndex;

            long start = index - length;

            if (start < 0)
                start += bufferLength;

            for (int i = 0; i < length; i++)
            {
                long pos = (start + i) % bufferLength;

                buffer[i] = dataPtr[pos];
            }
        }

        /// <summary>
        /// 释放共享内存资源
        /// </summary>
        public void Dispose()
        {
            accessor?.Dispose();
            mmf?.Dispose();
        }
    }

    /// <summary>
    /// UI共享内存
    /// 
    /// 用于传递降采样后的波形数据
    /// </summary>
    public unsafe class UISharedBuffer : IDisposable
    {
        const string MapName = "Global\\DAQ_UI_BUFFER";

        static MemoryMappedFile mmf;

        static MemoryMappedViewAccessor accessor;

        static byte* basePtr;

        static UIMemoryHeader* header;

        static double* minPtr;

        static double* maxPtr;

        static int pixelCount;

        /// <summary>
        /// 主进程创建UI共享内存
        /// </summary>
        public static void Create(int pixels)
        {
            pixelCount = pixels;

            int headerSize = sizeof(UIMemoryHeader);

            long dataSize = pixels * sizeof(double) * 2;

            long totalSize = headerSize + dataSize;

            mmf = MemoryMappedFile.CreateNew(
                MapName,
                totalSize,
                MemoryMappedFileAccess.ReadWrite);

            accessor = mmf.CreateViewAccessor();

            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);

            header = (UIMemoryHeader*)basePtr;

            minPtr = (double*)(basePtr + headerSize);

            maxPtr = minPtr + pixels;

            header->PixelCount = pixels;
        }

        /// <summary>
        /// 子进程连接共享内存
        /// </summary>
        public static void Open()
        {
            mmf = MemoryMappedFile.OpenExisting(
                MapName,
                MemoryMappedFileRights.ReadWrite);

            accessor = mmf.CreateViewAccessor();

            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);

            header = (UIMemoryHeader*)basePtr;

            pixelCount = header->PixelCount;

            minPtr = (double*)(basePtr + sizeof(UIMemoryHeader));

            maxPtr = minPtr + pixelCount;
        }

        /// <summary>
        /// 写入UI波形数据
        /// </summary>
        public static void Write(double[] minBuffer, double[] maxBuffer)
        {
            for (int i = 0; i < pixelCount; i++)
            {
                minPtr[i] = minBuffer[i];

                maxPtr[i] = maxBuffer[i];
            }

            // 更新帧号
            header->FrameIndex++;
        }

        public void Dispose()
        {
            accessor?.Dispose();
            mmf?.Dispose();
        }
    }


    /// <summary>
    /// UI共享内存头结构
    /// 
    /// 用于UI波形刷新
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UIMemoryHeader
    {
        /// <summary>
        /// UI帧号
        /// 
        /// 每刷新一次UI递增
        /// 用于检测数据是否更新
        /// </summary>
        public long FrameIndex;

        /// <summary>
        /// UI像素数量
        /// 
        /// 通常等于图表宽度
        /// </summary>
        public int PixelCount;
    }


    /// <summary>
    /// RAW数据共享内存头结构
    /// 
    /// 该结构位于共享内存最前面
    /// 用于描述当前共享内存状态
    /// 
    /// 主进程和子进程都会读取此结构
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RawMemoryHeader
    {
        /// <summary>
        /// 当前写入位置（环形缓冲区写指针）
        /// 
        /// 子进程不断更新该值
        /// 主进程通过该值确定最新数据位置
        /// </summary>
        public long WriteIndex;

        /// <summary>
        /// 采集通道数量
        /// 
        /// 例如：
        /// 1 = 单通道
        /// 2 = 双通道
        /// 8 = 八通道
        /// </summary>
        public int ChannelCount;

        /// <summary>
        /// 每个通道的缓冲区长度
        /// 
        /// 表示每个通道最多缓存多少个采样点
        /// </summary>
        public int BufferLength;

        /// <summary>
        /// 当前采样率
        /// 
        /// 例如：
        /// 1000000 = 1MHz
        /// </summary>
        public int SampleRate;
    }


}