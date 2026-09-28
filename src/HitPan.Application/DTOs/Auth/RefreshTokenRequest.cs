using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Auth;

public class RefreshTokenRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// 🔴 <b>서버가 헤더에서 직접 읽은 <c>User-Agent</c></b> — 갱신이 세션 행을 새로 만들 때
    /// 기기 종류를 이 값으로 정한다 (20260927작2 PM 결재 조건 C-5).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>왜 필요한가</b> — 종전 갱신 경로는 새 세션 행에 <b>항상 <c>'mobile'</c></b> 을 적었다.
    /// 그 행은 축 B 의 <c>device_kind='pc'</c> 조회에 안 잡히므로,
    /// <b>옛 토큰으로 갱신 한 번이면 로그인을 거치지 않고 차단 밖으로 나갈 수 있었다.</b>
    /// <para>
    /// 갱신 요청에는 클라이언트 신고값이 없다 ⇒ <b>User-Agent 단독 판정</b>이다
    /// (<c>DeviceTypeResolver.ResolveDeviceType(null, UserAgent)</c>).
    /// </para>
    /// <para>
    /// 🔴 <b>클라이언트가 채우는 칸이 아니다.</b> 컨트롤러가 헤더로 <b>무조건 덮어쓴다</b>(절F).
    /// 🚫 여기에 신고값 칸을 새로 만들지 마라 — 자진신고 입구를 하나 더 파는 일이다(PM 결재 C-5).
    /// </para>
    /// <para>⚠️ 값이 없으면(옛 클라이언트·헤더 없음) 종전과 같이 싼 칸(<c>mobile</c>)으로 떨어진다.</para>
    /// </remarks>
    public string? UserAgent { get; set; }
}
