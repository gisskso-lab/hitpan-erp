using System.Diagnostics;
using Dapper;
using HitPan.Backoffice.API.Attributes;
using HitPan.Backoffice.API.Security;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-8 사다리 과녁</b> — 대리점 <b>관리자 전용</b> 라우트(최소 등급 <c>reseller_admin</c>).
///
/// <para>⚠️ <b>이 컨트롤러는 제품이 아니다.</b> 시험 어셈블리에 있고, 기본적으로 라우팅에 올라가지도 않는다
/// (<c>SendAsync(includeGateRoutes: true)</c> 일 때만). 붙은 표식·가드·파이프라인은 <b>전부 실물</b>이다 —
/// 재는 대상은 <c>BoAccessGuard</c> 의 사다리 판정이고 이 컨트롤러는 과녁일 뿐이다.</para>
///
/// <para>🔴 <b>왜 시험용 과녁이 필요한가</b> — 이번 차수에는 <b>대리점 관리자 전용 제품 라우트가 없다</b>.
/// 설계가 <c>ResellerPortalController</c> 3개를 열었는데 그 중 어느 것을 관리자 전용으로 둘지는
/// 업무 판단이라 구현이 정하지 않았다(개발명세서 §5 — PM 보고 사항).
/// ⇒ G-8ⓐ 통과는 「<b>가드의 하향 차단이 동작한다</b>」의 증거이고,
///   「<b>제품 라우트가 보호된다</b>」의 증거는 <b>아니다</b>. 제품 라우트가 생기면 그때 과녁을 바꾼다.</para>
/// </summary>
[ApiController]
[Route("api/__w10gate/ladder")]
[Authorize]
[ResellerScoped(BoRoles.ResellerAdmin)]
public sealed class BoGateLadderController : ControllerBase
{
    private readonly IResellerScope _scope;

    public BoGateLadderController(IResellerScope scope) => _scope = scope;

    [HttpGet]
    public IActionResult Get() => Ok(new { success = true, scope = _scope.Current() });
}

/// <summary>
/// 🔴🔴 20261007작10 ①사이클 갈래 ㄹ — <b>G-8 · G-8음 · G-8ㄷ</b> (설계 §5 · §3-3 · PM 결재 P-5).
///
/// <para><b>재는 것</b>: 권한을 <b>낮추거나 계정을 끄거나 소속을 바꾸면 다음 요청부터 끊기는가.</b>
/// <b>재로그인·토큰 재발급 0회</b>로 잰다 — 같은 토큰 <c>T</c> 를 그대로 다시 쓴다.</para>
///
/// <para>🔴 <b>이 게이트가 막는 재발</b>: 작3·작4 병렬검증 <b>P1-01</b> — 「계정을 끈 뒤에도 8시간 토큰이
/// 계속 통과하고 차단 수단이 0」. 그 구멍이 ERP 쪽에 <b>미봉합으로 남아 있다.</b>
/// 백오피스에 같은 구멍을 새로 파지 않는 것이 이 게이트의 존재 이유다.</para>
///
/// <para>🔴 <b>봉합 전 FAIL 재현</b>: 같은 변경·같은 토큰으로 <c>withGuard: false</c>(= ⓞ 라이브 판독이 없는
/// 「토큰 역할만 믿는」 구조) 요청을 한 번 더 보내 <b>200</b> 을 같은 시험 안에서 찍는다.
/// 그 200 이 없으면 403 은 ⓞ 의 증거가 아니다.</para>
/// </summary>
[Collection(BoSignupTenantKeyGateCollection.Name)]
public sealed class BackofficeRoleDowngradeGateTests : IDisposable
{
    private const string Portal = "/api/reseller-portal/summary";
    private const string LadderRoute = "/api/__w10gate/ladder";

    private readonly BackofficeRoleGateHarness _h = new("bo_w10_role_downgrade_gate");

    public void Dispose() => _h.Dispose();

    private async Task SetAsync(string sql, object? p = null)
    {
        await using var db = await _h.OpenAsync();
        var n = await db.ExecuteAsync(sql, p);
        Assert.True(n > 0, $"DB 변경이 0행 — 시험 전제가 깨졌다: {sql}");
    }

    // ══════════════════════════════════════════════════════════════
    // G-8 하향·차단 즉시성
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-8 🔴 하향·차단 즉시성 — 같은 토큰으로 ⓐ역할낮춤 ⓑ비활성 ⓒ소속변경 → 전부 403 · 봉합 전(ⓞ 없음)에는 전부 200 (재로그인 0회)")]
    public async Task G8_Downgrade_And_Block_Are_Immediate()
    {
        if (!await _h.TrySetUpAsync("G-8")) return;

        // ── 출발점: 대리점 관리자로 로그인. 이 토큰 T 를 끝까지 그대로 쓴다. ──
        var t = await _h.LoginResellerAsync(_h.EmailA);
        Assert.Equal(BoRoles.ResellerAdmin, BackofficeRoleGateHarness.ClaimOf(t, "role"));

        var start = await _h.SendAsync(t, LadderRoute, includeGateRoutes: true);
        Assert.True(start.Status == 200,
            $"[G-8] 출발점이 200 이 아니다(실측 {start.Status}) — 아래 403 들이 무엇의 결과인지 알 수 없다. 본문: {Trim(start.Body)}");
        Console.WriteLine($"[G-8] 출발점(reseller_admin · 변경 전) {LadderRoute} → 200");

        // ── ⓐ 역할 낮춤 (reseller_admin → reseller_user) ──
        await SetAsync("UPDATE reseller_accounts SET role='reseller_user' WHERE account_id=@id", new { id = _h.AccountAId });

        var aAfter = await _h.SendAsync(t, LadderRoute, includeGateRoutes: true);
        var aBefore = await _h.SendAsync(t, LadderRoute, withGuard: false, includeGateRoutes: true);
        Console.WriteLine($"[G-8ⓐ] 역할낮춤 — 봉합 전 {aBefore.Status} → 봉합 후 {aAfter.Status}");
        Assert.True(aBefore.Status == 200,
            $"[G-8ⓐ] 봉합 전이 200 이 아니다(실측 {aBefore.Status}) — FAIL 재현 실패. 403 이 ⓞ 의 증거가 아니다.");
        Assert.True(aAfter.Status == 403,
            $"[G-8ⓐ] 역할을 낮췄는데 통과한다(실측 {aAfter.Status}) — 하향이 토큰 수명 내내 안 먹힌다. 본문: {Trim(aAfter.Body)}");

        // 낮춘 등급으로도 되는 라우트는 여전히 된다(하향이 전면 차단이 아님 — 음성 대조).
        var stillOk = await _h.SendAsync(t, Portal);
        Assert.True(stillOk.Status == 200,
            $"[G-8ⓐ] 하향 후 일반 포털까지 막혔다(실측 {stillOk.Status}) — 과잉 차단(헌법 #20). 본문: {Trim(stillOk.Body)}");
        Console.WriteLine($"[G-8ⓐ] 음성 대조 — 낮춘 등급으로 되는 라우트 {Portal} → 200");

        await SetAsync("UPDATE reseller_accounts SET role='reseller_admin' WHERE account_id=@id", new { id = _h.AccountAId });

        // ── ⓑ 계정 비활성 (모든 요청 403) ──
        await SetAsync("UPDATE reseller_accounts SET is_active=0 WHERE account_id=@id", new { id = _h.AccountAId });

        var bAfter = await _h.SendAsync(t, Portal);
        var bBefore = await _h.SendAsync(t, Portal, withGuard: false);
        Console.WriteLine($"[G-8ⓑ] 비활성 — 봉합 전 {bBefore.Status} → 봉합 후 {bAfter.Status}");
        Assert.True(bBefore.Status == 200,
            $"[G-8ⓑ] 봉합 전이 200 이 아니다(실측 {bBefore.Status}) — 작3·작4 P1-01 과 같은 구멍 모양을 재현하지 못했다.");
        Assert.True(bAfter.Status == 403,
            $"[G-8ⓑ] 계정을 껐는데 통과한다(실측 {bAfter.Status}) — 토큰 8시간 내내 들어온다(P1-01 재발). 본문: {Trim(bAfter.Body)}");
        Assert.Contains("비활성", bAfter.Body);

        var bLadder = await _h.SendAsync(t, LadderRoute, includeGateRoutes: true);
        Assert.True(bLadder.Status == 403, $"[G-8ⓑ] 비활성인데 사다리 라우트가 {bLadder.Status} — 「모든 요청」이 아니다.");

        await SetAsync("UPDATE reseller_accounts SET is_active=1 WHERE account_id=@id", new { id = _h.AccountAId });

        // ── ⓒ 소속 변경 (A → B) ──
        await SetAsync("UPDATE reseller_accounts SET reseller_id=@b WHERE account_id=@id",
            new { b = _h.ResellerB, id = _h.AccountAId });

        var cAfter = await _h.SendAsync(t, Portal);
        var cBefore = await _h.SendAsync(t, Portal, withGuard: false);
        Console.WriteLine($"[G-8ⓒ] 소속변경 — 봉합 전 {cBefore.Status} → 봉합 후 {cAfter.Status}");
        Assert.True(cBefore.Status == 200,
            $"[G-8ⓒ] 봉합 전이 200 이 아니다(실측 {cBefore.Status}) — 옛 소속 데이터가 더 나가는 모양을 재현하지 못했다.");
        Assert.True(cAfter.Status == 403,
            $"[G-8ⓒ] 소속이 바뀌었는데 통과한다(실측 {cAfter.Status}) — 옛 소속 데이터가 한 요청 더 나간다. 본문: {Trim(cAfter.Body)}");
        Assert.Contains("소속이 변경", cAfter.Body);
        Assert.Contains("다시 로그인", cAfter.Body);

        // 🔴 봉합 전에 **옛 소속(A)** 데이터가 실제로 나갔는지 — 「막는 것 ≠ 알려주는 것」의 반대편 증거.
        //    DB 는 이미 B 소속인데 토큰이 A 라 A 의 정산 2건이 그대로 나간다.
        Console.WriteLine($"[G-8ⓒ] 봉합 전 본문(옛 소속 데이터 누출): {Trim(cBefore.Body)}");

        await SetAsync("UPDATE reseller_accounts SET reseller_id=@a WHERE account_id=@id",
            new { a = _h.ResellerA, id = _h.AccountAId });
    }

    // ══════════════════════════════════════════════════════════════
    // G-8음 음성 대조군 — 상향은 재로그인까지 안 바뀐다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-8음 🔴 음성 대조군 — ⓓ상향은 같은 토큰으로 여전히 403, 재로그인 후 200 · ⓔ아무것도 안 바꾼 계정은 계속 200")]
    public async Task G8_Negative_Upgrade_Needs_Relogin()
    {
        if (!await _h.TrySetUpAsync("G-8음")) return;

        // ⓓ 먼저 낮은 등급으로 만들고 **그 상태로 로그인**한다(토큰이 reseller_user).
        await SetAsync("UPDATE reseller_accounts SET role='reseller_user' WHERE account_id=@id", new { id = _h.AccountAId });
        var t = await _h.LoginResellerAsync(_h.EmailA);
        Assert.Equal(BoRoles.ResellerUser, BackofficeRoleGateHarness.ClaimOf(t, "role"));

        var before = await _h.SendAsync(t, LadderRoute, includeGateRoutes: true);
        Assert.True(before.Status == 403, $"[G-8음ⓓ] 낮은 등급이 상위 라우트에 들어갔다(실측 {before.Status}).");

        // DB 에서 **올린다**. 같은 토큰은 여전히 403 이어야 한다 — 좁은 쪽(토큰)이 이긴다.
        await SetAsync("UPDATE reseller_accounts SET role='reseller_admin' WHERE account_id=@id", new { id = _h.AccountAId });

        var sameToken = await _h.SendAsync(t, LadderRoute, includeGateRoutes: true);
        Assert.True(sameToken.Status == 403,
            $"[G-8음ⓓ] 상향이 같은 토큰에 **즉시** 반영됐다(실측 {sameToken.Status}). "
          + "그러면 메뉴는 토큰에서 그려지므로 「API 는 되는데 메뉴에 없다」가 되어 G-6 이 깨진다(설계 §3-3 표). "
          + "「좁은 쪽 우선」을 「DB 쪽 우선」으로 바꾸면 이 자리가 200 이 된다.");
        Console.WriteLine($"[G-8음ⓓ] 상향 후 같은 토큰 → {sameToken.Status} (의도된 비대칭)");

        // 재로그인하면 열린다 — 상향이 영영 안 되는 것이 아니라 **느린** 것이다.
        var t2 = await _h.LoginResellerAsync(_h.EmailA);
        var afterRelogin = await _h.SendAsync(t2, LadderRoute, includeGateRoutes: true);
        Assert.True(afterRelogin.Status == 200,
            $"[G-8음ⓓ] 재로그인해도 403(실측 {afterRelogin.Status}) — 상향이 영영 안 먹힌다. 본문: {Trim(afterRelogin.Body)}");
        Console.WriteLine($"[G-8음ⓓ] 재로그인 후 → 200");

        // ⓔ 아무것도 안 바꾼 계정(B)은 계속 200 — 「시간이 지나면 다 막히는」 것이 아니라는 증거.
        var tb = await _h.LoginResellerAsync(_h.EmailB);
        for (var i = 1; i <= 3; i++)
        {
            var r = await _h.SendAsync(tb, Portal);
            Assert.True(r.Status == 200, $"[G-8음ⓔ] 안 바꾼 계정이 {i}회차에 {r.Status} — 과잉 차단. 본문: {Trim(r.Body)}");
        }
        Console.WriteLine("[G-8음ⓔ] 안 바꾼 계정 3회 연속 200");
    }

    // ══════════════════════════════════════════════════════════════
    // G-8ㄷ 캐시 금지
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-8ㄷ 🔴 캐시 금지 — DB 를 바꾼 직후 첫 요청(지연 0)이 403 · 되살린 직후 첫 요청이 200 · 10회 중 10회")]
    public async Task G8c_No_Cache_In_Live_Read()
    {
        if (!await _h.TrySetUpAsync("G-8ㄷ")) return;
        var t = await _h.LoginResellerAsync(_h.EmailA);

        var offHits = 0;
        var onHits = 0;
        for (var i = 1; i <= 10; i++)
        {
            await SetAsync("UPDATE reseller_accounts SET is_active=0 WHERE account_id=@id", new { id = _h.AccountAId });
            var off = await _h.SendAsync(t, Portal);              // 지연 0 — 끈 직후 첫 요청
            if (off.Status == 403) offHits++;

            await SetAsync("UPDATE reseller_accounts SET is_active=1 WHERE account_id=@id", new { id = _h.AccountAId });
            var on = await _h.SendAsync(t, Portal);               // 지연 0 — 되살린 직후 첫 요청
            if (on.Status == 200) onHits++;
        }

        Console.WriteLine($"[G-8ㄷ] 끈 직후 403: {offHits}/10 · 되살린 직후 200: {onHits}/10");
        Assert.True(offHits == 10,
            $"[G-8ㄷ] 끈 직후 첫 요청이 {10 - offHits}회 통과했다 — 판독 경로에 캐시가 끼었다. "
          + "TTL 을 두면 그만큼 「즉시」가 거짓이 된다(설계 §3-3-나).");
        Assert.True(onHits == 10,
            $"[G-8ㄷ] 되살린 직후 첫 요청이 {10 - onHits}회 막혔다 — 거부 쪽에 캐시가 끼었다(복구가 늦는다).");
    }

    [Fact(DisplayName = "G-8비용 ⓞ 라이브 판독 왕복 1회 실측 — UNION ALL 한 왕복(#16) · 캐시 없음 확인(매 호출 DB 왕복)")]
    public async Task G8_Cost_Measurement()
    {
        if (!await _h.TrySetUpAsync("G-8비용")) return;

        var reader = new BoLiveAccountReader(_h.Config(), NullLogger<BoLiveAccountReader>.Instance);
        await reader.ReadAsync(_h.AccountAId);   // 워밍업(연결 풀·표 모양 판별)

        var samples = new List<double>();
        for (var i = 0; i < 20; i++)
        {
            var sw = Stopwatch.StartNew();
            var live = await reader.ReadAsync(_h.AccountAId);
            sw.Stop();
            Assert.True(live.Found, "[G-8비용] 판독이 계정을 못 찾았다 — 측정 전제가 깨졌다.");
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }
        samples.Sort();
        Console.WriteLine($"[G-8비용] ⓞ 판독 {samples.Count}회 — 최소 {samples[0]:F2}ms · "
                        + $"중위 {samples[samples.Count / 2]:F2}ms · 최대 {samples[^1]:F2}ms");

        // 🔴 캐시가 없다는 것을 **동작으로** 한 번 더 확인 — 같은 sub 를 두 번 읽는 사이에 DB 를 바꾸면 결과가 바뀐다.
        await SetAsync("UPDATE reseller_accounts SET is_active=0 WHERE account_id=@id", new { id = _h.AccountAId });
        var after = await reader.ReadAsync(_h.AccountAId);
        Assert.False(after.IsActive, "[G-8비용] 판독기가 이전 결과를 재사용했다 — 캐시가 끼었다.");
        await SetAsync("UPDATE reseller_accounts SET is_active=1 WHERE account_id=@id", new { id = _h.AccountAId });
    }

    private static string Trim(string s) => s.Length <= 300 ? s : s[..300] + "…";
}
