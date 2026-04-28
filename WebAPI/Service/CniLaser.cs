using System;
using System.IO.Ports;
using Microsoft.Extensions.DependencyInjection;
using WebAPI.Service;
using WebAPI.Models;

namespace CniLaserControl
{
    public class CniLaser : IDisposable
    {
        private SerialPort _serialPort;
        private readonly byte[] _powerCommandBuffer = new byte[7]; // 55 AA 05 04 HH LL CS
        private readonly byte[] _freqCommandBuffer = new byte[6];  // 55 AA 04 00 FF CS
        private static readonly byte[] CommandLaserOn = { 0x55, 0xAA, 0x03, 0x01, 0x04 };
        private static readonly byte[] CommandLaserOff = { 0x55, 0xAA, 0x03, 0x00, 0x03 };
        private const int DefaultBaudRate = 9600;
        private ILogger<CniLaser> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly SignalRHubPublisher _hubPublisher;
        private readonly MqttEventPublisher _mqttEventPublisher;
        private bool _isEmissionOn;


        /// <summary>
        /// 获取用于激光器连接的串口状态
        /// </summary>
        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;

        /// <summary>
        /// 获取激光开关状态
        /// </summary>
        public bool IsEmissionOn => IsConnected && _isEmissionOn;

        /// <summary>
        /// 获取当前串口名称
        /// </summary>
        public string PortName => _serialPort?.PortName ?? string.Empty;

        public CniLaser(ILogger<CniLaser> logger, IServiceProvider serviceProvider, SignalRHubPublisher signalRHubPublisher, MqttEventPublisher mqttEventPublisher)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _hubPublisher = signalRHubPublisher;
            _mqttEventPublisher = mqttEventPublisher;
        }

        /// <summary>
        /// 打开串口连接激光器
        /// 从全局配置中获取串口号和波特率进行串口连接，并返回连接结果
        /// </summary>
        /// <param name="portName">串口号，例如 "COM3"</param>
        /// <param name="baudRate">波特率，根据设备手册设置，通常为9600或115200</param>
        public bool Connect(string portName, int baudRate = 9600)
        {
            try
            {
                // 记录连接参数
                _logger.LogInformation($"连接激光器串口: {portName}, 波特率: {baudRate}");
                
                // 断开现有连接
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.Close();
                }

                // 创建并打开串口
                _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 1000,              // 1秒读取超时
                    WriteTimeout = 1000,             // 1秒写入超时
                    Handshake = Handshake.None,      // 无流控（根据设备调整）
                    RtsEnable = true,                // RTS使能 - 关键！
                    DtrEnable = true,                // DTR使能 - 关键！
                    ReceivedBytesThreshold = 1,      // 实时响应
                    ReadBufferSize = 4096,           // 4KB缓冲区
                    WriteBufferSize = 4096           // 4KB缓冲区
                };
                _serialPort.Open();
                _isEmissionOn = false;

                // 等待串口稳定
                System.Threading.Thread.Sleep(100);
                _logger.LogInformation("串口连接成功");
                
                // 更新激光器状态缓存
                UpdateLaserStateCache();
                
                // MQTT 主通道：发布连接成功事件（异步不等待）
                // _ = _mqttEventPublisher.PublishStateChangedAsync("laser_connected", "laser", "激光串口连接成功", "激光器串口已连接");
                
                return true;
            }
            catch(Exception ex) 
            {
                _logger.LogError($"打开串口异常：{ex}");
                // 更新激光器状态缓存（确保状态为未连接）
                UpdateLaserStateCache();
                // MQTT 主通道：发布连接失败事件（异步不等待）
                // _ = _mqttEventPublisher.PublishStateChangedAsync("laser_connection_error", "laser", $"串口连接失败: {ex.Message}", "激光器串口连接失败");
                return false;
            }
        }


        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            _isEmissionOn = false;
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
                // 更新激光器状态缓存
                UpdateLaserStateCache();
                // MQTT 主通道：发布断开连接事件（异步不等待）
                // _ = _mqttEventPublisher.PublishStateChangedAsync("laser_disconnected", "laser", "激光串口主动断开", "激光器串口连接已断开");
            }
            else
            {
                // 串口未打开，仍需要更新缓存确保状态一致
                UpdateLaserStateCache();
            }
        }

        /// <summary>
        /// 自动扫描激光器连接的COM端口
        /// </summary>
        /// <returns>检测到的端口号，如果未找到则返回null</returns>
        public string AutoDetectPort()
        {
            _logger.LogInformation("开始自动检测激光器串口");
            string[] ports;
            try
            {
                ports = SerialPort.GetPortNames();
                _logger.LogInformation($"检测到串口: {string.Join(", ", ports)}");
                if (ports.Length == 0)
                {
                    _logger.LogInformation("未检测到可用串口");
                    return null;
                }
            }
            catch(Exception ex)
            {
                _logger.LogError($"获取串口列表失败: {ex.Message}");
                return null;
            }

            foreach (string port in ports)
            {
                _logger.LogInformation($"测试串口: {port}");
                SerialPort testPort = null;
                try
                {
                    // 创建临时串口进行测试
                    testPort = new SerialPort(port, DefaultBaudRate, Parity.None, 8, StopBits.One);
                    testPort.ReadTimeout = 500;  // 500毫秒读取超时
                    testPort.WriteTimeout = 500; // 500毫秒写入超时
                    testPort.Open();

                    // 等待串口稳定
                    System.Threading.Thread.Sleep(100);

                    // 发送激光关闭指令测试设备
                    _logger.LogInformation($"向串口 {port} 发送测试指令");
                    testPort.Write(CommandLaserOff, 0, CommandLaserOff.Length);

                    // 尝试读取响应（如果有响应说明可能是激光器）
                    System.Threading.Thread.Sleep(100);
                    if (testPort.BytesToRead > 0)
                    {
                        // 分配缓冲区读取响应（简单验证设备有响应）
                        byte[] buffer = new byte[testPort.BytesToRead];
                        testPort.Read(buffer, 0, buffer.Length);
                        _logger.LogInformation($"串口 {port} 收到响应: {BitConverter.ToString(buffer).Replace("-", " ")}");
                    }
                    else
                    {
                        _logger.LogInformation($"串口 {port} 无响应");
                    }

                    // 成功发送指令且没有异常，认为可能是激光器
                    testPort.Close();
                    testPort.Dispose();
                    _logger.LogInformation($"检测到激光器可能连接在串口: {port}");
                    return port;
                }
                catch(Exception ex)
                {
                    // 测试失败，继续测试下一个端口
                    _logger.LogInformation($"串口 {port} 测试失败: {ex.Message}");
                    testPort?.Close();
                    testPort?.Dispose();
                }
            }

            _logger.LogInformation("未检测到激光器");
            return null;
        }


        /// <summary>
        /// 设置激光功率 (指令1)
        /// </summary>
        /// <param name="powerMw">功率值，单位mW</param>
        /// <returns>是否发送成功</returns>
        public bool SetPower(int powerMw)
        {
            // 记录参数值
            _logger.LogInformation($"设置激光功率: {powerMw} mW");
            
            // 构建指令：55 AA 05 04 HH LL CS
            _powerCommandBuffer[0] = 0x55;  // 起始字节1
            _powerCommandBuffer[1] = 0xAA;  // 起始字节2
            _powerCommandBuffer[2] = 0x05;  // 长度/类型
            _powerCommandBuffer[3] = 0x04;  // 子命令
            _powerCommandBuffer[4] = (byte)((powerMw >> 8) & 0xFF); // 高8位
            _powerCommandBuffer[5] = (byte)(powerMw & 0xFF);        // 低8位
            // 计算校验和：05 + 04 + HH + LL (取低8位)
            _powerCommandBuffer[6] = (byte)(0x05 + 0x04 + _powerCommandBuffer[4] + _powerCommandBuffer[5]);
            
            return SendCommandNoAlloc(_powerCommandBuffer, 100);
        }



        /// <summary>
        /// 设置调制频率 (指令4)
        /// </summary>
        /// <param name="frequencyHz">频率值，单位Hz</param>
        /// <returns>是否发送成功</returns>
        public bool SetFrequency(int frequencyHz)
        {
            // 记录参数值
            _logger.LogInformation($"设置调制频率: {frequencyHz} Hz");
            
            // 构建指令：55 AA 04 00 FF CS
            _freqCommandBuffer[0] = 0x55;  // 起始字节1
            _freqCommandBuffer[1] = 0xAA;  // 启动字节2
            _freqCommandBuffer[2] = 0x04;  // 长度/类型
            _freqCommandBuffer[3] = 0x00;  // 子命令
            
            _freqCommandBuffer[4] = (byte)(frequencyHz & 0xFF); // 频率值（低8位）
            // 计算校验和：04 + 00 + FF (取低8位)
            _freqCommandBuffer[5] = (byte)(0x04 + 0x00 + _freqCommandBuffer[4]);
            
            return SendCommandNoAlloc(_freqCommandBuffer, 100);
        }



        /// <summary>
        /// 开启激光 (指令3)
        /// </summary>
        public bool LaserOn()
        {
            _logger.LogInformation("开启激光");
            bool success = SendCommandNoAlloc(CommandLaserOn, 100);
            if (success)
            {
                _isEmissionOn = true;
                // 更新激光器状态缓存
                UpdateLaserStateCache();
    
            }

            return success;
        }

        /// <summary>
        /// 关闭激光 (指令2)
        /// </summary>
        public bool LaserOff()
        {
            _logger.LogInformation("关闭激光");
            bool success = SendCommandNoAlloc(CommandLaserOff, 100);
            if (success)
            {
                _isEmissionOn = false;
                // 更新激光器状态缓存
                UpdateLaserStateCache();
                
            }

            return success;
        }



        /// <summary>
        /// 发送字节数组指令（无内存分配）
        /// </summary>
        /// <param name="buffer">预分配的指令缓冲区</param>
        /// <param name="delayAfterMs">发送后的延迟（毫秒）</param>
        /// <returns>是否发送成功</returns>
        private bool SendCommandNoAlloc(byte[] buffer, int delayAfterMs = 50)
        {
            if (_serialPort == null || !_serialPort.IsOpen)
            {
                _logger.LogError("串口未连接，无法发送命令");
                return false;
            }

            try
            {
                // 记录发送的命令内容以便调试
                string hexString = "";
                try
                {
                    hexString = BitConverter.ToString(buffer).Replace("-", " ");
                }
                catch (Exception hexEx)
                {
                    _logger.LogError($"转换命令为十六进制失败: {hexEx.Message}, 缓冲区长度: {buffer?.Length}");
                    hexString = "转换失败";
                }
                
                _logger.LogInformation($"发送激光器命令: {hexString}");
                
                // 添加详细调试信息
                _logger.LogDebug($"串口状态: IsOpen={_serialPort.IsOpen}, PortName={_serialPort.PortName}, BaudRate={_serialPort.BaudRate}");
                
                _serialPort.Write(buffer, 0, buffer.Length);
                _logger.LogInformation($"命令发送完成: {hexString}");
                
                // 添加延迟，确保设备有时间处理命令
                if (delayAfterMs > 0)
                {
                    System.Threading.Thread.Sleep(delayAfterMs);
                }
                
                return true;
            }
            catch(Exception ex)
            {
                _logger.LogError($"发送命令失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 发送原始命令（用于调试）
        /// </summary>
        /// <param name="hexCommand">十六进制字符串命令，如"55 AA 05 04 00 00 09"</param>
        /// <returns>是否发送成功</returns>
        public bool SendRawCommand(string hexCommand)
        {
            try
            {
                // 解析十六进制字符串
                string[] hexBytes = hexCommand.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                byte[] buffer = new byte[hexBytes.Length];
                
                for (int i = 0; i < hexBytes.Length; i++)
                {
                    buffer[i] = Convert.ToByte(hexBytes[i], 16);
                }
                
                _logger.LogInformation($"发送原始命令: {hexCommand}");
                return SendCommandNoAlloc(buffer, 100); // 增加延迟确保设备处理
            }
            catch (Exception ex)
            {
                _logger.LogError($"解析或发送原始命令失败: {ex.Message}, 命令: {hexCommand}");
                return false;
            }
        }

        /// <summary>
        /// 更新激光器状态缓存
        /// </summary>
        private void UpdateLaserStateCache()
        {
            try
            {
                var stateService = _serviceProvider.GetRequiredService<SystemStateService>();
                stateService.UpdateLaserState(state => new LaserStateDto
                {
                    SerialConnected = IsConnected,
                    EmissionOn = IsEmissionOn,
                    PortName = PortName,
                    LastMessage = IsConnected
                        ? (IsEmissionOn ? "激光已开启" : "激光串口已连接")
                        : "激光串口未连接",
                    Timestamp = DateTime.Now
                });
                _logger.LogDebug("激光器状态缓存已更新: SerialConnected={Connected}, EmissionOn={EmissionOn}", IsConnected, IsEmissionOn);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新激光器状态缓存失败");
            }
        }

        /// <summary>
        /// 异步发布激光器状态变更事件
        /// </summary>
        private async Task PublishLaserStateChangedAsync(string eventType, string reason, string message)
        {
            try
            {
                await _hubPublisher.PublishStateChangedAsync(
                    eventType,
                    "laser",
                    reason,
                    message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发布激光器状态变更事件失败");
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Disconnect();
        }
    }
}
