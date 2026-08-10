using HitPan.Application.DTOs.Device;
using HitPan.Application.Interfaces;
// 모바일 등록 QR 생성 (20260811작1 (D))
using HitPan.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 등록 기기 관리 API.
/// - 히트판 과금 모델: 기기 수 제한(계정 무제한).
/// - TenantAdmin: 테넌트 내 전체 기기 목록/폐기 가능
/// - 일반 사용자(tenant_user): 자기 기기만 조회
/// </summary>
[ApiController]
[Route("api/devices")]
[Authorize(Policy = "TenantOnly")]
public sealed class DeviceController : ControllerBase
{
    private readonly ITenantDeviceService _svc;

    public DeviceController(ITenantDeviceService svc) => _svc = svc;

    /// <summary>기기 목록 — TenantAdmin은 전체, 일반 사용자는 자기 것만.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var uid = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();

        var all = await _svc.GetAllAsync(tid, ct);

        // 부모계정(tenant_admin) 이외는 자기 기기만 필터
        //   platform_admin 절 제거 (보안 격벽 2026-06-18): 본사 계층은 백오피스 전용 — ERP가 발급 안 함.
        var accountType = User.FindFirst("account_type")?.Value;
        if (accountType != "tenant_admin")
        {
            all = all.Where(d => d.UserId == uid).ToList();
        }
        return Ok(all);
    }

    /// <summary>현재 테넌트의 기기 쿼터 (한도·사용량).</summary>
    [HttpGet("quota")]
    public async Task<IActionResult> GetQuota(CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        return Ok(await _svc.GetQuotaAsync(tid, ct));
    }

    /// <summary>기기 폐기 — TenantAdmin만.</summary>
    [HttpPost("revoke/{id}")]
    public async Task<IActionResult> Revoke(string id, [FromBody] RevokeDeviceRequest? body, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var uid = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(uid)) return Forbid();

        // platform_admin 절 제거 (보안 격벽 2026-06-18): 본사 계층은 백오피스 전용. 부모계정(tenant_admin)만 폐기 가능.
        var accountType = User.FindFirst("account_type")?.Value;
        if (accountType != "tenant_admin")
            return Forbid();

        try
        {
            await _svc.RevokeAsync(id, tid, uid, body?.Reason, ct);
        }
        catch (InvalidOperationException ex)
        {
            // 회사 서버(메인PC) 폐기 시도 — 서비스가 막는다 (20260810작3).
            // 500 이 아니라 이유를 담아 400 으로 돌려준다. 화면이 그대로 보여줄 문장이다.
            return BadRequest(new { message = ex.Message });
        }
        return Ok(new { message = "기기가 폐기되었습니다." });
    }

    /// <summary>
    /// 기기 승인 — 대표계정(tenant_admin)만 (20260811작1 (B)).
    /// 사장님 설계: "승인대기. 대표에게 기기승인의 권한을 주기"
    /// </summary>
    [HttpPost("approve/{id}")]
    public async Task<IActionResult> Approve(string id, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var uid = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(uid)) return Forbid();

        // 승인 권한은 대표계정에만 있다 — 직원이 자기 기기를 스스로 승인하면 승인제가 무의미해진다.
        var accountType = User.FindFirst("account_type")?.Value;
        if (accountType != "tenant_admin")
            return Forbid();

        string? authKey;
        try
        {
            // 대표가 승인하는 그 자리에서 인증키가 만들어진다(TenantDeviceService.ApproveAsync).
            authKey = await _svc.ApproveAsync(id, tid, uid, ct);
        }
        catch (InvalidOperationException ex)
        {
            // 한도 초과·폐기된 기기 등 — 화면이 그대로 보여줄 문장이다.
            return BadRequest(new { message = ex.Message });
        }

        // 🔴 인증키 원문을 대표 화면으로 올린다 (사장님 확정 2026-08-11):
        //   *"메인PC에서 인증키가 생성되면, 요청한 클라이언트PC에서 입력하는 방식.
        //     그 메인PC에서 키를 주는 방식은 대표 마음이지."*
        //   ⇒ 우리는 화면에 보여주는 데까지만 한다. 대표가 직원에게 어떻게 알려주는지는
        //     대표가 정한다(구두·메신저·메모).
        //   ⚠️ 이 값은 우리 DB 에 해시로만 남는다 — 이 응답을 놓치면 다시 알려줄 수 없다.
        //     화면이 반드시 사람에게 보여줘야 한다.
        return Ok(new
        {
            message = "기기가 승인되었습니다.",
            authKey                       // null = 이미 승인돼 있던 기기(다시 눌렀을 때)
        });
    }

    /// <summary>기기 승인 거부 — 대표계정만 (20260811작1 (B)).</summary>
    [HttpPost("reject/{id}")]
    public async Task<IActionResult> Reject(string id, [FromBody] RevokeDeviceRequest? body, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var uid = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(uid)) return Forbid();

        var accountType = User.FindFirst("account_type")?.Value;
        if (accountType != "tenant_admin")
            return Forbid();

        try
        {
            await _svc.RejectAsync(id, tid, uid, body?.Reason, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        return Ok(new { message = "기기 등록을 거부했습니다." });
    }

    /// <summary>
    /// 모바일기기 등록 QR 발급 — 대표계정만 (20260811작1 (D)).
    /// 사장님 오더: "모바일 등록기기 버튼 클릭시 QR생성"
    /// </summary>
    [HttpPost("mobile-token")]
    public async Task<IActionResult> IssueMobileToken(CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var uid = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(uid)) return Forbid();

        var accountType = User.FindFirst("account_type")?.Value;
        if (accountType != "tenant_admin")
            return Forbid();

        var token = await _svc.IssueMobileRegisterTokenAsync(tid, uid, ct);

        // QR 에 담을 주소 — 폰이 이 주소를 열면 등록 화면이 뜬다.
        //   접속한 그 주소를 그대로 쓴다. 고객사마다 도메인이 다르므로(= {고객사ID}.hitpan.kr)
        //   하드코딩하면 남의 회사 주소로 보내는 사고가 난다.
        var origin = $"{Request.Scheme}://{Request.Host}";
        var url = $"{origin}/m/device-register?t={token}";

        var matrix = QrCodeGenerator.Encode(url);
        var qrImage = QrCodeGenerator.ToPngDataUri(matrix);

        return Ok(new { qrImage, expiresInMinutes = 10 });
    }

    /// <summary>
    /// QR 로 모바일기기 등록 — **로그인 없이** 호출된다 (20260811작1 (D)).
    ///
    /// 사장님 설계: 폰으로 QR 을 찍으면 바로 등록 화면이 뜨고 Y/N 만 누른다.
    /// 로그인을 요구하면 그 자리에서 흐름이 끊긴다 — 현장 직원은 폰에 히트판 계정이 없을 수도 있다.
    /// 대신 **토큰 자체가 열쇠**다: 대표계정만 발급할 수 있고, 10분 만료·1회용이다.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("mobile-register")]
    public async Task<IActionResult> RegisterMobile([FromBody] MobileRegisterRequest body, CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Token))
            return BadRequest(new { message = "등록 정보가 올바르지 않습니다." });

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var ua = Request.Headers.UserAgent.ToString();

        var (ok, message, deviceId) = await _svc.RegisterMobileByTokenAsync(
            body.Token, body.DeviceName ?? "모바일 기기", body.Fingerprint ?? "", ip, ua, ct);

        if (!ok) return BadRequest(new { message });
        return Ok(new { message, deviceId });
    }

    /// <summary>
    /// 직원 PC 가 대표에게 받은 인증키를 **입력**한다 (20260811작3 (A)).
    ///
    /// 사장님 확정: *"메인PC에서 인증키가 생성되면, 요청한 클라이언트PC에서 입력하는 방식.
    ///                그 메인PC에서 키를 주는 방식은 대표 마음이지."*
    ///
    /// 🔴 이 방식이 "그게 직원인지 해커인지" 를 푼다 — 키를 **아는 사람만** 넣을 수 있고,
    ///    그 키는 **대표가 직접 건넨 것**이다. 서버가 추측할 일이 없다.
    /// </summary>
    [HttpPost("verify-key")]
    public async Task<IActionResult> VerifyKey([FromBody] VerifyKeyRequest? body, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();

        if (body is null || string.IsNullOrWhiteSpace(body.AuthKey))
            return BadRequest(new { message = "인증키를 입력해 주세요." });

        var deviceId = await _svc.VerifyAuthKeyAsync(body.AuthKey.Trim(), tid, ct);

        if (deviceId is null)
            // 어느 쪽이 틀렸는지 알려주지 않는다 — 알려주면 찍어 맞히는 데 도움이 된다.
            return BadRequest(new { message = "인증키가 올바르지 않습니다. 관리자에게 다시 확인해 주세요." });

        return Ok(new { message = "기기 인증이 완료되었습니다.", deviceId });
    }

    public sealed class VerifyKeyRequest
    {
        public string AuthKey { get; set; } = "";
    }

    public sealed class MobileRegisterRequest
    {
        public string Token { get; set; } = "";
        public string? DeviceName { get; set; }
        public string? Fingerprint { get; set; }
    }

    public sealed class RevokeDeviceRequest
    {
        public string? Reason { get; set; }
    }
}
