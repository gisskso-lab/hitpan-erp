namespace HitPan.Application.DTOs.Device;

/// <summary>
/// 등록 기기 목록 DTO — 설정·등록기기관리 화면에서 사용.
/// </summary>
public class DeviceListDto
{
    public string DeviceId { get; set; } = "";
    public string DeviceType { get; set; } = ""; // pc / mobile / tablet
    public string? DeviceName { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string? IpAddress { get; set; }
    public string Status { get; set; } = ""; // pending / approved / revoked
    public DateTime RegisteredAt { get; set; }
    public DateTime? LastSeenAt { get; set; }

    /// <summary>
    /// 메인PC(히트판 본체·DB 를 가진 그 PC) 여부. 테넌트당 1대. (20260810작3 · DB-86)
    ///
    /// 고객지원이 "그 PC 가 메인PC 인가" 를 화면으로 확인할 수 있어야 응대가 갈린다 —
    /// 메인PC 면 그 PC 를 살려야 하고, 클라이언트면 메인PC 가 꺼졌는지부터 물어야 한다.
    /// 메인PC 는 24시간 켜두기 좋은 자리로 추천되므로 대표자·담당자가 쓰는 PC 라는 보장이 없고,
    /// 설치를 맡았던 직원이 퇴사하면 표식 없이는 아무도 어느 PC 인지 모른다.
    /// </summary>
    public bool IsMainPc { get; set; }

    /// <summary>
    /// 🔴 등록 **확인번호** 4자리 — 대표가 눈으로 대조한다 (20260816작2 · 사장님 결재).
    ///
    /// 대표 화면: *"{기기이름} (인증번호 <b>4726</b>) 가 등록을 요청합니다"*
    /// 신청한 기기 화면에도 **같은 번호**가 뜬다.
    ///
    /// 기기 이름만으로는 사무실의 같은 기종 PC·폰 두 대가 구분되지 않는다.
    /// 구분이 안 되면 대표는 아무거나 누르게 되고 승인제가 무의미해진다.
    ///
    /// ⚠️ 승인 대기(pending)일 때만 값이 있다. 이미 승인된 기기는 대조할 일이 없다.
    /// ⚠️ 비밀번호가 아니다 — 계산으로 나오며 저장하지 않는다. 자세한 것은 <see cref="Common.DeviceConfirmCode"/>.
    /// </summary>
    public string? ConfirmCode { get; set; }
}

/// <summary>
/// 로그인 시 클라이언트가 전달하는 기기 정보.
/// - Fingerprint: 브라우저 기반 SHA-256 또는 간이 해시
/// - DeviceType: pc / mobile / tablet — <b>안 보내면 null</b>
/// </summary>
public class RegisterDeviceRequest
{
    public string Fingerprint { get; set; } = "";

    /// <summary>
    /// 기기 종류. <b>클라이언트가 안 보내면 <c>null</c></b> 이다.
    /// </summary>
    /// <remarks>
    /// 🔴 2026-08-15 20260815작3 P1 (I-6) — 기본값 <c>"pc"</c> 를 없앴다.
    ///
    /// <para>
    /// [무엇이 문제였나] 이 자리의 <c>= "pc"</c> 는 <b>세 번째 폴백</b>이었다.
    /// AuthController 와 TenantDeviceService 의 <c>?? "pc"</c> 두 곳을 고쳐도
    /// <b>여기가 살아 있으면 아무것도 안 바뀐다</b> — 역직렬화가 값을 안 채우면
    /// 이 기본값이 그대로 <c>"pc"</c> 로 들어가기 때문이다.
    /// </para>
    ///
    /// <para>
    /// [왜 null 인가] 종류를 모를 때 컴퓨터(비싼 칸)로 세면 <b>고객이 쓰지도 않은 자리에 돈을 낸다.</b>
    /// null 로 두면 판정이 <c>NormalizeDeviceType</c> 한 곳으로 모이고,
    /// 거기서 <b>모르는 값은 휴대기기</b>(고객에게 유리한 쪽)로 간다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ null 은 <b>갱신 경로에서도 뜻이 있다</b> — COALESCE 로 받아 <b>기존 종류를 지우지 않는다.</b>
    /// 종류를 못 보내는 옛 화면이 이미 등록된 기기의 칸을 덮어쓰지 않게 하는 장치다.
    /// </para>
    /// </remarks>
    public string? DeviceType { get; set; }

    public string? DeviceName { get; set; }
    public string? UserAgent { get; set; }

    /// <summary>
    /// 🔴 <b>장비넘버</b> — 이 기기가 지난번에 받아 보관해 둔 자기 번호 (20260816작2 · 명세서 §4-3·§4-4).
    ///
    /// <para>
    /// 값이 있으면 <b>지문보다 먼저</b> 이것으로 기존 기기를 찾는다.
    /// 지문은 브라우저가 바뀌면 달라지지만(<c>_envSeed</c> 가 userAgent 를 쓴다) 이 번호는 안 바뀐다
    /// ⇒ 같은 PC 에서 Edge ↔ Chrome 을 오가도 <b>한 줄</b>이다.
    /// </para>
    ///
    /// <para>⚠️ 처음 오는 기기·옛 기기는 <c>null</c> 이다 — 그때는 종전대로 지문으로 찾는다(호환).</para>
    /// </summary>
    public string? DeviceId { get; set; }
}

/// <summary>
/// 테넌트 기기 쿼터 정보 — KPI 카드에 노출.
/// </summary>
public class DeviceQuotaDto
{
    public int PcLimit { get; set; }
    public int MobileLimit { get; set; }
    public int PcUsed { get; set; }
    public int MobileUsed { get; set; }
    public int ExtraSlots { get; set; }
    public string SubscriptionTier { get; set; } = "";
}
