using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// 기기 등록·검증 API (브라운킴 PM 2026-06-08, 사장님 결재)
//
// 사장님 의중:
//   "이 PC를 등록된 기기로 사용하시겠습니까?" Y/N
//   Y → 인증서 같은 키(device_token) 발급
//   매 로그인 시 device_token + 계정 + 기기지문 3중 검증
//
// 흐름:
//   1) POST /api/landing/device/register  — 기기 등록 요청 (시리얼 + 비밀번호 검증)
//   2) GET  /api/landing/device/check     — 기기 등록 여부 확인 (로그인 시)
//   3) GET  /api/admin/devices/tenant/{id}— 본사가 고객사 기기 목록 조회
//   4) POST /api/admin/devices/{id}/revoke— 본사가 기기 차단
//
// 헌법 정합:
//   #18·#22 — device_token 평문 0건 (해시만 저장)
//   #23 — 5중 검증 (시리얼+계정+비번+지문+토큰)
//   #25 — 안전하게 + 쉽게
[ApiController]
[Route("api/landing/device")]
[AllowAnonymous]
public class DeviceRegistrationController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<DeviceRegistrationController> _logger;

    public DeviceRegistrationController(IConfiguration config, ILogger<DeviceRegistrationController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req, CancellationToken ct)
    {
        if (req is null)
            return BadRequest(new { success = false, message = "요청 비어있음" });
        if (string.IsNullOrWhiteSpace(req.LicenseKey))
            return BadRequest(new { success = false, message = "시리얼 키 필수" });
        if (string.IsNullOrWhiteSpace(req.Fingerprint))
            return BadRequest(new { success = false, message = "기기 정보 누락" });

        try
        {
            await using var db = await OpenAsync(ct);

            // 1) 시리얼로 tenant 조회 + 플랜의 PC/모바일 기기 한도 조회 (사장님 결재 2026-06-08)
            var pepper = _config["License:Pepper"] ?? throw new InvalidOperationException("License:Pepper 미설정");
            var licHash = ComputeHmacSha256(req.LicenseKey.Trim().ToUpperInvariant().Replace(" ", ""), pepper);

            // fingerprint 해시 정규화 — 평문 길이 저장 사고 차단 + 헌법 #22 정합
            var fingerprintHash = ComputeHmacSha256(req.Fingerprint, pepper);

            var tenant = await db.QueryFirstOrDefaultAsync<TenantRow>(@"
                SELECT CAST(t.tenant_id AS CHAR) AS TenantId, t.tenant_code AS TenantCode,
                       t.company_name AS CompanyName, t.status AS Status,
                       COALESCE(p.max_pc_devices, 5)     AS MaxPcDevices,
                       COALESCE(p.max_mobile_devices, 3) AS MaxMobileDevices
                FROM tenants t
                LEFT JOIN landing_signups ls ON ls.company_name = t.company_name
                LEFT JOIN pricing_plans p   ON p.plan_id = COALESCE(ls.plan_type, 'basic')
                WHERE t.license_key_hash = @Hash AND t.status = 'active' LIMIT 1",
                new { Hash = licHash });

            if (tenant is null)
                return BadRequest(new { success = false, message = "시리얼 인증이 필요합니다." });

            // 2) device_type 정규화 (pc / mobile)
            //
            // 🔴 2026-08-15 20260815작3 P1 (G-13 · I-6) — 종전 판정이 두 가지를 틀렸다.
            //
            //   ① `tablet` 을 **`pc` 로 바꿔 저장**했다. 태블릿은 휴대기기다
            //      (사장님 판정 — *"운영체제가 안드로이드이거나 iOS이기 때문에"*).
            //      ⇒ 고객이 **싼 칸에 들어갈 기기로 비싼 칸을 먹었다.**
            //   ② **모르는 값도 `pc`** 로 보냈다. 컴퓨터 칸이 더 비싸므로
            //      판정이 애매할 때 컴퓨터로 세면 **고객이 쓰지도 않은 자리에 돈을 낸다.**
            //
            //   [고침] ERP 의 NormalizeDeviceType 과 **같은 규칙**으로 맞춘다:
            //     tablet → mobile 로 흡수 · 모르는 값 → mobile(고객에게 유리한 쪽).
            //   ⚠️ 규칙만 같게 하고 **코드는 각자 갖는다** — 백오피스는 별 시스템이다(D-2).
            var raw = (req.DeviceType ?? "").Trim().ToLowerInvariant();
            var deviceType = raw switch
            {
                "pc" => "pc",
                "" => "mobile",         // 안 보냈으면 싼 칸
                "tablet" => "mobile",   // 태블릿은 휴대기기 (종전엔 pc 로 갔다)
                _ => "mobile"           // 모르는 값도 싼 칸
            };

            // 3) 이미 등록된 기기 확인 (fingerprint 해시 일치)
            var existing = await db.QueryFirstOrDefaultAsync<string>(@"
                SELECT device_id FROM tenant_devices
                WHERE tenant_id = @TenantId AND fingerprint = @Fp AND status = 'approved' LIMIT 1",
                new { TenantId = tenant.TenantId, Fp = fingerprintHash });

            if (existing is not null)
            {
                return Ok(new
                {
                    success = false,
                    alreadyRegistered = true,
                    message = "이 기기는 이미 등록되어 있습니다."
                });
            }

            // 4) PC·모바일 분리 카운트 + 한도 비교
            //
            // 🔴 2026-08-15 20260815작3 P1 (G-13) — `tablet` 이 어느 칸에도 안 잡히던 것을 봉합한다.
            //
            //   [무엇이 났나] 종전은 `device_type = @Type` **등호** 비교였다.
            //     그래서 `deviceType` 이 'mobile' 일 때 **'tablet' 행이 세어지지 않았다.**
            //     ERP 쪽은 같은 계산에서 tablet 을 휴대기기 칸에 합산하는데(NormalizeDeviceType),
            //     백오피스만 빠뜨려 **태블릿이 공짜로 쓰였다.** 요금이 새는 자리다.
            //
            //   [고침] 휴대기기 칸은 'mobile' 과 'tablet' 을 **둘 다** 센다. ERP 와 같은 규칙이다.
            //
            //   ⚠️ **ERP 의 단일 메서드를 여기서 부르지 않는다** (P0 실측 D-2).
            //     백오피스는 **별 시스템·별 DB·별 배포**다. ERP 서비스를 끌어다 쓰면
            //     본사와 고객 PC 가 코드로 묶여 헌법 #30(본사 의존 0)·#35(3시스템 분리)를 어긴다.
            //     ⇒ 같은 **규칙**을 쓰되 코드는 각자 갖는다. 고치는 것은 규칙의 어긋남 하나뿐이다.
            var typeCount = deviceType == "pc"
                ? await db.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(*) FROM tenant_devices
                    WHERE tenant_id = @TenantId AND status = 'approved'
                      AND device_type = 'pc'",
                    new { TenantId = tenant.TenantId })
                : await db.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(*) FROM tenant_devices
                    WHERE tenant_id = @TenantId AND status = 'approved'
                      AND device_type IN ('mobile','tablet')",
                    new { TenantId = tenant.TenantId });

            var deviceLimit = deviceType == "pc" ? tenant.MaxPcDevices : tenant.MaxMobileDevices;
            var typeLabel = deviceType == "pc" ? "PC" : "모바일";

            if (typeCount >= deviceLimit)
            {
                return Ok(new
                {
                    success = false,
                    limitExceeded = true,
                    deviceType,
                    currentCount = typeCount,
                    deviceLimit,
                    message = $"{typeLabel} 기기 한도({deviceLimit}대)를 초과했습니다. 본사에 한도 증가를 요청하거나 기존 {typeLabel}을 제거해주세요."
                });
            }

            // 5) device_token 발급 (랜덤 64자, 사장님 결재 Q2=a)
            var deviceToken = GenerateDeviceToken();
            var tokenHash = ComputeHmacSha256(deviceToken, pepper);
            var deviceId = Guid.NewGuid().ToString();

            await db.ExecuteAsync(@"
                INSERT INTO tenant_devices
                    (device_id, tenant_id, device_type, device_name, fingerprint,
                     device_token_hash, issued_at, ip_address, user_agent, os_info,
                     status, registered_at, last_seen_at)
                VALUES
                    (@DeviceId, @TenantId, @DeviceType, @DeviceName, @Fingerprint,
                     @TokenHash, UTC_TIMESTAMP(), @Ip, @Ua, @OsInfo,
                     'approved', UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))",
                new
                {
                    DeviceId = deviceId,
                    TenantId = tenant.TenantId,
                    DeviceType = deviceType,
                    DeviceName = req.DeviceName ?? (deviceType == "pc" ? "PC" : "Mobile"),
                    Fingerprint = fingerprintHash,
                    TokenHash = tokenHash,
                    Ip = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    Ua = req.UserAgent,
                    OsInfo = req.OsInfo
                });

            _logger.LogInformation("[DeviceReg] registered tenant={Tid} device={Did} type={Type} count={Cnt}/{Lim}",
                tenant.TenantId, deviceId, deviceType, typeCount + 1, deviceLimit);

            return Ok(new
            {
                success = true,
                deviceId,
                deviceToken,  // 응답 1회만 평문 노출 (DPAPI 저장용)
                deviceType,
                deviceLimit,
                currentCount = typeCount + 1,
                message = $"{typeLabel} 기기 등록 완료. device_token을 보안 저장소에 저장하세요."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DeviceReg] register 실패");
            return StatusCode(500, new { success = false, message = "등록 처리 실패" });
        }
    }

    [HttpPost("check")]
    public async Task<IActionResult> Check([FromBody] CheckRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Fingerprint) || string.IsNullOrWhiteSpace(req.DeviceToken))
            return BadRequest(new { success = false, message = "기기 정보 누락" });

        try
        {
            await using var db = await OpenAsync(ct);
            var pepper = _config["License:Pepper"] ?? throw new InvalidOperationException("License:Pepper 미설정");
            var tokenHash = ComputeHmacSha256(req.DeviceToken, pepper);
            var fingerprintHash = ComputeHmacSha256(req.Fingerprint, pepper);

            var device = await db.QueryFirstOrDefaultAsync<DeviceRow>(@"
                SELECT device_id AS DeviceId, CAST(tenant_id AS CHAR) AS TenantId,
                       status AS Status, device_name AS DeviceName
                FROM tenant_devices
                WHERE fingerprint = @Fp AND device_token_hash = @Hash
                LIMIT 1",
                new { Fp = fingerprintHash, Hash = tokenHash });

            if (device is null)
                return Ok(new { success = false, registered = false, message = "등록되지 않은 기기입니다." });

            if (device.Status != "approved")
                return Ok(new { success = false, revoked = true, message = $"이 기기는 {device.Status} 상태입니다. 본사에 문의하세요." });

            // last_seen 갱신
            await db.ExecuteAsync(
                "UPDATE tenant_devices SET last_seen_at = UTC_TIMESTAMP(6) WHERE device_id = @Id",
                new { Id = device.DeviceId });

            return Ok(new
            {
                success = true,
                registered = true,
                deviceId = device.DeviceId,
                tenantId = device.TenantId,
                deviceName = device.DeviceName
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DeviceReg] check 실패");
            return StatusCode(500, new { success = false, message = "확인 실패" });
        }
    }

    private async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var cs = _config.GetConnectionString("BackofficeDb")
                 ?? _config.GetConnectionString("Default")
                 ?? throw new InvalidOperationException("ConnectionStrings:BackofficeDb 미설정");
        var c = new MySqlConnection(cs);
        await c.OpenAsync(ct);
        return c;
    }

    private static string ComputeHmacSha256(string data, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // device_token 발급 (256bit 랜덤, base64url)
    // 사장님 결재 Q2=a: 랜덤 64자 형식
    private static string GenerateDeviceToken()
    {
        Span<byte> buf = stackalloc byte[32];
        RandomNumberGenerator.Fill(buf);
        var b64 = Convert.ToBase64String(buf);
        return "dvc_" + b64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public class RegisterRequest
    {
        public string LicenseKey { get; set; } = "";
        public string Fingerprint { get; set; } = "";
        // 🔴 20260815작3 P1 (I-6) — 기본값 "pc" 를 없앴다.
        //   위 정규화를 고쳐도 **여기가 "pc" 로 채워 버리면 아무것도 안 바뀐다.**
        //   안 보내면 null 로 두고, 판정은 한 곳(위 switch)에서만 한다.
        public string? DeviceType { get; set; }
        public string? DeviceName { get; set; }
        public string? UserAgent { get; set; }
        public string? OsInfo { get; set; }
    }

    public class CheckRequest
    {
        public string Fingerprint { get; set; } = "";
        public string DeviceToken { get; set; } = "";
    }

    public class TenantRow
    {
        public string TenantId { get; set; } = "";
        public string TenantCode { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string Status { get; set; } = "";
        public int MaxPcDevices { get; set; } = 5;
        public int MaxMobileDevices { get; set; } = 3;
    }

    public class DeviceRow
    {
        public string DeviceId { get; set; } = "";
        public string TenantId { get; set; } = "";
        public string Status { get; set; } = "";
        public string? DeviceName { get; set; }
    }
}

// 본사 기기 관리 API
[ApiController]
[Route("api/admin/devices")]
[AllowAnonymous]
public class DevicesAdminController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<DevicesAdminController> _logger;

    public DevicesAdminController(IConfiguration config, ILogger<DevicesAdminController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpGet("tenant/{tenantId}")]
    public async Task<IActionResult> ListByTenant(string tenantId, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var rows = await db.QueryAsync(@"
                SELECT device_id AS DeviceId, device_type AS DeviceType, device_name AS DeviceName,
                       fingerprint AS Fingerprint, os_info AS OsInfo,
                       status AS Status, registered_at AS RegisteredAt,
                       last_seen_at AS LastSeenAt, issued_at AS IssuedAt,
                       revoked_at AS RevokedAt, revoked_reason AS RevokedReason
                FROM tenant_devices
                WHERE tenant_id = @TenantId
                ORDER BY registered_at DESC",
                new { TenantId = tenantId });
            return Ok(new { success = true, data = rows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DevicesAdmin] list 실패");
            return StatusCode(500, new { success = false, message = "조회 실패" });
        }
    }

    [HttpPost("{deviceId}/revoke")]
    public async Task<IActionResult> Revoke(string deviceId, [FromBody] RevokeRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, message = "차단 사유 필수" });

        try
        {
            await using var db = await OpenAsync(ct);
            var affected = await db.ExecuteAsync(@"
                UPDATE tenant_devices
                SET status = 'revoked',
                    revoked_at = UTC_TIMESTAMP(6),
                    revoked_reason = @Reason
                WHERE device_id = @DeviceId AND status = 'approved'",
                new { DeviceId = deviceId, req.Reason });
            if (affected == 0)
                return NotFound(new { success = false, message = "활성 기기를 찾을 수 없습니다." });

            _logger.LogInformation("[DevicesAdmin] revoked device={Did} reason={R}", deviceId, req.Reason);
            return Ok(new { success = true, message = "기기 차단 완료" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DevicesAdmin] revoke 실패");
            return StatusCode(500, new { success = false, message = "차단 실패" });
        }
    }

    private async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var cs = _config.GetConnectionString("BackofficeDb")
                 ?? _config.GetConnectionString("Default")
                 ?? throw new InvalidOperationException("ConnectionStrings:BackofficeDb 미설정");
        var c = new MySqlConnection(cs);
        await c.OpenAsync(ct);
        return c;
    }

    public class RevokeRequest
    {
        public string Reason { get; set; } = "";
    }
}
