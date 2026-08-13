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
    private readonly IAuditService _audit;

    public UserService(IDbConnection db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
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
                created_at AS CreatedAt,
                is_parent AS IsParent
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
                created_at AS CreatedAt,
                is_parent AS IsParent
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

        // 🔴 봉합 (2026-08-14, 1.2.74 실사용 P0): 트랜잭션으로 묶는다.
        //    종전엔 users·employees 두 INSERT 가 **각각 따로** 커밋됐다. 그래서 채번 충돌로
        //    employees 가 실패해도 **users 는 이미 커밋된 뒤**라 되돌릴 수 없었다
        //    ⇒ "계정은 생겼는데 사원은 없다" 는 사장님이 보신 그 상태가 됐다.
        //
        //    더 나쁜 것은 스스로 복구가 안 됐다는 점이다 — 재등록은 이메일 중복으로 막히고,
        //    사원관리에서 새로 넣으면 user_id 가 NULL 인 **별개 행**이 하나 더 생겼다.
        //
        //    이제 둘 중 하나라도 실패하면 **둘 다 없던 일**이 된다. 반쪽 계정이 안 생긴다.
        using var tx = _db.BeginTransaction();

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
                    EmpName = string.IsNullOrWhiteSpace(dto.EmpName) ? dto.UserName : dto.EmpName,
                    Department = dto.Department,
                    Position = dto.Position,
                    Phone = dto.Phone,
                    Role = roleStr,
                    AccountType = accountType,
                    HireDate = dto.HireDate,
                    Memo = dto.Memo
                },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        // 사원 자동 등록 — users 생성과 동시에 employees 행도 만들어 사원연결 완성
        //
        // 🔴 봉합 (2026-08-14, 1.2.74 실사용 P0) — 사장님: "자식계정은 생성되었으나
        //    직원계정 관리 외 다른 그 어떤메뉴에도 그 계정직원은 안나옴."
        //
        //  ■ 종전 채번이 왜 터졌나
        //    "... WHERE emp_no LIKE 'EMP-%'" 로 **EMP- 형식만** 셌다. 그런데 실측하니
        //    이 DB 의 사번은 0001(부모계정 백필) · MIG-0001~0010(마이그) 뿐으로
        //    **EMP- 가 0건**이었다 ⇒ MAX 가 항상 0 ⇒ 채번이 늘 1 ⇒ 언제나 'EMP-001'.
        //    employees 에는 uq_tenant_empno(tenant_id, emp_no) UNIQUE 가 있어
        //    **두 번째 자식계정부터 INSERT 가 실패**했다.
        //
        //  ⇒ 접두를 가리지 않고 **끝의 숫자**로 센다. 0001·MIG-0007·EMP-012 를 모두 본다.
        //    REGEXP 로 숫자 꼬리를 뽑아 최대값을 구한다(형식이 섞여 있어도 안 깨진다).
        var maxNo = await _db.ExecuteScalarAsync<int?>(new CommandDefinition(
            """
            SELECT MAX(CAST(REGEXP_SUBSTR(emp_no, '[0-9]+$') AS UNSIGNED))
            FROM employees
            WHERE tenant_id = @TenantId
              AND emp_no REGEXP '[0-9]+$'
            """,
            new { TenantId = tenantId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        var empNo = (maxNo ?? 0) + 1;

        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO employees (
                employee_id, tenant_id, user_id,
                emp_no, emp_name,
                position, emp_type,
                join_date, is_active, role,
                annual_leave_total, annual_leave_used,
                created_at, created_by, updated_at, updated_by)
            VALUES (
                @EmpId, @TenantId, @UserId,
                @EmpNo, @EmpName,
                @Position, 'regular',
                @JoinDate, 1, @Role,
                15.0, 0.0,
                NOW(6), @UserId, NOW(6), @UserId)
            """,
            new
            {
                EmpId = Guid.NewGuid().ToString(),
                TenantId = tenantId,
                UserId = userId,
                EmpNo = $"EMP-{empNo:D3}",
                EmpName = string.IsNullOrWhiteSpace(dto.EmpName) ? dto.UserName : dto.EmpName,
                Position = dto.Position ?? string.Empty,
                JoinDate = dto.HireDate ?? DateTime.UtcNow,
                Role = roleStr
            },
            transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        // 🔴 여기까지 와야 둘 다 진짜로 남는다. Commit 을 빠뜨리면 using 이 끝나며
        //    통째로 되돌아가 **계정이 아예 안 생긴다**(반대 방향 사고).
        tx.Commit();

        // 감사로그 — 사용자 생성
        var afterJson = $"{{\"email\":\"{dto.Email}\",\"user_name\":\"{dto.UserName}\",\"role\":\"{roleStr}\",\"account_type\":\"{accountType}\"}}";
        await _audit.LogAsync("create", "user", userId, afterJson: afterJson, ct: ct);

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
                    EmpName = string.IsNullOrWhiteSpace(dto.EmpName) ? dto.UserName : dto.EmpName,
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

        // 감사로그 — 사용자 수정
        var afterJson = $"{{\"user_name\":\"{dto.UserName}\",\"role\":\"{roleStr}\",\"is_active\":{(dto.IsActive ? "true" : "false")}}}";
        await _audit.LogAsync("update", "user", userId, afterJson: afterJson, ct: ct);
    }

    public async Task DeactivateAsync(string userId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 헌법 #35 (사장님 결재 2026-06-04) — 부모계정은 삭제 차단
        var isParent = await _db.QueryFirstOrDefaultAsync<int?>(
            new CommandDefinition(
                "SELECT is_parent FROM users WHERE user_id = @UserId AND tenant_id = @TenantId",
                new { UserId = userId, TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
        if (isParent == 1)
            throw new InvalidOperationException("부모 계정은 삭제할 수 없습니다. 회사 정보·라이선스의 마스터 계정입니다.");

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

        // 감사로그 — 사용자 비활성화 (소프트 삭제)
        await _audit.LogAsync("delete", "user", userId, ct: ct);
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

        // 감사로그 — 비밀번호 초기화 (보안 민감 이벤트)
        await _audit.LogAsync("update", "user", userId, afterJson: "{\"action\":\"password_reset\"}", ct: ct);

        return temp;
    }

    public async Task<BulkCreateResultDto> BulkCreateAsync(List<CreateUserDto> rows, string tenantId, CancellationToken ct = default)
    {
        var result = new BulkCreateResultDto { TotalRows = rows.Count };

        // 각 행 독립 처리 — 한 행 실패해도 나머지 진행
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            try
            {
                await CreateAsync(row, tenantId, ct).ConfigureAwait(false);
                result.SuccessCount++;
            }
            catch (Exception ex)
            {
                result.FailedCount++;
                result.Errors.Add(new BulkRowError
                {
                    Row = i + 1,
                    Email = row.Email,
                    Reason = ex.Message
                });
            }
        }

        // 일괄 업로드 자체 이벤트 감사로그
        await _audit.LogAsync("create", "user_bulk", null,
            afterJson: $"{{\"total\":{result.TotalRows},\"success\":{result.SuccessCount},\"failed\":{result.FailedCount}}}",
            ct: ct);

        return result;
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
