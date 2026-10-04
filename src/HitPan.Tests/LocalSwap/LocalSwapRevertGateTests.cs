using HitPan.API.Services.LocalSwap;
using Xunit;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// 🚨 20260930작1 봉합 K1 — G-S7a 「원위치(S7)가 예외를 던져도 다시 켠다」(설계 §14-1 F-1 · 작업지시서 §12-5).
/// </summary>
/// <remarks>
/// <para>🔴 <b>잡는 사고</b> — 옛 S7 은 <c>try { Stop-All ; Invoke-Revert ; Start-All ; Test-Running }</c> 한 덩어리였다.
/// <c>Invoke-Revert</c> 가 던지면 같은 블록의 <c>Start-All</c> 을 건너뛰고 안쪽 catch 가 삼켜 <b>keepalive 두 작업·워치독·Guardian 이
/// 꺼진 채</b> <c>broken</c> 으로 끝났다(고객 손 없이는 안 켜진다 · #28·#30).</para>
/// <para>🟢 주입점(일꾼 <c>-TestRoot</c> 모드만): <c>S7R</c> = <c>Invoke-Revert</c> 첫 줄 throw · <c>S7R2</c> = 첫 폴더 원위치 뒤 throw.
/// 조합 <c>S6,S7R</c>(켠 뒤 확인 실패 → 원위치 실패) · <c>S3,S7R</c>(멈추기 실패 → 원위치 실패) · <c>S6,S7R2</c>(반쯤 원위치).</para>
/// <para>🔴 대조군 — S7 을 옛 모양(<c>Start-All</c> 이 <c>Invoke-Revert</c> 뒤 같은 try)으로 둔 사본은 마지막 keepalive 호출이 <c>/DISABLE</c>.</para>
/// </remarks>
public sealed class LocalSwapRevertGateTests
{
    private const string ApiKeepalive = "HitPan-ERP-API-keepalive-1";
    private const string WebKeepalive = "HitPan-ERP-WEB-keepalive-1";
    private const string Guardian = "HitPanWatchdogGuardian";

    /// <summary>봉합 줄(원위치 성패와 무관하게 finally 에서 켠다) — 대조군이 이것을 옛 모양으로 되돌린다.</summary>
    private const string SealedLines =
        "                        $revertOk = $true\n" +
        "                    } catch { Write-Log ('S7 revert failed: ' + $_.Exception.Message) }\n" +
        "                    finally { [void](Start-All) }";

    /// <summary>옛 모양 — <c>Start-All</c> 이 <c>Invoke-Revert</c> 뒤 같은 try 안(던지면 건너뛴다).</summary>
    private const string OldShapeLines =
        "                        $revertOk = $true\n" +
        "                        [void](Start-All)\n" +
        "                    } catch { Write-Log ('S7 revert failed: ' + $_.Exception.Message) }";

    public static string ControlOldShape()
    {
        var original = LocalSwapWorkerRig.OriginalScript();
        var crlf = original.Contains("\r\n", StringComparison.Ordinal);
        string Eol(string s) => crlf ? s.Replace("\n", "\r\n", StringComparison.Ordinal) : s;
        var target = Eol(SealedLines);
        Assert.True(original.Contains(target, StringComparison.Ordinal), "S7 봉합 줄을 못 찾았다 — 대조군을 같이 고쳐라");
        return original.Replace(target, Eol(OldShapeLines), StringComparison.Ordinal);
    }

    private static void AssertSwitchedBackOn(LocalSwapWorkerRig rig, SwapRequest final)
    {
        var why = rig.Log() + "\n--- calls ---\n" + rig.Calls();
        Assert.True(final.State == SwapStates.Broken, "끝 상태 " + final.State + " / " + why);
        Assert.Equal(SwapReasons.RevertFailed, final.Reason);
        Assert.True(rig.LastCall(ApiKeepalive).EndsWith("/ENABLE", StringComparison.Ordinal), "API keepalive 마지막 호출: " + rig.LastCall(ApiKeepalive) + " / " + why);
        Assert.True(rig.LastCall(WebKeepalive).EndsWith("/ENABLE", StringComparison.Ordinal), "WEB keepalive 마지막 호출: " + rig.LastCall(WebKeepalive));
        Assert.True(rig.LastCall(Guardian).EndsWith("/ENABLE", StringComparison.Ordinal), "Guardian 마지막 호출: " + rig.LastCall(Guardian));
        Assert.Equal("Running", rig.ServiceState());
        Assert.DoesNotContain(LocalSwapLauncher.TaskName, rig.LeftoverTasks());
    }

    [Theory(DisplayName = "K1 G-S7a 🚨 원위치가 던져도 다시 켠다 — 끝 broken · keepalive(API·WEB)·Guardian 마지막 호출 /ENABLE · 서비스 Running · HitPan-LocalSwap 잔존 0")]
    [InlineData("S6,S7R")]
    [InlineData("S3,S7R")]
    [InlineData("S6,S7R2")]
    public void Revert_throw_still_starts_everything(string failAt)
    {
        using var rig = LocalSwapWorkerRig.Rollback(DateTime.UtcNow);
        var exit = rig.Run(null, failAt);
        Assert.True(exit == 0, "일꾼 종료 코드 " + exit + " / " + rig.Log());
        AssertSwitchedBackOn(rig, rig.Final());
    }

    [Fact(DisplayName = "K1 G-S7a 기록 — S6,S7R2(반쯤 원위치) 뒤 판이 섞인 채 켠다: watchdog 옛 판 · api·web 새 판 · api.rbk·web.rbk 남음(S-3 · [4] 실측 대상)")]
    public void Half_revert_leaves_mixed_generation_and_runs()
    {
        using var rig = LocalSwapWorkerRig.Rollback(DateTime.UtcNow);
        var prev = Path.Combine(rig.Work, "prev");
        var appWebBefore = LocalSwapWorkerRig.PartMark(rig.App, "web");
        var prevWebBefore = LocalSwapWorkerRig.PartMark(prev, "web");

        Assert.Equal(0, rig.Run(null, "S6,S7R2"));
        AssertSwitchedBackOn(rig, rig.Final());

        // 원위치는 뒤에서부터(watchdog → web → api) — 첫 폴더(watchdog)만 돌아오고 멈췄다
        Assert.Equal(LocalSwapWorkerRig.From, LocalSwapWorkerRig.PartMark(rig.App, "watchdog")); // 옛 판(원위치됨)
        Assert.Equal(LocalSwapWorkerRig.To, LocalSwapWorkerRig.PartMark(rig.App, "api"));        // 새 판 그대로
        Assert.Equal(prevWebBefore, LocalSwapWorkerRig.PartMark(rig.App, "web"));                 // 새 판 그대로
        Assert.True(Directory.Exists(Path.Combine(rig.App, "api.rbk")), "api.rbk 가 남아야 한다(원위치 못 함)");
        Assert.True(Directory.Exists(Path.Combine(rig.App, "web.rbk")), "web.rbk 가 남아야 한다(원위치 못 함)");
        Assert.False(Directory.Exists(Path.Combine(rig.App, "watchdog.rbk")), "watchdog.rbk 는 원위치로 사라져야 한다");
        Assert.Equal(LocalSwapWorkerRig.From, LocalSwapWorkerRig.PartMark(rig.App, "api.rbk"));
        Assert.Equal(appWebBefore, File.ReadAllText(Path.Combine(rig.App, "web.rbk", "web.bin")));
    }

    [Theory(DisplayName = "K1 G-S7a 대조군 🔴 S7 옛 모양 사본 → 원위치가 던지면 keepalive 마지막 호출이 /DISABLE · 서비스 Stopped(게이트가 FAIL 을 낸다)")]
    [InlineData("S6,S7R")]
    [InlineData("S3,S7R")]
    [InlineData("S6,S7R2")]
    public void Control_old_shape_leaves_everything_off(string failAt)
    {
        using var rig = LocalSwapWorkerRig.Rollback(DateTime.UtcNow);
        Assert.Equal(0, rig.Run(ControlOldShape(), failAt));
        Assert.Equal(SwapStates.Broken, rig.Final().State);
        Assert.EndsWith("/DISABLE", rig.LastCall(ApiKeepalive), StringComparison.Ordinal);
        Assert.EndsWith("/DISABLE", rig.LastCall(WebKeepalive), StringComparison.Ordinal);
        Assert.Equal("Stopped", rig.ServiceState());
    }
}
