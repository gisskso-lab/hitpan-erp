using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Department;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>
/// departments 표 기반 부서 마스터 서비스. 작(2026-08-13) 그룹웨어 단계4 토대.
/// </summary>
/// <remarks>
/// 🔴 <b>종전엔 부서를 만들 방법 자체가 없었다.</b> <c>src/</c> 전체에
/// <c>INSERT INTO departments</c> 가 0건이었고(등록·수정·삭제 전부 부재),
/// 화면(<c>HrDepartmentsPage</c>)도 2열 조회 전용이었다.
/// 그래서 <c>departments</c> 0행 → 사원 등록의 부서 드롭다운 선택지 0개 →
/// 사원 전원 부서 없음. <b>버그가 아니라 필연이었다.</b>
///
/// 부서방(메신저)·조직도·부서별 결재선이 전부 이 축을 딛고 서므로 토대에서 세운다.
/// 구조는 <see cref="PositionService"/>(직급) 와 같은 패턴을 따른다 — 새 방식을 만들지 않는다.
/// </remarks>
public sealed class DepartmentService : IDepartmentService
{
    private readonly IDbConnection _db;

    public DepartmentService(IDbConnection db)
    {
        _db = db;
    }

    public async Task<List<DepartmentListDto>> GetListAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 상위 부서 이름과 재직 사원 수를 함께 준다.
        // 🔴 사원 수는 삭제 전 경고에 쓴다 — "이 부서에 3명이 있습니다" 를 못 보여주면
        //    관리자가 사람이 물린 부서를 지우고 나서야 안다.
        // ⚠️ is_active=1 인 재직자만 센다. 퇴사자까지 세면 이미 빈 부서를 못 지운다.
        const string sql = """
            SELECT
              d.dept_id        AS DeptId,
              d.parent_dept_id AS ParentDeptId,
              d.dept_name      AS DeptName,
              d.dept_code      AS DeptCode,
              d.sort_order     AS SortOrder,
              d.is_active      AS IsActive,
              p.dept_name      AS ParentDeptName,
              (SELECT COUNT(*) FROM employees e
                WHERE e.tenant_id = d.tenant_id
                  AND e.dept_id = d.dept_id
                  AND e.is_active = 1) AS EmployeeCount
            FROM departments d
            LEFT JOIN departments p
              ON p.dept_id = d.parent_dept_id
             AND p.tenant_id = d.tenant_id
            WHERE d.tenant_id = @TenantId
            ORDER BY d.sort_order, d.dept_name
            """;

        var rows = await _db.QueryAsync<DepartmentListDto>(new CommandDefinition(
            sql,
            new { TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    public async Task<string> CreateAsync(string tenantId, CreateDepartmentRequest request,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var deptName = (request.DeptName ?? string.Empty).Trim();
        if (deptName.Length == 0)
        {
            throw new InvalidOperationException("부서 이름을 입력해 주세요.");
        }

        // 같은 이름의 부서가 이미 있으면 막는다. 표에 유니크 제약이 없어(실측: PK·tenant 인덱스뿐)
        // 여기서 지킨다 — 이름이 같은 부서가 둘이면 사원 화면 드롭다운에서 무엇을 고른 건지 알 수 없다.
        await EnsureNameNotTakenAsync(tenantId, deptName, excludeDeptId: null, ct).ConfigureAwait(false);

        var parentId = NormalizeParent(request.ParentDeptId);
        if (parentId is not null)
        {
            await EnsureParentExistsAsync(tenantId, parentId, ct).ConfigureAwait(false);
        }

        var deptId = Guid.NewGuid().ToString();

        // ⚠️ sort_order·is_active·created_at·updated_at 은 NOT NULL 인데 기본값이 없다(실측).
        //    전부 명시해서 넣는다 — 빠뜨리면 런타임에 터진다(헌법 #13 DESCRIBE 의무).
        const string sql = """
            INSERT INTO departments
              (dept_id, tenant_id, parent_dept_id, dept_name, dept_code,
               sort_order, is_active, created_at, updated_at)
            VALUES
              (@DeptId, @TenantId, @ParentDeptId, @DeptName, @DeptCode,
               @SortOrder, @IsActive, NOW(6), NOW(6))
            """;

        await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                DeptId = deptId,
                TenantId = tenantId,
                ParentDeptId = parentId,
                DeptName = deptName,
                DeptCode = NormalizeCode(request.DeptCode),
                SortOrder = request.SortOrder,
                IsActive = request.IsActive ? 1 : 0
            },
            cancellationToken: ct)).ConfigureAwait(false);

        return deptId;
    }

    public async Task<bool> UpdateAsync(string tenantId, string deptId, UpdateDepartmentRequest request,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var deptName = (request.DeptName ?? string.Empty).Trim();
        if (deptName.Length == 0)
        {
            throw new InvalidOperationException("부서 이름을 입력해 주세요.");
        }

        await EnsureNameNotTakenAsync(tenantId, deptName, excludeDeptId: deptId, ct).ConfigureAwait(false);

        var parentId = NormalizeParent(request.ParentDeptId);

        // 🔴 자기 자신을 상위로 두면 조직도가 무한히 돈다.
        if (parentId == deptId)
        {
            throw new InvalidOperationException("자기 자신을 상위 부서로 지정할 수 없습니다.");
        }

        if (parentId is not null)
        {
            await EnsureParentExistsAsync(tenantId, parentId, ct).ConfigureAwait(false);

            // 🔴 순환 차단 — 내 하위(자손) 부서를 상위로 지정하면 고리가 생긴다.
            //    예: 영업부 > 영업1팀 인데 영업부의 상위를 영업1팀으로 두는 경우.
            //    조직도를 그리거나 부서방을 만들 때 무한 재귀로 앱이 멎는다.
            if (await IsDescendantAsync(tenantId, ancestorId: deptId, candidateId: parentId, ct)
                    .ConfigureAwait(false))
            {
                throw new InvalidOperationException("하위 부서를 상위 부서로 지정할 수 없습니다.");
            }
        }

        const string sql = """
            UPDATE departments
            SET parent_dept_id = @ParentDeptId,
                dept_name      = @DeptName,
                dept_code      = @DeptCode,
                sort_order     = @SortOrder,
                is_active      = @IsActive,
                updated_at     = NOW(6)
            WHERE tenant_id = @TenantId
              AND dept_id = @DeptId
            """;

        var affected = await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                TenantId = tenantId,
                DeptId = deptId,
                ParentDeptId = parentId,
                DeptName = deptName,
                DeptCode = NormalizeCode(request.DeptCode),
                SortOrder = request.SortOrder,
                IsActive = request.IsActive ? 1 : 0
            },
            cancellationToken: ct)).ConfigureAwait(false);

        return affected > 0;
    }

    public async Task<DeleteDepartmentResult> DeleteAsync(string tenantId, string deptId,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 🔴 사원이 물려 있으면 지우지 않는다. 지우면 그 사원들의 dept_id 가 유령을 가리켜
        //    사원 목록의 부서칸이 빈칸이 되고, 어느 부서였는지 되돌릴 수 없다.
        //    직급(PositionService)이 쓰는 원칙과 같다 — 사용 중이면 비활성으로 대체.
        var empCount = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM employees WHERE tenant_id = @TenantId AND dept_id = @DeptId",
            new { TenantId = tenantId, DeptId = deptId },
            cancellationToken: ct)).ConfigureAwait(false);

        var childCount = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM departments WHERE tenant_id = @TenantId AND parent_dept_id = @DeptId",
            new { TenantId = tenantId, DeptId = deptId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (empCount > 0 || childCount > 0)
        {
            var deactivated = await _db.ExecuteAsync(new CommandDefinition(
                "UPDATE departments SET is_active = 0, updated_at = NOW(6) WHERE tenant_id = @TenantId AND dept_id = @DeptId",
                new { TenantId = tenantId, DeptId = deptId },
                cancellationToken: ct)).ConfigureAwait(false);

            if (deactivated == 0)
            {
                return new DeleteDepartmentResult
                {
                    Deleted = false,
                    Message = "부서를 찾을 수 없습니다."
                };
            }

            // 🔴 왜 안 지워졌는지 사실대로 알린다(단계3 검증에서 같은 지적을 받았다).
            var reason = (empCount, childCount) switch
            {
                ( > 0, > 0) => $"소속 사원 {empCount}명과 하위 부서 {childCount}개가 있어 사용 안 함으로 돌렸습니다.",
                ( > 0, _)   => $"소속 사원 {empCount}명이 있어 사용 안 함으로 돌렸습니다.",
                _           => $"하위 부서 {childCount}개가 있어 사용 안 함으로 돌렸습니다."
            };

            return new DeleteDepartmentResult
            {
                Deleted = false,
                Deactivated = true,
                Message = reason + " 사원을 다른 부서로 옮긴 뒤 지울 수 있습니다."
            };
        }

        var removed = await _db.ExecuteAsync(new CommandDefinition(
            "DELETE FROM departments WHERE tenant_id = @TenantId AND dept_id = @DeptId",
            new { TenantId = tenantId, DeptId = deptId },
            cancellationToken: ct)).ConfigureAwait(false);

        return removed > 0
            ? new DeleteDepartmentResult { Deleted = true }
            : new DeleteDepartmentResult { Deleted = false, Message = "부서를 찾을 수 없습니다." };
    }

    // ───────────────────────────────────────────────────────────────
    // 헬퍼
    // ───────────────────────────────────────────────────────────────

    /// <summary>빈 문자열을 null 로 맞춘다 — 화면에서 "선택 안 함" 이 "" 로 온다.</summary>
    private static string? NormalizeParent(string? parentDeptId)
        => string.IsNullOrWhiteSpace(parentDeptId) ? null : parentDeptId.Trim();

    private static string? NormalizeCode(string? code)
        => string.IsNullOrWhiteSpace(code) ? null : code.Trim();

    private async Task EnsureNameNotTakenAsync(string tenantId, string deptName, string? excludeDeptId,
        CancellationToken ct)
    {
        var dup = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM departments
            WHERE tenant_id = @TenantId
              AND dept_name = @DeptName
              AND (@ExcludeId IS NULL OR dept_id <> @ExcludeId)
            """,
            new { TenantId = tenantId, DeptName = deptName, ExcludeId = excludeDeptId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (dup > 0)
        {
            throw new InvalidOperationException($"'{deptName}' 부서가 이미 있습니다.");
        }
    }

    private async Task EnsureParentExistsAsync(string tenantId, string parentDeptId, CancellationToken ct)
    {
        var exists = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM departments WHERE tenant_id = @TenantId AND dept_id = @DeptId",
            new { TenantId = tenantId, DeptId = parentDeptId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (exists == 0)
        {
            throw new InvalidOperationException("상위 부서를 찾을 수 없습니다.");
        }
    }

    /// <summary>
    /// <paramref name="candidateId"/> 가 <paramref name="ancestorId"/> 의 자손인가.
    /// </summary>
    /// <remarks>
    /// 위로 거슬러 올라가며 본다. 이미 고리가 있는 데이터(수동 INSERT 등)에서도 멎지 않도록
    /// <b>방문한 부서를 기억</b>하고, 깊이도 제한한다.
    /// </remarks>
    private async Task<bool> IsDescendantAsync(string tenantId, string ancestorId, string candidateId,
        CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = candidateId;

        for (var depth = 0; depth < 64 && current is not null; depth++)
        {
            if (!seen.Add(current))
            {
                // 이미 지나온 자리로 돌아왔다 = 데이터에 고리가 있다. 더 돌지 않는다.
                return false;
            }

            if (current == ancestorId)
            {
                return true;
            }

            current = await _db.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT parent_dept_id FROM departments WHERE tenant_id = @TenantId AND dept_id = @DeptId",
                new { TenantId = tenantId, DeptId = current },
                cancellationToken: ct)).ConfigureAwait(false);
        }

        return false;
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
}
