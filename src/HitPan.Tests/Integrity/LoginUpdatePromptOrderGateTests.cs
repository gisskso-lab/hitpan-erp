using System.Net;
using System.Text;
using HitPan.Web.Providers;
using HitPan.Web.Services;
using Microsoft.JSInterop;

namespace HitPan.Tests.Integrity
{
    /// <summary>
    /// 🔴 20261128작 로그인 경합 갈래 A 고침 1 (작업지시서 §8-2 A-②) — #43 「다음 로그인에 다시 묻는다」 순서.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 로그인 성공 길에서 업데이트 안내 기억(<c>sessionStorage</c> 의 <c>UpdatePromptPlan.SessionKey</c>)을 지우는 일이
    /// 인증 상태 방송<b>보다 먼저</b> 일어나야 한다 — 방송이 ERP 메인을 그리고, 그 첫 그림에서 업데이트 게이트가 이 칸을 읽는다.
    /// 뒤집히면 같은 탭에서 다시 로그인했을 때 Major 팝업이 안 뜰 수 있다.
    /// </para>
    /// <para>
    /// 제품 <c>AuthService.LoginAsync</c> 를 <b>실제로 불러</b> 잰다(소스 링크 · <c>AuthService.cs</c> 손 0).
    /// 가짜 응답(로그인 200) · 가짜 JS(부른 이름 기록) · 방송 대역(아래 <c>HitPanAuthStateProvider</c> — 부르면 기록)이
    /// <b>한 줄의 차례표</b>에 적는다. 소스 글자를 읽지 않는다.
    /// </para>
    /// <para>
    /// 대조군: 개발명세서 「고침 1」 절 V5(<c>AuthService.cs</c> 지움 ↔ 방송 두 줄 뒤집기) — 이 시험이 빨강이 됨을 확인했다.
    /// </para>
    /// </remarks>
    public sealed class LoginUpdatePromptOrderGateTests
    {
        private const string Broadcast = "broadcast";
        private const string ForgetUpdatePrompt = "sessionStorage.removeItem:" + UpdatePromptPlan.SessionKey;

        [Fact(DisplayName = "A-②a 로그인 성공 — 업데이트 안내 기억 지움이 방송보다 먼저 · 둘 다 정확히 1번")]
        public async Task LoginSuccess_ForgetsUpdatePromptBeforeBroadcast()
        {
            var calls = new List<string>();
            var service = NewService(calls, HttpStatusCode.OK,
                "{\"accessToken\":\"stand-in-access\",\"refreshToken\":\"stand-in-refresh\",\"userName\":\"시험\",\"tenantId\":\"t-stand-in\"}");

            var result = await service.LoginAsync("someone", "stand-in");

            Assert.True(result.Success);
            Assert.Equal(1, calls.Count(c => c == ForgetUpdatePrompt));
            Assert.Equal(1, calls.Count(c => c == Broadcast));

            var forgetAt = calls.IndexOf(ForgetUpdatePrompt);
            var broadcastAt = calls.IndexOf(Broadcast);
            Assert.True(forgetAt < broadcastAt,
                $"업데이트 안내 기억 지움({forgetAt})이 방송({broadcastAt})보다 늦다 — 차례표: {string.Join(" → ", calls)}");
        }

        [Fact(DisplayName = "A-②b 로그인 실패(401) — 지움 0 · 방송 0 (실패 길은 기억을 건드리지 않는다)")]
        public async Task LoginFailure_NeitherForgetsNorBroadcasts()
        {
            var calls = new List<string>();
            var service = NewService(calls, HttpStatusCode.Unauthorized, "{\"message\":\"아이디 또는 비밀번호가 틀립니다\"}");

            var result = await service.LoginAsync("someone", "stand-in");

            Assert.False(result.Success);
            Assert.DoesNotContain(ForgetUpdatePrompt, calls);
            Assert.DoesNotContain(Broadcast, calls);
        }

        private static AuthService NewService(List<string> calls, HttpStatusCode status, string body)
        {
            var js = new RecordingJs(calls);
            var http = new HttpClient(new FixedLoginHandler(status, body)) { BaseAddress = new Uri("http://stand-in.local/") };
            return new AuthService(
                http,
                new HitPanProtectedLocalStorage(js),
                new HitPanAuthStateProvider(calls),
                new NoRefresh(),
                js);
        }

        private sealed class RecordingJs(List<string> calls) : IJSRuntime
        {
            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
                InvokeAsync<TValue>(identifier, CancellationToken.None, args);

            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            {
                var first = args is { Length: > 0 } ? args[0]?.ToString() : null;
                calls.Add(first is null ? identifier : identifier + ":" + first);
                return ValueTask.FromResult(default(TValue)!);
            }
        }

        private sealed class FixedLoginHandler(HttpStatusCode status, string body) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
        }

        private sealed class NoRefresh : IAuthTokenRefresher
        {
            public Task<bool> TryRefreshAsync(CancellationToken ct = default) => Task.FromResult(false);
        }
    }
}

namespace HitPan.Web.Providers
{
    /// <summary>
    /// 🔴 시험 대역 — 제품 <c>HitPanAuthStateProvider</c> 자리에 들어가 「방송」 을 부른 차례만 적는다.
    /// </summary>
    /// <remarks>
    /// 진짜 형은 MudBlazor(<c>ISnackbar</c>) · <c>HitPanApiAuthHandler</c> 를 끌어와 시험 프로젝트에 링크하지 않는다.
    /// <c>AuthService</c> 가 이 형에서 부르는 것은 <c>NotifySessionChangedAsync</c> 하나뿐이다(로그인 · 갱신 · 로그아웃).
    /// HitPan.Tests 는 HitPan.Web 을 ProjectReference 하지 않는다 ⇒ 같은 이름 형이 둘이 되지 않는다(CS0436 0).
    /// </remarks>
    public sealed class HitPanAuthStateProvider(List<string> calls)
    {
        public Task NotifySessionChangedAsync()
        {
            calls.Add("broadcast");
            return Task.CompletedTask;
        }
    }
}
