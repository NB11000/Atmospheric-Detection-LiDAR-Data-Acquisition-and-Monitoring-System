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
        private static AD_Controlcs aD_Controlcs = new AD_Controlcs();

        /// <summary>
        /// 公开的静态【配置实体属性】，主窗口可以直接获取
        /// </summary>
        public static CaptureCardConfig deviceconfig { get; set; } = new CaptureCardConfig();

        /// <summary>
        /// 日志
        /// </summary>
        public static ILogger logger {  get; set; }

        /// <summary>
        /// 注册DI容器 (依赖注入容器)
        /// </summary>
        public static IServiceCollection services = new ServiceCollection();

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
                // 读取 2 个命令行参数：IP + 配置文件路径
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
                    logger.LogInformation("[主进程] 使用传入IP：" + GrpcAddress);
                    logger.LogInformation("[主进程] 使用传入配置：" + ConfigFilePath);
                }

                logger.LogInformation($"读取到完整的连接地址：{GrpcAddress}，准备启动Grpc客户端...");

                ConfigHelper.ConfigFilePath = ConfigFilePath;
                logger.LogInformation($"读取到配置文件路径：{ConfigHelper.ConfigFilePath},准备加载配置");

                // 连接共享内存
                uISharedBuffer = new UISharedBuffer();
                uISharedBuffer.Open();
                logger.LogInformation("连接主进程的[UI显示]专用共享内存缓冲区");


                // 构建.NET配置提供程序
                ConfigHelper.Config = new ConfigurationBuilder()
                    .SetBasePath(Path.GetDirectoryName(ConfigFilePath)!)
                    .AddJsonFile("DeviceConfig.json", optional: false, reloadOnChange: true)
                    .Build();

                //从JSON文件读取设备配置到实体类
                ConfigHelper.ReadDeviceConfig();


                //GrpcAddress = "https://localhost:10000";

                // 1. 初始化客户端（服务端地址+设备唯一标识）
                var client = new GrpcClient(GrpcAddress, "数据采集子进程", aD_Controlcs);

                // 2. 启动客户端（建立双向流连接）
                // 阻塞程序（避免程序退出）
                await client.StartAsync();

                // 3. 释放资源，子进程退出
                client.Dispose();
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
           
        }

      
    }
}










