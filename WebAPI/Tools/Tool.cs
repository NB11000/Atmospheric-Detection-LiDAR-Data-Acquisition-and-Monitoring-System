using Serilog;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using WebAPISharedMemoryFramework;

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


        /// <summary>
        /// 处理WebSocket连接，从共享内存读取数据并发送给客户端
        /// </summary>
        public static async Task HandleWebSocketConnection(
            WebSocket webSocket, 
            UISharedBuffer uiSharedBuffer,
            CancellationToken cancellationToken)
        {
            var logger = Program.app.Services.GetRequiredService<ILogger<Program>>();
            var buffer = new byte[16000]; // 1000点×2通道×8字节
            var channel1Buffer = new double[1000];
            var channel2Buffer = new double[1000];
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            try
            {
                logger.LogInformation("WebSocket UI数据流连接已建立");
                
                while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
                {

                    // 控制发送频率：每33毫秒一帧
                    var waitTime = 33 - stopwatch.ElapsedMilliseconds;
                    if (waitTime > 0)
                    {
                        await Task.Delay((int)waitTime, cancellationToken);
                    }
                    stopwatch.Restart();
                                        
                    // 从共享内存读取最新UI数据帧
                    uiSharedBuffer.ReadLatestFrame(ref channel1Buffer, ref channel2Buffer);
                    
                    // 将双精度数组复制到字节缓冲区
                    unsafe
                    {
                        fixed (double* pCh1 = channel1Buffer, pCh2 = channel2Buffer)
                        fixed (byte* pBuffer = buffer)
                        {
                            double* dest = (double*)pBuffer;
                            for (int i = 0; i < 1000; i++) dest[i] = pCh1[i];
                            for (int i = 0; i < 1000; i++) dest[1000 + i] = pCh2[i];
                        }
                    }
                    
                    // 发送二进制帧
                    await webSocket.SendAsync(
                        new ArraySegment<byte>(buffer, 0, buffer.Length),
                        WebSocketMessageType.Binary,
                        endOfMessage: true,
                        cancellationToken);
                    
                    // 可选：接收客户端控制指令（非阻塞检查）
                    // 注意：WebSocket类没有Available属性，可以通过异步接收实现
                    // 此处暂不实现双向通信
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("WebSocket连接已取消");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "WebSocket连接处理异常");
            }
            finally
            {
                logger.LogInformation("WebSocket UI数据流连接已关闭");
                stopwatch.Stop();
            }
        }

}
