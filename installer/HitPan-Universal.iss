; ============================================================
; HitPan ERP — 범용 설치마법사 (Inno Setup 6.x)
; 사장님 결재 Plan 정합 2026-06-09:
;   "설치마법사 파일 만들고 CICD로 지속적인 업데이트"
;
; 기존 HitPan.iss와의 차이:
;   - HitPan.iss      : 고객별 빌드 (TenantId/Token 빌드 시점 주입)
;   - HitPan-Universal: 모든 고객 동일 EXE, 시리얼 입력으로 자동 박힘 ⭐ Plan 정합
;
; 빌드 방법:
;   build-installer-universal.ps1
;
; 또는 직접:
;   ISCC.exe HitPan-Universal.iss /DAppVersion=1.1.0
;
; 사장님 헌법 정합:
;   #18·#22 — 본사 인프라 토큰 EXE에 박지 않음, 시리얼만 입력
;   #25 — 쉽게: 시리얼 1개만 입력
;   #28·#30 — 고객 손 0번 자동 봉합
;   #34 — 정식 완성도 (베타부터 정식 인프라)
;   #35 — 시리얼 = 백오피스↔ERP 포링키
; ============================================================

#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif

#ifndef BackofficeApi
  #define BackofficeApi "https://back.hitpan.kr"
#endif

#ifndef OutputName
  #define OutputName "HitPan-ERP-Setup-" + AppVersion
#endif

#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#ifndef BundleDir
  #define BundleDir "..\installer-build\bundle"
#endif

[Setup]
AppId={{F4E2A1D0-7BC8-4F31-9E5A-6D8C4B1E2F36}
AppName=HitPan ERP
AppVersion={#AppVersion}
AppPublisher=히트판
AppPublisherURL=https://hitpan.kr
AppSupportURL=https://back.hitpan.kr/support
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

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Messages]
korean.WelcomeLabel1=히트판 ERP 설치 마법사
korean.WelcomeLabel2=히트판 ERP를 이 PC에 설치합니다.%n%n설치되는 항목:%n  · MariaDB 11.4 데이터베이스%n  · .NET 8 런타임%n  · 히트판 ERP 본체%n  · Cloudflare 보안 터널%n  · 자동 업데이트 워치독%n%n준비물:%n  · 이메일로 받으신 시리얼 키 (HITP-XXXX-XXXX-XXXX-XXXX)%n  · 인터넷 연결%n%n계속하려면 [다음]을 클릭하세요.
korean.FinishedLabel=히트판 ERP가 설치되었습니다.%n%n바탕화면의 [HitPan ERP] 아이콘을 더블클릭하여 시작하세요.%n%n시리얼 키와 회사 정보는 자동으로 박혔습니다.

[Files]
; 의존성 (BundleDir에 미리 박혀있어야 박힘)
Source: "{#BundleDir}\dotnet-hosting.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall ignoreversion; Check: NeedsDotNet
Source: "{#BundleDir}\mariadb.msi"; DestDir: "{tmp}"; Flags: deleteafterinstall ignoreversion; Check: NeedsMariaDB
Source: "{#BundleDir}\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall ignoreversion; Check: NeedsVCRedist
Source: "{#BundleDir}\cloudflared.exe"; DestDir: "{app}"; Flags: ignoreversion

; HitPan ERP 산출물
Source: "{#BundleDir}\api\*"; DestDir: "{app}\api"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#BundleDir}\web\*"; DestDir: "{app}\web"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#BundleDir}\hitpan_db.sql"; DestDir: "{app}"; Flags: ignoreversion

; 워치독 (Day 7에 박힐 영역, 지금은 placeholder)
Source: "{#BundleDir}\watchdog\HitPan.Watchdog.exe"; DestDir: "{app}\watchdog"; Flags: ignoreversion; Check: WatchdogExists

; 헬퍼 스크립트
Source: "hitpan-start.bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "web-server.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "scripts\AntivirusExceptions.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "scripts\FirewallRules.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "scripts\InstallCloudflared.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "scripts\SelfCheck.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion

[Icons]
Name: "{group}\HitPan ERP"; Filename: "{app}\hitpan-start.bat"; WorkingDir: "{app}"; IconFilename: "{sys}\shell32.dll"; IconIndex: 21; Comment: "히트판 ERP 시작"
Name: "{commondesktop}\HitPan ERP"; Filename: "{app}\hitpan-start.bat"; WorkingDir: "{app}"; IconFilename: "{sys}\shell32.dll"; IconIndex: 21

[Run]
Filename: "{tmp}\dotnet-hosting.exe"; Parameters: "/quiet /norestart"; StatusMsg: ".NET 8 런타임 설치 중..."; Check: NeedsDotNet; Flags: waituntilterminated
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/quiet /norestart"; StatusMsg: "Visual C++ 런타임 설치 중..."; Check: NeedsVCRedist; Flags: waituntilterminated
; MariaDB·DB 셋업·cloudflared 등록은 Code 섹션에서 시리얼 정보 받은 후 박힘
Filename: "{app}\hitpan-start.bat"; Description: "히트판 ERP 지금 시작"; Flags: postinstall nowait skipifsilent unchecked

[UninstallRun]
Filename: "{app}\cloudflared.exe"; Parameters: "service uninstall"; Flags: runhidden; RunOnceId: "RemoveTunnelService"
Filename: "{app}\watchdog\HitPan.Watchdog.exe"; Parameters: "uninstall"; Flags: runhidden; RunOnceId: "RemoveWatchdog"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\hitpan-keys.conf"
Type: filesandordirs; Name: "{app}\hitpan-tunnel.conf"
Type: filesandordirs; Name: "{app}\tenant.conf"
Type: filesandordirs; Name: "{app}\bootstrap.conf"

[Code]
// ============================================================
// 전역 변수
// ============================================================
var
  SerialKeyPage: TInputQueryWizardPage;
  BootstrapResultPage: TOutputMsgWizardPage;

  G_LicenseKey: String;
  G_TenantCode: String;
  G_CompanyName: String;
  G_BizNo: String;
  G_CeoName: String;
  G_PrimaryDomain: String;
  G_ApiDomain: String;
  G_TunnelToken: String;
  G_BootstrapToken: String;
  G_BootstrapOk: Boolean;

// ============================================================
// 의존성 감지
// ============================================================
function NeedsDotNet: Boolean;
var Version: String;
begin
  Result := True;
  if RegQueryStringValue(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.AspNetCore.App',
                         '8.0.0', Version) then Result := False;
  if RegKeyExists(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost') then Result := False;
end;

function NeedsVCRedist: Boolean;
var Installed: Cardinal;
begin
  Result := True;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed) then
    if Installed = 1 then Result := False;
end;

function NeedsMariaDB: Boolean;
begin
  Result := True;
  if FileExists('C:\Program Files\MariaDB 11.4\bin\mysql.exe') then Result := False;
  if FileExists('C:\Program Files\MariaDB 10.11\bin\mysql.exe') then Result := False;
  if FileExists('C:\Program Files\MySQL\MySQL Server 8.4\bin\mysql.exe') then Result := False;
end;

function WatchdogExists: Boolean;
begin
  Result := FileExists(ExpandConstant('{#BundleDir}\watchdog\HitPan.Watchdog.exe'));
end;

// ============================================================
// 시리얼 형식 검증 — HITP-XXXX-XXXX-XXXX-XXXX
// ============================================================
function IsValidSerialFormat(Serial: String): Boolean;
var
  Cleaned: String;
  I: Integer;
begin
  Cleaned := Uppercase(Trim(Serial));
  // 하이픈·공백 제거
  StringChangeEx(Cleaned, '-', '', True);
  StringChangeEx(Cleaned, ' ', '', True);

  // HITP + 16자 = 20자
  Result := False;
  if Length(Cleaned) <> 20 then Exit;
  if Copy(Cleaned, 1, 4) <> 'HITP' then Exit;

  // 나머지 16자가 영숫자인지
  for I := 5 to 20 do
    if not ((Cleaned[I] >= '0') and (Cleaned[I] <= '9'))
       and not ((Cleaned[I] >= 'A') and (Cleaned[I] <= 'Z')) then Exit;

  Result := True;
end;

// ============================================================
// 단순 JSON 값 추출 — "key":"value" 패턴
// ============================================================
function ExtractJsonValue(Json: String; Key: String): String;
var
  SearchKey: String;
  StartPos, EndPos: Integer;
begin
  Result := '';
  SearchKey := '"' + Key + '":';
  StartPos := Pos(SearchKey, Json);
  if StartPos = 0 then Exit;

  StartPos := StartPos + Length(SearchKey);
  while (StartPos <= Length(Json)) and ((Json[StartPos] = ' ') or (Json[StartPos] = '"')) do
    StartPos := StartPos + 1;

  if Copy(Json, StartPos, 4) = 'null' then Exit;

  EndPos := StartPos;
  while (EndPos <= Length(Json)) and (Json[EndPos] <> '"') and (Json[EndPos] <> ',') and (Json[EndPos] <> '}') do
    EndPos := EndPos + 1;

  Result := Copy(Json, StartPos, EndPos - StartPos);
  while (Length(Result) > 0) and ((Result[Length(Result)] = '"') or (Result[Length(Result)] = ' ')) do
    Delete(Result, Length(Result), 1);
end;

// ============================================================
// PowerShell로 백오피스 API 호출 (Inno Setup HTTP 직접 불가)
// ============================================================
function CallBootstrapApi(Serial: String): Boolean;
var
  PsScript: String;
  ResultCode: Integer;
  ResponseFile, RequestFile: String;
  Lines: TArrayOfString;
  RawResponse: String;
  I: Integer;
begin
  Result := False;
  G_BootstrapOk := False;

  ResponseFile := ExpandConstant('{tmp}\bootstrap-response.json');
  RequestFile := ExpandConstant('{tmp}\bootstrap-request.json');

  // 요청 본문 박기 (JSON)
  SaveStringToFile(RequestFile,
    '{"licenseKey":"' + Serial + '",' +
    '"machineFingerprint":"' + ExpandConstant('{computername}') + '-' + ExpandConstant('{username}') + '",' +
    '"hostname":"' + ExpandConstant('{computername}') + '",' +
    '"installerVersion":"{#AppVersion}"}', False);

  PsScript :=
    '$ErrorActionPreference = ''Stop''; ' +
    'try { ' +
    '  $body = Get-Content -Raw -Path ''' + RequestFile + '''; ' +
    '  $r = Invoke-RestMethod -Uri ''{#BackofficeApi}/api/installer/bootstrap'' ' +
    '       -Method POST -Body $body -ContentType ''application/json'' -TimeoutSec 30; ' +
    '  $r | ConvertTo-Json -Depth 10 -Compress | Out-File -Encoding ASCII -NoNewline ''' + ResponseFile + '''; ' +
    '  exit 0; ' +
    '} catch { ' +
    '  $msg = $_.Exception.Message; ' +
    '  if ($_.ErrorDetails -and $_.ErrorDetails.Message) { $msg = $_.ErrorDetails.Message; } ' +
    '  "{\"success\":false,\"message\":\"$msg\"}" | Out-File -Encoding ASCII -NoNewline ''' + ResponseFile + '''; ' +
    '  exit 1; ' +
    '}';

  Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -Command "' + PsScript + '"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if not FileExists(ResponseFile) then begin
    MsgBox('백오피스 응답을 받지 못했습니다.' + #13#10 + '인터넷 연결을 확인해주세요.', mbError, MB_OK);
    Exit;
  end;

  if not LoadStringsFromFile(ResponseFile, Lines) then Exit;

  RawResponse := '';
  for I := 0 to GetArrayLength(Lines) - 1 do RawResponse := RawResponse + Lines[I];

  // 매우 단순 JSON 파싱 (정식 영역은 워치독에서 박을 영역)
  if Pos('"success":true', RawResponse) = 0 then begin
    // 에러 메시지 추출 (단순)
    G_CompanyName := ExtractJsonValue(RawResponse, 'message');
    if G_CompanyName = '' then G_CompanyName := '알 수 없는 오류';
    MsgBox('시리얼 인증 실패:' + #13#10 + G_CompanyName, mbError, MB_OK);
    Exit;
  end;

  // 응답에서 필드 추출
  G_TenantCode := ExtractJsonValue(RawResponse, 'tenantCode');
  G_CompanyName := ExtractJsonValue(RawResponse, 'companyName');
  G_BizNo := ExtractJsonValue(RawResponse, 'bizNo');
  G_CeoName := ExtractJsonValue(RawResponse, 'ceoName');
  G_PrimaryDomain := ExtractJsonValue(RawResponse, 'primary');
  G_ApiDomain := ExtractJsonValue(RawResponse, 'api');
  G_TunnelToken := ExtractJsonValue(RawResponse, 'tunnelToken');
  G_BootstrapToken := ExtractJsonValue(RawResponse, 'token');

  // 정리
  DeleteFile(ResponseFile);
  DeleteFile(RequestFile);

  G_BootstrapOk := True;
  Result := True;
end;

// ============================================================
// 마법사 페이지 박기
// ============================================================
procedure InitializeWizard;
begin
  // 시리얼 입력 페이지
  SerialKeyPage := CreateInputQueryPage(wpWelcome,
    '시리얼 키 입력',
    '이메일로 받으신 시리얼 키를 입력해주세요',
    '본사에서 발급된 시리얼 키는 다음 형식입니다:' + #13#10 +
    '    HITP-XXXX-XXXX-XXXX-XXXX' + #13#10 + #13#10 +
    '시리얼 키는 이메일로 발송되었습니다. 분실 시 본사 고객센터에 문의해주세요.');

  SerialKeyPage.Add('시리얼 키:', False);
  SerialKeyPage.Values[0] := 'HITP-';

  // 부트스트랩 결과 확인 페이지
  BootstrapResultPage := CreateOutputMsgPage(SerialKeyPage.ID,
    '회사 정보 확인',
    '시리얼 키로 회사 정보를 확인했습니다',
    '아래 정보가 맞는지 확인해주세요. 틀리면 설치를 취소하고 본사에 문의해주세요.');
end;

// ============================================================
// 페이지 검증
// ============================================================
function NextButtonClick(CurPageID: Integer): Boolean;
var
  TrimmedKey: String;
  SkipResponse: Integer;
begin
  Result := True;

  if CurPageID = SerialKeyPage.ID then
  begin
    TrimmedKey := Trim(SerialKeyPage.Values[0]);
    G_LicenseKey := TrimmedKey;

    // 시리얼 비어있거나 디폴트(HITP- 그대로)인 경우 — 로컬 단독 모드 안내
    if (TrimmedKey = '') or (TrimmedKey = 'HITP-') then begin
      SkipResponse := MsgBox(
        '시리얼 키를 입력하지 않고 설치하시겠습니까?' + #13#10 + #13#10 +
        '시리얼 없이 설치 시:' + #13#10 +
        '  ✓ ERP 본체·데이터베이스·로그인 화면까지 정상 작동' + #13#10 +
        '  ✗ 외부 도메인(www.회사명.hitpan.kr) 연결은 안 됨' + #13#10 +
        '  ✗ 시리얼은 추후 환경설정에서 입력 가능' + #13#10 + #13#10 +
        '「예」 시리얼 없이 설치 진행' + #13#10 +
        '「아니오」 시리얼 입력으로 돌아가기',
        mbConfirmation, MB_YESNO);
      if SkipResponse = IDYES then begin
        G_BootstrapOk := False;
        G_LicenseKey := '';
        G_CompanyName := '(미인증 — 시리얼 미입력)';
        G_TenantCode := 'LOCAL';
        G_PrimaryDomain := 'localhost:5234';
        Result := True;
        Exit;
      end else begin
        Result := False;
        Exit;
      end;
    end;

    if not IsValidSerialFormat(TrimmedKey) then begin
      MsgBox('시리얼 키 형식이 올바르지 않습니다.' + #13#10 +
             '예: HITP-XXXX-XXXX-XXXX-XXXX' + #13#10 + #13#10 +
             '시리얼 없이 설치하시려면 시리얼 입력란을 비워두세요.', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    // 백오피스 API 호출
    WizardForm.NextButton.Enabled := False;
    WizardForm.NextButton.Caption := '확인 중...';

    if not CallBootstrapApi(TrimmedKey) then begin
      WizardForm.NextButton.Enabled := True;
      WizardForm.NextButton.Caption := '다음';
      // 시리얼 인증 실패 — 로컬 단독 모드 폴백 결재 (사장님 헌법 #20·#25)
      SkipResponse := MsgBox(
        '시리얼 인증에 실패했습니다.' + #13#10 + #13#10 +
        '로컬 단독 모드로 설치를 계속 진행하시겠습니까?' + #13#10 +
        '  ✓ ERP 본체·데이터베이스·로그인 화면까지 정상 작동' + #13#10 +
        '  ✗ 외부 도메인 연결은 안 됨 (시리얼 추후 입력 가능)' + #13#10 + #13#10 +
        '「예」 로컬 단독 모드로 설치 진행' + #13#10 +
        '「아니오」 시리얼 다시 입력',
        mbConfirmation, MB_YESNO);
      if SkipResponse = IDYES then begin
        G_BootstrapOk := False;
        G_LicenseKey := '';
        G_CompanyName := '(미인증 — 시리얼 인증 실패)';
        G_TenantCode := 'LOCAL';
        G_PrimaryDomain := 'localhost:5234';
        Result := True;
        Exit;
      end else begin
        Result := False;
        Exit;
      end;
    end;

    WizardForm.NextButton.Enabled := True;
    WizardForm.NextButton.Caption := '다음';

    // 회사정보 확인 페이지에 박을 텍스트 갱신
    BootstrapResultPage.MsgLabel.Caption :=
      '회사명: ' + G_CompanyName + #13#10 +
      '사업자번호: ' + G_BizNo + #13#10 +
      '대표자: ' + G_CeoName + #13#10 +
      '테넌트 코드: ' + G_TenantCode + #13#10 +
      '도메인: ' + G_PrimaryDomain + #13#10 + #13#10 +
      '이 정보가 맞으시면 「다음」을 클릭하세요.' + #13#10 +
      '틀리면 설치를 취소하고 본사에 문의해주세요.';
  end;
end;

// ============================================================
// 보안 키 + 부트스트랩 정보 저장 + DB 셋업
// ============================================================
function GenerateRandomKey(Bytes: Integer): String;
var
  PsScript: String;
  ResultCode: Integer;
  TempFile: String;
  Lines: TArrayOfString;
begin
  TempFile := ExpandConstant('{tmp}\randkey.txt');
  PsScript := Format('[Convert]::ToBase64String((1..%d|%%{Get-Random -Max 256}|%%{[byte]$_})) | Out-File -Encoding ASCII -NoNewline "%s"', [Bytes, TempFile]);
  Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -Command "' + PsScript + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if (ResultCode = 0) and FileExists(TempFile) then begin
    if LoadStringsFromFile(TempFile, Lines) and (GetArrayLength(Lines) > 0) then Result := Lines[0] else Result := '';
    DeleteFile(TempFile);
  end else Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  JwtKey, AesKey, MariaRootPw: String;
  ConfFile, BatchFile, BootstrapFile: String;
  KeysContent, BatchContent, BootstrapContent: TStringList;
begin
  if CurStep <> ssPostInstall then Exit;
  // 사장님 헌법 #20·#25 정합 (2026-06-11): 시리얼 무관 ERP 본체·DB·바로가기는 박힘
  // G_BootstrapOk = false 일 때도 MariaDB·DB·hitpan-keys.conf·bootstrap.conf 박힘
  // cloudflared 터널만 G_TunnelToken 영역 조건부

  // 1. 보안 키 생성
  JwtKey := GenerateRandomKey(32);
  AesKey := GenerateRandomKey(32);
  MariaRootPw := GenerateRandomKey(24);

  // 2. hitpan-keys.conf — 관리자만 읽기
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
  Exec(ExpandConstant('{cmd}'),
       '/C icacls "' + ConfFile + '" /inheritance:r /grant:r "Administrators:F" /grant:r "SYSTEM:F"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // 3. bootstrap.conf — tenant 정보 + 부트스트랩 토큰
  BootstrapFile := ExpandConstant('{app}\bootstrap.conf');
  BootstrapContent := TStringList.Create;
  try
    BootstrapContent.Add('TENANT_CODE=' + G_TenantCode);
    BootstrapContent.Add('COMPANY_NAME=' + G_CompanyName);
    BootstrapContent.Add('BIZ_NO=' + G_BizNo);
    BootstrapContent.Add('CEO_NAME=' + G_CeoName);
    BootstrapContent.Add('PRIMARY_DOMAIN=' + G_PrimaryDomain);
    BootstrapContent.Add('API_DOMAIN=' + G_ApiDomain);
    BootstrapContent.Add('BACKOFFICE_URL={#BackofficeApi}');
    BootstrapContent.Add('BOOTSTRAP_TOKEN=' + G_BootstrapToken);
    BootstrapContent.Add('INSTALLED_AT=' + GetDateTimeString('yyyy/mm/dd hh:nn:ss', '-', ':'));
    BootstrapContent.Add('INSTALLER_VERSION={#AppVersion}');
    BootstrapContent.SaveToFile(BootstrapFile);
  finally
    BootstrapContent.Free;
  end;
  Exec(ExpandConstant('{cmd}'),
       '/C icacls "' + BootstrapFile + '" /inheritance:r /grant:r "Administrators:F" /grant:r "SYSTEM:F"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // 4. MariaDB silent install
  if NeedsMariaDB then begin
    Exec('msiexec.exe', Format('/i "%s\mariadb.msi" /quiet SERVICENAME=MariaDB PASSWORD=%s', [ExpandConstant('{tmp}'), MariaRootPw]), '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(10000);
  end;

  // 5. DB 셋업 (시드 박는 영역 사용자 데이터 보호)
  BatchFile := ExpandConstant('{tmp}\db-setup.bat');
  BatchContent := TStringList.Create;
  try
    BatchContent.Add('@echo off');
    BatchContent.Add('setlocal enabledelayedexpansion');
    BatchContent.Add('set "PATH=%PATH%;C:\Program Files\MariaDB 11.4\bin;C:\Program Files\MariaDB 10.11\bin"');
    BatchContent.Add(Format('mysql -u root -p%s -e "CREATE DATABASE IF NOT EXISTS hitpan_erp CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"', [MariaRootPw]));
    BatchContent.Add(Format('mysql -u root -p%s -e "CREATE USER IF NOT EXISTS ''hitpan''@''localhost'' IDENTIFIED BY ''Hitpan2025!''; GRANT ALL ON *.* TO ''hitpan''@''localhost''; FLUSH PRIVILEGES;"', [MariaRootPw]));
    BatchContent.Add('set EXISTING_DATA=0');
    BatchContent.Add('for /f "skip=1 tokens=*" %%c in (''mysql -u hitpan -pHitpan2025! -N -e "SELECT COALESCE((SELECT COUNT(*) FROM hitpan_erp.items),0)+COALESCE((SELECT COUNT(*) FROM hitpan_erp.partners),0)" 2^^^>nul'') do set EXISTING_DATA=%%c');
    BatchContent.Add('if "!EXISTING_DATA!"=="" set EXISTING_DATA=0');
    BatchContent.Add('if !EXISTING_DATA! GTR 0 (echo 기존 운영 데이터 !EXISTING_DATA!건 감지. 시드 import 건너뜀.) else (mysql -u hitpan -pHitpan2025! hitpan_erp < "' + ExpandConstant('{app}\hitpan_db.sql') + '")');
    BatchContent.SaveToFile(BatchFile);
  finally
    BatchContent.Free;
  end;
  Exec(ExpandConstant('{cmd}'), '/C "' + BatchFile + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  DeleteFile(BatchFile);

  // 6. cloudflared 터널 등록 (Day 5에 봉합 박을 영역 — 현재 G_TunnelToken null이면 건너뜀)
  if G_TunnelToken <> '' then begin
    Exec(ExpandConstant('{app}\cloudflared.exe'),
         'service install ' + G_TunnelToken,
         ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;

  // 7. 백신 예외 + 방화벽 (헌법 #31 정합)
  if FileExists(ExpandConstant('{app}\scripts\AntivirusExceptions.ps1')) then
    Exec('powershell.exe',
         '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\scripts\AntivirusExceptions.ps1') + '"',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if FileExists(ExpandConstant('{app}\scripts\FirewallRules.ps1')) then
    Exec('powershell.exe',
         '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\scripts\FirewallRules.ps1') + '"',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // 8. 워치독 서비스 등록 (Day 7에 박힐 영역)
  if FileExists(ExpandConstant('{app}\watchdog\HitPan.Watchdog.exe')) then
    Exec(ExpandConstant('{app}\watchdog\HitPan.Watchdog.exe'),
         'install',
         ExpandConstant('{app}\watchdog'), SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not IsAdminInstallMode then begin
    MsgBox('관리자 권한이 필요합니다.' + #13#10 +
           '설치 파일을 우클릭 → "관리자 권한으로 실행"하세요.',
           mbError, MB_OK);
    Result := False;
  end;
end;
