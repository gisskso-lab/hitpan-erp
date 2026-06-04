using Dapper;
using MySqlConnector;

namespace HitPan.Backoffice.API.Services;

// 대리점 월별 정산 산출 (사장님 결재 2026-06-04, W9)
//
// 산출 룰:
//   1) 해당 월 reseller_id별 tenants에서 발생한 결제 합산 (tenant_payments.status='approved')
//   2) commission_amount = gross × (resellers.commission_rate ?? 기본 0.15)
//   3) incentive_amount = 본 차수 0 (추후 룰 추가 — 분기·연간 보너스)
//   4) draft 상태로 INSERT, settlement_lines에 산출 근거 박제
//   5) 동일 reseller_id + month 중복 시 BadRequest (UNIQUE 키)
//
// 헌법 정합:
//   #3 INSERT ONLY (확정 후 UPDATE 금지)
//   #4 금액은 DECIMAL
//   #18·#22 메타만
public interface IResellerSettlementCalculator
{
    Task<(bool ok, long? settlementId, string? error)> CalculateAsync(
        string resellerId, string month, string actorUserId, CancellationToken ct);
}

public class ResellerSettlementCalculator : IResellerSettlementCalculator
{
    private readonly IConfiguration _config;
    private readonly ILogger<ResellerSettlementCalculator> _logger;

    private const decimal DefaultCommissionRate = 0.15m;

    public ResellerSettlementCalculator(IConfiguration config, ILogger<ResellerSettlementCalculator> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<(bool ok, long? settlementId, string? error)> CalculateAsync(
        string resellerId, string month, string actorUserId, CancellationToken ct)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(month, @"^\d{4}-\d{2}$"))
            return (false, null, "month 형식 오류 (YYYY-MM)");

        try
        {
            var cs = _config.GetConnectionString("BackofficeDb")
                     ?? _config.GetConnectionString("Default")
                     ?? throw new InvalidOperationException("ConnectionStrings:BackofficeDb 미설정");
            await using var db = new MySqlConnection(cs);
            await db.OpenAsync(ct);

            // 중복 검사
            var exists = await db.QueryFirstOrDefaultAsync<long?>(@"
                SELECT settlement_id FROM reseller_settlements
                WHERE reseller_id = @Rid AND settlement_month = @Month",
                new { Rid = resellerId, Month = month });
            if (exists is not null)
                return (false, exists, "이미 해당 월 정산이 있습니다.");

            // 수수료율 (resellers에 컬럼 없으면 기본 적용)
            var rate = DefaultCommissionRate;

            // 해당 월 결제 집계 (tenant_payments는 signup_token으로 연결, 따라서 landing_signups → tenants 조인)
            var lines = (await db.QueryAsync<LineDraft>(@"
                SELECT
                    CAST(t.tenant_id AS CHAR) AS TenantId,
                    t.tenant_code AS TenantCode,
                    t.company_name AS CompanyName,
                    COALESCE(SUM(tp.amount), 0) AS PaymentAmount
                FROM tenants t
                LEFT JOIN landing_signups ls ON ls.company_name = t.company_name
                LEFT JOIN tenant_payments tp ON tp.signup_token = ls.signup_token
                    AND tp.status = 'approved'
                    AND DATE_FORMAT(COALESCE(tp.approved_at, tp.created_at), '%Y-%m') = @Month
                WHERE t.reseller_id = @Rid
                GROUP BY t.tenant_id, t.tenant_code, t.company_name",
                new { Rid = resellerId, Month = month })).ToList();

            var gross = lines.Sum(l => l.PaymentAmount);
            var commission = Math.Round(gross * rate, 2);
            var tenantCount = lines.Count(l => l.PaymentAmount > 0);

            using var tx = await db.BeginTransactionAsync(ct);
            try
            {
                var settlementId = await db.ExecuteScalarAsync<long>(@"
                    INSERT INTO reseller_settlements
                        (reseller_id, settlement_month, tenant_count, gross_amount,
                         commission_rate, commission_amount, incentive_amount, total_payable,
                         status, created_at)
                    VALUES
                        (@Rid, @Month, @TenantCount, @Gross,
                         @Rate, @Commission, 0, @Commission,
                         'draft', NOW(6));
                    SELECT LAST_INSERT_ID();",
                    new { Rid = resellerId, Month = month, TenantCount = tenantCount,
                          Gross = gross, Rate = rate, Commission = commission },
                    tx);

                foreach (var l in lines.Where(x => x.PaymentAmount > 0))
                {
                    await db.ExecuteAsync(@"
                        INSERT INTO reseller_settlement_lines
                            (settlement_id, tenant_id, tenant_code, company_name,
                             payment_amount, commission_amount, created_at)
                        VALUES
                            (@SettlementId, @TenantId, @TenantCode, @CompanyName,
                             @PaymentAmount, @Comm, NOW(6))",
                        new { SettlementId = settlementId, l.TenantId, l.TenantCode,
                              l.CompanyName, l.PaymentAmount,
                              Comm = Math.Round(l.PaymentAmount * rate, 2) },
                        tx);
                }

                await tx.CommitAsync(ct);
                _logger.LogInformation("[Settlement] 산출 완료 reseller={Rid} month={Month} gross={Gross} commission={Comm}",
                    resellerId, month, gross, commission);
                return (true, settlementId, null);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Settlement] 산출 실패 reseller={Rid} month={Month}", resellerId, month);
            return (false, null, "산출 중 오류가 발생했습니다.");
        }
    }

    private class LineDraft
    {
        public string TenantId { get; set; } = "";
        public string TenantCode { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public decimal PaymentAmount { get; set; }
    }
}
