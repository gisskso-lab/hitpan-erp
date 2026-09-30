using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20260929작3 — 워치독 자가생성 DDL · 규칙 C 판독 SQL 의 <b>DB 게이트</b>.
/// ⬛ 갈래 W 판(G-W8 · <c>##W8-SCHEMA##</c> · apply_status <c>consent_id</c> 칸 + ALTER 보강)은 폐기됐다(작업지시서 §8 · 설계 §12).
/// 🟢 갈래 Y(9/30) 재표적 — <b>G-R4</b>(보호 표 apply_status 스키마 불변) · 새 표 <c>local_update_attempts</c> 자가생성 · G-W3 SQL · 병렬이슈 02.
/// </summary>
/// <remarks>
/// <para>
/// 워치독은 드라이버 없이 mariadb CLI 로 SQL 문자열을 던진다. 그 문자열이 옛 고객 DB 모양(시도 표 없음)과
/// 출하 DDL DB 에서 실제로 도는지, 그리고 <b>보호 표 apply_status 를 한 칸도 안 바꾸는지</b>는 DB 에서만 잰다.
/// </para>
/// <para>
/// 🟢 <b>흉내 SQL 이 아니다</b> — <c>WatchdogStatusWriter.cs</c>·<c>WatchdogConsentReader.cs</c> 원문의
/// <c>##R-SCHEMA-*##</c>·<c>##W3-READ-*##</c> 표식 사이 문자열 리터럴을 그대로 읽어 돌린다. DB-135 는 마이그 파일 원문을 읽는다
/// (워치독은 net8.0-windows 라 이 시험 프로젝트가 참조할 수 없다 — 선례: MdbLegacyFinalStockGate 가 서비스 원문을 읽는다).
/// 표식이 사라지면 FAIL.
/// </para>
/// <para>
/// 🔴 G-R4 의 베이스 = <c>985aed9d</c>(작3 착수 전 main 머지점). 그 커밋의 출하 DDL apply_status 블록과 워치독 자가생성 문자열을
/// 아래 상수로 옮겨 두었다(CI 체크아웃은 얕아 <c>git show 985aed9d</c> 를 못 한다 — 작성 시 <c>git show</c> 로 문자 대조함 · 명세서 Y).
/// </para>
/// <para>
/// 🔴 음성 대조군이 시험 안에 있다 — ① 옛 W 보강문(<c>ADD COLUMN consent_id</c>)을 던지면 apply_status 모양이 바뀌어야 한다
/// ② 시계 정렬이면 옛 [예] ③ 「approve 존재」 판독이면 approve ④ UNIQUE 를 빼면 같은 [예] 두 번째 INSERT 가 들어간다.
/// 대조가 안 갈리면 게이트가 무력하다는 뜻이라 FAIL.
/// </para>
/// <para>
/// ⚠️ 격리 DB(<c>hitpan_wd_w8_*</c>)만 만들고 지운다(헌법 #39). DB 없으면 <c>DbGateEnvironment.SkipOrFail</c> —
/// 로컬 SKIP 은 통과가 아니다. CI <c>db-gate</c> 잡 GATES 에 <c>WatchdogApplyStatusDdlGate</c> 로 등록.
/// DB 불필요 Fact(원문 추출·G-R4 정적)는 <c>build</c> 잡에서 돈다.
/// </para>
/// </remarks>
[Collection("DeviceAndKeyGate")]
public sealed class WatchdogApplyStatusDdlGateTests : IDisposable
{
    private readonly string _dbName = "hitpan_wd_w8_" + Guid.NewGuid().ToString("N")[..8];
    private bool _created;
    private const string V = "1.3.47";

    private const string ApplyTable = "local_update_apply_status";
    private const string AttemptsTable = "local_update_attempts";

    /// <summary>⬛ 폐기된 갈래 W 의 보강문(<c>7f1f2459^</c> WatchdogStatusWriter.cs:61 원문) — 음성 대조군 전용.</summary>
    private const string OldWAlter =
        "ALTER TABLE `local_update_apply_status` ADD COLUMN IF NOT EXISTS `consent_id` bigint(20) DEFAULT NULL;";

    /// <summary>베이스 <c>985aed9d</c> installer/hitpan_db_clean.sql:2261-2272 apply_status 블록(문자 그대로).</summary>
    private const string BaseCleanApplyBlock =
        "CREATE TABLE `local_update_apply_status` (\n" +
        "  `id` bigint(20) NOT NULL AUTO_INCREMENT,\n" +
        "  `tenant_id` varchar(36) DEFAULT NULL COMMENT '로컬 테넌트 식별자(local_company.tenant_id). 워치독 단일 테넌트라 NULL 허용',\n" +
        "  `applied_version` varchar(20) NOT NULL COMMENT '적용 시도한 버전(SemVer Major.Minor.Build). 멱등키 — 버전당 1행 UPSERT',\n" +
        "  `result` varchar(20) NOT NULL COMMENT '적용 결과: success|rolled_back|rollback_failed|blocked(마이그게이트 차단 등)',\n" +
        "  `detail` text DEFAULT NULL COMMENT '실패 사유·CS 안내 등 상세(선택)',\n" +
        "  `applied_at` datetime(3) NOT NULL COMMENT '적용 완료/실패 확정 시각',\n" +
        "  `created_at` datetime(3) NOT NULL DEFAULT current_timestamp(3) COMMENT '로컬 적재 시각',\n" +
        "  PRIMARY KEY (`id`),\n" +
        "  UNIQUE KEY `uk_local_update_apply_version` (`applied_version`),\n" +
        "  KEY `idx_local_update_apply_at` (`applied_at`)\n" +
        ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;";

    /// <summary>베이스 <c>985aed9d</c> WatchdogStatusWriter.cs:121-132 apply_status 자가생성 문자열(리터럴을 이은 값).</summary>
    private const string BaseWatchdogApplyCreate =
        "CREATE TABLE IF NOT EXISTS `local_update_apply_status` (" +
        "`id` bigint(20) NOT NULL AUTO_INCREMENT, " +
        "`tenant_id` varchar(36) DEFAULT NULL, " +
        "`applied_version` varchar(20) NOT NULL, " +
        "`result` varchar(20) NOT NULL, " +
        "`detail` text DEFAULT NULL, " +
        "`applied_at` datetime(3) NOT NULL, " +
        "`created_at` datetime(3) NOT NULL DEFAULT current_timestamp(3), " +
        "PRIMARY KEY (`id`), " +
        "UNIQUE KEY `uk_local_update_apply_version` (`applied_version`), " +
        "KEY `idx_local_update_apply_at` (`applied_at`)" +
        ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;";

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
        return $"Server={host};Port={port};User={user};Password={pass};DefaultCommandTimeout=90;AllowUserVariables=true;";
    }

    private string DbConnString() => ServerConnString().Replace("User=", $"Database={_dbName};User=");

    private static string MysqlExe() =>
        Environment.GetEnvironmentVariable("HITPAN_MYSQL") ?? @"C:\Program Files\MariaDB 11.4\bin\mysql.exe";

    private static bool ServerAvailable()
    {
        if (DbGateEnvironment.IsCi) return true;
        if (!File.Exists(MysqlExe())) return false;
        try
        {
            using var c = new MySqlConnection(ServerConnString());
            c.Open();
            // 개발 PC 의 hitpan 계정은 CREATE DATABASE 가 막혀 있다(ERROR 1044) — 그 경우도 못 도는 것이다.
            c.Execute("CREATE DATABASE IF NOT EXISTS `hitpan_wd_w8_probe`; DROP DATABASE IF EXISTS `hitpan_wd_w8_probe`;");
            return true;
        }
        catch (MySqlException)
        {
            return false;
        }
    }

    private void SetUpFreshInstall()
    {
        var ddlPath = Path.Combine(RepoRoot(), "installer", "hitpan_db_clean.sql");
        using (var admin = new MySqlConnection(ServerConnString()))
        {
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{_dbName}`; CREATE DATABASE `{_dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        }
        _created = true;

        var psi = new System.Diagnostics.ProcessStartInfo(MysqlExe())
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false
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

    private static string ReadRepoText(string relPath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relPath)).Replace("\r\n", "\n");

    /// <summary>원문 파일의 두 표식 사이 C# 문자열 리터럴을 이어 붙인다(흉내 SQL 금지).</summary>
    private static string LiteralsBetween(string relPath, string begin, string end)
    {
        var text = ReadRepoText(relPath);
        var b = text.IndexOf(begin, StringComparison.Ordinal);
        var e = text.IndexOf(end, StringComparison.Ordinal);
        Assert.True(b >= 0 && e > b, $"{relPath} 에서 표식 {begin}…{end} 을 못 찾았다 — 게이트가 원문을 못 읽는다");
        return JoinLiterals(text[b..e]);
    }

    private static string JoinLiterals(string code)
    {
        var sb = new StringBuilder();
        foreach (Match m in Regex.Matches(code, "\"((?:[^\"\\\\]|\\\\.)*)\""))
            sb.Append(m.Groups[1].Value);
        return sb.ToString();
    }

    private static readonly string StatusWriterPath =
        Path.Combine("src", "HitPan.Watchdog", "AutoUpdate", "WatchdogStatusWriter.cs");

    /// <summary>워치독 시도 표 자가생성 DDL(<c>AttemptsCreateSql</c> 원문).</summary>
    private static string AttemptsCreateSql() =>
        LiteralsBetween(StatusWriterPath, "##R-SCHEMA-BEGIN##", "##R-SCHEMA-END##");

    /// <summary>DB-135 마이그 파일 원문.</summary>
    private static string Db135Sql() =>
        ReadRepoText(Path.Combine("src", "HitPan.API", "Migrations", "SQL", "DB-135_local_update_attempts.sql"));

    /// <summary>워치독 원문의 apply_status 자가생성 문자열(표식이 없어 리터럴 시작~ENGINE 끝을 잡는다 — 제품 코드 무접촉).</summary>
    private static string WatchdogApplyCreate()
    {
        var text = ReadRepoText(StatusWriterPath);
        const string head = "\"CREATE TABLE IF NOT EXISTS `local_update_apply_status` (\"";
        const string tail = "COLLATE=utf8mb4_unicode_ci;\";";
        var b = text.IndexOf(head, StringComparison.Ordinal);
        Assert.True(b >= 0, "워치독 원문에서 apply_status 자가생성 문자열을 못 찾았다");
        Assert.True(text.IndexOf(head, b + 1, StringComparison.Ordinal) < 0, "apply_status 자가생성 문자열이 두 곳 이상이다");
        var e = text.IndexOf(tail, b, StringComparison.Ordinal);
        Assert.True(e > b, "apply_status 자가생성 문자열 끝을 못 찾았다");
        return JoinLiterals(text[b..(e + tail.Length)]);
    }

    /// <summary>출하 DDL 의 apply_status CREATE 블록(주석 칸 포함 · 줄바꿈 LF 정규화).</summary>
    private static string CleanApplyBlock(string ddl)
    {
        var m = Regex.Match(ddl.Replace("\r\n", "\n"),
            "CREATE TABLE `local_update_apply_status` \\(.*?\\) ENGINE=[^;]*;", RegexOptions.Singleline);
        Assert.True(m.Success, "출하 DDL 에서 apply_status CREATE 블록을 못 찾았다");
        return m.Value;
    }

    private static string ReadSql(string version) =>
        string.Format(LiteralsBetween(Path.Combine("src", "HitPan.Watchdog", "AutoUpdate", "WatchdogConsentReader.cs"),
            "##W3-READ-BEGIN##", "##W3-READ-END##"), version);

    /// <summary>DESCRIBE 상당 — 칸(이름·형·NULL·기본값·키·extra) + 인덱스 + 엔진·콜레이션. 주석은 뺀다.</summary>
    private static async Task<string> Shape(MySqlConnection db, string table)
    {
        var cols = await db.QueryAsync<string>(
            "SELECT CAST(CONCAT_WS('|', column_name, column_type, is_nullable, IFNULL(column_default,'(null)'), column_key, extra) AS CHAR) " +
            "FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name=@t ORDER BY ordinal_position", new { t = table });
        var idx = await db.QueryAsync<string>(
            "SELECT CAST(CONCAT_WS('|', index_name, non_unique, seq_in_index, column_name) AS CHAR) " +
            "FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name=@t ORDER BY index_name, seq_in_index", new { t = table });
        var eng = await db.ExecuteScalarAsync<string?>(
            "SELECT CAST(CONCAT_WS('|', engine, table_collation) AS CHAR) FROM information_schema.tables " +
            "WHERE table_schema=DATABASE() AND table_name=@t", new { t = table });
        return string.Join("\n", cols) + "\n--\n" + string.Join("\n", idx) + "\n--\n" + (eng ?? "(표 없음)");
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
            Console.Error.WriteLine($"[정리실패] 임시 DB {_dbName} 삭제 실패 — 사람이 지워야 한다: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════════════════════
    // DB 불필요 — build 잡에서 돈다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-R 원문 추출 — 표식 사이 SQL 을 읽을 수 있다 · 시도 표만 만든다(DB 불필요)")]
    public void GR_원문추출()
    {
        var create = AttemptsCreateSql();
        Assert.StartsWith("CREATE TABLE IF NOT EXISTS `local_update_attempts`", create);
        Assert.DoesNotContain(ApplyTable, create);
        Assert.DoesNotContain("ALTER", create, StringComparison.OrdinalIgnoreCase);

        var db135 = Db135Sql();
        Assert.Contains("CREATE TABLE IF NOT EXISTS `local_update_attempts`", db135);
        Assert.DoesNotMatch(new Regex("ALTER\\s+TABLE", RegexOptions.IgnoreCase), db135);

        var read = ReadSql(V);
        Assert.Contains("FROM local_update_attempts t", read);
        Assert.DoesNotContain(ApplyTable, read);
        Assert.Contains($"WHERE c.update_version = '{V}' ORDER BY c.id DESC LIMIT 1;", read);
    }

    [Fact(DisplayName = "G-R4 정적 🔴 apply_status 스키마 = 베이스 985aed9d — 출하 DDL 블록 · 워치독 자가생성 문자열 (대조: 옛 W 칸을 넣으면 불일치)")]
    public void GR4_정적_베이스와_같다()
    {
        var clean = CleanApplyBlock(ReadRepoText(Path.Combine("installer", "hitpan_db_clean.sql")));
        Assert.Equal(BaseCleanApplyBlock, clean);

        var wd = WatchdogApplyCreate();
        Assert.Equal(BaseWatchdogApplyCreate, wd);

        // 워치독 어떤 원문도 apply_status 를 ALTER 하지 않는다(옛 W 보강문 재유입 차단).
        var wdDir = Path.Combine(RepoRoot(), "src", "HitPan.Watchdog");
        var alterRx = new Regex("ALTER\\s+TABLE\\s+`?local_update_apply_status", RegexOptions.IgnoreCase);
        var offenders = Directory.GetFiles(wdDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Where(f => alterRx.IsMatch(JoinLiterals(File.ReadAllText(f))))
            .ToList();
        Assert.True(offenders.Count == 0, "워치독이 apply_status 를 ALTER 한다: " + string.Join(", ", offenders));

        // 🔴 음성 대조 — 옛 W 가 넣던 칸 한 줄을 끼우면 비교가 갈라져야 한다(비교기가 무력하지 않다).
        var tampered = BaseCleanApplyBlock.Replace("  PRIMARY KEY (`id`),",
            "  `consent_id` bigint(20) DEFAULT NULL,\n  PRIMARY KEY (`id`),");
        Assert.NotEqual(tampered, clean);
        Assert.NotEqual(BaseWatchdogApplyCreate + " " + OldWAlter, wd);
        Assert.Matches(alterRx, OldWAlter);
    }

    // ══════════════════════════════════════════════════════════════
    // DB 필요 — CI db-gate 잡만이 계측 경로
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-R4a 🔴 시도 표 없는 옛 DB — DB-135·자가생성 두 번씩 오류 0 · 시도 표 = 출하 모양 · apply_status 전후 동일 (대조: 옛 W 보강문이면 변화)")]
    public async Task GR4a_시도표없는_DB()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(GR4a_시도표없는_DB)); return; }
        SetUpFreshInstall();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var shipAttempts = await Shape(db, AttemptsTable);
        var applyBefore = await Shape(db, ApplyTable);

        // ① 워치독 자가생성이 먼저 도는 PC(마이그 전) — 두 번.
        await db.ExecuteAsync($"DROP TABLE {AttemptsTable};");
        Assert.EndsWith("(표 없음)", await Shape(db, AttemptsTable));
        await db.ExecuteAsync(AttemptsCreateSql());
        await db.ExecuteAsync(AttemptsCreateSql());
        await db.ExecuteAsync(Db135Sql());
        Assert.Equal(shipAttempts, await Shape(db, AttemptsTable));

        // ② DB-135 가 먼저 도는 PC — 두 번 + 자가생성 두 번.
        await db.ExecuteAsync($"DROP TABLE {AttemptsTable};");
        await db.ExecuteAsync(Db135Sql());
        await db.ExecuteAsync(Db135Sql());
        await db.ExecuteAsync(AttemptsCreateSql());
        await db.ExecuteAsync(AttemptsCreateSql());
        Assert.Equal(shipAttempts, await Shape(db, AttemptsTable));

        Assert.Equal(applyBefore, await Shape(db, ApplyTable));

        // 🔴 음성 대조 — 폐기된 갈래 W 보강문이면 보호 표 모양이 바뀐다 ⇒ 이 비교가 그것을 잡는다.
        await db.ExecuteAsync(OldWAlter);
        Assert.NotEqual(applyBefore, await Shape(db, ApplyTable));
    }

    [Fact(DisplayName = "G-R4b 출하 DDL DB — DB-135·자가생성 두 번 오류 0 · 두 표 모양 불변 · 같은 [예] 두 번째 INSERT 거부 (대조: UNIQUE 빼면 들어간다)")]
    public async Task GR4b_출하DDL_DB()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(GR4b_출하DDL_DB)); return; }
        SetUpFreshInstall();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var applyBefore = await Shape(db, ApplyTable);
        var attemptsBefore = await Shape(db, AttemptsTable);
        Assert.DoesNotContain("(표 없음)", attemptsBefore);

        await db.ExecuteAsync(Db135Sql() + "\n" + AttemptsCreateSql());
        await db.ExecuteAsync(Db135Sql() + "\n" + AttemptsCreateSql());
        Assert.Equal(applyBefore, await Shape(db, ApplyTable));
        Assert.Equal(attemptsBefore, await Shape(db, AttemptsTable));

        const string ins = "INSERT INTO local_update_attempts (consent_id, update_version, result, started_at) " +
                           "VALUES (7, '" + V + "', 'in_progress', NOW(3));";
        await db.ExecuteAsync(ins);
        var dup = await Assert.ThrowsAsync<MySqlException>(() => db.ExecuteAsync(ins));
        Assert.Equal(MySqlErrorCode.DuplicateKeyEntry, dup.ErrorCode);

        // 🔴 음성 대조 — UNIQUE 를 빼면 같은 [예]로 두 번째 시도 행이 들어간다(이 단언이 UNIQUE 를 재고 있다는 증거).
        await db.ExecuteAsync("ALTER TABLE local_update_attempts DROP INDEX uk_local_update_attempts_consent;");
        await db.ExecuteAsync(ins);
        Assert.Equal(2, await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM local_update_attempts WHERE consent_id = 7"));
    }

    private static Task InsertConsent(MySqlConnection db, long id, string action, string consentedAtExpr) =>
        db.ExecuteAsync($"INSERT INTO local_update_consents (id, tenant_id, user_id, update_version, action, consented_at) " +
                        $"VALUES ({id}, 't', 'u', '{V}', '{action}', {consentedAtExpr});");

    [Fact(DisplayName = "병렬이슈 02 — PC 시계가 뒤로 간 새 [예](id 더 큼)도 최신으로 읽힌다 (대조: 시계 정렬이면 옛 [예])")]
    public async Task 병렬이슈02_시계무관_최신()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(병렬이슈02_시계무관_최신)); return; }
        SetUpFreshInstall();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await InsertConsent(db, 5, "approve", "NOW(3)");
        await InsertConsent(db, 6, "approve", "NOW(3) - INTERVAL 1 DAY");   // 시계가 하루 뒤로 간 PC 의 새 [예]
        // 갈래 Y 재표적 — 「이미 쓴 [예]」의 출처는 시도 표(설계 §12-2).
        await db.ExecuteAsync($"INSERT INTO local_update_attempts (consent_id, update_version, result, started_at, ended_at) " +
                              $"VALUES (5, '{V}', 'blocked', NOW(3), NOW(3));");

        var row = await db.QuerySingleAsync<(long Id, string Action, long Used)>(ReadSql(V));
        Assert.Equal((6L, "approve", 5L), row);   // 새 [예] → 규칙 C 가 다시 시도한다

        // 🔴 음성 대조 — 설계 초안 정렬(consented_at DESC, id DESC)이면 옛 [예](5)를 골라 「이미 쓴 [예]」로 무시된다.
        var clockSql = ReadSql(V).Replace("ORDER BY c.id DESC", "ORDER BY c.consented_at DESC, c.id DESC");
        var clockRow = await db.QuerySingleAsync<(long Id, string Action, long Used)>(clockSql);
        Assert.Equal(5L, clockRow.Id);
    }

    [Fact(DisplayName = "G-W3 SQL — approve5→reject6 이면 최신은 reject (대조: 「approve 존재」 판독이면 approve)")]
    public async Task GW3_나중에가_최신()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(GW3_나중에가_최신)); return; }
        SetUpFreshInstall();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await InsertConsent(db, 5, "approve", "NOW(3)");
        await InsertConsent(db, 6, "reject", "NOW(3)");

        var row = await db.QuerySingleAsync<(long Id, string Action, long Used)>(ReadSql(V));
        Assert.Equal((6L, "reject", 0L), row);

        var existsSql = ReadSql(V).Replace("WHERE c.update_version", "WHERE c.action = 'approve' AND c.update_version");
        var existsRow = await db.QuerySingleAsync<(long Id, string Action, long Used)>(existsSql);
        Assert.Equal("approve", existsRow.Action);
    }
}
