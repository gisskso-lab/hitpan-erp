using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Permission;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

public class PermissionService : IPermissionService
{
    private readonly IDbConnection _db;
    private readonly ICurrentTenant _currentTenant;

    // 🔴 ERP 권한 메뉴 진실원.
    //
    // ⚠️ 이 목록을 고칠 때는 반드시 프론트
    //    src/HitPan.Web/Pages/Settings/PermissionPage.razor.cs 의 ErpMenus 를 함께 고친다.
    //    한쪽만 고치면 화면에서 체크·저장해도 권한이 영원히 안 먹는다(2026-08-09 봉합 사고).
    //    CI 스크립트 scripts/check-permission-menu-sync.sh 가 두 목록의 Code 집합을 비교해 막는다.
    private static readonly List<(string Code, string Name)> MenuList =
    [
        ("DELIVERY", "거래명세서"),
        ("QUOTATION", "견적서"),
        ("PURCHASE_ORDER", "발주서"),
        ("SALES_ORDER", "수주서"),
        ("PURCHASE", "매입명세서"),
        ("ITEM", "상품마스터"),
        ("PARTNER", "업체마스터"),
        ("BOM", "BOM 자재명세서"),
        ("STOCK", "재고현황"),
        ("LEDGER", "원장"),
        ("COLLECTION", "수금"),
        ("PAYMENT", "지급"),
        ("ACCOUNTING", "회계"),
        ("APPROVAL", "결재"),
        ("HR", "인사"),
        // 작(2026-08-21) 작10 A — 남의 근태를 대신 넣는 권한. 사장님: "남의 근퇴 넣는건 권한설정에 넣자."
        // 🔴 HR 5축(view/create/update/delete/export)에 얹지 않는다.
        //    5축은 "내 데이터에 무엇을 하나" 축이고 이것은 "남의 데이터를 건드리나" 축이다.
        //    update 에 얹으면 자기 근태 고치라고 준 권한이 남의 근태까지 연다.
        // 🔴 기본 OFF — 고객사가 켠다. 전원 계정 주는 회사는 안 켜면 그만이다(헌법 #11).
        //    사장님: "이건 고객사 마음이지" / "우리가 정할게 아님"
        ("HR_PROXY", "근태 대리입력"),
        ("MONTHLY_CLOSING", "월마감"),
        ("CERTIFICATE", "범용인증서"),
        ("DASHBOARD", "대시보드"),
        ("SETTINGS", "사용환경설정"),
        ("USERS", "사용자관리")
    ];

    public PermissionService(IDbConnection db, ICurrentTenant currentTenant)
    {
        _db = db;
        _currentTenant = currentTenant;
    }

    public async Task<List<UserPermissionDto>> GetAllUsersPermissionsAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // ERP 내부 권한은 employees 테이블 기준 (SaaS 계층 혼용 금지)
        var employees = (await _db.QueryAsync<EmployeeRow>(new CommandDefinition(
            """
            SELECT
                e.employee_id AS EmployeeId,
                e.user_id AS UserId,
                e.emp_name AS EmpName,
                e.emp_no AS EmpNo,
                e.position AS Position,
                e.role AS Role
            FROM employees e
            WHERE e.tenant_id = @TenantId
              AND e.is_active = 1
            ORDER BY
              CASE e.role
                WHEN 'TenantAdmin' THEN 1
                WHEN 'Manager' THEN 2
                ELSE 3
              END,
              e.emp_name
            """,
            new { TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false)).ToList();

        var result = new List<UserPermissionDto>();
        foreach (var emp in employees)
        {
            var permKey = emp.UserId ?? emp.EmployeeId;
            var dto = await GetByKeyAsync(permKey, tenantId, ct).ConfigureAwait(false);

            result.Add(new UserPermissionDto
            {
                UserId = permKey,
                UserName = emp.EmpNo,
                EmpName = emp.EmpName,
                Role = emp.Role,
                Permissions = dto?.Permissions ?? BuildDefaultMenus()
            });
        }

        return result;
    }

    public async Task<UserPermissionDto?> GetAsync(string userId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // employees 기준으로 먼저 조회 (user_id 또는 employee_id)
        var emp = await _db.QueryFirstOrDefaultAsync<EmployeeRow>(new CommandDefinition(
            """
            SELECT
                e.employee_id AS EmployeeId,
                e.user_id AS UserId,
                e.emp_name AS EmpName,
                e.emp_no AS EmpNo,
                e.position AS Position,
                e.role AS Role
            FROM employees e
            WHERE e.tenant_id = @TenantId
              AND e.is_active = 1
              AND (e.user_id = @Key OR e.employee_id = @Key)
            """,
            new { Key = userId, TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (emp is null)
        {
            return null;
        }

        var permKey = emp.UserId ?? emp.EmployeeId;
        var dto = await GetByKeyAsync(permKey, tenantId, ct).ConfigureAwait(false);

        return new UserPermissionDto
        {
            UserId = permKey,
            UserName = emp.EmpNo,
            EmpName = emp.EmpName,
            Role = emp.Role,
            Permissions = dto?.Permissions ?? BuildDefaultMenus()
        };
    }

    private async Task<UserPermissionDto?> GetByKeyAsync(string permKey, string tenantId, CancellationToken ct)
    {
        var saved = (await _db.QueryAsync<MenuPermissionDto>(new CommandDefinition(
            """
            SELECT
                menu_code AS MenuCode,
                can_view AS CanView,
                can_create AS CanCreate,
                can_update AS CanUpdate,
                can_delete AS CanDelete,
                can_export AS CanExport
            FROM user_permissions
            WHERE user_id = @UserId
              AND tenant_id = @TenantId
            """,
            new { UserId = permKey, TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false)).ToDictionary(x => x.MenuCode, StringComparer.OrdinalIgnoreCase);

        var perms = MenuList.Select(m =>
        {
            if (saved.TryGetValue(m.Code, out var p))
            {
                p.MenuName = m.Name;
                return p;
            }

            return new MenuPermissionDto
            {
                MenuCode = m.Code,
                MenuName = m.Name
            };
        }).ToList();

        return new UserPermissionDto
        {
            UserId = permKey,
            Permissions = perms
        };
    }

    private List<MenuPermissionDto> BuildDefaultMenus()
    {
        return MenuList.Select(m => new MenuPermissionDto
        {
            MenuCode = m.Code,
            MenuName = m.Name
        }).ToList();
    }

    public async Task SaveAsync(SavePermissionsDto dto, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        foreach (var p in dto.Permissions)
        {
            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO user_permissions (
                  perm_id, tenant_id, user_id,
                  menu_code, can_view,
                  can_create, can_update,
                  can_delete, can_export,
                  created_at, updated_at)
                VALUES (
                  UUID(), @TenantId, @UserId,
                  @MenuCode, @CanView,
                  @CanCreate, @CanUpdate,
                  @CanDelete, @CanExport,
                  NOW(6), NOW(6))
                ON DUPLICATE KEY UPDATE
                  can_view   = @CanView,
                  can_create = @CanCreate,
                  can_update = @CanUpdate,
                  can_delete = @CanDelete,
                  can_export = @CanExport,
                  updated_at = NOW(6)
                """,
                new
                {
                    TenantId = tenantId,
                    UserId = dto.UserId,
                    MenuCode = p.MenuCode,
                    CanView = p.CanView ? 1 : 0,
                    CanCreate = p.CanCreate ? 1 : 0,
                    CanUpdate = p.CanUpdate ? 1 : 0,
                    CanDelete = p.CanDelete ? 1 : 0,
                    CanExport = p.CanExport ? 1 : 0
                },
                cancellationToken: ct)).ConfigureAwait(false);
        }
    }

    public async Task<bool> HasPermissionAsync(string userId, string tenantId, string menuCode, string action, CancellationToken ct = default)
    {
        // Layer 0 — 부모계정(tenant_admin)은 항상 전권 (락아웃 방지)
        //   platform_admin 절 제거 (보안 격벽 2026-06-18): 본사 계층은 백오피스 전용 — ERP가 발급 안 함.
        if (userId == _currentTenant.UserId &&
            _currentTenant.AccountType == "tenant_admin")
        {
            return true;
        }

        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var col = action.ToLowerInvariant() switch
        {
            "view" => "can_view",
            "create" => "can_create",
            "update" => "can_update",
            "delete" => "can_delete",
            "export" => "can_export",
            _ => "can_view"
        };

        var result = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            $"""
            SELECT COALESCE({col}, 0)
            FROM user_permissions
            WHERE user_id = @UserId
              AND tenant_id = @TenantId
              AND menu_code = @MenuCode
            """,
            new { UserId = userId, TenantId = tenantId, MenuCode = menuCode },
            cancellationToken: ct)).ConfigureAwait(false);

        return result == 1;
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

    private sealed class EmployeeRow
    {
        public string EmployeeId { get; set; } = "";
        public string? UserId { get; set; }
        public string EmpName { get; set; } = "";
        public string EmpNo { get; set; } = "";
        public string? Position { get; set; }
        public string Role { get; set; } = "";
    }
}
