using System.Globalization;
using HitPan.API.Services.LocalSwap;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// 20260930작1 봉합2 차례 B1 — 설계 §15-3 게이트 G-MX1(<c>.rbk</c> 판정표 · 런처) · G-OLD2 런처 판정((a)(b) <c>CheckBusy</c>).
/// </summary>
/// <remarks>
/// 🟢 실제 <see cref="LocalSwapLauncher"/> 를 부른다(글자 검사 아님). 설치 폴더 = 임시 폴더 · 예약 작업 = 메모리 대역(<see cref="FakeSchtasks"/>) ·
/// 실제 schtasks·서비스·<c>C:\Program Files\HitPan</c>·DB 0.
/// 🔴 대조군(봉합 뺀 사본에서 FAIL)은 개발명세서 <c>docs/개발/erp/20260930작1_봉합2_개발명세서_B1.md</c> 에 기록.
/// G-OLD2 서비스 부분(수동 <c>Start</c> 받기·백업 0 · 되돌리기 번호 0)은 차례 B2.
/// </remarks>
public sealed class LocalSwapLeftoverLauncherGateTests : IDisposable
{
    private const string V47 = "1.3.47";
    private const string V48 = "1.3.48";
    private const string V49 = "1.3.49";
    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "hp-seal2-b1-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"[LocalSwapLeftoverLauncher] 임시 폴더 정리 실패: {ex.Message}");
        }
    }

    private static LocalSwapLauncher Launcher(FakeSwapEnvironment env, ISchtasksRunner sc) =>
        new(env, sc, new HookedFolderGuard(), NullLogger<LocalSwapLauncher>.Instance);

    private static string Work(FakeSwapEnvironment env) => Path.Combine(env.AppRoot!, LocalSwapLauncher.WorkFolderName);

    /// <summary>세 폴더가 다 살아 있고 api·watchdog 판이 <paramref name="version"/> 인 설치 · <c>api.rbk</c> 잔재 하나.</summary>
    private FakeSwapEnvironment Installed(string name, string version)
    {
        var env = FakeSwapEnvironment.Under(Path.Combine(_root, name));
        env.UtcNow = T0;
        env.CurrentVersion = version;
        env.WatchdogVersion = version;
        Directory.CreateDirectory(Path.Combine(env.AppRoot!, "web"));
        var rbk = Path.Combine(env.AppRoot!, "api.rbk");
        Directory.CreateDirectory(Path.Combine(rbk, "chat-files"));
        File.WriteAllText(Path.Combine(rbk, "chat-files", "a.txt"), "attachment");
        File.WriteAllText(Path.Combine(rbk, "HitPan.API.dll"), "old");
        return env;
    }

    /// <summary>
    /// 일꾼이 적은 끝 상태 요청서를 <b>글자 그대로</b> 쓴다(계약 §3 봉합 2차 <c>parts_after</c> 를 런처가 실제로 읽는지까지 잰다).
    /// 끝난 지 60분 — 쿨다운(10분) 밖.
    /// </summary>
    private static void Ended(FakeSwapEnvironment env, string state, string? reason, string from, string to, string? afterApi, string? afterWd)
    {
        Directory.CreateDirectory(Work(env));
        var at = T0.AddMinutes(-60).ToString("o", CultureInfo.InvariantCulture);
        var parts = afterApi is null ? "" : $",\n  \"parts_after\": {{ \"api\": \"{afterApi}\", \"watchdog\": \"{afterWd}\" }}";
        var reasonJson = reason is null ? "null" : "\"" + reason + "\"";
        var json =
            "{\n" +
            "  \"schema\": 1,\n" +
            $"  \"ticket\": \"{LocalSwapLauncher.NewTicket()}\",\n" +
            "  \"mode\": \"rollback\",\n" +
            $"  \"state\": \"{state}\",\n" +
            $"  \"reason\": {reasonJson},\n" +
            $"  \"from\": \"{from}\",\n" +
            $"  \"to\": \"{to}\",\n" +
            "  \"material\": { \"kind\": \"prev\", \"path\": \"x\", \"sha256\": null },\n" +
            "  \"app_root\": \"x\",\n" +
            "  \"slot\": 1,\n" +
            "  \"api_port\": 5257,\n" +
            "  \"requested_by\": \"gate-user\",\n" +
            $"  \"requested_at\": \"{at}\",\n" +
            "  \"entry\": \"menu\",\n" +
            "  \"auto_state\": null,\n" +
            $"  \"updated_at\": \"{at}\",\n" +
            "  \"step\": \"S7\"" + parts + "\n" +
            "}";
        File.WriteAllText(Path.Combine(Work(env), LocalSwapLauncher.RequestFileName), json);
    }

    // ══════════════════════════════════════════════════════════════
    // G-MX1 — 설계 §15-0 판정표(런처). 7줄 + 판 서로 다름 1줄.
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-MX1 🔴 .rbk 잔재 M5 — 성공 뒤 판 맞음 = 바쁨 아님(null) · 잔재 폴더 무접촉 (종전: update_in_progress 영구 거짓 표시)")]
    public void GMX1_success_same_version_is_leftover()
    {
        var env = Installed("mx1-success", V48);
        Ended(env, SwapStates.Success, null, V47, V48, V48, V48);
        var launcher = Launcher(env, new FakeSchtasks());

        Assert.Equal(V48, launcher.ReadLast()!.PartsAfter!.Api); // parts_after 를 실제로 읽는다
        Assert.Null(launcher.CheckBusy());
        Assert.True(File.Exists(Path.Combine(env.AppRoot!, "api.rbk", "chat-files", "a.txt"))); // 런처는 치우지 않는다(일꾼 S0 몫)
    }

    [Fact(DisplayName = "G-MX1 🔴 M4 — broken + parts_after = 지금 판 ⇒ swap_interrupted (S7R: .rbk 가 검증된 옛 판의 유일한 사본)")]
    public void GMX1_broken_parts_after_same_is_interrupted()
    {
        var env = Installed("mx1-broken-same", V48);
        Ended(env, SwapStates.Broken, SwapReasons.RevertFailed, V47, V48, V48, V48);
        Assert.Equal(SwapReasons.SwapInterrupted, Launcher(env, new FakeSchtasks()).CheckBusy());
    }

    [Fact(DisplayName = "G-MX1 🔴 M5 — broken + parts_after 와 지금 판 다름 · 판 맞음(워치독 업데이트가 맞춤) ⇒ 바쁨 아님(null)")]
    public void GMX1_broken_parts_after_changed_is_leftover()
    {
        var env = Installed("mx1-broken-moved", V49);
        Ended(env, SwapStates.Broken, SwapReasons.RevertFailed, V47, V48, V48, V47); // 섞인 판으로 끝났던 기록
        Assert.Null(Launcher(env, new FakeSchtasks()).CheckBusy());
    }

    [Fact(DisplayName = "G-MX1 🔴 M4 — worker_interrupted(parts_after 없음) + 지금 판 ∈ {from,to} ⇒ swap_interrupted")]
    public void GMX1_interrupted_version_in_from_to_is_interrupted()
    {
        foreach (var now in new[] { V47, V48 })
        {
            var env = Installed("mx1-int-" + now, now);
            Ended(env, SwapStates.Broken, SwapReasons.WorkerInterrupted, V48, V47, null, null);
            Assert.Null(Launcher(env, new FakeSchtasks()).ReadLast()!.PartsAfter);
            Assert.Equal(SwapReasons.SwapInterrupted, Launcher(env, new FakeSchtasks()).CheckBusy());
        }
    }

    [Fact(DisplayName = "G-MX1 M5 — worker_interrupted(parts_after 없음) + 지금 판이 from·to 둘 다와 다름(그 뒤 판이 바뀜) ⇒ 바쁨 아님(null)")]
    public void GMX1_interrupted_version_moved_is_leftover()
    {
        var env = Installed("mx1-int-moved", V49);
        Ended(env, SwapStates.Broken, SwapReasons.WorkerInterrupted, V48, V47, null, null);
        Assert.Null(Launcher(env, new FakeSchtasks()).CheckBusy());
    }

    [Fact(DisplayName = "G-MX1 🔴 M2 — api.rbk 있는데 살아 있는 api 없음 ⇒ swap_interrupted · 폴더 무접촉")]
    public void GMX1_live_part_missing_is_interrupted()
    {
        var env = Installed("mx1-noapi", V48);
        Ended(env, SwapStates.Success, null, V47, V48, V48, V48);
        Directory.Delete(Path.Combine(env.AppRoot!, "api"), recursive: true);

        Assert.Equal(SwapReasons.SwapInterrupted, Launcher(env, new FakeSchtasks()).CheckBusy());
        Assert.False(Directory.Exists(Path.Combine(env.AppRoot!, "api")));
        Assert.True(File.Exists(Path.Combine(env.AppRoot!, "api.rbk", "chat-files", "a.txt")));
    }

    [Fact(DisplayName = "G-MX1 🔴 M3 — 워치독 판 못 읽음(null) · api·watchdog 판 서로 다름 ⇒ swap_interrupted")]
    public void GMX1_versions_unreadable_or_mixed_is_interrupted()
    {
        var env = Installed("mx1-wd-null", V48);
        Ended(env, SwapStates.Success, null, V47, V48, V48, V48);
        env.WatchdogVersion = null;
        Assert.Equal(SwapReasons.SwapInterrupted, Launcher(env, new FakeSchtasks()).CheckBusy());

        var env2 = Installed("mx1-mixed", V48);
        Ended(env2, SwapStates.Success, null, V47, V48, V48, V48);
        env2.WatchdogVersion = V47;
        Assert.Equal(SwapReasons.SwapInterrupted, Launcher(env2, new FakeSchtasks()).CheckBusy());
    }

    [Fact(DisplayName = "G-MX1 🔴 판정표 전체 — M5 만 null · 나머지 swap_interrupted · update_in_progress 0 · Launch 는 swap_interrupted 에서 작업 등록 0")]
    public void GMX1_table_never_says_update_in_progress()
    {
        var results = new List<(string Row, string? Reason)>();

        var e1 = Installed("t1", V48); Ended(e1, SwapStates.Success, null, V47, V48, V48, V48);
        results.Add(("success+판같음", Launcher(e1, new FakeSchtasks()).CheckBusy()));
        var e2 = Installed("t2", V48); Ended(e2, SwapStates.Broken, SwapReasons.RevertFailed, V47, V48, V48, V48);
        results.Add(("broken+parts_after같음", Launcher(e2, new FakeSchtasks()).CheckBusy()));
        var e3 = Installed("t3", V49); Ended(e3, SwapStates.Broken, SwapReasons.RevertFailed, V47, V48, V48, V47);
        results.Add(("broken+parts_after다름·판맞음", Launcher(e3, new FakeSchtasks()).CheckBusy()));
        var e4 = Installed("t4", V47); Ended(e4, SwapStates.Broken, SwapReasons.WorkerInterrupted, V48, V47, null, null);
        results.Add(("worker_interrupted+판∈{from,to}", Launcher(e4, new FakeSchtasks()).CheckBusy()));
        var e5 = Installed("t5", V49); Ended(e5, SwapStates.Broken, SwapReasons.WorkerInterrupted, V48, V47, null, null);
        results.Add(("worker_interrupted+판이 둘 다와 다름", Launcher(e5, new FakeSchtasks()).CheckBusy()));
        var e6 = Installed("t6", V48); Ended(e6, SwapStates.Success, null, V47, V48, V48, V48);
        Directory.Delete(Path.Combine(e6.AppRoot!, "api"), recursive: true);
        results.Add(("api 없음", Launcher(e6, new FakeSchtasks()).CheckBusy()));
        var e7 = Installed("t7", V48); Ended(e7, SwapStates.Success, null, V47, V48, V48, V48); e7.WatchdogVersion = null;
        results.Add(("watchdog 판 null", Launcher(e7, new FakeSchtasks()).CheckBusy()));

        var shown = string.Join(" | ", results.Select(r => r.Row + "=" + (r.Reason ?? "null")));
        Assert.DoesNotContain(results, r => r.Reason == SwapReasons.UpdateInProgress);
        var nulls = results.Where(r => r.Reason is null).Select(r => r.Row).ToArray();
        Assert.True(nulls.SequenceEqual(new[] { "success+판같음", "broken+parts_after다름·판맞음", "worker_interrupted+판이 둘 다와 다름" }), shown);
        Assert.All(results.Where(r => r.Reason is not null), r => Assert.Equal(SwapReasons.SwapInterrupted, r.Reason));

        // swap_interrupted 면 교체를 걸지 않는다(작업 등록 0)
        var sc = new FakeSchtasks();
        var r = Launcher(e2, sc).Launch(new SwapLaunchInput(SwapModes.Rollback, V48, V47,
            new SwapMaterial { Kind = SwapMaterialKinds.Prev, Path = "x" }, "gate-user", SwapEntries.Menu, null, null));
        Assert.False(r.Started);
        Assert.Equal(SwapReasons.SwapInterrupted, r.Reason);
        Assert.Equal(0, sc.CountStartingWith("/Create"));
    }

    [Fact(DisplayName = "G-MX1 워치독 진행 표식이 .rbk 보다 먼저 — update.lock 15분 안 + M5 잔재 ⇒ update_in_progress (설계 §15-0 판정 순서)")]
    public void GMX1_watchdog_marks_come_first()
    {
        var env = Installed("mx1-lock-first", V48);
        Ended(env, SwapStates.Broken, SwapReasons.RevertFailed, V47, V48, V48, V48); // M4 모양이어도
        File.WriteAllText(Path.Combine(env.AppRoot!, "update.lock"),
            T0.AddMinutes(-3).ToString("o", CultureInfo.InvariantCulture) + "|" + V49);
        Assert.Equal(SwapReasons.UpdateInProgress, Launcher(env, new FakeSchtasks()).CheckBusy());
    }

    // ══════════════════════════════════════════════════════════════
    // G-OLD2 — 워치독 .old 런처 판정(N-2). 서비스 부분은 B2.
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-OLD2 🔴 (a) web.old 단독 ⇒ update_cleanup_pending · Launch 작업 등록 0 · 요청서 0 · .old 무접촉 (종전: null → S3 정지 뒤에야 거부)")]
    public void GOLD2_old_alone_is_cleanup_pending()
    {
        var env = FakeSwapEnvironment.Under(Path.Combine(_root, "old-a"));
        env.UtcNow = T0;
        var old = Path.Combine(env.AppRoot!, "web.old");
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, "index.html"), "old web");
        var sc = new FakeSchtasks();
        var launcher = Launcher(env, sc);

        Assert.Equal(SwapReasons.UpdateCleanupPending, launcher.CheckBusy());
        var r = launcher.Launch(new SwapLaunchInput(SwapModes.Rollback, V48, V47,
            new SwapMaterial { Kind = SwapMaterialKinds.Prev, Path = "x" }, "gate-user", SwapEntries.Menu, null, null));
        Assert.False(r.Started);
        Assert.Equal(SwapReasons.UpdateCleanupPending, r.Reason);
        Assert.Null(r.Ticket);
        Assert.Equal(0, sc.CountStartingWith("/Create"));
        Assert.False(File.Exists(Path.Combine(Work(env), LocalSwapLauncher.RequestFileName)));
        Assert.False(File.Exists(Path.Combine(Work(env), LocalSwapLauncher.LockFileName)));
        Assert.True(File.Exists(Path.Combine(old, "index.html"))); // 보기만 — 지우기·옮기기 0

        foreach (var p in new[] { "api", "watchdog" }) // 세 폴더 어느 것이든
        {
            var e = FakeSwapEnvironment.Under(Path.Combine(_root, "old-a-" + p));
            e.UtcNow = T0;
            Directory.CreateDirectory(Path.Combine(e.AppRoot!, p + ".old"));
            Assert.Equal(SwapReasons.UpdateCleanupPending, Launcher(e, new FakeSchtasks()).CheckBusy());
        }
    }

    [Fact(DisplayName = "G-OLD2 🔴 (b) web.old + update-swap.marker ⇒ 종전대로 update_in_progress (진짜 중단 모양 = 워치독 RecoverAtStartup 몫)")]
    public void GOLD2_old_with_marker_is_update_in_progress()
    {
        var env = FakeSwapEnvironment.Under(Path.Combine(_root, "old-b"));
        env.UtcNow = T0;
        Directory.CreateDirectory(Path.Combine(env.AppRoot!, "web.old"));
        File.WriteAllText(Path.Combine(env.AppRoot!, "update-swap.marker"), "x");
        Assert.Equal(SwapReasons.UpdateInProgress, Launcher(env, new FakeSchtasks()).CheckBusy());
        Assert.True(Directory.Exists(Path.Combine(env.AppRoot!, "web.old")));
    }

    // ══════════════════════════════════════════════════════════════
    // G-MX3 — 봉합3 16ⓑ: M4 는 정지 전 단계(S0·S1·S2)에서 끝난 broken 에 적용하지 않는다(설계 §16-3)
    // ══════════════════════════════════════════════════════════════

    /// <summary><see cref="Ended"/> 와 같은 요청서인데 <c>step</c> 만 <paramref name="step"/>(null 이면 JSON null)로 바꿔 쓴다.</summary>
    private static void EndedAt(FakeSwapEnvironment env, string? step, string state, string? reason, string from, string to, string? afterApi, string? afterWd)
    {
        Ended(env, state, reason, from, to, afterApi, afterWd);
        var path = Path.Combine(Work(env), LocalSwapLauncher.RequestFileName);
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        node["step"] = step;
        File.WriteAllText(path, node.ToJsonString());
    }

    [Fact(DisplayName = "봉합3 G-MX3 🔴 (a) worker_interrupted step S0 · parts_after 없음 · 판 = from ⇒ null(M5) — 멈춘 것 0 인데 잠기던 두 기능이 열린다")]
    public void GMX3_a_interrupted_at_S0_is_leftover()
    {
        var env = Installed("mx3-a", V48);
        EndedAt(env, "S0", SwapStates.Broken, SwapReasons.WorkerInterrupted, V48, V47, null, null);
        Assert.Equal("S0", Launcher(env, new FakeSchtasks()).ReadLast()!.Step); // step 을 실제로 읽는다
        Assert.Null(Launcher(env, new FakeSchtasks()).CheckBusy());
    }

    [Fact(DisplayName = "봉합3 G-MX3 🔴 (b) revert_failed step S1 · parts_after = 지금 판 ⇒ null(M5)")]
    public void GMX3_b_broken_at_S1_is_leftover()
    {
        var env = Installed("mx3-b", V48);
        EndedAt(env, "S1", SwapStates.Broken, SwapReasons.RevertFailed, V48, V47, V48, V48);
        Assert.Null(Launcher(env, new FakeSchtasks()).CheckBusy());
    }

    [Fact(DisplayName = "봉합3 G-MX3 🔴 대조 줄 (c) (a) 인데 step S6 · (d) step null ⇒ 종전대로 swap_interrupted(M4 · 보수)")]
    public void GMX3_cd_after_stop_or_unknown_is_interrupted()
    {
        var c = Installed("mx3-c", V48);
        EndedAt(c, "S6", SwapStates.Broken, SwapReasons.WorkerInterrupted, V48, V47, null, null);
        Assert.Equal(SwapReasons.SwapInterrupted, Launcher(c, new FakeSchtasks()).CheckBusy());

        var d = Installed("mx3-d", V48);
        EndedAt(d, null, SwapStates.Broken, SwapReasons.WorkerInterrupted, V48, V47, null, null);
        Assert.Null(Launcher(d, new FakeSchtasks()).ReadLast()!.Step);
        Assert.Equal(SwapReasons.SwapInterrupted, Launcher(d, new FakeSchtasks()).CheckBusy());
    }
}
