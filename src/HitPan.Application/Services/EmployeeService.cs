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

    /// <summary>
    /// 사원 목록을 조회한다. <b>기본은 재직자만</b>이다.
    /// </summary>
    /// <param name="includeResigned">
    /// 퇴사자까지 함께 볼지 여부. 화면의 <b>"퇴사자 포함"</b> 스위치가 이 값을 정한다.
    /// </param>
    /// <remarks>
    /// 🔴 작(2026-08-14) — 사장님 지시: <i>"사원관리 메뉴에서 퇴사직원 숨김처리 될 수 있도록"</i>
    /// <para>
    /// ■ 종전 (실측): <c>WHERE e.tenant_id = @TenantId</c> 로 끝이었다. 상태를 <b>전혀 안 걸렀다.</b>
    ///   퇴사 처리는 <c>is_active=0</c> 으로 정확히 기록하는데 <b>읽는 쪽이 안 거르니</b>
    ///   목록에 그대로 남았다 — 사장님이 <i>"퇴사처리해도 사원관리에 남아있음"</i> 이라 하신 자리다.
    ///   같은 표를 읽는 급여·결재·연차·휴직은 전부 <c>is_active=1</c> 을 걸고 있었다.
    ///   <b>사원관리 하나만 빠져 있었다.</b>
    /// </para>
    /// <para>
    /// ■ 왜 지우지 않고 감추나 — 퇴사자를 <b>아예 못 보게 하면 안 된다.</b>
    ///   퇴사자 경력증명서를 떼거나, 지난 급여를 확인하거나, 잘못 누른 퇴사를 되돌릴 일이 있다.
    ///   그래서 <b>기본은 감추고, 보고 싶을 때 켠다</b>(반자동 원칙 — 자동으로 정해 주되 사람이 바꾼다).
    /// </para>
    /// <para>
    /// 🔴 <c>is_active</c> 로 거른다 — <c>is_resigned</c> 가 아니다. 실측하니 두 칸이 같은 사실을
    ///   적고 있는데(퇴사 시 둘 다 바뀐다), <c>is_resigned</c> 는 <b>읽는 코드가 전 코드에 0건</b>인
    ///   레거시 이관용 쓰기 전용 칸이다. 다른 화면들이 모두 쓰는 칸에 맞춘다.
    ///   대신 <c>IsResigned</c> 를 함께 내려 준다 — 화면이 <b>휴직·비활성과 퇴사를 갈라</b> 보여줘야
    ///   하기 때문이다(종전엔 둘 다 "비활성" 한 마디로 뭉개졌다).
    /// </para>
    /// </remarks>
    public async Task<List<EmployeeListDto>> GetListAsync(
        string tenantId,
        bool includeResigned = false,
        CancellationToken ct = default)
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
              e.is_resigned AS IsResigned,
              e.resign_date AS ResignDate,
              e.work_status AS WorkStatus,
              e.annual_leave_total AS AnnualLeaveTotal,
              e.annual_leave_used  AS AnnualLeaveUsed,
              CASE WHEN e.user_id IS NULL OR e.user_id = '' THEN 0 ELSE 1 END AS HasUserAccount
            FROM employees e
            LEFT JOIN departments d
              ON d.dept_id = e.dept_id
             AND d.tenant_id = e.tenant_id
            WHERE e.tenant_id = @TenantId
              AND (@IncludeResigned = 1 OR e.is_active = 1)
            ORDER BY e.is_active DESC, e.emp_no
            """;

        var rows = await _db.QueryAsync<EmployeeListDto>(new CommandDefinition(
            sql,
            new { TenantId = tenantId, IncludeResigned = includeResigned ? 1 : 0 },
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

    /// <summary>
    /// 부서를 정한다. 고른 <c>dept_id</c> 가 있으면 그것을, 이름만 왔으면
    /// <b>같은 이름을 찾고 없으면 만든다.</b> 작(2026-08-13) — 사장님 지시:
    /// <i>"부서를 설정하면 자동으로 그 부서로 묶으면 되는거니"</i>.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>표는 그대로다.</b> <c>departments</c> 를 없앤 게 아니라 채우는 방법을 늘렸다 —
    /// 메신저 부서방이 <c>dept_id</c> 로 묶이므로 표가 죽으면 부서방이 죽는다(사장님 지시 5).
    ///
    /// 🔴 <b>대소문자·앞뒤 공백만 다른 이름은 같은 부서로 본다.</b> 안 그러면 "영업부" 와
    /// "영업부 " 가 각각 생겨, 사원 화면 드롭다운에 같아 보이는 부서가 둘 뜬다.
    /// <see cref="DepartmentService"/> 가 이름 중복을 막는 것과 같은 판단 기준이다.
    ///
    /// ⚠️ 우리가 조직도를 지어내지 않는다(헌법 #11). <b>고객이 친 이름 그대로</b> 만들 뿐,
    /// 계층·정렬·코드를 추측하지 않는다 — 상위부서 없음, 정렬 맨 뒤, 코드 없음.
    /// </remarks>
    private async Task<string?> ResolveDeptIdAsync(string tenantId, string? deptId, string? deptName,
        CancellationToken ct)
    {
        // 목록에서 고른 것이 있으면 그쪽이 이긴다.
        if (!string.IsNullOrWhiteSpace(deptId))
        {
            return deptId;
        }

        var name = (deptName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return null; // 부서 없음도 정상이다(신입·미배정).
        }

        var existing = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            """
            SELECT dept_id
            FROM departments
            WHERE tenant_id = @TenantId
              AND LOWER(TRIM(dept_name)) = LOWER(@DeptName)
            ORDER BY is_active DESC
            LIMIT 1
            """,
            new { TenantId = tenantId, DeptName = name },
            cancellationToken: ct)).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(existing))
        {
            return existing;
        }

        var newId = Guid.NewGuid().ToString();

        // ⚠️ sort_order·is_active·created_at·updated_at 은 NOT NULL 인데 기본값이 없다(헌법 #13).
        //    DepartmentService.CreateAsync 와 같은 컬럼 구성으로 넣는다.
        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO departments
              (dept_id, tenant_id, parent_dept_id, dept_name, dept_code,
               sort_order, is_active, created_at, updated_at)
            VALUES
              (@DeptId, @TenantId, NULL, @DeptName, NULL,
               @SortOrder, 1, NOW(6), NOW(6))
            """,
            new
            {
                DeptId = newId,
                TenantId = tenantId,
                DeptName = name,
                // 맨 뒤에 붙인다. 순서는 고객이 부서 관리에서 정할 몫이다.
                SortOrder = 999
            },
            cancellationToken: ct)).ConfigureAwait(false);

        return newId;
    }

    /// <summary>
    /// 직급 이름이 마스터에 없으면 <b>만들어 둔다.</b> 작(2026-08-13) — 사장님 지시:
    /// <i>"사원관리에서 직급을 설정하면 자동으로 직급이 생기고"</i>.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>부서와 다르다.</b> <c>employees.position</c> 은 이름 문자열을 담는 컬럼이라
    /// (FK 가 아니다) 사원 저장 자체는 마스터가 없어도 된다. 그래도 여기서 마스터에 넣는 이유는
    /// <b>결재선이 직급으로 짜이기</b> 때문이다 — 마스터에 없으면 그 직급으로 결재선을 못 만든다.
    ///
    /// ⚠️ <c>positions.code</c> 는 <c>UNIQUE(tenant_id, code)</c> 이고 영문 대문자를 쓰는 자리다
    /// (CEO·MANAGER…). 한글 이름에서 영문 코드를 <b>지어내지 않는다</b> — "과장"을 MANAGER 로
    /// 옮기는 판단은 우리가 할 일이 아니고(헌법 #11), 억지로 만들면 뜻이 어긋난 코드가 남는다.
    /// 대신 충돌하지 않는 내부 코드를 붙인다. 코드는 <b>고객에게 보이지 않고</b> 화면은 이름으로 돈다.
    ///
    /// 실패해도 사원 저장은 막지 않는다 — 직급 마스터 등재는 곁다리고,
    /// 이것 때문에 사원 등록이 통째로 실패하면 손해가 더 크다.
    /// </remarks>
    private async Task EnsurePositionExistsAsync(string tenantId, string? position, CancellationToken ct)
    {
        var name = (position ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return;
        }

        // 🔴 쓰레기 값은 마스터에 올리지 않는다. 종전 자유 텍스트 시절에 실제로
        //    들어와 있던 값들이다 — 12명 중 8명이 직급 없음이었고 그중 "0" 이 1건이었다.
        //    자동 생성을 열어 준다고 그때 쓰레기까지 정식 직급으로 등재하면
        //    직급 관리 목록에 "0" 이 직급으로 뜬다.
        if (!IsMeaningfulPositionName(name))
        {
            return;
        }

        var exists = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            """
            SELECT position_id
            FROM positions
            WHERE tenant_id = @TenantId
              AND LOWER(TRIM(name)) = LOWER(@Name)
            LIMIT 1
            """,
            new { TenantId = tenantId, Name = name },
            cancellationToken: ct)).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(exists))
        {
            return;
        }

        // 사람이 읽을 일이 없는 내부 코드. UNIQUE(tenant_id, code) 를 피하는 것이 목적이다.
        var code = $"CUSTOM_{Guid.NewGuid():N}"[..24].ToUpperInvariant();

        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO positions
              (position_id, tenant_id, code, name, sort_order, is_active)
            SELECT @PositionId, @TenantId, @Code, @Name, 0, 1
            WHERE NOT EXISTS (
                SELECT 1 FROM positions
                WHERE tenant_id = @TenantId AND LOWER(TRIM(name)) = LOWER(@Name)
            )
            """,
            new
            {
                PositionId = Guid.NewGuid().ToString(),
                TenantId = tenantId,
                Code = code,
                Name = name
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>
    /// 직급 이름으로 쓸 만한 값인가. 아니면 마스터에 올리지 않는다.
    /// </summary>
    /// <remarks>
    /// 자유 텍스트 시절 실측으로 들어와 있던 값: <c>NULL</c> 2건 · 공백 5건 · <c>"0"</c> 1건.
    /// 자동 생성이 열렸다고 이런 값까지 정식 직급으로 등재하면 직급 관리 목록이 더러워진다.
    /// ⚠️ 이름을 <b>판정</b>하는 게 아니라 <b>명백한 쓰레기만</b> 거른다 —
    /// 회사마다 직급 이름이 다르므로 우리가 옳은 이름을 정하지 않는다(헌법 #11).
    /// </remarks>
    private static bool IsMeaningfulPositionName(string name)
    {
        // 숫자만 있는 값("0", "1")은 직급이 아니다.
        if (name.All(char.IsDigit))
        {
            return false;
        }

        // 문자·숫자가 하나도 없는 값("-", "...")도 아니다.
        return name.Any(char.IsLetterOrDigit);
    }

    public async Task<string> CreateAsync(string tenantId, CreateEmployeeRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var deptId = await ResolveDeptIdAsync(tenantId, request.DeptId, request.DeptName, ct)
            .ConfigureAwait(false);
        await EnsurePositionExistsAsync(tenantId, request.Position, ct).ConfigureAwait(false);

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
                // 위 ResolveDeptIdAsync 가 정한 값 — 고른 것이거나, 이름으로 찾았거나, 새로 만든 것.
                DeptId = deptId,
                Position = request.Position,
                JobTitle = request.JobTitle,
                EmpType = request.EmpType,
                // 🔴 봉합 (2026-08-13, 단계4 검증 P0-1): 여기가 빠져 있었다.
                //    SQL 에는 @WeeklyHours 가 있는데 파라미터 객체에 없어서
                //    신규 등록 시 입력값이 조용히 NULL 로 들어갔다(수정은 정상이라 더 안 보였다).
                //    ⚠️ 예외조차 안 났다 — 연결문자열의 AllowUserVariables=true 때문에
                //    MySqlConnector 가 바인딩 안 된 @WeeklyHours 를 사용자변수(NULL)로 읽었다.
                //    그 옵션이 없었다면 첫 등록에서 바로 터져 발견됐을 것이다.
                WeeklyHours = NormalizeWeeklyHours(request.WeeklyHours),
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

        // 수정에서도 새 부서명을 칠 수 있어야 한다. 등록에서만 되면
        // "등록할 땐 됐는데 고칠 땐 안 되네" 가 된다.
        // 수정에서도 새 부서명을 칠 수 있어야 한다. 등록에서만 되면
        // "등록할 땐 됐는데 고칠 땐 안 되네" 가 된다.
        var deptId = await ResolveDeptIdAsync(tenantId, request.DeptId, request.DeptName, ct)
            .ConfigureAwait(false);
        await EnsurePositionExistsAsync(tenantId, request.Position, ct).ConfigureAwait(false);

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
                DeptId = deptId,
                Position = request.Position,
                JobTitle = request.JobTitle,
                EmpType = request.EmpType,
                WeeklyHours = NormalizeWeeklyHours(request.WeeklyHours),
                JoinDate = request.JoinDate == default ? DateTime.Today : request.JoinDate.Date,
                Phone = request.Phone,
                Email = request.Email,
                Role = string.IsNullOrWhiteSpace(request.Role) ? "sales_user" : request.Role
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>
    /// 주당 소정근로시간을 현실 범위로 다듬는다. 벗어나면 <c>null</c>(미정).
    /// </summary>
    /// <remarks>
    /// 🔴 봉합 (2026-08-13, 단계4 검증 P1-1): 화면에 <c>Min=0 Max=168</c> 이 있으나
    /// 그건 브라우저에서만 도는 것이라 API 를 직접 부르면 음수도 들어갔다
    /// (실측: -5 저장됨). 음수 근로시간으로 연차를 계산하면 결과를 믿을 수 없다.
    ///
    /// ⚠️ 같은 작업 안에서 <c>RegularEmployeeCount</c> 는 서버에서 막고 있었다
    /// (<c>SettingsService</c>) — <b>기준이 갈렸다</b>. 여기서 맞춘다.
    ///
    /// 한 주는 168시간(24×7)이 최대다. 그 이상은 입력 사고다.
    /// 값을 잘라내지 않고 <c>null</c>(미정)로 돌린다 — 잘못된 숫자를 그럴듯하게
    /// 고쳐 넣으면 사람이 틀린 줄 모른다(반자동 원칙).
    /// </remarks>
    private static decimal? NormalizeWeeklyHours(decimal? weeklyHours)
        => weeklyHours is >= 0m and <= 168m ? weeklyHours : null;

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
            // 🔴 작(2026-08-14) — 사장님 지시:
            //    *"퇴사자는 직원계정관리에서 계정이 자동삭제 되도록!!!!"*
            //    *"퇴사직원이 사용했던, 히트판계정은 사용 가능하도록 설계할것"*
            //
            //  ■ 종전: is_active=0 (끄기만) ⇒ 계정이 직원계정관리에 **그대로 남았다.**
            //         게다가 uq_tenant_email(tenant_id, email) 이 그 자리를 계속 붙들어
            //         **새 직원이 같은 아이디를 쓸 수 없었다** — 사장님이 말씀하신
            //         "계정은 사용 가능하도록" 이 바로 이 자리다.
            //
            //  ■ 왜 진짜 DELETE 를 안 하나 (실측 근거)
            //     users 를 가리키는 외래키가 3개 있다 —
            //       ai_conversations · esign_records · tenant_devices.
            //     그냥 지우면 외래키에 막혀 퇴사 자체가 실패하거나,
            //     **전자근로계약서 서명 기록(esign_records)이 함께 날아간다.**
            //     서명 기록은 퇴사 후에도 법적으로 보관해야 하는 증거다(법무팀장 소관).
            //     ⇒ 화면에서는 사라지고(=사장님이 말씀하신 자동삭제),
            //       법적 기록은 남는 **소프트 삭제**로 간다. UserService 가 이미 쓰는 방식이다
            //       (is_deleted=1 이면 직원계정관리 목록·로그인 조회에서 전부 빠진다).
            //
            //  ■ 아이디 자리를 어떻게 비우나
            //     email 을 그대로 두면 UNIQUE 가 자리를 붙든다. 그래서 지운 계정의 email 에
            //     표식을 붙여 옮긴다 — 원래 주소가 비어 새 직원이 이어받을 수 있다.
            //     원본은 표식 뒤에 그대로 남으므로 "누구 계정이었나" 를 나중에도 읽을 수 있다.
            //     ⚠️ email 은 varchar(100) 이다. 표식을 앞에 붙이면 긴 주소가 잘려
            //       서로 다른 계정이 같은 값이 될 수 있다 ⇒ 잘릴 자리를 미리 확보한다.
            //
            //  🔴 부모계정은 여기서도 제외한다(u.is_parent = 0). 대표 계정을 지우면
            //     고객사가 로그인 자체를 못 한다 — 로컬에 복구 경로가 없다(헌법 #38·#40).
            const string userSql = """
                UPDATE users u
                JOIN employees e
                  ON e.user_id = u.user_id
                 AND e.tenant_id = u.tenant_id
                SET u.is_active = 0,
                    u.is_deleted = 1,
                    u.deleted_at = NOW(6),
                    u.email = CONCAT('resigned+', u.user_id, '+', LEFT(u.email, 40)),
                    u.updated_at = NOW(6)
                WHERE e.tenant_id = @TenantId
                  AND e.employee_id = @EmployeeId
                  AND e.user_id IS NOT NULL
                  AND e.user_id <> ''
                  AND u.is_parent = 0
                  AND u.is_deleted = 0
                """;

            var accountBlocked = await _db.ExecuteAsync(new CommandDefinition(
                userSql,
                new { TenantId = tenantId, EmployeeId = employeeId },
                transaction: tx,
                cancellationToken: ct)).ConfigureAwait(false);

            // 🔴 사원과 계정의 연결을 끊는다. 작(2026-08-14).
            //
            //   계정을 지웠는데 employees.user_id 가 그대로 남아 있으면
            //   **"계정 있는 사원"** 으로 계속 취급된다 — 실측하니 메신저 상대 목록이
            //   딱 그 조건(user_id 유무)만 보고 있어, 퇴사자가 대화 상대에 계속 떴다
            //   (ChatService.GetEmployeesAsync). 계정을 지운 것과 앞뒤가 맞지 않는다.
            //
            //   ⚠️ 연결만 끊는다. 사원 행 자체는 지우지 않는다 — 과거 전표·결재의
            //     담당자가 사라지면 지난 장부를 못 읽는다(헌법 #1·#3).
            if (accountBlocked > 0)
            {
                await _db.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE employees
                    SET user_id = NULL,
                        updated_at = NOW(6)
                    WHERE tenant_id = @TenantId
                      AND employee_id = @EmployeeId
                    """,
                    new { TenantId = tenantId, EmployeeId = employeeId },
                    transaction: tx,
                    cancellationToken: ct)).ConfigureAwait(false);
            }

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
