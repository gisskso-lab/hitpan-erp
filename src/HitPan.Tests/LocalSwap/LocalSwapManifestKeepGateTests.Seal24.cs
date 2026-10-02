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
/// <para>대조(Edit 도구로 제품을 바꿔 돌림 → 원복 · 개발명세서 N7 §4): (ㄱ) <c>KeepFeedManifestNotBelow</c> 의 <c>return;</c> 뺌 → 「1.3.48」 에서 저장·되돌리기 섬.</para>
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
}
