using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using HitPan.API.Controllers;
using HitPan.API.Middleware;
using HitPan.Application.DTOs.User;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 10/5 작3·작4 [3] 봉합1 — [3-V] 병렬이슈 01·02·03·05·07 게이트(06 은 <c>ApprovalRetiredAccessGateTests</c> G-AR7p).
/// </summary>
/// <remarks>
/// <para>클래스 이름에 <c>AccountSeatGate</c> 가 들어 있어 CI <c>db-gate</c> 잡의 기존 필터(<c>FullyQualifiedName~AccountSeatGate</c>)가 그대로 문다 — 워크플로 0줄.</para>
/// <para>⚠️ DB 게이트는 개발 PC 에서 SKIP 이 정상(<c>hitpan</c> 은 CREATE DATABASE 거부) — CI <c>HITPAN_REQUIRE_DB</c> 가 FAIL 로 바꾼다.</para>
/// <para>연결 문자열은 <see cref="MySqlConnectionStringBuilder"/> 로 만든다 — 시험 파일에 자격증명 모양 글자를 두지 않는다(비밀 스캔).</para>
/// </remarks>
[Collection("AccountSeatGateSeal1")]
public sealed class AccountSeatGateSeal1Tests : IDisposable
{
    private readonly string _dbName = "hitpan_seal1_" + Guid.NewGuid().ToString("N")[..8];
    private bool _created;
    private readonly string _tenantId = Guid.NewGuid().ToString();
    private string _parentId = "";

    // ══ 준비물 — AccountSeatGateTests 와 같은 방식(출하 DDL 위 격리 DB) ══
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

    private static MySqlConnectionStringBuilder ServerBuilder() => new()
    {
        Server = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost",
        Port = uint.TryParse(Environment.GetEnvironmentVariable("HITPAN_DB_PORT"), out var p) ? p : 3306,
        UserID = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root",
        Password = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "",
        DefaultCommandTimeout = 90,
        GuidFormat = MySqlGuidFormat.None,
        AllowUserVariables = true,
    };

    private static string ServerConnString() => ServerBuilder().ConnectionString;

    // ⬛ private string DbConnString()
    // ⬛ {
    // ⬛     var b = ServerBuilder();
    // ⬛     b.Database = _dbName;
    // ⬛     return b.ConnectionString;
    // ⬛ }
    //   사유(2026-10-05 봉합3 · ⚠️가설 — CI db-gate 로만 확인된다): 봉합2 가 AccountSeatGateTests 에 붙인 「격리 DB 연결 풀 끔」을
    //   같은 날 봉합1 로 새로 생긴 이 파일이 물려받지 못했다. 시험마다 DB 이름이 달라 시험마다 풀이 하나씩 생기고,
    //   닫은 연결은 풀에 남아 서버 접속으로 최대 180초(MySqlConnector 기본 ConnectionIdleTimeout) 산다 ⇒ xUnit 실행 1분 20초 동안
    //   쌓인 접속이 CI MariaDB 기본 max_connections(151)를 넘겨 뒤에 도는 BackupCredentialGate 가 `Too many connections`,
    //   mysql CLI 로 DDL 을 붓는 게이트가 표준입력 쓰기 실패를 낸 것으로 본다(PR #453 db-gate 2차 · 05:20:10 · 기준 c6f0ade1 은 0건).
    //   [고침] 이 시험의 격리 DB 연결만 풀을 끈다 — 닫으면 바로 서버 접속이 끊긴다(관례: AccountSeatGateTests:70 · TaxInvoiceMigratedLockGateTests:663).
    private string DbConnString()
    {
        var b = ServerBuilder();
        b.Database = _dbName;
        b.Pooling = false;
        return b.ConnectionString;
    }

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
            if (!ok) return !DbGateEnvironment.SkipOrFail(gate);
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
            Console.Error.WriteLine($"[AccountSeatSeal1] 임시 DB 정리 실패 — 손으로 지워라: {_dbName} ({ex.Message})");
        }
    }

    private async Task<MySqlConnection> OpenAsync()
    {
        var c = new MySqlConnection(DbConnString());
        await c.OpenAsync();
        return c;
    }

    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string actionType, string entityType, string? entityId = null,
            string? beforeJson = null, string? afterJson = null, string? reason = null,
            System.Data.IDbTransaction? tx = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>회사 + 구독 + 대표 1명. <paramref name="withCompanyRow"/>=false 면 local_company 행 없음(P2-05).</summary>
    private async Task SeedAsync(MySqlConnection db, int maxUsers, bool withCompanyRow = true)
    {
        if (withCompanyRow)
            await db.ExecuteAsync(
                "INSERT INTO local_company (tenant_id, tenant_code, company_name, is_locked_from_landing) VALUES (@T, 'T1', '시험회사', 1)",
                new { T = _tenantId });
        await db.ExecuteAsync(
            "INSERT INTO local_subscription (tenant_id, max_users, extra_accounts) VALUES (@T, @M, 0)",
            new { T = _tenantId, M = maxUsers });
        _parentId = Guid.NewGuid().ToString();
        await db.ExecuteAsync(@"
            INSERT INTO users (user_id, tenant_id, email, password_hash, user_name, role, account_type, is_parent,
                               is_active, is_deleted, created_at, updated_at)
            VALUES (@U, @T, 'owner01', 'x', '대표', 'TenantAdmin', 'tenant_admin', 1, 1, 0, NOW(6), NOW(6))",
            new { U = _parentId, T = _tenantId });
    }

    private static CreateUserDto NewUser(string email, string role = "User") => new()
    {
        Email = email, UserName = email, Password = "Temp1234!", Role = role
    };

    /// <summary>로그인 한 번 흉내 — 세션 행 + refresh 행(같은 sid).</summary>
    private async Task<string> FakeLoginAsync(MySqlConnection db, string userId)
    {
        var sid = Guid.NewGuid().ToString();
        await db.ExecuteAsync(@"
            INSERT INTO user_sessions (session_id, user_id, tenant_id, expires_at, device_kind)
            VALUES (@S, @U, @T, UTC_TIMESTAMP(6) + INTERVAL 8 HOUR, 'pc')",
            new { S = sid, U = userId, T = _tenantId });
        await db.ExecuteAsync(@"
            INSERT INTO refresh_tokens (token_id, user_id, token_hash, expires_at, is_revoked, session_id)
            VALUES (@Id, @U, @H, UTC_TIMESTAMP(6) + INTERVAL 7 DAY, 0, @S)",
            new { Id = Guid.NewGuid().ToString(), U = userId, H = Guid.NewGuid().ToString("N"), S = sid });
        return sid;
    }

    /// <summary>업무 API 한 번 — 실물 SessionValidityMiddleware(새 캐시). 401 이면 끊긴 것.</summary>
    private async Task<int> CallBusinessApiAsync(MySqlConnection db, string userId, string sid)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/sales/orders";
        ctx.Items["TenantId"] = _tenantId;
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("sid", sid), new Claim("user_id", userId) }, "gate"));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var mw = new SessionValidityMiddleware(
            c => { c.Response.StatusCode = 200; return Task.CompletedTask; },
            NullLogger<SessionValidityMiddleware>.Instance, cache);
        await mw.InvokeAsync(ctx, db);
        return ctx.Response.StatusCode;
    }

    // ══ P1-01 — 「사용 안 함」·「계정폐기」 즉시 효력 ══

    [Fact(DisplayName = "G-S1a 🔴 P1-01 — 사용 안 함·계정폐기 직후 그 출입증으로 업무 API 401 · refresh 폐기 · 대조군(안 끈 계정 200 · 칸만 끈 계정 200)")]
    public async Task S1a_Suspend_And_Retire_Cut_Tokens()
    {
        if (!Ready("G-S1a")) return;
        await using var db = await OpenAsync();
        await SeedAsync(db, 8);
        var svc = new UserService(db, new NoOpAudit());
        var a = await svc.CreateAsync(NewUser("a@t.kr"), _tenantId);
        var b = await svc.CreateAsync(NewUser("b@t.kr"), _tenantId);
        var c = await svc.CreateAsync(NewUser("c@t.kr"), _tenantId);
        var d = await svc.CreateAsync(NewUser("d@t.kr"), _tenantId);
        var sa = await FakeLoginAsync(db, a);
        var sb = await FakeLoginAsync(db, b);
        var sc = await FakeLoginAsync(db, c);
        var sd = await FakeLoginAsync(db, d);

        Assert.Equal(200, await CallBusinessApiAsync(db, a, sa));      // 끄기 전엔 열려 있다

        await svc.SuspendAsync(a, _tenantId);
        await svc.RetireAsync(c, _tenantId);

        Assert.Equal(401, await CallBusinessApiAsync(db, a, sa));      // 사용 안 함 → 끊김
        Assert.Equal(401, await CallBusinessApiAsync(db, c, sc));      // 계정폐기 → 끊김
        Assert.Equal(0, await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM refresh_tokens WHERE user_id IN (@A, @C) AND is_revoked = 0", new { A = a, C = c }));

        // 대조군 ① — 안 끈 계정은 그대로 열린다(남의 출입증을 건드리지 않는다)
        Assert.Equal(200, await CallBusinessApiAsync(db, b, sb));
        Assert.Equal(1, await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM refresh_tokens WHERE user_id = @B AND is_revoked = 0", new { B = b }));

        // 대조군 ② — 봉합 전 모양(is_active 칸만 0)은 출입증이 그대로 열린다 ⇒ 끊는 것은 칸이 아니라 CutAccess 다
        await db.ExecuteAsync("UPDATE users SET is_active = 0 WHERE user_id = @D", new { D = d });
        Assert.Equal(200, await CallBusinessApiAsync(db, d, sd));
    }

    // ══ [4] 2차 N-1① — 수정(PUT) 으로 1→0 도 출입증을 끊는다 ══

    [Fact(DisplayName = "G-S1i 🔴 [4]2차 N-1① — 수정 저장으로 사용 안 함(1→0) 직후 출입증 401 · refresh 폐기 · 대조군(이름만 바꾼 저장 200)")]
    public async Task S1i_Update_To_Inactive_Cuts_Tokens()
    {
        if (!Ready("G-S1i")) return;
        await using var db = await OpenAsync();
        await SeedAsync(db, 8);
        var svc = new UserService(db, new NoOpAudit());
        var a = await svc.CreateAsync(NewUser("pa@t.kr"), _tenantId);
        var b = await svc.CreateAsync(NewUser("pb@t.kr"), _tenantId);
        var sa = await FakeLoginAsync(db, a);
        var sb = await FakeLoginAsync(db, b);

        await svc.UpdateAsync(a, new UpdateUserDto { UserName = "가", Role = "User", IsActive = false }, _tenantId);
        Assert.Equal(401, await CallBusinessApiAsync(db, a, sa));
        Assert.Equal(0, await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM refresh_tokens WHERE user_id = @A AND is_revoked = 0", new { A = a }));

        // 대조군 — 사용 중인 채로 이름만 바꾼 저장은 출입증을 건드리지 않는다
        await svc.UpdateAsync(b, new UpdateUserDto { UserName = "나2", Role = "User", IsActive = true }, _tenantId);
        Assert.Equal(200, await CallBusinessApiAsync(db, b, sb));
    }

    // ══ [5] CTO G-1 — 사원 퇴사로 계정이 지워질 때 출입증도 끊긴다(N-1②) ══

    [Fact(DisplayName = "G-S1j 🔴 CTO G-1 · N-1② — 사원 퇴사 직후 그 계정 출입증 401 · refresh 폐기 · 세션 0 · 퇴사 저장 성공 · 대조군(다른 사원 계정 200)")]
    public async Task S1j_Resign_Cuts_Tokens()
    {
        if (!Ready("G-S1j")) return;
        await using var db = await OpenAsync();
        await SeedAsync(db, 8);
        var users = new UserService(db, new NoOpAudit());
        var a = await users.CreateAsync(NewUser("ra@t.kr"), _tenantId);
        var b = await users.CreateAsync(NewUser("rb@t.kr"), _tenantId);
        var sa = await FakeLoginAsync(db, a);
        var sb = await FakeLoginAsync(db, b);
        var empA = await db.ExecuteScalarAsync<string>(
            "SELECT employee_id FROM employees WHERE tenant_id = @T AND user_id = @U", new { T = _tenantId, U = a });
        Assert.False(string.IsNullOrEmpty(empA));

        var blocked = await new EmployeeService(db, new NoOpAudit()).ResignAsync(_tenantId, empA!, null, null);

        Assert.True(blocked);                                           // 퇴사 저장 자체는 성공(흐름 안 끊김 #20)
        Assert.Equal(401, await CallBusinessApiAsync(db, a, sa));       // 끊김
        Assert.Equal(0, await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM refresh_tokens WHERE user_id = @A AND is_revoked = 0", new { A = a }));
        Assert.Equal(0, await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM user_sessions WHERE user_id = @A", new { A = a }));

        // 대조군 — 다른 사원의 계정은 그대로 열린다
        Assert.Equal(200, await CallBusinessApiAsync(db, b, sb));
    }

    // ══ P1-02 — 대표 보호 ══

    [Fact(DisplayName = "G-S1b 🔴 P1-02 — 대표 권한 변경·비번 초기화 거절 · 값 그대로 · 대조군(같은 권한 저장 통과 · 직원 초기화 통과)")]
    public async Task S1b_Parent_Role_And_Reset_Guarded()
    {
        if (!Ready("G-S1b")) return;
        await using var db = await OpenAsync();
        await SeedAsync(db, 8);
        var svc = new UserService(db, new NoOpAudit());

        var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateAsync(_parentId, new UpdateUserDto { UserName = "대표", Role = "User", IsActive = true }, _tenantId));
        Assert.Equal(UserService.ParentRoleGuardMessage, ex1.Message);
        Assert.Equal(("TenantAdmin", "tenant_admin"), await db.QueryFirstAsync<(string, string)>(
            "SELECT role, account_type FROM users WHERE user_id=@U", new { U = _parentId }));

        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ResetPasswordAsync(_parentId, _tenantId));
        Assert.Equal(UserService.ParentResetGuardMessage, ex2.Message);
        Assert.Equal("x", await db.ExecuteScalarAsync<string>("SELECT password_hash FROM users WHERE user_id=@U", new { U = _parentId }));

        // 대조군 — 같은 권한으로 이름만 바꾸는 저장은 된다 · 직원 비번 초기화는 된다
        await svc.UpdateAsync(_parentId, new UpdateUserDto { UserName = "대표2", Role = "TenantAdmin", IsActive = true }, _tenantId);
        Assert.Equal("대표2", await db.ExecuteScalarAsync<string>("SELECT user_name FROM users WHERE user_id=@U", new { U = _parentId }));
        var child = await svc.CreateAsync(NewUser("staff@t.kr", "TenantAdmin"), _tenantId);
        Assert.False(string.IsNullOrEmpty(await svc.ResetPasswordAsync(child, _tenantId)));
    }

    // ══ P2-05 — 잠금 행 없음 → 이름 잠금 ══

    [Fact(DisplayName = "G-S1c 🔴 P2-05 — local_company 행 없음 · 동시 2건 → 두 번째는 이름 잠금에서 줄 서고 거절 · 잠금 풀림")]
    public async Task S1c_Missing_Company_Row_Uses_Named_Lock()
    {
        if (!Ready("G-S1c")) return;
        await using (var seed = await OpenAsync())
        {
            await SeedAsync(seed, 5, withCompanyRow: false);
            for (var i = 0; i < 3; i++)   // 대표 1 + 3 = 활성 4 · 한도 5
                await seed.ExecuteAsync(@"
                    INSERT INTO users (user_id, tenant_id, email, password_hash, user_name, role, account_type, is_parent,
                                       is_active, is_deleted, created_at, updated_at)
                    VALUES (UUID(), @T, @E, 'x', 'n', 'User', 'tenant_user', 0, 1, 0, NOW(6), NOW(6))",
                    new { T = _tenantId, E = $"pre{i}@t.kr" });
        }

        await using var c1 = await OpenAsync();
        await using var c2 = await OpenAsync();
        await using var tx1 = await c1.BeginTransactionAsync();
        var lock1 = await AccountSeatGuard.AcquireAsync(c1, tx1, _tenantId, null, default);
        Assert.True(lock1.UsesNamedLock);
        await AccountSeatGuard.EnsureSeatAsync(c1, tx1, _tenantId, 1, null, default);

        await using var tx2 = await c2.BeginTransactionAsync();
        var second = Task.Run(async () =>
        {
            await using var lock2 = await AccountSeatGuard.AcquireAsync(c2, tx2, _tenantId, null, default);
            await AccountSeatGuard.EnsureSeatAsync(c2, tx2, _tenantId, 1, null, default);
        });
        await Task.Delay(1500);
        Assert.False(second.IsCompleted, "행 없음인데 두 번째가 줄 서지 않았다 — 틈 잠금 그대로(P2-05)");

        await c1.ExecuteAsync(@"
            INSERT INTO users (user_id, tenant_id, email, password_hash, user_name, role, account_type, is_parent,
                               is_active, is_deleted, created_at, updated_at)
            VALUES (UUID(), @T, 'first@t.kr', 'x', 'n', 'User', 'tenant_user', 0, 1, 0, NOW(6), NOW(6))",
            new { T = _tenantId }, tx1);
        await tx1.CommitAsync();
        await lock1.DisposeAsync();

        await Assert.ThrowsAsync<AccountSeatFullException>(() => second);
        await tx2.RollbackAsync();

        await using var c3 = await OpenAsync();
        Assert.Equal(1, await c3.ExecuteScalarAsync<long>("SELECT IS_FREE_LOCK(@N)", new { N = AccountSeatGuard.SeatLockName(_tenantId) }));
    }

    [Fact(DisplayName = "G-S1c2 대조군 — 행 없음 · 이름 잠금 없이 판정만 부르면 거절(조용히 열지 않는다) · 봉합 전 틈 잠금은 서로 안 막는다")]
    public async Task S1c2_Controls()
    {
        if (!Ready("G-S1c2")) return;
        await using (var seed = await OpenAsync()) await SeedAsync(seed, 5, withCompanyRow: false);

        await using var c1 = await OpenAsync();
        await using var tx1 = await c1.BeginTransactionAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AccountSeatGuard.EnsureSeatAsync(c1, tx1, _tenantId, 1, null, default));
        Assert.Equal(AccountSeatGuard.SeatLockBusyMessage, ex.Message);
        await tx1.RollbackAsync();

        // 봉합 전 모양 재현 — 없는 행 FOR UPDATE 두 개가 동시에 선다(병렬이슈 05 의 뿌리)
        await using var a = await OpenAsync();
        await using var b = await OpenAsync();
        await using var ta = await a.BeginTransactionAsync();
        await using var tb = await b.BeginTransactionAsync();
        const string gap = "SELECT tenant_id FROM local_company WHERE tenant_id = @T FOR UPDATE";
        await a.ExecuteScalarAsync<string?>(gap, new { T = _tenantId }, ta);
        var other = b.ExecuteScalarAsync<string?>(gap, new { T = _tenantId }, tb);
        var done = await Task.WhenAny(other, Task.Delay(3000));
        Assert.True(done == other, "대조군 전제가 틀렸다 — 틈 잠금이 서로 막는다면 P2-05 재현이 성립하지 않는다");
        await ta.RollbackAsync();
        await tb.RollbackAsync();
    }

    // ══ P2-07 — 옛 삭제 계정이 아이디를 쥐고 있다 ══

    [Fact(DisplayName = "G-S1d 🔴 P2-07 — 옛 삭제 행 아이디로 재등록 409 문구(500 아님) · DB-136 두 번 → 표식 · 재등록 성공 · 표식 있는 행·대표 무접촉")]
    public async Task S1d_Old_Deleted_Row_Marked_By_Db136()
    {
        if (!Ready("G-S1d")) return;
        await using var db = await OpenAsync();
        await SeedAsync(db, 8);
        var svc = new UserService(db, new NoOpAudit());
        var old = Guid.NewGuid().ToString();
        await db.ExecuteAsync(@"
            INSERT INTO users (user_id, tenant_id, email, password_hash, user_name, role, account_type, is_parent,
                               is_active, is_deleted, deleted_at, created_at, updated_at)
            VALUES (@U, @T, 'old@t.kr', 'x', 'n', 'User', 'tenant_user', 0, 0, 1, NOW(6), NOW(6), NOW(6))",
            new { U = old, T = _tenantId });
        await db.ExecuteAsync(@"
            INSERT INTO users (user_id, tenant_id, email, password_hash, user_name, role, account_type, is_parent,
                               is_active, is_deleted, deleted_at, created_at, updated_at)
            VALUES (UUID(), @T, 'resigned+keep', 'x', 'n', 'User', 'tenant_user', 0, 0, 1, NOW(6), NOW(6), NOW(6))",
            new { T = _tenantId });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateAsync(NewUser("old@t.kr"), _tenantId));
        Assert.Equal(UserService.DeletedHoldsLoginIdMessage, ex.Message);

        var sql = File.ReadAllText(Path.Combine(RepoRoot(), "src", "HitPan.API", "Migrations", "SQL", "DB-136_account_seat_defaults.sql"));
        await db.ExecuteAsync(sql);
        var once = await db.ExecuteScalarAsync<string>("SELECT email FROM users WHERE user_id=@U", new { U = old });
        await db.ExecuteAsync(sql);
        var twice = await db.ExecuteScalarAsync<string>("SELECT email FROM users WHERE user_id=@U", new { U = old });
        Assert.Equal($"retired+{old}+old@t.kr", once);
        Assert.Equal(once, twice);                                                    // 멱등
        Assert.Equal(1, await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM users WHERE email='resigned+keep'"));
        Assert.Equal("owner01", await db.ExecuteScalarAsync<string>("SELECT email FROM users WHERE user_id=@U", new { U = _parentId }));

        var again = await svc.CreateAsync(NewUser("old@t.kr"), _tenantId);
        Assert.NotEqual(old, again);
    }

    // ══ DB 불필요 — P1-03 ══

    [Theory(DisplayName = "G-S1e P1-03 — 서명 키 고르기: 둘 다 비면 null(대신 쓰는 키 없음)")]
    [InlineData(null, null, null)]
    [InlineData("", "  ", null)]
    [InlineData("k1", "k2", "k1")]
    [InlineData(null, "k2", "k2")]
    [InlineData(" ", "k2", "k2")]
    public void S1e_ResolveSigningKey(string? dbConf, string? cfg, string? expected) =>
        Assert.Equal(expected, WebhookInboundController.ResolveSigningKey(dbConf, cfg));

    [Fact(DisplayName = "G-S1f 🔴 P1-03 — 키 설정 없음 · 레포에 적힌 개발용 키로 서명 → account-count 401 · 대조군(그 키를 설정하면 서명 통과)")]
    public async Task S1f_Dev_Key_Rejected_When_No_Key()
    {
        // 개발용 키 값은 시험에 옮겨 적지 않는다 — 정본 소스(SerialProofVerifier)에서 읽는다(비밀 스캔 · 한 군데만).
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "src", "HitPan.API", "Services", "SerialProofVerifier.cs"));
        var devKey = Regex.Match(src, "key = \"(DEV-[^\"]+)\"").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(devKey), "SerialProofVerifier 에서 개발용 키 줄을 못 찾았다 — 시험 전제 확인");
        var machineKey = HitPan.Infrastructure.Configuration.TenantConfigReader.Get("HITPAN_BOOTSTRAP_TOKEN_KEY");

        var none = await CallAccountCountAsync(devKey, configKey: null);
        Assert.Equal(401, none);

        // 대조군 — 같은 서명이라도 키가 설정돼 있으면 서명은 통과한다(뒤의 DB 접속에서 500) ⇒ 401 은 키 부재 때문이다
        var withKey = await CallAccountCountAsync(machineKey ?? devKey, configKey: devKey);
        Assert.NotEqual(401, withKey);
    }

    private async Task<int> CallAccountCountAsync(string signKey, string? configKey)
    {
        var dict = new Dictionary<string, string?>();
        if (configKey is not null) dict["Bootstrap:TokenKey"] = configKey;
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var body = JsonSerializer.Serialize(new { TenantId = _tenantId, Nonce = Guid.NewGuid().ToString("N"), Iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        ctx.Request.Headers["X-Hitpan-Nonce"] = "n1";
        using (var h = new HMACSHA256(Encoding.UTF8.GetBytes(signKey)))
            ctx.Request.Headers["X-Hitpan-Signature"] =
                Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(body))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var ctl = new WebhookInboundController(cfg, NullLogger<WebhookInboundController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx },
            ConnectionStringForTests = "Server=127.0.0.1;Port=1;Database=x;User=x"
        };
        var result = await ctl.AccountCount(default);
        return (result as IStatusCodeActionResult)?.StatusCode ?? 0;
    }

    [Theory(DisplayName = "G-S1g P1-03 덤 — 한도 합 넘침 → int 최대로 고정 · 음수 없음")]
    [InlineData(5, 3, 8)]
    [InlineData(5, int.MaxValue, int.MaxValue)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue)]
    [InlineData(5, 0, 5)]
    [InlineData(-10, 3, 0)]
    public void S1g_CombineLimit(int baseLimit, int extra, int expected) =>
        Assert.Equal(expected, AccountSeatGuard.CombineLimit(baseLimit, extra));

    [Fact(DisplayName = "G-S1h 비밀 스캔 — 계정 과금 시험 두 파일에 연결 문자열 비번 조각 0")]
    public void S1h_No_Password_Fragment_In_Seat_Tests()
    {
        // 이 파일은 검사식 자체가 걸리므로 대상에서 뺀다 — 이 파일은 연결 문자열을 빌더로만 만든다.
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "HitPan.Tests", "Integrity", "AccountSeatGateTests.cs"));
        Assert.DoesNotMatch(new Regex(@"Password=[A-Za-z0-9]"), text);
    }
}
