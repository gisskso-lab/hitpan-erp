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
}

/// <summary>
/// 로그인 시 클라이언트가 전달하는 기기 정보.
/// - Fingerprint: 브라우저 기반 SHA-256 또는 간이 해시
/// - DeviceType: pc / mobile / tablet
/// </summary>
public class RegisterDeviceRequest
{
    public string Fingerprint { get; set; } = "";
    public string DeviceType { get; set; } = "pc";
    public string? DeviceName { get; set; }
    public string? UserAgent { get; set; }
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
