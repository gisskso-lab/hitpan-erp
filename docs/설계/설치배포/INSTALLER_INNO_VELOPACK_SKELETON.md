# Installer Hybrid (Inno Setup + Velopack) 골격

> 헌법 #31(백신 호환성)·#27(통신 무결성)·#29(인프라 사전 승인) 정합.
> 초기 설치 = Inno Setup. 자동 업데이트 = Velopack(또는 WS-28-D 워치독 통합).

---

## 0. 산출물 구조

```
installer/
├── HitPanSetup.iss                  # Inno Setup 메인 스크립트
├── scripts/
│   ├── AntivirusExceptions.ps1     # Defender·V3·알약·Naver 자동 예외
│   ├── FirewallRules.ps1           # UDP 7844 / TCP 3306·5257 허용
│   ├── InstallMariaDB.ps1          # MSI silent install
│   ├── InstallCloudflared.ps1      # cloudflared service install
│   └── InstallWatchdog.ps1         # HitPan.Watchdog service install
├── payload/
│   ├── mariadb-11.4.10-winx64.msi
│   ├── cloudflared.exe
│   ├── HitPan.API/                 # dotnet publish 결과
│   ├── HitPan.Web/
│   └── HitPan.Watchdog/
└── docs/
    ├── 백신_매뉴얼_Norton.pdf
    └── 백신_매뉴얼_McAfee.pdf
```

---

## 1. HitPanSetup.iss — 메인 스크립트

```ini
[Setup]
AppName=히트판 ERP
AppVersion=1.1.0
DefaultDirName={autopf}\HitPan
DefaultGroupName=히트판
OutputBaseFilename=HitPanSetup_v1.1.0
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern
SetupIconFile=assets\hitpan.ico
DisableWelcomePage=no
DisableProgramGroupPage=yes

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Files]
; Payload 일괄 복사
Source: "payload\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs
; PowerShell 스크립트
Source: "scripts\*.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
; 매뉴얼
Source: "docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion

[Run]
; STEP 1: 백신 5종 예외 등록 (격리 차단 — 헌법 #31)
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\AntivirusExceptions.ps1"" -InstallPath ""{app}"""; \
  StatusMsg: "백신 5종 자동 예외 등록 중..."; Flags: runhidden waituntilterminated

; STEP 2: 방화벽 규칙 추가
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\FirewallRules.ps1"""; \
  StatusMsg: "방화벽 규칙 추가 중..."; Flags: runhidden waituntilterminated

; STEP 3: MariaDB MSI silent install
Filename: "msiexec.exe"; \
  Parameters: "/i ""{app}\payload\mariadb-11.4.10-winx64.msi"" /qn SERVICENAME=MariaDB PASSWORD=Hitpan2025!"; \
  StatusMsg: "MariaDB 설치 중..."; Flags: waituntilterminated

; STEP 4: cloudflared service install
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\InstallCloudflared.ps1"" -LicenseKey ""{code:GetLicenseKey}"""; \
  StatusMsg: "통신 터널 설치 중..."; Flags: runhidden waituntilterminated

; STEP 5: HitPan.Watchdog service install
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\InstallWatchdog.ps1"""; \
  StatusMsg: "워치독 등록 중..."; Flags: runhidden waituntilterminated

; STEP 6: API · Web 시작
Filename: "sc.exe"; Parameters: "start HitPan.API"; Flags: runhidden
Filename: "sc.exe"; Parameters: "start HitPan.Web"; Flags: runhidden

; STEP 7: 통신 무결성 자가 점검 (5분 안 /health 200)
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\SelfCheck.ps1"""; \
  StatusMsg: "통신 무결성 확인 중..."; Flags: runhidden waituntilterminated

[UninstallRun]
Filename: "sc.exe"; Parameters: "stop HitPanWatchdog"; Flags: runhidden
Filename: "sc.exe"; Parameters: "delete HitPanWatchdog"; Flags: runhidden
Filename: "sc.exe"; Parameters: "stop cloudflared"; Flags: runhidden
Filename: "sc.exe"; Parameters: "delete cloudflared"; Flags: runhidden

[Code]
var
  LicenseKeyPage: TInputQueryWizardPage;

procedure InitializeWizard;
begin
  LicenseKeyPage := CreateInputQueryPage(wpWelcome,
    '라이선스 키 입력', '히트판 라이선스 키 한 개만 입력하시면 됩니다',
    '나머지는 자동으로 설정됩니다.');
  LicenseKeyPage.Add('라이선스 키:', False);
end;

function GetLicenseKey(Param: String): String;
begin
  Result := LicenseKeyPage.Values[0];
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = LicenseKeyPage.ID then
  begin
    if Length(LicenseKeyPage.Values[0]) < 16 then
    begin
      MsgBox('라이선스 키가 올바르지 않습니다.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;
```

---

## 2. AntivirusExceptions.ps1 — 4종 자동 예외 (헌법 #31)

```powershell
param([string]$InstallPath)

# Windows Defender
Add-MpPreference -ExclusionPath $InstallPath
Add-MpPreference -ExclusionProcess "$InstallPath\payload\cloudflared.exe"
Add-MpPreference -ExclusionProcess "$InstallPath\payload\HitPan.Watchdog\HitPan.Watchdog.exe"
Add-MpPreference -ExclusionProcess "C:\Program Files\MariaDB 11.4\bin\mysqld.exe"

# AhnLab V3 Lite — 레지스트리 기반 (관리자 권한)
$v3Key = "HKLM:\SOFTWARE\AhnLab\V3Lite\Exclusions"
if (Test-Path $v3Key) {
    New-ItemProperty -Path $v3Key -Name "HitPan" -Value $InstallPath -PropertyType String -Force
}

# ALYac (ESTsecurity)
$alyacKey = "HKLM:\SOFTWARE\ESTsoft\ALYac\Exclusions"
if (Test-Path $alyacKey) {
    New-ItemProperty -Path $alyacKey -Name "HitPan" -Value $InstallPath -PropertyType String -Force
}

# Naver Vaccine
$naverKey = "HKLM:\SOFTWARE\NAVER\Vaccine\Exclusions"
if (Test-Path $naverKey) {
    New-ItemProperty -Path $naverKey -Name "HitPan" -Value $InstallPath -PropertyType String -Force
}

# Norton·McAfee = 매뉴얼 안내 (docs PDF)
Write-EventLog -LogName Application -Source HitPanSetup `
    -EntryType Information -EventId 31001 `
    -Message "AV exceptions registered: Defender + V3 + ALYac + Naver. Norton·McAfee require manual."
```

---

## 3. FirewallRules.ps1

```powershell
New-NetFirewallRule -DisplayName "HitPan cloudflared (UDP 7844)" `
    -Direction Outbound -Protocol UDP -RemotePort 7844 -Action Allow -ErrorAction SilentlyContinue
New-NetFirewallRule -DisplayName "HitPan MariaDB (TCP 3306)" `
    -Direction Inbound -Protocol TCP -LocalPort 3306 -Action Allow -Profile Private -ErrorAction SilentlyContinue
New-NetFirewallRule -DisplayName "HitPan API (TCP 5257)" `
    -Direction Inbound -Protocol TCP -LocalPort 5257 -Action Allow -Profile Private -ErrorAction SilentlyContinue
New-NetFirewallRule -DisplayName "HitPan Web (TCP 5234)" `
    -Direction Inbound -Protocol TCP -LocalPort 5234 -Action Allow -Profile Private -ErrorAction SilentlyContinue
```

---

## 4. InstallCloudflared.ps1

```powershell
param([string]$LicenseKey)

# 본사 API에 라이선스 키 제출 → tunnel token 수령
$response = Invoke-RestMethod -Uri "https://api.hitpan.kr/provisioning/tunnel" `
    -Method Post -Body (@{license_key = $LicenseKey} | ConvertTo-Json) `
    -ContentType "application/json"

$tunnelId = $response.tunnel_id
$credJson = $response.credentials_json
$credDir = "$env:USERPROFILE\.cloudflared"
New-Item -ItemType Directory -Path $credDir -Force | Out-Null
$credJson | Out-File "$credDir\$tunnelId.json" -Encoding utf8 -NoNewline

# config.yml
@"
tunnel: $tunnelId
credentials-file: $credDir\$tunnelId.json
ingress:
  - hostname: $($response.subdomain).hitpan.kr
    service: http://localhost:5234
  - hostname: api-$($response.subdomain).hitpan.kr
    service: http://localhost:5257
  - service: http_status:404
"@ | Out-File "$credDir\config.yml" -Encoding utf8

# Service install
& "C:\Program Files\HitPan\payload\cloudflared.exe" service install
Start-Service cloudflared

# 환경변수 박제 (워치독이 읽음)
[Environment]::SetEnvironmentVariable("HITPAN_TUNNEL_ID", $tunnelId, "Machine")
[Environment]::SetEnvironmentVariable("HITPAN_TENANT_ID", $response.tenant_id, "Machine")
```

---

## 5. InstallWatchdog.ps1

```powershell
$exe = "C:\Program Files\HitPan\payload\HitPan.Watchdog\HitPan.Watchdog.exe"
sc.exe create HitPanWatchdog binPath= "`"$exe`"" start= auto DisplayName= "HitPan Watchdog"
sc.exe failure HitPanWatchdog reset= 60 actions= restart/5000/restart/5000/restart/60000
Start-Service HitPanWatchdog

# Guardian 작업 스케줄러 등록
Register-ScheduledTask -Xml (Get-Content "C:\Program Files\HitPan\scripts\HitPanWatchdogGuardian.xml" -Raw) `
    -TaskName "HitPanWatchdogGuardian" -Force
```

---

## 6. SelfCheck.ps1 — 설치 직후 통신 무결성 확인

```powershell
$attempts = 0
$max = 30  # 5분 (10초 × 30)
do {
    Start-Sleep -Seconds 10
    $attempts++
    $subdomain = (Get-Content "$env:USERPROFILE\.cloudflared\config.yml" | Select-String "hostname:" | Select-Object -First 1) -replace ".*hostname:\s*", ""
    try {
        $r = Invoke-WebRequest -Uri "https://$subdomain/health" -TimeoutSec 10 -UseBasicParsing
        if ($r.StatusCode -eq 200) {
            Write-EventLog -LogName Application -Source HitPanSetup `
                -EntryType Information -EventId 31002 `
                -Message "Self-check PASS after $attempts attempts"
            exit 0
        }
    } catch { }
} while ($attempts -lt $max)

# 5분 안 통신 안 됨 → 본사 알림
Invoke-RestMethod -Uri "https://api.hitpan.kr/install/failure" -Method Post `
    -Body (@{reason = "self_check_timeout"} | ConvertTo-Json) -ContentType "application/json"
Write-EventLog -LogName Application -Source HitPanSetup `
    -EntryType Error -EventId 31003 -Message "Self-check FAILED — HQ notified"
exit 1
```

---

## 7. Velopack 자동 업데이트 (선택, Phase 2)

Phase 1은 WS-28-D 워치독 자체 재설치만으로 충분.
Phase 2(정식 출시 후)에 Velopack delta 패키지로 EXE 자동 갱신 검토.

```csharp
// Velopack 통합 (HitPan.Watchdog 내부)
var mgr = new UpdateManager("https://updates.hitpan.kr");
var newVersion = await mgr.CheckForUpdatesAsync();
if (newVersion != null)
{
    await mgr.DownloadUpdatesAsync(newVersion);
    mgr.ApplyUpdatesAndRestart(newVersion);
}
```

---

## 8. 검증 게이트

| 단계 | PASS 기준 |
|---|---|
| 라이선스 키 입력 | 16자 이상 |
| 백신 5종 | 격리 0건 |
| 방화벽 4 규칙 | netsh advfirewall show rule = 4건 |
| MariaDB | `mysql --version` 응답 |
| cloudflared | `Get-Service cloudflared` Running |
| 워치독 | `Get-Service HitPanWatchdog` Running |
| API·Web | localhost:5257·5234 응답 |
| 외부 헬스 | `https://{subdomain}.hitpan.kr/health` 200 (5분 안) |

8/8 PASS만 설치 완료. 1건이라도 실패 = 본사 자동 알림 + 사용자 매뉴얼 안내.

---

**문서 끝.** 다음: 시나리오 20 검증 스크립트 + 본사 메타 ping 스키마.
