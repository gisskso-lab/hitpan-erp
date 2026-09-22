namespace HitPan.Application.Interfaces;

/// <summary>
/// 🔴 <b>메인PC 「왕복 증명」</b> — 이 브라우저가 <b>히트판 본체가 있는 그 컴퓨터</b>에서 열렸는가.
/// </summary>
/// <remarks>
/// <para>
/// <b>사장님 오더 2026-09-22 (전결)</b> —
/// <i>"메인PC식별은 A안으로, 물리적인 방법으로 메인을 잡도록"</i> ·
/// <i>"기기가 직접 인증키를 인식하고 메인PC로 알아보는게 정확함"</i>
/// </para>
///
/// <para>
/// [무엇이 문제였나] 종전 판정은 <c>터널 헤더 없음 AND 루프백</c> 하나뿐이었다.
/// 그런데 고객은 <b>도메인(터널)으로 접속한다.</b> Cloudflare 가 <c>CF-Connecting-IP</c> 를
/// 반드시 붙이므로 <b>첫 조건에서 항상 탈락</b>한다 — 불안정이 아니라 <b>100% 실패</b>였다.
/// 그리고 cloudflared 가 그 PC 안에서 히트판을 다시 부르므로 소켓 주소는
/// <b>외부 접속이든 메인PC 든 항상 127.0.0.1</b> 이다. <b>IP 로는 영영 못 가린다.</b>
/// </para>
///
/// <para>
/// [무엇이 증거인가] 메인PC 와 클라이언트 PC 의 차이는 <b>터널을 지나와도 사라지지 않는다</b> —
/// <b>그 컴퓨터 안에 히트판 본체가 있는가.</b> API 는 루프백 전용으로 바인딩돼 있으므로
/// 클라이언트 PC 가 <c>127.0.0.1:5257</c> 을 두드리면 <b>자기 PC 를 두드리는 것</b>이고 거기엔 아무도 없다.
/// <b>남의 메인PC 를 두드릴 방법이 없다.</b>
/// </para>
///
/// <para>
/// [흐름] ①표 발급(도메인) → ②표를 들고 자기 PC 안의 히트판을 두드림(로컬 직결) → ③결과 확인(도메인).
/// 클라이언트 PC 는 ②에서 끝난다. <b>거짓말할 여지가 없다.</b>
/// </para>
///
/// <para>
/// 🔴 <b>증표를 네트워크로 내보내지 않는다.</b> ②를 받는 것도 ③에 답하는 것도 <b>같은 프로세스</b>다.
/// 그러므로 서버가 <b>자기 메모리에 표시만</b> 하면 된다 — 브라우저는 표(challenge)만 나른다.
/// 서명값이 브라우저를 거쳐 나가지 않으므로 <b>탈취해 재사용할 대상 자체가 없다.</b>
/// </para>
///
/// <para>
/// ⚠️ 표는 <b>1회용 · 짧은 유효기간 · 그 세션에만</b> 유효하다. 미리 받아 두거나 남의 표를 써도 통하지 않는다.
/// ⚠️ 표는 <b>메모리에만</b> 둔다(DB 아님). API 가 재시작되면 사라지지만 <b>다시 왕복하면 그만</b>이다 —
///    고객은 그 일이 일어난 줄도 모른다.
/// </para>
///
/// <para>
/// 헌법: #1 추가만 / #2 tenant_id 는 JWT 에서만 / #5 키는 봉인 상태로만 보관 / #39 운영 무수술
/// </para>
/// </remarks>
public interface IMainPcProofService
{
    /// <summary>
    /// ① 표를 발급한다 — <b>도메인(터널) 경유 호출</b>.
    /// </summary>
    /// <param name="tenantId">JWT 에서 온 회사 식별자 (헌법 #2 — 파라미터로 받지 않는다)</param>
    /// <param name="sessionKey">이 표를 묶어 둘 세션. 다른 세션이 주워 써도 통하지 않게 한다</param>
    string IssueChallenge(string tenantId, string sessionKey);

    /// <summary>
    /// ② 표를 들고 온 <b>로컬 직결 요청</b>을 받는다 — 이 컴퓨터 안에 본체가 있다는 뜻이다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>호출 전에 반드시 「진짜 로컬인가」를 확인해야 한다</b>(<c>MainPcOnlyAttribute.IsMainPc</c>).
    /// 그 확인 없이 부르면 터널을 지나온 요청도 로컬로 인정되어 <b>이 설계 전체가 무너진다.</b>
    ///
    /// <para>
    /// 여기서 <b>등록 상태까지 함께 판정</b>한다 — 봉인된 키가 이 PC 에서 풀리는가.
    /// 풀리면 등록된 그 컴퓨터이고, 안 풀리면 <b>컴퓨터가 바뀐 것</b>이다(새로 산 PC 로 이전).
    /// </para>
    ///
    /// <para>
    /// 🔴 <b>회사 식별자를 파라미터로 받지 않는다 — 표에서 꺼낸다</b>(헌법 #2 계통).
    /// 이 길은 <b>로그인 토큰 없이</b> 지나간다. 브라우저가 자기 PC 로 보내는 요청에
    /// 토큰까지 실어 나를 이유가 없고, 실으면 토큰이 한 곳 더 돌아다닌다.
    /// <b>표만으로 충분하다</b> — 표는 서버가 발급했고, 1회용이며, 짧게 살고,
    /// 최종 판정은 ③에서 <b>세션까지 대조</b>한 뒤에 나온다.
    /// </para>
    /// </remarks>
    Task<MainPcProofOutcome> ConfirmLocalAsync(string challenge, CancellationToken ct);

    /// <summary>
    /// ③ 결과를 묻는다 — <b>도메인(터널) 경유 호출</b>. 표를 쓰고 나면 버린다.
    /// </summary>
    /// <param name="pass">
    /// 🟢 통과한 경우에만 채워지는 <b>출입증</b>. 이후 자료관리 요청이 이것을 내민다.
    /// </param>
    MainPcProofOutcome Consume(string tenantId, string sessionKey, string challenge, out string? pass);

    /// <summary>
    /// 🔴 이 요청이 내민 <b>출입증</b>이 살아 있는가 — 자료관리 관문이 매 요청 묻는다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// [왜 기기ID 로 기억하지 않나] 그것이 <b>지금 뚫려 있는 바로 그 구멍</b>이다.
    /// 기존 <c>JoinServerRowAsync</c> 는 브라우저가 보내온 기기ID 를 믿었고,
    /// 그래서 외부 PC 가 메인PC 의 기기ID 를 적어 보내면 <b>그대로 메인PC 가 됐다.</b>
    /// 밖에서 온 값으로 신분을 정하면 언제나 자칭이 가능하다.
    /// </para>
    /// <para>
    /// [왜 사용자ID 로도 안 되나] 같은 사람이 옆자리 PC 에서 로그인해도 같은 값이다.
    /// 우리가 가리려는 것은 <b>사람이 아니라 컴퓨터</b>다.
    /// </para>
    /// <para>
    /// ⇒ <b>서버가 만들어 준 비밀</b>로만 기억한다. 추측할 수 없고, 서버 메모리에만 있고,
    /// <b>짧게 산다.</b> 만료되면 화면이 조용히 왕복을 다시 돌아 새로 받는다 —
    /// 메인PC 면 저절로 갱신되고, 아니면 그때 닫힌다. <b>고객은 이 일을 모른다.</b>
    /// </para>
    /// </remarks>
    bool IsPassValid(string? pass);

    /// <summary>
    /// 🔵 <b>메인PC 로 등록한다</b> — 새 키를 만들어 <b>이 PC 에 봉인</b>하고 기기 줄에 심는다.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>반드시 ②가 성공한 표에 대해서만</b> 불러야 한다. 그렇지 않으면 아무나 자기를 메인PC 로 만든다.
    /// ⚠️ 이미 메인PC 줄이 있으면 <b>그 줄을 갱신</b>한다. 새 줄을 만들지 않는다 —
    ///    <c>is_main_pc=1</c> 이 두 줄이 된 사고가 실제로 있었다(2026-09-13 사장님 실측).
    ///    DB 도 <c>uq_tenant_main_pc</c> 로 이를 막는다(DB-120).
    /// </remarks>
    Task<bool> RegisterThisPcAsync(string tenantId, string deviceId, CancellationToken ct);

    /// <summary>
    /// 🔵 <b>이 회사에 아직 자료가 없는가</b> — 등록 직후 「자료 복구」를 안내할지 가른다.
    /// </summary>
    /// <remarks>
    /// 새 컴퓨터에 히트판을 깔면 DB 가 비어 있다. 그때 아무 말도 없으면 고객은
    /// <b>빈 화면을 보고 자료가 날아간 줄 안다.</b> 갈 곳을 알려 주지 않는 안내는
    /// 흐름이 끊긴 것이다(헌법 #20).
    /// </remarks>
    Task<bool> IsDataEmptyAsync(string tenantId, CancellationToken ct);
}

/// <summary>왕복 증명의 결과 — 화면이 무엇을 할지 여기서 갈린다.</summary>
public enum MainPcProofOutcome
{
    /// <summary>표가 없거나 만료됐거나 남의 세션 것이다. (클라이언트 PC 의 정상 결과이기도 하다)</summary>
    NotProven = 0,

    /// <summary>🟢 이 컴퓨터에 본체가 있고, <b>등록된 그 컴퓨터가 맞다</b>. 자료관리를 연다.</summary>
    MainPcConfirmed = 1,

    /// <summary>🔵 이 컴퓨터에 본체가 있는데 <b>아직 등록된 적이 없다</b>. [메인PC 등록] 팝업.</summary>
    NotRegisteredYet = 2,

    /// <summary>
    /// 🔵 이 컴퓨터에 본체가 있는데 <b>봉인이 안 풀린다 = 등록된 그 컴퓨터가 아니다</b>.
    /// 새 컴퓨터로 옮겨 온 것이다. [메인PC 변경] 팝업.
    /// </summary>
    DifferentPc = 3,
}
