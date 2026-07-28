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

    // ──────────────────────────────────────────────
    // WS-20260514-06 (2026-05-14): 자유 텍스트 PII 마스킹
    // MariaDB 예외 메시지·stack trace 같은 자유 텍스트에서 PII 패턴 검출 + 일괄 마스킹.
    // 헌법 #5, #22 / 개인정보보호법 §29 안전조치의무
    // ──────────────────────────────────────────────

    /// <summary>주민번호 패턴: '880101-1234567' (선행 7자리 보존, 뒤 7자리 마스킹).</summary>
    private static readonly Regex _residentNoPattern =
        new(@"\b(\d{6})-?[1-4]\d{6}\b", RegexOptions.Compiled);

    /// <summary>사업자등록번호 패턴: '123-45-67890' (가운데 5자리 마스킹).</summary>
    private static readonly Regex _bizNoPattern =
        new(@"\b(\d{3})-?(\d{2})-?(\d{5})\b", RegexOptions.Compiled);

    /// <summary>전화번호 패턴: 010-XXXX-XXXX / 02-XXX-XXXX / 070-XXXX-XXXX.</summary>
    private static readonly Regex _phonePattern =
        new(@"\b(01[016789]|02|0[3-6][1-5]|070)-?(\d{3,4})-?(\d{4})\b", RegexOptions.Compiled);

    /// <summary>이메일 패턴: 'user@domain.com' (앞 1자만 보존).</summary>
    private static readonly Regex _emailPattern =
        new(@"\b([A-Za-z0-9])[A-Za-z0-9._%+-]*@([A-Za-z0-9.-]+\.[A-Za-z]{2,})\b", RegexOptions.Compiled);

    /// <summary>계좌번호 패턴: 연속 9~16자리 숫자 (전화·주민·사업자 매칭 후 잔여 처리).</summary>
    private static readonly Regex _accountPattern =
        new(@"\b\d{9,16}\b", RegexOptions.Compiled);

    /// <summary>
    /// 자유 텍스트(예외 메시지·stack trace)에서 PII 패턴 검출 + 마스킹.
    /// 적용 순서: 주민번호 → 사업자번호 → 전화번호 → 이메일 → 계좌번호 (긴 패턴 우선).
    /// </summary>
    public static string MaskTextPII(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var masked = text;
        masked = _residentNoPattern.Replace(masked, m => $"{m.Groups[1].Value}-*******");
        masked = _bizNoPattern.Replace(masked, m => $"***-**-{m.Groups[3].Value}");
        masked = _phonePattern.Replace(masked, m => $"{m.Groups[1].Value}-****-{m.Groups[3].Value}");
        masked = _emailPattern.Replace(masked, m => $"{m.Groups[1].Value}***@{m.Groups[2].Value}");
        masked = _accountPattern.Replace(masked, m =>
        {
            var v = m.Value;
            return v.Length >= 4 ? $"****{v.Substring(v.Length - 4)}" : "***";
        });
        return masked;
    }
}
