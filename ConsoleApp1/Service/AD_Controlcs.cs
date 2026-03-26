using AvaloniaApplication1;
using AvaloniaApplication1.Models;
using ConsoleApp1.Tools;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ConsoleApp1.Service
{
    /// <summary>
    /// 数据采集控制类;
    /// 四个线程: ADWork(数据采集), ADDraw(数据处理), Analysis(数据分析), UI(UI刷新);
    /// 三个通道: channel(采样数据传递), Analysischannel(数据分析), UIchannel(UI显示);
    /// 依赖两个外部变量：1.mHandle(设备句柄); 2.CaptureCardConfig(当前设备配置);
    /// </summary>
    public class AD_Controlcs
    {
        public IntPtr mHandle;// 句柄
        private bool InitFlag = false;// 初始化Flag,避免反复添加Combox项
        public volatile bool ADDataTest_RunFlag = false;// AD线程运行标志位 //变量可见性
        public volatile byte chSel;
        public byte Gain;
        public int chStart;
        public int chEnd;
        public object LockFlag = false;
        // public double[] gainValue = new double[32];
        private int Chart_Count = 500;// Chart 最大可显示的点数
        //public FileStream fs;
        //public string DirName;
        public int FileName = 0;
        //public string subPath;
        Thread s1;
        Thread s2;
        Thread s3;
        Thread s4;
        List<Thread> AllThread = new List<Thread>();
        //private CaptureCardConfig deviceConfig; // 获取当前设备配置
        //private readonly MainWindowViewModel vm; // MainWindow的视图模型

        //private volatile ArrayPool<byte> pool = ArrayPool<byte>.Shared;// 声明一个字节数组池，用于减少内存分配和垃圾回收的开销
        private CancellationTokenSource cts;// 全局 CTS 取消令牌

        // 采样数据传递通道
        private Channel<Data_Block> channel;

        // 数据分析通道
        private Channel<Voltage_block> Analysischannel;

        // UI显示通道
        public static Channel<UI_Display> UIchannel;

        // 通信通道 用于将数据采集控制类的工作线程的错误信息传递给通信线程
        public static Channel<string> Errorchannel = Channel.CreateBounded<string>(new BoundedChannelOptions(10)
        {
            FullMode = BoundedChannelFullMode.DropOldest, // 或 Wait
            SingleWriter = true,
            SingleReader = true,
            AllowSynchronousContinuations = true // 同步续体
        });


        /// <summary>
        /// FIFO状态
        /// </summary>
        private volatile string FIFOStatus;

        public AD_Controlcs()
        {
            cts = new CancellationTokenSource();// 初始化 CTS 取消令牌
            CreateNewDataChannel();// 初始化通道实例
        }
        public AD_Controlcs(IntPtr m_Handle)
        {
            mHandle = m_Handle;
            cts = new CancellationTokenSource();// 初始化 CTS 取消令牌
            CreateNewDataChannel();// 初始化通道实例
        }

        /// <summary>
        /// （辅助函数）打开采集卡
        /// </summary>
        /// <returns>设备状态</returns>
        public string Device_Opened()
        {
            mHandle = USB1602.USB1602_OpenDevice(Program.deviceconfig.DeviceId);
            if (mHandle < 0)
                return "未检测到采集卡";
            else
                return "默认采集卡打开成功! 句柄：" + mHandle.ToString();
        }

        /// <summary>
        /// （辅助函数）重新打开采集卡
        /// </summary>
        /// <returns>设备状态</returns>
        public string Device_Opened_again()
        {
            //打开设备
            mHandle = USB1602.USB1602_OpenDevice(Program.deviceconfig.DeviceId);
            if (mHandle < 0)
                return "仍未检测到采集卡，请检查USB接口和是否安装采集卡驱动程序";
            else
            {
                return "采集卡设备打开成功! 句柄：" + mHandle.ToString();
                //mHandle = mHandle;
            }
        }

        /// <summary>
        /// （辅助函数）创建新的Channel实例，返回对应的Writer和Reader（按需返回）
        /// </summary>
        private void CreateNewDataChannel()
        {
            // 采样数据传递通道
            channel = Channel.CreateBounded<Data_Block>(new BoundedChannelOptions(64)
            {
                FullMode = BoundedChannelFullMode.DropOldest, // 或 Wait
                SingleWriter = true,
                SingleReader = true,
                AllowSynchronousContinuations = true // 同步续体
            },
                dropped =>
                {
                    // 触发背压时，归还旧数据中的数组
                    if (dropped.Buffer != null)
                        ArrayPool<byte>.Shared.Return(dropped.Buffer);
                } 
            );

            // 数据分析通道
            Analysischannel = Channel.CreateBounded<Voltage_block>(new BoundedChannelOptions(64)
            {
                FullMode = BoundedChannelFullMode.DropOldest, // 或 Wait
                SingleWriter = true,
                SingleReader = true,
                AllowSynchronousContinuations = true // 同步续体
            },
                dropped =>
                {
                    // 触发背压时，归还旧数据中的数组
                    if (dropped.Voltage1 != null)
                        ArrayPool<double>.Shared.Return(dropped.Voltage1);
                    if (dropped.Voltage2 != null)
                        ArrayPool<double>.Shared.Return(dropped.Voltage2);
                }
            );

            //UI显示通道
            UIchannel = Channel.CreateBounded<UI_Display>(new BoundedChannelOptions(10)
            {
                FullMode = BoundedChannelFullMode.DropOldest, // 或 Wait
                SingleWriter = true,
                SingleReader = true,
                AllowSynchronousContinuations = true // 同步续体
            },
                dropped =>
                {
                    // 触发背压时，归还旧数据中的数组
                    if (dropped.Voltage1 != null)
                        ArrayPool<double>.Shared.Return(dropped.Voltage1);
                    if (dropped.Voltage2 != null)
                        ArrayPool<double>.Shared.Return(dropped.Voltage2);
                }
            );
        }

        /// <summary>
        /// （辅助函数）校准公式
        /// </summary>
        /// <param name="dfRet"></param>
        /// <param name="k"></param>
        /// <param name="b"></param>
        private void Calibration(ref double dfRet, double k, double b)
        {
            dfRet = k * dfRet + b;
        }

        /// <summary>
        /// （辅助函数）触发异常时，为了让系统正常回到初始状态而调用的方法
        /// </summary>
        private void init()
        {
            //AD块关闭
            USB1602.USB1602_ADStop(mHandle);
            USB1602.USB1602_IOCTL_VENDOR_REQUEST(mHandle, 0XBA);
            //清除FIFO
            USB1602.USB1602_CLRRAM(mHandle);

            while (channel.Reader.TryRead(out var block))
            {
                ArrayPool<byte>.Shared.Return(block.Buffer);
            }
            while (Analysischannel.Reader.TryRead(out var block))
            {
                if (block.Voltage1 != null)
                {
                    ArrayPool<double>.Shared.Return(block.Voltage1);
                }
                if (block.Voltage2 != null)
                {
                    ArrayPool<double>.Shared.Return(block.Voltage2);
                }
            }
            while (UIchannel.Reader.TryRead(out var block))
            {
                if (block.Voltage1 != null)
                {
                    ArrayPool<double>.Shared.Return(block.Voltage1);
                }
                if (block.Voltage2 != null)
                {
                    ArrayPool<double>.Shared.Return(block.Voltage2);
                }
            }

            /// 清理旧通道的引用，方便GC回收
            channel = null;
            Analysischannel = null;
            UIchannel = null;
            cts = null;

            cts = new CancellationTokenSource();// 初始化 CTS 取消令牌
            CreateNewDataChannel();// 初始化通道实例

            GC.Collect();// 强制进行垃圾回收，释放未使用的内存
        }



        /// <summary>
        /// 最小值最大值降采样算法（工业示波器常用）
        /// 
        /// 作用：
        /// 将任意长度的数据降采样为指定像素数（例如1000点）
        /// 
        /// 算法原理：
        /// 1. 将原始数据分成 N 段
        /// 2. 每段计算最小值和最大值
        /// 3. 将 min / max 交替写入输出数组
        /// 
        /// 优点：
        /// - 保留波形尖峰
        /// - 不会丢失极值
        /// - 时间复杂度 O(N)
        /// - 无GC
        /// 
        /// 示例：
        /// 输入 90000 点 → 输出 1000 点
        /// 
        /// 90000 / 500 = 180
        /// 
        /// 每180个点：
        /// min → 输出
        /// max → 输出
        /// </summary>
        /// <param name="source">原始数据</param>
        /// <param name="dest">降采样后的数组（例如1000长度）</param>
        private static void DownSampleMinMax(double[] source, double[] dest)
        {
            // 原始数据长度（自动识别）
            int sourceLength = source.Length;
            // 输出长度（例如1000）
            int destLength = dest.Length;
            // 每段输出2个点（min + max），分为500段
            int segmentCount = destLength / 2;
            // 每段包含多少原始点
            int bucketSize = sourceLength / segmentCount;
            // 写入位置
            int di = 0;

            // 遍历每个段
            for (int i = 0; i < segmentCount; i++)
            {
                int start = i * bucketSize;
                int end = start + bucketSize;

                // 防止越界
                if (end > sourceLength)
                    end = sourceLength;

                double min = double.MaxValue;
                double max = double.MinValue;

                // 在当前段中寻找最小值和最大值
                for (int j = start; j < end; j++)
                {
                    double v = source[j];

                    if (v < min) min = v;
                    if (v > max) max = v;
                }

                // 写入降采样结果
                dest[di++] = min;
                dest[di++] = max;
            }
        }



        /// <summary>
        /// AD数据采集线程
        /// </summary>
        private unsafe void ADWork()
        {
            bool iResult;
            //byte[] buffer = new byte[1024 * 1024];
            uint BufferSize;
            uint nBytes = 0;
            int count = 0;
            byte u8Status = 0;
        
            // AD块启动
            iResult = USB1602.USB1602_IOCTL_VENDOR_REQUEST(mHandle, 0XB2);
            iResult = USB1602.USB1602_ADStart(mHandle);

            BufferSize = 184320;

            int q;

            //byte[] buffer = new byte[1024 * 1024];
            try
            {
                while (ADDataTest_RunFlag)
                {
                    // 从池里租一块数组
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(200000);
                    // 获取FIFO状态
                    // ref是 C# 中的引用传递关键字，核心作用是 “传递变量的内存地址”
                    // 因为引用了外部变量的内存地址，在方法内部可以直接修改传入的外部变量的值，而不仅仅是修改其副本。
                    iResult = USB1602.USB1602_GetFIFOInfo(mHandle, ref u8Status);


                    //FIFOStatus = "FIFO状态:" + u8Status; //显示FIFO状态
                    //vm.Fifo = "FIFO状态:" + u8Status;//显示FIFO状态

                    //buffer内存没有固定，可能会被GC移动，导致 C API 调用失败
                    //所以在此处没有固定buffer数组
                    fixed (byte* pBuffer = buffer)
                    {
                        //读取数据
                        iResult = USB1602.USB1602_IOCTL_BULK_READ(mHandle, (IntPtr)pBuffer, BufferSize, ref nBytes);//共读取到bufferSize/2个采样点,因为一个采样点占用2字节
                        q = Marshal.GetLastWin32Error();
                    }


                    //判断读取结果
                    if (!iResult || nBytes == 0)
                    {
                        //读取失败或无数据，归还数组并继续下一次循环
                        ArrayPool<byte>.Shared.Return(buffer);
                        //当数据读取失败时，停止采集，并更新UI界面
                        if (!iResult)
                        {
                            // 仅设置退出标志，不调用 stop()
                            ADDataTest_RunFlag = false;
                            cts.Cancel();  // 触发所有等待操作取消
                            //Dispatcher.UIThread.Post(() =>
                            //{
                            //    vm.Status = "数据读取失败或采集卡断开,已停止采集";
                            //    vm.Content = "开始采集";
                            //});
                            break;
                        }
                        continue;
                    }

                    //将数据放入队列，注意：new Data_Block()是分配在栈上的，每次循环都会被复用，不会有内存泄漏问题
                    //当通道无法写入数据时，归还租用的数组
                    if (!channel.Writer.TryWrite(new Data_Block(buffer, (int)nBytes)))
                        ArrayPool<byte>.Shared.Return(buffer); // 防止泄漏
                }
            }
            catch (Exception ex)
            {
                // 仅设置退出标志，不调用 stop()
                ADDataTest_RunFlag = false;
                cts.Cancel();  // 触发所有等待操作取消
                init();//系统初始化
                GrpcClient.SendErrorMessage("AD采集线程出现异常: " + ex.Message);
            }
        }

        /// <summary>
        /// AD数据处理线程; 
        /// 读取队列中的数据进行处理并显示到图表和表格中;
        /// </summary>
        private void ADDraw()
        {
            int i = 0;
            int j = 0;
            //byte[] m_RData = new byte[2048];//临时存储从队列中取回的数据
            double dfRet;
            double k1 = 0;
            double b1 = 0;
            double k2 = 0;
            double b2 = 0;
            bool iResult;
            long startTick; // 采样起始时间戳
            Data_Block block = new Data_Block();// 原始数据块（数字量）
            double a = 0; // 用于计算不同通道的采样点间隔比例系数
            long b = 0; //设定租用数组长度
            bool Result1 = false;
            bool Result2 = false;
            int c = 0;
            // 计算采样点间隔比例系数
            if (chSel == 1 || chSel == 2)
            {
                a = 0.5;
                c = 92160;
            }
            else if (chSel == 3)
            {
                a = 0.25;
                c = 46080; 
            }

            // 获取通道校准系数 
            if (chSel == 1 || chSel == 3)
            {
                iResult = USB1602.USB1602_GetKB(mHandle, 1, Gain, ref k1, ref b1);
                Result1 = true;
            }
            if (chSel == 2 || chSel == 3)
            {
                iResult = USB1602.USB1602_GetKB(mHandle, 2, Gain, ref k2, ref b2);
                Result2 = true;
            }

            // 临时存储转换后的电压数据
            double[]? result1 = null;
            double[]? result2 = null;
            double[]? Ui1 = null;
            double[]? Ui2 = null;

            try
            {
                while (ADDataTest_RunFlag)
                {
                    //_ = await channel.Reader.WaitToReadAsync();//当通道中没有数据，线程在此处等待挂起，避免cpu空转
                    //// 从通道中读取数据块,如果没读到，则跳出本次循环
                    //// 避免未读取数据块，仍然执行后续的归还数据块中的数组（block.buffer此时为空），从而触发异常
                    //if (!channel.Reader.TryRead(out block))
                    //    continue;

                    //try
                    //{
                    //    //当通道中没有数据，线程在此处等待挂起，避免cpu空转(同步阻塞)
                    //    block = channel.Reader.ReadAsync(cts.Token)
                    //                          .AsTask()
                    //                          .GetAwaiter()
                    //                          .GetResult();
                    //    //channel.Reader.TryRead(out block);
                    //}
                    //catch (OperationCanceledException)
                    //{
                    //    break; // 正常退出
                    //}
                    while (ADDataTest_RunFlag)
                    {
                        // 先Peek检查是否有数据
                        if (channel.Reader.TryPeek(out var item))
                        {
                            channel.Reader.TryRead(out block); // 检查有数据时，调用TryRead消费
                            break; // 正常退出
                        }
                        else
                        {
                            Thread.Sleep(1); // 阻塞等待1ms，防止cpu空转
                        }
                    }


                    //从数组池租用数组
                    if (Result1)
                    {
                        result1 = ArrayPool<double>.Shared.Rent(c);// 从数组池获取一个数组，临时存储通道1的电压数据
                        Ui1 = ArrayPool<double>.Shared.Rent(c);
                    }
                    if (Result2)
                    {
                        result2 = ArrayPool<double>.Shared.Rent(c);// 从数组池获取一个数组，临时存储通道2的电压数据
                        Ui2 = ArrayPool<double>.Shared.Rent(c);
                    }

                    startTick = Stopwatch.GetTimestamp();// 记录当前采样时间，采样时间戳
                                                         //m_RData = block.Buffer;// 获取数据块的字节数组

                    j = 0;
                    //处理从通道获取的数据块中所有的采样点数据
                    //可能有越界问题，后续测试
                    //注意：处理数据块中数组的有效数据长度nBytes
                    for (i = 0; i < block.nBytes / 2; i++)
                    {

                        if (Result1)
                        {
                            dfRet = block.Buffer[i * 2] + block.Buffer[i * 2 + 1] * 256;//仅示例一个采样点计算
                            dfRet = (int)dfRet & 0x0000ffff;
                            USB1602.Fun_DataToV(ref dfRet, Gain);
                            //Calibration(ref dfRet, k1, b1);
                            dfRet = k1 * dfRet + b1;//校准
                            dfRet = Math.Round(dfRet, 5);

                            // 处理后的数据依次写入分析数据数组和UI显示数组
                            result1[j] = dfRet;
                            Ui1[j] = dfRet;

                            // 这里写 根据通道1的数据存入电压数据块（Voltage_block） 注意不要把m_RData写入Voltage_block，因为m_RData存放的是数组的内存地址。不是数据本身。
                            // 然后再将电压数据块存入到一个集合/数组中，之后再写入到 Channel<Voltage_block> 通道（全量分析通道）中。
                            if (chSel == 3)
                                i++;
                        }
                        if (Result2)
                        {
                            dfRet = block.Buffer[i * 2] + block.Buffer[i * 2 + 1] * 256;//仅示例一个采样点计算
                            dfRet = (int)dfRet & 0x0000ffff;
                            USB1602.Fun_DataToV(ref dfRet, Gain);
                            //Calibration(ref dfRet, k2, b2);
                            dfRet = k2 * dfRet + b2;//校准
                            dfRet = Math.Round(dfRet, 5);

                            // 处理后的数据依次写入分析数据数组和UI显示数组
                            result2[j] = dfRet;
                            Ui2[j] = dfRet;
                            //这里写 同上  
                        }
                        j++;
                    }

                    ////这一步，从临时存储result1，result2中，拷贝全部采样点，然后写入UI显示通道 
                    //// span 内存块批量拷贝，无内存和GC开销
                    //if (Result1)
                    //    new Span<double>(result1).CopyTo(Ui1);
                    //if (Result2)
                    //    new Span<double>(result2).CopyTo(Ui2);

                    // 将处理后的电压数据块写入分析通道
                    if (!Analysischannel.Writer.TryWrite(new Voltage_block
                    { Voltage1 = result1, Voltage2 = result2, SampleCount = block.nBytes * a, StartTick = startTick }))
                    {
                        // 如果写入失败，归还数组，防止泄漏
                        if (Result1)
                        {
                            ArrayPool<double>.Shared.Return(result1);
                            result1 = null;
                        }
                        if (Result2)
                        {
                            ArrayPool<double>.Shared.Return(result2);
                            result2 = null;
                        }
                    }

                    // 写入UI显示通道
                    if (!UIchannel.Writer.TryWrite(new UI_Display
                    { Voltage1 = Ui1, Voltage2 = Ui2 }))
                    {
                        // 如果写入失败，归还数组，防止泄漏
                        if (Result1)
                        {
                            ArrayPool<double>.Shared.Return(Ui1);
                            result1 = null;
                        }
                        if (Result2)
                        {
                            ArrayPool<double>.Shared.Return(Ui2);
                            result2 = null;
                        }
                    }
                //End:;//当 ADDataTest_RunFlag 变为 false 时跳出循环到此处，此时没有必要在往Analysischannel中写数据了，但要归还数组
                    // 处理完数据后归还数组（由消费者线程归还）       
                    ArrayPool<byte>.Shared.Return(block.Buffer);

                    // result1，result2,Ui1,Ui2数组的归还，在数据分析线程中完成
                }
            }
            catch (Exception ex)
            {
                // 记录完整的异常信息
                string errorMsg = $"AD数据处理线程异常:\n" +
                                 $"异常类型: {ex.GetType()}\n" +
                                 $"异常消息: {ex.Message}\n" +
                                 $"堆栈跟踪:\n{ex.StackTrace}\n" +
                                 $"来源: {ex.Source}";
                // 输出到调试窗口
                Debug.WriteLine(errorMsg);
                // 仅设置退出标志，不调用 stop()
                ADDataTest_RunFlag = false;
                cts.Cancel();  // 触发所有等待操作取消
                init();//系统初始化
                GrpcClient.SendErrorMessage("AD数据处理线程出现异常: " + ex.Message);
                // 处理完数据后归还数组
                if (block.Buffer != null)
                    ArrayPool<byte>.Shared.Return(block.Buffer);
            }
        }

        /// <summary>
        /// 全量数据分析线程
        /// </summary>
        private void Analysis()
        {
            Voltage_block voltageBlock = new Voltage_block();
            try
            {
                while (ADDataTest_RunFlag)
                {
                    //_ = await Analysischannel.Reader.WaitToReadAsync();//当通道中没有数据，线程在此处等待挂起，避免cpu空转
                    //if (!Analysischannel.Reader.TryRead(out voltageBlock))// 从通道中读取数据块
                    //    continue;// 从通道中读取数据块,如果没读到，则跳出本次循环

                    //try
                    //{
                    //    //当通道中没有数据，线程在此处等待挂起，避免cpu空转(同步阻塞)
                    //    voltageBlock = Analysischannel.Reader.ReadAsync(cts.Token)
                    //                          .AsTask()
                    //                          .GetAwaiter()
                    //                          .GetResult();
                    //}
                    //catch (OperationCanceledException)
                    //{
                    //    break; // 正常退出
                    //}

                    while (ADDataTest_RunFlag)
                    {
                        // 先Peek检查是否有数据
                        if (Analysischannel.Reader.TryPeek(out var item))
                        {
                            Analysischannel.Reader.TryRead(out voltageBlock); // 检查有数据时，调用TryRead消费
                            break; // 正常退出
                        }
                        else
                        {
                            Thread.Sleep(1); // 阻塞等待1ms，防止cpu空转
                        }
                    }

                    // 下面写 数据分析代码



                    // 处理完数据后归还数组
                    if (voltageBlock.Voltage1 != null)
                        ArrayPool<double>.Shared.Return(voltageBlock.Voltage1);
                    if (voltageBlock.Voltage2 != null)
                        ArrayPool<double>.Shared.Return(voltageBlock.Voltage2);
                }
            }
            catch (Exception ex)
            {
                // 仅设置退出标志，不调用 stop()
                ADDataTest_RunFlag = false;
                cts.Cancel();  // 触发所有等待操作取消
                init();//系统初始化
                GrpcClient.SendErrorMessage("分析数据线程出现异常: " + ex.Message);
                // 处理完数据后归还数组
                if (voltageBlock.Voltage1 != null)
                    ArrayPool<double>.Shared.Return(voltageBlock.Voltage1);
                if (voltageBlock.Voltage2 != null)
                    ArrayPool<double>.Shared.Return(voltageBlock.Voltage2);
            }
        }


        private static Stopwatch stopwatch;
        /// <summary>
        /// UI刷新线程（UI调度层）
        /// </summary>
        private void UI()
        {
            UI_Display UIDisplay = new UI_Display();
            stopwatch = Stopwatch.StartNew();
            const int UI_INTERVAL_MS = 33; // ~30FPS
            double[] text1 = new double[1000];
            double[] text2 = new double[1000];
            bool ch1 = false;
            bool ch2 = false;

            try
            {
                while (ADDataTest_RunFlag)
                {

                    while (ADDataTest_RunFlag)
                    {
                        // 先Peek检查是否有数据
                        if (UIchannel.Reader.TryPeek(out var item))
                        {
                            UIchannel.Reader.TryRead(out UIDisplay); // 检查有数据时，调用TryRead消费
                            if (UIDisplay.Voltage1 != null)
                                ch1 = true;
                            if (UIDisplay.Voltage2 != null)
                                ch2 = true;
                            break; // 正常退出
                        }
                        else
                        {
                            Thread.Sleep(1); // 阻塞等待1ms，防止cpu空转
                        }
                    }

                    //// 是否到 33ms 刷新窗口
                    //if (stopwatch.ElapsedMilliseconds < UI_INTERVAL_MS)
                    //{
                    //    // 没到33ms归还数组
                    //    if (ch1)
                    //        ArrayPool<double>.Shared.Return(UIDisplay.Voltage1);
                    //    if (ch2)
                    //        ArrayPool<double>.Shared.Return(UIDisplay.Voltage2);
                    //    continue;
                    //}
                    //stopwatch.Restart();

                    //// 使用 span 将数据复制到预分配的数组中，避免归还数组时的影响跨线程操作
                    //if (ch1)
                    //    new Span<double>(UIDisplay.Voltage1, 0, 1000).CopyTo(text1);
                    //if (ch2)
                    //    new Span<double>(UIDisplay.Voltage2, 0, 1000).CopyTo(text2);

                    // 使用最小值最大值降采样算法,对数据进行降采样
                    // DownSampleMinMax(UIDisplay.Voltage1, text1);
                    if (ch1)
                        new Span<double>(UIDisplay.Voltage1, 0, 1000).CopyTo(text1);
                    if (ch2)
                        new Span<double>(UIDisplay.Voltage2, 0, 1000).CopyTo(text2);

                    ////切换到UI线程更新图表数据
                    //Dispatcher.UIThread.Post(() =>
                    //{
                    //    // 切换到 UI 线程刷新图表
                    //    // 此处一直在触发GC
                    //    //vm.UpdateChart1(UIDisplay.Voltage1, UIDisplay.Voltage2, 1000);
                    //    //vm.Fifo = FIFOStatus;

                    //    // 改为使用 ViewModel 的方法更新图表数据，减少对 UI 元素的直接操作
                    //    vm.UpdateChart1(text1, text2, 1000);

                    //});

                    // 将UI降采样数据写入共享内存
                    Program.uISharedBuffer.WriteSampleBatch(text1, text2, 1000);

                    // 处理完数据后归还数组
                    if (ch1)
                        ArrayPool<double>.Shared.Return(UIDisplay.Voltage1);
                    if (ch2)
                        ArrayPool<double>.Shared.Return(UIDisplay.Voltage2);
                }
            }
            catch (Exception ex)
            {
                // 仅设置退出标志，不调用 stop()
                ADDataTest_RunFlag = false;
                cts.Cancel();  // 触发所有等待操作取消
                init();//系统初始化
                GrpcClient.SendErrorMessage("UI刷新线程出现异常: " + ex.Message);
                // 处理完数据后归还数组
                if (UIDisplay.Voltage1 != null)
                    ArrayPool<double>.Shared.Return(UIDisplay.Voltage1);
                if (UIDisplay.Voltage2 != null)
                    ArrayPool<double>.Shared.Return(UIDisplay.Voltage2);
            }
        }


        /// <summary>
        /// AD开始采集
        /// </summary>
        public void start()
        {
            int i;
            bool iResult;
            float freq;
            ushort freDiv;
            byte clock;
            byte trigMode;
            int[] gain = new int[32];
            int[] difValue = new int[32];

            //if (AD_checkBox1.Checked == true)//保存文件
            //{
            //    string currPath = AppDomain.CurrentDomain.BaseDirectory;//获取程序的基目录
            //    DirName = DateTime.Now.ToString("yyyyMMddhhmmss");
            //    subPath = currPath + "\\" + DirName;
            //    Directory.CreateDirectory(subPath);//创建文件夹

            //    fs = new FileStream(subPath + "\\" + FileName + ".dat", FileMode.Create);
            //}

            chSel = (byte)(Program.deviceconfig.SyncChannelIndex + 1);
            Gain = (byte)Program.deviceconfig.RangeIndex;
            freq = (float)Program.deviceconfig.SampleRate;
            freDiv = (UInt16)Math.Round(40000.0 / freq);
            if (freDiv < 40)
                freDiv = 40;
            if (freDiv > 65535)
                freDiv = 65535;
            clock = (byte)Program.deviceconfig.ClockSourceIndex;
            trigMode = (byte)Program.deviceconfig.TriggerSourceIndex;

            //清fifo
            iResult = USB1602.USB1602_CLRRAM(mHandle);

            //通道设置
            iResult = USB1602.USB1602_SetADCh(mHandle, chSel);//设置同步通道
            iResult = USB1602.USB1602_AD1RangeSet(mHandle, Gain);//设置通道1量程
            iResult = USB1602.USB1602_AD2RangeSet(mHandle, Gain);//设置通道2量程

            //频率
            iResult = USB1602.USB1602_SetFreDiv(mHandle, freDiv);//设置采样频率

            //工作模式
            iResult = USB1602.USB1602_SetClkSource(mHandle, clock);//设置AD时钟源
            iResult = USB1602.USB1602_SetADMode(mHandle, trigMode);//设置AD触发模式


            ADDataTest_RunFlag = true;

            ////AD块启动 
            //iResult = XCDotNETAPI.USB1602.USB1602_IOCTL_VENDOR_REQUEST(mHandle, 0XB2);
            //iResult = XCDotNETAPI.USB1602.USB1602_ADStart(mHandle);

            //Control.CheckForIllegalCrossThreadCalls = false;

            s1 = new Thread(ADWork)
            {
                Name = "AD数据采集线程", // 便于调试
                Priority = ThreadPriority.Highest, // 关键：设置最高优先级
                IsBackground = true, // 后台线程，不阻塞程序退出
            };
            AllThread.Add(s1);
            s1.Start();

            s2 = new Thread(ADDraw)
            {
                Name = "AD数据处理线程", // 便于调试
                Priority = ThreadPriority.Highest, // 关键：设置最高优先级
                IsBackground = true, // 后台线程，不阻塞程序退出
            };
            AllThread.Add(s2);
            s2.Start();

            s3 = new Thread(Analysis)
            {
                Name = "分析数据线程", // 便于调试
                Priority = ThreadPriority.Highest, // 关键：设置最高优先级
                IsBackground = true, // 后台线程，不阻塞程序退出
            };
            AllThread.Add(s3);
            s3.Start();

            //UI刷新可以交由UI线程控制，而非后台线程
            //此线程特殊，要单独控制
            s4 = new Thread(UI)
            {
                Name = "UI刷新线程", // 便于调试
                Priority = ThreadPriority.Highest, // 关键：设置最高优先级
                IsBackground = true, // 后台线程，不阻塞程序退出
            };
            AllThread.Add(s4);                                                                                                  
            s4.Start();
        }

        /// <summary>
        /// AD停止采集
        /// </summary>
        public void stop()
        {
            ADDataTest_RunFlag = false;
            cts.Cancel();
            bool iResult;
            channel.Writer.TryComplete();
            Analysischannel.Writer.TryComplete();
            UIchannel.Writer.TryComplete();

            //等待所有（生产者-消费者）线程执行完毕
            foreach(var thread in AllThread)
            {
                thread.Join();
                //如果程序卡死，则强制退出
                if (thread.IsAlive == true)
                    thread.Abort();
            }
            //AD块关闭
            iResult = USB1602.USB1602_ADStop(mHandle);
            iResult = USB1602.USB1602_IOCTL_VENDOR_REQUEST(mHandle, 0XBA);
            //清除FIFO
            iResult = USB1602.USB1602_CLRRAM(mHandle);

            // 清除通道数据
            //channel
            //Analysischannel
            //UIchannel

            while (channel.Reader.TryRead(out var block))
            {
                ArrayPool<byte>.Shared.Return(block.Buffer);
            }
            while (Analysischannel.Reader.TryRead(out var block))
            {
                if (block.Voltage1 != null)
                {
                    ArrayPool<double>.Shared.Return(block.Voltage1);
                }
                if (block.Voltage2 != null)
                {
                    ArrayPool<double>.Shared.Return(block.Voltage2);
                }
            }
            while (UIchannel.Reader.TryRead(out var block))
            {
                if (block.Voltage1 != null)
                {
                    ArrayPool<double>.Shared.Return(block.Voltage1);
                }
                if (block.Voltage2 != null)
                {
                    ArrayPool<double>.Shared.Return(block.Voltage2);
                }
            }

            /// 清理旧通道的引用，方便GC回收
            channel = null;
            Analysischannel= null;
            UIchannel= null;
            cts = null;

            cts = new CancellationTokenSource();// 初始化 CTS 取消令牌
            CreateNewDataChannel();// 初始化通道实例

            GC.Collect();// 强制进行垃圾回收，释放未使用的内存

        }
    }

}

