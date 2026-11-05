using SqlStringGate;
using Xunit;
using Xunit.Abstractions;

namespace HitPan.Tests.Integrity;

public class SqlStringGateTests
{
    private readonly ITestOutputHelper _output;

    public SqlStringGateTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Controls_Match()
    {
        var log = new StringWriter();
        bool ok = GateRunner.RunControls(log);
        _output.WriteLine(log.ToString());
        Assert.True(ok);
    }

    [Fact]
    public void Tree_Passes()
    {
        var log = new StringWriter();
        int code = GateRunner.Run(FindRepoRoot(), log);
        _output.WriteLine(log.ToString());
        Assert.Equal(0, code);
        Assert.StartsWith("PASS ", log.ToString().TrimEnd().Split('\n')[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_Tree_Fails()
    {
        string empty = Path.Combine(Path.GetTempPath(), "sql-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            var log = new StringWriter();
            Assert.Equal(1, GateRunner.Run(empty, log));
            Assert.DoesNotContain("PASS", log.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(empty);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln")))
            {
                return dir.Parent!.FullName;
            }

            dir = dir.Parent;
        }

        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다.");
    }
}
