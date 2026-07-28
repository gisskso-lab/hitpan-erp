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
