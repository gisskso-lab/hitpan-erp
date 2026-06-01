namespace HitPan.API.Services.Notifications;

// ─────────────────────────────────────────────────────────────────────────────
// WS-20260601-14 이메일·SMS 2채널 인프라 (헌법 #16 정합)
// 백엔드 매니저(Harvard·Oracle 30년) + 보안 매니저 2(안랩 30년) 합의 추상화.
// 벤더 교체 가능하도록 IEmailSender / ISmsSender 독립 분리.
//   - 이메일: AWS SES 또는 SendGrid 스텁 (정식 키 환경변수 + 사장님 결재 후)
//   - SMS:   알리고 또는 NHN Cloud 스텁 (정식 키 환경변수 + 사장님 결재 후)
//   - 2채널 분리 원칙: 평문 임시비번은 SMS로만, 활성화 링크는 이메일로만
//   - 헌법 #16: MySqlConnection + Task.WhenAll 금지 → Dispatcher에서 순차 처리
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>이메일 발송 추상화 (DKIM·SPF·DMARC 정합 헤더, 발신자 noreply@hitpan.kr 고정).</summary>
public interface IEmailSender
{
    /// <param name="to">수신자 이메일.</param>
    /// <param name="subject">제목.</param>
    /// <param name="bodyHtml">HTML 본문 (활성화 링크 포함, 평문 임시비번 절대 금지).</param>
    /// <param name="activationToken">DKIM 본문 토큰 (30분 만료).</param>
    /// <param name="ct">취소 토큰.</param>
    /// <returns>발송 결과 (성공·실패 + 벤더 messageId + 오류 메시지).</returns>
    Task<NotificationResult> SendAsync(string to, string subject, string bodyHtml, string activationToken, CancellationToken ct = default);
}

/// <summary>SMS 발송 추상화 (60자 이내 본문, 시리얼 일부 + 임시비번 + 만료 안내).</summary>
public interface ISmsSender
{
    /// <param name="toPhone">수신자 휴대전화 (+82 또는 010-... 정규화 책임은 호출 측).</param>
    /// <param name="body">SMS 본문 (60자 이내 권장).</param>
    /// <param name="ct">취소 토큰.</param>
    Task<NotificationResult> SendAsync(string toPhone, string body, CancellationToken ct = default);
}

/// <summary>이메일·SMS 공통 발송 결과.</summary>
public sealed record NotificationResult(
    bool Success,
    string? VendorMessageId,
    string? Error,
    int AttemptCount);
