using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading.Tasks;
using WebAPI.Service;

namespace WebAPI.Controllers
{
    /// <summary>
    /// 系统统一状态控制器
    /// </summary>
    [ApiController]
    [Route("api/system")]
    public class SystemStateController : ControllerBase
    {
        private readonly SystemStateService _systemStateService;
        private readonly IHostApplicationLifetime _lifetime;

        public SystemStateController(
            SystemStateService systemStateService,
            IHostApplicationLifetime lifetime)
        {
            _systemStateService = systemStateService;
            _lifetime = lifetime;
        }

        /// <summary>
        /// 获取系统统一状态快照
        /// </summary>
        [HttpGet("state")]
        public async Task<IActionResult> GetSystemState()
        {
            try
            {
                var state = await _systemStateService.GetSystemStateAsync();
                return Ok(state);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"内部服务器错误：{ex.Message}");
            }
        }

        /// <summary>
        /// 关闭 WebAPI 服务进程（Issue 03 新增）
        /// </summary>
        [HttpPost("shutdown")]
        public IActionResult Shutdown()
        {
            _lifetime.StopApplication();
            return Ok();
        }
    }
}
