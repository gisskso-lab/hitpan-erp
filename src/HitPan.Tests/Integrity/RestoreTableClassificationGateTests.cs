using System.Text.RegularExpressions;
using HitPan.Application.DTOs.Backup;
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

    // 고침 1 Y2 ([4] P2-② · [3-V] P2-1) — 옛 줄: `CREATE OR REPLACE TABLE x`(MariaDB 문법)를 못 봄
    // private static readonly Regex CreateTableRx = new(
    //     @"\bCREATE\s+(?:TEMPORARY\s+)?TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?`?(?<n>[A-Za-z0-9_]+)`?",
    //     RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CreateTableRx = new(
        @"\bCREATE\s+(?:OR\s+REPLACE\s+)?(?:TEMPORARY\s+)?TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?`?(?<n>[A-Za-z0-9_]+)`?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 고침 1 Y1 ([4] P2-① · V3) — 옛 줄: `CREATE TRIGGER IF NOT EXISTS t …` 를 못 봄(TRIGGER 바로 뒤 이름을 요구)
    // private static readonly Regex TriggerRx = new(
    //     @"\bTRIGGER\s+`?[A-Za-z0-9_]+`?\s+(?:BEFORE|AFTER)\s+(?:INSERT|UPDATE|DELETE)\s+ON\s+`?(?<t>[A-Za-z0-9_]+)`?",
    //     RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TriggerRx = new(
        @"\bTRIGGER\s+(?:IF\s+NOT\s+EXISTS\s+)?`?[A-Za-z0-9_]+`?\s+(?:BEFORE|AFTER)\s+(?:INSERT|UPDATE|DELETE)\s+ON\s+`?(?<t>[A-Za-z0-9_]+)`?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>고침 1 Y2 — <c>RENAME TABLE a TO b[, c TO d …]</c> 의 글 덩어리(첫 <c>;</c> 까지).</summary>
    private static readonly Regex RenameTableRx = new(
        @"\bRENAME\s+TABLES?\s+(?<body>[^;]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>고침 1 Y2 — RENAME TABLE 덩어리 안 <c>옛이름 TO 새이름</c> 짝의 새 이름.</summary>
    private static readonly Regex RenamePairRx = new(
        @"`?[A-Za-z0-9_.]+`?\s+TO\s+`?(?<n>[A-Za-z0-9_]+)`?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 고침 1 Y2 — <c>ALTER TABLE a … RENAME [TO|AS] b</c> 의 새 이름.
    /// <c>RENAME COLUMN</c> · <c>RENAME INDEX</c> · <c>RENAME KEY</c> · <c>RENAME CONSTRAINT</c>(칸 · 인덱스 이름 바꾸기)는 표가 아니라 안 셈.
    /// </summary>
    private static readonly Regex AlterRenameRx = new(
        @"\bALTER\s+(?:ONLINE\s+)?(?:IGNORE\s+)?TABLE\s+[^;]*?\bRENAME\s+(?:TO\s+|AS\s+)?(?!(?:COLUMN|INDEX|KEY|CONSTRAINT)\b)`?(?<n>[A-Za-z0-9_]+)`?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>글 속 <c>CREATE TABLE</c> 이름들(소문자 · 중복 제거).</summary>
    internal static SortedSet<string> CreatedTables(string sql) =>
        new(CreateTableRx.Matches(StripComments(sql)).Select(m => m.Groups["n"].Value.ToLowerInvariant()), StringComparer.Ordinal);

    /// <summary>
    /// 고침 1 Y2 — 이름 바꾸기로 생기는 새 표 이름들(<c>RENAME TABLE</c> · <c>ALTER TABLE … RENAME TO</c> · 소문자 · 중복 제거).
    /// 실례: <c>DB-09</c> :32 <c>RENAME TABLE tenants TO tenants_old, tenants_rebuilt TO tenants</c> → <c>tenants_old</c> · <c>tenants</c>.
    /// </summary>
    internal static SortedSet<string> RenamedTables(string sql)
    {
        var text = StripComments(sql);
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match m in RenameTableRx.Matches(text))
            foreach (Match p in RenamePairRx.Matches(m.Groups["body"].Value))
                set.Add(p.Groups["n"].Value.ToLowerInvariant());
        foreach (Match m in AlterRenameRx.Matches(text))
            set.Add(m.Groups["n"].Value.ToLowerInvariant());
        return set;
    }

    /// <summary>고침 1 Y2 — 글이 만드는 표 전부 = <c>CREATE [OR REPLACE] TABLE</c> ∪ 이름 바꾸기 새 이름.</summary>
    internal static SortedSet<string> DefinedTables(string sql)
    {
        var set = CreatedTables(sql);
        set.UnionWith(RenamedTables(sql));
        return set;
    }

    /// <summary>
    /// 고침 1 Y3 ([3-V] P2-1 ③) — 제품 C# 코드의 <c>CREATE TABLE</c>. <c>SHOW CREATE TABLE</c> 은 안 셈.
    /// 이름 자리는 넓게 잡고(<c>[^\s(;]+</c>) 뒤에서 거른다 — <c>"… TABLE " + x</c> · <c>{name}</c> · <c>db.t</c> 같은 못 읽는 이름을 빨강으로 보이려고.
    /// </summary>
    private static readonly Regex CsCreateTableRx = new(
        @"(?<!\bSHOW\s+)\bCREATE\s+(?:OR\s+REPLACE\s+)?(?<tmp>TEMPORARY\s+)?TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(?<n>[^\s(;]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PlainNameRx = new(@"^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    /// <summary>
    /// C# 줄 주석(<c>// …</c> · <c>/// …</c>)만 걷는다. <c>https://</c> 처럼 앞에 빈칸이 없는 <c>//</c> 는 안 걷음.
    /// <c>/* … */</c> 는 걷지 않는다 — 글자 속 <c>"image/*"</c> 같은 것이 주석 시작으로 읽혀 뒤 코드를 삼키면 표를 놓친다(초록 쪽 위험).
    /// 블록 주석 속 <c>CREATE TABLE</c> 은 세어져 빨강 쪽(멈춤 쪽)으로만 틀린다.
    /// </summary>
    internal static string StripCsComments(string cs) =>
        Regex.Replace(cs, @"(?m)(?:^|(?<=\s))//.*$", "");

    /// <summary>
    /// 고침 1 Y3 — C# 글 속 영구 표 <c>CREATE TABLE</c>: 읽은 이름(소문자) · 못 읽는 이름(그대로) · 세션 임시 표 수(TEMPORARY = 연결이 끝나면 사라짐 · 분류 밖).
    /// </summary>
    internal static (SortedSet<string> Names, List<string> Unreadable, int Temporary) CsCreatedTables(string cs)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        var unreadable = new List<string>();
        var temporary = 0;
        foreach (Match m in CsCreateTableRx.Matches(StripCsComments(cs)))
        {
            if (m.Groups["tmp"].Success) { temporary++; continue; }
            var raw = m.Groups["n"].Value;
            if (IsStagingTemplate(raw)) continue;   // 🔴 A-2③ D6 — 변환기 임시 표 틀(동작으로 확인한 것만)
            var name = raw.Trim('`', '"', '\'', ',');
            if (PlainNameRx.IsMatch(name)) names.Add(name.ToLowerInvariant());
            else unreadable.Add(raw);
        }
        return (names, unreadable, temporary);
    }

    /// <summary>
    /// 🔴 W-G1-52 갈래 A-2③ D6 — 「변환기의 임시 표 틀(<c>rs_new__</c>) = 영구 표 아님」 을 <b>동작으로</b> 뺀다(글자 나눔에 기대지 않음).
    /// 이름 자리가 <c>rs_new__</c> 또는 그 상수 자리표(<c>{Prefix}</c> · <c>{RestoreDumpConverter.Prefix}</c> · <c>{RestoreTableClassification.StagingPrefix}</c>)로
    /// 시작하고, <see cref="StagingTemplateProven"/>(작은 덤프를 변환기로 실제로 바꿔 나온 CREATE TABLE 이 전부 <c>rs_new__</c> 로 시작)이 참일 때만.
    /// </summary>
    internal static bool IsStagingTemplate(string raw)
    {
        var name = raw.Trim('`', '"', '\'', ',', '$');
        string[] holders = ["{Prefix}", "{RestoreDumpConverter.Prefix}", "{RestoreTableClassification.StagingPrefix}"];
        var looks = name.StartsWith(RestoreTableClassification.StagingPrefix, StringComparison.Ordinal)
                    || holders.Any(h => name.StartsWith(h, StringComparison.Ordinal));
        return looks && StagingTemplateProven.Value;
    }

    /// <summary>D6 동작 확인 — 변환기 상수 = 분류 접두 · 작은 덤프의 「백업 값」 표 2개가 모두 <c>rs_new__</c> 이름으로만 나옴.</summary>
    internal static readonly Lazy<bool> StagingTemplateProven = new(() =>
    {
        if (!string.Equals(RestoreDumpConverter.Prefix, RestoreTableClassification.StagingPrefix, StringComparison.Ordinal)) return false;
        const string dump =
            "DROP TABLE IF EXISTS `partner_contacts`;\n"
            + "CREATE TABLE `partner_contacts` (\n  `contact_id` varchar(36) NOT NULL,\n  `tenant_id` varchar(36) NOT NULL,\n  PRIMARY KEY (`contact_id`)\n) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;\n"
            + "DROP TABLE IF EXISTS `items`;\n"
            + "CREATE TABLE `items` (\n  `item_id` varchar(36) NOT NULL,\n  PRIMARY KEY (`item_id`)\n) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;\n";
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(dump));
        var created = RestoreDumpConverter.Convert(ms, n => RestoreTableClassification.Classify(n) == RestoreTableClass.FromBackup
                ? RestoreStageDecision.Stage : RestoreStageDecision.Skip)
            .Select(x => System.Text.Encoding.UTF8.GetString(x.Sql))
            .SelectMany(t => Regex.Matches(t, @"\bCREATE\s+TABLE\s+`(?<n>[^`]+)`").Select(m => m.Groups["n"].Value))
            .ToList();
        return created.Count == 2 && created.All(n => n.StartsWith(RestoreTableClassification.StagingPrefix, StringComparison.Ordinal));
    });

    /// <summary>
    /// 고침 1 Y3 — ERP 고객 DB 에 닿는 제품 C# 파일(<c>src/</c> 아래). 시험 프로젝트(이름에 <c>Tests</c>) ·
    /// 백오피스(<c>HitPan.Backoffice*</c> — 본사 NCP 의 다른 DB · 복원 대상 아님) · 랜딩(<c>HitPan.Landing</c>) · <c>bin</c> · <c>obj</c> 제외.
    /// </summary>
    private static IEnumerable<(string File, string Text)> ProductCsTexts()
    {
        var src = Path.Join(RepoRoot(), "src");
        var list = new List<(string, string)>();
        foreach (var projDir in Directory.GetDirectories(src).OrderBy(d => d, StringComparer.Ordinal))
        {
            var proj = Path.GetFileName(projDir);
            if (proj.Contains("Tests", StringComparison.OrdinalIgnoreCase)
                || proj.StartsWith("HitPan.Backoffice", StringComparison.OrdinalIgnoreCase)
                || proj.StartsWith("HitPan.Landing", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var f in Directory.GetFiles(projDir, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                var rel = Path.GetRelativePath(src, f).Replace('\\', '/');
                if (rel.Contains("/bin/", StringComparison.Ordinal) || rel.Contains("/obj/", StringComparison.Ordinal)) continue;
                list.Add((rel, File.ReadAllText(f)));
            }
        }
        Assert.True(list.Count >= 100, $"제품 C# 파일을 {list.Count}개만 읽었다 — 읽기가 깨졌다(0건 no-op 초록 막음).");
        return list;
    }

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
        // 고침 1 Y2 — 옛 줄: var tables = CreatedTables(CleanDdlText());  (이름 바꾸기 새 이름 · CREATE OR REPLACE 를 안 셈)
        var tables = DefinedTables(CleanDdlText());
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
            // 고침 1 Y2 — 옛 줄: foreach (var t in CreatedTables(text))  (RENAME TABLE · ALTER … RENAME TO 새 이름을 안 셈 · [4] V4)
            foreach (var t in DefinedTables(text))
                if (RestoreTableClassification.Classify(t) == RestoreTableClass.Unclassified)
                    bad.Add($"{t}({file})");

        Assert.True(bad.Count == 0,
            Report("🔴 마이그가 만드는데 분류 안 된(또는 두 목록에 다 있는) 표 — 옛 고객 PC 에서 복원 걸음 ① 이 늘 멈춘다", bad));
    }

    // ══ G13-S3 — 목록에 죽은 이름 0 ══

    [Fact(DisplayName = "G13-S3 🔴 두 목록의 이름은 전부 출하 DDL 또는 마이그 폴더에 실제로 있다 (오타 · 지운 표 0)")]
    public void G13_S3_죽은이름_0()
    {
        // 고침 1 Y2 · Y3 — 옛 두 줄(CREATE TABLE 만 셈):
        // var known = new HashSet<string>(CreatedTables(CleanDdlText()), StringComparer.OrdinalIgnoreCase);
        // foreach (var (_, text) in MigrationTexts()) known.UnionWith(CreatedTables(text));
        var known = new HashSet<string>(DefinedTables(CleanDdlText()), StringComparer.OrdinalIgnoreCase);
        foreach (var (_, text) in MigrationTexts()) known.UnionWith(DefinedTables(text));
        foreach (var (_, text) in ProductCsTexts()) known.UnionWith(CsCreatedTables(text).Names);

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
    // 고침 1 Y6 ([3-V] P3-5) — licenses = 지금도 쓰는 인증 표(WatchdogBearerMiddleware 가 토큰 해시를 읽음). 백업 값이면 폐기 토큰이 되살아남
    [InlineData("licenses", RestoreTableClass.KeepCurrent)]
    // 고침 1 Y2 — DB-09 이름 바꾸기 도중 사망 자리 두 짝(tenants_rebuilt · tenants_old)은 같은 쪽
    [InlineData("tenants_rebuilt", RestoreTableClass.KeepCurrent)]
    [InlineData("tenants_old", RestoreTableClass.KeepCurrent)]
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

    // ══ 고침 1 — 읽기 도우미 자체(Y1 · Y2 · Y3) ══

    [Fact(DisplayName = "G13-S10b 읽기 도우미(고침 1) — CREATE OR REPLACE · RENAME TABLE · ALTER … RENAME TO 는 셈 · RENAME COLUMN/INDEX 는 안 셈 · 트리거 IF NOT EXISTS 셈")]
    public void G13_S10b_읽기도우미_고침1()
    {
        // Y2 — CREATE OR REPLACE
        Assert.Equal(["cor_one"], CreatedTables("CREATE OR REPLACE TABLE `cor_one` (x int);").ToList());

        // Y2 — 이름 바꾸기
        const string ren = "RENAME TABLE tenants TO tenants_old, `tenants_rebuilt` TO `tenants`;\n"
                         + "ALTER TABLE items RENAME TO zz_two;\n"
                         + "alter table a2 rename as zz_three;\n"
                         + "ALTER TABLE b2 ADD COLUMN c int, RENAME zz_four;\n"
                         + "ALTER TABLE c2 RENAME COLUMN old_c TO new_c;\n"
                         + "ALTER TABLE d2 RENAME INDEX ix_a TO ix_b;\n"
                         + "ALTER TABLE e2 RENAME KEY k_a TO k_b;\n"
                         + "-- RENAME TABLE in_comment TO zz_comment;\n";
        Assert.Equal(["tenants", "tenants_old", "zz_four", "zz_three", "zz_two"], RenamedTables(ren).ToList());
        Assert.Contains("tenants_old", DefinedTables("CREATE TABLE tenants_rebuilt (x int);\n" + ren));

        // Y1 — 트리거 IF NOT EXISTS
        Assert.Equal(["stock_ledger"],
            TriggerTables("CREATE TRIGGER IF NOT EXISTS trg_zz BEFORE INSERT ON stock_ledger FOR EACH ROW SET @a = 1;").ToList());
        Assert.Equal(["tax_invoices"],
            TriggerTables("CREATE DEFINER=`root`@`localhost` TRIGGER `trg_t` AFTER UPDATE ON `tax_invoices` FOR EACH ROW SET @a = 1;").ToList());

        // Y3 — C# 읽기: 주석 · SHOW CREATE TABLE · TEMPORARY 는 영구 표가 아님 · 동적 · 점 이름은 못 읽는 이름
        const string cs = "// CREATE TABLE IF NOT EXISTS(in_comment)\n"
                        + "/// CREATE TABLE doc_comment (x int)\n"
                        + "var a = \"CREATE TABLE IF NOT EXISTS `Cs_One` (\" + \"x int)\";\n"
                        + "var b = @\"create table cs_two (x int)\";\n"
                        + "var c = $\"CREATE TEMPORARY TABLE `{stage}` LIKE stock_ledger\";\n"
                        + "var d = $\"SHOW CREATE TABLE `{stage}`\";\n"
                        + "var e = \"CREATE TABLE \" + dyn;\n"
                        + "var f = \"CREATE TABLE other_db.zz (x int)\";\n"
                        + "var g = \"https://x.example/CREATE\";\n";
        var r = CsCreatedTables(cs);
        Assert.Equal(["cs_one", "cs_two"], r.Names.ToList());
        Assert.Equal(1, r.Temporary);
        Assert.Equal(2, r.Unreadable.Count);
    }

    // ══ 고침 1 Y3 — 제품 C# 코드의 CREATE TABLE ══

    [Fact(DisplayName = "G13-S11 🔴 제품 C# 코드(src/ · 시험 · 백오피스 · 랜딩 제외)의 영구 CREATE TABLE 전부가 두 목록 중 정확히 하나 · 못 읽는 이름 0")]
    public void G13_S11_제품CS_전수_정확히하나()
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        var bad = new List<string>();
        var unreadable = new List<string>();
        foreach (var (file, text) in ProductCsTexts())
        {
            var r = CsCreatedTables(text);
            foreach (var t in r.Names)
            {
                found.Add(t);
                if (RestoreTableClassification.Classify(t) == RestoreTableClass.Unclassified) bad.Add($"{t}({file})");
            }
            unreadable.AddRange(r.Unreadable.Select(u => $"{u}({file})"));
        }

        // 지금 3(schema_migrations = MigrationRunner · local_update_attempts · local_update_apply_status = 워치독 자가생성)
        Assert.True(found.Contains("schema_migrations"),
            $"C# 에서 schema_migrations(MigrationRunner)를 못 읽었다 — 읽기가 깨졌다. 읽은 것: {string.Join(", ", found)}");
        Assert.True(unreadable.Count == 0,
            Report("🔴 C# 의 CREATE TABLE 이름을 못 읽음(동적 · 다른 DB 이름) — 고정 이름으로 쓰고 분류하라", unreadable));
        Assert.True(bad.Count == 0,
            Report("🔴 C# 코드가 만드는데 분류 안 된(또는 두 목록에 다 있는) 표 — 고객 PC 에서 복원 걸음 ① 이 늘 멈춘다", bad));
    }

    // ══ 고침 1 Y4 · Y5 — 안전 복원 계약 기본값(실패 쪽) ══

    [Fact(DisplayName = "G13-S12 🔴 안전 복원 계약 기본값 = 실패 쪽 — DataUnchanged · Selectable 기본 false · 잠김 남은 시간 · 마지막 결과 기본 null · 경로 · 회사 칸 0")]
    public void G13_S12_계약기본값_실패쪽()
    {
        var res = new SafeRestoreResponse();
        Assert.False(res.DataUnchanged);                 // Y4 — 서버가 확인했을 때만 true
        Assert.False(res.Success);
        Assert.Null(res.LockoutRemainingSeconds);        // Y5 — 잠김일 때만 값

        var cand = new SafeRestoreCandidateDto();
        Assert.False(cand.Selectable);                   // Y5 — 서버가 고를 수 있다고 확인한 것만 true
        Assert.Null(cand.DenyCode);

        var last = new SafeRestoreLastResultDto();
        Assert.False(last.DataUnchanged);
        Assert.False(last.Success);

        Assert.Null(new SafeRestoreStatusDto().LastResult);

        // 화면 계약에 경로 · 회사 칸 0(K9 · #2)
        foreach (var type in new[] { typeof(SafeRestoreCandidateDto), typeof(SafeRestoreResponse), typeof(SafeRestoreLastResultDto), typeof(SafeRestoreStatusDto) })
        {
            var leaks = type.GetProperties()
                .Where(p => p.Name.Contains("Path", StringComparison.OrdinalIgnoreCase)
                         || p.Name.Contains("Tenant", StringComparison.OrdinalIgnoreCase)
                         || p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Name).ToList();
            Assert.True(leaks.Count == 0, Report($"🔴 {type.Name} 에 경로 · 회사 · 비번 칸", leaks));
        }
    }
}
