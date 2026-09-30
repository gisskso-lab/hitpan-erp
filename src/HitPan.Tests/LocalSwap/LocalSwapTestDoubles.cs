using System.Text.RegularExpressions;
using HitPan.API.Services.LocalSwap;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// 20260930작1 I-API — 교체 런처 시험 대역. 실제 예약 작업·서비스·설치 폴더 무접촉(작업 등록은 메모리 목록).
/// </summary>
internal sealed class FakeSwapEnvironment : ILocalSwapEnvironment
{
    public bool IsWindows { get; set; } = true;
    public string? AppRoot { get; set; }
    public string ScriptSourcePath { get; set; } = "";
    public string CurrentVersion { get; set; } = "1.3.48";
    public int? Slot { get; set; } = 1;
    public int ApiPort { get; set; } = 5257;
    public string WatchdogStagingDir { get; set; } = "";
    public DateTime UtcNow { get; set; } = DateTime.UtcNow;

    /// <summary>{root}\app 를 설치 폴더로 · 일꾼 원본은 {root}\src\local-swap.ps1(내용 무관 — 런처는 복사만).</summary>
    public static FakeSwapEnvironment Under(string root)
    {
        var app = Path.Combine(root, "app");
        Directory.CreateDirectory(Path.Combine(app, "api"));
        Directory.CreateDirectory(Path.Combine(app, "watchdog"));
        var src = Path.Combine(root, "src");
        Directory.CreateDirectory(src);
        var script = Path.Combine(src, LocalSwapLauncher.ScriptFileName);
        File.WriteAllText(script, "# stand-in");
        var staging = Path.Combine(root, "wdstaging");
        Directory.CreateDirectory(staging);
        return new FakeSwapEnvironment { AppRoot = app, ScriptSourcePath = script, WatchdogStagingDir = staging };
    }
}

/// <summary>
/// schtasks 대역 — 실제 schtasks 처럼 <c>/F</c> 없는 <c>/Create</c> 는 같은 이름이 있으면 실패한다.
/// </summary>
internal sealed class FakeSchtasks : ISchtasksRunner
{
    private static readonly Regex TaskNameArg = new("/TN \"([^\"]+)\"", RegexOptions.CultureInvariant);
    private readonly object _lock = new();

    public List<string> Calls { get; } = new();
    public HashSet<string> Tasks { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int CreateExit { get; set; }
    public int RunExit { get; set; }

    /// <summary>/Create 를 받았을 때 등록 전에 부른다(경합 시험용).</summary>
    public Action? OnCreate { get; set; }

    public int Run(string arguments)
    {
        string? name;
        lock (_lock)
        {
            Calls.Add(arguments);
            var m = TaskNameArg.Match(arguments);
            name = m.Success ? m.Groups[1].Value : null;
            if (arguments.StartsWith("/Query", StringComparison.Ordinal))
                return name is not null && Tasks.Contains(name) ? 0 : 1;
            if (arguments.StartsWith("/Delete", StringComparison.Ordinal))
            {
                if (name is not null) Tasks.Remove(name);
                return 0;
            }
            if (arguments.StartsWith("/Run", StringComparison.Ordinal)) return RunExit;
        }

        if (arguments.StartsWith("/Create", StringComparison.Ordinal))
        {
            OnCreate?.Invoke();
            lock (_lock)
            {
                if (CreateExit != 0) return CreateExit;
                if (name is null) return 1;
                var force = Regex.IsMatch(arguments, @"(^|\s)/F(\s|$)");
                if (Tasks.Contains(name) && !force) return 1;
                Tasks.Add(name);
                return 0;
            }
        }
        return 0;
    }

    public int CountStartingWith(string prefix)
    {
        lock (_lock) return Calls.Count(c => c.StartsWith(prefix, StringComparison.Ordinal));
    }

    public string? LastCreate()
    {
        lock (_lock) return Calls.LastOrDefault(c => c.StartsWith("/Create", StringComparison.Ordinal));
    }
}

/// <summary>작업 폴더 판정 — 실제 공용 판정(BackupService C-8·C-12)을 부르되 그 앞에 끼어들 자리를 둔다.</summary>
internal sealed class HookedFolderGuard : ISwapFolderGuard
{
    public Action<string>? Before { get; set; }

    public void EnsureSafe(string path)
    {
        Before?.Invoke(path);
        HitPan.Application.Services.BackupService.EnsureRestrictedSystemFolder(path);
    }
}
