using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WebSocket测试客户端
{
    /// <summary>
    /// 简单的WebSocket测试客户端，用于验证数据采集系统的WebSocket数据流
    /// 连接地址: ws://localhost:5135/ws/ui-data
    /// 数据格式: 16,000字节二进制帧 (1000点×2通道×8字节)
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== WebSocket数据流测试客户端 ===");
            Console.WriteLine("目标服务器: ws://localhost:5135/ws/ui-data");
            Console.WriteLine("数据格式: 16,000字节/帧 (2通道×1000点×8字节)");
            Console.WriteLine("按Ctrl+C停止测试\n");
            
            string serverUrl = "ws://localhost:5135/ws/ui-data";
            
            if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
            {
                serverUrl = args[0];
                Console.WriteLine($"使用自定义地址: {serverUrl}");
            }
            
            try
            {
                await TestWebSocketConnection(serverUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ 测试失败: {ex.Message}");
                Console.WriteLine($"堆栈跟踪: {ex.StackTrace}");
                Environment.Exit(1);
            }
        }
        
        /// <summary>
        /// 测试WebSocket连接和数据接收
        /// </summary>
        static async Task TestWebSocketConnection(string serverUrl)
        {
            using var client = new ClientWebSocket();
            using var cts = new CancellationTokenSource();
            
            // 设置Ctrl+C处理
            Console.CancelKeyPress += (sender, e) =>
            {
                Console.WriteLine("\n🛑 收到停止信号，正在关闭连接...");
                cts.Cancel();
                e.Cancel = true;
            };
            
            Console.WriteLine($"\n🔗 正在连接到 {serverUrl} ...");
            
            try
            {
                await client.ConnectAsync(new Uri(serverUrl), cts.Token);
                Console.WriteLine("✅ 连接成功！");
                Console.WriteLine($"连接状态: {client.State}");
                Console.WriteLine($"子协议: {client.SubProtocol ?? "(无)"}");
                
                // 开始接收数据
                await ReceiveDataAsync(client, cts.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("\n⏹️ 连接已取消");
            }
            catch (WebSocketException wsEx)
            {
                Console.WriteLine($"\n❌ WebSocket错误: {wsEx.Message}");
                Console.WriteLine($"WebSocket错误代码: {wsEx.WebSocketErrorCode}");
            }
            finally
            {
                if (client.State == WebSocketState.Open)
                {
                    Console.WriteLine("正在关闭WebSocket连接...");
                    await client.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "测试完成",
                        CancellationToken.None);
                }
                Console.WriteLine("测试客户端已退出");
            }
        }
        
        /// <summary>
        /// 接收并分析数据帧
        /// </summary>
        static async Task ReceiveDataAsync(ClientWebSocket client, CancellationToken cancellationToken)
        {
            var buffer = new byte[16000]; // 预期帧大小
            var stopwatch = Stopwatch.StartNew();
            long totalFrames = 0;
            long totalBytes = 0;
            long lastReportTime = 0;
            const long reportIntervalMs = 1000; // 每秒报告一次
            
            Console.WriteLine("\n📊 开始接收数据帧...");
            Console.WriteLine("预期帧大小: 16,000字节 (1000点×2通道×8字节)");
            Console.WriteLine("------------------------------------------------");
            
            try
            {
                while (!cancellationToken.IsCancellationRequested && client.State == WebSocketState.Open)
                {
                    // 接收完整消息
                    var result = await client.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        cancellationToken);
                    
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Console.WriteLine("\n收到关闭帧，连接即将关闭");
                        break;
                    }
                    
                    if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        totalFrames++;
                        totalBytes += result.Count;
                        
                        // 验证帧大小
                        if (result.Count != 16000)
                        {
                            Console.WriteLine($"⚠️  警告: 收到异常帧大小 {result.Count}字节 (预期16,000字节)");
                        }
                        
                        // 解析并验证数据
                        if (result.Count >= 16) // 至少检查前两个double
                        {
                            unsafe
                            {
                                fixed (byte* pBuffer = buffer)
                                {
                                    double* doubles = (double*)pBuffer;
                                    double ch1First = doubles[0];
                                    double ch2First = doubles[1000];
                                    
                                    // 每100帧显示一次样本值
                                    if (totalFrames % 100 == 1)
                                    {
                                        Console.WriteLine($"📈 样本值 - 通道1[0]: {ch1First:F6}, 通道2[0]: {ch2First:F6}");
                                    }
                                }
                            }
                        }
                        
                        // 每秒报告统计信息
                        long currentTime = stopwatch.ElapsedMilliseconds;
                        if (currentTime - lastReportTime >= reportIntervalMs)
                        {
                            double fps = totalFrames / (currentTime / 1000.0);
                            double mbps = (totalBytes * 8.0) / (currentTime / 1000.0) / 1_000_000.0;
                            
                            Console.WriteLine($"📊 统计: {totalFrames}帧, {fps:F1}FPS, {mbps:F2}Mbps");
                            lastReportTime = currentTime;
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        Console.WriteLine($"📝 收到文本消息: {text}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消，不报错
            }
            
            stopwatch.Stop();
            double totalSeconds = stopwatch.ElapsedMilliseconds / 1000.0;
            
            Console.WriteLine("\n" + new string('=', 50));
            Console.WriteLine("📋 测试结果汇总");
            Console.WriteLine(new string('=', 50));
            Console.WriteLine($"运行时间: {totalSeconds:F2}秒");
            Console.WriteLine($"总接收帧数: {totalFrames}");
            Console.WriteLine($"总接收字节: {totalBytes:N0}字节 ({totalBytes / 1024.0 / 1024.0:F2}MB)");
            
            if (totalSeconds > 0)
            {
                double avgFps = totalFrames / totalSeconds;
                double avgMbps = (totalBytes * 8.0) / totalSeconds / 1_000_000.0;
                Console.WriteLine($"平均帧率: {avgFps:F1} FPS");
                Console.WriteLine($"平均带宽: {avgMbps:F2} Mbps");
                Console.WriteLine($"平均帧大小: {(totalBytes / (double)Math.Max(totalFrames, 1)):F0} 字节");
            }
            
            Console.WriteLine($"最终连接状态: {client.State}");
        }
    }
}