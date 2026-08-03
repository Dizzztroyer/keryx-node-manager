# Keryx Node Manager

*[Читать по-русски](README.md)*

A Windows app for managing a Keryx node and GPU miner from a single window — no manual
PowerShell/WSL/Docker work required. Community tool, not an official Keryx Labs product.

## Download and install

Go to the **[Releases](https://github.com/Dizzztroyer/keryx-node-manager/releases/latest)** page
and download one of these:

- **`KeryxNodeManager-Setup-X.Y.Z.exe`** — regular installer. Run it, follow the wizard, done. A
  shortcut is added to your desktop and Start menu. No admin rights required.
- **`KeryxNodeManager-Portable-X.Y.Z.zip`** — no-install portable version. Unzip anywhere and run
  `KeryxNodeManager.exe`.

On first launch, a setup wizard walks you through a system check, entering your mining address,
and creating/selecting a profile.

**Requirements:** Windows 10/11 x64, an NVIDIA GPU (for auto-detection and overclocking). The
node binary (`keryxd.exe`) and miner binary (`keryx-miner.exe`) are not bundled — the app's
built-in updater can fetch them for you.

## Features

- Start/stop the node and miner with one click, tray icon with live status.
- Automatic GPU detection, auto-assignment of mining tier by VRAM, or manual per-card selection.
- GPU overclocking (core/memory clock) and fan control — gated behind a confirmation dialog.
- Managed model file downloads (resumable, integrity-checked).
- Public node directory plus automatic peer discovery through your own node; switch to a backup
  node while yours syncs, with automatic switch-back once it's caught up.
- One-click data-dir download and extraction (direct link or torrent).
- Logs with automatic secret masking, diagnostic export.
- Overheat protection, launch-at-Windows-startup option.
- Multiple profiles, UI available in 6 languages (ru/en/es/it/fr/uk).
- Built-in update checker for the node and miner binaries.

## Security

The app never asks for or stores seed phrases or private keys. Every RPC address the app can
respond on is bound to `127.0.0.1` (localhost) only — nothing is exposed externally. See
`docs/SECURITY.md` in the repository for details.

## For developers

```powershell
dotnet restore
dotnet test tests\KeryxNodeManager.Core.Tests\KeryxNodeManager.Core.Tests.csproj -c Release
dotnet run --project src\KeryxNodeManager.App -- --mock
```

`--mock` runs the UI against virtual GPUs, with no real Keryx binaries or NVAPI involved — a safe
way to preview the interface. See `docs/BUILD.md` for build details and `docs/RELEASE.md` for the
release process.

## License and status

Actively developed, community-driven project. Bug reports and suggestions are welcome via Issues.
