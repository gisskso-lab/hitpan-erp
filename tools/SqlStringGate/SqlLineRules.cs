using System.Text.RegularExpressions;

namespace SqlStringGate;

internal enum RuleId
{
    F1,
    F2,
    F3,
    F4,
}

internal readonly record struct Finding(RuleId Rule, int Offset);

internal sealed record LineWalk(List<int> Sites, int? Tail, List<int> HoleDashes);

internal static class SqlLineRules
{
    private const char Plain = 'N';
    private const char LineComment = 'E';
    private const char Block = 'C';
    private const char BlockStar = 'A';
    private const char Slash = 'S';
    private const char Dash1 = '1';
    private const char Dash2 = '2';

    private static readonly Regex Words = new(
        @"\b(?:(?i:SELECT|INSERT\s+INTO|UPDATE|DELETE|REPLACE\s+INTO|WHERE|FROM|JOIN|CREATE\s+TABLE|ALTER\s+TABLE|DROP\s+TABLE|TRUNCATE|VALUES|SET|GROUP\s+BY|ORDER\s+BY|HAVING|LIMIT|UNION|CASE\s+WHEN|CALL)|AND|OR|ON)\b",
        RegexOptions.CultureInvariant);

    private static readonly Regex Parameter = new(@"(?<![@\w])@[A-Za-z_]\w*", RegexOptions.CultureInvariant);

    public static string Normalize(string source) => source.Replace("\r\n", "\n").Replace('\r', '\n');

    public static bool IsSql(string text) => Words.IsMatch(text) || Parameter.IsMatch(text);

    public static LineWalk Walk(string sql)
    {
        var sites = new List<int>();
        var holes = new List<int>();
        int? tail = null;
        char state = Plain;
        int n = sql.Length;
        for (int i = 0; i < n; i++)
        {
            char c = sql[i];
            switch (state)
            {
                case LineComment:
                    if (c == '\n')
                    {
                        state = Plain;
                    }

                    break;
                case Block:
                    if (c == '*')
                    {
                        state = BlockStar;
                    }

                    break;
                case BlockStar:
                    state = c == '/' ? Plain : Block;
                    break;
                case '\'':
                case '"':
                case '`':
                    if (c == state)
                    {
                        state = Plain;
                    }
                    else if (c == '\\' && state != '`')
                    {
                        i++;
                    }

                    break;
                case Dash1:
                    state = c == '-' ? Dash2 : Plain;
                    break;
                case Dash2:
                    if (IsControl(c))
                    {
                        state = LineComment;
                        if (c == '\n')
                        {
                            sites.Add(i - 2);
                        }
                    }
                    else
                    {
                        state = Plain;
                    }

                    break;
                case Slash:
                    state = c == '*' ? Block : Plain;
                    break;
                default:
                    if (c == '-' && i < n - 2 && sql[i + 1] == '-' && IsControl(sql[i + 2]))
                    {
                        state = Dash1;
                    }
                    else if (c == '-' && i == n - 2 && sql[i + 1] == '-')
                    {
                        tail = i;
                    }
                    else if (c == '-' && i < n - 2 && sql[i + 1] == '-' && sql[i + 2] == CsStringLexer.HoleMark)
                    {
                        holes.Add(i);
                    }
                    else if (c == '/' && i < n - 1 && sql[i + 1] == '*')
                    {
                        state = Slash;
                    }
                    else if (c is '\'' or '"' or '`')
                    {
                        state = c;
                    }
                    else if (c == '#')
                    {
                        state = LineComment;
                    }

                    break;
            }
        }

        return new LineWalk(sites, tail, holes);
    }

    public static List<Finding> Evaluate(string sql, LineWalk walk)
    {
        var findings = new List<Finding>();
        foreach (int site in walk.Sites)
        {
            int lineFeed = site + 2;
            string next = NextLine(sql, lineFeed).Trim();
            if (next.Length == 0)
            {
                if (string.IsNullOrWhiteSpace(sql[(lineFeed + 1)..]))
                {
                    findings.Add(new Finding(RuleId.F3, site));
                }
            }
            else if (!IsComment(next))
            {
                findings.Add(new Finding(RuleId.F1, site));
            }
        }

        if (walk.Tail is int tail)
        {
            findings.Add(new Finding(RuleId.F2, tail));
        }

        foreach (int hole in walk.HoleDashes)
        {
            findings.Add(new Finding(RuleId.F4, hole));
        }

        return findings;
    }

    private static bool IsControl(char c) => c <= ' ' || c == '\u007F';

    private static string NextLine(string sql, int lineFeed)
    {
        int start = lineFeed + 1;
        int end = sql.IndexOf('\n', start);
        return end < 0 ? sql[start..] : sql[start..end];
    }

    private static bool IsComment(string line)
    {
        if (line == "--"
            || line.StartsWith("-- ", StringComparison.Ordinal)
            || line.StartsWith("--\t", StringComparison.Ordinal)
            || line.StartsWith('#'))
        {
            return true;
        }

        if (!line.StartsWith("/*", StringComparison.Ordinal) || !line.EndsWith("*/", StringComparison.Ordinal))
        {
            return false;
        }

        return line.IndexOf("*/", StringComparison.Ordinal) == line.Length - 2;
    }
}
