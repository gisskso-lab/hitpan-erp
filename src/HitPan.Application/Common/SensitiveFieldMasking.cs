using System.Text.RegularExpressions;

namespace HitPan.Application.Common;

/// <summary>
/// 형사영역(주민번호·급여·전화) 마스킹 헬퍼.
///
/// W2 D4 (2026-05-12): CRIMINAL_DOMAIN_POLICY.md §5.3 정책 구현.
/// 기본 표시 = 마스킹 / [보기] 버튼 = step-up 인증 후 평문 5분 노출.
/// </summary>
public static class SensitiveFieldMasking
{
    /// <summary>
    /// 주민번호 마스킹: '880101-1234567' → '880101-*******'.
    /// 짧거나 빈 값은 '***' 반환.
    /// </summary>
    public static string MaskResidentNo(string? plain)
    {
        if (string.IsNullOrEmpty(plain) || plain.Length < 13) return "***";
        return $"{plain.Substring(0, 6)}-*******";
    }

    /// <summary>급여는 전체 마스킹 (●●●). NULL/0도 마스킹.</summary>
    public static string MaskSalary(decimal? amount) => "●●●";

    /// <summary>
    /// 전화번호 마스킹: '010-1234-5678' → '010-****-5678'.
    /// '01012345678' 형태도 정규식으로 처리.
    /// </summary>
    public static string MaskPhone(string? plain)
    {
        if (string.IsNullOrEmpty(plain) || plain.Length < 8) return "***";
        // 하이픈 유무 모두 대응
        var pattern = @"(\d{2,3})-?(\d{3,4})-?(\d{4})";
        var match = Regex.Match(plain, pattern);
        if (!match.Success) return "***";
        // 결재 #3 (2026-05-13): 가운데 자리 수와 무관하게 ****(4개) 일관 마스킹 — 자릿수 추정 방지
        return $"{match.Groups[1].Value}-****-{match.Groups[3].Value}";
    }
}
