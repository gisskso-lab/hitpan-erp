using System.Diagnostics;

namespace HitPan.Watchdog.AutoUpdate;

/// <summary>
/// 업데이트 교체 구간의 프로세스 정지·복원 (작업지시서 20260715작1 W4-1, 사장님 결재 2026-07-16).
///
/// ■ 왜 필요한가 — keepalive 가 교체를 방해한다
///   인스톨러는 ERP 를 살려두려고 keepalive 예약작업을 1분마다 돌린다
///   (HitPan-ERP-{API|WEB}-keepalive-{슬롯}, .iss). 이 작업은 생사 검사 없이 무조건 schtasks /Run 을 건다.
///   그래서 파일을 교체하려고 ERP 를 죽이면 1분 안에 되살아나 dll 을 붙잡고, 교체가 실패한다.
///   ※ .iss 주석은 keepalive 가 "무해"하다고 적었는데, 그 근거("살아있으면 no-op")는 프로세스가 살아있을
///     때만 참이다. 교체 시나리오에서 그 전제가 깨진다.
///
/// ■ 왜 /End 가 아니라 /Change /DISABLE 인가
///   /End 는 실행 중 인스턴스만 죽이고 트리거는 살려둔다 → 1분 뒤 또 돈다. /DISABLE 만이 트리거를 멈춘다.
///
/// ■ 최대 급소 — "keepalive 가 영영 꺼진 채 남는다"
///   워치독이 교체 중 죽으면 keepalive 가 꺼진 채로 남아 ERP 가 영영 안 뜬다.
///   = 업데이트가 고객 ERP 를 영구 정지시키는 최악의 사고(재설치 말고는 복구 불가 = 사장님이 없앤 그 경로).
///   그래서 3중 보장을 둔다:
///     ① try/finally 무조건 복원        — 정상·예외·롤백 경로 (강제종료·정전은 못 막음)
///     ② 부팅 시 자동 복원 예약작업      — /DISABLE '전에' 등록하고 /Query 로 존재를 확인한 뒤에야 진행.
///                                        등록 실패면 아예 시작하지 않는다(바닥 없이 떨어지지 않는다).
///     ③ 워치독 기동 시 자가 점검        — UpdateLockFile 표식이 없거나 만료면 즉시 복원
///                                        (sc failure 5초·Guardian 5분이 워치독을 되살리므로 재부팅 없이 최대 5분)
///   셋 중 하나만 살아도 ERP 는 돌아온다.
///
/// ■ 건드리지 않는 것
///   cloudflared(터널 = CS 접근 경로)·MariaDB(데이터 = 롤백 자산)는 교체 대상이 아니므로 정지하지 않는다.
///
/// 헌법 정합: #15(침묵 금지) / #19(그냥 되어야 한다) / #20(워크플로우 끊김 0) / #30(자가 회복) / #34.
/// </summary>
public sealed class UpdateProcessGate
{
    /// <summary>부팅 시 keepalive 를 되살리는 안전망 작업 이름(② 보장).</summary>
    public const string RestoreTaskName = "HitPan-ERP-keepalive-restore";

    private readonly ILogger<UpdateProcessGate> _logger;

    public UpdateProcessGate(ILogger<UpdateProcessGate> logger) => _logger = logger;

    /// <summary>
    /// db.conf 의 SLOT_INDEX 로 이 PC 의 슬롯을 판정한다(DbConfReader 와 동일 출처).
    /// 슬롯을 모르면 어떤 작업을 끄고 켜야 할지 알 수 없으므로 업데이트를 진행하지 않는다.
    /// </summary>
    public int? ResolveSlot()
    {
        var raw = DbConfReader.GetValue("SLOT_INDEX");
        if (int.TryParse(raw?.Trim(), out var slot) && slot >= 1) return slot;

        _logger.LogError("[Update/Gate] db.conf 에서 SLOT_INDEX 를 읽지 못했습니다(값 '{Raw}') — " +
                         "정지할 작업 이름을 알 수 없어 업데이트를 진행하지 않습니다.", raw ?? "(없음)");
        return null;
    }

    public static string ApiTask(int slot) => $"HitPan-ERP-API-tenant-{slot}";
    public static string WebTask(int slot) => $"HitPan-ERP-WEB-tenant-{slot}";
    public static string ApiKeepalive(int slot) => $"HitPan-ERP-API-keepalive-{slot}";
    public static string WebKeepalive(int slot) => $"HitPan-ERP-WEB-keepalive-{slot}";

    /// <summary>
    /// 교체를 위해 ERP(API·Web)를 정지시킨다. 성공(true) 시에만 호출부가 파일 교체로 진입해야 한다.
    ///
    /// 순서가 중요하다:
    ///   0. ② 복원 안전망 등록 + /Query 확인   ← 실패하면 여기서 중단(아직 아무것도 끄지 않았다)
    ///   1. keepalive /DISABLE (API·Web)        ← 트리거를 멈춘다. 이걸 먼저 해야 3~5 가 유효하다
    ///   2. tenant 작업 /End
    ///   3. taskkill (End 불응 대비)
    ///   4. 잔존 확인 폴링
    /// </summary>
    public async Task<bool> StopForSwapAsync(int slot, CancellationToken ct)
    {
        // ── 0. ② 안전망 먼저 (등록 확인 전엔 아무것도 끄지 않는다) ──
        if (!RegisterRestoreSafetyNet(slot))
        {
            _logger.LogError("[Update/Gate] 🛑 부팅 복원 안전망을 등록하지 못해 업데이트를 시작하지 않습니다. " +
                             "이 상태로 keepalive 를 끄면, 워치독이 도중에 죽었을 때 ERP 가 영영 뜨지 않습니다.");
            return false;
        }

        // ── 1. keepalive 트리거 정지 (/End 가 아니라 /DISABLE) ──
        var k1 = RunSchtasks($"/Change /TN \"{ApiKeepalive(slot)}\" /DISABLE", "keepalive(API) 정지");
        var k2 = RunSchtasks($"/Change /TN \"{WebKeepalive(slot)}\" /DISABLE", "keepalive(Web) 정지");
        if (!k1 || !k2)
        {
            // 하나라도 못 껐으면 1분 뒤 ERP 가 되살아나 교체가 깨진다. 이미 끈 것은 finally 에서 복원된다.
            _logger.LogError("[Update/Gate] 🛑 keepalive 를 정지하지 못했습니다(API={A}, Web={W}) — 교체 중 ERP 가 " +
                             "되살아나 파일을 잠급니다. 업데이트를 중단합니다.", k1, k2);
            return false;
        }

        // ── 2. 실행 중 ERP 종료 ──
        RunSchtasks($"/End /TN \"{ApiTask(slot)}\"", "ERP API 종료");
        RunSchtasks($"/End /TN \"{WebTask(slot)}\"", "ERP Web 종료");

        // ── 3. /End 에 불응하는 프로세스 강제 종료 ──
        RunProcess("taskkill.exe", "/F /IM HitPan.API.exe", "ERP API 강제 종료", allowFailure: true);

        // ── 4. 잔존 확인 (최대 10초) — 여기서 살아있으면 교체가 실패한다 ──
        var gone = await WaitUntilProcessGoneAsync("HitPan.API", TimeSpan.FromSeconds(10), ct);
        if (!gone)
        {
            _logger.LogError("[Update/Gate] 🛑 ERP API 프로세스가 10초 안에 종료되지 않았습니다 — 파일이 잠겨 교체가 실패합니다. 업데이트를 중단합니다.");
            return false;
        }

        _logger.LogInformation("[Update/Gate] 교체 준비 완료 — keepalive 정지·ERP 종료 확인(슬롯 {Slot})", slot);
        return true;
    }

    /// <summary>
    /// keepalive 를 되살린다. try/finally 에서 무조건 호출한다(① 보장).
    /// 성공·실패·롤백 어느 경로든 ERP 는 살아나야 한다 — 이 메서드는 예외를 던지지 않는다.
    ///
    /// 반환값이 중요하다(검증팀 R-3 적발): 호출부는 이 값이 true 일 때만 ② 안전망을 제거해야 한다.
    ///   복원에 실패했는데 안전망까지 지우면 ①·② 가 동시에 사라져 3중이 1중으로 무너진다.
    /// </summary>
    /// <returns>API·Web keepalive 를 모두 되살렸으면 true.</returns>
    public bool RestoreKeepalive(int slot)
    {
        // /ENABLE 은 멱등이다 — 이미 켜져 있어도 성공이며 부작용이 없다.
        //   따라서 중복 호출(② 안전망과 겹치는 경우 등)은 무해하다. 반대(안 켜짐)만이 사고다.
        var a = RunSchtasks($"/Change /TN \"{ApiKeepalive(slot)}\" /ENABLE", "keepalive(API) 복원");
        var w = RunSchtasks($"/Change /TN \"{WebKeepalive(slot)}\" /ENABLE", "keepalive(Web) 복원");

        if (a && w)
        {
            _logger.LogInformation("[Update/Gate] keepalive 복원 완료(슬롯 {Slot}) — ERP 자동 유지가 다시 작동합니다.", slot);
            return true;
        }

        // 헌법 #15: 여기가 조용히 실패하면 ERP 가 영영 안 뜬다. 반드시 흔적을 남긴다.
        _logger.LogError("[Update/Gate] ⚠️ keepalive 복원 실패(API={A}, Web={W}, 슬롯 {Slot}) — " +
                         "'{Task}' 안전망을 남겨둡니다. 재부팅 1회로 자동 복원되며, 워치독 재기동 시 자가 점검도 재시도합니다.",
                         a, w, slot, RestoreTaskName);
        return false;
    }

    /// <summary>정상 완료 후 ② 안전망을 정리한다. 남아 있어도 무해하지만(멱등 /ENABLE), 흔적을 남기지 않는다.</summary>
    public void RemoveRestoreSafetyNet()
    {
        RunSchtasks($"/Delete /TN \"{RestoreTaskName}\" /F", "부팅 복원 안전망 정리", allowFailure: true);
    }

    /// <summary>
    /// ③ 워치독 기동 시 자가 점검 — keepalive 가 꺼진 채 방치된 상태를 되살린다.
    ///
    /// 언제 이 상황이 생기나: 업데이트가 keepalive 를 끈 뒤 워치독이 죽으면(크래시·강제종료·정전)
    ///   ① try/finally 가 못 돌아 keepalive 가 꺼진 채 남는다 → ERP 가 영영 안 뜬다.
    ///   워치독은 sc failure(5초)·Guardian(5분)이 되살리므로, 기동 때 이걸 점검하면
    ///   재부팅을 기다리지 않고 최대 5분 안에 ERP 가 복구된다(② 안전망보다 빠른 실질 경로).
    ///
    /// 판정: 표식(UpdateLockFile)이 '업데이트 진행 중'이면 손대지 않는다(진짜 교체 중일 수 있다).
    ///   표식이 없거나 만료·손상이면 → 방치된 것으로 보고 즉시 복원.
    ///   표식 판정이 애매하면 '복원한다' 쪽으로 기운다 — keepalive 가 잘못 켜지면 ERP 가 살아날 뿐이고,
    ///   반대(영영 안 켜짐)는 ERP 영구 정지다.
    ///
    /// 이 메서드는 예외를 던지지 않는다. 워치독 기동을 막으면 안 된다.
    /// </summary>
    public void SelfHealKeepaliveIfAbandoned(UpdateLockFile lockFile)
    {
        try
        {
            if (lockFile.IsUpdateInProgress())
            {
                _logger.LogInformation("[Update/Gate] 업데이트 진행 표식이 유효합니다 — keepalive 자가 점검을 건너뜁니다(교체 중일 수 있음).");
                return;
            }

            var slot = ResolveSlot();
            if (slot is null) return;   // 사유는 ResolveSlot 이 이미 기록했다.

            if (!IsTaskDisabled(ApiKeepalive(slot.Value)) && !IsTaskDisabled(WebKeepalive(slot.Value)))
                return;   // 정상 상태 — 조용히 통과(매 기동마다 로그를 남기지 않는다).

            _logger.LogWarning("[Update/Gate] 🔧 keepalive 가 꺼진 채 방치돼 있습니다(업데이트 흔적 없음) — " +
                               "중단된 업데이트의 잔재로 보고 즉시 복원합니다. 복원하지 않으면 ERP 가 다시 뜨지 않습니다.");
            // 복원에 성공했을 때만 안전망을 치운다(검증팀 R-3 정합) — 실패했으면 재부팅 복원망을 남겨야 한다.
            if (RestoreKeepalive(slot.Value)) RemoveRestoreSafetyNet();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Update/Gate] keepalive 자가 점검 중 예외 — 워치독 기동은 계속합니다.");
        }
    }

    /// <summary>
    /// schtasks /Query /FO LIST 출력이 '사용 안 함(Disabled)' 상태를 가리키는지 판정한다.
    ///
    /// 왜 순수 함수로 분리했나 (검증팀 R-3 적발): schtasks 출력은 OS 표시 언어를 따른다.
    ///   개발 PC 와 CI 는 영어라 한국어 분기('사용 안 함')를 **아무도 밟지 않는다** — 문자열이 틀려도
    ///   테스트가 통과해 회귀를 못 잡는다(W4-0 에서 겪은 "보호막이 허상" 사고와 같은 병).
    ///   실제 프로세스 실행에서 떼어내 두 로케일 출력을 모두 테스트로 고정한다.
    ///   ※ 한국어 표기 '사용 안 함'(띄어쓰기 포함)은 ko-KR 실제 출력으로 검증팀이 확인했다.
    /// </summary>
    public static bool ParseIsDisabled(string queryOutput)
        => queryOutput.Contains("Disabled", StringComparison.OrdinalIgnoreCase)
           || queryOutput.Contains("사용 안 함", StringComparison.Ordinal)
           // 표기 흔들림 대비(띄어쓰기 없는 변형). 오탐 위험은 없다 — Enabled 는 'Ready/준비'로 나온다.
           || queryOutput.Contains("사용 안함", StringComparison.Ordinal);

    /// <summary>예약작업이 '사용 안 함' 상태인지 확인한다. 조회 실패 시 false(모르면 건드리지 않는다).</summary>
    private bool IsTaskDisabled(string taskName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Query /TN \"{taskName}\" /FO LIST",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p is null) return false;

            var outText = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10000);
            if (p.ExitCode != 0) return false;   // 작업 자체가 없음 — 이 메서드가 다룰 상황이 아니다.

            return ParseIsDisabled(outText);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Update/Gate] 예약작업 상태 조회 실패({Task}) — 판정하지 않습니다.", taskName);
            return false;
        }
    }

    /// <summary>
    /// ② 부팅 시 keepalive 를 되살리는 예약작업을 등록하고, /Query 로 실제 존재를 확인한다.
    ///
    /// 확인이 핵심이다(CTO 적발): 종전 설계는 등록 반환코드를 검사하지 않아, 등록이 실패해도 그대로
    /// keepalive 정지로 진행했다 — 바닥이 없는 걸 모른 채 떨어지는 셈이다.
    /// </summary>
    private bool RegisterRestoreSafetyNet(int slot)
    {
        // 부팅 시 두 keepalive 를 /ENABLE 하는 1회성 작업. 명령 안에 큰따옴표가 겹치지 않도록 cmd /c 로 감싼다.
        var inner = $"cmd /c schtasks /Change /TN {ApiKeepalive(slot)} /ENABLE & schtasks /Change /TN {WebKeepalive(slot)} /ENABLE";
        var args = $"/Create /F /TN \"{RestoreTaskName}\" /TR \"{inner}\" /SC ONSTART /RU SYSTEM /RL HIGHEST";

        if (!RunSchtasks(args, "부팅 복원 안전망 등록"))
            return false;

        // 등록이 '성공'을 반환해도 실제로 존재하는지 확인한다 — 반환코드만 믿지 않는다.
        if (!RunSchtasks($"/Query /TN \"{RestoreTaskName}\"", "부팅 복원 안전망 확인"))
        {
            _logger.LogError("[Update/Gate] 안전망 작업이 등록됐다고 보고됐으나 조회되지 않습니다: {Task}", RestoreTaskName);
            return false;
        }

        _logger.LogInformation("[Update/Gate] 부팅 복원 안전망 등록·확인 완료 ({Task}) — " +
                               "업데이트가 도중에 중단돼도 재부팅 1회로 ERP 가 복구됩니다.", RestoreTaskName);
        return true;
    }

    private async Task<bool> WaitUntilProcessGoneAsync(string processName, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (Process.GetProcessesByName(processName).Length == 0) return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Update/Gate] 프로세스 조회 실패({Name}) — 종료된 것으로 간주합니다.", processName);
                return true;
            }
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
        return false;
    }

    private bool RunSchtasks(string args, string what, bool allowFailure = false)
        => RunProcess("schtasks.exe", args, what, allowFailure);

    /// <summary>외부 명령 1회 실행. 실패는 로그로 남기고 bool 로 알린다(예외를 던지지 않는다).</summary>
    private bool RunProcess(string exe, string args, string what, bool allowFailure = false)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p is null)
            {
                _logger.LogError("[Update/Gate] {What}: 프로세스를 시작하지 못했습니다 ({Exe})", what, exe);
                return false;
            }

            // 파이프 교착 방지 — 출력을 먼저 비동기로 받아두고 기다린다.
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(30000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                _logger.LogError("[Update/Gate] {What}: 30초 안에 끝나지 않아 중단했습니다.", what);
                return false;
            }

            if (p.ExitCode == 0)
            {
                _logger.LogInformation("[Update/Gate] {What} — 성공", what);
                return true;
            }

            var err = (stderr.Result + stdout.Result).Trim();
            if (allowFailure)
                _logger.LogInformation("[Update/Gate] {What}: 종료코드 {Code}(무시 가능) {Err}", what, p.ExitCode, err);
            else
                _logger.LogError("[Update/Gate] {What}: 실패 (종료코드 {Code}) {Err}", what, p.ExitCode, err);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Update/Gate] {What}: 예외 발생", what);
            return false;
        }
    }
}
