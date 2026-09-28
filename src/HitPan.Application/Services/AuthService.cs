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
        //         - `User-Agent` 를 **안 보내거나 공백**으로 두면 `DeviceTypeResolver.JudgeTypeFromUserAgent`
        //           의 첫 줄(`IsNullOrWhiteSpace(userAgent) → null`)이 판정을 포기하고,
        //           `DeviceTypeResolver.ResolveDeviceType` 의 `if (judged is null) return normalized ?? "mobile"`
        //           이 **신고값을 그대로 쓴다**(신고값도 없으면 `?? "mobile"` — 싼 칸).
        //         - **Mac 계열 UA** 는 `JudgeTypeFromUserAgent` 의 **마지막 `return null`**(어느 토큰에도
        //           안 맞는 fall-through)이 8/10 아이패드 사고 재발 방지를 위해
        //           **의도적으로 판정을 포기**한다 ⇒ 같은 길로 빠져나간다.
        //       🔴 20260927작2 **5차** — 바로 위 세 자리는 2차까지 줄번호(`DeviceTypeResolver.cs:176`·`:136`·
        //         `:191-197`)로 적혀 있었다. 2차가 **같은 파일에 주석 15줄을 얹어** 커밋하는 순간 이미
        //         어긋났다([4] R-7 실측: 실제는 `:191`·`:151`·`:204-212`). ⇒ 줄번호를 버리고
        //         **함수·식 이름**으로 가리킨다. 주석이 밀려도 안 어긋나는 것이 근본 해결이다.
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
        // 🔴 20260928작2 [4] N-6 — PI-7(갱신 경로 「UA 가 비면 `pc`」)을 **로그인에도 대칭으로** 둔다.
        //   UA 가 비면 위 판정이 신고값을 그대로 쓴다(`?? "mobile"`) ⇒ UA 를 안 보내고 신고값 `mobile`(또는 없음)이면
        //   직접 API 호출 한 번으로 PC 1대 차단 밖 세션이 생겼다. 판정을 포기한 경우는 **세는 쪽**이다(갱신과 같은 규칙).
        //   브라우저는 UA 를 늘 보낸다(컨트롤러가 헤더로 덮어쓴다) — 실사용 화면 영향 없음.
        //   ⚠️ Mac UA(판정 포기 fall-through)는 여전히 신고값·싼 칸이다(갱신과 같은 기록 · 설계 §13-6).
        //   ⚠️ 이 갈래를 빼면 G-B33b 가 FAIL 한다(새 행 `mobile`).
        if (string.IsNullOrWhiteSpace(request.UserAgent))
        {
            deviceKind = "pc";
        }
        // ⬛ [낡은 줄 · 20260927작1] `await EnforceSinglePcLoginAsync(loginDb, user, deviceKind, request.ForceSignOutOtherPc);`
        // 🔴 20260928작2 절B (K-4 다 · 사장님 원문 *"「다른 PC에서 사용 중입니다」 안내가 뜨고, 로그인 불가."*)
        //   — **먼저 들어온 PC 가 이긴다.** `request.ForceSignOutOtherPc` 는 **읽지 않는다**
        //   (옛 화면 1.3.46 캐시가 true 를 보내도 앞 PC 를 끊을 길이 없다 · 설계 §3-1).
        //   두 번째 인자 `isLogin: true` 는 「로그인 경로다 — 409 가 아니면 죽은 PC 세션의 refresh 만 정리한다」는 뜻이다.
        //
        // 🔴 20260928작2 절J (개정2 · 설계 §13-3 · PI-3) — **판정 앞에서 계정 잠금**(`GET_LOCK`)을 잡는다.
        //   같은 계정 PC 로그인 두 개가 동시에 오면 둘 다 판정을 통과해 PC 2대가 섰다.
        //   ⚠️ 해제 시점 = 이 메서드 끝(`await using`). 설계 §13-3 은 「세션 INSERT 뒤 해제」라 적었으나,
        //     절L 술어가 「그 세션의 쓸 수 있는 refresh 있음」까지 보므로 **refresh INSERT 전에 풀면 두 번째 로그인이
        //     앞 로그인을 못 센다**(행은 있는데 refresh 가 아직 없다) ⇒ refresh 저장까지 잡고 있는다(개발명세서 ⚠️ 기록).
        //   ⚠️ 이 잠금을 빼면 G-B29 가 FAIL 한다(둘 다 성공 · PC 행 2).
        var (loginLock, loginBusy) = await TryAcquireLoginLockAsync(loginDb, user, "로그인", ct);
        await using var loginLockScope = loginLock;
        if (loginBusy)
        {
            // 고객 언어(작지 절J 원문). 같은 계정 로그인이 5초 넘게 처리 중인 드문 경우다.
            throw new UnauthorizedAccessException("잠시 후 다시 시도해 주세요.");
        }

        try
        {
            await EnforceSinglePcLoginAsync(loginDb, user, deviceKind, isLogin: true);
        }
        catch (ConcurrentPcLoginException blocked)
        {
            // 🔴 20260928작2 절B-3 (K-5 가) — **막은 시도를 남긴다**(`security_alerts` `pc_login_blocked`).
            //   비밀번호 확인 **뒤**라 맞는 비밀번호의 시도만 남는다(틀린 비밀번호는 위에서 이미 401).
            //   기록 실패는 409 를 바꾸지 않는다(함수 안에서 경고 로그 · #15). 갱신 경로는 기록하지 않는다.
            await RecordPcLoginBlockedAsync(loginDb, user, blocked.OtherPcLastActiveAtUtc);
            throw;
        }

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
        // 🔴 20260928작2 절D (F-4) — 보상 인자를 **한 점**으로 둔다(갱신 경로 `sessionIdCreatedHere` 와 같은 모양).
        //   세션 기록이 실패했으면 지울 행이 없다 ⇒ null ⇒ `CompensateNewSessionRowAsync` 첫 줄이 즉시 반환.
        var sessionIdCreatedHere = sessionRecorded ? sessionId : null;

        // ⬛ [낡은 줄 · 20260928작2 이전] 아래 두 쓰기(토큰 생성 · refresh 저장)에 보상이 **0** 이었다.
        //   refresh INSERT 가 터지면 방금 넣은 `pc` 세션 행이 주인 없이 만료(8h)까지 남고, 켜진 고객사에서는
        //   **본인 재로그인이 자기 잔여 행 때문에 409** 가 된다 — K-4 (다)로 탈출 버튼이 없어져 최대 8h 잠긴다(작지 §6).
        // 🔴 [지금] 두 쓰기를 `try` 로 감싸 실패하면 **이 로그인이 만든 세션 행 하나만** 되돌리고 원인 예외를 그대로 던진다.
        //   트랜잭션은 새로 씌우지 않는다 — 전삭이 빠져 refresh 쓰기가 INSERT 한 문장이 됐다(설계 §4).
        LoginResponse response;
        try
        {
            response = CreateLoginResponse(
                user, employee, secret, redirectToWelcome, sessionIdCreatedHere);

            // refresh token DB 저장 — 로그아웃 is_revoked=1 차단의 기준
            //   ⚠️ `response` 를 쓰므로 토큰 생성 뒤여야 한다(설계 §4-2 순서 4).
            // ⬛ [낡은 줄 · 20260928작2 절B 에서 제거] 이전 토큰 정리 후 새 토큰 INSERT —
            //   `DELETE FROM refresh_tokens WHERE user_id = @UserId`
            //   [무엇이 틀렸나] 계정의 refresh 를 **전부** 지웠다 ⇒ 휴대폰 로그인 한 번에 일하던 PC 가
            //   갱신 때(8h 뒤) 튕겼다(선행검증 §1 · L). 사장님 원문 *"로그인 할떄, 다른기기를 왜 지워?"*
            //   ⇒ 로그인은 **남의 토큰을 지우지 않는다.** 예외는 이미 만료된 내 PC 세션의 refresh 정리뿐이고
            //     그것은 `EnforceSinglePcLoginAsync(isLogin: true)` 한 곳에서만 한다(설계 §3-1).
            // 🔴 `session_id` = 이 refresh JWT 의 `sid`(불변식 · 설계 §2). `sid` 를 안 실었으면 NULL.
            //   ⚠️ 이 줄을 빼면 로그아웃(`WHERE user_id AND session_id`)이 이 토큰을 못 가른다 — G-B18 ⓑ.
            await db.ExecuteAsync(
                @"INSERT INTO refresh_tokens (token_id, user_id, token_hash, expires_at, is_revoked, session_id)
                  VALUES (@TokenId, @UserId, @TokenHash, @ExpiresAt, 0, @SessionId)",
                new
                {
                    TokenId = Guid.NewGuid().ToString(),
                    UserId = user.Id,
                    TokenHash = HashToken(response.RefreshToken),
                    ExpiresAt = DateTime.UtcNow.Add(RefreshTokenLifetime),
                    SessionId = sessionIdCreatedHere
                });
        }
        catch (Exception)
        {
            // 🔴 F-4 보상 — 이 로그인이 만든 세션 행 하나만(`WHERE session_id`). 원인 예외는 바꾸지 않는다.
            //   ⚠️ 이 호출을 빼면 G-B24 가 FAIL 한다(잔여 행 1 · 재로그인 409).
            await CompensateNewSessionRowAsync(
                db, user, sessionIdCreatedHere, "로그인 토큰 생성·refresh 저장 중 예외");
            throw;
        }

        // 🔴 20260928작2 절B — 만료 행 청소. 갱신 경로의 「만료 잔여 행 정리(F4)」와 **같은 술어**다.
        //   종전 전삭이 하던 「쌓이지 않게」 몫을 이 한 줄이 받는다. 핵심 경로 밖이다 —
        //   실패해도 로그인은 이미 성립했다(경고 로그 · #15 · #20).
        try
        {
            await db.ExecuteAsync(
                "DELETE FROM refresh_tokens WHERE user_id = @UserId AND expires_at < @Now",
                new { UserId = user.Id, Now = DateTime.UtcNow });
        }
        catch (Exception cleanEx)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"[로그인] 만료 refresh 청소 실패(로그인은 진행) user={user.Id}: {cleanEx.Message}");
        }

        // 🔴 20260928작2 절E — 접속기록 1행(`audit_trail` · `user_session` · `login`).
        //   테넌트·사용자는 **DB 의 사용자 행**에서만(#2). 실패는 함수 안에서 경고 로그만 — 로그인을 막지 않는다.
        //   ⚠️ 이 호출을 빼면 G-B25 가 FAIL 한다(0행).
        await SessionAccessTrail.WriteAsync(
            db, user.TenantId, user.Id, SessionAccessTrail.ActionLogin,
            sessionIdCreatedHere, deviceKind, request.UserAgent);

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
    /// 🔴 「살아 있는 PC 접속」 술어 — <b>한 곳</b> (20260928작2 절L · 개정2 · 설계 §13-5 · PI-5).
    /// </summary>
    /// <remarks>
    /// <para>로그인 판정(<c>EnforceSinglePcLoginAsync</c>)과 갱신 가드 ⑦(<c>GuardExpiredPcSessionRevivalAsync</c>)이
    /// <b>이 상수 하나</b>를 쓴다. 별칭 <c>s</c> = <c>user_sessions</c>.</para>
    /// <para>세션 행 생존 <b>AND 그 <c>session_id</c> 로 쓸 수 있는 refresh 가 있다</b>. 쓸 수 있는 refresh 가 없는 접속은
    /// 아무도 이어 쓸 수 없다 ⇒ 보상 실패 잔여 행·로그아웃 반쪽 실패(refresh 폐기 뒤 행 삭제 실패)가 <b>본인을 잠그지 않는다.</b></para>
    /// <para>⚠️ 대가: 갱신 회전 INSERT 가 <c>session_id</c> 를 빠뜨리면 차단이 <b>조용히</b> 꺼진다 ⇒ G-B32 가 잰다.
    /// 1.3.46 이 발급한 refresh(<c>session_id</c> NULL)를 든 PC 는 첫 갱신 전까지 안 센다(그 판은 차단이 꺼져 있었다 — 후퇴 아님).</para>
    /// <para>⚠️ <c>refresh_tokens.expires_at</c> 은 앱이 <c>DateTime.UtcNow</c> 로 적는 UTC 다 ⇒ DB 의 <c>UTC_TIMESTAMP(6)</c> 와 비교한다.</para>
    /// </remarks>
    private const string LivePcSessionPredicate =
        @"s.device_kind = 'pc' AND s.expires_at > UTC_TIMESTAMP(6)
          AND EXISTS (SELECT 1 FROM refresh_tokens rt
                       WHERE rt.user_id = s.user_id AND rt.session_id = s.session_id
                         AND rt.is_revoked = 0 AND rt.expires_at > UTC_TIMESTAMP(6))";

    /// <summary>
    /// 🔴 같은 계정의 로그인 판정~기록을 <b>한 번에 하나</b>로 줄 세운다 (20260928작2 절J · 설계 §13-3 · PI-3).
    /// </summary>
    /// <remarks>
    /// <para>[무엇을 막나] 같은 계정 PC 로그인 두 개가 동시에 오면 둘 다 판정을 통과한 뒤 각자 세션을 넣는다 ⇒ PC 2대.</para>
    /// <para>MariaDB 이름 잠금 <c>GET_LOCK</c> — 시그니처·트랜잭션 구조 변경 0. <b>연결을 명시적으로 연다</b> —
    /// 닫힌 연결이면 Dapper 가 명령마다 열고 닫아 잠금이 곧 풀린다. 한 연결 위에서 순차로만 쓴다(#16).</para>
    /// <para>결과: <c>1</c> 잡음 · <c>0</c>(5초 안에 못 잡음) = <c>Busy</c> — 호출부가 경로에 맞는 예외를 던진다 ·
    /// <c>NULL</c>/예외 = 경고 후 잠금 없이 진행(가용성 — 판정과 같은 원칙).</para>
    /// <para>⚠️ <c>FOR UPDATE</c>·새 트랜잭션은 쓰지 않는다(작지 절J).</para>
    /// </remarks>
    private static async Task<(LoginLock? Lock, bool Busy)> TryAcquireLoginLockAsync(
        System.Data.Common.DbConnection db, User user, string where, CancellationToken ct)
    {
        int? got;
        try
        {
            if (db.State != System.Data.ConnectionState.Open)
            {
                await db.OpenAsync(ct).ConfigureAwait(false);
            }
            got = await db.ExecuteScalarAsync<int?>(
                "SELECT GET_LOCK(CONCAT('hp_login_', @UserId), 5)", new { UserId = user.Id });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"[축B] {where} 계정 잠금 실패(잠금 없이 진행) user={user.Id}: {ex.Message}");
            return (null, false);
        }

        if (got == 1) return (new LoginLock(db, user.Id, where), false);
        if (got == 0) return (null, true);

        System.Diagnostics.Trace.TraceWarning(
            $"[축B] {where} 계정 잠금 결과 NULL(잠금 없이 진행) user={user.Id}");
        return (null, false);
    }

    /// <summary><c>GET_LOCK</c> 을 잡은 동안만 사는 표 — <c>await using</c> 이 끝나면 <c>RELEASE_LOCK</c> (절J).</summary>
    /// <remarks>⚠️ 해제 실패는 삼키지 않고 경고한다(#15). 풀리지 않아도 5초 기다림 뒤 다음 로그인이 <c>0</c> 을 받을 뿐이고, 연결이 닫히면 서버가 푼다.</remarks>
    private sealed class LoginLock : IAsyncDisposable
    {
        private readonly System.Data.Common.DbConnection _db;
        private readonly string _userId;
        private readonly string _where;

        public LoginLock(System.Data.Common.DbConnection db, string userId, string where)
        {
            _db = db;
            _userId = userId;
            _where = where;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _db.ExecuteScalarAsync<int?>(
                    "SELECT RELEASE_LOCK(CONCAT('hp_login_', @UserId))", new { UserId = _userId });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning(
                    $"[축B] {_where} 계정 잠금 해제 실패 user={_userId}: {ex.Message}");
            }
        }
    }

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
    /// <para>🔴 <b>20260928작2 절B (K-4 다)</b> — <b>먼저 들어온 PC 가 이긴다.</b> 살아 있는 다른 PC 가 있으면
    /// <b>무조건 409</b> 다. ⬛ 종전 매개변수 <c>force</c>(사용자가 [끊고 여기서 사용] 을 누르면 앞 PC 를 지웠다)는
    /// <b>없앴다</b> — 사장님 원문 <i>"「다른 PC에서 사용 중입니다」 안내가 뜨고, 로그인 불가."</i></para>
    /// <para>🔴 <paramref name="isLogin"/> — 로그인(<c>true</c>)이고 409 가 아니면 <b>이미 만료된 내 PC 세션의 refresh 만</b>
    /// 폐기한다(설계 §3-1 · 세션 행은 지우지 않는다). 갱신(<c>false</c>)은 정리하지 않는다 —
    /// 사용자가 없는 자동 동작이 남의 토큰을 건드리지 않는다.</para>
    /// </remarks>
    private static async Task EnforceSinglePcLoginAsync(
        System.Data.IDbConnection db, User user, string deviceKind, bool isLogin)
    {
        if (!string.Equals(deviceKind, "pc", StringComparison.Ordinal)) return;   // 모바일은 대상 아님

        try
        {
            // ⬛ [낡은 판독 · 20260927작2 절D — 1.3.46 까지] 옛 칸 `enforce_single_pc_login` 을 `?? 0`(행 없음 = 끔)으로 읽었다.
            //   그 아래 주석(「행이 없을 때의 폴백을 `?? 1` → `?? 0` 으로 내렸다」)은 **옛 칸의 사정**이다.
            // 🔴 20260928작2 절B — 스위치를 **새 칸 `enforce_one_pc_login`(DB-131 · 기본 1)** 으로 옮겼다(K-1 가).
            //   옛 칸은 읽지 않는다 — 1.3.46 재게시가 곧 「꺼진 상태 그대로」의 되돌림이 되게 하려는 것이다(#37 · 무접촉).
            //   🔴 **행 없음 = 켬(`?? 1`)** — K-2 (가) · 사장님 원문 *"pc접속은 **무조건** 1대"*.
            //     `tenant_settings` 행을 만드는 곳은 설정 저장 하나뿐이라, `?? 0` 이면 설정을 한 번도 저장 안 한
            //     고객사가 **조용히** 안 켜진다(설계 §5). 명시적 `0` 만 끔(비상 스위치).
            //   ⚠️ 칸이 없어 조회가 터지면(DB-131 미적용) 아래 가용성 폴백 = 로그인 진행(끔) — 종전과 같다.
            //   ⚠️ 이 `?? 1` 을 `?? 0` 으로 되돌리면 G-B21 ① 이 FAIL 한다.
            // 킬스위치 — 사고 시 배포 없이 끌 수 있어야 한다(#21 로 appsettings 불가).
            //
            //   🔴 2026-09-27 20260927작2 절D — 행이 없을 때의 폴백을 `?? 1`(켬) → **`?? 0`(끔)** 으로 내렸다.
            //     [왜] 축 B 는 이번 차수에 **기본 OFF 로 출하**한다(사장님 전결 §1 — 선례 수준 도달 확인 후
            //       별도 결재로 켠다). `tenant_settings` 행이 없는 고객사(마이그가 안 붙은 DB 등)에서
            //       `?? 1` 은 **고객이 끄지도 못하는 상태로 켜진다** — 설정 화면에 쓸 행 자체가 없다.
            //     ⇒ 모르면 끈 쪽이다. 같은 방향으로 DB 기본값도 0 으로 내린다(절G · DB-128).
            // ⬛ [낡은 줄] `SELECT enforce_single_pc_login FROM tenant_settings WHERE tenant_id = @TenantId` … `?? 0`
            var enabled = await db.ExecuteScalarAsync<int?>(
                "SELECT enforce_one_pc_login FROM tenant_settings WHERE tenant_id = @TenantId",
                new { TenantId = user.TenantId }) ?? 1;
            if (enabled == 0) return;

            // ⬛ [낡은 문장 · 20260928작2 절B 까지] `SELECT last_active_at FROM user_sessions
            //     WHERE user_id = @UserId AND device_kind = 'pc' AND expires_at > UTC_TIMESTAMP(6) …`
            //   — 세션 **행**만 살아 있으면 셌다 ⇒ 보상 실패 잔여 행·로그아웃 반쪽 실패가 본인을 최대 8h 잠갔다(PI-5).
            // 🔴 20260928작2 절L (개정2 · 설계 §13-5) — 술어는 **`LivePcSessionPredicate` 한 곳**(가드 ⑦ 과 공유).
            //   ⚠️ 이 술어에서 EXISTS 를 빼면 G-B31·G-B30 ⓑ 가 FAIL 한다.
            var other = await db.QueryFirstOrDefaultAsync<DateTime?>(
                @"SELECT s.last_active_at FROM user_sessions s
                   WHERE s.user_id = @UserId AND " + LivePcSessionPredicate + @"
                   ORDER BY s.last_active_at DESC LIMIT 1",
                new { UserId = user.Id });

            if (other is not null)
            {
                // 🔴 20260928작2 절B (K-4 다) — **무조건 거절한다.** 앞 PC 를 끊는 길은 없다.
                //   풀리는 경우는 앞 PC 의 로그아웃 또는 앞 PC 세션 만료(마지막 사용 + 8h)뿐이다
                //   (쿨타임은 별도 트랙 1-b · 설계 §9-1).
                throw new ConcurrentPcLoginException(other.Value);
            }

            // ⬛ [낡은 갈래 · 20260927작1~작2 · 1.3.46 까지 — 20260928작2 절B 에서 제거]
            //   `if (!force) throw …;` 아래에서 사용자가 [그 PC 접속을 끊고 여기서 사용하기] 를 누르면
            //   `DELETE FROM user_sessions WHERE user_id = @UserId AND device_kind = 'pc'` 로 앞 PC 를 밀어내고
            //   `security_alerts` 에 `pc_session_forced_out` 1행을 남겼다(20260927작2 절D P1-2).
            //   [왜 없앴나] 사장님 9/28 K-4 원문 *"「다른 PC에서 사용 중입니다」 안내가 뜨고, 로그인 불가."*
            //     ⇒ 9/25 「뒤가 들어오면 앞이 나간다」와 그 버튼을 **뒤집었다.** `pc_session_forced_out` 은
            //     새로 생길 일이 없다(옛 행은 DB 에 그대로). ⚠️ 이 갈래를 되살리면 G-B26·G-B9 가 FAIL 한다.

            // 🔴 20260928작2 절B — **죽은 PC 정리**(설계 §3-1). 로그인이고, 409 가 아닐 때만.
            //   살아 있는 다른 PC 가 없다 = 있던 PC 세션은 **이미 만료**다(로그아웃 없이 자리를 떠 8h 지남 등).
            //   그 죽은 세션들의 refresh 만 폐기한다 ⇒ 앞 PC 가 갱신으로 되살아날 길이 **가드의 가용성 폴백과 무관하게** 닫힌다
            //   (가드 `GuardExpiredPcSessionRevivalAsync` 는 판정 실패 시 되살리는 쪽이다 — DB 순간 오류 한 번이면 PC 2대).
            //   🔴 조건상 못 건드리는 것: 모바일(`device_kind = 'pc'`) · 살아 있는 세션(`expires_at <= 지금`) ·
            //     자기 세션(새 세션 INSERT 보다 **앞**이다). 세션 **행**은 지우지 않는다 — 가드 ⑦ 과 접속 현황이 읽는다.
            //   🔴 갱신 경로(`isLogin == false`)는 하지 않는다 — 사용자가 없는 자동 동작이 남의 토큰을 건드리지 않는다.
            //   ⚠️ 실패는 아래 가용성 폴백(경고 · 로그인 진행). 폐기된 refresh 는 로그인이 실패해도 되돌리지 않는다
            //     — 이미 만료된 세션의 것이라 잃는 것이 없다(설계 §4).
            //   ⚠️ 이 줄을 빼면 G-B20 ⓐ, `device_kind` 조건을 빼면 G-B20 ⓑ 가 FAIL 한다.
            if (isLogin)
            {
                await db.ExecuteAsync(
                    @"UPDATE refresh_tokens SET is_revoked = 1
                       WHERE user_id = @UserId AND is_revoked = 0
                         AND session_id IN (SELECT session_id FROM user_sessions
                                             WHERE user_id = @UserId AND device_kind = 'pc'
                                               AND expires_at <= UTC_TIMESTAMP(6))",
                    new { UserId = user.Id });
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

    /// <summary>
    /// 두 번째 PC 로그인을 막은 사실을 남긴다 (20260928작2 절B-3 · K-5 가 · 설계 §6).
    /// </summary>
    /// <remarks>
    /// <para>9/25 §2 「시도 발생 시 <c>security_alerts</c> 기록 → 화면 경고」. 등록기기관리 「경고」가 읽는다(설계 §10).</para>
    /// <para>모양은 옛 <c>pc_session_forced_out</c> INSERT 와 같다(<c>alert_type varchar(50)</c>). 설명은 고객 언어로 적는다.</para>
    /// <para>🔴 <b>예외를 던지지 않는다</b> — 기록 실패가 409 를 다른 응답(500)으로 바꾸면 화면이
    /// 「다른 PC에서 사용 중」을 못 보여 준다. 조용히도 넘기지 않는다(#15).
    /// ⚠️ 이 함수의 <c>catch</c> 를 빼면 G-B27 의 「쓰기 실패 트리거에도 409」가 FAIL 한다.</para>
    /// </remarks>
    private static async Task RecordPcLoginBlockedAsync(
        System.Data.IDbConnection db, User user, DateTime otherPcLastActiveAtUtc)
    {
        try
        {
            await db.ExecuteAsync(
                @"INSERT INTO security_alerts (alert_id, tenant_id, user_id, alert_type, description)
                  VALUES (@Id, @TenantId, @UserId, 'pc_login_blocked', @Desc)",
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    TenantId = user.TenantId,
                    UserId = user.Id,
                    Desc = $"다른 PC 가 사용 중이라 로그인을 막았습니다(그 PC 마지막 사용 {otherPcLastActiveAtUtc:yyyy-MM-dd HH:mm} UTC)"
                });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"[축B] 두 번째 PC 로그인 차단 기록 실패(응답은 409 그대로) user={user.Id}: {ex.Message}");
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
            // ⬛ [낡은 값 · 20260928작2 절M 이전] `expires_at = @ExpiresAt`(`DateTime.UtcNow.Add(AccessTokenLifetime)` — 앱 시계).
            // 🔴 20260928작2 절M (PI-8 · 설계 §13-6) — 비교(`> UTC_TIMESTAMP(6)`)와 **같은 시계(DB)** 로 적는다.
            //   앱·DB 시계가 어긋나면 막 넣은 행이 「이미 만료」로 읽히거나 8h 를 넘겨 산다.
            //   ⚠️ `8 HOUR` 는 `AccessTokenLifetime`(8h)과 같아야 한다 — 한쪽만 바꾸지 마라.
            await db.ExecuteAsync(
                @"INSERT INTO user_sessions (session_id, user_id, tenant_id, login_at, last_active_at, expires_at, device_kind)
                  VALUES (@SessionId, @UserId, @TenantId, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6),
                          UTC_TIMESTAMP(6) + INTERVAL 8 HOUR, @DeviceKind)",
                new
                {
                    SessionId = sessionId,
                    UserId = user.Id,
                    TenantId = user.TenantId,
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
            // ⬛ [낡은 줄 · 20260928작2 절L 이전] `db.ExecuteAsync("DELETE …", …)` — 원인과 **같은 연결**로 지웠다.
            //   연결이 상한 것이 원인이면 보상도 같이 죽었다(PI-5 · 작지 §6 「G-B24 가 유일한 방어」 정정).
            // 🔴 20260928작2 절L (설계 §13-5 ①) — 복제할 수 있는 연결이면 **새 연결**로 지운다. 못 하면 종전 연결.
            var removed = await DeleteSessionRowOnFreshConnectionAsync(db, sessionIdCreatedHere);

            // ⬛ [낡은 머리말] 「갱신 회전 실패」 — 20260928작2 절D 부터 로그인 경로도 이 함수를 부른다(두 경로 공용).
            System.Diagnostics.Trace.TraceWarning(
                $"[세션] 토큰 발급 실패({reason}) — 방금 만든 세션 행을 되돌렸다(삭제 {removed}건) "
                + $"user={user.Id}, session={sessionIdCreatedHere}");
        }
        catch (Exception ex)
        {
            // 🔴 삼키지만 조용히는 넘기지 않는다 — 이 행이 남으면 본인 재로그인이 409 로 막힌다.
            System.Diagnostics.Trace.TraceError(
                $"[세션] 토큰 발급 실패 후 보상 삭제 실패 — 주인 없는 세션 행이 만료까지 남는다. "
                + $"user={user.Id}, session={sessionIdCreatedHere}: {ex.Message}");
        }
    }

    /// <summary>
    /// 보상 삭제를 <b>복제한 새 연결</b>로 한다 (20260928작2 절L · 설계 §13-5 ①).
    /// </summary>
    /// <remarks>
    /// <para><c>MySqlConnection</c> 은 <c>ICloneable</c> 이다(같은 접속 문자열의 닫힌 새 연결). Dapper 가 열고 닫는다.</para>
    /// <para>🔴 복제가 안 되거나(시험용 래퍼 등) 복제 연결이 실패하면 <b>종전 연결로 한 번 더</b> 시도한다 — 새 길이 막혀도
    /// 종전보다 나빠지지 않는다. 두 번 다 실패하면 예외를 올려 호출부 <c>catch</c> 가 남은 번호를 로그에 적는다.</para>
    /// </remarks>
    private static async Task<int> DeleteSessionRowOnFreshConnectionAsync(
        System.Data.IDbConnection db, string sessionId)
    {
        const string sql = "DELETE FROM user_sessions WHERE session_id = @SessionId";

        if (db is ICloneable cloneable && cloneable.Clone() is System.Data.IDbConnection fresh)
        {
            try
            {
                using (fresh)
                {
                    return await fresh.ExecuteAsync(sql, new { SessionId = sessionId });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning(
                    $"[세션] 보상 삭제를 새 연결로 못 했다 — 종전 연결로 다시 시도 session={sessionId}: {ex.Message}");
            }
        }

        return await db.ExecuteAsync(sql, new { SessionId = sessionId });
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
    /// ⬛ [낡은 서술 · 20260927작2] 행이 아예 없으면(밀어내기로 지워짐) <b>현행 유지</b> — 되살리지 않고, 뒤의 UPDATE 가 0행으로 끝난다.
    /// </para>
    /// <para>
    /// 🔴 [20260928작1 절B] 행이 아예 없으면 — 생존 확인(<c>enforce_session_validity</c>)이 <b>켜진</b> 고객사는
    /// <b>거절(401)</b>, 꺼진 고객사는 종전대로 통과. 종전 200 은 화면을 「갇힘」으로 만들었다(선행검증 R-1).
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

            // ⬛ [낡은 줄 · 20260927작2] `if (mine is null) return;   // ② 행 없음 — 현행 유지`
            //   [무엇이 틀렸나 · 20260928작1 절B · 선행검증 R-1] 미들웨어는 *"행 없음 = 죽은 세션 = 401"* 로 답하는데
            //     갱신은 같은 사실에 **200** 을 주었다 → 회전 성공 · 아래 UPDATE 0행 · 같은 죽은 `sid` 를 다시 싣는다
            //     → 재시도 401 → 화면은 「갱신 성공」 갈래라 토큰을 안 지운다 ⇒ **로그인 화면으로도 못 가는 「갇힘」.**
            //   [지금 · 사장님 결재 P-1 = B] 생존 확인이 켜진 고객사에서는 **거절(401)** 한다 ⇒ 화면이 토큰을 지우고
            //     재로그인 1회로 끝난다. 되살리지도(작1 절C 「지워진 세션은 되살리지 않는다」 유지) 새로 만들지도 않는다.
            //   🔴 스위치가 꺼졌으면(행 없음·NULL·0) **종전 그대로 통과** — 미들웨어(`SessionValidityMiddleware`)와
            //     같은 방향이어야 비상 스위치가 「종전 동작 완전 복귀」가 된다. 판정 자리를 새로 만들지 않는다.
            //   ⚠️ 스위치 조회가 터지면(DB-129 미적용 등) 아래 `catch (Exception)` 가용성 폴백으로 간다 = 통과(종전).
            if (mine is null)                                       // ② 행 없음
            {
                var validity = await db.ExecuteScalarAsync<int?>(
                    "SELECT enforce_session_validity FROM tenant_settings WHERE tenant_id = @TenantId",
                    new { TenantId = user.TenantId });
                // ⬛ [낡은 줄 · 20260928작1] `if (validity != 1) return;` — 행 없음(NULL)도 끔으로 읽었다.
                // 🔴 20260928작2 절B-2 (K-3 나) — **행 없음 = 켬**, 명시적 `0` 만 끔. 미들웨어
                //   (`SessionValidityMiddleware.IsSessionValidityEnforcedAsync`)와 **같은 방향**으로 함께 바꿨다
                //   (한쪽만 바꾸면 미들웨어 401 · 갱신 200 의 「갇힘」이 되돌아온다 — 선행검증 R-1).
                //   사장님 원문 *"pc접속은 무조건 1대"* — 막힌 상태가 8h 남으면 「무조건」이 아니다(설계 §9-1).
                //   ⚠️ 이 줄을 `!= 1` 로 되돌리면 G-B28 ② 가 FAIL 한다.
                if (validity == 0) return;                          // 생존 확인 비상 끔 — 종전 그대로(현행 유지)

                // 고객 언어로만 말한다(개발용어 금지). 사유(로그아웃·다른 곳 접속 등)를 단정하지 않는다.
                throw new UnauthorizedAccessException("접속이 종료되었습니다. 다시 로그인해 주세요.");
            }
            if (mine.Alive == 1) return;                           // ③ 아직 살아 있다 — 그대로 연장
            if (!string.Equals(mine.DeviceKind, "pc", StringComparison.Ordinal)) return;   // ④ 모바일은 축 B 대상 아님(9/25 결재)

            // ⑤ 킬스위치 — 꺼져 있으면 되살린다.
            //   ⬛ [낡은 줄 · 1.3.46 까지] 옛 칸 `enforce_single_pc_login` · 「행이 없으면 끈 것으로 본다」(`?? 0`).
            //   🔴 20260928작2 절B — 로그인 판정(`EnforceSinglePcLoginAsync`)과 **같은 칸·같은 방향**:
            //     새 칸 `enforce_one_pc_login`(DB-131) · **행 없음 = 켬(`?? 1`)** · 명시적 `0` 만 끔(K-1 가 · K-2 가).
            var enabled = await db.ExecuteScalarAsync<int?>(
                "SELECT enforce_one_pc_login FROM tenant_settings WHERE tenant_id = @TenantId",
                new { TenantId = user.TenantId }) ?? 1;
            if (enabled == 0) return;

            // ⑥⑦ 다른 PC 가 살아 있나 — 내 세션은 빼고 본다.
            // ⬛ [낡은 문장 · 20260928작2 절B 까지] `… WHERE user_id = @UserId AND device_kind = 'pc'
            //     AND session_id <> @SessionId AND expires_at > UTC_TIMESTAMP(6) LIMIT 1`
            // 🔴 20260928작2 절L (설계 §13-5) — 로그인 판정과 **같은 술어 상수**(`LivePcSessionPredicate`).
            //   판정 자리가 둘인데 술어가 갈리면 「로그인은 들여보내고 갱신은 끊는」 어긋남이 생긴다.
            var otherPcAlive = await db.ExecuteScalarAsync<int?>(
                @"SELECT 1 FROM user_sessions s
                   WHERE s.user_id = @UserId AND s.session_id <> @SessionId
                     AND " + LivePcSessionPredicate + @"
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
        //     ⇒ 로그인 경로(`LoginAsync` 의 `InsertSessionAsync(...)` 호출 → 그 바로 뒤
        //       `CreateLoginResponse(..., sessionRecorded ? sessionId : null)`)와 **똑같이** 순서를 뒤집었다:
        //       넣기를 먼저, **성공했을 때만** 싣는다.
        //       🔴 5차 — 여기 적혀 있던 `:160·166` 은 2차가 주석 12줄을 얹어 **커밋 순간 어긋났다**
        //         ([4] R-7 실측: 실제는 `:172·178`). 줄번호 대신 함수·식 이름으로 가리킨다.
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
        // 🔴 20260928작2 절J (설계 §13-3) — 새 PC 세션을 만드는 갱신(`isLogin: false` 판정)도 **로그인과 같은 계정 잠금** 안에서 한다.
        //   이어받은 세션(대부분의 갱신)은 판정을 안 타므로 잡지 않는다. 해제 = 메서드 끝(`await using` · 회전 커밋까지).
        //   `0` 이면 401 이 아니라 **500** 으로 끝낸다 — 화면이 401 을 「지움」으로 읽으면 멀쩡한 로그인을 버린다(절I).
        var (refreshLock, refreshBusy) = isNewSession
            ? await TryAcquireLoginLockAsync(conn, user, "갱신(새 세션)", ct)
            : (null, false);
        await using var refreshLockScope = refreshLock;
        if (refreshBusy)
        {
            throw new TimeoutException("잠시 후 다시 시도해 주세요.");
        }

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

            // 🔴 20260928작2 절M (PI-7 · 설계 §13-6) — **UA 가 비어 있으면 `pc`** 로 센다.
            //   갱신에는 신고값이 없어 위 판정이 `?? "mobile"`(싼 칸)로 떨어졌다 ⇒ UA 를 비운 옛 refresh 한 번이면
            //   차단 밖의 `mobile` 세션이 생겼다. 판정을 포기한 경우는 **세는 쪽**으로 둔다.
            //   ⚠️ Mac UA(판정 포기 fall-through)는 여전히 싼 칸이다(기록 · 설계 §13-6).
            //   ⚠️ 이 갈래를 빼면 G-B33 이 FAIL 한다(새 행 `mobile`).
            if (string.IsNullOrWhiteSpace(request.UserAgent))
            {
                refreshDeviceKind = "pc";
            }

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
            //     ⬛ [낡은 줄 · 1.3.46 까지] (409 + [그 PC 접속을 끊고 여기서 사용하기]).
            //     🔴 [20260928작2 · K-4 다] 재로그인 화면은 409 「다른 PC에서 사용 중입니다」 안내뿐이고 **버튼이 없다.**
            //     절C(`GuardExpiredPcSessionRevivalAsync`)가
            //     만료 세션에 대해 이미 하는 것과 **같은 규칙**이고, 고객에게 하는 말도 같은 문장이다.
            //   🔴 `sid` 없는 **옛 토큰 자체는 차단하지 않는다**(작지 §3 금지 #1b) — 통과는 시키고,
            //     **새 PC 세션 행을 만드는 순간에만** 판정을 거친다.
            //   🔴 20260928작2 절B — 매개변수가 `force` → `isLogin` 으로 바뀌었다. 갱신은 `false` 다 ⇒
            //     죽은 PC 정리(남의 refresh 폐기)를 **하지 않는다**(설계 §3-1). K-5 기록(`pc_login_blocked`)도 없다.
            try
            {
                await EnforceSinglePcLoginAsync(conn, user, refreshDeviceKind, isLogin: false);
            }
            catch (ConcurrentPcLoginException ex)
            {
                // 갱신 엔드포인트는 401 만 고객 언어로 번역한다(AuthController.Refresh) —
                //   409 는 로그인 화면에만 있는 선택지다. 그래서 절C 와 같은 문장으로 바꿔 던진다.
                System.Diagnostics.Trace.TraceWarning(
                    $"[축B] 갱신이 새 PC 세션을 만들려 했으나 다른 PC 가 살아 있다(401 · 재로그인 유도) user={user.Id}, 이전 사용={ex.OtherPcLastActiveAtUtc:O}");
                throw; // NEG-B36b 서비스의 401 변환 제거 — 예외가 그대로 컨트롤러로 간다
            }

            sessionRecorded = await InsertSessionAsync(conn, sessionId!, user, refreshDeviceKind);
        }

        // 🔴 20260928작1 절A (W-1 · CTO K-8) — 보상 삭제의 **보호대를 한 점으로** 모았다.
        //   종전에는 아래 보상 호출 세 자리(③ 회전 준비 · ① 단일사용 거부 · ② 회전 중 예외)에
        //   같은 식 `isNewSession && sessionRecorded ? sessionId : null` 이 **세 번 복사**돼 있었고,
        //   게이트(G-B13d)는 ③ 하나만 탔다 ⇒ ①② 에서 `isNewSession &&` 가 빠져도 FAIL 하는 게이트가 없었다.
        //   ⇒ 식은 **그대로**, 자리만 하나로. 이제 보호대를 지우면 세 자리가 동시에 빠지고 G-B13d 가 잡는다.
        //   🔴 `isNewSession &&` 가 **이어받은(일하고 있는) 세션**을 지키는 유일한 조각이다 — 지우면
        //     갱신 실패 한 번으로 남의 PC 세션 행을 지운다(#20). 이름은 받는 쪽 매개변수와 같게 했다.
        var sessionIdCreatedHere = isNewSession && sessionRecorded ? sessionId : null;

        // 🔴 세션 기록이 실패했으면 `sid` 를 **싣지 않는다**(D-2 · `LoginAsync` 의
        //   `CreateLoginResponse(..., sessionRecorded ? sessionId : null)` 과 **같은 취급**).
        //   🔴 5차 — 여기 적혀 있던 `:166` 도 2차 주석에 밀렸다([4] R-7). 식 이름으로 가리킨다.
        //   `sid` 없는 토큰은 미들웨어가 종전처럼 통과시킨다(옛 토큰 호환 경로 · 작지 §3 금지 #1b).
        // ⬛ [낡은 자리 · 20260928작2 절D 이전] `var response = CreateLoginResponse(...)` 가 여기 — 보상 `try` **밖** — 있었다.
        //   토큰 생성이 터지면 방금 만든 세션 행이 보상 없이 만료까지 남았다(F-4).
        // 🔴 [지금] 아래 「회전 준비」 `try` 안 **첫 줄**로 옮겼다 — 보상 자리를 새로 만들지 않고 세 번째 갈래가 덮는다(설계 §4).
        //   ⚠️ 갱신 쪽 토큰 생성 예외는 현실 입력을 못 찾았다(선행 2-1) ⇒ 동작 게이트 없음 · **모양 봉합**이다.
        LoginResponse response;
        // 🔴 20260928작2 절B — 회전 INSERT 의 `session_id` = 새 refresh JWT 의 `sid`(불변식 · 설계 §2).
        var rotatedSessionId = sessionRecorded ? sessionId : null;

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
        //   ⬛ [낡은 줄 — 바로 위 F5 정정 괄호] 20260928작2 절B 에서 LoginAsync 의 계정 전삭을 **없앴다.**
        //     지금 정책은 「단일 활성 세션」이 아니다 — **PC 1대 + 모바일 FREE**(사장님 9/28)이고, refresh 는
        //     로그인마다 따로 산다(`session_id` 로 가른다 · DB-130). 토큰별 회전이 그 정책 그대로의 모양이다.
        //   봉합 (2026-06-20, 3차 전수조사 후속): EF/Dapper 가 커넥션을 암묵적으로 닫아둔 상태면
        //   BeginTransaction 이 "open and available Connection" 예외로 터진다. 명시적으로 먼저 연다.
        // ── 🔴 세 번째 실패 경로도 보상으로 감싼다 (20260927작2 **5차** · [4] R-2 · PM 결재) ─────────
        //
        //   [무엇이 틀렸나] 3차는 *"회전 실패 경로는 단일사용 거부 401 과 그 밖의 예외 **둘뿐**"* 이라
        //     단정했다. **거짓이었다.** 바로 위 주석이 과거 실제 사고로 기록한 그대로,
        //     **연결 열기(`OpenAsync`)와 `conn.BeginTransaction()` 자체가 터질 수 있고** 그 두 줄은
        //     `try` **밖**이었다. 거기서 터지면 방금 넣은 세션 행이 **보상 없이 만료까지 남는다** —
        //     3차가 닫았다고 적은 그 구멍이 **한 갈래 그대로 열려 있었다**(#42 거짓봉합 경계).
        //
        //   ⇒ 이 두 줄만 `try` 로 감싸 **보상 호출 자리를 하나 늘린다.**
        //   🔴 트랜잭션 구조는 새로 짜지 않는다 — 범위를 넓히지 않고 `BeginTransaction` 결과를
        //     그대로 아래 `using` 에 넘긴다(아래 `catch` 두 곳은 손대지 않았다).
        //   🔴 **원인 예외는 그대로 전파**한다(`throw;`) — 보상 때문에 원인이 바뀌면
        //     다음 사람이 엉뚱한 곳을 본다(3차가 스스로 적은 위험).
        //   🔴 이어받은 세션(`isNewSession == false`)은 여기서도 **건드리지 않는다** —
        //     ⬛ [낡은 서술] *"인자가 `null` 이 되어 `CompensateNewSessionRowAsync` 첫 줄이 즉시 반환한다."*
        //     [지금 · 20260928작1 절A] 인자는 한 점 변수 `sessionIdCreatedHere` 다 — 이어받은 세션이면 그 값이
        //     `null` 이라 `CompensateNewSessionRowAsync` 첫 줄이 즉시 반환한다(세 자리 공통).
        //   ⚠️ 한계를 적는다 — 연결이 상한 원인이었다면 보상 `DELETE` 자체도 실패한다.
        //     그때는 삼키고 남은 세션 번호를 로그에 적는다(#15 · [4] R-6 과 같은 한계).
        System.Data.IDbTransaction tx;
        try
        {
            // 🔴 20260928작2 절D (F-4) — 토큰 생성을 이 `try` 의 **첫 줄**로 옮겼다(위 설명).
            response = CreateLoginResponse(
                user, employee, secret, redirectToWelcome: false, rotatedSessionId);

            // 🔴 6차 — 열기를 **한 갈래**로 정리했다([4] V-1 · CI CodeQL error).
            //   ⬛ [낡은 줄 · 5차] `if (conn is System.Data.Common.DbConnection dbConn) { await dbConn.OpenAsync(ct); }
            //     else { conn.Open(); }`
            //   [왜 지웠나] `IUnitOfWork.GetDbConnection()` 의 반환형이 **이미 `DbConnection`** 이다
            //     (`IUnitOfWork.cs` · `DbConnection GetDbConnection();`) ⇒ `is DbConnection` 은 항상 참이고
            //     `else` 가지는 `conn == null` 일 때만 닿는데, 그때는 바로 위 `conn.State` 가 먼저 터진다.
            //     **죽은 가지**라서 CodeQL 이 *"Variable conn is always null at this dereference"* 를
            //     **error** 로 올렸다(4차 커밋은 success → 5차가 이 줄을 `try` 안으로 옮겨 새 경고로 집계됐다).
            //   🔴 **동작은 그대로다** — 닫힌 연결을 여는 일 자체는 남긴다. 과거 실제 사고
            //     (*"open and available Connection"* · 위 주석)를 막는 것이 이 두 줄의 목적이고,
            //     그 목적은 `State` 검사 + `OpenAsync` 한 갈래로 온전히 유지된다.
            //   ⚠️ 이 모양은 **다른 자리에도 많다**(`AuthController` 4곳 · `SessionValidityMiddleware` 등).
            //     그쪽은 DI 가 `System.Data.IDbConnection` 으로 꺼내므로 `is DbConnection` 이 **실재 검사**다
            //     — 죽은 가지가 아니라서 **건드리지 않았다**(6차 범위 밖 · 명세서 §3-1 목록).
            if (conn.State != System.Data.ConnectionState.Open)
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);
            }
            tx = conn.BeginTransaction();
        }
        catch (Exception)
        {
            await CompensateNewSessionRowAsync(
                conn, user, sessionIdCreatedHere,
                "회전 준비 중 예외(토큰 생성·연결 열기·트랜잭션 시작)");
            throw;   // 원인 예외 그대로 — 보상은 흔적을 지우는 일이지 원인을 바꾸는 일이 아니다.
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
        //   ⬛ [낡은 줄 · 3차] *"(회전 실패 경로는 그 둘뿐이다: 단일사용 거부 401 · 그 밖의 예외)"*
        //     — **틀렸다**([4] R-2). 세 번째가 있었다: **연결 열기·`BeginTransaction` 자체의 예외**.
        //     그 둘은 `try` 밖이라 보상이 한 번도 안 불렸다.
        //   🔴 [정확한 서술 · 5차] 회전 실패 경로는 **셋**이고, 셋 모두 보상을 부른다:
        //     ① 단일사용 거부 401(`deleted == 0`) — 아래 `catch (UnauthorizedAccessException)`
        //     ② 회전 SQL 중 그 밖의 예외 — 아래 `catch (Exception)`
        //     ③ **회전 준비**(연결 열기·`BeginTransaction`) 중 예외 — **위 블록에서 5차에 함께 막았다**
        using (tx)
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

                // 🔴 20260928작2 절B — `session_id` 를 같이 적는다. 빠지면 갱신 한 번 뒤 로그아웃·죽은 PC 정리가
                //   이 토큰을 못 가른다(G-B18 은 갱신 뒤 토큰으로도 잰다).
                await conn.ExecuteAsync(
                    @"INSERT INTO refresh_tokens (token_id, user_id, token_hash, expires_at, is_revoked, session_id)
                      VALUES (@TokenId, @UserId, @TokenHash, @ExpiresAt, 0, @SessionId)",
                    new
                    {
                        TokenId = Guid.NewGuid().ToString(),
                        UserId = userId,
                        TokenHash = HashToken(response.RefreshToken),
                        ExpiresAt = DateTime.UtcNow.Add(RefreshTokenLifetime),
                        SessionId = rotatedSessionId
                    }, tx);

                tx.Commit();
            }
            catch (UnauthorizedAccessException)
            {
                // 🔴 3차 보상 삭제 — 이 갱신이 만든 세션 행이 주인 없이 남지 않게 한다(위 설명).
                await CompensateNewSessionRowAsync(
                    conn, user, sessionIdCreatedHere, "단일사용 거부(401)");
                throw; // 단일사용 거부는 이미 롤백됨 — 그대로 전파.
            }
            catch (Exception)
            {
                try { tx.Rollback(); } catch (Exception rbex) { Console.Error.WriteLine($"[AuthService] refresh 회전 롤백 실패: {rbex.Message}"); }
                // 🔴 3차 보상 삭제 — 같은 이유. 원인 예외는 바꾸지 않는다.
                await CompensateNewSessionRowAsync(
                    conn, user, sessionIdCreatedHere, "회전 중 예외");
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
                // ⬛ [낡은 값 · 20260928작2 절M 이전] `expires_at = @ExpiresAt`(`DateTime.UtcNow.Add(AccessTokenLifetime)`).
                // 🔴 절M (PI-8) — 로그인 INSERT 와 같이 **DB 시계**로 민다(`AccessTokenLifetime` 8h 와 같아야 한다).
                await conn.ExecuteAsync(
                    @"UPDATE user_sessions
                         SET last_active_at = UTC_TIMESTAMP(6), expires_at = UTC_TIMESTAMP(6) + INTERVAL 8 HOUR
                       WHERE session_id = @SessionId",
                    new { SessionId = sessionId });
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
