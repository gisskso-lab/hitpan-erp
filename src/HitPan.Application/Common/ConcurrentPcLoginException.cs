namespace HitPan.Application.Common;

/// <summary>
/// 같은 계정이 이미 다른 PC 에서 쓰이고 있어 로그인을 거절할 때 던진다 (DB-127 축 B).
/// </summary>
/// <remarks>
/// 🔴 <b>왜 전용 예외인가</b> — <c>UnauthorizedAccessException</c> 으로 뭉뚱그리면
/// 화면이 <i>"비밀번호가 틀렸나"</i> 와 구분하지 못한다. 그러면 사용자는 멀쩡한 비밀번호를
/// 몇 번씩 다시 친다. <b>원인이 화면에 보여야 한다</b>(히트판 정신 — *"처음 보는 사람이 혼자 쓸 수 있냐"*).
///
/// <para>
/// 🔴 <b>왜 마지막 사용 시각을 싣나</b> — 사용자가 <i>"아, 아까 사무실 컴퓨터구나"</i> 를
/// 스스로 알아야 밀어낼지 말지 판단할 수 있다. 시각이 없으면 끊어도 되는지 알 수 없다.
/// </para>
///
/// <para>
/// ⚠️ 이 예외는 <b>자동으로 밀어내지 않는다.</b> 사장님 전결(2026-09-27):
/// *"거절이 기본. 밀어내기는 사용자가 확정할 때만."* ERP 는 보는 서비스가 아니라
/// <b>입력하는 서비스</b>라, 전표 쓰던 화면이 예고 없이 튕기면 입력이 날아간다.
/// ⬛ [낡은 줄 · 1.3.46 까지] 밀어내려면 클라이언트가 <c>LoginRequest.ForceSignOutOtherPc = true</c> 로 다시 부른다.
/// </para>
/// <para>
/// 🔴 <b>[지금 · 20260928작2 절B · K-4 다]</b> 밀어내는 길은 <b>없다.</b> <b>먼저 들어온 PC 가 이긴다</b> —
/// 사장님 원문 <i>"「다른 PC에서 사용 중입니다」 안내가 뜨고, 로그인 불가."</i>
/// <c>ForceSignOutOtherPc</c> 는 서버가 받되 <b>읽지 않는다.</b> 풀리는 경우는 앞 PC 의 로그아웃 또는
/// 앞 PC 세션 만료(마지막 사용 + 8h)뿐이다(쿨타임은 별도 트랙 1-b).
/// 로그인 경로는 이 예외를 던지기 전에 <c>security_alerts</c> 에 <c>pc_login_blocked</c> 1행을 남긴다(K-5).
/// </para>
/// </remarks>
public sealed class ConcurrentPcLoginException : Exception
{
    /// <summary>다른 PC 세션이 마지막으로 움직인 시각 (UTC). 화면이 사람에게 보여준다.</summary>
    public DateTime OtherPcLastActiveAtUtc { get; }

    public ConcurrentPcLoginException(DateTime otherPcLastActiveAtUtc)
        : base("이미 다른 PC에서 사용 중입니다.")
    {
        OtherPcLastActiveAtUtc = otherPcLastActiveAtUtc;
    }
}
