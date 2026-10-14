using System.Data;
using System.Security.Cryptography;
using System.Text;
using Dapper;

namespace HitPan.API.Middleware;

/// <summary>
/// 워치독 endpoint(/watchdog/*) 전용 Bearer 토큰 검증.
/// 토큰 = sha256(license_key + "|" + machine_guid + "|" + tenant_id_hash)
/// licenses 테이블에서 (license_key_hash, machine_guid_hash) 매칭 확인.
/// 헌법 #22 정합: tenant_id 원본 비교 금지, hash만 사용.
///
/// <para>🔴 <b>20261014작10 측정 메모 (사장님 지시 「코드주석·md 에 메모 해둘것」 · 결-F)</b></para>
///
/// <para><b>측정</b>(2026-10-14):
/// <list type="bullet">
///   <item><c>licenses</c> 표는 <b><c>DB-71</c> 만 만든다</b>. 🔴 출하 DDL 에는 <b>그 이름이 0건</b>인데
///     <c>schema_migrations</c> 는 <c>DB-71</c> 을 「적용됨」으로 시드한다 ⇒ 적용기가 건너뛴다
///     ⇒ 🔴 <b>신규 설치 고객 PC 에 그 표가 없다.</b></item>
///   <item>표가 없으면 아래 <c>catch</c> 가 <b>503</b> 을 돌려준다(그 자리는 설계대로다).</item>
/// </list></para>
///
/// <para>🟢 <b>그래도 업데이트 흐름은 안 깨진다</b> — 실물 워치독은 <c>/watchdog/ping</c>·
/// <c>/watchdog/emergency</c> 를 <b><c>https://back.hitpan.kr</c>(백오피스)</b> 로 부른다
/// (<c>src/HitPan.Watchdog/WatchdogOptions.cs</c> <c>MetaPingEndpoint</c>). 즉 이 미들웨어가 지키는
/// 문은 <b>고객 PC 로컬 ERP API</b> 의 <c>/watchdog/*</c> 이고, <b>지금 그 문을 두드리는 쪽이 없다.</b>
/// 🔴 그래서 테넌트 2곳이 1.3.59 를 정상으로 받은 것과 모순이 없다 — 이 공백은 <b>잠복</b>이다.</para>
///
/// <para>🔴 <b>지우지도, 표를 만들지도 마라 — 둘 다 결재 거리다.</b>
/// #37(「안 읽힌다 ≠ 잔재」)로 이 미들웨어를 잔재라 판정할 수 없고, 출하 DDL 수정은 #36 이다.
/// <b>먼저 물을 것</b>: 로컬 <c>/watchdog/*</c> 수신부를 <b>쓸 계획이 있나</b>(있으면 표가 필요하고,
/// 없으면 이 문이 왜 서 있나). <c>WatchdogOptions.cs</c> 주석이 「수신부 부재로 보류됐다」고 적고 있다.</para>
///
/// <para>근거: docs/검증/선행/20261014_1V_작10_마이그산물_출하본어긋남_전수측정_선행검증서.md</para>
/// </summary>
public sealed class WatchdogBearerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<WatchdogBearerMiddleware> _logger;

    public WatchdogBearerMiddleware(RequestDelegate next, ILogger<WatchdogBearerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx, IDbConnection db)
    {
        if (!ctx.Request.Path.StartsWithSegments("/watchdog", StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx);
            return;
        }

        var auth = ctx.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("missing bearer");
            return;
        }

        var token = auth.Substring("Bearer ".Length).Trim();
        if (token.StartsWith("sha256:", StringComparison.Ordinal))
            token = token.Substring(7);

        if (token.Length != 64)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("invalid token format");
            return;
        }

        try
        {
            if (db.State != ConnectionState.Open)
            {
                if (db is System.Data.Common.DbConnection c) await c.OpenAsync(ctx.RequestAborted);
                else db.Open();
            }

            const string sql = @"
                SELECT COUNT(*) FROM licenses
                WHERE token_hash = @TokenHash
                  AND status = 'active'
                  AND (expires_at IS NULL OR expires_at > UTC_TIMESTAMP())
                LIMIT 1;";

            var ok = await db.ExecuteScalarAsync<int>(sql, new { TokenHash = token });
            if (ok == 0)
            {
                _logger.LogWarning("[WatchdogBearer] invalid token from {IP}", ctx.Connection.RemoteIpAddress);
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsync("unauthorized");
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WatchdogBearer] DB lookup failure");
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await ctx.Response.WriteAsync("service unavailable");
            return;
        }

        await _next(ctx);
    }

    /// <summary>고객 PC 워치독이 발신할 토큰 생성 (테스트용 헬퍼).</summary>
    public static string GenerateToken(string licenseKey, string machineGuid, string tenantIdHash)
    {
        var input = $"{licenseKey}|{machineGuid}|{tenantIdHash}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
