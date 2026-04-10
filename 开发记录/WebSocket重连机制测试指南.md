# WebSocket重连机制测试指南

**创建时间：** 2026-04-08  
**测试目标：** 验证WebSocket数据客户端的自动重连机制和错误处理能力

## 重连机制概述

### 实现原理
`WebSocketDataClient` 实现了以下重连特性：
1. **指数退避策略**：重试延迟 = 1000ms × 2^(n-1)（n为当前重试次数）
2. **最大重试次数**：5次
3. **连接状态事件**：`ConnectionStateChanged` 事件通知
4. **无缝恢复**：连接恢复后继续接收数据

### 关键代码
```csharp
private async Task ConnectAndReceiveAsync(CancellationToken cancellationToken)
{
    int retryCount = 0;
    const int maxRetryCount = 5;
    const int baseRetryDelay = 1000;
    
    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            // 连接尝试
            await webSocket.ConnectAsync(new Uri(_serverUrl), cancellationToken);
            retryCount = 0; // 重置重试计数
            
            // 接收数据循环
            await ReceiveLoopAsync(webSocket, cancellationToken);
        }
        catch (Exception ex)
        {
            // 指数退避重连
            if (retryCount < maxRetryCount)
            {
                retryCount++;
                int delay = baseRetryDelay * (int)Math.Pow(2, retryCount - 1);
                await Task.Delay(delay, cancellationToken);
            }
            else
            {
                // 达到最大重试次数，停止重连
                break;
            }
        }
    }
}
```

## 测试场景

### 场景1：网络临时中断
**测试目的**：验证客户端在网络短暂中断后能自动恢复

**测试步骤**：
1. 建立正常WebSocket连接和数据流
2. 禁用网络适配器（或断开网线）
3. 等待10-30秒
4. 恢复网络连接
5. 观察重连过程和数据恢复

**预期结果**：
- 网络中断后，`ConnectionStateChanged(false)` 事件触发
- 状态显示"WebSocket数据流断开，尝试重连..."
- 恢复网络后自动重连成功
- `ConnectionStateChanged(true)` 事件触发
- 数据流恢复，无数据丢失（缓冲区可能丢失部分数据）

### 场景2：服务器重启
**测试目的**：验证服务器重启后客户端能自动重连

**测试步骤**：
1. 建立正常连接和数据流
2. 停止WebAPI服务器（Ctrl+C）
3. 等待客户端检测到断开
4. 重启WebAPI服务器
5. 观察重连过程

**预期结果**：
- 服务器停止后，客户端检测到连接断开
- 开始重试连接（指数退避）
- 服务器重启后，客户端在下次重试时连接成功
- 数据流恢复

### 场景3：服务器不可达（长时间）
**测试目的**：验证最大重试次数机制

**测试步骤**：
1. 建立正常连接
2. 停止服务器并确保不会重启
3. 观察客户端重试行为至少10分钟
4. 记录重试次数和间隔

**预期结果**：
- 客户端尝试重连5次（可配置）
- 重试间隔：1s, 2s, 4s, 8s, 16s
- 达到最大重试次数后停止尝试
- 最终状态显示连接失败

### 场景4：认证/权限错误
**测试目的**：验证不可恢复错误的处理

**测试步骤**：
1. 配置错误的服务器URL（如不存在的端点）
2. 尝试连接
3. 观察错误处理和重试行为

**预期结果**：
- 连接立即失败（非网络错误）
- 可能不会触发重试，或快速失败
- 适当的错误消息显示

### 场景5：资源清理测试
**测试目的**：验证连接断开时的资源正确释放

**测试步骤**：
1. 建立多个WebSocket连接
2. 断开网络
3. 监控内存和句柄使用情况
4. 恢复网络
5. 检查资源是否泄漏

**预期结果**：
- 断开时正确释放WebSocket资源
- 内存使用稳定，无持续增长
- 句柄数量恢复正常

## 测试工具与监控

### 1. 日志监控
启用详细日志记录，观察重连过程：
```csharp
// 在WebSocketDataClient构造函数中
_logger?.LogInformation($"WebSocket数据客户端已初始化，服务器: {serverUrl}");

// 在连接过程中
_logger?.LogInformation($"将在{delay}毫秒后尝试重连 (重试 {retryCount}/{maxRetryCount})");
```

### 2. 性能计数器
监控以下指标：
- 连接建立时间
- 重试次数统计
- 断开持续时间
- 数据丢失率

### 3. 网络模拟工具
使用以下工具模拟网络故障：
- **Clumsy**：网络延迟、丢包模拟
- **Windows防火墙**：临时阻止端口
- **网络适配器禁用/启用**

## 测试验收标准

### 必须满足
- [ ] 网络中断30秒内能自动恢复连接
- [ ] 重连后数据流能恢复正常
- [ ] 达到最大重试次数后优雅停止
- [ ] 无资源泄漏（内存、句柄）
- [ ] 错误信息清晰，便于排查

### 建议满足
- [ ] 重连成功率 > 95%
- [ ] 平均重连时间 < 10秒
- [ ] 支持多次重复断开/重连
- [ ] 不影响应用程序其他功能

## 详细测试用例

### 用例1：快速网络闪断
**描述**：模拟网络瞬间断开（<1秒）后恢复  
**步骤**：
1. 正常数据流运行
2. 使用Clumsy模拟100ms网络断开
3. 观察客户端反应

**预期**：
- 可能检测不到断开（短于心跳间隔）
- 如检测到断开，应快速重连
- 数据流短暂中断后恢复

### 用例2：长时间断开后的恢复
**描述**：断开2分钟后恢复  
**步骤**：
1. 正常数据流
2. 断开网络2分钟
3. 恢复网络
4. 观察重连过程

**预期**：
- 断开期间持续重试（最多5次）
- 恢复网络后，在下次重试周期连接成功
- 数据流恢复

### 用例3：服务器地址变更
**描述**：服务器IP地址变化  
**步骤**：
1. 连接服务器A
2. 停止服务器A，启动服务器B（不同IP）
3. 更新客户端配置
4. 观察重连行为

**预期**：
- 客户端尝试重连原地址失败
- 配置更新后连接到新地址
- 需要实现动态配置更新支持

### 用例4：并发连接测试
**描述**：多个客户端同时断开/重连  
**步骤**：
1. 启动5个测试客户端
2. 断开网络
3. 恢复网络
4. 观察所有客户端重连

**预期**：
- 所有客户端独立重连
- 服务器能处理并发连接请求
- 无连接冲突或资源竞争

## 问题排查指南

### 问题1：重连失败
**症状**：网络恢复后客户端仍无法连接  
**排查步骤**：
1. 检查服务器是否确实运行
2. 验证URL和端口是否正确
3. 检查防火墙设置
4. 查看客户端和服务器日志

### 问题2：重连循环
**症状**：客户端不断重连但始终失败  
**排查步骤**：
1. 检查网络连通性
2. 验证服务器WebSocket端点
3. 检查身份验证/权限
4. 查看是否有永久性错误

### 问题3：数据丢失
**症状**：重连成功后数据不连续  
**排查步骤**：
1. 检查服务器端数据缓冲
2. 验证客户端数据解析
3. 检查时间戳连续性
4. 考虑添加重连后的数据同步

### 问题4：资源泄漏
**症状**：多次重连后内存持续增长  
**排查步骤**：
1. 检查WebSocket对象是否正确释放
2. 验证事件处理器是否取消注册
3. 检查缓冲区是否正确归还到池
4. 使用内存分析工具定位泄漏

## 优化建议

### 1. 智能重连策略
```csharp
// 根据错误类型调整重试策略
if (ex is WebSocketException wsEx)
{
    if (wsEx.WebSocketErrorCode == WebSocketError.InvalidMessageType)
    {
        // 协议错误，不重试
        return;
    }
    else if (wsEx.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
    {
        // 连接意外关闭，立即重试
        delay = 100;
    }
}
```

### 2. 心跳机制增强
添加双向心跳包，更快检测连接状态：
```csharp
// 定期发送ping
await webSocket.SendAsync(
    new ArraySegment<byte>(PING_FRAME),
    WebSocketMessageType.Binary,
    true,
    cancellationToken);
```

### 3. 连接状态持久化
记录连接历史，用于分析和优化：
```csharp
public class ConnectionMetrics
{
    public DateTime LastConnected { get; set; }
    public DateTime LastDisconnected { get; set; }
    public int TotalReconnections { get; set; }
    public TimeSpan TotalDowntime { get; set; }
}
```

### 4. 用户通知改进
提供更详细的重连状态：
```csharp
public enum ReconnectPhase
{
    Disconnected,
    WaitingBeforeRetry,
    AttemptingConnection,
    Connected
}

public event Action<ReconnectPhase, int, int> ReconnectStatusChanged;
```

## 测试报告模板

### 测试信息
- 测试日期：________
- 测试环境：________
- 测试人员：________
- 软件版本：________

### 测试结果
| 测试场景 | 结果 | 重连时间 | 数据完整性 | 备注 |
|----------|------|----------|------------|------|
| 网络闪断 | □通过 □失败 | ______ ms | □完整 □部分丢失 | |
| 服务器重启 | □通过 □失败 | ______ s | □完整 □部分丢失 | |
| 长时间断开 | □通过 □失败 | ______ s | □完整 □部分丢失 | |
| 最大重试次数 | □通过 □失败 | ______ s | N/A | |
| 资源泄漏 | □通过 □失败 | N/A | N/A | |

### 性能数据
- 平均重连时间：______ ms
- 最大重连时间：______ ms
- 重连成功率：______ %
- 数据丢失率：______ %
- 内存增长（10次重连）：______ MB

### 发现问题
1. ________
2. ________
3. ________

### 改进建议
1. ________
2. ________
3. ________

### 总体评价
□优秀 □良好 □合格 □需要改进

---

**文档版本：** 1.0  
**最后更新：** 2026-04-08  
**维护人员：** Kilo (AI助手)