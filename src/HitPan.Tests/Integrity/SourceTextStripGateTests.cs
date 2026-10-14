using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261015작B1 갈래 D · 조건2 — <see cref="SourceText"/> 벗기기 <b>자체</b>의 자물쇠 (G-B1-D-1 ~ G-B1-D-7).
///
/// <para><b>왜 이 게이트가 있나</b>: 소스 숫자를 세는 게이트 5곳이 전부 이 벗기기에 기댄다.
/// 벗기기가 틀리면 그 5곳이 <b>조용히</b> 거짓 판정으로 돌아간다 — 빨강도 안 나고 아무도 모른다.
/// 「주석을 세는 자리」가 이 트랙에서 6번 재발한 이유가 그것이다. 그래서 벗기개에 시험을 둔다.</para>
///
/// <para><b>DB 불필요</b> — 순수 문자열 시험이다.</para>
/// </summary>
public class SourceTextStripGateTests
{
    // ══════════════════════════════════════════════════════════════════════
    // G-B1-D-1 — 문자열 안의 `//` 를 주석으로 오인하지 않는다
    // ══════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-B1-D-1 문자열 안의 `//` 는 주석이 아니다 — 그 뒤의 실코드가 살아남는다")]
    public void G_B1_D_1_문자열_안의_슬래시두개는_주석이_아니다()
    {
        // 정규식 한 줄(`//[^\n]*`)이 틀리는 바로 그 모양: URL 뒤에 실코드가 더 있다.
        const string src = @"var url = ""https://smtp.example.invalid/send""; RecordAsync(a, b, sent: true);";

        var code = SourceText.ExecutableCode(src);

        Assert.Equal(1, Count(code, "RecordAsync("));
        Assert.Contains("sent: true", code);

        // 리터럴을 보존하는 쪽도 같은 판정이어야 한다.
        var kept = SourceText.StripComments(src);
        Assert.Equal(1, Count(kept, "RecordAsync("));
        Assert.Contains("https://smtp.example.invalid/send", kept);
    }

    // ══════════════════════════════════════════════════════════════════════
    // G-B1-D-2 — 블록주석 안의 코드 글자를 세지 않는다
    // ══════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-B1-D-2 블록주석 `/* */` 안의 코드 글자는 0으로 센다 (줄주석 벗기기가 못 하던 자리)")]
    public void G_B1_D_2_블록주석_안은_세지_않는다()
    {
        const string src = @"
/* 되돌림 설명:
   예전에는 _ = _email.SendAsync(x) 로 버려서 아무도 몰랐다.
   RecordAsync(toEmail, subject, sent: true) 도 없었다. */
var r = await _email.SendAsync(x);
RecordAsync(toEmail, subject, sent: true);";

        var code = SourceText.ExecutableCode(src);

        Assert.Equal(0, Count(code, "_ = _email."));
        Assert.Equal(1, Count(code, "RecordAsync("));

        // 줄 번호가 밀리지 않는다 — 위반 줄을 보고하는 게이트(ERP 스텁)가 이걸 믿는다.
        Assert.Equal(Count(src, "\n"), Count(code, "\n"));
    }

    // ══════════════════════════════════════════════════════════════════════
    // G-B1-D-3 — 보간 문자열을 지운다
    // ══════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-B1-D-3 보간 문자열 `$\"…\"` 의 내용은 실행코드에서 사라진다 (메시지 글자를 세지 않는다)")]
    public void G_B1_D_3_보간문자열은_지워진다()
    {
        // 실패 메시지 안에 세려는 토큰이 들어 있는 모양 — 게이트 메시지가 제 과녁을 늘리던 자리.
        const string src = @"
Assert.True(ok, $""{gate} MaskToken( 사용이 {n}곳뿐이다"");
var masked = MaskToken(req.SignupToken);";

        var code = SourceText.ExecutableCode(src);

        // 메시지 안의 `MaskToken(` 은 안 세고, 실제 호출 1곳만 센다.
        Assert.Equal(1, Count(code, "MaskToken("));
        Assert.DoesNotContain("곳뿐이다", code);

        // 보간 구멍 안의 코드도 리터럴과 함께 사라진다 — 접두사 `$` 까지 먹고 빈 `""` 모양만 남는다.
        Assert.DoesNotContain("{gate}", code);
        Assert.DoesNotContain("$", code);
        Assert.Contains(@", """");", code);
    }

    // ══════════════════════════════════════════════════════════════════════
    // G-B1-D-4 — 주석 안의 `"` 가 문자열을 열지 않는다 (역방향 오인)
    // ══════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-B1-D-4 주석 안의 홀따옴표·쌍따옴표가 문자열을 열어 뒤 코드를 삼키지 않는다")]
    public void G_B1_D_4_주석_안의_따옴표는_문자열을_열지_않는다()
    {
        const string src = @"
// 여기서 "" 를 하나만 적어 둔다 — 짝이 안 맞는다. don't
RecordAsync(toEmail, subject, sent: true);
_config[""Smtp:Host""];";

        var code = SourceText.ExecutableCode(src);
        Assert.Equal(1, Count(code, "RecordAsync("));

        // 리터럴 보존 쪽에서는 구성 키가 그대로 읽혀야 한다 (B-1A-6 이 이걸 센다).
        var kept = SourceText.StripComments(src);
        Assert.Contains(@"_config[""Smtp:Host""]", kept);
        Assert.DoesNotContain("짝이 안 맞는다", kept);
    }

    // ══════════════════════════════════════════════════════════════════════
    // G-B1-D-5 — 축자·원시 문자열
    // ══════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-B1-D-5 축자 `@\"\"` 와 원시 `\"\"\"…\"\"\"` 안의 코드 글자도 세지 않는다")]
    public void G_B1_D_5_축자와_원시문자열도_벗긴다()
    {
        // 축자: 안의 `""` 는 따옴표 한 개 — 여기서 문자열이 닫힌다고 보면 뒤가 전부 틀어진다.
        const string verbatim = @"var sql = @""SELECT '/* x */' AS ""ok"" -- _ = _email.SendAsync(y)""; RecordAsync(a);";
        var code1 = SourceText.ExecutableCode(verbatim);
        Assert.Equal(0, Count(code1, "_ = _email."));
        Assert.Equal(1, Count(code1, "RecordAsync("));

        // 원시 문자열 — 따옴표 3개로 열고 3개로 닫는다. 안의 `//` 도 주석이 아니다.
        var raw = "var t = \"\"\"\n  // _ = _email.SendAsync(z)\n  \"\"\"; RecordAsync(b);";
        var code2 = SourceText.ExecutableCode(raw);
        Assert.Equal(0, Count(code2, "_ = _email."));
        Assert.Equal(1, Count(code2, "RecordAsync("));
    }

    // ══════════════════════════════════════════════════════════════════════
    // G-B1-D-6 — 문자 리터럴 `'"'`
    // ══════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-B1-D-6 문자 리터럴 `'\"'` 가 문자열을 열지 않는다")]
    public void G_B1_D_6_문자리터럴_안의_따옴표는_문자열을_열지_않는다()
    {
        const string src = @"if (c == '""') { } RecordAsync(a); if (c == '\\') { } RecordAsync(b);";

        var code = SourceText.ExecutableCode(src);
        Assert.Equal(2, Count(code, "RecordAsync("));
    }

    // ══════════════════════════════════════════════════════════════════════
    // G-B1-D-7 🔴 음성 대조군 — 벗기기를 빼면(원문 그대로 세면) 같은 입력이 FAIL 한다
    // ══════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-B1-D-7 음성 대조군 — 벗기기 없이 원문을 세면 미끼 주석 때문에 숫자가 틀린다")]
    public void G_B1_D_7_벗기기를_빼면_숫자가_틀린다()
    {
        // 봉합을 끝낸 코드 + 되돌림을 **설명하는** 주석 하나. 실코드의 버림은 0곳이다.
        const string sealedSource = @"
// 예전 사고: `_ = _email.SendAsync(req.Email, …)` 로 버려서 아무도 몰랐다.
var sent = await _email.SendAsync(req.Email, subject, body);
if (!sent) _logger.LogWarning(""메일 발송 실패"");";

        // 벗긴 뒤 = 0곳 (초록)
        Assert.Equal(0, Count(SourceText.ExecutableCode(sealedSource), "_ = _email."));

        // 벗기기 없이 원문 그대로 = 1곳 (빨강) — 봉합본이 빨간불이 되던 그 숫자다.
        Assert.Equal(1, Count(sealedSource, "_ = _email."));

        // 블록주석 쪽 음성 대조 — 줄주석만 떼는 옛 방식(`//[^\n]*`)으로는 여전히 1곳이 남는다.
        const string blockBait = @"
/* 되돌리면: _ = _email.SendAsync(x) */
var sent = await _email.SendAsync(x);";
        Assert.Equal(0, Count(SourceText.ExecutableCode(blockBait), "_ = _email."));
        Assert.Equal(1, Count(
            System.Text.RegularExpressions.Regex.Replace(blockBait, @"//[^\n]*", ""), "_ = _email."));
    }

    private static int Count(string haystack, string needle)
    {
        var n = 0;
        var at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { n++; at += needle.Length; }
        return n;
    }
}
