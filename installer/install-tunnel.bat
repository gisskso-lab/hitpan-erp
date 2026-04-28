@echo off
title HitPan Tunnel Setup (v1.0.7)

echo.
echo  ============================================
echo   HitPan Tunnel Setup
echo   (Cloudflare Tunnel for Beta)
echo  ============================================
echo.
echo  This script installs cloudflared as a Windows service
echo  to enable secure remote access to your HitPan ERP.
echo.
echo  Your data stays on YOUR PC. Cloudflare only
echo  passes traffic — never stores ERP data.
echo  (Per HitPan Constitution Section 18)
echo.

set INSTALL_DIR=%~dp0
set CLOUDFLARED_VERSION=2024.10.0
set CLOUDFLARED_URL=https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe

:: ---- 1. Check Admin Rights ----
net session >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo   !! Administrator rights required.
    echo   !! Right-click this file and select "Run as administrator".
    echo.
    pause
    exit /b 1
)

:: ---- 2. Get Token from User ----
echo [1/4] Tunnel token input
echo.
echo  Paste the Cloudflare Tunnel token provided by HitPan support.
echo  (It looks like: eyJhXXXX...XXXX, very long)
echo.
set /p TUNNEL_TOKEN="  Token: "

if "%TUNNEL_TOKEN%"=="" (
    echo   !! Token is empty. Aborting.
    pause
    exit /b 1
)

:: Basic format validation (should start with eyJ)
echo %TUNNEL_TOKEN% | findstr /B "eyJ" >nul
if %ERRORLEVEL% NEQ 0 (
    echo   !! Token format looks wrong. It should start with "eyJ".
    echo   !! Please verify with HitPan support and try again.
    pause
    exit /b 1
)

:: ---- 3. Download cloudflared ----
echo.
echo [2/4] Downloading cloudflared...

if exist "%INSTALL_DIR%cloudflared.exe" (
    echo   - cloudflared already exists, skipping download
) else (
    powershell -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri '%CLOUDFLARED_URL%' -OutFile '%INSTALL_DIR%cloudflared.exe'"
    if not exist "%INSTALL_DIR%cloudflared.exe" (
        echo   !! Download failed. Check internet connection.
        pause
        exit /b 1
    )
    echo   - cloudflared downloaded
)

:: ---- 4. Install as Windows Service ----
echo [3/4] Installing as Windows service...

:: Stop existing service if any
sc query Cloudflared >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo   - Existing tunnel service found, removing...
    "%INSTALL_DIR%cloudflared.exe" service uninstall >nul 2>&1
    timeout /t 2 /nobreak >nul
)

:: Install service with token
"%INSTALL_DIR%cloudflared.exe" service install %TUNNEL_TOKEN%
if %ERRORLEVEL% NEQ 0 (
    echo   !! Service installation failed.
    echo   !! Common causes:
    echo   !! - Wrong token (verify with HitPan support)
    echo   !! - Internet blocked (firewall/proxy)
    echo   !! - cloudflared.exe corrupted (re-download)
    pause
    exit /b 1
)

:: Save token securely (admin-readable only)
(
echo CLOUDFLARED_TOKEN=%TUNNEL_TOKEN%
echo INSTALLED_AT=%DATE% %TIME%
) > "%INSTALL_DIR%hitpan-tunnel.conf"
icacls "%INSTALL_DIR%hitpan-tunnel.conf" /inheritance:r /grant:r "Administrators:F" /grant:r "SYSTEM:F" >nul 2>&1

echo   - Tunnel service installed and configured

:: ---- 5. Health Check ----
echo [4/4] Health check...

timeout /t 5 /nobreak >nul

sc query Cloudflared | findstr "RUNNING" >nul
if %ERRORLEVEL% EQU 0 (
    echo   - Tunnel service is RUNNING
) else (
    echo   !! Tunnel service is not running. Manual check needed.
    echo   !! Run: sc query Cloudflared
)

echo.
echo  ============================================
echo   Tunnel Setup Complete!
echo.
echo   Your HitPan ERP is now accessible securely
echo   via the Cloudflare Tunnel.
echo.
echo   The tunnel auto-starts on PC reboot.
echo   No further action needed.
echo.
echo   For support: contact HitPan
echo  ============================================
echo.
pause
