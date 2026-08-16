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
        (string id, string status, bool isMainPc)? existing = null;

        if (!string.IsNullOrWhiteSpace(req.DeviceId))
        {
            existing = await _db.QueryFirstOrDefaultAsync<(string id, string status, bool isMainPc)?>(new CommandDefinition(
                """
                SELECT device_id AS id, status AS status, COALESCE(is_main_pc, 0) AS isMainPc
                FROM tenant_devices
                WHERE tenant_id = @TenantId AND device_id = @DeviceId
                LIMIT 1
                """,
                new { TenantId = tenantId, DeviceId = req.DeviceId }, cancellationToken: ct));
        }

        // 2순위 — 옛 기기(장비넘버를 아직 못 받은 기기)는 지문으로 찾는다.
        existing ??= await _db.QueryFirstOrDefaultAsync<(string id, string status, bool isMainPc)?>(new CommandDefinition(
            """
            SELECT device_id AS id, status AS status, COALESCE(is_main_pc, 0) AS isMainPc
            FROM tenant_devices
            WHERE tenant_id = @TenantId AND fingerprint = @Fp
            LIMIT 1
            """,
            new { TenantId = tenantId, Fp = req.Fingerprint }, cancellationToken: ct));

        if (existing is not null)
        {
            var (id, status, isMainPc) = existing.Value;

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
            if (status == "revoked" && !isMainPc)
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
            var normalizedType = NormalizeDeviceType(req.DeviceType);

            await _db.ExecuteAsync(new CommandDefinition(
                """
                UPDATE tenant_devices
                SET last_seen_at = NOW(),
                    ip_address   = @Ip,
                    user_agent   = COALESCE(@Ua, user_agent),
                    device_name  = COALESCE(@Name, device_name),
                    device_type  = COALESCE(@Type, device_type)
                WHERE device_id = @Id
                """,
                new
                {
                    Id = id,
                    Ip = ipAddress,
                    Ua = req.UserAgent,
                    Name = string.IsNullOrWhiteSpace(req.DeviceName) ? null : req.DeviceName,
                    Type = normalizedType
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
        var type = NormalizeDeviceType(req.DeviceType) ?? "mobile";

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
    //   반환값 = **새로 발급한 인증키 원문**. 이미 승인된 기기를 다시 누르면 null 이다
    //   (원문은 우리가 갖고 있지 않으므로 다시 알려줄 수 없다 — 재발급은 별건).
    public async Task<string?> ApproveAsync(string deviceId, string tenantId, string approverUserId, CancellationToken ct = default)
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

        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE tenant_devices
            SET status = 'approved',
                approved_by = @Approver,
                approved_at = NOW(),
                auth_key_hash = @KeyHash,
                auth_key_issued_at = NOW(6)
            WHERE device_id = @Id AND tenant_id = @TenantId
            """,
            new { Id = deviceId, TenantId = tenantId, Approver = approverUserId, KeyHash = Sha256Hex(authKey) },
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
    /// 반환: 맞으면 그 기기 번호, 틀리면 null
    public async Task<string?> VerifyAuthKeyAsync(string authKey, string tenantId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(authKey)) return null;

        await EnsureOpenAsync(ct);

        var deviceId = await _db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
            """
            SELECT device_id
            FROM tenant_devices
            WHERE tenant_id = @TenantId
              AND auth_key_hash = @KeyHash
              AND status = 'approved'
            LIMIT 1
            """,
            new { TenantId = tenantId, KeyHash = Sha256Hex(authKey) }, cancellationToken: ct));

        return deviceId;
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
            return (false, "인증기기 한도초과. 관리자에게 문의하세요.", null);

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
        //   ⚠️ 승인제가 꺼져 있으면 **종전 그대로** 즉시 approved 다(개발·시험 편의).
        var qrStatus = _approvalEnabled ? "pending" : "approved";

        var deviceId = Guid.NewGuid().ToString();
        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO tenant_devices
              (device_id, tenant_id, user_id, device_type, device_name,
               fingerprint, ip_address, user_agent, status,
               registered_at, approved_by, approved_at, last_seen_at)
            VALUES
              (@Id, @TenantId, NULL, 'mobile', @Name,
               @Fp, @Ip, @Ua, @Status,
               NOW(6), @By, @ApprovedAt, NOW(6))
            """,
            new
            {
                Id = deviceId,
                TenantId = tenantId,
                Name = string.IsNullOrWhiteSpace(deviceName) ? "모바일 기기" : deviceName,
                Fp = fingerprint,
                Ip = ipAddress,
                Ua = userAgent,
                Status = qrStatus,

                // 승인 대기로 들어가면 아직 승인자가 없다 — 대표가 [예] 를 누를 때 채워진다.
                //   ⚠️ 여기에 issuedBy 를 그냥 넣으면 **승인받지 않았는데 승인된 것처럼** 기록된다.
                By = _approvalEnabled ? null : issuedBy,
                ApprovedAt = _approvalEnabled ? (DateTime?)null : DateTime.Now
            }, cancellationToken: ct));

        await MarkTokenUsedAsync(tokenId, deviceId, ct);
        await _audit.LogAsync("register", "device", deviceId, afterJson: "{\"type\":\"mobile\",\"via\":\"qr\"}", ct: ct);

        // 폰 화면이 무엇을 안내할지 갈린다 — 대기면 "대표님 허락을 기다립니다" 를 보여줘야 한다.
        return _approvalEnabled
            ? (true, "등록을 요청했습니다. 대표님이 허락하면 바로 쓸 수 있습니다.", deviceId)
            : (true, "모바일 기기가 등록되었습니다.", deviceId);
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
    /// 작업지시서 §5 가 못 박았다 — <i>"코드에 리터럴이 없는지"</i> 를 보는 게이트는
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

        // 태블릿은 휴대기기 칸으로 흡수한다 (사장님 판정 — 컴퓨터 운영체제가 아니다)
        if (t is "tablet") return "mobile";
        if (t is "mobile" or "pc") return t;

        // 모르는 값은 휴대기기로 본다 — 비싼 칸을 잘못 깎지 않는 쪽
        return "mobile";
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
