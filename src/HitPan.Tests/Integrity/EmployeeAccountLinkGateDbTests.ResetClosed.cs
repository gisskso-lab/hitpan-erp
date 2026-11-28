using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>D-B2 — my-level 실물 파이프라인 · 초기화 닫힘</b>(20261127작 W-S-01 갈래 B · 설계 §4 · CI db-gate 전용 · 이 PC 3306 = 실물이라 로컬 금지).
/// </summary>
/// <remarks>
/// <para>
/// 운영 구현(<see cref="DataResetBetaAvailability"/>)을 넣은 같은 파이프라인에서 대표 관리자의 <c>GET /api/users/my-level</c> 이
/// <c>dataResetGate = "closed"</c> 를 돌려줘야 한다 — 사이드바 「모든데이터 초기화」 한 줄이 저절로 사라지는 재료(<c>ShowResetLink("closed") == false</c>).
/// </para>
/// <para>대조 = 같은 시험 안에서 열린 가짜로 다시 부르면 <c>allowed</c>(닫힘이 판정 앞에서 온 것임을 보인다 · G-E9 와 같은 값).</para>
/// </remarks>
public sealed partial class EmployeeAccountLinkGateDbTests
{
    /// <summary>PipeAsync 가 넣는 초기화 열림 여부. 기본 = 열린 가짜(기존 G-E9 · G-E13 뜻 그대로). xUnit 은 시험마다 새 인스턴스라 다른 시험에 새지 않는다.</summary>
    private IDataResetAvailability _resetAvailability = DataResetAvailabilityForTest.OpenForTest;

    [Fact(DisplayName = "D-B2 🔴 my-level 실물 파이프라인 — 운영 구현(닫힘)이면 대표 관리자 dataResetGate = closed(메뉴 감춤) · 관리자 자식도 closed · 대조: 열린 가짜면 대표 allowed · 관리자 자식 not_parent")]
    public async Task DB2_my_level_닫힘_closed()
    {
        if (!Ready("D-B2")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db);
        var owner = await StaffAsync(db, "owner01", 3, parent: true);
        var manager = await StaffAsync(db, "mgr0927", 3, admin: true);

        _resetAvailability = new DataResetBetaAvailability();
        var closed = await PipeAsync(db, "GET", "/api/users/my-level", owner);
        Assert.True(closed.Status == 200, $"대표 my-level(닫힌 판) → {closed.Status} · {closed.Body}");
        Assert.Contains($"\"dataResetGate\":\"{HitPan.Web.Services.DataResetMenuRule.Closed}\"", closed.Body);
        Assert.False(HitPan.Web.Services.DataResetMenuRule.ShowResetLink(HitPan.Web.Services.DataResetMenuRule.Closed),
            "🔴 「closed」 인데 메뉴 규칙이 보인다고 한다 — 닫힌 판에서 사이드바에 초기화 한 줄이 남는다.");
        // 관리자 자식도 닫힌 판에서는 closed(판정이 깃발에서 먼저 끝난다 · 메뉴는 어차피 안 보임 · 기존 level · isAdmin 칸은 그대로).
        var mgrClosed = await PipeAsync(db, "GET", "/api/users/my-level", manager);
        Assert.Contains("\"isAdmin\":true", mgrClosed.Body);
        Assert.Contains($"\"dataResetGate\":\"{HitPan.Web.Services.DataResetMenuRule.Closed}\"", mgrClosed.Body);

        // 대조 — 같은 회사 · 같은 대표를 열린 가짜로 다시 부르면 allowed(닫힘은 판정 앞 깃발에서만 온다).
        _resetAvailability = DataResetAvailabilityForTest.OpenForTest;
        var open = await PipeAsync(db, "GET", "/api/users/my-level", owner);
        Assert.Contains($"\"dataResetGate\":\"{HitPan.Web.Services.DataResetMenuRule.Allowed}\"", open.Body);
        var mgrOpen = await PipeAsync(db, "GET", "/api/users/my-level", manager);
        Assert.Contains($"\"dataResetGate\":\"{HitPan.Web.Services.DataResetMenuRule.NotParent}\"", mgrOpen.Body);
    }
}
