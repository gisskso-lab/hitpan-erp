using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using HitPan.Application.Services;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 2026-10-05 작5 — 사원 ↔ 계정 연결 게이트의 <b>DB 불필요 몫</b>(설계 §8 G-E11 · G-E14). DB 몫은 <c>EmployeeAccountLinkGateDbTests</c>.
/// </summary>
/// <remarks>
/// <para>G-E11 은 <b>CI 가 실제로 돌리는 스크립트</b>(<c>scripts/check-permission-menu-sync.sh</c>)를 실물 파일과 「하나 뺀」 사본에 돌려 종료 코드를 잰다.
/// 백엔드 목록은 리플렉션(실행값)으로, 프론트 목록은 원문으로 — 대조 두 쪽을 같은 파서로 읽지 않는다.</para>
/// <para>G-E14 는 화면 렌더러(bUnit)가 시험 프로젝트에 없어 원문 구조(바인딩된 칸의 Label · 표 머리)로 잰다 — 같은 판정기를 옛 줄에 돌려 잡는지 함께 확인.</para>
/// <para>갈래2·3 합류 전에는 FAIL 이 정상(새 코드·「아이디」 표기가 아직 없다) — SKIP 아님.</para>
/// </remarks>
public sealed class EmployeeAccountLinkGateTests
{
    private static readonly string[] NewCodes = { "USERS_ACCOUNT", "USERS_SEAT" };

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

    private static string BashExe()
    {
        if (OperatingSystem.IsWindows())
        {
            var git = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe");
            if (File.Exists(git)) return git;
        }
        return "bash";
    }

    /// <summary>스크립트·두 목록 파일을 임시 트리에 같은 상대 경로로 복사(LF 로 — 우분투 CI 와 같은 바이트)하고 돌린다.</summary>
    private static (int code, string output) RunSync(Func<string, string>? mutateFront = null)
    {
        var root = RepoRoot();
        var tmp = Path.Combine(Path.GetTempPath(), "eal_sync_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            foreach (var rel in new[] { "scripts/check-permission-menu-sync.sh",
                                        "src/HitPan.Application/Services/PermissionService.cs",
                                        "src/HitPan.Web/Pages/Settings/PermissionPage.razor.cs" })
            {
                var text = File.ReadAllText(Path.Combine(root, rel)).Replace("\r\n", "\n");
                if (mutateFront is not null && rel.EndsWith("PermissionPage.razor.cs", StringComparison.Ordinal)) text = mutateFront(text);
                var dst = Path.Combine(tmp, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.WriteAllText(dst, text);
            }
            var psi = new ProcessStartInfo(BashExe()) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            psi.ArgumentList.Add(Path.Combine(tmp, "scripts", "check-permission-menu-sync.sh").Replace('\\', '/'));
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, o);
        }
        finally
        {
            try { Directory.Delete(tmp, true); }
            catch (IOException ex) { Console.Error.WriteLine($"[G-E11] 임시 폴더 정리 실패: {tmp} ({ex.Message})"); }
        }
    }

    [Fact(DisplayName = "G-E11 🔴 권한 메뉴 코드 정합 — 백엔드 MenuList(실행값)·프론트 ErpMenus 에 USERS_ACCOUNT·USERS_SEAT · CI 스크립트 종료 0 · 대조군(프론트에서 하나 빼면 같은 스크립트가 1 + 그 코드 이름)")]
    public void E11_Permission_Menu_Sync_Includes_New_Codes()
    {
        // 백엔드 — 실행값(리플렉션). 원문 파서가 아니다.
        var f = typeof(PermissionService).GetField("MenuList", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new Xunit.Sdk.XunitException("PermissionService.MenuList 가 없다 — 게이트를 같이 고쳐라");
        var back = ((System.Collections.IEnumerable)f.GetValue(null)!).Cast<object>()
            .Select(x => x.GetType().GetField("Item1")!.GetValue(x)!.ToString()).ToHashSet();
        var (code, output) = RunSync();
        Assert.True(code == 0, $"실물 파일에서 정합 스크립트 실패({code}):\n{output}");

        foreach (var c in NewCodes) Assert.True(back.Contains(c), $"갈래2 합류 전 — 백엔드 MenuList 에 {c} 없음");

        // 🔴 대조군 — 프론트 목록에서 USERS_SEAT 한 줄을 빼면 같은 스크립트가 1 로 끝나고 그 코드를 이름으로 댄다
        var (bad, badOut) = RunSync(t => Regex.Replace(t, @"^.*\(""USERS_SEAT"".*\n", "", RegexOptions.Multiline));
        Assert.Equal(1, bad);
        Assert.Contains("USERS_SEAT", badOut);
    }

    [Fact(DisplayName = "G-E11c 대조군 장치 확인 — 지금 있는 코드(USERS) 한 줄을 프론트에서 빼면 정합 스크립트가 1 + USERS (bash 실행·사본 변형이 실제로 돈다)")]
    public void E11c_Sync_Script_Catches_Removed_Line()
    {
        var (ok, okOut) = RunSync();
        Assert.True(ok == 0, $"실물 파일 정합 실패({ok}):\n{okOut}");
        var (bad, badOut) = RunSync(t => Regex.Replace(t, @"^.*\(""USERS"",.*\n", "", RegexOptions.Multiline));
        Assert.Equal(1, bad);
        Assert.Contains("- USERS", badOut);
    }

    // ══ G-E14 ══

    /// <summary>Razor 주석(<c>@* *@</c>)·C# 줄 주석을 걷어낸 화면 원문.</summary>
    private static string Visible(string razor) =>
        Regex.Replace(Regex.Replace(razor, @"@\*.*?\*@", "", RegexOptions.Singleline), @"^\s*//.*$", "", RegexOptions.Multiline);

    /// <summary><paramref name="binding"/> 에 묶인 입력 칸의 Label 값. 없으면 null.</summary>
    private static string? LabelOf(string razor, string binding)
    {
        var m = Regex.Match(razor, @"<Mud\w+[^>]*?@bind-Value=""" + Regex.Escape(binding) + @"""[^>]*>", RegexOptions.Singleline);
        if (!m.Success) return null;
        var l = Regex.Match(m.Value, @"\bLabel=""([^""]*)""");
        return l.Success ? l.Groups[1].Value : null;
    }

    [Fact(DisplayName = "G-E14 🔴 「이메일」→「아이디」 — Users.razor 계정 자리 「이메일」 0 · 아이디 입력 칸 Label 「아이디」 · EmployeePage 사원 이메일 칸은 「이메일」 유지 · 대조군(같은 판정기가 옛 줄을 잡는다)")]
    public void E14_Account_Label_Is_Id()
    {
        var root = RepoRoot();
        var users = Visible(File.ReadAllText(Path.Combine(root, "src", "HitPan.Web", "Pages", "Users.razor")));
        var emp = Visible(File.ReadAllText(Path.Combine(root, "src", "HitPan.Web", "Pages", "Settings", "EmployeePage.razor")));

        var left = Regex.Matches(users, "이메일").Count;
        Assert.True(left == 0, $"갈래3 합류 전 — Users.razor 에 계정 뜻 「이메일」 {left}곳(주석 제외)");
        Assert.Contains("아이디", LabelOf(users, "_createModel.Email") ?? "(칸 없음)");
        Assert.Equal("이메일", LabelOf(emp, "_edit.Email"));          // 사원 이메일 칸은 그대로(⑦-③)

        // 🔴 대조군 — 옛 줄(지금 main 의 :215)을 같은 판정기에 넣으면 잡힌다
        const string old = "<MudTextField T=\"string\" @bind-Value=\"_createModel.Email\" Label=\"이메일 *\" Variant=\"Variant.Outlined\" />";
        Assert.Equal("이메일 *", LabelOf(old, "_createModel.Email"));
        Assert.Single(Regex.Matches(Visible(old), "이메일"));
        Assert.Empty(Regex.Matches(Visible("@* 이메일 *@"), "이메일"));   // 주석은 세지 않는다
    }
}
