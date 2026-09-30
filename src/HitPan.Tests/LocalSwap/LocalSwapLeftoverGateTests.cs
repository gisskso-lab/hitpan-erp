using HitPan.API.Services.LocalSwap;
using Xunit;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// 🚨 20260930작1 봉합2 A1 — 교체 일꾼(<c>local-swap.ps1</c>) S0 쪽: 남은 <c>{p}.rbk</c> 를 「진행 중」이 아니라 <b>모양</b>으로 가른다(설계 §15-0)
/// · 잔재(M5)는 서비스를 멈추지 않은 채 문지기로 치운다(N-1 ⓓ) · 못 치우면 정지 전 정직한 사유로 거부 · 안전 줄(N-1 ⓔ) · 워치독 <c>.old</c> 는 정지 전 거부(N-2).
/// </summary>
/// <remarks>
/// <para>🟢 일꾼 <c>-TestRoot</c> 모드만 쓴다(<see cref="LocalSwapWorkerRig"/>) — 예약 작업·서비스·프로세스는 <c>{root}</c> 아래 대역 파일. DB 무접촉.</para>
/// <para>게이트: G-LO2 · G-LO2b · G-LO2c · G-LO3 · G-OLD1(설계 15-3). 각 게이트 옆에 봉합 뺀 사본(대조군)이 FAIL 모양을 낸다.</para>
/// <para>사유 문자열은 설계 15-2 가 원본 — 런처 쪽 <c>SwapReasons</c> 상수는 B1 갈래가 더한다(이 파일은 B1 에 기대지 않게 글자로 적는다).</para>
/// </remarks>
public sealed class LocalSwapLeftoverGateTests
{
    private const string UpdateCleanupPending = "update_cleanup_pending";
    private const string CarryPending = "carry_pending";
    private const string CleanupPending = "cleanup_pending";

    private const string ApiKeepaliveOff = "HitPan-ERP-API-keepalive-1\" /DISABLE";

    /// <summary>두 모드 — rollback(prev 재료 1.3.48 → 1.3.47) · update(수동 zip 1.3.47 → 1.3.48).</summary>
    private static LocalSwapWorkerRig Make(string mode) => mode switch
    {
        "rollback" => LocalSwapWorkerRig.Rollback(DateTime.UtcNow),
        "update" => LocalSwapWorkerRig.UpdateZip(DateTime.UtcNow, LocalSwapWorkerRig.To, LocalSwapWorkerRig.From),
        _ => throw new ArgumentException(mode),
    };

    private static string ApiRbk(LocalSwapWorkerRig rig) => Path.Combine(rig.App, "api.rbk");
    private static string Why(LocalSwapWorkerRig rig) => rig.Log() + "\n--- calls ---\n" + rig.Calls();

    /// <summary><paramref name="rig"/> 의 <c>{app}\api</c> ∪ <c>{app}\api.rbk</c> 안 옮겨 싣는 파일(같은 상대경로가 둘 다 있으면 FAIL 로 드러나게 둘 다 싣는다).</summary>
    private static Dictionary<string, string> CarryUnion(LocalSwapWorkerRig rig)
    {
        var map = LocalSwapWorkerRig.CarryFiles(Path.Combine(rig.App, "api"));
        foreach (var kv in LocalSwapWorkerRig.CarryFiles(ApiRbk(rig)))
            map[map.ContainsKey(kv.Key) ? "(api.rbk)" + kv.Key : kv.Key] = kv.Value;
        return map;
    }

    private static void AssertContainsAll(Dictionary<string, string> seed, Dictionary<string, string> now, string why)
    {
        foreach (var kv in seed)
        {
            Assert.True(now.TryGetValue(kv.Key, out var h), "시드 첨부가 사라졌다: " + kv.Key + " / " + why);
            Assert.True(h == kv.Value, "시드 첨부 내용이 바뀌었다: " + kv.Key + " / " + why);
        }
    }

    // ── 대조군 사본(봉합 뺀 사본) ──────────────────────────────────────

    private static string Sub(string original, string line, string with, string what)
    {
        Assert.True(original.Contains(line, StringComparison.Ordinal), what + " 줄을 못 찾았다 — 대조군을 같이 고쳐라");
        return original.Replace(line, with, StringComparison.Ordinal);
    }

    /// <summary>현 일꾼(봉합2 전) S0 — 모든 <c>.rbk</c> = 「진행 중」 · <c>.old</c> 안 봄.</summary>
    public static string ControlOldS0() => Sub(LocalSwapWorkerRig.OriginalScript(),
        "elseif ($null -ne ($s0Why = Get-S0Refusal)) { Complete-Swap 'refused' $s0Why 'S0' }",
        "elseif (Test-UpdateBusy) { Complete-Swap 'refused' 'update_in_progress' 'S0' }", "S0 판정");

    /// <summary>S0 정리 실패를 무시하고 진행하는 사본.</summary>
    public static string ControlIgnoreLeftoverFailure() => Sub(LocalSwapWorkerRig.OriginalScript(),
        "$why = Clear-Leftover", "[void](Clear-Leftover); $why = $null", "S0 정리");

    /// <summary>모양 판정(M2·M3 → swap_interrupted) 한 줄을 뺀 사본.</summary>
    public static string ControlNoShape() => Sub(LocalSwapWorkerRig.OriginalScript(),
        "if ($shape -eq 'M2' -or $shape -eq 'M3') { Write-Log ('S0 leftover shape ' + $shape); return 'swap_interrupted' }",
        "# (control) shape check removed", "모양 판정");

    /// <summary><c>Invoke-Swap</c> 안전 줄을 뺀 사본.</summary>
    public static string ControlNoSafetyLine() => Sub(LocalSwapWorkerRig.OriginalScript(),
        "foreach ($p in $Parts) { if (Test-Path -LiteralPath (Join-Path $script:AppRoot ($p + '.rbk'))) { throw ('S4 ' + $p + '.rbk is already there - not swapping over it') } }",
        "# (control) safety line removed", "안전 줄");

    // ══════════════════════════════════════════════════════════════
    // G-LO2 — 다음 교체 S0 가 잔재(M5)를 치우고 이어서 진행한다
    // ══════════════════════════════════════════════════════════════

    [Theory(DisplayName = "봉합2 G-LO2 🚨 지난 교체 잔재 api.rbk(M5) → S0 가 문지기로 치우고 success · api.rbk 없음 · 시드 첨부 전부 {app}\\api · 정지 1회")]
    [InlineData("rollback")]
    [InlineData("update")]
    public void Leftover_is_cleared_at_S0_and_the_swap_goes_on(string mode)
    {
        using var rig = Make(mode);
        var seed = rig.SeedLeftoverRbk();
        Assert.Equal(2, seed.Count);
        Assert.Equal(0, rig.Run());
        var final = rig.Final();
        var why = Why(rig);
        Assert.True(final.State == SwapStates.Success, mode + ": 끝 " + final.State + "/" + final.Reason + " / " + why);
        Assert.False(Directory.Exists(ApiRbk(rig)), "api.rbk 가 남았다 / " + why);
        AssertContainsAll(seed, LocalSwapWorkerRig.CarryFiles(Path.Combine(rig.App, "api")), why);
        Assert.Equal(1, rig.CountCalls(ApiKeepaliveOff));
        Assert.Contains("S0 leftover", rig.Log());
    }

    [Theory(DisplayName = "봉합2 G-LO2 대조군 🔴 현 일꾼(모든 .rbk = 진행 중) → S0 refused/update_in_progress(게이트가 FAIL 을 낸다)")]
    [InlineData("rollback")]
    [InlineData("update")]
    public void Control_old_S0_refuses_leftover_forever(string mode)
    {
        using var rig = Make(mode);
        rig.SeedLeftoverRbk();
        Assert.Equal(0, rig.Run(ControlOldS0()));
        var final = rig.Final();
        Assert.Equal(SwapStates.Refused, final.State);
        Assert.Equal(SwapReasons.UpdateInProgress, final.Reason);
        Assert.True(Directory.Exists(ApiRbk(rig)));
    }

    // ══════════════════════════════════════════════════════════════
    // G-LO2b — S0 에서 못 치우면(첨부 파일이 쥐어져 있음) 지우지 않고 정지 전 정직한 사유로 거부
    // ══════════════════════════════════════════════════════════════

    [Theory(DisplayName = "봉합2 G-LO2b 🚨 S0 정리 중 api.rbk\\chat-files 파일 잠김(RBKHC) → refused/carry_pending · step S0 · /DISABLE 0 · 서비스 무접촉 · api.rbk 파일 수·해시 그대로")]
    [InlineData("rollback")]
    [InlineData("update")]
    public void Held_leftover_is_kept_and_refused_before_stop(string mode)
    {
        using var rig = Make(mode);
        rig.SeedLeftoverRbk();
        var before = LocalSwapWorkerRig.AllFiles(ApiRbk(rig));
        Assert.Equal(0, rig.Run(null, "RBKHC"));
        var final = rig.Final();
        var why = Why(rig);
        Assert.True(final.State == SwapStates.Refused && final.Reason == CarryPending, mode + ": 끝 " + final.State + "/" + final.Reason + " / " + why);
        Assert.Equal("S0", final.Step);
        Assert.Equal(0, rig.CountCalls("/DISABLE"));
        Assert.Equal(0, rig.CountCalls("/Change"));
        Assert.Equal(0, rig.CountCalls("taskkill"));
        Assert.Equal("Running", rig.ServiceState());
        var after = LocalSwapWorkerRig.AllFiles(ApiRbk(rig));
        Assert.Equal(before.Count, after.Count);
        AssertContainsAll(before, after, why);
        Assert.Contains("test hold", rig.Log()); // 주입이 실제로 걸렸다
        Assert.Empty(rig.LeftoverTasks());
    }

    [Theory(DisplayName = "봉합2 G-LO2b 대조군 🔴 정리 실패를 무시하고 진행하는 사본 → 정지 발생(/DISABLE ≥1 · 게이트가 FAIL 을 낸다)")]
    [InlineData("rollback")]
    [InlineData("update")]
    public void Control_ignoring_cleanup_failure_stops_the_erp(string mode)
    {
        using var rig = Make(mode);
        rig.SeedLeftoverRbk();
        Assert.Equal(0, rig.Run(ControlIgnoreLeftoverFailure(), "RBKHC"));
        Assert.True(rig.CountCalls(ApiKeepaliveOff) >= 1, "대조군이 멈추지 않았다 / " + Why(rig));
        Assert.NotEqual(SwapStates.Refused, rig.Final().State);
    }

    [Fact(DisplayName = "봉합2 G-LO2b 🟢(보탬) 첨부 없는 잔재의 프로그램 파일이 잠김 → refused/cleanup_pending · S0 · /DISABLE 0 · 파일 그대로")]
    public void Held_program_file_only_is_cleanup_pending()
    {
        using var rig = Make("rollback");
        rig.SeedLeftoverRbk(withCarry: false);
        var before = LocalSwapWorkerRig.AllFiles(ApiRbk(rig));
        int exit;
        using (new FileStream(Path.Combine(ApiRbk(rig), "HitPan.Old.dll"), FileMode.Open, FileAccess.Read, FileShare.None))
            exit = rig.Run();
        Assert.Equal(0, exit);
        var final = rig.Final();
        Assert.True(final.State == SwapStates.Refused && final.Reason == CleanupPending, "끝 " + final.State + "/" + final.Reason + " / " + Why(rig));
        Assert.Equal("S0", final.Step);
        Assert.Equal(0, rig.CountCalls("/DISABLE"));
        Assert.Equal(before.Count, LocalSwapWorkerRig.AllFiles(ApiRbk(rig)).Count);
    }

    // ══════════════════════════════════════════════════════════════
    // G-LO2c — 모양 M2(api.rbk 있고 api 없음) · M3(api·watchdog 판 다름) → swap_interrupted · 폴더 무접촉
    // ══════════════════════════════════════════════════════════════
    // failAt S1 = 봉합 일꾼은 S0 에서 끝나 영향 없음 · 대조군은 S0 정리를 한 뒤 S1 에서 멈춘다(정지 0 · 확인 대기 180초 없음)

    private static LocalSwapWorkerRig MakeShape(string shape)
    {
        var rig = Make("rollback");
        switch (shape)
        {
            case "M2":
                rig.SeedLeftoverRbk(withCarry: false);
                Directory.Delete(Path.Combine(rig.App, "api"), recursive: true);
                break;
            case "M3":
                rig.SeedLeftoverRbk();
                File.WriteAllText(Path.Combine(rig.App, "watchdog", ".testversion"), "1.3.46");
                break;
            default: throw new ArgumentException(shape);
        }
        return rig;
    }

    private sealed record Snap(Dictionary<string, string> Rbk, Dictionary<string, string> Api, bool ApiExists, Dictionary<string, string> Web, Dictionary<string, string> Watchdog);

    private static Snap TakeSnap(LocalSwapWorkerRig rig) => new(
        LocalSwapWorkerRig.AllFiles(ApiRbk(rig)),
        LocalSwapWorkerRig.AllFiles(Path.Combine(rig.App, "api")),
        Directory.Exists(Path.Combine(rig.App, "api")),
        LocalSwapWorkerRig.AllFiles(Path.Combine(rig.App, "web")),
        LocalSwapWorkerRig.AllFiles(Path.Combine(rig.App, "watchdog")));

    private static void AssertSame(Dictionary<string, string> a, Dictionary<string, string> b, string what)
    {
        Assert.True(a.Count == b.Count, what + " 파일 수가 바뀌었다 " + a.Count + " → " + b.Count);
        AssertContainsAll(a, b, what);
    }

    [Theory(DisplayName = "봉합2 G-LO2c 🚨 모양 M2(api.rbk · api 없음) · M3(판 다름) → refused/swap_interrupted · S0 · 폴더 무접촉")]
    [InlineData("M2")]
    [InlineData("M3")]
    public void Interrupted_shapes_are_refused_and_untouched(string shape)
    {
        using var rig = MakeShape(shape);
        var before = TakeSnap(rig);
        Assert.Equal(0, rig.Run(null, "S1"));
        var final = rig.Final();
        var why = Why(rig);
        Assert.True(final.State == SwapStates.Refused && final.Reason == SwapReasons.SwapInterrupted, shape + ": 끝 " + final.State + "/" + final.Reason + " / " + why);
        Assert.Equal("S0", final.Step);
        Assert.Equal(0, rig.CountCalls("/DISABLE"));
        var after = TakeSnap(rig);
        AssertSame(before.Rbk, after.Rbk, "api.rbk");
        Assert.Equal(before.ApiExists, after.ApiExists);
        AssertSame(before.Api, after.Api, "api");
        AssertSame(before.Web, after.Web, "web");
        AssertSame(before.Watchdog, after.Watchdog, "watchdog");
    }

    [Theory(DisplayName = "봉합2 G-LO2c 대조군 🔴 모양 판정 뺀 사본 → api.rbk 를 치운다(게이트가 FAIL 을 낸다)")]
    [InlineData("M2")]
    [InlineData("M3")]
    public void Control_without_shape_check_clears_the_rbk(string shape)
    {
        using var rig = MakeShape(shape);
        Assert.Equal(0, rig.Run(ControlNoShape(), "S1"));
        Assert.False(Directory.Exists(ApiRbk(rig)), shape + ": 대조군이 api.rbk 를 안 치웠다 / " + Why(rig));
        Assert.NotEqual(SwapReasons.SwapInterrupted, rig.Final().Reason);
    }

    // ══════════════════════════════════════════════════════════════
    // G-LO3 — S0 정리를 건너뛰어도(LOSKIP) Move-Part 의 날것 지우기가 고객 자료를 못 지운다(안전 줄)
    // ══════════════════════════════════════════════════════════════

    [Theory(DisplayName = "봉합2 G-LO3 🚨 S0 정리 건너뜀(LOSKIP) + api.rbk\\chat-files → S4 throw → reverted · 첨부 합 = 시드")]
    [InlineData("rollback")]
    [InlineData("update")]
    public void Safety_line_stops_the_raw_delete(string mode)
    {
        using var rig = Make(mode);
        var seed = rig.SeedLeftoverRbk();
        Assert.Equal(0, rig.Run(null, "LOSKIP"));
        var final = rig.Final();
        var why = Why(rig);
        Assert.True(final.State == SwapStates.Reverted, mode + ": 끝 " + final.State + "/" + final.Reason + " / " + why);
        Assert.Equal(SwapReasons.SwapFailed, final.Reason);
        Assert.Contains("injected LOSKIP", rig.Log());
        var union = CarryUnion(rig);
        Assert.Equal(seed.Count, union.Count);
        AssertContainsAll(seed, union, why);
    }

    [Theory(DisplayName = "봉합2 G-LO3 대조군 🔴 안전 줄 뺀 사본 → 날것 지우기로 첨부 소실(게이트가 FAIL 을 낸다)")]
    [InlineData("rollback")]
    [InlineData("update")]
    public void Control_without_safety_line_loses_attachments(string mode)
    {
        using var rig = Make(mode);
        var seed = rig.SeedLeftoverRbk();
        Assert.Equal(0, rig.Run(ControlNoSafetyLine(), "LOSKIP"));
        var union = CarryUnion(rig);
        Assert.True(seed.Keys.Any(k => !union.ContainsKey(k)), "대조군에서 첨부가 안 사라졌다 / " + Why(rig));
    }

    // ══════════════════════════════════════════════════════════════
    // G-OLD1 — 워치독 .old 가 있으면 정지 전(S0) update_cleanup_pending · .old 는 보기만
    // ══════════════════════════════════════════════════════════════

    private static LocalSwapWorkerRig MakeOld(bool withMarker = false)
    {
        var rig = Make("rollback");
        var old = Path.Combine(rig.App, "api.old");
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, "HitPan.API.dll"), "watchdog-old");
        if (withMarker) File.WriteAllText(Path.Combine(rig.App, "update-swap.marker"), "x");
        return rig;
    }

    [Fact(DisplayName = "봉합2 G-OLD1 🚨 api.old 가 있으면(기존 pre-old 장면) → refused/update_cleanup_pending · step S0 · /DISABLE 0 · api.old 그대로")]
    public void Old_folder_is_refused_before_stop()
    {
        using var rig = MakeOld();
        Assert.Equal(0, rig.Run());
        var final = rig.Final();
        var why = Why(rig);
        Assert.True(final.State == SwapStates.Refused && final.Reason == UpdateCleanupPending, "끝 " + final.State + "/" + final.Reason + " / " + why);
        Assert.Equal("S0", final.Step);
        Assert.Equal(0, rig.CountCalls("/DISABLE"));
        Assert.Equal("Running", rig.ServiceState());
        Assert.Equal("watchdog-old", File.ReadAllText(Path.Combine(rig.App, "api.old", "HitPan.API.dll")));
        Assert.Equal(LocalSwapWorkerRig.From, LocalSwapWorkerRig.PartMark(rig.App, "api"));
        Assert.Empty(rig.LeftoverTasks());
    }

    [Fact(DisplayName = "봉합2 G-OLD1 🟢 .old + 교체 표식(update-swap.marker) → 종전대로 update_in_progress · S0(판정 순서 15-0)")]
    public void Old_folder_with_marker_is_update_in_progress()
    {
        using var rig = MakeOld(withMarker: true);
        Assert.Equal(0, rig.Run());
        var final = rig.Final();
        Assert.True(final.State == SwapStates.Refused && final.Reason == SwapReasons.UpdateInProgress, "끝 " + final.State + "/" + final.Reason + " / " + Why(rig));
        Assert.Equal("S0", final.Step);
        Assert.Equal(0, rig.CountCalls("/DISABLE"));
    }

    [Fact(DisplayName = "봉합2 G-OLD1 대조군 🔴 현 일꾼 → S3 뒤 update_in_progress · /DISABLE ≥1(전 직원 ERP 끊김 · 게이트가 FAIL 을 낸다)")]
    public void Control_old_S0_stops_before_seeing_old()
    {
        using var rig = MakeOld();
        Assert.Equal(0, rig.Run(ControlOldS0()));
        var final = rig.Final();
        Assert.Equal(SwapReasons.UpdateInProgress, final.Reason);
        Assert.Equal("S3", final.Step);
        Assert.True(rig.CountCalls(ApiKeepaliveOff) >= 1);
    }

    // ══════════════════════════════════════════════════════════════
    // 봉합2 A2 — 일꾼 끝 쪽: N-1 ⓐⓑ(성공 정리 재시도 · 정직한 끝 사유) · N-3 ⓐ(parts_after)
    // ══════════════════════════════════════════════════════════════

    /// <summary>A2 대조군 — 성공 정리는 그대로(잠김 주입 포함) 하되 끝 사유를 버리는 사본(= 봉합2 전 「사유 없는 success」 모양).</summary>
    public static string ControlNoSuccessReason() => Sub(LocalSwapWorkerRig.OriginalScript(),
        "return (Get-SuccessLeftover)", "[void](Get-SuccessLeftover); return $null", "성공 정리 사유");

    /// <summary>A2 대조군 — <c>Complete-Swap</c> 의 <c>parts_after</c> 한 줄을 뺀 사본(= 봉합2 전 요청서 · 칸 없음).</summary>
    public static string ControlNoPartsAfter() => Sub(LocalSwapWorkerRig.OriginalScript(),
        "Set-ReqField 'parts_after' (Get-PartsAfter)", "# (control) parts_after removed", "parts_after");

    /// <summary>되돌리기는 1.3.48 → 1.3.47 · 업데이트는 1.3.47 → 1.3.48(<see cref="Make"/>).</summary>
    private static string Target(string mode) => mode == "rollback" ? LocalSwapWorkerRig.To : LocalSwapWorkerRig.From;

    // ══════════════════════════════════════════════════════════════
    // G-LO1 — 성공인데 .rbk 가 남는 끝: 같은 실행 안 재시도 3회 → 그래도 남으면 success + 정직한 사유 · 첨부 0 손실
    // ══════════════════════════════════════════════════════════════
    //   RBKH      = 성공 정리 동안 api.rbk 맨 위 프로그램 파일 하나를 FileShare.None 으로 쥔다 → cleanup_pending
    //   S4C,RBKHC = S4 싣기 건너뜀(시드가 api.rbk 에 남음) + api.rbk\chat-files 파일 하나를 쥔다 → carry_pending

    public static IEnumerable<object[]> LeftoverAfterSuccessCases() => new[]
    {
        new object[] { "rollback", "RBKH", CleanupPending },
        new object[] { "update", "RBKH", CleanupPending },
        new object[] { "rollback", "S4C,RBKHC", CarryPending },
        new object[] { "update", "S4C,RBKHC", CarryPending },
    };

    [Theory(DisplayName = "봉합2 G-LO1 🚨 성공 정리 중 api.rbk 파일 잠김 → success + cleanup_pending(프로그램 파일) / carry_pending(첨부) · 재시도 3회 · 첨부 합(api ∪ api.rbk) = 시드")]
    [MemberData(nameof(LeftoverAfterSuccessCases))]
    public void Leftover_after_success_is_told_honestly(string mode, string failAt, string reason)
    {
        using var rig = Make(mode);
        var seed = rig.SeedCarry();
        Assert.Equal(5, seed.Count);
        Assert.Equal(0, rig.Run(null, failAt));
        var final = rig.Final();
        var why = Why(rig);
        Assert.True(final.State == SwapStates.Success && final.Reason == reason,
            mode + "/" + failAt + ": 끝 " + final.State + "/" + final.Reason + " (기대 success/" + reason + ") / " + why);
        Assert.Equal("S6", final.Step);
        Assert.Equal(Target(mode), rig.LiveVersion("api"));          // 교체 자체는 끝났다 — 성공은 성공
        Assert.Contains("test hold", rig.Log());                       // 주입이 실제로 걸렸다
        Assert.Equal(3, rig.CountLog("success cleanup retry "));       // ⓐ 같은 실행 안 재시도 3회
        Assert.True(Directory.Exists(ApiRbk(rig)), "잠긴 api.rbk 가 없다 — 주입이 안 걸렸다 / " + why);
        var union = CarryUnion(rig);
        Assert.Equal(seed.Count, union.Count);
        AssertContainsAll(seed, union, why);
        Assert.Empty(rig.LeftoverTasks());
    }

    [Theory(DisplayName = "봉합2 G-LO1 🟢(보탬) 잠김 없음 → success · 사유 없음 · 재시도 0 · api.rbk 없음")]
    [InlineData("rollback")]
    [InlineData("update")]
    public void Clean_success_has_no_reason_and_no_retry(string mode)
    {
        using var rig = Make(mode);
        rig.SeedCarry();
        Assert.Equal(0, rig.Run());
        var final = rig.Final();
        Assert.True(final.State == SwapStates.Success && string.IsNullOrEmpty(final.Reason), mode + ": 끝 " + final.State + "/" + final.Reason + " / " + Why(rig));
        Assert.Equal(0, rig.CountLog("success cleanup retry "));
        Assert.False(Directory.Exists(ApiRbk(rig)));
    }

    [Theory(DisplayName = "봉합2 G-LO1 대조군 🔴 끝 사유를 버리는 사본 → api.rbk 가 남았는데 사유 없는 success(N-1 거짓 끝 · 게이트가 FAIL 을 낸다)")]
    [InlineData("rollback", "RBKH")]
    [InlineData("update", "S4C,RBKHC")]
    public void Control_without_reason_says_plain_success(string mode, string failAt)
    {
        using var rig = Make(mode);
        rig.SeedCarry();
        Assert.Equal(0, rig.Run(ControlNoSuccessReason(), failAt));
        var final = rig.Final();
        Assert.Equal(SwapStates.Success, final.State);
        Assert.True(string.IsNullOrEmpty(final.Reason), "대조군인데 사유가 있다: " + final.Reason);
        Assert.True(Directory.Exists(ApiRbk(rig)), "대조군에서 api.rbk 가 안 남았다 — 대조가 성립 안 한다 / " + Why(rig));
    }

    // ══════════════════════════════════════════════════════════════
    // G-MX2 — 끝 상태마다 요청서 parts_after = 끝난 뒤 살아 있는 api·watchdog 판(.testversion)
    // ══════════════════════════════════════════════════════════════
    //   S6,S7R2 = 원위치가 첫 폴더(watchdog)만 돌려놓고 던짐 → broken · 섞인 판(api 1.3.47 · watchdog 1.3.48)

    public static IEnumerable<object?[]> PartsAfterCases() => new[]
    {
        new object?[] { "S6,S7R2", SwapStates.Broken, LocalSwapWorkerRig.To, LocalSwapWorkerRig.From },
        new object?[] { null, SwapStates.Success, LocalSwapWorkerRig.To, LocalSwapWorkerRig.To },
        new object?[] { "S6", SwapStates.Reverted, LocalSwapWorkerRig.From, LocalSwapWorkerRig.From },
        new object?[] { "S1", SwapStates.Refused, LocalSwapWorkerRig.From, LocalSwapWorkerRig.From }, // 🟢(보탬) 네 번째 끝
    };

    [Theory(DisplayName = "봉합2 G-MX2 🚨 끝 상태(broken 섞인 판 · success · reverted · refused) → 요청서 parts_after = 끝난 뒤 폴더 판")]
    [MemberData(nameof(PartsAfterCases))]
    public void Parts_after_is_the_live_versions_at_the_end(string? failAt, string state, string api, string watchdog)
    {
        using var rig = Make("rollback");
        Assert.Equal(0, rig.Run(null, failAt));
        var final = rig.Final();
        var why = Why(rig);
        Assert.True(final.State == state, failAt + ": 끝 " + final.State + "/" + final.Reason + " (기대 " + state + ") / " + why);
        // 기대값은 표로 고정하고, 폴더에서 다시 읽은 값과도 맞춘다(끝난 뒤에 읽었다는 증거)
        Assert.Equal(api, rig.LiveVersion("api"));
        Assert.Equal(watchdog, rig.LiveVersion("watchdog"));
        var pa = rig.PartsAfter();
        Assert.True(pa.Present, failAt + ": 요청서에 parts_after 칸이 없다 / " + why);
        Assert.Equal(api, pa.Api);
        Assert.Equal(watchdog, pa.Watchdog);
    }

    [Fact(DisplayName = "봉합2 G-MX2 🟢(보탬) 판을 못 읽으면(api 폴더 없음 · M2 거부) → parts_after 칸은 있고 api = null")]
    public void Parts_after_is_null_when_a_version_cannot_be_read()
    {
        using var rig = MakeShape("M2");
        Assert.Equal(0, rig.Run());
        Assert.Equal(SwapReasons.SwapInterrupted, rig.Final().Reason);
        var pa = rig.PartsAfter();
        Assert.True(pa.Present, "parts_after 칸이 없다 / " + Why(rig));
        Assert.Null(pa.Api);
        Assert.Equal(LocalSwapWorkerRig.From, pa.Watchdog);
    }

    [Theory(DisplayName = "봉합2 G-MX2 대조군 🔴 parts_after 줄 뺀 사본 → 요청서에 칸 없음(게이트가 FAIL 을 낸다)")]
    [InlineData("S6,S7R2")]
    [InlineData(null)]
    public void Control_without_parts_after_has_no_field(string? failAt)
    {
        using var rig = Make("rollback");
        Assert.Equal(0, rig.Run(ControlNoPartsAfter(), failAt));
        Assert.False(rig.PartsAfter().Present, "대조군인데 parts_after 칸이 있다 / " + Why(rig));
    }

    // ══════════════════════════════════════════════════════════════
    // 봉합3 C — 병렬이슈 15(M6 「보관 미완」 = 옛 판의 유일한 사본은 잔재가 아니다) · 16ⓐ(S0 정리 던짐 → broken 아님) · 설계 §16
    // ══════════════════════════════════════════════════════════════
    // 🔴 C-0 실측(10/1): PS5.1 Move-Item 은 폴더 안 파일 하나가 FileShare.None 으로 쥐어져 있으면 던진다 — 단 다른 파일을 먼저 옮긴 뒤에
    //    던진다(부분 이동). 그래서 일꾼 Complete-PrevGeneration 은 옮기기 전에 폴더가 비어 있는지(Test-DirFree) 보고, 반쯤 옮겨진 부분은 파일 단위로 합친다.

    private const string OldGen = "1.3.45";

    private static string Rbk(LocalSwapWorkerRig rig, string part) => Path.Combine(rig.App, part + ".rbk");

    /// <summary><paramref name="gen"/>(<c>{part}\상대경로</c>)에서 한 부분만 떼어 <c>{part}\</c> 를 벗긴다.</summary>
    private static Dictionary<string, string> PartOf(Dictionary<string, string> gen, string part) =>
        gen.Where(kv => kv.Key.StartsWith(part + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
           .ToDictionary(kv => kv.Key.Substring(part.Length + 1), kv => kv.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary><c>prev</c> 완성본: 세 부분 · <c>version.txt</c>=<paramref name="from"/> · <c>replaced-by.txt</c>=<paramref name="to"/> · <c>sha256.txt</c> 줄 수 = 시드 · 파일 해시 = 시드 · 표식 없음 · <c>.rbk</c> 없음.</summary>
    private static void AssertPrevComplete(LocalSwapWorkerRig rig, Dictionary<string, string> gen, string from, string to, string why)
    {
        foreach (var p in new[] { "api", "web", "watchdog" })
        {
            Assert.True(Directory.Exists(Path.Combine(rig.PrevDir, p)), "prev\\" + p + " 가 없다 / " + why);
            Assert.False(Directory.Exists(Rbk(rig, p)), p + ".rbk 가 남았다 / " + why);
        }
        Assert.Equal(from, File.ReadAllText(Path.Combine(rig.PrevDir, "version.txt")).Trim());
        Assert.Equal(to, File.ReadAllText(Path.Combine(rig.PrevDir, "replaced-by.txt")).Trim());
        var list = Path.Combine(rig.PrevDir, "sha256.txt");
        Assert.True(File.Exists(list), "sha256.txt 가 없다 / " + why);
        Assert.Equal(gen.Count, File.ReadAllLines(list).Count(l => !string.IsNullOrWhiteSpace(l)));
        AssertSame(gen, rig.PrevParts(), "prev");
        Assert.False(File.Exists(rig.PrevSavingPath), "보관 표식이 남았다 / " + why);
    }

    /// <summary>봉합3 대조군 — 성공 정리 재시도의 M6 보관 마침(15ⓒ)을 뺀 사본(재시도가 지우기로 돌아감 = 봉합3 전).</summary>
    public static string ControlNoPrevRetry() => Sub(LocalSwapWorkerRig.OriginalScript(),
        "if ($null -ne $pm -and -not $pm.stale) { [void](Complete-PrevGeneration $pm.from $pm.to); continue }",
        "# (control) seal3 15 (c) removed", "성공 재시도 보관 마침");

    /// <summary>봉합3 대조군 — S0 의 M6 보관 마침(15ⓓ)을 뺀 사본(S0 가 M5 로 보고 지움 = 봉합3 전).</summary>
    public static string ControlNoS0Finish() => Sub(LocalSwapWorkerRig.OriginalScript(),
        "if ($null -ne $mark -and -not $mark.stale) { if (-not (Complete-PrevGeneration $mark.from $mark.to)) { Write-Log 'S0 previous generation saving not finished - old folders kept'; return (Get-RbkLeftReason) } }",
        "# (control) seal3 15 (d) removed", "S0 보관 마침");

    /// <summary>봉합3 대조군 — 낡은 표식 판정에서 <c>to</c> 조건을 뺀 사본.</summary>
    public static string ControlNoMarkToCheck() => Sub(LocalSwapWorkerRig.OriginalScript(),
        "if ($o.to -ne $live) { $stale = $true }", "# (control) mark to check removed", "표식 to 조건");

    /// <summary>봉합3 대조군 — <c>Clear-Leftover</c> 자체 catch 가 다시 던지는 사본(= 봉합3 전: 본문 catch 가 broken).</summary>
    public static string ControlNoOwnCatch() => Sub(LocalSwapWorkerRig.OriginalScript(),
        "Write-Log ('S0 leftover cleanup threw: ' + $_.Exception.Message + ' - refused before anything is stopped')",
        "throw", "S0 자체 catch");

    // ── G-PV1 · G-PV2 — 같은 실행 안 보관 마침 / 끝내 못 마침(업데이트 모드) ──

    [Fact(DisplayName = "봉합3 G-PV1 🚨 업데이트 첫 보관 중 web.rbk 파일 잠김(PVH) → 재시도가 보관을 마침 · success 사유 없음 · prev 세 부분+세 파일 · 표식·.rbk 없음 · prev 해시 = 옛 판")]
    public void Prev_saving_is_finished_by_the_success_retry()
    {
        using var rig = Make("update");
        var gen = rig.LiveGeneration();
        Assert.Equal(0, rig.Run(null, "PVH"));
        var final = rig.Final();
        var why = Why(rig);
        Assert.True(final.State == SwapStates.Success && string.IsNullOrEmpty(final.Reason), "끝 " + final.State + "/" + final.Reason + " / " + why);
        Assert.Contains("test hold", rig.Log());                                  // 주입이 실제로 걸렸다
        Assert.Contains("previous generation not finished", rig.Log());          // 첫 보관은 실제로 못 마쳤다
        AssertPrevComplete(rig, gen, LocalSwapWorkerRig.To, LocalSwapWorkerRig.From, why);
    }

    [Fact(DisplayName = "봉합3 G-PV1 대조군 🔴 재시도 보관 마침(15ⓒ) 뺀 사본 → web.rbk·watchdog.rbk 지워짐 · prev 반쪽(게이트가 FAIL 을 낸다)")]
    public void Control_success_retry_deletes_the_old_generation()
    {
        using var rig = Make("update");
        Assert.Equal(0, rig.Run(ControlNoPrevRetry(), "PVH"));
        Assert.False(Directory.Exists(Rbk(rig, "watchdog")), "대조군에서 watchdog.rbk 가 안 지워졌다 / " + Why(rig));
        Assert.False(Directory.Exists(Path.Combine(rig.PrevDir, "watchdog")), "대조군인데 prev\\watchdog 가 있다 / " + Why(rig));
    }

    [Fact(DisplayName = "봉합3 G-PV2 🚨 web.rbk 파일을 끝까지 쥠(PVHH) → success+cleanup_pending · web.rbk·watchdog.rbk 파일 수·해시 = 옛 판 · 표식 남음 · prev\\api 있음")]
    public void Prev_saving_not_finished_keeps_the_old_generation()
    {
        using var rig = Make("update");
        var gen = rig.LiveGeneration();
        Assert.Equal(0, rig.Run(null, "PVHH"));
        var final = rig.Final();
        var why = Why(rig);
        Assert.True(final.State == SwapStates.Success && final.Reason == CleanupPending, "끝 " + final.State + "/" + final.Reason + " / " + why);
        Assert.Contains("test hold", rig.Log());
        Assert.Equal(3, rig.CountLog("success cleanup retry "));
        AssertSame(PartOf(gen, "web"), LocalSwapWorkerRig.AllFiles(Rbk(rig, "web")), "web.rbk");
        AssertSame(PartOf(gen, "watchdog"), LocalSwapWorkerRig.AllFiles(Rbk(rig, "watchdog")), "watchdog.rbk");
        Assert.True(File.Exists(rig.PrevSavingPath), "보관 표식이 없다 / " + why);
        Assert.True(Directory.Exists(Path.Combine(rig.PrevDir, "api")), "prev\\api 가 없다 / " + why);
        AssertSame(PartOf(gen, "api"), LocalSwapWorkerRig.AllFiles(Path.Combine(rig.PrevDir, "api")), "prev\\api");
    }

    [Fact(DisplayName = "봉합3 G-PV2 대조군 🔴 재시도 보관 마침(15ⓒ) 뺀 사본 → 문지기가 watchdog.rbk 를 지움(게이트가 FAIL 을 낸다)")]
    public void Control_success_retry_deletes_the_free_part()
    {
        using var rig = Make("update");
        var gen = rig.LiveGeneration();
        Assert.Equal(0, rig.Run(ControlNoPrevRetry(), "PVHH"));
        Assert.NotEqual(PartOf(gen, "watchdog").Count, LocalSwapWorkerRig.AllFiles(Rbk(rig, "watchdog")).Count);
    }

    // ── G-PV3 · G-PV3b — 다음 교체 S0 가 보관을 마친다 / 못 마치면 정지 전 거부 ──

    [Theory(DisplayName = "봉합3 G-PV3 🚨 보관 반쪽(prev\\api 만 · web.rbk·watchdog.rbk · 표식) → S0 가 보관 마침 · S1 까지 감(material_invalid) · prev 완성 · 표식·.rbk 없음 · /DISABLE 0")]
    [InlineData("rollback")]
    [InlineData("update")]
    public void Next_S0_finishes_the_prev_saving(string mode)
    {
        using var rig = Make(mode);
        var live = rig.LiveVersion("api")!;
        var gen = rig.SeedHalfPrev(OldGen);
        Assert.Equal(0, rig.Run(null, "S1"));
        var final = rig.Final();
        var why = Why(rig);
        Assert.True(final.State == SwapStates.Refused && final.Reason == SwapReasons.MaterialInvalid, mode + ": 끝 " + final.State + "/" + final.Reason + " / " + why);
        Assert.Equal("S1", final.Step);
        Assert.Equal(0, rig.CountCalls("/DISABLE"));
        AssertPrevComplete(rig, gen, OldGen, live, why);
    }

    [Theory(DisplayName = "봉합3 G-PV3 대조군 🔴 S0 보관 마침(15ⓓ) 뺀 사본 → S0 가 M5 로 보고 .rbk 를 지움 · prev 반쪽(게이트가 FAIL 을 낸다)")]
    [InlineData("rollback")]
    [InlineData("update")]
    public void Control_S0_deletes_the_half_saved_generation(string mode)
    {
        using var rig = Make(mode);
        rig.SeedHalfPrev(OldGen);
        Assert.Equal(0, rig.Run(ControlNoS0Finish(), "S1"));
        Assert.False(Directory.Exists(Rbk(rig, "web")), mode + ": 대조군에서 web.rbk 가 안 지워졌다 / " + Why(rig));
        Assert.False(Directory.Exists(Path.Combine(rig.PrevDir, "web")));
    }

    [Theory(DisplayName = "봉합3 G-PV3b 🚨 보관 반쪽 + web.rbk 파일을 실행 내내 쥠 → refused/cleanup_pending · S0 · /DISABLE 0 · web.rbk·watchdog.rbk 그대로 · 표식 남음")]
    [InlineData("rollback")]
    [InlineData("update")]
    public void Next_S0_cannot_finish_and_refuses_before_stop(string mode)
    {
        using var rig = Make(mode);
        var gen = rig.SeedHalfPrev(OldGen);
        int exit;
        using (new FileStream(Path.Combine(Rbk(rig, "web"), "web.bin"), FileMode.Open, FileAccess.Read, FileShare.None))
            exit = rig.Run();
        Assert.Equal(0, exit);
        var final = rig.Final();
        var why = Why(rig);
        Assert.True(final.State == SwapStates.Refused && final.Reason == CleanupPending, mode + ": 끝 " + final.State + "/" + final.Reason + " / " + why);
        Assert.Equal("S0", final.Step);
        Assert.Equal(0, rig.CountCalls("/DISABLE"));
        Assert.Equal("Running", rig.ServiceState());
        AssertSame(PartOf(gen, "web"), LocalSwapWorkerRig.AllFiles(Rbk(rig, "web")), "web.rbk");
        AssertSame(PartOf(gen, "watchdog"), LocalSwapWorkerRig.AllFiles(Rbk(rig, "watchdog")), "watchdog.rbk");
        Assert.True(File.Exists(rig.PrevSavingPath), "보관 표식이 없다 / " + why);
        Assert.Empty(rig.LeftoverTasks());
    }

    [Theory(DisplayName = "봉합3 G-PV3b 대조군 🔴 S0 보관 마침(15ⓓ) 뺀 사본 → 쥔 web.rbk 만 남고 watchdog.rbk 지워짐(게이트가 FAIL 을 낸다)")]
    [InlineData("rollback")]
    [InlineData("update")]
    public void Control_S0_deletes_the_free_part(string mode)
    {
        using var rig = Make(mode);
        rig.SeedHalfPrev(OldGen);
        using (new FileStream(Path.Combine(Rbk(rig, "web"), "web.bin"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal(0, rig.Run(ControlNoS0Finish()));
        Assert.False(Directory.Exists(Rbk(rig, "watchdog")), mode + ": 대조군에서 watchdog.rbk 가 안 지워졌다 / " + Why(rig));
    }

    // ── G-PV4 — 낡은 표식: 표식만 지우고 .rbk 는 M5 대로 · prev 무접촉 ──

    private static LocalSwapWorkerRig MakeStaleMark(string kind, out Dictionary<string, string> prevLists)
    {
        var rig = Make("rollback");
        switch (kind)
        {
            case "to-moved": // (i) 표식 to ≠ 지금 api 판(그 사이 워치독 업데이트가 판을 바꿈)
                rig.SeedHalfPrev(OldGen, markTo: "1.3.46");
                break;
            case "prev-done": // (ii) prev 가 이미 완성본(sha256.txt 있음 · 다른 판)
                rig.SeedHalfPrev(OldGen);
                rig.MakePrev("1.3.44", "1.3.45");
                File.WriteAllText(Path.Combine(rig.PrevDir, "sha256.txt"), "someone else's list\n");
                break;
            default: throw new ArgumentException(kind);
        }
        prevLists = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in LocalSwapWorkerRig.AllFiles(rig.PrevDir))
            if (!kv.Key.Contains(Path.DirectorySeparatorChar)) prevLists[kv.Key] = kv.Value;
        return rig;
    }

    [Theory(DisplayName = "봉합3 G-PV4 🚨 낡은 표식((i) to ≠ 지금 판 · (ii) prev 완성본) → 표식만 지움 · .rbk 는 M5 대로 치움 · prev 목록 파일 해시 그대로")]
    [InlineData("to-moved")]
    [InlineData("prev-done")]
    public void Stale_mark_is_removed_alone(string kind)
    {
        using var rig = MakeStaleMark(kind, out var lists);
        var prevApi = LocalSwapWorkerRig.AllFiles(Path.Combine(rig.PrevDir, "api"));
        Assert.Equal(0, rig.Run(null, "S1"));
        var why = Why(rig);
        Assert.Equal(SwapReasons.MaterialInvalid, rig.Final().Reason);
        Assert.False(File.Exists(rig.PrevSavingPath), kind + ": 표식이 남았다 / " + why);
        Assert.False(Directory.Exists(Rbk(rig, "web")), kind + ": web.rbk 가 안 치워졌다 / " + why);
        Assert.False(Directory.Exists(Rbk(rig, "watchdog")), kind + ": watchdog.rbk 가 안 치워졌다 / " + why);
        var now = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in LocalSwapWorkerRig.AllFiles(rig.PrevDir))
            if (!kv.Key.Contains(Path.DirectorySeparatorChar)) now[kv.Key] = kv.Value;
        AssertSame(lists, now, kind + " prev 목록 파일");
        AssertSame(prevApi, LocalSwapWorkerRig.AllFiles(Path.Combine(rig.PrevDir, "api")), kind + " prev\\api");
        Assert.Contains("stale", rig.Log());
    }

    [Fact(DisplayName = "봉합3 G-PV4 대조군 🔴 표식 to 조건 뺀 사본 → (i) 에서 남의 판을 보관으로 마쳐 prev 에 목록 파일을 쓴다(게이트가 FAIL 을 낸다)")]
    public void Control_without_to_check_writes_into_prev()
    {
        using var rig = MakeStaleMark("to-moved", out var lists);
        Assert.Empty(lists);
        Assert.Equal(0, rig.Run(ControlNoMarkToCheck(), "S1"));
        Assert.True(File.Exists(Path.Combine(rig.PrevDir, "version.txt")), "대조군인데 prev 에 version.txt 가 안 생겼다 / " + Why(rig));
    }

    // ── G-S0X — S0 정리가 던지면 broken 이 아니라 정지 전 정직한 거부 ──

    [Theory(DisplayName = "봉합3 G-S0X 🚨 S0 정리 던짐(LOTHROW) → refused · cleanup_pending(첨부 있으면 carry_pending) · step S0 · broken 0 · /DISABLE 0 · api.rbk 그대로")]
    [InlineData("rollback", true)]
    [InlineData("rollback", false)]
    [InlineData("update", true)]
    [InlineData("update", false)]
    public void S0_cleanup_throw_is_refused_not_broken(string mode, bool withCarry)
    {
        using var rig = Make(mode);
        rig.SeedLeftoverRbk(withCarry);
        var before = LocalSwapWorkerRig.AllFiles(ApiRbk(rig));
        Assert.Equal(0, rig.Run(null, "LOTHROW"));
        var final = rig.Final();
        var why = Why(rig);
        var want = withCarry ? CarryPending : CleanupPending;
        Assert.True(final.State == SwapStates.Refused && final.Reason == want, mode + "/" + withCarry + ": 끝 " + final.State + "/" + final.Reason + " (기대 refused/" + want + ") / " + why);
        Assert.Equal("S0", final.Step);
        Assert.Equal(0, rig.CountCalls("/DISABLE"));
        Assert.Equal("Running", rig.ServiceState());
        Assert.Contains("injected LOTHROW", rig.Log());
        AssertSame(before, LocalSwapWorkerRig.AllFiles(ApiRbk(rig)), "api.rbk");
        Assert.Empty(rig.LeftoverTasks());
    }

    [Theory(DisplayName = "봉합3 G-S0X 대조군 🔴 자체 catch 가 다시 던지는 사본 → broken/revert_failed step S0(게이트가 FAIL 을 낸다)")]
    [InlineData("rollback")]
    [InlineData("update")]
    public void Control_S0_throw_ends_broken(string mode)
    {
        using var rig = Make(mode);
        rig.SeedLeftoverRbk();
        Assert.Equal(0, rig.Run(ControlNoOwnCatch(), "LOTHROW"));
        var final = rig.Final();
        Assert.Equal(SwapStates.Broken, final.State);
        Assert.Equal(SwapReasons.RevertFailed, final.Reason);
        Assert.Equal("S0", final.Step);
    }

    // ══════════════════════════════════════════════════════════════
    // 봉합4 D — 「모르면 남긴다」 · 병렬이슈 18(표식 못 읽음) · [4] P2-2(표식 못 씀) · P2-1(부분 이동 게이트 공백) · 설계 §17 · 작업지시서 §15
    // ══════════════════════════════════════════════════════════════
    // 🔴 D-0 실측(10/1 · PS 5.1.26100 · $ErrorActionPreference='Stop'): 폴더 안 마지막 열거 파일(lib\…)을 FileShare.None 으로 쥐고 그 폴더를
    //    Move-Item → 앞 두 파일은 옮겨지고 던진다(부분 이동). 첫 파일을 쥐면 아무것도 안 옮기고 던지지만 목적지 빈 폴더는 생긴다.
    //    열거 순서(Get-ChildItem -Recurse -File) = 맨 위 파일 → 하위 폴더 = Move-Item 이 옮기는 순서.

    /// <summary>봉합4 대조군 — 「못 읽음」을 종전처럼 낡음으로 보는 사본(= 봉합4 전 안쪽 catch).</summary>
    public static string ControlUnreadAsStale() => Sub(LocalSwapWorkerRig.OriginalScript(),
        "$o.from = $null; $o.to = $null; $o.stale = $false; $o.unread = $true",
        "$o.unread = $false # (control) unreadable judged stale", "표식 못 읽음 판정");

    /// <summary>봉합4 대조군 — 표식이 24시간 넘게 못 읽히면 낡음으로 보는 사본(⬛ 설계 17-0 「풀리는 길」 · 사장님 V-3 로 폐기된 안).</summary>
    public static string ControlUnreadStaleAfterHours() => Sub(LocalSwapWorkerRig.OriginalScript(),
        "$o.from = $null; $o.to = $null; $o.stale = $false; $o.unread = $true",
        "if (((Get-Date).ToUniversalTime() - (Get-Item -LiteralPath $path -Force).LastWriteTimeUtc).TotalHours -gt 24) { $o.unread = $false } else { $o.from = $null; $o.to = $null; $o.stale = $false; $o.unread = $true }",
        "표식 못 읽음 판정");

    // ── G-PV8 — 표식 못 읽음(18): 재시도·S0 모두 .rbk·표식 무접촉 · 시간이 지나도 풀지 않는다 ──

    [Fact(DisplayName = "봉합4 G-PV8(a) 🚨 첫 보관 못 마침(PVH) + 표식을 끝까지 쥠(PVMR) → success+cleanup_pending · web.rbk·watchdog.rbk 해시 = 옛 판 · 표식 남음 · prev\\api = 옛 판")]
    public void Unreadable_mark_keeps_the_old_generation_in_the_retry()
    {
        using var rig = Make("update");
        var gen = rig.LiveGeneration();
        Assert.Equal(0, rig.Run(null, "PVH,PVMR"));
        var final = rig.Final();
        var why = Why(rig);
        Assert.True(final.State == SwapStates.Success && final.Reason == CleanupPending, "끝 " + final.State + "/" + final.Reason + " / " + why);
        Assert.Contains("test hold mark", rig.Log());                                   // 주입이 실제로 걸렸다
        Assert.Equal(3, rig.CountLog("success cleanup: prev-saving mark unreadable")); // 재시도 3회 모두 못 읽음
        AssertSame(PartOf(gen, "web"), LocalSwapWorkerRig.AllFiles(Rbk(rig, "web")), "web.rbk");
        AssertSame(PartOf(gen, "watchdog"), LocalSwapWorkerRig.AllFiles(Rbk(rig, "watchdog")), "watchdog.rbk");
        Assert.True(File.Exists(rig.PrevSavingPath), "보관 표식이 없다 / " + why);
        AssertSame(PartOf(gen, "api"), LocalSwapWorkerRig.AllFiles(Path.Combine(rig.PrevDir, "api")), "prev\\api");
    }

    [Fact(DisplayName = "봉합4 G-PV8(a) 대조군 🔴 못 읽음 = 낡음 사본(봉합4 전) → 재시도가 web.rbk·watchdog.rbk 를 지운다(게이트가 FAIL 을 낸다)")]
    public void Control_unreadable_mark_as_stale_deletes_in_the_retry()
    {
        using var rig = Make("update");
        Assert.Equal(0, rig.Run(ControlUnreadAsStale(), "PVH,PVMR"));
        Assert.Contains("test hold mark", rig.Log());
        Assert.False(Directory.Exists(Rbk(rig, "watchdog")), "대조군에서 watchdog.rbk 가 안 지워졌다 / " + Why(rig));
    }

    /// <summary>G-PV3 시드(보관 반쪽) · 표식 시각을 <paramref name="markAgeHours"/> 시간 전으로 · 표식을 FileShare.None 으로 쥔 채 일꾼을 S1 주입으로 돌린다.</summary>
    private static int RunWithMarkHeld(LocalSwapWorkerRig rig, string? script, int markAgeHours)
    {
        if (markAgeHours > 0) File.SetLastWriteTimeUtc(rig.PrevSavingPath, DateTime.UtcNow.AddHours(-markAgeHours));
        using var held = new FileStream(rig.PrevSavingPath, FileMode.Open, FileAccess.Read, FileShare.None);
        return rig.Run(script, "S1");
    }

    [Theory(DisplayName = "봉합4 G-PV8(b)(c) 🚨 보관 반쪽 + 표식을 시험이 쥠((c) 표식 25시간 전) → refused/cleanup_pending · S0 · /DISABLE 0 · .rbk 해시 = 옛 판 · 표식 남음 · prev 무접촉")]
    [InlineData("rollback", 0)]
    [InlineData("update", 0)]
    [InlineData("rollback", 25)]
    [InlineData("update", 25)]
    public void Unreadable_mark_is_refused_at_S0_and_kept(string mode, int markAgeHours)
    {
        using var rig = Make(mode);
        var gen = rig.SeedHalfPrev(OldGen);
        var prevBefore = LocalSwapWorkerRig.AllFiles(rig.PrevDir);
        var markBefore = File.ReadAllText(rig.PrevSavingPath);
        Assert.Equal(0, RunWithMarkHeld(rig, null, markAgeHours));
        var final = rig.Final();
        var why = Why(rig);
        Assert.True(final.State == SwapStates.Refused && final.Reason == CleanupPending, mode + "/" + markAgeHours + "h: 끝 " + final.State + "/" + final.Reason + " / " + why);
        Assert.Equal("S0", final.Step);
        Assert.Equal(0, rig.CountCalls("/DISABLE"));
        Assert.Equal("Running", rig.ServiceState());
        Assert.Contains("S0 prev-saving mark unreadable", rig.Log());
        AssertSame(PartOf(gen, "web"), LocalSwapWorkerRig.AllFiles(Rbk(rig, "web")), "web.rbk");
        AssertSame(PartOf(gen, "watchdog"), LocalSwapWorkerRig.AllFiles(Rbk(rig, "watchdog")), "watchdog.rbk");
        Assert.Equal(markBefore, File.ReadAllText(rig.PrevSavingPath));
        AssertSame(prevBefore, LocalSwapWorkerRig.AllFiles(rig.PrevDir), "prev");
        Assert.Empty(rig.LeftoverTasks());
    }

    [Theory(DisplayName = "봉합4 G-PV8(b) 대조군 🔴 못 읽음 = 낡음 사본(봉합4 전) → S0 가 M5 로 web.rbk·watchdog.rbk 를 지운다(게이트가 FAIL 을 낸다)")]
    [InlineData("rollback")]
    [InlineData("update")]
    public void Control_unreadable_mark_as_stale_deletes_at_S0(string mode)
    {
        using var rig = Make(mode);
        rig.SeedHalfPrev(OldGen);
        Assert.Equal(0, RunWithMarkHeld(rig, ControlUnreadAsStale(), 0));
        Assert.False(Directory.Exists(Rbk(rig, "web")), mode + ": 대조군에서 web.rbk 가 안 지워졌다 / " + Why(rig));
        Assert.False(Directory.Exists(Rbk(rig, "watchdog")), mode + ": 대조군에서 watchdog.rbk 가 안 지워졌다 / " + Why(rig));
    }

    [Theory(DisplayName = "봉합4 G-PV8(c) 대조군 🔴 24시간 지나면 낡음으로 보는 사본(⬛ 폐기안) → 25시간 전 표식에서 .rbk 를 지운다(게이트가 FAIL 을 낸다)")]
    [InlineData("rollback")]
    [InlineData("update")]
    public void Control_unreadable_mark_expires_after_hours(string mode)
    {
        using var rig = Make(mode);
        rig.SeedHalfPrev(OldGen);
        Assert.Equal(0, RunWithMarkHeld(rig, ControlUnreadStaleAfterHours(), 25));
        Assert.False(Directory.Exists(Rbk(rig, "web")), mode + ": 대조군에서 web.rbk 가 안 지워졌다 / " + Why(rig));
    }

    // ── G-PV7 — 표식 못 씀(P2-2): .rbk 이동 멈춤 · 재시도는 표식 다시 쓰기만 ──

    /// <summary>봉합4 대조군 — 성공 재시도의 「표식 없음」 갈래(17-1 ⓒ)를 뺀 사본(= 봉합4 전: 표식 없으면 .rbk 를 지운다).</summary>
    public static string ControlNoUnmarkedRetry() => Sub(LocalSwapWorkerRig.OriginalScript(),
        "if ($null -eq $pm -and $script:PrevSaveUnmarked) {",
        "if ($false) { # (control) seal4 P2-2 (c) removed", "표식 없음 재시도 갈래");

    /// <summary>봉합4 대조군 — 17-1 ⓒ 의 「표식 다시 쓰기」만 뺀 사본(.rbk 는 남기되 보관을 마치지 못한다).</summary>
    public static string ControlNoMarkRewrite() => Sub(LocalSwapWorkerRig.OriginalScript(),
        "elseif (Write-PrevSavingMark) { [void](Complete-PrevGeneration ([string]$script:Req.from) ([string]$script:Req.to)) }",
        "elseif ($false) { } # (control) mark rewrite removed", "표식 다시 쓰기");

    [Fact(DisplayName = "봉합4 G-PV7(a) 🚨 표식 매번 못 씀(PVMW) → success+cleanup_pending · api·web·watchdog.rbk 파일 수·해시 = 옛 판 · 표식 없음 · prev 안 부분 0")]
    public void Mark_never_written_keeps_the_old_generation_unmoved()
    {
        using var rig = Make("update");
        var gen = rig.LiveGeneration();
        Assert.Equal(0, rig.Run(null, "PVMW"));
        var final = rig.Final();
        var why = Why(rig);
        Assert.True(final.State == SwapStates.Success && final.Reason == CleanupPending, "끝 " + final.State + "/" + final.Reason + " / " + why);
        Assert.Equal(4, rig.CountLog("injected PVMW:"));                              // 첫 보관 1 + 재시도 3 — 주입이 매번 걸렸다
        Assert.Equal(3, rig.CountLog("success cleanup: prev-saving mark still not written"));
        foreach (var p in new[] { "api", "web", "watchdog" })
        {
            AssertSame(PartOf(gen, p), LocalSwapWorkerRig.AllFiles(Rbk(rig, p)), p + ".rbk");
            Assert.False(Directory.Exists(Path.Combine(rig.PrevDir, p)), "prev\\" + p + " 가 생겼다 / " + why);
        }
        Assert.False(File.Exists(rig.PrevSavingPath), "보관 표식이 생겼다 / " + why);
    }

    [Fact(DisplayName = "봉합4 G-PV7(a) 대조군 🔴 표식 없음 갈래(17-1 ⓒ) 뺀 사본(봉합4 전) → 재시도가 .rbk 세 개를 지운다 = 두 세대 모두 없음(게이트가 FAIL 을 낸다)")]
    public void Control_mark_never_written_deletes_the_old_generation()
    {
        using var rig = Make("update");
        Assert.Equal(0, rig.Run(ControlNoUnmarkedRetry(), "PVMW"));
        Assert.Contains("injected PVMW:", rig.Log());
        foreach (var p in new[] { "api", "web", "watchdog" })
            Assert.False(Directory.Exists(Rbk(rig, p)), "대조군에서 " + p + ".rbk 가 안 지워졌다 / " + Why(rig));
        Assert.Empty(rig.PrevParts());
    }

    [Fact(DisplayName = "봉합4 G-PV7(b) 🚨 표식 첫 쓰기만 실패(PVMW1) → 재시도가 표식을 다시 쓰고 보관을 마침 · success 사유 없음 · prev 완성 · 해시 = 옛 판 · 표식·.rbk 없음")]
    public void Mark_written_by_the_retry_finishes_the_saving()
    {
        using var rig = Make("update");
        var gen = rig.LiveGeneration();
        Assert.Equal(0, rig.Run(null, "PVMW1"));
        var final = rig.Final();
        var why = Why(rig);
        Assert.True(final.State == SwapStates.Success && string.IsNullOrEmpty(final.Reason), "끝 " + final.State + "/" + final.Reason + " / " + why);
        Assert.Equal(1, rig.CountLog("injected PVMW1"));                              // 첫 쓰기는 실제로 실패했다
        AssertPrevComplete(rig, gen, LocalSwapWorkerRig.To, LocalSwapWorkerRig.From, why);
    }

    [Fact(DisplayName = "봉합4 G-PV7(b) 대조군 🔴 표식 다시 쓰기 뺀 사본 → .rbk 는 남지만 보관 못 마침 · success+cleanup_pending(게이트가 FAIL 을 낸다)")]
    public void Control_without_mark_rewrite_is_cleanup_pending()
    {
        using var rig = Make("update");
        Assert.Equal(0, rig.Run(ControlNoMarkRewrite(), "PVMW1"));
        var final = rig.Final();
        Assert.True(final.State == SwapStates.Success && final.Reason == CleanupPending, "대조군 끝 " + final.State + "/" + final.Reason + " / " + Why(rig));
        Assert.True(Directory.Exists(Rbk(rig, "web")), "대조군에서 web.rbk 가 없다 / " + Why(rig));
    }

    // ── G-PV5 · G-PV6 — 부분 이동(P2-1): 마지막 열거 파일을 쥐면 앞 파일은 옮겨지고 던진다(D-0) ──

    /// <summary>봉합4 대조군 — <c>Complete-PrevGeneration</c> 합치기 경로를 뺀 사본(<c>prev\{p}</c> 가 이미 있으면 바로 $false).</summary>
    public static string ControlNoMergePath() => Sub(LocalSwapWorkerRig.OriginalScript(),
        "$base = (Get-Item -LiteralPath $rbk -Force).FullName.TrimEnd('\\')",
        "return $false # (control) merge path removed", "합치기 경로");

    /// <summary>봉합4 대조군 — <c>Complete-PrevGeneration</c> 의 <c>Test-DirFree</c> 선확인을 뺀 사본.</summary>
    public static string ControlNoDirFreeCheck() => Sub(LocalSwapWorkerRig.OriginalScript(),
        "if (-not (Test-DirFree $rbk)) { Write-Log ('previous generation not finished: a file in ' + $rbk + ' is in use'); return $false }",
        "# (control) Test-DirFree pre-check removed", "보관 Test-DirFree 선확인");

    /// <summary>업데이트 한 판 · 살아 있는 web 에 파일 셋(맨 위 둘 + lib\ 하나).</summary>
    private static LocalSwapWorkerRig MakeMultiFileWeb()
    {
        var rig = Make("update");
        rig.AddLiveFiles("web");
        Assert.Equal(3, LocalSwapWorkerRig.AllFiles(Path.Combine(rig.App, "web")).Count);
        return rig;
    }

    [Fact(DisplayName = "봉합4 G-PV5 🚨 첫 보관에서 web.rbk 마지막 파일을 쥔 채 이동(PVL) = 실제 부분 이동 → 재시도가 합친다 · success 사유 없음 · prev 완성 · prev\\web 해시 = 옛 판(쥔 파일 포함) · 표식·.rbk 없음")]
    public void Half_moved_part_is_merged_by_the_retry()
    {
        using var rig = MakeMultiFileWeb();
        var gen = rig.LiveGeneration();
        Assert.Equal(0, rig.Run(null, "PVL"));
        var final = rig.Final();
        var why = Why(rig);
        Assert.True(final.State == SwapStates.Success && string.IsNullOrEmpty(final.Reason), "끝 " + final.State + "/" + final.Reason + " / " + why);
        Assert.Equal(1, rig.CountLog("test hold last "));                                                   // 주입이 한 번 걸렸다
        Assert.Contains("previous generation save not finished", rig.Log());                                // 첫 이동이 실제로 던졌다
        Assert.Contains("previous generation: the rest of", rig.Log());                                     // 합치기 경로가 돌았다
        AssertPrevComplete(rig, gen, LocalSwapWorkerRig.To, LocalSwapWorkerRig.From, why);
    }

    [Fact(DisplayName = "봉합4 G-PV5 대조군 🔴 합치기 경로 뺀 사본 → web.rbk 에 쥔 파일 하나만 남고 prev\\web 에 나머지 둘 = 부분 이동이 실제로 났다 · sha256.txt 없음 · cleanup_pending(게이트가 FAIL 을 낸다)")]
    public void Control_without_merge_path_leaves_the_half_move()
    {
        using var rig = MakeMultiFileWeb();
        var gen = rig.LiveGeneration();
        Assert.Equal(0, rig.Run(ControlNoMergePath(), "PVL"));
        var final = rig.Final();
        var why = Why(rig);
        Assert.True(final.State == SwapStates.Success && final.Reason == CleanupPending, "대조군 끝 " + final.State + "/" + final.Reason + " / " + why);
        var left = LocalSwapWorkerRig.AllFiles(Rbk(rig, "web"));
        var moved = LocalSwapWorkerRig.AllFiles(Path.Combine(rig.PrevDir, "web"));
        Assert.Equal(new[] { Path.Combine("lib", "web-lib.dll") }, left.Keys.ToArray());                    // 쥔 마지막 파일만 남았다
        Assert.Equal(PartOf(gen, "web").Count - 1, moved.Count);                                            // 앞 파일들은 옮겨졌다
        Assert.False(File.Exists(Path.Combine(rig.PrevDir, "sha256.txt")), "대조군인데 sha256.txt 가 있다 / " + why);
    }

    [Fact(DisplayName = "봉합4 G-PV6 🚨 web.rbk 마지막 파일을 끝까지 쥠(PVLL) → success+cleanup_pending · prev\\web 없음(반쪽 0) · web.rbk 파일 수·해시 = 옛 판 · sha256.txt 없음 · 표식 남음")]
    public void Held_last_file_never_splits_the_part()
    {
        using var rig = MakeMultiFileWeb();
        var gen = rig.LiveGeneration();
        Assert.Equal(0, rig.Run(null, "PVLL"));
        var final = rig.Final();
        var why = Why(rig);
        Assert.True(final.State == SwapStates.Success && final.Reason == CleanupPending, "끝 " + final.State + "/" + final.Reason + " / " + why);
        Assert.Equal(1, rig.CountLog("test hold last "));
        Assert.False(Directory.Exists(Path.Combine(rig.PrevDir, "web")), "prev\\web 가 생겼다(반쪽) / " + why);
        AssertSame(PartOf(gen, "web"), LocalSwapWorkerRig.AllFiles(Rbk(rig, "web")), "web.rbk");
        Assert.False(File.Exists(Path.Combine(rig.PrevDir, "sha256.txt")), "sha256.txt 가 있다 / " + why);
        Assert.True(File.Exists(rig.PrevSavingPath), "보관 표식이 없다 / " + why);
    }

    [Fact(DisplayName = "봉합4 G-PV6 대조군 🔴 Test-DirFree 선확인 뺀 사본 → prev\\web 반쪽 · web.rbk 해시 ≠ 옛 판(게이트가 FAIL 을 낸다)")]
    public void Control_without_dir_free_check_splits_the_part()
    {
        using var rig = MakeMultiFileWeb();
        var gen = rig.LiveGeneration();
        Assert.Equal(0, rig.Run(ControlNoDirFreeCheck(), "PVLL"));
        var why = Why(rig);
        Assert.True(Directory.Exists(Path.Combine(rig.PrevDir, "web")), "대조군인데 prev\\web 가 없다 / " + why);
        Assert.NotEqual(PartOf(gen, "web").Count, LocalSwapWorkerRig.AllFiles(Rbk(rig, "web")).Count);
    }
}
