using ConsoleApp1.Models;
using ConsoleApp1.Service;
using ConsoleApp1.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using NetMQ;
using NetMQ.Sockets;
using SharedMemoryFramework;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection.Metadata;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using System.Threading;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace ConsoleApp1
{
    class Program
    {
        /// <summary>
        /// 数据采集控制类实例，负责设备控制与采集逻辑
        /// </summary>
        private static AD_Controlcs aD_Controlcs;

        /// <summary>
        /// 公开的静态【配置实体属性】，主窗口可以直接获取
        /// </summary>
        public static CaptureCardConfig deviceconfig { get; set; } = new CaptureCardConfig();

        /// <summary>
        /// 日志
        /// </summary>
        public static ILogger logger {  get; set; }

        /// <summary>
        /// DI服务提供程序
        /// </summary>
        private static IServiceProvider _serviceProvider;

        /// <summary>
        /// 服务端IP
        /// </summary>
        public static string GrpcAddress { get; private set; }
        /// <summary>
        /// 配置文件路径
        /// </summary>
        public static string ConfigFilePath { get; private set; }

        /// <summary>
        /// UI显示专用共享缓冲区
        /// </summary>
        public static UISharedBuffer uISharedBuffer { get; private set; }

        /// <summary>
        /// 核心数据总线实例（用于释放资源，通过DI获取）
        /// </summary>
        private static CoreDataBus coreBus;

        /// <summary>
        /// 父进程监控取消令牌源
        /// </summary>
        private static CancellationTokenSource _parentMonitorCts;

        /// <summary>
        /// gRPC客户端实例引用
        /// </summary>
        private static GrpcClient _grpcClient;


        /// <summary>
        /// 客户端入口（控制台/UI程序均可）
        /// 用于接受主进程的调用命令，并反馈结果,以及上报数据
        /// </summary>
        static async Task Main(string[] args)
        {
            try
            {

                Console.Title = "数据采集子进程";

                // 初始化日志
                logger = LoggerFactory.Create(b => b.AddConsole()
                .SetMinimumLevel(LogLevel.Debug)).CreateLogger("运行日志"); 

                // ==============================================
                // 读取命令行参数：IP + 配置文件路径 + 父进程ID
                // ==============================================
                // 校验参数（确保传入了完整的参数）
                if (args.Length == 0 || !args[0].StartsWith("http://"))
                {
                    logger.LogInformation("错误：未获取到有效的Grpc服务端连接地址（格式：http://IP:Port）");
                    return;
                }
                else if (args.Length >= 2)
                {
                    // 直接读取完整的连接地址（无解析，零GC）
                    GrpcAddress = args[0];
                    // 第2个参数：配置文件路径
                    ConfigFilePath = args[1];
                    logger.LogInformation("[主进程] 传入IP：" + GrpcAddress);
                    logger.LogInformation("[主进程] 传入配置：" + ConfigFilePath);
                }

                // 解析父进程ID（第三个参数，可选）
                int parentProcessId = -1;
                if (args.Length >= 3 && int.TryParse(args[2], out int parsedPid))
                {
                    parentProcessId = parsedPid;
                    logger.LogInformation($"父进程ID：{parentProcessId}，启动父进程监视");
                }

                logger.LogInformation($"读取到完整的连接地址：{GrpcAddress}，准备启动Grpc客户端...");

                ConfigHelper.ConfigFilePath = ConfigFilePath;
                logger.LogInformation($"读取到配置文件路径：{ConfigHelper.ConfigFilePath},准备加载配置");

                // 连接共享内存
                uISharedBuffer = new UISharedBuffer();
                uISharedBuffer.Open();
                logger.LogInformation("连接主进程的[UI显示]专用共享内存缓冲区");


                // 连接核心数据总线
                coreBus = new CoreDataBus();
                coreBus.Open();
                logger.LogInformation("连接主进程的核心数据总线");



                // 构建.NET配置提供程序
                ConfigHelper.Config = new ConfigurationBuilder()
                    .SetBasePath(Path.GetDirectoryName(ConfigFilePath)!)
                    .AddJsonFile("DeviceConfig.json", optional: false, reloadOnChange: true)
                    .Build();

                //从JSON文件读取设备配置到实体类
                ConfigHelper.ReadDeviceConfig();

                // ==============================================
                // 构建 DI 容器（依赖注入）
                // ==============================================
                var services = new ServiceCollection();
                services.AddSingleton<ILogger>(logger);
                services.AddSingleton(deviceconfig);
                services.AddSingleton(uISharedBuffer);
                services.AddSingleton(coreBus);
                services.AddSingleton<AD_Controlcs>();
                _serviceProvider = services.BuildServiceProvider();
                aD_Controlcs = _serviceProvider.GetRequiredService<AD_Controlcs>();
                logger.LogInformation("DI容器初始化完成，CoreDataBus 已注入");

                // 启动父进程监控（如果传入了父进程ID）
                if (parentProcessId > 0)
                {
                    _parentMonitorCts = new CancellationTokenSource();
                    _ = Task.Run(() => MonitorParentProcessAsync(parentProcessId, _parentMonitorCts.Token));
                }

                //GrpcAddress = "https://localhost:10000";

                // 1. 初始化客户端（服务端地址+设备唯一标识）
                _grpcClient = new GrpcClient(GrpcAddress, "数据采集子进程", aD_Controlcs);

                // 2. 启动客户端（建立双向流连接）
                // 阻塞程序（避免程序退出）
                await _grpcClient.StartAsync();

                // 3. 停止父进程监控（如果已启动）
                _parentMonitorCts?.Cancel();

                // 4. 释放资源，子进程退出
                _grpcClient.Dispose();
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
           
        }

        /// <summary>
        /// 异步监视父进程状态（使用WaitForExitAsync事件驱动）
        /// </summary>
        /// <param name="parentProcessId">父进程ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        private static async Task MonitorParentProcessAsync(int parentProcessId, CancellationToken cancellationToken)
        {
            try
            {
                logger.LogInformation($"开始监控父进程（PID={parentProcessId}）...");
                
                Process parentProcess = null;
                
                try
                {
                    // 获取父进程对象
                    parentProcess = Process.GetProcessById(parentProcessId);
                }
                catch (ArgumentException)
                {
                    // GetProcessById抛出ArgumentException表示进程不存在
                    logger.LogWarning($"父进程（PID={parentProcessId}）不存在，可能已退出，触发子进程优雅退出");
                    await TriggerGracefulExitFromParentMonitorAsync();
                    return;
                }

                // 使用WaitForExitAsync异步等待父进程退出（事件驱动，非轮询）
                // logger.LogInformation($"等待父进程退出...");
                await parentProcess.WaitForExitAsync(cancellationToken);

                // 检查是否是因为取消令牌而退出
                if (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning($"检测到父进程（PID={parentProcessId}）已退出，触发子进程优雅退出");
                    await TriggerGracefulExitFromParentMonitorAsync();
                }
                else
                {
                    logger.LogInformation("父进程监控已取消（正常退出流程）");
                }

                // 释放父进程资源
                parentProcess?.Dispose();
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("父进程监控任务已取消");
            }
            catch (Exception ex)
            {
                logger.LogError($"父进程监视异常：{ex.Message}");
                // 发生异常时也触发优雅退出，确保资源清理
                await TriggerGracefulExitFromParentMonitorAsync();
            }
        }

        /// <summary>
        /// 从父进程监控触发的优雅退出方法（独立于gRPC EXIT命令）
        /// </summary>
        private static async Task TriggerGracefulExitFromParentMonitorAsync()
        {
            try
            {
                logger.LogInformation("开始父进程监控触发的优雅退出流程...");


                // 1. 停止数据采集（如果设备正在采集）
                // 2. 关闭采集卡设备
                if (aD_Controlcs.mHandle >= 0)
                {
                    // 注意：这里假设stop()方法是幂等的，多次调用安全
                    aD_Controlcs.stop(); // 停止采集
                    logger.LogInformation("数据采集已停止");
                    USB1602.USB1602_CloseDevice(aD_Controlcs.mHandle);
                    aD_Controlcs.mHandle = -1;
                    logger.LogInformation("采集卡设备已关闭");
                }

                // 3. 释放共享内存资源
                uISharedBuffer?.Dispose();
                coreBus?.Dispose();
                logger.LogInformation("共享内存资源已释放");
                
                // 4. 停止gRPC客户端（如果仍在运行）
                if (_grpcClient != null)
                {
                    try
                    {
                        // 只停止客户端，不发送任何响应（因为主进程已关闭）
                        _grpcClient.Stop();
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning($"停止gRPC客户端时发生异常：{ex.Message}");
                    }
                }
                
                logger.LogInformation("父进程监控触发的优雅退出流程完成");
            }
            catch (Exception ex)
            {
                logger.LogError($"父进程监控触发的优雅退出过程中发生异常：{ex.Message}");
            }
            finally
            {
                // 确保进程退出
                Environment.Exit(0);
            }
        }
    }
}










