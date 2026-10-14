using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HitPan.Backoffice.API.Controllers;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261015작B1 <b>갈래 F — 손님 화면·메일이 사실을 말하나</b> (G-B1-F-1 ~ F-5).
///
/// <para><b>왜 이 게이트가 있나</b> — 갈래 E 가 API 를 정직하게 만들었다(메일이 나갔으면 나갔다고,
/// 못 나갔으면 못 나갔다고 서버가 문장을 쪼개 돌려준다). 그런데 <b>화면이 그 문장을 안 읽었다.</b>
/// 성공 경로는 <c>ApplicationId</c> 만 꺼내 쓰고 <c>Message</c> 를 버렸고, 화면엔
/// 「…로 발송했습니다」가 <b>글자로 박혀</b> 있었다 ⇒ 메일 0통에도 손님은 「발송했습니다」를 봤다.
/// 메일 본문도 같은 모양이었다 — 본문을 한 번만 만들고 첨부만 갈라서,
/// 첨부 0건인 메일이 「<b>첨부된</b> 가입계약서 초안 PDF를 확인해주십시오」라고 말했다.</para>
///
/// <para>🔴 <b>신청 접수는 성공이다</b>(#20). 이 게이트는 접수 성공 표시를 건드리지 않는다 —
/// 무는 것은 <b>메일에 관한 문장</b> 하나와 <b>메일 본문</b> 한 문장이다.</para>
///
/// <para>🔴 <b>기본값이 거짓이면 반려다</b>(G-B1-F-3). 서버가 메일에 대해 아무 말도 안 했을 때
/// (<c>Message</c> 널·빈 문장) 화면이 「발송했습니다」로 되돌아가면 안 된다.
/// <b>모를 때는 단정하지 않는 문장</b>이 기본값이다.</para>
///
/// <para><b>이 게이트가 재는 자리</b>
/// <list type="bullet">
/// <item><c>src/HitPan.Backoffice/Components/Pages/Auth/ResellerApplyPage.razor</c> — 화면</item>
/// <item><c>src/HitPan.Backoffice.API/Controllers/ResellerApplicationController.cs</c> — 메일 본문</item>
/// </list></para>
///
/// <para>🔴 <b>계측 한계 — 숨기지 않고 적는다.</b> F-4·F-5 는 실물 함수
/// <see cref="ResellerApplicationController.BuildEmailHtml"/> 를 <b>직접 불러</b> 본문을 읽는다(베낀 모양 아님).
/// 반면 F-1·F-2·F-3 의 화면 쪽은 <b>실물 .razor 파일을 읽어</b> 잰다 —
/// <c>HitPan.Tests</c> 는 <c>HitPan.Backoffice</c> 를 ProjectReference 하지 않고, 화면 모델
/// <c>ApplyResponse</c>·결정 함수 <c>MailNotice</c> 는 그 razor 의 <c>@code</c> 블록 안에 있어
/// 시험이 컴파일된 그 형을 <b>볼 수 없다.</b> 「사전으로 읽지 말라」는 요구는 두 갈래로 지켰다:
/// <list type="number">
/// <item>서버 응답을 <b>typed 모델</b>(<see cref="ScreenApplyResponse"/>)로 역직렬화해 읽는다 — 사전 아님.</item>
/// <item>그 모델이 <b>화면의 실제 선언과 어긋나면 FAIL</b>(G-B1-F-3 ④ 표류 감시) — 베낌이 조용히 낡는 걸 막는다.</item>
/// </list>
/// 실행형으로 올리려면 공용 <c>.cs</c> 소스링크(선례 <c>CsOutboundRow.cs</c>)가 필요하고 그건
/// 갈래 F 담당표 밖이다 ⇒ <b>PM 결재 거리</b>로 남긴다.</para>
/// </summary>
public sealed class BackofficeResellerScreenTruthGateTests
{
    private const string RazorRel = "ResellerApplyPage.razor";

    // 🔴 「발송했습니다」 — 화면이 박아선 안 되는 단정. 「발송」 한 글자는 단정 검사의 뿌리.
    private const string AssertSent = "발송했습니다";
    private const string SendWord = "발송";
    private const string AttachedWord = "첨부된";

    // ══════════════════════════════════════════════════════════════════════
    // G-B1-F-1 — 화면 성공 경로가 payload.Message 를 읽는다
    // ══════════════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-B1-F-1 화면 성공 경로가 서버 문장(payload.Message)을 읽는다")]
    public void G_B1_F_1_성공경로가_서버문장을_읽는다()
    {
        var code = CodeBlockOf(ReadRazor());
        var success = SuccessBranchOf(code);

        Assert.True(
            Regex.IsMatch(success, @"=\s*payload\s*\.\s*Message\b"),
            "G-B1-F-1 FAIL — 화면 성공 경로가 payload.Message 를 읽지 않는다. " +
            "서버가 「메일은 아직 못 보냈다」고 말해도 화면은 그 문장을 버린다.\n" +
            "성공 분기 본문(주석·문자열 벗긴 것):\n" + success);
    }

    // ══════════════════════════════════════════════════════════════════════
    // G-B1-F-2 — 화면 마크업에 「발송했습니다」가 글자로 박혀 있지 않다
    // ══════════════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-B1-F-2 화면 마크업에 「발송했습니다」가 글자로 박혀 있지 않다")]
    public void G_B1_F_2_화면에_발송했습니다가_박혀있지_않다()
    {
        // 🔴 여기서는 **문자열·마크업 글자는 남기고 주석만** 벗긴다.
        //    문자열까지 벗기면 과녁(화면이 박아 둔 글자)이 사라지고,
        //    주석을 안 벗기면 「이 글자를 박지 마라」는 설명글이 FAIL 을 만든다.
        //    🔴 실측: 처음엔 주석을 안 벗겼더니 **내 경고 주석 2줄 때문에 이 게이트가 FAIL** 했다.
        var raw = StripCommentsKeepText(ReadRazor());

        var hits = LinesContaining(raw, AssertSent);

        Assert.True(hits.Count == 0,
            $"G-B1-F-2 FAIL — 화면에 「{AssertSent}」가 글자로 박혀 있다. " +
            "메일이 0통 나가도 손님은 이 문장을 본다.\n" + string.Join("\n", hits));
    }

    // ══════════════════════════════════════════════════════════════════════
    // G-B1-F-3 — Message 빈/널일 때 표시 문장에 「발송」 단정이 없다
    //   ① 서버 응답을 화면 모델로 **역직렬화**해서 읽는다(사전 아님)
    //   ② 화면 기본 문장(MailNoticeUnknown)에 「발송」 단정이 없다
    //   ③ 널·빈 문장이 그 기본 문장으로 간다(결정 모양)
    //   ④ 베낀 모델이 화면 선언과 어긋나면 FAIL(표류 감시)
    // ══════════════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-B1-F-3 Message 널·빈 문장일 때 기본 문장이 「발송」을 단정하지 않는다")]
    public void G_B1_F_3_모를때는_단정하지_않는다()
    {
        var razor = ReadRazor();
        var code = CodeBlockOf(razor);

        // ── ④ 표류 감시: 화면이 실제로 선언한 모델과 베낀 모델이 같은가
        var declared = DeclaredScreenModelProps(code);
        Assert.Equal(
            new[] { "ApplicationId", "Message", "Success" },
            declared.OrderBy(p => p, StringComparer.Ordinal).ToArray());

        // ── ① 서버가 실제로 내보내는 키(controller 성공 분기)로 JSON 을 세우고
        //      화면 모델 형으로 역직렬화한다. 사전으로 읽으면 화면 경로를 못 잰다(10/12 전례).
        foreach (var key in new[] { "success", "message", "applicationId" })
        {
            Assert.Contains(key, ServerSuccessPayloadKeys());
        }

        foreach (var json in new[]
                 {
                     "{\"success\":true,\"message\":null,\"applicationId\":\"a1\"}",
                     "{\"success\":true,\"message\":\"\",\"applicationId\":\"a2\"}",
                     "{\"success\":true,\"message\":\"   \",\"applicationId\":\"a3\"}",
                     "{\"success\":true,\"applicationId\":\"a4\"}"
                 })
        {
            var payload = JsonSerializer.Deserialize<ScreenApplyResponse>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.NotNull(payload);
            Assert.True(payload!.Success);
            Assert.True(string.IsNullOrWhiteSpace(payload.Message),
                "이 네 모양은 전부 「서버가 메일에 대해 아무 말도 안 한 것」이어야 한다: " + json);
        }

        // ── ③ 결정 모양: 널·빈 문장은 화면 기본 문장으로 간다
        Assert.True(
            Regex.IsMatch(code, @"IsNullOrWhiteSpace\s*\(\s*serverMessage\s*\)\s*\?\s*MailNoticeUnknown"),
            "G-B1-F-3 FAIL — 널·빈 문장이 기본 문장으로 가는 결정이 화면에 없다. " +
            "fallback 이 「발송」을 단정하면 그 자체가 반려 사유다.\n" + code);

        // ── ② 그 기본 문장이 「발송」을 단정하지 않는다
        var fallback = FallbackSentenceOf(razor);
        Assert.False(string.IsNullOrWhiteSpace(fallback),
            "G-B1-F-3 FAIL — 화면 기본 문장(MailNoticeUnknown)을 찾지 못했다.");
        Assert.DoesNotContain(SendWord, fallback);

        // 개발 용어 금지(#23) — 손님 화면이다
        foreach (var jargon in new[] { "SMTP", "API", "오류 코드", "설정" })
        {
            Assert.DoesNotContain(jargon, fallback);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // G-B1-F-4 — 첨부 **없는** 본문에 「첨부된」이 없다  (실물 함수 직접 호출)
    // ══════════════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-B1-F-4 첨부 없는 메일 본문은 「첨부된」을 말하지 않는다")]
    public void G_B1_F_4_첨부없는_본문은_첨부된을_말하지_않는다()
    {
        var html = ResellerApplicationController.BuildEmailHtml(
            "㈜정직상사", "00000000-1111-2222-3333-444444444444", hasAttachment: false);

        Assert.DoesNotContain(AttachedWord, html);

        // 접수 사실 자체는 계속 말한다 — 끊지 않는다(#20)
        Assert.Contains("접수", html);
        Assert.Contains("00000000-1111-2222-3333-444444444444", html);
    }

    // ══════════════════════════════════════════════════════════════════════
    // G-B1-F-5 — 첨부 **있는** 본문에는 「첨부된」이 있다 (양성 대조군)
    // ══════════════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-B1-F-5 첨부 있는 메일 본문에는 「첨부된」이 있다 (양성 대조군)")]
    public void G_B1_F_5_첨부있는_본문에는_첨부된이_있다()
    {
        var html = ResellerApplicationController.BuildEmailHtml(
            "㈜정직상사", "00000000-1111-2222-3333-444444444444", hasAttachment: true);

        Assert.Contains(AttachedWord, html);

        // 🔴 두 본문이 실제로 다른가 — 분기가 살아 있다는 증거
        var none = ResellerApplicationController.BuildEmailHtml(
            "㈜정직상사", "00000000-1111-2222-3333-444444444444", hasAttachment: false);
        Assert.NotEqual(none, html);
    }

    // ══════════════════════════════════════════════════════════════════════
    // 화면 모델 — 베낀 모양. 어긋나면 G-B1-F-3 ④ 가 FAIL 한다.
    // 🔴 공용 .cs 소스링크(선례 CsOutboundRow.cs)로 올리는 건 담당표 밖 ⇒ PM 결재 거리.
    // ══════════════════════════════════════════════════════════════════════
    private sealed class ScreenApplyResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? ApplicationId { get; set; }
    }

    // ───────────────────────── 읽기·벗기기 도구 ─────────────────────────
    // 🔴 D 헬퍼로 교체 필요 — 갈래 D 의 src/HitPan.Tests/Integrity/SourceText.cs 가
    //    주석·문자열 벗기기를 공용으로 만든다. 합류 후 아래 세 함수를 그것으로 바꾼다
    //    (지금 바닥엔 그 파일이 없어 또 만들지 않고 이 클래스 안에만 둔다).

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.True(dir is not null && Directory.Exists(Path.Combine(dir, "src")),
            "레포 루트를 찾아야 한다");
        return dir!;
    }

    private static string ReadRazor()
    {
        var path = Path.Combine(RepoRoot(), "src", "HitPan.Backoffice", "Components",
            "Pages", "Auth", RazorRel);
        Assert.True(File.Exists(path), $"화면 파일이 있어야 한다: {path}");
        return File.ReadAllText(path);
    }

    private static string ReadController()
    {
        var path = Path.Combine(RepoRoot(), "src", "HitPan.Backoffice.API", "Controllers",
            "ResellerApplicationController.cs");
        Assert.True(File.Exists(path), $"컨트롤러 파일이 있어야 한다: {path}");
        return File.ReadAllText(path);
    }

    private static string StripRazorComments(string s) =>
        Regex.Replace(s, @"@\*.*?\*@", " ", RegexOptions.Singleline);

    /// <summary>
    /// 주석만 지우고 <b>문자열·마크업 글자는 남긴다</b>(G-B1-F-2 용).
    /// 줄 수를 유지해서 FAIL 메시지의 줄번호가 실제 파일과 맞는다.
    /// <c>://</c> 는 URL 이라 줄 주석으로 보지 않는다.
    /// </summary>
    private static string StripCommentsKeepText(string s)
    {
        s = Regex.Replace(s, @"@\*.*?\*@", m => BlankKeepLines(m.Value), RegexOptions.Singleline);
        s = Regex.Replace(s, @"/\*.*?\*/", m => BlankKeepLines(m.Value), RegexOptions.Singleline);

        var lines = s.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var at = -1;
            for (var j = 0; j + 1 < lines[i].Length; j++)
            {
                if (lines[i][j] != '/' || lines[i][j + 1] != '/') continue;
                if (j > 0 && lines[i][j - 1] == ':') continue; // https:// 등
                at = j;
                break;
            }

            if (at >= 0) lines[i] = lines[i].Substring(0, at);
        }

        return string.Join("\n", lines);
    }

    private static string BlankKeepLines(string block) =>
        new string('\n', block.Count(c => c == '\n'));

    /// <summary>@code { … } 블록만 떼어 내고 주석·문자열을 벗긴다.</summary>
    private static string CodeBlockOf(string razor)
    {
        var at = razor.IndexOf("@code", StringComparison.Ordinal);
        Assert.True(at >= 0, "화면에 @code 블록이 있어야 한다");
        var open = razor.IndexOf('{', at);
        Assert.True(open > 0, "@code 블록의 여는 괄호를 찾아야 한다");
        return StripCommentsAndStrings(Braced(razor, open));
    }

    /// <summary>성공 분기 — if (res.IsSuccessStatusCode …) { … } 의 본문.</summary>
    private static string SuccessBranchOf(string code)
    {
        var m = Regex.Match(code, @"if\s*\([^{]*IsSuccessStatusCode[^{]*\)\s*\{");
        Assert.True(m.Success, "성공 분기(if … IsSuccessStatusCode …)를 찾아야 한다:\n" + code);
        return Braced(code, m.Index + m.Length - 1);
    }

    /// <summary>여는 괄호 위치부터 짝이 맞는 닫는 괄호까지.</summary>
    private static string Braced(string s, int openIndex)
    {
        Assert.Equal('{', s[openIndex]);
        var depth = 0;
        for (var i = openIndex; i < s.Length; i++)
        {
            if (s[i] == '{') depth++;
            else if (s[i] == '}' && --depth == 0) return s.Substring(openIndex + 1, i - openIndex - 1);
        }

        Assert.Fail("괄호 짝이 맞지 않는다 — 소스를 다시 봐라");
        return string.Empty;
    }

    /// <summary>
    /// 주석(// · /* */ · @* *@)과 문자열 리터럴을 공백으로 바꾼다.
    /// 🔴 주석을 안 벗기면 설명글의 「payload.Message」가 통과를 만든다(글자 세기의 고전적 구멍).
    /// </summary>
    private static string StripCommentsAndStrings(string s)
    {
        s = StripRazorComments(s);
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            // 줄 주석
            if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n') i++;
                sb.Append('\n');
                continue;
            }

            // 블록 주석
            if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) i++;
                i++;
                sb.Append(' ');
                continue;
            }

            // 축자 문자열 @"…" ($@"…" 포함) — "" 가 따옴표 한 개
            if (s[i] == '@' && i + 1 < s.Length && s[i + 1] == '"')
            {
                i += 2;
                while (i < s.Length)
                {
                    if (s[i] == '"')
                    {
                        if (i + 1 < s.Length && s[i + 1] == '"') { i += 2; continue; }
                        break;
                    }

                    i++;
                }

                sb.Append(' ');
                continue;
            }

            // 보통 문자열 "…" ($"…" 포함)
            if (s[i] == '"')
            {
                i++;
                while (i < s.Length && s[i] != '"')
                {
                    if (s[i] == '\\') i++;
                    i++;
                }

                sb.Append(' ');
                continue;
            }

            // 문자 리터럴 '…'
            if (s[i] == '\'')
            {
                i++;
                while (i < s.Length && s[i] != '\'')
                {
                    if (s[i] == '\\') i++;
                    i++;
                }

                sb.Append(' ');
                continue;
            }

            sb.Append(s[i]);
        }

        return sb.ToString();
    }

    /// <summary>화면이 선언한 ApplyResponse 의 속성 이름들.</summary>
    private static string[] DeclaredScreenModelProps(string code)
    {
        var m = Regex.Match(code, @"class\s+ApplyResponse\s*\{");
        Assert.True(m.Success, "화면에 ApplyResponse 선언이 있어야 한다:\n" + code);
        var body = Braced(code, m.Index + m.Length - 1);
        return Regex.Matches(body, @"public\s+[\w\?<>\.]+\s+(\w+)\s*\{\s*get;")
            .Select(x => x.Groups[1].Value)
            .ToArray();
    }

    /// <summary>화면 기본 문장(MailNoticeUnknown)의 리터럴 값. 문자열을 벗기지 않은 원문에서 뽑는다.</summary>
    private static string FallbackSentenceOf(string razor)
    {
        var m = Regex.Match(StripRazorComments(razor),
            @"MailNoticeUnknown\s*=\s*""((?:[^""\\]|\\.)*)""\s*;");
        return m.Success ? m.Groups[1].Value : string.Empty;
    }

    /// <summary>컨트롤러 성공 분기가 실제로 내보내는 키 이름들.</summary>
    private static string[] ServerSuccessPayloadKeys()
    {
        var src = StripCommentsAndStrings(ReadController());
        var m = Regex.Match(src, @"return\s+Ok\s*\(\s*new\s*\{");
        Assert.True(m.Success, "컨트롤러 성공 분기(return Ok(new { … }))를 찾아야 한다");
        var body = Braced(src, m.Index + m.Length - 1);
        return Regex.Matches(body, @"(\w+)")
            .Select(x => x.Groups[1].Value)
            .ToArray();
    }

    private static List<string> LinesContaining(string text, string needle)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var hits = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(needle, StringComparison.Ordinal))
            {
                hits.Add($"  {RazorRel}:{i + 1}  {lines[i].Trim()}");
            }
        }

        return hits;
    }
}
