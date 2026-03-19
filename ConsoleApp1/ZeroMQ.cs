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
    internal class ZeroMQ
    {
        // 数据采集控制类实例，负责设备控制与采集逻辑
        private static AD_Controlcs aD_Controlcs = new AD_Controlcs();
        // NetMQ通信Socket(客户端)
        private static DealerSocket socket;
        // 记录最后一次收到主进程消息的时间
        private static DateTime lastHeartbeat = DateTime.Now;
        // 全局复用字符串（避免每次创建新实例）
        private static string Error_msg;
        private static string command = null;

        private static string zmqAddress;
        private static volatile bool True_False = true;

        /// <summary>msg
        /// (主线程)子进程通信线程()；
        /// 用于接受主进程的调用命令，并反馈结果
        /// </summary>
        static void aain(string[] args)
        {
            // 校验参数（确保传入了完整的ZeroMQ地址）
            if (args.Length == 0 || !args[0].StartsWith("tcp://"))
            {
                Debug.WriteLine("错误：未获取到有效的ZeroMQ连接地址（格式：tcp://IP:Port）");
                return;
            }
            // 直接读取完整的连接地址（无解析，零GC）
            zmqAddress = args[0];
            try
            {
                // 创建DealerSocket（子进程通常使用Dealer）
                socket = new DealerSocket();

                // 设置子进程身份ID（Router端会根据Identity识别不同子进程）
                socket.Options.Identity = Encoding.UTF8.GetBytes("AD_PROCESS");
                // Socket 断开时不等待未发送的数据，避免阻塞
                socket.Options.Linger = TimeSpan.Zero;
                // 连接主进程（主进程一般使用Router并bind端口）
                socket.Connect(zmqAddress);

                // 通知主进程：子进程已经启动
                socket.SendFrame("READY");

                Debug.WriteLine("子进程已启动");

                // 进入控制循环
                ControlLoop();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"连接主进程失败：{ex.Message}");
                // 释放资源
                socket?.Dispose();
                socket = null;
                Environment.Exit(1);
            }
        }

        /// <summary>
        /// 主控制循环
        /// 持续监听来自主进程的控制指令,并向主进程上传信息
        /// </summary>
        static void ControlLoop()
        {
            while (True_False)
            {
                // 上传错误信息
                if (AD_Controlcs.Errorchannel.Reader.TryRead(out Error_msg))
                {
                    SendResponseWithType("ERROR", Error_msg);
                }
                // 尝试接收指令（最多等待1秒）
                // 这样线程不会忙轮询，同时也可以周期性执行一些检查逻辑
                if (socket.TryReceiveFrameString(TimeSpan.FromSeconds(1), out command))
                {
                    // 更新最后一次通信时间
                    lastHeartbeat = DateTime.Now;
                    if (command != "PING")
                        Console.WriteLine($"收到主进程指令: {command}");//日志
                    HandleCommand(command);
                }
                else
                {
                    // 每秒执行一次检测心跳
                    CheckHeartbeat();
                }
            }
        }

        private static string response;
        /// <summary>
        /// 处理主进程发送的控制命令
        /// </summary>
        static void HandleCommand(string command)
        {
            switch (command)
            {
                // 打开采集卡
                case "OPEN_DEVICE":

                    response = aD_Controlcs.Device_Opened();
                    //socket.SendFrame(aD_Controlcs.Device_Opened());
                    SendResponseWithType("RESPONSE", response);
                    break;

                // 重新打开采集卡
                case "OPEN_DEVICE_AGAIN":

                    response = aD_Controlcs.Device_Opened_again();
                    //socket.SendFrame(aD_Controlcs.Device_Opened_again());
                    SendResponseWithType("RESPONSE", response);
                    break;

                // 开始采集
                case "START_AD":

                    aD_Controlcs.start();
                    socket.SendFrame("ACQ_STARTED");
                    break;

                // 停止采集
                case "STOP_AD":

                    aD_Controlcs.stop();
                    socket.SendFrame("ACQ_STOPPED");
                    break;

                // 主进程发送心跳检测
                case "PING":
                    // 回复心跳
                    socket.SendFrame("PONG");
                    break;

                // 主进程要求退出
                case "EXIT":

                    socket.SendFrame("EXIT_OK");
                    Console.WriteLine("收到退出指令，子进程准备退出");
                    //主线程退出循环
                    True_False = false;
                    // 新增：释放ZeroMQ资源
                    socket?.Disconnect(zmqAddress);
                    socket?.Close();
                    NetMQConfig.Cleanup(false); // 清理NetMQ上下文

                    //socket?.Dispose();
                    //socket = null;

                    if (aD_Controlcs.mHandle < 0)
                    {
                        Environment.Exit(0);
                        break;
                    }
                    else
                    {
                        aD_Controlcs.stop();
                        USB1602.USB1602_CloseDevice(aD_Controlcs.mHandle);//关闭设备
                        Environment.Exit(0);
                        break;
                    }

                // 未知命令
                default:

                    socket.SendFrame("UNKNOWN_COMMAND");
                    break;
            }
        }

        /// <summary>
        /// 检查心跳
        /// 如果长时间没有收到主进程消息，可以做安全处理
        /// </summary>
        static void CheckHeartbeat()
        {
            var diff = DateTime.Now - lastHeartbeat;

            // 如果10秒没有收到主进程消息
            if (diff.TotalSeconds > 10)
            {
                Debug.WriteLine("警告：超过10秒未收到主进程消息");

                // 这里可以选择：
                // 1 自动停止采集
                // 2 自动退出
                // 3 尝试重新连接
            }
        }


        private static NetMQMessage msg = new NetMQMessage();
        /// <summary>
        /// 子进程发送带标识的多帧消息
        /// 发送复杂信息
        /// 子进程发送消息：帧0=WorkerId，帧1=消息类型（INFO/WARN/ERROR/RESPONSE），帧2=消息内容
        /// </summary>
        /// <param name="msgType"></param>
        /// <param name="content"></param>
        private static void SendResponseWithType(string msgType, string content)
        {
            msg.Append(socket.Options.Identity);       // 帧0：WorkerId（主进程识别子进程）
            msg.Append(msgType);         // 帧1：消息类型标识（INFO/WARN/ERROR）
            msg.Append(content);         // 帧2：消息内容
            socket.SendMultipartMessage(msg);
            msg.Clear(); //清空当前NetMQMessage的帧数据，为下一次接收做准备(重置消息对象，复用)
        }



    }
}
