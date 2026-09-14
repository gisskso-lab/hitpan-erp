using Dapper;
using HitPan.Application.DTOs.Backup;
using HitPan.Application.DTOs.DataReset;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G7 LegacyUnpostedDdlGate</b> — 20260915작1 갈래 F (DB-121 + 출하 DDL).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>무엇을 재나</b>
/// ⓐ 신규 설치(출하 DDL 한 방)에 보관 표 2개 · 거래처 이월잔액 표 1개가 <b>InnoDB · utf8mb4_unicode_ci · tenant_id NOT NULL</b> 로 있다.
/// ⓑ <b>이미 설치된 고객</b>(DB-121 이전 모양)에 <b>실제 <c>DB-121</c> 파일</b>을 두 번 돌리면
///    칼럼·키·표 속성이 신규 설치와 <b>한 글자도 다르지 않다</b>(헌법 #36) — 두 번째는 무변화(멱등).
/// ⓒ <c>item_stock.avg_cost</c> = decimal(19,6) · 넓힐 때 기존 값 손실 0 · 끝전 6자리가 실제로 저장된다(설계 §16).
/// ⓓ <b>실제 <c>DataResetService.ResetAllAsync</c></b> 뒤 이 회사의 세 표가 0행 · 다른 회사 줄은 남는다(대조군).
/// </para>
/// <para>
/// 🟢 <b>초록불이 어디서 오나</b> — 글자를 안 본다. 격리 DB 에 파일을 <b>실행</b>하고 <c>information_schema</c> 와 표를 읽는다.
/// </para>
/// <para>
/// ⚠️ <b>운영 무접촉</b>(헌법 #39) — 임시 DB(<c>hitpan_legacy_ddl_*</c>)만 만들고 반드시 지운다.
/// ⚠️ MariaDB 가 없으면 건너뛴다(<c>DbGateEnvironment.SkipOrFail</c>) — <b>그 환경에서 이 게이트는 아무것도 검사하지 않는다.</b>
/// CI(<c>db-gate</c> 잡)는 <c>HITPAN_REQUIRE_DB</c> 로 SKIP 을 막는다.
/// </para>
/// </remarks>
[Collection("DeviceAndKeyGate")]
public sealed class LegacyUnpostedDdlGateTests : IDisposable
{
    private readonly string _dbName = "hitpan_legacy_ddl_" + Guid.NewGuid().ToString("N")[..8];
    private bool _created;

    private const string TenantId = "33333333-3333-3333-3333-333333333333";
    private const string OtherTenantId = "44444444-4444-4444-4444-444444444444";
    private const string UserId = "55555555-5555-5555-5555-555555555555";
    private const string AdminPassword = "Gate-Reset-1234!";

    private static readonly string[] NewTables =
    {
        "legacy_unposted_documents", "legacy_unposted_document_lines", "partner_legacy_balances"
    };

    // ══════════════════════════════════════════════════════════════
    // 준비물 — 선례(MainPcRestartMarkGateTests)와 같은 방식
    // ══════════════════════════════════════════════════════════════

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 시험이 출하 DDL·마이그를 읽을 수 없다.");
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

    /// <summary>🔴 마이그 러너와 같은 연결 성질 — <c>AllowUserVariables=true</c>(<c>SET @…</c>/<c>PREPARE</c>).</summary>
    private string DbConnString() =>
        ServerConnString().Replace("User=", $"Database={_dbName};User=");

    private static string MysqlExe() =>
        Environment.GetEnvironmentVariable("HITPAN_MYSQL")
        ?? @"C:\Program Files\MariaDB 11.4\bin\mysql.exe";

    private static bool ServerAvailable()
    {
        if (DbGateEnvironment.IsCi) return true;   // CI 는 DB 필수 — 못 붙으면 아래에서 실패로 드러난다 (작14 W1)
        if (!File.Exists(MysqlExe())) return false;
        try
        {
            using var c = new MySqlConnection(ServerConnString());
            c.Open();
            return true;
        }
        catch (MySqlException)
        {
            return false;
        }
    }

    /// <summary>🔴 신규 설치 그대로 — 빈 DB 에 출하 DDL 한 방(헌법 #36).</summary>
    private void SetUpFreshInstall()
    {
        var ddlPath = Path.Combine(RepoRoot(), "installer", "hitpan_db_clean.sql");
        Assert.True(File.Exists(ddlPath), $"출하 DDL 이 없다: {ddlPath}");

        using (var admin = new MySqlConnection(ServerConnString()))
        {
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{_dbName}`; "
                        + $"CREATE DATABASE `{_dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        }
        _created = true;

        var psi = new System.Diagnostics.ProcessStartInfo(MysqlExe())
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // 🔴 지정 안 하면 Windows 에서 콘솔 코드페이지(CP949)로 흘려보내 한글 COMMENT 가 깨진 채 들어간다
            //    (20260915 실측 — G7-2 가 출하 DDL 쪽 COMMENT 만 `???` 로 달라 빨간불). CI(리눅스)는 원래 UTF-8.
            StandardInputEncoding = new System.Text.UTF8Encoding(false)
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

        Assert.True(proc.ExitCode == 0,
            $"출하 DDL import 가 실패했다 — 신규 설치가 같은 자리에서 죽는다:\n{err}");
    }

    private static string Db121Path() =>
        Path.Combine(RepoRoot(), "src", "HitPan.API", "Migrations", "SQL",
                     "DB-121_legacy_unposted_documents.sql");

    /// <summary>🔴 <b>파일을 실제로 읽는다.</b> 글자검사가 아니라 <b>실행</b>이 판정이다.</summary>
    private static string Db121Sql()
    {
        var path = Db121Path();
        Assert.True(File.Exists(path),
            $"🔴 DB-121 마이그 파일이 없다: {path}\n"
          + "  ⇒ 이미 설치된 고객 DB 에는 보관 표·이월잔액 표가 안 생기고 avg_cost 도 2자리로 남는다(이관 500 · 끝전 불일치).");
        return File.ReadAllText(path);
    }

    /// <summary>🔴 마이그 러너와 같은 방식 — 문장들을 <b>한 번의 <c>ExecuteAsync</c></b> 로 보낸다(<c>MigrationRunner.cs:135-137</c>).</summary>
    private async Task RunMigrationSqlAsync(string sql)
    {
        await using var conn = new MySqlConnection(DbConnString());
        await conn.OpenAsync();
        await conn.ExecuteAsync(new CommandDefinition(sql, commandTimeout: 86400));
    }

    /// <summary>
    /// 🔴 <b>DB-121 이전(이미 설치된 고객) 모양</b> — 새 표 3개 없음 · avg_cost decimal(15,2).
    /// <c>IF EXISTS</c> 라서 봉합 전(출하 DDL 에 아직 표가 없을 때)에도 그대로 돈다.
    /// </summary>
    private static async Task MakePreDb121ShapeAsync(MySqlConnection db)
    {
        foreach (var t in NewTables)
            await db.ExecuteAsync($"DROP TABLE IF EXISTS `{t}`;");
        await db.ExecuteAsync("ALTER TABLE item_stock MODIFY COLUMN avg_cost decimal(15,2) NOT NULL DEFAULT 0.00;");
    }

    /// <summary>
    /// 새 표 3개 + item_stock 의 칼럼·키·표 속성을 한 줄씩 문자열로 뜬다.
    /// 🔴 두 쪽(신규 설치 / 마이그 적용)을 <b>같은 DB 서버의 information_schema</b> 로 읽는다 — 파일 글자를 파싱하지 않는다.
    /// </summary>
    private static async Task<List<string>> SchemaSnapshotAsync(MySqlConnection db)
    {
        var tables = NewTables.Append("item_stock").ToArray();
        var rows = new List<string>();

        rows.AddRange(await db.QueryAsync<string>(
            """
            SELECT CONCAT_WS('|', 'COL', table_name, ordinal_position, column_name, column_type, is_nullable,
                             IFNULL(column_default, '~'), IFNULL(collation_name, '~'), extra, column_comment)
            FROM information_schema.columns
            WHERE table_schema = DATABASE() AND table_name IN @Tables
            ORDER BY table_name, ordinal_position
            """, new { Tables = tables }));

        rows.AddRange(await db.QueryAsync<string>(
            """
            SELECT CONCAT_WS('|', 'IDX', table_name, index_name, non_unique, seq_in_index, column_name)
            FROM information_schema.statistics
            WHERE table_schema = DATABASE() AND table_name IN @Tables
            ORDER BY table_name, index_name, seq_in_index
            """, new { Tables = tables }));

        rows.AddRange(await db.QueryAsync<string>(
            """
            SELECT CONCAT_WS('|', 'TBL', table_name, engine, table_collation, table_comment)
            FROM information_schema.tables
            WHERE table_schema = DATABASE() AND table_name IN @Tables
            ORDER BY table_name
            """, new { Tables = tables }));

        return rows;
    }

    private static Task<long> CountAsync(MySqlConnection db, string table, string tenantId) =>
        db.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM `{table}` WHERE tenant_id = @T", new { T = tenantId });

    private static async Task InsertSampleRowsAsync(MySqlConnection db, string tenantId, string tag)
    {
        var docId = Guid.NewGuid().ToString();
        await db.ExecuteAsync(
            """
            INSERT INTO legacy_unposted_documents
              (doc_id, tenant_id, io_type, doc_date, legacy_dt, legacy_seq, legacy_buy_code, partner_id, reason,
               supply_amount, vat_amount, line_count, memo, source_type, source_id, migrated_source_hash)
            VALUES
              (@Doc, @T, 'sales', '2026-02-28', '00000000', 1, 2147438512, NULL, 'no_header_no_ledger',
               33059.20, -5.00, 1, NULL, 'migration', @Src, NULL)
            """, new { Doc = docId, T = tenantId, Src = $"mig-docfb-00000000-sales-1-{tag}" });

        await db.ExecuteAsync(
            """
            INSERT INTO legacy_unposted_document_lines
              (line_id, tenant_id, doc_id, line_no, item_id, item_name, spec, qty, unit_price,
               supply_amount, vat_amount, memo, stock_source_id, migrated_source_hash)
            VALUES
              (UUID(), @T, @Doc, 1, NULL, 'HTP21C', '표준형-L', 12345678.125, 305872.5490,
               33059.20, -5.00, NULL, 'mb-gate', SHA2(@Tag, 256))
            """, new { T = tenantId, Doc = docId, Tag = tag });

        await db.ExecuteAsync(
            """
            INSERT INTO partner_legacy_balances
              (balance_id, tenant_id, partner_id, legacy_buy_code, base_date, balance_amount, source_type, source_id)
            VALUES
              (UUID(), @T, 'partner-gate', 2147438512, '2026-02-28', -51577479.00, 'migration', @Src)
            """, new { T = tenantId, Src = $"mig-docf5bal-{tag}" });
    }

    // ══════════════════════════════════════════════════════════════
    // G7-1 — 신규 설치
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G7-1 🔴 출하 DDL 신규 설치 — 새 표 3개 InnoDB·utf8mb4_unicode_ci·tenant_id NOT NULL · avg_cost (19,6) · DB-121 시드")]
    public async Task G7_1_출하DDL_신규설치()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(G7_1_출하DDL_신규설치)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        await AssertTableShapeAsync(db, "출하 DDL");

        var seed = await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM schema_migrations WHERE migration_id = 'DB-121' AND app_version = 'clean-ddl' AND success = 1");
        Assert.True(seed == 1,
            "🔴 출하 DDL 시드에 ('DB-121','clean-ddl',1) 이 없다 — 신규 설치에서 DB-121 이 한 번 더 돌고, "
          + "ddl-smoke 5단계(파일 수 == 시드 수)가 빨간불이 된다.");
    }

    /// <summary>
    /// 🔴 새 표 3개 ENGINE·collation·tenant_id NOT NULL·UNIQUE + avg_cost (19,6).
    /// 출하 DDL(G7-1)과 DB-121 적용 뒤(G7-2) <b>둘 다</b> 이 함수로 잰다(병렬이슈39 ②).
    /// </summary>
    private static async Task AssertTableShapeAsync(MySqlConnection db, string origin)
    {
        var uniques = new Dictionary<string, string[]>
        {
            ["legacy_unposted_documents"] = new[] { "tenant_id,source_id" },
            ["legacy_unposted_document_lines"] = new[] { "tenant_id,migrated_source_hash" },
            ["partner_legacy_balances"] = new[] { "tenant_id,source_id", "tenant_id,partner_id" },
        };

        foreach (var t in NewTables)
        {
            var have = (await db.QueryAsync<string>(
                """
                SELECT GROUP_CONCAT(column_name ORDER BY seq_in_index SEPARATOR ',')
                FROM information_schema.statistics
                WHERE table_schema = DATABASE() AND table_name = @T AND non_unique = 0 AND index_name <> 'PRIMARY'
                GROUP BY index_name
                """, new { T = t })).ToHashSet();
            foreach (var u in uniques[t])
                Assert.True(have.Contains(u),
                    $"🔴 [{origin}] `{t}` 에 UNIQUE({u}) 가 없다 — 재이관 때 같은 줄·잔액이 두 번 쌓인다(병렬이슈39).");
        }

        foreach (var t in NewTables)
        {
            var tbl = await db.QueryFirstOrDefaultAsync<(string Engine, string Collation)>(
                "SELECT engine, table_collation FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = @T",
                new { T = t });
            Assert.True(tbl.Engine is not null,
                $"🔴 출하 DDL 에 `{t}` 가 없다 — 신규 설치 고객은 이관이 이 표에 INSERT 하다 500 이 난다(헌법 #36).");
            Assert.True(tbl.Engine == "InnoDB", $"🔴 `{t}` ENGINE={tbl.Engine} — InnoDB 여야 한다(헌법 #17).");
            Assert.True(tbl.Collation == "utf8mb4_unicode_ci",
                $"🔴 `{t}` collation={tbl.Collation} — 기존 표와 같은 utf8mb4_unicode_ci 여야 한다(조인 시 Illegal mix of collations).");

            var tenantNullable = await db.ExecuteScalarAsync<string?>(
                "SELECT is_nullable FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = @T AND column_name = 'tenant_id'",
                new { T = t });
            Assert.True(tenantNullable == "NO",
                $"🔴 `{t}.tenant_id` 가 없거나 NULL 허용이다(is_nullable={tenantNullable ?? "칼럼 없음"}) — "
              + "테넌트 격리(헌법 #2)가 깨지고 초기화(DataResetService)가 이 표를 건너뛴다.");
        }

        var cost = await db.QueryFirstAsync<(long Precision, long Scale)>(
            "SELECT numeric_precision, numeric_scale FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = 'item_stock' AND column_name = 'avg_cost'");
        Assert.True(cost.Precision == 19 && cost.Scale == 6,
            $"🔴 [{origin}] item_stock.avg_cost = decimal({cost.Precision},{cost.Scale}) — decimal(19,6) 이어야 끝전이 레거시와 맞는다(설계 §16 · R6-2).");
    }

    // ══════════════════════════════════════════════════════════════
    // G7-2 — 이미 설치된 고객 + 실제 DB-121 파일 = 신규 설치
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G7-2 🔴 기존 고객에 DB-121 두 번 — 칼럼·키·표 속성이 출하 DDL 과 같고 두 번째는 무변화")]
    public async Task G7_2_DB121_칼럼_출하DDL과_같다_멱등()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(G7_2_DB121_칼럼_출하DDL과_같다_멱등)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var fresh = await SchemaSnapshotAsync(db);

        await MakePreDb121ShapeAsync(db);
        var sql = Db121Sql();

        await RunMigrationSqlAsync(sql);
        await AssertTableShapeAsync(db, "DB-121");
        var once = await SchemaSnapshotAsync(db);

        await RunMigrationSqlAsync(sql);   // 🔴 재실행 — 마이그 러너가 실패 기록 뒤 다시 돌 수 있다
        var twice = await SchemaSnapshotAsync(db);

        var onlyFresh = fresh.Except(once).ToList();
        var onlyMig = once.Except(fresh).ToList();
        Assert.True(onlyFresh.Count == 0 && onlyMig.Count == 0,
            "🔴 DB-121 로 만든 표가 출하 DDL 로 만든 표와 다르다(헌법 #36 — 신규 고객과 기존 고객의 DB 가 갈라진다).\n"
          + $"  출하 DDL 에만: {string.Join(" ;; ", onlyFresh)}\n"
          + $"  DB-121 에만:   {string.Join(" ;; ", onlyMig)}");

        Assert.True(once.SequenceEqual(twice),
            "🔴 DB-121 두 번째 실행이 스키마를 바꿨다 — 멱등이 아니다.\n"
          + $"  달라진 줄: {string.Join(" ;; ", once.Except(twice).Concat(twice.Except(once)))}");
    }

    // ══════════════════════════════════════════════════════════════
    // G7-3 — avg_cost 넓히기: 기존 값 손실 0 · 6자리 끝전 저장
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G7-3 🔴 avg_cost 넓히기 — 기존 값 그대로 · 6자리가 저장돼 −51 × 단가 반올림이 레거시 −15,599,500.00")]
    public async Task G7_3_avg_cost_소수6자리()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(G7_3_avg_cost_소수6자리)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await MakePreDb121ShapeAsync(db);

        await db.ExecuteAsync(
            "INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost) VALUES ('st-old', @T, 'item-old', 'default', 6, 3083.33)",
            new { T = TenantId });

        await RunMigrationSqlAsync(Db121Sql());

        var old = await db.ExecuteScalarAsync<decimal>("SELECT avg_cost FROM item_stock WHERE stock_id = 'st-old'");
        Assert.True(old == 3083.33m, $"🔴 DB-121 이 기존 avg_cost 를 바꿨다: 3083.33 → {old} (넓히기만 해야 한다).");

        // 설계 §16 — HTP21C|표준형-L : −51 · 금액 −15,599,500 · 단가 305,872.549020
        await db.ExecuteAsync(
            "INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost) VALUES ('st-new', @T, 'item-new', 'default', -51, 305872.549020)",
            new { T = TenantId });

        var stored = await db.ExecuteScalarAsync<decimal>("SELECT avg_cost FROM item_stock WHERE stock_id = 'st-new'");
        Assert.True(stored == 305872.549020m,
            $"🔴 avg_cost 가 6자리로 저장되지 않았다: 305872.549020 → {stored} — 끝전 3품목이 레거시와 어긋난다(설계 §16).");

        var value = await db.ExecuteScalarAsync<decimal>(
            "SELECT ROUND(current_qty * avg_cost, 2) FROM item_stock WHERE stock_id = 'st-new'");
        Assert.True(value == -15599500.00m,
            $"🔴 화면 재고금액(current_qty × avg_cost) = {value} — 레거시 −15,599,500.00 과 다르다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G7-4 — 실제 초기화 뒤 0행 (+ 다른 회사 대조군)
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G7-4 🔴 실제 DataResetService 뒤 새 표 3개가 이 회사 0행 · 다른 회사 줄은 남는다(대조군)")]
    public async Task G7_4_초기화뒤_0행()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(G7_4_초기화뒤_0행)); return; }
        SetUpFreshInstall();

        await using (var seed = new MySqlConnection(DbConnString()))
        {
            await seed.OpenAsync();
            await seed.ExecuteAsync(
                """
                INSERT INTO users
                  (user_id, tenant_id, email, password_hash, user_name, role,
                   account_type, is_active, created_at, updated_at)
                VALUES
                  (@Uid, @Tid, 'legacy-ddl-gate@hitpan.kr', @Hash, '게이트시험자', 'tenant_admin',
                   'tenant_admin', 1, NOW(6), NOW(6))
                """,
                new { Uid = UserId, Tid = TenantId, Hash = BCrypt.Net.BCrypt.HashPassword(AdminPassword) });

            await InsertSampleRowsAsync(seed, TenantId, "mine");
            await InsertSampleRowsAsync(seed, OtherTenantId, "other");

            foreach (var t in NewTables)
                Assert.True(await CountAsync(seed, t, TenantId) == 1, $"준비 실패 — `{t}` 에 시험 줄이 안 들어갔다.");
        }

        await using var db = new MySqlConnection(DbConnString());
        var svc = new DataResetService(db, new OkBackup(), new NoOpAudit(), NullLogger<DataResetService>.Instance);
        var res = await svc.ResetAllAsync(new DataResetRequest { Password = AdminPassword }, TenantId, UserId);
        Assert.True(res.Success, $"준비 실패 — 초기화 자체가 실패했다: {res.Error}");

        await using var check = new MySqlConnection(DbConnString());
        await check.OpenAsync();
        foreach (var t in NewTables)
        {
            var mine = await CountAsync(check, t, TenantId);
            Assert.True(mine == 0,
                $"🔴 초기화 뒤 `{t}` 에 이 회사 줄이 {mine}행 남았다 — 「모두 지우고 새로 가져오기」 뒤 보관 줄·이월잔액이 두 번 쌓인다.\n"
              + "  DataResetService.PreservedTables 에 들어갔거나 tenant_id 칼럼이 빠졌는지 확인하라.");

            var other = await CountAsync(check, t, OtherTenantId);
            Assert.True(other == 1,
                $"🔴 대조군 — 초기화가 다른 회사의 `{t}` 줄까지 지웠다({other}행) — tenant_id 범위 삭제가 아니다(헌법 #2).");
        }
    }

    // ══════════════════════════════════════════════════════════════
    // 대역 — 초기화 앞 강제 백업만 성공으로 돌린다 (DB 판정과 무관)
    // ══════════════════════════════════════════════════════════════

    private sealed class OkBackup : IBackupService
    {
        public Task<RunBackupResponse> RunBackupAsync(string tenantId, string triggeredBy = "manual", CancellationToken ct = default) =>
            Task.FromResult(new RunBackupResponse { Success = true, BackupId = "gate-backup" });

        public Task<BackupSettingsDto> GetSettingsAsync(string tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException("G7 은 백업 설정을 쓰지 않는다.");
        public Task UpdateSettingsAsync(string tenantId, UpdateBackupSettingsRequest req, CancellationToken ct = default) =>
            throw new NotSupportedException("G7 은 백업 설정을 쓰지 않는다.");
        public Task<List<BackupHistoryDto>> GetHistoryAsync(string tenantId, int limit = 50, CancellationToken ct = default) =>
            throw new NotSupportedException("G7 은 백업 이력을 쓰지 않는다.");
        public Task<RestoreResponse> RestoreAsync(string tenantId, string? userId, RestoreRequest req, CancellationToken ct = default) =>
            throw new NotSupportedException("G7 은 복원을 쓰지 않는다.");
        public Task<List<RestoreHistoryDto>> GetRestoreHistoryAsync(string tenantId, int limit = 50, CancellationToken ct = default) =>
            throw new NotSupportedException("G7 은 복원 이력을 쓰지 않는다.");
    }

    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string actionType, string entityType, string? entityId = null,
            string? beforeJson = null, string? afterJson = null, string? reason = null,
            System.Data.IDbTransaction? tx = null,
            CancellationToken ct = default) => Task.CompletedTask;
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
            Console.Error.WriteLine($"[G7] 임시 DB 정리 실패 — 손으로 지워라: {_dbName} ({ex.Message})");
        }
    }
}
