using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261008작16 §D — <b>코드가 읽는 칸이 표에 있나</b> (G-COL-1 · G-COL-1n).
///
/// <para><b>왜 생겼나(2026-10-08 실사고)</b>: 운영 백오피스 화면 4개가 500 이었다.
/// 뿌리는 둘 다 「코드가 읽는 칸」과 「표의 칸」이 어긋난 것이다.
/// <list type="number">
/// <item><c>PromotionController</c> 는 legacy 칸 이름(<c>promo_code</c>·<c>title</c>·<c>use_count</c>·
///       <c>target_plan</c>·<c>status</c>)으로 <b>admin 모양 표</b>(<c>promotions</c>)를 읽었다 — 넉 달.</item>
/// <item><c>pricing_plans</c> 의 <c>price_display</c>·<c>max_pc_devices</c>·<c>max_mobile_devices</c> 는
///       6/11 커밋에서 정의가 사라졌는데 컨트롤러 4개가 그 칸을 계속 읽었다.</item>
/// </list>
/// 헌법 #13(새 SQL 전 DESCRIBE 의무)은 <b>사람의 의무로만</b> 있었고 한 번도 코드로 집행된 적이 없다.
/// 그래서 이 어긋남은 운영에서 화면을 처음 열기까지 <b>아무 소리도 내지 않았다</b>.</para>
///
/// <para><b>이 게이트가 무는 것 — 글자가 아니라 색인 대조</b>:
/// <c>installer/backoffice/*.sql</c>(백오피스 DDL 진실원)을 파싱해 <b>표 → 칸 색인</b>을 만들고
/// (<c>CREATE TABLE</c> 본문 + <c>ALTER TABLE … ADD COLUMN</c>),
/// 대상 컨트롤러의 SQL 문자열에서 <c>FROM</c>/<c>JOIN</c>/<c>INSERT INTO</c>/<c>UPDATE</c> 뒤 표 이름과
/// 그 문장이 쓰는 식별자를 뽑아 <b>색인에 없는 칸이 하나라도 있으면 FAIL</b> 한다.
/// 「그 글자가 어딘가 있나」를 묻지 않는다 — <b>그 표에 그 칸이 있나</b>를 묻는다.</para>
///
/// <para><b>1차 범위는 고정이다</b>(PM 결재 D-6): 이번 사고의 컨트롤러 <b>5개</b>
/// (<c>Promotion</c>·<c>PromotionsAdmin</c>·<c>PricingAdmin</c>·<c>LandingPublic</c>·<c>DeviceRegistration</c>).
/// 백오피스 컨트롤러 전체로 넓히는 것은 2차(별건) — 1차에서 전체를 켜면 동적 <c>sql +=</c>·별칭·서브쿼리
/// 오탐으로 CI 가 못 선다.</para>
///
/// <para><b>DB 가 없어도 돈다</b> — <c>build</c> 잡에서 먼저 소리를 낸다(조용한 SKIP 경로 0).</para>
///
/// <para>🔴 <b>봉합 전 FAIL 실측(2026-10-08 · 개발명세서 §4-1)</b>: 봉합 전 코드로 돌리면
/// 8칸이 전부 잡힌다 — <c>promotions</c> 쪽 5칸(<c>promo_code</c>·<c>title</c>·<c>use_count</c>·
/// <c>target_plan</c>·<c>status</c>) + <c>pricing_plans</c> 쪽 3칸(<c>price_display</c>·
/// <c>max_pc_devices</c>·<c>max_mobile_devices</c>).</para>
/// </summary>
public sealed class BackofficeColumnContractGateTests
{
    // ══════════════════════════════════════════════════════════════
    // 1차 과녁 — 이번 사고의 컨트롤러 5개 (PM 결재 D-6: 전체 확대는 2차)
    // ══════════════════════════════════════════════════════════════
    private static readonly string[] TargetControllers =
    {
        "PromotionController.cs",
        "PromotionsAdminController.cs",
        "PricingAdminController.cs",
        "LandingPublicController.cs",
        "DeviceRegistrationController.cs",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 대조할 DDL·컨트롤러를 읽을 수 없다.");
    }

    private static string BoSqlDir() => Path.Combine(RepoRoot(), "installer", "backoffice");

    private static string ControllerDir()
        => Path.Combine(RepoRoot(), "src", "HitPan.Backoffice.API", "Controllers");

    /// <summary>DDL 파일 집합을 읽는다(파일명 → 내용). G-COL-1n 은 이 사전을 손대 음성 대조군을 만든다.</summary>
    private static Dictionary<string, string> LoadDdl()
    {
        var dir = BoSqlDir();
        Assert.True(Directory.Exists(dir), $"백오피스 DDL 폴더가 없다: {dir}");
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in Directory.GetFiles(dir, "*.sql").OrderBy(f => f, StringComparer.Ordinal))
            map[Path.GetFileName(f)] = File.ReadAllText(f);
        Assert.NotEmpty(map);
        return map;
    }

    // ══════════════════════════════════════════════════════════════
    // 색인 — 표 → 칸 (CREATE TABLE 본문 + ALTER TABLE … ADD COLUMN)
    // ══════════════════════════════════════════════════════════════

    /// <summary>제약·키 줄은 칸이 아니다 — 이 머리말로 시작하는 정의는 건너뛴다.</summary>
    private static readonly HashSet<string> NotAColumn = new(StringComparer.OrdinalIgnoreCase)
    {
        "PRIMARY", "UNIQUE", "KEY", "INDEX", "CONSTRAINT", "FOREIGN", "CHECK", "FULLTEXT", "SPATIAL",
    };

    internal static Dictionary<string, HashSet<string>> BuildIndex(IReadOnlyDictionary<string, string> ddl)
    {
        var index = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        HashSet<string> Bucket(string table)
        {
            if (!index.TryGetValue(table, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                index[table] = set;
            }
            return set;
        }

        foreach (var text in ddl.Values)
        {
            var sql = StripComments(text);

            // 1) CREATE TABLE [IF NOT EXISTS] <표> ( … )
            foreach (Match m in Regex.Matches(
                         sql, @"CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?`?([A-Za-z_][A-Za-z0-9_]*)`?\s*\(",
                         RegexOptions.IgnoreCase))
            {
                var table = m.Groups[1].Value;
                var body = BalancedBody(sql, m.Index + m.Length - 1);
                if (body is null) continue;
                foreach (var part in SplitTopLevel(body))
                {
                    var head = Regex.Match(part.TrimStart(), @"^`?([A-Za-z_][A-Za-z0-9_]*)`?");
                    if (!head.Success) continue;
                    var name = head.Groups[1].Value;
                    if (NotAColumn.Contains(name)) continue;
                    Bucket(table).Add(name);
                }
            }

            // 2) ALTER TABLE <표> … ADD COLUMN [IF NOT EXISTS] <칸>
            foreach (Match m in Regex.Matches(
                         sql, @"ALTER\s+TABLE\s+`?([A-Za-z_][A-Za-z0-9_]*)`?(.*?);",
                         RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var table = m.Groups[1].Value;
                foreach (Match c in Regex.Matches(
                             m.Groups[2].Value,
                             @"ADD\s+COLUMN\s+(?:IF\s+NOT\s+EXISTS\s+)?`?([A-Za-z_][A-Za-z0-9_]*)`?",
                             RegexOptions.IgnoreCase))
                {
                    Bucket(table).Add(c.Groups[1].Value);
                }
            }
        }

        return index;
    }

    /// <summary>여는 괄호 위치에서 시작해 짝이 맞는 닫는 괄호까지의 본문. 못 찾으면 null.</summary>
    private static string? BalancedBody(string s, int openParen)
    {
        var depth = 0;
        for (var i = openParen; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')')
            {
                depth--;
                if (depth == 0) return s.Substring(openParen + 1, i - openParen - 1);
            }
        }
        return null;
    }

    /// <summary>괄호 깊이 0 의 콤마로만 쪼갠다(CHECK (x IN ('a','b')) 같은 안쪽 콤마를 안 센다).</summary>
    private static List<string> SplitTopLevel(string body)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        var depth = 0;
        foreach (var ch in body)
        {
            if (ch == '(') depth++;
            else if (ch == ')') depth--;
            if (ch == ',' && depth == 0) { parts.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(ch);
        }
        if (sb.Length > 0) parts.Add(sb.ToString());
        return parts;
    }

    private static string StripComments(string sql)
    {
        sql = Regex.Replace(sql, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        sql = Regex.Replace(sql, @"--[^\r\n]*", " ");
        return sql;
    }

    // ══════════════════════════════════════════════════════════════
    // 컨트롤러에서 SQL 문자열 뽑기
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// C# 원문에서 <c>@"…"</c> / <c>$@"…"</c> 축자 문자열과 보통 문자열 리터럴을 뽑는다.
    /// SQL 로 보이는 것(<c>SELECT</c>/<c>INSERT</c>/<c>UPDATE</c>/<c>DELETE</c> 로 시작하는 것)만 남긴다.
    /// </summary>
    internal static List<string> ExtractSqlLiterals(string source)
    {
        var found = new List<string>();
        var i = 0;
        while (i < source.Length)
        {
            var ch = source[i];

            // 한 줄 주석·블록 주석은 통째로 넘긴다(주석 안의 따옴표가 파서를 흔들지 않게).
            if (ch == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }
            if (ch == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? source.Length : end + 2;
                continue;
            }

            var verbatim = ch == '@' && i + 1 < source.Length && source[i + 1] == '"';
            var interpolatedVerbatim = ch == '$' && i + 2 < source.Length
                                       && source[i + 1] == '@' && source[i + 2] == '"';
            if (verbatim || interpolatedVerbatim)
            {
                var start = i + (verbatim ? 2 : 3);
                var sb = new StringBuilder();
                var j = start;
                while (j < source.Length)
                {
                    if (source[j] == '"')
                    {
                        if (j + 1 < source.Length && source[j + 1] == '"') { sb.Append('"'); j += 2; continue; }
                        break;
                    }
                    sb.Append(source[j]);
                    j++;
                }
                found.Add(sb.ToString());
                i = j + 1;
                continue;
            }

            if (ch == '"')
            {
                var sb = new StringBuilder();
                var j = i + 1;
                while (j < source.Length && source[j] != '"')
                {
                    if (source[j] == '\\' && j + 1 < source.Length) { sb.Append(source[j + 1]); j += 2; continue; }
                    sb.Append(source[j]);
                    j++;
                }
                found.Add(sb.ToString());
                i = j + 1;
                continue;
            }

            i++;
        }

        return found
            .Where(s => Regex.IsMatch(s.TrimStart(), @"^(SELECT|INSERT|UPDATE|DELETE)\b", RegexOptions.IgnoreCase))
            .ToList();
    }

    // ══════════════════════════════════════════════════════════════
    // 문장 하나를 색인과 대조
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// SQL 키워드·내장함수. 🔴 코드의 SQL 은 키워드를 전부 대문자로 쓰지만, 그 관례에 기대지 않고
    /// 소문자 형태도 함께 막는다(관례가 깨지는 날 오탐을 내지 않게).
    /// </summary>
    private static readonly HashSet<string> SqlWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "select","from","where","and","or","not","null","is","in","as","on","set","values","insert","into",
        "update","delete","join","left","right","inner","outer","order","by","group","having","limit","offset",
        "asc","desc","distinct","union","all","case","when","then","else","end","exists","between","like",
        "char","signed","unsigned","interval","day","month","year","hour","minute","second","using","default",
        "current_timestamp","current_date","utc_timestamp","curdate","now","true","false","duplicate",
    };

    internal sealed record Violation(string Controller, string Table, string Column, string Statement);

    /// <summary>한 문장을 본다. 색인에 없는 표가 끼면 판정하지 않는다(판정 불가를 통과로 쓰지 않으려고 세어 보고한다).</summary>
    private static void CheckStatement(
        string controller, string statement,
        Dictionary<string, HashSet<string>> index,
        List<Violation> violations, ref int judged, ref int unjudgeable)
    {
        var tables = new List<string>();
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(
                     statement,
                     @"(?:FROM|JOIN|INSERT\s+INTO|UPDATE)\s+`?([A-Za-z_][A-Za-z0-9_]*)`?(?:\s+(?:AS\s+)?`?([A-Za-z_][A-Za-z0-9_]*)`?)?",
                     RegexOptions.IgnoreCase))
        {
            var table = m.Groups[1].Value;
            if (SqlWords.Contains(table)) continue;      // FROM ( … ) 같은 자리
            tables.Add(table);
            var alias = m.Groups[2].Value;
            if (!string.IsNullOrEmpty(alias) && !SqlWords.Contains(alias)) aliases.Add(alias);
        }

        if (tables.Count == 0) return;                   // 표가 없는 문장(SELECT LAST_INSERT_ID() 등)
        var unknown = tables.Where(t => !index.ContainsKey(t)).ToList();
        if (unknown.Count > 0) { unjudgeable++; return; }

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tables) foreach (var c in index[t]) allowed.Add(c);

        judged++;

        foreach (Match m in Regex.Matches(statement, @"(@?)([A-Za-z_][A-Za-z0-9_]*)(\.([A-Za-z_][A-Za-z0-9_]*))?(\s*\()?"))
        {
            if (m.Groups[1].Value == "@") continue;                 // 파라미터
            if (m.Groups[5].Success) continue;                      // 함수 호출
            var qualified = m.Groups[3].Success;
            var token = qualified ? m.Groups[4].Value : m.Groups[2].Value;

            if (!qualified)
            {
                // 별칭·표 이름 자체는 칸이 아니다.
                if (aliases.Contains(token)) continue;
                if (index.ContainsKey(token)) continue;
                // 🔴 별명(AS PromotionId)은 칸이 아니다 — DDL 의 칸은 전부 소문자 snake 다.
                if (token.Any(char.IsUpper)) continue;
            }
            if (SqlWords.Contains(token)) continue;

            if (!allowed.Contains(token))
            {
                violations.Add(new Violation(controller, string.Join("+", tables), token, Compact(statement)));
            }
        }
    }

    private static string Compact(string s)
    {
        var one = Regex.Replace(s, @"\s+", " ").Trim();
        return one.Length <= 160 ? one : one[..160] + " …";
    }

    /// <summary>문장 단위로 쪼갠다 — 문자열 리터럴을 먼저 비워 그 안의 <c>;</c>·낱말이 섞이지 않게 한다.</summary>
    private static IEnumerable<string> SplitStatements(string sql)
    {
        var cleaned = Regex.Replace(StripComments(sql), @"'(?:[^']|'')*'", "''");
        return cleaned.Split(';').Where(s => !string.IsNullOrWhiteSpace(s));
    }

    /// <summary>엔진 — 대상 컨트롤러 전부를 색인과 대조한다. G-COL-1 과 G-COL-1n 이 같은 엔진을 쓴다.</summary>
    internal static (List<Violation> Violations, int Judged, int Unjudgeable) Analyze(
        IReadOnlyDictionary<string, string> ddl, IEnumerable<string> controllers)
    {
        var index = BuildIndex(ddl);
        var violations = new List<Violation>();
        var judged = 0;
        var unjudgeable = 0;

        foreach (var name in controllers)
        {
            var path = Path.Combine(ControllerDir(), name);
            Assert.True(File.Exists(path), $"과녁 컨트롤러가 없다: {path}");
            foreach (var literal in ExtractSqlLiterals(File.ReadAllText(path)))
                foreach (var st in SplitStatements(literal))
                    CheckStatement(name, st, index, violations, ref judged, ref unjudgeable);
        }

        return (violations, judged, unjudgeable);
    }

    // ══════════════════════════════════════════════════════════════
    // G-COL-1
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-COL-1 🔴 코드가 읽는 칸이 백오피스 DDL 색인에 전부 있다(10/8 운영 500 의 뿌리)")]
    public void G_COL_1_코드가_읽는_칸이_표에_있다()
    {
        var (violations, judged, unjudgeable) = Analyze(LoadDdl(), TargetControllers);

        // 🔴 양성 축 — 「한 문장도 판정 못 했는데 위반 0」이라는 조용한 초록을 막는다.
        Assert.True(judged >= 15,
            $"판정한 SQL 문장이 {judged} 개뿐이다 — 파서가 컨트롤러 SQL 을 못 읽고 있다(위반 0 은 증거가 아니다).");

        Console.WriteLine($"[G-COL-1] 판정한 문장 {judged} 개 · 판정 불가(색인에 없는 표) {unjudgeable} 개 · 위반 {violations.Count} 건");

        if (violations.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("코드가 읽는 칸이 백오피스 DDL(installer/backoffice/*.sql) 색인에 없다.");
            sb.AppendLine("⇒ 운영에서 그 화면을 열면 Unknown column 으로 500 이다(2026-10-08 실사고와 같은 모양).");
            sb.AppendLine();
            foreach (var g in violations
                         .GroupBy(v => (v.Controller, v.Table, v.Column))
                         .OrderBy(g => g.Key.Controller, StringComparer.Ordinal)
                         .ThenBy(g => g.Key.Table, StringComparer.Ordinal)
                         .ThenBy(g => g.Key.Column, StringComparer.Ordinal))
            {
                sb.AppendLine($"  · {g.Key.Controller} — 표 [{g.Key.Table}] 에 칸 [{g.Key.Column}] 이 없다 ({g.Count()} 문장)");
                sb.AppendLine($"      {g.First().Statement}");
            }
            throw new Xunit.Sdk.XunitException(sb.ToString());
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-COL-1n — 음성 대조군 (파서가 죽어 있으면 여기서 드러난다)
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-COL-1n 🔴 음성 대조군 — 44 에서 price_display 한 줄을 지우면 G-COL-1 이 그 칸을 잡는다")]
    public void G_COL_1n_봉합을_빼면_잡힌다()
    {
        var ddl = LoadDdl();

        // 세 칸은 44(ALTER) 와 00(CREATE) 양쪽에 있다 — 둘 다에서 price_display 를 빼야 「없는 칸」이 된다.
        var removed = 0;
        var mutated = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (file, text) in ddl)
        {
            var next = Regex.Replace(text, @"[^\r\n]*\bprice_display\b[^\r\n]*(\r?\n)?", "",
                                     RegexOptions.IgnoreCase);
            if (!ReferenceEquals(next, text) && next != text) removed++;
            mutated[file] = next;
        }

        Assert.True(removed >= 1,
            "DDL 어디에도 price_display 줄이 없다 — 봉합(44·00)이 들어가지 않았다. 음성 대조군을 세울 수 없다.");

        var beforeIndex = BuildIndex(ddl);
        Assert.True(beforeIndex.TryGetValue("pricing_plans", out var cols) && cols.Contains("price_display"),
            "봉합 상태에서 색인에 pricing_plans.price_display 가 없다 — 색인 파서가 ADD COLUMN/CREATE 를 못 읽는다.");

        var afterIndex = BuildIndex(mutated);
        Assert.False(afterIndex.TryGetValue("pricing_plans", out var cols2) && cols2.Contains("price_display"),
            "price_display 를 지운 사본인데 색인에 아직 있다 — 사본 만들기가 실패했다.");

        var (violations, judged, _) = Analyze(mutated, TargetControllers);
        Assert.True(judged >= 15, $"음성 대조군에서 판정한 문장이 {judged} 개뿐이다 — 파서가 안 돌았다.");

        var caught = violations
            .Where(v => v.Column.Equals("price_display", StringComparison.OrdinalIgnoreCase))
            .Select(v => v.Controller)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        Assert.True(caught.Count >= 3,
            "price_display 를 DDL 에서 지웠는데 G-COL-1 이 그 칸을 잡은 컨트롤러가 "
          + $"{caught.Count} 개뿐이다(기대: PricingAdmin·LandingPublic·PromotionsAdmin 셋 이상).\n"
          + "⇒ 파서가 죽어 있다. 이 게이트의 초록은 검증이 아니다.\n"
          + $"  잡힌 곳: {string.Join(", ", caught)}");

        Console.WriteLine($"[G-COL-1n] price_display 제거 사본에서 {caught.Count} 개 컨트롤러가 잡혔다: {string.Join(", ", caught)}");
    }
}
