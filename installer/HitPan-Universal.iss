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

; 버전 (작1 W4-0, 2026-07-16 — 사장님 결재)
;   단일출처 = 레포 루트 Directory.Build.props 의 <HitPanVersion>.
;   CI 는 ISCC /DAppVersion=1.2.34 로 주입하며, 그 값은 Directory.Build.props 와 반드시 같아야 한다
;   (다르면 인스톨러가 굽는 버전 ≠ 어셈블리 버전 → 워치독 업데이트 판정이 어긋난다).
;   아래 폴백은 /DAppVersion 없이 로컬에서 구울 때만 쓰이는 값 — 릴리스 경로가 아니다.
;   ⚠️ 종전 폴백은 "1.2.13" 이었다. 실제 배포본이 1.2.33 이던 시점에도 이 값이 남아 있어
;      "인스톨러는 1.2.13, 본사 메타핑은 1.0.0, /health 는 1.0.0-beta" 로 전부 제각각이었다.
#ifndef AppVersion
  #define AppVersion "1.2.40"
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

; 워치독 — 자가회복 서비스 (헌법 #28·#30)
; ★ 봉합 (2026-07-14, Sandbox 실측 적발 — 사장님 결재): 종전 `Check: WatchdogExists`는 설치 시점(고객 PC)에서
;   빌드PC 상대경로({#BundleDir})를 검사해 항상 False → 워치독이 설치EXE에 담겨 있어도 추출 0건
;   (전 고객 서비스 미등록 침묵 실패 = 헌법 #28·#30·자동업데이트 전체 무력화). 워치독은 빌드 [3/5]가
;   항상 포함(publish 실패 시 출시 차단)하므로 조건 없이 복사한다. 또한 단일 EXE 복사도 오답 —
;   self-contained 222파일 구성이라 의존 파일 전체(recursesubdirs)를 복사해야 기동 가능.
Source: "{#BundleDir}\watchdog\*"; DestDir: "{app}\watchdog"; Flags: ignoreversion recursesubdirs createallsubdirs

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
  // W-2 (작업지시서 20260716작2, 사장님 승인 2026-07-16): 설치 방식 선택 — 처음 설치 / 다시 설치.
  //   무단사용 방지: 신규설치는 같은 PC 에 이미 있는 사업자번호를 실제 차단(경고만 아님).
  //   재설치는 그 사업자번호 슬롯을 재사용(멀티슬롯 무손상 — 다른 사업자번호는 계속 추가 허용).
  InstallModePage: TInputOptionWizardPage;
  SerialKeyPage: TInputQueryWizardPage;
  BootstrapResultPage: TOutputMsgWizardPage;
  // 부모계정 입력 페이지 (작업지시서 20260707작1 ③단계 P0-3, 사장님 결재 2026-07-07):
  //   아이디+비번+비번확인. ssPostInstall 앞(설치 전)에서 입력받아 seed-parent 오프라인 서브커맨드로
  //   로컬 DB 에 부모계정을 심는다. 비번 평문은 임시파일(ACL)로만 전달 — SetupLog·PowerShell 인자 미노출(#40·#22).
  ParentAccountPage: TInputQueryWizardPage;

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
  // 봉합 (재설치 P0 2차 벽, 사장님 결재 2026-07-06, 작지서 20260706작2): 부모계정 서명검증용 키.
  //   백오피스 응답 bootstrap.tokenKey → db.conf HITPAN_BOOTSTRAP_TOKEN_KEY 기록 → ERP 로컬 검증 정합.
  //   ※ 이행기(④단계 전) 유지. ④단계에서 제거.
  G_BootstrapTokenKey: String;
  G_BootstrapOk: Boolean;

  // 부모계정 온보딩 로컬 완결 (작업지시서 20260707작1 ③단계, 사장님 결재 2026-07-07):
  //   G_BizNo       : 사업자번호(10자리). license/claim 서버간 검증 + local_company 저장에 사용.
  //   G_SignedProof : 백오피스 발급 증표(3-part ECDSA 공개키 / 2-part HMAC 이행기). seed-parent 가
  //                   EXE 내장 공개키로 오프라인 검증한다(브라우저·CORS·터널 우회 = 오늘 CORS 진범 원천 차단).
  //     ※ 진범(오늘 Sandbox 실측): 웹 setup(브라우저)이 back.hitpan.kr 를 직접 호출 → CORS 차단 → 증표 못 받음.
  //       봉합: 증표 취득은 설치마법사가 PowerShell 서버간 호출(license/claim)로만 수행 = CORS 무관.
  //   G_SignedProofKid : 증표 공개키 버전(kid). 진단·롤오버 추적용(검증 자체는 증표 3번째 분절로 함).
  G_BizNo: String;
  G_SignedProof: String;
  G_SignedProofKid: String;
  // W-2/3/4 (작업지시서 20260716작2, 사장님 승인 2026-07-16): 재설치/신규설치 무단사용 방지.
  //   G_IsReinstall : 설치 방식 선택(True=다시 설치 / False=처음 설치). 신규/재설치 검증 분기.
  //   G_BizNoHash   : 사업자번호 SHA-256(로컬 자기중복 탐지 편의용, 본사 페퍼 아님 — #22·#23 로컬 전용).
  //                   ⚠️ 증표 대조용 biz_no_hash(본사 HMAC)와 다른 값이다. 혼용 금지(작지서 W-3 페퍼 정합).
  //   G_SlotBlocked : DetermineMultiTenantSlot 이 신규설치 동일 사업자번호를 만나 실제 차단해야 함을 알림.
  //                   procedure 라 반환값이 없어, 이 전역으로 NextButtonClick 이 Result:=False 를 낸다.
  G_IsReinstall: Boolean;
  G_BizNoHash: String;
  G_SlotBlocked: Boolean;
  // ★ 봉합 20260707작2 (W1-6): seed-parent 종료코드를 호출부(CurStepChanged)에서 판정하기 위한 전역.
  //   0=성공 / 10=기존 부모계정 유지(멱등 성공 — 방금 입력한 비번 미적용, 호출부에서 고지) /
  //   6=로그인 스모크 검증 실패(신설) / -1=실행 자체 실패(Exec=False 또는 사전 단계 실패).
  G_SeedParentExitCode: Integer;
  // ★ 봉합 20260707작2 (W2-2): 기동(schtasks) 등록 실패 경고 MsgBox 1회만 표시(중복 팝업 차단).
  G_StartupWarnShown: Boolean;
  // ★ 봉합 20260714작1 (W1-1): 터널 구성 실패 경고 MsgBox 1회만 표시(W2-2 동일 패턴).
  G_TunnelWarnShown: Boolean;

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

// WatchdogExists 제거 (2026-07-14 봉합): 설치 시점에 빌드PC 경로를 검사하는 논리 오류로
//   워치독 추출을 전 고객에서 차단하던 함수 — [Files] 무조건 복사로 대체(위 봉합 주석 참조).

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

// W-3 (작업지시서 20260716작2): Pascal Boolean 을 PowerShell 리터럴($true/$false)로 변환.
function BoolToPsLiteral(B: Boolean): String;
begin
  if B then Result := '$true' else Result := '$false';
end;

// ============================================================
// 멀티사업자 슬롯 결정 (사고 #16·#21·#22 봉합 WS-20260612-01 2026-06-12)
// registry.json 단일출처 — 첫 설치 / 추가 설치 분기
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

  // W-3/W-4 (작업지시서 20260716작2, 검증팀 P0-1 봉합 + 사장님 결재 2026-07-17 "재설치=새 시리얼 인증으로만"):
  //   ■ 재설치 판정 = 로컬 registry 유무가 아니라 백오피스 새 시리얼 인증(license/claim)이 진짜 관문.
  //     사장님 정의상 정당한 재설치 = 포맷·PC교체(백지 PC)이므로, 백지 PC엔 registry 가 없는 게 정상이다.
  //     따라서 재설치인데 로컬에 그 사업자번호가 없어도 차단하지 않고 새 슬롯으로 통과시킨다(옛 시리얼이면
  //     이미 CallLicenseClaimApi 에서 거부됨 — 여기 슬롯 판정은 무단사용 게이트가 아니다).
  //   ■ 신규설치만 같은 PC 동일 사업자번호를 실제 차단(-2). 시리얼 중복(-1)은 종전 유지. 멀티슬롯 무손상.
  PsScript :=
    '$ErrorActionPreference = ''Continue'';' + #13#10 +
    '$registryPath = "' + ExpandConstant('{commonappdata}') + '\HitPan\registry.json";' + #13#10 +
    '$slot = 1;' + #13#10 +
    '$tenantCode = "' + G_TenantCode + '";' + #13#10 +
    '$bizNoHash = "' + G_BizNoHash + '";' + #13#10 +
    '$isReinstall = ' + BoolToPsLiteral(G_IsReinstall) + ';' + #13#10 +
    'function Get-NextFreeSlot($tenants) {' + #13#10 +
    '  $used = @($tenants | ForEach-Object { [int]$_.slotIndex });' + #13#10 +
    '  for ($i = 1; $i -le 5; $i++) { if ($used -notcontains $i) { return $i } }' + #13#10 +
    '  return 1;' + #13#10 +
    '}' + #13#10 +
    'if (Test-Path $registryPath) {' + #13#10 +
    '  try {' + #13#10 +
    '    $reg = Get-Content $registryPath -Raw | ConvertFrom-Json;' + #13#10 +
    '    if ($reg.tenants) {' + #13#10 +
    '      $sameBiz = $reg.tenants | Where-Object { $_.bizNoHash -eq $bizNoHash };' + #13#10 +
    '      $sameSerial = $reg.tenants | Where-Object { $_.tenantCode -eq $tenantCode };' + #13#10 +
    '      if ($isReinstall) {' + #13#10 +
    '        # 재설치: 같은 사업자번호 슬롯이 있으면 재사용, 없으면(백지·레거시) 새 슬롯으로 통과.' + #13#10 +
    '        if ($sameBiz) { $slot = [int]($sameBiz | Select-Object -First 1).slotIndex; }' + #13#10 +
    '        else { $slot = Get-NextFreeSlot $reg.tenants; }' + #13#10 +
    '      } elseif ($sameBiz) {' + #13#10 +
    '        # 신규설치인데 같은 사업자번호가 이미 이 PC 에 있음 → 실제 차단(-2).' + #13#10 +
    '        $slot = -2;' + #13#10 +
    '      } elseif ($sameSerial) {' + #13#10 +
    '        # 신규설치, 사업자번호는 새것이나 같은 시리얼이 이미 있음(종전 -1 유지).' + #13#10 +
    '        $slot = -1;' + #13#10 +
    '      } else {' + #13#10 +
    '        $slot = Get-NextFreeSlot $reg.tenants;' + #13#10 +
    '      }' + #13#10 +
    '    }' + #13#10 +
    '  } catch { $slot = 1; }' + #13#10 +
    '}' + #13#10 +
    '# registry 자체가 없음(백지 PC) = 재설치·신규 모두 슬롯 1 로 정상 시작.' + #13#10 +
    '[System.IO.File]::WriteAllText("' + ResultFile + '", $slot.ToString(), [System.Text.Encoding]::UTF8);';

  SaveStringToFile(PsFile, PsScript, False);
  Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -File "' + PsFile + '"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if FileExists(ResultFile) and LoadStringsFromFile(ResultFile, Lines) and (GetArrayLength(Lines) > 0) then begin
    G_SlotIndex := StrToIntDef(Trim(Lines[0]), 1);
  end;

  DeleteFile(PsFile);
  DeleteFile(ResultFile);

  // W-3 (작업지시서 20260716작2): 신규설치인데 같은 PC 에 동일 사업자번호가 이미 있음 → 실제 차단.
  //   종전엔 시리얼 중복(-1)에도 경고만 하고 G_SlotIndex:=1 로 통과했다(사장님 관찰 "경고만 뜨고 설치됨").
  //   이제 -2(동일 사업자번호)·-1(동일 시리얼)은 G_SlotBlocked 로 NextButtonClick 이 실제 차단한다.
  //   (재설치 대상 없음은 -3 차단이 아니라 새 슬롯 통과로 바뀌었다 — W-4 봉합, 사장님 결재 2026-07-17.)
  if G_SlotIndex = -2 then begin
    MsgBox('이 PC 에 이미 같은 사업자등록번호로 설치된 회사가 있습니다.' + #13#10 + #13#10 +
           '같은 회사를 다시 설치하시려면 [뒤로] 눌러 「다시 설치」를 선택하세요.' + #13#10 +
           '다른 회사(다른 사업자번호)라면 사업자번호를 확인해 주세요.', mbError, MB_OK);
    G_SlotBlocked := True;
    G_SlotIndex := 1;
    G_BootstrapOk := False;
    Exit;
  end;

  // W-4 (검증팀 P0-1 봉합, 사장님 결재 2026-07-17): 재설치는 로컬 registry 유무로 차단하지 않는다.
  //   백지 PC(포맷·PC교체)엔 registry 가 없는 게 정상 — 새 시리얼 인증(CallLicenseClaimApi)이 진짜 관문이다.
  //   따라서 -3(재설치 대상 없음) 차단은 제거했다. PS 판정이 새 슬롯을 반환하므로 여기서 별도 처리 불필요.

  // 슬롯 = -1 = 동일 시리얼 중복 (사업자번호는 다르나 같은 시리얼) — 종전 경고 유지.
  if G_SlotIndex = -1 then begin
    MsgBox('이 시리얼 키로 이미 설치된 회사가 있습니다.' + #13#10 +
           '다른 시리얼로 시도하거나 본사에 문의해주세요.', mbError, MB_OK);
    G_SlotBlocked := True;
    G_SlotIndex := 1;
    G_BootstrapOk := False;
    Exit;
  end;

  // 슬롯 정합 — 포트·DB 결정
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
  // 재설치 P0 2차 벽 봉합 (2026-07-06): 부모계정 서명검증 키 추출 (응답 bootstrap.tokenKey).
  G_BootstrapTokenKey := ExtractJsonValue(RawResponse, 'tokenKey');

  // 정리
  DeleteFile(ResponseFile);
  DeleteFile(RequestFile);

  G_BootstrapOk := True;
  Result := True;
end;

// ============================================================
// 부모계정 증표(SignedProof) 서버간 취득 — license/claim (작업지시서 20260707작1 ③단계)
// ============================================================
//   왜 별도 호출인가: installer/bootstrap 응답의 token 은 무작위 부트스트랩 토큰(bst_...)이라 오프라인
//     서명검증 대상이 아니다. 오프라인으로 검증 가능한 증표(SignedProof: 3-part ECDSA / 2-part HMAC)는
//     오직 POST /api/landing/license/claim 이 발급한다(사업자번호 해시 매칭 후). 그래서 시리얼+사업자번호로
//     이 엔드포인트를 PowerShell 서버간(브라우저 아님)으로 1회 호출해 증표를 받아 온다.
//   ★ 오늘 CORS 진범 봉합: 종전엔 이 호출을 ERP 첫 화면(브라우저)이 back.hitpan.kr 로 직접 쏴서 CORS 차단.
//     여기(설치마법사 PowerShell)는 브라우저가 아니므로 CORS 자체가 무관 = 전 고객 구조적 차단 원천 제거.
//   반환: True 면 G_SignedProof·G_SignedProofKid 세팅. False 면 증표 미취득(설치는 계속하되 부모계정은 못 심음).
function CallLicenseClaimApi(Serial: String; BizNo: String): Boolean;
var
  PsScript, PsScriptFile: String;
  ResultCode: Integer;
  ResponseFile, RequestFile: String;
  Lines: TArrayOfString;
  RawResponse: String;
  I: Integer;
begin
  Result := False;
  G_SignedProof := '';
  G_SignedProofKid := '';

  ResponseFile := ExpandConstant('{tmp}\claim-response.json');
  RequestFile := ExpandConstant('{tmp}\claim-request.json');
  PsScriptFile := ExpandConstant('{tmp}\claim-call.ps1');

  // 요청 본문 — LicenseKey + BizNo (LandingPublicController.ClaimLicense 계약).
  SaveStringToFile(RequestFile,
    '{"licenseKey":"' + Serial + '","bizNo":"' + BizNo + '"}', False);

  // CallBootstrapApi 와 동일한 5축 봉합(외부 .ps1·TLS1.2·UTF-8·UseBasicParsing·catch 기록).
  PsScript :=
    '$ErrorActionPreference = ''Stop'';' + #13#10 +
    '$ProgressPreference = ''SilentlyContinue'';' + #13#10 +
    'try {' + #13#10 +
    '  [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13;' + #13#10 +
    '} catch { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; }' + #13#10 +
    'try {' + #13#10 +
    '  $body = Get-Content -Raw -Path "' + RequestFile + '";' + #13#10 +
    '  $r = Invoke-RestMethod -Uri "{#BackofficeApi}/api/landing/license/claim" -Method POST -Body $body -ContentType "application/json" -TimeoutSec 30 -UseBasicParsing;' + #13#10 +
    '  $json = $r | ConvertTo-Json -Depth 10 -Compress;' + #13#10 +
    '  [System.IO.File]::WriteAllText("' + ResponseFile + '", $json, [System.Text.Encoding]::UTF8);' + #13#10 +
    '  exit 0;' + #13#10 +
    '} catch {' + #13#10 +
    '  $msg = $_.Exception.Message;' + #13#10 +
    '  if ($_.ErrorDetails -and $_.ErrorDetails.Message) { $msg = $_.ErrorDetails.Message; }' + #13#10 +
    '  try {' + #13#10 +
    '    [System.IO.File]::WriteAllText("' + ResponseFile + '", ''{"valid":false,"message":"'' + ($msg -replace ''"'', ''\"'') + ''"}'', [System.Text.Encoding]::UTF8);' + #13#10 +
    '  } catch { }' + #13#10 +
    '  exit 1;' + #13#10 +
    '}';

  SaveStringToFile(PsScriptFile, PsScript, False);
  Exec('powershell.exe',
       '-NoProfile -ExecutionPolicy Bypass -File "' + PsScriptFile + '"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  DeleteFile(PsScriptFile);

  if not FileExists(ResponseFile) then Exit;
  if not LoadStringsFromFile(ResponseFile, Lines) then Exit;
  RawResponse := '';
  for I := 0 to GetArrayLength(Lines) - 1 do RawResponse := RawResponse + Lines[I];

  // 백오피스 API 는 camelCase 직렬화 → "valid"/"signedProof"/"signedProofKid".
  if Pos('"valid":true', RawResponse) = 0 then begin
    // 증표 미발급(사업자번호 불일치·비활성 등). 설치 자체는 폴백으로 계속(부모계정은 못 심음).
    DeleteFile(ResponseFile);
    DeleteFile(RequestFile);
    Exit;
  end;

  G_SignedProof := ExtractJsonValue(RawResponse, 'signedProof');
  G_SignedProofKid := ExtractJsonValue(RawResponse, 'signedProofKid');

  DeleteFile(ResponseFile);
  DeleteFile(RequestFile);

  // 증표가 실제로 있어야 성공. 개인키 미구성(백오피스) 등으로 signedProof=null 이면 빈 문자열 → False.
  Result := (G_SignedProof <> '');
end;

// ============================================================
// 마법사 페이지 박기
// ============================================================
procedure InitializeWizard;
begin
  // W-2 (작업지시서 20260716작2): 설치 방식 선택 페이지 — 시리얼 입력보다 앞.
  //   두 방식 모두 같은 시리얼+사업자번호 입력을 쓰고, 검증만 분기한다(마법사 단순 유지 = 히트판 정신).
  InstallModePage := CreateInputOptionPage(wpWelcome,
    '설치 방식 선택',
    '이 PC 에 히트판 ERP 를 어떻게 설치할지 선택하세요',
    '처음 설치와 다시 설치는 시리얼 확인 방식이 다릅니다.' + #13#10 +
    '한 PC 에 여러 회사를 설치하실 수 있습니다(회사마다 사업자번호가 달라야 합니다).',
    True, False);
  InstallModePage.Add('처음 설치 — 이 PC 에 새 회사를 설치합니다');
  InstallModePage.Add('다시 설치 — 이미 쓰던 회사를 재설치합니다 (본사에서 새로 발급받은 시리얼 필요)');
  InstallModePage.SelectedValueIndex := 0;

  // 시리얼 입력 페이지 (부모 = 설치 방식 선택 페이지)
  SerialKeyPage := CreateInputQueryPage(InstallModePage.ID,
    '시리얼 키 입력',
    '이메일로 받으신 시리얼 키를 입력해주세요',
    '본사에서 발급된 시리얼 키는 다음 형식입니다:' + #13#10 +
    '    HITP-XXXX-XXXX-XXXX-XXXX' + #13#10 + #13#10 +
    '시리얼 키는 이메일로 발송되었습니다. 분실 시 본사 고객센터에 문의해주세요.');

  SerialKeyPage.Add('시리얼 키:', False);
  SerialKeyPage.Values[0] := 'HITP-';

  // 부모계정 온보딩 로컬 완결 (작업지시서 20260707작1 ③단계, 사장님 결재 2026-07-07):
  //   사업자번호 입력칸 신설. 증표 취득(license/claim 서버간 검증)과 local_company 저장에 필요.
  //   종전엔 ERP 첫 화면(/setup/license)에서 브라우저가 back.hitpan.kr 를 직접 호출해 CORS 차단(오늘 진범).
  //   봉합: 설치마법사가 여기서 사업자번호를 받아 PowerShell 서버간으로 검증 → 증표를 로컬에서 오프라인 검증.
  SerialKeyPage.Add('사업자등록번호 (숫자 10자리):', False);
  SerialKeyPage.Values[1] := '';

  // 부트스트랩 결과 확인 페이지
  BootstrapResultPage := CreateOutputMsgPage(SerialKeyPage.ID,
    '회사 정보 확인',
    '시리얼 키로 회사 정보를 확인했습니다',
    '아래 정보가 맞는지 확인해주세요. 틀리면 설치를 취소하고 본사에 문의해주세요.');

  // 부모계정 입력 페이지 (아이디 + 비번 + 비번확인) — 설치(ssPostInstall) 앞.
  //   헌법 #40: 아이디 방식(이메일 형식 강제 금지) + 비번은 로컬 BCrypt 만(본사 0).
  //   Add(prompt, Password) 의 두번째 인자 True = 비번 마스킹(화면·SetupLog 노출 차단).
  ParentAccountPage := CreateInputQueryPage(BootstrapResultPage.ID,
    '부모 계정 만들기',
    'ERP 최상위(부모) 계정을 이 PC 에 만듭니다',
    '부모 계정은 ERP 마스터 계정입니다. 이 계정으로만 자식 계정(직원)을 만들 수 있습니다.' + #13#10 +
    '아이디와 비밀번호는 이 PC 에만 저장되며, 본사로 전송되지 않습니다.');
  ParentAccountPage.Add('대표 이름:', False);
  ParentAccountPage.Add('로그인 아이디 (4자 이상, 공백 없이):', False);
  ParentAccountPage.Add('비밀번호 (8자 이상):', True);
  ParentAccountPage.Add('비밀번호 확인:', True);
end;

// ============================================================
// 페이지 검증
// ============================================================
function NextButtonClick(CurPageID: Integer): Boolean;
var
  TrimmedKey, TrimmedBiz, BizDigits: String;
  SkipResponse, I: Integer;
  ParentName, ParentId, ParentPw, ParentPwc: String;
begin
  Result := True;

  // W-2 (작업지시서 20260716작2): 설치 방식 선택 확정. 이후 시리얼 페이지 검증이 이 값으로 분기한다.
  if CurPageID = InstallModePage.ID then
  begin
    G_IsReinstall := (InstallModePage.SelectedValueIndex = 1);
    Exit;
  end;

  if CurPageID = SerialKeyPage.ID then
  begin
    TrimmedKey := Trim(SerialKeyPage.Values[0]);
    G_LicenseKey := TrimmedKey;
    // 사업자번호 — 하이픈·공백 제거 후 숫자 10자리만 남긴다(license/claim 계약: 정규화 10자리).
    TrimmedBiz := Trim(SerialKeyPage.Values[1]);
    BizDigits := '';
    for I := 1 to Length(TrimmedBiz) do
      if (TrimmedBiz[I] >= '0') and (TrimmedBiz[I] <= '9') then
        BizDigits := BizDigits + TrimmedBiz[I];
    G_BizNo := BizDigits;

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

    // 사업자번호 10자리 검증 — 증표(license/claim) 취득·부모계정 로컬 완결에 필수(#40).
    if Length(G_BizNo) <> 10 then begin
      MsgBox('사업자등록번호를 숫자 10자리로 입력해주세요.' + #13#10 +
             '예: 123-45-67890 (하이픈은 자동으로 제거됩니다).' + #13#10 + #13#10 +
             '가입 시 입력한 사업자번호와 동일해야 합니다.', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    // W-3 (작업지시서 20260716작2): 로컬 자기중복 탐지용 사업자번호 해시.
    //   본사 페퍼 없는 SHA-256 — 이 값은 같은 PC 안에서 "이 사업자번호가 이미 설치됐나"만 판정한다.
    //   본사 대조용이 아니므로 페퍼가 필요 없고, 페퍼를 EXE 에 넣으면 오히려 #22·#23 위반이다.
    //   증표 대조용 biz_no_hash(본사 HMAC)와는 다른 값 — 혼용 금지. GetSHA256OfUnicodeString 은 Inno 내장.
    G_BizNoHash := GetSHA256OfUnicodeString(G_BizNo);

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

    // 부모계정 증표(SignedProof) 서버간 취득 — license/claim (작업지시서 20260707작1 ③단계).
    //   installer/bootstrap 는 회사정보·도메인·터널만 주고, 오프라인 검증 가능한 증표는 안 준다.
    //   여기서 시리얼+사업자번호로 증표를 받아 두면, ssPostInstall 에서 seed-parent 가 그 증표를
    //   EXE 내장 공개키로 오프라인 검증해 부모계정을 로컬에 심는다(브라우저·CORS·터널 우회).
    if not CallLicenseClaimApi(TrimmedKey, G_BizNo) then begin
      WizardForm.NextButton.Enabled := True;
      WizardForm.NextButton.Caption := '다음';
      // 증표 미취득 — 사업자번호 불일치가 가장 흔한 원인. ERP 본체·DB 설치 자체는 계속 진행 가능하나,
      //   부모계정 없이는 로그인이 불가능해 재설치가 필요하다 (폴백 기능 미구현 — 로그인 불가 상태로
      //   설치됨, 20260707작2 정정). 종전 "로그인 화면 최초등록(P0-4)" 안내는 실존하지 않는 기능을
      //   약속한 거짓 안내였음 → 문구 정정 + 기본 버튼을 「아니오」(재입력 권장)로 (MB_DEFBUTTON2).
      SkipResponse := MsgBox(
        '부모계정 인증 증표를 받지 못했습니다.' + #13#10 + #13#10 +
        '「예」를 누르면 부모계정 없이 설치되며, 이 상태에서는 로그인이 불가능해 재설치가 필요합니다.' + #13#10 + #13#10 +
        '「아니오」를 눌러 시리얼 번호와 사업자등록번호를 다시 확인해 주세요. (권장)',
        mbConfirmation, MB_YESNO or MB_DEFBUTTON2);
      if SkipResponse = IDYES then begin
        G_SignedProof := '';
        // 회사정보(installer/bootstrap)는 이미 확인됨 → 설치 자체는 정상 진행.
      end else begin
        Result := False;
        Exit;
      end;
    end;

    WizardForm.NextButton.Enabled := True;
    WizardForm.NextButton.Caption := '다음';

    // 멀티사업자 슬롯 결정 (사고 #16·#21·#22 봉합 WS-20260612-01)
    //   registry.json 단일출처 — 첫 설치 = 1번, 추가 = 다음 빈 슬롯
    //   포트: 5257 + 100*(N-1) → 슬롯 1=5257, 슬롯 2=5357, 슬롯 3=5457, ...
    //   DB: hitpan_erp_{tenantCode}
    //   디렉터리: {app}\tenant-N
    // W-3 (작업지시서 20260716작2): 신규설치 동일 사업자번호는 실제 차단.
    //   procedure 라 반환값이 없어 G_SlotBlocked 전역으로 판정을 받는다. 재시도 위해 매번 초기화.
    // 검증팀 P1 봉합: [뒤로] 눌러 사업자번호를 바꿔 다시 넘어오면 슬롯·중복판정을 다시 해야 한다.
    //   G_SlotAlreadyDetermined(사고#45 슬롯 이중점유 방지)가 재판정을 막아, 종전 슬롯값으로 통과하며
    //   바뀐 사업자번호의 중복 차단(-2)이 스킵되는 구멍이 있었다. SerialKeyPage 를 다시 넘어올 때마다
    //   리셋해 재판정을 강제한다(슬롯 확정은 여전히 이 판정 1회로 끝나므로 이중점유는 안 생긴다).
    G_SlotAlreadyDetermined := False;
    G_SlotBlocked := False;
    DetermineMultiTenantSlot();
    if G_SlotBlocked then begin
      // 차단 사유 MsgBox 는 DetermineMultiTenantSlot 내부에서 이미 표시함. 여기서는 진행만 막는다.
      Result := False;
      Exit;
    end;

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
  end
  else if CurPageID = ParentAccountPage.ID then
  begin
    // 부모계정 입력 검증 (작업지시서 20260707작1 ③단계, 헌법 #40 아이디 방식).
    ParentName := Trim(ParentAccountPage.Values[0]);
    ParentId   := Trim(ParentAccountPage.Values[1]);
    ParentPw   := ParentAccountPage.Values[2];   // 비번은 Trim 안 함(의도된 공백 보존)
    ParentPwc  := ParentAccountPage.Values[3];

    if ParentName = '' then begin
      MsgBox('대표 이름을 입력해주세요.', mbError, MB_OK);
      Result := False; Exit;
    end;
    // 아이디: 형식(이메일 등) 강제 금지 — 4자 이상 + 공백 없음만(헌법 #40, Login.razor·create-parent 정합).
    if Length(ParentId) < 4 then begin
      MsgBox('로그인 아이디는 4자 이상이어야 합니다.', mbError, MB_OK);
      Result := False; Exit;
    end;
    for I := 1 to Length(ParentId) do
      if (ParentId[I] = ' ') or (ParentId[I] = #9) then begin
        MsgBox('로그인 아이디에는 공백을 사용할 수 없습니다.', mbError, MB_OK);
        Result := False; Exit;
      end;
    if Length(ParentPw) < 8 then begin
      MsgBox('비밀번호는 8자 이상이어야 합니다.', mbError, MB_OK);
      Result := False; Exit;
    end;
    if ParentPw <> ParentPwc then begin
      MsgBox('비밀번호가 일치하지 않습니다.', mbError, MB_OK);
      Result := False; Exit;
    end;
    // 값은 페이지 컨트롤에 그대로 남아있고(ParentAccountPage.Values[*]), ssPostInstall 에서 임시 JSON 으로만 전달.
    //   전역변수에 비번을 복사해 두지 않는다(메모리·로그 노출면 최소화). CurStepChanged 에서 페이지 값을 직접 읽는다.
  end;
end;

// 부모계정 입력 페이지는 인증(G_SignedProof) 취득에 성공했을 때만 노출한다.
//   증표 미취득(LOCAL·사업자번호 불일치 폴백)이면 부모계정 자동 생성 경로가 없으므로 페이지를 건너뛴다
//   (폴백 기능 미구현 — 로그인 불가 상태로 설치됨, 20260707작2 정정). 시리얼 미입력 LOCAL 모드도 동일.
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if PageID = ParentAccountPage.ID then
    Result := (G_SignedProof = '');
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
// 단일 appsettings.json 의 ApiBaseUrl **값만** 정정한다(다른 키·주석·서식 전부 보존).
//
// ★★ 봉합 20260804 P0-E (검증팀 데이비드 박 [4] 최종검증 적발) — 인코딩 ★★
//   ■ 무엇이 문제였나
//     직전 구현은 Pascal 로 읽고(LoadStringsFromFile) Pascal 로 썼다(SaveStringToFile(..,False)).
//     그런데 SaveStringToFile 의 3번째 인자 False = **ANSI(CP949)** 다. 대상 파일은
//     한글·이모지 주석을 담은 **UTF-8** 이다. 실측(CP949 라운드트립):
//       · '🔴'(U+1F534) → '??'  복원 불가
//       · CP949 산출물을 엄격 UTF-8 로 디코드 → 예외 / 브라우저(관대) → U+FFFD 599개
//     구조 문자({ " :)가 ASCII 라 JSON 파싱과 ApiBaseUrl='' 자체는 살아남아 **로그인은 된다.**
//     즉 격리는 안 무너진다. 그러나 깨지는 대상이 하필 **재발방지 주석**이다 —
//     이 함수를 전면덮어쓰기에서 값치환으로 바꾼 **명시적 목적이 주석 보존**이었는데
//     인코딩이 그 목적을 스스로 무효화했다. 다음 사고 조사자가 고객 PC 를 열면 경고를 못 읽는다.
//
//   ■ 이 파일이 같은 진범에 이미 한 번 당했다 (1226-1229행 참조)
//     20260707작2 W1-7 — "SaveStringToFile(..,False)=ANSI 로 쓰는데 상대는 UTF-8 로 읽는다"
//     → 한글이 U+FFFD 로 깨져 로그인 영구 불가. 그때는 JsonEscape 로 봉합했다.
//     직전 구현은 **그 교훈을 같은 파일 안에서 재적용하지 않았다.**
//
//   ■ 왜 LoadStringsFromFile 도 못 믿나 (직전 커밋의 '선례 4건' 주장 정정 — 헌법 #32)
//     선례 395·519·531·627 은 전부 **PowerShell 이 UTF-8 로 쓴 ASCII 기계 출력**
//     (슬롯번호·API 응답)을 되읽는 자리다. **한글 UTF-8 원본을 읽는 선례는 0건.**
//     ⇒ 읽기 쪽 UTF-8 정합도 미검증이었다. 쓰기만 고치면 반쪽이다.
//
//   ■ 채택: 읽기·수정·쓰기를 통째로 PowerShell 에 위임한다
//     · UTF8Encoding($false) 로 **읽고 쓴다** — BOM 없이, 왕복 무손실. 양쪽 다 UTF-8 로 통일.
//     · 이 파일의 지배적 관용구다(WriteAllText 선례 389·497·503·505·609·615 = 6건).
//       Pascal 문자열 수술(선례 0건)보다 검증된 경로다.
//     · 정규식으로 "ApiBaseUrl" **값만** 치환 —
//       (?<!\w) 로 '_comment_ApiBaseUrl'·'BackofficeApiBaseUrl' 오매칭을 원천 차단한다.
//     · 실패해도 원본을 건드리지 않는다(치환 0건이면 그대로 종료) — 파괴 금지.
//   ⚠️ 정적 분석만으로 확정할 수 없는 영역이라 **Sandbox 실측으로 산출 파일을 눈으로 확인**한다
//     (검증팀 단서 조건). '코드 고쳤다'는 봉합이 아니다.
procedure FixupOneAppSettings(AppSettingsPath: String; TargetUrl: String);
var
  PsFile, ResultFile, PsScript: String;
  Lines: TArrayOfString;
  ResultCode: Integer;
  Outcome: String;
begin
  if not FileExists(AppSettingsPath) then begin
    Log('[FixupBlazor] 0건(파일없음): ' + AppSettingsPath);
    Exit;
  end;

  PsFile     := ExpandConstant('{tmp}\hitpan-fixup-appsettings.ps1');
  ResultFile := ExpandConstant('{tmp}\hitpan-fixup-appsettings.out');
  DeleteFile(ResultFile);

  // TargetUrl 은 도메인 또는 빈 문자열이라 따옴표·역슬래시가 들어올 여지가 없다.
  //   그래도 정규식 치환문에서 '$' 는 특수문자이므로 리터럴로 넘기기 위해 -replace 대신
  //   [Regex]::Replace 의 MatchEvaluator 를 쓰지 않고, 값에 $ 가 없음을 전제로 단순 치환한다.
  PsScript :=
    '$ErrorActionPreference = ''Stop'';' + #13#10 +
    '$enc = New-Object System.Text.UTF8Encoding($false);' + #13#10 +
    'try {' + #13#10 +
    '  $p = "' + AppSettingsPath + '";' + #13#10 +
    '  $raw = [System.IO.File]::ReadAllText($p, $enc);' + #13#10 +
    // (?<!\w) = 앞 문자가 단어문자가 아님 → _comment_ApiBaseUrl / BackofficeApiBaseUrl 배제
    '  $re = ''(?<!\w)("ApiBaseUrl"\s*:\s*")[^"]*(")'';' + #13#10 +
    '  $new = [System.Text.RegularExpressions.Regex]::Replace($raw, $re, ''${1}' + TargetUrl + '${2}'');' + #13#10 +
    '  if ($new -eq $raw) {' + #13#10 +
    // 이미 정답이거나 키가 없다. 어느 쪽이든 원본을 건드리지 않는다.
    '    [System.IO.File]::WriteAllText("' + ResultFile + '", "NOCHANGE", $enc);' + #13#10 +
    '    exit 0;' + #13#10 +
    '  }' + #13#10 +
    '  [System.IO.File]::WriteAllText($p, $new, $enc);' + #13#10 +
    '  [System.IO.File]::WriteAllText("' + ResultFile + '", "OK", $enc);' + #13#10 +
    '  exit 0;' + #13#10 +
    '} catch {' + #13#10 +
    '  try { [System.IO.File]::WriteAllText("' + ResultFile + '", "ERR:" + $_.Exception.Message, $enc); } catch { }' + #13#10 +
    '  exit 1;' + #13#10 +
    '}';

  SaveStringToFile(PsFile, PsScript, False);
  Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -File "' + PsFile + '"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  Outcome := '';
  if FileExists(ResultFile) and LoadStringsFromFile(ResultFile, Lines) and (GetArrayLength(Lines) > 0) then
    Outcome := Trim(Lines[0]);

  DeleteFile(PsFile);
  DeleteFile(ResultFile);

  if (ResultCode <> 0) or (Copy(Outcome, 1, 4) = 'ERR:') then begin
    // ★ 봉합 20260707작2 (W2-1) 유지: 실패를 Log 만 남기고 통과하면 잘못된 주소가 잔존해
    //   로그인이 남의 서버로 샌다 — 실패를 설치 실패로 승격한다(헌법 #15·#19).
    Log('[FixupBlazor] 정정 실패(exit=' + IntToStr(ResultCode) + ', ' + Outcome + '): ' + AppSettingsPath);
    RaiseException('설정 파일(appsettings.json) 갱신에 실패했습니다. 설치를 다시 실행해 주세요: ' + AppSettingsPath);
  end;

  if Outcome = 'NOCHANGE' then
    // 레포 원본이 이미 빈 값이면 정상적으로 여기 걸린다(= 정정할 게 없음).
    Log('[FixupBlazor] 변경 없음(이미 정답이거나 키 없음): ' + AppSettingsPath)
  else
    Log('[FixupBlazor] 정정 완료 → ' + AppSettingsPath + ' = [' + TargetUrl + ']');
end;

procedure FixupBlazorAppSettings();
var
  TargetUrl: String;
begin
  // 고객사 도메인 정정 (G_PrimaryDomain = "test000.hitpan.kr" 등)
  // ★ P0 봉합 (작7, 사장님 지시 2026-07-01): LOCAL(시리얼 없이) 모드는 종전 Exit 로 정정을 건너뛰어
  //   appsettings.json 이 레포 원본 'api-demo.hitpan.kr' 그대로 남았다 → 로그인이 외부 demo 서버로 가
  //   401(설치된 로컬 히트판이 아니라 데모를 봄). 진범(샌드박스 실측: 콘솔 api-demo.hitpan.kr/api/auth/login 401).
  //   봉합: LOCAL 모드는 ApiBaseUrl 을 '빈 값'으로 정정한다. 그러면 Program.cs 의 폴백
  //   (string.IsNullOrWhiteSpace → HostEnvironment.BaseAddress = 현재 페이지 출처 localhost:5234)이 발동해
  //   로그인이 '자기 자신'(설치된 로컬 히트판)으로 간다. web-server.ps1 이 /api/* 를 로컬 API 포트로 프록시.
  //   파일은 유지·ApiBaseUrl 값만 비움 → 헌법 #21(appsettings 삭제·구조 파괴 금지) 정합.
  if (G_PrimaryDomain = '') or (Pos('localhost', G_PrimaryDomain) > 0) then begin
    Log('[FixupBlazor] LOCAL 모드 → ApiBaseUrl 빈 값 정정(현재 출처 폴백 발동)');
    FixupOneAppSettings(ExpandConstant('{app}') + '\web\wwwroot\appsettings.json', '');
    FixupOneAppSettings(ExpandConstant('{app}') + '\web\wwwroot\appsettings.Local.json', '');
    FixupOneAppSettings(ExpandConstant('{app}') + '\web\wwwroot\appsettings.Development.json', '');
    FixupOneAppSettings(ExpandConstant('{app}') + '\api\wwwroot\appsettings.json', '');
    Exit;
  end;

  // ★★★ 봉합 20260803작2 P0-1 (병렬검증 적발 · 사장님 지시 "고쳐") ★★★
  //
  // ■ 종전: TargetUrl := 'https://' + G_PrimaryDomain 을 써넣었다.
  //   그러면 격리가 **G_PrimaryDomain 이 정확하다는 가정 하나**에만 걸린다.
  //   그 값이 오염되면(7/01·7/07 계통) 고객 화면이 남의 서버를 본다 = 테넌트 격리 붕괴.
  //   2026-08-03 사고가 정확히 그 계통이었다(레포 원본 api-demo 가 살아남).
  //
  // ■ 이제: **빈 값**으로 통일한다. LOCAL 분기와 같다.
  //   빈 값이면 Program.cs:35 가 HostEnvironment.BaseAddress(현재 페이지 출처)로 폴백한다.
  //   실측으로 확인한 두 접속 경로 모두 이 폴백이 자기 회사 API 로 간다:
  //     · 터널 — ingress origin = http://localhost:5257(API). API 는 자기 wwwroot 로
  //       Blazor 를 서빙하면서 /api/* 도 같은 프로세스가 처리한다(Program.cs:59-65).
  //       ⇒ 페이지 출처 = API 출처. 같은 출처라 CORS 자체가 없다.
  //     · localhost:5234 — web-server.ps1:89 이 /api/* 를 슬롯 API 포트로 프록시한다.
  //
  // ■ 왜 이게 더 안전한가 — 격리가 '값이 맞는가'에서 '구조'로 바뀐다
  //   도메인을 써넣으면 그 값이 틀릴 가능성이 영원히 남는다.
  //   빈 값이면 **틀릴 값 자체가 없다.** 브라우저가 자기가 열린 곳으로만 요청한다.
  //   ⇒ 남의 회사 서버를 보는 것이 구조적으로 불가능해진다.
  //
  // ■ 부수 봉합: api\wwwroot\appsettings.Local.json 이 이 분기에서 누락돼 있었다(4개만 정정).
  //   web 쪽은 3개를 정정하면서 api 쪽은 1개만 봤다 — 비대칭. 5개로 맞춘다.
  TargetUrl := '';
  Log('[FixupBlazor] 정식 설치 → ApiBaseUrl 빈 값 정정(자기 출처 폴백). ' +
      '도메인(' + G_PrimaryDomain + ')을 써넣지 않는다 — 20260803작2 P0-1 격리 봉합');

  // ★ 진범 봉합 2026-06-18: Blazor WASM 은 web\wwwroot 에서 서빙됨(web-server.ps1:3).
  //   기존엔 api\wwwroot 만 고쳐서 로그인이 읽는 web\wwwroot 는 api-demo 데모주소 그대로 →
  //   터널 연결돼도 로그인이 데모서버로 가서 실패. web·api 양쪽 + 변형 파일 전부 정정.
  FixupOneAppSettings(ExpandConstant('{app}') + '\web\wwwroot\appsettings.json', TargetUrl);
  FixupOneAppSettings(ExpandConstant('{app}') + '\web\wwwroot\appsettings.Local.json', TargetUrl);
  FixupOneAppSettings(ExpandConstant('{app}') + '\web\wwwroot\appsettings.Development.json', TargetUrl);
  FixupOneAppSettings(ExpandConstant('{app}') + '\api\wwwroot\appsettings.json', TargetUrl);
  FixupOneAppSettings(ExpandConstant('{app}') + '\api\wwwroot\appsettings.Local.json', TargetUrl);
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

// ============================================================
// 부모계정 오프라인 생성 — seed-parent 서브커맨드 배선 (작업지시서 20260707작1 ③단계 P0-3)
// ============================================================
// JSON 문자열 이스케이프 — 사용자 입력(이름·아이디·비번)에 " \ 제어문자가 있어도 JSON 파손·주입 방지.
//   ★ 봉합 20260707작2 (W1-7, 인코딩 분열 원천 차단): 비ASCII 문자(Ord > 126)도 전부 \uXXXX 로 이스케이프.
//   진범: 이 산출 JSON 을 SaveStringToFile(..., False) = ANSI(CP949) 로 쓰는데, seed-parent 쪽은
//   File.ReadAllTextAsync = UTF-8 로 읽는다 → 한글 아이디·비번·대표이름이 U+FFFD 로 깨진 채 저장
//   (한글 아이디면 로그인 영구 불가). 비ASCII 를 \u 이스케이프하면 산출 JSON 이 순수 ASCII 가 되어
//   파일 인코딩(CP949/UTF-8) 비대칭과 무관해진다. Unicode Inno 의 Ord() = UTF-16 코드유닛이므로
//   서로게이트 쌍도 \u 2개로 나가며 이는 유효 JSON — System.Text.Json 이 원문으로 정상 복원한다.
function JsonEscape(S: String): String;
var
  I: Integer;
  C: Char;
  R: String;
begin
  R := '';
  for I := 1 to Length(S) do begin
    C := S[I];
    if C = '"' then R := R + '\"'
    else if C = '\' then R := R + '\\'
    else if (Ord(C) < 32) or (Ord(C) > 126) then R := R + Format('\u%.4x', [Ord(C)])
    else R := R + C;
  end;
  Result := R;
end;

// seed-parent 실행. 증표(G_SignedProof)가 있을 때만 호출된다(없으면 부모계정 없이 설치 —
//   폴백 기능 미구현, 로그인 불가 상태로 설치됨, 20260707작2 정정).
//   비번 평문은 임시 JSON 파일(ACL 잠금)로만 전달 — SetupLog·Exec 인자에 노출하지 않는다(#40·#22).
//   반환 True = 부모계정 생성 성공(exit 0) 또는 기존 부모계정 유지 멱등 성공(exit 10, 20260707작2 W1-6).
//   False = 실패(설치 실패로 처리). 종료코드 상세 판정은 전역 G_SeedParentExitCode 로 호출부에서 수행.
function RunSeedParent(ParentName, ParentId, ParentPw: String): Boolean;
var
  InputFile, ExePath, Json: String;
  ResultCode: Integer;
  Launched: Boolean;
begin
  Result := False;
  // ★ 봉합 20260707작2 (W1-6): 종료코드 전역 초기화 — 사전 단계 실패·Exec 실패 시 -1 유지.
  G_SeedParentExitCode := -1;

  ExePath := ExpandConstant('{app}\api\HitPan.API.exe');
  if not FileExists(ExePath) then begin
    Log('[SeedParent] HitPan.API.exe 없음: ' + ExePath);
    Exit;
  end;

  InputFile := ExpandConstant('{tmp}\seed-parent-input.json');

  // 입력 JSON — seed-parent 계약(SeedParentInput): signedProof·loginId·password·name·bizNo(+회사정보 선택).
  //   비번은 여기 파일에만 존재하고, Exec 인자에는 '경로'만 넘어간다(평문 인자 노출 0).
  Json :=
    '{' +
    '"signedProof":"' + JsonEscape(G_SignedProof) + '",' +
    '"loginId":"' + JsonEscape(ParentId) + '",' +
    '"password":"' + JsonEscape(ParentPw) + '",' +
    '"name":"' + JsonEscape(ParentName) + '",' +
    '"bizNo":"' + JsonEscape(G_BizNo) + '"' +
    '}';

  if not SaveStringToFile(InputFile, Json, False) then begin
    Log('[SeedParent] 입력 파일 작성 실패: ' + InputFile);
    Exit;
  end;

  // ACL 잠금 — Administrators·SYSTEM 만 읽기(다른 프로세스·일반 사용자 접근 차단).
  Exec(ExpandConstant('{cmd}'),
       '/C icacls "' + InputFile + '" /inheritance:r /grant:r "Administrators:F" /grant:r "SYSTEM:F"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // 오프라인 서브커맨드 실행 — 웹 호스트·터널·API 포트 불필요(DI 만 빌드 후 종료). db.conf 는 이미 기록됨.
  //   서브커맨드가 입력 파일을 읽는 즉시 스스로 소각한다(SeedParentCommand.TryDeleteFile). 아래에서 2차 소각.
  ResultCode := -1;
  Launched := Exec(ExePath, 'seed-parent "' + InputFile + '"',
       ExpandConstant('{app}\api'), SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if Launched then begin
    // ★ 봉합 20260707작2 (W1-6): 종료코드를 전역에 보존 — 호출부(CurStepChanged)가 10(멱등 고지)·
    //   6(로그인 스모크 실패) 을 구분 판정한다. Exec=False 면 ResultCode 가 OS 오류코드라 보존하지 않는다(-1 유지).
    G_SeedParentExitCode := ResultCode;
    Log('[SeedParent] exit code = ' + IntToStr(ResultCode));
  end else
    Log('[SeedParent] 프로세스 실행 자체 실패(Exec=False).');

  // 2차 소각 — 서브커맨드가 못 지웠을 경우 대비(비번 평문 잔존 차단). 공백 덮어쓰기 후 삭제.
  if FileExists(InputFile) then begin
    SaveStringToFile(InputFile, StringOfChar(' ', 512), False);
    DeleteFile(InputFile);
  end;

  // 종료 코드 판정 — 실행 성공 + exit 0(성공) 또는 exit 10(기존 부모계정 유지 멱등 성공)만 성공.
  //   ★ 봉합 20260707작2 (W1-6): ParentExists 가 exit 0 → exit 10 으로 분리됨(C# SeedParentCommand 정합).
  //     10 = 설치는 계속하되 "방금 입력한 비번 미적용" 을 호출부에서 고지(재설치 시 새 비번이 조용히
  //     버려지는 함정 제거). 6 = 로그인 스모크 검증 실패(신설). 그 외(launch 실패·2/3/4/5/6)는 실패.
  Result := Launched and ((ResultCode = 0) or (ResultCode = 10));
end;

// ★ 봉합 20260707작2 (W2-2, 기동 실패 가시화): schtasks /Create·/Run 실패를 Log 만 남기고 지나가면
//   "설치 완료 화면인데 서비스가 안 떠 있는" 침묵 고장이 된다. 실패 시 경고 MsgBox 로 가시화하되
//   설치는 중단하지 않는다(ONSTART 작업이라 PC 재부팅 시 자가복구 가능 = 비차단, 헌법 #15).
//   여러 지점에서 연속 실패해도 팝업은 1회만(G_StartupWarnShown) — Log 는 지점별 전부 남긴다.
procedure WarnStartupRegistrationFailure(Context: String; Code: Integer);
begin
  Log('[20260707작2 W2-2] ' + Context + ' 실패 ResultCode=' + IntToStr(Code));
  if not G_StartupWarnShown then begin
    G_StartupWarnShown := True;
    MsgBox('HitPan 서비스 자동 시작 등록에 실패했습니다.' + #13#10 +
           'PC를 재부팅하면 자동으로 시작됩니다. 즉시 사용하려면 고객센터에 문의해 주세요.',
           mbError, MB_OK);
  end;
end;

// ★ 봉합 20260714작1 (W1-1, 재설치 터널 소멸 P0 — Sandbox 실측 적발): 터널(외부 접속) 구성 실패를
//   Log 만 남기고 지나가면 "설치 완료인데 외부 접속 1033" 침묵 고장이 된다(7/14 실측 그대로).
//   실패 시 경고 MsgBox 로 가시화하되 설치는 중단하지 않는다(워치독 WS-28-D 가 db.conf 토큰으로 재시도).
procedure WarnTunnelFailure(Context: String; Code: Integer);
begin
  Log('[20260714작1 W1] 터널 구성 실패: ' + Context + ' ResultCode=' + IntToStr(Code));
  if not G_TunnelWarnShown then begin
    G_TunnelWarnShown := True;
    MsgBox('외부 접속 연결 구성에 실패했습니다.' + #13#10 +
           '설치는 계속되지만 외부(다른 기기)에서 접속이 안 될 수 있습니다.' + #13#10 +
           '프로그램이 자동 복구를 시도하며, 계속 안 되면 고객센터에 문의해 주세요.',
           mbError, MB_OK);
  end;
end;

// ★ 봉합 20260714작1 (W1-2): 기존 db.conf 에서 키 값을 읽는다(재설치 보존용).
//   db.conf 는 [UninstallDelete] 대상이 아니라 덮어 재설치·제거 후 재설치 모두에서 살아남는다.
function ReadDbConfValue(const Key: String): String;
var
  Lines: TStringList;
  i: Integer;
  Line: String;
begin
  Result := '';
  if not FileExists(ExpandConstant('{app}\db.conf')) then Exit;
  Lines := TStringList.Create;
  try
    Lines.LoadFromFile(ExpandConstant('{app}\db.conf'));
    for i := 0 to Lines.Count - 1 do begin
      Line := Trim(Lines[i]);
      if Pos(Key + '=', Line) = 1 then begin
        Result := Copy(Line, Length(Key) + 2, Length(Line));
        Exit;
      end;
    end;
  finally
    Lines.Free;
  end;
end;

// ★ 봉합 20260730작8 P0-3 (사장님 지시: "재설치 할때 동작할수 있게 만들어놔")
//   ■ 왜 필요한가 (급소)
//     db.conf 는 6단계(터널)보다 **앞**에서 저장된다(SaveToFile). 그래서 6단계에서 자동복구로
//     새 터널 토큰을 받아도 db.conf 에는 **옛(죽은) 토큰이 그대로 남는다.**
//     그러면 워치독 WS-28-D 가 자가복구할 때 또 죽은 토큰으로 service install 을 해
//     영구히 1033 이 된다(헌법 #28 자가복구가 구조적으로 무력화).
//   ⇒ 새 토큰을 받은 즉시 db.conf 의 해당 줄만 교체한다. 다른 줄은 손대지 않는다(헌법 #1).
//   ACL(icacls Administrators·SYSTEM)은 파일을 새로 만들지 않고 내용만 덮으므로 유지된다.

// ★ 봉합 20260730작10 P0-5 (사장님 결재 2026-07-30 "결재!!") — 침묵 실패 가시화
//
// ■ 왜 이 함수가 필요한가 (오늘 손실의 직접 원인)
//   6-1·6-2(cloudflared 좀비정리·service install)는 Exec(..., SW_HIDE, ...) 로 돌면서
//   ResultCode 를 받아놓고 **한 번도 검사하지 않았고 로그도 남기지 않았다.**
//   그래서 cloudflared 가 명확한 오류를 내도 세 겹으로 은폐됐다:
//     ① SW_HIDE  → 화면에 안 보임
//     ② ResultCode 미검사 → 실패를 무시하고 다음 단계로 진행
//     ③ 로그 0줄 → 기록도 없음
//   실측 2026-07-30 샌드박스: sc query cloudflared = 1060(미등록)인데 설치 로그의 마지막 줄은
//     '[FixupWatchdog] 정정 완료' 였다. 6-1·6-2 가 성공했는지 실패했는지 **아무 흔적이 없어**
//     PM 이 원인을 9번 잘못 짚었다(MariaDB·seed-parent·PowerShell·timeout·파일누락·실행불가·
//     토큰손상·권한·샌드박스제약 — 전부 실측이 반증). 오류 한 줄만 로그에 있었으면 5분에 끝났다.
//   ⇒ 헌법 #15(빈 catch 금지 = 실패를 침묵시키지 않는다) 위반을 봉합한다.
//
// ■ 무엇을 하나
//   cmd /C 로 명령을 돌리면서 stdout·stderr 를 임시파일로 받아 **로그에 그대로 남긴다.**
//   종료코드도 함께 기록한다. 이제 다음 설치에서는 원인이 로그에 직접 찍힌다.
//
// ■ 설계 판단
//   · Exec 는 출력을 못 받는다(Inno 제약) → `> file 2>&1` 리다이렉트 + LoadStringsFromFile.
//     이 파일의 기존 사용례(395·519행 PowerShell 결과 수신)와 동일 패턴이라 안전하다.
//   · 실패해도 설치를 중단하지 않는다. 이 함수는 '보이게 하는' 역할만 한다.
//     중단 판단은 호출부가 한다(6-3 RUNNING 확인이 최종 판정 — 작8 P0-1).
//   · 출력이 길 수 있어 최대 12줄만 남긴다(로그 폭주 방지). cloudflared 오류는 앞부분에 나온다.
//   · 임시파일은 {tmp} 에 두고 즉시 삭제한다. 토큰이 인자에 있을 수 있어 **명령문 전문은 로그에
//     남기지 않는다**(헌법 #22 — 시크릿 로그 유출 차단). Tag 로만 구분한다.
function ExecLogged(const Tag, CmdLine: String): Integer;
var
  OutFile: String;
  Lines: TArrayOfString;
  Line: String;
  i, ResultCode, Shown, Total: Integer;
begin
  OutFile := ExpandConstant('{tmp}\execlog-' + Tag + '.txt');
  DeleteFile(OutFile);

  // cmd /C "명령 > 파일 2>&1" — 바깥 따옴표는 cmd 가 벗기므로 내부 인용을 그대로 보존한다.
  Exec(ExpandConstant('{cmd}'), '/C ' + CmdLine + ' > "' + OutFile + '" 2>&1',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := ResultCode;

  if ResultCode = 0 then
    Log('[작10 P0-5][' + Tag + '] 종료코드 0 (성공)')
  else
    Log('[작10 P0-5][' + Tag + '] ❌ 종료코드 ' + IntToStr(ResultCode) + ' (실패)');

  // 출력 본문 — 성공이어도 남긴다. cloudflared 는 실패를 exit 0 으로 내는 사례가 있어
  //   (작8 P0-3 실측: 무효 토큰 → exit 0 으로 조용히 종료) 종료코드만으로는 부족하다.
  if FileExists(OutFile) and LoadStringsFromFile(OutFile, Lines) then
  begin
    Shown := 0;
    Total := 0;
    for i := 0 to GetArrayLength(Lines) - 1 do
    begin
      if Trim(Lines[i]) <> '' then
      begin
        Total := Total + 1;
        // 검증 P1-2: 상한 20 (sc query 정상 출력이 12줄 근처라 12는 WIN32_EXIT_CODE 를 자를 수 있다.
        //   작8 P0-1 이 확정한 진범 지표가 그 줄이므로 잘리면 진단이 무의미해진다).
        if Shown < 20 then
        begin
          // 검증 P1-4: 제어문자 제거 — sc/cloudflared 출력에 CR(#13)·BS(#8) 가 섞이면
          //   설치 로그가 깨진다(timeout 카운트다운의 '...1'+BS+'0' 이 실측 예).
          //   ※ 문자 인덱스 대입(Line[j] := ...)은 이 파일에 선례가 0건이라 쓰지 않는다.
          //     StringChangeEx 는 257·258행에 기존 사용례가 있어 검증된 수단이다
          //     (검증팀 교훈: "선례 없는 기법을 '검증된 패턴'이라 부른 것이 P0 를 놓친 원인").
          Line := Lines[i];
          StringChangeEx(Line, #13, ' ', True);
          StringChangeEx(Line, #10, ' ', True);
          StringChangeEx(Line, #8,  '', True);
          // 검증 #22 지적(V-4 미확정): cloudflared 가 인자 파싱 실패 시 usage 에 입력 인자를
          //   에코하는 CLI 패턴이 흔하다. 실측 전이므로 **안전측으로 마스킹**한다.
          //   토큰이 안 나오면 이 치환은 무해하고, 나오면 로그 유출을 막는다(헌법 #22).
          if (G_TunnelToken <> '') and (Pos(G_TunnelToken, Line) > 0) then
            StringChangeEx(Line, G_TunnelToken, '***TOKEN-MASKED***', True);
          Log('[작10 P0-5][' + Tag + '] > ' + Trim(Line));
          Shown := Shown + 1;
        end;
      end;
    end;
    if Shown = 0 then
      Log('[작10 P0-5][' + Tag + '] > (출력 없음)');
    // 검증 P1-2: 잘린 사실 자체를 남긴다. 종전엔 조용히 버려 "출력이 이게 전부"로 오판하게 했다.
    if Total > Shown then
      Log('[작10 P0-5][' + Tag + '] > (...' + IntToStr(Total - Shown) + '줄 생략)');
  end
  else
    // 검증 P1-1·P0-3: 단정 삭제. cmd 는 리다이렉트 좌변이 실행에 도달해야 파일을 만든다.
    //   즉 '&&' 좌변 실패(정상 상황)에서도 파일이 안 생긴다 — 종전 문구
    //   "명령 자체가 실행되지 않았을 수 있다"는 백지 신규설치 전건에 찍히는 거짓 진단이었다.
    Log('[작10 P0-5][' + Tag + '] > (출력 파일 없음 — 위 종료코드로 판정할 것)');

  // 검증 P1-3: 삭제 실패를 침묵하지 않는다(헌법 #15). 백신이 파일을 잡으면 {tmp} 에 잔류하고,
  //   6-2 출력에는 토큰이 에코될 가능성이 있어(V-4 미확정) 잔류 자체가 시크릿 노출 창이 된다.
  if not DeleteFile(OutFile) then
    Log('[작10 P0-5][' + Tag + '] ⚠️ 임시 출력파일 삭제 실패 — ' + OutFile + ' 잔류(백신 점유 의심)');
end;

procedure UpdateDbConfValue(const Key, Value: String);
var
  Lines: TStringList;
  i: Integer;
  Found: Boolean;
  ConfPath: String;
begin
  ConfPath := ExpandConstant('{app}\db.conf');
  if not FileExists(ConfPath) then Exit;
  Found := False;
  Lines := TStringList.Create;
  try
    Lines.LoadFromFile(ConfPath);
    for i := 0 to Lines.Count - 1 do begin
      if Pos(Key + '=', Trim(Lines[i])) = 1 then begin
        Lines[i] := Key + '=' + Value;
        Found := True;
      end;
    end;
    if not Found then Lines.Add(Key + '=' + Value);
    Lines.SaveToFile(ConfPath);
    Log('[20260730작8 P0-3] db.conf 갱신: ' + Key + ' (값 길이 ' + IntToStr(Length(Value)) + ')');
  finally
    Lines.Free;
  end;
end;

// ★ 봉합 20260714작1 (W5, 재설치 P0 — 2026-07-15 Sandbox 실측 로그 확정): 덮어 재설치는 [UninstallRun]
//   을 거치지 않아 기존에 실행 중인 API(schtasks HitPan-ERP-API-*)·워치독·cloudflared 가 DLL 을 붙잡은 채로
//   남는다. 그 위에 파일을 덮으면 수백 건 'DeleteFile 실패; 코드 5(ACCESS_DENIED=파일 사용중)' → 신·구 DLL
//   혼재 → 직후 seed-parent(HitPan.API.exe)가 혼재 DLL 로 뜨다 예외 크래시(exit 5) → 설치 전체 중단.
//   백지 신규설치가 멀쩡했던 건 붙잡을 프로세스가 없어서였다. 파일 복사(ssInstall) 전에 기존 실행 요소를
//   전부 정지시켜 파일 잠금을 해소한다(제거가 아니라 '정지'라 데이터·설정 무손상, 헌법 #20 재설치 안전).
// ★ 봉합 20260721작1 (P0, 2026-07-21 백지 Sandbox 실측 확정 — 사장님 결재): 이 정지 로직은
//   '재설치'(이미 설치된 위에 다시) 전용이다. 그런데 CurStepChanged 가 ssInstall 마다 최초/재설치
//   구분 없이 무조건 호출해, 백지 최초 설치에서도 taskkill /F /IM 이 돌았다. Windows Sandbox 는
//   taskkill /IM 의 프로세스 이미지 열거가 제약 계층에 걸려 '대상 없음' 반환 없이 무한 hang →
//   설치 전체 정지(로그 '[재설치안전] ... 정지 시작'에서 멈춤, taskkill 죽이면 다음 taskkill 재 hang).
//   = 전 신규 고객 설치 차단. 사장님 헌법 #40 '덮어 재설치는 존재하지 않는 경로'와도 정면 충돌
//   (재설치용 코드가 최초 설치를 죽임). → 최초 설치면 이 함수를 통째로 건너뛴다(정지시킬 것이 원천적
//   으로 없으므로 스킵이 정확·안전). 재설치 판정 = Inno 가 이전 설치 때 남긴 언인스톨 레지스트리 키
//   (AppId F4E2A1D0..., 64-bit HKLM `..\Uninstall\{AppId}_is1`) 존재 여부. 이 코드베이스가 이미
//   FileExists(ExpandConstant('{app}\...')) 관용구를 쓰고 있어(예: L1245 db.conf) 안전한 판정이다.
//   축 B(재설치 경로에서 taskkill /IM 자체의 hang 방어)는 다음 작지서 P1 로 분리(CTO 결재).
//
//   판정 방식 (CTO B-1 재검토): 애초 레지스트리 언인스톨 키를 보려 했으나, 이 [Code] 섹션은 리터럴
//   중괄호를 문자열에 쓴 사례가 0건이고(Inno Pascal 에서 '{' 는 주석 시작 문자라 전처리기 오해 위험),
//   대신 {app} 파일 존재 판정이 이 파일의 검증된 관용구다. db.conf 는 [UninstallDelete] 대상이 아니라
//   '덮어 재설치·제거 후 재설치 모두에서 살아남는' 파일(L1237 주석)이라, 재설치 판정에 가장 정확하다.
//   {app} 은 ssInstall 시점이면 이미 확정(wpReady 통과 후) — CTO 가 우려한 미초기화는 그보다 훨씬 이른
//   SerialKeyPage 시점 얘기였다.
function IsPreviouslyInstalled(): Boolean;
begin
  // db.conf 가 있으면 이전 설치가 있었다는 뜻(제거 후 재설치에도 살아남음). 없으면 백지 최초 설치.
  Result := FileExists(ExpandConstant('{app}\db.conf'))
         or FileExists(ExpandConstant('{app}\hitpan-keys.conf'));
end;

procedure StopRunningComponentsForReinstall();
var
  ResultCode, i: Integer;
begin
  // ★ 최초 설치 스킵 가드 (봉합 20260721작1). 로그도 남기지 않고 통째 반환 —
  //   백지엔 정지시킬 프로세스가 없어 스킵이 정확하고, taskkill hang 을 원천 차단한다.
  if not IsPreviouslyInstalled() then begin
    Log('[재설치안전] 최초 설치 감지(이전 설치 흔적 없음) — 프로세스 정지 로직 건너뜀(hang 원천 차단)');
    Exit;
  end;
  Log('[재설치안전] 파일 복사 전 기존 실행 프로세스 정지 시작');
  // ① ERP API — schtasks ONSTART/keepalive 작업 종료 + 프로세스 강제 종료 (슬롯 1~5 전부)
  for i := 1 to 5 do begin
    Exec(ExpandConstant('{cmd}'), '/C schtasks /End /TN "HitPan-ERP-API-tenant-' + IntToStr(i) + '"',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{cmd}'), '/C schtasks /End /TN "HitPan-ERP-API-keepalive-' + IntToStr(i) + '"',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
  // ② 워치독 서비스 정지 (재설치 후 InstallWatchdog.ps1 이 다시 등록·기동한다)
  Exec(ExpandConstant('{cmd}'), '/C sc stop HitPanWatchdog', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  // ③ cloudflared 서비스 정지 (6단계에서 좀비정리·재설치가 이어받는다)
  Exec(ExpandConstant('{cmd}'), '/C sc stop cloudflared', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  // ④ 잔여 프로세스 강제 종료 — 서비스로 안 잡히는 자식/고아 프로세스까지 (파일 잠금 완전 해소)
  Exec(ExpandConstant('{cmd}'), '/C taskkill /F /IM HitPan.API.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{cmd}'), '/C taskkill /F /IM HitPan.Watchdog.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{cmd}'), '/C taskkill /F /IM cloudflared.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  // ⑤ 파일 핸들 해제 대기 (Windows SCM 정지 완료 + 핸들 반납 여유)
  Sleep(4000);
  Log('[재설치안전] 기존 실행 프로세스 정지 완료 — 파일 복사 진행 가능');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  JwtKey, AesKey, MariaRootPw: String;
  ConfFile, BatchFile, BootstrapFile: String;
  KeysContent, BatchContent, BootstrapContent: TStringList;
  SeedOk: Boolean;
  // 봉합 20260730작8 P0-1: cloudflared RUNNING 확인용(지역 변수 — 전역 오염 0).
  TunnelRunning: Boolean;
  TunnelTry: Integer;
  // 봉합 20260731: 6-1 sc stop 종료코드 보관 — 1060(서비스 미등록)이면 taskkill 을 건너뛴다.
  //   Sandbox 에서 taskkill /IM 이 무한 hang 하는 것이 7/30 회귀 진범이었다(7/21작1 축 B).
  ScStopCode: Integer;
begin
  // ★ 봉합 20260714작1 (W5): 파일 복사(ssInstall) 직전 = 기존 실행 프로세스 정지의 유일한 안전 시점.
  //   ssPostInstall(복사 후)에 하면 이미 잠금 사고가 끝난 뒤라 늦다.
  if CurStep = ssInstall then begin
    StopRunningComponentsForReinstall();
    Exit;
  end;
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
  //    봉합: 예전엔 전 고객 공통 고정 계정·비번을 그대로 적어두었는데, 사고 #18 정합 → 회사별 분리
  //    (20260731 시크릿스캔 봉합: 이 주석에 남아 있던 옛 비번 문자열 자체를 제거.
  //     실행 코드가 아니라 이력 설명이라 값은 필요 없다 — 값이 남아 있으면 유출 표면만 는다)
  //    G_DbName = hitpan_erp_{tenantCode}
  //    G_DbUser = hitpan_{tenantCode}
  //    G_DbPassword = 랜덤 32자 영문·숫자 (사고 #26 봉합 — Base64 +/= SQL escape 사고 차단)
  // ★ P0 봉합 (작6, 사장님 결재 2026-06-30, 안A 단일 가드): 시리얼 없이 설치(LOCAL)·시리얼
  //   인증 실패 폴백 분기는 DetermineMultiTenantSlot()을 호출하지 않고 Exit 하므로(L521·L559),
  //   G_DbUser/G_DbName 기본값(그 함수 L276-277에만 존재)이 비어 mysql 인자가
  //   'mysql -u  -p<비번> ...' 로 깨지고 ERROR 1045 → DB 초기화 실패 → 설치 DOA(헌법 #20 끊김).
  //   DB 셋업 진입 직전, 빈 값이면 단독 모드 기본값으로 보장한다(DB셋업 들어오는 모든 경로 차단).
  //   M4 Windows Sandbox 종단 빌드테스트 실측으로 발견(에러로그 + 코드 확정).
  if G_DbName = '' then G_DbName := 'hitpan_erp';
  if G_DbUser = '' then G_DbUser := 'hitpan';
  // ★ P0 봉합 (작7, 사장님 결재 2026-07-01): LOCAL(시리얼 없이) 모드는 DetermineMultiTenantSlot()을
  //   안 타서 G_ApiPort가 전역 초기값 0으로 남고 → db.conf에 API_PORT=0 기록(L1005·L1056) →
  //   hitpan-start.bat이 '--urls http://0.0.0.0:0'로 API를 띄워 5257이 아닌 랜덤포트로 뜸 →
  //   web-server.ps1(5257 프록시)과 불일치 → 로그인 502 Bad Gateway. (샌드박스 실측: API_PORT=0 확정.)
  //   bat 폴백 'if "%API_PORT%"==""'는 빈값만 잡고 "0"은 못 잡으므로 여기서 소스에서 5257 보장.
  //   G_DbName/User 빈값 가드와 동일 진범(LOCAL이 슬롯 로직 우회, 작6 정합).
  if G_ApiPort = 0 then G_ApiPort := 5257;
  if G_SlotIndex = 0 then G_SlotIndex := 1;
  G_DbPassword := GenerateAlphanumericKey(32);

  BatchFile := ExpandConstant('{tmp}\db-setup.bat');
  BatchContent := TStringList.Create;
  try
    BatchContent.Add('@echo off');
    BatchContent.Add('setlocal enabledelayedexpansion');
    BatchContent.Add('set "PATH=%PATH%;C:\Program Files\MariaDB 11.4\bin;C:\Program Files\MariaDB 10.11\bin"');
    // ★ P0 근본 재봉합 (작7 안C, 사장님 결재 2026-07-01): mysql 절대경로 변수 + for/f 제거.
    //   진범(1.2.18 진단 로그 + 재현 실측 확정): 카운트 검증이 for/f (''mysql ...'') 구조인데,
    //   for/f 안에서 cmd가 명령을 재파싱할 때 PATH의 'C:\Program Files'(공백)에서 잘려 mysql이
    //   실행 안 됨 → TBL_COUNT/FINAL_COUNT 빈값 → 'if LSS 124' → exit 1 → 설치 DOA.
    //   root로 바꿔도 안 고쳐진 이유 = 유저가 아니라 for/f 안 mysql 실행 자체가 진범이었기 때문.
    //   봉합: ① mysql을 절대경로+따옴표(!MYSQL!)로 호출해 PATH·공백 의존 제거,
    //         ② 카운트는 for/f 대신 '-e "SELECT" > 파일' 후 'set /p VAR=<파일'로 읽어 파싱 제거.
    BatchContent.Add('set "MYSQL=C:\Program Files\MariaDB 11.4\bin\mysql.exe"');
    BatchContent.Add('if not exist "!MYSQL!" set "MYSQL=C:\Program Files\MariaDB 10.11\bin\mysql.exe"');
    BatchContent.Add('set "CNTF=%LOCALAPPDATA%\Temp\hitpan_cnt.txt"');
    // ★ 진단 로그 (작7): 배치 각 단계 성패를 파일로 남긴다(봉합 검증용, 비번 미기록).
    BatchContent.Add('set "DIAGLOG=%LOCALAPPDATA%\Temp\hitpan_dbsetup_diag.log"');
    BatchContent.Add('echo ===== db-setup 진단 시작 %DATE% %TIME% ===== > "!DIAGLOG!"');
    BatchContent.Add('echo [chk] MYSQL=!MYSQL! >> "!DIAGLOG!"');
    // 사고 #16·#21·#22 봉합 — 회사별 DB 박음
    BatchContent.Add(Format('"!MYSQL!" -u root -p%s -e "CREATE DATABASE IF NOT EXISTS %s CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"', [MariaRootPw, G_DbName]));
    // 사고 #18 봉합 — 회사별 user + 랜덤 비번 (하드코딩 0건)
    BatchContent.Add(Format('"!MYSQL!" -u root -p%s -e "CREATE USER IF NOT EXISTS ''%s''@''localhost'' IDENTIFIED BY ''%s''; GRANT ALL ON %s.* TO ''%s''@''localhost''; FLUSH PRIVILEGES;"', [MariaRootPw, G_DbUser, G_DbPassword, G_DbName, G_DbUser]));
    BatchContent.Add('echo [chk] CREATE DB/USER errorlevel=!errorlevel! >> "!DIAGLOG!"');
    // ★ 봉합 2026-06-18 (트리거 import 안전화): 새 MariaDB는 log_bin=ON + trust=0 기본일 수 있어
    //   UUID() 등 비결정 함수를 쓰는 트리거 8개 생성이 ERROR 1419로 실패 → 스키마 부분 import → 설치 실패.
    //   import 전 GLOBAL log_bin_trust_function_creators=1 로 트리거 생성을 허용(헌법 #20 끊김 방지).
    BatchContent.Add(Format('"!MYSQL!" -u root -p%s -e "SET GLOBAL log_bin_trust_function_creators=1;"', [MariaRootPw]));
    BatchContent.Add('echo [chk] SET GLOBAL errorlevel=!errorlevel! >> "!DIAGLOG!"');
    // 봉합 2026-06-17 (1.2.14): 로그인 500 진범 봉합 — 스키마 import 판정 정정.
    //   진범1: hitpan_db.sql 안 'USE hitpan_erp'로 회사별 DB 지정이 무력화 → 별도 봉합(덤프 스트립).
    //   진범2: 기존 'items/partners COUNT' 판정은 신규 빈 DB에서 테이블 자체가 없어 오작동.
    //          + 'skip=1'(-N이라 결과 1줄인데 그 줄을 버림) + '2^^^>nul'(caret 3겹으로 redirect 사망) 2중 버그.
    //   봉합: information_schema로 users 테이블 '존재 여부' 1차 판정(빈 DB에서도 에러 0).
    //         테이블 없으면 신규 → 무조건 import / 있으면 운영데이터(items+partners) 보호 분기.
    //   goto/괄호 혼용은 .bat 파서 사고 위험 → goto 없이 평면 if 구조로 작성.
    //   1차: users 테이블 존재 여부 + 2차: 운영 데이터 보호. 두 변수를 먼저 구한 뒤 한 줄 분기.
    // 1차: 테이블 수(BASE TABLE) + 운영 데이터(items+partners) 파악
    // ★ P0 근본 재봉합 (작7 안C): 카운트를 for/f 대신 '파일 출력 → set /p 읽기'로 (위 §안C 주석 참조).
    //   for/f (''mysql...'')가 PATH 공백에서 mysql을 못 실행하는 진범을 통째 제거.
    BatchContent.Add('set TBL_COUNT=0');
    BatchContent.Add(Format('"!MYSQL!" -u root -p%s %s -N -B -e "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_type=''BASE TABLE''" > "!CNTF!" 2> nul', [MariaRootPw, G_DbName]));
    BatchContent.Add('set /p TBL_COUNT=<"!CNTF!"');
    BatchContent.Add('if "!TBL_COUNT!"=="" set TBL_COUNT=0');
    BatchContent.Add('echo [chk] 1차 TBL_COUNT=[!TBL_COUNT!] >> "!DIAGLOG!"');
    BatchContent.Add('set EXISTING_DATA=0');
    // 운영 데이터 카운트는 테이블이 있을 때만 의미 — 없으면 쿼리가 에러내므로 TBL_COUNT>0일 때만 조회
    BatchContent.Add(Format('if !TBL_COUNT! GTR 0 ("!MYSQL!" -u root -p%s -N -B -e "SELECT COALESCE((SELECT COUNT(*) FROM %s.items),0)+COALESCE((SELECT COUNT(*) FROM %s.partners),0)" > "!CNTF!" 2> nul & set /p EXISTING_DATA=<"!CNTF!")', [MariaRootPw, G_DbName, G_DbName]));
    BatchContent.Add('if "!EXISTING_DATA!"=="" set EXISTING_DATA=0');
    // 봉합 2026-06-17 (1.2.15, P1): 재설치 멱등성 — 운영 데이터 0건인데 스키마 불완전(91개 미만)이면
    //   손상된 부분 import로 간주하고 DROP 후 재생성. 운영 데이터 있으면 절대 DROP 금지(헌법 #1·#22).
    // 봉합 (2026-06-23, SHIP-DDL-01): 정본 구조가 121테이블이므로 불완전 스키마 임계값 91→121 정정(구조 드리프트).
    // 봉합 (2026-06-29, A안 고리2): local_update_consents(헌법 #30) 편입으로 정본 121→122 정정.
    // 봉합 (2026-06-29, A안 고리2 마지막 빈 칸): local_update_status(헌법 #30) 편입으로 정본 122→123 정정.
    // 봉합 (2026-06-29, 고리4 ①단계): schema_migrations(헌법 #36) 편입으로 정본 123→124 정정.
    BatchContent.Add(Format('if !EXISTING_DATA! EQU 0 if !TBL_COUNT! GTR 0 if !TBL_COUNT! LSS 124 (echo 불완전 스키마 !TBL_COUNT!/124 감지 - 재생성. & "!MYSQL!" -u root -p%s -e "DROP DATABASE IF EXISTS %s; CREATE DATABASE %s CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci; GRANT ALL ON %s.* TO ''%s''@''localhost''; FLUSH PRIVILEGES;" & set TBL_COUNT=0)', [MariaRootPw, G_DbName, G_DbName, G_DbName, G_DbUser]));
    // 분기: 운영 데이터 있으면 보호(건너뜀, 헌법 #1) / 없으면 import
    //   봉합 2026-06-17 (1.2.15, P1): import stderr를 로그로 남기고 errorlevel 즉시 검사(--force 금지, 헌법 #15)
    //   ★ 안C: import mysql도 절대경로(!MYSQL!)로. import는 '< file' stdin이라 for/f는 아니었으나 PATH 의존 제거로 통일.
    BatchContent.Add(Format('if !EXISTING_DATA! GTR 0 (echo 기존 운영 데이터 !EXISTING_DATA!건 감지. 시드 import 건너뜀.) else (echo 스키마 import 실행. & "!MYSQL!" -u root -p%s --show-warnings %s < "%s" 2> "%%TEMP%%\hitpan_import_err.log" & if errorlevel 1 (echo [오류] 스키마 import 실패. 로그: %%TEMP%%\hitpan_import_err.log & exit /b 1))', [MariaRootPw, G_DbName, ExpandConstant('{app}\hitpan_db.sql')]));
    // 봉합 검증 가드(1.2.15, SHIP-DDL-01 정정 2026-06-23 / A안 고리2 2026-06-29 local_update_status 122→123 / 고리4 ① schema_migrations 123→124): import 후 테이블 124개 + users 실존 재확인. 미달이면 명확한 실패(헌법 #15·#19).
    // ★ P0 재봉합 (작7, 안A): 최종 검증 카운트도 root 통일 (위 §진범 주석 참조).
    BatchContent.Add('echo [chk] import 이후 TBL_COUNT=[!TBL_COUNT!] EXISTING_DATA=[!EXISTING_DATA!] >> "!DIAGLOG!"');
    // ★ P0 근본 재봉합 (작7 안C): 최종 검증 카운트도 for/f 제거 → 파일출력.
    BatchContent.Add('set FINAL_COUNT=0');
    BatchContent.Add(Format('"!MYSQL!" -u root -p%s %s -N -B -e "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_type=''BASE TABLE''" > "!CNTF!" 2> nul', [MariaRootPw, G_DbName]));
    BatchContent.Add('set /p FINAL_COUNT=<"!CNTF!"');
    BatchContent.Add('if "!FINAL_COUNT!"=="" set FINAL_COUNT=0');
    BatchContent.Add('set USERS_OK=0');
    BatchContent.Add(Format('"!MYSQL!" -u root -p%s %s -N -B -e "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=''users''" > "!CNTF!" 2> nul', [MariaRootPw, G_DbName]));
    BatchContent.Add('set /p USERS_OK=<"!CNTF!"');
    BatchContent.Add('if "!USERS_OK!"=="" set USERS_OK=0');
    BatchContent.Add('echo [chk] 최종검증 FINAL_COUNT=[!FINAL_COUNT!] USERS_OK=[!USERS_OK!] >> "!DIAGLOG!"');
    // 운영 데이터 보호로 import 건너뛴 경우(EXISTING_DATA>0)는 이미 정상 DB이므로 검증 통과로 간주
    BatchContent.Add('if !EXISTING_DATA! GTR 0 (echo 기존 운영 DB 유지 - 검증 생략. & exit /b 0)');
    BatchContent.Add(Format('if !USERS_OK! EQU 0 (echo [오류] DB 초기 설정 실패 - users 테이블 없음. & exit /b 1)', []));
    BatchContent.Add(Format('if !FINAL_COUNT! LSS 124 (echo [오류] DB 초기 설정 실패 - 테이블 !FINAL_COUNT!/124개만 생성됨. & exit /b 1) else (echo DB 스키마 검증 완료 - 테이블 !FINAL_COUNT!개 + users 정상.)', []));
    // ★ P0 재봉합 (작7, 사장님 결재 2026-07-01, hitpan 가드 제거): 어제 안A에서 넣은 hitpan 접속 가드를
    //   설치 차단(exit 1)에서 제외한다. 진범 확정(1.2.19 진단 로그): 카운트 검증(124/124/users)은 안C로
    //   전부 통과하는데, 이 가드의 'mysql -u hitpan -p<G_DbPassword> -e "SELECT 1"'만 errorlevel=1로 실패해
    //   유일하게 설치를 막고 있었다. hitpan 유저는 CREATE USER로 정상 생성되며(로그 확인), DB_PASSWORD는
    //   db.conf에 CREATE USER와 동일한 G_DbPassword가 기록되어 ERP 첫 기동이 실제 접속을 검증한다.
    //   따라서 설치 시점 hitpan 접속 가드는 과잉이며 정상 설치를 막았다 → 차단 제거.
    //   단 접속 성패는 진단 로그에만 남겨 원인(명령줄 -p 비번 해석 차이) 추적은 계속 가능하게 둔다(차단 X).
    BatchContent.Add(Format('"!MYSQL!" -u %s -p%s %s -e "SELECT 1" > nul 2> nul', [G_DbUser, G_DbPassword, G_DbName]));
    BatchContent.Add('echo [chk] (참고) hitpan SELECT 1 errorlevel=!errorlevel! (설치 차단 안 함) >> "!DIAGLOG!"');
    BatchContent.Add('echo [chk] 배치 정상 종료 도달 (exit 0 예정) >> "!DIAGLOG!"');
    BatchContent.Add('del "!CNTF!" > nul 2> nul');
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
  // ★ 봉합 20260714작1 (W1-2, 재설치 터널 소멸 P0 — Sandbox 실측 적발): 이번 설치에서 터널 토큰을
  //   못 받았으면(재설치 분기·백오피스 발급 실패 등) 직전 설치의 TUNNEL_ID·TUNNEL_TOKEN 을 보존한다.
  //   빈값 덮어쓰기 = 워치독 자가복구(WS-28-D) 영구 무력 + 아래 6단계 터널 재설치 스킵 = 1033 침묵 고장.
  //   보존된 토큰으로 6단계 service install 도 다시 돌므로 재설치에서 터널이 그대로 살아난다.
  if G_TunnelToken = '' then begin
    G_TunnelToken := ReadDbConfValue('TUNNEL_TOKEN');
    if G_TunnelId = '' then G_TunnelId := ReadDbConfValue('TUNNEL_ID');
    if G_TunnelToken <> '' then
      Log('[20260714작1 W1-2] 터널 토큰 미수령 — 기존 db.conf 값 보존(재설치 정합)');
  end;

  // ★ 봉합 20260730작8 P0-3 (사장님 지시: "재설치 할때 동작할수 있게 만들어놔")
  //   ■ W1-2 보존 로직의 사각지대 (재설치 1033 진범 후보)
  //     W1-2 는 "새 토큰을 못 받으면 옛 토큰을 되살려 쓴다". 재설치 연속성엔 옳다.
  //     그런데 **본사에서 터널을 삭제한 뒤 재설치**하면 그 옛 토큰은 이미 죽은 토큰이다.
  //     cloudflared 는 무효 토큰을 받으면 오류가 아니라 **정상 종료(exit 0)** 로 조용히 죽는다.
  //       → 실측(test2): sc query = STOPPED / WIN32_EXIT_CODE = 0 (오류 아님) → 1033
  //     즉 "보존"이 "죽은 토큰 재사용"으로 뒤집히는 순간 침묵 고장이 된다(헌법 #15).
  //
  //   ■ 무엇을 하나 — 죽은 토큰을 물었을 가능성을 가시화한다
  //     이번 설치에서 백오피스가 새 토큰을 줬다면(G_BootstrapOk) 그 값이 최신이므로 안전하다.
  //     반대로 부트스트랩이 실패했는데 옛 토큰으로 진행하는 경우 = 검증 불가 상태다.
  //     여기서 설치를 막지는 않는다(오프라인·일시 장애 재설치를 살려야 한다 — W1-2 취지 보존).
  //     대신 아래 6-3 의 RUNNING 확인이 실패를 반드시 잡도록 흔적을 남긴다.
  if (G_TunnelToken <> '') and (not G_BootstrapOk) then
    Log('[20260730작8 P0-3] ⚠️ 부트스트랩 미성공 + db.conf 옛 토큰 사용 — 본사에서 터널이 삭제됐다면 ' +
        '이 토큰은 무효이고 cloudflared 가 exit 0 으로 조용히 종료한다(1033). 6-3 RUNNING 확인이 최종 판정.');

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
    // 재설치 P0 2차 벽 봉합 (사장님 결재 2026-07-06, 작지서 20260706작2, 4인회의+보안상무 결재):
    //   부모계정 생성용 부트스트랩 토큰 서명검증 키. 백오피스(LandingPublicController)가 이 키로 서명한 토큰을
    //   ERP(CompanyBootstrapController.VerifyBootstrapToken)가 db.conf 의 이 값으로 로컬 검증(본사 통신 0, 헌법 #30).
    //   미기록 시 ERP 가 DEV 폴백값으로 검증 → 서명 불일치 401 → 부모계정 생성 전면 차단(헌법 #20). TUNNEL_TOKEN 과
    //   동일하게 아래 icacls(Administrators·SYSTEM 만) 보호 + 본사 미전송(헌법 #22, 보안상무 결재 조건2 충족).
    //   [베타=전 고객 공용키 / 정식 전=HMAC(마스터키,tenant_id) 파생키 전환 필수 — 보안상무 결재.]
    BootstrapContent.Add('HITPAN_BOOTSTRAP_TOKEN_KEY=' + G_BootstrapTokenKey);
    BootstrapContent.SaveToFile(ExpandConstant('{app}\db.conf'));
  finally
    BootstrapContent.Free;
  end;
  // 사고 #19 봉합 — db.conf 영역 ACL 박음 (Administrators·SYSTEM만)
  Exec(ExpandConstant('{cmd}'),
       '/C icacls "' + ExpandConstant('{app}\db.conf') + '" /inheritance:r /grant:r "Administrators:F" /grant:r "SYSTEM:F"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // 5-0. 부모계정 오프라인 생성 — seed-parent (작업지시서 20260707작1 ③단계 P0-3, 사장님 결재 2026-07-07).
  //   ★ 순서: DB 초기화(위 db-setup.bat, users 테이블 실존 검증 통과) + db.conf(DB 자격증명·
  //     HITPAN_BOOTSTRAP_TOKEN_KEY) 기록·ACL 완료 직후 = seed-parent 가 필요로 하는 전제(MariaDB·db.conf)가
  //     모두 준비된 최초 시점. seed-parent 는 웹 호스트를 안 띄우므로(DI 만 빌드) API 포트·터널·/health 폴링이
  //     불필요하다 → 여기서 실행이 가장 이르고 안전(오늘 CORS 진범 원천 우회, 헌법 #30 본사 통신 0).
  //   증표(G_SignedProof)가 있을 때만 실행(ShouldSkipPage 가 입력 페이지도 그 조건으로 노출).
  if G_SignedProof <> '' then begin
    SeedOk := RunSeedParent(
      Trim(ParentAccountPage.Values[0]),   // 대표 이름
      Trim(ParentAccountPage.Values[1]),   // 로그인 아이디
      ParentAccountPage.Values[2]);        // 비밀번호(원문 그대로)
    if not SeedOk then begin
      // 실패 = 설치를 명확히 중단(헌법 #15·#19·#20 — "완료처럼 보이는 실패" 차단).
      //   임시 입력파일은 RunSeedParent 내부에서 이미 소각됨(비번 잔존 0).
      //   ★ 봉합 20260707작2 (W1-6): exit 6 = 계정 생성 후 로그인 스모크 검증 실패(설치 환경 문제) —
      //     시리얼·증표 문제가 아니므로 별도 안내(고객이 헛되이 시리얼 재입력을 반복하는 함정 차단).
      if G_SeedParentExitCode = 6 then
        RaiseException('생성된 계정의 로그인 검증에 실패했습니다(설치 환경 문제). 고객센터에 문의해 주세요.')
      else
        RaiseException('부모 계정 생성에 실패했습니다. 시리얼·사업자번호·서명 증표를 확인 후 다시 설치해 주세요. '
          + '문제가 지속되면 고객센터에 문의해 주세요.');
    end;
    // ★ 봉합 20260707작2 (W1-6): exit 10 = 기존 부모계정 유지(ParentExists 멱등 성공) — 재설치 시
    //   방금 입력한 새 비번이 조용히 버려지는 함정 제거: 설치는 계속하되 기존 비번 사용을 명시 고지.
    if G_SeedParentExitCode = 10 then begin
      Log('[SeedParent] exit 10 — 기존 부모계정 유지(방금 입력한 비밀번호 미적용) 고지 표시.');
      MsgBox('기존 부모계정이 유지되었습니다.' + #13#10 +
             '방금 입력하신 비밀번호는 적용되지 않았습니다. 기존 비밀번호로 로그인해 주세요.' + #13#10 +
             '비밀번호를 잊으셨다면 고객센터에 문의해 주세요.', mbInformation, MB_OK);
    end;
    Log('[SeedParent] 부모계정 생성 성공(또는 멱등).');
  end else
    Log('[SeedParent] 증표 미취득 — 부모계정 자동 생성 건너뜀(폴백 기능 미구현 — 로그인 불가 상태로 설치됨, 20260707작2 정정).');

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
    // W-3 (작업지시서 20260716작2): 로컬 자기중복 판정용 사업자번호 해시(다음 설치가 이 값으로 중복 차단).
    BatchContent.Add('BIZ_NO_HASH=' + G_BizNoHash);
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
    BatchContent.Add('  bizNoHash = $kv["BIZ_NO_HASH"];');
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
    // ★ 봉합 20260730작10 P0-5: 이하 6-1·6-2 를 ExecLogged 로 교체 — 침묵 실패 가시화.
    //   종전엔 ResultCode 를 받고도 검사·기록을 0건 했다(헌법 #15 위반). 그 결과 오늘 샌드박스에서
    //   'sc query = 1060(미등록)'인데 로그 마지막 줄이 [FixupWatchdog] 이라 원인 추적이 불가능했다.
    // ★ 검증팀 반려 봉합 (P0-1·P0-2, 2026-07-30 데이비드 박):
    //   종전 한 줄 '&' 체인은 두 가지로 실패했다 — **실측 채증됨**:
    //     ① 리다이렉트가 마지막 명령에만 붙어 sc stop·sc delete 출력이 100% 유실
    //     ② 종료코드가 마지막 timeout 의 것이라 **항상 0** → sc delete 실패에도 '성공' 로그
    //   실측: `sc delete NoSuchSvcXyz & timeout /t 1 /nobreak > f 2>&1`
    //         → EXITCODE=0(실제 sc 는 1060), 파일엔 timeout 카운트다운만
    //   즉 침묵을 **거짓 성공**으로 바꿨다(침묵보다 나쁘다 — 10번째 오답의 재료).
    //   ⇒ 명령을 개별 호출로 분해한다. timeout 은 Pascal Sleep 으로 대체(로그도 깨끗해진다).
    // ★ 봉합 20260731v2 (검증팀 P0-1 반려): 서비스 존재 판정을 sc stop 이 아니라 sc query 로 한다.
    //   1차 봉합은 `ScStopCode = 1060` 으로 판정했는데 **실측 반증됐다**:
    //     cmd /C "sc stop NoSuchSvcXyz > f 2>&1"  → **종료코드 36** (1060 아님)
    //     1060 은 종료코드가 아니라 **출력 본문의 메시지 텍스트**였다.
    //   ⇒ = 1060 은 절대 참이 될 수 없어 taskkill 이 종전과 100% 동일하게 실행됐다.
    //     설계서 §2 에서 스스로 경계한 "봉합한 척" 을 1차 봉합이 그대로 저질렀다.
    //   실측표(이 PC 3회 반복 동일):
    //     sc query  없음=36 / 있음=0    ← 판정에 쓴다(0 이 명확)
    //     sc stop   없음=36 / 권한부족=5
    //     taskkill  대상없음=128
    ScStopCode := ExecLogged('6-1-svcprobe', 'sc query cloudflared');
    ExecLogged('6-1-scstop', 'sc stop cloudflared');
    Sleep(3000);
    ExecLogged('6-1-scdelete', 'sc delete cloudflared');
    Sleep(2000);
    // 좀비 프로세스 영역 제거 (taskkill 영역 — 서비스 영역 안 박혔어도 프로세스 영역 살아있을 가능)
    //
    // ★ 봉합 20260731 (7/30 회귀 진범 — 7/21작1 축 B 미이행분의 잔여 이행):
    //   ■ 진범
    //     `taskkill /F /IM` 은 **Windows Sandbox 에서 무한 대기**한다. 7/21 실측 채증:
    //       taskkill 1828 @23:58:11 → 25분 hang / 죽이면 taskkill 6644 가 또 hang
    //     원인은 /IM 의 **프로세스 이미지 열거**가 Sandbox 제약 계층에서 응답을 못 받는 것이다
    //     (같은 실측에서 Get-CimInstance Win32_Process 도 '액세스 거부').
    //     Exec(..., ewWaitUntilTerminated) 라 반환이 없으면 **설치 전체가 영영 멈춘다.**
    //   ■ 왜 7/30 에 재발했나 — 봉합이 3곳 중 1곳만 됐다
    //     :1450-1452(StopRunningComponentsForReinstall) 만 IsPreviouslyInstalled() 로 가드했고
    //     이 자리(6-1)와 DeinitializeSetup 은 무가드로 남았다. 7/21 커밋 f06615b 메시지가
    //     "축 B 는 다음 작지서 P1 로 분리" 라 스스로 갭을 적어뒀고, 그것이 오늘까지 미이행이었다.
    //     작10(b793664)이 ExecLogged 로 감싸기만 했을 뿐 hang 방어는 0건이었다.
    //   ■ 로그 침묵이 이 지점을 지목한다
    //     7/30 설치 로그 마지막 = '[FixupWatchdog] 정정 완료'(:1797). 그 다음 Log() 는
    //     6-1-2 분기(:1917)까지 없다. 그 사이 실행 요소는 registry.json PS·6-1 sc체인·이 taskkill 뿐.
    //   ■ 왜 IsPreviouslyInstalled() 가드를 쓰지 않았나 (PM 반증 — 그대로 썼으면 봉합한 척만 됐다)
    //     그 함수는 {app}\db.conf 존재로 판정하는데, db.conf 는 :1747 에서 **이미 생성된다.**
    //     즉 백지 신규설치인데도 6단계 시점엔 항상 true 라 가드가 무력이다.
    //   ■ 채택한 봉합 — 실행 조건 축소
    //     백지 신규설치에는 죽일 cloudflared 프로세스가 **존재하지 않는다.**
    //     바로 위 sc stop 이 1060(서비스 없음)을 냈다는 것은 서비스도 프로세스도 없다는 확증이다.
    //     ⇒ 그때는 taskkill 을 아예 실행하지 않는다. hang 원천 차단.
    //     ⇒ 재설치 경로(서비스 존재)에서는 종전대로 실행한다 — 좀비 정리 기능 무손실.
    //   ■ ewNoWait 는 쓰지 않았다 (작10 교훈 — 선례 없는 기법을 '검증된 패턴'이라 부르지 않는다)
    //     이 파일의 ewNoWait 선례 3건은 전부 ShellExec(브라우저 열기)이고 **Exec 에는 0건**이다.
    //     검증 없이 도입하면 taskkill 미종료 상태로 6-2 가 cloudflared.exe 를 만지는 위험이 생긴다.
    //   ■ 건너뛰어도 침묵하지 않는다 (헌법 #15 · 작10 P1-3 정신)
    //     "관측성을 넣는다며 관측성을 파괴" 한 작10 의 실수를 반복하지 않는다. 분기마다 로그를 남긴다.
    //   ※ 판정은 `<> 0`(안전측)이다. `= 36` 이 아니라 `<> 0` 인 이유:
    //     sc query 가 0 이 아닌 값은 전부 "서비스를 정상 조회하지 못했다"는 뜻이고,
    //     그 경우 죽일 프로세스가 있다고 볼 근거가 없다. 36 만 특정하면 다른 실패코드에서
    //     또 taskkill 이 돌아 hang 한다(검증팀 P0-1 교훈 — 코드값을 추측하지 않는다).
    if ScStopCode <> 0 then
      Log('[작10 P0-5][6-1-taskkill] 건너뜀 — sc query=' + IntToStr(ScStopCode) +
          '(서비스 미등록/조회불가)라 잔존 프로세스 없음. Sandbox 무한 hang 원천 차단(20260731v2 · 7/21작1 축 B)')
    else
    begin
      Log('[작10 P0-5][6-1-taskkill] 실행 — sc query=0(서비스 존재) → 잔존 프로세스 정리 필요');
      ExecLogged('6-1-taskkill', 'taskkill /F /IM cloudflared.exe');
    end;
    Sleep(2000);

    // 6-1-2. 좀비 영역 재검사 영역 — 박혀있으면 sc delete 영역 한 번 더
    //   StopPending 영역에 박힌 영역 = 재부팅 영역까지 영역 사라짐 0건
    //   하지만 service install 영역 새 영역 시도 영역 가도되도록 영역 sc delete 영역 한 번 더
    // ★ 검증팀 반려 봉합 (P0-3):
    //   종전 'sc query && sc delete' 는 (a) sc query 출력이 유실되고
    //   (b) 좀비가 없는 정상 경로(=백지 신규설치 전건)에서 && 좌변 실패로 파일이 생성되지 않아
    //   "(명령 자체가 실행되지 않았을 수 있다)" 라는 **거짓 진단**이 매번 찍혔다.
    //   ⇒ query 로 존재를 판정하고(종료코드가 sc 의 것이라 정확하다), 있을 때만 delete 한다.
    //   ※ 설계서 §4-6 의 "cmd 파싱이 흔들린다" 는 근거 없는 추측이었다 — 검증팀이 반증(무해).
    //     실제 문제는 충돌이 아니라 '리다이렉트 범위'와 '출력 유실'이었다.
    if ExecLogged('6-1-2-query', 'sc query cloudflared') = 0 then
    begin
      Log('[6-1-2] 좀비 서비스 잔존 확인 — sc delete 1회 더 실행');
      ExecLogged('6-1-2-delete', 'sc delete cloudflared');
    end
    else
      Log('[6-1-2] 좀비 서비스 없음(정상) — delete 건너뜀');
    Sleep(1000);

    // ★★★ 봉합 20260803작1 W-1 (P0 — 8/03 백지 Sandbox 실측 확정, 사장님 오더) ★★★
    //
    // ■ 진범 (사장님 직접 실행으로 채증)
    //     > sc delete Cloudflared
    //       [SC] DeleteService SUCCESS            ← SCM 에서는 지워졌다
    //     > cloudflared.exe service install
    //       cloudflared service is already installed at Cloudflared;
    //       ... you can do `cloudflared service uninstall` to clean up ...
    //   **방금 지웠는데도 "이미 설치돼 있다"며 설치를 거부한다.**
    //   이어서 uninstall → install 을 하니 그제서야 성공했고, 그때 진짜 이유가 나왔다:
    //       Cannot install event logger:
    //       SYSTEM\CurrentControlSet\Services\EventLog\Application\Cloudflared
    //       registry key already exists
    //   ⇒ cloudflared 는 서비스 존재를 SCM 이 아니라 **자체 레지스트리 키**로 판정한다.
    //     `sc delete` 는 그 키를 지우지 않는다. 키가 남으면 **영구히 설치를 거부**한다.
    //   또 하나: uninstall 출력의 'The specified service has been marked for deletion' —
    //     `sc delete` 직후 서비스는 즉시 사라지지 않고 **삭제 표시 상태**로 남는다.
    //     6-1 이 지우고 3초 뒤 6-2 가 설치를 시도하면 그 상태에 정면으로 걸린다.
    //
    // ■ 8/03 19:02 설치 로그가 이 하나로 전부 설명된다
    //     6-1-scdelete        1060  (SCM 엔 없음. 그러나 cloudflared 잔재는 남음)
    //     6-2-serviceinstall  **1**  ← 잔재 때문에 거부 = 서비스가 안 만들어짐
    //     6-3-scconfig/start/query 1060 (없는 서비스라 당연)
    //     → 터널 미연결 → **외부 접속 1033**
    //
    // ■ 봉합 — cloudflared 자기 명령으로 정리한다
    //   `sc delete` 로는 안 된다. cloudflared 가 오류 메시지에서 직접 안내한 방법을 쓴다.
    //   실패해도 무시한다 — 백지 신규설치엔 잔재가 없어 실패가 정상이다(비차단).
    //   ※ 6-1 의 sc delete 는 남긴다. SCM 등록분 정리는 여전히 필요하다(헌법 #1 — 추가만).
    //   ※ taskkill 가드(6-1)는 한 글자도 건드리지 않는다 — 8/03 실측 통과분(50분 → 2초).
    ExecLogged('6-1-3-svcuninstall',
      '"' + ExpandConstant('{app}\cloudflared.exe') + '" service uninstall');
    // 'marked for deletion' 해소 대기. SCM 이 삭제를 실제로 반영하려면 핸들이 모두 닫혀야 한다.
    //   3초는 6-2 가 삭제 대기 상태에 걸리기 충분히 짧았다(8/03 실측) → 6초로 늘린다.
    Sleep(6000);

    // 6-2. service install 박음 (사고 #11·#28 봉합 후)
    //
    // ★ 봉합 20260730작10 P0-5 (사장님 결재): **이 단계가 오늘 실패의 현장이다.**
    //   실측 2026-07-30 샌드박스: 전제 전부 정상(cloudflared.exe 54MB·version 2026.7.3·
    //   토큰 디코딩 정합·부트스트랩 success·6-1 정상·관리자 권한)인데 결과는 sc query 1060(미등록).
    //   종전 코드는 이 Exec 의 ResultCode 를 버렸고 SW_HIDE 로 출력도 버렸다 → 원인 불명.
    //   ⇒ 이제 종료코드와 cloudflared 의 stdout·stderr 를 로그에 남긴다.
    //
    //   ■ 토큰 노출 차단 (헌법 #22)
    //     토큰은 인자에 실린다. ExecLogged 는 **명령문 전문을 로그에 남기지 않고** Tag 만 남기므로
    //     설치 로그에 시크릿이 새지 않는다. 출력 본문은 cloudflared 의 메시지일 뿐 토큰을 되뱉지 않는다.
    //
    //   ■ 경로 인용
    //     {app} 에 공백이 있다("C:\Program Files\HitPan") → cmd 경유이므로 반드시 따옴표로 감싼다.
    //     종전 Exec 는 파일명을 직접 넘겨 인용이 불필요했으나, cmd /C 로 바뀌었으니 필수다.
    ExecLogged('6-2-serviceinstall',
      '"' + ExpandConstant('{app}\cloudflared.exe') + '" service install ' + G_TunnelToken);
    Sleep(3000);

    // 6-3. 서비스 시작
    //
    // ★ 봉합 20260730작8 P0-1 (사장님 결재 · test2 실측 확정):
    //   진범 = 서비스가 등록·시작까지 됐는데도 그 뒤 STOPPED 로 남아 1033 이 됐다.
    //   실측(test2 PC): sc query = STOPPED / WIN32_EXIT_CODE = 0 (오류 아님, 정상 종료)
    //                  → sc start 수동 실행하니 RUNNING + 접속 즉시 정상
    //   Cloudflare 대시보드 교차확인: 터널 hitpan-t001~t003 전부 '복제본 0'
    //   (= 접속하는 cloudflared 가 하나도 없음). demo 만 복제본 1 → 502(터널 통과).
    //
    //   ■ 왜 sc start 만으로 부족한가
    //     `cloudflared service install` 이 등록하는 시작 유형을 우리가 명시하지 않았다.
    //     그래서 재부팅·서비스 종료 후 자동 복귀가 보장되지 않는다. 아래 sc config 로
    //     start= auto 를 못박아 "고객 손 0번"을 지킨다(헌법 #28·#30).
    //     ※ delayed-auto 가 아니라 auto 다 — 부팅 직후 터널이 붙어야 외부 접속이 산다.
    // 작10 P0-5: 출력까지 남긴다 — 'sc config' 는 서비스 미등록 시 1060 을 내는데,
    //   그 1060 이 곧 6-2 실패의 확증이다(종전엔 code 만 남겨 원인 판별이 안 됐다).
    ExecLogged('6-3-scconfig', 'sc config cloudflared start= auto');

    ExecLogged('6-3-scstart', 'sc start cloudflared');

    // ★ 봉합 20260714작1 (W1-3, 원자성): 6-1 이 기존 서비스를 지운 뒤 6-2 설치가 실패하면 터널이
    //   통째로 소멸하는데 종전엔 어떤 검사도 없었다(7/14 Sandbox 재설치 1033 유력 진범). 설치 후
    //   서비스 존재를 검증하고, 없으면 1회 재시도, 그래도 없으면 경고 가시화(비차단).
    //   ※ 서비스명: cloudflared 2026.3.0 의 `service install` 등록명은 'Cloudflared' — sc 는 대소문자
    //     무시라 'cloudflared' 로 조회 가능(2026-07-15 바이너리 실측). demo PC 의 'CloudflaredAgent' 는
    //     폐기된 옛 수동 스크립트 잔재로 본 경로와 무관.
    ResultCode := ExecLogged('6-3-scquery', 'sc query cloudflared');
    if ResultCode <> 0 then begin
      Log('[20260714작1 W1-3] cloudflared 서비스 미존재 — service install 1회 재시도');
      // ★ 20260803작1 W-1: 재시도 앞에도 잔재 정리를 넣는다.
      //   8/03 실측에서 재시도(6-3-retry-serviceinstall)도 **동일하게 코드 1** 로 실패했다.
      //   잔재를 안 지우면 몇 번을 다시 해도 같은 이유로 거부당한다(= 재시도가 무의미했다).
      ExecLogged('6-3-retry-svcuninstall',
        '"' + ExpandConstant('{app}\cloudflared.exe') + '" service uninstall');
      Sleep(6000);
      // 작10 P0-5: 재시도의 출력이 진단의 핵심이다. 1차와 2차가 같은 오류를 내면
      //   원인이 일시적 경합이 아니라 구조적(권한·정책·바이너리)이라는 뜻이다.
      ExecLogged('6-3-retry-serviceinstall',
        '"' + ExpandConstant('{app}\cloudflared.exe') + '" service install ' + G_TunnelToken);
      Sleep(3000);
      ExecLogged('6-3-retry-scstart', 'sc start cloudflared');
      ResultCode := ExecLogged('6-3-retry-scquery', 'sc query cloudflared');
      if ResultCode <> 0 then
        WarnTunnelFailure('터널 서비스 설치(service install) 재시도 실패', ResultCode);
    end;

    // ★ 봉합 20260730작8 P0-1 / N-2 (사장님 결재): "실행 중"까지 확인해야 봉합이다.
    //   위 6-3 은 sc start 를 '호출'하지만 실제로 RUNNING 이 됐는지는 아무도 안 봤다.
    //   그래서 서비스가 죽은 채로 설치가 "완료"로 끝났다(침묵 고장 — 헌법 #15 위반).
    //   sc query 의 종료코드는 '서비스 존재'만 알려주고 상태는 알려주지 않으므로,
    //   `sc query … | find "RUNNING"` 으로 상태 문자열을 직접 확인한다(find 는 미발견 시 exit 1).
    //   시작에 시간이 걸릴 수 있어(START_PENDING) 2초 간격 5회까지 기다린다.
    //   비차단(경고)로 둔 이유: 여기서 설치를 되돌리면 이미 등록된 DB·서비스가 고아가 된다.
    //   대신 고객이 반드시 알도록 가시화한다 — 조용한 성공만은 만들지 않는다.
    //   ※ while 로 쓴 이유: Inno Setup Pascal Script 의 for 루프 안 Break 지원이 판본별로
    //     불확실하다. 이 파일에 기존 사용례가 0건이라 실증할 수 없어, 확실히 동작하는
    //     조건 루프로 쓴다(컴파일 실패 = 전 고객 설치 불가라 모험하지 않는다).
    TunnelRunning := False;
    TunnelTry := 0;
    while (TunnelTry < 5) and (not TunnelRunning) do
    begin
      TunnelTry := TunnelTry + 1;
      Exec(ExpandConstant('{cmd}'), '/C sc query cloudflared | find "RUNNING"',
           '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      if ResultCode = 0 then
        TunnelRunning := True
      else
        Sleep(2000);
    end;
    if TunnelRunning then
      Log('[20260730작8 P0-1] cloudflared RUNNING 확인 (시도 ' + IntToStr(TunnelTry) + '회)');
    // ★ 봉합 20260730작8 P0-3 자동복구 (사장님 지시: "재설치 할때 동작할수 있게 만들어놔")
    //   ■ 왜 필요한가 — 재설치의 가장 흔한 실패가 '죽은 토큰'이다
    //     본사에서 터널을 삭제·재발급한 뒤 재설치하면 db.conf 의 옛 토큰(W1-2 보존분)은 무효다.
    //     cloudflared 는 무효 토큰에 오류를 내지 않고 **정상 종료(exit 0)** 한다 → 조용히 1033.
    //     사람이 sc start 를 쳐도 다시 죽는다(토큰이 무효라 근본이 안 풀림).
    //   ■ 무엇을 하나
    //     RUNNING 실패 = '토큰이 죽었을 가능성'이 가장 크다. 시리얼이 있으면 백오피스에
    //     **새 토큰을 1회 다시 요청**해 service install 을 재실행한다. 이게 되면 고객 손 0번으로
    //     재설치가 완주한다(헌법 #28·#30 자가회복 · #20 흐름 불단절).
    //   ■ 1회만 시도하는 이유
    //     무한 재시도는 설치를 멈추게 한다. 1회로 안 되면 원인이 토큰이 아니라 백신·방화벽 쪽이므로
    //     아래 경고로 넘긴다(추측 재시도 금지 — 두더지잡기 방지).
    if (not TunnelRunning) and (G_LicenseKey <> '') then
    begin
      Log('[20260730작8 P0-3] RUNNING 실패 — 죽은 토큰 의심. 백오피스에 새 토큰 1회 재요청.');
      if CallBootstrapApi(G_LicenseKey) and (G_TunnelToken <> '') then
      begin
        Log('[20260730작8 P0-3] 새 토큰 수령 — service install 재실행');
        // db.conf 를 새 토큰·터널ID 로 갱신한다 — 워치독(WS-28-D)이 다음에 죽은 토큰을 쓰지 않도록.
        //   db.conf 저장이 6단계보다 앞이라 이 갱신이 없으면 자가복구가 영구 무력해진다.
        UpdateDbConfValue('TUNNEL_TOKEN', G_TunnelToken);
        if G_TunnelId <> '' then
          UpdateDbConfValue('TUNNEL_ID', G_TunnelId);
        // 작10 P0-5: 자동복구 경로도 가시화한다. 여기까지 왔다는 건 이미 1차가 실패한 상황이므로
        //   출력이 없으면 "복구를 시도했는데 왜 안 됐는지"를 영영 알 수 없다.
        // 검증팀 반려 봉합 (P0-1·P0-2 동일 결함) — '&' 체인 분해 + timeout → Sleep
        ExecLogged('P0-3-scstop', 'sc stop cloudflared');
        Sleep(3000);
        ExecLogged('P0-3-scdelete', 'sc delete cloudflared');
        Sleep(2000);
        // ★ 20260803작1 W-1: 자동복구 경로도 sc delete 만으로는 cloudflared 잔재가 안 지워진다.
        //   여기서 막히면 '새 토큰을 받아왔는데 설치가 거부되는' 침묵 실패가 된다.
        ExecLogged('P0-3-svcuninstall',
          '"' + ExpandConstant('{app}\cloudflared.exe') + '" service uninstall');
        Sleep(6000);
        ExecLogged('P0-3-serviceinstall',
          '"' + ExpandConstant('{app}\cloudflared.exe') + '" service install ' + G_TunnelToken);
        Sleep(3000);
        ExecLogged('P0-3-scconfig', 'sc config cloudflared start= auto');
        ExecLogged('P0-3-scstart', 'sc start cloudflared');
        // 재확인 — 여기서도 안 되면 토큰 문제가 아니다.
        TunnelTry := 0;
        while (TunnelTry < 5) and (not TunnelRunning) do
        begin
          TunnelTry := TunnelTry + 1;
          Exec(ExpandConstant('{cmd}'), '/C sc query cloudflared | find "RUNNING"',
               '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
          if ResultCode = 0 then
            TunnelRunning := True
          else
            Sleep(2000);
        end;
        if TunnelRunning then
          Log('[20260730작8 P0-3] ✅ 자동복구 성공 — 새 토큰으로 RUNNING (재설치 완주)')
        else
          Log('[20260730작8 P0-3] 자동복구 후에도 RUNNING 실패 — 토큰 외 원인(백신·방화벽) 의심');
      end
      else
        Log('[20260730작8 P0-3] 새 토큰 재요청 실패 — 본사 통신 또는 시리얼 문제');
    end;

    if not TunnelRunning then
    begin
      Log('[20260730작8 P0-1] cloudflared 가 RUNNING 이 아니다 — 외부 접속(1033) 발생');
      WarnTunnelFailure('터널 연결 프로그램이 실행되지 않았습니다(백신 차단 가능성)', -2);
    end;

    // 6-4. HITPAN_SUBDOMAIN 영역 db.conf 영역 박음 (사고 #41·#42·#39 봉합 — 환경변수 폐기)
    //   사장님 결재 2026-06-12 — 싱글 각각 설치 영역 = 회사별 db.conf 영역만 박힘
    //   환경변수 영역 = 글로벌 영역 영역 슬롯 영역 덮어쓰기 영역 사고 차단 → db.conf 영역 박음
    //   ERP 본체 영역 TenantConfigReader 영역 db.conf 영역 직접 읽음 (사고 #46 봉합 정합)
  end
  else begin
    // ★ 봉합 20260714작1 (W1-1): 터널 토큰이 없으면(이번 발급도, 기존 db.conf 보존값도 없음) 종전엔
    //   아무 표시 없이 건너뛰어 "설치 성공인데 외부 접속 1033" 침묵 고장이 됐다. 가시화(비차단).
    if G_TenantCode <> 'LOCAL' then
      WarnTunnelFailure('터널 토큰 미수령 — 외부 접속 미구성', -1);
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
  // ★ 봉합 (2026-07-14, Sandbox 실측 적발): 결과코드 미검사로 서비스 미등록이 침묵 통과되던 것을
  //   W2-2 패턴(Log + 경고 MsgBox 1회, 비차단)으로 가시화. InstallWatchdog.ps1 도 EXE 부재 시
  //   exit 1 로 승격(종전 exit 0 = 침묵 성공).
  if FileExists(ExpandConstant('{app}\scripts\InstallWatchdog.ps1')) then begin
    Exec('powershell.exe',
         '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\scripts\InstallWatchdog.ps1') + '" -InstallPath "' + ExpandConstant('{app}') + '"',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // ★ 봉합 20260804작1: 종료코드 3분화에 맞춰 안내를 가른다.
    //   InstallWatchdog.ps1 규약 — 0=완전성공 / 1=서비스 자체 없음(치명) / 2=기동만 실패(자가복구 가능)
    //   종전엔 <>0 하나로 뭉뚱그려 **exit 1 인데도 "재부팅하면 시작됩니다"** 라고 안내했다.
    //   서비스가 아예 안 만들어진 경우엔 재부팅해도 안 뜬다 — 고객에게 틀린 안내였다(헌법 #24 가르침 의무).
    if ResultCode = 2 then begin
      // 서비스·Guardian 은 만들어졌고 기동만 실패 → 5분 뒤 Guardian, 재부팅 시 start=auto.
      //   설치를 막지 않으며, 사실인 안내만 한다.
      Log('[20260804작1] 워치독 기동만 실패(exit=2) — Guardian/재부팅으로 자가복구 예정. 설치 계속.');
      if not G_StartupWarnShown then begin
        G_StartupWarnShown := True;
        MsgBox('히트판 자동 관리 프로그램이 아직 시작되지 않았습니다.' + #13#10 +
               '잠시 후 자동으로 시작되며, 그래도 안 되면 PC를 다시 켜면 시작됩니다.',
               mbInformation, MB_OK);
      end;
    end
    else if ResultCode = 3 then begin
      // ★ 봉합 20260804작1 P0-2 ([4] 검증팀 적발): exit 3 = 미처리 예외.
      //   이 경우 **서비스가 만들어졌는지 여부 자체를 모른다.** 그런데 종전엔 아래
      //   WarnStartupRegistrationFailure 로 흡수돼 "PC를 재부팅하면 자동으로 시작됩니다" 라고
      //   **단언**했다. 서비스가 없으면 재부팅해도 안 뜬다 — 고객은 재부팅하고 여전히 안 되는데
      //   왜인지 모른다(침묵 고장 + 틀린 안내, 헌법 #24 가르침 의무 위반).
      //   ⇒ 모르는 것을 아는 척하지 않는다. 사실만 말하고 고객센터로 연결한다.
      Log('[20260804작1] 워치독 등록 중 예상 못 한 오류(exit=3) — 서비스 상태 불명. 설치는 계속.');
      if not G_StartupWarnShown then begin
        G_StartupWarnShown := True;
        MsgBox('히트판 자동 관리 프로그램 설정을 마치지 못했습니다.' + #13#10 +
               '히트판 사용에는 지장이 없지만, 자동 업데이트가 동작하지 않을 수 있습니다.' + #13#10 +
               '고객센터에 문의해 주세요.',
               mbError, MB_OK);
      end;
    end
    else if ResultCode <> 0 then
      WarnStartupRegistrationFailure('워치독 서비스 등록(InstallWatchdog.ps1)', ResultCode);
  end;

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
  // ★ 봉합 20260707작2 (W2-2): /Create 결과 검사 — 실패 시 Log + 경고 MsgBox 가시화(비차단).
  if ResultCode <> 0 then
    WarnStartupRegistrationFailure('API schtasks /Create', ResultCode);
  // 보강 2026-06-17 (1.2.12, P1 #5): schtasks /Run 결과 검사 + 실패 시 1회 재시도
  Exec(ExpandConstant('{cmd}'), '/C schtasks /Run /TN "HitPan-ERP-API-tenant-' + IntToStr(G_SlotIndex) + '"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if ResultCode <> 0 then begin
    Log('[1.2.12 P1#5] schtasks /Run 실패 ResultCode=' + IntToStr(ResultCode) + ', 5초 대기 후 1회 재시도');
    Sleep(5000);
    Exec(ExpandConstant('{cmd}'), '/C schtasks /Run /TN "HitPan-ERP-API-tenant-' + IntToStr(G_SlotIndex) + '"',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    if ResultCode <> 0 then
      // ★ 봉합 20260707작2 (W2-2): 재시도 최종 실패를 Log 만 남기지 않고 경고 MsgBox 가시화(비차단).
      //   (WarnStartupRegistrationFailure 가 Log 도 함께 남긴다.)
      WarnStartupRegistrationFailure('API schtasks /Run(재시도)', ResultCode);
  end;

  // 9-B. ERP API keepalive (봉합 2026-06-25, C — OS 레벨 2중 안전망):
  //   종전엔 ONSTART(부팅 시 1회) 작업뿐이라, ERP 가 떠 있다 죽으면(예외·메모리·업데이트) OS 가
  //   자동 재실행하지 않았다(2026-06-25 demo 502 사고). 워치독(A)이 1분 주기로 살리지만, 워치독
  //   자체가 죽은 경우를 대비해 OS 작업 스케줄러에도 독립 keepalive 를 둔다.
  //   동작: 1분마다 같은 ONSTART 작업을 'schtasks /Run' 한다. schtasks 는 작업당 단일 인스턴스가
  //   기본이라 ERP 가 살아있으면 새 인스턴스를 띄우지 않아 무해하고, 죽었으면 다시 띄운다(부활).
  //   → 워치독 ON: A 가 먼저(최대 1분) 살림 / 워치독 OFF: keepalive 가 (최대 1분) 살림 = 단일점 고장 0.
  Exec(ExpandConstant('{cmd}'),
       '/C schtasks /Create /F /TN "HitPan-ERP-API-keepalive-' + IntToStr(G_SlotIndex) + '"' +
       ' /TR "schtasks /Run /TN \"HitPan-ERP-API-tenant-' + IntToStr(G_SlotIndex) + '\""' +
       ' /SC MINUTE /MO 1 /RU SYSTEM /RL HIGHEST',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if ResultCode <> 0 then
    Log('[2026-06-25 C] ERP API keepalive 작업 생성 실패 ResultCode=' + IntToStr(ResultCode))
  else
    Log('[2026-06-25 C] ERP API keepalive 작업 생성(1분 주기, OS 레벨 2중 안전망)');

  // 9-C. ERP Web 정적서버(5234) 자동 시작 (봉합 2026-06-25, 배포 전수조사 P0-1):
  //   진범: 종전엔 web-server.ps1 을 [Files]로 복사만 하고(자동시작·감시 0), ERP API(5257)만 schtasks
  //   자동시작했다. demo.hitpan.kr 은 5234(Web)를 보므로 Web 이 안 뜨면 502 — 2026-06-25 사고의 절반이
  //   이것이다(워치독도 API 만 감시해 Web 죽으면 부활 0). API 와 동일하게 ONSTART SYSTEM 작업으로 등록한다.
  //   Web 은 정적 서빙 + /api 프록시(web-server.ps1 이 db.conf API_PORT 로 프록시 포트 자동 결정).
  Exec(ExpandConstant('{cmd}'),
       '/C schtasks /Create /F /TN "HitPan-ERP-WEB-tenant-' + IntToStr(G_SlotIndex) + '"' +
       ' /TR "powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"' + ExpandConstant('{app}\web-server.ps1') + '\""' +
       ' /SC ONSTART /RU SYSTEM /RL HIGHEST',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if ResultCode <> 0 then
    Log('[2026-06-25 P0-1] ERP Web 작업 생성 실패 ResultCode=' + IntToStr(ResultCode))
  else
    Log('[2026-06-25 P0-1] ERP Web(5234) 작업 생성(ONSTART)');
  // 즉시 1회 실행 — 설치 직후 5234 가 떠야 헬스체크·브라우저 열기가 성공한다.
  Exec(ExpandConstant('{cmd}'), '/C schtasks /Run /TN "HitPan-ERP-WEB-tenant-' + IntToStr(G_SlotIndex) + '"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // 9-D. ERP Web keepalive (봉합 2026-06-25, C — OS 레벨 2중 안전망, API 와 대칭):
  //   1분마다 Web ONSTART 작업을 schtasks /Run. 살아있으면 단일 인스턴스라 무해, 죽었으면 부활.
  Exec(ExpandConstant('{cmd}'),
       '/C schtasks /Create /F /TN "HitPan-ERP-WEB-keepalive-' + IntToStr(G_SlotIndex) + '"' +
       ' /TR "schtasks /Run /TN \"HitPan-ERP-WEB-tenant-' + IntToStr(G_SlotIndex) + '\""' +
       ' /SC MINUTE /MO 1 /RU SYSTEM /RL HIGHEST',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if ResultCode <> 0 then
    Log('[2026-06-25 C] ERP Web keepalive 작업 생성 실패 ResultCode=' + IntToStr(ResultCode))
  else
    Log('[2026-06-25 C] ERP Web keepalive 작업 생성(1분 주기)');

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
      // ★ 봉합 20260707작2 (W2-3): 폴백으로 여는 localhost 화면에서 로그인이 안 될 수 있음을 명시.
      //   종전 문구는 터널 지연만 안내해, 고객이 열린 localhost 화면에서 로그인 실패를 반복하며
      //   "설치가 잘못됐다"고 오판하는 함정이 있었다(워치독 자동 복구 = 헌법 #28·#30 정합 안내).
      MsgBox('히트판 ERP 설치가 완료되었으나, 외부 터널 활성화에 시간이 필요합니다.' + #13#10 + #13#10 +
             '잠시 후 열리는 임시 화면(localhost)에서는 로그인이 안 될 수 있습니다.' + #13#10 +
             '워치독이 자동 복구를 마치면(보통 5~10분) 아래 정식 주소로 접속해 주세요:' + #13#10 +
             'https://' + G_PrimaryDomain + #13#10 + #13#10 +
             '10분 후에도 접속이 안 되면 본사 고객센터에 문의해주세요.',
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
  // ★ 봉합 20260731: sc query 로 서비스 존재를 먼저 판정한다(6-1 과 동일 원칙).
  //   taskkill /F /IM 은 Sandbox 에서 프로세스 이미지 열거가 막혀 무한 hang 한다(7/21 실측).
  //   여기는 제거(uninstall) 경로라 hang 하면 **설치 마법사가 안 닫힌다.**
  //   서비스가 없으면 프로세스도 없으므로 taskkill 자체를 건너뛴다.
  //   ※ 판정은 `<> 0` (검증팀 P0-1 반려 반영). sc 의 종료코드는 1060 이 아니라 36 이다
  //     (1060 은 출력 본문 메시지). 코드값을 추측하지 않고 "정상 조회(0)" 만 참으로 본다.
  Exec(ExpandConstant('{cmd}'), '/C sc query cloudflared', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if ResultCode <> 0 then
    Log('[제거] cloudflared 서비스 미등록/조회불가(종료코드 ' + IntToStr(ResultCode) +
        ') — sc/taskkill 건너뜀(Sandbox hang 차단 · 20260731v2 봉합)')
  else
  begin
    Exec(ExpandConstant('{cmd}'),
         '/C sc stop cloudflared & timeout /t 2 /nobreak & sc delete cloudflared',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{cmd}'),
         '/C taskkill /F /IM cloudflared.exe',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Log('[제거] cloudflared 서비스 정리 완료(종료코드 ' + IntToStr(ResultCode) + ')');
  end;

  // schtasks 영역 부분 영역 박힌 영역 제거 (모든 슬롯 영역)
  //   봉합 2026-06-25 (C): keepalive 작업(1~5)도 함께 제거 — 잔존 시 삭제된 ONSTART 작업을
  //   1분마다 /Run 시도하는 고아 작업이 남는다(로그 노이즈).
  Exec(ExpandConstant('{cmd}'),
       '/C schtasks /Delete /F /TN "HitPan-ERP-API-tenant-1" & ' +
       'schtasks /Delete /F /TN "HitPan-ERP-API-tenant-2" & ' +
       'schtasks /Delete /F /TN "HitPan-ERP-API-tenant-3" & ' +
       'schtasks /Delete /F /TN "HitPan-ERP-API-tenant-4" & ' +
       'schtasks /Delete /F /TN "HitPan-ERP-API-tenant-5" & ' +
       'schtasks /Delete /F /TN "HitPan-ERP-API-keepalive-1" & ' +
       'schtasks /Delete /F /TN "HitPan-ERP-API-keepalive-2" & ' +
       'schtasks /Delete /F /TN "HitPan-ERP-API-keepalive-3" & ' +
       'schtasks /Delete /F /TN "HitPan-ERP-API-keepalive-4" & ' +
       'schtasks /Delete /F /TN "HitPan-ERP-API-keepalive-5" & ' +
       'schtasks /Delete /F /TN "HitPan-ERP-WEB-tenant-1" & ' +
       'schtasks /Delete /F /TN "HitPan-ERP-WEB-tenant-2" & ' +
       'schtasks /Delete /F /TN "HitPan-ERP-WEB-tenant-3" & ' +
       'schtasks /Delete /F /TN "HitPan-ERP-WEB-tenant-4" & ' +
       'schtasks /Delete /F /TN "HitPan-ERP-WEB-tenant-5" & ' +
       'schtasks /Delete /F /TN "HitPan-ERP-WEB-keepalive-1" & ' +
       'schtasks /Delete /F /TN "HitPan-ERP-WEB-keepalive-2" & ' +
       'schtasks /Delete /F /TN "HitPan-ERP-WEB-keepalive-3" & ' +
       'schtasks /Delete /F /TN "HitPan-ERP-WEB-keepalive-4" & ' +
       'schtasks /Delete /F /TN "HitPan-ERP-WEB-keepalive-5"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // 환경변수 영역 정리 — 부분 영역 박힌 영역 제거
  Exec(ExpandConstant('{cmd}'),
       '/C reg delete "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v HITPAN_SUBDOMAIN /f & ' +
       'reg delete "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v DB_PASSWORD /f & ' +
       'reg delete "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v JWT_SECRET /f',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // db.conf 영역·bootstrap.conf 영역 잔재 영역 — Inno Setup 영역 [UninstallDelete] 영역 박힘
end;
