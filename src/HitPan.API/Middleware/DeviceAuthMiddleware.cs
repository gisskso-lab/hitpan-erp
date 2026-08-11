using System.Data;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Http;

namespace HitPan.API.Middleware;

/// 기기 인증 검사 (20260811작3 (A)).
///
/// 사장님 원문 (2026-08-11):
///   *"인증키가 없어 인증할 수 없는 슬롯은 로그인만 되고 화면은 비활성화 그리고
///     비활성화된 화면에 '인증된 기기가 아닙니다. 히트판 관리자에게 문의하세요.' 안내"*
///   *"인증요청을 메인PC에 하면, 그 요청을 승인하는건 대표야"*
///   *"메인PC에서 인증키가 생성되면, 요청한 클라이언트PC에서 입력하는 방식."*
///
/// 🔴 로그인은 막지 않는다. 업무 기능만 막는다.
///   로그인부터 막으면 직원은 뭐가 잘못됐는지 모르고 전화한다.
///   화면이 "인증된 기기가 아닙니다" 라고 말해주고, 번호를 넣을 자리를 준다.
///
/// 🔴 서버는 추측하지 않는다.
///   넘어온 번호를 해시로 만들어 저장된 것과 **같은지만** 본다.
///   대표가 승인하며 만든 번호이므로, 그것을 아는 사람은 대표에게 받은 사람뿐이다.
///
/// ⚠️ 이것은 **기기 대수를 세는 일과 다르다.** 대수는 기기 목록의 줄 수로 이미 센다
///   (장치ID·장치이름). 여기서 하는 일은 문을 여느냐 마느냐 하나뿐이다.
public sealed class DeviceAuthMiddleware
{
    /// 직원 화면이 인증 번호를 실어 보내는 헤더.
    private const string DeviceKeyHeader = "X-HitPan-Device-Key";

    /// 🔴 이 길들은 막지 않는다.
    ///   번호를 넣으러 가는 길(verify-key)까지 막으면 **영원히 빠져나올 수 없다.**
    private static readonly string[] BypassPrefixes = new[]
    {
        "/api/auth",              // 로그인·갱신·내 정보 — 로그인 자체는 막지 않는다
        "/api/devices",           // 기기 인증·목록·승인 — 번호를 넣는 길이 여기다
        "/api/appversion",        // 업데이트 확인 — 막으면 고칠 방법이 사라진다
        "/api/watchdog",
        "/health",
        "/swagger"
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<DeviceAuthMiddleware> _logger;
    private readonly bool _enabled;

    public DeviceAuthMiddleware(RequestDelegate next, ILogger<DeviceAuthMiddleware> logger, IConfiguration config)
    {
        _next = next;
        _logger = logger;

        // 승인제와 같은 스위치를 쓴다 — 승인을 안 받는데 인증 번호를 요구하면 앞뒤가 안 맞는다.
        //   개발 중에는 꺼둔다(우리가 우리 기능에 막히지 않도록 · 사장님 지적).
        _enabled = config?.GetValue<bool>("DeviceApproval:Enabled") ?? false;
    }

    public async Task InvokeAsync(HttpContext context, IDbConnection db)
    {
        if (!_enabled)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;

        // 화면·그림 같은 것은 막지 않는다. 막으면 안내 화면 자체가 안 뜬다.
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        foreach (var prefix in BypassPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }
        }

        // 로그인 안 한 요청은 앞 단계가 이미 처리한다.
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var tenantId = context.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            await _next(context);
            return;
        }

        var authKey = context.Request.Headers[DeviceKeyHeader].ToString();

        try
        {
            var ok = false;

            if (!string.IsNullOrWhiteSpace(authKey))
            {
                // 대조 하나. 같은 회사 안에서만 찾는다(헌법 #2) — 남의 회사 번호로 못 들어온다.
                // 승인된 기기만 인정한다 — 해제된 기기의 옛 번호가 살아나면 안 된다.
                ok = (await db.ExecuteScalarAsync<int>(new CommandDefinition(
                    @"SELECT COUNT(*) FROM tenant_devices
                       WHERE tenant_id = @Tid AND auth_key_hash = @Hash AND status = 'approved'",
                    new { Tid = tenantId, Hash = Sha256Hex(authKey) },
                    cancellationToken: context.RequestAborted))) > 0;
            }

            if (!ok)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync(
                    "{\"error\":\"forbidden_device_auth\"," +
                    "\"message\":\"인증된 기기가 아닙니다. 히트판 관리자에게 문의하세요.\"}",
                    context.RequestAborted);
                return;
            }
        }
        catch (Exception ex)
        {
            // 🔴 검사 자체가 고장 나면 **통과시킨다.**
            //   우리 검사가 고장 나서 고객 전원이 일을 못 하는 것이 훨씬 큰 사고다.
            //   (약관 검사도 같은 방식 — TermsConsentMiddleware)
            _logger.LogWarning(ex, "기기 인증 검사를 건너뜁니다 (검사 자체 실패): tenant={Tenant}", tenantId);
        }

        await _next(context);
    }

    private static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
