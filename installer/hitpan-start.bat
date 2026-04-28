@echo off
title HitPan ERP

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
set DOTNET_ENVIRONMENT=Development
set ASPNETCORE_ENVIRONMENT=Development

:: Security keys (from config file or fallback)
if exist "%BASE%hitpan-keys.conf" (
    for /f "tokens=1,2 delims==" %%a in (%BASE%hitpan-keys.conf) do set %%a=%%b
) else (
    set JWT_SECRET=hitpan-jwt-secret-key-32chars-min!
    set ERP_ENCRYPTION_KEY=hitpan-aes-key-32bytes-exactly!!
)

:: Start server
echo  Starting server on port 5234...
echo.
echo  ============================================
echo   HitPan ERP
echo   http://localhost:5234
echo   Login: tenant@hitpan.kr / Admin1234!
echo   Close this window to stop the server.
echo  ============================================
echo.

:: Open browser after 5 seconds
start "" cmd /c "timeout /t 5 /nobreak >nul && start http://localhost:5234"

:: Run server (foreground - keeps window open)
cd /d "%BASE%api"
HitPan.API.exe --urls http://0.0.0.0:5234
