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
        public static void StartChildProcess(string bindIp)
        {
            string childExePath = Path.Combine(
            AppContext.BaseDirectory,  // 替换为可执行文件工作目录
            "ConsoleApp1.exe");      // 子进程可执行文件文件名（直接放在可执行文件同目录）

            // 校验文件是否存在
            if (!File.Exists(childExePath))
            {
                Program.logger.LogError($"自动查找子进程失败：未找到文件 {childExePath}");
                return;
            }

            // 主进程中启动子进程的正确方式
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = childExePath, // 子进程的完整路径
                Arguments = $"{bindIp} \"{ConfigHelper.ConfigFilePath}\"", // 两个参数：Grpc连接地址 + 配置文件路径   
                //UseShellExecute = false, // 必须为false才能重定向输出
                UseShellExecute = true,
                CreateNoWindow = false,   // 不创建新窗口
                //RedirectStandardOutput = true,  // 重定向输出以便调试
                //RedirectStandardError = true,   // 重定向错误以便调试
                WorkingDirectory = Path.GetDirectoryName(childExePath), // 关键：设置工作目录为子进程所在目录
            };


            Process p = new Process { StartInfo = startInfo };
            //// 可选：捕获子进程输出（调试用，无GC开销）
            //p.OutputDataReceived += (sender, e) =>
            //{
            //    if (!string.IsNullOrEmpty(e.Data)) Program.logger.LogDebug($"子进程输出：{e.Data}");
            //};
            //p.ErrorDataReceived += (sender, e) =>
            //{
            //    if (!string.IsNullOrEmpty(e.Data)) Program.logger.LogDebug($"子进程错误：{e.Data}");
            //};
            // 启动进程
            p.Start();
            ////启动输出监听（可选）
            //p.BeginOutputReadLine();
            //p.BeginErrorReadLine();

            Program.logger.LogInformation($"子进程已启动，传递参数：IP={bindIp}，等待子进程连接...");
        }

}
