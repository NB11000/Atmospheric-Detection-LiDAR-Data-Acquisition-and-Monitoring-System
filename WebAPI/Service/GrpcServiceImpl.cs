using AvaloniaApplication1.Grpc;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;
using WebAPI;
using WebAPI.Tools;

namespace WebAPI.Service
{
    /// <summary>
    /// gRPC CollectorService 服务实现类
    /// 核心功能：处理客户端流式连接、管理客户端连接状态、向客户端发送指令并同步等待响应
    /// 注：所有指令发送均需等待客户端响应（超时/取消/成功）
    /// </summary>
    public class GrpcServiceImpl : GrpcService.GrpcServiceBase
    {
        /// <summary>
        /// 存储已连接客户端的流式写入器
        /// Key：设备/客户端唯一标识（processId）
        /// Value：服务端向客户端发送消息的流式写入器
        /// 线程安全：使用ConcurrentDictionary保证多线程下的增删改查安全
        /// </summary>
        public ConcurrentDictionary<string, IServerStreamWriter<AdRequest>> _clientStreams = new();

        /// <summary>
        /// 存储指令的响应等待器（用于同步等待客户端指令响应）
        /// Key：指令唯一标识（requestId）
        /// Value：指令响应的任务完成源，用于阻塞等待客户端响应并接收结果
        /// 
        /// 基于「唯一指令 ID（匹配指令和响应） + TaskCompletionSource（TCS）异步等待器（阻塞等待） + 双向流长连接（通信通道）」，
        /// 实现 “发送指令→阻塞等待→响应唤醒” 的闭环。
        /// </summary>
        private readonly ConcurrentDictionary<string, TaskCompletionSource<AdResponse>> _commandResponseWaiters = new();

        /// <summary>
        /// MainWindow的视图模型实例（用于UI交互，注意线程安全）
        /// </summary>
        //private readonly MainWindowViewModel _vm;

        public GrpcServiceImpl()
        {

        }

        /// <summary>
        /// 重写gRPC流式连接方法（客户端持续连接，双向通信入口）
        /// 客户端通过该方法建立长连接，上报数据/返回指令响应；服务端通过该连接发送指令
        /// </summary>
        /// <param name="responseStream">客户端向服务端发送消息的流式读取器</param>
        /// <param name="requestStream">服务端向客户端发送消息的流式写入器</param>
        /// <param name="context">gRPC调用上下文（包含元数据、取消令牌等）</param>
        /// <returns>异步任务</returns>
        public override async Task Communicate(IAsyncStreamReader<AdResponse> responseStream,
                        IServerStreamWriter<AdRequest> requestStream, ServerCallContext context)
        {
            // 客户端唯一标识（初始化时为null，首次接收消息后赋值）
            string processId = null;
            try
            {
                // 循环读取客户端发送的流式消息（客户端断开前持续监听）
                while (await responseStream.MoveNext(CancellationToken.None))
                {
                    // 获取当前客户端发送的消息
                    AdResponse clientMsg = responseStream.Current;

                    // 初始化客户端ID：优先使用客户端上报的deviceId，无则自动生成唯一标识
                    processId = clientMsg.ProcessId ?? $"client_{Guid.NewGuid():N}";

                    // 客户端首次连接：注册流式写入器到连接池
                    if (!_clientStreams.ContainsKey(processId))
                    {
                        _clientStreams.TryAdd(processId, requestStream);
                        Program.logger.LogInformation($"客户端[{processId}]已连接");

                        // 客户端首次连接后，向客户端发送打开采集设备指令
/*                         Dispatcher.UIThread.Post(async () =>
                        {
                            AdResponse msg = await SendCommandToClientAndWaitResponse(processId, "OPEN_DEVICE");
                            MainWindow.mHandle = (IntPtr)msg.MHandle;
                            Program.logger.LogError($"客户端[{processId}]获取设备句柄：{MainWindow.mHandle}");
                            _vm.Status = msg.Content;
                        }); */
                    }

                    // 消息类型1：数据上报（客户端的主动上报的消息）
                    if (clientMsg.MessageType == "data_report")
                    {
                        Program.logger.LogInformation($"收到[{processId}]消息：{clientMsg.Content}");
                    }
                    // 消息类型2：错误消息（客户端的主动上报的错误消息）
                    else if (clientMsg.MessageType == "Error")
                    {
                        Program.logger.LogError($"收到[{processId}]错误消息：{clientMsg.Content}");
                        // UI交互仅通过Dispatcher异步投递，避免阻塞通信线程
/*                         Dispatcher.UIThread.Post(() =>
                        {
                            _vm.Status = $"收到[{processId}]错误消息：{clientMsg.Content}";
                        }); */

                    }
                    // 消息类型3：指令响应（客户端处理完服务端指令后返回的结果）
                    else if (clientMsg.MessageType == "command_response")
                    {
                        Program.logger.LogInformation($"收到[{processId}]指令[{clientMsg.ResponseId}]响应：{clientMsg.Content}");

                        // 找到该指令对应的等待器，并设置响应结果（唤醒阻塞的指令发送线程）
                        if (_commandResponseWaiters.TryRemove(clientMsg.ResponseId, out var tcs))
                        {
                            tcs.SetResult(clientMsg);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 捕获客户端连接过程中的异常（如网络断开、消息解析失败等）
                Program.logger.LogError($"客户端[{processId}]连接异常：{ex.Message}");
                // UI交互仅通过Dispatcher异步投递，避免阻塞通信线程
/*                 Dispatcher.UIThread.Post(() =>
                {
                    _vm.Status = $"客户端[{processId}]连接异常：{ex.Message}";
                }); */

            }
            finally
            {
                // 客户端断开连接：从连接池移除该客户端的流式写入器
                if (processId != null && _clientStreams.ContainsKey(processId))
                {
                    _clientStreams.TryRemove(processId, out _);
                    Program.logger.LogInformation($"客户端[{processId}]已断开");
                }
            }
        }

        /// <summary>
        /// 向指定客户端发送指令，并同步等待客户端响应（核心业务方法）
        /// 包含超时控制（10秒）、取消令牌联动、自动清理等待器等逻辑
        /// </summary>
        /// <param name="processId">客户端/设备唯一标识</param>
        /// <param name="cancellationToken">取消令牌（外部可通过该令牌主动取消等待）</param>
        /// <returns>客户端返回的指令处理结果</returns>
        /// <exception cref="Exception">客户端未连接时抛出</exception>
        /// <exception cref="TimeoutException">等待响应超时时抛出（10秒）</exception>
        public async Task<AdResponse> SendCommandToClientAndWaitResponse(
            string processId ,
            string command ,
            CancellationToken cancellationToken = default)
        {
            // 校验客户端是否在线：从连接池获取服务端向该客户端发送消息的流式写入器
            if (!_clientStreams.TryGetValue(processId, out var streamWriter))
            {
                throw new Exception($"客户端[{processId}]未连接");
            }

            // 步骤1：生成唯一指令ID，创建响应等待器（用于阻塞等待客户端响应）
            string requestId = Guid.NewGuid().ToString("N");
            var tcs = new TaskCompletionSource<AdResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

            // 步骤2：注册等待器到全局容器（客户端响应时会通过commandId找到该等待器）
            _commandResponseWaiters.TryAdd(requestId, tcs);

            // 步骤3：设置超时令牌（10秒超时），并关联外部取消令牌
            using var timeoutToken = new CancellationTokenSource(TimeSpan.FromSeconds(10)); // 10秒超时
            using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutToken.Token);

            try
            {
                // 构造gRPC指令对象
                var cmd = new AdRequest
                {
                    RequestId = requestId, // 指令id
                    Command = command // 指令
                };

                // 向客户端发送指令（流式写入）
                await streamWriter.WriteAsync(cmd);
                Program.logger.LogInformation($"向[{processId}]进程发送指令[{requestId}]：{command}");

                // 步骤4：阻塞等待客户端响应（直到收到响应/超时/被取消）
                // 注册取消回调：当令牌触发取消时，标记等待器为取消状态
                using (linkedToken.Token.Register(() => tcs.TrySetCanceled()))
                {
                    return await tcs.Task; // 等待客户端响应，响应结果由Connect方法中的command_response分支设置
                }
            }
            catch (OperationCanceledException)
            {
                // 捕获取消/超时异常，转换为超时异常抛出
                throw new TimeoutException($"等待客户端[{processId}]响应超时（10秒）");
            }
            finally
            {
                // 最终清理：无论指令发送成功/失败/超时，都移除该指令的等待器（避免内存泄漏）
                _commandResponseWaiters.TryRemove(requestId, out _);
            }
        }


        /// <summary>
        /// 简化版指令发送方法（无返回值重载，内部调用带等待的核心方法）
        /// 适用于不需要处理响应结果、仅需发送指令的场景
        /// </summary>
        /// <param name="deviceId">客户端/设备唯一标识</param>
        /// <param name="commandType">指令类型</param>
        /// <param name="collectInterval">采集间隔（毫秒）</param>
        /// <returns>异步任务</returns>
        public async Task SendCommandToClient(string deviceId, string command)
        {
            await SendCommandToClientAndWaitResponse(deviceId, command);
        }




        /// <summary>
        /// 获取当前所有已连接的客户端ID列表
        /// </summary>
        /// <returns>客户端ID数组</returns>
        public string[] GetConnectedClients() => _clientStreams.Keys.ToArray();
    }
}
