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
    /// - 허용되면 (true, "", deviceId, newlyRegistered), 거부되면 (false, 사유, null, false) 반환.
    /// - newlyRegistered: 이번 호출에서 처음 등록된 신규 기기면 true (작1 F3 첫 접속 안내용).
    /// </summary>
    Task<(bool allowed, string reason, string? deviceId, bool newlyRegistered)> RegisterOrRefreshAsync(
        string tenantId,
        string userId,
        RegisterDeviceRequest req,
        string ipAddress,
        CancellationToken ct = default);

    /// <summary>기기 폐기 (TenantAdmin만 호출) — status='revoked'.</summary>
    Task RevokeAsync(string deviceId, string tenantId, string userId, string? reason, CancellationToken ct = default);

    /// <summary>
    /// 기기 승인 — 대표계정(tenant_admin)만 (20260811작1 (B)).
    /// 사장님 설계: "승인대기. 대표에게 기기승인의 권한을 주기"
    ///
    /// pending → approved 로 바꾸고 누가 언제 승인했는지 남긴다.
    /// 승인 시점에 한도를 **다시 확인**한다 — 대기 중에 다른 기기가 슬롯을 채웠을 수 있다.
    /// </summary>
    /// <summary>기기 승인 (대표계정). 반환값 = 새로 발급한 인증키 원문 (이미 승인된 기기면 null).</summary>
    Task<string?> ApproveAsync(string deviceId, string tenantId, string approverUserId, CancellationToken ct = default);

    /// <summary>직원 PC 가 입력한 인증키를 대조한다. 맞으면 기기 번호, 틀리면 null.</summary>
    Task<string?> VerifyAuthKeyAsync(string authKey, string tenantId, CancellationToken ct = default);

    /// <summary>
    /// 기기 승인 거부 — 대표계정만 (20260811작1 (B)).
    /// pending → revoked. 사장님이 "모르는 기기다" 라고 판단한 경우다.
    /// </summary>
    Task RejectAsync(string deviceId, string tenantId, string approverUserId, string? reason, CancellationToken ct = default);

    /// <summary>미들웨어용 — 해당 기기가 approved 상태인지 빠르게 확인.</summary>
    Task<bool> IsDeviceAllowedAsync(string deviceId, string tenantId, CancellationToken ct = default);

    /// <summary>
    /// 모바일기기 등록 QR 토큰 발급 — 대표계정만 (20260811작1 (D)).
    /// 사장님 오더: "모바일 등록기기 버튼 클릭시 QR생성"
    ///
    /// 10분 만료·1회용. 평문 토큰은 저장하지 않고 SHA-256 해시만 남긴다.
    /// 돌려주는 평문은 QR 에 담기는 그 순간에만 존재한다.
    /// </summary>
    Task<string> IssueMobileRegisterTokenAsync(string tenantId, string issuerUserId, CancellationToken ct = default);

    /// <summary>
    /// QR 토큰으로 모바일기기 등록 (20260811작1 (D)).
    /// 사장님 설계: QR 을 띄운 것 자체가 대표계정의 승인이므로 **별도 승인 단계 없이 바로 등록**된다.
    ///
    /// 반환: (성공 여부, 메시지, 기기 ID)
    /// </summary>
    Task<(bool ok, string message, string? deviceId)> RegisterMobileByTokenAsync(
        string token,
        string deviceName,
        string fingerprint,
        string ipAddress,
        string? userAgent,
        CancellationToken ct = default);
}
