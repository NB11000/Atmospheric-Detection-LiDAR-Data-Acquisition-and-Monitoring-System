using System;
using System.Buffers;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
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
using SharedModels;
using WebAPI.Tools;

namespace WebAPI.Service
{
    /// <summary>
    /// MQTT RPC 后台服务 — 托管 MQTT 客户端完整生命周期，纯 RPC 路由职责
    /// </summary>
    public class MqttRpcBackgroundService : BackgroundService
    {
        private const string RpcTopicPrefix = "$rpc/";

        // ── 依赖注入 ──────────────────────────────────────────
        private readonly IServiceProvider _serviceProvider;
        private readonly IOptionsMonitor<MqttSettings> _mqttSettings;
        private readonly MqttEventPublisher _eventPublisher;
        private readonly SystemStateService _systemStateService;
        private readonly ILogger<MqttRpcBackgroundService> _logger;

        // ── RPC 方法路由（启动时构建一次） ──────────────────────
        private readonly Dictionary<string, Func<byte[], Task<byte[]>>> _rpcHandlers;

        // ── 预计算的 MQTT 主题字符串 ────────────────────────────
        private readonly string _rpcRoutePrefix;
        private readonly string _rpcSubscribePattern;

        /// <summary>
        /// ── 遗嘱消息负载（进程意外退出时 Broker 代为发布）───────
        /// </summary>
        private readonly byte[] _willPayloadBytes;

        // ── 预构建的 RPC 错误响应片段（运行时拼入方法名）──────────
        internal static byte[] BuildWillPayloadBytes()
        {
            return Encoding.UTF8.GetBytes(
                "{\"status\":\"offline\",\"ts\":0,\"eventType\":\"process_crashed\",\"source\":\"mqtt_broker\",\"message\":\"设备已离线\",\"timestamp\":\"0001-01-01T00:00:00Z\"}");
        }

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
        private readonly CancellationTokenSource _shutdownCts = new();

        // ══════════════════════════════════════════════════════════
        //  构造函数
        // ══════════════════════════════════════════════════════════

        public MqttRpcBackgroundService(
            IServiceProvider serviceProvider,
            IOptionsMonitor<MqttSettings> mqttSettings,
            MqttEventPublisher eventPublisher,
            SystemStateService systemStateService,
            ILogger<MqttRpcBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _mqttSettings = mqttSettings;
            _eventPublisher = eventPublisher;
            _systemStateService = systemStateService;
            _logger = logger;

            var settings = _mqttSettings.CurrentValue;

            // 启动时构建 RPC 方法路由表，后续重连不再重建
            _rpcHandlers = BuildHandlerTable();

            // RPC 主题前缀（如 $rpc/daq-srv-01/），方法调用时根据此进行路由分发
            _rpcRoutePrefix = $"{RpcTopicPrefix}{settings.MachineId}/";
            _rpcSubscribePattern = $"{RpcTopicPrefix}{settings.MachineId}/#";

            // 遗嘱消息：如进程意外崩溃，Broker 据此通知订阅方
            _willPayloadBytes = BuildWillPayloadBytes();
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

            // 初始连接：失败时不崩溃，由 DisconnectedAsync 事件驱动后续重连
            try
            {
                await ConnectAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "MQTT 初始连接失败，等待 DisconnectedAsync 事件驱动重连");
            }

            // 服务运行期间保持活动，直到外部取消信号触发
            try { await Task.Delay(Timeout.Infinite, stoppingToken); }
            catch (OperationCanceledException) { }
        }


        /// <summary>
        /// 连接 Broker 并完成订阅
        /// 异常会向上抛出让调用方（ExecuteAsync / OnDisconnectedAsync）自行处理
        /// </summary>
        internal async Task ConnectAsync(CancellationToken ct)
        {
            var settings = _mqttSettings.CurrentValue;

            // 构造 MQTT 连接选项（含心跳、遗嘱消息）
            var opts = new MqttClientOptionsBuilder()
                .WithTcpServer(settings.BrokerHost, settings.BrokerPort)
                .WithClientId(settings.MachineId)
                .WithCleanSession(false)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
                .WithTimeout(TimeSpan.FromSeconds(10));
            // 如果配置了用户名，添加认证信息
            if (!string.IsNullOrEmpty(settings.Username))
                opts.WithCredentials(settings.Username, settings.Password);
            // TLS/SSL 配置（EMQX Serverless 端口 8883 必须启用）
            if (settings.UseTls)
            {
                opts.WithTlsOptions(tls =>
                    {
                        tls.UseTls();
                        tls.WithSslProtocols(SslProtocols.Tls12 | SslProtocols.Tls13);
                        
                        // 如果提供了CA证书,加载它
                        if (!string.IsNullOrEmpty(settings.CaCertificatePath))
                        {
                            var caCert = new X509Certificate2(settings.CaCertificatePath);
                            tls.WithClientCertificates(new[] { caCert });
                        }
                        
                        // 证书验证回调
                        tls.WithCertificateValidationHandler(context =>
                        {
                            var certificate = context.Certificate as X509Certificate2;
                            
                            // 测试环境: 接受所有证书
                            // return true;
                            
                            // 生产环境: 严格证书验证
                            return Tool.ValidateServerCertificate(certificate, context.Chain, context.SslPolicyErrors);
                        });
                    });   
            }
            // 遗嘱消息：如进程意外崩溃，Broker 据此通知订阅方
            opts.WithWillTopic($"daq/{settings.MachineId}/events/will")
               .WithWillPayload(_willPayloadBytes)
               .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
               .WithWillRetain(true);

            // 连接MQTT服务器（使用 linked token，确保 StopAsync 可取消重连中的连接）
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token, ct);
            var result = await _mqttClient!.ConnectAsync(opts.Build(), linkedCts.Token);
            if (result.ResultCode != MqttClientConnectResultCode.Success)
            {
                throw new InvalidOperationException(
                    $"MQTT 连接被拒绝: {result.ResultCode} — {result.ReasonString ?? "无详细信息"}。请检查 Broker 地址、端口、认证凭据。");
            }
            // EMQX账号密码认证是必须的，如未配置则警告
            if (settings.UseTls && string.IsNullOrEmpty(settings.Username))
            {
                _logger.LogWarning("EMQX Serverless 需要用户名/密码认证，请在 appsettings.json 的 Mqtt.Username / Mqtt.Password 中填入凭据");
            }
            _logger.LogInformation("MQTT 已连接 {Host}:{Port} ClientId={ClientId}",
                settings.BrokerHost, settings.BrokerPort, settings.MachineId);
            
            // 确保在订阅前连接依然有效（Broker 可能在连接后立即因认证失败而断连）
            if (!_mqttClient.IsConnected)
            {
                throw new InvalidOperationException("MQTT 已连接但立即断开，可能是认证凭据无效或 Broker 拒绝了连接");
            }

            // 订阅 RPC 主题
            await _mqttClient.SubscribeAsync(
                new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter(_rpcSubscribePattern, MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build(),
                ct);
            _logger.LogInformation("已订阅 RPC 主题: {Topic}", _rpcSubscribePattern);

            await _eventPublisher.PublishDeviceOnlineAsync();

            // 更新系统状态服务中的 MQTT 连接状态（供 API 查询和 UI 显示）
            _systemStateService.UpdateMqttConnectionState(_mqttClient.IsConnected);

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
            Merge(_serviceProvider.GetRequiredService<MqttRpc.ConfigHandler>().GetHandlers());

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
            // MQTT 5.0 规范中，响应主题可由请求消息携带 ResponseTopic 属性指定；
            // 如果没有，则按约定使用 "请求主题/response" 作为响应主题
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
        /// 断线重连事件回调 → 根据断开原因和服务状态决定是否触发重连 → 指数退避重连（避免短时间内频繁重试）
        /// 触发条件：非正常断开 且 服务未在停止中 → 指数退避重连
        /// </summary>
        private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
        {
            _logger.LogWarning("MQTT 断开 Reason={Reason} WasConnected={WasConnected}",
                args.Reason, args.ClientWasConnected);

            _systemStateService.UpdateMqttConnectionState(false);

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
        /// 流程：取消重连中的连接 → 发布 offline retained 消息 → 设置重连标志 → 获取重连锁 → 条件断开/兜底
        /// </summary>
        public override async Task StopAsync(CancellationToken ct)
        {
            _logger.LogInformation("BackgroundService 正在停止…");

            _shutdownCts.Cancel();

            bool offlinePublished = false;
            if (_mqttClient != null && _mqttClient.IsConnected)
            {
                try
                {
                    await _eventPublisher.PublishDeviceOfflineAsync();
                    offlinePublished = true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "PublishDeviceOfflineAsync 失败，将跳过正常 DISCONNECT 由 Will 兜底");
                }
            }

            _shouldReconnect = false;
            _systemStateService.UpdateMqttConnectionState(false);

            // 等待正在进行的重连完成
            await _reconnectLock.WaitAsync(ct);
            try
            {
                if (_mqttClient != null)
                {
                    _mqttClient.DisconnectedAsync -= OnDisconnectedAsync;
                    _mqttClient.ApplicationMessageReceivedAsync -= HandleRpcRequestAsync;

                    if (_mqttClient.IsConnected)
                    {
                        if (offlinePublished)
                        {
                            try { await _mqttClient.DisconnectAsync(new MqttClientDisconnectOptions(), ct); }
                            catch (Exception ex) { _logger.LogDebug(ex, "断开时异常（可能已断开）"); }
                            _mqttClient.Dispose();
                        }
                        else
                        {
                            _logger.LogInformation("PublishDeviceOfflineAsync 未成功，跳过 DisconnectAsync/Dispose，由 OS 关闭 TCP 触发 Broker Will");
                        }
                    }
                    else
                    {
                        _mqttClient.Dispose();
                    }

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
        /// 释放同步原语
        /// </summary>
        public override void Dispose()
        {
            _shutdownCts.Dispose();
            _reconnectLock?.Dispose();

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
