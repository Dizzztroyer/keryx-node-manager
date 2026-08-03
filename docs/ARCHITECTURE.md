# Architecture

## Stack decision

**.NET 8 / C# / WPF / MVVM**, self-contained `win-x64` publish, as the task brief requested by
default. No deviation to Electron or another cross-platform toolkit — the brief's reasoning
(native tray, native process control, Task Scheduler integration, no bundled Chromium) holds up
under research: none of it requires anything Keryx-specific that would push toward a different
stack.

One correction to the brief's *runtime* assumption, based on `docs/KERYX_RESEARCH.md` §1: **WSL2
is not the primary runtime.** Both `keryxd` and `keryx-miner` ship official win64 binaries and the
miner's source has first-class `#[cfg(target_os = "windows")]` paths. So:

- `NativeWindowsRuntimeBackend` (runs `keryxd.exe` / `keryx-miner.exe` directly as Windows
  processes via `ProcessStartInfo`, no shell) is the **default and recommended** backend.
- `WslRuntimeBackend` is kept as a **secondary/optional** backend for users who explicitly want to
  run Linux-built binaries under WSL2 (e.g. reusing an existing HiveOS-style Linux setup, or a
  from-source Linux build). It is not part of the first-run wizard's happy path.
- `DockerRuntimeBackend` is **not implemented in this pass.** Nothing in the research indicates
  Keryx needs containerization on Windows, and the brief itself says not to make Docker mandatory
  "just because it's easier for the developer." A stub interface member exists so it can be added
  later without reshaping `IKeryxRuntimeBackend`, but no working implementation ships.

This single change removes an entire tier of setup friction (installing WSL2, Ubuntu, verifying
GPU passthrough into a VM) for the common case, which directly serves the brief's core complaint
that end users shouldn't need PowerShell/WSL/Docker expertise.

## Module map

```
KeryxNodeManager.Core        <- domain logic, no WPF/UI references, unit-testable on any OS
  Models/                    <- GpuDevice, ModelSpec/ModelTier, MiningProfile, AppSettings (POCOs)
  Gpu/                       <- IGpuInfoProvider + NvidiaSmiGpuInfoProvider + MockGpuInfoProvider
  ModelAssignment/           <- ModelTierCatalog (verified VRAM table) + TierAssigner
  Cli/                       <- MinerArgumentBuilder / NodeArgumentBuilder (ArgumentList-safe)
  Process/                   <- ProcessSupervisor, RestartPolicy (exponential backoff, capped)
  Config/                    <- ConfigStore (atomic write+backup), versioned schema + migrations
  Validation/                <- KeryxAddressValidator, PathValidator
  Runtime/                   <- IKeryxRuntimeBackend, NativeWindowsRuntimeBackend,
                                 WslRuntimeBackend, MockRuntimeBackend
  Secrets/                   <- SecretMasker (log/diagnostic redaction)

KeryxNodeManager.App          <- WPF net8.0-windows, MVVM, DI (Microsoft.Extensions.Hosting)
  Views/ + ViewModels/        <- Dashboard, Gpu, Models, Node, Miner, Logs, Settings, About
  Wizard/                     <- first-run wizard window
  Tray/                       <- tray icon + context menu, single-instance guard

KeryxNodeManager.Core.Tests   <- xUnit, targets Core only (no WPF dependency, runs on any OS/CI)
```

`KeryxNodeManager.App` depends on `KeryxNodeManager.Core` and never talks to `nvidia-smi`,
processes, or the filesystem directly — every ViewModel goes through `Core` interfaces, so the
mock backend can drive the entire UI without real hardware (brief §23).

## Runtime backend abstraction

```csharp
public interface IKeryxRuntimeBackend
{
    string Name { get; }
    Task<bool> IsAvailableAsync(CancellationToken ct);
    Task<ManagedProcessHandle> StartNodeAsync(NodeLaunchSpec spec, CancellationToken ct);
    Task<ManagedProcessHandle> StartMinerAsync(MinerLaunchSpec spec, CancellationToken ct);
    Task StopAsync(ManagedProcessHandle handle, TimeSpan gracePeriod, CancellationToken ct);
}
```

`NativeWindowsRuntimeBackend` implements this over `System.Diagnostics.Process` with
`CreateNoWindow = true`, `UseShellExecute = false`, and `ArgumentList` (never a concatenated
command string — see `docs/SECURITY.md`). `WslRuntimeBackend` wraps `wsl.exe -d <distro> --
<argv...>`, still via `ArgumentList`, never string interpolation into a shell. `MockRuntimeBackend`
simulates GPU/node/miner state transitions per brief §23 and is only ever selected via an explicit
`--mock` launch flag or a `Debug` build constant — never a runtime auto-detected fallback in
Release, so it cannot accidentally activate for a real user.

## Models: app-managed downloads, not miner-managed

Per `KERYX_RESEARCH.md` §4/§7, the miner will auto-fetch a missing model over IPFS with **no
progress/pause/resume/checksum UI** of its own. The brief's Models page (§7) requires pause,
resume, checksum verification, and a "not installed until verified" state. Decision: **the app
manages model downloads itself** (direct HTTPS mirrors listed in the miner's own README — HuggingFace/
direct/torrent), writing into the same `<models-dir>/<Model-Name>/model.gguf` layout the miner
expects, and computing/verifying a checksum before marking a model "Installed." This avoids
racing the miner's own first-run download (the app checks "is `model.gguf` present and
checksummed" before ever launching the miner for a tier that needs it) while staying compatible —
if a user lets the miner do its own first-run fetch instead, the app just discovers the resulting
file on next scan.

## GPU identity

GPUs are tracked by **UUID** (from `nvidia-smi --query-gpu=uuid`) as the persistent key in
`MiningProfile`; CUDA driver index (0..N, the same order `--force-model`'s CSV list uses) is
resolved at launch time by re-querying `nvidia-smi --query-gpu=index,uuid` and mapping the
profile's UUIDs to current indices. This survives GPU reordering after a driver update or a
physical slot change — exactly the failure mode the brief calls out in §6.

## Process supervision

`ProcessSupervisor` never launches via `cmd /c` or a shell string. Each managed process
(`keryxd.exe`, `keryx-miner.exe`) gets its own `RestartPolicy` (max attempts, exponential backoff
with a ceiling, reset-after-stable-uptime) and a PID file under
`%LocalAppData%\KeryxNodeManager\runtime\`. On app startup, stale PID files are checked against
the live process table (image name + start time, not PID alone — PIDs are reused) before being
treated as "still running."

## Configuration storage

`%LocalAppData%\KeryxNodeManager\` — `settings.json` (versioned schema + `SchemaVersion` field),
`profiles\*.json`, `state\runtime.json`, `logs\`, `cache\`, `backups\`. Writes go through
`ConfigStore.SaveAtomicAsync`: serialize to a temp file in the same directory, `FileStream.Flush
(true)`, then `File.Replace` (atomic rename on NTFS) with the previous version kept as `*.bak`.
Read path validates `SchemaVersion` and runs registered migrations in order before the app touches
the deserialized object.

## What is NOT implemented in this pass

This is a large brief (30 numbered sections). This session delivers the domain layer
(`KeryxNodeManager.Core`, fully implemented and unit-tested) plus a working WPF shell
(navigation, Dashboard, GPU, first-run wizard skeleton, tray icon, mock backend wired end-to-end)
so the app runs and is inspectable, but not every page/dialog in the 30-section brief is built out
yet. See `PROJECT_STATUS.md` for the exact done/pending split — do not take this document as a
claim that installer, auto-update, or every settings page is finished.
