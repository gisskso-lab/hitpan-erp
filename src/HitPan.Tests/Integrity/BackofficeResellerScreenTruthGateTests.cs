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
    // 🔴 20261015작B1 갈래 G(G-2 · PM 재결재3 §19 판정2 + 검증팀 보강) — 금지어 **한 벌**.
    //
    //   과녁은 「발송」 어간이 **아니다.** PM 실측(작지서 §19 판정2): 어간 전수 = 오탐 5건
    //   (미래형 약속 2 — `:21`·`:37` · 주석 3 — `:31`·`:146`·`:177`).
    //   어간으로 넓히면 **값이 들어오면 참이 되는 약속 문장**까지 걸려 뒤에 되돌릴 글자가 된다.
    //   그래서 과녁은 **결과를 단정하는 완료형 세 모양**이다.
    //
    //   🔴 이 배열은 <b>한 벌</b>이다. BackofficeResellerMailTruthGateTests(G-4)가 **같은 것**을 쓴다 —
    //      두 벌이 되면 한 벌만 고쳐지는 날이 온다(§19 판정2 마지막 줄).
    // ══════════════════════════════════════════════════════════════════════
    internal static readonly string[] SendCompletedShapes =
    {
        "발송했습니다",
        "발송되었습니다",
        "발송 완료"
    };

    /// <summary>🔴 미래형 약속 — 걸리면 그 게이트가 틀린 것이다(§19 판정2). 오탐 방지 대조군.</summary>
    internal static readonly string[] FutureTensePromises =
    {
        "가입 신청 시 가입계약서 초안 PDF가 이메일로 발송됩니다.",
        "본사 검토 후 정식 계약서가 별도로 발송됩니다."
    };

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
        //    🔴 갈래 G(G-1) — 벗기개를 공용 SourceText 로 바꿨다(지역 정규식 → 상태기계).
        var raw = ScreenTextWithoutComments(ReadRazor());

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
    // 🔴 G-B1-G-1 — 벗기개가 **공용 SourceText** 이고, 은퇴한 지역 벗기개로는 못 잡는다
    //   (갈래 G · P1-1 · 조건15의 미끼 대조를 **게이트 안으로** 옮긴 것)
    // ══════════════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-B1-G-1 주석을 세지 않는다 — 공용 벗기개가 실코드와 주석을 가른다")]
    public void G_B1_G_1_공용벗기개가_주석과_실코드를_가른다()
    {
        // ── ① 살아 있는 미끼: 화면 파일의 **경고 주석** 안에 「발송했습니다」가 실제로 있다(razor:31 등).
        //    제품 소스에 미끼를 **심지 않았다**(조건15) — 이미 있는 글자를 미끼로 쓴다.
        var razor = ReadRazor();
        var inRaw = LinesContaining(razor, AssertSent).Count;
        Assert.True(inRaw >= 1,
            "G-B1-G-1 FAIL — 화면 주석의 경고문이 사라졌다. 이 게이트의 미끼가 없어져 " +
            "「주석을 세지 않는다」를 더는 증명하지 못한다. 경고문을 지웠다면 이 게이트를 다시 설계해라.");

        // ── ② 정본 벗기개는 그 미끼를 **0건**으로 만든다. (안 벗기면 F-2·G-2 가 자기 경고문에 FAIL)
        var clean = ScreenTextWithoutComments(razor);
        Assert.True(LinesContaining(clean, AssertSent).Count == 0,
            $"G-B1-G-1 FAIL — 정본 벗기개가 주석을 못 벗겼다(원문 {inRaw}건 → 벗긴 뒤 " +
            $"{LinesContaining(clean, AssertSent).Count}건). 게이트가 주석을 센다.");

        // ── ③ 줄 수 보존 — FAIL 메시지의 줄번호가 실제 파일과 맞아야 한다.
        Assert.Equal(
            razor.Replace("\r\n", "\n").Split('\n').Length,
            clean.Replace("\r\n", "\n").Split('\n').Length);

        // ── ④ 🔴 은퇴한 지역 벗기개는 **이 일을 못 한다** — 왜 교체했나를 숫자로 고정한다.
        //    ⓐ 은퇴본(StripCommentsKeepText)은 **문자열 안의 `//`** 를 줄주석으로 오인해
        //       그 줄의 뒤를 통째로 날린다 ⇒ 뒤에 있던 과녁 글자를 **못 세고 통과**한다.
        //       (`://` 예외 한 개는 URL 만 막는다 — 아래처럼 `:` 가 앞에 없으면 뚫린다.)
        const string trap = "var t = \"a//b 발송했습니다\";";
        Assert.Empty(LinesContaining(StripCommentsKeepText(trap), AssertSent));   // 은퇴본: 놓친다
        Assert.Single(LinesContaining(SourceText.StripComments(trap), AssertSent)); // 정본: 잡는다

        //    ⓑ 은퇴본(StripCommentsAndStrings)은 블록주석을 공백 한 칸으로 바꿔 **줄번호를 깨뜨린다**
        //       ⇒ FAIL 메시지가 가리키는 줄이 실제 파일과 어긋난다. 정본은 줄 수를 보존한다.
        const string multi = "a\n/* x\ny */\nb";
        Assert.Equal(4, multi.Split('\n').Length);
        Assert.Equal(3, StripCommentsAndStrings(multi).Split('\n').Length);   // 은퇴본: 1줄 잃는다
        Assert.Equal(4, SourceText.ExecutableCode(multi).Split('\n').Length); // 정본: 그대로
    }

    // ══════════════════════════════════════════════════════════════════════
    // 🔴 G-B1-G-2 — 성공 블록이 결과를 **단정하지 않는다** (갈래 G · P1-2)
    //   과녁은 「발송」 어간이 아니라 **완료형 3모양**이다 (§19 판정2 — 어간이면 오탐 5건).
    //   ⓐ `@if (_submitted)` **안**  ⓑ 파일 **전체**(성공 블록 밖으로 새는 길)
    //   ⓒ 🔴 오탐 대조군 — 미래형 약속 2문장은 **그대로 살아 있어야** 한다
    // ══════════════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-B1-G-2 성공 블록(@if (_submitted)) 안에 완료형 발송 단정이 없다")]
    public void G_B1_G_2_성공블록이_결과를_단정하지_않는다()
    {
        var razor = ReadRazor();
        var clean = ScreenTextWithoutComments(razor);

        // ── ⓐ 성공 블록 안 — 완료형 3모양 전부 금지
        var m = Regex.Match(clean, @"@if\s*\(\s*_submitted\s*\)\s*\{");
        Assert.True(m.Success,
            "G-B1-G-2 FAIL — 성공 블록(@if (_submitted))을 찾지 못했다. 화면 구조가 바뀌었으면 이 게이트를 고쳐라.");
        var successBlock = Braced(clean, m.Index + m.Length - 1);

        foreach (var shape in SendCompletedShapes)
        {
            Assert.True(!successBlock.Contains(shape, StringComparison.Ordinal),
                $"G-B1-G-2 FAIL — 성공 블록 안에 완료형 단정 「{shape}」이 글자로 박혀 있다. " +
                "메일이 0통 나가도, 초안이 안 붙어도 손님은 이 문장을 본다. " +
                "메일에 관한 문장은 서버가 말한 그대로(@MailNotice) 보여 준다.\n성공 블록:\n" + successBlock);
        }

        // ── ⓑ 파일 전체 — 성공 블록 밖으로 새는 길도 막는다(검증팀 보강 · §19 판정2)
        foreach (var shape in SendCompletedShapes)
        {
            var hits = LinesContaining(clean, shape);
            Assert.True(hits.Count == 0,
                $"G-B1-G-2 FAIL — 화면에 완료형 단정 「{shape}」이 새로 생겼다(성공 블록 밖). " +
                "주석은 이미 벗긴 뒤다 ⇒ 이건 손님이 읽는 글자다.\n" + string.Join("\n", hits));
        }

        // ── ⓒ 🔴 오탐 대조군 — 미래형 약속은 **건드리지 않는다**(§19 판정2).
        //    이 둘이 사라지면 「어간으로 넓히기」로 되돌아간 것이다. 그건 PM 이 반려한 처방이다.
        foreach (var promise in FutureTensePromises)
        {
            Assert.True(clean.Contains(promise, StringComparison.Ordinal),
                $"G-B1-G-2 FAIL — 미래형 약속 문장이 사라졌다: 「{promise}」. " +
                "이 문장은 정당하다(값이 들어오면 참이 되는 약속). 과녁은 완료형 단정뿐이다.");
        }

        // ── ⓓ 게이트가 비어 돌지 않는다 — 과녁 3모양이 실제로 선언돼 있나
        Assert.Equal(3, SendCompletedShapes.Length);
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
    // 🔴 20261015작B1 갈래 G(G-1 · P1-3 앞) — **교체 완료.** 바로 위 줄에 있던
    //    「D 헬퍼로 교체 필요 … 지금 바닥엔 그 파일이 없어 이 클래스 안에만 둔다」는 합류로 해소됐다.
    //
    //   바뀐 것 — 호출 자리 3곳이 공용 SourceText(src/HitPan.Tests/Integrity/SourceText.cs) 로 간다:
    //     ① G-B1-F-2  StripCommentsKeepText(…)      → ScreenTextWithoutComments(…)  = @* *@ 선처리 + SourceText.StripComments
    //     ② CodeBlockOf            StripCommentsAndStrings(…) → SourceText.ExecutableCode(StripRazorComments(…))
    //     ③ ServerSuccessPayloadKeys  StripCommentsAndStrings(…) → SourceText.ExecutableCode(…)   (.cs 라 @* *@ 없음)
    //
    //   🔴 **안 바꾼 것과 그 사유** — `StripRazorComments` 는 **살아 있다.**
    //     SourceText 는 `//`·`/* */`·문자열·문자 리터럴만 안다. Razor 주석 `@* *@` 는 **모른다**
    //     (SourceText.cs 의 Scan 에 그 가지가 없다). 억지로 밀어 넣지 않았다 — 화면 파일의
    //     `@* *@` 주석 안에 「발송했습니다」가 실제로 있어서(razor:31) 그 한 가지를 못 벗기면
    //     F-2·G-2 가 **자기 경고문 때문에 FAIL** 한다. 그래서 `@* *@` 는 지역 선처리로 남긴다.
    //     공용으로 올리는 것은 SourceText 의 계약 변경이라 갈래 G 담당표 밖 ⇒ **PM 결재 거리**.
    //
    //   🔴 **은퇴(지우지 않는다 · #1)** — 아래 `StripCommentsKeepText`·`StripCommentsAndStrings` 는
    //     더 이상 게이트의 판정 경로가 아니다. [Obsolete] **속성은 붙이지 않았다**: 붙이면 호출하는
    //     자리에서 CS0618 경고가 나 경고 0(#19)이 깨진다. 대신 글자로 은퇴를 적고,
    //     **G-B1-G-1 이 이 둘을 불러 「왜 은퇴했나」를 숫자로 고정**한다(지워지지도, 되살아나지도 않게).

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

    /// <summary>
    /// 🔴 Razor 주석 <c>@* *@</c> 만 떼는 선처리. <b>SourceText 가 모르는 유일한 모양</b>이라 지역에 남는다.
    /// 줄 수는 안 지킨다 — 줄번호를 보고하지 않는 자리(CodeBlockOf·FallbackSentenceOf)에서만 쓴다.
    /// </summary>
    private static string StripRazorComments(string s) =>
        Regex.Replace(s, @"@\*.*?\*@", " ", RegexOptions.Singleline);

    /// <summary>
    /// 🔴 20261015작B1 갈래 G(G-1) — G-B1-F-2·G-B1-G-2 가 쓰는 <b>정본 벗기개</b>.
    /// <c>@* *@</c> 만 지역에서 떼고(줄 수 보존) <b>나머지는 공용 <see cref="SourceText.StripComments"/></b> 가 한다.
    /// 문자열·마크업 글자는 남는다 — 과녁이 「화면이 박아 둔 글자」라서 벗기면 과녁이 사라진다.
    /// </summary>
    private static string ScreenTextWithoutComments(string razor)
    {
        var razorStripped = Regex.Replace(
            razor, @"@\*.*?\*@", m => BlankKeepLines(m.Value), RegexOptions.Singleline);
        return SourceText.StripComments(razorStripped);
    }

    /// <summary>
    /// ⬛ <b>은퇴(2026-10-15 갈래 G-1)</b> — 판정 경로가 아니다. 지우지 않는다(#1).
    /// 정본은 <see cref="ScreenTextWithoutComments"/> 다. 이 함수는 <b>G-B1-G-1 의 대조군</b>으로만 남는다:
    /// 정규식 <c>/\*.*?\*/</c> + <c>://</c> 예외 한 개라서 <b>문자열 안의 <c>//</c></b> 를 주석으로 오인해
    /// 그 줄의 뒤를 통째로 날린다(= 뒤에 있던 과녁 글자가 안 세어진다).
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
        // 🔴 갈래 G(G-1) — 공용 SourceText.ExecutableCode. @* *@ 만 지역 선처리.
        return SourceText.ExecutableCode(StripRazorComments(Braced(razor, open)));
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
    /// ⬛ <b>은퇴(2026-10-15 갈래 G-1)</b> — 판정 경로가 아니다. 지우지 않는다(#1).
    /// 정본은 <see cref="SourceText.ExecutableCode"/> 다. <b>G-B1-G-1 의 대조군</b>으로만 남는다:
    /// 블록주석을 먹을 때 <c>i + 1 &lt; s.Length</c> 로 끝을 찾아 <b>닫히지 않은 <c>/*</c></b> 에서
    /// 남은 글자를 통째로 버리고, 원시 문자열 <c>"""…"""</c> 도 모른다.
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
        // 🔴 갈래 G(G-1) — .cs 라 @* *@ 가 없다. 공용 SourceText 를 바로 쓴다.
        var src = SourceText.ExecutableCode(ReadController());
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
