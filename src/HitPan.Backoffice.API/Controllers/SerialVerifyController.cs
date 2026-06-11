using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// 시리얼 검증 API (브라운킴 PM 작성 2026-06-08, 사장님 결재)
//
// 사장님 헌법:
//   1) ERP 사용자가 시리얼 입력 → 본사 API 호출 → license_key_hash 비교
//   2) 일치 시 tenant.serial_verified=1 → ERP 활성화 + 회사정보 응답
//   3) 5회 실패 시 client_fingerprint 단위로 잠금 → 본사 수동 해제
//
// 헌법 정합:
//   #3 INSERT ONLY — serial_verify_attempts 이력
//   #18·#22 — 시리얼 평문 0건 (해시만 저장·비교)
//   #25 — 안전하게
[ApiController]
[Route("api/landing/serial")]
[AllowAnonymous]
public class SerialVerifyController : ControllerBase
{
    private const int MaxFailedAttempts = 5;
    private const int FailWindowMinutes = 60;  // 60분 내 5회면 잠금

    private readonly IConfiguration _config;
    private readonly ILogger<SerialVerifyController> _logger;

    public SerialVerifyController(IConfiguration config, ILogger<SerialVerifyController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] VerifyRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.LicenseKey))
            return BadRequest(new { success = false, message = "시리얼 키를 입력해주세요." });
        if (string.IsNullOrWhiteSpace(req.ClientFingerprint))
            return BadRequest(new { success = false, message = "기기 정보가 누락되었습니다." });

        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var normalizedKey = req.LicenseKey.Trim().ToUpperInvariant().Replace(" ", "");
        var pepper = _config["License:Pepper"] ?? throw new InvalidOperationException("License:Pepper 미설정");
        var submittedHash = ComputeHmacSha256(normalizedKey, pepper);

        // fingerprint 영역은 user-agent + 해상도 + timezone 박힌 영역 — 길이 박혀있어 저장 영역 컬럼 초과 가능.
        // SHA-256 해시 64자 박은 영역으로 정규화. 헌법 #22 정합 (본사가 client 식별 평문 박지 않음).
        var fingerprintHash = ComputeHmacSha256(req.ClientFingerprint, pepper);

        try
        {
            await using var db = await OpenAsync(ct);

            // 1) 기기 잠금 상태 확인
            var lockState = await db.QueryFirstOrDefaultAsync<LockRow>(@"
                SELECT lock_id AS LockId, failed_count AS FailedCount, is_locked AS IsLocked,
                       last_failed_at AS LastFailedAt
                FROM serial_verify_locks WHERE client_fingerprint = @Fp",
                new { Fp = fingerprintHash });

            if (lockState is not null && lockState.IsLocked == 1)
            {
                await LogAttempt(db, null, submittedHash, clientIp, fingerprintHash, "locked");
                _logger.LogWarning("[SerialVerify] locked fingerprint={Fp}", fingerprintHash);
                return StatusCode(423, new
                {
                    success = false,
                    locked = true,
                    message = "시리얼 입력 5회 실패로 사용이 중지되었습니다. 본사 고객센터에 문의해주세요."
                });
            }

            // 2) 시리얼 검증 (license_key_hash 비교)
            var tenant = await db.QueryFirstOrDefaultAsync<TenantRow>(@"
                SELECT CAST(tenant_id AS CHAR) AS TenantId, tenant_code AS TenantCode,
                       company_name AS CompanyName, status AS Status,
                       serial_verified AS SerialVerified
                FROM tenants WHERE license_key_hash = @Hash AND status = 'active'
                LIMIT 1",
                new { Hash = submittedHash });

            if (tenant is null)
            {
                // 실패 카운트 증가
                await IncrementFailedCount(db, fingerprintHash, ct);
                await LogAttempt(db, null, submittedHash, clientIp, fingerprintHash, "mismatch");

                // 다시 조회해서 남은 시도 횟수 반환
                var current = await db.QueryFirstOrDefaultAsync<LockRow>(
                    "SELECT failed_count AS FailedCount, is_locked AS IsLocked FROM serial_verify_locks WHERE client_fingerprint = @Fp",
                    new { Fp = fingerprintHash });
                var failedCount = current?.FailedCount ?? 1;
                var nowLocked = current?.IsLocked == 1;
                var remaining = Math.Max(0, MaxFailedAttempts - failedCount);

                _logger.LogInformation("[SerialVerify] mismatch fingerprint={Fp} count={Cnt} locked={L}",
                    fingerprintHash, failedCount, nowLocked);

                if (nowLocked)
                {
                    return StatusCode(423, new
                    {
                        success = false,
                        locked = true,
                        message = "시리얼 입력 5회 실패로 사용이 중지되었습니다. 본사 고객센터에 문의해주세요."
                    });
                }

                return Ok(new
                {
                    success = false,
                    locked = false,
                    remainingAttempts = remaining,
                    message = $"올바르지 않은 시리얼입니다. (남은 시도: {remaining}회)"
                });
            }

            // 3) 일치 — tenant 활성화 + 실패 카운트 리셋
            await db.ExecuteAsync(@"
                UPDATE tenants
                SET serial_verified = 1,
                    serial_verified_at = UTC_TIMESTAMP(),
                    serial_verified_fingerprint = @Fp,
                    updated_at = UTC_TIMESTAMP()
                WHERE tenant_id = @TenantId",
                new { TenantId = tenant.TenantId, Fp = fingerprintHash });

            await db.ExecuteAsync(@"
                UPDATE serial_verify_locks
                SET failed_count = 0, is_locked = 0
                WHERE client_fingerprint = @Fp",
                new { Fp = fingerprintHash });

            await LogAttempt(db, tenant.TenantId, submittedHash, clientIp, fingerprintHash, "success");

            _logger.LogInformation("[SerialVerify] success tenant={Tid} code={Code}",
                tenant.TenantId, tenant.TenantCode);

            // 회사정보 응답 (ERP 화면 자동 입력용)
            var companyInfo = await db.QueryFirstOrDefaultAsync<CompanyInfo>(@"
                SELECT t.tenant_code AS TenantCode, t.company_name AS CompanyName,
                       t.biz_no AS BizNo, t.ceo_name AS CeoName,
                       t.tel AS Tel, t.address AS Address,
                       t.status AS Status,
                       ls.email AS Email, ls.phone AS Phone, ls.plan_type AS PlanType
                FROM tenants t
                LEFT JOIN landing_signups ls ON ls.company_name = t.company_name
                WHERE t.tenant_id = @TenantId",
                new { TenantId = tenant.TenantId });

            return Ok(new
            {
                success = true,
                locked = false,
                message = "시리얼 인증 완료 — 히트판 ERP 사용 활성화",
                tenantId = tenant.TenantId,
                tenantCode = tenant.TenantCode,
                companyInfo
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SerialVerify] 처리 실패");
            return StatusCode(500, new { success = false, message = "검증 처리 중 오류가 발생했습니다." });
        }
    }

    private async Task IncrementFailedCount(MySqlConnection db, string fingerprint, CancellationToken ct)
    {
        // UPSERT — 처음이면 INSERT, 있으면 카운트 증가
        await db.ExecuteAsync(@"
            INSERT INTO serial_verify_locks (client_fingerprint, failed_count, first_failed_at, last_failed_at)
            VALUES (@Fp, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
                failed_count = CASE
                    WHEN last_failed_at < DATE_SUB(UTC_TIMESTAMP(6), INTERVAL @Window MINUTE) THEN 1
                    ELSE failed_count + 1
                END,
                first_failed_at = CASE
                    WHEN last_failed_at < DATE_SUB(UTC_TIMESTAMP(6), INTERVAL @Window MINUTE) THEN UTC_TIMESTAMP(6)
                    ELSE first_failed_at
                END,
                last_failed_at = UTC_TIMESTAMP(6),
                is_locked = CASE
                    WHEN last_failed_at < DATE_SUB(UTC_TIMESTAMP(6), INTERVAL @Window MINUTE) THEN 0
                    WHEN failed_count + 1 >= @Max THEN 1
                    ELSE is_locked
                END,
                locked_at = CASE
                    WHEN last_failed_at < DATE_SUB(UTC_TIMESTAMP(6), INTERVAL @Window MINUTE) THEN NULL
                    WHEN failed_count + 1 >= @Max AND is_locked = 0 THEN UTC_TIMESTAMP()
                    ELSE locked_at
                END",
            new { Fp = fingerprint, Window = FailWindowMinutes, Max = MaxFailedAttempts });
    }

    private async Task LogAttempt(MySqlConnection db, string? tenantId, string submittedHash,
        string? clientIp, string fingerprint, string result)
    {
        await db.ExecuteAsync(@"
            INSERT INTO serial_verify_attempts (tenant_id, submitted_hash, client_ip, client_fingerprint, result)
            VALUES (@TenantId, @Hash, @Ip, @Fp, @Result)",
            new { TenantId = tenantId, Hash = submittedHash, Ip = clientIp, Fp = fingerprint, Result = result });
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

    public class VerifyRequest
    {
        public string LicenseKey { get; set; } = "";
        public string ClientFingerprint { get; set; } = "";
    }

    public class LockRow
    {
        public long LockId { get; set; }
        public int FailedCount { get; set; }
        public int IsLocked { get; set; }
        public DateTime LastFailedAt { get; set; }
    }

    public class TenantRow
    {
        public string TenantId { get; set; } = "";
        public string TenantCode { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string Status { get; set; } = "";
        public int SerialVerified { get; set; }
    }

    public class CompanyInfo
    {
        public string TenantCode { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string? BizNo { get; set; }
        public string? CeoName { get; set; }
        public string? Tel { get; set; }
        public string? Address { get; set; }
        public string Status { get; set; } = "";
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? PlanType { get; set; }
    }
}

// 본사 잠금 해제 API (super_admin 권한)
[ApiController]
[Route("api/admin/serial-locks")]
[AllowAnonymous]
public class SerialLocksAdminController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<SerialLocksAdminController> _logger;

    public SerialLocksAdminController(IConfiguration config, ILogger<SerialLocksAdminController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool? lockedOnly, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var rows = await db.QueryAsync(@"
                SELECT lock_id AS LockId, client_fingerprint AS ClientFingerprint,
                       failed_count AS FailedCount, is_locked AS IsLocked,
                       first_failed_at AS FirstFailedAt, last_failed_at AS LastFailedAt,
                       locked_at AS LockedAt, unlocked_at AS UnlockedAt,
                       unlocked_by AS UnlockedBy, unlock_reason AS UnlockReason
                FROM serial_verify_locks
                WHERE (@LockedOnly IS NULL OR @LockedOnly = 0 OR is_locked = 1)
                ORDER BY last_failed_at DESC LIMIT 100",
                new { LockedOnly = lockedOnly == true ? 1 : (int?)null });
            return Ok(new { success = true, data = rows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SerialLocks] list 실패");
            return StatusCode(500, new { success = false, message = "조회 실패" });
        }
    }

    [HttpPost("{lockId:long}/unlock")]
    public async Task<IActionResult> Unlock(long lockId, [FromBody] UnlockRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, message = "잠금 해제 사유를 입력해주세요." });

        try
        {
            await using var db = await OpenAsync(ct);
            var affected = await db.ExecuteAsync(@"
                UPDATE serial_verify_locks
                SET is_locked = 0, failed_count = 0,
                    unlocked_at = UTC_TIMESTAMP(), unlocked_by = @By, unlock_reason = @Reason
                WHERE lock_id = @Id AND is_locked = 1",
                new { Id = lockId, By = req.UnlockedBy ?? "system", req.Reason });

            if (affected == 0) return NotFound(new { success = false, message = "잠금 상태가 아닙니다." });

            _logger.LogInformation("[SerialLocks] unlocked lockId={Id} by={By} reason={R}",
                lockId, req.UnlockedBy, req.Reason);
            return Ok(new { success = true, message = "잠금 해제 완료" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SerialLocks] unlock 실패");
            return StatusCode(500, new { success = false, message = "처리 실패" });
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

    public class UnlockRequest
    {
        public string Reason { get; set; } = "";
        public string? UnlockedBy { get; set; }
    }
}
