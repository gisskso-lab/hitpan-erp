using HitPan.Web.Services;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-33 ~ G-36</b> — 메인PC 식별이 <b>브라우저 로컬권한</b>에 걸려 끊기던 것의 봉합을 지킨다.
/// 20260925작1 절D · 설계 <c>§6</c>.
/// </summary>
/// <remarks>
/// <para>
/// [무엇을 봉합했나] 브라우저가 <i>"이 사이트가 당신 컴퓨터 안을 봐도 됩니까"</i> 를 묻는 동안
/// 앱이 <b>1500ms 에 포기</b>해 버렸다. 고객이 [허용] 을 눌러도 그 왕복은 이미 죽어 있어
/// <b>새로고침해야만</b> 자료관리가 열렸다 (선행검증 2026-09-25).
/// </para>
/// <para>
/// ⚠️ <b>여기가 못 재는 것을 먼저 적는다</b> — 브라우저 권한 정책 자체는 CI 러너에서 잴 수 없다.
/// 실물 브라우저가 없고, 있더라도 <b>헤드리스로 재면 결과가 뒤집힌다.</b>
/// 그 축은 <b>실물 실측</b>으로만 닫힌다 (설계 §8 M-1~M-3).
/// ⇒ 이 시험들이 지키는 것은 <b>되돌림(회귀)</b> 이지, "고객 PC 에서 열린다" 가 아니다.
/// </para>
/// </remarks>
public sealed class MainPcLocalPermissionGateTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.True(dir is not null && Directory.Exists(Path.Combine(dir, "src")),
            "레포 루트를 찾아야 한다");
        return dir!;
    }

    private static string ReadSource(params string[] parts)
    {
        var path = Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"파일이 있어야 한다: {path}");
        return File.ReadAllText(path);
    }

    private static string ProofJs() => ReadSource(
        "src", "HitPan.Web", "wwwroot", "js", "hitpan-mainpc-proof.js");

    private static string MainPcOnlyRazor() => ReadSource(
        "src", "HitPan.Web", "Components", "Common", "MainPcOnly.razor");

    private static string MainPcGateRazor() => ReadSource(
        "src", "HitPan.Web", "Components", "Common", "MainPcGate.razor");

    // ══════════════════════════════════════════════════════════════════
    // G-33 — 시간제한이 1500ms 로 되돌아가지 않았다
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>1500ms 로 되돌리면 이 시험이 FAIL 한다.</b>
    /// </summary>
    /// <remarks>
    /// [왜 되돌아갈 위험이 있나] 종전 주석에 <i>"길게 잡으면 히트판이 없는 컴퓨터에서
    /// 로그인이 그만큼 느려진다"</i> 고 <b>사실처럼</b> 적혀 있었다. 그 말을 믿으면 누구든 되돌린다.
    /// 실측은 정반대다 — 히트판이 없는 자리는 <b>연결 거부로 1ms 에 끝난다.</b>
    /// </remarks>
    [Fact]
    public void G33_왕복_시간제한이_1500ms_로_되돌아가지_않았다()
    {
        var js = ProofJs();

        var m = System.Text.RegularExpressions.Regex.Match(js, @"TIMEOUT_MS\s*=\s*(\d+)");
        Assert.True(m.Success, "TIMEOUT_MS 를 찾아야 한다 — 이름이 바뀌었으면 이 시험도 함께 고쳐라");

        var ms = int.Parse(m.Groups[1].Value);

        Assert.True(ms >= 5000,
            $"왕복 시간제한이 {ms}ms 다. 브라우저 권한 팝업을 사람이 읽고 누르기에 너무 짧다. " +
            "이것이 짧아서 [허용] 을 눌러도 안 열리던 것이 2026-09-25 의 사고다. " +
            "늘려도 잃는 것이 없다 — 히트판 없는 PC 는 1ms 에 끝난다(연결 거부).");

        // 🔴 무한정 기다리지도 않는다 — 사람이 팝업을 무시하면 회전자가 영원히 돈다.
        Assert.True(ms <= 30000,
            $"왕복 시간제한이 {ms}ms 다. 너무 길면 팝업을 무시한 고객의 화면이 그만큼 멈춘 것처럼 보인다.");
    }

    // ══════════════════════════════════════════════════════════════════
    // G-34 — 왕복 규칙이 한 곳에만 있다 (복붙 금지)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>왕복 3콜을 어디든 복붙하면 FAIL 한다.</b>
    /// </summary>
    /// <remarks>
    /// [왜] <c>MainPcOnlyAttribute.cs</c> 와 <c>DeviceController.cs</c> 가 이미 같은 경고를 달고 있다 —
    /// <i>"규칙을 복붙하면 한쪽만 고쳐지는 날이 온다."</i> 실제로 그 일이 한 번 났다(20260922작2 절D).
    /// 이제 화면 두 곳(<c>MainPcGate</c>·<c>MainPcOnly</c>)이 같은 왕복을 쓴다 —
    /// <b>한 곳에 두지 않으면 반드시 어긋난다.</b>
    /// </remarks>
    [Fact]
    public void G34_왕복_3콜이_공용_한곳에만_있다()
    {
        // 표를 받는 자리(①)를 기준으로 센다 — 왕복을 복붙하면 이것이 함께 따라온다.
        const string issue = "mainpc-challenge";

        var runner = ReadSource("src", "HitPan.Web", "Services", "MainPcProofRunner.cs");
        Assert.Contains(issue, runner);
        Assert.Contains("mainpc-verify", runner);
        Assert.Contains("hitpanMainPc.probe", runner);

        // 🔴 자료관리 가드는 왕복을 **직접 들고 있으면 안 된다.** 공용을 불러야 한다.
        var only = MainPcOnlyRazor();
        Assert.False(only.Contains(issue, StringComparison.Ordinal),
            "MainPcOnly.razor 가 왕복을 직접 들고 있다. 복붙하지 말고 MainPcProofRunner 를 불러라 — " +
            "두 곳이 각자 들고 있으면 한쪽만 고쳐지는 날이 온다.");
        Assert.Contains("Proof.RunAsync", only);

        // 🔴 MainPcGate 도 마찬가지 — 등록(②)은 자기 일이지만 **왕복 본문**은 공용 것을 쓴다.
        var gate = MainPcGateRazor();
        Assert.False(
            gate.Contains("mainpc-verify", StringComparison.Ordinal),
            "MainPcGate.razor 에 왕복 본문이 되살아났다(mainpc-verify). " +
            "20260925작1 절B 에서 MainPcProofRunner 로 옮긴 것이다 — 복붙하지 마라.");
        Assert.Contains("Proof.RunAsync", gate);
    }

    // ══════════════════════════════════════════════════════════════════
    // G-35 — 자료관리 진입에서 한 번 더 돈다 (그러나 딱 한 번)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>재시도를 빼면 FAIL 한다.</b> 그리고 <b>고리가 되면 안 된다.</b>
    /// </summary>
    /// <remarks>
    /// ⚠️ 이 시험은 <b>구조</b>를 본다 — 재시도가 <c>ask</c> 안에 있고, 판정이
    /// <c>MainPcConfirmed</c> 일 때만 다시 묻는지. 실제 브라우저에서 열리는지는
    /// <b>여기서 못 잰다</b>(설계 §8 M-1·M-2).
    /// </remarks>
    [Fact]
    public void G35_자료관리_진입에서_왕복을_한번만_더_돈다()
    {
        var only = MainPcOnlyRazor();

        // 한 번 더 도는가
        Assert.Contains("Proof.RunAsync", only);

        // 🔴 통과했을 때만 다시 묻는다 — 아무 판정에나 다시 물으면 소음이 된다.
        Assert.Contains("MainPcConfirmed", only);

        // 🔴 **정확히 한 번**. 두 번 이상 부르면 고리가 될 수 있다.
        var calls = System.Text.RegularExpressions.Regex.Matches(only, @"Proof\.RunAsync\s*\(").Count;
        Assert.True(calls == 1,
            $"MainPcOnly.razor 가 왕복을 {calls}번 부른다. **한 번만** 돌아야 한다 — " +
            "두 번째 실패는 그대로 실패로 두어야 고리가 돌지 않는다.");

        // 🔴 루프 안에서 돌리지 않는다.
        Assert.False(
            System.Text.RegularExpressions.Regex.IsMatch(only, @"(while|for)\s*\([^)]*\)[^;]*Proof\.RunAsync"),
            "왕복을 반복문 안에서 돌리고 있다 — 고리가 된다.");
    }

    // ══════════════════════════════════════════════════════════════════
    // G-36 — 재시도를 넣어도 G-27(1회 규칙)이 안 깨진다  ← 동작으로 잰다
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>동작 시험.</b> <c>ask</c> 안에서 왕복을 더 돌아도
    /// <c>ResolveAsync</c> 의 계약(<b>막혔을 때 0회 · 통과했을 때 정확히 1회</b>)은 그대로여야 한다.
    /// </summary>
    /// <remarks>
    /// 재시도를 <c>ResolveAsync</c> <b>바깥</b>에 넣었다면 이 계약이 깨진다.
    /// 그래서 절C 는 <c>ask</c> <b>안에서만</b> 고치게 돼 있다.
    /// </remarks>
    [Theory]
    [InlineData(false, 0)]  // 끝내 아니면 — 화면은 안 열린다
    [InlineData(true, 1)]   // 재시도 끝에 통과하면 — 정확히 한 번 열린다
    public async Task G36_재시도가_있어도_통과시_정확히_한번만_연다(bool eventuallyMainPc, int expectedOpens)
    {
        var state = new MainPcAccessState();
        var proofRuns = 0;
        var opened = 0;

        // MainPcOnly 의 ask 와 **같은 모양** — 먼저 묻고, 아니면 왕복 한 번, 통과하면 다시 묻는다.
        async Task<bool> Ask()
        {
            // ① 첫 물음 — 여기서는 늘 "아니다"(권한 팝업에 걸려 출입증이 없는 상황)
            await Task.Yield();

            proofRuns++;                      // ② 왕복 한 번
            return eventuallyMainPc;          // ③ 다시 물음
        }

        await state.ResolveAsync(
            Ask,
            () => { opened++; return Task.CompletedTask; },
            _ => { });

        Assert.Equal(1, proofRuns);                       // 딱 한 번만 돌았다
        Assert.Equal(expectedOpens, opened);              // G-27 계약 무회귀
        Assert.Equal(eventuallyMainPc, state.IsMainPc);
        Assert.False(state.Checking);

        // 🔴 두 번 불러도 두 번 열지 않는다 — 재렌더링 무회귀.
        await state.ResolveAsync(Ask, () => { opened++; return Task.CompletedTask; }, _ => { });
        Assert.Equal(1, proofRuns);
        Assert.Equal(expectedOpens, opened);
    }
}
