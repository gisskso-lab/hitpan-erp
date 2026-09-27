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
///
/// <para>
/// ⬛ <b>2026-09-27 봉합 4차 — 이 미들웨어는 더 이상 축 B 스위치를 읽지 않는다.</b>
/// 여기서 끄고 켜는 것은 <c>enforce_session_validity</c>(DB-129 · 기본 <b>켬</b>) 하나다.
/// 축 B(PC 동시로그인 차단 409)의 판정은 로그인 경로
/// <c>AuthService.EnforceSinglePcLoginAsync</c> 에만 있다.
/// 사유 → <c>docs/운영기록/20260927_PM전결_VB3_통제분리_결재.md</c> §2.
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

        // ── 생존 확인 스위치를 여기서 읽는다 (20260927작2 봉합 4차 절K · PM 전결 §2) ─────
        //
        //   ⬛ 2026-09-27 4차에서 **읽는 설정이 바뀌었다.**
        //     종전(3차까지): 축 B 스위치 `enforce_single_pc_login` 을 읽었다.
        //     지금(4차):     생존 확인 전용 스위치 `enforce_session_validity` 를 읽는다.
        //     사유 → docs/운영기록/20260927_PM전결_VB3_통제분리_결재.md §2 (안 다 — 통제 분리)
        //     🔴 낡은 서술을 지우지 않고 남긴 이유: 다음 사람이 `git blame` 없이도
        //       *"왜 여기서 축 B 를 안 읽나"* 를 이 자리에서 알 수 있어야 한다.
        //
        //   🔴 <b>스위치 하나가 성질이 다른 두 통제를 함께 끄고 있었다</b>(병렬이슈 V-B3).
        //     ㉮ 세션 생존 확인   = 로그아웃·밀어내기 즉시 반영 → **정합성**. 모든 고객이 원한다
        //     ㉯ PC 동시로그인 차단 = 2번째 PC 를 409 로 막는다  → **정책**. 과금을 켠 고객만
        //     우리는 ㉯ 를 기본 OFF 로 출하한다(DB-128) ⇒ 그 한 스위치를 읽던 3차까지는
        //     **출하 상태에서 로그아웃이 최대 8시간(액세스 토큰 수명) 먹지 않았다.**
        //   🔴 그래서 이 미들웨어는 이제 **축 B 스위치를 읽지 않는다.**
        //     ㉯ 의 판정 자리는 로그인 경로(`AuthService.EnforceSinglePcLoginAsync`) 하나뿐이다.
        //     여기서 또 읽으면 한 통제를 두 곳에서 끄는 3차의 혼선이 그대로 돌아온다.
        //   🔴 판정 자리는 `sid` 판독 **뒤**, 세션 생존 조회 **앞**이다 (3차와 같다).
        //     앞에 두면 `sid` 없는 옛 토큰에도 DB 조회가 한 번 더 붙고(금지 #1b 경로),
        //     뒤에 두면 이미 401 을 내보낸 다음이라 끌 수 없다.
        //   🔴 꺼져 있으면(=비상 스위치 0) 생존 확인을 **건너뛰고 통과**한다. (게이트 G-B14)
        //     켜져 있으면(기본 1) 죽은 세션이 401 이 된다 — **축 B 가 꺼져 있어도 그렇다.** (게이트 G-B10)
        if (!await IsSessionValidityEnforcedAsync(context, db)) { await _next(context); return; }

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

    /// <summary>
    /// 세션 생존 확인(㉮) 스위치 — 이 테넌트에서 켜져 있는가 (20260927 봉합 4차 절K · PM 전결 §2 · DB-129).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⬛ <b>3차까지 이 메서드는 <c>IsSinglePcLoginEnabledAsync</c> 였고 축 B 스위치
    /// (<c>enforce_single_pc_login</c> · 캐시 키 <c>single-pc-login:{tenantId}</c>)를 읽었다.</b>
    /// 4차에서 읽는 설정·캐시 키·이름을 전부 새 스위치로 바꿨다 —
    /// 사유 → <c>docs/운영기록/20260927_PM전결_VB3_통제분리_결재.md</c> §2.
    /// 🔴 캐시 키를 함께 바꾼 이유: 옛 키가 남으면 이름은 축 B 인데 값은 생존 확인인
    /// 항목이 메모리에 떠 있어 다음 사람이 무엇을 재는지 못 가린다.
    /// </para>
    /// <para>
    /// 🔴 <b>기본은 켬(1)이다</b> — 여기가 축 B 스위치와 방향이 반대다(DB-129 <c>DEFAULT 1</c>).
    /// 로그아웃·밀어내기는 <b>모든 고객이 원하는 정합성</b>이므로 켜서 출하한다.
    /// 끄는 것은 <b>비상용</b>이고, DB 한 줄로 끈다(설정 화면은 별건).
    /// </para>
    /// <para>
    /// 🔴 <b>그런데 값이 없거나 못 읽으면 <c>false</c>(끈 쪽)로 간다</b> — 기본값과 방향이 어긋나 보이지만
    /// 일부러다(전결 §5 · fail-open). 표가 부재·손상이거나 마이그가 아직 안 돈 옛 DB 에서
    /// <b>못 읽었다는 이유로 사람을 끊으면</b> 고객이 끌 수도 없는 전면 잠금이 된다([4] 반려 F-1).
    /// 정상 DB 라면 DB-129 가 행에 1 을 넣어 두므로 켜진 채로 읽힌다.
    /// ⚠️ 대가는 숨기지 않는다: DB 를 흔들 수 있는 쪽은 이 통제를 끌 수 있다([3-V] P2-4 계통).
    /// </para>
    /// <para>
    /// ⚠️ 축 A(<c>SetTenantSessionLimitFlagAsync</c>)와 <b>같은 방식·같은 캐시 수명</b>이다.
    /// 다르게 만들면 한쪽만 고쳐지는 사고가 난다.
    /// </para>
    /// <para>
    /// 🔴 <c>SessionLimitMiddleware</c> 처럼 판정을 저쪽 파일 안으로 넣지 않는다 —
    /// 스위치는 <b>바깥에서</b> 건다(작지 금지 #1 · 축이 다른 파일은 한 줄도 안 건드린다).
    /// </para>
    /// </remarks>
    private async Task<bool> IsSessionValidityEnforcedAsync(HttpContext context, IDbConnection db)
    {
        // #2 계통 — tenant_id 는 요청 파라미터에서 받지 않는다. JWT 를 푼 Items 값만 쓴다.
        var tenantId = context.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return false;   // 없으면 = 끔 (fail-open)

        try
        {
            var enabled = await _cache.GetOrCreateAsync($"session-validity:{tenantId}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = SessionCacheTtl;
                var v = await db.ExecuteScalarAsync<int?>(
                    "SELECT enforce_session_validity FROM tenant_settings WHERE tenant_id = @TenantId",
                    new { TenantId = tenantId });
                return v == 1;   // 행 없음·NULL = 끔 (fail-open · 전결 §5)
            });

            return enabled;
        }
        catch (Exception ex)
        {
            // 가용성 우선 — 못 읽었다고 사람을 끊지 않는다. 다만 조용히 넘기지 않는다(#15).
            // ⚠️ 컬럼이 없는 옛 DB(DB-129 미적용)가 여기로 온다 — 그 고객은 3차와 같은 상태로 돈다.
            _logger.LogWarning(ex,
                "세션 생존확인 스위치 조회 실패 — 끈 것으로 둔다. tenant={TenantId}", tenantId);
            return false;
        }
    }
}
