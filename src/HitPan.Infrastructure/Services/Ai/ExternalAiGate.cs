using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services.Ai;

/// <summary>
/// <see cref="IExternalAiGate"/> 구현 — <c>ai_export_consents</c>(DB-138) 존재 검사 하나로 판정한다.
/// 표 부재·조회 예외·기록 0건 = 전부 닫힘(fail-closed · 20261006작9 §4-2).
/// </summary>
public sealed class ExternalAiGate : IExternalAiGate
{
    private readonly IDbConnection _db;
    private readonly ILogger<ExternalAiGate> _logger;

    public ExternalAiGate(IDbConnection db, ILogger<ExternalAiGate> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> IsOpenAsync(string tenantId, CancellationToken ct = default)
    {
        // 테넌트 식별이 비면 열 수 없다 — 닫힘.
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return false;
        }

        try
        {
            if (_db.State != ConnectionState.Open)
            {
                if (_db is DbConnection dbConnection)
                {
                    await dbConnection.OpenAsync(ct).ConfigureAwait(false);
                }
                else
                {
                    _db.Open();
                }
            }

            var count = await _db.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM ai_export_consents WHERE tenant_id = @TenantId",
                new { TenantId = tenantId },
                cancellationToken: ct)).ConfigureAwait(false);

            return count > 0;
        }
        catch (Exception ex)
        {
            // 헌법 #15: 빈 catch 금지. 판정 실패는 조용히 여는 쪽이 아니라 **닫는** 쪽이다(fail-closed).
            _logger.LogWarning(ex,
                "외부 반출 게이트 판정 실패 — 닫힘(fail-closed)으로 처리. tenant={Tenant}", tenantId);
            return false;
        }
    }
}
