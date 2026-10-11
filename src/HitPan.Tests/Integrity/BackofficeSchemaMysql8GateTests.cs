using Dapper;
using HitPan.Backoffice.API.Services;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261011작1 — 백오피스 스키마 적용기를 <b>운영과 같은 판(MySQL 8)</b>에서 문다 (G-M8-1~5).
///
/// <para><b>사고</b>: 운영 백오피스는 <b>MySQL 8.0.45</b> 인데 우리 SQL 은 MariaDB 전용
/// <c>ALTER … ADD COLUMN IF NOT EXISTS</c> 를 쓴다. MySQL 8 은 그걸 1064 로 거부하고,
/// 적용기는 첫 실패에서 기동을 중단한다 ⇒ <c>11</c> 번 이후 <b>표 9개·칸 17개가 운영에 한 번도 안 들어갔다</b>
/// (2026-10-11 사장님 손으로 서버 로그·<c>_schema_migrations</c>·<c>SHOW TABLES</c> 실측 —
/// docs/검증/선행/20261011_1V_백오피스_MySQL8_문법거부_선행검증서.md).</para>
///
/// <para>🔴 <b>왜 게이트 수십 개가 못 잡았나</b>: 백오피스 스키마를 적재하는 게이트가 전부
/// <c>mariadb:11.4</c> 에서만 돌았다. <b>판이 하나였다.</b> 그래서 이 파일은 <b>판을 확인하는 것</b>부터 한다 —
/// MariaDB 에 붙었으면 통과시키지 않는다(그건 다른 것을 재는 것이다).</para>
///
/// <para><b>글자가 아니라 동작을 잰다</b>: SQL 문자열을 비교하지 않는다. 실물
/// <see cref="SchemaMigrator"/> 가 <c>installer/backoffice/*.sql</c> 를 <b>진짜 MySQL 8 에 적용</b>하게 하고,
/// 그 결과를 <c>information_schema</c> 에서 <b>표·칸·인덱스 존재</b>로 판정한다.</para>
///
/// <para>DB 자리: CI <c>bo-mysql8-gate</c> 잡(root)이 격리 DB 를 만들었다 지운다.
/// 로컬에는 MySQL 8 이 없으므로 <see cref="DbGateEnvironment.SkipOrFailStrict"/> 로 간다 —
/// 🔴 <b>로컬 초록은 증거가 아니다. 증거는 CI <c>bo-mysql8-gate</c> 잡뿐이다.</b></para>
/// </summary>
public sealed class BackofficeSchemaMysql8GateTests
{
    // ══════════════════════════════════════════════════════════════
    // 선행검증서 §4 가 센 "운영에 없던 것" 목록 — 그대로 과녁으로 쓴다
    // ══════════════════════════════════════════════════════════════

    /// <summary>적용기가 만들어야 하는 표 (20·40·41번 소관).</summary>
    private static readonly string[] RequiredTables =
    [
        "tenant_update_history", "tenant_update_state",                             // 20
        "bo_cs_tickets", "bo_cs_ticket_logs", "bo_cs_replies", "bo_cs_reject_logs", // 40
        "bo_kb_docs", "bo_kb_doc_logs", "bo_ai_export_consents",                    // 41
        // 🔴 20261011작2 — 45번 신설분. G-M8-1 이 전 파일을 적용하므로 여기 더하면 MySQL 8 에서 검산된다.
        "bo_cs_outbound", "bo_cs_retention_policy",                                 // 45
    ];

    /// <summary>적용기가 <c>ALTER … ADD COLUMN</c> 으로 붙여야 하는 칸 (11·30·31·32·41·42·44번 소관).</summary>
    private static readonly (string Table, string Column)[] RequiredColumns =
    [
        ("tenant_payments", "approved_at_dt"),                                       // 11
        ("tenants", "extra_accounts"),                                               // 30
        ("landing_signups", "tenant_id"),                                            // 31
        ("tenants", "owner_account_id"), ("tenants", "owner_email"),                 // 32
        ("tenants", "owner_collected_at"), ("tenants", "owner_collect_path"),        // 32
        ("bo_cs_tickets", "promised_at"),                                            // 41
        ("bo_cs_tickets", "read_at"), ("bo_cs_tickets", "read_by"),                  // 42
        ("bo_cs_tickets", "handled_via"), ("bo_cs_tickets", "rating"),               // 42
        ("bo_cs_tickets", "rated_at"), ("bo_cs_tickets", "rating_comment"),          // 42
        ("pricing_plans", "price_display"),                                          // 44
        ("pricing_plans", "max_pc_devices"), ("pricing_plans", "max_mobile_devices"),// 44
        // 🔴 20261011작2 [4] 검증 V-1 봉합 — 45번은 표 2개 **말고 칸 3개도** 더한다.
        //   표만 `RequiredTables` 에 넣고 칸을 안 넣어서, 작1 사고의 모양(「표는 생겼는데
        //   칸이 조용히 안 생김」)이 바로 그 자리에 다시 열려 있었다. `ALTER` 는 1060 관용
        //   경로를 타므로 **실패해도 적용됨으로 기록된다** ⇒ 보증인이 없으면 영영 모른다.
        ("bo_cs_tickets", "purged_at"), ("bo_cs_tickets", "purged_by"),              // 45
        ("bo_cs_tickets", "purge_reason"),                                           // 45
    ];

    /// <summary>
    /// 🔴 20261011작1 2차수 ([4] 검증 V-2) — **형까지** 센다. 1060 관용은 「있다」만 보장하고 형은 안 본다.
    /// <para>MySQL 8 은 `int` 표시폭을 떼므로 `int(11)` 이 아니라 `int` 로 보고한다 — 실측 기준 그대로 적는다.</para>
    /// </summary>
    private static readonly (string Table, string Column, string Type)[] RequiredColumnTypes =
    [
        ("tenant_payments", "approved_at_dt", "datetime"),                 // 11
        ("tenants", "extra_accounts", "int"),                              // 30
        ("landing_signups", "tenant_id", "varchar(36)"),                   // 31
        ("tenants", "owner_account_id", "varchar(100)"),                   // 32
        ("tenants", "owner_email", "varchar(255)"),                        // 32
        ("tenants", "owner_collected_at", "datetime"),                     // 32
        ("tenants", "owner_collect_path", "varchar(30)"),                  // 32
        ("bo_cs_tickets", "promised_at", "datetime(6)"),                   // 41
        ("bo_cs_tickets", "read_by", "varchar(36)"),                       // 42
        ("bo_cs_tickets", "handled_via", "varchar(20)"),                   // 42
        ("bo_cs_tickets", "rating", "tinyint"),                            // 42
        ("bo_cs_tickets", "rating_comment", "varchar(500)"),               // 42
        ("pricing_plans", "price_display", "varchar(50)"),                 // 44
        ("pricing_plans", "max_pc_devices", "int"),                        // 44
        ("pricing_plans", "max_mobile_devices", "int"),                    // 44
        // 🔴 20261011작2 [4] V-1 — 형까지 센다(1060 관용은 「있다」만 보장한다).
        ("bo_cs_tickets", "purged_at", "datetime(6)"),                     // 45
        ("bo_cs_tickets", "purged_by", "varchar(36)"),                     // 45
        ("bo_cs_tickets", "purge_reason", "varchar(200)"),                 // 45
    ];

    /// <summary>적용기가 <c>ALTER … ADD KEY</c> 로 붙여야 하는 인덱스 — ADD KEY 벗기기 경로의 과녁.</summary>
    private static readonly (string Table, string Index)[] RequiredIndexes =
    [
        ("landing_signups", "ix_landing_signups_tenant"), // 31
        ("bo_cs_tickets", "idx_bo_cs_stage"),             // 42
    ];

    /// <summary>env <c>${VAR}</c> 치환이 필요해 값 없이는 건너뛰는 파일 — 적용 기대 목록에서 뺀다.</summary>
    private const string EnvDependentFile = "91_seed_admin.sql";

    // ══════════════════════════════════════════════════════════════
    // G-M8-1 — 빈 MySQL 8 DB 에 전 파일 적용이 완주한다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-M8-1 MySQL 8 에 installer/backoffice/*.sql 전체 적용이 예외 없이 완주한다")]
    public async Task G_M8_1_전체적용이_완주한다()
    {
        const string gate = "G-M8-1 MySQL8 전체적용";
        await using var db = await Mysql8GateDb.TryCreateAsync(gate, "hitpan_bo_mysql8_gate_1", applyAll: false);
        if (db is null) return;

        // 🔴 여기서 던지면 그게 이 게이트의 FAIL 이다 — 봉합 전에는 1064(IF NOT EXISTS)로 던진다.
        await db.ApplyWithRealMigratorAsync();

        var applied = (await db.QueryAsync<string>(
            "SELECT file_name FROM _schema_migrations ORDER BY file_name")).ToList();

        var expected = db.SqlFileNames().Where(f => f != EnvDependentFile).ToList();
        var missing = expected.Except(applied).ToList();

        Assert.True(missing.Count == 0,
            $"[{gate}] 적용 안 된 파일 {missing.Count}개: {string.Join(", ", missing)}\n"
          + $"  적용된 것: {string.Join(", ", applied)}\n"
          + "  (적용기가 중간에 멈췄다 — 운영 10/11 사고와 같은 모양이다)");

        // 양성 축 — 아무것도 안 하고 초록이 되는 것을 막는다.
        Assert.True(applied.Count >= 13, $"[{gate}] 적용 파일 {applied.Count}개 — 너무 적다(13 이상이어야 한다).");
    }

    // ══════════════════════════════════════════════════════════════
    // G-M8-2 — 표·칸·인덱스가 실제로 생겼다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-M8-2 적용 후 표 9개·칸 17개·인덱스 2개가 MySQL 8 에 실재한다")]
    public async Task G_M8_2_표와_칸이_실재한다()
    {
        const string gate = "G-M8-2 MySQL8 표·칸 실재";
        await using var db = await Mysql8GateDb.TryCreateAsync(gate, "hitpan_bo_mysql8_gate_2", applyAll: true);
        if (db is null) return;

        var tables = (await db.QueryAsync<string>(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE()")).ToList();
        var missingTables = RequiredTables.Except(tables, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.True(missingTables.Count == 0,
            $"[{gate}] 없는 표 {missingTables.Count}개: {string.Join(", ", missingTables)}");

        var columns = (await db.QueryAsync<string>(
            "SELECT LOWER(CONCAT(table_name, '.', column_name)) "
          + "FROM information_schema.columns WHERE table_schema = DATABASE()")).ToHashSet();
        var missingColumns = RequiredColumns
            .Where(x => !columns.Contains($"{x.Table}.{x.Column}".ToLowerInvariant()))
            .Select(x => $"{x.Table}.{x.Column}").ToList();
        Assert.True(missingColumns.Count == 0,
            $"[{gate}] 없는 칸 {missingColumns.Count}개: {string.Join(", ", missingColumns)}\n"
          + "  (ALTER … ADD COLUMN 경로가 MySQL 8 에서 안 돌았다)");

        var indexes = (await db.QueryAsync<string>(
            "SELECT LOWER(CONCAT(table_name, '.', index_name)) "
          + "FROM information_schema.statistics WHERE table_schema = DATABASE()")).ToHashSet();
        var missingIndexes = RequiredIndexes
            .Where(x => !indexes.Contains($"{x.Table}.{x.Index}".ToLowerInvariant()))
            .Select(x => $"{x.Table}.{x.Index}").ToList();
        Assert.True(missingIndexes.Count == 0,
            $"[{gate}] 없는 인덱스 {missingIndexes.Count}개: {string.Join(", ", missingIndexes)}\n"
          + "  (ALTER … ADD KEY 경로가 MySQL 8 에서 안 돌았다)");

        // 🔴 20261011작1 2차수 — [4] 검증 V-2 교정: 종전 단언은 **존재만** 봤다.
        //   1060 관용은 「칸이 있다」만 보장하고 **형은 안 본다** — `varchar(10)` 자리에 `VARCHAR(100)` 을
        //   요청해도 1060 으로 넘어가며 짧은 칸이 그대로 남는다(검증자 MariaDB 실측).
        //   그러면 요금제 500·S-2 수집 1406 같은 사고가 **칸이 있는데도** 난다. 형까지 센다.
        var types = (await db.QueryAsync<string>(
            "SELECT LOWER(CONCAT(table_name, '.', column_name, '=', column_type)) "
          + "FROM information_schema.columns WHERE table_schema = DATABASE()")).ToHashSet();
        var wrongTypes = RequiredColumnTypes
            .Where(x => !types.Contains($"{x.Table}.{x.Column}={x.Type}".ToLowerInvariant()))
            .Select(x => $"{x.Table}.{x.Column} 기대 {x.Type}").ToList();
        Assert.True(wrongTypes.Count == 0,
            $"[{gate}] 형이 다른(또는 없는) 칸 {wrongTypes.Count}개: {string.Join(" · ", wrongTypes)}\n"
          + "  실제 형: "
          + string.Join(" · ", types.Where(t => RequiredColumnTypes.Any(
                r => t.StartsWith($"{r.Table}.{r.Column}=".ToLowerInvariant(), StringComparison.Ordinal)))));
    }

    // ══════════════════════════════════════════════════════════════
    // G-M8-6 — 🔴 [4] 검증 V-1: 「한 문장 여러 절」 ALTER 의 부분 상태
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-M8-6 부분 상태 — 여러 절 ALTER 에서 한 칸만 미리 있어도 나머지 절이 전부 적용된다")]
    public async Task G_M8_6_부분상태에서도_나머지_절이_적용된다()
    {
        const string gate = "G-M8-6 MySQL8 부분상태";
        await using var db = await Mysql8GateDb.TryCreateAsync(gate, "hitpan_bo_mysql8_gate_6", applyAll: false);
        if (db is null) return;

        // ① `tenants` 표만 세운다(적용기 경로 밖 — 사고 상태를 만드는 재료).
        await db.ApplyOneFileAsync("00_backoffice_core.sql");

        // ② 🔴 32번이 더할 **4칸 중 하나만** 미리 만든다 = 실제로 일어날 수 있는 모양
        //    (`00_backoffice_core.sql:101` 이 「운영 수동 ALTER만」 전례를 스스로 적어 둔다).
        await db.ExecuteAsync(
            "ALTER TABLE tenants ADD COLUMN owner_account_id VARCHAR(100) NULL DEFAULT NULL");

        // ③ 실물 적용기를 돌린다.
        await db.ApplyWithRealMigratorAsync();

        // ④ 봉합 전이면: 32번이 1060 으로 통째 롤백되고 「적용됨」으로 기록돼 **나머지 3칸이 영영 안 생긴다.**
        var owner = await db.CountAsync(
            "SELECT COUNT(*) FROM information_schema.columns "
          + "WHERE table_schema = DATABASE() AND table_name = 'tenants' "
          + "AND column_name IN ('owner_account_id','owner_email','owner_collected_at','owner_collect_path')");
        Assert.True(owner == 4,
            $"[{gate}] `tenants` owner 칸이 {owner}/4 — 여러 절 ALTER 가 부분 적용으로 끝났다(V-1 재발).");

        // ⑤ 두 번째 축 — 이미 다 적용된 DB 에서 **42번(6절 한 문장)** 의 칸 하나만 지우고 이력을 비운다.
        //    6절 문장이 1060 을 내고, 절 단위 재시도가 그 한 칸을 되살려야 한다.
        await db.ExecuteAsync("ALTER TABLE bo_cs_tickets DROP COLUMN rating_comment");
        await db.ExecuteAsync("DELETE FROM _schema_migrations");
        await db.ApplyWithRealMigratorAsync();

        var restored = await db.CountAsync(
            "SELECT COUNT(*) FROM information_schema.columns "
          + "WHERE table_schema = DATABASE() AND table_name = 'bo_cs_tickets' "
          + "AND column_name = 'rating_comment'");
        Assert.True(restored == 1,
            $"[{gate}] `bo_cs_tickets.rating_comment` 이 {restored}/1 — 6절 문장의 부분 상태가 안 복구됐다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-M8-3 — 두 번 적용해도 완주한다 (「이미 있다」 관용 경로를 실제로 태운다)
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-M8-3 이력을 비우고 두 번째 적용해도 완주한다 — 1060·1061 관용 경로 실측")]
    public async Task G_M8_3_두번_적용해도_완주한다()
    {
        const string gate = "G-M8-3 MySQL8 재적용 멱등";
        await using var db = await Mysql8GateDb.TryCreateAsync(gate, "hitpan_bo_mysql8_gate_3", applyAll: true);
        if (db is null) return;

        // 이력표를 비운다 = 「칸은 이미 있는데 적용 안 된 것으로 보이는」 상태. 관용 경로를 지나가야 통과다.
        await db.ExecuteAsync("DELETE FROM _schema_migrations");

        // 🔴 여기서 1060(Duplicate column name)으로 던지면 FAIL — 관용이 안 걸린 것이다.
        await db.ApplyWithRealMigratorAsync();

        var applied = await db.CountAsync("SELECT COUNT(*) FROM _schema_migrations");
        Assert.True(applied >= 13, $"[{gate}] 2회차 적용 기록 {applied}개 — 13 이상이어야 한다.");

        // 칸이 2회차에서 날아가지 않았는지(= ADD 가 아니라 다른 짓을 하지 않았는지) 확인한다.
        var colCount = await db.CountAsync(
            "SELECT COUNT(*) FROM information_schema.columns "
          + "WHERE table_schema = DATABASE() AND table_name = 'tenants' "
          + "AND column_name IN ('owner_account_id','owner_email','owner_collected_at','owner_collect_path')");
        Assert.Equal(4L, colCount);
    }

    // ══════════════════════════════════════════════════════════════
    // G-M8-4 — 🔴 대조군: 보정이 없으면 같은 DB 가 같은 파일을 거부한다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-M8-4 대조군 — 11번 원문을 종전 방식(통째 한 배치)으로 보내면 MySQL 8 이 1064 로 거부한다")]
    public async Task G_M8_4_대조군_원문은_거부된다()
    {
        const string gate = "G-M8-4 MySQL8 대조군";
        await using var db = await Mysql8GateDb.TryCreateAsync(gate, "hitpan_bo_mysql8_gate_4", applyAll: true);
        if (db is null) return;

        var raw = await File.ReadAllTextAsync(
            Path.Combine(Mysql8GateDb.BoSqlDir(), "11_backoffice_payment_z3_approved_at.sql"));

        // ① 종전 경로(파일 통째 한 배치) — 이 판에서는 거부돼야 한다.
        //    🔴 이게 통과하면 이 게이트 전체가 다른 것을 재고 있다는 뜻이다(판이 MySQL 8 이 아니거나,
        //       파일에서 그 문법이 사라졌거나). 그러면 G-M8-1 의 초록은 증거가 아니다.
        var ex = await Assert.ThrowsAsync<MySqlException>(() => db.ExecuteAsync(raw));
        Assert.Equal(1064, (int)ex.ErrorCode);

        // ② 같은 파일을 보정 경로(쪼개기 + 토큰 벗기기)로 보내면 **문법 오류가 사라진다**.
        //    이미 적용된 DB 라 「이미 있다」(1060 등)는 날 수 있다 — 그건 정상이고, 1064 만 없어야 한다.
        foreach (var statement in SchemaMigrator.SplitStatements(raw))
        {
            try
            {
                await db.ExecuteAsync(SchemaMigrator.StripAlterExistsTokens(statement));
            }
            catch (MySqlException retried)
            {
                Assert.True((int)retried.ErrorCode != 1064,
                    $"[{gate}] 보정 후에도 문법 오류(1064)가 났다: {retried.Message}");
                Assert.True((int)retried.ErrorCode is 1060 or 1061 or 1091 or 1826,
                    $"[{gate}] 예상 못 한 오류 {(int)retried.ErrorCode}: {retried.Message}");
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-M8-5 — 쪼개기·벗기기 단위시험 (DB 불필요 · 음성 대조 포함)
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-M8-5a 쪼개기 — 문자열·주석 안의 ; 는 문장 경계가 아니다")]
    public void G_M8_5a_쪼개기가_인용과_주석을_가린다()
    {
        var sql = """
            -- 주석 안 세미콜론; 은 경계가 아니다
            CREATE TABLE a (x varchar(10));
            INSERT INTO a (x) VALUES ('세미콜론; 포함');
            /* 블록주석; 안 */
            ALTER TABLE a ADD COLUMN IF NOT EXISTS y int NULL;
            """;

        var statements = SchemaMigrator.SplitStatements(sql);

        Assert.Equal(3, statements.Count);
        Assert.Contains("CREATE TABLE a", statements[0]);
        Assert.Contains("세미콜론; 포함", statements[1]);  // 문자열이 안 쪼개졌다
        Assert.Contains("ALTER TABLE a", statements[2]);

        // 음성 대조 — 주석만 있는 꼬리는 문장으로 세지 않는다(빈 문장은 MySQL 이 1065 로 거부한다).
        Assert.Empty(SchemaMigrator.SplitStatements("-- 주석만 있다\n/* 이것도 */\n"));
    }

    [Fact(DisplayName = "G-M8-5b 벗기기 — ALTER 의 IF NOT EXISTS 만 벗기고 CREATE TABLE IF NOT EXISTS 는 보존한다")]
    public void G_M8_5b_ALTER만_벗긴다()
    {
        // ① ALTER 는 벗긴다
        var altered = SchemaMigrator.StripAlterExistsTokens(
            "ALTER TABLE t ADD COLUMN IF NOT EXISTS c datetime NULL DEFAULT NULL");
        Assert.DoesNotContain("IF NOT EXISTS", altered, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ADD COLUMN c", altered);

        // ② ADD KEY 도 벗긴다 — 인덱스 이름은 남아야 한다
        var key = SchemaMigrator.StripAlterExistsTokens(
            "ALTER TABLE t ADD KEY IF NOT EXISTS ix_t (c)");
        Assert.DoesNotContain("IF NOT EXISTS", key, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ADD KEY ix_t", key);

        // ③ 🔴 이름이 중간에 끼는 FK 모양 — 실물 31번이 쓰는 그 모양이다(모양 열거로는 놓쳤던 자리).
        var fk = SchemaMigrator.StripAlterExistsTokens(
            "ALTER TABLE t ADD CONSTRAINT fk_x FOREIGN KEY IF NOT EXISTS (c) REFERENCES u (c)");
        Assert.DoesNotContain("IF NOT EXISTS", fk, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FOREIGN KEY (c) REFERENCES u (c)", fk);

        // ④ 🔴 음성 대조 — 인용 안의 글자는 안 고친다. COMMENT 를 고치면 그 글자가 DB 에 들어간다.
        var comment = SchemaMigrator.StripAlterExistsTokens(
            "ALTER TABLE t ADD COLUMN IF NOT EXISTS c int NULL COMMENT 'IF NOT EXISTS 라고 적힌 설명'");
        Assert.Contains("COMMENT 'IF NOT EXISTS 라고 적힌 설명'", comment);
        Assert.Contains("ADD COLUMN c int NULL", comment);

        // ⑤ 🔴 음성 대조 — CREATE TABLE IF NOT EXISTS 는 MySQL 도 아는 문법이다. 벗기면 멱등성이 깨진다.
        const string create = "CREATE TABLE IF NOT EXISTS t (c int)";
        Assert.Equal(create, SchemaMigrator.StripAlterExistsTokens(create));

        // ⑥ 음성 대조 — DROP TABLE IF EXISTS 도 그대로 둔다
        const string drop = "DROP TABLE IF EXISTS t";
        Assert.Equal(drop, SchemaMigrator.StripAlterExistsTokens(drop));
    }

    [Fact(DisplayName = "G-M8-5c 실물 SQL 14파일 — 쪼갠 문장이 0개인 파일이 없다")]
    public void G_M8_5c_실물파일을_쪼갤_수_있다()
    {
        var dir = Mysql8GateDb.BoSqlDir();
        var files = Directory.GetFiles(dir, "*.sql").OrderBy(Path.GetFileName, StringComparer.Ordinal).ToList();
        Assert.True(files.Count >= 14, $"installer/backoffice 의 .sql 이 {files.Count}개 — 14 이상이어야 한다.");

        foreach (var path in files)
        {
            var statements = SchemaMigrator.SplitStatements(File.ReadAllText(path));
            Assert.True(statements.Count > 0, $"{Path.GetFileName(path)} — 쪼갠 문장이 0개다.");

            // 쪼갠 문장의 **실행 부분**에 ALTER 의 IF NOT EXISTS 가 남아 있으면 MySQL 8 에서 1064 가 된다.
            //   🔴 주석을 지우고 본다 — 31번 파일은 주석 안에 「ADD CONSTRAINT … FOREIGN KEY IF NOT EXISTS」
            //      라는 설명 문장을 들고 있다. 그건 실행되지 않으므로 판정 대상이 아니다.
            foreach (var raw in statements)
            {
                var executable = StripComments(SchemaMigrator.StripAlterExistsTokens(raw)).Trim();
                if (!executable.StartsWith("ALTER", StringComparison.OrdinalIgnoreCase)) continue;

                Assert.DoesNotContain("IF NOT EXISTS", executable, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("IF EXISTS", executable, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact(DisplayName = "G-M8-5d 절 쪼개기 — 여러 절 ALTER 를 머리+절로 가른다 (괄호·인용 안 쉼표는 경계 아님)")]
    public void G_M8_5d_여러절_ALTER를_가른다()
    {
        // ① 실물 32번 — 세미콜론 1개 = 한 문장 · 그 안에 ADD COLUMN 4절 (V-1 의 현장)
        var raw = File.ReadAllText(Path.Combine(Mysql8GateDb.BoSqlDir(), "32_backoffice_s2_owner.sql"));
        var statements = SchemaMigrator.SplitStatements(raw);
        Assert.Single(statements);

        var clauses = SchemaMigrator.SplitAlterClauses(statements[0], out var prefix);
        Assert.Equal(4, clauses.Count);
        Assert.Contains("ALTER TABLE tenants", prefix);
        Assert.All(clauses, c =>
            Assert.StartsWith("ADD COLUMN", StripComments(c).Trim(), StringComparison.OrdinalIgnoreCase));

        // ② 실물 42번 첫 문장 — 6절
        var raw42 = File.ReadAllText(Path.Combine(Mysql8GateDb.BoSqlDir(), "42_backoffice_cs_stages.sql"));
        var first42 = SchemaMigrator.SplitStatements(raw42)[0];
        Assert.Equal(6, SchemaMigrator.SplitAlterClauses(first42, out _).Count);

        // ③ 괄호 안 쉼표는 경계가 아니다
        Assert.Equal(2, SchemaMigrator.SplitAlterClauses(
            "ALTER TABLE t ADD KEY ix (a, b, c), ADD COLUMN d int NULL", out _).Count);

        // ④ 인용 안 쉼표도 경계가 아니다
        Assert.Equal(2, SchemaMigrator.SplitAlterClauses(
            "ALTER TABLE t ADD COLUMN a int NULL COMMENT 'ㄱ, ㄴ, ㄷ', ADD COLUMN b int NULL", out _).Count);

        // ⑤ 음성 대조 — ALTER 가 아니면 빈 목록(절 쪼개기를 엉뚱한 문장에 적용하지 않는다)
        Assert.Empty(SchemaMigrator.SplitAlterClauses("CREATE TABLE t (a int, b int)", out _));
    }

    /// <summary>주석만 지운다 — 시험이 「실행되는 부분」만 보고 판정하기 위한 보조.</summary>
    private static string StripComments(string sql) => System.Text.RegularExpressions.Regex.Replace(
        sql,
        @"/\*.*?\*/|--(?=[ \t\r\n])[^\n]*|--$|#[^\n]*",
        " ",
        System.Text.RegularExpressions.RegexOptions.Singleline
        | System.Text.RegularExpressions.RegexOptions.Multiline);

    // ══════════════════════════════════════════════════════════════
    // MySQL 8 전용 격리 DB — 판을 확인하는 것부터 한다
    // ══════════════════════════════════════════════════════════════

    private sealed class Mysql8GateDb : IAsyncDisposable
    {
        private readonly string _name;

        private Mysql8GateDb(string name) { _name = name; }

        public string ConnString => ServerConnString().Replace("User=", $"Database={_name};User=");

        internal static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
                dir = dir.Parent;
            }
            throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — installer/backoffice 를 읽을 수 없다.");
        }

        internal static string BoSqlDir() => Path.Combine(RepoRoot(), "installer", "backoffice");

        private static string ServerConnString()
        {
            var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
            var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
            var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root";
            var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
            return $"Server={host};Port={port};User={user};Password={pass};"
                 + "DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;";
        }

        public List<string> SqlFileNames() => Directory.GetFiles(BoSqlDir(), "*.sql")
            .Select(f => Path.GetFileName(f))
            .OrderBy(x => x, StringComparer.Ordinal).ToList();

        /// <summary>
        /// 격리 DB 한 벌. <paramref name="applyAll"/> = true 면 만들면서 실물 적용기까지 돌린다.
        /// <para>🔴 <b>판 확인이 먼저다</b> — MariaDB 에 붙었으면 통과시키지 않는다. 이 게이트의 존재 이유가
        /// 「운영과 같은 판에서 재는 것」이기 때문이다.</para>
        /// </summary>
        public static async Task<Mysql8GateDb?> TryCreateAsync(string gateName, string dbName, bool applyAll)
        {
            try
            {
                await using var admin = new MySqlConnection(ServerConnString());
                await admin.OpenAsync();

                var version = await admin.ExecuteScalarAsync<string>("SELECT VERSION()") ?? string.Empty;
                if (version.Contains("mariadb", StringComparison.OrdinalIgnoreCase))
                {
                    // 🔴 MariaDB 는 ADD COLUMN IF NOT EXISTS 를 받아준다 ⇒ 여기서 초록이 나면 그건 거짓 초록이다.
                    if (DbGateEnvironment.IsCi)
                    {
                        throw new Xunit.Sdk.XunitException(
                            $"[게이트 오배치] {gateName} — 붙은 서버가 MariaDB({version})다.\n"
                          + "  이 게이트는 운영과 같은 MySQL 8 판에서만 의미가 있다.\n"
                          + "  CI 잡 bo-mysql8-gate 의 서비스 컨테이너가 mysql:8.0 인지 확인하라.");
                    }
                    Console.Error.WriteLine(
                        $"[SKIP] {gateName} — 붙은 서버가 MariaDB({version}). MySQL 8 판이 아니라 재지 않는다.");
                    DbGateEnvironment.SkipOrFailStrict(gateName);
                    return null;
                }

                await admin.ExecuteAsync($"DROP DATABASE IF EXISTS `{dbName}`; "
                    + $"CREATE DATABASE `{dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
            }
            catch (MySqlException ex)
            {
                Console.Error.WriteLine($"[{gateName}] MySQL 8 DB 준비 실패({ex.Number}): {ex.Message}");
                DbGateEnvironment.SkipOrFailStrict(gateName);
                return null;
            }

            var db = new Mysql8GateDb(dbName);
            if (applyAll)
            {
                await db.ApplyWithRealMigratorAsync();
            }
            return db;
        }

        /// <summary>실물 적용기 그 자체를 돌린다 — 시험이 적재 방식을 흉내내지 않는다.</summary>
        public Task ApplyWithRealMigratorAsync() =>
            new SchemaMigrator(ConnString, BoSqlDir(), NullLogger<SchemaMigrator>.Instance).ApplyAsync();

        /// <summary>
        /// 파일 하나를 내용 그대로 실행한다(적용기 경로 **밖** — 사고 상태를 만드는 재료로만 쓴다).
        /// <para>쓰는 곳은 G-M8-6 뿐이고, 거기 쓰는 `00` 번은 `CREATE TABLE IF NOT EXISTS` 뿐이라 통째 실행이 된다.</para>
        /// </summary>
        public async Task ApplyOneFileAsync(string fileName)
        {
            var path = Path.Combine(BoSqlDir(), fileName);
            Assert.True(File.Exists(path), $"재료 파일이 없다: {path}");
            await ExecuteAsync(await File.ReadAllTextAsync(path));
        }

        public async Task<IEnumerable<T>> QueryAsync<T>(string sql)
        {
            await using var c = new MySqlConnection(ConnString);
            await c.OpenAsync();
            return await c.QueryAsync<T>(sql);
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var c = new MySqlConnection(ConnString);
            await c.OpenAsync();
            await c.ExecuteAsync(sql);
        }

        public async Task<long> CountAsync(string sql)
        {
            await using var c = new MySqlConnection(ConnString);
            await c.OpenAsync();
            return await c.ExecuteScalarAsync<long>(sql);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var admin = new MySqlConnection(ServerConnString());
                await admin.OpenAsync();
                await admin.ExecuteAsync($"DROP DATABASE IF EXISTS `{_name}`;");
            }
            catch (MySqlException ex)
            {
                // #15 — 뒷정리 실패를 삼키지 않는다(다음 실행의 DROP IF EXISTS 가 지운다).
                Console.Error.WriteLine($"[작1게이트] 격리 DB 뒷정리 실패: {ex.Message}");
            }
        }
    }
}
