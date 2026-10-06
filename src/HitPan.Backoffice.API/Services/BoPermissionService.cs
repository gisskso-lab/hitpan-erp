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

/// <summary>
/// 설계 §1-4 저장 거부 — 호출자(<c>BoPermissionsController</c>)가 <b>400 + 한글 사유</b>로 바꾼다.
/// (20261007작10 ①사이클 갈래 ㄱ)
/// </summary>
public sealed class BoPermissionRoleRejectedException : Exception
{
    public BoPermissionRoleRejectedException(string message) : base(message) { }
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
        // 🔴 20261007작10 ①사이클 갈래 ㄱ — 설계 §1-4 추가 방어.
        //    CSV 는 **기능 접근**만 정한다. 행 범위는 BoAccessGuard 가 정한다.
        //    그래도 본사 전용 키에 대리점 역할이 저장되면 「권한을 줬다고 믿는데 403」이 되어 혼선이 된다
        //    ⇒ 저장 자체를 거부한다. (행 범위가 안 넓어지는 것은 G-4 가 별도로 잰다 — 이 검사가 없어도 403 이다.)
        var reject = RejectReasonFor(permissionKey, allowedRolesCsv);
        if (reject is not null)
        {
            _logger.LogWarning("[BoPermission] 저장 거부 key={Key} roles={Roles} 사유={Why}",
                permissionKey, allowedRolesCsv, reject);
            throw new BoPermissionRoleRejectedException(reject);
        }

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

    // ══════════════════════════════════════════════════════════════════
    // 🔴 설계 §1-4 — CSV 가 행 범위를 못 넓힌다 (20261007작10 갈래 ㄱ)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 지금 <c>bo_permissions</c> 에 있는 키는 <b>전부 본사 업무</b>다(대리점 포털은 [BoPermission] 을 쓰지 않는다).
    /// 대리점 전용 키가 생기면 여기 등록한다 — 등록되지 않은 키에는 대리점 역할을 저장할 수 없다.
    /// </summary>
    private static readonly HashSet<string> ResellerFacingKeys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 알고 있는 역할 어휘. 🔴 <b>4값이 진실원</b>(<c>Security/BoRoles.cs</c>)이고 아래 옛 값들은
    /// <b>지금 DB·시드에 실제로 들어 있는 값</b>이라 같이 허용한다(실측 2026-10-07:
    /// <c>owner·admin·super_admin·platform_owner·platform_admin</c> 17행).
    /// <para>⚠️ <b>설계와 다른 점</b> — 설계 §1-4 는 「4값만」이라고 적었다. 그대로 하면 지금 저장된
    /// <c>owner·admin·super_admin</c> 이 전부 무효가 되어 <b>사장님 본인이 권한 화면에서 잠긴다</b>(#20 흐름 끊김).
    /// 역할 4값 전환은 2차수(갈래 ㄴ) 몫이므로, 이 차수는 <b>옛 값을 같이 허용</b>하고
    /// 설계가 노린 보안 성질(= 대리점 역할 차단)만 강제한다. 2차수에서 아래 옛 목록을 지우면 「4값만」이 된다.</para>
    /// </summary>
    private static readonly HashSet<string> KnownRoleVocabulary = new(StringComparer.OrdinalIgnoreCase)
    {
        // 4값 (진실원)
        Security.BoRoles.PlatformOwner, Security.BoRoles.PlatformAdmin,
        Security.BoRoles.ResellerAdmin, Security.BoRoles.ResellerUser,
        // ⬛ 옛 값 — 2차수에 제거 예정
        "owner", "admin", "super_admin", "billing_admin", "cs_admin", "readonly",
        "platform_manager", "platform_staff",
    };

    /// <summary>
    /// 저장 거부 사유(한글) 또는 통과 시 null. 테스트가 판정식을 베껴 쓰지 않도록 <c>internal static</c>.
    /// </summary>
    internal static string? RejectReasonFor(string permissionKey, string? allowedRolesCsv)
    {
        if (string.IsNullOrWhiteSpace(allowedRolesCsv))
            return "허용 역할이 비어 있습니다.";

        var roles = allowedRolesCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var unknown = roles.Where(r => !KnownRoleVocabulary.Contains(r)).ToList();
        if (unknown.Count > 0)
            return $"알 수 없는 역할입니다: {string.Join(", ", unknown)}";

        if (!ResellerFacingKeys.Contains(permissionKey))
        {
            var resellerRoles = roles
                .Where(r => Security.BoRoles.IsReseller(Security.BoRoles.Normalize(r)))
                .ToList();
            if (resellerRoles.Count > 0)
                return $"본사 전용 기능에는 대리점 역할을 저장할 수 없습니다: {string.Join(", ", resellerRoles)}";
        }

        return null;
    }

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
