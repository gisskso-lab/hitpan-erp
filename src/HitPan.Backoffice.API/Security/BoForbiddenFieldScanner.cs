using System.Text.RegularExpressions;

namespace HitPan.Backoffice.API.Security;

/// <summary>
/// 🔴 백오피스 금지필드 본문 검사 — <b>수신 최종 판정자</b>의 규칙 한 벌 (작14 B-4 문③ · C-4 승인 재스캔).
///
/// <para>ERP <c>ForbiddenFieldScanner</c> 와 <b>같은 규칙·같은 어휘</b>지만 <b>독립 복제</b>다
/// (백오피스는 ERP 프로젝트를 참조하지 않는다 — 경계 분리가 설계값이고, 구버전 ERP 가
/// 문①②를 뚫고 와도 이 문이 혼자 판정해야 한다).</para>
///
/// <para>🔴 <b>검사 순서가 규칙의 일부다</b>: 주민 → 카드(Luhn) → <b>사업자(3-2-5)</b> → 계좌.
/// 사업자번호가 계좌 모양보다 특정적이어서 먼저 와야 한다 — 뒤집으면 사업자번호가
/// <c>account_no</c> 로 잘못 잡힌다(2026-10-08 CI G-CS-5 실측 교훈).</para>
///
/// <para>걸린 <b>값은 어디에도 저장하지 않는다</b>(#22·#40) — 사유코드만 남긴다.</para>
/// </summary>
internal static class BoForbiddenFieldScanner
{
    private static readonly Regex BizNo = new(@"\b\d{3}-?\d{2}-?\d{5}\b", RegexOptions.Compiled);
    private static readonly Regex ResidentNo = new(@"\b\d{6}-\d{7}\b|\b\d{13}\b", RegexOptions.Compiled);
    private static readonly Regex CardShape = new(@"\b\d{4}([ -]?\d{4}){2,3}([ -]?\d{1,3})?\b", RegexOptions.Compiled);
    private static readonly Regex AccountShape = new(@"\b\d{2,6}-\d{2,6}-\d{2,8}\b", RegexOptions.Compiled);

    /// <summary>걸리면 사유코드, 깨끗하면 <c>null</c>.</summary>
    internal static string? Scan(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        if (ResidentNo.IsMatch(body)) return "resident_no";
        foreach (Match m in CardShape.Matches(body))
        {
            var digits = new string(m.Value.Where(char.IsDigit).ToArray());
            if (digits.Length is >= 13 and <= 19 && Luhn(digits)) return "card_no";
        }
        if (BizNo.IsMatch(body)) return "biz_no";          // 3-2-5 가 더 특정적 — 계좌보다 먼저
        if (AccountShape.IsMatch(body)) return "account_no";
        return null;
    }

    private static bool Luhn(string digits)
    {
        int sum = 0; bool alt = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int d = digits[i] - '0';
            if (alt) { d *= 2; if (d > 9) d -= 9; }
            sum += d; alt = !alt;
        }
        return sum % 10 == 0;
    }
}
