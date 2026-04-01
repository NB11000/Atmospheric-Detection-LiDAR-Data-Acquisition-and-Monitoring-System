using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebAPI.Tools;
using WebAPI.Models;
using WebAPI.Service;
using SharedMemoryFramework;


namespace WebAPI                    
{
    class Program
    {
        /// <summary>
        /// 公开的静态【配置实体属性】，主窗口可以直接获取
        /// </summary>
        public static CaptureCardConfig CurrentConfig { get; set; } = new CaptureCardConfig();

        /// <summary>
        /// 日志
        /// </summary>
        public static ILogger logger {  get; private set; } =                        
                logger = LoggerFactory.Create(b => b.AddConsole()
                .SetMinimumLevel(LogLevel.Debug)).CreateLogger("[服务器]运行日志"); // 初始化日志

        /// <summary>
        /// Asp.net core 服务器实例
        /// </summary>
        public static WebApplication app {  get; private set; }


        static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // 读取具体值
            var env = builder.Configuration["ASPNETCORE_ENVIRONMENT"];
            // 动态获取10000以上端口号
            int port = Tool.GetAvailablePort(minPort: 10000);

            Console.WriteLine($"环境: {env}");  // 输出: Development

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();


            // 注册配置文件读写管理器（实例化的配置文件读写辅助类）
            builder.Services.AddSingleton<UISharedBuffer>(new UISharedBuffer());
            // 初始化Grpc服务端通信模块
            // 注册GrpcServiceImpl实例（GrpcService）为单例服务
            builder.Services.AddSingleton<GrpcServiceImpl>(new GrpcServiceImpl());
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

                //供局域网使用的HTTP/1.1端口（固定5135，方便前端调用）
                options.ListenAnyIP(5135, o =>
                {
                    // 启用HTTP/1.1
                    o.Protocols = HttpProtocols.Http1;
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

            app = builder.Build();



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


            app.UseHttpsRedirection();

            app.UseAuthorization();

            app.MapControllers();

            app.MapGet("/", () => $"后台grpc服务已启动，绑定HTTP/2端口{port}，HTTP/1.1端口5135");

            // 程序启动完成后，创建共享内存（必须加！）
            app.Lifetime.ApplicationStarted.Register(() =>
            {
                try
                { 
                    // 从DI容器中获取UI共享内存缓冲区实例
                    var uISharedBuffer = (UISharedBuffer)app.Services.GetRequiredService(typeof(UISharedBuffer));
                    // 初始化UI共享内存缓冲区，并创建UI共享内存，内存映射文件
                    uISharedBuffer.Create(1000);
                    // 启动子进程，并将主进程监听的ip地址转递给子进程
                    Tool.StartChildProcess($"http://localhost:{port}");
                    Program.logger.LogInformation($"http://localhost:{port}");
                }
                catch (Exception ex)
                {
                    Program.logger.LogInformation(ex.Message);
                }
            });

            app.Run();

            


        }
    }
}

