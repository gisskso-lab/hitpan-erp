using System.Reflection;
using System.Text.Json;
using Dapper;
using HitPan.API.BackgroundServices;
using HitPan.API.Services.Headquarters;
using HitPan.Backoffice.API.Controllers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261014작2 — <b>본사발신 낱말 정합 게이트</b> (G-W10-1 ~ G-W10-6).
/// 일정표 §3 순서 2 · M-2 + 결-A 흡수 · 10/13 백오피스 설계 §5-3 <b>결-12</b> 를 닫는 차수.
///
/// <para><b>왜 이 묶음이 필요하나</b>: [1-V] P-5 실측 — 이 자리를 도는 시험이 레포에 <b>0건</b>이었다.
/// <c>PurposeLabel</c> 을 읽는 시험도, <c>ReceiveOutboundAsync</c>(수신 사이클)를 돌리는 시험도 없었다.
/// 기존 <c>G-W7-4</c> 는 <b>백오피스가 옛 값을 저장하나</b>만 본다 — <b>고객 화면에 뭐라 보이나는
/// 아무도 안 셌다.</b> 그래서 백오피스가 6종을 허용하는데 ERP 는 3값만 알고 있었고,
/// <c>etc·update·notice</c> 셋이 전부 「본사안내」로 뭉개진 채 아무 빨간불도 안 켜졌다.</para>
///
/// <para>🔴 <b>두 쪽을 같은 파서로 읽지 않는다</b>(누적 교훈). 백오피스 허용값은 그 클래스의
/// <b>필드를 리플렉션으로</b> 꺼내고, ERP 낱말은 <c>PurposeLabel</c> 을 <b>실제로 호출해서</b> 받는다.
/// 어느 쪽도 문서·문자열 목록을 베껴 적은 게 아니다 — 한쪽이 바뀌면 이 게이트가 바로 어긋남을 본다.</para>
///
/// <para>🔴 그리고 4건은 <b>실제 DB 에서 수신 사이클을 돌린다</b>. 낱말은 본문 머리에 글자로 박혀
/// 저장되므로, 「표가 맞나」가 아니라 <b>「저장된 본문이 그 글자인가」</b>를 재야 한다
/// (게이트는 글자가 아니라 동작을 잰다 — 누적 30번째 교훈).</para>
/// </summary>
public sealed class ErpCsPurposeLabelGateTests : IDisposable
{
    private readonly ErpCsGateDb _db = new("HITPAN_W10_LABEL_DB", "w10label");

    /// <summary>ERP 낱말 표를 <b>실제로 호출해서</b> 받는다 — 목록을 베껴 적지 않는다.</summary>
    private static string ErpLabel(string? purpose)
    {
        var m = typeof(CsOutboxSenderWorker).GetMethod("PurposeLabel",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(m);   // 이름이 바뀌면 여기서 멈춘다 — 조용히 통과하지 않는다
        return (string)m!.Invoke(null, new object?[] { purpose })!;
    }

    /// <summary>백오피스 허용값을 <b>그 클래스의 필드에서</b> 꺼낸다 — ERP 쪽과 다른 길이다.</summary>
    private static HashSet<string> BackofficeAllowed()
    {
        var f = typeof(CsAdminController).GetField("AllowedOutboundPurpose",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(f);
        var set = (HashSet<string>)f!.GetValue(null)!;
        Assert.True(set.Count >= 6, $"백오피스 허용값이 6종보다 적다({set.Count}) — 보내는 쪽이 줄었나(#1)");
        return set;
    }

    // ══════════════════════════════════════════════════════════════
    // G-W10-1 — 🔴 백오피스가 보내는 값 **전수**가 ERP 에서 「본사안내」로 안 떨어진다
    //   이 한 건이 작2 의 본체다. 10/13 설계 §5-3 이 올린 결-12 가 바로 이 어긋남이다.
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W10-1 🔴 백오피스 허용 발신종류 전수가 ERP 에서 제 낱말을 받는다 — 「본사안내」로 떨어지는 값이 0건")]
    public void G_W10_1_허용값_전수가_제낱말을_받는다()
    {
        var allowed = BackofficeAllowed();

        var fellThrough = allowed.Where(p => ErpLabel(p) == "본사안내").ToList();

        Assert.True(fellThrough.Count == 0,
            "백오피스가 보내는데 ERP 가 모르는 발신종류가 있다 — 고객 화면에 「본사안내」로 뭉개진다.\n"
          + "  모르는 값: " + string.Join(", ", fellThrough) + "\n"
          + "  고칠 자리: src/HitPan.API/BackgroundServices/CsOutboxSenderWorker.cs PurposeLabel");

        // 🔴 낱말이 **서로 달라야** 한다 — 다 받았는데 두 종류가 같은 글자면 가른 뜻이 없다.
        var labels = allowed.Select(ErpLabel).ToList();
        Assert.Equal(labels.Count, labels.Distinct().Count());
    }

    // ══════════════════════════════════════════════════════════════
    // G-W10-2 — 🔴 **실제 DB 에서 수신 사이클을 돌려** 저장된 본문 머리를 읽는다 (허용값 전수)
    //   표가 맞는 것과 저장된 글자가 그것인 것은 다른 사실이다.
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W10-2 🔴 수신 사이클을 실제로 돌려 저장된 본문 머리가 제 낱말이다 (허용값 전수)")]
    public async Task G_W10_2_저장된_본문머리가_제낱말이다()
    {
        const string gate = "G-W10-2 본사발신 낱말 저장";
        if (!await _db.ReadyAsync(gate)) return;

        await using var erp = await _db.OpenAsync();
        await ErpCsGateDb.SeedUsersAsync(erp);

        var allowed = BackofficeAllowed().OrderBy(x => x, StringComparer.Ordinal).ToList();

        var items = allowed.Select((p, i) => new CsOutboxSenderWorker.OutboundDto
        {
            OutboundId = $"w10-{p}",
            Purpose = p,
            Title = $"{p} 제목",
            Body = "본문입니다",
            CreatedAt = DateTime.UtcNow.AddMinutes(-i - 1),
        }).ToList();

        var hq = new AckHq(items.Count);
        await CsOutboxSenderWorker.ReceiveOutboundAsync(
            erp, hq, "w10owner", items, NullLogger.Instance, default);

        foreach (var p in allowed)
        {
            var body = await Dapper.SqlMapper.ExecuteScalarAsync<string?>(erp,
                "SELECT body FROM cs_requests WHERE cs_request_id = @Id", new { Id = $"w10-{p}" });

            Assert.NotNull(body);   // 안 앉았으면 수신 자체가 깨진 것이다
            var head = body!.Split('\n')[0];

            Assert.Equal($"[{ErpLabel(p)}] {p} 제목", head);
            // 🔴 「본사안내」로 뭉개진 채 저장되는 일이 없다
            Assert.DoesNotContain("[본사안내]", head);
        }

        // ack 는 **저장이 끝난 뒤에만** 간다 — 그 약속도 함께 본다
        Assert.Equal(1, hq.PostCalls);
    }

    // ══════════════════════════════════════════════════════════════
    // G-W10-3 — 🔴 **모르는 값이 와도 안 죽는다** ([1-V] A-2)
    //   fallback `_ => "본사안내"` 를 지우면 `switch` 식이 던지고,
    //   수신 사이클이 죽으면 **같은 응답에 실린 본사 답변까지 못 내려온다**(10/12 사고와 같은 모양).
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W10-3 🔴 모르는 발신종류가 와도 수신이 안 죽고 「본사안내」로 앉는다 (fallback 생존)")]
    public async Task G_W10_3_모르는_값에도_안_죽는다()
    {
        const string gate = "G-W10-3 모르는 발신종류 안전망";
        if (!await _db.ReadyAsync(gate)) return;

        // ① 표 자체가 안 던진다 — 백오피스가 7번째 값을 더하는 순간의 모양
        Assert.Equal("본사안내", ErpLabel("zzz-unknown-purpose"));
        Assert.Equal("본사안내", ErpLabel(null));
        Assert.Equal("본사안내", ErpLabel(""));

        // ② 수신 사이클 전체가 그 값으로도 돈다 — 그리고 **옆에 있는 정상 건까지 같이 앉는다**
        //    (한 건이 터져 목록 전부가 가려진 10/12 전례를 이 자리에서 막는다)
        await using var erp = await _db.OpenAsync();
        await ErpCsGateDb.SeedUsersAsync(erp);

        var items = new List<CsOutboxSenderWorker.OutboundDto>
        {
            new() { OutboundId = "w10-unknown", Purpose = "zzz-unknown-purpose", Title = "모르는 종류",
                    Body = "본문", CreatedAt = DateTime.UtcNow.AddMinutes(-2) },
            new() { OutboundId = "w10-known", Purpose = "update", Title = "아는 종류",
                    Body = "본문", CreatedAt = DateTime.UtcNow.AddMinutes(-1) },
        };

        await CsOutboxSenderWorker.ReceiveOutboundAsync(
            erp, new AckHq(2), "w10owner", items, NullLogger.Instance, default);

        var unknown = await Dapper.SqlMapper.ExecuteScalarAsync<string?>(erp,
            "SELECT body FROM cs_requests WHERE cs_request_id = 'w10-unknown'");
        var known = await Dapper.SqlMapper.ExecuteScalarAsync<string?>(erp,
            "SELECT body FROM cs_requests WHERE cs_request_id = 'w10-known'");

        Assert.StartsWith("[본사안내] 모르는 종류", unknown);
        Assert.StartsWith("[업데이트공지사항] 아는 종류", known);
    }

    // ══════════════════════════════════════════════════════════════
    // G-W10-4 — 🔴 **옛 행을 고쳐 적지 않는다** ([1-V] P-9 를 코드로 못 박는다)
    //   낱말은 본문에 글자로 박혀 저장된다. 표를 고쳐도 이미 내려간 쪽지는 그대로다.
    //   누군가 나중에 「옛 행도 새 낱말로 고쳐 주자」며 UPDATE 를 넣으면 이 게이트가 빨개진다 —
    //   이미 보낸 글의 종류를 다른 이름으로 덮어 적는 것은 거짓말이고 #1 위반이다.
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W10-4 🔴 같은 쪽지가 다른 종류로 다시 내려와도 이미 앉은 본문을 덮지 않는다 (멱등 · 옛 행 불변)")]
    public async Task G_W10_4_이미_앉은_본문을_안_덮는다()
    {
        const string gate = "G-W10-4 옛 행 불변";
        if (!await _db.ReadyAsync(gate)) return;

        await using var erp = await _db.OpenAsync();
        await ErpCsGateDb.SeedUsersAsync(erp);

        var first = new CsOutboxSenderWorker.OutboundDto
        {
            OutboundId = "w10-same", Purpose = "promo", Title = "첫 글",
            Body = "본문", CreatedAt = DateTime.UtcNow.AddMinutes(-5),
        };
        await CsOutboxSenderWorker.ReceiveOutboundAsync(
            erp, new AckHq(1), "w10owner", new List<CsOutboxSenderWorker.OutboundDto> { first },
            NullLogger.Instance, default);

        var before = await Dapper.SqlMapper.ExecuteScalarAsync<string?>(erp,
            "SELECT body FROM cs_requests WHERE cs_request_id = 'w10-same'");
        Assert.StartsWith("[프로모션 안내] 첫 글", before);

        // 같은 열쇠 · **다른 종류 · 다른 제목**으로 다시 내려온다
        var again = new CsOutboxSenderWorker.OutboundDto
        {
            OutboundId = "w10-same", Purpose = "billing", Title = "덮어쓴 글",
            Body = "다른 본문", CreatedAt = DateTime.UtcNow,
        };
        await CsOutboxSenderWorker.ReceiveOutboundAsync(
            erp, new AckHq(1), "w10owner", new List<CsOutboxSenderWorker.OutboundDto> { again },
            NullLogger.Instance, default);

        var after = await Dapper.SqlMapper.ExecuteScalarAsync<string?>(erp,
            "SELECT body FROM cs_requests WHERE cs_request_id = 'w10-same'");

        Assert.Equal(before, after);                      // 🔴 한 글자도 안 바뀐다
        Assert.DoesNotContain("과금결재 안내", after!);     // 새 낱말이 옛 행에 스며들지 않았다

        // 행도 한 건뿐이다 — 두 번 내려와도 중복이 안 생긴다
        var n = await Dapper.SqlMapper.ExecuteScalarAsync<long>(erp,
            "SELECT COUNT(*) FROM cs_requests WHERE cs_request_id = 'w10-same'");
        Assert.Equal(1L, n);
    }

    // ══════════════════════════════════════════════════════════════
    // G-W10-5 — 🔴 낱말이 **공지 화면 제목까지** 닿는다
    //   사전으로 읽는 게이트는 화면 경로를 못 잰다(10/12 교훈) ⇒ 직렬화→**비널러블 int 가 있는
    //   클라이언트 모델**로 역직렬화한 뒤, 화면과 **같은 셈**으로 제목을 뽑아 본다.
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W10-5 🔴 낱말이 화면 모델 왕복을 거쳐 공지 목록 제목까지 닿는다")]
    public async Task G_W10_5_낱말이_화면제목까지_닿는다()
    {
        const string gate = "G-W10-5 낱말이 화면까지";
        if (!await _db.ReadyAsync(gate)) return;

        await using var erp = await _db.OpenAsync();
        await ErpCsGateDb.SeedUsersAsync(erp);

        var items = new List<CsOutboxSenderWorker.OutboundDto>
        {
            new() { OutboundId = "w10-scr", Purpose = "notice", Title = "히트판 소식",
                    Body = "본문", CreatedAt = DateTime.UtcNow.AddMinutes(-1) },
        };
        await CsOutboxSenderWorker.ReceiveOutboundAsync(
            erp, new AckHq(1), "w10owner", items, NullLogger.Instance, default);

        // 화면이 지나는 길 그대로 — 공지 쪽만 묻고(작1 의 쪽 가르기), JSON 을 거쳐 모델로 받는다
        var rows = await ErpCsGateDb.ListAsync(erp, "hq");
        var json = JsonSerializer.Serialize(rows);
        var mirror = JsonSerializer.Deserialize<List<NoticeMirror>>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Single(mirror, x => x.CsRequestId == "w10-scr");
        var row = mirror.First(x => x.CsRequestId == "w10-scr");

        // 🔴 CsNoticePage.razor 의 Title() 과 **같은 셈** — 첫 줄을 통째로 제목으로 쓴다
        var title = (row.Body ?? "").Split('\n')[0].Trim();
        Assert.Equal("[히트판 공지사항] 히트판 소식", title);
    }

    // ══════════════════════════════════════════════════════════════
    // G-W10-6 — 낱말에 개발 용어·내부 꼬리표가 0건 (#23)
    //   🔴 「(옛 값)」은 **본사 직원이 보는 조회용** 꼬리표다. 고객 쪽지 제목에 서면
    //   내부 장부 용어의 외부 노출이고, 그 쪽지가 낡았다는 **거짓 정보**로도 읽힌다.
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W10-6 🔴 고객에게 보이는 발신종류 낱말에 개발 용어·내부 꼬리표가 0건 (#23)")]
    public void G_W10_6_낱말에_개발용어가_없다()
    {
        var banned = new[]
        {
            "옛 값", "옛값", "purpose", "null", "etc", "promo",
            "notice", "update", "billing", "event", "컬럼", "마이그",
        };

        var bad = new List<string>();
        foreach (var p in BackofficeAllowed().Append("zzz-unknown"))
        {
            var label = ErpLabel(p);
            foreach (var w in banned)
                if (label.Contains(w, StringComparison.OrdinalIgnoreCase))
                    bad.Add($"{p} → 「{label}」 에 금지 낱말 「{w}」");
        }

        Assert.True(bad.Count == 0,
            "고객에게 보이는 낱말에 개발 용어·내부 꼬리표가 있다(#23):\n  " + string.Join("\n  ", bad));
    }

    /// <summary>
    /// 가짜 본사 — 도착확인만 받는다. 🔴 <b>Pull 은 404 로 둔다</b>: 이 묶음은 수신 사이클을
    /// 직접 돌리므로 Pull 을 흉내 내면 재는 자리가 둘이 된다(10/12 「가짜 봉투」의 교훈).
    /// </summary>
    private sealed class AckHq : IHeadquartersClient
    {
        private readonly int _acked;
        public int PostCalls { get; private set; }

        public AckHq(int acked) => _acked = acked;

        public Task<(int StatusCode, string Body)> PostAsync(string path, string dataJson, string ownerAccountId, CancellationToken ct)
        {
            PostCalls++;
            return Task.FromResult((200, $"{{\"acked\":{_acked}}}"));
        }

        public Task<(int StatusCode, string Body)> PullAsync(string path, string ownerAccountId, CancellationToken ct)
            => Task.FromResult((404, ""));

        public Task<(int StatusCode, string Body)> PostOwnerReportAsync(string ownerAccountId, CancellationToken ct)
            => Task.FromResult((200, "{\"success\":true}"));
    }

    /// <summary>
    /// 🔴 공지 화면 모델의 거울 — <b>비널러블 int</b> 를 그대로 둔다. 서버가 0 을 보장하지 않으면
    /// 여기서 역직렬화가 터지고, 그게 10/12 에 <b>쪽지 한 건이 목록 전부를 가린</b> 모양이다.
    /// </summary>
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
    }

    public void Dispose() => _db.Dispose();
}
