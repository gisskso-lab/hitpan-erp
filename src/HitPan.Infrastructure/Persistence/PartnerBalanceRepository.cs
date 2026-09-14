using HitPan.Application.DTOs.Partner;
using HitPan.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HitPan.Infrastructure.Persistence;

public class PartnerBalanceRepository : IPartnerBalanceRepository
{
    private readonly AppDbContext _dbContext;

    public PartnerBalanceRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PartnerBalanceDto?> GetBalanceAsync(string tenantId, string partnerId, CancellationToken ct = default)
    {
        var query = _dbContext.Database.SqlQuery<PartnerBalanceDto>(
            $"""
             SELECT
                v.partner_id AS PartnerId,
                v.partner_name AS PartnerName,
                v.tenant_id AS TenantId,
                v.total_sales AS TotalSales,
                v.total_received AS TotalReceived,
                v.receivable_balance + GREATEST(COALESCE(plb.balance_amount, 0), 0) AS ReceivableBalance,
                v.total_purchase AS TotalPurchase,
                v.total_paid AS TotalPaid,
                v.payable_balance + GREATEST(-COALESCE(plb.balance_amount, 0), 0) AS PayableBalance,
                v.calculated_at AS CalculatedAt
             FROM v_partner_balance v
             LEFT JOIN partner_legacy_balances plb
               ON plb.tenant_id = v.tenant_id
              AND plb.partner_id = v.partner_id
             WHERE v.partner_id = {partnerId}
               AND v.tenant_id = {tenantId}
             """);
        // 🔴 20260915작1 갈래 E (설계 §17) — 「이전 프로그램 이월」 잔액을 한 번 더한다(+ 미수 · − 미지급).
        //   ⚠️ v_partner_balance 는 수주·발주(sales_orders·purchase_orders) 기준이라 이관 행을 뷰 밖에서 뺄 수 없다 — 개발명세서 §4 미완.

        return await query.FirstOrDefaultAsync(ct);
    }
}
