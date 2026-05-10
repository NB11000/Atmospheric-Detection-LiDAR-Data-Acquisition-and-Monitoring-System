using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using WebAPI.Hubs;
using WebAPI.Models;

namespace WebAPI.Service
{
    /// <summary>
    /// SignalR 统一推送服务
    /// 职责：封装所有 SignalR 消息推送逻辑
    /// </summary>
    public class SignalRHubPublisher : ISignalRHubPublisher
    {
        private readonly IHubContext<SystemStateHub> _hubContext;
        private readonly SystemStateService _stateService;
        private readonly ILogger<SignalRHubPublisher> _logger;

        public SignalRHubPublisher(
            IHubContext<SystemStateHub> hubContext,
            SystemStateService stateService,
            ILogger<SignalRHubPublisher> logger)
        {
            _hubContext = hubContext;
            _stateService = stateService;
            _logger = logger;
        }

        /// <summary>
        /// 推送状态变更事件（核心推送方法）
        /// </summary>
        public async Task PublishStateChangedAsync(string eventType, string source, string reason, string message)
        {
            try
            {
                // 从状态服务获取当前系统状态快照
                var state = _stateService.GetSystemState();
                
                var @event = new StateChangedEvent
                {
                    EventType = eventType,
                    Source = source,
                    Reason = reason,
                    Message = message,
                    State = state,
                    Timestamp = DateTime.Now
                };

                // 统一推送入口：使用 SignalRHub 定义的枚举事件名
                await _hubContext.Clients.All.SendAsync(
                    SystemStateHub.SignalREvent.StateChanged.ToString(), 
                    @event);
                
                _logger.LogDebug("推送状态变更: {EventType} ({Source})", eventType, source);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "推送状态变更事件失败: {EventType}", eventType);
            }
        }

        /// <summary>
        /// 推送设备报警事件
        /// </summary>
        public async Task PublishDeviceAlarmAsync(string alarmType, string device, string message, int severity)
        {
            try
            {
                var alarmEvent = new
                {
                    AlarmType = alarmType,
                    Device = device,
                    Message = message,
                    Severity = severity,
                    Timestamp = DateTime.Now
                };

                await _hubContext.Clients.All.SendAsync(
                    SystemStateHub.SignalREvent.DeviceAlarm.ToString(),
                    alarmEvent);
                
                _logger.LogWarning("设备报警: {Device} - {Message}", device, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "推送设备报警事件失败");
            }
        }

        /// <summary>
        /// 推送数据更新事件
        /// </summary>
        public async Task PublishDataUpdatedAsync(string dataType, object data)
        {
            try
            {
                await _hubContext.Clients.All.SendAsync(
                    SystemStateHub.SignalREvent.DataUpdated.ToString(),
                    new { DataType = dataType, Data = data, Timestamp = DateTime.Now });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "推送数据更新事件失败");
            }
        }
    }
}