using HitPan.Application.DTOs.Auth;
using HitPan.Application.DTOs.Device;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private const string CurrentTermsVersion = "v2.0.0";

    private readonly IAuthService _authService;
    private readonly IHrService _hrService;
    private readonly ITenantDeviceService _deviceService;
    private readonly ITermsConsentService _termsConsentService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService authService,
        IHrService hrService,
        ITenantDeviceService deviceService,
        ITermsConsentService termsConsentService,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _hrService = hrService;
        _deviceService = deviceService;
        _termsConsentService = termsConsentService;
        _logger = logger;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        try
        {
            var response = await _authService.LoginAsync(request, ct);

            // ── 메인PC 면제 (작1 결재 2026-07-02, 헌법 사장님 정의) ──
            //   메인PC = 데이터+ERP 설치마법사가 깔린 PC(1도메인 1대). 설치 시점에 이미 등록된 존재라
            //   접속 기기인증/슬롯에서 제외한다. 클라이언트PC(브라우저 접속)만 기기 슬롯 대상.
            //   판별 = loopback 접속 여부. 메인PC 본인은 localhost/127.0.0.1(loopback)로 접속하고
            //   (installer HitPan-Universal.iss 바로가기), 클라이언트는 터널 도메인으로만 온다.
            //   IP 기반이 아니라 loopback 여부라 cloudflared 터널 프록시가 위조할 수 없다(신뢰 가능).
            var isMainPcLoopback = HttpContext.Connection.RemoteIpAddress?.Equals(System.Net.IPAddress.Loopback) == true
                || HttpContext.Connection.RemoteIpAddress?.Equals(System.Net.IPAddress.IPv6Loopback) == true;

            // ── 기기 기반 라이선싱: 로그인 성공 후 기기 등록/갱신 ──
            // - fingerprint 없으면 스킵 (기존 클라이언트 호환)
            // - 메인PC(loopback)면 스킵 (슬롯 미소모)
            // - 한도 초과면 로그인 거부 (Unauthorized)
            if (!string.IsNullOrEmpty(response.TenantId) && !string.IsNullOrEmpty(request.DeviceFingerprint) && !isMainPcLoopback)
            {
                try
                {
                    var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                    var jwt = handler.ReadJwtToken(response.AccessToken);
                    var userId = jwt.Claims.FirstOrDefault(c => c.Type == "user_id")?.Value;

                    if (!string.IsNullOrEmpty(userId))
                    {
                        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
                        var ua = request.DeviceName; // 편의상 UA를 DeviceName 필드에 담아 보낼 수도 있음
                        var deviceReq = new RegisterDeviceRequest
                        {
                            Fingerprint = request.DeviceFingerprint!,
                            DeviceType = request.DeviceType ?? "pc",
                            DeviceName = request.DeviceName,
                            UserAgent = Request.Headers["User-Agent"].ToString()
                        };

                        var (allowed, reason, deviceId, newlyRegistered) = await _deviceService.RegisterOrRefreshAsync(
                            response.TenantId, userId, deviceReq, ipAddress, ct);

                        if (!allowed)
                        {
                            // 기기 한도 초과·폐기 등 → 로그인 거부 (작1 F3: "등록된 기기가 아닙니다" 명확한 사유)
                            return Unauthorized(new { message = reason });
                        }

                        response.DeviceId = deviceId;
                        // 작1 F3 — 처음 등록된 신규 기기면 클라이언트가 안내 노출(첫 접속 인지)
                        response.DeviceNewlyRegistered = newlyRegistered;
                        if (newlyRegistered)
                            response.DeviceNotice = "이 기기가 새 기기로 등록되었습니다.";
                    }
                }
                catch (Exception ex)
                {
                    // 기기 등록 로직 자체가 실패해도 로그인 흐름은 막지 않음(운영 추적만)
                    _logger.LogWarning(ex, "기기 등록/갱신 실패 — TenantId: {TenantId}", response.TenantId);
                }
            }

            // 로그인 성공 시 자동 출근 기록 (중복 출근은 서비스에서 무시)
            if (!string.IsNullOrEmpty(response.TenantId))
            {
                try
                {
                    // 봉합 (2026-06-22, 13차 2단 교차검증 — HR employee_id 부분봉합 회귀): HrController 4곳을
                    //   employee_id 로 바꿨으나 같은 HrService 를 호출하는 자동출/퇴근(AuthController)을 누락하면
                    //   자동출근은 user_id 키, 수동출근은 employee_id 키로 attendance 이중행·CheckOut 미스가 난다
                    //   (헌법 #12 구현체 전수 누락, 11차 반품사유와 동형). JWT employee_id 클레임으로 통일.
                    var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                    var jwt = handler.ReadJwtToken(response.AccessToken);
                    var employeeId = jwt.Claims.FirstOrDefault(c => c.Type == "employee_id")?.Value;
                    if (!string.IsNullOrEmpty(employeeId))
                    {
                        await _hrService.CheckInAsync(response.TenantId, employeeId,
                            new HitPan.Application.DTOs.Employee.CheckInOutRequest { Memo = "자동출근" }, ct);
                    }
                }
                catch (Exception ex)
                {
                    // 출근 기록 실패는 로그인 자체를 막지 않지만 운영 추적을 위해 로그 남김
                    _logger.LogWarning(ex, "자동 출근 기록 실패 — TenantId: {TenantId}", response.TenantId);
                }
            }

            // 헌법 #24: 약관 v2.0.0 동의 여부 저장 (미동의 시 /terms 강제 이동)
            if (!string.IsNullOrEmpty(response.TenantId))
            {
                try
                {
                    var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                    var jwt = handler.ReadJwtToken(response.AccessToken);
                    var userId = jwt.Claims.FirstOrDefault(c => c.Type == "user_id")?.Value;
                    if (!string.IsNullOrEmpty(userId))
                    {
                        var hasAgreed = await _termsConsentService.HasAgreedAsync(
                            response.TenantId, userId, CurrentTermsVersion, ct);
                        response.RequiresTermsConsent = !hasAgreed;
                    }
                }
                catch (Exception ex)
                {
                    // 테이블 부재 등 인프라 사고 = 동의 강제 보류 (베타 1주차 정합, 헌법 #15 silent swallow 금지)
                    _logger.LogWarning(ex, "약관 동의 상태 조회 실패 — TenantId: {TenantId}", response.TenantId);
                    response.RequiresTermsConsent = false;
                }
            }

            // ── 고리2(A안): 업데이트 동의 Y/N 팝업 정보 (헌법 #30 로컬 자가완결, 본사 의존 0) ──
            //   CurrentVersion 은 ERP 어셈블리 버전으로 항상 채운다. 새 버전 존재 여부(UpdateAvailable)는
            //   로컬에서 워치독이 평가한 결과를 읽어 채운다 — 정보가 아직 없으면 false 안전 폴백.
            //   ⚠️ 로그인 흐름은 절대 막지 않는다(어떤 예외도 로그만, false 폴백). 로그인 깨짐 = P0.
            await EnrichUpdateConsentInfoAsync(response, ct);

            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    /// <summary>로그아웃 + 자동 퇴근 + 세션 정리</summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = HttpContext.Items["UserId"]?.ToString();
        // 봉합 (2026-06-22, 13차 2단 교차검증): 자동퇴근도 employee_id 로 통일(자동출근·HrController 정합).
        var employeeId = HttpContext.Items["EmployeeId"]?.ToString();

        if (!string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(userId))
        {
            // 자동 퇴근 기록 — employee_id 키로 자동출근행과 매칭(없으면 무시, 로그인/로그아웃 자체는 불방해)
            if (!string.IsNullOrEmpty(employeeId))
            {
                try { await _hrService.CheckOutAsync(tenantId, employeeId, ct); }
                catch (Exception ex) { _logger.LogWarning(ex, "자동 퇴근 기록 실패 — EmployeeId: {EmployeeId}", employeeId); }
            }

            // 세션 + refresh_token 정리
            try
            {
                var db = HttpContext.RequestServices.GetRequiredService<System.Data.IDbConnection>();
                if (db.State != System.Data.ConnectionState.Open)
                {
                    if (db is System.Data.Common.DbConnection c) await c.OpenAsync(ct);
                    else db.Open();
                }
                await Dapper.SqlMapper.ExecuteAsync(db,
                    "UPDATE refresh_tokens SET is_revoked = 1 WHERE user_id = @UserId",
                    new { UserId = userId });
                await Dapper.SqlMapper.ExecuteAsync(db,
                    "DELETE FROM user_sessions WHERE user_id = @UserId",
                    new { UserId = userId });
            }
            catch (Exception ex) { _logger.LogWarning(ex, "로그아웃 세션/토큰 정리 실패 — UserId: {UserId}", userId); }
        }

        return Ok(new { message = "로그아웃 완료" });
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        try
        {
            var response = await _authService.RefreshAsync(request, ct);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Step-up 인증: 현재 로그인한 어드민 본인 비밀번호 재검증.
    /// WO-20260430-9 (사장님 검증 발견 — 사용자 정보 수정 등 민감 작업 보호용).
    /// 통과 시 200, 실패 시 401. 5분 캐싱은 후속 작업.
    /// </summary>
    [HttpPost("verify-password")]
    [Authorize]
    public async Task<IActionResult> VerifyPassword([FromBody] VerifyPasswordRequest request, CancellationToken ct)
    {
        var userId = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(request?.Password))
        {
            return Unauthorized(new { message = "인증 정보가 부족합니다." });
        }

        var ok = await _authService.VerifyOwnPasswordAsync(userId, request.Password, ct);
        return ok ? Ok(new { verified = true })
                  : Unauthorized(new { message = "비밀번호가 일치하지 않습니다." });
    }

    /// <summary>
    /// 고리2(A안) — 업데이트 동의(Y/N)를 고객 PC 로컬 ERP DB(local_update_consents)에 기록한다.
    ///   · 본사를 거치지 않는다(헌법 #30 본사 의존 0). 워치독이 로컬에서 본 테이블을 SELECT 해 적용 가부 판단.
    ///   · tenant_id·user_id 는 JWT 클레임에서만 받는다(헌법 #2). 파라미터로 받지 않는다.
    ///   · INSERT ONLY — 동의 이력은 갱신/삭제하지 않는다(매 동의/거부를 한 행으로 남긴다).
    /// </summary>
    [HttpPost("update-consent")]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> UpdateConsent([FromBody] UpdateConsentRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId))
            return Forbid();

        if (request is null
            || string.IsNullOrWhiteSpace(request.UpdateVersion)
            || string.IsNullOrWhiteSpace(request.Action))
        {
            return BadRequest(new { message = "동의 버전과 동작(approve/reject)이 필요합니다." });
        }

        var action = request.Action.Trim().ToLowerInvariant();
        if (action != "approve" && action != "reject")
            return BadRequest(new { message = "동작은 approve 또는 reject 여야 합니다." });

        try
        {
            var db = HttpContext.RequestServices.GetRequiredService<System.Data.IDbConnection>();
            if (db.State != System.Data.ConnectionState.Open)
            {
                if (db is System.Data.Common.DbConnection c) await c.OpenAsync(ct);
                else db.Open();
            }

            await Dapper.SqlMapper.ExecuteAsync(db, new Dapper.CommandDefinition(
                """
                INSERT INTO local_update_consents
                    (tenant_id, user_id, update_version, action, consented_at)
                VALUES
                    (@TenantId, @UserId, @UpdateVersion, @Action, @ConsentedAt)
                """,
                new
                {
                    TenantId = tenantId,
                    UserId = userId,
                    UpdateVersion = request.UpdateVersion.Trim(),
                    Action = action,
                    ConsentedAt = DateTime.Now
                },
                cancellationToken: ct));

            return Ok(new
            {
                // ★ 20260806작1 P0-2 ([3-V] 병렬검증 적발): 종전 "다음 재시작 때 적용됩니다"는
                //   즉시 적용 봉합(Worker.cs Approve 분기) 이후 **거짓**이 된다.
                //   문구와 동작이 어긋나면 고객은 언제 무슨 일이 일어나는지 알 수 없다(헌법 #24).
                message = action == "approve"
                    ? "업데이트를 시작합니다. 잠시 후 히트판이 꺼졌다가 다시 켜집니다."
                    : "나중에 안내해 드리겠습니다."
            });
        }
        catch (Exception ex)
        {
            // 헌법 #15: 동의 기록 실패는 침묵하지 않는다. 단, 사용자에겐 명확한 한국어 메시지로 반환.
            _logger.LogWarning(ex, "업데이트 동의 기록 실패 — TenantId: {TenantId}", tenantId);
            return StatusCode(500, new { message = "동의 기록에 실패했습니다. 잠시 후 다시 시도해주세요." });
        }
    }

    /// <summary>
    /// 고리2(A안) — 로그인 응답에 업데이트 동의 팝업 정보를 채운다.
    ///   CurrentVersion 은 ERP 어셈블리 버전으로 항상 채운다.
    ///   UpdateAvailable 은 로컬에 워치독이 적재한 "새 버전" 정보가 있고 그게 설치버전보다 높을 때만 true.
    ///   경로(2026-06-29 봉합, 고리2 마지막 빈 칸): 워치독이 Major 새버전 발견 시 local_update_status(DB-83)에
    ///     적재 → 여기서 그 행(최신 1건)을 읽어 SemVer 비교 후 채운다. 본사를 거치지 않는다(A안, 헌법 #30).
    ///   ⚠️ 로그인은 절대 안 깨진다: 테이블 부재·조회 실패·버전 파싱 실패 등 어떤 예외도 로그만 남기고
    ///      UpdateAvailable=false 안전 폴백한다(헌법 #15 침묵 금지 + 로그인 깨짐 = P0).
    ///   tenant_id 는 JWT 클레임 영역에서만 다룬다(헌법 #2). 로컬 단일 테넌트 DB라 본 조회는 tenant 필터 불요.
    /// </summary>
    private async Task EnrichUpdateConsentInfoAsync(LoginResponse response, CancellationToken ct)
    {
        // 조회 로직은 ComputeUpdateStatusAsync 로 추출해 로그인·GET(update-status) 두 경로가 공용한다
        //   (2026-07-22 작1 W-1, CTO A-2: 추출 후 로그인 동작 1비트도 안 변해야 함 — 동일 로직·동일 폴백).
        var status = await ComputeUpdateStatusAsync(ct);
        response.CurrentVersion = status.CurrentVersion;
        response.UpdateAvailable = status.UpdateAvailable;
        response.LatestVersion = status.LatestVersion;
        response.UpdateChannel = status.UpdateChannel;
        response.ConsentMessage = status.ConsentMessage;
    }

    /// <summary>
    /// 고리2(A안) — 업데이트 상태 조회 GET. 팝업을 로그인 페이지가 아닌 ERP 메인(MainLayout)에서
    ///   띄우기 위해(2026-07-22 작1: 로그인 성공 시 /login 폐기로 팝업이 0.5초 만에 사라지던 진범 봉합)
    ///   상태를 독립적으로 조회하는 전용 엔드포인트다. UpdateConsentGate 컴포넌트가 이걸 호출한다.
    ///   · 조회 로직은 로그인과 완전히 동일(ComputeUpdateStatusAsync 공용) — 두 경로가 절대 갈라지지 않게.
    ///   · [Authorize] 필수(CTO B-2). 미인증 401 시 게이트는 조용히 스킵한다.
    ///   · 어떤 예외도 로그인처럼 안전 폴백(UpdateAvailable=false) — ERP 사용을 막지 않는다(헌법 #15·#20).
    /// </summary>
    [HttpGet("update-status")]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetUpdateStatus(CancellationToken ct)
    {
        var status = await ComputeUpdateStatusAsync(ct);
        return Ok(status);
    }

    /// <summary>
    /// 워치독이 local_update_status(DB-83)에 적재한 최신 새버전을 읽어 설치버전과 SemVer 비교한다.
    ///   로그인 응답 보강(EnrichUpdateConsentInfoAsync)과 GET(update-status)의 단일 진실원.
    ///   테이블 부재·조회 실패·파싱 실패 등 어떤 예외도 UpdateAvailable=false 안전 폴백(헌법 #15 침묵 금지).
    ///   tenant_id 는 JWT 클레임에서만 다룬다(헌법 #2). 로컬 단일 테넌트 DB라 tenant 필터 불요.
    /// </summary>
    private async Task<UpdateStatusDto> ComputeUpdateStatusAsync(CancellationToken ct)
    {
        // 안전 기본값 — 어떤 분기에서도 호출자(로그인·게이트)를 막지 않는다.
        var status = new UpdateStatusDto
        {
            CurrentVersion = GetCurrentErpVersion(),
            UpdateAvailable = false,
            LatestVersion = null,
            UpdateChannel = null,
            ConsentMessage = null
        };

        try
        {
            var db = HttpContext.RequestServices.GetRequiredService<System.Data.IDbConnection>();
            if (db.State != System.Data.ConnectionState.Open)
            {
                if (db is System.Data.Common.DbConnection c) await c.OpenAsync(ct);
                else db.Open();
            }

            // 워치독이 적재한 최신 새버전 1건. 테이블이 없거나 비어 있으면 null → 안전 폴백.
            var row = await Dapper.SqlMapper.QueryFirstOrDefaultAsync<LocalUpdateStatusRow>(db,
                new Dapper.CommandDefinition(
                    """
                    SELECT latest_version AS LatestVersion,
                           update_channel AS UpdateChannel,
                           consent_message AS ConsentMessage
                    FROM local_update_status
                    ORDER BY discovered_at DESC, id DESC
                    LIMIT 1
                    """,
                    cancellationToken: ct));

            if (row is null || string.IsNullOrWhiteSpace(row.LatestVersion))
                return status; // 적재된 새버전 없음 → 폴백 유지

            // 설치버전보다 높은 새버전일 때만 팝업 노출(SemVer Major.Minor.Build 비교).
            if (IsNewerVersion(status.CurrentVersion, row.LatestVersion))
            {
                status.UpdateAvailable = true;
                status.LatestVersion = row.LatestVersion;
                status.UpdateChannel = row.UpdateChannel;
                status.ConsentMessage = row.ConsentMessage;
            }
        }
        catch (Exception ex)
        {
            // 헌법 #15: 침묵 금지 + 호출자는 절대 안 깨지게(false 폴백).
            _logger.LogWarning(ex, "업데이트 상태 조회 실패 — UpdateAvailable false 폴백");
            status.UpdateAvailable = false;
        }

        return status;
    }

    /// <summary>local_update_status 1행 매핑(고리2 새버전 상태).</summary>
    private sealed class LocalUpdateStatusRow
    {
        public string? LatestVersion { get; set; }
        public string? UpdateChannel { get; set; }
        public string? ConsentMessage { get; set; }
    }

    /// <summary>
    /// SemVer(Major.Minor.Build) 비교 — latest 가 current 보다 높으면 true. 파싱 실패 시 false(안전 폴백).
    /// "2.0.0" > "1.0.0", "1.2.0" > "1.1.9" 식. 누락 자리는 0 으로 본다("2.0" → 2.0.0).
    /// </summary>
    private static bool IsNewerVersion(string? current, string? latest)
    {
        var c = ParseSemVer(current);
        var l = ParseSemVer(latest);
        if (c is null || l is null) return false; // 파싱 실패 = 팝업 안 띄움(보수적)

        for (var i = 0; i < 3; i++)
        {
            if (l[i] > c[i]) return true;
            if (l[i] < c[i]) return false;
        }
        return false; // 동일 버전
    }

    /// <summary>"Major.Minor.Build" → int[3]. 형식 위반·음수·비숫자면 null.</summary>
    private static int[]? ParseSemVer(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var parts = v.Trim().Split('.');
        if (parts.Length == 0 || parts.Length > 3) return null;

        var result = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out var n) || n < 0) return null;
            result[i] = n;
        }
        return result;
    }

    /// <summary>
    /// 현재 ERP 어셈블리 버전 "Major.Minor.Build"(예: "1.2.34"). 워치독 VersionInfo.Current 와 동일 형식.
    ///
    /// 정리 (2026-07-16, 작1 W4-0): 동일 로직이 VersionInfo 로 승격됐다(HealthController 도 같은 출처를 보게 하려고).
    ///   사본을 남기면 언젠가 두 값이 갈라져 "로그인 응답 버전 ≠ /health 버전"이 된다 — 위임한다.
    /// </summary>
    private static string GetCurrentErpVersion() => VersionInfo.Current;
}

public sealed record VerifyPasswordRequest(string Password);

/// <summary>
/// 고리2(A안) 업데이트 상태 응답 — GET api/auth/update-status 및 로그인 응답 보강의 공용 형태.
///   UpdateConsentGate 컴포넌트가 이 형태로 받아 팝업 노출을 판단한다(2026-07-22 작1).
/// </summary>
public sealed class UpdateStatusDto
{
    /// <summary>현재 설치된 ERP 버전(예: "1.2.34").</summary>
    public string? CurrentVersion { get; set; }

    /// <summary>설치버전보다 높은 새버전이 로컬에 적재돼 있으면 true.</summary>
    public bool UpdateAvailable { get; set; }

    /// <summary>적재된 새버전(예: "1.2.36"). UpdateAvailable=false 면 null.</summary>
    public string? LatestVersion { get; set; }

    /// <summary>업데이트 채널(Major/Normal/Emergency).</summary>
    public string? UpdateChannel { get; set; }

    /// <summary>팝업에 보여줄 안내 문구(manifest 발행값).</summary>
    public string? ConsentMessage { get; set; }
}

/// <summary>고리2 업데이트 동의 요청 — tenant_id·user_id 는 JWT 클레임에서만(헌법 #2), 바디에 두지 않는다.</summary>
public sealed class UpdateConsentRequest
{
    /// <summary>동의 대상 새 버전(예: "2.0.0").</summary>
    public string UpdateVersion { get; set; } = string.Empty;

    /// <summary>"approve"(예) 또는 "reject"(나중에).</summary>
    public string Action { get; set; } = string.Empty;
}
