using Microsoft.AspNetCore.SignalR;
using WebAPI.Service;

namespace WebAPI.Hubs
{
    /// <summary>
    /// 系统状态推送Hub
    /// </summary>
    public class SystemStateHub : Hub
    {
        private readonly SystemStateService _stateService;

        public SystemStateHub(SystemStateService stateService)
        {
            _stateService = stateService;
        }


        /// <summary>
        /// SignalR 消息名称/事件名 枚举
        /// </summary>
        public enum SignalREvent
        {
            StateChanged,      // 状态机状态变更
            DeviceAlarm,       // 设备报警
            DataUpdated,       // 数据上报
            ConnectionStatus   // 连接状态
        }


        /// <summary>
        /// 客户端连接时推送一次当前状态
        /// </summary>
/*         public override async Task OnConnectedAsync()
        {
            var state = await _stateService.GetSystemStateAsync();
            await Clients.Caller.SendAsync("StateChanged", new Models.StateChangedEvent
            {
                EventType = "connected",
                Source = "server",
                Message = "状态同步完成",
                State = state
            });
            await base.OnConnectedAsync();
        } */
    }
}
