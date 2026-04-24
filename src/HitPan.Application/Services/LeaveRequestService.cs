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
              lr.created_at AS CreatedAt
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

    public async Task ApproveAsync(string tenantId, ApproveLeaveRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            UPDATE leave_requests
            SET status = 'approved',
                approved_by = (
                  SELECT e.employee_id
                  FROM employees e
                  WHERE e.tenant_id = @TenantId
                    AND e.role = 'TenantAdmin'
                    AND e.is_active = 1
                  ORDER BY e.created_at
                  LIMIT 1
                ),
                approved_at = NOW(6),
                reject_reason = NULL,
                updated_at = NOW(6)
            WHERE tenant_id = @TenantId
              AND request_id = @RequestId
            """;

        await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new { TenantId = tenantId, RequestId = request.RequestId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task RejectAsync(string tenantId, ApproveLeaveRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            UPDATE leave_requests
            SET status = 'rejected',
                approved_by = (
                  SELECT e.employee_id
                  FROM employees e
                  WHERE e.tenant_id = @TenantId
                    AND e.role = 'TenantAdmin'
                    AND e.is_active = 1
                  ORDER BY e.created_at
                  LIMIT 1
                ),
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
                RequestId = request.RequestId,
                RejectReason = request.RejectReason
            },
            cancellationToken: ct)).ConfigureAwait(false);
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
