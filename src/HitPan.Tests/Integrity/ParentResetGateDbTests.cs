using Dapper;
using HitPan.Application.Services;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-G1-52 갈래 C — 대표 비번 되찾기 DB 게이트(설계 §12 G10 · CTO K7). 시험 DB(HITPAN_TEST_DB = CI hitpan_e2e · 출하 DDL 적재)만.
/// </summary>
/// <remarks>
/// 동시 2요청은 연결이 둘이라 TEMPORARY 표로 못 가린다 → 실표에 <b>이 시험만의 무작위 id</b> 로 넣고 끝에 지운다(시험 DB 전용 · 이 PC 3306 실물 손 0 #39).
/// 🔴 CI 등록 = build-and-test.yml GATES 에 <c>ParentResetGate</c> 한 줄(갈래 C 파일 범위 밖 · PM 몫 — 명세서 「못 한 것」).
/// </remarks>
public sealed class ParentResetGateDbTests
{
    private static string TestDb => Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    private static string ConnString()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "hitpan";
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
        return $"Server={host};Port={port};Database={TestDb};User={user};Password={pass};"
             + "DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;Pooling=false;";
    }

    private static bool ServerAvailable()
    {
        if (DbGateEnvironment.IsCi) return true;
        try
        {
            using var c = new MySqlConnection(ConnString());
            c.Open();
            return true;
        }
        catch (MySqlException)
        {
            return false;
        }
    }

    private sealed record Seed(string Tenant, string Parent, string Child, string Token);

    private static async Task<Seed> SeedAsync(MySqlConnection db, int expiresInMinutes = 10, bool forChild = false)
    {
        var s = new Seed(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), ParentResetToken.NewToken());
        const string insUser = @"INSERT INTO users (user_id, tenant_id, email, password_hash, user_name, role, is_active, created_at, updated_at,
                                   is_parent, failed_login_count, lockout_end)
                                 VALUES (@Id, @T, @Email, 'old-hash', 'gate', 'tenant_admin', 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), @P, 5, DATE_ADD(UTC_TIMESTAMP(6), INTERVAL 1 HOUR))";
        await db.ExecuteAsync(insUser, new { Id = s.Parent, T = s.Tenant, Email = "gate-p-" + s.Parent[..8], P = 1 });
        await db.ExecuteAsync(insUser, new { Id = s.Child, T = s.Tenant, Email = "gate-c-" + s.Child[..8], P = 0 });
        await db.ExecuteAsync(@"INSERT INTO refresh_tokens (token_id, user_id, token_hash, expires_at, is_revoked)
                                VALUES (@Id, @U, 'gate', DATE_ADD(UTC_TIMESTAMP(6), INTERVAL 1 DAY), 0)", new { Id = Guid.NewGuid().ToString(), U = s.Parent });
        await db.ExecuteAsync(@"INSERT INTO parent_reset_tokens (token_id, tenant_id, user_id, token_hash, expires_at)
                                VALUES (@Id, @T, @U, @H, DATE_ADD(UTC_TIMESTAMP(6), INTERVAL @M MINUTE))",
            new { Id = Guid.NewGuid().ToString(), T = s.Tenant, U = forChild ? s.Child : s.Parent, H = ParentResetToken.Hash(s.Token), M = expiresInMinutes });
        return s;
    }

    private static async Task CleanAsync(MySqlConnection db, Seed s)
    {
        var ids = new[] { s.Parent, s.Child };
        await db.ExecuteAsync("DELETE FROM parent_reset_tokens WHERE tenant_id = @T", new { T = s.Tenant });
        await db.ExecuteAsync("DELETE FROM refresh_tokens WHERE user_id IN @Ids", new { Ids = ids });
        await db.ExecuteAsync("DELETE FROM audit_logs WHERE tenant_id = @T", new { T = s.Tenant });
        await db.ExecuteAsync("DELETE FROM users WHERE tenant_id = @T", new { T = s.Tenant });
    }

    private static ParentResetCommit Commit(Seed s, string userId) =>
        new(ParentResetToken.Hash(s.Token), s.Tenant, userId, BCrypt.Net.BCrypt.HashPassword("NewPass1!"), "127.0.0.1");

    [Fact]
    public async Task G10_Db_Good_Token_Resets_Parent_Clears_Lock_Revokes_And_Audits()
    {
        if (!ServerAvailable()) { Assert.True(DbGateEnvironment.SkipOrFail("G10 ParentResetGate 정상")); return; }
        await using var db = new MySqlConnection(ConnString());
        await db.OpenAsync();
        var s = await SeedAsync(db);
        try
        {
            var store = new DapperParentResetStore(db);
            var row = await store.FindTokenAsync(ParentResetToken.Hash(s.Token), default);
            Assert.NotNull(row);
            Assert.True(row!.TargetIsParent);
            Assert.False(row.Expired || row.Used);

            Assert.Equal(ParentResetCommitOutcome.Committed, await store.ConsumeAndResetAsync(Commit(s, s.Parent), default));

            var u = await db.QuerySingleAsync<(string Hash, int Failed, DateTime? Lockout)>(
                "SELECT password_hash, failed_login_count, lockout_end FROM users WHERE user_id = @U", new { U = s.Parent });
            Assert.True(BCrypt.Net.BCrypt.Verify("NewPass1!", u.Hash));
            Assert.Equal(0, u.Failed);
            Assert.Null(u.Lockout);
            Assert.Equal("old-hash", await db.ExecuteScalarAsync<string>("SELECT password_hash FROM users WHERE user_id = @U", new { U = s.Child })); // 자식 손 0
            Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM refresh_tokens WHERE user_id = @U AND is_revoked = 0", new { U = s.Parent }));
            Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit_logs WHERE tenant_id = @T AND endpoint = @E",
                new { T = s.Tenant, E = DapperParentResetStore.AuditEndpoint }));

            // 재사용 = 거절(1회)
            Assert.Equal(ParentResetCommitOutcome.TokenNotConsumed, await store.ConsumeAndResetAsync(Commit(s, s.Parent), default));
            Assert.True((await store.FindTokenAsync(ParentResetToken.Hash(s.Token), default))!.Used);
        }
        finally { await CleanAsync(db, s); }
    }

    [Fact]
    public async Task G10_Db_Expired_Token_Is_Not_Consumed()
    {
        if (!ServerAvailable()) { Assert.True(DbGateEnvironment.SkipOrFail("G10 ParentResetGate 만료")); return; }
        await using var db = new MySqlConnection(ConnString());
        await db.OpenAsync();
        var s = await SeedAsync(db, expiresInMinutes: -1);
        try
        {
            var store = new DapperParentResetStore(db);
            Assert.True((await store.FindTokenAsync(ParentResetToken.Hash(s.Token), default))!.Expired);
            Assert.Equal(ParentResetCommitOutcome.TokenNotConsumed, await store.ConsumeAndResetAsync(Commit(s, s.Parent), default));
            Assert.Equal("old-hash", await db.ExecuteScalarAsync<string>("SELECT password_hash FROM users WHERE user_id = @U", new { U = s.Parent }));
        }
        finally { await CleanAsync(db, s); }
    }

    [Fact]
    public async Task G10_Db_Child_Row_Token_Changes_Nothing()
    {
        if (!ServerAvailable()) { Assert.True(DbGateEnvironment.SkipOrFail("G10 ParentResetGate 자식 행")); return; }
        await using var db = new MySqlConnection(ConnString());
        await db.OpenAsync();
        var s = await SeedAsync(db, forChild: true);
        try
        {
            var store = new DapperParentResetStore(db);
            var row = await store.FindTokenAsync(ParentResetToken.Hash(s.Token), default);
            Assert.False(row!.TargetIsParent);
            Assert.Null(row.LoginId);
            Assert.Equal(ParentResetCommitOutcome.NotParent, await store.ConsumeAndResetAsync(Commit(s, s.Child), default));
            Assert.Equal("old-hash", await db.ExecuteScalarAsync<string>("SELECT password_hash FROM users WHERE user_id = @U", new { U = s.Child }));
            Assert.Null(await db.ExecuteScalarAsync<DateTime?>("SELECT used_at FROM parent_reset_tokens WHERE tenant_id = @T", new { T = s.Tenant })); // 되돌림
        }
        finally { await CleanAsync(db, s); }
    }

    [Fact]
    public async Task G10_Db_Same_Token_Two_Concurrent_Requests_Exactly_One_Wins()
    {
        if (!ServerAvailable()) { Assert.True(DbGateEnvironment.SkipOrFail("G10 ParentResetGate 동시 2요청")); return; }
        await using var seedDb = new MySqlConnection(ConnString());
        await seedDb.OpenAsync();
        var s = await SeedAsync(seedDb);
        try
        {
            await using var a = new MySqlConnection(ConnString());
            await using var b = new MySqlConnection(ConnString());
            await a.OpenAsync();
            await b.OpenAsync();
            // #16 은 「한 연결을 여럿이 나눠 씀」 을 막는다 — 여기는 연결 둘(a · b)이 각자 한 요청 = 동시 2요청 흉내(K7)라 해당 없음.
            var results = await Task.WhenAll(
                Task.Run(() => new DapperParentResetStore(a).ConsumeAndResetAsync(Commit(s, s.Parent), default)),
                Task.Run(() => new DapperParentResetStore(b).ConsumeAndResetAsync(Commit(s, s.Parent), default)));
            Assert.Equal(1, results.Count(r => r == ParentResetCommitOutcome.Committed));
            Assert.Equal(1, results.Count(r => r == ParentResetCommitOutcome.TokenNotConsumed));
            Assert.Equal(1, await seedDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit_logs WHERE tenant_id = @T", new { T = s.Tenant }));
        }
        finally { await CleanAsync(seedDb, s); }
    }
}
