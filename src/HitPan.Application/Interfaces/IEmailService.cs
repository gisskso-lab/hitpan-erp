using HitPan.Application.DTOs.Email;
using HitPan.Application.DTOs.Documents;

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

    /// <summary>
    /// 미리보기 — <b>저장 전 양식값·예시 자료</b>를 주입해 <b>같은 통로</b>로 그린다.
    /// 근거: 20261021작 §11-3·§19-2 ㉯ · 사장님 오더 2026-10-24.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>조건 1</b> — <paramref name="overrides"/> 가 null 이면 위 4인자와 <b>똑같이</b> 동작한다.
    ///   기본 구현이 그것을 보장한다(기존 구현체·시험 Stub 는 한 글자도 안 고쳤다 · #1).
    /// 🔴 실제 주입은 <c>PdfRenderService</c> 가 이 메서드를 재정의해서 한다.
    ///   그 재정의를 빼면 미리보기가 <b>저장값으로 떨어져 음성 대조군이 실패</b>한다(조건 3).
    /// </remarks>
    Task<(byte[] Bytes, string FileName)> RenderDocumentAsync(string tenantId, string documentType,
        string documentId, PdfPreviewOverrides? overrides, CancellationToken ct = default)
        => RenderDocumentAsync(tenantId, documentType, documentId, ct);
}
