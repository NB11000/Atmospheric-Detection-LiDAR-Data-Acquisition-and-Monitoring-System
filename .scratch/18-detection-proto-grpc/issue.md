# Proto 扩展 + 子进程结构化检测发送

- **Category**: enhancement
- **State**: todo
- **Blocked by**: —

## Parent

从 `开发计划/检测告警链路实现计划.md` 拆解，Issue 1/2。

## What to build

ConsoleApp1 子进程的 Detection 线程当前用 `SendErrorMessage` 发送纯文本告警，丢失 Timestamp/CH1/CH2/Time 上下文。本 slice 从 proto 层切到调用方，将检测告警改为结构化 `DetectionAlert` 消息通过 gRPC 上报。

**切过的完整路径**：`Proto → GrpcClient.SendDetectionAlert() → AD_Controlcs.Detection()`

具体改动：
- 3 处 `Protos/Grpc.proto` 新增 `DetectionAlert` message（alarm_type, severity, timestamp, time_ticks, ch1, ch2）
- `ConsoleApp1/Service/GrpcClient.cs` 新增 `SendDetectionAlert()`，用 `Any.Pack()` 打包，`MessageType = "Detection"`
- `ConsoleApp1/Service/AD_Controlcs.cs` 的 `Detection()` 中，将 `SendErrorMessage` 调用替换为 `SendDetectionAlert("SIGNAL_OBSTRUCTION", "warning", ...)`

## Acceptance criteria

- [ ] 3 处 proto 文件包含 `DetectionAlert` 消息，`dotnet build` 自动生成 C# stub
- [ ] `SendDetectionAlert()` 正确打包 `DetectionAlert` 到 `AdResponse.Data`，`MessageType = "Detection"`
- [ ] `Detection()` 调用新方法，不再使用 `SendErrorMessage` 发送遮挡告警
- [ ] 编译零错误（`dotnet build ConsoleApp1`）

## Blocked by

None — 可立即开始。
