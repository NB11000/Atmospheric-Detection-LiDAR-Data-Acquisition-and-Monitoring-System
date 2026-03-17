using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaApplication1.Models
{
    /// <summary>
    /// 电压数据块结构体
    /// </summary>
    public struct Voltage_block
    {
        public double[] Voltage1; // 通道1电压数据
        public double[] Voltage2; // 通道2电压数据

        
        // 如果同步双通道采样，则每个通道采样点=nBytes/4（每个采样点2字节）
        // 如果单通道采样，则每个通道采样点=nBytes/2（每个采样点2字节）
        public double SampleCount;   // 每个通道的采样点数量
        public long StartTick;       // 起始时间戳（高精度）
    }
}
