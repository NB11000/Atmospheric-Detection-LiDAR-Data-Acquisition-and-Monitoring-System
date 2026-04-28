using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;
using WebAPI.Models;
using WebAPISharedMemoryFramework;

namespace WebAPI.Service
{
    /// <summary>
    /// MQTT RPC 后台服务 — 托管 MQTT 客户端完整生命周期
    /// </summary>
    public class MqttRpcBackgroundService : BackgroundService
    {
        private const string RpcTopicPrefix = "$rpc/";
        private const int WaveformFramePoints = 1000;
        private const int WaveformFrameBytes = WaveformFramePoints * sizeof(double);

        // ── 依赖注入 ──────────────────────────────────────────
        private readonly IServiceProvider _serviceProvider;
        private readonly IOptionsMonitor<MqttSettings> _mqttSettings;
        private readonly MqttEventPublisher _eventPublisher;
        private readonly ILogger<MqttRpcBackgroundService> _logger;

        // ── RPC 方法路由（启动时构建一次） ──────────────────────
        private readonly Dictionary<string, Func<byte[], Task<byte[]>>> _rpcHandlers;

        // ── 预计算的 MQTT 主题字符串 ────────────────────────────
        private readonly string _rpcRoutePrefix;
        private readonly string _rpcSubscribePattern;
        private readonly string _waveformCh1Topic;
        private readonly string _waveformCh2Topic;

        // ── 波形写入缓冲（双通道 1000×double，写入后 BlockCopy 至 _ch1Bytes / _ch2Bytes）──
        private double[] _waveformBuf1;
        private double[] _waveformBuf2;

        // ── 波形发布复用缓冲区（从 ArrayPool 租用，服务停止时归还）──
        private byte[] _ch1Bytes;
        private byte[] _ch2Bytes;

        /// <summary>
        /// ── 遗嘱消息负载（进程意外退出时 Broker 代为发布）───────
        /// </summary>
        private readonly byte[] _willPayloadBytes;

        // ── 波形消息模板（Topic + QOS 固定，每帧仅换 Payload）────
        private MqttApplicationMessage? _ch1Template;
        private MqttApplicationMessage? _ch2Template;

        // ── 预构建的 RPC 错误响应片段（运行时拼入方法名）──────────
        private static readonly byte[] _errUnknownMethodHead =
            Encoding.UTF8.GetBytes("{\"success\":false,\"code\":\"UNKNOWN_METHOD\",\"message\":\"未知的 RPC 方法: ");
        private static readonly byte[] _errUnknownMethodTail = Encoding.UTF8.GetBytes("\"}");
        private static readonly byte[] _errHandlerExceptionHead =
            Encoding.UTF8.GetBytes("{\"success\":false,\"code\":\"HANDLER_EXCEPTION\",\"message\":\"服务端处理异常: ");
        private static readonly byte[] _errHandlerExceptionTail = Encoding.UTF8.GetBytes("\"}");

        // ── 重连机制 ──────────────────────────────────────────
        private readonly SemaphoreSlim _reconnectLock = new(1, 1);
        private bool _shouldReconnect = true;
        private IMqttClient? _mqttClient;
        private CancellationTokenSource? _waveformCts;

        // ══════════════════════════════════════════════════════════
        //  构造函数
        // ══════════════════════════════════════════════════════════

        public MqttRpcBackgroundService(
            IServiceProvider serviceProvider,
            IOptionsMonitor<MqttSettings> mqttSettings,
            MqttEventPublisher eventPublisher,
            ILogger<MqttRpcBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _mqttSettings = mqttSettings;
            _eventPublisher = eventPublisher;
            _logger = logger;

            var settings = _mqttSettings.CurrentValue;

            // 启动时构建 RPC 方法路由表，后续重连不再重建
            _rpcHandlers = BuildHandlerTable();

            // 主题字符串全部在构造时拼接完成
            _rpcRoutePrefix = $"{RpcTopicPrefix}{settings.MachineId}/";
            _rpcSubscribePattern = $"{RpcTopicPrefix}{settings.MachineId}/#";
            _waveformCh1Topic = $"daq/{settings.MachineId}/waveform/ch1";
            _waveformCh2Topic = $"daq/{settings.MachineId}/waveform/ch2";

            // 遗嘱消息：如进程意外崩溃，Broker 据此通知订阅方
            _willPayloadBytes = Encoding.UTF8.GetBytes(
                "{\"eventType\":\"process_crashed\",\"source\":\"mqtt_broker\",\"reason\":\"will_message\",\"message\":\"进程已异常退出\"}");

            // 预分配波形读取缓冲区（按 1000 点×double 固定大小）
            _waveformBuf1 = new double[WaveformFramePoints];
            _waveformBuf2 = new double[WaveformFramePoints];

            // 从 ArrayPool 租用字节缓冲区，波形循环内复用
            _ch1Bytes = ArrayPool<byte>.Shared.Rent(WaveformFrameBytes);
            _ch2Bytes = ArrayPool<byte>.Shared.Rent(WaveformFrameBytes);
        }

        // ══════════════════════════════════════════════════════════
        //  生命周期入口
        // ══════════════════════════════════════════════════════════

        /// <summary>
        /// 创建 MQTT 客户端 → 注册断连事件（驱动自动重连）→ 连接 Broker → 阻塞等待服务停止
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var factory = new MqttClientFactory();
            _mqttClient = factory.CreateMqttClient();

            _mqttClient.DisconnectedAsync += OnDisconnectedAsync;
            _mqttClient.ApplicationMessageReceivedAsync += HandleRpcRequestAsync;
            _eventPublisher.MqttClient = _mqttClient;

            await ConnectAsync(stoppingToken);

            // 服务运行期间保持活动，直到外部取消信号触发
            try { await Task.Delay(Timeout.Infinite, stoppingToken); }
            catch (OperationCanceledException) { }
        }


        /// <summary>
        /// 连接与订阅
        /// 流程：构造 MQTT 连接选项（含心跳、遗嘱消息）→ 连接 → 订阅 RPC 主题 → 启动波形发布循环
        /// </summary>
        private async Task ConnectAsync(CancellationToken ct)
        {
            var settings = _mqttSettings.CurrentValue;

            // 构造 MQTT 连接选项（含心跳、遗嘱消息）
            var opts = new MqttClientOptionsBuilder()
                .WithTcpServer(settings.BrokerHost, settings.BrokerPort)
                .WithClientId(settings.MachineId)
                .WithCleanSession(true)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
                .WithTimeout(TimeSpan.FromSeconds(10));
            // 如果配置了用户名，添加认证信息
            if (!string.IsNullOrEmpty(settings.Username))
                opts.WithCredentials(settings.Username, settings.Password);
            // 遗嘱消息：如进程意外崩溃，Broker 据此通知订阅方
            opts.WithWillTopic($"daq/{settings.MachineId}/events/state_changed")
               .WithWillPayload(_willPayloadBytes)
               .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
               .WithWillRetain(true);
            // （可选）启用 TLS 加密连接
            await _mqttClient!.ConnectAsync(opts.Build(), ct);
            _logger.LogInformation("MQTT 已连接 {Host}:{Port} ClientId={ClientId}",
                settings.BrokerHost, settings.BrokerPort, settings.MachineId);

            // 预构建波形消息模板（Topic + QOS 固定，每帧替换 Payload 即可）
            _ch1Template = new MqttApplicationMessageBuilder()
                .WithTopic(_waveformCh1Topic)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                .Build();
            _ch2Template = new MqttApplicationMessageBuilder()
                .WithTopic(_waveformCh2Topic)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                .Build();

            await _mqttClient.SubscribeAsync(
                new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter(_rpcSubscribePattern, MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build(),
                ct);
            _logger.LogInformation("已订阅 RPC 主题: {Topic}", _rpcSubscribePattern);

            // 启动后台波形发布循环
            _waveformCts?.Cancel();
            _waveformCts?.Dispose();
            _waveformCts = new CancellationTokenSource();
            _ = PublishWaveformLoopAsync(_waveformCts.Token);

            _logger.LogInformation("RPC 服务端就绪，已注册 {Count} 个方法", _rpcHandlers.Count);
        }


        /// <summary>
        /// RPC 路由表构建：
        /// 从 DI 容器取出四个 Handler，合并其方法映射为一张总路由表
        /// </summary>
        private Dictionary<string, Func<byte[], Task<byte[]>>> BuildHandlerTable()
        {
            var table = new Dictionary<string, Func<byte[], Task<byte[]>>>();

            void Merge(Dictionary<string, Func<byte[], Task<byte[]>>> source)
            {
                foreach (var kv in source) table[kv.Key] = kv.Value;
            }

            Merge(_serviceProvider.GetRequiredService<MqttRpc.CollectorHandler>().GetHandlers());
            Merge(_serviceProvider.GetRequiredService<MqttRpc.LaserHandler>().GetHandlers());
            Merge(_serviceProvider.GetRequiredService<MqttRpc.SystemHandler>().GetHandlers());
            Merge(_serviceProvider.GetRequiredService<MqttRpc.LogHandler>().GetHandlers());

            return table;
        }

#region   RPC 请求分发
        /// <summary>
        /// 解析主题提取 方法名 / CorrelationId → 查路由表 → 执行 Handler → 发布 JSON 响应
        /// </summary>
        private async Task HandleRpcRequestAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            if (!ParseTopic(e.ApplicationMessage.Topic, out var method, out var corrId))
                return;

            var payload = e.ApplicationMessage.Payload.ToArray();

            // 响应主题：优先取 MQTT 5.0 ResponseTopic，否则按约定拼接
            var responseTopic = e.ApplicationMessage.ResponseTopic;
            if (string.IsNullOrEmpty(responseTopic))
                responseTopic = $"{e.ApplicationMessage.Topic}/response";

            _logger.LogDebug("RPC 请求 method={Method} corrId={CorrId}", method, corrId);

            if (!_rpcHandlers.TryGetValue(method, out var handler))
            {
                _logger.LogWarning("RPC 方法未注册: {Method}", method);
                await PublishAsync(responseTopic,
                    CombineBytes(_errUnknownMethodHead, Encoding.UTF8.GetBytes(method), _errUnknownMethodTail));
                return;
            }

            try
            {
                // 执行 Handler 获取响应负载 → 发布到响应主题
                await PublishAsync(responseTopic, await handler(payload));
            }
            catch (Exception ex)
            {
                // Handler 内部异常 → 记录错误日志 → 发布通用错误响应（包含异常消息，避免泄露敏感信息）
                _logger.LogError(ex, "RPC 执行异常 method={Method}", method);
                await PublishAsync(responseTopic,
                    CombineBytes(_errHandlerExceptionHead, Encoding.UTF8.GetBytes(ex.Message), _errHandlerExceptionTail));
            }
        }

        /// <summary>
        /// 解析 RPC 主题：前缀校验后提取 方法名 和 CorrelationId
        /// （抽出为同步方法以使用 ReadOnlySpan，避免 async 栈分配额外的 GC）
        /// </summary>
        private bool ParseTopic(string topic, out string method, out string corrId)
        {
            method = corrId = string.Empty;

            var span = topic.AsSpan();
            var prefix = _rpcRoutePrefix.AsSpan();
            if (!span.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            var rest = span.Slice(_rpcRoutePrefix.Length);
            var slash = rest.IndexOf('/');
            if (slash <= 0)
            {
                _logger.LogWarning("无法解析 RPC 主题: {Topic}", topic);
                return false;
            }

            method = rest.Slice(0, slash).ToString();
            corrId = rest.Slice(slash + 1).ToString();
            return true;
        }

        /// <summary>
        /// 将响应负载发布到指定 MQTT 主题（QOS ≥1 保证投递）
        /// </summary>
        private async Task PublishAsync(string topic, byte[] payload)
        {
            if (_mqttClient == null || !_mqttClient.IsConnected)
                return;

            try
            {
                await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(payload)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RPC 响应发布失败 Topic={Topic}", topic);
            }
        }
#endregion

  
        /// <summary>
        /// 波形数据发布循环：持续运行直到取消 → 每隔固定间隔 → 从共享内存读取最新双通道波形帧 → BlockCopy 到字节缓冲区 → 并行发布到 MQTT
        /// 按固定间隔从共享内存读取双通道波形帧 → BlockCopy 到复用缓冲区 → 并行发布到 MQTT
        /// </summary>
        private async Task PublishWaveformLoopAsync(CancellationToken ct)
        {
            var uiBuffer = _serviceProvider.GetRequiredService<UISharedBuffer>();
            var interval = _mqttSettings.CurrentValue.WaveformPublishIntervalMs;

            _logger.LogInformation("波形发布循环启动 间隔={Interval}ms", interval);

            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (_mqttClient?.IsConnected != true)
                    continue;

                try
                {
                    uiBuffer.ReadLatestFrame(ref _waveformBuf1, ref _waveformBuf2);

                    Buffer.BlockCopy(_waveformBuf1, 0, _ch1Bytes, 0, WaveformFrameBytes);
                    Buffer.BlockCopy(_waveformBuf2, 0, _ch2Bytes, 0, WaveformFrameBytes);

                    var msg1 = new MqttApplicationMessage
                    {
                        Topic = _ch1Template!.Topic,
                        QualityOfServiceLevel = _ch1Template.QualityOfServiceLevel,
                        PayloadSegment = new ArraySegment<byte>(_ch1Bytes, 0, WaveformFrameBytes)
                    };
                    var msg2 = new MqttApplicationMessage
                    {
                        Topic = _ch2Template!.Topic,
                        QualityOfServiceLevel = _ch2Template.QualityOfServiceLevel,
                        PayloadSegment = new ArraySegment<byte>(_ch2Bytes, 0, WaveformFrameBytes)
                    };

                    await Task.WhenAll(
                        _mqttClient.PublishAsync(msg1, ct),
                        _mqttClient.PublishAsync(msg2, ct));
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "波形发布失败，下次循环重试");
                }
            }

            _logger.LogInformation("波形发布循环已停止");
        }


        /// <summary>
        /// 断线重连事件回调 → 根据断开原因和服务状态决定是否触发重连 → 指数退避重连（避免短时间内频繁重试）
        /// 触发条件：非正常断开 且 服务未在停止中 → 指数退避重连
        /// </summary>
        private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
        {
            _logger.LogWarning("MQTT 断开 Reason={Reason} WasConnected={WasConnected}",
                args.Reason, args.ClientWasConnected);

            _waveformCts?.Cancel();

            if (!_shouldReconnect)
            {
                _logger.LogInformation("服务正在停止，不触发重连");
                return;
            }

            if (args.Reason == MqttClientDisconnectReason.NormalDisconnection)
            {
                _logger.LogInformation("主动断开，不触发重连");
                return;
            }

            if (!await _reconnectLock.WaitAsync(0))
            {
                _logger.LogDebug("重连已在进行中");
                return;
            }

            try
            {
                var settings = _mqttSettings.CurrentValue;
                var delay = TimeSpan.FromSeconds(1);
                var cap = TimeSpan.FromSeconds(settings.ReconnectDelaySeconds > 0
                    ? settings.ReconnectDelaySeconds : 60);
                var n = 0;

                while (_shouldReconnect)
                {
                    n++;
                    try
                    {
                        _logger.LogInformation("第 {Attempt} 次重连尝试…", n);
                        await ConnectAsync(CancellationToken.None);
                        _logger.LogInformation("重连成功");
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "第 {Attempt} 次重连失败，{Delay:F1}s 后重试", n, delay.TotalSeconds);
                        await Task.Delay(delay);
                        delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, cap.TotalSeconds));
                    }
                }
            }
            finally
            {
                _reconnectLock.Release();
            }
        }


        /// <summary>
        /// 服务停止
        /// 流程：设置重连标志 → 获取重连锁确保无并发 → 停波形循环 → 注销事件 → 断开 Broker → 释放 MQTT 客户端
        /// </summary>
        public override async Task StopAsync(CancellationToken ct)
        {
            _logger.LogInformation("BackgroundService 正在停止…");

            _shouldReconnect = false;

            // 等待正在进行的重连完成
            await _reconnectLock.WaitAsync(ct);
            try
            {
                _waveformCts?.Cancel();

                if (_mqttClient != null)
                {
                    _mqttClient.DisconnectedAsync -= OnDisconnectedAsync;
                    _mqttClient.ApplicationMessageReceivedAsync -= HandleRpcRequestAsync;

                    if (_mqttClient.IsConnected)
                    {
                        try { await _mqttClient.DisconnectAsync(new MqttClientDisconnectOptions(), ct); }
                        catch (Exception ex) { _logger.LogDebug(ex, "断开时异常（可能已断开）"); }
                    }

                    _mqttClient.Dispose();
                    _mqttClient = null;
                }

                _eventPublisher.MqttClient = null;
            }
            finally
            {
                _reconnectLock.Release();
            }

            await base.StopAsync(ct);
            _logger.LogInformation("BackgroundService 已停止");
        }

        /// <summary>
        /// 服务销毁
        /// 归还 ArrayPool 租用的缓冲区并释放同步原语
        /// </summary>
        public override void Dispose()
        {
            if (_ch1Bytes != null) { ArrayPool<byte>.Shared.Return(_ch1Bytes); _ch1Bytes = null!; }
            if (_ch2Bytes != null) { ArrayPool<byte>.Shared.Return(_ch2Bytes); _ch2Bytes = null!; }

            _reconnectLock?.Dispose();
            _waveformCts?.Dispose();

            base.Dispose();
        }

        /// <summary>
        /// 合并三个字节数组为一个新数组（用于构建错误响应 JSON）
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="c"></param>
        /// <returns></returns>
        private static byte[] CombineBytes(byte[] a, byte[] b, byte[] c)
        {
            var r = new byte[a.Length + b.Length + c.Length];
            Buffer.BlockCopy(a, 0, r, 0, a.Length);
            Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
            Buffer.BlockCopy(c, 0, r, a.Length + b.Length, c.Length);
            return r;
        }
    }
}
