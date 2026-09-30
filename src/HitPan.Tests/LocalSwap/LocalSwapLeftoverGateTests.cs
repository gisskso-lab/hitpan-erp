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
}
