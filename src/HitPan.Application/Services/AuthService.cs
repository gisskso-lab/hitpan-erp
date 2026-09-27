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
        //   🔴 2026-09-27 20260927작2 절A — **판정을 서버로 되돌렸다.**
        //     [종전] `NormalizeDeviceKind(request.DeviceType)` — 클라이언트 **자진신고 한 칸**만 봤다.
        //       화면에서 "mobile" 이라고 보내면 축 B 차단을 그대로 빠져나갔다([4] 반려 F-4).
        //     [지금] `DeviceTypeResolver.ResolveDeviceType` — 이미 8/18(V-05)에 결재돼 기기 등록이
        //       쓰고 있던 **그 함수를 같이 부른다.** 신고값과 서버가 읽은 User-Agent 가 어긋나면
        //       **서버가 이긴다.** 판정 자리를 둘로 만들지 않았다(작지 §3 금지 #2).
        //     ⚠️ 한계를 정확히 적는다(20260927작2 **2차** · [3-V] V-B1 — 아래가 실측으로 확인된 서술이다).
        //       ⬛ [낡은 줄] *"화면 조작만으로 되던 것을 **헤더까지 함께 위조해야** 되게 바꿨을 뿐이다"*
        //         — **틀렸다.** 위조를 요구하지 않는다. 함께 맞출 필요도 없다.
        //       🔴 **「서버가 이긴다」는 UA 가 말을 할 때만이다. UA 를 침묵시키면 신고값이 이긴다.**
        //         - `User-Agent` 를 **안 보내거나 공백**으로 두면 `JudgeTypeFromUserAgent` 가 `null` 을
        //           돌려주고(`DeviceTypeResolver.cs:176`), `:136` 이 **신고값을 그대로 쓴다**
        //           (신고값도 없으면 `?? "mobile"` — 싼 칸).
        //         - **Mac 계열 UA** 는 `:191-197` 이 8/10 아이패드 사고 재발 방지를 위해
        //           **의도적으로 판정을 포기**한다 ⇒ 같은 길로 빠져나간다.
        //         - `curl`·Postman·`HttpClient` 처럼 어느 토큰에도 안 맞는 클라이언트도 마찬가지다.
        //       ⇒ 이 봉합이 실제로 한 일은 **"브라우저가 스스로 붙인 UA 가 PC 라고 말하는데 화면만
        //         mobile 이라 신고하는" 경로를 닫은 것**이다. 그것 하나다.
        //       🔴 남는 것을 막는 것은 **다음 차수의 장비넘버(`hardware_id`)** 몫이고, 여기서 약속하지 않는다.
        //       🔴 이것을 *"고쳤다"* 고 적으면 거짓봉합이다 — 한계를 적은 것이다.
        var loginDb = _unitOfWork.GetDbConnection();
        var resolvedDeviceType = DeviceTypeResolver.ResolveDeviceType(request.DeviceType, request.UserAgent);
        // user_sessions.device_kind 는 enum('pc','mobile') 두 값뿐이다(DESCRIBE 실측 · #13).
        //   판정은 위에서 끝났고, 여기서는 저장 가능한 칸으로만 접는다(tablet → 휴대기기 칸).
        var deviceKind = DeviceTypeResolver.ToSessionDeviceKind(resolvedDeviceType);
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

        // ── 세션 기록 (20260927작1 절B → 20260927작2 절B 에서 **순서를 뒤집었다**) ──────
        //
        //   🔴 **왜 토큰보다 먼저 넣나** — 세션 행이 안 만들어졌는데 토큰에 세션 번호(`sid`)를
        //     실으면, 미들웨어가 그 번호로 행을 찾다 못 찾아 **모든 요청을 401 로 끊는다.**
        //     고객은 로그인은 됐는데 아무것도 못 하고, **끌 방법도 없다**([4] 반려 F-1 전면 잠금).
        //     ⇒ 넣기를 먼저 하고, **성공했을 때만** 번호를 토큰에 싣는다.
        //
        //   ⚠️ 종전 순서(토큰 `:129` → INSERT `:159`)로는 이 규칙이 **원리적으로 성립하지 않았다.**
        //     토큰이 이미 만들어진 뒤라 실패를 알 방법이 없었다(설계 §4-2).
        //
        //   ⚠️ refresh_tokens INSERT 와 **같은 트랜잭션이 아니다.** 이 경로에는 트랜잭션이 없고
        //     (갱신 경로에는 있다), 새로 씌우는 것은 기존 로그인 구조 변경이라 범위 밖이다.
        //     같은 연결·바로 다음 줄에 붙인다.
        //
        //   🔴 **이 표에 넣는 코드가 여태 한 줄도 없었다.**
        //     세는 곳(SessionLimitMiddleware:114)과 지우는 곳(AuthController 로그아웃)은 있는데
        //     넣는 곳만 없어 user_sessions 가 항상 비어 있었다 ⇒ 동시접속 제한이 한 번도 안 걸렸다.
        //     DB-28 은 행이 있다고 전제하고 expires_at 컬럼까지 늘렸다 — 최소 2명이 속았다.
        //     (선행검증서 docs/검증/선행/20260927_선행검증서_계정과금_PC동시로그인차단_전제실측.md §2)
        var db = _unitOfWork.GetDbConnection();
        var sessionRecorded = await InsertSessionAsync(db, sessionId, user, deviceKind);

        // 🔴 세션 기록이 실패했으면 `sid` 를 **싣지 않는다.** `sid` 없는 토큰은 미들웨어가
        //   종전처럼 통과시킨다(옛 토큰 호환 경로 · 작지 §3 금지 #1b) ⇒ 전면 잠금이 생기지 않는다.
        //   그 대신 흔적은 남았다(security_alerts — InsertSessionAsync 참조).
        var response = CreateLoginResponse(
            user, employee, secret, redirectToWelcome, sessionRecorded ? sessionId : null);

        // refresh token DB 저장 — 로그아웃 is_revoked=1 차단의 기준
        //   ⚠️ `response` 를 쓰므로 토큰 생성 뒤여야 한다(설계 §4-2 순서 4).
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

        // ⚠️ 세션 기록은 **위(토큰 생성 앞)로 올라갔다** — 20260927작2 절B · 설계 §4-2.
        //   여기서 부르면 실패를 알기 전에 이미 `sid` 가 실려 전면 잠금이 생긴다.

        return response;
    }

    // ── 기기 종류 판정 (20260927작2 절A) ──────────────────────────────────────────────
    //
    //   🔴 여기 있던 `NormalizeDeviceKind` · `NormalizeDeviceKindForTests` 를 **없앴다.**
    //     그 함수는 클라이언트 신고값(`request.DeviceType`) **한 칸만** 보고 판정해서,
    //     화면에서 "mobile" 이라고 보내면 축 B 차단을 그대로 빠져나갔다([4] 반려 F-4).
    //     주석에 *"🚫 UserAgent 문자열로 판정하지 마라"* 고 적혀 있었는데, 그 금지 자체가
    //     **8/18 에 이미 결재된 교차검증 봉합(V-05)을 금지**하고 있었다(사장님 전결 9/27 §1·§3).
    //
    //   ⇒ 판정은 `HitPan.Application.Common.DeviceTypeResolver` **한 곳**에 있다.
    //     기기 등록(`TenantDeviceService`)과 로그인이 **같은 함수**를 부른다 —
    //     판정 자리를 둘로 만들면 한쪽만 고쳐지는 사고가 난다(폴백 2곳 D-9 선례 · 작지 §3 금지 #2).
    //     게이트도 그 파일의 `public static` 을 직접 부른다(그래서 여기 시험용 창구가 필요 없다).

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
            // 킬스위치 — 사고 시 배포 없이 끌 수 있어야 한다(#21 로 appsettings 불가).
            //
            //   🔴 2026-09-27 20260927작2 절D — 행이 없을 때의 폴백을 `?? 1`(켬) → **`?? 0`(끔)** 으로 내렸다.
            //     [왜] 축 B 는 이번 차수에 **기본 OFF 로 출하**한다(사장님 전결 §1 — 선례 수준 도달 확인 후
            //       별도 결재로 켠다). `tenant_settings` 행이 없는 고객사(마이그가 안 붙은 DB 등)에서
            //       `?? 1` 은 **고객이 끄지도 못하는 상태로 켜진다** — 설정 화면에 쓸 행 자체가 없다.
            //     ⇒ 모르면 끈 쪽이다. 같은 방향으로 DB 기본값도 0 으로 내린다(절G · DB-128).
            var enabled = await db.ExecuteScalarAsync<int?>(
                "SELECT enforce_single_pc_login FROM tenant_settings WHERE tenant_id = @TenantId",
                new { TenantId = user.TenantId }) ?? 0;
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
            var forcedOut = await db.ExecuteAsync(
                "DELETE FROM user_sessions WHERE user_id = @UserId AND device_kind = 'pc'",
                new { UserId = user.Id });

            // 🔴 2026-09-27 20260927작2 절D (P1-2) — **밀어낸 사실을 남긴다.**
            //   [무엇이 비대칭이었나] 세션 기록 **실패**(기술적 사고)는 security_alerts 에 남는데,
            //     **남의 PC 접속을 끊은 일**(보안 사건)은 아무 흔적도 남지 않았다.
            //     계정이 털려 남이 내 PC 를 밀어내도 **물어볼 자료가 없다.**
            //   ⚠️ 기록 실패로 로그인을 막지 않는다 — 사용자는 이미 확정 버튼을 눌렀다.
            try
            {
                await db.ExecuteAsync(
                    @"INSERT INTO security_alerts (alert_id, tenant_id, user_id, alert_type, description)
                      VALUES (@Id, @TenantId, @UserId, 'pc_session_forced_out', @Desc)",
                    new
                    {
                        Id = Guid.NewGuid().ToString(),
                        TenantId = user.TenantId,
                        UserId = user.Id,
                        Desc = $"사용자 확인 후 다른 컴퓨터의 접속을 끊었습니다(끊은 접속 {forcedOut}건, 이전 사용 {other.Value:yyyy-MM-dd HH:mm} UTC)"
                    });
            }
            catch (Exception alertEx)
            {
                System.Diagnostics.Trace.TraceError(
                    $"[축B] 밀어내기 기록 실패(로그인은 진행) user={user.Id}: {alertEx.Message}");
            }
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
    /// <returns>
    /// 🔴 <b>행을 실제로 남겼는가</b> — 20260927작2 절B 에서 <c>void</c> 에서 <c>bool</c> 로 넓혔다.
    /// <para>
    /// 호출부가 이 답을 알아야 <b>실패했을 때 토큰에 세션 번호(<c>sid</c>)를 안 실을 수 있다.</b>
    /// 행이 없는데 번호를 실으면 미들웨어가 그 번호로 행을 못 찾아 <b>모든 요청을 401 로 끊는다</b>
    /// ([4] 반려 F-1 전면 잠금 — 고객이 끌 방법도 없다).
    /// </para>
    /// </returns>
    private static async Task<bool> InsertSessionAsync(
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
            return true;
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

            // 🔴 실패를 **호출부에 알린다**(절B). 종전에는 삼키고 void 로 끝나서,
            //   행이 없는데 `sid` 가 실린 토큰이 나갔다 — 그것이 전면 잠금의 입구였다.
            return false;
        }
    }

    /// <summary>
    /// 갱신이 <b>방금 만든</b> 세션 행을, 토큰 회전이 실패했을 때 되돌린다 (20260927작2 <b>3차</b> 보상 삭제).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>왜 필요한가</b> — 2차 봉합(D-2)이 세션 INSERT 를 refresh_tokens 회전 <b>앞</b>으로 올렸다.
    /// <c>sid</c> 를 실을지 말지를 알려면 그 자리여야 한다. 그래서 회전이 실패해 <b>401</b> 을 던지면
    /// 방금 넣은 <c>pc</c> 행이 <b>아무도 쓰지 않는 채 만료까지 남는다.</b> 축 B 를 켠 고객은
    /// <b>자기 잔여 행 때문에 자기 재로그인이 409</b> 가 된다.
    /// </para>
    /// <para>
    /// 🔴 <b>이 호출이 만든 행 하나만</b> 지운다(<c>WHERE session_id</c>). <c>user_id</c> 전삭은
    /// <b>살아 있는 다른 PC·모바일 세션을 끊는 일</b>이라 하지 않는다.
    /// 이어받은 세션(<c>isNewSession == false</c>)은 애초에 이 함수로 넘기지 않는다 —
    /// 이 호출이 만든 행이 아니고, 지우면 <b>일하고 있는 PC 를 갱신 실패 한 번으로 끊는다.</b>
    /// </para>
    /// <para>
    /// 🔴 <b>삭제 실패는 삼킨다</b> — 부르는 자리는 이미 예외를 던지는 길이다. 여기서 새 예외를 올리면
    /// <b>원인이 바뀌어</b>(401 이 500 으로) 다음 사람이 엉뚱한 곳을 본다. 조용히는 넘기지 않는다(#15) —
    /// 남은 행의 번호를 로그에 적어 손으로 찾을 수 있게 한다.
    /// </para>
    /// </remarks>
    private static async Task CompensateNewSessionRowAsync(
        System.Data.IDbConnection db, User user, string? sessionIdCreatedHere, string reason)
    {
        if (string.IsNullOrWhiteSpace(sessionIdCreatedHere)) return;   // 이어받은 세션·기록 실패 — 지울 것이 없다

        try
        {
            var removed = await db.ExecuteAsync(
                "DELETE FROM user_sessions WHERE session_id = @SessionId",
                new { SessionId = sessionIdCreatedHere });

            System.Diagnostics.Trace.TraceWarning(
                $"[세션] 갱신 회전 실패({reason}) — 방금 만든 세션 행을 되돌렸다(삭제 {removed}건) "
                + $"user={user.Id}, session={sessionIdCreatedHere}");
        }
        catch (Exception ex)
        {
            // 🔴 삼키지만 조용히는 넘기지 않는다 — 이 행이 남으면 본인 재로그인이 409 로 막힌다.
            System.Diagnostics.Trace.TraceError(
                $"[세션] 갱신 회전 실패 후 보상 삭제 실패 — 주인 없는 세션 행이 만료까지 남는다. "
                + $"user={user.Id}, session={sessionIdCreatedHere}: {ex.Message}");
        }
    }

    /// <summary>
    /// 만료된 <b>내 PC 세션</b>을 갱신으로 되살려도 되는지 판정한다 (20260927작2 절C · 설계 §5).
    /// </summary>
    /// <remarks>
    /// <para>
    /// [무엇이 문제였나] 갱신은 <c>sid</c> 만 보고 <c>expires_at = +8h</c> 를 <b>무조건</b> 밀었다.
    /// 그래서 <b>만료된 PC1 이 부활해 PC2 와 영구 공존</b>했다([4] 반려 F-6) —
    /// 「같은 계정 PC 1대」 정책이 갱신 한 번으로 무력화된다.
    /// </para>
    /// <para>
    /// 🔴 <b>그렇다고 만료를 곧바로 끊으면 안 된다.</b> 밤샘 근무는 정상 업무다(#20).
    /// 8시간이 지났을 뿐 다른 PC 가 없다면 <b>되살리는 것이 맞다.</b>
    /// ⇒ 끊는 경우는 딱 하나다: <b>만료된 내 PC + 살아 있는 다른 PC.</b>
    /// 그때만 401 로 보내 재로그인 화면에서 <b>사용자가 직접 고르게</b> 한다(반자동 원칙 · 그 자리에서 밀어내기 선택).
    /// </para>
    /// <para>
    /// ⚠️ <b>판정에 실패하면 되살리는 쪽</b>이다(가용성 우선 — <c>EnforceSinglePcLoginAsync</c> 와 같은 원칙).
    /// 못 읽었다고 일하는 사람을 끊지 않는다. 다만 조용히 넘기지 않는다(#15).
    /// </para>
    /// <para>
    /// ⚠️ 행이 아예 없으면(밀어내기로 지워짐) <b>현행 유지</b> — 되살리지 않고, 뒤의 UPDATE 가 0행으로 끝난다.
    /// </para>
    /// </remarks>
    private static async Task GuardExpiredPcSessionRevivalAsync(
        System.Data.IDbConnection db, User user, string sessionId)
    {
        try
        {
            // 내 세션 한 줄. ⚠️ 만료 비교는 **DB 안에서** 한다 — 서버·DB 시간대 해석이 갈리면
            //   "만료됐나" 의 답이 달라진다(저장은 UTC_TIMESTAMP(6) · DESCRIBE 실측 datetime(6)).
            var mine = await db.QueryFirstOrDefaultAsync<SessionAliveRow>(
                @"SELECT device_kind AS DeviceKind,
                         CASE WHEN expires_at > UTC_TIMESTAMP(6) THEN 1 ELSE 0 END AS Alive
                    FROM user_sessions
                   WHERE session_id = @SessionId",
                new { SessionId = sessionId });

            if (mine is null) return;                               // ② 행 없음 — 현행 유지
            if (mine.Alive == 1) return;                            // ③ 아직 살아 있다 — 그대로 연장
            if (!string.Equals(mine.DeviceKind, "pc", StringComparison.Ordinal)) return;   // ④ 모바일은 축 B 대상 아님(9/25 결재)

            // ⑤ 킬스위치 — 꺼져 있으면 되살린다. 행이 없으면 끈 것으로 본다(절D 와 같은 방향).
            var enabled = await db.ExecuteScalarAsync<int?>(
                "SELECT enforce_single_pc_login FROM tenant_settings WHERE tenant_id = @TenantId",
                new { TenantId = user.TenantId }) ?? 0;
            if (enabled == 0) return;

            // ⑥⑦ 다른 PC 가 살아 있나 — 내 세션은 빼고 본다.
            var otherPcAlive = await db.ExecuteScalarAsync<int?>(
                @"SELECT 1 FROM user_sessions
                   WHERE user_id = @UserId AND device_kind = 'pc'
                     AND session_id <> @SessionId AND expires_at > UTC_TIMESTAMP(6)
                   LIMIT 1",
                new { UserId = user.Id, SessionId = sessionId });

            if (otherPcAlive is null) return;                       // ⑥ 다른 PC 없음 — 밤샘이다. 되살린다.

            // ⑦ 🔴 되살리지 않는다. 고객 언어로만 말한다(개발용어 금지).
            throw new UnauthorizedAccessException(
                "다른 컴퓨터에서 사용 중이어서 접속이 만료되었습니다. 다시 로그인해 주세요.");
        }
        catch (UnauthorizedAccessException)
        {
            throw;   // 거절은 의도된 결과다 — 아래 가용성 폴백으로 삼키면 판정이 사라진다.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError(
                $"[축B] 갱신 세션 되살림 판정 실패(갱신은 진행) user={user.Id}: {ex.Message}");
        }
    }

    /// <summary>
    /// <c>GuardExpiredPcSessionRevivalAsync</c> 가 읽는 내 세션 한 줄 (20260927작2 절C).
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>Alive</c> 는 DB 가 계산한 값이다(<c>expires_at &gt; UTC_TIMESTAMP(6)</c>).
    /// 시각을 그대로 받아 코드에서 비교하면 시간대 해석에 따라 답이 갈린다.
    /// </remarks>
    private sealed class SessionAliveRow
    {
        public string? DeviceKind { get; set; }
        public int Alive { get; set; }
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

        // 🔴 20260927작2 절C — **되살려도 되는 세션인지 먼저 묻는다.**
        //   토큰을 회전시키기 **전**에 판정한다. 회전 뒤에 거절하면 쓴 토큰은 이미 소비돼
        //   고객은 재로그인밖에 길이 없는데, 우리는 굳이 새 토큰을 하나 굽고 버리는 셈이다.
        if (!isNewSession)
        {
            await GuardExpiredPcSessionRevivalAsync(conn, user, sessionId!);
        }

        // ── 새 세션을 만드는 갱신 (20260927작2 **2차 봉합** · [4] D-2·D-3 · [3-V] V-B2) ────────
        //
        //   🔴 여기 있는 것은 **종전 맨 아래(토큰 회전 뒤)에 있던 세션 INSERT 를 위로 올린 것**이다.
        //     아래 「세션 수명 연장」 블록의 `isNewSession` 분기가 이 자리로 왔다.
        //
        //   [D-2 · 왜 올렸나] 종전에는 `CreateLoginResponse(..., sessionId!)` 가 **무조건** `sid` 를
        //     토큰에 싣고, INSERT 는 그 **뒤에** 일어나며 **반환값을 버렸다.** 그래서 INSERT 가
        //     한 번 실패하면 **행은 없는데 `sid` 는 실린 토큰**이 나가고, `SessionValidityMiddleware`
        //     가 그 번호로 행을 못 찾아 **모든 요청을 401 로 끊는다.** 화면의 자동 재발급이 다시
        //     `/refresh` 를 불러 같은 상태를 재생산한다 — 회복은 재로그인뿐이다.
        //     ⇒ 로그인 경로(`:160·166`)와 **똑같이** 순서를 뒤집었다: 넣기를 먼저, **성공했을 때만** 싣는다.
        //     🔴 전결 §2 는 *"두 자리를 같이 막아야 닫힌다"* 였는데 로그인 한쪽만 닫혀 있었다.
        //
        //   ⚠️ **트랜잭션 경계는 건드리지 않았다.** 아래 refresh_tokens 회전 트랜잭션(`BeginTransaction`)
        //     **앞**이다 — 세션 기록 실패가 갱신 자체를 롤백시키면 고객이 못 들어온다.
        //     실패는 `InsertSessionAsync` 안에서 삼키고(`security_alerts` 기록은 유지) `sid` 만 안 싣는다.
        //
        //   ⚠️ 대가를 적는다 — 이 뒤 트랜잭션이 단일사용 거부(`deleted == 0`)로 401 이 되면
        //     **쓰이지 않는 세션 행 하나가 남는다**(만료 시각까지). 로그인 경로도 같은 모양이다
        //     (INSERT 뒤 refresh_tokens 쓰기가 터지면 같은 잔여 행이 남는다) — 새 구조를 만들지 않고
        //     그 선례와 같은 모양을 유지했다. 잔여 행은 `expires_at` 으로 자연 소멸한다.
        var sessionRecorded = true;   // 세션 번호를 이어받은 경우는 행이 이미 있다(아래 UPDATE 가 민다).
        if (isNewSession)
        {
            // 🔴 2026-09-27 20260927작2 PM 결재 조건 C-5 — **항상 'mobile' 을 적던 자리다.**
            //
            //   [무엇이 문제였나] 종전 `NormalizeDeviceKind(null)` 은 **무조건 'mobile'** 이었다.
            //     그 행은 축 B 의 `device_kind='pc'` 조회(EnforceSinglePcLoginAsync)에 안 잡히므로,
            //     **옛 토큰으로 /refresh 한 번이면 로그인을 거치지 않고 차단 밖으로 나갔다.**
            //
            //   [지금] 갱신 요청에는 클라이언트 신고값이 없다 ⇒ **서버가 읽은 User-Agent 단독 판정**이다.
            //   🚫 여기에 신고값 칸을 새로 만들지 않는다 — 자진신고 입구를 하나 더 파는 일이다(C-5).
            //   ⚠️ UA 가 없거나(빈 문자열 포함) Mac 처럼 못 가리는 경우엔 종전과 같이 싼 칸으로 떨어진다
            //     (한계는 이 파일 `:119` 이하 참조 — UA 를 침묵시키면 판정이 성립하지 않는다).
            var refreshDeviceType = DeviceTypeResolver.ResolveDeviceType(null, request.UserAgent);
            var refreshDeviceKind = DeviceTypeResolver.ToSessionDeviceKind(refreshDeviceType);

            // ── D-3 — 새 세션을 만드는 갱신도 **축 B 판정을 거친다** ──────────────────────
            //
            //   [무엇이 문제였나] 종전에는 `if (!isNewSession)` 조건 때문에 `sid` 없는 토큰으로 갱신하면
            //     **축 B 판정을 한 번도 안 거치고** `'pc'` 세션 행이 하나 더 생겼다 ⇒ 살아 있는 다른 PC 가
            //     있어도 **PC 2대 공존이 로그인을 거치지 않고 성립**했다([4] D-3).
            //     🔴 그 `sid` 없는 토큰은 절B(위 D-2)가 **만들어 내는 것**이기도 하다 —
            //     봉합 하나가 다른 봉합의 구멍을 상시화했다.
            //
            //   🔴 **판정 자리를 새로 만들지 않았다** — 로그인이 쓰는 `EnforceSinglePcLoginAsync` 를 그대로 부른다
            //     (판정이 둘이면 한쪽만 고쳐지는 사고가 난다 · 작지 §3 금지 #2).
            //   🔴 **밀어내기는 하지 않는다**(`force: false`). 갱신에는 사용자가 없다 — 화면이 자동으로 돈다.
            //     남의 세션을 조용히 끊으면 반자동 원칙 위반이다(사장님 전결).
            //   ⇒ 다른 PC 가 살아 있으면 **되살리지 않고 401**. 재로그인 화면에서 사용자가 그 자리에서 고른다
            //     (409 + [그 PC 접속을 끊고 여기서 사용하기]). 절C(`GuardExpiredPcSessionRevivalAsync`)가
            //     만료 세션에 대해 이미 하는 것과 **같은 규칙**이고, 고객에게 하는 말도 같은 문장이다.
            //   🔴 `sid` 없는 **옛 토큰 자체는 차단하지 않는다**(작지 §3 금지 #1b) — 통과는 시키고,
            //     **새 PC 세션 행을 만드는 순간에만** 판정을 거친다.
            try
            {
                await EnforceSinglePcLoginAsync(conn, user, refreshDeviceKind, force: false);
            }
            catch (ConcurrentPcLoginException ex)
            {
                // 갱신 엔드포인트는 401 만 고객 언어로 번역한다(AuthController.Refresh) —
                //   409 는 로그인 화면에만 있는 선택지다. 그래서 절C 와 같은 문장으로 바꿔 던진다.
                System.Diagnostics.Trace.TraceWarning(
                    $"[축B] 갱신이 새 PC 세션을 만들려 했으나 다른 PC 가 살아 있다(401 · 재로그인 유도) user={user.Id}, 이전 사용={ex.OtherPcLastActiveAtUtc:O}");
                throw new UnauthorizedAccessException(
                    "다른 컴퓨터에서 사용 중이어서 접속이 만료되었습니다. 다시 로그인해 주세요.");
            }

            sessionRecorded = await InsertSessionAsync(conn, sessionId!, user, refreshDeviceKind);
        }

        // 🔴 세션 기록이 실패했으면 `sid` 를 **싣지 않는다**(D-2 · 로그인 경로 `:166` 과 같은 취급).
        //   `sid` 없는 토큰은 미들웨어가 종전처럼 통과시킨다(옛 토큰 호환 경로 · 작지 §3 금지 #1b).
        var response = CreateLoginResponse(
            user, employee, secret, redirectToWelcome: false, sessionRecorded ? sessionId : null);

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
        // ── 보상 삭제로 감싼다 (20260927작2 **3차** · 2차 개발명세서 §7 위험 · PM 결재) ──────────
        //
        //   🔴 [무엇이 위험했나] 2차에서 세션 INSERT 를 이 트랜잭션 **앞**으로 올렸다 — `sid` 를 실을지
        //     말지를 알려면 그 자리여야 한다(D-2). 그래서 **회전이 실패해 401 을 던지면** 방금 넣은
        //     `pc` 세션 행이 **아무도 쓰지 않는 채 만료까지 남는다.** 축 B 를 켠 고객은
        //     **자기 잔여 행 때문에 자기 재로그인이 409** 가 된다 — 자기 자신에게 막히는 모양이다.
        //
        //   ⇒ 회전이 실패하면 **이 갱신이 직접 만든 그 행 하나만** 지운다.
        //   🔴 순서를 되돌리지 않는다 — INSERT 를 회전 뒤로 옮기면 실패를 알기 전에 `sid` 가 실린다(2차 제약).
        //   🔴 이어받은 세션(`isNewSession == false`)은 **건드리지 않는다.** 이 호출이 만든 행이 아니고,
        //     지우면 **일하고 있는 PC 를 갱신 실패 한 번으로 끊는다.**
        //   🔴 트랜잭션 안으로 넣지 않는다 — 부르는 자리는 **롤백이 끝난 뒤**의 `catch` 두 곳이다.
        //     (회전 실패 경로는 그 둘뿐이다: 단일사용 거부 401 · 그 밖의 예외)
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
                // 🔴 3차 보상 삭제 — 이 갱신이 만든 세션 행이 주인 없이 남지 않게 한다(위 설명).
                await CompensateNewSessionRowAsync(
                    conn, user, isNewSession && sessionRecorded ? sessionId : null, "단일사용 거부(401)");
                throw; // 단일사용 거부는 이미 롤백됨 — 그대로 전파.
            }
            catch (Exception)
            {
                try { tx.Rollback(); } catch (Exception rbex) { Console.Error.WriteLine($"[AuthService] refresh 회전 롤백 실패: {rbex.Message}"); }
                // 🔴 3차 보상 삭제 — 같은 이유. 원인 예외는 바꾸지 않는다.
                await CompensateNewSessionRowAsync(
                    conn, user, isNewSession && sessionRecorded ? sessionId : null, "회전 중 예외");
                throw;
            }
        }

        // ── 세션 수명 연장 (20260927작1 절C) ──────────────────────────────────────────
        //
        //   갱신은 "아직 쓰고 있다" 는 신호다. 세션의 만료 시각을 새 access 토큰에 맞춰 민다.
        //   ⚠️ 밀어내기로 **이미 지워진 세션은 되살리지 않는다**(UPDATE 는 0행으로 끝난다) —
        //     되살리면 사용자가 끊은 PC 가 갱신 한 번으로 스스로 부활한다.
        //   ⬛ [낡은 줄 · 20260927작2 2차] *"옛 토큰(sid 없음)일 때만 행을 새로 만든다"* — 만드는 자리가
        //     여기가 아니다. D-2 봉합으로 **토큰을 굽기 전**으로 올라갔다(위 `sessionRecorded` 블록).
        //   🔴 20260927작2 절C — **만료된 PC 세션을 되살려도 되는지는 위에서 이미 판정했다**
        //     (`GuardExpiredPcSessionRevivalAsync`). 끊어야 하는 경우엔 이 줄까지 오지 않는다(401).
        //     여기 UPDATE 는 *"되살려도 되는 경우"* 만 통과해서 온다.
        try
        {
            // 🔴 20260927작2 **2차 봉합**(D-2) — 여기 있던 `isNewSession` **INSERT 분기는 위로 올라갔다**
            //   (토큰을 굽기 전 · `CreateLoginResponse` 앞). 이 자리에서 넣으면 실패를 알기 전에
            //   이미 `sid` 가 실려 **행 없는 `sid` 토큰**이 나간다(= 보호 API 전면 401).
            //   C-5(갱신 시 기기 종류 판정) 서술도 그 자리로 함께 옮겼다.
            //   ⇒ 여기 남은 것은 **이어받은 세션의 수명 연장**뿐이다.
            if (!isNewSession)
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

    /// <param name="sessionId">
    /// 🔴 세션 번호. <b><c>null</c> 이면 <c>sid</c> 클레임을 아예 넣지 않는다</b> (20260927작2 절B).
    /// <para>
    /// 세션 행을 못 남겼을 때 번호를 실으면, 미들웨어가 그 번호로 행을 찾다 못 찾아
    /// <b>모든 요청을 401 로 끊는다</b>([4] 반려 F-1). <c>sid</c> 없는 토큰은 종전 경로로
    /// 그냥 통과한다(옛 토큰 호환 · 작지 §3 금지 #1b) ⇒ 잠금이 원리적으로 생기지 않는다.
    /// </para>
    /// </param>
    private static LoginResponse CreateLoginResponse(
        User user, Employee? employee, string secret, bool redirectToWelcome, string? sessionId)
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

        // 🔴 세션 번호 (DB-127 축 B · 20260927작1 절H)
        //   이것이 없으면 **PC1 과 PC2 를 못 가른다.** user_id 만으로는
        //   "이 사용자 세션이 있나" 는 답해도 "**이 PC** 의 세션이 살아 있나" 는 못 답한다.
        //   SessionValidityMiddleware 가 이 값으로 밀어내기를 즉시 반영한다.
        //   ⚠️ 비밀이 아니다 — 무작위 GUID 이고, 이 값만으로는 아무것도 못 한다.
        //
        //   🔴 20260927작2 절B — **행이 없으면 번호도 없다.** 세션 기록이 실패했을 때
        //     번호를 실으면 미들웨어가 못 찾아 전면 잠금이 된다(F-1). 그때는 클레임을 생략한다.
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            accessClaims.Add(new Claim("sid", sessionId));
        }

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

        // 🔴 갱신 토큰도 세션 번호를 들고 다닌다 — 안 그러면 8시간마다 갱신할 때
        //   **자기가 어느 세션인지 잊어버려** 새 세션 행이 계속 쌓인다.
        //   (옛 갱신 토큰에는 이 값이 없다 — 그때는 새 번호를 만든다. RefreshAsync 참조)
        //   🔴 20260927작2 절B — 세션 기록 실패 시엔 여기에도 안 넣는다(위 access 토큰과 같은 이유).
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            refreshClaims.Add(new Claim("sid", sessionId));
        }

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
