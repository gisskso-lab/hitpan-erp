using System.Text.RegularExpressions;
using HitPan.Application.Services;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20260915작1 3판 갈래 Z (작지 §15-13) — LegacyBalanceLabelParityGate.
/// Web 은 Application 을 참조하지 않아 이름표·안내 문구·대사 상태값을 <b>복사</b>해 둔다(R4).
/// 서버 값만 바뀌면 화면은 조용히 「기타」·「—」로 떨어지고 안내 줄이 두 번 뜨거나 사라진다 — 컴파일은 통과한다.
/// 그래서 Web 소스의 상수 문자열을 읽어 서버 상수와 글자 단위로 대조한다.
/// </summary>
public sealed class LegacyBalanceLabelParityGateTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.True(dir is not null && Directory.Exists(Path.Combine(dir, "src")), "레포 루트를 찾아야 한다");
        return dir!;
    }

    private static string ReadSource(params string[] parts)
    {
        var path = Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"파일이 있어야 한다: {path}");
        return File.ReadAllText(path);
    }

    /// <summary>`const string 이름 = "값";` 한 줄의 값. 주석 줄(`//`·`///`)은 건너뛴다 — 설명문에 걸려 헛통과하지 않게.</summary>
    private static string ConstValue(string source, string name)
    {
        var pattern = new Regex("^\\s*(?:public|private|internal)\\s+const\\s+string\\s+" + Regex.Escape(name) + "\\s*=\\s*\"(?<v>[^\"]*)\"\\s*;",
            RegexOptions.Multiline);
        var matches = pattern.Matches(source);
        Assert.True(matches.Count == 1, $"Web 상수 {name} 가 정확히 1곳이어야 한다(실제 {matches.Count})");
        return matches[0].Groups["v"].Value;
    }

    [Fact]
    public void Web_collection_labels_match_server_legacy_balance_constants()
    {
        var models = ReadSource("src", "HitPan.Web", "Models", "ApprovalModels.cs");
        Assert.Equal(LegacyBalanceMatching.RefType, ConstValue(models, "LegacyBalanceRefType"));
        Assert.Equal(LegacyBalanceMatching.Label, ConstValue(models, "LegacyBalanceLabel"));
    }

    [Fact]
    public void Web_reconciliation_page_matches_server_status_and_notice()
    {
        var page = ReadSource("src", "HitPan.Web", "Pages", "Settings", "MdbMigration.razor");
        Assert.Equal(MdbReconPosting.StatusExplained, ConstValue(page, "ReconExplained"));
        Assert.Equal(MdbReconPosting.StatusInfo, ConstValue(page, "ReconInfo"));
        Assert.Equal(MdbLegacyFinalStock.RollForwardNotice, ConstValue(page, "RollForwardNoticeFormat"));
    }
}
