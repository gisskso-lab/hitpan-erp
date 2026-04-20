using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Employee;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>인사·근태 통합 서비스 — 출퇴근, 초과근무, HR경비</summary>
public class HrService : IHrService
{
    private readonly IDbConnection _db;
    public HrService(IDbConnection db) => _db = db;

    // ═══ 출퇴근 ═══

    public async Task<List<AttendanceDto>> GetAttendanceAsync(string tenantId, DateTime? from, DateTime? to, string? employeeId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var sql = """
            SELECT a.attendance_id AS AttendanceId, a.employee_id AS EmployeeId, e.emp_name AS EmployeeName,
                   a.work_date AS WorkDate, a.check_in AS CheckIn, a.check_out AS CheckOut,
                   a.work_hours AS WorkHours, a.status AS Status, a.memo AS Memo
            FROM attendance a
            LEFT JOIN employees e ON e.employee_id = a.employee_id
            WHERE a.tenant_id = @TenantId
            """;
        if (from.HasValue) sql += " AND a.work_date >= @From";
        if (to.HasValue) sql += " AND a.work_date <= @To";
        if (!string.IsNullOrEmpty(employeeId)) sql += " AND a.employee_id = @EmpId";
        sql += " ORDER BY a.work_date DESC, e.emp_name";

        return (await _db.QueryAsync<AttendanceDto>(new CommandDefinition(
            sql, new { TenantId = tenantId, From = from, To = to, EmpId = employeeId }, cancellationToken: ct))).ToList();
    }

    public async Task<string> CheckInAsync(string tenantId, string employeeId, CheckInOutRequest req, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var id = Guid.NewGuid().ToString();
        var now = DateTime.Now;
        // 오늘 이미 출근했는지 확인
        var existing = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            "SELECT attendance_id FROM attendance WHERE tenant_id = @TenantId AND employee_id = @EmpId AND work_date = @Today",
            new { TenantId = tenantId, EmpId = employeeId, Today = now.Date }, cancellationToken: ct));
        if (existing is not null) throw new InvalidOperationException("오늘 이미 출근 처리되었습니다.");

        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO attendance (attendance_id, tenant_id, employee_id, work_date, check_in, status, memo)
            VALUES (@Id, @TenantId, @EmpId, @Today, @Now, 'normal', @Memo)
            """,
            new { Id = id, TenantId = tenantId, EmpId = employeeId, Today = now.Date, Now = now, Memo = req.Memo }, cancellationToken: ct));
        return id;
    }

    public async Task CheckOutAsync(string tenantId, string employeeId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var now = DateTime.Now;
        var att = await _db.QueryFirstOrDefaultAsync<(string Id, DateTime CheckIn)>(new CommandDefinition(
            "SELECT attendance_id AS Id, check_in AS CheckIn FROM attendance WHERE tenant_id = @TenantId AND employee_id = @EmpId AND work_date = @Today AND check_out IS NULL",
            new { TenantId = tenantId, EmpId = employeeId, Today = now.Date }, cancellationToken: ct));
        if (string.IsNullOrEmpty(att.Id)) throw new InvalidOperationException("출근 기록이 없거나 이미 퇴근 처리되었습니다.");

        var hours = Math.Round((now - att.CheckIn).TotalHours, 1);
        await _db.ExecuteAsync(new CommandDefinition(
            "UPDATE attendance SET check_out = @Now, work_hours = @Hours WHERE attendance_id = @Id",
            new { Now = now, Hours = hours, Id = att.Id }, cancellationToken: ct));
    }

    // ═══ 초과근무 ═══

    public async Task<List<OvertimeDto>> GetOvertimeAsync(string tenantId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var sql = """
            SELECT o.overtime_id AS OvertimeId, o.employee_id AS EmployeeId, e.emp_name AS EmployeeName,
                   o.work_date AS WorkDate, o.start_time AS StartTime, o.end_time AS EndTime,
                   o.hours AS Hours, o.overtime_type AS OvertimeType, o.reason AS Reason, o.status AS Status
            FROM overtime o
            LEFT JOIN employees e ON e.employee_id = o.employee_id
            WHERE o.tenant_id = @TenantId
            """;
        if (from.HasValue) sql += " AND o.work_date >= @From";
        if (to.HasValue) sql += " AND o.work_date <= @To";
        sql += " ORDER BY o.work_date DESC";

        return (await _db.QueryAsync<OvertimeDto>(new CommandDefinition(
            sql, new { TenantId = tenantId, From = from, To = to }, cancellationToken: ct))).ToList();
    }

    public async Task<string> CreateOvertimeAsync(CreateOvertimeRequest req, string tenantId, string employeeId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var id = Guid.NewGuid().ToString();
        var hours = Math.Round((req.EndTime - req.StartTime).TotalHours, 1);
        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO overtime (overtime_id, tenant_id, employee_id, work_date, start_time, end_time, hours, overtime_type, reason, status)
            VALUES (@Id, @TenantId, @EmpId, @WorkDate, @Start, @End, @Hours, @Type, @Reason, 'pending')
            """,
            new { Id = id, TenantId = tenantId, EmpId = employeeId, req.WorkDate,
                  Start = req.StartTime, End = req.EndTime, Hours = hours, Type = req.OvertimeType, req.Reason }, cancellationToken: ct));
        return id;
    }

    // ═══ HR 경비신청 ═══

    public async Task<List<HrExpenseRequestDto>> GetHrExpensesAsync(string tenantId, string? employeeId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var sql = """
            SELECT r.request_id AS RequestId, r.employee_id AS EmployeeId, e.emp_name AS EmployeeName,
                   r.request_date AS RequestDate, r.category AS Category, r.description AS Description,
                   r.amount AS Amount, r.status AS Status
            FROM hr_expense_requests r
            LEFT JOIN employees e ON e.employee_id = r.employee_id
            WHERE r.tenant_id = @TenantId
            """;
        if (!string.IsNullOrEmpty(employeeId)) sql += " AND r.employee_id = @EmpId";
        sql += " ORDER BY r.request_date DESC";

        return (await _db.QueryAsync<HrExpenseRequestDto>(new CommandDefinition(
            sql, new { TenantId = tenantId, EmpId = employeeId }, cancellationToken: ct))).ToList();
    }

    public async Task<string> CreateHrExpenseAsync(CreateHrExpenseRequest req, string tenantId, string employeeId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var id = Guid.NewGuid().ToString();
        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO hr_expense_requests (request_id, tenant_id, employee_id, request_date, category, description, amount, status)
            VALUES (@Id, @TenantId, @EmpId, @Date, @Category, @Description, @Amount, 'pending')
            """,
            new { Id = id, TenantId = tenantId, EmpId = employeeId, Date = req.RequestDate,
                  req.Category, req.Description, req.Amount }, cancellationToken: ct));
        return id;
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open) return;
        if (_db is DbConnection c) { await c.OpenAsync(ct); return; }
        _db.Open();
    }
}
