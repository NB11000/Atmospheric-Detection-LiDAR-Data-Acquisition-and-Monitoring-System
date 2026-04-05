using Serilog;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace WebAPI.Tools;

public class Tool
{
      /// <summary>
        /// 原生API实现的端口获取方法（零依赖、低GC）
        /// </summary>
        /// <param name="minPort"></param>
        /// <returns></returns>
        public static int GetAvailablePort(int minPort = 10000)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port >= minPort ? port : GetAvailablePort(minPort);
        }

        /// <summary>
        /// 启动子进程（对外暴露，UI线程调用）
        /// 改造StartChildProcess方法（核心：添加命令行参数）
        /// </summary>
        /// <param name="bindIp">gRPC绑定地址</param>
        /// <param name="parentProcessId">父进程ID（用于子进程监视），默认值-1表示不传递</param>
        /// <returns>启动的子进程Process对象，若启动失败返回null</returns>
        public static Process StartChildProcess(string bindIp, int parentProcessId = -1)
        {
            string childExePath = Path.Combine(
            AppContext.BaseDirectory,  // 替换为可执行文件工作目录
            "ConsoleApp1.exe");      // 子进程可执行文件文件名（直接放在可执行文件同目录）

            // 校验文件是否存在
            if (!File.Exists(childExePath))
            {
                Log.Error($"自动查找子进程失败：未找到文件 {childExePath}");
                return null;
            }

            // 构建命令行参数：IP + 配置文件路径 + 父进程ID
            string arguments = $"{bindIp} \"{ConfigHelper.ConfigFilePath}\" {parentProcessId}";

            // 主进程中启动子进程的正确方式
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = childExePath, // 子进程的完整路径
                Arguments = arguments, // 三个参数：Grpc连接地址 + 配置文件路径 + 父进程ID   
                UseShellExecute = true,
                CreateNoWindow = false,   // 不创建新窗口
                WorkingDirectory = Path.GetDirectoryName(childExePath), // 关键：设置工作目录为子进程所在目录
            };


            Process p = new Process { StartInfo = startInfo };
            // 启动进程
            p.Start();

            Log.Information($"子进程已启动，传递参数：IP={bindIp}，父进程ID={parentProcessId}，等待子进程连接...");
            return p;
        }

}
