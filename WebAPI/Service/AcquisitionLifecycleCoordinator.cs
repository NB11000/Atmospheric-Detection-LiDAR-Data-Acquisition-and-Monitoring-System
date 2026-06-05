using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace WebAPI.Service
{
    public class AcquisitionLifecycleCoordinator
    {
        private readonly IEnumerable<IAcquisitionBoundService> _services;
        private readonly ILogger<AcquisitionLifecycleCoordinator> _logger;
        private bool _acquiring;
        private bool _mqttConnected;

        public AcquisitionLifecycleCoordinator(
            IEnumerable<IAcquisitionBoundService> services,
            SystemStateService systemStateService,
            ILogger<AcquisitionLifecycleCoordinator> logger)
        {
            _services = services;
            _logger = logger;

            // 从当前系统状态快照拉取初始 MQTT 连接状态，避免晚创建时错过已触发的连接事件
            _mqttConnected = systemStateService.GetSystemState().Server.IsMqttConnected;

            systemStateService.AcquiringStateChanged += OnAcquiringStateChanged;
            systemStateService.MqttConnectionStateChanged += OnMqttConnectionStateChanged;
        }

        /// <summary>
        /// 根据采集状态和 MQTT 连接状态，协调启动/停止绑定服务
        /// </summary>
        /// <param name="isAcquiring"></param>
        private void OnAcquiringStateChanged(bool isAcquiring)
        {
            _acquiring = isAcquiring;
            _logger.LogInformation("采集状态变更: {State}", isAcquiring ? "采集中" : "已停止");
            Apply();
        }
        
        /// <summary>
        /// 根据采集状态和 MQTT 连接状态，协调启动/停止绑定服务
        /// </summary>
        /// <param name="isConnected"></param>
        private void OnMqttConnectionStateChanged(bool isConnected)
        {
            _mqttConnected = isConnected;
            _logger.LogInformation("MQTT 连接状态变更: {State}", isConnected ? "已连接" : "已断开");
            Apply();
        }

        /// <summary>
        /// 根据当前采集状态和 MQTT 连接状态，决定启动或停止每个绑定服务
        /// </summary>
        private void Apply()
        {
            foreach (var service in _services)
            {
                bool canRun = _acquiring && (!service.RequiresMqttConnection || _mqttConnected);
                if (canRun)
                    service.Start();
                else
                    service.Stop();
            }
        }
    }
}
