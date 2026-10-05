using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.User;
using HitPan.Application.Interfaces;
using HitPan.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

public sealed class UserService : IUserService
{
    private readonly IDbConnection _db;
    private readonly IAuditService _audit;
    // 20261005작3 — 판정기 바닥값(5)으로 간 경우 LogWarning(설계 §2 ③). 없으면 로그만 빠진다.
    private readonly ILogger<UserService>? _logger;

    public UserService(IDbConnection db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    // 20261005작3 — DI 는 채울 수 있는 가장 긴 생성자(이것)를 쓴다. 위 생성자는 지우지 않는다(#1 · 시험이 직접 만든다).
    public UserService(IDbConnection db, IAuditService audit, ILogger<UserService> logger)
        : this(db, audit)
    {
        _logger = logger;
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
        // 🔴 20261005작5 [3-V] P3-09 — 이메일 형식 강요([EmailAddress])를 걷은 뒤 남는 아이디 서버 검증(부트스트랩과 같은 규칙).
        ValidateLoginId(dto.Email);
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
            // ⬛ throw new InvalidOperationException("이미 사용 중인 이메일입니다.");
            // 20261005작5 §6 — 계정 칸 이름은 「아이디」(사원 이메일 칸과 헷갈리지 않게 · 사장님 ③ 추가·④)
            throw new InvalidOperationException(LoginIdTakenMessage);
        }

        // 🔴 10/5 봉합1 P2-07 — 옛 DELETE(DeactivateAsync)로 지운 행은 아이디를 그대로 쥐고 있다(uq_tenant_email).
        //   위 검사는 is_deleted=0 만 봐서 통과하고 INSERT 에서 UNIQUE 예외 ⇒ 500 이었다. DB-136 이 표식을 붙이지만
        //   아직 안 돈 DB 를 위해 여기서 409 친절 문구로 먼저 막는다.
        var heldByDeleted = await _db.ExecuteScalarAsync<long>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM users WHERE tenant_id = @TenantId AND email = @Email AND is_deleted = 1",
                new { TenantId = tenantId, dto.Email },
                cancellationToken: ct)).ConfigureAwait(false);
        if (heldByDeleted > 0)
            throw new InvalidOperationException(DeletedHoldsLoginIdMessage);

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

        // 🔴 20261005작3 E-1 — 계정 사용 한도 판정. **트랜잭션 첫 문장**이어야 한다(설계 §2 · G-A1·G-A6).
        // 🔴 10/5 봉합1 P2-05 — 첫 문장은 잠금 손잡이(행 없으면 이름 잠금). 판정은 그 뒤.
        await using var seatLock = await AccountSeatGuard.AcquireAsync(_db, tx, tenantId, _logger, ct).ConfigureAwait(false);
        await AccountSeatGuard.EnsureSeatAsync(_db, tx, tenantId, 1, _logger, ct).ConfigureAwait(false);

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

        // ⬛ [20261005작5 전] INSERT 칸에 login_id 가 없었다 — 사원계정 칸(DB-137)을 같은 INSERT 에서 채운다(설계 §2-2).
        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO employees (
                employee_id, tenant_id, user_id, login_id,
                emp_no, emp_name,
                position, emp_type,
                join_date, is_active, role,
                annual_leave_total, annual_leave_used,
                created_at, created_by, updated_at, updated_by)
            VALUES (
                @EmpId, @TenantId, @UserId, @LoginId,
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
                LoginId = dto.Email,
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

        // 🔴 20261005작3 E-4 — 트랜잭션으로 감싼다. 첫 문장 = 판정기 잠금과 **같은 행**(잠금 순서 고정 · 반증 F10).
        //    ① 대표(is_parent=1) 를 is_active=0 으로 바꾸는 요청 거절(반증 F3 · 기존 :284-289 문구 재사용)
        //    ② 0→1 일 때만 판정(API 직접 호출 우회 차단 · G-A5)
        using var tx = _db.BeginTransaction();
        // ⬛ [봉합1 전] await _db.ExecuteScalarAsync<string?>(... "SELECT tenant_id FROM local_company ... FOR UPDATE" ...);
        //   🔴 10/5 봉합1 P2-05 — 같은 행 잠금 + 행 없으면 이름 잠금(AcquireAsync). 잠금 순서는 그대로(첫 문장).
        await using var seatLock = await AccountSeatGuard.AcquireAsync(_db, tx, tenantId, _logger, ct).ConfigureAwait(false);

        // ⬛ [봉합1 전] "SELECT is_parent AS IsParent, is_active AS IsActive FROM users ... FOR UPDATE" — role 을 같이 읽는다(P1-02).
        var current = await _db.QueryFirstOrDefaultAsync<SeatStateRow>(new CommandDefinition(
            "SELECT is_parent AS IsParent, is_active AS IsActive, role AS Role FROM users WHERE user_id = @UserId AND tenant_id = @TenantId AND is_deleted = 0 FOR UPDATE",
            new { UserId = userId, TenantId = tenantId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        if (current is not null && current.IsParent && !dto.IsActive)
            throw new InvalidOperationException("부모 계정은 삭제할 수 없습니다. 회사 정보·라이선스의 마스터 계정입니다.");

        // 🔴 10/5 봉합1 P1-02 — 대표(is_parent=1)의 권한은 못 바꾼다. 직원 관리자가 대표를 User 로 내리면
        //   대표가 관리자 화면·API 에서 전부 403 이 되고 스스로 못 되돌린다(#38·#40). 같은 값으로 보내는 저장은 통과.
        if (current is not null && current.IsParent
            && !string.Equals(ParseUserRole(current.Role?.Replace("_", "")).ToString(), roleStr, StringComparison.Ordinal))
            throw new InvalidOperationException(ParentRoleGuardMessage);

        if (current is not null && !current.IsActive && dto.IsActive)
            await AccountSeatGuard.EnsureSeatAsync(_db, tx, tenantId, 1, _logger, ct).ConfigureAwait(false);

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
                // ⬛ cancellationToken: ct)).ConfigureAwait(false);   ← 20261005작3: 트랜잭션 안으로
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        // 🔴 10/5 [4] 2차 N-1① — 수정 화면·API 로 「사용 중 → 사용 안 함」(1→0)이 되면 [사용 안 함] 버튼과 같이 출입증을 끊는다.
        //   안 끊으면 PUT 한 번으로 끄고 다른 계정을 만드는 「한도 돌려쓰기」가 이 길로 그대로 된다(병렬이슈 01 재발 자리).
        if (current is not null && current.IsActive && !dto.IsActive)
            await CutAccessAsync(userId, tx, ct).ConfigureAwait(false);

        tx.Commit();

        // 감사로그 — 사용자 수정
        var afterJson = $"{{\"user_name\":\"{dto.UserName}\",\"role\":\"{roleStr}\",\"is_active\":{(dto.IsActive ? "true" : "false")}}}";
        await _audit.LogAsync("update", "user", userId, afterJson: afterJson, ct: ct);
    }

    // ⬛ DELETE 경로는 20261005작3 부터 RetireAsync(계정폐기 · 아이디 비움)다. 부르는 곳 0 — 지우지 않는다(#1).
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

        // 🔴 10/5 봉합1 P1-02 — 대표 대상은 거절. 응답에 임시 비번 원문이 실려 직원 관리자가 대표로 로그인할 수 있었다(#40).
        await RejectParentAsync(userId, tenantId, null, ParentResetGuardMessage, ct).ConfigureAwait(false);

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

        // 🔴 20261005작3 E-2 — 한도 사전 판정(F-5): 남은 자리 < 올린 행 수 ⇒ 한 줄도 안 넣는다.
        //    통과해도 행마다 CreateAsync 의 판정은 그대로 돈다(경쟁 대비).
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var seats = await AccountSeatGuard.GetSeatsAsync(_db, tenantId, ct).ConfigureAwait(false);
        if (seats.Limit - seats.Active < rows.Count)
        {
            result.SeatFull = true;
            result.Active = seats.Active;
            result.Limit = seats.Limit;
            result.FailedCount = rows.Count;
            result.Errors.Add(new BulkRowError
            {
                Row = 0,
                Reason = $"계정 사용 한도({seats.Limit}개) 때문에 한 줄도 등록하지 않았습니다. 남은 계정 {Math.Max(seats.Limit - seats.Active, 0)}개 · 올린 행 {rows.Count}개"
            });
            await _audit.LogAsync("create", "user_bulk", null,
                afterJson: $"{{\"total\":{result.TotalRows},\"success\":0,\"seat_full\":true}}", ct: ct);
            return result;
        }

        // 각 행 독립 처리 — 한 행 실패해도 나머지 진행
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            try
            {
                // 🔴 20261005작5 V5-06 ③ — 같은 이름의 재직·미등록 사원이 있으면 결과에 경고(막지 않는다 · 동명이인일 수 있다).
                //   CreateAsync 전에 본다 — 만든 뒤엔 새 사원도 계정이 있어 후보에서 빠진다.
                var sameName = string.IsNullOrWhiteSpace(row.EmpName) ? row.UserName : row.EmpName;
                if (!string.IsNullOrWhiteSpace(sameName))
                {
                    var candidates = await ListLinkableEmployeesAsync(tenantId, sameName.Trim(), false, ct).ConfigureAwait(false);
                    if (candidates.Count > 0)
                        result.SameNameWarnings.Add(new BulkSameNameWarning { Row = i + 1, Name = sameName.Trim(), Candidates = candidates });
                }

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

    // ══════════════════════════════════════════════════════════════
    // 20261005작3 — 계정 과금: 「사용 안 함」 · [다시 사용] · 「계정폐기」 · 카드 숫자 (설계 §1·§4)
    // ══════════════════════════════════════════════════════════════

    private sealed class SeatStateRow
    {
        public bool IsParent { get; set; }
        public bool IsActive { get; set; }
        // 10/5 봉합1 P1-02 — 대표 권한 변경 거절 판정용
        public string? Role { get; set; }
    }

    private const string ParentGuardMessage = "부모 계정은 삭제할 수 없습니다. 회사 정보·라이선스의 마스터 계정입니다.";

    // 10/5 봉합1 — 기존 「부모 계정은 …」 문구 틀 재사용(P1-02 · P2-07)
    public const string ParentRoleGuardMessage = "부모 계정은 권한을 바꿀 수 없습니다. 회사 정보·라이선스의 마스터 계정입니다.";
    public const string ParentResetGuardMessage = "부모 계정은 여기서 비밀번호를 초기화할 수 없습니다. 회사 정보·라이선스의 마스터 계정입니다.";
    public const string DeletedHoldsLoginIdMessage = "예전에 삭제한 계정이 이 아이디를 아직 쥐고 있습니다. 다른 아이디로 만들어 주세요.";

    public async Task<AccountSeatSnapshot> GetSeatsAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var snap = await AccountSeatGuard.GetSeatsAsync(_db, tenantId, ct).ConfigureAwait(false);
        if (snap.UsedFloor)
            _logger?.LogWarning("[AccountSeat] 기본 계정 수가 바닥값보다 작거나 없다 — {Floor} 으로 본다", AccountSeatGuard.DefaultBaseAccounts);
        return snap;
    }

    /// <summary>사용 중 → 사용 안 함. 판정 없음(자리가 줄 뿐). 대표는 거절(#38·#40).</summary>
    public async Task SuspendAsync(string userId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        // ⬛ [봉합1 전] await RejectParentAsync(userId, tenantId, null, ct) · UPDATE 는 트랜잭션 없이 — 출입증을 안 끊었다(P1-01)
        //   🔴 10/5 봉합1 P1-01 — 끄기와 출입증 끊기를 한 트랜잭션으로(반쪽 상태 없음).
        using var tx = _db.BeginTransaction();
        await RejectParentAsync(userId, tenantId, tx, ct).ConfigureAwait(false);

        var affected = await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE users SET is_active = 0, updated_at = NOW(6)
            WHERE user_id = @UserId AND tenant_id = @TenantId AND is_deleted = 0 AND is_parent = 0
            """,
            // ⬛ new { UserId = userId, TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
            new { UserId = userId, TenantId = tenantId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        if (affected == 0) throw new InvalidOperationException("사용자를 찾을 수 없습니다.");

        await CutAccessAsync(userId, tx, ct).ConfigureAwait(false);
        tx.Commit();

        await _audit.LogAsync("update", "user", userId, afterJson: "{\"action\":\"suspend\",\"is_active\":false}", ct: ct);
    }

    /// <summary>사용 안 함 → 사용 중([다시 사용]). <b>판정기 통과 후</b>에만(D-10 · G-A4).</summary>
    public async Task ResumeAsync(string userId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        using var tx = _db.BeginTransaction();

        // E-5 — 첫 문장 = 판정기(잠금 포함).
        // 🔴 10/5 봉합1 P2-05 — 첫 문장은 잠금 손잡이(행 없으면 이름 잠금).
        await using var seatLock = await AccountSeatGuard.AcquireAsync(_db, tx, tenantId, _logger, ct).ConfigureAwait(false);
        await AccountSeatGuard.EnsureSeatAsync(_db, tx, tenantId, 1, _logger, ct).ConfigureAwait(false);

        var affected = await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE users SET is_active = 1, updated_at = NOW(6)
            WHERE user_id = @UserId AND tenant_id = @TenantId AND is_deleted = 0 AND is_active = 0
            """,
            new { UserId = userId, TenantId = tenantId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        if (affected == 0) throw new InvalidOperationException("다시 사용할 계정을 찾을 수 없습니다.");

        tx.Commit();
        await _audit.LogAsync("update", "user", userId, afterJson: "{\"action\":\"resume\",\"is_active\":true}", ct: ct);
    }

    /// <summary>
    /// 계정폐기 — 퇴사 선례(<c>EmployeeService:709·741-753</c>) 그대로. 아이디 자리를 비워 같은 아이디 재등록이
    /// <c>uq_tenant_email</c> 에 안 걸린다(F-2). 사원 행은 남고 <c>employees.user_id</c> 만 끊는다(D-2). 되살림 없음.
    /// </summary>
    public async Task RetireAsync(string userId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        using var tx = _db.BeginTransaction();
        await RejectParentAsync(userId, tenantId, tx, ct).ConfigureAwait(false);

        // retired+(8) + GUID(36) + '+'(1) + 40 = 85 ≤ varchar(100)
        var affected = await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE users SET
                is_active = 0,
                is_deleted = 1,
                email = CONCAT('retired+', user_id, '+', LEFT(email, 40)),
                deleted_at = NOW(6),
                updated_at = NOW(6)
            WHERE user_id = @UserId AND tenant_id = @TenantId AND is_deleted = 0 AND is_parent = 0
            """,
            new { UserId = userId, TenantId = tenantId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        if (affected == 0) throw new InvalidOperationException("사용자를 찾을 수 없습니다.");

        // ⬛ [20261005작5 전] "UPDATE employees SET user_id = NULL, updated_at = NOW(6) WHERE tenant_id = @TenantId AND user_id = @UserId"
        //   사원계정 칸(login_id · DB-137)도 같은 UPDATE 에서 비운다(설계 §2-2).
        await _db.ExecuteAsync(new CommandDefinition(
            "UPDATE employees SET user_id = NULL, login_id = NULL, updated_at = NOW(6) WHERE tenant_id = @TenantId AND user_id = @UserId",
            new { UserId = userId, TenantId = tenantId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        // 🔴 10/5 봉합1 P1-01 — 폐기한 계정의 출입증도 그 자리에서 끊는다.
        await CutAccessAsync(userId, tx, ct).ConfigureAwait(false);

        tx.Commit();
        await _audit.LogAsync("delete", "user", userId, afterJson: "{\"action\":\"retire\"}", ct: ct);
    }

    /// <summary>
    /// 🔴 10/5 봉합1 P1-01 — 그 계정의 출입증을 끊는다. 로그아웃 선례(<c>AuthController</c> 절C·K)의 순서 그대로
    /// ① refresh 폐기 ② 세션 행 삭제 — 범위만 「그 로그인 하나」가 아니라 「그 계정 전부」다.
    /// </summary>
    /// <remarks>
    /// <para>끊기는 자리 = <c>SessionValidityMiddleware</c>: 접근 토큰의 <c>sid</c> 로 <c>user_sessions</c> 행을 찾고 없으면 401
    /// (생존 캐시 10초 뒤). 갱신은 <c>is_revoked=1</c> 로 막힌다.</para>
    /// <para>부르는 쪽이 <c>user_id</c> 가 그 회사 것임을 먼저 확인했다(tenant 조건 UPDATE 의 affected) — <c>refresh_tokens</c> 에는 tenant 칸이 없다.</para>
    /// </remarks>
    private async Task CutAccessAsync(string userId, IDbTransaction tx, CancellationToken ct)
    {
        await _db.ExecuteAsync(new CommandDefinition(
            "UPDATE refresh_tokens SET is_revoked = 1 WHERE user_id = @UserId AND is_revoked = 0",
            new { UserId = userId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        await _db.ExecuteAsync(new CommandDefinition(
            "DELETE FROM user_sessions WHERE user_id = @UserId",
            new { UserId = userId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
    }

    private Task RejectParentAsync(string userId, string tenantId, IDbTransaction? tx, CancellationToken ct) =>
        RejectParentAsync(userId, tenantId, tx, ParentGuardMessage, ct);

    // 10/5 봉합1 P1-02 — 같은 판정에 문구만 다르게(비번 초기화 거절)
    private async Task RejectParentAsync(string userId, string tenantId, IDbTransaction? tx, string message, CancellationToken ct)
    {
        // TINYINT(1) 은 Boolean 으로 온다 — int 로 받지 않는다
        var isParent = await _db.QueryFirstOrDefaultAsync<bool?>(new CommandDefinition(
            "SELECT is_parent FROM users WHERE user_id = @UserId AND tenant_id = @TenantId",
            new { UserId = userId, TenantId = tenantId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        if (isParent == true) throw new InvalidOperationException(message);
    }

    // ══════════════════════════════════════════════════════════════
    // 20261005작5 — 사원 ↔ 계정 연결 (설계 §3·§4·§5-3 · [3-V] P1-01·P3-08·P3-09·P3-10 · V5-06)
    // ══════════════════════════════════════════════════════════════

    public const string LoginIdTakenMessage = "이미 사용 중인 아이디입니다.";
    public const string LoginIdRuleMessage = "아이디는 공백 없이 4자 이상이어야 합니다.";

    // 퇴사 판별(C-1 · P3-11) — EmployeeService.GetListAsync 의 IsLeaver 와 같은 식(바꾸면 둘 다).
    private const string LeaverPredicate =
        "(e.is_active = 0 OR e.is_resigned = 1 OR (e.resign_date IS NOT NULL AND e.resign_date <= NOW(6)))";

    private sealed class LinkTargetRow
    {
        public string EmployeeId { get; set; } = "";
        public string? UserId { get; set; }
        public bool IsLeaver { get; set; }
        public string EmpName { get; set; } = "";
        public string? Position { get; set; }
        public string? Department { get; set; }
        public string? Phone { get; set; }
        public string? Role { get; set; }
        public DateTime? JoinDate { get; set; }
    }

    /// <summary>
    /// 기존 사원에게 계정을 만든다(사원 행 INSERT 0 · 쌍둥이 차단). 사원관리 [계정 만들기](§3)와 직원계정 「기존 사원 고르기」(§4)가 같은 함수.
    /// </summary>
    /// <remarks>
    /// <para>🔴 잠금 순서 고정(P3-10 · 1213 교착 방지): ① 좌석 잠금(<see cref="AccountSeatGuard.AcquireAsync"/> · 첫 문장) →
    /// ② 사원 행 <c>FOR UPDATE</c> → ③ users 는 <b>잠금 읽기 금지</b>(그냥 SELECT) · INSERT 만.
    /// 계정폐기(<see cref="RetireAsync"/>)는 users → 사원 순이지만 이 함수가 users 행을 잠그지 않으므로 고리가 안 생긴다.</para>
    /// <para>🔴 <paramref name="actorIsAdmin"/> 이 아니면(2단계 직원): 만드는 계정은 일반(User) 고정(§5-3 · P-4) ·
    /// 대상 사원 직무가 일반이 아니면 409(P1-01 — 출입증 role 은 사원 role 이 먼저다 · <c>AuthService</c> CreateLoginResponse).</para>
    /// </remarks>
    public async Task<string> CreateForEmployeeAsync(CreateForEmployeeDto dto, string tenantId, bool actorIsAdmin, CancellationToken ct = default)
    {
        ValidatePassword(dto.Password);
        ValidateLoginId(dto.LoginId);
        var loginId = dto.LoginId.Trim();
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var dup = await _db.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM users WHERE tenant_id = @TenantId AND email = @LoginId AND is_deleted = 0",
            new { TenantId = tenantId, LoginId = loginId }, cancellationToken: ct)).ConfigureAwait(false);
        if (dup > 0)
            throw new AccountLinkConflictException("login_id_taken", LoginIdTakenMessage);

        var heldByDeleted = await _db.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM users WHERE tenant_id = @TenantId AND email = @LoginId AND is_deleted = 1",
            new { TenantId = tenantId, LoginId = loginId }, cancellationToken: ct)).ConfigureAwait(false);
        if (heldByDeleted > 0)
            throw new AccountLinkConflictException("login_id_held_by_deleted", DeletedHoldsLoginIdMessage);

        using var tx = _db.BeginTransaction();

        // ① 첫 문장 = 좌석 잠금 + 한도 판정(A · 작3 규칙 그대로 · 한도 초과는 AccountSeatFullException → 409 account_seat_full)
        await using var seatLock = await AccountSeatGuard.AcquireAsync(_db, tx, tenantId, _logger, ct).ConfigureAwait(false);
        await AccountSeatGuard.EnsureSeatAsync(_db, tx, tenantId, 1, _logger, ct).ConfigureAwait(false);

        // ② 사원 행 잠금
        var emp = await _db.QueryFirstOrDefaultAsync<LinkTargetRow>(new CommandDefinition(
            $"""
            SELECT e.employee_id AS EmployeeId, e.user_id AS UserId,
                   CASE WHEN {LeaverPredicate} THEN 1 ELSE 0 END AS IsLeaver,
                   e.emp_name AS EmpName, e.position AS Position, e.department AS Department,
                   e.phone AS Phone, e.role AS Role, e.join_date AS JoinDate
            FROM employees e
            WHERE e.tenant_id = @TenantId AND e.employee_id = @EmployeeId
            FOR UPDATE
            """,
            new { TenantId = tenantId, dto.EmployeeId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        if (emp is null) throw new EmployeeNotFoundForAccountException();

        if (emp.IsLeaver)
            throw new AccountLinkConflictException("employee_leaver", "퇴사한 사원은 계정을 만들 수 없습니다.");

        if (!actorIsAdmin && !IsGeneralEmployeeRole(emp.Role))
            throw new AccountLinkConflictException("employee_role_not_general",
                "이 사원은 일반 직무가 아니라서 계정을 만들 수 없습니다. 대표님께 요청하세요.");

        // ③ 이미 계정이 있나 — users 는 잠금 읽기 금지(P3-10). 사용중지(is_active=0)도 「있음」(F-2).
        string? deadId = null;
        if (!string.IsNullOrEmpty(emp.UserId))
        {
            var linkedDeleted = await _db.QueryFirstOrDefaultAsync<bool?>(new CommandDefinition(
                "SELECT is_deleted FROM users WHERE user_id = @UserId AND tenant_id = @TenantId",
                new { emp.UserId, TenantId = tenantId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            if (linkedDeleted == false)
                throw new AccountLinkConflictException("employee_has_account",
                    "이미 계정이 있는 사원입니다. 직원 계정 관리에서 확인하세요.");
            deadId = emp.UserId; // 죽은 연결(C-4) — 덮어쓴다
        }

        var role = actorIsAdmin ? ParseUserRole(dto.Role) : UserRole.User;
        var roleStr = role.ToString();
        var accountType = role == UserRole.TenantAdmin ? "tenant_admin" : "tenant_user";
        var userId = Guid.NewGuid().ToString();
        var userName = string.IsNullOrWhiteSpace(dto.UserName) ? emp.EmpName : dto.UserName.Trim();

        try
        {
            await _db.ExecuteAsync(new CommandDefinition(
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
                    @HireDate, NULL,
                    1, 0,
                    NOW(6), NOW(6))
                """,
                new
                {
                    UserId = userId,
                    TenantId = tenantId,
                    Email = loginId,
                    Hash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                    UserName = userName,
                    EmpName = emp.EmpName,
                    emp.Department,
                    emp.Position,
                    emp.Phone,
                    Role = roleStr,
                    AccountType = accountType,
                    HireDate = emp.JoinDate
                },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            // ④ 조건부 연결 — 두 창 경쟁이면 0행(C-3). UNIQUE(tenant_id,user_id) 가 둘째 장치.
            var linked = await _db.ExecuteAsync(new CommandDefinition(
                """
                UPDATE employees
                SET user_id = @UserId, login_id = @LoginId, updated_at = NOW(6)
                WHERE tenant_id = @TenantId
                  AND employee_id = @EmployeeId
                  AND (user_id IS NULL OR user_id = '' OR user_id = @DeadId)
                """,
                new { UserId = userId, LoginId = loginId, TenantId = tenantId, dto.EmployeeId, DeadId = deadId ?? "" },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            if (linked == 0)
                throw new AccountLinkConflictException("employee_link_race",
                    "다른 화면에서 이 사원에 먼저 계정을 연결했습니다. 목록을 새로 보고 확인하세요.");
        }
        catch (MySqlConnector.MySqlException ex) when (ex.ErrorCode == MySqlConnector.MySqlErrorCode.DuplicateKeyEntry)
        {
            _logger?.LogWarning(ex, "[CreateForEmployee] 중복 키 — 아이디 또는 사원 연결 경쟁");
            if (ex.Message.Contains("uq_tenant_email", StringComparison.OrdinalIgnoreCase))
                throw new AccountLinkConflictException("login_id_taken", LoginIdTakenMessage);
            throw new AccountLinkConflictException("employee_link_race",
                "다른 화면에서 이 사원에 먼저 계정을 연결했습니다. 목록을 새로 보고 확인하세요.");
        }

        tx.Commit();

        var afterJson = $"{{\"email\":\"{loginId}\",\"user_name\":\"{userName}\",\"role\":\"{roleStr}\",\"account_type\":\"{accountType}\",\"employee_id\":\"{dto.EmployeeId}\"}}";
        await _audit.LogAsync("create", "user", userId, afterJson: afterJson, ct: ct);
        return userId;
    }

    /// <summary>
    /// 계정을 만들 수 있는 사원 — 재직(C-1 아님) + 계정 없음(연결 없음 또는 죽은 연결). <paramref name="name"/> 이 있으면 그 이름만(같은 이름 확인 V5-06).
    /// <paramref name="onlyGeneralRoles"/> 면 일반 직무만(2단계 직원용 · P1-01).
    /// </summary>
    public async Task<List<LinkableEmployeeDto>> ListLinkableEmployeesAsync(string tenantId, string? name, bool onlyGeneralRoles, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var rows = await _db.QueryAsync<LinkableEmployeeDto>(new CommandDefinition(
            $"""
            SELECT e.employee_id AS EmployeeId, e.emp_no AS EmpNo, e.emp_name AS EmpName,
                   d.dept_name AS DeptName, e.position AS Position, e.role AS Role
            FROM employees e
            LEFT JOIN departments d ON d.dept_id = e.dept_id AND d.tenant_id = e.tenant_id
            LEFT JOIN users ua ON ua.user_id = e.user_id AND ua.tenant_id = e.tenant_id
            WHERE e.tenant_id = @TenantId
              AND NOT {LeaverPredicate}
              AND (ua.user_id IS NULL OR ua.is_deleted = 1)
              AND (@Name IS NULL OR e.emp_name = @Name)
            ORDER BY e.emp_no
            """,
            new { TenantId = tenantId, Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim() },
            cancellationToken: ct)).ConfigureAwait(false);
        var list = rows.ToList();
        return onlyGeneralRoles ? list.Where(r => IsGeneralEmployeeRole(r.Role)).ToList() : list;
    }

    /// <summary>2단계 직원의 [사용 안 함] — 대상이 일반 계정이고 단계 0 일 때만(§5-3 · P3-08).</summary>
    public async Task SuspendAsStaffAsync(string userId, string actorUserId, string tenantId, CancellationToken ct = default)
    {
        await EnsureStaffMayTouchAsync(userId, actorUserId, tenantId, ct).ConfigureAwait(false);
        await SuspendAsync(userId, tenantId, ct).ConfigureAwait(false);
    }

    /// <summary>2단계 직원의 [다시 사용] — 대상이 일반 계정이고 단계 0 일 때만(§5-3 · P3-08).</summary>
    public async Task ResumeAsStaffAsync(string userId, string actorUserId, string tenantId, CancellationToken ct = default)
    {
        await EnsureStaffMayTouchAsync(userId, actorUserId, tenantId, ct).ConfigureAwait(false);
        await ResumeAsync(userId, tenantId, ct).ConfigureAwait(false);
    }

    private sealed class StaffTargetRow
    {
        public bool IsParent { get; set; }
        public string? AccountType { get; set; }
    }

    // ⚠️ 판정과 바꾸기가 한 트랜잭션이 아니다 — 그 사이 대표가 대상에게 권한을 주면 한 번 빠질 수 있다(권한 부여는 대표·관리자만 · 개발명세서 「남은 위험」).
    private async Task EnsureStaffMayTouchAsync(string userId, string actorUserId, string tenantId, CancellationToken ct)
    {
        if (string.Equals(userId, actorUserId, StringComparison.Ordinal))
            throw new AccountActionForbiddenException("self_account", "본인 계정은 여기서 바꿀 수 없습니다.");

        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var target = await _db.QueryFirstOrDefaultAsync<StaffTargetRow>(new CommandDefinition(
            "SELECT is_parent AS IsParent, account_type AS AccountType FROM users WHERE user_id = @UserId AND tenant_id = @TenantId AND is_deleted = 0",
            new { UserId = userId, TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
        if (target is null) throw new InvalidOperationException("사용자를 찾을 수 없습니다.");

        if (target.IsParent || string.Equals(target.AccountType, "tenant_admin", StringComparison.OrdinalIgnoreCase))
            throw new AccountActionForbiddenException("protected_account", "대표·관리자 계정은 대표님만 바꿀 수 있습니다.");

        var hasLevel = await _db.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM user_permissions
            WHERE user_id = @UserId AND tenant_id = @TenantId
              AND menu_code IN ('USERS', 'USERS_ACCOUNT', 'USERS_SEAT')
              AND can_view = 1
            """,
            new { UserId = userId, TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
        if (hasLevel > 0)
            throw new AccountActionForbiddenException("target_has_users_level",
                "직원 계정 관리 권한을 가진 직원의 계정은 대표님만 바꿀 수 있습니다.");
    }

    /// <summary>
    /// 일반(직원) 직무인가 — 2단계 직원이 계정을 만들어 줄 수 있는 사원(P1-01). 정규화 뒤 판정(P3-07 「1」→TenantAdmin).
    /// 빈 값 · User · Readonly · <c>*_user</c>(admin 글자 없는 것)만 일반. 관리자·매니저·hr 등은 아니다.
    /// </summary>
    public static bool IsGeneralEmployeeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return true;
        var r = role.Trim();
        if (int.TryParse(r, out var n))
            return n == (int)UserRole.User || n == (int)UserRole.Readonly;
        if (Enum.TryParse<UserRole>(r, ignoreCase: true, out var parsed))
            return parsed is UserRole.User or UserRole.Readonly;
        var lower = r.ToLowerInvariant();
        return lower.EndsWith("_user", StringComparison.Ordinal) && !lower.Contains("admin", StringComparison.Ordinal);
    }

    /// <summary>아이디 규칙 — 부트스트랩(<c>CompanyBootstrapProvisioner</c> :205)과 같다: 공백 없이 4자 이상(P3-09). 100자 = users.email 칸.</summary>
    public static void ValidateLoginId(string? loginId)
    {
        var v = loginId?.Trim() ?? "";
        if (v.Length < 4 || v.Length > 100 || v.Any(char.IsWhiteSpace))
            throw new InvalidOperationException(LoginIdRuleMessage);
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
