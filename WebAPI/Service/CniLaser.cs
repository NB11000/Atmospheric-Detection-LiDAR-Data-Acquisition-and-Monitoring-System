using System;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace CniLaserControl
{
    public class CniLaser
    {
        private SerialPort _serialPort;

        // 指令定义
        private static readonly byte[] CommandLaserOn = { 0x55, 0xAA, 0x03, 0x01, 0x04 };
        private static readonly byte[] CommandLaserOff = { 0x55, 0xAA, 0x03, 0x00, 0x03 };

        /// <summary>
        /// 初始化激光器连接
        /// </summary>
        /// <param name="portName">串口号，例如 "COM3"</param>
        /// <param name="baudRate">波特率，根据设备手册设置，通常为9600或115200</param>
        public bool Connect(string portName, int baudRate = 9600)
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.Close();
                }

                _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
                _serialPort.ReadTimeout = 1000; // 1秒超时
                _serialPort.Open();

                // 等待串口稳定
                Thread.Sleep(100);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("连接失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
            }
        }

        /// <summary>
        /// 计算校验和 (指令中从第3字节开始的所有数据相加，取低8位)
        /// </summary>
        /// <param name="data">不包含起始标志的指令数据部分</param>
        /// <returns>校验和字节</returns>
        private byte CalculateChecksum(byte[] data)
        {
            int sum = 0;
            foreach (byte b in data)
            {
                sum += b;
            }
            return (byte)(sum & 0xFF); // 取低8位
        }

        /// <summary>
        /// 设置激光功率 (指令1)
        /// </summary>
        /// <param name="powerMw">功率值，单位mW</param>
        /// <returns>是否发送成功</returns>
        public bool SetPower(int powerMw)
        {
            try
            {
                // 将十进制功率转换为16位十六进制（高位在前，低位在后）
                byte highByte = (byte)((powerMw >> 8) & 0xFF); // 高8位
                byte lowByte = (byte)(powerMw & 0xFF);        // 低8位

                // 构建指令数据部分 (不包含55 AA)
                // 指令结构: 05 04 [High] [Low] [Checksum]
                byte[] data = { 0x05, 0x04, highByte, lowByte };
                byte checksum = CalculateChecksum(data);

                // 组装完整指令
                byte[] command = { 0x55, 0xAA, 0x05, 0x04, highByte, lowByte, checksum };

                SendCommand(command);
                Console.WriteLine($"功率设置指令发送成功: {powerMw} mW");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("设置功率失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 设置调制频率 (指令4)
        /// </summary>
        /// <param name="frequencyHz">频率值，单位Hz</param>
        /// <returns>是否发送成功</returns>
        public bool SetFrequency(int frequencyHz)
        {
            try
            {
                // 将十进制频率转换为字节
                byte freqByte = (byte)(frequencyHz & 0xFF); // 仅取低8位，假设频率 < 256

                // 构建指令数据部分 (不包含55 AA)
                // 指令结构: 04 00 [Freq] [Checksum]
                byte[] data = { 0x04, 0x00, freqByte };
                byte checksum = CalculateChecksum(data);

                // 组装完整指令
                byte[] command = { 0x55, 0xAA, 0x04, 0x00, freqByte, checksum };

                SendCommand(command);
                Console.WriteLine($"频率设置指令发送成功: {frequencyHz} Hz");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("设置频率失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 开启激光 (指令3)
        /// </summary>
        public bool LaserOn()
        {
            try
            {
                SendCommand(CommandLaserOn);
                Console.WriteLine("激光开启指令发送成功");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("开启激光失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 关闭激光 (指令2)
        /// </summary>
        public bool LaserOff()
        {
            try
            {
                SendCommand(CommandLaserOff);
                Console.WriteLine("激光关闭指令发送成功");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("关闭激光失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 发送字节数组指令
        /// </summary>
        /// <param name="command">完整的指令字节数组</param>
        private void SendCommand(byte[] command)
        {
            if (_serialPort == null || !_serialPort.IsOpen)
                throw new InvalidOperationException("串口未打开");

            _serialPort.Write(command, 0, command.Length);
            
            // 可选：读取设备返回的响应
            // ReadResponse();
        }

        /// <summary>
        /// 读取设备响应（示例）
        /// </summary>
        private void ReadResponse()
        {
            try
            {
                int bytes = _serialPort.BytesToRead;
                if (bytes > 0)
                {
                    byte[] buffer = new byte[bytes];
                    _serialPort.Read(buffer, 0, bytes);
                    Console.WriteLine("收到响应: " + BitConverter.ToString(buffer));
                }
            }
            catch (TimeoutException) { }
            catch (Exception ex) { Console.WriteLine("读取响应错误: " + ex.Message); }
        }
    }

    // --- 程序入口示例 ---
    class Program
    {
        static void Main(string[] args)
        {
            CniLaser laser = new CniLaser();

            // 1. 连接串口 (请根据实际情况修改COM端口号)
            if (laser.Connect("COM3", 9600))
            {
                // 2. 设置功率为 2000mW (对应指令1示例)
                laser.SetPower(2000);

                // 3. 设置频率为 15Hz (对应指令4示例)
                laser.SetFrequency(15);

                // 4. 开启激光 (对应指令3)
                laser.LaserOn();

                // 模拟运行
                Thread.Sleep(2000);

                // 5. 关闭激光 (对应指令2)
                laser.LaserOff();

                // 6. 断开连接
                laser.Disconnect();
            }
            else
            {
                Console.WriteLine("无法连接到激光器，请检查串口设置。");
            }

            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
        }
    }
}