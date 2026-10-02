using System.Net;
using HitPan.API.Services.LocalSwap;
using HitPan.API.Services.ManualUpdate;
using Xunit;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// 🚨 20260930작1 확대 1.3.50 갈래 N7 — 작업지시서 18-10 「고친다」 (ㄱ) 병렬이슈 24 · (ㄴ) [4] F-2.
/// </summary>
/// <remarks>
/// <para>🟢 NCP 실접속 0 — 모든 요청은 <see cref="RecordingHandler"/> 가 손에 쥔다. 서명 = N4b 시험 키(<see cref="N4bTestKey"/> · 병렬 끈 컬렉션은
/// 이 partial 클래스의 다른 조각이 이미 붙였다 · db.conf finally 원상). 시험 한 판 = N6 의 <c>N6Bench</c> 재사용.</para>
/// <para>대조(Edit 도구로 제품을 바꿔 돌림 → 원복 · 개발명세서 N7 §4): (ㄱ) <c>KeepFeedManifestNotBelow</c> 의 <c>return;</c> 뺌 → 「1.3.48」 에서 저장·되돌리기 섬.
/// (ㄴ) ① <c>FirstPreviousOncePerVersion</c> 의 표지 검사 줄 뺌 → G-N7b 404·500 · G-N7c 에서 확인표 다시 물음
/// ② <c>CheckAsync</c> 의 받기 부름을 옛 줄(<c>await TryFetchFirstPrevious(…)</c>)로 되돌림 → G-N7b 다시 물음 · G-N7c 두 갈래 화면 확인이 기다림.</para>
/// </remarks>
public sealed partial class LocalSwapManifestKeepGateTests
{
    // ── N7 (ㄱ) G-N7a — 피드 확인이 남기는 서명본 = 설치 판 이상만 ──

    [Theory(DisplayName = "N7 G-N7a 🚨 첫 회(이력 1.3.50 한 줄) · 피드가 설치 판보다 낮은 서명본 1.3.48 → 남기지 않음 · 되돌리기 no_previous_version(그 판이 P 가 되지 않음) · 같은 판·높은 판은 그대로 남김")]
    [InlineData("1.3.48")] // 설치 판보다 낮음 — 남기지 않는다(병렬이슈 24)
    [InlineData("1.3.50")] // 설치 판 — 남긴다(기존)
    [InlineData("1.3.51")] // 설치 판보다 높음 — 남긴다(기존)
    public async Task N7a_feed_check_keeps_only_installed_or_above(string feedVersion)
    {
        using var b = new N6Bench();
        await N4bTestKey.WithKeyAsync(async () =>
        {
            b.WriteLedger(N6Current);
            var feed = N4bTestKey.Signed(b.Unsigned(feedVersion, "https://updates.hitpan.kr/packages/hitpan-" + feedVersion + ".zip"));
            b.Http.Handler.Respond = (req, _) => Task.FromResult(
                req.RequestUri!.AbsolutePath.EndsWith(".previous-manifest.json", StringComparison.Ordinal)
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : N6Json(feed));

            var r = await b.Adapter.CheckAsync(N6Current, CancellationToken.None);
            Assert.Equal(feedVersion == "1.3.51" ? FeedCheckStatus.Newer : FeedCheckStatus.NotNewer, r.Status);   // 확인 결과는 그대로
            var status = b.Service.GetStatus("gate-user");

            if (feedVersion == "1.3.48")
            {
                Assert.True(Kept(b.Env).Length == 0, "설치 판보다 낮은 피드 서명본이 남았다: " + string.Join(", ", Kept(b.Env)));
                Assert.False(status.CanRollback, "피드가 준 낮은 판이 되돌리기 대상이 됐다 — " + status.TargetVersion);
                Assert.Equal(SwapReasons.NoPreviousVersion, status.Reason);
                return;
            }
            Assert.Equal(new[] { feedVersion + ".json" }, Kept(b.Env));
        });
    }

    // ── N7 (ㄴ) G-N7b · G-N7c — 첫 회 확인표 받기 = 프로세스당·설치 판당 한 번 · 화면 확인은 기다리지 않음 ──

    private static int N7PrevRequests(N6Bench b)
    {
        lock (b.Http.Handler.Requests)
            return b.Http.Handler.Requests.Count(q => q.RequestUri!.AbsolutePath.EndsWith(".previous-manifest.json", StringComparison.Ordinal));
    }

    private static async Task<bool> N7WaitUntil(Func<bool> cond, TimeSpan limit)
    {
        var until = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < until)
        {
            if (cond()) return true;
            await Task.Delay(20);
        }
        return cond();
    }

    [Theory(DisplayName = "N7 G-N7b 🚨 첫 회 확인표 받기가 404·실패로 끝나도 같은 프로세스·같은 설치 판의 다음 확인은 다시 묻지 않음(확인표 GET 정확히 1) · 확인 결과는 그대로")]
    [InlineData("404")] // NCP 에 확인표 없음(1.3.51 이후 새 설치 PC — [4] F-2)
    [InlineData("500")] // 받기 실패
    public async Task N7b_first_previous_fetch_once_per_process_and_version(string which)
    {
        using var b = new N6Bench();
        await N4bTestKey.WithKeyAsync(async () =>
        {
            b.WriteLedger(N6Current);
            var feed = N4bTestKey.Signed(b.Unsigned(N6Current, N6FeedZipUrl));
            b.Http.Handler.Respond = (req, _) => Task.FromResult(
                req.RequestUri!.AbsolutePath.EndsWith(".previous-manifest.json", StringComparison.Ordinal)
                    ? new HttpResponseMessage(which == "404" ? HttpStatusCode.NotFound : HttpStatusCode.InternalServerError)
                    : N6Json(feed));

            var first = await b.Adapter.CheckAsync(N6Current, CancellationToken.None);   // 기동 확인 자리
            Assert.Equal(1, N7PrevRequests(b));
            var second = await b.Adapter.CheckAsync(N6Current, CancellationToken.None);  // 업데이트 화면 확인 자리
            var third = await b.Adapter.CheckAsync(N6Current, CancellationToken.None);
            Assert.Equal(FeedCheckStatus.NotNewer, first.Status);
            Assert.Equal(first, second);
            Assert.Equal(first, third);
            // 뒤에서 시작하는 길이 있다면 잠깐 기다려도 늘지 않아야 한다
            Assert.False(await N7WaitUntil(() => N7PrevRequests(b) > 1, TimeSpan.FromMilliseconds(500)),
                which + ": 같은 판 동안 확인표를 다시 물었다 — 확인표 GET " + N7PrevRequests(b) + "회");
            HttpRequestMessage[] reqs;
            lock (b.Http.Handler.Requests) reqs = b.Http.Handler.Requests.ToArray();
            Assert.Equal(4, reqs.Length);                                               // 피드 3 + 확인표 1
            Assert.Equal(new[] { N6Current + ".json" }, Kept(b.Env));
        });
    }

    [Theory(DisplayName = "N7 G-N7c 🚨 화면 확인은 첫 회 확인표 받기를 기다리지 않음 — ⓐ기동 확인이 받는 중이면 건너뜀(추가 GET 0) ⓑ기동 확인이 피드에 못 닿았으면 뒤에서 한 번 시작하고 곧바로 돌아옴 → 받기는 뒤에서 끝나 1.3.49 저장")]
    [InlineData("inflight")]
    [InlineData("startup-missed")]
    public async Task N7c_screen_check_does_not_wait_for_first_previous_fetch(string which)
    {
        using var b = new N6Bench();
        await N4bTestKey.WithKeyAsync(async () =>
        {
            b.WriteLedger(N6Current);
            var feed = N4bTestKey.Signed(b.Unsigned(N6Current, N6FeedZipUrl));
            var prev = N4bTestKey.Signed(b.Unsigned(N6Prev, "https://updates.hitpan.kr/packages/hitpan-" + N6Prev + ".zip"));
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var feedDown = which == "startup-missed";
            b.Http.Handler.Respond = async (req, _) =>
            {
                if (req.RequestUri!.AbsolutePath.EndsWith(".previous-manifest.json", StringComparison.Ordinal))
                {
                    await gate.Task.ConfigureAwait(false);                                // 확인표 응답을 붙잡아 둔다(느린 망)
                    return N6Json(prev);
                }
                return feedDown ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : N6Json(feed);
            };

            try
            {
                Task<FeedCheckResult>? startup = null;
                if (which == "inflight")
                {
                    startup = b.Adapter.CheckAsync(N6Current, CancellationToken.None);      // 기동 확인 — 확인표에서 멈춰 있다
                    Assert.True(await N7WaitUntil(() => N7PrevRequests(b) == 1, TimeSpan.FromSeconds(10)), "기동 확인이 확인표를 묻지 않았다");
                }
                else
                {
                    var down = await b.Adapter.CheckAsync(N6Current, CancellationToken.None); // 기동 확인 — 피드에 못 닿음
                    Assert.NotEqual(FeedCheckStatus.NotNewer, down.Status);
                    Assert.Equal(0, N7PrevRequests(b));
                    feedDown = false;
                }

                var screen = b.Adapter.CheckAsync(N6Current, CancellationToken.None);       // 업데이트 화면 확인
                var done = await Task.WhenAny(screen, Task.Delay(TimeSpan.FromSeconds(5)));
                Assert.True(ReferenceEquals(done, screen), which + ": 화면 확인이 첫 회 확인표 받기(응답 붙잡힘)를 기다렸다");
                Assert.Equal(FeedCheckStatus.NotNewer, (await screen).Status);
                Assert.False(gate.Task.IsCompleted);

                if (which == "inflight")
                {
                    Assert.Equal(1, N7PrevRequests(b));                                    // 받는 중 = 건너뜀(추가 GET 0)
                    gate.SetResult(true);
                    Assert.Equal(FeedCheckStatus.NotNewer, (await startup!).Status);
                }
                else
                {
                    Assert.True(await N7WaitUntil(() => N7PrevRequests(b) == 1, TimeSpan.FromSeconds(10)), "화면 확인이 뒤에서 받기를 시작하지 않았다");
                    gate.SetResult(true);
                }
                Assert.True(await N7WaitUntil(() => Kept(b.Env).Contains(N6Prev + ".json"), TimeSpan.FromSeconds(10)),
                    which + ": 받은 직전 판 확인표가 남지 않았다 — " + string.Join(", ", Kept(b.Env)));
                Assert.Equal(new[] { N6Prev + ".json", N6Current + ".json" }, Kept(b.Env));
                await b.Adapter.CheckAsync(N6Current, CancellationToken.None);
                Assert.Equal(1, N7PrevRequests(b));
            }
            finally
            {
                gate.TrySetResult(true);                                                    // 대조에서 붙잡힌 요청도 풀어 준다
            }
        });
    }
}
