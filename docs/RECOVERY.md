# Recovery — resuming development after a context/tooling loss

If you're picking this project back up after a dropped session, a build failure, or a partial
refactor, read in this order:

1. `PROJECT_STATUS.md` — the authoritative "what's done / what's next" snapshot. Trust this over
   memory of a previous conversation; it's updated at the end of each work session.
2. `docs/KERYX_RESEARCH.md` — verified facts about Keryx's actual CLI/behavior. Don't re-derive
   these from scratch; they were pulled from cloned source at a specific commit and re-verified
   once (see the two correction passes noted inline — appdir path, bridge component). If Keryx
   itself has since changed, re-clone and diff before trusting old numbers.
3. `docs/ARCHITECTURE.md` — why NativeWindowsRuntimeBackend is primary (not WSL2), why models are
   app-managed, module map.

## If the build is broken

This was last verified to build clean with:

```
dotnet build src/KeryxNodeManager.Core/KeryxNodeManager.Core.csproj -c Release
dotnet test tests/KeryxNodeManager.Core.Tests/KeryxNodeManager.Core.Tests.csproj -c Release
dotnet build src/KeryxNodeManager.App/KeryxNodeManager.App.csproj -c Release -p:EnableWindowsTargeting=true
```

(the last one works even on Linux/CI for compile-checking the WPF project; it does not produce a
runnable `.exe` off Windows). If any of these fail on a fresh clone, suspect: a `.csproj` edit that
introduced a bad `ProjectReference` path, a namespace collision (this bit us once already — see
`Gpu/NvidiaSmiGpuInfoProvider.cs`'s explicit `System.Diagnostics.Process` qualification, needed
because `KeryxNodeManager.Core.Process` is also a namespace in this codebase and C#'s
enclosing-namespace lookup prefers it over a `using System.Diagnostics;` for the bare name
`Process`), or a missing NuGet restore (`dotnet restore` first).

## If a refactor was left half-done

Check `git status`/`git diff` before assuming any given file is "the current state" — this
project's convention (per its own brief) is small, logical commits at named checkpoints
(architecture, GPU monitoring, config, dashboard, models, tray, autostart, diagnostics, installer,
tests, release). If you land mid-refactor with no commit, prefer reverting to the last commit and
re-applying intent from `PROJECT_STATUS.md`'s "In progress" section rather than guessing at
half-written code.

## If the sandbox/toolchain is gone (no .NET SDK, no network)

The `.NET 8 SDK` used to verify this project was fetched directly from
`https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.423/dotnet-sdk-8.0.423-linux-x64.tar.gz` via
plain `curl` (the `dotnet-install.sh` script's own retry logic failed repeatedly in this sandbox
for unclear reasons — a direct `curl -L -C -` download of the same URL succeeded on the first
try). If `dotnet-install.sh` is failing again, try a direct download of the SDK tarball for your
platform instead of the installer script. Also: this sandbox's default `$TMPDIR`/`$HOME` pointed at
a filesystem that filled up mid-build (`No space left on device`) even though `df` showed room
elsewhere — redirecting `TMPDIR`, `HOME`, `DOTNET_CLI_HOME`, and `NUGET_PACKAGES` to a roomier
mount fixed it. A second, unrelated issue: NuGet's atomic-write-then-rename during restore failed
with "Operation not permitted" when the project lived on one particular mounted path — copying the
whole solution to a plain local filesystem path resolved it. Neither of these is a code problem;
both are sandbox-specific filesystem quirks worth remembering if they recur.
