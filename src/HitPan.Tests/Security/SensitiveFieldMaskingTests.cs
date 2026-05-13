using HitPan.Application.Common;

namespace HitPan.Tests.Security;

/// <summary>
/// W2 D4 (2026-05-12) — SensitiveFieldMasking 단위 테스트.
///
/// 검증 범위: CRIMINAL_DOMAIN_POLICY.md §5.3 마스킹 정책 구현 정확성.
/// - 주민번호 13자리 → 앞 6자리만 표시
/// - 급여는 항상 ●●●
/// - 전화번호는 가운데 4자리 마스킹 (하이픈 유무 무관)
/// </summary>
public class SensitiveFieldMaskingTests
{
    [Theory(DisplayName = "MASK-01: 주민번호 마스킹 — 정상 13자리")]
    [InlineData("880101-1234567", "880101-*******")]
    [InlineData("9001011234567", "900101-*******")]  // 하이픈 없는 13자리도 앞 6자리 살림
    public void MaskResidentNo_Returns_FirstSixWithMaskedTail(string input, string expected)
    {
        var result = SensitiveFieldMasking.MaskResidentNo(input);
        Assert.Equal(expected, result);
    }

    [Theory(DisplayName = "MASK-02: 주민번호 마스킹 — null/짧은 입력은 *** 반환")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("880101")]   // 짧음 (6자리)
    [InlineData("12345")]    // 짧음 (5자리)
    public void MaskResidentNo_InvalidInput_ReturnsStars(string? input)
    {
        var result = SensitiveFieldMasking.MaskResidentNo(input);
        Assert.Equal("***", result);
    }

    [Theory(DisplayName = "MASK-03: 급여는 항상 ●●● (NULL/0/큰 수 모두)")]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(5000000.0)]
    [InlineData(1234567890.99)]
    public void MaskSalary_AlwaysReturnsBullets(double? amount)
    {
        var input = amount.HasValue ? (decimal?)amount.Value : null;
        var result = SensitiveFieldMasking.MaskSalary(input);
        Assert.Equal("●●●", result);
    }

    // 결재 #3 (2026-05-13) — MaskPhone 단위 테스트 5케이스 추가 (D4 누락 보완)
    [Theory(DisplayName = "MASK-04: 전화번호 마스킹 — 휴대전화/지역번호/하이픈 유무")]
    [InlineData("010-1234-5678", "010-****-5678")]
    [InlineData("01012345678", "010-****-5678")]
    [InlineData("02-123-4567", "02-****-4567")]
    [InlineData("031-1234-5678", "031-****-5678")]
    public void MaskPhone_Returns_MaskedMiddle(string input, string expected)
    {
        var result = SensitiveFieldMasking.MaskPhone(input);
        Assert.Equal(expected, result);
    }

    [Theory(DisplayName = "MASK-05: 전화번호 마스킹 — null/빈값/비정상 입력은 *** 반환")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("잘못된번호")]
    [InlineData("123")]
    public void MaskPhone_InvalidInput_ReturnsStars(string? input)
    {
        var result = SensitiveFieldMasking.MaskPhone(input);
        Assert.Equal("***", result);
    }
}
