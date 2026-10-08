using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 SQL 주석 마커 게이트 — <c>--</c> 뒤 공백 누락이 **마이그 전체를 멈추는** 사고를 막는다.
///
/// <para><b>왜 생겼나(2026-10-08 실사고)</b>: 사장님 지시 원문을 <c>41_backoffice_cs_kb.sql</c> 에
/// <c>--누리집을…</c> 로 적었다. MariaDB/MySQL 의 <c>--</c> 주석은 <b>뒤에 공백(또는 줄끝)이
/// 있어야</b> 주석으로 읽힌다. 공백이 없으면 그 줄이 <b>구문</b>으로 들어가 그 파일 적용이 통째로
/// 실패하고, 적재기가 거기서 멈추므로 <b>그 뒤 번호의 파일도 안 들어간다</b>.
/// 실제 피해 모양: 게이트 5건이 「단언 실패」가 아니라 DB 문법 오류로 동시에 넘어졌고,
/// 운영에서 같은 일이 나면 <b>백오피스 API 가 기동을 거부</b>한다(스키마 불일치 잠복 방지 설계).</para>
///
/// <para>이 게이트는 DB 가 없어도 돈다 — 그래서 <c>build</c> 잡에서 **먼저** 소리를 낸다.
/// 번호순 적재의 특성상 한 글자가 뒤의 모든 파일을 막으므로, 사람이 눈으로 세는 데 맡기지 않는다.</para>
/// </summary>
public sealed class InstallerSqlCommentGateTests
{
    /// <summary>그 줄이 「깨진 주석」인가. true = DB 가 구문으로 읽어 터지는 줄.</summary>
    private static bool IsBrokenComment(string line)
    {
        var t = line.TrimStart();
        if (!t.StartsWith("--", StringComparison.Ordinal)) return false;
        if (t.Length == 2) return false;                       // "--" 만 있는 줄은 정상
        if (t.TrimEnd().All(c => c == '-')) return false;       // "--------" 구분선은 정상 취급
        return !char.IsWhiteSpace(t[2]);                        // "--" 뒤가 공백이 아니면 깨진 줄
    }

    [Fact(DisplayName = "G-SQL-1 🔴 판별식 자체가 맞는지 먼저 본다(양성·음성 대조군)")]
    public void G_SQL_1_판별식_대조군()
    {
        // 음성 — 이런 줄은 터진다(실사고 모양 그대로)
        Assert.True(IsBrokenComment("--누리집을 만들고 AI를 통한 CS자동화"));
        Assert.True(IsBrokenComment("    --comment"));
        Assert.True(IsBrokenComment("--🔴 표시"));

        // 양성 — 이런 줄은 정상이고, 게이트가 괜한 빨간불을 내면 안 된다
        Assert.False(IsBrokenComment("-- 누리집을 만들고 AI를 통한 CS자동화"));
        Assert.False(IsBrokenComment("--"));
        Assert.False(IsBrokenComment("-- ============================"));
        Assert.False(IsBrokenComment("--------------------------------"));
        Assert.False(IsBrokenComment("CREATE TABLE t (a int); -- 뒤쪽 주석"));
        Assert.False(IsBrokenComment("    id bigint NOT NULL,"));
    }

    [Fact(DisplayName = "G-SQL-2 🔴 적재되는 SQL 전체에 깨진 주석 0건 — 한 줄이 뒤의 모든 파일을 막는다")]
    public void G_SQL_2_적재대상_전수()
    {
        var root = RepoRoot();
        var targets = new[]
        {
            Path.Combine(root, "installer"),                                   // 백오피스·출하 DDL
            Path.Combine(root, "src", "HitPan.API", "Migrations", "SQL"),       // ERP 마이그
        };

        var bad = new List<string>();
        int scanned = 0;

        foreach (var dir in targets)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.sql", SearchOption.AllDirectories))
            {
                scanned++;
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (IsBrokenComment(lines[i]))
                        bad.Add($"{Path.GetFileName(file)}:{i + 1}");
                }
            }
        }

        // 세어 본 파일이 0이면 이 게이트는 아무것도 안 잰 것이다(경로가 바뀐 경우를 잡는다)
        Assert.True(scanned > 50, $"적재 대상 SQL 을 거의 못 읽었다 — 경로 확인 필요(scanned={scanned})");
        Assert.True(bad.Count == 0,
            "「-- 」 뒤 공백 없는 주석이 있다 — 그 파일부터 마이그가 멈춘다:\n  " + string.Join("\n  ", bad));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 적재 대상 SQL 을 읽을 수 없다.");
    }
}
