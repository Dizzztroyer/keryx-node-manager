# Full wallet integration — plan (2026-08-04)

User request: integrate Keryx Labs' own Windows wallet into KeryxNodeManager, with seed import,
consolidation, real transaction history, and mining-yield percentage, fully localized.

This document is research + a recommendation, not a landed feature. Seed-phrase/private-key
handling is the single highest-stakes thing this app could ever touch — a mistake here can
directly cost the user real money, permanently and silently. That is why this was written up for
review instead of wired in unattended while the user was away: the *architecture* decision below
(launch the official binary, never reimplement the crypto) is the one that actually matters, and
it deserves a deliberate yes before any code lands.

## 1. What the official wallet actually is (verified 2026-08-04, github.com/Keryx-Labs/keryx-desktop-wallet)

- **Tauri v2** desktop app: Rust shell + **React/TypeScript** frontend (Tailwind), for **Windows
  and Linux**. Not a .NET/WPF app, not a library we can `<ProjectReference>`.
- Cryptography is **not reimplemented** by the wallet UI itself — it wraps "the upstream wallet
  library" (Keryx's own Rust wallet-core, compiled to **WebAssembly** and vendored into
  `src/sdk/`), the same crypto the rest of the ecosystem uses. Recovery phrase is **encrypted at
  rest** (Argon2 key derivation + XChaCha20-Poly1305), never logged; keys are derived in memory
  only; every send is confirmed and signed locally.
- Talks to a **Keryx node's Borsh wRPC endpoint** — default `ws://127.0.0.1:23110` — which MUST be
  started with **`--utxoindex`**. This is a **different port and wire encoding** than
  `KeryxRpcJsonClient` in this app already uses (that's the JSON wRPC listener, port 24110/24210,
  added via `--rpclisten-json`). NodeArgumentBuilder does not currently emit `--rpclisten-borsh`
  at all.
- License: **MIT**.
- **No GitHub releases published yet** (checked 2026-08-04) — only source, a CI workflow that
  builds `.msi`/`.exe` (Windows, via GitHub Actions) and `.deb`/`.AppImage` (Linux) into a **draft**
  release when a version tag is pushed. There is currently nothing to download and bundle.

## 2. Recommendation: launch it as its own process, never reimplement its crypto

Treat `keryx-desktop-wallet.exe` exactly the way this app already treats `keryxd.exe` and
`keryx-miner.exe`: a real, independently-versioned, officially-built binary that this app can help
a user obtain and launch, but whose internals this app never reimplements or reaches into.

Concretely, once the wallet has its first real release:

1. Add `ManagedBinaryKind.Wallet` alongside `Node`/`Miner` in `KeryxRepos`/`GitHubReleaseChecker` so
   `BinaryUpdateService`'s existing check/download/extract/apply-update machinery (see
   `deploy_updater_fix.bat`'s commit history, 2026-08-04 — the sibling-file-copy fix applies here
   too, since a Tauri app ships more than one file) works for it with zero new code in that layer.
2. Add `MiningProfile.WalletExecutablePath` (same pattern as `NodeExecutablePath`/
   `MinerExecutablePath` — empty until the user points at a real binary, never bundled).
3. Add `--rpclisten-borsh=127.0.0.1:{port}` to `NodeArgumentBuilder` (new
   `MiningProfile.NodeRpcBorshEnabled`/`NodeRpcBorshPort`, mirroring the existing
   `NodeRpcJsonEnabled`/`NodeRpcJsonPort` fields exactly) — this is a prerequisite the wallet
   needs and this app doesn't currently provide at all.
4. Add a **"Кошелёк" nav page or a single "Open Wallet" button** (Dashboard or Node page) that does
   `Process.Start` on the configured wallet exe — an ordinary GUI launch, **not** something
   `ProcessSupervisor` manages (the wallet is an interactive app the user drives and closes
   themselves, not a background daemon we restart on crash).
5. Everything the user actually asked for — seed import, consolidation (send-to-self /
   sweep UI), real transaction history, password unlock, QR receive — already exists in that
   wallet's own UI. This app's job is only: help the user get the right binary, make sure the node
   is running with the flag the wallet needs, and launch it. Zero seed/key code in this repo, ever.

## 3. What this explicitly rules out

- **Do not** reimplement seed generation/import, BIP-39-equivalent derivation, or transaction
  signing in this app's C#/.NET code. Getting this byte-exact wrong (address derivation, change
  outputs, fee calculation, signature scheme) is a real-money bug class, and there is already a
  first-party, MIT-licensed implementation actively maintained by the team that built the chain
  itself — reimplementing it here would only add a second, unaudited copy of the riskiest code in
  the whole ecosystem.
- **Do not** embed the wallet's WASM `sdk/` directly into this app (e.g. via a WebView2 hosting the
  wallet's own web build) as a shortcut to "one process instead of two" — that reintroduces the
  same key-handling surface into this app's process boundary for a UX convenience that doesn't
  justify the risk, and the wallet's own CSP/Tauri-capability hardening (documented in its README)
  wouldn't carry over to a raw WebView2 host.
- **Do not** try to source a private build/older commit ourselves given the repo currently has no
  releases — better to wait for the team's own signed release + draft-review process (their
  release workflow explicitly creates a **draft**, reviewed release, not an auto-published one) than
  to point users at an unreviewed build of wallet software.

## 4. What already exists in this repo and how it relates

- The Dashboard "Кошелёк" balance card added 2026-08-04 (`WalletRpcService`,
  `DashboardViewModel.WalletFeatureEnabled`/`RefreshWalletCommand`, `KeryxRpcJsonClient.
  GetBalanceByAddressAsync`/`GetUtxosByAddressesAsync`) is **public-address-only** (reads
  `MiningProfile.MiningAddress`, no private key/seed anywhere) and uses the **JSON** wRPC listener
  this app already manages. It is currently hidden (`WalletFeatureEnabled = false`) per the user's
  2026-08-04 request, pending this larger feature. It does not need to be removed once the full
  wallet lands — it's a reasonable "quick glance without launching another app" supplement, and it
  never conflicts with the real wallet's own balance/history view since both ultimately read the
  same on-chain state via the node.
- `docs/KERYX_RESEARCH.md` should get a §11 pointing at this file once the wallet's first release
  ships, so future passes don't have to re-derive the Borsh-vs-JSON port distinction from scratch.

## 5. Localization

Once step 4 above (Open Wallet button) lands, the strings involved are small (button label,
"point your node at --utxoindex + --rpclisten-borsh" explainer, an error message if the exe path
isn't set yet) - same `Str_*` pattern across all 7 `Strings.*.xaml` files as every other feature in
this app. The wallet's own UI (React/TypeScript, a separate process) has its own localization,
independent of this app's - not something this repo controls or should try to override.

## 6. Trigger to revisit

Check `https://github.com/Keryx-Labs/keryx-desktop-wallet/releases` periodically (or when the user
next asks about wallet integration) - the moment a real release exists, steps 1-4 in §2 become a
normal, bounded increment like any other binary this app already manages.
