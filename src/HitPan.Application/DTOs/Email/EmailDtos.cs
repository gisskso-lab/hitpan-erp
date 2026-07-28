namespace HitPan.Application.DTOs.Email;

// ═══ SMTP 설정 ═══════════════════════════════════════════
public sealed class EmailSettingsDto
{
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string SmtpUser { get; set; } = "";
    public bool HasPassword { get; set; }                      // 패스워드 등록 여부만 노출 (실값 미노출)
    public bool UseSsl { get; set; } = true;
    public string FromAddress { get; set; } = "";
    public string? FromName { get; set; }
    public bool BccSelf { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastTestAt { get; set; }
    public string? LastTestResult { get; set; }
    public string? LastTestError { get; set; }
}

public sealed class UpdateEmailSettingsRequest
{
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string SmtpUser { get; set; } = "";
    public string? SmtpPassword { get; set; }                  // null이면 기존 비번 유지
    public bool UseSsl { get; set; } = true;
    public string FromAddress { get; set; } = "";
    public string? FromName { get; set; }
    public bool BccSelf { get; set; }
}

public sealed class TestSmtpResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}

// ═══ 발송 ═══════════════════════════════════════════════
public sealed class SendDocumentEmailRequest
{
    public string DocumentType { get; set; } = "";              // quotation/sales_order/delivery/tax_invoice/purchase_order/purchase_receipt
    public string DocumentId { get; set; } = "";
    public string DocumentNo { get; set; } = "";
    public string? PartnerId { get; set; }
    public string RecipientEmail { get; set; } = "";
    public string? CcEmail { get; set; }
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public bool AttachPdf { get; set; } = true;
}

public sealed class SendEmailResponse
{
    public bool Success { get; set; }
    public string EmailId { get; set; } = "";
    public string? Error { get; set; }
}

// ═══ 발송 이력 ══════════════════════════════════════════
public sealed class EmailHistoryDto
{
    public string EmailId { get; set; } = "";
    public DateTime SentAt { get; set; }
    public string DocumentType { get; set; } = "";
    public string DocumentNo { get; set; } = "";
    public string? PartnerId { get; set; }
    public string RecipientEmail { get; set; } = "";
    public string Subject { get; set; } = "";
    public bool HasAttachment { get; set; }
    public string Status { get; set; } = "";
    public string? ErrorMessage { get; set; }
}
