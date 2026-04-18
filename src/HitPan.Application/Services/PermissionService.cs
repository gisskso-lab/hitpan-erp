using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Permission;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

public class PermissionService : IPermissionService
{
    private readonly IDbConnection _db;

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
        ("DASHBOARD", "대시보드"),
        ("SETTINGS", "사용환경설정"),
        ("USERS", "사용자관리")
    ];

    public PermissionService(IDbConnection db)
    {
        _db = db;
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
