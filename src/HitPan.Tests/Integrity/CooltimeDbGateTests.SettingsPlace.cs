using Dapper;
using HitPan.Application.DTOs.Settings;
using HitPan.Application.Services;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🆕 2026-11-28 쿨타임1b — <b>G-D1 ~ G-D6</b>: 자동 로그아웃 설정 좁은 저장(<see cref="SettingsService.SaveCooltimeOnlyAsync"/>)을 <b>실제 DB</b>로 잰다.
/// </summary>
/// <remarks>
/// 근거: 설계 <c>docs/설계/erp/쿨타임_설정자리옮김_설계_20261128.md</c> §5 · 작업지시서 <c>docs/운영기록/20261128작_쿨타임1b_설정자리옮김_작업지시서.md</c> ④.
/// 판정은 제품 공개 메서드(<see cref="SettingsService.SaveCooltimeOnlyAsync"/> · <see cref="SettingsService.GetCooltimeAsync"/>)가 한다 — 이 파일의 SQL 은 준비와 읽기뿐.
/// 🔴 이 PC(3306 = 실물)에서는 돌리지 않는다 — CI <c>db-gate</c> 가 유일한 계측 경로(같은 파일 머리 <c>Ready</c> 규칙 그대로).
/// </remarks>
public sealed partial class CooltimeDbGateTests
{
    private sealed class SettingsPlaceRow
    {
        public long CooltimeEnabled { get; set; }
        public TimeSpan? CooltimeAt { get; set; }
        public TimeSpan? RegularLeaveTime { get; set; }
        public DateTime? CooltimeChangedAt { get; set; }
        public int PriceDeviationLimit { get; set; }
        public string IndustryType { get; set; } = "";
        public long EnforceOnePcLogin { get; set; }
        public long EnforceSessionValidity { get; set; }
        public long EnforceTenantSessionLimit { get; set; }
        public long AllowForcePriceInput { get; set; }
        public string StockEvalMethod { get; set; } = "";
    }

    private static Task<SettingsPlaceRow?> SettingsPlaceRowAsync(MySqlConnection db, string tenantId) =>
        db.QueryFirstOrDefaultAsync<SettingsPlaceRow?>(@"
            SELECT CAST(cooltime_enabled AS SIGNED) AS CooltimeEnabled, cooltime_at AS CooltimeAt,
                   regular_leave_time AS RegularLeaveTime, cooltime_changed_at AS CooltimeChangedAt,
                   price_deviation_limit AS PriceDeviationLimit, industry_type AS IndustryType,
                   CAST(enforce_one_pc_login AS SIGNED) AS EnforceOnePcLogin,
                   CAST(enforce_session_validity AS SIGNED) AS EnforceSessionValidity,
                   CAST(enforce_tenant_session_limit AS SIGNED) AS EnforceTenantSessionLimit,
                   CAST(allow_force_price_input AS SIGNED) AS AllowForcePriceInput,
                   stock_eval_method AS StockEvalMethod
            FROM tenant_settings WHERE tenant_id = @T", new { T = tenantId });

    [Fact(DisplayName = "G-D1 행 없는 회사 → 좁은 저장(켬 22:00 · 18:00) → 행 생김 · 세 칸 맞음 · 다른 칸 = DDL 기본값 · changed_at 채워짐 · 읽기 = 같은 값")]
    public async Task G_D1_처음_행을_만든다()
    {
        if (!Ready("G-D1")) return;
        await using var db = await OpenAsync();
        var svc = new SettingsService(db);
        Assert.Null(await SettingsPlaceRowAsync(db, _tenantA));

        // 짝(행 없음 읽기) = 끔 · 빈 시각
        var before = await svc.GetCooltimeAsync(_tenantA);
        Assert.False(before.CooltimeEnabled);
        Assert.Null(before.CooltimeAt);
        Assert.Null(before.RegularLeaveTime);

        await svc.SaveCooltimeOnlyAsync(new UpdateCooltimeSettingsDto
        { CooltimeEnabled = true, CooltimeAt = "22:00", RegularLeaveTime = "18:00" }, _tenantA);

        var r = await SettingsPlaceRowAsync(db, _tenantA);
        Assert.NotNull(r);
        Assert.Equal(1, r!.CooltimeEnabled);
        Assert.Equal(new TimeSpan(22, 0, 0), r.CooltimeAt);
        Assert.Equal(new TimeSpan(18, 0, 0), r.RegularLeaveTime);
        Assert.NotNull(r.CooltimeChangedAt);
        // 다른 칸 = DDL 기본값(「행 없음」 읽기와 같은 동작 · 설계 F9)
        Assert.Equal(50, r.PriceDeviationLimit);
        Assert.Equal("general", r.IndustryType);
        Assert.Equal(1, r.EnforceOnePcLogin);
        Assert.Equal(1, r.EnforceSessionValidity);
        Assert.Equal(0, r.EnforceTenantSessionLimit);
        Assert.Equal(1, r.AllowForcePriceInput);
        Assert.Equal("fifo", r.StockEvalMethod);

        var after = await svc.GetCooltimeAsync(_tenantA);
        Assert.True(after.CooltimeEnabled);
        Assert.Equal("22:00", after.CooltimeAt);
        Assert.Equal("18:00", after.RegularLeaveTime);
    }

    [Fact(DisplayName = "G-D2 행 있는 회사(price_deviation_limit 77 · industry_type food · enforce_one_pc_login 0) → 좁은 저장 → 셋 다 그대로 · 세 칸만 바뀜")]
    public async Task G_D2_다른_칸을_덮지_않는다()
    {
        if (!Ready("G-D2")) return;
        await using var db = await OpenAsync();
        await db.ExecuteAsync(@"
            INSERT INTO tenant_settings (tenant_id, price_deviation_limit, industry_type, enforce_one_pc_login, stock_eval_method)
            VALUES (@T, 77, 'food', 0, 'avg')", new { T = _tenantA });

        await new SettingsService(db).SaveCooltimeOnlyAsync(new UpdateCooltimeSettingsDto
        { CooltimeEnabled = true, CooltimeAt = "21:30", RegularLeaveTime = "18:00" }, _tenantA);

        var r = await SettingsPlaceRowAsync(db, _tenantA);
        Assert.NotNull(r);
        Assert.Equal(77, r!.PriceDeviationLimit);
        Assert.Equal("food", r.IndustryType);
        Assert.Equal(0, r.EnforceOnePcLogin);
        Assert.Equal("avg", r.StockEvalMethod);
        Assert.Equal(1, r.CooltimeEnabled);
        Assert.Equal(new TimeSpan(21, 30, 0), r.CooltimeAt);
    }

    [Fact(DisplayName = "G-D3 켬 + 시각 빈칸 → 거절(문구 상수) · 아무 칸도 안 바뀜 / 모양 틀린 시각 → 거절 · 짝(시각 채움) = 저장")]
    public async Task G_D3_켬인데_시각이_비면_거절()
    {
        if (!Ready("G-D3")) return;
        await using var db = await OpenAsync();
        var svc = new SettingsService(db);
        await SettingsAsync(db, _tenantA, false, new TimeSpan(22, 0, 0), null);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => svc.SaveCooltimeOnlyAsync(
            new UpdateCooltimeSettingsDto { CooltimeEnabled = true }, _tenantA));
        Assert.Equal(SettingsService.CooltimeNeedsTimesMessage, ex.Message);

        var ex2 = await Assert.ThrowsAsync<ArgumentException>(() => svc.SaveCooltimeOnlyAsync(
            new UpdateCooltimeSettingsDto { CooltimeEnabled = true, CooltimeAt = "25:00", RegularLeaveTime = "18:00" }, _tenantA));
        Assert.Equal(SettingsService.CooltimeBadTimeMessage, ex2.Message);

        var r = await SettingsPlaceRowAsync(db, _tenantA);
        Assert.Equal(0, r!.CooltimeEnabled);
        Assert.Equal(new TimeSpan(22, 0, 0), r.CooltimeAt);
        Assert.Null(r.RegularLeaveTime);

        // 짝 — 빠진 한 칸을 채우면 저장된다(거절이 「늘 거절」 이 아님)
        await svc.SaveCooltimeOnlyAsync(new UpdateCooltimeSettingsDto { CooltimeEnabled = true, RegularLeaveTime = "18:00" }, _tenantA);
        var ok = await SettingsPlaceRowAsync(db, _tenantA);
        Assert.Equal(1, ok!.CooltimeEnabled);
        Assert.Equal(new TimeSpan(18, 0, 0), ok.RegularLeaveTime);
    }

    [Fact(DisplayName = "G-D4 회사 A 저장 → 회사 B 행 손 0(값 · changed_at 그대로)")]
    public async Task G_D4_다른_회사는_손대지_않는다()
    {
        if (!Ready("G-D4")) return;
        await using var db = await OpenAsync();
        var bChanged = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        await SettingsAsync(db, _tenantB, true, new TimeSpan(20, 0, 0), new TimeSpan(17, 0, 0), changedAtUtc: bChanged);

        await new SettingsService(db).SaveCooltimeOnlyAsync(new UpdateCooltimeSettingsDto
        { CooltimeEnabled = false, CooltimeAt = "23:00", RegularLeaveTime = "19:00" }, _tenantA);

        var a = await SettingsPlaceRowAsync(db, _tenantA);
        Assert.Equal(new TimeSpan(23, 0, 0), a!.CooltimeAt);   // 짝 — A 는 실제로 저장됐다
        var b = await SettingsPlaceRowAsync(db, _tenantB);
        Assert.Equal(1, b!.CooltimeEnabled);
        Assert.Equal(new TimeSpan(20, 0, 0), b.CooltimeAt);
        Assert.Equal(new TimeSpan(17, 0, 0), b.RegularLeaveTime);
        Assert.Equal(bChanged, DateTime.SpecifyKind(b.CooltimeChangedAt!.Value, DateTimeKind.Utc));
    }

    [Fact(DisplayName = "G-D5 켬 → 끔 좁은 저장 = 그 회사 PC 세션 물음·미룸 칸 비움(E-3) · 다른 회사 세션 그대로")]
    public async Task G_D5_켬끔때_물음칸을_비운다()
    {
        if (!Ready("G-D5")) return;
        await using var db = await OpenAsync();
        var svc = new SettingsService(db);
        await svc.SaveCooltimeOnlyAsync(new UpdateCooltimeSettingsDto
        { CooltimeEnabled = true, CooltimeAt = "22:00", RegularLeaveTime = "18:00" }, _tenantA);

        var u = await InsertUserAsync(db, _tenantA);
        var sid = await InsertSessionAsync(db, _tenantA, u, "pc", askedAgoSec: 10);
        await db.ExecuteAsync("UPDATE user_sessions SET cooltime_snooze_until = UTC_TIMESTAMP(6) + INTERVAL 1 HOUR WHERE session_id = @S", new { S = sid });
        var uB = await InsertUserAsync(db, _tenantB);
        var sidB = await InsertSessionAsync(db, _tenantB, uB, "pc", askedAgoSec: 10);

        await svc.SaveCooltimeOnlyAsync(new UpdateCooltimeSettingsDto { CooltimeEnabled = false }, _tenantA);

        Assert.Null(await AskedAtAsync(db, sid));
        Assert.Null(await db.ExecuteScalarAsync<DateTime?>(
            "SELECT cooltime_snooze_until FROM user_sessions WHERE session_id = @S", new { S = sid }));
        Assert.NotNull(await AskedAtAsync(db, sidB));   // 다른 회사 = 그대로
    }

    [Fact(DisplayName = "G-D6 {cooltimeEnabled:false} 만(나머지 null) → 시각 두 칸 그대로 · 세 칸 모두 null = 쓰지 않음")]
    public async Task G_D6_null_은_그대로()
    {
        if (!Ready("G-D6")) return;
        await using var db = await OpenAsync();
        var svc = new SettingsService(db);
        await svc.SaveCooltimeOnlyAsync(new UpdateCooltimeSettingsDto
        { CooltimeEnabled = true, CooltimeAt = "22:00", RegularLeaveTime = "18:00" }, _tenantA);

        await svc.SaveCooltimeOnlyAsync(new UpdateCooltimeSettingsDto { CooltimeEnabled = false }, _tenantA);
        var r = await SettingsPlaceRowAsync(db, _tenantA);
        Assert.Equal(0, r!.CooltimeEnabled);
        Assert.Equal(new TimeSpan(22, 0, 0), r.CooltimeAt);
        Assert.Equal(new TimeSpan(18, 0, 0), r.RegularLeaveTime);

        // 세 칸 모두 null — 행 없는 회사 B 에 행을 만들지 않는다
        await svc.SaveCooltimeOnlyAsync(new UpdateCooltimeSettingsDto(), _tenantB);
        Assert.Null(await SettingsPlaceRowAsync(db, _tenantB));
    }
}
