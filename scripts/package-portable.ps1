#Requires -Version 5.1
<#
.SYNOPSIS
    Packages the self-contained win-x64 publish output into a portable ZIP under artifacts/.
.DESCRIPTION
    Expects `dotnet publish src\KeryxNodeManager.App\KeryxNodeManager.App.csproj -c Release
    -r win-x64 --self-contained true -o artifacts\publish\win-x64` to have already run
    (see docs/BUILD.md). Not yet executed in the sandbox this project was authored in - no
    Windows/PowerShell available there. Review before first real run.
#>

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$PublishDir = Join-Path $RepoRoot "artifacts\publish\win-x64"
$ArtifactsDir = Join-Path $RepoRoot "artifacts"

if (-not (Test-Path $PublishDir)) {
    throw "Publish output not found at $PublishDir. Run 'dotnet publish' first - see docs/BUILD.md."
}

$csprojPath = Join-Path $RepoRoot "src\KeryxNodeManager.App\KeryxNodeManager.App.csproj"
[xml]$csproj = Get-Content $csprojPath
$version = $csproj.Project.PropertyGroup.Version | Select-Object -First 1
if (-not $version) { $version = "0.0.0" }

$StagingDir = Join-Path $ArtifactsDir "portable-staging"
if (Test-Path $StagingDir) { Remove-Item $StagingDir -Recurse -Force }
New-Item -ItemType Directory -Path $StagingDir | Out-Null

Copy-Item -Path (Join-Path $PublishDir "*") -Destination $StagingDir -Recurse
Copy-Item -Path (Join-Path $RepoRoot "README.md") -Destination $StagingDir

$sampleConfig = @"
{
  "SchemaVersion": 1,
  "ActiveProfileName": "Default",
  "Language": "ru",
  "Theme": "dark",
  "Profiles": [
    {
      "Name": "Default",
      "MiningAddress": "",
      "NodeEndpoint": "127.0.0.1",
      "ModelsDirectory": ""
    }
  ]
}
"@
Set-Content -Path (Join-Path $StagingDir "settings.example.json") -Value $sampleConfig -Encoding UTF8

$firstRunNote = @"
Keryx Node Manager - portable build

Это portable-версия: установка не требуется, просто запустите KeryxNodeManager.exe.
Конфигурация всё равно сохраняется в %LocalAppData%\KeryxNodeManager\ (не рядом с exe),
чтобы несколько portable-копий не конфликтовали друг с другом.

Подробности - README.md и docs/USER_GUIDE_RU.md в исходном репозитории.
"@
Set-Content -Path (Join-Path $StagingDir "ПЕРВЫЙ_ЗАПУСК.txt") -Value $firstRunNote -Encoding UTF8

$zipPath = Join-Path $ArtifactsDir "KeryxNodeManager-Portable-$version.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

# Deliberately NOT using Compress-Archive here: it (Microsoft.PowerShell.Archive) does not set the
# UTF-8 language-encoding flag on zip entries, so non-ASCII entry names (e.g. this package's
# Cyrillic "ПЕРВЫЙ_ЗАПУСК.txt" first-run note) get written using the legacy IBM437/OEM codepage and
# come out corrupted on extraction (confirmed by extracting a real build and finding the file
# renamed to garbage bytes). System.IO.Compression.ZipFile (.NET's own zip writer) sets that flag
# correctly for any non-ASCII entry name, so every mainstream unzip tool (Explorer, 7-Zip,
# Expand-Archive) reads the name back correctly.
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $StagingDir, $zipPath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false)

Remove-Item $StagingDir -Recurse -Force

Write-Host "Portable ZIP created: $zipPath"
