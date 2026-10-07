using System.Text.RegularExpressions;

namespace HitPan.Application.Services;

/// <summary>
/// 🔴 작14 B-4 문①·문② 공용 — CS 쪽지 본문의 금지필드 검사 (고객 개인정보가 본사로 나가는 것을 막는 쪽).
///
/// <para>근거: 작업지시서 20261008작14 §3 B-4(CTO 조건③) · 설계 20261007_개발매니저피드백 §5-1.
/// 선례 2개를 따랐다(새 기법 금지): <c>OutboxPublisherService.cs:25</c> 의 사업자번호 정규식 ·
/// <c>MetaPingClient.cs:20-27</c> 의 금지 필드명 집합. CS 본문은 자유 글이라 키 이름 검사로는
/// 못 막으므로 **모양(패턴) 검사**를 더한다.</para>
///
/// <para>🔴 설계가 미리 적은 반증 — 숫자 패턴은 **오탐이 있다**(긴 전표번호가 잡힐 수 있다).
/// 그래서 ①걸려도 "거부"가 아니라 「이 부분을 지워 주세요」로 되돌리고(고칠 기회),
/// ②규칙별 적중을 <c>cs_forbidden_rejects</c>(값은 저장 안 함 · 규칙코드만)로 세어
/// CS팀장이 규칙을 조정한다(반자동). 정확도는 ⚠️실측 전엔 모른다 — 그 말을 지우지 않는다.</para>
///
/// <para>🔴 막힌 값 자체는 어디에도 저장·로깅하지 않는다(CTO 조건③) — 반환은 규칙코드뿐이다.</para>
/// </summary>
public static class ForbiddenFieldScanner
{
    // 선례 그대로 — OutboxPublisherService.cs:25 (사업자번호 10자리 3-2-5 모양)
    private static readonly Regex BizNoPattern =
        new(@"\b\d{3}-?\d{2}-?\d{5}\b", RegexOptions.Compiled);

    // 주민등록번호 모양 — 6자리-7자리 (붙여 쓴 13자리 연속 숫자도 같은 규칙으로 본다)
    private static readonly Regex ResidentNoPattern =
        new(@"\b\d{6}-\d{7}\b|\b\d{13}\b", RegexOptions.Compiled);

    // 카드번호 모양 — 4자리 묶음 구분(공백/하이픈) 13~19자리. Luhn 검산까지 통과해야 적중.
    private static readonly Regex CardShapePattern =
        new(@"\b\d{4}([ -]?\d{4}){2,3}([ -]?\d{1,3})?\b", RegexOptions.Compiled);

    // 계좌번호 모양 — 하이픈 2개 이상으로 묶인 숫자 10~14자리.
    //   ⚠️ 오탐 위험이 가장 큰 규칙(설계 §5-1)이라 하이픈 없는 긴 숫자는 잡지 않는다
    //   (「전표번호 1234567890 이 안 열려요」를 막지 않기 위해 — 조정은 거부 집계를 보고 CS팀장이).
    private static readonly Regex AccountShapePattern =
        new(@"\b\d{2,6}-\d{2,6}-\d{2,8}\b", RegexOptions.Compiled);

    /// <summary>
    /// 본문에서 금지필드 모양을 찾는다. 적중 시 규칙코드(DB-142 <c>rule_code</c> 어휘),
    /// 없으면 null. 🔴 적중한 값은 반환하지 않는다 — 코드만.
    /// </summary>
    public static string? Scan(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        if (ResidentNoPattern.IsMatch(body)) return "resident_no";

        foreach (Match m in CardShapePattern.Matches(body))
        {
            var digits = new string(m.Value.Where(char.IsDigit).ToArray());
            if (digits.Length is >= 13 and <= 19 && PassesLuhn(digits)) return "card_no";
        }

        // 🔴 순서: 사업자(3-2-5 — 더 특정적) 먼저, 계좌(하이픈 묶음 일반형)는 마지막.
        //    거꾸로 두면 000-00-00000 모양이 account_no 로 집계돼 CS팀장의 규칙 조정 숫자가 틀어진다
        //    — CI db-gate G-CS-5 가 실측으로 잡은 흠(2026-10-08 · 6/7 PASS 중 1 FAIL).
        if (BizNoPattern.IsMatch(body)) return "biz_no";
        if (AccountShapePattern.IsMatch(body)) return "account_no";

        return null;
    }

    /// <summary>규칙코드 → 고객 안내 문구(문① UX — 거부가 아니라 고칠 기회).</summary>
    public static string GuideMessage(string ruleCode) => ruleCode switch
    {
        "resident_no" => "주민등록번호로 보이는 숫자가 있습니다. 그 부분을 지워 주세요.",
        "card_no" => "카드번호로 보이는 숫자가 있습니다. 그 부분을 지워 주세요.",
        "account_no" => "계좌번호로 보이는 숫자가 있습니다. 그 부분을 지워 주세요.",
        "biz_no" => "사업자번호로 보이는 숫자가 있습니다. 그 부분을 지워 주세요.",
        _ => "보낼 수 없는 내용이 있습니다. 숫자 식별정보를 지워 주세요.",
    };

    // 카드번호 검산(Luhn) — 모양만으로 자르면 오탐이 커서 검산 통과분만 적중으로 본다.
    private static bool PassesLuhn(string digits)
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
