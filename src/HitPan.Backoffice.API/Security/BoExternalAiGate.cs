using Dapper;
using MySqlConnector;

namespace HitPan.Backoffice.API.Security;

/// <summary>
/// 🔴 백오피스 외부 AI 반출 잠금장치 — <b>fail-closed</b> (작14 C-5 · 사장님 결재 2026-10-08).
///
/// <para>판정은 <c>bo_ai_export_consents</c> 에 그 테넌트의 동의 행이 <b>있는가</b> 하나뿐이다.
/// 표 부재·조회 예외·0건 = 전부 <b>닫힘</b>. 지금은 동의 절차 자체가 없으므로 사실상 전면 차단이고,
/// 그것이 결재된 상태다 — 2차 열쇠는 법무 4건 + 결-7 이다
/// (그 전에 게이트 해제·동의 행 임의 INSERT·동의 수집 화면 선개발 전부 금지).</para>
///
/// <para>ERP <c>ExternalAiGate</c>(작9 · DB-138)와 같은 뜻이지만 <b>독립 복제</b>다 —
/// 백오피스는 ERP 프로젝트를 참조하지 않는다(경계 분리가 설계값 · 메모리 기록).</para>
///
/// <para>⚠️ 묶음 D(AI 3사 어댑터)가 생기면 <b>이 게이트를 통과한 뒤에만</b> 외부로 나간다.
/// 호출자가 늘어나도 판정은 이 한 곳이어야 한다(문이 여러 개면 하나는 반드시 열린 채 남는다).</para>
/// </summary>
public interface IBoExternalAiGate
{
    // 🔴 사장님 지시 2026-10-08 — 원문 그대로 보존(지우지 말 것)
    //누리집을 만들고 AI를 통한 CS자동화 즉시 실행하지 않고, 준비중인 이유는 추후 쪽지 형태에서 챗봇 형태로 백오피스가 히트판에 1:1로 CS를 자동화 처리 할 수 있도록 하는 준비단계이다. //
    //
    // ⇒ 그래서 이 게이트는 「기능을 안 만든 것」이 아니라 **열쇠를 아직 안 돌린 것**이다.
    //   지금 쌓는 CS 기록·분류·해결 문서가 그때 챗봇이 읽을 재료가 된다.

    /// <summary>이 테넌트의 외부 AI 반출이 열려 있는가. 판정 실패는 던지지 않고 닫힘을 돌려준다.</summary>
    Task<bool> IsOpenAsync(string tenantId, CancellationToken ct = default);
}

/// <inheritdoc cref="IBoExternalAiGate"/>
public sealed class BoExternalAiGate : IBoExternalAiGate
{
    private readonly IConfiguration _config;
    private readonly ILogger<BoExternalAiGate> _logger;

    public BoExternalAiGate(IConfiguration config, ILogger<BoExternalAiGate> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<bool> IsOpenAsync(string tenantId, CancellationToken ct = default)
    {
        // 테넌트 식별이 비면 열 근거가 없다 — 닫힘.
        if (string.IsNullOrWhiteSpace(tenantId)) return false;

        try
        {
            var cs = _config.GetConnectionString("BackofficeDb")
                     ?? _config.GetConnectionString("Default")
                     ?? throw new InvalidOperationException("ConnectionStrings:BackofficeDb 미설정");
            await using var db = new MySqlConnection(cs);
            await db.OpenAsync(ct);

            var count = await db.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM bo_ai_export_consents WHERE tenant_id = @TenantId",
                new { TenantId = tenantId },
                cancellationToken: ct));

            return count > 0;
        }
        catch (Exception ex)
        {
            // #15 빈 catch 금지 · 판정 실패는 **닫는** 쪽이다(조용히 열리면 그게 사고다).
            _logger.LogWarning(ex, "[AI잠금] 판정 실패 — 닫힘(fail-closed) 처리. tenant={Tenant}", tenantId);
            return false;
        }
    }
}
