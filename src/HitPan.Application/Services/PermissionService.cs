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

        var users = (await _db.QueryAsync<UserRow>(new CommandDefinition(
            """
            SELECT
                user_id AS UserId,
                user_name AS UserName,
                emp_name AS EmpName,
                role AS Role
            FROM users
            WHERE tenant_id = @TenantId
              AND is_deleted = 0
              AND is_active = 1
              AND account_type IN ('tenant_admin', 'tenant_user') -- 플랫폼·리셀러 관리자는 권한 설정 대상에서 제외
            ORDER BY
              CASE role
                WHEN 'TenantAdmin' THEN 1
                WHEN 'Manager' THEN 2
                ELSE 3
              END,
              COALESCE(emp_name, user_name)
            """,
            new { TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false)).ToList();

        var result = new List<UserPermissionDto>();
        foreach (var u in users)
        {
            var dto = await GetAsync(u.UserId, tenantId, ct).ConfigureAwait(false);
            if (dto is not null)
            {
                result.Add(dto);
            }
        }

        return result;
    }

    public async Task<UserPermissionDto?> GetAsync(string userId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var user = await _db.QueryFirstOrDefaultAsync<UserRow>(new CommandDefinition(
            """
            SELECT
                user_id AS UserId,
                user_name AS UserName,
                emp_name AS EmpName,
                role AS Role
            FROM users
            WHERE user_id = @UserId
              AND tenant_id = @TenantId
              AND is_deleted = 0
              AND account_type IN ('tenant_admin', 'tenant_user') -- 플랫폼·리셀러 관리자는 조회·편집 대상에서 제외
            """,
            new { UserId = userId, TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (user is null || string.IsNullOrWhiteSpace(user.UserId))
        {
            return null;
        }

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
            new { UserId = userId, TenantId = tenantId },
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
            UserId = user.UserId,
            UserName = user.UserName,
            EmpName = user.EmpName,
            Role = user.Role,
            Permissions = perms
        };
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

    private sealed class UserRow
    {
        public string UserId { get; set; } = "";
        public string UserName { get; set; } = "";
        public string? EmpName { get; set; }
        public string Role { get; set; } = "";
    }
}
