using HitPan.Application.DTOs.Email;

namespace HitPan.Application.Interfaces;

/// <summary>이메일 발송 서비스 (사장님 결재 2026-04-29 — 6종 문서 자동발송).</summary>
public interface IEmailService
{
    Task<EmailSettingsDto> GetSettingsAsync(string tenantId, CancellationToken ct = default);
    Task UpdateSettingsAsync(string tenantId, UpdateEmailSettingsRequest req, CancellationToken ct = default);
    Task<TestSmtpResponse> TestSmtpAsync(string tenantId, CancellationToken ct = default);
    Task<SendEmailResponse> SendDocumentAsync(string tenantId, string? userId, SendDocumentEmailRequest req, CancellationToken ct = default);
    Task<List<EmailHistoryDto>> GetHistoryAsync(string tenantId, string? documentType, int limit = 100, CancellationToken ct = default);
}

/// <summary>문서 PDF 렌더링 서비스.</summary>
public interface IPdfRenderService
{
    /// <summary>문서 종류와 ID로 PDF byte[] 생성. 템플릿 미지원 타입은 빈 안내문서.</summary>
    Task<(byte[] Bytes, string FileName)> RenderDocumentAsync(string tenantId, string documentType, string documentId, CancellationToken ct = default);
}
