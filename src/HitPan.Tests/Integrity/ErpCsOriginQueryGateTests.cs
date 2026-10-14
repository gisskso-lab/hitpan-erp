using Dapper;
using HitPan.API.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261014작1 — <b>쪽 가르기 질의칸 게이트</b> (G-W9-1 ~ G-W9-5 · G-W9-15).
///
/// <para><b>무엇을 지키나</b>: 사장님 오더(10/14) = <b>공지사항 ↔ CS쪽지 분리</b>.
/// 화면만 둘로 갈라도 쿼리가 하나면 본사 공지가 쌓일 때 <b>고객이 쓴 문의가 LIMIT 밖으로
/// 밀려 사라진다</b>(선행검증 A-1). 그래서 서버가 쪽을 가르고 <b>쪽마다 자기 몫을 센다.</b></para>
///
/// <para>🔴 <b>글자가 아니라 동작을 잰다</b> — 실물 <see cref="CsRequestController"/> 를 실물 DB 에
/// 붙여 호출한다. 각 게이트의 음성 대조군은 작업지시서 §7 표에 1:1 로 적혀 있다.</para>
///
/// <para>로컬 실행 = <c>HITPAN_W9_ERP_DB=test_w9_erp HITPAN_REQUIRE_DB=true</c>
/// (개발 계정은 <c>test_</c> 접두 이름이면 CREATE 가 된다 — 10/12 실측).
/// 🔴 선언 없는 건너뛰기는 실패다(<see cref="DbGateEnvironment.SkipOrFailStrict"/>).</para>
/// </summary>
public sealed class ErpCsOriginQueryGateTests : IDisposable
{
    private readonly ErpCsGateDb _db = new("HITPAN_W9_ERP_DB", "w9origin");

    // ══════════════════════════════════════════════════════════════
    // G-W9-1 — ?origin=hq 는 **본사 것만** 준다
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W9-1 🔴 ?origin=hq 가 본사 안내만 준다 — 고객이 쓴 문의가 0건 섞인다")]
    public async Task G_W9_1_공지쪽만_온다()
    {
        const string gate = "G-W9-1 공지 쪽만";
        if (!await _db.ReadyAsync(gate)) return;

        await using var erp = await _db.OpenAsync();
        await ErpCsGateDb.SeedUsersAsync(erp);
        await ErpCsGateDb.InsertAsync(erp, "hq-1", "hq", "본사 공지 하나", minutesAgo: 10);
        await ErpCsGateDb.InsertAsync(erp, "hq-2", "hq", "본사 공지 둘", minutesAgo: 9);
        await ErpCsGateDb.InsertAsync(erp, "me-1", "local", "저장 단추가 안 보입니다", minutesAgo: 8);

        var rows = await ErpCsGateDb.ListAsync(erp, "hq");

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("hq", ErpCsGateDb.Origin(r)));
        Assert.DoesNotContain(rows, r => ErpCsGateDb.Id(r) == "me-1");
    }

    // ══════════════════════════════════════════════════════════════
    // G-W9-2 — ?origin=local 은 **내 문의만** 준다
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W9-2 🔴 ?origin=local 이 내 문의만 준다 — 본사 안내가 0건 섞인다")]
    public async Task G_W9_2_내쪽만_온다()
    {
        const string gate = "G-W9-2 내 쪽만";
        if (!await _db.ReadyAsync(gate)) return;

        await using var erp = await _db.OpenAsync();
        await ErpCsGateDb.SeedUsersAsync(erp);
        await ErpCsGateDb.InsertAsync(erp, "hq-1", "hq", "본사 공지", minutesAgo: 10);
        await ErpCsGateDb.InsertAsync(erp, "me-1", "local", "문의 하나", minutesAgo: 9);
        await ErpCsGateDb.InsertAsync(erp, "me-2", "local", "문의 둘", minutesAgo: 8);

        var rows = await ErpCsGateDb.ListAsync(erp, "local");

        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, r => ErpCsGateDb.Origin(r) == "hq");
    }

    // ══════════════════════════════════════════════════════════════
    // G-W9-3 — 질의칸 **없는** 호출은 종전 그대로 (회귀 0 · #1 추가만)
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W9-3 🔴 origin 없는 호출은 종전처럼 전체를 준다 (옛 화면·옛 게이트 회귀 0)")]
    public async Task G_W9_3_질의칸_없으면_전체()
    {
        const string gate = "G-W9-3 전체 회귀";
        if (!await _db.ReadyAsync(gate)) return;

        await using var erp = await _db.OpenAsync();
        await ErpCsGateDb.SeedUsersAsync(erp);
        await ErpCsGateDb.InsertAsync(erp, "hq-1", "hq", "본사 공지", minutesAgo: 10);
        await ErpCsGateDb.InsertAsync(erp, "me-1", "local", "문의", minutesAgo: 9);

        var all = await ErpCsGateDb.ListAsync(erp, null);
        Assert.Equal(2, all.Count);

        // 🔴 낯선 값에 화면을 비우지 않는다 — 전체로 떨어진다(설계 §3).
        var weird = await ErpCsGateDb.ListAsync(erp, "zzz");
        Assert.Equal(2, weird.Count);
    }

    // ══════════════════════════════════════════════════════════════
    // G-W9-4 — 🔴 공지가 LIMIT 를 채워도 **내 문의가 산다** (A-1 의 본체)
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W9-4 🔴 공지 201건이 쌓여도 내 문의가 목록에서 사라지지 않는다 (쪽별 LIMIT)")]
    public async Task G_W9_4_공지가_쌓여도_내문의가_산다()
    {
        const string gate = "G-W9-4 쪽별 LIMIT";
        if (!await _db.ReadyAsync(gate)) return;

        await using var erp = await _db.OpenAsync();
        await ErpCsGateDb.SeedUsersAsync(erp);

        // 내 문의가 **가장 오래된** 행이다 — 단일 LIMIT 200 이면 제일 먼저 밀려난다.
        await ErpCsGateDb.InsertAsync(erp, "me-1", "local", "저장 단추가 안 보입니다", minutesAgo: 5000);
        for (var i = 0; i < 201; i++)
            await ErpCsGateDb.InsertAsync(erp, $"hq-{i:D3}", "hq", $"본사 공지 {i}", minutesAgo: 400 - i);

        // ── 음성 축(이 게이트 안에서 바로 보인다): 쪽을 안 가르면 내 문의가 **없다**
        var mixed = await ErpCsGateDb.ListAsync(erp, null);
        Assert.Equal(200, mixed.Count);
        Assert.DoesNotContain(mixed, r => ErpCsGateDb.Id(r) == "me-1");

        // ── 양성 축: 쪽을 가르면 **산다**
        var mine = await ErpCsGateDb.ListAsync(erp, "local");
        Assert.Contains(mine, r => ErpCsGateDb.Id(r) == "me-1");

        // 공지 쪽도 자기 몫 200 을 센다(한 쪽이 다른 쪽 몫을 빼앗지 않는다)
        var notices = await ErpCsGateDb.ListAsync(erp, "hq");
        Assert.Equal(200, notices.Count);
        Assert.All(notices, r => Assert.Equal("hq", ErpCsGateDb.Origin(r)));
    }

    // ══════════════════════════════════════════════════════════════
    // G-W9-5 — 테넌트 격리 (#2) — 쪽 가르기가 격리를 뚫지 않는다
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W9-5 🔴 남의 고객사 행은 origin=hq 로도 안 넘어온다 (#2 토큰 클레임만)")]
    public async Task G_W9_5_남의_테넌트는_안_온다()
    {
        const string gate = "G-W9-5 테넌트 격리";
        if (!await _db.ReadyAsync(gate)) return;

        await using var erp = await _db.OpenAsync();
        await ErpCsGateDb.SeedUsersAsync(erp);
        await ErpCsGateDb.InsertAsync(erp, "hq-mine", "hq", "우리 공지", minutesAgo: 10);
        await ErpCsGateDb.InsertAsync(erp, "hq-other", "hq", "남의 공지", minutesAgo: 9,
            tenantId: ErpCsGateDb.OtherTenantId);

        foreach (var side in new string?[] { "hq", "local", null })
        {
            var rows = await ErpCsGateDb.ListAsync(erp, side);
            Assert.DoesNotContain(rows, r => ErpCsGateDb.Id(r) == "hq-other");
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-W9-15 — 🔴 칸이 **없는** DB 에서 공지 쪽은 **빈 결과**다 (구현 보강분)
    //   근거: 본사 선발신을 앉히는 INSERT 가 `origin` 을 명시한다 ⇒ 칸이 없으면 그 행은
    //   애초에 못 들어온다 ⇒ 본사 행은 원리적으로 0건. 여기서 「전체」를 주면 공지 화면에
    //   **내 문의가 섞여** 10/12 반려가 그대로 되살아난다.
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W9-15 🔴 출처 칸이 없는 DB — 공지 쪽은 빈 결과, 내 쪽은 종전처럼 전부 (1054 도 안 난다)")]
    public async Task G_W9_15_칸이_없으면_공지는_빈결과()
    {
        const string gate = "G-W9-15 칸 없는 DB";
        if (!await _db.ReadyAsync(gate)) return;

        await using var erp = await _db.OpenAsync();
        await ErpCsGateDb.SeedUsersAsync(erp);
        await ErpCsGateDb.InsertAsync(erp, "me-1", "local", "문의 하나", minutesAgo: 10);
        await ErpCsGateDb.InsertAsync(erp, "me-2", "local", "문의 둘", minutesAgo: 9);

        // 업데이트 직후 마이그 전 1회 상태를 만든다
        await erp.ExecuteAsync("ALTER TABLE cs_requests DROP COLUMN origin");
        CsRequestController.ResetOriginColumnCache();

        // 공지 쪽 = 빈 결과 (예외가 아니라 0건이다 — WHERE 에 없는 칸을 달면 1054 로 죽는다)
        var notices = await ErpCsGateDb.ListAsync(erp, "hq");
        Assert.Empty(notices);

        // 내 쪽·전체 = 종전처럼 전부 (화면이 비지 않는다 — 설계 §8 보장)
        Assert.Equal(2, (await ErpCsGateDb.ListAsync(erp, "local")).Count);
        Assert.Equal(2, (await ErpCsGateDb.ListAsync(erp, null)).Count);
    }

    // ══════════════════════════════════════════════════════════════
    // G-W9-17 — 🔴 안읽음이 **읽음과 같은 축**이다 ([4] 조건 C-2)
    //   종전 화면 판정은 `status='답변도착'` 이었는데 읽음 표시는 `cs_replies.read_at` 만 바꾼다.
    //   status 를 내리는 코드가 레포에 0건이라 **읽은 뒤에도 칩이 영원히 남았다**(거짓 알림).
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W9-17 🔴 안읽음 수가 읽으면 줄어든다 — 읽음 표시와 같은 축이다 (status 는 안 내려간다)")]
    public async Task G_W9_17_안읽음은_읽으면_줄어든다()
    {
        const string gate = "G-W9-17 안읽음 축";
        if (!await _db.ReadyAsync(gate)) return;

        await using var erp = await _db.OpenAsync();
        await ErpCsGateDb.SeedUsersAsync(erp);
        await ErpCsGateDb.InsertAsync(erp, "hq-1", "hq", "본사 공지", minutesAgo: 10);
        await ErpCsGateDb.AddReplyAsync(erp, "r-1", "hq-1");

        // 읽기 전 — 안읽음 1
        var before = await ErpCsGateDb.ListAsync(erp, "hq");
        Assert.Equal(1, ErpCsGateDb.Cell<int>(before[0], "unreadCount"));

        // 실물 읽음 표시 경로를 지난다
        await ErpCsGateDb.NewController(erp).MarkRead("r-1", default);

        var after = await ErpCsGateDb.ListAsync(erp, "hq");
        Assert.Equal(0, ErpCsGateDb.Cell<int>(after[0], "unreadCount"));

        // 🔴 그리고 `status` 는 **그대로**다 — 그래서 status 로 칩을 세우면 영원히 남는다(이 게이트의 이유)
        Assert.Equal("답변도착", ErpCsGateDb.Cell<string>(after[0], "status"));
    }

    public void Dispose() => _db.Dispose();
}

/// <summary>
/// 🔴 20261014작1 — G-W9 묶음 공용 ERP DB 장치. 20261012작3 <c>CsPipelineRoundTripGateTests</c> 의
/// 모양을 그대로 쓴다(출하 DDL import · <c>test_</c> 이름 강제 · CI 에서는 파일 존재 검사 안 함).
/// <para>🔴 <b>묶음마다 다른 DB 이름을 쓴다</b> — xUnit 이 클래스를 병렬로 돌리므로 한 DB 를 공유하면
/// 서로 DROP 한다(10/13 §6-6).</para>
/// </summary>
internal sealed class ErpCsGateDb : IDisposable
{
    public const string TenantId = "99999999-9999-9999-9999-999999999999";
    public const string OtherTenantId = "88888888-8888-8888-8888-888888888888";
    private const string UserId = "77777777-7777-7777-7777-777777777777";

    private readonly string _dbName;
    private bool _created;

    public ErpCsGateDb(string envKey, string role)
    {
        var fallback = $"hitpan_gate_{role}_{Guid.NewGuid():N}"[..40];
        var given = Environment.GetEnvironmentVariable(envKey);
        if (string.IsNullOrWhiteSpace(given)) { _dbName = fallback; return; }

        // 🔴 이 장치는 DB 를 DROP 한다 — 이름에 test·gate 가 없으면 지정을 거부한다.
        var ok = given.Contains("test", StringComparison.OrdinalIgnoreCase)
              || given.Contains("gate", StringComparison.OrdinalIgnoreCase);
        if (!ok)
        {
            Console.Error.WriteLine(
                $"[G-W9] {envKey}='{given}' 는 거부했다 — 이 게이트는 DB 를 DROP 한다. "
              + "이름에 test 나 gate 가 있어야 한다. 무작위 이름으로 돈다.");
            _dbName = fallback;
            return;
        }
        _dbName = given.Trim();
    }

    public async Task<bool> ReadyAsync(string gate)
    {
        try
        {
            await using var admin = new MySqlConnection(ServerConnString());
            await admin.OpenAsync();
            await admin.ExecuteAsync($"DROP DATABASE IF EXISTS `{_dbName}`; "
                + $"CREATE DATABASE `{_dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
            _created = true;
        }
        catch (MySqlException ex)
        {
            Console.Error.WriteLine($"[{gate}] DB 준비 실패({ex.Number}): {ex.Message}");   // #15
            return !DbGateEnvironment.SkipOrFailStrict(gate);
        }

        // 🔴 파일 존재 검사는 로컬에서만 — CI 는 PATH 상의 명령 이름(`mysql`)을 준다(10/11 사고).
        if (!DbGateEnvironment.IsCi && !File.Exists(MysqlExe()))
        {
            Console.Error.WriteLine($"[{gate}] mysql 클라이언트 없음: {MysqlExe()}");
            return !DbGateEnvironment.SkipOrFailStrict(gate);
        }

        ImportShippingDdl();
        // 🔴 판정 캐시는 **DB 이름**으로 걸린다 — 같은 이름이 다시 태어나면 옛 판정이 그대로 적용된다.
        CsRequestController.ResetOriginColumnCache();
        CsRequestController.ResetHqStatusColumnCache();
        // 🔴 20261014작3 [4] 조건 C-6 — 용도 칸(DB-146) 판정도 **여기서 비운다.**
        //   안 비우면: 앞 시험이 칸을 떼고 「없다」로 굳힌 판정이 **다음 시험까지 살아남아**,
        //   칸이 있는 DB 에서도 「없다」로 돌아 **종류 필터가 조용히 안 걸린 채 초록**이 된다.
        //   origin·hq_status 는 같은 이유로 이미 비우고 있었고, 새 칸만 빠져 있었다.
        CsRequestController.ResetPurposeColumnCache();
        return true;
    }

    public async Task<MySqlConnection> OpenAsync()
    {
        var c = new MySqlConnection(Conn());
        await c.OpenAsync();
        return c;
    }

    /// <summary>부모계정 1명 + 자식계정 1명 — 쪽지를 쓰는 쪽의 전제.</summary>
    public static async Task SeedUsersAsync(MySqlConnection erp) =>
        await erp.ExecuteAsync(@"
            INSERT IGNORE INTO users (user_id, tenant_id, email, password_hash, user_name, role, account_type, is_parent, is_active)
            VALUES (@Owner, @Tid, 'w9owner@gate.test', 'x', '대표', 'tenant_admin', 'tenant_admin', 1, 1),
                   (@Child, @Tid, 'w9child@gate.test', 'x', '직원', 'tenant_user', 'tenant_user', 0, 1)",
            new { Owner = "66666666-6666-6666-6666-666666666666", Child = UserId, Tid = TenantId });

    /// <summary>쪽지 한 줄을 앉힌다. <paramref name="origin"/> = 'hq'(본사 발신) · 'local'(고객이 쓴 것).</summary>
    public static async Task InsertAsync(MySqlConnection erp, string id, string origin, string body,
        int minutesAgo, string? tenantId = null) =>
        await erp.ExecuteAsync(@"
            INSERT INTO cs_requests
                (cs_request_id, tenant_id, created_by, category, sub_tag, body,
                 screen_code, erp_version, status, origin, created_at)
            VALUES
                (@Id, @Tid, @By, 'etc', 'etc', @Body,
                 NULL, @Ver, @Status, @Origin, DATE_SUB(UTC_TIMESTAMP(6), INTERVAL @Min MINUTE))",
            new
            {
                Id = id, Tid = tenantId ?? TenantId,
                By = origin == "hq" ? "headquarters" : UserId,
                Body = body, Ver = origin == "hq" ? "hq" : "1.3.57",
                Status = origin == "hq" ? "답변도착" : "접수",
                Origin = origin, Min = minutesAgo,
            });

    /// <summary>실물 컨트롤러를 불러 목록을 받는다 — 글자가 아니라 동작이다.</summary>
    public static async Task<List<object>> ListAsync(MySqlConnection erp, string? origin)
    {
        var res = await NewController(erp).List(origin, default);
        var ok = Assert.IsType<OkObjectResult>(res);
        var data = ok.Value!.GetType().GetProperty("data")!.GetValue(ok.Value!);
        return ((System.Collections.IEnumerable)data!).Cast<object>().ToList();
    }

    public static string Id(object row) => Cell<string>(row, "csRequestId");
    public static string? Origin(object row) => Cell<string?>(row, "origin");

    /// <summary>본사 발신의 답 한 줄(= 공지 본문). <c>read_at</c> 을 넣지 않아 **안 읽은 상태**로 앉는다.</summary>
    public static async Task AddReplyAsync(MySqlConnection erp, string replyId, string requestId) =>
        await erp.ExecuteAsync(@"
            INSERT INTO cs_replies (reply_id, tenant_id, cs_request_id, body, replied_by_kind, replied_at)
            VALUES (@Id, @Tid, @Req, '본문', '본사', UTC_TIMESTAMP(6))",
            new { Id = replyId, Tid = TenantId, Req = requestId });

    /// <summary>Dapper 의 동적 행에서 칸 하나를 꺼낸다.</summary>
    public static T Cell<T>(object row, string name)
    {
        var dict = (IDictionary<string, object>)row;
        return dict.TryGetValue(name, out var v) && v is not null ? (T)Convert.ChangeType(v, typeof(T)) : default!;
    }

    public static CsRequestController NewController(MySqlConnection erp)
    {
        var ctx = new DefaultHttpContext();
        ctx.Items["TenantId"] = TenantId;
        ctx.Items["UserId"] = UserId;
        return new CsRequestController(erp, NullLogger<CsRequestController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx },
        };
    }

    /// <summary>출하 DDL(= 신규설치와 같은 판)로 세운다 — 출하본에 그 칸이 있는지도 여기서 드러난다.</summary>
    private void ImportShippingDdl()
    {
        var ddl = Path.Combine(RepoRoot(), "installer", "hitpan_db_clean.sql");
        var psi = new System.Diagnostics.ProcessStartInfo(MysqlExe())
        {
            RedirectStandardInput = true, RedirectStandardError = true, UseShellExecute = false,
            // 🔴 이걸 안 적으면 stdin 이 콘솔 인코딩(이 PC 는 CP949)으로 나가 한글이 깨지고 1067 로 죽는다.
            StandardInputEncoding = new System.Text.UTF8Encoding(false),
        };
        psi.ArgumentList.Add($"--host={Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost"}");
        psi.ArgumentList.Add($"--port={Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306"}");
        psi.ArgumentList.Add($"-u{Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root"}");
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS");
        if (!string.IsNullOrEmpty(pass)) psi.ArgumentList.Add($"-p{pass}");
        psi.ArgumentList.Add("--default-character-set=utf8mb4");
        psi.ArgumentList.Add(_dbName);

        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.StandardInput.Write(File.ReadAllText(ddl));
        proc.StandardInput.Close();
        var err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        Assert.True(proc.ExitCode == 0, $"ERP 출하 DDL import 실패:\n{err}");
    }

    public static string RepoRoot()
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

    private string Conn() => ServerConnString().Replace("User=", $"Database={_dbName};User=") + "Pooling=false;";

    public void Dispose()
    {
        try
        {
            using var admin = new MySqlConnection(ServerConnString());
            admin.Open();
            if (_created) admin.Execute($"DROP DATABASE IF EXISTS `{_dbName}`");
        }
        catch (MySqlException ex)
        {
            Console.Error.WriteLine($"[G-W9] 시험 DB 정리 실패(무해): {ex.Message}");   // #15
        }
    }
}
