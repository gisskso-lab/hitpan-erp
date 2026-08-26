using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Payroll;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>
/// 급여·퇴직금 구현. 작(2026-08-13) 그룹웨어 단계8.
/// </summary>
/// <remarks>
/// 🔴 <b>이 클래스는 급여를 계산하지 않는다.</b>
/// 사장님(2026-08-13): <i>"급여는 자동계산하지 말고 수동으로 int값 직접 받아서 입력하는게 가장 깔끔함"</i> /
/// <i>"각 고객사 니즈나 사정도 부합시킬 수 있고."</i>
///
/// 4대보험 요율·간이세액표를 우리가 돌리지 않는 이유:
/// <list type="bullet">
///   <item>국민연금 9%→9.5%(2026-01) · 건강보험 7.09%→7.19%(2026-01) ·
///         간이세액표 개정(2026-02) — <b>매년 바뀐다.</b></item>
///   <item>회사마다 상여 주기·수당 종류·비과세 항목이 전부 다르다.</item>
///   <item>틀리면 <b>직원 돈이 틀린다.</b> 되돌리기 어렵다.</item>
/// </list>
///
/// ⇒ 서버가 하는 계산은 <b>줄의 합계</b>뿐이다. 그것도 화면이 보내온 합계를 안 믿기 위해서다 —
///    줄과 합계가 어긋난 명세가 저장되면 명세서와 회계 숫자가 갈라진다.
///
/// 🔴 <b>보호는 권한 계층이 한다</b>(사장님: <i>"권한 계층분리로 급여를 관리해도 충분히 됨"</i>).
/// 금액은 평문이고 컨트롤러가 <c>menu_code='PAYROLL'</c> 로 막는다.
/// </remarks>
public sealed class PayrollService : IPayrollService
{
    /// <summary>
    /// 급여명세서 결재·PDF 문서타입 (20260826작6).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>리터럴을 여기저기 적지 않는다.</b> <c>PdfRenderService.PayslipDocType</c> 하나를 가리킨다 —
    /// 같은 뜻을 서로 다른 자리에 문자열로 적어두면 한쪽만 고쳐져 조용히 갈린다
    /// (8/25 창고분리가 그 병으로 통째로 죽었다).
    /// </remarks>
    private const string PayslipDocType = PdfRenderService.PayslipDocType;

    /// <summary>
    /// 결재 목록에 보일 짧은 참조번호. <b>길이를 가정하지 않는다.</b>
    /// </summary>
    /// <remarks>
    /// ⚠️ 종전엔 <c>slipId[..8]</c> 이었는데 <b>id 가 8자보다 짧으면 그 자리에서 죽었다</b>
    /// (실측에서 잡았다 — <c>ArgumentOutOfRangeException</c>).
    /// 지금은 GUID 라 8자를 넘지만, <b>남의 id 길이를 가정한 코드는 언젠가 터진다.</b>
    /// 더구나 이 호출은 <c>catch</c> 안에서도 쓰여서, 예외 메시지를 만들다 **또 죽는** 자리였다.
    /// </remarks>
    private static string ShortRef(string id) =>
        string.IsNullOrEmpty(id) ? "-" : (id.Length <= 8 ? id : id[..8]);

    private readonly IDbConnection _db;
    private readonly IAuditService? _audit;

    public PayrollService(IDbConnection db, IAuditService? audit = null)
    {
        _db = db;
        _audit = audit;
    }

    // ───────────────────────────────────────────────────────────────
    // 급여 명세 — 조회
    // ───────────────────────────────────────────────────────────────

    private const string SlipSelectSql =
        """
        SELECT
          s.slip_id       AS SlipId,
          s.employee_id   AS EmployeeId,
          e.emp_name      AS EmployeeName,
          d.dept_name     AS DeptName,
          s.pay_year      AS PayYear,
          s.pay_month     AS PayMonth,
          s.pay_date      AS PayDate,
          s.total_payment AS TotalPayment,
          s.total_deduct  AS TotalDeduct,
          s.net_payment   AS NetPayment,
          s.status        AS Status,
          s.confirmed_by  AS ConfirmedBy,
          s.confirmed_at  AS ConfirmedAt,
          s.absence_id    AS AbsenceId,
          s.memo          AS Memo,
          s.created_at    AS CreatedAt
        FROM payroll_slips s
        LEFT JOIN employees e   ON e.employee_id = s.employee_id AND e.tenant_id = s.tenant_id
        LEFT JOIN departments d ON d.dept_id = e.dept_id         AND d.tenant_id = s.tenant_id
        """;

    public async Task<List<PayrollSlipDto>> GetSlipsAsync(string tenantId, int year, int month,
        string? employeeId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var slips = (await _db.QueryAsync<PayrollSlipDto>(new CommandDefinition(
            SlipSelectSql +
            """

            WHERE s.tenant_id = @TenantId
              AND s.pay_year  = @Year
              AND s.pay_month = @Month
              AND (@EmployeeId IS NULL OR s.employee_id = @EmployeeId)
            ORDER BY e.emp_name
            """,
            new { TenantId = tenantId, Year = year, Month = month, EmployeeId = employeeId },
            cancellationToken: ct)).ConfigureAwait(false)).ToList();

        if (slips.Count == 0) return slips;

        // 항목을 한 번에 가져와 붙인다.
        // ⚠️ 명세마다 따로 물어보면 사원 수만큼 왕복한다(N+1) — 급여일에 전 직원을 연다.
        var ids = slips.Select(s => s.SlipId).ToList();
        var lines = (await _db.QueryAsync<PayrollSlipLineDto>(new CommandDefinition(
            """
            SELECT line_id AS LineId, slip_id AS SlipId, line_type AS LineType,
                   item_name AS ItemName, amount AS Amount,
                   sort_order AS SortOrder, is_taxable AS IsTaxable, memo AS Memo
            FROM payroll_slip_lines
            WHERE tenant_id = @TenantId AND slip_id IN @Ids
            ORDER BY line_type, sort_order
            """,
            new { TenantId = tenantId, Ids = ids }, cancellationToken: ct)).ConfigureAwait(false))
            .ToList();

        foreach (var slip in slips)
        {
            slip.Lines = lines.Where(l => l.SlipId == slip.SlipId).ToList();
        }

        return slips;
    }

    public async Task<PayrollSlipDto?> GetSlipAsync(string tenantId, string slipId,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var slip = await _db.QueryFirstOrDefaultAsync<PayrollSlipDto>(new CommandDefinition(
            SlipSelectSql +
            """

            WHERE s.tenant_id = @TenantId AND s.slip_id = @SlipId
            """,
            new { TenantId = tenantId, SlipId = slipId }, cancellationToken: ct)).ConfigureAwait(false);

        if (slip is null) return null;

        slip.Lines = (await _db.QueryAsync<PayrollSlipLineDto>(new CommandDefinition(
            """
            SELECT line_id AS LineId, slip_id AS SlipId, line_type AS LineType,
                   item_name AS ItemName, amount AS Amount,
                   sort_order AS SortOrder, is_taxable AS IsTaxable, memo AS Memo
            FROM payroll_slip_lines
            WHERE tenant_id = @TenantId AND slip_id = @SlipId
            ORDER BY line_type, sort_order
            """,
            new { TenantId = tenantId, SlipId = slipId }, cancellationToken: ct)).ConfigureAwait(false))
            .ToList();

        return slip;
    }

    // ───────────────────────────────────────────────────────────────
    // 참고 자료 — 보여만 준다
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 그 달 급여를 만들 때 참고할 것들. 🔴 <b>자동으로 채우지 않는다.</b>
    /// </summary>
    /// <remarks>
    /// 사장님: <i>"휴직시 급여 : 텍스트 박스로 수동입력 → 그러면 자연스럽게 급여, 회계이슈도 해결될듯"</i>
    ///
    /// 단계6 에서 <b>사람이 정해 둔</b> 휴직 급여를 가져와 보여준다.
    /// 급여 명세에 <b>넣지는 않는다</b> — 넣을지 말지는 담당자가 정한다(반자동 원칙).
    /// </remarks>
    public async Task<List<PayrollContextDto>> GetContextAsync(string tenantId, int year, int month,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var first = new DateTime(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        var rows = (await _db.QueryAsync<PayrollContextDto>(new CommandDefinition(
            """
            SELECT
              e.employee_id  AS EmployeeId,
              e.emp_name     AS EmployeeName,
              d.dept_name    AS DeptName,
              e.position     AS Position,
              e.work_status  AS WorkStatus,
              a.absence_id   AS AbsenceId,
              a.pay_amount   AS AbsencePayAmount,
              a.reason       AS AbsenceReason,
              a.start_date   AS AbsenceStart,
              a.end_date     AS AbsenceEnd,
              s.slip_id      AS ExistingSlipId,
              s.status       AS ExistingStatus
            FROM employees e
            LEFT JOIN departments d
              ON d.dept_id = e.dept_id AND d.tenant_id = e.tenant_id
            LEFT JOIN employee_leave_of_absence a
              ON  a.employee_id = e.employee_id
              AND a.tenant_id   = e.tenant_id
              AND a.status IN ('approved', 'active', 'returned')
              AND a.start_date <= @Last
              AND COALESCE(a.actual_return_date, a.end_date) >= @First
            LEFT JOIN payroll_slips s
              ON  s.employee_id = e.employee_id
              AND s.tenant_id   = e.tenant_id
              AND s.pay_year    = @Year
              AND s.pay_month   = @Month
            WHERE e.tenant_id = @TenantId
              AND e.is_active = 1
            ORDER BY e.emp_name
            """,
            new { TenantId = tenantId, Year = year, Month = month, First = first, Last = last },
            cancellationToken: ct)).ConfigureAwait(false)).ToList();

        foreach (var r in rows)
        {
            // 🔴 담당자가 그냥 지나치면 안 되는 것들을 글로 말해 준다.
            //    막지는 않는다 — 판단은 사람이 한다.
            if (r.AbsenceId is not null)
            {
                r.Notes.Add(
                    $"이 달에 휴직이 있습니다({r.AbsenceStart:yyyy-MM-dd} ~ {r.AbsenceEnd:yyyy-MM-dd})"
                    + (string.IsNullOrWhiteSpace(r.AbsenceReason) ? "" : $" — {r.AbsenceReason}")
                    + $". 정해 둔 지급액은 {r.AbsencePayAmount:#,0}원입니다.");
            }

            if (r.ExistingSlipId is not null)
            {
                r.Notes.Add($"{year}년 {month}월 명세가 이미 있습니다"
                            + $"({PayrollStatusLabels.Of(r.ExistingStatus)}).");
            }
        }

        return rows;
    }

    // ───────────────────────────────────────────────────────────────
    // 급여 명세 — 저장
    // ───────────────────────────────────────────────────────────────

    public async Task<string> SaveSlipAsync(string tenantId, string actorId,
        SavePayrollSlipRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(request.EmployeeId))
            throw new InvalidOperationException("급여 대상 사원을 선택하세요.");

        if (request.PayYear < 2000 || request.PayYear > 2100)
            throw new InvalidOperationException("귀속 연도를 확인하세요.");

        if (request.PayMonth is < 1 or > 12)
            throw new InvalidOperationException("귀속 월은 1~12 사이여야 합니다.");

        var empExists = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM employees WHERE tenant_id=@TenantId AND employee_id=@EmpId",
            new { TenantId = tenantId, EmpId = request.EmployeeId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (empExists == 0)
            throw new InvalidOperationException("급여를 넣을 수 없는 사원입니다.");

        // 🔴 합계는 **서버가 줄을 더해서** 낸다. 화면이 보내온 합계를 믿지 않는다 —
        //    줄과 합계가 어긋난 명세가 저장되면 명세서와 회계 숫자가 갈라진다.
        var lines = request.Lines ?? new List<PayrollSlipLineDto>();

        foreach (var l in lines)
        {
            if (string.IsNullOrWhiteSpace(l.ItemName))
                throw new InvalidOperationException("항목 이름을 넣으세요.");

            if (l.Amount < 0)
                throw new InvalidOperationException($"'{l.ItemName}' 금액은 0보다 작을 수 없습니다.");

            if (!PayrollLineTypeLabels.All.ContainsKey(l.LineType))
                throw new InvalidOperationException($"알 수 없는 항목 구분입니다: {l.LineType}");
        }

        var totalPayment = lines.Where(l => l.LineType == PayrollLineTypeLabels.Payment).Sum(l => l.Amount);
        var totalDeduct = lines.Where(l => l.LineType == PayrollLineTypeLabels.Deduct).Sum(l => l.Amount);
        var netPayment = totalPayment - totalDeduct;

        var isNew = string.IsNullOrWhiteSpace(request.SlipId);
        var slipId = isNew ? Guid.NewGuid().ToString() : request.SlipId!;

        // 이 달에 걸린 휴직이 있으면 이어 둔다(어느 명세가 휴직분인지 나중에 알아야 한다).
        var absenceId = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            """
            SELECT absence_id FROM employee_leave_of_absence
            WHERE tenant_id = @TenantId AND employee_id = @EmpId
              AND status IN ('approved', 'active', 'returned')
              AND start_date <= @Last
              AND COALESCE(actual_return_date, end_date) >= @First
            LIMIT 1
            """,
            new
            {
                TenantId = tenantId,
                EmpId = request.EmployeeId,
                First = new DateTime(request.PayYear, request.PayMonth, 1),
                Last = new DateTime(request.PayYear, request.PayMonth, 1).AddMonths(1).AddDays(-1)
            },
            cancellationToken: ct)).ConfigureAwait(false);

        // 🔴 머리와 줄을 ★같은 트랜잭션★ 으로. 따로 하면 합계는 바뀌었는데 줄은 옛것이 남는다.
        using var tx = _db.BeginTransaction();
        try
        {
            if (isNew)
            {
                // 같은 사람의 같은 달 명세는 하나뿐이다. 두 장이면 어느 것이 진짜인지 모른다.
                var dup = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
                    """
                    SELECT COUNT(*) FROM payroll_slips
                    WHERE tenant_id = @TenantId AND employee_id = @EmpId
                      AND pay_year = @Year AND pay_month = @Month
                    """,
                    new { TenantId = tenantId, EmpId = request.EmployeeId, Year = request.PayYear, Month = request.PayMonth },
                    transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

                if (dup > 0)
                {
                    throw new InvalidOperationException(
                        $"{request.PayYear}년 {request.PayMonth}월 명세가 이미 있습니다. 그 명세를 고치세요.");
                }

                await _db.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO payroll_slips
                      (slip_id, tenant_id, employee_id, pay_year, pay_month, pay_date,
                       total_payment, total_deduct, net_payment, status, absence_id, memo, created_by)
                    VALUES
                      (@SlipId, @TenantId, @EmpId, @Year, @Month, @PayDate,
                       @TotalPayment, @TotalDeduct, @NetPayment, 'draft', @AbsenceId, @Memo, @ActorId)
                    """,
                    new
                    {
                        SlipId = slipId, TenantId = tenantId, EmpId = request.EmployeeId,
                        Year = request.PayYear, Month = request.PayMonth, PayDate = request.PayDate?.Date,
                        TotalPayment = totalPayment, TotalDeduct = totalDeduct, NetPayment = netPayment,
                        AbsenceId = absenceId, Memo = request.Memo, ActorId = actorId
                    },
                    transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            }
            else
            {
                // 확정한 명세는 못 고친다 — 확정한 급여가 뒤에서 바뀌면 명세서가 거짓이 된다.
                var status = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
                    "SELECT status FROM payroll_slips WHERE tenant_id=@TenantId AND slip_id=@SlipId",
                    new { TenantId = tenantId, SlipId = slipId },
                    transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

                if (status is null)
                    throw new InvalidOperationException("급여 명세를 찾을 수 없습니다.");

                if (status is not "draft")
                    throw new InvalidOperationException(
                        $"이미 {PayrollStatusLabels.Of(status)} 상태라 고칠 수 없습니다.");

                await _db.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE payroll_slips
                    SET pay_date      = @PayDate,
                        total_payment = @TotalPayment,
                        total_deduct  = @TotalDeduct,
                        net_payment   = @NetPayment,
                        absence_id    = @AbsenceId,
                        memo          = @Memo,
                        updated_at    = NOW(6)
                    WHERE tenant_id = @TenantId AND slip_id = @SlipId
                    """,
                    new
                    {
                        SlipId = slipId, TenantId = tenantId, PayDate = request.PayDate?.Date,
                        TotalPayment = totalPayment, TotalDeduct = totalDeduct, NetPayment = netPayment,
                        AbsenceId = absenceId, Memo = request.Memo
                    },
                    transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

                // 줄은 통째로 다시 쓴다. 항목이 늘고 줄고 이름이 바뀌므로 맞춰 지우는 것보다 안전하다.
                await _db.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM payroll_slip_lines WHERE tenant_id=@TenantId AND slip_id=@SlipId",
                    new { TenantId = tenantId, SlipId = slipId },
                    transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            }

            var order = 0;
            foreach (var l in lines)
            {
                await _db.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO payroll_slip_lines
                      (line_id, tenant_id, slip_id, line_type, item_name, amount,
                       sort_order, is_taxable, memo)
                    VALUES
                      (@LineId, @TenantId, @SlipId, @LineType, @ItemName, @Amount,
                       @SortOrder, @IsTaxable, @Memo)
                    """,
                    new
                    {
                        LineId = Guid.NewGuid().ToString(),
                        TenantId = tenantId,
                        SlipId = slipId,
                        LineType = l.LineType,
                        ItemName = l.ItemName,
                        Amount = l.Amount,
                        SortOrder = l.SortOrder > 0 ? l.SortOrder : ++order,
                        IsTaxable = l.IsTaxable ? 1 : 0,
                        Memo = l.Memo
                    },
                    transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        if (_audit is not null)
        {
            // 급여는 돈이다. 누가 언제 얼마로 만들었는지 남아야 한다.
            var json = $"{{\"pay\":\"{request.PayYear}-{request.PayMonth:00}\","
                       + $"\"net\":{netPayment}}}";
            await _audit.LogAsync(isNew ? "create" : "update", "payroll_slip", slipId,
                afterJson: json, ct: ct).ConfigureAwait(false);
        }

        return slipId;
    }

    public async Task ConfirmSlipAsync(string tenantId, string actorId, string slipId,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var affected = await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE payroll_slips
            SET status = 'confirmed', confirmed_by = @ActorId, confirmed_at = NOW(6),
                updated_at = NOW(6)
            WHERE tenant_id = @TenantId AND slip_id = @SlipId AND status = 'draft'
            """,
            new { TenantId = tenantId, SlipId = slipId, ActorId = actorId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (affected == 0)
            throw new InvalidOperationException("확정할 수 없습니다. 이미 확정됐거나 없는 명세입니다.");

        if (_audit is not null)
        {
            await _audit.LogAsync("confirm", "payroll_slip", slipId, ct: ct).ConfigureAwait(false);
        }

        // 🔴 20260826작6 — 확정하면 결재를 올린다. 사장님: "급여명세는 대표이사 결재로 넘길거임."
        //
        //   ⚠️ **금액에는 손대지 않는다.** 사장님(2026-08-26): "결재승인은 급여표에 써지는 내용을
        //     건들라는게 아니고, **이미 써진 급여표의 동작만 표시**하면 되는거."
        //     여기서 하는 일은 이미 손으로 넣은 명세를 결재로 **넘기는 것**뿐이다 —
        //     수동입력 원칙(2026-08-21)과 기존 게이트(결재승인은_급여표를_건드리지_않는다)에 어긋나지 않는다.
        //
        //   ⚠️ 결재선이 꺼져 있으면 조용히 통과한다(TryCreateApprovalAsync 안의 IsEnabled 판정).
        //     결재를 안 쓰는 회사도 급여는 확정할 수 있어야 한다(헌법 #20 — 흐름을 끊지 않는다).
        //
        //   ⚠️ 트리거 실패가 확정을 되돌리지 않는다 — 확정은 이미 커밋됐다. 초과근무·경비와 같은 축이다.
        //     다만 **삼키지 않는다**(헌법 #15) — 실패를 남겨야 왜 결재함에 안 뜨는지 알 수 있다.
        try
        {
            var info = await _db.QueryFirstOrDefaultAsync<(string EmpName, int PayYear, int PayMonth, decimal NetPayment, string EmployeeId)>(
                new CommandDefinition(
                    """
                    SELECT COALESCE(e.emp_name, '') AS EmpName,
                           s.pay_year    AS PayYear,
                           s.pay_month   AS PayMonth,
                           s.net_payment AS NetPayment,
                           s.employee_id AS EmployeeId
                    FROM payroll_slips s
                    LEFT JOIN employees e ON e.employee_id = s.employee_id AND e.tenant_id = s.tenant_id
                    WHERE s.tenant_id = @TenantId AND s.slip_id = @SlipId
                    """,
                    new { TenantId = tenantId, SlipId = slipId }, cancellationToken: ct)).ConfigureAwait(false);

            await ApprovalTriggerHelper.TryCreateApprovalAsync(
                _db, docType: PayslipDocType, refId: slipId, refNo: ShortRef(slipId),
                title: $"급여명세서: {info.PayYear}년 {info.PayMonth}월 {info.EmpName}",
                amount: info.NetPayment,
                tenantId: tenantId, requesterId: actorId, requesterName: string.Empty,
                ct: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"[ApprovalTrigger] 급여명세서 {ShortRef(slipId)} 결재 트리거 실패: {ex}");
        }
    }

    public async Task MarkPaidAsync(string tenantId, string actorId, string slipId, DateTime payDate,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        if (payDate == default)
            throw new InvalidOperationException("지급일을 넣으세요.");

        var affected = await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE payroll_slips
            SET status = 'paid', pay_date = @PayDate, updated_at = NOW(6)
            WHERE tenant_id = @TenantId AND slip_id = @SlipId AND status = 'confirmed'
            """,
            new { TenantId = tenantId, SlipId = slipId, PayDate = payDate.Date },
            cancellationToken: ct)).ConfigureAwait(false);

        if (affected == 0)
            throw new InvalidOperationException("지급 처리할 수 없습니다. 확정된 명세만 지급 처리됩니다.");

        if (_audit is not null)
        {
            await _audit.LogAsync("pay", "payroll_slip", slipId,
                afterJson: $"{{\"pay_date\":\"{payDate:yyyy-MM-dd}\"}}", ct: ct).ConfigureAwait(false);
        }

        // 🔴 20260827작4 (사장님 오더 "모든 돈의 흐름을 회계장부 하나로") — 급여 자동기표.
        //
        //   🔴 **왜 확정(confirmed)이 아니라 지급(paid) 시점인가**
        //     확정은 "명세를 잠갔다"이고, 지급이 "돈이 실제로 나갔다"이다.
        //     회계는 돈이 나간 시점을 잡아야 한다. 확정에서 기표하면, 확정만 하고
        //     지급을 안 한 달에도 현금이 빠져나간 것으로 장부에 남는다.
        //     매입·판매가 「확정」에서 기표하는 것과 시점 이름은 다르지만 축은 같다 —
        //     **거래가 실제로 성립한 순간**에 기표한다.
        //
        //   ⚠️ 사장님 헌법 "급여는 수동입력 원칙" — 금액을 시스템이 계산하지 않는다.
        //     여기서는 **이미 사람이 넣어 확정한 숫자를 그대로 회계로 옮길 뿐**이다.
        await PostPayrollJournalAsync(tenantId, actorId, slipId, payDate, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 지급 처리된 급여명세 1건을 분개로 옮긴다. 이미 기표됐으면 아무 일도 하지 않는다(멱등).
    /// </summary>
    /// <remarks>
    /// 차변 급여(총지급액) / 대변 예수금(공제액) + 현금·예금(실지급액) — 3줄 분개다.
    /// 총지급액과 실수령액의 차액(원천징수분)은 회사가 대신 보관했다 나라에 내는 돈이라
    /// 부채(예수금)로 잡는다. 자세한 근거는 AutoJournalHelper.RecordPayrollAsync 주석 참조.
    /// </remarks>
    private async Task PostPayrollJournalAsync(string tenantId, string actorId, string slipId,
        DateTime payDate, CancellationToken ct)
    {
        // 이미 기표됐나 — 두 번 지급 처리해도 인건비가 두 배로 잡히면 안 된다.
        var already = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM journal_entries WHERE tenant_id=@TenantId AND source_type='payroll' AND source_id=@Id",
            new { TenantId = tenantId, Id = slipId }, cancellationToken: ct)).ConfigureAwait(false);
        if (already > 0) return;

        var row = await _db.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(
            """
            SELECT s.total_payment, s.total_deduct, s.pay_year, s.pay_month,
                   COALESCE(e.emp_name, '') AS emp_name
              FROM payroll_slips s
              LEFT JOIN employees e ON e.employee_id = s.employee_id AND e.tenant_id = s.tenant_id
             WHERE s.tenant_id=@TenantId AND s.slip_id=@Id
            """,
            new { TenantId = tenantId, Id = slipId }, cancellationToken: ct)).ConfigureAwait(false);
        if (row is null) return;

        // ⚠️ decimal 은 드라이버에 따라 타입이 달리 온다 — Convert 로 전 타입을 받는다
        //   (dynamic 에 직접 캐스팅하면 RuntimeBinderException 으로 500. 8/25 사고 자리).
        var gross = Convert.ToDecimal(row.total_payment);
        var deduct = Convert.ToDecimal(row.total_deduct);
        var empName = row.emp_name as string ?? string.Empty;
        var memo = $"{Convert.ToInt32(row.pay_year)}년 {Convert.ToInt32(row.pay_month)}월 {empName}".Trim();

        // 급여 지급수단은 명세에 없다 — 계좌이체가 통상이므로 보통예금으로 본다.
        //   현금 지급이면 사장님이 수기로 대체분개한다("현금은 수기로").
        using var tx = _db.BeginTransaction();
        try
        {
            await AutoJournalHelper.RecordPayrollAsync(
                _db, tx, tenantId, slipId, payDate.Date, gross, deduct,
                "bank_transfer", actorId, memo, ct).ConfigureAwait(false);
            tx.Commit();
        }
        catch (Exception)
        {
            try { tx.Rollback(); }
            catch (Exception rbex) { System.Diagnostics.Trace.TraceWarning($"[PayrollService] 급여 기표 롤백 실패: {rbex.Message}"); }
            throw;
        }
    }

    public async Task CancelSlipAsync(string tenantId, string actorId, string slipId,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var affected = await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE payroll_slips
            SET status = 'cancelled', updated_at = NOW(6)
            WHERE tenant_id = @TenantId AND slip_id = @SlipId
              AND status IN ('draft', 'confirmed')
            """,
            new { TenantId = tenantId, SlipId = slipId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (affected == 0)
            throw new InvalidOperationException("취소할 수 없습니다. 이미 지급됐거나 없는 명세입니다.");

        if (_audit is not null)
        {
            await _audit.LogAsync("cancel", "payroll_slip", slipId, ct: ct).ConfigureAwait(false);
        }
    }

    // ───────────────────────────────────────────────────────────────
    // 퇴직금
    // ───────────────────────────────────────────────────────────────

    public async Task<List<SeverancePaymentDto>> GetSeveranceListAsync(string tenantId,
        string? employeeId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var rows = await _db.QueryAsync<SeverancePaymentDto>(new CommandDefinition(
            """
            SELECT
              v.severance_id     AS SeveranceId,
              v.employee_id      AS EmployeeId,
              e.emp_name         AS EmployeeName,
              d.dept_name        AS DeptName,
              v.join_date        AS JoinDate,
              v.resign_date      AS ResignDate,
              v.service_days     AS ServiceDays,
              v.avg_wage         AS AvgWage,
              v.severance_amount AS SeveranceAmount,
              v.tax_amount       AS TaxAmount,
              v.net_amount       AS NetAmount,
              v.pay_type         AS PayType,
              v.pay_date         AS PayDate,
              v.status           AS Status,
              v.calc_basis       AS CalcBasis,
              v.memo             AS Memo,
              v.confirmed_by     AS ConfirmedBy,
              v.confirmed_at     AS ConfirmedAt,
              v.created_at       AS CreatedAt
            FROM severance_payments v
            LEFT JOIN employees e   ON e.employee_id = v.employee_id AND e.tenant_id = v.tenant_id
            LEFT JOIN departments d ON d.dept_id = e.dept_id         AND d.tenant_id = v.tenant_id
            WHERE v.tenant_id = @TenantId
              AND (@EmployeeId IS NULL OR v.employee_id = @EmployeeId)
            ORDER BY v.resign_date DESC
            """,
            new { TenantId = tenantId, EmployeeId = employeeId },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    /// <summary>
    /// 퇴직금 저장. 🔴 <b>금액을 사람이 넣는다.</b> 법정 산식을 우리가 돌리지 않는다.
    /// </summary>
    /// <remarks>
    /// 산식(평균임금 × 30일 × 재직일수/365)이 있지만 평균임금에 상여·연차수당을 어떻게 넣는지가
    /// 회사마다 다르고 <b>다툼이 잦다</b>. 퇴직연금(DB·DC·IRP)이면 산식 자체가 다르다.
    /// 틀리면 <b>법적 분쟁</b>이 된다.
    ///
    /// ⚠️ 법정 퇴직금은 <b>최소</b>다 — 더 줄 순 있어도 덜 주면 위법이다.
    /// 그래서 더더욱 우리가 계산해 넣으면 안 된다.
    /// </remarks>
    public async Task<string> SaveSeveranceAsync(string tenantId, string actorId,
        SaveSeveranceRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(request.EmployeeId))
            throw new InvalidOperationException("퇴직금 대상 사원을 선택하세요.");

        if (request.JoinDate == default || request.ResignDate == default)
            throw new InvalidOperationException("입사일과 퇴사일을 넣으세요.");

        if (request.ResignDate.Date < request.JoinDate.Date)
            throw new InvalidOperationException("퇴사일이 입사일보다 빠릅니다.");

        foreach (var (name, value) in new[]
                 {
                     ("평균임금", request.AvgWage),
                     ("퇴직금", request.SeveranceAmount),
                     ("공제액", request.TaxAmount),
                 })
        {
            if (value < 0)
                throw new InvalidOperationException($"{name}은 0보다 작을 수 없습니다.");
        }

        // 실지급액만 서버가 뺀다(빼기 하나까지 사람에게 시키면 오타가 난다).
        var net = request.SeveranceAmount - request.TaxAmount;

        var payType = SeverancePayTypeLabels.All.ContainsKey(request.PayType ?? "")
            ? request.PayType! : "direct";

        var isNew = string.IsNullOrWhiteSpace(request.SeveranceId);
        var id = isNew ? Guid.NewGuid().ToString() : request.SeveranceId!;

        if (isNew)
        {
            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO severance_payments
                  (severance_id, tenant_id, employee_id, join_date, resign_date, service_days,
                   avg_wage, severance_amount, tax_amount, net_amount,
                   pay_type, pay_date, status, calc_basis, memo, created_by)
                VALUES
                  (@Id, @TenantId, @EmpId, @JoinDate, @ResignDate, @ServiceDays,
                   @AvgWage, @Severance, @Tax, @Net,
                   @PayType, @PayDate, 'draft', @CalcBasis, @Memo, @ActorId)
                """,
                new
                {
                    Id = id, TenantId = tenantId, EmpId = request.EmployeeId,
                    JoinDate = request.JoinDate.Date, ResignDate = request.ResignDate.Date,
                    ServiceDays = request.ServiceDays,
                    AvgWage = request.AvgWage, Severance = request.SeveranceAmount,
                    Tax = request.TaxAmount, Net = net,
                    PayType = payType, PayDate = request.PayDate?.Date,
                    CalcBasis = request.CalcBasis, Memo = request.Memo, ActorId = actorId
                },
                cancellationToken: ct)).ConfigureAwait(false);
        }
        else
        {
            var status = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
                "SELECT status FROM severance_payments WHERE tenant_id=@TenantId AND severance_id=@Id",
                new { TenantId = tenantId, Id = id }, cancellationToken: ct)).ConfigureAwait(false);

            if (status is null)
                throw new InvalidOperationException("퇴직금 건을 찾을 수 없습니다.");

            if (status is not "draft")
                throw new InvalidOperationException(
                    $"이미 {PayrollStatusLabels.Of(status)} 상태라 고칠 수 없습니다.");

            await _db.ExecuteAsync(new CommandDefinition(
                """
                UPDATE severance_payments
                SET join_date        = @JoinDate,
                    resign_date      = @ResignDate,
                    service_days     = @ServiceDays,
                    avg_wage         = @AvgWage,
                    severance_amount = @Severance,
                    tax_amount       = @Tax,
                    net_amount       = @Net,
                    pay_type         = @PayType,
                    pay_date         = @PayDate,
                    calc_basis       = @CalcBasis,
                    memo             = @Memo,
                    updated_at       = NOW(6)
                WHERE tenant_id = @TenantId AND severance_id = @Id
                """,
                new
                {
                    Id = id, TenantId = tenantId,
                    JoinDate = request.JoinDate.Date, ResignDate = request.ResignDate.Date,
                    ServiceDays = request.ServiceDays,
                    AvgWage = request.AvgWage, Severance = request.SeveranceAmount,
                    Tax = request.TaxAmount, Net = net,
                    PayType = payType, PayDate = request.PayDate?.Date,
                    CalcBasis = request.CalcBasis, Memo = request.Memo
                },
                cancellationToken: ct)).ConfigureAwait(false);
        }

        if (_audit is not null)
        {
            await _audit.LogAsync(isNew ? "create" : "update", "severance", id,
                afterJson: $"{{\"net\":{net}}}", ct: ct).ConfigureAwait(false);
        }

        return id;
    }

    public async Task ConfirmSeveranceAsync(string tenantId, string actorId, string severanceId,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var affected = await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE severance_payments
            SET status = 'confirmed', confirmed_by = @ActorId, confirmed_at = NOW(6),
                updated_at = NOW(6)
            WHERE tenant_id = @TenantId AND severance_id = @Id AND status = 'draft'
            """,
            new { TenantId = tenantId, Id = severanceId, ActorId = actorId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (affected == 0)
            throw new InvalidOperationException("확정할 수 없습니다. 이미 확정됐거나 없는 건입니다.");

        if (_audit is not null)
        {
            await _audit.LogAsync("confirm", "severance", severanceId, ct: ct).ConfigureAwait(false);
        }
    }

    // ───────────────────────────────────────────────────────────────

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open) return;
        if (_db is DbConnection dbConnection)
        {
            await dbConnection.OpenAsync(ct).ConfigureAwait(false);
            return;
        }
        _db.Open();
    }
}
