using System.Reflection;
using HitPan.API.Services.ManualUpdate;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// 🚨 20260930작1 확대 1.3.50 갈래 N4 — 「지금 판의 서명 manifest 를 PC 에 남긴다」 게이트 G-NK1~5
/// (설계 §19-1 조각 A · §19-2 · §19-3 계약 · 작업지시서 §18).
/// </summary>
/// <remarks>
/// <para>이 파일 = <b>계약 이름 확인</b>(설계 19-3 · 19-1 조각 A 파일 목록). 갈래 N1 이 그 타입을 올리기 전에는 빨강이다
/// — 「이름 먼저 등록 → 전부 빨강 → 구현 따라 초록」(작업지시서 18-1). 타입을 컴파일 시점에 부르면 N1 전에는 빌드가 깨지므로
/// 여기서는 리플렉션으로 이름·모양만 잰다.</para>
/// <para>⏳ G-NK1~5 동작 시험(가짜 <c>IHttpClientFactory</c> 처리기 · 시험 키 서명 · 대조 사본)은 N1 의
/// <c>IPreviousPackageFeed.cs</c> 가 올라온 뒤 같은 이름의 partial 파일(<c>LocalSwapManifestKeepGateTests.Behavior.cs</c>)로 붙인다(개발명세서 N4 §3).</para>
/// </remarks>
public sealed partial class LocalSwapManifestKeepGateTests
{
    private static readonly Assembly Api = typeof(WatchdogUpdateCoreAdapter).Assembly;

    private static Type? Named(string name) => Api.GetTypes().SingleOrDefault(t => t.Name == name);

    [Fact(DisplayName = "N4 G-NK 계약 — IPreviousPackageFeed { FeedPackage? LoadVerified(string version) } 가 있다(설계 19-3)")]
    public void Contract_previous_package_feed()
    {
        var t = Named("IPreviousPackageFeed");
        Assert.True(t is not null, "IPreviousPackageFeed 가 없다 — 갈래 N1 대기");
        Assert.True(t!.IsInterface, "IPreviousPackageFeed 가 인터페이스가 아니다");
        var m = t.GetMethod("LoadVerified", new[] { typeof(string) });
        Assert.True(m is not null, "LoadVerified(string) 가 없다");
        Assert.Equal(typeof(FeedPackage), m!.ReturnType);
    }

    [Fact(DisplayName = "N4 G-NK 계약 — 어댑터(WatchdogUpdateCoreAdapter)가 IPreviousPackageFeed 를 구현한다(설계 19-1 조각 A — 재검증은 어댑터 안)")]
    public void Contract_adapter_implements_feed()
    {
        var t = Named("IPreviousPackageFeed");
        Assert.True(t is not null, "IPreviousPackageFeed 가 없다 — 갈래 N1 대기");
        Assert.True(t!.IsAssignableFrom(typeof(WatchdogUpdateCoreAdapter)), "어댑터가 IPreviousPackageFeed 를 구현하지 않는다");
    }

    [Fact(DisplayName = "N4 G-NK 계약 — SignedManifestKeeper(파일 읽기·쓰기·보관 규칙)가 있다")]
    public void Contract_keeper()
    {
        Assert.True(Named("SignedManifestKeeper") is not null, "SignedManifestKeeper 가 없다 — 갈래 N1 대기");
    }

    [Fact(DisplayName = "N4 G-NK5 계약 — SignedManifestStartupCheck 가 IHostedService 다(X-2 (나) 승인 · 기동 1회)")]
    public void Contract_startup_check()
    {
        var t = Named("SignedManifestStartupCheck");
        Assert.True(t is not null, "SignedManifestStartupCheck 가 없다 — 갈래 N1 대기");
        Assert.True(typeof(IHostedService).IsAssignableFrom(t), "SignedManifestStartupCheck 가 IHostedService 가 아니다");
    }
}
