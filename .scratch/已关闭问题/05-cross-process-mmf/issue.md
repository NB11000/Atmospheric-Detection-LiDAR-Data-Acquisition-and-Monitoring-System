# 跨进程 MMF 端到端集成测试

- **Category**: enhancement
- **State**: done
- **Blocked by**: #2

## What to build

验证跨进程 CoreDataBus 映射正确性：WebAPI `Create()` 创建共享内存 → 启动 ConsoleApp1 子进程 `Open()` → 子进程 `Write()` → WebAPI `TryReadLatestSingle()` 读到正确数据。

需要真实的双进程启动（`Process.Start`），验证：
- 指针映射正确（子进程能看到相同数据）
- `CoreBusHeader` 跨进程布局一致（`LayoutKind.Sequential`）
- 数据区首地址偏移一致

## Acceptance criteria

- [x] WebAPI Create → 子进程 Open 不崩溃
- [x] 子进程 Write 一条 → WebAPI TryRead 读到相同数据
- [x] 子进程写入 N 条 → WebAPI TryRead 读到最新一条
- [x] WriteIndex 在跨进程间一致可见
