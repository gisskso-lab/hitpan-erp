using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace HitPan.API.Services.LocalSwap;

/// <summary>
/// 교체 일꾼 런처 — 계약 <c>docs/설계/erp/20260930_계약_local-swap_request.md</c> §1·§4.
/// </summary>
/// <remarks>
/// <para>🔴 <b>작업 폴더 = <c>{app}\rollback</c></b>(Program Files — 관리자만 쓴다). <c>%ProgramData%\HitPan</c> 은 Users 가 하위 폴더를
/// 먼저 만들어 가질 수 있어 일꾼 사본·요청서를 바꿔치면 SYSTEM 실행을 얻는다(병렬이슈 01). 그래도 쓰기 전마다
/// <see cref="ISwapFolderGuard"/>(C-8·C-12 공용 판정)를 받는다 — 걸리면 작업 등록 0.</para>
/// <para>🔴 <b>한 번에 하나</b>(병렬이슈 03): <c>swap.lock</c> 을 <see cref="FileMode.CreateNew"/> 로만 만든다(있으면 실패) ·
/// <c>schtasks /Create</c> 에 <c>/F</c> 를 쓰지 않는다(이미 있으면 등록 실패) · 끝난 교체 뒤 10분 쿨다운.</para>
/// <para>🔴 <b>작업 명령줄은 고정 틀</b>(병렬이슈 04): 들어가는 값은 서버가 정한 설치 경로·모드 상수·서버가 만든 32자리 번호뿐.</para>
/// </remarks>
public sealed class LocalSwapLauncher : ILocalSwapLauncher
{
    public const string TaskName = "HitPan-LocalSwap";
    public const string WorkFolderName = "rollback";
    public const string RequestFileName = "request.json";
    public const string LockFileName = "swap.lock";
    public const string ScriptFileName = "local-swap.ps1";

    /// <summary>열린 요청(<c>requested</c>·<c>running</c>)이 이보다 오래 안 바뀌면 죽은 것으로 본다.</summary>
    public static readonly TimeSpan OpenRequestStale = TimeSpan.FromMinutes(30);
    /// <summary>끝난 교체(두 모드 합쳐) 뒤 다음 교체까지(병렬이슈 03).</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);
    /// <summary>워치독 <c>UpdateLockFile.Ttl</c> 과 같은 값 — 그 규칙을 따를 뿐 워치독 코드는 안 바꾼다.</summary>
    public static readonly TimeSpan WatchdogLockTtl = TimeSpan.FromMinutes(15);
    /// <summary>schtasks <c>/TR</c> 길이 한도.</summary>
    public const int MaxTaskCommandLength = 261;

    private static readonly string[] Parts = { "api", "web", "watchdog" };
    private static readonly Regex TicketShape = new("^[0-9a-f]{32}$", RegexOptions.CultureInvariant);

    private readonly ILocalSwapEnvironment _env;
    private readonly ISchtasksRunner _schtasks;
    private readonly ISwapFolderGuard _guard;
    private readonly ILogger<LocalSwapLauncher> _logger;
    private readonly object _gate = new();

    public LocalSwapLauncher(ILocalSwapEnvironment env, ISchtasksRunner schtasks, ISwapFolderGuard guard, ILogger<LocalSwapLauncher> logger)
    {
        _env = env;
        _schtasks = schtasks;
        _guard = guard;
        _logger = logger;
    }

    /// <summary><c>{app}\rollback</c>. 설치 루트를 못 찾으면 null.</summary>
    public static string? WorkDir(string? appRoot) => appRoot is null ? null : Path.Combine(appRoot, WorkFolderName);

    public SwapRequest? ReadLast()
    {
        var work = WorkDir(_env.AppRoot);
        if (work is null) return null;
        var path = Path.Combine(work, RequestFileName);
        if (!File.Exists(path)) return null;
        try
        {
            return SwapRequest.FromJson(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "[LocalSwap] 요청서를 읽지 못했습니다: {Path}", path);
            return null;
        }
    }

    public string? CheckBusy()
    {
        if (!_env.IsWindows) return SwapReasons.NotWindows;
        var app = _env.AppRoot;
        if (app is null) return SwapReasons.RequestInvalid;
        var now = _env.UtcNow;

        // ── 두 수동 동작(한 틀) ──
        var last = ReadLast();
        if (last is not null)
        {
            var age = now - last.LastTouchedUtc;
            if (SwapStates.IsOpen(last.State) && age < OpenRequestStale) return SwapReasons.SwapInProgress;
            if (!SwapStates.IsOpen(last.State) && last.State != SwapStates.Refused && age >= TimeSpan.Zero && age < Cooldown)
                return SwapReasons.Cooldown;
        }
        if (IsLockFresh(Path.Combine(app, WorkFolderName, LockFileName), OpenRequestStale, now, 1)) return SwapReasons.SwapInProgress;
        if (TaskExists(TaskName)) return SwapReasons.SwapInProgress;

        // ── 자동 업데이트(워치독) 진행 표식 — 설계 §1 P-d ──
        if (File.Exists(Path.Combine(app, "update-swap.marker"))) return SwapReasons.UpdateInProgress;
        if (Directory.Exists(Path.Combine(app, "watchdog.new"))) return SwapReasons.UpdateInProgress;
        foreach (var p in Parts)
            if (Directory.Exists(Path.Combine(app, p + ".rbk"))) return SwapReasons.UpdateInProgress;
        if (IsLockFresh(Path.Combine(app, "update.lock"), WatchdogLockTtl, now, 0)) return SwapReasons.UpdateInProgress;
        if (TaskExists("HitPanWatchdogSelfReplace") || TaskExists("HitPanWatchdogSelfReplaceRecover"))
            return SwapReasons.UpdateInProgress;
        return null;
    }

    public SwapLaunchResult Launch(SwapLaunchInput input)
    {
        lock (_gate)
        {
            return LaunchCore(input);
        }
    }

    private SwapLaunchResult LaunchCore(SwapLaunchInput input)
    {
        if (!_env.IsWindows) return SwapLaunchResult.Refuse(SwapReasons.NotWindows);
        var app = _env.AppRoot;
        var slot = _env.Slot;
        if (app is null || slot is null or < 1) return SwapLaunchResult.Refuse(SwapReasons.RequestInvalid);
        if (input.Mode is not (SwapModes.Update or SwapModes.Rollback)) return SwapLaunchResult.Refuse(SwapReasons.RequestInvalid);
        if (!File.Exists(_env.ScriptSourcePath)) return SwapLaunchResult.Refuse(SwapReasons.ScriptMissing);

        var ticket = input.Ticket ?? NewTicket();
        if (!TicketShape.IsMatch(ticket)) return SwapLaunchResult.Refuse(SwapReasons.TicketInvalid);

        var work = Path.Combine(app, WorkFolderName);
        var run = Path.Combine(work, "run");
        try
        {
            _guard.EnsureSafe(work);
            _guard.EnsureSafe(run);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "[LocalSwap] 작업 폴더가 안전하지 않아 교체를 걸지 않았습니다(병렬이슈 01).");
            return SwapLaunchResult.Refuse(SwapReasons.FolderUnsafe);
        }

        if (CheckBusy() is { } busy) return SwapLaunchResult.Refuse(busy);

        var lockPath = Path.Combine(work, LockFileName);
        if (!TryCreateLock(lockPath, ticket)) return SwapLaunchResult.Refuse(SwapReasons.SwapInProgress);

        var ok = false;
        try
        {
            var request = new SwapRequest
            {
                Ticket = ticket,
                Mode = input.Mode,
                State = SwapStates.Requested,
                From = input.From,
                To = input.To,
                Material = input.Material,
                AppRoot = app,
                Slot = slot.Value,
                ApiPort = _env.ApiPort,
                RequestedBy = input.RequestedBy,
                RequestedAt = _env.UtcNow,
                Entry = input.Entry,
                AutoState = input.AutoState,
            };
            WriteAtomic(Path.Combine(work, RequestFileName), request.ToJson());
            File.Copy(_env.ScriptSourcePath, Path.Combine(run, ScriptFileName), overwrite: true);

            var command = BuildTaskCommand(Path.Combine(run, ScriptFileName), input.Mode, ticket);
            if (command.Length > MaxTaskCommandLength)
            {
                _logger.LogWarning("[LocalSwap] 작업 명령줄이 {Len}자라 schtasks 한도({Max})를 넘습니다 — 설치 경로가 너무 깁니다.",
                    command.Length, MaxTaskCommandLength);
                MarkRefused(work, request, SwapReasons.TaskRegisterFailed);
                return SwapLaunchResult.Refuse(SwapReasons.TaskRegisterFailed);
            }

            var at = DateTime.Now.AddMinutes(1);
            var sd = at.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
            var st = at.ToString("HH:mm", CultureInfo.InvariantCulture);
            // /F 없음 — 같은 이름이 이미 있으면 등록 실패(병렬이슈 03)
            var create = _schtasks.Run(
                $"/Create /TN \"{TaskName}\" /TR \"{command}\" /SC ONCE /SD {sd} /ST {st} /RU SYSTEM /RL HIGHEST");
            if (create != 0)
            {
                _logger.LogWarning("[LocalSwap] 1회용 작업 등록 실패(exit={Code}) — 교체를 걸지 않았습니다(U-3).", create);
                MarkRefused(work, request, SwapReasons.TaskRegisterFailed);
                return SwapLaunchResult.Refuse(SwapReasons.TaskRegisterFailed);
            }
            var start = _schtasks.Run($"/Run /TN \"{TaskName}\"");
            if (start != 0)
            {
                _logger.LogWarning("[LocalSwap] 1회용 작업 실행 실패(exit={Code}) — 작업을 지우고 멈춥니다.", start);
                _schtasks.Run($"/Delete /TN \"{TaskName}\" /F");
                MarkRefused(work, request, SwapReasons.TaskRegisterFailed);
                return SwapLaunchResult.Refuse(SwapReasons.TaskRegisterFailed);
            }

            ok = true;
            _logger.LogInformation("[LocalSwap] 교체 시작 — {Mode} {From} → {To} · 재료 {Kind} · 번호 {Ticket}",
                input.Mode, input.From, input.To, input.Material.Kind, ticket);
            return new SwapLaunchResult(true, SwapReasons.Ok, ticket);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "[LocalSwap] 요청서·일꾼 사본을 쓰지 못해 교체를 걸지 않았습니다.");
            return SwapLaunchResult.Refuse(SwapReasons.TaskRegisterFailed);
        }
        finally
        {
            // 일꾼이 돌기 시작하면 잠금은 일꾼이 푼다. 여기서 끝나면 런처가 푼다.
            if (!ok) TryDelete(lockPath);
        }
    }

    /// <summary>
    /// schtasks <c>/TR</c> 고정 틀. 값 = 설치 경로(서버) · 모드 상수 · 서버가 만든 번호뿐(병렬이슈 04).
    /// 경로는 <c>\"</c> 로 감싼다 — 공백이 있으면 인자가 쪼개진다(워치독 <c>PsRunner</c> 와 같은 규칙).
    /// </summary>
    public static string BuildTaskCommand(string scriptPath, string mode, string ticket)
    {
        if (mode is not (SwapModes.Update or SwapModes.Rollback)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!TicketShape.IsMatch(ticket)) throw new ArgumentException("ticket shape", nameof(ticket));
        return "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \\\"" + scriptPath + "\\\" -Mode " + mode + " -Ticket " + ticket;
    }

    /// <summary>1회용 번호 — 32자리 소문자 16진(일꾼 <c>-Ticket</c> 모양과 같다).</summary>
    public static string NewTicket() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private bool TaskExists(string name) => _schtasks.Run($"/Query /TN \"{name}\"") == 0;

    /// <summary>파일 내용 <c>UTC|…</c> 기준으로 유효한가(워치독 <c>UpdateLockFile</c> 와 같은 판정 — 미래·못 읽음 = 무효).</summary>
    /// <param name="stampIndex"><c>|</c> 로 나눈 몇 번째 칸이 시각인가 — <c>update.lock</c>(워치독) = 0 · <c>swap.lock</c>(<c>번호|UTC</c>) = 1.</param>
    public static bool IsLockFresh(string path, TimeSpan ttl, DateTime nowUtc, int stampIndex)
    {
        if (!File.Exists(path)) return false;
        string raw;
        try { raw = File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 못 읽는 잠금은 누가 쥐고 있다는 뜻이다 — 보수적으로 바쁨.
            return true;
        }
        var parts = raw.Trim().Split('|');
        if (parts.Length <= stampIndex) return false;
        if (!DateTime.TryParse(parts[stampIndex], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)) return false;
        var age = nowUtc - t.ToUniversalTime();
        return age >= TimeSpan.Zero && age <= ttl;
    }

    private bool TryCreateLock(string path, string ticket)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var body = Encoding.ASCII.GetBytes(ticket + "|" + _env.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                fs.Write(body, 0, body.Length);
                return true;
            }
            catch (IOException ex) when (File.Exists(path))
            {
                if (attempt == 0 && !IsLockFresh(path, OpenRequestStale, _env.UtcNow, 1))
                {
                    _logger.LogWarning(ex, "[LocalSwap] 묵은 교체 잠금({Min}분 넘음)을 지우고 다시 잡습니다.", OpenRequestStale.TotalMinutes);
                    TryDelete(path);
                    continue;
                }
                return false;
            }
        }
        return false;
    }

    private void MarkRefused(string work, SwapRequest request, string reason)
    {
        request.State = SwapStates.Refused;
        request.Reason = reason;
        request.UpdatedAt = _env.UtcNow;
        try { WriteAtomic(Path.Combine(work, RequestFileName), request.ToJson()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "[LocalSwap] 거부 상태를 요청서에 적지 못했습니다({Reason}).", reason);
        }
    }

    private static void WriteAtomic(string path, string text)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    private void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "[LocalSwap] 파일을 지우지 못했습니다: {Path}", path);
        }
    }
}

/// <summary>설치 환경 — 실제 PC.</summary>
public sealed class LocalSwapEnvironment : ILocalSwapEnvironment
{
    public bool IsWindows => OperatingSystem.IsWindows();

    public string? AppRoot
    {
        get
        {
            var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var parent = Directory.GetParent(baseDir)?.FullName;
            // {app}\watchdog 가 있어야 진짜 설치 루트다(WatchdogHandoffBootstrap.ResolveAppRoot 와 같은 판정)
            return parent is not null && Directory.Exists(Path.Combine(parent, "watchdog")) ? parent : null;
        }
    }

    public string ScriptSourcePath => Path.Combine(AppContext.BaseDirectory, "Rollback", LocalSwapLauncher.ScriptFileName);

    public string CurrentVersion => VersionInfo.Current;

    // 20260930작1 I-API 7 — SLOT_INDEX·API_PORT 는 워치독과 「같은 한 벌」로 읽는다(링크 컴파일한 워치독 원본 DbConfReader ·
    //   HitPan.API.csproj <Compile Link>). 일꾼이 이 슬롯으로 keepalive 작업 이름을 만들고, 워치독 UpdateProcessGate 도
    //   DbConfReader.GetValue("SLOT_INDEX") 로 같은 이름을 만든다 ⇒ 두 쪽 판정(찾는 db.conf 순서·환경변수 폴백 여부)이 갈리면
    //   서로 다른 작업을 멈추고 켠다. 종전 TenantConfigReader 는 exe 폴더를 먼저 보고 환경변수로 폴백해 워치독과 달랐다.
    public int? Slot => int.TryParse(HitPan.Watchdog.DbConfReader.GetValue("SLOT_INDEX"), out var s) && s >= 1 ? s : null;

    public int ApiPort => int.TryParse(HitPan.Watchdog.DbConfReader.GetValue("API_PORT"), out var p) && p > 0 ? p : 5257;

    public string WatchdogStagingDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HitPan", "Updates", "staging");

    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>schtasks — 실제 PC(창 없이 · 셸 없이).</summary>
public sealed class SchtasksRunner : ISchtasksRunner
{
    private readonly ILogger<SchtasksRunner> _logger;

    public SchtasksRunner(ILogger<SchtasksRunner> logger) => _logger = logger;

    public int Run(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return -1;
            p.StandardOutput.ReadToEnd();
            var err = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(30_000))
            {
                p.Kill(entireProcessTree: true);
                _logger.LogWarning("[LocalSwap] schtasks 30초 초과: {Args}", arguments);
                return -1;
            }
            if (p.ExitCode != 0 && !arguments.StartsWith("/Query", StringComparison.Ordinal))
                _logger.LogWarning("[LocalSwap] schtasks exit={Code} {Args} {Err}", p.ExitCode, arguments, err.Trim());
            return p.ExitCode;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _logger.LogWarning(ex, "[LocalSwap] schtasks 실행 실패: {Args}", arguments);
            return -1;
        }
    }
}

/// <summary>작업 폴더 안전 판정 — 백업 폴더와 같은 C-8·C-12 공용 판정을 부른다(복붙 0 · 병렬이슈 01).</summary>
public sealed class SwapFolderGuard : ISwapFolderGuard
{
    public void EnsureSafe(string path) => HitPan.Application.Services.BackupService.EnsureRestrictedSystemFolder(path);
}
