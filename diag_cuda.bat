@echo off
setlocal
set LOG=C:\Users\hambi\KRX_miner\KeryxNodeManager\diag_cuda.log
echo ==== %DATE% %TIME% ==== > "%LOG%"

echo ---- nvidia-smi ---- >> "%LOG%"
nvidia-smi >> "%LOG%" 2>&1

echo ---- nvidia-smi driver/CUDA version query ---- >> "%LOG%"
nvidia-smi --query-gpu=name,driver_version --format=csv >> "%LOG%" 2>&1

echo ---- CUDA toolkit install dirs ---- >> "%LOG%"
dir "C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA" >> "%LOG%" 2>&1

echo ---- searching for cudart64 DLLs on disk (system + toolkit + miner dir) ---- >> "%LOG%"
where cudart64_12.dll >> "%LOG%" 2>&1
dir /s /b "C:\Windows\System32\cudart64_12*.dll" >> "%LOG%" 2>&1
dir /s /b "C:\Program Files\NVIDIA GPU Computing Toolkit\cudart64_12*.dll" >> "%LOG%" 2>&1
dir /s /b "C:\Users\hambi\AppData\Local\KeryxNodeManager\bin\*.dll" >> "%LOG%" 2>&1

echo ---- PATH ---- >> "%LOG%"
echo %PATH% >> "%LOG%" 2>&1

echo ---- miner bin dir contents ---- >> "%LOG%"
dir "C:\Users\hambi\AppData\Local\KeryxNodeManager\bin" >> "%LOG%" 2>&1

echo DIAG_DONE >> "%LOG%"
