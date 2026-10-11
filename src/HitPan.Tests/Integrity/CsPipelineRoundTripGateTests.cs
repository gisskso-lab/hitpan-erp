using System.Text;
using System.Text.Json;
using Dapper;
using HitPan.API.BackgroundServices;
using HitPan.API.Controllers;
using HitPan.API.Services.Headquarters;
using HitPan.Backoffice.API.Controllers;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261012작3 — <b>CS 쪽지 파이프라인 왕복 게이트</b> (G-W3-1 ~ G-W3-4).
///
/// <para><b>왜 이 게이트가 필요한가</b>: 종전 파이프라인 게이트
/// (<c>CsMessagePipelineGateTests</c>)는 <b>가짜 본사</b>(FakeHq)로 ERP 쪽만 쟀다.
/// 백오피스 수신 게이트(<c>CsInboundGateTests</c>)는 <b>가짜 ERP 봉투</b>로 본사 쪽만 쟀다.
/// ⇒ <b>두 반쪽이 맞물려 실제로 도는지 재 본 게이트가 0개였다.</b>
/// 사장님 오더(2026-10-12): *「파이프라인 설계·연결·구축 → <b>최종 도는지</b>」*</para>
///
/// <para><b>그래서 이 게이트는 DB 를 두 개 쓴다</b> — ERP(출하 DDL 실물 import)와
/// 백오피스(실물 <c>SchemaMigrator</c>). 그 사이를 <c>BridgeHq</c> 가 잇는데,
/// 가짜 응답을 만들지 않고 <b>실물 <c>CsInboundController</c> 를 호출</b>한다.
/// ⇒ 글자가 아니라 <b>두 제품이 맞물리는 동작</b>을 잰다(누적 지적 자리).</para>
///
/// <para>🔴 <b>로컬에서는 못 돈다</b> — ERP DB 는 <c>CREATE DATABASE</c> 가 필요하고
/// 개발 PC 의 <c>hitpan</c> 계정은 그것을 거부한다(선례). ⇒ <b>CI <c>db-gate</c> 가 유일한 계측 경로다.</b>
/// 선언 없는 건너뛰기는 <see cref="DbGateEnvironment.SkipOrFailStrict"/> 가 실패로 만든다.</para>
/// </summary>
public sealed class CsPipelineRoundTripGateTests : IDisposable
{
    // ── 고정값 ────────────────────────────────────────────────────
    private const string Pepper = "gate-pepper-cs-w3";
    private const string TenantCode = "W3GATE";
    private const string LicenseKey = "W3-GATE-LICENSE-0001";
    private const string TenantId = "11111111-1111-1111-1111-111111111111";
    private const string OwnerEmail = "w3owner@gate.test";
    private const string ChildUserId = "22222222-2222-2222-2222-222222222222";

    private readonly string _erpDb = $"hitpan_gate_w3_erp_{Guid.NewGuid():N}"[..40];
    private readonly string _boDb = $"hitpan_gate_w3_bo_{Guid.NewGuid():N}"[..40];
    private bool _erpCreated, _boCreated;

    // ══════════════════════════════════════════════════════════════
    // G-W3-1 — 올라가는 왕복: ERP 쪽지 → 큐 → 워커 → **실물 수신부** → bo_cs_tickets
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W3-1 🔴 ERP 쪽지가 실물 백오피스 수신부까지 올라가 CS 창구에 1건으로 선다 (+음성 대조)")]
    public async Task G_W3_1_올라가는_왕복이_실제로_돈다()
    {
        const string gate = "G-W3-1 올라가는 왕복";
        if (!await ReadyAsync(gate)) return;

        await using var erp = await OpenErpAsync();
        await SeedErpAsync(erp);
        await SeedBackofficeTenantAsync();

        var bo = NewBoController();

        // ── 음성 축 — 보내기 전엔 창구에 **없다**(아래 양성이 「원래 있던 것」이 아님을 보인다)
        Assert.Equal(0, await CountTicketsAsync());

        // ① 자식계정이 실물 화면 경로로 쪽지를 쓴다
        var write = await NewErpController(erp).Create(
            new CsRequestController.CreateCsRequest
            {
                Category = "use", SubTag = "etc", Body = "저장 단추가 안 보입니다",
            }, default);
        Assert.IsType<OkObjectResult>(write);

        // 로컬 원본 + 송신 큐가 **둘 다** 생겼나(한쪽만 생기면 유실이다)
        Assert.Equal(1, await erp.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cs_requests"));
        Assert.Equal(1, await erp.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cs_outbox"));

        // ② 실물 워커 사이클 — 가짜 응답이 아니라 **실물 수신 컨트롤러**가 받는다
        await CsOutboxSenderWorker.SendPendingAsync(erp, new BridgeHq(bo), OwnerEmail,
            NullLogger<CsPipelineRoundTripGateTests>.Instance, default);

        // ③ 🔴 본사 창구에 섰나 — 이것이 「도는지」의 증거다
        Assert.Equal(1, await CountTicketsAsync());

        await using var bodb = await OpenBoAsync();
        var t = await bodb.QueryFirstOrDefaultAsync(@"
            SELECT tenant_id AS TenantId, received_channel AS Channel, category AS Category,
                   body AS Body, status AS Status
              FROM bo_cs_tickets LIMIT 1");
        Assert.NotNull(t);
        Assert.Equal(TenantId, (string)t!.TenantId);
        Assert.Equal("erp_message", (string)t.Channel);
        Assert.Equal("use", (string)t.Category);
        Assert.Equal("저장 단추가 안 보입니다", (string)t.Body);

        // ④ ERP 쪽 큐가 **보냈다고 기록**했나(안 하면 다음 사이클에 또 보낸다)
        Assert.NotNull(await erp.ExecuteScalarAsync<DateTime?>("SELECT sent_at FROM cs_outbox LIMIT 1"));

        // ⑤ 멱등 — 한 번 더 돌려도 창구는 1건이다
        await CsOutboxSenderWorker.SendPendingAsync(erp, new BridgeHq(bo), OwnerEmail,
            NullLogger<CsPipelineRoundTripGateTests>.Instance, default);
        Assert.Equal(1, await CountTicketsAsync());
    }

    // ══════════════════════════════════════════════════════════════
    // G-W3-2 — 내려오는 선발신: 본사가 먼저 보낸 쪽지가 ERP 에 앉고 ack 로 「전달됨」이 된다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W3-2 🔴 본사 선발신이 ERP 쪽지함에 앉고 ack 뒤 창구가 「전달됨」으로 바뀐다")]
    public async Task G_W3_2_선발신이_고객에게_닿는다()
    {
        const string gate = "G-W3-2 선발신 전달";
        if (!await ReadyAsync(gate)) return;

        await using var erp = await OpenErpAsync();
        await SeedErpAsync(erp);
        await SeedBackofficeTenantAsync();

        var bo = NewBoController();
        var admin = NewCsAdminController();

        // ① 본사가 먼저 쪽지를 쓴다(실물 작2 입구)
        var sent = await admin.CreateOutbound(new CsAdminController.OutboundRequest
        {
            TenantCode = TenantCode, Purpose = "billing", Title = "구독요금 안내",
            Body = "이번 달 구독요금이 미결재 상태입니다",
        }, default);
        Assert.True(StatusOf(sent) == 200, Describe(sent));
        var outboundId = Field<string>(ValueOf(sent), "outboundId");

        // 🔴 작2 가 약속한 그대로 — 아직 「보냈다」가 아니다
        Assert.False(Field<bool>(ValueOf(sent), "delivered"));

        await using (var bodb = await OpenBoAsync())
        {
            Assert.Null(await bodb.ExecuteScalarAsync<DateTime?>(
                "SELECT delivered_at FROM bo_cs_outbound WHERE outbound_id = @Id", new { Id = outboundId }));
        }

        // ② 음성 축 — ERP 에는 아직 그 쪽지가 없다
        Assert.Equal(0, await erp.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM cs_requests WHERE origin = 'hq'"));

        // ③ 실물 Pull 사이클 — 백오피스가 내려주고 ERP 가 앉히고 ack 를 보낸다
        await CsOutboxSenderWorker.PullRepliesAsync(erp, new BridgeHq(bo), OwnerEmail,
            NullLogger<CsPipelineRoundTripGateTests>.Instance, default);

        // ④ 🔴 ERP 에 앉았나 — 원본 문의가 없어도 앉아야 한다(10/11 에 발견된 구멍의 반대 증거)
        var row = await erp.QueryFirstOrDefaultAsync(@"
            SELECT cs_request_id AS Id, origin AS Origin, created_by AS CreatedBy,
                   body AS Body, status AS Status
              FROM cs_requests WHERE origin = 'hq' LIMIT 1");
        Assert.NotNull(row);
        Assert.Equal(outboundId, (string)row!.Id);          // 멱등 열쇠를 그대로 쓴다
        Assert.Equal("headquarters", (string)row.CreatedBy);
        Assert.Contains("구독요금 안내", (string)row.Body);
        Assert.Contains("요금안내", (string)row.Body);        // 용도가 사람 말로 보인다(#23)

        // ⑤ 🔴 쪽지함이 **실제로 보여주나** — 「고쳤다 ≠ 갔다」의 자리다
        var replies = await NewErpController(erp).Replies(outboundId, default);
        Assert.True(StatusOf(replies) == 200, Describe(replies));
        var list = Field<System.Collections.IEnumerable>(ValueOf(replies), "data").Cast<object>().ToList();
        Assert.True(list.Count == 1, $"{gate} 쪽지함이 본사 안내를 {list.Count}건 보여준다(기대 1건).");

        // ⑥ 🔴 평가 대상이 아니다(질문이 아니다) — B-5
        Assert.False(Field<bool>(Field<object>(ValueOf(replies), "rating"), "canRate"));

        // ⑦ 🔴 ack 가 도착해 창구가 「전달됨」으로 바뀌었나 — 이것이 D-4 의 과녁이다
        await using (var bodb = await OpenBoAsync())
        {
            Assert.NotNull(await bodb.ExecuteScalarAsync<DateTime?>(
                "SELECT delivered_at FROM bo_cs_outbound WHERE outbound_id = @Id", new { Id = outboundId }));
        }

        var listed = await admin.ListOutbound("delivered", default);
        Assert.True(Field<System.Collections.IEnumerable>(ValueOf(listed), "items").Cast<object>().Count() == 1,
            $"{gate} 창구의 「전달됨」 목록에 1건이 보여야 한다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-W3-3 — 음성·멱등: 거둬들인 쪽지는 안 내려가고, 두 번 내려와도 1건이고, ack 없으면 또 내려온다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W3-3 🔴 취소·폐기분은 안 내려간다 · 두 번 내려와도 1건 · ack 없으면 또 내려온다(유실 0)")]
    public async Task G_W3_3_음성과_멱등()
    {
        const string gate = "G-W3-3 음성·멱등";
        if (!await ReadyAsync(gate)) return;

        await using var erp = await OpenErpAsync();
        await SeedErpAsync(erp);
        await SeedBackofficeTenantAsync();

        var bo = NewBoController();
        var admin = NewCsAdminController();

        // ① 취소한 쪽지 — 사람이 거둬들인 것이 뒤늦게 고객에게 가면 안 된다
        var canceled = Field<string>(ValueOf(await admin.CreateOutbound(new CsAdminController.OutboundRequest
        { TenantCode = TenantCode, Purpose = "promo", Body = "취소될 안내" }, default)), "outboundId");
        Assert.True(StatusOf(await admin.CancelOutbound(canceled, default)) == 200);

        // ② 폐기한 쪽지
        var purged = Field<string>(ValueOf(await admin.CreateOutbound(new CsAdminController.OutboundRequest
        { TenantCode = TenantCode, Purpose = "event", Body = "폐기될 안내" }, default)), "outboundId");
        Assert.True(StatusOf(await admin.PurgeOutbound(purged,
            new CsAdminController.PurgeRequest { Reason = "오발송" }, default)) == 200);

        // ③ 살아 있는 쪽지 하나
        var alive = Field<string>(ValueOf(await admin.CreateOutbound(new CsAdminController.OutboundRequest
        { TenantCode = TenantCode, Purpose = "promo", Body = "정상 안내" }, default)), "outboundId");

        await CsOutboxSenderWorker.PullRepliesAsync(erp, new BridgeHq(bo), OwnerEmail,
            NullLogger<CsPipelineRoundTripGateTests>.Instance, default);

        // 🔴 살아 있는 것만 내려왔다
        var ids = (await erp.QueryAsync<string>("SELECT cs_request_id FROM cs_requests WHERE origin = 'hq'")).ToList();
        Assert.True(ids.Count == 1, $"{gate} ERP 에 {ids.Count}건 앉았다(기대 1건 — 취소·폐기분이 내려갔다).");
        Assert.Equal(alive, ids[0]);
        Assert.DoesNotContain(canceled, ids);
        Assert.DoesNotContain(purged, ids);

        // ④ 멱등 — 한 번 더 돌려도 1건(이미 전달돼 안 내려오지만, 내려와도 INSERT IGNORE 가 막는다)
        await CsOutboxSenderWorker.PullRepliesAsync(erp, new BridgeHq(bo), OwnerEmail,
            NullLogger<CsPipelineRoundTripGateTests>.Instance, default);
        Assert.Equal(1, await erp.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM cs_requests WHERE origin = 'hq'"));
        Assert.Equal(1, await erp.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM cs_replies WHERE cs_request_id = @Id", new { Id = alive }));

        // ⑤ 🔴 유실 0 — ack 가 **안 가면** 또 내려온다. ack 를 삼키는 다리로 확인한다.
        var again = Field<string>(ValueOf(await admin.CreateOutbound(new CsAdminController.OutboundRequest
        { TenantCode = TenantCode, Purpose = "etc", Body = "ack 없이 받을 안내" }, default)), "outboundId");

        var noAck = new BridgeHq(bo) { SwallowAck = true };
        await CsOutboxSenderWorker.PullRepliesAsync(erp, noAck, OwnerEmail,
            NullLogger<CsPipelineRoundTripGateTests>.Instance, default);

        await using (var bodb = await OpenBoAsync())
        {
            // 저장은 됐지만 전달 표식은 **안 찍혔다**(ack 가 안 갔다)
            Assert.NotNull(await erp.ExecuteScalarAsync<string?>(
                "SELECT cs_request_id FROM cs_requests WHERE cs_request_id = @Id", new { Id = again }));
            Assert.Null(await bodb.ExecuteScalarAsync<DateTime?>(
                "SELECT delivered_at FROM bo_cs_outbound WHERE outbound_id = @Id", new { Id = again }));
        }

        // ⇒ 다음 사이클에 **또 내려온다**(사라지지 않았다) · 멱등이 중복을 막는다
        var bridge = new BridgeHq(bo);
        await CsOutboxSenderWorker.PullRepliesAsync(erp, bridge, OwnerEmail,
            NullLogger<CsPipelineRoundTripGateTests>.Instance, default);
        Assert.True(bridge.LastOutboundCount >= 1,
            $"{gate} ack 안 보낸 쪽지가 다음 Pull 에 안 내려왔다 — 조용히 사라지는 길이 열려 있다.");
        Assert.Equal(1, await erp.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM cs_requests WHERE cs_request_id = @Id", new { Id = again }));
    }

    // ══════════════════════════════════════════════════════════════
    // G-W3-4 — 3중 일치 음성 대조군: 하나만 틀려도 못 올라가고, 쪽지는 **큐에 남는다**
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W3-4 🔴 시리얼·부모계정 축이 틀리면 401 이고 쪽지는 큐에 남는다(유실 0)")]
    public async Task G_W3_4_삼중일치_음성대조()
    {
        const string gate = "G-W3-4 3중 일치";
        if (!await ReadyAsync(gate)) return;

        await using var erp = await OpenErpAsync();
        await SeedErpAsync(erp);
        await SeedBackofficeTenantAsync();

        var bo = NewBoController();

        Assert.IsType<OkObjectResult>(await NewErpController(erp).Create(
            new CsRequestController.CreateCsRequest { Category = "use", SubTag = "etc", Body = "인증 대조용" }, default));

        // ① 시리얼 축 — 틀린 라이선스로 보낸다
        await CsOutboxSenderWorker.SendPendingAsync(erp, new BridgeHq(bo) { License = "W3-GATE-WRONG-9999" },
            OwnerEmail, NullLogger<CsPipelineRoundTripGateTests>.Instance, default);
        Assert.Equal(0, await CountTicketsAsync());
        // 🔴 쪽지는 큐에 **남아 있다** — 인증 실패가 유실이 되면 안 된다
        Assert.Null(await erp.ExecuteScalarAsync<DateTime?>("SELECT sent_at FROM cs_outbox LIMIT 1"));

        // ② 부모계정 축 — 등록된 대표와 다른 계정으로 보낸다
        await CsOutboxSenderWorker.SendPendingAsync(erp, new BridgeHq(bo), "stranger@gate.test",
            NullLogger<CsPipelineRoundTripGateTests>.Instance, default);
        Assert.Equal(0, await CountTicketsAsync());
        Assert.Null(await erp.ExecuteScalarAsync<DateTime?>("SELECT sent_at FROM cs_outbox LIMIT 1"));

        // ③ 양성 — 셋이 맞으면 올라간다(음성이 「원래 안 되는 것」이 아님을 보인다)
        await CsOutboxSenderWorker.SendPendingAsync(erp, new BridgeHq(bo), OwnerEmail,
            NullLogger<CsPipelineRoundTripGateTests>.Instance, default);
        Assert.Equal(1, await CountTicketsAsync());

        // ④ 거부가 기록됐나 — 「누가 왜 거부됐나」가 없으면 운영에서 원인을 못 찾는다
        await using var bodb = await OpenBoAsync();
        Assert.True(await bodb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM bo_cs_reject_logs") >= 2);
    }

    // ══════════════════════════════════════════════════════════════
    // G-W3-5 — 🔴 `cs_requests.origin`(DB-144)이 **아직 없는 DB** 에서도 종전 화면이 산다
    //
    // 왜 이 게이트가 필요한가: ERP 마이그 적용기는 **실패해도 기동을 계속**한다
    //   (`Program.cs:535~550` 가 직접 적어 뒀다 — 「새 컬럼을 쓰는 화면은 오류가 날 수 있으나」).
    //   ⇒ 「코드는 갔는데 칸은 없는」 상태가 **실재한다**(9/29 디스크 부족으로 업데이트가 막힌 전례).
    //   그 상태에서 새 칸을 조건 없이 읽으면 **되던 CS 쪽지 화면이 전부 500** 이다 —
    //   신규 기능 실패가 아니라 **회귀**다. 작2 [4] V-2 가 백오피스에서 잡은 바로 그 모양이다.
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W3-5 🔴 origin 칸이 없는 DB 에서도 쪽지 목록·평가 판정이 200 이다 (+음성 대조)")]
    public async Task G_W3_5_칸이_없어도_종전화면이_산다()
    {
        const string gate = "G-W3-5 칸 없는 DB";
        if (!await ReadyAsync(gate)) return;

        await using var erp = await OpenErpAsync();
        await SeedErpAsync(erp);

        // 🔴 음성 대조군 먼저 — 칸이 **있을 때** 200 이고 origin 이 'local' 로 나온다.
        //   (이게 없으면 「원래 안 되던 것」을 봉합 덕분이라고 읽을 수 있다)
        CsRequestController.ResetOriginColumnCache();
        Assert.IsType<OkObjectResult>(await NewErpController(erp).Create(
            new CsRequestController.CreateCsRequest { Category = "use", SubTag = "etc", Body = "칸 있을 때" }, default));

        var withCol = await NewErpController(erp).List(default);
        Assert.True(StatusOf(withCol) == 200, $"{gate} 칸이 있는데 목록이 200 이 아니다. {Describe(withCol)}");
        var first = Field<System.Collections.IEnumerable>(ValueOf(withCol), "data").Cast<object>().First();
        Assert.Equal("local", (string)((IDictionary<string, object?>)first)["origin"]!);

        // 🔴 칸을 **실제로 떼어** 마이그 미적용 상태를 만든다(글자로 흉내내지 않는다)
        await erp.ExecuteAsync("ALTER TABLE cs_requests DROP INDEX idx_cs_req_origin");
        await erp.ExecuteAsync("ALTER TABLE cs_requests DROP COLUMN origin");
        CsRequestController.ResetOriginColumnCache();

        // ① 목록이 **여전히 200** 이어야 한다 — 이것이 회귀 차단의 과녁이다
        var noCol = await NewErpController(erp).List(default);
        Assert.True(StatusOf(noCol) == 200,
            $"{gate} origin 칸이 없을 때 쪽지 목록이 500 이다 — **되던 화면의 회귀**다. {Describe(noCol)}");

        var rows = Field<System.Collections.IEnumerable>(ValueOf(noCol), "data").Cast<object>().ToList();
        Assert.True(rows.Count == 1, $"{gate} 칸이 없을 때 목록이 {rows.Count}건이다(기대 1건).");
        Assert.Equal("local", (string)((IDictionary<string, object?>)rows[0])["origin"]!);

        // ② 답·평가 판정 경로도 산다(여기서도 origin 을 읽는다)
        var id = (string)((IDictionary<string, object?>)rows[0])["csRequestId"]!;
        var replies = await NewErpController(erp).Replies(id, default);
        Assert.True(StatusOf(replies) == 200,
            $"{gate} origin 칸이 없을 때 쪽지함이 500 이다. {Describe(replies)}");

        // 뒷정리 — 다음 시험이 칸 있는 상태를 보게 되돌린다(이 DB 는 이 시험 전용이지만 규율로 남긴다)
        await erp.ExecuteAsync("ALTER TABLE cs_requests ADD COLUMN origin varchar(10) NOT NULL DEFAULT 'local'");
        CsRequestController.ResetOriginColumnCache();
    }

    // ══════════════════════════════════════════════════════════════
    // 다리 — 가짜 응답을 만들지 않는다. 실물 수신 컨트롤러를 부른다.
    // ══════════════════════════════════════════════════════════════

    private sealed class BridgeHq : IHeadquartersClient
    {
        private readonly CsInboundController _bo;
        public BridgeHq(CsInboundController bo) => _bo = bo;

        /// <summary>틀린 시리얼로 음성 대조하기 위한 자리(기본은 정상 값).</summary>
        public string License { get; init; } = LicenseKey;

        /// <summary>ack 를 삼켜 「전달 확인 실패」를 만든다 — 유실 0 을 재는 장치.</summary>
        public bool SwallowAck { get; init; }

        /// <summary>직전 Pull 에서 내려온 선발신 건수(또 내려오는지 재는 자리).</summary>
        public int LastOutboundCount { get; private set; }

        public async Task<(int StatusCode, string Body)> PostAsync(string path, string dataJson, string ownerAccountId, CancellationToken ct)
        {
            if (SwallowAck && path.Contains("outbound/ack")) return (500, "swallowed");

            var env = Envelope(dataJson, ownerAccountId, License);
            IActionResult res = path switch
            {
                var p when p.EndsWith("/cs/messages") => await _bo.ReceiveMessage(env, ct),
                var p when p.EndsWith("/cs/outbound/ack") => await _bo.AckOutbound(env, ct),
                var p when p.EndsWith("/cs/ratings") => await _bo.ReceiveRating(env, ct),
                _ => new NotFoundResult(),
            };
            return (StatusOf(res), JsonSerializer.Serialize(ValueOf(res)));
        }

        public async Task<(int StatusCode, string Body)> PullAsync(string path, string ownerAccountId, CancellationToken ct)
        {
            var res = await _bo.PullReplies(Envelope("{}", ownerAccountId, License), ct);
            var body = JsonSerializer.Serialize(ValueOf(res),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            try
            {
                using var doc = JsonDocument.Parse(body);
                LastOutboundCount = doc.RootElement.TryGetProperty("outbound", out var o)
                    && o.ValueKind == JsonValueKind.Array ? o.GetArrayLength() : 0;
            }
            catch (JsonException) { LastOutboundCount = 0; }   // #15 — 계측 실패가 시험을 죽이지 않는다

            return (StatusOf(res), body);
        }

        public Task<(int StatusCode, string Body)> PostOwnerReportAsync(string ownerAccountId, CancellationToken ct)
            => Task.FromResult((200, "{}"));

        private static JsonElement Envelope(string dataJson, string owner, string license)
            => JsonDocument.Parse(
                "{\"tenantCode\":" + JsonSerializer.Serialize(TenantCode)
              + ",\"licenseKey\":" + JsonSerializer.Serialize(license)
              + ",\"ownerAccountId\":" + JsonSerializer.Serialize(owner)
              + ",\"data\":" + dataJson + "}").RootElement.Clone();
    }

    // ══════════════════════════════════════════════════════════════
    // 시험 장치 — DB 둘(ERP 출하 DDL · 백오피스 실물 적용기)
    // ══════════════════════════════════════════════════════════════

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다.");
    }

    private static string ServerConnString()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root";
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
        return $"Server={host};Port={port};User={user};Password={pass};"
             + "DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;";
    }

    private static string MysqlExe() =>
        Environment.GetEnvironmentVariable("HITPAN_MYSQL") ?? @"C:\Program Files\MariaDB 11.4\bin\mysql.exe";

    private string ErpConn() => ServerConnString().Replace("User=", $"Database={_erpDb};User=") + "Pooling=false;";
    private string BoConn() => ServerConnString().Replace("User=", $"Database={_boDb};User=") + "Pooling=false;";

    private async Task<MySqlConnection> OpenErpAsync()
    {
        var c = new MySqlConnection(ErpConn());
        await c.OpenAsync();
        return c;
    }

    private async Task<MySqlConnection> OpenBoAsync()
    {
        var c = new MySqlConnection(BoConn());
        await c.OpenAsync();
        return c;
    }

    /// <summary>DB 둘을 세운다. 못 세우면 🔴 선언 없는 건너뛰기는 **실패**다.</summary>
    private async Task<bool> ReadyAsync(string gate)
    {
        try
        {
            await using var admin = new MySqlConnection(ServerConnString());
            await admin.OpenAsync();
            await admin.ExecuteAsync($"DROP DATABASE IF EXISTS `{_erpDb}`; "
                + $"CREATE DATABASE `{_erpDb}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
            _erpCreated = true;
            await admin.ExecuteAsync($"DROP DATABASE IF EXISTS `{_boDb}`; "
                + $"CREATE DATABASE `{_boDb}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
            _boCreated = true;
        }
        catch (MySqlException ex)
        {
            Console.Error.WriteLine($"[{gate}] DB 준비 실패({ex.Number}): {ex.Message}");
            return !DbGateEnvironment.SkipOrFailStrict(gate);
        }

        if (!File.Exists(MysqlExe()))
        {
            Console.Error.WriteLine($"[{gate}] mysql 클라이언트 없음: {MysqlExe()}");
            return !DbGateEnvironment.SkipOrFailStrict(gate);
        }

        ImportErpShippingDdl();                 // ERP = 출하 DDL 실물(= 신규설치와 같은 판)
        await ApplyBackofficeSchemaAsync();     // 백오피스 = 실물 적용기
        return true;
    }

    /// <summary>🔴 출하 DDL 로 세운다 — <c>cs_requests.origin</c>(작3 B-1)이 출하본에 실제로 있는지도 여기서 드러난다.</summary>
    private void ImportErpShippingDdl()
    {
        var ddl = Path.Combine(RepoRoot(), "installer", "hitpan_db_clean.sql");
        var psi = new System.Diagnostics.ProcessStartInfo(MysqlExe())
        {
            RedirectStandardInput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        psi.ArgumentList.Add($"--host={Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost"}");
        psi.ArgumentList.Add($"--port={Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306"}");
        psi.ArgumentList.Add($"-u{Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root"}");
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS");
        if (!string.IsNullOrEmpty(pass)) psi.ArgumentList.Add($"-p{pass}");
        psi.ArgumentList.Add("--default-character-set=utf8mb4");
        psi.ArgumentList.Add(_erpDb);

        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.StandardInput.Write(File.ReadAllText(ddl));
        proc.StandardInput.Close();
        var err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        Assert.True(proc.ExitCode == 0, $"ERP 출하 DDL import 실패:\n{err}");
    }

    private async Task ApplyBackofficeSchemaAsync()
    {
        var dir = Path.Combine(RepoRoot(), "installer", "backoffice");
        await new SchemaMigrator(BoConn(), dir, NullLogger<SchemaMigrator>.Instance).ApplyAsync();
    }

    /// <summary>ERP 쪽 전제 — 부모계정 1명(3중 재료) + 자식계정 1명(쓴 사람).</summary>
    private static async Task SeedErpAsync(MySqlConnection erp)
    {
        await erp.ExecuteAsync(@"
            INSERT IGNORE INTO users (user_id, tenant_id, email, password_hash, user_name, role, account_type, is_parent, is_active)
            VALUES (@Owner, @Tid, @Email, 'x', '대표', 'tenant_admin', 'tenant_admin', 1, 1),
                   (@Child, @Tid, 'child@gate.test', 'x', '직원', 'tenant_user', 'tenant_user', 0, 1)",
            new { Owner = "33333333-3333-3333-3333-333333333333", Child = ChildUserId, Tid = TenantId, Email = OwnerEmail });
    }

    /// <summary>백오피스 쪽 전제 — 3중 일치가 통과하도록 고객사 1건(시리얼 해시·대표 아이디 포함).</summary>
    private async Task SeedBackofficeTenantAsync()
    {
        await using var db = await OpenBoAsync();
        await db.ExecuteAsync(@"
            INSERT IGNORE INTO tenants (tenant_id, tenant_code, company_name, license_key_hash, owner_account_id)
            VALUES (@Tid, @Code, '왕복 게이트 상사', @Hash, @Owner)",
            new
            {
                Tid = TenantId, Code = TenantCode, Owner = OwnerEmail,
                Hash = Hmac(LicenseKey.Trim().ToUpperInvariant().Replace(" ", ""), Pepper),
            });
    }

    private static string Hmac(string data, string key)
    {
        using var h = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
    }

    private async Task<int> CountTicketsAsync()
    {
        await using var db = await OpenBoAsync();
        return await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM bo_cs_tickets");
    }

    private IConfiguration BoConfig() => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["ConnectionStrings:BackofficeDb"] = BoConn(),
            ["License:Pepper"] = Pepper,
        }).Build();

    private CsInboundController NewBoController() =>
        new(BoConfig(), NullLogger<CsInboundController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private CsAdminController NewCsAdminController()
    {
        var identity = new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim("sub", "w3-gate-admin"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, "w3admin@gate.test"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "platform_admin"),
        }, "gate");
        var ctx = new DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(identity) };
        return new CsAdminController(BoConfig(), NullLogger<CsAdminController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx },
        };
    }

    private static CsRequestController NewErpController(MySqlConnection erp)
    {
        var ctx = new DefaultHttpContext();
        ctx.Items["TenantId"] = TenantId;
        ctx.Items["UserId"] = ChildUserId;
        return new CsRequestController(erp, NullLogger<CsRequestController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx },
        };
    }

    private static int StatusOf(IActionResult res) => res switch
    {
        ObjectResult o => o.StatusCode ?? 200,
        StatusCodeResult s => s.StatusCode,
        ForbidResult => 403,
        _ => 200,
    };

    private static object? ValueOf(IActionResult res) => (res as ObjectResult)?.Value;

    private static T Field<T>(object? value, string name)
    {
        Assert.NotNull(value);
        var prop = value!.GetType().GetProperty(name);
        Assert.NotNull(prop);
        return (T)prop!.GetValue(value)!;
    }

    private static string Describe(IActionResult res) =>
        $"status={StatusOf(res)} value={JsonSerializer.Serialize(ValueOf(res))}";

    public void Dispose()
    {
        try
        {
            using var admin = new MySqlConnection(ServerConnString());
            admin.Open();
            if (_erpCreated) admin.Execute($"DROP DATABASE IF EXISTS `{_erpDb}`");
            if (_boCreated) admin.Execute($"DROP DATABASE IF EXISTS `{_boDb}`");
        }
        catch (MySqlException ex)
        {
            Console.Error.WriteLine($"[G-W3] 시험 DB 정리 실패(무해): {ex.Message}");  // #15
        }
    }
}
