using System.Security.Claims;
using System.Text.Json;
using Dapper;
using HitPan.Backoffice.API.Controllers;
using HitPan.Backoffice.API.Services;
using HitPan.Backoffice.Components.Pages.Platform.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261013작1 <b>봉합</b> — [4] 조건 4건 + [3-V] 미봉합 1건 중 <b>동작으로 잴 수 있는 셋</b>.
///
/// <list type="bullet">
/// <item><b>G-W7-8</b> 「전달됨」 거짓말 — 수동 접수 건에 전달 상태를 적지 않는다([3-V] 미봉합 1건).</item>
/// <item><b>G-W7-9</b> 없는 분모 — 서버가 상한에 <b>닿았을 때만</b> 「최근 200건 중」이라 적는다([4] 조건-2).</item>
/// <item><b>G-W7-10</b> 평가 낱말 한 벌 — 목록과 상세가 <b>같은 함수</b>에서 같은 낱말을 받는다([4] 조건-3).</item>
/// </list>
///
/// <para>🔴 <b>[4] 조건-1(띠를 다시 눌러도 아무 일이 없다)은 여기 없다.</b> 그것은 Blazor 컴포넌트의
/// 생애주기 거동이고 이 레포에 렌더 하네스(bUnit)가 <b>0건</b>이다 ⇒ <b>게이트로 못 잰다.</b>
/// 가짜 게이트를 세우는 것이 이 프로젝트가 30번 데인 자리라, 안 세우고 개발명세서에 「못 잰다」를 적었다.</para>
///
/// <para>🔴 <b>사전으로 읽지 않는다</b> — 컨트롤러 결과를 dictionary 로 읽으면 화면이 지나는 자리를
/// 아무도 안 지나간다(2026-10-12 사고: G-W3-2 는 사전으로 읽어 통과했고 화면은 터졌다).
/// ⇒ 응답을 <b>직렬화 → 화면 모델 모양으로 역직렬화</b>한 뒤, 그 모델의 값을 실제 함수
/// (<see cref="CsVocab.ReplyDeliveryText"/> · <see cref="CsVocab.OutboundCountLabel"/> ·
/// <see cref="CsVocab.RatingLabel"/>)에 <b>넣어</b> 낱말을 받는다.</para>
///
/// <para>🔴 <b>글자를 세지 않는다</b> — 화면 파일에 어떤 글자가 있나는 보지 않는다.</para>
///
/// <para>🔴 <b>이 게이트가 못 재는 것</b>: <c>.razor</c> 가 그 함수를 <b>실제로 부르는지</b>는 못 잰다
/// (렌더 하네스 0건 · 소스링크는 <c>.cs</c> 만 가능하다). 즉 함수가 참을 말하는 것만 잰다 —
/// 호출자가 옛 식으로 되돌아가면 이 게이트는 초록이다. 그 한계를 개발명세서에 적었다.</para>
///
/// <para>DB 자리: CI <c>db-gate</c> 잡(root)은 격리 DB 를 만들었다 지운다 ·
/// 로컬은 <c>HITPAN_BO_GATE_DB</c> 로 지정해 실측한다(이름에 <c>test</c>·<c>gate</c> 가 없으면 거부 · #39).
/// 둘 다 안 되면 <see cref="DbGateEnvironment.SkipOrFailStrict"/> — <c>[SKIP]</c> 을 <b>찍고</b> 넘긴다.
/// 🔴 판별은 <b>소요시간이 아니다</b> — <c>Skipped</c> 수와 <c>[SKIP]</c> 줄로 가른다.</para>
///
/// <para>🔴 <b>로컬에서는 이 클래스를 하나씩</b> 돌려라 — <c>HITPAN_BO_GATE_DB</c> 는 DB 가 하나인데
/// xUnit 은 클래스를 병렬로 돌린다(2026-10-11 실측).</para>
/// </summary>
public sealed class CsSupportCenterSealGateTests
{
    private const string AdminId = "w7-seal-gate-admin";

    // ══════════════════════════════════════════════════════════════
    // 화면 모델 — `AdminCsListPage.razor` 의 형들과 **같은 널 허용성**.
    //   화면 쪽 모양이 바뀌면 이 게이트가 같이 깨져야 한다.
    // ══════════════════════════════════════════════════════════════

    /// <summary>`AdminCsListPage.CsTicketDetail` 과 같은 자리 — 상세가 화면에서 지나는 모양.</summary>
    private sealed class WebCsTicketDetail
    {
        public long Id { get; set; }
        public string CompanyName { get; set; } = "";
        public string Category { get; set; } = "";
        public string Channel { get; set; } = "";
        public string Status { get; set; } = "";
        public string? Body { get; set; }
        public int? Rating { get; set; }
        public DateTime ReceivedAt { get; set; }
    }

    /// <summary>`AdminCsListPage.ReplyRow` 와 같은 자리 — <c>DeliveredAt</c> 은 널 허용이다.</summary>
    private sealed class WebReplyRow
    {
        public string ReplyId { get; set; } = "";
        public string Body { get; set; } = "";
        public DateTime ApprovedAt { get; set; }
        public DateTime? DeliveredAt { get; set; }
    }

    private sealed class WebDetailResponse
    {
        public bool Success { get; set; }
        public WebCsTicketDetail? Ticket { get; set; }
        public List<WebReplyRow>? Replies { get; set; }
    }

    private sealed class WebCsTicketRow
    {
        public long Id { get; set; }
        public string Channel { get; set; } = "";
        public int? Rating { get; set; }
    }

    private sealed class WebListResponse
    {
        public bool Success { get; set; }
        public List<WebCsTicketRow>? Data { get; set; }
    }

    private sealed class WebOutboundListResponse
    {
        public bool Success { get; set; }
        public List<CsOutboundRow>? Items { get; set; }
    }

    // ══════════════════════════════════════════════════════════════
    // G-W7-8 — 수동 접수 건에 「전달됨」이라 적지 않는다 ([3-V] 미봉합 1건)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>봉합한 거짓말</b>: 상세 패널이 <b>수동 접수(전화·이메일·SNS·기타)</b> 건에도 「전달됨」이라 적었다.
    /// 그런데 같은 화면 아래쪽은 「적은 답은 고객사 화면으로 가지 않습니다」라고 적는다 ⇒ <b>한 화면이 두 말</b>을 했다.
    ///
    /// <para>🔴 <b>왜 「전달됨」이 떴나</b>(뿌리는 선재다): <c>CsInboundController.cs:168-172</c> 가 Pull 1회마다
    /// <b>테넌트 전체</b> <c>delivered_at</c> 을 무조건 찍는다 ⇒ 갈 길이 없는 수동 건의 답에도 날짜가 앉는다.
    /// 그 서버 축은 이 차수가 <b>건드리지 않는다</b>(별 트랙) ⇒ 이 게이트는 그 선재를 <b>그대로 재현</b>해 놓고
    /// (두 건의 답에 <c>delivered_at</c> 을 찍는다) <b>화면 낱말만</b> 참인지 본다.</para>
    ///
    /// <para>🟢 양성 = 고객사 쪽지(<c>erp_message</c>) 건은 「전달됨」 — 그건 실제로 간다(거동 불변).
    /// 🔴 음성 = 수동 건은 「전달」이라는 낱말이 <b>아예 없다.</b></para>
    /// </summary>
    [Fact(DisplayName = "G-W7-8 수동 접수 건의 답에 전달 상태를 적지 않는다 · 고객사 쪽지 건은 지금 거동 그대로")]
    public async Task G_W7_8_전달됨_거짓말을_안_한다()
    {
        const string gate = "G-W7-8 「전달됨」 거짓말";
        await using var env = await SealGateDb.TryCreateAsync(gate, "hitpan_bo_w7_gate_8");
        if (env is null) return;

        await env.SeedTenantAsync("W7S8", "전달 낱말 게이트 상사");
        var cs = NewController(env);

        // ── ① 손으로 적은 접수 4종 — 화면이 보내는 그 길(엔드포인트 그대로)
        var manual = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var channel in new[] { "phone", "email", "sns", "etc" })
        {
            var made = await cs.PhoneIntake(new CsAdminController.PhoneRequest
            {
                TenantCode = "W7S8", Category = "use", SubTag = "etc", Channel = channel,
                Body = $"{channel} 로 들어온 A/S 입니다",
            }, default);
            Assert.True(StatusOf(made) == 200, $"{gate} 접수경로 {channel} 접수가 200 이 아니다. {Describe(made)}");
            manual[channel] = await env.TicketIdByChannelAsync(channel);
        }

        // ── ② 고객사에서 올라온 쪽지 1건 — 사람이 고를 수 없는 값이라 표에 직접 앉힌다
        //    (수동 접수로는 400 이다 — 갈래① G-W7-1 이 그것을 문다)
        await env.SeedErpMessageTicketAsync("W7S8", "고객사 ERP 에서 올라온 쪽지");
        var erpTicketId = await env.TicketIdByChannelAsync(CsVocab.ErpMessageChannel);

        // ── ③ 다섯 건 모두에 본사 답을 쓴다 — 실물 Reply 엔드포인트가 쓴다
        foreach (var id in manual.Values.Append(erpTicketId))
        {
            var replied = await cs.Reply(id, new CsAdminController.ReplyRequest { Body = "확인해 드렸습니다" }, default);
            Assert.True(StatusOf(replied) == 200, $"{gate} 답 저장이 200 이 아니다(ticket={id}). {Describe(replied)}");
        }

        // ── ④ 🔴 선재 재현 — 서버가 Pull 1회에 **테넌트 전체** delivered_at 을 찍는 그 모양.
        //    이 한 줄이 바로 「전달됨」이 수동 건에까지 떴던 까닭이다.
        var stamped = await env.StampAllRepliesDeliveredAsync();
        Assert.True(stamped == 5,
            $"{gate} 선재 재현이 {stamped} 건에만 걸렸다(5 건이어야 한다) — 대조군이 비어 있으면 아래 단언이 「없으니 통과」가 된다.");

        // ── ⑤ 🔴 화면이 지나는 경로 — 상세 응답을 **직렬화 → 화면 모델로 역직렬화**한다
        foreach (var (channel, ticketId) in manual)
        {
            var page = await ReadDetailAsWebAsync(cs, ticketId, gate);
            Assert.Equal(channel, page.Ticket!.Channel);
            Assert.True(page.Replies!.Count == 1,
                $"{gate} {channel} 건의 답이 {page.Replies.Count} 줄이다(1 줄이어야 한다) — 읽는 자리가 틀렸다.");

            var reply = page.Replies[0];
            // 🔴 대조 — 선재가 실제로 걸려 있나. null 이면 「전달됨」이 애초에 안 떠서 거짓말도 안 난다.
            Assert.True(reply.DeliveredAt is not null,
                $"{gate} {channel} 건의 답에 delivered_at 이 없다 — 거짓말이 날 조건 자체가 안 만들어졌다.");

            // 🔴 화면이 그 행의 값으로 받는 낱말
            var shown = CsVocab.ReplyDeliveryText(page.Ticket.Channel, reply.DeliveredAt);

            Assert.False(shown.Contains("전달", StringComparison.Ordinal),
                $"{gate} 수동 접수({channel}) 건이 화면에서 「{shown}」으로 보인다 — "
              + "갈 길이 없는 건에 전달 상태를 적는 것이 [3-V] 가 문 거짓말이다.");
            Assert.True(shown == "본사에 적은 기록 — 고객사 화면으로 가지 않습니다",
                $"{gate} 수동 접수({channel}) 건의 낱말이 「{shown}」이다 — 사람 말로 사실을 적어야 한다.");
        }

        // ── ⑥ 🟢 양성 — 고객사 쪽지 건은 **지금 거동 그대로**. 여기까지 같이 지워 버리면 과봉합이다.
        var erpPage = await ReadDetailAsWebAsync(cs, erpTicketId, gate);
        Assert.Equal(CsVocab.ErpMessageChannel, erpPage.Ticket!.Channel);
        var erpReply = erpPage.Replies![0];
        Assert.True(erpReply.DeliveredAt is not null, $"{gate} 쪽지 건의 delivered_at 이 비었다 — 양성 대조가 성립하지 않는다.");
        Assert.True(CsVocab.ReplyDeliveryText(erpPage.Ticket.Channel, erpReply.DeliveredAt) == "전달됨",
            $"{gate} 실제로 가는 건(고객사 쪽지)이 「전달됨」이 아니다 — 과봉합으로 참말까지 지웠다.");

        // ── ⑦ 🔴 아직 안 간 쪽지 — 「전달 대기」 가지가 살아 있나(이것도 거동 불변)
        Assert.Equal("전달 대기(5분 주기)", CsVocab.ReplyDeliveryText(CsVocab.ErpMessageChannel, null));

        // ── ⑧ 🔴 음성 대조 — 모르는 경로는 **가는 쪽으로 넘겨주지 않는다.**
        //    서버가 새 경로를 하나 더해도 화면이 「전달됨」이라 거짓말하면 안 된다.
        Assert.False(CsVocab.ReplyDeliveryText("새로운경로", DateTime.UtcNow).Contains("전달", StringComparison.Ordinal),
            $"{gate} 모르는 경로가 전달 상태를 받았다 — 기본가지가 「간다」쪽으로 떨어진다.");
        Assert.False(CsVocab.ReplyDeliveryText(null, DateTime.UtcNow).Contains("전달", StringComparison.Ordinal),
            $"{gate} 경로가 null 인데 전달 상태를 받았다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-W7-9 — 분모는 서버가 **실제로 준 건수**로 가른다 ([4] 조건-2)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>봉합한 거짓말</b>: 「히트판공지 전송」 머리글이 서버가 12건만 줘도 한결같이
    /// 「최근 200건 중 12건」이라 적었다. 처음 보는 사람은 「200건에서 12건만 걸러졌다」로 읽는데
    /// 실제로는 <b>보낸 공지가 통틀어 12건</b>이다.
    ///
    /// <para>이 게이트는 두 끝을 <b>실제로</b> 만든다: ⓐ 12건만 있을 때 ⓑ 상한을 넘게(201건) 있을 때.
    /// 서버 <c>LIMIT 200</c> 이 진짜 끊는지도 같이 드러난다(쿼리는 한 줄도 안 건드렸다).</para>
    /// </summary>
    [Fact(DisplayName = "G-W7-9 발신목록 분모 — 상한에 닿았을 때만 「최근 200건 중」이고 적으면 「총」이다")]
    public async Task G_W7_9_없는_분모를_안_적는다()
    {
        const string gate = "G-W7-9 없는 분모";
        await using var env = await SealGateDb.TryCreateAsync(gate, "hitpan_bo_w7_gate_9");
        if (env is null) return;

        await env.SeedTenantAsync("W7S9", "분모 게이트 상사");
        var cs = NewController(env);

        // ── ⓐ 12건 — 상한(200)에 한참 못 미친다 ⇒ 그게 전부라서 「총」이라 말해도 참이다
        for (var i = 0; i < 12; i++)
        {
            var made = await cs.CreateOutbound(new CsAdminController.OutboundRequest
            {
                TenantCode = "W7S9", Purpose = "notice", Title = $"공지 {i + 1}", Body = "본문입니다",
            }, default);
            Assert.True(StatusOf(made) == 200, $"{gate} 선발신 적재가 200 이 아니다. {Describe(made)}");
        }

        var few = await ReadOutboundAsWebAsync(cs, gate);
        Assert.True(few.Count == 12,
            $"{gate} 서버가 {few.Count} 건을 줬다(12 건이어야 한다) — 읽는 자리가 틀렸다.");

        var fewLabel = CsVocab.OutboundCountLabel(few.Count);
        Assert.True(fewLabel == "총",
            $"{gate} 12건만 있는데 머리글이 「{fewLabel} 12건」이라 적는다 — 없는 분모를 말하고 있다.");
        Assert.False(fewLabel.Contains("200", StringComparison.Ordinal),
            $"{gate} 12건 받아 놓고 분모에 200 을 적었다.");

        // ── ⓑ 상한을 넘긴다 — 189건 더 넣어 201건. 서버는 200건만 줘야 하고,
        //    그때는 뒤가 잘렸을 수 있으니 창을 밝혀야 한다.
        var added = await env.SeedOutboundBulkAsync("W7S9", 189);
        Assert.True(added == 189, $"{gate} 벌크 적재가 {added} 건이다(189 건이어야 한다).");

        var many = await ReadOutboundAsWebAsync(cs, gate);
        Assert.True(many.Count == CsVocab.ServerOutboundListLimit,
            $"{gate} 201건이 있는데 서버가 {many.Count} 건을 줬다 — 상한 상수({CsVocab.ServerOutboundListLimit})가 서버와 어긋났다.");

        var manyLabel = CsVocab.OutboundCountLabel(many.Count);
        Assert.True(manyLabel == "최근 200건 중",
            $"{gate} 상한에 닿았는데 머리글이 「{manyLabel}」이라 적는다 — 잘린 목록을 「총」이라 말하면 그것도 거짓말이다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-W7-10 — 평가 낱말이 **한 벌**이다 ([4] 조건-3)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>봉합한 사고</b>: 같은 <c>RatingLabel</c> 식이 <c>AdminCsListPage.razor</c> 와
    /// <c>CsTicketDetailPanel.razor</c> <b>두 곳에 똑같이</b> 적혀 있었다. 한쪽만 고치면 목록 ★ 과
    /// 상세 ★ 이 서로 다른 말을 하는데 <b>빌드·게이트는 전건 초록</b>이다 ⇒ 사람 눈으로만 잡히는 사고다.
    ///
    /// <para>이 게이트는 <b>목록 응답과 상세 응답을 따로 읽어</b> 두 자리의 값을 각각
    /// <see cref="CsVocab.RatingLabel"/> 에 넣고 <b>같은 낱말이 나오나</b>를 본다 — 두 자리가 한 벌을 쓰는지
    /// 거동으로 재는 길이다(글자를 세지 않는다).</para>
    /// </summary>
    [Fact(DisplayName = "G-W7-10 평가 낱말 한 벌 — 목록과 상세가 같은 값에서 같은 낱말을 받는다 · 모르는 값은 아는 척 안 한다")]
    public async Task G_W7_10_평가_낱말이_한_벌이다()
    {
        const string gate = "G-W7-10 평가 낱말 한 벌";
        await using var env = await SealGateDb.TryCreateAsync(gate, "hitpan_bo_w7_gate_10");
        if (env is null) return;

        await env.SeedTenantAsync("W7S10", "평가 낱말 게이트 상사");
        var cs = NewController(env);

        // 평가 3종 + 안 매긴 1건 — 고객이 매길 수 있는 값 전부
        var expected = new Dictionary<int, string> { [3] = "좋아요", [2] = "보통", [1] = "아쉬워요" };
        foreach (var rating in new int?[] { 3, 2, 1, null })
        {
            var made = await cs.PhoneIntake(new CsAdminController.PhoneRequest
            {
                TenantCode = "W7S10", Category = "use", SubTag = "etc", Channel = "phone",
                Body = $"평가 {rating?.ToString() ?? "없음"} 건입니다",
            }, default);
            Assert.True(StatusOf(made) == 200, $"{gate} 접수가 200 이 아니다. {Describe(made)}");
            if (rating is not null) await env.StampRatingAsync(rating.Value, $"평가 {rating} 건입니다");
        }

        // ── 목록 쪽 — 직렬화 → 화면 모델
        var listed = await cs.List(null, null, false, null, default);
        Assert.True(StatusOf(listed) == 200, $"{gate} 목록이 200 이 아니다. {Describe(listed)}");
        var rows = JsonSerializer.Deserialize<WebListResponse>(
            JsonSerializer.Serialize(ValueOf(listed)),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Data;
        Assert.NotNull(rows);
        Assert.True(rows!.Count == 4,
            $"{gate} 화면 모델로 읽은 행이 {rows.Count} 건이다(4 건이어야 한다) — 읽는 자리가 틀렸다.");

        // ── 🔴 목록 쪽 낱말 — 평가가 **실제로 도착하는** 자리다
        var seen = 0;
        foreach (var row in rows)
        {
            var listWord = CsVocab.RatingLabel(row.Rating);

            if (row.Rating is not null)
            {
                Assert.True(listWord == expected[row.Rating.Value],
                    $"{gate} 평가 {row.Rating} 이 「{listWord}」로 보인다(「{expected[row.Rating.Value]}」이어야 한다).");
                seen++;
            }
            else
            {
                Assert.True(listWord == "-", $"{gate} 평가를 안 매긴 건이 「{listWord}」로 보인다 — 없는 평가를 아는 척한다.");
            }
        }

        Assert.True(seen == 3, $"{gate} 평가가 매겨진 건이 {seen} 건이다(3 건이어야 한다) — 대조군이 비었다.");

        // ── 🔴🔴 **이 게이트를 세우다 알게 된 것**(봉합 범위 밖 · 서버 0줄이라 고치지 않았다)
        //
        //   상세 응답에는 `rating` 이 **아예 없다.** `CsAdminController.Detail` 의 SELECT 목록
        //   (`CsAdminController.cs:173-185`)이 `rating`·`rated_at`·`read_at` 을 고르지 않는다.
        //   화면 모델은 `CsTicketDetail : CsTicketRow` 로 그 칸을 **갖고 있어서**(상속) 빌드는 통과하고,
        //   값은 늘 null 이라 `CsTicketDetailPanel.razor:31-33` 의 ★ 칩은 **한 번도 뜨지 않는다.**
        //
        //   ⇒ 그래서 [4] 조건-3 이 말한 「목록 ★ 과 상세 ★ 이 갈린다」는 **오늘은 일어나지 않는다**
        //      (상세 ★ 이 애초에 안 뜨니까). 그래도 사본을 한 벌로 모은 것은 맞다 — 서버가 저 칸을
        //      내주기 시작하는 순간 두 자리가 갈리기 때문이다.
        //
        //   🔴 아래는 **오늘의 사실을 못으로 박는 자리**다. 서버가 `rating` 을 내주기 시작하면 이 단언이
        //      깨지고, 깨진 사람이 위의 「목록 ↔ 상세 낱말 맞대기」를 되살리면 된다(지우지 말고 되살려라).
        var anyRated = rows.First(r => r.Rating is not null);
        var detail = await ReadDetailAsWebAsync(cs, anyRated.Id, gate);
        Assert.True(detail.Ticket!.Rating is null,
            $"{gate} 🔴 상세 응답이 이제 평가({detail.Ticket.Rating})를 준다 — 종전엔 안 줬다. "
          + "상세 ★ 이 살아났다는 뜻이다 ⇒ 이 단언을 지우고 「목록 ↔ 상세 같은 낱말」 맞대기를 되살려라.");

        // 🔴 세 낱말이 서로 갈려야 한다 — 하나로 뭉개지면 평가 축 자체가 뜻을 잃는다
        var distinct = expected.Keys.Select(r => CsVocab.RatingLabel(r)).Distinct(StringComparer.Ordinal).Count();
        Assert.True(distinct == 3, $"{gate} 평가 3종이 {distinct} 개 낱말로만 보인다.");

        // 🔴 음성 대조 — 목록 밖 값을 아는 척하지 않는다(서버 `rating` 은 tinyint 다)
        Assert.Equal("-", CsVocab.RatingLabel(0));
        Assert.Equal("-", CsVocab.RatingLabel(9));
        Assert.Equal("-", CsVocab.RatingLabel(null));
    }

    // ══════════════════════════════════════════════════════════════
    // 시험 장치 — 갈래①②③ 게이트와 같은 모양
    // ══════════════════════════════════════════════════════════════

    /// <summary>🔴 상세를 **화면이 지나는 길**로 읽는다 — 직렬화한 뒤 화면 모델로 되돌려 읽는다.</summary>
    private static async Task<WebDetailResponse> ReadDetailAsWebAsync(CsAdminController cs, long ticketId, string gate)
    {
        var res = await cs.Detail(ticketId, default);
        Assert.True(StatusOf(res) == 200, $"{gate} 상세가 200 이 아니다(ticket={ticketId}). {Describe(res)}");

        var page = JsonSerializer.Deserialize<WebDetailResponse>(
            JsonSerializer.Serialize(ValueOf(res)),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(page);
        Assert.NotNull(page!.Ticket);
        Assert.NotNull(page.Replies);
        return page;
    }

    /// <summary>🔴 발신목록을 화면이 쓰는 그 형(<see cref="CsOutboundRow"/> · 소스링크)으로 되돌려 읽는다.</summary>
    private static async Task<List<CsOutboundRow>> ReadOutboundAsWebAsync(CsAdminController cs, string gate)
    {
        var res = await cs.ListOutbound(null, default);
        Assert.True(StatusOf(res) == 200, $"{gate} 발신목록이 200 이 아니다. {Describe(res)}");

        var page = JsonSerializer.Deserialize<WebOutboundListResponse>(
            JsonSerializer.Serialize(ValueOf(res)),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(page);
        Assert.NotNull(page!.Items);
        return page.Items!;
    }

    private static CsAdminController NewController(SealGateDb env) =>
        new(env.Config(), NullLogger<CsAdminController>.Instance, NullLoggerFactory.Instance) { ControllerContext = AdminContext() };

    /// <summary>실물 컨트롤러가 요구하는 `sub` 클레임을 가진 문맥 — 없으면 Forbid 가 돈다.</summary>
    private static ControllerContext AdminContext()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("sub", AdminId),
            new Claim(ClaimTypes.Email, "w7seal@hitpan.test"),
            new Claim(ClaimTypes.Role, "platform_admin"),
        }, "gate");
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        return new ControllerContext { HttpContext = ctx };
    }

    private static int StatusOf(IActionResult res) => res switch
    {
        ObjectResult o => o.StatusCode ?? 200,
        StatusCodeResult s => s.StatusCode,
        ForbidResult => 403,
        _ => 200,
    };

    private static object? ValueOf(IActionResult res) => (res as ObjectResult)?.Value;

    private static string Describe(IActionResult res) =>
        $"status={StatusOf(res)} value={JsonSerializer.Serialize(ValueOf(res))}";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 레포 뿌리를 모른다.");
    }

    /// <summary>격리 DB 한 벌 — 실물 적용기로 `installer/backoffice/*.sql` 전부 적재.</summary>
    private sealed class SealGateDb : IAsyncDisposable
    {
        private readonly string _name;
        private readonly bool _created;

        private SealGateDb(string name, bool created) { _name = name; _created = created; }

        private static string BoSqlDir() => Path.Combine(RepoRoot(), "installer", "backoffice");

        private static string ServerConnString()
        {
            var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
            var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
            var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root";
            var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
            return $"Server={host};Port={port};User={user};Password={pass};"
                 + "DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;";
        }

        public string ConnString => ServerConnString().Replace("User=", $"Database={_name};User=");

        public IConfiguration Config() => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:BackofficeDb"] = ConnString })
            .Build();

        public static async Task<SealGateDb?> TryCreateAsync(string gateName, string defaultName)
        {
            var overrideName = Environment.GetEnvironmentVariable("HITPAN_BO_GATE_DB");
            var useOverride = !string.IsNullOrWhiteSpace(overrideName);
            var name = useOverride ? overrideName!.Trim() : defaultName;

            if (useOverride)
            {
                // 지정 DB 는 표를 비우므로 시험 성격의 이름만 허용 — 운영·실데이터 축 오폭 차단(#39).
                var lower = name.ToLowerInvariant();
                if (lower is "hitpan_erp" or "hitpan_erp_t004" or "hitpan_backoffice" or "hitpan_e2e"
                    || (!lower.Contains("test") && !lower.Contains("gate")))
                {
                    throw new Xunit.Sdk.XunitException(
                        $"HITPAN_BO_GATE_DB={name} — 시험 DB 가 아니다(이름에 test/gate 필요 · 운영 축 금지).");
                }
            }

            var created = false;
            try
            {
                await using var admin = new MySqlConnection(ServerConnString());
                await admin.OpenAsync();
                if (useOverride)
                {
                    var tables = (await admin.QueryAsync<string>(
                        "SELECT table_name FROM information_schema.tables WHERE table_schema = @Db AND table_type = 'BASE TABLE'",
                        new { Db = name })).ToList();
                    if (tables.Count > 0)
                    {
                        await admin.ExecuteAsync($"USE `{name}`; SET FOREIGN_KEY_CHECKS = 0; "
                            + string.Join(" ", tables.Select(t => $"DROP TABLE IF EXISTS `{t}`;"))
                            + " SET FOREIGN_KEY_CHECKS = 1;");
                    }
                }
                else
                {
                    await admin.ExecuteAsync($"DROP DATABASE IF EXISTS `{name}`; "
                        + $"CREATE DATABASE `{name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
                    created = true;
                }
            }
            catch (MySqlException ex)
            {
                Console.Error.WriteLine($"[{gateName}] DB 준비 실패({ex.Number}): {ex.Message}");
                DbGateEnvironment.SkipOrFailStrict(gateName);
                return null;
            }

            var env = new SealGateDb(name, created);
            await new SchemaMigrator(env.ConnString, BoSqlDir(), NullLogger<SchemaMigrator>.Instance).ApplyAsync();
            return env;
        }

        /// <summary>고객사 1곳.</summary>
        public async Task SeedTenantAsync(string code, string companyName)
        {
            await using var db = await OpenAsync();
            await db.ExecuteAsync(@"
                INSERT INTO tenants (tenant_id, tenant_code, company_name, status)
                VALUES (@Tid, @Code, @Name, 'active')",
                new { Tid = Guid.NewGuid().ToString(), Code = code, Name = companyName });
        }

        /// <summary>
        /// 고객사에서 올라온 쪽지 1건 — 사람이 고를 수 없는 값이라 표에 직접 앉힌다.
        /// 칸 이름은 `DESCRIBE bo_cs_tickets` 로 확인했다(#13).
        /// </summary>
        public async Task SeedErpMessageTicketAsync(string tenantCode, string body)
        {
            await using var db = await OpenAsync();
            await db.ExecuteAsync(@"
                INSERT INTO bo_cs_tickets
                  (tenant_id, client_ticket_uid, received_channel, category, sub_tag, body, status, received_at)
                SELECT t.tenant_id, @Uid, 'erp_message', 'bug', 'stock', @Body, '접수', NOW()
                  FROM tenants t WHERE t.tenant_code = @Code",
                new { Uid = Guid.NewGuid().ToString(), Body = body, Code = tenantCode });
        }

        public async Task<long> TicketIdByChannelAsync(string channel)
        {
            await using var db = await OpenAsync();
            return await db.ExecuteScalarAsync<long>(
                "SELECT id FROM bo_cs_tickets WHERE received_channel = @Ch ORDER BY id DESC LIMIT 1",
                new { Ch = channel });
        }

        /// <summary>
        /// 🔴 선재 재현 — `CsInboundController.cs:168-172` 가 Pull 1회에 테넌트 전체 `delivered_at` 을 찍는 그 모양.
        /// 이 차수는 그 서버 축을 고치지 않으므로, 게이트는 그 상태를 **만들어 놓고** 화면 낱말만 본다.
        /// </summary>
        public async Task<int> StampAllRepliesDeliveredAsync()
        {
            await using var db = await OpenAsync();
            return await db.ExecuteAsync(
                "UPDATE bo_cs_replies SET delivered_at = UTC_TIMESTAMP(6) WHERE delivered_at IS NULL");
        }

        /// <summary>고객이 매긴 평가 — 화면에 그 입구가 없어(고객 ERP 쪽이다) 표에 앉힌다.</summary>
        public async Task StampRatingAsync(int rating, string bodyMatch)
        {
            await using var db = await OpenAsync();
            var hit = await db.ExecuteAsync(@"
                UPDATE bo_cs_tickets
                   SET rating = @Rating, rated_at = UTC_TIMESTAMP(6)
                 WHERE body = @Body",
                new { Rating = rating, Body = bodyMatch });
            Assert.True(hit == 1, $"평가 {rating} 을 앉힐 티켓을 {hit} 건 찾았다(1 건이어야 한다).");
        }

        /// <summary>
        /// 상한을 넘기려고 선발신을 벌크로 넣는다. 칸 이름·널 허용성은 `DESCRIBE bo_cs_outbound` 로 확인했다(#13):
        /// `outbound_id`·`tenant_id`·`purpose`·`created_by` 는 NOT NULL · `created_at` 은 datetime(6).
        /// 🔴 `created_at` 을 UTC 로 명시한다 — DEFAULT 에 맡기면 세션 시간대로 들어간다([4] V-6 과 같은 함정).
        /// </summary>
        public async Task<int> SeedOutboundBulkAsync(string tenantCode, int count)
        {
            await using var db = await OpenAsync();
            var rows = Enumerable.Range(0, count).Select(i => new
            {
                Id = Guid.NewGuid().ToString(),
                Code = tenantCode,
                Title = $"벌크 공지 {i + 1}",
                Admin = AdminId,
                Secs = i + 1,
            });
            return await db.ExecuteAsync(@"
                INSERT INTO bo_cs_outbound
                    (outbound_id, tenant_id, purpose, title, body, created_by, created_at)
                SELECT @Id, t.tenant_id, 'notice', @Title, '본문입니다', @Admin,
                       UTC_TIMESTAMP(6) - INTERVAL @Secs SECOND
                  FROM tenants t WHERE t.tenant_code = @Code", rows);
        }

        public async Task<MySqlConnection> OpenAsync()
        {
            var c = new MySqlConnection(ConnString);
            await c.OpenAsync();
            return c;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_created) return;
            try
            {
                await using var admin = new MySqlConnection(ServerConnString());
                await admin.OpenAsync();
                await admin.ExecuteAsync($"DROP DATABASE IF EXISTS `{_name}`;");
            }
            catch (MySqlException ex)
            {
                // #15 — 뒷정리 실패를 삼키지 않는다(다음 실행의 DROP IF EXISTS 가 지운다).
                Console.Error.WriteLine($"[작1봉합게이트] 격리 DB 뒷정리 실패: {ex.Message}");
            }
        }
    }
}
