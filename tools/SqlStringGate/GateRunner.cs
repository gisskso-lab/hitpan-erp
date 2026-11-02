using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace SqlStringGate;

internal sealed record SiteRef(int Line, int Offset, int NextLine);

internal sealed class FileReport
{
    public FileReport(string name, string source, LexError? error)
    {
        Name = name;
        Source = source;
        Error = error;
    }

    public string Name { get; }
    public string Source { get; }
    public LexError? Error { get; }
    public int Strings { get; set; }
    public int SqlStrings { get; set; }
    public List<SiteRef> Sites { get; } = new();
    public List<(RuleId Rule, int Line)> Findings { get; } = new();
}

/// SQL 문자열의 주석 표기 규칙을 검사한다.
/// 근거: docs/운영기록/20261102작_막는장치_작업지시서.md
/// 실행 시 값으로 조립되는 문자열은 대상이 아니다. 초록이 전수 보증은 아니다.
internal static class GateRunner
{
    public const int MinSites = 19;
    public const int MinFiles = 740;
    public const int MinStrings = 22000;
    public const int MinSqlStrings = 2200;

    private const string Probe = "    , @p ";

    private static readonly string[] Excluded =
    {
        "src/HitPan.Tests/",
        "src/HitPan.IntegrationTests/",
        "src/HitPan.Watchdog.Tests/",
        "tests/",
        "tools/SqlStringGate/",
    };

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static int Run(string root, TextWriter output)
    {
        try
        {
            return RunSteps(root, output);
        }
        catch (Exception ex)
        {
            output.WriteLine($"FAIL {ex.GetType().Name}");
            return 1;
        }
    }

    public static bool RunControls(TextWriter output)
    {
        bool ok = true;
        for (int i = 0; i < GateControls.Negative.Length; i++)
        {
            (string source, RuleId rule) = GateControls.Negative[i];
            FileReport report = Analyze("control", source);
            if (report.Error != null || !report.Findings.Exists(f => f.Rule == rule))
            {
                output.WriteLine($"FAIL control N{i + 1}");
                ok = false;
            }
        }

        for (int i = 0; i < GateControls.Positive.Length; i++)
        {
            FileReport report = Analyze("control", GateControls.Positive[i]);
            if (report.Error != null || report.Findings.Count > 0)
            {
                output.WriteLine($"FAIL control P{i + 1}");
                ok = false;
            }
        }

        for (int i = 0; i < GateControls.Broken.Length; i++)
        {
            if (Analyze("control", GateControls.Broken[i]).Error == null)
            {
                output.WriteLine($"FAIL control B{i + 1}");
                ok = false;
            }
        }

        return ok;
    }

    public static FileReport Analyze(string name, string source)
    {
        string text = SqlLineRules.Normalize(source);
        var lexer = new CsStringLexer(text);
        List<StringGroup> groups = lexer.Run(out LexError? error);
        var report = new FileReport(name, text, error);
        foreach (StringGroup group in groups)
        {
            report.Strings++;
            if (!SqlLineRules.IsSql(group.Text))
            {
                continue;
            }

            report.SqlStrings++;
            LineWalk walk = SqlLineRules.Walk(group.Text);
            foreach (int site in walk.Sites)
            {
                int next = site + 3;
                int position = next < group.Map.Length ? group.Map[next] : -1;
                report.Sites.Add(new SiteRef(lexer.LineOf(group.Map[site]), site, position));
            }

            foreach (Finding finding in SqlLineRules.Evaluate(group.Text, walk))
            {
                report.Findings.Add((finding.Rule, lexer.LineOf(group.Map[finding.Offset])));
            }
        }

        return report;
    }

    public static string Digest(IEnumerable<FileReport> reports)
    {
        var keys = new List<string>();
        foreach (FileReport report in reports)
        {
            foreach (SiteRef site in report.Sites)
            {
                keys.Add($"{report.Name}:{site.Line}:{site.Offset}");
            }
        }

        keys.Sort(StringComparer.Ordinal);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', keys)));
        return Convert.ToHexString(hash).ToLowerInvariant()[..12];
    }

    private static int RunSteps(string root, TextWriter output)
    {
        if (!RunControls(output))
        {
            return 1;
        }

        List<string>? names = ListFiles(root);
        if (names == null)
        {
            output.WriteLine("FAIL files");
            return 1;
        }

        var reports = new List<FileReport>();
        bool clean = true;
        foreach (string name in names)
        {
            string? source = ReadSource(root, name);
            if (source == null)
            {
                output.WriteLine($"{name}:1  분석 오류 — encoding");
                clean = false;
                continue;
            }

            FileReport report = Analyze(name, source);
            reports.Add(report);
            clean &= Report(report, output);
        }

        if (!clean)
        {
            output.WriteLine("FAIL tree");
            return 1;
        }

        int strings = reports.Sum(r => r.Strings);
        if (!CheckFloors(reports, strings, output) || !CheckMutations(reports, output))
        {
            return 1;
        }

        string digest = Digest(reports);
        if (!CheckLineEndings(reports, digest, strings, output))
        {
            return 1;
        }

        output.WriteLine($"PASS files={reports.Count} strings={strings} digest={digest}");
        return 0;
    }

    private static List<string>? ListFiles(string root)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = new UTF8Encoding(false),
        };
        foreach (string argument in new[] { "ls-files", "-z", "--", ":(icase)*.cs" })
        {
            start.ArgumentList.Add(argument);
        }

        using Process git = Process.Start(start) ?? throw new InvalidOperationException("git");
        Task<string> errors = git.StandardError.ReadToEndAsync();
        string listing = git.StandardOutput.ReadToEnd();
        git.WaitForExit();
        errors.GetAwaiter().GetResult();
        if (git.ExitCode != 0)
        {
            return null;
        }

        var names = new List<string>();
        foreach (string name in listing.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Array.Exists(Excluded, prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static string? ReadSource(string root, string name)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(root, name));
        int skip = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        try
        {
            return StrictUtf8.GetString(bytes, skip, bytes.Length - skip);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static bool Report(FileReport report, TextWriter output)
    {
        if (report.Error != null)
        {
            output.WriteLine($"{report.Name}:{report.Error.Line}  분석 오류 — {report.Error.Kind}");
            return false;
        }

        foreach ((RuleId _, int line) in report.Findings)
        {
            output.WriteLine($"{report.Name}:{line}  SQL 문자열 주석 규칙 위반 — 맨 '--' 뒤에 설명을 붙이거나 표시를 지운다");
        }

        return report.Findings.Count == 0;
    }

    private static bool CheckFloors(List<FileReport> reports, int strings, TextWriter output)
    {
        bool ok = true;
        void Require(string label, int actual, int floor)
        {
            if (actual < floor)
            {
                output.WriteLine($"FAIL floor {label}");
                ok = false;
            }
        }

        Require("files", reports.Count, MinFiles);
        Require("strings", strings, MinStrings);
        Require("sql", reports.Sum(r => r.SqlStrings), MinSqlStrings);
        Require("sites", reports.Sum(r => r.Sites.Count), MinSites);
        return ok;
    }

    private static bool CheckMutations(List<FileReport> reports, TextWriter output)
    {
        foreach (FileReport report in reports)
        {
            foreach (SiteRef site in report.Sites)
            {
                bool caught = false;
                if (site.NextLine >= 0)
                {
                    FileReport mutated = Analyze(report.Name, report.Source.Insert(site.NextLine, Probe));
                    caught = mutated.Error == null
                        && mutated.Findings.FindAll(f => f.Rule == RuleId.F1).Count == 1
                        && !Report(mutated, TextWriter.Null);
                }

                if (!caught)
                {
                    output.WriteLine("FAIL mutation");
                    return false;
                }
            }
        }

        return true;
    }

    private static bool CheckLineEndings(List<FileReport> reports, string digest, int strings, TextWriter output)
    {
        var converted = new List<FileReport>(reports.Count);
        foreach (FileReport report in reports)
        {
            converted.Add(Analyze(report.Name, report.Source.Replace("\n", "\r\n")));
        }

        if (Digest(converted) != digest || converted.Sum(r => r.Strings) != strings)
        {
            output.WriteLine("FAIL eol");
            return false;
        }

        return true;
    }
}
