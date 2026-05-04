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

            systemStateService.AcquiringStateChanged += OnAcquiringStateChanged;
            systemStateService.MqttConnectionStateChanged += OnMqttConnectionStateChanged;
        }

        private void OnAcquiringStateChanged(bool isAcquiring)
        {
            _acquiring = isAcquiring;
            _logger.LogInformation("采集状态变更: {State}", isAcquiring ? "采集中" : "已停止");
            Apply();
        }

        private void OnMqttConnectionStateChanged(bool isConnected)
        {
            _mqttConnected = isConnected;
            _logger.LogInformation("MQTT 连接状态变更: {State}", isConnected ? "已连接" : "已断开");
            Apply();
        }

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
