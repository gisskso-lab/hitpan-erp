using System.Data;
using Dapper;

namespace HitPan.API.Services;

/// <summary>
/// CS 자동 발신 서비스 PoC.
/// watchdog_emergencies cs_notified_at IS NULL 5분 주기 스캔 → Twilio Korea / Toast SMS 호출.
/// 헌법 #28 정합: cool down 5회 초과 = 즉시 본사 알림.
///
/// <para>🔴🔴 <b>20261014작10 측정 메모 (사장님 지시 「코드주석·md 에 메모 해둘것」 · 결-F)</b>
/// — <b>이 서비스는 신규 설치 고객 PC 에서 아무 일도 하지 못한다.</b></para>
///
/// <para><b>측정</b>(2026-10-14 · 실물 <c>MigrationRunner</c> 로 출하본 위에서 전 마이그 재적용):
/// <list type="bullet">
///   <item>이 서비스가 읽는 <c>watchdog_emergencies</c>·<c>watchdog_pings</c> 는
///     <b><c>DB-70</c> 만 만든다</b>. 🔴 출하 DDL(<c>installer/hitpan_db_clean.sql</c>)에는
///     <b>그 이름이 0건</b>이다(글자 검색 0 · 표 생성 0).</item>
///   <item>그런데 출하 DDL 은 <c>schema_migrations</c> 에 <c>DB-70</c> 을 <b>「적용됨」으로 시드</b>한다
///     ⇒ 적용기가 <b>건너뛴다</b> ⇒ 🔴 <b>그 표가 영영 안 생긴다.</b></item>
///   <item><b>증상</b>: 이 루프가 <c>pollSec</c>(기본 5분) 주기로 <c>FROM watchdog_emergencies</c> 를 쳐서
///     <b>1146 Table doesn't exist</b> 로 떨어지고 <c>[CsAutoDispatch] loop failure</c> 를 남긴다.
///     기동은 안 죽는다(#15 로 로그만) ⇒ <b>조용히 아무 일도 안 하는 기능 + 5분마다 오류 로그</b>다.</item>
/// </list></para>
///
/// <para>🔴 <b>고치지 마라 — 결재 거리다.</b> 출하 DDL 수정은 <b>신규 설치 단일 진실원</b>(#36)이라
/// 사장님 결재가 필요하고, #37(「안 읽힌다 ≠ 잔재」)이 있어 이 표를 잔재로 판정할 수도 없다.
/// <br/>⚠️ 그리고 이 서비스는 <b>SMS 발송</b>을 한다 — CS 발송은 백오피스 몫이라는 결정이 있어
/// (<c>project_cs_backoffice_own_ai_adapter_20261008</c>), <b>이 자리가 맞는지부터</b> 물어야 한다.
/// 「표를 만들면 된다」로 달려들면 고객 PC 에 본사 기능을 켜는 일이 될 수 있다(#18·#22).</para>
///
/// <para>근거: docs/검증/선행/20261014_1V_작10_마이그산물_출하본어긋남_전수측정_선행검증서.md</para>
/// </summary>
public sealed class CsAutoDispatchService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<CsAutoDispatchService> _logger;
    private readonly IConfiguration _config;

    public CsAutoDispatchService(
        IServiceProvider sp,
        ILogger<CsAutoDispatchService> logger,
        IConfiguration config)
    {
        _sp = sp;
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _config.GetValue("CsAutoDispatch:Enabled", false);
        if (!enabled)
        {
            _logger.LogInformation("[CsAutoDispatch] disabled — skip");
            return;
        }

        var pollSec = _config.GetValue("CsAutoDispatch:PollSeconds", 300);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CsAutoDispatch] loop failure");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(pollSec), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ProcessPendingAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        if (db.State != ConnectionState.Open)
        {
            if (db is System.Data.Common.DbConnection c) await c.OpenAsync(ct);
            else db.Open();
        }

        const string sql = @"
            SELECT id, tenant_id_hash, received_at, reason, stage
            FROM watchdog_emergencies
            WHERE cs_notified_at IS NULL
              AND received_at >= UTC_TIMESTAMP() - INTERVAL 1 HOUR
            ORDER BY received_at ASC
            LIMIT 20;";

        var rows = (await db.QueryAsync<PendingEmergency>(sql)).ToList();
        if (rows.Count == 0) return;

        _logger.LogWarning("[CsAutoDispatch] processing {Count} pending emergencies", rows.Count);

        foreach (var row in rows)
        {
            var ok = await SendNotificationAsync(row, ct);
            if (ok)
            {
                await db.ExecuteAsync(
                    "UPDATE watchdog_emergencies SET cs_notified_at = UTC_TIMESTAMP(3) WHERE id = @Id",
                    new { row.Id });
            }
        }
    }

    private async Task<bool> SendNotificationAsync(PendingEmergency row, CancellationToken ct)
    {
        var channel = _config.GetValue<string>("CsAutoDispatch:Channel") ?? "log";
        try
        {
            switch (channel.ToLowerInvariant())
            {
                case "twilio":
                    return await SendTwilioAsync(row, ct);
                case "toast":
                    return await SendToastAsync(row, ct);
                default:
                    _logger.LogError("[CsAutoDispatch] EMERGENCY tenant={Hash} reason={Reason} stage={Stage} — channel=log only",
                        row.TenantIdHash, row.Reason, row.Stage);
                    return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CsAutoDispatch] send failure id={Id}", row.Id);
            return false;
        }
    }

    private Task<bool> SendTwilioAsync(PendingEmergency row, CancellationToken ct)
    {
        // Twilio Korea REST API 통합 (PoC). 정식 적용은 사장님 계약 결재 후.
        _logger.LogWarning("[CsAutoDispatch] Twilio stub — would dial CS for tenant={Hash}", row.TenantIdHash);
        return Task.FromResult(true);
    }

    private Task<bool> SendToastAsync(PendingEmergency row, CancellationToken ct)
    {
        // NHN Toast SMS REST API 통합 (PoC).
        _logger.LogWarning("[CsAutoDispatch] Toast SMS stub — would notify CS for tenant={Hash}", row.TenantIdHash);
        return Task.FromResult(true);
    }

    private sealed class PendingEmergency
    {
        public long Id { get; set; }
        public string TenantIdHash { get; set; } = "";
        public DateTime ReceivedAt { get; set; }
        public string Reason { get; set; } = "";
        public string? Stage { get; set; }
    }
}
