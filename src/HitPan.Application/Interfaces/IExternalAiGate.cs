namespace HitPan.Application.Interfaces;

/// <summary>
/// 🔴 외부 AI 반출 게이트 — 서버 단일 판정 (20261006작9 §4-2 · PM 결재 §8-1).
/// </summary>
/// <remarks>
/// <para>
/// 선행검증([1-V] 20261006): 외부 AI HTTP 호출 지점 3곳(챗봇 폴백·AI직원 엔진·연결확인 핑) 중
/// 업무 데이터가 나가는 ①②를 이 게이트 하나가 문 3곳에서 막는다 —
/// 문① ChatbotService 외부 폴백 분기 · 문② 엔진 분기(TryRunAgentAsync) ·
/// 문③ AiAgentService.RunAsync 입구(심층방어 — 새 호출자가 문②를 우회해도 Tool 반출 0).
/// ③ 핑은 업무 데이터 0 이라 차단하지 않는다(PM 결재 §8-2).
/// </para>
/// <para>
/// 🔴 fail-closed: <c>ai_export_consents</c> 표에 해당 테넌트의 동의 기록이 <b>존재</b>할 때만 열림.
/// 표 부재·조회 예외·기록 0건 = 전부 닫힘. 지금은 동의 절차 자체가 없으므로 사실상 전면 차단 —
/// 재개는 작업지시서 §7(법무 계약·고지 + 사장님 결재) 후다. 그 전에는 게이트 해제·동의 기록
/// 임의 INSERT·동의 수집 화면 선개발 전부 금지.
/// </para>
/// </remarks>
public interface IExternalAiGate
{
    /// <summary>
    /// 이 테넌트의 외부 AI 반출이 열려 있는가. tenantId 는 JWT 클레임 유래 인자(헌법 #2).
    /// 판정 실패(표 부재·예외)는 던지지 않고 false(닫힘)를 반환한다.
    /// </summary>
    Task<bool> IsOpenAsync(string tenantId, CancellationToken ct = default);
}
