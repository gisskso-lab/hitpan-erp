using System.Text.Json;
using Dapper;
using HitPan.Web.Services;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261014작1 — <b>「고객지원센터」 메뉴 게이트</b> (G-W9-6 · G-W9-8 ~ G-W9-11).
///
/// <para>사장님 결재 2건을 집행한다: <b>결-B</b> 업데이트 2종은 옮겨도 직원에게 안 보인다 ·
/// <b>결-C</b> 안읽음 숫자는 하위 둘에 각각.</para>
///
/// <para>🔴 <b>렌더 하네스가 0건</b>이라 화면이 실제로 어떻게 그려지는지는 이 게이트가 못 잰다.
/// 그래서 ① 서버→모델 경로는 <b>직렬화→역직렬화</b>로 재고 ② 메뉴 구조는 <b>소스 줄 순서</b>로 잰다.
/// 눈으로 보는 판정은 작업지시서 §8 사장님 실측표가 한다.</para>
/// </summary>
public sealed class ErpSupportCenterMenuGateTests : IDisposable
{
    private readonly ErpCsGateDb _db = new("HITPAN_W9_MENU_DB", "w9menu");

    private static string SidebarSource() =>
        File.ReadAllText(Path.Combine(ErpCsGateDb.RepoRoot(), "src", "HitPan.Web", "Layout", "Sidebar.razor"));

    // ══════════════════════════════════════════════════════════════
    // G-W9-6 — 안읽음 3칸이 **화면이 쓰는 모델까지** 닿는다 (결-C)
    //   🔴 미러 모델만 재면 거짓 통과한다 — 실물 Sidebar.razor 의 선언도 함께 대조한다.
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W9-6 🔴 안읽음 3칸이 직렬화→사이드바 모델 역직렬화로 다 찬다 + 실물 소스에 3칸 선언이 있다")]
    public async Task G_W9_6_안읽음_3칸이_모델까지_닿는다()
    {
        const string gate = "G-W9-6 안읽음 3칸";
        if (!await _db.ReadyAsync(gate)) return;

        await using var erp = await _db.OpenAsync();
        await ErpCsGateDb.SeedUsersAsync(erp);

        // 본사 안내 2건 + 내 문의에 온 답 1건 — 셋 다 안 읽은 상태로 앉힌다
        await ErpCsGateDb.InsertAsync(erp, "hq-1", "hq", "본사 공지 하나", minutesAgo: 20);
        await ErpCsGateDb.InsertAsync(erp, "hq-2", "hq", "본사 공지 둘", minutesAgo: 19);
        await ErpCsGateDb.InsertAsync(erp, "me-1", "local", "문의", minutesAgo: 18);
        await AddReplyAsync(erp, "r-hq-1", "hq-1", "본사");
        await AddReplyAsync(erp, "r-hq-2", "hq-2", "본사");
        await AddReplyAsync(erp, "r-me-1", "me-1", "상담원");

        var res = await ErpCsGateDb.NewController(erp).UnreadCount(default);
        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(res);

        // 🔴 화면이 지나는 길 그대로 — 사전으로 읽지 않는다(10/12 사고: 사전으로 읽어 통과하고 화면이 터졌다)
        var json = JsonSerializer.Serialize(ok.Value);
        var model = JsonSerializer.Deserialize<SidebarUnreadMirror>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal(3, model.Count);        // 합계는 종전 그대로 산다(#1 — 지우지 않았다)
        Assert.Equal(2, model.HqCount);      // 「본사공지사항」 쪽
        Assert.Equal(1, model.ReplyCount);   // 「고객지원 요청」 쪽

        // 🔴 실물 소스 대조 — 미러만 재면 실물 모델에 칸이 없어도 초록이 된다
        var src = SidebarSource();
        Assert.Contains("public int HqCount", src);
        Assert.Contains("public int ReplyCount", src);
        Assert.Contains("public int Count", src);
        // 숫자를 하위 둘에 각각 붙였나 — 두 필드가 실제로 화면에서 쓰이는지
        Assert.Contains("_csHqUnread", src);
        Assert.Contains("_csReplyUnread", src);
    }

    // ══════════════════════════════════════════════════════════════
    // G-W9-8 — 「고객지원센터」 묶음 + 하위 2개
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W9-8 🔴 사이드바에 「고객지원센터」 묶음이 서고 하위에 고객지원 요청·본사공지사항이 있다")]
    public void G_W9_8_묶음과_하위2개()
    {
        var src = SidebarSource();

        Assert.Contains("<MudNavGroup Title=\"고객지원센터\"", src);

        var group = GroupBlock(src, "고객지원센터");
        Assert.Contains("Href=\"/support/cs\"", group);
        Assert.Contains("고객지원 요청", group);
        Assert.Contains("Href=\"/support/notice\"", group);
        Assert.Contains("본사공지사항", group);

        // 🔴 묶음 머리에는 합계를 안 붙인다(결-C) — 머리 줄에 칩이 없다
        var head = group.Split('\n')[0];
        Assert.DoesNotContain("MudChip", head);
    }

    // ══════════════════════════════════════════════════════════════
    // G-W9-9 — 업데이트 2종이 **가드 안**이고 자료관리에서 빠졌다 (결-B)
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W9-9 🔴 업데이트 2종이 고객지원센터 안 부모계정 가드 안에 있고, 자료관리 묶음에선 빠졌다")]
    public void G_W9_9_가드_안에_있고_자료관리에선_빠졌다()
    {
        var src = SidebarSource();

        var support = GroupBlock(src, "고객지원센터");
        var data = GroupBlock(src, "자료관리");

        // ① 자료관리 묶음에는 **없다** (옮겼다)
        Assert.DoesNotContain("LocalSwapUiText.UpdatePath", data);
        Assert.DoesNotContain("LocalSwapUiText.RollbackPath", data);
        // 자료관리의 나머지는 그대로 산다 — 묶음을 지운 게 아니다(#1)
        Assert.Contains("/data/backup", data);
        Assert.Contains("/data/reset", data);

        // ② 고객지원센터 묶음에는 **있다**
        Assert.Contains("LocalSwapUiText.UpdatePath", support);
        Assert.Contains("LocalSwapUiText.RollbackPath", support);

        // ③ 🔴 그 두 줄이 `@if (_isTenantAdmin)` **뒤**에 있다 — 줄 순서로 대조한다.
        //    가드가 없거나 두 줄이 가드 앞에 있으면 직원 화면에 뜬다(결-B 위반).
        var guard = support.IndexOf("@if (_isTenantAdmin)", StringComparison.Ordinal);
        var update = support.IndexOf("LocalSwapUiText.UpdatePath", StringComparison.Ordinal);
        var rollback = support.IndexOf("LocalSwapUiText.RollbackPath", StringComparison.Ordinal);
        Assert.True(guard >= 0, "고객지원센터 묶음 안에 부모계정 가드가 없다 — 직원에게 업데이트 2종이 보인다(결-B 위반)");
        Assert.True(update > guard, "업데이트 메뉴가 가드 앞에 있다 — 직원에게 보인다(결-B 위반)");
        Assert.True(rollback > guard, "되돌리기 메뉴가 가드 앞에 있다 — 직원에게 보인다(결-B 위반)");

        // ④ 하위 2개(고객지원 요청·본사공지사항)는 가드 **밖**이다 — CS 는 모두의 일이다
        var cs = support.IndexOf("Href=\"/support/cs\"", StringComparison.Ordinal);
        var notice = support.IndexOf("Href=\"/support/notice\"", StringComparison.Ordinal);
        Assert.True(cs < guard && notice < guard, "고객지원 요청·본사공지사항이 부모계정 가드 안에 들어갔다 — 직원이 CS 를 못 쓴다");
    }

    // ══════════════════════════════════════════════════════════════
    // G-W9-10 — 🔴 경로 2개·낱말 2개 무변경 (1V C-7)
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W9-10 🔴 업데이트·되돌리기의 경로 2개와 낱말 2개는 자리를 옮겨도 한 글자도 안 바뀐다")]
    public void G_W9_10_경로와_낱말_무변경()
    {
        Assert.Equal("/data/update", LocalSwapUiText.UpdatePath);
        Assert.Equal("/data/rollback", LocalSwapUiText.RollbackPath);
        Assert.Equal("최신 버전 확인/업데이트", LocalSwapUiText.MenuUpdate);
        Assert.Equal("이전 버전으로 되돌리기", LocalSwapUiText.MenuRollback);
    }

    // ══════════════════════════════════════════════════════════════
    // G-W9-11 — 안내 글자가 새 대메뉴 이름이다 (A-2)
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W9-11 🔴 사이드바 안내 링크 글자가 「고객지원센터 ›」 이고, Web 소스에 「자료관리 ›」 가 0건이다")]
    public void G_W9_11_안내_글자가_거짓이_아니다()
    {
        Assert.StartsWith("고객지원센터 ›", LocalSwapUiText.SidebarIssueLink);
        Assert.Contains(LocalSwapUiText.MenuUpdate, LocalSwapUiText.SidebarIssueLink);

        // 🔴 같은 거짓말이 다른 자리에 남아 있지 않은지 Web 소스를 전수로 본다.
        var web = Path.Combine(ErpCsGateDb.RepoRoot(), "src", "HitPan.Web");
        var stale = Directory.EnumerateFiles(web, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
                      || f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("자료관리 ›"))
            .ToList();

        Assert.True(stale.Count == 0,
            "메뉴가 고객지원센터로 옮겨갔는데 「자료관리 ›」 라고 말하는 자리가 남았다:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>해당 MudNavGroup 의 머리부터 그 묶음이 닫히는 줄까지를 잘라 준다(중첩 없음 — 사이드바는 1단이다).</summary>
    private static string GroupBlock(string src, string title)
    {
        var start = src.IndexOf($"<MudNavGroup Title=\"{title}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"「{title}」 묶음을 사이드바에서 못 찾았다");
        var end = src.IndexOf("</MudNavGroup>", start, StringComparison.Ordinal);
        Assert.True(end > start, $"「{title}」 묶음이 닫히지 않았다");
        return src[start..end];
    }

    private static async Task AddReplyAsync(MySqlConnector.MySqlConnection erp, string replyId, string requestId, string kind) =>
        await erp.ExecuteAsync(@"
            INSERT INTO cs_replies (reply_id, tenant_id, cs_request_id, body, replied_by_kind, replied_at)
            VALUES (@Id, @Tid, @Req, '내용', @Kind, UTC_TIMESTAMP(6))",
            new { Id = replyId, Tid = ErpCsGateDb.TenantId, Req = requestId, Kind = kind });

    /// <summary>
    /// 🔴 사이드바의 <c>CsUnreadResponse</c> 는 private 이라 시험이 직접 못 쓴다 ⇒ **같은 모양의 미러**로
    /// 역직렬화해서 길을 재고, 실물 선언은 G-W9-6 이 소스 문자열로 함께 대조한다(미러만 재면 거짓 통과).
    /// </summary>
    private sealed class SidebarUnreadMirror
    {
        public bool Success { get; set; }
        public int Count { get; set; }
        public int HqCount { get; set; }
        public int ReplyCount { get; set; }
    }

    public void Dispose() => _db.Dispose();
}
