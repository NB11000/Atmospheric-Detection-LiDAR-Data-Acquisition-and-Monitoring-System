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
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace ConsoleApp1
{
    internal class Program
    {
        // 数据采集控制类实例，负责设备控制与采集逻辑
        private static AD_Controlcs aD_Controlcs = new AD_Controlcs();
        // 记录最后一次收到主进程消息的时间
        private static DateTime lastHeartbeat = DateTime.Now;
        // 全局复用字符串（避免每次创建新实例）
        private static string Error_msg;
        private static string command = null;

        private static string zmqAddress;
        private static volatile bool True_False = true;

        /// <summary>
        /// 客户端入口（控制台/UI程序均可）
        /// 用于接受主进程的调用命令，并反馈结果,以及上报数据
        /// </summary>
        static async Task Main(string[] args)
        {
            // 1. 初始化客户端（服务端地址+设备唯一标识）
            var client = new GrpcClient("http://localhost:5000", "card_001", aD_Controlcs);

            // 2. 启动客户端（建立双向流连接）
            // 阻塞主线程（避免程序退出）
            await client.StartAsync();

            // 3. 释放资源，子进程退出
            client.Dispose();
            Environment.Exit(0);
        }

      
    }
}
