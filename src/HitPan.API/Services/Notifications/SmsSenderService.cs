namespace HitPan.API.Services.Notifications;

// ─────────────────────────────────────────────────────────────────────────────
// SmsSenderService — 알리고 / NHN Cloud 스텁 (WS-20260601-14)
// 백엔드 매니저 + 보안 매니저 2 합의 스텁.
//   - 정식 키는 환경변수 (Notifications:Sms:Provider / ApiKey / Sender) + 사장님 결재 후
//   - 현재는 스텁: 로그만 저장, 실제 전송 없음
//   - SMS 본문 60자 이내: 시리얼 일부 + 임시비번 + 만료 안내 (2채널 분리: 이메일에는 임시비번 금지)
//   - 헌법 #15: 빈 catch 금지 → _logger.LogWarning(ex, ...)
// ─────────────────────────────────────────────────────────────────────────────
public sealed class SmsSenderService : ISmsSender
{
    private const int MaxSmsBodyLength = 60;

    private readonly ILogger<SmsSenderService> _logger;
    private readonly IConfiguration _config;

    public SmsSenderService(ILogger<SmsSenderService> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    public Task<NotificationResult> SendAsync(string toPhone, string body, CancellationToken ct = default)
    {
        var provider = _config.GetValue<string>("Notifications:Sms:Provider") ?? "stub";
        var apiKey = _config.GetValue<string>("Notifications:Sms:ApiKey");
        var sender = _config.GetValue<string>("Notifications:Sms:Sender") ?? "1666-0000";

        try
        {
            if (string.IsNullOrWhiteSpace(toPhone) || toPhone.Length < 9)
            {
                _logger.LogWarning("[SmsSender] invalid phone: {Phone}", toPhone);
                return Task.FromResult(new NotificationResult(false, null, "invalid_phone", 1));
            }

            if (string.IsNullOrEmpty(body))
            {
                _logger.LogWarning("[SmsSender] empty body to={Phone}", toPhone);
                return Task.FromResult(new NotificationResult(false, null, "empty_body", 1));
            }

            if (body.Length > MaxSmsBodyLength)
            {
                // 60자 초과는 경고만, 자르지는 않음 (LMS 전환은 정식 SDK 연결 시점에 판단)
                _logger.LogWarning("[SmsSender] body length {Len} exceeds SMS limit {Max} — vendor may downgrade to LMS",
                    body.Length, MaxSmsBodyLength);
            }

            if (string.IsNullOrWhiteSpace(apiKey) || provider.Equals("stub", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "[SmsSender:stub] from={Sender} to={Phone} bodyLen={Len}",
                    sender, MaskPhone(toPhone), body.Length);
                return Task.FromResult(new NotificationResult(true, $"stub-{Guid.NewGuid():N}", null, 1));
            }

            // 정식 키 미보유 가정: 알리고/NHN Cloud 분기는 사장님 결재 후 SDK 연결
            _logger.LogInformation("[SmsSender:{Provider}] would send to={Phone} (key configured)", provider, MaskPhone(toPhone));
            return Task.FromResult(new NotificationResult(true, $"{provider}-stub-{Guid.NewGuid():N}", null, 1));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SmsSender] send failed to={Phone}", MaskPhone(toPhone));
            return Task.FromResult(new NotificationResult(false, null, ex.Message, 1));
        }
    }

    private static string MaskPhone(string phone)
    {
        if (string.IsNullOrEmpty(phone) || phone.Length < 4) return "****";
        return string.Concat(phone.AsSpan(0, phone.Length - 4), "****");
    }
}
