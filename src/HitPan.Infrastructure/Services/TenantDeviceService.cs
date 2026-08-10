using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Device;
using HitPan.Application.Interfaces;
// 기기 승인제 끄기 스위치 (20260811작1 (E)) — appsettings 에서 읽는다
using Microsoft.Extensions.Configuration;

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

    /// <summary>
    /// 기기 승인제 사용 여부 (20260811작1 (E), 사장님 지시 2026-08-11).
    ///
    /// //개발과정 속  테스트시 잠시 이 기능은 죽일 수 있음.//
    ///
    /// ■ 왜 스위치가 필요한가 — 사장님 지적
    ///   "개발하면서 슬롯을 다 써서 테스트도 못하는 상황이 올수 있음"
    ///   Sandbox 를 여러 번 띄우고 브라우저를 바꿔가며 테스트하면 슬롯이 금방 찬다.
    ///   그러면 **우리가 만든 기능 때문에 우리가 테스트를 못 한다.**
    ///   급할 때 코드를 뒤져 주석 처리하는 게 아니라 설정 한 줄로 끈다.
    ///
    /// ■ 기본값이 false 인 이유
    ///   켜는 순간 새 기기가 전부 승인 대기로 들어간다 = 실고객 영향.
    ///   **켜는 것은 사장님 별도 결재 사항**이다(작1 §7).
    ///   false 면 종전과 100% 동일하게 자동 승인된다 — 아무것도 안 바뀐다.
    ///
    /// ■ 끄는 법
    ///   appsettings.json → "DeviceApproval": { "Enabled": false }
    /// </summary>
    private readonly bool _approvalEnabled;

    public TenantDeviceService(IDbConnection db, IAuditService audit, IConfiguration? config = null)
    {
        _db = db;
        _audit = audit;
        // 설정이 없으면 꺼짐(false) — 안전측. 종전 동작 그대로다.
        _approvalEnabled = config?.GetValue<bool>("DeviceApproval:Enabled") ?? false;
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
                   d.last_seen_at  AS LastSeenAt,
                   d.is_main_pc    AS IsMainPc
            FROM tenant_devices d
            LEFT JOIN users u ON u.user_id = d.user_id
            WHERE d.tenant_id = @TenantId
            ORDER BY d.is_main_pc DESC, d.registered_at DESC
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
            if (status == "revoked")
            {
                await LogDeniedAsync(tenantId, userId, ipAddress, id, "denied_revoked", ct);
                return (false, "폐기된 기기입니다. 관리자에게 문의하세요.", null, false);
            }

            // approved / pending → last_seen / ip / ua / 이름 갱신
            //
            // 🔴 2026-08-10 [4] D-4 봉합 — device_name 을 갱신 대상에 넣는다.
            //   종전 UPDATE 는 device_name 을 건드리지 않았고, 이름이 쓰이는 곳은 INSERT 뿐이었다.
            //   ⇒ 이름은 "그 기기가 처음 등록되는 순간" 에만 붙었다. 그런데 지문은 접속 주소와
            //     무관하게 설계돼 있어(device-fingerprint.js), 이미 등록된 기기는 갱신 경로로만
            //     들어온다 — 그런 기기에는 이름이 **영원히 안 붙는다.**
            //   ⇒ COALESCE 로 **넘어온 이름이 있을 때만** 덮는다. null 이면 기존 이름을 보존한다.
            //
            // ⚠️ 예약된 사고: 고객이 목록에서 기기 이름을 직접 바꾸는 기능이 생기는 날,
            //   이 COALESCE 가 그 이름을 로그인마다 덮어쓴다. 그 기능의 작업지시서에
            //   이 문장이 반드시 실려야 한다. (지금은 이름 변경 입구가 0개라 발생하지 않는다)
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
            //
            // 🔴 (F) 기존 기기 = 인증된 것으로 인정 (사장님 결재 2026-08-11 "나 로 가")
            //   이미 approved 로 등록된 기기는 승인제를 켜도 **그대로 통과**한다.
            //   여기서 _approvalEnabled 를 보지 않는 것이 핵심이다 — 보게 만들면
            //   규칙을 켜는 순간 쓰던 사람이 전부 막힌다(2026-08-10 아침 4차 사고 계통).
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
        // 🔴 사장님 설계 정정 (20260811작1): 문구를 **"인증기기 한도초과. 관리자에게 문의하세요."** 로 통일한다.
        //   종전 문구("등록된 기기가 아닙니다… 기존 기기를 해제하거나…")는 직원에게 **직원이 할 수 없는 일**을
        //   시킨다. 기기 해제는 대표계정만 할 수 있다(DeviceController.Revoke → tenant_admin 만).
        //   직원은 그 문장을 읽고 할 수 있는 게 없다.
        //   ⇒ 직원에게는 "관리자에게 문의" 만 말하고, **파는 쪽 제안은 대표계정 화면**에서 뜬다.
        //     (작1 (C) — "비인증 PC에서 접속이 시도되었습니다. 슬롯 1개를 추가하시겠습니까?")
        //
        // ⚠️ allowed=false 이지만 **로그인 거부가 아니다.** 사장님: "일단 로그인, 접속까지는 가능하게 해."
        //   호출부가 이 값을 기기 상태로 실어 보내고, 화면이 메뉴를 잠근 채 안내문을 띄운다.
        const string LimitExceededMessage = "인증기기 한도초과. 관리자에게 문의하세요.";

        if (type == "pc" && pcUsed >= pcLimit)
        {
            await LogDeniedAsync(tenantId, userId, ipAddress, null, "denied_limit", ct);
            return (false, LimitExceededMessage, null, false);
        }
        if ((type == "mobile" || type == "tablet") && mobileUsed >= mobileLimit)
        {
            await LogDeniedAsync(tenantId, userId, ipAddress, null, "denied_limit", ct);
            return (false, LimitExceededMessage, null, false);
        }

        // 3) INSERT — 승인제가 켜져 있으면 'pending', 꺼져 있으면 종전대로 'approved'
        //
        // //개발과정 속  테스트시 잠시 이 기능은 죽일 수 있음.//
        //
        // ■ 사장님 설계 (20260811작1): "승인대기. 대표에게 기기승인의 권한을 주기"
        //   슬롯이 남든 없든 모든 기기는 대표계정을 거쳐 들어온다. 돈이 걸린 자원이라 문지기가 필요하다.
        //
        // ■ 스위치가 꺼져 있으면(_approvalEnabled=false, 기본값)
        //   종전과 100% 동일하게 즉시 approved 다. 아무것도 안 바뀐다.
        //   ⇒ 이것이 (F) "기존 기기는 인증된 것으로 인정" 의 실현이기도 하다.
        //     기본값이 꺼짐이므로 규칙을 켜기 전까지 아무도 안 막힌다.
        var newStatus = _approvalEnabled ? "pending" : "approved";
        var deviceId = Guid.NewGuid().ToString();
        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO tenant_devices
              (device_id, tenant_id, user_id, device_type, device_name,
               fingerprint, ip_address, user_agent, status,
               registered_at, approved_by, approved_at, last_seen_at)
            VALUES
              (@Id, @TenantId, @UserId, @Type, @Name,
               @Fp, @Ip, @Ua, @Status,
               NOW(), @ApprovedBy, @ApprovedAt, NOW())
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
                Ua = req.UserAgent,
                Status = newStatus,
                // 승인 대기 상태에서는 "누가 승인했다" 를 기록하지 않는다 — 아직 아무도 승인 안 했다.
                ApprovedBy = _approvalEnabled ? null : userId,
                ApprovedAt = _approvalEnabled ? (DateTime?)null : DateTime.Now
            }, cancellationToken: ct));

        await LogLoginAsync(tenantId, userId, ipAddress, deviceId, "success", ct);

        // 감사로그 — 메타만
        var afterJson = $"{{\"type\":\"{type}\",\"name\":\"{req.DeviceName ?? ""}\"}}";
        await _audit.LogAsync("register", "device", deviceId, afterJson: afterJson, ct: ct);

        // 신규 기기 등록 성공 — newlyRegistered=true (작1 F3: 클라이언트가 첫 접속 안내 노출)
        //
        // 승인제가 켜져 있으면 allowed=false 로 돌려준다(=아직 못 쓴다).
        //   ⚠️ 단 이것은 "로그인 거부" 가 아니다. 사장님 설계(20260811작1):
        //     "일단 로그인, 접속까지는 가능하게 해. 그러나, 인증기기 외 아무 메뉴도 열지 못하고"
        //   ⇒ 로그인은 통과시키고 화면에서 메뉴를 잠근다. 호출부(AuthController)가 이 값을
        //     기기 상태로 실어 보내고, 화면이 안내문 + [기기 인증하기] 를 띄운다.
        return _approvalEnabled
            ? (false, "기기 승인 대기 중입니다.", deviceId, true)
            : (true, "", deviceId, true);
    }

    // ── 기기 폐기 ──
    public async Task RevokeAsync(string deviceId, string tenantId, string userId, string? reason, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        // 🔴 메인PC(회사 서버)는 폐기하지 않는다 (20260810작3).
        //   폐기하면 그 PC 에서 로그인이 막히는데 되살리는 API 가 없다 —
        //   DeviceController 에는 목록·쿼터·폐기뿐이고 복구가 0건이다.
        //   그리고 그 PC 는 회사의 모든 자료를 가진 PC 다(DB_HOST=localhost).
        //   ⚠️ 화면에서도 버튼을 막지만(DeviceManagePage), 화면만으로는 가드가 아니다 —
        //     API 를 직접 부르면 통과하므로 여기서 막는다.
        var isMainPc = await _db.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT COALESCE(is_main_pc, 0) FROM tenant_devices WHERE device_id = @Id AND tenant_id = @TenantId LIMIT 1",
            new { Id = deviceId, TenantId = tenantId }, cancellationToken: ct));

        if (isMainPc)
        {
            throw new InvalidOperationException(
                "회사 서버는 해제할 수 없습니다. 자료를 보관하는 컴퓨터입니다.");
        }

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

    // ── 기기 승인 (대표계정) ── 20260811작1 (B)
    //   사장님 설계: "승인대기. 대표에게 기기승인의 권한을 주기"
    public async Task ApproveAsync(string deviceId, string tenantId, string approverUserId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        // 승인 대상 확인 — 남의 테넌트 기기를 승인할 수 없다(헌법 #2 격리).
        var target = await _db.QueryFirstOrDefaultAsync<(string status, string type)?>(new CommandDefinition(
            "SELECT status AS status, device_type AS type FROM tenant_devices WHERE device_id = @Id AND tenant_id = @TenantId",
            new { Id = deviceId, TenantId = tenantId }, cancellationToken: ct));

        if (target is null)
            throw new InvalidOperationException("기기를 찾을 수 없습니다.");

        var (curStatus, devType) = target.Value;
        if (curStatus == "approved") return;             // 이미 승인됨 — 두 번 눌러도 안전(멱등)
        if (curStatus == "revoked")
            throw new InvalidOperationException("폐기된 기기는 승인할 수 없습니다. 그 기기에서 다시 접속해 주세요.");

        // 🔴 승인 시점에 한도를 **다시** 본다.
        //   대기 목록에 3대가 쌓여 있고 남은 슬롯이 1대라면, 첫 승인은 되고 나머지는 막혀야 한다.
        //   등록 시점에만 검사하면 대기분이 한도를 넘겨 통과한다(슬롯 과금이 무너지는 자리).
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

        if (devType == "pc" && pcUsed >= pcLimit)
            throw new InvalidOperationException("인증기기 한도초과. 슬롯을 추가하거나 사용하지 않는 기기를 해제해 주세요.");
        if ((devType == "mobile" || devType == "tablet") && mobileUsed >= mobileLimit)
            throw new InvalidOperationException("인증기기 한도초과. 슬롯을 추가하거나 사용하지 않는 기기를 해제해 주세요.");

        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE tenant_devices
            SET status = 'approved',
                approved_by = @Approver,
                approved_at = NOW()
            WHERE device_id = @Id AND tenant_id = @TenantId
            """,
            new { Id = deviceId, TenantId = tenantId, Approver = approverUserId }, cancellationToken: ct));

        await _audit.LogAsync("approve", "device", deviceId, ct: ct);
    }

    // ── 기기 승인 거부 (대표계정) ── 20260811작1 (B)
    public async Task RejectAsync(string deviceId, string tenantId, string approverUserId, string? reason, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        // 대기 중인 기기만 거부한다. 이미 쓰고 있는(approved) 기기를 여기서 끊지 않는다 —
        // 그건 폐기(RevokeAsync)이고, 회사 서버 보호 가드가 그쪽에 있다.
        var affected = await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE tenant_devices
            SET status = 'revoked',
                revoked_at = NOW(),
                revoked_reason = @Reason
            WHERE device_id = @Id AND tenant_id = @TenantId AND status = 'pending'
            """,
            new { Id = deviceId, TenantId = tenantId, Reason = reason ?? "대표계정 승인 거부" }, cancellationToken: ct));

        if (affected == 0)
            throw new InvalidOperationException("승인 대기 중인 기기가 아닙니다.");

        await _audit.LogAsync("reject", "device", deviceId, reason: reason, ct: ct);
    }

    // ── 모바일기기 등록 QR 토큰 발급 (20260811작1 (D)) ──
    //   사장님 오더: "모바일 등록기기 버튼 클릭시 QR생성"
    public async Task<string> IssueMobileRegisterTokenAsync(string tenantId, string issuerUserId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        // 평문 토큰 — 이 값이 QR 에 담긴다. DB 에는 해시만 남기므로 여기서만 존재한다.
        var plain = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var hash = Sha256Hex(plain);

        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO device_register_tokens
              (token_id, tenant_id, token_hash, issued_by, issued_at, expires_at)
            VALUES
              (@Id, @TenantId, @Hash, @By, NOW(6), DATE_ADD(NOW(6), INTERVAL 10 MINUTE))
            """,
            new
            {
                Id = Guid.NewGuid().ToString(),
                TenantId = tenantId,
                Hash = hash,
                By = issuerUserId
            }, cancellationToken: ct));

        return plain;
    }

    // ── QR 토큰으로 모바일기기 등록 (20260811작1 (D)) ──
    //   QR 을 띄운 것 자체가 대표계정의 승인이다 → 별도 승인 단계 없이 approved 로 들어간다.
    public async Task<(bool ok, string message, string? deviceId)> RegisterMobileByTokenAsync(
        string token, string deviceName, string fingerprint, string ipAddress, string? userAgent,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(fingerprint))
            return (false, "등록 정보가 올바르지 않습니다.", null);

        await EnsureOpenAsync(ct);
        var hash = Sha256Hex(token);

        // 아직 안 쓰였고 만료 전인 토큰만 받는다.
        var row = await _db.QueryFirstOrDefaultAsync<(string tokenId, string tenantId, string? issuedBy)?>(
            new CommandDefinition(
                """
                SELECT token_id AS tokenId, tenant_id AS tenantId, issued_by AS issuedBy
                FROM device_register_tokens
                WHERE token_hash = @Hash AND used_at IS NULL AND expires_at > NOW(6)
                LIMIT 1
                """,
                new { Hash = hash }, cancellationToken: ct));

        if (row is null)
            return (false, "만료되었거나 이미 사용된 코드입니다. 다시 시도해 주세요.", null);

        var (tokenId, tenantId, issuedBy) = row.Value;

        // 같은 폰이 다시 찍은 경우 — 이미 등록돼 있으면 그대로 성공 처리(멱등).
        var existing = await _db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT device_id FROM tenant_devices WHERE tenant_id = @TenantId AND fingerprint = @Fp LIMIT 1",
            new { TenantId = tenantId, Fp = fingerprint }, cancellationToken: ct));

        if (!string.IsNullOrEmpty(existing))
        {
            await MarkTokenUsedAsync(tokenId, existing, ct);
            return (true, "이미 등록된 기기입니다.", existing);
        }

        // 모바일 한도 확인 — QR 로 들어와도 슬롯 규칙은 같다.
        var tenant = await _db.QueryFirstOrDefaultAsync<(string? tier, int extra)>(new CommandDefinition(
            "SELECT subscription_tier AS tier, COALESCE(extra_device_slots, 0) AS extra FROM local_subscription WHERE tenant_id = @TenantId",
            new { TenantId = tenantId }, cancellationToken: ct));

        var (_, mobileLimit) = GetLimitsForTier(tenant.tier);
        mobileLimit += tenant.extra * 2;

        var mobileUsed = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM tenant_devices
            WHERE tenant_id = @TenantId AND status = 'approved'
              AND device_type IN ('mobile','tablet')
            """,
            new { TenantId = tenantId }, cancellationToken: ct));

        if (mobileUsed >= mobileLimit)
            return (false, "인증기기 한도초과. 관리자에게 문의하세요.", null);

        var deviceId = Guid.NewGuid().ToString();
        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO tenant_devices
              (device_id, tenant_id, user_id, device_type, device_name,
               fingerprint, ip_address, user_agent, status,
               registered_at, approved_by, approved_at, last_seen_at)
            VALUES
              (@Id, @TenantId, NULL, 'mobile', @Name,
               @Fp, @Ip, @Ua, 'approved',
               NOW(6), @By, NOW(6), NOW(6))
            """,
            new
            {
                Id = deviceId,
                TenantId = tenantId,
                Name = string.IsNullOrWhiteSpace(deviceName) ? "모바일 기기" : deviceName,
                Fp = fingerprint,
                Ip = ipAddress,
                Ua = userAgent,
                By = issuedBy      // QR 을 띄운 대표계정이 곧 승인자다
            }, cancellationToken: ct));

        await MarkTokenUsedAsync(tokenId, deviceId, ct);
        await _audit.LogAsync("register", "device", deviceId, afterJson: "{\"type\":\"mobile\",\"via\":\"qr\"}", ct: ct);

        return (true, "모바일 기기가 등록되었습니다.", deviceId);
    }

    private async Task MarkTokenUsedAsync(string tokenId, string deviceId, CancellationToken ct)
    {
        await _db.ExecuteAsync(new CommandDefinition(
            "UPDATE device_register_tokens SET used_at = NOW(6), used_device_id = @Dev WHERE token_id = @Id",
            new { Id = tokenId, Dev = deviceId }, cancellationToken: ct));
    }

    private static string Sha256Hex(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
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
