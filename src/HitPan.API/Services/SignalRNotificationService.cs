using HitPan.API.Hubs;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace HitPan.API.Services;

/// <summary>
/// <see cref="INotificationService"/> 의 SignalR 구현. 작(2026-08-13) 그룹웨어 단계2.
/// </summary>
/// <remarks>
/// 🟢 배관은 이미 검증돼 있다 — 마이그레이션 진행률이 같은 방식으로 돌고 있고,
/// WebSocket 인증 난제(<c>?access_token=</c>)도 <c>AuthExtensions</c> 에서 이미 봉합됐다.
/// ⇒ 신규 인프라가 아니라 <b>검증된 패턴 복제</b>다.
/// </remarks>
public sealed class SignalRNotificationService : INotificationService
{
    private readonly IHubContext<NotificationHub> _hub;
    private readonly ILogger<SignalRNotificationService> _logger;

    public SignalRNotificationService(
        IHubContext<NotificationHub> hub,
        ILogger<SignalRNotificationService> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task NotifyEmployeeAsync(string tenantId, string employeeId,
        AppNotification notification, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(employeeId))
        {
            return;
        }

        try
        {
            await _hub.Clients
                .Group(NotificationHub.GroupFor(tenantId, employeeId))
                .SendAsync("Notify", notification, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 🔴 알림 실패가 업무를 막으면 안 된다. 결재는 이미 저장됐는데 알림 때문에
            // 되돌리면 그게 더 큰 사고다. 다만 삼키지도 않는다 — 로그는 남긴다(헌법 #15).
            _logger.LogWarning(ex,
                "알림 전송 실패 (tenantId={TenantId}, employeeId={EmployeeId}, type={Type})",
                tenantId, employeeId, notification.Type);
        }
    }
}
