using System.Data;
using Dapper;

namespace HitPan.Application.Services;

/// <summary>결재 트리거 + 월마감 체크 공통 헬퍼</summary>
public static class ApprovalTriggerHelper
{
    /// <summary>해당 날짜의 월이 마감되었으면 예외 발생</summary>
    public static async Task EnsureNotClosedAsync(IDbConnection db, string tenantId, DateTime date, CancellationToken ct)
    {
        var ym = date.ToString("yyyyMM");
        var status = await db.QueryFirstOrDefaultAsync<string>(
            new CommandDefinition(
                "SELECT status FROM monthly_closing WHERE tenant_id = @TenantId AND `year_month` = @Ym",
                new { TenantId = tenantId, Ym = ym }, cancellationToken: ct));
        if (status == "closed")
            throw new InvalidOperationException($"{ym[..4]}년 {ym[4..]}월은 마감된 기간입니다. 전표를 수정할 수 없습니다.");
    }

    /// <summary>
    /// [Deprecated 작4 P0-4] 매출/매입 확정 시 monthly_summary 갱신.
    /// 멱등 보장이 없어 같은 source 두 번 가산되는 위험. 호출처 0건이지만 안전 차원에서 Obsolete 마킹.
    /// 신규 호출은 <see cref="MonthlySummaryGuard.TryApplyAsync"/>를 사용할 것.
    /// </summary>
    [Obsolete("멱등 보장 없음. MonthlySummaryGuard.TryApplyAsync를 사용하세요. (작4 P0-4)", error: false)]
    public static async Task UpdateMonthlySummaryAsync(IDbConnection db, string tenantId, DateTime date, decimal salesAmount, decimal purchaseAmount, CancellationToken ct)
    {
        var ym = date.ToString("yyyyMM");
        await db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO monthly_summary (summary_id, tenant_id, `year_month`, total_sales, total_purchase, total_receipt, total_payment, last_updated_at)
            VALUES (UUID(), @TenantId, @Ym, @Sales, @Purchase, 0, 0, NOW(6))
            ON DUPLICATE KEY UPDATE
              total_sales = total_sales + @Sales,
              total_purchase = total_purchase + @Purchase,
              last_updated_at = NOW(6)
            """,
            new { TenantId = tenantId, Ym = ym, Sales = salesAmount, Purchase = purchaseAmount },
            cancellationToken: ct));
    }

    /// <summary>결재 설정 ON이고 기준금액 이상이면 결재 문서 자동 생성</summary>
    public static async Task TryCreateApprovalAsync(
        IDbConnection db, string docType, string refId, string refNo, string title,
        decimal amount, string tenantId, string requesterId, string requesterName,
        CancellationToken ct)
    {
        // 결재 설정 확인
        var setting = await db.QueryFirstOrDefaultAsync<(bool IsEnabled, decimal Threshold, bool AutoBelow)>(
            new CommandDefinition(
                "SELECT is_enabled AS IsEnabled, threshold_amount AS Threshold, auto_approve_below AS AutoBelow FROM approval_settings WHERE tenant_id = @TenantId AND doc_type = @DocType",
                new { TenantId = tenantId, DocType = docType }, cancellationToken: ct));

        if (!setting.IsEnabled) return;

        // 기준금액 미만 자동승인이면 결재 불요
        if (setting.AutoBelow && setting.Threshold > 0 && amount < setting.Threshold) return;

        // 결재 라인 수 확인
        var lineCount = await db.QueryFirstOrDefaultAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM approval_lines WHERE tenant_id = @TenantId AND doc_type = @DocType AND is_active = 1",
                new { TenantId = tenantId, DocType = docType }, cancellationToken: ct));
        if (lineCount == 0) return;

        // 중복 방지
        var exists = await db.QueryFirstOrDefaultAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM approval_documents WHERE tenant_id = @TenantId AND doc_type = @DocType AND ref_id = @RefId",
                new { TenantId = tenantId, DocType = docType, RefId = refId }, cancellationToken: ct));
        if (exists > 0) return;

        // 결재 문서 생성
        await db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO approval_documents
              (approval_id, tenant_id, doc_type, ref_id, ref_no, title, amount,
               status, current_seq, total_lines, requester_id, requester_name, memo, created_by)
            VALUES
              (UUID(), @TenantId, @DocType, @RefId, @RefNo, @Title, @Amount,
               'pending', 1, @TotalLines, @RequesterId, @RequesterName, '확정 시 자동 생성', @RequesterId)
            """,
            new
            {
                TenantId = tenantId, DocType = docType, RefId = refId, RefNo = refNo,
                Title = title, Amount = amount, TotalLines = lineCount,
                RequesterId = requesterId, RequesterName = requesterName
            }, cancellationToken: ct));
    }
}
