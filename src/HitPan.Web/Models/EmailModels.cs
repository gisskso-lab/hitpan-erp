namespace HitPan.Web.Models;

public sealed class EmailSettingsModel
{
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string SmtpUser { get; set; } = "";
    public bool HasPassword { get; set; }
    public bool UseSsl { get; set; } = true;
    public string FromAddress { get; set; } = "";
    public string? FromName { get; set; }
    public bool BccSelf { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastTestAt { get; set; }
    public string? LastTestResult { get; set; }
    public string? LastTestError { get; set; }
}

public sealed class UpdateEmailSettingsModel
{
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string SmtpUser { get; set; } = "";
    public string? SmtpPassword { get; set; }
    public bool UseSsl { get; set; } = true;
    public string FromAddress { get; set; } = "";
    public string? FromName { get; set; }
    public bool BccSelf { get; set; }
}

public sealed class TestSmtpResultModel
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}

public sealed class SendDocumentEmailModel
{
    public string DocumentType { get; set; } = "";
    public string DocumentId { get; set; } = "";
    public string DocumentNo { get; set; } = "";
    public string? PartnerId { get; set; }
    public string RecipientEmail { get; set; } = "";
    public string? CcEmail { get; set; }
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public bool AttachPdf { get; set; } = true;
}

public sealed class SendEmailResultModel
{
    public bool Success { get; set; }
    public string EmailId { get; set; } = "";
    public string? Error { get; set; }
}

public sealed class EmailHistoryRowModel
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
