using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HitPan.API.Services.LocalSwap;
using Xunit;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// 🚨 20260930작1 봉합 K1 — 교체 일꾼(<c>local-swap.ps1</c>) 봉합 게이트: G-R2(조기 종료 · 설계 §14-1 06ⓐ) · G-S4(겹침 · 14-2 08).
/// </summary>
/// <remarks>
/// <para>🟢 일꾼 <c>-TestRoot</c> 모드만 쓴다(<see cref="LocalSwapWorkerRig"/>) — 예약 작업·서비스·프로세스는 <c>{root}</c> 아래 대역 파일.</para>
/// <para>🔴 <b>G-R2 가 잡는 사고</b> — 조기 종료(<c>exit 2</c>·<c>exit 3~8</c>)가 try 밖이라 1회용 작업 <c>HitPan-LocalSwap</c> 을 안 지웠다.
/// 런처 <c>CheckBusy</c> 는 작업이 있으면 무조건 「진행 중」 ⇒ 되돌리기·수동 업데이트가 영구히 막힌다(병렬이슈 06).</para>
/// </remarks>
public sealed class LocalSwapWorkerSealGateTests
{
    // ══════════════════════════════════════════════════════════════
    // G-R2 — 조기 종료는 1회용 작업만 지운다(잠금·요청서 무접촉)
    // ══════════════════════════════════════════════════════════════

    /// <summary>조기 종료 장면 — (이름, 종료 코드). 설계 게이트 표의 셋 = schema2 · ticket · done.</summary>
    public static IEnumerable<object[]> EarlyCases() => new[]
    {
        new object[] { "schema2", 5 },
        new object[] { "ticket", 6 },
        new object[] { "done", 8 },
        new object[] { "mode", 7 },
        new object[] { "missing", 3 },
        new object[] { "unreadable", 4 },
        new object[] { "badarg", 2 },
    };

    private sealed record EarlyResult(int Exit, string[] Left, string LockBefore, string LockAfter, string? RequestBefore, string? RequestAfter, string Calls, string Log);

    private static EarlyResult RunEarly(string scene, string? scriptText = null)
    {
        using var rig = LocalSwapWorkerRig.Rollback(DateTime.UtcNow);
        // 한 번에 하나 잠금(API 가 이 번호로 만든 것) — 조기 종료는 이것을 건드리면 안 된다
        var lockBody = rig.Ticket + "|" + DateTime.UtcNow.ToString("o");
        File.WriteAllText(rig.SwapLockPath, lockBody);
        string? ticketArg = null;
        switch (scene)
        {
            case "schema2": rig.SetRequestField("schema", 2); break;
            case "ticket": rig.SetRequestField("ticket", LocalSwapLauncher.NewTicket()); break;
            case "done": rig.SetRequestField("state", "done"); break;
            case "mode": rig.SetRequestField("mode", SwapModes.Update); break;
            case "missing": File.Delete(rig.RequestPath); break;
            case "unreadable": File.WriteAllText(rig.RequestPath, "{ not json"); break;
            case "badarg": ticketArg = "not-a-ticket"; break;
            default: throw new ArgumentException(scene);
        }
        var requestBefore = File.Exists(rig.RequestPath) ? File.ReadAllText(rig.RequestPath) : null;
        Assert.Contains(LocalSwapLauncher.TaskName, rig.LeftoverTasks()); // 작업은 미리 있다(API 가 등록한 모양)

        var exit = rig.Run(scriptText, null, ticketArg);
        return new EarlyResult(
            exit,
            rig.LeftoverTasks(),
            lockBody,
            File.Exists(rig.SwapLockPath) ? File.ReadAllText(rig.SwapLockPath) : "(없음)",
            requestBefore,
            File.Exists(rig.RequestPath) ? File.ReadAllText(rig.RequestPath) : null,
            rig.Calls(),
            rig.Log());
    }

    [Theory(DisplayName = "K1 G-R2 🚨 조기 종료 → 1회용 작업 잔존 0 · swap.lock 무접촉 · 요청서 무접촉 · 아무것도 안 멈춤")]
    [MemberData(nameof(EarlyCases))]
    public void Early_exit_removes_only_the_one_time_task(string scene, int code)
    {
        var r = RunEarly(scene);
        Assert.True(r.Exit == code, $"{scene}: 종료 코드 {r.Exit}(기대 {code}) / {r.Log}");
        Assert.DoesNotContain(LocalSwapLauncher.TaskName, r.Left);
        Assert.Equal(r.LockBefore, r.LockAfter);
        Assert.Equal(r.RequestBefore, r.RequestAfter);
        Assert.DoesNotContain("/DISABLE", r.Calls);
        Assert.DoesNotContain("taskkill", r.Calls);
    }

    /// <summary>대조군 사본 — 조기 종료를 옛 모양(<c>exit N</c> 만 · 작업 안 지움)으로 되돌린다.</summary>
    public static string ControlPlainExit()
    {
        var original = LocalSwapWorkerRig.OriginalScript();
        var copy = Regex.Replace(original, @"Exit-Early (\d) \}", "exit $1 }");
        Assert.True(Regex.Matches(original, @"Exit-Early \d \}").Count == 7, "조기 종료 7줄을 못 찾았다 — 대조군을 같이 고쳐라");
        return copy;
    }

    [Theory(DisplayName = "K1 G-R2 대조군 🔴 옛 일꾼(exit N 만) → 1회용 작업이 남는다(게이트가 FAIL 을 낸다)")]
    [InlineData("schema2", 5)]
    [InlineData("ticket", 6)]
    [InlineData("done", 8)]
    public void Control_plain_exit_leaves_task(string scene, int code)
    {
        var r = RunEarly(scene, ControlPlainExit());
        Assert.Equal(code, r.Exit);
        Assert.Contains(LocalSwapLauncher.TaskName, r.Left);
    }

    // ══════════════════════════════════════════════════════════════
    // G-S4 — S3(워치독 정지) 뒤 한 번 더 잰다 · 남의 교체가 끼어 있으면 폴더 무접촉
    // ══════════════════════════════════════════════════════════════
    // 주입(일꾼 -TestRoot 모드만):
    //   OVL  = S2 끝(S3 잠금 전)에 남의 update.lock 본문 + app\web.old  — 설계 게이트 표 그대로
    //   OVLK = S3 잠금 뒤(멈추는 도중)에 남의 update.lock 본문만     — 「본문 ≠ 내가 마지막에 쓴 본문」 갈래 단독 증명
    //   pre-old = 시작 전부터 app\api.old 가 있다(S0 은 .old 를 안 본다 · S3 뒤 재판정만 잡는다)

    private const string ForeignVersion = "9.9.9";

    private static LocalSwapWorkerRig RunOverlap(string scene, string? scriptText = null)
    {
        var rig = LocalSwapWorkerRig.Rollback(DateTime.UtcNow);
        string? failAt = null;
        switch (scene)
        {
            case "OVL": failAt = "OVL"; break;
            case "OVLK": failAt = "OVLK"; break;
            case "pre-old": Directory.CreateDirectory(Path.Combine(rig.App, "api.old")); break;
            default: throw new ArgumentException(scene);
        }
        var exit = rig.Run(scriptText, failAt);
        Assert.True(exit == 0, "일꾼 종료 코드 " + exit + " / " + rig.Log());
        return rig;
    }

    [Theory(DisplayName = "K1 G-S4 🚨 멈춘 뒤 남의 교체가 보이면 → refused/update_in_progress · 세 폴더 판 그대로 · .rbk 0 · keepalive /ENABLE · 서비스 Running · 작업 잔존 0")]
    [InlineData("OVL")]
    [InlineData("OVLK")]
    [InlineData("pre-old")]
    public void Overlap_after_stop_refuses_and_touches_no_folder(string scene)
    {
        using var rig = RunOverlap(scene);
        var final = rig.Final();
        var why = rig.Log() + "\n--- calls ---\n" + rig.Calls();
        Assert.True(final.State == SwapStates.Refused, scene + ": 끝 상태 " + final.State + " / " + why);
        Assert.Equal(SwapReasons.UpdateInProgress, final.Reason);
        Assert.Equal("S3", final.Step);

        // 세 폴더 판 그대로(지금 판 1.3.48) · 재료(prev)도 그대로 · .rbk 0
        Assert.Equal(LocalSwapWorkerRig.From, LocalSwapWorkerRig.PartMark(rig.App, "api"));
        Assert.Equal(LocalSwapWorkerRig.From, LocalSwapWorkerRig.PartMark(rig.App, "watchdog"));
        Assert.Equal(LocalSwapWorkerRig.To, LocalSwapWorkerRig.PartMark(Path.Combine(rig.Work, "prev"), "api"));
        foreach (var p in new[] { "api", "web", "watchdog" })
            Assert.False(Directory.Exists(Path.Combine(rig.App, p + ".rbk")), p + ".rbk 가 생겼다(폴더를 건드렸다) / " + why);

        // 멈췄던 것을 다시 켰다
        Assert.EndsWith("/ENABLE", rig.LastCall("HitPan-ERP-API-keepalive-1"), StringComparison.Ordinal);
        Assert.EndsWith("/ENABLE", rig.LastCall("HitPan-ERP-WEB-keepalive-1"), StringComparison.Ordinal);
        Assert.EndsWith("/ENABLE", rig.LastCall("HitPanWatchdogGuardian"), StringComparison.Ordinal);
        Assert.Equal("Running", rig.ServiceState());
        Assert.Empty(rig.LeftoverTasks()); // 1회용 작업 · 이 판이 만든 부팅 안전망 모두 지움
    }

    [Fact(DisplayName = "K1 G-S4 🟢 남의 update.lock(OVLK)은 지우지 않는다 — 끝난 뒤에도 남의 본문 그대로")]
    public void Foreign_lock_is_left_alone()
    {
        using var rig = RunOverlap("OVLK");
        Assert.True(File.Exists(rig.UpdateLockPath), "남의 update.lock 을 지웠다 / " + rig.Log());
        Assert.EndsWith("|" + ForeignVersion, File.ReadAllText(rig.UpdateLockPath), StringComparison.Ordinal);
    }

    /// <summary>대조군 사본 — 정지 뒤 재판정 한 줄을 뺀다.</summary>
    public static string ControlNoRecheck()
    {
        var original = LocalSwapWorkerRig.OriginalScript();
        const string line = "if ($null -eq $failReason) { $overlap = Test-OverlapAfterStop }";
        Assert.True(original.Contains(line, StringComparison.Ordinal), "정지 뒤 재판정 줄을 못 찾았다 — 대조군을 같이 고쳐라");
        return original.Replace(line, "# (control) recheck removed", StringComparison.Ordinal);
    }

    [Theory(DisplayName = "K1 G-S4 대조군 🔴 정지 뒤 재판정 뺀 사본 → S4 로 간다(.rbk 생김 · refused 아님 — 게이트가 FAIL 을 낸다)")]
    [InlineData("OVL")]
    [InlineData("OVLK")]
    [InlineData("pre-old")]
    public void Control_without_recheck_goes_on_to_swap(string scene)
    {
        using var rig = RunOverlap(scene, ControlNoRecheck());
        Assert.NotEqual(SwapStates.Refused, rig.Final().State);
        Assert.Contains("S4 swapped api", rig.Log());
    }
}
