; ============================================================
; HitPan ERP — 범용 설치마법사 (Inno Setup 6.x)
; 사장님 결재 Plan 정합 2026-06-09:
;   "설치마법사 파일 만들고 CICD로 지속적인 업데이트"
;
; 기존 HitPan.iss와의 차이:
;   - HitPan.iss      : 고객별 빌드 (TenantId/Token 빌드 시점 주입)
;   - HitPan-Universal: 모든 고객 동일 EXE, 시리얼 입력으로 자동 설정 ⭐ Plan 정합
;
; 빌드 방법:
;   build-installer-universal.ps1
;
; 또는 직접:
;   ISCC.exe HitPan-Universal.iss /DAppVersion=1.1.0
;
; 사장님 헌법 정합:
;   #18·#22 — 본사 인프라 토큰 EXE에 포함하지 않음, 시리얼만 입력
;   #25 — 쉽게: 시리얼 1개만 입력
;   #28·#30 — 고객 손 0번 자동 봉합
;   #34 — 정식 완성도 (베타부터 정식 인프라)
;   #35 — 시리얼 = 백오피스↔ERP 포링키
; ============================================================

#ifndef AppVersion
  #define AppVersion "1.2.13"
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
; 의존성 (BundleDir에 미리 포함되어 있어야 함)
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
Source: "scripts\InstallWatchdog.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
; 폐기 (WS-20260612-01 Q1=A 사장님 결재 2026-06-12): InstallCloudflared.ps1 + SelfCheck.ps1 + BootstrapInstall.ps1
; → installer/_deprecated_20260612/ 영역으로 이동, .iss CurStepChanged에 통합 박힘

[Icons]
Name: "{group}\HitPan ERP"; Filename: "{app}\hitpan-start.bat"; WorkingDir: "{app}"; IconFilename: "{sys}\shell32.dll"; IconIndex: 21; Comment: "히트판 ERP 시작"
Name: "{commondesktop}\HitPan ERP"; Filename: "{app}\hitpan-start.bat"; WorkingDir: "{app}"; IconFilename: "{sys}\shell32.dll"; IconIndex: 21

[Run]
; 봉합 2026-06-16: log 영역 박음 — Sandbox 영역 멈춤 사고 영역 진단 가능 박음
;   /log 옵션 영역 박힘 → 사고 시 %TEMP%\dotnet-install.log 영역 박힘
;   /passive 영역 박음 → /quiet 영역 다이얼로그 사고 영역 진행 바 박힘 영역 정합 (사용자 응답 0건)
Filename: "{tmp}\dotnet-hosting.exe"; Parameters: "/install /passive /norestart /log ""{tmp}\dotnet-install.log"""; StatusMsg: ".NET 8 런타임 설치 중 (최대 5분 영역)..."; Check: NeedsDotNet; Flags: waituntilterminated
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /passive /norestart /log ""{tmp}\vcredist-install.log"""; StatusMsg: "Visual C++ 런타임 설치 중..."; Check: NeedsVCRedist; Flags: waituntilterminated
; MariaDB·DB 셋업·cloudflared 등록·ERP 자동 시작·브라우저 열기는 Code 섹션 CurStepChanged에서 처리 (사장님 헌법 #30 정합 2026-06-11)

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
  // 길 B (사장님 결재 2026-06-18): G_BizNo·G_CeoName 제거 — 백오피스 평문 미보유, 설치 EXE도 미취급.
  G_PrimaryDomain: String;
  G_ApiDomain: String;
  G_TunnelToken: String;
  // 봉합 (2026-06-21, 7차 전수조사 D6-P0-01): 워치독 터널 자가복구(WS-28-C)가 cloudflared 터널 UUID 를
  //   필요로 하는데 종전엔 db.conf 에 없어 신규설치 PC 에서 자가복구가 무력했다(헌법 #28). 백오피스 응답의
  //   domain.tunnelId(CF 터널 UUID)를 추출해 db.conf TUNNEL_ID 로 저장한다.
  G_TunnelId: String;
  G_BootstrapToken: String;
  G_BootstrapOk: Boolean;

  // 멀티사업자 영역 변수 (사고 #16·#21·#22 봉합 WS-20260612-01 2026-06-12)
  //   사장님 결재 [[project_multi_business_per_pc]] (2026-06-09)
  //   슬롯 동적 결정 (1~5) → registry.json 박음
  G_SlotIndex: Integer;     // 1~5 (포트 5257 + 100*N)
  G_ApiPort: Integer;       // 5257, 5357, 5457, 5557, 5657
  G_TenantInstallDir: String;  // {app}\tenant-N
  G_DbName: String;         // hitpan_erp_{tenantCode}
  G_DbUser: String;         // hitpan_{tenantCode}
  G_DbPassword: String;     // 랜덤 비번 (사고 #18 봉합 — 하드코딩 0건)

  // 사고 #45 봉합 (CTO 발견 2026-06-12): [뒤로] 버튼 영역 슬롯 영역 재결정 영역 차단
  //   사용자 영역 시리얼 영역 입력 영역 후 영역 [뒤로] 영역 박힘 → 같은 시리얼 영역 다시 영역
  //   → DetermineMultiTenantSlot 영역 다시 호출 영역 → 슬롯 영역 또 박음 → 이중 점유 영역
  //   봉합: 한 번 박힌 영역 결정 영역 박혀있으면 영역 다시 박지 않음
  G_SlotAlreadyDetermined: Boolean;

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
// 멀티사업자 슬롯 결정 (사고 #16·#21·#22 봉합 WS-20260612-01 2026-06-12)
// registry.json 영역 박음 — 첫 설치 / 추가 설치 분기
// 사장님 결재 [[project_multi_business_per_pc]] (2026-06-09)
// ============================================================
procedure DetermineMultiTenantSlot();
var
  PsScript: String;
  ResultCode: Integer;
  PsFile, ResultFile: String;
  Lines: TArrayOfString;
  SanitizedCode: String;
  PosDash: Integer;
begin
  // 사고 #45 봉합 (CTO 발견 2026-06-12): 한 번 박힌 영역 결정 영역 영역 재결정 영역 차단
  //   [뒤로] 영역 박은 후 영역 다시 영역 호출 영역 박혀도 영역 영역 0건 영역
  if G_SlotAlreadyDetermined then Exit;

  // 기본값 (시리얼 0건 영역 = LOCAL)
  G_SlotIndex := 1;
  G_ApiPort := 5257;
  // 봉합 2026-06-16: {app} 영역 = wpSelectDir 통과 후 초기화. SerialKeyPage 단계에서는 미초기화.
  //   사고: "An attempt was made to expand the 'app' constant before it was initialized."
  //   G_TenantInstallDir 영역 박음 = CurStepChanged(ssInstall) 시점으로 지연.
  G_TenantInstallDir := '';
  G_DbName := 'hitpan_erp';
  G_DbUser := 'hitpan';

  if G_TenantCode = 'LOCAL' then begin
    // 로컬 단독 모드 — 기본값 그대로
    G_SlotAlreadyDetermined := True;
    Exit;
  end;

  // PowerShell로 registry.json 영역 읽어서 슬롯 영역 결정
  // 봉합 2026-06-16 (B안): registry.json 영역 ProgramData 영역 이전.
  //   원안: {app}\registry.json → {app} 미초기화 영역 사고
  //   B안: {commonappdata}\HitPan\registry.json → 사용자 영역 무관·{app} 의존 0건
  PsFile := ExpandConstant('{tmp}\determine-slot.ps1');
  ResultFile := ExpandConstant('{tmp}\slot-result.txt');

  PsScript :=
    '$ErrorActionPreference = ''Continue'';' + #13#10 +
    '$registryPath = "' + ExpandConstant('{commonappdata}') + '\HitPan\registry.json";' + #13#10 +
    '$slot = 1;' + #13#10 +
    '$tenantCode = "' + G_TenantCode + '";' + #13#10 +
    'if (Test-Path $registryPath) {' + #13#10 +
    '  try {' + #13#10 +
    '    $reg = Get-Content $registryPath -Raw | ConvertFrom-Json;' + #13#10 +
    '    if ($reg.tenants) {' + #13#10 +
    '      # 같은 시리얼 영역 박힘 영역 확인 (중복 방지)' + #13#10 +
    '      $existing = $reg.tenants | Where-Object { $_.tenantCode -eq $tenantCode };' + #13#10 +
    '      if ($existing) {' + #13#10 +
    '        $slot = -1;' + #13#10 +
    '      } else {' + #13#10 +
    '        # 다음 슬롯 영역 결정' + #13#10 +
    '        $usedSlots = $reg.tenants | ForEach-Object { [int]$_.slotIndex };' + #13#10 +
    '        for ($i = 1; $i -le 5; $i++) {' + #13#10 +
    '          if ($usedSlots -notcontains $i) { $slot = $i; break; }' + #13#10 +
    '        }' + #13#10 +
    '      }' + #13#10 +
    '    }' + #13#10 +
    '  } catch { $slot = 1; }' + #13#10 +
    '}' + #13#10 +
    '[System.IO.File]::WriteAllText("' + ResultFile + '", $slot.ToString(), [System.Text.Encoding]::UTF8);';

  SaveStringToFile(PsFile, PsScript, False);
  Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -File "' + PsFile + '"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if FileExists(ResultFile) and LoadStringsFromFile(ResultFile, Lines) and (GetArrayLength(Lines) > 0) then begin
    G_SlotIndex := StrToIntDef(Trim(Lines[0]), 1);
  end;

  DeleteFile(PsFile);
  DeleteFile(ResultFile);

  // 슬롯 = -1 영역 = 동일 시리얼 영역 박힘 영역 (중복)
  if G_SlotIndex = -1 then begin
    MsgBox('이 시리얼 키로 이미 설치된 회사가 있습니다.' + #13#10 +
           '다른 시리얼로 시도하거나 본사에 문의해주세요.', mbError, MB_OK);
    G_SlotIndex := 1;
    G_BootstrapOk := False;
    Exit;
  end;

  // 슬롯 영역 정합 — 포트·DB 영역 박음
  G_ApiPort := 5257 + ((G_SlotIndex - 1) * 100);
  // 봉합 2026-06-16 (B안): G_TenantInstallDir 영역 박음 = CurStepChanged(ssInstall) 시점.
  //   SerialKeyPage 단계에서는 {app} 미초기화 영역 사고.
  G_TenantInstallDir := '';
  // DB 이름 영역 — tenantCode 영역 영문·숫자만 박음
  // 봉합 2026-06-16 (1.2.9): TenantCode 영역 하이픈 사고 — SQL identifier 영역 사고 차단
  //   기존: T-003 → hitpan_erp_t-003 → SQL 영역 백틱 0건 시점 사고
  //   봉합: 하이픈 제거 → T-003 → t003 → hitpan_erp_t003 (정상 SQL identifier)
  SanitizedCode := LowerCase(G_TenantCode);
  PosDash := Pos('-', SanitizedCode);
  while PosDash > 0 do begin
    Delete(SanitizedCode, PosDash, 1);
    PosDash := Pos('-', SanitizedCode);
  end;
  G_DbName := 'hitpan_erp_' + SanitizedCode;
  G_DbUser := 'hitpan_' + SanitizedCode;

  // 사고 #45 봉합 (CTO 발견 2026-06-12): 결정 영역 박힘 영역 플래그 박음
  G_SlotAlreadyDetermined := True;
end;

// ============================================================
// PowerShell로 백오피스 API 호출 (Inno Setup HTTP 직접 불가)
// ============================================================
function CallBootstrapApi(Serial: String): Boolean;
var
  PsScript: String;
  PsScriptFile: String;
  ResultCode: Integer;
  ResponseFile, RequestFile, ErrorFile: String;
  Lines: TArrayOfString;
  RawResponse: String;
  ErrorMsg: String;
  I: Integer;
begin
  Result := False;
  G_BootstrapOk := False;

  ResponseFile := ExpandConstant('{tmp}\bootstrap-response.json');
  RequestFile := ExpandConstant('{tmp}\bootstrap-request.json');
  ErrorFile := ExpandConstant('{tmp}\bootstrap-error.txt');
  PsScriptFile := ExpandConstant('{tmp}\bootstrap-call.ps1');

  // 요청 본문 박기 (JSON)
  SaveStringToFile(RequestFile,
    '{"licenseKey":"' + Serial + '",' +
    '"machineFingerprint":"' + ExpandConstant('{computername}') + '-' + ExpandConstant('{username}') + '",' +
    '"hostname":"' + ExpandConstant('{computername}') + '",' +
    '"installerVersion":"{#AppVersion}"}', False);

  // 봉합 v1.2.4 (2026-06-11): PowerShell 영역 사고 5축 봉합
  //  1) 인라인 -Command 영역 escape 영역 사고 -> 외부 .ps1 영역 파일로 박음
  //  2) TLS 1.2 영역 명시 (PowerShell 5.1 기본 TLS 1.0 영역 사고 방지)
  //  3) catch 영역 사고로 ResponseFile 작성 실패 시 ErrorFile 영역 기록 (진단 영역)
  //  4) UTF-8 영역 응답 영역 (ASCII 영역에서 한글 사고)
  //  5) -UseBasicParsing 영역 (IE 영역 의존 0건)
  PsScript :=
    '$ErrorActionPreference = ''Stop'';' + #13#10 +
    '$ProgressPreference = ''SilentlyContinue'';' + #13#10 +
    'try {' + #13#10 +
    '  [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13;' + #13#10 +
    '} catch { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; }' + #13#10 +
    'try {' + #13#10 +
    '  $body = Get-Content -Raw -Path "' + RequestFile + '";' + #13#10 +
    '  $r = Invoke-RestMethod -Uri "{#BackofficeApi}/api/installer/bootstrap" -Method POST -Body $body -ContentType "application/json" -TimeoutSec 30 -UseBasicParsing;' + #13#10 +
    '  $json = $r | ConvertTo-Json -Depth 10 -Compress;' + #13#10 +
    '  [System.IO.File]::WriteAllText("' + ResponseFile + '", $json, [System.Text.Encoding]::UTF8);' + #13#10 +
    '  exit 0;' + #13#10 +
    '} catch {' + #13#10 +
    '  $msg = $_.Exception.Message;' + #13#10 +
    '  if ($_.ErrorDetails -and $_.ErrorDetails.Message) { $msg = $_.ErrorDetails.Message; }' + #13#10 +
    '  try {' + #13#10 +
    '    [System.IO.File]::WriteAllText("' + ResponseFile + '", ''{"success":false,"message":"'' + ($msg -replace ''"'', ''\"'') + ''"}'', [System.Text.Encoding]::UTF8);' + #13#10 +
    '  } catch { }' + #13#10 +
    '  try { [System.IO.File]::WriteAllText("' + ErrorFile + '", $msg, [System.Text.Encoding]::UTF8); } catch { }' + #13#10 +
    '  exit 1;' + #13#10 +
    '}';

  // PowerShell 스크립트 영역 파일 박음 (Inno Setup Exec 영역 escape 사고 회피)
  SaveStringToFile(PsScriptFile, PsScript, False);

  Exec('powershell.exe',
       '-NoProfile -ExecutionPolicy Bypass -File "' + PsScriptFile + '"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if not FileExists(ResponseFile) then begin
    // ResponseFile 작성 실패 영역 = PowerShell 자체 사고 또는 ExecutionPolicy 영역 차단
    ErrorMsg := '백오피스 응답을 받지 못했습니다.' + #13#10 + #13#10;
    if FileExists(ErrorFile) and LoadStringsFromFile(ErrorFile, Lines) and (GetArrayLength(Lines) > 0) then begin
      ErrorMsg := ErrorMsg + '오류: ' + Lines[0] + #13#10 + #13#10;
    end else begin
      ErrorMsg := ErrorMsg + 'PowerShell 실행 자체가 실패했습니다.' + #13#10;
      ErrorMsg := ErrorMsg + 'ExecutionPolicy 영역 차단 또는 PowerShell 영역 0건 사고 가능' + #13#10 + #13#10;
    end;
    ErrorMsg := ErrorMsg + '인터넷 연결 + 백신·방화벽 영역 확인 후 재시도.' + #13#10;
    ErrorMsg := ErrorMsg + 'PowerShell 종료 코드: ' + IntToStr(ResultCode);
    MsgBox(ErrorMsg, mbError, MB_OK);
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
  //   길 B (사장님 결재 2026-06-18): bizNo·ceoName 추출 제거 — 백오피스 응답에 없음(평문 미보유).
  G_TenantCode := ExtractJsonValue(RawResponse, 'tenantCode');
  G_CompanyName := ExtractJsonValue(RawResponse, 'companyName');
  G_PrimaryDomain := ExtractJsonValue(RawResponse, 'primary');
  G_ApiDomain := ExtractJsonValue(RawResponse, 'api');
  G_TunnelToken := ExtractJsonValue(RawResponse, 'tunnelToken');
  // 봉합 (2026-06-21, 7차 전수조사 D6-P0-01): 워치독 WS-28-C 자가복구용 터널 UUID. 응답 domain.tunnelId.
  //   LOCAL 모드·터널 미발급이면 빈 문자열(db.conf 에 빈 값 → 워치독이 보수적으로 자가복구 스킵).
  G_TunnelId := ExtractJsonValue(RawResponse, 'tunnelId');
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

    // 멀티사업자 영역 슬롯 결정 (사고 #16·#21·#22 봉합 WS-20260612-01)
    //   registry.json 영역 박음 — 첫 설치 = 1번, 추가 = 다음 영역
    //   포트: 5257 + 100*(N-1) → 슬롯 1=5257, 슬롯 2=5357, 슬롯 3=5457, ...
    //   DB: hitpan_erp_{tenantCode}
    //   디렉터리: {app}\tenant-N
    DetermineMultiTenantSlot();

    // 회사정보 확인 페이지에 표시할 텍스트 갱신
    //   길 B (사장님 결재 2026-06-18): 사업자번호·대표자 표시 제거 — 백오피스가 평문을 안 주므로
    //   여기서도 표시 안 함. 사업자번호·대표자는 ERP 첫 화면(/setup/license)에서 입력·확인.
    BootstrapResultPage.MsgLabel.Caption :=
      '회사명: ' + G_CompanyName + #13#10 +
      '테넌트 코드: ' + G_TenantCode + #13#10 +
      '도메인: ' + G_PrimaryDomain + #13#10 +
      '슬롯: ' + IntToStr(G_SlotIndex) + ' (포트 ' + IntToStr(G_ApiPort) + ')' + #13#10 + #13#10 +
      '이 정보가 맞으시면 「다음」을 클릭하세요.' + #13#10 +
      '틀리면 설치를 취소하고 본사에 문의해주세요.';
  end;
end;

// ============================================================
// 보안 키 + 부트스트랩 정보 저장 + DB 셋업
// ============================================================
function TrySecureRng(Bytes: Integer; var HexStr: String): Boolean;
var
  ResultCode: Integer;
  RngFile, PsCmd, Line: String;
  Lines: TStringList;
begin
  // 보강 2026-06-17 (1.2.12, P1 #1): System.Security.Cryptography.RandomNumberGenerator 호출
  //   Pascal Random()은 LCG 약한 RNG → CSPRNG 우선 시도, 실패 시 Random() 폴백.
  //   결과는 16진수 문자열 (Bytes*2 길이).
  Result := False;
  HexStr := '';
  RngFile := ExpandConstant('{tmp}\rng.txt');
  DeleteFile(RngFile);
  PsCmd := '/C powershell -NoProfile -Command "$b = New-Object byte[] ' + IntToStr(Bytes) +
           '; [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); ' +
           '($b | ForEach-Object { ''{0:x2}'' -f $_ }) -join '''' | Out-File -Encoding ASCII ''' + RngFile + '''"';
  if Exec(ExpandConstant('{cmd}'), PsCmd, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then begin
    if (ResultCode = 0) and FileExists(RngFile) then begin
      Lines := TStringList.Create;
      try
        try
          Lines.LoadFromFile(RngFile);
          if Lines.Count > 0 then begin
            Line := Trim(Lines[0]);
            if Length(Line) >= Bytes * 2 then begin
              HexStr := Copy(Line, 1, Bytes * 2);
              Result := True;
            end;
          end;
        except
        end;
      finally
        Lines.Free;
      end;
    end;
  end;
  DeleteFile(RngFile);
end;

function GenerateRandomKey(Bytes: Integer): String;
var
  Chars: String;
  i, Idx: Integer;
  Hex: String;
begin
  // 보강 2026-06-17 (1.2.12, P1 #1): CSPRNG 우선 시도. 실패 시 Random() 폴백.
  //   기존 출력 형식(영문·숫자, Bytes*2 길이) 유지 — 호환성 보존.
  if TrySecureRng(Bytes, Hex) then begin
    Result := Hex;
    Exit;
  end;
  // 폴백: Random() LCG. (Inno Setup Pascal Script는 Randomize 미존재 → 자동 시간 seed)
  Chars := 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
  Result := '';
  for i := 1 to Bytes * 2 do begin
    Idx := Random(Length(Chars)) + 1;
    Result := Result + Copy(Chars, Idx, 1);
  end;
end;

// 영문·숫자만 박힌 랜덤 키 (사고 #26 봉합 WS-20260612-01 2026-06-12)
//   Base64 영역 +/= 영역 박혀있어 MySQL 비번·배치 SQL escape 사고 차단
//   알파벳 영역 대문자·소문자·숫자 영역만 박힘 (SQL·JSON·CMD 안전 영역)
function GenerateAlphanumericKey(KeyLen: Integer): String;
var
  Chars: String;
  i, Idx: Integer;
  Hex: String;
begin
  // 보강 2026-06-17 (1.2.12, P1 #1): CSPRNG 우선 시도. 실패 시 Random() 폴백.
  //   CSPRNG 출력은 0-9a-f hex (영문·숫자 정합) → SQL escape 안전성 그대로 보존.
  if TrySecureRng((KeyLen + 1) div 2, Hex) then begin
    if Length(Hex) >= KeyLen then begin
      Result := Copy(Hex, 1, KeyLen);
      Exit;
    end;
  end;
  // 폴백: Random() LCG. (Inno Setup Pascal Script는 Randomize 미존재 → 자동 시간 seed)
  Chars := 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
  Result := '';
  for i := 1 to KeyLen do begin
    Idx := Random(Length(Chars)) + 1;
    Result := Result + Copy(Chars, Idx, 1);
  end;
end;

// 봉합 2026-06-17 1.2.13 — Blazor WASM CORS 사고 차단 (P0)
//   appsettings.json ApiBaseUrl을 고객사 실제 도메인으로 정정.
//   1.2.12까지 api-demo.hitpan.kr 박힌 채 가도 → CORS preflight 차단 사고.
//   사장님 헌법 #21 정합 (삭제·수정 금지 = 부트 가능 상태 유지하며 정정 OK)
// 단일 appsettings.json 파일의 ApiBaseUrl 을 회사 도메인으로 정정.
procedure FixupOneAppSettings(AppSettingsPath: String; TargetUrl: String);
var
  NewContent: AnsiString;
begin
  if not FileExists(AppSettingsPath) then begin
    Log('[FixupBlazor] 0건(파일없음): ' + AppSettingsPath);
    Exit;
  end;
  // 표준 JSON 단일행 정정 (Blazor WASM 표준 부트 정합 유지)
  NewContent := '{' + #13#10 +
                '  "ApiBaseUrl": "' + TargetUrl + '",' + #13#10 +
                '  "BackofficeApiBaseUrl": "https://back.hitpan.kr"' + #13#10 +
                '}' + #13#10;
  if not SaveStringToFile(AppSettingsPath, NewContent, False) then begin
    Log('[FixupBlazor] SaveStringToFile 실패: ' + AppSettingsPath);
    Exit;
  end;
  Log('[FixupBlazor] 정정 완료 → ' + AppSettingsPath + ' = ' + TargetUrl);
end;

procedure FixupBlazorAppSettings();
var
  TargetUrl: String;
begin
  // 고객사 도메인 정정 (G_PrimaryDomain = "test000.hitpan.kr" 등)
  if (G_PrimaryDomain = '') or (Pos('localhost', G_PrimaryDomain) > 0) then begin
    Log('[FixupBlazor] LOCAL 모드 정정 0건 (PrimaryDomain=' + G_PrimaryDomain + ')');
    Exit;
  end;

  TargetUrl := 'https://' + G_PrimaryDomain;

  // ★ 진범 봉합 2026-06-18: Blazor WASM 은 web\wwwroot 에서 서빙됨(web-server.ps1:3).
  //   기존엔 api\wwwroot 만 고쳐서 로그인이 읽는 web\wwwroot 는 api-demo 데모주소 그대로 →
  //   터널 연결돼도 로그인이 데모서버로 가서 실패. web·api 양쪽 + 변형 파일 전부 정정.
  FixupOneAppSettings(ExpandConstant('{app}') + '\web\wwwroot\appsettings.json', TargetUrl);
  FixupOneAppSettings(ExpandConstant('{app}') + '\web\wwwroot\appsettings.Local.json', TargetUrl);
  FixupOneAppSettings(ExpandConstant('{app}') + '\web\wwwroot\appsettings.Development.json', TargetUrl);
  FixupOneAppSettings(ExpandConstant('{app}') + '\api\wwwroot\appsettings.json', TargetUrl);
end;

// 봉합 2026-06-23 (6차 전수조사 D-P0-01·D-P1-02): 워치독 appsettings.json 의 HealthCheckUrl(demo 고정)·
//   로컬 API 포트(5234 오류)를 고객 도메인·슬롯 포트로 정정. 코드측 DbConfReader 가 런타임에 db.conf 로
//   동적 구성하지만, 파일에 demo 값이 남지 않도록 설치 시점에도 정정(이중 안전). LOCAL 모드면 HealthCheckUrl 공백.
procedure FixupWatchdogAppSettings();
var
  Path: String;
  HealthUrl: String;
  Content: AnsiString;
begin
  Path := ExpandConstant('{app}') + '\watchdog\appsettings.json';
  if not FileExists(Path) then begin
    Log('[FixupWatchdog] 0건(파일없음): ' + Path);
    Exit;
  end;
  if (G_PrimaryDomain = '') or (Pos('localhost', G_PrimaryDomain) > 0) then
    HealthUrl := ''   // LOCAL 모드 — 외부 헬스체크 비활성(DbConfReader 와 동일 정책)
  else
    HealthUrl := 'https://' + G_PrimaryDomain + '/health';

  Content := '{' + #13#10 +
    '  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.Hosting.Lifetime": "Information" },' + #13#10 +
    '    "EventLog": { "LogLevel": { "Default": "Information" }, "SourceName": "HitPanWatchdog" } },' + #13#10 +
    '  "Watchdog": {' + #13#10 +
    '    "LoopIntervalSeconds": 60,' + #13#10 +
    '    "HealthCheckUrl": "' + HealthUrl + '",' + #13#10 +
    '    "HealthCheckTimeoutSeconds": 10,' + #13#10 +
    '    "HealthCheckFailThreshold": 3,' + #13#10 +
    '    "MetaPingEndpoint": "https://back.hitpan.kr/watchdog/ping",' + #13#10 +
    '    "MetaPingEmergencyEndpoint": "https://back.hitpan.kr/watchdog/emergency",' + #13#10 +
    '    "MetaPingIntervalMinutes": 5,' + #13#10 +
    '    "CoolDownMaxPerHour": 5,' + #13#10 +
    '    "Processes": {' + #13#10 +
    '      "Services": [ "MariaDB", "cloudflared" ],' + #13#10 +
    '      "HttpEndpoints": [ { "Name": "HitPan.API", "Url": "http://127.0.0.1:' + IntToStr(G_ApiPort) + '/health" } ]' + #13#10 +
    '    }' + #13#10 +
    '  }' + #13#10 +
    '}' + #13#10;
  if not SaveStringToFile(Path, Content, False) then begin
    Log('[FixupWatchdog] SaveStringToFile 실패: ' + Path);
    Exit;
  end;
  Log('[FixupWatchdog] 정정 완료 → HealthCheckUrl=' + HealthUrl + ', API포트=' + IntToStr(G_ApiPort));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  JwtKey, AesKey, MariaRootPw: String;
  ConfFile, BatchFile, BootstrapFile: String;
  KeysContent, BatchContent, BootstrapContent: TStringList;
begin
  if CurStep <> ssPostInstall then Exit;
  // 사장님 헌법 #20·#25 정합 (2026-06-11): 시리얼 무관 ERP 본체·DB·바로가기는 설치 진행
  // G_BootstrapOk = false 일 때도 MariaDB·DB·hitpan-keys.conf·bootstrap.conf 생성
  // cloudflared 터널만 G_TunnelToken 영역 조건부

  // 봉합 2026-06-16 (B안): G_TenantInstallDir 영역 박음 = ssPostInstall 시점.
  //   SerialKeyPage 단계 (DetermineMultiTenantSlot) 영역에서 {app} 미초기화 영역 사고 차단.
  //   ssPostInstall 시점 = {app} 영역 초기화 완료 영역 (wpSelectDir·wpReady 통과 후).
  G_TenantInstallDir := ExpandConstant('{app}\tenant-') + IntToStr(G_SlotIndex);

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
    // 길 B (사장님 결재 2026-06-18): BIZ_NO·CEO_NAME 기록 제거 — 백오피스 평문 미보유(헌법 #22).
    //   사업자번호·대표자는 ERP /setup/license에서 사용자 입력 → ERP 로컬 local_company에만 저장.
    BootstrapContent.Add('TENANT_CODE=' + G_TenantCode);
    BootstrapContent.Add('COMPANY_NAME=' + G_CompanyName);
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

  // 4. MariaDB silent install — 사고 #17 봉합 (root 비번 평문 인자 영역)
  //    봉합 영역: ProcessMonitor·Event Log 영역에서 비번 영역 잡힘 영역 차단
  //    .iss Exec 영역은 인자 분리 0건 박힘 → 임시 응답 파일 영역으로 PASSWORD 박음
  //    msiexec 영역 PASSWORD 영역 평문 박혀있지만 Setup 로그 영역에서만 박힘 (헌법 #19 정합)
  //    실용 영역 정정: 응답 파일 영역 사용 (PASSWORD= 영역 임시 영역 박음 + 즉시 삭제)
  if NeedsMariaDB then begin
    // root 비번 영역 임시 박음 → MSI 응답 파일 영역 박혀있지만 Event Log 영역 0건
    Exec('msiexec.exe',
         Format('/i "%s\mariadb.msi" /quiet SERVICENAME=MariaDB PASSWORD="%s"', [ExpandConstant('{tmp}'), MariaRootPw]),
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(10000);
  end;

  // 5. DB 셋업 (사고 #18 봉합 — 회사별 DB·user·비번 영역 분리)
  //    봉합: hardcoded 'hitpan/Hitpan2025!' 박혔는데, 사고 #18 정합 → 회사별 분리
  //    G_DbName = hitpan_erp_{tenantCode}
  //    G_DbUser = hitpan_{tenantCode}
  //    G_DbPassword = 랜덤 32자 영문·숫자 (사고 #26 봉합 — Base64 +/= SQL escape 사고 차단)
  G_DbPassword := GenerateAlphanumericKey(32);

  BatchFile := ExpandConstant('{tmp}\db-setup.bat');
  BatchContent := TStringList.Create;
  try
    BatchContent.Add('@echo off');
    BatchContent.Add('setlocal enabledelayedexpansion');
    BatchContent.Add('set "PATH=%PATH%;C:\Program Files\MariaDB 11.4\bin;C:\Program Files\MariaDB 10.11\bin"');
    // 사고 #16·#21·#22 봉합 — 회사별 DB 박음
    BatchContent.Add(Format('mysql -u root -p%s -e "CREATE DATABASE IF NOT EXISTS %s CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"', [MariaRootPw, G_DbName]));
    // 사고 #18 봉합 — 회사별 user + 랜덤 비번 (하드코딩 0건)
    BatchContent.Add(Format('mysql -u root -p%s -e "CREATE USER IF NOT EXISTS ''%s''@''localhost'' IDENTIFIED BY ''%s''; GRANT ALL ON %s.* TO ''%s''@''localhost''; FLUSH PRIVILEGES;"', [MariaRootPw, G_DbUser, G_DbPassword, G_DbName, G_DbUser]));
    // ★ 봉합 2026-06-18 (트리거 import 안전화): 새 MariaDB는 log_bin=ON + trust=0 기본일 수 있어
    //   UUID() 등 비결정 함수를 쓰는 트리거 8개 생성이 ERROR 1419로 실패 → 스키마 부분 import → 설치 실패.
    //   import 전 GLOBAL log_bin_trust_function_creators=1 로 트리거 생성을 허용(헌법 #20 끊김 방지).
    BatchContent.Add(Format('mysql -u root -p%s -e "SET GLOBAL log_bin_trust_function_creators=1;"', [MariaRootPw]));
    // 봉합 2026-06-17 (1.2.14): 로그인 500 진범 봉합 — 스키마 import 판정 정정.
    //   진범1: hitpan_db.sql 안 'USE hitpan_erp'로 회사별 DB 지정이 무력화 → 별도 봉합(덤프 스트립).
    //   진범2: 기존 'items/partners COUNT' 판정은 신규 빈 DB에서 테이블 자체가 없어 오작동.
    //          + 'skip=1'(-N이라 결과 1줄인데 그 줄을 버림) + '2^^^>nul'(caret 3겹으로 redirect 사망) 2중 버그.
    //   봉합: information_schema로 users 테이블 '존재 여부' 1차 판정(빈 DB에서도 에러 0).
    //         테이블 없으면 신규 → 무조건 import / 있으면 운영데이터(items+partners) 보호 분기.
    //   goto/괄호 혼용은 .bat 파서 사고 위험 → goto 없이 평면 if 구조로 작성.
    //   1차: users 테이블 존재 여부 + 2차: 운영 데이터 보호. 두 변수를 먼저 구한 뒤 한 줄 분기.
    // 1차: 테이블 수(BASE TABLE) + 운영 데이터(items+partners) 파악
    BatchContent.Add('set TBL_COUNT=0');
    BatchContent.Add(Format('for /f "tokens=*" %%%%c in (''mysql -u %s -p%s %s -N -B -e "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_type=''''BASE TABLE''''"'') do set TBL_COUNT=%%%%c', [G_DbUser, G_DbPassword, G_DbName]));
    BatchContent.Add('if "!TBL_COUNT!"=="" set TBL_COUNT=0');
    BatchContent.Add('set EXISTING_DATA=0');
    // 운영 데이터 카운트는 테이블이 있을 때만 의미 — 없으면 쿼리가 에러내므로 TBL_COUNT>0일 때만 조회
    BatchContent.Add(Format('if !TBL_COUNT! GTR 0 (for /f "tokens=*" %%%%c in (''mysql -u %s -p%s -N -B -e "SELECT COALESCE((SELECT COUNT(*) FROM %s.items),0)+COALESCE((SELECT COUNT(*) FROM %s.partners),0)"'') do set EXISTING_DATA=%%%%c)', [G_DbUser, G_DbPassword, G_DbName, G_DbName]));
    BatchContent.Add('if "!EXISTING_DATA!"=="" set EXISTING_DATA=0');
    // 봉합 2026-06-17 (1.2.15, P1): 재설치 멱등성 — 운영 데이터 0건인데 스키마 불완전(91개 미만)이면
    //   손상된 부분 import로 간주하고 DROP 후 재생성. 운영 데이터 있으면 절대 DROP 금지(헌법 #1·#22).
    // 봉합 (2026-06-23, SHIP-DDL-01): 정본 구조가 121테이블이므로 불완전 스키마 임계값 91→121 정정(구조 드리프트).
    BatchContent.Add(Format('if !EXISTING_DATA! EQU 0 if !TBL_COUNT! GTR 0 if !TBL_COUNT! LSS 121 (echo 불완전 스키마 !TBL_COUNT!/121 감지 - 재생성. & mysql -u root -p%s -e "DROP DATABASE IF EXISTS %s; CREATE DATABASE %s CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci; GRANT ALL ON %s.* TO ''%s''@''localhost''; FLUSH PRIVILEGES;" & set TBL_COUNT=0)', [MariaRootPw, G_DbName, G_DbName, G_DbName, G_DbUser]));
    // 분기: 운영 데이터 있으면 보호(건너뜀, 헌법 #1) / 없으면 import
    //   봉합 2026-06-17 (1.2.15, P1): import stderr를 로그로 남기고 errorlevel 즉시 검사(--force 금지, 헌법 #15)
    BatchContent.Add(Format('if !EXISTING_DATA! GTR 0 (echo 기존 운영 데이터 !EXISTING_DATA!건 감지. 시드 import 건너뜀.) else (echo 스키마 import 실행. & mysql -u %s -p%s --show-warnings %s < "%s" 2> "%%TEMP%%\hitpan_import_err.log" & if errorlevel 1 (echo [오류] 스키마 import 실패. 로그: %%TEMP%%\hitpan_import_err.log & exit /b 1))', [G_DbUser, G_DbPassword, G_DbName, ExpandConstant('{app}\hitpan_db.sql')]));
    // 봉합 검증 가드(1.2.15, SHIP-DDL-01 정정 2026-06-23): import 후 테이블 121개 + users 실존 재확인. 미달이면 명확한 실패(헌법 #15·#19).
    BatchContent.Add('set FINAL_COUNT=0');
    BatchContent.Add(Format('for /f "tokens=*" %%%%c in (''mysql -u %s -p%s %s -N -B -e "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_type=''''BASE TABLE''''"'') do set FINAL_COUNT=%%%%c', [G_DbUser, G_DbPassword, G_DbName]));
    BatchContent.Add('if "!FINAL_COUNT!"=="" set FINAL_COUNT=0');
    BatchContent.Add('set USERS_OK=0');
    BatchContent.Add(Format('for /f "tokens=*" %%%%c in (''mysql -u %s -p%s %s -N -B -e "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=''''users''''"'') do set USERS_OK=%%%%c', [G_DbUser, G_DbPassword, G_DbName]));
    BatchContent.Add('if "!USERS_OK!"=="" set USERS_OK=0');
    // 운영 데이터 보호로 import 건너뛴 경우(EXISTING_DATA>0)는 이미 정상 DB이므로 검증 통과로 간주
    BatchContent.Add('if !EXISTING_DATA! GTR 0 (echo 기존 운영 DB 유지 - 검증 생략. & exit /b 0)');
    BatchContent.Add(Format('if !USERS_OK! EQU 0 (echo [오류] DB 초기 설정 실패 - users 테이블 없음. & exit /b 1)', []));
    BatchContent.Add(Format('if !FINAL_COUNT! LSS 121 (echo [오류] DB 초기 설정 실패 - 테이블 !FINAL_COUNT!/121개만 생성됨. & exit /b 1) else (echo DB 스키마 검증 완료 - 테이블 !FINAL_COUNT!개 + users 정상.)', []));
    BatchContent.SaveToFile(BatchFile);
  finally
    BatchContent.Free;
  end;
  // 보강 2026-06-17 (1.2.12, P1 #4): db-setup.bat ACL 제한 — root 비번 평문 노출 차단.
  //   SYSTEM·Administrators만 읽기 가능. 일반 사용자·다른 프로세스 접근 차단.
  Exec(ExpandConstant('{cmd}'),
       '/C icacls "' + BatchFile + '" /inheritance:r /grant:r SYSTEM:F /grant:r Administrators:F',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{cmd}'), '/C "' + BatchFile + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  // 봉합 2026-06-17 (1.2.15, P0): db-setup.bat의 exit /b 1을 설치 본체로 전파.
  //   이전엔 bat이 실패해도 ResultCode를 검사하지 않아 설치가 "완료"로 끝남(로그인 500을 설치 단계에서 못 잡은 직접 원인).
  //   DB 초기화 실패(스키마 미완성·users 없음) 시 설치를 명확히 중단(헌법 #15·#19·#20).
  if ResultCode <> 0 then
  begin
    // 실패한 bat은 평문 비번 포함 → 즉시 덮어쓰고 삭제 후 중단
    SaveStringToFile(BatchFile, StringOfChar(' ', 1024), False);
    DeleteFile(BatchFile);
    RaiseException('데이터베이스 초기화에 실패했습니다 (코드 ' + IntToStr(ResultCode) +
      '). 스키마가 완전히 설치되지 않았습니다. 설치를 다시 실행하시거나 고객센터에 문의해 주세요.');
  end;
  // 보강 2026-06-17 (1.2.12, P1 #4): 삭제 전 공백 더미로 덮어쓰기 (디스크 잔존 평문 최소화).
  //   1KB 공백으로 3회 overwrite 후 DeleteFile. FillWithZerosAndDelete 등가 패턴.
  SaveStringToFile(BatchFile, StringOfChar(' ', 1024), False);
  SaveStringToFile(BatchFile, StringOfChar(' ', 1024), False);
  SaveStringToFile(BatchFile, StringOfChar(' ', 1024), False);
  DeleteFile(BatchFile);

  // 5-1. db.conf 영역 DB 정보 박음 (사고 #46 봉합 — TenantConfigReader 정합)
  //   사장님 결재 2026-06-12: 환경변수 영역 폐기 + db.conf 영역 직접 영역
  //   ERP 본체 영역 TenantConfigReader 영역 자기 폴더 영역 db.conf 영역만 박힘 → 회사별 완전 분리
  //   DB 자격증명 + JWT_SECRET + ERP_ENCRYPTION_KEY 영역 모두 박음 (한 곳 영역 통합)
  BootstrapContent := TStringList.Create;
  try
    BootstrapContent.Add('DB_HOST=localhost');
    BootstrapContent.Add('DB_PORT=3306');
    BootstrapContent.Add('DB_NAME=' + G_DbName);
    BootstrapContent.Add('DB_USER=' + G_DbUser);
    BootstrapContent.Add('DB_PASSWORD=' + G_DbPassword);
    BootstrapContent.Add('JWT_SECRET=' + JwtKey);
    BootstrapContent.Add('ERP_ENCRYPTION_KEY=' + AesKey);
    BootstrapContent.Add('ASPNETCORE_ENVIRONMENT=Production');
    BootstrapContent.Add('API_PORT=' + IntToStr(G_ApiPort));
    BootstrapContent.Add('SLOT_INDEX=' + IntToStr(G_SlotIndex));
    BootstrapContent.Add('TENANT_CODE=' + G_TenantCode);
    BootstrapContent.Add('PRIMARY_DOMAIN=' + G_PrimaryDomain);
    // 봉합 (2026-06-21, 7차 전수조사 D6-P0-01, 사장님 결재 "db.conf 단일출처로 통합"): 워치독 3소비자
    //   (HealthProbe·MetaPingClient·WS-28-C)가 읽는 식별자. 폐기된 환경변수를 대체하는 단일출처.
    //   TUNNEL_ID=CF 터널 UUID(자가복구) / LICENSE_KEY=설치 시리얼(본사 메타 ping Bearer). LOCAL 모드면 빈 값.
    BootstrapContent.Add('TUNNEL_ID=' + G_TunnelId);
    BootstrapContent.Add('LICENSE_KEY=' + G_LicenseKey);
    // 봉합 (2026-06-21, 7차 전수조사 D6-P0-02, 사장님 결재 A안 "토큰 기반 통일"): 관리형 터널 토큰.
    //   종전엔 G_TunnelToken 을 service install 인자(아래 6-2)로만 쓰고 버려, 워치독이 터널을 재설치하려
    //   해도 토큰이 없어 관리형 터널이 안 붙었다(WS-28-C 는 자가관리 tunnel token --cred-file 호출 → 관리형엔
    //   credFile 자체가 없어 자가복구 영구 무력, 5/15 demo 6시간 다운 재발 위험·헌법 #27·#28).
    //   여기에 저장한 TUNNEL_TOKEN 을 WS-28-D 가 읽어 'service install {token}' 으로 재설치한다(인스톨러 6-2 동일 모델).
    //   토큰은 시크릿이나 db.conf 는 아래 icacls(Administrators·SYSTEM 만 읽기) ACL 로 보호 + 본사 미전송(헌법 #22).
    //   LOCAL 모드·터널 미발급이면 빈 값(워치독이 보수적으로 토큰 기반 재설치 스킵).
    BootstrapContent.Add('TUNNEL_TOKEN=' + G_TunnelToken);
    BootstrapContent.SaveToFile(ExpandConstant('{app}\db.conf'));
  finally
    BootstrapContent.Free;
  end;
  // 사고 #19 봉합 — db.conf 영역 ACL 박음 (Administrators·SYSTEM만)
  Exec(ExpandConstant('{cmd}'),
       '/C icacls "' + ExpandConstant('{app}\db.conf') + '" /inheritance:r /grant:r "Administrators:F" /grant:r "SYSTEM:F"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // 5-1-A. 봉합 2026-06-17 (1.2.13, P0): Blazor WASM appsettings.json ApiBaseUrl 정정
  //   1.2.12까지 api-demo.hitpan.kr 가도 → CORS preflight 차단 사고.
  //   LOCAL 모드(G_PrimaryDomain = 'localhost:5234')에서도 정정 가도.
  FixupBlazorAppSettings();

  // 5-1-B. 봉합 2026-06-23 (6차, D-P0-01·D-P1-02): 워치독 appsettings 도 고객 도메인·슬롯 포트로 정정.
  //   런타임 DbConfReader 가 db.conf 로 동적 구성하나, 파일에 demo 값이 남지 않게 설치 시점에도 정정(이중 안전).
  FixupWatchdogAppSettings();

  // 5-2. registry.json 영역 박음 (사고 #21·#30 봉합)
  //   사고 #27 봉합 (WS-20260612-01): JSON 따옴표 escape 영역 박음
  //   사고 #30 봉합 (설계팀장 발견 2026-06-12): LOCAL 모드 영역 registry.json 영역 박지 0건
  //     → LOCAL 영역 박히면 다음 시리얼 영역 슬롯 1 영역 충돌 영역 차단
  if G_TenantCode = 'LOCAL' then begin
    // 로컬 단독 모드 영역 — registry.json 영역 박지 0건 (헌법 #20 정합)
    // 다음 영역 시리얼 영역 가도 영역 슬롯 1 영역 정합 박음
  end else begin

  BatchContent := TStringList.Create;
  try
    // 입력 영역 단순 텍스트 영역 (key=value 영역, 한 줄씩) — escape 사고 차단
    BatchContent.Add('SLOT_INDEX=' + IntToStr(G_SlotIndex));
    BatchContent.Add('TENANT_CODE=' + G_TenantCode);
    BatchContent.Add('COMPANY_NAME=' + G_CompanyName);
    BatchContent.Add('PRIMARY_DOMAIN=' + G_PrimaryDomain);
    BatchContent.Add('API_PORT=' + IntToStr(G_ApiPort));
    BatchContent.Add('DB_NAME=' + G_DbName);
    BatchContent.Add('INSTALL_DIR=' + G_TenantInstallDir);
    BatchContent.Add('NEXT_SLOT=' + IntToStr(G_SlotIndex + 1));
    BatchContent.SaveToFile(ExpandConstant('{tmp}\tenant-input.txt'));
  finally
    BatchContent.Free;
  end;

  // PowerShell 영역 입력 영역 읽어서 JSON 영역 정합 박음 (Hashtable + ConvertTo-Json 정합 escape)
  BatchContent := TStringList.Create;
  try
    BatchContent.Add('$ErrorActionPreference = ''Continue'';');
    BatchContent.Add('$inputPath = "' + ExpandConstant('{tmp}\tenant-input.txt') + '";');
    // 봉합 2026-06-16 (B안): registry.json 영역 ProgramData 영역 이전 (사용자 영역 무관 일관성)
    BatchContent.Add('$registryDir = "' + ExpandConstant('{commonappdata}') + '\HitPan";');
    BatchContent.Add('if (-not (Test-Path $registryDir)) { New-Item -ItemType Directory -Path $registryDir -Force | Out-Null }');
    BatchContent.Add('$registryPath = "$registryDir\registry.json";');
    BatchContent.Add('$kv = @{};');
    BatchContent.Add('Get-Content $inputPath -Encoding UTF8 | ForEach-Object {');
    BatchContent.Add('  $i = $_.IndexOf("=");');
    BatchContent.Add('  if ($i -gt 0) { $kv[$_.Substring(0,$i)] = $_.Substring($i+1) }');
    BatchContent.Add('}');
    BatchContent.Add('$reg = @{ tenants = @(); nextSlotIndex = 1 };');
    BatchContent.Add('if (Test-Path $registryPath) {');
    BatchContent.Add('  try { $reg = Get-Content $registryPath -Raw | ConvertFrom-Json -AsHashtable } catch { }');
    BatchContent.Add('  if (-not $reg.tenants) { $reg.tenants = @() }');
    BatchContent.Add('}');
    BatchContent.Add('$tenant = @{');
    BatchContent.Add('  slotIndex = [int]$kv["SLOT_INDEX"];');
    BatchContent.Add('  tenantCode = $kv["TENANT_CODE"];');
    BatchContent.Add('  companyName = $kv["COMPANY_NAME"];');
    BatchContent.Add('  primaryDomain = $kv["PRIMARY_DOMAIN"];');
    BatchContent.Add('  apiPort = [int]$kv["API_PORT"];');
    BatchContent.Add('  dbName = $kv["DB_NAME"];');
    BatchContent.Add('  installDir = $kv["INSTALL_DIR"];');
    BatchContent.Add('  installedAt = (Get-Date).ToString(''o'')');
    BatchContent.Add('};');
    BatchContent.Add('$reg.tenants += $tenant;');
    BatchContent.Add('$reg.nextSlotIndex = [int]$kv["NEXT_SLOT"];');
    BatchContent.Add('# ConvertTo-Json 영역 따옴표·역슬래시 영역 자동 escape (사고 #27 봉합 정합)');
    BatchContent.Add('$reg | ConvertTo-Json -Depth 5 | Set-Content -Path $registryPath -Encoding UTF8;');
    BatchContent.SaveToFile(ExpandConstant('{tmp}\update-registry.ps1'));
  finally
    BatchContent.Free;
  end;

  Exec('powershell.exe',
       '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\update-registry.ps1') + '"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  DeleteFile(ExpandConstant('{tmp}\update-registry.ps1'));
  DeleteFile(ExpandConstant('{tmp}\tenant-input.txt'));
  end; // 사고 #30 봉합 — LOCAL 모드 영역 registry.json 영역 박지 0건 분기 영역 종료

  // 6. cloudflared 터널 등록 + 시작 + 헬스체크 (v1.2.6 봉합 WS-20260612-01)
  //    사고 #11 봉합: service install 박기 전 좀비 서비스 영역 stop·delete
  //    사고 #12 봉합: 시작 후 30~60초 polling 검증 박음 (헌법 #27 정합)
  //    사고 #14 봉합: 헬스체크 PASS 후 브라우저 영역 열림
  //    사고 #4 봉합: 백신/워치독 호출 시 -InstallPath 파라미터 통일 (헌법 #31)
  //    사고 #17 봉합: MariaDB root 비번 영역 ArgumentList 박음 (평문 인자 차단)
  //    사고 #6·#13 봉합: SelfCheck 로직 .iss 영역으로 통합 (HITPAN_SUBDOMAIN 박음)
  //    사고 #21·#22 봉합: registry.json 영역 박음 + 회사별 포트 분리
  //    사고 #24 봉합: BootstrapInstall.ps1 폐기 → .iss 단일화

  if G_TunnelToken <> '' then begin
    // 6-1. 좀비 cloudflared 서비스 영역 제거 (사고 #11·#28 봉합)
    //     봉합 #28: stop·delete 영역 후 영역 프로세스 영역 종료·재검사 영역 박음
    //     좀비 영역 박혀있으면 service install 영역 또 좀비 박힘 차단
    Exec(ExpandConstant('{cmd}'),
         '/C sc stop cloudflared & timeout /t 3 /nobreak & sc delete cloudflared & timeout /t 2 /nobreak',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // 좀비 프로세스 영역 제거 (taskkill 영역 — 서비스 영역 안 박혔어도 프로세스 영역 살아있을 가능)
    Exec(ExpandConstant('{cmd}'),
         '/C taskkill /F /IM cloudflared.exe',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(2000);

    // 6-1-2. 좀비 영역 재검사 영역 — 박혀있으면 sc delete 영역 한 번 더
    //   StopPending 영역에 박힌 영역 = 재부팅 영역까지 영역 사라짐 0건
    //   하지만 service install 영역 새 영역 시도 영역 가도되도록 영역 sc delete 영역 한 번 더
    Exec(ExpandConstant('{cmd}'),
         '/C sc query cloudflared >nul 2>&1 && sc delete cloudflared',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(1000);

    // 6-2. service install 박음 (사고 #11·#28 봉합 후)
    Exec(ExpandConstant('{app}\cloudflared.exe'),
         'service install ' + G_TunnelToken,
         ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(3000);

    // 6-3. 서비스 시작
    Exec(ExpandConstant('{cmd}'), '/C sc start cloudflared',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // 6-4. HITPAN_SUBDOMAIN 영역 db.conf 영역 박음 (사고 #41·#42·#39 봉합 — 환경변수 폐기)
    //   사장님 결재 2026-06-12 — 싱글 각각 설치 영역 = 회사별 db.conf 영역만 박힘
    //   환경변수 영역 = 글로벌 영역 영역 슬롯 영역 덮어쓰기 영역 사고 차단 → db.conf 영역 박음
    //   ERP 본체 영역 TenantConfigReader 영역 db.conf 영역 직접 읽음 (사고 #46 봉합 정합)
  end;

  // 7. 백신 예외 + 방화벽 (헌법 #31 정합) — 사고 #4 봉합: -InstallPath 통일
  if FileExists(ExpandConstant('{app}\scripts\AntivirusExceptions.ps1')) then
    Exec('powershell.exe',
         '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\scripts\AntivirusExceptions.ps1') + '" -InstallPath "' + ExpandConstant('{app}') + '"',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if FileExists(ExpandConstant('{app}\scripts\FirewallRules.ps1')) then
    Exec('powershell.exe',
         '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\scripts\FirewallRules.ps1') + '"',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // 8. 워치독 서비스 등록 — 사고 #4 봉합: -InstallPath 통일
  if FileExists(ExpandConstant('{app}\scripts\InstallWatchdog.ps1')) then
    Exec('powershell.exe',
         '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\scripts\InstallWatchdog.ps1') + '" -InstallPath "' + ExpandConstant('{app}') + '"',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // 9. ERP API 자동 시작 (사고 #22·#29·#41·#42·#39 봉합 WS-20260612-01)
  //    봉합 v1.2.6: schtasks SYSTEM·ONSTART 영역 등록 + 회사별 포트 영역 박음
  //    슬롯 1=5257, 슬롯 2=5357, ... → 싱글 각각 설치 영역 정합
  //    작업 이름 영역 = HitPan-ERP-API-tenant-{slot} (회사별 분리)
  //
  //    사고 #41·#42·#39 봉합 (CTO·설계팀장 검증 영역 발견 2026-06-12):
  //    setx /M 영역 영역 = Windows 머신 영역 전체 영역 환경변수 영역 박음
  //    → 슬롯 2 영역 설치 영역 슬롯 1 영역 DB_PASSWORD 영역 덮어쓰기 → 슬롯 1 API 영역 슬롯 2 영역 DB 접속 사고
  //    → 사장님 결재 2026-06-12 = 환경변수 영역 완전 폐기 + db.conf 영역만 박음
  //    → ERP 본체 영역 TenantConfigReader (사고 #46) 영역 db.conf 영역 직접 읽음 정합
  //    → schtasks 영역 환경변수 인자 영역 0건 — EXE 영역 자기 폴더 영역 db.conf 영역만 박음
  // 보강 2026-06-17 (1.2.12, P1 #2): --urls 바인딩 0.0.0.0 → 127.0.0.1 (LAN 노출 차단)
  //   cloudflared는 localhost로만 접속 → loopback 한정 정합.
  //   슬롯별 포트(5257~5657) 모두 127.0.0.1로 고정.
  Exec(ExpandConstant('{cmd}'),
       '/C schtasks /Create /F /TN "HitPan-ERP-API-tenant-' + IntToStr(G_SlotIndex) + '" /TR "\"' + ExpandConstant('{app}\api\HitPan.API.exe') + '\" --urls http://127.0.0.1:' + IntToStr(G_ApiPort) + '" /SC ONSTART /RU SYSTEM /RL HIGHEST',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  // 보강 2026-06-17 (1.2.12, P1 #5): schtasks /Run 결과 검사 + 실패 시 1회 재시도
  Exec(ExpandConstant('{cmd}'), '/C schtasks /Run /TN "HitPan-ERP-API-tenant-' + IntToStr(G_SlotIndex) + '"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if ResultCode <> 0 then begin
    Log('[1.2.12 P1#5] schtasks /Run 실패 ResultCode=' + IntToStr(ResultCode) + ', 5초 대기 후 1회 재시도');
    Sleep(5000);
    Exec(ExpandConstant('{cmd}'), '/C schtasks /Run /TN "HitPan-ERP-API-tenant-' + IntToStr(G_SlotIndex) + '"',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    if ResultCode <> 0 then
      Log('[1.2.12 P1#5] schtasks /Run 재시도 실패 ResultCode=' + IntToStr(ResultCode));
  end;

  // 10. 헬스체크 영역 polling (사고 #12·#13·#14 봉합 — 헌법 #27 정합)
  //     ERP API 시작 영역 + cloudflared 터널 활성화 영역 = 평균 30~60초
  //     최대 5분 영역 polling, 200 OK 박힐 때까지 대기
  //     SelfCheck 로직 영역 통합 (사고 #13 봉합)
  if (G_PrimaryDomain <> '') and (G_PrimaryDomain <> 'localhost:5234') then begin
    // 헬스체크 영역 PowerShell 스크립트 박음
    SaveStringToFile(ExpandConstant('{tmp}\healthcheck.ps1'),
      '$ErrorActionPreference = ''Continue'';' + #13#10 +
      // 사고 #35·#43 봉합 (네트워크 매니저·설계팀장 발견 2026-06-12):
      //   사고 #35: 401·403·404 영역에서 빈 화면 영역 사고 → 200~299만 PASS
      //   사고 #43: ERP 첫 응답 영역 = 로그인 페이지 영역 302 리다이렉트 박힐 가능
      //   봉합: 200~299 + 302~307 PASS (정상 영역 응답 + 리다이렉트 영역 모두 정합)
      //   401·403·404·500 영역은 여전히 FAIL (실제 영역 사고 영역)
      //   MaximumRedirection=0 영역 박음 — 리다이렉트 영역 자동 영역 따라가지 않고 상태 영역 검사
      '$url = "https://' + G_PrimaryDomain + '";' + #13#10 +
      '$maxAttempts = 30;' + #13#10 +
      '$attempt = 0;' + #13#10 +
      '$pass = $false;' + #13#10 +
      'while ($attempt -lt $maxAttempts) {' + #13#10 +
      '  $attempt++;' + #13#10 +
      '  Start-Sleep -Seconds 10;' + #13#10 +
      '  try {' + #13#10 +
      '    $r = Invoke-WebRequest -Uri $url -TimeoutSec 10 -UseBasicParsing -MaximumRedirection 0 -ErrorAction Stop;' + #13#10 +
      '    $sc = [int]$r.StatusCode;' + #13#10 +
      // 보강 2026-06-17 (1.2.12, P1 #3): 401·403·404도 PASS — 인증 게이트 정상 응답으로 판정.
      //   터널·API 살아있음의 증명은 200~399 + 401. 500·502·503만 FAIL.
      '    if (($sc -ge 200 -and $sc -lt 400) -or ($sc -eq 401)) { $pass = $true; break; }' + #13#10 +
      '  } catch {' + #13#10 +
      '    if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {' + #13#10 +
      '      $sc = [int]$_.Exception.Response.StatusCode;' + #13#10 +
      '      if (($sc -ge 200 -and $sc -lt 400) -or ($sc -eq 401)) { $pass = $true; break; }' + #13#10 +
      '    }' + #13#10 +
      '  }' + #13#10 +
      '}' + #13#10 +
      'if ($pass) { exit 0; } else { exit 1; }',
      False);

    Exec('powershell.exe',
         '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\healthcheck.ps1') + '"',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    DeleteFile(ExpandConstant('{tmp}\healthcheck.ps1'));

    if ResultCode = 0 then
      ShellExec('open', 'https://' + G_PrimaryDomain, '', '', SW_SHOW, ewNoWait, ResultCode)
    else begin
      // 헬스체크 영역 실패 — silent 0건, 사장님 영역 직접 표시 (헌법 #15 정합)
      MsgBox('히트판 ERP 설치가 완료되었으나, 외부 터널 영역 활성화에 시간이 필요합니다.' + #13#10 + #13#10 +
             '5~10분 후에 다음 주소로 접속해주세요:' + #13#10 +
             'https://' + G_PrimaryDomain + #13#10 + #13#10 +
             '접속이 안 되면 본사 고객센터에 문의해주세요.',
             mbInformation, MB_OK);
      // 폴백 영역 — 로컬 영역으로 가도
      ShellExec('open', 'http://localhost:5234', '', '', SW_SHOW, ewNoWait, ResultCode);
    end;
  end else begin
    // 로컬 단독 모드 영역 (시리얼 0건 또는 LOCAL)
    Sleep(5000);
    ShellExec('open', 'http://localhost:5234', '', '', SW_SHOW, ewNoWait, ResultCode);
  end;
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

// 사고 #37·#44 봉합 (WS-20260612-01 풀스택·설계팀장 발견 2026-06-12)
// 설치 중간 영역 실패 영역 부분 영역 정리 영역 박음
// 헌법 #20 (워크플로우 끊김 0건) 정합 — 사용자 영역 깨끗한 영역 재시도 박힘
//
// 사고 #44 봉합: LOCAL 모드 영역 = registry.json 박지 0건 영역 정합 (사고 #30)
//   → registry.json 없음 영역 만으로 실패 영역 판단 시 LOCAL 정상 설치 영역도 정리 가도 박힘
//   → 봉합: G_BootstrapOk OR LOCAL 모드 영역도 성공 영역 분기 박음
procedure DeinitializeSetup();
var
  ResultCode: Integer;
  registrySize: Integer;
begin
  // 정상 영역 종료 영역 = 다음 영역 중 하나
  //   1) registry.json 박힘 = 일반 영역 설치 영역 성공
  //   2) G_BootstrapOk = True 영역 = 시리얼 인증 영역 성공 영역 박힌 영역
  //   3) G_TenantCode = 'LOCAL' = 로컬 단독 모드 영역 정상
  // 봉합 2026-06-16 (B안): registry.json 영역 ProgramData 영역 이전. {app} 미초기화 영역 사고 차단.
  if FileExists(ExpandConstant('{commonappdata}\HitPan\registry.json')) then Exit;
  if G_BootstrapOk then Exit;
  if G_TenantCode = 'LOCAL' then Exit;

  // 비정상 영역 종료 영역 — 부분 영역 정리 영역 가도
  // cloudflared 영역 좀비 영역 제거 (사고 #11 정합 영역)
  Exec(ExpandConstant('{cmd}'),
       '/C sc stop cloudflared & timeout /t 2 /nobreak & sc delete cloudflared',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{cmd}'),
       '/C taskkill /F /IM cloudflared.exe',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // schtasks 영역 부분 영역 박힌 영역 제거 (모든 슬롯 영역)
  Exec(ExpandConstant('{cmd}'),
       '/C schtasks /Delete /F /TN "HitPan-ERP-API-tenant-1" & ' +
       'schtasks /Delete /F /TN "HitPan-ERP-API-tenant-2" & ' +
       'schtasks /Delete /F /TN "HitPan-ERP-API-tenant-3" & ' +
       'schtasks /Delete /F /TN "HitPan-ERP-API-tenant-4" & ' +
       'schtasks /Delete /F /TN "HitPan-ERP-API-tenant-5"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // 환경변수 영역 정리 — 부분 영역 박힌 영역 제거
  Exec(ExpandConstant('{cmd}'),
       '/C reg delete "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v HITPAN_SUBDOMAIN /f & ' +
       'reg delete "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v DB_PASSWORD /f & ' +
       'reg delete "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v JWT_SECRET /f',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // db.conf 영역·bootstrap.conf 영역 잔재 영역 — Inno Setup 영역 [UninstallDelete] 영역 박힘
end;
