using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using HitPan.API.BackgroundServices;
using HitPan.API.Controllers;
using HitPan.API.Services.Headquarters;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261014작3 — <b>공지 종류 필터 게이트</b> (G-W11-1 ~ G-W11-8).
/// 일정표 §3 순서 3 · M-3 · 사장님 결재 <b>결-D = ⓑ</b>(본문 글자를 되읽는 ⓐ 를 버리고 <b>칸</b>을 신설).
///
/// <para><b>이 묶음이 지키는 사실 넷</b>
/// ① 받은 종류가 <b>칸에 값으로</b> 앉는다(낱말이 아니라 값 — 낱말을 바꿔도 필터가 안 깨진다)
/// ② 칸이 아직 없는 DB 에서도 <b>수신과 목록이 안 죽는다</b>(업데이트 직후 마이그 전 1회가 그 상태다)
/// ③ 거르는 일을 <b>서버가</b> 한다 — 쪽별 <c>LIMIT 200</c> 밖 쪽지까지 데려온다
/// ④ 「기타 공지사항」을 고르면 <b>옛 쪽지(값이 비어 있는 것)까지</b> 함께 온다</para>
///
/// <para>🔴 <b>기대값의 출처를 적는다</b>(작2 에서 배운 것 — 배선 게이트와 내용 게이트는 다른 물건이다).
/// 여기서 비교하는 숫자·값은 <b>시험이 봉투에 넣은 것</b>과 <b>시험이 센 행 수</b>다 — 과녁의 코드에서
/// 가져온 기대값이 아니다. 낱말 <b>내용</b>을 박는 자리는 작2 의 <c>G-W10-7</c> 이다.</para>
/// </summary>
public sealed class ErpCsPurposeFilterGateTests : IDisposable
{
    private readonly ErpCsGateDb _db = new("HITPAN_W11_FILTER_DB", "w11filter");

    private static CsOutboxSenderWorker.OutboundDto Out(string id, string? purpose, int minutesAgo) => new()
    {
        OutboundId = id,
        Purpose = purpose,
        Title = $"{purpose ?? "없음"} 제목",
        Body = "본문입니다",
        CreatedAt = DateTime.UtcNow.AddMinutes(-minutesAgo),
    };

    private static async Task ReceiveAsync(MySqlConnector.MySqlConnection erp,
        params CsOutboxSenderWorker.OutboundDto[] items) =>
        await CsOutboxSenderWorker.ReceiveOutboundAsync(
            erp, new AckHq(items.Length), "w11owner", items.ToList(), NullLogger.Instance, default);

    /// <summary>목록을 **화면이 지나는 길** 그대로 받는다 — 직렬화를 거쳐 모델로 역직렬화한다.</summary>
    private static async Task<List<NoticeMirror>> ListAsync(
        MySqlConnector.MySqlConnection erp, string? origin, string? purpose)
    {
        CsRequestController.ResetPurposeColumnCache();
        CsRequestController.ResetOriginColumnCache();

        var res = await ErpCsGateDb.NewController(erp).List(origin, purpose, default);
        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(res);
        var data = ok.Value!.GetType().GetProperty("data")!.GetValue(ok.Value!);
        var json = JsonSerializer.Serialize(data);
        return JsonSerializer.Deserialize<List<NoticeMirror>>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static async Task DropPurposeColumnAsync(MySqlConnector.MySqlConnection erp)
    {
        await erp.ExecuteAsync("ALTER TABLE cs_requests DROP COLUMN purpose");
        CsRequestController.ResetPurposeColumnCache();
    }

    // ══════════════════════════════════════════════════════════════
    // G-W11-1 — 🔴 받은 종류가 **칸에 값으로** 앉는다 (허용값 전수)
    //   기대값 출처 = **시험이 봉투에 넣은 값**. 과녁에서 가져오지 않는다.
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W11-1 🔴 본사가 보낸 종류가 용도 칸에 값으로 앉는다 (전수) — 낱말이 아니라 값이다")]
    public async Task G_W11_1_받은_종류가_칸에_값으로_앉는다()
    {
        const string gate = "G-W11-1 용도 칸 저장";
        if (!await _db.ReadyAsync(gate)) return;

        await using var erp = await _db.OpenAsync();
        await ErpCsGateDb.SeedUsersAsync(erp);

        var sent = new[] { "update", "notice", "promo", "etc", "event", "billing" };
        await ReceiveAsync(erp, sent.Select((p, i) => Out($"w11-{p}", p, i + 1)).ToArray());

        foreach (var p in sent)
        {
            var stored = await erp.ExecuteScalarAsync<string?>(
                "SELECT purpose FROM cs_requests WHERE cs_request_id = @Id", new { Id = $"w11-{p}" });

            // 🔴 **값**이 그대로 있어야 한다 — 낱말(「업데이트공지사항」 등)이 들어가 있으면
            //   낱말을 고치는 차수마다 필터가 조용히 깨진다(결-D ⓑ 를 고른 이유).
            Assert.Equal(p, stored);
        }

        // 🔴 고객이 쓴 문의에는 종류가 없다 — 본사 발신 축이다.
        await ErpCsGateDb.InsertAsync(erp, "w11-local", "local", "내 문의입니다", minutesAgo: 1);
        var localPurpose = await erp.ExecuteScalarAsync<string?>(
            "SELECT purpose FROM cs_requests WHERE cs_request_id = 'w11-local'");
        Assert.Null(localPurpose);
    }

    // ══════════════════════════════════════════════════════════════
    // G-W11-2 — 🔴 **칸이 없는 DB 에서도 수신과 목록이 안 죽는다**
    //   업데이트 직후 마이그 적용 전 1회가 정확히 그 상태다. 그때 1054 가 나면
    //   목록이 통째로 죽고 **본사 답변까지 못 보인다**(10/12 사고와 같은 모양).
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W11-2 🔴 용도 칸이 없는 DB 에서도 수신이 돌고 목록이 안 죽는다 (마이그 전 1회를 견딘다)")]
    public async Task G_W11_2_칸없는_DB에서도_안_죽는다()
    {
        const string gate = "G-W11-2 칸 없는 DB";
        if (!await _db.ReadyAsync(gate)) return;

        await using var erp = await _db.OpenAsync();
        await ErpCsGateDb.SeedUsersAsync(erp);

        // 🔴 **칸을 실제로 뗀다.** 글자로 흉내내면 이 게이트는 아무것도 재지 않는다.
        await DropPurposeColumnAsync(erp);

        // ① 수신이 돈다
        await ReceiveAsync(erp, Out("w11-nocol", "update", 2));
        var body = await erp.ExecuteScalarAsync<string?>(
            "SELECT body FROM cs_requests WHERE cs_request_id = 'w11-nocol'");
        Assert.StartsWith("[업데이트공지사항]", body);   // 본문 머리 한 줄은 그대로다

        // ② 목록이 돈다 — 종류를 물어도 죽지 않는다
        var all = await ListAsync(erp, "hq", null);
        Assert.Single(all);
        Assert.Null(all[0].Purpose);          // 칸이 없으면 NULL 로 내려온다
        Assert.False(string.IsNullOrEmpty(all[0].PurposeLabel));   // 낱말은 그래도 온다

        // ③ 🔴 칸이 없는데 종류를 걸면 **전체**를 준다 — 비우면 거짓이다
        //    (칸이 없으면 모든 공지가 「기타」이므로 「기타」를 골랐을 때 0건이면 사라진 것처럼 보인다)
        var filtered = await ListAsync(erp, "hq", "etc");
        Assert.Single(filtered);
    }

    // ══════════════════════════════════════════════════════════════
    // G-W11-3 — 🔴 거르는 일을 **서버가** 한다
    //   화면에서 거르면 쪽별 LIMIT 200 밖 쪽지는 **아예 오지 않는다** ⇒ 옛 공지를 못 본다.
    //   기대값 출처 = **시험이 센 행 수**.
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W11-3 🔴 서버가 거른다 — LIMIT 밖으로 밀린 옛 쪽지도 그 종류를 고르면 온다")]
    public async Task G_W11_3_서버가_거른다()
    {
        const string gate = "G-W11-3 서버 필터";
        if (!await _db.ReadyAsync(gate)) return;

        await using var erp = await _db.OpenAsync();
        await ErpCsGateDb.SeedUsersAsync(erp);

        // 🔴 LIMIT(200)을 **넘기게** 쌓는다. 찾는 한 건은 **가장 오래된 자리**에 둔다 —
        //   화면 필터라면 이 건은 200건 안에 못 들어와 영원히 안 보인다.
        var items = new List<CsOutboxSenderWorker.OutboundDto>
        {
            Out("w11-old-update", "update", minutesAgo: 100000),
        };
        for (var i = 0; i < 230; i++) items.Add(Out($"w11-bulk-{i}", "notice", minutesAgo: i + 1));

        await ReceiveAsync(erp, items.ToArray());

        // 전체로 물으면 200건에서 끊기고 **옛 건은 안 보인다**(그게 LIMIT 다)
        var all = await ListAsync(erp, "hq", null);
        Assert.Equal(200, all.Count);
        Assert.DoesNotContain(all, x => x.CsRequestId == "w11-old-update");

        // 🔴 그 종류를 고르면 **온다** — 서버가 LIMIT 앞에서 걸렀기 때문이다
        var picked = await ListAsync(erp, "hq", "update");
        Assert.Single(picked);
        Assert.Equal("w11-old-update", picked[0].CsRequestId);
    }

    // ══════════════════════════════════════════════════════════════
    // G-W11-4 — 🔴 「기타 공지사항」이 **옛 쪽지까지** 데려온다 (1V A-1)
    //   옛 쪽지는 종류를 **모른다**(칸이 비어 있다). 화면에는 사장님 결-D 대로 「기타 공지사항」으로
    //   보인다 ⇒ 그 칩을 눌렀을 때 사라지면 **「쪽지가 없어졌다」로 보인다.**
    //   보이는 것과 걸러지는 것이 같아야 한다.
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W11-4 🔴 「기타 공지사항」을 고르면 종류를 모르는 옛 쪽지도 함께 온다 (사라지지 않는다)")]
    public async Task G_W11_4_기타가_옛쪽지까지_데려온다()
    {
        const string gate = "G-W11-4 기타 + 옛 쪽지";
        if (!await _db.ReadyAsync(gate)) return;

        await using var erp = await _db.OpenAsync();
        await ErpCsGateDb.SeedUsersAsync(erp);

        await ReceiveAsync(erp,
            Out("w11-etc", "etc", 3),        // 본사가 「기타 공지사항」으로 골라 보낸 것
            Out("w11-none", null, 4),        // 🔴 작3 이전에 내려온 옛 쪽지 모양 — 종류를 모른다
            Out("w11-update", "update", 5)); // 다른 종류

        var picked = await ListAsync(erp, "hq", "etc");
        var ids = picked.Select(x => x.CsRequestId).ToList();

        Assert.Contains("w11-etc", ids);
        Assert.Contains("w11-none", ids);      // 🔴 이 한 줄이 「쪽지가 없어졌다」를 막는다
        Assert.DoesNotContain("w11-update", ids);
        Assert.Equal(2, picked.Count);

        // 🔴 옛 쪽지도 화면에서는 「기타 공지사항」으로 보인다(사장님 결-D)
        var none = picked.Single(x => x.CsRequestId == "w11-none");
        Assert.Null(none.Purpose);                       // 저장은 「모른다」
        Assert.Equal("기타 공지사항", none.PurposeLabel); // 표시는 「기타」
    }

    // ══════════════════════════════════════════════════════════════
    // G-W11-5 — 낯선 값은 **전체**로 떨어진다 (화면을 비우지 않는다)
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W11-5 🔴 모르는 종류를 물어도 화면을 비우지 않고 전체를 준다")]
    public async Task G_W11_5_낯선값은_전체다()
    {
        const string gate = "G-W11-5 낯선 종류";
        if (!await _db.ReadyAsync(gate)) return;

        await using var erp = await _db.OpenAsync();
        await ErpCsGateDb.SeedUsersAsync(erp);

        await ReceiveAsync(erp, Out("w11-a", "update", 2), Out("w11-b", "notice", 3));

        var weird = await ListAsync(erp, "hq", "zzz-unknown-purpose");
        Assert.Equal(2, weird.Count);

        // 🔴 옛 값(event·billing)은 칩으로 세우지 않지만 **물으면 안 비운다** — 전체로 떨어진다.
        //   그 쪽지들은 「전체」에서 보여야 하고, 어떤 경우에도 사라지지 않는다.
        var legacy = await ListAsync(erp, "hq", "billing");
        Assert.Equal(2, legacy.Count);
    }

    // ══════════════════════════════════════════════════════════════
    // G-W11-6 — 🔴 **값과 낱말이 둘 다** 화면 모델까지 닿는다 + 칩 줄 재료도 서버가 준다
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W11-6 🔴 값과 낱말이 둘 다 화면 모델까지 닿고, 칩 줄에 세울 종류도 서버가 준다")]
    public async Task G_W11_6_값과_낱말이_둘_다_온다()
    {
        const string gate = "G-W11-6 값·낱말 동행";
        if (!await _db.ReadyAsync(gate)) return;

        await using var erp = await _db.OpenAsync();
        await ErpCsGateDb.SeedUsersAsync(erp);
        await ReceiveAsync(erp, Out("w11-pair", "notice", 2));

        var rows = await ListAsync(erp, "hq", null);
        var row = Assert.Single(rows);
        Assert.Equal("notice", row.Purpose);                    // 값 — 필터가 쓴다
        Assert.Equal("히트판 공지사항", row.PurposeLabel);        // 낱말 — 화면이 보여 준다

        // 🔴 칩 줄 재료 — 화면이 낱말표를 들지 않으려면 **서버가** 값과 낱말을 함께 줘야 한다
        CsRequestController.ResetPurposeColumnCache();
        var res = await ErpCsGateDb.NewController(erp).List("hq", null, default);
        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(res);
        var chips = ok.Value!.GetType().GetProperty("noticePurposes")!.GetValue(ok.Value!);
        var chipJson = JsonSerializer.Serialize(chips);
        var opts = JsonSerializer.Deserialize<List<PurposeOptionMirror>>(chipJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal(4, opts.Count);   // 쓰기 콤보 4종만 — 옛 값은 늘 0건이라 세우지 않는다
        Assert.All(opts, o => Assert.False(string.IsNullOrWhiteSpace(o.Label)));
        Assert.Contains(opts, o => o.Value == "update" && o.Label == "업데이트공지사항");
        Assert.Contains(opts, o => o.Value == "etc" && o.Label == "기타 공지사항");
        Assert.DoesNotContain(opts, o => o.Value == "billing");
    }

    // ══════════════════════════════════════════════════════════════
    // G-W11-7 — 🔴 **낱말 사본이 하나다**
    //   `HitPan.Web` 은 서버 코드를 참조하지 않는다 ⇒ 화면이 표를 들면 사본이 둘이 되고
    //   작2 가 그 자리에서 이미 어긋났다(본사 「요금 안내(옛 값)」 vs 고객 「과금결재 안내」).
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W11-7 🔴 화면이 발신종류 낱말표를 들지 않는다 — 낱말 사본은 서버 하나뿐")]
    public void G_W11_7_낱말_사본이_하나다()
    {
        var dir = Path.Combine(ErpCsGateDb.RepoRoot(), "src", "HitPan.Web");
        var files = Directory.GetFiles(dir, "*.razor", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.True(files.Count > 10, $"화면 파일을 못 읽었다({files.Count}) — 경로가 바뀌었나");

        // 사장님 낱말 4종 + 작2 가 고친 2종. 화면 어디에도 **글자로** 있어서는 안 된다.
        var labels = new[] { "업데이트공지사항", "히트판 공지사항", "프로모션 안내", "기타 공지사항", "과금결재 안내" };

        var bad = new List<string>();
        foreach (var f in files)
        {
            // 🔴 주석 블록은 통째로 걷어낸다 — 사유를 적어 둔 것은 기록이고 사본이 아니다.
            //   (작1 에서 게이트가 자기 주석에 걸려 빨개진 전례가 3회 있다.)
            var src = File.ReadAllText(f);
            src = Regex.Replace(src, @"@\*.*?\*@", " ", RegexOptions.Singleline);
            src = Regex.Replace(src, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            src = string.Join("\n", src.Split('\n')
                .Where(l => !l.TrimStart().StartsWith("//")));

            foreach (var label in labels)
                if (src.Contains(label, StringComparison.Ordinal))
                    bad.Add($"{Path.GetFileName(f)} → 「{label}」");
        }

        Assert.True(bad.Count == 0,
            "화면이 발신종류 낱말을 **글자로** 들고 있다 — 서버 낱말표와 사본이 둘이 된다:\n  "
          + string.Join("\n  ", bad)
          + "\n  낱말은 서버가 `purposeLabel`·`noticePurposes` 로 내려보낸다(설계 §3-1).");
    }

    // ══════════════════════════════════════════════════════════════
    // G-W11-8 — 🔴 **종류 낱말을 못 박은 자리가 0건이다** (작2 [4] 조건 C-5 봉합)
    //
    //   C-5 가 무엇이었나: 공지 한 줄에서 제목은 「[히트판 공지사항]」인데 칩은
    //   `KindLabel() => "본사 안내"` 로 **못 박혀** 있어 **같은 축을 두 번 말하고 값이 달랐다.**
    //
    //   🔴 PM 자기정정: 이 게이트의 첫 판은 **「본사 안내」라는 글자를 레포 전체에서 금지**했다.
    //   그러자 7파일이 빨개졌는데 **그게 제품 결함이 아니었다** — 「본사 안내를 불러오지
    //   못했습니다」·「본사 안내 {N}건 수신」 같은 **문장**들이다. 거기서 띄어쓰기를 없애면
    //   한국어가 깨진다. **설계 §4-2 의 「표기를 한 가지로」가 문장까지 덮도록 적힌 것이 틀렸다.**
    //   ⇒ 게이트를 **낱말로 쓰인 자리**만 보도록 좁혔다(느슨하게 푼 것이 아니라, 재는 과녁을
    //      제품의 사실에 맞게 고친 것이다 — 리뷰서 §에 그대로 적는다).
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W11-8 🔴 종류 낱말을 못 박아 돌려주는 자리가 0건 — 칩은 서버가 준 낱말을 쓴다 ([4] C-5)")]
    public void G_W11_8_낱말을_못박은_자리가_0건()
    {
        var roots = new[]
        {
            Path.Combine(ErpCsGateDb.RepoRoot(), "src", "HitPan.Web"),
            Path.Combine(ErpCsGateDb.RepoRoot(), "src", "HitPan.API", "Controllers"),
        };

        // 🔴 「값·속성에 종류 낱말을 **박아 넣는** 모양」만 잡는다.
        //   `=> "본사 안내"` · `Label = "본사 안내"` · `return "본사 안내"` 꼴.
        //   문장 안에 든 「본사 안내」(「… 불러오지 못했습니다」)는 **라벨이 아니다.**
        var hardcoded = new Regex(
            @"(=>|=|return)\s*""(본사\s*안내|업데이트공지사항|히트판 공지사항|프로모션 안내|기타 공지사항)""",
            RegexOptions.Compiled);

        var bad = new List<string>();
        foreach (var root in roots)
        foreach (var f in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".razor") || f.EndsWith(".cs"))
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                              && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var src = File.ReadAllText(f);
            src = Regex.Replace(src, @"@\*.*?\*@", " ", RegexOptions.Singleline);
            src = Regex.Replace(src, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            src = string.Join("\n", src.Split('\n')
                .Where(l => !l.TrimStart().StartsWith("//") && !l.TrimStart().StartsWith("///")));

            foreach (Match m in hardcoded.Matches(src))
                bad.Add($"{Path.GetFileName(f)} → {m.Value.Trim()}");
        }

        Assert.True(bad.Count == 0,
            "종류 낱말을 못 박아 돌려주는 자리가 있다 — 칩과 제목이 다른 말을 하게 된다([4] C-5):\n  "
          + string.Join("\n  ", bad)
          + "\n  낱말은 서버가 `purposeLabel`·`noticePurposes` 로 내려보낸다(설계 §3-1).");

        // 🔴 공지 화면에 **못 박은 종류 메서드가 없어야** 한다 — C-5 의 뿌리가 그 메서드였다.
        //   🔴 주석을 **블록째로** 걷어낸 뒤 센다. 이 트랙에서 게이트가 **자기·남의 주석**에 걸려
        //   빨개진 전례가 네 번째다(옛 이름을 이력으로 적어 두는 것은 기록이고 코드가 아니다).
        var notice = File.ReadAllText(Path.Combine(ErpCsGateDb.RepoRoot(),
            "src", "HitPan.Web", "Pages", "Support", "CsNoticePage.razor"));
        var noticeCode = Regex.Replace(notice, @"@\*.*?\*@", " ", RegexOptions.Singleline);
        noticeCode = string.Join("\n", noticeCode.Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//") && !l.TrimStart().StartsWith("///")));

        Assert.DoesNotContain("KindLabel", noticeCode);
        Assert.Contains("it.PurposeLabel", noticeCode);   // 서버가 준 낱말을 쓴다

        // 🔴 정본 쪽은 **있어야** 한다 — 둘 다 0건이면 낱말이 사라진 것이다(글자를 잘못 세는 것 방지)
        var worker = File.ReadAllText(Path.Combine(ErpCsGateDb.RepoRoot(),
            "src", "HitPan.API", "BackgroundServices", "CsOutboxSenderWorker.cs"));
        Assert.Contains("\"본사안내\"", worker);
    }

    /// <summary>가짜 본사 — 도착확인만 받는다(Pull 은 404 로 둔다 · 작2 선례).</summary>
    private sealed class AckHq : IHeadquartersClient
    {
        private readonly int _acked;
        public AckHq(int acked) => _acked = acked;

        public Task<(int StatusCode, string Body)> PostAsync(string path, string dataJson, string ownerAccountId, CancellationToken ct)
            => Task.FromResult((200, $"{{\"acked\":{_acked}}}"));

        public Task<(int StatusCode, string Body)> PullAsync(string path, string ownerAccountId, CancellationToken ct)
            => Task.FromResult((404, ""));

        public Task<(int StatusCode, string Body)> PostOwnerReportAsync(string ownerAccountId, CancellationToken ct)
            => Task.FromResult((200, "{\"success\":true}"));
    }

    /// <summary>🔴 공지 화면 모델의 거울 — 비널러블 int 를 그대로 둔다(10/12 전례).</summary>
    private sealed class NoticeMirror
    {
        public string CsRequestId { get; set; } = "";
        public string? Body { get; set; }
        public string Status { get; set; } = "";
        public string? Origin { get; set; }
        public DateTime CreatedAt { get; set; }
        public int AttemptCount { get; set; }
        public int ReplyCount { get; set; }
        public int UnreadCount { get; set; }
        public string? Purpose { get; set; }
        public string PurposeLabel { get; set; } = "";
    }

    private sealed class PurposeOptionMirror
    {
        public string Value { get; set; } = "";
        public string Label { get; set; } = "";
    }

    public void Dispose() => _db.Dispose();
}
