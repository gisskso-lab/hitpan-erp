using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Approval;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>월마감 서비스 — 마감/재오픈/차단 체크</summary>
public class MonthlyClosingService : IMonthlyClosingService
{
    private readonly IDbConnection _db;
    private readonly IAuditService _audit;

    // 상태 라벨
    private static readonly Dictionary<string, string> StatusLabels = new()
    {
        ["open"] = "미마감",
        ["closed"] = "마감",
        ["reopened"] = "재오픈"
    };

    public MonthlyClosingService(IDbConnection db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<List<MonthlyClosingDto>> GetStatusAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        // 최근 12개월 월 목록 생성
        var months = new List<string>();
        var now = DateTime.Today;
        for (var i = 0; i < 12; i++)
        {
            var m = now.AddMonths(-i);
            months.Add(m.ToString("yyyyMM"));
        }

        // DB에 저장된 마감 정보 조회
        var existing = (await _db.QueryAsync<MonthlyClosingDto>(new CommandDefinition(
            """
            SELECT closing_id AS ClosingId, `year_month` AS YearMonth, status AS Status,
                   closed_at AS ClosedAt, closed_by AS ClosedBy,
                   reopened_at AS ReopenedAt, reopened_by AS ReopenedBy,
                   reopen_reason AS ReopenReason,
                   sales_amount AS SalesAmount, purchase_amount AS PurchaseAmount,
                   receipt_amount AS ReceiptAmount, payment_amount AS PaymentAmount,
                   memo AS Memo
            FROM monthly_closing
            WHERE tenant_id = @TenantId
            ORDER BY `year_month` DESC
            """,
            new { TenantId = tenantId }, cancellationToken: ct))).ToDictionary(x => x.YearMonth);

        // 12개월 채워서 반환
        var result = new List<MonthlyClosingDto>();
        foreach (var ym in months)
        {
            if (existing.TryGetValue(ym, out var dto))
            {
                dto.YearMonthLabel = FormatYm(ym);
                dto.StatusLabel = StatusLabels.GetValueOrDefault(dto.Status, dto.Status);
                result.Add(dto);
            }
            else
            {
                // 마감 정보 없음 = 미마감 (open)
                // monthly_summary에서 금액 가져오기
                var summary = await _db.QueryFirstOrDefaultAsync<(decimal Sales, decimal Purchase, decimal Receipt, decimal Payment)>(
                    new CommandDefinition(
                        "SELECT COALESCE(total_sales,0) AS Sales, COALESCE(total_purchase,0) AS Purchase, COALESCE(total_receipt,0) AS Receipt, COALESCE(total_payment,0) AS Payment FROM monthly_summary WHERE tenant_id = @TenantId AND `year_month` = @Ym",
                        new { TenantId = tenantId, Ym = ym }, cancellationToken: ct));

                result.Add(new MonthlyClosingDto
                {
                    YearMonth = ym,
                    YearMonthLabel = FormatYm(ym),
                    Status = "open",
                    StatusLabel = "미마감",
                    SalesAmount = summary.Sales,
                    PurchaseAmount = summary.Purchase,
                    ReceiptAmount = summary.Receipt,
                    PaymentAmount = summary.Payment
                });
            }
        }
        return result;
    }

    public async Task CloseAsync(CloseMonthRequest request, string tenantId, string userId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        // 이전 월이 미마감이면 차단 (순차 마감 원칙)
        var prevYm = GetPrevYm(request.YearMonth);
        var prevStatus = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            "SELECT status FROM monthly_closing WHERE tenant_id = @TenantId AND `year_month` = @Ym",
            new { TenantId = tenantId, Ym = prevYm }, cancellationToken: ct));

        // 이전 월에 거래가 있는데 마감 안 했으면 경고 (첫 달은 예외)
        if (prevStatus is null)
        {
            var prevHasData = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM monthly_summary WHERE tenant_id = @TenantId AND `year_month` = @Ym AND (total_sales > 0 OR total_purchase > 0)",
                new { TenantId = tenantId, Ym = prevYm }, cancellationToken: ct));
            if (prevHasData > 0)
                throw new InvalidOperationException($"{FormatYm(prevYm)} 마감을 먼저 완료해주세요.");
        }
        else if (prevStatus != "closed")
        {
            throw new InvalidOperationException($"{FormatYm(prevYm)} 마감을 먼저 완료해주세요.");
        }

        // monthly_summary에서 금액 스냅샷
        var summary = await _db.QueryFirstOrDefaultAsync<(decimal Sales, decimal Purchase, decimal Receipt, decimal Payment)>(
            new CommandDefinition(
                "SELECT COALESCE(total_sales,0) AS Sales, COALESCE(total_purchase,0) AS Purchase, COALESCE(total_receipt,0) AS Receipt, COALESCE(total_payment,0) AS Payment FROM monthly_summary WHERE tenant_id = @TenantId AND `year_month` = @Ym",
                new { TenantId = tenantId, Ym = request.YearMonth }, cancellationToken: ct));

        // UPSERT
        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO monthly_closing
              (closing_id, tenant_id, `year_month`, status, closed_at, closed_by,
               sales_amount, purchase_amount, receipt_amount, payment_amount, memo)
            VALUES
              (UUID(), @TenantId, @Ym, 'closed', NOW(6), @UserId,
               @Sales, @Purchase, @Receipt, @Payment, @Memo)
            ON DUPLICATE KEY UPDATE
              status = 'closed', closed_at = NOW(6), closed_by = @UserId,
              sales_amount = @Sales, purchase_amount = @Purchase,
              receipt_amount = @Receipt, payment_amount = @Payment,
              memo = @Memo, reopened_at = NULL, reopened_by = NULL, reopen_reason = NULL
            """,
            new
            {
                TenantId = tenantId, Ym = request.YearMonth, UserId = userId,
                summary.Sales, summary.Purchase, summary.Receipt, summary.Payment,
                Memo = request.Memo
            }, cancellationToken: ct));

        // 감사로그 — 마감 확정 (DB-16 미실행 환경에서는 서비스 내부에서 스킵)
        var afterJson = $"{{\"status\":\"closed\",\"sales\":{summary.Sales},\"purchase\":{summary.Purchase},\"receipt\":{summary.Receipt},\"payment\":{summary.Payment}}}";
        await _audit.LogAsync("state_change", "monthly_closing", request.YearMonth,
            beforeJson: "{\"status\":\"open\"}", afterJson: afterJson, ct: ct);
    }

    public async Task ReopenAsync(ReopenMonthRequest request, string tenantId, string userId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new InvalidOperationException("재오픈 사유를 입력해주세요.");

        // 이후 월이 마감되어 있으면 재오픈 불가
        var nextYm = GetNextYm(request.YearMonth);
        var nextStatus = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            "SELECT status FROM monthly_closing WHERE tenant_id = @TenantId AND `year_month` = @Ym AND status = 'closed'",
            new { TenantId = tenantId, Ym = nextYm }, cancellationToken: ct));
        if (nextStatus == "closed")
            throw new InvalidOperationException($"{FormatYm(nextYm)}이 마감 상태이므로 재오픈할 수 없습니다. 이후 월을 먼저 재오픈해주세요.");

        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE monthly_closing SET
              status = 'reopened', reopened_at = NOW(6), reopened_by = @UserId, reopen_reason = @Reason
            WHERE tenant_id = @TenantId AND `year_month` = @Ym AND status = 'closed'
            """,
            new { TenantId = tenantId, Ym = request.YearMonth, UserId = userId, Reason = request.Reason },
            cancellationToken: ct));

        // 감사로그 — 재오픈 (사유 포함)
        await _audit.LogAsync("state_change", "monthly_closing", request.YearMonth,
            beforeJson: "{\"status\":\"closed\"}",
            afterJson: "{\"status\":\"reopened\"}",
            reason: request.Reason, ct: ct);
    }

    public async Task<bool> IsClosedAsync(string tenantId, DateTime date, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var ym = date.ToString("yyyyMM");
        var status = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            "SELECT status FROM monthly_closing WHERE tenant_id = @TenantId AND `year_month` = @Ym",
            new { TenantId = tenantId, Ym = ym }, cancellationToken: ct));
        return status == "closed";
    }

    // ── 헬퍼 ──

    private static string FormatYm(string ym) =>
        ym.Length == 6 ? $"{ym[..4]}년 {ym[4..]}월" : ym;

    private static string GetPrevYm(string ym)
    {
        var y = int.Parse(ym[..4]);
        var m = int.Parse(ym[4..]);
        if (m == 1) { y--; m = 12; } else m--;
        return $"{y}{m:D2}";
    }

    private static string GetNextYm(string ym)
    {
        var y = int.Parse(ym[..4]);
        var m = int.Parse(ym[4..]);
        if (m == 12) { y++; m = 1; } else m++;
        return $"{y}{m:D2}";
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open) return;
        if (_db is DbConnection c) { await c.OpenAsync(ct); return; }
        _db.Open();
    }
}
