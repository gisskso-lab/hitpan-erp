; ============================================================
; HitPan ERP — 원클릭 통합 인스톨러 (Inno Setup 6.x)
; WS-20260428-02 — 옵션 X2 + Gard 4개
;
; 빌드 방법:
;   build-installer.ps1 -TenantId tenant-001 -Token "eyJh..."
;
; 또는 직접:
;   ISCC.exe HitPan.iss /DTunnelToken="eyJh..." /DTenantId="tenant-001"
;
; 사장님 헌법 부합:
;   §18 본사 데이터 경계: 인스톨러 = 고객 PC 셋업, 본사 미수신
;   §19 errors 0 / warnings 0
;   §20 워크플로우 끊김 금지: 4축 무결성 0건 영향
;   잘 되는 거 안 건드림: 기존 install.bat 유지, 별도 .iss 추가
;   쉬워야 한다: 더블클릭 1번 + 다음 클릭
; ============================================================

; ─── 빌드 시점 주입 변수 (Gard 1) ─────────────────────────────
#ifndef AppVersion
  #define AppVersion "1.0.7"
#endif

#ifndef TunnelToken
  #define TunnelToken ""
#endif

#ifndef TenantId
  #define TenantId "tenant-default"
#endif

#ifndef OutputName
  #define OutputName "HitPan-Setup-" + TenantId
#endif

#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#ifndef BundleDir
  #define BundleDir "..\installer-build\bundle"
#endif

; ─── 메타정보 ────────────────────────────────────────────────
[Setup]
AppId={{F4E2A1D0-7BC8-4F31-9E5A-6D8C4B1E2F35}
AppName=HitPan ERP
AppVersion={#AppVersion}
AppPublisher=HitPan
AppPublisherURL=https://hitpan.app
AppSupportURL=https://hitpan.app/support
DefaultDirName={autopf}\HitPan
DefaultGroupName=HitPan ERP
DisableProgramGroupPage=yes
LicenseFile=
PrivilegesRequired=admin
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=120
ShowLanguageDialog=no
LanguageDetectionMethod=none
DisableWelcomePage=no
DisableReadyPage=no
UsePreviousAppDir=yes
SetupLogging=yes
ChangesEnvironment=no
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

; ─── 한국어 ──────────────────────────────────────────────────
[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

; ─── 사용자 정의 메시지 ──────────────────────────────────────
[Messages]
korean.WelcomeLabel1=히트판 ERP 설치 마법사
korean.WelcomeLabel2=히트판 ERP 베타 버전을 이 PC에 설치합니다.%n%n설치되는 항목:%n  · MariaDB 11.4 데이터베이스%n  · .NET 8 런타임%n  · 히트판 ERP 본체%n  · Cloudflare 보안 터널%n%n설치 도중 인터넷 연결이 필요하지 않습니다.%n%n사장님 헌법 §18 부합: 고객사 데이터는 본사로 절대 전송되지 않습니다.%n%n계속하려면 [다음]을 클릭하세요.
korean.FinishedLabel=히트판 ERP가 설치되었습니다.%n%n바탕화면의 [HitPan ERP] 아이콘을 더블클릭하여 시작하세요.%n%n기본 로그인:%n  계정: tenant@hitpan.kr%n  비밀번호: Admin1234!

; ─── 설치 파일 (의존성 번들) ─────────────────────────────────
; ※ BundleDir 안에 의존성들이 미리 다운로드되어 있어야 함
; ※ build-installer.ps1 이 자동으로 캐시 다운로드
[Files]
; .NET 8 ASP.NET Core Hosting Bundle
Source: "{#BundleDir}\dotnet-hosting.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall ignoreversion; Check: NeedsDotNet

; MariaDB 11.4 MSI
Source: "{#BundleDir}\mariadb.msi"; DestDir: "{tmp}"; Flags: deleteafterinstall ignoreversion; Check: NeedsMariaDB

; Visual C++ Redistributable
Source: "{#BundleDir}\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall ignoreversion; Check: NeedsVCRedist

; cloudflared
Source: "{#BundleDir}\cloudflared.exe"; DestDir: "{app}"; Flags: ignoreversion

; HitPan ERP 산출물
Source: "{#BundleDir}\api\*"; DestDir: "{app}\api"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#BundleDir}\web\*"; DestDir: "{app}\web"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#BundleDir}\hitpan_db.sql"; DestDir: "{app}"; Flags: ignoreversion

; 시작·헬퍼 스크립트 (기존 인스톨러 자산 재활용)
Source: "hitpan-start.bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "web-server.ps1"; DestDir: "{app}"; Flags: ignoreversion

; ─── 데스크톱·시작메뉴 아이콘 ────────────────────────────────
[Icons]
Name: "{group}\HitPan ERP"; Filename: "{app}\hitpan-start.bat"; WorkingDir: "{app}"; IconFilename: "{sys}\shell32.dll"; IconIndex: 21; Comment: "히트판 ERP 시작"
Name: "{group}\HitPan ERP 종료"; Filename: "{app}\stop-hitpan.bat"; WorkingDir: "{app}"; Comment: "히트판 ERP 종료"
Name: "{commondesktop}\HitPan ERP"; Filename: "{app}\hitpan-start.bat"; WorkingDir: "{app}"; IconFilename: "{sys}\shell32.dll"; IconIndex: 21

; ─── 설치 후 자동 실행 (선택) ────────────────────────────────
[Run]
; 1. .NET 8 Hosting Bundle (조건부)
Filename: "{tmp}\dotnet-hosting.exe"; Parameters: "/quiet /norestart"; StatusMsg: ".NET 8 런타임 설치 중..."; Check: NeedsDotNet; Flags: waituntilterminated

; 2. Visual C++ Redist (조건부)
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/quiet /norestart"; StatusMsg: "Visual C++ 런타임 설치 중..."; Check: NeedsVCRedist; Flags: waituntilterminated

; 3. MariaDB (조건부, silent install + 랜덤 root 비밀번호)
;    Code 섹션에서 별도 처리 (랜덤 비밀번호 생성 필요)

; 4. ERP 시작 (체크박스 옵션)
Filename: "{app}\hitpan-start.bat"; Description: "히트판 ERP 지금 시작"; Flags: postinstall nowait skipifsilent unchecked

; ─── 제거 시 ─────────────────────────────────────────────────
[UninstallRun]
; cloudflared 서비스 제거
Filename: "{app}\cloudflared.exe"; Parameters: "service uninstall"; Flags: runhidden; RunOnceId: "RemoveTunnelService"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\hitpan-keys.conf"
Type: filesandordirs; Name: "{app}\hitpan-tunnel.conf"
Type: filesandordirs; Name: "{app}\tenant.conf"

; ─── 코드 섹션 ───────────────────────────────────────────────
[Code]

// ─── 의존성 감지 ──────────────────────────────────────────
function NeedsDotNet: Boolean;
var
  Version: String;
begin
  // .NET 8 Hosting Bundle 설치 확인
  Result := True;
  if RegQueryStringValue(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.AspNetCore.App',
                         '8.0.0', Version) then
    Result := False;
  if RegKeyExists(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost') then
    Result := False;
end;

function NeedsVCRedist: Boolean;
var
  Installed: Cardinal;
begin
  // VC++ 2015~2022 x64 확인
  Result := True;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64',
                        'Installed', Installed) then
  begin
    if Installed = 1 then Result := False;
  end;
end;

function NeedsMariaDB: Boolean;
begin
  // MariaDB 11.4 또는 10.11 또는 MySQL 8.x 확인
  Result := True;
  if FileExists('C:\Program Files\MariaDB 11.4\bin\mysql.exe') then Result := False;
  if FileExists('C:\Program Files\MariaDB 10.11\bin\mysql.exe') then Result := False;
  if FileExists('C:\Program Files\MySQL\MySQL Server 8.4\bin\mysql.exe') then Result := False;
end;

// ─── 랜덤 문자열 생성 (32자 base64) ───────────────────────
function GenerateRandomKey(Bytes: Integer): String;
var
  PsScript: String;
  ResultCode: Integer;
  TempFile: String;
  Lines: TArrayOfString;
begin
  TempFile := ExpandConstant('{tmp}\randkey.txt');
  PsScript := Format(
    '[Convert]::ToBase64String((1..%d|%%{Get-Random -Max 256}|%%{[byte]$_})) | Out-File -Encoding ASCII -NoNewline "%s"',
    [Bytes, TempFile]);

  Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -Command "' + PsScript + '"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if (ResultCode = 0) and FileExists(TempFile) then
  begin
    if LoadStringsFromFile(TempFile, Lines) and (GetArrayLength(Lines) > 0) then
      Result := Lines[0]
    else
      Result := '';
    DeleteFile(TempFile);
  end
  else
    Result := '';
end;

// ─── 보안 키 + 토큰 + DB 셋업 (설치 후 실행) ──────────────
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  JwtKey, AesKey, MariaRootPw: String;
  ConfFile, BatchFile: String;
  TenantConfPath: String;
  KeysContent, BatchContent: TStringList;
begin
  if CurStep = ssPostInstall then
  begin
    // 1. 보안 키 생성 (JWT, AES)
    JwtKey := GenerateRandomKey(32);
    AesKey := GenerateRandomKey(32);
    MariaRootPw := GenerateRandomKey(24);

    // 2. hitpan-keys.conf 작성 (관리자 권한만 읽기)
    ConfFile := ExpandConstant('{app}\hitpan-keys.conf');
    KeysContent := TStringList.Create;
    try
      KeysContent.Add('JWT_SECRET=' + JwtKey);
      KeysContent.Add('ERP_ENCRYPTION_KEY=' + AesKey);
      KeysContent.Add('MARIADB_ROOT_PW=' + MariaRootPw);
      KeysContent.Add('INSTALLED_AT=' + GetDateTimeString('yyyy/mm/dd hh:nn:ss', '-', ':'));
      KeysContent.SaveToFile(ConfFile);
    finally
      KeysContent.Free;
    end;

    // ICACLS로 Administrators/SYSTEM only
    Exec(ExpandConstant('{cmd}'),
         '/C icacls "' + ConfFile + '" /inheritance:r /grant:r "Administrators:F" /grant:r "SYSTEM:F"',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // 3. tenant_id 박음 (Gard 2)
    TenantConfPath := ExpandConstant('{app}\tenant.conf');
    SaveStringToFile(TenantConfPath, 'TENANT_ID={#TenantId}', False);

    // 4. MariaDB silent install (NeedsMariaDB이면)
    if NeedsMariaDB then
    begin
      Exec('msiexec.exe',
           Format('/i "%s\mariadb.msi" /quiet SERVICENAME=MariaDB PASSWORD=%s',
                  [ExpandConstant('{tmp}'), MariaRootPw]),
           '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Sleep(10000);  // 서비스 시작 대기
    end;

    // 5. DB 셋업 배치 파일 작성 + 실행
    BatchFile := ExpandConstant('{tmp}\db-setup.bat');
    BatchContent := TStringList.Create;
    try
      BatchContent.Add('@echo off');
      BatchContent.Add('set "PATH=%PATH%;C:\Program Files\MariaDB 11.4\bin;C:\Program Files\MariaDB 10.11\bin;C:\Program Files\MySQL\MySQL Server 8.4\bin"');
      BatchContent.Add(Format('mysql -u root -p%s -e "CREATE DATABASE IF NOT EXISTS hitpan_erp CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"', [MariaRootPw]));
      BatchContent.Add(Format('mysql -u root -p%s -e "CREATE USER IF NOT EXISTS ''hitpan''@''localhost'' IDENTIFIED BY ''Hitpan2025!''; GRANT ALL ON *.* TO ''hitpan''@''localhost''; FLUSH PRIVILEGES;"', [MariaRootPw]));
      BatchContent.Add('mysql -u hitpan -pHitpan2025! hitpan_erp < "' + ExpandConstant('{app}\hitpan_db.sql') + '"');
      BatchContent.SaveToFile(BatchFile);
    finally
      BatchContent.Free;
    end;

    Exec(ExpandConstant('{cmd}'), '/C "' + BatchFile + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    DeleteFile(BatchFile);

    // 6. cloudflared Windows 서비스 등록 (토큰 사전 주입 — Gard 1)
    if '{#TunnelToken}' <> '' then
    begin
      Exec(ExpandConstant('{app}\cloudflared.exe'),
           'service install {#TunnelToken}',
           ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode);

      // 토큰 사후 보관 (참조용, 권한 잠금)
      SaveStringToFile(ExpandConstant('{app}\hitpan-tunnel.conf'),
                       'CLOUDFLARED_TOKEN={#TunnelToken}'#13#10 +
                       'TENANT_ID={#TenantId}'#13#10 +
                       'INSTALLED_AT=' + GetDateTimeString('yyyy/mm/dd hh:nn:ss', '-', ':'),
                       False);
      Exec(ExpandConstant('{cmd}'),
           '/C icacls "' + ExpandConstant('{app}\hitpan-tunnel.conf') + '" /inheritance:r /grant:r "Administrators:F" /grant:r "SYSTEM:F"',
           '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
  end;
end;

// ─── 설치 시작 전 — 관리자 권한 재확인 ─────────────────────
function InitializeSetup(): Boolean;
begin
  Result := True;
  if not IsAdminInstallMode then
  begin
    MsgBox('관리자 권한이 필요합니다.'#13#10'설치 파일을 우클릭 → "관리자 권한으로 실행"하세요.',
           mbError, MB_OK);
    Result := False;
  end;
end;
