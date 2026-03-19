using AvaloniaApplication1.Grpc; // 生成的gRPC客户端代码命名空间
using ConsoleApp1.Tools;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.ObjectPool;
using NetMQ;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection.Metadata;
using System.ServiceModel.Channels;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApp1.Service
{
    /// <summary>
    /// gRPC双向流客户端
    /// 核心：建立与服务端的双向流连接，处理服务端指令，返回响应，支持主动上报
    /// </summary>
    public class GrpcClient : IDisposable
    {
        /// <summary>
        /// gRPC通道（客户端与服务端的连接通道）
        /// 单例复用，避免频繁创建/销毁连接
        /// </summary>
        private readonly GrpcChannel channel;

        /// <summary>
        /// gRPC客户端（自动生成）
        /// </summary>
        private readonly GrpcService.GrpcServiceClient client;

        /// <summary>
        /// 客户端/设备唯一标识（与服务端的deviceId对应）
        /// </summary>
        private static string _processId;

        /// <summary>
        /// 双向流的响应写入器（客户端→服务端发送消息）
        /// </summary>
        private IAsyncStreamWriter<AdResponse> _responseStreamWriter;

        /// <summary>
        /// 取消令牌源（控制连接生命周期，主动断开时取消）
        /// </summary>
        private readonly CancellationTokenSource _cts = new();

        /// <summary>
        /// 数据采集控制类实例，负责设备控制与采集逻辑
        /// </summary>
        private static AD_Controlcs aD_Controlcs;

        /// <summary>
        /// 全局单例AdResponse对象池（核心：复用对象）
        /// </summary>
        private static readonly ObjectPool<AdResponse> ResponsePool =
            new DefaultObjectPool<AdResponse>(new AdResponsePoolPolicy(), 30);

        /// <summary>
        /// 主动/被动响应 消息发送队列
        /// </summary>
        private static readonly ConcurrentQueue<AdResponse> ResponseQueue = new ConcurrentQueue<AdResponse>();

        /// <summary>
        /// 构造函数：初始化通道和客户端,并注入数据采集控制器
        /// </summary>
        /// <param name="serverAddress">服务端地址（如http://localhost:5000）</param>
        /// <param name="processId">客户端唯一标识（需与服务端匹配）</param>
        public GrpcClient(string serverAddress, string processId, AD_Controlcs Controlcs)
        {
            // 创建gRPC通道（默认HTTP/2，适配双向流）
            channel = GrpcChannel.ForAddress(serverAddress);
            client = new GrpcService.GrpcServiceClient(channel);
            _processId = processId;
            // 注入数据采集控制器
            aD_Controlcs = Controlcs;
        }

        /// <summary>
        /// 启动客户端：建立双向流连接，持续监听服务端指令
        /// </summary>
        /// <returns>异步任务</returns>
        public async Task StartAsync()
        {
            try
            {
                // 1. 调用服务端的Communicate双向流方法，获取流对象
                var stream = client.Communicate(cancellationToken: _cts.Token);

                // 2. 保存客户端→服务端的响应写入器（用于发送响应/上报）
                _responseStreamWriter = stream.RequestStream;

                // 从队列中获取响应消息，并集中发生给grpc服务端（为了保证_responseStreamWriter.WriteAsync的线程安全）
                var sendResponse = Task.Run(() =>
                {
                    AdResponse response = null;
                    while (!_cts.Token.IsCancellationRequested)
                    {
                        Thread.Sleep(10);
                        if(ResponseQueue.TryDequeue(out response) && response!=null)
                        {
                            //同步阻塞减少线程池调度
                            _responseStreamWriter.WriteAsync(response).Wait();
                            ResponsePool.Return(response);
                        }
                    }
                }, _cts.Token);

                // 3. 启动后台任务：持续读取服务端下发的指令（服务端→客户端）
                var readTask = Task.Run(() =>
                {
                    // 循环读取服务端的指令流（服务端断开前持续监听）
                    while (stream.ResponseStream.MoveNext(_cts.Token).GetAwaiter().GetResult())
                    {
                        // 获取服务端发送的指令
                        var serverCommand = stream.ResponseStream.Current;
                        Console.WriteLine($"客户端[{_processId}]收到服务端指令：RequestId={serverCommand.RequestId}, Command={serverCommand.Command}");

                        // 4. 处理指令并返回响应
                        HandleServerCommand(serverCommand);
                    }
                }, _cts.Token);


                // 4. 先发生准备完成消息，传递子进程ID
                SendMessage("子进程准备完毕");

                // 5. 启动心跳上报后台任务（可选：客户端主动向服务端发送心跳）
                var heartbeatTask = Task.Run(() => SendHeartbeat(_cts.Token));

                // 等待任务完成（或被取消）
                await Task.WhenAll(readTask, heartbeatTask, sendResponse);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"客户端[{_processId}]连接已取消");
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"客户端[{_processId}]gRPC连接异常：{ex.Status.Detail}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"客户端[{_processId}]异常：{ex.Message}");
            }
        }

        /// <summary>
        /// 处理服务端下发的指令，生成并发送响应
        /// </summary>
        /// <param name="request">服务端指令</param>
        /// <returns>异步任务</returns>
        private void HandleServerCommand(AdRequest request)
        {
            var response = ResponsePool.Get();
            response.ResponseId = request.RequestId;// 关联服务端指令的RequestId
            response.MessageType = "command_response";// 标记为指令响应（与服务端逻辑匹配）
            response.ErrorCode = "NONE";// 默认无错误
            response.Content = string.Empty;
            response.ProcessId = _processId; // 进程ID

            try
            {
                // 根据服务端指令类型处理业务逻辑
                switch (request.Command)
                {

                    // 打开采集卡
                    case "OPEN_DEVICE":

                        response.Content = aD_Controlcs.Device_Opened();
                        Console.WriteLine(response.Content);
                        break;

                    // 重新打开采集卡
                    case "OPEN_DEVICE_AGAIN":

                        response.Content = aD_Controlcs.Device_Opened_again();
                        Console.WriteLine(response.Content);
                        break;

                    // 开始采集
                    case "START_AD":

                        aD_Controlcs.start();
                        response.Content = "AD_STARTED";
                        break;

                    // 停止采集
                    case "STOP_AD":

                        aD_Controlcs.stop();
                        response.Content = "AD_STOPPED";
                        break;

                    // 主进程发送心跳检测
                    case "PING":
                        // 回复心跳
                        response.Content = "PONG";
                        break;

                    // 主进程要求退出
                    case "EXIT":

                        Console.WriteLine("收到退出指令，开始优雅退出流程...");
                        // 停止数据采集（确保采集卡停止工作）
                        if (aD_Controlcs.mHandle >= 0)
                        {
                            aD_Controlcs.stop(); // 停止采集
                            USB1602.USB1602_CloseDevice(aD_Controlcs.mHandle); // 关闭采集卡硬件
                            Console.WriteLine("采集卡资源已释放");
                        }

                        // 向服务端发送退出确认响应（确保服务端收到）
                        response.Content = "EXIT_OK";
                        ResponseQueue.Enqueue(response);
                        Console.WriteLine("已向服务端发送退出确认响应");
                        // 触发gRPC任务取消和通道关闭
                        Stop();
                        break;

                    // 未知命令
                    default:

                        response.Content = "UNKNOWN_COMMAND";
                        break;

                }
            }
            catch (Exception ex)
            {
                // 指令处理异常，返回错误信息
                response.MessageType = "ERROR";
                response.ErrorCode = "COMMAND_HANDLE_FAILED";
                response.Content = $"指令处理失败：{ex.Message}";
            }

            // 发送响应到消息队列
            ResponseQueue.Enqueue(response);

        }

        // 复用StringBuilder，避免频繁拼接
        private static readonly StringBuilder sb1 = new();
        /// <summary>
        /// 客户端主动上报心跳（示例：每5秒上报一次）
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>异步任务</returns>
        private void SendHeartbeat(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 复用字符串拼接
                    sb1.Clear();
                    sb1.Append("客户端[").Append(_processId).Append("]心跳 - ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                    var heartbeat = ResponsePool.Get();
                    heartbeat.ResponseId = Guid.NewGuid().ToString("N");// 随机ID
                    heartbeat.MessageType = "data_report";// 标记为数据上报（与服务端逻辑匹配）
                    heartbeat.ErrorCode = "NONE";// 默认无错误
                    heartbeat.Content = sb1.ToString();
                    heartbeat.ProcessId = _processId; // 进程ID

                    // 发送心跳到消息队列
                    ResponseQueue.Enqueue(heartbeat);
                    Task.Delay(5000, cancellationToken).Wait(); // 5秒间隔
                }
                catch (OperationCanceledException)
                {
                    break;
                }   
                catch (Exception ex)
                {
                    Console.WriteLine($"心跳上报失败：{ex.Message}");
                    Task.Delay(1000, cancellationToken).Wait(); // 失败后重试间隔
                }
            }
        }


        // 复用StringBuilder，避免频繁拼接
        private static readonly StringBuilder sb2 = new();
        /// <summary>
        /// 客户端主动上报消息
        /// </summary>
        /// <param name="message">消息内容</param>
        /// <returns></returns>
        public static void SendMessage(string message)
        {
            try
            {
                // 复用字符串拼接
                sb2.Clear();
                sb2.Append("客户端[").Append(_processId).Append("]消息 -").Append(message);

                var heartbeat = ResponsePool.Get();
                heartbeat.ResponseId = Guid.NewGuid().ToString("N"); // 随机ID
                heartbeat.MessageType = "data_report"; // 标记为数据上报（与服务端逻辑匹配）
                heartbeat.Content = sb2.ToString();
                heartbeat.ErrorCode = "NONE";
                heartbeat.ProcessId = _processId;

                // 发送主动上报消息到消息队列
                ResponseQueue.Enqueue(heartbeat);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"错误：{ex.Message}");
            }
        }


        // 复用StringBuilder，避免频繁拼接
        private static readonly StringBuilder sb3 = new();
        /// <summary>
        /// 客户端主动上报错误消息
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public static void SendErrorMessage(string message)
        {
            try
            {
                // 复用字符串拼接
                sb3.Clear();
                sb3.Append("客户端[").Append(_processId).Append("]错误消息 -").Append(message);

                var heartbeat = ResponsePool.Get();

                heartbeat.ResponseId = Guid.NewGuid().ToString("N"); // 随机ID
                heartbeat.MessageType = "Error";// 标记为数据上报（与服务端逻辑匹配）
                heartbeat.Content = sb3.ToString();
                heartbeat.ErrorCode = "NONE";
                heartbeat.ProcessId = _processId;

                // 发送错误消息到消息队列
                ResponseQueue.Enqueue(heartbeat);

            }
            catch (Exception ex)
            {
                Console.WriteLine($"错误：{ex.Message}");
            }
        }

        /// <summary>
        /// 停止客户端：取消令牌，关闭通道
        /// </summary>
        /// <returns></returns>
        public void Stop()
        {
            _cts.Cancel(); // 取消所有异步任务
            Task.Delay(1000).Wait();
            channel.ShutdownAsync().Wait(); // 关闭gRPC通道
            Console.WriteLine($"客户端[{_processId}]已停止");
        }

        /// <summary>
        /// 新增：外部触发退出（可选）
        /// </summary>
        /// <returns></returns>
        public void ExitGracefully()
        {
            Console.WriteLine("收到退出指令，开始优雅退出流程...");
            // 停止数据采集（确保采集卡停止工作）
            if (aD_Controlcs.mHandle >= 0)
            {
                aD_Controlcs.stop(); // 停止采集
                USB1602.USB1602_CloseDevice(aD_Controlcs.mHandle); // 关闭采集卡硬件
                Console.WriteLine("采集卡资源已释放");
            }
            // 向服务端发送退出确认响应（确保服务端收到）
            SendMessage("EXIT_OK");
            Console.WriteLine("已向服务端发送退出确认响应");
            // 触发gRPC任务取消和通道关闭
            Stop();
            Dispose();
        }


        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _cts.Dispose();
            channel.Dispose();
            // 释放采集卡资源（兜底）
            if (aD_Controlcs.mHandle >= 0)
            {
                USB1602.USB1602_CloseDevice(aD_Controlcs.mHandle);
            }
        }
    }

    /// <summary>
    /// Response对象池
    /// </summary>
    public class AdResponsePoolPolicy : IPooledObjectPolicy<AdResponse>
    {

        /// <summary>
        /// 池中空闲对象不足时，创建新AdResponse实例
        /// </summary>
        public AdResponse Create()
        {
            // 初始化默认值，避免空引用/脏数据
            return new AdResponse
            {
                ResponseId = string.Empty,
                MessageType = string.Empty,
                ErrorCode = "NONE", // 默认无错误
                Content = string.Empty,
                ProcessId = string.Empty
            };
        }

        /// <summary>
        /// 对象使用完毕归还时，重置状态（核心：避免脏数据复用）
        /// </summary>
        /// <param name="obj">待回收的AdResponse对象</param>
        /// <returns>是否允许回收（true=回收，false=丢弃）</returns>
        public bool Return(AdResponse obj)
        {
            // 防御性检查：空对象直接丢弃
            if (obj == null) return false;

            // 重置所有字段为初始状态
            obj.ResponseId = string.Empty;
            obj.MessageType = string.Empty;
            obj.ErrorCode = "NONE";
            obj.Content = string.Empty;
            obj.ProcessId = string.Empty;

            return true; // 允许回收至对象池
        }
    }

}