using HitPan.Application.DTOs.Fax;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// 팩스 공급자 미설정 상태의 기본 구현.
///
/// 🔴 이 구현은 **아무것도 전송하지 않는다.** 그리고 전송한 척도 하지 않는다.
///
/// 왜 이렇게 만드는가 (§#23 · 검증팀 SoD):
///   팩스는 외부 유료 서비스 계약이 있어야 실제로 나간다. 벤더 결재 전에
///   "성공" 을 돌려주면 화면은 초록불이 되고, 사장님과 고객은 팩스가 갔다고 믿는다.
///   거래처는 못 받았는데 우리 화면만 성공인 상태 — 이것이 가장 위험한 거짓봉합이다.
///   그래서 이 구현은 Success=false 를 돌려주고, IsMock=true 로 이유를 분명히 밝힌다.
///
///   ⚠️ 후임자 주의: 이 클래스가 Success=true 를 돌려주도록 바꾸는 것은
///      기능 개선이 아니라 **안전장치 제거**다. 실송출은 반드시 실제 벤더 구현체로 한다.
/// </summary>
public sealed class MockFaxProvider : IFaxProvider
{
    private readonly ILogger<MockFaxProvider> _logger;

    public MockFaxProvider(ILogger<MockFaxProvider> logger) => _logger = logger;

    public string ProviderCode => "mock";

    /// <summary>실제 송출 불가 — 화면 경고 노출의 근거.</summary>
    public bool CanSendReal => false;

    private const string Notice =
        "팩스 공급자가 설정되지 않아 실제로 전송되지 않았습니다. 설정 › 팩스 설정에서 공급자를 먼저 등록하세요.";

    public Task<FaxProviderResult> SendAsync(FaxProviderRequest req, CancellationToken ct = default)
    {
        // 시도 자체는 이력에 남긴다 — 사용자가 눌렀다는 사실은 기록되어야 한다 (§#3).
        _logger.LogWarning(
            "팩스 송출 시도 — 공급자 미설정으로 전송되지 않음. TenantId={TenantId} 수신={Fax} 파일={File}",
            req.TenantId, req.RecipientFaxNo, req.FileName);

        return Task.FromResult(new FaxProviderResult
        {
            Success = false,          // ← 위장 금지. 실제로 안 갔으므로 false.
            IsMock = true,
            Error = Notice,
            RawResponse = "mock-provider: no transmission performed"
        });
    }

    public Task<FaxProviderResult> TestAsync(FaxProviderRequest req, CancellationToken ct = default)
    {
        _logger.LogInformation("팩스 연결 시험 — 공급자 미설정 (Mock). TenantId={TenantId}", req.TenantId);

        return Task.FromResult(new FaxProviderResult
        {
            Success = false,
            IsMock = true,
            Error = "팩스 공급자가 설정되지 않았습니다. 연결을 확인할 대상이 없습니다.",
            RawResponse = "mock-provider: nothing to test"
        });
    }
}
