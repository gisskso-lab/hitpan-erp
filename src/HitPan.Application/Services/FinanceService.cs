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

        // 🔴 20260825작17 — 매입반품도 장부에 뜬다.
        //   여기는 **집계가 아니라 전표 목록**이다. 반품을 빼는 게 아니라 **행으로 보여야** 한다.
        //   안 그러면 세무사·거래처가 장부를 볼 때 "매입은 있는데 돌려준 기록이 없는" 장부가 된다.
        //   금액은 음수로 적는다 — 합계를 내면 실제 매입액이 되도록.
        var returnSql = """
            SELECT rt.return_date AS TxDate, '매입반품' AS DocType, rt.return_no AS DocNo,
                   p.partner_name AS PartnerName,
                   -COALESCE(SUM(rti.supply_amount), 0) AS SupplyAmount,
                   -COALESCE(SUM(rti.vat_amount), 0) AS VatAmount,
                   -COALESCE(SUM(rti.supply_amount + rti.vat_amount), 0) AS TotalAmount,
                   rt.memo AS Memo
            FROM purchase_returns rt
            LEFT JOIN purchase_return_items rti ON rti.return_id = rt.return_id AND rti.tenant_id = rt.tenant_id
            LEFT JOIN partners p ON p.partner_id = rt.partner_id AND p.tenant_id = rt.tenant_id
            WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
            """;
        if (from.HasValue) returnSql += " AND rt.return_date >= @From";
        if (to.HasValue) returnSql += " AND rt.return_date <= @To";
        returnSql += " GROUP BY rt.return_id, rt.return_date, rt.return_no, p.partner_name, rt.memo";

        // 🔴 20260831작15 — 매출반품도 장부에 뜬다 (사장님 전결 · PRD FR-10).
        //   매입은 20260825작17 에 이 행이 생겼는데 **매출만 없었다.**
        //   세무사가 "반품 건만 뽑아 주세요" 할 때 매출은 못 뽑던 자리다.
        //
        //   ⚠️ 매입 코드(위 returnSql) 복붙이 아니다 — 표·컬럼·철자가 다르다.
        //     · sales_return_items(sri) ← purchase_return_items(rti)
        //     · sales_returns 는 delivery_id(원 거래명세서) 를 갖는다
        //   🔴 status 는 **양성 비교(= 'confirmed')만** 쓴다.
        //     sales_returns 는 취소를 'canceled'(l 하나)로 쓰고 sales_deliveries 는 'cancelled'(l 둘)이라
        //     `<> 'canceled'` 같은 부정 비교를 쓰면 철자 하나로 조용히 통과한다.
        //   🔴 GROUP BY 필수 — LEFT JOIN 이 헤더를 라인 수만큼 뻥튀기한다.
        //   🔴 금액은 **라인(sri) 기준** — 매입반품이 rti 기준이라 대칭을 맞춘다.
        //     (헤더 sales_returns.total_amount 는 공급가 합계라 VAT 축이 어긋난다)
        var salesReturnSql = """
            SELECT sr.return_date AS TxDate, '매출반품' AS DocType, sr.return_no AS DocNo,
                   p.partner_name AS PartnerName,
                   -COALESCE(SUM(sri.supply_amount), 0) AS SupplyAmount,
                   -COALESCE(SUM(sri.vat_amount), 0) AS VatAmount,
                   -COALESCE(SUM(sri.supply_amount + sri.vat_amount), 0) AS TotalAmount,
                   sr.memo AS Memo
            FROM sales_returns sr
            LEFT JOIN sales_return_items sri ON sri.return_id = sr.return_id AND sri.tenant_id = sr.tenant_id
            LEFT JOIN partners p ON p.partner_id = sr.partner_id AND p.tenant_id = sr.tenant_id
            WHERE sr.tenant_id = @TenantId AND sr.is_deleted = 0 AND sr.status = 'confirmed'
            """;
        if (from.HasValue) salesReturnSql += " AND sr.return_date >= @From";
        if (to.HasValue) salesReturnSql += " AND sr.return_date <= @To";
        salesReturnSql += " GROUP BY sr.return_id, sr.return_date, sr.return_no, p.partner_name, sr.memo";

        var sql = $"({salesSql}) UNION ALL ({purchaseSql}) UNION ALL ({returnSql}) UNION ALL ({salesReturnSql}) ORDER BY TxDate DESC";

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

        // 매출 집계 — 🔴 20260831작15: 매출반품을 뺀다 (사장님 전결 · PRD FR-9).
        //   종전에는 안 뺐다 ⇒ **매출세액 과대계상 = 국세청에 더 냈다.**
        //   매입은 아래에서 purchase_returns 를 이미 빼고 있었다(20260825작17). 매출만 빠져 있었다.
        //   ⚠️ 라인(sri) 기준 · 양성 비교(= 'confirmed') · GROUP BY 없이 전체 SUM — 위 매입반품과 같은 규칙.
        var sales = await _db.QueryFirstOrDefaultAsync<(decimal Supply, decimal Vat, int Cnt)>(new CommandDefinition(
            """
            SELECT COALESCE(SUM(Supply),0) AS Supply, COALESCE(SUM(Vat),0) AS Vat, COALESCE(SUM(Cnt),0) AS Cnt
            FROM (
              SELECT COALESCE(SUM(total_amount),0) AS Supply, COALESCE(SUM(vat_amount),0) AS Vat, COUNT(*) AS Cnt
              FROM sales_deliveries
              WHERE tenant_id = @TenantId AND status IN ('confirmed','invoiced') AND is_deleted = 0
                AND delivery_date BETWEEN @From AND @To
              UNION ALL
              SELECT -COALESCE(SUM(sri.supply_amount),0), -COALESCE(SUM(sri.vat_amount),0), -COUNT(DISTINCT sr.return_id)
              FROM sales_returns sr
              LEFT JOIN sales_return_items sri ON sri.return_id = sr.return_id AND sri.tenant_id = sr.tenant_id
              WHERE sr.tenant_id = @TenantId AND sr.is_deleted = 0 AND sr.status = 'confirmed'
                AND sr.return_date BETWEEN @From AND @To
            ) t
            """,
            new { TenantId = tenantId, From = fromDate, To = toDate }, cancellationToken: ct));

        // 매입 집계
        var purchase = await _db.QueryFirstOrDefaultAsync<(decimal Supply, decimal Vat, int Cnt)>(new CommandDefinition(
            """
            SELECT COALESCE(SUM(Supply),0) AS Supply, COALESCE(SUM(Vat),0) AS Vat, COALESCE(SUM(Cnt),0) AS Cnt
            FROM (
              SELECT COALESCE(SUM(total_amount),0) AS Supply, COALESCE(SUM(vat_amount),0) AS Vat, COUNT(*) AS Cnt
              FROM purchase_receipts
              WHERE tenant_id = @TenantId AND status = 'confirmed'
                AND receipt_date BETWEEN @From AND @To
              UNION ALL
              SELECT -COALESCE(SUM(rti.supply_amount),0), -COALESCE(SUM(rti.vat_amount),0), -COUNT(DISTINCT rt.return_id)
              FROM purchase_returns rt
              LEFT JOIN purchase_return_items rti ON rti.return_id = rt.return_id AND rti.tenant_id = rt.tenant_id
              WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
                AND rt.return_date BETWEEN @From AND @To
            ) t
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

    /// <summary>
    /// 경비 분류(한글) → 계정과목 코드. 못 찾으면 잡비로 떨어뜨린다.
    /// </summary>
    /// <remarks>
    /// 🔴 20260827작4 — 화면 분류는 한글 6종(교통비·식대·소모품비·접대비·통신비·기타)인데
    ///   회계 계정과목은 코드다. 그 사이를 여기서 잇는다.
    ///
    /// ⚠️ **모르는 분류는 추측하지 않고 잡비(84100)로 보낸다.**
    ///   틀린 계정에 넣는 것보다 잡비에 모아두고 사람이 재분류하는 편이 안전하다 —
    ///   접대비는 세무상 한도가 따로 있어서, 다른 계정에 잘못 넣으면 신고가 틀어진다.
    ///
    /// ⚠️ 「식대」는 복리후생비로 본다(직원 식대 기준). 거래처 식사면 접대비가 맞지만
    ///   화면이 그 둘을 구분해 받지 않는다 — 구분이 필요해지면 사장님 결재로 분류를 늘린다.
    /// </remarks>
    private static string ResolveExpenseAccount(string? category) => category switch
    {
        "교통비" => AutoJournalHelper.TravelExpense,
        "식대" => AutoJournalHelper.WelfareExpense,
        "소모품비" => AutoJournalHelper.SuppliesExpense,
        "접대비" => AutoJournalHelper.EntertainmentExpense,
        "통신비" => AutoJournalHelper.CommunicationExpense,
        _ => AutoJournalHelper.MiscExpense,
    };

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

        // 🔴 20260827작4 (사장님 오더 "모든 돈의 흐름을 회계장부 하나로") — 경비 자동기표.
        //
        //   🔴 **왜 승인 시점인가** — 매입·판매가 「확정」에서만 기표하는 것과 같은 축이다.
        //     대기(pending) 중인 경비를 장부에 올리면, 반려된 경비가 비용으로 남는다.
        //     승인된 것만 회계로 넘어간다.
        //
        //   ⚠️ 승인(approved)일 때만 기표한다. 반려·취소는 기표하지 않는다.
        //   ⚠️ 멱등 — 이미 기표된 경비는 journal_entries 에 source_id 가 있으므로 건너뛴다.
        //     (두 번 승인 눌러도 비용이 두 배로 잡히면 안 된다.)
        if (string.Equals(action, "approved", StringComparison.OrdinalIgnoreCase))
        {
            await PostExpenseJournalAsync(expenseId, tenantId, ct);
        }

        // 감사로그 — 경비 승인/반려 (action 값: approved/rejected 등)
        var afterJson = $"{{\"action\":\"{action}\"}}";
        await _audit.LogAsync("approve", "expense", expenseId, afterJson: afterJson, ct: ct);
    }

    /// <summary>
    /// 승인된 경비 1건을 분개로 옮긴다. 이미 기표됐으면 아무 일도 하지 않는다(멱등).
    /// </summary>
    private async Task PostExpenseJournalAsync(string expenseId, string tenantId, CancellationToken ct)
    {
        // 이미 기표됐나 — 중복 비용 계상 차단
        var already = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM journal_entries WHERE tenant_id=@TenantId AND source_type='expense' AND source_id=@Id",
            new { TenantId = tenantId, Id = expenseId }, cancellationToken: ct));
        if (already > 0) return;

        var row = await _db.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(
            """
            SELECT expense_date, amount, vat_amount, category, payment_method, description, employee_id
              FROM expenses
             WHERE expense_id=@Id AND tenant_id=@TenantId
            """,
            new { Id = expenseId, TenantId = tenantId }, cancellationToken: ct));
        if (row is null) return;

        // ⚠️ decimal·날짜는 드라이버에 따라 타입이 달리 온다 — Convert 로 전 타입을 받는다.
        //   (dynamic 에 직접 캐스팅하면 RuntimeBinderException 으로 500. 8/25 사고 자리.)
        var amount = Convert.ToDecimal(row.amount);
        var vat = row.vat_amount is null ? 0m : Convert.ToDecimal(row.vat_amount);
        var expenseDate = Convert.ToDateTime(row.expense_date);
        var category = row.category as string;
        var method = row.payment_method as string;
        var memo = row.description as string;
        var empId = row.employee_id as string;

        // 경비 총액 = 공급가 + 부가세. 부가세를 따로 안 뽑는 이유는, 경비는 매입세액
        // 공제 대상이 아닌 건(접대비 등)이 섞여 있어 일괄로 대급금 처리하면 신고가 틀어진다.
        // 세액공제 분리는 부가세 신고 화면에서 별도로 다룬다.
        var total = amount + vat;

        using var tx = _db.BeginTransaction();
        try
        {
            await AutoJournalHelper.RecordExpenseAsync(
                _db, tx, tenantId, expenseId, expenseDate, total,
                ResolveExpenseAccount(category), method, empId, memo, ct);
            tx.Commit();
        }
        catch (Exception)
        {
            try { tx.Rollback(); }
            catch (Exception rbex) { System.Diagnostics.Trace.TraceWarning($"[FinanceService] 경비 기표 롤백 실패: {rbex.Message}"); }
            throw;
        }
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
              SELECT ym, SUM(amt) AS amt FROM (
                SELECT YEAR(receipt_date)*100+MONTH(receipt_date) AS ym, SUM({{purchaseAmt}}) AS amt
                FROM purchase_receipts WHERE tenant_id=@TenantId AND status='confirmed'
                  AND receipt_date BETWEEN @From AND @To GROUP BY ym
                UNION ALL
                SELECT YEAR(rt.return_date)*100+MONTH(rt.return_date) AS ym, -SUM(rti.supply_amount + rti.vat_amount) AS amt
                FROM purchase_returns rt
                LEFT JOIN purchase_return_items rti ON rti.return_id=rt.return_id AND rti.tenant_id=rt.tenant_id
                WHERE rt.tenant_id=@TenantId AND rt.is_deleted=0 AND rt.status='confirmed'
                  AND rt.return_date BETWEEN @From AND @To GROUP BY ym
              ) pu GROUP BY ym
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
        //
        // 🔴 20260903작18 — <b>창고축 봉합.</b>
        //   사장님 오더: "마스타에서 창고1 = 재고 몆개, 창고2 = 재고 몆개 이렇게 관리하면 되잖아"
        //             → "그럼 수불부와, 마스터 재고가 안맞을 일이 없을거 같은데???"
        //
        //   사장님 말씀이 맞았다. 구조는 이미 창고별이다
        //   (item_stock UNIQUE (tenant_id,item_id,warehouse_id) · stock_ledger.warehouse_id NOT NULL).
        //   틀린 것은 데이터가 아니라 이 검사식이었다 — JOIN 에 warehouse_id 가 없어
        //   <b>창고 1곳의 재고를 원장 전체합과 비교</b>했다.
        //   예) 볼트너트 본창고 14 vs 원장전체 15 · 부창고 1 vs 원장전체 15 ⇒ 멀쩡한데 2건 불일치.
        //   실측(test1234): 거짓 경보 3건 — 창고를 합산하면 전 품목 차이 0 이었다.
        //
        //   ⚠️ GROUP BY 에도 warehouse_id 를 넣어야 창고별로 갈린다. JOIN 만 고치면
        //      같은 품목의 여러 창고 행이 한 줄로 뭉쳐 여전히 틀린다.
        var mismatch = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM (
              SELECT s.item_id, s.warehouse_id, s.current_qty,
                     COALESCE(SUM(l.qty_in)-SUM(l.qty_out),0) AS lq
              FROM item_stock s
              LEFT JOIN stock_ledger l ON l.item_id = s.item_id AND l.tenant_id = s.tenant_id
                                      AND l.warehouse_id = s.warehouse_id
              WHERE s.tenant_id = @T
              GROUP BY s.item_id, s.warehouse_id, s.current_qty
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

        // ═══════════════════════════════════════════════════════════════════
        // 🔴 20260827작6 — 사슬 정합성 검사 (사장님 지시)
        //
        //   *"ERP의 핵심인 입력과, 입력이 연결된 각 도구의 사슬과 조회의 사슬들의
        //     정합성에 집중해. 데이터가 맞아야 하고, 혹시나 데이터가 틀릴 경우,
        //     빠르게 틀린 데이터를 발견할 수 있어야 해"*
        //
        //   종전 8항목은 **재고·마스터**만 봤다. 회계 장부와 매입↔반품 사슬은
        //   **한 건도 안 봤다**(실측: journal_entries·purchase_returns 참조 0건).
        //   ⇒ 장부가 틀어져도, 사슬이 끊겨도 이 화면은 계속 초록불이었다.
        //
        //   아래는 **틀린 데이터를 빨리 찾는** 검사다. 고치는 건 사람이 한다.
        // ═══════════════════════════════════════════════════════════════════

        // ① 복식부기 검산 — 차변합 = 대변합. 이게 깨지면 장부 전체를 못 믿는다.
        var jeImbalance = await _db.QueryFirstOrDefaultAsync<decimal?>(new CommandDefinition(
            """
            SELECT COALESCE(SUM(debit_amount) - SUM(credit_amount), 0)
              FROM journal_lines WHERE tenant_id = @T
            """, new { T = tenantId }, cancellationToken: ct));
        var imbal = jeImbalance ?? 0m;
        items.Add(new IntegrityItem
        {
            Category = "회계",
            CheckName = "차변합 = 대변합",
            Status = imbal == 0m ? "OK" : "FAIL",
            Detail = imbal == 0m ? null : $"{imbal:N0}원 차이"
        });

        // ② 전표 단위 불균형 — 총합은 맞아도 개별 전표가 틀어질 수 있다.
        //   ①만 있으면 +100/-100 두 전표가 서로 상쇄해 안 보인다.
        var jeBadEntries = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM (
              SELECT entry_id FROM journal_lines WHERE tenant_id = @T
               GROUP BY entry_id
              HAVING SUM(debit_amount) <> SUM(credit_amount)
            ) x
            """, new { T = tenantId }, cancellationToken: ct));
        items.Add(new IntegrityItem
        {
            Category = "회계",
            CheckName = "전표별 차·대 균형",
            Status = jeBadEntries == 0 ? "OK" : "FAIL",
            Detail = jeBadEntries > 0 ? $"{jeBadEntries}건 불균형" : null
        });

        // ③ 계정과목 없는 분개 — FK 가 막아주지만, 마이그로 들어온 건은 뚫릴 수 있다.
        var jeOrphanAcct = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM journal_lines l
             LEFT JOIN accounts a ON a.tenant_id = l.tenant_id AND a.account_code = l.account_code
             WHERE l.tenant_id = @T AND a.account_code IS NULL
            """, new { T = tenantId }, cancellationToken: ct));
        items.Add(new IntegrityItem
        {
            Category = "회계",
            CheckName = "계정과목 참조",
            Status = jeOrphanAcct == 0 ? "OK" : "FAIL",
            Detail = jeOrphanAcct > 0 ? $"{jeOrphanAcct}건 미등록 계정" : null
        });

        // ④ 확정인데 기표 안 된 전표 — 재고는 움직였는데 장부에 없는 상태.
        //   🔴 이게 "숫자가 안 맞는" 대표 원인이다. 재고와 회계가 갈린다.
        var unpostedConfirmed = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            """
            SELECT
              (SELECT COUNT(*) FROM purchase_receipts pr
                WHERE pr.tenant_id=@T AND pr.status='confirmed'
                  AND NOT EXISTS (SELECT 1 FROM journal_entries j
                                   WHERE j.tenant_id=pr.tenant_id AND j.source_type='purchase'
                                     AND j.source_id=pr.receipt_id))
            + (SELECT COUNT(*) FROM purchase_returns r
                WHERE r.tenant_id=@T AND r.status='confirmed' AND r.is_deleted=0
                  AND NOT EXISTS (SELECT 1 FROM journal_entries j
                                   WHERE j.tenant_id=r.tenant_id AND j.source_type='purchase_return'
                                     AND j.source_id=r.return_id))
            + (SELECT COUNT(*) FROM sales_returns sr
                WHERE sr.tenant_id=@T AND sr.status='confirmed' AND sr.is_deleted=0
                  AND NOT EXISTS (SELECT 1 FROM journal_entries j
                                   WHERE j.tenant_id=sr.tenant_id AND j.source_type='sales_return'
                                     AND j.source_id=sr.return_id))
            """, new { T = tenantId }, cancellationToken: ct));
        items.Add(new IntegrityItem
        {
            Category = "회계",
            CheckName = "확정전표 기표 누락",
            Status = unpostedConfirmed == 0 ? "OK" : "FAIL",
            Detail = unpostedConfirmed > 0 ? $"{unpostedConfirmed}건 미기표" : null
        });

        // ⑤ 원 매입이 사라진 반품 — 사슬이 끊긴 상태.
        //   반품은 있는데 그 매입전표가 없으면 「반품전표」 대조가 불가능하다.
        var orphanReturn = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM purchase_returns r
             WHERE r.tenant_id=@T AND r.is_deleted=0
               AND r.receipt_id IS NOT NULL AND r.receipt_id <> ''
               AND NOT EXISTS (SELECT 1 FROM purchase_receipts pr
                                WHERE pr.tenant_id=r.tenant_id AND pr.receipt_id=r.receipt_id)
            """, new { T = tenantId }, cancellationToken: ct));
        items.Add(new IntegrityItem
        {
            Category = "매입",
            CheckName = "반품↔매입 사슬",
            Status = orphanReturn == 0 ? "OK" : "FAIL",
            Detail = orphanReturn > 0 ? $"{orphanReturn}건 원전표 없음" : null
        });

        // ⑥ 원 발주가 사라진 매입 — 같은 축(발주↔매입 대조).
        var orphanReceipt = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM purchase_receipts pr
             WHERE pr.tenant_id=@T
               AND pr.po_id IS NOT NULL AND pr.po_id <> ''
               AND NOT EXISTS (SELECT 1 FROM purchase_orders po
                                WHERE po.tenant_id=pr.tenant_id AND po.po_id=pr.po_id)
            """, new { T = tenantId }, cancellationToken: ct));
        items.Add(new IntegrityItem
        {
            Category = "매입",
            CheckName = "매입↔발주 사슬",
            Status = orphanReceipt == 0 ? "OK" : "FAIL",
            Detail = orphanReceipt > 0 ? $"{orphanReceipt}건 원전표 없음" : null
        });

        // ⑦ 반품 수량이 매입 수량을 넘는 건 — 100개 받아 120개 반품은 있을 수 없다.
        //   🔴 넘으면 재고가 음수로 가거나 매입액이 마이너스가 된다.
        var overReturn = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM (
              SELECT r.receipt_id, ri.item_id,
                     SUM(ri.qty) AS ret_qty,
                     (SELECT COALESCE(SUM(pi.qty),0) FROM purchase_receipt_items pi
                       WHERE pi.receipt_id=r.receipt_id AND pi.item_id=ri.item_id) AS buy_qty
                FROM purchase_returns r
                JOIN purchase_return_items ri ON ri.return_id=r.return_id
               WHERE r.tenant_id=@T AND r.is_deleted=0 AND r.status<>'canceled'
                 AND r.receipt_id IS NOT NULL AND r.receipt_id <> ''
               GROUP BY r.receipt_id, ri.item_id
              HAVING ret_qty > buy_qty
            ) x
            """, new { T = tenantId }, cancellationToken: ct));
        items.Add(new IntegrityItem
        {
            Category = "매입",
            CheckName = "반품수량 ≤ 매입수량",
            Status = overReturn == 0 ? "OK" : "FAIL",
            Detail = overReturn > 0 ? $"{overReturn}건 초과반품" : null
        });

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
            SELECT 'month_purchase',
              COALESCE((SELECT SUM(total_amount + vat_amount) FROM purchase_receipts WHERE tenant_id=@TenantId AND status='confirmed'
                AND receipt_date>=@MonthStart AND receipt_date<=@Today), 0)
              - COALESCE((SELECT SUM(rti.supply_amount + rti.vat_amount) FROM purchase_returns rt
                LEFT JOIN purchase_return_items rti ON rti.return_id=rt.return_id AND rti.tenant_id=rt.tenant_id
                WHERE rt.tenant_id=@TenantId AND rt.is_deleted=0 AND rt.status='confirmed'
                  AND rt.return_date>=@MonthStart AND rt.return_date<=@Today), 0)
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
              - COALESCE((SELECT COALESCE(SUM(rti.supply_amount + rti.vat_amount),0) FROM purchase_returns rt LEFT JOIN purchase_return_items rti ON rti.return_id=rt.return_id AND rti.tenant_id=rt.tenant_id WHERE rt.tenant_id=@TenantId AND rt.is_deleted=0 AND rt.status='confirmed'), 0)
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
              SELECT ym, SUM(amt) AS amt FROM (
                SELECT DATE_FORMAT(receipt_date, '%Y-%m-01') AS ym,
                       SUM(total_amount + vat_amount) AS amt
                FROM purchase_receipts
                WHERE tenant_id = @TenantId AND status = 'confirmed'
                  AND receipt_date >= @Start
                GROUP BY ym
                UNION ALL
                SELECT DATE_FORMAT(rt.return_date, '%Y-%m-01') AS ym,
                       -SUM(rti.supply_amount + rti.vat_amount) AS amt
                FROM purchase_returns rt
                LEFT JOIN purchase_return_items rti ON rti.return_id=rt.return_id AND rti.tenant_id=rt.tenant_id
                WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
                  AND rt.return_date >= @Start
                GROUP BY ym
              ) pu GROUP BY ym
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
    // 회계장부 — 합계잔액시산표 (20260827작4)
    // ═══════════════════════════════════════

    /// <summary>
    /// 🔴 20260827작4 — <b>합계잔액시산표.</b> 사장님 오더의 본안이다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 사장님: <i>"매입매출, 그밖에 모든 돈의 흐름을 한번에, <b>전체 돈 숫자가 모이는 곳</b>"</i>
    /// · <i>"13개의 모든 돈 흐름이 모이는 회계장부가 필요한데, 회계에선 이게 핵심인데, 이 기능이 빠졌네"</i>
    /// </para>
    ///
    /// <para>
    /// 🔴 <b>왜 이게 없었나</b> — 실측 결과 <c>journal_entries</c>/<c>journal_lines</c> 를
    /// <b>읽는 조회 화면이 이 시스템에 0건</b>이었다. 13개 회계 화면이 전부 원본 테이블
    /// (<c>sales_deliveries</c>·<c>purchase_receipts</c>·<c>expenses</c>)을 각자 직독한다.
    /// <b>분개를 쌓기만 하고 여는 문이 없었다.</b> 이 메서드가 그 첫 문이다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>읽기 전용이다</b> — 분개 생성은 각 업무(매입확정·판매확정 등)가 한다.
    /// 여기서 <c>journal_lines</c> 를 쓰지 않는다(헌법 #3 INSERT ONLY 원장).
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>기간 필수</b> — 무기한 조회를 허용하면 전표 수만 건에서 화면이 죽는다.
    /// 화면이 기본 당월을 보낸다. 인덱스 <c>idx_je_tenant_date</c> 적중.
    /// </para>
    /// </remarks>
    public async Task<TrialBalanceDto> GetTrialBalanceAsync(
        string tenantId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        // 🔴 문자열 조립을 하지 않는다 — 20260826작2 의 `0AND` 500 사고 자리.
        //   조건은 전부 (@X IS NULL OR ...) 로 고정 SQL 안에 둔다.
        //
        // 🔴 잔액 방향은 계정 성격이 정한다:
        //     자산(asset)·비용(expense)   → 차변 − 대변
        //     부채(liability)·자본(equity)·수익(revenue) → 대변 − 차변
        //   화면이 회계 규칙을 다시 알 필요 없게 **서버가 계산해서** 내려보낸다.
        //
        // ⚠️ INNER JOIN accounts 가 아니라 LEFT JOIN 이다. 계정과목이 지워졌거나
        //   시드에 없는 코드로 기표된 분개가 있어도 **시산표에서 사라지면 안 된다**
        //   (사라지면 차변합≠대변합 이 되어 검산이 거짓으로 깨진다).
        const string sql = """
            SELECT jl.account_code                                   AS AccountCode,
                   COALESCE(a.account_name, CONCAT('(미등록 ', jl.account_code, ')')) AS AccountName,
                   COALESCE(a.account_type, 'unknown')               AS AccountType,
                   COALESCE(SUM(jl.debit_amount), 0)                 AS DebitTotal,
                   COALESCE(SUM(jl.credit_amount), 0)                AS CreditTotal,
                   CASE
                     WHEN COALESCE(a.account_type,'') IN ('asset','expense')
                       THEN COALESCE(SUM(jl.debit_amount),0) - COALESCE(SUM(jl.credit_amount),0)
                     ELSE COALESCE(SUM(jl.credit_amount),0) - COALESCE(SUM(jl.debit_amount),0)
                   END                                               AS Balance
              FROM journal_lines jl
              JOIN journal_entries je
                ON je.entry_id = jl.entry_id AND je.tenant_id = jl.tenant_id
              LEFT JOIN accounts a
                ON a.account_code = jl.account_code AND a.tenant_id = jl.tenant_id
             WHERE jl.tenant_id = @TenantId
               AND (@From IS NULL OR je.entry_date >= @From)
               AND (@To   IS NULL OR je.entry_date <= @To)
             GROUP BY jl.account_code, a.account_name, a.account_type, a.sort_order
             ORDER BY COALESCE(a.sort_order, 999999), jl.account_code
            """;

        var rows = (await _db.QueryAsync<TrialBalanceRowDto>(new CommandDefinition(
            sql,
            new { TenantId = tenantId, From = from?.Date, To = to?.Date },
            cancellationToken: ct))).ToList();

        var dto = new TrialBalanceDto
        {
            Rows = rows,
            TotalDebit = rows.Sum(r => r.DebitTotal),
            TotalCredit = rows.Sum(r => r.CreditTotal)
        };

        // 🔴 검산 — 복식부기는 차변합 = 대변합 이 항상 성립해야 한다.
        //   decimal 이라 == 비교가 안전하다(헌법 #4 — float 였으면 이 비교가 못 미더웠을 자리).
        dto.IsBalanced = dto.TotalDebit == dto.TotalCredit;

        // ⚠️ 아직 기표되지 않는 업무를 화면에 알려준다(20260827 설계 결재서 §4).
        //   이게 없으면 담당자가 "수금 100건인데 장부에 0건" 을 보고 고장으로 오해한다.
        //   🔴 "없는 것을 없다고 말해주는 것" 도 기능이다 — 히트판 정신(쉬움).
        await AppendUnpostedNoticeAsync(dto, tenantId, from, to, ct);

        return dto;
    }

    /// <summary>
    /// 아직 분개로 안 들어오는 업무의 건수를 세어 안내문 재료를 채운다.
    /// </summary>
    /// <remarks>
    /// 🔴 이 메서드는 <b>한시적</b>이다. 수금·지급·경비·급여 기표가 붙으면(3·4차)
    /// 건수가 0 이 되어 안내문이 저절로 사라진다. 그때 이 코드를 지운다.
    ///
    /// 🔴 <b>20260827작4 — 기표 배선 후 판정 방식을 바꿨다.</b>
    /// 종전엔 <c>collections/payments/expenses</c> 행을 <b>그냥 다 셌다</b>. 그땐 기표가
    /// 아예 0건이라 "행이 있다 = 미기표"가 참이었다. 이제 기표가 붙었으므로 그 셈법은
    /// <b>이미 장부에 올라간 건까지 미기표로 세는 거짓 경고</b>가 된다.
    /// ⇒ <c>journal_entries</c> 에 그 <c>source_id</c> 가 있는지로 판정한다.
    /// 옛 데이터(배선 전에 쌓인 건)는 분개가 없으므로 그대로 잡힌다 — 그게 맞는 동작이다.
    /// </remarks>
    private async Task AppendUnpostedNoticeAsync(
        TrialBalanceDto dto, string tenantId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        // ⚠️ 표가 없는 고객 DB 도 있을 수 있다(마이그 시점 차이) — 실패해도 시산표 본안은 살린다.
        try
        {
            // ⚠️ 판정 기준 = "분개가 있느냐"이지 "행이 있느냐"가 아니다.
            //   NOT EXISTS 서브쿼리가 journal_entries 를 source_type+source_id 로 되짚는다.
            const string sql = """
                SELECT
                  (SELECT COUNT(*) FROM collections c
                    WHERE c.tenant_id=@TenantId
                      AND (@From IS NULL OR c.collection_date >= @From)
                      AND (@To   IS NULL OR c.collection_date <= @To)
                      AND NOT EXISTS (SELECT 1 FROM journal_entries j
                                       WHERE j.tenant_id=c.tenant_id
                                         AND j.source_type='collection'
                                         AND j.source_id=c.collection_id))  AS c1,
                  (SELECT COUNT(*) FROM payments p
                    WHERE p.tenant_id=@TenantId
                      AND (@From IS NULL OR p.payment_date >= @From)
                      AND (@To   IS NULL OR p.payment_date <= @To)
                      AND NOT EXISTS (SELECT 1 FROM journal_entries j
                                       WHERE j.tenant_id=p.tenant_id
                                         AND j.source_type='payment'
                                         AND j.source_id=p.payment_id))     AS c2,
                  (SELECT COUNT(*) FROM expenses e
                    WHERE e.tenant_id=@TenantId
                      AND (@From IS NULL OR e.expense_date >= @From)
                      AND (@To   IS NULL OR e.expense_date <= @To)
                      AND NOT EXISTS (SELECT 1 FROM journal_entries j
                                       WHERE j.tenant_id=e.tenant_id
                                         AND j.source_type='expense'
                                         AND j.source_id=e.expense_id))     AS c3,
                  -- 🔴 20260827작4 — 급여가 빠져 있었다(PM 누락). 수금·지급·경비만 세고 있었다.
                  (SELECT COUNT(*) FROM payroll_slips s
                    WHERE s.tenant_id=@TenantId
                      AND (@From IS NULL OR s.pay_date >= @From)
                      AND (@To   IS NULL OR s.pay_date <= @To)
                      AND NOT EXISTS (SELECT 1 FROM journal_entries j
                                       WHERE j.tenant_id=s.tenant_id
                                         AND j.source_type='payroll'
                                         AND j.source_id=s.slip_id))        AS c4
                """;

            var r = await _db.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(
                sql, new { TenantId = tenantId, From = from?.Date, To = to?.Date },
                cancellationToken: ct));
            if (r is null) return;

            // ⚠️ COUNT 는 드라이버에 따라 long 으로 온다 — Convert 로 전 타입을 받는다
            //   (dynamic 에 (int) 캐스팅하면 RuntimeBinderException 으로 500 이 난다. 8/25 사고 자리).
            var c1 = Convert.ToInt32(r.c1);
            var c2 = Convert.ToInt32(r.c2);
            var c3 = Convert.ToInt32(r.c3);
            var c4 = Convert.ToInt32(r.c4);

            if (c1 > 0) dto.UnpostedSources.Add($"수금 {c1:N0}건");
            if (c2 > 0) dto.UnpostedSources.Add($"지급 {c2:N0}건");
            if (c3 > 0) dto.UnpostedSources.Add($"경비 {c3:N0}건");
            if (c4 > 0) dto.UnpostedSources.Add($"급여 {c4:N0}건");
            dto.UnpostedCount = c1 + c2 + c3 + c4;
        }
        catch (Exception ex)
        {
            // 헌법 #15 — 빈 catch 금지. 이 클래스엔 ILogger 주입이 없어(생성자 실측)
            //   같은 파일의 기존 관례대로 stderr 에 남긴다. 안내문은 부가 기능이므로
            //   실패해도 본안(시산표)은 그대로 반환한다.
            Console.Error.WriteLine(
                "[작4] 미기표 안내 집계 실패 — 시산표 본안은 정상 반환한다: "
                + ex.GetType().Name + ": " + ex.Message);
        }
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
