# AGENTS.md — 大气检测激光雷达数据采集与检测系统 V2.0

## Build & Test

```bash
dotnet build              # 根目录下构建整个解决方案
dotnet test               # 运行所有 xUnit 测试（Test/WebAPI.Tests/）
dotnet test --filter "FullyQualifiedName~Integration"
cd WebAPI && dotnet run   # 启动主控进程
```

## Two-Process Architecture

| Process | Project | Runtime | Entry |
|---------|---------|---------|-------|
| Master (主控) | `WebAPI/` | ASP.NET Core 8.0 | `Program.cs` — DI + spawn subprocess |
| Subprocess (采集子进程) | `ConsoleApp1/` | .NET 8 Native AOT | `Program.cs` — 5 threads |

WebAPI auto-spawns ConsoleApp1. Subprocess runs 5 threads: ADWork → ADDraw → Analysis / UI / Detection.

## IPC & Data Flow

- **CoreDataBus** (MMF, ~96MB): Analysis thread → per-sample streaming write → master reads for persistence/low-freq publish
- **UISharedBuffer** (MMF, ~480KB): UI thread downsamples 1000:1 → master reads for waveform publish (100ms)
- **gRPC bidirectional stream**: commands down, status + detection alerts up
- **DetectionChannel** (in-process `Channel<DetectionBatch>`): Analysis → Detection thread, isolated from CoreDataBus

## Mock Mode (no hardware)

```bash
cd WebAPI && dotnet run -- --mock   # master in mock mode (spawns subprocess with --mock)
cd MMFWriter && dotnet run CoreDataBus 100000  # inject fake data into CoreDataBus
```

Subprocess `--mock` enables: fake data generator, HTTP health endpoint on port 19999.

## Critical Conventions

- **CoreDataBus.WriteIndex**: `long` monotonic, never wraps. Read with `Volatile.Read`.
- **Persistence**: periodic snapshot (1s/5s/30s/1min/5min), **not** full archive
- **Cn²**: first 99 frames = `-1.0` sentinel. Consumer must `if (sample.Cn2 < 0) skip`.
- **Acquisition-bound services**: pure Singletons (not `BackgroundService`), started/stopped by `AcquisitionLifecycleCoordinator`
- **Waveform MQTT payload**: binary `double[]`, **NOT JSON**
- **ClientID = MachineId** (`appsettings.json → Mqtt.MachineId`). Must be unique per device.

## Configuration

| File | Purpose |
|------|---------|
| `WebAPI/appsettings.json` | MQTT broker connection |
| `WebAPI/Config/DeviceConfig.json` | Capture card, LiDAR algorithm, laser serial, persistence |

## Key Files

- `CONTEXT.md` — domain terminology glossary (read this first for any unfamiliar term)
- `MQTT主题文档.md` — full MQTT topic reference
- `docs/adr/` — architecture decision records

## Known Issue

`ConsoleApp1.csproj` has a broken `BaseOutputPath` pointing to a non-existent `E:\` path. Build may fail with MSB3026/MSB3027 copy errors. Workaround: remove or fix the `BaseOutputPath` element in that file.
