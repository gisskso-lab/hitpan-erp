using System.Reflection;
using HitPan.API.Services.LocalRollback;
using Xunit;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// 🚨 20260930작1 확대 1.3.50 갈래 N4 — 「①·② 둘 다 없을 때만 NCP 에서 받는 세 번째 길」 게이트 G-NP1~7
/// (설계 §19-1 조각 B · §19-2 · §19-3 계약 · 작업지시서 §18).
/// </summary>
/// <remarks>
/// <para>이 파일 = <b>계약 이름 확인</b>(설계 19-3 · 19-1 조각 B 파일 목록). 갈래 N1(<c>IPreviousPackageFeed</c>)·N2
/// (<c>PreviousPackageFetch</c> · <c>LocalRollbackStatus.Fetch</c> · 덧붙인 생성자)가 올라오기 전에는 빨강이다(작업지시서 18-1).</para>
/// <para>⏳ G-NP1~7 동작 시험(가짜 파일 서버 처리기 · 「바깥 주소로 나간 요청 0」 단언 · 대조 사본)은 N2 가 올라온 뒤
/// 같은 이름의 partial 파일(<c>LocalSwapPrevFetchGateTests.Behavior.cs</c>)로 붙인다(개발명세서 N4 §3).</para>
/// </remarks>
public sealed partial class LocalSwapPrevFetchGateTests
{
    private static readonly Assembly Api = typeof(LocalRollbackService).Assembly;

    private static Type? Named(string name) => Api.GetTypes().SingleOrDefault(t => t.Name == name);

    [Fact(DisplayName = "N4 G-NP 계약 — PreviousPackageFetch(받기 일)가 있다")]
    public void Contract_fetch_job()
    {
        Assert.True(Named("PreviousPackageFetch") is not null, "PreviousPackageFetch 가 없다 — 갈래 N2 대기");
    }

    [Fact(DisplayName = "N4 G-NP 계약 — LocalRollbackStatus 끝 선택 칸 Fetch(기본 null · 설계 19-3)")]
    public void Contract_status_fetch()
    {
        var p = typeof(LocalRollbackStatus).GetProperty("Fetch");
        Assert.True(p is not null, "LocalRollbackStatus.Fetch 가 없다 — 갈래 N2 대기");
        Assert.True(!p!.PropertyType.IsValueType || Nullable.GetUnderlyingType(p.PropertyType) is not null, "Fetch 가 null 을 못 담는다(선택 칸이어야 한다)");
    }

    [Fact(DisplayName = "N4 G-NP 계약 — LocalRollbackService 에 IPreviousPackageFeed 를 받는 생성자가 덧붙었다(기존 생성자 유지)")]
    public void Contract_service_ctor()
    {
        var feed = Named("IPreviousPackageFeed");
        Assert.True(feed is not null, "IPreviousPackageFeed 가 없다 — 갈래 N1 대기");
        var ctors = typeof(LocalRollbackService).GetConstructors();
        Assert.True(ctors.Length >= 2, "생성자가 " + ctors.Length + "개 — 기존 생성자 유지 + 하나 덧붙임이어야 한다");
        Assert.Contains(ctors, c => c.GetParameters().Any(x => x.ParameterType == feed));
        Assert.Contains(ctors, c => c.GetParameters().All(x => x.ParameterType != feed));
    }
}
