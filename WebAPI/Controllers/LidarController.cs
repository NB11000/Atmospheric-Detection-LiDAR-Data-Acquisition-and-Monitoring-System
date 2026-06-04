using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using WebAPI.Tools;
using SharedModels;

namespace WebAPI.Controllers
{
    /// <summary>
    /// 反演算法配置API控制器
    /// 提供 LidarAlgorithmConfig 的读取、更新和默认值端点
    /// </summary>
    [ApiController]
    [Route("api/lidar")]
    public class LidarController : ControllerBase
    {
        private readonly ILogger<LidarController> _logger;
        private readonly ConfigHelper _configHelper;

        public LidarController(ILogger<LidarController> logger, ConfigHelper configHelper)
        {
            _logger = logger;
            _configHelper = configHelper;
        }

        /// <summary>
        /// 读取当前反演算法配置
        /// </summary>
        /// <returns>LidarAlgorithmConfig</returns>
        [HttpPost("config/read")]
        public IActionResult ReadConfig()
        {
            _configHelper.ReadLidarConfig();
            return Ok(Program.LidarConfig);
        }

        /// <summary>
        /// 更新反演算法配置并写入 DeviceConfig.json
        /// </summary>
        /// <param name="newConfig">新的反演算法配置</param>
        /// <returns>更新后的配置</returns>
        [HttpPost("config/update")]
        public IActionResult UpdateConfig([FromBody] LidarAlgorithmConfig newConfig)
        {
            if (newConfig == null)
            {
                return BadRequest("配置不能为空");
            }

            try
            {
                _configHelper.WriteLidarConfig(newConfig);
                _configHelper.ReadLidarConfig();
                _logger.LogInformation("反演算法配置更新完成");
                return Ok(Program.LidarConfig);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新反演算法配置时发生异常");
                return StatusCode(500, $"内部服务器错误：{ex.Message}");
            }
        }

        /// <summary>
        /// 返回反演算法配置的默认值
        /// </summary>
        /// <returns>LidarAlgorithmConfig 默认实例</returns>
        [HttpPost("config/default")]
        public IActionResult DefaultConfig()
        {
            return Ok(new LidarAlgorithmConfig());
        }
    }
}
