using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261014작1 — <b>「본사공지사항」 화면·입구 게이트</b> (G-W9-7 · G-W9-12 · G-W9-13).
///
/// <para>사장님 오더(10/14) = 공지사항 ↔ CS쪽지 분리. 화면을 둘로 가르면 <b>되돌아갈 자리</b>와
/// <b>보러 갈 자리</b>도 함께 갈려야 한다 — 글자는 갈라 놓고 목적지가 같으면 그 글자가 거짓이다.</para>
/// </summary>
public sealed class ErpCsNoticeScreenGateTests : IDisposable
{
    private readonly ErpCsGateDb _db = new("HITPAN_W9_NOTICE_DB", "w9notice");

    private static string Support(string file) =>
        File.ReadAllText(Path.Combine(ErpCsGateDb.RepoRoot(), "src", "HitPan.Web", "Pages", "Support", file));

    // ══════════════════════════════════════════════════════════════
    // G-W9-7 — 목록이 **공지 화면 모델까지** 터지지 않고 닿는다 (1V C-6)
    //   본사 선발신은 올려보낼 일이 없어 cs_outbox 행이 **없다** ⇒ LEFT JOIN 이 NULL 을 준다.
    //   그 NULL 이 비널러블 int 에 들어가면 역직렬화가 터지고 **목록 전부가 가려진다**(10/12 실측).
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W9-7 🔴 공지 목록이 직렬화→화면 모델 역직렬화로 닿는다 — 큐 행이 없는 본사 쪽지에서도 안 터진다")]
    public async Task G_W9_7_공지목록이_화면모델까지_닿는다()
    {
        const string gate = "G-W9-7 공지 목록 역직렬화";
        if (!await _db.ReadyAsync(gate)) return;

        await using var erp = await _db.OpenAsync();
        await ErpCsGateDb.SeedUsersAsync(erp);
        // 🔴 cs_outbox 행을 **일부러 만들지 않는다** — 본사 선발신의 실제 모양이다.
        await ErpCsGateDb.InsertAsync(erp, "hq-1", "hq", "[프로모션] 10월 안내\n본문입니다", minutesAgo: 10);

        var res = await ErpCsGateDb.NewController(erp).List("hq", default);
        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(res);
        var data = ok.Value!.GetType().GetProperty("data")!.GetValue(ok.Value!);

        // 화면이 지나는 길 그대로 — JSON 을 거쳐 **비널러블 int 가 있는 모델**로 받는다
        var json = JsonSerializer.Serialize(data);
        var items = JsonSerializer.Deserialize<List<NoticeItemMirror>>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Single(items);
        Assert.Equal("hq-1", items[0].CsRequestId);
        Assert.Equal(0, items[0].AttemptCount);     // 🔴 서버가 0 을 보장한다(COALESCE) — null 이면 위에서 터진다
        Assert.Equal(0, items[0].ReplyCount);
        Assert.Equal("hq", items[0].Origin);

        // 제목 자리 — 본사는 본문 머리에 「[종류] 제목」 한 줄을 얹어 보낸다. 화면이 그 줄을 제목으로 쓴다.
        Assert.StartsWith("[프로모션]", items[0].Body);
    }

    // ══════════════════════════════════════════════════════════════
    // G-W9-12 — 옛 주소 생존 + 새 주소 신설 (둘 다 있어야 통과 · #1)
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W9-12 🔴 /support/cs 가 그대로 살아 있고 /support/notice 가 신설됐다 (옛 주소를 바꿔 끼우지 않았다)")]
    public void G_W9_12_옛주소_생존_새주소_신설()
    {
        Assert.Contains("@page \"/support/cs\"", Support("CsRequestPage.razor"));
        Assert.Contains("@page \"/support/notice\"", Support("CsNoticePage.razor"));

        // 상세는 **공유**한다 — 새로 만들지 않았다(판정이 둘이 되면 어긋난다)
        Assert.Contains("@page \"/support/cs/{RequestId}\"", Support("CsInboxPage.razor"));

        // 공지 화면은 **읽기 전용**이다 — 쓰는 칸·보내기 통로가 없다
        var notice = Support("CsNoticePage.razor");
        Assert.DoesNotContain("PostAsJsonAsync", notice);
        Assert.DoesNotContain("MudTextField", notice);

        // 🔴 빈 상태는 **사람 말**이다 — 개발 용어가 있으면 반려(#23)
        Assert.Contains("아직 본사에서 보낸 안내가 없습니다", notice);
        foreach (var jargon in new[] { "컬럼", "마이그", "DB", "스키마", "null" })
            Assert.DoesNotContain(jargon, NoticeVisibleText(notice));
    }

    // ══════════════════════════════════════════════════════════════
    // G-W9-13 — 입구 분기: 배너·상세 [목록으로]
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W9-13 🔴 배너 [안내 보러 가기] 와 상세 [목록으로] 가 공지 쪽으로 갈라진다 (글자만 갈라 놓지 않았다)")]
    public void G_W9_13_입구가_갈라진다()
    {
        var badge = File.ReadAllText(Path.Combine(ErpCsGateDb.RepoRoot(),
            "src", "HitPan.Web", "Components", "Common", "CsReplyBadge.razor"));

        // ① 배너 목적지가 `_hq`·`_reply` 판정으로 갈라진다 — 글자 판정과 **같은 조건**이어야 한다
        var nav = badge.Split('\n').FirstOrDefault(l => l.Contains("NavigateTo(") && l.Contains("/support/notice"));
        Assert.NotNull(nav);
        Assert.Contains("_hq", nav!);
        Assert.Contains("_reply", nav!);
        Assert.Contains("/support/cs", nav!);

        // ② 공지 화면에 서 있으면 배너를 또 띄우지 않는다
        Assert.Contains("/support/notice", badge[badge.IndexOf("IsOnSupportPage", StringComparison.Ordinal)..]);

        // ③ 상세 [목록으로] 가 `_isHq` 로 갈라진다 — **같은 판정 재사용**(새 판정 금지)
        var inbox = Support("CsInboxPage.razor");
        var back = inbox.Split('\n').FirstOrDefault(l => l.Contains("NavigateTo(") && l.Contains("/support/notice"));
        Assert.NotNull(back);
        Assert.Contains("_isHq", back!);
        Assert.Contains("/support/cs", back!);
    }

    // ══════════════════════════════════════════════════════════════
    // G-W9-16 — 🔴 ① 화면이 **자기 쪽만** 묻는다 (사장님 오더 이행을 묶는 자리)
    //   [4] 적발: 이 한 줄이 빠지면 공지가 계속 섞여 **오더 미이행**인데 묶는 게이트가 0건이었다.
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W9-16 🔴 「고객지원 요청」이 ?origin=local 로 묻고 공지 화면은 ?origin=hq 로 묻는다 (분리의 본체)")]
    public void G_W9_16_두_화면이_자기_쪽만_묻는다()
    {
        Assert.Contains("api/cs/requests?origin=local", Support("CsRequestPage.razor"));
        Assert.Contains("api/cs/requests?origin=hq", Support("CsNoticePage.razor"));

        // 🔴 쪽을 안 묻는 **목록 조회**가 남아 있으면 분리가 새는 자리다.
        //   단 쪽지를 **보내는** POST 는 같은 주소를 쓴다(`PostAsJsonAsync("api/cs/requests", …)`) —
        //   그건 분리와 무관하다. 그래서 「목록을 받아오는 호출」만 골라 본다
        //   (이 게이트가 처음에 그 POST 를 분리 누락으로 오인해 빨개졌다).
        foreach (var f in new[] { "CsRequestPage.razor", "CsNoticePage.razor" })
        {
            var gets = Support(f).Split('\n').Where(l => l.Contains("GetFromJsonAsync")).ToList();
            Assert.NotEmpty(gets);
            Assert.All(gets, l => Assert.Contains("?origin=", l));
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-W9-18 — 화면 머리글이 메뉴 낱말과 같고, 부제가 거짓이 아니다 ([4] 조건 C-1)
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W9-18 🔴 ① 화면 제목이 메뉴와 같은 「고객지원 요청」이고 부제가 더는 「함께 봅니다」라 말하지 않는다")]
    public void G_W9_18_제목과_부제가_거짓이_아니다()
    {
        var page = Support("CsRequestPage.razor");
        var visible = NoticeVisibleText(page);

        Assert.Contains("<PageTitle>고객지원 요청</PageTitle>", page);
        Assert.Contains("Title=\"고객지원 요청\"", page);
        // 🔴 본사 안내를 갈라냈으므로 「함께 봅니다」는 거짓이다
        Assert.DoesNotContain("함께 봅니다", visible);
        // 어디로 가야 하는지는 말해 준다
        Assert.Contains("본사공지사항", visible);
    }

    // ══════════════════════════════════════════════════════════════
    // G-W9-20 — ① 목록도 같은 게시판 규격을 쓴다 ([4] 디자인검증 F-2)
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W9-20 🔴 두 목록이 같은 게시판 규격 클래스를 쓴다 — ① 이 혼자 다른 집이 아니다")]
    public void G_W9_20_두_목록이_한_집이다()
    {
        foreach (var f in new[] { "CsRequestPage.razor", "CsNoticePage.razor" })
        {
            var src = Support(f);
            Assert.Contains("hp-cs-board", src);
            Assert.Contains("hp-cs-row", src);
            Assert.Contains("hp-cs-row-title", src);
            Assert.Contains("hp-cs-row-meta", src);
            Assert.Contains("hp-cs-row-chips", src);
        }
        // ①은 칩이 최대 4개라 아래 줄로 떨어뜨리는 보조 클래스를 쓴다(제목을 지킨다)
        Assert.Contains("hp-cs-row-chips--many", Support("CsRequestPage.razor"));
    }

    /// <summary>
    /// 화면에 보이는 글자만 남긴다 — 개발 용어 금지(#23)는 <b>보이는 글자</b>에 대한 규칙이다.
    /// 🔴 줄 머리로 거르면 안 된다 — 여러 줄 주석의 가운데 줄은 아무 글자로나 시작한다
    /// (이 게이트가 처음에 **자기 주석**에 걸려 빨개졌다). <b>블록째로</b> 걷어낸다.
    /// </summary>
    private static string NoticeVisibleText(string razor)
    {
        var body = Regex.Replace(razor, @"@\*.*?\*@", " ", RegexOptions.Singleline);   // Razor 주석 블록
        var code = body.IndexOf("@code {", StringComparison.Ordinal);
        if (code > 0) body = body[..code];                                            // @code 뒤는 화면 글자가 아니다
        return body;
    }

    private sealed class NoticeItemMirror
    {
        public string CsRequestId { get; set; } = "";
        public string? Body { get; set; }
        public string Status { get; set; } = "";
        public string? Origin { get; set; }
        public DateTime CreatedAt { get; set; }
        public int AttemptCount { get; set; }
        public int ReplyCount { get; set; }
    }

    public void Dispose() => _db.Dispose();
}
