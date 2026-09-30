using System.Text.RegularExpressions;
using HitPan.API.Services.LocalSwap;
using Xunit;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// 🚨 20260930작1 봉합 K2 — 교체 일꾼(<c>local-swap.ps1</c>) 고객 자료 옮겨 싣기 게이트 G-CF1~CF4(설계 §14-1 F-2 · §14-3).
/// </summary>
/// <remarks>
/// <para>🔴 <b>잡는 사고</b> — 교체 단위가 <c>{app}\api</c> 폴더 통째라, 그 안의 고객 자료
/// <c>chat-files</c>(채팅 첨부 기본 위치)·<c>HitpanBackup</c>(백업 최후 폴백 · 사장님 결재 S-4)가
/// <c>api.rbk</c>·<c>rollback\prev</c> 와 함께 밀려났다가 지워졌다.</para>
/// <para>🟢 일꾼 <c>-TestRoot</c> 모드만 쓴다(<see cref="LocalSwapWorkerRig"/>) — 예약 작업·서비스·프로세스는 <c>{root}</c> 아래 대역 파일.</para>
/// <para>시드 = 두 폴더 모두(<c>chat-files\T1\202609\{guid}.*</c> 3개 · <c>HitpanBackup\*.sql</c> 2개 · 경로+SHA-256).</para>
/// </remarks>
public sealed class LocalSwapChatFilesGateTests
{
    // ── 판 차리기 ──
    //   prev   = 되돌리기 · 재료 ① rollback\prev (1.3.48 → 1.3.47)
    //   zip    = 되돌리기 · 재료 ② 워치독 받은 zip (1.3.48 → 1.3.47)
    //   update = 수동 업데이트 · manual zip (1.3.47 → 1.3.48)
    private static LocalSwapWorkerRig Make(string kind) => kind switch
    {
        "prev" => LocalSwapWorkerRig.Rollback(DateTime.UtcNow),
        "zip" => LocalSwapWorkerRig.RollbackZip(DateTime.UtcNow),
        "update" => LocalSwapWorkerRig.UpdateZip(DateTime.UtcNow, LocalSwapWorkerRig.To, LocalSwapWorkerRig.From),
        _ => throw new ArgumentException(kind),
    };

    private static string ApiDir(LocalSwapWorkerRig rig) => Path.Combine(rig.App, "api");

    private static string Why(LocalSwapWorkerRig rig) => rig.Log() + "\n--- calls ---\n" + rig.Calls();

    /// <summary><paramref name="expected"/> 의 모든 (경로, 해시)가 <paramref name="actual"/> 에 그대로 있나.</summary>
    private static void AssertContainsAll(Dictionary<string, string> expected, Dictionary<string, string> actual, string where)
    {
        foreach (var (rel, hash) in expected)
        {
            Assert.True(actual.TryGetValue(rel, out var h), where + ": " + rel + " 가 없다(소실)");
            Assert.True(string.Equals(hash, h, StringComparison.OrdinalIgnoreCase), where + ": " + rel + " 내용이 바뀌었다");
        }
    }

    private static Dictionary<string, string> Union(params string[] apiDirs)
    {
        var all = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in apiDirs)
            foreach (var (rel, hash) in LocalSwapWorkerRig.CarryFiles(d))
                all.TryAdd(rel, hash);
        return all;
    }

    // ── 대조군 사본(봉합을 뺀 글자) — 봉합 줄을 못 찾으면 먼저 알린다 ──

    private static string Require(string text, string anchor, string what)
    {
        Assert.True(text.Contains(anchor, StringComparison.Ordinal), what + " 줄을 못 찾았다 — 대조군을 같이 고쳐라");
        return anchor;
    }

    /// <summary><c>Move-CarryData</c> 를 빈 함수로(옮겨 싣기 0).</summary>
    public static string ControlEmptyCarry(string? text = null)
    {
        text ??= LocalSwapWorkerRig.OriginalScript();
        var a = Require(text, "function Move-CarryData([string]$fromApi, [string]$toApi) {", "Move-CarryData 머리");
        return text.Replace(a, "function Move-CarryData([string]$fromApi, [string]$toApi) { }\nfunction Move-CarryData-Unused([string]$fromApi, [string]$toApi) {", StringComparison.Ordinal);
    }

    /// <summary>S7 ⓑ(원위치 전 api → api.rbk 싣기 + 못 실으면 멈춤) 블록을 뺀다.</summary>
    public static string ControlNoS7Carry(string? text = null)
    {
        text ??= LocalSwapWorkerRig.OriginalScript();
        var re = new Regex(@"        if \(\$p -eq 'api' -and \(Test-Path -LiteralPath \$dst\)\) \{\r?\n.*?\r?\n.*?\r?\n.*?\r?\n        \}\r?\n");
        Assert.True(re.Matches(text).Count == 1, "S7 ⓑ 블록을 못 찾았다 — 대조군을 같이 고쳐라");
        return re.Replace(text, "        # (control) S7 carry removed\n");
    }

    /// <summary>지우기 문지기를 옛 <c>Remove-DirSafe</c> 로(안을 안 보고 지운다).</summary>
    public static string ControlNoGatekeeper(string? text = null)
    {
        text ??= LocalSwapWorkerRig.OriginalScript();
        var a = Require(text, "function Remove-AppDirSafe([string]$dir) {", "Remove-AppDirSafe 머리");
        return text.Replace(a, "function Remove-AppDirSafe([string]$dir) { Remove-DirSafe $dir; return $true }\nfunction Remove-AppDirSafe-Unused([string]$dir) {", StringComparison.Ordinal);
    }

    /// <summary>S4 ⓐ(교체 직후 api.rbk → api 싣기) 한 줄을 뺀다.</summary>
    public static string ControlNoS4Carry(string? text = null)
    {
        text ??= LocalSwapWorkerRig.OriginalScript();
        var a = Require(text, "if ($p -eq 'api' -and -not (Move-CarryData $rbk $dst)) { Write-Log 'S4 customer data carry incomplete - rest stays in api.rbk' }", "S4 싣기");
        return text.Replace(a, "# (control) S4 carry removed", StringComparison.Ordinal);
    }

    /// <summary>S7 에서 못 실었는데도 api 를 지운다 — ⓑ 의 멈춤 줄 + 문지기를 뺀 사본(옛 일꾼과 같은 지우기).</summary>
    public static string ControlS7DeletesAnyway()
    {
        var text = LocalSwapWorkerRig.OriginalScript();
        var stop = Require(text, "if (Test-HasCarryData $dst) { throw ('S7 customer data could not be carried out of ' + $dst + ' - api left as it is') }", "S7 멈춤");
        text = text.Replace(stop, "# (control) S7 stop removed", StringComparison.Ordinal);
        return ControlNoGatekeeper(text);
    }

    // ══════════════════════════════════════════════════════════════
    // G-CF1 — 성공 세 판(되돌리기 prev · 되돌리기 zip · 수동 업데이트): 첨부·백업 = 시드
    // ══════════════════════════════════════════════════════════════

    [Theory(DisplayName = "K2 G-CF1 🚨 성공 → {app}\\api 의 chat-files·HitpanBackup = 시드(경로+해시) · api.rbk 없음 · prev·stage 에 고객 자료 0")]
    [InlineData("prev")]
    [InlineData("zip")]
    [InlineData("update")]
    public void Success_keeps_customer_data(string kind)
    {
        using var rig = Make(kind);
        var seed = rig.SeedCarry();
        Assert.Equal(5, seed.Count);
        Assert.Equal(0, rig.Run());
        var final = rig.Final();
        Assert.True(final.State == SwapStates.Success, kind + ": 끝 " + final.State + "/" + final.Reason + " / " + Why(rig));

        var now = LocalSwapWorkerRig.CarryFiles(ApiDir(rig));
        AssertContainsAll(seed, now, kind + " {app}\\api");
        Assert.Equal(seed.Count, now.Count);
        Assert.False(Directory.Exists(Path.Combine(rig.App, "api.rbk")), kind + ": api.rbk 가 남았다 / " + Why(rig));
        Assert.Empty(LocalSwapWorkerRig.CarryFiles(Path.Combine(rig.Work, "prev", "api")));
        Assert.False(Directory.Exists(Path.Combine(rig.Work, "stage")), kind + ": stage 가 남았다");
    }

    [Fact(DisplayName = "K2 G-CF1 🟢 덮어쓰기 0 — 옛 prev 에 같은 이름 첨부가 있으면 .dup-{번호} 로 둘 다 둔다")]
    public void Same_name_is_kept_twice_never_overwritten()
    {
        using var rig = LocalSwapWorkerRig.Rollback(DateTime.UtcNow);
        var seed = rig.SeedCarry();
        var clash = seed.Keys.First(k => k.StartsWith("chat-files", StringComparison.OrdinalIgnoreCase));
        // 봉합 전 일꾼이 남긴 prev 모양 — prev\api 안에 같은 이름의 다른 첨부
        var prevFile = Path.Combine(rig.Work, "prev", "api", clash);
        Directory.CreateDirectory(Path.GetDirectoryName(prevFile)!);
        File.WriteAllText(prevFile, "older attachment with the same name");
        var prevHash = LocalSwapWorkerRig.CarryFiles(Path.Combine(rig.Work, "prev", "api"))[clash];
        // prev 해시 목록에 넣지 않으면 재료 검사는 그 파일을 모른다(목록 안의 파일만 잰다) — 모양만 본다

        Assert.Equal(0, rig.Run());
        Assert.True(rig.Final().State == SwapStates.Success, Why(rig));
        var now = LocalSwapWorkerRig.CarryFiles(ApiDir(rig));
        Assert.Equal(prevHash, now[clash]);
        Assert.Equal(seed[clash], now[clash + ".dup-" + rig.Ticket]);
        Assert.Equal(seed.Count + 1, now.Count);
    }

    [Theory(DisplayName = "K2 G-CF1 대조군 🔴 Move-CarryData 빈 함수 → 성공 뒤 {app}\\api 의 고객 자료 0개(게이트가 FAIL 을 낸다)")]
    [InlineData("prev")]
    [InlineData("zip")]
    [InlineData("update")]
    public void Control_empty_carry_loses_data_from_live_api(string kind)
    {
        using var rig = Make(kind);
        rig.SeedCarry();
        Assert.Equal(0, rig.Run(ControlEmptyCarry()));
        Assert.Equal(SwapStates.Success, rig.Final().State);
        Assert.Empty(LocalSwapWorkerRig.CarryFiles(ApiDir(rig)));
    }

    // ══════════════════════════════════════════════════════════════
    // G-CF2 — 원위치(reverted): S5~S6 에 새 판이 쓴 첨부까지 옛 api 로
    // ══════════════════════════════════════════════════════════════

    [Theory(DisplayName = "K2 G-CF2 🚨 S6W(새 첨부 1개) + S6 확인 실패 → reverted · {app}\\api = 시드 + 새 파일")]
    [InlineData("prev")]
    [InlineData("zip")]
    public void Revert_carries_back_new_attachment(string kind)
    {
        using var rig = Make(kind);
        var seed = rig.SeedCarry();
        Assert.Equal(0, rig.Run(null, "S6W,S6"));
        var final = rig.Final();
        Assert.True(final.State == SwapStates.Reverted, kind + ": 끝 " + final.State + "/" + final.Reason + " / " + Why(rig));
        Assert.Equal(LocalSwapWorkerRig.From, LocalSwapWorkerRig.PartMark(rig.App, "api")); // 옛 판으로 돌아왔다

        var now = LocalSwapWorkerRig.CarryFiles(ApiDir(rig));
        AssertContainsAll(seed, now, kind + " {app}\\api");
        Assert.True(now.ContainsKey(rig.S6wRelPath), kind + ": S6 에 쓴 새 첨부가 없다 / " + Why(rig));
        Assert.Equal(seed.Count + 1, now.Count);
    }

    [Theory(DisplayName = "K2 G-CF2 대조군 🔴 S7 ⓑ(원위치 전 싣기) 뺀 사본 → reverted 아님 또는 {app}\\api 에 새 파일 없음(게이트가 FAIL 을 낸다)")]
    [InlineData("prev")]
    [InlineData("zip")]
    public void Control_without_s7_carry_loses_new_attachment(string kind)
    {
        using var rig = Make(kind);
        rig.SeedCarry();
        Assert.Equal(0, rig.Run(ControlNoS7Carry(), "S6W,S6"));
        var gatePasses = rig.Final().State == SwapStates.Reverted
                         && LocalSwapWorkerRig.CarryFiles(ApiDir(rig)).ContainsKey(rig.S6wRelPath);
        Assert.False(gatePasses, kind + ": 대조군인데 게이트가 통과했다 / " + Why(rig));
    }

    // ══════════════════════════════════════════════════════════════
    // G-CF3 — broken: {app}\api ∪ api.rbk ∪ rollback\prev\api 의 합 = 시드(0 손실)
    // ══════════════════════════════════════════════════════════════
    //   S6,S7R  = 원위치 첫 줄에서 던짐 · S6,S7R2 = 첫 폴더(watchdog) 원위치 뒤 던짐 — 설계 게이트 표 그대로
    //   S6W,S6,S7C = S7 싣기가 실패(주입) — 그래도 api 를 지우지 않고 broken 으로 멈춰야 한다(zip 재료는 원위치가 api 를 지운다)

    public static IEnumerable<object[]> BrokenCases() => new[]
    {
        new object[] { "prev", "S6,S7R" },
        new object[] { "prev", "S6,S7R2" },
        new object[] { "zip", "S6,S7R" },
        new object[] { "zip", "S6,S7R2" },
        new object[] { "prev", "S6W,S6,S7C" },
        new object[] { "zip", "S6W,S6,S7C" },
    };

    private static Dictionary<string, string> BrokenUnion(LocalSwapWorkerRig rig) =>
        Union(ApiDir(rig), Path.Combine(rig.App, "api.rbk"), Path.Combine(rig.Work, "prev", "api"));

    [Theory(DisplayName = "K2 G-CF3 🚨 broken → {app}\\api ∪ api.rbk ∪ rollback\\prev\\api 의 고객 자료 합 = 시드(0 손실 · S6W 새 첨부 포함)")]
    [MemberData(nameof(BrokenCases))]
    public void Broken_loses_nothing(string kind, string failAt)
    {
        using var rig = Make(kind);
        var seed = rig.SeedCarry();
        Assert.Equal(0, rig.Run(null, failAt));
        var final = rig.Final();
        Assert.True(final.State == SwapStates.Broken, kind + "/" + failAt + ": 끝 " + final.State + "/" + final.Reason + " / " + Why(rig));

        var all = BrokenUnion(rig);
        AssertContainsAll(seed, all, kind + "/" + failAt + " 합");
        if (failAt.Contains("S6W", StringComparison.Ordinal))
            Assert.True(all.ContainsKey(rig.S6wRelPath), kind + "/" + failAt + ": S6 새 첨부 소실 / " + Why(rig));
    }

    [Fact(DisplayName = "K2 G-CF3 대조군 🔴 못 실었는데도 지우는 사본(S7 멈춤 줄 + 문지기 뺌) · zip S7C → 시드·새 첨부 소실(게이트가 FAIL 을 낸다)")]
    public void Control_delete_without_gatekeeper_loses_data()
    {
        using var rig = Make("zip");
        var seed = rig.SeedCarry();
        Assert.Equal(0, rig.Run(ControlS7DeletesAnyway(), "S6W,S6,S7C"));
        var all = BrokenUnion(rig);
        var lost = seed.Keys.Count(k => !all.ContainsKey(k));
        Assert.True(lost > 0 || !all.ContainsKey(rig.S6wRelPath), "대조군인데 잃은 것이 없다 / " + Why(rig));
    }

    // ══════════════════════════════════════════════════════════════
    // G-CF4 — 수동 업데이트 성공 2회 연속(두 번째가 prev 를 지운다): 첨부·백업 = 시드
    // ══════════════════════════════════════════════════════════════

    private const string V1 = "1.3.47", V2 = "1.3.48", V3 = "1.3.49";

    private static LocalSwapWorkerRig TwoUpdates() => LocalSwapWorkerRig.UpdateZip(DateTime.UtcNow, V1, V2);

    private static (Dictionary<string, string> Seed, Dictionary<string, string> Now, Dictionary<string, string> Prev) RunTwoUpdates(LocalSwapWorkerRig rig, string? scriptText)
    {
        var seed = rig.SeedCarry();
        Assert.Equal(0, rig.Run(scriptText));
        Assert.True(rig.Final().State == SwapStates.Success, "1회차 " + rig.Final().State + "/" + rig.Final().Reason + " / " + Why(rig));
        rig.NextUpdate(DateTime.UtcNow, V2, V3);
        Assert.Equal(0, rig.Run(scriptText));
        Assert.True(rig.Final().State == SwapStates.Success, "2회차 " + rig.Final().State + "/" + rig.Final().Reason + " / " + Why(rig));
        Assert.Equal(V3, LocalSwapWorkerRig.PartMark(rig.App, "api"));
        Assert.Equal(V2, File.ReadAllText(Path.Combine(rig.Work, "prev", "version.txt")).Trim());
        return (seed, LocalSwapWorkerRig.CarryFiles(ApiDir(rig)), LocalSwapWorkerRig.CarryFiles(Path.Combine(rig.Work, "prev", "api")));
    }

    [Theory(DisplayName = "K2 G-CF4 🚨 수동 업데이트 2회 연속 → {app}\\api 고객 자료 = 시드 (봉합 전체 · 문지기 단독[S4 싣기 뺌] 둘 다)")]
    [InlineData("full")]
    [InlineData("gatekeeper-only")]
    public void Two_updates_keep_customer_data(string variant)
    {
        using var rig = TwoUpdates();
        var script = variant == "full" ? null : ControlNoS4Carry();
        var (seed, now, prev) = RunTwoUpdates(rig, script);
        AssertContainsAll(seed, now, variant + " {app}\\api");
        Assert.Equal(seed.Count, now.Count);
        Assert.Empty(prev);
    }

    [Theory(DisplayName = "K2 G-CF4 대조군 🔴 문지기를 Remove-DirSafe 로 되돌린 사본(+S4 싣기 뺌) → 두 번째 업데이트가 prev 와 함께 고객 자료를 지운다(게이트가 FAIL 을 낸다)")]
    [InlineData("no-gatekeeper+no-s4")]
    public void Control_without_gatekeeper_loses_data_on_second_update(string variant)
    {
        using var rig = TwoUpdates();
        var (seed, now, _) = RunTwoUpdates(rig, ControlNoGatekeeper(ControlNoS4Carry()));
        Assert.True(seed.Keys.Any(k => !now.ContainsKey(k)), variant + ": 대조군인데 잃은 것이 없다 / " + Why(rig));
    }

    // ══════════════════════════════════════════════════════════════
    // 🆕 G-CF3b(작1 봉합2 A2 · PM 범위 추가 10/1) — 되돌리기 경로에서 「지우기 전 문지기」 하나만 빼도 FAIL 하는 장면
    // ══════════════════════════════════════════════════════════════
    //   A0 변이 ④ 발견: 문지기 본문만 무력화하면 G-CF1~4 21건 중 되돌리기 쪽을 잡는 게이트가 0.
    //   S7(원위치)의 api 지우기는 멈춤 줄(Test-HasCarryData $dst)이 문지기와 같은 폴더를 먼저 본다 — 거기선 문지기가 단독일 수 없다.
    //   문지기가 유일한 줄인 되돌리기 장면 = S4 싣기가 다 못 끝나 고객 자료가 api.rbk 에 남은 채 성공(S4C) →
    //   성공 정리의 Remove-AppDirSafe(api.rbk) 만이 그 자료를 {app}\api 로 싣는다.

    /// <summary>A0 변이 ④ 그대로 — 문지기 본문 첫 줄에서 안을 안 보고 지운다(호출 자리 8곳 · 반환값 모양은 그대로).</summary>
    public static string ControlGatekeeperBodyOff(string? text = null)
    {
        text ??= LocalSwapWorkerRig.OriginalScript();
        var a = Require(text, "function Remove-AppDirSafe([string]$dir) {", "Remove-AppDirSafe 머리");
        return text.Replace(a, a + "\r\n    Remove-DirSafe $dir; return (-not (Test-Path -LiteralPath $dir))", StringComparison.Ordinal);
    }

    [Theory(DisplayName = "봉합2 G-CF3b 🚨 되돌리기 · S4 싣기 못 끝남(S4C) → 성공 정리의 문지기가 api.rbk 고객 자료를 {app}\\api 로 싣고 지운다 · {app}\\api = 시드")]
    [InlineData("prev")]
    [InlineData("zip")]
    public void Rollback_gatekeeper_alone_carries_what_S4_left(string kind)
    {
        using var rig = Make(kind);
        var seed = rig.SeedCarry();
        Assert.Equal(0, rig.Run(null, "S4C"));
        var final = rig.Final();
        Assert.True(final.State == SwapStates.Success, kind + ": 끝 " + final.State + "/" + final.Reason + " / " + Why(rig));
        Assert.Contains("injected S4C", rig.Log()); // S4 싣기가 정말 건너뛰어졌다 — 싣는 줄은 문지기뿐
        var now = LocalSwapWorkerRig.CarryFiles(ApiDir(rig));
        AssertContainsAll(seed, now, kind + " {app}\\api");
        Assert.Equal(seed.Count, now.Count);
        Assert.False(Directory.Exists(Path.Combine(rig.App, "api.rbk")), kind + ": api.rbk 가 남았다 / " + Why(rig));
    }

    [Theory(DisplayName = "봉합2 G-CF3b 대조군 🔴 문지기 본문만 무력화(A0 변이 ④) · S4C → api.rbk 와 함께 고객 자료 소실(게이트가 FAIL 을 낸다)")]
    [InlineData("prev")]
    [InlineData("zip")]
    public void Control_gatekeeper_body_off_loses_what_S4_left(string kind)
    {
        using var rig = Make(kind);
        var seed = rig.SeedCarry();
        Assert.Equal(0, rig.Run(ControlGatekeeperBodyOff(), "S4C"));
        var all = Union(ApiDir(rig), Path.Combine(rig.App, "api.rbk"), Path.Combine(rig.Work, "prev", "api"));
        Assert.True(seed.Keys.Any(k => !all.ContainsKey(k)), kind + ": 대조군인데 잃은 것이 없다 / " + Why(rig));
    }
}
