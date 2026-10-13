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
/// 🔴 20261013<b>작2</b> — 사장님 실측 반려 4건(고객A/S창구)을 <b>동작으로</b> 문다.
///
/// <list type="bullet">
/// <item><b>G-W8-1</b> 쪽지건은 응대유형 없이도 완료로 간다 · 그 순간 <c>as_center</c> 가 앉고 <b>옛 값은 안 덮는다</b>.</item>
/// <item><b>G-W8-2</b> 🔴 #1 감시 — 손으로 적은 건은 <b>여전히</b> 응대유형 없으면 완료 거부(작1 약속 보존).</item>
/// <item><b>G-W8-3</b> 손으로 적은 건의 상태는 접수·완료만 · 쪽지건은 5종 전부 통과(음성 대조군).</item>
/// <item><b>G-W8-4</b> 🔴 옛 값 보존 — 예전부터 「처리중」인 수기건을 서버가 <b>조용히 고치지 않는다</b>.</item>
/// <item><b>G-W8-5</b> 응대유형은 수기건만 받는다 — 쪽지건에 꽂으면 400 + 칸 변화 0.</item>
/// <item><b>G-W8-6</b> 상세를 <b>화면 모델로</b> 읽어 고객평가·처리 과정이 실제로 나온다.</item>
/// <item><b>G-W8-7</b> 🔴 #1 감시 — 옛 응대유형 3값 보존 + 새 값 저장 + <c>as_center</c> 는 사람 입력 거부.</item>
/// </list>
///
/// <para>🔴 <b>사전으로 읽지 않는다</b> — 상세 응답을 직렬화 → <b>화면 모델 모양</b>으로 역직렬화한 뒤 그 값을
/// 실제 낱말 함수(<see cref="CsVocab.HandledViaTextFor"/> · <see cref="CsVocab.ProgressText"/> ·
/// <see cref="CsVocab.RatingLabel"/>)에 넣어 받는다(2026-10-12 사고: 사전으로 읽어 통과했고 화면은 터졌다).</para>
///
/// <para>🔴 <b>이 게이트가 못 재는 것</b>: <c>.razor</c> 가 그 함수를 실제로 부르는지는 못 잰다
/// (렌더 하네스 0건). 목록 칸에서 상태를 누르는 손짓도 못 잰다 — 사장님 실측이 그 자리다.</para>
///
/// <para>DB 자리: CI <c>db-gate</c> 잡(root)이 격리 DB 를 만들었다 지운다 ·
/// 로컬은 <c>HITPAN_BO_GATE_DB</c>(이름에 <c>test</c>·<c>gate</c> 필요 · #39) ·
/// 둘 다 안 되면 <see cref="DbGateEnvironment.SkipOrFailStrict"/> 가 <c>[SKIP]</c> 을 <b>찍고</b> 넘긴다.
/// 🔴 판별은 소요시간이 아니라 <c>Skipped</c> 수와 <c>[SKIP]</c> 줄이다.</para>
/// </summary>
public sealed class CsAsCounterStatusGateTests
{
    private const string AdminId = "w8-status-gate-admin";

    // ══════════════════════════════════════════════════════════════
    // 화면 모델 — `AdminCsListPage.razor` 의 형들과 **같은 널 허용성**
    // ══════════════════════════════════════════════════════════════

    private sealed class WebCsTicketDetail
    {
        public long Id { get; set; }
        public string CompanyName { get; set; } = "";
        public string Category { get; set; } = "";
        public string Channel { get; set; } = "";
        public string Status { get; set; } = "";
        public string? HandledVia { get; set; }
        public string? Body { get; set; }
        public int? Rating { get; set; }
        public DateTime? RatedAt { get; set; }
        public string? RatingComment { get; set; }
        public DateTime? ReadAt { get; set; }
        public DateTime ReceivedAt { get; set; }
    }

    private sealed class WebReplyRow
    {
        public string ReplyId { get; set; } = "";
        public string Body { get; set; } = "";
        public DateTime ApprovedAt { get; set; }
        public DateTime? DeliveredAt { get; set; }
    }

    /// <summary>`AdminCsListPage.ProgressLogRow` 와 같은 자리 — 🔴 칸 전부 널 허용이다.</summary>
    private sealed class WebProgressLogRow
    {
        public string? FromStatus { get; set; }
        public string? ToStatus { get; set; }
        public string? ActorId { get; set; }
        public DateTime ActedAt { get; set; }
    }

    private sealed class WebDetailResponse
    {
        public bool Success { get; set; }
        public WebCsTicketDetail? Ticket { get; set; }
        public List<WebReplyRow>? Replies { get; set; }
        public List<WebProgressLogRow>? Logs { get; set; }
    }

    private sealed class WebCsTicketRow
    {
        public long Id { get; set; }
        public string Channel { get; set; } = "";
        public string Status { get; set; } = "";
        public string? HandledVia { get; set; }
    }

    private sealed class WebListResponse
    {
        public bool Success { get; set; }
        public List<WebCsTicketRow>? Data { get; set; }
    }

    // ══════════════════════════════════════════════════════════════
    // G-W8-1 — 쪽지건은 응대유형 없이도 완료로 간다 (사장님 반려 2)·4) 의 뿌리)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>봉합한 막힘</b>: 완료로 넘길 때 <c>handled_via</c> 가 비면 서버가 400 으로 거부했고,
    /// 쪽지로 들어온 건은 수신 통로가 그 칸을 <b>아예 안 채운다</b> ⇒ 쪽지 CS 는 응대유형을 먼저
    /// 따로 저장하지 않으면 <b>완료로 갈 수 없었다</b>. 사장님 반려 4)「상태변경이 잘 안 됨」의 범인 ①.
    ///
    /// <para>🟢 양성 = 쪽지건은 응대유형 0건으로도 완료가 된다 · 그 순간 칸에 <c>as_center</c> 가 앉는다.
    /// 🔴 음성 = 응대유형이 <b>이미 있는</b> 쪽지건은 완료 뒤에도 <b>그 값 그대로</b>다(안 덮는다 · 조건 C-2).</para>
    /// </summary>
    [Fact(DisplayName = "G-W8-1 쪽지 A/S 는 응대유형 없이도 완료된다 · 빈 칸만 히트판 A/S센터로 채우고 옛 값은 안 덮는다")]
    public async Task G_W8_1_쪽지건은_응대유형_없이도_완료된다()
    {
        const string gate = "G-W8-1 쪽지건 완료";
        await using var env = await StatusGateDb.TryCreateAsync(gate, "hitpan_bo_w8_gate_1");
        if (env is null) return;

        await env.SeedTenantAsync("W8G1", "쪽지 완료 게이트 상사");
        var cs = NewController(env);

        // ── ① 쪽지 2건 — 수신 통로가 앉히는 모양 그대로(handled_via 는 NULL)
        await env.SeedErpMessageTicketAsync("W8G1", "쪽지 ①: 응대유형이 비어 있는 건");
        var emptyId = await env.TicketIdByBodyAsync("쪽지 ①: 응대유형이 비어 있는 건");
        await env.SeedErpMessageTicketAsync("W8G1", "쪽지 ②: 응대유형이 이미 있는 건");
        var filledId = await env.TicketIdByBodyAsync("쪽지 ②: 응대유형이 이미 있는 건");

        Assert.Null(await env.HandledViaOfAsync(emptyId));
        // 음성 대조군 — 옛 값이 앉아 있는 쪽지건(예전 판에서 사람이 저장했을 수 있다)
        await env.StampHandledViaAsync(filledId, "phone");
        Assert.Equal("phone", await env.HandledViaOfAsync(filledId));

        // ── ② 🟢 양성 — 응대유형 0건인 쪽지건을 바로 완료로
        var done = await cs.ChangeStatus(emptyId, new CsAdminController.StatusRequest { To = "완료" }, default);
        Assert.True(StatusOf(done) == 200,
            $"{gate} 쪽지건이 완료로 못 갔다 — 사장님 반려 4) 가 그대로다. {Describe(done)}");
        Assert.Equal("완료", await env.StatusOfAsync(emptyId));

        // 🔴 서버가 그 자리에서 칸을 채웠나 — 화면이 「히트판 A/S센터」라 적는 것과 자료가 맞아야 한다
        Assert.Equal("as_center", await env.HandledViaOfAsync(emptyId));

        // ── ③ 🔴 음성 — 옛 값이 있던 건은 완료 뒤에도 그 값 그대로(#1 · 조건 C-2)
        var done2 = await cs.ChangeStatus(filledId, new CsAdminController.StatusRequest { To = "완료" }, default);
        Assert.True(StatusOf(done2) == 200, $"{gate} 옛 값 있는 쪽지건이 완료로 못 갔다. {Describe(done2)}");
        Assert.Equal("phone", await env.HandledViaOfAsync(filledId));

        // ── ④ 화면이 그 두 건에 적을 말 — 쪽지건은 칸이 무엇이든 창구가 하나다
        var page1 = await ReadDetailAsWebAsync(cs, emptyId, gate);
        Assert.Equal("히트판 A/S센터", CsVocab.HandledViaTextFor(page1.Ticket!.Channel, page1.Ticket.HandledVia));

        // 🔴 칸이 비어 있어도 쪽지건은 「히트판 A/S센터」다 — 새 쪽지(미완료)로 확인한다
        await env.SeedErpMessageTicketAsync("W8G1", "쪽지 ③: 아직 완료 안 한 건");
        var freshId = await env.TicketIdByBodyAsync("쪽지 ③: 아직 완료 안 한 건");
        var page3 = await ReadDetailAsWebAsync(cs, freshId, gate);
        Assert.Null(page3.Ticket!.HandledVia);
        Assert.Equal("히트판 A/S센터", CsVocab.HandledViaTextFor(page3.Ticket.Channel, page3.Ticket.HandledVia));
    }

    // ══════════════════════════════════════════════════════════════
    // G-W8-2 — 🔴 #1 감시: 손으로 적은 건의 완료 조건은 **그대로 산다**
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 작1 이 세운 약속을 작2 가 지우지 않았나를 문다. 쪽지건을 풀어 주면서 <b>수기건까지</b> 풀면,
    /// 손으로 응대한 건의 「어떻게 응대했나」가 영영 비고 CS 실적이 소급 불가가 된다(작1 이 막은 원래 이유).
    ///
    /// <para>🔴 음성 = 응대유형 없는 수기건은 완료 거부(400) · 🟢 양성 = 저장한 뒤엔 통과.</para>
    /// </summary>
    [Fact(DisplayName = "G-W8-2 🔴 #1 감시 — 손으로 적은 A/S 는 여전히 응대유형 없으면 완료 거부")]
    public async Task G_W8_2_수기건_완료조건은_그대로_산다()
    {
        const string gate = "G-W8-2 수기건 완료조건";
        await using var env = await StatusGateDb.TryCreateAsync(gate, "hitpan_bo_w8_gate_2");
        if (env is null) return;

        await env.SeedTenantAsync("W8G2", "수기 완료조건 게이트 상사");
        var cs = NewController(env);

        var made = await cs.PhoneIntake(new CsAdminController.PhoneRequest
        {
            TenantCode = "W8G2", Category = "use", SubTag = "etc", Channel = "phone",
            Body = "전화로 받은 A/S",
        }, default);
        Assert.True(StatusOf(made) == 200, $"{gate} 수기 접수가 200 이 아니다. {Describe(made)}");
        var id = await env.TicketIdByBodyAsync("전화로 받은 A/S");
        Assert.Null(await env.HandledViaOfAsync(id));

        // ── 🔴 음성 — 응대유형 없이 완료 ⇒ 거부
        var blocked = await cs.ChangeStatus(id, new CsAdminController.StatusRequest { To = "완료" }, default);
        Assert.True(StatusOf(blocked) == 400,
            $"{gate} 응대유형 없는 수기건이 완료로 넘어갔다 — 작1 이 막은 자리를 작2 가 열었다. {Describe(blocked)}");
        Assert.Equal("접수", await env.StatusOfAsync(id));
        // 🔴 거부한 건에 칸을 몰래 채우지도 않았나(쪽지 가지가 수기건으로 새면 이 줄이 잡는다)
        Assert.Null(await env.HandledViaOfAsync(id));

        // ── 🟢 양성 — 응대유형을 저장하면 완료로 간다
        var via = await cs.SetHandledVia(id, new CsAdminController.HandledViaRequest { Via = "remote" }, default);
        Assert.True(StatusOf(via) == 200, $"{gate} 수기건 응대유형 저장이 200 이 아니다. {Describe(via)}");
        var ok = await cs.ChangeStatus(id, new CsAdminController.StatusRequest { To = "완료" }, default);
        Assert.True(StatusOf(ok) == 200, $"{gate} 응대유형 저장 뒤에도 완료가 안 된다. {Describe(ok)}");
        Assert.Equal("완료", await env.StatusOfAsync(id));
        Assert.Equal("remote", await env.HandledViaOfAsync(id));
    }

    // ══════════════════════════════════════════════════════════════
    // G-W8-3 — 손으로 적은 건의 상태는 접수·완료만 (사장님 지시 3)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 사장님 지시: 「수기기록은 또한 상태기록만 하고, 처리상태를 고객사에게 보여주지 않음으로,
    /// 접수와 완료만 기록한다」. 중간 상태는 고객사가 볼 수 없는데 본사만 적어 두면 <b>아무도 안 읽는 기록</b>이다.
    ///
    /// <para>🔴 음성 = 수기건에 <c>처리중</c>·<c>보류</c>·<c>고객답대기</c> ⇒ 400 <b>+ 행 변화 0줄</b>.
    /// 🟢 양성(대조군) = 같은 세 값이 <b>쪽지건에서는 전부 통과</b>한다 — 경로를 안 보고 전부 막은 게 아니다.</para>
    /// </summary>
    [Fact(DisplayName = "G-W8-3 손으로 적은 A/S 는 접수·완료만 · 쪽지 A/S 는 5종 전부 통과(대조군)")]
    public async Task G_W8_3_수기건_상태는_두_종류다()
    {
        const string gate = "G-W8-3 수기건 상태 2종";
        await using var env = await StatusGateDb.TryCreateAsync(gate, "hitpan_bo_w8_gate_3");
        if (env is null) return;

        await env.SeedTenantAsync("W8G3", "상태 2종 게이트 상사");
        var cs = NewController(env);

        var made = await cs.PhoneIntake(new CsAdminController.PhoneRequest
        {
            TenantCode = "W8G3", Category = "set", SubTag = "etc", Channel = "email",
            Body = "이메일로 받은 A/S",
        }, default);
        Assert.True(StatusOf(made) == 200, $"{gate} 수기 접수가 200 이 아니다. {Describe(made)}");
        var manualId = await env.TicketIdByBodyAsync("이메일로 받은 A/S");

        // ── 🔴 음성 — 중간 상태 3종은 전부 거부되고 **행이 안 바뀐다**
        foreach (var bad in new[] { "처리중", "보류", "고객답대기" })
        {
            var res = await cs.ChangeStatus(manualId, new CsAdminController.StatusRequest { To = bad }, default);
            Assert.True(StatusOf(res) == 400,
                $"{gate} 수기건이 「{bad}」로 넘어갔다 — 사장님 지시 3) 위반. {Describe(res)}");
            Assert.Equal("접수", await env.StatusOfAsync(manualId));
        }
        // 🔴 거부한 세 번이 전이 기록을 남기지도 않았나(남기면 「과정」이 거짓이 된다)
        Assert.Equal(0, await env.LogCountOfAsync(manualId));

        // ── 🟢 양성 — 수기건도 「완료」는 된다(응대유형 저장 뒤)
        await cs.SetHandledVia(manualId, new CsAdminController.HandledViaRequest { Via = "email" }, default);
        var done = await cs.ChangeStatus(manualId, new CsAdminController.StatusRequest { To = "완료" }, default);
        Assert.True(StatusOf(done) == 200, $"{gate} 수기건 완료가 막혔다. {Describe(done)}");

        // ── 🟢 대조군 — 같은 세 값이 **쪽지건에서는 통과**한다
        await env.SeedErpMessageTicketAsync("W8G3", "쪽지로 받은 A/S");
        var erpId = await env.TicketIdByBodyAsync("쪽지로 받은 A/S");
        foreach (var good in new[] { "처리중", "보류", "고객답대기", "완료" })
        {
            var res = await cs.ChangeStatus(erpId, new CsAdminController.StatusRequest { To = good }, default);
            Assert.True(StatusOf(res) == 200,
                $"{gate} 쪽지건이 「{good}」로 못 갔다 — 경로를 안 보고 전부 막았다(과봉합). {Describe(res)}");
            Assert.Equal(good, await env.StatusOfAsync(erpId));
        }

        // ── 낱말 한 벌도 같은 선인가 — 화면이 고를 수 있는 목록이 서버와 맞나
        Assert.Equal(2, CsVocab.StatusesFor("phone").Count);
        Assert.Equal(5, CsVocab.StatusesFor(CsVocab.ErpMessageChannel).Count);
    }

    // ══════════════════════════════════════════════════════════════
    // G-W8-4 — 🔴 옛 값 보존(U-1): 예전부터 「처리중」인 수기건
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 상태를 2종으로 좁히면 <b>운영에 이미 앉아 있는</b> 수기건(예: 「처리중」)이 목록 밖 값이 된다.
    /// 그 값을 서버가 조용히 고치거나 화면이 다른 값으로 바꿔 적으면 <b>사실이 사라진다</b>(#1).
    ///
    /// <para>🔴 이 게이트는 그 상태를 <b>만들어 놓고</b> ① 목록·상세가 그 값을 그대로 돌려주나
    /// ② 그 값으로 다시 고르면(같은 값) 조용히 통과하나 ③ 낱말 한 벌이 「목록 밖」이라고 판정하나를 본다.</para>
    /// </summary>
    [Fact(DisplayName = "G-W8-4 🔴 옛 값 보존 — 예전부터 「처리중」인 수기건을 서버가 조용히 고치지 않는다")]
    public async Task G_W8_4_옛_상태값을_안_고친다()
    {
        const string gate = "G-W8-4 옛 값 보존";
        await using var env = await StatusGateDb.TryCreateAsync(gate, "hitpan_bo_w8_gate_4");
        if (env is null) return;

        await env.SeedTenantAsync("W8G4", "옛 값 보존 게이트 상사");
        var cs = NewController(env);

        var made = await cs.PhoneIntake(new CsAdminController.PhoneRequest
        {
            TenantCode = "W8G4", Category = "net", SubTag = "etc", Channel = "sns",
            Body = "예전부터 처리중인 수기 A/S",
        }, default);
        Assert.True(StatusOf(made) == 200, $"{gate} 수기 접수가 200 이 아니다. {Describe(made)}");
        var id = await env.TicketIdByBodyAsync("예전부터 처리중인 수기 A/S");

        // 🔴 작2 이전 판에서 앉았을 상태를 표에 직접 만든다(지금 서버로는 못 만든다 — 그게 G-W8-3 이다)
        await env.StampStatusAsync(id, "처리중");

        // ── ① 목록이 그 값을 그대로 돌려주나
        var list = await ReadListAsWebAsync(cs, gate);
        var row = Assert.Single(list, r => r.Id == id);
        Assert.Equal("처리중", row.Status);

        // ── ② 상세도 그대로인가
        var page = await ReadDetailAsWebAsync(cs, id, gate);
        Assert.Equal("처리중", page.Ticket!.Status);

        // ── ③ 같은 값으로 다시 고르면 조용히 통과(400 으로 사람을 막지 않는다)
        var same = await cs.ChangeStatus(id, new CsAdminController.StatusRequest { To = "처리중" }, default);
        Assert.True(StatusOf(same) == 200, $"{gate} 지금 값과 같은 값이 거부됐다. {Describe(same)}");
        Assert.Equal("처리중", await env.StatusOfAsync(id));

        // ── ③-2 🔴 **전이 경로에서도** 옛 값이 살아남나.
        //   FAIL 대조로 드러난 구멍: ①②③ 은 **읽기와 제자리 전이**만 재서, 서버가 전이 도중에
        //   「목록 밖 값이면 접수로 되돌린다」를 몰래 넣어도 전부 초록이었다.
        //   ⇒ 목록 밖 값(보류)으로 가 보는 **거부되는 전이**를 한 번 치고, 그때도 옛 값이 그대로인지 본다.
        var refused = await cs.ChangeStatus(id, new CsAdminController.StatusRequest { To = "보류" }, default);
        Assert.True(StatusOf(refused) == 400, $"{gate} 수기건이 「보류」로 넘어갔다. {Describe(refused)}");
        Assert.Equal("처리중", await env.StatusOfAsync(id));

        // ── ③-3 🔴 허용되는 전이(완료)를 치기 **전후**로도 옛 값이 조용히 안 고쳐지나.
        //   완료로 가는 길에 「되돌리기」가 끼어 있으면 중간 상태가 접수로 덮인다 ⇒ 기록이 거짓이 된다.
        await cs.SetHandledVia(id, new CsAdminController.HandledViaRequest { Via = "phone" }, default);
        Assert.Equal("처리중", await env.StatusOfAsync(id));
        var toDone = await cs.ChangeStatus(id, new CsAdminController.StatusRequest { To = "완료" }, default);
        Assert.True(StatusOf(toDone) == 200, $"{gate} 옛 값 건의 완료가 막혔다. {Describe(toDone)}");
        Assert.Equal("완료", await env.StatusOfAsync(id));

        // 🔴 전이 기록에 **옛 값이 출발점으로 남아야** 한다 — 「접수 → 완료」로 적히면 과정이 거짓이다
        var fromStatuses = await env.LogFromStatusesOfAsync(id);
        Assert.Contains("처리중", fromStatuses);
        Assert.DoesNotContain("접수", fromStatuses);   // 접수에서 떠난 전이는 한 번도 없었다

        // ── ④ 낱말 한 벌이 「목록 밖」이라고 알려 주나 — 화면이 그 값을 비활성으로 보이는 근거
        Assert.True(CsVocab.IsLegacyStatus("sns", "처리중"),
            $"{gate} 낱말 한 벌이 옛 값을 못 가린다 — 화면이 그 값을 지우거나 아는 척하게 된다.");
        Assert.False(CsVocab.IsLegacyStatus(CsVocab.ErpMessageChannel, "처리중"),
            $"{gate} 쪽지건의 「처리중」을 옛 값으로 잘못 봤다.");
        Assert.False(CsVocab.IsLegacyStatus("sns", "완료"));
    }

    // ══════════════════════════════════════════════════════════════
    // G-W8-5 — 응대유형은 손으로 적은 건만 받는다 (사장님 지시 3)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 쪽지건의 응대 창구는 히트판 A/S센터 하나다. 사람이 바꿀 수 있게 열어 두면
    /// 「전화로 응대했다」로 바꿔 적을 수 있고, 그 순간 <b>쪽지가 전화를 얼마나 줄였나</b>를 세는 자료가 뜻을 잃는다.
    ///
    /// <para>🔴 음성 = 쪽지건에 응대유형 꽂기 ⇒ 400 <b>+ 칸 변화 0</b> · 🟢 양성 = 수기건은 앉는다.</para>
    /// </summary>
    [Fact(DisplayName = "G-W8-5 응대유형은 손으로 적은 A/S 만 받는다 · 쪽지 A/S 는 400 + 칸 변화 0")]
    public async Task G_W8_5_쪽지건_응대유형은_사람이_못_바꾼다()
    {
        const string gate = "G-W8-5 응대유형 입구";
        await using var env = await StatusGateDb.TryCreateAsync(gate, "hitpan_bo_w8_gate_5");
        if (env is null) return;

        await env.SeedTenantAsync("W8G5", "응대유형 입구 게이트 상사");
        var cs = NewController(env);

        await env.SeedErpMessageTicketAsync("W8G5", "쪽지 A/S — 응대유형 고정");
        var erpId = await env.TicketIdByBodyAsync("쪽지 A/S — 응대유형 고정");

        // ── 🔴 음성 — 쪽지건은 거부되고 칸이 안 바뀐다
        var blocked = await cs.SetHandledVia(erpId, new CsAdminController.HandledViaRequest { Via = "phone" }, default);
        Assert.True(StatusOf(blocked) == 400,
            $"{gate} 쪽지건의 응대유형을 사람이 바꿀 수 있다 — 고정이 아니다. {Describe(blocked)}");
        Assert.Null(await env.HandledViaOfAsync(erpId));
        Assert.Equal(0, await env.LogCountOfAsync(erpId));

        // ── 🟢 양성 — 수기건은 앉는다
        await cs.PhoneIntake(new CsAdminController.PhoneRequest
        {
            TenantCode = "W8G5", Category = "dat", SubTag = "etc", Channel = "etc",
            Body = "기타 경로로 받은 A/S",
        }, default);
        var manualId = await env.TicketIdByBodyAsync("기타 경로로 받은 A/S");
        var ok = await cs.SetHandledVia(manualId, new CsAdminController.HandledViaRequest { Via = "sns" }, default);
        Assert.True(StatusOf(ok) == 200, $"{gate} 수기건 응대유형 저장이 막혔다. {Describe(ok)}");
        Assert.Equal("sns", await env.HandledViaOfAsync(manualId));
    }

    // ══════════════════════════════════════════════════════════════
    // G-W8-6 — 상세를 화면 모델로 읽어 평가·처리 과정이 실제로 나온다
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>봉합한 거짓말 2건</b>(사장님 반려 1)·2)):
    /// ⓐ 상세 SELECT 가 <c>rating</c> 을 안 읽어 <b>고객평가 ★ 가 영영 안 떴다</b>
    /// ⓑ <c>bo_cs_ticket_logs</c> 는 5곳에서 쓰는데 <b>읽는 SELECT 가 0건</b>이라 처리 과정이 안 보였다.
    ///
    /// <para>🔴 화면 모델로 역직렬화해 읽는다 — 서버가 칸을 주지 않으면 값이 null 이 되고,
    /// 그 null 을 낱말 함수에 넣어 <b>「-」가 나오는지</b>까지 본다(사전으로 읽으면 이 틈이 안 보인다).</para>
    ///
    /// <para>🔴 음성 = 평가가 없는 건은 null 로 와도 <b>안 터지고</b> 「-」라 적는다(아는 척 금지).</para>
    /// </summary>
    [Fact(DisplayName = "G-W8-6 상세를 화면 모델로 읽어 고객평가 ★ 와 처리 과정이 나온다 · 없으면 아는 척 안 한다")]
    public async Task G_W8_6_상세가_평가와_과정을_내려준다()
    {
        const string gate = "G-W8-6 평가·과정";
        await using var env = await StatusGateDb.TryCreateAsync(gate, "hitpan_bo_w8_gate_6");
        if (env is null) return;

        await env.SeedTenantAsync("W8G6", "평가·과정 게이트 상사");
        var cs = NewController(env);

        await env.SeedErpMessageTicketAsync("W8G6", "평가까지 받은 쪽지 A/S");
        var ratedId = await env.TicketIdByBodyAsync("평가까지 받은 쪽지 A/S");

        // ── ① 실제로 일을 시킨다 — 분류 바꾸고, 답 쓰고, 처리중 → 완료
        await cs.Recategorize(ratedId, new CsAdminController.CategoryRequest { To = "net" }, default);
        await cs.Reply(ratedId, new CsAdminController.ReplyRequest { Body = "조치해 드렸습니다" }, default);
        await cs.ChangeStatus(ratedId, new CsAdminController.StatusRequest { To = "처리중" }, default);
        await cs.ChangeStatus(ratedId, new CsAdminController.StatusRequest { To = "완료" }, default);

        // ── ② 고객이 매긴 평가(그 입구는 고객 ERP 쪽이라 표에 앉힌다)
        await env.StampRatingAsync(ratedId, 3, "친절했어요");

        // ── ③ 🔴 화면 모델로 읽는다
        var page = await ReadDetailAsWebAsync(cs, ratedId, gate);

        // ⓐ 고객평가 — 오늘까지 여기가 null 이었다
        Assert.True(page.Ticket!.Rating is not null,
            $"{gate} 상세가 고객평가를 안 내려준다 — 사장님 반려 2) 가 그대로다(SELECT 에 rating 이 없다).");
        Assert.Equal(3, page.Ticket.Rating);
        Assert.Equal("좋아요", CsVocab.RatingLabel(page.Ticket.Rating));
        Assert.True(page.Ticket.RatedAt is not null, $"{gate} 평가 시각이 비었다.");
        Assert.Equal("친절했어요", page.Ticket.RatingComment);

        // ⓑ 처리 과정 — 분류 1 + 상태 2 = 3줄 이상, 시간순
        Assert.True(page.Logs is not null, $"{gate} 상세 응답에 처리 과정 칸이 없다 — 사장님 반려 1) 가 그대로다.");
        Assert.True(page.Logs!.Count >= 3,
            $"{gate} 처리 과정이 {page.Logs.Count} 줄이다(분류 1 + 상태 2 = 3 줄 이상이어야 한다).");
        var times = page.Logs.Select(l => l.ActedAt).ToList();
        Assert.True(times.SequenceEqual(times.OrderBy(t => t)),
            $"{gate} 처리 과정이 시간순이 아니다 — 읽는 순서가 일한 순서여야 한다.");

        // 🔴 그 줄들이 **사람 말**로 읽히나(개발 낱말 금지 · #23)
        var texts = page.Logs.Select(l => CsVocab.ProgressText(l.FromStatus, l.ToStatus)).ToList();
        Assert.Contains(texts, t => t.Contains("상태를 「처리중」 → 「완료」로 바꿈", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.Contains("분류를", StringComparison.Ordinal) && t.Contains("통신", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, t => t.Contains("분류:", StringComparison.Ordinal));

        // ── ④ 🔴 음성 — 평가가 없는 건은 null 로 와도 안 터지고 「-」라 적는다
        await env.SeedErpMessageTicketAsync("W8G6", "평가가 없는 쪽지 A/S");
        var plainId = await env.TicketIdByBodyAsync("평가가 없는 쪽지 A/S");
        var plain = await ReadDetailAsWebAsync(cs, plainId, gate);
        Assert.Null(plain.Ticket!.Rating);
        Assert.Null(plain.Ticket.RatedAt);
        Assert.Equal("-", CsVocab.RatingLabel(plain.Ticket.Rating));
        Assert.True(plain.Logs is not null && plain.Logs.Count == 0,
            $"{gate} 아무 일도 안 한 건의 과정이 비어 있지 않다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-W8-7 — 🔴 #1 감시: 응대유형 값은 **넓히기만** 했다
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 작1 의 G-W7-4 와 같은 선 — 값 목록을 <b>교체</b>하면 옛 값으로 앉은 행을 못 읽는다.
    /// 이 게이트는 ⓐ 새 값 5종이 DB 에 그대로 앉나 ⓑ 옛 값 <c>message</c>·<c>phone</c>·<c>remote</c> 가
    /// <b>낱말 사전에서 전수 산 채로</b> 있나 ⓒ <c>as_center</c> 는 <b>사람 입력으로 거부</b>되나를 본다.
    /// </summary>
    [Fact(DisplayName = "G-W8-7 🔴 #1 감시 — 응대유형 새 5값 저장 · 옛 3값 보존 · as_center 는 사람 입력 거부")]
    public async Task G_W8_7_응대유형은_넓히기만_했다()
    {
        const string gate = "G-W8-7 응대유형 넓히기";
        await using var env = await StatusGateDb.TryCreateAsync(gate, "hitpan_bo_w8_gate_7");
        if (env is null) return;

        await env.SeedTenantAsync("W8G7", "응대유형 보존 게이트 상사");
        var cs = NewController(env);

        // ── ⓐ 사람이 고를 수 있는 5값이 전수 앉나
        foreach (var via in new[] { "phone", "remote", "email", "sns", "etc" })
        {
            var body = $"{via} 응대 건";
            await cs.PhoneIntake(new CsAdminController.PhoneRequest
            {
                TenantCode = "W8G7", Category = "use", SubTag = "etc", Channel = "phone", Body = body,
            }, default);
            var id = await env.TicketIdByBodyAsync(body);

            var res = await cs.SetHandledVia(id, new CsAdminController.HandledViaRequest { Via = via }, default);
            Assert.True(StatusOf(res) == 200, $"{gate} 응대유형 「{via}」가 거부됐다. {Describe(res)}");
            Assert.Equal(via, await env.HandledViaOfAsync(id));

            // 화면 콤보에도 그 값이 있나(서버만 받고 화면에 없으면 사람이 못 고른다)
            Assert.Contains(CsVocab.HandledViasManual, kv => kv.Key == via);
        }

        // ── ⓑ 🔴 옛 값 전수 보존 — 낱말 사전이 그 값을 아직 읽는다
        Assert.Equal("쪽지", CsVocab.HandledViaLabel("message"));
        Assert.Equal("전화", CsVocab.HandledViaLabel("phone"));
        Assert.Equal("원격지원", CsVocab.HandledViaLabel("remote"));
        // 🔴 옛 값은 **고르는 목록에는 없다**(쪽지 응대의 정본은 as_center 다)
        Assert.DoesNotContain(CsVocab.HandledViasManual, kv => kv.Key == "message");

        // 🔴 옛 값으로 앉은 행을 화면이 그대로 읽나 — 지운 값 0개의 실측
        await env.SeedErpMessageTicketAsync("W8G7", "옛 값 message 로 앉은 건");
        var legacyId = await env.TicketIdByBodyAsync("옛 값 message 로 앉은 건");
        await env.StampHandledViaAsync(legacyId, "message");
        Assert.Equal("message", await env.HandledViaOfAsync(legacyId));

        // ── ⓒ 🔴 as_center 는 사람 입력으로 거부 — 수기건을 「히트판 A/S센터가 받았다」로 꾸미지 못한다
        await cs.PhoneIntake(new CsAdminController.PhoneRequest
        {
            TenantCode = "W8G7", Category = "use", SubTag = "etc", Channel = "phone", Body = "꾸미기 시도 건",
        }, default);
        var fakeId = await env.TicketIdByBodyAsync("꾸미기 시도 건");
        var faked = await cs.SetHandledVia(fakeId,
            new CsAdminController.HandledViaRequest { Via = CsVocab.AsCenterHandledVia }, default);
        Assert.True(StatusOf(faked) == 400,
            $"{gate} 수기건을 「히트판 A/S센터」로 적을 수 있다 — 꾸미는 길이 열렸다. {Describe(faked)}");
        Assert.Null(await env.HandledViaOfAsync(fakeId));

        // 🔴 목록 밖 자유입력도 여전히 거부
        var junk = await cs.SetHandledVia(fakeId, new CsAdminController.HandledViaRequest { Via = "아무거나" }, default);
        Assert.True(StatusOf(junk) == 400, $"{gate} 목록 밖 응대유형이 저장됐다. {Describe(junk)}");
    }

    // ══════════════════════════════════════════════════════════════
    // 공통 — 화면이 지나는 경로로 읽는 두 함수
    // ══════════════════════════════════════════════════════════════

    private static async Task<WebDetailResponse> ReadDetailAsWebAsync(CsAdminController cs, long id, string gate)
    {
        var res = await cs.Detail(id, default);
        Assert.True(StatusOf(res) == 200, $"{gate} 상세가 200 이 아니다(ticket={id}). {Describe(res)}");

        var page = JsonSerializer.Deserialize<WebDetailResponse>(
            JsonSerializer.Serialize(ValueOf(res)),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(page);
        Assert.NotNull(page!.Ticket);
        return page;
    }

    private static async Task<List<WebCsTicketRow>> ReadListAsWebAsync(CsAdminController cs, string gate)
    {
        var res = await cs.List(null, null, false, null, default);
        Assert.True(StatusOf(res) == 200, $"{gate} 목록이 200 이 아니다. {Describe(res)}");

        var page = JsonSerializer.Deserialize<WebListResponse>(
            JsonSerializer.Serialize(ValueOf(res)),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(page);
        Assert.NotNull(page!.Data);
        return page.Data!;
    }

    private static CsAdminController NewController(StatusGateDb env) =>
        new(env.Config(), NullLogger<CsAdminController>.Instance, NullLoggerFactory.Instance)
        { ControllerContext = AdminContext() };

    private static ControllerContext AdminContext()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("sub", AdminId),
            new Claim(ClaimTypes.Email, "w8status@hitpan.test"),
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

    /// <summary>격리 DB 한 벌 — 실물 적용기로 <c>installer/backoffice/*.sql</c> 전부 적재.</summary>
    private sealed class StatusGateDb : IAsyncDisposable
    {
        private readonly string _name;
        private readonly bool _created;

        private StatusGateDb(string name, bool created) { _name = name; _created = created; }

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

        public static async Task<StatusGateDb?> TryCreateAsync(string gateName, string defaultName)
        {
            var overrideName = Environment.GetEnvironmentVariable("HITPAN_BO_GATE_DB");
            var useOverride = !string.IsNullOrWhiteSpace(overrideName);
            var name = useOverride ? overrideName!.Trim() : defaultName;

            if (useOverride)
            {
                // 지정 DB 는 표를 비우므로 시험 성격의 이름만 허용 — 운영 축 오폭 차단(#39).
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

            var env = new StatusGateDb(name, created);
            await new SchemaMigrator(env.ConnString, BoSqlDir(), NullLogger<SchemaMigrator>.Instance).ApplyAsync();
            return env;
        }

        public async Task SeedTenantAsync(string code, string companyName)
        {
            await using var db = await OpenAsync();
            await db.ExecuteAsync(@"
                INSERT INTO tenants (tenant_id, tenant_code, company_name, status)
                VALUES (@Tid, @Code, @Name, 'active')",
                new { Tid = Guid.NewGuid().ToString(), Code = code, Name = companyName });
        }

        /// <summary>고객사에서 올라온 쪽지 1건 — 사람이 고를 수 없는 값이라 표에 직접 앉힌다(#13 로 칸 확인).
        /// <para>🔴 <c>handled_via</c> 를 <b>안 넣는다</b> — 수신 통로(<c>CsInboundController</c>)가 그러기 때문이다.
        /// 그 모양이 바로 사장님 반려 4) 의 조건이다.</para></summary>
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

        public async Task<long> TicketIdByBodyAsync(string body)
        {
            await using var db = await OpenAsync();
            var id = await db.ExecuteScalarAsync<long?>(
                "SELECT id FROM bo_cs_tickets WHERE body = @Body ORDER BY id DESC LIMIT 1", new { Body = body });
            Assert.True(id is not null, $"본문 「{body}」 티켓을 못 찾았다 — 앞 단계가 안 들어갔다.");
            return id!.Value;
        }

        public async Task<string?> StatusOfAsync(long id)
        {
            await using var db = await OpenAsync();
            return await db.ExecuteScalarAsync<string?>(
                "SELECT status FROM bo_cs_tickets WHERE id = @Id", new { Id = id });
        }

        public async Task<string?> HandledViaOfAsync(long id)
        {
            await using var db = await OpenAsync();
            return await db.ExecuteScalarAsync<string?>(
                "SELECT handled_via FROM bo_cs_tickets WHERE id = @Id", new { Id = id });
        }

        /// <summary>그 티켓의 전이 기록 출발점들 — 「어디서 떠났나」가 과정의 참·거짓을 가른다.</summary>
        public async Task<List<string>> LogFromStatusesOfAsync(long id)
        {
            await using var db = await OpenAsync();
            return (await db.QueryAsync<string>(
                "SELECT from_status FROM bo_cs_ticket_logs WHERE ticket_id = @Id ORDER BY acted_at",
                new { Id = id })).ToList();
        }

        public async Task<int> LogCountOfAsync(long id)
        {
            await using var db = await OpenAsync();
            return await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM bo_cs_ticket_logs WHERE ticket_id = @Id", new { Id = id });
        }

        /// <summary>🔴 작2 이전 판에서 앉았을 상태를 만든다 — 지금 서버로는 못 만든다(그게 G-W8-3 이다).</summary>
        public async Task StampStatusAsync(long id, string status)
        {
            await using var db = await OpenAsync();
            var hit = await db.ExecuteAsync(
                "UPDATE bo_cs_tickets SET status = @Status WHERE id = @Id", new { Id = id, Status = status });
            Assert.True(hit == 1, $"옛 상태 「{status}」를 앉힐 티켓을 {hit} 건 찾았다(1 건이어야 한다).");
        }

        /// <summary>옛 응대유형 값이 앉은 행을 만든다(사람 입구로는 못 만드는 값도 있다).</summary>
        public async Task StampHandledViaAsync(long id, string via)
        {
            await using var db = await OpenAsync();
            var hit = await db.ExecuteAsync(
                "UPDATE bo_cs_tickets SET handled_via = @Via WHERE id = @Id", new { Id = id, Via = via });
            Assert.True(hit == 1, $"응대유형 「{via}」를 앉힐 티켓을 {hit} 건 찾았다(1 건이어야 한다).");
        }

        /// <summary>고객이 매긴 평가 — 그 입구는 고객 ERP 쪽이라 표에 앉힌다.</summary>
        public async Task StampRatingAsync(long id, int rating, string comment)
        {
            await using var db = await OpenAsync();
            var hit = await db.ExecuteAsync(@"
                UPDATE bo_cs_tickets
                   SET rating = @Rating, rated_at = UTC_TIMESTAMP(6), rating_comment = @Comment
                 WHERE id = @Id",
                new { Id = id, Rating = rating, Comment = comment });
            Assert.True(hit == 1, $"평가 {rating} 을 앉힐 티켓을 {hit} 건 찾았다(1 건이어야 한다).");
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
                Console.Error.WriteLine($"[작2게이트] 격리 DB 뒷정리 실패: {ex.Message}");
            }
        }
    }
}
