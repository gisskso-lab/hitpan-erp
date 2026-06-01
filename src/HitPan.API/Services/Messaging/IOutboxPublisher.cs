using System.Data;

namespace HitPan.API.Services.Messaging;

/// <summary>
/// 본사 ERP ↔ 백오피스 단방향 Outbox 퍼블리셔 (WS-20260601-20).
///
/// 절대 원칙
///   - 단방향: 백오피스(클라우드) → 본사 ERP(로컬) Push만. 역방향 절대 금지 (헌법 #18·#22).
///   - 평문 0: payload(JSON) 에 사업자번호·상호·대표자·주소·이메일·휴대폰 박제 절대 금지 (8명제 #3).
///   - 트랜잭션 원자성: 백오피스 비즈니스 INSERT 와 동일 IDbTransaction 으로 outbox INSERT.
///   - 헌법 #16: MySqlConnection 은 thread-safe 가 아니므로 호출자 트랜잭션 재사용 (Task.WhenAll 금지).
/// </summary>
public interface IOutboxPublisher
{
    /// <summary>
    /// messaging_outbox 에 메시지 1건 INSERT.
    /// 호출자 트랜잭션 안에서 atomic 하게 실행되어야 한다.
    /// </summary>
    /// <param name="connection">호출자가 소유한 백오피스 MySqlConnection (Open 상태).</param>
    /// <param name="transaction">호출자가 소유한 트랜잭션 (atomic 보장).</param>
    /// <param name="eventType">이벤트 종류 (예: TENANT_ISSUED / PAYMENT_ACTIVATED).</param>
    /// <param name="targetSerial">대상 시리얼 (HP-/HR-YYMM-XXXXXXXX-CRC).</param>
    /// <param name="payloadJson">메타 JSON. 평문 사업자 정보 박제 금지.</param>
    /// <param name="ct">취소 토큰.</param>
    /// <returns>새로 INSERT 된 outbox_id.</returns>
    Task<long> PublishAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string eventType,
        string targetSerial,
        string payloadJson,
        CancellationToken ct);
}
