namespace HitPan.Web.Models;

// 20260821작1 W3 — 팩스. 서버 DTO(FaxDtos)와 짝을 이룬다.

public sealed class FaxSettingsModel
{
    public string Provider { get; set; } = "mock";
    public string? ApiEndpoint { get; set; }
    public bool HasApiKey { get; set; }
    public bool HasApiSecret { get; set; }
    public string? SenderFaxNo { get; set; }
    public string? SenderName { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastTestAt { get; set; }
    public string? LastTestResult { get; set; }
    public string? LastTestError { get; set; }

    /// <summary>실제 팩스 송출이 가능한 상태인가. false 면 화면은 경고를 노출해야 한다.</summary>
    public bool CanSendReal { get; set; }
}

public sealed class UpdateFaxSettingsModel
{
    public string Provider { get; set; } = "mock";
    public string? ApiEndpoint { get; set; }
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }
    public string? SenderFaxNo { get; set; }
    public string? SenderName { get; set; }
    public bool IsActive { get; set; }
}

public sealed class TestFaxResultModel
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public bool IsMock { get; set; }
}

public sealed class SendFaxModel
{
    public string DocumentType { get; set; } = "";
    public string DocumentId { get; set; } = "";
    public string DocumentNo { get; set; } = "";
    public string? PartnerId { get; set; }
    public string RecipientFaxNo { get; set; } = "";
    public string? RecipientName { get; set; }
}

public sealed class SendFaxResultModel
{
    public bool Success { get; set; }
    public string FaxId { get; set; } = "";
    public string? ProviderJobId { get; set; }
    public string? Error { get; set; }

    /// <summary>true = 공급자 미설정으로 실제 전송되지 않음. 성공처럼 표시하면 안 된다.</summary>
    public bool IsMock { get; set; }
    public string? Notice { get; set; }
}

public sealed class FaxHistoryModel
{
    public string FaxId { get; set; } = "";
    public DateTime SentAt { get; set; }
    public string DocumentType { get; set; } = "";
    public string DocumentNo { get; set; } = "";
    public string? PartnerId { get; set; }
    public string RecipientFaxNo { get; set; } = "";
    public string? RecipientName { get; set; }
    public int? PageCount { get; set; }
    public string Provider { get; set; } = "";
    public string Status { get; set; } = "";
    public string? ErrorMessage { get; set; }
}
