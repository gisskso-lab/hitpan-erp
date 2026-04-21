using HitPan.Application.DTOs.Device;

namespace HitPan.Application.Interfaces;

/// <summary>
/// 테넌트 기기(Device) 관리 서비스.
/// - 히트판은 디바이스 개수 과금 — 계정 수 무제한, 총 기기 수 제한.
/// - 신규 기기 등록 시 티어별 PC/모바일 한도 체크 후 자동 승인(MVP).
/// </summary>
public interface ITenantDeviceService
{
    /// <summary>테넌트의 전체 기기 목록 (TenantAdmin용).</summary>
    Task<List<DeviceListDto>> GetAllAsync(string tenantId, CancellationToken ct = default);

    /// <summary>남은 슬롯 정보 — KPI 카드 노출용.</summary>
    Task<DeviceQuotaDto> GetQuotaAsync(string tenantId, CancellationToken ct = default);

    /// <summary>
    /// 로그인 시 호출. 지문(fingerprint)이 있으면 last_seen_at 갱신,
    /// 없으면 신규 등록하며 티어별 한도 검사를 수행한다.
    /// - 허용되면 (true, "", deviceId), 거부되면 (false, 사유, null) 반환.
    /// </summary>
    Task<(bool allowed, string reason, string? deviceId)> RegisterOrRefreshAsync(
        string tenantId,
        string userId,
        RegisterDeviceRequest req,
        string ipAddress,
        CancellationToken ct = default);

    /// <summary>기기 폐기 (TenantAdmin만 호출) — status='revoked'.</summary>
    Task RevokeAsync(string deviceId, string tenantId, string userId, string? reason, CancellationToken ct = default);

    /// <summary>미들웨어용 — 해당 기기가 approved 상태인지 빠르게 확인.</summary>
    Task<bool> IsDeviceAllowedAsync(string deviceId, string tenantId, CancellationToken ct = default);
}
