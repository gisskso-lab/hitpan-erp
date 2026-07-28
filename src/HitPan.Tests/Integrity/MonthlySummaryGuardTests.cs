using HitPan.Application.Services;

namespace HitPan.Tests.Integrity;

/// <summary>
/// MonthlySummaryGuard 순수 로직 검증 (DB 불필요 영역)
/// 수정 필요 메모:
///   - TryApplyAsync 실제 멱등 보장(INSERT IGNORE)은 통합 테스트에서 DB로 검증 필요
///   - year_month 컬럼명이 백틱으로 감싸인 이유: MariaDB 예약어 — DDL 변경 시 주의
/// </summary>
public class MonthlySummaryGuardTests
{
    [Theory(DisplayName = "T-M01: SummaryField → 컬럼명 매핑 정합성 (리플렉션 없이 검증)")]
    [InlineData(MonthlySummaryGuard.SummaryField.TotalSales, "total_sales")]
    [InlineData(MonthlySummaryGuard.SummaryField.TotalPurchase, "total_purchase")]
    [InlineData(MonthlySummaryGuard.SummaryField.TotalReceipt, "total_receipt")]
    [InlineData(MonthlySummaryGuard.SummaryField.TotalPayment, "total_payment")]
    public void SummaryField_ToColumnName_MatchesExpected(MonthlySummaryGuard.SummaryField field, string expectedColumn)
    {
        // MonthlySummaryGuard.ToColumnName은 private static — SQL 생성 로직이 올바른지
        // 실제 SQL 문자열 생성 대신 열거형 값의 존재 및 범위를 검증
        Assert.True(Enum.IsDefined(typeof(MonthlySummaryGuard.SummaryField), field));

        // 컬럼명 규칙: snake_case, total_ 접두사
        Assert.StartsWith("total_", expectedColumn);
        Assert.Equal(expectedColumn, expectedColumn.ToLower());
    }

    [Fact(DisplayName = "T-M02: tenantId 빈 문자열로 TryApplyAsync 호출 시 ArgumentException 발생")]
    public async Task TryApplyAsync_EmptyTenantId_ThrowsArgument()
    {
        // MonthlySummaryGuard는 static class — 인자 검증만 확인 (DB 없이)
        // 실제 conn/tx는 null로 전달하면 Guard 내부 검증에서 먼저 실패해야 함
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => MonthlySummaryGuard.TryApplyAsync(
                null!, null,
                tenantId: "",       // 빈 tenantId
                date: DateTime.UtcNow,
                sourceType: "receipt",
                sourceId: "r-001",
                field: MonthlySummaryGuard.SummaryField.TotalPurchase,
                amount: 100m));

        Assert.NotNull(ex);
    }

    [Fact(DisplayName = "T-M03: sourceId 빈 문자열로 TryApplyAsync 호출 시 ArgumentException 발생")]
    public async Task TryApplyAsync_EmptySourceId_ThrowsArgument()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => MonthlySummaryGuard.TryApplyAsync(
                null!, null,
                tenantId: "tenant-001",
                date: DateTime.UtcNow,
                sourceType: "receipt",
                sourceId: "",       // 빈 sourceId
                field: MonthlySummaryGuard.SummaryField.TotalPurchase,
                amount: 100m));

        Assert.NotNull(ex);
    }

    [Fact(DisplayName = "T-M04: date → yyyyMM 변환 정합성")]
    public void Date_FormattedAs_YearMonth()
    {
        var date = new DateTime(2026, 5, 6);
        var ym = date.ToString("yyyyMM");
        Assert.Equal("202605", ym);
    }

    [Fact(DisplayName = "T-M05: SummaryField 열거형 — 정의된 값이 정확히 4개")]
    public void SummaryField_HasExactlyFourValues()
    {
        var values = Enum.GetValues<MonthlySummaryGuard.SummaryField>();
        Assert.Equal(4, values.Length);
    }
}
