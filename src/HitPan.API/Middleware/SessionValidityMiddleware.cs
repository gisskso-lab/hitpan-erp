using System.Data;
using System.Security.Claims;
using Dapper;
using Microsoft.Extensions.Caching.Memory;

namespace HitPan.API.Middleware;

/// <summary>
/// 세션이 아직 살아 있는가 — 밀어내기를 요청 경로에서 즉시 반영한다 (DB-127 축 B · 20260927작1 절G).
/// </summary>
/// <remarks>
/// 🔴 <b>왜 필요한가 — JWT 는 상태가 없다.</b>
/// <para>
/// 실측(2026-09-27): 로그인은 그 계정의 <c>refresh_tokens</c> 를 전부 지운다(<c>AuthService</c>).
/// 그런데 access 토큰은 <b>8시간</b>짜리 JWT 라 서버가 DB 를 보지 않는다.
/// ⇒ 사용자가 [그 PC 접속을 끊기] 를 눌러 세션 행을 지워도, <b>그 PC 는 최대 8시간 그대로 쓴다.</b>
/// 고객은 끊었다고 믿는데 안 끊긴 상태 — 세션만 지우는 방식(갈래 가)을 버린 이유다.
/// </para>
///
/// <para>
/// 🔴 <b>왜 SessionLimitMiddleware 를 고치지 않고 새로 만드나</b> —
/// 그쪽은 <c>COUNT(DISTINCT user_id)</c> 로 <b>테넌트 총량</b>을 센다. 축이 다르다.
/// 한 파일에 두 축을 섞으면 이번 트랙이 고치려는 혼선(세는 곳·지우는 곳은 있는데 넣는 곳이 없던 것)을
/// 그대로 재생산한다. 기존 파일은 <b>한 줄도 건드리지 않는다</b>(헌법 #1 — 추가만).
/// </para>
///
/// <para>
/// ⚠️ <b>「즉시」의 정직한 정의 — 최대 10초다.</b> 매 요청 DB 조회를 피하려고 10초 캐시를 둔다.
/// 사람 체감으로는 즉시지만 0초가 아니다. <i>"즉시"</i> 라고만 적으면 다음 사람이 0초로 믿는다.
/// </para>
/// </remarks>
public sealed class SessionValidityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SessionValidityMiddleware> _logger;
    private readonly IMemoryCache _cache;

    /// <summary>세션 생존 여부 캐시 수명 — 밀어내기 반영 지연의 상한이기도 하다.</summary>
    private static readonly TimeSpan SessionCacheTtl = TimeSpan.FromSeconds(10);

    public SessionValidityMiddleware(
        RequestDelegate next,
        ILogger<SessionValidityMiddleware> logger,
        IMemoryCache cache)
    {
        _next = next;
        _logger = logger;
        _cache = cache;
    }

    public async Task InvokeAsync(HttpContext context, IDbConnection db)
    {
        if (!context.Request.Path.StartsWithSegments("/api")) { await _next(context); return; }
        if (context.User?.Identity?.IsAuthenticated != true) { await _next(context); return; }

        // 🔴 옛 토큰 호환 — 이 두 줄이 없으면 **배포 순간 전 고객이 튕긴다.**
        //   배포 전에 발급된 access 토큰에는 `sid` 가 없다(최대 8시간, refresh 는 7일).
        //   없는 것을 "세션 없음" 으로 읽으면 멀쩡히 일하던 사람이 전부 끊긴다.
        //   ⇒ 통과시킨다. 토큰이 갈리면 저절로 새 판정으로 들어온다. (게이트 G-7)
        // ── 축 A 킬스위치를 여기서 읽어 뒤 미들웨어에 전달한다 (20260927작1 절E) ──────────
        //
        //   🔴 왜 여기서 읽나 — `SessionLimitMiddleware` 를 **한 줄도 건드리지 않기 위해서**다.
        //     그 파일은 축이 달라 손대지 않기로 결재됐다(작지 금지 #1).
        //     대신 이 값을 Items 에 실어 주고, Program.cs 가 `UseWhen` 으로 그 미들웨어를
        //     아예 태울지 말지 고른다.
        //   🔴 값이 없으면 **끈 것으로 본다.** 축 A 는 한 번도 돈 적이 없어(넣는 코드가 없었다)
        //     켜지는 순간 한도 초과 고객이 429 를 맞는다. 기본 OFF 로 출발한다.
        await SetTenantSessionLimitFlagAsync(context, db);

        var sid = context.User.FindFirstValue("sid");
        if (string.IsNullOrWhiteSpace(sid)) { await _next(context); return; }

        bool alive;
        try
        {
            alive = await _cache.GetOrCreateAsync($"session-alive:{sid}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = SessionCacheTtl;

                if (db.State != ConnectionState.Open)
                {
                    if (db is System.Data.Common.DbConnection c) await c.OpenAsync();
                    else db.Open();
                }

                var found = await db.ExecuteScalarAsync<int?>(
                    "SELECT 1 FROM user_sessions WHERE session_id = @Sid AND expires_at > UTC_TIMESTAMP(6)",
                    new { Sid = sid });

                return found is not null;
            });
        }
        catch (Exception ex)
        {
            // 가용성 우선 — 판정에 실패했다고 일하던 사람을 끊지 않는다.
            // ⚠️ 다만 조용히 넘기지 않는다(#15). 기존 SessionLimitMiddleware 가 예외를 삼켜
            //   **아무도 안 도는 걸 몰랐던** 그 구조를 물려받지 않는다.
            _logger.LogError(ex, "세션 생존 확인 실패 — 요청은 통과시킴. sid={Sid}", sid);
            await _next(context);
            return;
        }

        if (!alive)
        {
            // 🔴 사용자가 다른 PC 에서 [이 접속을 끊기] 를 눌렀거나, 세션이 만료됐다.
            //   401 로 답한다 — 화면의 자동 재발급이 돌고, 그것도 막히면 로그인 화면으로 간다.
            //   ⚠️ 개발용어를 쓰지 않는다(고객이 읽는 문구).
            _logger.LogInformation("끊긴 세션의 요청 — 차단. sid={Sid} path={Path}", sid, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(
                "{\"message\":\"다른 컴퓨터에서 이 계정으로 접속해 연결이 끊어졌습니다. 다시 로그인해주세요.\"}");
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// 축 A(테넌트 총량 동시세션 제한) 를 이 요청에서 태울지 여부를 <c>Items</c> 에 실어 준다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>기본은 끔이다.</b> 축 A 는 <c>user_sessions</c> 에 넣는 코드가 없어 **한 번도 돈 적이 없다**.
    /// 이번 작업으로 행이 생기는 순간 그 제한이 소리 없이 켜지고, 한도를 넘는 고객은 그날부터 429 다.
    /// 테넌트별 활성 사용자 수를 실측한 뒤에 켠다(작지 §0 B-2).
    /// <para>⚠️ 판정에 실패하면 <b>끈 쪽</b>으로 둔다 — 못 읽었다고 한 번도 안 돌던 제한을 켜지 않는다.</para>
    /// </remarks>
    private async Task SetTenantSessionLimitFlagAsync(HttpContext context, IDbConnection db)
    {
        var tenantId = context.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return;   // 없으면 Items 미설정 = 끔

        try
        {
            var enabled = await _cache.GetOrCreateAsync($"tenant-session-limit:{tenantId}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = SessionCacheTtl;
                var v = await db.ExecuteScalarAsync<int?>(
                    "SELECT enforce_tenant_session_limit FROM tenant_settings WHERE tenant_id = @TenantId",
                    new { TenantId = tenantId });
                return v == 1;
            });

            context.Items[TenantSessionLimitFlag] = enabled;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "축A 킬스위치 조회 실패 — 끈 것으로 둔다. tenant={TenantId}", tenantId);
        }
    }

    /// <summary>
    /// <c>Program.cs</c> 의 <c>UseWhen</c> 이 읽는 열쇠. 값이 <c>true</c> 일 때만 축 A 를 태운다.
    /// </summary>
    public const string TenantSessionLimitFlag = "TenantSessionLimitEnabled";
}
