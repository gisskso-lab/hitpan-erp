using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Email;
using HitPan.Application.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace HitPan.Application.Services;

/// <summary>
/// 이메일 발송 서비스 (사장님 결재 2026-04-29).
/// 6종 문서(견적/수주/명세/계산/발주/매입) 자동발송 + PDF 첨부.
/// §#5 SMTP 패스워드 AES암호화 / §#3 send_history INSERT ONLY / §#18 본사 미수신 (고객사 본인 SMTP만).
/// </summary>
public sealed class EmailService : IEmailService
{
    private readonly IDbConnection _db;
    private readonly IPasswordEncryptor _enc;
    private readonly IPdfRenderService _pdf;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IDbConnection db, IPasswordEncryptor enc, IPdfRenderService pdf, ILogger<EmailService> logger)
    {
        _db = db; _enc = enc; _pdf = pdf; _logger = logger;
    }

    // ─── 설정 ───────────────────────────────────────────
    public async Task<EmailSettingsDto> GetSettingsAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        const string sql = """
            SELECT smtp_host AS SmtpHost, smtp_port AS SmtpPort, smtp_user AS SmtpUser,
                   (smtp_password_enc IS NOT NULL AND LENGTH(smtp_password_enc) > 0) AS HasPassword,
                   use_ssl AS UseSsl, from_address AS FromAddress, from_name AS FromName,
                   bcc_self AS BccSelf, is_active AS IsActive,
                   last_test_at AS LastTestAt, last_test_result AS LastTestResult, last_test_error AS LastTestError
            FROM email_settings WHERE tenant_id = @TenantId
            """;
        var row = await _db.QuerySingleOrDefaultAsync<EmailSettingsDto>(
            new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct))
            .ConfigureAwait(false);
        return row ?? new EmailSettingsDto();
    }

    public async Task UpdateSettingsAsync(string tenantId, UpdateEmailSettingsRequest req, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(req.SmtpHost)) throw new ArgumentException("SMTP 호스트 필수");
        if (string.IsNullOrWhiteSpace(req.FromAddress)) throw new ArgumentException("발신 이메일 필수");

        // 패스워드: null=기존 유지 / "" 또는 값=교체
        byte[]? newEnc = null;
        bool replacePw = req.SmtpPassword is not null;
        if (replacePw && !string.IsNullOrEmpty(req.SmtpPassword))
        {
            newEnc = _enc.Encrypt(req.SmtpPassword);
        }

        if (replacePw)
        {
            const string upsertWithPw = """
                INSERT INTO email_settings
                  (tenant_id, smtp_host, smtp_port, smtp_user, smtp_password_enc, use_ssl,
                   from_address, from_name, bcc_self, is_active)
                VALUES
                  (@TenantId, @Host, @Port, @User, @Pw, @Ssl, @From, @FromName, @Bcc, 1)
                ON DUPLICATE KEY UPDATE
                  smtp_host = VALUES(smtp_host), smtp_port = VALUES(smtp_port),
                  smtp_user = VALUES(smtp_user), smtp_password_enc = VALUES(smtp_password_enc),
                  use_ssl = VALUES(use_ssl), from_address = VALUES(from_address),
                  from_name = VALUES(from_name), bcc_self = VALUES(bcc_self), is_active = 1
                """;
            await _db.ExecuteAsync(new CommandDefinition(upsertWithPw, new
            {
                TenantId = tenantId, Host = req.SmtpHost, Port = req.SmtpPort,
                User = req.SmtpUser, Pw = newEnc ?? Array.Empty<byte>(),
                Ssl = req.UseSsl ? 1 : 0,
                From = req.FromAddress, FromName = req.FromName, Bcc = req.BccSelf ? 1 : 0
            }, cancellationToken: ct)).ConfigureAwait(false);
        }
        else
        {
            // 패스워드 그대로 두고 나머지만 갱신
            const string upsertNoPw = """
                INSERT INTO email_settings
                  (tenant_id, smtp_host, smtp_port, smtp_user, use_ssl, from_address, from_name, bcc_self, is_active)
                VALUES
                  (@TenantId, @Host, @Port, @User, @Ssl, @From, @FromName, @Bcc, 1)
                ON DUPLICATE KEY UPDATE
                  smtp_host = VALUES(smtp_host), smtp_port = VALUES(smtp_port),
                  smtp_user = VALUES(smtp_user), use_ssl = VALUES(use_ssl),
                  from_address = VALUES(from_address), from_name = VALUES(from_name),
                  bcc_self = VALUES(bcc_self), is_active = 1
                """;
            await _db.ExecuteAsync(new CommandDefinition(upsertNoPw, new
            {
                TenantId = tenantId, Host = req.SmtpHost, Port = req.SmtpPort,
                User = req.SmtpUser, Ssl = req.UseSsl ? 1 : 0,
                From = req.FromAddress, FromName = req.FromName, Bcc = req.BccSelf ? 1 : 0
            }, cancellationToken: ct)).ConfigureAwait(false);
        }
    }

    // ─── 테스트 ────────────────────────────────────────
    public async Task<TestSmtpResponse> TestSmtpAsync(string tenantId, CancellationToken ct = default)
    {
        var (host, port, user, pw, ssl, from, fromName, _) = await LoadSmtpAsync(tenantId, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(host))
            return new TestSmtpResponse { Success = false, Error = "SMTP 설정이 없습니다." };

        try
        {
            using var client = new SmtpClient();
            var secure = ssl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
            await client.ConnectAsync(host, port, secure, ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(user))
                await client.AuthenticateAsync(user, pw, ct).ConfigureAwait(false);
            await client.DisconnectAsync(true, ct).ConfigureAwait(false);

            await UpdateLastTestAsync(tenantId, "success", null, ct).ConfigureAwait(false);
            return new TestSmtpResponse { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Email] SMTP 테스트 실패 tenant={Tenant}", tenantId);
            await UpdateLastTestAsync(tenantId, "failed", ex.Message, ct).ConfigureAwait(false);
            return new TestSmtpResponse { Success = false, Error = ex.Message };
        }
    }

    private async Task UpdateLastTestAsync(string tenantId, string result, string? err, CancellationToken ct)
    {
        const string sql = """
            UPDATE email_settings
            SET last_test_at = @Now, last_test_result = @Result, last_test_error = @Err
            WHERE tenant_id = @TenantId
            """;
        await _db.ExecuteAsync(new CommandDefinition(sql,
            new { Now = DateTime.Now, Result = result, Err = err, TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    // ─── 발송 ──────────────────────────────────────────
    public async Task<SendEmailResponse> SendDocumentAsync(string tenantId, string? userId, SendDocumentEmailRequest req, CancellationToken ct = default)
    {
        var (host, port, user, pw, ssl, from, fromName, bccSelf) = await LoadSmtpAsync(tenantId, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(host))
            return new SendEmailResponse { Success = false, Error = "SMTP 설정이 없습니다. /settings/email 에서 등록하세요." };
        if (string.IsNullOrWhiteSpace(req.RecipientEmail))
            return new SendEmailResponse { Success = false, Error = "받는사람 이메일이 비어있습니다." };

        var emailId = Guid.NewGuid().ToString();
        byte[]? pdfBytes = null;
        string? pdfFileName = null;

        // 1) PDF 렌더링 (옵션)
        if (req.AttachPdf && !string.IsNullOrWhiteSpace(req.DocumentId))
        {
            try
            {
                var (bytes, fname) = await _pdf.RenderDocumentAsync(tenantId, req.DocumentType, req.DocumentId, ct).ConfigureAwait(false);
                pdfBytes = bytes;
                pdfFileName = fname;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Email] PDF 렌더 실패. 첨부 없이 발송 진행.");
            }
        }

        // 2) MimeMessage 빌드
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(fromName ?? from, from));
        foreach (var to in req.RecipientEmail.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            msg.To.Add(MailboxAddress.Parse(to.Trim()));
        if (!string.IsNullOrWhiteSpace(req.CcEmail))
            foreach (var cc in req.CcEmail.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                msg.Cc.Add(MailboxAddress.Parse(cc.Trim()));
        if (bccSelf) msg.Bcc.Add(MailboxAddress.Parse(from));
        msg.Subject = req.Subject;

        var builder = new BodyBuilder { TextBody = req.Body };
        if (pdfBytes is not null && pdfFileName is not null)
            builder.Attachments.Add(pdfFileName, pdfBytes, new ContentType("application", "pdf"));
        msg.Body = builder.ToMessageBody();

        // 3) 발송
        string status = "queued";
        string? errMsg = null;
        string? smtpResp = null;
        try
        {
            using var client = new SmtpClient();
            var secure = ssl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
            await client.ConnectAsync(host, port, secure, ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(user))
                await client.AuthenticateAsync(user, pw, ct).ConfigureAwait(false);
            smtpResp = await client.SendAsync(msg, ct).ConfigureAwait(false);
            await client.DisconnectAsync(true, ct).ConfigureAwait(false);
            status = "sent";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Email] 발송 실패 tenant={Tenant} doc={DocNo}", tenantId, req.DocumentNo);
            status = "failed";
            errMsg = ex.Message;
        }

        // 4) 이력 INSERT (§#3 INSERT ONLY)
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        const string histSql = """
            INSERT INTO email_send_history
              (email_id, tenant_id, sent_at, sent_by_user, document_type, document_no, document_id,
               partner_id, recipient_email, cc_email, subject, body_text, has_attachment, status,
               error_message, smtp_response)
            VALUES
              (@Id, @TenantId, @Now, @UserId, @DocType, @DocNo, @DocId,
               @PartnerId, @Recipient, @Cc, @Subject, @Body, @HasAtt, @Status,
               @Err, @Resp)
            """;
        await _db.ExecuteAsync(new CommandDefinition(histSql, new
        {
            Id = emailId, TenantId = tenantId, Now = DateTime.Now, UserId = userId,
            DocType = req.DocumentType, DocNo = req.DocumentNo, DocId = req.DocumentId,
            PartnerId = req.PartnerId, Recipient = req.RecipientEmail, Cc = req.CcEmail,
            Subject = req.Subject, Body = req.Body, HasAtt = pdfBytes is not null ? 1 : 0,
            Status = status, Err = errMsg, Resp = smtpResp
        }, cancellationToken: ct)).ConfigureAwait(false);

        // 5) 첨부 메타 INSERT (PDF 보관은 v2 — 메타만 기록)
        if (pdfBytes is not null && pdfFileName is not null)
        {
            const string attSql = """
                INSERT INTO email_attachment_history (attachment_id, email_id, tenant_id, filename, file_size_bytes, mime_type)
                VALUES (@AttId, @EmailId, @TenantId, @FileName, @Size, 'application/pdf')
                """;
            await _db.ExecuteAsync(new CommandDefinition(attSql, new
            {
                AttId = Guid.NewGuid().ToString(), EmailId = emailId, TenantId = tenantId,
                FileName = pdfFileName, Size = (long)pdfBytes.Length
            }, cancellationToken: ct)).ConfigureAwait(false);
        }

        return new SendEmailResponse
        {
            Success = status == "sent",
            EmailId = emailId,
            Error = errMsg
        };
    }

    // ─── 이력 ──────────────────────────────────────────
    public async Task<List<EmailHistoryDto>> GetHistoryAsync(string tenantId, string? documentType, int limit = 100, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        const string sql = """
            SELECT email_id AS EmailId, sent_at AS SentAt,
                   document_type AS DocumentType, document_no AS DocumentNo,
                   partner_id AS PartnerId, recipient_email AS RecipientEmail,
                   subject AS Subject, has_attachment AS HasAttachment,
                   status AS Status, error_message AS ErrorMessage
            FROM email_send_history
            WHERE tenant_id = @TenantId
              AND (@Type IS NULL OR document_type = @Type)
            ORDER BY sent_at DESC LIMIT @Limit
            """;
        var rows = await _db.QueryAsync<EmailHistoryDto>(new CommandDefinition(sql,
            new { TenantId = tenantId, Type = documentType, Limit = limit },
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToList();
    }

    // ─── SMTP 자격증명 로드 ──────────────────────────
    private async Task<(string Host, int Port, string User, string Pw, bool Ssl, string From, string? FromName, bool BccSelf)> LoadSmtpAsync(string tenantId, CancellationToken ct)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        const string sql = """
            SELECT smtp_host, smtp_port, smtp_user, smtp_password_enc,
                   use_ssl, from_address, from_name, bcc_self
            FROM email_settings WHERE tenant_id = @TenantId AND is_active = 1
            """;
        var row = await _db.QuerySingleOrDefaultAsync<dynamic>(
            new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct))
            .ConfigureAwait(false);
        if (row is null) return ("", 0, "", "", false, "", null, false);

        var host = (string)(row.smtp_host ?? "");
        int port = Convert.ToInt32(row.smtp_port ?? 587);
        var user = (string)(row.smtp_user ?? "");
        byte[] enc = row.smtp_password_enc is byte[] b ? b : Array.Empty<byte>();
        var pw = enc.Length > 0 ? _enc.Decrypt(enc) : "";
        bool ssl = Convert.ToInt32(row.use_ssl ?? 1) == 1;
        var from = (string)(row.from_address ?? "");
        var fromName = row.from_name as string;
        bool bcc = Convert.ToInt32(row.bcc_self ?? 0) == 1;
        return (host, port, user, pw, ssl, from, fromName, bcc);
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State != ConnectionState.Open && _db is DbConnection dc)
            await dc.OpenAsync(ct).ConfigureAwait(false);
    }
}
