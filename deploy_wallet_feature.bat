@echo off
setlocal
set REPO=C:\Users\hambi\KRX_miner\KeryxNodeManager
set INSTALL=C:\Users\hambi\AppData\Local\Programs\KeryxNodeManager
set LOG=%REPO%\deploy5.log

echo ==== START %DATE% %TIME% ==== > "%LOG%"

if exist "%REPO%\.git\index.lock" del /f /q "%REPO%\.git\index.lock" >> "%LOG%" 2>&1
if exist "%REPO%\.git\HEAD.lock" del /f /q "%REPO%\.git\HEAD.lock" >> "%LOG%" 2>&1

cd /d "%REPO%"

echo ---- dotnet build ---- >> "%LOG%"
dotnet build -c Release >> "%LOG%" 2>&1
if errorlevel 1 (
  echo BUILD_FAILED >> "%LOG%"
  goto :end
)

echo ---- dotnet test ---- >> "%LOG%"
dotnet test tests\KeryxNodeManager.Core.Tests\KeryxNodeManager.Core.Tests.csproj -c Release >> "%LOG%" 2>&1
if errorlevel 1 (
  echo TESTS_FAILED >> "%LOG%"
  goto :end
)

echo ---- git status before commit ---- >> "%LOG%"
git status --short >> "%LOG%" 2>&1

echo ---- git add/commit/push ---- >> "%LOG%"
git add -A >> "%LOG%" 2>&1
git commit -m "0.2.7: add public-address wallet balance + recent unspent outputs to Dashboard (getBalanceByAddress/getUtxosByAddresses RPC, --utxoindex); no private key/seed ever touched" >> "%LOG%" 2>&1
git push >> "%LOG%" 2>&1

echo ---- dotnet publish (self-contained win-x64) ---- >> "%LOG%"
dotnet publish src\KeryxNodeManager.App\KeryxNodeManager.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o artifacts\publish\win-x64 >> "%LOG%" 2>&1
if errorlevel 1 (
  echo PUBLISH_FAILED >> "%LOG%"
  goto :end
)

echo ---- stopping old KeryxNodeManager.exe (UI only) ---- >> "%LOG%"
taskkill /IM KeryxNodeManager.exe /F >> "%LOG%" 2>&1
timeout /t 2 /nobreak >nul

echo ---- robocopy into real install directory ---- >> "%LOG%"
robocopy artifacts\publish\win-x64 "%INSTALL%" /E /XO /R:2 /W:1 /NFL /NDL /NJH >> "%LOG%" 2>&1

echo ---- relaunching updated app ---- >> "%LOG%"
start "" "%INSTALL%\KeryxNodeManager.exe"

echo DEPLOY_DONE >> "%LOG%"
:end
echo ==== END %DATE% %TIME% ==== >> "%LOG%"
