using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;
using WebAPI.Models;

namespace WebAPI.Service
{
    /// <summary>
    /// MQTT 事件发布服务
    /// 替代 SignalRHubPublisher 作为系统状态变更事件的主推送通道
    /// MQTT 客户端实例由 MqttRpcBackgroundService 在连接建立后注入
    /// </summary>
    public class MqttEventPublisher
    {
        private readonly SystemStateService _stateService;
        private readonly IOptionsMonitor<MqttSettings> _mqttSettings;
        private readonly ILogger<MqttEventPublisher> _logger;

        /// <summary>
        /// JSON 序列化选项（紧凑格式，不缩进）
        /// </summary>
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// MQTT 客户端实例，由 MqttRpcBackgroundService 在连接建立后设置
        /// </summary>
        public IMqttClient? MqttClient { get; set; }

        /// <summary>
        /// 构造函数，注入系统状态服务和 MQTT 配置
        /// </summary>
        /// <param name="stateService">系统状态服务</param>
        /// <param name="mqttSettings">MQTT 配置选项监控器</param>
        /// <param name="logger">日志记录器</param>
        public MqttEventPublisher(
            SystemStateService stateService,
            IOptionsMonitor<MqttSettings> mqttSettings,
            ILogger<MqttEventPublisher> logger)
        {
            _stateService = stateService;
            _mqttSettings = mqttSettings;
            _logger = logger;
        }

        /// <summary>
        /// 推送状态变更事件到 MQTT（核心推送方法）
        /// 发布到主题：daq/{machineId}/events/state_changed
        /// QOS 1（至少一次），确保状态变更不丢失
        /// </summary>
        /// <param name="eventType">事件类型（如 collector_connected、laser_disconnected）</param>
        /// <param name="source">事件来源（collector / laser / system）</param>
        /// <param name="reason">事件原因描述</param>
        /// <param name="message">事件消息内容</param>
        public async Task PublishStateChangedAsync(string eventType, string source, string reason, string message)
        {
            if (MqttClient == null || !MqttClient.IsConnected)
            {
                _logger.LogDebug("MQTT 客户端未连接，跳过状态变更事件推送: {EventType}", eventType);
                return;
            }

            try
            {
                // 从状态服务获取当前系统状态快照
                var state = _stateService.GetSystemState();

                var @event = new MqttStateChangedEvent
                {
                    EventType = eventType,
                    Source = source,
                    Reason = reason,
                    Message = message,
                    State = state,
                    Timestamp = DateTime.Now
                };

                var payload = JsonSerializer.SerializeToUtf8Bytes(@event, _jsonOptions);
                var topic = $"daq/{_mqttSettings.CurrentValue.MachineId}/events/state_changed";

                var mqttMessage = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(payload)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build();

                await MqttClient.PublishAsync(mqttMessage);

                _logger.LogDebug("MQTT 状态变更事件已推送: {EventType} ({Source}) → {Topic}", eventType, source, topic);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT 推送状态变更事件失败: {EventType}", eventType);
            }
        }

        /// <summary>
        /// 推送设备报警事件到 MQTT
        /// 发布到主题：daq/{machineId}/events/device_alarm
        /// QOS 1 + 保留消息，确保新客户端也能收到最新报警
        /// </summary>
        /// <param name="alarmType">报警类型</param>
        /// <param name="device">设备名称</param>
        /// <param name="message">报警消息</param>
        /// <param name="severity">严重程度（1-5）</param>
        public async Task PublishDeviceAlarmAsync(string alarmType, string device, string message, int severity)
        {
            if (MqttClient == null || !MqttClient.IsConnected)
            {
                _logger.LogDebug("MQTT 客户端未连接，跳过设备报警事件推送");
                return;
            }

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

                var payload = JsonSerializer.SerializeToUtf8Bytes(alarmEvent, _jsonOptions);
                var topic = $"daq/{_mqttSettings.CurrentValue.MachineId}/events/device_alarm";

                var mqttMessage = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(payload)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithRetainFlag(true) // 保留消息，新订阅者也能获取最新报警
                    .Build();

                await MqttClient.PublishAsync(mqttMessage);

                _logger.LogWarning("MQTT 设备报警已推送: {Device} - {Message}（严重度: {Severity}）", device, message, severity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT 推送设备报警事件失败");
            }
        }

        /// <summary>
        /// 推送数据更新事件到 MQTT
        /// 发布到主题：daq/{machineId}/events/data_updated
        /// QOS 0（至多一次），允许丢失，保证低延迟
        /// </summary>
        /// <param name="dataType">数据类型标识</param>
        /// <param name="data">数据对象</param>
        public async Task PublishDataUpdatedAsync(string dataType, object data)
        {
            if (MqttClient == null || !MqttClient.IsConnected)
            {
                return;
            }

            try
            {
                var updateEvent = new
                {
                    DataType = dataType,
                    Data = data,
                    Timestamp = DateTime.Now
                };

                var payload = JsonSerializer.SerializeToUtf8Bytes(updateEvent, _jsonOptions);
                var topic = $"daq/{_mqttSettings.CurrentValue.MachineId}/events/data_updated";

                var mqttMessage = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(payload)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                    .Build();

                await MqttClient.PublishAsync(mqttMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT 推送数据更新事件失败");
            }
        }
    }
}
