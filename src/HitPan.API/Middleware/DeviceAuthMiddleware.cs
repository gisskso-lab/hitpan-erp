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

    // 🔴 장비넘버 헤더 (20260816작2) — 인증번호를 받은 적 없는 기기(메인PC)가 통과할 길.
    //   ⚠️ 클라이언트 HitPanApiAuthHandler.DeviceIdHeader 와 **글자가 같아야** 한다.
    private const string DeviceIdHeader = "X-HitPan-Device-Id";

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
        var deviceId = context.Request.Headers[DeviceIdHeader].ToString();

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

            // 🔴 2026-08-16 20260816작2 P0 봉합 — **메인PC 가 자기 화면에서 막혔다** (사장님 실측).
            //
            //   [무엇이 났나] 이 미들웨어는 **인증번호(auth_key_hash)만** 봤다.
            //     그런데 메인PC 는 그 번호를 **받은 적이 없다** — 인증번호는 대표가 *다른* 기기를
            //     승인할 때 만들어지는 값이고, 메인PC 는 MainPcRegistrationService 가
            //     서버 기동 시 **스스로** 등록하기 때문이다(is_main_pc=1, status='approved').
            //     ⇒ 승인제를 켜자 **자기 자신이 막혔고, 그 화면에서 풀어줄 사람이 자기 자신**이라
            //       스스로 못 빠져나온다.
            //
            //   [왜 결재 위반인가] 사장님 1차 결재 — *"메인PC = **설치 = 승인**"*.
            //     설치한 그 PC 는 이미 승인된 것이고 거기 앉은 사람이 대표다.
            //
            //   ⚠️ 같은 덫을 이미 한 번 겪었다 — RevokeAsync 가 메인PC 폐기를 막는 이유,
            //     RegisterOrRefreshAsync 가 revoked 메인PC 의 로그인을 통과시키는 이유가 전부
            //     *"막히면 되살릴 화면에 못 들어간다"* 였다. 이 미들웨어에만 그 규칙이 없었다.
            //
            //   [고침] **장비넘버로도 통과할 수 있게 한다.**
            //     ⚠️ 아무 기기나 통과시키는 것이 아니다 — `status='approved'` 인 기기만 인정한다.
            //       승인 대기(pending)·폐기(revoked)는 여전히 막힌다. 대조는 서버가 한다.
            if (!ok && !string.IsNullOrWhiteSpace(deviceId))
            {
                ok = (await db.ExecuteScalarAsync<int>(new CommandDefinition(
                    @"SELECT COUNT(*) FROM tenant_devices
                       WHERE tenant_id = @Tid AND device_id = @Did AND status = 'approved'",
                    new { Tid = tenantId, Did = deviceId },
                    cancellationToken: context.RequestAborted))) > 0;
            }

            // 🔴 2026-08-16 20260816작2 P0 — **대표계정은 막지 않는다.**
            //
            //   [무엇이 났나] 사장님이 1.2.82 를 받으시자 **메인PC 가 자기 화면에서 막혔다.**
            //     그 화면에 뜬 안내는 *"관리자에게 문의하세요"* 인데 **그 관리자가 사장님 자신**이다.
            //     승인을 해줄 수 있는 유일한 사람이 승인 화면에 못 들어간다 —
            //     **스스로 못 빠져나오는 방**이 된다.
            //
            //   [왜 이 축인가] 메인PC 인지를 접속 경로(loopback)로 판정하는 길은 **쓰지 않는다.**
            //     2026-08-10 에 그 판정이 *"고객사에서는 항상 참"* 임이 드러나 걷어낸 자리다
            //     (터널이 고객 PC 안에서 localhost 를 다시 부른다). 그 위에 무엇을 세우면
            //     전 기기에 적용돼 요금정책이 무너진다.
            //     ⇒ 대신 **누가 로그인했나**를 본다. 이 값은 JWT 클레임이라 흔들리지 않는다.
            //
            //   [왜 안전한가] 대표계정은 **어차피 모든 기기를 승인할 수 있는 사람**이다.
            //     막아도 기기 관리 화면(/api/devices)은 이미 통과 목록이라 스스로 풀 수 있다 —
            //     즉 이 예외가 새로 열어주는 권한은 없고, **막다른 길만 없앤다.**
            //     ⚠️ 직원 계정(tenant_user)은 그대로 막힌다. 요금·접근 통제는 유지된다.
            //
            //   ⚠️ 이 예외를 지우려면 먼저 답해야 한다 — *"대표가 막히면 누가 풀어주나?"*
            if (!ok)
            {
                var accountType = context.User?.FindFirst("account_type")?.Value;
                if (string.Equals(accountType, "tenant_admin", StringComparison.Ordinal))
                    ok = true;
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
