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

    /// <summary>
    /// 🔴 <b>장비넘버</b> — 이 기기가 지난번에 서버에게 받아 보관해 둔 자기 번호 (20260816작2 · 명세서 §4-4).
    ///
    /// <para>
    /// 사장님 오더 *"기기슬롯 인증을 하면 그 기기는 <b>100번을 접속해도 한번만</b>"* 이 안 되던
    /// 이유가 정확히 이 칸이 없어서였다. 서버가 번호를 내려주고, 기기가 저장까지 하는데,
    /// <b>다음 접속에 도로 보내지 않았다</b> — 그래서 서버는 매번 처음 보는 기기로 여겼다.
    /// (명세서 §2-2: <i>"장비넘버는 있는데 안 보낸다 — 호출처 0곳"</i>)
    /// </para>
    ///
    /// <para>
    /// 이 값이 오면 서버는 <b>지문보다 먼저</b> 이것으로 기기를 찾는다(명세서 §4-3 순서 ②).
    /// 지문은 브라우저가 바뀌면 달라지지만 이 번호는 안 바뀐다 ⇒ Edge 로 들어오든 Chrome 으로
    /// 들어오든 <b>같은 줄</b>이다.
    /// </para>
    ///
    /// <para>⚠️ 선택값이다. 처음 오는 기기·옛 기기는 없다(null) — 그때는 종전대로 지문으로 찾는다.</para>
    /// </summary>
    public string? DeviceId { get; set; }
}
