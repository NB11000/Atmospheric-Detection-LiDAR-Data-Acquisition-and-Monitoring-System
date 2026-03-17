using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaApplication1.Models
{
    /// <summary>
    /// 原始数据块结构体
    /// </summary>
    public struct Data_Block
    {
        public byte[] Buffer;// 数据缓冲区

        // 写入的数据长度 
        // 如果同步双通道采样，则每个通道采样点=nBytes/4（每个采样点2字节）
        // 如果同步单通道采样，则每个通道采样点=nBytes/2（每个采样点2字节）        
        public int nBytes;

        public Data_Block(byte[] buffer, int nBytes1)
        {
            Buffer = buffer;  // 数据缓冲区
            nBytes = nBytes1; // 写入的数据长度
        }
    }
}
