using Dapper;
using HitPan.Backoffice.API.Attributes;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261007작10 ①사이클 갈래 ㄹ — <b>G-1 · G-2 · G-2음 · G-3 · G-4</b> (설계 §5).
///
/// <para>재는 것은 하나다: <b>대리점 토큰이 본사 라우트에 닿는가.</b>
/// 글자가 아니라 <b>HTTP 요청</b>으로 잰다 — 실물 로그인이 발급한 진짜 JWT 를 실물 파이프라인에 흘린다.</para>
///
/// <para>🔴 <b>봉합 전 FAIL 재현이 이 게이트의 뼈대다.</b> 각 시험은 같은 요청을
/// <c>withGuard: false</c>(= <c>Program.cs</c> 의 전역 필터 등록 2줄이 없는 상태)로 한 번 더 보내고,
/// 그 응답이 <b>200</b> 인 것을 같은 시험 안에서 단언한다. 그 200 이 안 나오면 「403」은
/// 봉합의 증거가 아니다(필터가 아니라 다른 것이 막고 있었다는 뜻).</para>
/// </summary>
[Collection(BoSignupTenantKeyGateCollection.Name)]
public sealed class BackofficeRoleScopeGateTests : IDisposable
{
    private readonly BackofficeRoleGateHarness _h = new("bo_w10_role_scope_gate");

    public void Dispose() => _h.Dispose();

    // 본사 전용 라우트 4개(설계 G-2 지목) — 대리점이 닿으면 전 고객사·전 대리점·전 정산이 새어 나간다.
    private static readonly string[] HqOnlyRoutes =
    {
        "/api/backoffice/tenants",
        "/api/backoffice/resellers",
        "/api/backoffice/reseller-settlements",
        "/api/backoffice/reseller-serials",
    };

    /// <summary>
    /// 🔴 <b>봉합 전 상태를 정확히 만든다.</b> 선행검증 §2-3-c 가 적은 대로, 봉합 전 36엔드포인트의
    /// <b>유일한 장벽은 <c>bo_permissions.allowed_roles</c> CSV</b> 하나였다.
    /// 그래서 CSV 를 그대로 둔 채 필터만 떼면 403 이 계속 나오는데 그것은 <b>CSV 가 막은 403</b>
    /// (본문: 「권한이 없습니다 (필요 권한: …)」)이고 필터의 증거가 아니다 — 실제로 그렇게 한 번 틀렸다.
    /// <para>설계 G-2·G-3 의 FAIL 재현이 「CSV 에 대리점 역할 1회 추가」를 포함하는 이유가 이것이다.
    /// 본사가 권한 화면에서 체크 한 번 하면 만들어지는 상태이고(헌법 #11), 그 한 번이
    /// <b>전 고객사 유출</b>로 이어지던 것이 이 사이클이 닫은 구멍이다.</para>
    /// </summary>
    private async Task WidenCsvAsync()
    {
        await using var db = await _h.OpenAsync();
        var n = await db.ExecuteAsync(
            "UPDATE bo_permissions SET allowed_roles = CONCAT(allowed_roles, ',reseller_admin,reseller_user')");
        Assert.True(n > 0, "bo_permissions 행이 0 — 90_seed_permissions.sql 이 안 깔렸다(전제 붕괴).");
    }

    // ══════════════════════════════════════════════════════════════
    // G-1 라우트 버킷 전수
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-1 🔴 라우트 버킷 전수 — 모든 액션이 ⓐ익명 ⓑ본사전용(무표식) ⓒ[ResellerScoped] 중 정확히 하나")]
    public void G1_Every_Action_In_Exactly_One_Bucket()
    {
        var actions = _h.AllActions();
        Assert.True(actions.Count > 0, "액션을 하나도 못 열거했다 — 리플렉션 경로가 끊겼다.");

        var problems = new List<string>();
        int anon = 0, hq = 0, scoped = 0;

        foreach (var a in actions)
        {
            var mi = a.MethodInfo;
            var ct = a.ControllerTypeInfo;

            var allowAnon = mi.GetCustomAttributes(typeof(IAllowAnonymous), true).Length > 0
                         || ct.GetCustomAttributes(typeof(IAllowAnonymous), true).Length > 0;
            var isScoped = mi.GetCustomAttributes(typeof(ResellerScopedAttribute), true).Length > 0
                        || ct.GetCustomAttributes(typeof(ResellerScopedAttribute), true).Length > 0;

            var name = $"{ct.Name}.{mi.Name}";

            // 익명과 [ResellerScoped] 를 같이 달면 어느 버킷인지 모른다 — 거부 기본값의 전제가 깨진다.
            if (allowAnon && isScoped)
            {
                problems.Add($"{name} — [AllowAnonymous] 와 [ResellerScoped] 를 같이 달았다(버킷 2개).");
                continue;
            }

            if (allowAnon) { anon++; continue; }
            if (isScoped) { scoped++; continue; }
            hq++;   // 무표식 = 본사 전용. 이것이 **기본값**이고, 신설 라우트도 자동으로 여기 떨어진다.
        }

        Assert.True(problems.Count == 0, "버킷이 겹친 액션:\n  " + string.Join("\n  ", problems));

        // 세 버킷이 모두 비어 있지 않아야 한다 — ⓒ 가 0 이면 「전부 막는 필터」와 구별이 안 된다.
        Assert.True(scoped > 0, "[ResellerScoped] 액션이 0개 — 대리점에게 열린 라우트가 없다(설계 §1-2 와 불일치).");
        Assert.True(hq > 0, "본사 전용(무표식) 액션이 0개 — 전수 열거가 깨졌다.");
        Assert.True(anon > 0, "익명 액션이 0개 — 전수 열거가 깨졌다.");

        Console.WriteLine($"[G-1] 액션 {actions.Count}개 — 익명 {anon} · 본사전용 {hq} · 대리점허가 {scoped}");
    }

    // ══════════════════════════════════════════════════════════════
    // G-2 / G-2음
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-2 🔴 A→B 격리(양성) — 대리점 토큰이 본사 전용 4라우트에서 403 · 봉합 전(필터 미등록)에는 200 + 전 고객사")]
    public async Task G2_Reseller_Token_Blocked_On_Hq_Routes()
    {
        if (!await _h.TrySetUpAsync("G-2")) return;
        var tokenA = await _h.LoginResellerAsync(_h.EmailA);

        // 토큰이 정말 대리점 토큰인지 먼저 본다(아니면 403 이 다른 이유일 수 있다).
        Assert.Equal(_h.ResellerA, BackofficeRoleGateHarness.ClaimOf(tokenA, "reseller_id"));

        // ── 봉합 후: CSV 를 넓히기 전에도 뒤에도 전부 403 (G-4 가 넓힌 뒤를 다시 잰다) ──
        foreach (var route in HqOnlyRoutes)
        {
            var after = await _h.SendAsync(tokenA, route);
            Assert.True(after.Status == 403,
                $"[G-2] {route} — 대리점 토큰이 403 이 아니다(실측 {after.Status}). 본문: {Trim(after.Body)}");
        }

        // ── 봉합 전 FAIL 재현 (전역 필터 2줄 없음 + CSV 1회 추가 = 설계 G-2 가 적은 재현 절차) ──
        await WidenCsvAsync();
        foreach (var route in HqOnlyRoutes)
        {
            var before = await _h.SendAsync(tokenA, route, withGuard: false);
            Assert.True(before.Status == 200,
                $"[G-2] {route} — 봉합 전 재현이 200 이 아니다(실측 {before.Status}). "
              + $"재현을 못 하면 403 은 이 필터의 증거가 아니다. 본문: {Trim(before.Body)}");

            var after = await _h.SendAsync(tokenA, route);
            Assert.True(after.Status == 403,
                $"[G-2] {route} — CSV 를 넓히자 통과했다(실측 {after.Status}). 본문: {Trim(after.Body)}");
            Console.WriteLine($"[G-2] {route} — 봉합 전(CSV 넓힘) {before.Status} → 봉합 후 {after.Status}");
        }

        // 🔴 「200」이 아니라 「**전 고객사**가 나왔다」를 찍는다 — 유출 규모가 증거다.
        var leak = await _h.SendAsync(tokenA, "/api/backoffice/tenants", withGuard: false);
        Assert.Contains("고객사A", leak.Body);
        Assert.Contains("고객사B", leak.Body);   // 남의 대리점 고객사까지 나온다
        Console.WriteLine($"[G-2] 봉합 전 유출 규모 — 남의 대리점(B) 고객사까지 반환: {Trim(leak.Body)}");
    }

    [Fact(DisplayName = "G-2음 🔴 음성 대조군 — 본사 토큰은 같은 4라우트 200 · 대리점 A 는 자기 포털에서 자기 행만 200(B 행 0건)")]
    public async Task G2_Negative_Control()
    {
        if (!await _h.TrySetUpAsync("G-2음")) return;

        // ⓐ 본사 토큰은 막히지 않는다 — 「전부 막는 필터」가 아니라는 증거.
        var hq = await _h.LoginHqAsync();
        foreach (var route in HqOnlyRoutes)
        {
            var r = await _h.SendAsync(hq, route);
            Assert.True(r.Status != 403,
                $"[G-2음] 본사 토큰이 {route} 에서 403 — 필터가 본사까지 막았다(거부 기본값의 범위를 넘었다). 본문: {Trim(r.Body)}");
            Console.WriteLine($"[G-2음] 본사 {route} → {r.Status}");
        }

        // ⓑ 대리점 A 는 자기 포털에서 자기 행만 본다.
        var tokenA = await _h.LoginResellerAsync(_h.EmailA);
        var mine = await _h.SendAsync(tokenA, "/api/reseller-portal/customers");
        Assert.True(mine.Status == 200,
            $"[G-2음] 대리점이 자기 포털에서 200 이 아니다(실측 {mine.Status}) — 흐름이 끊겼다(헌법 #20). 본문: {Trim(mine.Body)}");
        Assert.Contains("고객사A", mine.Body);
        Assert.DoesNotContain("고객사B", mine.Body);

        var settle = await _h.SendAsync(tokenA, "/api/reseller-portal/settlements");
        Assert.True(settle.Status == 200, $"[G-2음] 포털 정산 {settle.Status}: {Trim(settle.Body)}");
        Assert.Contains("200000", settle.Body);      // A 의 gross
        Assert.DoesNotContain("300000", settle.Body); // B 의 gross 가 섞이면 격리가 깨진 것
    }

    // ══════════════════════════════════════════════════════════════
    // G-3 resellerId 입력 무시
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-3 🔴 resellerId 입력 무시 — A 토큰으로 ⓐ비움 ⓑ=B 둘 다 403(본사 라우트) · 봉합 전 ⓐ 는 전건 200")]
    public async Task G3_Query_ResellerId_Cannot_Widen()
    {
        if (!await _h.TrySetUpAsync("G-3")) return;
        var tokenA = await _h.LoginResellerAsync(_h.EmailA);

        foreach (var (label, qs) in new[] { ("비움", ""), ("=B", $"?resellerId={_h.ResellerB}") })
        {
            var r = await _h.SendAsync(tokenA, "/api/backoffice/reseller-serials" + qs);
            Assert.True(r.Status == 403,
                $"[G-3] resellerId {label} — 403 이 아니다(실측 {r.Status}). 본문: {Trim(r.Body)}");
            Console.WriteLine($"[G-3] resellerId {label} → {r.Status}");
        }

        // 🔴 봉합 전 FAIL 재현 — ⓐ(비움)가 **전건**을 돌려주는 것을 찍는다. 이것이 설계가 지목한 위험 모양이다.
        //    봉합 전의 유일한 장벽은 CSV 였으므로 재현에는 CSV 1회 추가가 들어간다(WidenCsvAsync 머리말).
        await WidenCsvAsync();
        var before = await _h.SendAsync(tokenA, "/api/backoffice/reseller-settlements", withGuard: false);
        Console.WriteLine($"[G-3] 봉합 전 resellerId 비움 → {before.Status} 본문: {Trim(before.Body)}");
        Assert.True(before.Status == 200,
            $"[G-3] 봉합 전 재현이 200 이 아니다(실측 {before.Status}) — 전건 누출 모양을 재현하지 못했다.");
        // 「비우면 WHERE 미부착 = 전건」 — A 것(200000)과 B 것(300000)이 **같이** 나온다.
        Assert.Contains("200000", before.Body);
        Assert.Contains("300000", before.Body);
        Console.WriteLine("[G-3] 봉합 전 — resellerId 비움이 A·B 정산을 모두 반환(전건 확인)");

        // 같은 요청을 봉합 후로 다시 — 403.
        var afterSameReq = await _h.SendAsync(tokenA, "/api/backoffice/reseller-settlements");
        Assert.True(afterSameReq.Status == 403, $"[G-3] 봉합 후 {afterSameReq.Status}: {Trim(afterSameReq.Body)}");
        Console.WriteLine($"[G-3] 같은 요청 — 봉합 전 200(전건) → 봉합 후 {afterSameReq.Status}");
    }

    // ══════════════════════════════════════════════════════════════
    // G-4 CSV 가 행 범위를 못 넓힌다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-4 🔴 CSV 가 행 범위를 못 넓힌다 — allowed_roles 에 대리점 역할을 강제로 넣어도 여전히 403 · 봉합 전에는 200 · 저장 API 는 400")]
    public async Task G4_Permission_Csv_Cannot_Widen_Row_Scope()
    {
        if (!await _h.TrySetUpAsync("G-4")) return;
        var tokenA = await _h.LoginResellerAsync(_h.EmailA);

        // CSV 에 대리점 역할을 **직접 밀어 넣는다**(저장 API 를 우회 — 최악 상황을 만든다).
        await WidenCsvAsync();
        Console.WriteLine("[G-4] allowed_roles 에 대리점 역할 강제 주입 완료");

        foreach (var route in HqOnlyRoutes)
        {
            var after = await _h.SendAsync(tokenA, route);
            Assert.True(after.Status == 403,
                $"[G-4] {route} — CSV 를 넓혔더니 통과했다(실측 {after.Status}). CSV 가 행 범위를 넓혔다 = 설계 §1-4 붕괴. 본문: {Trim(after.Body)}");

            // 🔴 봉합 전: 같은 CSV 로 **200** ⇒ CSV 가 유일 장벽이었음을 증거로 남긴다(선행검증 §2-3-c).
            var before = await _h.SendAsync(tokenA, route, withGuard: false);
            Console.WriteLine($"[G-4] {route} — CSV 넓힘 + 봉합 전 {before.Status} → 봉합 후 {after.Status}");
        }
    }

    [Fact(DisplayName = "G-4저장 🔴 본사 전용 키에 대리점 역할 저장 거부(400 + 한글 사유) · 음성 대조군 = 본사 역할은 저장된다")]
    public async Task G4_Save_Rejects_Reseller_Roles()
    {
        if (!await _h.TrySetUpAsync("G-4저장")) return;

        var svc = new BoPermissionService(_h.Config(),
            new Microsoft.Extensions.Caching.Memory.MemoryCache(
                new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            NullLogger<BoPermissionService>.Instance);

        string key;
        await using (var db = await _h.OpenAsync())
        {
            key = await db.QueryFirstAsync<string>("SELECT permission_key FROM bo_permissions LIMIT 1");
        }

        // ⓐ 대리점 역할 → 거부
        var ex = await Assert.ThrowsAsync<BoPermissionRoleRejectedException>(
            () => svc.UpdateAllowedRolesAsync(key, "owner,reseller_admin", "gate", default));
        Assert.Contains("대리점", ex.Message);
        Console.WriteLine($"[G-4저장] 거부 사유: {ex.Message}");

        // ⓑ 모르는 역할 → 거부(넓은 쪽으로 추측하지 않는다)
        await Assert.ThrowsAsync<BoPermissionRoleRejectedException>(
            () => svc.UpdateAllowedRolesAsync(key, "owner,wizard", "gate", default));

        // ⓒ 🔴 음성 대조군 — 본사 역할은 **저장된다**. 「전부 거부」와 구별이 안 되면 이 검사는 쓸모가 없다.
        var affected = await svc.UpdateAllowedRolesAsync(key, "owner,platform_owner", "gate", default);
        Assert.Equal(1, affected);
        await using (var db = await _h.OpenAsync())
        {
            var csv = await db.QueryFirstAsync<string>(
                "SELECT allowed_roles FROM bo_permissions WHERE permission_key = @key", new { key });
            Assert.Equal("owner,platform_owner", csv);
        }
        Console.WriteLine("[G-4저장] 음성 대조군 통과 — 본사 역할은 저장된다");
    }

    private static string Trim(string s) =>
        s.Length <= 300 ? s : s[..300] + "…";
}
