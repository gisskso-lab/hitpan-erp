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
    ///
    /// <para>
    /// <paramref name="isMainPc"/> — 이 접속이 **메인PC(자료를 가진 PC)** 인가.
    /// 🔴 2026-08-10 [4] D-3·D-5 봉합 (검증팀장 데이비드 박 적발 · P0).
    ///   메인PC 는 다른 기기와 위상이 다르다. 클라이언트PC 가 막히면 고객은 메인PC 로 가서
    ///   기기를 폐기하면 되지만, **막힌 것이 메인PC 면 그 탈출구 자체가 잠긴다**
    ///   (폐기 버튼이 로그인 뒤에 있다). 그리고 그 PC 는 회사의 모든 자료를 가진 PC 다.
    ///   ⇒ 메인PC 는 슬롯을 **소모하되**(사장님 결재 — 요금정책 정합성), 한도·폐기를 이유로
    ///     **로그인을 거부하지 않는다.** 슬롯 계산과 출입 통제를 분리한 것이다.
    /// </para>
    /// </summary>
    Task<(bool allowed, string reason, string? deviceId, bool newlyRegistered)> RegisterOrRefreshAsync(
        string tenantId,
        string userId,
        RegisterDeviceRequest req,
        string ipAddress,
        bool isMainPc = false,
        CancellationToken ct = default);

    /// <summary>기기 폐기 (TenantAdmin만 호출) — status='revoked'.</summary>
    Task RevokeAsync(string deviceId, string tenantId, string userId, string? reason, CancellationToken ct = default);

    /// <summary>미들웨어용 — 해당 기기가 approved 상태인지 빠르게 확인.</summary>
    Task<bool> IsDeviceAllowedAsync(string deviceId, string tenantId, CancellationToken ct = default);
}
