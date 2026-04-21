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

        private readonly GrpcServiceImpl _grpcService;
        private readonly CniLaserControl.CniLaser _laser;
        private readonly ILogger<SystemStateService> _logger;

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
            GrpcServiceImpl grpcService,
            CniLaserControl.CniLaser laser,
            ILogger<SystemStateService> logger)
        {
            _grpcService = grpcService;
            _laser = laser;
            _logger = logger;
        }

        /// <summary>
        /// 更新采集卡状态缓存（由 GrpcServiceImpl 在收到消息时调用）
        /// 使用不可变对象替换模式，确保并发读写安全
        /// </summary>
        /// <param name="updater">状态更新函数，接收当前状态副本，返回新状态</param>
        public void UpdateCollectorState(Func<CollectorStateDto, CollectorStateDto> updater)
        {
            var current = _cachedCollectorState;
            var newState = updater(current);
            newState.Timestamp = DateTime.Now;
            _cachedCollectorState = newState; // volatile 写，保证可见性
            _logger.LogDebug("采集卡状态缓存已更新: ProcessConnected={Connected}, DeviceOpened={Opened}, Acquiring={Acquiring}",
                newState.ProcessConnected, newState.DeviceOpened, newState.Acquiring);
        }

        /// <summary>
        /// 更新激光器状态缓存（由 CniLaser 在状态变更时调用）
        /// 使用不可变对象替换模式，确保并发读写安全
        /// </summary>
        /// <param name="updater">状态更新函数，接收当前状态副本，返回新状态</param>
        public void UpdateLaserState(Func<LaserStateDto, LaserStateDto> updater)
        {
            var current = _cachedLaserStateDto;
            var newState = updater(current);
            newState.Timestamp = DateTime.Now;
            _cachedLaserStateDto = newState; // volatile 写，保证可见性
            _logger.LogDebug("激光器状态缓存已更新: SerialConnected={Connected}, EmissionOn={EmissionOn}, PortName={PortName}",
                newState.SerialConnected, newState.EmissionOn, newState.PortName);
        }

        /// <summary>
        /// 重置采集卡状态缓存为默认值（子进程断开时调用）
        /// </summary>
        public void ResetCollectorState()
        {
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
        /// 检查采集子进程是否已连接
        /// </summary>
        private bool IsCollectorConnected()
        {
            try
            {
                var clients = _grpcService.GetConnectedClients();
                return Array.Exists(clients, id => id == CollectorClientId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查采集子进程连接状态时发生异常");
                return false;
            }
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
