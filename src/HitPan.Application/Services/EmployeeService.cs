using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Employee;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>
/// employees/departments 테이블 기반 사원관리 서비스 구현체이다.
/// </summary>
public sealed class EmployeeService : IEmployeeService
{
    private readonly IDbConnection _db;

    public EmployeeService(IDbConnection db)
    {
        _db = db;
    }

    public async Task<List<EmployeeListDto>> GetListAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT
              e.employee_id AS EmployeeId,
              e.emp_no AS EmpNo,
              e.emp_name AS EmpName,
              d.dept_name AS DeptName,
              e.position AS Position,
              e.job_title AS JobTitle,
              e.phone AS Phone,
              e.email AS Email,
              e.role AS Role,
              e.is_active AS IsActive,
              e.annual_leave_total AS AnnualLeaveTotal,
              e.annual_leave_used  AS AnnualLeaveUsed,
              CASE WHEN e.user_id IS NULL OR e.user_id = '' THEN 0 ELSE 1 END AS HasUserAccount
            FROM employees e
            LEFT JOIN departments d
              ON d.dept_id = e.dept_id
             AND d.tenant_id = e.tenant_id
            WHERE e.tenant_id = @TenantId
            ORDER BY e.emp_no
            """;

        var rows = await _db.QueryAsync<EmployeeListDto>(new CommandDefinition(
            sql,
            new { TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    /// <summary>
    /// 봉합 (2026-06-22, 10차 P1-1): 부서 드롭다운용 목록 (departments 마스터, 읽기 전용).
    /// 사원의 부서는 dept_id 로 저장하므로 화면 선택지를 (dept_id, dept_name)로 내려준다.
    /// 활성 부서만, sort_order → dept_name 순으로 정렬한다.
    /// </summary>
    public async Task<List<DepartmentDto>> GetDepartmentsAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT
              dept_id   AS DeptId,
              dept_name AS DeptName
            FROM departments
            WHERE tenant_id = @TenantId
              AND is_active = 1
            ORDER BY sort_order, dept_name
            """;

        var rows = await _db.QueryAsync<DepartmentDto>(new CommandDefinition(
            sql,
            new { TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    public async Task<EmployeeDetailDto?> GetAsync(string tenantId, string employeeId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT
              e.employee_id AS EmployeeId,
              e.tenant_id AS TenantId,
              e.user_id AS UserId,
              e.emp_no AS EmpNo,
              e.emp_name AS EmpName,
              e.dept_id AS DeptId,
              d.dept_name AS DeptName,
              e.position AS Position,
              e.job_title AS JobTitle,
              e.emp_type AS EmpType,
              e.join_date AS JoinDate,
              e.resign_date AS ResignDate,
              e.birth_date AS BirthDate,
              e.phone AS Phone,
              e.email AS Email,
              e.role AS Role,
              e.is_active AS IsActive,
              e.annual_leave_total AS AnnualLeaveTotal,
              e.annual_leave_used  AS AnnualLeaveUsed,
              e.created_by AS CreatedBy,
              e.updated_by AS UpdatedBy,
              e.created_at AS CreatedAt,
              e.updated_at AS UpdatedAt
            FROM employees e
            LEFT JOIN departments d
              ON d.dept_id = e.dept_id
             AND d.tenant_id = e.tenant_id
            WHERE e.tenant_id = @TenantId
              AND e.employee_id = @EmployeeId
            """;

        return await _db.QueryFirstOrDefaultAsync<EmployeeDetailDto>(new CommandDefinition(
            sql,
            new { TenantId = tenantId, EmployeeId = employeeId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<string> CreateAsync(string tenantId, CreateEmployeeRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 사번은 EMP-001 형식으로 테넌트별 최대값 + 1 규칙으로 자동 채번한다.
        var maxEmpNo = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            """
            SELECT MAX(emp_no)
            FROM employees
            WHERE tenant_id = @TenantId
            """,
            new { TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);

        var next = ParseNextEmpNo(maxEmpNo);
        var empNo = $"EMP-{next:000}";
        var employeeId = Guid.NewGuid().ToString();

        const string sql = """
            INSERT INTO employees (
              employee_id, tenant_id, user_id,
              emp_no, emp_name, dept_id,
              position, job_title, emp_type,
              join_date, phone, email,
              role, is_active,
              created_at, updated_at)
            VALUES (
              @EmployeeId, @TenantId, NULL,
              @EmpNo, @EmpName, @DeptId,
              @Position, @JobTitle, @EmpType,
              @JoinDate, @Phone, @Email,
              @Role, 1,
              NOW(6), NOW(6))
            """;

        await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                EmployeeId = employeeId,
                TenantId = tenantId,
                EmpNo = empNo,
                EmpName = request.EmpName,
                DeptId = request.DeptId,
                Position = request.Position,
                JobTitle = request.JobTitle,
                EmpType = request.EmpType,
                JoinDate = request.JoinDate == default ? DateTime.Today : request.JoinDate.Date,
                Phone = request.Phone,
                Email = request.Email,
                Role = string.IsNullOrWhiteSpace(request.Role) ? "sales_user" : request.Role
            },
            cancellationToken: ct)).ConfigureAwait(false);

        return employeeId;
    }

    public async Task UpdateAsync(string tenantId, string employeeId, UpdateEmployeeRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            UPDATE employees
            SET emp_name = @EmpName,
                dept_id = @DeptId,
                position = @Position,
                job_title = @JobTitle,
                emp_type = @EmpType,
                join_date = @JoinDate,
                phone = @Phone,
                email = @Email,
                role = @Role,
                updated_at = NOW(6)
            WHERE tenant_id = @TenantId
              AND employee_id = @EmployeeId
            """;

        await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                TenantId = tenantId,
                EmployeeId = employeeId,
                EmpName = request.EmpName,
                DeptId = request.DeptId,
                Position = request.Position,
                JobTitle = request.JobTitle,
                EmpType = request.EmpType,
                JoinDate = request.JoinDate == default ? DateTime.Today : request.JoinDate.Date,
                Phone = request.Phone,
                Email = request.Email,
                Role = string.IsNullOrWhiteSpace(request.Role) ? "sales_user" : request.Role
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>
    /// 작20260429 (사장님 결재): 연차 부여·사용 일수만 단독 저장 (사원관리 그리드용).
    /// 다른 사원 정보는 건드리지 않는다 — 권한 분리 + 워크플로우 영향 0건.
    /// 잔여는 (Total - Used) 계산값. 음수 입력 차단(0 이하면 0으로 보정).
    /// </summary>
    public async Task UpdateAnnualLeaveAsync(string tenantId, string employeeId,
        decimal annualLeaveTotal, decimal annualLeaveUsed, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 음수 차단 — 사용자 실수 방어 (원장 무결성 정신과 동일)
        if (annualLeaveTotal < 0m) annualLeaveTotal = 0m;
        if (annualLeaveUsed  < 0m) annualLeaveUsed  = 0m;

        const string sql = """
            UPDATE employees
            SET annual_leave_total = @Total,
                annual_leave_used  = @Used,
                updated_at         = NOW(6)
            WHERE tenant_id  = @TenantId
              AND employee_id = @EmployeeId
            """;

        await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                TenantId = tenantId,
                EmployeeId = employeeId,
                Total = annualLeaveTotal,
                Used = annualLeaveUsed
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string tenantId, string employeeId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            UPDATE employees
            SET is_active = 0,
                resign_date = NOW(6),
                updated_at = NOW(6)
            WHERE tenant_id = @TenantId
              AND employee_id = @EmployeeId
            """;

        await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new { TenantId = tenantId, EmployeeId = employeeId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    private static int ParseNextEmpNo(string? maxEmpNo)
    {
        if (string.IsNullOrWhiteSpace(maxEmpNo))
        {
            return 1;
        }

        var numeric = new string(maxEmpNo.Where(char.IsDigit).ToArray());
        return int.TryParse(numeric, out var n) ? n + 1 : 1;
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
