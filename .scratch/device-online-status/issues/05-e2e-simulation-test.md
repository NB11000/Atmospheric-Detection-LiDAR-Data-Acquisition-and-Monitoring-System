# 05 — 全链路集成测试：崩溃→Will→重启→online

- **Label**: needs-triage
- **Parent**: [IMPLEMENTATION_PLAN.md](../IMPLEMENTATION_PLAN.md)
- **Blocked by**: 01, 02, 03, 04

## Execution Rules

> **此 Issue 执行顺序不可变更，必须遵循 TDD 红绿重构循环：**
>
> **1. RED** — 先写一个测试，确认测试 FAIL。禁止一次写多个测试。
> **2. GREEN** — 写最少代码让当前测试 PASS。禁止预判未来测试。
> **3. REFACTOR** — 消除重复、深化模块。禁止 RED 期间重构。
>
> **硬禁止：**
> - 禁止"先全部实现再补测试"（水平切片反模式）
> - 禁止跳过 RED 直接写 GREEN
> - 测试必须通过公共接口验证行为，不耦合实现细节
> - 每次循环只一个测试 → 一个实现，垂直切片推进

## What to build

在 SimulationRunner 中新增测试场景 JSON，验证 Will + retained 方案端到端正确性。

测试场景（S 阶段，单次启停）：
1. 启动 mock 模式 WebAPI → 启动独立 MQTT 观察客户端订阅 `daq/+/events/will`
2. 断言收到 retained online（`status:"online"`, `eventType:"device_online"`）
3. 杀 WebAPI 进程 → 等待 keepalive 超时（~60s 安全值）
4. 断言收到 Will retained offline（`status:"offline"`, `eventType:"process_crashed"`, `ts:0`）
5. 重启 WebAPI → 断言收到 retained online（`eventType:"device_online"`）

## Acceptance criteria

- [ ] 场景文件 JSON 定义完整，包含 S 阶段的检查点
- [ ] 步骤 2：启动后 10s 内收到 retained online
- [ ] 步骤 4：进程被杀后 90s 内收到 Will retained offline
- [ ] 步骤 5：重启后 10s 内收到 retained online
- [ ] `eventType` 字段在三个阶段分别正确
- [ ] `ts` 字段可验证：主动收/发不为 0，Will 为 0
