using AvaloniaApplication1.Grpc;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
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
using WebAPI;
using WebAPI.Controllers;
using WebAPI.Models;
using WebAPI.Tools;
using static System.Runtime.InteropServices.JavaScript.JSType;

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

        private ILogger<GrpcServiceImpl> _logger;
        private readonly IServiceProvider _serviceProvider;

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
        /// 存储 requestId 与原始命令名称的映射
        /// 用于 command_response 到达时查找对应的命令，以推断状态变更
        /// </summary>
        private readonly ConcurrentDictionary<string, string> _requestCommandMap = new();

        /// <summary>
        /// MainWindow的视图模型实例（用于UI交互，注意线程安全）
        /// </summary>
        //private readonly MainWindowViewModel _vm;

        /// <summary> 
        /// 系统状态服务实例（用于发布状态变更事件，注意单例共享）
        /// </summary>
        private readonly SystemStateService _stateService;

        /// <summary>
        /// SignalR统一推送服务实例（用于推送消息到前端，保留作为兼容过渡通道）
        /// </summary>
        private readonly SignalRHubPublisher _hubPublisher;

        /// <summary>
        /// MQTT 事件发布器，替代 SignalR 作为主事件推送通道
        /// </summary>
        private readonly MqttEventPublisher _mqttEventPublisher;

        /// <summary>
        /// 检测发布服务，接收结构化检测告警并发布到 MQTT
        /// </summary>
        private readonly DetectionPublisherService _detectionPublisher;

        public GrpcServiceImpl(ILogger<GrpcServiceImpl> logger, IServiceProvider serviceProvider,
        SystemStateService stateService, SignalRHubPublisher hubPublisher, MqttEventPublisher mqttEventPublisher,
        DetectionPublisherService detectionPublisher)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _stateService = stateService;
            _hubPublisher = hubPublisher;
            _mqttEventPublisher = mqttEventPublisher;
            _detectionPublisher = detectionPublisher;
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
                        _logger.LogInformation($"客户端[{processId}]已连接");

                        // 如果是采集子进程连接，发布状态变更事件
                        if (processId == SystemStateService.CollectorClientId)
                        {
                            try
                            {
                                // 初始化缓存：标记进程已连接，硬件状态待后续命令响应填充
                                _stateService.UpdateCollectorState(_ => new CollectorStateDto
                                {
                                    ProcessConnected = true,
                                    DeviceOpened = false,
                                    Acquiring = false,
                                    Handle = 0,
                                    LastMessage = "采集子进程已连接，等待设备操作"
                                });

                                // MQTT 主通道：推送采集子进程连接事件（异步不等待）
                                _ = _mqttEventPublisher.PublishStateChangedAsync(
                                    "collector_connected",
                                    "collector",
                                    "采集子进程已连接",
                                    "数据采集子进程连接已建立");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "发布采集子进程连接事件失败");
                            }
                        }

                        // 客户端首次连接后，向客户端发送打开采集设备指令
/*                         Dispatcher.UIThread.Post(async () =>
                        {
                            AdResponse msg = await SendCommandToClientAndWaitResponse(processId, "OPEN_DEVICE");
                            MainWindow.mHandle = (IntPtr)msg.MHandle;
                            _logger.LogError($"客户端[{processId}]获取设备句柄：{MainWindow.mHandle}");
                            _vm.Status = msg.Content;
                        }); */
                    }

                    // 消息类型1：数据上报（客户端的主动上报的消息）
                    if (clientMsg.MessageType == "data_report")
                    {
                        _logger.LogInformation($"收到[{processId}]消息：{clientMsg.Content}");

                        // ===== 新增：处理采集子进程的状态类 data_report =====
                        if (processId == SystemStateService.CollectorClientId)
                        {
                            UpdateStateFromDataReport(clientMsg);
                        }

                        // 服务端上传 发布状态变更事件
/*                         await _hubPublisher.PublishStateChangedAsync(
                            clientMsg.MessageType,
                            "collector",
                            "数据上报",
                            clientMsg.Content); */
                    }
                    // 消息类型2：错误消息（客户端的主动上报的错误消息）
                    else if (clientMsg.MessageType == "Error")
                    {
                        _logger.LogError($"收到[{processId}]错误消息：{clientMsg.Content}");

                        // ===== 新增：根据错误码更新采集卡状态 =====
                        if (processId == SystemStateService.CollectorClientId)
                        {
                            UpdateStateFromError(clientMsg);
                        }
                        // UI交互仅通过Dispatcher异步投递，避免阻塞通信线程
/*                         Dispatcher.UIThread.Post(() =>
                        {
                            _vm.Status = $"收到[{processId}]错误消息：{clientMsg.Content}";
                        }); */

                        // MQTT 主通道：推送错误事件（异步不等待，SignalR 保留作为兼容通道）
                        _ = _mqttEventPublisher.PublishStateChangedAsync(
                            clientMsg.MessageType,
                            "collector",
                            "数据采集子进程主动上报的错误消息",
                            clientMsg.Content);

                        // SignalR 兼容通道：发布状态变更事件
                        await _hubPublisher.PublishStateChangedAsync(
                            clientMsg.MessageType,
                            "collector",
                            "数据采集子进程主动上报的错误消息",
                            clientMsg.Content); 

                    }
                    // 消息类型3：检测告警（Detection 线程的结构化告警上报）
                    else if (clientMsg.MessageType == "Detection")
                    {
                        try
                        {
                            var alert = System.Text.Json.JsonSerializer.Deserialize<WebAPI.Models.DetectionAlertDto>(
                                clientMsg.Content);
                            if (alert != null)
                                _detectionPublisher.OnAlertReceived(alert);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "解析 Detection 告警失败: {Content}", clientMsg.Content);
                        }
                    }

                    // 消息类型4：指令响应（客户端处理完服务端指令后返回的结果）
                    else if (clientMsg.MessageType == "command_response")
                    {
                        _logger.LogInformation($"收到[{processId}]指令[{clientMsg.ResponseId}]响应：{clientMsg.Content}");

                        // 找到该指令对应的等待器，并设置响应结果（唤醒阻塞的指令发送线程）
                        if (_commandResponseWaiters.TryRemove(clientMsg.ResponseId, out var tcs))
                        {
                            tcs.SetResult(clientMsg);
                        }

                        // ===== 新增：根据命令响应推断并更新采集卡状态 =====
                        if (processId == SystemStateService.CollectorClientId
                            && _requestCommandMap.TryRemove(clientMsg.ResponseId, out var originalCommand))
                        {
                            UpdateStateFromCommandResponse(originalCommand, clientMsg);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 捕获客户端连接过程中的异常（如网络断开、消息解析失败等）
                _logger.LogError($"客户端[{processId}]连接异常：{ex.Message}");
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
                    _logger.LogInformation($"客户端[{processId}]已断开");

                    // 如果是采集子进程断开，发布状态变更事件（异步不等待）
                    _ = PublishCollectorDisconnectedAsync(processId);
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

            // 新增：记录 requestId 与命令的映射关系
            _requestCommandMap.TryAdd(requestId, command);

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
                _logger.LogInformation($"向[{processId}]进程发送指令[{requestId}]：{command}");

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
                _requestCommandMap.TryRemove(requestId, out _); // 同步清理映射
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
        /// 异步发布采集子进程断开事件（不等待）
        /// </summary>
        private async Task PublishCollectorDisconnectedAsync(string processId)
        {
            if (processId == SystemStateService.CollectorClientId)
            {
                try
                {
                    // 重置缓存为默认值（兜底保障）
                    _stateService.ResetCollectorState();

                    // MQTT 主通道：推送采集子进程断开事件（异步不等待）
                    _ = _mqttEventPublisher.PublishStateChangedAsync(
                        "collector_disconnected",
                        "collector",
                        "采集子进程已断开",
                        "数据采集子进程连接已断开");

                    // SignalR 兼容通道
                    await _hubPublisher.PublishStateChangedAsync(
                        "collector_disconnected",
                        "collector",
                        "采集子进程已断开",
                        "数据采集子进程连接已断开");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "发布采集子进程断开事件失败");
                }
            }
        }

        /// <summary>
        /// 根据命令响应推断并更新采集卡状态缓存
        /// </summary>
        /// <param name="command">原始命令名称</param>
        /// <param name="response">子进程返回的响应</param>
        private void UpdateStateFromCommandResponse(string command, AdResponse response)
        {
            try
            {

                // 如果命令处理失败，不更新硬件状态，仅记录错误信息
                if (response.ErrorCode == "COMMAND_HANDLE_FAILED")
                {
                    _stateService.UpdateCollectorState(current => new CollectorStateDto
                    {
                        ProcessConnected = current.ProcessConnected,
                        DeviceOpened = current.DeviceOpened,
                        Acquiring = current.Acquiring,
                        Handle = current.Handle,
                        LastMessage = $"命令[{command}]执行失败：{response.Content}"
                    });
                    return;
                }

                switch (command)
                {
                    case "OPEN_DEVICE":
                    case "OPEN_DEVICE_AGAIN":
                        _stateService.UpdateCollectorState(current => new CollectorStateDto
                        {
                            ProcessConnected = true,
                            DeviceOpened = response.MHandle > 0,
                            Acquiring = current.Acquiring,
                            Handle = response.MHandle,
                            LastMessage = response.Content
                        });
                        break;

                    case "CLOSE_DEVICE":
                        _stateService.UpdateCollectorState(current => new CollectorStateDto
                        {
                            ProcessConnected = true,
                            DeviceOpened = false,
                            Acquiring = false,
                            Handle = 0,
                            LastMessage = response.Content
                        });
                        break;

                    case "START_AD":
                        _stateService.UpdateCollectorState(current => new CollectorStateDto
                        {
                            ProcessConnected = true,
                            DeviceOpened = current.DeviceOpened,
                            Acquiring = response.Content == "AD_STARTED",
                            Handle = current.Handle,
                            LastMessage = response.Content == "AD_STARTED" ? "正在采集" : response.Content
                        });
                        break;

                    case "STOP_AD":
                        _stateService.UpdateCollectorState(current => new CollectorStateDto
                        {
                            ProcessConnected = true,
                            DeviceOpened = current.DeviceOpened,
                            Acquiring = false,
                            Handle = current.Handle,
                            LastMessage = "已停止采集"
                        });
                        break;

                    case "EXIT":
                        _stateService.ResetCollectorState();
                        break;

                    // CONFIG_UPDATE、CONFIG_READ、PING、GET_COLLECTOR_STATE 等不影响硬件状态
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据命令响应更新采集卡状态失败");
            }
        }

        /// <summary>
        /// 根据 data_report 主动上报消息更新采集卡状态
        /// </summary>
        private void UpdateStateFromDataReport(AdResponse report)
        {
            try
            {
                var content = report.Content ?? string.Empty;

                // 识别结构化状态消息（预留扩展：以 [STATE] 前缀标识）
                if (content.Contains("[STATE]"))
                {
                    // 提取 JSON 部分并解析（未来扩展）
                    // var json = content.Substring(content.IndexOf("[STATE]") + 7);
                    // var state = JsonSerializer.Deserialize<CollectorStateDto>(json);
                    // stateService.UpdateCollectorState(_ => state);
                }
                else
                {
                    // 普通消息：仅更新 LastMessage
                    _stateService.UpdateCollectorState(current => new CollectorStateDto
                    {
                        ProcessConnected = current.ProcessConnected,
                        DeviceOpened = current.DeviceOpened,
                        Acquiring = current.Acquiring,
                        Handle = current.Handle,
                        LastMessage = content
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理 data_report 状态更新失败");
            }
        }

        /// <summary>
        /// 根据错误消息推断并更新采集卡状态
        /// </summary>
        private void UpdateStateFromError(AdResponse errorMsg)
        {
            try
            {
                var errorCode = errorMsg.ErrorCode ?? "NONE";

                switch (errorCode)
                {
                    case "DEVICE_DISCONNECTED":
                        // 设备意外断开，全部硬件状态重置
                        _stateService.UpdateCollectorState(current => new CollectorStateDto
                        {
                            ProcessConnected = true, // 子进程本身还在
                            DeviceOpened = false,
                            Acquiring = false,
                            Handle = 0,
                            LastMessage = $"设备异常断开：{errorMsg.Content}"
                        });
                        break;

                    case "ACQUISITION_FAILED":
                        // 采集异常终止
                        _stateService.UpdateCollectorState(current => new CollectorStateDto
                        {
                            ProcessConnected = true,
                            DeviceOpened = current.DeviceOpened,
                            Acquiring = false,
                            Handle = current.Handle,
                            LastMessage = $"采集异常终止：{errorMsg.Content}"
                        });
                        break;

                    case "DEVICE_OPEN_FAILED":
                        // 设备打开失败
                        _stateService.UpdateCollectorState(current => new CollectorStateDto
                        {
                            ProcessConnected = true,
                            DeviceOpened = false,
                            Acquiring = false,
                            Handle = 0,
                            LastMessage = $"设备打开失败：{errorMsg.Content}"
                        });
                        break;

                    default:
                        // 一般性错误或未分类错误，仅更新 LastMessage
                        _stateService.UpdateCollectorState(current => new CollectorStateDto
                        {
                            ProcessConnected = current.ProcessConnected,
                            DeviceOpened = current.DeviceOpened,
                            Acquiring = current.Acquiring,
                            Handle = current.Handle,
                            LastMessage = $"错误：{errorMsg.Content}"
                        });
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理错误消息状态更新失败");
            }
        }

        /// <summary>
        /// 获取当前grpc服务已连接的客户端列表（用于UI展示或管理）
        /// </summary>
        /// <returns>客户端ID数组</returns>
        public string[] GetConnectedClients() => _clientStreams.Keys.ToArray();
    }
}
