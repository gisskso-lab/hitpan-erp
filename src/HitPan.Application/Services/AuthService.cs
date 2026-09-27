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
        // W2-4 (작업지시서 20260707작2, 헌법 #40 백로그): 아이디 앞뒤 공백 서버측 방어깊이.
        //   설치마법사 create-parent 는 Trim 검증하는데 로그인은 raw 비교라, 복사·붙여넣기로 공백이
        //   딸려오면 "계정은 있는데 로그인 실패"가 난다. 비밀번호는 공백도 유효 문자므로 손대지 않는다.
        var loginId = request.Email.Trim();
        var user = await _authUserLookup.FindUserByEmailAsync(loginId, ct);

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

        // W1-3 (작업지시서 20260707작2): 로그인은 익명 경로라 TenantMiddleware 스킵 → CurrentTenant=''
        //   → Repository 전역 테넌트필터가 tenant_id='' 로 걸려 이 조회가 항상 0건이었다(계정 정상인데
        //   매 로그인 백필 헛INSERT 1062 + employee_id claim 공백 = 결재·경비·HR 침묵 고장의 진범).
        //   IgnoreQueryFilters + user 행에서 얻은 TenantId 명시 한정으로 교체(헌법 #2 테넌트 격리 유지).
        var employee = await _authUserLookup.FindActiveEmployeeByUserAsync(user.Id, user.TenantId, ct);

        // 봉합 (2026-06-22, 13차 2단 교차검증 A안 — 기존 부모계정 employees 백필): 7차 A-P0-1 은
        //   신규 가입(CompanyBootstrap)만 부모 employees 행을 만들었고, 그 전에 생성된 부모계정은 백필이
        //   없어 employee=null → 토큰 employee_id 빈 문자열 → 결재·경비·HR(13차) 전부 403. 부모계정인데
        //   연결 employees 행이 없으면 로그인 시 멱등 생성해 보장한다(라이브 DB 직접 변경 0, 런타임 자가치유).
        // 🔴 봉합 (2026-08-14, 1.2.74 실사용 P0): 자식계정도 백필한다.
        //    사장님: "자식계정은 생성되었으나 ... 다른 그 어떤메뉴에도 그 계정직원은 안나옴."
        //    사번 채번 충돌로 employees INSERT 가 실패해도 users 는 커밋돼 **고아 계정**이 남았는데,
        //    이 백필이 tenant_admin 전용이라 **자식은 영원히 자가치유가 안 됐다** —
        //    재등록은 이메일 중복으로 막히고, 사원관리에서 넣으면 연결 안 된 별개 행이 생겨
        //    DB 직접 수술 외엔 길이 없었다.
        //    ⇒ 이미 만들어진 고아 계정도 **다음 로그인 한 번으로 스스로 복구된다.**
        //    (채번·트랜잭션은 UserService.CreateAsync 에서 봉합했으므로 새 고아는 더 안 생긴다.)
        if (employee is null)
        {
            // 백필은 보조 자가치유 — 실패(비-1062 예외: 연결 끊김 등)해도 로그인 자체는 막지 않는다.
            // employee=null 이면 이번 세션은 employee_id 없이 진행하고, 다음 로그인에 재시도된다(13차 거짓봉합 재봉합).
            try { employee = await BackfillParentEmployeeAsync(user, ct); }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"[Backfill] 부모계정 employees 백필 실패(로그인은 진행): {ex.Message}"); }
        }

        // ── 축 B: 같은 계정의 PC 동시 로그인 차단 (DB-127 · 20260927작1 절D) ──────────────
        //
        //   🔴 여기서 막는 이유 — **상태를 바꾸기 전**이다.
        //     아래 LastLoginAt 갱신·토큰 발급이 끝난 뒤에 던지면, 거절당한 로그인이
        //     "마지막 로그인 시각"을 남기고 남의 refresh 토큰을 지운 흔적만 남긴다.
        //
        //   ⚠️ 축 A(테넌트 총량 제한, SessionLimitMiddleware)와 **다른 축**이다.
        //     그쪽은 `COUNT(DISTINCT user_id)` 라 한 계정이 5대에 붙어도 1로 센다 —
        //     이 정책을 그 식으로는 영원히 만들 수 없다(선행검증서 §4).
        var loginDb = _unitOfWork.GetDbConnection();
        var deviceKind = NormalizeDeviceKind(request.DeviceType);
        await EnforceSinglePcLoginAsync(loginDb, user, deviceKind, request.ForceSignOutOtherPc);

        var redirectToWelcome = user.LastLoginAt is null;
        user.LastLoginAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        // 봉합 2026-06-17 1.2.12 — TenantConfigReader 정합 (1.2.6 환경변수 폐기 결재)
        var secret = TenantConfigReader.GetRequired("JWT_SECRET");

        // 🔴 세션 번호를 **토큰보다 먼저** 만든다 — 토큰 안에 이 번호가 들어가야
        //   나중에 "이 PC 의 세션이 살아 있나" 를 물을 수 있다(절G·절H).
        //   user_id 만으로는 PC1 과 PC2 를 못 가른다.
        var sessionId = Guid.NewGuid().ToString();

        var response = CreateLoginResponse(user, employee, secret, redirectToWelcome, sessionId);

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

        // ── 세션 기록 (20260927작1 절B) ────────────────────────────────────────────────
        //
        //   🔴 **이 표에 넣는 코드가 여태 한 줄도 없었다.**
        //     세는 곳(SessionLimitMiddleware:114)과 지우는 곳(AuthController 로그아웃)은 있는데
        //     넣는 곳만 없어 user_sessions 가 항상 비어 있었다 ⇒ 동시접속 제한이 한 번도 안 걸렸다.
        //     DB-28 은 행이 있다고 전제하고 expires_at 컬럼까지 늘렸다 — 최소 2명이 속았다.
        //     (선행검증서 docs/검증/선행/20260927_선행검증서_계정과금_PC동시로그인차단_전제실측.md §2)
        //
        //   ⚠️ refresh_tokens INSERT 와 **같은 트랜잭션이 아니다.** 이 경로에는 트랜잭션이 없고
        //     (갱신 경로에는 있다), 새로 씌우는 것은 기존 로그인 구조 변경이라 범위 밖이다.
        //     같은 연결·바로 다음 줄에 붙인다.
        await InsertSessionAsync(db, sessionId, user, deviceKind);

        return response;
    }

    /// <summary>
    /// 기기 종류를 <c>pc</c> / <c>mobile</c> 둘로 정규화한다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>규칙을 새로 만들지 않는다</b> — 히트판에 이미 판정이 끝난 원칙이 있다:
    /// <b>"모르는 것은 싼 칸으로"</b> (20260815 아키텍처명세서 §3 #4 — <i>"ERP 가 맞다"</i>).
    /// 백오피스 <c>DeviceRegistrationController.cs:89-96</c> 과 <b>같은 규칙</b>이다
    /// (규칙만 같게, 코드는 각자 — 별 시스템이므로).
    ///
    /// <para>
    /// 근거가 된 사고: 모르는 값을 <c>pc</c> 로 보내 <b>고객이 쓰지도 않은 비싼 자리에 돈을 냈다.</b>
    /// <c>?? "pc"</c> 폴백도 같은 이유로 걷어냈다(20260815작3 P1 I-6).
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>남는 구멍을 숨기지 않는다</b> — DeviceType 을 안 보내는 클라이언트는 mobile 로 떨어져
    /// 축 B 차단을 안 받는다. 매출 누수가 아니라 <b>차단 누락</b>이다(과금 축은 9/25 결재로 계정이 됐다).
    /// ERP 웹은 항상 보낸다(<c>Web/Services/AuthService.cs:45</c>).
    /// </para>
    ///
    /// <para>🚫 UserAgent 문자열로 판정하지 마라 — 사용자가 바꿀 수 있어 위장하면 차단이 뚫린다.</para>
    /// </remarks>
    /// <summary>
    /// 게이트 전용 — 실제 판정 메서드를 그대로 부른다 (20260927작1 G-3).
    /// </summary>
    /// <remarks>
    /// ⚠️ 시험이 <b>값</b>을 대조하기 위해 연다. 글자 검사로는 이 결함을 못 잡는다 —
    /// 규칙을 다른 파일로 옮기거나 문자열만 바꿔도 통과하던 실패를 되풀이하지 않는다
    /// (<c>SessionLimitMiddleware.TierSessionLimitForTests</c> 선례).
    /// </remarks>
    public static string NormalizeDeviceKindForTests(string? deviceType) => NormalizeDeviceKind(deviceType);

    private static string NormalizeDeviceKind(string? deviceType)
        => string.Equals((deviceType ?? "").Trim(), "pc", StringComparison.OrdinalIgnoreCase)
            ? "pc"
            : "mobile";   // 빈값 · tablet · 모르는 값 전부 — 싼 칸 = 안 막히는 쪽

    /// <summary>
    /// 같은 계정이 이미 다른 PC 에서 살아 있으면 로그인을 거절한다 (축 B).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>모바일은 세지 않는다</b> — 9/25 결재로 모바일은 FREE 이고 PC 와 동시 접속이 허용된다.
    /// <para>🔴 <b>죽은 세션은 막지 않는다</b>(<c>expires_at &gt; NOW()</c>) — PC 가 꺼져 로그아웃이
    /// 안 돈 경우 남은 행이 <b>본인 계정을 스스로 잠그는</b> 일을 막는다.</para>
    /// <para>⚠️ 판정에 실패해도 <b>로그인은 통과시킨다</b>(가용성 우선). 다만 조용히 넘기지 않는다 —
    /// 기존 SessionLimitMiddleware 가 예외를 삼켜 <i>아무도 안 도는 걸 몰랐던</i> 그 구조를
    /// 물려받지 않는다(#15).</para>
    /// </remarks>
    private static async Task EnforceSinglePcLoginAsync(
        System.Data.IDbConnection db, User user, string deviceKind, bool force)
    {
        if (!string.Equals(deviceKind, "pc", StringComparison.Ordinal)) return;   // 모바일은 대상 아님

        try
        {
            // 킬스위치 — 행이 없으면 기본 켬(1). 사고 시 배포 없이 끌 수 있어야 한다(#21 로 appsettings 불가).
            var enabled = await db.ExecuteScalarAsync<int?>(
                "SELECT enforce_single_pc_login FROM tenant_settings WHERE tenant_id = @TenantId",
                new { TenantId = user.TenantId }) ?? 1;
            if (enabled == 0) return;

            var other = await db.QueryFirstOrDefaultAsync<DateTime?>(
                @"SELECT last_active_at FROM user_sessions
                   WHERE user_id = @UserId AND device_kind = 'pc' AND expires_at > UTC_TIMESTAMP(6)
                   ORDER BY last_active_at DESC LIMIT 1",
                new { UserId = user.Id });

            if (other is null) return;

            if (!force)
            {
                // 🔴 자동으로 밀어내지 않는다. 사용자가 확정해야 끊는다(반자동 원칙 · 사장님 전결 9/27).
                throw new ConcurrentPcLoginException(other.Value);
            }

            // 사용자가 [그 PC 접속을 끊고 여기서 사용하기] 를 눌렀다 — 그때만 끊는다.
            await db.ExecuteAsync(
                "DELETE FROM user_sessions WHERE user_id = @UserId AND device_kind = 'pc'",
                new { UserId = user.Id });
        }
        catch (ConcurrentPcLoginException)
        {
            throw;   // 거절은 의도된 결과다 — 아래 가용성 폴백으로 삼키면 차단이 사라진다.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError(
                $"[축B] PC 동시로그인 판정 실패(로그인은 진행) user={user.Id}: {ex.Message}");
        }
    }

    /// <summary>로그인 성공 시 세션 행을 남긴다. 실패해도 로그인은 막지 않는다(G-6).</summary>
    private static async Task InsertSessionAsync(
        System.Data.IDbConnection db, string sessionId, User user, string deviceKind)
    {
        try
        {
            await db.ExecuteAsync(
                @"INSERT INTO user_sessions (session_id, user_id, tenant_id, login_at, last_active_at, expires_at, device_kind)
                  VALUES (@SessionId, @UserId, @TenantId, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), @ExpiresAt, @DeviceKind)",
                new
                {
                    SessionId = sessionId,
                    UserId = user.Id,
                    TenantId = user.TenantId,
                    ExpiresAt = DateTime.UtcNow.Add(AccessTokenLifetime),
                    DeviceKind = deviceKind
                });
        }
        catch (Exception ex)
        {
            // 🔴 로그만 남기면 또 아무도 모른다 — 기존 미들웨어가 정확히 그래서 안 걸렸다.
            //   눈에 보이는 흔적(security_alerts)을 같이 남긴다.
            System.Diagnostics.Trace.TraceError(
                $"[세션] user_sessions INSERT 실패(로그인은 진행) user={user.Id}: {ex.Message}");
            try
            {
                await db.ExecuteAsync(
                    @"INSERT INTO security_alerts (alert_id, tenant_id, user_id, alert_type, description)
                      VALUES (@Id, @TenantId, @UserId, 'session_insert_failed', @Desc)",
                    new
                    {
                        Id = Guid.NewGuid().ToString(),
                        TenantId = user.TenantId,
                        UserId = user.Id,
                        Desc = $"세션 기록 실패: {ex.Message}".Length > 500
                            ? $"세션 기록 실패: {ex.Message}"[..500]
                            : $"세션 기록 실패: {ex.Message}"
                    });
            }
            catch (Exception inner)
            {
                System.Diagnostics.Trace.TraceError($"[세션] security_alerts 기록도 실패: {inner.Message}");
            }
        }
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

        // W1-3 (작업지시서 20260707작2): refresh 도 익명 경로 — LoginAsync 와 동일 사유로
        //   테넌트필터 우회 + user.TenantId 명시 한정 조회로 교체(헌법 #2 테넌트 격리 유지).
        var employee = await _authUserLookup.FindActiveEmployeeByUserAsync(user.Id, user.TenantId, ct);

        // 백필 (2026-06-22, 13차 A안): refresh 경로도 동일 — 부모계정 employees 행 멱등 보장.
        // 실패해도 refresh(=세션 유지)는 막지 않는다(13차 거짓봉합 재봉합, 백필은 보조 자가치유).
        // 🔴 봉합 (2026-08-14, 1.2.74 실사용 P0): 자식계정도 백필한다.
        //    사장님: "자식계정은 생성되었으나 ... 다른 그 어떤메뉴에도 그 계정직원은 안나옴."
        //    사번 채번 충돌로 employees INSERT 가 실패해도 users 는 커밋돼 **고아 계정**이 남았는데,
        //    이 백필이 tenant_admin 전용이라 **자식은 영원히 자가치유가 안 됐다** —
        //    재등록은 이메일 중복으로 막히고, 사원관리에서 넣으면 연결 안 된 별개 행이 생겨
        //    DB 직접 수술 외엔 길이 없었다.
        //    ⇒ 이미 만들어진 고아 계정도 **다음 로그인 한 번으로 스스로 복구된다.**
        //    (채번·트랜잭션은 UserService.CreateAsync 에서 봉합했으므로 새 고아는 더 안 생긴다.)
        if (employee is null)
        {
            try { employee = await BackfillParentEmployeeAsync(user, ct); }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"[Backfill] 부모계정 employees 백필 실패(refresh는 진행): {ex.Message}"); }
        }

        // ── 세션 번호 이어받기 (20260927작1 절C) ──────────────────────────────────────
        //
        //   갱신 토큰이 들고 온 세션 번호를 **그대로 쓴다.** 새로 만들면 8시간마다
        //   세션 행이 하나씩 늘어 「같은 계정 PC 1대」 판정이 자기 자신 때문에 막힌다.
        //
        //   🔴 옛 갱신 토큰에는 `sid` 가 없다(배포 전에 발급된 것). 그때만 새로 만든다 —
        //     없다고 거절하면 **배포 순간 전 고객이 튕긴다.** 7일이면 자연히 갈린다.
        var sessionId = principal.FindFirst("sid")?.Value;
        var isNewSession = string.IsNullOrWhiteSpace(sessionId);
        if (isNewSession) sessionId = Guid.NewGuid().ToString();

        var response = CreateLoginResponse(user, employee, secret, redirectToWelcome: false, sessionId!);

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

        // ── 세션 수명 연장 (20260927작1 절C) ──────────────────────────────────────────
        //
        //   갱신은 "아직 쓰고 있다" 는 신호다. 세션의 만료 시각을 새 access 토큰에 맞춰 민다.
        //   ⚠️ 밀어내기로 **이미 지워진 세션은 되살리지 않는다**(UPDATE 는 0행으로 끝난다) —
        //     되살리면 사용자가 끊은 PC 가 갱신 한 번으로 스스로 부활한다.
        //   🔴 옛 토큰(sid 없음)일 때만 행을 새로 만든다.
        try
        {
            if (isNewSession)
            {
                // 🔴 갱신 경로에는 기기 정보가 없다 ⇒ **모르는 값**이다.
                //   같은 원칙을 그대로 적용한다: 모르는 것은 싼 칸(mobile) — 차단 대상이 아니다.
                //   여기서 "pc" 로 적으면, 옛 모바일 토큰이 갱신되는 순간 PC 세션이 생겨
                //   **사용자 본인의 PC 로그인을 막는다.**
                await InsertSessionAsync(conn, sessionId!, user, NormalizeDeviceKind(null));
            }
            else
            {
                await conn.ExecuteAsync(
                    @"UPDATE user_sessions
                         SET last_active_at = UTC_TIMESTAMP(6), expires_at = @ExpiresAt
                       WHERE session_id = @SessionId",
                    new { SessionId = sessionId, ExpiresAt = DateTime.UtcNow.Add(AccessTokenLifetime) });
            }
        }
        catch (Exception ex)
        {
            // 갱신 자체는 이미 성공했다 — 세션 기록 실패로 사용자를 끊지 않는다(가용성 우선).
            System.Diagnostics.Trace.TraceError(
                $"[세션] 갱신 시 user_sessions 기록 실패(갱신은 성공) user={user.Id}: {ex.Message}");
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

        // 봉합 (2026-06-22, 13차 후순위 emp_no 비연속 엣지 → 2단 교차검증 거짓봉합 재봉합):
        //   1차 봉합은 emp_no 를 Count()+1 에서 MAX(파싱)+1 로 바꾼 것까진 맞았으나, 충돌 catch 에서
        //   employeeRepo.Remove(실패엔티티) 를 호출해 "추적 해제"를 의도했다. 그런데 Repository.Remove 는
        //   Detach 가 아니라 소프트삭제(IsActive=false + DbSet.Update)라, 실패 Added 엔티티가 Modified 로
        //   트래커에 남아 다음 SaveChanges 가 커밋된 적 없는 행 UPDATE → DbUpdateConcurrencyException →
        //   1062 아님 → 로그인 하드 차단(원버그보다 악화). 재봉합 = 재시도 루프 자체를 제거한다.
        //
        //   근거: emp_no = 같은 tenant 의 가장 큰 emp_no(숫자 파싱) +1. 가장 큰 값 +1 은 정의상 기존에
        //   존재할 수 없으므로 비연속(0001·0003·0005 → 0006)에도 단일 시도로 충돌하지 않는다. 남는 위험은
        //   동시 로그인 경합(같은 user 가 두 세션에서 동시 백필)뿐인데, 그땐 catch 에서 SaveChanges·Update·
        //   Remove 를 일절 호출하지 않고 재조회만 한다 — 실패 Added 엔티티를 flush 하지 않으므로 트래커
        //   오염이 무해하고, 먼저 커밋한 세션이 만든 행을 그대로 사용한다(헌법 #12 동시성·#15 silent 금지).
        // W1-3 (작업지시서 20260707작2): 백필도 로그인·refresh 에서 불리는 익명 경로 — 종전 조회는
        //   테넌트필터(CurrentTenant='')에 걸려 항상 0건 → emp_no 가 매번 0001 로 계산돼 기존 행과
        //   1062 충돌(헛INSERT)이 반복됐다. 필터 우회 + user.TenantId 명시 한정으로 교체.
        var existing = await _authUserLookup.FindEmployeesByTenantAsync(user.TenantId, ct);

        // 이미 이 user 의 employees 행이 있으면(중복 백필 방지) 그걸 그대로 사용.
        var already = existing.FirstOrDefault(e => e.UserId == user.Id && e.IsActive);
        if (already is not null) return already;

        // 🔴 작(2026-08-21) 사장님 지시 ①·③ — 퇴사자에게 새 사번을 발급하지 않는다.
        //
        //    이 백필은 ★고아 계정 자가치유★ 용이다(2026-06-22·08-14 봉합):
        //    users 는 만들어졌는데 employees 행이 ★없는★ 사람을 구제한다.
        //    그런데 판정이 IsActive 하나뿐이라 ★퇴사자★(행은 있는데 꺼진 사람)까지
        //    "행이 없는 사람" 으로 보고 새 행을 만들어 버렸다.
        //
        //    ⇒ 퇴사 기록 1행 + 새로 만든 재직 1행 = 같은 사람이 둘이 되고 사번이 갈린다.
        //       사장님 지시 ③ "재입사도 상태변경으로" 가 깨진다 — 상태변경이 아니라 새 사람이 생긴다.
        //       재입사 권한(부모계정 관리자만)도 우회된다 — 본인 로그인만으로 재직 행이 생기므로.
        //
        // ⚠️ 지금은 ResignAsync 가 users.is_active 도 꺼서 로그인 자체가 막히지만,
        //    ★재입사 기능이 계정을 되살리는 순간 이 경로가 열린다.★ 그래서 먼저 막는다.
        //
        // ⚠️ 백필을 없애는 게 아니다 — 고아 계정 구제는 그대로 살려둔다(8/14 사장님 P0).
        //    "자식계정은 생성되었으나 다른 그 어떤메뉴에도 그 계정직원은 안나옴."
        //    ★퇴사자만 갈라낸다.★ (게이트: ResignedLoginGateGuardTests)
        var resignedRow = existing.FirstOrDefault(e => e.UserId == user.Id && !e.IsActive);
        if (resignedRow is not null)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"[Backfill] 퇴사 이력이 있는 계정이라 새 사원 행을 만들지 않는다 " +
                $"(user={user.Id}, employee={resignedRow.EmployeeId}). " +
                $"재입사는 부모계정 관리자가 사원관리에서 상태를 되돌린다.");
            return null;
        }

        // 🔴 봉합 (2026-08-14, 1.2.74 실사용 P0): 접두가 붙은 사번을 못 읽던 자리.
        //    int.TryParse("MIG-0007") 도 int.TryParse("EMP-001") 도 **실패해서 0** 이 된다.
        //    실측한 DB 에는 0001 · MIG-0001~0010 뿐이라 MAX 가 0 으로 나왔고,
        //    채번이 늘 0001 → 부모계정 기존 행과 uq_tenant_empno 충돌이 났다.
        //    ⇒ 접두를 무시하고 **끝의 숫자**만 읽는다(UserService.CreateAsync 와 같은 규칙).
        var maxNo = existing
            .Select(e =>
            {
                var digits = System.Text.RegularExpressions.Regex.Match(
                    e.EmpNo ?? string.Empty, @"[0-9]+$").Value;
                return int.TryParse(digits, out var n) ? n : 0;
            })
            .DefaultIfEmpty(0)
            .Max();
        var empNo = (maxNo + 1).ToString("D4");

        // 🔴 작(2026-08-14): 부모/자식을 가른다. 종전엔 이 백필이 부모계정 전용이라
        //    전부 tenant_admin 으로 굳어 있었다. 이제 자식계정도 여기로 들어오므로
        //    **자식에게 대표 직급·관리자 역할을 붙이면 안 된다**(권한 승격 사고).
        var isOwner = user.AccountType == "tenant_admin";

        // EmployeeId 는 EF 매핑상 Ignore(실 PK 는 BaseEntity.Id) — 설정 안 함. employee_id 클레임은 .Id 사용.
        var newEmployee = new Employee
        {
            TenantId = user.TenantId,
            UserId = user.Id,
            EmpNo = empNo,
            EmpName = user.UserName,
            // 🔴 사장님 지시(2026-08-14): "부모계정 = 직급은 자동으로 대표.등록"
            //    종전엔 비워 둬 부모계정 직급이 NULL 이었다 — 결재선에서 대표를 못 골랐다.
            //    ⚠️ 자식계정은 직급을 우리가 정하지 않는다(헌법 #11) — 사원관리에서 사람이 넣는다.
            Position = isOwner ? HitPan.Domain.Common.OrgDefaults.OwnerPositionName : null,
            EmpType = HitPan.Domain.Enums.EmployeeType.Regular,
            JoinDate = DateTime.UtcNow,
            IsActive = true,
            Role = isOwner ? "tenant_admin" : "tenant_user",
            Email = user.Email,
        };

        try
        {
            await employeeRepo.AddAsync(newEmployee);
            await _unitOfWork.SaveChangesAsync(ct);
            return newEmployee;
        }
        catch (Exception ex)
        {
            // ★13차 2단 교차검증 거짓봉합 2회 재봉합의 핵심:
            //   SaveChanges 실패 시 newEmployee 는 Added 상태로 공유 DbContext 트래커에 남는다(EF 는 실패해도
            //   detach 하지 않음). 그대로 두면 이후 호출부의 SaveChanges(예: LastLoginAt 저장)가 이 좀비를
            //   재INSERT 시도 → 두 번째 실패가 try/catch 없는 곳에서 터져 로그인 500(원버그보다 악화).
            //   1차 재봉합의 "재조회만 하면 무해" 전제가 바로 이 지점에서 거짓이었다. 반드시 Detach 한다.
            employeeRepo.Detach(newEmployee);

            if (IsUniqueViolation(ex))
            {
                // 동시 로그인 경합으로 다른 세션이 먼저 백필했다 — 재조회로 그 행을 사용한다.
                //   W1-3 (작업지시서 20260707작2): 재조회도 익명 경로이므로 필터 우회 + tenant 한정으로 교체.
                return await _authUserLookup.FindActiveEmployeeByUserAsync(user.Id, user.TenantId, ct);
            }

            // 비-1062 예외(연결 끊김 등)는 전파한다(silent swallow 금지, 헌법 #15). 좀비는 위에서 Detach 했으니
            // 호출부 SaveChanges 는 안전하다. 백필 실패가 로그인을 막지 않도록 호출부를 try/catch 로 감싼다.
            throw;
        }
    }

    /// <summary>
    /// MariaDB UNIQUE 제약(uq_tenant_empno 등) 위반 여부 판정 — 에러코드 1062(ER_DUP_ENTRY).
    /// 다른 예외(연결·타임아웃·검증)는 false 를 반환해 호출부에서 전파되도록 한다(헌법 #15 silent swallow 금지).
    /// </summary>
    private static bool IsUniqueViolation(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is MySqlConnector.MySqlException my && my.Number == 1062) return true;
            var msg = e.Message;
            if (msg.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("uq_tenant_empno", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static LoginResponse CreateLoginResponse(
        User user, Employee? employee, string secret, bool redirectToWelcome, string sessionId)
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
            new("role", employeeRole),

            // 🔴 세션 번호 (DB-127 축 B · 20260927작1 절H)
            //   이것이 없으면 **PC1 과 PC2 를 못 가른다.** user_id 만으로는
            //   "이 사용자 세션이 있나" 는 답해도 "**이 PC** 의 세션이 살아 있나" 는 못 답한다.
            //   SessionValidityMiddleware 가 이 값으로 밀어내기를 즉시 반영한다.
            //   ⚠️ 비밀이 아니다 — 무작위 GUID 이고, 이 값만으로는 아무것도 못 한다.
            new("sid", sessionId)
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
            new("token_type", "refresh"),

            // 🔴 갱신 토큰도 세션 번호를 들고 다닌다 — 안 그러면 8시간마다 갱신할 때
            //   **자기가 어느 세션인지 잊어버려** 새 세션 행이 계속 쌓인다.
            //   (옛 갱신 토큰에는 이 값이 없다 — 그때는 새 번호를 만든다. RefreshAsync 참조)
            new("sid", sessionId)
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
