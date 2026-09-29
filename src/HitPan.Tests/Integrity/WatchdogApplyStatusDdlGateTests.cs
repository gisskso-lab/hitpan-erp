using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20260929작3 갈래 W — <b>G-W8(DB 절반) · G-W3 SQL · 병렬이슈 02 게이트</b>.
/// </summary>
/// <remarks>
/// <para>
/// 워치독은 드라이버 없이 mariadb CLI 로 SQL 문자열을 던진다. 그 문자열이 **옛 고객 DB 모양 3가지**에서
/// 실제로 도는지, 판독 SQL 이 「최신 1건」을 **시계와 무관하게** 고르는지는 DB 에서만 잰다.
/// </para>
/// <para>
/// 🟢 <b>흉내 SQL 이 아니다</b> — <c>WatchdogStatusWriter.cs</c>·<c>WatchdogConsentReader.cs</c> 원문의
/// <c>##W8-SCHEMA-*##</c>·<c>##W3-READ-*##</c> 표식 사이 문자열 리터럴을 그대로 읽어 돌린다
/// (워치독은 net8.0-windows 라 이 시험 프로젝트가 참조할 수 없다 — 선례: MdbLegacyFinalStockGate 가 서비스 원문을 읽는다).
/// 표식이 사라지면 FAIL.
/// </para>
/// <para>
/// 🔴 음성 대조군이 시험 안에 있다 — ① ALTER 만 표 없는 DB 에 던지면 실패해야 한다
/// ② 시계 정렬(consented_at DESC) 판독이면 옛 [예]를 골라야 한다. 대조가 안 갈리면 게이트가 무력하다는 뜻이라 FAIL.
/// </para>
/// <para>
/// ⚠️ 격리 DB(<c>hitpan_wd_w8_*</c>)만 만들고 지운다(헌법 #39). DB 없으면 <c>DbGateEnvironment.SkipOrFail</c> —
/// 로컬 SKIP 은 통과가 아니다. CI <c>db-gate</c> 잡 GATES 에 <c>WatchdogApplyStatusDdlGate</c> 로 등록.
/// </para>
/// </remarks>
[Collection("DeviceAndKeyGate")]
public sealed class WatchdogApplyStatusDdlGateTests : IDisposable
{
    private readonly string _dbName = "hitpan_wd_w8_" + Guid.NewGuid().ToString("N")[..8];
    private bool _created;
    private const string V = "1.3.47";

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

    /// <summary>원문 파일의 두 표식 사이 C# 문자열 리터럴을 이어 붙인다(흉내 SQL 금지).</summary>
    private static string LiteralsBetween(string relPath, string begin, string end)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), relPath));
        var b = text.IndexOf(begin, StringComparison.Ordinal);
        var e = text.IndexOf(end, StringComparison.Ordinal);
        Assert.True(b >= 0 && e > b, $"{relPath} 에서 표식 {begin}…{end} 을 못 찾았다 — 게이트가 원문을 못 읽는다");
        var sb = new StringBuilder();
        foreach (Match m in Regex.Matches(text[b..e], "\"((?:[^\"\\\\]|\\\\.)*)\""))
            sb.Append(m.Groups[1].Value);
        return sb.ToString();
    }

    private static (string Create, string Alter) SchemaSql()
    {
        var all = LiteralsBetween(Path.Combine("src", "HitPan.Watchdog", "AutoUpdate", "WatchdogStatusWriter.cs"),
            "##W8-SCHEMA-BEGIN##", "##W8-SCHEMA-END##");
        var i = all.IndexOf("ALTER TABLE", StringComparison.Ordinal);
        Assert.True(i > 0, "자가생성 DDL 에 ALTER(옛 표 칸 보강)가 없다");
        return (all[..i].Trim(), all[i..].Trim());
    }

    private static string ReadSql(string version) =>
        string.Format(LiteralsBetween(Path.Combine("src", "HitPan.Watchdog", "AutoUpdate", "WatchdogConsentReader.cs"),
            "##W3-READ-BEGIN##", "##W3-READ-END##"), version);

    private static Task<long> ConsentColumnCount(MySqlConnection db) =>
        db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() " +
                                    "AND table_name='local_update_apply_status' AND column_name='consent_id'");

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

    [Fact(DisplayName = "G-W8 원문 추출 — 표식 사이 SQL 을 읽을 수 있다(DB 불필요 · build 잡에서 돈다)")]
    public void GW8_원문추출()
    {
        var (create, alter) = SchemaSql();
        Assert.StartsWith("CREATE TABLE IF NOT EXISTS `local_update_apply_status`", create);
        Assert.Contains("`consent_id` bigint(20) DEFAULT NULL", create);
        Assert.StartsWith("ALTER TABLE `local_update_apply_status` ADD COLUMN IF NOT EXISTS `consent_id`", alter);
        var read = ReadSql(V);
        Assert.Contains($"WHERE c.update_version = '{V}' ORDER BY c.id DESC LIMIT 1;", read);
    }

    [Fact(DisplayName = "G-W8a 표 없는 옛 DB — 자가생성 DDL 이 표+consent_id 를 만든다 (대조: ALTER 만이면 실패)")]
    public async Task GW8a_표없는_DB()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(GW8a_표없는_DB)); return; }
        SetUpFreshInstall();
        var (create, alter) = SchemaSql();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await db.ExecuteAsync("DROP TABLE local_update_apply_status;");

        // 🔴 음성 대조 — ALTER 만 두면 표 없는 DB 에서 죽는다(작업지시서 G-W8 대조군).
        await Assert.ThrowsAsync<MySqlException>(() => db.ExecuteAsync(alter));

        await db.ExecuteAsync(create + " " + alter);
        Assert.Equal(1, await ConsentColumnCount(db));
    }

    [Fact(DisplayName = "G-W8b 칸 없는 옛 DB — 행 보존 + consent_id 추가 (대조: CREATE 만이면 칸 없음)")]
    public async Task GW8b_칸없는_DB()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(GW8b_칸없는_DB)); return; }
        SetUpFreshInstall();
        var (create, alter) = SchemaSql();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await db.ExecuteAsync("ALTER TABLE local_update_apply_status DROP COLUMN consent_id;");
        await db.ExecuteAsync("INSERT INTO local_update_apply_status (applied_version, result, applied_at) VALUES ('1.3.40','success',NOW(3));");

        // 🔴 음성 대조 — CREATE IF NOT EXISTS 만으로는 옛 표에 칸이 생기지 않는다.
        await db.ExecuteAsync(create);
        Assert.Equal(0, await ConsentColumnCount(db));

        await db.ExecuteAsync(create + " " + alter);
        Assert.Equal(1, await ConsentColumnCount(db));
        Assert.Equal(1, await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM local_update_apply_status WHERE applied_version='1.3.40' AND consent_id IS NULL"));
    }

    [Fact(DisplayName = "G-W8c 이미 적용된 DB(출하 DDL) — 두 번 던져도 오류 0")]
    public async Task GW8c_이미_적용된_DB()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(GW8c_이미_적용된_DB)); return; }
        SetUpFreshInstall();
        var (create, alter) = SchemaSql();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await db.ExecuteAsync(create + " " + alter);
        await db.ExecuteAsync(create + " " + alter);
        Assert.Equal(1, await ConsentColumnCount(db));
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
        await db.ExecuteAsync($"INSERT INTO local_update_apply_status (applied_version, result, applied_at, consent_id) VALUES ('{V}','blocked',NOW(3),5);");

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
