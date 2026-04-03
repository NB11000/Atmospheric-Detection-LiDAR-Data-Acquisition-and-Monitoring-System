// 此文件已废弃，功能已合并到ClientController.cs中
// 保留此文件以避免编译错误，实际使用的是ClientController
using Microsoft.AspNetCore.Mvc;

namespace WebAPI.Controllers
{
    [ApiController]
    [Route("api/collector/command-deprecated")]
    public class CollectorCommandControllerDeprecated : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return NotFound("此控制器已废弃，请使用/api/collector/command");
        }
    }
}
