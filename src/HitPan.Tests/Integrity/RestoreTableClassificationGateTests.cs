using System.Text.RegularExpressions;
using HitPan.Application.Services;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G13 분류 전수 게이트 — 비DB 쪽</b>(W-G1-52 갈래 A-1 · 설계 §15-4 · §15-9 G13 · 덧 지시서 §3).
/// </summary>
/// <remarks>
/// <para>출하 DDL(<c>installer/hitpan_db_clean.sql</c> · #36 신규설치 단일 진실원)과 마이그 폴더(<c>src/HitPan.API/Migrations/SQL/</c>)의
/// <c>CREATE TABLE</c> 전부가 <see cref="RestoreTableClassification"/> 두 목록 중 <b>정확히 하나</b>에 있어야 초록이다.
/// 새 표를 만들고 분류를 안 하면 빨강 — 「나머지 전부 = 백업 값」 기본값이 없어서다(CTO F5).</para>
/// <para>DB 쪽(실제 <c>information_schema</c> 대조)은 <see cref="RestoreTableClassificationGateDbTests"/> — CI db-gate 잡 몫.
/// 이 클래스는 DB 를 열지 않는다(SKIP 0 · 어디서나 돈다).</para>
/// </remarks>
public sealed class RestoreTableClassificationGateTests
{
    // ══ 읽기 도우미 ══

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Join(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 시험이 출하 DDL 을 읽을 수 없다.");
    }

    private static string CleanDdlText()
    {
        var path = Path.Join(RepoRoot(), "installer", "hitpan_db_clean.sql");
        Assert.True(File.Exists(path), $"출하 DDL 이 없다: {path}");
        return File.ReadAllText(path);
    }

    private static IEnumerable<(string File, string Text)> MigrationTexts()
    {
        var dir = Path.Join(RepoRoot(), "src", "HitPan.API", "Migrations", "SQL");
        Assert.True(Directory.Exists(dir), $"마이그 폴더가 없다: {dir}");
        var files = Directory.GetFiles(dir, "*.sql").OrderBy(f => f, StringComparer.Ordinal).ToList();
        Assert.True(files.Count > 0, $"마이그 폴더에 .sql 이 0개다: {dir} — 0건 no-op 초록을 막는다.");
        return files.Select(f => (Path.GetFileName(f), File.ReadAllText(f)));
    }

    /// <summary>
    /// 주석을 걷는다 — <c>-- …</c> 줄 · <c>/* … */</c>(단 MariaDB 판 주석 <c>/*!…*/</c> 은 실제로 도는 글이라 남김).
    /// 마이그 머리말의 「예: CREATE TABLE x」 같은 글이 표로 세어지지 않게.
    /// </summary>
    internal static string StripComments(string sql)
    {
        var noBlock = Regex.Replace(sql, @"/\*(?!!).*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(noBlock, @"(?m)^\s*--.*$|(?m)\s--\s.*$", "");
    }

    private static readonly Regex CreateTableRx = new(
        @"\bCREATE\s+(?:TEMPORARY\s+)?TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?`?(?<n>[A-Za-z0-9_]+)`?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TriggerRx = new(
        @"\bTRIGGER\s+`?[A-Za-z0-9_]+`?\s+(?:BEFORE|AFTER)\s+(?:INSERT|UPDATE|DELETE)\s+ON\s+`?(?<t>[A-Za-z0-9_]+)`?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>글 속 <c>CREATE TABLE</c> 이름들(소문자 · 중복 제거).</summary>
    internal static SortedSet<string> CreatedTables(string sql) =>
        new(CreateTableRx.Matches(StripComments(sql)).Select(m => m.Groups["n"].Value.ToLowerInvariant()), StringComparer.Ordinal);

    /// <summary>글 속 트리거가 걸린 표 이름들(소문자).</summary>
    internal static SortedSet<string> TriggerTables(string sql) =>
        new(TriggerRx.Matches(StripComments(sql)).Select(m => m.Groups["t"].Value.ToLowerInvariant()), StringComparer.Ordinal);

    /// <summary>출하 DDL 의 외래키(자식 → 부모). mariadb-dump 꼴: <c>CREATE TABLE `x` (</c> 블록 안 <c>REFERENCES `y`</c>.</summary>
    internal static List<(string Child, string Parent)> CleanDdlForeignKeys(string ddl)
    {
        var list = new List<(string, string)>();
        string? current = null;
        foreach (var raw in ddl.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var ct = Regex.Match(line, @"^CREATE TABLE `(?<n>[^`]+)`");
            if (ct.Success) { current = ct.Groups["n"].Value.ToLowerInvariant(); continue; }
            if (line.StartsWith(") ENGINE", StringComparison.Ordinal)) { current = null; continue; }
            if (current is null) continue;
            var fk = Regex.Match(line, @"REFERENCES `(?<p>[^`]+)`");
            if (fk.Success) list.Add((current, fk.Groups["p"].Value.ToLowerInvariant()));
        }
        return list;
    }

    /// <summary>「백업 값」 사이 외래키 그래프의 고리(자기 참조 포함 · G13 ⓓ). 없으면 빈 목록.</summary>
    internal static List<string> FindCycles(IEnumerable<(string Child, string Parent)> edges)
    {
        var graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (c, p) in edges)
        {
            if (!graph.TryGetValue(c, out var l)) graph[c] = l = new List<string>();
            l.Add(p);
        }
        var cycles = new List<string>();
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // 1 = 도는 중 · 2 = 끝
        var stack = new List<string>();
        void Visit(string n)
        {
            state[n] = 1; stack.Add(n);
            foreach (var m in graph.TryGetValue(n, out var next) ? next : new List<string>())
            {
                state.TryGetValue(m, out var s);
                if (s == 1) cycles.Add(string.Join(" → ", stack.Skip(stack.FindIndex(x => x.Equals(m, StringComparison.OrdinalIgnoreCase)))) + " → " + m);
                else if (s == 0) Visit(m);
            }
            stack.RemoveAt(stack.Count - 1); state[n] = 2;
        }
        foreach (var n in graph.Keys.ToList())
            if (!state.ContainsKey(n)) Visit(n);
        return cycles;
    }

    private static string Report(string title, IEnumerable<string> names) =>
        $"{title}({names.Count()}개): {string.Join(", ", names)}";

    // ══ G13-S1 — 출하 DDL 전부 정확히 하나 ══

    [Fact(DisplayName = "G13-S1 🔴 출하 DDL 의 CREATE TABLE 전부가 두 목록 중 정확히 하나 (ⓐ · 새 표 분류 안 하면 빨강)")]
    public void G13_S1_출하DDL_전수_정확히하나()
    {
        var tables = CreatedTables(CleanDdlText());
        Assert.True(tables.Count >= 100, $"출하 DDL 에서 표를 {tables.Count}개만 읽었다 — 읽기가 깨졌다(0건 no-op 초록 막음).");

        var missing = tables.Where(t => !RestoreTableClassification.KeepCurrent.Contains(t)
                                     && !RestoreTableClassification.FromBackup.Contains(t)).ToList();
        var both = tables.Where(t => RestoreTableClassification.KeepCurrent.Contains(t)
                                  && RestoreTableClassification.FromBackup.Contains(t)).ToList();

        Assert.True(missing.Count == 0,
            Report("🔴 분류 안 된 표 — RestoreTableClassification 의 「지금 값」 · 「백업 값」 중 하나에 더하라(기본값 없음)", missing));
        Assert.True(both.Count == 0, Report("🔴 두 목록에 다 있는 표 — 하나만 남겨라", both));
        Assert.All(tables, t => Assert.NotEqual(RestoreTableClass.Unclassified, RestoreTableClassification.Classify(t)));
    }

    // ══ G13-S2 — 마이그 폴더 전부 정확히 하나 ══

    [Fact(DisplayName = "G13-S2 🔴 마이그 폴더의 CREATE TABLE 전부가 두 목록 중 정확히 하나 (옛 고객 DB 에 남은 표 포함)")]
    public void G13_S2_마이그_전수_정확히하나()
    {
        var bad = new List<string>();
        foreach (var (file, text) in MigrationTexts())
            foreach (var t in CreatedTables(text))
                if (RestoreTableClassification.Classify(t) == RestoreTableClass.Unclassified)
                    bad.Add($"{t}({file})");

        Assert.True(bad.Count == 0,
            Report("🔴 마이그가 만드는데 분류 안 된(또는 두 목록에 다 있는) 표 — 옛 고객 PC 에서 복원 걸음 ① 이 늘 멈춘다", bad));
    }

    // ══ G13-S3 — 목록에 죽은 이름 0 ══

    [Fact(DisplayName = "G13-S3 🔴 두 목록의 이름은 전부 출하 DDL 또는 마이그 폴더에 실제로 있다 (오타 · 지운 표 0)")]
    public void G13_S3_죽은이름_0()
    {
        var known = new HashSet<string>(CreatedTables(CleanDdlText()), StringComparer.OrdinalIgnoreCase);
        foreach (var (_, text) in MigrationTexts()) known.UnionWith(CreatedTables(text));

        var dead = RestoreTableClassification.KeepCurrentNames
            .Concat(RestoreTableClassification.FromBackupNames)
            .Where(n => !known.Contains(n)).ToList();

        Assert.True(dead.Count == 0, Report("🔴 어디에도 없는 표 이름 — 오타면 그 표가 분류 밖으로 샌다", dead));
    }

    // ══ G13-S4 — 배열 자체에 겹침 0 ══

    [Fact(DisplayName = "G13-S4 🔴 한 목록 안 같은 이름 두 번 0 · 두 목록 사이 겹침 0")]
    public void G13_S4_겹침_0()
    {
        static List<string> Dups(IEnumerable<string> a) =>
            a.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

        Assert.True(Dups(RestoreTableClassification.KeepCurrentNames).Count == 0,
            Report("🔴 「지금 값」 안 중복", Dups(RestoreTableClassification.KeepCurrentNames)));
        Assert.True(Dups(RestoreTableClassification.FromBackupNames).Count == 0,
            Report("🔴 「백업 값」 안 중복", Dups(RestoreTableClassification.FromBackupNames)));

        var cross = RestoreTableClassification.KeepCurrentNames
            .Intersect(RestoreTableClassification.FromBackupNames, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.True(cross.Count == 0, Report("🔴 두 목록에 다 있는 표", cross));
        Assert.All(cross, t => Assert.Equal(RestoreTableClass.Unclassified, RestoreTableClassification.Classify(t)));
    }

    // ══ G13-S5 — 이름 길이 · 예약 접두 ══

    [Fact(DisplayName = "G13-S5 🔴 분류 표 이름 ≤ 56자(접두 8 + 64 한도 · ⓖ) · 예약 접두 rs_new__ 로 시작하는 분류 이름 0 (ⓑ)")]
    public void G13_S5_이름길이_예약접두()
    {
        Assert.Equal(8, RestoreTableClassification.StagingPrefix.Length);
        Assert.True(RestoreTableClassification.StagingPrefix.Length + RestoreTableClassification.MaxClassifiedNameLength <= 64);

        var all = RestoreTableClassification.KeepCurrentNames.Concat(RestoreTableClassification.FromBackupNames).ToList();
        var tooLong = all.Where(n => n.Length > RestoreTableClassification.MaxClassifiedNameLength).ToList();
        var reserved = all.Where(n => n.StartsWith(RestoreTableClassification.StagingPrefix, StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.True(tooLong.Count == 0, Report("🔴 56자 넘는 표 — rs_new__ 를 붙이면 64자 한도를 넘는다", tooLong));
        Assert.True(reserved.Count == 0, Report("🔴 임시 접두로 시작하는 분류 이름", reserved));

        // 임시 표가 남아 있으면 판정 함수가 멈춤 쪽으로 돌려준다(동작)
        Assert.Equal(RestoreTableClass.Unclassified, RestoreTableClassification.Classify("rs_new__stock_ledger"));
    }

    // ══ G13-S6 — 외래키 방향 · 고리(출하 DDL) ══

    [Fact(DisplayName = "G13-S6 🔴 출하 DDL 외래키 — 「지금 값」 자식 → 「백업 값」 부모 0 (ⓒ) · 「백업 값」 사이 고리 0(자기 참조 포함 · ⓓ)")]
    public void G13_S6_외래키_방향_고리()
    {
        var fks = CleanDdlForeignKeys(CleanDdlText());
        Assert.True(fks.Count >= 20, $"출하 DDL 외래키를 {fks.Count}개만 읽었다 — 읽기가 깨졌다.");

        var wrongWay = fks.Where(e => RestoreTableClassification.Classify(e.Child) == RestoreTableClass.KeepCurrent
                                   && RestoreTableClassification.Classify(e.Parent) == RestoreTableClass.FromBackup)
                          .Select(e => $"{e.Child} → {e.Parent}").ToList();
        Assert.True(wrongWay.Count == 0,
            Report("🔴 지금 값 표가 백업 값 표를 외래키로 가리킴 — 교체(지우고 붓기) 때 지금 값 표가 끊긴다", wrongWay));

        var backupEdges = fks.Where(e => RestoreTableClassification.Classify(e.Child) == RestoreTableClass.FromBackup
                                      && RestoreTableClassification.Classify(e.Parent) == RestoreTableClass.FromBackup);
        var cycles = FindCycles(backupEdges);
        Assert.True(cycles.Count == 0, Report("🔴 백업 값 외래키 고리 — 지우는 순서 · 붓는 순서가 안 선다", cycles));
    }

    [Fact(DisplayName = "G13-S6b 고리 찾기 자체 시험 — 자기 참조 · 둘 사이 고리를 잡고 · 사슬은 안 잡는다")]
    public void G13_S6b_고리찾기_자체()
    {
        Assert.NotEmpty(FindCycles([("a", "a")]));
        Assert.NotEmpty(FindCycles([("a", "b"), ("b", "a")]));
        Assert.Empty(FindCycles([("a", "b"), ("b", "c"), ("a", "c")]));
    }

    // ══ G13-S7 — 트리거 표 ══

    [Fact(DisplayName = "G13-S7 🔴 출하 DDL · 마이그의 트리거 걸린 표 전부가 아는 목록 안 (ⓔ · 새 트리거 = 설계 다시 봄)")]
    public void G13_S7_트리거표_아는목록()
    {
        var found = new SortedSet<string>(TriggerTables(CleanDdlText()), StringComparer.Ordinal);
        Assert.True(found.Count >= 2, $"출하 DDL 트리거 표를 {found.Count}개만 읽었다 — 읽기가 깨졌다(지금 2 = partner_special_prices · tax_invoices).");
        foreach (var (_, text) in MigrationTexts()) found.UnionWith(TriggerTables(text));

        var unknown = found.Where(t => !RestoreTableClassification.KnownTriggerTables.Contains(t)).ToList();
        Assert.True(unknown.Count == 0, Report("🔴 아는 목록 밖 트리거 표 — 교체 걸음이 트리거를 밟는지 설계를 다시 보라", unknown));
    }

    // ══ G13-S8 — 기본값 0 · 결재된 자리 ══

    [Fact(DisplayName = "G13-S8 🔴 기본값 0 — 모르는 표 · 빈 이름 = 분류 안 됨 · FindUnclassified 가 그것만 돌려줌")]
    public void G13_S8_기본값_0()
    {
        Assert.Equal(RestoreTableClass.Unclassified, default(RestoreTableClass));
        Assert.Equal(0, (int)RestoreTableClass.Unclassified);
        Assert.Equal(RestoreTableClass.Unclassified, RestoreTableClassification.Classify("brand_new_table_nobody_classified"));
        Assert.Equal(RestoreTableClass.Unclassified, RestoreTableClassification.Classify(""));

        var found = RestoreTableClassification.FindUnclassified(["users", "stock_ledger", "brand_new_table_x", "USERS", "brand_new_table_x"]);
        Assert.Equal(["brand_new_table_x"], found);
    }

    [Theory(DisplayName = "G13-S9 🔴 결재된 자리 — 계정 · 회사정보 · 수신 캐시 · 로그 · 1회용 = 지금 값 / 장부 · K3 · K12 · 외래키 강제 = 백업 값")]
    [InlineData("users", RestoreTableClass.KeepCurrent)]
    [InlineData("refresh_tokens", RestoreTableClass.KeepCurrent)]
    [InlineData("tenants", RestoreTableClass.KeepCurrent)]
    [InlineData("local_subscription", RestoreTableClass.KeepCurrent)]
    [InlineData("tenant_devices", RestoreTableClass.KeepCurrent)]
    [InlineData("audit_logs", RestoreTableClass.KeepCurrent)]
    [InlineData("backup_history", RestoreTableClass.KeepCurrent)]
    [InlineData("restore_history", RestoreTableClass.KeepCurrent)]
    [InlineData("schema_migrations", RestoreTableClass.KeepCurrent)]
    [InlineData("device_register_tokens", RestoreTableClass.KeepCurrent)]
    [InlineData("stock_ledger", RestoreTableClass.FromBackup)]
    [InlineData("journal_lines", RestoreTableClass.FromBackup)]
    [InlineData("stock_adjust_logs", RestoreTableClass.FromBackup)]
    [InlineData("haccp_logs", RestoreTableClass.FromBackup)]
    [InlineData("mold_production_log", RestoreTableClass.FromBackup)]
    [InlineData("idempotency_keys", RestoreTableClass.FromBackup)]
    [InlineData("etax_send_history", RestoreTableClass.FromBackup)]
    [InlineData("employees", RestoreTableClass.FromBackup)]
    public void G13_S9_결재된자리(string table, RestoreTableClass expected)
    {
        Assert.Equal(expected, RestoreTableClassification.Classify(table));
        Assert.Equal(expected, RestoreTableClassification.Classify(table.ToUpperInvariant()));
    }

    // ══ 읽기 도우미 자체 ══

    [Fact(DisplayName = "G13-S10 읽기 도우미 — 주석 속 CREATE TABLE 은 안 세고 · 판 주석(/*!…*/)과 IF NOT EXISTS · 따옴표는 셈")]
    public void G13_S10_읽기도우미()
    {
        const string sql = "-- CREATE TABLE in_line_comment (x int);\n"
                         + "/* CREATE TABLE in_block_comment (x int); */\n"
                         + "CREATE TABLE IF NOT EXISTS `Real_One` (x int);\n"
                         + "create table real_two (x int); -- CREATE TABLE trailing_comment\n"
                         + "/*!50001 CREATE TABLE versioned_one (x int)*/;\n";
        Assert.Equal(["real_one", "real_two", "versioned_one"], CreatedTables(sql).ToList());
    }
}
