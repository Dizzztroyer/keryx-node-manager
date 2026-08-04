@echo off
setlocal
set REPO=C:\Users\hambi\KRX_miner\KeryxNodeManager
set LOG=%REPO%\fix_and_push2.log

echo ==== START %DATE% %TIME% ==== > "%LOG%"

if exist "%REPO%\.git\index.lock" del /f /q "%REPO%\.git\index.lock" >> "%LOG%" 2>&1
if exist "%REPO%\.git\HEAD.lock" del /f /q "%REPO%\.git\HEAD.lock" >> "%LOG%" 2>&1

cd /d "%REPO%"

echo ---- undo the bad commit (it was never accepted by GitHub, safe to drop) ---- >> "%LOG%"
git reset --soft 50a6606 >> "%LOG%" 2>&1

echo ---- unstage the large binaries entirely (already removed from disk) ---- >> "%LOG%"
git reset -- _miner_update/cublas64_12.dll _miner_update/cublasLt64_12.dll keryx-llama.dll keryxcuda.dll >> "%LOG%" 2>&1

echo ---- add a .gitignore rule so this can never happen again ---- >> "%LOG%"
echo. >> .gitignore
echo # Large third-party binaries that sometimes get dropped into the repo root during >> .gitignore
echo # manual diagnostics/testing - these must never be committed (GitHub rejects files >> .gitignore
echo # over 100MB, and they don't belong in source control anyway). >> .gitignore
echo *.dll >> .gitignore
echo _miner_update/ >> .gitignore
git add .gitignore >> "%LOG%" 2>&1

echo ---- staging only the real fix ---- >> "%LOG%"
git add src\KeryxNodeManager.Core\Updates\BinaryUpdateService.cs >> "%LOG%" 2>&1
git add tests\KeryxNodeManager.Core.Tests\BinaryUpdateServiceTests.cs >> "%LOG%" 2>&1
git add deploy_updater_fix.bat fix_and_push.bat fix_and_push2.bat >> "%LOG%" 2>&1

echo ---- status before commit ---- >> "%LOG%"
git status --short >> "%LOG%" 2>&1

echo ---- committing ---- >> "%LOG%"
git commit -m "0.2.7: fix updater dropping plugin DLLs on install; gitignore *.dll to stop large binaries from ever being committed again" >> "%LOG%" 2>&1

echo ---- pushing ---- >> "%LOG%"
git push >> "%LOG%" 2>&1

echo ---- final status ---- >> "%LOG%"
git status --short >> "%LOG%" 2>&1
git log --oneline -3 >> "%LOG%" 2>&1

echo DONE >> "%LOG%"
:end
echo ==== END %DATE% %TIME% ==== >> "%LOG%"
