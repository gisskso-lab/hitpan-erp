using System.Collections.Concurrent;
using Dapper;
using Microsoft.Extensions.Caching.Memory;
using MySqlConnector;

namespace HitPan.Backoffice.API.Services;

// 백오피스 계층 권한 서비스 (사장님 결재 2026-06-04, 헌법 #11·#35)
//
// 사장님이 /owner/permissions 에서 저장한 권한을 DB에서 읽어 검사.
// 60초 메모리 캐시 (변경 시 InvalidateAll로 즉시 무효화).
//
// 헌법 정합:
//   #11 — 권한은 어드민(사장님)이 직접 설정
//   #15 — 빈 catch 금지
//   #25 — 정확하게(DB 단일 진실원천) + 안전하게(캐시·트랜잭션)
public interface IBoPermissionService
{
    Task<bool> IsAllowedAsync(string permissionKey, string userRole, CancellationToken ct = default);
    Task<IReadOnlyList<PermissionRow>> ListAsync(CancellationToken ct = default);
    Task<int> UpdateAllowedRolesAsync(string permissionKey, string allowedRolesCsv, string updatedBy, CancellationToken ct = default);
    void InvalidateAll();
}

public class PermissionRow
{
    public string PermissionKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public string Category { get; set; } = "";
    public string AllowedRoles { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class BoPermissionService : IBoPermissionService
{
    private readonly IConfiguration _config;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BoPermissionService> _logger;
    private const string CacheKey = "bo_permissions_all";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public BoPermissionService(IConfiguration config, IMemoryCache cache, ILogger<BoPermissionService> logger)
    {
        _config = config;
        _cache = cache;
        _logger = logger;
    }

    public async Task<bool> IsAllowedAsync(string permissionKey, string userRole, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(permissionKey) || string.IsNullOrWhiteSpace(userRole))
            return false;

        var all = await GetAllCachedAsync(ct);
        if (!all.TryGetValue(permissionKey, out var row))
        {
            _logger.LogWarning("[BoPermission] 미저장 권한 key={Key}", permissionKey);
            return false;
        }

        var allowed = row.AllowedRoles
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return allowed.Any(r => string.Equals(r, userRole, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<PermissionRow>> ListAsync(CancellationToken ct = default)
    {
        var all = await GetAllCachedAsync(ct);
        return all.Values
            .OrderBy(r => r.Category)
            .ThenBy(r => r.PermissionKey)
            .ToList();
    }

    public async Task<int> UpdateAllowedRolesAsync(string permissionKey, string allowedRolesCsv, string updatedBy, CancellationToken ct = default)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var affected = await db.ExecuteAsync(@"
                UPDATE bo_permissions
                SET allowed_roles = @Roles, updated_by = @UpdatedBy
                WHERE permission_key = @Key",
                new { Key = permissionKey, Roles = allowedRolesCsv, UpdatedBy = updatedBy });

            InvalidateAll();
            _logger.LogInformation("[BoPermission] updated key={Key} roles={Roles} by={By}",
                permissionKey, allowedRolesCsv, updatedBy);
            return affected;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BoPermission] 갱신 실패 key={Key}", permissionKey);
            throw;
        }
    }

    public void InvalidateAll() => _cache.Remove(CacheKey);

    private async Task<Dictionary<string, PermissionRow>> GetAllCachedAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(CacheKey, out Dictionary<string, PermissionRow>? cached) && cached is not null)
            return cached;

        try
        {
            await using var db = await OpenAsync(ct);
            var rows = await db.QueryAsync<PermissionRow>(@"
                SELECT permission_key AS PermissionKey, display_name AS DisplayName,
                       description AS Description, category AS Category,
                       allowed_roles AS AllowedRoles, updated_at AS UpdatedAt, updated_by AS UpdatedBy
                FROM bo_permissions");

            var dict = rows.ToDictionary(r => r.PermissionKey, r => r);
            _cache.Set(CacheKey, dict, CacheTtl);
            return dict;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BoPermission] DB 조회 실패");
            return new Dictionary<string, PermissionRow>();
        }
    }

    private async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var cs = _config.GetConnectionString("BackofficeDb")
                 ?? _config.GetConnectionString("Default")
                 ?? throw new InvalidOperationException("ConnectionStrings:BackofficeDb 미설정");
        var c = new MySqlConnection(cs);
        await c.OpenAsync(ct);
        return c;
    }
}
