using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.User;
using HitPan.Application.Interfaces;
using HitPan.Domain.Enums;

namespace HitPan.Application.Services;

public sealed class UserService : IUserService
{
    private readonly IDbConnection _db;

    public UserService(IDbConnection db)
    {
        _db = db;
    }

    public async Task<List<UserListDto>> GetListAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT
                user_id AS UserId,
                email AS Email,
                user_name AS UserName,
                emp_name AS EmpName,
                department AS Department,
                position AS Position,
                phone AS Phone,
                role AS Role,
                account_type AS AccountType,
                is_active AS IsActive,
                hire_date AS HireDate,
                created_at AS CreatedAt
            FROM users
            WHERE tenant_id = @TenantId
              AND is_deleted = 0
              AND account_type IN ('tenant_admin', 'tenant_user')
            ORDER BY
                CASE role
                    WHEN 'TenantAdmin' THEN 1
                    WHEN 'Manager' THEN 2
                    ELSE 3
                END,
                emp_name,
                user_name
            """;

        var rows = await _db.QueryAsync<UserListDto>(
            new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToList();
    }

    public async Task<UserListDto?> GetAsync(string userId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT
                user_id AS UserId,
                email AS Email,
                user_name AS UserName,
                emp_name AS EmpName,
                department AS Department,
                position AS Position,
                phone AS Phone,
                role AS Role,
                account_type AS AccountType,
                is_active AS IsActive,
                hire_date AS HireDate,
                created_at AS CreatedAt
            FROM users
            WHERE user_id = @UserId
              AND tenant_id = @TenantId
              AND is_deleted = 0
            """;

        return await _db.QueryFirstOrDefaultAsync<UserListDto>(
            new CommandDefinition(sql, new { UserId = userId, TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<string> CreateAsync(CreateUserDto dto, string tenantId, CancellationToken ct = default)
    {
        ValidatePassword(dto.Password);
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var dup = await _db.ExecuteScalarAsync<long>(
            new CommandDefinition(
                """
                SELECT COUNT(*) FROM users
                WHERE tenant_id = @TenantId
                  AND email = @Email
                  AND is_deleted = 0
                """,
                new { TenantId = tenantId, dto.Email },
                cancellationToken: ct)).ConfigureAwait(false);

        if (dup > 0)
        {
            throw new InvalidOperationException("이미 사용 중인 이메일입니다.");
        }

        var userId = Guid.NewGuid().ToString();

        var role = ParseUserRole(dto.Role);
        var roleStr = role.ToString();
        var accountType = role == UserRole.TenantAdmin ? "tenant_admin" : "tenant_user";
        var hash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

        await _db.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO users (
                    user_id, tenant_id, email,
                    password_hash, user_name,
                    emp_name, department, position,
                    phone, role, account_type,
                    hire_date, memo,
                    is_active, is_deleted,
                    created_at, updated_at)
                VALUES (
                    @UserId, @TenantId, @Email,
                    @Hash, @UserName,
                    @EmpName, @Department, @Position,
                    @Phone, @Role, @AccountType,
                    @HireDate, @Memo,
                    1, 0,
                    NOW(6), NOW(6))
                """,
                new
                {
                    UserId = userId,
                    TenantId = tenantId,
                    dto.Email,
                    Hash = hash,
                    UserName = dto.UserName,
                    EmpName = dto.EmpName,
                    Department = dto.Department,
                    Position = dto.Position,
                    Phone = dto.Phone,
                    Role = roleStr,
                    AccountType = accountType,
                    HireDate = dto.HireDate,
                    Memo = dto.Memo
                },
                cancellationToken: ct)).ConfigureAwait(false);

        return userId;
    }

    public async Task UpdateAsync(string userId, UpdateUserDto dto, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var role = ParseUserRole(dto.Role);
        var roleStr = role.ToString();
        var accountType = role == UserRole.TenantAdmin ? "tenant_admin" : "tenant_user";

        await _db.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE users SET
                    user_name = @UserName,
                    emp_name = @EmpName,
                    department = @Department,
                    position = @Position,
                    phone = @Phone,
                    role = @Role,
                    account_type = @AccountType,
                    is_active = @IsActive,
                    hire_date = @HireDate,
                    memo = @Memo,
                    updated_at = NOW(6)
                WHERE user_id = @UserId
                  AND tenant_id = @TenantId
                  AND is_deleted = 0
                """,
                new
                {
                    UserId = userId,
                    TenantId = tenantId,
                    UserName = dto.UserName,
                    EmpName = dto.EmpName,
                    Department = dto.Department,
                    Position = dto.Position,
                    Phone = dto.Phone,
                    Role = roleStr,
                    AccountType = accountType,
                    IsActive = dto.IsActive ? 1 : 0,
                    HireDate = dto.HireDate,
                    Memo = dto.Memo
                },
                cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task DeactivateAsync(string userId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        await _db.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE users SET
                    is_active = 0,
                    is_deleted = 1,
                    deleted_at = NOW(6),
                    updated_at = NOW(6)
                WHERE user_id = @UserId
                  AND tenant_id = @TenantId
                """,
                new { UserId = userId, TenantId = tenantId },
                cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<string> ResetPasswordAsync(string userId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var temp = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var hash = BCrypt.Net.BCrypt.HashPassword(temp);

        var affected = await _db.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE users SET
                    password_hash = @Hash,
                    updated_at = NOW(6)
                WHERE user_id = @UserId
                  AND tenant_id = @TenantId
                  AND is_deleted = 0
                """,
                new { Hash = hash, UserId = userId, TenantId = tenantId },
                cancellationToken: ct)).ConfigureAwait(false);

        if (affected == 0)
        {
            throw new InvalidOperationException("사용자를 찾을 수 없습니다.");
        }

        return temp;
    }

    private static UserRole ParseUserRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return UserRole.User;
        }

        return Enum.TryParse<UserRole>(role, ignoreCase: true, out var parsed)
            ? parsed
            : UserRole.User;
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            throw new InvalidOperationException("비밀번호는 최소 8자 이상이어야 합니다.");
        if (!password.Any(char.IsUpper))
            throw new InvalidOperationException("비밀번호에 대문자가 1개 이상 포함되어야 합니다.");
        if (!password.Any(char.IsDigit))
            throw new InvalidOperationException("비밀번호에 숫자가 1개 이상 포함되어야 합니다.");
        if (!password.Any(c => !char.IsLetterOrDigit(c)))
            throw new InvalidOperationException("비밀번호에 특수문자가 1개 이상 포함되어야 합니다.");
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
