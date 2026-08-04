@echo off
setlocal
set REPO=C:\Users\hambi\KRX_miner\KeryxNodeManager
set BIN=C:\Users\hambi\AppData\Local\KeryxNodeManager\bin
set LOG=%REPO%\install_miner_plugins.log

echo ==== %DATE% %TIME% ==== > "%LOG%"

echo ---- stopping KeryxNodeManager.exe so keryx-miner.exe file handles are free ---- >> "%LOG%"
taskkill /IM KeryxNodeManager.exe /F >> "%LOG%" 2>&1
taskkill /IM keryx-miner.exe /F >> "%LOG%" 2>&1
timeout /t 2 /nobreak >nul

echo ---- copying official plugin files into the real miner bin directory ---- >> "%LOG%"
copy /Y "%REPO%\keryxcuda.dll" "%BIN%\keryxcuda.dll" >> "%LOG%" 2>&1
copy /Y "%REPO%\keryx-llama.dll" "%BIN%\keryx-llama.dll" >> "%LOG%" 2>&1

echo ---- listing bin dir after copy ---- >> "%LOG%"
dir "%BIN%" >> "%LOG%" 2>&1

echo ---- relaunching KeryxNodeManager ---- >> "%LOG%"
start "" "C:\Users\hambi\AppData\Local\Programs\KeryxNodeManager\KeryxNodeManager.exe"

echo INSTALL_DONE >> "%LOG%"
