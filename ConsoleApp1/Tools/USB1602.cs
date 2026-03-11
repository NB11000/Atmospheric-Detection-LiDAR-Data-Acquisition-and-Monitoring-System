using System;
using System.Runtime.InteropServices;

namespace ConsoleApp1.Tools
{
    /// <summary>
    /// USB1602的 C API 函数声明
    /// </summary>
    public class USB1602
    {
        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "USB1602_OpenDevice", SetLastError = true)]
        public static extern nint USB1602_OpenDevice(int card_id);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "USB1602_GetID", SetLastError = true)]
        public static extern byte USB1602_GetID(nint hDevice, ref bool b_Success);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, SetLastError = true)]
        public static extern bool Writetousb(nint hDevice, byte addr, byte data);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, SetLastError = true)]
        public static extern byte Readfromusb(nint hDevice, byte addr, ref bool b_Success);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, SetLastError = true)]
        public static extern bool USB_VENDOR_OR_CLASS_REQUEST(nint hDevice, byte request, ushort value, int len, ref byte buffer);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, SetLastError = true)]
        public static extern bool USB1602_IOCTL_VENDOR_REQUEST(nint hDevice, byte request);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "DelayUs", SetLastError = true)]
        public static extern void DelayUs(int dly);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "USB1602_CloseDevice", SetLastError = true)]
        public static extern bool USB1602_CloseDevice(nint hDevice);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "USB1602_GetBoardVersion", SetLastError = true)]
        public static extern bool USB1602_GetBoardVersion(nint hDevice, byte[] BoardVer);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "xc_GetVersion", SetLastError = true)]
        public static extern double xc_GetVersion();

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "USB1602_RWEEp", SetLastError = true)]
        public static extern bool USB1602_RWEEp(nint hDevice, ushort addr, ushort len, byte[] buffer, bool bIsRead);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "USB1602_REEp_B", SetLastError = true)]
        public static extern bool USB1602_REEp_B(nint hDevice, ushort addr, ref byte buffer);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "USB1602_WEEp_B", SetLastError = true)]
        public static extern bool USB1602_WEEp_B(nint hDevice, ushort addr, ref byte buffer);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "USB1602_SetFreDiv", SetLastError = true)]
        public static extern bool USB1602_SetFreDiv(nint hDevice, ushort FreDiv);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "USB1602_SetClkSource", SetLastError = true)]
        public static extern bool USB1602_SetClkSource(nint hDevice, byte ClkSource);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "USB1602_SetADMode", SetLastError = true)]
        public static extern bool USB1602_SetADMode(nint hDevice, byte ADMode);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "USB1602_SetADCh", SetLastError = true)]
        public static extern bool USB1602_SetADCh(nint hDevice, byte ChSelect);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "USB1602_CLRRAM", SetLastError = true)]
        public static extern bool USB1602_CLRRAM(nint hDevice);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "USB1602_ADReset", SetLastError = true)]
        public static extern bool USB1602_ADReset(nint hDevice, byte ChSelect);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "USB1602_AD1RangeSet", SetLastError = true)]
        public static extern bool USB1602_AD1RangeSet(nint hDevice, byte ADRange);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "USB1602_AD2RangeSet", SetLastError = true)]
        public static extern bool USB1602_AD2RangeSet(nint hDevice, byte ADRange);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "Fun_DataToV", SetLastError = true)]
        public static extern void Fun_DataToV(ref double data, byte ADRange);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "USB1602_GetKB", SetLastError = true)]
        public static extern bool USB1602_GetKB(nint hDevice, byte ADChannel, byte ADRange, ref double k, ref double b);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "USB1602_SetFIFOValue", SetLastError = true)]
        public static extern bool USB1602_SetFIFOValue(nint hDevice, uint u32FIFOValue);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "USB1602_ADStart", SetLastError = true)]
        public static extern bool USB1602_ADStart(nint hDevice);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "USB1602_ADStop", SetLastError = true)]
        public static extern bool USB1602_ADStop(nint hDevice);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "USB1602_GetFIFOInfo", SetLastError = true)]
        public static extern bool USB1602_GetFIFOInfo(nint hDevice, ref byte u8Status);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "USB1602_BULK_READDelay", SetLastError = true)]
        public static extern bool USB1602_BULK_READDelay(int DelayMs);

        [DllImport("USB1602.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "USB1602_IOCTL_BULK_READ", SetLastError = true)]
        public static extern bool USB1602_IOCTL_BULK_READ(nint hDevice, nint pBuffer, uint BufferSize, ref uint pBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(
                nint hFile,
                byte[] Buffer,
                uint nNumberOfBytesToRead,
                ref uint pBytes ,
                nint lpOverlapped // 传入IntPtr.Zero表示同步操作
            );
    }
}