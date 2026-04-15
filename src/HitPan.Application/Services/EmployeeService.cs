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
