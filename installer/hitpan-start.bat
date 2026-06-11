@echo off
title HitPan ERP
chcp 65001 >nul

echo.
echo  HitPan ERP Starting...
echo.

set BASE=%~dp0

:: MariaDB
if exist "C:\Program Files\MariaDB 11.4\bin" set "PATH=%PATH%;C:\Program Files\MariaDB 11.4\bin"
if exist "C:\Program Files\MariaDB 10.11\bin" set "PATH=%PATH%;C:\Program Files\MariaDB 10.11\bin"
net start MariaDB >nul 2>&1
net start MySQL >nul 2>&1

:: Env
set DB_NAME=hitpan_erp
set DB_USER=hitpan
set DB_PASSWORD=Hitpan2025!
set DB_HOST=localhost
set DB_PORT=3306
set DOTNET_ENVIRONMENT=Production
set ASPNETCORE_ENVIRONMENT=Production

:: Security keys — Base64 padding(=) 영역 보존 위해 첫 = 영역만 split
if exist "%BASE%hitpan-keys.conf" (
    for /f "usebackq tokens=1* delims==" %%a in ("%BASE%hitpan-keys.conf") do set %%a=%%b
) else (
    set JWT_SECRET=hitpan-jwt-secret-key-32chars-min!
    set ERP_ENCRYPTION_KEY=hitpan-aes-key-32bytes-exactly!!
)

:: Bootstrap (tenant + domain) — 첫 = 영역만 split (도메인·회사명 영역 보존)
set PRIMARY_DOMAIN=localhost:5234
if exist "%BASE%bootstrap.conf" (
    for /f "usebackq tokens=1* delims==" %%a in ("%BASE%bootstrap.conf") do set %%a=%%b
)

:: Open URL — domain if available, else localhost
set OPEN_URL=http://localhost:5234
if not "%PRIMARY_DOMAIN%"=="" if not "%PRIMARY_DOMAIN%"=="localhost:5234" set OPEN_URL=https://%PRIMARY_DOMAIN%

:: Start server
echo.
echo  ============================================
echo   HitPan ERP
echo   Local: http://localhost:5234
if not "%OPEN_URL%"=="http://localhost:5234" echo   Open : %OPEN_URL%
echo   Close this window to stop the server.
echo  ============================================
echo.

:: Open browser after 5 seconds
start "" cmd /c "timeout /t 5 /nobreak >nul && start %OPEN_URL%"

:: Run server (foreground - keeps window open)
cd /d "%BASE%api"
HitPan.API.exe --urls http://0.0.0.0:5234
