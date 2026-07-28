namespace HitPan.API.Services.Notifications;

// ─────────────────────────────────────────────────────────────────────────────
// NotificationDispatcher — 이메일·SMS 2채널 분리 발송 (WS-20260601-14)
//   - 헌법 #16 절대 정합: Task.WhenAll(SendEmail, SendSms) 절대 금지.
//     본 디스패처는 두 채널 모두 "순차" 처리. 각 Sender는 자체 HttpClient/SDK 사용 (MySqlConnection 미관여).
//   - 2채널 분리 원칙: 활성화 링크는 이메일로만, 임시비번은 SMS로만.
//   - 재시도 지수백오프 (1m·5m·30m) + DLQ 24h (메모리 큐 스텁, 정식 영속화는 결재 후).
//   - 실패 분기:
//       이메일 실패 → SMS 단독
//       SMS 실패 → 이메일 단독
//       둘 다 실패 → 본사 어드민 알림 (로그 LogError + DLQ 적재)
//   - 헌법 #15: 빈 catch 금지 → 모든 catch는 _logger.LogWarning(ex, ...)
// ─────────────────────────────────────────────────────────────────────────────

public interface INotificationDispatcher
{
    /// <summary>
    /// 활성화 통지 2채널 발송 (이메일=링크, SMS=시리얼 일부+임시비번+만료).
    /// </summary>
    Task<NotificationDispatchResult> SendActivationAsync(NotificationDispatchRequest req, CancellationToken ct = default);
}

public sealed record NotificationDispatchRequest(
    string Email,
    string Phone,
    string SerialKey,         // 라이선스 시리얼 (이메일=전체, SMS=뒤 6자리만)
    string ActivationToken,   // DKIM 30분 만료 토큰
    string TempPassword,      // SMS 전용 (이메일 본문 저장 금지)
    string ActivationUrl);    // 이메일 본문에만

public sealed record NotificationDispatchResult(
    bool EmailSent,
    bool SmsSent,
    string? EmailMessageId,
    string? SmsMessageId,
    string? EscalationReason); // 둘 다 실패 시 본사 어드민 알림 사유

public sealed class NotificationDispatcher : INotificationDispatcher
{
    // 재시도 백오프 (1분·5분·30분) — 사장님 헌법 #16 정합 순차 처리
    private static readonly TimeSpan[] Backoffs = { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30) };
    private const int DlqRetentionHours = 24;

    private readonly IEmailSender _email;
    private readonly ISmsSender _sms;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(IEmailSender email, ISmsSender sms, ILogger<NotificationDispatcher> logger)
    {
        _email = email;
        _sms = sms;
        _logger = logger;
    }

    public async Task<NotificationDispatchResult> SendActivationAsync(NotificationDispatchRequest req, CancellationToken ct = default)
    {
        // 1) 이메일 채널 (활성화 링크 전용, 평문 임시비번 저장 절대 금지)
        var emailSubject = "[히트판] 라이선스 활성화 안내";
        var emailBody = BuildEmailBody(req.SerialKey, req.ActivationUrl);
        var emailResult = await SendWithRetryAsync(
            ct,
            attempt => _email.SendAsync(req.Email, emailSubject, emailBody, req.ActivationToken, ct),
            channel: "email");

        // 2) SMS 채널 (시리얼 일부 + 임시비번 + 만료 안내, 60자 이내)
        //    ※ 헌법 #16: 위 이메일과 Task.WhenAll 금지 → 순차 await
        var smsBody = BuildSmsBody(req.SerialKey, req.TempPassword);
        var smsResult = await SendWithRetryAsync(
            ct,
            attempt => _sms.SendAsync(req.Phone, smsBody, ct),
            channel: "sms");

        // 3) 실패 분기 처리
        if (!emailResult.Success && !smsResult.Success)
        {
            var reason = $"both_failed: email={emailResult.Error} sms={smsResult.Error}";
            _logger.LogError("[NotifyDispatcher] 2-channel ALL FAILED — escalate to admin. {Reason}", reason);
            EnqueueDlq(req, reason);
            return new NotificationDispatchResult(false, false, null, null, reason);
        }
        if (!emailResult.Success)
        {
            _logger.LogWarning("[NotifyDispatcher] email failed, SMS standalone delivered. err={Err}", emailResult.Error);
        }
        if (!smsResult.Success)
        {
            _logger.LogWarning("[NotifyDispatcher] sms failed, email standalone delivered. err={Err}", smsResult.Error);
        }

        return new NotificationDispatchResult(
            emailResult.Success, smsResult.Success,
            emailResult.VendorMessageId, smsResult.VendorMessageId,
            null);
    }

    private async Task<NotificationResult> SendWithRetryAsync(
        CancellationToken ct,
        Func<int, Task<NotificationResult>> sendFunc,
        string channel)
    {
        NotificationResult last = new(false, null, "not_attempted", 0);
        for (int attempt = 0; attempt <= Backoffs.Length; attempt++)
        {
            try
            {
                last = await sendFunc(attempt);
                if (last.Success) return last with { AttemptCount = attempt + 1 };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[NotifyDispatcher:{Channel}] attempt {Attempt} threw", channel, attempt + 1);
                last = new NotificationResult(false, null, ex.Message, attempt + 1);
            }

            if (attempt < Backoffs.Length)
            {
                try
                {
                    await Task.Delay(Backoffs[attempt], ct);
                }
                catch (OperationCanceledException ex)
                {
                    _logger.LogWarning(ex, "[NotifyDispatcher:{Channel}] cancelled during backoff", channel);
                    return last;
                }
            }
        }
        return last;
    }

    private static string BuildEmailBody(string serial, string activationUrl)
    {
        // 평문 임시비번 저장 절대 금지 — 활성화 링크만
        return $"<p>안녕하세요, 히트판입니다.</p>" +
               $"<p>라이선스 시리얼: <b>{serial}</b></p>" +
               $"<p>아래 링크를 30분 안에 클릭하여 활성화해 주세요.</p>" +
               $"<p><a href=\"{activationUrl}\">{activationUrl}</a></p>" +
               $"<p>임시 로그인 정보는 별도 문자(SMS)로 발송되었습니다.</p>";
    }

    private static string BuildSmsBody(string serial, string tempPassword)
    {
        // 60자 이내 권장. 시리얼 뒤 6자리 + 임시비번 + 30분 만료
        var tail = serial.Length >= 6 ? serial[^6..] : serial;
        return $"[히트판] 시리얼:{tail} 임시비번:{tempPassword} 30분 내 활성화";
    }

    private void EnqueueDlq(NotificationDispatchRequest req, string reason)
    {
        // DLQ 24h 보관 — 메모리 큐 스텁. 정식 영속화(MariaDB notification_dlq 테이블)는 사장님 결재 후.
        _logger.LogError("[NotifyDispatcher:DLQ] retain {Hours}h reason={Reason} email={Email}",
            DlqRetentionHours, reason, req.Email);
    }
}
