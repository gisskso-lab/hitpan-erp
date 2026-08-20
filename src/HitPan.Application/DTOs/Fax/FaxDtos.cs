namespace HitPan.Application.DTOs.Fax;

// ═══ 팩스 설정 ═══════════════════════════════════════════
// 이메일(EmailDtos)과 동일 골격. 검증된 패턴을 그대로 미러링한다.

public sealed class FaxSettingsDto
{
    /// <summary>공급자 코드. 'mock' = 미설정 (실제 송출 안 됨).</summary>
    public string Provider { get; set; } = "mock";
    public string? ApiEndpoint { get; set; }
    /// <summary>키 등록 여부만 노출. 실값은 절대 내려보내지 않는다 (§#5).</summary>
    public bool HasApiKey { get; set; }
    public bool HasApiSecret { get; set; }
    public string? SenderFaxNo { get; set; }
    public string? SenderName { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastTestAt { get; set; }
    public string? LastTestResult { get; set; }
    public string? LastTestError { get; set; }

    /// <summary>
    /// 실제 팩스 송출이 가능한 상태인가.
    /// false 면 화면은 "실제 전송되지 않습니다" 경고를 반드시 노출해야 한다 (§#23 거짓봉합 방지).
    /// </summary>
    public bool CanSendReal { get; set; }
}

public sealed class UpdateFaxSettingsRequest
{
    public string Provider { get; set; } = "mock";
    public string? ApiEndpoint { get; set; }
    /// <summary>null = 기존 키 유지 / 값 = 교체.</summary>
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }
    public string? SenderFaxNo { get; set; }
    public string? SenderName { get; set; }
    public bool IsActive { get; set; }
}

public sealed class TestFaxResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    /// <summary>Mock 공급자로 시험한 경우 true — "실제 연결을 확인한 것이 아니다".</summary>
    public bool IsMock { get; set; }
}

// ═══ 발송 ═══════════════════════════════════════════════
public sealed class SendFaxRequest
{
    public string DocumentType { get; set; } = "";
    public string DocumentId { get; set; } = "";
    public string DocumentNo { get; set; } = "";
    public string? PartnerId { get; set; }
    public string RecipientFaxNo { get; set; } = "";
    public string? RecipientName { get; set; }
}

public sealed class SendFaxResponse
{
    public bool Success { get; set; }
    public string FaxId { get; set; } = "";
    public string? ProviderJobId { get; set; }
    public string? Error { get; set; }

    /// <summary>
    /// true = 공급자 미설정으로 **실제 전송되지 않았다**.
    /// 화면은 이 값이 true 면 성공처럼 보이는 표시를 해서는 안 된다 (§#23).
    /// </summary>
    public bool IsMock { get; set; }

    /// <summary>사용자에게 그대로 보여줄 안내 문구 (개발용어 금지).</summary>
    public string? Notice { get; set; }
}

// ═══ 발송 이력 ══════════════════════════════════════════
public sealed class FaxHistoryDto
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

// ═══ 공급자 계약 ════════════════════════════════════════

/// <summary>공급자에게 넘기는 송출 요청.</summary>
public sealed class FaxProviderRequest
{
    public string TenantId { get; set; } = "";
    public string RecipientFaxNo { get; set; } = "";
    public string? SenderFaxNo { get; set; }
    public string? SenderName { get; set; }
    public byte[] DocumentBytes { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = "";
    public string? ApiEndpoint { get; set; }
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }
}

/// <summary>공급자 송출 결과.</summary>
public sealed class FaxProviderResult
{
    public bool Success { get; set; }
    public string? JobId { get; set; }
    public string? Error { get; set; }
    public string? RawResponse { get; set; }
    public int? PageCount { get; set; }

    /// <summary>실제 송출이 아니었음 (Mock). 절대 성공으로 위장하지 않는다.</summary>
    public bool IsMock { get; set; }
}
