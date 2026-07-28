using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Auth;

public class LoginRequest
{
    // [EmailAddress] 제거 (봉합 2026-07-07): 부모계정을 아이디 방식(예: hitpan_admin)으로
    //   등록하도록 바뀜(설치마법사). email 컬럼을 아이디로 재사용하므로 로그인 식별자가
    //   이메일 형식이 아닐 수 있다. [EmailAddress]가 남아있으면 [ApiController] 자동 모델검증이
    //   아이디 로그인을 AuthService 도달 전 400으로 차단(헌법 #20 워크플로우 끊김) → 제거.
    [Required]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    // ── 기기 기반 라이선싱용 (선택값; 미지원 클라이언트는 그대로 null) ──
    /// <summary>브라우저 지문 (SHA-256 또는 간이 해시).</summary>
    public string? DeviceFingerprint { get; set; }

    /// <summary>pc / mobile / tablet</summary>
    public string? DeviceType { get; set; }

    /// <summary>사용자가 붙인 기기 이름(선택).</summary>
    public string? DeviceName { get; set; }
}
