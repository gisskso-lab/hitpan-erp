using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Leave;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>
/// 휴직 구현. 작(2026-08-13) 그룹웨어 단계6.
/// </summary>
/// <remarks>
/// 🔴 <b>사장님이 정한 범위</b>(2026-08-13):
/// <i>"복잡하게 생각할것 없이 그냥 휴직은 상태처리, 상태확인 정도로만 써도 될듯."</i> /
/// <i>"휴직은 모든걸 다 수동으로."</i>
///
/// 그래서 이 클래스는 <b>계산을 하지 않는다.</b>
/// <list type="bullet">
///   <item>기간 — 사람이 넣은 날짜를 그대로 저장한다. 법정 한도와 비교하지 않는다.</item>
///   <item>급여 — 금액을 그대로 받는다. 무급/유급/일부지급을 <b>판단하지 않는다.</b>
///         사장님: <i>"휴직시 급여 : 텍스트 박스로 수동입력 → 그러면 자연스럽게 급여, 회계이슈도 해결될듯"</i>
///         맞다. 숫자가 있으면 급여도 회계도 그 숫자만 가져가면 된다.</item>
///   <item>사유 — 비고에 자유롭게 쓴다. 종류를 고르게 하지 않는다.
///         사장님: <i>"비고에 육아, 연수, 교육, 등 자유롭게 쓰면 됨"</i></item>
/// </list>
///
/// ⇒ 처음엔 법정 기준값(육아휴직 18개월 등)을 읽어 초과를 경고하는 구조로 짰다가
///    <b>전부 걷어냈다.</b> 사장님 지시대로면 비교할 기준 자체가 필요 없다.
///    기준을 두면 법이 바뀔 때마다 그 기준을 관리해야 하고, 그게 피하려던 복잡함이다.
///
/// 🔴 <b>휴가(<c>leave_requests</c>)와 왜 나눴는지</b> — 실측 근거다. "합치자" 가 또 나올 것이라 남긴다.
/// <list type="number">
///   <item>휴가 표의 <c>leave_days</c> 는 <c>decimal(3,1)</c> = 최대 99.9일.
///         육아휴직 548일을 넣으면 MySQL 이 거부한다
///         (실측: <c>ERROR 1264 Out of range value for column 'leave_days'</c>).</item>
///   <item>휴가가 승인되면 <c>LeaveBalanceHelper</c> 가 <c>annual_leave_used</c> 를 더한다.
///         휴직을 휴가로 올리면 <b>연차 잔여가 마이너스</b>가 되어 복직 후 연차를 못 쓴다.</item>
/// </list>
/// </remarks>
public sealed class AbsenceService : IAbsenceService
{
    private const string DocType = "absence";

    /// <summary>
    /// 반려 사유가 들어가는 칸의 폭. 🔴 단계3 P0-2 재발 방지 —
    /// 그때는 <c>approval_history.comment</c>(500)가 <c>reject_reason</c>(200)보다 넓어서
    /// 201자를 넣으면 <c>ERROR 1406</c> 으로 <b>결재 트랜잭션 전체가 되감겼다</b>.
    /// DB-98 에서 이 칸을 500 으로 맞춰 뒀고, 여기서 한 번 더 자른다.
    /// </summary>
    private const int RejectReasonMaxLength = 500;

    private readonly IDbConnection _db;
    private readonly INotificationService? _notifier;

    public AbsenceService(IDbConnection db, INotificationService? notifier = null)
    {
        _db = db;
        _notifier = notifier;
    }

    // ───────────────────────────────────────────────────────────────
    // 조회
    // ───────────────────────────────────────────────────────────────

    private const string SelectSql =
        """
        SELECT
          a.absence_id         AS AbsenceId,
          a.employee_id        AS EmployeeId,
          e.emp_name           AS EmployeeName,
          d.dept_name          AS DeptName,
          a.start_date         AS StartDate,
          a.end_date           AS EndDate,
          a.actual_return_date AS ActualReturnDate,
          a.status             AS Status,
          a.reason             AS Reason,
          a.pay_amount         AS PayAmount,
          a.pay_note           AS PayNote,
          a.approval_id        AS ApprovalId,
          a.approved_by        AS ApprovedBy,
          ap.emp_name          AS ApprovedByName,
          a.approved_at        AS ApprovedAt,
          a.reject_reason      AS RejectReason,
          a.created_at         AS CreatedAt
        FROM employee_leave_of_absence a
        LEFT JOIN employees e  ON e.employee_id = a.employee_id AND e.tenant_id = a.tenant_id
        LEFT JOIN departments d ON d.dept_id = e.dept_id        AND d.tenant_id = a.tenant_id
        LEFT JOIN employees ap ON ap.employee_id = a.approved_by AND ap.tenant_id = a.tenant_id
        """;

    public async Task<List<AbsenceDto>> GetListAsync(string tenantId, string? employeeId, string? status,
        DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var rows = await _db.QueryAsync<AbsenceDto>(new CommandDefinition(
            SelectSql +
            """

            WHERE a.tenant_id = @TenantId
              AND (@EmployeeId IS NULL OR a.employee_id = @EmployeeId)
              AND (@Status IS NULL OR a.status = @Status)
              AND (@From IS NULL OR a.end_date   >= @From)
              AND (@To   IS NULL OR a.start_date <= @To)
            ORDER BY a.start_date DESC, a.created_at DESC
            """,
            new
            {
                TenantId = tenantId,
                EmployeeId = employeeId,
                Status = status,
                From = from?.Date,
                To = to?.Date
            },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    public async Task<AbsenceDto?> GetAsync(string tenantId, string absenceId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        return await _db.QueryFirstOrDefaultAsync<AbsenceDto>(new CommandDefinition(
            SelectSql +
            """

            WHERE a.tenant_id = @TenantId AND a.absence_id = @AbsenceId
            """,
            new { TenantId = tenantId, AbsenceId = absenceId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    // ───────────────────────────────────────────────────────────────
    // 저장 — 사람이 넣은 그대로
    // ───────────────────────────────────────────────────────────────

    public async Task<SaveAbsenceResult> SaveAsync(string tenantId, string actorId, string actorName,
        SaveAbsenceRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(request.EmployeeId))
            throw new InvalidOperationException("휴직 대상 사원을 선택하세요.");

        if (request.StartDate == default || request.EndDate == default)
            throw new InvalidOperationException("휴직 시작일과 종료일을 넣으세요.");

        // ⚠️ 날짜가 뒤집힌 것은 '기준 초과' 와 다르다. 회사 규정 문제가 아니라
        //    **입력이 잘못된 것**이라 막는다.
        if (request.EndDate.Date < request.StartDate.Date)
            throw new InvalidOperationException("종료일이 시작일보다 빠릅니다.");

        if (request.PayAmount < 0)
            throw new InvalidOperationException("급여 금액은 0보다 작을 수 없습니다.");

        var empExists = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM employees WHERE tenant_id=@TenantId AND employee_id=@EmpId AND is_active=1",
            new { TenantId = tenantId, EmpId = request.EmployeeId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (empExists == 0)
            throw new InvalidOperationException("휴직을 넣을 수 없는 사원입니다(미존재 또는 퇴직).");

        // 🔴 같은 사람의 기간이 겹치면 막는다.
        //    겹친 휴직은 회사가 더 주는 것이 아니라 **데이터가 모순된 것**이다 —
        //    급여를 두 번 빼거나 조직도가 어긋난다. 그래서 이건 막는다.
        var overlap = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            """
            SELECT CONCAT(DATE_FORMAT(start_date, '%Y-%m-%d'), ' ~ ',
                          DATE_FORMAT(end_date, '%Y-%m-%d'))
            FROM employee_leave_of_absence
            WHERE tenant_id = @TenantId
              AND employee_id = @EmpId
              AND status IN ('pending', 'approved', 'active')
              AND (@AbsenceId IS NULL OR absence_id <> @AbsenceId)
              AND start_date <= @End
              AND end_date   >= @Start
            LIMIT 1
            """,
            new
            {
                TenantId = tenantId,
                EmpId = request.EmployeeId,
                AbsenceId = request.AbsenceId,
                Start = request.StartDate.Date,
                End = request.EndDate.Date
            },
            cancellationToken: ct)).ConfigureAwait(false);

        if (overlap is not null)
            throw new InvalidOperationException($"이미 등록된 휴직 기간({overlap})과 겹칩니다.");

        var result = new SaveAbsenceResult();
        var isNew = string.IsNullOrWhiteSpace(request.AbsenceId);
        var absenceId = isNew ? Guid.NewGuid().ToString() : request.AbsenceId!;
        result.AbsenceId = absenceId;

        // 금액이 0 이면 무급, 크면 유급. 사람이 넣은 숫자가 답이므로 우리가 고르지 않는다.
        // (pay_type 은 기존 칸이라 남겨 두고 표기용으로만 채운다 — 헌법 #1)
        var payType = request.PayAmount > 0 ? "paid" : "unpaid";

        if (isNew)
        {
            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO employee_leave_of_absence
                  (absence_id, tenant_id, employee_id,
                   start_date, end_date, status, reason,
                   pay_type, pay_amount, pay_note, created_by)
                VALUES
                  (@AbsenceId, @TenantId, @EmployeeId,
                   @StartDate, @EndDate, 'draft', @Reason,
                   @PayType, @PayAmount, @PayNote, @CreatedBy)
                """,
                new
                {
                    AbsenceId = absenceId,
                    TenantId = tenantId,
                    EmployeeId = request.EmployeeId,
                    StartDate = request.StartDate.Date,
                    EndDate = request.EndDate.Date,
                    Reason = request.Reason,
                    PayType = payType,
                    PayAmount = request.PayAmount,
                    PayNote = request.PayNote,
                    CreatedBy = actorId
                },
                cancellationToken: ct)).ConfigureAwait(false);
        }
        else
        {
            // 결재가 끝난 건은 못 고친다 — 승인된 내용이 뒤에서 바뀌면 결재가 무의미해진다.
            var current = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
                "SELECT status FROM employee_leave_of_absence WHERE tenant_id=@TenantId AND absence_id=@AbsenceId",
                new { TenantId = tenantId, AbsenceId = absenceId },
                cancellationToken: ct)).ConfigureAwait(false);

            if (current is null)
                throw new InvalidOperationException("휴직 건을 찾을 수 없습니다.");

            if (current is "approved" or "active" or "returned")
                throw new InvalidOperationException($"이미 {AbsenceStatusLabels.Of(current)} 상태라 고칠 수 없습니다.");

            await _db.ExecuteAsync(new CommandDefinition(
                """
                UPDATE employee_leave_of_absence
                SET employee_id = @EmployeeId,
                    start_date  = @StartDate,
                    end_date    = @EndDate,
                    reason      = @Reason,
                    pay_type    = @PayType,
                    pay_amount  = @PayAmount,
                    pay_note    = @PayNote,
                    updated_at  = NOW(6)
                WHERE tenant_id = @TenantId AND absence_id = @AbsenceId
                """,
                new
                {
                    AbsenceId = absenceId,
                    TenantId = tenantId,
                    EmployeeId = request.EmployeeId,
                    StartDate = request.StartDate.Date,
                    EndDate = request.EndDate.Date,
                    Reason = request.Reason,
                    PayType = payType,
                    PayAmount = request.PayAmount,
                    PayNote = request.PayNote
                },
                cancellationToken: ct)).ConfigureAwait(false);
        }

        if (request.Submit)
        {
            var submitted = await SubmitInternalAsync(tenantId, actorId, actorName, absenceId, ct)
                .ConfigureAwait(false);
            result.ApprovalCreated = submitted.Created;
            result.ApprovalSkipReason = submitted.SkipReason;
        }

        return result;
    }

    // ───────────────────────────────────────────────────────────────
    // 결재
    // ───────────────────────────────────────────────────────────────

    public async Task<SaveAbsenceResult> SubmitAsync(string tenantId, string actorId, string actorName,
        string absenceId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var submitted = await SubmitInternalAsync(tenantId, actorId, actorName, absenceId, ct)
            .ConfigureAwait(false);

        return new SaveAbsenceResult
        {
            AbsenceId = absenceId,
            ApprovalCreated = submitted.Created,
            ApprovalSkipReason = submitted.SkipReason
        };
    }

    /// <summary>
    /// 결재를 올리고 <b>실제로 올라갔는지 확인해서</b> 사실대로 돌려준다.
    /// </summary>
    /// <remarks>
    /// 🔴 단계3 P0-1 교훈: <c>TryCreateApprovalAsync</c> 는 결재 설정이 꺼져 있거나 결재선이 없으면
    /// <b>조용히 아무것도 안 한다</b>. 그때 화면이 "결재에 올렸습니다" 를 띄우면 직원은 올라간 줄 알고
    /// 문서는 <c>pending</c> 에 갇힌다. 그래서 <b>올린 뒤 세어 본다.</b>
    /// </remarks>
    private async Task<(bool Created, string? SkipReason)> SubmitInternalAsync(
        string tenantId, string actorId, string actorName, string absenceId, CancellationToken ct)
    {
        var row = await _db.QueryFirstOrDefaultAsync<SubmitRow>(new CommandDefinition(
            """
            SELECT a.absence_id AS AbsenceId, a.status AS Status,
                   a.start_date AS StartDate, a.end_date AS EndDate,
                   a.employee_id AS EmployeeId, e.emp_name AS EmployeeName
            FROM employee_leave_of_absence a
            LEFT JOIN employees e ON e.employee_id = a.employee_id AND e.tenant_id = a.tenant_id
            WHERE a.tenant_id = @TenantId AND a.absence_id = @AbsenceId
            """,
            new { TenantId = tenantId, AbsenceId = absenceId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (row is null)
            throw new InvalidOperationException("휴직 건을 찾을 수 없습니다.");

        if (row.Status is "approved" or "active" or "returned")
            throw new InvalidOperationException($"이미 {AbsenceStatusLabels.Of(row.Status)} 상태입니다.");

        var blocker = await ApprovalTriggerHelper
            .DescribeApprovalBlockerAsync(_db, tenantId, DocType, ct).ConfigureAwait(false);

        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE employee_leave_of_absence
            SET status = 'pending', updated_at = NOW(6)
            WHERE tenant_id = @TenantId AND absence_id = @AbsenceId
            """,
            new { TenantId = tenantId, AbsenceId = absenceId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (blocker is not null)
        {
            // 결재를 못 올린다는 걸 **감추지 않는다.**
            return (false, blocker);
        }

        var title = $"{row.EmployeeName} 휴직 ({row.StartDate:yyyy-MM-dd} ~ {row.EndDate:yyyy-MM-dd})";

        await ApprovalTriggerHelper.TryCreateApprovalAsync(
            _db, DocType, absenceId, absenceId[..8], title,
            0m, tenantId, actorId, actorName, ct, _notifier).ConfigureAwait(false);

        // 🔴 올렸다고 믿지 않고 세어 본다.
        var created = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM approval_documents
            WHERE tenant_id = @TenantId AND doc_type = @DocType AND ref_id = @RefId
            """,
            new { TenantId = tenantId, DocType = DocType, RefId = absenceId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (created == 0)
        {
            return (false, "결재 문서가 만들어지지 않았습니다. 설정 → 결재설정을 확인해주세요.");
        }

        return (true, null);
    }

    /// <summary>
    /// 승인. 🔴 <b>사원 상태를 함께 바꾼다</b> — 사장님: <i>"상태처리 : 재직 휴직 연차"</i>.
    /// </summary>
    public async Task ApproveAsync(string tenantId, string actorId, string absenceId,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var row = await _db.QueryFirstOrDefaultAsync<SubmitRow>(new CommandDefinition(
            """
            SELECT absence_id AS AbsenceId, status AS Status, employee_id AS EmployeeId,
                   start_date AS StartDate, end_date AS EndDate
            FROM employee_leave_of_absence
            WHERE tenant_id = @TenantId AND absence_id = @AbsenceId
            """,
            new { TenantId = tenantId, AbsenceId = absenceId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (row is null)
            throw new InvalidOperationException("휴직 건을 찾을 수 없습니다.");

        if (row.Status is "approved" or "active" or "returned")
            throw new InvalidOperationException($"이미 {AbsenceStatusLabels.Of(row.Status)} 상태입니다.");

        // 승인 시점에 이미 시작일이 지났으면 바로 '휴직중' 으로 둔다 —
        // 결재가 늦게 나는 것은 흔한 일이고, 그때 '승인(시작 전)' 에 멈춰 있으면
        // 조직도·급여가 이 사람을 재직자로 본다.
        var started = row.StartDate.Date <= DateTime.Today;
        var nextStatus = started ? "active" : "approved";

        // 🔴 휴직 상태와 사원 상태를 ★같은 트랜잭션★ 으로 묶는다.
        //    따로 하면 휴직은 '휴직중' 인데 사원은 '재직' 으로 남아 급여가 그대로 나간다.
        using var tx = _db.BeginTransaction();
        try
        {
            await _db.ExecuteAsync(new CommandDefinition(
                """
                UPDATE employee_leave_of_absence
                SET status = @Status, approved_by = @ActorId, approved_at = NOW(6),
                    reject_reason = NULL, updated_at = NOW(6)
                WHERE tenant_id = @TenantId AND absence_id = @AbsenceId
                """,
                new { Status = nextStatus, ActorId = actorId, TenantId = tenantId, AbsenceId = absenceId },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            if (started)
            {
                await SetWorkStatusCoreAsync(tenantId, row.EmployeeId, WorkStatusLabels.Absence, tx, ct)
                    .ConfigureAwait(false);
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task RejectAsync(string tenantId, string actorId, string absenceId, string? reason,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE employee_leave_of_absence
            SET status = 'rejected', approved_by = @ActorId, approved_at = NOW(6),
                reject_reason = @Reason, updated_at = NOW(6)
            WHERE tenant_id = @TenantId AND absence_id = @AbsenceId
              AND status IN ('draft', 'pending')
            """,
            new
            {
                ActorId = actorId,
                // 🔴 단계3 P0-2 와 같은 사고를 막는다. 길다고 500 을 터뜨리지 않는다.
                Reason = TruncateRejectReason(reason),
                TenantId = tenantId,
                AbsenceId = absenceId
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task CancelAsync(string tenantId, string actorId, string absenceId,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var affected = await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE employee_leave_of_absence
            SET status = 'cancelled', updated_at = NOW(6)
            WHERE tenant_id = @TenantId AND absence_id = @AbsenceId
              AND status IN ('draft', 'pending', 'approved')
            """,
            new { TenantId = tenantId, AbsenceId = absenceId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (affected == 0)
        {
            throw new InvalidOperationException(
                "취소할 수 없습니다. 이미 휴직이 시작됐거나 복직 처리된 건입니다.");
        }
    }

    // ───────────────────────────────────────────────────────────────
    // 복직
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 복직 처리. 🔴 실제 복직일은 <b>사람이 넣는다</b>(사장님: 전부 수동).
    /// 사원 상태도 함께 '재직' 으로 되돌린다.
    /// </summary>
    public async Task ReturnAsync(string tenantId, string actorId, ReturnFromAbsenceRequest request,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        if (request.ActualReturnDate == default)
            throw new InvalidOperationException("실제 복직일을 넣으세요.");

        var row = await _db.QueryFirstOrDefaultAsync<SubmitRow>(new CommandDefinition(
            """
            SELECT absence_id AS AbsenceId, status AS Status, employee_id AS EmployeeId,
                   start_date AS StartDate, end_date AS EndDate
            FROM employee_leave_of_absence
            WHERE tenant_id = @TenantId AND absence_id = @AbsenceId
            """,
            new { TenantId = tenantId, AbsenceId = request.AbsenceId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (row is null)
            throw new InvalidOperationException("휴직 건을 찾을 수 없습니다.");

        if (row.Status is not ("approved" or "active"))
            throw new InvalidOperationException(
                $"{AbsenceStatusLabels.Of(row.Status)} 상태는 복직 처리할 수 없습니다.");

        if (request.ActualReturnDate.Date < row.StartDate.Date)
            throw new InvalidOperationException("복직일이 휴직 시작일보다 빠릅니다.");

        // ⚠️ 예정보다 이르거나 늦은 복직은 **정상이다.** 막지 않고 그대로 남긴다 —
        //    예정일과 실제일을 둘 다 가지고 있어야 급여·근속 계산이 실제에 맞는다.
        using var tx = _db.BeginTransaction();
        try
        {
            await _db.ExecuteAsync(new CommandDefinition(
                """
                UPDATE employee_leave_of_absence
                SET status = 'returned',
                    actual_return_date = @ReturnDate,
                    reason = CASE WHEN @Note IS NULL OR @Note = '' THEN reason
                                  ELSE CONCAT(COALESCE(reason, ''), ' / 복직: ', @Note) END,
                    updated_at = NOW(6)
                WHERE tenant_id = @TenantId AND absence_id = @AbsenceId
                """,
                new
                {
                    ReturnDate = request.ActualReturnDate.Date,
                    Note = request.Note,
                    TenantId = tenantId,
                    AbsenceId = request.AbsenceId
                },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            await SetWorkStatusCoreAsync(tenantId, row.EmployeeId, WorkStatusLabels.Active, tx, ct)
                .ConfigureAwait(false);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ───────────────────────────────────────────────────────────────
    // 상태 맞추기 · 조회 보조
    // ───────────────────────────────────────────────────────────────

    public async Task<int> SyncStatusAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 시작일이 된 승인 건을 '휴직중' 으로.
        // 🔴 종료일이 지났다고 자동으로 '복직' 시키지 않는다 —
        //    실제 복직일은 사람이 넣어야 하고(사장님: 전부 수동), 연장되는 경우도 흔하다.
        //    자동으로 복직시키면 아직 안 나온 사람이 재직자로 잡혀 급여가 나간다.
        using var tx = _db.BeginTransaction();
        try
        {
            var changed = await _db.ExecuteAsync(new CommandDefinition(
                """
                UPDATE employee_leave_of_absence
                SET status = 'active', updated_at = NOW(6)
                WHERE tenant_id = @TenantId
                  AND status = 'approved'
                  AND start_date <= CURDATE()
                """,
                new { TenantId = tenantId },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            // 사원 상태도 함께 맞춘다. 안 하면 휴직중인데 재직으로 보인다.
            await _db.ExecuteAsync(new CommandDefinition(
                """
                UPDATE employees e
                JOIN employee_leave_of_absence a
                  ON a.employee_id = e.employee_id AND a.tenant_id = e.tenant_id
                SET e.work_status = @Absence, e.updated_at = NOW(6)
                WHERE e.tenant_id = @TenantId
                  AND a.status = 'active'
                  AND a.start_date <= CURDATE()
                  AND COALESCE(a.actual_return_date, a.end_date) >= CURDATE()
                  AND e.work_status <> @Absence
                """,
                new { TenantId = tenantId, Absence = WorkStatusLabels.Absence },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            tx.Commit();
            return changed;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<List<string>> GetActiveEmployeeIdsAsync(string tenantId, DateTime asOf,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var rows = await _db.QueryAsync<string>(new CommandDefinition(
            """
            SELECT DISTINCT employee_id
            FROM employee_leave_of_absence
            WHERE tenant_id = @TenantId
              AND status IN ('approved', 'active')
              AND start_date <= @AsOf
              AND COALESCE(actual_return_date, end_date) >= @AsOf
            """,
            new { TenantId = tenantId, AsOf = asOf.Date },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    /// <summary>
    /// 그 달에 휴직 중이던 사람과 <b>정해 둔 급여 금액</b>을 돌려준다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>급여·회계가 휴직에서 가져갈 것은 이 금액 하나뿐이다.</b>
    /// 사장님(2026-08-13): <i>"휴직시 급여 : 텍스트 박스로 수동입력 →
    /// 그러면 자연스럽게 급여, 회계이슈도 해결될듯"</i>
    ///
    /// 그 말대로다. 사람이 금액을 정해 뒀으므로 급여가 기간·비율·요율을 <b>다시 계산할 필요가 없다.</b>
    /// 단계8(급여)에서 이 목록을 그대로 쓰면 되고, 회계는 그 급여 명세를 기표하면 된다.
    /// </remarks>
    public async Task<List<AbsencePayDto>> GetPayForMonthAsync(string tenantId, int year, int month,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var first = new DateTime(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        var rows = await _db.QueryAsync<AbsencePayDto>(new CommandDefinition(
            """
            SELECT
              a.absence_id  AS AbsenceId,
              a.employee_id AS EmployeeId,
              e.emp_name    AS EmployeeName,
              a.start_date  AS StartDate,
              a.end_date    AS EndDate,
              a.actual_return_date AS ActualReturnDate,
              a.pay_amount  AS PayAmount,
              a.pay_note    AS PayNote,
              a.reason      AS Reason,
              a.status      AS Status
            FROM employee_leave_of_absence a
            LEFT JOIN employees e ON e.employee_id = a.employee_id AND e.tenant_id = a.tenant_id
            WHERE a.tenant_id = @TenantId
              AND a.status IN ('approved', 'active', 'returned')
              AND a.start_date <= @Last
              AND COALESCE(a.actual_return_date, a.end_date) >= @First
            ORDER BY e.emp_name
            """,
            new { TenantId = tenantId, First = first, Last = last },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    /// <summary>
    /// 사원의 재직 중 상태를 바꾼다(재직·휴직·연차).
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>is_active</c> 는 <b>건드리지 않는다</b>. 그 칸은 재직/퇴사고, 여기는 재직 중 상태다.
    /// 휴직자를 <c>is_active=0</c> 으로 만들면 퇴사자와 구분이 안 되고,
    /// 기존 화면·쿼리가 전부 그 칸을 보고 있어 사원이 목록에서 통째로 사라진다.
    /// </remarks>
    public async Task SetWorkStatusAsync(string tenantId, string employeeId, string workStatus,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        await SetWorkStatusCoreAsync(tenantId, employeeId, workStatus, null, ct).ConfigureAwait(false);
    }

    private async Task SetWorkStatusCoreAsync(string tenantId, string employeeId, string workStatus,
        IDbTransaction? tx, CancellationToken ct)
    {
        if (!WorkStatusLabels.All.ContainsKey(workStatus))
        {
            throw new InvalidOperationException(
                $"알 수 없는 직원 상태입니다: {workStatus} (재직·휴직·연차 중 하나여야 합니다).");
        }

        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE employees
            SET work_status = @WorkStatus, updated_at = NOW(6)
            WHERE tenant_id = @TenantId AND employee_id = @EmployeeId
            """,
            new { WorkStatus = workStatus, TenantId = tenantId, EmployeeId = employeeId },
            transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
    }

    // ───────────────────────────────────────────────────────────────
    // 보조
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 반려 사유를 칸 폭에 맞게 자른다. 🔴 단계3 P0-2 재발 방지.
    /// </summary>
    /// <remarks>
    /// 글자 수를 <see cref="System.Globalization.StringInfo"/> 로 센다 — 한글·이모지를
    /// <c>string.Length</c> 로 세면 실제 저장 폭과 어긋난다.
    /// </remarks>
    internal static string? TruncateRejectReason(string? reason)
    {
        if (string.IsNullOrEmpty(reason)) return reason;
        var si = new System.Globalization.StringInfo(reason);
        return si.LengthInTextElements <= RejectReasonMaxLength
            ? reason
            : si.SubstringByTextElements(0, RejectReasonMaxLength);
    }

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

    private sealed class SubmitRow
    {
        public string AbsenceId { get; set; } = "";
        public string Status { get; set; } = "";
        public string EmployeeId { get; set; } = "";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string? EmployeeName { get; set; }
    }
}
