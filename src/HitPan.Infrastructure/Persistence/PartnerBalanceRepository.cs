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
                partner_id AS PartnerId,
                partner_name AS PartnerName,
                tenant_id AS TenantId,
                total_sales AS TotalSales,
                total_received AS TotalReceived,
                receivable_balance AS ReceivableBalance,
                total_purchase AS TotalPurchase,
                total_paid AS TotalPaid,
                payable_balance AS PayableBalance,
                calculated_at AS CalculatedAt
             FROM v_partner_balance
             WHERE partner_id = {partnerId}
               AND tenant_id = {tenantId}
             """);

        return await query.FirstOrDefaultAsync(ct);
    }
}
