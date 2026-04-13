using WebAPI.Tools;
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

        /// <summary>
        /// 构造函数，注入激光器服务,日志器和配置助手
        /// </summary>
        /// <param name="laser">激光器服务实例</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="configHelper">配置助手</param>
        public LaserController(CniLaserControl.CniLaser laser, ILogger<LaserController> logger,
                    ConfigHelper configHelper)
        {
            _laser = laser;
            _logger = logger;
            _configHelper = configHelper;
        }

        /// <summary>
        /// 打开串口连接激光器
        /// 从全局配置中获取串口号和波特率进行串口连接，并返回连接结果
        /// </summary>
        /// <returns>连接结果</returns>
        [HttpPost("connect")]
        public IActionResult Connect()
        {
            try
            {
                // 从全局配置中获取串口号和波特率
                var radarConfig = Program.RadarConfig;
                
                if (string.IsNullOrEmpty(radarConfig.SerialPort))
                {
                    return BadRequest("串口号未配置，请先在配置中设置串口号");
                }

                _logger.LogInformation($"尝试打开串口连接激光器，端口：{radarConfig.SerialPort}，波特率：{radarConfig.BaudRate}");
                bool success = _laser.Connect(radarConfig.SerialPort, radarConfig.BaudRate);
                if (success)
                {
                    _logger.LogInformation($"串口连接成功，端口：{radarConfig.SerialPort}");
                    return Ok(new { 
                        Message = "串口连接成功", 
                        Port = radarConfig.SerialPort, 
                        BaudRate = radarConfig.BaudRate 
                    });
                }
                else
                {
                    _logger.LogWarning($"串口连接失败，端口：{radarConfig.SerialPort}");
                    return StatusCode(500, "串口连接失败，请检查端口和波特率设置");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"打开串口连接激光器时发生异常");
                return StatusCode(500, $"内部服务器错误：{ex.Message}");
            }
        }

        /// <summary>
        /// 断开串口连接
        /// </summary>
        /// <returns>断开结果</returns>
        [HttpPost("disconnect")]
        public IActionResult Disconnect()
        {
            try
            {
                _logger.LogInformation("断开串口连接");
                _laser.Disconnect();
                return Ok(new { Message = "串口已断开连接" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "断开串口连接时发生异常");
                return StatusCode(500, $"内部服务器错误：{ex.Message}");
            }
        }

        /// <summary>
        /// 开启激光
        /// 在开启激光前，先从全局配置中获取激光功率和调制频率并设置
        /// </summary>
        /// <returns>操作结果</returns>
        [HttpPost("on")]
        public IActionResult LaserOn()
        {
            try
            {
                // 检查激光器是否已连接
                if (!_laser.IsConnected)
                {
                    return StatusCode(500, "激光器未连接，请先连接激光器");
                }

                // 从全局配置中获取激光功率和调制频率
                var radarConfig = Program.RadarConfig;
                
                // 验证配置
                if (radarConfig.LaserPower <= 0)
                {
                    return BadRequest("激光功率未配置或配置无效，请先设置激光功率");
                }

                if (radarConfig.LaserModulationFrequency <= 0)
                {
                    return BadRequest("激光调制频率未配置或配置无效，请先设置调制频率");
                }

                _logger.LogInformation($"开始设置激光参数：功率={radarConfig.LaserPower} mW，频率={radarConfig.LaserModulationFrequency} Hz");

                // 1. 设置激光功率
                bool powerSuccess = _laser.SetPower(radarConfig.LaserPower);
                if (!powerSuccess)
                {
                    _logger.LogWarning($"激光功率设置失败：{radarConfig.LaserPower} mW");
                    return StatusCode(500, "激光功率设置失败，请检查激光器连接状态");
                }
                _logger.LogInformation($"激光功率设置成功：{radarConfig.LaserPower} mW");

                // 2. 设置激光调制频率
                bool frequencySuccess = _laser.SetFrequency(radarConfig.LaserModulationFrequency);
                if (!frequencySuccess)
                {
                    _logger.LogWarning($"激光调制频率设置失败：{radarConfig.LaserModulationFrequency} Hz");
                    return StatusCode(500, "激光调制频率设置失败，请检查激光器连接状态");
                }
                _logger.LogInformation($"激光调制频率设置成功：{radarConfig.LaserModulationFrequency} Hz");

                // 3. 开启激光
                _logger.LogInformation("开启激光");
                bool laserOnSuccess = _laser.LaserOn();
                if (laserOnSuccess)
                {
                    _logger.LogInformation("激光开启成功");
                    return Ok(new { 
                        Message = "激光已开启",
                        PowerMw = radarConfig.LaserPower,
                        FrequencyHz = radarConfig.LaserModulationFrequency
                    });
                }
                else
                {
                    _logger.LogWarning("激光开启失败");
                    return StatusCode(500, "激光开启失败，请检查激光器连接状态");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "开启激光时发生异常");
                return StatusCode(500, $"内部服务器错误：{ex.Message}");
            }
        }

        /// <summary>
        /// 关闭激光
        /// </summary>
        /// <returns>操作结果</returns>
        [HttpPost("off")]
        public IActionResult LaserOff()
        {
            try
            {
                _logger.LogInformation("关闭激光");
                bool success = _laser.LaserOff();
                if (success)
                {
                    _logger.LogInformation("激光关闭成功");
                    return Ok(new { Message = "激光已关闭" });
                }
                else
                {
                    _logger.LogWarning("激光关闭失败");
                    return StatusCode(500, "激光关闭失败，请检查激光器连接状态");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "关闭激光时发生异常");
                return StatusCode(500, $"内部服务器错误：{ex.Message}");
            }
        }

        /// <summary>
        /// 检查激光器连接状态
        /// </summary>
        /// <returns>连接状态</returns>
        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            try
            {
                // 使用激光器连接状态属性
                bool isConnected = _laser.IsConnected;
                return Ok(new { Connected = isConnected, Timestamp = DateTime.Now });
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