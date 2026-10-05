using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using HitPan.API.Controllers;
using HitPan.API.Services;
using HitPan.Application.DTOs.User;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using HitPan.Infrastructure.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-A1~A17</b> — 계정 과금 판정기가 <b>동작으로</b>막는가 (20261005작3 · 설계 §8 · 반증 F1·F2·F3·F6·P2).
/// </summary>
/// <remarks>
/// <para>격리 DB 에 <b>출하 DDL</b>(#36)을 한 방 넣고 <b>실물</b> <see cref="UserService"/>·<see cref="CompanyBootstrapProvisioner"/>·
/// <see cref="WebhookInboundController"/> 를 불러 <b>표를 읽는다.</b> 판정하는 SQL 은 이 파일에 없다(셈은 결과 확인용 COUNT 뿐).</para>
/// <para>⚠️ DB 게이트는 개발 PC 에서 SKIP 이 정상이다(<c>hitpan</c> 은 CREATE DATABASE 거부) — CI <c>db-gate</c>(<c>HITPAN_REQUIRE_DB</c>)가 유일한 계측 경로.
/// 운영 무접촉(#39) — 임시 DB(<c>hitpan_seat_*</c>)만 만들고 지운다.</para>
/// </remarks>
[Collection("AccountSeatGate")]
public sealed class AccountSeatGateTests : IDisposable
{
    private readonly string _dbName = "hitpan_seat_" + Guid.NewGuid().ToString("N")[..8];
    private bool _created;
    private readonly string _tenantId = Guid.NewGuid().ToString();
    private string _parentId = "";

    // ══ 준비물 — MainPcSealRegistryGateTests 와 같은 방식 ══

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

    // ⬛ private string DbConnString() => ServerConnString().Replace("User=", $"Database={_dbName};User=");
    //   사유(2026-10-05 봉합2 · ⚠️가설): 시험마다 DB 이름이 달라 **시험마다 연결 풀이 하나씩** 생긴다.
    //   닫은 연결은 풀에 남아 서버 쪽 접속으로 최대 180초(MySqlConnector 기본 ConnectionIdleTimeout) 살아 있다.
    //   22건이 별도 컬렉션으로 다른 DB 게이트와 나란히 돌면서 CI MariaDB 기본 max_connections(151)를 갉아
    //   뒤에 도는 BackupCredentialGate(직렬 컬렉션)가 `Too many connections`, 다른 게이트가 연결 시간초과를 낸 것으로 본다
    //   (draft PR #453 db-gate job 111616295679 · 기준 판 c6f0ade1 은 0건).
    //   [고침] 이 시험의 격리 DB 연결만 풀을 끈다 — 닫으면 바로 서버 접속이 끊긴다(관례: TaxInvoiceMigratedLockGateTests:663).
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
            Console.Error.WriteLine($"[AccountSeat] 임시 DB 정리 실패 — 손으로 지워라: {_dbName} ({ex.Message})");
        }
    }

    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string actionType, string entityType, string? entityId = null,
            string? beforeJson = null, string? afterJson = null, string? reason = null,
            System.Data.IDbTransaction? tx = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CountingLogger<T> : ILogger<T>
    {
        public int Warnings;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning && formatter(state, exception).Contains("[AccountSeat]")) Warnings++;
        }
    }

    private async Task<MySqlConnection> OpenAsync()
    {
        var c = new MySqlConnection(DbConnString());
        await c.OpenAsync();
        return c;
    }

    /// <summary>회사 1개 + 대표 1명(활성) + 구독 행(maxUsers · extra). 대표는 셈에 1로 들어간다.</summary>
    private async Task SeedCompanyAsync(MySqlConnection db, int? maxUsers, int extra = 0, bool withParent = true)
    {
        await db.ExecuteAsync(
            "INSERT INTO local_company (tenant_id, tenant_code, company_name, is_locked_from_landing) VALUES (@T, 'T1', '시험회사', 1)",
            new { T = _tenantId });
        if (maxUsers is not null)
            await db.ExecuteAsync(
                "INSERT INTO local_subscription (tenant_id, max_users, extra_accounts) VALUES (@T, @M, @E)",
                new { T = _tenantId, M = maxUsers, E = extra });
        if (!withParent) return;
        _parentId = Guid.NewGuid().ToString();
        await db.ExecuteAsync(@"
            INSERT INTO users (user_id, tenant_id, email, password_hash, user_name, role, account_type, is_parent,
                               is_active, is_deleted, created_at, updated_at)
            VALUES (@U, @T, 'owner01', 'x', '대표', 'TenantAdmin', 'tenant_admin', 1, 1, 0, NOW(6), NOW(6))",
            new { U = _parentId, T = _tenantId });
    }

    private static CreateUserDto NewUser(string email) => new()
    {
        Email = email, UserName = email, Password = "Temp1234!", Role = "User"
    };

    private async Task<long> ActiveAsync(MySqlConnection db) =>
        await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM users WHERE tenant_id=@T AND is_active=1 AND is_deleted=0", new { T = _tenantId });

    private async Task FillChildrenAsync(UserService svc, int n)
    {
        for (var i = 0; i < n; i++) await svc.CreateAsync(NewUser($"child{i:D2}@t.kr"), _tenantId);
    }

    // ══ DB 게이트 ══

    [Fact(DisplayName = "G-A1 🔴 한도 5·활성 5 → 계정 추가 거절 · users·employees 행 0 증가")]
    public async Task A1_Create_At_Full_Is_Rejected()
    {
        if (!Ready("G-A1")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db, 5);
        var svc = new UserService(db, new NoOpAudit());
        await FillChildrenAsync(svc, 4);

        var users = await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM users WHERE tenant_id=@T", new { T = _tenantId });
        var emps = await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM employees WHERE tenant_id=@T", new { T = _tenantId });

        var ex = await Assert.ThrowsAsync<AccountSeatFullException>(() => svc.CreateAsync(NewUser("over@t.kr"), _tenantId));
        Assert.Equal(5, ex.Limit);
        Assert.Equal(users, await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM users WHERE tenant_id=@T", new { T = _tenantId }));
        Assert.Equal(emps, await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM employees WHERE tenant_id=@T", new { T = _tenantId }));
    }

    [Fact(DisplayName = "G-A1b 🔴 반증 F1 — 본사가 max_users 3 을 보내도 5 까지 된다 · 6번째만 막힌다")]
    public async Task A1b_Floor_Five_When_Backoffice_Sends_Three()
    {
        if (!Ready("G-A1b")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db, 3);
        var log = new CountingLogger<UserService>();
        var svc = new UserService(db, new NoOpAudit(), log);

        await FillChildrenAsync(svc, 4);            // 대표 1 + 4 = 5 — 3 에서 막히면 FAIL
        Assert.Equal(5, await ActiveAsync(db));
        await Assert.ThrowsAsync<AccountSeatFullException>(() => svc.CreateAsync(NewUser("sixth@t.kr"), _tenantId));
        Assert.True(log.Warnings > 0, "바닥값으로 간 경우 LogWarning 이 있어야 한다");
        Assert.Equal(3, await db.ExecuteScalarAsync<int>("SELECT max_users FROM local_subscription WHERE tenant_id=@T", new { T = _tenantId }));
    }

    [Fact(DisplayName = "G-A2 🔴 남은 자리 2 · 엑셀 3행 → 한 줄도 안 들어간다 · 2행이면 2 성공")]
    public async Task A2_Bulk_PreCheck()
    {
        if (!Ready("G-A2")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db, 5);
        var svc = new UserService(db, new NoOpAudit());
        await FillChildrenAsync(svc, 2);            // 활성 3 · 남은 2

        var r = await svc.BulkCreateAsync(new() { NewUser("b1@t.kr"), NewUser("b2@t.kr"), NewUser("b3@t.kr") }, _tenantId);
        Assert.True(r.SeatFull);
        Assert.Equal(0, r.SuccessCount);
        Assert.Equal(3, await ActiveAsync(db));

        var r2 = await svc.BulkCreateAsync(new() { NewUser("b1@t.kr"), NewUser("b2@t.kr") }, _tenantId);
        Assert.False(r2.SeatFull);
        Assert.Equal(2, r2.SuccessCount);
    }

    [Fact(DisplayName = "G-A3 🔴 Bootstrap 부모 · max_users=0 → 생성 성공 · 한도 5 · 경고 로그")]
    public async Task A3_Bootstrap_Parent_Passes_With_Floor()
    {
        if (!Ready("G-A3")) return;
        await using (var db = await OpenAsync())
            await SeedCompanyAsync(db, 0, withParent: false);

        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = DbConnString() })
            .Build();
        var log = new CountingLogger<CompanyBootstrapProvisioner>();
        var prov = new CompanyBootstrapProvisioner(cfg, log);
        var (outcome, msg, userId) = await prov.CreateParentAsync(
            new ProofPayload { Sub = _tenantId, TenantCode = "T1", CompanyName = "시험회사" },
            new CreateParentInput { LoginId = "owner01", Password = "Owner1234!", Name = "대표" }, default);

        Assert.True(outcome == CompanyBootstrapProvisioner.CreateParentOutcome.Ok, msg);
        Assert.False(string.IsNullOrEmpty(userId));
        Assert.True(log.Warnings > 0);
        await using var db2 = await OpenAsync();
        var seats = await AccountSeatGuard.GetSeatsAsync(db2, _tenantId, default);
        Assert.Equal(5, seats.Limit);
    }

    [Fact(DisplayName = "G-A4 🔴 활성 5 + 사용 안 함 1 → [다시 사용] 거절 · 하나 사용 안 함 뒤 성공")]
    public async Task A4_Resume_Needs_Seat()
    {
        if (!Ready("G-A4")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db, 5);
        var svc = new UserService(db, new NoOpAudit());
        await FillChildrenAsync(svc, 4);
        var ids = (await db.QueryAsync<string>("SELECT user_id FROM users WHERE tenant_id=@T AND is_parent=0 ORDER BY email", new { T = _tenantId })).ToList();

        await svc.SuspendAsync(ids[0], _tenantId);
        await svc.CreateAsync(NewUser("fill@t.kr"), _tenantId);      // 다시 5/5
        await Assert.ThrowsAsync<AccountSeatFullException>(() => svc.ResumeAsync(ids[0], _tenantId));
        Assert.Equal(5, await ActiveAsync(db));

        await svc.SuspendAsync(ids[1], _tenantId);
        await svc.ResumeAsync(ids[0], _tenantId);
        Assert.Equal(5, await ActiveAsync(db));
    }

    [Fact(DisplayName = "G-A5 🔴 PUT is_active=true 우회 · 꽉 참 → 거절")]
    public async Task A5_Update_ZeroToOne_Is_Judged()
    {
        if (!Ready("G-A5")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db, 5);
        var svc = new UserService(db, new NoOpAudit());
        await FillChildrenAsync(svc, 4);
        var id = await db.ExecuteScalarAsync<string>("SELECT user_id FROM users WHERE tenant_id=@T AND is_parent=0 LIMIT 1", new { T = _tenantId });
        await svc.SuspendAsync(id!, _tenantId);
        await svc.CreateAsync(NewUser("fill@t.kr"), _tenantId);

        await Assert.ThrowsAsync<AccountSeatFullException>(() =>
            svc.UpdateAsync(id!, new UpdateUserDto { UserName = "x", Role = "User", IsActive = true }, _tenantId));
        Assert.False(await db.ExecuteScalarAsync<bool>("SELECT is_active FROM users WHERE user_id=@U", new { U = id }));
    }

    [Fact(DisplayName = "G-A6 🔴 반증 F-6·P2 — 활성 4·한도 5 · 동시 2건 → 두 번째는 첫 번째 커밋까지 줄 서고 거절된다")]
    public async Task A6_Concurrent_Second_Waits_And_Fails()
    {
        if (!Ready("G-A6")) return;
        await using (var seed = await OpenAsync())
        {
            await SeedCompanyAsync(seed, 5);
            await FillChildrenAsync(new UserService(seed, new NoOpAudit()), 3);   // 활성 4
        }

        await using var c1 = await OpenAsync();
        await using var c2 = await OpenAsync();
        await using var tx1 = await c1.BeginTransactionAsync();
        await AccountSeatGuard.EnsureSeatAsync(c1, tx1, _tenantId, 1, null, default);   // 첫 번째가 잠금을 쥔다

        await using var tx2 = await c2.BeginTransactionAsync();
        var second = Task.Run(() => AccountSeatGuard.EnsureSeatAsync(c2, tx2, _tenantId, 1, null, default));
        await Task.Delay(1500);
        Assert.False(second.IsCompleted, "두 번째 판정이 첫 번째 잠금을 기다려야 한다(잠금 없으면 바로 통과 — F-6)");

        await c1.ExecuteAsync(@"INSERT INTO users (user_id, tenant_id, email, password_hash, user_name, role, is_active, is_deleted, created_at, updated_at)
                                VALUES (UUID(), @T, 'race1@t.kr', 'x', 'r', 'User', 1, 0, NOW(6), NOW(6))",
            new { T = _tenantId }, tx1);
        await tx1.CommitAsync();

        await Assert.ThrowsAsync<AccountSeatFullException>(() => second);
        await tx2.RollbackAsync();
    }

    [Fact(DisplayName = "G-A7 🔴 계정폐기 → 아이디 표식 · 같은 아이디 재생성 성공 · 사원 연결 끊김 · 사원 행 남음")]
    public async Task A7_Retire_Frees_LoginId()
    {
        if (!Ready("G-A7")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db, 5);
        var svc = new UserService(db, new NoOpAudit());
        var id = await svc.CreateAsync(NewUser("same@t.kr"), _tenantId);

        await svc.RetireAsync(id, _tenantId);
        var email = await db.ExecuteScalarAsync<string>("SELECT email FROM users WHERE user_id=@U", new { U = id });
        Assert.StartsWith("retired+", email);
        Assert.Equal(1, await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM employees WHERE tenant_id=@T AND user_id IS NULL AND emp_name='same@t.kr'", new { T = _tenantId }));

        var again = await svc.CreateAsync(NewUser("same@t.kr"), _tenantId);   // 표식 빼면 uq_tenant_email 에 걸린다
        Assert.NotEqual(id, again);
    }

    [Fact(DisplayName = "G-A8 사용 안 함 → 목록에 보임 · 셈 −1")]
    public async Task A8_Suspend_Visible_And_Uncounted()
    {
        if (!Ready("G-A8")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db, 5);
        var svc = new UserService(db, new NoOpAudit());
        var id = await svc.CreateAsync(NewUser("rest@t.kr"), _tenantId);
        var before = (await svc.GetSeatsAsync(_tenantId)).Active;

        await svc.SuspendAsync(id, _tenantId);
        Assert.Equal(before - 1, (await svc.GetSeatsAsync(_tenantId)).Active);
        var row = (await svc.GetListAsync(_tenantId)).Single(u => u.UserId == id);
        Assert.False(row.IsActive);
    }

    [Fact(DisplayName = "G-A9 🔴 반증 F3 — 대표 suspend·retire·PUT is_active=false 모두 거절 · 값 그대로")]
    public async Task A9_Parent_Cannot_Be_Turned_Off()
    {
        if (!Ready("G-A9")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db, 5);
        var svc = new UserService(db, new NoOpAudit());

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SuspendAsync(_parentId, _tenantId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RetireAsync(_parentId, _tenantId));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateAsync(_parentId, new UpdateUserDto { UserName = "대표", Role = "TenantAdmin", IsActive = false }, _tenantId));

        var row = await db.QueryFirstAsync<(bool, bool, string)>("SELECT is_active, is_deleted, email FROM users WHERE user_id=@U", new { U = _parentId });
        Assert.Equal((true, false, "owner01"), row);
    }

    [Fact(DisplayName = "G-A11 DB-136 · 출하 DDL 위에 두 번 돌려도 같다 · 기본값 5 · extra_accounts 칸")]
    public async Task A11_Migration_Idempotent_On_Clean_Ddl()
    {
        if (!Ready("G-A11")) return;
        var sql = File.ReadAllText(Path.Combine(RepoRoot(), "src", "HitPan.API", "Migrations", "SQL", "DB-136_account_seat_defaults.sql"));
        await using var db = await OpenAsync();
        await db.ExecuteAsync(sql);
        await db.ExecuteAsync(sql);

        var def = await db.ExecuteScalarAsync<string>(
            "SELECT COLUMN_DEFAULT FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='local_subscription' AND COLUMN_NAME='max_users'");
        Assert.Equal("5", def);
        Assert.Equal(1, await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='local_subscription' AND COLUMN_NAME='extra_accounts'"));
        Assert.Equal(1, await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM schema_migrations WHERE migration_id='DB-136'"));
    }

    [Fact(DisplayName = "G-A16 한도 5 · 활성 7 → 추가 거절 · 카드 숫자 그대로(음수 없음)")]
    public async Task A16_Downgrade_Keeps_Existing()
    {
        if (!Ready("G-A16")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db, 8);
        var svc = new UserService(db, new NoOpAudit());
        await FillChildrenAsync(svc, 6);                  // 활성 7
        await db.ExecuteAsync("UPDATE local_subscription SET max_users=5 WHERE tenant_id=@T", new { T = _tenantId });

        await Assert.ThrowsAsync<AccountSeatFullException>(() => svc.CreateAsync(NewUser("over@t.kr"), _tenantId));
        var s = await svc.GetSeatsAsync(_tenantId);
        Assert.Equal(7, s.Active);
        Assert.Equal(5, s.Limit);
        Assert.Equal(7, await ActiveAsync(db));           // 기존 7 은 끊지 않는다
    }

    [Fact(DisplayName = "G-A17 🔴 account-count — 남의 회사 403 · 정상 = 활성 자식 수 · 응답 키 2개")]
    public async Task A17_AccountCount_Db()
    {
        if (!Ready("G-A17")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db, 8);
        var svc = new UserService(db, new NoOpAudit());
        await FillChildrenAsync(svc, 3);
        var ids = (await db.QueryAsync<string>("SELECT user_id FROM users WHERE tenant_id=@T AND is_parent=0", new { T = _tenantId })).ToList();
        await svc.SuspendAsync(ids[0], _tenantId);        // 사용 안 함 1 — 안 센다
        await svc.RetireAsync(ids[1], _tenantId);         // 폐기 1 — 안 센다

        var other = await CallAccountCountAsync(Guid.NewGuid().ToString(), sign: true, DbConnString());
        Assert.Equal(403, other.Status);

        var mine = await CallAccountCountAsync(_tenantId, sign: true, DbConnString());
        Assert.Equal(200, mine.Status);
        using var doc = JsonDocument.Parse(mine.Json!);
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "activeChildAccounts", "asOf" }, keys);
        Assert.Equal(1, doc.RootElement.GetProperty("activeChildAccounts").GetInt32());
    }

    // ══ DB 불필요 게이트 ══

    [Fact(DisplayName = "G-A17a 🔴 account-count — 서명 없음·틀린 서명 401 (DB 안 닿는다)")]
    public async Task A17a_AccountCount_Needs_Signature()
    {
        var none = await CallAccountCountAsync(_tenantId, sign: false, "Server=127.0.0.1;Port=1;Database=x;User=x");
        Assert.Equal(401, none.Status);
        var bad = await CallAccountCountAsync(_tenantId, sign: false, "Server=127.0.0.1;Port=1;Database=x;User=x", badSig: true);
        Assert.Equal(401, bad.Status);
    }

    private const string TestKey = "test-account-count-key-0123456789abcdef";

    private static async Task<(int Status, string? Json)> CallAccountCountAsync(string tenantId, bool sign, string cs, bool badSig = false)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bootstrap:TokenKey"] = TestKey }).Build();
        var key = TenantConfigReader.Get("HITPAN_BOOTSTRAP_TOKEN_KEY") ?? TestKey;   // 컨트롤러와 같은 순서
        var body = JsonSerializer.Serialize(new { TenantId = tenantId, Nonce = Guid.NewGuid().ToString("N"), Iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });

        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        ctx.Request.Headers["X-Hitpan-Nonce"] = "n1";
        if (sign || badSig)
        {
            using var h = new HMACSHA256(Encoding.UTF8.GetBytes(badSig ? key + "x" : key));
            ctx.Request.Headers["X-Hitpan-Signature"] =
                Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(body))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        var ctl = new WebhookInboundController(cfg, NullLogger<WebhookInboundController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx },
            ConnectionStringForTests = cs
        };
        var result = await ctl.AccountCount(default);
        var status = (result as IStatusCodeActionResult)?.StatusCode ?? 0;
        var json = result is ObjectResult o ? JsonSerializer.Serialize(o.Value) : null;
        return (status, json);
    }

    private static IEnumerable<(string File, int Line, string Text)> ErpCodeLines()
    {
        var root = RepoRoot();
        foreach (var proj in new[] { "HitPan.API", "HitPan.Application", "HitPan.Infrastructure" })
        {
            var dir = Path.Combine(root, "src", proj);
            foreach (var f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                    || f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}")) continue;
                var lines = File.ReadAllLines(f);
                for (var i = 0; i < lines.Length; i++)
                    yield return (Path.GetRelativePath(root, f).Replace('\\', '/'), i + 1, lines[i]);
            }
        }
    }

    /// <summary>주석 줄(//·///·*·⬛ 로 시작) 판별 — G-A15 필터. 대조: 실행 줄은 걸러지지 않아야 한다.</summary>
    internal static bool IsCommentLine(string line)
    {
        var t = line.TrimStart();
        return t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal)
            || t.StartsWith("/*", StringComparison.Ordinal) || t.StartsWith("⬛", StringComparison.Ordinal);
    }

    [Fact(DisplayName = "G-A12 셈 술어 — 활성 계정 셈(COUNT … FROM users … is_active = 1)은 AccountSeatGuard 한 곳")]
    public void A12_Single_Counting_Place()
    {
        var rx = new Regex(@"COUNT\([^)]*\)\s+FROM\s+users\b.*is_active\s*=\s*1", RegexOptions.IgnoreCase);
        var hits = ErpCodeLines()
            .Where(l => !IsCommentLine(l.Text) && rx.IsMatch(l.Text) && !l.File.EndsWith("Services/AccountSeatGuard.cs"))
            .Select(l => $"{l.File}:{l.Line}").ToList();
        Assert.True(hits.Count == 0, "AccountSeatGuard 밖에서 활성 계정을 센다: " + string.Join(", ", hits));
    }

    /// <summary>G-A15 허용목록 — 쓰기(본사 수신 캐시)와 Bootstrap 시드 확인(반증 F6 · P-C). 줄 번호 대신 파일+글자로(줄은 움직인다).</summary>
    private static readonly (string File, string Snippet)[] SlotAllow =
    {
        ("src/HitPan.API/Controllers/WebhookInboundController.cs", "extra_device_slots"),       // 웹훅 UPSERT 쓰기 · 본사가 아직 보낸다
        ("src/HitPan.API/Services/CompanyBootstrapProvisioner.cs", "extra_device_slots"),       // 증표 UPSERT 쓰기
        ("src/HitPan.API/Services/CompanyBootstrapProvisioner.cs", "device_slot_policy_settings"), // :646·:720 COUNT · :732 INSERT · :736 NOT EXISTS (P-C · F6)
    };

    internal static List<string> SlotReadHits(IEnumerable<(string File, int Line, string Text)> lines)
    {
        var rx = new Regex(@"extra_device_slots|ExtraDeviceSlots\b|device_slot_policy_settings|SlotPolicyDefaults\.Value\(\$?""tier\.|""extra_slot\.|UseMiddleware<SessionLimitMiddleware>");
        return lines
            .Where(l => !IsCommentLine(l.Text) && rx.IsMatch(l.Text))
            .Where(l => !SlotAllow.Any(a => l.File == a.File && l.Text.Contains(a.Snippet, StringComparison.Ordinal)))
            // DTO 속성 정의(받아 적기만 하는 칸)는 읽기가 아니다
            .Where(l => !Regex.IsMatch(l.Text, @"public\s+int\??\s+ExtraDeviceSlots\s*\{"))
            .Where(l => !(l.File.EndsWith("CompanyBootstrapProvisioner.cs") && l.Text.Contains("sub.ExtraDeviceSlots")))
            .Where(l => !(l.File.EndsWith("WebhookInboundController.cs") && l.Text.Contains("payload.ExtraDeviceSlots")))
            // 쓰기 SQL 의 값 자리(@ExtraDeviceSlots) — Webhook·Bootstrap UPSERT 만
            .Where(l => !((l.File.EndsWith("WebhookInboundController.cs") || l.File.EndsWith("CompanyBootstrapProvisioner.cs"))
                          && l.Text.Contains("@ExtraDeviceSlots")))
            // 정의 파일 자체(키·값 표)는 남긴다(설계 §7 「파일·키는 남김」) — 정의는 읽기가 아니다
            .Where(l => !l.File.EndsWith("Services/SlotPolicyDefaults.cs"))
            .Select(l => $"{l.File}:{l.Line}: {l.Text.Trim()}").ToList();
    }

    [Fact(DisplayName = "G-A15 🔴 슬롯 재읽기 0 — 비시험 ERP 코드(주석 줄 제외 · 쓰기 허용목록) · 접속기기 트랙(작4)이 들어가야 초록")]
    public void A15_No_Slot_Reads()
    {
        var hits = SlotReadHits(ErpCodeLines());
        Assert.True(hits.Count == 0, "슬롯 칸·기준값을 아직 읽는 줄:\n" + string.Join("\n", hits.Take(40)));
    }

    [Fact(DisplayName = "G-A15c 대조군 — 필터가 실제 참조를 놓치지 않는다(가짜 읽기 한 줄 · 주석 줄은 거른다)")]
    public void A15c_Filter_Catches_Fake_Read()
    {
        var fake = new[]
        {
            ("src/HitPan.API/Fake.cs", 1, "    var x = SlotPolicyDefaults.Value($\"tier.{t}.pc_limit\", 0);"),
            ("src/HitPan.API/Fake.cs", 2, "    var y = await db.QueryAsync(\"SELECT extra_device_slots FROM tenants\");"),
            ("src/HitPan.API/Fake.cs", 3, "    // ⬛ var x = SlotPolicyDefaults.Value($\"tier.{t}.pc_limit\", 0);"),
            ("src/HitPan.API/Program.cs", 4, "    branch => branch.UseMiddleware<SessionLimitMiddleware>());"),
        };
        var hits = SlotReadHits(fake);
        Assert.Equal(3, hits.Count);
        Assert.DoesNotContain(hits, h => h.Contains(":3:"));
    }

    [Fact(DisplayName = "G-A15p SessionLimitMiddleware 파이프라인 등록 0 (Program.cs 실행 줄)")]
    public void A15p_SessionLimit_Not_Registered()
    {
        var program = File.ReadAllLines(Path.Combine(RepoRoot(), "src", "HitPan.API", "Program.cs"));
        Assert.DoesNotContain(program, l => !IsCommentLine(l) && l.Contains("UseMiddleware<SessionLimitMiddleware>", StringComparison.Ordinal));
    }
}
