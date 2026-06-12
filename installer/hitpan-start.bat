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

:: Env — 사고 #33·#38·#46 봉합 (WS-20260612-01 2026-06-12)
::   사장님 결재 — 환경변수 영역 폐기 + ERP 본체 영역 TenantConfigReader 영역 db.conf 직접 영역
::   hitpan-start.bat 영역 = 사용자 영역 수동 영역 가도 영역 (schtasks 영역 미가도 영역 폴백)
::   사고 #38 봉합: 폴백 영역 `Hitpan2025!` 영역 제거 — 잘못된 영역 자격증명 영역 사용 영역 차단
::   db.conf 영역 0건 시 → 사용자 영역 직접 영역 봉합 박음 (안내 메시지 영역)
set DB_HOST=localhost
set DB_PORT=3306
set DOTNET_ENVIRONMENT=Production
set ASPNETCORE_ENVIRONMENT=Production
if exist "%BASE%db.conf" (
    for /f "usebackq tokens=1* delims==" %%a in ("%BASE%db.conf") do set %%a=%%b
) else (
    echo.
    echo  ============================================
    echo   [오류] db.conf 영역 0건
    echo   설치 영역 영역 완료되지 않았거나 영역 사고 발생.
    echo   본사 고객센터 영역 문의 영역 부탁드립니다.
    echo  ============================================
    echo.
    pause
    exit /b 1
)

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

:: 회사별 포트 영역 (사고 #34 봉합 — db.conf 영역 API_PORT 영역 박힘)
if "%API_PORT%"=="" set API_PORT=5257

:: Open URL — domain if available, else localhost (회사별 포트 영역 박음)
set OPEN_URL=http://localhost:%API_PORT%
if not "%PRIMARY_DOMAIN%"=="" if not "%PRIMARY_DOMAIN%"=="localhost:5234" set OPEN_URL=https://%PRIMARY_DOMAIN%

:: Start server
echo.
echo  ============================================
echo   HitPan ERP
echo   Local: http://localhost:%API_PORT%
if not "%OPEN_URL%"=="http://localhost:%API_PORT%" echo   Open : %OPEN_URL%
echo   Close this window to stop the server.
echo  ============================================
echo.

:: Open browser after 5 seconds
start "" cmd /c "timeout /t 5 /nobreak >nul && start %OPEN_URL%"

:: Run server (foreground - keeps window open) — 회사별 포트 영역 박음 (사고 #22 정합)
cd /d "%BASE%api"
HitPan.API.exe --urls http://0.0.0.0:%API_PORT%
