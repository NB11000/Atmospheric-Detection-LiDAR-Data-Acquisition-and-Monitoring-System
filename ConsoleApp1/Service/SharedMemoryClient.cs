using System;
using System.IO.MemoryMappedFiles;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using static SharedMemoryFramework.UIRingHeader;

namespace SharedMemoryFramework
{

    ///这是一个基于 C# 内存映射文件（MMF）实现的、无锁、无 GC、高性能的双区共享内存框架，
    ///专门用于多进程之间高速传输 DAQ（数据采集）原始采样数据与 UI 降采样波形数据。
    /// <summary>
    /// 共享内存整体布局说明
    /// 内存结构：
    /// ┌────────────
    /// │ 内存头结构(Header)    │  固定元数据区
    /// ├────────────
    /// │ 通道0数据缓冲区       │  实际采样数据存储区
    /// │ 通道1数据缓冲区       │
    /// │ ...                   │
    /// └────────────
    /// </summary>



    /// <summary>
    /// UI数据共享内存类
    /// 作用：传递**降采样后的波形数据**（双通道）
    /// 生产者：子进程 1MHz 批量写入1000个抽样点
    /// 消费者：主进程 33ms 读取一帧波形
    /// 满足 BufferLength > PixelCount → 永无覆盖、永无冲突
    /// </summary>
    public unsafe class UISharedBuffer : IDisposable
    {
        // 共享内存名称（无管理员权限）
        const string MapName = "DAQ_UI_RING_BUFFER";

        private MemoryMappedFile mmf;
        private MemoryMappedViewAccessor accessor;
        private byte* basePtr;
        private UIRingHeader* header;

        // 沿用你的原始内存布局：双通道缓冲区
        private double* Buffer1;
        private double* Buffer2;

        private int pixelCount;

        /// <summary>
        /// 【主进程】创建共享内存
        /// </summary>
        /// <param name="pixels">UI一帧像素数</param>
        public void Create(int pixels)
        {
            pixelCount = pixels;
            int headerSize = sizeof(UIRingHeader);

            // ===================== 精准定制安全容量 =====================
            const int SAFE_CAPACITY = 30000;

            // 总数据大小：UI共享内存头 + 通道1缓冲区 + 通道2缓冲区
            long dataSize = (SAFE_CAPACITY * sizeof(double)) * 2;
            long totalSize = headerSize + dataSize;

            // 创建共享内存
            mmf = MemoryMappedFile.CreateNew(MapName, totalSize, MemoryMappedFileAccess.ReadWrite);
            accessor = mmf.CreateViewAccessor();
            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);

            // 初始化头结构
            header = (UIRingHeader*)basePtr;
            header->PixelCount = pixels;
            header->BufferLength = SAFE_CAPACITY;
            header->WriteIndex = 0;

            // 指针映射（和你旧代码完全一致）
            Buffer1 = (double*)(basePtr + headerSize);
            Buffer2 = Buffer1 + SAFE_CAPACITY;
        }

        /// <summary>
        /// 【子进程】连接共享内存
        /// </summary>
        public void Open()
        {
            mmf = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.ReadWrite);
            accessor = mmf.CreateViewAccessor();
            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);

            // 从头结构读取信息
            header = (UIRingHeader*)basePtr;
            pixelCount = header->PixelCount;

            Buffer1 = (double*)(basePtr + sizeof(UIRingHeader));
            Buffer2 = Buffer1 + header->BufferLength;
        }

        // ===================== 核心：批量写入1000个抽样点 =====================
        /// <summary>
        /// 子进程调用：写入降采样后的1000个点
        /// </summary>
        /// <param name="buffer1"></param>
        /// <param name="buffer2"></param>
        /// <param name="batchSize"></param>
        public void WriteSampleBatch(double[] buffer1, double[] buffer2,int batchSize)
        {

            long currentIdx = header->WriteIndex;
            // 批量写入所有抽样点
            for (int i = 0; i < batchSize; i++)
            {
                long idx = currentIdx + i;
                int pos = (int)(idx % header->BufferLength);

                Buffer1[pos] = buffer1[i];
                Buffer2[pos] = buffer2[i];
            }

            // 原子更新写指针（无锁安全）
            Interlocked.MemoryBarrier(); // 确保写入在更新索引前对其他线程可见
            Interlocked.Add(ref header->WriteIndex, batchSize);
        }

        // ===================== 主进程：33ms读取最新一帧 =====================
        /// <summary>
        /// 栈上读取，无GC，无冲突
        /// </summary>
        public void ReadLatestFrame(double* buffer1, double* buffer2)
        {
            int frameSize = header->PixelCount;
            long writeIdx = header->WriteIndex;
            // 从最新位置往前读一帧
            long startIdx = writeIdx - frameSize;

            for (int i = 0; i < frameSize; i++)
            {
                int pos = (int)((startIdx + i) % header->BufferLength);
                buffer1[i] = Buffer1[pos];
                buffer2[i] = Buffer2[pos];
            }
        }

        /// <summary>
        /// 清空环形缓冲区（使用循环方式，兼容性更好）
        /// </summary>
        /// <param name="resetWriteIndex">是否重置写索引</param>
        public void ClearBufferSafe(bool resetWriteIndex = true)
        {
            if (header == null)
                throw new InvalidOperationException("Shared memory not initialized");

            int bufferLength = header->BufferLength;

            // 清空缓冲区1
            for (int i = 0; i < bufferLength; i++)
            {
                Buffer1[i] = 0.0;
            }

            // 清空缓冲区2
            for (int i = 0; i < bufferLength; i++)
            {
                Buffer2[i] = 0.0;
            }

            // 重置写索引
            if (resetWriteIndex)
            {
                header->WriteIndex = 0;
            }
        }

        public void Dispose()
        {
            accessor?.Dispose();
            mmf?.Dispose();
        }
    }

    /// <summary>
    /// 原始采样数据环形共享缓冲区
    /// 作用：用于**子进程写入高频采样数据**、**主进程读取原始数据**
    /// 核心特性：无锁设计、无GC垃圾回收、支持MHz级别高频率采样
    /// </summary>
    public unsafe class RawRingBuffer : IDisposable
    {
        /// <summary>
        /// 共享内存唯一名称
        /// 前缀 Global\\ 表示：系统全局共享，所有用户进程均可访问
        /// </summary>
        const string MapName = "DAQ_RAW_BUFFER";

        // 内存映射文件对象
        private MemoryMappedFile mmf;

        // 共享内存视图访问器
        private MemoryMappedViewAccessor accessor;

        // 共享内存起始基地址指针
        private byte* basePtr;
        // 共享内存头结构指针（存储配置与状态）
        private RawMemoryHeader* header;
        // 采样数据区起始指针（存储 double 类型采样值）
        private double* dataPtr;
        // 采集通道总数
        private int channelCount;
        // 单个通道环形缓冲区最大容量（采样点数量）
        private int bufferLength;

        /// <summary>
        /// 【主进程调用】创建并初始化共享内存
        /// 作用：分配物理内存、初始化头结构、建立指针映射
        /// 子进程不可调用，只能连接已创建的内存
        /// </summary>
        /// <param name="channel">通道数量</param>
        /// <param name="buffer">单通道缓冲区长度</param>
        /// <param name="sampleRate">采样率(Hz)</param>
        public void Create(int channel, int buffer, int sampleRate)
        {
            channelCount = channel;
            bufferLength = buffer;

            // 计算头结构占用字节数
            int headerSize = sizeof(RawMemoryHeader);

            // 计算数据区总大小 = 通道数 × 单通道长度 × 单个double大小
            long dataSize = (long)channel * buffer * sizeof(double);

            // 共享内存总大小 = 头 + 数据区
            long totalSize = headerSize + dataSize;

            // 创建/打开全局共享内存
            mmf = MemoryMappedFile.CreateNew(
                MapName,
                totalSize,
                MemoryMappedFileAccess.ReadWrite);

            // 创建可读写的内存视图
            accessor = mmf.CreateViewAccessor();

            // 获取共享内存原生指针（高性能访问必须）
            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);

            // 头指针 = 基地址
            header = (RawMemoryHeader*)basePtr;

            // 数据指针 = 基地址 + 头结构偏移量
            dataPtr = (double*)(basePtr + headerSize);

            // 初始化共享内存头信息
            header->ChannelCount = channel;
            header->BufferLength = buffer;
            header->SampleRate = sampleRate;
            header->WriteIndex = 0;
        }

        /// <summary>
        /// 【子进程调用】连接主进程创建好的共享内存
        /// 作用：获取指针映射、读取配置参数，不创建新内存
        /// </summary>
        public void Open()
        {
            // 打开已存在的全局共享内存
            mmf = MemoryMappedFile.OpenExisting(
                MapName,
                MemoryMappedFileRights.ReadWrite);

            accessor = mmf.CreateViewAccessor();

            // 映射到进程地址空间
            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);

            // 解析头结构与数据区
            header = (RawMemoryHeader*)basePtr;
            dataPtr = (double*)(basePtr + sizeof(RawMemoryHeader));

            // 从共享内存读取配置（由主进程写入）
            channelCount = header->ChannelCount;
            bufferLength = header->BufferLength;
        }

        /// <summary>
        /// 【子进程调用】向环形缓冲区写入采样数据
        /// 无锁、高性能、直接指针写入
        /// </summary>
        /// <param name="samples">待写入的采样数据数组</param>
        public void Write(double[] samples)
        {
            // 获取当前写指针位置
            long index = header->WriteIndex;

            // 循环写入所有采样点
            for (int i = 0; i < samples.Length; i++)
            {
                // 环形计算：确保位置不越界，循环复用缓冲区
                long pos = (index + i) % bufferLength;
                // 直接指针赋值（无GC、极快）
                dataPtr[pos] = samples[i];
            }

            // 更新写指针（主进程通过此值判断最新数据位置）
            header->WriteIndex = (index + samples.Length) % bufferLength;
        }

        /// <summary>
        /// 【主进程调用】读取缓冲区最新N条数据
        /// 用于获取实时最新采样值
        /// </summary>
        /// <param name="buffer">接收数据的数组</param>
        /// <param name="length">需要读取的数据长度</param>
        public void ReadLatest(double[] buffer, int length)
        {
            // 从共享头获取最新写指针
            long index = header->WriteIndex;

            // 计算读取起始位置 = 最新位置往前推length个点
            long start = index - length;

            // 处理环形越界（负数则加上缓冲区长度）
            if (start < 0)
                start += bufferLength;

            // 循环读取数据
            for (int i = 0; i < length; i++)
            {
                long pos = (start + i) % bufferLength;
                buffer[i] = dataPtr[pos];
            }
        }

        /// <summary>
        /// 释放共享内存资源
        /// 关闭映射、释放指针、销毁对象
        /// </summary>
        public void Dispose()
        {
            accessor?.Dispose();
            mmf?.Dispose();
        }
    }

    /// <summary>
    /// UI共享内存头结构
    /// 作用：记录UI波形帧状态，用于进程间同步刷新
    /// 内存布局：顺序布局，确保跨进程解析一致
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UIRingHeader
    {
        /// <summary>
        /// 生产者递增写指针（只增不减）
        /// </summary>
        public long WriteIndex;

        /// <summary>
        /// UI单帧读取的像素点数（每帧长度）
        /// </summary>
        public int PixelCount;

        /// <summary>
        /// 环形缓冲区总长度（必须 > PixelCount）
        /// </summary>
        public int BufferLength;
    }

    /// <summary>
    /// 原始数据共享内存头结构
    /// 作用：描述共享缓冲区状态，主/子进程共享配置
    /// 位于共享内存最前端，是数据区的“目录”
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RawMemoryHeader
    {
        /// <summary>
        /// 环形缓冲区写指针（当前写入位置）
        /// 子进程写入数据后更新
        /// 主进程读取时以此定位最新数据
        /// </summary>
        public long WriteIndex;

        /// <summary>
        /// 采集通道数量
        /// 例：1=单通道、8=八通道
        /// </summary>
        public int ChannelCount;

        /// <summary>
        /// 单个通道缓冲区容量（采样点个数）
        /// </summary>
        public int BufferLength;

        /// <summary>
        /// 采样率(Hz)
        /// 例：1000000 = 1MHz
        /// </summary>
        public int SampleRate;
    }


}