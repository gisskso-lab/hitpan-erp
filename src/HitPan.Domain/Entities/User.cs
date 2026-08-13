using HitPan.Domain.Common;
using HitPan.Domain.Enums;

namespace HitPan.Domain.Entities;

public class User : BaseEntity, ITenantEntity
{
    public string UserId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    // AccountType: ERP는 부모/자식 둘뿐 — tenant_admin(부모)/tenant_user(자식)만 사용.
    //   본사(platform)·대리점(reseller) 계층은 백오피스 전용이라 PlatformId/ResellerId 제거 (보안 격벽, 2026-06-18).
    public string AccountType { get; set; } = "tenant_user";
    public string? DeptId { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 지워진 계정인가(소프트 삭제). 작(2026-08-14).
    /// </summary>
    /// <remarks>
    /// 🔴 사장님 지적: <i>"퇴사처리한 인원이 계정에 로그인 할 수 있다는 건데. 계정은 죽여야지"</i>
    /// <para>
    /// ⚠️ <b>DB 에는 이 칸이 원래 있었는데 여기(EF 모델)에만 없었다.</b>
    /// 그래서 계정 목록(Dapper 로 <c>is_deleted</c> 를 직접 읽는다)에서는 지운 계정이
    /// 사라졌지만, <b>로그인은 EF 를 쓰는데 EF 가 이 칸의 존재조차 몰라</b> 그대로 열려 있었다.
    /// 한 표를 두 방식으로 읽으면서 한쪽만 알고 있으면 이런 구멍이 난다.
    /// </para>
    /// <para>
    /// <c>IsActive=0</c>(잠시 막음)과 다르다 — 이쪽은 <b>없앤 것</b>이다. 되살리지 않는다.
    /// 행 자체를 지우지 않는 이유는 전자근로계약서 서명 기록 같은 법적 증거가
    /// 이 계정을 가리키고 있기 때문이다.
    /// </para>
    /// </remarks>
    public bool IsDeleted { get; set; }

    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEnd { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime? PasswordChangedAt { get; set; }
}
