using Microsoft.AspNetCore.Mvc;
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

        public SystemStateController(SystemStateService systemStateService)
        {
            _systemStateService = systemStateService;
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
    }
}
