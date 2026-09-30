using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HitPan.API.Services.LocalSwap;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// 20260930작1 I-API — 교체 일꾼(<c>local-swap.ps1</c>)을 <b>실제로 돌리는</b> 시험 대역 폴더.
/// </summary>
/// <remarks>
/// 🟢 일꾼의 <c>-TestRoot</c> 모드를 쓴다 — 예약 작업·서비스·프로세스 종료·상태 확인·이벤트로그가 전부 <c>{root}</c> 아래 대역 파일로 바뀐다
/// (일꾼 머리말). 실제 예약 작업·서비스·설치 폴더 무접촉.
/// 되돌리기(rollback) 한 판: 지금 1.3.48 · 재료 ① <c>prev</c> = 1.3.47(해시 목록 포함).
/// </remarks>
internal sealed class LocalSwapWorkerRig : IDisposable
{
    public const string From = "1.3.48";
    public const string To = "1.3.47";

    public string Root { get; }
    public string App => Path.Combine(Root, "app");
    public string Work => Path.Combine(App, "rollback");
    public string TasksDir => Path.Combine(Root, "tasks");
    public string Ticket { get; } = LocalSwapLauncher.NewTicket();

    private LocalSwapWorkerRig(string root) => Root = root;

    /// <summary>되돌리기 한 판을 차린다. <paramref name="requestedAtUtc"/> = 요청서 발급 시각.</summary>
    public static LocalSwapWorkerRig Rollback(DateTime requestedAtUtc)
    {
        var rig = new LocalSwapWorkerRig(Path.Combine(Path.GetTempPath(), "hp-lsw-" + Guid.NewGuid().ToString("N")));
        Part(rig.App, "api", From);
        Part(rig.App, "web", null);
        Part(rig.App, "watchdog", From);

        var prev = Path.Combine(rig.Work, "prev");
        Part(prev, "api", To);
        Part(prev, "web", null);
        Part(prev, "watchdog", To);
        File.WriteAllText(Path.Combine(prev, "version.txt"), To);
        File.WriteAllText(Path.Combine(prev, "replaced-by.txt"), From);
        var sb = new StringBuilder();
        foreach (var f in Directory.EnumerateFiles(prev, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(prev, f);
            if (!rel.Contains(Path.DirectorySeparatorChar)) continue; // 목록 파일 셋은 빼고 세 폴더 안만
            sb.Append(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))).ToLowerInvariant()).Append("  ").Append(rel).Append('\n');
        }
        File.WriteAllText(Path.Combine(prev, "sha256.txt"), sb.ToString());

        var request = new SwapRequest
        {
            Ticket = rig.Ticket,
            Mode = SwapModes.Rollback,
            State = SwapStates.Requested,
            From = From,
            To = To,
            Material = new SwapMaterial { Kind = SwapMaterialKinds.Prev, Path = prev },
            AppRoot = rig.App,
            Slot = 1,
            ApiPort = 5257,
            RequestedBy = "gate-user",
            RequestedAt = requestedAtUtc,
            Entry = SwapEntries.Menu,
        };
        File.WriteAllText(Path.Combine(rig.Work, LocalSwapLauncher.RequestFileName), request.ToJson(), new UTF8Encoding(false));

        Directory.CreateDirectory(Path.Combine(rig.Root, "services"));
        File.WriteAllText(Path.Combine(rig.Root, "services", "HitPanWatchdog"), "Running");
        // API 런처가 등록해 둔 1회용 작업(대역) — 일꾼이 끝에서 스스로 지워야 한다(G-SV)
        Directory.CreateDirectory(rig.TasksDir);
        File.WriteAllText(Path.Combine(rig.TasksDir, LocalSwapLauncher.TaskName), "task");
        return rig;
    }

    private static void Part(string root, string part, string? version)
    {
        var dir = Path.Combine(root, part);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, part + ".bin"), part + "-" + (version ?? "web") + "-" + root.Length);
        if (version is not null) File.WriteAllText(Path.Combine(dir, ".testversion"), version);
    }

    /// <summary>레포의 일꾼 원본 글자.</summary>
    public static string OriginalScript() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "HitPan.API", "Rollback", LocalSwapLauncher.ScriptFileName));

    /// <summary>일꾼을 돌린다. <paramref name="scriptText"/> = 대조군 사본(없으면 원본) · <paramref name="failAt"/> = 실패 주입(S1·S6·S7 …).</summary>
    public int Run(string? scriptText = null, string? failAt = null)
    {
        var script = Path.Combine(Root, "script", LocalSwapLauncher.ScriptFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        File.WriteAllText(script, scriptText ?? OriginalScript(), new UTF8Encoding(false));
        if (failAt is not null) File.WriteAllText(Path.Combine(Root, "fail-at.txt"), failAt);

        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
                     "-Mode", SwapModes.Rollback, "-Ticket", Ticket, "-TestRoot", Root })
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("powershell 을 못 띄웠다");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(180_000))
        {
            p.Kill(entireProcessTree: true);
            throw new TimeoutException("일꾼이 180초 안에 안 끝났다");
        }
        Output = stdout.Result + stderr.Result;
        return p.ExitCode;
    }

    public string Output { get; private set; } = "";

    public SwapRequest Final() =>
        SwapRequest.FromJson(File.ReadAllText(Path.Combine(Work, LocalSwapLauncher.RequestFileName)))
        ?? throw new InvalidOperationException("요청서를 못 읽었다");

    /// <summary>끝난 뒤 남은 <c>HitPan-*</c> 작업(대역 폴더의 파일 이름).</summary>
    public string[] LeftoverTasks() => Directory.Exists(TasksDir)
        ? Directory.GetFiles(TasksDir).Select(Path.GetFileName).Where(n => n!.StartsWith("HitPan", StringComparison.OrdinalIgnoreCase)).Select(n => n!).ToArray()
        : Array.Empty<string>();

    public string Calls()
    {
        var f = Path.Combine(Root, "calls.log");
        return File.Exists(f) ? File.ReadAllText(f) : "";
    }

    public string Log()
    {
        var f = Path.Combine(Work, "logs", "swap-" + Ticket + ".log");
        return File.Exists(f) ? File.ReadAllText(f) : "(일꾼 기록 없음) " + Output;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"[LocalSwapWorkerRig] 임시 폴더 정리 실패: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"[LocalSwapWorkerRig] 임시 폴더 정리 실패: {ex.Message}");
        }
    }

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("레포 루트(src/HitPan.sln)를 못 찾았다");
    }
}
