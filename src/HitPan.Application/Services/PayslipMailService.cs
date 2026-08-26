using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Email;
using HitPan.Application.DTOs.Payroll;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// 급여명세서 일괄 메일발송. 20260826작6 W4.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>이 기능은 되돌릴 수 없다.</b> 잘못 나가면 전 직원에게 남의 연봉이 간다. 회수 불가다.
/// 그래서 <b>미리보기 → 사람이 눈으로 확인 → 발송</b> 2단으로 나눈다(사장님 반자동 원칙).
/// </para>
/// <para>
/// 🔴 <b>PDF 가 없으면 보내지 않는다.</b> <c>EmailService.SendDocumentAsync</c> 는 PDF 렌더가
/// 실패해도 <b>첨부 없이 메일을 보낸다</b>(그 안의 catch 가 그렇게 되어 있다). 거래명세서면
/// <i>"본문만이라도"</i> 가 말이 되지만 급여명세서는 <b>본문에 금액을 안 적는다</b>(②결재).
/// 그래서 렌더가 실패하면 직원은 <b>「급여명세서를 발송드립니다」 라고만 적힌 빈 메일</b>을 받고
/// 이력에는 <c>sent</c> 로 남는다 — 경리는 다 보낸 줄 안다. 전형적인 <b>"되는 척"</b> 이다.
/// ⇒ 여기서 <b>미리 렌더해서 성공한 것만</b> 발송에 넘긴다.
/// ⚠️ <c>EmailService</c> 는 <b>고치지 않는다</b> — 거래문서 8종이 쓰는 길이다(헌법 #1).
/// </para>
/// <para>
/// 🔴 <b>본문에 금액·항목을 넣지 않는다</b>(②결재, 사장님 2026-08-26:
/// <i>"히트판에서 발송되는 사내메일은 무조건 본문에 자료내용을 공개하지 않음"</i>).
/// </para>
/// </remarks>
public sealed class PayslipMailService : IPayslipMailService
{
    private readonly IDbConnection _db;
    private readonly IEmailService _email;
    private readonly IPdfRenderService _pdf;
    private readonly ILogger<PayslipMailService> _logger;

    /// <summary>결재·PDF·메일이 이 문서를 부르는 이름. 한 곳에서 정의한다.</summary>
    private const string PayslipDocType = PdfRenderService.PayslipDocType;

    public PayslipMailService(IDbConnection db, IEmailService email, IPdfRenderService pdf,
        ILogger<PayslipMailService> logger)
    {
        _db = db;
        _email = email;
        _pdf = pdf;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════
    //  미리보기 — 누가 받고 누가 못 받나
    // ═══════════════════════════════════════════════════════════════

    public async Task<PayslipSendPreviewDto> GetSendPreviewAsync(string tenantId, int year, int month,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var approvalRequired = await IsApprovalRequiredAsync(tenantId, ct).ConfigureAwait(false);
        var rows = await LoadRowsAsync(tenantId, year, month, null, ct).ConfigureAwait(false);

        var targets = rows.Select(r => ToTarget(r, approvalRequired)).ToList();

        return new PayslipSendPreviewDto
        {
            Year = year,
            Month = month,
            ApprovalRequired = approvalRequired,
            Targets = targets
        };
    }

    // ═══════════════════════════════════════════════════════════════
    //  발송
    // ═══════════════════════════════════════════════════════════════

    public async Task<SendPayslipMailResponse> SendAsync(string tenantId, string? actorUserId,
        SendPayslipMailRequest request, CancellationToken ct = default)
    {
        var response = new SendPayslipMailResponse();

        if (request.SlipIds is null || request.SlipIds.Count == 0) return response;

        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var approvalRequired = await IsApprovalRequiredAsync(tenantId, ct).ConfigureAwait(false);

        // 🔴 화면이 보낸 id 를 그대로 믿지 않는다. 서버가 ★다시 판정★ 한다.
        //    화면을 우회한 요청으로 미결재 명세서가 나가면 안 된다(⑤결재).
        //    ⚠️ tenant_id 조건이 여기 걸려 있어야 남의 회사 명세서를 못 부른다(헌법 #2).
        var rows = await LoadRowsAsync(tenantId, request.Year, request.Month, request.SlipIds, ct)
            .ConfigureAwait(false);

        var byId = rows.ToDictionary(r => r.SlipId, StringComparer.Ordinal);

        foreach (var slipId in request.SlipIds.Distinct(StringComparer.Ordinal))
        {
            // 요청에는 있는데 조회에 없다 = 남의 회사거나 그 달 명세가 아니다.
            if (!byId.TryGetValue(slipId, out var row))
            {
                response.Items.Add(new PayslipSendResultItemDto
                {
                    SlipId = slipId,
                    EmployeeName = "-",
                    Success = false,
                    Error = "명세서를 찾을 수 없습니다."
                });
                continue;
            }

            var target = ToTarget(row, approvalRequired);

            if (!target.CanSend)
            {
                response.Items.Add(new PayslipSendResultItemDto
                {
                    SlipId = slipId,
                    EmployeeName = target.EmployeeName,
                    RecipientEmail = target.RecipientEmail,
                    Success = false,
                    Error = target.BlockReasonLabel
                });
                continue;
            }

            var item = await SendOneAsync(tenantId, actorUserId, row, target, ct).ConfigureAwait(false);
            response.Items.Add(item);
        }

        return response;
    }

    /// <summary>한 사람에게 보낸다. 🔴 <b>PDF 를 먼저 만들고, 성공했을 때만</b> 발송한다.</summary>
    private async Task<PayslipSendResultItemDto> SendOneAsync(string tenantId, string? actorUserId,
        SlipRow row, PayslipSendTargetDto target, CancellationToken ct)
    {
        var item = new PayslipSendResultItemDto
        {
            SlipId = row.SlipId,
            EmployeeName = target.EmployeeName,
            RecipientEmail = target.RecipientEmail
        };

        // ① PDF 를 ★먼저★ 만든다. 실패하면 여기서 끝 — 빈 메일을 보내지 않는다.
        try
        {
            var (bytes, _) = await _pdf
                .RenderDocumentAsync(tenantId, PayslipDocType, row.SlipId, ct)
                .ConfigureAwait(false);

            if (bytes is null || bytes.Length == 0)
                throw new InvalidOperationException("명세서 PDF 가 비어 있습니다.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Payslip] PDF 렌더 실패 tenant={Tenant} slip={Slip}", tenantId, row.SlipId);

            // 🔴 이력에 failed 로 남긴다. 안 남기면 "안 보냈다" 는 사실이 어디에도 없다.
            await WriteFailedHistoryAsync(tenantId, actorUserId, row, target,
                $"명세서 PDF 를 만들지 못했습니다: {ex.Message}", ct).ConfigureAwait(false);

            item.Success = false;
            item.Error = "명세서 PDF 를 만들지 못했습니다.";
            return item;
        }

        // ② 발송. EmailService 가 PDF 를 다시 렌더하고 이력까지 남긴다.
        //    ⚠️ 여기서 렌더가 또 도는 것은 중복이지만, 이력·첨부 메타를 한 자리에서 남기려면
        //       그 경로를 그대로 쓰는 것이 맞다. ①은 "빈 메일 차단" 용 사전 확인이다.
        var req = new SendDocumentEmailRequest
        {
            DocumentType = PayslipDocType,
            DocumentId = row.SlipId,
            DocumentNo = ShortRef(row.SlipId),
            RecipientEmail = target.RecipientEmail!,
            Subject = $"{row.PayYear}년 {row.PayMonth}월 급여명세서",
            // 🔴 ②결재 — 본문에 금액·항목을 적지 않는다. 첨부로만 간다.
            Body = $"{row.PayYear}년 {row.PayMonth}월 급여명세서를 발송드립니다.\r\n첨부파일을 확인해 주세요.",
            AttachPdf = true
        };

        try
        {
            var res = await _email.SendDocumentAsync(tenantId, actorUserId, req, ct).ConfigureAwait(false);
            item.Success = res.Success;
            item.Error = res.Success ? null : (res.Error ?? "발송하지 못했습니다.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Payslip] 발송 실패 tenant={Tenant} slip={Slip}", tenantId, row.SlipId);
            item.Success = false;
            item.Error = ex.Message;
        }

        return item;
    }

    // ═══════════════════════════════════════════════════════════════
    //  판정
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 이 회사가 급여명세서에 <b>결재를 쓰는가</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>두 가지를 다 본다</b> — <c>is_enabled</c> 와 <b>결재선 줄 수</b>. 켜두기만 하고
    /// 결재선을 안 짜면 결재 문서가 <b>영영 안 생긴다</b>(14차 P2 사고 자리). 그 상태에서
    /// "승인된 것만 발송" 을 걸면 <b>한 통도 못 보낸다</b> — 워크플로우가 끊긴다(#20).
    /// </para>
    /// <para>
    /// ⚠️ 결재를 <b>안 쓰는 회사</b>는 확정(<c>confirmed</c>)만으로 보낼 수 있다. 결재를
    /// <b>쓰는 회사</b>는 승인 없이는 못 보낸다(⑤결재).
    /// </para>
    /// </remarks>
    private async Task<bool> IsApprovalRequiredAsync(string tenantId, CancellationToken ct)
    {
        var enabled = await _db.QueryFirstOrDefaultAsync<bool?>(new CommandDefinition(
            "SELECT is_enabled FROM approval_settings WHERE tenant_id = @TenantId AND doc_type = @DocType",
            new { TenantId = tenantId, DocType = PayslipDocType }, cancellationToken: ct)).ConfigureAwait(false);

        if (enabled is not true) return false;

        var lineCount = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM approval_doc_lines WHERE tenant_id = @TenantId AND doc_type = @DocType AND is_active = 1",
            new { TenantId = tenantId, DocType = PayslipDocType }, cancellationToken: ct)).ConfigureAwait(false);

        return lineCount > 0;
    }

    /// <summary>한 줄을 <b>보낼 수 있는지</b> 판정한다. 사유는 <b>하나로 뭉치지 않는다</b>.</summary>
    /// <remarks>
    /// 🔴 순서가 뜻을 갖는다 — <b>미확정 → 미승인 → 이메일없음</b>. 확정도 안 된 명세서에
    /// "이메일 없음" 이라고 하면 경리가 엉뚱한 것을 고친다.
    /// </remarks>
    private static PayslipSendTargetDto ToTarget(SlipRow r, bool approvalRequired)
    {
        var t = new PayslipSendTargetDto
        {
            SlipId = r.SlipId,
            EmployeeId = r.EmployeeId,
            EmployeeName = r.EmployeeName ?? "-",
            DeptName = r.DeptName,
            RecipientEmail = string.IsNullOrWhiteSpace(r.Email) ? null : r.Email.Trim()
        };

        // ① 확정되지 않았으면 금액이 바뀔 수 있다 — 나가면 직원이 틀린 금액을 받는다.
        if (!IsConfirmedOrPaid(r.Status))
        {
            t.CanSend = false;
            t.BlockReason = PayslipSendBlockReasons.NotConfirmed;
            return t;
        }

        // ② 결재를 쓰는 회사인데 아직 승인 안 됐으면 못 나간다(⑤결재).
        if (approvalRequired && !r.IsApproved)
        {
            t.CanSend = false;
            t.BlockReason = PayslipSendBlockReasons.NotApproved;
            return t;
        }

        // ③ 받을 주소가 없으면 못 보낸다. 🔴 여기서 조용히 건너뛰면 그 직원만 영영 못 받는다.
        if (t.RecipientEmail is null)
        {
            t.CanSend = false;
            t.BlockReason = PayslipSendBlockReasons.NoEmail;
            return t;
        }

        t.CanSend = true;
        return t;
    }

    /// <summary>확정 이후 상태인가. <c>paid</c> 는 확정을 지난 상태이므로 함께 인정한다.</summary>
    private static bool IsConfirmedOrPaid(string? status)
        => string.Equals(status, "confirmed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "paid", StringComparison.OrdinalIgnoreCase);

    // ═══════════════════════════════════════════════════════════════
    //  조회
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 그 달 명세서 + 직원 이메일 + <b>결재 승인 여부</b>를 한 번에 읽는다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 승인 여부를 <c>payroll_slips</c> 에서 읽지 <b>않는다</b> — 거기에는 승인 칸이 없다.
    /// W2 에서 <b>급여표를 건드리지 않기로</b> 했기 때문이다(사장님:
    /// <i>"결재승인은 급여표에 써지는 내용을 건들라는게 아니고"</i>).
    /// ⇒ <c>approval_documents</c> 를 <c>ref_id = slip_id</c> 로 되짚어 판정한다.
    /// </para>
    /// <para>
    /// ⚠️ <c>EXISTS</c> 로 본다 — 같은 명세서로 결재가 여러 번 올라갔을 수 있어
    /// <c>JOIN</c> 하면 <b>행이 늘어난다</b>(한 사람에게 두 번 발송된다).
    /// </para>
    /// </remarks>
    private async Task<List<SlipRow>> LoadRowsAsync(string tenantId, int year, int month,
        List<string>? slipIds, CancellationToken ct)
    {
        const string sql =
            """
            SELECT
              s.slip_id     AS SlipId,
              s.employee_id AS EmployeeId,
              e.emp_name    AS EmployeeName,
              d.dept_name   AS DeptName,
              e.email       AS Email,
              s.pay_year    AS PayYear,
              s.pay_month   AS PayMonth,
              s.status      AS Status,
              EXISTS (
                SELECT 1 FROM approval_documents a
                 WHERE a.tenant_id = s.tenant_id
                   AND a.doc_type  = @DocType
                   AND a.ref_id    = s.slip_id
                   AND a.status    = 'approved'
              ) AS IsApproved
            FROM payroll_slips s
            LEFT JOIN employees   e ON e.employee_id = s.employee_id AND e.tenant_id = s.tenant_id
            LEFT JOIN departments d ON d.dept_id     = e.dept_id     AND d.tenant_id = s.tenant_id
            WHERE s.tenant_id = @TenantId
              AND s.pay_year  = @Year
              AND s.pay_month = @Month
              AND (@FilterIds = 0 OR s.slip_id IN @SlipIds)
            ORDER BY e.emp_name
            """;

        // ⚠️ Dapper 는 빈 목록을 IN () 으로 펼쳐 문법 오류를 낸다. 그래서 목록이 없을 때는
        //    조건을 아예 끄고(@FilterIds=0), 자리만 채울 더미 한 개를 넘긴다.
        var filter = slipIds is { Count: > 0 };

        var rows = await _db.QueryAsync<SlipRow>(new CommandDefinition(sql, new
        {
            TenantId = tenantId,
            Year = year,
            Month = month,
            DocType = PayslipDocType,
            FilterIds = filter ? 1 : 0,
            SlipIds = filter ? slipIds! : new List<string> { "-" }
        }, cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    // ═══════════════════════════════════════════════════════════════
    //  이력
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 보내지 <b>못한</b> 건을 이력에 남긴다.
    /// </summary>
    /// <remarks>
    /// 🔴 안 남기면 <b>"안 보냈다" 는 사실이 어디에도 없다.</b> 나중에 그 직원이
    /// <i>"저는 못 받았는데요"</i> 라고 했을 때 확인할 근거가 사라진다.
    /// 발송이 성공한 건은 <c>EmailService</c> 가 자기 이력을 남기므로 여기서 손대지 않는다.
    /// </remarks>
    private async Task WriteFailedHistoryAsync(string tenantId, string? actorUserId,
        SlipRow row, PayslipSendTargetDto target, string error, CancellationToken ct)
    {
        const string sql =
            """
            INSERT INTO email_send_history
              (email_id, tenant_id, sent_at, sent_by_user, document_type, document_no, document_id,
               recipient_email, subject, body_text, has_attachment, status, error_message)
            VALUES
              (@Id, @TenantId, @Now, @UserId, @DocType, @DocNo, @DocId,
               @Recipient, @Subject, @Body, 0, 'failed', @Err)
            """;

        try
        {
            await _db.ExecuteAsync(new CommandDefinition(sql, new
            {
                Id = Guid.NewGuid().ToString(),
                TenantId = tenantId,
                Now = DateTime.Now,
                UserId = actorUserId,
                DocType = PayslipDocType,
                DocNo = ShortRef(row.SlipId),
                DocId = row.SlipId,
                Recipient = target.RecipientEmail ?? "-",
                Subject = $"{row.PayYear}년 {row.PayMonth}월 급여명세서",
                Body = "발송하지 못했습니다.",
                Err = error
            }, cancellationToken: ct)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 이력 기록 실패가 발송 결과 보고를 막으면 안 된다 — 경리는 결과를 봐야 한다.
            _logger.LogWarning(ex, "[Payslip] 실패이력 기록 실패 slip={Slip}", row.SlipId);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  헬퍼
    // ═══════════════════════════════════════════════════════════════

    /// <summary>문서번호 자리에 쓸 짧은 표기. ⚠️ 길이를 가정하지 않는다(W2 에서 겪은 자리).</summary>
    private static string ShortRef(string id)
        => string.IsNullOrEmpty(id) ? "-" : (id.Length <= 8 ? id : id[..8]);

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open) return;
        if (_db is DbConnection c) { await c.OpenAsync(ct).ConfigureAwait(false); return; }
        _db.Open();
    }

    /// <summary>조회 한 줄. Dapper 가 채운다.</summary>
    private sealed class SlipRow
    {
        public string SlipId { get; set; } = string.Empty;
        public string EmployeeId { get; set; } = string.Empty;
        public string? EmployeeName { get; set; }
        public string? DeptName { get; set; }
        public string? Email { get; set; }
        public int PayYear { get; set; }
        public int PayMonth { get; set; }
        public string? Status { get; set; }
        public bool IsApproved { get; set; }
    }
}
