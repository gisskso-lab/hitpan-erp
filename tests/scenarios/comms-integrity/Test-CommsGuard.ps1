#Requires -Version 5.1
<#
  Test-CommsGuard.ps1 — W-G1-53 ㉣ CI 시험(가짜 입력만 · 진짜 끊기 0 · 외부 모듈 없이)

  근거(주소만):
    설계       docs/설계/설치배포/20261118_WG1-53_장애시험도구재설계_설계.md  §7(경우 C1~C22) · §5 · §5-1a · §5-3
    작업지시서 docs/운영기록/20261118작_WG1-53_장애시험도구재설계_작업지시서.md  §2 ㉣
    개발명세서 docs/개발/설치배포/20261118_개발명세서_WG1-53_장애시험도구재설계.md  §0 다음 줄 2 · §7 새 창 구현 메모

  하는 일:
    가짜 세계(정수 ms 시계 · 서비스 · 프로세스 · HTTP · 이벤트)를 만들고, 새 러너(Run-RoutinePcScenarios.ps1)를
    -Hooks 로만 돌려 경우 C1~C22 의 기대값과 맞춰 본다. 실물 공급자(레지스트리 · 서비스 · 외부 HTTP · 이벤트 로그) 호출 0.
    G4a 는 임시 git 저장소(저장소 밖 임시 폴더)로 잰다 — 이 저장소에는 git 쓰기 0.

  인자:
    -ModulePath  판정 모듈 경로(기본 = 같은 폴더 CommsGuard.psm1) — 「가드 한 줄 깬 사본」 대조군용
    -RunnerPath  러너 경로(기본 = 같은 폴더 Run-RoutinePcScenarios.ps1)

  끝 줄: 「Test-CommsGuard 끝 — 경우 35/35 일치 · 세부 확인 k/k · 진짜 끊기 호출 0」 · 종료 코드 0(전부 일치 · 세부 확인 하한 이상) / 1.
    (11/19 [4] F8~F10) 경우 C33~C35 = [4] 작업리뷰서 변이 M02(G5 원점 /health) · M05(해석 9 2초 값) · M07(IPv6 ::1) 이 살아남던 줄 — 옛 끝 줄 「경우 32/32」.
    (11/19 [4] F1) 시험 시간 — 임시 저장소 = repo-ok 복사 재사용 · G4a git 읽기 = 같은 저장소 · 같은 인자면 첫 1회 결과 재사용(Copy-CgRepo · $script:GitCacheInstall).
    근거 docs/검증/20261119_WG1-53_갈래1_2_4_작업리뷰서.md §2 F1 · F8~F10 · 개발명세서 §4 · §7.
    (11/19 [3-V] 고침) 경우 C23~C27 = 보안상무 [3-V] A1~A5 입력 · 기대 「합격 아님」 — C1~C22 그대로 · 옛 끝 줄 「경우 22/22」.
    근거 docs/검증/병렬이슈/20261119_3V_WG1-53_갈래1_가드모듈_보안상무.md A 표.
    (11/19 [3-V] 갈래2 고침) 경우 C28~C32 = 보안상무 [3-V] 러너 · 시험 V1 · V2(+V6) · V3 · V4 · V9 — 옛 끝 줄 「경우 27/27」.
    V5: 경우 기본 = 불일치 · 실행된 경우만 · 세부 확인 수 하한. 근거 docs/검증/병렬이슈/20261119_3V_WG1-53_갈래2_러너시험_보안상무.md A 표.
  비밀 꼴 키 이름 · 노출 금지어는 실행 중에 이어 붙여 만든다(이 파일 바이트에 0).
  인코딩: UTF-8 BOM.
#>
param(
    [string]$ModulePath,
    [string]$RunnerPath
)

Set-StrictMode -Off

# ─────────────────────────────────────────────── 가짜 세계

class CgWorld {
    [datetime]$Base
    [long]$Ms
    [hashtable]$Cfg
    [hashtable]$Cut
    [hashtable]$CutCalls
    [hashtable]$InfoCalls
    [long]$LastStopMs
    [int]$FakeCuts
    [int]$ExtGets
    [int]$LocalGets
    [int]$HandCalls
    [int]$BadUrls
    [int]$NoCacheMissing
    [int]$ApiProcCalls
    [System.Collections.Generic.List[string]]$Nonces
    [bool]$Verbose

    CgWorld([datetime]$base, [hashtable]$cfg) {
        $this.Base = $base
        $this.Ms = 0
        $this.Cfg = $cfg
        $this.Cut = @{}
        $this.CutCalls = @{}
        $this.InfoCalls = @{}
        $this.LastStopMs = -1
        $this.Nonces = New-Object 'System.Collections.Generic.List[string]'
        $this.Verbose = $false
    }

    [datetime] Now() { return $this.Base.AddMilliseconds([double]$this.Ms) }
    [void] Sleep([int]$delta) {
        # (11/19 [3-V] 갈래2 V1 · C28) 관측 중 훅 예외 — 마지막 끊기(서비스 · 프로세스) 뒤 ObsThrowAt ms · 손 기록 뒤 HandThrowAt ms 지나면 잠 훅이 던짐
        if ([long]$this.Cfg.ObsThrowAt -ge 0 -and $this.LastStopMs -ge 0 -and ($this.Ms - $this.LastStopMs) -ge [long]$this.Cfg.ObsThrowAt) { throw 'fake observe failure' }
        if ([long]$this.Cfg.HandThrowAt -ge 0 -and $this.IsCut('hand') -and $this.Rel('hand') -ge [long]$this.Cfg.HandThrowAt) { throw 'fake observe failure' }
        if ($delta -gt 0) { $this.Ms += $delta }
    }
    [bool] IsCut([string]$k) { return $this.Cut.ContainsKey($k) }
    [long] Rel([string]$k) { return ($this.Ms - [long]$this.Cut[$k]) }
    [bool] InWin([object[]]$w, [long]$r) {
        if ($null -eq $w) { return $false }
        for ($i = 0; $i + 1 -lt $w.Count; $i += 2) {
            if ($r -ge [long]$w[$i] -and $r -lt [long]$w[$i + 1]) { return $true }
        }
        return $false
    }

    # ── 서비스(cloudflared · MariaDB) — 시각은 그 대상 끊기 시각 기준 ms
    [string] SvcStatus([string]$name) {
        $c = $this.Cfg.Svc[$name]
        if ($null -eq $c) { return 'Running' }
        if (-not $this.IsCut($name)) { return [string]$c.Pre }
        $r = $this.Rel($name)
        if ([long]$c.StopAt -lt 0) { return 'Running' }
        if ($r -lt [long]$c.StopAt) { return 'StopPending' }
        if ([long]$c.RunAt -ge 0 -and $r -ge [long]$c.RunAt) { return 'Running' }
        return 'Stopped'
    }
    [bool] SvcBack([string]$name) {
        $c = $this.Cfg.Svc[$name]
        if ($null -eq $c -or -not $this.IsCut($name)) { return $false }
        return ([long]$c.StopAt -ge 0 -and [long]$c.RunAt -ge 0 -and $this.Rel($name) -ge [long]$c.RunAt)
    }
    [int] SvcPid([string]$name) {
        $c = $this.Cfg.Svc[$name]
        if ($null -eq $c) { return 999 }
        if ($this.SvcStatus($name) -eq 'Stopped') { return 0 }
        if ($this.SvcBack($name)) { return [int]$c.PostPid }
        return [int]$c.PrePid
    }
    [string] SnapStatus([string]$name) {
        if ($this.Cfg.SnapSvc.ContainsKey($name)) { return [string]$this.Cfg.SnapSvc[$name] }
        return $this.SvcStatus($name)
    }
    [hashtable] ServiceInfo([string]$name) {
        $this.InfoCalls[$name] = 1 + [int]$this.InfoCalls[$name]
        return @{ Status = $this.SvcStatus($name); Pid = $this.SvcPid($name) }
    }
    [void] StopService([string]$name) {
        $this.FakeCuts++
        $this.CutCalls[$name] = 1 + [int]$this.CutCalls[$name]
        $this.LastStopMs = $this.Ms
        $c = $this.Cfg.Svc[$name]
        if ($null -ne $c -and [bool]$c.StopThrows) { throw 'fake stop failure' }
        $this.Cut[$name] = $this.Ms
    }

    # ── HitPan.API 프로세스(S-C)
    [bool] OldApi() {
        if (-not $this.IsCut('api')) { return $true }
        $g = [long]$this.Cfg.Api.OldGoneAt
        if ($g -lt 0) { return $true }
        return ($this.Rel('api') -lt $g)
    }
    [bool] NewApi() {
        if (-not $this.IsCut('api')) { return $false }
        $n = [long]$this.Cfg.Api.NewAt
        if ($n -lt 0) { return $false }
        return ($this.Rel('api') -ge $n)
    }
    [bool] ApiUp() { return ($this.OldApi() -or $this.NewApi()) }
    [int] OldPid() {
        $d = [int]$this.Cfg.Api.DbRestartPid
        if ($d -gt 0 -and $this.SvcBack('MariaDB')) { return $d }
        return [int]$this.Cfg.Api.OldPid
    }
    [int] ApiPid() {
        if ($this.NewApi()) { return [int]$this.Cfg.Api.NewPid }
        if ($this.OldApi()) { return $this.OldPid() }
        return 0
    }
    [object[]] ApiProcs() {
        $l = New-Object 'System.Collections.Generic.List[object]'
        $u = [bool]$this.Cfg.Api.UnderInstall
        # (11/19 [3-V] 갈래2 V3 · C30) 끊기 전 두 번째 조회(= 러너 끊기 직전 재확인)부터 번호 바뀜(pid) · 경로 바뀜(path)
        if (-not $this.IsCut('api')) {
            $this.ApiProcCalls++
            $mode = [string]$this.Cfg.Api.RecheckMode
            if ($this.ApiProcCalls -ge 2 -and $mode -eq 'pid') { $l.Add(@{ Pid = 5999; UnderInstall = $u }); return $l.ToArray() }
            if ($this.ApiProcCalls -ge 2 -and $mode -eq 'path') { $l.Add(@{ Pid = $this.OldPid(); UnderInstall = $false }); return $l.ToArray() }
        }
        if ($this.OldApi()) { $l.Add(@{ Pid = $this.OldPid(); UnderInstall = $u }) }
        if ($this.NewApi()) { $l.Add(@{ Pid = [int]$this.Cfg.Api.NewPid; UnderInstall = $u }) }
        return $l.ToArray()
    }
    [object] PortOwner() {
        $p = $this.ApiPid()
        if ($p -eq 0) { return $null }
        return @{ Pid = $p; UnderInstall = [bool]$this.Cfg.Api.UnderInstall }
    }
    [void] StopProcesses([int[]]$pids) {
        $this.FakeCuts++
        $this.CutCalls['api'] = 1 + [int]$this.CutCalls['api']
        $this.LastStopMs = $this.Ms
        if ([bool]$this.Cfg.Api.KillThrows) { throw 'fake kill failure' }
        $this.Cut['api'] = $this.Ms
    }

    # ── HTTP
    [hashtable] Origin() {
        if (-not $this.ApiUp()) { return @{ Status = 0; Body = $null } }
        $v = [string]$this.Cfg.Version
        $dbOk = ([bool]$this.Cfg.DbAlwaysOk -or $this.SvcStatus('MariaDB') -eq 'Running')
        if ($dbOk) { return @{ Status = 200; Body = ('{"status":"Healthy","checks":{"database":"ok","version":"' + $v + '"}}') } }
        return @{ Status = 503; Body = ('{"status":"Unhealthy","checks":{"database":"' + [string]$this.Cfg.DbErrText + '","version":"' + $v + '"}}') }
    }
    [hashtable] Ext() {
        $ok = ($this.SvcStatus('cloudflared') -eq 'Running' -and $this.ApiUp())
        $ver = [string]$this.Cfg.Version
        if ($this.IsCut('cloudflared')) {
            $r = $this.Rel('cloudflared')
            if ([bool]$this.Cfg.ExtAlwaysOk) { $ok = $true }
            if ($this.InWin($this.Cfg.ExtOkWin, $r)) { $ok = $true }
            if ($this.SvcBack('cloudflared')) {
                if ($this.Cfg.ExtAfterRun -eq 'fail') { $ok = $false }
                elseif ($this.Cfg.ExtAfterRun -eq 'other') { $ver = '0.0.0-edge' }
            }
        } elseif ([bool]$this.Cfg.ExtBaselineFail) { $ok = $false }
        if ($this.IsCut('hand') -and $this.InWin($this.Cfg.SdDown, $this.Rel('hand'))) { $ok = $false }
        if ($ok) { return @{ Status = 200; Body = ('{"version":"' + $ver + '"}') } }
        return @{ Status = 530; Body = '<html><body>error code: 1033</body></html>' }
    }
    [hashtable] Control([int]$timeoutSec) {
        if ($this.IsCut('hand')) {
            $r = $this.Rel('hand')
            if ($this.InWin($this.Cfg.CtlThrow, $r)) { throw 'fake control failure' }
            if ($this.InWin($this.Cfg.CtlTimeout, $r)) { $this.Ms += [long]$timeoutSec * 1000 + 1; return @{ Status = 0; Body = $null } }
            if ($this.InWin($this.Cfg.CtlFail, $r)) { return @{ Status = 0; Body = $null } }
        } elseif ([bool]$this.Cfg.CtlBaselineFail) { return @{ Status = 0; Body = $null } }
        return @{ Status = 200; Body = ("fl=1f1`nh=" + [string]$this.Cfg.Domain + "`nip=" + [string]$this.Cfg.ObserverIp + "`nts=1700000000.000`nvisit_scheme=https`n") }
    }
    [hashtable] HttpGet([string]$url, [int]$timeoutSec, [hashtable]$headers) {
        $this.Ms += [long]$this.Cfg.LatencyMs
        if ($url.StartsWith('http://127.0.0.1:')) {
            $this.LocalGets++
            if ($url -ne ('http://127.0.0.1:' + [string]$this.Cfg.Port + '/health')) { $this.BadUrls++ }
            # (11/19 [4] F8 · C33) 원점 /health 처음부터 실패 — '503'(DB 실패 본문 · 표지 글자) · 'timeout'(시간 초과 = Status 0 · 시계는 상한만큼)
            if ([string]$this.Cfg.OriginFail -eq 'timeout') { $this.Ms += [long]$timeoutSec * 1000 + 1; return @{ Status = 0; Body = $null } }
            if ([string]$this.Cfg.OriginFail -eq '503') { return @{ Status = 503; Body = ('{"status":"Unhealthy","checks":{"database":"' + [string]$this.Cfg.DbErrText + '","version":"' + [string]$this.Cfg.Version + '"}}') } }
            return $this.Origin()
        }
        $this.ExtGets++
        if ($null -eq $headers -or -not ([string]$headers['Cache-Control']).Contains('no-cache') -or [string]$headers['Pragma'] -ne 'no-cache') { $this.NoCacheMissing++ }
        $m = [regex]::Match($url, '\Ahttps://([^/?#]+)(/[^?#]*)\?cg=([0-9a-f]{32})\z')
        if (-not $m.Success) { $this.BadUrls++; $this.NoCacheMissing++; return @{ Status = 404; Body = $null } }
        if ($m.Groups[1].Value -ne [string]$this.Cfg.Domain) { $this.BadUrls++ }
        $this.Nonces.Add($m.Groups[3].Value)
        $path = $m.Groups[2].Value
        if ($path -eq '/api/app-version') { return $this.Ext() }
        if ($path -eq '/cdn-cgi/trace') { return $this.Control($timeoutSec) }
        $this.BadUrls++
        return @{ Status = 404; Body = $null }
    }
    [string[]] Resolve([string]$hostName) { return [string[]]$this.Cfg.Addrs }

    # ── 이벤트(워치독 · 서비스 제어 관리자) · 실패 동작 · 손 기록
    [void] AddEv([object]$items, [string]$svc, [string]$kind, [long]$atMs, [datetime]$since, [datetime]$now) {
        $t = $this.Base.AddMilliseconds([double]$atMs)
        if ($t -ge $since -and $t -le $now) { $items.Add(@{ TimeUtc = $t; Text = ('WS-28-I: service ' + $svc + ' ' + $kind + [string]$this.Cfg.EventJunk) }) }
    }
    [hashtable] WdEvents([datetime]$since) {
        if (-not [bool]$this.Cfg.EventsReadable) { return @{ Ok = $false; Items = @() } }
        $now = $this.Now()
        $items = New-Object 'System.Collections.Generic.List[object]'
        foreach ($e in @($this.Cfg.Events)) {
            if ($null -eq $e) { continue }
            $this.AddEv($items, [string]$e.Svc, [string]$e.Kind, [long]$e.AtMs, $since, $now)
        }
        foreach ($name in @('cloudflared', 'MariaDB')) {
            if (-not $this.IsCut($name)) { continue }
            $c = $this.Cfg.Svc[$name]
            $cutMs = [long]$this.Cut[$name]
            if ([long]$c.E1At -ge 0) { $this.AddEv($items, $name, 'restarted', $cutMs + [long]$c.E1At, $since, $now) }
            if ([long]$c.FailAt -ge 0) { $this.AddEv($items, $name, 'restart failure', $cutMs + [long]$c.FailAt, $since, $now) }
        }
        return @{ Ok = $true; Items = $items.ToArray() }
    }
    [hashtable] ScmEvents([string]$name, [datetime]$since) {
        if (-not [bool]$this.Cfg.ScmReadable) { return @{ Ok = $false; Items = @() } }
        $items = New-Object 'System.Collections.Generic.List[object]'
        $c = $this.Cfg.Svc[$name]
        if ($null -ne $c -and $this.IsCut($name) -and [long]$c.E2At -ge 0) {
            $t = $this.Base.AddMilliseconds([double]([long]$this.Cut[$name] + [long]$c.E2At))
            if ($t -ge $since -and $t -le $this.Now()) { $items.Add(@{ TimeUtc = $t }) }
        }
        return @{ Ok = $true; Items = $items.ToArray() }
    }
    [string] FailureActions([string]$name) { return [string]$this.Cfg.FailureText }
    [object] Hand() {
        $this.HandCalls++
        if ($this.Cfg.HandMode -eq 'none') { return $null }
        $this.Cut['hand'] = $this.Ms
        $who = if ([string]$this.Cfg.HandWho) { [string]$this.Cfg.HandWho } else { '시험자' }   # (갈래2 V9 · C32) 「누가」 칸 바꿔 넣기
        return @{ Who = $who; Utc = $this.Now() }
    }
}

# ─────────────────────────────────────────────── 준비

$sw = [System.Diagnostics.Stopwatch]::StartNew()
# (11/19 [3-V] 갈래2 V5) 경우 수 · 세부 확인 수 하한 — 경우 블록이 지워지거나 안 불리면 불일치 · 세부 확인이 줄면 종료 1
$script:CaseMax = 35      # (11/19 [4] F8~F10) 32 → 35(C33~C35)
$script:MinDetail = 561   # 이 커밋 기준 세부 확인 수 567 − C29 정션 확인 6(「미측정」 일 수 있어 뺌) · 옛 524(530 − 6) · 420 이상 — 늘리기만 한다
if (-not $ModulePath) { $ModulePath = Join-Path $PSScriptRoot 'CommsGuard.psm1' }
if (-not $RunnerPath) { $RunnerPath = Join-Path $PSScriptRoot 'Run-RoutinePcScenarios.ps1' }
$script:ModulePath = [System.IO.Path]::GetFullPath($ModulePath)
$script:RunnerPath = [System.IO.Path]::GetFullPath($RunnerPath)
foreach ($f in @($script:ModulePath, $script:RunnerPath)) {
    if (-not [System.IO.File]::Exists($f)) {
        Write-Host ('파일 없음: {0}' -f [System.IO.Path]::GetFileName($f))
        Write-Host ('Test-CommsGuard 끝 — 경우 0/{0} 일치 · 세부 확인 0/0 · 진짜 끊기 호출 0' -f $script:CaseMax)
        exit 1
    }
}
$global:HitPanCommsRealCutCalls = 0
Import-Module -Name $script:ModulePath -Force -ErrorAction Stop

$script:Base = New-Object datetime(2026, 11, 19, 1, 0, 0, [System.DateTimeKind]::Utc)
$rnd = [guid]::NewGuid().ToString('N')
$script:Mark = 'CGMARK' + $rnd.Substring(20, 10)
$script:Id = @{
    Domain  = 'pc1-' + $rnd.Substring(0, 8) + '.cg-test.invalid'
    Tenant  = 'TCG' + $rnd.Substring(8, 6).ToUpperInvariant()
    Guid    = [guid]::NewGuid().ToString()
    Port    = 15257
    Domain2 = 'pc9-' + $rnd.Substring(14, 6) + '.cg-test.invalid'
    Tenant2 = 'TCX' + $rnd.Substring(14, 6).ToUpperInvariant()
}
$script:ObserverIp = '198.51.100.' + (10 + ([int]('0x' + $rnd.Substring(30, 2)) % 200))
$script:DbErrText = 'dbfail-' + $script:Mark + 'DBERR'
$script:EventJunk = ' (C:\' + $script:Mark + 'EVPATH\run.exe --' + $script:Mark + 'EVCMD)'
$script:FailureText = "[SC] QueryServiceConfig2 SUCCESS`n`nSERVICE_NAME: x`n        RESET_PERIOD (in seconds)    : 0`n        REBOOT_MESSAGE               : `n" +
    "        COMMAND_LINE                 : C:\" + $script:Mark + "FAPATH\run.exe /" + $script:Mark + "FACMD`n        FAILURE_ACTIONS              : `n"
# 비밀 꼴 키 이름 6종(설치본 db.conf 동거 키 · 이름만) — 이어 붙여 만든다
$script:SecretKeys = @(
    ('DB_' + 'PASS' + 'WORD'), ('JWT_' + 'SEC' + 'RET'), ('ERP_ENCRYP' + 'TION_KEY'),
    ('LICEN' + 'SE_KEY'), ('TUN' + 'NEL_TO' + 'KEN'), ('HITPAN_BOOT' + 'STRAP_TO' + 'KEN_KEY')
)
# 외부 노출 금지어(헌법 #23) — 코드포인트로 만든다
$script:BannedRx = @(
    ('\b' + (-join [char[]](65, 73)) + '\b'),
    ('(?i)\b' + (-join [char[]](99, 108, 97, 117, 100, 101)) + '\b'),
    ('(?i)\b' + (-join [char[]](99, 117, 114, 115, 111, 114)) + '\b')
)
$script:CredRx = @(
    ('(?i)\b(' + 'pass' + '(word|wd)|pwd|' + 'sec' + 'ret|' + 'to' + 'ken|api_?key)\s*[:=]\s*\S'),
    ('ey' + 'J[A-Za-z0-9_-]{8,}'),
    '\b[0-9a-fA-F]{32,}\b',
    '[A-Za-z0-9+/]{40,}={0,2}'
)

$script:Root = Join-Path ([System.IO.Path]::GetTempPath()) ('cg-test-' + $rnd.Substring(0, 12))
$null = [System.IO.Directory]::CreateDirectory($script:Root)
$script:CaseSeq = 0
$script:AllText = New-Object System.Collections.Generic.List[string]
$script:CaseOk = [ordered]@{}
# (11/19 [3-V] 갈래2 V5) 기본 = 불일치. 일치 = 그 경우 블록이 실제로 돌았고(CaseRan) · 세부 확인 1건 이상(CaseDetail) · 어긋남 0(CaseBad)일 때만.
foreach ($n in 1..$script:CaseMax) { $script:CaseOk[('C{0}' -f $n)] = $false }
$script:CaseOk['부가'] = $false
$script:CaseRan = @{}
$script:CaseBad = @{}
$script:CaseDetail = @{}
$script:Unmeasured = New-Object System.Collections.Generic.List[string]
$script:DetailPass = 0
$script:DetailTotal = 0
$script:Fails = New-Object System.Collections.Generic.List[string]

$HdrPass = '합격(도구 범위 4 ID 중 4 · 자동 3 PASS + 반자동 1 완료)'
$Fixed4 = '손 1회 · #28 「고객 손 0번」 증명 아님 · 복구 주체 판정 아님 · #27 게이트 통과 아님'
$HandoffEnd = '시험 종료 아님 · 인계 필요(§5-4)'
$WarnLine = '⚠ 대상 상태 불명 · 사람 확인 필요'
$ActorMatch = '워치독 재시작 기록과 상태 전이 일치 · 단독 원인 귀속 미확정'
$LblA = '- S-A [외부 경로] '
$LblB = '- S-B [로컬 원점] '
$LblC = '- S-C [로컬 원점] '
$LblD = '- S-D [외부 경로 · 반자동(손 1회)] '

# ─────────────────────────────────────────────── 도우미

function Test-CgCase {
    param([string]$Case, [string]$What, [bool]$Ok)
    $script:DetailTotal++
    $script:CaseDetail[$Case] = 1 + [int]$script:CaseDetail[$Case]
    if (-not $script:CaseOk.Contains($Case)) { $Ok = $false; $What = '모르는 경우 이름 · ' + $What }   # (V5) 표에 없는 경우 이름 = 어긋남
    if ($Ok) { $script:DetailPass++ }
    else {
        $script:CaseBad[$Case] = $true
        $script:Fails.Add(('{0} · {1}' -f $Case, $What))
    }
}

function Update-CgCaseOk {
    # (V5) 일치 = 블록 실행 + 세부 확인 1건 이상 + 어긋남 0
    param([string]$Case)
    $script:CaseOk[$Case] = ([bool]$script:CaseRan[$Case] -and [int]$script:CaseDetail[$Case] -gt 0 -and -not [bool]$script:CaseBad[$Case])
}

function Invoke-CgCaseBlock {
    param([string]$Case, [string]$Title, [scriptblock]$Body)
    $before = $script:DetailTotal
    $script:CaseRan[$Case] = $true
    $t = [System.Diagnostics.Stopwatch]::StartNew()
    try { & $Body }
    catch { Test-CgCase $Case ('예외 ' + $_.Exception.GetType().Name + ' · ' + $_.Exception.Message) $false }
    if ($script:DetailTotal -eq $before) { Test-CgCase $Case '확인 0건' $false }
    Update-CgCaseOk $Case
    Write-Host ('{0} {1} · {2} ({3}초 · 세부 {4})' -f $Case, $(if ($script:CaseOk[$Case]) { '일치' } else { '불일치' }), $Title, [math]::Round($t.Elapsed.TotalSeconds, 1), ($script:DetailTotal - $before))
}

function Write-CgFile {
    param([string]$Path, [string]$Text)
    $null = [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($Path))
    [System.IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($false)))
}

function Remove-CgTree {
    param([string]$Dir)
    if (-not [System.IO.Directory]::Exists($Dir)) { return }
    try {
        foreach ($f in [System.IO.Directory]::GetFiles($Dir, '*', [System.IO.SearchOption]::AllDirectories)) { [System.IO.File]::SetAttributes($f, [System.IO.FileAttributes]::Normal) }
        [System.IO.Directory]::Delete($Dir, $true)
    } catch { Write-Warning ("임시 폴더 정리 실패({0})" -f $_.Exception.GetType().Name) }
}

function Invoke-CgGit {
    param([string]$Repo, [string[]]$GitArgs)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'git'
    $all = @('-C', $Repo, '-c', 'core.hooksPath=.cg-no-hooks', '-c', 'commit.gpgsign=false', '-c', 'user.name=cg', '-c', 'user.email=cg') + $GitArgs
    $psi.Arguments = (($all | ForEach-Object { '"' + ($_ -replace '"', '\"') + '"' }) -join ' ')
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $psi.EnvironmentVariables['GIT_TERMINAL_PROMPT'] = '0'
    $p = [System.Diagnostics.Process]::Start($psi)
    try {
        $errTask = $p.StandardError.ReadToEndAsync()
        $null = $p.StandardOutput.ReadToEnd()
        $p.WaitForExit()
        $null = $errTask.Result
        if ($p.ExitCode -ne 0) { throw ('임시 저장소 git 실패: {0}({1})' -f $GitArgs[0], $p.ExitCode) }
    } finally { $p.Dispose() }
}

function Get-CgAllowText {
    param([string]$Domain, [string]$Tenant, [string]$Nl = "`n")
    $d = Get-CgIdentityHash -Value $Domain
    $t = Get-CgIdentityHash -Value $Tenant
    return ('{' + $Nl + '  "entries": [' + $Nl + '    { "primaryDomainSha256": "' + $d + '", "tenantCodeSha256": "' + $t + '" }' + $Nl + '  ]' + $Nl + '}' + $Nl)
}

function New-CgRepo {
    # 임시 git 저장소(저장소 밖). 허용목록 커밋 → origin/main 참조(선택) → 뒤 커밋(선택) → 작업 사본 덮어쓰기(선택).
    param([string]$Name, [string]$MainText, [string]$HeadText, [string]$WorkText, [switch]$AutoCrlf, [switch]$NoOrigin)
    $repo = Join-Path $script:Root $Name
    $null = [System.IO.Directory]::CreateDirectory($repo)
    Invoke-CgGit -Repo $repo -GitArgs @('init', '-q')
    Invoke-CgGit -Repo $repo -GitArgs @('config', 'core.autocrlf', $(if ($AutoCrlf) { 'true' } else { 'false' }))
    $rel = 'tests/scenarios/comms-integrity/test-pc-allowlist.json'
    $abs = Join-Path $repo ($rel -replace '/', '\')
    Write-CgFile -Path $abs -Text $MainText
    Invoke-CgGit -Repo $repo -GitArgs @('add', '--', $rel)
    Invoke-CgGit -Repo $repo -GitArgs @('commit', '-q', '-m', 'main')
    if (-not $NoOrigin) { Invoke-CgGit -Repo $repo -GitArgs @('update-ref', 'refs/remotes/origin/main', 'HEAD') }
    if ($HeadText) {
        Write-CgFile -Path $abs -Text $HeadText
        Invoke-CgGit -Repo $repo -GitArgs @('add', '--', $rel)
        Invoke-CgGit -Repo $repo -GitArgs @('commit', '-q', '-m', 'local')
    }
    if ($WorkText) { Write-CgFile -Path $abs -Text $WorkText }
    return $repo
}

function Copy-CgRepo {
    # (11/19 [4] F1 · 시험 시간) 임시 저장소 재사용 — 다 만든 임시 저장소를 폴더째 복사(git 프로세스 0) · 부르는 쪽이 바꿀 칸만 고친다.
    #   옛: 저장소 5개를 New-CgRepo 로 따로 만듦(git 프로세스 26개 · 이 PC 5~6초). 지금: repo-ok 하나만 새로 · 나머지 = 복사 + git 0~3개.
    param([Parameter(Mandatory)][string]$From, [Parameter(Mandatory)][string]$Name)
    $to = Join-Path $script:Root $Name
    $null = [System.IO.Directory]::CreateDirectory($to)
    foreach ($d in [System.IO.Directory]::GetDirectories($From, '*', [System.IO.SearchOption]::AllDirectories)) { $null = [System.IO.Directory]::CreateDirectory($to + $d.Substring($From.Length)) }
    foreach ($f in [System.IO.Directory]::GetFiles($From, '*', [System.IO.SearchOption]::AllDirectories)) { [System.IO.File]::Copy($f, $to + $f.Substring($From.Length), $true) }
    return $to
}

# (11/19 [4] F1 · 시험 시간) G4a git 읽기 재사용 — 모듈 Get-CgAllowlistGitState(git 프로세스 3개 · 이 PC 러너 런 1회당 0.3~0.5초 · 런 60번 남짓)를
#   같은 (저장소 · 경로 · git 실행 파일) 이면 첫 1회 = 모듈 함수 그대로 실제 git · 그다음 = 그 결과의 사본. 임시 저장소는 준비 뒤 바뀌지 않음(C22 ⓕ 가 .git 무변을 잼).
#   러너가 런마다 Import-Module -Force 로 모듈을 새로 읽으므로 스냅숏 훅이 런마다 다시 씌운다(모듈 파일 · Get-EnvSnapshot · Get-GuardDecision 무변).
#   저장소가 다른 G4a 경우(C22 ⓐ ⓑ ⓒ · ⓕ)는 키가 달라 저마다 실제 git. 끝에 「G4a git 읽기: 실제 n · 재사용 m」 한 줄.
$script:GitCache = @{}
$script:GitCacheStat = @{ Real = 0; Reuse = 0 }
$script:GitCacheInstall = {
    param([hashtable]$Cache, [hashtable]$Stat)
    $mods = @(Get-Module -Name 'CommsGuard')
    if ($mods.Count -ne 1) { return }   # 못 찾으면 안 씌움(= 매번 실제 git · 느려질 뿐 판정 무변)
    & $mods[0] {
        param($Cache, $Stat)
        if ($script:CgTestGitReuse) { return }
        $orig = ${function:Get-CgAllowlistGitState}
        if ($null -eq $orig) { return }
        $wrap = {
            [CmdletBinding()]
            param([Parameter(Mandatory)][string]$RepoRoot, [Parameter(Mandatory)][string]$RelPath, [string]$GitExe = 'git')
            $k = $RepoRoot + '|' + $RelPath + '|' + $GitExe
            if (-not $Cache.ContainsKey($k)) {
                $Cache[$k] = & $orig -RepoRoot $RepoRoot -RelPath $RelPath -GitExe $GitExe
                $Stat['Real'] = 1 + [int]$Stat['Real']
            } else { $Stat['Reuse'] = 1 + [int]$Stat['Reuse'] }
            $copy = [ordered]@{}
            foreach ($kk in @($Cache[$k].Keys)) { $copy[$kk] = $Cache[$k][$kk] }
            return $copy
        }.GetNewClosure()
        ${function:script:Get-CgAllowlistGitState} = $wrap
        $script:CgTestGitReuse = $true
    } $Cache $Stat
}

function Get-CgTreeStamp {
    param([string]$Dir)
    $rows = foreach ($f in @([System.IO.Directory]::GetFiles($Dir, '*', [System.IO.SearchOption]::AllDirectories) | Sort-Object)) {
        $fi = New-Object System.IO.FileInfo($f)
        '{0}|{1}|{2}' -f $f.Substring($Dir.Length), $fi.Length, $fi.LastWriteTimeUtc.Ticks
    }
    return (@($rows) -join "`n")
}

function Get-CgSvcTiming {
    param([int]$StopAt, [int]$RunAt)
    $e1 = -1; $e2 = -1
    if ($RunAt -ge 0) { $e1 = $RunAt + 200; $e2 = $RunAt }
    return @{ StopAt = $StopAt; RunAt = $RunAt; E1At = $e1; E2At = $e2 }
}

function New-CgCfg {
    param([hashtable]$Over)
    $cfg = @{
        Domain = $script:Id.Domain; Port = $script:Id.Port; Version = '1.3.70'; LatencyMs = 20
        Addrs = @('203.0.113.10')
        Svc = @{
            cloudflared = @{ Pre = 'Running'; StopAt = 500; RunAt = 5500; PrePid = 4100; PostPid = 4200; StopThrows = $false; E1At = 5700; E2At = 5500; FailAt = -1 }
            MariaDB     = @{ Pre = 'Running'; StopAt = 400; RunAt = 4400; PrePid = 3100; PostPid = 3200; StopThrows = $false; E1At = 4600; E2At = 4400; FailAt = -1 }
        }
        Api = @{ OldPid = 5100; NewPid = 5200; OldGoneAt = 300; NewAt = 3300; UnderInstall = $true; KillThrows = $false; DbRestartPid = 0; RecheckMode = '' }
        SnapSvc = @{}; IsAdmin = $true; MachineGuid = $script:Id.Guid
        EventsReadable = $true; ScmReadable = $true; Events = @()
        FailureText = $script:FailureText; DbErrText = $script:DbErrText; EventJunk = $script:EventJunk; ObserverIp = $script:ObserverIp
        ExtAlwaysOk = $false; ExtOkWin = @(); ExtAfterRun = 'ok'; ExtBaselineFail = $false; DbAlwaysOk = $false
        SdDown = @(2500, 12500); CtlFail = @(); CtlTimeout = @(); CtlThrow = @(); CtlBaselineFail = $false
        HandMode = 'record'
        ObsThrowAt = -1; HandThrowAt = -1; HandWho = ''   # (11/19 [3-V] 갈래2 · C28 · C32) 기본 = 끔
        OriginFail = ''   # (11/19 [4] F8 · C33) 기본 = 끔
    }
    if ($null -ne $Over) {
        foreach ($k in $Over.Keys) {
            if ($k -eq 'cloudflared' -or $k -eq 'MariaDB') { foreach ($kk in $Over[$k].Keys) { $cfg.Svc[$k][$kk] = $Over[$k][$kk] } }
            elseif ($k -eq 'Api') { foreach ($kk in $Over[$k].Keys) { $cfg.Api[$kk] = $Over[$k][$kk] } }
            else { $cfg[$k] = $Over[$k] }
        }
    }
    return $cfg
}

function New-CgCaseEnv {
    # 가짜 PC 파일: 마커 · 설치본({app}\db.conf · {app}\watchdog\appsettings.json) — 전부 임시 폴더
    param([hashtable]$Opt)
    if ($null -eq $Opt) { $Opt = @{} }
    $script:CaseSeq++
    $dir = Join-Path $script:Root ('case-{0:D3}' -f $script:CaseSeq)
    $null = [System.IO.Directory]::CreateDirectory($dir)
    $markerPath = Join-Path $dir 'marker.txt'
    if ($Opt.Marker -ne 'none') {
        $mach = if ($Opt.ContainsKey('MarkerMachine')) { [string]$Opt.MarkerMachine } else { Get-CgIdentityHash -Value $script:Id.Guid }
        $exp = if ($Opt.ContainsKey('Expires')) { [string]$Opt.Expires } else { Format-CgUtc -Value $script:Base.AddHours(24) }
        Write-CgFile -Path $markerPath -Text ("# test marker`nENV=TEST`nMACHINE=" + $mach + "`nEXPIRES=" + $exp + "`n")
    }
    $installDir = $null
    if ($Opt.Install -ne 'none') {
        $installDir = Join-Path $dir 'app'
        $null = [System.IO.Directory]::CreateDirectory((Join-Path $installDir 'watchdog'))
        if ($Opt.DbConf -ne 'none') {
            $dom = if ($Opt.ContainsKey('Domain')) { [string]$Opt.Domain } else { $script:Id.Domain }
            $ten = if ($Opt.ContainsKey('Tenant')) { [string]$Opt.Tenant } else { $script:Id.Tenant }
            $lines = New-Object System.Collections.Generic.List[string]
            $lines.Add('# db.conf (test)')
            $lines.Add('PRIMARY_DOMAIN=' + $dom)
            if ($Opt.Secrets) { $lines.Add($script:SecretKeys[0] + '=' + $script:Mark + 'V1') }
            $lines.Add('TENANT_CODE=' + $ten)
            if ($Opt.Secrets) { $lines.Add($script:SecretKeys[1] + '=' + $script:Mark + 'V2') }
            $lines.Add('SLOT_INDEX=9')
            if ($Opt.Secrets) { $lines.Add($script:SecretKeys[2] + '=' + $script:Mark + 'V3') }
            # (11/19 [3-V] A5 · C27) ApiPort = 포트 글자 바꿔 넣기(전각 · 아랍 숫자) — 없으면 옛 값 그대로
            $lines.Add('API_PORT=' + $(if ($Opt.ContainsKey('ApiPort')) { [string]$Opt.ApiPort } else { [string]$script:Id.Port }))
            if ($Opt.Secrets) {
                $lines.Add($script:SecretKeys[3] + '=' + $script:Mark + 'V4')
                $lines.Add($script:SecretKeys[4] + '=' + $script:Mark + 'V5')
                $lines.Add($script:SecretKeys[5] + '=' + $script:Mark + 'V6')
                $lines.Add('this line is broken ' + $script:Mark + 'V7')
                $lines.Add('PRIMARY_DOMAIN_SHADOW=' + $script:Mark + 'V8')
            }
            Write-CgFile -Path (Join-Path $installDir 'db.conf') -Text (($lines -join "`r`n") + "`r`n")
        }
        $svcs = if ($Opt.ContainsKey('WdServices')) { @($Opt.WdServices) } else { @('MariaDB', 'cloudflared') }
        $json = '{"Watchdog":{"Processes":{"Services":[' + ((@($svcs) | ForEach-Object { '"' + $_ + '"' }) -join ',') + ']}}}'
        Write-CgFile -Path (Join-Path $installDir 'watchdog\appsettings.json') -Text $json
    }
    return @{ Dir = $dir; MarkerPath = $markerPath; InstallDir = $installDir; ReportDir = (Join-Path $dir 'reports') }
}

function New-CgProviders {
    param([Parameter(Mandatory)]$W, [Parameter(Mandatory)][hashtable]$E, [Parameter(Mandatory)][string]$Repo, [string]$GitExe = 'git')
    return @{
        MarkerPath       = $E.MarkerPath
        InstallDir       = $E.InstallDir
        RepoRoot         = $Repo
        GitExe           = $GitExe
        GetMachineGuid   = { $W.Cfg.MachineGuid }.GetNewClosure()
        GetServiceStatus = { param([string]$Name) $W.SnapStatus($Name) }.GetNewClosure()
        GetIsAdmin       = { [bool]$W.Cfg.IsAdmin }.GetNewClosure()
        HttpGet          = { param([string]$Url, [int]$TimeoutSec, [hashtable]$Headers) $W.HttpGet($Url, $TimeoutSec, $Headers) }.GetNewClosure()
    }
}

function New-CgHooks {
    param([Parameter(Mandatory)]$W, [Parameter(Mandatory)][hashtable]$Prov, [Parameter(Mandatory)][string]$ModPath)
    $gcI = $script:GitCacheInstall; $gcC = $script:GitCache; $gcS = $script:GitCacheStat   # (F1) G4a git 읽기 재사용 — 스냅숏 직전에 씌움
    return @{
        GetSnapshot        = { & $gcI $gcC $gcS; Get-EnvSnapshot -Providers $Prov -NowUtc $W.Now() -Verbose:$W.Verbose }.GetNewClosure()
        Now                = { $W.Now() }.GetNewClosure()
        Sleep              = { param([int]$Ms) $W.Sleep($Ms) }.GetNewClosure()
        HttpGet            = { param([string]$Url, [int]$TimeoutSec, [hashtable]$Headers) $W.HttpGet($Url, $TimeoutSec, $Headers) }.GetNewClosure()
        ResolveHost        = { param([string]$HostName) $W.Resolve($HostName) }.GetNewClosure()
        GetServiceInfo     = { param([string]$Name) $W.ServiceInfo($Name) }.GetNewClosure()
        GetApiProcesses    = { param([string]$InstallDir) $W.ApiProcs() }.GetNewClosure()
        GetPortOwner       = { param([int]$Port, [string]$InstallDir) $W.PortOwner() }.GetNewClosure()
        StopService        = { param([string]$Name) $W.StopService($Name) }.GetNewClosure()
        StopProcesses      = { param([int[]]$Pids) $W.StopProcesses($Pids) }.GetNewClosure()
        ReadWatchdogEvents = { param([datetime]$SinceUtc) $W.WdEvents($SinceUtc) }.GetNewClosure()
        ReadScmEvents      = { param([string]$Name, [datetime]$SinceUtc) $W.ScmEvents($Name, $SinceUtc) }.GetNewClosure()
        ReadFailureActions = { param([string]$Name) $W.FailureActions($Name) }.GetNewClosure()
        HandRecord         = { $W.Hand() }.GetNewClosure()
        ModulePath         = $ModPath
    }
}

function Invoke-CgRun {
    # 러너 1회(가짜 훅만). 화면(모든 흐름) · 보고서 · 실행 기록을 모아 돌려준다.
    # (11/19 [3-V] 갈래2) -Runner = 다른 러너 파일(V2 · V6 가짜 저장소 안 사본) · 러너가 예외로 빠져도 그때까지의 화면 줄을 잃지 않게 모은다(V1 대조군)
    param([hashtable]$Cfg, [hashtable]$Env, [switch]$NoConfirm, [switch]$Verbose, [string[]]$History,
        [string]$Repo, [string]$GitExe = 'git', [string]$ReportDir, [string[]]$DropHook, [string]$Runner)
    $W = [CgWorld]::new($script:Base, (New-CgCfg -Over $Cfg))
    $W.Verbose = [bool]$Verbose
    $e = New-CgCaseEnv -Opt $Env
    $repoRoot = if ($Repo) { $Repo } else { $script:RepoOk }
    $prov = New-CgProviders -W $W -E $e -Repo $repoRoot -GitExe $GitExe
    $hooks = New-CgHooks -W $W -Prov $prov -ModPath $script:ModulePath
    foreach ($d in @($DropHook)) { if ($d) { $hooks.Remove($d) } }
    $rd = if ($ReportDir) { $ReportDir } else { $e.ReportDir }
    if ($History) { Write-CgFile -Path (Join-Path $rd 'run-history.jsonl') -Text ((@($History) -join "`n") + "`n") }
    $p = @{ Hooks = $hooks; ReportDir = $rd }
    if (-not $NoConfirm) { $p.Confirm = $true }
    if ($Verbose) { $p.Verbose = $true }
    $global:LASTEXITCODE = 0
    $err = $null
    $acc = New-Object System.Collections.Generic.List[string]
    $rp = if ($Runner) { $Runner } else { $script:RunnerPath }
    try { & $rp @p *>&1 | ForEach-Object { $acc.Add([string]$_) } }
    catch { $err = $_ }
    $lines = $acc.ToArray()
    $exit = $LASTEXITCODE
    $reportText = $null
    $histText = $null
    if ([System.IO.Directory]::Exists($rd)) {
        $f = @([System.IO.Directory]::GetFiles($rd, 'routine-pc-*.txt'))
        if ($f.Count -gt 0) { $reportText = [System.IO.File]::ReadAllText($f[0], [System.Text.Encoding]::UTF8) }
        $hp = Join-Path $rd 'run-history.jsonl'
        if ([System.IO.File]::Exists($hp)) { $histText = [System.IO.File]::ReadAllText($hp, [System.Text.Encoding]::UTF8) }
    }
    $script:AllText.Add(($lines -join "`n"))
    if ($reportText) { $script:AllText.Add($reportText) }
    if ($histText) { $script:AllText.Add($histText) }
    $reportLines = @()
    if ($reportText) { $reportLines = @($reportText -split "`r?`n") }
    return [pscustomobject]@{
        Lines = $lines; Exit = $exit; Error = $err; W = $W; Env = $e; ReportDir = $rd
        ReportText = $reportText; ReportLines = $reportLines; HistText = $histText
    }
}

function Get-CgGuardFails {
    param($Lines)
    return @(@($Lines) | ForEach-Object { if ($_ -match '\A  (G\d+a?) 거절 · ') { $Matches[1] } })
}

function Get-CgHeaderIdx {
    param($Lines)
    $a = @($Lines)
    for ($i = $a.Count - 1; $i -ge 0; $i--) { if ([string]$a[$i] -match '\A(합격|미합격)') { return $i } }
    return -1
}

function Get-CgHeader {
    param($R)
    $i = Get-CgHeaderIdx $R.Lines
    if ($i -lt 0) { return '' }
    return [string]$R.Lines[$i]
}

function Get-CgItem {
    param($R, [string]$Id)
    $l = @(@($R.Lines) | Where-Object { ([string]$_).StartsWith('- ' + $Id + ' [') })
    if ($l.Count -eq 0) { return '' }
    return [string]$l[-1]
}

function Get-CgActorOf {
    param($R, [string]$Id)
    $a = @($R.Lines)
    for ($i = $a.Count - 2; $i -ge 0; $i--) {
        if (([string]$a[$i]).StartsWith('- ' + $Id + ' [')) {
            $n = [string]$a[$i + 1]
            if ($n.StartsWith('  복구 주체(판정 미반영): ')) { return $n }
            return ''
        }
    }
    return ''
}

function Get-CgSummaryEnd {
    # 종합 블록 마지막 줄(머리부터 빈 줄 · 「보고서:」 줄 · 끝 전까지)
    param($R)
    $a = @($R.Lines)
    $i = Get-CgHeaderIdx $a
    if ($i -lt 0) { return '' }
    $last = ''
    for ($j = $i; $j -lt $a.Count; $j++) {
        $s = [string]$a[$j]
        if ($s.Length -eq 0 -or $s.StartsWith('보고서: ')) { break }
        $last = $s
    }
    return $last
}

function Assert-CgRejected {
    param([string]$Case, $R, [string[]]$Expect, [switch]$Subset)
    $fails = @(Get-CgGuardFails $R.Lines)
    if ($Subset) { foreach ($g in $Expect) { Test-CgCase $Case ('가드 거절 칸에 ' + $g) ($fails -contains $g) } }
    else {
        $want = (@($Expect | Sort-Object) -join ',')
        $got = (@($fails | Sort-Object) -join ',')
        Test-CgCase $Case ('가드 거절 칸 = ' + $want + '(실제 ' + $got + ')') ($want -eq $got)
    }
    Test-CgCase $Case '가드 거절 줄' (@($R.Lines) -contains '가드(G1~G6 + G4a): 거절')
    Test-CgCase $Case '끊기 0' ($R.W.FakeCuts -eq 0)
    Test-CgCase $Case '외부 GET 0' ($R.W.ExtGets -eq 0)
    Test-CgCase $Case '손 기록 부름 0' ($R.W.HandCalls -eq 0)
    Test-CgCase $Case '종합 「미합격(측정 0건)」' ((Get-CgHeader $R).StartsWith('미합격(측정 0건)'))
    Test-CgCase $Case '종료 코드 1' ($R.Exit -eq 1)
    Test-CgCase $Case '파일 쓰기 0(보고서 폴더 없음)' (-not [System.IO.Directory]::Exists($R.ReportDir))
}

function Assert-CgItem {
    param([string]$Case, $R, [string]$Id, [string]$Expect, [switch]$Prefix)
    $got = Get-CgItem $R $Id
    $ok = if ($Prefix) { $got.StartsWith($Expect) } else { $got -eq $Expect }
    Test-CgCase $Case ('{0} 줄 = 「{1}」(실제 「{2}」)' -f $Id, $Expect, $got) $ok
}

function Assert-CgNoPass {
    param([string]$Case, $R, [string]$Id)
    Test-CgCase $Case ($Id + ' PASS 0') (-not (Get-CgItem $R $Id).Contains('] PASS('))
}

function Assert-CgHandoff {
    param([string]$Case, $R)
    $a = @($R.Lines)
    foreach ($k in @('  ── 인계 쪽지(§5-4) · ', '  대상: ', '  마지막 관측 상태: ', '  t0: ', '  경과: ', '  워치독 마지막 복구 기록 시각: ', '  쿨다운 셈: ', '  사람이 할 일(', '  안전 복구 순서: ', '  종료 조건: ')) {
        Test-CgCase $Case ('인계 쪽지 칸 「' + $k.Trim() + '」') (@($a | Where-Object { ([string]$_).StartsWith($k) }).Count -ge 1)
    }
    Test-CgCase $Case '도구 시작 · 재시작 호출: 0' ($a -contains '  도구 시작 · 재시작 호출: 0')
    Test-CgCase $Case '사람 확인 경고(쪽지)' ($a -contains '  경고: 대상 상태 불명 · 사람 확인 필요')
    Test-CgCase $Case '종합 경고 줄' ($a -contains $script:WarnLineG)
    Test-CgCase $Case ('종합 끝 줄 = 「' + $script:HandoffEndG + '」') ((Get-CgSummaryEnd $R) -eq $script:HandoffEndG)
}
$script:WarnLineG = $WarnLine
$script:HandoffEndG = $HandoffEnd

function Get-CgWhere {
    # 어긋났을 때만 쓰는 위치 표시: 몇 번째 덩이 · 그 줄(가짜 값뿐 · 120자까지)
    param([string]$Rx)
    $k = 0
    foreach ($chunk in $script:AllText) {
        $k++
        foreach ($ln in ($chunk -split "`r?`n")) {
            if ($ln -match $Rx) {
                $s = if ($ln.Length -gt 120) { $ln.Substring(0, 120) } else { $ln }
                return (' · 덩이 ' + $k + ' 「' + $s + '」')
            }
        }
    }
    return ''
}

function Get-CgHist {
    param([string]$Key, [int]$Count)
    $l = New-Object System.Collections.Generic.List[string]
    for ($i = 1; $i -le $Count; $i++) {
        $l.Add(('{{"utc":"{0}","key":"{1}"}}' -f (Format-CgUtc -Value $script:Base.AddMinutes(-10 * $i)), $Key))
    }
    return $l.ToArray()
}

# ─────────────────────────────────────────────── 임시 저장소(G4a)

$allowOk = Get-CgAllowText -Domain $script:Id.Domain -Tenant $script:Id.Tenant
$allowOther = Get-CgAllowText -Domain $script:Id.Domain2 -Tenant $script:Id.Tenant2
$allowBoth = '{' + "`n" + '  "entries": [' + "`n" +
    '    { "primaryDomainSha256": "' + (Get-CgIdentityHash -Value $script:Id.Domain2) + '", "tenantCodeSha256": "' + (Get-CgIdentityHash -Value $script:Id.Tenant2) + '" },' + "`n" +
    '    { "primaryDomainSha256": "' + (Get-CgIdentityHash -Value $script:Id.Domain) + '", "tenantCodeSha256": "' + (Get-CgIdentityHash -Value $script:Id.Tenant) + '" }' + "`n" + '  ]' + "`n" + '}' + "`n"
try {
    $script:RepoOk = New-CgRepo -Name 'repo-ok' -MainText $allowOk
    # (11/19 [4] F1 · 시험 시간) 나머지 4개 = repo-ok 복사 + 바꿀 칸만(옛 New-CgRepo 따로 만들기와 G4a 재료가 같음 · git 프로세스 26 → 10)
    $relA = 'tests/scenarios/comms-integrity/test-pc-allowlist.json'
    #   repo-a: origin/main = 다른 PC 허용목록 커밋 · HEAD = 그 뒤 로컬 커밋(이 PC 허용목록) → 작업 사본 ≠ origin/main
    $script:RepoA = Copy-CgRepo -From $script:RepoOk -Name 'repo-a'
    Write-CgFile -Path (Join-Path $script:RepoA ($relA -replace '/', '\')) -Text $allowOther
    Invoke-CgGit -Repo $script:RepoA -GitArgs @('commit', '-a', '-q', '-m', 'main')
    Invoke-CgGit -Repo $script:RepoA -GitArgs @('update-ref', 'refs/remotes/origin/main', 'HEAD')
    Write-CgFile -Path (Join-Path $script:RepoA ($relA -replace '/', '\')) -Text $allowOk
    Invoke-CgGit -Repo $script:RepoA -GitArgs @('commit', '-a', '-q', '-m', 'local')
    #   repo-b: 커밋 안 한 작업 사본 변경(두 PC 허용목록)
    $script:RepoB = Copy-CgRepo -From $script:RepoOk -Name 'repo-b'
    Write-CgFile -Path (Join-Path $script:RepoB ($relA -replace '/', '\')) -Text $allowBoth
    #   repo-c: origin/main 참조 없음
    $script:RepoC = Copy-CgRepo -From $script:RepoOk -Name 'repo-c'
    Invoke-CgGit -Repo $script:RepoC -GitArgs @('update-ref', '-d', 'refs/remotes/origin/main')
    #   repo-f: core.autocrlf true · 작업 사본 CRLF · 저장소 LF(같은 blob)
    $script:RepoF = Copy-CgRepo -From $script:RepoOk -Name 'repo-f'
    Invoke-CgGit -Repo $script:RepoF -GitArgs @('config', 'core.autocrlf', 'true')
    Write-CgFile -Path (Join-Path $script:RepoF ($relA -replace '/', '\')) -Text (Get-CgAllowText -Domain $script:Id.Domain -Tenant $script:Id.Tenant -Nl "`r`n")
    # (11/19 [3-V] 갈래2 V6) 「보고서 폴더 = 저장소 안」 시험용 가짜 저장소 — 임시 폴더에 러너 사본을 같은 상대 위치에 둔다(러너 저장소 뿌리 = 이 폴더).
    #   실제 저장소 안 경로는 쓰지 않는다(거절이 회귀해도 찌꺼기는 임시 폴더에만 · finally 가 지움).
    $script:FakeRepo = Join-Path $script:Root 'fake-repo'
    $script:FakeRunner = Join-Path $script:FakeRepo 'tests\scenarios\comms-integrity\Run-RoutinePcScenarios.ps1'
    $null = [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($script:FakeRunner))
    [System.IO.File]::Copy($script:RunnerPath, $script:FakeRunner, $true)
} catch {
    Write-Host ('임시 git 저장소 준비 실패: {0}' -f $_.Exception.Message)
    Remove-CgTree $script:Root
    Write-Host ('Test-CommsGuard 끝 — 경우 0/{0} 일치 · 세부 확인 0/0 · 진짜 끊기 호출 0' -f $script:CaseMax)
    exit 1
}

# ─────────────────────────────────────────────── 경우 C1~C22

try {

Invoke-CgCaseBlock 'C1' '빈 PC(마커 · db.conf 없음) → 거절(G1) · 끊기 0' {
    $r = Invoke-CgRun -Env @{ Marker = 'none'; Install = 'none' }
    Assert-CgRejected 'C1' $r @('G1') -Subset
}

Invoke-CgCaseBlock 'C2' '대표 PC 모양(마커 없음 · demo 도메인 · 서비스 Running) → 거절(G1 · G4) · 운영 도메인 접촉 0' {
    $r = Invoke-CgRun -Env @{ Marker = 'none'; Domain = ('de' + 'mo.hit' + 'pan.kr') }
    Assert-CgRejected 'C2' $r @('G1', 'G4') -Subset
    Test-CgCase 'C2' 'G4 사유 = demo 도메인' (@($r.Lines) -contains '  G4 거절 · demo 도메인(운영)')
}

Invoke-CgCaseBlock 'C3' '마커 오잔존(기기값 다름) → 거절(G2)' {
    $r = Invoke-CgRun -Env @{ MarkerMachine = (Get-CgIdentityHash -Value ([guid]::NewGuid().ToString())) }
    Assert-CgRejected 'C3' $r @('G2')
}

Invoke-CgCaseBlock 'C4' '마커 만료 · 72시간 넘게 잡힌 만료 → 거절(G3)' {
    $r1 = Invoke-CgRun -Env @{ Expires = (Format-CgUtc -Value $script:Base.AddHours(-1)) }
    Assert-CgRejected 'C4' $r1 @('G3')
    Test-CgCase 'C4' '만료 사유' (@($r1.Lines) -contains '  G3 거절 · 만료됨')
    $r2 = Invoke-CgRun -Env @{ Expires = (Format-CgUtc -Value $script:Base.AddHours(73)) }
    Assert-CgRejected 'C4' $r2 @('G3')
    Test-CgCase 'C4' '72시간 사유' (@($r2.Lines) -contains '  G3 거절 · 72시간 넘게 잡힌 만료')
}

Invoke-CgCaseBlock 'C5' '실고객 모양(도메인 · 테넌트 허용목록 밖) → 거절(G4)' {
    $r = Invoke-CgRun -Env @{ Domain = $script:Id.Domain2; Tenant = $script:Id.Tenant2 }
    Assert-CgRejected 'C5' $r @('G4')
}

Invoke-CgCaseBlock 'C6' 'db.conf 없음(마커 · 기기 맞음) → 거절(G4 · fail-open 닫힘)' {
    $r = Invoke-CgRun -Env @{ DbConf = 'none' }
    Assert-CgRejected 'C6' $r @('G4', 'G5')
    Test-CgCase 'C6' 'G4 사유 = db.conf 없음' (@($r.Lines) -contains '  G4 거절 · db.conf 없음')
}

Invoke-CgCaseBlock 'C7' '비관리자 → 거절(G6)' {
    $r = Invoke-CgRun -Cfg @{ IsAdmin = $false }
    Assert-CgRejected 'C7' $r @('G6')
}

Invoke-CgCaseBlock 'C8' '워치독 멈춤 → 거절(G5)' {
    $r = Invoke-CgRun -Cfg @{ SnapSvc = @{ HitPanWatchdog = 'Stopped' } }
    Assert-CgRejected 'C8' $r @('G5')
}

Invoke-CgCaseBlock 'C9' '감시 목록에 cloudflared 없음 → S-A 측정불가(감시 대상 아님)' {
    $r = Invoke-CgRun -Env @{ WdServices = @('MariaDB') }
    Assert-CgItem 'C9' $r 'S-A' ($LblA + '측정불가(감시 대상 아님)')
    Assert-CgItem 'C9' $r 'S-B' ($LblB + '미실행(앞 건 정지)')
    Test-CgCase 'C9' '끊기 0' ($r.W.FakeCuts -eq 0)
    Test-CgCase 'C9' '종료 코드 1' ($r.Exit -eq 1)
}

Invoke-CgCaseBlock 'C10' '끊기 명령 실패 → 측정불가(멈추기 실패) · 복구 대기 0 · PASS 0' {
    $script:R10 = Invoke-CgRun -Cfg @{ cloudflared = @{ StopThrows = $true } }
    $r = $script:R10
    Assert-CgItem 'C10' $r 'S-A' ($LblA + '측정불가(멈추기 실패)')
    Assert-CgNoPass 'C10' $r 'S-A'
    Test-CgCase 'C10' '복구 대기 0(끊기 뒤 상태 조회 0)' ([int]$r.W.InfoCalls['cloudflared'] -eq 1)
    Test-CgCase 'C10' '복구 대기 0(끊기 뒤 시계 1초 미만)' (($r.W.Ms - $r.W.LastStopMs) -lt 1000)
    Assert-CgItem 'C10' $r 'S-B' ($LblB + '미실행(앞 건 정지)')
    Test-CgCase 'C10' '종료 코드 1' ($r.Exit -eq 1)
}

Invoke-CgCaseBlock 'C11' '끊기 성공 보고 · 계속 Running → 측정불가(멈춤 미관측) · PASS 0' {
    $script:R11 = Invoke-CgRun -Cfg @{ cloudflared = @{ StopAt = -1 } }
    $r = $script:R11
    Assert-CgItem 'C11' $r 'S-A' ($LblA + '측정불가(멈춤 미관측)')
    Assert-CgNoPass 'C11' $r 'S-A'
    Test-CgCase 'C11' '멈춤 관측 창 30초 안에서 끝(복구 대기 0)' ([int]$r.W.InfoCalls['cloudflared'] -le 32)
    Test-CgCase 'C11' '종료 코드 1' ($r.Exit -eq 1)
}

Invoke-CgCaseBlock 'C12' '프로세스 끊기 뒤 옛 번호 남음 → 측정불가 · PASS 0' {
    $r = Invoke-CgRun -Cfg @{ Api = @{ OldGoneAt = -1 } }
    Assert-CgItem 'C12' $r 'S-A' ($LblA + 'PASS(') -Prefix
    Assert-CgItem 'C12' $r 'S-B' ($LblB + 'PASS(') -Prefix
    Assert-CgItem 'C12' $r 'S-C' ($LblC + '측정불가(멈춤 미관측)')
    Assert-CgNoPass 'C12' $r 'S-C'
    Test-CgCase 'C12' '종료 코드 1' ($r.Exit -eq 1)
}

Invoke-CgCaseBlock 'C13' '전 건 가드 거절(-Confirm 있음 · 없음) → 미합격(측정 0건) · 종료 코드 1 · 외부 GET 0' {
    $r1 = Invoke-CgRun -Env @{ Marker = 'none' }
    Assert-CgRejected 'C13' $r1 @('G1') -Subset
    $r2 = Invoke-CgRun -Env @{ Marker = 'none' } -NoConfirm
    Assert-CgRejected 'C13' $r2 @('G1') -Subset
    Test-CgCase 'C13' '판정 모드 거절 = 「외부 GET 안 함」 줄' (@($r2.Lines) -contains '외부 GET 안 함(가드 거절) · 끊기 0 · 파일 쓰기 0')
}

Invoke-CgCaseBlock 'C14' 'S-A FAIL → 그 자리 정지 · S-B · S-C 미실행(앞 건 정지)' {
    $script:R14 = Invoke-CgRun -Cfg @{ cloudflared = (Get-CgSvcTiming -StopAt 500 -RunAt -1) }
    $r = $script:R14
    Assert-CgItem 'C14' $r 'S-A' ($LblA + 'FAIL — 5분 안 복구 안 됨')
    Assert-CgItem 'C14' $r 'S-B' ($LblB + '미실행(앞 건 정지)')
    Assert-CgItem 'C14' $r 'S-C' ($LblC + '미실행(앞 건 정지)')
    Assert-CgItem 'C14' $r 'S-D' ($LblD + '반자동 — 미실행(앞 건 정지)')
    Test-CgCase 'C14' '뒤 건 끊기 0' ([int]$r.W.CutCalls['MariaDB'] -eq 0 -and [int]$r.W.CutCalls['api'] -eq 0 -and $r.W.HandCalls -eq 0)
    Test-CgCase 'C14' '종료 코드 1' ($r.Exit -eq 1)
}

Invoke-CgCaseBlock 'C15' '같은 대상 60분 안 5회째 → 미실행(쿨다운 보호)' {
    $r = Invoke-CgRun -History (Get-CgHist -Key 'svc:cloudflared' -Count 4)
    Assert-CgItem 'C15' $r 'S-A' ($LblA + '미실행(쿨다운 보호)')
    Test-CgCase 'C15' 'S-A 끊기 0' ([int]$r.W.CutCalls['cloudflared'] -eq 0)
    Assert-CgItem 'C15' $r 'S-B' ($LblB + 'PASS(') -Prefix
    Test-CgCase 'C15' '종합 = 4 ID 중 3 실행' ((Get-CgHeader $r) -eq '미합격(도구 범위 4 ID 중 3 실행) — 칸별 사유 · 반자동 완료')
    # ⓑ 워치독 이벤트 셈(restarted 2 + restart failure 2 = 4) · 보고서 기록 0
    $ev = @(
        @{ Svc = 'cloudflared'; Kind = 'restarted'; AtMs = -600000 }, @{ Svc = 'cloudflared'; Kind = 'restarted'; AtMs = -1200000 },
        @{ Svc = 'cloudflared'; Kind = 'restart failure'; AtMs = -1800000 }, @{ Svc = 'cloudflared'; Kind = 'restart failure'; AtMs = -2400000 }
    )
    $rb = Invoke-CgRun -Cfg @{ Events = $ev }
    Assert-CgItem 'C15' $rb 'S-A' ($LblA + '미실행(쿨다운 보호)')
    Test-CgCase 'C15' 'ⓑ 셈 · S-A 끊기 0' ([int]$rb.W.CutCalls['cloudflared'] -eq 0)
    # 이벤트 로그 못 읽음 = 전부 미실행(fail-closed)
    $ru = Invoke-CgRun -Cfg @{ EventsReadable = $false }
    Assert-CgItem 'C15' $ru 'S-A' ($LblA + '미실행(쿨다운 보호 · 이벤트 로그 못 읽음)')
    Assert-CgItem 'C15' $ru 'S-D' ($LblD + '반자동 — 미실행(쿨다운 보호 · 이벤트 로그 못 읽음)')
    Test-CgCase 'C15' '이벤트 못 읽음 · 끊기 0' ($ru.W.FakeCuts -eq 0 -and $ru.W.HandCalls -eq 0)
    # 경계: 지난 4회 미만(3) = 실행
    $r3 = Invoke-CgRun -History (Get-CgHist -Key 'svc:cloudflared' -Count 3)
    Assert-CgItem 'C15' $r3 'S-A' ($LblA + 'PASS(') -Prefix
}

Invoke-CgCaseBlock 'C16' '전 조건 맞음 · 멈춤 관측 · 90초 뒤 복구 → PASS(90초) · 종합 합격' {
    $script:R16 = Invoke-CgRun -Cfg @{ cloudflared = (Get-CgSvcTiming -StopAt 500 -RunAt 90500) }
    $r = $script:R16
    Assert-CgItem 'C16' $r 'S-A' ($LblA + 'PASS(90초) — 5분 안 복구됨')
    Assert-CgItem 'C16' $r 'S-B' ($LblB + 'PASS(4초) — 5분 안 복구됨')
    Assert-CgItem 'C16' $r 'S-C' ($LblC + 'PASS(3초) — 5분 안 복구됨')
    Assert-CgItem 'C16' $r 'S-D' ($LblD + '반자동 — 완료(첫 실패 뒤 10초 복귀 · 손 1회)')
    Test-CgCase 'C16' '종합 합격' ((Get-CgHeader $r) -eq $HdrPass)
    Test-CgCase 'C16' '종료 코드 0' ($r.Exit -eq 0)
}

Invoke-CgCaseBlock 'C18' '비밀 꼴 키 섞인 db.conf · 표지 경로 · 명령줄 → 어디에도 0 · 스냅숏 = 화이트리스트 4키' {
    $r = Invoke-CgRun -Env @{ Secrets = $true } -Verbose
    $script:R18 = $r
    # 같은 가짜 PC 로 스냅숏 직접 수집(-Verbose · 경고 · 자세히 흐름 포함) → 직렬화
    $w = [CgWorld]::new($script:Base, (New-CgCfg -Over $null))
    $prov = New-CgProviders -W $w -E $r.Env -Repo $script:RepoOk
    $recs = @(Get-EnvSnapshot -Providers $prov -NowUtc $script:Base -Verbose 4>&1 3>&1)
    $snap = @($recs | Where-Object { $_ -is [pscustomobject] -and $null -ne $_.PSObject.Properties['DbConf'] })[0]
    $msgs = (@($recs | Where-Object { -not ($_ -is [pscustomobject] -and $null -ne $_.PSObject.Properties['DbConf']) } | ForEach-Object { [string]$_ }) -join "`n")
    $json = $snap | ConvertTo-Json -Depth 10 -Compress
    $hay = @(($r.Lines -join "`n"), [string]$r.ReportText, [string]$r.HistText, $json, $msgs) -join "`n"
    Test-CgCase 'C18' '자세히 출력 실제로 나옴(화이트리스트 밖 버림 줄)' ($msgs.Contains('화이트리스트 밖') -and (($r.Lines -join "`n").Contains('화이트리스트 밖')))
    Test-CgCase 'C18' '표지 문자열 0(값 · 경로 · 명령줄 · 응답 · 이벤트 글)' (-not $hay.Contains($script:Mark))
    foreach ($k in $script:SecretKeys) { Test-CgCase 'C18' ('비밀 꼴 키 이름 0 · ' + $k.Length + '자') (-not $hay.Contains($k)) }
    $keys = (@($snap.DbConf.Values.Keys) | Sort-Object) -join ','
    Test-CgCase 'C18' ('스냅숏 키 = 화이트리스트 4(실제 ' + $keys + ')') ($keys -eq ((@('API_PORT', 'PRIMARY_DOMAIN', 'SLOT_INDEX', 'TENANT_CODE') | Sort-Object) -join ','))
    Test-CgCase 'C18' '깨진 줄 1' ([int]$snap.DbConf.BadLineCount -eq 1)
    $actorRx = '\A  복구 주체\(판정 미반영\): (워치독 재시작 기록과 상태 전이 일치 · 단독 원인 귀속 미확정|불명) [—·] (E1 (예 \d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ|아니오) · E2 (예 \d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ|아니오) · E3 (\d+|모름) · E4 (없음|있음|모름)|S-C = 서비스 증거\(E1 · E2\) 해당 없음)\z'
    $al = @(@($r.Lines) + @($r.ReportLines) | Where-Object { ([string]$_).StartsWith('  복구 주체(판정 미반영): ') })
    Test-CgCase 'C18' ('E1~E4 칸 = 예/아니오 · UTC 시각만(' + $al.Count + '줄)') ($al.Count -ge 6 -and @($al | Where-Object { $_ -notmatch $actorRx }).Count -eq 0)
    Test-CgCase 'C18' '섞인 db.conf 로도 합격 · 종료 코드 0' ((Get-CgHeader $r) -eq $HdrPass -and $r.Exit -eq 0)
}

Invoke-CgCaseBlock 'C19' 'S-A 외부 경로 ⓐ~ⓕ → PASS 0' {
    $a = Invoke-CgRun -Cfg @{ ExtAlwaysOk = $true }
    $script:R19a = $a
    Assert-CgItem 'C19' $a 'S-A' ($LblA + '측정불가(끊김 미반영)')
    Assert-CgItem 'C19' $a 'S-B' ($LblB + '미실행(앞 건 정지)')
    $b = Invoke-CgRun -Cfg @{ Addrs = @('127.0.0.1') }
    Assert-CgItem 'C19' $b 'S-A' ($LblA + '측정불가(외부 경로 아님)')
    Test-CgCase 'C19' 'ⓑ 끊기 0 · 외부 GET 0' ($b.W.FakeCuts -eq 0 -and $b.W.ExtGets -eq 0)
    $c = Invoke-CgRun -Cfg @{ ExtAfterRun = 'other' }
    Assert-CgItem 'C19' $c 'S-A' ($LblA + 'FAIL — 5분 안 복구 안 됨')
    $d = Invoke-CgRun -Cfg @{ ExtBaselineFail = $true }
    Assert-CgItem 'C19' $d 'S-A' ($LblA + '측정불가(기준 응답 없음)')
    Test-CgCase 'C19' 'ⓓ 끊기 0' ($d.W.FakeCuts -eq 0)
    $e = Invoke-CgRun -Cfg @{ ExtAfterRun = 'fail' }
    Assert-CgItem 'C19' $e 'S-A' ($LblA + 'FAIL — 5분 안 복구 안 됨')
    $f = Invoke-CgRun -Cfg @{ cloudflared = (Get-CgSvcTiming -StopAt 500 -RunAt 10500); ExtOkWin = @(1500, 3500) }
    Assert-CgItem 'C19' $f 'S-A' ($LblA + '측정불가(끊김 미반영)')
    foreach ($x in @($a, $b, $c, $d, $e, $f)) { Assert-CgNoPass 'C19' $x 'S-A'; Test-CgCase 'C19' '종료 코드 1' ($x.Exit -eq 1) }
}

Invoke-CgCaseBlock 'C20' 'S-B 원점 ⓐ 끊김 미반영 · ⓑ 인스턴스 불일치 · ⓒ 번호 바뀜 기록' {
    $a = Invoke-CgRun -Cfg @{ DbAlwaysOk = $true }
    Assert-CgItem 'C20' $a 'S-B' ($LblB + '측정불가(끊김 미반영)')
    Assert-CgNoPass 'C20' $a 'S-B'
    $b = Invoke-CgRun -Cfg @{ Api = @{ UnderInstall = $false } }
    Assert-CgItem 'C20' $b 'S-B' ($LblB + '측정불가(인스턴스 불일치)')
    Test-CgCase 'C20' 'ⓑ MariaDB 끊기 0' ([int]$b.W.CutCalls['MariaDB'] -eq 0)
    $c = Invoke-CgRun -Cfg @{ Api = @{ DbRestartPid = 5300 } }
    Assert-CgItem 'C20' $c 'S-B' ($LblB + 'PASS(4초) — 5분 안 복구됨 · 원점 프로세스 번호 바뀜(기록만)')
}

Invoke-CgCaseBlock 'C21' '인계 쪽지 · 복구 주체 별도 칸 · 종합 문구 수준 표지' {
    foreach ($x in @($script:R10, $script:R11, $script:R19a)) { Assert-CgHandoff 'C21' $x }
    # ⓑ 워치독 복구 기록 없는 PASS → 복구 주체 불명 · PASS 그대로
    $b = Invoke-CgRun -Cfg @{ cloudflared = @{ E1At = -1 } }
    Assert-CgItem 'C21' $b 'S-A' ($LblA + 'PASS(5초) — 5분 안 복구됨')
    Test-CgCase 'C21' 'ⓑ 복구 주체 불명' ((Get-CgActorOf $b 'S-A').StartsWith('  복구 주체(판정 미반영): 불명 '))
    # ⓒ 전부 PASS(C16) — 머리 · 고정 줄(콘솔 · 보고서) · 건별 표지 · 금지 문구
    $r = $script:R16
    $hi = Get-CgHeaderIdx $r.Lines
    Test-CgCase 'C21' 'ⓒ 머리 = 합격(4 ID 중 4)' ($hi -ge 0 -and [string]$r.Lines[$hi] -eq $HdrPass)
    Test-CgCase 'C21' 'ⓒ 머리 바로 다음 줄 = 고정 줄(콘솔)' ($hi -ge 0 -and [string]$r.Lines[$hi + 1] -eq $Fixed4)
    Test-CgCase 'C21' 'ⓒ 보고서 1 · 2줄 = 머리 · 고정 줄' (@($r.ReportLines).Count -ge 2 -and $r.ReportLines[0] -eq $HdrPass -and $r.ReportLines[1] -eq $Fixed4)
    foreach ($bad in @('통신', '#27', '17 중')) { Test-CgCase 'C21' ('ⓒ 머리에 「' + $bad + '」 0') (-not $HdrPass.Contains($bad) -and -not ([string]$r.Lines[$hi]).Contains($bad)) }
    Assert-CgItem 'C21' $r 'S-A' $LblA -Prefix
    Assert-CgItem 'C21' $r 'S-B' $LblB -Prefix
    Assert-CgItem 'C21' $r 'S-C' $LblC -Prefix
    Assert-CgItem 'C21' $r 'S-D' $LblD -Prefix
    $all = (@($r.Lines) + @($r.ReportLines)) -join "`n"
    foreach ($bad in @('워치독이 살', '#27 통과', '통신 복구 검증', '워치독(증거 4종)', '- S-A [로컬')) { Test-CgCase 'C21' ('ⓒ 「' + $bad + '」 0') (-not $all.Contains($bad)) }
    Test-CgCase 'C21' 'ⓒ S-A 복구 주체 = 상태 전이 일치 · 단독 원인 귀속 미확정' ((Get-CgActorOf $r 'S-A').StartsWith('  복구 주체(판정 미반영): ' + $ActorMatch + ' — '))
    Test-CgCase 'C21' 'ⓒ S-B 복구 주체 = 상태 전이 일치' ((Get-CgActorOf $r 'S-B').StartsWith('  복구 주체(판정 미반영): ' + $ActorMatch + ' — '))
    # E1 이 E2 앞 · 실패 기록 있음 → 불명 · 둘 다 PASS 그대로
    $c1 = Invoke-CgRun -Cfg @{ cloudflared = @{ E1At = 5300; E2At = 5500 } }
    Assert-CgItem 'C21' $c1 'S-A' ($LblA + 'PASS(5초) — 5분 안 복구됨')
    Test-CgCase 'C21' 'E1 이 E2 앞 → 불명' ((Get-CgActorOf $c1 'S-A').StartsWith('  복구 주체(판정 미반영): 불명 '))
    $c2 = Invoke-CgRun -Cfg @{ cloudflared = @{ FailAt = 3000 } }
    Assert-CgItem 'C21' $c2 'S-A' ($LblA + 'PASS(5초) — 5분 안 복구됨')
    Test-CgCase 'C21' '실패 기록 있음 → 불명' ((Get-CgActorOf $c2 'S-A').StartsWith('  복구 주체(판정 미반영): 불명 '))
}

Invoke-CgCaseBlock 'C22' 'G4a · 안 B 조합 · S-D 끊김 반영 · 관측자 대조' {
    # ⓐ~ⓒ G4a 거절
    $a = Invoke-CgRun -Repo $script:RepoA
    Assert-CgRejected 'C22' $a @('G4a')
    $b = Invoke-CgRun -Repo $script:RepoB
    Assert-CgRejected 'C22' $b @('G4a')
    $c1 = Invoke-CgRun -Repo $script:RepoC
    Assert-CgRejected 'C22' $c1 @('G4a')
    Test-CgCase 'C22' 'ⓒ origin/main 참조 없음 사유' (@($c1.Lines) -contains '  G4a 거절 · 승인본 대조 못 함(origin/main 참조 없음)')
    $c2 = Invoke-CgRun -GitExe (Join-Path $script:Root 'no-such-git.exe')
    Assert-CgRejected 'C22' $c2 @('G4a')
    Test-CgCase 'C22' 'ⓒ git 못 읽음 사유' (@($c2.Lines) -contains '  G4a 거절 · 승인본 대조 못 함(git 못 읽음)')
    # ⓓ 안 B 조합 4줄 + 일부 실행
    $d1 = $script:R16
    $d2 = Invoke-CgRun -Cfg @{ SdDown = @(2500, 99999999) }
    $d3 = Invoke-CgRun -Cfg @{ HandMode = 'none' }
    $d4 = $script:R14
    $d5 = Invoke-CgRun -History (Get-CgHist -Key 'svc:MariaDB' -Count 4)
    $want = @(
        @($d1, $HdrPass, 0),
        @($d2, '미합격 — 자동 3 PASS · 반자동 1 실패(5분 안 복귀 없음) · 시험 종료 아님 · 인계 필요(§5-4)', 1),
        @($d3, '미합격(반자동 미실행 · 4 ID 중 3 실행) — 자동 3 PASS · 합격선 「자동 3 + 반자동 1」 미충족', 1),
        @($d4, '미합격(도구 범위 4 ID 중 1 실행) — 칸별 사유 · 반자동 미실행', 1),
        @($d5, '미합격(도구 범위 4 ID 중 3 실행) — 칸별 사유 · 반자동 완료', 1)
    )
    $k = 0
    foreach ($row in $want) {
        $k++
        $h = Get-CgHeader $row[0]
        Test-CgCase 'C22' ('ⓓ 조합 ' + $k + ' 머리(실제 「' + $h + '」)') ($h -eq $row[1])
        Test-CgCase 'C22' ('ⓓ 조합 ' + $k + ' 종료 코드 ' + $row[2]) ($row[0].Exit -eq $row[2])
        if ($k -gt 1) { Test-CgCase 'C22' ('ⓓ 조합 ' + $k + ' 「합격」 머리 아님') (-not $h.StartsWith('합격')) }
    }
    Assert-CgItem 'C22' $d5 'S-B' ($LblB + '미실행(쿨다운 보호)')
    Test-CgCase 'C22' 'ⓓ 손 기록 없음 = 반자동 미실행' ((Get-CgItem $d3 'S-D') -eq ($LblD + '반자동 — 미실행(손 기록 없음)'))
    # ⓔ 손 기록 뒤 외부 내내 200 → 실패(끊김 미반영)
    $e = Invoke-CgRun -Cfg @{ SdDown = @() }
    Assert-CgItem 'C22' $e 'S-D' ($LblD + '반자동 — 실패(끊김 미반영)')
    Test-CgCase 'C22' 'ⓔ 미합격 · 종료 코드 1 · 인계 문구' ((Get-CgHeader $e) -eq '미합격 — 자동 3 PASS · 반자동 1 실패(끊김 미반영) · 시험 종료 아님 · 인계 필요(§5-4)' -and $e.Exit -eq 1 -and (Get-CgSummaryEnd $e) -eq $HandoffEnd)
    # ⓕ 작업 사본 CRLF · 저장소 LF · 내용 같음 → G4a 통과 · 저장소 쓰기 0
    $wf = [CgWorld]::new($script:Base, (New-CgCfg -Over $null))
    $ef = New-CgCaseEnv -Opt @{}
    $relF = Join-Path $script:RepoF 'tests\scenarios\comms-integrity\test-pc-allowlist.json'
    Test-CgCase 'C22' 'ⓕ 작업 사본 = CRLF' ([System.IO.File]::ReadAllText($relF).Contains("`r`n"))
    $before = Get-CgTreeStamp (Join-Path $script:RepoF '.git')
    $snapF = Get-EnvSnapshot -Providers (New-CgProviders -W $wf -E $ef -Repo $script:RepoF) -NowUtc $script:Base
    $gF = Get-GuardDecision -Snapshot $snapF -NowUtc $script:Base
    $after = Get-CgTreeStamp (Join-Path $script:RepoF '.git')
    Test-CgCase 'C22' 'ⓕ G4a 통과' (@($gF.Checks | Where-Object { $_.Id -eq 'G4a' -and $_.Pass }).Count -eq 1)
    Test-CgCase 'C22' 'ⓕ 가드 허용(나머지도 맞음)' ([bool]$gF.Allowed)
    Test-CgCase 'C22' 'ⓕ 저장소 쓰기 0(.git 파일 · 크기 · 시각 그대로)' ($before -eq $after -and $before.Length -gt 0)
    # ⓖ 단발 실패 1회 · 연속 4회 = 실패(끊김 미반영) / 연속 5회 + 복귀 = 완료
    $g1 = Invoke-CgRun -Cfg @{ SdDown = @(2500, 3500) }
    Assert-CgItem 'C22' $g1 'S-D' ($LblD + '반자동 — 실패(끊김 미반영)')
    $g4 = Invoke-CgRun -Cfg @{ SdDown = @(2500, 6500) }
    Assert-CgItem 'C22' $g4 'S-D' ($LblD + '반자동 — 실패(끊김 미반영)')
    $g5 = Invoke-CgRun -Cfg @{ SdDown = @(2500, 7500) }
    Assert-CgItem 'C22' $g5 'S-D' ($LblD + '반자동 — 완료(첫 실패 뒤 5초 복귀 · 손 1회)')
    foreach ($x in @($g1, $g4)) { Test-CgCase 'C22' 'ⓖ 미합격 · 종료 코드 1' ($x.Exit -eq 1 -and -not (Get-CgHeader $x).StartsWith('합격')) }
    Test-CgCase 'C22' 'ⓖ 5회 + 복귀 = 합격 · 종료 코드 0' ($g5.Exit -eq 0 -and (Get-CgHeader $g5) -eq $HdrPass)
    # ⓗ 관측자 연결 대조
    $h1 = Invoke-CgRun -Cfg @{ CtlFail = @(2500, 12500) }
    Assert-CgItem 'C22' $h1 'S-D' ($LblD + '반자동 — 실패(측정불가(관측자 연결 불명))')
    $h2 = Invoke-CgRun -Cfg @{ CtlTimeout = @(4500, 5500) }
    Assert-CgItem 'C22' $h2 'S-D' ($LblD + '반자동 — 실패(측정불가(관측자 연결 불명))')
    $h3 = Invoke-CgRun -Cfg @{ CtlBaselineFail = $true }
    Assert-CgItem 'C22' $h3 'S-D' ($LblD + '반자동 — 실패(측정불가(기준 응답 없음))')
    Test-CgCase 'C22' 'ⓗ③ 손 안내 0(손 기록 부름 0 · 안내 줄 0)' ($h3.W.HandCalls -eq 0 -and @(@($h3.Lines) | Where-Object { ([string]$_).StartsWith('  손 안내:') }).Count -eq 0)
    $h4 = $g5
    Assert-CgItem 'C22' $h4 'S-D' ($LblD + '반자동 — 완료(') -Prefix
    $h5 = Invoke-CgRun -Cfg @{ CtlThrow = @(4500, 5500) }
    Assert-CgItem 'C22' $h5 'S-D' ($LblD + '반자동 — 실패(측정불가(관측자 연결 불명))')
    foreach ($x in @($h1, $h2, $h3, $h5)) { Test-CgCase 'C22' 'ⓗ 미합격 · 종료 코드 1' ($x.Exit -eq 1 -and -not (Get-CgHeader $x).StartsWith('합격')) }
    foreach ($x in @($h1, $h2, $h3, $h4, $h5)) {
        $t = (@($x.Lines) -join "`n") + "`n" + [string]$x.ReportText + "`n" + [string]$x.HistText
        Test-CgCase 'C22' 'ⓗ 대조 응답 본문(관측자 주소 줄) 0' (-not $t.Contains($script:ObserverIp) -and -not $t.Contains('ip='))
    }
}

# ─────────────────────────────────────────────── 경우 C23~C27 — (11/19) [3-V] 보안상무 A1~A5 입력 · 기대 = 「합격 아님」
# 근거 docs/검증/병렬이슈/20261119_3V_WG1-53_갈래1_가드모듈_보안상무.md A 표. 판정 함수를 가짜 관측 기록으로 직접 부른다(러너 · 실물 0).
# C27 ⓔ 만 러너 1회(가짜 훅). 「대조」 줄 = 고친 뒤에도 맞는 입력은 그대로 PASS · 합격(회귀 확인 · 옛 모듈도 맞음).

$script:VV = '1.3.70'
function New-CgExtS {
    param([string]$S)
    if ($S -eq 'ok') { return @{ AddrOk = $true; Status = 200; Version = $script:VV } }
    return @{ AddrOk = $true; Status = 530; Version = $null }
}
function New-CgOrgS {
    param([string]$S)
    if ($S -eq 'ok') { return @{ Ok = $true; Status = 200; DatabaseOk = $true; Version = $script:VV; UnderInstall = $true; Pid = 5100 } }
    return @{ Ok = $false; Status = 503; DatabaseOk = $false; Version = $script:VV; UnderInstall = $true; Pid = 5100 }
}
function New-CgSvcSm {
    # 서비스 한 회차 — T(-NoT = 칸 없음) · 상태 · 서비스 프로세스 번호 · S-A 외부(ok / fail) 또는 S-B 원점(ok / fail)
    param($T, [string]$Svc, [int]$SPid, [string]$Ext, [string]$Org, [switch]$NoT)
    $h = @{ Svc = $Svc; SvcPid = $SPid }
    if (-not $NoT) { $h.T = $T }
    if ($Ext) { $h.Ext = New-CgExtS $Ext }
    if ($Org) { $h.Origin = New-CgOrgS $Org }
    return $h
}
function New-CgObsSvc {
    param([object[]]$Samples, [int]$PrePid)
    return @{
        Baseline = @{ Ok = $true; Reason = $null; Version = $script:VV; OriginPid = 5100 }
        Cut = @{ Issued = $true; Ok = $true }; CutUtc = $script:Base; PreSvcPid = $PrePid; Samples = $Samples
    }
}
function New-CgRd {
    # S-D 한 회차 — T(-NoT = 칸 없음) · 목표 성공 여부 · 대조 성공 · 간격 Skew
    param($T, [bool]$TOk, $Skew = 0.0, [switch]$NoT)
    $h = @{ Target = @{ Ok = $TOk }; Control = @{ Ok = $true }; Skew = $Skew }
    if (-not $NoT) { $h.T = $T }
    return $h
}
function Get-CgSdRounds {
    # 완료 꼴: T=1~5 목표 실패(대조 성공) · T=6 목표 복귀 → 「완료(첫 실패 뒤 5초 복귀)」. -Skews 로 회차별 Skew 바꿈 · -NoT 로 T 칸 뺌
    param([object[]]$Skews, [switch]$NoT)
    $l = New-Object System.Collections.Generic.List[object]
    for ($i = 1; $i -le 6; $i++) {
        $sk = 0.0
        if ($null -ne $Skews -and $Skews.Count -ge $i) { $sk = $Skews[$i - 1] }
        $l.Add((New-CgRd -T ([double]$i) -TOk ($i -eq 6) -Skew $sk -NoT:$NoT))
    }
    return , ($l.ToArray())
}
function New-CgObsSd {
    param([object[]]$Rounds, $HandUtc = $script:Base, $ObsStart = $script:Base, [switch]$NoObsStart)
    $o = @{ SdBaseline = @{ TargetOk = $true; ControlOk = $true; Version = $script:VV }; Hand = @{ Who = '시험자'; Utc = $HandUtc }; Rounds = $Rounds }
    if (-not $NoObsStart) { $o.ObsStartUtc = $ObsStart }
    return $o
}
function Get-CgStdResults {
    # 합격 짝(자동 3 PASS + 반자동 완료) — 종합 대조용 · Stopped 닻 있는 정상 꼴
    return @{
        A = Get-ScenarioVerdict -Id 'S-A' -Observation (New-CgObsSvc -PrePid 4100 -Samples @((New-CgSvcSm -T 1.0 -Svc 'Stopped' -SPid 0 -Ext 'fail'), (New-CgSvcSm -T 2.0 -Svc 'Running' -SPid 4200 -Ext 'ok')))
        B = Get-ScenarioVerdict -Id 'S-B' -Observation (New-CgObsSvc -PrePid 3100 -Samples @((New-CgSvcSm -T 1.0 -Svc 'Stopped' -SPid 0 -Org 'fail'), (New-CgSvcSm -T 2.0 -Svc 'Running' -SPid 3200 -Org 'ok')))
        C = Get-ScenarioVerdict -Id 'S-C' -Observation @{
            Baseline = @{ Ok = $true; Version = $script:VV; OriginPid = 5100 }; Cut = @{ Issued = $true; Ok = $true }; CutUtc = $script:Base
            Samples = @(@{ T = 1.0; Origin = (New-CgOrgS 'fail'); OldAlive = $false }, @{ T = 2.0; Origin = (New-CgOrgS 'ok'); OldAlive = $false })
        }
        D = Get-ScenarioVerdict -Id 'S-D' -Observation (New-CgObsSd -Rounds (Get-CgSdRounds))
    }
}
function Assert-CgV {
    param([string]$Case, [string]$What, $V, [string]$Expect)
    $got = if ($null -eq $V) { '(없음)' } else { [string]$V.Display }
    Test-CgCase $Case ('{0} = 「{1}」(실제 「{2}」)' -f $What, $Expect, $got) ($null -ne $V -and $got -eq $Expect)
}
$PassA1 = 'PASS(1초) — 5분 안 복구됨'
$SdDone5 = '반자동 — 완료(첫 실패 뒤 5초 복귀 · 손 1회)'

Invoke-CgCaseBlock 'C23' '[3-V] A1 · S-A · S-B 가 Stopped 를 안 보고(번호 바뀜 · 멈춤 전 실패 1회) PASS → 합격 아님' {
    $std = Get-CgStdResults
    Assert-CgV 'C23' '대조 S-A Stopped 닻 = PASS 그대로' $std.A $PassA1
    Assert-CgV 'C23' '대조 S-B Stopped 닻 = PASS 그대로' $std.B $PassA1
    # ⓐ 보안상무 측정 입력: T1 Running/번호 100/외부 실패 → T2 Running/번호 200/외부 ok → T3 ok (옛 = PASS(1초))
    $oa = New-CgObsSvc -PrePid 100 -Samples @((New-CgSvcSm -T 1.0 -Svc 'Running' -SPid 100 -Ext 'fail'), (New-CgSvcSm -T 2.0 -Svc 'Running' -SPid 200 -Ext 'ok'), (New-CgSvcSm -T 3.0 -Svc 'Running' -SPid 200 -Ext 'ok'))
    $va = Get-ScenarioVerdict -Id 'S-A' -Observation $oa
    Assert-CgV 'C23' 'ⓐ S-A 번호 바뀜만' $va '측정불가(멈춤 미관측)'
    Test-CgCase 'C23' 'ⓐ 번호 바뀜 = 기록 칸(Notes)만' (@($va.Notes) -contains '서비스 프로세스 번호 바뀜(멈춤 관측 아님 · 기록만)')
    Test-CgCase 'C23' 'ⓐ 상태 불명 · 인계 · 그 자리 정지' ($va.StateUnknown -and $va.Handoff -and $va.Halt)
    # ⓐ' -Partial 로 30초 창을 다 채움(T2~T31 Running/번호 200/외부 ok) → 정해짐 = 측정불가(멈춤 미관측)
    $sl = New-Object System.Collections.Generic.List[object]
    $sl.Add((New-CgSvcSm -T 1.0 -Svc 'Running' -SPid 100 -Ext 'fail'))
    for ($k = 2; $k -le 31; $k++) { $sl.Add((New-CgSvcSm -T ([double]$k) -Svc 'Running' -SPid 200 -Ext 'ok')) }
    Assert-CgV 'C23' "ⓐ' -Partial 창 끝" (Get-ScenarioVerdict -Id 'S-A' -Observation (New-CgObsSvc -PrePid 100 -Samples $sl.ToArray()) -Partial) '측정불가(멈춤 미관측)'
    # ⓑ S-B 같은 길: T1 Running/번호 바뀜/503 → T2 Running/원점 ok (옛 = PASS(1초))
    $ob = New-CgObsSvc -PrePid 3100 -Samples @((New-CgSvcSm -T 1.0 -Svc 'Running' -SPid 3200 -Org 'fail'), (New-CgSvcSm -T 2.0 -Svc 'Running' -SPid 3200 -Org 'ok'))
    Assert-CgV 'C23' 'ⓑ S-B 번호 바뀜만' (Get-ScenarioVerdict -Id 'S-B' -Observation $ob) '측정불가(멈춤 미관측)'
    # ⓒ S-B 503 이 Stopped 관측 전에만: T1 StopPending/503 → T2 Stopped/원점 ok → T3 Running/ok (옛 = PASS(1초))
    $oc = New-CgObsSvc -PrePid 3100 -Samples @((New-CgSvcSm -T 1.0 -Svc 'StopPending' -SPid 3100 -Org 'fail'), (New-CgSvcSm -T 2.0 -Svc 'Stopped' -SPid 0 -Org 'ok'), (New-CgSvcSm -T 3.0 -Svc 'Running' -SPid 3200 -Org 'ok'))
    $vc = Get-ScenarioVerdict -Id 'S-B' -Observation $oc
    Assert-CgV 'C23' 'ⓒ S-B 멈춤 전 503 만' $vc '측정불가(끊김 미반영)'
    Test-CgCase 'C23' 'ⓒ 상태 불명 · 인계' ($vc.StateUnknown -and $vc.Handoff)
    # ⓓ 종합: ⓐ + 나머지 합격 짝 → 합격 아님 · 종료 1 · 경고 · 인계
    $s = Get-Summary -Results @($va, $std.B, $std.C, $std.D)
    Test-CgCase 'C23' ('ⓓ 종합 합격 아님 · 종료 1(실제 「' + $s.Header + '」)') (-not $s.Header.StartsWith('합격') -and $s.ExitCode -eq 1)
    Test-CgCase 'C23' 'ⓓ 경고 줄 · 인계 끝 줄' ((@($s.Text) -contains $WarnLine) -and $s.Text[-1] -eq $HandoffEnd)
}

Invoke-CgCaseBlock 'C24' '[3-V] A2 · 회차 시각 칸 T 누락 · 숫자 아님 · 음수 · 역행 → 측정불가(관측 기록 무효) · 합격 아님' {
    $want = '측정불가(관측 기록 무효)'
    # ⓐ T 누락(옛 = PASS(0초))
    $oa = New-CgObsSvc -PrePid 4100 -Samples @((New-CgSvcSm -NoT -Svc 'Stopped' -SPid 0 -Ext 'fail'), (New-CgSvcSm -NoT -Svc 'Running' -SPid 4200 -Ext 'ok'))
    $va = Get-ScenarioVerdict -Id 'S-A' -Observation $oa
    Assert-CgV 'C24' 'ⓐ S-A T 누락' $va $want
    Test-CgCase 'C24' 'ⓐ 상태 불명 · 인계 · 그 자리 정지' ($va.StateUnknown -and $va.Handoff -and $va.Halt)
    # ⓑ 역행 T=10 → 900 → 12(옛 = PASS(2초)) · -Partial 도 바로 정해짐(러너가 그 자리 멈춤)
    $ob = New-CgObsSvc -PrePid 4100 -Samples @((New-CgSvcSm -T 10.0 -Svc 'Stopped' -SPid 0 -Ext 'fail'), (New-CgSvcSm -T 900.0 -Svc 'Stopped' -SPid 0 -Ext 'fail'), (New-CgSvcSm -T 12.0 -Svc 'Running' -SPid 4200 -Ext 'ok'))
    Assert-CgV 'C24' 'ⓑ S-A 역행' (Get-ScenarioVerdict -Id 'S-A' -Observation $ob) $want
    Assert-CgV 'C24' 'ⓑ S-A 역행 -Partial' (Get-ScenarioVerdict -Id 'S-A' -Observation $ob -Partial) $want
    # ⓒ 숫자 아님(글자 「1」 「2」 · 옛 = PASS(1초))
    $oc = New-CgObsSvc -PrePid 4100 -Samples @((New-CgSvcSm -T '1' -Svc 'Stopped' -SPid 0 -Ext 'fail'), (New-CgSvcSm -T '2' -Svc 'Running' -SPid 4200 -Ext 'ok'))
    Assert-CgV 'C24' 'ⓒ S-A T 글자' (Get-ScenarioVerdict -Id 'S-A' -Observation $oc) $want
    # ⓓ 음수 T=-5 → -4(옛 = PASS(1초))
    $od = New-CgObsSvc -PrePid 4100 -Samples @((New-CgSvcSm -T (-5.0) -Svc 'Stopped' -SPid 0 -Ext 'fail'), (New-CgSvcSm -T (-4.0) -Svc 'Running' -SPid 4200 -Ext 'ok'))
    Assert-CgV 'C24' 'ⓓ S-A T 음수' (Get-ScenarioVerdict -Id 'S-A' -Observation $od) $want
    # ⓔ 같은 값(엄격 증가 아님) T=1 → 1(옛 = PASS(0초)) · NaN
    $oe = New-CgObsSvc -PrePid 4100 -Samples @((New-CgSvcSm -T 1.0 -Svc 'Stopped' -SPid 0 -Ext 'fail'), (New-CgSvcSm -T 1.0 -Svc 'Running' -SPid 4200 -Ext 'ok'))
    Assert-CgV 'C24' 'ⓔ S-A T 같은 값' (Get-ScenarioVerdict -Id 'S-A' -Observation $oe) $want
    $on = New-CgObsSvc -PrePid 4100 -Samples @((New-CgSvcSm -T ([double]::NaN) -Svc 'Stopped' -SPid 0 -Ext 'fail'), (New-CgSvcSm -T 2.0 -Svc 'Running' -SPid 4200 -Ext 'ok'))
    Assert-CgV 'C24' 'ⓔ S-A T NaN' (Get-ScenarioVerdict -Id 'S-A' -Observation $on) $want
    # ⓕ S-B T 누락(옛 = PASS(0초))
    $of = New-CgObsSvc -PrePid 3100 -Samples @((New-CgSvcSm -NoT -Svc 'Stopped' -SPid 0 -Org 'fail'), (New-CgSvcSm -NoT -Svc 'Running' -SPid 3200 -Org 'ok'))
    Assert-CgV 'C24' 'ⓕ S-B T 누락' (Get-ScenarioVerdict -Id 'S-B' -Observation $of) $want
    # ⓖ S-D T 누락(옛 = 완료(0초)) · 역행
    $vg = Get-ScenarioVerdict -Id 'S-D' -Observation (New-CgObsSd -Rounds (Get-CgSdRounds -NoT))
    Assert-CgV 'C24' 'ⓖ S-D T 누락' $vg ('반자동 — 실패(' + $want + ')')
    Test-CgCase 'C24' 'ⓖ S-D 인계 · 상태 불명' ($vg.Handoff -and $vg.StateUnknown)
    $rr = Get-CgSdRounds
    $rr[3].T = 2.5
    Assert-CgV 'C24' 'ⓖ S-D T 역행' (Get-ScenarioVerdict -Id 'S-D' -Observation (New-CgObsSd -Rounds $rr)) ('반자동 — 실패(' + $want + ')')
    # ⓗ 종합: ⓐ + 나머지 합격 짝 → 합격 아님 · 종료 1
    $std = Get-CgStdResults
    $s = Get-Summary -Results @($va, $std.B, $std.C, $std.D)
    Test-CgCase 'C24' ('ⓗ 종합 합격 아님 · 종료 1(실제 「' + $s.Header + '」)') (-not $s.Header.StartsWith('합격') -and $s.ExitCode -eq 1)
}

Invoke-CgCaseBlock 'C25' '[3-V] A3 · S-D 손 기록 시각이 관측 창 밖 · 대조 간격 Skew NaN · 음수 → 합격 아님' {
    $wantRec = '반자동 — 실패(측정불가(관측 기록 무효))'
    $wantObs = '반자동 — 실패(측정불가(관측자 연결 불명))'
    $rd = Get-CgSdRounds
    Assert-CgV 'C25' '대조 관측 시작 = 손 기록 · Skew 0 → 완료' (Get-ScenarioVerdict -Id 'S-D' -Observation (New-CgObsSd -Rounds $rd)) $SdDone5
    # ⓐ 손 기록 2000-01-01T00:00:00Z(관측은 2026) — 옛 = 완료
    $va = Get-ScenarioVerdict -Id 'S-D' -Observation (New-CgObsSd -Rounds $rd -HandUtc '2000-01-01T00:00:00Z')
    Assert-CgV 'C25' 'ⓐ 손 기록 2000년' $va $wantRec
    Test-CgCase 'C25' 'ⓐ 인계 · 상태 불명' ($va.Handoff -and $va.StateUnknown)
    # ⓑ 관측 시작 기록 없음(fail-closed · 옛 = 완료)
    Assert-CgV 'C25' 'ⓑ 관측 시작 없음' (Get-ScenarioVerdict -Id 'S-D' -Observation (New-CgObsSd -Rounds $rd -NoObsStart)) $wantRec
    # ⓒ 손 기록이 관측 시작보다 10초 뒤
    Assert-CgV 'C25' 'ⓒ 손 기록이 관측보다 뒤' (Get-ScenarioVerdict -Id 'S-D' -Observation (New-CgObsSd -Rounds $rd -HandUtc $script:Base.AddSeconds(10))) $wantRec
    # ⓓ 관측 시작 = 손 기록 + 3초(상한 2초 넘김) · ⓔ 첫 회차(T=1)가 관측 시작(손 기록 + 1.5초) 앞
    Assert-CgV 'C25' 'ⓓ 관측 시작이 손 기록 + 3초' (Get-ScenarioVerdict -Id 'S-D' -Observation (New-CgObsSd -Rounds $rd -ObsStart $script:Base.AddSeconds(3))) $wantRec
    Assert-CgV 'C25' 'ⓔ 첫 회차가 관측 시작 앞' (Get-ScenarioVerdict -Id 'S-D' -Observation (New-CgObsSd -Rounds $rd -ObsStart $script:Base.AddMilliseconds(1500))) $wantRec
    # ⓕ Skew NaN · ⓖ −500 · −0.001(옛 = 완료)
    Assert-CgV 'C25' 'ⓕ Skew NaN' (Get-ScenarioVerdict -Id 'S-D' -Observation (New-CgObsSd -Rounds (Get-CgSdRounds -Skews @([double]::NaN)))) $wantObs
    Assert-CgV 'C25' 'ⓖ Skew -500' (Get-ScenarioVerdict -Id 'S-D' -Observation (New-CgObsSd -Rounds (Get-CgSdRounds -Skews @(0.0, 0.0, (-500.0))))) $wantObs
    Assert-CgV 'C25' 'ⓖ Skew -0.001' (Get-ScenarioVerdict -Id 'S-D' -Observation (New-CgObsSd -Rounds (Get-CgSdRounds -Skews @(0.0, 0.0, 0.0, 0.0, 0.0, (-0.001))))) $wantObs
    # 대조 경계: Skew 2(상한 그대로) = 완료 · 2.001 = 관측자 연결 불명
    Assert-CgV 'C25' '대조 Skew 2 = 완료' (Get-ScenarioVerdict -Id 'S-D' -Observation (New-CgObsSd -Rounds (Get-CgSdRounds -Skews @(2.0)))) $SdDone5
    Assert-CgV 'C25' '대조 Skew 2.001 = 관측자 연결 불명' (Get-ScenarioVerdict -Id 'S-D' -Observation (New-CgObsSd -Rounds (Get-CgSdRounds -Skews @(2.001)))) $wantObs
    # ⓗ 종합: 자동 3 PASS + ⓐ → 「미합격 — … 반자동 1 실패(측정불가(관측 기록 무효)) …」 · 종료 1
    $std = Get-CgStdResults
    $s = Get-Summary -Results @($std.A, $std.B, $std.C, $va)
    Test-CgCase 'C25' ('ⓗ 종합 = 미합격(반자동 실패) · 종료 1(실제 「' + $s.Header + '」)') ($s.Header -eq '미합격 — 자동 3 PASS · 반자동 1 실패(측정불가(관측 기록 무효)) · 시험 종료 아님 · 인계 필요(§5-4)' -and $s.ExitCode -eq 1)
}

Invoke-CgCaseBlock 'C26' '[3-V] A4 · Get-Summary 같은 ID 결과 두 번 → 거절(뒤가 앞을 안 덮음 · 종료 1 · 경고 줄)' {
    $std = Get-CgStdResults
    $fa = Get-ScenarioVerdict -Id 'S-A' -Observation (New-CgObsSvc -PrePid 4100 -Samples @((New-CgSvcSm -T 1.0 -Svc 'Stopped' -SPid 0 -Ext 'fail'), (New-CgSvcSm -T 302.0 -Svc 'Stopped' -SPid 0 -Ext 'fail')))
    Assert-CgV 'C26' '준비 S-A FAIL(상태 불명 · 인계)' $fa 'FAIL — 5분 안 복구 안 됨'
    $s0 = Get-Summary -Results @($std.A, $std.B, $std.C, $std.D)
    Test-CgCase 'C26' '대조 중복 없음 = 합격 · 종료 0' ($s0.Header -eq $HdrPass -and $s0.ExitCode -eq 0)
    $dupA = '⚠ 결과 중복(S-A 2건) · 뒤 결과로 앞을 덮지 않음 · 사람 확인 필요'
    # ⓐ 보안상무 측정 입력: S-A FAIL 다음 S-A PASS(옛 = 합격 · 종료 0 · 경고 0 · 인계 0)
    $sa = Get-Summary -Results @($fa, $std.A, $std.B, $std.C, $std.D)
    Test-CgCase 'C26' ('ⓐ 머리 = 미합격(결과 중복 · S-A 2건)(실제 「' + $sa.Header + '」)') ($sa.Header.StartsWith('미합격(결과 중복 · S-A 2건)'))
    Test-CgCase 'C26' 'ⓐ 종료 코드 1' ($sa.ExitCode -eq 1)
    Test-CgCase 'C26' 'ⓐ 결과 중복 경고 줄' (@($sa.Text) -contains $dupA)
    Test-CgCase 'C26' 'ⓐ 상태 불명 경고 줄(FAIL 쪽 그대로)' (@($sa.Text) -contains $WarnLine)
    Test-CgCase 'C26' 'ⓐ 인계 끝 줄' ($sa.Text[-1] -eq $HandoffEnd)
    Test-CgCase 'C26' 'ⓐ 건별 S-A 줄 = 앞 결과(FAIL) · 뒤 PASS 가 안 덮음' ((@($sa.Text) -contains ($LblA + 'FAIL — 5분 안 복구 안 됨')) -and -not (@($sa.Text) -contains ($LblA + $PassA1)))
    Test-CgCase 'C26' 'ⓐ 고정 줄 = 머리 바로 다음 줄' ($sa.Text[1] -eq $Fixed4)
    foreach ($bad in @('통신', '#27', '17 중')) { Test-CgCase 'C26' ('ⓐ 머리에 「' + $bad + '」 0') (-not $sa.Header.Contains($bad)) }
    # ⓑ 순서 반대(PASS 다음 FAIL) — 미합격 · 종료 1 · 경고 · 인계(뒤 FAIL 의 인계가 사라지지 않음)
    $sb = Get-Summary -Results @($std.A, $std.B, $std.C, $std.D, $fa)
    Test-CgCase 'C26' 'ⓑ 순서 반대 = 미합격(결과 중복) · 종료 1 · 경고 2줄 · 인계' ($sb.Header.StartsWith('미합격(결과 중복 · S-A 2건)') -and $sb.ExitCode -eq 1 -and (@($sb.Text) -contains $dupA) -and (@($sb.Text) -contains $WarnLine) -and $sb.Text[-1] -eq $HandoffEnd)
    # ⓒ 같은 PASS 두 번 · ⓓ S-D 두 번(완료 · 실패) — 다른 칸이 다 맞아도 합격 · 종료 0 없음
    $sc = Get-Summary -Results @($std.A, $std.A, $std.B, $std.C, $std.D)
    Test-CgCase 'C26' 'ⓒ 같은 PASS 두 번 = 미합격 · 종료 1 · 경고' ($sc.Header.StartsWith('미합격(결과 중복 · S-A 2건)') -and $sc.ExitCode -eq 1 -and (@($sc.Text) -contains $dupA))
    $sdFail = Get-ScenarioVerdict -Id 'S-D' -Observation (New-CgObsSd -Rounds (Get-CgSdRounds -Skews @([double]::NaN)))
    $sd2 = Get-Summary -Results @($std.A, $std.B, $std.C, $std.D, $sdFail)
    Test-CgCase 'C26' ('ⓓ S-D 두 번 = 미합격 · 종료 1(실제 「' + $sd2.Header + '」)') ($sd2.Header.StartsWith('미합격(결과 중복 · S-D 2건)') -and $sd2.ExitCode -eq 1 -and $sd2.Text[-1] -eq $HandoffEnd)
}

Invoke-CgCaseBlock 'C27' '[3-V] A5 · db.conf 포트 · 슬롯이 전각 · 아랍 숫자 → 예외 0 · 거절 판정 객체(fail-closed)' {
    $fw = -join [char[]](0xFF11, 0xFF15, 0xFF12, 0xFF15, 0xFF17)   # 전각 숫자 15257
    $ar = -join [char[]](0x0661, 0x0665, 0x0662, 0x0665, 0x0667)   # 아랍-인도 숫자 15257
    $ar9 = [string][char]0x0669
    # ⓐ 변환 함수 — 예외 0 · 없음($null) / ASCII 는 그대로(대조)
    Test-CgCase 'C27' 'ⓐ 포트 전각 = 없음(예외 0)' ($null -eq (ConvertTo-CgPort -Value $fw))
    Test-CgCase 'C27' 'ⓐ 포트 아랍 숫자 = 없음(예외 0)' ($null -eq (ConvertTo-CgPort -Value $ar))
    Test-CgCase 'C27' 'ⓐ 슬롯 아랍 숫자 = 없음(예외 0)' ($null -eq (ConvertTo-CgSlot -Value $ar9))
    Test-CgCase 'C27' '대조 ASCII 포트 · 슬롯 그대로' ((ConvertTo-CgPort -Value '15257') -eq 15257 -and (ConvertTo-CgSlot -Value '9') -eq 9)
    # ⓑ Get-EnvSnapshot(가짜 공급자 · db.conf API_PORT = 전각) → 객체(예외 0) · 원점 GET 0
    $w = [CgWorld]::new($script:Base, (New-CgCfg -Over $null))
    $e = New-CgCaseEnv -Opt @{ ApiPort = $fw }
    $snap = Get-EnvSnapshot -Providers (New-CgProviders -W $w -E $e -Repo $script:RepoOk) -NowUtc $script:Base
    Test-CgCase 'C27' 'ⓑ 스냅숏 = 객체(예외 0) · 원점 GET 0' ($null -ne $snap -and $w.LocalGets -eq 0)
    # ⓒ Get-GuardDecision → 객체(예외 0) · Allowed = $false(불리언) · 거절 = G5 하나
    $g = Get-GuardDecision -Snapshot $snap -NowUtc $script:Base
    Test-CgCase 'C27' 'ⓒ 판정 객체 · Allowed = 불리언 거짓' ($null -ne $g -and $g.Allowed -is [bool] -and $g.Allowed -eq $false)
    Test-CgCase 'C27' ('ⓒ 거절 = G5 API_PORT 없음 · 못 읽음 하나(실제 ' + (@($g.RejectReasons) -join ' / ') + ')') ((@($g.RejectReasons) -join '|') -eq 'G5 API_PORT 없음 · 못 읽음')
    # ⓓ 아랍 숫자 포트를 스냅숏에 넣고 판정만 다시 → 객체 · 거절
    $snap.DbConf.Values['API_PORT'] = $ar
    $g2 = Get-GuardDecision -Snapshot $snap -NowUtc $script:Base
    Test-CgCase 'C27' 'ⓓ 아랍 숫자 포트 = 판정 객체 · 거절' ($null -ne $g2 -and $g2.Allowed -eq $false -and (@($g2.RejectReasons) -contains 'G5 API_PORT 없음 · 못 읽음'))
    # ⓔ 러너 전체 런(가짜 훅) → 가드 거절(G5 하나) · 끊기 0 · 외부 GET 0 · 손 0 · 파일 0 · 종료 1
    $r = Invoke-CgRun -Env @{ ApiPort = $fw }
    Assert-CgRejected 'C27' $r @('G5')
    # ⓕ 러너 거절 조건 = Allowed 가 참이 아니면 거절($null · 객체 없음도 거절 쪽 · 러너 규칙 고정)
    $rt = [System.IO.File]::ReadAllText($script:RunnerPath, [System.Text.Encoding]::UTF8)
    Test-CgCase 'C27' 'ⓕ 러너 거절 조건 = 「if (-not $guard.Allowed)」' ($rt.Contains('if (-not $guard.Allowed) {'))
}

# ─────────────────────────────────────────────── 경우 C28~C32 — (11/19) [3-V] 갈래2 보안상무 V1 · V2(+V6) · V3 · V4 · V9
# 근거 docs/검증/병렬이슈/20261119_3V_WG1-53_갈래2_러너시험_보안상무.md A 표 · 끝 「PM 판단」 표(고침 행).
# 러너는 가짜 훅으로만(-Confirm 은 이 시험 장치 안 · 실물 공급자 0). 순수 함수는 러너 파일에서 구문 트리로 꺼내 정의만(러너 본체 실행 0).

function Get-CgRunnerFunctions {
    param([string]$Path, [string[]]$Names)
    $tk = $null; $pe = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tk, [ref]$pe)
    $out = [ordered]@{}
    foreach ($f in @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true))) {
        if ($Names -contains $f.Name) { $out[$f.Name] = $f.Extent.Text }
    }
    return $out
}

function Get-CgCutHits {
    # (V4) 「진짜 끊기」 꼴 — 별칭(spsv · kill · spps) · taskkill · net stop · sc stop · sc.exe stop · .Kill( (구문 트리 · 실행 0)
    param($Ast)
    $q = [char[]]@([char]34, [char]39)
    $hits = New-Object System.Collections.Generic.List[string]
    foreach ($c in @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true))) {
        $name = [string]$c.GetCommandName()
        if (-not $name) { continue }
        $nm = $name.ToLowerInvariant()
        $rest = @($c.CommandElements | Select-Object -Skip 1 | ForEach-Object { ([string]$_.Extent.Text).Trim($q).ToLowerInvariant() })
        if (@('spsv', 'kill', 'spps', 'taskkill', 'taskkill.exe') -contains $nm) { $hits.Add($nm) }
        elseif (@('net', 'net.exe', 'net1', 'net1.exe') -contains $nm -and $rest -contains 'stop') { $hits.Add($nm + ' stop') }
        elseif (@('sc', 'sc.exe') -contains $nm -and $rest -contains 'stop') { $hits.Add($nm + ' stop') }
    }
    foreach ($m in @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.InvokeMemberExpressionAst] }, $true))) {
        if (([string]$m.Member.Extent.Text).Trim($q) -ieq 'Kill') { $hits.Add('.Kill(') }
    }
    return $hits.ToArray()   # 원소로 내보냄(부르는 쪽이 @() 로 받음)
}

Invoke-CgCaseBlock 'C28' '[3-V 갈래2] V1 · 끊은 뒤 관측 중 훅 예외 → 인계 쪽지 · 대상 상태 불명 경고 · 실행 기록 · 보고서 · 종료 1' {
    $abortLine = '  관측 중단: 정상 끝 없이 빠짐('
    # ⓐ S-A 끊은 뒤 잠 훅이 3초째 던짐(가짜 · 실물 0)
    $a = Invoke-CgRun -Cfg @{ ObsThrowAt = 2500 }
    Test-CgCase 'C28' 'ⓐ 러너 밖으로 예외 0(종류만 경고 · 스스로 끝냄)' ($null -eq $a.Error)
    Test-CgCase 'C28' 'ⓐ 종료 코드 1' ($a.Exit -eq 1)
    Test-CgCase 'C28' 'ⓐ 끊기 1회뿐(다음 건 끊기 0)' ($a.W.FakeCuts -eq 1 -and [int]$a.W.CutCalls['cloudflared'] -eq 1)
    Test-CgCase 'C28' 'ⓐ 관측 중단 줄 1' (@(@($a.Lines) | Where-Object { ([string]$_).StartsWith($abortLine) }).Count -eq 1)
    Assert-CgItem 'C28' $a 'S-A' ($LblA + '측정불가(관측 중단)')
    Assert-CgItem 'C28' $a 'S-B' ($LblB + '미실행(앞 건 정지)')
    Assert-CgItem 'C28' $a 'S-D' ($LblD + '반자동 — 미실행(앞 건 정지)')
    Assert-CgNoPass 'C28' $a 'S-A'
    Assert-CgHandoff 'C28' $a
    Test-CgCase 'C28' 'ⓐ 인계 쪽지 t0 = 모름(관측 중단)' (@($a.Lines) -contains '  t0: 모름(관측 중단 · 마지막 판정 없음)')
    Test-CgCase 'C28' 'ⓐ 보고서 파일 · 인계 쪽지 · 쪽지 경고 · 종합 경고 · 인계 끝 줄' ($null -ne $a.ReportText -and $a.ReportText.Contains('  ── 인계 쪽지(§5-4) · S-A') -and $a.ReportText.Contains('  경고: 대상 상태 불명 · 사람 확인 필요') -and (@($a.ReportLines) -contains $WarnLine) -and (@($a.ReportLines) -contains $HandoffEnd))
    Test-CgCase 'C28' 'ⓐ 실행 기록(쿨다운) svc:cloudflared 1줄' ($null -ne $a.HistText -and @(([string]$a.HistText -split "`n") | Where-Object { $_ -match '"key":"svc:cloudflared"' }).Count -eq 1)
    Test-CgCase 'C28' 'ⓐ 예외 글(메시지) 0 · 종류만' (-not ((($a.Lines -join "`n") + [string]$a.ReportText).Contains('fake observe failure')))
    # ⓑ S-D 손 기록 뒤 관측 중 던짐 → 반자동 실패(관측 중단) · 인계
    $b = Invoke-CgRun -Cfg @{ HandThrowAt = 2500 }
    Test-CgCase 'C28' 'ⓑ 러너 밖으로 예외 0 · 종료 코드 1' ($null -eq $b.Error -and $b.Exit -eq 1)
    Assert-CgItem 'C28' $b 'S-D' ($LblD + '반자동 — 실패(관측 중단)')
    Test-CgCase 'C28' ('ⓑ 머리 = 미합격(반자동 1 실패(관측 중단))(실제 「' + (Get-CgHeader $b) + '」)') ((Get-CgHeader $b) -eq '미합격 — 자동 3 PASS · 반자동 1 실패(관측 중단) · 시험 종료 아님 · 인계 필요(§5-4)')
    Assert-CgHandoff 'C28' $b
    Test-CgCase 'C28' 'ⓑ 인계 대상 = 터널 자격증명' (@($b.Lines) -contains '  대상: 터널 자격증명(사람이 무효화)')
    Test-CgCase 'C28' 'ⓑ 실행 기록 TunnelSecret 1줄' ($null -ne $b.HistText -and @(([string]$b.HistText -split "`n") | Where-Object { $_ -match '"key":"TunnelSecret"' }).Count -eq 1)
    # ⓒ 정적 — 건 순회(foreach $cat in $catalog)가 try 안 · 그 try 의 finally 가 Complete-RgAbort 를 부름
    #   (Ctrl+C = 파이프라인 정지 — catch 는 안 돌고 finally 만 돎: 이 PC 5.1 [PowerShell]::Stop() 흉내 실측 · 개발명세서 §4)
    $tk = $null; $pe = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($script:RunnerPath, [ref]$tk, [ref]$pe)
    $tries = @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.TryStatementAst] }, $true) | Where-Object {
        $null -ne $_.Finally -and @($_.Body.FindAll({ param($n) $n -is [System.Management.Automation.Language.ForEachStatementAst] -and $n.Variable.VariablePath.UserPath -eq 'cat' -and [string]$n.Condition.Extent.Text -eq '$catalog' }, $true)).Count -ge 1
    })
    $fin = if ($tries.Count -eq 1) { @($tries[0].Finally.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Complete-RgAbort' }, $true)).Count } else { 0 }
    Test-CgCase 'C28' 'ⓒ 건 순회 try 의 finally 가 Complete-RgAbort 부름(Ctrl+C 길)' ($tries.Count -eq 1 -and $fin -eq 1)
}

Invoke-CgCaseBlock 'C29' '[3-V 갈래2] V2 · V6 · 보고서 폴더 장치 경로 · UNC · 상대 · 정션이 (가짜) 저장소 안 → 거절 · 끊기 0 · 폴더 0' {
    $fx = Get-CgRunnerFunctions -Path $script:RunnerPath -Names @('Test-RgUnderDir', 'Resolve-RgRealPath', 'Test-RgReportDirOutside')
    Test-CgCase 'C29' '러너 함수 3개 있음' ($fx.Count -eq 3)
    foreach ($t in @($fx.Values)) { . ([scriptblock]::Create($t)) }
    $repo = $script:FakeRepo
    $inP = Join-Path $repo 'tests\cg-p'
    $drv = $inP.Substring(0, 1)
    $rest = $inP.Substring(3)
    # 순수(파일 시스템을 건드리기 전 글자로 거절) — 보안상무 측정 4꼴(ⓑ~ⓔ) + 「/」 꼴 · 드라이브 상대 · 뿌리 상대 · 상대
    $forms = [ordered]@{
        'ⓐ 그대로'            = $inP
        'ⓑ \\?\'              = ('\\?\' + $inP)
        'ⓒ \\.\'              = ('\\.\' + $inP)
        'ⓓ \\localhost\C$'    = ('\\localhost\' + $drv.ToUpperInvariant() + '$\' + $rest)
        'ⓔ \\127.0.0.1\c$'    = ('\\127.0.0.1\' + $drv.ToLowerInvariant() + '$\' + $rest)
        'ⓕ //localhost/C$/'   = (('//localhost/' + $drv + '$/' + $rest) -replace '\\', '/')
        'ⓖ 드라이브 상대 C:x' = ($inP.Substring(0, 2) + $rest)
        'ⓗ 뿌리 상대 \x'      = ('\' + $rest)
        'ⓘ 상대 cg-rel'       = 'cg-rel'
    }
    foreach ($k in $forms.Keys) { Test-CgCase 'C29' ('순수 ' + $k + ' = 거절') (-not (Test-RgReportDirOutside -Dir $forms[$k] -RepoRoot $repo)) }
    Test-CgCase 'C29' '대조 순수 · 임시 폴더(저장소 밖) = 받음' (Test-RgReportDirOutside -Dir (Join-Path $script:Root 'outside-rd') -RepoRoot $repo)
    # e2e(가짜 저장소 안 러너 사본 · 가짜 훅) — 장치 경로 2꼴. UNC 꼴은 순수로만(이 PC 관리 공유 접속 0 — 거절이 회귀한 사본이 SMB 로 폴더를 만들지 않게)
    foreach ($pair in @(@('ⓑ', '\\?\', 'cg-rd-b'), @('ⓒ', '\\.\', 'cg-rd-c'))) {
        $real = Join-Path $repo ('tests\' + $pair[2])
        $x = Invoke-CgRun -Runner $script:FakeRunner -ReportDir ($pair[1] + $real)
        Test-CgCase 'C29' ('e2e ' + $pair[0] + ' 끊기 0 · 폴더 0 · 판정 모드 줄') ($x.W.FakeCuts -eq 0 -and -not [System.IO.Directory]::Exists($real) -and (($x.Lines -join "`n").Contains('판정 모드(보고서 폴더가 저장소 안')))
    }
    # 정션(임시 폴더 · 관리자 권한 불요) → 가짜 저장소 tests — 못 만들면 「미측정」(합격으로 안 셈 · 끝에 표시 · 세부 확인 하한에서 뺀 6건)
    $jx = Join-Path $script:Root 'jx'
    $made = $false
    try { $null = New-Item -ItemType Junction -Path $jx -Value (Join-Path $repo 'tests') -ErrorAction Stop; $made = $true }
    catch {
        Write-Warning ("정션 못 만듦({0}) — C29 정션 확인 미측정" -f $_.Exception.GetType().Name)
        $script:Unmeasured.Add(('C29 정션 — 만들기 실패({0}) · 정션 확인 6건 안 함' -f $_.Exception.GetType().Name))
    }
    if ($made) {
        try {
            Test-CgCase 'C29' '정션 = 재분석 지점' ((([System.IO.File]::GetAttributes($jx)) -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
            $rr = Resolve-RgRealPath -Path (Join-Path $jx 'cg-p')
            Test-CgCase 'C29' '정션 실제 경로 = 가짜 저장소 tests 아래' ([string]::Equals($rr, (Join-Path $repo 'tests\cg-p'), [System.StringComparison]::OrdinalIgnoreCase))
            Test-CgCase 'C29' '순수 정션 = 거절' (-not (Test-RgReportDirOutside -Dir (Join-Path $jx 'cg-p') -RepoRoot $repo))
            $y = Invoke-CgRun -Runner $script:FakeRunner -ReportDir (Join-Path $jx 'cg-rd-f')
            Test-CgCase 'C29' 'e2e 정션 끊기 0' ($y.W.FakeCuts -eq 0)
            Test-CgCase 'C29' 'e2e 정션 폴더 0(실제 위치)' (-not [System.IO.Directory]::Exists((Join-Path $repo 'tests\cg-rd-f')))
            Test-CgCase 'C29' 'e2e 정션 판정 모드 줄' (($y.Lines -join "`n").Contains('판정 모드(보고서 폴더가 저장소 안'))
        } finally {
            try { [System.IO.Directory]::Delete($jx) } catch { Write-Warning ("정션 지우기 실패({0})" -f $_.Exception.GetType().Name) }
        }
    }
}

Invoke-CgCaseBlock 'C30' '[3-V 갈래2] V3 · 설치본 드라이브 뿌리 거절 · S-C 대상 = 실행 파일 정확 일치 · 끊기 직전 번호 · 경로 재확인' {
    # ⓐ 모듈 ConvertFrom-CgImagePath — 드라이브 뿌리 · 빈 값 · 상대 · UNC 공유 뿌리 = 없음 / 대조 = 설치본 폴더
    $wd = 'watchdog\HitPan.Watchdog.exe'
    Test-CgCase 'C30' 'ⓐ X:\watchdog\… = 없음(드라이브 뿌리)' ($null -eq (ConvertFrom-CgImagePath -ImagePath ('X:\' + $wd)))
    Test-CgCase 'C30' 'ⓐ 따옴표 · 인자 붙은 뿌리 = 없음' ($null -eq (ConvertFrom-CgImagePath -ImagePath ('"D:\' + $wd + '" --service')))
    Test-CgCase 'C30' 'ⓐ 빈 값 · 공백 = 없음' ($null -eq (ConvertFrom-CgImagePath -ImagePath '') -and $null -eq (ConvertFrom-CgImagePath -ImagePath '   '))
    Test-CgCase 'C30' 'ⓐ 상대 경로 = 없음' ($null -eq (ConvertFrom-CgImagePath -ImagePath $wd))
    Test-CgCase 'C30' 'ⓐ UNC 공유 뿌리 = 없음' ($null -eq (ConvertFrom-CgImagePath -ImagePath ('\\srv\share\' + $wd)))
    Test-CgCase 'C30' 'ⓐ 대조 = 설치본 폴더' ((ConvertFrom-CgImagePath -ImagePath ('"C:\Program Files\HitPan\' + $wd + '"')) -eq 'C:\Program Files\HitPan')
    # ⓑ 러너 Test-RgIsInstallApi(순수)
    $fx = Get-CgRunnerFunctions -Path $script:RunnerPath -Names @('Test-RgIsInstallApi')
    Test-CgCase 'C30' 'ⓑ 러너 Test-RgIsInstallApi 있음' ($fx.Count -eq 1)
    foreach ($t in @($fx.Values)) { . ([scriptblock]::Create($t)) }
    $api = 'api\HitPan.API.exe'
    Test-CgCase 'C30' 'ⓑ 정확 일치 = 예' (Test-RgIsInstallApi -Path ('C:\HitPan\' + $api) -InstallDir 'C:\HitPan')
    Test-CgCase 'C30' 'ⓑ 대소문자만 다름 · 설치본 끝 \ = 예' (Test-RgIsInstallApi -Path 'c:\hitpan\API\hitpan.api.EXE' -InstallDir 'C:\HitPan\')
    Test-CgCase 'C30' 'ⓑ 설치본 아래 다른 슬롯(측정 꼴) = 아니오' (-not (Test-RgIsInstallApi -Path ('C:\HitPan\slot2\' + $api) -InstallDir 'C:\HitPan'))
    Test-CgCase 'C30' 'ⓑ 설치본 바로 아래(api 폴더 아님) = 아니오' (-not (Test-RgIsInstallApi -Path 'C:\HitPan\HitPan.API.exe' -InstallDir 'C:\HitPan'))
    Test-CgCase 'C30' 'ⓑ 드라이브 뿌리 설치본(측정 꼴 D:\dev\bin) = 아니오' (-not (Test-RgIsInstallApi -Path 'D:\dev\bin\HitPan.API.exe' -InstallDir 'D:\') -and -not (Test-RgIsInstallApi -Path ('D:\' + $api) -InstallDir 'D:\'))
    Test-CgCase 'C30' 'ⓑ 이름 비슷한 이웃 폴더 = 아니오' (-not (Test-RgIsInstallApi -Path ('C:\HitPanX\' + $api) -InstallDir 'C:\HitPan'))
    Test-CgCase 'C30' 'ⓑ 경로 못 읽음 · 설치본 없음 = 아니오' (-not (Test-RgIsInstallApi -Path $null -InstallDir 'C:\HitPan') -and -not (Test-RgIsInstallApi -Path ('C:\HitPan\' + $api) -InstallDir $null))
    Test-CgCase 'C30' 'ⓑ UNC 설치본 = 아니오' (-not (Test-RgIsInstallApi -Path ('\\srv\share\HitPan\' + $api) -InstallDir '\\srv\share\HitPan'))
    # ⓒ 러너 실물 훅(GetApiProcesses · GetPortOwner)이 정확 일치를 씀 — 구문 트리(실물 훅 실행 0)
    $tk = $null; $pe = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($script:RunnerPath, [ref]$tk, [ref]$pe)
    $hookFn = @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'New-RgRealHooks' }, $true))[0]
    $hookCalls = if ($null -ne $hookFn) { @($hookFn.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) | ForEach-Object { $_.GetCommandName() }) } else { @() }
    Test-CgCase 'C30' 'ⓒ 실물 훅 = Test-RgIsInstallApi 2곳 · Test-RgUnderDir 0' (@($hookCalls | Where-Object { $_ -eq 'Test-RgIsInstallApi' }).Count -eq 2 -and @($hookCalls | Where-Object { $_ -eq 'Test-RgUnderDir' }).Count -eq 0)
    # ⓓ e2e 끊기 직전 재확인 — 기준 때 번호 5100 → 재확인 때 다른 번호(pid) / 같은 번호인데 설치본 API 아님(path) → 끊기 0 · 그 자리 정지
    foreach ($mode in @('pid', 'path')) {
        $r = Invoke-CgRun -Cfg @{ Api = @{ RecheckMode = $mode } }
        Assert-CgItem 'C30' $r 'S-C' ($LblC + '측정불가(인스턴스 불일치)')
        Test-CgCase 'C30' ('ⓓ ' + $mode + ' · S-C 끊기 0 · 재확인 줄 · 종료 1') ([int]$r.W.CutCalls['api'] -eq 0 -and (@($r.Lines) -contains '  끊기 직전 재확인: 프로세스 번호 · 경로가 기준 때와 다름 · 끊지 않음') -and $r.Exit -eq 1)
        Assert-CgItem 'C30' $r 'S-D' ($LblD + '반자동 — 미실행(앞 건 정지)')
    }
    Test-CgCase 'C30' 'ⓓ 대조 C16 = 재확인 같음 줄 · S-C 끊기 1' ((@($script:R16.Lines) -contains '  끊기 직전 재확인: 기준 때와 같음(번호 · 경로)') -and [int]$script:R16.W.CutCalls['api'] -eq 1)
}

Invoke-CgCaseBlock 'C31' '[3-V 갈래2] V4 · 「진짜 끊기 0」 정적 검사 — 러너 + 모듈 · 별칭 · .Kill( · taskkill · net stop · sc stop' {
    # ⓐ 검사기 자체(가짜 글 · 파싱만 · 실행 0) — 8꼴 전부 잡음
    $sample = "spsv x`nkill 1`nspps -Id 1`n`$p.Kill()`ntaskkill /F /IM x`nnet stop x`nsc stop x`nsc.exe stop x`n"
    $tk = $null; $pe = $null
    $sAst = [System.Management.Automation.Language.Parser]::ParseInput($sample, [ref]$tk, [ref]$pe)
    $sh = @(Get-CgCutHits $sAst)
    Test-CgCase 'C31' ('ⓐ 검사기 = 가짜 글 8꼴 전부 잡음(실제 ' + $sh.Count + ')') ($sh.Count -eq 8)
    # ⓑ 러너 · 모듈 오늘 판 — 끊기 꼴 0 · 시작 · 재시작 · 실행 명령 0
    $startBad = @('Start-Service', 'Restart-Service', 'Resume-Service', 'Set-Service', 'New-Service', 'Start-Process', 'saps', 'start', 'Invoke-Expression', 'iex', 'Invoke-Item', 'ii')
    $asts = @{}
    foreach ($pair in @(@('러너', $script:RunnerPath), @('모듈', $script:ModulePath))) {
        $tk = $null; $pe = $null
        $fAst = [System.Management.Automation.Language.Parser]::ParseFile($pair[1], [ref]$tk, [ref]$pe)
        $asts[$pair[0]] = $fAst
        Test-CgCase 'C31' ('ⓑ ' + $pair[0] + ' 구문 오류 0') (@($pe).Count -eq 0)
        $fh = @(Get-CgCutHits $fAst)
        Test-CgCase 'C31' ('ⓑ ' + $pair[0] + ' 끊기 꼴 0(실제 ' + ($fh -join ',') + ')') ($fh.Count -eq 0)
        $names = @($fAst.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ })
        $sb = @($names | Where-Object { $startBad -contains $_ })
        Test-CgCase 'C31' ('ⓑ ' + $pair[0] + ' 시작 · 재시작 · 실행 명령 0(실제 ' + ($sb -join ',') + ')') ($sb.Count -eq 0)
    }
    # ⓒ 모듈 — Stop-Service · Stop-Process 0 · .Start() = 읽기 전용 git(Invoke-CgGitRead)의 [System.Diagnostics.Process]::Start 1곳뿐 · .Continue() 0
    $mAst = $asts['모듈']
    $mNames = @($mAst.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ })
    Test-CgCase 'C31' 'ⓒ 모듈 Stop-Service · Stop-Process 0' (@($mNames | Where-Object { @('Stop-Service', 'Stop-Process') -contains $_ }).Count -eq 0)
    $mStart = @($mAst.FindAll({ param($n) $n -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and @('Start', 'Continue') -contains ([string]$n.Member.Extent.Text) }, $true))
    $p = if ($mStart.Count -eq 1) { $mStart[0].Parent } else { $null }
    while ($null -ne $p -and -not ($p -is [System.Management.Automation.Language.FunctionDefinitionAst])) { $p = $p.Parent }
    Test-CgCase 'C31' 'ⓒ 모듈 .Start() = Invoke-CgGitRead 안 [System.Diagnostics.Process]::Start 1곳뿐 · .Continue() 0' ($mStart.Count -eq 1 -and $mStart[0].Static -and [string]$mStart[0].Expression.Extent.Text -eq '[System.Diagnostics.Process]' -and $null -ne $p -and $p.Name -eq 'Invoke-CgGitRead')
}

Invoke-CgCaseBlock 'C32' '[3-V 갈래2] V9 · 손 기록 「누가」 16자 넘는 16진 꼴 거절' {
    $fx = Get-CgRunnerFunctions -Path $script:RunnerPath -Names @('Format-RgWho')
    Test-CgCase 'C32' 'ⓐ 러너 Format-RgWho 있음' ($fx.Count -eq 1)
    foreach ($t in @($fx.Values)) { . ([scriptblock]::Create($t)) }
    $hex34 = ('0123456789abcdef' * 3).Substring(0, 34)   # 보안상무 측정 꼴(34자 16진) — 실행 중에 만든다(파일 바이트에 자격증명 꼴 0)
    Test-CgCase 'C32' 'ⓐ 34자 16진 = 거절' ($null -eq (Format-RgWho -Who $hex34))
    Test-CgCase 'C32' 'ⓐ 17자 16진 = 거절' ($null -eq (Format-RgWho -Who 'abcdef01234567890'))
    Test-CgCase 'C32' 'ⓐ GUID 꼴(구분 글자 섞음) = 거절' ($null -eq (Format-RgWho -Who ([guid]::NewGuid().ToString())))
    Test-CgCase 'C32' 'ⓐ 공백 섞은 16진 18자 = 거절' ($null -eq (Format-RgWho -Who 'dead beef cafe babe 01'))
    Test-CgCase 'C32' 'ⓐ 대조 16자 16진 = 받음(경계)' ((Format-RgWho -Who 'deadbeefcafebabe') -eq 'deadbeefcafebabe')
    Test-CgCase 'C32' 'ⓐ 대조 사람 이름 = 받음' ((Format-RgWho -Who ' 홍길동 (PM) ') -eq '홍길동 (PM)' -and (Format-RgWho -Who 'PM-kim (QA)') -eq 'PM-kim (QA)' -and (Format-RgWho -Who '시험자') -eq '시험자')
    Test-CgCase 'C32' 'ⓐ 옛 거절 그대로(41자 · 줄바꿈 · 경로 꼴)' ($null -eq (Format-RgWho -Who ('a' * 41)) -and $null -eq (Format-RgWho -Who "x`ny") -and $null -eq (Format-RgWho -Who 'C:\a'))
    # ⓑ e2e — 손 기록 「누가」 = 34자 16진 → 손 기록 없음 = 반자동 미실행 · 그 글자 화면 · 보고서 · 실행 기록 0
    $r = Invoke-CgRun -Cfg @{ HandWho = $hex34 }
    Assert-CgItem 'C32' $r 'S-D' ($LblD + '반자동 — 미실행(손 기록 없음)')
    Test-CgCase 'C32' 'ⓑ 손 기록 부름 1 · 미합격 · 종료 1' ($r.W.HandCalls -eq 1 -and -not (Get-CgHeader $r).StartsWith('합격') -and $r.Exit -eq 1)
    $all = (@($r.Lines) -join "`n") + "`n" + [string]$r.ReportText + "`n" + [string]$r.HistText
    Test-CgCase 'C32' 'ⓑ 그 글자 0(화면 · 보고서 · 실행 기록)' (-not $all.Contains($hex34))
    Test-CgCase 'C32' 'ⓑ 실행 기록 TunnelSecret 0' (-not ([string]$r.HistText).Contains('TunnelSecret'))
}

# ─────────────────────────────────────────────── 경우 C33~C35 — (11/19) [4] 작업리뷰서 F8 · F9 · F10(변이 M02 · M05 · M07 이 살아남던 줄)
# 근거 docs/검증/20261119_WG1-53_갈래1_2_4_작업리뷰서.md §2 F8~F10 · §1-2 변이 표. 러너는 가짜 훅으로만(실물 0).

Invoke-CgCaseBlock 'C33' '[4] F8 · G5 원점 /health 200 아님(503 · 시간 초과) · 나머지 맞음 → 거절(G5) · 끊기 0 · 외부 GET 0' {
    foreach ($pair in @(@('ⓐ 503', '503'), @('ⓑ 시간 초과', 'timeout'))) {
        $r = Invoke-CgRun -Cfg @{ OriginFail = $pair[1] }
        Assert-CgRejected 'C33' $r @('G5')
        Test-CgCase 'C33' ($pair[0] + ' · G5 사유 = 원점 /health 200 아님') (@($r.Lines) -contains '  G5 거절 · 원점 /health 200 아님')
        Test-CgCase 'C33' ($pair[0] + ' · 원점 GET = 가드 읽기 1회뿐') ($r.W.LocalGets -eq 1)
        Test-CgCase 'C33' ($pair[0] + ' · 나머지 가드 칸 통과(G1~G4 · G4a · G6)') (@(@($r.Lines) | Where-Object { ([string]$_) -match '\A  (G1|G2|G3|G4|G4a|G6) 통과 · ' }).Count -eq 6)
    }
}

Invoke-CgCaseBlock 'C34' '[4] F9 · S-D 손 기록 → 관측 시작 간격 상한 2초 경계(2.000 완료 · 2.001 측정불가(관측 기록 무효))' {
    # 첫 회차 T 가 간격보다 커야 함(해석 9 둘째 조건) — 회차 T = 3~8(1~5 실패 · 6 복귀)로 간격 조건만 갈리게 한다
    $rd = Get-CgSdRounds
    foreach ($x in $rd) { $x.T = [double]$x.T + 2.0 }
    $v2 = Get-ScenarioVerdict -Id 'S-D' -Observation (New-CgObsSd -Rounds $rd -ObsStart $script:Base.AddSeconds(2))
    Assert-CgV 'C34' 'ⓐ 간격 2.000초(상한 그대로) = 완료' $v2 $SdDone5
    Test-CgCase 'C34' 'ⓐ 인계 0 · 상태 불명 0' (-not $v2.Handoff -and -not $v2.StateUnknown)
    $v3 = Get-ScenarioVerdict -Id 'S-D' -Observation (New-CgObsSd -Rounds $rd -ObsStart $script:Base.AddMilliseconds(2001))
    Assert-CgV 'C34' 'ⓑ 간격 2.001초 = 관측 기록 무효' $v3 '반자동 — 실패(측정불가(관측 기록 무효))'
    Test-CgCase 'C34' 'ⓑ 인계 · 상태 불명' ($v3.Handoff -and $v3.StateUnknown)
    # ⓒ 종합: 자동 3 PASS + ⓑ → 미합격 · 종료 1 / 자동 3 PASS + ⓐ → 합격 · 종료 0(대조)
    $std = Get-CgStdResults
    $s3 = Get-Summary -Results @($std.A, $std.B, $std.C, $v3)
    Test-CgCase 'C34' ('ⓒ 2.001 종합 = 미합격 · 종료 1(실제 「' + $s3.Header + '」)') (-not $s3.Header.StartsWith('합격') -and $s3.ExitCode -eq 1)
    $s2 = Get-Summary -Results @($std.A, $std.B, $std.C, $v2)
    Test-CgCase 'C34' 'ⓒ 대조 2.000 종합 = 합격 · 종료 0' ($s2.Header -eq $HdrPass -and $s2.ExitCode -eq 0)
}

Invoke-CgCaseBlock 'C35' '[4] F10 · 이름 풀이 주소 IPv6 루프백 ::1 → 외부 경로 아님 · S-A 측정불가(외부 경로 아님) · 끊기 0 · 외부 GET 0' {
    # ⓐ 판정 함수(순수) — ::1 · 풀어 쓴 꼴 · IPv4 섞인 목록 = 외부 아님 / 대조 = 문서용 대역 주소(IPv4 · IPv6)는 외부
    Test-CgCase 'C35' 'ⓐ ::1 = 외부 아님' (-not (Test-CgExternalAddress -Addresses @('::1')))
    Test-CgCase 'C35' 'ⓐ 0:0:0:0:0:0:0:1 = 외부 아님' (-not (Test-CgExternalAddress -Addresses @('0:0:0:0:0:0:0:1')))
    Test-CgCase 'C35' 'ⓐ 외부 IPv4 + ::1 섞임 = 외부 아님' (-not (Test-CgExternalAddress -Addresses @('203.0.113.10', '::1')))
    Test-CgCase 'C35' 'ⓐ 대조 IPv6 2001:db8::10 = 외부' (Test-CgExternalAddress -Addresses @('2001:db8::10'))
    Test-CgCase 'C35' 'ⓐ 대조 IPv4 203.0.113.10 = 외부' (Test-CgExternalAddress -Addresses @('203.0.113.10'))
    # ⓑ 러너(가짜 훅) — 이름 풀이 = ::1 → S-A 기준 응답에서 멈춤
    $b = Invoke-CgRun -Cfg @{ Addrs = @('::1') }
    Assert-CgItem 'C35' $b 'S-A' ($LblA + '측정불가(외부 경로 아님)')
    Assert-CgNoPass 'C35' $b 'S-A'
    Test-CgCase 'C35' 'ⓑ 끊기 0 · 외부 GET 0 · 종료 1' ($b.W.FakeCuts -eq 0 -and $b.W.ExtGets -eq 0 -and $b.Exit -eq 1)
    Assert-CgItem 'C35' $b 'S-B' ($LblB + '미실행(앞 건 정지)')
}

# ── 부가(경우 번호 밖 · 세부 확인에 셈)
Invoke-CgCaseBlock '부가' '훅 전부-아니면-멈춤 · 판정 모드 · 보고서 폴더 · 캐시 회피 · 러너 정적 검사' {
    $x = Invoke-CgRun -DropHook @('ReadScmEvents')
    Test-CgCase '부가' '훅 하나 빠짐 → 멈춤(실물로 안 메움)' (($null -ne $x.Error -or (($x.Lines -join "`n").Contains('시험 훅 누락'))) -and -not (@($x.Lines) -contains '가드(G1~G6 + G4a): 허용'))
    Test-CgCase '부가' '훅 빠짐 · 끊기 0' ($x.W.FakeCuts -eq 0)
    $j = Invoke-CgRun -NoConfirm
    Test-CgCase '부가' '판정 모드(가드 허용) · 끊기 0 · 손 0 · 파일 0' ($j.W.FakeCuts -eq 0 -and $j.W.HandCalls -eq 0 -and -not [System.IO.Directory]::Exists($j.ReportDir))
    Test-CgCase '부가' '판정 모드 · 상태 읽기(외부 GET 가드 통과 뒤)' ($j.W.ExtGets -ge 1 -and (Get-CgHeader $j).StartsWith('미합격(측정 0건)') -and $j.Exit -eq 1)
    # (11/19 [3-V] 갈래2 V6) 저장소 안 = 임시 폴더의 가짜 저장소(러너 사본) — 실제 저장소 경로 0
    $inRepo = Join-Path $script:FakeRepo ('tests\scenarios\comms-integrity\cg-no-write-' + $rnd.Substring(0, 8))
    $q = Invoke-CgRun -ReportDir $inRepo -Runner $script:FakeRunner
    Test-CgCase '부가' '보고서 폴더 = (가짜) 저장소 안 → 끊기 0 · 폴더 안 만듦' ($q.W.FakeCuts -eq 0 -and -not [System.IO.Directory]::Exists($inRepo) -and (($q.Lines -join "`n").Contains('판정 모드(보고서 폴더가 저장소 안')))
    Test-CgCase '부가' '가짜 저장소 = 임시 폴더 아래(실제 저장소 경로 아님)' ($script:FakeRepo.StartsWith($script:Root, [System.StringComparison]::OrdinalIgnoreCase))
    $r = $script:R16
    Test-CgCase '부가' ('캐시 회피 · no-cache 머리 빠짐 0 · 주소 이상 0(외부 GET ' + $r.W.ExtGets + ')') ($r.W.NoCacheMissing -eq 0 -and $r.W.BadUrls -eq 0 -and $r.W.ExtGets -gt 0)
    Test-CgCase '부가' '캐시 회피 · 쿼리값 매번 다름' ($r.W.Nonces.Count -eq $r.W.ExtGets -and @($r.W.Nonces | Select-Object -Unique).Count -eq $r.W.Nonces.Count)
    # 러너 정적 검사(구문 트리): 시작 · 재시작 명령 0 · 끊기 명령은 실물 끊기 함수 둘 안에만 · 첫 줄 = 진짜 끊기 계수
    $tk = $null; $pe = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($script:RunnerPath, [ref]$tk, [ref]$pe)
    Test-CgCase '부가' '러너 구문 오류 0' (@($pe).Count -eq 0)
    $cmds = @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true))
    $names = @($cmds | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ })
    foreach ($bad in @('Start-Service', 'Restart-Service', 'Resume-Service', 'Set-Service', 'New-Service', 'Start-Process', 'Invoke-Expression', 'Invoke-Item')) {
        Test-CgCase '부가' ('러너에 ' + $bad + ' 0') (@($names | Where-Object { $_ -ieq $bad }).Count -eq 0)
    }
    $members = @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.InvokeMemberExpressionAst] }, $true) | ForEach-Object { [string]$_.Member.Extent.Text })
    Test-CgCase '부가' '러너에 .Start() · .Continue() 0' (@($members | Where-Object { @('Start', 'Continue') -contains $_ }).Count -eq 0)
    foreach ($pair in @(@('Stop-Service', 'Invoke-RgRealStopService'), @('Stop-Process', 'Invoke-RgRealStopProcesses'))) {
        $hits = @($cmds | Where-Object { $_.GetCommandName() -ieq $pair[0] })
        $fn = $null
        if ($hits.Count -eq 1) {
            $p = $hits[0].Parent
            while ($null -ne $p -and -not ($p -is [System.Management.Automation.Language.FunctionDefinitionAst])) { $p = $p.Parent }
            $fn = $p
        }
        $first = if ($null -ne $fn) { [string]$fn.Body.EndBlock.Statements[0].Extent.Text } else { '' }
        Test-CgCase '부가' ($pair[0] + ' = ' + $pair[1] + ' 안 1곳 · 첫 줄 진짜 끊기 계수') ($hits.Count -eq 1 -and $null -ne $fn -and $fn.Name -eq $pair[1] -and $first -eq '$global:HitPanCommsRealCutCalls++')
    }
    $scs = @($cmds | Where-Object { $_.GetCommandName() -ieq 'sc.exe' })
    Test-CgCase '부가' 'sc.exe 는 qfailure(조회)만' ($scs.Count -eq 1 -and [string]$scs[0].CommandElements[1].Extent.Text -eq 'qfailure')
}

Invoke-CgCaseBlock 'C17' '보고서 · 화면 · 실행 기록(전체 런) → 자격증명 모양 · 명령줄 · 외부 노출 금지어 0' {
    $hay = ($script:AllText.ToArray()) -join "`n"
    Test-CgCase 'C17' ('모은 글 있음(' + $script:AllText.Count + '덩이)') ($script:AllText.Count -ge 40)
    Test-CgCase 'C17' '도메인 원문 0' ($hay.IndexOf($script:Id.Domain, [System.StringComparison]::OrdinalIgnoreCase) -lt 0 -and $hay.IndexOf($script:Id.Domain2, [System.StringComparison]::OrdinalIgnoreCase) -lt 0)
    Test-CgCase 'C17' '테넌트 코드 0' ($hay.IndexOf($script:Id.Tenant, [System.StringComparison]::OrdinalIgnoreCase) -lt 0)
    Test-CgCase 'C17' '기기 식별값 0' ($hay.IndexOf($script:Id.Guid, [System.StringComparison]::OrdinalIgnoreCase) -lt 0)
    Test-CgCase 'C17' '임시 폴더 경로(설치본 · 마커) 0' ($hay.IndexOf($script:Root, [System.StringComparison]::OrdinalIgnoreCase) -lt 0)
    Test-CgCase 'C17' '표지 문자열(응답 · 이벤트 글 · 실패 동작 출력) 0' (-not $hay.Contains($script:Mark))
    Test-CgCase 'C17' '명령줄 칸 · 실행 파일 0' ($hay -notmatch 'COMMAND_LINE' -and $hay -notmatch '(?i)\.exe\b')
    Test-CgCase 'C17' '주소(URL) 0' ($hay -notmatch '(?i)https?://')
    Test-CgCase 'C17' '관측자 주소 0' (-not $hay.Contains($script:ObserverIp))
    $i = 0
    foreach ($rx in $script:BannedRx) { $i++; Test-CgCase 'C17' ('외부 노출 금지어 ' + $i + ' 0' + (Get-CgWhere $rx)) ($hay -notmatch $rx) }
    $i = 0
    foreach ($rx in $script:CredRx) { $i++; Test-CgCase 'C17' ('자격증명 모양 ' + $i + ' 0' + (Get-CgWhere $rx)) ($hay -notmatch $rx) }
}

} finally {
    Remove-CgTree $script:Root
}

# ─────────────────────────────────────────────── 끝

# (11/19 [3-V] 갈래2 V5) 블록 뒤에 늦게 들어온 어긋남까지 다시 셈 · 안 돈 경우 = 불일치 · 세부 확인 수 하한
foreach ($k in @($script:CaseOk.Keys)) { Update-CgCaseOk $k }
$caseIds = @($script:CaseOk.Keys | Where-Object { $_ -ne '부가' })
$okCases = @($caseIds | Where-Object { $script:CaseOk[$_] }).Count
$notRun = @($script:CaseOk.Keys | Where-Object { -not $script:CaseRan[$_] })
$minOk = ($script:DetailTotal -ge $script:MinDetail)
$real = [int]$global:HitPanCommsRealCutCalls
if ($script:Fails.Count -gt 0) {
    Write-Host ''
    Write-Host ('어긋난 세부 확인 {0}건:' -f $script:Fails.Count)
    foreach ($f in $script:Fails) { Write-Host ('  - ' + $f) }
}
if ($notRun.Count -gt 0) { Write-Host ('실행 안 된 경우(불일치로 셈): ' + ($notRun -join ' · ')) }
foreach ($u in $script:Unmeasured) { Write-Host ('미측정: ' + $u) }
if (-not $script:CaseOk['부가']) { Write-Host '부가 확인(경우 번호 밖) 어긋남 — 세부 확인 칸에 셈' }
Write-Host ('세부 확인 하한 {0} — 실제 {1} · {2}' -f $script:MinDetail, $script:DetailTotal, $(if ($minOk) { '맞음' } else { '모자람(종료 1)' }))
Write-Host ('G4a git 읽기(시험 시간 · F1): 실제 {0} · 재사용 {1}' -f [int]$script:GitCacheStat['Real'], [int]$script:GitCacheStat['Reuse'])
Write-Host ('걸린 시간 {0}초' -f [math]::Round($sw.Elapsed.TotalSeconds))
Write-Host ('Test-CommsGuard 끝 — 경우 {0}/{1} 일치 · 세부 확인 {2}/{3} · 진짜 끊기 호출 {4}' -f $okCases, $caseIds.Count, $script:DetailPass, $script:DetailTotal, $real)
if ($okCases -eq $caseIds.Count -and $caseIds.Count -eq $script:CaseMax -and $script:CaseOk['부가'] -and $script:DetailPass -eq $script:DetailTotal -and $minOk -and $real -eq 0) { exit 0 }
exit 1
