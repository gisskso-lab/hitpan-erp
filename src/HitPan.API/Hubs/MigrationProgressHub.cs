using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HitPan.API.Hubs;

/// <summary>
/// 마이그레이션 진행률 SignalR Hub (정공법 CODE-01, 2026-05-14).
///
/// 봉합 → 정공법 전환:
///   - 기존 봉합: MigrationJobStore static dict + Razor 2초 폴링 (30분 마이그 = 900회 호출).
///   - 정공법: 서버가 진행 콜백 발생 즉시 jobId 그룹에 push. 폴링 0회.
///
/// 동작:
///   1) Web Blazor가 hubConnection.StartAsync() → SubscribeJob(jobId) 호출.
///   2) Hub가 해당 connection을 Groups.AddToGroupAsync(jobId)로 묶음.
///   3) MdbMigrationService._progressCallback → IMigrationProgressService.UpdateAsync →
///      Hub.Clients.Group(jobId).SendAsync("ProgressUpdate", payload).
///
/// 보안:
///   - [Authorize] — JWT 인증 필수 (TenantAdminOnly 권한 정책은 Controller 단에서 검증).
///   - jobId 자체는 GUID라 추측 불가. 다른 테넌트가 우연히 같은 jobId 못 들음.
///
/// 헌법 준수: #15 빈 catch 금지(미사용), #19 warnings 0, #25 안전하게(Authorize).
/// </summary>
[Authorize]
public sealed class MigrationProgressHub : Hub
{
    private readonly ILogger<MigrationProgressHub> _logger;

    public MigrationProgressHub(ILogger<MigrationProgressHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 클라이언트가 특정 jobId 진행률을 구독하도록 그룹에 추가.
    /// Blazor MdbMigration.razor에서 마이그 시작 직후 호출.
    /// </summary>
    public async Task SubscribeJob(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            _logger.LogWarning("[MigrationProgressHub] SubscribeJob with empty jobId from {ConnectionId}", Context.ConnectionId);
            return;
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, jobId);
        _logger.LogInformation("[MigrationProgressHub] {ConnectionId} subscribed jobId={JobId}", Context.ConnectionId, jobId);
    }

    /// <summary>
    /// 잡 종료 시 구독 해제 (선택, connection 끊기면 자동 해제됨).
    /// </summary>
    public async Task UnsubscribeJob(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId)) return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, jobId);
    }
}
