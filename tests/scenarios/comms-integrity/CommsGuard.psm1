#Requires -Version 5.1
<#
  CommsGuard.psm1 — W-G1-53 ㉠ 가드 · 판정 모듈

  근거(주소만 · 내용은 거기에):
    설계     docs/설계/설치배포/20261118_WG1-53_장애시험도구재설계_설계.md  §3 · §4 · §5 · §5-1a · §5-2 · §5-3 · §7-1
    작업지시서 docs/운영기록/20261118작_WG1-53_장애시험도구재설계_작업지시서.md  §2 ㉠

  설계 함수(이름 그대로): Get-EnvSnapshot(읽기만) · Get-GuardDecision(G1~G6 + G4a · fail-closed · 사유 목록)
                        Get-ScenarioVerdict(판정 6칸) · Get-Summary(4 ID 전부 맞을 때만 「합격」)
  나머지 *-Cg* 함수 = 위 넷과 러너 · 시험이 함께 쓰는 도우미.

  지키는 것:
    - Get-EnvSnapshot 은 읽기만 한다(파일 · 레지스트리 · 서비스 상태 · git 개체 읽기 · 로컬 127.0.0.1 /health GET).
      쓰기 · 서비스 조작 · git 개체 쓰기 · 가져오기 · 체크아웃 0.
    - db.conf 는 키 화이트리스트(PRIMARY_DOMAIN · TENANT_CODE · SLOT_INDEX · API_PORT)만 꺼낸다.
      한 줄씩 읽고, 화이트리스트 키로 시작하지 않는 줄은 키 이름도 값도 변수에 옮기지 않고 버린다.
      오류 · 자세히 출력엔 줄 번호만(줄 내용 0). hitpan-tunnel.conf · bootstrap.conf · tenant.conf 는 열지 않는다.
    - 어떤 수집이 실패해도 「허용」 으로 떨어지지 않는다(fail-closed).
    - 시험 공급자(Providers)를 주면 전부 줘야 한다 — 하나라도 빠지면 실물 읽기로 메우지 않고 멈춘다.
    - 빈 catch 0(헌법 #15): catch 마다 Write-Warning / Write-Verbose 한 줄 — 예외 종류만(메시지 0 · 경로 · 도메인 유출 방지).
    - 보고서 · 판정 문구에 복구 주체 단정 문구 0(H1 3인 F6) · 「17 중 n」 0(#32) · 머리 줄에 「통신」 · 「#27」 0.
  인코딩: UTF-8 BOM(Windows PowerShell 5.1 한글 오독 방지 — README_테스트환경구축.md 인코딩 절).
#>

Set-StrictMode -Off

$script:CgConst = @{
    MarkerPath            = 'C:\HitPanTest\HITPAN_TEST_ENV.marker'
    DbConfWhitelist       = @('PRIMARY_DOMAIN', 'TENANT_CODE', 'SLOT_INDEX', 'API_PORT')
    ExpiresMaxHours       = 72
    GuardServices         = @('HitPanWatchdog', 'cloudflared', 'MariaDB')
    WatchdogService       = 'HitPanWatchdog'
    WatchdogExeName       = 'HitPan.Watchdog.exe'
    AllowlistRelPath      = 'tests/scenarios/comms-integrity/test-pc-allowlist.json'
    DemoDomainPattern     = 'demo\.hitpan\.kr'
    StopWindowSec         = 30
    RecoverWindowSec      = 300
    SdWindowSec           = 300
    SdStreak              = 5
    GetTimeoutSec         = 2
    MaxSkewSec            = 2
    GuardHealthTimeoutSec = 5
    CooldownWindowMin     = 60
    CooldownMaxPrior      = 4
    E1AfterE2Sec          = 30
    HandoffWaitMin        = 15
    UtcFormat             = "yyyy-MM-dd'T'HH:mm:ss'Z'"
}

# 도구 범위 4 ID(설계 §2). Target = 워치독 감시 목록에서 찾을 이름(기대값) — 목록에 없으면 「측정불가(감시 대상 아님)」.
$script:CgCatalog = @(
    [pscustomobject]@{ Id = 'S-A'; Kind = 'service'; Target = 'cloudflared'; Key = 'svc:cloudflared'; Label = '외부 경로' }
    [pscustomobject]@{ Id = 'S-B'; Kind = 'service'; Target = 'MariaDB';     Key = 'svc:MariaDB';     Label = '로컬 원점' }
    [pscustomobject]@{ Id = 'S-C'; Kind = 'process'; Target = 'HitPan.API';  Key = 'http:HitPan.API'; Label = '로컬 원점' }
    [pscustomobject]@{ Id = 'S-D'; Kind = 'semi';    Target = $null;         Key = 'TunnelSecret';    Label = '외부 경로 · 반자동(손 1회)' }
)

$script:CgColumns = @('PASS', 'FAIL', '측정불가(가드)', '측정불가', '미실행', '반자동')
$script:CgActorMatch = '워치독 재시작 기록과 상태 전이 일치 · 단독 원인 귀속 미확정'
$script:CgActorUnknown = '불명'

# ─────────────────────────────────────────────── 기본 도우미

function Get-CgConstant { [CmdletBinding()] param() return $script:CgConst.Clone() }

function Get-CgScenarioCatalog { [CmdletBinding()] param() return $script:CgCatalog }

function Format-CgUtc {
    [CmdletBinding()]
    param([AllowNull()] $Value)
    if ($null -eq $Value) { return '없음' }
    $d = [datetime]$Value
    if ($d.Kind -eq [DateTimeKind]::Local) { $d = $d.ToUniversalTime() }
    return $d.ToString($script:CgConst.UtcFormat, [Globalization.CultureInfo]::InvariantCulture)
}

function ConvertTo-CgUtc {
    # 문자열(UTC ISO 8601 · 끝 Z · 초 단위) 또는 DateTime → UTC DateTime. 형식이 틀리면 $null.
    [CmdletBinding()]
    param([AllowNull()] $Value)
    if ($null -eq $Value) { return $null }
    if ($Value -is [datetime]) {
        if ($Value.Kind -eq [DateTimeKind]::Local) { return $Value.ToUniversalTime() }
        return [datetime]::SpecifyKind($Value, [DateTimeKind]::Utc)
    }
    $s = [string]$Value
    if ($s -notmatch '\A\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z\z') { return $null }
    $out = [datetime]::MinValue
    $styles = [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal
    if ([datetime]::TryParseExact($s, $script:CgConst.UtcFormat, [Globalization.CultureInfo]::InvariantCulture, $styles, [ref]$out)) {
        return [datetime]::SpecifyKind($out, [DateTimeKind]::Utc)
    }
    return $null
}

function ConvertTo-CgSha256Hex {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Text))
        return (-join ($hash | ForEach-Object { $_.ToString('x2') }))
    } finally { $sha.Dispose() }
}

function Get-CgIdentityHash {
    # 신원 값(도메인 · 테넌트 코드 · 기기 식별값) 정규화 = 앞뒤 공백 제거 + 소문자(Invariant) → UTF-8 → SHA-256 소문자 hex.
    # 빈 값 · 공백뿐 = $null(빈 문자열의 해시를 만들지 않는다 — 허용목록 우회 차단).
    [CmdletBinding()]
    param([AllowNull()][AllowEmptyString()][string]$Value)
    if ($null -eq $Value) { return $null }
    $n = $Value.Trim().ToLowerInvariant()
    if ($n.Length -eq 0) { return $null }
    return ConvertTo-CgSha256Hex -Text $n
}

function Test-CgHex {
    [CmdletBinding()]
    param([AllowNull()][string]$Value, [int[]]$Length = @(40, 64))
    if ([string]::IsNullOrEmpty($Value)) { return $false }
    if ($Value -notmatch '\A[0-9a-fA-F]+\z') { return $false }
    return ($Length -contains $Value.Length)
}

function ConvertTo-CgPort {
    [CmdletBinding()]
    param([AllowNull()][string]$Value)
    if ($null -eq $Value) { return $null }
    $v = $Value.Trim()
    if ($v -notmatch '\A\d{1,5}\z') { return $null }
    $n = [int]$v
    if ($n -lt 1 -or $n -gt 65535) { return $null }
    return $n
}

function ConvertTo-CgSlot {
    [CmdletBinding()]
    param([AllowNull()][string]$Value)
    if ($null -eq $Value) { return $null }
    $v = $Value.Trim()
    if ($v -notmatch '\A\d{1,3}\z') { return $null }
    $n = [int]$v
    if ($n -lt 1) { return $null }
    return $n
}

# ─────────────────────────────────────────────── 읽기(수집) 도우미 — 전부 읽기만

function Read-CgMarker {
    # 마커 = KEY=VALUE 줄. 인식 키 ENV · MACHINE · EXPIRES 각 1회만(중복 = 무효 · 여러 줄 마커 우회 차단).
    # 값은 형식이 맞을 때만 담는다(MACHINE = hex 64 · EXPIRES = UTC ISO 8601 끝 Z) — 보고서에 임의 글자가 옮겨지지 않게.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $r = [ordered]@{ Exists = $false; EnvTest = $false; Machine = $null; Expires = $null; Invalid = @() }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $r }
    $r.Exists = $true
    $invalid = New-Object System.Collections.Generic.List[string]
    $seen = @{}
    $sr = $null
    $line = $null
    try {
        $sr = New-Object System.IO.StreamReader($Path, [System.Text.Encoding]::UTF8, $true)
        $lineNo = 0
        while ($null -ne ($line = $sr.ReadLine())) {
            $lineNo++
            $t = $line.Trim()
            $line = $null
            if ($t.Length -eq 0 -or $t.StartsWith('#')) { continue }
            $eq = $t.IndexOf('=')
            if ($eq -le 0) { $invalid.Add(("줄 {0} 형식 오류" -f $lineNo)); continue }
            $k = $t.Substring(0, $eq).Trim().ToUpperInvariant()
            if (@('ENV', 'MACHINE', 'EXPIRES') -notcontains $k) { continue }
            $v = $t.Substring($eq + 1).Trim()
            if ($seen.ContainsKey($k)) { $invalid.Add(("{0} 중복" -f $k)); continue }
            $seen[$k] = $true
            switch ($k) {
                'ENV'     { $r.EnvTest = ($v -eq 'TEST') }
                'MACHINE' { if ($v -match '\A[0-9a-fA-F]{64}\z') { $r.Machine = $v.ToLowerInvariant() } }
                'EXPIRES' { if ($null -ne (ConvertTo-CgUtc -Value $v)) { $r.Expires = $v } }
            }
        }
    } catch {
        Write-Warning ("마커 읽기 실패({0}) — G1 거절로 처리" -f $_.Exception.GetType().Name)
        $invalid.Add('읽기 실패')
    } finally {
        if ($null -ne $sr) { $sr.Dispose() }
        $line = $null
    }
    $r.Invalid = $invalid.ToArray()
    return $r
}

function Read-CgDbConf {
    # 키 화이트리스트만. 화이트리스트 키로 시작하는 줄인지 정규식으로만 본다 — 아닌 줄은 키 이름 · 값을 꺼내지 않는다.
    # 같은 화이트리스트 키가 두 번 = 그 값 $null(워치독과 다른 값을 볼 위험 · fail-closed).
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $values = [ordered]@{}
    foreach ($k in $script:CgConst.DbConfWhitelist) { $values[$k] = $null }
    $r = [ordered]@{ Found = $false; ReadOk = $false; Values = $values; BadLineCount = 0; DuplicateKeys = 0 }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $r }
    $r.Found = $true
    $patterns = @{}
    foreach ($k in $script:CgConst.DbConfWhitelist) {
        $patterns[$k] = New-Object System.Text.RegularExpressions.Regex(('\A\s*' + [regex]::Escape($k) + '\s*=(?<v>.*)\z'), 'IgnoreCase')
    }
    $seen = @{}
    $sr = $null
    $line = $null
    try {
        $sr = New-Object System.IO.StreamReader($Path, [System.Text.Encoding]::UTF8, $true)
        $lineNo = 0
        while ($null -ne ($line = $sr.ReadLine())) {
            $lineNo++
            $head = $line.TrimStart()
            if ($head.Length -eq 0 -or $head.StartsWith('#')) { $head = $null; $line = $null; continue }
            $head = $null
            $hit = $null
            $m = $null
            foreach ($k in $script:CgConst.DbConfWhitelist) {
                $mm = $patterns[$k].Match($line)
                if ($mm.Success) { $hit = $k; $m = $mm; break }
            }
            if ($null -eq $hit) {
                if ($line.IndexOf('=') -le 0) {
                    $r.BadLineCount++
                    Write-Verbose ("db.conf 줄 {0}: 형식 오류(내용 생략)" -f $lineNo)
                } else {
                    Write-Verbose ("db.conf 줄 {0}: 화이트리스트 밖 → 버림" -f $lineNo)
                }
                $line = $null
                continue
            }
            $line = $null
            $val = $m.Groups['v'].Value.Trim()
            $m = $null
            if ($seen.ContainsKey($hit)) {
                $values[$hit] = $null
                $r.DuplicateKeys++
                Write-Verbose ("db.conf 줄 {0}: 화이트리스트 키 중복 → 그 키 값 버림" -f $lineNo)
            } else {
                $seen[$hit] = $true
                $values[$hit] = $val
            }
            $val = $null
        }
        $r.ReadOk = $true
    } catch {
        Write-Warning ("db.conf 읽기 실패({0}) — G4 거절로 처리" -f $_.Exception.GetType().Name)
    } finally {
        if ($null -ne $sr) { $sr.Dispose() }
        $line = $null
    }
    # 중복된 키는 끝까지 $null 로 둔다
    return $r
}

function Read-CgWatchdogServices {
    # 설치본 {app}\watchdog\appsettings.json 의 Watchdog.Processes.Services(읽기만). 못 읽음 = $null.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    try {
        $raw = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8).TrimStart([char]0xFEFF)
        $j = $raw | ConvertFrom-Json
        $raw = $null
        $list = New-Object System.Collections.Generic.List[string]
        foreach ($s in @($j.Watchdog.Processes.Services)) {
            if ($s -is [string] -and $s -match '\A[A-Za-z0-9_.-]{1,64}\z') { $list.Add($s) }
        }
        return , ($list.ToArray())
    } catch {
        Write-Warning ("워치독 감시 목록 읽기 실패({0})" -f $_.Exception.GetType().Name)
        return $null
    }
}

function Read-CgAllowlist {
    # 허용목록 JSON(해시만). entries[] = { primaryDomainSha256, tenantCodeSha256 } · 각 hex 64. 한 항목이라도 틀리면 목록 전체 무효.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $r = [ordered]@{ Exists = $false; Valid = $false; Entries = @() }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $r }
    $r.Exists = $true
    try {
        $raw = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8).TrimStart([char]0xFEFF)
        $j = $raw | ConvertFrom-Json
        if ($null -eq $j -or $null -eq $j.PSObject.Properties['entries']) {
            Write-Verbose '허용목록: entries 칸 없음 → 무효'
            return $r
        }
        $entries = New-Object System.Collections.Generic.List[object]
        foreach ($e in @($j.entries)) {
            $d = [string]$e.primaryDomainSha256
            $t = [string]$e.tenantCodeSha256
            if ($d -notmatch '\A[0-9a-fA-F]{64}\z' -or $t -notmatch '\A[0-9a-fA-F]{64}\z') {
                Write-Verbose '허용목록: 항목 형식 틀림 → 목록 전체 무효'
                return $r
            }
            $entries.Add([pscustomobject]@{ D = $d.ToLowerInvariant(); T = $t.ToLowerInvariant() })
        }
        $r.Entries = $entries.ToArray()
        $r.Valid = $true
    } catch {
        Write-Warning ("허용목록 읽기 실패({0}) — G4 거절로 처리" -f $_.Exception.GetType().Name)
    }
    return $r
}

function ConvertTo-CgProcessArg {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Value)
    $v = $Value.TrimEnd('\')
    return '"' + ($v -replace '"', '\"') + '"'
}

function Invoke-CgGitRead {
    # 읽기 전용 git 호출. 부르는 명령 = hash-object(-w 없음) · rev-parse 뿐. GIT_OPTIONAL_LOCKS=0(색인 갱신 · 잠금 0).
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$GitExe, [Parameter(Mandatory)][string]$RepoRoot, [Parameter(Mandatory)][string[]]$GitArgs)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $GitExe
    $all = @('-C', $RepoRoot) + $GitArgs
    $psi.Arguments = (($all | ForEach-Object { ConvertTo-CgProcessArg -Value $_ }) -join ' ')
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.EnvironmentVariables['GIT_OPTIONAL_LOCKS'] = '0'
    $psi.EnvironmentVariables['GIT_TERMINAL_PROMPT'] = '0'
    $p = [System.Diagnostics.Process]::Start($psi)
    try {
        $errTask = $p.StandardError.ReadToEndAsync()
        $out = $p.StandardOutput.ReadToEnd()
        $p.WaitForExit()
        $null = $errTask.Result
        return @{ Code = $p.ExitCode; Out = ([string]$out).Trim() }
    } finally { $p.Dispose() }
}

function Get-CgAllowlistGitState {
    # G4a 재료(설계 §3-2 · 3인 R9): 작업 사본 blob id = 저장소 변환 규칙(줄끝 · 속성) 적용 계산(hash-object 기본 동작 · -w 없음)
    # · origin/main 같은 경로 blob id · origin/main 커밋. 하나라도 못 읽으면 Readable = $false.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepoRoot, [Parameter(Mandatory)][string]$RelPath, [string]$GitExe = 'git')
    $r = [ordered]@{ Readable = $false; WorkBlob = $null; MainBlob = $null; MainCommit = $null; Reason = $null }
    try {
        $w = Invoke-CgGitRead -GitExe $GitExe -RepoRoot $RepoRoot -GitArgs @('hash-object', '--', $RelPath)
        if ($w.Code -ne 0 -or -not (Test-CgHex -Value $w.Out)) { $r.Reason = '작업 사본 blob 못 읽음'; return $r }
        $r.WorkBlob = $w.Out.ToLowerInvariant()
        $c = Invoke-CgGitRead -GitExe $GitExe -RepoRoot $RepoRoot -GitArgs @('rev-parse', '--verify', '--quiet', 'origin/main^{commit}')
        if ($c.Code -ne 0 -or -not (Test-CgHex -Value $c.Out)) { $r.Reason = 'origin/main 참조 없음'; return $r }
        $r.MainCommit = $c.Out.ToLowerInvariant()
        $m = Invoke-CgGitRead -GitExe $GitExe -RepoRoot $RepoRoot -GitArgs @('rev-parse', '--verify', '--quiet', ('origin/main:' + $RelPath))
        if ($m.Code -ne 0 -or -not (Test-CgHex -Value $m.Out)) { $r.Reason = 'origin/main 에 같은 경로 없음(추적 안 됨)'; return $r }
        $r.MainBlob = $m.Out.ToLowerInvariant()
        $r.Readable = $true
    } catch {
        Write-Warning ("git 읽기 실패({0}) — G4a 거절로 처리" -f $_.Exception.GetType().Name)
        $r.Reason = 'git 못 읽음'
    }
    return $r
}

function ConvertFrom-CgImagePath {
    # 서비스 ImagePath → 설치본 폴더({app}). 실행 파일이 ...\watchdog\HitPan.Watchdog.exe 꼴이 아니면 $null.
    [CmdletBinding()]
    param([AllowNull()][string]$ImagePath)
    if ([string]::IsNullOrWhiteSpace($ImagePath)) { return $null }
    if ($ImagePath.Trim() -notmatch '\A"?(?<exe>[^"]+?\.exe)(?:"|\s|\z)') { return $null }
    $exe = $Matches['exe']
    try {
        if ([System.IO.Path]::GetFileName($exe) -ne $script:CgConst.WatchdogExeName) { return $null }
        $dir = [System.IO.Path]::GetDirectoryName($exe)
        if ([System.IO.Path]::GetFileName($dir) -ne 'watchdog') { return $null }
        return [System.IO.Path]::GetDirectoryName($dir)
    } catch {
        Write-Verbose ("워치독 실행 파일 경로 해석 실패({0})" -f $_.Exception.GetType().Name)
        return $null
    }
}

# ─────────────────────────────────────────────── 응답 해석(순수) — 본문 · 실패 문구는 담지 않는다

function Test-CgExternalAddress {
    # 설계 §5-1a ⓐ: 해석 주소가 하나라도 루프백 · 사설 · 링크로컬 · 미지정이면 「외부 경로 아님」. 주소 0개 · 해석 불가도 아님.
    [CmdletBinding()]
    param([AllowNull()][string[]]$Addresses)
    $list = @($Addresses | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($list.Count -eq 0) { return $false }
    foreach ($a in $list) {
        $ip = $null
        if (-not [System.Net.IPAddress]::TryParse($a.Trim(), [ref]$ip)) { return $false }
        if ($ip.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetworkV6 -and $ip.IsIPv4MappedToIPv6) { $ip = $ip.MapToIPv4() }
        if ([System.Net.IPAddress]::IsLoopback($ip)) { return $false }
        $b = $ip.GetAddressBytes()
        if ($ip.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetwork) {
            if ($b[0] -eq 0 -or $b[0] -eq 10 -or $b[0] -eq 127) { return $false }
            if ($b[0] -eq 172 -and $b[1] -ge 16 -and $b[1] -le 31) { return $false }
            if ($b[0] -eq 192 -and $b[1] -eq 168) { return $false }
            if ($b[0] -eq 169 -and $b[1] -eq 254) { return $false }
        } elseif ($ip.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetworkV6) {
            if ($ip.IsIPv6LinkLocal -or $ip.IsIPv6SiteLocal) { return $false }
            if (($b[0] -band 0xFE) -eq 0xFC) { return $false }
            if ($ip.Equals([System.Net.IPAddress]::IPv6Any)) { return $false }
        } else { return $false }
    }
    return $true
}

function ConvertFrom-CgOriginHealth {
    # 원점 /health(HealthController) → Ok(200) · DatabaseOk(ok / 실패 / 모름) · Version. 실패 문구(예외 메시지)는 버린다.
    [CmdletBinding()]
    param([AllowNull()] $Response)
    $r = [ordered]@{ Ok = $false; Status = 0; DatabaseOk = $null; Version = $null }
    if ($null -eq $Response) { return $r }
    $r.Status = [int]$Response.Status
    $body = [string]$Response.Body
    if ($body) {
        try {
            $j = $body | ConvertFrom-Json
            if ($null -ne $j -and $null -ne $j.checks) {
                $db = $j.checks.database
                if ($db -is [string]) { $r.DatabaseOk = ($db -ceq 'ok') }
                $ver = $j.checks.version
                if ($ver -is [string] -and $ver -match '\A[0-9A-Za-z.+-]{1,64}\z') { $r.Version = $ver }
            }
        } catch { Write-Verbose ("원점 응답 해석 불가({0})" -f $_.Exception.GetType().Name) }
    }
    $body = $null
    $r.Ok = ($r.Status -eq 200)
    return $r
}

function ConvertFrom-CgAppVersion {
    # 외부 /api/app-version(AppVersionController · 버전 하나만) → Status · Version. JSON 아님(에지 오류 쪽) = Version $null.
    [CmdletBinding()]
    param([AllowNull()] $Response)
    $r = [ordered]@{ Status = 0; Version = $null }
    if ($null -eq $Response) { return $r }
    $r.Status = [int]$Response.Status
    $body = [string]$Response.Body
    if ($body) {
        try {
            $j = $body | ConvertFrom-Json
            if ($null -ne $j -and $j.version -is [string] -and $j.version -match '\A[0-9A-Za-z.+-]{1,64}\z') { $r.Version = $j.version }
        } catch { Write-Verbose ("외부 응답 해석 불가({0})" -f $_.Exception.GetType().Name) }
    }
    $body = $null
    return $r
}

function Test-CgControlTrace {
    # 관측자 연결 대조(설계 §5-3 고침 6차 · 7차): 200 + 본문에 h= 줄 → $true. 본문(관측자 주소 줄 등)은 담지 않는다.
    [CmdletBinding()]
    param([AllowNull()] $Response)
    if ($null -eq $Response) { return $false }
    if ([int]$Response.Status -ne 200) { return $false }
    $ok = ([string]$Response.Body) -match '(?m)^h=\S+'
    return [bool]$ok
}

function Get-CgExtState {
    # 외부 응답 한 번의 칸: ok(200 + version = 기준 + 외부 경로) · other(외부 경로 아님 또는 200 + 다른 version) · fail(그 밖 — 에지 오류 · 시간 초과 포함).
    [CmdletBinding()]
    param([AllowNull()] $Ext, [AllowNull()][string]$BaselineVersion)
    if ($null -eq $Ext) { return 'fail' }
    if ($Ext.AddrOk -ne $true) { return 'other' }
    if ([int]$Ext.Status -eq 200 -and $Ext.Version) {
        if ($BaselineVersion -and $Ext.Version -ceq $BaselineVersion) { return 'ok' }
        return 'other'
    }
    return 'fail'
}

function ConvertFrom-CgFailureActions {
    # E4: 서비스 「실패 시 복구 동작」 조회 출력 → none · present · unknown. 출력 원문(명령줄 칸 포함)은 담지 않는다(3인 R8).
    [CmdletBinding()]
    param([AllowNull()][string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return 'unknown' }
    $lines = $Text -split "`r?`n"
    $Text = $null
    $idx = -1
    for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match '\A\s*FAILURE_ACTIONS\s*:') { $idx = $i; break } }
    if ($idx -lt 0) { return 'unknown' }
    $chunk = New-Object System.Collections.Generic.List[string]
    $chunk.Add(($lines[$idx] -replace '\A\s*FAILURE_ACTIONS\s*:', ''))
    for ($j = $idx + 1; $j -lt $lines.Count; $j++) {
        if ($lines[$j] -match '\A\s*[A-Z_]+(\s*\([^)]*\))?\s*:') { break }
        $chunk.Add($lines[$j])
    }
    $joined = ($chunk -join "`n")
    $lines = $null
    if ($joined -match '(?i)\b(RESTART|RUN PROCESS|REBOOT)\b') { return 'present' }
    if ($joined.Trim().Length -eq 0) { return 'none' }
    return 'unknown'
}

# ─────────────────────────────────────────────── 쿨다운(설계 §5-1 ① · H1 3인 F9 · 3인 R3)

function Measure-CgHistory {
    # ⓐ 보고서 폴더 실행 기록(JSON 한 줄 = { utc, key }) 중 같은 키 · 지난 60분 수. 해석 못 한 줄 = 1 로 셈(보호 쪽).
    [CmdletBinding()]
    param([AllowNull()][string[]]$Lines, [Parameter(Mandatory)][string]$Key, [Parameter(Mandatory)][datetime]$NowUtc)
    $now = ConvertTo-CgUtc -Value $NowUtc
    $from = $now.AddMinutes(-$script:CgConst.CooldownWindowMin)
    $n = 0
    foreach ($l in @($Lines)) {
        if ([string]::IsNullOrWhiteSpace($l)) { continue }
        try {
            $o = $l | ConvertFrom-Json
            $t = ConvertTo-CgUtc -Value $o.utc
            if ($null -eq $t) { throw (New-Object System.FormatException) }
            if ($o.key -ceq $Key -and $t -gt $from -and $t -le $now.AddMinutes(1)) { $n++ }
        } catch {
            Write-Warning ("실행 기록 한 줄 해석 실패({0}) — 셈에 1 더함(보호 쪽)" -f $_.Exception.GetType().Name)
            $n++
        }
    }
    return $n
}

function Measure-CgWatchdogEvents {
    # 워치독 이벤트(원본 HitPanWatchdog · 읽기만) 중 같은 서비스의 「restarted」(WS28I_FourProcess.cs:70) · 「restart failure」(:75) 시각.
    # 이벤트 글은 맞춰 보기만 하고 담지 않는다(3인 R8).
    [CmdletBinding()]
    param([AllowNull()][object[]]$Events, [Parameter(Mandatory)][string]$Service, [Parameter(Mandatory)][datetime]$SinceUtc)
    $since = ConvertTo-CgUtc -Value $SinceUtc
    $ok = New-Object System.Text.RegularExpressions.Regex(('WS-28-I: service ' + [regex]::Escape($Service) + ' restarted'), 'IgnoreCase')
    $bad = New-Object System.Text.RegularExpressions.Regex(('WS-28-I: service ' + [regex]::Escape($Service) + ' restart failure'), 'IgnoreCase')
    $restarted = New-Object System.Collections.Generic.List[datetime]
    $failures = New-Object System.Collections.Generic.List[datetime]
    foreach ($e in @($Events)) {
        if ($null -eq $e) { continue }
        $t = ConvertTo-CgUtc -Value $e.TimeUtc
        if ($null -eq $t -or $t -lt $since) { continue }
        $txt = [string]$e.Text
        if ($ok.IsMatch($txt)) { $restarted.Add($t) }
        elseif ($bad.IsMatch($txt)) { $failures.Add($t) }
        $txt = $null
    }
    $restarted.Sort()
    $failures.Sort()
    return [pscustomobject]@{ Restarted = $restarted.ToArray(); Failures = $failures.ToArray() }
}

function Get-CgCooldownDecision {
    # 셈 = ⓐ · ⓑ 중 큰 값. 이전 기록 4건 이상(= 이번이 5회째) → 미실행(쿨다운 보호). 이벤트 로그 못 읽음 → 미실행(fail-closed).
    [CmdletBinding()]
    param([int]$HistoryCount, [int]$EventCount, [bool]$EventReadable)
    if (-not $EventReadable) { return [pscustomobject]@{ Blocked = $true; Count = $HistoryCount; Reason = '쿨다운 보호 · 이벤트 로그 못 읽음' } }
    $c = [math]::Max($HistoryCount, $EventCount)
    if ($c -ge $script:CgConst.CooldownMaxPrior) { return [pscustomobject]@{ Blocked = $true; Count = $c; Reason = '쿨다운 보호' } }
    return [pscustomobject]@{ Blocked = $false; Count = $c; Reason = $null }
}

# ─────────────────────────────────────────────── 수집 — Get-EnvSnapshot (읽기만)

function Get-CgRepoRoot { return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..')) }

function Resolve-CgSnapshotProviders {
    [CmdletBinding()]
    param([AllowNull()][hashtable]$Providers)
    $required = @('MarkerPath', 'InstallDir', 'GetMachineGuid', 'GetServiceStatus', 'GetIsAdmin', 'HttpGet', 'RepoRoot', 'GitExe')
    if ($null -ne $Providers) {
        foreach ($k in $required) {
            if (-not $Providers.ContainsKey($k)) { throw ("시험 공급자 누락: {0} — 실물 읽기로 대체하지 않음" -f $k) }
        }
        $p = @{}
        foreach ($k in $required) { $p[$k] = $Providers[$k] }
        $p.AllowlistRelPath = if ($Providers.ContainsKey('AllowlistRelPath')) { [string]$Providers.AllowlistRelPath } else { $script:CgConst.AllowlistRelPath }
        return $p
    }
    $installDir = $null
    try {
        $item = Get-ItemProperty -LiteralPath ('HKLM:\SYSTEM\CurrentControlSet\Services\' + $script:CgConst.WatchdogService) -Name ImagePath -ErrorAction Stop
        $installDir = ConvertFrom-CgImagePath -ImagePath ([string]$item.ImagePath)
    } catch { Write-Verbose ("워치독 서비스 등록 못 읽음({0})" -f $_.Exception.GetType().Name) }
    return @{
        MarkerPath       = $script:CgConst.MarkerPath
        InstallDir       = $installDir
        GetMachineGuid   = { (Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Cryptography' -Name MachineGuid -ErrorAction Stop).MachineGuid }
        GetServiceStatus = { param([string]$Name) (Get-Service -Name $Name -ErrorAction Stop).Status.ToString() }
        GetIsAdmin       = { ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) }
        HttpGet          = { param([string]$Url, [int]$TimeoutSec, [hashtable]$Headers) Invoke-CgHttpGet -Url $Url -TimeoutSec $TimeoutSec -Headers $Headers }
        RepoRoot         = (Get-CgRepoRoot)
        GitExe           = 'git'
        AllowlistRelPath = $script:CgConst.AllowlistRelPath
    }
}

function Get-EnvSnapshot {
    <#
      설계 §7-1 수집 층. 한 객체로 읽는다 — 마커 · 기기값 해시 · db.conf(화이트리스트 4키) · 워치독 감시 목록 · 허용목록
      · 허용목록 git 상태(G4a) · 서비스 상태 · 관리자 여부 · 원점 /health(G5). 읽기만.
      -Providers: 시험용(전부 줘야 함 · 빠지면 멈춤). 안 주면 실물 읽기.
    #>
    [CmdletBinding()]
    param([AllowNull()][hashtable]$Providers, [datetime]$NowUtc = [datetime]::UtcNow)
    $p = Resolve-CgSnapshotProviders -Providers $Providers
    $installDir = if ([string]::IsNullOrWhiteSpace([string]$p.InstallDir)) { $null } else { [string]$p.InstallDir }

    $marker = Read-CgMarker -Path ([string]$p.MarkerPath)

    $machineHash = $null
    try {
        $guid = [string](& $p.GetMachineGuid)
        $machineHash = Get-CgIdentityHash -Value $guid
        $guid = $null
    } catch { Write-Warning ("기기 식별값 못 읽음({0}) — G2 거절로 처리" -f $_.Exception.GetType().Name) }

    if ($installDir) {
        $dbConf = Read-CgDbConf -Path (Join-Path $installDir 'db.conf')
        $wdList = Read-CgWatchdogServices -Path (Join-Path $installDir 'watchdog\appsettings.json')
    } else {
        $values = [ordered]@{}
        foreach ($k in $script:CgConst.DbConfWhitelist) { $values[$k] = $null }
        $dbConf = [ordered]@{ Found = $false; ReadOk = $false; Values = $values; BadLineCount = 0; DuplicateKeys = 0 }
        $wdList = $null
    }

    $repoRoot = ([string]$p.RepoRoot).TrimEnd('\')
    $allow = Read-CgAllowlist -Path (Join-Path $repoRoot (([string]$p.AllowlistRelPath) -replace '/', '\'))
    $git = Get-CgAllowlistGitState -RepoRoot $repoRoot -RelPath ([string]$p.AllowlistRelPath) -GitExe ([string]$p.GitExe)

    $svc = [ordered]@{}
    foreach ($n in $script:CgConst.GuardServices) {
        $st = $null
        try { $st = [string](& $p.GetServiceStatus $n) } catch { Write-Verbose ("서비스 상태 못 읽음: {0}({1})" -f $n, $_.Exception.GetType().Name) }
        $svc[$n] = if ([string]::IsNullOrWhiteSpace($st)) { $null } else { $st }
    }

    $isAdmin = $false
    try { $isAdmin = [bool](& $p.GetIsAdmin) } catch { Write-Warning ("관리자 여부 못 읽음({0}) — G6 거절로 처리" -f $_.Exception.GetType().Name) }

    $origin = ConvertFrom-CgOriginHealth -Response $null
    $port = ConvertTo-CgPort -Value $dbConf.Values['API_PORT']
    if ($null -ne $port) {
        try {
            $resp = & $p.HttpGet ('http://127.0.0.1:{0}/health' -f $port) $script:CgConst.GuardHealthTimeoutSec $null
            $origin = ConvertFrom-CgOriginHealth -Response $resp
            $resp = $null
        } catch { Write-Verbose ("원점 /health 읽기 실패({0})" -f $_.Exception.GetType().Name) }
    }

    return [pscustomobject][ordered]@{
        CollectedUtc     = (Format-CgUtc -Value $NowUtc)
        Marker           = $marker
        MachineIdHash    = $machineHash
        InstallDirFound  = [bool]$installDir
        InstallDir       = $installDir   # 러너 내부용(S-B ⓐ · S-C) — 보고서 · 화면에 옮기지 않는다
        DbConf           = $dbConf
        WatchdogServices = $wdList
        Allowlist        = $allow
        AllowlistGit     = $git
        Services         = $svc
        IsAdmin          = $isAdmin
        OriginHealth     = $origin
    }
}

# ─────────────────────────────────────────────── 판정 — Get-GuardDecision (순수)

function Get-GuardDecision {
    <#
      설계 §3-2: 「이 PC 는 허가된 시험 PC 다」 — G1 · G2 · G3 · G4 · G4a · G5 · G6 전부 AND · 하나라도 못 읽으면 거절.
      칸을 전부 평가해 통과 · 거절 사유를 모두 돌려준다(보고서 첫 절). 대상 선택을 막는 장치는 이 일곱 칸뿐(§3-3 끝).
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowNull()] $Snapshot, [Parameter(Mandatory)][datetime]$NowUtc)
    $now = ConvertTo-CgUtc -Value $NowUtc
    $checks = New-Object System.Collections.Generic.List[object]
    $add = { param([string]$Id, [bool]$Pass, [string]$Reason) $checks.Add([pscustomobject]@{ Id = $Id; Pass = $Pass; Reason = $Reason }) }
    $s = $Snapshot
    $m = if ($null -ne $s) { $s.Marker } else { $null }

    # G1 마커 존재 + ENV=TEST
    if ($null -eq $m -or -not $m.Exists) { & $add 'G1' $false '마커 없음' }
    elseif (@($m.Invalid).Count -gt 0) { & $add 'G1' $false ('마커 내용 무효(' + (@($m.Invalid) -join ' · ') + ')') }
    elseif (-not $m.EnvTest) { & $add 'G1' $false 'ENV=TEST 없음' }
    else { & $add 'G1' $true '마커 있음 · ENV=TEST' }

    # G2 기기 묶음
    if ($null -eq $m -or -not $m.Exists -or -not (Test-CgHex -Value ([string]$m.Machine) -Length 64)) { & $add 'G2' $false 'MACHINE 없음 · 형식 틀림' }
    elseif (-not (Test-CgHex -Value ([string]$s.MachineIdHash) -Length 64)) { & $add 'G2' $false '기기 식별값 못 읽음' }
    elseif (([string]$m.Machine).ToLowerInvariant() -cne ([string]$s.MachineIdHash).ToLowerInvariant()) { & $add 'G2' $false '기기값 불일치' }
    else { & $add 'G2' $true '기기값 일치' }

    # G3 만료(UTC ISO 8601 · 끝 Z · 지금 < EXPIRES ≤ 지금 + 72시간)
    $exp = if ($null -ne $m) { ConvertTo-CgUtc -Value $m.Expires } else { $null }
    if ($null -eq $exp) { & $add 'G3' $false 'EXPIRES 없음 · 형식 틀림(UTC ISO 8601 · 끝 Z)' }
    elseif ($exp -le $now) { & $add 'G3' $false '만료됨' }
    elseif ($exp -gt $now.AddHours($script:CgConst.ExpiresMaxHours)) { & $add 'G3' $false '72시간 넘게 잡힌 만료' }
    else { & $add 'G3' $true '만료 전 · 72시간 안' }

    # G4 db.conf PRIMARY_DOMAIN + TENANT_CODE 해시 쌍 = 허용목록 한 줄 · demo 거절 · db.conf 없음 거절
    $dh = $null; $th = $null
    $dc = if ($null -ne $s) { $s.DbConf } else { $null }
    $al = if ($null -ne $s) { $s.Allowlist } else { $null }
    if ($null -eq $dc -or -not $dc.Found) { & $add 'G4' $false 'db.conf 없음' }
    elseif (-not $dc.ReadOk) { & $add 'G4' $false 'db.conf 읽기 실패' }
    else {
        $dom = [string]$dc.Values['PRIMARY_DOMAIN']
        $ten = [string]$dc.Values['TENANT_CODE']
        $dh = Get-CgIdentityHash -Value $dom
        $th = Get-CgIdentityHash -Value $ten
        if (-not $dh) { & $add 'G4' $false 'PRIMARY_DOMAIN 없음' }
        elseif (-not $th) { & $add 'G4' $false 'TENANT_CODE 없음' }
        elseif ($dom.Trim().ToLowerInvariant() -match $script:CgConst.DemoDomainPattern) { & $add 'G4' $false 'demo 도메인(운영)' }
        elseif ($null -eq $al -or -not $al.Exists) { & $add 'G4' $false '허용목록 없음' }
        elseif (-not $al.Valid) { & $add 'G4' $false '허용목록 형식 틀림' }
        else {
            $hit = @($al.Entries | Where-Object { $_.D -ceq $dh -and $_.T -ceq $th })
            if ($hit.Count -ge 1) { & $add 'G4' $true '허용목록 한 줄과 일치' }
            else { & $add 'G4' $false '허용목록 밖(도메인 · 테넌트 해시 쌍 불일치)' }
        }
        $dom = $null; $ten = $null
    }

    # G4a 허용목록 작업 사본 = origin/main 승인본
    $g = if ($null -ne $s) { $s.AllowlistGit } else { $null }
    if ($null -eq $g -or -not $g.Readable) {
        $why = if ($null -ne $g -and $g.Reason) { [string]$g.Reason } else { 'git 못 읽음' }
        & $add 'G4a' $false ('승인본 대조 못 함(' + $why + ')')
    }
    elseif (-not (Test-CgHex -Value ([string]$g.WorkBlob)) -or -not (Test-CgHex -Value ([string]$g.MainBlob))) { & $add 'G4a' $false 'blob id 형식 틀림' }
    elseif (([string]$g.WorkBlob).ToLowerInvariant() -cne ([string]$g.MainBlob).ToLowerInvariant()) { & $add 'G4a' $false '작업 사본 ≠ origin/main 승인본' }
    else { & $add 'G4a' $true '작업 사본 = origin/main 승인본' }

    # G5 기준 건강 — HitPanWatchdog · cloudflared · MariaDB Running + 원점 /health 200(db.conf API_PORT)
    $notRunning = New-Object System.Collections.Generic.List[string]
    foreach ($n in $script:CgConst.GuardServices) {
        $st = if ($null -ne $s -and $null -ne $s.Services) { $s.Services[$n] } else { $null }
        if ([string]$st -ne 'Running') { $notRunning.Add($n) }
    }
    $port = if ($null -ne $dc -and $null -ne $dc.Values) { ConvertTo-CgPort -Value $dc.Values['API_PORT'] } else { $null }
    if ($notRunning.Count -gt 0) { & $add 'G5' $false ('Running 아님: ' + ($notRunning -join ' · ')) }
    elseif ($null -eq $port) { & $add 'G5' $false 'API_PORT 없음 · 못 읽음' }
    elseif ($null -eq $s.OriginHealth -or -not $s.OriginHealth.Ok) { & $add 'G5' $false '원점 /health 200 아님' }
    else { & $add 'G5' $true '워치독 · cloudflared · MariaDB Running · 원점 /health 200' }

    # G6 관리자 권한
    if ($null -ne $s -and $s.IsAdmin -eq $true) { & $add 'G6' $true '관리자' } else { & $add 'G6' $false '관리자 권한 아님' }

    $arr = $checks.ToArray()
    $fails = @($arr | Where-Object { -not $_.Pass })
    return [pscustomobject][ordered]@{
        Allowed          = ($fails.Count -eq 0 -and $arr.Count -eq 7)
        Checks           = $arr
        RejectReasons    = @($fails | ForEach-Object { '{0} {1}' -f $_.Id, $_.Reason })
        DomainHashPrefix = if ($dh) { $dh.Substring(0, 8) } else { $null }
        TenantHashPrefix = if ($th) { $th.Substring(0, 8) } else { $null }
    }
}

# ─────────────────────────────────────────────── 판정 — Get-ScenarioVerdict (순수)

function New-CgVerdict {
    param($Cat)
    return [pscustomobject][ordered]@{
        Id = $Cat.Id; Label = $Cat.Label; Column = $null; Reason = $null; Seconds = $null; Display = $null
        SdState = $null; Executed = $false; CutIssued = $false; StateUnknown = $false; Handoff = $false; Halt = $false
        Actor = $null; ActorDetail = $null; Notes = @(); T0Utc = $null; LastState = $null; ElapsedSec = $null; SampleCount = 0
    }
}

function Complete-CgVerdict {
    param($V)
    switch ($V.Column) {
        'PASS'           { $V.Display = ('PASS({0}초) — 5분 안 복구됨' -f [math]::Round([double]$V.Seconds)) }
        'FAIL'           { $V.Display = ('FAIL — {0}' -f $V.Reason) }
        '측정불가(가드)' { $V.Display = '측정불가(가드)' }
        '측정불가'       { $V.Display = ('측정불가({0})' -f $V.Reason) }
        '미실행'         { $V.Display = ('미실행({0})' -f $V.Reason) }
        '반자동'         {
            if ($V.SdState -eq '완료') { $V.Display = ('반자동 — 완료(첫 실패 뒤 {0}초 복귀 · 손 1회)' -f [math]::Round([double]$V.Seconds)) }
            else { $V.Display = ('반자동 — {0}({1})' -f $V.SdState, $V.Reason) }
        }
    }
    $V.Executed = (@('미실행', '측정불가(가드)') -notcontains $V.Column) -and -not ($V.Column -eq '반자동' -and $V.SdState -eq '미실행')
    return $V
}

function Set-CgSkip {
    param($V, [string]$Skip)
    if ($Skip -eq '가드') { $V.Column = '측정불가(가드)'; $V.Reason = '가드' }
    elseif ($Skip -like '쿨다운 보호*' -or $Skip -eq '앞 건 정지' -or $Skip -like '판정 모드*') { $V.Column = '미실행'; $V.Reason = $Skip }
    else { $V.Column = '측정불가'; $V.Reason = $Skip; $V.Halt = $true }   # 감시 대상 아님 · 재기동 작업 없음 · 모르는 사유(fail-closed)
}

function Get-CgActor {
    # 복구 주체(별도 칸 · PASS 미반영 · 설계 §5-1a 끝 줄 · 고침 6차 ⑨-2). 하나라도 빠지면 「불명」.
    param($Evidence, [datetime]$T0Utc)
    if ($null -eq $Evidence) { return @($script:CgActorUnknown, 'E1~E4 없음') }
    $e1 = ConvertTo-CgUtc -Value $Evidence.E1Utc
    $e2 = ConvertTo-CgUtc -Value $Evidence.E2Utc
    $e3 = $Evidence.E3
    $e4 = [string]$Evidence.E4
    $detail = 'E1 {0} · E2 {1} · E3 {2} · E4 {3}' -f $(if ($e1) { '예 ' + (Format-CgUtc $e1) } else { '아니오' }), $(if ($e2) { '예 ' + (Format-CgUtc $e2) } else { '아니오' }), $(if ($null -ne $e3) { [string]$e3 } else { '모름' }), $(switch ($e4) { 'none' { '없음' } 'present' { '있음' } default { '모름' } })
    $okOrder = ($null -ne $e1 -and $null -ne $e2 -and $e1 -ge $e2 -and ($e1 - $e2).TotalSeconds -le $script:CgConst.E1AfterE2Sec)
    if ($okOrder -and $Evidence.E1Failure -eq $false -and $e3 -eq 0 -and $e4 -eq 'none') { return @($script:CgActorMatch, $detail) }
    return @($script:CgActorUnknown, $detail)
}

function Resolve-CgAutoVerdict {
    param($Cat, [hashtable]$Obs, [bool]$Partial)
    $c = $script:CgConst
    $v = New-CgVerdict -Cat $Cat
    if ($Obs.Skip) { Set-CgSkip -V $v -Skip ([string]$Obs.Skip); return (Complete-CgVerdict $v) }
    $b = $Obs.Baseline
    if ($null -eq $b -or -not $b.Ok) {
        $v.Column = '측정불가'; $v.Reason = if ($null -ne $b -and $b.Reason) { [string]$b.Reason } else { '기준 응답 없음' }; $v.Halt = $true
        return (Complete-CgVerdict $v)
    }
    $cut = $Obs.Cut
    if ($null -eq $cut -or -not $cut.Issued) { $v.Column = '측정불가'; $v.Reason = '멈추기 실패'; $v.Halt = $true; $v.StateUnknown = $true; $v.Handoff = $true; return (Complete-CgVerdict $v) }
    $v.CutIssued = $true
    $samples = @($Obs.Samples | Where-Object { $null -ne $_ })
    $v.SampleCount = $samples.Count
    $cutUtc = ConvertTo-CgUtc -Value $Obs.CutUtc
    if (-not $cut.Ok) {
        $v.Column = '측정불가'; $v.Reason = '멈추기 실패'; $v.Halt = $true; $v.StateUnknown = $true; $v.Handoff = $true
        $v.LastState = '멈추기 명령 실패'; $v.ElapsedSec = 0
        return (Complete-CgVerdict $v)
    }
    $lastT = if ($samples.Count -gt 0) { [double]$samples[-1].T } else { 0.0 }
    $v.ElapsedSec = [math]::Round($lastT)
    if ($samples.Count -gt 0) {
        $last = $samples[-1]
        $v.LastState = if ($Cat.Kind -eq 'service') { [string]$last.Svc } else { if ($last.OldAlive) { '끊기 전 프로세스 남음' } else { '끊기 전 프로세스 없음' } }
    }
    $baseVer = [string]$b.Version
    $isStop = {
        param($sm)
        if ($Cat.Kind -eq 'service') { return ([string]$sm.Svc -eq 'Stopped' -or ($Obs.PreSvcPid -and $sm.SvcPid -and $sm.SvcPid -ne $Obs.PreSvcPid)) }
        return $false
    }
    $isReflect = {
        param($sm)
        switch ($Cat.Id) {
            'S-A' { return ((Get-CgExtState -Ext $sm.Ext -BaselineVersion $baseVer) -eq 'fail') }
            'S-B' { return ($null -ne $sm.Origin -and $sm.Origin.DatabaseOk -eq $false) }
            'S-C' { return ($null -eq $sm.Origin -or -not $sm.Origin.Ok) }
        }
        return $false
    }
    $isRecovered = {
        param($sm)
        switch ($Cat.Id) {
            'S-A' { return ([string]$sm.Svc -eq 'Running' -and (Get-CgExtState -Ext $sm.Ext -BaselineVersion $baseVer) -eq 'ok') }
            'S-B' { return ([string]$sm.Svc -eq 'Running' -and $null -ne $sm.Origin -and $sm.Origin.Ok -and $sm.Origin.DatabaseOk -eq $true -and $sm.Origin.UnderInstall -eq $true -and [string]$sm.Origin.Version -ceq $baseVer) }
            'S-C' { return ($null -ne $sm.Origin -and $sm.Origin.Ok -and $sm.Origin.UnderInstall -eq $true) }
        }
        return $false
    }

    # ③ 멈춤 관측(최대 30초) — 서비스: Stopped 또는 서비스 프로세스 번호 바뀜 / 프로세스: 끊기 전 번호 전부 사라짐 + /health 실패 1회 이상
    $stopIdx = -1; $reflIdx = -1; $gone = $false; $failSeen = $false
    for ($i = 0; $i -lt $samples.Count; $i++) {
        $sm = $samples[$i]
        if ([double]$sm.T -gt $c.StopWindowSec) { break }
        if ($reflIdx -lt 0 -and (& $isReflect $sm)) { $reflIdx = $i }
        if ($Cat.Kind -eq 'process') {
            if ($sm.OldAlive -eq $false) { $gone = $true }
            if ($reflIdx -ge 0) { $failSeen = $true }
            if ($stopIdx -lt 0 -and $gone -and $failSeen) { $stopIdx = $i }
        } elseif ($stopIdx -lt 0 -and (& $isStop $sm)) { $stopIdx = $i }
    }
    if ($stopIdx -lt 0) {
        if ($Partial -and $lastT -lt $c.StopWindowSec) { return $null }
        $v.Column = '측정불가'; $v.Reason = '멈춤 미관측'; $v.Halt = $true; $v.StateUnknown = $true; $v.Handoff = $true
        return (Complete-CgVerdict $v)
    }
    $t0 = [double]$samples[$stopIdx].T
    if ($cutUtc) { $v.T0Utc = $cutUtc.AddMilliseconds([math]::Round($t0 * 1000)) }

    # S-A R2(3인 R2): Stopped 관측 ~ Running 관측 사이 외부 성공(ok · other) 1회라도 → 측정불가(끊김 미반영)
    if ($Cat.Id -eq 'S-A') {
        for ($j = $stopIdx; $j -lt $samples.Count; $j++) {
            $sm = $samples[$j]
            if ([string]$sm.Svc -eq 'Running') { break }
            if (@('ok', 'other') -contains (Get-CgExtState -Ext $sm.Ext -BaselineVersion $baseVer)) {
                $v.Column = '측정불가'; $v.Reason = '끊김 미반영'; $v.Halt = $true; $v.StateUnknown = $true; $v.Handoff = $true
                return (Complete-CgVerdict $v)
            }
        }
    }
    # 끊김 반영(멈춤 관측 창 30초 안) — 서비스 쪽만 별도(프로세스는 멈춤 관측에 들어 있음)
    if ($Cat.Kind -eq 'service' -and $reflIdx -lt 0) {
        if ($Partial -and $lastT -lt $c.StopWindowSec) { return $null }
        $v.Column = '측정불가'; $v.Reason = '끊김 미반영'; $v.Halt = $true; $v.StateUnknown = $true; $v.Handoff = $true
        return (Complete-CgVerdict $v)
    }
    $phaseIdx = [math]::Max($stopIdx, $reflIdx)

    # ④ 복구 대기 — t0 = 멈춤 관측 시각 · 5분 안 §5-1a 조건이 처음 맞은 때
    $rIdx = -1
    for ($j = $phaseIdx + 1; $j -lt $samples.Count; $j++) { if (& $isRecovered $samples[$j]) { $rIdx = $j; break } }
    if ($rIdx -ge 0) {
        $sec = [double]$samples[$rIdx].T - $t0
        $v.Seconds = $sec
        if ($sec -le $c.RecoverWindowSec) { $v.Column = 'PASS'; $v.Reason = '5분 안 복구됨' }
        else { $v.Column = 'FAIL'; $v.Reason = '5분 안 복구 안 됨'; $v.Halt = $true; $v.StateUnknown = $true; $v.Handoff = $true }
        if ($Cat.Id -eq 'S-B' -and $b.OriginPid -and $samples[$rIdx].Origin.Pid -and $samples[$rIdx].Origin.Pid -ne $b.OriginPid) { $v.Notes = @('원점 프로세스 번호 바뀜(기록만)') }
    } else {
        if ($Partial -and ($lastT - $t0) -lt $c.RecoverWindowSec) { return $null }
        $v.Column = 'FAIL'; $v.Reason = '5분 안 복구 안 됨'; $v.Halt = $true; $v.StateUnknown = $true; $v.Handoff = $true
    }
    if ($Cat.Kind -eq 'service') {
        $a = Get-CgActor -Evidence $Obs.Evidence -T0Utc $v.T0Utc
        $v.Actor = $a[0]; $v.ActorDetail = $a[1]
    } else {
        $v.Actor = $script:CgActorUnknown; $v.ActorDetail = 'S-C = 서비스 증거(E1 · E2) 해당 없음'
    }
    return (Complete-CgVerdict $v)
}

function Resolve-CgSdVerdict {
    # S-D 반자동(설계 §5-3 · 3인 R1 · CTO A1 · 고침 6차 ④-1 · 고침 7차 C1). 칸 = 반자동 · 상태 = 완료 / 실패 / 미실행.
    param($Cat, [hashtable]$Obs, [bool]$Partial)
    $c = $script:CgConst
    $v = New-CgVerdict -Cat $Cat
    $v.Column = '반자동'
    $fail = { param([string]$Why) $v.SdState = '실패'; $v.Reason = $Why; $v.StateUnknown = $true; $v.Handoff = $true }
    if ($Obs.Skip) {
        $v.SdState = '미실행'
        $v.Reason = if ($Obs.Skip -eq '가드') { '가드 거절' } else { [string]$Obs.Skip }
        return (Complete-CgVerdict $v)
    }
    $b = $Obs.SdBaseline
    if ($null -eq $b -or -not $b.TargetOk -or -not $b.ControlOk) { & $fail '측정불가(기준 응답 없음)'; return (Complete-CgVerdict $v) }
    $hand = $Obs.Hand
    if ($null -eq $hand -or [string]::IsNullOrWhiteSpace([string]$hand.Who) -or $null -eq (ConvertTo-CgUtc -Value $hand.Utc)) {
        $v.SdState = '미실행'; $v.Reason = '손 기록 없음'
        return (Complete-CgVerdict $v)
    }
    $v.CutIssued = $true
    $v.T0Utc = ConvertTo-CgUtc -Value $hand.Utc
    $rounds = @($Obs.Rounds | Where-Object { $null -ne $_ })
    $v.SampleCount = $rounds.Count
    $lastT = if ($rounds.Count -gt 0) { [double]$rounds[-1].T } else { 0.0 }
    $v.ElapsedSec = [math]::Round($lastT)

    $streak = 0; $curFirst = -1; $firstIdx = -1; $cutIdx = -1; $recIdx = -1; $windowEnd = $rounds.Count - 1
    for ($i = 0; $i -lt $rounds.Count; $i++) {
        $r = $rounds[$i]
        $tOk = ($null -ne $r.Target -and $r.Target.Ok -eq $true)
        if ($cutIdx -lt 0) {
            if ([double]$r.T -gt $c.SdWindowSec) { $windowEnd = $i - 1; break }
            if (-not $tOk) {
                if ($streak -eq 0) { $curFirst = $i }
                $streak++
                if ($streak -ge $c.SdStreak) { $cutIdx = $i; $firstIdx = $curFirst }
            } else { $streak = 0 }
        } elseif ($tOk) { $recIdx = $i; break }
    }
    $endIdx = if ($recIdx -ge 0) { $recIdx } elseif ($cutIdx -ge 0) { $rounds.Count - 1 } else { $windowEnd }
    # 관측자 연결 대조 — 대조 기록 없음 · 실패(시간 초과 포함) · 목표 끝 → 대조 시작 간격 2초 넘김 = 구분 불가 → 측정불가(관측자 연결 불명)
    for ($i = 0; $i -le $endIdx; $i++) {
        $r = $rounds[$i]
        if ($null -eq $r.Control -or $r.Control.Ok -ne $true -or $null -eq $r.Skew -or [double]$r.Skew -gt $c.MaxSkewSec) {
            $v.LastState = '대조 실패 · 기록 없음'
            & $fail '측정불가(관측자 연결 불명)'
            return (Complete-CgVerdict $v)
        }
    }
    if ($recIdx -ge 0) {
        $sec = [double]$rounds[$recIdx].T - [double]$rounds[$firstIdx].T
        $v.Seconds = $sec
        if ($sec -le $c.RecoverWindowSec) {
            $v.SdState = '완료'; $v.Reason = '끊김 반영 + 복귀'
            $v.LastState = '외부 응답 복귀'
            return (Complete-CgVerdict $v)
        }
        $v.LastState = '복귀 늦음'
        & $fail '5분 안 복귀 없음'
        return (Complete-CgVerdict $v)
    }
    if ($cutIdx -ge 0) {
        if ($Partial -and ($lastT - [double]$rounds[$firstIdx].T) -lt $c.RecoverWindowSec) { return $null }
        $v.LastState = '외부 응답 실패 계속'
        & $fail '5분 안 복귀 없음'
        return (Complete-CgVerdict $v)
    }
    if ($Partial -and $lastT -lt $c.SdWindowSec) { return $null }
    $v.LastState = '연속 실패 5회 미달'
    & $fail '끊김 미반영'
    return (Complete-CgVerdict $v)
}

function Get-ScenarioVerdict {
    <#
      설계 §5-1 · §5-1a · §5-2 · §5-3. 관측 기록(Observation)만 보고 판정 6칸 중 하나를 준다(순수).
      -Partial: 러너가 관측 도중 부르는 모드 — 아직 정해지지 않았으면 $null(보고서에 나가는 칸 아님).
      관측 기록 칸(러너가 채움): Skip · Baseline{Ok,Reason,Version,OriginPid} · Cut{Issued,Ok} · CutUtc · PreSvcPid
        · Samples[{T,Svc,SvcPid,Ext{AddrOk,Status,Version},Origin{Ok,Status,DatabaseOk,Version,UnderInstall,Pid},OldAlive}]
        · Evidence{E1Utc,E1Failure,E2Utc,E3,E4} / S-D: SdBaseline{TargetOk,ControlOk,Version} · Hand{Who,Utc}
        · Rounds[{T,Target{Ok},Control{Ok}|$null,Skew}]
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('S-A', 'S-B', 'S-C', 'S-D')][string]$Id,
        [Parameter(Mandatory)][hashtable]$Observation,
        [switch]$Partial
    )
    $cat = $script:CgCatalog | Where-Object { $_.Id -eq $Id } | Select-Object -First 1
    if ($Id -eq 'S-D') { return (Resolve-CgSdVerdict -Cat $cat -Obs $Observation -Partial ([bool]$Partial)) }
    return (Resolve-CgAutoVerdict -Cat $cat -Obs $Observation -Partial ([bool]$Partial))
}

# ─────────────────────────────────────────────── 종합 — Get-Summary (순수)

function Get-Summary {
    <#
      설계 §5-3 조합 표. 「합격」 머리는 자동 3 PASS + 반자동 완료(4 ID 중 4)일 때 한 줄뿐 · 종료 코드 0 도 그때뿐.
      고정 줄 = 머리 바로 다음 줄. 칸마다 개수 전부. 상태 불명 = 경고 줄 · 인계 = 끝 줄 「시험 종료 아님 · 인계 필요(§5-4)」.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Results)
    $byId = @{}
    foreach ($r in @($Results)) { if ($null -ne $r -and $r.Id) { $byId[[string]$r.Id] = $r } }
    $ordered = New-Object System.Collections.Generic.List[object]
    foreach ($cat in $script:CgCatalog) {
        if ($byId.ContainsKey($cat.Id)) { $ordered.Add($byId[$cat.Id]) }
        else {
            $miss = New-CgVerdict -Cat $cat
            if ($cat.Id -eq 'S-D') { $miss.Column = '반자동'; $miss.SdState = '미실행'; $miss.Reason = '결과 없음' }
            else { $miss.Column = '미실행'; $miss.Reason = '결과 없음' }
            $ordered.Add((Complete-CgVerdict $miss))
        }
    }
    $list = $ordered.ToArray()
    $auto = @($list | Where-Object { $_.Id -ne 'S-D' })
    $sd = @($list | Where-Object { $_.Id -eq 'S-D' })[0]
    $allAutoPass = (@($auto | Where-Object { $_.Column -eq 'PASS' }).Count -eq 3)
    $n = @($list | Where-Object { $_.Executed }).Count
    $cuts = @($list | Where-Object { $_.CutIssued }).Count
    $sdState = [string]$sd.SdState

    $exit = 1
    if ($allAutoPass -and $sdState -eq '완료') {
        $header = '합격(도구 범위 4 ID 중 4 · 자동 3 PASS + 반자동 1 완료)'
        $exit = 0
    } elseif ($allAutoPass -and $sdState -eq '실패') {
        $header = ('미합격 — 자동 3 PASS · 반자동 1 실패({0}) · 시험 종료 아님 · 인계 필요(§5-4)' -f $sd.Reason)
    } elseif ($allAutoPass) {
        $header = '미합격(반자동 미실행 · 4 ID 중 3 실행) — 자동 3 PASS · 합격선 「자동 3 + 반자동 1」 미충족'
    } elseif ($cuts -eq 0) {
        $header = ('미합격(측정 0건) — 도구 범위 4 ID 중 {0} 실행 · 칸별 사유 · 반자동 {1}' -f $n, $sdState)
    } else {
        $header = ('미합격(도구 범위 4 ID 중 {0} 실행) — 칸별 사유 · 반자동 {1}' -f $n, $sdState)
    }
    $fixed = '복구 주체 판정 아님 · #27 게이트 통과 아님'
    if ($sd.CutIssued) { $fixed = '손 1회 · #28 「고객 손 0번」 증명 아님 · ' + $fixed }

    $warn = if (@($list | Where-Object { $_.StateUnknown }).Count -gt 0) { '⚠ 대상 상태 불명 · 사람 확인 필요' } else { $null }
    $handoff = (@($list | Where-Object { $_.Handoff }).Count -gt 0)
    $counts = [ordered]@{}
    foreach ($col in $script:CgColumns) { $counts[$col] = @($list | Where-Object { $_.Column -eq $col }).Count }

    $text = New-Object System.Collections.Generic.List[string]
    $text.Add($header)
    $text.Add($fixed)
    if ($warn) { $text.Add($warn) }
    $text.Add(('칸별 개수: ' + (($counts.Keys | ForEach-Object { '{0}={1}' -f $_, $counts[$_] }) -join ' · ')))
    foreach ($r in $list) {
        $line = '- {0} [{1}] {2}' -f $r.Id, $r.Label, $r.Display
        if (@($r.Notes).Count -gt 0) { $line += ' · ' + (@($r.Notes) -join ' · ') }
        $text.Add($line)
        if ($r.Actor) { $text.Add(('  복구 주체(판정 미반영): {0} — {1}' -f $r.Actor, $r.ActorDetail)) }
    }
    if ($handoff) { $text.Add('시험 종료 아님 · 인계 필요(§5-4)') }
    return [pscustomobject][ordered]@{
        Header = $header; FixedLine = $fixed; Warning = $warn; Handoff = $handoff; Counts = $counts
        Executed = $n; Cuts = $cuts; ExitCode = $exit; Results = $list; Text = $text.ToArray()
    }
}

# ─────────────────────────────────────────────── 실물 HTTP(러너 실물 모드 · 시험에선 안 씀)

function Invoke-CgHttpGet {
    # 읽기 GET 하나. 시간 상한 = TimeoutSec · 넘김 · 연결 실패 = Status 0. 다른 주소로 옮겨 가지 않는다(자동 이동 끔).
    # 예외 메시지는 옮기지 않는다(주소 · 도메인 유출 방지) — 종류만 자세히 출력.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Url, [int]$TimeoutSec = 2, [AllowNull()][hashtable]$Headers)
    $handler = $null; $client = $null; $resp = $null
    try {
        Add-Type -AssemblyName System.Net.Http -ErrorAction Stop
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $handler = New-Object System.Net.Http.HttpClientHandler
        $handler.AllowAutoRedirect = $false
        $client = New-Object System.Net.Http.HttpClient($handler)
        $client.Timeout = [TimeSpan]::FromSeconds([math]::Max(1, $TimeoutSec))
        $req = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Get, $Url)
        if ($null -ne $Headers) { foreach ($k in $Headers.Keys) { $null = $req.Headers.TryAddWithoutValidation([string]$k, [string]$Headers[$k]) } }
        $resp = $client.SendAsync($req).GetAwaiter().GetResult()
        $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if ($null -ne $body -and $body.Length -gt 65536) { $body = $body.Substring(0, 65536) }
        return @{ Status = [int]$resp.StatusCode; Body = $body }
    } catch {
        Write-Verbose ("GET 실패({0})" -f $_.Exception.GetType().Name)
        return @{ Status = 0; Body = $null }
    } finally {
        if ($null -ne $resp) { $resp.Dispose() }
        if ($null -ne $client) { $client.Dispose() }
        elseif ($null -ne $handler) { $handler.Dispose() }
    }
}

Export-ModuleMember -Function @(
    'Get-EnvSnapshot', 'Get-GuardDecision', 'Get-ScenarioVerdict', 'Get-Summary',
    'Get-CgConstant', 'Get-CgScenarioCatalog', 'Format-CgUtc', 'ConvertTo-CgUtc', 'ConvertTo-CgSha256Hex', 'Get-CgIdentityHash',
    'Test-CgHex', 'ConvertTo-CgPort', 'ConvertTo-CgSlot', 'Read-CgMarker', 'Read-CgDbConf', 'Read-CgWatchdogServices', 'Read-CgAllowlist',
    'Get-CgAllowlistGitState', 'ConvertFrom-CgImagePath', 'Test-CgExternalAddress', 'ConvertFrom-CgOriginHealth', 'ConvertFrom-CgAppVersion',
    'Test-CgControlTrace', 'Get-CgExtState', 'ConvertFrom-CgFailureActions', 'Measure-CgHistory', 'Measure-CgWatchdogEvents',
    'Get-CgCooldownDecision', 'Invoke-CgHttpGet'
)
