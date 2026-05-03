using System;
using System.IO.MemoryMappedFiles;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using ConsoleApp1.Models;
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
        public void Open1()
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

        /// <summary>
        /// 【子进程】连接共享内存
        /// 引入重试机制，增强健壮性，适应WebAPI启动时共享内存尚未创建的情况
        /// </summary>
        /// <exception cref="Exception"></exception>
        public void Open()
        {
            int retry = 30;      // 重试30次
            int sleepMs = 300;   // 每次间隔300ms

            //
            for (int i = 0; i < retry; i++)
            {
                try
                {
                    // 尝试打开共享内存
                    mmf = MemoryMappedFile.OpenExisting(
                        MapName,
                        MemoryMappedFileRights.ReadWrite);

                    // 打开成功
                    break;
                }
                catch (FileNotFoundException)
                {
                    // 没找到，等待后重试
                    Thread.Sleep(sleepMs);
                }
            }

            if (mmf == null)
            {
                throw new Exception("共享内存打开超时，请检查WebAPI是否已创建共享内存");
            }

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
    /// 核心数据总线（扁平环形数组）
    /// 作用：跨进程高速传输结构化采样数据
    /// 生产者：Analysis 线程逐条流式写入
    /// 消费者：持久化/低频UI 线程定时单条读取
    /// 核心特性：无锁设计、单写者多读者、WriteIndex 单调递增
    /// </summary>
    public unsafe class CoreDataBus : IDisposable
    {
        const string MapName = "DAQ_CORE_DATA_BUS";

        private MemoryMappedFile mmf;
        private MemoryMappedViewAccessor accessor;
        /// <summary>
        /// 指向内存映射文件的基地址
        /// </summary>
        private byte* basePtr;
        /// <summary>
        /// 指向内存头结构
        /// </summary>
        private CoreBusHeader* header; 
        /// <summary>
        /// 指向数据区的指针（紧跟头结构之后）
        /// </summary>
        private StructuredSample* dataPtr;
        /// <summary>
        /// 环形缓冲区容量（采样点数）
        /// </summary>
        private int bufferLength;

        /// <summary>
        /// 【主进程调用】创建并初始化共享内存
        /// </summary>
        /// <param name="channels">通道数量</param>
        /// <param name="buffer">环形缓冲区容量（采样点数）</param>
        /// <param name="sampleRate">采样率(Hz)</param>
        public void Create(int channels, int buffer, int sampleRate)
        {
            bufferLength = buffer;

            int headerSize = sizeof(CoreBusHeader); // 头结构大小
            long dataSize = (long)buffer * sizeof(StructuredSample); // 数据区大小
            long totalSize = headerSize + dataSize;  // 总内存大小

            mmf = MemoryMappedFile.CreateNew(
                MapName,
                totalSize,
                MemoryMappedFileAccess.ReadWrite);

            accessor = mmf.CreateViewAccessor();
            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);

            header = (CoreBusHeader*)basePtr;
            dataPtr = (StructuredSample*)(basePtr + headerSize);

            header->ChannelCount = channels;
            header->BufferLength = buffer;
            header->SampleRate = sampleRate;
            header->WriteIndex = 0;
        }

        /// <summary>
        /// 【子进程调用】连接主进程创建好的共享内存
        /// </summary>
        public void Open()
        {
            mmf = MemoryMappedFile.OpenExisting(
                MapName,
                MemoryMappedFileRights.ReadWrite);

            accessor = mmf.CreateViewAccessor();
            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);

            header = (CoreBusHeader*)basePtr;
            dataPtr = (StructuredSample*)(basePtr + sizeof(CoreBusHeader));
            bufferLength = header->BufferLength;
        }

        /// <summary>
        /// 【Analysis 线程】逐条流式写入——单写者无锁
        /// </summary>
        public void Write(ref StructuredSample sample)
        {
            // 获取当前写索引
            long index = header->WriteIndex;
            // 计算写入位置（取模环形）
            long pos = index % bufferLength;
            *(dataPtr + pos) = sample;

            Interlocked.MemoryBarrier();           // 确保数据在 WriteIndex 更新前对消费者可见
            header->WriteIndex = index + 1;        // 单写者，普通赋值即可
        }

        /// <summary>
        /// 【持久化/低频UI】读取写指针前最新 1 条数据
        /// </summary>
        public StructuredSample ReadLatestSingle()
        {
            long index = Volatile.Read(ref header->WriteIndex);
            if (index == 0) return default;
            long pos = (index - 1) % bufferLength;
            return *(dataPtr + pos);
        }

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
    /// 原始数据共享内存头结构（保留向后兼容）
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RawMemoryHeader
    {
        public long WriteIndex;
        public int ChannelCount;
        public int BufferLength;
        public int SampleRate;
    }

    /// <summary>
    /// 核心数据总线头结构
    /// 与 RawMemoryHeader 布局完全一致，仅改名
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CoreBusHeader
    {
        public long WriteIndex;    // 写指针（单调递增，取模 = 真实索引）
        public int ChannelCount;   // 通道数
        public int BufferLength;   // 环形缓冲区容量（采样点数）
        public int SampleRate;     // 采样率 (Hz)
    }
}





