using ConsoleApp1.Service;
using ConsoleApp1.Tools;
using NetMQ;
using NetMQ.Sockets;
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
        // 数据采集控制类实例，负责设备控制与采集逻辑
        private static AD_Controlcs aD_Controlcs = new AD_Controlcs();
        // 记录最后一次收到主进程消息的时间
        private static DateTime lastHeartbeat = DateTime.Now;
        // 全局复用字符串（避免每次创建新实例）
        private static string Error_msg;
        private static string command = null;

        private static string GrpcAddress;
        private static volatile bool True_False = true;

        /// <summary>
        /// 客户端入口（控制台/UI程序均可）
        /// 用于接受主进程的调用命令，并反馈结果,以及上报数据
        /// </summary>
        static async Task Main(string[] args)
        {
            GC.RegisterForFullGCNotification(1, 1);
            // 后台线程监控GC暂停
            _ = Task.Run( () =>
            {
                while (true)
                {
                    var status = GC.WaitForFullGCApproach();
                    if (status == GCNotificationStatus.Succeeded)
                    {
                        Debug.WriteLine("GC信息: Full GC 即将触发");
                    }
                    Task.Delay(100).Wait();
                }
            });
            try
            {
                // 校验参数（确保传入了完整的ZeroMQ地址）
                if (args.Length == 0 || !args[0].StartsWith("https://"))
                {
                    Debug.WriteLine("错误：未获取到有效的ZeroMQ连接地址（格式：https://IP:Port）");
                    return;
                }
                // 直接读取完整的连接地址（无解析，零GC）
                GrpcAddress = args[0];
                Debug.WriteLine(GrpcAddress);

                //GrpcAddress = "https://localhost:10000";

                // 1. 初始化客户端（服务端地址+设备唯一标识）
                var client = new GrpcClient(GrpcAddress, "数据采集子进程", aD_Controlcs);

                // 2. 启动客户端（建立双向流连接）
                // 阻塞主线程（避免程序退出）
                await client.StartAsync();

                // 3. 释放资源，子进程退出
                client.Dispose();
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
           
        }

      
    }
}
