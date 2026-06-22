using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using HitPan.Application.Common;
using HitPan.Application.DTOs.Auth;
using HitPan.Application.Interfaces;
using HitPan.Domain.Entities;
using Microsoft.IdentityModel.Tokens;

namespace HitPan.Application.Services;

public class AuthService : IAuthService
{
    private const string InvalidCredentialMessage = "이메일 또는 비밀번호가 틀립니다";
    // 사장님 결재 (2026-06-19): ERP는 고객사 직원이 하루 종일 쓰는 업무 도구.
    //   AccessToken 15분은 너무 짧아 근무 중 잦은 만료로 끊김(헌법 #19·#27 위반) → 근무 하루 8시간으로 상향.
    //   추가 안전망: 만료돼도 HitPanApiAuthHandler 가 401 시 RefreshToken 으로 자동 재발급+재시도(사용자 무자각).
    //   RefreshToken 7일 유지 → 다음 날 출근 시 재로그인. 보안·편의 균형(ERP 표준).
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(8);
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthUserLookup _authUserLookup;

    public AuthService(IUnitOfWork unitOfWork, IAuthUserLookup authUserLookup)
    {
        _unitOfWork = unitOfWork;
        _authUserLookup = authUserLookup;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await _authUserLookup.FindUserByEmailAsync(request.Email, ct);

        if (user is null)
        {
            throw new UnauthorizedAccessException(InvalidCredentialMessage);
        }

        // 계정 잠금 확인
        if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow)
        {
            var remaining = (int)(user.LockoutEnd.Value - DateTime.UtcNow).TotalMinutes + 1;
            throw new UnauthorizedAccessException($"계정이 잠겼습니다. {remaining}분 후 다시 시도해주세요.");
        }

        var passwordValid = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);
        if (!passwordValid)
        {
            // 로그인 실패 횟수 증가
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= 5)
            {
                user.LockoutEnd = DateTime.UtcNow.AddMinutes(15);
                await _unitOfWork.SaveChangesAsync(ct);
                throw new UnauthorizedAccessException("로그인 5회 실패로 계정이 15분간 잠겼습니다.");
            }

            await _unitOfWork.SaveChangesAsync(ct);
            throw new UnauthorizedAccessException($"이메일 또는 비밀번호가 올바르지 않습니다. (실패 {user.FailedLoginCount}/5)");
        }

        if (!user.IsActive)
        {
            throw new UnauthorizedAccessException("비활성화된 계정입니다");
        }

        // 로그인 성공 시 실패 카운트 초기화
        user.FailedLoginCount = 0;
        user.LockoutEnd = null;

        var employeeRepo = _unitOfWork.Repository<Employee>();
        var employees = await employeeRepo.FindAsync(x => x.UserId == user.Id && x.IsActive);
        var employee = employees.FirstOrDefault();

        // 봉합 (2026-06-22, 13차 2단 교차검증 A안 — 기존 부모계정 employees 백필): 7차 A-P0-1 은
        //   신규 가입(CompanyBootstrap)만 부모 employees 행을 만들었고, 그 전에 생성된 부모계정은 백필이
        //   없어 employee=null → 토큰 employee_id 빈 문자열 → 결재·경비·HR(13차) 전부 403. 부모계정인데
        //   연결 employees 행이 없으면 로그인 시 멱등 생성해 보장한다(라이브 DB 직접 변경 0, 런타임 자가치유).
        if (employee is null && user.AccountType == "tenant_admin")
        {
            employee = await BackfillParentEmployeeAsync(user, ct);
        }

        var redirectToWelcome = user.LastLoginAt is null;
        user.LastLoginAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        // 봉합 2026-06-17 1.2.12 — TenantConfigReader 정합 (1.2.6 환경변수 폐기 결재)
        var secret = TenantConfigReader.GetRequired("JWT_SECRET");

        var response = CreateLoginResponse(user, employee, secret, redirectToWelcome);

        // refresh token DB 저장 — 로그아웃 is_revoked=1 차단의 기준
        var db = _unitOfWork.GetDbConnection();
        // 이전 토큰 정리 후 새 토큰 INSERT
        await db.ExecuteAsync(
            "DELETE FROM refresh_tokens WHERE user_id = @UserId",
            new { UserId = user.Id });
        await db.ExecuteAsync(
            @"INSERT INTO refresh_tokens (token_id, user_id, token_hash, expires_at, is_revoked)
              VALUES (@TokenId, @UserId, @TokenHash, @ExpiresAt, 0)",
            new
            {
                TokenId = Guid.NewGuid().ToString(),
                UserId = user.Id,
                TokenHash = HashToken(response.RefreshToken),
                ExpiresAt = DateTime.UtcNow.Add(RefreshTokenLifetime)
            });

        return response;
    }

    public async Task<LoginResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken ct = default)
    {
        // 봉합 2026-06-17 1.2.12 — TenantConfigReader 정합
        var secret = TenantConfigReader.GetRequired("JWT_SECRET");

        var tokenHandler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

        ClaimsPrincipal principal;
        try
        {
            // 보안 강화: refresh 토큰도 issuer/audience 검증
            var refreshIssuer = TenantConfigReader.Get("JWT_ISSUER") ?? "hitpan-erp";
            var refreshAudience = TenantConfigReader.Get("JWT_AUDIENCE") ?? "hitpan-client";

            principal = tokenHandler.ValidateToken(
                request.RefreshToken,
                new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = refreshIssuer,
                    ValidateAudience = true,
                    ValidAudience = refreshAudience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                },
                out _);
        }
        catch (Exception)
        {
            throw new UnauthorizedAccessException("유효하지 않은 토큰입니다");
        }

        var tokenType = principal.FindFirst("token_type")?.Value;
        if (!string.Equals(tokenType, "refresh", StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("유효하지 않은 토큰입니다");
        }

        var userId = principal.FindFirst("user_id")?.Value;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedAccessException("유효하지 않은 토큰입니다");
        }

        var user = await _authUserLookup.FindUserByIdAsync(userId, ct);
        if (user is null)
        {
            throw new UnauthorizedAccessException("유효하지 않은 토큰입니다");
        }

        // 로그아웃된 토큰 차단 — DB에 해당 토큰이 없거나 is_revoked=1이면 차단
        var conn = _unitOfWork.GetDbConnection();
        var tokenRecord = await conn.ExecuteScalarAsync<int?>(
            "SELECT is_revoked FROM refresh_tokens WHERE user_id = @UserId AND token_hash = @TokenHash",
            new { UserId = userId, TokenHash = HashToken(request.RefreshToken) });
        if (tokenRecord is null || tokenRecord.Value == 1)
        {
            throw new UnauthorizedAccessException("로그아웃된 토큰입니다. 다시 로그인해주세요.");
        }

        var employeeRepo = _unitOfWork.Repository<Employee>();
        var employees = await employeeRepo.FindAsync(x => x.UserId == user.Id && x.IsActive);
        var employee = employees.FirstOrDefault();

        // 백필 (2026-06-22, 13차 A안): refresh 경로도 동일 — 부모계정 employees 행 멱등 보장.
        if (employee is null && user.AccountType == "tenant_admin")
        {
            employee = await BackfillParentEmployeeAsync(user, ct);
        }

        var response = CreateLoginResponse(user, employee, secret, redirectToWelcome: false);

        // 진범 봉합 (2026-06-20, 2차 전수조사 AUTH-01 P0 → 3차 전수조사 F1/F2 강화):
        //   ① [2차 P0] 종전엔 새 RefreshToken 을 발급해 클라이언트에만 주고 테이블을 갱신하지 않아, 회전 토큰의
        //      hash 가 DB 에 없어 다음 401 검증(160-168)에서 차단 → 자동 재발급이 1회 후 영구 실패했다.
        //   ② [3차 F2 보안] 동시 refresh 단일사용 미강제: 같은 토큰으로 R1·R2 가 동시 도착하면 둘 다 통과해
        //      구 토큰이 살아있는 자식 2개로 회전(탈취 토큰 replay 공존). → DELETE 의 affected rows 로 단일사용
        //      강제: 토큰을 실제로 지운(=소비 권리를 획득한) 요청만 새 토큰을 발급하고, 0행이면 거부.
        //   ③ [3차 F1 무결성] DELETE→INSERT 비원자: 중간 예외 시 토큰 둘 다 소실 → 강제 재로그인.
        //      → 단일 트랜잭션으로 묶어 원자화. 실패 시 롤백되어 구 토큰이 보존된다.
        //   ④ [3차 F4 위생] 만료된 잔여 행을 같은 트랜잭션에서 정리(방치 세션 누적 방지).
        //   user_id 전체가 아니라 사용한 token_hash 만 회전한다(F5 정정: LoginAsync 가 새 로그인 시 user_id 의
        //   모든 토큰을 일괄 삭제하므로 현 정책은 사실상 '단일 활성 세션'이다 — 회전이 토큰별인 것은 동시
        //   401 경쟁에서 사용한 토큰만 정확히 소비하기 위함이지 멀티기기 동시 세션을 의도한 것은 아니다).
        //   봉합 (2026-06-20, 3차 전수조사 후속): EF/Dapper 가 커넥션을 암묵적으로 닫아둔 상태면
        //   BeginTransaction 이 "open and available Connection" 예외로 터진다. 명시적으로 먼저 연다.
        if (conn.State != System.Data.ConnectionState.Open)
        {
            if (conn is System.Data.Common.DbConnection dbConn)
            {
                await dbConn.OpenAsync(ct).ConfigureAwait(false);
            }
            else
            {
                conn.Open();
            }
        }
        using (var tx = conn.BeginTransaction())
        {
            try
            {
                var deleted = await conn.ExecuteAsync(
                    "DELETE FROM refresh_tokens WHERE user_id = @UserId AND token_hash = @OldHash",
                    new { UserId = userId, OldHash = HashToken(request.RefreshToken) }, tx);
                if (deleted == 0)
                {
                    // 다른 동시 요청이 이미 이 토큰을 소비함(또는 폐기됨) → 단일사용 강제로 거부.
                    tx.Rollback();
                    throw new UnauthorizedAccessException("이미 사용된 토큰입니다. 다시 로그인해주세요.");
                }

                // 만료 잔여 행 정리(F4) — 같은 user 의 기한 지난 토큰만.
                await conn.ExecuteAsync(
                    "DELETE FROM refresh_tokens WHERE user_id = @UserId AND expires_at < @Now",
                    new { UserId = userId, Now = DateTime.UtcNow }, tx);

                await conn.ExecuteAsync(
                    @"INSERT INTO refresh_tokens (token_id, user_id, token_hash, expires_at, is_revoked)
                      VALUES (@TokenId, @UserId, @TokenHash, @ExpiresAt, 0)",
                    new
                    {
                        TokenId = Guid.NewGuid().ToString(),
                        UserId = userId,
                        TokenHash = HashToken(response.RefreshToken),
                        ExpiresAt = DateTime.UtcNow.Add(RefreshTokenLifetime)
                    }, tx);

                tx.Commit();
            }
            catch (UnauthorizedAccessException)
            {
                throw; // 단일사용 거부는 이미 롤백됨 — 그대로 전파.
            }
            catch (Exception)
            {
                try { tx.Rollback(); } catch (Exception rbex) { Console.Error.WriteLine($"[AuthService] refresh 회전 롤백 실패: {rbex.Message}"); }
                throw;
            }
        }

        return response;
    }

    /// <summary>
    /// 부모계정(tenant_admin)인데 연결된 employees 행이 없으면 멱등 생성한다(13차 A안 백필).
    /// 7차 A-P0-1(CompanyBootstrap)의 부모 employees 행 생성 패턴을 기존 계정에도 적용 — 결재·경비·HR
    /// 의 employee_id 체계가 빈 문자열로 깨지는 것을 런타임에 자가치유한다. 동시 로그인 경합 시 재조회로 안전.
    /// </summary>
    private async Task<Employee?> BackfillParentEmployeeAsync(User user, CancellationToken ct)
    {
        var employeeRepo = _unitOfWork.Repository<Employee>();

        // emp_no 충돌 방지: 같은 tenant 의 기존 사원 수 +1 (없으면 0001). 단일 회사라 충돌 가능성 희박.
        var existing = await employeeRepo.FindAsync(x => x.TenantId == user.TenantId);
        var empNo = (existing.Count() + 1).ToString("D4");

        // EmployeeId 는 EF 매핑상 Ignore(실 PK 는 BaseEntity.Id) — 설정 안 함. employee_id 클레임은 .Id 사용.
        var newEmployee = new Employee
        {
            TenantId = user.TenantId,
            UserId = user.Id,
            EmpNo = empNo,
            EmpName = user.UserName,
            EmpType = HitPan.Domain.Enums.EmployeeType.Regular,
            JoinDate = DateTime.UtcNow,
            IsActive = true,
            Role = "tenant_admin",
            Email = user.Email,
        };

        try
        {
            await employeeRepo.AddAsync(newEmployee);
            await _unitOfWork.SaveChangesAsync(ct);
            return newEmployee;
        }
        catch
        {
            // 동시 로그인 경합(또는 UNIQUE 충돌)으로 이미 생성됐을 수 있다 — 재조회해서 있으면 그걸 쓴다.
            // (AuthService 는 ILogger 미주입 — 생성자 변경 회피. 경합은 재조회로 정상 흡수되므로 silent 아님.)
            var retry = await employeeRepo.FindAsync(x => x.UserId == user.Id && x.IsActive);
            return retry.FirstOrDefault();
        }
    }

    private static LoginResponse CreateLoginResponse(User user, Employee? employee, string secret, bool redirectToWelcome)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;
        var accessExpiresAt = now.Add(AccessTokenLifetime);

        var employeeRole = string.IsNullOrWhiteSpace(employee?.Role)
            ? MapLegacyRole(user.Role.ToString())
            : employee.Role;
        var employeeId = employee?.Id ?? string.Empty;

        var accessClaims = new List<Claim>
        {
            new("tenant_id", user.TenantId),
            new("user_id", user.Id),
            new("name", user.UserName),
            new("account_type", user.AccountType ?? "tenant_user"),
            // 보안 격벽 (사장님 결재 2026-06-18): platform_id·reseller_id 클레임 제거.
            //   본사·대리점 계층은 백오피스 전용 — ERP 토큰에 본사 식별자를 굽지 않아
            //   고객사 PC가 뚫려도 본사·타 고객사 정보가 노출되지 않게 함(헌법 #7·#22·#35).
            new("employee_id", employeeId),
            new(ClaimTypes.Role, employeeRole),
            new("role", employeeRole)
        };

        // Issuer/Audience — 토큰 스푸핑 방지 (ValidIssuer/ValidAudience와 일치해야 검증 통과)
        var issuer = TenantConfigReader.Get("JWT_ISSUER") ?? "hitpan-erp";
        var audience = TenantConfigReader.Get("JWT_AUDIENCE") ?? "hitpan-client";

        var accessTokenDescriptor = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: accessClaims,
            expires: accessExpiresAt,
            signingCredentials: credentials);
        var accessToken = new JwtSecurityTokenHandler().WriteToken(accessTokenDescriptor);

        var refreshClaims = new List<Claim>
        {
            new("user_id", user.Id),
            new("token_type", "refresh")
        };

        var refreshTokenDescriptor = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: refreshClaims,
            expires: now.Add(RefreshTokenLifetime),
            signingCredentials: credentials);
        var refreshToken = new JwtSecurityTokenHandler().WriteToken(refreshTokenDescriptor);

        return new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = accessExpiresAt,
            TenantId = user.TenantId,
            UserName = user.UserName,
            Role = employeeRole,
            RedirectToWelcome = redirectToWelcome
        };
    }

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string MapLegacyRole(string role)
    {
        return role switch
        {
            "TenantAdmin" => "system_admin",
            _ => role
        };
    }

    /// <summary>
    /// Step-up 인증 (WO-20260430-9): 현재 로그인한 사용자의 비밀번호 재검증.
    /// 사용자 정보 수정 등 민감 작업 진입 전 안전장치.
    /// </summary>
    public async Task<bool> VerifyOwnPasswordAsync(string userId, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        var user = await _authUserLookup.FindUserByIdAsync(userId, ct);
        if (user is null || !user.IsActive)
        {
            return false;
        }

        return BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
    }
}
