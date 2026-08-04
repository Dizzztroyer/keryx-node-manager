@echo off
setlocal
set REPO=C:\Users\hambi\KRX_miner\KeryxNodeManager
set LOG=%REPO%\fix_and_push.log

echo ==== START %DATE% %TIME% ==== > "%LOG%"

if exist "%REPO%\.git\index.lock" del /f /q "%REPO%\.git\index.lock" >> "%LOG%" 2>&1
if exist "%REPO%\.git\HEAD.lock" del /f /q "%REPO%\.git\HEAD.lock" >> "%LOG%" 2>&1

cd /d "%REPO%"

echo ---- removing accidentally-tracked large binaries from working tree ---- >> "%LOG%"
if exist "%REPO%\keryx-llama.dll" del /f /q "%REPO%\keryx-llama.dll" >> "%LOG%" 2>&1
if exist "%REPO%\keryxcuda.dll" del /f /q "%REPO%\keryxcuda.dll" >> "%LOG%" 2>&1
if exist "%REPO%\_miner_update" rmdir /s /q "%REPO%\_miner_update" >> "%LOG%" 2>&1

echo ---- git status after reset ---- >> "%LOG%"
git status --short >> "%LOG%" 2>&1
git log --oneline -3 >> "%LOG%" 2>&1

echo ---- staging only the real fix ---- >> "%LOG%"
git add src\KeryxNodeManager.Core\Updates\BinaryUpdateService.cs >> "%LOG%" 2>&1
git add tests\KeryxNodeManager.Core.Tests\BinaryUpdateServiceTests.cs >> "%LOG%" 2>&1
git add deploy_updater_fix.bat fix_and_push.bat >> "%LOG%" 2>&1

echo ---- committing ---- >> "%LOG%"
git commit -m "0.2.7: fix updater dropping plugin DLLs - ApplyUpdate now copies sibling files (e.g. keryxcuda.dll, keryx-llama.dll) extracted alongside the exe, not just the exe itself, so a fresh user's own Install Update never leaves a newer binary paired with missing/stale plugins" >> "%LOG%" 2>&1

echo ---- pushing ---- >> "%LOG%"
git push >> "%LOG%" 2>&1

echo ---- final status ---- >> "%LOG%"
git status --short >> "%LOG%" 2>&1
git log --oneline -3 >> "%LOG%" 2>&1

echo DONE >> "%LOG%"
:end
echo ==== END %DATE% %TIME% ==== >> "%LOG%"
