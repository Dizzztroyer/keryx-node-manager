#Requires -Version 5.1
<#
.SYNOPSIS
    Full release pipeline: publish -> portable ZIP -> installer -> checksums.
.DESCRIPTION
    Run this from the repo root on Windows with the .NET 8 SDK and (optionally) Inno Setup 6
    installed. Not yet executed for real - see docs/BUILD.md / PROJECT_STATUS.md.
#>

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

Write-Host "==> dotnet test"
dotnet test tests\KeryxNodeManager.Core.Tests\KeryxNodeManager.Core.Tests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw "Tests failed - aborting release build." }

Write-Host "==> dotnet publish (self-contained win-x64)"
dotnet publish src\KeryxNodeManager.App\KeryxNodeManager.App.csproj `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -o artifacts\publish\win-x64
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

Write-Host "==> packaging portable ZIP"
powershell -File scripts\package-portable.ps1

$innoCandidates = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    # winget install JRSoftware.InnoSetup installs per-user (no admin prompt) here rather than
    # Program Files - confirmed on the real dev machine, where neither Program Files path existed
    # after a successful winget install.
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$iscc = $innoCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($iscc) {
    Write-Host "==> building installer with $iscc"
    & $iscc "installer\KeryxNodeManager.iss"
} else {
    Write-Warning "Inno Setup 6 (ISCC.exe) not found - skipping installer build. Install it from https://jrsoftware.org/isinfo.php and re-run, or run ISCC.exe manually against installer\KeryxNodeManager.iss."
}

Write-Host "==> generating checksums.txt"
$artifacts = Get-ChildItem "artifacts" -File | Where-Object { $_.Extension -in ".exe", ".zip" }
$checksumLines = foreach ($file in $artifacts) {
    $hash = Get-FileHash -Path $file.FullName -Algorithm SHA256
    "$($hash.Hash.ToLower())  $($file.Name)"
}
$checksumLines | Set-Content -Path "artifacts\checksums.txt" -Encoding ASCII

Write-Host ""
Write-Host "Done. Contents of artifacts\:"
Get-ChildItem "artifacts" -File | Format-Table Name, Length -AutoSize
