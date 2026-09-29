using System.Data;
using System.Text.Json;
using Dapper;

namespace HitPan.Application.Common;

/// <summary>
/// 접속기록 — 로그인·로그아웃을 <c>audit_trail</c> 에 한 줄씩 남긴다 (20260928작2 절E · 설계 §6).
/// </summary>
/// <remarks>
/// <para>
/// 사장님 원문: <i>"로그에, 어느 기기에서 로그인 했는지 남겨두는 정도만."</i> ·
/// <i>"로그인 할떄, 다른기기를 왜 지워? 접속기록만 기록하면 되지."</i>
/// </para>
/// <para>
/// 🔴 <b>SQL 은 이 한 곳에만 있다</b> — 로그인(<c>AuthService.LoginAsync</c>)과 로그아웃
/// (<c>AuthController.Logout</c>) 두 자리가 같은 함수를 부른다. 두 곳에 복사하면 한쪽만 고쳐지는 사고가 난다.
/// </para>
/// <para>
/// ⚠️ <c>IAuditService</c> 를 쓰지 않는 이유 — 그 서비스는 JWT 테넌트가 없으면 건너뛴다(<c>AuditService</c>).
/// 로그인은 익명 요청이라 거기서 늘 빠진다. ⇒ 직접 넣는다.
/// 테넌트·사용자는 <b>DB 의 사용자 행</b>(로그인) 또는 <b>JWT</b>(로그아웃)에서만 받는다(#2) — 요청 본문 0.
/// 인터페이스를 늘리지 않았다(#12 — 구현체 grep 대상 없음).
/// </para>
/// <para>
/// 🔴 <b>기록 실패는 로그인·로그아웃을 막지 않는다</b>(#20). 조용히도 넘기지 않는다(#15 — 경고 로그 1줄).
/// </para>
/// <para>
/// ⚠️ <c>device_kind</c>·<c>user_agent</c> 는 <b>표시용</b>이다 — 어떤 판정에도 쓰지 않는다.
/// 판정은 <c>user_sessions.device_kind</c>(서버 판정값)가 한다.
/// </para>
/// </remarks>
public static class SessionAccessTrail
{
    /// <summary><c>audit_trail.entity_type</c> 고정값 — 등록기기관리 「접속 기록」이 이 값으로 거른다(설계 §10).</summary>
    public const string EntityType = "user_session";

    public const string ActionLogin = "login";
    public const string ActionLogout = "logout";

    /// <summary><c>user_agent</c> 최대 길이 — <c>user_sessions.user_agent varchar(500)</c> 와 같게 둔다.</summary>
    private const int UserAgentMax = 500;

    /// <summary>
    /// 접속기록 한 줄을 남긴다. <b>예외를 던지지 않는다</b> — 실패는 경고 로그만 남기고 <c>false</c>.
    /// </summary>
    /// <param name="db">운영 연결(로그인·로그아웃이 이미 쓰는 그 연결).</param>
    /// <param name="tenantId">사용자 행(로그인) 또는 JWT(로그아웃)의 테넌트 — 요청 본문에서 받지 않는다(#2).</param>
    /// <param name="userId">같은 출처의 사용자 번호.</param>
    /// <param name="actionType"><see cref="ActionLogin"/> 또는 <see cref="ActionLogout"/>.</param>
    /// <param name="sessionId">이 로그인의 <c>sid</c>(없으면 <c>null</c>) — <c>audit_trail.entity_id</c>.</param>
    /// <param name="deviceKind"><c>DeviceTypeResolver.ToSessionDeviceKind</c> 결과(<c>pc</c>/<c>mobile</c>).</param>
    /// <param name="userAgent">서버가 헤더에서 읽은 값. 500자에서 자른다.</param>
    /// <returns>행을 실제로 남겼는가.</returns>
    public static async Task<bool> WriteAsync(
        IDbConnection db,
        string tenantId,
        string userId,
        string actionType,
        string? sessionId,
        string deviceKind,
        string? userAgent)
    {
        try
        {
            var ua = userAgent ?? string.Empty;
            if (ua.Length > UserAgentMax) ua = ua[..UserAgentMax];

            // 🔴 JSON 키는 고정이다 — 화면(설계 §10)이 JSON_VALUE(after_value,'$.device_kind') 로 읽는다.
            var afterValue = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["device_kind"] = deviceKind,
                ["user_agent"] = ua
            });

            await db.ExecuteAsync(
                @"INSERT INTO audit_trail
                      (log_id, tenant_id, user_id, action_type, entity_type, entity_id, before_value, after_value, reason)
                  VALUES (@LogId, @TenantId, @UserId, @ActionType, @EntityType, @EntityId, NULL, @AfterValue, NULL)",
                new
                {
                    LogId = Guid.NewGuid().ToString(),
                    TenantId = tenantId,
                    UserId = userId,
                    ActionType = actionType,
                    EntityType,
                    EntityId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId,
                    AfterValue = afterValue
                });
            return true;
        }
        catch (Exception ex)
        {
            // #15 — 빈 catch 금지. 기록 실패로 로그인·로그아웃을 막지 않는다(#20).
            System.Diagnostics.Trace.TraceWarning(
                $"[접속기록] audit_trail 기록 실패({actionType} · 로그인/로그아웃은 진행) user={userId}: {ex.Message}");
            return false;
        }
    }
}
