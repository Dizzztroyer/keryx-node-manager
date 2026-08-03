# Keryx Research Notes

Source-verified facts about `Keryx-Labs/keryx-node` and `Keryx-Labs/keryx-miner`, collected by
cloning both repositories (`git clone --depth 1`) on 2026-08-02 and reading the actual Rust
source (`src/cli.rs`, `src/models.rs`, `src/miner.rs`, `src/stats.rs`, `keryxd/src/args.rs`) plus
the published GitHub Releases. Every claim below is either a direct quote/paraphrase of code or
README text, or explicitly marked as unverified. Nothing here is guessed.

Repos checked: `keryx-node` (Rust full node, GHOSTDAG BlockDAG, a Kaspa-derived codebase — same
crate naming conventions as `kaspad`), `keryx-miner` (Rust GPU miner with CUDA-only PoM + OPoI
inference). Latest releases at research time: `keryx-node v1.4.3-OPoI`, `keryx-miner
v0.4.2-OPoI`.

## 1. Windows support is native and official — this changes the whole architecture

The single most important finding: **both projects ship official precompiled win64 binaries**,
downloaded directly from GitHub Releases:

- `keryx-node-v1.4.3-OPoI-win64-amd64.zip`
- `keryx-miner-v0.4.2-OPoI-win64-amd64.zip`

The miner's own source has `#[cfg(target_os = "windows")]` branches in `main.rs`, `miner.rs`,
`llama_engine.rs`, `slm.rs`, and `ipfs.rs` (which downloads `ipfs.exe` and a `.zip` archive on
Windows, vs. `ipfs` + `.tar.gz` on Linux). The node's README documents a full native Windows build
procedure (Git for Windows, Protocol Buffers, LLVM 15, Rust, `wasm-pack`) alongside Linux and
macOS.

**Consequence for this app:** WSL2 is not required to run Keryx on Windows. A
`NativeWindowsRuntimeBackend` that runs the official `keryxd.exe` / `keryx-miner.exe` binaries
directly is the primary, supported path, not a fallback. WSL2/Docker backends are kept as
optional/secondary for users who specifically want a Linux-side setup (e.g. HiveOS-style rigs,
or building from source with the Linux CUDA toolchain), but the default experience needs neither.
This directly overrides the task brief's assumption that WSL2 is the primary target — that
assumption predates checking the actual releases.

One Linux-only wrinkle: `install_cuda_libs()` in the miner's `main.rs` (auto-installing
`libcublas-12-2`/`libcurand-12-2` via `apt-get`) is `#[cfg(target_os = "linux")]` only. On Windows
the equivalent CUDA runtime DLLs ship with the NVIDIA driver/CUDA toolkit and are not something
the miner manages — if inference fails to load, the fix is installing/updating the NVIDIA driver
or CUDA runtime, not an apt-style step.

## 2. Miner CLI (`keryx-miner.exe --help`, from `src/cli.rs`)

```
keryx-miner --mining-address keryx:YOUR_ADDRESS
```

Confirmed flags (`clap::Parser` struct `Opt`):

| Flag | Meaning |
|---|---|
| `--mining-address` / `-a` (required) | Keryx address for rewards. Address prefix format is `keryx:...` (bech32-style, same family as Kaspa's `kaspa:` addresses — this is a Kaspa-derived codebase). |
| `--very-light` | Tier 0: Qwen3-8B-abliterated, Q4_K_S, 6 GB+ VRAM |
| `--light` | Tier 1: Mistral-7B-v0.3, Q6_K, 8 GB+ VRAM |
| *(no flag = default)* | Tier 2: GLM-4-9B-0414, Q6_K, 12 GB+ VRAM |
| `--high` | Tier 3: Qwen3.6-27B, Q4_K_M, 24 GB+ VRAM |
| `--very-high` | Tier 4: Kimi-Linear-48B, Q4_K_M, 32 GB+ VRAM |
| `--force-model TIER[,TIER...]` | Per-GPU override, **CSV in CUDA driver order** (GPU0,GPU1,...). Values: `very-light\|light\|default\|high\|very-high`. Bypasses the VRAM check for listed GPUs (undersized card can OOM); GPUs beyond the list keep auto best-fit. |
| `--models-dir` (or `KERYX_MODELS_DIR` env) | Root directory for model files; miner still appends `<Model-Name>/model.gguf`. Default: `<exe_dir>/models`. |
| `--hiveos` | Use `/hive/miners/custom/models` as the default models dir when `--models-dir` is unset. Linux/HiveOS-specific; irrelevant on Windows. |
| `--ipfs-url` | IPFS Kubo API URL for uploading inference results. Default `http://127.0.0.1:5001`. |
| `--keryxd-address` / `-s` | Node host (default `127.0.0.1`); the miner talks to it over **gRPC**. Auto-prefixed to `grpc://host:port` if no scheme given. |
| `--port` / `-p` | keryxd port, default 22110 mainnet / 22211 testnet. |
| `--testnet` | Switch DAA activation gates to testnet values. |
| `--devfund-percent` | Percent of blocks to devfund, **minimum enforced at 2%** (values below 2 are silently forced to 2.00). Devfund address is hardcoded in the binary. |
| `--threads` / `-t` | CPU miner threads (0 = default in observed code path). |
| `--mine-when-not-synced` | Mine even if keryxd reports not-synced (paired with a keryxd flag). |
| `--stats-bind` / `--stats-port` | Local stats HTTP server bind address/port. Default port **3338**. |
| `--escrow-key-file` / `--escrow-state-file` | OPoI escrow key/state file paths, default `escrow.key` / `escrow_state.json` in the working directory. |
| `--recover-escrow` / `--recover-escrow-api` | Rebuilds escrow state from the public API (default `https://keryx-labs.com`), then exits. |
| `--plain-log-file` | Write plain-text logs to a file. |
| `--debug` / `-d` | Debug log level. |

No `--exclude-gpu` or `CUDA_VISIBLE_DEVICES`-handling code was found anywhere in the miner source
(`grep` across `src/` for `CUDA_VISIBLE_DEVICES`, `exclude`, `device_count` came up empty aside from
unrelated matches). `CUDA_VISIBLE_DEVICES` is a **CUDA driver-level environment variable**, so it
should still work transparently (any CUDA application respects it) — this is standard CUDA
behavior, not something confirmed by reading the miner's own code, so treat it as "expected to
work by platform convention," not "confirmed by Keryx code." The supported, code-confirmed way to
control per-GPU behavior is `--force-model`, which is positional in **CUDA driver enumeration
order** — the same order `nvidia-smi` and `nvcc`-based CUDA apps normally agree on, but not
guaranteed to match Windows Device Manager order.

## 3. GPU / model-tier mechanics (`src/models.rs`, `src/miner.rs`, `src/pom_gpu.rs`)

- **One process, multiple GPUs.** `MinerManager::launch_gpu_threads` iterates over
  `PluginManager::build()`'s device specs and spawns one worker thread per GPU inside the single
  `keryx-miner` process. There is **no need to run a separate process per GPU** — this is
  code-confirmed, not an assumption.
- **Tier is per-GPU, chosen from VRAM.** `min_vram_mb` on each `ModelSpec` gates which tier a card
  auto-selects (comments in `models.rs` state this explicitly, e.g. GLM-4-9B needs 12 GB because
  Q6_K weights (~8.3 GB) plus KV/workspace (~1.5 GB) don't fit a 6 GB or 8 GB card). A mixed rig
  runs different tiers on different cards simultaneously — confirmed by the README ("a mixed rig
  runs several tiers side by side") and by `--force-model` accepting one tier value per GPU.
- **No VRAM pooling.** Nothing in the source combines multiple GPUs' VRAM into a shared pool —
  each `ModelSpec` loads into exactly one GPU's context (`CudaContext::new(device_id)` in
  `pom_gpu.rs::load_llama`). The UI must not suggest that 2×12 GB becomes 24 GB; each GPU holds
  and serves its own independent model copy.
- **Verified VRAM table** (from `models.rs` `min_vram_mb` constants — these are the authoritative
  numbers, not the README table, though they agree):

  | Tier flag | Model | Format | `min_vram_mb` |
  |---|---|---|---|
  | `--very-light` | Qwen3-8B-abliterated (Q4_K_S) | GgufQwen35 | 6000 |
  | `--light` | Mistral-7B-v0.3 (Q6_K) | Gguf | 8000 |
  | *(default)* | GLM-4-9B-0414 (Q6_K) | GgufGlm4 | 12000 |
  | `--high` | Qwen3.6-27B (Q4_K_M) | GgufQwen35 | 24000 |
  | `--very-high` | Kimi-Linear-48B (Q4_K_M) | GgufKimiLinear | 30000 |

  Note the top tier's actual gate is 30000 MB, not the README's rounded "32 GB+" — use 30000 MB as
  the hard cutoff in the app's auto-assignment logic, with the README's 32 GB figure treated as a
  practical safety margin.
- **Models auto-download over IPFS on first run** and are cached at
  `<models-dir>/<Model-Name>/model.gguf`. The miner itself validates the download (writes an
  internal `.ok`-equivalent marker after validation — do not create that marker file manually).
  Manual install is supported: download the zip (HuggingFace/direct/torrent mirrors are listed in
  the miner README) and unzip into `<models-dir>/<Model-Name>/model.gguf`, keeping the exact
  folder name from the table above.
- **Mining pauses for inference, per GPU.** README: "Mining pauses on that GPU during inference,
  then resumes automatically." This is per-card, not a whole-rig pause.
- **H4/H5 hardfork gating.** `models.rs` shows the tier lineup is DAA-activation-gated
  (`coin_age_verification_activation_daa`, `h5_activation_daa`); a miner refuses to mine below the
  H4 activation height. Not actionable for the desktop app beyond knowing that "which tier is
  valid" can shift at a hardfork boundary — the app should not hardcode assumptions about which
  DAA range is active, just surface whatever the running binaries report.

## 4. Telemetry / monitoring available from the miner itself

The miner runs its own tiny HTTP/1.1 JSON server (`src/stats.rs`, `spawn_stats_server`, default
port **3338**, bind configurable via `--stats-bind`). Plain `GET /stats` or `GET /v1/miner/stats`
returns `MinerStatsSnapshot` as JSON — a real, working, low-friction API to poll instead of
re-parsing stdout:

```json
{
  "started_epoch_s": 0, "uptime_s": 0, "synced": true, "opoi_challenge_active": false,
  "mining_address": "keryx:...", "api_port": 3338, "total_hashrate_hs": 0,
  "accepted_blocks": 0, "rejected_blocks": 0, "claimed_outputs": 0, "claimed_sompi": 0,
  "escrow_pending_outputs": 0, "escrow_pending_sompi": 0, "last_update_epoch_s": 0,
  "devices": [ { "id": "...", "hashrate_hs": 0, "temp_c": 0, "memory_temp_c": 0,
                 "fan_percent": 0, "power_draw_w": 0.0 } ]
}
```

The miner itself gets GPU temperature/fan/power by shelling out to `nvidia-smi
--query-gpu=temperature.gpu,temperature.memory,fan.speed,power.draw --format=csv,noheader,nounits`
on a 10-second interval (`refresh_gpu_telemetry` in `stats.rs`). **This app should independently
call `nvidia-smi` for its own GPU cards page** (more fields needed: name, UUID, VRAM used/total,
utilization, driver version, power limit, clocks) rather than depend solely on the miner's stats
endpoint, since the miner's own telemetry only has 4 fields and only while it is running. Use the
miner's `/stats` endpoint for hashrate/blocks/escrow/sync data that only the miner process knows.

**Hashrate and blocks are the only mining-economics numbers the miner reports.** There is no
earnings/revenue estimate anywhere in the source — the task brief's instruction not to invent a
"hashrate or earnings" figure the miner doesn't provide is consistent with the code: don't
extrapolate USD/day or similar, only surface `total_hashrate_hs`, per-device `hashrate_hs`,
`accepted_blocks`/`rejected_blocks`, and the escrow/claimed counters, all taken verbatim from
`/stats`.

## 5. Node (`keryxd`) surface (`keryxd/src/args.rs`, `keryxd/src/daemon.rs`)

- **RPC ports** (mainnet / testnet): gRPC `22110` / `22210`, wRPC Borsh `23110` / `23210`, wRPC
  JSON `24110` / `24210`, P2P listen `22111` / `22211`.
- **Default data directory**, confirmed directly in `keryxd/src/daemon.rs::get_app_dir()`:
  `home_dir().join("keryx-labs")` on Windows (i.e. `%USERPROFILE%\keryx-labs`), and
  `home_dir().join(".keryx-labs")` on Linux/macOS. (A leftover help-text comment elsewhere in the
  CLI still shows a stale `Kaspad`-style example path — `get_app_dir()` is the actual code path and
  is authoritative.) Override with `--appdir`.
- `--rpclisten`, `--rpclisten-borsh`, `--rpclisten-json`, `--unsaferpc`, `--utxoindex`,
  `--reset-db`, `--archival`, `--connect`/`--addpeer`, `--disable-grpc`, `--disable-dns-seeding`,
  `--disable-upnp`, `--testnet`/`--devnet`/`--simnet` are all real, present flags.
- Config file support: `--configfile` (TOML), default location again OS-templated similarly to
  appdir.
- The mobile wallet integration (see project memory `keryx-mobile-project-facts`) confirms a
  **separate REST gateway** (not the gRPC/wRPC ports above) exposing `/broadcast`,
  `/api/v1/infer`, `/api/v1/capabilities`, `/api/v1/challenges` — a different service from
  `keryxd` itself. This pass checked the `bridge/` component in the `keryx-node` repo tree
  (`keryx-stratum-bridge`, a stratum protocol adapter for stratum-based miners) and confirmed by
  grep that it does **not** serve any `/api/v1/*` or `/broadcast` route — so the REST/inference
  gateway is a separate, still-unidentified component (possibly hosted only at keryx-labs.com, or
  shipped with a different repo not covered in this pass). Do not assume it ships bundled with
  `keryxd.exe`, and do not point the app at the `bridge/` binary expecting inference routes.

## 6. Address format

Addresses are `keryx:`-prefixed (confirmed in `cli.rs`'s hardcoded devfund address
`keryx:qrxpcusyrxjxghfdumcxm2rqw4dhe3n9hyqpvgn2wfyldltf99w2xhnajuhte` and the address-prefix
handling in `crypto/addresses/src/lib.rs`, which is structurally the Kaspa `Prefix`/bech32m address
crate). Treat it as "must start with `keryx:` followed by a bech32-charset payload" for a
lightweight local format check; do not attempt full checksum validation in the desktop app unless
the `keryx_addresses` crate's exact charset/checksum algorithm is vendored and tested — a false
"invalid address" rejection is worse than a shallow prefix check that lets the node's own error
message be authoritative.

## 7. What was explicitly NOT found / not confirmed (do not implement as if it exists)

- No GPU-exclude flag, no `CUDA_VISIBLE_DEVICES`-aware code path in the miner.
- No cross-GPU VRAM pooling / tensor-parallel model splitting anywhere in `models.rs` or
  `pom_gpu.rs`. Each tier is one model in one GPU's memory.
- No earnings/hashrate-to-currency estimate anywhere in the miner.
- No pause/resume/download-progress IPC beyond what `/stats` exposes and stdout logging — download
  progress for the app's own "Models" page (section 7 of the brief) has to come from the app
  driving its own download (see Architecture doc: app-managed downloads, not miner-managed, for
  progress/pause/resume/checksum UI).
- The exact Windows default data directory for `keryxd.exe` was not independently confirmed
  against the compiled release binary (see §5) — flag this to verify once the Windows binary is
  available, rather than hardcoding a guess into the shipped app.

## 8. Licensing

Both `keryx-node` and `keryx-miner` ship dual `LICENSE-APACHE` / `LICENSE-MIT` files (permissive,
inherited from the Kaspa lineage this codebase forks from). These licenses permit
building/redistributing the binaries; they say nothing about the Keryx brand/logo, which is a
separate trademark question — treat brand assets as "ask before using," per the task brief's own
instruction, and ship a neutral, original icon instead.
