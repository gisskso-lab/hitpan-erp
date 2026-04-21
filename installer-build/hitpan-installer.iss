; ══════════════════════════════════════════════════════════════
;  히트판 ERP 원클릭 설치파일 (Inno Setup)
;  - MariaDB 11.4 자동 설치
;  - 히트판 ERP API + Web 자동 배포
;  - DB 스키마 자동 Import
;  - 바탕화면 / 시작 메뉴 바로가기 생성
;  - Windows 설치 마법사 UI (CMD 미노출)
; ══════════════════════════════════════════════════════════════

#define MyAppName "히트판 ERP"
#define MyAppNameEn "HitPan ERP"
#define MyAppVersion "1.0.0"
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
DisableDirPage=yes
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=히트판_ERP_설치
SetupIconFile=
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayName={#MyAppName}
LicenseFile=
WizardImageStretch=no
; 빠른 설치: 기본 설정으로 바로 설치
DisableReadyPage=yes
DisableFinishedPage=no
ShowLanguageDialog=no

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Files]
; 히트판 ERP 자체 포함 실행파일 (약 120MB)
Source: "hitpan\*"; DestDir: "{app}\hitpan"; Flags: ignoreversion recursesubdirs createallsubdirs

; DB 초기 덤프
Source: "hitpan_db.sql"; DestDir: "{app}"; Flags: ignoreversion

; MariaDB MSI (78MB)
Source: "prereqs\mariadb.msi"; DestDir: "{tmp}"; Flags: deleteafterinstall

; 시작/중지 스크립트
Source: "scripts\start-hitpan.vbs"; DestDir: "{app}"; Flags: ignoreversion
Source: "scripts\stop-hitpan.bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "scripts\open-browser.vbs"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autodesktop}\히트판 ERP"; Filename: "{app}\start-hitpan.vbs"; IconFilename: "{app}\hitpan\{#MyAppExeName}"
Name: "{group}\히트판 ERP"; Filename: "{app}\start-hitpan.vbs"; IconFilename: "{app}\hitpan\{#MyAppExeName}"
Name: "{group}\히트판 ERP 중지"; Filename: "{app}\stop-hitpan.bat"
Name: "{group}\히트판 ERP 설치 제거"; Filename: "{uninstallexe}"

[Run]
; 1. MariaDB 조용히 설치 (root 비밀번호 Hitpan2025!)
Filename: "msiexec.exe"; Parameters: "/i ""{tmp}\mariadb.msi"" PASSWORD=Hitpan2025! PORT=3306 SERVICENAME=MariaDB /qn /norestart"; StatusMsg: "MariaDB 11.4 설치 중... (약 1~2분)"; Flags: runhidden waituntilterminated

; 2. MariaDB 서비스 시작
Filename: "net"; Parameters: "start MariaDB"; Flags: runhidden; StatusMsg: "MariaDB 서비스 시작 중..."

; 3. DB 스키마 Import — PowerShell로 실행 (CMD 숨김)
Filename: "powershell.exe"; Parameters: "-WindowStyle Hidden -NoProfile -Command ""& 'C:\Program Files\MariaDB 11.4\bin\mariadb.exe' -uroot -pHitpan2025! -e ""CREATE DATABASE IF NOT EXISTS hitpan_erp CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci; CREATE USER IF NOT EXISTS 'hitpan'@'localhost' IDENTIFIED BY 'Hitpan2025!'; GRANT ALL PRIVILEGES ON hitpan_erp.* TO 'hitpan'@'localhost'; FLUSH PRIVILEGES;""; Get-Content '{app}\hitpan_db.sql' | & 'C:\Program Files\MariaDB 11.4\bin\mariadb.exe' -uhitpan -pHitpan2025! hitpan_erp"""; Flags: runhidden waituntilterminated; StatusMsg: "히트판 데이터베이스 초기화 중..."

; 4. 방화벽 예외 등록 (포트 5234, 5257)
Filename: "netsh"; Parameters: "advfirewall firewall add rule name=""HitPan ERP Web"" dir=in action=allow protocol=TCP localport=5234"; Flags: runhidden; StatusMsg: "방화벽 설정 중..."
Filename: "netsh"; Parameters: "advfirewall firewall add rule name=""HitPan ERP API"" dir=in action=allow protocol=TCP localport=5257"; Flags: runhidden

; 5. 설치 완료 — 히트판 ERP 실행 + 브라우저 자동 열기
Filename: "{app}\start-hitpan.vbs"; Description: "히트판 ERP 지금 실행"; Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
; 제거 시 히트판 프로세스 중지
Filename: "{app}\stop-hitpan.bat"; Flags: runhidden

[UninstallDelete]
Type: filesandordirs; Name: "{app}\hitpan\logs"
Type: filesandordirs; Name: "{app}\hitpan\temp"

[Code]
// 설치 전 시스템 요구사항 확인
function InitializeSetup(): Boolean;
var
    Msg: String;
begin
    Result := True;

    // Windows 10 이상만 지원
    if (GetWindowsVersion < $0A000000) then
    begin
        Msg := '히트판 ERP는 Windows 10 이상에서만 설치 가능합니다.';
        MsgBox(Msg, mbError, MB_OK);
        Result := False;
        Exit;
    end;

    // 안내 메시지
    MsgBox('히트판 ERP를 설치합니다.' + #13#10 + #13#10 +
           '포함된 구성요소:' + #13#10 +
           '  • 히트판 ERP (자체 포함 실행파일)' + #13#10 +
           '  • MariaDB 11.4 (데이터베이스)' + #13#10 + #13#10 +
           '설치 소요 시간: 약 2~3분' + #13#10 +
           '설치 경로: C:\HitPan', mbInformation, MB_OK);
end;

// 설치 완료 후 안내
procedure CurStepChanged(CurStep: TSetupStep);
begin
    if CurStep = ssPostInstall then
    begin
        MsgBox('설치가 완료되었습니다!' + #13#10 + #13#10 +
               '바탕화면의 "히트판 ERP" 아이콘을 더블클릭하여 시작하세요.' + #13#10 + #13#10 +
               '로그인:' + #13#10 +
               '  아이디: tenant@hitpan.kr' + #13#10 +
               '  비밀번호: Admin1234!', mbInformation, MB_OK);
    end;
end;
