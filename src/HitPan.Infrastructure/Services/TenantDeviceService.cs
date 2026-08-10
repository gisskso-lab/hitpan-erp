using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Device;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<TenantDeviceService> _logger;

    public TenantDeviceService(IDbConnection db, IAuditService audit, ILogger<TenantDeviceService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
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
    public async Task<(bool allowed, string reason, string? deviceId, bool newlyRegistered)> RegisterOrRefreshAsync(
        string tenantId,
        string userId,
        RegisterDeviceRequest req,
        string ipAddress,
        bool isMainPc = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Fingerprint))
        {
            // 지문 미지원 클라이언트 → 등록 스킵 (로그인은 허용)
            // ⚠️ 가드 절대 제거 금지 (작1 검증팀 발굴 2026-07-02): tenant_devices.fingerprint 는
            //   NOT NULL(hitpan_db_clean.sql). 지문 보강(2차)에서 "환경해시라 항상 값 있다" 가정하고
            //   이 스킵을 지우면, 해시 실패 시 빈 문자열이 아래 INSERT 로 흘러 NOT NULL 위반 500.
            //   빈값은 반드시 여기서 걸러 등록을 건너뛴다.
            return (true, "", null, false);
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
            // 폐기된 기기는 재사용 금지.
            //
            // 🔴 2026-08-10 [3-V] 되돌림 — 메인PC 자동 부활을 **철회**한다.
            //   직전 봉합은 "메인PC 면 폐기 상태를 되살린다" 였다. 그런데 메인PC 판정이
            //   고객사에서 항상 참이므로(위 한도 주석의 3가지 근거), 이 예외는
            //   **폐기한 기기를 전부 다음 로그인에 부활**시킨다.
            //   폐기는 DeviceController 가 주는 유일한 접근 차단 수단이다 —
            //   퇴사자 노트북·분실 태블릿까지 되살아나면 차단 기능 자체가 사라진다.
            //   ⇒ 종전대로 거부한다.
            //
            //   ⬜ 원래 D-5(메인PC 가 폐기됐으면 영구 잠김)는 아직 미해결로 남는다.
            //     복구 수단(관리자 해제 API 또는 본사 경유)이 필요하며 결재 사항이다.
            if (status == "revoked")
            {
                await LogDeniedAsync(tenantId, userId, ipAddress, id, "denied_revoked", ct);
                return (false, "폐기된 기기입니다. 관리자에게 문의하세요.", null, false);
            }

            // approved / pending → last_seen / ip / ua 갱신
            //
            // 🔴 2026-08-10 [4] D-4 봉합 (P0) — device_name 을 갱신 대상에 넣는다.
            //   종전 UPDATE 는 device_name 을 건드리지 않았다. device_name 이 쓰이는 곳은 INSERT 뿐이라
            //   **표식은 "그 기기가 처음 등록되는 순간" 에만 붙었다.**
            //   그런데 지문(device-fingerprint.js)은 origin 과 무관해서, 메인PC 가 과거에 터널 도메인으로
            //   접속한 적이 있으면 이미 같은 지문으로 등록돼 있다 — 그런 기기에는 **표식이 영원히 안 붙는다.**
            //   즉 T4 가 신규 설치 고객에게만 동작했다.
            //   ⇒ COALESCE 로 **넘어온 이름이 있을 때만** 덮는다. null 이면 기존 이름을 보존하므로
            //     클라이언트PC 의 기존 이름이 지워지는 사고는 없다.
            //
            await _db.ExecuteAsync(new CommandDefinition(
                """
                UPDATE tenant_devices
                SET last_seen_at = NOW(),
                    ip_address   = @Ip,
                    user_agent   = COALESCE(@Ua, user_agent),
                    device_name  = COALESCE(@Name, device_name)
                WHERE device_id = @Id
                """,
                new
                {
                    Id = id,
                    Ip = ipAddress,
                    Ua = req.UserAgent,
                    Name = string.IsNullOrWhiteSpace(req.DeviceName) ? null : req.DeviceName
                }, cancellationToken: ct));

            await LogLoginAsync(tenantId, userId, ipAddress, id, "success", ct);
            // 기존 기기 재접속 — 신규 아님(newlyRegistered=false)
            return (status == "approved", status == "approved" ? "" : "기기 승인 대기 중입니다.", id, false);
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
        //
        // 🔴 2026-08-10 [4] D-3 봉합 (P0 · 검증팀장 데이비드 박 적발) — 메인PC 는 한도로 막지 않는다.
        //
        //   ■ 무슨 일이 일어날 뻔했나
        //     메인PC 를 기기로 등록하게 되면서(작2 T3), 이미 한도를 꽉 채운 테넌트는
        //     **다음 로그인에서 메인PC 가 401 로 거부**된다. 그런데 그 PC 는 회사의 모든 자료를 가진 PC 다.
        //
        //   ■ 왜 P0 인가 — 고객이 스스로 풀 방법이 0개다
        //     · 메인PC 에서 기기 폐기 → 로그인이 막혀서 불가 (폐기 버튼이 로그인 뒤에 있다)
        //     · 다른 PC 에서 폐기   → 그 PC 도 한도 안에 있어야 하는데, 한도가 꽉 찼다는 건 여유가 없다는 뜻
        //     · 본사에 전화        → 본사 DB 는 다른 테이블이라 고객 로컬 tenant_devices 를 못 고친다
        //
        //   ■ 사장님 결재와의 관계 (범위를 넘지 않는다)
        //     결재는 *"메인PC가 슬롯기기를 소모해야 정상이지. 그래야 요금정책이 의미가 있지"* 였다.
        //     ⇒ **소모는 그대로 한다** — 아래 INSERT 로 등록되어 pcUsed 에 계속 집계된다.
        //       요금정책 정합성(basic 5대가 사실상 6대가 되는 문제)은 그대로 지켜진다.
        //     "슬롯을 쓴다" 와 "슬롯 때문에 잠긴다" 는 다른 사안이다. 앞은 결재 사항이고, 뒤는 사고다.
        //     ⇒ 한도를 넘겨도 **등록은 시키고 출입은 막지 않는다**(초과분은 목록에 그대로 보인다).
        //       고객은 로그인해 들어와서 필요 없는 기기를 스스로 정리할 수 있다 — 탈출구를 남긴다.
        //
        //   ⚠️ 클라이언트PC 는 종전대로 막는다. 막혀도 메인PC 로 가면 풀 수 있기 때문이다.
        // 🔴 2026-08-10 [3-V] 되돌림 — 한도 면제를 **철회**한다.
        //
        //   직전 봉합은 "메인PC 면 한도를 넘겨도 통과" 였다. 그런데 [3-V] 병렬검증이
        //   그 봉합이 딛고 선 전제를 무너뜨렸다 — **메인PC 판정이 고객사에서 항상 참이다.**
        //     · 터널은 히트판을 같은 PC 안에서 부른다(HitPan-Universal.iss:1134)
        //     · 히트판은 127.0.0.1 로만 귀를 연다(HitPan-Universal.iss:2359)
        //       ⇒ 도달한 요청은 100% loopback 이다
        //     · 사내 다른 PC 경로(web-server.ps1)는 X-Forwarded-For 를 붙이지 않는다
        //       ⇒ 프록시 헤더 검사로도 이 경로는 못 거른다
        //
        //   ⇒ 한도 면제를 두면 **모든 기기가 면제**를 받아 basic 5대 제한이 사라진다.
        //     슬롯을 정직하게 세라는 사장님 결재가 슬롯을 없애는 결과가 된다. 정반대다.
        //
        //   ⚠️ 되돌리면 검증팀이 지적한 D-3(메인PC 잠김 P0)이 되살아난다. 그러나 그것은
        //     **한도가 꽉 찬 테넌트에서만** 나고, 지금 봉합은 **모든 테넌트**의 과금을 무너뜨린다.
        //     둘 다 P0 지만 지금 상태가 더 나쁘다. 안전한 쪽으로 되돌린다.
        //
        //   ⬜ 진짜 해결은 판정 근거를 **접속 경로가 아닌 것**으로 바꾸는 것이다
        //     (설치 시점 식별자 / 테넌트당 1건 제한 등). 설계 변경이라 사장님·CTO 결재 사항이며
        //     `docs/검증/병렬이슈/20260810_병렬이슈_09_작2봉합_독립반증.md` 에 선택지가 있다.
        if (type == "pc" && pcUsed >= pcLimit)
        {
            await LogDeniedAsync(tenantId, userId, ipAddress, null, "denied_limit", ct);
            return (false, $"등록된 기기가 아닙니다. PC 기기 한도({pcLimit}대)를 초과했습니다. 기존 기기를 해제하거나 관리자에게 문의하세요.", null, false);
        }
        if ((type == "mobile" || type == "tablet") && mobileUsed >= mobileLimit)
        {
            await LogDeniedAsync(tenantId, userId, ipAddress, null, "denied_limit", ct);
            return (false, $"등록된 기기가 아닙니다. 모바일 기기 한도({mobileLimit}대)를 초과했습니다. 기존 기기를 해제하거나 관리자에게 문의하세요.", null, false);
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

        // 신규 기기 등록 성공 — newlyRegistered=true (작1 F3: 클라이언트가 첫 접속 안내 노출)
        return (true, "", deviceId, true);
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
