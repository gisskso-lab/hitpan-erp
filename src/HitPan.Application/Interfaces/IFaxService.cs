using HitPan.Application.DTOs.Fax;

namespace HitPan.Application.Interfaces;

/// <summary>
/// 팩스 발송 서비스 (사장님 오더 2026-08-21 — "업체팩스번호: 실제 팩스전송").
/// 이메일(IEmailService) 과 동일 골격. 문서 PDF 는 IPdfRenderService 를 재사용한다.
/// §#3 발송이력 INSERT ONLY / §#5 API키 AES암호화 / §#18 고객사 본인 계정만 (본사 대리송출 금지).
/// </summary>
public interface IFaxService
{
    Task<FaxSettingsDto> GetSettingsAsync(string tenantId, CancellationToken ct = default);
    Task UpdateSettingsAsync(string tenantId, UpdateFaxSettingsRequest req, CancellationToken ct = default);
    Task<TestFaxResponse> TestConnectionAsync(string tenantId, CancellationToken ct = default);
    Task<SendFaxResponse> SendDocumentAsync(string tenantId, string? userId, SendFaxRequest req, CancellationToken ct = default);
    Task<List<FaxHistoryDto>> GetHistoryAsync(string tenantId, string? documentType, int limit = 100, CancellationToken ct = default);
}

/// <summary>
/// 팩스 공급자 추상화 — 벤더 교체점.
///
/// 🔴 실제 팩스 송출은 외부 유료 서비스 계약이 필요하다. 사장님 벤더 결재 전까지는
///    MockFaxProvider 가 기본으로 동작하며, **절대 전송 성공을 위장하지 않는다** (§#23).
///    벤더 결재 후 이 인터페이스 구현체 1개 추가 + DI 한 줄 교체로 실송출로 전환된다.
///    FaxService 본체는 수정하지 않는다.
/// </summary>
public interface IFaxProvider
{
    /// <summary>공급자 코드. fax_settings.provider 와 대조해 선택된다.</summary>
    string ProviderCode { get; }

    /// <summary>실제 송출 가능 여부. Mock 은 false — 화면 경고 노출의 근거값.</summary>
    bool CanSendReal { get; }

    Task<FaxProviderResult> SendAsync(FaxProviderRequest req, CancellationToken ct = default);

    /// <summary>연결 시험. 자격증명이 유효한지만 확인하고 실제 송출은 하지 않는다.</summary>
    Task<FaxProviderResult> TestAsync(FaxProviderRequest req, CancellationToken ct = default);
}
