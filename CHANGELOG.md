# Changelog

## [0.1.0] - 2026-08-02

Initial architecture and domain layer.

### Added
- Research pass over `Keryx-Labs/keryx-node` and `Keryx-Labs/keryx-miner` source
  (`docs/KERYX_RESEARCH.md`) — CLI flags, model tiers/VRAM table, GPU enumeration behavior, stats
  API, confirmed official win64 binaries exist for both projects.
- Architecture decision (`docs/ARCHITECTURE.md`): NativeWindowsRuntimeBackend as the default
  runtime, WSL2 as optional/secondary, no Docker backend in this pass.
- `KeryxNodeManager.Core`: GPU detection (`NvidiaSmiGpuInfoProvider`, `MockGpuInfoProvider`),
  tier auto/manual assignment (`TierAssigner`, `ModelTierCatalog`), safe CLI argument builders
  (`MinerArgumentBuilder`, `NodeArgumentBuilder`), process supervision with exponential-backoff
  restart policy (`ProcessSupervisor`, `RestartPolicy`), runtime backend abstraction
  (`IKeryxRuntimeBackend`, `NativeWindowsRuntimeBackend`, `WslRuntimeBackend`,
  `MockRuntimeBackend`), atomic config storage with migrations (`ConfigStore`), address/path
  validation, log secret masking.
- `KeryxNodeManager.App`: WPF shell with DI (`Microsoft.Extensions.Hosting`), MVVM
  (`CommunityToolkit.Mvvm`), single-instance guard, tray icon (Dashboard/GPU pages working;
  Models/Node/Miner/Logs/Diagnostics/Settings/About are explicit placeholders), dark theme,
  original app/tray icons (multi-resolution `.ico`).
- 55 xUnit tests covering tier assignment, CLI arg building, address validation, restart policy
  backoff/reset, atomic config save/load/migration, secret masking, path validation, nvidia-smi
  CSV parsing.
- `docs/BUILD.md`, `docs/RELEASE.md`, `docs/SECURITY.md`, `docs/USER_GUIDE_RU.md`,
  `docs/TROUBLESHOOTING_RU.md`, `docs/RECOVERY.md`, `PROJECT_STATUS.md`.
- Inno Setup installer script and PowerShell packaging scripts (untested on real Windows in this
  pass — see `PROJECT_STATUS.md`).

### Verified in this session
- `KeryxNodeManager.Core` builds clean on `net8.0`.
- `KeryxNodeManager.Core.Tests`: 55/55 passing.
- `KeryxNodeManager.App` (WPF, `net8.0-windows`) compiles clean via
  `dotnet build -p:EnableWindowsTargeting=true` (compile-only cross-platform check; not a runtime
  verification).

### Not yet implemented (see PROJECT_STATUS.md for the full list)
First-run wizard steps beyond the shell, Models/Node/Miner/Logs/Diagnostics/Settings/About pages,
real process launch wired to a persisted MiningProfile, autostart (Task Scheduler), safety
monitor (overheat protection), diagnostic ZIP export, self-update flow, profiles UI, localization
beyond Russian strings already used in the built pages, tested installer/portable build on real
Windows hardware.
