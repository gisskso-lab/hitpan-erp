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

    // ══════════════════════════════════════════════════════════════
    // G-W10-7 — 🔴 낱말 **내용**을 고정 기대값으로 못 박는다
    //
    //   [4] 적발(C-3): G-W10-1·2·5 는 기대값을 **과녁 자신**(`PurposeLabel` 리플렉션)에서
    //   가져온다 ⇒ **배선은 진짜로 재지만 낱말 내용은 원리적으로 못 잰다.**
    //   [4] 가 자체 음성 대조군으로 증명했다: `etc`·`event` 를 「엉뚱한말」·「행사꾸러미」로
    //   바꿨는데 **6/6 그대로 초록**이었다(PM 이 직접 재현 확인 · 2026-10-14).
    //
    //   그래서 이 한 건만 **글자를 박는다.** 낱말은 사장님이 정한 것이고 코드에서
    //   끌어올 수 있는 상위 출처가 없다 — **내용 자체가 규격**이다. 출처:
    //     · 4종 = docs/설계/백오피스/20261013_설계_고객지원센터_3화면_프론트재구성.md §5-1 ①~④
    //     · `billing` = 사장님 결-A(2026-10-14) 「과금결재 안내」
    //     · `event`  = §5-1 옛 값 — ERP(고객) 쪽은 「(옛 값)」 꼬리표를 안 붙인다(#23)
    //     · fallback = 모르는 값의 안전망
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W10-7 🔴 발신종류 낱말이 사장님이 정한 글자 그대로다 (내용을 못 박는다 — [4] C-3 봉합)")]
    public void G_W10_7_낱말_내용이_정본_그대로다()
    {
        var spec = new (string Purpose, string Label)[]
        {
            ("update",  "업데이트공지사항"),
            ("notice",  "히트판 공지사항"),
            ("promo",   "프로모션 안내"),
            ("etc",     "기타 공지사항"),
            ("event",   "이벤트"),
            ("billing", "과금결재 안내"),
        };

        var wrong = new List<string>();
        foreach (var (purpose, label) in spec)
        {
            var actual = ErpLabel(purpose);
            if (actual != label) wrong.Add($"{purpose} → 「{actual}」 (정본 「{label}」)");
        }

        Assert.True(wrong.Count == 0,
            "발신종류 낱말이 정본과 다르다 — 낱말은 사장님이 정한 것이다(일정표 §1 결-A · 10/13 설계 §5-1):\n  "
          + string.Join("\n  ", wrong)
          + "\n  낱말을 바꿔야 한다면 **이 기대값과 정본 문서를 함께** 고쳐라.");

        // 모르는 값의 안전망 낱말도 못 박는다 — 이 글자가 바뀌면 고객이 보는 말이 바뀐다
        Assert.Equal("본사안내", ErpLabel("zzz-unknown"));

        // 🔴 규격이 **허용값 전수를 덮나** — 백오피스가 값을 더하면 이 게이트가 먼저 알려 준다
        var uncovered = BackofficeAllowed().Except(spec.Select(s => s.Purpose)).ToList();
        Assert.True(uncovered.Count == 0,
            "백오피스 허용값인데 이 규격표에 없다(낱말을 정하지 않은 채 보내지고 있다): "
          + string.Join(", ", uncovered));
    }

    // ══════════════════════════════════════════════════════════════
    // G-W10-8 — 🔴 **본사 직원이 보는 낱말과 고객이 보는 낱말이 어긋나는지** 드러낸다
    //
    //   [4] 적발(C-2): 백오피스 화면도 자기 낱말표를 코드로 들고 있다
    //   (`src/HitPan.Backoffice/.../CsVocab.cs`). PM 의 [1-V] P-10 은 「백오피스 화면
    //   프로젝트가 레포에 없다」고 적었는데 **그건 틀렸다**(글로브 오류 — 1-V 에 정정을 적었다).
    //
    //   🔴 어긋나면 지원이 안 된다: 고객이 「과금결재 안내 쪽지 받았는데요」라고 전화하면
    //   직원 화면에는 그 낱말이 없다. 그래서 **어긋남을 게이트로 드러내 둔다.**
    //
    //   🔴 두 쪽을 **다른 길로** 읽는다: ERP = 메서드 리플렉션 호출 / 백오피스 = **소스 글자**.
    //   (`HitPan.Tests` 는 `HitPan.Backoffice` 를 ProjectReference 하지 않는다 — 같은 형이
    //   둘이 되는 것을 막으려고 일부러 그렇게 뒀다 · `HitPan.Tests.csproj:67` 주석.)
    //
    //   🔴 **20261014작9 개정 (결-A″ 봉합) — 예외 목록을 「규칙」으로 바꿨다.**
    //
    //   종전: 어긋난 두 값(`event`·`billing`)을 **사유 적힌 자유 예외**로 뺐다.
    //     · `event`   = 꼬리표 「(옛 값)」만 달랐다 → 양해할 수 있는 차이
    //     · `billing` = 🔴 **낱말 몸통 자체가 달랐다**(「요금 안내」 vs 「과금결재 안내」) → 양해 못 할 차이
    //     그런데 게이트는 **둘을 똑같이 봤다.** 자유 예외 목록은 「꼬리표뿐」과 「몸통이 다르다」를
    //     가르지 못한다 ⇒ 진짜 사고가 주석 한 줄로 조용히 사는 자리가 된다.
    //
    //   봉합 뒤: 백오피스가 `billing` 낱말을 고객 쪽 말에 맞췄으므로 **남은 차이는 꼬리표 하나**다.
    //     ⇒ 예외를 지우고 **규칙**으로 잰다: 「백오피스 낱말 = ERP 낱말, 또는 ERP 낱말 + 「(옛 값)」」.
    //     🔴 **예외 0건.** 몸통이 어긋나면 어느 값이든 빨강이고, 예외로 비켜 갈 길이 없다.
    //
    //   🔴 꼬리표를 왜 허용하나: 그 값들은 **쓰기 콤보에 없어 새로 못 쓴다** — 꼬리표가 없으면
    //     직원이 「이걸로 보내면 되나」로 읽는다. 그리고 꼬리표는 **본사 화면에만** 있으므로
    //     고객에게 내부 낱말이 안 간다(#23). 꼬리표가 **고객 쪽에** 생기면 G-W10-7 이 잡는다.
    //
    //   🔴 두 쪽을 **다른 길로** 읽는다: ERP = 메서드 리플렉션 호출 / 백오피스 = **소스 글자**.
    //   (`HitPan.Tests` 는 `HitPan.Backoffice` 를 ProjectReference 하지 않는다 — 같은 형이
    //   둘이 되는 것을 막으려고 일부러 그렇게 뒀다 · `HitPan.Tests.csproj:67` 주석.)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 본사 화면에만 허용되는 꼬리표 — 「이 값은 이미 보낸 글의 종류이고 새로 쓸 수는 없다」를
    /// 직원에게 알리는 말이다. <b>낱말 몸통에는 못 붙는다</b>(G-W10-8 의 규칙).
    /// </summary>
    private const string StaffOnlySuffix = "(옛 값)";

    [Fact(DisplayName = "G-W10-8 🔴 본사 화면 낱말 = 고객 화면 낱말 (+ 「(옛 값)」 꼬리표만 허용) — 예외 0건 (20261014작9 결-A″)")]
    public void G_W10_8_본사낱말과_고객낱말이_어긋나지_않는다()
    {
        var block = BackofficeNoticeKindLabelBlock();

        // 🔴 규칙 — 백오피스 낱말은 「ERP 낱말」 그 자체이거나, 거기에 본사 전용 꼬리표가 붙은 것뿐이다.
        //    예외 목록이 **없다**: 어느 값이든 몸통이 어긋나면 빨강이고 비켜 갈 길이 없다.
        var mismatched = new List<string>();
        var withSuffix = new List<string>();

        foreach (var p in BackofficeAllowed())
        {
            var erp = ErpLabel(p);

            var plain = $"\"{p}\" => \"{erp}\"";                        // 글자까지 같다
            var tagged = $"\"{p}\" => \"{erp}{StaffOnlySuffix}\"";      // 몸통 같고 꼬리표만 붙었다

            if (block.Contains(plain, StringComparison.Ordinal)) continue;
            if (block.Contains(tagged, StringComparison.Ordinal)) { withSuffix.Add(p); continue; }

            mismatched.Add(
                $"{p} → 고객은 「{erp}」로 본다. 본사 낱말표에 「{erp}」도 「{erp}{StaffOnlySuffix}」도 없다");
        }

        Assert.True(mismatched.Count == 0,
            "🔴 본사 직원이 보는 낱말과 고객이 보는 낱말의 **몸통**이 어긋난다 — "
          + "고객이 전화로 말한 낱말을 직원이 못 찾는다(결-A″ 가 봉합한 바로 그 사고):\n  "
          + string.Join("\n  ", mismatched)
          + $"\n  🔴 허용되는 차이는 꼬리표 「{StaffOnlySuffix}」 **하나뿐**이다. 예외 목록은 없앴다 — "
          + "사유 한 줄로 어긋남을 재워 두는 길을 닫았다(20261014작9).");

        // 양성 축 — 규칙이 **아무것도 안 세고** 초록이 되는 것을 막는다.
        //   꼬리표 가지를 한 번도 안 탔다면, 꼬리표 규칙 자체가 죽은 글자다.
        Assert.True(withSuffix.Count >= 1,
            "🔴 꼬리표 가지를 한 건도 안 탔다 — 이 규칙이 실제로 무엇을 재는지 알 수 없다. "
          + "옛 값(event·billing)이 본사 낱말표에서 사라졌나(#1 위반 의심).");
    }

    /// <summary>백오피스 <c>NoticeKindLabel</c> 의 <c>switch</c> 블록을 <b>소스 글자</b>로 떠 온다.</summary>
    private static string BackofficeNoticeKindLabelBlock()
    {
        var src = File.ReadAllText(BackofficeVocabPath());
        var start = src.IndexOf("NoticeKindLabel", StringComparison.Ordinal);
        Assert.True(start > 0, "백오피스 NoticeKindLabel 을 못 찾았다 — 이름이 바뀌었나");
        var block = src[start..];
        var end = block.IndexOf("};", StringComparison.Ordinal);
        Assert.True(end > 0, "백오피스 낱말표 블록의 끝을 못 찾았다");
        return block[..end];
    }

    private static string BackofficeVocabPath()
    {
        var path = Path.Combine(ErpCsGateDb.RepoRoot(),
            "src", "HitPan.Backoffice", "Components", "Pages", "Platform", "Components", "CsVocab.cs");
        Assert.True(File.Exists(path),
            $"백오피스 낱말표를 못 찾았다: {path} — 자리가 바뀌었으면 이 게이트도 함께 옮겨라");
        return path;
    }

    // ══════════════════════════════════════════════════════════════
    // G-W12 — 🔴 20261014작9 갈래A · 결재 거리 **결-A″** 봉합 묶음
    //
    //   [사고] 같은 쪽지를 본사 직원은 「요금 안내」, 고객은 「과금결재 안내」로 봤다.
    //     G-W10-8 이 그 어긋남을 **드러내 뒀고**, 사장님 전결(「전결. 이 두개까지만 해.」)로 봉합했다.
    //
    //   🔴 [1-V] 가 내 셈을 고쳤다: 나는 「낱말 한 줄」이라 말했는데 **자리가 셋**이었다.
    //     세 번째가 **조회 필터 콤보**(`AdminCsNoticePage.razor`)이고, 그게 이 사고의 **본체**다 —
    //     고객이 말한 낱말로 **거를 칸**이 없으면 직원은 끝까지 그 쪽지를 못 찾는다.
    //     낱말표(`CsVocab`)만 고치고 필터 칸을 두면 **같은 화면이 두 낱말**을 쓴다.
    //
    //   🔴 기대값을 글자로 박지 않는다 — `PurposeLabel("billing")` 을 **실제로 불러** 받는다.
    //     사장님이 낱말을 또 고치시면 게이트가 **따라간다**(작2 G-W10-1 이 자기 과녁에서
    //     기대값을 꺼내 동어반복이 됐던 사고의 반대편: 여기서 과녁은 **백오피스 글자**이고
    //     기대값 출처는 **ERP 메서드**다 — 두 쪽이 다른 길이다).
    // ══════════════════════════════════════════════════════════════

    private const string LegacyBillingPurpose = "billing";

    [Fact(DisplayName = "G-W12-1 🔴 본사 조회 필터 칸이 **고객이 말한 낱말**을 품는다 — 결-A″ 의 본체")]
    public void G_W12_1_조회필터_칸이_고객낱말을_품는다()
    {
        var expected = ErpLabel(LegacyBillingPurpose);   // 🔴 글자 하드코딩 금지 — ERP 에서 받아 온다

        var page = Path.Combine(ErpCsGateDb.RepoRoot(),
            "src", "HitPan.Backoffice", "Components", "Pages", "Platform", "AdminCsNoticePage.razor");
        Assert.True(File.Exists(page), $"본사 공지 화면을 못 찾았다: {page}");

        // 🔴 주석을 걷어낸 뒤에 센다 — 내 주석에 든 낱말을 「화면이 쓴다」로 읽으면 거짓 초록이다
        //    (이 트랙에서 네 번 밟은 함정: Razor 주석 `@* *@` 과 C# 블록 주석 둘 다 벗긴다).
        var body = StripComments(File.ReadAllText(page));

        // 옛 값 묶음을 거르는 칸 — 값은 `legacy` 다(화면이 두 옛 값을 한 묶음으로 거른다).
        var idx = body.IndexOf("\"legacy\"", StringComparison.Ordinal);
        Assert.True(idx > 0,
            "본사 공지 화면에 옛 값 묶음 필터 칸(`legacy`)이 없다 — 이미 보낸 옛 쪽지를 거를 길이 사라졌나");

        // 그 칸이 **고객이 보는 낱말**을 품어야 한다. 한 줄 안에서 본다(칸 하나의 글자).
        var lineEnd = body.IndexOf('\n', idx);
        var line = lineEnd < 0 ? body[idx..] : body[idx..lineEnd];

        Assert.True(line.Contains(expected, StringComparison.Ordinal),
            $"🔴 본사 조회 필터 칸에 고객이 말한 낱말 「{expected}」이 없다.\n"
          + $"  그 칸의 글자: {line.Trim()}\n"
          + "  고객이 「" + expected + " 쪽지 받았는데요」라고 전화하면 직원은 **거를 칸에 그 말이 없어** "
          + "끝까지 그 쪽지를 못 찾는다. 낱말표만 고치고 이 칸을 두면 같은 화면이 두 낱말을 쓴다(결-A″).");
    }

    [Fact(DisplayName = "G-W12-2 🔴 쓰기 콤보는 4종 그대로 — 낱말을 고쳤다고 옛 값으로 **새로 보낼** 길이 열리지 않았다")]
    public void G_W12_2_쓰기콤보는_4종_그대로()
    {
        var src = File.ReadAllText(BackofficeVocabPath());
        var start = src.IndexOf("NoticeKinds =", StringComparison.Ordinal);
        Assert.True(start > 0, "백오피스 쓰기 콤보(NoticeKinds)를 못 찾았다 — 이름이 바뀌었나");
        var end = src.IndexOf("};", start, StringComparison.Ordinal);
        Assert.True(end > 0, "쓰기 콤보 블록의 끝을 못 찾았다");
        var block = src[start..end];

        // 🔴 옛 값이 쓰기 콤보에 들어오면 빨강 — 사장님 10/14 교정(「축이 아니라 명분의 한 예」).
        foreach (var legacy in new[] { LegacyBillingPurpose, "event" })
        {
            Assert.False(block.Contains($"\"{legacy}\"", StringComparison.Ordinal),
                $"🔴 옛 값 `{legacy}` 이 **쓰기 콤보**에 들어왔다. 작9 는 **낱말만** 맞춘 차수다 — "
              + "보내는 길을 여는 것은 별 결재다(사장님 10/14: 「축이 아니라 명분의 한 예」).");
        }

        // 4종 전건이 그대로 있나 — 줄어든 것도 사고다.
        foreach (var kind in new[] { "update", "notice", "promo", "etc" })
        {
            Assert.True(block.Contains($"\"{kind}\"", StringComparison.Ordinal),
                $"쓰기 콤보에서 `{kind}` 가 사라졌다 — 사장님 낱말 4종(10/13 설계 §5-1)이 줄었다");
        }
    }

    [Fact(DisplayName = "G-W12-3 🔴 서버 허용값 6종 그대로 — 낱말을 고치면서 옛 값을 지우지 않았다 (#1)")]
    public void G_W12_3_허용값_6종_그대로()
    {
        var allowed = BackofficeAllowed();

        foreach (var v in new[] { "promo", "event", LegacyBillingPurpose, "etc", "update", "notice" })
        {
            Assert.True(allowed.Contains(v),
                $"🔴 서버 허용값에서 `{v}` 가 사라졌다 — 이미 그 값으로 앉은 줄을 다시 저장·재전송할 길이 막힌다(#1).");
        }
    }

    [Fact(DisplayName = "G-W12-4 🔴 옛 낱말 「요금 안내」가 **낱말 자리**에 0건 — 제목·문장은 과녁 밖")]
    public void G_W12_4_옛낱말이_낱말자리에_0건()
    {
        const string oldWord = "요금 안내";

        // 🔴 과녁을 **낱말이 앉는 자리**로 좁힌다. 작3 G-W11-8 에서 레포 전체를 금지했다가
        //    「본사 안내를 불러오지 못했습니다」 같은 **문장** 7건이 빨강이 됐다 — 한국어가 깨진다.
        //    여기서 보는 것은 ①백오피스 낱말표 switch ②옛 값 목록 ③조회 필터 칸, 셋뿐이다.
        var vocab = StripComments(File.ReadAllText(BackofficeVocabPath()));
        var page = StripComments(File.ReadAllText(Path.Combine(ErpCsGateDb.RepoRoot(),
            "src", "HitPan.Backoffice", "Components", "Pages", "Platform", "AdminCsNoticePage.razor")));

        var hits = new List<string>();

        // ① · ② 낱말표와 옛 값 목록 — `=> "…"` / `new("…", "…")` 로 앉는 자리만 본다
        foreach (var marker in new[] { $"=> \"{oldWord}", $"\"{oldWord}" })
        {
            if (vocab.Contains(marker, StringComparison.Ordinal))
                hits.Add($"CsVocab.cs 에 낱말 「{oldWord}…」이 남아 있다");
        }

        // ③ 조회 필터 칸 — `legacy` 칸 한 줄
        var idx = page.IndexOf("\"legacy\"", StringComparison.Ordinal);
        if (idx > 0)
        {
            var lineEnd = page.IndexOf('\n', idx);
            var line = lineEnd < 0 ? page[idx..] : page[idx..lineEnd];
            if (line.Contains(oldWord, StringComparison.Ordinal))
                hits.Add($"AdminCsNoticePage.razor 의 옛 값 필터 칸에 「{oldWord}」이 남아 있다");
        }

        Assert.True(hits.Count == 0,
            $"🔴 옛 낱말 「{oldWord}」이 낱말 자리에 남았다 — 직원과 고객이 다른 말을 본다(결-A″):\n  "
          + string.Join("\n  ", hits.Distinct())
          + "\n  🔴 쪽지 **제목**(예: 시험의 「구독요금 안내」)과 **문장**은 이 게이트의 과녁이 아니다.");
    }

    /// <summary>
    /// Razor 주석(<c>@* *@</c>)·C# 블록 주석·줄 주석을 걷어낸다.
    /// <para>🔴 왜: 이 트랙에서 **네 번** 밟았다 — 내가 주석에 적은 낱말을 「화면이 쓴다」로 읽어
    /// 게이트가 거짓 초록(또는 애먼 빨강)이 됐다. 글자를 세는 게이트는 주석을 먼저 벗긴다.</para>
    /// </summary>
    private static string StripComments(string text)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(
            text, @"@\*.*?\*@", " ", System.Text.RegularExpressions.RegexOptions.Singleline);
        s = System.Text.RegularExpressions.Regex.Replace(
            s, @"/\*.*?\*/", " ", System.Text.RegularExpressions.RegexOptions.Singleline);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"^\s*//.*$", " ",
            System.Text.RegularExpressions.RegexOptions.Multiline);
        return s;
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
