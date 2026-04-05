using Microsoft.AspNetCore.Mvc;
using WebAPI.Service;
using AvaloniaApplication1.Grpc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using WebAPI.Tools;
using WebAPI.Models;

namespace WebAPI.Controllers
{
    /// <summary>
    /// 数据采集子进程命令转发控制器
    /// 专用于转发前端命令到数据采集子进程，客户端ID硬编码为"数据采集子进程"
    /// 原ClientController修改为专用命令转发控制器，移除多客户端支持
    /// </summary>
    [ApiController]
    [Route("api/collector/command")]
    public class ClientController : ControllerBase
    {
        /// <summary>
        /// 硬编码的客户端进程ID（与Program.cs中保持一致）
        /// </summary>
        private const string ClientId = "数据采集子进程";

        private readonly GrpcServiceImpl _grpcService;
        private readonly ILogger<ClientController> _logger;
        private readonly ConfigHelper _configHelper;

        /// <summary>
        /// 构造函数，注入gRPC服务、日志器和配置助手
        /// </summary>
        /// <param name="grpcService">gRPC服务实例</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="configHelper">配置文件助手</param>
        public ClientController(GrpcServiceImpl grpcService, ILogger<ClientController> logger, ConfigHelper configHelper)
        {
            _grpcService = grpcService;
            _logger = logger;
            _configHelper = configHelper;
        }

        /// <summary>
        /// 检查数据采集子进程是否已连接
        /// </summary>
        /// <returns>连接状态</returns>
        [HttpGet("status")]
        public IActionResult GetConnectionStatus()
        {
            try
            {
                bool isConnected = IsClientConnected();
                return Ok(new 
                { 
                    ClientId = ClientId,
                    Connected = isConnected,
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查数据采集子进程连接状态时发生异常");
                return StatusCode(500, $"内部服务器错误：{ex.Message}");
            }
        }

        /// <summary>
        /// 向数据采集子进程发送指令并等待响应
        /// </summary>
        /// <param name="command">指令内容（如OPEN_DEVICE、START_AD等）</param>
        /// <returns>客户端响应</returns>
        [HttpPost]
        public async Task<IActionResult> SendCommand([FromBody] string command)
        {
            if (string.IsNullOrEmpty(command))
            {
                return BadRequest("指令不能为空");
            }

            try
            {
                _logger.LogInformation($"向数据采集子进程发送指令：{command}");
                var response = await _grpcService.SendCommandToClientAndWaitResponse(ClientId, command);
                return Ok(response);
            }
            catch (Exception ex) when (ex.Message.Contains("未连接"))
            {
                _logger.LogWarning($"数据采集子进程未连接");
                return NotFound($"数据采集子进程未连接");
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning($"等待数据采集子进程响应超时");
                return StatusCode(504, $"等待客户端响应超时：{ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"向数据采集子进程发送指令[{command}]时发生异常");
                return StatusCode(500, $"内部服务器错误：{ex.Message}");
            }
        }

        /// <summary>
        /// 向数据采集子进程发送指令（异步，不等待响应）
        /// </summary>
        /// <param name="command">指令内容</param>
        /// <returns>202 Accepted</returns>
        [HttpPost("async")]
        public async Task<IActionResult> SendCommandAsync([FromBody] string command)
        {
            if (string.IsNullOrEmpty(command))
            {
                return BadRequest("指令不能为空");
            }

            try
            {
                _logger.LogInformation($"向数据采集子进程异步发送指令：{command}");
                await _grpcService.SendCommandToClient(ClientId, command);
                return Accepted();
            }
            catch (Exception ex) when (ex.Message.Contains("未连接"))
            {
                _logger.LogWarning($"数据采集子进程未连接");
                return NotFound($"数据采集子进程未连接");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"向数据采集子进程异步发送指令[{command}]时发生异常");
                return StatusCode(500, $"内部服务器错误：{ex.Message}");
            }
        }

        /// <summary>
        /// 发送打开采集卡设备指令
        /// </summary>
        /// <returns>客户端响应</returns>
        [HttpPost("open")]
        public async Task<IActionResult> OpenDevice()
        {
            return await SendCommand("OPEN_DEVICE");
        }

        /// <summary>
        /// 发送重新打开采集卡设备指令
        /// </summary>
        /// <returns>客户端响应</returns>
        [HttpPost("open-again")]
        public async Task<IActionResult> OpenDeviceAgain()
        {
            return await SendCommand("OPEN_DEVICE_AGAIN");
        }

        /// <summary>
        /// 发送关闭采集卡设备指令
        /// </summary>
        /// <returns>客户端响应</returns>
        [HttpPost("close")]
        public async Task<IActionResult> CloseDevice()
        {
            return await SendCommand("CLOSE_DEVICE");
        }

        /// <summary>
        /// 发送开始采集指令
        /// </summary>
        /// <returns>客户端响应</returns>
        [HttpPost("start")]
        public async Task<IActionResult> StartAd()
        {
            return await SendCommand("START_AD");
        }

        /// <summary>
        /// 发送停止采集指令
        /// </summary>
        /// <returns>客户端响应</returns>
        [HttpPost("stop")]
        public async Task<IActionResult> StopAd()
        {
            return await SendCommand("STOP_AD");
        }

        /// <summary>
        /// 发送心跳检测指令
        /// </summary>
        /// <returns>客户端响应</returns>
        [HttpPost("ping")]
        public async Task<IActionResult> Ping()
        {
            return await SendCommand("PING");
        }

        /// <summary>
        /// 发送优雅退出指令
        /// </summary>
        /// <returns>客户端响应</returns>
        [HttpPost("exit")]
        public async Task<IActionResult> Exit()
        {
            return await SendCommand("EXIT");
        }

        /// <summary>
        /// 发送读取配置指令，并通过配置助手读取配置文件更新全局配置实体，
        /// 然后返回最新的全局配置实体（包含采集卡配置）给前端，方便前端展示当前配置状态
        /// </summary>
        /// <returns>客户端响应</returns>
        [HttpPost("config/read")]
        public async Task<IActionResult> Config()
        {
            await SendCommand("CONFIG_READ");
            // 使用注入的配置助手读取配置文件并更新全局配置实体
            _configHelper.ReadDeviceConfig();
            return Ok(Program.CurrentConfig); // 返回最新的全局配置实体（包含采集卡配置）给前端，方便前端展示当前配置状态  
        }

        /// <summary>
        /// 更新采集卡配置，同时通知数据采集子进程配置已更新，
        /// 然后更新内存中的全局配置实体，
        /// 再向前端返回最新的全局配置实体（包含采集卡配置），方便前端展示当前配置状态；
        /// </summary>
        /// <param name="newConfig">新的采集卡配置</param>
        /// <returns>更新后的配置</returns>
        [HttpPost("config/update")]
        public async Task<IActionResult> UpdateConfig([FromBody] CaptureCardConfig newConfig)
        {
            if (newConfig == null)
            {
                return BadRequest("配置不能为空");
            }

            try
            {
                // 1. 写入配置文件
                _configHelper.WriteCaptureCardConfig(newConfig);
                
                // 2. 通知数据采集子进程配置已更新
                await _grpcService.SendCommandToClientAndWaitResponse(ClientId, "CONFIG_UPDATE");
                
                // 3. 更新内存中的全局配置
                _configHelper.ReadDeviceConfig();
                
                _logger.LogInformation("采集卡配置更新完成");
                return Ok(Program.CurrentConfig);
            }
            catch (Exception ex) when (ex.Message.Contains("未连接"))
            {
                _logger.LogWarning($"数据采集子进程未连接，但配置已保存");
                // 即使子进程未连接，配置也已保存，返回成功
                _configHelper.ReadDeviceConfig();
                return Ok(Program.CurrentConfig);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"更新采集卡配置时发生异常");
                return StatusCode(500, $"内部服务器错误：{ex.Message}");
            }
        }

        /// <summary>
        /// 获取默认采集卡配置（注意此时并没有更新全局配置实体，和更新配置文件）
        /// </summary>
        /// <returns>默认配置</returns>
        [HttpGet("config/default")]
        public IActionResult GetDefaultConfig()
        {
            try
            {
                _logger.LogInformation("获取默认采集卡配置");
                // 创建新的CaptureCardConfig实例，构造函数会自动设置默认值
                var defaultConfig = new CaptureCardConfig();
                return Ok(defaultConfig);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取默认配置时发生异常");
                return StatusCode(500, $"内部服务器错误：{ex.Message}");
            }
        }

        /// <summary>
        /// 检查数据采集子进程是否在连接池中
        /// </summary>
        /// <returns>是否已连接</returns>
        private bool IsClientConnected()
        {
            try
            {
                var clients = _grpcService.GetConnectedClients();
                return Array.Exists(clients, id => id == ClientId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查客户端连接状态时发生异常");
                return false;
            }
        }
    }
}
