using HitPan.API.Services.Cooltime;
using HitPan.Application.Services;
using HitPan.Web.Services;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 쿨타임(설정 시각 자동 로그아웃) — <b>DB 없는</b> 게이트(순수 규칙을 실제로 불러 값을 본다).
/// </summary>
/// <remarks>
/// 근거: 설계 <c>docs/설계/erp/쿨타임_자동로그아웃_설계_20260928.md</c> §7 · §D · §E ·
/// 개발명세서 <c>docs/개발/erp/20261127_쿨타임1b_갈래3_게이트_개발명세서.md</c>.
/// <para>DB 를 쓰는 게이트는 <see cref="CooltimeDbGateTests"/> 에 있다(CI <c>db-gate</c> 잡이 유일한 계측 경로).</para>
/// <para>🔴 화면 쪽 규칙은 <c>HitPan.Web/Services/CooltimeScreenRules.cs</c> 를 <b>소스 링크</b>해 부른다(시험 프로젝트 Compile Include ·
/// ApiErrorText 와 같은 방식). 글자를 세지 않고 함수에 값을 넣어 받은 값을 본다.</para>
/// </remarks>
public sealed class CooltimeRuleGateTests
{
    // ══════════════════════════════════════════════════════════════
    // G-C18 ⓐ (설계 §E E-1) — 푸시 연결 토큰은 부를 때마다 지금 것
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-C18 ⓐ 푸시 연결 토큰 — 두 번 부르는 사이 저장소 토큰이 바뀌면 두 번째는 새 토큰 · 대조(첫 토큰 붙잡기)는 옛 토큰을 준다")]
    public async Task G_C18a_토큰은_부를_때마다_지금_것()
    {
        var stored = "token-1";
        var refreshCalls = 0;
        Task<string?> Read() => Task.FromResult<string?>(stored);
        Task<bool> Refresh() { refreshCalls++; return Task.FromResult(true); }

        var first = await CooltimeScreenRules.ResolveHubTokenAsync(Read, Refresh, _ => false);
        stored = "token-2";                                   // 다른 탭의 갱신 · 로그인 바뀜
        var second = await CooltimeScreenRules.ResolveHubTokenAsync(Read, Refresh, _ => false);

        Assert.Equal("token-1", first);
        Assert.Equal("token-2", second);
        Assert.Equal(0, refreshCalls);                        // 만료 아님 = 갱신 길 안 탐

        // 🔴 음성 대조군 ⓔ(시험 안) — 옛 판(NotificationClient.cs 의 ⬛ 줄 「() => Task.FromResult(token)」)과 같은 꼴은
        //    같은 순서에서 두 번째도 첫 토큰을 준다 ⇒ 위 단언이 그 꼴을 실제로 가른다.
        stored = "token-1";
        var pinned = await Read();
        Func<Task<string?>> oldProvider = () => Task.FromResult<string?>(pinned);
        stored = "token-2";
        Assert.NotEqual(second, await oldProvider());
    }

    [Fact(DisplayName = "G-C18 ⓐ 곧 만료면 갱신 길을 타고 결과와 무관하게 저장소를 다시 읽는다 · 로그인 없으면 null")]
    public async Task G_C18a_만료면_갱신_뒤_다시_읽는다()
    {
        var stored = "old";
        var refreshed = 0;
        Task<string?> Read() => Task.FromResult<string?>(stored);
        Task<bool> RefreshOk() { refreshed++; stored = "new"; return Task.FromResult(true); }
        Task<bool> RefreshFailButOtherTab() { refreshed++; stored = "other-tab"; return Task.FromResult(false); }

        Assert.Equal("new", await CooltimeScreenRules.ResolveHubTokenAsync(Read, RefreshOk, t => t == "old"));
        stored = "old";
        Assert.Equal("other-tab", await CooltimeScreenRules.ResolveHubTokenAsync(Read, RefreshFailButOtherTab, t => t == "old"));
        Assert.Equal(2, refreshed);

        stored = "";
        Assert.Null(await CooltimeScreenRules.ResolveHubTokenAsync(Read, RefreshOk, _ => true));
    }

    /// <summary>
    /// G-C18 ⓐ 배선 — 위 함수가 <b>실제 연결 설정에 물려 있는지</b>. 이것만 글자 확인이다(WASM 연결 객체는 시험이 띄울 수 없다).
    /// 동작은 위 두 시험이 잰다. 음성 대조 ⓔ = <c>NotificationClient.cs</c> 의 공급자를 첫 토큰 붙잡기로 되돌리면 이 시험이 FAIL.
    /// </summary>
    [Fact(DisplayName = "G-C18 ⓐ 배선 — NotificationClient 의 AccessTokenProvider = GetHubTokenAsync → ResolveHubTokenAsync (살아 있는 줄 기준)")]
    public void G_C18a_배선()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "src", "HitPan.Web", "Services", "NotificationClient.cs"));
        var live = string.Join('\n', src.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        Assert.Contains("options.AccessTokenProvider = GetHubTokenAsync;", live);
        Assert.Contains("CooltimeScreenRules.ResolveHubTokenAsync(", live);
        Assert.DoesNotContain("AccessTokenProvider = () => Task.FromResult", live);
    }

    // ══════════════════════════════════════════════════════════════
    // G-C14 화면 짝 (설계 §E E-5) — 401 · 409 · ended:false = 끝남
    // ══════════════════════════════════════════════════════════════

    [Theory(DisplayName = "G-C14 화면 짝 — [계속] 응답: 200 계속 · 401/409 끝남 · 5xx/연결실패 실패")]
    [InlineData(200, CooltimeScreenRules.CallOutcome.Continued)]
    [InlineData(401, CooltimeScreenRules.CallOutcome.Ended)]
    [InlineData(409, CooltimeScreenRules.CallOutcome.Ended)]
    [InlineData(500, CooltimeScreenRules.CallOutcome.Failed)]
    [InlineData(503, CooltimeScreenRules.CallOutcome.Failed)]
    [InlineData(null, CooltimeScreenRules.CallOutcome.Failed)]
    public void G_C14_화면_계속_응답(int? status, CooltimeScreenRules.CallOutcome expected)
        => Assert.Equal(expected, CooltimeScreenRules.ContinueOutcome(status));

    [Theory(DisplayName = "G-C14 화면 짝 — [로그아웃] 응답: 2xx(ended true/false)·401·409 끝남 · 503 만 한 번 더")]
    [InlineData(200, CooltimeScreenRules.CallOutcome.Ended, false)]
    [InlineData(401, CooltimeScreenRules.CallOutcome.Ended, false)]
    [InlineData(409, CooltimeScreenRules.CallOutcome.Ended, false)]
    [InlineData(503, CooltimeScreenRules.CallOutcome.Failed, true)]
    [InlineData(500, CooltimeScreenRules.CallOutcome.Failed, false)]
    [InlineData(null, CooltimeScreenRules.CallOutcome.Failed, false)]
    public void G_C14_화면_로그아웃_응답(int? status, CooltimeScreenRules.CallOutcome expected, bool retry)
    {
        Assert.Equal(expected, CooltimeScreenRules.LogoutOutcome(status));
        Assert.Equal(retry, CooltimeScreenRules.ShouldRetryLogout(status));
    }

    // ══════════════════════════════════════════════════════════════
    // G-C15 화면 몫 · C-5 대상 (설계 §D-2 안 A)
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-C15 화면 몫 — cooltime_blank = 「자동 로그아웃 · 퇴근 빈칸」 · cooltime = 「자동」 · edited = 「고침」 · 사람 = 표시 없음")]
    public void G_C15_표시_낱말()
    {
        Assert.Equal("자동 로그아웃 · 퇴근 빈칸", CooltimeScreenRules.SourceLabel("cooltime_blank"));
        Assert.Equal("자동", CooltimeScreenRules.SourceLabel("cooltime"));
        Assert.Equal("고침", CooltimeScreenRules.SourceLabel("edited"));
        Assert.Null(CooltimeScreenRules.SourceLabel(null));
    }

    [Fact(DisplayName = "C-5 고치기 대상 — 화면(IsEditableSource) = 서버(HrService.EditableCheckOutSources) = 엔진 값(CooltimeWire)")]
    public void C5_고치기_대상_화면_서버_같다()
    {
        Assert.Equal(new[] { CooltimeWire.SourceCooltime, CooltimeWire.SourceCooltimeBlank }.OrderBy(x => x),
                     HrService.EditableCheckOutSources.OrderBy(x => x));
        foreach (var s in new[] { "cooltime", "cooltime_blank", "edited", null, "" })
            Assert.Equal(s is not null && HrService.EditableCheckOutSources.Contains(s), CooltimeScreenRules.IsEditableSource(s));
    }

    [Fact(DisplayName = "주소·이벤트 대조 — 화면 약속(CooltimeScreenRules) = 서버 약속(CooltimeWire · CooltimeController 경로)")]
    public void 주소_이벤트_대조()
    {
        Assert.Equal(CooltimeWire.PromptEvent, CooltimeScreenRules.PromptEvent);
        Assert.Equal(CooltimeWire.EndedEvent, CooltimeScreenRules.EndedEvent);
        Assert.Equal(CooltimeWire.PromptSeconds, CooltimeScreenRules.PromptSeconds);

        // 컨트롤러 경로는 특성에서 읽는다(글자 복사 아님 — 반사로 실제 값을 꺼낸다)
        var route = (Microsoft.AspNetCore.Mvc.RouteAttribute)Attribute.GetCustomAttribute(
            typeof(HitPan.API.Controllers.CooltimeController), typeof(Microsoft.AspNetCore.Mvc.RouteAttribute))!;
        string RoutePath(string action)
        {
            var m = typeof(HitPan.API.Controllers.CooltimeController).GetMethod(action)!;
            var t = m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute), false)
                     .Cast<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Single().Template;
            return route.Template + "/" + t;
        }
        Assert.Equal(CooltimeScreenRules.ContinuePath, RoutePath("Continue"));
        Assert.Equal(CooltimeScreenRules.LogoutPath, RoutePath("Logout"));
        Assert.Equal(CooltimeScreenRules.StatePath, RoutePath("State"));
    }

    // ══════════════════════════════════════════════════════════════
    // 엔진 순수 규칙 — G-C8 · G-C9 · G-C11 · G-C19 의 시각 판정 몸통
    // ══════════════════════════════════════════════════════════════

    [Theory(DisplayName = "설정 시각 C = 가장 최근에 지난 그 시각(오늘 지났으면 오늘 · 아니면 어제)")]
    [InlineData("2026-11-27 22:00:15", "22:00", "2026-11-27 22:00:00")]
    [InlineData("2026-11-27 21:59:59", "22:00", "2026-11-26 22:00:00")]
    [InlineData("2026-11-28 08:00:00", "22:00", "2026-11-27 22:00:00")]   // G-C11 서버 켜짐 다음 날 08:00
    [InlineData("2026-11-28 02:00:00", "02:00", "2026-11-28 02:00:00")]   // 경계 = 지남
    public void C_가장_최근_설정시각(string now, string at, string expected)
        => Assert.Equal(DateTime.Parse(expected),
            CooltimeEngine.MostRecentCooltimeLocal(DateTime.Parse(now), TimeSpan.Parse(at)));

    [Theory(DisplayName = "정시 퇴근 = 출근 뒤 처음 오는 그 시각 — 주간 같은 날 · 야간 다음 날 · 19:00 출근(18:00)은 다음 날")]
    [InlineData("2026-11-27 09:00", "18:00", "2026-11-27 18:00")]   // G-C8
    [InlineData("2026-11-27 22:00", "06:00", "2026-11-28 06:00")]   // 야간
    [InlineData("2026-11-27 19:00", "18:00", "2026-11-28 18:00")]   // G-C15 의 「아직 안 온 시각」
    public void 정시퇴근_첫_시각(string checkIn, string leave, string expected)
        => Assert.Equal(DateTime.Parse(expected),
            CooltimeEngine.FirstRegularLeaveAfter(DateTime.Parse(checkIn), TimeSpan.Parse(leave)));

    [Fact(DisplayName = "근무 시간 = 퇴근 − 출근(소수 1자리) — 09:00 → 18:00 = 9.0")]
    public void 근무시간()
    {
        Assert.Equal(9.0, CooltimeEngine.WorkHours(DateTime.Parse("2026-11-27 09:00"), DateTime.Parse("2026-11-27 18:00")));
        Assert.Equal(9.7, CooltimeEngine.WorkHours(DateTime.Parse("2026-11-27 09:00"), DateTime.Parse("2026-11-27 18:40")));
    }

    [Fact(DisplayName = "G-C19 규칙(E-2) — C ≤ 마지막 켬·시각 바꿈이면 건너뜀 · 기록 없음(NULL)이면 안 건너뜀")]
    public void G_C19_건너뛰기_규칙()
    {
        var c = DateTime.Parse("2026-11-27 04:00:00");
        Assert.True(CooltimeEngine.IsBeforeSettingChange(c, DateTime.Parse("2026-11-27 05:00:00")));
        Assert.True(CooltimeEngine.IsBeforeSettingChange(c, c));
        Assert.False(CooltimeEngine.IsBeforeSettingChange(c, DateTime.Parse("2026-11-27 03:59:59")));
        Assert.False(CooltimeEngine.IsBeforeSettingChange(c, null));
    }

    [Fact(DisplayName = "서버 로컬 ↔ UTC 왕복이 같은 값을 돌려준다(엔진 C 변환 · 근태 기준 순간)")]
    public void 로컬_UTC_왕복()
    {
        var local = DateTime.Parse("2026-11-27 22:00:00");
        Assert.Equal(local, CooltimeEngine.UtcToServerLocal(CooltimeEngine.ServerLocalToUtc(local)));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다.");
    }
}
