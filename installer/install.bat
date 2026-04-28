@echo off
title HitPan ERP Install

echo.
echo  ============================================
echo   HitPan ERP Install
echo  ============================================
echo.

set INSTALL_DIR=%~dp0
set MYSQL_OK=0

:: ---- 1. MariaDB ----
echo [1/3] Checking MariaDB...

where mysql >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    set MYSQL_OK=1
    echo   - MariaDB found in PATH
    goto :dbsetup
)

if exist "C:\Program Files\MariaDB 11.4\bin\mysql.exe" (
    set "PATH=%PATH%;C:\Program Files\MariaDB 11.4\bin"
    set MYSQL_OK=1
    echo   - MariaDB 11.4 found
    goto :dbsetup
)

if exist "C:\Program Files\MariaDB 10.11\bin\mysql.exe" (
    set "PATH=%PATH%;C:\Program Files\MariaDB 10.11\bin"
    set MYSQL_OK=1
    echo   - MariaDB 10.11 found
    goto :dbsetup
)

if exist "C:\Program Files\MySQL\MySQL Server 8.4\bin\mysql.exe" (
    set "PATH=%PATH%;C:\Program Files\MySQL\MySQL Server 8.4\bin"
    set MYSQL_OK=1
    echo   - MySQL 8.4 found
    goto :dbsetup
)

echo.
echo   !! MariaDB not found. Downloading...
echo.
powershell -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://archive.mariadb.org/mariadb-11.4.4/winx64-packages/mariadb-11.4.4-winx64.msi' -OutFile '%INSTALL_DIR%mariadb.msi'"
if exist "%INSTALL_DIR%mariadb.msi" (
    echo   - Installing MariaDB...
    msiexec /i "%INSTALL_DIR%mariadb.msi" /quiet SERVICENAME=MariaDB PASSWORD=Hitpan2025!
    timeout /t 10 /nobreak >nul
    set "PATH=%PATH%;C:\Program Files\MariaDB 11.4\bin"
    set MYSQL_OK=1
    del "%INSTALL_DIR%mariadb.msi" >nul 2>&1
    echo   - MariaDB installed
) else (
    echo   !! Download failed.
    echo   !! Please install MariaDB manually: https://mariadb.org/download/
    echo   !! Then run this installer again.
    pause
    exit /b 1
)

:dbsetup
:: ---- 2. DB Setup ----
echo [2/3] Setting up database...

net start MariaDB >nul 2>&1
net start MySQL >nul 2>&1

:: MariaDB root password input
echo.
set /p ROOT_PW="  MariaDB root password: "
echo.

mysql -u root -p%ROOT_PW% -e "SELECT 1" >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo   !! Wrong password. Please try again.
    pause
    exit /b 1
)

mysql -u root -p%ROOT_PW% -e "CREATE DATABASE IF NOT EXISTS hitpan_erp CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;" >nul 2>&1
mysql -u root -p%ROOT_PW% -e "CREATE USER IF NOT EXISTS 'hitpan'@'localhost' IDENTIFIED BY 'Hitpan2025!'; GRANT ALL ON *.* TO 'hitpan'@'localhost'; FLUSH PRIVILEGES;" >nul 2>&1

:: Import schema + sample data
if exist "%INSTALL_DIR%hitpan_db.sql" (
    echo   - Importing schema and sample data...
    mysql -u hitpan -pHitpan2025! hitpan_erp < "%INSTALL_DIR%hitpan_db.sql" >nul 2>&1
    echo   - DB ready (200 items, 200 partners, 600+ transactions)
) else (
    echo   - Warning: hitpan_db.sql not found. Empty database.
)

:: ---- 3. Security Keys ----
echo [3.5/4] Generating security keys...
:: Generate random JWT secret and AES key per installation
for /f %%i in ('powershell -Command "[Convert]::ToBase64String((1..32|%%{Get-Random -Max 256}|%%{[byte]$_}))"') do set JWT_KEY=%%i
for /f %%i in ('powershell -Command "[Convert]::ToBase64String((1..32|%%{Get-Random -Max 256}|%%{[byte]$_}))"') do set AES_KEY=%%i

:: Save keys to config file (not in bat)
(
echo JWT_SECRET=%JWT_KEY%
echo ERP_ENCRYPTION_KEY=%AES_KEY%
) > "%INSTALL_DIR%hitpan-keys.conf"
echo   - Security keys generated (hitpan-keys.conf)

:: ---- 4. Desktop Shortcut ----
echo [3/3] Creating desktop shortcut...
powershell -ExecutionPolicy Bypass -Command "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut([Environment]::GetFolderPath('Desktop') + '\HitPan ERP.lnk'); $s.TargetPath = '%INSTALL_DIR%hitpan-start.bat'; $s.WorkingDirectory = '%INSTALL_DIR%'; $s.IconLocation = 'shell32.dll,21'; $s.Description = 'HitPan ERP'; $s.Save()" >nul 2>&1
echo   - Desktop shortcut created

echo.
echo  ============================================
echo   Install complete!
echo.
echo   Double-click [HitPan ERP] on your desktop.
echo   Login: tenant@hitpan.kr / Admin1234!
echo  ============================================
echo.
pause
