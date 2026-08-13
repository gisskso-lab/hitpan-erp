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

    // 작(2026-08-12) 검증팀 P2-7 봉합: 퇴사 처리는 사람의 계정을 끄는 행위다.
    // 누가·언제 했는지 기록이 없으면 노무 분쟁에서 근거를 댈 수 없다.
    private readonly IAuditService _audit;

    public EmployeeService(IDbConnection db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
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
              -- 작(2026-08-13) 단계4: 주당 소정근로시간. 연차·주휴 판정이 이 숫자를 본다.
              e.weekly_hours AS WeeklyHours,
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
              position, job_title, emp_type, weekly_hours,
              join_date, phone, email,
              role, is_active,
              created_at, updated_at)
            VALUES (
              @EmployeeId, @TenantId, NULL,
              @EmpNo, @EmpName, @DeptId,
              @Position, @JobTitle, @EmpType, @WeeklyHours,
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
                -- 작(2026-08-13) 단계4: 주당 소정근로시간. null 이면 '미정' 으로 되돌아간다.
                weekly_hours = @WeeklyHours,
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
                WeeklyHours = request.WeeklyHours,
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

    /// <summary>
    /// 사원 퇴사 처리이다. 이름은 Delete 지만 물리 삭제가 아니라 <b>퇴사 처리</b>다(헌법 #1).
    /// </summary>
    /// <remarks>
    /// 작(2026-08-12) 그룹웨어 단계0 P0-A·C 봉합.
    /// <para>
    /// 봉합 전에는 <c>employees.is_active</c> 한 줄만 껐다. 그런데 로그인은
    /// <c>users.IsActive</c> 를 본다(AuthService.LoginAsync). <b>서로 다른 표의 다른 칸이라
    /// 퇴사시켜도 계정이 살아 있었다</b> — 퇴사자가 거래처·단가·재고를 계속 열람했다.
    /// </para>
    /// <para>
    /// 또한 <c>is_resigned</c>·<c>resign_reason</c> 컬럼은 존재하는데 ERP 가 한 번도 채우지
    /// 않았고(레거시 MDB 이관 경로만 사용), 퇴사일을 <c>NOW()</c> 로 넣어 <b>소급 퇴사 처리가
    /// 불가능</b>했다. 실제 퇴사일과 처리한 날은 다를 수 있다.
    /// </para>
    /// </remarks>
    public async Task DeleteAsync(string tenantId, string employeeId, CancellationToken ct = default)
        => await ResignAsync(tenantId, employeeId, resignDate: null, resignReason: null, ct).ConfigureAwait(false);

    /// <summary>
    /// 퇴사 처리 본체. 퇴사일·사유를 받는다(반자동 원칙 — 시스템이 날짜를 단정하지 않는다).
    /// </summary>
    /// <param name="resignDate">실제 퇴사일. null 이면 오늘로 본다(기존 동작 보존).</param>
    /// <param name="resignReason">퇴사 사유. null 이면 기록하지 않는다.</param>
    /// <returns>로그인 계정을 실제로 차단했으면 true. 계정이 없거나 대표계정이면 false.</returns>
    public async Task<bool> ResignAsync(
        string tenantId,
        string employeeId,
        DateTime? resignDate,
        string? resignReason,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 🔴 검증팀 P1-4 봉합: 두 UPDATE 를 한 트랜잭션으로 묶는다.
        // 앞서는 주석에 "호출부에서 감싼다"고 적어 놓고 감싸는 호출부가 없었다.
        // 사원만 퇴사 처리되고 계정이 살아남으면, 화면상 이미 퇴사자라 되돌릴 방법이 없다.
        using var tx = _db.BeginTransaction();

        try
        {
            // 사원 퇴사 처리. resign_date 는 넘어온 퇴사일을 쓰고, 없으면 오늘(기존 동작).
            const string employeeSql = """
                UPDATE employees
                SET is_active = 0,
                    is_resigned = 1,
                    resign_date = COALESCE(@ResignDate, NOW(6)),
                    resign_reason = COALESCE(@ResignReason, resign_reason),
                    updated_at = NOW(6)
                WHERE tenant_id = @TenantId
                  AND employee_id = @EmployeeId
                """;

            await _db.ExecuteAsync(new CommandDefinition(
                employeeSql,
                new
                {
                    TenantId = tenantId,
                    EmployeeId = employeeId,
                    ResignDate = resignDate,
                    ResignReason = resignReason
                },
                transaction: tx,
                cancellationToken: ct)).ConfigureAwait(false);

            // 🔴 P0-A: 로그인 계정도 함께 끈다. employees.user_id 로 연결된 계정만 대상이다.
            //
            // 🔴 검증팀 P0-1 봉합 — 대표계정 제외 조건이 틀려 있었다.
            //    처음에 `account_type <> 'tenant_owner'` 로 썼는데 이 시스템에 'tenant_owner'
            //    라는 값은 존재하지 않는다(실측: users.account_type 은 'tenant_admin' 뿐이고,
            //    부모계정 생성기 CompanyBootstrapProvisioner 도 'tenant_admin' 으로 넣는다).
            //    ⇒ 비교가 항상 참이라 가드가 아무도 막지 못했고, 대표계정을 퇴사 처리하면
            //      로그인이 잠겨 고객사 업무가 전면 정지된다(헌법 #38 — ERP 계정은 부모/자식뿐이라
            //      로컬에 복구 경로가 없다).
            //    부모 여부를 실제로 가리는 컬럼은 `is_parent` 다(UserService·프로비저너가 쓰는 그 칸).
            const string userSql = """
                UPDATE users u
                JOIN employees e
                  ON e.user_id = u.user_id
                 AND e.tenant_id = u.tenant_id
                SET u.is_active = 0,
                    u.updated_at = NOW(6)
                WHERE e.tenant_id = @TenantId
                  AND e.employee_id = @EmployeeId
                  AND e.user_id IS NOT NULL
                  AND e.user_id <> ''
                  AND u.is_parent = 0
                """;

            var accountBlocked = await _db.ExecuteAsync(new CommandDefinition(
                userSql,
                new { TenantId = tenantId, EmployeeId = employeeId },
                transaction: tx,
                cancellationToken: ct)).ConfigureAwait(false);

            // 🔴 검증팀 P2-7 봉합: 감사 기록. 트랜잭션 안에서 함께 남긴다 —
            // 퇴사는 됐는데 기록만 없는 상태가 생기면 안 된다.
            // 급여 같은 민감값은 넣지 않는다(감사로그에 평문으로 남는다).
            await _audit.LogAsync(
                actionType: "resign",
                entityType: "employee",
                entityId: employeeId,
                afterJson: System.Text.Json.JsonSerializer.Serialize(new
                {
                    resignDate,
                    accountBlocked = accountBlocked > 0
                }),
                reason: resignReason,
                tx: tx,
                ct: ct).ConfigureAwait(false);

            tx.Commit();

            // 몇 행이 꺼졌는지 돌려준다. 화면이 "계정도 차단했다"고 단정하지 않게 하기 위해서다
            // (검증팀 P1-5 — 계정 없는 사원에게도 차단했다고 안내하던 거짓 표시).
            return accountBlocked > 0;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>
    /// 퇴사 처리 전 사전 점검. 막지는 않고 <b>무슨 일이 벌어지는지 알려준다</b>(반자동 원칙).
    /// </summary>
    /// <remarks>
    /// 작(2026-08-12) P0-B. 결재선에 들어 있는 사람이 퇴사하면 그 차례에서 결재가 멈춘다
    /// (헌법 #20 워크플로우 끊김). 김삼성 상무 경고: <i>"사람 이름으로 결재선을 짜면
    /// 그 사람이 퇴사하는 날 회사의 모든 결재가 멈춥니다."</i> — 가설이 아니라 현재 구조다.
    /// 결재선 정본화(직급 기반)는 별도 차수이므로, 지금은 <b>퇴사 전에 알려주는 것</b>까지 한다.
    /// </remarks>
    public async Task<EmployeeResignPrecheckDto> GetResignPrecheckAsync(
        string tenantId,
        string employeeId,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT
              (SELECT COUNT(*)
                 FROM approval_doc_lines l
                WHERE l.tenant_id = @TenantId
                  AND l.is_active = 1
                  AND (l.approver_id = @EmployeeId OR l.delegate_id = @EmployeeId)) AS ApprovalLineCount,
              (SELECT COUNT(*)
                 FROM approval_documents d
                WHERE d.tenant_id = @TenantId
                  AND d.status = 'pending'
                  AND d.requester_id = @EmployeeId) AS PendingRequestCount,
              (SELECT CASE WHEN e.user_id IS NULL OR e.user_id = '' THEN 0 ELSE 1 END
                 FROM employees e
                WHERE e.tenant_id = @TenantId
                  AND e.employee_id = @EmployeeId) AS HasUserAccount
            """;

        var result = await _db.QueryFirstOrDefaultAsync<EmployeeResignPrecheckDto>(
            new CommandDefinition(
                sql,
                new { TenantId = tenantId, EmployeeId = employeeId },
                cancellationToken: ct)).ConfigureAwait(false);

        return result ?? new EmployeeResignPrecheckDto();
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
