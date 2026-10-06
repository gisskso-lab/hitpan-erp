using Dapper;
using HitPan.Backoffice.API.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-2 (작8 Z3)</b> — 정산 0원 재현 소멸: <c>status='confirmed'</c> 결제 1건을 넣고
/// <b>실물</b> <see cref="ResellerSettlementCalculator"/> 를 불러 정산 금액이 일치하는가 (20261006작8 §4 G-2).
/// </summary>
/// <remarks>
/// <para>사고: 토스 결제는 <c>'confirmed'</c> 로 기록되는데(TossPaymentsController:135) 정산 집계는
/// <c>tp.status='approved'</c> 를 찾았다(선행검증 A-② 🔴 — <c>'approved'</c> 를 기록하는 코드 grep 0건)
/// ⇒ 토스 결제가 정산에서 전부 빠져 <b>정산 0원</b>. 사장님 Q-2 결재로 통일값 = <c>'confirmed'</c>.</para>
/// <para>게이트는 ① 새 DDL(11_z3)이 <b>실제 SchemaMigrator 경로</b>로 적용돼 <c>approved_at_dt</c> DATETIME 이
/// 생기는가 ② confirmed 결제 1건 → 정산 gross 가 금액과 일치하는가 ③ 대조군 — pending 결제·다른 달
/// confirmed 결제는 집계에 안 들어가는가 를 전부 <b>동작으로</b> 잰다. 판정 SQL 복사본이 아니라
/// 실물 계산기를 부른다(글자검사 금지 — 반복 사고 25회).</para>
/// <para>⚠️ DB 게이트는 개발 PC 에서 SKIP 이 정상(<c>hitpan</c> 은 CREATE DATABASE 거부) — CI <c>db-gate</c>
/// (<c>HITPAN_REQUIRE_DB</c>)가 유일한 계측 경로. 로컬 전후 대조는 <c>HITPAN_BO_TEST_DB</c> 로 권한 있는
/// 시험 DB 를 빌려 돈다(씨드 행은 끝나면 지운다 · 운영 무접촉 #39).</para>
/// </remarks>
[Collection("BackofficeSettlementStatusGate")]
public sealed class BackofficeSettlementStatusGateTests : IDisposable
{
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];
    private string _dbName = "";
    private bool _ownDb;      // true = 이 시험이 만든 격리 DB(통째 DROP) · false = 빌린 시험 DB(씨드 행만 삭제)
    private bool _seeded;

    private readonly string _resellerId = Guid.NewGuid().ToString();
    private readonly string _tenantId = Guid.NewGuid().ToString();
    private readonly string _signupToken = "z3-" + Guid.NewGuid().ToString("N");

    // ══ 준비물 — AccountSeatGateTests 와 같은 방식 ══

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

    // 격리 DB 연결만 풀을 끈다 — 시험마다 DB 이름이 달라 풀이 쌓이면 CI max_connections 를 갉는다
    // (작6 20261006 · AccountSeatGateTests 봉합2 관례 그대로).
    private string DbConnString() =>
        ServerConnString().Replace("User=", $"Database={_dbName};User=") + "Pooling=false;";

    /// <summary>DB 를 정하고 실제 SchemaMigrator 로 백오피스 스키마(+11_z3)를 적용한다.</summary>
    private bool Ready(string gate)
    {
        var probe = "hitpan_boz3_" + _suffix;
        try
        {
            using var c = new MySqlConnection(ServerConnString());
            c.Open();
            c.Execute($"CREATE DATABASE `{probe}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci");
            _dbName = probe;
            _ownDb = true;
        }
        catch (MySqlException ex)
        {
            // CREATE DATABASE 권한이 없는 자리(개발 PC) — 빌릴 시험 DB 가 지정돼 있으면 그쪽으로.
            var borrowed = Environment.GetEnvironmentVariable("HITPAN_BO_TEST_DB");
            if (string.IsNullOrWhiteSpace(borrowed))
            {
                Console.Error.WriteLine($"[{gate}] 서버 접속·권한 없음: {ex.Message}");
                return !DbGateEnvironment.SkipOrFail(gate);
            }
            _dbName = borrowed;
            _ownDb = false;
        }

        // 실물 적용기 — 새 DDL 파일(11_backoffice_payment_z3_approved_at.sql)이 "그 방식대로" 올라가는지가 검사 대상.
        var scriptsDir = Path.Combine(RepoRoot(), "installer", "backoffice");
        var migrator = new SchemaMigrator(DbConnString(), scriptsDir, NullLogger<SchemaMigrator>.Instance);
        migrator.ApplyAsync().GetAwaiter().GetResult();
        return true;
    }

    private void Seed(MySqlConnection db)
    {
        var bizNo = ("9" + _suffix + "000")[..10];
        db.Execute(@"
            INSERT INTO resellers (reseller_id, reseller_code, reseller_name, biz_no, ceo_name, join_date, status)
            VALUES (@Rid, @Code, @Name, @Biz, '게이트대표', CURDATE(), 'active')",
            new { Rid = _resellerId, Code = "Z3-" + _suffix, Name = "작8Z3게이트대리점" + _suffix, Biz = bizNo });

        db.Execute(@"
            INSERT INTO tenants (tenant_id, tenant_code, company_name, reseller_id, status)
            VALUES (@Tid, @Code, @Company, @Rid, 'active')",
            new { Tid = _tenantId, Code = "z3" + _suffix, Company = "작8Z3게이트회사" + _suffix, Rid = _resellerId });

        db.Execute(@"
            INSERT INTO landing_signups (signup_token, biz_no_hash, company_name, email, phone, status)
            VALUES (@Token, @Hash, @Company, 'z3gate@hitpan.test', '010-0000-0000', 'paid')",
            new { Token = _signupToken, Hash = "z3hash-" + _suffix, Company = "작8Z3게이트회사" + _suffix });

        // P1 — confirmed · 2026-10 · 110,000원 : 이 한 건이 정산에 잡혀야 한다 (G-2 본안)
        // P2 — pending  · 2026-10 : 집계에 들어가면 안 된다 (대조군 — 필터 자체가 도는지)
        // P3 — confirmed · 2026-09 : 다른 달 — 들어가면 안 된다 (월 축 대조군)
        db.Execute(@"
            INSERT INTO tenant_payments
                (payment_id, order_id, signup_token, payment_key, amount, method, status,
                 approved_at, approved_at_dt, raw_status, created_at)
            VALUES
                (UUID(), @O1, @Token, 'z3-key-1', 110000, '카드', 'confirmed',
                 '2026-10-15T10:00:00+09:00', '2026-10-15 10:00:00', 'DONE', '2026-10-15 01:00:00'),
                (UUID(), @O2, @Token, 'z3-key-2', 999999, '카드', 'pending',
                 NULL, '2026-10-16 10:00:00', 'READY', '2026-10-16 01:00:00'),
                (UUID(), @O3, @Token, 'z3-key-3', 555555, '카드', 'confirmed',
                 '2026-09-15T10:00:00+09:00', '2026-09-15 10:00:00', 'DONE', '2026-09-15 01:00:00')",
            new { O1 = "z3o1-" + _suffix, O2 = "z3o2-" + _suffix, O3 = "z3o3-" + _suffix, Token = _signupToken });
        _seeded = true;
    }

    [Fact]
    public async Task G2_Confirmed_Payment_Must_Appear_In_Settlement()
    {
        if (!Ready(nameof(BackofficeSettlementStatusGateTests))) return;

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        // ① DDL 이 실제 SchemaMigrator 경로로 적용됐는가 — approved_at_dt DATETIME · varchar 칸 병행 보존(#1)
        var dtType = await db.QueryFirstOrDefaultAsync<string>(@"
            SELECT DATA_TYPE FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = @Db AND TABLE_NAME = 'tenant_payments' AND COLUMN_NAME = 'approved_at_dt'",
            new { Db = _dbName });
        Assert.True(dtType == "datetime",
            $"[G-2①] approved_at_dt 가 DATETIME 으로 안 올라갔다 (실측: {dtType ?? "컬럼 없음"}). " +
            "11_backoffice_payment_z3_approved_at.sql 이 SchemaMigrator 를 못 탔다.");
        var varcharKept = await db.QueryFirstOrDefaultAsync<string>(@"
            SELECT DATA_TYPE FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = @Db AND TABLE_NAME = 'tenant_payments' AND COLUMN_NAME = 'approved_at'",
            new { Db = _dbName });
        Assert.True(varcharKept == "varchar",
            $"[G-2①] 기존 varchar approved_at 이 사라졌다(실측: {varcharKept ?? "없음"}) — #1·#37 위반.");

        Seed(db);

        // ② 실물 계산기 호출 — confirmed 1건(110,000) 이 2026-10 정산에 잡히는가
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:BackofficeDb"] = DbConnString()
        }).Build();
        var calc = new ResellerSettlementCalculator(cfg, NullLogger<ResellerSettlementCalculator>.Instance);
        var (ok, settlementId, error) = await calc.CalculateAsync(_resellerId, "2026-10", "z3-gate", CancellationToken.None);
        Assert.True(ok, $"[G-2②] 정산 산출 실패: {error}");

        var row = await db.QueryFirstAsync<(decimal Gross, int TenantCount, decimal Commission)>(@"
            SELECT gross_amount AS Gross, tenant_count AS TenantCount, commission_amount AS Commission
            FROM reseller_settlements WHERE settlement_id = @Id", new { Id = settlementId });

        Assert.True(row.Gross == 110000m,
            $"[G-2②] 정산 0원 재현 — confirmed 결제 110,000원이 집계에서 빠졌다 (gross 실측: {row.Gross}). " +
            "집계가 'approved' 를 찾고 토스는 'confirmed' 를 기록한다 — Q-2 통일값은 'confirmed' 다.");
        Assert.True(row.TenantCount == 1, $"[G-2②] tenant_count 실측 {row.TenantCount} ≠ 1");
        Assert.True(row.Commission == Math.Round(110000m * 0.15m, 2),
            $"[G-2②] 수수료 실측 {row.Commission} ≠ 16,500 (금액 decimal 축 #4)");

        // ③ 대조군 — pending(999,999)·다른 달 confirmed(555,555) 가 섞였으면 이 게이트는 다른 것을 재고 있는 것이다
        var lineSum = await db.QueryFirstAsync<decimal>(@"
            SELECT COALESCE(SUM(payment_amount), 0) FROM reseller_settlement_lines
            WHERE settlement_id = @Id", new { Id = settlementId });
        Assert.True(lineSum == 110000m,
            $"[G-2③] 정산 상세 합 실측 {lineSum} ≠ 110,000 — pending 또는 다른 달 결제가 섞였다.");
    }

    public void Dispose()
    {
        try
        {
            if (_ownDb && _dbName.Length > 0)
            {
                using var admin = new MySqlConnection(ServerConnString());
                admin.Open();
                admin.Execute($"DROP DATABASE IF EXISTS `{_dbName}`;");
            }
            else if (_seeded)
            {
                // 빌린 시험 DB — 씨드한 행만 정확히 되지운다 (운영 무접촉 #39 · 남의 시험 자료 무접촉)
                using var db = new MySqlConnection(DbConnString());
                db.Open();
                db.Execute(@"
                    DELETE FROM reseller_settlements WHERE reseller_id = @Rid;
                    DELETE FROM tenant_payments WHERE signup_token = @Token;
                    DELETE FROM landing_signups WHERE signup_token = @Token;
                    DELETE FROM tenants WHERE tenant_id = @Tid;
                    DELETE FROM resellers WHERE reseller_id = @Rid;",
                    new { Rid = _resellerId, Token = _signupToken, Tid = _tenantId });
            }
        }
        catch (MySqlException ex)
        {
            // 뒷정리 실패는 판정을 바꾸지 않지만 삼키지 않는다(#15) — 다음 사람이 잔재를 알게 남긴다.
            Console.Error.WriteLine($"[BackofficeSettlementStatusGate] 뒷정리 실패 db={_dbName}: {ex.Message}");
        }
    }
}
