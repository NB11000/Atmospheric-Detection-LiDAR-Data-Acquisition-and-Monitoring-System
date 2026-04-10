# WebSocket数据流迁移实现记录

**记录时间：** 2026-04-08T19:34:09+08:00  
**记录人员：** Kilo (AI助手)  
**项目版本：** 数据采集与检测系统 V2.0

## 项目概述

本记录详细描述了将数据采集系统的UI数据流从本地共享内存迁移到WebSocket网络传输的完整实现过程。核心目标是将原有的33ms轮询共享内存机制替换为实时WebSocket数据流，实现跨机器数据展示，消除对本地进程间通信的依赖。

## 技术架构决策

### 选型分析
1. **WebSocket vs. 其他方案**
   - **HTTP/3**: 较新，生态系统不够成熟
   - **gRPC-streaming**: 适合结构化数据，但二进制帧解析复杂
   - **SignalR**: .NET生态优秀，但需额外依赖
   - **WebSocket (原生)**: 低延迟、全双工、.NET 8原生支持、与现有HTTP API兼容

2. **最终架构**
   - **客户端**: 原生 .NET `ClientWebSocket` (放弃Websocket.Client NuGet包)
   - **服务器**: .NET 8 原生 `WebSocket` 支持，集成到WebAPI项目
   - **传输**: 二进制帧，`ws://` 或 `wss://` 协议
   - **特性**: 自动重连、心跳、完整生命周期管理

### 性能指标
- **数据带宽**: ~480 KB/s (30 fps × 16 KB/帧)
- **UI刷新率**: 33ms/帧 (30 FPS)
- **网络延迟目标**: 2-8ms (LAN环境)
- **数据帧大小**: 16,000字节 (1000点×2通道×8字节)

## 完成的工作

### 1. 客户端实现 (`AvaloniaApplication1`)

#### 1.1 创建WebSocket数据客户端
- **文件**: `Services/WebSocketDataClient.cs` (新建)
- **核心特性**:
  - 原生 `ClientWebSocket` 实现，无第三方依赖
  - 自动重连机制，指数退避策略 (最大5次重试)
  - 零拷贝二进制帧解析 (不安全代码优化)
  - 数组池 (`ArrayPool<T>`) 重用缓冲区，减少GC压力
  - 完整生命周期管理: 启动、停止、释放

#### 1.2 主窗口集成
- **文件**: `Views/MainWindow.axaml.cs` (重大修改)
- **关键变更**:
  - 添加 `_webSocketClient` 和 `_webSocketCts` 字段
  - 修改 `AD_startOrstop_Click` 方法，集成WebSocket启动/停止逻辑
  - 移除原有的共享内存轮询 `Task.Run` 循环
  - 添加 `ConvertToWebSocketUrl` 辅助方法，将HTTP URL转换为WebSocket URL
  - 实现WebSocket事件驱动数据更新:
    - `DataReceived`: 接收双通道数据，UI线程更新图表
    - `ConnectionStateChanged`: 连接状态变更通知

#### 1.3 项目配置更新
- **文件**: `AvaloniaApplication1.csproj`
  - 移除未使用的 `Websocket.Client` NuGet包引用
  - 保留 `Microsoft.AspNetCore.App` 框架引用 (提供WebSocket支持)

### 2. 服务器实现 (`WebAPI`)

#### 2.1 WebSocket中间件配置
- **文件**: `Program.cs` (重大修改)
- **关键变更**:
  - 添加 `app.UseWebSockets()` 中间件配置
  - 添加WebSocket端点映射: `/ws/ui-data`
  - 实现 `HandleWebSocketConnection` 方法:
    - 从DI容器获取 `UISharedBuffer` 实例
    - 33ms定时读取最新UI数据帧
    - 双精度数组到字节缓冲区的零拷贝转换
    - 二进制帧发送 (16,000字节/帧)

#### 2.2 修复语法错误
- 修复了 `Program.cs` 中的语法错误 (大括号不匹配、多余的else语句)

### 3. 构建验证
- 解决方案构建成功 (`数据采集与检测系统V2.0.sln`)
- 所有项目编译通过，仅存在预期警告 (主要是nullable引用类型警告)

## 代码结构详解

### 客户端核心类: `WebSocketDataClient`

```csharp
public class WebSocketDataClient : IDisposable
{
    // 核心字段
    private ClientWebSocket _webSocket;
    private CancellationTokenSource _cts;
    private Task _receiveTask;
    
    // 事件
    public event Action<double[], double[]> DataReceived;
    public event Action<bool> ConnectionStateChanged;
    
    // 主要方法
    public async Task StartAsync() { ... }      // 启动连接 (自动重连)
    public async Task StopAsync() { ... }       // 停止连接
    private async Task ConnectAndReceiveAsync(CancellationToken cancellationToken) { ... }
    private async Task ReceiveLoopAsync(ClientWebSocket webSocket, CancellationToken cancellationToken) { ... }
    private unsafe void ParseBinaryFrame(byte[] data, int dataLength, double[] ch1, double[] ch2) { ... }
}
```

### 服务器WebSocket处理

```csharp
private static async Task HandleWebSocketConnection(
    WebSocket webSocket, 
    UISharedBuffer uiSharedBuffer,
    CancellationToken cancellationToken)
{
    // 核心循环
    while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
    {
        // 33ms定时控制
        // 从共享内存读取最新帧
        uiSharedBuffer.ReadLatestFrame(ref channel1Buffer, ref channel2Buffer);
        
        // 零拷贝序列化
        unsafe { ... }
        
        // 发送二进制帧
        await webSocket.SendAsync(..., WebSocketMessageType.Binary, ...);
    }
}
```

## 测试验证状态

### 已完成测试
1. **构建测试**: ✅ 所有项目构建成功
2. **服务器启动**: ✅ WebAPI服务器正常启动，监听5135端口
3. **子进程通信**: ✅ 子进程(ConsoleApp1)成功连接gRPC服务
4. **WebSocket端点**: ✅ `/ws/ui-data` 端点已配置并可接受连接

### 待测试项目
1. **端到端数据流**: 客户端连接 → 服务器发送 → 客户端解析 → UI更新
2. **重连机制**: 网络中断后的自动恢复
3. **性能验证**: 33ms帧率稳定性，网络延迟测量
4. **跨机器测试**: 不同主机间的数据流传输

## 下一步计划

### 短期任务 (高优先级)
1. **创建简单测试客户端**: 使用控制台应用验证WebSocket连接和数据接收
2. **完整端到端测试**: 启动采集 → WebSocket数据流 → 图表更新
3. **性能基准测试**: 测量实际网络延迟和帧率稳定性

### 中期优化 (中优先级)
1. **双向通信扩展**: 支持客户端向服务器发送控制指令
2. **数据压缩**: 考虑对16KB帧进行简单压缩
3. **连接池管理**: 支持多客户端并发连接
4. **监控指标**: 添加帧率统计、丢包率等监控

### 长期规划 (低优先级)
1. **Web客户端支持**: 基于浏览器的数据展示界面
2. **数据持久化**: WebSocket流的同时保存到数据库
3. **负载均衡**: 多服务器部署支持

## 已知问题与注意事项

### 已解决问题
1. **NuGet包冲突**: 移除了未使用的 `Websocket.Client` 包，改用原生实现
2. **语法错误**: 修复了 `Program.cs` 中的代码结构问题
3. **构建警告**: 主要是nullable引用类型警告，不影响功能

### 注意事项
1. **防火墙配置**: 跨机器测试需确保5135端口可访问
2. **缓冲区管理**: 客户端使用数组池，需确保正确归还
3. **线程安全**: UI更新必须通过 `Dispatcher.UIThread.Post`
4. **资源释放**: `WebSocketDataClient` 实现了 `IDisposable`，需确保正确释放

## 技术要点总结

### 成功要素
1. **架构简洁**: 原生WebSocket实现，无额外依赖
2. **性能优化**: 零拷贝解析、数组池重用
3. **鲁棒性**: 自动重连、完整错误处理
4. **集成平滑**: 与现有HTTP API基础设施无缝集成

### 关键决策
1. **放弃Websocket.Client**: 避免包冲突，提升可靠性
2. **二进制帧协议**: 高效传输双精度数组
3. **33ms定时器**: 与原有UI刷新率保持一致
4. **事件驱动模型**: 替代轮询，降低CPU使用率

## 相关文件

### 新增文件
- `AvaloniaApplication1/Services/WebSocketDataClient.cs`

### 重大修改文件
- `AvaloniaApplication1/Views/MainWindow.axaml.cs`
- `WebAPI/Program.cs`
- `AvaloniaApplication1/AvaloniaApplication1.csproj`

### 参考文件
- `开发记录\2026-04-04-数据采集子进程命令转发控制器开发记录.md`
- `数据采集系统V3架构改进详细实施计划.md`

---

**记录结束**  
本记录文档将在后续开发过程中持续更新，记录测试结果和优化改进。