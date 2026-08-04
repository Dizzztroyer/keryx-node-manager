@echo off
setlocal
set LOG=C:\Users\hambi\KRX_miner\KeryxNodeManager\restart_app.log
echo ==== %DATE% %TIME% ==== > "%LOG%"
taskkill /IM KeryxNodeManager.exe /F >> "%LOG%" 2>&1
timeout /t 2 /nobreak >nul
start "" "C:\Users\hambi\AppData\Local\Programs\KeryxNodeManager\KeryxNodeManager.exe"
echo RESTART_DONE >> "%LOG%"
