# Project Status

_Last updated: 2026-08-03. Most recent work: ran the full release pipeline for real for the first
time - `dotnet publish` (self-contained win-x64) → portable ZIP → Inno Setup installer →
checksums.txt - closing the long-standing "packaging never run end-to-end" Known Issue. Caught and
fixed a real bug along the way (a BOM-less `.ps1` file plus `Compress-Archive`'s missing UTF-8
entry-name flag corrupted a Cyrillic filename inside the portable ZIP) and live-verified both
deliverables actually install/launch/uninstall (installer) and extract/launch (portable) on the
real machine - see the thirty-eighth increment below. Before that: adding two new user-requested
features: binary auto-update for
keryxd.exe/keryx-miner.exe (polls the real Keryx-Labs GitHub repos' releases, downloads/extracts/
replaces with a backup of the old binary, never applied without an explicit user click) and a
public/community node directory (bundled-empty by design, optional remote JSON URL, real TCP
health-check per node, honestly labeled "reachable + latency" rather than a fabricated "uptime").
Both live-verified against the real GitHub API and a real loopback socket - see the thirty-seventh
increment below. This followed the user's decision on Core-layer localization ("give Core its
own resource abstraction") was implemented end-to-end: a new `CoreStrings` static lookup class
(ru/en/es/it/fr/uk, no WPF/.resx dependency so Core stays cross-platform-buildable) now backs every
exception/status message in `SystemChecker`, `ProcessSupervisor`, `TierAssigner`,
`TaskSchedulerAutostart`, `PathValidator`, `SafetyMonitor`, `ProfileStore`,
`ModelDownloadException`, `NvidiaSmiGpuInfoProvider`, and `NativeWindowsRuntimeBackend`, wired to
the App's existing `LocalizationManager.Apply` so both layers switch language together. Caught and
fixed two real bugs along the way (an untranslated Ukrainian string that was verbatim Russian, and
a test-parallelism race from the new tests mutating global static state) before live-verifying the
whole chain on the real Windows machine (a Core-layer exception message actually rendered in
English after switching the language) - see the thirty-sixth increment below. This followed a
nine-item backlog pass plus one further continuation item under "продолжай" - every remaining "In
progress" item from that backlog is now closed, plus the Finish-step address highlight: adding a
Models-page delete confirmation dialog, Ukrainian (uk)
localization (sixth language, full static-label coverage), wiring CUDA_VISIBLE_DEVICES into the
miner launch as defense-in-depth alongside --force-model, fixing the long-standing
missing-ScrollViewer bug on Settings/Profiles/About, formally scoping (not yet implementing) GPU
fan-speed/power-limit control, closing out the Models page with a distinct "Продолжить" resume
label plus an aggregate disk-usage summary, adding per-profile quick-glance info (masked address +
GPU count) to the Profiles page, letting the first-run wizard create+switch to a new profile from
its own welcome step, building a real themed ComboBox control template (which surfaced and
fixed a genuine DisplayMemberPath-projection bug on the GPU page along the way), highlighting
an invalid mining address in amber on the wizard's Finish step (live-verified with a real
invalid-address pass through the wizard, including catching and restoring a test address that
Skip correctly persisted into the real profile), giving the tray icon a real per-state colored
badge (grey/yellow/green wired to actual Dashboard state, live-verified with a real grey→green
transition; red/blue icons exist but aren't driven by anything yet since no error/inference signal
exists in Core to drive them honestly), and adding a "Перейти к настройкам" nudge button on the
Dashboard that jumps straight to the Node or Miner page when Start All fails for missing config
(live-verified end-to-end, including the actual page switch), wiring the tray menu's
Start/Stop All (previously silent no-ops with a stale "not wired yet" comment) plus a real
stop-before-exit on "Выйти → Да" (previously a broken promise - the dialog said it would stop
first but never did), verifying WslRuntimeBackend.StopAsync's teardown claim against
real WSL on this machine (confirmed correct with an actual sleep-process kill test, catching and
ruling out an unrelated automation-host console quirk along the way), and root-causing the
long-guessed Task Scheduler access-denied issue for real (it's UAC token filtering on this
machine, not antivirus - confirmed with a controlled elevated-vs-non-elevated A/B test, and the
old antivirus guess was retracted from both the code comments and the user-facing error hint) -
on top of localizing the first-run
wizard's static strings (closing the full static-string-extraction pass across every page), About's,
Profiles', Logs', Miner's, Node's, Models', GPU's, and Dashboard's static strings, the Language
ComboBox contrast fix, Spanish/Italian/French localization, real single-instance IPC, the first
localization increment, Profiles + About pages, Safety monitor, Settings page + autostart, Logs
page, first-run wizard build, Models page, GPU→launch wiring, MiningProfile wiring, and
icon-replacement sessions earlier the same week._

## Current goal

Ship a working Windows desktop app that manages a Keryx node and GPU miner from one window,
with no PowerShell/WSL/Docker expertise required from the end user (per the original 30-section
brief). This is a large brief; this document tracks real progress against it honestly — nothing
below is claimed "done" unless it actually compiled/ran and, where applicable, passed tests.

## Completed

- Research pass over real `keryx-node`/`keryx-miner` source (cloned, not guessed) →
  `docs/KERYX_RESEARCH.md`.
- Architecture decision recorded → `docs/ARCHITECTURE.md`. Key deviation from the original brief:
  NativeWindowsRuntimeBackend (official win64 binaries) is the default, not WSL2.
- `KeryxNodeManager.Core` (full domain layer): GPU detection incl. nvidia-smi CSV parsing, tier
  auto/manual assignment against the verified VRAM table, ArgumentList-safe CLI builders for
  both `keryxd`/`keryx-miner`, process supervisor with exponential-backoff restart policy,
  three runtime backends (native/WSL/mock), atomic versioned config storage with a migration
  pipeline, **ProfileStore** (new: shares one `MiningProfile` across Node/Miner/Dashboard
  ViewModels, persisted via ConfigStore), address/path validation, log secret masking.
- 55 xUnit tests across 8 test classes — **passing on real Windows** (see "Test status").
- `KeryxNodeManager.App`: WPF shell with working **Dashboard, GPU, Node, Miner, Models, Logs, and
  Settings** pages (Diagnostics/About are still explicit placeholders). Node page collects
  `keryxd.exe` path/endpoint/testnet and has a **real TCP connectivity check**. Miner page collects
  mining address (validated), `keryx-miner.exe` path, models directory (via .NET 8's native
  `OpenFolderDialog`), and an advanced-mode command-line preview built from the *exact* same
  `MinerArgumentBuilder.Build()` the real launch path uses. Dashboard's Start All/Stop All now
  **actually launch and stop processes** through `ProcessSupervisor` + the DI'd runtime backend —
  this was the single highest-priority open item last session and is now done and
  live-verified (see below).
- Docs: `KERYX_RESEARCH.md`, `ARCHITECTURE.md`, `BUILD.md`, `RELEASE.md`, `SECURITY.md`,
  `USER_GUIDE_RU.md`, `TROUBLESHOOTING_RU.md`, `RECOVERY.md`, `CHANGELOG.md`, `README.md`, this
  file.
- Inno Setup installer script + PowerShell packaging scripts written — still not run end-to-end
  (need a full publish + Inno Setup install to verify; see "Known issues").
- **GPU page mode selection is now real, not decorative.** Each GPU card has a "Режим" ComboBox
  (Авто / Отключено / one of the 5 tiers by name), persisted per-GPU-UUID to
  `MiningProfile.GpuAssignments` as soon as it's changed (no separate Save step needed - a
  dropdown pick doesn't need one). A new `GpuAssignmentResolver` (Core/ModelAssignment) is the
  single place that turns those saved choices into the CUDA-ordered `ModelTier?` list +
  `anyManualOverride` flag `MinerArgumentBuilder.Build` needs - both `DashboardViewModel`'s real
  launch and `MinerViewModel`'s advanced-mode command preview now call through this one resolver,
  so what the user sees in the Miner page's preview is guaranteed to match what Dashboard's
  "Запустить всё" would actually run. Previously both of those independently hardcoded
  "all-Auto, no override," so a GPU page choice was silently never applied - that's fixed now.

## This session's real-Windows verification (new)

Previous sessions only verified compilation from a Linux sandbox. This session got direct access
to the user's actual Windows 11 machine and did a full live check:

- Installed .NET 8 SDK for real (`winget install Microsoft.DotNet.SDK.8`), confirmed
  `Microsoft.WindowsDesktop.App` (WPF runtime) is present.
- `dotnet build -c Release` on the **real solution** (all 3 projects, actual WPF markup compiler,
  not the Linux compile-check): **0 warnings, 0 errors.**
- `dotnet test`: **55/55 passing**, on Windows, twice (before and after the wiring changes below).
- Launched the actual `.exe` in `--mock` mode. **Found and fixed a real runtime crash**: `App.xaml`
  crashed on startup with `"#FF3A4050" is not a valid value for property "BorderBrush"` — the
  `CardStyle` in `DarkTheme.xaml` set `BorderBrush` to a `Color` resource (`BorderColor`) instead
  of the `SolidColorBrush` (`BorderBrush2`). This is exactly the kind of bug that can only be
  caught by actually running the app, not by reading the XAML — fixed and re-verified.
- After the fix: launched the app, clicked through Dashboard → GPU → a placeholder page → Node →
  Miner, confirming no other page crashes. GPU page correctly rendered 3 mock GPUs with live
  TierAssigner explanations.
- Added Node/Miner pages, wired `DashboardViewModel` to `ProcessSupervisor`, rebuilt (caught and
  fixed two more real compiler errors along the way: `UseWindowsForms` caused a `CS0104` ambiguity
  between `System.Windows.Controls.UserControl`/`Application` and their WinForms namesakes across
  *every* file in the project — switched to .NET 8's native `Microsoft.Win32.OpenFolderDialog`
  instead of `FolderBrowserDialog` to avoid it entirely; a missing `using System.IO;` in two new
  files).
- Live-tested the wired-up flow end to end:
  - Node page's "Проверить endpoint" button did a **real TCP connect** to `127.0.0.1:22110` and
    correctly reported that a listener is already there — it detected the user's own pre-existing
    `keryxd.exe`/`keryx-miner.exe` processes already running on that machine (unrelated to this
    app), proving the check is a genuine network probe, not a stub.
  - Miner page: typed a real-shaped Keryx address, watched the validation warning disappear,
    clicked "Расширенный режим" and confirmed the command preview was byte-for-byte what
    `MinerArgumentBuilder` produces.
  - Dashboard: clicked "Запустить всё" with the mock backend selected — status flipped to
    "Работает"/"Работает" for both Node and Miner, driven by real `ProcessSupervisor` events (not
    a hardcoded string). Clicked "Остановить всё" — both cleanly returned to
    "Остановлена"/"Остановлен".

This is now a materially more trustworthy checkpoint than "it compiles" — it's "it runs, and the
one increment built this session does what it claims."

## This session's second increment: GPU → launch wiring (new)

Same day, immediately following the above. Added `GpuAssignmentResolver` in Core (with 6 new unit
tests: all-auto/no-override, CUDA-order-not-input-order, manual tier override sets
`anyManualOverride`, disabled GPU yields a null tier and also counts as override, an
auto-assignment that disables a card does NOT count as override, and an unknown persisted mode
string fails safe to Auto). Wired it into `GpuViewModel` (mode ComboBox + per-UUID persistence via
`ProfileStore`), `DashboardViewModel.StartAllAsync` (real launch), and `MinerViewModel`'s preview.

Verified on the real Windows machine, in `--mock` mode, with the 61/61 test suite passing both in
the Linux sandbox (cross-compile check via `EnableWindowsTargeting=true` for the WPF project) and
for real (`dotnet build`/`dotnet test` on Windows, 0 warnings/0 errors, 61/61 passing):
- Opened the GPU page, confirmed all 3 mock GPUs render a "Режим" dropdown with the expected 7
  options (Авто, Отключено, and one entry per tier with its real model name, e.g. "Вручную:
  GLM-4-9B-0414").
- Set the RTX 3060 card (CUDA index 0) to "Отключено".
- Switched to the Miner page, enabled Advanced Mode, and confirmed the live command preview
  changed from the previous session's plain command to:
  `keryx-miner.exe --mining-address keryx:... --keryxd-address 127.0.0.1 --ipfs-url
  http://127.0.0.1:5001 --force-model default,light` — i.e. `--force-model` now appears (because a
  manual override exists), lists exactly 2 tokens for 3 GPUs (the disabled RTX 3060 is correctly
  excluded, not represented as a bogus third token), and the two Auto GPUs show the *actual* tiers
  `TierAssigner.AssignAuto` picked for their VRAM (RTX 3090 → `default`, RTX 5070 → `light`) rather
  than a placeholder. This is exactly the CLI the Dashboard's "Запустить всё" would build, since
  both paths call the same `GpuAssignmentResolver.Resolve(...)`.
- Reset the card back to Auto and killed the test app instance afterward, leaving the machine's
  actual pre-existing `keryxd.exe`/`keryx-miner.exe` (unrelated real processes, PIDs 38980/18776)
  untouched throughout.

This closed that day's first "next highest-value increment." Immediately after, this session built
the Models page (see below), closing the second.

## This session's third increment: real Models page (new)

Brief §7 asked for app-managed model downloads with progress/pause/resume/checksum UI. Built:

- `Core/ModelsManagement/ModelDownloader.cs` - resumable HTTP download via Range headers to a
  `.part` file, optional SHA-256 verification (deletes the `.part` file and throws
  `ModelChecksumMismatchException` on mismatch rather than leaving a corrupt/mislabeled file
  behind), atomic `File.Move` to the final path only once the transfer (and checksum, if given)
  fully succeeds. "Pause" and "Resume" are not separate code paths - cancelling the
  `CancellationToken` simply stops mid-stream and leaves the `.part` file in place; the next call
  resumes from it automatically via `Range`. "Cancel" (as opposed to pause) is the caller
  additionally deleting the `.part` file via `ModelDownloader.DeletePartial`.
- `Core/ModelsManagement/ModelFileLocator.cs` - computes the exact
  `<models-dir>/<Model-Name>/model.gguf` path the miner itself expects (docs/KERYX_RESEARCH.md
  §3), and checks real install/partial-download state from the filesystem, never from memory.
- **Deliberately no hardcoded download URL or checksum for any tier.** docs/KERYX_RESEARCH.md §7
  notes the miner's own README lists HuggingFace/direct/torrent mirrors for manual installs, but
  this research pass did not capture the exact links or published hashes - shipping a guessed URL
  would fail the "never claim something works when it's a stub" standard this project holds
  itself to. The Models page instead lets the user paste a URL (and optionally a SHA-256) per
  tier, persisted to a new `MiningProfile.ModelSources` dictionary. The miner's own IPFS
  auto-download on first run needs no URL at all and still works independently - this page is a
  convenience for pre-staging models, not the only way to get them.
- 6 new unit tests (`ModelDownloaderTests.cs`) against an in-process fake `HttpMessageHandler` +
  a custom throttled-read `Stream` (needed because `HttpContent`'s default
  `ReadAsStreamAsync(ct)` buffers the whole response via `SerializeToStreamAsync` before
  returning, which would defeat a cancellation-mid-transfer test - overriding
  `CreateContentReadStreamAsync` instead was required to actually exercise that path). Covers:
  fresh download, correct checksum, wrong checksum (cleans up, no corrupt file left), HTTP
  Range-based resume, a server that ignores Range and returns the whole file (must restart from
  zero, not corrupt-append), and cancellation mid-transfer (partial file kept, destination file
  never created).
- `ModelsViewModel`/`ModelsView.xaml` - one card per tier with install status, URL/checksum
  fields, and Download/Pause/Cancel/Delete/Open-folder buttons, wired into DI and
  `MainWindow.ShowPage`.

**Verified live on the real Windows machine** (not just unit tests): set `ModelsDirectory` to a
throwaway test folder, entered `https://www.google.com/favicon.ico` as the VeryLight tier's URL
(a real small public HTTPS file, used purely to exercise the download path - not a real model),
clicked Скачать, and confirmed via PowerShell that `_test_models\Qwen3-8B-abliterated\model.gguf`
was created at exactly the path `ModelFileLocator` computes, with the UI flipping to "Установлена"
and a full progress bar. Clicked Удалить and confirmed the file was actually removed. Cleaned up
afterward: deleted the test folder and reset `MinerExecutablePath`/`ModelsDirectory`/
`ModelSources` in `settings.json` back to empty so the machine is left in a clean state (an
earlier misclick this session had accidentally typed the test path into `MinerExecutablePath`
instead of `ModelsDirectory` - caught by checking `settings.json` directly, not assumed from the
UI, and corrected before re-testing).

61 → 67 Core tests; all passing on real Windows (`dotnet build`/`dotnet test`, 0 warnings/0
errors).

## This session's fourth increment: first-run wizard (new)

Brief §4 asked for a first-run wizard covering system checks, directory setup, mining address
entry, GPU/tier assignment, autostart, and a finish/save step. Built:

- `Core/Diagnostics/SystemChecker.cs` - real (not simulated) checks: Windows version
  (`Environment.OSVersion`), NVIDIA GPU presence via the same `IGpuInfoProvider` the rest of the
  app already uses (so the wizard can never disagree with what the GPU page sees), WSL presence
  (`wsl.exe --status` with a 3s timeout so a missing/hung binary can't stall the wizard), and
  Docker presence (a PATH scan for `docker.exe`, no process spawn needed). WSL/Docker are marked
  `Required: false` and never block progress - `docs/ARCHITECTURE.md` makes
  `NativeWindowsRuntimeBackend` the default, so a machine with neither installed is a fully
  supported configuration, not a warning-worthy one.
- `WizardViewModel`/`WizardWindow` (App/ViewModels, App/Views) - 7 linear steps (Welcome → System
  checks → Directories → Mining address → GPU/tier preview → Autostart → Finish), all reading from
  and writing directly into the same `ProfileStore.ActiveProfile`/`ProfileStore.Settings` instances
  the Node/Miner/GPU pages already bind to - there is no separate, throwaway wizard-only copy of
  the configuration. System checks re-run automatically on entering step 1; the GPU/tier preview
  (reusing `TierAssigner.AssignAuto` - the same call `GpuViewModel` makes) re-runs automatically on
  entering step 4, both against live state rather than a cached snapshot from wizard-open time.
- New `AppSettings.FirstRunCompleted` bool (default `false`) gates whether `App.xaml.cs` shows the
  wizard (as a modal `ShowDialog()`) before `MainWindow` on startup. Both "Завершить" (after
  reviewing all 7 steps) and "Пропустить" (jump out early) call the same `FinishAsync()` which
  persists whatever has been entered so far via `ProfileStore.SaveAsync()` and sets
  `FirstRunCompleted = true` - neither path silently discards partial input, and the wizard is
  deliberately shown in `--mock` runs too (not special-cased) so its own manual-verification
  coverage isn't skipped during quick dev iteration.
- **Found and fixed a real bug during manual verification, not just written and assumed correct:**
  the Models-directory TextBox was bound `TwoWay` directly to `Profile.ModelsDirectory` - since
  `MiningProfile` is a plain data class with no `INotifyPropertyChanged`, typing into the field
  updated the model but never told the "Далее" button's `CanExecute` to re-run, so the button
  stayed permanently disabled after typing (only the folder-picker dialog path used to work, since
  that one called `NotifyCanExecuteChanged()` explicitly). Fixed by adding
  `NodeExecutablePathInput`/`MinerExecutablePathInput`/`ModelsDirectoryInput` observable mirror
  properties that write through to `Profile` and explicitly requery `NextCommand` on every
  keystroke. Caught by literally typing into the field on the real machine and watching the button
  fail to appear in the Windows-MCP interactive-elements snapshot - would not have been caught by
  a compile check or a superficial screenshot glance.
- 5 new unit tests (`SystemCheckerTests.cs`): `CheckWindowsVersion`/`CheckDocker` never throw and
  are correctly marked non-required; `CheckNvidiaAsync` against a fake `IGpuInfoProvider` covers
  devices-found, empty-list, and `GpuQueryException` cases. `CheckWslAsync` (a real process spawn)
  is deliberately not unit-tested - its actual presence/absence is host-dependent, so it's verified
  live on the real Windows machine instead (see below), matching the same reasoning already applied
  to `NvidiaSmiGpuInfoProvider`'s CSV-parsing-vs-process-spawn test split.

**Verified live on the real Windows machine** (not just unit tests): cleared `settings.json`,
launched with `--mock`, and walked every one of the 7 steps via Windows-MCP. Step 1's checks came
back real and correct (Windows 10.0.26200.0 supported; the 3 mock GPUs listed by name; WSL detected
available; Docker found in PATH). Step 2 correctly disabled "Далее" with an empty Models directory
(confirmed absent from the interactive-elements list, not just visually greyed) and correctly
re-enabled it live after typing a path (after the CanExecute-requery fix above). Step 4's GPU
preview showed the real `TierAssigner` output for all 3 mock GPUs (RTX 3060/5070 → Mistral-7B-v0.3
"Light"; RTX 3090 → GLM-4-9B-0414 "Default"), matching what the GPU page itself would show for the
same hardware. Step 6 correctly showed "Завершить" in place of "Далее". Clicking it persisted
`FirstRunCompleted: true` and `ModelsDirectory` to `settings.json` (checked directly via
PowerShell, not assumed from the UI) and the app proceeded straight to `MainWindow`. Relaunching
afterward correctly skipped the wizard entirely and went straight to the Dashboard, confirming the
gate. (One relaunch hit the pre-existing "zombie low-memory process, no window" flake documented
in an earlier session's memory notes - killing that PID and relaunching once more produced a normal
~138 MB working set with the window visible immediately, confirming it's the known launch flake,
not a wizard regression.)

72 total Core tests; all passing on real Windows (`dotnet build`/`dotnet test`, 0 warnings/0
errors).

## This session's fifth increment: Logs page + diagnostic ZIP export (new)

Brief §12 asked for a Logs page showing node/miner output plus a one-click diagnostic export.
Built:

- **Found and fixed a real, previously-undiscovered runtime bug while researching this feature,
  not while testing it.** Both `NativeWindowsRuntimeBackend` and `WslRuntimeBackend` already set
  `RedirectStandardOutput`/`RedirectStandardError = true`, but neither ever called
  `BeginOutputReadLine()`/`BeginErrorReadLine()` — meaning the redirected OS pipes were never
  drained. A sufficiently long-running or verbose `keryxd.exe`/`keryx-miner.exe` process would
  eventually fill the pipe buffer and block on its own `stdout`/`stderr` writes, hanging the
  child process. This had been latent since the very first process-launch code was written and
  had nothing to do with the Logs page directly — it surfaced only because giving `LaunchSpecs`
  an `OnOutputLine` callback for the Logs page meant actually looking at how those streams were
  wired. Fixed by always draining via `OutputDataReceived`/`ErrorDataReceived` and always calling
  `BeginOutputReadLine`/`BeginErrorReadLine`, regardless of whether a forwarding callback is
  supplied.
- `Core/Logging/LogSink.cs` — the single choke point all captured output flows through: masks
  each line via the existing `SecretMasker`, buffers the last 2000 lines per `ManagedProcessKind`
  in memory for the UI, and durably writes to day-named, size-rotated (numeric-suffix rolling),
  retention-pruned (`AppSettings.LogRetentionDays`, checked against real `LastWriteTimeUtc`, not a
  cached timestamp) files under `%LOCALAPPDATA%\KeryxNodeManager\Logs`.
- `Core/Logging/DiagnosticsExporter.cs` — bundles the log files, a JSON-round-trip-cloned and
  redacted settings snapshot (mining address masked, environment variables and model source URLs
  stripped — never mutates the live in-memory `AppSettings`), and basic system info into a ZIP via
  `System.IO.Compression.ZipFile`.
- `LogsViewModel`/`LogsView.xaml` — two live-scrolling panels (Node/Miner), wired to
  `LogSink.LineAppended` via the WPF dispatcher (the event fires from a ThreadPool thread inside
  `OutputDataReceived`, so every `ObservableCollection` mutation has to be marshaled back to the UI
  thread or WPF throws). "Экспорт диагностики..." opens a native Save dialog and calls
  `DiagnosticsExporter.Export`; "Открыть папку логов" opens the logs directory in Explorer;
  "Очистить экран" clears only the displayed lines, never the on-disk files.
- `DashboardViewModel.StartAllAsync` now wires each launch's `OnOutputLine` to
  `LogSink.Append(...)`, so the Logs page can never show anything other than exactly what the real
  process wrote.
- 9 new unit tests (`LogSinkTests.cs`, `DiagnosticsExporterTests.cs`): masking, per-kind buffer
  isolation, file rolling (tiny `maxBytesPerFile` to avoid needing megabytes of text), prune-by-
  real-timestamp, the `LineAppended` event firing, redaction correctness, non-mutation of the
  original settings object, ZIP bundling correctness, and ZIP overwrite behavior.

**Verified live on the real Windows machine** (not just unit tests): started the mock Node from
Dashboard, navigated to the Logs page, and confirmed real-time lines streamed into the UI (e.g.
`[20:17:39] OUT Peer connected: 12 active`, `[20:17:35] OUT GHOSTDAG: new blue block accepted`,
rotating every 4 seconds). Cross-checked via PowerShell (`Get-Content` against
`keryxd-2026-08-02.log`) that the exact same lines were durably persisted with correct UTC
timestamps — the UI and the on-disk file were never allowed to disagree. Clicked "Экспорт
диагностики...", saved a real ZIP, and verified its contents directly (not just that the dialog
closed): `logs/keryxd-2026-08-02.log` present, `settings-redacted.json` present with
`MiningAddress` correctly empty/masked (no address was set in this test profile) and
`EnvironmentVariables`/`ModelSources` stripped, `system-info.txt` present with real OS/.NET
version/export timestamp. Deleted the test ZIP afterward. Clicked "Очистить экран" and confirmed
via PowerShell that the on-disk log file kept growing after the display cleared (5218 bytes,
`LastWriteTime` still advancing) — proving the clear-screen action is display-only, never
data-loss. Clicked "Остановить всё" to leave the app in a clean, stopped state at the end of the
session.

One button did **not** verify cleanly in this environment: "Открыть папку логов" opened Explorer
but hit a "Расположение недоступно" (location unavailable) dialog. Investigated rather than
shrugged off: `fsutil reparsepoint query` on `%LOCALAPPDATA%\KeryxNodeManager` reports it is *not*
a real NTFS reparse point, yet `Get-Item` resolves its `Target` to a path under
`AppData\Local\Packages\Claude_...\LocalCache\...` — i.e. some folder-virtualization layer
specific to how this automated session's processes are launched is redirecting `%LOCALAPPDATA%`
for this environment only, and Explorer (a separate, unvirtualized system process) can't follow
it. `Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })` in
`LogsViewModel.OpenLogsFolder` is standard, correct .NET — this reads as an artifact of the
automation sandbox, not a defect in the app, but is flagged here rather than quietly assumed fine,
since it was never actually confirmed working end to end. Worth a quick re-check next time this
page is touched on a normal (non-automated) desktop session.

81 total Core tests; all passing on real Windows (`dotnet build`/`dotnet test`, 0 warnings/0
errors).

## This session's sixth increment: real Settings page + Task Scheduler autostart (new)

User request: "автостарт должен быть выбираться в настройках" (autostart should be a Settings-page
toggle, not the wizard-only flags). Brief §11 asked for real Task Scheduler registration, distinct
from `MiningProfile.AutoStartNode`/`AutoStartMiner` (which only govern whether the *already-running*
app also launches keryxd/keryx-miner - `AppSettings.StartWithWindows` existed as a dead field since
an earlier session but nothing implemented it). Built:

- `Core/Autostart/TaskSchedulerAutostart.cs` - registers/unregisters a per-user `ONLOGON`/`LIMITED`
  Task Scheduler entry via `schtasks.exe` (no COM interop, no elevation needed in principle - see
  Known issues below for what actually happened live). `Build*Arguments` are pure/testable,
  separated from the process-spawning methods, matching the existing
  `NvidiaSmiGpuInfoProvider.ParseCsv` split. `UnregisterAsync` is idempotent (treats "task not
  found" as success). `IsRegisteredAsync` queries real Task Scheduler state rather than trusting
  the persisted flag.
- `SettingsViewModel`/`SettingsView.xaml` - real Settings page (previously a placeholder):
  autostart checkbox (queries real state on load, registers/unregisters immediately on toggle,
  reverts itself if the Task Scheduler call fails rather than showing a checked box that lied),
  plus UI for the other `AppSettings` fields that already existed but had no page
  (`StartMinimizedToTray`, `CloseButtonMinimizesToTray`, `NotificationsEnabled`,
  `LogRetentionDays`, `MaxLogSizeMb`, `MonitoringIntervalSeconds`), saved via an explicit
  "Сохранить" button.
- 6 new unit tests (`TaskSchedulerAutostartTests.cs`): register/unregister/query argument
  construction, task-name consistency across all three commands, `/RL LIMITED`/`/SC ONLOGON`/`/F`
  flags present, executable path passed as its own `ArgumentList` element (not manually quoted).

**Found and fixed a real bug during live verification, not just written and assumed correct:**
`schtasks.exe` writes its (Cyrillic, on this Russian-locale machine) error text using the console's
OEM codepage (866), not UTF-8. `Process.StandardError`'s default encoding doesn't match, which
turned a genuine "Access is denied" error into unreadable mojibake in the Settings page's status
message - caught by actually reading that message on the real machine, not by a passing unit test
(the encoding mismatch is invisible to any test that doesn't decode real OEM-codepage bytes).
Fixed by registering `System.Text.Encoding.CodePages` and resolving the real OEM codepage via
`GetOEMCP()` (P/Invoke), falling back to UTF-8 on any non-Windows host or resolution failure so the
cross-platform-buildable `Core` project still compiles and runs its test suite on Linux/CI.

**Live-verified on the real Windows machine, and a genuine environment-level limitation was found
and honestly documented rather than worked around or hidden:**
- Rebuilt (`dotnet build -c Release`, 0 warnings/0 errors) and retested (`dotnet test`, **87/87
  passing**, up from 81) on the real machine.
- Opened the Settings page: the autostart checkbox correctly loaded as unchecked (a real
  `schtasks /Query` against a task that doesn't exist), and all six other fields rendered with
  their current persisted values.
- Toggled the autostart checkbox on. The Task Scheduler call failed with **"ERROR: Отказано в
  доступе" (Access is denied)** - reproduced identically via a bare `schtasks /Create` and via
  PowerShell's `Register-ScheduledTask` cmdlet run directly in the same session, ruling out an
  app-specific bug. Investigated rather than assumed: the account is a member of
  `BUILTIN\Administrators`, but that membership is marked "Group used for deny only" in this
  session's token (standard UAC-filtered token for a non-elevated process) - normally a per-user
  `ONLOGON`/`LIMITED` task should not need elevation, and the filesystem ACLs on
  `C:\Windows\System32\Tasks` (checked via `icacls`) matched Windows' unmodified defaults
  (Authenticated Users get create-file rights, CREATOR OWNER gets full control on what they
  create) - so this reads as a Task Scheduler-level policy or security-software gate specific to
  this machine/session (Windows Defender real-time protection is active; a security product
  blocking "register a new autorun Task Scheduler entry" as a known persistence technique is the
  most likely explanation) rather than a code or ACL problem. After the encoding fix, this same
  failure now surfaces as a **readable** Russian error message, and the checkbox correctly reverts
  to unchecked rather than showing a state that was never actually applied - confirmed via
  `schtasks /Query` that no task was left behind after the failed attempt.
- Toggled `StartMinimizedToTray` on, clicked "Сохранить", and confirmed via PowerShell
  (`Get-Content settings.json | ConvertFrom-Json`) that it persisted correctly alongside the
  unchanged other fields. Reverted it to off and saved again, confirmed via the same check, leaving
  the machine's `settings.json` in its original state (`StartWithWindows: false`,
  `StartMinimizedToTray: false`, no leftover `KeryxNodeManager_Autostart` task).
- (Hit the same pre-existing "zombie low-memory process, no window" launch flake documented in
  earlier sessions' notes, several times in a row this session before a clean launch succeeded -
  worth a closer look in a future session if it starts happening on every single launch rather than
  intermittently, since this time it took 3-4 attempts instead of the usual 1-2.)

**What this means for brief §11**: the register/unregister/query implementation is real and
correctly wired to a real checkbox, the failure path is handled honestly (revert + readable error,
never a silently-lying persisted flag), and the *query* path (checkbox correctly shows "off" for a
non-existent task) was verified working. The *successful create* path could not be end-to-end
verified as actually completing on this specific machine due to the access-denial above - this is
flagged as a real, unresolved-in-this-session open question (see Known issues), not glossed over.

87 total Core tests; all passing on real Windows (`dotnet build`/`dotnet test`, 0 warnings/0
errors).

## This session's seventh increment: Safety monitor / overheat protection (new)

Brief §14 asked for overheat protection: watch GPU temperatures while mining and stop the miner
automatically if a card gets dangerously hot. Built:

- `Core/Safety/SafetyMonitor.cs` - polls the same `IGpuInfoProvider` the GPU page already uses (so
  it can never disagree with what the user sees there) on a background loop while mining is
  running, and is **edge-triggered**: a card sitting at a sustained high temperature only raises
  one event when its level actually changes (`Evaluate`/`ShouldRaiseEvent`/`BuildMessage` are pure,
  unit-testable, separated from the polling loop itself, matching this project's established
  pattern of splitting decision logic from I/O). Deliberately does not call `ProcessSupervisor` or
  stop anything itself - it only raises `SafetyEvent`s; `DashboardViewModel` (which already owns
  the mining session) decides that Critical means stop-all, keeping `SafetyMonitor` a pure "tell me
  what's happening" component.
- New `AppSettings.SafetyMonitorEnabled`/`GpuWarningTempC`/`GpuCriticalTempC` (defaults: enabled,
  85°C warning, 95°C critical), with a real Settings-page card ("Защита от перегрева") to view/edit
  them - not just Core fields nobody could reach.
- Wired into `DashboardViewModel`: `SafetyMonitor.Start(...)` is called only when the miner
  actually starts (tied to `AutoStartMiner`'s branch of `StartAllAsync`, not running unconditionally
  in the background), and `Stop()` on both the explicit "Остановить всё" path and the automatic
  overheat-triggered stop. A Critical event triggers `StopForOverheatAsync()`, which stops both
  Node and Miner supervisors and sets a distinct final status message
  ("Майнинг остановлен автоматически из-за перегрева GPU.") rather than reusing the generic
  user-requested-stop message, so the Dashboard can never make an overheat shutdown look like a
  manual one.
- 8 new unit tests (`SafetyMonitorTests.cs`): threshold boundaries (including a deliberate
  `Critical - 1` sanity check against silent enum reordering), edge-triggering transition table,
  message-content checks, and the regression test described below.

**Found and fixed a real race condition during live verification, not just written and assumed
correct.** `SafetyMonitor.EventRaised` fires synchronously from inside the polling loop's
`foreach (var device in devices)`. `DashboardViewModel`'s Critical-level handler reacts by calling
`_safetyMonitor.Stop()` **synchronously, from within that same event callback** - which cancels the
loop's token and clears the `_lastLevel` tracking dictionary. Without a guard, the *next* device in
that same batch would see an empty `_lastLevel`, wrongly treat its already-known Warning state as a
brand-new transition, and fire a second, stale event that overwrote the correct "stopped due to
overheat" Dashboard message with a plain warning-format one - reproduced live (two mock GPUs, one
crossing into Critical and triggering the stop, a second already sitting in Warning). Fixed with a
one-line `if (ct.IsCancellationRequested) break;` at the top of the loop, with a regression test
(`Stop_CalledFromWithinEventHandler_DoesNotCauseStaleEventForLaterDeviceInSameBatch`) using a new
`FakeGpuInfoProvider` test double that reproduces exactly this two-device-one-batch scenario and
asserts only the Critical event survives.

**Found and fixed a second, much more serious bug while re-verifying live on the real Windows
machine - a real, intermittent WPF startup deadlock that was almost certainly the true cause of the
"zombie process, no window" launch flake this project's own notes had puzzled over across several
earlier sessions (fourth and sixth increments above) and previously blamed on unrelated background
antivirus/installer activity.** `App.OnStartup` called
`profileStore.LoadAsync().GetAwaiter().GetResult()` directly. WPF installs a
`DispatcherSynchronizationContext` on the UI thread before `OnStartup` runs, but the Dispatcher's
actual message pump (`Dispatcher.Run()`) does not start until *after* `OnStartup` returns.
`ConfigStore.LoadAsync` does real async file I/O (`File.OpenRead` + `JsonDocument.ParseAsync`);
whenever that I/O doesn't happen to complete synchronously, its continuation is posted back to the
captured dispatcher context - which has no pump running yet to ever deliver it - while the same
thread sits blocked forever inside `GetResult()` waiting for that very continuation. Proved this
empirically rather than guessing: added a temporary startup trace log, reproduced 9 consecutive
hangs (real, not the usual 1-2-cycle flake - process alive, `Responding=True`, but genuinely zero
top-level windows ever created, confirmed via a `EnumWindows` P/Invoke check, not just an idle
screenshot), and the trace showed execution stopping dead at that exact line every time. Confirmed
the mechanism directly: renaming `settings.json` away (forcing `ConfigStore.LoadAsync`'s
`File.Exists` short-circuit, which returns a synchronously-completed Task) made the hang disappear
immediately and reliably; restoring the real file brought back the same class of hang, but
intermittently rather than deterministically - consistent with the read sometimes completing
synchronously (lucky, no deadlock) and sometimes genuinely yielding (unlucky, permanent deadlock),
which lines up exactly with this bug having been silently present and only occasionally triggering
across every earlier session in this project. Fixed by wrapping the call in `Task.Run(() =>
profileStore.LoadAsync()).GetAwaiter().GetResult()`, which moves the whole async chain onto a
thread-pool thread with no dispatcher context installed, so its awaits complete against the
thread-pool's own context and can never need the (not-yet-running) UI message pump. Removed the
temporary trace log after diagnosis. Stress-tested the fix with 8 consecutive kill/relaunch cycles
in `--mock` mode after applying it: **8/8 clean launches**, versus roughly 9 out of the preceding 10
attempts hanging before the fix. (`ProfileStore.SaveAsync`, called from Settings/wizard button
handlers, does not have this hazard - by the time a button click fires, the Dispatcher message pump
is already running, so its `await` continuations are delivered normally.)

**Live-verified the actual overheat behavior end to end** on the real Windows machine, in `--mock`
mode, after both fixes above: lowered `GpuWarningTempC`/`GpuCriticalTempC` to 40/50°C via the real
Settings page (mock GPU temperatures otherwise never reach the 85/95°C defaults) and set a valid
test mining address so the mock miner would actually start (`MinerArgumentBuilder.Build`'s own
pre-existing "mining address is required" guard - unrelated, expected behavior, not a bug). Clicked
"Запустить всё": within one poll, the Dashboard showed
`[Защита] NVIDIA GeForce RTX 5070: высокая температура 41°C (порог 40°C).` - correct Warning-level
detection. After further polls, the RTX 3090 (base 48°C ± jitter) rolled up to 50°C, crossing
Critical: the Dashboard's Node/Miner status flipped to "Остановлена"/"Остановлен" and the message
read exactly `Майнинг остановлен автоматически из-за перегрева GPU.` - stable, not overwritten by a
stray Warning message for a different card, confirming the race-condition fix above actually works
live, not just in the unit test. Cross-checked via the real log files (not just trusting the UI):
both `keryxd-...log` and `keryx-miner-...log` had their last line at `18:50:47 UTC` with no further
output more than a minute later, matching the "stopped" status and proving the mock processes were
genuinely terminated, not just visually marked stopped.

103 total Core tests; all passing on real Windows (`dotnet build -c Release` then `dotnet test`,
0 warnings/0 errors, 103/103).

## This session's eighth increment: Profiles + About pages (new)

Both were the last two nav items still showing the generic "not implemented" placeholder. Built:

- **Core (`Config/ProfileStore.cs`)**: `AppSettings.Profiles`/`ActiveProfileName` have existed
  since the very first config schema, but `ProfileStore` itself could previously only
  load-once/save-the-one-it-has - there was no way to actually reach a second profile from the UI,
  even though the data model was already shaped for it. Added `ProfileNames` (a live projection,
  never a stale cache), `SwitchActiveProfileAsync`, `CreateProfileAsync` (rejects empty/
  duplicate-case-insensitive names, switches to the new profile immediately), `RenameProfileAsync`
  (renames the same `MiningProfile` instance in place - GPU assignments/executable paths/etc. all
  carry over, a rename is not a create+delete), `DeleteProfileAsync` (refuses to delete the last
  remaining profile - the app always needs at least one to bind Node/Miner/Dashboard against;
  deleting the active profile switches to whichever profile is now first in the list), and an
  `ActiveProfileChanged` event so `MainViewModel`'s nav-strip label can stay live without polling.
  13 new unit tests (`ProfileStoreTests.cs`): auto-create-default-on-empty-file, create/switch/
  rename/delete round-tripping through a real `ConfigStore` on a temp file (not mocked - matching
  `ConfigStoreTests`' own pattern), every guard rail (duplicate name, empty name, unknown name,
  last-profile deletion, deleting the active profile), and the event firing exactly on the
  transitions that actually change `ActiveProfile`.
- **`AppVersionInfo.cs` (new)** - single source of truth for the version string, reading the real
  assembly version (`Assembly.GetExecutingAssembly().GetName().Version`) instead of a hand-written
  literal. Found and fixed a small but real duplication bug while building the About page:
  `MainViewModel` and `LogsViewModel` each separately hardcoded their own `"0.1.0"` string
  constant, with `LogsViewModel`'s own comment admitting "kept in sync manually" - a version bump
  in the `.csproj` would have silently stopped matching what the UI showed, with nothing ever
  flagging the mismatch. Both now read `AppVersionInfo.Current`.
- **`ProfilesViewModel`/`ProfilesView.xaml` (new)** - active-profile display, a `ListBox` of
  profile names, and three small forms (switch/create/rename) plus a delete button, each calling
  straight through to the new `ProfileStore` methods and surfacing failures as a status message
  rather than a silent no-op.
- **`AboutViewModel`/`AboutView.xaml` (new)** - real version (via `AppVersionInfo`), app
  description, links to the Keryx protocol's own upstream repos (`keryx-node`, `keryx-miner` on
  GitHub) and `keryx-labs.com` (per docs/KERYX_RESEARCH.md - this app itself has no public repo of
  its own yet, stated plainly rather than inventing a URL), and real system info (OS version,
  .NET runtime version, 64-bit flag) reusing the same fields `DiagnosticsExporter`'s
  `system-info.txt` already captures, so the two can never disagree.
- Wired into `MainWindow.ShowPage`, `App.xaml.cs` DI, and `MainViewModel.Pages`
  (Dashboard/GPU/Models/Node/Miner/Logs/Diagnostics/**Profiles**/Settings/About). `MainViewModel`
  now takes a real `ProfileStore` dependency and subscribes to `ActiveProfileChanged` instead of
  holding a hardcoded `"Default"` literal that nothing ever updated.

116 total Core tests; all passing on real Windows (`dotnet build -c Release` then `dotnet test`,
0 warnings/0 errors, 116/116) - built and tested in the Linux sandbox first
(`EnableWindowsTargeting=true`, exercising the WPF markup compiler for the two new views), then
rebuilt/retested for real on Windows.

**Live-verified on the real Windows machine, in `--mock` mode** (clean launch confirming the
previous session's startup-deadlock fix continues to hold): opened Profiles, created a
`TestRig2` profile via the real UI and confirmed it both appeared in the list and became the
active profile immediately, with the nav-strip footer ("Профиль: TestRig2") updating live in the
same instant - proving the `ActiveProfileChanged` wiring actually reaches `MainViewModel`. Switched
back to `Default`, selected `TestRig2` and deleted it, confirmed the list correctly shrank back to
one entry and the status message read "Профиль «TestRig2» удалён." - then, with only `Default`
left, clicked Delete again and confirmed the last-profile guard correctly blocked it ("Не удалось
удалить профиль: Нельзя удалить последний оставшийся профиль.") rather than leaving the app with
zero profiles. Cross-checked `settings.json` directly afterward (not just trusting the UI): only
`Default` remains, `ActiveProfileName` correctly reads `"Default"`, no `TestRig2` residue left
behind. Opened About and confirmed the version shown ("0.1.0") matches the `.csproj`'s real
`<Version>` (not a stale duplicate), the hyperlinks render as real clickable links, and the system
info block shows the actual live OS/.NET values for this machine.

## This session's ninth increment: localization infrastructure, first pass (new)

Brief §16 asks for EN/UK localization. Honest scope assessment done before touching code (via a
research pass): the app has accumulated roughly 300 lines of hardcoded Cyrillic text across ~31
files (13 XAML Views, 18 C# files - and notably some of it lives in `KeryxNodeManager.Core` itself,
e.g. `SystemChecker`/`ProcessSupervisor`/`TierAssigner` exception messages, not just the App layer).
Extracting every one of those strings and translating all of them to English *and* Ukrainian in a
single pass was judged not realistic to actually finish and verify properly in one sitting - and
claiming otherwise would violate this project's own "never claim done unless it actually
ran/passed" rule. So this increment deliberately builds and proves the *mechanism* end-to-end on a
representative slice, rather than attempting (and likely half-finishing) full coverage:

- **`LocalizationManager.cs` (new)** - swaps a `Strings.{lang}.xaml` `ResourceDictionary` into
  `Application.Current.Resources.MergedDictionaries` at runtime, piggybacking on the same merge
  mechanism `App.xaml` already uses for `DarkTheme.xaml` - the difference is this one is swappable
  *after* startup. XAML consumers bind via `{DynamicResource Str_Xxx}`, never `{StaticResource}` -
  the latter resolves once at load time and would silently never pick up a later language switch.
  Falls back to `"ru"` for any unrecognized language code (including `"uk"`, not yet built) rather
  than throwing, since a bad or old persisted value must never crash startup.
- **`Resources/Strings.ru.xaml` / `Strings.en.xaml` (new)** - only `"ru"` and `"en"` exist so far;
  Ukrainian is explicitly not attempted this session (stated honestly rather than shipped as a
  low-quality machine-style translation).
- Wired `AppSettings.Language` (existed since the very first config schema, defaulted to `"ru"`,
  but was never actually read by any code before this) into a real, live-switching Settings-page
  `ComboBox` (`SettingsViewModel.Language` + `OnLanguageChanged` calling `LocalizationManager.Apply`
  and persisting via the existing `SaveAsync`/autostart-toggle pattern of "apply immediately, don't
  wait for the Save button").
- Converted the **Settings page** (every label, checkbox, header, and button) and the **MainWindow
  nav-footer** ("Version: X" / "Profile: Y") to `DynamicResource` bindings - chosen specifically
  because the language switcher itself lives on the Settings page (dogfooding: if switching didn't
  actually re-render its own page live, that would be immediately obvious) and the nav footer is
  visible on every single page regardless of which one is open.
- **Everything else - all ~11 other Views, their ViewModels, and every Core-layer exception/status
  message - is still hardcoded Russian, unchanged from before this session.** This is stated
  explicitly here and in "In progress / next steps" below, not left implicit.

**Found and fixed a real crash bug during live verification, not just written and assumed
correct.** WPF's `Run.Text` dependency property defaults to `BindingMode.TwoWay` (unlike
`TextBlock.Text`, which defaults to `OneWay`) - a WPF quirk that is easy to not know about. The nav
footer's `Run Text="{Binding AppVersion}"` (no explicit `Mode=`) crashed the app on every launch
with `InvalidOperationException: A TwoWay or OneWayToSource binding cannot work on the read-only
property 'AppVersion'` - because the Profiles/About increment earlier this same session had
changed `MainViewModel.AppVersion` from a mutable `[ObservableProperty]` into a read-only computed
property (`=> AppVersionInfo.Current`). The two changes were made in different increments and
never exercised together until this one actually ran the app after wiring the nav footer through
`Run` elements. Confirmed via Event Viewer (`.NET Runtime` error entry with the full stack trace
pointing at `App.OnStartup` → `Window.Show()`) rather than guessing from symptoms. Fixed by adding
an explicit `Mode=OneWay` to both nav-footer `Run` bindings (`AppVersion` and `ActiveProfileName`).

**Live-verified on the real Windows machine, in `--mock` mode**, after the fix: rebuilt (0
warnings/0 errors) and retested (`dotnet test`, 116/116 - no new Core tests needed since this
increment is presentation-only, matching how earlier UI-only work in this project was verified
live rather than unit-tested). Opened Settings, selected "English" from the new Language dropdown,
and confirmed **every single label on the page re-rendered live, with no app restart**, including
the nav-footer's "Version: 0.1.0" / "Profile: Default" (previously "Версия"/"Профиль") - proof the
`DynamicResource` mechanism actually works, not just that the resource files parse. Confirmed
`settings.json`'s `Language` field persisted to `"en"` (checked directly, not just trusted the UI).
Killed and relaunched the app fresh and confirmed the Settings page came up in English immediately
on startup (proving `LocalizationManager.Apply` at the top of `OnStartup` actually takes effect
before any window is shown, not just after a live in-session switch). Restored `Language` back to
`"ru"` afterward (directly via `settings.json`, since this was cleanup rather than something that
needed UI re-verification) to leave the machine in its original default state.

One cosmetic issue noticed but not chased down: the Language `ComboBox`'s own selected-item text
was not visibly legible against its dark background in this environment's screenshots (the
dropdown *list* itself, when open, rendered "Русский"/"English" correctly - it's specifically the
closed-state selection box's text/background contrast that looked off). Every other converted
element (checkboxes, headers, buttons, the nav footer) rendered with correct, legible contrast, so
this reads as a narrow styling gap in the unstyled default `ComboBox` template rather than a
functional defect - the underlying value binds, persists, and drives the switch correctly regardless
of what it looks like. Flagged honestly below rather than silently left for someone else to
discover.

116 total Core tests; all passing on real Windows (`dotnet build -c Release` then `dotnet test`,
0 warnings/0 errors, 116/116) - unchanged from the previous increment, since this one added no new
Core-layer logic to test.

## This session's tenth increment: real single-instance IPC (new)

Brief §10/§27: relaunching the app while an instance is already running previously just refused
the second launch with a message box. Built a minimal one-directional named-pipe signal
(`SingleInstanceIpc`, `KeryxNodeManager.SingleInstance.Pipe`): the primary instance starts a
background accept-loop after `mainWindow.Show()`; a second launch attempt (the one that loses the
`KeryxNodeManager.SingleInstance` mutex race) connects, writes the literal string `"SHOW"`, and the
primary instance's callback marshals onto the UI thread via `Dispatcher.Invoke` and calls
`TrayIconService.ShowMainWindow()` (made `public` - the exact same restore logic the tray icon's own
menu item already uses, not a second implementation). If signaling fails within a 2-second timeout
(no listener, or something's actually wrong), the second instance falls back to the original honest
"already running, couldn't reach it" message box rather than silently doing nothing.

**Real bug found and fixed - the third occurrence of the same WPF sync-over-async deadlock class
in this project** (see the eleventh-hour bugfix in the seventh increment for the first: the
`ProfileStore.LoadAsync()` case). The first version of this code called
`SingleInstanceIpc.TrySendShowRequestAsync(...).GetAwaiter().GetResult()` directly in
`App.OnStartup`'s `!isNew` branch, with an inline comment claiming this was safe because "this runs
before any WPF window/Dispatcher machinery has been touched at all." That reasoning was wrong: WPF
installs the `DispatcherSynchronizationContext` on the UI thread as part of `Application`'s own
startup sequence, before `OnStartup` itself ever runs - regardless of what code executes early in
the method. Reproduced live: launching a second `--mock` instance while the first was running left
the second process alive and `Responding=True` but with `MainWindowHandle=0`, hung indefinitely -
the exact "zombie process, no window" signature from the earlier bug. This instance of the bug is
**deterministic, not intermittent** (unlike the file-read case): `NamedPipeClientStream.ConnectAsync`
is genuine wait-for-a-server I/O that essentially never completes synchronously, so the deadlock
reproduced 100% of the time rather than only under I/O contention. Fixed with the same proven
pattern: `Task.Run(() => SingleInstanceIpc.TrySendShowRequestAsync(...)).GetAwaiter().GetResult()`,
moving the whole async chain onto a thread-pool thread with no captured
`DispatcherSynchronizationContext` to deadlock against.

Live-verified on real Windows after the fix: launched a first `--mock` instance (clean launch,
non-zero `MainWindowHandle`), minimized it via `ShowWindowAsync`/`SW_MINIMIZE` and confirmed
`IsIconic()` returned `true`, then launched a second `--mock` instance. The second instance's
process exited on its own within ~3.5 seconds (not hung); only the first instance's process
remained. Confirmed via `IsIconic()` (now `false`) and `GetForegroundWindow()` (now equal to the
first instance's handle) that the window was genuinely un-minimized and brought to the foreground -
not a no-op - and confirmed the same visually via screenshot. This is a meaningful restore test,
not just "the second process exited," because the window was deliberately minimized first.

Did not separately re-test the failure-fallback message-box path (e.g. by killing the pipe server
mid-test) this session - lower priority given the primary signal path is now proven to work, and
the fallback code itself is unchanged from before this increment (already existed as the sole
behavior previously). Flagged here rather than silently assumed to still work.

116 total Core tests; all passing on real Windows (`dotnet build -c Release` then `dotnet test`,
0 warnings/0 errors, 116/116) - unchanged, since this increment is presentation/App-layer wiring
with no new Core-layer logic to test.

## This session's eleventh increment: es/it/fr localization (new)

User request: bring the language switcher up to "top popular languages" - specifically Spanish,
Italian, and French, alongside the existing Russian/English. Scoped narrowly and honestly to match
what the ninth increment actually built: only the Settings page and MainWindow nav footer are wired
to `DynamicResource`/`Strings.*.xaml` keys so far (item 1 in "In progress" below, unchanged), so
this increment adds three new translations of that *same* 21-key set rather than doing full-app
string extraction, which remains a separate, larger, already-tracked task.

Added `Strings.es.xaml`, `Strings.it.xaml`, `Strings.fr.xaml` (translating all 21 existing keys),
extended `LocalizationManager.SupportedLanguages` from `{ "ru", "en" }` to `{ "ru", "en", "es", "it",
"fr" }`, and added three `ComboBoxItem`s to the Settings page's language dropdown
(`Tag="es"`/`Content="Español"`, etc.). No `.csproj` changes needed - the new `.xaml` files under
`Resources/` are picked up automatically by the SDK-style project's default `Page` item glob, same
as the original `Strings.ru.xaml`/`Strings.en.xaml`.

No new bug found this increment (the mechanism itself - `DynamicResource` + `LocalizationManager.Apply`
swapping the merged dictionary - was already proven correct in the ninth increment; adding more
translation files to prove language codes exercises no new code path). Live-verified on real
Windows: rebuilt (`dotnet build -c Release`, 0 warnings/0 errors) and re-ran tests (116/116,
unchanged - presentation-only). Launched the app, opened the Settings page, and switched through
Español → Italiano → Français in sequence, confirming for each: (a) every label on the page and the
nav footer re-rendered live in the correct language with no app restart (checked via screenshot
each time - "Configuración"/"Idioma"/"Guardar", "Impostazioni"/"Lingua"/"Salva",
"Paramètres"/"Langue"/"Enregistrer", all correct), and (b) `settings.json`'s `Language` field
persisted the right two-letter code each time (checked directly via PowerShell, not just trusted
the UI: `es` → `it` → `fr`). Restored `Language` back to `"ru"` afterward directly in
`settings.json` (cleanup, not something needing UI re-verification) and relaunched once more to
confirm the app still starts cleanly off a hand-edited settings file (`MainWindowHandle` non-zero
immediately) - this doubled as a light regression check on `ConfigStore`'s JSON round-trip.

While reaching the Language dropdown, re-confirmed the maximized-window-only ScrollViewer gap noted
in the ninth increment (item 8 below) - navigating there on a non-maximized window left the control
off-screen, worked around the same way as before (maximize first). Not re-fixed this increment
(out of scope for a translation-only pass); still tracked below.

116 total Core tests; all passing on real Windows (0 warnings/0 errors, 116/116) - unchanged, since
this increment added no new Core-layer logic.

## This session's twelfth increment: Language ComboBox contrast bug, root-caused and fixed (new)

Picked up the item flagged (not yet chased down) after the ninth and eleventh increments: the
Settings page's Language `ComboBox` showed no visible text at all in its closed state across every
screenshot taken this session, while the open dropdown list rendered fine.

**Root cause**: the `ComboBox` had explicit `Background="{StaticResource SurfaceAltBrush}"` and
`Foreground="{StaticResource TextPrimaryBrush}"` (dark surface / near-white text - the same pairing
used successfully elsewhere in the app, e.g. `CardStyle` borders). But WPF's default `ComboBox`
control template does not actually route the `Background` property through to the closed-state
selection box's chrome - that part renders with the system's own light/white brush regardless of
what's set on the `ComboBox` element. So only the `Foreground` override took effect, producing
near-white text (`#FFE6E9EF`) on a background that was still system white: invisible in practice.
The open dropdown list looked fine because `ComboBoxItem`'s own default template does route
`Foreground` against a background that isn't pinned white the same way, so the mismatch was
specific to the closed state - exactly why it was easy to notice-but-not-diagnose across two prior
increments' screenshots without actually opening the dropdown to compare.

**Fix**: removed both overrides, letting the `ComboBox` use its default system colors - the same,
already-working pattern GpuView's unstyled per-card mode `ComboBox` already used successfully
(confirmed by inspection, not just assumption, before making this call). This trades visual
dark-theme consistency for correctness within this increment; a proper themed `ComboBox`/`ComboBoxItem`
control template (matching the app's `CardStyle`/`PrimaryButtonStyle` custom-template pattern) is a
separate, larger undertaking, not attempted here to keep the change minimal and low-risk.

Live-verified on real Windows: rebuilt (`dotnet build -c Release`, 0 warnings/0 errors), re-ran
tests (116/116, unchanged), launched, maximized (still needed to reach the control - the
ScrollViewer gap, item 8 below, is unrelated and untouched), navigated to Settings, and confirmed
via screenshot that "Русский" is now clearly legible in the closed ComboBox against its (now
default, light) background.

116 total Core tests; all passing on real Windows (0 warnings/0 errors, 116/116) - unchanged, since
this increment is a pure XAML/styling fix with no Core-layer logic involved.

## This session's thirteenth increment: Dashboard string extraction, first slice of full extraction (new)

Started on item 1 below ("full string extraction remains") - the largest, most open-ended item on
the list - by taking the first concrete slice: the Dashboard page's *static* View-layer strings
(title "Обзор"/"Overview"/etc., the Node/Miner/GPU card labels, the GPU active/disabled count
suffixes, and the Start All/Stop All/Refresh button labels). Added 9 new keys
(`Str_Dashboard_Title`, `_Node`, `_Miner`, `_Gpu`, `_GpuActiveSuffix`, `_GpuDisabledSuffix`,
`_StartAll`, `_StopAll`, `_Refresh`) to all five `Strings.*.xaml` files and wired `DashboardView.xaml`
to them via `DynamicResource`, same mechanism as the Settings page.

**Deliberately NOT covered by this slice, and important to be explicit about why**:
`DashboardViewModel`'s dynamically-set status strings - `NodeStatus`/`MinerStatus` (initialized to
literal `"Остановлена"`/`"Остановлен"` and reassigned imperatively from `ProcessSupervisor.EventRaised`
handlers) and `LastActionMessage` (built from interpolated strings, several including raw
`ex.Message` from Core-layer exceptions) - are plain C# `string` properties, not XAML markup. They
cannot be wired to `DynamicResource` at all (that only works from XAML), and even a C#-side
`LocalizationManager.GetString(key)` lookup would go stale the moment the user switches language
languages while Dashboard is open, because nothing currently re-evaluates already-set ViewModel
string properties when `LocalizationManager.Apply` runs - there's no "language changed" event to
hook. Confirmed this gap live and honestly, on purpose: switched to French mid-session (via
`settings.json` + relaunch) and confirmed the Dashboard title/labels/buttons all correctly showed
French while `NodeStatus`/`MinerStatus` continued to display hardcoded Russian
("Остановлена"/"Остановлен") - exactly the documented limitation, not a surprise bug. Properly
fixing this needs real design work (most likely: store status as an enum, add a
`LocalizationManager.LanguageChanged` event, and have ViewModels that display translatable dynamic
state re-project their displayed strings on that event) - out of scope for this slice, tracked
explicitly below rather than silently left as an unexplained inconsistency.

Live-verified on real Windows: rebuilt (`dotnet build -c Release`, 0 warnings/0 errors), re-ran
tests (116/116, unchanged - View-layer only), launched fresh in Russian (default) and confirmed the
Dashboard renders correctly, then switched `settings.json`'s `Language` to `"fr"` and relaunched to
confirm `LocalizationManager.Apply` at startup correctly translates the static Dashboard
strings before any window is shown ("Aperçu", "Nœud"/"Mineur"/"GPU", "3 active(s) / 0 désactivée(s)",
"Tout démarrer"/"Tout arrêter"/"Actualiser" - all correct, including the dynamically-count-suffixed
GPU text), while separately confirming the known NodeStatus/MinerStatus gap above. Restored
`Language` back to `"ru"` afterward directly in `settings.json` (cleanup).

116 total Core tests; all passing on real Windows (0 warnings/0 errors, 116/116) - unchanged, since
this increment is View-layer XAML/resource work only.

## This session's fourteenth increment: GPU page string extraction, second slice (new)

Continued item 1 with the GPU page's static labels: page title "Видеокарты"/etc., the "Обновить"
button, the "Режим:" mode-dropdown label, and the static text fragments inside each GPU card's
CUDA/VRAM/temperature summary line (`Str_Gpu_CudaLabel` = "CUDA ", `Str_Gpu_VramUnit` =
" МБ VRAM · "/" MB VRAM · "/" Mo VRAM · " for French's megabyte abbreviation, `Str_Gpu_TempUnit` =
"°C"). Added 6 new keys to all five `Strings.*.xaml` files and wired `GpuView.xaml` to them via
`DynamicResource`, same pattern as Dashboard.

Same explicit, deliberate scope limit as Dashboard, and for a related reason: `GpuCardViewModel.
AssignmentSummary` is set from Core's `TierAssigner.Explanation` (a genuine Core-layer message, not
covered by this pass - same open decision noted in item 1) and `GpuViewModel.ModeOptions`
("Авто"/"Отключено"/"Вручную: {tier}") is a `static` property built once at type-load time from
hardcoded Russian literals - not tied to `Application` state at all, so it can't react to a language
switch even in principle without a real refactor (making it instance-level and rebuilding it on a
future `LocalizationManager.LanguageChanged` event, the same fix item 1 already flags for
Dashboard's `NodeStatus`/`MinerStatus`). Documented here rather than silently left inconsistent.

Live-verified on real Windows: rebuilt (0 warnings/0 errors), re-ran tests (116/116, unchanged),
launched with `--mock` (so the GPU page has real card data to render) after switching `settings.json`
to `"it"`, and confirmed via screenshot: "Schede video" title, "Aggiorna" button, "Modalità:" label,
and each card's summary line correctly reading "CUDA0 · 12288 MB VRAM · 47 °C" - all four newly
localized surfaces correct simultaneously, alongside the ModeOptions dropdown ("Авто") and
AssignmentSummary text ("доступно ... назначен тир ...") staying Russian exactly as documented
above, not a surprise. Restored `Language` to `"ru"` afterward directly in `settings.json`.

116 total Core tests; all passing on real Windows (0 warnings/0 errors, 116/116) - unchanged, since
this increment is View-layer XAML/resource work only.

## This session's fifteenth increment: Models page string extraction, third slice (new)

Continued item 1 with the Models page's static labels: page title, "Обновить" button, the
" · от "/VRAM-suffix separators in each model card's spec line, the URL/SHA-256 field labels, and
the Download/Pause/Cancel/Delete/Open-folder buttons. Added 11 new keys to all five
`Strings.*.xaml` files and wired `ModelsView.xaml` to them via `DynamicResource`. Same documented
scope limit as the previous two slices: `ModelCardViewModel.StatusText` (install status, download
progress, error messages) is dynamic ViewModel-generated text and stays hardcoded Russian.

Live-verified on real Windows: rebuilt (0 warnings/0 errors), re-ran tests (116/116, unchanged),
switched `settings.json` to `"es"`, launched with `--mock`, and confirmed via screenshot on the
Models page: "Modelos" title, "Actualizar" refresh button, "Descargar"/"Cancelar"/"Abrir carpeta"
buttons, and "URL de descarga (pega un enlace del README de keryx-miner)"/"SHA-256 esperado
(opcional)" field labels all correctly in Spanish across all 5 rendered model cards, while each
card's `StatusText` stayed hardcoded Russian ("Не установлена. Модель также будет скачана
автоматически...") exactly as documented, not a surprise. Restored `Language` to `"ru"` afterward
directly in `settings.json`.

116 total Core tests; all passing on real Windows (0 warnings/0 errors, 116/116) - unchanged, since
this increment is View-layer XAML/resource work only.

## This session's sixteenth increment: Node page string extraction, fourth slice (new)

Continued item 1 with the Node page's static labels: page title, the keryxd.exe path/endpoint
field labels, the Browse/Save/Check-endpoint buttons, the testnet checkbox, and the footer note
explaining that start/stop happens from the Overview page. Added 8 new keys to all five
`Strings.*.xaml` files and wired `NodeView.xaml` to them via `DynamicResource`. Same documented
scope limit as the previous slices: `NodeViewModel.StatusMessage` (endpoint-check results/errors)
and the native `OpenFileDialog`'s title/filter (set directly in C#, not XAML) stay hardcoded
Russian.

Live-verified on real Windows: rebuilt (0 warnings/0 errors), re-ran tests (116/116, unchanged),
switched `settings.json` to `"fr"`, launched with `--mock`, and confirmed via screenshot on the
Node page: "Nœud" title, "Chemin vers keryxd.exe"/"Adresse keryxd (généralement 127.0.0.1)" labels,
"Parcourir..."/"Enregistrer"/"Vérifier l'endpoint" buttons, "Réseau de test (testnet)" checkbox,
and the footer note all correctly in French. Restored `Language` to `"ru"` afterward directly in
`settings.json`.

116 total Core tests; all passing on real Windows (0 warnings/0 errors, 116/116) - unchanged, since
this increment is View-layer XAML/resource work only.

## This session's seventeenth increment: Miner page string extraction, fifth slice (new)

Continued item 1 with the Miner page's static labels: page title, mining-address/executable-path/
models-folder field labels, the Browse/Save buttons, the Advanced Mode checkbox, the command-preview
header, and the multi-GPU note ("each GPU runs its own model, VRAM is never pooled" - brief §6's
critical constraint, worth having correctly translated everywhere it appears). Added 9 new keys to
all five `Strings.*.xaml` files and wired `MinerView.xaml` to them via `DynamicResource`. One label
("Mining address") had been shown in English regardless of the selected language even before this
pass - a small pre-existing gap, fixed here by giving it a real `Str_Miner_AddressLabel` key
translated properly per language (e.g. "Адрес для майнинга" for Russian) rather than perpetuating
the English-only literal. Same documented scope limit as the previous slices:
`MinerViewModel.StatusMessage`/`AddressValidationMessage`/`CommandPreview` (the last of which is
built from live `MinerArgumentBuilder.Build()` output - the actual keryx-miner.exe command line,
not just a label) and the native `OpenFileDialog`/`OpenFolderDialog` titles stay hardcoded Russian.

Live-verified on real Windows: rebuilt (0 warnings/0 errors), re-ran tests (116/116, unchanged),
switched `settings.json` to `"it"`, launched with `--mock`, and confirmed via screenshot on the
Miner page: "Miner" title (Italian keeps the English loanword, this is intentionally correct per
translation, not a miss), "Indirizzo di mining"/"Percorso di keryx-miner.exe"/"Cartella dei
modelli" labels, "Sfoglia..." (both browse buttons)/"Salva" buttons, "Modalità avanzata" checkbox,
and - after checking that box to reveal the advanced panel - "Comando di avvio (anteprima)" header
plus "Ogni GPU esegue il proprio modello. La VRAM di GPU diverse non viene mai unita." note, all
correctly in Italian, alongside the live command preview and `StatusMessage`/`AddressValidationMessage`
areas (empty/not triggered in this pass) which remain wired to Russian-only ViewModel text as
documented. Restored `Language` to `"ru"` afterward directly in `settings.json`.

116 total Core tests; all passing on real Windows (0 warnings/0 errors, 116/116) - unchanged, since
this increment is View-layer XAML/resource work only.

## This session's eighteenth increment: Logs page string extraction, sixth slice (new)

Continued item 1 with the Logs page's static labels: page title, the Export diagnostics/Open
folder/Clear display buttons, and the Node(keryxd)/Miner(keryx-miner) column headers. Added 6 new
keys to all five `Strings.*.xaml` files and wired `LogsView.xaml` to them via `DynamicResource`.
Same documented scope limit as the previous slices: `LogsViewModel.StatusMessage` and the native
`SaveFileDialog` title/filter stay hardcoded Russian; the `[ERR]`/`[OUT]` line-prefix tags inside
each formatted log line (`LogsViewModel.Format`) were deliberately left as invariant technical
shorthand rather than translated, matching how `CUDA`/`°C` were treated on the GPU page.

Live-verified on real Windows: rebuilt (0 warnings/0 errors), re-ran tests (116/116, unchanged),
switched `settings.json` to `"es"`, launched with `--mock`, and confirmed via screenshot on the
Logs page: "Registros" title, "Exportar diagnóstico..."/"Abrir carpeta de registros"/"Limpiar
pantalla" buttons, and "Nodo (keryxd)"/"Minero (keryx-miner)" column headers all correctly in
Spanish. Restored `Language` to `"ru"` afterward directly in `settings.json`.

116 total Core tests; all passing on real Windows (0 warnings/0 errors, 116/116) - unchanged, since
this increment is View-layer XAML/resource work only.

## This session's nineteenth increment: Profiles page string extraction, seventh slice (new)

Continued item 1 with the Profiles page's static labels: page title, the "Active profile: " label,
Make Active/Delete buttons, and the New Profile/Rename sections' headers, buttons, and explanatory
notes. Added 10 new keys to all five `Strings.*.xaml` files and wired `ProfilesView.xaml` to them
via `DynamicResource`. Same documented scope limit as the previous slices:
`ProfilesViewModel.StatusMessage` (switch/create/rename/delete confirmation and error messages)
stays hardcoded Russian.

Live-verified on real Windows: rebuilt (0 warnings/0 errors), re-ran tests (116/116, unchanged),
switched `settings.json` to `"fr"`, launched with `--mock`, and confirmed via screenshot on the
Profiles page: "Profils" title, "Profil actif : Default", "Activer"/"Supprimer" buttons, "Nouveau
profil"/"Renommer le profil sélectionné" headers, "Créer"/"Renommer" buttons, and both explanatory
notes ("Crée un profil avec les paramètres par défaut..."/"Les paramètres, les affectations GPU et
les chemins des exécutables sont conservés...") all correctly in French. Restored `Language` to
`"ru"` afterward directly in `settings.json`.

116 total Core tests; all passing on real Windows (0 warnings/0 errors, 116/116) - unchanged, since
this increment is View-layer XAML/resource work only.

## This session's twentieth increment: About page string extraction, eighth slice (new)

Continued item 1 with the About page's static labels: the version label, the full app-description
paragraph, the Links card's header/two GitHub-repo-link texts/no-public-repo note, and the System
card's header plus OS/.NET/64-bit labels. Added 10 new keys to all five `Strings.*.xaml` files and
rewrote `AboutView.xaml` to wire every static label to `DynamicResource`. The four value bindings
(`AppVersion`/`OperatingSystem`/`DotNetRuntime`/`Is64Bit`) needed converting from `StringFormat` to
adjacent-`Run`-pairs, since `StringFormat` can't hold a nested `DynamicResource` for the label half
- each got an explicit `Mode=OneWay` (all four are read-only computed `AboutViewModel` properties,
so the same `Run.Text`-defaults-to-`TwoWay` crash class documented in the ninth increment applies
here too if omitted). "Keryx Node Manager" (product name) and "keryx-labs.com" (domain literal) were
deliberately left untranslated - proper nouns, not UI text.

Unlike every other page in this string-extraction pass, **About has no ViewModel-owned dynamic
string gap to document**: all of `AboutViewModel`'s properties (`AppVersion`, `OperatingSystem`,
`DotNetRuntime`, `Is64Bit`) are read-only computed values with no status/validation/preview message
equivalent, so this page's localization is now fully complete rather than partial.

Live-verified on real Windows: rebuilt (0 warnings/0 errors), re-ran tests (116/116, unchanged),
switched `settings.json` to `"it"`, launched with `--mock`, navigated to the About page (the highest
concentration of `Run.Text` bindings to read-only properties of any page touched this pass -
double-checked via a direct `Get-Process` call after navigating, confirming no crash, before taking
the confirming screenshot), and confirmed via screenshot: "Keryx Node Manager" / "Versione0.1.0",
the full Italian description paragraph, "Link" header with "keryx-node (nodo) su GitHub"/
"keryx-miner (miner) su GitHub"/"keryx-labs.com" hyperlink texts, the Italian no-public-repo note,
"Sistema" header, and "SO:Microsoft Windows NT 10.0.26200.0"/".NET:8.0.29"/"SO a 64 bit:True" all
rendering correctly. Restored `Language` to `"ru"` afterward directly in `settings.json` and killed
the test process.

116 total Core tests; all passing on real Windows (0 warnings/0 errors, 116/116) - unchanged, since
this increment is View-layer XAML/resource work only.

## This session's twenty-first increment: first-run wizard string extraction, ninth and final static-page slice (new)

Continued (and closes out) item 1 with the first-run wizard's static labels across all 7 steps:
the window title, header, every step's title/body/checkbox/note text, and the Skip/Back/Next/
Finish buttons - 30 new keys added to all five `Strings.*.xaml` files, `WizardWindow.xaml` fully
rewritten to bind every static label to `DynamicResource`. The four multi-line explanatory
paragraphs (Welcome, Directories note, Address body, GPU-preview body, Autostart note, Finish
body) were collapsed to single-line resource strings (matching the pattern already used for
`Str_Node_FooterNote` etc. in earlier increments) since WPF collapses element-content whitespace
identically either way. Same documented scope limit as every other page this pass:
`WizardViewModel.StepHeader`/`DirectoriesError`/`AddressValidationMessage`, the live
`SystemChecker`/`GpuAssignmentResolver` result strings shown per-row (`WizardCheckRow.Detail`,
`WizardGpuPreviewRow.Explanation`), and the two "Адрес: "/"Папка моделей: " summary-row values on
the Finish step's own live data stay hardcoded Russian/dynamic - only the wizard's own static
labels moved.

No new bug found this increment - `Title="{DynamicResource ...}"` on a `Window` (rather than a
`UserControl`, which every earlier page in this pass used) works the same way as on any other
`DependencyObject`, confirmed by the window's actual titlebar text changing correctly with the
language switch (see below).

Live-verified on real Windows: rebuilt (`dotnet build -c Release`, 0 warnings/0 errors) and re-ran
tests (`dotnet test`, 116/116, unchanged - presentation-only). The Linux sandbox cross-compile
pre-check that every earlier increment this session also ran was skipped this time only - the
sandbox environment hit unrelated infrastructure trouble (its own disk full at `/sessions`, then a
stalled/no-output `dotnet build` after redirecting `HOME`/`NUGET_PACKAGES` off that volume) that
had nothing to do with this change; the authoritative real-Windows build/test/run below is what
actually matters and is unaffected. Set `settings.json`'s `Language` to `"es"` and
`FirstRunCompleted` to `false` to force the wizard to show, launched with `--mock`, and walked all
7 steps end to end via Windows-MCP, confirming at each step (via snapshot + screenshot) that every
static label was correctly Spanish: step 1 "Bienvenido"/welcome paragraph, step 2 "Comprobación
del sistema" (with the dynamic `SystemChecker` result lines still correctly Russian, as
documented), step 3 "Rutas de los programas y de la carpeta de modelos" with all three
path labels and three "Examinar..." buttons, step 4 "Dirección de recompensa", step 5 "Vista
previa de la asignación de modelos por GPU" (dynamic per-GPU explanation lines still Russian, as
documented), step 6 "Inicio automático" with all three checkbox labels and the footnote, and step
7 "Listo" with "Dirección: "/"Carpeta de modelos: " labels and the "Finalizar" button in place of
"Siguiente" - also confirming the window's own titlebar read "Keryx Node Manager — primer inicio"
throughout. Clicked "Finalizar": confirmed via `settings.json` directly that `FirstRunCompleted`
persisted `true`. The app then closed with no window ever appearing (`Get-Process` found no
process at all a couple seconds later) - relaunching immediately after confirmed a normal, clean
launch straight to the Dashboard (`MainWindowHandle` non-zero, `Responding=True`, "Resumen"/
"Nodo"/"Minero" correctly in Spanish, confirming the wizard→MainWindow hand-off itself is fine).
This reads as the same pre-existing intermittent "zombie process, no window" launch flake this
project's own notes have tracked since the fourth/sixth increments (and mostly fixed at its
startup-deadlock root cause in the seventh increment) recurring at a different transition point
(wizard-close → MainWindow-show) rather than a regression from this purely XAML-binding change -
flagged here honestly rather than silently dismissed, since the exact mechanism at *this*
transition point specifically hasn't been proven closed the way the `OnStartup` one was. Restored
`Language` to `"ru"` afterward directly in `settings.json` and killed the test process
(`FirstRunCompleted` was left at its correctly-earned `true` value, matching the machine's real
prior state).

116 total Core tests; all passing on real Windows (0 warnings/0 errors, 116/116) - unchanged, since
this increment is View-layer XAML/resource work only.

**This closes item 1's static-string-extraction pass**: every page in the app (Dashboard, GPU,
Models, Node, Miner, Logs, Profiles, About, Settings, the nav footer, and now the first-run
wizard) has its static View-layer labels wired to `DynamicResource`/`Strings.*.xaml` across all
five supported languages. What remains of item 1 is exactly the dynamic-string/Core-message gap
documented below, not any more static View markup.

## This session's twenty-second increment: fixed missing ScrollViewer on Settings/Profiles/About (new)

Closed item 8, a long-standing gap flagged since the fourth increment: `SettingsView.xaml`,
`ProfilesView.xaml`, and `AboutView.xaml` each had a bare `<StackPanel MaxWidth="700"
HorizontalAlignment="Left">` as their root with no `ScrollViewer`, so on a non-maximized window
content past the visible height (the Critical-threshold field and Save button on Settings, for
example) was genuinely unreachable - every earlier increment touching these pages had simply
maximized the window to work around it during verification rather than fixing the XAML itself.
Wrapped each page's root `StackPanel` in a `<ScrollViewer VerticalScrollBarVisibility="Auto">`,
matching the pattern `WizardWindow.xaml` already used successfully for its own (much longer)
7-step content.

Built (`dotnet build -c Release`, 0 warnings/0 errors) and re-ran tests (`dotnet test`, 116/116,
unchanged - View-layer only). **Live-verified the actual fix, not just that it compiles**: resized
the running window down to 900×560 via a direct `MoveWindow` P/Invoke call (small enough to
guarantee overflow) and opened Settings. Before scrolling, a Windows-MCP snapshot showed the "Save"
button and several fields reporting coordinates `(0,0)` - the accessibility-tree signature of an
element that exists but isn't currently rendered on screen, i.e. genuinely unreachable, confirming
the bug was real and not just a screenshot artifact. The snapshot's scrollable-elements list also
now reports a real scrollable region (`vertical_scrollable: true`) where none existed before this
fix. Scrolled it via the Windows-MCP `Scroll` tool and re-snapshotted: the "Save" button now
reported real, clickable coordinates and the scroll region read 100% scrolled - confirming the fix
actually restores reachability, not just that a scrollbar renders. Did not repeat the full
resize/scroll walkthrough for Profiles/About individually since they use the byte-identical
`ScrollViewer` wrapping pattern verified on Settings; noted here rather than silently implied as
separately tested.

116 total Core tests; all passing on real Windows (0 warnings/0 errors, 116/116) - unchanged, since
this increment is View-layer XAML markup only.

## This session's twenty-third increment: CUDA_VISIBLE_DEVICES wired at launch (new)

Closed item 5. `MinerArgumentBuilder.BuildCudaVisibleDevices` (a pure, already-unit-tested helper)
existed since the GPU→launch wiring increment but its output was never actually applied - the only
mechanism that told keryx-miner.exe which GPUs to skip was `--force-model`'s CSV list omitting the
disabled card's tier token, a purely positional convention that depends entirely on the miner's own
CLI parser getting the position-to-GPU mapping right. `DashboardViewModel.StartAllAsync` now also
computes the enabled CUDA indexes (via `devices.OrderBy(d => d.CudaIndex).Zip(gpuAssignments, ...)`
- reusing the exact same `GpuAssignmentResolver` output already driving `--force-model`, so the two
mechanisms can never disagree about which GPUs are excluded) and sets `CUDA_VISIBLE_DEVICES` on the
miner's `EnvironmentVariables` dictionary, merged with (not replacing) whatever the user already
set via `MiningProfile.EnvironmentVariables`. Only set when the GPU query actually succeeded
(`devices.Count > 0`) - if it failed, the existing fallback (let the miner auto-fit unassisted)
is left alone rather than fighting it with an empty/wrong value.

**Live-verified the actual computed value, not just that the app doesn't crash** - `MockRuntimeBackend`
never spawns a real process, so there's no OS environment block to inspect after the fact the way a
native launch would allow. Used the same honest, temporary-trace-then-remove technique already
established in this project (see the seventh increment's startup-deadlock diagnosis) rather than
skip verification or assume correctness: added a one-line `File.AppendAllText` trace of the computed
`CUDA_VISIBLE_DEVICES` value right after building it, rebuilt, launched with `--mock`, went to the
GPU page and set the RTX 3060 (CUDA index 0) to "Отключено", switched to Dashboard, clicked
"Запустить всё", then read the trace file: `CUDA_VISIBLE_DEVICES=1,2` - confirming index 0 was
correctly excluded and the other two real GPUs' indexes were included, in the correct order.
Confirmed via screenshot that the Dashboard showed both Node and Miner "Работает" with no crash
(including the Safety monitor's own background polling still running normally alongside the new
code path). Removed the temporary trace line, rebuilt (`dotnet build -c Release`, 0 warnings/0
errors) and re-ran tests (`dotnet test`, 116/116, unchanged - the new logic is App-layer glue code
around an already-tested Core helper, not new Core logic) to confirm the trace removal didn't
regress anything, and killed the test process.

116 total Core tests; all passing on real Windows (0 warnings/0 errors, 116/116) - unchanged.

## This session's twenty-fourth increment: Ukrainian (uk) localization (new)

Closed item 2. Added `Strings.uk.xaml` translating the full set of keys already extracted across
every page as of the twenty-first increment (Settings, nav footer, Dashboard, GPU, Models, Node,
Miner, Logs, Profiles, About, and the first-run wizard - the complete static-label set, not a
partial subset like the original es/it/fr increment which only covered the 21 keys that existed at
the time). Extended `LocalizationManager.SupportedLanguages` from 5 to 6 entries and added a
`Tag="uk" Content="Українська"` `ComboBoxItem` to the Settings page's language dropdown. No
`.csproj` changes needed (SDK-style project auto-globs new `.xaml` files under `Resources/`).

No new bug found - adding a sixth translation file exercises the same already-proven mechanism as
the fifth (Italian/French/Spanish increment), not a new code path.

Live-verified on real Windows: rebuilt (`dotnet build -c Release`, 0 warnings/0 errors) and re-ran
tests (`dotnet test`, 116/116, unchanged - presentation-only). Switched `settings.json`'s `Language`
to `"uk"`, launched with `--mock`, and confirmed via screenshot: Dashboard showed "Огляд" title,
"Нода"/"Майнер"/GPU cards, "3 активна(і) / 0 вимкнена(і)" GPU-count suffix, and
"Запустити все"/"Зупинити все"/"Оновити" buttons all correctly Ukrainian (dynamic
`NodeStatus`/`MinerStatus` values remained Russian, as documented/expected). Settings page showed
"Налаштування" title and every section header/checkbox/label in Ukrainian, confirmed the
already-fixed `ScrollViewer` (twenty-second increment) still worked correctly in this language by
scrolling to reveal the language `ComboBox` showing "Українська" selected and the "Зберегти" Save
button. About page showed the full Ukrainian description paragraph, "Посилання"/"Система" headers,
both GitHub link texts, the no-public-repo note, and "ОС:"/".NET:"/"64-розрядна ОС:" labels with
their live system values - no crash on the page with the highest concentration of `Run.Text`
bindings, same safety check applied in the twentieth (About) increment. Restored `Language` to
`"ru"` afterward directly in `settings.json` and killed the test process.

116 total Core tests; all passing on real Windows (0 warnings/0 errors, 116/116) - unchanged, since
this increment is Resources/View-layer work only.

## This session's twenty-fifth increment: Models page delete confirmation dialog (new)

Closed the first half of item 6. `ModelCardViewModel.Delete()` previously removed a model file (up
to several GB) from disk immediately on a single click, with no confirmation - a real usability
risk for an irreversible, potentially large deletion. Added a `MessageBox.Show(..., YesNo,
Warning)` confirmation naming the specific tier before deleting, reusing the exact same
`MessageBox`/`MessageBoxButton.YesNo` pattern `TrayIconService` already uses for its own
close-vs-minimize confirmation rather than introducing a second UI convention for the same kind of
decision.

Built (`dotnet build -c Release`, 0 warnings/0 errors) and re-ran tests (`dotnet test`, 116/116,
unchanged - View-layer only). **Live-verified both paths of the actual dialog, not just that it
appears**: pointed a test profile's `ModelsDirectory` at a throwaway folder, downloaded a real
small file via the same `https://www.google.com/favicon.ico` trick used in the original Models
increment (a real public HTTPS download, not a stub), then clicked "Удалить". Confirmed via
screenshot the dialog read exactly `Удалить модель «Qwen3-8B-abliterated»? Файл будет удалён с
диска безвозвратно.` with "Да"/"Нет" buttons. Clicked "Нет" first and confirmed via direct
filesystem check (`Test-Path`/`Get-ChildItem`, not just trusting the UI) that `model.gguf` was
still present - the cancel path genuinely blocks deletion rather than being cosmetic. Clicked
"Удалить" again and this time confirmed "Да", then confirmed via `Test-Path` that the file was
actually gone - the confirm path still performs the real deletion, unchanged in behavior from
before this increment. Cleaned up afterward: deleted the throwaway test folder and reset
`settings.json`'s `ModelsDirectory`/`GpuAssignments`/`ModelSources` back to their prior clean state
(this session's earlier CUDA_VISIBLE_DEVICES verification increment had left a test GPU assignment
in place, caught and reverted here too rather than left behind).

116 total Core tests; all passing on real Windows (0 warnings/0 errors, 116/116) - unchanged, since
this increment is View-layer/ViewModel glue code only.

## This session's twenty-sixth increment: Models page Resume label + disk usage summary (new)

Closed the second half of item 6. Two independent, small additions to the Models page, both
View/ViewModel-layer only:

**Distinct "Продолжить" label.** `ModelCardViewModel` previously had one `ShowDownloadButton` flag
(`!IsDownloading`), so a paused download's button still read "Скачать" even though `DownloadAsync`
was actually about to resume from the `.part` file via HTTP Range (already correct in behavior,
just unclear in wording - noted as a known gap in the twenty-fifth increment). Split into two
mutually-exclusive flags, `ShowDownloadButton` (`!IsDownloading && !HasPausedDownload`) and the new
`ShowResumeButton` (`!IsDownloading && HasPausedDownload`), recomputed via
`OnIsDownloadingChanged`/new `OnHasPausedDownloadChanged` partial-property hooks. `ModelsView.xaml`
gained a second `Button` bound to `ShowResumeButton`/`Str_Models_Resume`, calling the *same*
`DownloadCommand` as the original - no new command, no behavior change, only a clearer label. Added
`Str_Models_Resume` to all six `Strings.*.xaml` files (ru "Продолжить", en "Resume", es "Reanudar",
it "Riprendi", fr "Reprendre", uk "Продовжити").

**Aggregate disk-usage summary.** Added `ModelsViewModel.TotalDiskUsageText`, recomputed from the
real filesystem (`ModelFileLocator.IsInstalled`/`GetInstalledSizeBytes` per tier, summed - never
cached or estimated, matching this project's established "never trust a stale figure" convention)
on construction, on the existing Refresh command, and now also automatically whenever any card's
installed/paused state changes: `ModelCardViewModel` gained a `StateChanged` event fired at both
exit points of `RefreshState()`, and `ModelsViewModel`'s constructor subscribes every card's
`StateChanged` to `RefreshTotalDiskUsage()`. Deliberately renders as an *empty* string (collapsing
the `TextBlock` entirely via a new `StringToVisibilityConverter`) when no models directory is set
or nothing is installed yet, rather than showing a misleading "0 моделей" line to a first-run user
who hasn't configured anything. `ModelCardViewModel.FormatSize` changed from `private` to
`internal` so the page-level ViewModel can reuse the exact same size-formatting rule instead of a
second, independently-drifting copy.

Built (`dotnet build -c Release`, 0 warnings/0 errors) and re-ran tests (`dotnet test`, 116/116,
unchanged - this increment touches no Core-layer logic). **Live-verified both behaviors on the real
Windows machine, not just that the code compiles**: pointed the active profile's `ModelsDirectory`
at `C:\KeryxData\models` (already configured), manually created a `.part` file for the
Mistral-7B-v0.3 tier to simulate a paused download and confirmed via screenshot the button read
"Продолжить" (not "Скачать") once Refresh was clicked; separately created a real `model.gguf` file
for the Qwen3-8B-abliterated tier to simulate an installed model and confirmed via screenshot the
header now showed "Установлено моделей: 1, занято на диске: 10 МБ." exactly matching the file's
real size. Cleaned up both throwaway test files afterward and confirmed via `Test-Path` they were
actually gone; `settings.json` was not otherwise modified (its `ModelsDirectory` was already set to
that path from an earlier session, unrelated to this increment's test files).

116 total Core tests; all passing on real Windows (0 warnings/0 errors, 116/116) - unchanged, since
this increment is View/ViewModel-layer only.

## This session's twenty-seventh increment: Profiles page per-profile quick-glance info (new)

Closed item 9. The Profiles page's `ListBox` previously bound directly to `ProfileStore.ProfileNames`
(a `List<string>`), so distinguishing profiles beyond their name meant switching to each one and
checking the Miner/GPU pages. Added `ProfileStore.Profiles` (a live `IReadOnlyList<MiningProfile>`
projection, same "never a stale cached snapshot" rule as the existing `ProfileNames`) so
`ProfilesViewModel` can read more than just the name. Introduced a small `ProfileRow(string Name,
string Summary)` record; `ProfilesViewModel.ProfileRows` now replaces `ProfileNames` as the
`ListBox`'s `ItemsSource`, with each row's `Summary` built from the profile's mining address (masked
via `SecretMasker.MaskAddress` - the exact same helper `DiagnosticsExporter` already uses to redact
addresses in diagnostic ZIP exports, not a new masking rule) and its `GpuAssignments.Count` (shown as
"GPU: авто" when empty, "GPU назначено: N" otherwise). `ProfilesView.xaml`'s `ListBox` gained a
two-line `ItemTemplate` (name + summary) and switched from `SelectedItem` to
`SelectedValuePath="Name"`/`SelectedValue` binding so `ProfilesViewModel.SelectedProfileName` stays
a plain string, unchanged for the existing Switch/Rename/Delete commands.

Built (`dotnet build -c Release`, 0 warnings/0 errors after killing a stale running instance that
was locking `KeryxNodeManager.Core.dll` mid-build - not a code issue) and re-ran tests (`dotnet
test`, 116/116, unchanged - no Core-layer logic changed, `ProfileStore.Profiles` is a pure
projection over the same `_settings.Profiles` list `ProfileNames` already read). **Live-verified on
the real Windows machine**: confirmed the existing "Default" profile's row showed its real masked
address (`keryx:qrxpcu…uhte`) and "GPU: авто"; created a second profile ("TestRig2") via the page's
own "Создать" flow and confirmed via screenshot it appeared as a second row reading "адрес не
задан · GPU: авто" (no address configured yet - the masking/fallback logic correctly distinguishes
"empty" from "present but short"), with `SelectedValue` correctly following the newly-created
profile per `CreateAsync`'s existing behavior. Switched back to Default via "Сделать активным",
selected TestRig2 and deleted it via "Удалить", then confirmed via `Get-Content ... | ConvertFrom-
Json` that `settings.json` was back to exactly one profile ("Default") - the throwaway test profile
was not left behind.

116 total Core tests; all passing on real Windows (0 warnings/0 errors, 116/116) - unchanged, since
this increment is View/ViewModel-layer only (plus one new pure-projection property on
`ProfileStore`).

## This session's twenty-eighth increment: wizard "create a new profile" option (new)

Closed item 10. Previously a returning user wanting to run the wizard again for a second rig had no
in-wizard path to a fresh profile - the wizard always configured whatever `ProfileStore.ActiveProfile`
already was, so they'd have to visit the Profiles page, create+switch to a new profile there, then
reopen the wizard separately. Added a small section to the wizard's step 0 (Welcome): a label showing
which profile the wizard is currently configuring (`Profile.Name`), an explanatory hint, a text field,
and a "Создать и настроить" button wired to a new `WizardViewModel.CreateNewProfileCommand`. That
command calls the exact same `ProfileStore.CreateProfileAsync` the Profiles page uses (no parallel
profile-creation code path), then re-seeds the wizard's own `NodeExecutablePathInput`/
`MinerExecutablePathInput`/`ModelsDirectoryInput` mirror properties from the freshly-active profile and
clears any stale `DirectoriesError`/`AddressValidationMessage`. Deliberately only offered on step 0,
before any wizard step has been filled in - creating a profile after step 3 (address) or step 2
(directories) had already been edited for the *old* profile would silently discard that in-progress
input, which would be a surprising, unannounced data loss; step 0 has nothing yet to lose. Added
`Str_Wizard_Step0_ActiveProfileLabel`/`Str_Wizard_Step0_NewProfileHint`/`Str_Wizard_Step0_CreateProfile`
to all six `Strings.*.xaml` files (the fourth new key, `Str_Wizard_Step0_NewProfileNamePlaceholder`,
was added for a possible watermark but ended up unused - the plain `TextBox` has no watermark support
without extra custom-control work, left as a harmless unused resource rather than adding scope here).

Built (`dotnet build -c Release`, 0 warnings/0 errors) and re-ran tests (`dotnet test`, 116/116,
unchanged - View/ViewModel-layer only). **Live-verified the actual create+reseed behavior on the real
Windows machine, not just that the button appears**: temporarily flipped `settings.json`'s
`FirstRunCompleted` to `false` to reopen the wizard (restored afterward), typed "RigB" into the new
field and clicked "Создать и настроить" - confirmed via screenshot the label updated to "Мастер
настраивает профиль: RigB" and the status line read "Профиль «RigB» создан и стал активным...".
Advanced to step 2 (Directories) and confirmed via screenshot all three path fields were genuinely
blank (RigB's own blank defaults), not carried over from Default's already-configured
`C:\KeryxData\models` - proving the re-seed logic runs, not just that a new profile got created
underneath an unchanged wizard state. Clicked Skip to close the wizard.

**Real mistake made and caught during cleanup, worth recording**: `ConfigStore.SaveAtomicAsync`
(documented in its own doc comment) keeps its own `settings.json.bak` as part of its atomic-write
scheme. This session's manual cleanup step naively used the *same* `settings.json.bak` filename for
a "let me snapshot the pristine file before testing" backup - every real save the app made during the
wizard test (profile creation, then Skip/Finish) silently overwrote that manual backup with the app's
own pre-save snapshot, so restoring from it did not actually restore the pristine pre-test state
(it restored an intermediate mid-test state with `RigB` still present and `FirstRunCompleted: false`).
Caught immediately by re-reading the restored file rather than assuming the copy succeeded - fixed by
hand-editing the live JSON back to exactly one `Default` profile with `ActiveProfileName: "Default"`
and `FirstRunCompleted: true`, then confirmed via a real app relaunch that it opened straight to
`MainWindow` with no wizard, matching the pre-test state. Lesson for any future manual settings.json
backup: use a filename `ConfigStore` doesn't already claim (e.g. `settings.json.manual-bak`), not
`.bak`.

116 total Core tests; all passing on real Windows (0 warnings/0 errors, 116/116) - unchanged, since
this increment is View/ViewModel-layer only.

## This session's twenty-ninth increment: themed ComboBox control template (new)

Closed item 3, the last remaining "In progress" item from this session's backlog pass. Added a
full custom `ComboBox`/`ComboBoxItem` control template pair to `DarkTheme.xaml` (flat surfaces, one
accent color, matching `CardStyle`/`PrimaryButtonStyle`'s existing pattern - no gradients, per the
brief's "no acid gamer aesthetic" instruction), applied app-wide via bare `TargetType` (not
`x:Key`) rather than requiring every page to opt in, so every `ComboBox` in the app - Settings'
language picker, GPU page's per-card mode picker - gets it automatically and no future page can
regress back to the invisible-text bug (twelfth increment) by adding a new `ComboBox` without
knowing to style it. Kept close to the framework's own default template structure (a
`ToggleButton`-driven `Popup` with an `ItemsPresenter`, non-editable path only) specifically to
avoid regressing keyboard nav/popup placement - the exact risk this item had been left unattempted
for across several earlier increments.

**A real, non-obvious bug was found and fixed during live verification, not just a cosmetic
check.** The standard WPF pattern for a themed ComboBox's closed selection box - binding
`ContentPresenter.Content`/`ContentTemplate`/`ContentStringFormat` to the framework's own
`SelectionBoxItem`/`SelectionBoxItemTemplate`/`SelectionBoxItemStringFormat` TemplateBindings (this
is literally what Microsoft's own default Aero/Fluent ComboBox templates do) - does not correctly
project `DisplayMemberPath` through a *custom* template in practice: GpuView's per-card mode
picker (`ItemsSource` of `GpuModeOption` records + `DisplayMemberPath="Label"`) rendered the raw
record's `ToString()` in its closed box (`"GpuModeOption { Value = auto, Label = ... }"`) instead
of the intended `"Авто"`/`"Вручную: ..."` text - caught by actually looking at a screenshot of the
GPU page after the initial build, not just the Settings page's language picker (whose `ComboBox`
items are literal `<ComboBoxItem Content="..."/>` elements, a different code path that happened to
work correctly with the naive TemplateBinding approach and would have hidden this bug if that were
the only page checked). Root-caused to `SelectedItem` for a literal-`ComboBoxItem` list being the
`ComboBoxItem` container itself (whose real text is its `Content` property) versus `SelectedItem`
for a data-bound list being the raw bound object with no automatic `DisplayMemberPath` projection
through this custom template's `ContentPresenter`. Fixed by adding
`Converters/ComboBoxSelectionDisplayConverter.cs`, an `IMultiValueConverter` that takes
`(SelectedItem, DisplayMemberPath)` and: returns `ComboBoxItem.Content` if the item is a
`ComboBoxItem`; otherwise reads the named property via reflection if `DisplayMemberPath` is set;
otherwise returns the item itself - covering both usage patterns in this app with one converter
rather than two divergent templates.

Built (`dotnet build -c Release`, 0 warnings/0 errors) and re-ran tests (`dotnet test`, 116/116,
unchanged - View-layer only). **Live-verified thoroughly on the real Windows machine, including the
specific risks this item was previously deferred over**: resized the app window via a P/Invoke
`MoveWindow` call (same technique as the twenty-second increment's ScrollViewer verification) to
see both the Settings and GPU pages without scrolling blind. Confirmed the Settings language
`ComboBox`'s closed state now reads "Русский" in light text on a dark background (previously
invisible near-white-on-white, per the twelfth increment); opened its dropdown and confirmed the
list itself renders correctly with the current selection highlighted in the accent color; pressed
the Down arrow key and confirmed keyboard focus moved to "English" (via `Snapshot`'s
`has_focused` field, not just visual guessing), then pressed Enter and confirmed the popup closed,
the ComboBox committed to "English", and the *entire app actually switched to English* (proving
`SelectedValue`'s two-way binding and the whole language-switch mechanism still work through the new
template) - switched back to Russian afterward via the same click-and-select flow. Separately found
and fixed the `GpuModeOption` bug described above on the GPU page, rebuilt, and reconfirmed both
cards now show "Авто" legibly with a working dropdown listing "Отключено" and each "Вручную: <tier
name>" option; pressed Escape to close the dropdown without changing the selection and confirmed
via screenshot the setting was left unchanged (no accidental GPU assignment write). Also updated a
now-stale comment in `SettingsView.xaml` (left over from the twelfth increment) that had explicitly
instructed against overriding `Background`/`Foreground` on that `ComboBox` - no longer accurate now
that a real themed template exists.

116 total Core tests; all passing on real Windows (0 warnings/0 errors, 116/116) - unchanged, since
this increment is View-layer only (one new converter, no Core-layer logic touched).

## This session's thirtieth increment: wizard Finish-step invalid-address highlight (new)

Closed the address-highlight portion of item 7 (the "Wizard polish" item), picked up after
"продолжай" as a well-scoped continuation once the original nine-item backlog closed. Step 3
already warns if the mining address doesn't look valid but still lets the user proceed (matching
the Miner page's own leniency, per `KeryxAddressValidator.LooksValid`) - so a user who dismissed
that warning and clicked through steps 4-5 could reach the Finish step with no visual reminder the
address was still off, just the raw value echoed back identically to a valid one. Added
`WizardViewModel.IsAddressValid` (`[ObservableProperty]`, default `true`), recomputed only on
entering step 6 via the existing `OnCurrentStepIndexChanged` hook (`Profile` is a bare data class
with no `INotifyPropertyChanged`, so there's no cheaper per-keystroke hook to recompute this from,
and step 6 is the only place it's displayed - recomputing once on step entry is sufficient and
matches how step 3's own validation message already works). Reused
`KeryxAddressValidator.LooksValid` rather than duplicating validation logic. Wired an inline
`DataTrigger` on the Finish step's address `TextBlock` (`Foreground`/`FontWeight` swap to
`WarningBrush`/`SemiBold` when `IsAddressValid` is `false`), plus a new warning line below it shown
via a new `Converters/InverseBooleanToVisibilityConverter.cs` (the app already had
`BooleanToVisibilityConverter` for the direct case but needed the inverse for "only show this
warning when NOT valid"). Added `Str_Wizard_Step6_AddressWarning` to all six locale dictionaries
(ru/en/es/it/fr/uk).

Built (`dotnet build -c Release`, 0 warnings/0 errors) and re-ran tests (`dotnet test`, 116/116,
unchanged - this increment touches no Core-layer logic, `KeryxAddressValidator` itself is
unmodified). **Live-verified on the real Windows machine, including a real invalid-address pass,
not just a code read.** Set `FirstRunCompleted` to `false` in the live `settings.json` to reopen
the first-run wizard, relaunched, stepped through 0→3, overwrote the real mining address with the
deliberately-invalid `"not-a-valid-address"`, continued through to step 6, and confirmed via
screenshot the address rendered in bold amber (`WarningBrush`) with the new warning line beneath it
reading (Russian UI): "Адрес не похож на действительный Keryx-адрес - майнинг с этим адресом будет
отклонён узлом. Вернитесь к шагу «Адрес для начисления вознаграждения», чтобы исправить." -
confirming the highlight and warning both render exactly as intended. Clicked "Пропустить" (Skip)
to close the session - per `WizardViewModel`'s own documented behavior, Skip and Finish both call
the same "save what's here" path, never a silent discard, so this was expected to (and did)
persist the test invalid address into the real `Default` profile. Read the live `settings.json`
back afterward rather than assuming: confirmed `FirstRunCompleted` was `true` again (Skip/Finish
sets this automatically) but `MiningAddress` was indeed now `"not-a-valid-address"` - hand-restored
it to the real address
(`keryx:qrxpcusyrxjxghfdumcxm2rqw4dhe3n9hyqpvgn2wfyldltf99w2xhnajuhte`, confirmed present earlier
this session) via direct JSON edit, then relaunched the app once more and confirmed it opened
straight to `MainWindow`'s Dashboard with no wizard, and that the Miner page displayed the restored
real address - clean state confirmed, not assumed.

116 total Core tests; all passing on real Windows (0 warnings/0 errors, 116/116) - unchanged, since
this increment is View/ViewModel-layer only (one new `[ObservableProperty]`, one new converter, no
Core-layer logic touched).

## This session's thirty-first increment: tray icon state colors (new)

Closed item 4 (brief §10: grey/yellow/green/red/blue tray icon per state). `TrayIconService`
already had a `TrayState` enum and a `SetState` method, but it only ever updated the tooltip text -
`SetState(TrayState.Stopped)` was the only call anywhere in the codebase, so the icon itself never
changed and nothing beyond the constructor's initial state was ever observed. Generated five
`.ico` variants (`tray-stopped/starting/running/error/inference.ico`, each with 16/20/24/32/48px
frames matching the original `tray.ico`) via a Python/Pillow script: rather than recoloring the
whole glyph (which risks becoming illegible for what is a detailed multi-tone logo, not a flat
icon), each variant re-renders the same base glyph at every size and overlays a colored circular
badge in the bottom-right quadrant with a thin dark outline for contrast against both light and
dark taskbars - the Keryx mark stays recognizable and only the badge color changes, matching how
Windows' own accent-color notification badges work. Added all five as `<Resource>` items in the
.csproj and wired `TrayIconService` to cache one `BitmapImage` per `TrayState` and swap
`_icon.IconSource` in `SetState` (previously only `ToolTipText` was updated there).

**Also wired real state transitions, not just the icon-swap plumbing** - an icon-per-state feature
that's never driven by anything beyond the constructor's `Stopped` default would be dead code
matching the same "orphaned enum" gap the original `SetState` had. `App.xaml.cs` now resolves the
already-DI-registered `DashboardViewModel` singleton after creating `_tray` and computes
`TrayState` from what it already exposes: `TrayState.Running` when `NodeStatus`/`MinerStatus`
report "Работает" (set from `ProcessSupervisor.EventRaised`, the same signal the Dashboard page
itself displays), `TrayState.Starting` while `StartAllCommand.IsRunning` is true (CommunityToolkit's
generated `AsyncRelayCommand` property, true for the span between clicking "Запустить всё" and the
launch attempt finishing), else `TrayState.Stopped`. Deliberately did **not** invent detection for
`TrayState.Error`/`TrayState.InferenceActive`: there is no Core-layer "last launch failed" or
"inference in progress" signal anywhere in this codebase today, and guessing one now (e.g.
treating any `LastActionMessage` write as an error) would misclassify normal status updates as
errors. `SetState(Error)`/`SetState(InferenceActive)` and their icons work correctly if called -
the icon assets and switch statement are complete - but nothing calls them yet, and that gap is
recorded here rather than papered over with a fake heuristic.

Built (`dotnet build -c Release`, 0 warnings/0 errors) and re-ran tests (`dotnet test`, 116/116,
unchanged - no Core-layer logic touched, this is App-layer wiring plus static image assets).
**Live-verified the actual badge color on the real Windows machine, not just the code path.**
Launched normally (no `--mock`): confirmed via `Get-Process`/window title the app opened cleanly,
then opened the notification area's overflow flyout and captured a cropped, upscaled screenshot
showing the Keryx tray icon with a grey badge (`Stopped`, matching `NodeStatus`/`MinerStatus`
both being "Остановлен(а)" with nothing running). Relaunched with `--mock`, clicked "Запустить
всё" on the Dashboard, confirmed both `NodeStatus`/`MinerStatus` flipped to "Работает" in the UI,
then recaptured the same cropped flyout screenshot and confirmed the badge had switched to green -
proving the wiring actually reacts to real ViewModel state changes, not just the one state set at
construction. Stopped the mock run, killed the process, and confirmed via a direct read of the
live `settings.json` that starting/stopping in `--mock` mode left the real profile (mining address,
`FirstRunCompleted`, executable paths) completely unchanged, since `ProcessSupervisor` state is
runtime-only and never persisted.

116 total Core tests; all passing on real Windows (0 warnings/0 errors, 116/116) - unchanged, since
this increment touches no Core-layer logic (five new static `.ico` assets, one App-layer service
change, one `App.xaml.cs` wiring addition).

## This session's thirty-second increment: Dashboard "go to settings" nudge on missing config (new)

Closed a "Known issues" gap picked up after "продолжай": `StartAllAsync`'s two config-validation
early returns (missing `NodeExecutablePath` or `MiningAddress` when not using the mock backend)
only ever set `LastActionMessage` - correct information, but no way to act on it from the Dashboard
itself; the user had to remember which nav item to click. Added
`DashboardViewModel.MissingConfigTarget` (`[ObservableProperty]`, `"Node"`/`"Miner"`/`null`), set
alongside `LastActionMessage` in each validation branch and reset to `null` at the top of every
`StartAllAsync` call so it never lingers past the specific failure it was set for. Added a
`GoToMissingConfigCommand` that raises a new `public event Action<string>? NavigationRequested`.
`DashboardViewModel` deliberately has no reference to `MainViewModel`/`MainWindow` - a page
shouldn't own the nav shell - so `MainWindow.xaml.cs` (which already holds both) subscribes to this
event in its constructor and sets `_viewModel.SelectedPage = page`; since `NavList.SelectedItem` is
already `TwoWay`-bound to `SelectedPage`, this fires the exact same `NavList_SelectionChanged` path
a real click would, no separate page-switch logic needed. `DashboardView.xaml` gained a
"Перейти к настройкам" button reusing the already-existing `StringToVisibilityConverter` (built for
the twenty-sixth increment's disk-usage summary) so it only shows when `MissingConfigTarget` is
set. Added `Str_Dashboard_GoToSettings` to all six locale files (ru/en/es/it/fr/uk).

Built (`dotnet build -c Release`, 0 warnings/0 errors) and re-ran tests (`dotnet test`, 116/116,
unchanged - no Core-layer logic touched). **Live-verified the actual click-through on the real
Windows machine**, not just that the button renders: launched normally (no `--mock`, so the real,
still-empty `NodeExecutablePath` triggers the first validation branch), clicked "Запустить всё",
confirmed via screenshot the error text and the new "Перейти к настройкам" button both appeared,
clicked the button, and confirmed via a second screenshot the app actually switched to the "Нода"
(Node) page with that nav item highlighted - proving the event wiring works end-to-end, not just
that `NavigationRequested` compiles. Killed the test process afterward; no settings were touched by
this pass (`MissingConfigTarget` is pure in-memory UI state, never persisted).

116 total Core tests; all passing on real Windows (0 warnings/0 errors, 116/116) - unchanged, since
this increment is View/ViewModel-layer only (one new observable property, one new event, one new
command, no Core-layer logic touched).

## This session's thirty-third increment: tray menu Start/Stop All + logs + real stop-on-exit (new)

Fixed a real, user-facing gap found while scanning for leftover `TODO`s: `TrayIconService`'s
"Запустить всё"/"Остановить всё" context-menu items were still literal no-op lambdas carrying a
stale comment ("wired once a MiningProfile exists") from before `ProfileStore`/
`DashboardViewModel`'s commands became real - that condition has been true since the GPU-wiring
session, this was simply never revisited. Worse, `ConfirmExit`'s "Да" branch (the option whose
dialog text explicitly promises "остановить всё и закрыть приложение") had a literal
`// TODO once wired: await ProcessSupervisor.StopAsync for both node and miner` and did the exact
same thing as "Нет" - force-close with no stop at all, silently breaking the promise the dialog
itself made.

Gave `TrayIconService` references to `MainViewModel` and `DashboardViewModel` (both already
DI-registered singletons, resolved in `App.xaml.cs` before constructing the tray service instead
of after) rather than reinventing separate delegates for each action - the tray should mean
exactly what the Dashboard page means by "start"/"stop", not maintain a parallel notion of it.
Wired: "Запустить всё"/"Остановить всё" call `DashboardViewModel.StartAllCommand`/
`StopAllCommand.Execute(null)` directly; "Открыть логи" and "Настройки" now set
`MainViewModel.SelectedPage` (reusing the exact same `NavList.SelectedItem` `TwoWay`-binding path
the thirty-second increment's Dashboard nudge button uses) before restoring the window, so they
actually land on the intended page instead of just restoring whatever page was last visible;
`ConfirmExit` is now `async void` and awaits `StopAllCommand.ExecuteAsync(null)` before force-closing
on "Да", so the app can no longer exit "mid-promise." Left "Перезапустить майнер"/"Перезапустить
ноду" as an explicit, documented gap rather than papering over it with a "stop everything, start
everything" fake restart under a per-process label: no Core-layer primitive exists yet for
restarting one process alone (each `Start*Async` needs a freshly-built `LaunchSpec` - arguments, GPU
assignment resolution, env vars - that today only `DashboardViewModel.StartAllAsync` knows how to
assemble per-process), and guessing at that plumbing now would be genuine scope creep, not a
quick wire-up like the other three items.

Built (`dotnet build -c Release`, 0 warnings/0 errors) and re-ran tests (`dotnet test`, 116/116,
unchanged - no Core-layer logic touched). **Live-verified all three fixes on the real Windows
machine with the mock backend**, not just that the menu items compile: right-clicked the tray icon,
confirmed the full context menu renders; clicked "Запустить всё" and confirmed via screenshot both
`NodeStatus`/`MinerStatus` flipped to "Работает" on the Dashboard - proving the tray command
actually reached the same `DashboardViewModel` instance; clicked "Открыть логи" and confirmed the
app actually switched to and displayed the Logs page (live mock node/miner output visible), not
just restored the window; right-clicked again and picked "Выйти" → "Да", and confirmed via
`Get-Process` that the process exited cleanly afterward with no hang - proving `ExecuteAsync` was
correctly awaited rather than fired-and-forgotten into a deadlock.

116 total Core tests; all passing on real Windows (0 warnings/0 errors, 116/116) - unchanged, since
this increment is App-layer wiring only (two new constructor parameters, three menu-item lambdas
rewired, `ConfirmExit` made async - no Core-layer logic touched).

## This session's thirty-fourth increment: WslRuntimeBackend.StopAsync verified against real WSL (new)

Closed a genuinely open "Known issues" item rather than another code-and-verify pass: this machine
turned out to have real WSL installed and running (`wsl -l -v` showed `Ubuntu-22.04`, the exact
distro `WslRuntimeBackend`'s default constructor targets, already `Running`) - this session's
prior passes had only exercised the native/mock backends. `WslRuntimeBackend.StopAsync`'s doc
comment claimed that killing the `wsl.exe` wrapper process (`Process.Kill(entireProcessTree: true)`)
tears down the Linux-side command too, but explicitly flagged that claim as unverified.

**Method**: replicated `StartAsync`'s exact `ProcessStartInfo` shape (`UseShellExecute=false`,
`CreateNoWindow=true`, `RedirectStandardOutput/Error=true`) to launch `wsl.exe -d Ubuntu-22.04 --
sleep 120`, captured the Windows-side `wsl.exe` PID, and independently confirmed via `wsl -d
Ubuntu-22.04 -- ps aux` that a real Linux `sleep 120` process was running with its own PID - two
independent confirmations the command genuinely launched, not just that the wrapper started.
Then ran `taskkill /PID <wrapper> /T /F` (the OS-level equivalent of `Process.Kill(entireProcessTree:
true)`) and re-ran the same `ps aux` check: the Linux-side `sleep` process was gone. **The claim in
the doc comment holds** - confirmed with a real kill, not inferred from documentation or WSL's
general reputation.

**A real trap was found and ruled out before trusting this result, worth recording since it could
mislead a future verification pass in a similar environment**: launching `wsl.exe` as a direct
child process from *within this session's own automation-driven shell* returned in ~150ms with
exit code 0 for a `sleep 5`/`sleep 30` command that should take 5-30 seconds - looking exactly like
a bug where the wrapper detaches instantly without the command ever running. Bisected by launching
the identical `ProcessStartInfo` from a properly detached process instead (`Start-Process` to a
fresh top-level `powershell.exe`, decoupled from the automation shell's own console/handles) - that
version blocked for the correct duration and returned the correct exit code. So the instant-return
behavior was a console/handle-inheritance quirk specific to nesting `wsl.exe` under this particular
automation host's own PowerShell session, not a bug in `wsl.exe`, .NET's `Process` class, or this
app's code - a real, top-level-launched process (which is exactly how `KeryxNodeManager.exe` itself
always runs, WinExe with no console) does not hit this. Documented this caveat directly in
`WslRuntimeBackend.StopAsync`'s comment so a future session re-testing this from a similar
automation context isn't misled into "fixing" a bug that only exists in the test harness.

No code changes this increment - this was pure verification plus a doc-comment update reflecting
the confirmed result (`WslRuntimeBackend.cs`). Build/tests not re-run since no source logic
changed; 116/116 still holds unchanged from the prior increment.

## This session's thirty-fifth increment: Task Scheduler access-denied root-caused, AV guess retracted (new)

Closed the last remaining investigable "Known issues" item: `TaskSchedulerAutostart.RegisterAsync`
failing with "Access is denied" had only ever been guessed at (Windows Defender/EDR treating a new
autorun Task Scheduler entry as a persistence-technique red flag) and explicitly flagged as
unconfirmed. Checked Defender's own event log first (`Get-MpThreatDetection`,
`Microsoft-Windows-Windows Defender/Operational`, ASR block event IDs 1116/1117/1121) - zero
matches, no block or detection event exists anywhere near any `schtasks`/`Register-ScheduledTask`
attempt, in either direction (this session's or earlier ones). That alone was enough to cast real
doubt on the AV hypothesis, so pushed further instead of leaving it as "not yet confirmed" again.

**Method**: reproduced the failure fresh (`schtasks /Create ... /RL LIMITED /F` → "ERROR: Access is
denied", confirmed via `cmd /c` with `2>&1` captured directly rather than trusting a bare exit
code). Checked `whoami`/group membership: the current user is a genuine member of the local
Administrators group (`net localgroup Администраторы` lists it), but the *process token this
session actually runs under* does not include `BUILTIN\Administrators` in its active groups - the
classic UAC "filtered admin token" running at Medium integrity. Tested the hypothesis directly:
ran the identical `schtasks /Create` command from a UAC-elevated PowerShell (`Start-Process -Verb
RunAs`) - **it succeeded immediately** ("УСПЕХ. Запланированная задача... была успешно создана."),
with the exact same arguments that fail every time from the non-elevated session. That is a
controlled, repeatable A/B result, not a guess: elevation is the actual differentiator, antivirus
was never involved.

This contradicts the class's own long-standing doc comment ("a per-user ONLOGON task should not
need elevation") - that's Microsoft's documented general behavior, but this specific Windows
install's Task Scheduler/UAC policy evidently denies `/Create` for a filtered (non-elevated) token
regardless. Updated `TaskSchedulerAutostart.RegisterAsync`'s doc comment and the user-facing error
hint text to point at "try running as Administrator" instead of "check your antivirus" - the old
hint was actively pointing users at the wrong fix. Cleaned up the throwaway test tasks
(`KeryxTestAutostartRepro`, `KeryxTestElevated`, etc.) created during this investigation; one
elevated-session cleanup attempt got stuck on a UAC consent prompt this automation session can't
click through (the secure desktop isn't screenshot- or click-able from here) - a leftover
`KeryxTestElevated` scheduled task (harmless, points at `notepad.exe`, never fires since it's an
ONLOGON trigger) and a hung `consent.exe` process remain and should be cleared manually on the real
desktop, noted here rather than left silently unmentioned.

Built (`dotnet build -c Release`, 0 warnings/0 errors) and re-ran tests (`dotnet test`, 116/116,
unchanged - this increment only touched comments and one user-facing string, no logic). No new
live-verification of the register flow itself was needed beyond the A/B test above, since that test
*is* the live verification this item was waiting on.

116 total Core tests; all passing on real Windows (0 warnings/0 errors, 116/116) - unchanged, since
this increment is comment/string-only (no branching logic changed, `looksLikeAccessDenied`'s
condition is untouched).

## This session's thirty-sixth increment: Core-layer localization via `CoreStrings` (new)

Resolved the design question the thirty-fifth increment's "In progress" item 1 had left open (give
`Core` its own resource-lookup abstraction, or accept Core stays Russian-only): user explicitly
chose **give Core its own resource abstraction**, so every remaining Russian literal in
`SystemChecker`, `ProcessSupervisor`, `TierAssigner`, `TaskSchedulerAutostart`, `PathValidator`,
`SafetyMonitor`, `ProfileStore`, `ModelDownloadException`, `NvidiaSmiGpuInfoProvider`, and
`NativeWindowsRuntimeBackend` is now localized too, matching the six languages (ru/en/es/it/fr/uk)
the App layer already supports.

**Design**: new `KeryxNodeManager.Core/Localization/CoreStrings.cs` - a plain static class with a
nested `Dictionary<language, Dictionary<key, text>>`, a settable `Language` property (default
`"ru"`), and `Get(key)`/`Format(key, args)` lookups that fall back to Russian and then to the bare
key itself rather than ever throwing. Deliberately *not* `.resx`/`ResourceManager`: this project's
build is plain `dotnet build` with no Visual Studio designer pass, and `.resx` codegen is fragile
without one; a plain dictionary has none of that risk, is trivially unit-testable, and - critically
- keeps zero WPF/resource-assembly dependency in `Core`, which must stay buildable/testable
cross-platform (`net8.0`, not `net8.0-windows` - see `Core.csproj`). ~35 keys covering every message
across the ten target files were populated for all six languages. One literal was deliberately left
untouched: `TaskSchedulerAutostart.RegisterAsync`'s `stderr.Contains("отказано", ...)` check is a
locale-detection heuristic against `schtasks.exe`'s own OEM console output, not a user-facing
string - translating it would break the detection, not the UI.

**Wiring**: `App`'s `LocalizationManager.Apply(languageCode)` - the single method already called
once at startup and again on every language switch (`SettingsViewModel`'s language ComboBox) - now
also sets `CoreStrings.Language = normalized` right after swapping the WPF resource dictionary, so
both the App-layer XAML strings and the Core-layer exception/status strings move together with one
call, with no separate "language changed" event needed on the Core side.

**New tests**: `CoreStringsTests.cs` (5 tests) - covers the Russian default, live language
switching, unknown-key-returns-key-not-throw behavior, `Format`'s argument substitution, and (the
most valuable one) a parametrized test asserting every one of the ~35 keys resolves to a genuinely
different string in each of en/es/it/fr/uk versus the Russian baseline, so a future added key that's
only translated for some languages fails loudly at test time instead of silently falling back to
Russian in production. This test caught a real bug on its first run: the `uk` dictionary's
`Process.Restarted` entry was still the verbatim Russian text ("Перезапущено (PID {0}).") - fixed to
"Перезапуск виконано (PID {0})." (both fixes not shown to differ by feel, this was compared
Ordinal-string-equal, not eyeballed).

**A second, unrelated bug the new test exposed**: `CoreStringsTests` mutates the process-global
`CoreStrings.Language` static, and xUnit parallelizes different test classes across threads by
default. `SystemCheckerTests` - which never touches `CoreStrings.Language` itself but calls
`SystemChecker` methods that read it implicitly - failed non-deterministically with a Ukrainian
string where a Russian one was expected, because `CoreStringsTests` had set `Language = "uk"` on
another thread mid-run. Reproduced this failure directly (not assumed from a flaky-sounding
description), then fixed it the simple way rather than defensively touching every existing test:
added `tests/KeryxNodeManager.Core.Tests/TestAssemblyConfig.cs` with
`[assembly: CollectionBehavior(DisableTestParallelization = true)]`. The whole suite is still fast
(576ms sequential vs. 225ms parallel for 125 tests), so the tradeoff is a non-issue here.

**Build/test**: `dotnet build -c Release` → 0 warnings / 0 errors (all three projects, including
`KeryxNodeManager.App`, which references `Core` normally). `dotnet test` → 125/125 passing (116
pre-existing + 5 new `CoreStringsTests`, plus the `Process.Restarted` and test-parallelism fixes
above were both caught and closed before this count was reached - the very first run after adding
the test was 123/125 with 2 real failures).

**Live verification on the real Windows machine**: set `settings.json`'s `Language` to `"en"`
directly (faster and more reliable than driving the Settings page's ComboBox through this
automation session's UI-tree/coordinate quirks - see the recurring `(0,0)`-coordinate note in
"Known issues" below), relaunched the app, and confirmed the Dashboard/nav rendered in English
(`Start All`/`Stop All`/`Refresh`/`Dashboard`/`GPU`/`Models`/... instead of the Russian labels
active before this session). Then navigated to the Profiles page, typed `Default` (the existing
profile's name) into the "New profile" name field, and clicked Create - this calls
`ProfileStore.CreateProfileAsync`, which throws `InvalidOperationException` via
`CoreStrings.Format("Profile.AlreadyExists", name)`. The page displayed: `Не удалось создать
профиль: Profile "Default" already exists.` - the leading `Не удалось создать профиль:` prefix is
`ProfilesViewModel`'s own hardcoded Russian wrapper text (a separate, pre-existing App-layer gap,
out of scope for this task's ten named Core classes), but the actual Core-layer message -
`Profile "Default" already exists.` - rendered in real English, exactly matching the `en` dictionary
entry, proving the full chain (`LocalizationManager.Apply` → `CoreStrings.Language` →
`ProfileStore.CreateProfileAsync` → `CoreStrings.Format`) works end-to-end on the real machine, not
just in a unit test. Reverted `settings.json`'s `Language` back to `"ru"` afterward to restore the
session's prior state.

**Follow-up not done in this pass** (noted honestly rather than silently left out): the
`ProfilesViewModel`/`NodeViewModel`/`MinerViewModel`/etc. wrapper-text prefixes seen above
(`Не удалось создать профиль:`, and similarly-shaped ViewModel-level strings elsewhere) are a
distinct, still-open gap from the dynamic-ViewModel-string item already tracked below - this
increment only closed the ten specifically-named Core-layer classes the user's decision covered.

## This session's thirty-seventh increment: binary auto-update (keryxd/keryx-miner) + public node directory (new)

Two new user-requested features, both scoped via research first (see below) rather than assumed:
"pull updates for the node/miner binaries from their own upstream repos" and "let the miner use
someone else's node when mine isn't ready, with basic health info visible."

**Scoping research (no guessed facts)**: confirmed via live `curl` against the real GitHub API that
`Keryx-Labs/keryx-node` and `Keryx-Labs/keryx-miner` both exist and are reachable, that their
release tags follow `vMAJOR.MINOR.PATCH-OPoI` (not pure semver), and that each release's Windows
build is attached as an asset named `{repo}-{tag}-win64-amd64.zip` (e.g.
`keryx-node-v1.4.4-OPoI-win64-amd64.zip`) - this exact naming is what the new asset-matching code
targets, not a guess. Also confirmed keryxd/keryx-miner have no documented `--version` flag and the
protocol has no public seed-node/peer-directory RPC (`docs/KERYX_RESEARCH.md` was checked, not
assumed) - both facts directly shaped the design below.

**`KeryxNodeManager.Core/Updates/`** (new): `GitHubReleaseChecker` queries the public,
unauthenticated GitHub Releases API (`/repos/{owner}/{repo}/releases/latest`) and picks out the
Windows zip asset; `KeryxRepos` hardcodes the two real Keryx-Labs repo names and the expected exe
filename per binary kind; `BinaryUpdateService` compares a locally-recorded installed version
against the latest tag (any mismatch, including "never recorded," counts as an update - tags
aren't parsed/ordered numerically since `/releases/latest` has already done that work), then
downloads the zip by reusing `ModelDownloader.DownloadAsync` unchanged (same resumable/
progress-reporting machinery the Models page already relies on), extracts it, and replaces the
target exe - backing up the previous binary to `{path}.bak` first. Deliberately no single
"just update it" method: checking and applying are two distinct, explicit steps, and applying
never touches process state itself (the caller must have already stopped the process - Windows'
own exclusive file lock on a running exe makes this obvious rather than silent if the caller gets
the ordering wrong). `MiningProfile` gained `NodeInstalledVersion`/`MinerInstalledVersion` (nullable
strings) to track what this app itself downloaded, since the binaries have no queryable version.

**`KeryxNodeManager.Core/Networking/`** (new): `PublicNodeInfo` (Name/Endpoint/Port/Region/Notes/
SelfReportedUptimePercent) and `PublicNodeDirectoryService`, which loads a bundled default node
list (embedded resource, `Resources/PublicNodes.json` via `EmbeddedResource`/`LogicalName`) and/or
fetches a remote JSON list of the same shape, plus a real TCP-connect-with-timeout health check
per node. The bundled default ships as an **empty array, on purpose** - no real, confirmed public
Keryx node address exists anywhere in this project's research, and shipping a guessed one would
repeat the exact mistake `ModelDownloader`'s own doc comment already warns against for model
mirrors. `PublicNodeHealthResult` is deliberately named around "reachable just now, Nms," never
"uptime" - a desktop app that isn't running continuously cannot honestly claim to know a remote
node's historical uptime; only the operator's own self-reported number (if present in the JSON) is
ever labeled as uptime, and it's always attributed as self-reported in the UI text.

**Tests** (17 new, all against fakes/loopback sockets, no real network in the suite):
`GitHubReleaseCheckerTests` (payload parsing modeled on the real captured API response, Windows-
asset matching, non-2xx error path), `BinaryUpdateServiceTests` (version-mismatch detection
including the "never recorded" case, zip extraction finding the right exe, missing-exe-in-archive
failure, and - the one most worth protecting - `ApplyUpdate` backing up the old binary before
overwrite and never touching the target file at all if the source is missing),
`PublicNodeDirectoryServiceTests` (bundled-resource loading actually works - a packaging regression
here would silently return nothing rather than fail loudly, which is exactly what this test
catches -, remote JSON parsing including optional fields staying genuinely null rather than
defaulted, and the health check against a real loopback `TcpListener` for both the reachable and
unreachable cases, rather than mocking the socket layer trivially).

**UI**: `BinaryUpdateSectionViewModel` (shared, composed into both `NodeViewModel.NodeUpdate` and
`MinerViewModel.MinerUpdate` rather than duplicated) drives a "check for update / install update"
card on the Node and Miner pages, with a progress bar and an `IOException`-aware error message
telling the user to stop the process on the Dashboard first if the file is locked.
`PublicNodeListViewModel`/`PublicNodeRowViewModel` drive a new "Публичные ноды" card on the Node
page: refresh (bundled + optional remote URL), per-row "Проверить" (real TCP ping) and
"Использовать" (points `NodeEndpoint`/`NodePort` at that node and turns off `AutoStartNode` so this
app doesn't also launch a redundant local node). Both `NodeView.xaml` and `MinerView.xaml` gained a
`ScrollViewer` wrapper, since both pages are now taller than before.

**Build/test**: `dotnet build -c Release` → 0 warnings/0 errors. `dotnet test` → 142/142 passing
(125 pre-existing + 17 new).

**Live verification on the real Windows machine (real network, not a fake)**: clicked "Проверить
обновления" on the Node page's new card - it made a real call to `api.github.com/repos/Keryx-Labs/
keryx-node/releases/latest` and correctly reported "Установлена версия: неизвестно · Последняя
версия: v1.4.4-OPoI" with "Доступно обновление: v1.4.4-OPoI," matching exactly what this session's
own research had independently confirmed live earlier - not a coincidence, proof the real code path
matches the real API shape. "Установить обновление" correctly became enabled. Clicking it with no
`NodeExecutablePath` set correctly short-circuited with "Сначала укажите путь к исполняемому файлу
выше" rather than attempting a pointless download. On the Public Nodes card, clicking "Обновить
список" correctly loaded the (intentionally empty) bundled list and showed "Список пуст. Если у вас
есть ссылка на публичный список нод, укажите её выше." - the honest, no-fake-data state this
feature is designed to have until a real list is populated. Did not exercise a full download+
extract+replace against the real ~dozens-of-MB GitHub asset in this session (no real
`NodeExecutablePath` was configured to safely replace) - `BinaryUpdateServiceTests` covers that path
against a real (small, synthetic) zip instead; the live-verified parts here are the pieces that
can't be faked in a unit test (the real GitHub API response shape, and the two safety guards).

## This session's thirty-eighth increment: real release pipeline run (portable ZIP + Inno Setup installer), first time end-to-end

Closed the longest-standing "Known issues" item: the release pipeline (`scripts/build-release.ps1`,
`scripts/package-portable.ps1`, `installer/KeryxNodeManager.iss`) had been written but never
actually executed since the environment this project was originally authored in had no Windows/
PowerShell available. Ran it for real on the actual Windows dev machine this session:

1. `dotnet test tests\KeryxNodeManager.Core.Tests\KeryxNodeManager.Core.Tests.csproj -c Release` →
   **142/142 passing**.
2. `dotnet publish src\KeryxNodeManager.App\KeryxNodeManager.App.csproj -c Release -r win-x64
   --self-contained true -o artifacts\publish\win-x64` → succeeded.
3. `scripts\package-portable.ps1` → produced `artifacts\KeryxNodeManager-Portable-0.1.0.zip`.

**Caught and fixed a real bug along the way**: the portable ZIP's Russian first-run note
(`ПЕРВЫЙ_ЗАПУСК.txt`) came out of the archive with a mangled, garbage filename on extraction.
Root-caused it properly rather than guessing - extracted with raw `System.IO.Compression.ZipFile`
(bypassing `Expand-Archive`) and confirmed the corrupted bytes were already baked into the zip
entry itself, then checked `package-portable.ps1`'s own file bytes and found it had **no UTF-8
BOM**. Windows PowerShell 5.1 reads a BOM-less `.ps1` file using the system codepage, not UTF-8, so
the Cyrillic literal in the script's own source got misdecoded before it ever reached
`Set-Content`/`Compress-Archive` - the zip library was never the bug. Fixed by re-saving
`package-portable.ps1` with a UTF-8 BOM, and separately replaced the `Compress-Archive` call with
`[System.IO.Compression.ZipFile]::CreateFromDirectory` (Compress-Archive's own zip writer doesn't
set the UTF-8 language-encoding flag on entries, which is a second, independent bug in the same
area - belt-and-suspenders fix since either one alone would have left non-ASCII filenames broken
for some unzip tools). Re-ran the script and confirmed via raw `ZipFile.OpenRead` that the entry
name now reads back as `ПЕРВЫЙ_ЗАПУСК.txt` correctly.

**Live-verified the portable build actually launches**: extracted the corrected ZIP to a clean
directory separate from the dev build and launched `KeryxNodeManager.exe` from there directly -
confirmed a real running process (not just "the exe exists").

4. Installed Inno Setup 6 via `winget install JRSoftware.InnoSetup` (v6.7.3). Note: winget installed
   it **per-user** at `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`, not either of the two
   `Program Files` paths `build-release.ps1` originally checked - added that third path to the
   script's candidate list so future runs on this machine (or any other per-user winget install)
   actually find it instead of silently skipping the installer step.
5. `ISCC.exe installer\KeryxNodeManager.iss` → compiled successfully in ~19s, producing
   `artifacts\KeryxNodeManager-Setup-0.1.0.exe` (~49 MB).

**Live-verified the real installer end-to-end, not just that it compiled**:
- Ran `KeryxNodeManager-Setup-0.1.0.exe /VERYSILENT /DIR=...` against a clean test directory -
  confirmed it actually copied the app files and both `KeryxNodeManager.exe` and the uninstaller
  (`unins000.exe`) landed where expected.
- Launched the freshly-installed `KeryxNodeManager.exe` directly from the install directory -
  confirmed a real running process.
- Ran the generated uninstaller (`unins000.exe /VERYSILENT`) - confirmed the install directory was
  fully removed afterward.
- Confirmed the `.iss` script's documented behavior actually holds in practice: `%LocalAppData%\
  KeryxNodeManager` (the user's real config directory, created when the app first ran) **survived
  the uninstall** - matching the deliberate "don't delete user data on uninstall" design already
  written into the `.iss` comments, now actually proven rather than just asserted.

6. Generated `artifacts\checksums.txt` (SHA-256 over both the portable ZIP and the installer exe).

This closes the "Installer/portable packaging scripts... still not run end-to-end" Known Issues
entry for real - every step in the pipeline has now been executed against real artifacts on the
real machine, not just reviewed as a script.

## In progress / next steps

1. ~~**Localization: static View labels are now fully extracted...the dynamic-string gap
   remains.**~~ — **Core-layer half done.** See the thirty-sixth increment above:
   `SystemChecker`, `ProcessSupervisor`, `TierAssigner`, `TaskSchedulerAutostart`, `PathValidator`,
   `SafetyMonitor`, `ProfileStore`, `ModelDownloadException`, `NvidiaSmiGpuInfoProvider`, and
   `NativeWindowsRuntimeBackend` all now route through `CoreStrings`, live-verified to actually
   change with the app's language setting. Still open: `DashboardViewModel`'s and `GpuViewModel`'s
   own dynamic strings (`NodeStatus`/`MinerStatus`/`LastActionMessage`, `AssignmentSummary`, and the
   static `ModeOptions` list), `ModelCardViewModel.StatusText`, `NodeViewModel.StatusMessage`,
   `MinerViewModel.StatusMessage`/`AddressValidationMessage`/`CommandPreview`,
   `LogsViewModel.StatusMessage`, `ProfilesViewModel.StatusMessage` (including the
   "Не удалось создать профиль:" wrapper prefix seen live this session), and
   `WizardViewModel.StepHeader`/`DirectoriesError`/`AddressValidationMessage` plus the live
   `SystemChecker`/`GpuAssignmentResolver` result strings shown per-row in the wizard - see
   thirteenth-twenty-first increments above for exactly why that's harder than static View labels:
   no "language changed" event exists yet for ViewModels to re-project already-displayed dynamic
   strings.
2. ~~**Ukrainian ("uk") not implemented**~~ — **done.** See the twenty-fourth increment below: six
   languages (ru/en/es/it/fr/uk) are now supported, with full coverage of every static label
   extracted so far (matching the twenty-first increment's static-string-extraction scope).
3. ~~**Language ComboBox no longer dark-themed**~~ — **done.** See the twenty-ninth increment
   above: a real themed `ComboBox`/`ComboBoxItem` control template now lives in `DarkTheme.xaml`
   and applies app-wide, with keyboard nav/popup placement live-verified intact and a real
   `DisplayMemberPath` projection bug (found on the GPU page during that verification) fixed
   alongside it.
4. ~~**Tray icon state colors**~~ (brief §10) — **done for Stopped/Starting/Running.** See the
   thirty-first increment above: five badge-overlay `.ico` variants generated and wired through
   `TrayIconService.SetState`, driven live from `DashboardViewModel`'s real Node/Miner status and
   `StartAllCommand.IsRunning`, live-verified with a real grey→green badge transition. `Error`/
   `InferenceActive` icons exist and render correctly if called, but nothing calls them yet - no
   Core-layer "launch failed" or "inference active" signal exists to drive them honestly.
5. ~~**CUDA_VISIBLE_DEVICES exclusion for Disabled GPUs**~~ — **done.** See the twenty-third
   increment below: `DashboardViewModel.StartAllAsync` now sets `CUDA_VISIBLE_DEVICES` on the
   miner's launch environment as defense-in-depth alongside the existing `--force-model` exclusion,
   live-verified to correctly exclude a Disabled card's CUDA index.
6. ~~**Models page polish**~~ — **done.** ~~No confirmation dialog before Delete~~ fixed (twenty-fifth
    increment); ~~no aggregate "total models disk usage" summary~~ and ~~Pause/Resume UX reused
    "Скачать" for both fresh and resumed downloads~~ both fixed (twenty-sixth increment above).
7. ~~**Wizard polish: Finish step doesn't highlight an invalid address**~~ — **done.** See the
    thirtieth increment above: `IsAddressValid` now drives an amber highlight + warning line on the
    Finish step, live-verified with a real invalid-address pass through the wizard. The other half
    of this item remains open: the elevation-confirmation flow for an eventual WSL-backend opt-in
    (mentioned in `docs/SECURITY.md`) is still not wired to any concrete UI, wizard or otherwise -
    out of scope for this pass, no design decision made yet on what that flow should look like.
8. ~~**SettingsView.xaml has no ScrollViewer**~~ — **fixed.** See the twenty-second increment
   below: `SettingsView.xaml`/`ProfilesView.xaml`/`AboutView.xaml` are now each wrapped in a
   `ScrollViewer`, live-verified to actually scroll and expose previously-unreachable controls on
   a small window.
9. ~~**Profiles page has no per-profile quick-glance info**~~ — **done.** See the twenty-seventh
   increment above: each row now shows a masked mining address and GPU-assignment count alongside
   the name.
10. ~~**Wizard doesn't offer "create a new profile"**~~ — **done.** See the twenty-eighth increment
   above: step 0 now has a "Создать и настроить" option that creates+switches to a new profile and
   re-seeds the wizard's own input state from it.

## Known issues

- **(Fixed this session, noted here for context) The "zombie process, no window" launch flake
  documented in the fourth and sixth increments above was almost certainly this WPF
  sync-over-async startup deadlock, not AV/installer interference as originally guessed** - see
  the seventh increment for the full mechanism and fix. If a similar zero-window,
  `Responding=True`, no-crash-in-Event-Viewer hang ever shows up again after this fix, treat it as
  a new bug, not a recurrence of the old one - the specific deadlock window (a real, non-trivial
  `ConfigStore.LoadAsync` call inside `OnStartup` before `Dispatcher.Run()`) is now closed.
- ~~**Task Scheduler denies autostart registration on this machine/session - cause not fully
  pinned down**~~ — **root-caused for real, previous AV guess was wrong.** See the thirty-fifth
  increment below: this is UAC token filtering, not antivirus/EDR interference. Confirmed by
  reproducing `schtasks /Create` with the exact same `/RL LIMITED` arguments from both a normal and
  a UAC-elevated session on this machine: normal session fails with "Access is denied" every time,
  elevated session succeeds immediately, and the Defender/Task-Scheduler event logs show zero block
  events for any attempt. The app's own handling of the failure (readable error after the
  OEM-codepage fix, checkbox reverts, no partial/lying state persisted) was already correct
  regardless of root cause and needed no change; the user-facing hint text and this class's doc
  comments were updated to point at elevation instead of antivirus.
- **Automation-environment artifact, not an app bug**: on this specific Windows-MCP-driven session,
  clicking "Открыть папку логов" surfaced a "Расположение недоступно" Explorer dialog because
  `%LOCALAPPDATA%\KeryxNodeManager` resolves (per `Get-Item`, though `fsutil` denies it's a real
  NTFS reparse point) to a path under this session's own `AppData\Local\Packages\Claude_...`
  container — some folder-virtualization layer specific to how processes get launched through this
  automation channel. The app's own code (`Process.Start(..., UseShellExecute = true)`) is
  standard and correct; re-verify this specific button on a normal, non-automated desktop session
  before fully trusting it.
- The BorderBrush/Color XAML bug (see above) is fixed, but it's a reminder that anything touching
  `DarkTheme.xaml` should be re-launched and eyeballed, not just compiled — WPF resource-type
  mismatches like this don't show up as build errors, only runtime exceptions.
- ~~`WslRuntimeBackend.StopAsync`'s claim... unverified against real WSL~~ — **verified, claim
  holds.** See the thirty-fourth increment below: started a real `sleep 120` under WSL the same way
  `StartAsync` does, confirmed both the Windows-side `wsl.exe` PID and the Linux-side `sleep` PID
  were genuinely running, killed the Windows-side process tree the way `StopAsync` does, and
  confirmed the Linux-side process was gone afterward. A real, unrelated environment quirk was
  found and ruled out along the way (see that increment) before trusting the result.
- ~~Installer/portable packaging scripts (`installer/KeryxNodeManager.iss`,
  `scripts/*.ps1`) are still not run end-to-end~~ — **done.** See the thirty-eighth increment
  above: full pipeline run for real (publish → portable ZIP → Inno Setup installer → checksums),
  with a real filename-corruption bug caught and fixed along the way (BOM-less `.ps1` +
  `Compress-Archive`'s missing UTF-8 entry-name flag), and both the portable build and the
  installer's install/launch/uninstall cycle live-verified on the real machine.
- ~~`StartAllAsync` requires `NodeExecutablePath`/`MiningAddress`... not yet surfaced as a friendly
  first-run nudge~~ — **done.** See the thirty-second increment below: a "Перейти к настройкам"
  button now appears next to the error and jumps straight to the Node or Miner page.

## Scoped, not implemented: GPU fan speed / power limit control

User request: expose fan speed and power limit controls (adjustable up/down) on the app's own
panels, not just read-only monitoring. Scoped honestly rather than half-built, since real hardware
testing (this environment only has mock GPUs) is required before shipping anything that writes to
real hardware:

- **Power limit is the more tractable half.** `nvidia-smi -pl <watts>` sets a card's power limit
  directly and is officially supported by NVIDIA's own tool (unlike fan control, no undocumented
  API needed). Requires administrator rights (the same UAC-elevation question already open for
  Task Scheduler autostart - see Known issues above). Needs per-card min/max validation before
  sending the command (`nvidia-smi -q -d POWER` reports the card's actual enforceable range) - a
  value outside that range is silently clamped or rejected by `nvidia-smi` itself depending on
  driver version, which the app should surface as a validation error up front rather than trust
  blindly.
- **Fan speed has no public, vendor-supported API for consumer (GeForce) cards.** `nvidia-smi`
  itself only exposes fan control on workstation/datacenter (Quadro/Tesla) cards. The only path for
  GeForce is NVAPI - the same undocumented-but-stable private API MSI Afterburner/HWiNFO/EVGA
  Precision use - via a .NET wrapper such as the `NvAPIWrapper` NuGet package. This is materially
  riskier than the power-limit path: no official support contract, coupled to specific driver
  versions, and a third-party dependency this project hasn't vetted yet.
- **NVIDIA-only, no AMD/ADL support** - consistent with the rest of this project's GPU layer
  (`docs/KERYX_RESEARCH.md`/`ARCHITECTURE.md` already scope Keryx mining to NVIDIA CUDA cards
  only), so this isn't a new limitation, just inherited.
- **Must integrate tightly with the existing `SafetyMonitor`** (see the seventh increment above) -
  a bad fan-speed write that fails silently or a power-limit set too low right before a Critical
  overheat event would actively work against the overheat-protection this project already shipped.
  The natural design is for `SafetyMonitor` to own a "known-safe" floor/ceiling and refuse to relay
  a user-requested change that would defeat its own protection, rather than trusting the Settings/
  GPU page to enforce that independently.
- **Cannot be meaningfully built or tested in this session's environment.** Every GPU-facing
  feature in this project so far (`NvidiaSmiGpuInfoProvider`, `SafetyMonitor`, the wizard's system
  check) was built against mock data and then verified against this project's real hardware (3
  actual NVIDIA cards) directly by reading their state - but *writing* a power limit or fan curve
  to real silicon for the first time is qualitatively different risk than reading temperatures,
  and shouldn't be the first thing tried without the user directly present and aware a real
  hardware-affecting command is about to run. Recommend building the power-limit path first (its
  official-tool-support and lower risk profile make it the sensible starting point), gated behind
  an explicit confirmation dialog, and testing it live with the user watching before ever touching
  fan control at all.

Given this scope and risk profile, this session did not implement either control - the fan-speed/
power-limit backlog item is deliberately left as a scoped-but-not-started design note rather than
a rushed implementation that would need real hardware validation this session can't safely provide
unsupervised.

## Decisions

- WPF/.NET 8/MVVM confirmed as the stack.
- NativeWindowsRuntimeBackend is the default runtime, not WSL2.
- Docker backend not implemented.
- Models are app-managed downloads, not left to the miner's own IPFS auto-fetch.
- Mock backend only activates via an explicit `--mock` CLI flag, never as an automatic fallback.
- Folder picking uses .NET 8's native `Microsoft.Win32.OpenFolderDialog`, not WinForms'
  `FolderBrowserDialog` — enabling `UseWindowsForms` in a WPF project pulls a global
  `using System.Windows.Forms` into every file and collides with `UserControl`/`Application`
  everywhere (`CS0104`). Learned the hard way this session; documented in `docs/RECOVERY.md` too.

## Build status

`dotnet build -c Release` on the full solution: **passing on real Windows** (WPF markup compiler
included, not just a cross-platform compile-check). Full `win-x64` self-contained publish, portable
ZIP packaging, and Inno Setup installer build: **run for real this session** — see the
thirty-eighth increment above. Produces `artifacts\KeryxNodeManager-Portable-0.1.0.zip` and
`artifacts\KeryxNodeManager-Setup-0.1.0.exe`, both live-verified (install/launch/uninstall for the
installer; extract/launch for the portable ZIP), plus `artifacts\checksums.txt`.

## Test status

`KeryxNodeManager.Core.Tests`: **142/142 passing on real Windows** (TierAssigner,
MinerArgumentBuilder, KeryxAddressValidator, RestartPolicy, ConfigStore, SecretMasker,
PathValidator, NvidiaSmiGpuInfoProvider.ParseCsv, GpuAssignmentResolver, ModelDownloader,
SystemChecker, LogSink, DiagnosticsExporter, TaskSchedulerAutostart, SafetyMonitor, ProfileStore,
CoreStrings, GitHubReleaseChecker, BinaryUpdateService, PublicNodeDirectoryService - 35 tests added
across the Settings/autostart, Safety-monitor, and Profiles/About increments, 5 more
(`CoreStringsTests`) for the `CoreStrings` localization lookup (which also required adding
`TestAssemblyConfig.cs`, `[assembly: CollectionBehavior(DisableTestParallelization = true)]`, after
those tests exposed a real cross-test-class race on `CoreStrings.Language`'s global mutable state -
see the thirty-sixth increment above), and 17 more this session
(`GitHubReleaseCheckerTests`/`BinaryUpdateServiceTests`/`PublicNodeDirectoryServiceTests`) for the
binary-update and public-node-directory features - see the thirty-seventh increment above). No
automated UI tests yet; verification of the WPF pages was manual click-through via Windows-MCP,
documented above, including one real (non-mock) network download against a live HTTPS URL to prove
the resumable-download/checksum/progress path actually works end to end, a full 7-step click-through
of the first-run wizard that caught a real CanExecute-requery bug (see above) before it shipped, a
full click-through of the Logs page (export/open-folder/clear-screen) that caught a real latent
stdout/stderr pipe-draining bug (see above) before it shipped, a full click-through of the Settings
page's autostart toggle that caught a real OEM-codepage encoding bug and surfaced a genuine Task
Scheduler access denial on this machine (see Known issues), a full live-fire overheat test of the
Safety monitor that caught both a real synchronous-event race condition and a much more serious,
previously-misdiagnosed WPF startup deadlock (see seventh increment above) before either shipped, a
full create/switch/delete/guard-rail click-through of the new Profiles page (cross-checked against
`settings.json` directly, not just the UI) plus the About page's version/links/system-info, a live
language-switch + duplicate-profile-creation test (see thirty-sixth increment above) that confirmed
a real Core-layer exception message renders in the selected language on the actual running app, not
just in a unit test, and a live GitHub-API + loopback-socket verification (see thirty-seventh
increment above) that confirmed the real update-check flow against the real Keryx-Labs repos and
both of the update/node-list feature's safety guards (no-path-set, empty-list-is-honest).

## Last verified commit

No git repository initialized yet in this working copy. Initializing git with the checkpoint-commit
convention the brief requests (architecture, GPU monitoring, config, dashboard, node/miner wiring,
GPU→launch wiring, Models page, ...) is a good next step — this single day's work alone would be at
least four logical commits ("fix BorderBrush crash", "wire MiningProfile end-to-end", "thread GPU
page modes into miner launch via GpuAssignmentResolver", "add Models page with resumable/
checksummed downloads").

## This session's thirty-ninth increment: node RPC peer discovery, one-click data-dir download (HTTP+torrent), sync-aware node switching, and GPU overclock/fan control via NVAPI (new)

Four user-requested features, prompted by the user directly: (1) discover other nodes' IPs from
the user's own running node instead of a hand-curated list only, (2) a one-click "paste a link,
it downloads/extracts/starts syncing" data-dir bootstrap including real torrent support, (3) let
the miner point at a remote node while the local one is still syncing and auto-switch back once
it catches up, and (4) full GPU core/memory clock overclock plus fan-curve control for users who
explicitly want it, alongside the existing overheat protection (not instead of it).

**RPC peer discovery.** Confirmed keryxd's real wRPC JSON surface from source (`RpcApiOps`,
`rpc/wrpc/examples/simple_client`) rather than guessing: `getServerInfo` (`is_synced`),
`getBlockDagInfo`, `getConnectedPeerInfo`, `getPeerAddresses`, all camelCase over a WebSocket
exchanging one JSON object per frame. Built `KeryxRpcJsonClient` (Core) against this real
protocol and `OwnNodePeerDiscoveryService` on top of it, which turns the node's own peer list into
`PublicNodeInfo` entries labeled by provenance so the Node page can show "found via your own node"
distinctly from the bundled/hand-curated list. Along the way, found and fixed a real,
previously-undiscovered dead-code bug: `NodeArgumentBuilder.Build()` always emitted keryxd with
`--appdir` unset because `DashboardViewModel.cs` always called it with `appDataDir: null`, despite
the field existing on `MiningProfile` for a while - `--appdir` had silently never been passed.
Fixed by having `Build()` fall back to `profile.NodeDataDirectory` when the parameter is null,
requiring no caller changes. `--rpclisten-json` is now emitted (always bound to 127.0.0.1, never
0.0.0.0) so this app's own RPC client can talk to the node it launched.

**Data-dir download.** Confirmed there is no official Keryx snapshot/bootstrap mechanism in
keryxd itself - the only real source is the dev team's ad-hoc Discord announcements. The user
pasted a real, current such message naming four mirrors (HuggingFace direct zip, Google Drive,
a keryx-labs.com `.torrent`, and a keryx-labs.com direct zip) - used as justification to build
both HTTP (reusing the existing `ModelDownloader`) and real torrent (via the `MonoTorrent` NuGet
package) download paths in the new `DataDirDownloadService`, deliberately excluding Google Drive
(no reliable programmatic download without manual confirmation-page handling). Always wipes the
target directory before extracting, to avoid mixing old and new chain data.

**Sync-aware node switching.** `PublicNodeListViewModel` now has `DiscoverFromOwnNodeCommand` and
`SwitchBackToOwnNodeCommand`: switching to a remote node while the local one syncs remembers the
original endpoint/port/AutoStartNode, and a `PeriodicTimer`-driven watch loop (20s interval)
polls `GetIsSyncedAsync` against the local node and prompts/auto-switches back once `is_synced`
is true.

**GPU overclock/fan control.** No documented API exists for consumer GeForce clock offsets or
fan curves - the same undocumented-but-stable NVAPI that MSI Afterburner/HWiNFO/EVGA Precision
use is the only real option, via the `NvAPIWrapper.Net` package (Windows-only, referenced only
from `KeryxNodeManager.App`, never `Core` - same cross-platform-buildable constraint as every
other native-interop dependency in this project). The high-level `PhysicalGPU.PerformanceStatesInfo`
façade this library exposes turned out to be READ-ONLY for clock deltas (confirmed by reflecting
against the installed DLL, not by guessing) - actually writing an offset requires the low-level
`GPUApi.SetClockBoostTable` entry point with NVIDIA's own fixed-size per-domain delta array
(index 0 = graphics/core, index 4 = memory, the same slot convention every third-party NVAPI tool
uses). `NvApiGpuOverclockController` always read-modify-writes this table so slots this app
doesn't manage are never touched. Fan control similarly needed reflection to find the real
`GPUCoolerInformation.SetCoolerSettings(coolerId, level-or-policy)` overloads (my first guess at a
single-argument overload doesn't exist). `Core` only ever sees the `IGpuOverclockController`
interface plus `MockGpuOverclockController` (in-memory, range-validated, same contract real
hardware must honor) - `MockGpuOverclockControllerTests` (9 tests) exercise that contract without
Windows or real hardware.

Wired into the GPU page as a new `GpuOverclockSectionViewModel` per card (`GpuOverclockSectionViewModel.cs`),
constructed fresh on every `RefreshAsync` alongside its `GpuCardViewModel`, reading LIVE state from
NVAPI on load (not the persisted "last requested" value - a reboot/driver reset silently returns a
card to stock, and showing a stale value as current would be actively misleading). Every
hardware-affecting action (Apply, Reset) is gated behind a `MessageBox.Show(...YesNo...)`
confirmation - the same low-risk-irreversible-action pattern `ModelsViewModel.Delete` already
established - plus Apply additionally refuses to run while `SafetyMonitor.GetLastLevel(gpuUuid)`
reports Critical (added `SafetyMonitor.GetLastLevel` for exactly this read, covered by two new
tests in `SafetyMonitorTests`). Reset is deliberately NOT blocked by the Critical check, since
resetting to stock can only help an already-hot card.

**Testing and verification actually performed this pass:**
- `KeryxRpcJsonClientTests` (3 tests) run against a hand-rolled real loopback WebSocket server
  built directly on `TcpListener` with a manual RFC 6455 handshake/frame parser - `System.Net.
  HttpListener.AcceptWebSocketAsync` hung indefinitely for unknown reasons in the dev sandbox, so
  rather than fight an opaque hang, the test server was rewritten at a lower level. This also
  caught a real protocol bug: `KeryxRpcJsonClient` originally serialized its request envelope from
  a C# record, producing PascalCase JSON keys (`Id`/`Method`/`Params`) instead of the lowercase
  keys keryxd's wRPC JSON actually expects - caught by the test itself, fixed by switching to an
  anonymous object with explicit lowercase property names.
- `NodeArgumentBuilderTests` (7 tests), `DataDirDownloadServiceTests` (dispatch + real HTTP
  zip download/extract + stale-content-wipe cases), `MockGpuOverclockControllerTests` (9 tests),
  and two new `SafetyMonitorTests` cases for `GetLastLevel`.
- Full solution build (`dotnet build -c Release`, both in the Linux cross-compile-check sandbox
  with `-p:EnableWindowsTargeting=true` AND, separately, a real `dotnet build` on the actual
  Windows machine) - both succeed with **0 warnings, 0 errors**.
- Full Core test suite (`dotnet test`) - **170/170 passing**, both in-sandbox and matching the
  count expected from the new tests above.
- Launched the real built app on the real Windows machine with `--mock` (so no real hardware or
  live keryxd/miner process is touched) - it starts cleanly, `Get-Process` shows it Responding,
  and the Dashboard renders correctly with the mock GPU count shown, confirming the DI wiring
  changes (new `IGpuOverclockController` registration, `GpuViewModel`'s extra constructor
  parameters) don't crash app startup.

**What was NOT verified this pass, honestly flagged rather than glossed over:**
- UPDATE (immediately following session): live-verified. Launched the real built app on the
  real Windows machine, both with `--mock` (screenshot confirms the new sliders/checkbox/Apply/
  Reset buttons render correctly for two mock GPU cards) and WITHOUT `--mock` against the actual
  installed hardware (an NVIDIA GeForce RTX 5070 and an NVIDIA CMP 90HX). On the RTX 5070, the
  overclock section's `GetCapabilitiesAsync`/`GetCurrentStateAsync` real NVAPI calls succeeded and
  populated genuine live values (a non-default fan state was shown, i.e. this wasn't a
  coincidental zero/placeholder render) - screenshot captured as evidence. The CMP 90HX card's
  overclock section was not visible in the captured screenshot (window too small / overlapped by
  other app windows on this desktop) - whether it renders correctly, shows a real "not supported"
  message (CMP mining-only cards are known to lack the display/cooler surface NVAPI needs on some
  driver versions), or something else, is UNKNOWN and should be checked next with a full-size,
  unobstructed window. No Apply/Reset was clicked in either run - the app was closed via
  `Stop-Process` immediately after the read-only screenshot, per the hardware-safety rule below.
- No real hardware WRITE (an actual `ApplyClockOffsetsAsync`/`ApplyFanSpeedAsync` call against a
  real NVIDIA GPU) has been performed. This is deliberate, not an oversight: changing real clock
  offsets or fan curves is the one action in this entire app that can genuinely damage hardware or
  destabilize the OS if a wrong value is applied, and per this project's own established norm this
  should not be the first thing tried without the user directly present and aware a real
  hardware-affecting command is about to run. `GetCapabilitiesAsync`/`GetCurrentStateAsync`
  (read-only NVAPI calls) are safe to verify live and are the natural next step before ever
  touching `Apply`.
- The torrent download path (`MonoTorrent`) was exercised only through the dispatch-logic tests
  (`IsTorrentUrl`) - no real `.torrent` file was actually downloaded end-to-end this pass, since
  the real mirror in the user's pasted Discord message may no longer be seeded/current by the time
  this is read; this should be checked against a currently-live torrent before relying on it.

**Known follow-ups, not yet done:**
- Live-verify GPU page rendering + a read-only Capabilities/State NVAPI call on the real machine
  (next step for task #111), and only after that - with the user present - consider a real,
  small, reversible Apply (e.g. 0 MHz offset, a no-op write) as the first-ever real hardware write.
- `MiningProfile.GpuOverclockSettings`' own doc comment describes an "auto re-apply on Start All"
  idea (mirroring the AutoRestartOnCrash pattern) - this was scoped in the comment but NOT
  implemented; today the persisted settings are write-only from the UI's perspective and are
  never read back to auto-apply anything.
- Google Drive is deliberately unsupported as a data-dir mirror (no reliable programmatic
  download without manual confirmation-page handling) - if the HuggingFace/keryx-labs.com mirrors
  ever go down, only the Discord-pasted Google Drive link would remain, requiring a manual
  browser download.

## This session's fortieth increment: closed the CMP 90HX overclock-verification gap, found and fixed a real UI bug along the way (new)

Direct follow-up to task #113 from the thirty-ninth increment: the CMP 90HX card's overclock
section was cut off in the earlier screenshot and its actual behavior was unknown. Resized the
real app window on the real Windows machine (via a direct `MoveWindow` Win32 call through
PowerShell, bypassing the UI-automation tool's unreliable coordinate clicks on this app's custom
controls) so both real GPU cards were fully visible, then re-checked.

**Real bug found and fixed:** the CMP 90HX's overclock section genuinely fails - NVAPI enumerates
only 1 GPU on this machine (`PhysicalGPU.GetPhysicalGPUs().Length == 1`) while `IGpuInfoProvider`
(nvidia-smi) reports 2, so `NvApiGpuOverclockController.ResolveGpu` correctly throws
`GpuOverclockException("NVAPI reports 1 GPU(s), but device index 1 was expected...")` for the
second card - almost certainly because this specific card (a headless, display-less mining ASIC-
like card) doesn't register with NVAPI's device enumeration on this driver at all, a real and
worth-documenting hardware quirk, not a bug in this app's own logic. However, the ORIGINAL
`GpuView.xaml` nested the `LastError` TextBlock inside the same `Visibility`-gated `Border` as the
interactive controls (both gated together on `Overclock.IsSupported`), so when a card genuinely
isn't supported, the user saw absolutely nothing - no sliders, no error, no explanation. Fixed by
moving the error message to a sibling `TextBlock`, gated on the inverse condition (reusing the
existing `InverseBooleanToVisibilityConverter` from the wizard), so an unsupported card now shows
"Разгон недоступен для этой видеокарты: <real NVAPI exception message>" instead of a blank space.

Confirmed the fix with a second real, non-mock launch: the RTX 5070 card's overclock section still
works exactly as before (live NVAPI-sourced sliders/fan state), and the CMP 90HX card now
correctly shows the explanatory error text instead of rendering nothing. Rebuilt clean (0
warnings/errors, both Linux cross-compile-check and real Windows `dotnet build`) and re-ran the
full Core test suite (170/170 passing, unaffected since this was a View-only XAML change) before
and after the fix. Task #111/#113's live-verification gap is now fully closed - both the supported
and unsupported real-hardware paths have been observed to render correctly on the actual machine.

No Apply/Reset was clicked in this pass either - both launches were closed via `Stop-Process`
immediately after their respective screenshots.
