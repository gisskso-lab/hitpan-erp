; ==============================================================
;  HitPan ERP One-Click Installer (Inno Setup 6)  v1.0.7
;
;  CHANGES v1.0.6 -> v1.0.7  (작업지시서 20260425작3)
;  - install-setup.ps1 -> install-setup-v107.ps1
;    · 4/23 대리점 망신 원인 차단: S1/S2/S3 시나리오 자동 분기
;    · S2: 기존 MariaDB + 모르는 root 비번 -> WinForms 다이얼로그 1회 입력
;    · S3: hitpan 계정 살아있음 -> root 단계 + 스키마 import 전부 스킵
;          (.env / 키 / logs 보존)
;    · root 비번은 메모리에서 즉시 폐기, 디스크 미기록
;    · PROV_BASE_URL 환경변수 추가 (베타: hitpan-prov.workers.dev)
;
;  - 안내 다이얼로그 갱신: S1~S3 시나리오 안내 + Cloudflare Tunnel 예고
;
;  - cloudflared 번들·prov 등록 흐름은 v1.0.8에서 추가 (마커스 리 작업)
; ==============================================================

#define MyAppName "히트판 ERP"
#define MyAppNameEn "HitPan ERP"
#define MyAppVersion "1.0.7"
#define MyAppPublisher "HitPan"
#define MyAppURL "https://hitpan.kr"
#define MyAppExeName "HitPan.API.exe"

[Setup]
AppId={{B7F4A5C8-9D3E-4F21-A6B5-C8D2E9F0A1B4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName=C:\HitPan
DefaultGroupName={#MyAppName}
DisableDirPage=no
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=히트판_ERP_설치_v{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayName={#MyAppName}
SetupLogging=yes
ShowLanguageDialog=no
DisableReadyPage=no
DisableFinishedPage=no

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Files]
; ERP core (API + Blazor wwwroot merged)
Source: "hitpan\*"; DestDir: "{app}\hitpan"; Flags: ignoreversion recursesubdirs createallsubdirs

; DB schema + 6-tenant sample dump (~59MB)
Source: "hitpan_db.sql"; DestDir: "{app}"; Flags: ignoreversion

; MariaDB MSI (bundled, /passive)
Source: "prereqs\mariadb.msi"; DestDir: "{tmp}"; Flags: deleteafterinstall

; Launcher scripts
;   hitpan-launcher.vbs    : desktop icon target (hidden window, ASCII-only)
;   start-hitpan.ps1       : actual launcher logic (loopback bind + smoke wait)
;   open-browser.vbs       : optional browser shortcut
;   stop-hitpan.bat        : kill API process
;   install-setup-v107.ps1 : v1.0.7 post-install (S1/S2/S3 + WinForms root prompt)
Source: "scripts\hitpan-launcher.vbs";     DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "scripts\start-hitpan.ps1";        DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "scripts\open-browser.vbs";        DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "scripts\stop-hitpan.bat";         DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "scripts\install-setup-v107.ps1";  DestDir: "{app}\scripts"; Flags: ignoreversion

; Install guide
Source: "설치방법.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\scripts\hitpan-launcher.vbs"; IconFilename: "{app}\hitpan\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\{#MyAppName}"; Filename: "{app}\scripts\hitpan-launcher.vbs"; IconFilename: "{app}\hitpan\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\{#MyAppName} 중지"; Filename: "{app}\scripts\stop-hitpan.bat"
Name: "{group}\{#MyAppName} 설치방법"; Filename: "{app}\설치방법.txt"
Name: "{group}\{#MyAppName} 제거"; Filename: "{uninstallexe}"

[Run]
; 1. MariaDB MSI (/passive keeps progress UI but needs no input)
;    S2 시나리오에서는 이미 설치된 MariaDB를 그대로 사용 — MSI는 IF EXISTS 확인 없이 무조건 호출되지만
;    /passive + 동일 버전이면 Windows Installer가 중복 설치를 자동 스킵한다.
Filename: "msiexec.exe"; \
  Parameters: "/i ""{tmp}\mariadb.msi"" PASSWORD=Hitpan2025! PORT=3306 SERVICENAME=MariaDB /passive /norestart"; \
  StatusMsg: "MariaDB 11.4 설치 중입니다. (약 1~2분)"; \
  Flags: waituntilterminated

; 2. v1.0.7 post-install: 시나리오 분기 + .env + 스모크 테스트
;    runhidden 환경에서도 Add-Type WinForms 다이얼로그가 표시됨을 검증 (작3 §6.1 노트)
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\install-setup-v107.ps1"" -AppDir ""{app}"""; \
  StatusMsg: "데이터베이스 초기화 및 환경설정 중... (S2 시 비밀번호 입력 다이얼로그가 표시될 수 있습니다)"; \
  Flags: waituntilterminated runhidden

; 3. Firewall rule for port 5234
Filename: "netsh.exe"; \
  Parameters: "advfirewall firewall add rule name=""HitPan ERP"" dir=in action=allow protocol=TCP localport=5234"; \
  StatusMsg: "방화벽 설정 중..."; \
  Flags: runhidden

; 4. Finish page "Run now" checkbox
Filename: "wscript.exe"; \
  Parameters: """{app}\scripts\hitpan-launcher.vbs"""; \
  Description: "히트판 ERP 지금 실행"; \
  Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\scripts\stop-hitpan.bat"; Flags: runhidden

[UninstallDelete]
Type: filesandordirs; Name: "{app}\hitpan\logs"
Type: filesandordirs; Name: "{app}\hitpan\temp"
Type: files;          Name: "{app}\hitpan\.env"

[Code]
function InitializeSetup(): Boolean;
begin
    Result := True;
    if (GetWindowsVersion < $0A000000) then
    begin
        MsgBox('히트판 ERP는 Windows 10 이상에서만 설치 가능합니다.', mbError, MB_OK);
        Result := False;
        Exit;
    end;
    MsgBox('히트판 ERP v1.0.7 설치를 시작합니다.' + #13#10 + #13#10 +
           '구성요소:' + #13#10 +
           '  • 히트판 ERP 본체 (자체 포함 .NET 런타임)' + #13#10 +
           '  • MariaDB 11.4 (데이터베이스 엔진, 기존 설치 자동 감지)' + #13#10 +
           '  • 6개 업종 샘플 (공구/금속/전자/플라스틱/가구/식품)' + #13#10 + #13#10 +
           '설치 시나리오 자동 분기 (v1.0.7 신규):' + #13#10 +
           '  • 깨끗한 PC: 자동 진행 (2~3분)' + #13#10 +
           '  • 기존 MariaDB: root 비밀번호 1회 입력 다이얼로그' + #13#10 +
           '  • 재설치: 데이터·키 보존' + #13#10 + #13#10 +
           '소요시간: 약 2~3분' + #13#10 +
           '설치 로그: %TEMP%\hitpan-install.log', mbInformation, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
    if CurStep = ssPostInstall then
    begin
        MsgBox('설치가 완료되었습니다.' + #13#10 + #13#10 +
               '다음 화면에서 [히트판 ERP 지금 실행] 체크박스를 유지한 채' + #13#10 +
               '[마침] 버튼을 누르면 서버가 시작되고 브라우저가 자동으로 열립니다.' + #13#10 + #13#10 +
               '로그인:' + #13#10 +
               '  아이디: tenant@hitpan.kr' + #13#10 +
               '  비밀번호: Admin1234!', mbInformation, MB_OK);
    end;
end;
