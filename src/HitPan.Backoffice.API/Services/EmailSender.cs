using System.Net;
using System.Net.Mail;
using Dapper;
using MySqlConnector;

namespace HitPan.Backoffice.API.Services;

// SMTP 메일 송부 (헌법 #35 정합, 사장님 결재 2026-06-04)
//
// 자격증명 결재 전 행동:
//   - Smtp:Host 또는 Smtp:User 미저장 시 → 로그만 출력 (전송은 NOOP)
//   - 사장님 SMTP 결재 후 → 환경변수 저장 → 실 전송
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

        // 🔴 20261015작B1 B-2 (G2) — 발송 1회 = 표 1행. 호출출처는 여기서 한 번만 가린다
        //   (아래 세 경로가 같은 값을 쓴다 · 스택은 첫 await 전에만 온전하다).
        var caller = ResolveCaller();
        var hasAttachment = attachment is not null;

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user))
        {
            _logger.LogInformation("[EmailSender] SMTP 미저장 (자격증명 결재 대기). to={To} subject={Subject} attached={Att}",
                toEmail, subject, attachment is not null ? attachmentFileName : "-");
            // 🔴 경로 ① 미설정 — 여기가 지금까지 「아무도 모르는」 자리였다.
            //   호출자가 결과를 버리면 로그조차 안 읽히므로, 조회 가능한 자리에 사실을 남긴다.
            await RecordAsync(toEmail, subject, sent: false, "not_configured",
                "SMTP 자격증명 미저장 — 전송을 시도하지 않았다(결재 대기).", caller, hasAttachment, ct);
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
            // 🔴 경로 ② 성공 — 「진짜 1통」을 조회로 증명하는 유일한 자리다(§6 ① 2단 합격선).
            await RecordAsync(toEmail, subject, sent: true, "ok", null, caller, hasAttachment, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EmailSender] 전송 실패 to={To} subject={Subject}", toEmail, subject);
            // 🔴 경로 ③ 예외 — 사유는 예외 **종류 + 메시지 앞부분**만. 자격증명 값은 담지 않는다.
            await RecordAsync(toEmail, subject, sent: false, "smtp_error",
                $"{ex.GetType().Name}: {Clip(ex.Message, 400)}", caller, hasAttachment, ct);
            return false;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 20261015작B1 B-2 (G2) — 발송기록 적재
    //   표: bo_email_send_history (installer/backoffice/46_backoffice_email_send_history.sql)
    //   #3 INSERT ONLY · #15 적재 실패를 삼키지 않는다 · #22 본문은 담지 않는다
    //
    // 🔴 적재가 실패해도 **발송 판정은 바꾸지 않는다**(#20 흐름 불단절).
    //   기록이 안 남은 것은 LogWarning 으로 드러내고, 메일 자체의 성공·실패는 그대로 돌려준다.
    //   반대로 하면(적재 실패 시 false) 「메일은 갔는데 안 갔다고 보고」하는 더 나쁜 거짓말이 된다.
    // ══════════════════════════════════════════════════════════════════════════
    // 🔴 `internal` 인 이유 (선례: 이 csproj 의 `InternalsVisibleTo HitPan.Tests` 주석 — 같은 이유·같은 방식):
    //   **성공 행**은 이 차수에 실경로로 못 만든다 — SMTP 자격증명 **값**이 결재 대기(G5)이고
    //   메일 실발송은 금지다. 그래서 성공 행의 **모양**(result='sent' · reason_code='ok')만
    //   게이트가 이 함수를 직접 불러 잰다. §6 ① 이 승인한 **2단 합격선** 그대로다
    //   (1단 = 실패 행 실경로 · 2단 = 값 투입 후 성공 행 실경로).
    //   ⚠️ 운영 코드는 아래 세 경로에서만 부른다 — 외부에서 부르는 곳은 시험뿐이다.
    internal async Task RecordAsync(
        string toEmail, string subject, bool sent, string reasonCode, string? reason,
        string caller, bool hasAttachment, CancellationToken ct)
    {
        var cs = _config.GetConnectionString("BackofficeDb");
        if (string.IsNullOrWhiteSpace(cs))
        {
            // 커넥션 문자열이 없으면 적재할 자리가 없다. 조용히 넘기지 않는다(#15 정신).
            _logger.LogWarning("[EmailSender] 발송기록 미적재 — ConnectionStrings:BackofficeDb 미설정. to={To} result={Result}",
                toEmail, sent ? "sent" : "failed");
            return;
        }

        try
        {
            await using var db = new MySqlConnection(cs);
            await db.OpenAsync(ct);
            await db.ExecuteAsync(new CommandDefinition(@"
                INSERT INTO bo_email_send_history
                    (send_id, to_email, subject, result, reason_code, reason, sent_at, caller, has_attachment)
                VALUES
                    (@SendId, @ToEmail, @Subject, @Result, @ReasonCode, @Reason, UTC_TIMESTAMP(6), @Caller, @HasAtt)",
                new
                {
                    SendId = Guid.NewGuid().ToString(),
                    ToEmail = Clip(toEmail, 200),
                    Subject = Clip(subject, 200),
                    Result = sent ? "sent" : "failed",
                    ReasonCode = Clip(reasonCode, 40),
                    Reason = reason is null ? null : Clip(reason, 500),
                    Caller = Clip(caller, 120),
                    HasAtt = hasAttachment ? 1 : 0
                },
                cancellationToken: ct));
        }
        catch (MySqlException ex)
        {
            // 표가 아직 안 깔렸거나(1146) DB 가 안 붙는 경우. #15 — 한 줄 이상 남긴다.
            _logger.LogWarning(ex,
                "[EmailSender] 발송기록 적재 실패({Errno}) — 발송 판정은 그대로 둔다. to={To} result={Result} (46_backoffice_email_send_history.sql 적용 여부 확인)",
                ex.Number, toEmail, sent ? "sent" : "failed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[EmailSender] 발송기록 적재 실패(DB 외) — 발송 판정은 그대로 둔다. to={To} result={Result}",
                toEmail, sent ? "sent" : "failed");
        }
    }

    /// <summary>
    /// 호출출처를 스택에서 가린다 — 예: <c>LandingSignupController.Submit</c>.
    /// <para>🔴 왜 스택인가: 출처를 인자로 받으려면 <see cref="IEmailSender"/> 서명을 바꿔야 하고,
    /// 그러면 이 차수 갈래표 **밖** 파일(다른 호출자 3곳 + 시험 대역 <c>FakeEmail</c>)까지 번진다.
    /// 출처는 「있으면 좋은 단서」이지 판정 근거가 아니므로 최선노력으로 가리고,
    /// 못 가리면 <c>unknown</c> 으로 **행은 반드시 남긴다**.</para>
    /// <para>async 호출자는 프레임이 상태기계(<c>Controller+&lt;Submit&gt;d__12.MoveNext</c>)로 보인다 —
    /// 바깥 타입과 꺾쇠 안 이름으로 접어 사람이 읽을 모양으로 되돌린다.</para>
    /// </summary>
    private string ResolveCaller()
    {
        try
        {
            var st = new System.Diagnostics.StackTrace(fNeedFileInfo: false);
            for (var i = 0; i < st.FrameCount; i++)
            {
                var method = st.GetFrame(i)?.GetMethod();
                var declaring = method?.DeclaringType;
                if (method is null || declaring is null) continue;

                var outer = declaring.DeclaringType ?? declaring;
                if (outer == typeof(EmailSender)) continue;
                if (outer.Namespace is null) continue;
                if (outer.Namespace.StartsWith("System", StringComparison.Ordinal)) continue;
                if (outer.Namespace.StartsWith("Microsoft", StringComparison.Ordinal)) continue;

                var member = method.Name;
                if (member is "MoveNext" or "Start")
                {
                    // "<Submit>d__12" → "Submit"
                    var n = declaring.Name;
                    var lt = n.IndexOf('<');
                    var gt = n.IndexOf('>');
                    member = (lt >= 0 && gt > lt) ? n[(lt + 1)..gt] : n;
                }

                return Clip($"{outer.Name}.{member}", 120);
            }
        }
        catch (Exception ex)
        {
            // #15 — 출처를 못 가린 사실도 남긴다. 적재 자체는 unknown 으로 계속한다.
            _logger.LogWarning(ex, "[EmailSender] 호출출처 판별 실패 — caller=unknown 으로 적재한다.");
        }

        return "unknown";
    }

    private static string Clip(string? s, int max)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max]);
}
