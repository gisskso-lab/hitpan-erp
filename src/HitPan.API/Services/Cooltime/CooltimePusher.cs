using HitPan.API.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace HitPan.API.Services.Cooltime;

/// <summary>
/// 쿨타임 푸시 — 로그인 한 건 그룹(<see cref="NotificationHub.SessionGroupFor"/>)으로만 보낸다(20260928작4 §5-3).
/// </summary>
/// <remarks>
/// 인터페이스로 둔 이유: 게이트(갈래 ③)가 「그룹 송신 1회」(G-C2)를 SignalR 없이 센다.
/// 새 인터페이스이므로 기존 구현체 grep 대상은 없다(#12) — 구현체 = <see cref="SignalRCooltimePusher"/> 하나.
/// 🔴 실패는 예외로 올리지 않는다 — 끝내기·물음을 되돌리지 않는다(설계 §4 「트랜잭션 밖」). 경고 로그 1줄(#15).
/// </remarks>
public interface ICooltimePusher
{
    /// <summary><c>CooltimePrompt { secondsLeft }</c> 를 그 로그인에 보낸다.</summary>
    Task PromptAsync(string tenantId, string sessionId, int secondsLeft, CancellationToken ct = default);

    /// <summary><c>CooltimeEnded { cause }</c> 를 그 로그인에 보낸다.</summary>
    Task EndedAsync(string tenantId, string sessionId, CooltimeEndCause cause, CancellationToken ct = default);

    /// <summary>
    /// 🆕 11/27 고침 차례(§273 V1) — <c>CooltimeContinued</c>(본문 없음)를 그 로그인에 보낸다. 같은 출입증을 쓰는 다른 탭이 팝업을 닫는다.
    /// #12 구현체 grep(11/27): <see cref="SignalRCooltimePusher"/> · 시험 <c>CooltimeDbGateTests.CountingPusher</c> — 둘 다 같이 고침.
    /// </summary>
    Task ContinuedAsync(string tenantId, string sessionId, CancellationToken ct = default);
}

/// <summary><see cref="ICooltimePusher"/> 의 SignalR 구현 — 기존 <c>NotificationHub</c> 연결 하나를 재사용한다.</summary>
public sealed class SignalRCooltimePusher : ICooltimePusher
{
    private readonly IHubContext<NotificationHub> _hub;
    private readonly ILogger<SignalRCooltimePusher> _logger;

    public SignalRCooltimePusher(IHubContext<NotificationHub> hub, ILogger<SignalRCooltimePusher> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PromptAsync(string tenantId, string sessionId, int secondsLeft, CancellationToken ct = default)
    {
        try
        {
            await _hub.Clients
                .Group(NotificationHub.SessionGroupFor(tenantId, sessionId))
                .SendAsync(CooltimeWire.PromptEvent, new { secondsLeft }, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // #15 — 푸시를 못 받은 화면은 서버가 §3 ① 로 끝낸다(안전 쪽 실패).
            _logger.LogWarning(ex, "[쿨타임] 물음 푸시 실패 — tenant={TenantId} sid={Sid}", tenantId, sessionId);
        }
    }

    /// <inheritdoc />
    public async Task EndedAsync(string tenantId, string sessionId, CooltimeEndCause cause, CancellationToken ct = default)
    {
        try
        {
            await _hub.Clients
                .Group(NotificationHub.SessionGroupFor(tenantId, sessionId))
                .SendAsync(CooltimeWire.EndedEvent, new { cause = CooltimeWire.CauseWire(cause) }, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // #15 — 못 받은 화면은 SessionValidityMiddleware 401 → 갱신 → refresh 폐기 401 → 로그인 화면(설계 §4).
            _logger.LogWarning(ex, "[쿨타임] 끝남 푸시 실패 — tenant={TenantId} sid={Sid}", tenantId, sessionId);
        }
    }

    /// <inheritdoc />
    public async Task ContinuedAsync(string tenantId, string sessionId, CancellationToken ct = default)
    {
        try
        {
            await _hub.Clients
                .Group(NotificationHub.SessionGroupFor(tenantId, sessionId))
                .SendAsync(CooltimeWire.ContinuedEvent, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // #15 — 못 받은 탭은 0초에 서버가 「안 끝냄」 이라 답하면 상태를 다시 물어 팝업만 닫는다(§273 V1 뒷받침).
            _logger.LogWarning(ex, "[쿨타임] 계속됨 푸시 실패 — tenant={TenantId} sid={Sid}", tenantId, sessionId);
        }
    }
}
