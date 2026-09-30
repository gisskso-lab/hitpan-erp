using System.Collections.Concurrent;
using HitPan.API.Services.LocalSwap;

namespace HitPan.API.Services.LocalRollback;

/// <summary>
/// 수동 되돌리기 — 판정(읽기만) + 1회용 확인 번호 + 런처 호출. 20260930작1 갈래 A.
/// </summary>
/// <remarks>
/// <para>🔴 <b>고객 자료 불변</b>(설계 §5): 생성자 인자에 자료 연결·저장소 0. 판정은 파일 존재·글자 파일·zip 목차뿐.</para>
/// <para>🔴 <b>화면 값 불신</b>(병렬이슈 04): [예] 요청은 번호 하나만 받는다. 판·재료는 여기서 다시 계산하고,
/// 번호를 줄 때 본 재료와 다르면 거부한다(그 사이 자동 업데이트가 지나갔다).</para>
/// <para>L-2(사장님 · 늘 열림): 업데이트가 막혔는지(P-b)는 전제에서 뺐다 — 재료·메인PC·관리자·바쁨 아님이면 연다.</para>
/// </remarks>
public sealed class LocalRollbackService : ILocalRollbackService
{
    /// <summary>1회용 확인 번호 유효 시간(병렬이슈 03).</summary>
    public static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(10);

    private sealed record Issued(string UserId, string Kind, string Path, string To, DateTime IssuedUtc);

    private readonly ILocalSwapLauncher _launcher;
    private readonly ILocalSwapEnvironment _env;
    private readonly ILogger<LocalRollbackService> _logger;
    private readonly ConcurrentDictionary<string, Issued> _tickets = new(StringComparer.Ordinal);

    public LocalRollbackService(ILocalSwapLauncher launcher, ILocalSwapEnvironment env, ILogger<LocalRollbackService> logger)
    {
        _launcher = launcher;
        _env = env;
        _logger = logger;
    }

    public LocalRollbackStatus GetStatus(string userId)
    {
        PurgeExpired();
        var current = _env.CurrentVersion;
        var last = ToLast(_launcher.ReadLast());
        var (material, reason) = Judge();
        if (material is null)
            return new LocalRollbackStatus(false, reason, current, null, null, null, (int)TicketLifetime.TotalMinutes, last);

        var ticket = LocalSwapLauncher.NewTicket();
        _tickets[ticket] = new Issued(userId, material.Kind, material.Path, material.Version, _env.UtcNow);
        return new LocalRollbackStatus(true, SwapReasons.Ok, current, material.Version, material.Kind, ticket,
            (int)TicketLifetime.TotalMinutes, last);
    }

    public SwapLaunchResult Start(string userId, string? ticket)
    {
        PurgeExpired();
        if (string.IsNullOrWhiteSpace(ticket) || !_tickets.TryRemove(ticket, out var issued))
            return SwapLaunchResult.Refuse(SwapReasons.TicketInvalid);
        if (!string.Equals(issued.UserId, userId, StringComparison.Ordinal) || _env.UtcNow - issued.IssuedUtc > TicketLifetime)
            return SwapLaunchResult.Refuse(SwapReasons.TicketInvalid);

        var (material, reason) = Judge();
        if (material is null) return SwapLaunchResult.Refuse(reason);
        if (material.Kind != issued.Kind || material.Version != issued.To ||
            !string.Equals(material.Path, issued.Path, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("[LocalRollback] 확인 번호를 준 뒤 재료가 바뀌었습니다({OldKind} {OldTo} → {Kind} {To}) — 거부.",
                issued.Kind, issued.To, material.Kind, material.Version);
            return SwapLaunchResult.Refuse(SwapReasons.TicketInvalid);
        }

        var result = _launcher.Launch(new SwapLaunchInput(
            Mode: SwapModes.Rollback,
            From: _env.CurrentVersion,
            To: material.Version,
            Material: new SwapMaterial { Kind = material.Kind, Path = material.Path, Sha256 = null },
            RequestedBy: userId,
            Entry: SwapEntries.Menu,
            AutoState: null,
            Ticket: ticket));
        _logger.LogInformation("[LocalRollback] 되돌리기 요청 — {From} → {To} · 재료 {Kind} · 사용자 {User} · 결과 {Reason}",
            _env.CurrentVersion, material.Version, material.Kind, userId, result.Reason);
        return result;
    }

    /// <summary>전제 판정(설계 §1 P-c~P-f · P-a 는 컨트롤러 문). 순서: 바쁨 → 재료 → 저장 공간.</summary>
    private (RollbackMaterial? Material, string Reason) Judge()
    {
        if (_launcher.CheckBusy() is { } busy) return (null, busy);
        var app = _env.AppRoot;
        if (app is null) return (null, SwapReasons.RequestInvalid);
        try
        {
            var material = RollbackMaterialFinder.Find(app, _env.WatchdogStagingDir, _env.CurrentVersion, out var reason);
            if (material is null) return (null, reason);

            var need = RollbackMaterialFinder.RequiredFreeBytes(material);
            var root = Path.GetPathRoot(app);
            if (root is not null && new DriveInfo(root).AvailableFreeSpace < need) return (null, SwapReasons.DiskLow);
            return (material, SwapReasons.Ok);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            _logger.LogWarning(ex, "[LocalRollback] 되돌리기 재료를 판정하지 못했습니다 — 되돌릴 이전 버전 없음으로 답합니다.");
            return (null, SwapReasons.NoPreviousVersion);
        }
    }

    private static LocalSwapLastResult? ToLast(SwapRequest? r) =>
        r is null ? null : new LocalSwapLastResult(r.Mode, r.State, r.Reason, r.From, r.To, r.LastTouchedUtc);

    private void PurgeExpired()
    {
        var now = _env.UtcNow;
        foreach (var kv in _tickets)
            if (now - kv.Value.IssuedUtc > TicketLifetime) _tickets.TryRemove(kv.Key, out _);
    }
}
