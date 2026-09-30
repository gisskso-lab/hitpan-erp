using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// 20260930작1 I-API 3 — 이벤트 번호 겹침 0 (계약 <c>20260930_계약_local-swap_request.md</c> §9).
/// </summary>
/// <remarks>
/// 🟢 번호 목록을 코드 쪽에서 외워 두지 않는다 — 코드가 <b>실제로 쓰는 번호</b>를 뽑아(일꾼은 <c>$EventBase</c> + <c>Complete-Swap</c>
/// 오프셋 표를 계산) 계약 표와 대조한다. 🔴 음성대조군: 일꾼 update 기준을 28065 로 옮긴 <b>사본 글자</b>는 같은 판정에서 FAIL 해야 한다.
/// DB·설치본·이벤트로그 무접촉(레포 파일 읽기만).
/// </remarks>
public sealed class LocalSwapEventIdGateTests
{
    private static readonly Regex Id = new(@"\b280\d{2}\b", RegexOptions.CultureInvariant);

    /// <summary>API 는 상수 선언만 센다(주석에 적힌 대역 설명은 번호 사용이 아니다).</summary>
    private static readonly Regex ApiConst = new(@"const\s+int\s+\w+\s*=\s*(280\d{2})\s*;", RegexOptions.CultureInvariant);

    /// <summary>계약 §9 — 번호 → 주인.</summary>
    private static readonly Dictionary<int, string> Table = new()
    {
        [28008] = "installer",
        [28030] = "watchdog",
        [28031] = "watchdog",
        [28040] = "worker-rollback",
        [28041] = "worker-rollback",
        [28042] = "worker-rollback",
        [28043] = "worker-rollback",
        [28044] = "worker-rollback",
        [28060] = "worker-update",
        [28061] = "worker-update",
        [28062] = "worker-update",
        [28063] = "worker-update",
        [28064] = "worker-update",
        [28065] = "api-manual",
    };

    [Fact(DisplayName = "I-API3 이벤트 번호 — 코드가 쓰는 280xx 는 계약 §9 표와 같고 주인 둘이 한 번호를 안 쓴다 (음성대조군 포함)")]
    public void EventIds_match_contract_and_do_not_overlap()
    {
        var root = RepoRoot();
        var worker = File.ReadAllText(Path.Combine(root, "src", "HitPan.API", "Rollback", "local-swap.ps1"));
        var usage = File.ReadAllText(Path.Combine(root, "src", "HitPan.API", "Services", "ManualUpdate", "ManualUsageLog.cs"));
        var watchdog = File.ReadAllText(Path.Combine(root, "src", "HitPan.Watchdog", "AutoUpdate", "UpdateOrchestrator.cs"));
        var installerPath = Path.Combine(root, "installer", "scripts", "InstallWatchdog.ps1");
        var installer = File.Exists(installerPath) ? File.ReadAllText(installerPath) : "";

        var problems = Judge(Collect(worker, usage, watchdog, installer));
        Assert.True(problems.Count == 0, "이벤트 번호 불일치: " + string.Join(" / ", problems));

        // 🔴 음성대조군 — 일꾼 update 기준을 28065 로 옮긴 사본: 28065 가 API 번호와 겹치고 28066~ 은 표 밖 ⇒ FAIL 이어야 한다.
        var badWorker = worker.Replace("$EventBase = 28060", "$EventBase = 28065", StringComparison.Ordinal);
        Assert.NotEqual(worker, badWorker);
        Assert.NotEmpty(Judge(Collect(badWorker, usage, watchdog, installer)));
    }

    /// <summary>번호 → 쓰는 주인들.</summary>
    private static Dictionary<int, HashSet<string>> Collect(string worker, string usage, string watchdog, string installer)
    {
        var used = new Dictionary<int, HashSet<string>>();
        void Add(int id, string owner)
        {
            if (!used.TryGetValue(id, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                used[id] = set;
            }
            set.Add(owner);
        }

        // 일꾼: 기준 두 개 × (시작 0 + Complete-Swap 오프셋 표)
        var baseUpdate = Regex.Match(worker, @"^\$EventBase = (\d+)\s*$", RegexOptions.Multiline);
        var baseRollback = Regex.Match(worker, @"if \(\$Mode -eq 'rollback'\) \{ \$EventBase = (\d+) \}");
        var table = Regex.Match(worker, @"\$offset = @\{([^}]*)\}\[\$state\]");
        Assert.True(baseUpdate.Success && baseRollback.Success && table.Success,
            "일꾼의 이벤트 기준·오프셋 표를 못 읽었다 — 일꾼 모양이 바뀌었으면 게이트를 같이 고쳐라");
        var offsets = Regex.Matches(table.Groups[1].Value, @"=\s*(\d+)")
            .Select(m => int.Parse(m.Groups[1].Value)).Prepend(0).ToList();
        Assert.True(offsets.Count >= 5, "일꾼 Complete-Swap 오프셋 표가 5칸(시작·성공·거부·원위치·망가짐)보다 적다");
        foreach (var o in offsets)
        {
            Add(int.Parse(baseUpdate.Groups[1].Value) + o, "worker-update");
            Add(int.Parse(baseRollback.Groups[1].Value) + o, "worker-rollback");
        }

        foreach (Match m in ApiConst.Matches(usage)) Add(int.Parse(m.Groups[1].Value), "api-manual");
        foreach (Match m in Id.Matches(watchdog)) Add(int.Parse(m.Value), "watchdog");
        foreach (Match m in Id.Matches(installer)) Add(int.Parse(m.Value), "installer");
        return used;
    }

    private static List<string> Judge(Dictionary<int, HashSet<string>> used)
    {
        var problems = new List<string>();
        foreach (var (id, owners) in used)
        {
            if (owners.Count > 1) problems.Add($"{id} 을 둘 이상이 씀({string.Join(",", owners)})");
            if (!Table.TryGetValue(id, out var expected)) problems.Add($"{id} 은 계약 §9 표에 없음");
            else if (!owners.Contains(expected)) problems.Add($"{id} 의 주인이 표({expected})와 다름({string.Join(",", owners)})");
        }
        if (!used.ContainsKey(28065)) problems.Add("API 수동 업데이트 번호(28065)를 코드에서 못 찾음");
        return problems;
    }

    private static string RepoRoot()
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
