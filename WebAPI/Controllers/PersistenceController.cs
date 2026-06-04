using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using WebAPI.Tools;
using SharedModels;

namespace WebAPI.Controllers
{
    /// <summary>
    /// 持久化配置API控制器
    /// 提供 PersistenceSettings 的读取、更新和默认值端点
    /// </summary>
    [ApiController]
    [Route("api/persistence")]
    public class PersistenceController : ControllerBase
    {
        private readonly ILogger<PersistenceController> _logger;
        private readonly ConfigHelper _configHelper;

        public PersistenceController(ILogger<PersistenceController> logger, ConfigHelper configHelper)
        {
            _logger = logger;
            _configHelper = configHelper;
        }

        /// <summary>
        /// 读取当前持久化配置
        /// </summary>
        /// <returns>PersistenceSettings</returns>
        [HttpPost("config/read")]
        public IActionResult ReadConfig()
        {
            _configHelper.ReadPersistenceConfig();
            return Ok(Program.PersistenceConfig);
        }

        /// <summary>
        /// 更新持久化配置并写入 DeviceConfig.json
        /// </summary>
        /// <param name="newConfig">新的持久化配置</param>
        /// <returns>更新后的配置</returns>
        [HttpPost("config/update")]
        public IActionResult UpdateConfig([FromBody] PersistenceSettings newConfig)
        {
            if (newConfig == null)
            {
                return BadRequest("配置不能为空");
            }

            try
            {
                _configHelper.WritePersistenceConfig(newConfig);
                _configHelper.ReadPersistenceConfig();
                _logger.LogInformation("持久化配置更新完成");
                return Ok(Program.PersistenceConfig);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新持久化配置时发生异常");
                return StatusCode(500, $"内部服务器错误：{ex.Message}");
            }
        }

        /// <summary>
        /// 返回持久化配置的默认值
        /// </summary>
        /// <returns>PersistenceSettings 默认实例</returns>
        [HttpPost("config/default")]
        public IActionResult DefaultConfig()
        {
            var defaults = new PersistenceSettings();
            defaults.DataDirectory = Path.GetFullPath(defaults.DataDirectory);
            return Ok(defaults);
        }
    }
}
