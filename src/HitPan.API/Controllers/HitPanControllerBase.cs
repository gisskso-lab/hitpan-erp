using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 히트판 API 컨트롤러 공통 베이스.
/// HttpContext.Items["TenantId"] · ["UserId"] 반복 호출(169곳) 제거를 위한 단일 접근점.
///
/// 사용 예:
///   public class FooController : HitPanControllerBase {
///     public async Task&lt;IActionResult&gt; Get() {
///         var tenantId = TenantId;  // 이전: HttpContext.Items["TenantId"]?.ToString()
///         ...
///     }
///   }
///
/// 마이그레이션 가이드:
///   - 신규 컨트롤러는 HitPanControllerBase 상속
///   - 기존 ControllerBase 상속 컨트롤러는 점진 전환 (v1.1)
/// </summary>
public abstract class HitPanControllerBase : ControllerBase
{
    /// <summary>
    /// JWT 클레임에서 추출된 현재 테넌트 ID.
    /// TenantMiddleware가 HttpContext.Items["TenantId"]에 세팅한 값.
    /// 인증 미들웨어 통과 후에는 반드시 값이 존재해야 하므로 null 반환 시 호출자가 401 처리.
    /// </summary>
    protected string? TenantId => HttpContext.Items["TenantId"]?.ToString();

    /// <summary>현재 로그인 사용자 ID (JWT user_id 클레임)</summary>
    protected string? UserId => HttpContext.Items["UserId"]?.ToString();

    /// <summary>계정 유형 (tenant_admin / tenant_user / platform_admin / reseller_admin)</summary>
    protected string? AccountType => HttpContext.Items["AccountType"]?.ToString();

    /// <summary>테넌트 ID가 없으면 401 Unauthorized 반환하는 보조 헬퍼.</summary>
    protected IActionResult? EnsureTenant()
    {
        if (string.IsNullOrEmpty(TenantId))
            return Unauthorized(new { error = "테넌트 정보가 없습니다. 다시 로그인해주세요." });
        return null;
    }

    /// <summary>플랫폼 관리자 여부 (본사 관리 메뉴)</summary>
    protected bool IsPlatformAdmin => AccountType == "platform_admin";

    /// <summary>테넌트 관리자 여부 (고객사 어드민)</summary>
    protected bool IsTenantAdmin => AccountType == "tenant_admin";
}
