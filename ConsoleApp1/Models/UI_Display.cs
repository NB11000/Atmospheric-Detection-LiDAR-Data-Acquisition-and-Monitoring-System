using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaApplication1.Models
{
    /// <summary>
    /// UI显示快照 结构体
    /// </summary>
    public struct UI_Display
    {
        public double[] Voltage1; // 通道1电压数据快照
        public double[] Voltage2; // 通道2电压数据快照
    }
}
