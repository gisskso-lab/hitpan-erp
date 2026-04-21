@echo off
:: 히트판 ERP 중지 스크립트 (제거 시 호출)
taskkill /F /IM "HitPan.API.exe" >nul 2>&1
exit /b 0
