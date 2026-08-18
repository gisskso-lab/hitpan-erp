using HitPan.Application.Common;
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

    /// <summary>
    /// 🔴 <b>대표에게 연락할 곳</b> — 관문에 막힌 직원이 <b>누구에게 전화할지</b> 알기 위한 값 (20260818작2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// [왜 필요한가] 종전 안내는 <i>"관리자에게 문의하세요"</i> 로 끝났다.
    /// 직원은 <b>누구에게</b> 전화할지 모른다 — <b>갈 곳 없는 안내는 흐름이 끊긴 것</b>이다(헌법 #20).
    /// </para>
    /// <para>
    /// ⚠️ <b>승인 안 난 기기도 이 길은 지나갈 수 있어야 한다</b> — 막히면 안내를 못 받는다.
    /// <c>/api/devices</c> 는 <c>DeviceAuthMiddleware</c> 의 통과 목록에 이미 있다(그 파일은 안 건드렸다).
    /// </para>
    /// <para>
    /// 🔴 <b>고객사 안에서만 보이는 값이다</b>(헌법 #18·#22) — 본사로 나가는 경로가 없다.
    /// ⚠️ 이름·전화번호뿐이며, 그 이상은 돌려주지 않는다(필요 최소).
    /// </para>
    /// </remarks>
    [HttpGet("admin-contact")]
    public async Task<IActionResult> GetAdminContact(CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();

        // ⚠️ 못 찾으면 null 을 그대로 돌려준다 — 화면은 그때 종전 문구로 간다.
        //   404 로 만들지 않는다. 없는 것은 오류가 아니라 **그냥 없는 것**이다.
        return Ok(await _svc.GetAdminContactAsync(tid, ct));
    }

    /// <summary>현재 테넌트의 기기 쿼터 (한도·사용량).</summary>
    [HttpGet("quota")]
    public async Task<IActionResult> GetQuota(CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();
        return Ok(await _svc.GetQuotaAsync(tid, ct));
    }

    /// <summary>
    /// 지금 보고 있는 이 화면이 **메인PC(자료보관 컴퓨터)에서 열린 것인지** 알려준다.
    ///
    /// 🔴 2026-08-11 (사장님 지시):
    ///   *"자료관리는 **부모계정 + 메인PC 환경에서만** 돌도록"*
    ///
    ///   [왜 필요한가] 백업·복구·자료이관·모든데이터 초기화는 **자료가 실제로 들어 있는
    ///     그 컴퓨터**에서 해야 하는 일이다. 다른 자리에서 원격으로 남의 회사 컴퓨터 자료를
    ///     지우거나 되돌릴 수 있으면 안 된다.
    ///
    ///   [어떻게 아는가] 브라우저에게 "너 메인PC냐" 고 묻지 않는다 — 그건 얼마든지
    ///     그렇다고 답할 수 있다. **서버가 접속해 들어온 자리를 직접 본다.**
    ///     히트판 본체는 자료가 있는 그 컴퓨터에서 돈다. 그러니 그 컴퓨터에서 화면을 열면
    ///     자기 자신에게 붙는 것이고(로컬), 다른 컴퓨터에서 열면 바깥에서 들어온다.
    ///
    ///   ⚠️ 터널을 타고 들어온 요청은 **원래 주소가 헤더에 있다.** 그것을 먼저 본다.
    ///     헤더가 있는데 로컬만 보고 판단하면, 바깥 접속을 메인PC 로 오인한다.
    /// </summary>
    [HttpGet("is-main-pc")]
    public IActionResult IsMainPc()
    {
        // 터널(cloudflared)을 지나온 요청은 원래 주소를 헤더에 달고 온다.
        // 헤더가 **하나라도 있으면** 바깥에서 들어온 것이다 — 로컬일 수 없다.
        var viaTunnel =
            Request.Headers.ContainsKey("CF-Connecting-IP") ||
            Request.Headers.ContainsKey("X-Forwarded-For");

        var remote = HttpContext.Connection.RemoteIpAddress;
        var isLoopback = remote is not null && System.Net.IPAddress.IsLoopback(remote);

        return Ok(new { isMainPc = !viaTunnel && isLoopback });
    }

    /// <summary>
    /// 🔴 [디바이스 인증] 관문이 묻는다 — 이 기기 지금 쓸 수 있나? (20260816작2 · 사장님 전결)
    ///
    /// 사장님 설계: *"로그인 후 로딩화면에 기기슬롯 과정을 넣으면 되잖아"*
    ///   로그인은 통과했고(401 을 내지 않는다 — 20260815 §3 결재), 화면이 여기에 물어
    ///   **승인됐으면 그대로 ERP 로, 아니면 관문에 머문다.**
    ///
    /// 승인 대기 중인 기기가 **자기 상태를 확인하려고** 반복해서 부른다.
    /// 대표가 [예] 를 누르는 순간 approved=true 로 바뀌고, 화면은 그때 ERP 로 넘어간다.
    ///
    /// ⚠️ 로그인은 했으므로 인증은 있다. 남의 기기는 물을 수 없다 —
    ///   tenant_id 는 JWT 클레임에서만 온다(헌법 #2).
    /// </summary>
    [HttpGet("gate-status")]
    public async Task<IActionResult> GateStatus([FromQuery] string? deviceId, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid)) return Forbid();

        // 🔴 20260818작1 (증상③) — **번호가 없다고 무조건 통과시키던 구멍을 닫았다.**
        //
        //   [종전] `deviceId` 가 비면 `approved = true` 로 돌려줬다.
        //     ⇒ 관문이 **묻기만 하면 열리는 문**이었다. 번호를 안 보내면 그만이다.
        //
        //   [🔴 종전 주석의 걱정에 답한다 — 무시하고 닫지 않았다]
        //     그 자리엔 *"여기서 막으면 지문을 못 만드는 기기가 영영 못 들어온다"* 고 적혀 있었다.
        //     그 걱정은 **번호를 기기가 스스로 만들어 와야 하던 시절**의 것이다.
        //     지금은 **서버가 등록 때 번호를 발급해 내려준다**(RegisterOrRefreshAsync 가 device_id 를 돌려주고
        //     화면이 그것을 보관한다). ⇒ **번호 없는 접속 자체가 없어졌다.**
        //     ⚠️ 그래서 이 구멍은 1-1 이 들어간 **뒤에야** 닫을 수 있었다(작업지시서 §4 순서 ③).
        //
        //   [닫는 방식] 통과가 아니라 **대기**로 돌린다. 화면은 관문에 머물고,
        //     다음 접속에서 번호를 받으면 그때 넘어간다. **아무도 영구히 갇히지 않는다.**
        //     ⚠️ 로그인은 여전히 통과한다 — 여기서 하는 일은 화면 상태 판정뿐이다.
        if (string.IsNullOrWhiteSpace(deviceId))
            return Ok(new { approved = false, confirmCode = (string?)null });

        // 🔴 IsDeviceAllowedAsync 가 아니다 — 그것은 1-2 로 **메인PC 만** 통과시킨다.
        //   관문이 그 판정을 쓰면 승인받은 직원 기기가 영원히 대기 화면에 갇힌다(8/10 사고형).
        var approved = await _svc.IsDeviceApprovedAsync(deviceId, tid, ct);

        return Ok(new
        {
            approved,
            // 대표 화면과 **같은 번호**를 보여준다 — 대표가 눈으로 대조한다(사장님 결재).
            //   승인이 난 뒤에는 보여줄 이유가 없다.
            confirmCode = approved ? null : DeviceConfirmCode.From(deviceId)
        });
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
    public async Task<IActionResult> Approve(string id, [FromBody] ApproveDeviceRequest? body, CancellationToken ct)
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
            //
            // 🔴 2026-08-18 20260818작2 (2-5) — 대표가 **누구 기기인지** 함께 고른다.
            //   QR 로 들어온 폰은 주인이 비어 있다(등록 시점엔 알 수 없다).
            //   ⚠️ 안 고르면 null 이고, 그때는 기존 주인을 그대로 둔다(안 지운다).
            authKey = await _svc.ApproveAsync(id, tid, uid, body?.AssignUserId, ct);
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

        // 🔴 2026-08-18 20260818작2 (2-3) — 폰이 보관해 둔 **자기 번호**를 함께 넘긴다.
        //   지문은 브라우저가 바뀌면 흔들리지만 이 번호는 안 흔들린다.
        //   ⇒ 같은 폰이 다시 찍어도 **같은 줄**로 간다(사장님 8/16 증상② — 슬롯 중복).
        //   ⚠️ 처음 오는 폰은 null 이다. 그때는 종전대로 지문으로 찾는다(헌법 #37).
        var (ok, message, deviceId, adminContact) = await _svc.RegisterMobileByTokenAsync(
            body.Token, body.DeviceName ?? "모바일 기기", body.Fingerprint ?? "", ip, ua,
            body.DeviceId, ct);

        // 🔴 실패할 때도 연락처를 함께 보낸다 — **막혔을 때야말로 갈 곳이 필요하다**(헌법 #20).
        //   한도 초과는 직원 혼자 못 푼다(슬롯 추가·기기 해제는 대표만 가능).
        if (!ok) return BadRequest(new { message, adminContact });

        // 🔴 2026-08-18 20260818작2 — **대표 연락처를 여기 실어 보낸다** (직원이 갈 곳 · 헌법 #20).
        //
        //   [왜 별도 호출이 아닌가] 이 화면은 `[AllowAnonymous]` 다 — 폰에는 로그인 토큰이 없어서
        //     `/api/devices/admin-contact` 를 부를 수 없다(그 길은 로그인한 PC 관문용이다).
        //     ⇒ **이미 통과한 이 응답에 얹는다.** QR 토큰이 곧 그 회사 사람이라는 증명이다.
        //
        //   ⚠️ 못 찾으면 null 이고, 폰 화면은 그때 연락처 줄 없이 안내만 띄운다.
        //   🔴 이름·전화번호뿐이다(필요 최소). 고객사 안에서만 보이며 본사로 안 나간다(헌법 #18·#22).
        //
        //   ⚠️ 회사(tenant)를 **컨트롤러가 정하지 않는다** — 그 값은 QR 토큰에서만 나온다(헌법 #2).
        //     그래서 서비스가 등록을 끝내며 **자기가 아는 회사의** 연락처를 함께 돌려준다.

        // 🔴 폰이 이 번호를 받아 보관해야 다음번에 같은 줄로 온다 — 응답에 반드시 실린다(2-2 의 짝).
        return Ok(new { message, deviceId, adminContact });
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

        // 🔴 20260818작1 (1-1) — **어느 줄을 열지는 세션이 정한다. 키가 정하지 않는다.**
        //
        //   [무엇이 문제였나] 종전엔 키만 서비스로 넘겼고, 서비스가 **그 키를 가진 줄을 찾아** 열었다.
        //     ⇒ 남의 키를 넣으면 **남의 줄이 열렸다** — 인증키가 회사 공용 열쇠였다.
        //
        //   [고침] 이 세션이 관문에서 받은 **장비넘버**를 함께 넘긴다.
        //     서버는 그 줄을 먼저 잡고, **그 줄의 해시와만** 대조한다.
        //     ⇒ 남의 키를 넣어도 **자기 줄에서 틀림으로 끝난다.**
        //
        //   ⚠️ 헤더를 먼저 보고, 없으면 본문 값을 쓴다 — 화면이 둘 중 무엇으로 보내든 받는다.
        //     🔴 이 값은 **신분 증명이 아니다.** 남의 번호를 넣어도 그 줄의 키를 모르면 못 연다.
        //       번호가 하는 일은 **"어느 줄을 대조할 것인가"** 하나뿐이다.
        var sessionDeviceId = Request.Headers["X-HitPan-Device-Id"].ToString();
        if (string.IsNullOrWhiteSpace(sessionDeviceId))
            sessionDeviceId = body.DeviceId;

        if (string.IsNullOrWhiteSpace(sessionDeviceId))
            return BadRequest(new { message = "기기 정보를 확인할 수 없습니다. 화면을 새로고침한 뒤 다시 시도해 주세요." });

        var deviceId = await _svc.VerifyAuthKeyAsync(body.AuthKey.Trim(), tid, sessionDeviceId.Trim(), ct);

        if (deviceId is null)
            // 어느 쪽이 틀렸는지 알려주지 않는다 — 알려주면 찍어 맞히는 데 도움이 된다.
            return BadRequest(new { message = "인증키가 올바르지 않습니다. 관리자에게 다시 확인해 주세요." });

        return Ok(new { message = "기기 인증이 완료되었습니다.", deviceId });
    }

    /// <summary>
    /// 🔴 인증키 재발급 — 대표계정만 (20260818작1 (1-8) · 사장님 결재 4).
    ///
    /// <para>사장님 문구: <i>"1회용 + 재발급 화면 필요 — 버튼 [인증키 재발급]"</i></para>
    ///
    /// <para>
    /// 🔴 <b>왜 필요한가</b> — 1-1 이 인증키를 <b>1회용</b>으로 만들었다.
    /// 직원이 <b>오타를 내거나</b> 키를 잃으면 되살릴 길이 없어 <b>영구 차단</b>된다.
    /// 그것이 8/10 사고와 같은 모양이라 <b>1-1 과 한 몸으로</b> 넣는다.
    /// </para>
    ///
    /// <para>⚠️ 새 키는 <b>응답에 한 번만</b> 실린다. 화면이 대표에게 보여주고 끝이다 —
    /// 알림·메일·문자에 싣지 않는다(사장님 8/16 오더 <i>"옆에서 보면 샌다"</i>).</para>
    /// </summary>
    [HttpPost("reissue-key/{id}")]
    public async Task<IActionResult> ReissueKey(string id, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        var uid = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(uid)) return Forbid();

        // 대표계정만 — 승인과 같은 문지기다(본사 계층은 백오피스 전용).
        var accountType = User.FindFirst("account_type")?.Value;
        if (accountType != "tenant_admin")
            return Forbid();

        try
        {
            var authKey = await _svc.ReissueAuthKeyAsync(id, tid, uid, ct);
            return Ok(new { message = "인증키를 다시 발급했습니다.", authKey });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    public sealed class VerifyKeyRequest
    {
        public string AuthKey { get; set; } = "";

        /// 🔴 이 세션의 장비넘버 (20260818작1 1-1). 헤더가 없을 때 쓰는 자리다.
        public string? DeviceId { get; set; }
    }

    public sealed class MobileRegisterRequest
    {
        public string Token { get; set; } = "";
        public string? DeviceName { get; set; }
        public string? Fingerprint { get; set; }

        /// <summary>
        /// 🔴 <b>이 폰이 지난번에 받아 보관해 둔 자기 번호</b> (20260818작2 · 2-3).
        /// <para>
        /// 값이 있으면 서버가 <b>지문보다 먼저</b> 이것으로 기존 줄을 찾는다 — PC 경로와 같은 순서다.
        /// ⚠️ 처음 오는 폰은 <c>null</c> 이며, 그때는 종전대로 지문으로 찾는다(헌법 #37 호환 보존).
        /// </para>
        /// </summary>
        public string? DeviceId { get; set; }
    }

    public sealed class RevokeDeviceRequest
    {
        public string? Reason { get; set; }
    }

    /// <summary>
    /// 기기 승인 요청 본문 (20260818작2 · 2-5).
    /// </summary>
    public sealed class ApproveDeviceRequest
    {
        /// <summary>
        /// 🔴 대표가 고른 <b>이 기기를 쓸 사람</b>. QR 로 들어온 폰은 주인이 비어 있다.
        /// <para>⚠️ 안 고르면 <c>null</c> 이고, 그때는 기존 주인을 <b>그대로 둔다</b>(안 지운다).</para>
        /// </summary>
        public string? AssignUserId { get; set; }
    }
}
