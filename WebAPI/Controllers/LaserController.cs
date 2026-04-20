using WebAPI.Tools;
using WebAPI.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using WebAPI.Models;

namespace WebAPI.Controllers
{
    /// <summary>
    /// 激光器控制API控制器
    /// 提供激光器连接、功率、频率、开关等控制功能
    /// </summary>
    [ApiController]
    [Route("api/laser")]
    public class LaserController : ControllerBase
    {
        private readonly CniLaserControl.CniLaser _laser;
        private readonly ILogger<LaserController> _logger;
        private readonly ConfigHelper _configHelper;
        private readonly SystemStateService _systemStateService;

        /// <summary>
        /// 构造函数，注入激光器服务、日志器、配置助手和系统状态服务
        /// </summary>
        /// <param name="laser">激光器服务实例</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="configHelper">配置助手</param>
        /// <param name="systemStateService">系统状态服务</param>
        public LaserController(CniLaserControl.CniLaser laser, ILogger<LaserController> logger,
                    ConfigHelper configHelper, SystemStateService systemStateService)
        {
            _laser = laser;
            _logger = logger;
            _configHelper = configHelper;
            _systemStateService = systemStateService;
        }

        /// <summary>
        /// 打开串口连接激光器，返回统一命令响应
        /// 从全局配置中获取串口号和波特率进行串口连接
        /// </summary>
        /// <returns>CommandResult 包含最新系统状态</returns>
        [HttpPost("connect")]
        public async Task<IActionResult> Connect()
        {
            try
            {
                // 从全局配置中获取串口号和波特率
                var radarConfig = Program.RadarConfig;
                
                // 验证串口配置
                if (string.IsNullOrEmpty(radarConfig.SerialPort))
                {
                    // 如果串口号未配置，直接返回错误响应
                    // var state = await _systemStateService.GetSystemStateAsync();
                    return Ok(new CommandResult
                    {
                        Success = false,
                        Code = "LASER_CONNECT_FAILED",
                        Message = "串口号未配置，请先在配置中设置串口号",
                        State = null
                    });
                }

                _logger.LogInformation($"尝试打开串口连接激光器，端口：{radarConfig.SerialPort}，波特率：{radarConfig.BaudRate}");
                bool success = _laser.Connect(radarConfig.SerialPort, radarConfig.BaudRate);
                var stateAfter = _systemStateService.Get_System_State_Struct();
                return Ok(new CommandResult
                {
                    Success = stateAfter.Laser.SerialConnected,
                    Code = stateAfter.Laser.SerialConnected ? "LASER_CONNECTED" : "LASER_CONNECT_FAILED",
                    Message = stateAfter.Laser.SerialConnected ? "串口连接成功" : "串口连接失败，请检查端口和波特率设置",
                    State = null
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "打开串口连接激光器时发生异常");
                var state = await _systemStateService.GetSystemStateAsync();
                return Ok(new CommandResult
                {
                    Success = false,
                    Code = "LASER_CONNECT_EXCEPTION",
                    Message = $"打开串口连接激光器时发生异常:{ex.Message}",
                    State = null
                });
            }
        }

        /// <summary>
        /// 断开串口连接，返回统一命令响应
        /// </summary>
        /// <returns>CommandResult 包含最新系统状态</returns>
        [HttpPost("disconnect")]
        public async Task<IActionResult> Disconnect()
        {
            try
            {
                _logger.LogInformation("断开串口连接");
                _laser.Disconnect();
                var state = _systemStateService.Get_System_State_Struct();
                return Ok(new CommandResult
                {
                    Success = !state.Laser.SerialConnected,
                    Code = !state.Laser.SerialConnected ? "LASER_DISCONNECTED" : "LASER_DISCONNECT_FAILED",
                    Message = !state.Laser.SerialConnected ? "串口已断开连接" : "串口断开连接失败",
                    State = null
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "断开串口连接时发生异常");
                var state = await _systemStateService.GetSystemStateAsync();
                return Ok(new CommandResult
                {
                    Success = false,
                    Code = "LASER_DISCONNECT_EXCEPTION",
                    Message = $"断开串口连接时发生异常:{ex.Message}",
                    State = null
                });
            }
        }

        /// <summary>
        /// 开启激光，返回统一命令响应
        /// 在开启激光前，先从全局配置中获取激光功率和调制频率并设置
        /// </summary>
        /// <returns>CommandResult 包含最新系统状态</returns>
        [HttpPost("on")]
        public async Task<IActionResult> LaserOn()
        {
            try
            {
                // 检查激光器是否已连接
                if (!_laser.IsConnected)
                {
                    // var state = await _systemStateService.GetSystemStateAsync();
                    return Ok(new CommandResult
                    {
                        Success = false,
                        Code = "LASER_ON_FAILED",
                        Message = "激光器未连接，请先连接激光器",
                        State = null
                    });
                }

                // 从全局配置中获取激光功率和调制频率
                var radarConfig = Program.RadarConfig;
                
                // 验证配置
                if (radarConfig.LaserPower <= 0)
                {
                    // var state = await _systemStateService.GetSystemStateAsync();
                    return Ok(new CommandResult
                    {
                        Success = false,
                        Code = "LASER_ON_FAILED",
                        Message = "激光功率未配置或配置无效，请先设置激光功率",
                        State = null
                    });
                }

                if (radarConfig.LaserModulationFrequency <= 0)
                {
                    // var state = await _systemStateService.GetSystemStateAsync();
                    return Ok(new CommandResult
                    {
                        Success = false,
                        Code = "LASER_ON_FAILED",
                        Message = "激光调制频率未配置或配置无效，请先设置调制频率",
                        State = null
                    });
                }

                _logger.LogInformation($"开始设置激光参数：功率={radarConfig.LaserPower} mW，频率={radarConfig.LaserModulationFrequency} Hz");

                // 1. 设置激光功率
                bool powerSuccess = _laser.SetPower(radarConfig.LaserPower);
                if (!powerSuccess)
                {
                    // var state = await _systemStateService.GetSystemStateAsync();
                    return Ok(new CommandResult
                    {
                        Success = false,
                        Code = "LASER_ON_FAILED",
                        Message = "激光功率设置失败，请检查激光器连接状态",
                        State = null
                    });
                }

                // 2. 设置激光调制频率
                bool frequencySuccess = _laser.SetFrequency(radarConfig.LaserModulationFrequency);
                if (!frequencySuccess)
                {
                    // var state = await _systemStateService.GetSystemStateAsync();
                    return Ok(new CommandResult
                    {
                        Success = false,
                        Code = "LASER_ON_FAILED",
                        Message = "激光调制频率设置失败，请检查激光器连接状态",
                        State = null
                    });
                }

                // 3. 开启激光
                _laser.LaserOn();
                var stateAfter = _systemStateService.Get_System_State_Struct();
                return Ok(new CommandResult
                {
                    Success = stateAfter.Laser.EmissionOn,
                    Code = stateAfter.Laser.EmissionOn ? "LASER_ON" : "LASER_ON_FAILED",
                    Message = stateAfter.Laser.EmissionOn ? "激光开启成功" : "激光开启失败",
                    State = null
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "开启激光时发生异常");
                var state = await _systemStateService.GetSystemStateAsync();
                return Ok(new CommandResult
                {
                    Success = false,
                    Code = "LASER_ON_EXCEPTION",
                    Message = $"开启激光时发生异常:{ex.Message}",
                    State = null
                });
            }
        }

        /// <summary>
        /// 关闭激光，返回统一命令响应
        /// </summary>
        /// <returns>CommandResult 包含最新系统状态</returns>
        [HttpPost("off")]
        public async Task<IActionResult> LaserOff()
        {
            try
            {
                _logger.LogInformation("关闭激光");
                _laser.LaserOff();
                var state = _systemStateService.Get_System_State_Struct();
                return Ok(new CommandResult
                {
                    Success = !state.Laser.EmissionOn,
                    Code = !state.Laser.EmissionOn ? "LASER_OFF" : "LASER_OFF_FAILED",
                    Message = !state.Laser.EmissionOn ? "激光已关闭" : "激光关闭失败",
                    State = null
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "关闭激光时发生异常");
                var state = await _systemStateService.GetSystemStateAsync();
                return Ok(new CommandResult
                {
                    Success = false,
                    Code = "LASER_OFF_EXCEPTION",
                    Message = $"关闭激光时发生异常:{ex.Message}",
                    State = null
                });
            }
        }

        /// <summary>
        /// 检查激光器连接状态
        /// 根据CniLaser实例的属性直接返回连接状态和发射状态，不依赖系统状态服务，避免潜在的性能问题
        /// </summary>
        /// <returns>连接状态</returns>
        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            try
            {
                // 直接返回激光器的连接状态和发射状态，不依赖系统状态服务
                return Ok(new
                {
                    Connected = _laser.IsConnected,
                    EmissionOn = _laser.IsEmissionOn,
                    PortName = _laser.PortName,
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查激光器连接状态时发生异常");
                return StatusCode(500, $"内部服务器错误：{ex.Message}");
            }
        }

        /// <summary>
        /// 更新激光雷达配置
        /// 然后更新内存中的全局配置实体，
        /// 再向前端返回最新的全局配置实体，方便前端展示当前配置状态；
        /// </summary>
        /// <param name="newConfig">新的激光雷达配置</param>
        /// <returns>更新后的配置</returns>
        [HttpPost("config/update")]
        public async Task<IActionResult> UpdateConfig([FromBody] RadarConfig newConfig)
        {
            if (newConfig == null)
            {
                return BadRequest("配置不能为空");
            }

            try
            {
                // 1. 写入配置文件
                _configHelper.WriteRadarConfig(newConfig);
            
                // 2. 更新内存中的全局配置
                _configHelper.ReadRadarDeviceConfig();
                
                _logger.LogInformation("激光雷达配置更新完成");
                return Ok(Program.RadarConfig);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"更新激光雷达配置时发生异常");
                return StatusCode(500, $"内部服务器错误：{ex.Message}");
            }
        }

        /// <summary>
        /// 发送读取配置指令，并通过配置助手读取配置文件中的激光雷达配置
        /// 并更新全局配置实体，
        /// 然后返回最新的全局配置实体给前端，方便前端展示当前配置状态
        /// </summary>
        /// <returns>客户端响应</returns>
        [HttpPost("config/read")]
        public async Task<IActionResult> Config()
        {
            //使用注入的配置助手读取配置文件中的激光雷达配置并更新全局配置实体
            _configHelper.ReadRadarDeviceConfig();
            return Ok(Program.RadarConfig); // 返回最新的全局配置实体（包含采集卡配置）给前端，方便前端展示当前配置状态  
        }

    }

}
