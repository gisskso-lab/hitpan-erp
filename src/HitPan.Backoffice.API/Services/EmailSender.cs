using System.Net;
using System.Net.Mail;

namespace HitPan.Backoffice.API.Services;

// SMTP 메일 송부 (헌법 #35 정합, 사장님 결재 2026-06-04)
//
// 자격증명 결재 전 행동:
//   - Smtp:Host 또는 Smtp:User 미박제 시 → 로그만 출력 (전송은 NOOP)
//   - 사장님 SMTP 결재 후 → 환경변수 박제 → 실 전송
//
// 헌법 정합:
//   #15 — 빈 catch 금지
//   #29 — SMTP 자격증명은 환경변수에서만
public interface IEmailSender
{
    Task<bool> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);

    // 첨부 1건 (PDF 계약서 등) — 사장님 결재 2026-06-04 협력업체 가입 흐름
    Task<bool> SendWithAttachmentAsync(
        string toEmail, string subject, string htmlBody,
        byte[] attachment, string attachmentFileName, string attachmentMimeType,
        CancellationToken ct = default);
}

public class EmailSender : IEmailSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailSender> _logger;

    public EmailSender(IConfiguration config, ILogger<EmailSender> logger)
    {
        _config = config;
        _logger = logger;
    }

    public Task<bool> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default) =>
        SendInternalAsync(toEmail, subject, htmlBody, attachment: null, attachmentFileName: null, attachmentMimeType: null, ct);

    public Task<bool> SendWithAttachmentAsync(
        string toEmail, string subject, string htmlBody,
        byte[] attachment, string attachmentFileName, string attachmentMimeType,
        CancellationToken ct = default) =>
        SendInternalAsync(toEmail, subject, htmlBody, attachment, attachmentFileName, attachmentMimeType, ct);

    private async Task<bool> SendInternalAsync(
        string toEmail, string subject, string htmlBody,
        byte[]? attachment, string? attachmentFileName, string? attachmentMimeType,
        CancellationToken ct)
    {
        var host = _config["Smtp:Host"];
        var user = _config["Smtp:User"];
        var pass = _config["Smtp:Password"];
        var from = _config["Smtp:From"] ?? "no-reply@hitpan.kr";
        var fromName = _config["Smtp:FromName"] ?? "히트판";
        var port = int.TryParse(_config["Smtp:Port"], out var p) ? p : 587;

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user))
        {
            _logger.LogInformation("[EmailSender] SMTP 미박제 (자격증명 결재 대기). to={To} subject={Subject} attached={Att}",
                toEmail, subject, attachment is not null ? attachmentFileName : "-");
            return false;
        }

        try
        {
            using var client = new SmtpClient(host, port)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(user, pass ?? "")
            };
            using var msg = new MailMessage
            {
                From = new MailAddress(from, fromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true,
                SubjectEncoding = System.Text.Encoding.UTF8,
                BodyEncoding = System.Text.Encoding.UTF8
            };
            msg.To.Add(toEmail);

            Attachment? att = null;
            MemoryStream? attStream = null;
            try
            {
                if (attachment is not null && !string.IsNullOrWhiteSpace(attachmentFileName))
                {
                    attStream = new MemoryStream(attachment);
                    att = new Attachment(attStream, attachmentFileName, attachmentMimeType ?? "application/octet-stream");
                    msg.Attachments.Add(att);
                }
                await client.SendMailAsync(msg, ct);
            }
            finally
            {
                att?.Dispose();
                attStream?.Dispose();
            }

            _logger.LogInformation("[EmailSender] sent to={To} subject={Subject} attached={Att}",
                toEmail, subject, attachmentFileName ?? "-");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EmailSender] 전송 실패 to={To} subject={Subject}", toEmail, subject);
            return false;
        }
    }
}
