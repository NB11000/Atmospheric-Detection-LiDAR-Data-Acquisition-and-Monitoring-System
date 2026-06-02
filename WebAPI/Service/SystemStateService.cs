using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using WebAPI.Hubs;
using WebAPI.Models;

namespace WebAPI.Service
{
    /// <summary>
    /// 系统统一状态服务
    /// </summary>
    public class SystemStateService
    {
        /// <summary>
        /// 采集子进程固定客户端标识
        /// </summary>
        public const string CollectorClientId = "数据采集子进程";
        private readonly ILogger<SystemStateService> _logger;
        private readonly Lazy<IMqttEventPublisher>? _mqttEventPublisher;
        private readonly ISignalRHubPublisher? _signalRHubPublisher;

        /// <summary>
        /// 采集状态变更事件（true=开始采集，false=停止采集）
        /// WaveformPublishService 订阅此事件以驱动波形发布循环启停
        /// </summary>
        public event Action<bool>? AcquiringStateChanged;

        /// <summary>
        /// MQTT 连接状态变更事件（true=已连接，false=已断开）
        /// AcquisitionLifecycleCoordinator 订阅此事件以结合采集状态决定消费者启停
        /// </summary>
        public event Action<bool>? MqttConnectionStateChanged;


        /// <summary>
        /// MQTT 连接状态本地缓存（由 MqttRpcBackgroundService 在断连/重连时更新）
        /// </summary>
        private volatile bool _mqttConnected;

        /// <summary>
        /// 采集卡状态本地缓存（通过命令响应和主动上报推断更新，避免每次快照都发起 IPC）
        /// 使用 volatile 保证多线程可见性，更新时采用整体替换（不可变对象模式）
        /// </summary>
        private volatile CollectorStateDto _cachedCollectorState = new CollectorStateDto
        {
            ProcessConnected = false,
            DeviceOpened = false,
            Acquiring = false,
            Handle = 0,
            LastMessage = "采集子进程未连接",
            Timestamp = DateTime.Now
        };

        /// <summary>
        /// 激光器状态本地缓存（通过命令响应和主动上报推断更新，避免每次快照都发起 IPC）
        /// 使用 volatile 保证多线程可见性，更新时采用整体替换（不可变对象模式）
        /// </summary>
        private volatile LaserStateDto _cachedLaserStateDto = new LaserStateDto
        {
            SerialConnected = false,
            EmissionOn = false,
            PortName = string.Empty,
            LastMessage = "激光串口未打开",
            Timestamp = DateTime.Now
        };


        public SystemStateService(
            ILogger<SystemStateService> logger)
        {
            _logger = logger;
            _mqttEventPublisher = null;
            _signalRHubPublisher = null;
        }

        public SystemStateService(
            ILogger<SystemStateService> logger,
            Lazy<IMqttEventPublisher> mqttEventPublisher,
            ISignalRHubPublisher signalRHubPublisher)
        {
            _logger = logger;
            _mqttEventPublisher = mqttEventPublisher;
            _signalRHubPublisher = signalRHubPublisher;
        }

        /// <summary>
        /// 更新 MQTT 连接状态（由 MqttRpcBackgroundService 在断连/重连时调用）
        /// 仅在值变化时触发 MqttConnectionStateChanged 事件
        /// </summary>
        public void UpdateMqttConnectionState(bool isConnected)
        {
            if (_mqttConnected == isConnected)
                return;
            _mqttConnected = isConnected;
            MqttConnectionStateChanged?.Invoke(isConnected);
            _logger.LogInformation("MQTT 连接状态已更新: {State}", isConnected ? "已连接" : "已断开");

            if (isConnected)
            {
                // 恢复：仅 SignalR 推送（MQTT 连接状态由 PublishDeviceOnlineAsync 承担；去掉 BroadcastAsync 避免冗余 MQTT state_changed）
                try
                {
                    _ = _signalRHubPublisher?.PublishStateChangedAsync(
                        "mqtt_connected", "system", "MQTT 连接已恢复", "MQTT 连接已恢复");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SignalR 推送 MQTT 连接恢复事件失败");
                }
            }
            else
            {
                // 断开：仅 SignalR 广播，MQTT 通道不可用
                try
                {
                    _ = _signalRHubPublisher?.PublishStateChangedAsync(
                        "mqtt_disconnected", "system", "MQTT 连接已断开", "MQTT 连接已断开");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SignalR 推送 MQTT 断连事件失败");
                }
            }
        }

        /// <summary>
        /// 静默更新采集卡状态（仅缓存，不广播）
        /// 适用场景：MQTT RPC 命令响应成功后，命令响应已携带确认，无需 state_changed 广播
        /// </summary>
        public void UpdateCollectorStateSilent(Func<CollectorStateDto, CollectorStateDto> updater)
        {
            var current = _cachedCollectorState;
            var oldAcquiring = current.Acquiring;
            var newState = updater(current);
            newState.Timestamp = DateTime.Now;
            _cachedCollectorState = newState; // volatile 写，保证可见性
            _logger.LogDebug("采集卡状态缓存已更新: ProcessConnected={Connected}, DeviceOpened={Opened}, Acquiring={Acquiring}",
                newState.ProcessConnected, newState.DeviceOpened, newState.Acquiring);

            // Acquiring 值变化时通知订阅方
            if (oldAcquiring != newState.Acquiring)
            {
                AcquiringStateChanged?.Invoke(newState.Acquiring);
            }
        }

        /// <summary>
        /// 静默更新激光器状态（仅缓存，不广播）
        /// 适用场景：MQTT RPC laser-connect/disconnect/on/off 命令成功后
        /// </summary>
        public void UpdateLaserStateSilent(Func<LaserStateDto, LaserStateDto> updater)
        {
            var current = _cachedLaserStateDto;
            var newState = updater(current);
            newState.Timestamp = DateTime.Now;
            _cachedLaserStateDto = newState; // volatile 写，保证可见性
            _logger.LogDebug("激光器状态缓存已更新: SerialConnected={Connected}, EmissionOn={EmissionOn}, PortName={PortName}",
                newState.SerialConnected, newState.EmissionOn, newState.PortName);
        }

        /// <summary>
        /// 广播更新采集卡状态（更新缓存 + 双通道推送 state_changed 事件）
        /// 适用场景：gRPC 断连、设备异常断开、采集异常终止等非命令链路变更
        /// </summary>
        public void UpdateCollectorStateAndBroadcast(
            Func<CollectorStateDto, CollectorStateDto> updater,
            string eventType,
            string reason)
        {
            UpdateCollectorStateSilent(updater);
            var state = _cachedCollectorState;
            BroadcastAsync(eventType, "collector", reason, state.LastMessage);
        }

        /// <summary>
        /// 广播更新激光器状态（更新缓存 + 双通道推送 state_changed 事件）
        /// 适用场景：激光器硬件意外断开等非命令链路变更
        /// </summary>
        public void UpdateLaserStateAndBroadcast(
            Func<LaserStateDto, LaserStateDto> updater,
            string eventType,
            string reason)
        {
            UpdateLaserStateSilent(updater);
            var state = _cachedLaserStateDto;
            BroadcastAsync(eventType, "laser", reason, state.LastMessage);
        }

        /// <summary>
        /// 重置采集卡状态并广播（路径 [B]）
        /// </summary>
        public void ResetCollectorStateAndBroadcast(string reason)
        {
            var oldAcquiring = _cachedCollectorState.Acquiring;
            _cachedCollectorState = new CollectorStateDto
            {
                ProcessConnected = false,
                DeviceOpened = false,
                Acquiring = false,
                Handle = 0,
                LastMessage = "采集子进程未连接",
                Timestamp = DateTime.Now
            };
            _logger.LogInformation("采集卡状态缓存已重置");

            if (oldAcquiring)
            {
                AcquiringStateChanged?.Invoke(false);
            }

            BroadcastAsync("collector_disconnected", "collector", reason, "采集子进程未连接");
        }

        private void BroadcastAsync(string eventType, string source, string reason, string message)
        {
            try
            {
                _ = _mqttEventPublisher?.Value.PublishStateChangedAsync(eventType, source, reason, message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MQTT 推送失败（异步）: {EventType}", eventType);
            }

            try
            {
                _ = _signalRHubPublisher?.PublishStateChangedAsync(eventType, source, reason, message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SignalR 推送失败（异步）: {EventType}", eventType);
            }
        }

        /// <summary>
        /// 获取系统统一状态快照（全部本地读取，零 IPC）
        /// </summary>
        public SystemStateDto GetSystemState()
        {
            var collectorState = GetCollectorState(); // 读缓存
            var laserState = GetLaserState();         // 读缓存

            // 构建并返回系统状态快照
            return new SystemStateDto
            {
                // 服务器状态（API存活性）
                Server = new ServerStateDto
                {
                    IsApiAlive = true,
                    IsMqttConnected = _mqttConnected,
                    Timestamp = DateTime.Now
                },
                // 采集卡状态
                Collector = collectorState,
                // 激光器状态
                Laser = laserState,
                // UI提示状态（基于当前硬件状态推断可用操作）
                UiHints = BuildUiHints(collectorState, laserState),
                // 快照生成时间
                Timestamp = DateTime.Now
            };
        }

        /// <summary>
        /// 获取系统统一状态快照（异步包装，兼容现有调用）
        /// </summary>
        public Task<SystemStateDto> GetSystemStateAsync()
        {
            return Task.FromResult(GetSystemState());
        }

        /// <summary>
        /// 获取采集卡运行状态（直接读取本地缓存，零 IPC 开销）
        /// 从缓存读取状态
        /// </summary>
        public CollectorStateDto GetCollectorState()
        {
/*             // 读取 volatile 字段，保证获取最新值
            var state = _cachedCollectorState;
            // 同步连接状态（以 _clientStreams 为准）
            if (state.ProcessConnected != IsCollectorConnected())
            {
                state = new CollectorStateDto
                {
                    ProcessConnected = IsCollectorConnected(),
                    DeviceOpened = state.DeviceOpened,
                    Acquiring = state.Acquiring,
                    Handle = state.Handle,
                    LastMessage = state.LastMessage,
                    Timestamp = DateTime.Now
                };
            } */
            // 读取 volatile 字段，保证获取最新值
            var state= new CollectorStateDto
                {
                    ProcessConnected = _cachedCollectorState.ProcessConnected,
                    DeviceOpened = _cachedCollectorState.DeviceOpened,
                    Acquiring = _cachedCollectorState.Acquiring,
                    Handle = _cachedCollectorState.Handle,
                    LastMessage = _cachedCollectorState.LastMessage,
                    Timestamp = DateTime.Now
                };

            return state;
        }

        /// <summary>
        /// 获取激光器运行状态
        /// 从缓存读取状态，零 IPC 开销
        /// </summary>
        public LaserStateDto GetLaserState()
        {
            // 读取 volatile 字段，保证获取最新值
            var state = new LaserStateDto
            {
                SerialConnected = _cachedLaserStateDto.SerialConnected,
                EmissionOn = _cachedLaserStateDto.EmissionOn,
                PortName = _cachedLaserStateDto.PortName,
                LastMessage = _cachedLaserStateDto.LastMessage,
                Timestamp = DateTime.Now
            };

            return state;
        }

        /// <summary>
        /// 生成UI操作提示状态
        /// </summary>
        private static UiHintStateDto BuildUiHints(CollectorStateDto collectorState, LaserStateDto laserState)
        {
            return new UiHintStateDto
            {
                CanOpenCollector = collectorState.ProcessConnected && !collectorState.DeviceOpened,
                CanCloseCollector = collectorState.ProcessConnected && collectorState.DeviceOpened && !collectorState.Acquiring,
                CanStartAcquisition = collectorState.ProcessConnected && collectorState.DeviceOpened && !collectorState.Acquiring,
                CanStopAcquisition = collectorState.ProcessConnected && collectorState.Acquiring,
                CanConnectLaser = !laserState.SerialConnected,
                CanDisconnectLaser = laserState.SerialConnected && !laserState.EmissionOn,
                CanTurnLaserOn = laserState.SerialConnected && !laserState.EmissionOn,
                CanTurnLaserOff = laserState.SerialConnected && laserState.EmissionOn
            };
        }




        /// <summary>
        /// 获取系统状态结构体快照
        /// 从本地缓存读取状态，构建结构体返回，零 GC 开销
        /// </summary>
        public System_State_struct Get_System_State_Struct()
        {

            // 构建并返回系统状态快照
            return new System_State_struct
            {
                // 服务器状态（API存活性）
                Server = new ServerState_struct
                {
                    IsApiAlive = true
                },
                // 采集卡状态
                Collector = new Collector_State_struct
                {
                    ProcessConnected = _cachedCollectorState.ProcessConnected,
                    DeviceOpened = _cachedCollectorState.DeviceOpened,
                    Acquiring = _cachedCollectorState.Acquiring,
                    Handle = _cachedCollectorState.Handle
                },
                // 激光器状态
                Laser = new Laser_State_struct
                {
                    SerialConnected = _cachedLaserStateDto.SerialConnected,
                    EmissionOn = _cachedLaserStateDto.EmissionOn,
                    PortName = _cachedLaserStateDto.PortName
                },
            };
        }

    }



}
