using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using WebAPI.Tools;
using WebAPI.Models;
using WebAPI.Service;
using WebAPISharedMemoryFramework;
using Grpc.Core;
using Serilog;
using Serilog.Sinks.InMemory;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace WebAPI                    
{
    class Program
    {
        /// <summary>
        /// 公开的静态【配置实体属性】，主窗口可以直接获取
        /// </summary>
        public static CaptureCardConfig CurrentConfig { get; set; } = new CaptureCardConfig();

        /// <summary>
        /// 初始化日志 ：日志不再手动创建，统一由 DI 注入
        /// </summary>
        // public static ILogger logger { get; set; } = null!;

        /// <summary>
        /// Asp.net core 服务器实例
        /// </summary>
        public static WebApplication app {  get; private set; }

        /// <summary>
        /// 子进程Process实例
        /// </summary>
        public static Process ChildProcess { get; private set; }


        static void Main(string[] args)
        {


#region 服务器配置

            var builder = WebApplication.CreateBuilder(args);
            // 先清空所有系统默认日志（必须第一步！）
            builder.Logging.ClearProviders();

            // 配置日志（程序启动前就配置）
/*             Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()                  // 默认日志级别
                .Enrich.FromLogContext()                     //  enrich 上下文信息
                .WriteTo.Console()                           // 输出到控制台
                 .WriteTo.File(
                    path: "logs/log-.txt",                   // 日志文件路径
                    rollingInterval: RollingInterval.Day,    // 按天切割
                    fileSizeLimitBytes: 10485760,            // 单个文件最大 10MB
                    retainedFileCountLimit: 31,              // 最多保留31天
                    encoding: System.Text.Encoding.UTF8       // UTF8 避免中文乱码
                )
                 .WriteTo.InMemory()
                .CreateLogger(); */

            // 用 Serilog 替换默认日志（必须）
            builder.Host.UseSerilog((context, loggerConfig) =>
            {
                // 配置Serilog日志
                loggerConfig
                    .MinimumLevel.Information()                  // 默认日志级别
                    .Enrich.FromLogContext()                     //  enrich 上下文信息
                    .WriteTo.Console()                           // 输出到控制台
                    .WriteTo.File(
                        path: "logs/log-.txt",                   // 日志文件路径
                        rollingInterval: RollingInterval.Day,    // 按天切割
                        fileSizeLimitBytes: 10485760,            // 单个文件最大 10MB
                        retainedFileCountLimit: 31,              // 最多保留31天
                        encoding: System.Text.Encoding.UTF8       // UTF8 避免中文乱码
                    )
                    // ✅ 关键：内存日志必须在这里配置，才能全局生效
                    .WriteTo.InMemory();// 强制写入全局公开的内存日志
            });
            // Add services to the container.
            // 注册Grpc服务
            builder.Services.AddGrpc();
            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // 注册配置文件读写管理器（实例化的配置文件读写辅助类）
            builder.Services.AddSingleton<UISharedBuffer>();
            // 初始化Grpc服务端通信模块
            // 注册GrpcServiceImpl实例（GrpcService）为单例服务
            builder.Services.AddSingleton<GrpcServiceImpl>();
            // 注册配置文件读写管理器（实例化的配置文件读写辅助类）
            builder.Services.AddSingleton<ConfigHelper>();
            // 注册Options（绑定到CaptureCard）
            builder.Services.AddOptions()
                .Configure<CaptureCardConfig>(ConfigHelper.Config.GetSection("CaptureCard"));

            /* if (builder.Environment.IsProduction())
            {
                builder.Host.UseSerilog((context, config) =>
                {
                    config.ReadFrom.Configuration(context.Configuration)
                        .WriteTo.Console()
                        .WriteTo.File("logs/log-.txt",
                            rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: 7);
                });
            } */

            // 动态获取10000以上端口号
            int port = Tool.GetAvailablePort(minPort: 10000);

            builder.WebHost.ConfigureKestrel(options =>
            {
                //供Grpc使用的HTTP/2端口（动态获取，避免冲突）
                options.ListenLocalhost(port, o =>
                {
                    // 启用HTTP/2
                    o.Protocols = HttpProtocols.Http2;
                    // 开发环境自签名证书（HTTPS 是 HTTP/2 必需）
                    //o.UseHttps();
                });

                //供局域网使用的端口（固定5135，方便前端调用）
                options.ListenAnyIP(5135, o =>
                {
                    // 启用HTTP/1.1与HTTP/2（解决协议不匹配问题）
                    o.Protocols = HttpProtocols.Http1AndHttp2;
                    // 开发环境自签名证书（HTTPS 是 HTTP/2 必需）
                    //o.UseHttps();
                });

                //var Ip = IPAddress.Parse("127.0.0.1");
                //options.Listen(new IPEndPoint(Ip, port), o =>
                //{
                //    // 同时启用 HTTP/1.1 和 HTTP/2（解决协议不匹配问题）
                //    o.Protocols = HttpProtocols.Http1AndHttp2;
                //    // 开发环境自签名证书（HTTPS 是 HTTP/2 必需）
                //    o.UseHttps();
                //});
            });
#endregion

            // 读取具体值
            var env = builder.Configuration["ASPNETCORE_ENVIRONMENT"];
            Console.WriteLine($"环境: {env}");  // 输出: Development

            app = builder.Build();


            app.MapGrpcService<GrpcServiceImpl>();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                Console.WriteLine("当前环境: 开发环境");
                app.UseSwagger();
                app.UseSwaggerUI();
            }
            if (app.Environment.IsProduction())
            {
                Console.WriteLine("当前环境: 生产环境");
            }

            // ========== 打印本机局域网 IP ==========
            Console.WriteLine("\n==================================");
            Console.WriteLine("本机局域网地址：");
            foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    Console.WriteLine("👉 " + ip.ToString() + ":5135");
                }
            }
            Console.WriteLine("==================================\n");

            app.UseHttpsRedirection();

            app.UseAuthorization();

            app.MapControllers();

            // 启用WebSocket支持
            app.UseWebSockets(new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(30),
                AllowedOrigins = { "http://localhost:5135", "http://localhost:8080" } // 允许的源
            });
            
            // WebSocket端点：实时UI数据流
            app.Map("/ws/ui-data", async (HttpContext context) =>
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    // 从服务容器获取UI共享内存缓冲区
                    var uiSharedBuffer = context.RequestServices.GetRequiredService<UISharedBuffer>();
                    await HandleWebSocketConnection(webSocket, uiSharedBuffer, context.RequestAborted);
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync("此端点仅支持WebSocket连接");
                }
            });


            // 注册应用程序启动完成后事件，创建共享内存
            app.Lifetime.ApplicationStarted.Register(() =>
            {
                try
                { 
                    // 从DI容器中获取UI共享内存缓冲区实例
                    var uISharedBuffer = app.Services.GetRequiredService<UISharedBuffer>();
                    // 初始化UI共享内存缓冲区，并创建UI共享内存，内存映射文件
                    uISharedBuffer.Create(1000);
                    // 从DI容器中获取配置文件读写辅助类实例
                    var configHelper = app.Services.GetRequiredService<ConfigHelper>();
                    // 读取配置文件并更新全局配置实体
                    configHelper.ReadDeviceConfig();
                    // 启动子进程，并将主进程监听的ip地址转递给子进程
                    int parentProcessId = Process.GetCurrentProcess().Id;
                    ChildProcess = Tool.StartChildProcess($"http://localhost:{port}", parentProcessId);
                    Log.Information($"子进程已启动，PID={ChildProcess?.Id}, 监听地址: http://localhost:{port}");
                }
                catch (Exception ex)
                {
                    Log.Information(ex.Message);
                }
            });

            // 注册应用程序停止事件，优雅关闭子进程
            app.Lifetime.ApplicationStopping.Register(() =>
            {
                try
                {
                    if (ChildProcess == null)
                    {
                        Log.Information("未找到子进程实例");
                        return;
                    }

                    if (ChildProcess.HasExited)
                    {
                        Log.Information($"子进程已退出，退出代码: {ChildProcess.ExitCode}");
                        return;
                    }

                    Log.Information("应用程序正在停止，开始关闭子进程...");
                    var grpcService = app.Services.GetRequiredService<GrpcServiceImpl>();

                    try
                    {
                        // 发送退出指令并等待响应
                        var response = grpcService.SendCommandToClientAndWaitResponse(
                            "数据采集子进程",
                            "EXIT",
                            CancellationToken.None).GetAwaiter().GetResult();

                        if (response.Content == "EXIT_OK")
                        {
                            Log.Information("子进程已确认退出，等待进程退出...");
                            if (ChildProcess.WaitForExit(5000))
                            {
                                Log.Information($"子进程已优雅退出，退出代码: {ChildProcess.ExitCode}");
                            }
                            else
                            {
                                Log.Warning("子进程未在5秒内退出，强制终止");
                                ChildProcess.Kill();
                            }
                        }
                        else
                        {
                            Log.Warning($"子进程返回非EXIT_OK响应: {response.Content}，强制终止");
                            ChildProcess.Kill();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"通知子进程退出失败: {ex.Message}，强制终止子进程");
                        ChildProcess.Kill();
                    }
                }
                finally
                {
                    // 统一资源清理
                    ChildProcess?.Dispose();
                    ChildProcess = null;
                }
            });

            app.MapGet("/", () => $"后台grpc服务已启动，绑定HTTP/2端口{port}，HTTP/1.1端口5135");

             app.MapGet("/open", async (GrpcServiceImpl grpc) =>
            {
                await grpc.SendCommandToClientAndWaitResponse("数据采集子进程", "OPEN_DEVICE");
            });


            app.Run();
        }

        /// <summary>
        /// 处理WebSocket连接，从共享内存读取数据并发送给客户端
        /// </summary>
        private static async Task HandleWebSocketConnection(
            WebSocket webSocket, 
            UISharedBuffer uiSharedBuffer,
            CancellationToken cancellationToken)
        {
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
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
                    var elapsed = stopwatch.ElapsedMilliseconds;
                    if (elapsed < 33)
                    {
                        await Task.Delay(1, cancellationToken);
                        continue;
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
}