using System.Data;
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
        // 🔴 2026-08-18 20260818작5 — **글자 하나(`-`)가 고객을 가뒀다.**
        //   (사장님 실측: "업데이트 인증으로 갇힌듯" · 콘솔 `GET /api/app-version 403`)
        //
        //   [무엇이 났나] 여기엔 `/api/appversion` 이라 적혀 있었는데
        //     실제 컨트롤러는 `[Route("api/app-version")]` — **하이픈이 있다.**
        //     ⇒ 통과 목록이 그 길을 **못 알아봤다.** 업데이트 화면이 자기 버전을 물으면 403.
        //
        //   🔴 [왜 이것이 최악인가] 바로 윗줄 주석이 *"막으면 고칠 방법이 사라진다"* 고
        //     적어 두고 **정작 그 자리를 안 열어 뒀다.** 기기 문제로 갇힌 고객은
        //     업데이트로 봉합을 받아야 풀리는데, **업데이트 화면 자체가 막혀** 영원히 갇힌다.
        //     ⇒ 오늘 사장님이 정확히 그 방에 갇히셨다.
        //
        //   🔴 [기존 게이트가 왜 못 잡았나] `DeviceApprovalGateTests` 가
        //     `InlineData("/api/appversion")` — **없는 경로**를 시험하고 있었다.
        //     목록에 있으니 초록불이었다. **막는 척만 하는 게이트**였다(가짜게이트 8번째).
        //     ⇒ G-45 가 이제 **컨트롤러의 실제 Route 를 읽어** 대조한다.
        //
        //   ⚠️ 옛 글자도 **남겨 둔다**(헌법 #1) — 지워도 동작은 같지만, 옛 화면이
        //     그 주소로 부를 가능성을 굳이 끊을 이유가 없다.
        "/api/app-version",       // 🔴 실제 경로 — UpdateProgressOverlay 가 이것을 부른다
        "/api/appversion",        // (옛 표기 · 호환용으로 남긴다)
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

    public async Task InvokeAsync(HttpContext context, IDbConnection db,
        HitPan.Application.Interfaces.ITenantDeviceService deviceSvc)
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
                // 🔴 2026-08-19 20260819작1 (K-3) — **비밀값과 기기 번호를 짝으로 요구한다.**
                //
                //   [무엇이 문제였나] 종전 축① 은 해시만 봤다:
                //     `WHERE tenant_id=@Tid AND auth_key_hash=@Hash AND status='approved'`
                //     기기 조건이 없어 **한 기기의 비밀값이 회사 공용 통행증**이 될 수 있었다.
                //     8/18 축② 주석이 *"남은 구멍(기기별 비밀값)은 다음 차수 몫"* 이라 적은 그 자리다.
                //
                //   [고침] 판정을 TenantDeviceService.VerifyDeviceSecretAsync 로 모았다 —
                //     같은 회사 · **그 기기 번호** · 그 줄의 해시 · 승인 상태, 넷이 전부 맞아야 통과다.
                //     ⚠️ 인라인 SQL 을 서비스 호출로 바꾼 것은 두 자리가 갈리는 사고를 막기 위해서다
                //       (축② 가 IsDeviceAllowedAsync 와 같은 모양을 유지하는 이유와 같다).
                //
                //   ⚠️ 이 헤더에 실리는 값은 이제 **사람 키가 아니라 기계비밀**이다(K-1) —
                //     verify-key 성공 시 서버가 만들어 화면이 보관한 값. 사람 키는 verify 순간 죽는다.
                ok = await deviceSvc.VerifyDeviceSecretAsync(
                    deviceId, authKey, tenantId, context.RequestAborted);
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
            //   🔴 2026-08-18 20260818작1 (1-2) — **이 길을 메인PC 한 줄로 좁혔다.**
            //
            //     [무엇이 문제였나] `status='approved'` 만 봤다. 그런데 `device_id` 는
            //       **비밀이 아니다** — 기기 목록 화면에 보이고, gate-status **쿼리스트링
            //       (서버 로그에 평문)** 에 실리며, 브라우저 저장소에 그대로 있다.
            //       ⇒ 승인된 아무 기기의 번호나 헤더에 넣으면 **그것만으로 문이 열렸다.**
            //
            //     [정확히 무엇을 고친 것인가] 이것은 **"도용 차단" 이 아니다.**
            //       번호가 비밀이 아닌 이상 메인PC 번호를 손에 넣은 자는 **여전히 통과한다.**
            //       이 봉합이 하는 일은 **통과 가능한 범위를 메인PC 한 줄로 좁히는 것**이다.
            //       ⚠️ 이것을 "도용을 막았다" 고 적으면 거짓봉합이 된다. 남은 구멍(기기별 비밀값)은
            //         다음 차수 몫이고, 게이트 G-32-d 가 그 사실을 값으로 세워 두었다.
            //
            //     [왜 하필 메인PC 인가] 이 길은 **메인PC 를 구하려고** 낸 길이다(8/16 P0) —
            //       메인PC 는 인증키를 받은 적이 없어 **다른 길이 없다.**
            //       나머지 기기는 **인증키라는 제 길**이 있으므로 이 길을 열어 둘 이유가 없다.
            //
            //     ⚠️ 판정을 TenantDeviceService.IsDeviceAllowedAsync 와 **같은 모양**으로 둔다.
            //       두 자리가 갈리면 한쪽만 고쳐지는 사고가 난다(이 파일이 이미 그 사고를 겪었다).
            if (!ok && !string.IsNullOrWhiteSpace(deviceId))
            {
                ok = (await db.ExecuteScalarAsync<int>(new CommandDefinition(
                    @"SELECT COUNT(*) FROM tenant_devices
                       WHERE tenant_id = @Tid AND device_id = @Did
                         AND status = 'approved' AND is_main_pc = 1",
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
            //   🔴 2026-08-18 20260818작1 (1-3) — **예외를 지우지 않았다. 경로만 좁혔다.**
            //
            //     [무엇이 문제였나] 이 예외에 **경로 조건이 0개**였다.
            //       ⇒ 대표계정으로 로그인하면 **미승인 기기가 ERP 전체를 쓴다.**
            //         대표 한 명의 계정이 모든 기기 통제를 무력화하는 자리였다.
            //
            //     [왜 지우지 않는가] 지우면 *"대표가 막히면 누가 풀어주나"* 가 되살아난다 —
            //       8/16 PR#169 가 닫은 자리다(헌법 #1). **막다른 길은 그대로 없애고,
            //       열려 있던 ERP 전체만 닫는다.**
            //
            //     [무엇만 남기나] 대표가 **스스로 빠져나오는 데 필요한 길**뿐이다:
            //       · `/api/devices` — 자기 기기를 승인·해제하는 화면
            //       · `/api/auth`    — 로그인·갱신·내 정보
            //       ⚠️ 이 두 길은 사실 위 BypassPrefixes 에 이미 있어 여기 오지도 않는다.
            //         그래도 **명시해 둔다** — 나중에 누가 BypassPrefixes 에서 빼는 날
            //         대표가 갇히는 것을 이 목록이 막는다(그 사고를 이미 한 번 겪었다).
            //
            //     ⚠️ 직원 계정(tenant_user)은 종전대로 막힌다. 요금·접근 통제는 유지된다.
            if (!ok)
            {
                var accountType = context.User?.FindFirst("account_type")?.Value;
                if (string.Equals(accountType, "tenant_admin", StringComparison.Ordinal)
                    && IsOwnerEscapePath(path))
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

    /// 🔴 대표계정이 **스스로 빠져나오는 데 필요한 길** (20260818작1 1-3).
    ///
    ///   ⚠️ 이 목록은 **좁을수록 옳다.** 여기에 길을 더하는 것은
    ///     *"미승인 기기의 대표계정에게 그 기능을 열어준다"* 는 뜻이다.
    ///     더하기 전에 물어라 — **그 길이 없으면 대표가 갇히는가?** 아니면 넣지 마라.
    private static readonly string[] OwnerEscapePrefixes = new[]
    {
        "/api/devices",   // 기기 승인·해제 — 대표가 자기를 푸는 자리
        "/api/auth"       // 로그인·갱신·내 정보
    };

    private static bool IsOwnerEscapePath(string path)
    {
        foreach (var prefix in OwnerEscapePrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // Sha256Hex 는 K-3(20260819작1)에서 제거 — 해시 대조가 VerifyDeviceSecretAsync 로 모였다.
}
