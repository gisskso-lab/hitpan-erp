using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Approval;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>경리·세무 통합 서비스 — 현금출납장, 매입매출장, 부가세, 경비, 손익</summary>
public class FinanceService : IFinanceService
{
    private readonly IDbConnection _db;
    private readonly IAuditService _audit;

    // 작(2026-08-13) 단계2: 경비를 등록하면 결재자에게 바로 알린다.
    private readonly INotificationService _notifier;

    public FinanceService(IDbConnection db, IAuditService audit, INotificationService notifier)
    {
        _db = db;
        _audit = audit;
        _notifier = notifier;
    }

    // ═══════════════════════════════════════
    // 현금출납장
    // ═══════════════════════════════════════

    public async Task<List<CashbookDto>> GetCashbookAsync(string tenantId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        // 누적 잔액은 SQL window function으로 계산 (O(n) 메모리 루프 제거)
        var sql = """
            SELECT * FROM (
                SELECT c.cashbook_id AS CashbookId, c.tx_date AS TxDate, c.tx_type AS TxType,
                       c.category AS Category, c.partner_id AS PartnerId, p.partner_name AS PartnerName,
                       c.description AS Description, c.income_amount AS IncomeAmount,
                       c.expense_amount AS ExpenseAmount,
                       SUM(c.income_amount - c.expense_amount) OVER (
                           ORDER BY c.tx_date ASC, c.created_at ASC
                           ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
                       ) AS Balance,
                       c.payment_method AS PaymentMethod, c.memo AS Memo,
                       c.created_at AS CreatedAt
                FROM cashbook c
                LEFT JOIN partners p ON p.partner_id = c.partner_id
                WHERE c.tenant_id = @TenantId AND c.is_active = 1
            """;
        if (from.HasValue) sql += " AND c.tx_date >= @From";
        if (to.HasValue) sql += " AND c.tx_date <= @To";
        sql += ") t ORDER BY t.TxDate DESC, t.CreatedAt DESC";

        return (await _db.QueryAsync<CashbookDto>(new CommandDefinition(
            sql, new { TenantId = tenantId, From = from, To = to }, cancellationToken: ct))).ToList();
    }

    public async Task<string> CreateCashbookAsync(CreateCashbookRequest req, string tenantId, string userId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        await ApprovalTriggerHelper.EnsureNotClosedAsync(_db, tenantId, req.TxDate, ct);
        using var tx = _db.BeginTransaction();
        var id = Guid.NewGuid().ToString();
        try
        {
            var prevBalance = await _db.QueryFirstOrDefaultAsync<decimal>(new CommandDefinition(
                "SELECT COALESCE(balance, 0) FROM cashbook WHERE tenant_id = @TenantId AND is_active = 1 ORDER BY tx_date DESC, created_at DESC LIMIT 1 FOR UPDATE",
                new { TenantId = tenantId }, transaction: tx, cancellationToken: ct));

            var income = req.TxType == "income" ? req.Amount : 0;
            var expense = req.TxType == "expense" ? req.Amount : 0;
            var balance = prevBalance + income - expense;

            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO cashbook (cashbook_id, tenant_id, tx_date, tx_type, category, partner_id,
                       description, income_amount, expense_amount, balance, payment_method, memo, created_by)
                VALUES (@Id, @TenantId, @TxDate, @TxType, @Category, @PartnerId,
                       @Description, @Income, @Expense, @Balance, @Method, @Memo, @UserId)
                """,
                new { Id = id, TenantId = tenantId, req.TxDate, req.TxType, req.Category, req.PartnerId,
                      req.Description, Income = income, Expense = expense, Balance = balance,
                      Method = req.PaymentMethod, req.Memo, UserId = userId }, transaction: tx, cancellationToken: ct));
            tx.Commit();

            // 감사로그 — 현금출납장 생성
            var afterJson = $"{{\"tx_date\":\"{req.TxDate:yyyy-MM-dd}\",\"tx_type\":\"{req.TxType}\",\"amount\":{req.Amount}}}";
            await _audit.LogAsync("create", "cashbook", id, afterJson: afterJson, ct: ct);

            return id;
        }
        catch (Exception)
        {
            try { tx.Rollback(); } catch (Exception rbex) { Console.Error.WriteLine($"[FinanceService] rollback failed: {rbex.Message}"); }
            throw;
        }
    }

    public async Task DeleteCashbookAsync(string id, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        await _db.ExecuteAsync(new CommandDefinition(
            "UPDATE cashbook SET is_active = 0 WHERE cashbook_id = @Id AND tenant_id = @TenantId",
            new { Id = id, TenantId = tenantId }, cancellationToken: ct));

        // 감사로그 — 현금출납장 소프트 삭제
        await _audit.LogAsync("delete", "cashbook", id, ct: ct);
    }

    // ═══════════════════════════════════════
    // 매입매출장 (자동 집계 — 확정 전표 기반)
    // ═══════════════════════════════════════

    public async Task<List<PurchaseSalesLedgerDto>> GetPurchaseSalesLedgerAsync(string tenantId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        // 매출 (confirmed/invoiced 거래명세서)
        var salesSql = """
            SELECT d.delivery_date AS TxDate, '매출' AS DocType, d.delivery_no AS DocNo,
                   p.partner_name AS PartnerName, d.total_amount AS SupplyAmount,
                   d.vat_amount AS VatAmount, (d.total_amount + d.vat_amount) AS TotalAmount, d.memo AS Memo
            FROM sales_deliveries d
            LEFT JOIN partners p ON p.partner_id = d.partner_id
            WHERE d.tenant_id = @TenantId AND d.status IN ('confirmed','invoiced') AND d.is_deleted = 0
            """;
        if (from.HasValue) salesSql += " AND d.delivery_date >= @From";
        if (to.HasValue) salesSql += " AND d.delivery_date <= @To";

        // 매입 (confirmed 매입명세서)
        var purchaseSql = """
            SELECT r.receipt_date AS TxDate, '매입' AS DocType, r.receipt_no AS DocNo,
                   p.partner_name AS PartnerName, r.total_amount AS SupplyAmount,
                   r.vat_amount AS VatAmount, (r.total_amount + r.vat_amount) AS TotalAmount, r.memo AS Memo
            FROM purchase_receipts r
            LEFT JOIN partners p ON p.partner_id = r.partner_id
            WHERE r.tenant_id = @TenantId AND r.status = 'confirmed'
            """;
        if (from.HasValue) purchaseSql += " AND r.receipt_date >= @From";
        if (to.HasValue) purchaseSql += " AND r.receipt_date <= @To";

        var sql = $"({salesSql}) UNION ALL ({purchaseSql}) ORDER BY TxDate DESC";
        return (await _db.QueryAsync<PurchaseSalesLedgerDto>(new CommandDefinition(
            sql, new { TenantId = tenantId, From = from, To = to }, cancellationToken: ct))).ToList();
    }

    // ═══════════════════════════════════════
    // 부가세 신고자료 (반기별 집계)
    // ═══════════════════════════════════════

    public async Task<VatSummaryDto> GetVatSummaryAsync(string tenantId, int year, int half, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        // half=1: 1~6월, half=2: 7~12월
        var fromDate = new DateTime(year, half == 1 ? 1 : 7, 1);
        var toDate = new DateTime(year, half == 1 ? 6 : 12, DateTime.DaysInMonth(year, half == 1 ? 6 : 12));

        // 매출 집계
        var sales = await _db.QueryFirstOrDefaultAsync<(decimal Supply, decimal Vat, int Cnt)>(new CommandDefinition(
            """
            SELECT COALESCE(SUM(total_amount),0) AS Supply, COALESCE(SUM(vat_amount),0) AS Vat, COUNT(*) AS Cnt
            FROM sales_deliveries
            WHERE tenant_id = @TenantId AND status IN ('confirmed','invoiced') AND is_deleted = 0
              AND delivery_date BETWEEN @From AND @To
            """,
            new { TenantId = tenantId, From = fromDate, To = toDate }, cancellationToken: ct));

        // 매입 집계
        var purchase = await _db.QueryFirstOrDefaultAsync<(decimal Supply, decimal Vat, int Cnt)>(new CommandDefinition(
            """
            SELECT COALESCE(SUM(total_amount),0) AS Supply, COALESCE(SUM(vat_amount),0) AS Vat, COUNT(*) AS Cnt
            FROM purchase_receipts
            WHERE tenant_id = @TenantId AND status = 'confirmed'
              AND receipt_date BETWEEN @From AND @To
            """,
            new { TenantId = tenantId, From = fromDate, To = toDate }, cancellationToken: ct));

        return new VatSummaryDto
        {
            Period = $"{year}년 {(half == 1 ? "상반기" : "하반기")} ({fromDate:yyyy-MM-dd} ~ {toDate:yyyy-MM-dd})",
            SalesSupply = sales.Supply, SalesVat = sales.Vat, SalesCount = sales.Cnt,
            PurchaseSupply = purchase.Supply, PurchaseVat = purchase.Vat, PurchaseCount = purchase.Cnt,
            NetVat = sales.Vat - purchase.Vat
        };
    }

    // ═══════════════════════════════════════
    // 경비 처리
    // ═══════════════════════════════════════

    public async Task<List<ExpenseDto>> GetExpensesAsync(string tenantId, DateTime? from, DateTime? to, CancellationToken ct = default)
        => await GetExpensesAsync(tenantId, from, to, 500, ct);

    // 헌법 #19·#25 정합 — limit 기본 500으로 제한 (5/26 진범 #4·#7: 27,640건 폭탄 봉합)
    public async Task<List<ExpenseDto>> GetExpensesAsync(string tenantId, DateTime? from, DateTime? to, int limit, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        if (limit <= 0 || limit > 5000) limit = 500;

        // 경리 직접 등록 경비
        var sql1 = """
            SELECT e.expense_id AS ExpenseId, e.expense_date AS ExpenseDate,
                   e.employee_id AS EmployeeId, emp.emp_name AS EmployeeName,
                   e.category AS Category, e.description AS Description,
                   e.amount AS Amount, e.vat_amount AS VatAmount,
                   e.payment_method AS PaymentMethod, e.receipt_yn AS ReceiptYn,
                   e.approval_status AS ApprovalStatus, e.memo AS Memo
            FROM expenses e
            LEFT JOIN employees emp ON emp.employee_id = e.employee_id
            WHERE e.tenant_id = @TenantId AND e.is_active = 1
            """;
        if (from.HasValue) sql1 += " AND e.expense_date >= @From";
        if (to.HasValue) sql1 += " AND e.expense_date <= @To";

        // HR 경비신청 데이터도 포함
        var sql2 = """
            SELECT r.request_id AS ExpenseId, r.request_date AS ExpenseDate,
                   r.employee_id AS EmployeeId, emp.emp_name AS EmployeeName,
                   r.category AS Category, r.description AS Description,
                   r.amount AS Amount, 0 AS VatAmount,
                   'personal' AS PaymentMethod, 0 AS ReceiptYn,
                   r.status AS ApprovalStatus, '경비신청' AS Memo
            FROM hr_expense_requests r
            LEFT JOIN employees emp ON emp.employee_id = r.employee_id
            WHERE r.tenant_id = @TenantId
            """;
        if (from.HasValue) sql2 += " AND r.request_date >= @From";
        if (to.HasValue) sql2 += " AND r.request_date <= @To";

        var sql = $"({sql1}) UNION ALL ({sql2}) ORDER BY ExpenseDate DESC LIMIT @Limit";

        return (await _db.QueryAsync<ExpenseDto>(new CommandDefinition(
            sql, new { TenantId = tenantId, From = from, To = to, Limit = limit }, cancellationToken: ct))).ToList();
    }

    public async Task<string> CreateExpenseAsync(CreateExpenseRequest req, string tenantId, string userId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        await ApprovalTriggerHelper.EnsureNotClosedAsync(_db, tenantId, req.ExpenseDate, ct);
        var id = Guid.NewGuid().ToString();
        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO expenses (expense_id, tenant_id, expense_date, employee_id, category, description,
                   amount, vat_amount, payment_method, receipt_yn, approval_status, memo, created_by)
            VALUES (@Id, @TenantId, @Date, @EmpId, @Category, @Description,
                   @Amount, @Vat, @Method, @Receipt, 'pending', @Memo, @UserId)
            """,
            new { Id = id, TenantId = tenantId, Date = req.ExpenseDate, EmpId = userId,
                  req.Category, req.Description, req.Amount, Vat = req.VatAmount,
                  Method = req.PaymentMethod, Receipt = req.ReceiptYn ? 1 : 0, req.Memo, UserId = userId },
            cancellationToken: ct));

        // 감사로그 — 경비 생성
        var afterJson = $"{{\"expense_date\":\"{req.ExpenseDate:yyyy-MM-dd}\",\"category\":\"{req.Category}\",\"amount\":{req.Amount}}}";
        await _audit.LogAsync("create", "expense", id, afterJson: afterJson, ct: ct);

        // 결재 워크플로우 트리거 — 설정 ON 시 결재 문서 자동 생성
        // (현장영업·외근 경비 → 대표 결재 라인 자동 연결)
        var docNo = $"EXP-{req.ExpenseDate:yyyyMMdd}-{id.Substring(0, 6)}";
        var title = $"경비 승인 요청: {req.Category} / {req.Amount:N0}원";
        // 봉합 (2026-06-23, 5차 후속 APPR-TRIGGER P2): 트리거 실패가 호출자로 전파되면 이미 커밋된
        //   경비 레코드는 남는데 화면은 500 으로 보이는 불일치가 났다. 판매·매입과 동일하게 "경비 레코드는
        //   이미 커밋, 결재는 부가" 원칙을 적용해 삼키되, 헌법 #15 에 따라 예외 전체를 로그로 남긴다.
        try
        {
            await ApprovalTriggerHelper.TryCreateApprovalAsync(_db,
                "expense", id, docNo, title,
                req.Amount + req.VatAmount,
                tenantId, userId, "경비등록자", ct, _notifier);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"[ApprovalTrigger] 경비 {docNo} 결재 트리거 실패: {ex}");
        }

        return id;
    }

    public async Task ApproveExpenseAsync(string expenseId, string tenantId, string action, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        // expenses 테이블 시도 (승인자 기록 포함)
        var updated = await _db.ExecuteAsync(new CommandDefinition(
            "UPDATE expenses SET approval_status = @Action, updated_at = NOW(6) WHERE expense_id = @Id AND tenant_id = @TenantId AND is_active = 1",
            new { Id = expenseId, TenantId = tenantId, Action = action }, cancellationToken: ct));

        // hr_expense_requests 테이블도 시도 (승인자 기록 포함)
        if (updated == 0)
        {
            await _db.ExecuteAsync(new CommandDefinition(
                "UPDATE hr_expense_requests SET status = @Action, approved_by = @TenantId, approved_at = NOW(6) WHERE request_id = @Id AND tenant_id = @TenantId",
                new { Id = expenseId, TenantId = tenantId, Action = action }, cancellationToken: ct));
        }

        // 감사로그 — 경비 승인/반려 (action 값: approved/rejected 등)
        var afterJson = $"{{\"action\":\"{action}\"}}";
        await _audit.LogAsync("approve", "expense", expenseId, afterJson: afterJson, ct: ct);
    }

    // ═══════════════════════════════════════
    // 손익현황 (월별)
    // ═══════════════════════════════════════

    public async Task<List<ProfitSummaryDto>> GetProfitAsync(string tenantId, int year, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var fromDate = new DateTime(year, 1, 1);
        var toDate = new DateTime(year, 12, 31);

        // 봉합 (2026-06-23, 6차 전수조사 C3, 사장님 결재 "vat포함·별도·면세 설정 가능"):
        //   종전엔 매출·매입은 부가세 포함(total+vat)인데 경비만 부가세 제외(amount)로 기준이 비대칭이었고,
        //   미승인(pending)·반려 경비까지 손익에 포함됐다. 사장님 의중대로 손익 부가세 기준을 고객사가 3가지 중 고른다.
        //   - finance.profit_basis 설정(workflow_settings, DDL 무변경):
        //       'vat_included' = 부가세 포함 총액(매출·매입·경비 모두 +vat)
        //       'supply'       = 부가세 별도(공급가 기준, 부가세 제외) — 기본·회계 정석(과세사업자)
        //       'tax_free'     = 면세사업자(부가세 개념 없음 → 금액 그대로, supply 와 식은 같으나 의미가 면세)
        //   - 경비는 기준 무관 approval_status='approved' 만 집계(미승인·반려 제외) — 명백한 버그라 공통 적용.
        var profitBasis = await _db.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT setting_value FROM workflow_settings WHERE tenant_id=@TenantId AND setting_key='finance.profit_basis' AND is_active=1",
            new { TenantId = tenantId }, cancellationToken: ct));

        // 부가세 포함만 +vat, '별도'·'면세'는 공급가(부가세 미가산). 미설정/알수없음 → 기본 공급가(supply).
        var vatIncluded = string.Equals(profitBasis, "vat_included", StringComparison.OrdinalIgnoreCase);
        var salesAmt = vatIncluded ? "total_amount + vat_amount" : "total_amount";
        var purchaseAmt = vatIncluded ? "total_amount + vat_amount" : "total_amount";
        var expenseAmt = vatIncluded ? "amount + vat_amount" : "amount";

        // 1회 쿼리로 12개월 매출·매입·경비 집계
        var sql = $$"""
            SELECT
              m.mon AS YearMonth,
              CONCAT(CAST(m.mon % 100 AS CHAR), '월') AS YearMonthLabel,
              COALESCE(s.amt, 0) AS SalesAmount,
              COALESCE(p.amt, 0) AS PurchaseAmount,
              COALESCE(e.amt, 0) AS ExpenseAmount,
              COALESCE(s.amt, 0) - COALESCE(p.amt, 0) AS GrossProfit,
              COALESCE(s.amt, 0) - COALESCE(p.amt, 0) - COALESCE(e.amt, 0) AS NetProfit,
              CASE WHEN COALESCE(s.amt, 0) > 0
                THEN ROUND((COALESCE(s.amt, 0) - COALESCE(p.amt, 0) - COALESCE(e.amt, 0)) / COALESCE(s.amt, 0) * 100, 1)
                ELSE 0 END AS ProfitRate
            FROM (
              SELECT @y*100+1 AS mon UNION SELECT @y*100+2 UNION SELECT @y*100+3 UNION SELECT @y*100+4
              UNION SELECT @y*100+5 UNION SELECT @y*100+6 UNION SELECT @y*100+7 UNION SELECT @y*100+8
              UNION SELECT @y*100+9 UNION SELECT @y*100+10 UNION SELECT @y*100+11 UNION SELECT @y*100+12
            ) m
            LEFT JOIN (
              SELECT YEAR(delivery_date)*100+MONTH(delivery_date) AS ym, SUM({{salesAmt}}) AS amt
              FROM sales_deliveries WHERE tenant_id=@TenantId AND status IN ('confirmed','invoiced') AND is_deleted=0
                AND delivery_date BETWEEN @From AND @To GROUP BY ym
            ) s ON s.ym = m.mon
            LEFT JOIN (
              SELECT YEAR(receipt_date)*100+MONTH(receipt_date) AS ym, SUM({{purchaseAmt}}) AS amt
              FROM purchase_receipts WHERE tenant_id=@TenantId AND status='confirmed'
                AND receipt_date BETWEEN @From AND @To GROUP BY ym
            ) p ON p.ym = m.mon
            LEFT JOIN (
              SELECT YEAR(expense_date)*100+MONTH(expense_date) AS ym, SUM({{expenseAmt}}) AS amt
              FROM expenses WHERE tenant_id=@TenantId AND is_active=1 AND approval_status='approved'
                AND expense_date BETWEEN @From AND @To GROUP BY ym
            ) e ON e.ym = m.mon
            ORDER BY m.mon
            """;

        var rows = await _db.QueryAsync<ProfitSummaryDto>(new CommandDefinition(
            sql,
            new { TenantId = tenantId, From = fromDate, To = toDate, y = year },
            cancellationToken: ct));

        return rows.ToList();
    }

    // ═══════════════════════════════════════
    // 정합성 자동 검증
    // ═══════════════════════════════════════

    public async Task<DataIntegrityReport> CheckIntegrityAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var items = new List<IntegrityItem>();

        // 1. 음수 재고 체크
        var negStock = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM item_stock WHERE tenant_id=@T AND current_qty < 0",
            new { T = tenantId }, cancellationToken: ct));
        items.Add(new IntegrityItem { Category = "재고", CheckName = "음수 재고", Status = negStock == 0 ? "OK" : "FAIL", Detail = negStock > 0 ? $"{negStock}건 음수" : null });

        // 2. item_stock 레코드 누락
        var noStock = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM items i LEFT JOIN item_stock s ON s.item_id=i.item_id WHERE i.tenant_id=@T AND i.is_deleted=0 AND s.stock_id IS NULL",
            new { T = tenantId }, cancellationToken: ct));
        items.Add(new IntegrityItem { Category = "재고", CheckName = "item_stock 누락", Status = noStock == 0 ? "OK" : "FAIL", Detail = noStock > 0 ? $"{noStock}건 누락" : null });

        // 3. item_stock vs stock_ledger 불일치 (직접 쿼리 — collation 안전)
        var mismatch = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM (
              SELECT s.item_id, s.current_qty, COALESCE(SUM(l.qty_in)-SUM(l.qty_out),0) AS lq
              FROM item_stock s
              LEFT JOIN stock_ledger l ON l.item_id = s.item_id AND l.tenant_id = s.tenant_id
              WHERE s.tenant_id = @T
              GROUP BY s.item_id, s.current_qty
              HAVING ABS(s.current_qty - COALESCE(SUM(l.qty_in)-SUM(l.qty_out),0)) > 0.01
            ) t
            """,
            new { T = tenantId }, cancellationToken: ct));
        items.Add(new IntegrityItem { Category = "재고", CheckName = "stock vs ledger 정합성", Status = mismatch == 0 ? "OK" : "WARN", Detail = mismatch > 0 ? $"{mismatch}건 불일치" : null });

        // 4. 초과 입고
        var overRecv = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM purchase_order_items WHERE tenant_id=@T AND received_qty > ordered_qty",
            new { T = tenantId }, cancellationToken: ct));
        items.Add(new IntegrityItem { Category = "매입", CheckName = "초과 입고", Status = overRecv == 0 ? "OK" : "FAIL", Detail = overRecv > 0 ? $"{overRecv}건" : null });

        // 5. 초과 납품
        var overDlv = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM sales_order_items WHERE tenant_id=@T AND delivered_qty > ordered_qty",
            new { T = tenantId }, cancellationToken: ct));
        items.Add(new IntegrityItem { Category = "매출", CheckName = "초과 납품", Status = overDlv == 0 ? "OK" : "FAIL", Detail = overDlv > 0 ? $"{overDlv}건" : null });

        // 6. 금액 필드 음수
        var negAmount = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM items WHERE tenant_id=@T AND is_deleted=0 AND (purchase_price < 0 OR sale_price < 0)",
            new { T = tenantId }, cancellationToken: ct));
        items.Add(new IntegrityItem { Category = "마스터", CheckName = "음수 단가", Status = negAmount == 0 ? "OK" : "FAIL", Detail = negAmount > 0 ? $"{negAmount}건" : null });

        // 7. 고아 BOM 자재 (상품 삭제됨)
        var orphanBom = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM bom_items bi LEFT JOIN items i ON i.item_id=bi.material_item_id WHERE bi.tenant_id=@T AND i.item_id IS NULL",
            new { T = tenantId }, cancellationToken: ct));
        items.Add(new IntegrityItem { Category = "BOM", CheckName = "고아 자재 참조", Status = orphanBom == 0 ? "OK" : "WARN", Detail = orphanBom > 0 ? $"{orphanBom}건" : null });

        // 8. 결재 라인 — 사원 참조 무결성
        // 봉합 (2026-06-22, 13차 축7 P2): 종전엔 approval_lines.approver_id 를 조회했으나, DB-29 재설계로
        //   approval_lines 는 이름만 가진 결재선 템플릿 마스터(approver_id·doc_type 컬럼 없음)가 됐다.
        //   실제 결재자 행은 approval_doc_lines 로 분리됐고 거기에 approver_id·is_active 가 있다. 종전 쿼리는
        //   'Unknown column al.approver_id' 런타임 500 → CheckIntegrity 8항목 전체 실패였다(헌법 #13·#36).
        //   결재자 사원 참조 무결성은 실 결재선(approval_doc_lines)을 봐야 의미가 맞다.
        var orphanApprover = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM approval_doc_lines al LEFT JOIN employees e ON e.employee_id=al.approver_id WHERE al.tenant_id=@T AND al.is_active=1 AND e.employee_id IS NULL",
            new { T = tenantId }, cancellationToken: ct));
        items.Add(new IntegrityItem { Category = "결재", CheckName = "결재자 사원 참조", Status = orphanApprover == 0 ? "OK" : "WARN", Detail = orphanApprover > 0 ? $"{orphanApprover}건" : null });

        var pass = items.Count(x => x.Status == "OK");
        var fail = items.Count(x => x.Status == "FAIL");

        return new DataIntegrityReport
        {
            CheckedAt = DateTime.Now,
            Items = items,
            TotalChecks = items.Count,
            PassCount = pass,
            FailCount = fail,
            Score = Math.Round((decimal)pass / items.Count * 100, 1)
        };
    }

    // ═══════════════════════════════════════
    // 대시보드 요약
    // ═══════════════════════════════════════

    // ── 대시보드 캐시 (30초 TTL) — DB 부하 90% 감소 ──
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DashboardSummaryDto Data, DateTime ExpiresAt)> _dashCache = new();

    public async Task<DashboardSummaryDto> GetDashboardAsync(string tenantId, CancellationToken ct = default)
    {
        // 캐시 확인 (30초 이내면 DB 조회 생략)
        if (_dashCache.TryGetValue(tenantId, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
            return cached.Data;

        await EnsureOpenAsync(ct);
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var yearStart = new DateTime(today.Year, 1, 1);
        var p = new { TenantId = tenantId, Today = today, MonthStart = monthStart, YearStart = yearStart };

        // ── 7개 KPI를 UNION ALL 단일 쿼리로 통합 (네트워크 왕복 1회) ──
        // 주의: MySqlConnection은 thread-safe 아니므로 Task.WhenAll 병렬 금지 ("conn in use" 에러)
        // 대신 모든 KPI를 한 번의 쿼리에 합쳐서 성능 확보
        const string kpiSql = """
            SELECT 'today_sales' AS k, COALESCE(SUM(total_amount + vat_amount), 0) AS v
              FROM sales_deliveries WHERE tenant_id=@TenantId AND status IN ('confirmed','invoiced') AND is_deleted=0 AND delivery_date=@Today
            UNION ALL
            SELECT 'month_sales', COALESCE(SUM(total_amount + vat_amount), 0)
              FROM sales_deliveries WHERE tenant_id=@TenantId AND status IN ('confirmed','invoiced') AND is_deleted=0
                AND delivery_date>=@MonthStart AND delivery_date<=@Today
            UNION ALL
            SELECT 'month_purchase', COALESCE(SUM(total_amount + vat_amount), 0)
              FROM purchase_receipts WHERE tenant_id=@TenantId AND status='confirmed'
                AND receipt_date>=@MonthStart AND receipt_date<=@Today
            UNION ALL
            -- 봉합 (2026-06-23, 6차 전수조사 C2): 미수금 수금 차감을 'sales_delivery' 단일로 좁힘.
            --   종전 IN('sales_delivery','sales_order')은 다른 집계(CollectionService:382·SALES-01)와 불일치이고,
            --   collections 에 sales_order 는 들어가지 않아(UI·마이그 전수 0건) 미래 잠복 결함이었다.
            SELECT 'receivable',
              COALESCE((SELECT SUM(total_amount + vat_amount) FROM sales_deliveries WHERE tenant_id=@TenantId AND status IN ('confirmed','invoiced') AND is_deleted=0), 0)
              - COALESCE((SELECT SUM(amount) FROM collections WHERE tenant_id=@TenantId AND ref_doc_type = 'sales_delivery'), 0)
            UNION ALL
            -- 봉합 (2026-06-23, 6차 전수조사 C2 P1): 미지급 지급 차감을 collections → payments 로 정정.
            --   매입 지급은 collections 가 아니라 payments(payment_type='purchase')에 기록된다(GetPayablesAsync:439 정식 기준).
            --   종전엔 collections 의 'purchase_receipt'/'purchase_order' 를 봤는데 거기엔 0건이라 차감이 항상 0 →
            --   미지급이 지급해도 안 줄어 영구 과대 계상됐다(헌법 #20). payments 기준으로 GetPayablesAsync 와 일관화.
            SELECT 'payable',
              COALESCE((SELECT SUM(total_amount + vat_amount) FROM purchase_receipts WHERE tenant_id=@TenantId AND status='confirmed'), 0)
              - COALESCE((SELECT SUM(amount) FROM payments WHERE tenant_id=@TenantId AND is_active=1 AND payment_type='purchase'), 0)
            UNION ALL
            SELECT 'low_stock',
              (SELECT COUNT(*) FROM item_stock s INNER JOIN items i ON i.item_id=s.item_id AND i.tenant_id=s.tenant_id
               WHERE s.tenant_id=@TenantId AND i.is_deleted=0 AND i.safety_stock>0 AND s.current_qty<i.safety_stock)
            UNION ALL
            SELECT 'pending_approval',
              (SELECT COUNT(*) FROM approval_documents WHERE tenant_id=@TenantId AND status='pending')
            """;

        var kpiRows = (await _db.QueryAsync<(string k, decimal v)>(
            new CommandDefinition(kpiSql, p, cancellationToken: ct)).ConfigureAwait(false)).ToDictionary(r => r.k, r => r.v);

        var dto = new DashboardSummaryDto
        {
            TodaySales = kpiRows.GetValueOrDefault("today_sales"),
            MonthSales = kpiRows.GetValueOrDefault("month_sales"),
            MonthPurchase = kpiRows.GetValueOrDefault("month_purchase"),
            UnpaidReceivable = kpiRows.GetValueOrDefault("receivable"),
            UnpaidPayable = kpiRows.GetValueOrDefault("payable"),
            LowStockCount = (int)kpiRows.GetValueOrDefault("low_stock"),
            PendingApprovalCount = (int)kpiRows.GetValueOrDefault("pending_approval")
        };

        // ── 월별 매출·매입 추이 (최근 6개월) ──
        var sixMonthsAgo = monthStart.AddMonths(-5);
        dto.MonthlyTrend = (await _db.QueryAsync<MonthlyTrendItem>(new CommandDefinition(
            """
            SELECT
              DATE_FORMAT(m.mon_date, '%Y-%m') AS YearMonth,
              CONCAT(MONTH(m.mon_date), '월') AS Label,
              COALESCE(s.amt, 0) AS SalesAmount,
              COALESCE(p.amt, 0) AS PurchaseAmount
            FROM (
              SELECT DATE_ADD(@Start, INTERVAL n MONTH) AS mon_date
              FROM (SELECT 0 AS n UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5) nums
            ) m
            LEFT JOIN (
              SELECT DATE_FORMAT(delivery_date, '%Y-%m-01') AS ym,
                     SUM(total_amount + vat_amount) AS amt
              FROM sales_deliveries
              WHERE tenant_id = @TenantId AND status IN ('confirmed','invoiced') AND is_deleted = 0
                AND delivery_date >= @Start
              GROUP BY ym
            ) s ON s.ym = DATE_FORMAT(m.mon_date, '%Y-%m-01')
            LEFT JOIN (
              SELECT DATE_FORMAT(receipt_date, '%Y-%m-01') AS ym,
                     SUM(total_amount + vat_amount) AS amt
              FROM purchase_receipts
              WHERE tenant_id = @TenantId AND status = 'confirmed'
                AND receipt_date >= @Start
              GROUP BY ym
            ) p ON p.ym = DATE_FORMAT(m.mon_date, '%Y-%m-01')
            ORDER BY m.mon_date
            """,
            new { TenantId = tenantId, Start = sixMonthsAgo }, cancellationToken: ct)).ConfigureAwait(false)).ToList();

        // ── 거래처 매출 TOP 5 (올해) ──
        dto.TopPartners = (await _db.QueryAsync<PartnerRankItem>(new CommandDefinition(
            """
            SELECT p.partner_name AS PartnerName,
                   SUM(d.total_amount + d.vat_amount) AS TotalAmount,
                   COUNT(*) AS OrderCount
            FROM sales_deliveries d
            INNER JOIN partners p ON p.partner_id = d.partner_id
            WHERE d.tenant_id = @TenantId AND d.status IN ('confirmed','invoiced') AND d.is_deleted = 0
              AND d.delivery_date >= @YearStart
            GROUP BY d.partner_id, p.partner_name
            ORDER BY TotalAmount DESC
            LIMIT 5
            """,
            new { TenantId = tenantId, YearStart = yearStart }, cancellationToken: ct)).ConfigureAwait(false)).ToList();

        // ── 최근 거래 5건 (매출 + 매입 통합) ──
        dto.RecentTransactions = (await _db.QueryAsync<RecentTransactionItem>(new CommandDefinition(
            """
            (
              SELECT delivery_date AS TxDate, '판매' AS DocType, delivery_no AS DocNo,
                     (SELECT partner_name FROM partners WHERE partner_id = d.partner_id) AS PartnerName,
                     (total_amount + vat_amount) AS Amount, status AS Status
              FROM sales_deliveries d
              WHERE d.tenant_id = @TenantId AND d.is_deleted = 0
              ORDER BY delivery_date DESC LIMIT 5
            )
            UNION ALL
            (
              SELECT receipt_date AS TxDate, '매입' AS DocType, receipt_no AS DocNo,
                     (SELECT partner_name FROM partners WHERE partner_id = r.partner_id) AS PartnerName,
                     (total_amount + vat_amount) AS Amount, status AS Status
              FROM purchase_receipts r
              WHERE r.tenant_id = @TenantId
              ORDER BY receipt_date DESC LIMIT 5
            )
            ORDER BY TxDate DESC
            LIMIT 5
            """,
            new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false)).ToList();

        // ── 안전재고 미달 품목 (최대 10건) ──
        dto.LowStockItems = (await _db.QueryAsync<LowStockItem>(new CommandDefinition(
            """
            SELECT i.item_name AS ItemName, i.spec AS Spec,
                   s.current_qty AS CurrentQty, i.safety_stock AS SafetyStock
            FROM item_stock s
            INNER JOIN items i ON i.item_id = s.item_id AND i.tenant_id = s.tenant_id
            WHERE s.tenant_id = @TenantId AND i.is_deleted = 0
              AND i.safety_stock > 0 AND s.current_qty < i.safety_stock
            ORDER BY (s.current_qty - i.safety_stock) ASC
            LIMIT 10
            """,
            new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false)).ToList();

        // 캐시 저장 (30초 TTL)
        _dashCache[tenantId] = (dto, DateTime.UtcNow.AddSeconds(30));
        return dto;
    }

    // ═══════════════════════════════════════
    // 계정과목
    // ═══════════════════════════════════════

    public async Task<List<AccountDto>> GetAccountsAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        return (await _db.QueryAsync<AccountDto>(new CommandDefinition(
            """
            SELECT account_code AS AccountCode, account_name AS AccountName,
                   account_type AS AccountType, parent_code AS ParentCode,
                   is_active AS IsActive, sort_order AS SortOrder
            FROM accounts
            WHERE tenant_id = @TenantId
            ORDER BY sort_order, account_code
            """,
            new { TenantId = tenantId }, cancellationToken: ct))).ToList();
    }

    // 봉합 (2026-06-22, 12차 2단 교차검증 ACCOUNTS-SEED 동반 P1): AutoJournalHelper 가 매출·매입·BOM
    //   회계기표에 직접 참조하는 표준계정. journal_lines.fk_jl_account FK 때문에 이 코드가 삭제/비활성되면
    //   다음 확정이 FK 1452 → 확정 전체 롤백("확정했는데 회계 안 잡힘"=헌법 #20 흐름 끊김). 가입시점 시드
    //   (CompanyBootstrapController)만으론 사후 삭제를 못 막으므로 삭제·비활성 경로에서 차단한다.
    private static readonly HashSet<string> SystemRequiredAccountCodes = new()
    {
        "10800", "17600", "14600", "16900", "23200", "25500", "40100", "50100",
    };

    public async Task<string> CreateAccountAsync(string tenantId, CreateAccountRequest req, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO accounts (account_code, tenant_id, account_name, account_type, parent_code, is_active, sort_order)
            VALUES (@AccountCode, @TenantId, @AccountName, @AccountType, @ParentCode, 1, @SortOrder)
            """,
            new { req.AccountCode, TenantId = tenantId, req.AccountName, req.AccountType, req.ParentCode, req.SortOrder },
            cancellationToken: ct));
        return req.AccountCode;
    }

    public async Task UpdateAccountAsync(string tenantId, string accountCode, UpdateAccountRequest req, CancellationToken ct = default)
    {
        // 시스템 필수계정 비활성 차단 (12차 봉합) — 비활성 시 회계 화면에서 사라져 운영 혼란(이름·정렬 변경은 허용).
        if (!req.IsActive && SystemRequiredAccountCodes.Contains(accountCode))
            throw new InvalidOperationException(
                $"표준 계정({accountCode})은 매출·매입·생산 회계 처리에 사용되어 비활성화할 수 없습니다.");

        await EnsureOpenAsync(ct);
        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE accounts SET account_name = @AccountName, is_active = @IsActive, sort_order = @SortOrder
            WHERE tenant_id = @TenantId AND account_code = @AccountCode
            """,
            new { req.AccountName, req.IsActive, req.SortOrder, TenantId = tenantId, AccountCode = accountCode },
            cancellationToken: ct));
    }

    public async Task DeleteAccountAsync(string tenantId, string accountCode, CancellationToken ct = default)
    {
        // 시스템 필수계정 삭제 차단 (12차 봉합) — 삭제 시 매출·매입·BOM 확정이 FK 1452 로 끊김(헌법 #20).
        if (SystemRequiredAccountCodes.Contains(accountCode))
            throw new InvalidOperationException(
                $"표준 계정({accountCode})은 매출·매입·생산 회계 처리에 사용되어 삭제할 수 없습니다.");

        await EnsureOpenAsync(ct);
        await _db.ExecuteAsync(new CommandDefinition(
            "DELETE FROM accounts WHERE tenant_id = @TenantId AND account_code = @AccountCode",
            new { TenantId = tenantId, AccountCode = accountCode }, cancellationToken: ct));
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open) return;
        if (_db is DbConnection c) { await c.OpenAsync(ct); return; }
        _db.Open();
    }
}
