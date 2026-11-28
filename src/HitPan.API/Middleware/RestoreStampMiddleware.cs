using System.Data;
using System.Globalization;
using System.Security.Claims;
using Dapper;
using HitPan.Application.Interfaces;

namespace HitPan.API.Middleware;

/// <summary>
/// 🔴 W-G1-52 갈래 A-2① — <b>복원 시각 표지</b>: 표지보다 앞에 발급된 access token = 401(설계 §15-5 ① ⓔ · CTO N1-a · 고를 수 없음).
/// </summary>
/// <remarks>
/// <para><b>왜 필요한가 (4-0 ① 실측 · <c>SessionValidityMiddleware.cs</c>)</b> — 갱신 토큰 폐기 · 세션 지움만으로 access token(8시간)이
/// 바로 죽지 않는 길이 넷이다: :82 <c>sid</c> 없는 옛 토큰 통과 · :128 <c>enforce_session_validity = 0</c> 통과 · :50 10초 캐시 ·
/// :150 조회 실패 통과. 복원(ⓑ′)은 활성 계정 · 살아 있는 세션을 가진 사원 행을 처음으로 지우므로, 그 토큰이 살아 있으면
/// 없는 <c>employee_id</c> 로 결재 · 경비가 써진다(조용한 고아 · #20).</para>
/// <para><b>표지 자리</b> = 「지금 값」 표 <c>restore_history</c>(새 표 0) 의 이 회사 <c>MAX(COALESCE(finished_at, started_at))</c>
/// (출하 DDL <c>installer/hitpan_db_clean.sql</c> :3344-3356 칸 대조) + 프로세스 안 값(<see cref="IRestoreMaintenance.EndAsync"/> 가 즉시 올림).
/// 끝 시각이 없는 줄(복원 도중 프로세스가 죽음) = 시작 시각 — 그 앞 토큰 전부 401(보수적).
/// DB 값은 서버 지역 시각(옛 <c>BackupService</c> 와 같은 계약 · <see cref="IRestoreMaintenance"/> 머리 주석) → UTC 로 바꿔 댄다.</para>
/// <para><b>토큰 발급 시각</b> = <c>iat</c> → <c>nbf</c> → <c>exp − 8시간</c>(<c>AuthService.GenerateTokens</c> 는 <c>iat</c> 를 안 싣는다 ·
/// 수명 상수가 <see cref="AccessTokenLifetime"/> 와 같아야 한다 — 게이트가 반사로 맞댄다).</para>
/// <para>자리 = <c>SessionValidityMiddleware</c> 바로 뒤(회사 번호 = JWT 를 푼 <c>Items["TenantId"]</c> 만 · #2).
/// 점검 중에는 건너뛴다 — 대표의 복원 진행 요청이 옛 토큰으로 돌고, 다른 요청은 점검 미들웨어가 이미 503 으로 돌렸다.</para>
/// <para>조회 실패 = 통과 + 오류 기록(<c>SessionValidityMiddleware</c> 와 같은 가용성 우선 · 실패는 기억하지 않아 다음 요청이 다시 읽음).</para>
/// </remarks>
public sealed class RestoreStampMiddleware
{
    /// <summary><c>AuthService.AccessTokenLifetime</c>(8시간)과 같아야 한다 — 다르면 표지 판정이 어긋난다(게이트 반사 대조).</summary>
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(8);

    /// <summary>표지 앞 토큰 401 문구(고객이 읽음 · 개발용어 0).</summary>
    public const string StampMessage = "자료를 백업 시점으로 되살렸습니다. 다시 로그인해주세요.";

    /// <summary>표지 읽는 SQL — 출하 DDL <c>restore_history</c>(tenant_id · started_at NOT NULL · finished_at NULL) 대조.</summary>
    public const string StampSql =
        "SELECT MAX(COALESCE(finished_at, started_at)) FROM restore_history WHERE tenant_id = @TenantId";

    private readonly RequestDelegate _next;
    private readonly IRestoreMaintenance _maintenance;
    private readonly ILogger<RestoreStampMiddleware> _logger;

    public RestoreStampMiddleware(RequestDelegate next, IRestoreMaintenance maintenance, ILogger<RestoreStampMiddleware> logger)
    {
        _next = next;
        _maintenance = maintenance;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IDbConnection db)
    {
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/api") && !path.StartsWithSegments("/hubs")) { await _next(context); return; }
        if (context.User?.Identity?.IsAuthenticated != true) { await _next(context); return; }
        if (_maintenance.IsActive) { await _next(context); return; }

        // #2 — 회사 번호는 JWT 를 푼 Items 값만(요청 파라미터 0)
        var tenantId = context.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) { await _next(context); return; }

        var stamp = _maintenance.GetStampUtc(tenantId);
        if (stamp is null)
        {
            try
            {
                if (db.State != ConnectionState.Open)
                {
                    if (db is System.Data.Common.DbConnection c) await c.OpenAsync(context.RequestAborted);
                    else db.Open();
                }
                var local = await db.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
                    StampSql, new { TenantId = tenantId }, cancellationToken: context.RequestAborted));
                _maintenance.RememberStampUtc(tenantId, LocalToUtc(local));
                stamp = _maintenance.GetStampUtc(tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "복원 시각 표지 조회 실패 — 요청은 통과시킴. tenant={TenantId}", tenantId);
                await _next(context);
                return;
            }
        }

        if (stamp is null || stamp.Value == DateTime.MinValue) { await _next(context); return; }

        var issued = IssuedAtUtc(context.User);
        if (issued is null || issued.Value < stamp.Value)
        {
            _logger.LogInformation("복원 시각 표지 앞 토큰 — 차단. tenant={TenantId} issued={Issued:o} stamp={Stamp:o} path={Path}",
                tenantId, issued, stamp, path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync("{\"message\":\"" + StampMessage + "\",\"code\":\"restored_relogin\"}");
            return;
        }

        await _next(context);
    }

    /// <summary>서버 지역 시각(DB <c>datetime</c>) → UTC. <c>null</c> = 복원 기록 없음.</summary>
    public static DateTime? LocalToUtc(DateTime? local) =>
        local is null ? null : DateTime.SpecifyKind(local.Value, DateTimeKind.Local).ToUniversalTime();

    /// <summary>토큰 발급 시각(UTC) — <c>iat</c> → <c>nbf</c> → <c>exp − 8시간</c>. 셋 다 없으면 <c>null</c>(표지가 있으면 401).</summary>
    public static DateTime? IssuedAtUtc(ClaimsPrincipal? user)
    {
        if (user is null) return null;
        if (TryEpoch(user, "iat", out var iat)) return iat;
        if (TryEpoch(user, "nbf", out var nbf)) return nbf;
        if (TryEpoch(user, "exp", out var exp)) return exp - AccessTokenLifetime;
        return null;
    }

    private static bool TryEpoch(ClaimsPrincipal user, string type, out DateTime utc)
    {
        utc = default;
        var raw = user.FindFirst(type)?.Value;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)) return false;
        if (seconds < -62135596800L || seconds > 253402300799L) return false;   // DateTimeOffset 범위 밖 = 못 읽음
        utc = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        return true;
    }
}
