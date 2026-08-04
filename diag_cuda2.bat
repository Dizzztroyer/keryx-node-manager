@echo off
setlocal
set LOG=C:\Users\hambi\KRX_miner\KeryxNodeManager\diag_cuda2.log
echo ==== %DATE% %TIME% ==== > "%LOG%"

echo ---- full CUDA v12.6 bin listing ---- >> "%LOG%"
dir "C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.6\bin" >> "%LOG%" 2>&1

echo ---- checking specific runtime DLLs the miner may dlopen ---- >> "%LOG%"
for %%F in (cudart64_12.dll cublas64_12.dll cublasLt64_12.dll curand64_10.dll nvJitLink_120_0.dll nvrtc64_120_0.dll cudnn64_9.dll) do (
  if exist "C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.6\bin\%%F" (
    echo FOUND: %%F >> "%LOG%"
  ) else (
    echo MISSING: %%F >> "%LOG%"
  )
)

echo DIAG2_DONE >> "%LOG%"
