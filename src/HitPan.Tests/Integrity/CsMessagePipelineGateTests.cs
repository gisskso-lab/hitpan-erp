using Dapper;
using HitPan.API.BackgroundServices;
using HitPan.API.Controllers;
using HitPan.API.Services.Headquarters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 작14 묶음 B 게이트 — CS 쪽지 왕복을 **동작으로** 잰다 (작업지시서 B-게이트 · 설계 §5-ⓓ).
///
/// <para>격리 DB 는 **출하 DDL(installer/hitpan_db_clean.sql) 실물 import** 로 세운다
/// (AccountSeatGateTests 선례 그대로) ⇒ cs_* 4표가 출하 DDL 에 진짜 들어갔는지(#36)가
/// 모든 시험의 선결 과녁이 된다 — 빠졌으면 전부 즉사한다.</para>
///
/// <para>무엇을 재나:
/// G-CS-1 유실 0 — 본사가 안 닿아도(연결 실패) 큐 행은 남고, 닿으면 자동 송신·전송완료.
///         🔴 이 시험이 next_attempt_at 의 UTC 명시도 잰다(로컬시각 기본값이면 KST 에서 9시간 지연).
/// G-CS-2 400 = 종결 — 재시도 없음 · 고객 화면 상태 「거부」.
/// G-CS-3 5xx = 재시도 — attempt 증가 · 행 삭제 0 (「포기 = 삭제」 금지 자리).
/// G-CS-4 문① — 식별번호 모양 본문 400 + 거부 기록 + 저장 0 · **양성 대조군 = 같은 요청에서 그 숫자만 뺀 것**.
/// G-CS-5 문② — 문①을 우회해 큐에 직접 넣어도 송신 직전에 잡혀 rejected + 거부 기록.
/// G-CS-6 멱등 — 같은 쪽지는 큐에 두 번 못 든다(UNIQUE).
/// G-CS-7 답 멱등 — 같은 답 2번 수신 = 1건 + 상태 「답변도착」.</para>
///
/// <para>DB 없는 로컬 = SKIP 선언 · CI db-gate(HITPAN_REQUIRE_DB)에서는 SKIP=FAIL.
/// ⑥ 17시나리오 재측정(#1·#2·#4)은 실물 측정이라 이 게이트 밖 — [4] 몫으로 남긴다(숨기지 않고 적는다).</para>
/// </summary>
public sealed class CsMessagePipelineGateTests : IDisposable
{
    private readonly string _dbName = $"hitpan_gate_cs_{Guid.NewGuid():N}";
    private bool _created;

    // ── 격리 DB (AccountSeatGateTests 선례 그대로 — 출하 DDL 실물 import) ──

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

    // 풀 끄기 — 시험마다 DB 이름이 달라 풀이 쌓이면 CI max_connections 를 갉는다(AccountSeat 봉합2 선례).
    private string DbConnString() => ServerConnString().Replace("User=", $"Database={_dbName};User=") + "Pooling=false;";

    private static string MysqlExe() =>
        Environment.GetEnvironmentVariable("HITPAN_MYSQL") ?? @"C:\Program Files\MariaDB 11.4\bin\mysql.exe";

    private bool Ready(string gate)
    {
        if (!DbGateEnvironment.IsCi)
        {
            var ok = File.Exists(MysqlExe());
            if (ok)
            {
                try
                {
                    using var c = new MySqlConnection(ServerConnString());
                    c.Open();
                    c.Execute($"CREATE DATABASE IF NOT EXISTS `{_dbName}`");
                    c.Execute($"DROP DATABASE IF EXISTS `{_dbName}`");
                }
                catch (MySqlException ex)
                {
                    Console.Error.WriteLine($"[{gate}] 서버 접속·권한 없음: {ex.Message}");
                    ok = false;
                }
            }
            // 🔴 엄격판 — 비엄격 SkipOrFail 로 돌렸더니 로컬 무DB 에서 7건이 633ms 「통과」로 보였다
            //    (조용한 초록 · 판별은 소요시간). 선언 없는 건너뛰기는 실패로 만든다(S-2 게이트와 같은 규율).
            if (!ok) return !DbGateEnvironment.SkipOrFailStrict(gate);
        }
        SetUpFreshInstall();
        return true;
    }

    private void SetUpFreshInstall()
    {
        var ddlPath = Path.Combine(RepoRoot(), "installer", "hitpan_db_clean.sql");
        using (var admin = new MySqlConnection(ServerConnString()))
        {
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{_dbName}`; "
                        + $"CREATE DATABASE `{_dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        }
        _created = true;

        var psi = new System.Diagnostics.ProcessStartInfo(MysqlExe())
        {
            RedirectStandardInput = true, RedirectStandardError = true, UseShellExecute = false
        };
        psi.ArgumentList.Add($"--host={Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost"}");
        psi.ArgumentList.Add($"--port={Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306"}");
        psi.ArgumentList.Add($"-u{Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root"}");
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS");
        if (!string.IsNullOrEmpty(pass)) psi.ArgumentList.Add($"-p{pass}");
        psi.ArgumentList.Add("--default-character-set=utf8mb4");
        psi.ArgumentList.Add(_dbName);
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.StandardInput.Write(File.ReadAllText(ddlPath));
        proc.StandardInput.Close();
        var err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        Assert.True(proc.ExitCode == 0, $"출하 DDL import 실패:\n{err}");
    }

    public void Dispose()
    {
        if (!_created) return;
        try
        {
            using var admin = new MySqlConnection(ServerConnString());
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{_dbName}`;");
        }
        catch (MySqlException ex)
        {
            Console.Error.WriteLine($"[CS게이트] 격리 DB 뒷정리 실패: {ex.Message}");
        }
    }

    // ── 재료 ─────────────────────────────────────────────────────────

    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string Owner = "boss@gate.test";

    private async Task<MySqlConnection> OpenAsync()
    {
        var c = new MySqlConnection(DbConnString());
        await c.OpenAsync();
        return c;
    }

    private static CsRequestController Controller(MySqlConnection db)
    {
        var ctrl = new CsRequestController(db, NullLogger<CsRequestController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        ctrl.HttpContext.Items["TenantId"] = Tenant;
        ctrl.HttpContext.Items["UserId"] = "child-user-1";
        return ctrl;
    }

    private static async Task<IActionResult> PostAsync(CsRequestController c, string? body)
        => await c.Create(new CsRequestController.CreateCsRequest
        {
            Category = "how_to",
            SubTag = "etc",
            Body = body,
            ScreenCode = "gate",
        }, default);

    /// <summary>시간 여행 — 백오프로 미래에 걸린 행을 지금 집히게 당긴다(시험 전용).</summary>
    private static Task RewindAsync(MySqlConnection db) =>
        db.ExecuteAsync("UPDATE cs_outbox SET next_attempt_at = DATE_SUB(UTC_TIMESTAMP(6), INTERVAL 1 SECOND)");

    private static Task RunCycleAsync(MySqlConnection db, FakeHq hq) =>
        CsOutboxSenderWorker.SendPendingAsync(db, hq, Owner, NullLogger.Instance, default);

    // ── 게이트 ───────────────────────────────────────────────────────

    [Fact(DisplayName = "G-CS-1 🔴 유실 0 — 본사가 안 닿아도 큐 행은 남고, 닿으면 자동 송신·「전송완료」 (UTC 폴링 포함)")]
    public async Task G_CS_1_유실0_자동재송신()
    {
        if (!Ready("G-CS-1")) return;
        await using var db = await OpenAsync();

        var ok = await PostAsync(Controller(db), "저장 단추가 안 보여요");
        Assert.IsType<OkObjectResult>(ok);

        // 🔴 UTC 명시 검증 — 방금 넣은 행이 **지금** 집혀야 한다(로컬시각 기본값이면 KST 에서 0건).
        var pickable = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM cs_outbox WHERE sent_at IS NULL AND next_attempt_at <= UTC_TIMESTAMP(6)");
        Assert.True(pickable == 1, $"방금 넣은 쪽지가 폴링에 안 집힌다(집힘 {pickable}건) — next_attempt_at 시간대 자리");

        // 터널 끊김(연결 실패) — 행은 남는다. 글 삭제는 없다.
        var dead = new FakeHq((0, "connect-fail"));
        await RunCycleAsync(db, dead);
        var row1 = await db.QueryFirstAsync(
            "SELECT sent_at AS SentAt, attempt_count AS Attempt, terminal_reason AS Reason FROM cs_outbox");
        Assert.Null((object?)row1.SentAt);
        Assert.Equal(1, (int)row1.Attempt);
        Assert.Null((object?)row1.Reason);

        // 복구 — 자동 송신 + 「전송완료」 + 봉투에 3중 재료(부모계정)가 실렸다.
        await RewindAsync(db);
        var alive = new FakeHq((200, "{\"success\":true}"));
        await RunCycleAsync(db, alive);
        Assert.Equal(1, alive.PostCalls);
        Assert.Equal(Owner, alive.LastOwner);
        var sent = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cs_outbox WHERE sent_at IS NOT NULL");
        Assert.Equal(1, sent);
        var status = await db.ExecuteScalarAsync<string>("SELECT status FROM cs_requests");
        Assert.Equal("전송완료", status);

        // 송신 뒤 다시 돌려도 재전송 0 (sent_at 이 멱등의 자리)
        await RunCycleAsync(db, alive);
        Assert.Equal(1, alive.PostCalls);
    }

    [Fact(DisplayName = "G-CS-2 🔴 400 = 종결 — 재시도 없음 · 상태 「거부」 · 행 삭제 0")]
    public async Task G_CS_2_400_종결()
    {
        if (!Ready("G-CS-2")) return;
        await using var db = await OpenAsync();
        Assert.IsType<OkObjectResult>(await PostAsync(Controller(db), null));

        var hq = new FakeHq((400, "{\"message\":\"형식 오류\"}"));
        await RunCycleAsync(db, hq);
        var row = await db.QueryFirstAsync(
            "SELECT terminal_reason AS Reason, sent_at AS SentAt FROM cs_outbox");
        Assert.Equal("rejected", (string)row.Reason);
        Assert.Equal("거부", await db.ExecuteScalarAsync<string>("SELECT status FROM cs_requests"));

        // 종결 뒤에는 집지 않는다 — 재시도가 돌면 FAIL.
        await RewindAsync(db);
        await RunCycleAsync(db, hq);
        Assert.Equal(1, hq.PostCalls);
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cs_outbox"));
    }

    [Fact(DisplayName = "G-CS-3 🔴 5xx = 재시도 — attempt 증가 · terminal 없음 · 행 삭제 0")]
    public async Task G_CS_3_5xx_재시도()
    {
        if (!Ready("G-CS-3")) return;
        await using var db = await OpenAsync();
        Assert.IsType<OkObjectResult>(await PostAsync(Controller(db), null));

        var hq = new FakeHq((500, "서버 오류"));
        await RunCycleAsync(db, hq);
        await RewindAsync(db);
        await RunCycleAsync(db, hq);

        var row = await db.QueryFirstAsync(
            "SELECT attempt_count AS Attempt, terminal_reason AS Reason, sent_at AS SentAt FROM cs_outbox");
        Assert.Equal(2, (int)row.Attempt);
        Assert.Null((object?)row.Reason);
        Assert.Null((object?)row.SentAt);
        Assert.Equal(2, hq.PostCalls);
    }

    [Fact(DisplayName = "G-CS-4 🔴 문① — 식별번호 모양 본문 = 400 + 거부 기록 + 저장 0 · 양성 대조군 = 숫자만 뺀 같은 요청")]
    public async Task G_CS_4_문1_입력거부_대조군()
    {
        if (!Ready("G-CS-4")) return;
        await using var db = await OpenAsync();
        var ctrl = Controller(db);

        // 음성 — 주민등록번호 **모양**의 가짜 숫자(000000-0000000)만 싣는다. 진짜 값이 아니라 모양 검사 과녁.
        var bad = await PostAsync(ctrl, "직원 등록이 안 돼요. 000000-0000000 입니다.");
        Assert.IsType<BadRequestObjectResult>(bad);
        Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cs_requests"));
        Assert.Equal(1, await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM cs_forbidden_rejects WHERE rule_code = 'resident_no'"));
        // 🔴 막힌 값 자체는 어디에도 없다(CTO 조건③) — 거부 표는 규칙코드·시각 칸뿐이다(표 모양으로 보증).

        // 양성 대조군 — 같은 문장에서 그 숫자만 뺐다. 거부가 「원래 안 되는 것」이 아님을 증명.
        var good = await PostAsync(ctrl, "직원 등록이 안 돼요.");
        Assert.IsType<OkObjectResult>(good);
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cs_requests"));
    }

    [Fact(DisplayName = "G-CS-5 🔴 문② — 문①을 우회해 큐에 직접 넣어도 송신 직전에 잡힌다(rejected + 거부 기록)")]
    public async Task G_CS_5_문2_송신직전거부()
    {
        if (!Ready("G-CS-5")) return;
        await using var db = await OpenAsync();

        // 문①을 거치지 않은 행(수동 INSERT·마이그 경로 모사) — body 에 사업자번호 모양.
        await db.ExecuteAsync(@"
            INSERT INTO cs_requests (cs_request_id, tenant_id, created_by, category, sub_tag, body, erp_version)
            VALUES ('bypass-1', @T, 'u', 'how_to', 'etc', '사업자 000-00-00000 등록이 안 돼요', '0');
            INSERT INTO cs_outbox (tenant_id, cs_request_id, payload_json, next_attempt_at)
            VALUES (@T, 'bypass-1', '{""csRequestId"":""bypass-1"",""body"":""사업자 000-00-00000 등록이 안 돼요""}', UTC_TIMESTAMP(6));",
            new { T = Tenant });

        var hq = new FakeHq((200, "ok"));
        await RunCycleAsync(db, hq);

        Assert.Equal(0, hq.PostCalls); // 본사로 한 발도 안 나갔다
        Assert.Equal("rejected", await db.ExecuteScalarAsync<string>("SELECT terminal_reason FROM cs_outbox"));
        Assert.Equal(1, await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM cs_forbidden_rejects WHERE rule_code = 'biz_no'"));
    }

    [Fact(DisplayName = "G-CS-6 🔴 멱등 — 같은 쪽지는 큐에 두 번 못 든다(UNIQUE 가 서버 강제)")]
    public async Task G_CS_6_큐멱등()
    {
        if (!Ready("G-CS-6")) return;
        await using var db = await OpenAsync();
        Assert.IsType<OkObjectResult>(await PostAsync(Controller(db), null));

        var id = await db.ExecuteScalarAsync<string>("SELECT cs_request_id FROM cs_outbox");
        var ex = await Assert.ThrowsAsync<MySqlException>(() => db.ExecuteAsync(
            "INSERT INTO cs_outbox (tenant_id, cs_request_id, payload_json) VALUES (@T, @Id, '{}')",
            new { T = Tenant, Id = id }));
        Assert.Equal(1062, ex.Number); // Duplicate entry — uq_cs_outbox_req 가 실제로 막았다
    }

    [Fact(DisplayName = "G-CS-7 🔴 답 멱등 — 같은 답 2번 수신 = 1건 · 상태 「답변도착」 · 깨진 응답은 버리지 않는다")]
    public async Task G_CS_7_답멱등()
    {
        if (!Ready("G-CS-7")) return;
        await using var db = await OpenAsync();
        Assert.IsType<OkObjectResult>(await PostAsync(Controller(db), null));
        var reqId = await db.ExecuteScalarAsync<string>("SELECT cs_request_id FROM cs_requests");

        var pullBody = "{\"replies\":[{\"replyId\":\"r-1\",\"csRequestId\":\"" + reqId
                     + "\",\"body\":\"확인했습니다. 설정 화면에서 다시 시도해 주세요.\",\"repliedByKind\":\"hq\",\"repliedAt\":\"2026-10-08T01:00:00Z\"}]}";
        var hq = new FakeHq((0, "")) { PullResult = (200, pullBody) };

        await CsOutboxSenderWorker.PullRepliesAsync(db, hq, Owner, NullLogger.Instance, default);
        await CsOutboxSenderWorker.PullRepliesAsync(db, hq, Owner, NullLogger.Instance, default); // 같은 답 한 번 더

        Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cs_replies"));
        Assert.Equal("답변도착", await db.ExecuteScalarAsync<string>("SELECT status FROM cs_requests"));

        // 깨진 응답 — 예외 없이 넘기고(다음 주기 재시도) 기존 행은 그대로.
        hq.PullResult = (200, "{깨진 json");
        await CsOutboxSenderWorker.PullRepliesAsync(db, hq, Owner, NullLogger.Instance, default);
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cs_replies"));
    }

    // ── 가짜 본사 — 응답을 각본대로 주고 호출 수를 센다 ─────────────────
    private sealed class FakeHq : IHeadquartersClient
    {
        private readonly (int, string) _postResult;
        public int PostCalls { get; private set; }
        public string? LastOwner { get; private set; }
        public (int, string) PullResult { get; set; } = (404, "");

        public FakeHq((int, string) postResult) => _postResult = postResult;

        public Task<(int StatusCode, string Body)> PostAsync(string path, string dataJson, string ownerAccountId, CancellationToken ct)
        {
            PostCalls++;
            LastOwner = ownerAccountId;
            return Task.FromResult(_postResult);
        }

        public Task<(int StatusCode, string Body)> PullAsync(string path, string ownerAccountId, CancellationToken ct)
            => Task.FromResult(PullResult);

        public Task<(int StatusCode, string Body)> PostOwnerReportAsync(string ownerAccountId, CancellationToken ct)
            => Task.FromResult((200, "{\"success\":true}")); // 이 게이트의 과녁 아님 — 워커 사이클이 직접 호출하지 않는다
    }
}
