# Issue 01: 项目骨架 + ConfigManager

- **Label**: needs-triage
- **Parent**: [implementation-plan.md](../implementation-plan.md)
- **Blocked by**: None — can start immediately

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

创建 `SharedModels` 类库 + `AvaloniaApplication_ConfigLauncher` 项目骨架，实现 `ConfigManager` 纯逻辑类。

### SharedModels 类库

- 创建 `SharedModels` 项目（.NET 8.0 类库）
- MqttSettings（10 字段，默认值与 WebAPI 一致）
- CommandResult（Success / Code / Message / State / Timestamp）
- SystemStateDto（Server / Collector / Laser / UiHints / Timestamp）

### ConfigManager 完整公共接口

```
class ConfigManager(baseDirectory: string):
    HasExistingConfig() → bool
    LoadConfig() → MqttSettings
    SaveConfig(MqttSettings) → void
    MarkConfigured() → void
    LoadBaseUrl() → string
    SaveBaseUrl(string) → void
```

关键行为：
- `HasExistingConfig()` 检查 `baseDirectory/.mqtt_configured` 文件是否存在
- `LoadConfig()` 读取 `baseDirectory/appsettings.json`，只反序列化 `Mqtt` 节点为 `MqttSettings` 对象；文件不存在则返回默认 MqttSettings
- `SaveConfig(settings)` 读取现有 JSON 全文，替换 `.Mqtt` 节点，写回文件（保留 Logging / AllowedHosts / Launcher 等节点）；文件不存在则创建含 Logging + AllowedHosts 默认值 + Mqtt 节点的完整 JSON
- `MarkConfigured()` 创建空 `.mqtt_configured` 文件
- `LoadBaseUrl()` 读 `Launcher.BaseUrl` 节点，不存在返回 `"http://localhost:5135"`
- `SaveBaseUrl(url)` 写 `Launcher.BaseUrl` 节点（不触碰 Mqtt 等其他节点）

## Acceptance criteria

- [ ] SharedModels 类库创建成功，MqttSettings / CommandResult / SystemStateDto 定义完整
- [ ] AvaloniaApplication_ConfigLauncher 项目创建成功，引用 SharedModels，与 WebAPI 在同一解决方案下
- [ ] ConfigManager 单元测试：SaveConfig 后 LoadConfig 回读字段一致
- [ ] ConfigManager 单元测试：MarkConfigured 后 HasExistingConfig 返回 true
- [ ] ConfigManager 单元测试：文件不存在时 HasExistingConfig 返回 false
- [ ] ConfigManager 单元测试：SaveConfig 覆盖 BrokerHost 后，JSON 文件中 Logging 节点未被破坏
- [ ] ConfigManager 单元测试：appsettings.json 不存在时 SaveConfig 创建完整文件（含 Logging + AllowedHosts + Mqtt）
- [ ] ConfigManager 单元测试：LoadBaseUrl 首次不存在返回 `http://localhost:5135`；SaveBaseUrl 后 LoadBaseUrl 回读一致
- [ ] ConfigManager 单元测试：SaveConfig 不破坏 Launcher 节点

## Comments
