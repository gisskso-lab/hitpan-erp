using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Device;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>
/// 테넌트 기기 등록/조회/폐기 서비스.
/// - 히트판 과금 모델: 계정 무제한, 기기 수 제한(PC + 모바일).
/// - 티어별 기본 한도:
///     basic  = PC 5  / Mobile 3   (= 8대)
///     pro    = PC 10 / Mobile 8   (= 18대)
///     premium= PC 100/ Mobile 80  (= 180대)
///     trial  = PC 10 / Mobile 5
///     기본   = PC 5  / Mobile 3
/// - 추가 슬롯 1개 구매 = PC +1 OR 모바일 +1, 그리고 보너스로 모바일 +1 추가 허용.
///   (단순화: pc_limit += extra, mobile_limit += extra*2)
/// - MVP에서는 OTP/관리자 승인 없이 자동 approved. 추후 고도화.
/// </summary>
public sealed class TenantDeviceService : ITenantDeviceService
{
    private readonly IDbConnection _db;
    private readonly IAuditService _audit;

    public TenantDeviceService(IDbConnection db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    // ── 목록 조회 ──
    public async Task<List<DeviceListDto>> GetAllAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var rows = (await _db.QueryAsync<DeviceListDto>(new CommandDefinition(
            """
            SELECT d.device_id     AS DeviceId,
                   d.device_type   AS DeviceType,
                   d.device_name   AS DeviceName,
                   d.user_id       AS UserId,
                   u.user_name     AS UserName,
                   d.ip_address    AS IpAddress,
                   d.status        AS Status,
                   d.registered_at AS RegisteredAt,
                   d.last_seen_at  AS LastSeenAt
            FROM tenant_devices d
            LEFT JOIN users u ON u.user_id = d.user_id
            WHERE d.tenant_id = @TenantId
            ORDER BY d.registered_at DESC
            """,
            new { TenantId = tenantId }, cancellationToken: ct))).ToList();
        return rows;
    }

    // ── 쿼터 계산 (approved 기기 수 기반) ──
    public async Task<DeviceQuotaDto> GetQuotaAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var tenant = await _db.QueryFirstOrDefaultAsync<(string? tier, int extra)>(new CommandDefinition(
            "SELECT subscription_tier AS tier, COALESCE(extra_device_slots, 0) AS extra FROM local_subscription WHERE tenant_id = @TenantId",
            new { TenantId = tenantId }, cancellationToken: ct));

        var (pcLimit, mobileLimit) = GetLimitsForTier(tenant.tier);
        // 추가 슬롯은 단순화 규칙: pc +1, mobile +2
        pcLimit += tenant.extra;
        mobileLimit += tenant.extra * 2;

        // approved 기기 수 집계 (pc는 pc/tablet 합산이 아니라 pc만, tablet은 mobile로 묶음)
        var counts = (await _db.QueryAsync<(string t, int c)>(new CommandDefinition(
            """
            SELECT device_type AS t, COUNT(*) AS c
            FROM tenant_devices
            WHERE tenant_id = @TenantId AND status = 'approved'
            GROUP BY device_type
            """,
            new { TenantId = tenantId }, cancellationToken: ct))).ToList();

        int pcUsed = counts.Where(x => x.t == "pc").Sum(x => x.c);
        int mobileUsed = counts.Where(x => x.t == "mobile" || x.t == "tablet").Sum(x => x.c);

        return new DeviceQuotaDto
        {
            PcLimit = pcLimit,
            MobileLimit = mobileLimit,
            PcUsed = pcUsed,
            MobileUsed = mobileUsed,
            ExtraSlots = tenant.extra,
            SubscriptionTier = tenant.tier ?? "basic"
        };
    }

    // ── 로그인 시 호출: 기존 기기면 last_seen 갱신, 신규면 한도 검사 후 등록 ──
    public async Task<(bool allowed, string reason, string? deviceId)> RegisterOrRefreshAsync(
        string tenantId,
        string userId,
        RegisterDeviceRequest req,
        string ipAddress,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Fingerprint))
        {
            // 지문 미지원 클라이언트 → 등록 스킵 (로그인은 허용)
            return (true, "", null);
        }

        await EnsureOpenAsync(ct);

        // 1) 기존 기기(같은 tenant + fingerprint)가 있는지 확인
        var existing = await _db.QueryFirstOrDefaultAsync<(string id, string status)?>(new CommandDefinition(
            """
            SELECT device_id AS id, status AS status
            FROM tenant_devices
            WHERE tenant_id = @TenantId AND fingerprint = @Fp
            LIMIT 1
            """,
            new { TenantId = tenantId, Fp = req.Fingerprint }, cancellationToken: ct));

        if (existing is not null)
        {
            var (id, status) = existing.Value;
            // 폐기된 기기는 재사용 금지
            if (status == "revoked")
            {
                await LogDeniedAsync(tenantId, userId, ipAddress, id, "denied_revoked", ct);
                return (false, "폐기된 기기입니다. 관리자에게 문의하세요.", null);
            }

            // approved / pending → last_seen / ip / ua 갱신
            await _db.ExecuteAsync(new CommandDefinition(
                """
                UPDATE tenant_devices
                SET last_seen_at = NOW(),
                    ip_address   = @Ip,
                    user_agent   = COALESCE(@Ua, user_agent)
                WHERE device_id = @Id
                """,
                new { Id = id, Ip = ipAddress, Ua = req.UserAgent }, cancellationToken: ct));

            await LogLoginAsync(tenantId, userId, ipAddress, id, "success", ct);
            return (status == "approved", status == "approved" ? "" : "기기 승인 대기 중입니다.", id);
        }

        // 2) 신규 기기 — 티어별 한도 검사
        var tenant = await _db.QueryFirstOrDefaultAsync<(string? tier, int extra)>(new CommandDefinition(
            "SELECT subscription_tier AS tier, COALESCE(extra_device_slots, 0) AS extra FROM local_subscription WHERE tenant_id = @TenantId",
            new { TenantId = tenantId }, cancellationToken: ct));

        var (pcLimit, mobileLimit) = GetLimitsForTier(tenant.tier);
        pcLimit += tenant.extra;
        mobileLimit += tenant.extra * 2;

        var counts = (await _db.QueryAsync<(string t, int c)>(new CommandDefinition(
            """
            SELECT device_type AS t, COUNT(*) AS c
            FROM tenant_devices
            WHERE tenant_id = @TenantId AND status = 'approved'
            GROUP BY device_type
            """,
            new { TenantId = tenantId }, cancellationToken: ct))).ToList();

        int pcUsed = counts.Where(x => x.t == "pc").Sum(x => x.c);
        int mobileUsed = counts.Where(x => x.t == "mobile" || x.t == "tablet").Sum(x => x.c);

        var type = string.IsNullOrWhiteSpace(req.DeviceType) ? "pc" : req.DeviceType.ToLowerInvariant();
        if (type is not ("pc" or "mobile" or "tablet")) type = "pc";

        // 한도 초과 체크
        if (type == "pc" && pcUsed >= pcLimit)
        {
            await LogDeniedAsync(tenantId, userId, ipAddress, null, "denied_limit", ct);
            return (false, $"PC 기기 한도 초과 ({pcUsed}/{pcLimit}대)", null);
        }
        if ((type == "mobile" || type == "tablet") && mobileUsed >= mobileLimit)
        {
            await LogDeniedAsync(tenantId, userId, ipAddress, null, "denied_limit", ct);
            return (false, $"모바일 기기 한도 초과 ({mobileUsed}/{mobileLimit}대)", null);
        }

        // 3) INSERT (MVP: 자동 승인)
        var deviceId = Guid.NewGuid().ToString();
        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO tenant_devices
              (device_id, tenant_id, user_id, device_type, device_name,
               fingerprint, ip_address, user_agent, status,
               registered_at, approved_by, approved_at, last_seen_at)
            VALUES
              (@Id, @TenantId, @UserId, @Type, @Name,
               @Fp, @Ip, @Ua, 'approved',
               NOW(), @UserId, NOW(), NOW())
            """,
            new
            {
                Id = deviceId,
                TenantId = tenantId,
                UserId = userId,
                Type = type,
                Name = string.IsNullOrWhiteSpace(req.DeviceName) ? null : req.DeviceName,
                Fp = req.Fingerprint,
                Ip = ipAddress,
                Ua = req.UserAgent
            }, cancellationToken: ct));

        await LogLoginAsync(tenantId, userId, ipAddress, deviceId, "success", ct);

        // 감사로그 — 메타만
        var afterJson = $"{{\"type\":\"{type}\",\"name\":\"{req.DeviceName ?? ""}\"}}";
        await _audit.LogAsync("register", "device", deviceId, afterJson: afterJson, ct: ct);

        return (true, "", deviceId);
    }

    // ── 기기 폐기 ──
    public async Task RevokeAsync(string deviceId, string tenantId, string userId, string? reason, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE tenant_devices
            SET status = 'revoked',
                revoked_at = NOW(),
                revoked_reason = @Reason
            WHERE device_id = @Id AND tenant_id = @TenantId
            """,
            new { Id = deviceId, TenantId = tenantId, Reason = reason }, cancellationToken: ct));

        await _audit.LogAsync("revoke", "device", deviceId, reason: reason, ct: ct);
    }

    // ── 미들웨어용: 기기 허용 여부 ──
    public async Task<bool> IsDeviceAllowedAsync(string deviceId, string tenantId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(deviceId)) return false;
        await EnsureOpenAsync(ct);
        var status = await _db.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT status FROM tenant_devices WHERE device_id = @Id AND tenant_id = @TenantId LIMIT 1",
            new { Id = deviceId, TenantId = tenantId }, cancellationToken: ct));
        return status == "approved";
    }

    // ── 내부 헬퍼 ──
    private static (int pc, int mobile) GetLimitsForTier(string? tier) => (tier ?? "basic").ToLowerInvariant() switch
    {
        "basic"   => (5, 3),
        "pro"     => (10, 8),
        "premium" => (100, 80),
        "trial"   => (10, 5),
        _         => (5, 3)
    };

    private async Task LogLoginAsync(string tenantId, string userId, string ip, string deviceId, string result, CancellationToken ct)
    {
        try
        {
            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO device_login_logs
                  (device_id, tenant_id, user_id, ip_address, login_at, login_result)
                VALUES
                  (@DeviceId, @TenantId, @UserId, @Ip, NOW(), @Result)
                """,
                new { DeviceId = deviceId, TenantId = tenantId, UserId = userId, Ip = ip, Result = result },
                cancellationToken: ct));
        }
        catch { /* 로그 실패는 주 로직 보호를 위해 무시 */ }
    }

    private async Task LogDeniedAsync(string tenantId, string userId, string ip, string? deviceId, string result, CancellationToken ct)
    {
        try
        {
            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO device_login_logs
                  (device_id, tenant_id, user_id, ip_address, login_at, login_result)
                VALUES
                  (@DeviceId, @TenantId, @UserId, @Ip, NOW(), @Result)
                """,
                new { DeviceId = deviceId, TenantId = tenantId, UserId = userId, Ip = ip, Result = result },
                cancellationToken: ct));
        }
        catch { /* 로그 실패는 주 로직 보호를 위해 무시 */ }
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open) return;
        if (_db is DbConnection dbc) { await dbc.OpenAsync(ct); return; }
        _db.Open();
    }
}
