@echo off
title HitPan Tunnel Uninstall

echo.
echo  ============================================
echo   HitPan Tunnel Uninstall
echo  ============================================
echo.
echo  This removes the Cloudflare Tunnel service.
echo  Your HitPan ERP will continue to work locally.
echo.

set INSTALL_DIR=%~dp0

:: Check Admin Rights
net session >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo   !! Administrator rights required.
    echo   !! Right-click and "Run as administrator".
    pause
    exit /b 1
)

set /p CONFIRM="  Are you sure you want to uninstall the tunnel? (y/N): "
if /i not "%CONFIRM%"=="y" (
    echo   - Cancelled.
    pause
    exit /b 0
)

echo.
echo [1/2] Stopping and removing tunnel service...

if exist "%INSTALL_DIR%cloudflared.exe" (
    "%INSTALL_DIR%cloudflared.exe" service uninstall >nul 2>&1
    echo   - Service removed
) else (
    sc stop Cloudflared >nul 2>&1
    sc delete Cloudflared >nul 2>&1
    echo   - Service removed (cloudflared.exe missing, fallback)
)

echo [2/2] Cleaning up files...

if exist "%INSTALL_DIR%hitpan-tunnel.conf" (
    del /f /q "%INSTALL_DIR%hitpan-tunnel.conf" >nul 2>&1
    echo   - Token config removed
)

echo.
echo  ============================================
echo   Tunnel Uninstall Complete.
echo.
echo   HitPan ERP still works locally.
echo   To re-enable tunnel, run install-tunnel.bat
echo  ============================================
echo.
pause
