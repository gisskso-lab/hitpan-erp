using HitPan.API.Services.LocalRollback;
using HitPan.API.Services.LocalSwap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Bench = HitPan.Tests.LocalSwap.LocalSwapPrevFetchGateTests.S21Bench;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// 🚨 20260930작1 확대 1.3.50 갈래 N5 — 병렬이슈 21 보강 게이트 G-NK7 · G-NK8(설계 §19-7 · 작업지시서 18-8 X-9).
/// 대역(<see cref="Bench"/>)은 같은 갈래의 <c>LocalSwapPrevFetchGateTests.Seal21.cs</c> 에 있다(이름 앞 S21).
/// </summary>
public sealed partial class LocalSwapManifestKeepGateTests
{
    // ── G-NK7 — 빈칸 메우기(R-21b) ──

    [Fact(DisplayName = "N5 G-NK7 🚨 이력 1.3.50 한 줄 · 지금 1.3.51 · Keep(1.3.51) → 이력 마지막 = 1.3.51 · 다시 불러도 줄 수 그대로 · Keep(1.3.52) 는 이력 무변화")]
    public void Nk7_fill_gap_idempotent()
    {
        using var b = new Bench("1.3.51", fill: true);
        b.WriteLedger("1.3.50");
        Assert.True(b.Keeper.Keep("1.3.51", b.S21Manifest("1.3.51")));
        Assert.Equal(new[] { "1.3.50", "1.3.51" }, b.LedgerVersions());

        var before = File.ReadAllText(b.Ledger);
        Assert.True(b.Keeper.Keep("1.3.51", b.S21Manifest("1.3.51")));
        Assert.Equal(before, File.ReadAllText(b.Ledger));                 // 멱등 — 줄 수·글자 그대로

        Assert.True(b.Keeper.Keep("1.3.52", b.S21Manifest("1.3.52")));    // 지금 판 아님(피드 최신 · 깔린 적 없음)
        Assert.Equal(before, File.ReadAllText(b.Ledger));
    }

    [Fact(DisplayName = "N5 G-NK7 🚨 운영 DI 등록(AddSingleton<SignedManifestKeeper>) 그대로 해석해도 메운다(인자 많은 생성자 선택 · Program.cs diff 0)")]
    public void Nk7_di_picks_filling_ctor()
    {
        using var b = new Bench("1.3.51", fill: true);
        b.WriteLedger("1.3.50");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILocalSwapEnvironment>(b.Env);
        services.AddSingleton<ISwapFolderGuard>(b.Guard);
        services.AddSingleton<SignedManifestKeeper>();                    // Program.cs:442 와 같은 등록
        using var sp = services.BuildServiceProvider();
        var keeper = sp.GetRequiredService<SignedManifestKeeper>();
        Assert.True(keeper.Keep("1.3.51", b.S21Manifest("1.3.51")));
        Assert.Equal(new[] { "1.3.50", "1.3.51" }, b.LedgerVersions());
    }

    [Fact(DisplayName = "N5 G-NK7 대조군 🔴 R-21b 끈 저장본 담당(기존 3인자 생성자) → 이력 마지막 = 1.3.50 그대로(게이트가 FAIL 을 낸다)")]
    public void Nk7_control_no_fill()
    {
        using var b = new Bench("1.3.51", fill: false);
        b.WriteLedger("1.3.50");
        Assert.True(b.Keeper.Keep("1.3.51", b.S21Manifest("1.3.51")));
        Assert.Equal(new[] { "1.3.50" }, b.LedgerVersions());
    }

    // ── G-NK8 — 되돌렸다 올라간 길(47 → 49 → 48 → 50) ──

    private static void S21Place(Bench b, string v)
    {
        var dir = SignedManifestKeeper.ManifestsDir(b.Env.AppRoot)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, v + ".json"), b.S21Manifest(v));
    }

    private static string[] S21Kept(Bench b)
    {
        var dir = SignedManifestKeeper.ManifestsDir(b.Env.AppRoot)!;
        return Directory.GetFiles(dir, "*.json").Select(Path.GetFileName).Select(n => n!).OrderBy(n => n, StringComparer.Ordinal).ToArray();
    }

    [Fact(DisplayName = "N5 G-NK8 🚨 이력 47,49,48,50 · 저장본 47·48·49 · 지금 50 · Keep(1.3.50) → 남은 아래 판 = 48 하나 · T5 판 = 48 · 재료 hitpan-1.3.48.zip")]
    public async Task Nk8_down_then_up_keeps_direct_previous()
    {
        using var b = new Bench("1.3.50", fill: true);
        b.WriteLedger("1.3.47", "1.3.49", "1.3.48", "1.3.50");
        foreach (var v in new[] { "1.3.47", "1.3.48", "1.3.49" }) S21Place(b, v);
        Assert.True(b.Keeper.Keep("1.3.50", b.S21Manifest("1.3.50")));
        Assert.Equal(new[] { "1.3.48.json", "1.3.50.json" }, S21Kept(b));
        Assert.Equal(new[] { "1.3.47", "1.3.49", "1.3.48", "1.3.50" }, b.LedgerVersions());   // 마지막 = 지금 판 ⇒ 메우기 무동작

        var (status, start, request) = await b.RollbackThroughFetch();
        Assert.True(status.CanRollback, status.Reason);
        Assert.Equal("1.3.48", status.TargetVersion);
        Assert.Equal(SwapMaterialKinds.ManualZip, status.MaterialKind);
        Assert.True(start is { Started: true }, "시작 거부 " + start?.Reason);
        Assert.Equal("https://feed.invalid/hitpan-1.3.48.zip", Assert.Single(b.Server.Urls));
        Assert.True(request is not null, "요청서가 안 적혔다");
        Assert.Equal("1.3.48", request!.To);
        Assert.Equal("hitpan-1.3.48.zip", Path.GetFileName(request.Material.Path));
    }

    [Fact(DisplayName = "N5 G-NK8 대조군 🔴 「아래 가장 높은 판」 규칙이 남길 상태(49 남음 · 48 지움) → no_previous_version(게이트가 FAIL 을 낸다)")]
    public void Nk8_control_highest_below_rule_loses_previous()
    {
        using var b = new Bench("1.3.50", fill: true);
        b.WriteLedger("1.3.47", "1.3.49", "1.3.48", "1.3.50");
        S21Place(b, "1.3.49");                                            // 틀린 규칙의 결과 — 48 은 지워졌고 49 만 남았다
        S21Place(b, "1.3.50");
        var status = b.Service.GetStatus("gate-user");
        Assert.False(status.CanRollback);
        Assert.Equal(SwapReasons.NoPreviousVersion, status.Reason);
        Assert.Empty(b.Server.Urls);
    }
}
