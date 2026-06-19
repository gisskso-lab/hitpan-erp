using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Employee;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>
/// leave_requests 테이블 기반 연차 신청/결재 서비스 구현체이다.
/// </summary>
public sealed class LeaveRequestService : ILeaveRequestService
{
    private readonly IDbConnection _db;

    public LeaveRequestService(IDbConnection db)
    {
        _db = db;
    }

    public async Task<List<LeaveRequestListDto>> GetListAsync(string tenantId, string? employeeId = null, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT
              lr.request_id AS RequestId,
              e.emp_name AS EmployeeName,
              lr.leave_type AS LeaveType,
              lr.leave_days AS LeaveDays,
              lr.start_date AS StartDate,
              lr.end_date AS EndDate,
              lr.status AS Status,
              lr.created_at AS CreatedAt,
              lr.reject_reason AS RejectReason
            FROM leave_requests lr
            INNER JOIN employees e
              ON e.employee_id = lr.employee_id
             AND e.tenant_id = lr.tenant_id
            WHERE lr.tenant_id = @TenantId
              AND (@EmployeeId IS NULL OR lr.employee_id = @EmployeeId)
            ORDER BY lr.created_at DESC
            """;

        var rows = await _db.QueryAsync<LeaveRequestListDto>(new CommandDefinition(
            sql,
            new { TenantId = tenantId, EmployeeId = employeeId },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    public async Task<string> CreateAsync(string tenantId, CreateLeaveRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var requestId = Guid.NewGuid().ToString();

        const string sql = """
            INSERT INTO leave_requests (
              request_id, tenant_id, employee_id,
              leave_type, leave_days,
              start_date, end_date,
              reason, status,
              created_at, updated_at)
            VALUES (
              @RequestId, @TenantId, @EmployeeId,
              @LeaveType, @LeaveDays,
              @StartDate, @EndDate,
              @Reason, 'pending',
              NOW(6), NOW(6))
            """;

        await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                RequestId = requestId,
                TenantId = tenantId,
                EmployeeId = request.EmployeeId,
                LeaveType = string.IsNullOrWhiteSpace(request.LeaveType) ? "annual" : request.LeaveType,
                LeaveDays = request.LeaveDays <= 0 ? 1.0m : request.LeaveDays,
                StartDate = request.StartDate.Date,
                EndDate = request.EndDate.Date,
                Reason = request.Reason
            },
            cancellationToken: ct)).ConfigureAwait(false);

        // 결재 트리거 — 연차도 결재 라인이 설정돼 있으면 approval_documents 자동 생성(발신함 노출).
        // 금액은 연차일수 기반(레거시 금액 기반 결재 로직 재활용).
        var empName = await _db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT emp_name FROM employees WHERE tenant_id=@TenantId AND employee_id=@EmpId",
            new { TenantId = tenantId, EmpId = request.EmployeeId },
            cancellationToken: ct)).ConfigureAwait(false) ?? "직원";

        var title = $"연차 신청: {empName} {request.StartDate:yyyy-MM-dd}~{request.EndDate:yyyy-MM-dd} ({request.LeaveDays}일)";
        await ApprovalTriggerHelper.TryCreateApprovalAsync(
            _db, docType: "leave", refId: requestId, refNo: requestId[..8],
            title: title, amount: request.LeaveDays,
            tenantId: tenantId, requesterId: request.EmployeeId, requesterName: empName,
            ct: ct).ConfigureAwait(false);

        return requestId;
    }

    public async Task ApproveAsync(string tenantId, string approverId, ApproveLeaveRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // P1-3 봉합(2026-06-20): 실제 처리한 결재자(@ApproverId)를 기록한다.
        // 종전 "첫 TenantAdmin 서브쿼리"는 관리자 여럿일 때 실제 승인자 추적이 불가했다.
        const string sql = """
            UPDATE leave_requests
            SET status = 'approved',
                approved_by = @ApproverId,
                approved_at = NOW(6),
                reject_reason = NULL,
                updated_at = NOW(6)
            WHERE tenant_id = @TenantId
              AND request_id = @RequestId
            """;

        await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new { TenantId = tenantId, ApproverId = approverId, RequestId = request.RequestId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task RejectAsync(string tenantId, string approverId, ApproveLeaveRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // P1-3 봉합(2026-06-20): 실제 처리한 결재자(@ApproverId)를 기록한다.
        const string sql = """
            UPDATE leave_requests
            SET status = 'rejected',
                approved_by = @ApproverId,
                approved_at = NOW(6),
                reject_reason = @RejectReason,
                updated_at = NOW(6)
            WHERE tenant_id = @TenantId
              AND request_id = @RequestId
            """;

        await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                TenantId = tenantId,
                ApproverId = approverId,
                RequestId = request.RequestId,
                RejectReason = request.RejectReason
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>
    /// 작20260429 (사장님 결재): 월간 직원 연차 캘린더 조회.
    /// 활성 사원 + 해당 월에 걸치는 휴가 (status approved/pending) 매트릭스로 반환.
    /// 한 사원이 여러 일에 걸친 휴가는 일자별로 셀 펼쳐서 반환.
    /// </summary>
    public async Task<LeaveCalendarDto> GetCalendarAsync(string tenantId, int year, int month, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var monthStart = new DateTime(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        // 1) 활성 사원 + 연차 부여/사용
        const string empSql = """
            SELECT
              employee_id        AS EmployeeId,
              emp_name           AS EmpName,
              position           AS Position,
              annual_leave_total AS AnnualLeaveTotal,
              annual_leave_used  AS AnnualLeaveUsed
            FROM employees
            WHERE tenant_id = @TenantId AND is_active = 1
            ORDER BY emp_no
            """;
        var emps = (await _db.QueryAsync<LeaveCalendarRow>(new CommandDefinition(
            empSql, new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false)).ToList();

        // 2) 이번달과 겹치는 휴가 (start <= monthEnd AND end >= monthStart)
        const string leaveSql = """
            SELECT employee_id, leave_type, leave_days, start_date, end_date, status
            FROM leave_requests
            WHERE tenant_id = @TenantId
              AND status IN ('approved','pending')
              AND start_date <= @MonthEnd
              AND end_date   >= @MonthStart
            """;
        var leaves = (await _db.QueryAsync<(
            string EmployeeId, string LeaveType, decimal LeaveDays, DateTime StartDate, DateTime EndDate, string Status)>(
            new CommandDefinition(leaveSql,
                new { TenantId = tenantId, MonthStart = monthStart, MonthEnd = monthEnd },
                cancellationToken: ct)).ConfigureAwait(false)).ToList();

        // 3) 사원별 셀 분포 채우기 — 일자별로 펼침
        var byEmp = leaves.GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var row in emps)
        {
            if (!byEmp.TryGetValue(row.EmployeeId, out var list)) continue;
            foreach (var lv in list)
            {
                var dStart = lv.StartDate < monthStart ? monthStart : lv.StartDate;
                var dEnd   = lv.EndDate   > monthEnd   ? monthEnd   : lv.EndDate;
                for (var d = dStart; d <= dEnd; d = d.AddDays(1))
                {
                    row.Cells.Add(new LeaveCalendarCell
                    {
                        Date = d,
                        LeaveType = lv.LeaveType,
                        LeaveDays = lv.LeaveDays,
                        Status = lv.Status
                    });
                }
            }
        }

        // 4) 통계
        var totalCount = emps.Sum(r => r.Cells.Count);
        var top = emps.OrderByDescending(r => r.Cells.Count).FirstOrDefault();

        return new LeaveCalendarDto
        {
            Year = year,
            Month = month,
            DayCount = DateTime.DaysInMonth(year, month),
            Rows = emps,
            TotalLeaveCount = totalCount,
            TopUserName = top?.Cells.Count > 0 ? top.EmpName : null,
            TopUserDays = top?.Cells.Count > 0 ? top.Cells.Count : 0m
        };
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open)
        {
            return;
        }

        if (_db is DbConnection dbConnection)
        {
            await dbConnection.OpenAsync(ct).ConfigureAwait(false);
            return;
        }

        _db.Open();
    }
}
