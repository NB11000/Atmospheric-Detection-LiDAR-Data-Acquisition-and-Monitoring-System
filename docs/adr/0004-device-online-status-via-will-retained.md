# 通过 Will + Retained 消息实现设备在线状态监控，取代 $SYS 主题

`$SYS` 主题仅在设备连接/断开的瞬间发布一次，无 retained 标志，前端晚于设备上线时错过所有已发布事件，已在线设备永远显示为离线/未知。且 `$SYS` 格式因 broker 而异（EMQX、Mosquitto、HiveMQ 各不同）。

决定将设备在线状态统一收敛到 `daq/{MachineId}/events/will` 单一 topic，利用 MQTT Will Message + Retained 消息实现时序无关的在线状态监控。设备 CONNACK 成功后主动 publish retained online，正常 DISCONNECT 前主动 publish retained offline，异常崩溃由 Will 代为发布 retained offline。前端任何时候通配符订阅即可拿到每台设备的最新状态。

**Considered Options:**

1. **继续使用 `$SYS` 主题** — 拒绝。时机依赖缺陷无解（`$SYS` 不受应用层控制），且换 broker 必改代码。
2. **仅用 HTTP polling** — 拒绝。增加轮询开销 + 无法检测崩溃瞬间。
3. **Will + Retained 方案** — 采纳。利用 MQTT 协议原生能力，无额外依赖，Retained 天然消除时序问题。

**Consequences:**

- `events/will` topic 承载三种来源（主动 online / 主动 offline / Will 崩溃 offline），payload 格式统一为 6 字段 JSON
- 正常 offline publish 失败时跳过 DisconnectAsync，由 Will 兜底（keepalive 窗口最多 ~45s）
- `events/state_changed` topic 中 `mqtt_connected`/`mqtt_disconnected` 的 MQTT 广播停发（MQTT 通道断了发不出去，恢复了由 `events/will` retained 覆盖）
- 前端需从 topic 路径 `daq/{MachineId}/events/will` 提取 MachineId，payload 不冗余携带
