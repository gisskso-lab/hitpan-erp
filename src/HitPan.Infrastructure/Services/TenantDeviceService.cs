using System.Data;
using System.Data.Common;
using Dapper;
// 🔴 20260816작2 — 등록 확인번호(대표가 눈으로 대조하는 4자리). 계산은 그 파일 한 곳에만 있다.
using HitPan.Application.Common;
using HitPan.Application.DTOs.Device;
using HitPan.Application.Interfaces;
// 기기 승인제 끄기 스위치 (20260811작1 (E)) — appsettings 에서 읽는다
using Microsoft.Extensions.Configuration;
// 🔴 20260816작1 (B-5) — 기준값을 못 읽은 사유를 **운영 로그에** 남긴다(헌법 #15).
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HitPan.Application.Services;

/// <summary>
/// 테넌트 기기 등록/조회/폐기 서비스.
/// - 히트판 과금 모델: 계정 무제한, 기기 수 제한(PC + 모바일).
///
/// 🔴 2026-08-15 20260815작3 P1·P2 — 계수와 한도를 <b>한 곳으로 모았다.</b>
///
///   [종전] 슬롯을 세는 SQL 이 이 파일에만 4곳(:97 · :245 · :410 · :597), 한도 계산이 또 4곳.
///     같은 일을 하는 코드가 여덟 자리에 흩어져 있었고 <b>모양도 서로 달랐다.</b>
///     ⇒ 요금이 틀렸을 때 <b>어디가 틀렸는지 못 찾는다.</b> 한 곳만 고치면 나머지가 옛 규칙으로 돈다.
///
///   [지금] <see cref="CountUsedSlotsAsync"/> 하나가 세고, <see cref="GetLimitsAsync"/> 하나가 한도를 만든다.
///     ⚠️ 모으면서 <b>동작은 한 줄도 바꾸지 않았다.</b> 호출부마다 비교 대상이 다른 것은 그대로 뒀다
///        (QR 은 여전히 모바일만 본다 — P0 실측 D-1).
///
/// - 티어별 기본 한도는 이제 <b>코드에 없다</b> — <c>device_slot_policy_settings</c>(DB-104)에서 읽는다.
///   헌법 #11(기준값은 어드민이 설정) · #21(appsettings 무접촉).
///   표가 비어 있으면 종전 숫자로 떨어진다(무회귀 안전망 — <see cref="FallbackLimits"/>).
///
/// - 추가 슬롯 1개 구매 = <b>PC +1 · 모바일 +1</b> (사장님 확정 2026-08-15: "추가슬롯 1+1당 1만원").
///   ⚠️ 종전 코드는 모바일에 +2 를 줬다. 설정으로 정정했다.
///
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

    /// <summary>
    /// 🔴 20260816작1 (B-5) — 기준값 표를 못 읽었을 때 <b>운영에 남는</b> 기록.
    /// </summary>
    /// <remarks>
    /// 종전엔 <c>Debug.WriteLine</c> 이었다. 그것은 <c>[Conditional("DEBUG")]</c> 이라
    /// <b>Release 빌드에서 호출 자체가 사라진다</b> — 고객 PC 에는 아무 기록도 안 남았다.
    /// <para>
    /// 🔴 <b>왜 이것이 중요한가</b>: 이 catch 가 삼키는 것은
    /// <b>"요금 한도를 설정에서 못 읽었다"</b> 는 신호다. 그 신호가 사라지면
    /// 표가 비어 있어도 아무도 모른 채 안전망 숫자로 조용히 돈다.
    /// <b>R-1(신규 설치에서 표가 영원히 빈 사고)을 눈에 안 띄게 만든 것이 바로 이 조항</b>이라,
    /// 검증팀이 둘을 <b>한 몸</b>으로 보라고 판정했다.
    /// </para>
    /// ⚠️ 로그를 안 넘겨도 죽지 않게 <c>NullLogger</c> 로 떨어뜨린다 — 이 서비스는
    /// 로그인 경로에서 불리므로 <b>로그 때문에 로그인이 막히면 안 된다.</b>
    /// </remarks>
    private readonly ILogger<TenantDeviceService> _logger;

    public TenantDeviceService(
        IDbConnection db,
        IAuditService audit,
        IConfiguration? config = null,
        ILogger<TenantDeviceService>? logger = null)
    {
        _db = db;
        _audit = audit;
        // 설정이 없으면 꺼짐(false) — 안전측. 종전 동작 그대로다.
        _approvalEnabled = config?.GetValue<bool>("DeviceApproval:Enabled") ?? false;
        _logger = logger ?? NullLogger<TenantDeviceService>.Instance;
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

        // 🔴 확인번호는 **계산값**이라 DB 에 없다 (20260816작2 · 사장님 결재).
        //   대표 화면에 *"{기기이름} (인증번호 4726) 가 등록을 요청합니다"* 로 뜨고,
        //   신청한 기기 화면에도 같은 번호가 떠서 대표가 **눈으로 대조**한다.
        //   ⚠️ 승인 대기 중인 기기만 채운다 — 이미 승인된 기기는 대조할 일이 없다.
        foreach (var r in rows)
        {
            if (r.Status == "pending")
                r.ConfirmCode = DeviceConfirmCode.From(r.DeviceId);
        }

        return rows;
    }

    // ── 쿼터 계산 (approved 기기 수 기반) ──
    public async Task<DeviceQuotaDto> GetQuotaAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        // 화면에 보여주기만 한다 — 여기서는 한도를 비교하지 않는다(P0 스냅샷 경우 6).
        var (pcLimit, mobileLimit) = await GetLimitsAsync(tenantId, ct);
        var (pcUsed, mobileUsed) = await CountUsedSlotsAsync(tenantId, ct);

        // 화면에 그대로 보여줄 값이라 요금제 이름은 저장된 원문을 쓴다
        // (설정 열쇠용 정규화값 NormalizeTier 와 다르다 — 'default' 를 고객에게 보이면 안 된다).
        // 🔴 2026-08-16 CR2-5 — 같은 행을 **두 번 읽던 것을 한 번으로** 합쳤다.
        //   요금제 이름과 추가슬롯 수는 `local_subscription` 의 **같은 한 행**에 있다.
        //   따로 읽으면 두 번째 조회 사이에 값이 바뀔 때 **서로 안 맞는 짝**이 나올 수 있고,
        //   무엇보다 화면 한 번 그리는 데 왕복이 늘어난다.
        //   ⚠️ 행이 없을 수 있다(부트스트랩 전) — 그때는 둘 다 기본값으로 떨어진다.
        var sub = await _db.QueryFirstOrDefaultAsync<(string? tier, int extra)?>(new CommandDefinition(
            """
            SELECT subscription_tier AS tier, COALESCE(extra_device_slots, 0) AS extra
            FROM local_subscription WHERE tenant_id = @TenantId
            """,
            new { TenantId = tenantId }, cancellationToken: ct));

        var tier = sub?.tier;
        var extra = sub?.extra ?? 0;

        return new DeviceQuotaDto
        {
            PcLimit = pcLimit,
            MobileLimit = mobileLimit,
            PcUsed = pcUsed,
            MobileUsed = mobileUsed,
            ExtraSlots = extra,
            SubscriptionTier = tier ?? "basic"
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

        // 1) 기존 기기가 있는지 확인 — 🔴 **찾는 순서를 뒤집었다** (20260816작2 · 명세서 §4-3)
        //
        //   [종전] 지문 하나로만 찾았다.
        //   [무엇이 났나] 지문은 브라우저가 바뀌면 달라진다. `device-fingerprint.js` 의
        //     `_envSeed()` 가 씨앗에 **userAgent 를 포함**하기 때문이다(:48).
        //     ⇒ 같은 PC 인데 Edge 로 들어오면 A, Chrome 으로 들어오면 B ⇒ **서로 다른 기기**로
        //       잡혀 슬롯을 두 번 먹었다. 사장님이 1.2.81 백지 실측에서 보신 바로 그 증상이다.
        //     ⇒ 메인PC 는 더 심하다 — 서버가 만드는 `MAINPC-…` 와 브라우저가 만드는 `HFPv2-…` 가
        //       애초에 **다른 산식**이라 같은 컴퓨터가 반드시 두 줄이 된다(DB-103 진단).
        //
        //   [고침] **장비넘버(device_id)를 1순위 열쇠로 둔다.** 지문은 옛 기기 호환용 2순위다.
        //     장비넘버는 서버가 발급해 기기가 보관하는 값이라 **브라우저가 바뀌어도 안 바뀐다.**
        //
        //   ⚠️ 지문 조회를 **없애지 않는다**(헌법 #37 · #1). 이미 등록된 고객 기기는 전부
        //     지문으로만 찾을 수 있다. 지우는 순간 쓰던 사람이 전부 새 기기가 되어 슬롯을 다시 먹는다.
        //
        //   ⚠️ 장비넘버는 **같은 테넌트 안에서만** 인정한다. 남의 회사 번호를 들고 와도
        //     tenant_id 가 다르면 안 잡힌다(헌법 #2 — tenant_id 는 JWT 클레임에서만 온다).
        // 🔴 2026-08-18 20260818작2 (2-1) — `device_type` 을 **함께 읽는다.**
        //   종류 변경을 방향별로 가르려면 **지금 무슨 칸에 있는지**를 알아야 한다.
        //   종전엔 안 읽어서 "바뀌는가" 만 알았고 "어느 쪽으로 바뀌는가" 를 몰랐다.
        (string id, string status, bool isMainPc, string curType)? existing = null;

        if (!string.IsNullOrWhiteSpace(req.DeviceId))
        {
            existing = await _db.QueryFirstOrDefaultAsync<(string id, string status, bool isMainPc, string curType)?>(new CommandDefinition(
                """
                SELECT device_id AS id, status AS status, COALESCE(is_main_pc, 0) AS isMainPc,
                       device_type AS curType
                FROM tenant_devices
                WHERE tenant_id = @TenantId AND device_id = @DeviceId
                LIMIT 1
                """,
                new { TenantId = tenantId, DeviceId = req.DeviceId }, cancellationToken: ct));
        }

        // 2순위 — 옛 기기(장비넘버를 아직 못 받은 기기)는 지문으로 찾는다.
        existing ??= await _db.QueryFirstOrDefaultAsync<(string id, string status, bool isMainPc, string curType)?>(new CommandDefinition(
            """
            SELECT device_id AS id, status AS status, COALESCE(is_main_pc, 0) AS isMainPc
            FROM tenant_devices
            WHERE tenant_id = @TenantId AND fingerprint = @Fp
            LIMIT 1
            """,
            new { TenantId = tenantId, Fp = req.Fingerprint }, cancellationToken: ct));

        if (existing is not null)
        {
            var (id, status, isMainPc, curType) = existing.Value;

            // ══════════════════════════════════════════════════════════════
            // 🔴 2026-08-18 20260818작4 — **서버가 도는 그 컴퓨터의 화면은 메인PC 다.**
            //   (사장님 실측: *"모바일·외부 클라이언트는 봉합됨. 하지만 메인pc도 막힘"*)
            //
            //   [무엇이 났나] 메인PC 는 **반드시 두 줄**이 된다:
            //     · 서버 줄 — 지문 `MAINPC-…`(컴퓨터 이름), `is_main_pc=1`, `approved`
            //     · 화면 줄 — 지문 `HFPv2-…`(userAgent), `is_main_pc=0`, `pending`
            //     두 지문은 **서로 만들 수 없다** — MainPcRegistrationService 가 일부러 갈라 놨다
            //     (*"네임스페이스가 겹치면 안 된다"*). 위 :197 주석도 *"반드시 두 줄이 된다"* 고
            //     이미 진단해 뒀다.
            //
            //   🔴 [왜 8/16 봉합이 이걸 못 막았나] 그때 장비넘버를 1순위 열쇠로 올린 것은
            //     **브라우저끼리 갈리는 것**(Edge↔Chrome)을 막았을 뿐이다.
            //     `is_main_pc` 표식이 **서버 지문 줄에만** 붙는다는 사실은 그대로 뒀다.
            //     ⇒ 사장님은 **그 컴퓨터에 앉아 계신데** 화면 줄이 승인 대기라 막혔고,
            //       승인 화면에 들어갈 수 있는 유일한 사람이 **자기가 갇혔다.**
            //       8/16 P0(커밋 30e3873)·8/11 revoked 구제와 **같은 계통의 세 번째 재발**이다.
            //
            //   [고침] 그 컴퓨터에서 직접 연 화면이면 **그 줄을 메인PC 로 인정한다.**
            //     "그 컴퓨터에서 히트판이 돈다" 는 사실 자체가 인증이다 — 8/11 부터 지켜 온 축이다.
            //
            //   🔴 [왜 새 슬롯이 안 새는가] **줄을 만들지 않는다. 있는 줄에 표식만 옮긴다.**
            //     그리고 옮기기 전에 **옛 서버 줄을 내린다**(아래 ①) — 그래서 `is_main_pc=1` 은
            //     언제나 한 줄뿐이다. 슬롯 계수는 `status='approved'` 로 세므로
            //     화면 줄이 승인되는 만큼 **서버 줄이 통계에서 빠지지 않는다** ⇒ 아래 ①이 그것도 정리한다.
            //
            //   ⚠️ `IsLocalConsole` 은 **서버가 채운 값**이다(AuthController). 클라이언트가 못 정한다.
            //     터널을 지나온 접속은 헤더로 배제되므로 **바깥에서는 절대 참이 될 수 없다.**
            //     ⚠️ 이 조건을 `req` 가 아닌 다른 데서 받게 바꾸면 **아무나 메인PC 를 자칭한다.**
            // ══════════════════════════════════════════════════════════════
            if (req.IsLocalConsole && !isMainPc)
            {
                // ① 옛 서버 줄을 내린다 — 표식도, 슬롯도 한 줄만 남긴다.
                //   ⚠️ `revoked` 로 두는 이유: 지우면 감사 기록이 사라진다(헌법 #1 — 덮어쓰기 금지).
                //     폐기 상태는 슬롯 계수에서 빠지므로 요금이 이중으로 잡히지 않는다.
                var demoted = await _db.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE tenant_devices
                    SET is_main_pc     = 0,
                        status         = 'revoked',
                        revoked_at     = NOW(6),
                        revoked_reason = '메인PC 표식을 실제 사용 화면으로 옮김 (20260818작4)'
                    WHERE tenant_id = @TenantId AND is_main_pc = 1 AND device_id <> @Id
                    """,
                    new { TenantId = tenantId, Id = id }, cancellationToken: ct));

                // ② 지금 쓰는 그 줄을 메인PC 로 세운다.
                await _db.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE tenant_devices
                    SET is_main_pc     = 1,
                        status         = 'approved',
                        approved_at    = COALESCE(approved_at, NOW(6)),
                        revoked_at     = NULL,
                        revoked_reason = NULL
                    WHERE device_id = @Id AND tenant_id = @TenantId
                    """,
                    new { Id = id, TenantId = tenantId }, cancellationToken: ct));

                await _audit.LogAsync("device_mainpc_adopt", "device", id,
                    reason: $"서버가 도는 컴퓨터의 화면을 메인PC 로 인정 (옛 서버 줄 {demoted}건 내림)", ct: ct);

                _logger.LogWarning(
                    "[TenantDeviceService] 메인PC 표식을 실제 사용 화면으로 옮겼다 — "
                    + "서버 지문 줄과 브라우저 지문 줄이 갈려 있었다. "
                    + "device={DeviceId} tenant={TenantId} 내린옛줄={Demoted}",
                    id, tenantId, demoted);

                isMainPc = true;
                status = "approved";
            }

            // 🔴 2026-08-11 사장님 실측 적발 — "메인PC가 폐기되는게 말이되?"
            //
            //   [무엇이 났나] 메인PC(회사 서버)가 `revoked` 상태가 되자 **로그인 자체가 막혔다.**
            //     그 컴퓨터는 자료가 들어 있는 그 자리이고, 거기서 막히면 대표계정이
            //     등록기기 화면에 들어가 폐기를 되돌릴 수도 없다 — **스스로 못 빠져나온다.**
            //
            //   [왜 생겼나] 화면에는 자물쇠를 달아 메인PC 폐기 버튼을 막아뒀다(:340).
            //     그런데 그건 **앞으로 폐기되는 것**을 막을 뿐, **이미 revoked 인 기록**은 손대지 못한다.
            //     ⇒ 막는 자리를 화면에만 두고 **로그인 검사에는 두지 않은 것**이 진범이다.
            //
            //   [고침] 메인PC 는 폐기 상태여도 로그인을 막지 않는다.
            //     "그 컴퓨터에서 히트판이 돈다" 는 사실 자체가 인증이므로, 폐기라는 표식이
            //     그것을 뒤집을 수 없다.
            //
            //   🔴 2026-08-15 20260815작3 P1 — **이 자리 주석이 틀려 있었다. 정정한다.**
            //
            //     [틀린 문장] *"메인PC 는 슬롯을 세지 않고 한도에도 안 걸리는 특별한 자리다"*
            //
            //     [실제] 메인PC 는 **슬롯을 1대로 센다.** 사장님 확정 — *"기기 1대 = 슬롯 1개, 메인PC 포함"*.
            //       계수 SQL 어디에도 `is_main_pc` 를 빼는 절이 없고(CountUsedSlotsAsync),
            //       메인PC 행은 `status='approved'` · `device_type='pc'` 이므로 정상적으로 잡힌다.
            //       ⇒ **코드가 맞고 주석이 틀렸다.**
            //
            //     [절반만 맞았던 부분] 메인PC 가 **자기를 등록할 때**는 한도를 보지 않는다
            //       (MainPcRegistrationService — 한도가 찬 회사에서도 메인PC 표식은 붙어야
            //        CS 가 "그 컴퓨터가 본체입니다" 를 찾을 수 있기 때문이다).
            //       그러나 **등록된 뒤에는 다른 기기들의 한도 계산에 1대로 포함된다.**
            //
            //     🔴 이 주석을 읽고 계수 SQL 에 `AND is_main_pc = 0` 을 넣으면
            //       **적게 세어 요금이 샌다.** 넣지 마라(P0 실측 D-3).
            //
            //   ⚠️ 일반 기기는 종전대로 막는다 — 폐기의 의미가 사라지면 안 된다.
            //
            // ══════════════════════════════════════════════════════════════
            // 🔴 2026-08-20 20260820작3 (사장님 실측 오더) — **막는 방식을 바꾼다. 막는 사실은 그대로다.**
            //
            //   사장님 원문: *"'폐기된 기기 입니다. 관리자에게 문의하세요' 가 아닌,
            //                  **기기 등록 전 상태로 회귀**하도록"*
            //
            //   [무엇이 문제였나] 종전엔 **로그인 자체를 거부**했다(`deviceId: null` ⇒ AuthController 401).
            //     ⇒ 그 기기는 **관문에 도달할 길이 없다.** 대표가 실수로 폐기하면 그 기기를
            //       되살릴 방법이 화면에 0개다(등록기기관리에는 폐기 행에 버튼이 안 그려진다).
            //     ⇒ 거절(rejected)에는 *"첫 화면 회귀"* 갈래를 만들어 줬는데(1-4),
            //       폐기에는 안 만들어 **사실상 되돌릴 수 없는 삭제**로 동작했다.
            //
            //   [고침] **대기(pending)로 되돌린다.** 그 기기는 "등록 전 상태" 화면으로 회귀한다.
            //     🔴 **자동 승인이 아니다.** 문지기는 그대로 대표다 — 대기 줄에 다시 설 뿐이고,
            //       대표가 [승인]을 눌러야 쓸 수 있다. 폐기했다는 판단은 **승인 화면에서 다시 묻는다.**
            //     🔴 **옛 열쇠는 되살아나지 않는다** — 폐기 때 `auth_key_hash` 를 이미 지웠고
            //       (RevokeAsync · G-DP1) 이 갈래는 그것을 **건드리지 않는다.**
            //     ⚠️ 감사기록에 *폐기였다가 다시 신청함* 을 남긴다 — 폐기 이력이 사라지지 않는다.
            //
            //   ⚠️ **조건문은 그대로 둔다**(`&& !isMainPc`) — 이 조건은 *"메인PC 는 여기 안 걸리고
            //     통과"* 라는 8/11 P0 구제(위 주석)를 **겸하는 자리**다. 지우면 그 사고가 재발한다.
            // ══════════════════════════════════════════════════════════════
            if (status == "revoked" && !isMainPc)
            {
                await _db.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE tenant_devices
                    SET status = 'pending',
                        revoked_at = NULL,
                        revoked_reason = NULL,
                        last_seen_at = NOW(6),
                        ip_address = @Ip
                    WHERE device_id = @Id AND tenant_id = @TenantId
                    """,
                    new { Id = id, TenantId = tenantId, Ip = ipAddress }, cancellationToken: ct));

                await _audit.LogAsync("device_reapply_after_revoke", "device", id,
                    reason: "폐기된 기기의 재신청 — 등록 전 상태로 회귀 (20260820작3)", ct: ct);

                // 🔴 사장님 문구 (2026-08-20): *"폐기된 기기입니다. 관리자의 재승인이 필요합니다."*
                //   ⚠️ 폐기였다는 사실을 **숨기지 않는다** — 그냥 대기와 구분되어야 직원이 상황을 안다.
                //     그러면서도 갈 곳이 있다(승인 요청). 사장님: *"어차피 관리자 재승인 없이는 등록못함"*
                return (false, "폐기된 기기입니다. 관리자의 재승인이 필요합니다.", id, false);
            }

            // ══════════════════════════════════════════════════════════════
            // 🔴 2026-08-18 20260818작2 (DP-2 ②) — **메인PC 는 `rejected` 에서도 빠져나온다.**
            //   (검증팀 [4] 적발 · docs/검증/병렬이슈/20260818_검증팀_DP1_DP2_폐기키생존_메인PC반려.md)
            //
            //   [무엇이 문제였나] 위 구제책은 **`"revoked"` 라는 문자열 하나**에 걸려 있었다.
            //     작1 이 **정당한 이유로** `rejected` 라는 새 상태를 만들자,
            //     구제책이 **조용히 그 상태를 안 덮게** 됐다. 아무도 그것을 못 봤다.
            //     ⇒ 🔴 **새 상태를 만들 때는 그 상태를 이름으로 검사하는 곳을 전부 찾아야 한다**(헌법 #12).
            //       `grep -n '"revoked"'` 한 번이면 나왔다.
            //
            //   [왜 pending 이 아니라 approved 인가] 아래 1-4 갈래는 거절당한 기기를 `pending` 으로
            //     되돌려 **대표의 판단을 다시 받게** 한다. 그것은 일반 기기에 옳다.
            //     🔴 그러나 메인PC 에 그러면 **아무것도 안 고친 것**이다 —
            //       승인해 줄 수 있는 유일한 사람이 **자기가 승인 대기에 갇혀** 승인 화면에 못 들어간다.
            //       (8/16 P0 커밋 30e3873 이 정확히 그 사고였다)
            //     ⇒ 메인PC 는 **되돌려 세운다.** "그 컴퓨터에서 히트판이 돈다" 는 사실 자체가 인증이다.
            //
            //   ⚠️ 이 갈래는 **메인PC 한 줄에만** 해당한다(`isMainPc`). 일반 기기는 아래 1-4 로 간다.
            // ══════════════════════════════════════════════════════════════
            if (status == "rejected" && isMainPc)
            {
                await _db.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE tenant_devices
                    SET status = 'approved',
                        revoked_at = NULL,
                        revoked_reason = NULL,
                        last_seen_at = NOW(6),
                        ip_address = @Ip
                    WHERE device_id = @Id AND tenant_id = @TenantId
                    """,
                    new { Id = id, TenantId = tenantId, Ip = ipAddress }, cancellationToken: ct));

                await _audit.LogAsync("device_mainpc_selfrescue", "device", id,
                    reason: "메인PC 가 반려 상태에서 스스로 회복 (DP-2)", ct: ct);

                _logger.LogWarning(
                    "[TenantDeviceService] 메인PC 가 반려(rejected) 상태였다 — 스스로 회복시켰다. "
                    + "누군가 회사 서버를 반려했다는 뜻이다. device={DeviceId} tenant={TenantId}",
                    id, tenantId);

                status = "approved";
            }

            // 🔴 2026-08-18 20260818작1 (1-4) — **거절당한 기기는 다시 신청할 수 있다.**
            //
            //   사장님 오더: *"거절하면 첫 화면 회귀"*
            //
            //   [왜 여기인가] `rejected` 를 `revoked` 에서 가르기만 하고 이 자리를 안 만들면
            //     **아무것도 안 고친 것**이다. 칸 이름만 바뀌고 직원은 여전히 갇힌다 —
            //     거절당한 기기가 다시 와도 `rejected` 인 채로 머물러 **대표의 승인 대기 목록에
            //     다시 뜨지 않는다.** 대표는 승인할 기회조차 없다.
            //
            //   [고침] 다시 온 그 기기를 **대기 줄에 다시 세운다.** 대표가 다시 판단한다.
            //     ⚠️ 자동 승인이 아니다 — `pending` 으로 돌릴 뿐이다. 문지기는 그대로 대표다.
            //
            //   ⚠️ `revoked` 는 여기로 오지 않는다(위에서 이미 갈렸다). 폐기는 폐기로 남는다.
            if (status == "rejected")
            {
                await _db.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE tenant_devices
                    SET status = 'pending',
                        revoked_at = NULL,
                        revoked_reason = NULL,
                        last_seen_at = NOW(6),
                        ip_address = @Ip
                    WHERE device_id = @Id AND tenant_id = @TenantId
                    """,
                    new { Id = id, TenantId = tenantId, Ip = ipAddress }, cancellationToken: ct));

                await _audit.LogAsync("device_reapply", "device", id,
                    reason: "거절된 기기의 재신청", ct: ct);

                return (false, "기기 승인 대기 중입니다.", id, false);
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
            // 🔴 2026-08-11 20260811작2 봉합 — device_type 도 갱신 대상에 넣는다 (사장님 지시).
            //   *"업데이트 이후 막히지 않고 **정확하게 모바일이냐 PC냐 잡으면 됨**"*
            //
            //   [왜 필요한가] 종전 UPDATE 는 종류를 건드리지 않았다. 그래서 아이패드를 컴퓨터로
            //     잘못 판정하던 시절에 등록된 기기는, 판정을 고친 뒤에도 **영원히 컴퓨터 칸을 먹었다.**
            //     히트판은 기기 수로 요금을 매기므로 이건 고객이 계속 잘못된 자리를 잃는다는 뜻이다.
            //     ⇒ 고객이 업데이트를 받으면 **그 다음 접속에 스스로 제자리를 찾아가야** 한다.
            //
            //   [🔴 막히지 않는다는 보장] 이 자리는 **이미 등록된 기기**만 지나간다(위 existing 분기).
            //     종류가 바뀌어도 한도 검사(아래 2번)에 **다시 들어가지 않는다.**
            //     ⇒ 휴대기기 칸이 꽉 찬 상태에서 아이패드가 컴퓨터→휴대기기로 옮겨와도
            //       **그 사람이 쫓겨나지 않는다.** 칸 수를 잠깐 넘길 수는 있으나, 쓰던 사람을
            //       막지 않는 쪽을 택한다 (2026-08-10 아침 4차 사고 계통 — 규칙을 켜서 쓰던
            //       사람이 막히는 일을 두 번 만들지 않는다).
            //
            //   ⚠️ COALESCE 인 이유: 지문만 보내고 종류를 못 보내는 옛 화면이 있으면
            //     기존 값을 지우지 않는다. 넘어온 값이 있을 때만 고친다.
            //
            // ══════════════════════════════════════════════════════════════
            // 🔴 2026-08-18 20260818작2 (2-1) — **위 주석을 지우지 않고 갱신한다.**
            //
            //   [위 문장은 절반만 맞았다] *"종류가 바뀌어도 한도 검사에 다시 들어가지 않는다"* 는
            //     **버그가 아니라 결정**이었다(8/10 사고 뒤 쓰던 사람을 보호하려고 일부러 그랬다).
            //     🔴 그런데 그 결정이 **한쪽 방향에서만 옳았다.**
            //
            //   | 방향 | 뜻 | 8/10 의도 | 요금 |
            //   |---|---|---|---|
            //   | pc → mobile | 아이패드가 제자리를 찾아감 | ✅ 이게 그 경우다 | 위험 없음 |
            //   | mobile → pc | 폰이 컴퓨터로 승격 | ❌ 무관 | 🔴 구멍 |
            //
            //   [무엇이 새고 있었나] 싼 칸(mobile)으로 등록해 두고 다음 접속에 pc 를 신고하면
            //     **컴퓨터 한도가 0이어도 컴퓨터 칸이 무제한**으로 늘었다. 검사가 없으니까.
            //
            //   🔴 [고침 — 가장 오해하기 쉬운 줄이다] `mobile → pc` 승격은 **한도를 다시 본다.**
            //     그러나 **초과해도 막지 않는다. 그냥 안 바꾼다.**
            //     ⇒ 그 사람은 **계속 휴대기기 칸으로 쓴다** — 불편함 0, 요금 구멍 0.
            //     ⚠️ 이것을 *"막는다"* 로 구현하면 **8/10 사고 그 자체**다. 절대 그러지 마라.
            //
            //   ⚠️ `pc → mobile` 은 **종전 그대로 무검사**다. 그쪽은 요금 위험이 없고,
            //     검사를 넣으면 아이패드가 제자리를 못 찾아 8/10 사고가 재발한다.
            //
            //   🔴 판정값 자체도 서버가 다시 본다(V-05) — 아래 ResolveDeviceType 참조.
            //     클라이언트 신고만 믿으면 승격을 막아도 **최초 등록에서 그냥 새어 나간다.**
            // ══════════════════════════════════════════════════════════════
            var normalizedType = ResolveDeviceType(req.DeviceType, req.UserAgent);

            // 🔴 승격(휴대기기 → 컴퓨터)일 때만 한도를 다시 본다.
            //   ⚠️ 자기 자신이 지금 휴대기기 칸에 세어지고 있으므로, 컴퓨터 칸만 보면 된다.
            var isPromotionToPc = normalizedType == "pc" && curType != "pc";

            if (isPromotionToPc)
            {
                var (promoPcLimit, _) = await GetLimitsAsync(tenantId, ct);
                var (promoPcUsed, _) = await CountUsedSlotsAsync(tenantId, ct);

                if (promoPcUsed >= promoPcLimit)
                {
                    // 🔴 **막지 않는다. 안 바꿀 뿐이다.** 그 기기는 종전 칸으로 계속 쓴다.
                    //   ⇒ 아래 UPDATE 에 null 을 주면 COALESCE 가 기존 값을 보존한다.
                    //   ⚠️ return 하지 않는다 — return 하면 그것이 곧 "막는다" 이고 8/10 사고다.
                    normalizedType = null;

                    _logger.LogInformation(
                        "[TenantDeviceService] 기기 종류 승격을 보류했다 — 컴퓨터 칸이 찼다. "
                        + "그 기기는 종전 칸({CurType})으로 계속 쓴다(막지 않는다). "
                        + "device={DeviceId} pcUsed={PcUsed} pcLimit={PcLimit}",
                        curType, id, promoPcUsed, promoPcLimit);
                }
            }

            // 🔴 2026-08-20 20260820작2 ([3-V] 실재 판정 ②③) — **회사서버 줄의 정체는 서버가 정한다.**
            //
            //   [무엇이 문제였나] 이 갱신은 장비넘버 1순위 대조다. 그런데 관문의 [회사서버 컴퓨터]
            //     합류(2-1)로 **브라우저가 서버 줄의 번호를 신분으로 갖게 됐다** ⇒ 그 브라우저의
            //     매 로그인이 서버 줄의 이름·UA·IP 를 브라우저 것으로 덮는다 — CS 가 본체를 못 찾는다
            //     (DeviceListDto.IsMainPc 의 존재 이유가 그 식별이다). 대표 **폰**이 합류하면
            //     COALESCE 가 종류를 mobile 로 바꿔 **요금 칸까지 이동**한다(pc 계수 -1).
            //
            //   [고침] is_main_pc=1 줄은 last_seen_at 만 갱신한다. 이름·종류·UA·IP 는
            //     MainPcRegistrationService 가 심은 값을 보존한다.
            //   ⚠️ 로컬 콘솔(지문 통일 8/18작4)의 갱신도 같은 줄을 지나므로 함께 보존된다 —
            //     서버 줄 이름이 브라우저 이름으로 바뀌던 종전 동작도 이것으로 멎는다(의도).
            var preserveMainPcIdentity = isMainPc;

            await _db.ExecuteAsync(new CommandDefinition(
                """
                UPDATE tenant_devices
                SET last_seen_at = NOW(),
                    ip_address   = COALESCE(@Ip, ip_address),
                    user_agent   = COALESCE(@Ua, user_agent),
                    device_name  = COALESCE(@Name, device_name),
                    device_type  = COALESCE(@Type, device_type)
                WHERE device_id = @Id
                """,
                new
                {
                    Id = id,
                    Ip = preserveMainPcIdentity ? null : ipAddress,
                    Ua = preserveMainPcIdentity ? null : req.UserAgent,
                    Name = preserveMainPcIdentity || string.IsNullOrWhiteSpace(req.DeviceName) ? null : req.DeviceName,
                    Type = preserveMainPcIdentity ? null : normalizedType
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
        //   🔴 20260815작3 P1 — 계수와 한도를 단일 메서드로 모았다.
        var (pcLimit, mobileLimit) = await GetLimitsAsync(tenantId, ct);
        var (pcUsed, mobileUsed) = await CountUsedSlotsAsync(tenantId, ct);

        // 🔴 20260811작2 — 판정을 NormalizeDeviceType 한 곳으로 모았다.
        //   종전엔 이 자리에만 있었고, 갱신 경로(위)는 종류를 아예 안 봤다.
        //   두 경로가 각자 판단하면 한쪽만 고쳐지는 사고가 난다.
        //
        // 🔴 20260815작3 P1 (I-6) — `?? "pc"` 폴백을 없앴다.
        //
        //   [무엇이 문제였나] 종류를 안 보내는 클라이언트가 오면 무조건 `pc`(비싼 칸)로 갔다.
        //     NormalizeDeviceType 은 이미 *"모르는 값은 휴대기기로 본다"* 는 원칙을 갖고 있는데
        //     (:724 — 애매하면 고객에게 유리한 쪽), 그 원칙이 **이 경로에서는 도달하지 못했다.**
        //     폴백이 먼저 채워 버렸기 때문이다.
        //
        //   [왜 mobile 인가] 컴퓨터 칸이 더 비싸다. 판정이 애매할 때 컴퓨터로 세면
        //     **고객이 쓰지도 않은 자리에 돈을 낸다.** 반대로 세면 우리가 조금 손해 볼 뿐이다.
        //
        //   ⚠️ 폴백이 **두 곳**이었다 — 여기와 AuthController.cs:93. 한 곳만 고치면
        //     아무것도 안 바뀐다(P0 실측 D-9). 두 곳을 같이 고쳤다.
        //
        // 🔴 2026-08-16 CR2-2 최종 판정 — **이 폴백은 결함이 아니다. 그대로 둔다.**
        //
        //   판정이 **네 번 엇갈린 자리**다(병렬검증 "한 곳만" ↔ 개발팀 "4곳" ↔ 검증팀 "개발팀 승"
        //   ↔ 코드리뷰 2회차 "다시 문제"). 말로 갈리지 않아 **코드로 갈랐다.**
        //
        //   [코드리뷰 2회차 주장] *getDeviceType 이 예외를 던지면 진짜 PC 가 모바일 칸으로 간다.*
        //   [실측 결과] **그 경로는 존재하지 않는다.** 세 겹이 막는다:
        //     ① 클라이언트 getDeviceType 은 자체 catch 로 'mobile' **문자열**을 돌려준다
        //        (device-fingerprint.js) — 예외가 나도 **빈 값이 오지 않는다.**
        //     ② NormalizeDeviceType 이 null 을 주는 경우는 **값을 아예 안 보냈을 때뿐**이고
        //        (IsNullOrWhiteSpace), 모르는 값은 이미 안에서 'mobile' 로 간다(:948·:957).
        //     ③ 🔴 결정적 — 값이 비는 유일한 상황은 AuthService 의 try 가 통째로 깨질 때인데,
        //        그 try 의 **첫 줄이 getFingerprint** 다. 거기서 던지면 지문도 null 이 되고,
        //        AuthController 는 `IsNullOrEmpty(DeviceFingerprint)` 로 **기기 등록을 통째로 건너뛴다.**
        //        ⇒ 이 줄에 **도달하지 못한다.**
        //
        //   ⇒ 폴백을 'pc' 로 되돌리면 오히려 P0 다 — 종류를 모를 때 비싼 칸으로 세어
        //     **고객이 쓰지도 않은 자리에 돈을 낸다**(이 차수가 존재하는 이유 그 자체).
        // 🔴 2026-08-18 20260818작2 ([3-V] V-05) — **여기가 진짜 구멍이었다.**
        //
        //   [왜 2-1(승격 검사)만으로는 부족한가] 공격자는 **승격할 이유가 없다.**
        //     처음부터 `mobile` 이라 신고하고 **안 바꾸면 그만**이다 —
        //     컴퓨터 한도가 0이어도 컴퓨터가 휴대기기 칸으로 무제한 들어온다.
        //     ⇒ 2-1 만 하면 게이트는 초록불이고 **구멍은 그대로다**(거짓봉합).
        //
        //   [고침] 서버가 **자기가 직접 읽은 User-Agent** 로 교차 검증한다.
        //     신고값과 어긋나면 **서버 판정이 이긴다**(ResolveDeviceType).
        //
        //   ⚠️ **완벽하지 않다.** User-Agent 도 위조할 수 있다.
        //     이 봉합이 하는 일은 *"신고값을 그대로 믿지 않는 것"* 하나다 —
        //     화면 조작만으로 칸을 고르던 것을 **헤더까지 함께 위조해야** 되게 바꾼다.
        //     🔴 이것을 *"위조를 막았다"* 고 적으면 거짓봉합이다.
        //
        //   🔴 [`?? "mobile"` 이 어디로 갔나 — 없앤 게 아니라 **안으로 옮겼다**]
        //     ResolveDeviceType 이 **항상 값을 돌려준다**(null 을 안 돌려준다). 그 안 마지막 줄이
        //     `normalized ?? "mobile"` 이다 — 8/16 CR2-2 로 종결된 그 결정이 **그대로 살아 있다.**
        //     ⚠️ 여기에 `?? "mobile"` 을 또 적으면 **절대 실행되지 않는 죽은 코드**가 되어,
        //       읽는 사람이 *"여기가 폴백을 지킨다"* 고 **잘못 믿는다.** 그래서 안 적는다.
        //     🔴 되돌려 `"pc"` 로 만들면 P0 다 — 종류를 모를 때 비싼 칸으로 세면
        //       고객이 쓰지도 않은 자리에 돈을 낸다.
        var type = ResolveDeviceType(req.DeviceType, req.UserAgent);

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
        // ⚠️ 2026-08-16 CR2-4 — 여기 `type == "tablet"` 은 **죽은 가지다.**
        //   위 `type` 은 NormalizeDeviceType 을 거쳐 오는데 그 함수가 tablet 을 이미
        //   mobile 로 흡수한다(:972). 그래서 이 비교는 참이 될 수 없다.
        //   🔴 그래도 **지우지 않는다** — 지워도 동작이 1도 안 바뀌는 대신,
        //     나중에 정규화를 건너뛰는 경로가 생기면 조용히 새는 자리가 된다(헌법 #1 가산 원칙).
        //   ⚠️ 혼동 금지: ApproveAsync:475 의 똑같이 생긴 검사는 **살아 있다.**
        //     거기 `devType` 은 DB 에서 읽은 값이라 옛 행에 'tablet' 이 남아 있을 수 있다.
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
        // 🔴 2026-08-18 20260818작4 — **서버가 도는 그 컴퓨터의 첫 화면은 곧바로 승인이다.**
        //
        //   [왜 갱신 경로만 고치면 안 되나] 위 봉합은 **이미 줄이 있는** 기기를 구한다.
        //     그런데 **새로 설치한 PC 의 첫 로그인**은 여기로 온다 — 줄이 아직 없다.
        //     여기를 안 고치면 대표가 **설치 직후 자기 컴퓨터에서 승인 대기에 갇힌다.**
        //     ⇒ 승인해 줄 사람이 자기인데 그 화면에 못 들어간다. **한 곳만 고치면 아무것도 안 고친 것**이다
        //       (D-9 계통 — 폴백이 두 곳이라 한 곳만 고쳐 아무것도 안 바뀐 사고를 이미 겪었다).
        //
        //   ⚠️ 표식(`is_main_pc`)은 여기서 붙이지 **않는다.** 옛 서버 줄을 내리는 일과 함께
        //     한 곳에서만 해야 하고(위 갱신 경로 ①②), 그 자리는 **다음 접속에 반드시 지나간다.**
        //     여기서 같이 붙이면 표식을 세우는 자리가 둘이 되어 한쪽만 고쳐지는 사고가 난다.
        //     ⇒ 여기서는 **막히지만 않게** 한다. 표식은 다음 접속에 제자리를 찾는다.
        //
        //   ⚠️ `IsLocalConsole` 은 서버가 채운 값이다 — 터널을 지나온 접속은 절대 참이 아니다.
        var newStatus = (_approvalEnabled && !req.IsLocalConsole) ? "pending" : "approved";
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
                //   🔴 20260818작4 — 판정 기준을 위 `newStatus` 와 **같은 식**으로 맞춘다.
                //     여기만 `_approvalEnabled` 를 보면 `approved` 인데 승인자가 비는
                //     **앞뒤가 안 맞는 줄**이 생긴다.
                ApprovedBy = newStatus == "pending" ? null : userId,
                ApprovedAt = newStatus == "pending" ? (DateTime?)null : DateTime.Now
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
        //   🔴 20260818작4 — 여기도 `newStatus` 를 본다. `_approvalEnabled` 를 보면
        //     방금 `approved` 로 넣어 놓고 화면에는 *"승인 대기"* 라 답하는
        //     **정반대 답**이 나간다 ⇒ 대표가 자기 컴퓨터에서 관문에 갇힌다(이 차수의 증상 그 자체).
        return newStatus == "pending"
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

        // ══════════════════════════════════════════════════════════════
        // 🔴 2026-08-18 20260818작2 (DP-1) — **폐기하면 열쇠도 함께 없앤다.**
        //   (검증팀 [4] 적발 · docs/검증/병렬이슈/20260818_검증팀_DP1_DP2_폐기키생존_메인PC반려.md)
        //
        //   [무엇이 문제였나] 종전엔 `status`·`revoked_at`·`revoked_reason` 만 바꾸고
        //     **`auth_key_hash` 를 그대로 뒀다.** 폐기된 기기의 열쇠가 표에 살아 있었다.
        //     막는 것은 VerifyAuthKeyAsync 안의 **한 줄뿐**(`if (status == "revoked") return null;`)이고,
        //     🔴 **검증팀이 그 한 줄을 빼도 게이트가 12/12 초록불이었다** —
        //       유일한 방어를 지켜보는 눈이 **0개**였다는 뜻이다.
        //
        //   [고침 — 근본은 이쪽이다] **키가 없으면 방어할 것도 없다.**
        //     상태 검사는 "열쇠는 살아 있지만 문을 잠갔다" 이고, 이것은 "열쇠를 없앴다" 이다.
        //     ⇒ 상태 검사 한 줄이 미래에 사라지거나 우회돼도 **되살아날 키 자체가 없다.**
        //
        //   ⚠️ `auth_key_issued_at` 도 함께 지운다 — 키가 없는데 발급 시각만 남으면
        //     화면·기록이 *"키가 있다"* 고 말하는 셈이 된다.
        //   ⚠️ VerifyAuthKeyAsync 의 `status == "revoked"` 검사는 **그대로 둔다**(작1 소관 · 헌법 #1).
        //     둘은 겹치는 것이 아니라 **겹쳐서 막는 것**이다 — 이미 폐기된 옛 행에는 키가 남아 있다.
        // ══════════════════════════════════════════════════════════════
        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE tenant_devices
            SET status = 'revoked',
                revoked_at = NOW(),
                revoked_reason = @Reason,
                auth_key_hash = NULL,
                auth_key_issued_at = NULL
            WHERE device_id = @Id AND tenant_id = @TenantId
            """,
            new { Id = deviceId, TenantId = tenantId, Reason = reason }, cancellationToken: ct));

        await _audit.LogAsync("revoke", "device", deviceId, reason: reason, ct: ct);
    }

    // ── 기기 승인 (대표계정) ── 20260811작1 (B)
    //   사장님 설계: "승인대기. 대표에게 기기승인의 권한을 주기"
    //   반환값 = **새로 발급한 인증키 원문**. 이미 승인된 기기를 다시 누르면 null 이다
    //   (원문은 우리가 갖고 있지 않으므로 다시 알려줄 수 없다 — 재발급은 별건).
    public async Task<string?> ApproveAsync(string deviceId, string tenantId, string approverUserId,
        string? assignUserId = null, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        // 승인 대상 확인 — 남의 테넌트 기기를 승인할 수 없다(헌법 #2 격리).
        var target = await _db.QueryFirstOrDefaultAsync<(string status, string type)?>(new CommandDefinition(
            "SELECT status AS status, device_type AS type FROM tenant_devices WHERE device_id = @Id AND tenant_id = @TenantId",
            new { Id = deviceId, TenantId = tenantId }, cancellationToken: ct));

        if (target is null)
            throw new InvalidOperationException("기기를 찾을 수 없습니다.");

        var (curStatus, devType) = target.Value;
        if (curStatus == "approved") return null;         // 이미 승인됨 — 두 번 눌러도 안전(멱등)
        if (curStatus == "revoked")
            throw new InvalidOperationException("폐기된 기기는 승인할 수 없습니다. 그 기기에서 다시 접속해 주세요.");

        // 🔴 승인 시점에 한도를 **다시** 본다.
        //   대기 목록에 3대가 쌓여 있고 남은 슬롯이 1대라면, 첫 승인은 되고 나머지는 막혀야 한다.
        //   등록 시점에만 검사하면 대기분이 한도를 넘겨 통과한다(슬롯 과금이 무너지는 자리).
        //   🔴 20260815작3 P1 — 계수와 한도를 단일 메서드로 모았다.
        //
        //   ⚠️ **자기 자신은 아직 `pending` 이라 안 세어진다** — 이 전제 위에 아래 비교가 서 있다.
        //     CountUsedSlotsAsync 가 `status='approved'` 만 세기 때문에 성립한다.
        //     🔴 계수 대상을 pending 까지 넓히면 승인하려는 그 기기가 자기를 세어
        //       남은 자리가 있어도 `pcUsed >= pcLimit` 이 참이 되어 **승인이 영원히 막힌다**
        //       (P0 실측 D-4). I-7 게이트가 이 경계를 지킨다.
        var (pcLimit, mobileLimit) = await GetLimitsAsync(tenantId, ct);
        var (pcUsed, mobileUsed) = await CountUsedSlotsAsync(tenantId, ct);

        if (devType == "pc" && pcUsed >= pcLimit)
            throw new InvalidOperationException("인증기기 한도초과. 슬롯을 추가하거나 사용하지 않는 기기를 해제해 주세요.");
        if ((devType == "mobile" || devType == "tablet") && mobileUsed >= mobileLimit)
            throw new InvalidOperationException("인증기기 한도초과. 슬롯을 추가하거나 사용하지 않는 기기를 해제해 주세요.");

        // 🔴 승인하는 **그 순간** 인증키를 발급한다 (20260811작3 · 사장님 오더).
        //   *"사용PC에는 물리적으로 간단한 인증서 같은 인증키를 부여"*
        //   *"인증 슬롯을 식별할 수 있도록 슬롯인증 절차에서 인증키 같은 걸 심자"*
        //
        //   [왜 인증키인가] 종전엔 서버가 **브라우저에게 "너 누구냐"** 를 물었다(지문).
        //     브라우저는 자기 안에만 흔적을 남기므로 같은 컴퓨터라도 Edge 와 Chrome 이
        //     각자 다른 답을 한다 — **한 대가 두 대로 세어지고 고객이 돈을 더 낸다.**
        //     인증키는 묻는 대상을 **"네가 받은 키를 내놔라"** 로 바꾼다.
        //     추측을 정교하게 만드는 게 아니라 **애초에 추측을 안 하게** 만든다.
        //
        //   🔴 [왜 "나중에 그 기기가 달라고 하면 준다" 가 아닌가 — 사장님 지적]
        //     *"그게 직원인지, 해커인지 어떻게 아니??"*
        //     승인만 나 있으면 **먼저 물어본 쪽이 키를 가져간다.** 물어보는 쪽이
        //     그 직원인지 확인할 방법이 없다 — 지문은 브라우저가 스스로 신고하는 값이라
        //     흉내낼 수 있다. 그래서 **대표가 승인하는 그 자리에서** 발급한다.
        //     발급 시점을 사람이 지키는 것이 유일하게 확실한 문지기다.
        //
        //   [원문을 갖지 않는다] 이 키는 그 자체로 접속 권한이다. 우리가 보관하면
        //     DB 가 새는 날 남의 기기로 들어올 수 있다. 해시만 남기고 원문은
        //     **발급 순간 한 번만** 위로 올린다(QR 토큰과 같은 원칙 · 헌법 #5).
        var authKey = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

        // 🔴 2026-08-18 20260818작2 (2-5) — **대표가 승인하며 "누구 기기인가" 를 고른다.**
        //
        //   [무엇이 문제였나] QR 로 들어온 폰은 `user_id` 가 NULL 이었다.
        //     등록 시점엔 **누구 폰인지 알 방법이 없다** — QR 은 로그인 없이 찍는다.
        //     그래서 기기 목록에 주인 없는 폰이 쌓이고, 대표는 나중에 그게 누구 것인지 모른다.
        //
        //   [고침] **아는 사람이 아는 자리에서** 채운다. 대표는 승인할 때 그 폰이 누구 것인지 안다
        //     (직원이 전화해서 "제 폰 승인해 주세요" 라고 한 그 순간이다).
        //
        //   ⚠️ **존재하는 사용자만** 받는다 — `fk_device_user` FK 라 없는 번호를 넣으면
        //     UPDATE 자체가 터져 **승인이 통째로 실패**한다. 대표가 못 고르는 게 아니라
        //     **아무도 승인이 안 되는** 사고가 된다. 그래서 먼저 확인한다.
        //   ⚠️ 같은 회사 사람만 받는다(헌법 #2) — 남의 회사 사람에게 우리 기기를 붙일 수 없다.
        string? resolvedUserId = null;

        if (!string.IsNullOrWhiteSpace(assignUserId))
        {
            resolvedUserId = await _db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
                "SELECT user_id FROM users WHERE user_id = @Uid AND tenant_id = @TenantId LIMIT 1",
                new { Uid = assignUserId, TenantId = tenantId }, cancellationToken: ct));

            if (resolvedUserId is null)
                throw new InvalidOperationException("지정한 사용자를 찾을 수 없습니다. 목록에서 다시 골라 주세요.");
        }

        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE tenant_devices
            SET status = 'approved',
                approved_by = @Approver,
                approved_at = NOW(),
                user_id = COALESCE(@AssignUser, user_id),
                auth_key_hash = @KeyHash,
                auth_key_issued_at = NOW(6)
            WHERE device_id = @Id AND tenant_id = @TenantId
            """,
            // ⚠️ COALESCE 인 이유: 대표가 사람을 안 고르면 **기존 주인을 지우지 않는다.**
            //   PC 경로는 로그인한 사람이 이미 붙어 있다 — 승인 한 번으로 그것을 날리면 안 된다.
            new
            {
                Id = deviceId,
                TenantId = tenantId,
                Approver = approverUserId,
                AssignUser = resolvedUserId,
                KeyHash = Sha256Hex(authKey)
            },
            cancellationToken: ct));

        await _audit.LogAsync("approve", "device", deviceId, ct: ct);

        // 원문은 딱 한 번 올라간다. 로그·감사기록에 남기지 않는다.
        return authKey;
    }

    /// 직원 PC 가 입력한 인증키를 대조한다 (20260811작3 (A)).
    ///
    /// 🔴 사장님 확정: *"메인PC에서 인증키가 생성되면, 요청한 클라이언트PC에서 입력하는 방식."*
    ///
    ///   여기서 서버가 하는 일은 **대조 하나**다. 추측하지 않는다.
    ///   넘어온 키를 해시로 만들어 저장된 해시와 같은지만 본다.
    ///
    ///   ⚠️ 같은 회사(tenant) 안에서만 찾는다 — 남의 회사 키로 들어올 수 없다(헌법 #2).
    ///   ⚠️ 승인된 기기만 인정한다 — 폐기된 기기의 옛 키가 살아나면 안 된다.
    ///
    /// 반환: 맞으면 **이 기기의 기계비밀 원문**(K-1 · 이 순간에만 존재), 틀리면 null
    ///
    /// 🔴 2026-08-18 20260818작1 (1-1) — **키를 "검색 열쇠"에서 "대조 열쇠"로 바꿨다.**
    ///
    ///   [무엇이 문제였나] 종전 SQL 은 **키만 보고 줄을 검색**했다:
    ///     `WHERE tenant_id=@T AND auth_key_hash=@H AND status='approved'` — **device_id 조건이 없다.**
    ///     ⇒ 남의 키를 넣으면 **그 남의 줄이 그대로 반환**된다.
    ///       인증키가 **회사 공용 열쇠**가 되어 아무 기기나 남의 줄을 열고 통과했다.
    ///       요금과 접근통제가 **동시에** 무너지는 자리다.
    ///
    ///   [고침 — 핵심 원칙] 🔴 **키는 "맞나 틀리나"만 판정한다. 무엇을 열지는 키가 정하지 않는다.**
    ///     ① 이 세션이 신청한 줄을 **먼저 특정**한다 (device_id 로).
    ///     ② 그 줄의 해시와 **대조**한다.
    ///     ⇒ 남의 키를 넣어도 **자기 줄에서 대조되어 틀림으로 끝난다.**
    ///       남의 device_id 를 반환하는 경로가 **0** 이 된다.
    ///
    ///   🔴 [왜 새 줄을 만들지 않는가 — [3-V] V-02 판정]
    ///     "성공 시 서버가 새 device_id 를 발급해 새 줄을 만든다" 로 짜면 **착수 당일 1062** 가 난다.
    ///     `fingerprint` 는 **NOT NULL** 이고 `uq_tenant_fp(tenant_id, fingerprint)` 가 **UNIQUE** 다:
    ///       NULL 불가 · 같은 지문 불가(1062) · 임의값은 다음 접속에 못 찾아 또 새 줄(지금 사고 재발).
    ///     ⇒ 🔴 **줄은 이미 있다.** 직원이 관문에서 신청할 때 `pending` 줄이 생겼다.
    ///       verify-key 가 하는 일은 **그 줄을 승인으로 바꾸는 것**이지 새로 만드는 것이 아니다.
    ///       INSERT 가 아니라 **UPDATE** 다. UNIQUE·NOT NULL 을 건드릴 일이 없다.
    ///
    ///   🔴 [1회용 → 교체] 대조에 성공하면 해시를 **기계비밀의 해시로 교체**한다
    ///     (20260819작1 K-1 · 사장님 8/20 오더로 결재 4 자구 조정).
    ///     🔴 **사람이 본 키는 지금처럼 죽는다** — 옆에서 본 사람은 여전히 못 들어온다(결재 4 의도 유지).
    ///     ⚠️ 왜 소거(NULL)가 아닌가 — **소거는 그 기기의 통행로 자체를 없앴다**(K-0 잠재 P0):
    ///       미들웨어 축①(auth_key_hash)이 이 기기의 매 요청 통행증인데, 소거하면 축① 이 영원히 0 이고
    ///       축②(is_main_pc)·축③(대표 탈출로)은 직원 기기에 해당이 없다 ⇒ **업무 API 전면 403.**
    ///       8/18 의 두 봉합(소거 + 축② 좁힘)이 **같은 커밋(c25a6dce)에 실리며 서로를 몰랐다.**
    ///     ⇒ 기계비밀(사람 눈에 안 보이는 값)이 그 자리를 이어받아 **매 요청 통행증**이 된다.
    ///     ⚠️ 오타로 막힌 직원은 대표가 [인증키 재발급] 을 눌러 되살린다(1-8) —
    ///       재발급이 없으면 **오타 낸 직원이 영구 차단**되고 그것이 8/10 사고와 같은 모양이다.
    ///
    ///   ⚠️ 같은 회사(tenant) 안에서만 찾는다 — 남의 회사 키로 들어올 수 없다(헌법 #2).
    ///
    /// <param name="sessionDeviceId">
    ///   🔴 **이 세션이 관문에서 발급받은 장비넘버.** 이것이 "어느 줄인가" 를 정한다.
    ///   비어 있으면 열 줄을 특정할 수 없으므로 **아무것도 열지 않는다**(null).
    /// </param>
    public async Task<string?> VerifyAuthKeyAsync(
        string authKey, string tenantId, string? sessionDeviceId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(authKey)) return null;

        // 🔴 열 줄을 모르면 아무것도 열지 않는다.
        //   종전처럼 "키로 찾아서 열어 주기" 로 되돌아가면 그 순간 공용 열쇠가 부활한다.
        if (string.IsNullOrWhiteSpace(sessionDeviceId)) return null;

        await EnsureOpenAsync(ct);

        // ① 이 세션의 줄을 **먼저** 잡는다. 키로 찾는 것이 아니다.
        //   ⚠️ status 를 조건에 넣지 않는다 — 폐기된 줄이면 아래에서 갈라 거절해야
        //     "왜 안 되는지" 를 서버가 알 수 있다.
        var row = await _db.QueryFirstOrDefaultAsync<(string? hash, string status)?>(new CommandDefinition(
            """
            SELECT auth_key_hash AS hash, status AS status
            FROM tenant_devices
            WHERE tenant_id = @TenantId AND device_id = @DeviceId
            LIMIT 1
            """,
            new { TenantId = tenantId, DeviceId = sessionDeviceId }, cancellationToken: ct));

        if (row is null) return null;

        var (storedHash, status) = row.Value;

        // 폐기된 기기는 옛 키가 남아 있어도 되살아나지 않는다.
        if (status == "revoked") return null;

        // 아직 키를 못 받은 줄(대표가 승인을 안 눌렀다) — 대조할 것이 없다.
        if (string.IsNullOrWhiteSpace(storedHash)) return null;

        // ② **대조 하나.** 이 줄의 해시와 같은가.
        //   🔴 다른 줄의 해시와는 절대 비교하지 않는다 — 그것이 공용 열쇠를 만드는 자리였다.
        if (!string.Equals(storedHash, Sha256Hex(authKey), StringComparison.OrdinalIgnoreCase))
            return null;

        // ③ 맞았다 — 이 줄을 승인으로 올리고 **사람 키를 기계비밀로 교체한다(K-1).**
        //   ⚠️ UPDATE 다. 새 줄을 만들지 않으므로 uq_tenant_fp·NOT NULL 을 건드리지 않는다.
        //
        //   🔴 소거(NULL)가 아니라 교체인 이유 — 소거는 이 기기의 매 요청 통행로(미들웨어 축①)를
        //     함께 없앴다(K-0 잠재 P0 · 20260819작1). 사람이 본 키는 이 교체로 지금처럼 죽고,
        //     사람 눈에 안 보인 기계비밀이 통행증 역할만 이어받는다.
        //   ⚠️ 원문은 보관하지 않는다 — 이 반환값이 유일하게 존재하는 순간이고,
        //     화면(DeviceAuthGate)이 브라우저 저장소에 넣는 것으로 끝난다(헌법 #5 원칙 동일).
        var deviceSecret = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE tenant_devices
            SET status = 'approved',
                approved_at = COALESCE(approved_at, NOW(6)),
                auth_key_hash = @SecretHash,
                last_seen_at = NOW(6)
            WHERE tenant_id = @TenantId AND device_id = @DeviceId
            """,
            new { TenantId = tenantId, DeviceId = sessionDeviceId, SecretHash = Sha256Hex(deviceSecret) },
            cancellationToken: ct));

        await _audit.LogAsync("device_key_verified", "device", sessionDeviceId, ct: ct);

        return deviceSecret;
    }

    /// 🔴 매 요청 통행 판정 — **이 기기의 기계비밀이 맞는가** (20260819작1 K-3).
    ///
    ///   미들웨어 축①(DeviceAuthMiddleware)이 요청마다 부르는 판정 본체다.
    ///   🔴 넷이 전부 맞아야 한다: 같은 회사(헌법 #2) · **그 기기 번호** · 그 줄의 해시 · 승인 상태.
    ///
    ///   [왜 device_id 를 결합하나 — K-3] 종전 축① 은 해시만 봤다:
    ///     `WHERE tenant_id=@T AND auth_key_hash=@H AND status='approved'` — 기기 조건이 없어
    ///     **한 기기의 비밀값이 회사 공용 통행증**이 될 수 있었다(8/18 주석의 "다음 차수 몫" 이 이 자리다).
    ///   ⇒ 비밀값과 기기 번호를 **짝으로** 요구한다. 남의 비밀 + 자기 번호도, 자기 비밀 + 남의 번호도 0 이다.
    ///
    ///   ⚠️ 판정 SQL 을 미들웨어에 인라인으로 두지 않고 여기로 모았다 —
    ///     축① 과 서비스 판정이 갈리면 한쪽만 고쳐지는 사고가 난다(이 파일이 이미 그 사고를 겪었다).
    public async Task<bool> VerifyDeviceSecretAsync(
        string deviceId, string deviceSecret, string tenantId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return false;
        if (string.IsNullOrWhiteSpace(deviceSecret)) return false;
        if (string.IsNullOrWhiteSpace(tenantId)) return false;

        await EnsureOpenAsync(ct);

        return (await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM tenant_devices
            WHERE tenant_id = @Tid AND device_id = @Did
              AND auth_key_hash = @Hash AND status = 'approved'
            """,
            new { Tid = tenantId, Did = deviceId, Hash = Sha256Hex(deviceSecret) },
            cancellationToken: ct))) > 0;
    }

    // ── 🔴 인증키 재발급 (대표계정) ── 20260818작1 (1-8) · 사장님 결재 4
    //
    //   사장님 문구 그대로: *"1회용 + 재발급 화면 필요 — 버튼 [인증키 재발급]"*
    //
    //   🔴 [왜 1-1 과 한 몸인가] 1-1 이 키를 **1회용**으로 만들었다. 1회용은 그 자체로는 위험하다 —
    //     직원이 **오타를 내거나** 키를 잃으면 **되살릴 길이 없다.**
    //     ⇒ 그 순간 8/10 사고와 **같은 모양**이 된다: *쓰던 사람이 새 규칙에 막혀 못 들어온다.*
    //     그래서 1-1 과 1-8 은 **한 작업**이고 쪼개면 안 된다(작업지시서 §2).
    //
    //   [동작] 옛 해시를 **버리고** 새 키를 만든다. 옛 키는 그 순간 죽는다.
    //     ⚠️ 원문은 우리가 보관하지 않는다 — 돌려주는 이 반환값이 **유일하게 존재하는 순간**이다.
    //       화면이 그것을 대표에게 한 번 보여주고 끝난다(헌법 #5 · ApproveAsync 와 같은 원칙).
    //
    //   🔴 [알림에 싣지 않는다] 사장님 8/16 오더 — *"옆에서 보면 샌다"*.
    //     이 값은 **화면에만** 뜬다. 알림·메일·문자에 절대 싣지 않는다.
    //
    //   ⚠️ 폐기된 기기는 재발급하지 않는다 — 폐기를 되돌리는 길은 여기가 아니다(폐기 해제가 따로 있다).
    /// <summary>인증키 재발급 (대표계정). 반환값 = 새 인증키 원문 — 이 순간에만 존재한다.</summary>
    public async Task<string?> ReissueAuthKeyAsync(
        string deviceId, string tenantId, string approverUserId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        var status = await _db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT status FROM tenant_devices WHERE device_id = @Id AND tenant_id = @TenantId LIMIT 1",
            new { Id = deviceId, TenantId = tenantId }, cancellationToken: ct));

        if (status is null)
            throw new InvalidOperationException("기기를 찾을 수 없습니다.");

        if (status == "revoked")
            throw new InvalidOperationException("폐기된 기기는 인증키를 재발급할 수 없습니다.");

        var authKey = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE tenant_devices
            SET auth_key_hash = @KeyHash,
                auth_key_issued_at = NOW(6)
            WHERE device_id = @Id AND tenant_id = @TenantId
            """,
            new { Id = deviceId, TenantId = tenantId, KeyHash = Sha256Hex(authKey) },
            cancellationToken: ct));

        // 이력 — 누가·언제·어느 기기 (기존 감사기록 패턴 재사용).
        //   ⚠️ 키 자체는 남기지 않는다. 남기면 보관 안 하는 의미가 사라진다.
        await _audit.LogAsync("device_key_reissue", "device", deviceId,
            reason: "대표계정 인증키 재발급", ct: ct);

        return authKey;
    }

    // ── 기기 승인 거부 (대표계정) ── 20260811작1 (B)
    public async Task RejectAsync(string deviceId, string tenantId, string approverUserId, string? reason, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        // 대기 중인 기기만 거부한다. 이미 쓰고 있는(approved) 기기를 여기서 끊지 않는다 —
        // 그건 폐기(RevokeAsync)이고, 회사 서버 보호 가드가 그쪽에 있다.
        //
        // 🔴 2026-08-18 20260818작1 (1-4) — **거절을 폐기에서 갈랐다.**
        //
        //   [무엇이 문제였나] 여기가 `SET status='revoked'` 였다.
        //     **"이번엔 아니다"** 와 **"폐기"** 가 **같은 칸**에 들어가 있었다.
        //     ⇒ 거절당한 직원은 RegisterOrRefreshAsync 의 `revoked` 갈래에 걸려
        //       *"폐기된 기기입니다"* 로 **로그인 자체가 막힌다.** 다시 신청할 길이 없다.
        //       ⇒ 사장님 오더 *"거절하면 첫 화면 회귀"* 가 **물리적으로 불가능**했다.
        //
        //   [고침] `rejected` 라는 **제 칸**을 준다. 그 기기는 **다시 신청할 수 있다.**
        //     🟢 DDL 변경 없음 — `status` 가 `varchar(20)` 이라 ALTER 가 필요 없다(실측).
        //
        //   🔴 **`revoked` 의 뜻은 한 글자도 안 바꿨다.** 폐기는 그대로 폐기다(작업지시서 §8).
        //     기존 `revoked` 행을 일괄 전환하지도 않는다 — 대표가 폐기한 것은 폐기로 남는다.

        // ══════════════════════════════════════════════════════════════
        // 🔴 2026-08-18 20260818작2 (DP-2) — **메인PC 는 반려할 수 없다.**
        //   (검증팀 [4] 적발 · docs/검증/병렬이슈/20260818_검증팀_DP1_DP2_폐기키생존_메인PC반려.md)
        //
        //   [무엇이 문제였나] 폐기(RevokeAsync)에는 메인PC 가드가 있는데 **반려에는 없었다.**
        //     그리고 **메인PC 도 `pending` 일 수 있다**(MainPcRegistrationService 가 pending 줄을 승격시킨다)
        //     ⇒ 검증팀이 **메인PC 를 실제로 `rejected` 로 만드는 데 성공했다.**
        //
        //   🔴 [왜 이게 게시를 막는 사고인가] 8/16 에 **대표가 자기 화면에서 막혀 스스로 못 빠져나온**
        //     P0 가 있었다(커밋 30e3873 — 사장님이 직접 겪으셨다). 그때 만든 구제책이
        //     RegisterOrRefreshAsync 의 `status == "revoked" && !isMainPc` 인데,
        //     그것은 **`revoked` 만 덮는다.** ⇒ 메인PC 가 `rejected` 가 되면 구제 경로가 없고,
        //     **승인해 줄 수 있는 유일한 사람이 승인 화면에 못 들어간다.**
        //
        //   [고침은 두 자리다 — 하나만 하면 다른 경로로 같은 사고가 난다]
        //     ① **막는 자리** = 여기. 애초에 메인PC 가 반려되지 않게 한다.
        //     ② **빠져나오는 자리** = RegisterOrRefreshAsync 의 구제 조건(`rejected` 도 덮게 했다).
        //     🔴 막는 것과 빠져나오는 것은 **다른 역할**이다. ②만 있으면 표가 더러워지고,
        //       ①만 있으면 이미 rejected 인 기존 행이 영영 못 나온다.
        //
        //   ⚠️ 일반 기기는 **종전대로 반려된다** — 여기서 전부 막으면 1-4(거절→재신청)가 죽는다.
        // ══════════════════════════════════════════════════════════════
        var rejectTargetIsMainPc = await _db.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT COALESCE(is_main_pc, 0) FROM tenant_devices WHERE device_id = @Id AND tenant_id = @TenantId LIMIT 1",
            new { Id = deviceId, TenantId = tenantId }, cancellationToken: ct));

        if (rejectTargetIsMainPc)
        {
            throw new InvalidOperationException(
                "회사 서버는 거부할 수 없습니다. 자료를 보관하는 컴퓨터입니다.");
        }

        var affected = await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE tenant_devices
            SET status = 'rejected',
                revoked_at = NOW(),
                revoked_reason = @Reason
            WHERE device_id = @Id AND tenant_id = @TenantId AND status = 'pending'
            """,
            new { Id = deviceId, TenantId = tenantId, Reason = reason ?? "대표계정 승인 거부" }, cancellationToken: ct));

        if (affected == 0)
            throw new InvalidOperationException("승인 대기 중인 기기가 아닙니다.");

        await _audit.LogAsync("reject", "device", deviceId, reason: reason, ct: ct);
    }

    // ── 대표 연락처 (20260818작2 — 직원이 갈 곳을 만든다) ──
    //
    //   사장님 오더 계통: 관문에 막힌 직원이 **누구에게 전화할지** 알아야 흐름이 안 끊긴다(헌법 #20).
    //
    //   🔴 [새 컬럼을 만들지 않았다] `users`·`employees` 에 이미 있는 값을 읽을 뿐이다(헌법 #9).
    //     ⚠️ 작업지시서는 `employees.home_phone` 도 있다고 적었으나 **출하 DDL 실측 결과 없다**
    //       (`employees` 에는 `phone`·`email` 만 있다). 있는 것만 쓴다 — 없는 컬럼을 SQL 에 적으면
    //       런타임 500 이다(헌법 #13 DESCRIBE 선행).
    //
    //   🔴 [본사로 안 나간다] 헌법 #18·#22 — 이 값은 고객사 화면에만 뜬다.
    //
    //   ⚠️ [못 찾으면 null] 대표가 없거나 이름이 비면 null 을 돌려준다.
    //     화면은 그때 종전 문구("관리자에게 문의")로 그대로 간다 — **안내가 사라지면 안 된다.**
    public async Task<AdminContactDto?> GetAdminContactAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        try
        {
            // 대표계정(tenant_admin) 한 사람을 찾는다.
            //   ⚠️ 부모계정(is_parent=1)을 앞에 세운다 — 대표가 여럿일 수 있고, 그중 원본은 부모다.
            //   ⚠️ 전화번호는 users 에 없으면 employees 에서 찾는다(사람은 같은데 값이 갈려 있다).
            var row = await _db.QueryFirstOrDefaultAsync<(string? name, string? phone)?>(new CommandDefinition(
                """
                SELECT COALESCE(NULLIF(u.emp_name, ''), u.user_name) AS name,
                       COALESCE(NULLIF(u.phone, ''), NULLIF(e.phone, '')) AS phone
                FROM users u
                LEFT JOIN employees e
                       ON e.user_id = u.user_id AND e.tenant_id = u.tenant_id
                WHERE u.tenant_id = @TenantId
                  AND u.account_type = 'tenant_admin'
                  AND u.is_active = 1
                  AND u.is_deleted = 0
                ORDER BY u.is_parent DESC, u.created_at ASC
                LIMIT 1
                """,
                new { TenantId = tenantId }, cancellationToken: ct));

            if (row is null) return null;

            var (name, phone) = row.Value;
            if (string.IsNullOrWhiteSpace(name)) return null;

            return new AdminContactDto { Name = name, Phone = string.IsNullOrWhiteSpace(phone) ? null : phone };
        }
        catch (Exception ex)
        {
            // 🔴 헌법 #15 — 빈 catch 금지. 연락처를 못 읽었다고 **관문 화면이 죽으면 안 된다.**
            //   못 찾은 것과 같게 처리한다 ⇒ 화면은 종전 문구로 간다.
            _logger.LogWarning(ex,
                "[TenantDeviceService] 대표 연락처를 못 읽었다 — 관문 화면은 종전 문구로 간다. tenant={TenantId}",
                tenantId);
            return null;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // 🔴 20260818작3 — 승인 요청을 알릴 **대표계정의 사원 ID**
    //
    //   [무엇이 문제였나] 사장님 실측: *"승인 메시지 안 떴어."*
    //     승인 화면은 있었으나 **알림이 0곳**이었다 — 대표가 [설정 → 등록 기기 관리] 에
    //     **직접 들어가야만** 요청이 온 줄 알 수 있었다.
    //     ⇒ 직원은 기다리고 대표는 모른다. 아무도 틀리지 않았는데 일이 안 된다.
    //
    //   [왜 GetAdminContactAsync 와 가르나] 그것은 **직원 화면에 보여 줄** 이름·전화번호이고,
    //     이것은 **알림을 보낼 주소**다. 물음이 다르다 —
    //     하나로 묶으면 화면에 쓸 값을 알림이 끌고 다니게 된다.
    //     (8/18 에 `IsDeviceAllowedAsync` 를 관문이 같이 쓰다 난 사고와 같은 모양이다.)
    //
    //   ⚠️ 대표가 사원으로 등록돼 있지 않으면 null — 그때는 알림을 조용히 건너뛴다.
    //     알림이 없다고 **등록 자체가 막히면 안 된다**(부수 기능이 본 기능을 죽이지 않는다).
    // ══════════════════════════════════════════════════════════════
    public async Task<string?> GetAdminEmployeeIdAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        try
        {
            // 대표계정(tenant_admin) 의 **사원 ID**. 부모계정을 앞에 세운다(원본이 부모다).
            return await _db.ExecuteScalarAsync<string?>(new CommandDefinition(
                """
                SELECT e.employee_id
                FROM users u
                JOIN employees e
                  ON e.user_id = u.user_id AND e.tenant_id = u.tenant_id
                WHERE u.tenant_id = @TenantId
                  AND u.account_type = 'tenant_admin'
                  AND u.is_active = 1
                  AND u.is_deleted = 0
                ORDER BY u.is_parent DESC, u.created_at ASC
                LIMIT 1
                """,
                new { TenantId = tenantId }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            // 🔴 헌법 #15 — 빈 catch 금지. 알림 주소를 못 찾았다고 **등록이 죽으면 안 된다.**
            _logger.LogWarning(ex,
                "[TenantDeviceService] 대표 사원 ID 를 못 읽었다 — 승인 요청 알림을 건너뛴다. tenant={TenantId}",
                tenantId);
            return null;
        }
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
    //
    // 🔴 2026-08-18 20260818작2 (2-4b) — **옛 주석을 걷어냈다. 그 결재는 뒤집혔다.**
    //
    //   [여기 있던 옛 문장] *"QR 을 띄운 것 자체가 대표계정의 승인이다
    //     → 별도 승인 단계 없이 approved 로 들어간다."*
    //
    //   [뒤집힌 근거] 2026-08-16 사장님 전결 — *"PC환경 절차와 같은 절차가 있어야 함."*
    //     (docs/운영기록/20260816작2 §7 결재 2 · docs/운영기록/20260818작2 §2 (2-4))
    //
    //   [왜 뒤집혔나] QR 이 화면에 떠 있는 10분 동안 **옆 사람 폰이 찍어도 등록**된다.
    //     "띄운 것"과 "그 폰인 것"은 다른 사실이다. 대표는 앞의 것만 알고 뒤의 것을 모른다.
    //
    //   🔴 **이 주석을 옛 문장으로 되돌리지 마라.** 주석이 옛 규칙을 말하면 다음 사람이
    //     그것을 근거로 아래 `qrStatus` 를 approved 로 되돌린다. 실제로 이 자리가
    //     8/16 봉합 뒤에도 옛 문장을 그대로 달고 있어 2-4b 로 따로 잡혔다.
    public async Task<(bool ok, string message, string? deviceId, AdminContactDto? adminContact)> RegisterMobileByTokenAsync(
        string token, string deviceName, string fingerprint, string ipAddress, string? userAgent,
        string? knownDeviceId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(fingerprint))
            return (false, "등록 정보가 올바르지 않습니다.", null, null);

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
            return (false, "만료되었거나 이미 사용된 코드입니다. 다시 시도해 주세요.", null, null);

        // 🔴 2026-08-18 20260818작2 (2-4) — `issued_by`(QR 을 띄운 사람)를 **더 이상 쓰지 않는다.**
        //   종전엔 승인제가 꺼져 있을 때 이 사람을 `approved_by` 에 적었다.
        //   그러나 그는 승인한 적이 없다 — QR 을 띄웠을 뿐이고 **누가 찍었는지 모른다.**
        //   ⚠️ 조회 SQL 에서 `issued_by` 를 빼지 않는다(헌법 #37 · #1) — 값은 계속 저장되고,
        //     "누가 이 QR 을 띄웠나" 는 나중에 추적할 때 쓰이는 사실이다. 여기서 안 읽을 뿐이다.
        var (tokenId, tenantId, _) = row.Value;

        // 같은 폰이 다시 찍은 경우 — 이미 등록돼 있으면 그대로 성공 처리(멱등).
        //
        // 🔴 2026-08-18 20260818작2 (2-3) — **번호를 먼저 보고, 없으면 지문을 본다.**
        //
        //   [무엇이 문제였나] 종전엔 **지문 하나로만** 찾았다. 그런데 지문은 브라우저 환경에서
        //     만들어져 **흔들린다** — 사파리에서 크롬으로 옮기거나, 브라우저를 새로 깔거나,
        //     사생활 보호 모드로 들어가면 다른 값이 나온다.
        //     ⇒ **같은 폰이 다시 찍어도 "처음 온 폰"** 이 되어 새 줄이 생겼다.
        //       이것이 사장님 8/16 증상② — *"한 기기에서 슬롯 중복으로 잡힘"* — 그 자체다.
        //
        //   [고침] PC 경로(RegisterOrValidateAsync)가 이미 쓰는 **같은 순서**로 맞춘다:
        //     ① 폰이 보관해 둔 자기 번호(device_id) → ② 없으면 종전대로 지문.
        //     번호는 서버가 준 값이라 브라우저를 바꿔도 안 흔들린다.
        //
        //   🔴 [지문 조회를 없애지 않는다 — 헌법 #37] *"안 읽힌다 ≠ 잔재"*.
        //     번호를 아직 못 받은 **옛 폰**은 지문밖에 없다. 지문 갈래를 지우면 그 폰들이
        //     전부 새 줄로 다시 등록되어 슬롯을 두 번 먹는다. **앞에 더하는 것이지 지우는 게 아니다.**
        //
        //   ⚠️ tenant_id 를 함께 본다 — 남의 회사 번호를 들고 와도 이 회사 줄은 안 열린다(헌법 #2).
        string? existing = null;

        if (!string.IsNullOrWhiteSpace(knownDeviceId))
        {
            existing = await _db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
                "SELECT device_id FROM tenant_devices WHERE tenant_id = @TenantId AND device_id = @Dev LIMIT 1",
                new { TenantId = tenantId, Dev = knownDeviceId }, cancellationToken: ct));
        }

        if (string.IsNullOrEmpty(existing))
        {
            existing = await _db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
                "SELECT device_id FROM tenant_devices WHERE tenant_id = @TenantId AND fingerprint = @Fp LIMIT 1",
                new { TenantId = tenantId, Fp = fingerprint }, cancellationToken: ct));
        }

        if (!string.IsNullOrEmpty(existing))
        {
            // 🔴 지문이 흔들려 번호로 찾아온 폰은 **지금 지문으로 갱신**해 둔다.
            //   그래야 번호를 잃어버린(저장소를 지운) 다음번에도 지문 갈래가 그 폰을 알아본다.
            //   ⚠️ uq_tenant_fp UNIQUE 에 걸릴 수 있다 — 그 지문을 이미 **다른 줄**이 쓰고 있으면
            //     갱신을 조용히 포기한다. 갱신 실패가 등록 자체를 죽이면 안 된다(헌법 #15 — 흔적은 남긴다).
            try
            {
                await _db.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE tenant_devices
                    SET fingerprint  = @Fp,
                        last_seen_at = NOW(6),
                        ip_address   = @Ip,
                        user_agent   = COALESCE(@Ua, user_agent)
                    WHERE device_id = @Id AND tenant_id = @TenantId
                    """,
                    new { Id = existing, TenantId = tenantId, Fp = fingerprint, Ip = ipAddress, Ua = userAgent },
                    cancellationToken: ct));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[TenantDeviceService] QR 재등록 — 지문 갱신을 못 했다(다른 줄이 그 지문을 쓰는 중일 수 있다). "
                    + "등록 자체는 그대로 성공시킨다. device={DeviceId}", existing);
            }

            await MarkTokenUsedAsync(tokenId, existing, ct);

            // ⚠️ 이미 등록된 폰도 **아직 대기 중일 수 있다** — 그때도 누구에게 말할지 알려줘야 한다.
            return (true, "이미 등록된 기기입니다.", existing, await GetAdminContactAsync(tenantId, ct));
        }

        // 모바일 한도 확인 — QR 로 들어와도 슬롯 규칙은 같다.
        //   🔴 20260815작3 P1 — 계수와 한도를 단일 메서드로 모았다.
        //
        //   🔴 **이 경로는 휴대기기 칸만 본다. 컴퓨터 칸을 보면 안 된다** (P0 실측 D-1).
        //     QR 로 들어오는 것은 폰뿐이라 컴퓨터 한도와는 무관하다.
        //     단일 메서드가 두 값을 다 돌려주지만 **여기서는 모바일 값만 비교한다** —
        //     "일관성" 을 이유로 컴퓨터 한도까지 검사하게 만들면
        //     **종전에 통과하던 QR 등록이 갑자기 막힌다.**
        //     ⇒ 계수를 모으는 일은 호출부의 비교식까지 같게 만드는 일이 아니다.
        var (_, mobileLimit) = await GetLimitsAsync(tenantId, ct);
        var (_, mobileUsed) = await CountUsedSlotsAsync(tenantId, ct);

        if (mobileUsed >= mobileLimit)
        {
            // 🔴 한도가 찼을 때야말로 **대표에게 말해야 하는 자리**다 — 직원 혼자서는 못 푼다.
            //   슬롯을 늘리거나 안 쓰는 기기를 해제하는 것은 대표만 할 수 있다.
            return (false, "인증기기 한도초과. 관리자에게 문의하세요.", null,
                await GetAdminContactAsync(tenantId, ct));
        }

        // 🔴 2026-08-16 20260816작2 — **QR 도 대표 승인을 거친다** (사장님 전결).
        //
        //   [뒤집은 결재] 1차에서는 *"QR 을 띄운 것 자체가 대표의 승인"* 으로 결재받아
        //     바로 approved 로 넣었다. 사장님이 2026-08-16 에 이것을 뒤집으셨다 —
        //     *"PC환경 절차와 같은 절차가 있어야 함."*
        //
        //   [왜] QR 이 화면에 떠 있는 10분 동안 **옆 사람 폰이 찍어도 등록**된다.
        //     대표가 "내가 등록시키려던 그 폰이 맞나" 를 확인할 자리가 없었다.
        //     ⇒ 폰도 대기줄에 서고, 대표 화면에서 확인번호를 대조한 뒤 [예] 를 누른다.
        //
        //   🔴 2026-08-18 20260818작2 (2-4) — **스위치를 안 본다. 항상 대기줄이다.**
        //
        //     [여기 있던 옛 문장] *"승인제가 꺼져 있으면 종전 그대로 즉시 approved 다(개발·시험 편의)."*
        //
        //     [왜 없앴나] 이 경로는 **`[AllowAnonymous]`** 다. PC 는 **로그인을 통과한 뒤**
        //       관문 앞에 서는데, QR 은 **로그인 없이** 들어온다.
        //       ⇒ 더 엄해야 할 자리가 **더 느슨했다.** 개발 편의로 열 문이 아니다.
        //
        //     ⚠️ **개발·시험 편의가 사라진다.** 스위치를 꺼도 QR 은 대기줄에 선다.
        //       시험은 **승인 API(ApproveAsync)를 불러** 통과시킨다 — 스위치로 우회하지 않는다.
        //       (되돌리려면 이 한 줄만 고치면 되도록 별도 자리에 둔다 — 작업지시서 §6 롤백 2순위)
        const string qrStatus = "pending";

        // 🔴 2026-08-18 20260818작2 (2-6) — **저장은 세밀하게, 과금은 단순하게.**
        //
        //   [무엇이 문제였나] 아래 INSERT 는 종류를 **`'mobile'` 리터럴로 고정**했다.
        //     태블릿이 QR 로 들어와도 저장값은 mobile 이었다.
        //     ⇒ 사장님이 나중에 *"태블릿은 따로 받자"* 하시면 **과거 자료가 없어 못 간다.**
        //
        //   [고침] 서버가 판정한 값을 **그대로 저장**한다. tablet 은 tablet 으로 남는다.
        //     🟢 DDL 변경 0 — `device_type varchar(10)` 이고 주석에 이미 `pc / mobile / tablet` 이 있다.
        //
        //   🔴 [과금은 한 칸 그대로 — 사장님 결재 3 *"테블렛,모바일 같이 씀"*]
        //     `MobileUsedFrom` 이 `mobile` 과 `tablet` 을 **함께 센다.** 계수 로직은 한 글자도 안 건드렸다.
        //     ⇒ 칸이 셋으로 늘지 않는다. 가격표 2칸 구조 그대로다(작업지시서 §8).
        //
        //   ⚠️ QR 은 폰이 찍는 경로라 pc 가 올 일이 없다 — 그래도 **판정 결과를 믿지 않고**
        //     휴대기기 칸(mobile/tablet)으로 좁힌다. 컴퓨터가 이 문으로 슬쩍 들어와
        //     **모바일 한도만 통과하고 컴퓨터 칸을 공짜로 먹는** 일이 없어야 한다.
        var qrType = ResolveDeviceType(null, userAgent);
        if (qrType != "tablet") qrType = "mobile";

        var deviceId = Guid.NewGuid().ToString();

        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO tenant_devices
              (device_id, tenant_id, user_id, device_type, device_name,
               fingerprint, ip_address, user_agent, status,
               registered_at, approved_by, approved_at, last_seen_at)
            VALUES
              (@Id, @TenantId, NULL, @Type, @Name,
               @Fp, @Ip, @Ua, @Status,
               NOW(6), NULL, NULL, NOW(6))
            """,
            new
            {
                Id = deviceId,
                TenantId = tenantId,
                Type = qrType,
                Name = string.IsNullOrWhiteSpace(deviceName) ? "모바일 기기" : deviceName,
                Fp = fingerprint,
                Ip = ipAddress,
                Ua = userAgent,
                Status = qrStatus

                // 🔴 2026-08-18 20260818작2 (2-4 · 2-5) — `approved_by` · `approved_at` 은 **항상 NULL** 이다.
                //
                //   [옛 코드] `By = _approvalEnabled ? null : issuedBy` — 스위치가 꺼져 있으면
                //     QR 을 발급한 사람을 **승인자로 기록**했다. 그러나 그 사람은 승인한 적이 없다.
                //     QR 을 띄웠을 뿐이고, 그 사이 **누가 찍었는지 모른다.**
                //   [지금] 위 (2-4)로 상태가 항상 `pending` 이므로 승인자가 있을 수 없다.
                //     대표가 [예] 를 누르는 순간 ApproveAsync 가 채운다.
                //
                //   🔴 (2-5) `user_id` 도 NULL 이다 — **등록 시점엔 누구 폰인지 모른다.**
                //     대표가 승인하며 사람을 고른다(ApproveAsync 의 assignUserId).
            }, cancellationToken: ct));

        await MarkTokenUsedAsync(tokenId, deviceId, ct);
        await _audit.LogAsync("register", "device", deviceId,
            afterJson: $"{{\"type\":\"{qrType}\",\"via\":\"qr\"}}", ct: ct);

        // 🔴 2026-08-18 20260818작2 (2-4) — 상태가 **항상 대기**이므로 안내도 하나다.
        //   종전엔 스위치를 보고 두 문장으로 갈렸는데, 이제 갈릴 일이 없다.
        //   ⚠️ 여기서 스위치를 다시 보면 **화면과 표가 어긋난다** — 표는 pending 인데
        //     폰에는 "등록되었습니다" 가 뜨고, 직원은 되는 줄 알고 기다리지 않는다.
        return (true, "등록을 요청했습니다. 대표님이 허락하면 바로 쓸 수 있습니다.", deviceId,
            await GetAdminContactAsync(tenantId, ct));
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
    //
    // 🔴 2026-08-18 20260818작1 (1-2) — **헤더 통과를 메인PC 한 줄로 좁혔다.**
    //
    //   [무엇이 문제였나] 종전은 `status='approved'` 만 봤다.
    //     그런데 `device_id` 는 **비밀이 아니다** — 기기 목록 화면에 보이고,
    //     `gate-status` **쿼리스트링(서버 로그에 평문)** 에 실리며, 브라우저 저장소에 그대로 있다.
    //     ⇒ 승인된 아무 기기의 번호나 헤더에 넣으면 **그것만으로 문이 열렸다.**
    //
    //   🔴 [무엇을 고친 것인가 — 표현을 정확히 한다 · [3-V] V-04]
    //     이것은 **"도용 차단" 이 아니다.** 번호가 비밀이 아닌 이상 그 번호를 손에 넣은 자는
    //     **여전히 통과한다.** 이 봉합이 하는 일은 **통과 가능한 범위를 메인PC 한 줄로 좁히는 것**이다.
    //     ⚠️ 이것을 "도용을 막았다" 고 적으면 **거짓봉합**이 된다. 남은 구멍(기기별 비밀값)은
    //       다음 차수 몫이고, G-32-d 가 그 사실을 값으로 세워 두었다.
    //
    //   [왜 하필 메인PC 인가] 이 헤더 통과 길은 **메인PC 를 구하려고** 낸 길이다(8/16 P0).
    //     메인PC 는 인증키를 **받은 적이 없다** — 인증키는 대표가 *다른* 기기를 승인할 때
    //     생기는 값이고, 메인PC 는 MainPcRegistrationService 가 스스로 등록하기 때문이다.
    //     ⇒ 메인PC 에겐 **이 길 말고 다른 길이 없다.**
    //     나머지 기기는 **인증키라는 제 길**이 있으므로 이 길을 열어 둘 이유가 없다.
    //
    //   ⚠️ 조건은 **AND** 다 — "메인PC **이면서** 승인됨". 메인PC 표식이 승인 검사를 덮지 않는다.
    public async Task<bool> IsDeviceAllowedAsync(string deviceId, string tenantId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(deviceId)) return false;
        await EnsureOpenAsync(ct);
        var ok = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*)
            FROM tenant_devices
            WHERE device_id = @Id
              AND tenant_id = @TenantId
              AND status = 'approved'
              AND is_main_pc = 1
            """,
            new { Id = deviceId, TenantId = tenantId }, cancellationToken: ct));
        return ok > 0;
    }

    // ── 🔴 관문용: 이 기기가 승인됐는가 ── 20260818작1
    //
    //   🔴 **IsDeviceAllowedAsync 와 묻는 것이 다르다. 합치면 안 된다.**
    //
    //   [두 물음이 다르다]
    //     · IsDeviceAllowedAsync  = *"이 헤더만으로 문을 열어도 되나"*  → **메인PC 만** (1-2)
    //     · IsDeviceApprovedAsync = *"이 기기가 승인은 났나"*          → **모든 승인 기기**
    //
    //   [왜 갈랐나 — 안 가르면 그 자리에서 P0 다]
    //     관문(gate-status)이 1-2 로 좁힌 판정을 쓰면, **승인받은 평범한 직원 기기가
    //     영원히 "승인 대기" 화면에 갇힌다.** 대표가 [예] 를 눌러도 화면이 안 넘어간다 —
    //     메인PC 가 아니라서 false 가 돌아오기 때문이다.
    //     ⇒ 정확히 **8/10 사고와 같은 모양**(규칙을 좁혀서 쓰던 사람이 막힘)이 된다.
    //
    //   ⚠️ 이 메서드는 **문을 열지 않는다.** 화면에게 상태를 알려줄 뿐이다.
    //     업무 API 를 통과시키는 판정은 여전히 인증키(미들웨어)와 IsDeviceAllowedAsync 가 한다.
    public async Task<bool> IsDeviceApprovedAsync(string deviceId, string tenantId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(deviceId)) return false;
        await EnsureOpenAsync(ct);
        var status = await _db.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT status FROM tenant_devices WHERE device_id = @Id AND tenant_id = @TenantId LIMIT 1",
            new { Id = deviceId, TenantId = tenantId }, cancellationToken: ct));
        return status == "approved";
    }

    /// 🔴 이 기기가 **폐기됐다가 돌아온 기기인가** (20260820작3 · 화면 문구용).
    ///
    ///   사장님 문구: *"폐기된 기기입니다. **관리자의 재승인이 필요합니다.**"*
    ///   그냥 처음 등록하는 대기와 **말이 달라야** 직원이 상황을 안다.
    ///
    ///   ⚠️ **문을 열거나 닫지 않는다.** 화면이 어떤 문장을 보여줄지만 정한다 —
    ///     통행·승인 판정에 이 값을 쓰면 안 된다(문지기는 그대로 대표다).
    ///   ⚠️ 상태 컬럼이 아니라 **감사기록**을 본다. 회귀하면서 `revoked_at` 을 비웠기 때문이고,
    ///     기록은 지우지 않으므로 *"폐기였다"* 는 사실이 남아 있다(헌법 #1).
    public async Task<bool> WasRevokedBeforeAsync(string deviceId, string tenantId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(deviceId)) return false;
        await EnsureOpenAsync(ct);

        return (await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM audit_trail
            WHERE tenant_id = @TenantId AND entity_type = 'device' AND entity_id = @Id
              AND action_type = 'device_reapply_after_revoke'
            """,
            new { Id = deviceId, TenantId = tenantId }, cancellationToken: ct))) > 0;
    }

    // ══════════════════════════════════════════════════════════════════
    // 슬롯 계수·한도 — 🔴 여기가 유일한 자리다 (20260815작3 P1·P2)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 지금 몇 슬롯을 쓰고 있는가 — <b>슬롯을 세는 유일한 자리</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>종전엔 이 SQL 이 네 곳에 복제돼 있었다</b>(:97 · :245 · :410 · :597).
    /// 네 곳이 <b>모양까지 서로 달라서</b> 한 곳을 고쳐도 나머지가 옛 규칙으로 돌았다.
    /// 요금이 걸린 계산이라 갈리면 안 된다.
    ///
    /// <para>
    /// ⚠️ <b>세는 규칙 — 바꾸면 요금이 샌다. P0 스냅샷 ①-4 가 봉인한 현행이다.</b>
    /// </para>
    /// <list type="bullet">
    /// <item><b><c>status='approved'</c> 만 센다.</b> pending·revoked 는 안 센다.
    ///   🔴 <c>pending</c> 을 넣으면 <see cref="ApproveAsync"/> 가 <b>영원히 막힌다</b> —
    ///   승인하려는 그 기기가 자기 자신을 세어 버려 한도가 항상 꽉 찬 것으로 보인다
    ///   (P0 실측 D-4). I-7 게이트가 이것을 지킨다.</item>
    /// <item><b><c>tablet</c> 은 휴대기기 칸에 합산한다.</b> 사장님 판정 — 컴퓨터 운영체제가 아니다.
    ///   🔴 등호(<c>= 'mobile'</c>)로 비교하면 <b>태블릿이 어느 칸에도 안 잡혀 공짜로 쓰인다.</b>
    ///   G-13 게이트가 이것을 지킨다.</item>
    /// <item>🔴 <b><c>is_main_pc</c> 를 빼지 않는다.</b> 메인PC 도 1대로 센다(사장님 확정).
    ///   <c>AND is_main_pc = 0</c> 을 넣으면 <b>적게 세어 요금이 샌다</b>(P0 실측 D-3).</item>
    /// </list>
    /// </remarks>
    /// <returns>(컴퓨터 사용 대수, 휴대기기 사용 대수)</returns>
    private async Task<(int pcUsed, int mobileUsed)> CountUsedSlotsAsync(string tenantId, CancellationToken ct)
    {
        await EnsureOpenAsync(ct);

        // 한 번의 조회로 두 칸을 다 만든다. 칸별로 따로 물으면 그 사이에 값이 바뀔 수 있다.
        var counts = (await _db.QueryAsync<(string t, int c)>(new CommandDefinition(
            """
            SELECT device_type AS t, COUNT(*) AS c
            FROM tenant_devices
            WHERE tenant_id = @TenantId AND status = 'approved'
            GROUP BY device_type
            """,
            new { TenantId = tenantId }, cancellationToken: ct))).ToList();

        return (PcUsedFrom(counts), MobileUsedFrom(counts));
    }

    /// <summary>컴퓨터 칸으로 세는 것 — <c>pc</c> 하나뿐이다.</summary>
    /// <remarks>⚠️ internal 인 이유는 시험이 직접 부르기 때문이다(G-13 · I-7).</remarks>
    internal static int PcUsedFrom(IEnumerable<(string t, int c)> counts)
        => counts.Where(x => x.t == "pc").Sum(x => x.c);

    /// <summary>
    /// 휴대기기 칸으로 세는 것 — <c>mobile</c> 과 <c>tablet</c> <b>둘 다</b>.
    /// 🔴 <c>tablet</c> 을 빼면 그 기기가 공짜로 쓰인다(G-13).
    /// </summary>
    /// <remarks>⚠️ internal 인 이유는 시험이 직접 부르기 때문이다(G-13).</remarks>
    internal static int MobileUsedFrom(IEnumerable<(string t, int c)> counts)
        => counts.Where(x => x.t == "mobile" || x.t == "tablet").Sum(x => x.c);

    /// <summary>
    /// 이 회사가 쓸 수 있는 한도는 몇 대인가 — <b>한도를 만드는 유일한 자리</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>종전엔 한도 계산도 네 곳에 복제돼 있었다</b>(:93-94 · :242-243 · :407-408 · :595).
    /// 계수만 모으고 한도를 안 모으면 <b>여전히 네 곳이 갈린다</b>(P0 실측 D-7).
    ///
    /// <para>
    /// 🔴 <b>숫자는 코드에 없다</b> — <c>device_slot_policy_settings</c>(DB-104)에서 읽는다.
    /// 요금제는 사업이 정하는 것이라 <b>고칠 때 재배포가 필요하면 안 된다</b>(헌법 #11).
    /// 상수로 옮기는 것은 설정화가 아니다 — 자리만 옮긴 것이다(DB-96 이 같은 판단을 했다).
    /// </para>
    ///
    /// <para>
    /// 추가슬롯: <b>1개 = 컴퓨터 +1 · 휴대기기 +1</b> (사장님 확정 "추가슬롯 1+1당 1만원").
    /// ⚠️ 종전 코드는 휴대기기에 <c>extra * 2</c> 를 줬다. 배수도 설정에서 읽는다.
    /// </para>
    /// </remarks>
    private async Task<(int pcLimit, int mobileLimit)> GetLimitsAsync(string tenantId, CancellationToken ct)
    {
        await EnsureOpenAsync(ct);

        var tenant = await _db.QueryFirstOrDefaultAsync<(string? tier, int extra)>(new CommandDefinition(
            "SELECT subscription_tier AS tier, COALESCE(extra_device_slots, 0) AS extra FROM local_subscription WHERE tenant_id = @TenantId",
            new { TenantId = tenantId }, cancellationToken: ct));

        // 설정표를 한 번에 읽는다 — 열쇠마다 따로 물으면 왕복이 늘어난다.
        var settings = await LoadSlotPolicyAsync(tenantId, ct);

        return ResolveLimits(tenant.tier, tenant.extra, settings);
    }

    /// <summary>
    /// 설정값 + 요금제 + 추가슬롯 → <b>실제 한도</b>. 판정의 전부가 여기 있다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>DB 를 타지 않는 순수 계산</b>으로 떼어 둔 이유는 <b>시험 때문</b>이다(G-8).
    ///
    /// <para>
    /// 작업지시서 §5 가 고정했다 — <i>"코드에 리터럴이 없는지"</i> 를 보는 게이트는
    /// <b>상수를 옮기면 통과하는 가짜</b>다. 8/15 메신저 사고(<c>ChatWindowGuardTests</c> 가
    /// 글자만 봐서 통과시켰다)를 근거로 든 지적이다.
    /// ⇒ 게이트가 물어야 할 것은 <b>"설정값을 바꾸면 실제 한도가 바뀌는가"</b> 하나다.
    /// 그러려면 시험이 이 판정을 <b>직접 부를 수 있어야</b> 한다.
    /// </para>
    /// </remarks>
    /// <param name="rawTier">저장된 요금제 이름 원문(정규화 전).</param>
    /// <param name="extraSlots">구매한 추가슬롯 개수.</param>
    /// <param name="settings">DB-104 설정표. 비어 있으면 종전 숫자로 떨어진다.</param>
    internal static (int pcLimit, int mobileLimit) ResolveLimits(
        string? rawTier, int extraSlots, IReadOnlyDictionary<string, int> settings)
    {
        var tier = NormalizeTier(rawTier);
        var (fbPc, fbMobile) = FallbackLimits(tier);

        var pcLimit     = Pick(settings, $"tier.{tier}.pc_limit",     fbPc);
        var mobileLimit = Pick(settings, $"tier.{tier}.mobile_limit", fbMobile);

        // 🔴 1+1 — 사장님 확정. 배수도 설정값이라 사업이 바뀌면 값만 갈아끼운다.
        var pcPerSlot     = Pick(settings, "extra_slot.pc_per_slot",     1);
        var mobilePerSlot = Pick(settings, "extra_slot.mobile_per_slot", 1);

        pcLimit     += extraSlots * pcPerSlot;
        mobileLimit += extraSlots * mobilePerSlot;

        return (pcLimit, mobileLimit);
    }

    /// <summary>기기 슬롯 기준값 표(DB-104)를 통째로 읽는다.</summary>
    /// <remarks>
    /// ⚠️ 표가 아직 없는 DB(마이그레이션 전)에서도 죽지 않아야 한다 —
    /// 로그인 경로에서 불리므로 여기서 던지면 <b>고객이 로그인을 못 한다.</b>
    /// 못 읽으면 빈 사전을 돌려주고 호출부가 종전 숫자로 떨어진다.
    /// </remarks>
    private async Task<Dictionary<string, int>> LoadSlotPolicyAsync(string tenantId, CancellationToken ct)
    {
        try
        {
            var rows = await _db.QueryAsync<(string k, int v)>(new CommandDefinition(
                """
                SELECT policy_key AS k, policy_value AS v
                FROM device_slot_policy_settings
                WHERE tenant_id = @TenantId
                """,
                new { TenantId = tenantId }, cancellationToken: ct));

            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in rows) map[k] = v;
            return map;
        }
        catch (Exception ex)
        {
            // 🔴 헌법 #15 — 빈 catch 금지. 왜 종전 숫자로 떨어졌는지 남긴다.
            //   ⚠️ 종전엔 Debug.WriteLine 이었다 — Release 에서 사라져 **운영에 아무 기록도 안 남았다**(B-5).
            //     운영에서 사라지는 기록은 기록이 아니다. 요금이 걸린 신호라 더욱.
            _logger.LogWarning(ex,
                "[TenantDeviceService] 기기 슬롯 기준값 표(device_slot_policy_settings)를 못 읽었다 — "
                + "종전 숫자(안전망)로 진행한다. tenant={TenantId}", tenantId);
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static int Pick(IReadOnlyDictionary<string, int> settings, string key, int fallback)
        => settings.TryGetValue(key, out var v) ? v : fallback;

    /// <summary>
    /// 요금제 이름을 설정 열쇠로 쓸 수 있게 정리한다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b><c>enterprise</c> 가 실제로 발급되는 최상위 값이다</b>(명세서 §17-4 · PI-14).
    /// 코드만 <c>premium</c> 이라 쓰고 있었고 DDL·시리얼 발급·백오피스는 전부 <c>enterprise</c> 라,
    /// 최상위 고객이 갈래를 못 찾아 <b>basic 한도로 떨어지고 있었다.</b>
    /// ⚠️ <c>premium</c> 도 살려 둔다 — 옛 데이터에 남아 있을 수 있다(P0 실측 D-16).
    /// 모르는 값은 <c>default</c> 로 보내 종전 <c>_ =&gt; (5,3)</c> 과 같게 만든다.
    /// </remarks>
    private static string NormalizeTier(string? tier)
    {
        var t = (tier ?? "basic").Trim().ToLowerInvariant();
        return t switch
        {
            "basic" or "pro" or "enterprise" or "premium" or "trial" => t,
            _ => "default"
        };
    }

    /// <summary>
    /// 설정표를 못 읽었을 때 쓰는 숫자 — <b>종전 코드와 한 글자도 다르지 않다.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ 이것은 <b>설정의 대체물이 아니라 안전망</b>이다. 마이그레이션이 아직 안 돈 DB 나
    /// 표가 비어 있는 회사에서 <b>로그인이 죽지 않게</b> 하는 것이 유일한 목적이다.
    /// 🔴 이 숫자를 고쳐서 요금제를 바꾸려 하면 안 된다 — 설정표(DB-104)를 고쳐야 한다.
    /// 정상 경로에서는 이 값이 <b>쓰이지 않는다</b>(G-8 이 그것을 시험한다).
    /// </para>
    /// <para>
    /// 🔴 <b>2026-08-16 (20260816작1) — 숫자를 여기 적지 않는다.</b>
    /// 종전엔 이 <c>switch</c> 안에 <c>(5,3)·(10,8)…</c> 이 직접 적혀 있어,
    /// 회사 생성 시드(<see cref="SlotPolicyDefaults"/>)와 <b>같은 숫자가 두 벌</b>이 됐다.
    /// 한쪽만 고치면 <b>신규 고객과 안전망이 서로 다른 한도로 돈다</b> —
    /// 이 차수가 계수 8곳을 모으며 없앤 바로 그 병이다.
    /// ⇒ 안전망도 <b>시드와 같은 정의를 읽는다.</b> 갈라질 수가 없다(G-20).
    /// </para>
    /// </remarks>
    private static (int pc, int mobile) FallbackLimits(string normalizedTier)
    {
        // 모르는 요금제는 NormalizeTier 가 이미 "default" 로 보낸다(종전 `_ => (5,3)` 과 동일).
        var pc     = SlotPolicyDefaults.Value($"tier.{normalizedTier}.pc_limit");
        var mobile = SlotPolicyDefaults.Value($"tier.{normalizedTier}.mobile_limit");
        return (pc, mobile);
    }

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

    /// 기기 종류를 저장 가능한 값으로 정리한다.
    ///
    /// 🔴 2026-08-11 20260811작2 (사장님 판정 기준 확정):
    ///   *"태블릿도 모바일로 잡으면 됨 — **운영체제가 안드로이드이거나 iOS이기 때문에**"*
    ///   *"**태블릿, 폰, 스마트TV = 모바일** · 운영체제로 구분하면 됨"*
    ///   *"**윈도우, 맥OS, 리눅스 등의 PC기반 운영체제가 아닌 것은 모두 모바일**"*
    ///
    ///   [왜 운영체제로 가르나] 화면 크기나 터치 여부로 가르면 경계가 계속 흔들린다.
    ///     터치 되는 노트북, 화면 큰 태블릿, 데스크톱 화면을 요청한 아이패드…
    ///     끝이 없다. 그러나 **운영체제는 흔들리지 않는다.**
    ///     Windows·Mac·리눅스는 책상에 두고 쓰는 컴퓨터의 운영체제이고, 나머지는 아니다.
    ///
    ///   ⇒ 그래서 칸도 **둘뿐**이다: 휴대기기(mobile) · 컴퓨터(pc).
    ///     'tablet' 은 받아주되 **모바일로 흡수**한다 — 요금 계산이 이미 둘을 같은 칸에
    ///     합산하고 있었으므로(아래 mobileUsed), 굳이 셋으로 나눠 부를 이유가 없다.
    ///
    ///   🔴 [모르는 값은 휴대기기로 본다] 컴퓨터 칸이 더 비싸다. 판정이 애매할 때
    ///     컴퓨터로 세면 **고객이 쓰지도 않은 자리에 돈을 낸다.** 반대로 세면 우리가 조금
    ///     손해 볼 뿐이다. ⇒ 애매한 것은 **고객에게 유리한 쪽**으로 보낸다.
    ///     (클라이언트도 같은 방향으로 판정한다 — device-fingerprint.js getDeviceType)
    ///
    ///   ⚠️ null 을 돌려주는 경우: 클라이언트가 종류를 **안 보냈을 때**.
    ///     갱신 경로에서 COALESCE 로 받아 **기존 값을 지우지 않게** 하기 위함이다.
    ///
    ///   🔴 2026-08-15 20260815작3 P1 (I-6) — **이 자리 주석이 낡았다. 정정한다.**
    ///     [옛 문장] *"신규 등록 경로는 호출부에서 ?? "pc" 로 받는다"*
    ///     [지금] 신규 등록 경로는 **`?? "mobile"`** 로 받는다. 폴백을 싼 칸으로 돌렸다.
    ///     컴퓨터 칸이 더 비싸므로, 종류를 모를 때 컴퓨터로 세면 고객이 쓰지도 않은 자리에
    ///     돈을 낸다. ⇒ 값이 아예 없든 모르는 값이든 **똑같이 휴대기기**로 간다.
    /// <remarks>⚠️ internal 인 이유는 시험이 직접 부르기 때문이다(I-6).</remarks>
    internal static string? NormalizeDeviceType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var t = raw.Trim().ToLowerInvariant();

        // 🔴 2026-08-18 20260818작2 (2-6) — **`tablet` 을 더 이상 뭉개지 않는다.**
        //
        //   [옛 코드] `if (t is "tablet") return "mobile";` — 저장까지 mobile 로 덮었다.
        //   [지금] `tablet` 은 `tablet` 으로 남는다.
        //
        //   🔴 **과금은 한 글자도 안 바뀐다** (사장님 결재 3 — *"테블렛,모바일 같이 씀"*).
        //     칸을 세는 자리는 MobileUsedFrom 하나뿐이고, 그 함수가 `mobile` 과 `tablet` 을
        //     **원래부터 함께 세고 있었다.** ⇒ 저장만 갈라지고 칸 수는 그대로 둘이다.
        //
        //   ⚠️ 이 값을 **셋째 칸으로 착각하지 마라.** 가격표는 2칸이다(작업지시서 §8).
        //     저장을 가르는 이유는 하나뿐 — 나중에 *"태블릿은 따로 받자"* 하실 때
        //     **과거 자료가 있어야** 갈 수 있기 때문이다.
        if (t is "tablet") return "tablet";
        if (t is "mobile" or "pc") return t;

        // 모르는 값은 휴대기기로 본다 — 비싼 칸을 잘못 깎지 않는 쪽
        return "mobile";
    }

    /// <summary>
    /// 🔴 <b>기기 종류를 서버가 정한다</b> — 클라이언트 신고값을 <b>그대로 믿지 않는다</b> (20260818작2 · [3-V] V-05).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>진짜 구멍은 승격이 아니라 최초 등록이었다.</b>
    /// 2-1 은 <c>mobile → pc</c> 승격에 한도를 다시 보게 만든다. 그런데 <b>공격자는 승격할 이유가 없다</b> —
    /// 처음부터 <c>mobile</c> 이라고 신고하고 <b>안 바꾸면 그만</b>이다. 컴퓨터 한도가 0이어도
    /// 컴퓨터가 휴대기기 칸으로 무제한 들어온다.
    /// ⇒ <b>2-1 만 하면 게이트는 초록이고 구멍은 그대로다.</b> 그것을 막으려고 이 함수가 있다.
    /// </para>
    /// <para>
    /// [무엇을 하나] 클라이언트가 <c>DeviceType</c> 을 신고하지만, 그 값은 브라우저 안의 스크립트가
    /// 만든 것이라 <b>사람이 손으로 바꿔 보낼 수 있다.</b> 반면 <c>User-Agent</c> 는 브라우저가
    /// 스스로 붙여 서버가 <b>직접</b> 읽는 값이다(<c>Request.Headers.UserAgent</c>).
    /// ⇒ 둘이 <b>어긋나면 서버 판정을 쓴다.</b>
    /// </para>
    /// <para>
    /// ⚠️ <b>완벽을 약속하지 않는다.</b> User-Agent 도 마음만 먹으면 바꿀 수 있다.
    /// 이 함수가 하는 일은 <b>"신고값을 그대로 믿지 않는 것"</b> 하나다 —
    /// 화면 조작만으로 칸을 고르던 것을, <b>헤더까지 함께 위조해야</b>만 되게 바꾼다.
    /// 🔴 이것을 <i>"위조를 막았다"</i> 고 적으면 <b>거짓봉합</b>이다.
    /// </para>
    /// <para>
    /// 🔴 <b>판정 순서는 클라이언트(<c>device-fingerprint.js getDeviceType</c>)와 똑같이 간다.</b>
    /// 두 곳이 다른 순서를 쓰면 <b>정상 기기가 어긋남으로 잡혀</b> 멀쩡한 고객의 칸이 바뀐다.
    /// 특히 <b>휴대기기를 먼저 걷어낸다</b> — 아이폰·아이패드는 <c>like Mac OS X</c> 를,
    /// 안드로이드는 <c>Linux</c> 를 달고 오기 때문이다.
    /// </para>
    /// <para>
    /// ⚠️ <b>어긋남이 없으면 신고값을 존중한다.</b> User-Agent 로 못 가리는 것(태블릿 세부 구분 등)까지
    /// 서버가 뭉개면, 클라이언트가 애써 판정한 정보를 잃는다.
    /// </para>
    /// </remarks>
    /// <param name="claimed">클라이언트가 신고한 종류. 안 보냈으면 <c>null</c>.</param>
    /// <param name="userAgent">서버가 직접 읽은 <c>User-Agent</c>. 없으면 <c>null</c>.</param>
    internal static string ResolveDeviceType(string? claimed, string? userAgent)
    {
        var normalized = NormalizeDeviceType(claimed);
        var judged = JudgeTypeFromUserAgent(userAgent);

        // User-Agent 가 없거나 못 가리면 서버가 할 말이 없다 — 종전 규칙 그대로.
        //   🔴 그래도 `?? "mobile"` 로 끝낸다(8/16 CR2-2 종결 — 싼 칸이 고객에게 유리하다).
        if (judged is null) return normalized ?? "mobile";

        // 신고를 안 했으면 서버 판정을 쓴다.
        if (normalized is null) return judged;

        // 🔴 **어긋나면 서버가 이긴다.** 여기가 V-05 의 본체다.
        //   컴퓨터로 보이는데 휴대기기라고 신고했다 ⇒ 컴퓨터로 센다(요금이 새지 않는다).
        //   휴대기기로 보이는데 컴퓨터라고 신고했다 ⇒ 휴대기기로 센다(고객이 덜 낸다).
        var normalizedIsPc = normalized == "pc";
        var judgedIsPc = judged == "pc";
        if (normalizedIsPc != judgedIsPc) return judged;

        // 어긋나지 않는다 — 신고값을 존중한다(tablet 같은 세부 구분을 잃지 않기 위해서다).
        return normalized;
    }

    /// <summary>
    /// 🔴 <b>서버가 <c>User-Agent</c> 만 보고 종류를 가른다</b> (20260818작2 · [3-V] V-05).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>순서가 전부다.</b> <c>device-fingerprint.js</c> 의 <c>getDeviceType</c> 과
    /// <b>같은 순서</b>를 쓴다 — 두 곳이 갈리면 정상 기기가 어긋남으로 잡힌다.
    /// </para>
    /// <para>
    /// ⚠️ <b>못 가리면 <c>null</c> 을 돌려준다.</b> 억지로 하나를 고르면
    /// <b>모르는 것을 아는 척</b>하는 것이고, 그 값이 고객의 칸을 바꾼다.
    /// 모르면 신고값을 존중하는 쪽이 맞다.
    /// </para>
    /// <para>
    /// ⚠️ <b>서버는 손가락 터치를 알 수 없다.</b> 클라이언트는 <c>maxTouchPoints</c> 로
    /// Mac 으로 위장한 아이패드를 잡아내지만(<c>_isTouchMac</c>), 그 값은 헤더에 없다.
    /// ⇒ 서버 눈에 아이패드는 <b>Mac(pc)</b> 으로 보인다. 그래서 클라이언트가 <c>mobile</c> 이라
    /// 신고하면 <b>어긋남으로 잡혀 pc 로 뒤집힐 위험</b>이 있다 —
    /// 🔴 그것이 정확히 <b>2026-08-10 사고</b>(아이패드가 컴퓨터 칸을 먹던 일)의 재발이다.
    /// ⇒ <b>Mac 계열은 판정하지 않고 <c>null</c> 로 비켜선다.</b> 아이패드는 클라이언트 말을 믿는다.
    /// </para>
    /// </remarks>
    internal static string? JudgeTypeFromUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return null;

        var ua = userAgent;

        // 1) 컴퓨터 운영체제와 헷갈리는 휴대기기부터 걷어낸다 — 클라이언트와 같은 순서다.
        //    ⚠️ 아이폰·아이패드는 "like Mac OS X" 를, 안드로이드는 "Linux" 를 달고 온다.
        if (Contains(ua, "iPhone") || Contains(ua, "iPad") || Contains(ua, "iPod")) return "mobile";
        if (Contains(ua, "Android")) return "mobile";

        // 2) 컴퓨터 운영체제인가 — Windows 와 책상 위 리눅스만 확신한다.
        if (Contains(ua, "Windows NT") || Contains(ua, "Win64") || Contains(ua, "Win32")) return "pc";
        if (Contains(ua, "X11") || Contains(ua, "Wayland") || Contains(ua, "CrOS")
            || Contains(ua, "Ubuntu") || Contains(ua, "Fedora")
            || Contains(ua, "FreeBSD") || Contains(ua, "OpenBSD")) return "pc";

        // 🔴 3) Mac 계열은 **판정하지 않는다.** 위 remarks 참조 —
        //    서버는 터치 여부를 못 봐서 아이패드와 책상 위 Mac 을 가를 수 없다.
        //    억지로 pc 라 하면 2026-08-10 사고가 재발한다.
        //
        //    ⚠️ 그 대가를 정확히 적는다: **Mac 을 사칭하면 이 검사를 빠져나간다.**
        //      막는 것은 다음 차수의 장비넘버(hardware_id) 몫이고, 여기서 약속하지 않는다.
        return null;
    }

    private static bool Contains(string haystack, string needle)
        => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

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
