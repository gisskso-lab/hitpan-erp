using System.Diagnostics;
using System.Text;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-R1</b> — 20260929작3 갈래 Y(9/30 · 작업지시서 §8-2). <b>진짜</b> <c>installer/updates/build-manifest.ps1</c> 를 실행해
/// 「호환성 코드 게이트」(§3 · 보호 표 ALTER·CREATE 칸 변경 차단)가 DB-135(새 표 <c>local_update_attempts</c>)를 통과시키는지 잰다.
/// </summary>
/// <remarks>
/// <para>
/// 🟢 <b>흉내가 아니다</b> — 스크립트를 한 글자도 바꾸지 않고 <c>-File</c> 로 통째로 돌린다. 임시 payload 에
/// 현 <c>src/HitPan.API/Migrations/SQL/DB-*.sql</c> 전부(DB-135 포함)를 <c>api\Migrations\SQL</c> 로 복사한다.
/// 스크립트는 <c>$PSScriptRoot\..\hitpan_db_clean.sql</c>(실물 출하 DDL)과 대조한다.
/// </para>
/// <para>
/// ⚠️ 버전 단계(§2)를 지나가려고 api·watchdog 자리에 <b>같은 <c>HitPan.API.dll</c></b>(이 시험이 참조하는 빌드 산출물)을 둔다 —
/// 버전 갈라짐 검사는 이 게이트의 대상이 아니다. web 자리는 빈 표시 파일. zip·manifest 는 임시 OutDir 에만 쓴다(레포 무접촉).
/// </para>
/// <para>
/// 🔴 음성 대조군이 시험 안에 있다 — 임시 <c>DB-999</c> 한 줄로 ① <c>ALTER TABLE local_update_apply_status</c>
/// ② <c>ALTER TABLE local_update_attempts</c>(R12 로 보호 목록 +1) ③ 시도 표 CREATE 에서 칸 하나 뺌 → 셋 다 스크립트가 <b>실패</b>해야 한다.
/// 대조가 안 갈리면 게이트가 무력하다는 뜻이라 FAIL.
/// </para>
/// <para>
/// ⚠️ Windows 전용(스크립트가 <c>Migrations\SQL</c> 역슬래시 경로를 쓴다 · 게시 파이프라인도 windows-latest + pwsh) ⇒
/// CI <c>build</c> 잡(windows-latest · 전 시험)에서 돈다. DB 불필요라 <c>db-gate</c> GATES 에는 넣지 않는다.
/// pwsh 가 있으면 pwsh, 없으면 Windows PowerShell 5.1 로 돈다(이 개발 PC 는 5.1 뿐).
/// </para>
/// </remarks>
public sealed class BuildManifestGuardGateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hp_gr1_" + Guid.NewGuid().ToString("N")[..8]);

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

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"[정리실패] 임시 폴더 {_root} 삭제 실패 — 사람이 지워야 한다: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"[정리실패] 임시 폴더 {_root} 삭제 권한 없음 — 사람이 지워야 한다: {ex.Message}");
        }
    }

    private static string PowerShellExe()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var d in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var p = Path.Combine(d.Trim(), "pwsh.exe");
            if (File.Exists(p)) return p;
        }
        return "powershell.exe";
    }

    /// <summary>임시 payload 를 만들고 진짜 스크립트를 돌린다. extraSql 이 있으면 <c>DB-999_gr1_control.sql</c> 로 얹는다.</summary>
    private (int Exit, string Output, string OutDir) Run(string name, string? extraSql)
    {
        if (!OperatingSystem.IsWindows())
            throw new Xunit.Sdk.XunitException("G-R1 은 Windows 러너(CI build 잡)에서만 잰다 — 여기서 돌면 안 된다.");

        var repo = RepoRoot();
        var payload = Path.Combine(_root, name, "payload");
        var outDir = Path.Combine(_root, name, "out");
        var api = Path.Combine(payload, "api");
        var sqlDir = Path.Combine(api, "Migrations", "SQL");
        var web = Path.Combine(payload, "web");
        var wd = Path.Combine(payload, "watchdog");
        Directory.CreateDirectory(sqlDir);
        Directory.CreateDirectory(web);
        Directory.CreateDirectory(wd);

        var apiDll = typeof(HitPan.API.Controllers.AuthController).Assembly.Location;
        File.Copy(apiDll, Path.Combine(api, "HitPan.API.dll"));
        File.Copy(apiDll, Path.Combine(wd, "HitPan.Watchdog.dll"));
        File.WriteAllText(Path.Combine(web, "placeholder.txt"), "G-R1");

        var srcSql = Path.Combine(repo, "src", "HitPan.API", "Migrations", "SQL");
        var copied = 0;
        foreach (var f in Directory.GetFiles(srcSql, "DB-*.sql"))
        {
            File.Copy(f, Path.Combine(sqlDir, Path.GetFileName(f)));
            copied++;
        }
        Assert.True(copied > 0, "마이그 SQL 을 한 건도 못 옮겼다");
        Assert.True(File.Exists(Path.Combine(sqlDir, "DB-135_local_update_attempts.sql")), "payload 에 DB-135 가 없다");
        if (extraSql is not null)
            File.WriteAllText(Path.Combine(sqlDir, "DB-999_gr1_control.sql"), extraSql, new UTF8Encoding(false));

        var script = Path.Combine(repo, "installer", "updates", "build-manifest.ps1");
        Assert.True(File.Exists(script), "build-manifest.ps1 을 못 찾았다");

        var psi = new ProcessStartInfo(PowerShellExe())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
                     "-PayloadDir", payload, "-OutDir", outDir, "-Channel", "Major",
                     "-DownloadUrlBase", "https://example.invalid/packages" })
            psi.ArgumentList.Add(a);
        psi.Environment["HITPAN_RELEASED_AT"] = "2026-09-30T00:00:00Z";

        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();
        if (!proc.WaitForExit(180_000))
        {
            proc.Kill(entireProcessTree: true);
            throw new Xunit.Sdk.XunitException($"build-manifest.ps1 이 180초 안에 안 끝났다({name})");
        }
        proc.WaitForExit();
        return (proc.ExitCode, stdout.Result + "\n" + stderr.Result, outDir);
    }

    [Fact(DisplayName = "G-R1 🔴 진짜 build-manifest.ps1 — 현 마이그 전부(DB-135 포함)가 호환성 게이트를 통과한다")]
    public void GR1_DB135_통과()
    {
        var (exit, output, outDir) = Run("pass", null);

        Assert.True(exit == 0, $"스크립트가 실패했다(exit {exit}):\n{output}");
        Assert.True(File.Exists(Path.Combine(outDir, "manifest.json")), "manifest.json 이 안 나왔다 — 스크립트가 끝까지 안 갔다");
        var sidecar = Directory.GetFiles(outDir, "migrations-*.txt").Single();
        Assert.Contains("DB-135_local_update_attempts.sql", File.ReadAllText(sidecar));
    }

    [Fact(DisplayName = "G-R1 대조 🔴 DB-999 가 local_update_apply_status 를 ALTER 하면 스크립트가 실패한다")]
    public void GR1_대조_apply_status_ALTER_는_실패()
    {
        var (exit, output, outDir) = Run("alter_apply", "ALTER TABLE local_update_apply_status ADD COLUMN x INT;\n");

        Assert.True(exit != 0, "보호 표 ALTER 가 통과됐다 — 게이트가 무력하다:\n" + output);
        Assert.Contains("DB-999_gr1_control.sql", output);
        Assert.Contains("'local_update_apply_status'", output);
        Assert.False(File.Exists(Path.Combine(outDir, "manifest.json")), "실패했는데 manifest 가 나왔다");
    }

    [Fact(DisplayName = "G-R1 대조 🔴 DB-999 가 local_update_attempts 를 ALTER 하면 스크립트가 실패한다(보호 목록 +1)")]
    public void GR1_대조_attempts_ALTER_는_실패()
    {
        var (exit, output, _) = Run("alter_attempts", "ALTER TABLE local_update_attempts ADD COLUMN x INT;\n");

        Assert.True(exit != 0, "시도 표 ALTER 가 통과됐다 — R12 보호 목록 +1 이 안 걸렸다:\n" + output);
        Assert.Contains("DB-999_gr1_control.sql", output);
        Assert.Contains("'local_update_attempts'", output);
    }

    [Fact(DisplayName = "G-R1 대조 DB-999 가 시도 표를 칸 하나 뺀 CREATE 로 다시 정의하면 스크립트가 실패한다")]
    public void GR1_대조_attempts_CREATE_칸다름은_실패()
    {
        var db135 = File.ReadAllText(Path.Combine(RepoRoot(), "src", "HitPan.API", "Migrations", "SQL",
            "DB-135_local_update_attempts.sql")).Replace("\r\n", "\n");
        var lines = db135.Split('\n').Where(l => !l.TrimStart().StartsWith("`ended_at`", StringComparison.Ordinal)).ToArray();
        Assert.True(lines.Length == db135.Split('\n').Length - 1, "대조군 준비 실패 — ended_at 줄을 정확히 하나 빼지 못했다");

        var (exit, output, _) = Run("create_diff", string.Join("\n", lines));

        Assert.True(exit != 0, "칸이 다른 시도 표 CREATE 가 통과됐다 — 게이트가 무력하다:\n" + output);
        Assert.Contains("DB-999_gr1_control.sql", output);
        Assert.Contains("'local_update_attempts' CREATE", output);
    }
}
