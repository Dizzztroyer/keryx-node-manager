@echo off
setlocal
set LOG=C:\Users\hambi\KRX_miner\KeryxNodeManager\diag_miner_help.log
echo ==== %DATE% %TIME% ==== > "%LOG%"
cd /d "C:\Users\hambi\AppData\Local\KeryxNodeManager\bin"
echo ---- --version ---- >> "%LOG%"
keryx-miner.exe --version >> "%LOG%" 2>&1
echo ---- --help ---- >> "%LOG%"
keryx-miner.exe --help >> "%LOG%" 2>&1
echo DIAG3_DONE >> "%LOG%"
