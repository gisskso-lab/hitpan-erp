using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
/// 🔴 작15 E-1 게이트 — 「받은답」 만족도 평가 왕복을 **동작으로** 잰다
/// (작업지시서 20261008작15 §E · 설계 20261008_E1_CS만족도_보내기_설계문서.md §9).
///
/// <para>격리 DB 는 **출하 DDL(installer/hitpan_db_clean.sql) 실물 import** 로 세운다
/// (CsMessagePipelineGateTests 선례 그대로) ⇒ <c>cs_rating_outbox</c> 가 출하 DDL 에 진짜
/// 들어갔는지(#36)가 모든 시험의 선결 과녁이 된다 — 빠졌으면 전부 즉사한다(DB-126 선례).</para>
///
/// <para>무엇을 재나:
/// G-E1-0 출하 DDL 에 표가 있고 INSERT 가 통과 + UTC 폴링에 바로 집힌다.
/// G-E1-1 열림 조건 — 전송 전·답 0건은 400 + 행 0 / 답이 오면 같은 요청이 200 + 행 1(음·양 한 시험 안에).
/// G-E1-2 유실 0 — 연결 실패에도 행은 남고, 복구되면 자동 송신 · **주소가 ratings 다** · 재전송 0.
/// G-E1-3 400 = 종결 — rejected · 재시도 0 · 행 삭제 0 · 점수 보존.
/// G-E1-4 재시도 축 — 500 **그리고 429** 각각 attempt 증가 · terminal 없음(429 를 종결하면 평가 영구 소실).
/// G-E1-5 멱등 — 같은 쪽지 2번 평가 = 행 1 · 첫 점수 불변 · UNIQUE 가 서버에서 실제로 막는다.
/// G-E1-6 조용한 소실 계측 — 2xx + duplicated 면 last_error 에 흔적 · 양성 대조군 = 평범한 2xx 는 흔적 0.
/// G-E1-7 문①·문② — 한 줄 평 식별번호 모양 거부(우회 경로 포함) · 양성 대조군 = 숫자만 뺀 같은 요청.
/// G-E1-8 몸통·봉투 — 키 집합이 **정확히 3개** · rating 은 JSON 숫자 · 봉투 오너 = 부모계정.
/// G-E1-9 3단 매핑 — 좋아요·보통·아쉬워요 = 3·2·1 (화면 표 실물 + 끝까지 가는 숫자 둘 다).</para>
///
/// <para>🔴 **한글 단언 금지 규율**(2026-10-08 실사고): <c>JsonSerializer</c> 는 한글을 <c>\uXXXX</c> 로
/// 쓴다 ⇒ 송신 몸통·응답에 대고 한글 글자 비교를 **쓰지 않는다**. 전부 파싱 후 **숫자·ASCII 키**로 본다.
/// (G-E1-9 의 화면 표 검사만 예외 — 그것은 JSON 이 아니라 레포 UTF-8 원문이고, 적중 건수 양성 단언을 함께 둔다.)</para>
///
/// <para>DB 없는 로컬 = 엄격판 <c>SkipOrFailStrict</c> 로 선언을 요구한다(조용한 초록 0).
/// CI <c>db-gate</c>(HITPAN_REQUIRE_DB)에서는 SKIP=FAIL ⇒ **PR 의 db-gate 잡이 유일한 계측 경로**다.
/// 운영 왕복(실제 ratings 200)은 운영 CS 표 미적용 500(P0) + 접속 금지로 **이 게이트 밖** — [4] 몫으로 남긴다.</para>
/// </summary>
public sealed class CsRatingPipelineGateTests : IDisposable
{
    /// <summary>
    /// 🔴 로컬 실측 경로 — 이 PC 의 DB 계정은 <c>CREATE DATABASE</c> 를 거부한다(실측 1044 · GRANT 가 4개 DB 이름 한정).
    /// 그대로 두면 10건이 전부 SKIP 이고, 「봉합 빼면 FAIL」 대조를 **한 건도 못 돌린다**(누적 26회 지적 자리).
    /// <para>그래서 선례 <c>HITPAN_BO_GATE_DB</c>(20261007작12 · <see cref="DbGateEnvironment.UndeclaredMessage"/> 가
    /// 직접 안내하는 방식)와 **같은 모양**으로 「이미 있는 시험 DB」를 지정해 돌릴 수 있게 한다.
    /// 🚫 CI <c>db-gate</c> 는 이 변수를 주지 않는다 — CI 경로는 종전 그대로 격리 DB 생성이다.
    /// 🔴 지정한 DB 는 출하 DDL 실물 import 로 **덮인다**(신규설치 모사가 G-E1-0 의 과녁이라 피할 수 없다).
    /// 쓸 수 있는 것은 버려도 되는 시험 DB 뿐이다.</para>
    /// </summary>
    private static string? FixedDb() => Environment.GetEnvironmentVariable("HITPAN_CS_RATING_GATE_DB");

    private readonly string _dbName = FixedDb() is { Length: > 0 } fixedDb
        ? fixedDb
        : $"hitpan_gate_rate_{Guid.NewGuid():N}";
    private bool _created;

    // ── 격리 DB (CsMessagePipelineGateTests 선례 그대로 — 출하 DDL 실물 import) ──

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
                    // 지정 DB 모드면 생성 권한을 묻지 않는다 — 붙기만 하면 된다.
                    if (FixedDb() is { Length: > 0 })
                        c.Execute($"USE `{_dbName}`");
                    else
                    {
                        c.Execute($"CREATE DATABASE IF NOT EXISTS `{_dbName}`");
                        c.Execute($"DROP DATABASE IF EXISTS `{_dbName}`");
                    }
                }
                catch (MySqlException ex)
                {
                    Console.Error.WriteLine($"[{gate}] 서버 접속·권한 없음: {ex.Message}");
                    ok = false;
                }
            }
            // 🔴 엄격판 — 선언 없는 건너뛰기는 실패로 만든다. DB 없이 1초 초록은 SKIP 의 다른 얼굴이다.
            if (!ok) return !DbGateEnvironment.SkipOrFailStrict(gate);
        }
        SetUpFreshInstall();
        return true;
    }

    private void SetUpFreshInstall()
    {
        var ddlPath = Path.Combine(RepoRoot(), "installer", "hitpan_db_clean.sql");
        if (FixedDb() is not { Length: > 0 })
        {
            using var admin = new MySqlConnection(ServerConnString());
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{_dbName}`; "
                        + $"CREATE DATABASE `{_dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
            _created = true;   // 내가 만든 DB 만 내가 지운다 — 지정 DB 는 뒷정리에서 손대지 않는다
        }

        var psi = new System.Diagnostics.ProcessStartInfo(MysqlExe())
        {
            RedirectStandardInput = true, RedirectStandardError = true, UseShellExecute = false,
            // 🔴 출하 DDL 은 한글 기본값(status DEFAULT)과 한글 COMMENT 를 담고 있다.
            //    Windows 에서 표준입력 기본 인코딩은 콘솔 ANSI(CP949)라서, --default-character-set=utf8mb4
            //    로 말해 놓고 CP949 바이트를 밀어 넣어 1067 Invalid default value 로 터진다(로컬 실측으로 확인).
            //    CI(ubuntu)는 기본이 UTF-8 이라 드러나지 않던 자리다 — 양쪽 다 UTF-8 로 못 박는다.
            StandardInputEncoding = new UTF8Encoding(false),
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
            Console.Error.WriteLine($"[CS평가게이트] 격리 DB 뒷정리 실패: {ex.Message}");
        }
    }

    // ── 재료 ─────────────────────────────────────────────────────────

    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string Owner = "boss@gate.test";
    private const string RatingsPath = "/api/backoffice/cs/ratings";

    // 문①·문② 과녁 — 사업자번호 **모양**의 가짜 숫자. 진짜 값이 아니라 모양 검사 과녁이다.
    private const string DirtyComment = "사업자 000-00-00000 등록이 안 돼요";
    private const string CleanComment = "사업자 등록이 안 돼요";

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

    private static Task<IActionResult> RateAsync(MySqlConnection db, string id, int score, string? comment = null)
        => Controller(db).Rate(id, new CsRequestController.RateCsRequest { Rating = score, Comment = comment }, default);

    /// <summary>쪽지 한 건을 세운다. <paramref name="sent"/>=true 면 본사에 티켓이 생긴 상태(= cs_outbox.sent_at).</summary>
    private static async Task SeedRequestAsync(MySqlConnection db, string id, bool sent, string? terminal = null, int replies = 0)
    {
        await db.ExecuteAsync(@"
            INSERT INTO cs_requests (cs_request_id, tenant_id, created_by, category, sub_tag, body, erp_version)
            VALUES (@Id, @T, 'child-user-1', 'how_to', 'etc', 'gate-seed', '0');
            INSERT INTO cs_outbox (tenant_id, cs_request_id, payload_json, next_attempt_at, sent_at, terminal_reason)
            VALUES (@T, @Id, '{}', UTC_TIMESTAMP(6), @Sent, @Terminal);",
            new { Id = id, T = Tenant, Sent = sent ? (DateTime?)DateTime.UtcNow : null, Terminal = terminal });

        for (var i = 0; i < replies; i++) await AddReplyAsync(db, id, $"{id}-reply-{i}");
    }

    private static Task AddReplyAsync(MySqlConnection db, string requestId, string replyId)
        => db.ExecuteAsync(@"
            INSERT INTO cs_replies (reply_id, tenant_id, cs_request_id, body, replied_by_kind, replied_at)
            VALUES (@R, @T, @Id, 'gate-reply', 'hq', UTC_TIMESTAMP(6))",
            new { R = replyId, T = Tenant, Id = requestId });

    /// <summary>평가가 **열린** 쪽지 — 전송됨(티켓 있다) + 답 1건.</summary>
    private static Task SeedOpenAsync(MySqlConnection db, string id) => SeedRequestAsync(db, id, sent: true, replies: 1);

    /// <summary>시간 여행 — 백오프로 미래에 걸린 평가 행을 지금 집히게 당긴다(시험 전용).</summary>
    private static Task RewindAsync(MySqlConnection db) =>
        db.ExecuteAsync("UPDATE cs_rating_outbox SET next_attempt_at = DATE_SUB(UTC_TIMESTAMP(6), INTERVAL 1 SECOND)");

    private static Task RunCycleAsync(MySqlConnection db, FakeHq hq) =>
        CsOutboxSenderWorker.SendPendingRatingsAsync(db, hq, Owner, NullLogger.Instance, default);

    private static Task<int> RowsAsync(MySqlConnection db, string id) =>
        db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cs_rating_outbox WHERE cs_request_id = @Id", new { Id = id });

    private static Task<int> RejectsAsync(MySqlConnection db, string rule) =>
        db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cs_forbidden_rejects WHERE rule_code = @R", new { R = rule });

    /// <summary>응답 몸통을 ASCII 키로만 읽는다 — 한글 글자 비교는 쓰지 않는다.</summary>
    private static JsonElement Body(IActionResult res)
        => JsonSerializer.SerializeToElement(Assert.IsAssignableFrom<ObjectResult>(res).Value);

    private static string? Reason(IActionResult res)
        => Body(res).TryGetProperty("reason", out var r) ? r.GetString() : null;

    private static bool AlreadyRated(IActionResult res)
        => Body(res).TryGetProperty("alreadyRated", out var a) && a.ValueKind == JsonValueKind.True;

    private sealed class RateRow
    {
        public int? Rating { get; set; }
        public string? Comment { get; set; }
        public DateTime? SentAt { get; set; }
        public int AttemptCount { get; set; }
        public string? TerminalReason { get; set; }
        public string? LastError { get; set; }
    }

    private static Task<RateRow> RowAsync(MySqlConnection db, string id)
        => db.QueryFirstAsync<RateRow>(@"
            SELECT `rating` AS Rating, `comment` AS Comment, sent_at AS SentAt,
                   attempt_count AS AttemptCount, terminal_reason AS TerminalReason, last_error AS LastError
              FROM cs_rating_outbox WHERE cs_request_id = @Id", new { Id = id });

    // ── 게이트 ───────────────────────────────────────────────────────

    [Fact(DisplayName = "G-E1-0 🔴 출하 DDL(#36) — 신규 설치 DB 에 cs_rating_outbox 가 있고 INSERT 가 통과 · UTC 폴링에 바로 집힌다")]
    public async Task G_E1_0_출하DDL_표존재()
    {
        if (!Ready("G-E1-0")) return;
        await using var db = await OpenAsync();

        var exists = await db.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM information_schema.tables
             WHERE table_schema = DATABASE() AND table_name = 'cs_rating_outbox'");
        Assert.True(exists == 1, "출하 DDL 에 cs_rating_outbox 가 없다 — 신규 설치 고객만 평가가 터진다(#36 · DB-126 선례)");

        await SeedOpenAsync(db, "q-0");
        var inserted = await db.ExecuteAsync(@"
            INSERT INTO cs_rating_outbox (tenant_id, cs_request_id, `rating`, `comment`, next_attempt_at)
            VALUES (@T, 'q-0', 3, 'gate', UTC_TIMESTAMP(6))", new { T = Tenant });
        Assert.Equal(1, inserted);

        // 🔴 UTC 명시 검증 — 방금 넣은 행이 **지금** 집혀야 한다(로컬시각 기본값이면 KST 에서 0건).
        var pickable = await db.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM cs_rating_outbox
             WHERE sent_at IS NULL AND terminal_reason IS NULL AND next_attempt_at <= UTC_TIMESTAMP(6)");
        Assert.True(pickable == 1, $"방금 넣은 평가가 폴링에 안 집힌다(집힘 {pickable}건) — next_attempt_at 시간대 자리");
    }

    [Fact(DisplayName = "G-E1-1 🔴 열림 조건(결재 Q-1) — 전송 전·답 0건은 400 + 행 0 / 답이 1건 오면 같은 요청이 200 + 행 1")]
    public async Task G_E1_1_열림조건_음성과양성()
    {
        if (!Ready("G-E1-1")) return;
        await using var db = await OpenAsync();
        await SeedRequestAsync(db, "q-1", sent: false);

        // 음성 ① — 아직 본사에 안 갔다. 보내면 본사가 200 duplicated 로 **조용히 버린다** ⇒ 여기서 막는다.
        var notSent = await RateAsync(db, "q-1", 3, "fine");
        Assert.IsType<BadRequestObjectResult>(notSent);
        Assert.Equal("not_sent", Reason(notSent));
        Assert.Equal(0, await RowsAsync(db, "q-1"));

        // 음성 ② — 전달은 됐지만 답이 0건. 결재 Q-1 = 답이 와야 열린다.
        await db.ExecuteAsync("UPDATE cs_outbox SET sent_at = UTC_TIMESTAMP(6) WHERE cs_request_id = 'q-1'");
        var noReply = await RateAsync(db, "q-1", 3, "fine");
        Assert.IsType<BadRequestObjectResult>(noReply);
        Assert.Equal("no_reply", Reason(noReply));
        Assert.Equal(0, await RowsAsync(db, "q-1"));

        // 음성 ③ — 종결(거부)된 쪽지. 본사에 티켓이 없다.
        await SeedRequestAsync(db, "q-1r", sent: false, terminal: "rejected", replies: 1);
        var rejected = await RateAsync(db, "q-1r", 3, "fine");
        Assert.IsType<BadRequestObjectResult>(rejected);
        Assert.Equal("rejected", Reason(rejected));
        Assert.Equal(0, await RowsAsync(db, "q-1r"));

        // 🔴 양성 — 똑같은 요청인데 답이 1건 오면 열린다. 「원래 안 되는 것」이 아님을 증명한다.
        await AddReplyAsync(db, "q-1", "q-1-reply-0");
        var open = await RateAsync(db, "q-1", 3, "fine");
        Assert.IsType<OkObjectResult>(open);
        Assert.Equal(1, await RowsAsync(db, "q-1"));
    }

    [Fact(DisplayName = "G-E1-2 🔴 유실 0 — 연결 실패에도 행은 남고, 복구되면 자동 송신 · 주소가 ratings 다 · 재전송 0")]
    public async Task G_E1_2_유실0_주소()
    {
        if (!Ready("G-E1-2")) return;
        await using var db = await OpenAsync();
        await SeedOpenAsync(db, "q-2");
        Assert.IsType<OkObjectResult>(await RateAsync(db, "q-2", 3, "fine"));

        // 터널 끊김 — 행은 남는다. 고객이 쓴 글은 삭제되지 않는다.
        await RunCycleAsync(db, new FakeHq((0, "connect-fail")));
        var down = await RowAsync(db, "q-2");
        Assert.Null(down.SentAt);
        Assert.Equal(1, down.AttemptCount);
        Assert.Null(down.TerminalReason);
        Assert.Equal(1, await RowsAsync(db, "q-2"));

        // 복구 — 자동 송신. 🔴 주소·오너 양성 단언(주소를 messages 로 바꾸면 여기서 FAIL).
        await RewindAsync(db);
        var alive = new FakeHq((200, "{\"success\":true}"));
        await RunCycleAsync(db, alive);
        Assert.Equal(1, alive.PostCalls);
        Assert.Equal(RatingsPath, alive.LastPath);
        Assert.Equal(Owner, alive.LastOwner);
        Assert.NotNull((await RowAsync(db, "q-2")).SentAt);

        // 송신 뒤 다시 돌려도 재전송 0 (sent_at 이 멱등의 자리)
        await RunCycleAsync(db, alive);
        Assert.Equal(1, alive.PostCalls);
    }

    [Fact(DisplayName = "G-E1-3 🔴 400 = 종결 — rejected · 재시도 0 · 행 삭제 0 · 점수 보존")]
    public async Task G_E1_3_400_종결()
    {
        if (!Ready("G-E1-3")) return;
        await using var db = await OpenAsync();
        await SeedOpenAsync(db, "q-3");
        Assert.IsType<OkObjectResult>(await RateAsync(db, "q-3", 3, "fine"));

        var hq = new FakeHq((400, "{\"error\":\"forbidden_field\"}"));
        await RunCycleAsync(db, hq);

        var row = await RowAsync(db, "q-3");
        Assert.Equal("rejected", row.TerminalReason);
        Assert.Null(row.SentAt);
        Assert.Equal(3, row.Rating);                      // 고객이 누른 점수는 그대로 남는다
        Assert.Equal(1, await RowsAsync(db, "q-3"));

        // 종결 뒤에는 집지 않는다 — 재시도가 돌면 FAIL.
        await RewindAsync(db);
        await RunCycleAsync(db, hq);
        Assert.Equal(1, hq.PostCalls);
        Assert.Equal(1, await RowsAsync(db, "q-3"));
    }

    [Fact(DisplayName = "G-E1-4 🔴 재시도 축 — 500 **그리고 429** 각각 attempt 증가 · terminal 없음(429 종결 = 평가 영구 소실)")]
    public async Task G_E1_4_재시도_500과429()
    {
        if (!Ready("G-E1-4")) return;
        await using var db = await OpenAsync();

        // 축 ① 5xx
        await SeedOpenAsync(db, "q-4a");
        Assert.IsType<OkObjectResult>(await RateAsync(db, "q-4a", 2, "fine"));
        var h5 = new FakeHq((500, "server-error"));
        await RunCycleAsync(db, h5);
        var a = await RowAsync(db, "q-4a");
        Assert.Equal(1, a.AttemptCount);
        Assert.Null(a.TerminalReason);
        Assert.Null(a.SentAt);

        // 축 ② 429 — 본사 60분 잠금. 🔴 이것을 rejected 로 분류하면 그 창의 평가가 **영구 소실**된다.
        //    q-4a 는 위 백오프로 미래에 걸려 이번 사이클엔 안 집힌다 ⇒ 429 축만 깨끗하게 잰다.
        await SeedOpenAsync(db, "q-4b");
        Assert.IsType<OkObjectResult>(await RateAsync(db, "q-4b", 1, "fine"));
        var h429 = new FakeHq((429, "locked"));
        await RunCycleAsync(db, h429);
        Assert.Equal(1, h429.PostCalls);
        var b = await RowAsync(db, "q-4b");
        Assert.Equal(1, b.AttemptCount);
        Assert.Null(b.TerminalReason);
        Assert.Null(b.SentAt);

        // 행 삭제 0 — 두 건 다 살아 있다.
        Assert.Equal(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cs_rating_outbox"));
    }

    [Fact(DisplayName = "G-E1-5 🔴 멱등(결재 Q-2 수정 불가) — 같은 쪽지 2번 평가 = 행 1 · 첫 점수 불변 · UNIQUE 가 서버에서 막는다")]
    public async Task G_E1_5_멱등_첫평가만()
    {
        if (!Ready("G-E1-5")) return;
        await using var db = await OpenAsync();
        await SeedOpenAsync(db, "q-5");

        Assert.IsType<OkObjectResult>(await RateAsync(db, "q-5", 3, "first"));

        // 두 번째 — 점수를 바꿔 다시 눌러도 받지 않는다. 사고가 아니라 멱등이다(200 + alreadyRated).
        var second = await RateAsync(db, "q-5", 1, "second");
        Assert.IsType<OkObjectResult>(second);
        Assert.True(AlreadyRated(second), "두 번째 평가가 alreadyRated 로 안 돌아온다 — 수정이 열렸다(결재 Q-2 위반)");

        Assert.Equal(1, await RowsAsync(db, "q-5"));
        var row = await RowAsync(db, "q-5");
        Assert.Equal(3, row.Rating);                       // 3 → 1 로 바뀌면 FAIL
        Assert.Equal("first", row.Comment);

        // UNIQUE 가 **서버에서** 막는다 — API 를 우회해도 2번째 행이 안 들어간다(DDL 에서 빼면 여기서 FAIL).
        var ex = await Assert.ThrowsAsync<MySqlException>(() => db.ExecuteAsync(
            "INSERT INTO cs_rating_outbox (tenant_id, cs_request_id, `rating`) VALUES (@T, 'q-5', 1)",
            new { T = Tenant }));
        Assert.Equal(1062, ex.Number);
    }

    [Fact(DisplayName = "G-E1-6 🔴 조용한 소실 계측 — 2xx + duplicated 면 last_error 에 흔적 · 양성 대조군 = 평범한 2xx 는 흔적 0")]
    public async Task G_E1_6_duplicated_흔적()
    {
        if (!Ready("G-E1-6")) return;
        await using var db = await OpenAsync();

        // 본사에 티켓이 없을 때의 응답 — 200 인데 아무것도 저장되지 않았다(CsInboundController.cs:231).
        await SeedOpenAsync(db, "q-6");
        Assert.IsType<OkObjectResult>(await RateAsync(db, "q-6", 3, "fine"));
        await RunCycleAsync(db, new FakeHq((200, "{\"success\":true,\"duplicated\":true}")));
        var dup = await RowAsync(db, "q-6");
        Assert.NotNull(dup.SentAt);
        Assert.Equal("duplicated", dup.LastError);         // ASCII 양성 단언 — 읽는 줄을 빼면 null 로 FAIL

        // 🔴 양성 대조군 — 평범한 2xx 에는 흔적을 남기지 않는다(늘 duplicated 를 적는 게 아니라는 증거).
        await SeedOpenAsync(db, "q-6b");
        Assert.IsType<OkObjectResult>(await RateAsync(db, "q-6b", 2, "fine"));
        await RunCycleAsync(db, new FakeHq((200, "{\"success\":true}")));
        var plain = await RowAsync(db, "q-6b");
        Assert.NotNull(plain.SentAt);
        Assert.Null(plain.LastError);
    }

    [Fact(DisplayName = "G-E1-7 🔴 문①·문② — 한 줄 평 식별번호 모양 거부(큐 우회 포함) · 양성 대조군 = 숫자만 뺀 같은 요청")]
    public async Task G_E1_7_금지필드_양쪽문()
    {
        if (!Ready("G-E1-7")) return;
        await using var db = await OpenAsync();

        // ── 문② 먼저 — API 를 우회해 큐에 직접 넣어도 송신 직전에 잡힌다.
        //    기존 ScanPayloadBody 는 payload.body 만 보므로 평가는 그 그물에 안 걸린다 ⇒ 이 자리가 그 보완이다.
        await SeedOpenAsync(db, "q-7b");
        await db.ExecuteAsync(@"
            INSERT INTO cs_rating_outbox (tenant_id, cs_request_id, `rating`, `comment`, next_attempt_at)
            VALUES (@T, 'q-7b', 3, @C, UTC_TIMESTAMP(6))", new { T = Tenant, C = DirtyComment });

        var hq = new FakeHq((200, "{\"success\":true}"));
        await RunCycleAsync(db, hq);
        Assert.Equal(0, hq.PostCalls);                     // 본사로 한 발도 안 나갔다
        Assert.Equal("rejected", (await RowAsync(db, "q-7b")).TerminalReason);
        Assert.Equal(1, await RejectsAsync(db, "biz_no"));

        // ── 문① — 저장 전 거부. 행 0 + 거부 기록 1건 더.
        await SeedOpenAsync(db, "q-7");
        var bad = await RateAsync(db, "q-7", 3, DirtyComment);
        Assert.IsType<BadRequestObjectResult>(bad);
        Assert.Equal("forbidden_field", Reason(bad));
        Assert.Equal(0, await RowsAsync(db, "q-7"));
        Assert.Equal(2, await RejectsAsync(db, "biz_no"));

        // 🔴 양성 대조군 — 같은 문장에서 그 숫자만 뺐다. 거부가 「원래 안 되는 것」이 아님을 증명.
        var good = await RateAsync(db, "q-7", 3, CleanComment);
        Assert.IsType<OkObjectResult>(good);
        Assert.Equal(1, await RowsAsync(db, "q-7"));
        Assert.Equal(2, await RejectsAsync(db, "biz_no"));
    }

    [Fact(DisplayName = "G-E1-8 🔴 몸통·봉투 — 키 집합이 정확히 {csRequestId, rating, comment} · rating 은 JSON 숫자 · 오너 = 부모계정")]
    public async Task G_E1_8_몸통_3키()
    {
        if (!Ready("G-E1-8")) return;
        await using var db = await OpenAsync();
        await SeedOpenAsync(db, "q-8");
        Assert.IsType<OkObjectResult>(await RateAsync(db, "q-8", 2, "fine"));

        var hq = new FakeHq((200, "{\"success\":true}"));
        await RunCycleAsync(db, hq);
        Assert.Equal(1, hq.PostCalls);
        Assert.Equal(Owner, hq.LastOwner);

        using var doc = JsonDocument.Parse(Assert.IsType<string>(hq.LastBody));
        var el = doc.RootElement;
        Assert.Equal(JsonValueKind.Object, el.ValueKind);

        // 🔴 키가 하나라도 더 있으면 본사가 **통째 400** 으로 거부한다(화이트리스트 3키).
        var keys = el.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "comment", "csRequestId", "rating" }, keys);

        Assert.Equal(JsonValueKind.Number, el.GetProperty("rating").ValueKind);  // 글자 "2" 로 가면 본사 400
        Assert.Equal(2, el.GetProperty("rating").GetInt32());
        Assert.Equal("q-8", el.GetProperty("csRequestId").GetString());
        Assert.Equal("fine", el.GetProperty("comment").GetString());
    }

    [Fact(DisplayName = "G-E1-9 🔴 3단 매핑(결재 Q-3) — 좋아요·보통·아쉬워요 = 3·2·1 · 화면 표 실물 + 끝까지 가는 숫자")]
    public async Task G_E1_9_3단_매핑()
    {
        // ── 축 ① 화면 표 실물 — 고객이 누르는 글자와 숫자의 짝. 레포 UTF-8 원문이라 JSON 이스케이프 축이 아니다.
        //    (적중 3건 양성 단언을 함께 둔다 — 못 찾으면 그 자체로 FAIL 이다)
        var razor = Path.Combine(RepoRoot(), "src", "HitPan.Web", "Pages", "Support", "CsInboxPage.razor");
        var text = File.ReadAllText(razor, Encoding.UTF8);
        var pairs = Regex.Matches(text, @"new\(\s*(\d)\s*,\s*""([^""]+)""\s*\)")
                         .Select(m => (Score: int.Parse(m.Groups[1].Value), Label: m.Groups[2].Value))
                         .ToList();
        Assert.True(pairs.Count == 3, $"화면 3단 표를 못 읽었다(찾음 {pairs.Count}건) — 좋아요·보통·아쉬워요 짝이 있어야 한다");
        Assert.Equal(3, pairs.Single(p => p.Label == "좋아요").Score);
        Assert.Equal(2, pairs.Single(p => p.Label == "보통").Score);
        Assert.Equal(1, pairs.Single(p => p.Label == "아쉬워요").Score);

        // ── 축 ② 그 숫자가 **끝까지** 그대로 간다 — DB 와 송신 몸통 둘 다 숫자로 본다(한글 비교 0).
        if (!Ready("G-E1-9")) return;
        await using var db = await OpenAsync();

        var cases = new[] { ("q-9a", 3), ("q-9b", 2), ("q-9c", 1) };
        foreach (var (id, score) in cases)
        {
            await SeedOpenAsync(db, id);
            Assert.IsType<OkObjectResult>(await RateAsync(db, id, score, "fine"));
            Assert.Equal(score, (await RowAsync(db, id)).Rating);
        }

        var hq = new FakeHq((200, "{\"success\":true}"));
        await RunCycleAsync(db, hq);
        Assert.Equal(3, hq.PostCalls);

        var sent = hq.Bodies.Select(b =>
        {
            using var d = JsonDocument.Parse(b);
            return (Id: d.RootElement.GetProperty("csRequestId").GetString()!,
                    Score: d.RootElement.GetProperty("rating").GetInt32());
        }).ToDictionary(x => x.Id, x => x.Score);

        foreach (var (id, score) in cases)
            Assert.Equal(score, sent[id]);
    }

    // ── 가짜 본사 — 응답을 각본대로 주고 **주소·몸통·오너**를 적는다 ─────
    //   기존 CsMessagePipelineGateTests 의 FakeHq 는 path·body 를 안 적는다 ⇒ 그 파일은 무접촉하고 제 것을 둔다(#1).
    private sealed class FakeHq : IHeadquartersClient
    {
        private readonly (int, string) _postResult;
        public int PostCalls { get; private set; }
        public string? LastPath { get; private set; }
        public string? LastBody { get; private set; }
        public string? LastOwner { get; private set; }
        public List<string> Bodies { get; } = new();
        public (int, string) PullResult { get; set; } = (404, "");

        public FakeHq((int, string) postResult) => _postResult = postResult;

        public Task<(int StatusCode, string Body)> PostAsync(string path, string dataJson, string ownerAccountId, CancellationToken ct)
        {
            PostCalls++;
            LastPath = path;
            LastBody = dataJson;
            LastOwner = ownerAccountId;
            Bodies.Add(dataJson);
            return Task.FromResult(_postResult);
        }

        public Task<(int StatusCode, string Body)> PullAsync(string path, string ownerAccountId, CancellationToken ct)
            => Task.FromResult(PullResult);

        public Task<(int StatusCode, string Body)> PostOwnerReportAsync(string ownerAccountId, CancellationToken ct)
            => Task.FromResult((200, "{\"success\":true}")); // 이 게이트의 과녁 아님 — 평가 사이클이 호출하지 않는다
    }
}
