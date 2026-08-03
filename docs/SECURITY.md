# Security notes

Rules this codebase follows, and why:

**Never asks for or stores a seed phrase or private key.** Only a public mining address is
collected (`KeryxAddressValidator`, first-run wizard). There is no code path anywhere in
`KeryxNodeManager.Core`/`App` that reads or persists a private key.

**No shell string concatenation, ever.** Every child process (`keryxd.exe`, `keryx-miner.exe`,
`wsl.exe`, `nvidia-smi`) is launched via `ProcessStartInfo.ArgumentList` — see
`MinerArgumentBuilder`, `NodeArgumentBuilder`, `NativeWindowsRuntimeBackend`, `WslRuntimeBackend`,
`NvidiaSmiGpuInfoProvider`. `UseShellExecute = false` everywhere. This is covered by an explicit
regression test (`MinerArgumentBuilderTests.Build_NeverProducesASingleConcatenatedCommandString`).
User-supplied "extra arguments" strings are appended as individual list entries, not glued into a
combined command line, so a value like `--foo; rm -rf /` cannot break out into a second command.

**Runs as the current user (`asInvoker`), never demands admin silently.** `app.manifest` sets
`requestedExecutionLevel level="asInvoker"`. The one operation that genuinely needs elevation
(enabling the WSL Windows feature, if the user opts into the WSL backend) must show what command
will run and ask for confirmation before triggering a UAC prompt — this is a first-run-wizard
requirement not yet wired to a concrete implementation in this pass (see `PROJECT_STATUS.md`).

**Path validation before any path is persisted or handed to a child process.** `PathValidator`
rejects invalid characters, relative paths, and paths inside protected system folders
(`%WINDIR%`, `%SYSTEMROOT%`).

**Secrets are masked before they can reach a log file or the diagnostic ZIP.** `SecretMasker`
truncates addresses to `keryx:abcdef…wxyz` form and redacts anything matching a long hex string or
a `token=`/`secret:`/`bearer `-shaped pattern in captured stdout/stderr before it's written to disk
or included in a diagnostics export.

**No telemetry by default.** `AppSettings.TelemetryOptIn` defaults to `false`. There is no
telemetry-sending code in this pass at all — the flag exists so a future opt-in feature has
somewhere to live, not because anything currently reads it to phone home.

**Downloads only from sources the miner's own README lists**, or a source the user explicitly
supplies (models page, not built in this pass — see `PROJECT_STATUS.md`). No silent redirection to
a third-party mirror.

**Does not touch Windows Defender or antivirus exclusions.** No code adds folders to AV
exclusion lists, disables Defender, or suppresses SmartScreen prompts. If a user hits a
SmartScreen warning on an unsigned build, the fix is code-signing the release (see
`docs/RELEASE.md`), not telling the user to disable protections.

**Config integrity.** `ConfigStore.SaveAtomicAsync` writes to a temp file, flushes to disk, then
does an atomic `File.Replace` with a `.bak` kept — a crash mid-save cannot leave `settings.json`
half-written (covered by `ConfigStoreTests`).

## Licensing note

`keryx-node` and `keryx-miner` are dual-licensed Apache-2.0/MIT (see
`docs/KERYX_RESEARCH.md` §8) — this app never redistributes their binaries itself, only launches
binaries the user downloaded from the official GitHub Releases. The app's icon (`app.ico`,
`tray.ico`, `icon_256.png`) was originally a neutral, non-branded design generated for this
project specifically to avoid using the Keryx brand mark without clear permission - on
2026-08-02 the project owner explicitly requested switching to the real logo from
`https://keryx-labs.com/logo.png`, so the icon now uses that mark directly. If this app is ever
redistributed beyond the owner's own use, re-confirm that using the keryx-labs.com logo is still
authorized (a personal management tool run by the trademark holder themselves is a very different
situation from a public download for third parties).

## Known gaps to close before a public release

Code signing (an unsigned `.exe`/installer will trigger SmartScreen); a documented process for
verifying `keryxd.exe`/`keryx-miner.exe` release checksums before first launch (the miner's
GitHub Releases page does not currently publish a `checksums.txt` — verify this before writing
copy that claims the app checks it); and a security review of the (not yet built, see
`PROJECT_STATUS.md`) auto-update flow described in the brief §19, since automatic updates are the
highest-risk feature in the whole spec if implemented carelessly.
