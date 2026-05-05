using Dapper;
using HitPan.Application.Services;
using HitPan.IntegrationTests.Infrastructure;

namespace HitPan.IntegrationTests.Integrity;

/// <summary>
/// MonthlySummaryGuard 멱등성 통합 테스트 — 실제 MariaDB 검증
/// INSERT IGNORE 기반 멱등 보장이 실제 DB에서 작동하는지 확인
/// </summary>
[Collection("DB")]
public class MonthlySummaryIdempotencyTests : IAsyncLifetime
{
    private readonly DbFixture _db = new();
    private string _tenantId = "";

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        _tenantId = DbFixture.NewTestTenantId();
        await _db.InsertTestTenantAsync(_tenantId);
    }

    public async Task DisposeAsync()
    {
        await _db.CleanupTenantAsync(_tenantId);
        await _db.DisposeAsync();
    }

    [Fact(DisplayName = "IT-M01: 동일 sourceId로 TryApplyAsync 2회 호출 시 1회만 반영 (멱등 보장)")]
    public async Task TryApply_SameSource_AppliedOnce()
    {
        var sourceId = $"receipt-{Guid.NewGuid():N}";
        var date = DateTime.UtcNow;
        var amount = 100_000m;

        // 1회차
        var first = await MonthlySummaryGuard.TryApplyAsync(
            _db.Connection, null,
            _tenantId, date,
            "purchase_receipt_confirmed", sourceId,
            MonthlySummaryGuard.SummaryField.TotalPurchase, amount);

        // 2회차 — 같은 sourceId
        var second = await MonthlySummaryGuard.TryApplyAsync(
            _db.Connection, null,
            _tenantId, date,
            "purchase_receipt_confirmed", sourceId,
            MonthlySummaryGuard.SummaryField.TotalPurchase, amount);

        Assert.True(first, "1회차는 반드시 true(신규 반영)여야 한다");
        Assert.False(second, "2회차는 반드시 false(멱등 스킵)여야 한다");

        // DB에서 실제 합산값 확인 — 한 번만 반영됐어야 함
        var ym = date.ToString("yyyyMM");
        var totalPurchase = await _db.Connection.QueryFirstOrDefaultAsync<decimal>(
            "SELECT total_purchase FROM monthly_summary WHERE tenant_id = @T AND `year_month` = @Ym",
            new { T = _tenantId, Ym = ym });

        Assert.Equal(amount, totalPurchase);
    }

    [Fact(DisplayName = "IT-M02: 서로 다른 sourceId는 각각 독립 반영")]
    public async Task TryApply_DifferentSources_BothApplied()
    {
        var sourceId1 = $"receipt-{Guid.NewGuid():N}";
        var sourceId2 = $"receipt-{Guid.NewGuid():N}";
        var date = DateTime.UtcNow;
        var amount = 50_000m;

        await MonthlySummaryGuard.TryApplyAsync(
            _db.Connection, null, _tenantId, date,
            "purchase_receipt_confirmed", sourceId1,
            MonthlySummaryGuard.SummaryField.TotalPurchase, amount);

        await MonthlySummaryGuard.TryApplyAsync(
            _db.Connection, null, _tenantId, date,
            "purchase_receipt_confirmed", sourceId2,
            MonthlySummaryGuard.SummaryField.TotalPurchase, amount);

        var ym = date.ToString("yyyyMM");
        var totalPurchase = await _db.Connection.QueryFirstOrDefaultAsync<decimal>(
            "SELECT total_purchase FROM monthly_summary WHERE tenant_id = @T AND `year_month` = @Ym",
            new { T = _tenantId, Ym = ym });

        Assert.Equal(amount * 2, totalPurchase);
    }

    [Fact(DisplayName = "IT-M03: 매출(TotalSales)과 매입(TotalPurchase) 필드 독립 누적")]
    public async Task TryApply_SalesAndPurchase_IndependentFields()
    {
        var date = DateTime.UtcNow;

        await MonthlySummaryGuard.TryApplyAsync(
            _db.Connection, null, _tenantId, date,
            "delivery_confirmed", $"del-{Guid.NewGuid():N}",
            MonthlySummaryGuard.SummaryField.TotalSales, 200_000m);

        await MonthlySummaryGuard.TryApplyAsync(
            _db.Connection, null, _tenantId, date,
            "purchase_receipt_confirmed", $"rec-{Guid.NewGuid():N}",
            MonthlySummaryGuard.SummaryField.TotalPurchase, 80_000m);

        var ym = date.ToString("yyyyMM");
        var row = await _db.Connection.QueryFirstOrDefaultAsync<(decimal Sales, decimal Purchase)>(
            "SELECT total_sales AS Sales, total_purchase AS Purchase FROM monthly_summary WHERE tenant_id = @T AND `year_month` = @Ym",
            new { T = _tenantId, Ym = ym });

        Assert.Equal(200_000m, row.Sales);
        Assert.Equal(80_000m, row.Purchase);
    }

    [Fact(DisplayName = "IT-M04: 다른 tenant의 monthly_summary와 격리됨")]
    public async Task TryApply_TenantIsolation()
    {
        var otherTenant = DbFixture.NewTestTenantId();
        var date = DateTime.UtcNow;

        await MonthlySummaryGuard.TryApplyAsync(
            _db.Connection, null, _tenantId, date,
            "delivery_confirmed", $"del-{Guid.NewGuid():N}",
            MonthlySummaryGuard.SummaryField.TotalSales, 100_000m);

        await MonthlySummaryGuard.TryApplyAsync(
            _db.Connection, null, otherTenant, date,
            "delivery_confirmed", $"del-{Guid.NewGuid():N}",
            MonthlySummaryGuard.SummaryField.TotalSales, 999_000m);

        var ym = date.ToString("yyyyMM");
        var mySales = await _db.Connection.QueryFirstOrDefaultAsync<decimal>(
            "SELECT total_sales FROM monthly_summary WHERE tenant_id = @T AND `year_month` = @Ym",
            new { T = _tenantId, Ym = ym });

        Assert.Equal(100_000m, mySales);  // 다른 tenant 금액이 섞이면 안 됨

        // 정리
        await _db.Connection.ExecuteAsync("DELETE FROM monthly_summary WHERE tenant_id = @T", new { T = otherTenant });
        await _db.Connection.ExecuteAsync("DELETE FROM monthly_summary_sources WHERE tenant_id = @T", new { T = otherTenant });
    }
}
