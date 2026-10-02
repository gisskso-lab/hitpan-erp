using System.Security.Cryptography;
using System.Text.Json.Nodes;
using HitPan.API.Services.LocalSwap;
using Xunit;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// 🚨 20260930작1 확대 1.3.50 갈래 N6 — G-NW5 일꾼 「이력이 막 태어난 상태」(설계 §19-8 판정 4a · 작업지시서 18-9 X-12).
/// </summary>
/// <remarks>
/// <para>🔴 <b>잡는 사고</b> — 1.3.50 의 판 이력은 한 줄로 태어나 R-21c 가 첫 회 되돌리기를 늘 거부한다. API T3 과 같은 모양으로
/// 「읽힌 줄 전부 == from + 저장본 manifests 중 from 아래 정확히 1개 == to(&lt; from)」일 때만 허용한다. 그 밖은 지금 규칙 그대로.</para>
/// <para>대조 = 허용 줄을 <c>if ($false) {</c> 로 끈 사본(같은 줄에 본문이 이어져 <c>&lt;# … #&gt;</c> 블록 주석).</para>
/// <para>이름 앞 N6 — 같은 클래스의 다른 갈래 partial 과 이름 겹침 0. 기존 시험 파일·<c>.Behavior.cs</c>(N4c) 무접촉.</para>
/// </remarks>
public sealed partial class LocalSwapPrevFetchGateTests
{
    private const string N6From = "1.3.50";
    private const string N6To = "1.3.49";

    /// <summary>N6 허용 줄 머리 — 대조 사본이 찾는 글자(기대값 아님).</summary>
    public const string N6WorkerHead = "if (Test-FirstRunFetchedZip $vers $now $to) {";

    public static string N6ControlNoFirstRun() =>
        S21Sub(LocalSwapWorkerRig.OriginalScript(), N6WorkerHead, "<# (control) N6 off #> if ($false) {");

    /// <summary>되돌리기 from → to · 재료 = <c>{app}\manual\staging\hitpan-{to}.zip</c>(p1·p2 통과 모양).</summary>
    private static LocalSwapWorkerRig N6ManualZipRig(string from, string to)
    {
        var rig = LocalSwapWorkerRig.RollbackZip(DateTime.UtcNow, from, to);
        var zip = Path.Combine(rig.ManualStaging, "hitpan-" + to + ".zip");
        LocalSwapWorkerRig.MakeZip(zip, to);
        rig.SetRequestField("material", new JsonObject
        {
            ["kind"] = SwapMaterialKinds.ManualZip,
            ["path"] = zip,
            ["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip))).ToLowerInvariant(),
        });
        return rig;
    }

    private static void N6Keep(LocalSwapWorkerRig rig, params string[] versions)
    {
        var dir = Path.Combine(rig.Work, "manifests");
        Directory.CreateDirectory(dir);
        foreach (var v in versions) File.WriteAllText(Path.Combine(dir, v + ".json"), "{}");
    }

    [Fact(DisplayName = "N6 G-NW5 🚨 되돌리기 + manual_zip · 이력 1.3.50 한 줄 · from 1.3.50 · to 1.3.49 · manifests\\1.3.49.json 있음 → R-21c 통과 · S1 재료 준비")]
    public void Nw5_first_run_history_allowed()
    {
        using var rig = N6ManualZipRig(N6From, N6To);
        S21WriteSeen(rig, N6From);
        N6Keep(rig, N6To, N6From);
        Assert.Equal(0, rig.Run());
        Assert.Equal(0, rig.CountLog("S1 fetched zip refused"));
        Assert.True(rig.CountLog("S1 fetched zip first run") >= 1, "N6 허용 줄이 아닌 다른 길로 통과했다 / " + S21Why(rig));
        Assert.True(rig.CountLog("S1 material ready") >= 1, "막 태어난 이력인데 S1 재료 준비까지 안 갔다 / " + rig.Final().State + "/" + rig.Final().Reason + " / " + S21Why(rig));
    }

    [Theory(DisplayName = "N6 G-NW5 🚨 to > from · 저장본 없음 · 저장본 아래 2개 · 이력 1.3.48,1.3.50(지금 규칙) → refused(to > from 은 기존 S0 request_invalid · 나머지 material_invalid) · 프로그램 폴더·stage 무변화")]
    [InlineData("to-above")]
    [InlineData("no-kept")]
    [InlineData("two-kept")]
    [InlineData("ledger2")]
    public void Nw5_other_shapes_refused(string which)
    {
        var to = which == "to-above" ? "1.3.51" : N6To;
        using var rig = N6ManualZipRig(N6From, to);
        switch (which)
        {
            case "to-above": S21WriteSeen(rig, N6From); N6Keep(rig, to); break;
            case "no-kept": S21WriteSeen(rig, N6From); break;
            case "two-kept": S21WriteSeen(rig, N6From); N6Keep(rig, "1.3.48", N6To); break;
            case "ledger2": S21WriteSeen(rig, "1.3.48", N6From); N6Keep(rig, N6To); break;
            default: throw new ArgumentException(which);
        }
        Assert.Equal(0, rig.Run());
        var final = rig.Final();
        // to > from 은 기존 S0 「rollback target is not lower」 가 request_invalid 로 먼저 막는다(실측 10/2 · 설계 19-8 표의 material_invalid 와 다름 → 개발명세서 N6 §5).
        var expected = which == "to-above" ? SwapReasons.RequestInvalid : SwapReasons.MaterialInvalid;
        Assert.True(final.State == SwapStates.Refused && final.Reason == expected,
            which + ": " + final.State + "/" + final.Reason + " / " + S21Why(rig));
        Assert.Equal(N6From, LocalSwapWorkerRig.PartMark(rig.App, "api"));
        Assert.Equal(N6From, LocalSwapWorkerRig.PartMark(rig.App, "watchdog"));
        foreach (var p in new[] { "api", "web", "watchdog" })
            Assert.False(Directory.Exists(Path.Combine(rig.App, p + ".rbk")), which + ": " + p + ".rbk 가 생겼다 / " + S21Why(rig));
        Assert.False(Directory.Exists(Path.Combine(rig.Work, "stage", to)), which + ": stage 에 풀었다 / " + S21Why(rig));
        Assert.Equal(0, rig.CountLog("S1 fetched zip first run"));
        if (which == "to-above")
        {
            Assert.True(rig.CountLog("rollback target is not lower") >= 1, which + ": S0 가 아닌 다른 줄이 거부했다 / " + S21Why(rig));
            return;
        }
        // ledger2 는 기존 봉합 05(Get-StagingZipRefusal)가 먼저 막을 수 있다 — 이력 규칙 두 벽 중 하나면 된다(N5 G-NW4 ③ 과 같은 모양).
        Assert.True(rig.CountLog("S1 fetched zip refused") + (which == "ledger2" ? rig.CountLog("S1 staging zip refused") : 0) >= 1,
            which + ": 이력 규칙이 아닌 다른 줄이 거부했다 / " + S21Why(rig));
    }

    [Fact(DisplayName = "N6 G-NW5 대조군 🔴 N6 허용 줄을 if ($false) 로 끈 사본 → 막 태어난 이력 첫 경우가 refused·material_invalid(게이트가 FAIL 을 낸다)")]
    public void Nw5_control_first_run_off_refused()
    {
        var script = N6ControlNoFirstRun();
        using var rig = N6ManualZipRig(N6From, N6To);
        S21WriteSeen(rig, N6From);
        N6Keep(rig, N6To, N6From);
        Assert.Equal(0, rig.Run(script));
        var final = rig.Final();
        Assert.True(final.State == SwapStates.Refused && final.Reason == SwapReasons.MaterialInvalid,
            "대조군인데 거부되지 않았다 — N6 허용 줄이 아닌 다른 길이 통과시킨다 / " + final.State + "/" + final.Reason + " / " + S21Why(rig));
        Assert.True(rig.CountLog("S1 fetched zip refused") >= 1, S21Why(rig));
    }
}
