namespace HitPan.Application.Interfaces;

/// <summary>주소 → 좌표 변환 결과.</summary>
public sealed class GeocodeResult
{
    public bool Success { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    /// <summary>변환에 실제로 쓰인 주소 (도로명 우선). 사람이 확인할 수 있게 돌려준다.</summary>
    public string? MatchedAddress { get; set; }

    /// <summary>실패 사유 — 사용자에게 그대로 보일 수 있는 문구 (개발용어 금지).</summary>
    public string? Error { get; set; }

    /// <summary>
    /// 좌표 서비스가 아직 설정되지 않아 시도조차 못 한 경우 true.
    /// "주소를 못 찾았다" 와 "찾아볼 수단이 없다" 는 다른 상황이므로 구분한다.
    /// </summary>
    public bool NotConfigured { get; set; }
}

/// <summary>
/// 주소 → 좌표 변환 (사장님 결재 2026-08-21).
///
/// 왜 필요한가: 카카오맵·카카오내비 딥링크는 **좌표를 요구한다.**
///   종전엔 주소 문자열을 좌표 자리에 넣어 파싱이 실패했고, 지도가 해당 위치가 아니라
///   기본 위치로 열렸다 (사장님 지적: "맵이 뜨긴 하지만 실제 해당주소 좌표가 안찍힘").
///   우편번호 서비스는 좌표를 주지 않으므로(공식 문서 확인) 별도 변환이 필요하다.
///
/// §#18·#22: 변환은 고객사 PC 의 서버가 직접 호출한다. 본사를 경유하지 않는다.
/// §#100(반자동 원칙): 변환 결과는 **제안값**이다. 사람이 확인·수정할 수 있어야 한다.
/// </summary>
public interface IGeocodingService
{
    /// <summary>
    /// 주소를 좌표로 바꾼다.
    /// 🔴 실패해도 예외를 던지지 않는다 — 좌표는 부가정보이고, 없어도 업체 저장은 되어야 한다 (§#20).
    /// </summary>
    Task<GeocodeResult> GeocodeAsync(string address, CancellationToken ct = default);

    /// <summary>좌표 변환을 쓸 수 있는 상태인가 (키 설정 여부).</summary>
    bool IsConfigured { get; }
}
