; ============================================================
;  HitPan ERP 설치 EXE - Inno Setup 스크립트
;  v1.1.0 (헌법 #27·#28·#29·#31 정합)
;  - 라이선스 키 1번 입력 외 사용자 입력 0
;  - 백신 5종 자동 예외 (Defender + V3 + 알약 + Naver) + 매뉴얼 (Norton + McAfee)
;  - cloudflared / MariaDB / API / Web / Watchdog 자동 설치
;  - 통신 무결성 자가 점검 (5분 안 /health 200)
; ============================================================

[Setup]
AppName=히트판 ERP
AppVersion=1.1.0
AppPublisher=히트판
AppPublisherURL=https://hitpan.kr
DefaultDirName={autopf}\HitPan
DefaultGroupName=히트판
OutputDir=output
OutputBaseFilename=HitPanSetup_v1.1.0
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern
DisableWelcomePage=no
DisableProgramGroupPage=yes
DisableReadyPage=no
ShowLanguageDialog=no
CloseApplications=force

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Files]
Source: "payload\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "scripts\*.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion skipifsourcedoesntexist

[Run]
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\AntivirusExceptions.ps1"" -InstallPath ""{app}"""; \
  StatusMsg: "백신 5종 자동 예외 등록 중..."; \
  Flags: runhidden waituntilterminated

Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\FirewallRules.ps1"""; \
  StatusMsg: "방화벽 규칙 추가 중..."; \
  Flags: runhidden waituntilterminated

Filename: "msiexec.exe"; \
  Parameters: "/i ""{app}\payload\mariadb-11.4.10-winx64.msi"" /qn SERVICENAME=MariaDB PASSWORD=Hitpan2025!"; \
  StatusMsg: "MariaDB 설치 중..."; \
  Flags: waituntilterminated; Check: NeedMariaDB

Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\InstallCloudflared.ps1"" -LicenseKey ""{code:GetLicenseKey}"""; \
  StatusMsg: "통신 터널 설치 중..."; \
  Flags: runhidden waituntilterminated

Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\InstallWatchdog.ps1"" -InstallPath ""{app}"""; \
  StatusMsg: "워치독 등록 중..."; \
  Flags: runhidden waituntilterminated

Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\SelfCheck.ps1"""; \
  StatusMsg: "통신 무결성 확인 중..."; \
  Flags: runhidden waituntilterminated

[UninstallRun]
Filename: "sc.exe"; Parameters: "stop HitPanWatchdog"; Flags: runhidden
Filename: "sc.exe"; Parameters: "delete HitPanWatchdog"; Flags: runhidden
Filename: "sc.exe"; Parameters: "stop cloudflared"; Flags: runhidden
Filename: "sc.exe"; Parameters: "delete cloudflared"; Flags: runhidden
Filename: "schtasks.exe"; Parameters: "/Delete /TN HitPanWatchdogGuardian /F"; Flags: runhidden

[Code]
var
  LicenseKeyPage: TInputQueryWizardPage;

procedure InitializeWizard;
begin
  LicenseKeyPage := CreateInputQueryPage(wpWelcome,
    '라이선스 키 입력', '히트판 라이선스 키 한 개만 입력하세요',
    '나머지는 자동으로 설정됩니다. 본사로부터 받으신 16자 이상 키를 입력해 주세요.');
  LicenseKeyPage.Add('라이선스 키:', False);
end;

function GetLicenseKey(Param: String): String;
begin
  Result := LicenseKeyPage.Values[0];
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (LicenseKeyPage <> nil) and (CurPageID = LicenseKeyPage.ID) then
  begin
    if Length(LicenseKeyPage.Values[0]) < 16 then
    begin
      MsgBox('라이선스 키는 16자 이상이어야 합니다.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function NeedMariaDB: Boolean;
var
  ResultCode: Integer;
begin
  Result := not Exec('sc.exe', 'query MariaDB', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0);
end;
