using System.Security.Cryptography;
using System.Text.Json.Nodes;
using HitPan.API.Services.LocalSwap;
using Xunit;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// 🚨 20260930작1 확대 1.3.50 갈래 N4 — 교체 일꾼(<c>local-swap.ps1</c>) 「되돌리기 + <c>manual_zip</c>」 게이트 G-NW1~3
/// (설계 §19-1 조각 B p1~p3 · §19-2 · 작업지시서 §18).
/// </summary>
/// <remarks>
/// <para>🔴 <b>잡는 사고</b> — 본사(NCP)에서 받은 이전 판 zip 으로 되돌릴 때, 일꾼이 (p1) 허용 뿌리·이름·해시 칸을 안 보고 받아들이거나
/// (p2) API 검증 뒤 놓인 zip 이 바뀌었는데 해시를 다시 안 대고 프로그램 폴더를 갈아 끼우거나
/// (p3) 성공 뒤 받은 zip 을 남기는 것.</para>
/// <para>🟢 일꾼 <c>-TestRoot</c> 모드만 쓴다(<see cref="LocalSwapWorkerRig"/>) — 실제 예약 작업·서비스·설치 폴더·바깥 주소 무접촉.
/// 판 = <see cref="LocalSwapWorkerRig.RollbackZip"/>(지금 1.3.48 · 되돌리기 모드 · prev 없음) 위에서 요청서의 <c>material</c> 칸만
/// <c>{app}\manual\staging\hitpan-1.3.47.zip</c> + sha256 으로 바꾼다(API 를 건너뛴 모양 — 기존 rig 무변경).</para>
/// <para>⏳ <b>빨강→초록 순서</b>: p1~p3 은 갈래 N2 가 덧붙인다. 그 전에는 양성 시험·대조군이 빨강이다(대조군은 N2 머리 글자를 못 찾아
/// 「대조군을 같이 고쳐라」로 멈춘다). 아래 머리 글자 상수는 <b>대조 사본이 찾는 글자</b>일 뿐 기대값이 아니다 —
/// N2 의 실제 글자가 다르면 이 상수 한 줄만 맞춘다(개발명세서 N4 §4).</para>
/// </remarks>
public sealed class LocalSwapPrevFetchWorkerGateTests
{
    /// <summary>p1 머리(<c>Test-RequestValid</c> · update 허용 줄 뒤) — 설계 §19-1 「rollback + manual_zip 허용」.</summary>
    public const string P1Head = "if ($Mode -eq 'rollback' -and $kind -eq 'manual_zip') {";

    /// <summary>p2 머리(<c>Initialize-Material</c> · update 해시 대조 뒤) — 설계 §19-1 「rollback + manual_zip 이면 해시 대조」.</summary>
    public const string P2Head = "if ($Mode -eq 'rollback' -and $mat.kind -eq 'manual_zip') {";

    /// <summary>p3 머리(<c>Clear-AfterSuccess</c> 되돌리기 갈래 · prev 지우기 줄 뒤) — 설계 §19-1 「재료가 manual_zip 이면 그 zip 지움」.</summary>
    public const string P3Head = "if ($script:Req.material.kind -eq 'manual_zip') {";

    private static string Why(LocalSwapWorkerRig rig) => rig.Log() + "\n--- calls ---\n" + rig.Calls();

    private static string Sha(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();

    /// <summary>
    /// 되돌리기 판(1.3.48 → 1.3.47)을 차리고 재료를 <c>manual_zip</c> 으로 바꾼다.
    /// <paramref name="zipDir"/> = zip 을 둘 폴더(null = <c>{app}\manual\staging</c>) · <paramref name="zipVersion"/> = 파일 이름의 판
    /// · <paramref name="sha"/> = 요청서에 적을 sha256(null = 실제 해시).
    /// </summary>
    private static (LocalSwapWorkerRig Rig, string Zip) Make(string? zipDir = null, string zipVersion = LocalSwapWorkerRig.To, string? sha = null)
    {
        var rig = LocalSwapWorkerRig.RollbackZip(DateTime.UtcNow);
        // 18-8 X-9 R-21c: rollback + manual_zip 은 판 이력(마지막 == from · 바로 위 == to)이 있어야 선다 — 정상 PC 모양의 이력을 깐다
        File.WriteAllText(rig.SeenPath, LocalSwapWorkerRig.To + "|2026-09-01T00:00:00Z\r\n" + LocalSwapWorkerRig.From + "|2026-09-02T00:00:00Z\r\n");
        var zip = Path.Combine(zipDir ?? rig.ManualStaging, "hitpan-" + zipVersion + ".zip");
        LocalSwapWorkerRig.MakeZip(zip, LocalSwapWorkerRig.To);
        rig.SetRequestField("material", new JsonObject
        {
            ["kind"] = SwapMaterialKinds.ManualZip,
            ["path"] = zip,
            ["sha256"] = sha ?? Sha(zip),
        });
        return (rig, zip);
    }

    /// <summary>프로그램 세 폴더가 손대지 않은 지금 판(1.3.48)인가 · <c>.rbk</c> 0.</summary>
    private static void AssertUntouched(LocalSwapWorkerRig rig, string where)
    {
        Assert.Equal(LocalSwapWorkerRig.From, LocalSwapWorkerRig.PartMark(rig.App, "api"));
        Assert.Equal(LocalSwapWorkerRig.From, LocalSwapWorkerRig.PartMark(rig.App, "watchdog"));
        foreach (var p in new[] { "api", "web", "watchdog" })
            Assert.False(Directory.Exists(Path.Combine(rig.App, p + ".rbk")), where + ": " + p + ".rbk 가 생겼다 / " + Why(rig));
    }

    // ── 대조 사본(봉합 줄을 끈 글자) — 찾는 글자가 정확히 한 번 있어야 한다 ──

    private static string Sub(string original, string line, string with, string what)
    {
        var n = 0;
        for (var i = original.IndexOf(line, StringComparison.Ordinal); i >= 0; i = original.IndexOf(line, i + line.Length, StringComparison.Ordinal)) n++;
        Assert.True(n == 1, what + " 줄이 " + n + "번 있다(1번이어야 한다) — 대조군을 같이 고쳐라");
        return original.Replace(line, with, StringComparison.Ordinal);
    }

    // 🔴 같은 줄에 본문(`{` 뒤)이 이어지므로 `#` 줄 주석 대신 `<# … #>` 블록 주석(10/2 실제 사고 — `#` 가 뒤 본문을 삼켰다).
    public static string ControlNoP1() => Sub(LocalSwapWorkerRig.OriginalScript(), P1Head, "<# (control) p1 off #> if ($false) {", "p1 rollback+manual_zip 허용");

    public static string ControlNoP2() => Sub(LocalSwapWorkerRig.OriginalScript(), P2Head, "<# (control) p2 off #> if ($false) {", "p2 rollback+manual_zip 해시 대조");

    public static string ControlNoP3() => Sub(LocalSwapWorkerRig.OriginalScript(), P3Head, "<# (control) p3 off #> if ($false) {", "p3 rollback+manual_zip 성공 뒤 zip 지움");

    // ── G-NW1 — 일꾼 허용(p1) ──

    [Fact(DisplayName = "N4 G-NW1 🚨 되돌리기 + manual_zip(manual\\staging · hitpan-{to}.zip · sha 64자) → S0 통과 · S1 재료 준비")]
    public void Nw1_allowed_request_passes_s0()
    {
        var (rig, _) = Make();
        using var _r = rig;
        Assert.Equal(0, rig.Run());
        var final = rig.Final();
        Assert.True(final.Reason != SwapReasons.RequestInvalid, "맞는 요청이 S0 에서 거부됐다 / " + final.State + "/" + final.Reason + " / " + Why(rig));
        Assert.True(rig.CountLog("S1 material ready") >= 1, "S1 재료 준비까지 안 갔다 / " + Why(rig));
    }

    [Theory(DisplayName = "N4 G-NW1 🚨 되돌리기 + manual_zip 하나씩 틀림(뿌리 밖 · 이름 판 다름 · sha 형식) → refused·request_invalid · 폴더 무접촉")]
    [InlineData("outside")]
    [InlineData("name")]
    [InlineData("sha")]
    public void Nw1_wrong_request_refused(string wrong)
    {
        var (rig, zip) = wrong switch
        {
            "outside" => MakeOutside(),
            "name" => Make(zipVersion: "1.3.46"),
            "sha" => Make(sha: "abc123"),
            _ => throw new ArgumentException(wrong),
        };
        using var _r = rig;
        Assert.Equal(0, rig.Run());
        var final = rig.Final();
        Assert.True(final.State == SwapStates.Refused && final.Reason == SwapReasons.RequestInvalid,
            wrong + ": " + final.State + "/" + final.Reason + " / " + Why(rig));
        AssertUntouched(rig, wrong);
        Assert.True(File.Exists(zip), wrong + ": 거부했는데 zip 이 사라졌다");
    }

    /// <summary>뿌리 밖 — 워치독 staging(<c>{root}\wdstaging</c>)에 같은 이름으로 둔 zip 을 <c>manual_zip</c> 이라고 적는다.</summary>
    private static (LocalSwapWorkerRig, string) MakeOutside()
    {
        var rig = LocalSwapWorkerRig.RollbackZip(DateTime.UtcNow);
        return Make2(rig, Path.Combine(rig.WdStaging, "hitpan-" + LocalSwapWorkerRig.To + ".zip"));
    }

    private static (LocalSwapWorkerRig, string) Make2(LocalSwapWorkerRig rig, string zip)
    {
        LocalSwapWorkerRig.MakeZip(zip, LocalSwapWorkerRig.To);
        rig.SetRequestField("material", new JsonObject
        {
            ["kind"] = SwapMaterialKinds.ManualZip,
            ["path"] = zip,
            ["sha256"] = Sha(zip),
        });
        return (rig, zip);
    }

    [Fact(DisplayName = "N4 G-NW1 대조군 🔴 p1 을 if ($false) 로 끈 사본 → 맞는 요청도 request_invalid(게이트가 FAIL 을 낸다)")]
    public void Nw1_control_p1_off_refuses_good_request()
    {
        var script = ControlNoP1();
        var (rig, _) = Make();
        using var _r = rig;
        Assert.Equal(0, rig.Run(script));
        var final = rig.Final();
        Assert.True(final.State == SwapStates.Refused && final.Reason == SwapReasons.RequestInvalid,
            "대조군인데 맞는 요청이 통과했다 — p1 이 아닌 다른 줄이 허용하고 있다 / " + final.State + "/" + final.Reason + " / " + Why(rig));
    }

    // ── G-NW2 — 일꾼 해시(p2) ──

    [Fact(DisplayName = "N4 G-NW2 🚨 sha 틀린 zip(64자 · 형식은 맞음) → refused·hash_mismatch · 프로그램 폴더·첨부 해시 그대로")]
    public void Nw2_hash_mismatch_refused_nothing_changed()
    {
        var (rig, zip) = Make(sha: new string('0', 64));
        using var _r = rig;
        var seed = rig.SeedCarry();
        Assert.Equal(0, rig.Run());
        var final = rig.Final();
        Assert.True(final.State == SwapStates.Refused && final.Reason == SwapReasons.HashMismatch,
            "끝 " + final.State + "/" + final.Reason + " / " + Why(rig));
        AssertUntouched(rig, "hash");
        var now = LocalSwapWorkerRig.CarryFiles(Path.Combine(rig.App, "api"));
        Assert.Equal(seed.Count, now.Count);
        foreach (var (rel, hash) in seed)
            Assert.True(now.TryGetValue(rel, out var h) && string.Equals(h, hash, StringComparison.OrdinalIgnoreCase), rel + " 가 바뀌었다");
        Assert.True(File.Exists(zip), "해시 거부 뒤 zip 은 일꾼이 지우지 않는다(정리는 API 다음 상태 확인 · X-5)");
    }

    [Fact(DisplayName = "N4 G-NW2 대조군 🔴 p2 를 if ($false) 로 끈 사본 → sha 틀린 zip 으로 교체가 진행된다(게이트가 FAIL 을 낸다)")]
    public void Nw2_control_p2_off_swaps_anyway()
    {
        var script = ControlNoP2();
        var (rig, _) = Make(sha: new string('0', 64));
        using var _r = rig;
        Assert.Equal(0, rig.Run(script));
        var final = rig.Final();
        Assert.True(final.Reason != SwapReasons.HashMismatch, "대조군인데 hash_mismatch — p2 아닌 다른 줄이 해시를 대고 있다 / " + Why(rig));
        Assert.True(rig.CountLog("S1 material ready") >= 1, "대조군인데 S1 재료 준비까지 안 갔다 / " + final.State + "/" + final.Reason + " / " + Why(rig));
    }

    // ── G-NW3 — 일꾼 정리(p3) ──

    [Fact(DisplayName = "N4 G-NW3 🚨 되돌리기 + manual_zip 성공 → success · 프로그램 = 1.3.47 · 받은 zip 0 · .rbk·stage 0 · 되돌림 표식")]
    public void Nw3_success_removes_zip()
    {
        var (rig, zip) = Make();
        using var _r = rig;
        var seed = rig.SeedCarry();
        Assert.Equal(0, rig.Run());
        var final = rig.Final();
        Assert.True(final.State == SwapStates.Success, "끝 " + final.State + "/" + final.Reason + " / " + Why(rig));
        Assert.Equal(LocalSwapWorkerRig.To, LocalSwapWorkerRig.PartMark(rig.App, "api"));
        Assert.Equal(LocalSwapWorkerRig.To, LocalSwapWorkerRig.PartMark(rig.App, "watchdog"));
        Assert.False(File.Exists(zip), "성공 뒤 받은 zip 이 남았다 / " + Why(rig));
        foreach (var p in new[] { "api", "web", "watchdog" })
            Assert.False(Directory.Exists(Path.Combine(rig.App, p + ".rbk")), p + ".rbk 가 남았다 / " + Why(rig));
        Assert.False(Directory.Exists(Path.Combine(rig.Work, "stage")), "stage 가 남았다");
        Assert.True(File.Exists(Path.Combine(rig.Work, "rolled-back.txt")), "되돌림 표식(연쇄 차단)이 없다");
        var now = LocalSwapWorkerRig.CarryFiles(Path.Combine(rig.App, "api"));
        Assert.Equal(seed.Count, now.Count);
    }

    [Fact(DisplayName = "N4 G-NW3 대조군 🔴 p3 을 if ($false) 로 끈 사본 → 성공인데 받은 zip 이 남는다(게이트가 FAIL 을 낸다)")]
    public void Nw3_control_p3_off_leaves_zip()
    {
        var script = ControlNoP3();
        var (rig, zip) = Make();
        using var _r = rig;
        Assert.Equal(0, rig.Run(script));
        Assert.True(rig.Final().State == SwapStates.Success, "대조군 끝 " + rig.Final().State + "/" + rig.Final().Reason + " / " + Why(rig));
        Assert.True(File.Exists(zip), "대조군인데 zip 이 지워졌다 — p3 아닌 다른 줄이 지우고 있다 / " + Why(rig));
    }
}
