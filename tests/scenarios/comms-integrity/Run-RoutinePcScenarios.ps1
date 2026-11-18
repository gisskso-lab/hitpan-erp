#Requires -Version 5.1
<#
  Run-RoutinePcScenarios.ps1 — W-G1-53 ㉡ 새 러너(루틴 PC 장애 시험 · 도구 범위 4 ID: S-A · S-B · S-C · S-D)

  근거(주소만 · 내용은 거기에):
    설계       docs/설계/설치배포/20261118_WG1-53_장애시험도구재설계_설계.md  §3-3 · §4 · §5 · §5-1a · §5-3 · §5-4
    작업지시서 docs/운영기록/20261118작_WG1-53_장애시험도구재설계_작업지시서.md  §2 ㉡
    개발명세서 docs/개발/설치배포/20261118_개발명세서_WG1-53_장애시험도구재설계.md  §7 「새 창 구현 메모」
    판정 모듈  CommsGuard.psm1(같은 폴더) — Get-EnvSnapshot · Get-GuardDecision · Get-ScenarioVerdict · Get-Summary

  쓰는 법:
    -Confirm 없음 = 가드 판정 + 상태 읽기만(끊기 0 · 파일 쓰기 0). 가드 거절이면 외부 GET 도 0.
    -Confirm      = 가드 7칸(G1~G6 + G4a) 전부 통과 + 보고서 폴더가 저장소 밖일 때만 S-A → S-B → S-C → S-D 한 건씩.
    -Hooks        = 시험 전용(Test-CommsGuard.ps1). 전부 줘야 한다 — 하나라도 빠지면 실물 함수로 메우지 않고 멈춘다.
                    (가짜 스냅숏 + 실물 끊기 같은 섞인 조합이 생기지 않게)

  지키는 것:
    - 대상 선택(이 PC 를 열지)을 막는 장치 = 가드 7칸뿐. 거절이면 끊기 0 · 외부 GET 0 · 파일 쓰기 0.
    - 한 건씩(R1) · FAIL · 측정불가면 그 자리 정지(R2) · 손으로 살리는 코드 0(R3 — 시작 · 재시작 명령 0).
    - 멈추기 명령 오류를 삼키지 않는다. 멈추기 실패 · 멈춤 미관측 · 끊김 미반영 · FAIL = 그 자리에서 인계 쪽지
      (사람이 할 일 안내 · 대상 상태 불명 경고 · §3-3 · §5-4).
    - 외부 GET 은 가드 통과 뒤에만 · 외부 주소 = G4 를 통과한 PRIMARY_DOMAIN 만 · 원점 = G5 와 같은 API_PORT 만(인자 · 기본값 0).
      캐시 회피 = no-cache 머리 + 매번 다른 쿼리값 · GET 1회 상한 2초(목표 · 대조 같은 값).
    - 화면 · 보고서에 도메인 원문 · 경로 · 명령줄 · 응답 본문 · 이벤트 글 0 — 해시 앞 8자리 · 예/아니오 · UTC 시각 · 버전 문자열만.
    - PASS 문구 = 「5분 안 복구됨」. 복구 주체는 별도 칸(판정 미반영).
    - 보고서 · 실행 기록(run-history.jsonl)은 -Confirm + 가드 허용 + 보고서 폴더가 저장소 밖일 때만 쓴다.
    - 빈 catch 0(헌법 #15) — catch 마다 Write-Warning / Write-Verbose(예외 종류만 · 메시지 0).
  인코딩: UTF-8 BOM(Windows PowerShell 5.1 한글 오독 방지).
#>
[CmdletBinding()]
param(
    [switch]$Confirm,
    [string]$ReportDir = 'C:\HitPanTest\reports',
    [hashtable]$Hooks
)

Set-StrictMode -Off

$script:RgHookNames = @(
    'GetSnapshot', 'Now', 'Sleep', 'HttpGet', 'ResolveHost', 'GetServiceInfo', 'GetApiProcesses', 'GetPortOwner',
    'StopService', 'StopProcesses', 'ReadWatchdogEvents', 'ReadScmEvents', 'ReadFailureActions', 'HandRecord'
)
$script:RgStartCalls = 0          # 도구 자신의 시작 · 재시작 호출 수(E3) — 그런 코드가 없으므로 늘 0
$script:RgLog = New-Object System.Collections.Generic.List[string]
$script:RgNoCache = @{ 'Cache-Control' = 'no-cache, no-store'; 'Pragma' = 'no-cache' }
$script:H = $null
$script:C = $null
$script:Ctx = $null
$script:HistPath = $null

# ─────────────────────────────────────────────── 실물 끊기(실물 훅에서만 · 시험은 -Hooks 로 가짜만)

function Invoke-RgRealStopService {
    param([Parameter(Mandatory)][string]$Name)
    $global:HitPanCommsRealCutCalls++
    # -NoWait = 멈춤은 관측으로 확인(설계 §5-1 ③). -Force 안 씀 = 종속 서비스를 같이 끊지 않는다(종속이 있으면 멈추기 실패).
    # 오류는 삼키지 않는다(-ErrorAction Stop → 부르는 쪽에서 「측정불가(멈추기 실패)」).
    Stop-Service -Name $Name -NoWait -ErrorAction Stop -Confirm:$false
}

function Invoke-RgRealStopProcesses {
    param([Parameter(Mandatory)][int[]]$Pids)
    $global:HitPanCommsRealCutCalls++
    # -Force = 다른 계정 소유 프로세스의 확인 질문만 끈다(무인 실행). 오류는 삼키지 않는다.
    Stop-Process -Id $Pids -Force -ErrorAction Stop -Confirm:$false
}

function Test-RgUnderDir {
    param([AllowNull()][string]$Path, [AllowNull()][string]$Dir)
    if ([string]::IsNullOrWhiteSpace($Path) -or [string]::IsNullOrWhiteSpace($Dir)) { return $false }
    try {
        $root = [System.IO.Path]::GetFullPath($Dir).TrimEnd('\') + '\'
        return [System.IO.Path]::GetFullPath($Path).StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)
    } catch {
        Write-Verbose ("경로 비교 실패({0}) — 설치본 폴더 하위 아님으로 처리" -f $_.Exception.GetType().Name)
        return $false
    }
}

function New-RgRealHooks {
    # 실물 공급자. 읽기 = 서비스 상태 · 프로세스 · 포트 · 이벤트 로그 · 실패 동작 조회 · HTTP GET. 쓰기 = 끊기 두 함수뿐.
    return @{
        GetSnapshot        = { Get-EnvSnapshot -NowUtc ([datetime]::UtcNow) -Verbose:($VerbosePreference -eq 'Continue') }
        Now                = { [datetime]::UtcNow }
        Sleep              = { param([int]$Ms) if ($Ms -gt 0) { Start-Sleep -Milliseconds $Ms } }
        HttpGet            = { param([string]$Url, [int]$TimeoutSec, [hashtable]$Headers) Invoke-CgHttpGet -Url $Url -TimeoutSec $TimeoutSec -Headers $Headers }
        ResolveHost        = { param([string]$HostName) @([System.Net.Dns]::GetHostAddresses($HostName) | ForEach-Object { $_.IPAddressToString }) }
        GetServiceInfo     = {
            param([string]$Name)
            $st = (Get-Service -Name $Name -ErrorAction Stop).Status.ToString()
            $w = Get-CimInstance -ClassName Win32_Service -Filter ("Name='{0}'" -f ($Name -replace "'", "''")) -ErrorAction Stop -Verbose:$false
            $procId = if ($null -ne $w) { [int]$w.ProcessId } else { 0 }
            return @{ Status = $st; Pid = $procId }
        }
        GetApiProcesses    = {
            param([string]$InstallDir)
            $list = New-Object System.Collections.Generic.List[object]
            foreach ($p in @(Get-Process -Name 'HitPan.API' -ErrorAction SilentlyContinue)) {
                $path = $null
                try { $path = $p.Path } catch { Write-Verbose ("프로세스 경로 못 읽음({0})" -f $_.Exception.GetType().Name) }
                $list.Add(@{ Pid = [int]$p.Id; UnderInstall = (Test-RgUnderDir -Path $path -Dir $InstallDir) })
                $path = $null
            }
            return $list.ToArray()   # 원소로 내보냄(부르는 쪽이 @() 로 받음)
        }
        GetPortOwner       = {
            param([int]$Port, [string]$InstallDir)
            $c = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction Stop -Verbose:$false)[0]
            if ($null -eq $c) { return $null }
            $p = Get-Process -Id ([int]$c.OwningProcess) -ErrorAction Stop
            $path = $null
            try { $path = $p.Path } catch { Write-Verbose ("프로세스 경로 못 읽음({0})" -f $_.Exception.GetType().Name) }
            $r = @{ Pid = [int]$p.Id; UnderInstall = (Test-RgUnderDir -Path $path -Dir $InstallDir) }
            $path = $null
            return $r
        }
        StopService        = { param([string]$Name) Invoke-RgRealStopService -Name $Name }
        StopProcesses      = { param([int[]]$Pids) Invoke-RgRealStopProcesses -Pids $Pids }
        ReadWatchdogEvents = {
            param([datetime]$SinceUtc)
            try {
                $ev = @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = 'HitPanWatchdog'; StartTime = $SinceUtc.ToLocalTime() } -ErrorAction Stop)
            } catch {
                if ([string]$_.FullyQualifiedErrorId -like 'NoMatchingEventsFound*') { return @{ Ok = $true; Items = @() } }
                Write-Warning ("워치독 이벤트 로그 못 읽음({0})" -f $_.Exception.GetType().Name)
                return @{ Ok = $false; Items = @() }
            }
            # 글은 모듈이 맞춰 보기만 한다(담지 않음 · 3인 R8)
            return @{ Ok = $true; Items = @($ev | ForEach-Object { @{ TimeUtc = $_.TimeCreated.ToUniversalTime(); Text = [string]$_.Message } }) }
        }
        ReadScmEvents      = {
            # E2: 시스템 로그 서비스 제어 관리자 7036 중 「{서비스 이름}/4」(실행 중 진입) — 이진 칸(UTF-16)으로 맞춤(표시 언어 무관 · 실측 = H3)
            param([string]$Name, [datetime]$SinceUtc)
            try {
                $ev = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; ProviderName = 'Service Control Manager'; Id = 7036; StartTime = $SinceUtc.ToLocalTime() } -ErrorAction Stop)
            } catch {
                if ([string]$_.FullyQualifiedErrorId -like 'NoMatchingEventsFound*') { return @{ Ok = $true; Items = @() } }
                Write-Warning ("서비스 제어 관리자 기록 못 읽음({0})" -f $_.Exception.GetType().Name)
                return @{ Ok = $false; Items = @() }
            }
            $want = $Name + '/4'
            $items = New-Object System.Collections.Generic.List[object]
            foreach ($e in $ev) {
                try {
                    $bin = [string]([xml]$e.ToXml()).Event.EventData.Binary
                    if ($bin -and $bin.Length % 2 -eq 0) {
                        $bytes = New-Object byte[] ($bin.Length / 2)
                        for ($i = 0; $i -lt $bytes.Length; $i++) { $bytes[$i] = [Convert]::ToByte($bin.Substring($i * 2, 2), 16) }
                        $s = [System.Text.Encoding]::Unicode.GetString($bytes).TrimEnd([char]0)
                        if ($s -ieq $want) { $items.Add(@{ TimeUtc = $e.TimeCreated.ToUniversalTime() }) }
                    }
                } catch { Write-Verbose ("7036 한 건 해석 실패({0})" -f $_.Exception.GetType().Name) }
            }
            return @{ Ok = $true; Items = $items.ToArray() }
        }
        ReadFailureActions = {
            # E4: 서비스 「실패 시 복구 동작」 조회(읽기만). 출력 원문은 모듈이 맞춰 보기만 한다(명령줄 칸 포함 · 담지 않음)
            param([string]$Name)
            $o = @(& sc.exe qfailure $Name 2>&1)
            if ($LASTEXITCODE -ne 0) { return $null }
            return (($o | ForEach-Object { [string]$_ }) -join "`n")
        }
        HandRecord         = {
            $who = Read-Host '무효화를 한 사람(역할 · 이름) — 하지 않았으면 빈 칸으로 Enter'
            if ([string]::IsNullOrWhiteSpace($who)) { return $null }
            return @{ Who = $who.Trim(); Utc = [datetime]::UtcNow }
        }
    }
}

# ─────────────────────────────────────────────── 도우미

function Write-RgLine {
    param([AllowEmptyString()][string]$Text = '')
    Write-Host $Text
    $script:RgLog.Add($Text)
}

function Get-RgRepoRoot { return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..')) }

function Test-RgReportDirOutside {
    # 보고서 폴더 = 저장소 밖(PM 결재). 상대 경로 · 저장소 안 · 해석 불가 = 거절.
    param([AllowNull()][string]$Dir, [Parameter(Mandatory)][string]$RepoRoot)
    if ([string]::IsNullOrWhiteSpace($Dir)) { return $false }
    try {
        if (-not [System.IO.Path]::IsPathRooted($Dir)) { return $false }
        return -not (Test-RgUnderDir -Path ([System.IO.Path]::GetFullPath($Dir).TrimEnd('\') + '\') -Dir $RepoRoot)
    } catch {
        Write-Warning ("보고서 폴더 경로 해석 실패({0}) — 거절" -f $_.Exception.GetType().Name)
        return $false
    }
}

function Wait-RgUntil {
    param([Parameter(Mandatory)][datetime]$Utc)
    $ms = [math]::Ceiling(($Utc - (& $script:H.Now)).TotalMilliseconds)
    if ($ms -gt 0) { & $script:H.Sleep ([int]$ms) }
}

function Get-RgNonceUrl {
    param([Parameter(Mandatory)][string]$Path)
    return ('https://{0}{1}?cg={2}' -f $script:Ctx.Domain, $Path, [guid]::NewGuid().ToString('N'))
}

function Invoke-RgGet {
    # GET 하나(훅). 예외 = Status 0(시간 초과 · 연결 실패와 같은 칸). 본문은 해석 함수만 보고 버린다.
    param([Parameter(Mandatory)][string]$Url, [int]$TimeoutSec, [AllowNull()][hashtable]$Headers)
    try {
        $r = & $script:H.HttpGet $Url $TimeoutSec $Headers
        if ($null -eq $r) { return @{ Status = 0; Body = $null } }
        return $r
    } catch {
        Write-Verbose ("GET 실패({0})" -f $_.Exception.GetType().Name)
        return @{ Status = 0; Body = $null }
    }
}

function Get-RgExternal {
    # 외부 경로(§5-1a S-A): 이름 풀이 주소가 외부인지(ⓐ) → 외부일 때만 /api/app-version GET(ⓑ). 외부 아님 = GET 0.
    $addrs = @()
    if ($script:Ctx.DomainOk) {
        try { $addrs = @(& $script:H.ResolveHost $script:Ctx.Domain | ForEach-Object { [string]$_ }) }
        catch { Write-Verbose ("이름 풀이 실패({0})" -f $_.Exception.GetType().Name) }
    }
    if (-not (Test-CgExternalAddress -Addresses ([string[]]$addrs))) { return @{ AddrOk = $false; Status = 0; Version = $null } }
    $resp = Invoke-RgGet -Url (Get-RgNonceUrl -Path '/api/app-version') -TimeoutSec $script:C.GetTimeoutSec -Headers $script:RgNoCache
    $p = ConvertFrom-CgAppVersion -Response $resp
    $resp = $null
    return @{ AddrOk = $true; Status = [int]$p.Status; Version = $p.Version }
}

function Get-RgControl {
    # 관측자 연결 대조(S-D): 같은 도메인 /cdn-cgi/trace · 같은 캐시 회피 · 같은 2초. 결과 = 예/아니오만(본문 0).
    # 훅 예외 = 기록 없음($null) → 모듈이 「측정불가(관측자 연결 불명)」 으로 셈.
    param([bool]$AddrOk)
    if (-not $AddrOk) { return @{ Ok = $false } }
    try {
        $resp = & $script:H.HttpGet (Get-RgNonceUrl -Path '/cdn-cgi/trace') $script:C.GetTimeoutSec $script:RgNoCache
        $ok = Test-CgControlTrace -Response $resp
        $resp = $null
        return @{ Ok = [bool]$ok }
    } catch {
        Write-Verbose ("대조 GET 기록 없음({0})" -f $_.Exception.GetType().Name)
        return $null
    }
}

function Get-RgOrigin {
    # 원점(§5-1a S-B · S-C): http://127.0.0.1:{API_PORT}/health + 그 포트를 듣는 프로세스가 설치본 폴더 하위인지(예/아니오).
    $resp = Invoke-RgGet -Url ('http://127.0.0.1:{0}/health' -f $script:Ctx.Port) -TimeoutSec $script:C.GetTimeoutSec -Headers $null
    $h = ConvertFrom-CgOriginHealth -Response $resp
    $resp = $null
    $owner = $null
    try { $owner = & $script:H.GetPortOwner $script:Ctx.Port $script:Ctx.InstallDir }
    catch { Write-Verbose ("포트 주인 못 읽음({0})" -f $_.Exception.GetType().Name) }
    $ownerPid = $null
    if ($null -ne $owner -and $owner.Pid) { $ownerPid = [int]$owner.Pid }
    return @{
        Ok = [bool]$h.Ok; Status = [int]$h.Status; DatabaseOk = $h.DatabaseOk; Version = $h.Version
        UnderInstall = ($null -ne $owner -and $owner.UnderInstall -eq $true); Pid = $ownerPid
    }
}

function Get-RgServiceInfo {
    param([Parameter(Mandatory)][string]$Name)
    try {
        $i = & $script:H.GetServiceInfo $Name
        if ($null -eq $i) { return @{ Status = $null; Pid = $null } }
        $p = $null
        if ($i.Pid) { $p = [int]$i.Pid }
        return @{ Status = [string]$i.Status; Pid = $p }
    } catch {
        Write-Verbose ("서비스 상태 못 읽음({0})" -f $_.Exception.GetType().Name)
        return @{ Status = $null; Pid = $null }
    }
}

function Get-RgApiProcs {
    # 설치본 폴더 하위 HitPan.API 프로세스(번호 · 하위 여부). 못 읽음 = $null.
    try { return , @(& $script:H.GetApiProcesses $script:Ctx.InstallDir | Where-Object { $null -ne $_ }) }
    catch {
        Write-Verbose ("API 프로세스 못 읽음({0})" -f $_.Exception.GetType().Name)
        return $null
    }
}

function Add-RgHistory {
    param([Parameter(Mandatory)][datetime]$Utc, [Parameter(Mandatory)][string]$Key)
    $line = ([ordered]@{ utc = (Format-CgUtc -Value $Utc); key = $Key } | ConvertTo-Json -Compress)
    try { [System.IO.File]::AppendAllText($script:HistPath, $line + "`n", (New-Object System.Text.UTF8Encoding($false))) }
    catch { Write-Warning ("실행 기록 쓰기 실패({0}) — 다음 실행의 쿨다운 셈 ⓐ 가 낮게 나올 수 있음(ⓑ 이벤트 셈은 그대로)" -f $_.Exception.GetType().Name) }
}

function Get-RgCooldown {
    # 설계 §5-1 ①: ⓐ 보고서 폴더 실행 기록 · ⓑ 워치독 이벤트(서비스 대상만 · 설계 공백 1 — S-C · S-D 는 ⓑ = 0) 중 큰 값.
    # 이벤트 로그 못 읽음 = 미실행(쿨다운 보호 · fail-closed · 대상 무관). 실행 기록 못 읽음 = 미실행(쿨다운 보호).
    param([Parameter(Mandatory)]$Cat)
    $now = & $script:H.Now
    $lines = @()
    if ([System.IO.File]::Exists($script:HistPath)) {
        try { $lines = [System.IO.File]::ReadAllLines($script:HistPath, [System.Text.Encoding]::UTF8) }
        catch {
            Write-Warning ("실행 기록 못 읽음({0}) — 쿨다운 보호" -f $_.Exception.GetType().Name)
            return [pscustomobject]@{ Blocked = $true; Count = 0; Reason = '쿨다운 보호' }
        }
    }
    $hist = Measure-CgHistory -Lines ([string[]]$lines) -Key $Cat.Key -NowUtc $now
    $ev = $null
    try { $ev = & $script:H.ReadWatchdogEvents ($now.AddMinutes(-$script:C.CooldownWindowMin)) }
    catch { Write-Warning ("워치독 이벤트 못 읽음({0})" -f $_.Exception.GetType().Name) }
    $evOk = ($null -ne $ev -and $ev.Ok -eq $true)
    $evCount = 0
    if ($evOk -and $Cat.Kind -eq 'service') {
        $m = Measure-CgWatchdogEvents -Events @($ev.Items) -Service $Cat.Target -SinceUtc ($now.AddMinutes(-$script:C.CooldownWindowMin))
        $evCount = @($m.Restarted).Count + @($m.Failures).Count
    }
    $ev = $null
    return (Get-CgCooldownDecision -HistoryCount $hist -EventCount $evCount -EventReadable $evOk)
}

function Get-RgLastRecovery {
    param([Parameter(Mandatory)]$Cat)
    if ($Cat.Kind -ne 'service') { return '해당 없음(서비스 증거 아님)' }
    try {
        $now = & $script:H.Now
        $since = $now.AddMinutes(-$script:C.CooldownWindowMin)
        $ev = & $script:H.ReadWatchdogEvents $since
        if ($null -eq $ev -or $ev.Ok -ne $true) { return '못 읽음' }
        $r = @((Measure-CgWatchdogEvents -Events @($ev.Items) -Service $Cat.Target -SinceUtc $since).Restarted)
        $ev = $null
        if ($r.Count -eq 0) { return '없음(지난 60분)' }
        return (Format-CgUtc -Value $r[-1])
    } catch {
        Write-Warning ("워치독 마지막 복구 기록 못 읽음({0})" -f $_.Exception.GetType().Name)
        return '못 읽음'
    }
}

function Get-RgEvidence {
    # 복구 주체 E1~E4(설계 §5-1a 끝 줄) — 예/아니오 · UTC 시각만 남긴다(이벤트 글 · 설정 출력 원문 0 · 3인 R8).
    param([Parameter(Mandatory)]$Cat, [Parameter(Mandatory)][datetime]$T0Utc)
    $e = @{ E1Utc = $null; E1Failure = $null; E2Utc = $null; E3 = $script:RgStartCalls; E4 = 'unknown' }
    try {
        $ev = & $script:H.ReadWatchdogEvents $T0Utc
        if ($null -ne $ev -and $ev.Ok -eq $true) {
            $m = Measure-CgWatchdogEvents -Events @($ev.Items) -Service $Cat.Target -SinceUtc $T0Utc
            $rs = @($m.Restarted); $fs = @($m.Failures)
            if ($rs.Count -gt 0) {
                $e.E1Utc = $rs[0]
                $e.E1Failure = (@($fs | Where-Object { $_ -le $rs[0] }).Count -gt 0)
            } else { $e.E1Failure = ($fs.Count -gt 0) }
        }
        $ev = $null
    } catch { Write-Warning ("E1 못 읽음({0})" -f $_.Exception.GetType().Name) }
    try {
        $sc = & $script:H.ReadScmEvents $Cat.Target $T0Utc
        if ($null -ne $sc -and $sc.Ok -eq $true) {
            $ts = @(@($sc.Items) | ForEach-Object { ConvertTo-CgUtc -Value $_.TimeUtc } | Where-Object { $null -ne $_ -and $_ -ge $T0Utc } | Sort-Object)
            if ($ts.Count -gt 0) { $e.E2Utc = $ts[0] }
        }
    } catch { Write-Warning ("E2 못 읽음({0})" -f $_.Exception.GetType().Name) }
    try { $e.E4 = ConvertFrom-CgFailureActions -Text ([string](& $script:H.ReadFailureActions $Cat.Target)) }
    catch { Write-Warning ("E4 못 읽음({0})" -f $_.Exception.GetType().Name) }
    return $e
}

function Get-RgBaseline {
    # 끊기 직전 기준 응답 1회(§5-1a 공통). 안 맞으면 끊지 않는다 → 측정불가(기준 응답 없음 · 인스턴스 불일치 · 외부 경로 아님) + R2.
    param([Parameter(Mandatory)]$Cat)
    switch ($Cat.Id) {
        'S-A' {
            $ext = Get-RgExternal
            if (-not $ext.AddrOk) { return @{ Ok = $false; Reason = '외부 경로 아님' } }
            $o = Get-RgOrigin
            if ($ext.Status -ne 200 -or -not $ext.Version) { return @{ Ok = $false; Reason = '기준 응답 없음' } }
            if (-not $o.Ok -or -not $o.Version) { return @{ Ok = $false; Reason = '기준 응답 없음' } }
            if ([string]$ext.Version -cne [string]$o.Version) { return @{ Ok = $false; Reason = '인스턴스 불일치' } }
            return @{ Ok = $true; Reason = $null; Version = $ext.Version; OriginPid = $o.Pid }
        }
        'S-B' {
            $o = Get-RgOrigin
            if (-not $o.Ok -or $o.DatabaseOk -ne $true -or -not $o.Version) { return @{ Ok = $false; Reason = '기준 응답 없음' } }
            if ($o.UnderInstall -ne $true) { return @{ Ok = $false; Reason = '인스턴스 불일치' } }
            return @{ Ok = $true; Reason = $null; Version = $o.Version; OriginPid = $o.Pid }
        }
        'S-C' {
            $o = Get-RgOrigin
            if (-not $o.Ok -or -not $o.Version) { return @{ Ok = $false; Reason = '기준 응답 없음' } }
            if ($o.UnderInstall -ne $true) { return @{ Ok = $false; Reason = '인스턴스 불일치' } }
            $procs = Get-RgApiProcs
            $old = @()
            if ($null -ne $procs) { $old = @($procs | Where-Object { $_.UnderInstall -eq $true } | ForEach-Object { [int]$_.Pid }) }
            if ($old.Count -eq 0) { return @{ Ok = $false; Reason = '인스턴스 불일치' } }
            return @{ Ok = $true; Reason = $null; Version = $o.Version; OriginPid = $o.Pid; OldPids = $old }
        }
    }
    return @{ Ok = $false; Reason = '기준 응답 없음' }
}

function Get-RgSample {
    param([Parameter(Mandatory)]$Cat, [Parameter(Mandatory)][datetime]$CutUtc, [int[]]$OldPids)
    $sm = @{ T = ((& $script:H.Now) - $CutUtc).TotalSeconds }
    switch ($Cat.Id) {
        'S-A' {
            $si = Get-RgServiceInfo -Name $Cat.Target
            $sm.Svc = $si.Status; $sm.SvcPid = $si.Pid
            $sm.Ext = Get-RgExternal
        }
        'S-B' {
            $si = Get-RgServiceInfo -Name $Cat.Target
            $sm.Svc = $si.Status; $sm.SvcPid = $si.Pid
            $sm.Origin = Get-RgOrigin
        }
        'S-C' {
            $sm.Origin = Get-RgOrigin
            $procs = Get-RgApiProcs
            # 못 읽음 = 남아 있음으로 셈(멈춤을 거짓으로 보지 않게)
            if ($null -eq $procs) { $sm.OldAlive = $true }
            else { $sm.OldAlive = (@($procs | Where-Object { @($OldPids) -contains [int]$_.Pid }).Count -gt 0) }
        }
    }
    return $sm
}

function Get-RgTargetName {
    param([Parameter(Mandatory)]$Cat)
    if ($Cat.Kind -eq 'semi') { return '터널 자격증명(사람이 무효화)' }
    return [string]$Cat.Target
}

function Write-RgHandoff {
    # 인계 쪽지(§5-4) + 사람이 할 일 안내(§3-3) — 정지 자리에서 화면 · 보고서 둘 다. 비밀 · 경로 · 명령줄 0.
    param([Parameter(Mandatory)]$V, [Parameter(Mandatory)]$Cat, [int]$CooldownCount)
    $last = if ($V.LastState) { [string]$V.LastState } else { '관측 없음' }
    $t0 = if ($null -ne $V.T0Utc) { Format-CgUtc -Value $V.T0Utc } else { '없음(멈춤 관측 전)' }
    $el = if ($null -ne $V.ElapsedSec) { [string]$V.ElapsedSec } else { '0' }
    Write-RgLine ('  ── 인계 쪽지(§5-4) · {0} [{1}]' -f $V.Id, $V.Label)
    Write-RgLine ('  대상: {0}' -f (Get-RgTargetName -Cat $Cat))
    Write-RgLine ('  마지막 관측 상태: {0}' -f $last)
    Write-RgLine ('  t0: {0}' -f $t0)
    Write-RgLine ('  경과: {0}초(끊기 · 손 기록 뒤)' -f $el)
    Write-RgLine ('  워치독 마지막 복구 기록 시각: {0}' -f (Get-RgLastRecovery -Cat $Cat))
    Write-RgLine ('  쿨다운 셈: {0}' -f $CooldownCount)
    Write-RgLine ('  도구 시작 · 재시작 호출: {0}' -f $script:RgStartCalls)
    if ($V.StateUnknown) { Write-RgLine '  경고: 대상 상태 불명 · 사람 확인 필요' }
    Write-RgLine '  사람이 할 일(읽기만 · 도구는 손으로 살리지 않음):'
    Write-RgLine '    ① -Confirm 없이 이 러너를 다시 돌려 가드 판정(G5 · 서비스 상태 · 원점 /health)과 외부 응답을 읽는다(외부 GET 은 가드 7칸 통과 뒤에만)'
    Write-RgLine '    ② 서비스 상태 · 워치독 이벤트 로그(원본 HitPanWatchdog)를 읽는다'
    Write-RgLine '    ③ StopPending 고착이면 프로세스를 죽이지 않고 기록만 한다'
    Write-RgLine '  안전 복구 순서: 워치독 자가 복구를 t0 + 15분까지 기다림 → 넘기면 복구 담당 → PM 보고 → #29 사전 승인 범위 안에서만 사람이 도구 밖에서 시작 1회(누가 · 언제 기록) · 범위 밖이면 손대지 않고 대표 보고'
    Write-RgLine '  종료 조건: G5 기준 건강이 다시 전부 맞음 확인 또는 복구 담당이 이 쪽지를 받았다는 기록(누가 · 언제) · 그 전엔 시험 종료 아님'
}

function Write-RgResult {
    param([Parameter(Mandatory)]$V)
    $line = '  결과: ' + [string]$V.Display
    if (@($V.Notes).Count -gt 0) { $line += ' · ' + (@($V.Notes) -join ' · ') }
    Write-RgLine $line
    if ($V.Actor) { Write-RgLine ('  복구 주체(판정 미반영): {0} · {1}' -f $V.Actor, $V.ActorDetail) }
}

function Format-RgWho {
    param([AllowNull()] $Who)
    $w = ([string]$Who).Trim()
    if ($w -notmatch '\A[\p{L}\p{N} ._()\-]{1,40}\z') { return $null }
    return $w
}

# ─────────────────────────────────────────────── 한 건 — 자동(S-A · S-B · S-C)

function Invoke-RgAuto {
    param([Parameter(Mandatory)]$Cat)
    $c = $script:C
    $id = $Cat.Id
    Write-RgLine ('{0} [{1}] 대상 {2}' -f $id, $Cat.Label, (Get-RgTargetName -Cat $Cat))
    $res = @{ V = $null; Cooldown = 0 }

    # 대상 확인(§4): 서비스 = 설치본 워치독 감시 목록에 있어야 · S-C = SLOT_INDEX 있어야(재기동 작업)
    if ($Cat.Kind -eq 'service') {
        if ($null -eq $script:Ctx.WdList -or @($script:Ctx.WdList) -notcontains $Cat.Target) {
            $res.V = Get-ScenarioVerdict -Id $id -Observation @{ Skip = '감시 대상 아님' }
            return $res
        }
    } elseif ($null -eq $script:Ctx.Slot) {
        $res.V = Get-ScenarioVerdict -Id $id -Observation @{ Skip = '재기동 작업 없음' }
        return $res
    }

    # ① 쿨다운
    $cd = Get-RgCooldown -Cat $Cat
    $res.Cooldown = [int]$cd.Count
    Write-RgLine ('  쿨다운 셈: {0}(보고서 실행 기록 · 워치독 이벤트 중 큰 값)' -f $cd.Count)
    if ($cd.Blocked) {
        $res.V = Get-ScenarioVerdict -Id $id -Observation @{ Skip = [string]$cd.Reason }
        return $res
    }

    # 기준 응답(끊기 직전 1회)
    $b = Get-RgBaseline -Cat $Cat
    if ($b.Ok) { Write-RgLine ('  기준 응답: 맞음 · 버전 {0}' -f $b.Version) }
    else { Write-RgLine ('  기준 응답: 안 맞음({0}) · 끊지 않음' -f $b.Reason) }
    $obs = @{ Baseline = $b }
    if (-not $b.Ok) {
        $res.V = Get-ScenarioVerdict -Id $id -Observation $obs
        return $res
    }

    # ② 끊기 — 오류를 삼키지 않는다
    $oldPids = @()
    if ($Cat.Kind -eq 'service') { $obs.PreSvcPid = (Get-RgServiceInfo -Name $Cat.Target).Pid }
    else { $oldPids = @($b.OldPids) }
    $cutUtc = & $script:H.Now
    $obs.CutUtc = $cutUtc
    $okCut = $false
    try {
        if ($Cat.Kind -eq 'service') { $null = & $script:H.StopService $Cat.Target }
        else { $null = & $script:H.StopProcesses ([int[]]$oldPids) }
        $okCut = $true
    } catch {
        Write-Warning ("멈추기 명령 실패({0}) — 측정불가(멈추기 실패)" -f $_.Exception.GetType().Name)
    }
    $obs.Cut = @{ Issued = $true; Ok = $okCut }
    Add-RgHistory -Utc $cutUtc -Key $Cat.Key
    Write-RgLine ('  끊기: {0} · {1}' -f $(if ($okCut) { '명령 보냄' } else { '명령 실패' }), (Format-CgUtc -Value $cutUtc))

    # ③ 멈춤 관측 · ④ 복구 대기 — 1초 간격 · 회차마다 -Partial 판정 · 정해지면 멈춤
    if ($okCut) {
        $samples = New-Object System.Collections.Generic.List[object]
        $cap = $c.StopWindowSec + $c.RecoverWindowSec + 60
        for ($i = 1; ; $i++) {
            Wait-RgUntil -Utc $cutUtc.AddSeconds($i)
            $sm = Get-RgSample -Cat $Cat -CutUtc $cutUtc -OldPids ([int[]]$oldPids)
            $samples.Add($sm)
            $obs.Samples = $samples.ToArray()
            $pv = Get-ScenarioVerdict -Id $id -Observation $obs -Partial
            if ($null -ne $pv) { break }
            if ([double]$sm.T -ge $cap) { break }
        }
    }
    $v = Get-ScenarioVerdict -Id $id -Observation $obs
    # 복구 주체 증거(E1~E4 · 판정 미반영) — t0 가 있고 PASS · FAIL 일 때만 모아 한 번 더 판정
    if ($Cat.Kind -eq 'service' -and $null -ne $v.T0Utc -and @('PASS', 'FAIL') -contains $v.Column) {
        $obs.Evidence = Get-RgEvidence -Cat $Cat -T0Utc $v.T0Utc
        $v = Get-ScenarioVerdict -Id $id -Observation $obs
    }
    $res.V = $v
    return $res
}

# ─────────────────────────────────────────────── 한 건 — 반자동(S-D)

function Invoke-RgSemi {
    param([Parameter(Mandatory)]$Cat)
    $c = $script:C
    $id = $Cat.Id
    Write-RgLine ('{0} [{1}] 대상 {2}' -f $id, $Cat.Label, (Get-RgTargetName -Cat $Cat))
    $res = @{ V = $null; Cooldown = 0 }

    $cd = Get-RgCooldown -Cat $Cat
    $res.Cooldown = [int]$cd.Count
    Write-RgLine ('  쿨다운 셈: {0}(보고서 실행 기록 · 이벤트 셈 해당 없음)' -f $cd.Count)
    if ($cd.Blocked) {
        $res.V = Get-ScenarioVerdict -Id $id -Observation @{ Skip = [string]$cd.Reason }
        return $res
    }

    # 손 안내 전 기준 1회 — 목표(외부 /api/app-version = 원점 version) + 대조(/cdn-cgi/trace)
    $ext = Get-RgExternal
    $o = Get-RgOrigin
    $targetOk = ($ext.AddrOk -and $ext.Status -eq 200 -and $ext.Version -and $o.Version -and ([string]$ext.Version -ceq [string]$o.Version))
    $ctl = Get-RgControl -AddrOk ([bool]$ext.AddrOk)
    $sdb = @{ TargetOk = [bool]$targetOk; ControlOk = ($null -ne $ctl -and $ctl.Ok -eq $true); Version = $ext.Version }
    $obs = @{ SdBaseline = $sdb }
    Write-RgLine ('  기준: 목표 {0} · 대조 {1}' -f $(if ($sdb.TargetOk) { '맞음' } else { '안 맞음' }), $(if ($sdb.ControlOk) { '예' } else { '아니오' }))
    if (-not ($sdb.TargetOk -and $sdb.ControlOk)) {
        Write-RgLine '  기준이 안 맞아 손 안내를 하지 않음'
        $res.V = Get-ScenarioVerdict -Id $id -Observation $obs
        return $res
    }

    # 손 안내 → 손 기록(누가 · 언제). 도구는 무효화 · 되살림을 하지 않는다(읽기 · 관측만).
    Write-RgLine '  손 안내: 정해진 절차대로 시험 PC 터널 자격증명을 사람이 무효화한 뒤, 한 사람(역할 · 이름)을 적는다. 도구는 무효화 · 되살림을 하지 않는다.'
    $hand = $null
    try { $hand = & $script:H.HandRecord } catch { Write-Warning ("손 기록 못 받음({0})" -f $_.Exception.GetType().Name) }
    $who = $null; $handUtc = $null
    if ($null -ne $hand) { $who = Format-RgWho -Who $hand.Who; $handUtc = ConvertTo-CgUtc -Value $hand.Utc }
    if (-not $who -or $null -eq $handUtc) {
        Write-RgLine '  손 기록: 없음'
        $obs.Hand = $null
        $res.V = Get-ScenarioVerdict -Id $id -Observation $obs
        return $res
    }
    $obs.Hand = @{ Who = $who; Utc = $handUtc }
    Add-RgHistory -Utc $handUtc -Key $Cat.Key
    Write-RgLine ('  손 기록: {0} · {1}' -f $who, (Format-CgUtc -Value $handUtc))

    # (11/19 [3-V] A3) 관측 시작 시각 — 손 기록 바로 뒤 · 첫 회차 전. 모듈이 「손 기록 ≤ 관측 시작 ≤ 손 기록 + 2초」 로 관측 창을 손 기록에 묶는다.
    $obs.ObsStartUtc = & $script:H.Now

    # 관측 창 — 1초 회차마다 목표 GET 바로 뒤 대조 GET 1회(1:1) · 회차마다 -Partial 판정
    $rounds = New-Object System.Collections.Generic.List[object]
    $cap = $c.SdWindowSec + $c.RecoverWindowSec + 60
    for ($i = 1; ; $i++) {
        Wait-RgUntil -Utc $handUtc.AddSeconds($i)
        $t = ((& $script:H.Now) - $handUtc).TotalSeconds
        $tg = Get-RgExternal
        $tOk = ((Get-CgExtState -Ext $tg -BaselineVersion ([string]$sdb.Version)) -eq 'ok')
        $tEnd = & $script:H.Now
        $cStart = & $script:H.Now
        $cr = Get-RgControl -AddrOk ([bool]$tg.AddrOk)
        $rounds.Add(@{ T = $t; Target = @{ Ok = $tOk }; Control = $cr; Skew = ($cStart - $tEnd).TotalSeconds })
        $obs.Rounds = $rounds.ToArray()
        $pv = Get-ScenarioVerdict -Id $id -Observation $obs -Partial
        if ($null -ne $pv) { break }
        if ($t -ge $cap) { break }
    }
    $res.V = Get-ScenarioVerdict -Id $id -Observation $obs
    return $res
}

# ─────────────────────────────────────────────── 시작

$modulePath = Join-Path $PSScriptRoot 'CommsGuard.psm1'
if ($PSBoundParameters.ContainsKey('Hooks')) {
    if ($null -eq $Hooks) { throw '시험 훅이 비었음 — 실물 함수로 메우지 않고 멈춤' }
    $missing = @($script:RgHookNames | Where-Object { -not $Hooks.ContainsKey($_) -or $Hooks[$_] -isnot [scriptblock] })
    if ($missing.Count -gt 0) { throw ('시험 훅 누락: {0} — 실물 함수로 메우지 않고 멈춤' -f ($missing -join ', ')) }
    $script:H = @{}
    foreach ($k in $script:RgHookNames) { $script:H[$k] = $Hooks[$k] }
    if ($Hooks.ContainsKey('ModulePath') -and -not [string]::IsNullOrWhiteSpace([string]$Hooks.ModulePath)) { $modulePath = [string]$Hooks.ModulePath }
} else {
    $script:H = New-RgRealHooks
}
# -Verbose:$false = 모듈 경로 · 함수 목록이 자세히 출력(실행 로그)에 실리지 않게
Import-Module -Name $modulePath -Force -ErrorAction Stop -Verbose:$false
$script:C = Get-CgConstant

$repoRoot = Get-RgRepoRoot
$now = & $script:H.Now
$snap = & $script:H.GetSnapshot
$guard = Get-GuardDecision -Snapshot $snap -NowUtc $now

Write-RgLine ('Run-RoutinePcScenarios · 루틴 PC 장애 시험(W-G1-53) · 모드 {0}' -f $(if ($Confirm) { '실행(-Confirm)' } else { '판정만(-Confirm 없음)' }))
Write-RgLine ('시각: {0}' -f (Format-CgUtc -Value $now))
Write-RgLine ('가드(G1~G6 + G4a): {0}' -f $(if ($guard.Allowed) { '허용' } else { '거절' }))
foreach ($ck in @($guard.Checks)) { Write-RgLine ('  {0} {1} · {2}' -f $ck.Id, $(if ($ck.Pass) { '통과' } else { '거절' }), $ck.Reason) }
if ($guard.DomainHashPrefix) { Write-RgLine ('  허용목록 대조 해시 앞 8자리: 도메인 {0} · 테넌트 {1}' -f $guard.DomainHashPrefix, $guard.TenantHashPrefix) }

$catalog = @(Get-CgScenarioCatalog)
$results = New-Object System.Collections.Generic.List[object]
$writeFiles = $false

if (-not $guard.Allowed) {
    Write-RgLine '외부 GET 안 함(가드 거절) · 끊기 0 · 파일 쓰기 0'
    foreach ($cat in $catalog) { $results.Add((Get-ScenarioVerdict -Id $cat.Id -Observation @{ Skip = '가드' })) }
} else {
    $dv = $snap.DbConf.Values
    $dom = ([string]$dv['PRIMARY_DOMAIN']).Trim()
    $script:Ctx = @{
        Domain     = $dom
        DomainOk   = ($dom -match '\A[A-Za-z0-9](?:[A-Za-z0-9-]{0,62})(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,62}))+\z')
        Port       = (ConvertTo-CgPort -Value $dv['API_PORT'])
        Slot       = (ConvertTo-CgSlot -Value $dv['SLOT_INDEX'])
        InstallDir = $snap.InstallDir
        WdList     = $snap.WatchdogServices
    }
    $dom = $null
    $skip = $null
    if (-not $Confirm) { $skip = '판정 모드(-Confirm 없음)' }
    elseif (-not (Test-RgReportDirOutside -Dir $ReportDir -RepoRoot $repoRoot)) {
        $skip = '판정 모드(보고서 폴더가 저장소 안 · 상대 경로 · 거절)'
    } else {
        try { $null = [System.IO.Directory]::CreateDirectory($ReportDir) }
        catch {
            Write-Warning ("보고서 폴더 못 만듦({0})" -f $_.Exception.GetType().Name)
            $skip = '판정 모드(보고서 폴더 못 만듦)'
        }
    }
    if ($null -ne $skip) {
        # 상태 확인(§5-4 ①) — 읽기만 · 끊기 0 · 파일 쓰기 0
        Write-RgLine ('{0} · 끊기 0 · 파일 쓰기 0 · 상태 읽기만' -f $skip)
        $ext = Get-RgExternal
        $o = Get-RgOrigin
        $extTxt = if (-not $ext.AddrOk) { '외부 경로 아님' } elseif ($ext.Status -eq 200 -and $ext.Version -and $o.Version -and ([string]$ext.Version -ceq [string]$o.Version)) { '맞음(원점과 같은 판)' } else { '안 맞음' }
        Write-RgLine ('  외부 응답: {0} · 원점 /health: {1}' -f $extTxt, $(if ($o.Ok) { '200' } else { '200 아님' }))
        foreach ($cat in $catalog) { $results.Add((Get-ScenarioVerdict -Id $cat.Id -Observation @{ Skip = $skip })) }
    } else {
        $writeFiles = $true
        $script:HistPath = Join-Path $ReportDir 'run-history.jsonl'
        $halted = $false
        foreach ($cat in $catalog) {
            if ($halted) {
                $results.Add((Get-ScenarioVerdict -Id $cat.Id -Observation @{ Skip = '앞 건 정지' }))
                Write-RgLine ('{0} [{1}] 미실행(앞 건 정지)' -f $cat.Id, $cat.Label)
                continue
            }
            if ($cat.Kind -eq 'semi') { $r = Invoke-RgSemi -Cat $cat } else { $r = Invoke-RgAuto -Cat $cat }
            $v = $r.V
            $results.Add($v)
            Write-RgResult -V $v
            if ($v.Handoff) { Write-RgHandoff -V $v -Cat $cat -CooldownCount $r.Cooldown }
            if ($v.Halt -or ($cat.Kind -eq 'semi' -and $v.Handoff)) {
                Write-RgLine '  그 자리 정지(R2) · 다음 건은 끊지 않음'
                $halted = $true
            }
        }
    }
}

$sum = Get-Summary -Results $results.ToArray()
Write-Host ''
foreach ($l in @($sum.Text)) { Write-Host $l }

if ($writeFiles) {
    $name = 'routine-pc-{0}.txt' -f (& $script:H.Now).ToString("yyyyMMdd'T'HHmmss'Z'", [Globalization.CultureInfo]::InvariantCulture)
    $body = (@($sum.Text) + @('') + $script:RgLog.ToArray()) -join "`r`n"
    try {
        [System.IO.File]::WriteAllText((Join-Path $ReportDir $name), $body + "`r`n", (New-Object System.Text.UTF8Encoding($true)))
        Write-Host ('보고서: {0}(보고서 폴더)' -f $name)
    } catch { Write-Warning ("보고서 쓰기 실패({0}) — 화면 출력이 유일한 기록" -f $_.Exception.GetType().Name) }
}
exit ([int]$sum.ExitCode)
