using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HitPan.API.Hubs;

/// <summary>
/// 앱 내 알림 SignalR Hub. 작(2026-08-13) 그룹웨어 단계2.
/// </summary>
/// <remarks>
/// <para>
/// <b>왜 만드나</b> — 김삼성 상무: <i>"메신저는 '결재가 올라왔습니다' 알림부터 붙이세요.
/// 그러면 직원이 메신저를 켤 이유가 생깁니다."</i> 사장님: <i>"히트판ERP내 알림되도록(인스타나
/// 페이스북처럼)"</i>. 지금은 사이드바 결재 뱃지가 <b>최초 1회 조회</b>뿐이라, 결재가 올라와도
/// 새로고침 전에는 아무도 모른다.
/// </para>
/// <para>
/// 🔴 <b>기존 <see cref="MigrationProgressHub"/> 와 그룹 결정 방식이 다르다.</b>
/// 그쪽은 클라이언트가 <c>SubscribeJob(jobId)</c> 로 스스로 그룹을 고른다. jobId 가 추측 불가능한
/// GUID 라 그때는 성립했지만, <b>알림에 그 방식을 쓰면 남의 사원 ID 를 넣어 남의 결재 알림을
/// 받아볼 수 있다.</b> 그래서 여기서는 <b>연결 시 서버가 JWT 를 보고 그룹을 정한다</b> —
/// 클라이언트에게 그룹을 고를 수단을 아예 주지 않는다(헌법 #2 — 식별자는 JWT 클레임에서만).
/// </para>
/// <para>
/// 🔴 <b>사원(employee_id) 기준이다.</b> 결재 체계 전체가 employee_id 이기 때문이다
/// (approval_documents.requester_id · approval_doc_lines.approver_id). user_id 로 묶으면
/// 2026-06-21 A-P0-1 과 같은 사고가 난다("결재 권한이 없습니다" 영구 차단).
/// </para>
/// <para>
/// ⚠️ 다만 <b>알림을 받으려면 로그인 계정이 필요하다</b>(사장님 결재 2026-08-12 —
/// <i>"메신저는 계정이 있는 사원에게만"</i>). 알림은 로그인한 화면에 뜨는 것이므로 구조상 당연하다.
/// ⇒ 계정 없는 사원이 결재선에 있으면 그 사람은 알림을 못 받는다. 결함이 아니라 구조이며,
/// 인사담당자·대표가 대신 처리하는 경로가 있어야 한다(설계서 §3-5).
/// </para>
/// </remarks>
[Authorize]
public sealed class NotificationHub : Hub
{
    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(ILogger<NotificationHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 사원 한 명에게 보낼 그룹 이름. <b>테넌트까지 포함</b>한다 —
    /// 사원 ID 는 GUID 라 충돌 가능성이 사실상 없지만, 격리는 우연이 아니라 구조로 보장해야 한다.
    /// </summary>
    public static string GroupFor(string tenantId, string employeeId)
        => $"n:{tenantId}:{employeeId}";

    /// <summary>
    /// 연결되는 순간 서버가 그룹에 넣는다. 클라이언트는 관여하지 않는다.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var tenantId = Context.User?.FindFirstValue("tenant_id");
        var employeeId = Context.User?.FindFirstValue("employee_id");

        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(employeeId))
        {
            // 사원 연결이 없는 계정(예: 사원 백필 전)은 알림 대상이 아니다.
            // 연결 자체는 끊지 않는다 — 화면이 알림 없이도 동작해야 하기 때문이다.
            _logger.LogInformation(
                "[NotificationHub] 알림 그룹 없이 연결됨 (사원 연결 없음). connectionId={ConnectionId}",
                Context.ConnectionId);
            await base.OnConnectedAsync().ConfigureAwait(false);
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(tenantId, employeeId))
            .ConfigureAwait(false);

        await base.OnConnectedAsync().ConfigureAwait(false);
    }
}
