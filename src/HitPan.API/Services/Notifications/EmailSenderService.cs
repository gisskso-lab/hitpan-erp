namespace HitPan.API.Services.Notifications;

// ─────────────────────────────────────────────────────────────────────────────
// EmailSenderService — AWS SES / SendGrid 스텁 (WS-20260601-14)
// 백엔드 매니저 + 보안 매니저 2 합의 스텁.
//   - 정식 키는 환경변수 (Notifications:Email:Provider / ApiKey) + 사장님 결재 후
//   - 현재는 스텁: 로그만 저장, 실제 전송 없음
//   - DKIM·SPF·DMARC 정합 헤더: From=noreply@hitpan.kr 고정
//   - 본문 = 시리얼 + 활성화 링크 (평문 임시비번 절대 금지, SMS 채널 분리)
//   - DKIM 본문 토큰 = 30분 만료 (호출 측에서 생성·검증)
//   - 헌법 #15: 빈 catch 금지 → _logger.LogWarning(ex, ...)
//
// 🔴 20261015작B1 갈래 C(G4) — 이 통로는 현재 **죽어 있다**: 호출자 0곳
//    (Program.cs DI 등록 한 줄뿐 · 주입·호출 전수 0건). 제거하지 않는다(#37 「안 읽힌다 ≠ 잔재」).
//    ⚠️ 이 통로를 살리기 전에 LooksLikeTempPassword(아래 임시비번 차단)의 **오탐**을 먼저 고칠 것 —
//       주석은 "키워드 + 8자 이상 영숫자 인접 패턴"이라고 적혀 있는데 코드는 **낱말 포함만** 본다.
//       정상 안내문에 "임시비번"이 한 번 나오기만 해도 전량 차단된다. (범위 밖 — 통합 여부는 B5 이후 재판단)
//    ⚠️ 이름 혼동 경고 — 메일·SMS 축은 INotificationDispatcher(이 파일 계열)이고,
//       화면 축 INotificationService(HitPan.Application.Interfaces)는 **별개**다.
//       후자는 SignalR 로 실제 쓰인다 — 둘을 같은 것으로 보고 고치면 산 코드를 건드린다.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class EmailSenderService : IEmailSender
{
    private const string FromAddress = "noreply@hitpan.kr";

    private readonly ILogger<EmailSenderService> _logger;
    private readonly IConfiguration _config;

    public EmailSenderService(ILogger<EmailSenderService> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    public Task<NotificationResult> SendAsync(string to, string subject, string bodyHtml, string activationToken, CancellationToken ct = default)
    {
        var provider = _config.GetValue<string>("Notifications:Email:Provider") ?? "stub";
        var apiKey = _config.GetValue<string>("Notifications:Email:ApiKey");

        try
        {
            if (string.IsNullOrWhiteSpace(to) || !to.Contains('@'))
            {
                _logger.LogWarning("[EmailSender] invalid recipient: {To}", to);
                return Task.FromResult(new NotificationResult(false, null, "invalid_recipient", 1));
            }

            // 평문 임시비번 저장 차단 (사장님 헌법 #18·#22 정합 + 2채널 분리 원칙)
            if (LooksLikeTempPassword(bodyHtml))
            {
                _logger.LogError("[EmailSender] BLOCKED — body contains temp password pattern. 2-channel separation violated.");
                return Task.FromResult(new NotificationResult(false, null, "blocked_temp_password_in_email", 1));
            }

            if (string.IsNullOrWhiteSpace(apiKey) || provider.Equals("stub", StringComparison.OrdinalIgnoreCase))
            {
                // 스텁 모드: 로그만, 실제 전송 없음
                _logger.LogInformation(
                    "[EmailSender:stub] from={From} to={To} subject={Subject} tokenLen={TokenLen}",
                    FromAddress, to, subject, activationToken?.Length ?? 0);

                // 🔴 20261015작B1 갈래 C — 거짓 성공 제거.
                //    한 통도 보내지 않았으므로 Success=false 다. 벤더 messageId 도 없다(가짜 id 금지).
                //    사유코드 2종: 열쇠가 없으면 not_configured · provider 가 stub 이면 stub_mode.
                var stubReason = string.IsNullOrWhiteSpace(apiKey) ? "not_configured" : "stub_mode";
                return Task.FromResult(new NotificationResult(false, null, stubReason, 1));
            }

            // 정식 키 미보유 가정: provider 분기는 사장님 결재 후 SES/SendGrid SDK 연결
            _logger.LogInformation("[EmailSender:{Provider}] would send to={To} (key configured)", provider, to);

            // 🔴 20261015작B1 갈래 C — "would send" 는 "sent" 가 아니다.
            //    열쇠는 설정됐지만 벤더 SDK 가 아직 0줄이므로 여전히 스텁이다 ⇒ stub_mode.
            return Task.FromResult(new NotificationResult(false, null, "stub_mode", 1));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EmailSender] send failed to={To}", to);
            return Task.FromResult(new NotificationResult(false, null, ex.Message, 1));
        }
    }

    private static bool LooksLikeTempPassword(string body)
    {
        if (string.IsNullOrEmpty(body)) return false;
        // 임시비번/temp password/임시 비밀번호 키워드 + 8자 이상 영숫자 인접 패턴이면 차단
        var lower = body.ToLowerInvariant();
        return lower.Contains("임시비번") || lower.Contains("임시 비밀번호") || lower.Contains("temp password") || lower.Contains("temporary password");
    }
}
