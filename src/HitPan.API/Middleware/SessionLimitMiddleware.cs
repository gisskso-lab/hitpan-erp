using System.Data;
using Dapper;
using Microsoft.Extensions.Caching.Memory;
// 🔴 B-4 — 기기 한도의 단일 정의. 세션 제한 숫자를 여기 또 적지 않는다.
using HitPan.Application.Services;

namespace HitPan.API.Middleware;

/// <summary>동시 세션 제한 미들웨어 — 티어별 최대 접속 수 제한</summary>
public sealed class SessionLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SessionLimitMiddleware> _logger;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan TierCacheTtl = TimeSpan.FromMinutes(5);
    // 티어별 동시 세션 제한 (기본값)
    //
    // 🔴 2026-08-16 B-4 봉합 — **여기 숫자가 기기 한도와 갈라져 있었다.**
    //
    //   [무엇이 났나] `enterprise` 가 이 표에 **없었다.** 그래서 아래
    //     `GetValueOrDefault(tier, 50)` 이 **50** 을 주는데, 같은 요금제의 기기 한도는
    //     PC 100 + 휴대기기 80 = **180 대**다(SlotPolicyDefaults).
    //     ⇒ 고객이 산 기기를 다 쓰기 훨씬 전에 **429 가 먼저 떠서** 막힌다.
    //     기기는 등록되는데 접속이 안 되는, 원인을 짐작하기 어려운 종류다.
    //
    //   [고침] 숫자를 여기 또 적지 않는다. 기기 한도의 **단일 정의**
    //     (`SlotPolicyDefaults`)에서 **PC + 휴대기기 합**으로 뽑는다.
    //     ⇒ 요금제 한도를 고치면 세션 제한이 **자동으로 따라온다.** 갈라질 수가 없다.
    //
    //   ⚠️ `premium` 은 `enterprise` 의 옛 이름이라 같은 값이 나온다(정의가 그렇게 돼 있다).
    //   ⚠️ 이 값은 **동시 세션** 상한이지 기기 대수가 아니다. 한 기기에서 여러 창을
    //     띄우는 경우가 있어 기기 한도와 같게 두면 빡빡하다 —
    //     그래서 종전처럼 **여유를 준다**(아래 SessionHeadroom).
    private const double SessionHeadroom = 1.5;

    // 🔴 종전 값 — **바닥으로만 쓴다. 절대 이보다 빡빡해지지 않는다.**
    //
    //   ⚠️ 실측으로 잡았다. 기기 한도에서 뽑기만 하면 `trial` 이 50 → 23,
    //     모르는 요금제가 50 → 12 로 **줄어든다.** 지금 잘 쓰던 고객이
    //     이 봉합 때문에 429 를 새로 맞는 것은 **B-4 가 고치려던 것과 같은 사고**다.
    //   ⇒ 이 봉합은 **넓히기만 한다**(아래 Math.Max).
    private static readonly Dictionary<string, int> LegacyFloor = new()
    {
        ["basic"] = 8,
        ["pro"] = 20,
        ["premium"] = 100,
        ["trial"] = 50,
        ["default"] = 50
    };

    private static int TierSessionLimit(string tier)
    {
        var pc = SlotPolicyDefaults.Value($"tier.{tier}.pc_limit", 0);
        var mobile = SlotPolicyDefaults.Value($"tier.{tier}.mobile_limit", 0);

        // 기기 한도를 아는 요금제면 그 합에 여유를 준다.
        var fromDevices = (pc == 0 && mobile == 0)
            ? 0
            : (int)Math.Ceiling((pc + mobile) * SessionHeadroom);

        var floor = LegacyFloor.GetValueOrDefault(tier, 0);

        // 🔴 둘 중 **큰 쪽**. 넓히기만 하고 좁히지 않는다.
        var limit = Math.Max(fromDevices, floor);
        return limit;   // 0 이면 호출부가 default 로 다시 묻는다
    }

    /// <summary>
    /// 게이트 전용 — 실제 판정 메서드를 그대로 부른다(B-4).
    /// </summary>
    /// <remarks>
    /// ⚠️ 시험이 <b>값</b>을 대조하기 위해 연다. 글자 검사로는 이 결함을 못 잡는다
    /// (상수를 다른 파일로 옮기면 통과하던 G-8 의 실패를 되풀이하지 않는다).
    /// </remarks>
    public static int TierSessionLimitForTests(string tier)
    {
        var v = TierSessionLimit(tier);
        return v > 0 ? v : TierSessionLimit("default");
    }

    public SessionLimitMiddleware(
        RequestDelegate next,
        ILogger<SessionLimitMiddleware> logger,
        IMemoryCache cache)
    {
        _next = next;
        _logger = logger;
        _cache = cache;
    }

    public async Task InvokeAsync(HttpContext context, IDbConnection db)
    {
        // API 경로만 체크
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/api")) { await _next(context); return; }

        // 인증 안 된 요청은 통과 (로그인/리프레시 등)
        if (context.User?.Identity?.IsAuthenticated != true) { await _next(context); return; }

        var tenantId = context.Items["TenantId"]?.ToString();
        var userId = context.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId)) { await _next(context); return; }

        try
        {
            // 현재 활성 세션 수 확인 (최근 30분 내 활동)
            if (db.State != ConnectionState.Open)
            {
                if (db is System.Data.Common.DbConnection c) await c.OpenAsync();
                else db.Open();
            }

            var activeCount = await db.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(DISTINCT user_id) FROM user_sessions WHERE tenant_id = @TenantId AND expires_at > NOW()",
                new { TenantId = tenantId });

            // 테넌트 티어 확인 — 5분 캐싱 (과금 주기 내 거의 불변)
            var tier = await _cache.GetOrCreateAsync($"tenant-tier:{tenantId}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TierCacheTtl;
                return await db.QueryFirstOrDefaultAsync<string>(
                    "SELECT COALESCE(subscription_tier, '') FROM local_subscription WHERE tenant_id = @TenantId",
                    new { TenantId = tenantId }) ?? "";
            }) ?? "";

            // 🔴 B-4 — 기기 한도의 단일 정의에서 뽑는다(위 TierSessionLimit 주석).
            //   모르는 요금제는 `tier.default.*` 로 떨어진다 — 기기 한도와 같은 사전을 쓴다.
            var resolved = TierSessionLimit(tier.ToLower());
            var limit = resolved > 0 ? resolved : TierSessionLimit("default");

            if (activeCount > limit)
            {
                _logger.LogWarning(
                    "Session limit exceeded. Tenant={TenantId} User={UserId} Active={Active} Limit={Limit} Tier={Tier}",
                    tenantId, userId, activeCount, limit, tier);
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(
                    $"{{\"error\":\"동시 접속 제한 초과 ({activeCount}/{limit}). 다른 기기에서 로그아웃해주세요.\"}}");
                return;
            }
        }
        catch (Exception ex)
        {
            // 세션 체크 실패해도 요청은 통과 (가용성 우선) — 단, 반드시 로그 남김
            _logger.LogError(ex,
                "Session limit check failed. Tenant={TenantId} User={UserId}. Request passed through (availability priority).",
                tenantId, userId);
        }

        await _next(context);
    }
}
