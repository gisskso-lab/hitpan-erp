// ============================================================================
// WS-20260601-17 ResellerRlsService — 대리점 RLS 셀프 서비스 (8명제 #6·#7 정합)
// ----------------------------------------------------------------------------
// 작성일       : 2026-06-01
// 작성팀       : 백엔드 매니저(Harvard·Oracle 30년) + 백오피스 개발자
// 정합 산출물  : src/HitPan.API/Repositories/BackofficeRepositoryBase.cs (WS-16)
//                src/HitPan.API/Services/Auth/JwtClaimsBuilder.cs (WS-16)
//                docs/design/EIGHT_PROPOSITIONS_BACKOFFICE_LANDING.md §5·§6·§7
//
// 사장님 8명제 정합
//   #6 대리점 RLS               : 자기 영업 고객사·CS·실적만 (다른 대리점 0)
//   #7 본사 전권한 + JIT 복호화 : 평문 사업자번호·상호·대표자 노출 금지 (마스킹·시리얼만)
//
// 절대 원칙
//   - reseller_serial 은 오직 JWT 클레임(BackofficeAuthContext)에서만 추출
//     (헌법 #2 확장 + WS-16 ApplyRls 강제)
//   - 컨트롤러/서비스가 reseller_id·tenant_id 파라미터로 받으면 즉시 ArgumentException
//   - 응답 DTO 에 평문 사업자번호·상호·대표자·주소·이메일·휴대폰 박제 금지 (8명제 #3·#6)
//   - 다른 대리점 데이터 접근 시도 = UnauthorizedAccessException → 컨트롤러에서 403 + 감사로그
//   - 빈 catch 금지 (헌법 #15)
//   - MySqlConnection thread-safe — 단일 흐름 직렬 호출, Task.WhenAll 미사용 (헌법 #16)
//
// DDL 가도
//   - hitpan_backoffice.cs_tickets / reseller_audit_log / reseller_commissions 는
//     WS-20260601-15 후속 DDL(사장님 결재 후, 헌법 #29). 미실행 환경에서는 빈 목록 안전 반환.
//   - 헌법 #13 (DESCRIBE 의무): 실 DB 결합 시 컬럼·타입 재검증 후 SELECT 확장.
// ============================================================================

using System.Data.Common;
using Dapper;
using HitPan.API.Repositories;
using HitPan.API.Services.Auth;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Http;

namespace HitPan.API.Services;

public interface IResellerRlsService
{
    Task<IReadOnlyList<ResellerTenantRow>> GetTenantsByResellerAsync(
        string? search, string? paymentStatus, int page, int size, CancellationToken ct = default);

    Task<IReadOnlyList<ResellerCsTicketRow>> GetTicketsByResellerAsync(
        string? status, int page, int size, CancellationToken ct = default);

    Task<long> CreateTicketByResellerAsync(
        ResellerCsTicketCreateRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<ResellerCommissionRow>> GetCommissionsByResellerAsync(
        string? period, int page, int size, CancellationToken ct = default);

    Task<ResellerSalesStatsResponse> GetSalesStatsAsync(CancellationToken ct = default);
}

// ── DTO ─────────────────────────────────────────────────────────────────────

public sealed class ResellerTenantRow
{
    /// <summary>시리얼 (HP-YYMM-XXXXXXXX-CRC). 평문 사업자정보 절대 박제 금지.</summary>
    public string Serial { get; init; } = string.Empty;
    public string PaymentStatus { get; init; } = string.Empty;
    public string? SubscriptionTier { get; init; }
    public string ActivationStatus { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
}

public sealed class ResellerCsTicketRow
{
    public long TicketId { get; init; }
    public string TicketNo { get; init; } = string.Empty;
    public string TargetSerial { get; init; } = string.Empty;
    public string Priority { get; init; } = "p2";
    public string Subject { get; init; } = string.Empty;
    public string Status { get; init; } = "open";
    public DateTime OpenedAt { get; init; }
}

public sealed class ResellerCsTicketCreateRequest
{
    public string TargetSerial { get; set; } = string.Empty;
    public string Priority { get; set; } = "p2";
    public string Subject { get; set; } = string.Empty;
    public string? Body { get; set; }
}

public sealed class ResellerCommissionRow
{
    public string Period { get; init; } = string.Empty; // yyyy-MM
    public decimal Rate { get; init; }
    public decimal Amount { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime? SettledAt { get; init; }
}

public sealed class ResellerSalesStatsResponse
{
    public int TenantCount { get; init; }
    public int ActiveCount { get; init; }
    public int ExpiringSoonCount { get; init; }
    public int AnomalyCount { get; init; }
    public decimal MonthlyCommissionEstimate { get; init; }
    public IReadOnlyList<ResellerExpiringTenantMeta> ExpiringTenants { get; init; }
        = Array.Empty<ResellerExpiringTenantMeta>();
}

public sealed class ResellerExpiringTenantMeta
{
    /// <summary>시리얼만 노출 (평문 0).</summary>
    public string Serial { get; init; } = string.Empty;
    public DateTime? ExpiresAt { get; init; }
    public int DaysLeft { get; init; }
    public string? SubscriptionTier { get; init; }
}

// ── 서비스 본체 ─────────────────────────────────────────────────────────────

public sealed class ResellerRlsService : BackofficeRepositoryBase, IResellerRlsService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ResellerRlsService> _logger;

    public ResellerRlsService(
        IHttpContextAccessor httpContextAccessor,
        IUnitOfWork unitOfWork,
        ILogger<ResellerRlsService> logger)
        : base(httpContextAccessor)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<ResellerTenantRow>> GetTenantsByResellerAsync(
        string? search, string? paymentStatus, int page, int size, CancellationToken ct = default)
    {
        // 헌법 #2 확장: 호출자가 reseller_serial/tenant_id 를 파라미터로 받지 못하도록
        // 시그니처 자체에서 차단됨. (ApplyRls 가 JWT 에서만 추출)
        var p = new DynamicParameters();
        var ctx = ApplyRls(p, out var rlsWhere);

        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 200);
        var offset = (page - 1) * size;

        var extraWhere = string.Empty;
        if (!string.IsNullOrWhiteSpace(paymentStatus))
        {
            extraWhere += " AND payment_status = @PaymentStatus ";
            p.Add("PaymentStatus", paymentStatus);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            // 평문 식별자 검색 금지 — 시리얼 prefix 검색만 허용 (8명제 #3·#6)
            extraWhere += " AND serial LIKE @SerialLike ";
            p.Add("SerialLike", search + "%");
        }
        p.Add("Limit", size);
        p.Add("Offset", offset);

        // ApplyRls 반환 WHERE 절(본사=" WHERE 1=1 " / 대리점=" WHERE reseller_serial=@__rls_serial ")
        var sql = $@"
SELECT serial          AS Serial,
       payment_status  AS PaymentStatus,
       subscription_tier AS SubscriptionTier,
       activation_status AS ActivationStatus,
       created_at      AS CreatedAt,
       expires_at      AS ExpiresAt
FROM tenants
{rlsWhere}
{extraWhere}
ORDER BY created_at DESC
LIMIT @Limit OFFSET @Offset;";

        try
        {
            var db = _unitOfWork.GetDbConnection();
            var rows = await db.QueryAsync<ResellerTenantRow>(new CommandDefinition(sql, p, cancellationToken: ct));
            return rows.AsList();
        }
        catch (DbException ex)
        {
            // 헌법 #15 (빈 catch 금지) + 헌법 #13 후속 DDL 미반영 환경 안전망.
            // 신규 backoffice DB / 컬럼 미존재 시 빈 목록 반환하되 경고 로그 박제.
            _logger.LogWarning(ex,
                "[ResellerRls] tenants 조회 실패 (DDL 미반영 가능) reseller_serial={Serial} role={Role}",
                ctx.ResellerSerial, ctx.Role);
            return Array.Empty<ResellerTenantRow>();
        }
    }

    public async Task<IReadOnlyList<ResellerCsTicketRow>> GetTicketsByResellerAsync(
        string? status, int page, int size, CancellationToken ct = default)
    {
        var p = new DynamicParameters();
        var ctx = ApplyRls(p, out var rlsWhere);

        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 200);
        p.Add("Limit", size);
        p.Add("Offset", (page - 1) * size);

        var extraWhere = string.Empty;
        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            extraWhere += " AND status = @Status ";
            p.Add("Status", status);
        }

        // cs_tickets 는 WS-15 후속 DDL — 미실행 환경에서는 안전 빈 목록 반환.
        var sql = $@"
SELECT ticket_id      AS TicketId,
       ticket_no      AS TicketNo,
       target_serial  AS TargetSerial,
       priority       AS Priority,
       subject        AS Subject,
       status         AS Status,
       opened_at      AS OpenedAt
FROM cs_tickets
{rlsWhere}
{extraWhere}
ORDER BY opened_at DESC
LIMIT @Limit OFFSET @Offset;";

        try
        {
            var db = _unitOfWork.GetDbConnection();
            var rows = await db.QueryAsync<ResellerCsTicketRow>(new CommandDefinition(sql, p, cancellationToken: ct));
            return rows.AsList();
        }
        catch (DbException ex)
        {
            _logger.LogWarning(ex,
                "[ResellerRls] cs_tickets 조회 실패 (DDL 미반영 가능) reseller_serial={Serial}",
                ctx.ResellerSerial);
            return Array.Empty<ResellerCsTicketRow>();
        }
    }

    public async Task<long> CreateTicketByResellerAsync(
        ResellerCsTicketCreateRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.TargetSerial))
        {
            throw new ArgumentException("target_serial 필수", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            throw new ArgumentException("subject 필수", nameof(request));
        }

        var p = new DynamicParameters();
        var ctx = ApplyRls(p, out _);
        // RLS: 대리점은 자기 시리얼 외 target_serial 으로 티켓 생성 금지 — INSERT 전 검증.
        if (ctx.IsReseller)
        {
            // tenants WHERE serial=@Target AND reseller_serial=@__rls_serial 한 행 일치 검증.
            const string verify = @"
SELECT COUNT(1) FROM tenants
WHERE serial = @Target AND reseller_serial = @__rls_serial;";
            p.Add("Target", request.TargetSerial);
            int hit;
            try
            {
                var db0 = _unitOfWork.GetDbConnection();
                hit = await db0.ExecuteScalarAsync<int>(new CommandDefinition(verify, p, cancellationToken: ct));
            }
            catch (DbException ex)
            {
                _logger.LogWarning(ex,
                    "[ResellerRls] tenant 소유권 검증 실패 reseller_serial={Serial} target={Target}",
                    ctx.ResellerSerial, request.TargetSerial);
                throw new UnauthorizedAccessException("대상 시리얼 소유권 검증 실패");
            }
            if (hit == 0)
            {
                // 8명제 #6 — 다른 대리점 고객사에 대한 CS 작성 시도 차단.
                throw new UnauthorizedAccessException(
                    $"reseller_serial='{ctx.ResellerSerial}' 는 target_serial='{request.TargetSerial}' 에 접근 불가");
            }
        }

        var insertParams = new DynamicParameters();
        insertParams.Add("TargetSerial", request.TargetSerial);
        insertParams.Add("Priority", string.IsNullOrWhiteSpace(request.Priority) ? "p2" : request.Priority);
        insertParams.Add("Subject", request.Subject);
        insertParams.Add("Body", request.Body);
        insertParams.Add("ResellerSerial", ctx.IsReseller ? ctx.ResellerSerial : null);
        insertParams.Add("OpenedAt", DateTime.UtcNow);

        // ticket_no = CS-YYYYMMDD-{seq}; 실 발번은 트리거/시퀀스 또는 후속 서비스에서 결합.
        insertParams.Add("TicketNo", $"CS-{DateTime.UtcNow:yyyyMMddHHmmss}");

        const string insert = @"
INSERT INTO cs_tickets
    (ticket_no, target_serial, reseller_serial, priority, subject, body, status, opened_at)
VALUES
    (@TicketNo, @TargetSerial, @ResellerSerial, @Priority, @Subject, @Body, 'open', @OpenedAt);
SELECT LAST_INSERT_ID();";

        try
        {
            var db = _unitOfWork.GetDbConnection();
            var ticketId = await db.ExecuteScalarAsync<long>(new CommandDefinition(insert, insertParams, cancellationToken: ct));
            _logger.LogInformation(
                "[ResellerRls] CS 티켓 생성 ticket_id={Id} reseller_serial={Serial} target={Target}",
                ticketId, ctx.ResellerSerial, request.TargetSerial);
            return ticketId;
        }
        catch (DbException ex)
        {
            _logger.LogWarning(ex,
                "[ResellerRls] CS 티켓 INSERT 실패 (DDL 미반영 가능) reseller_serial={Serial}",
                ctx.ResellerSerial);
            // 미반영 환경에서 임시 ID 0 반환 — UI 는 Snackbar 로 처리.
            return 0L;
        }
    }

    public async Task<IReadOnlyList<ResellerCommissionRow>> GetCommissionsByResellerAsync(
        string? period, int page, int size, CancellationToken ct = default)
    {
        var p = new DynamicParameters();
        var ctx = ApplyRls(p, out var rlsWhere);

        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 200);
        p.Add("Limit", size);
        p.Add("Offset", (page - 1) * size);

        var extraWhere = string.Empty;
        if (!string.IsNullOrWhiteSpace(period))
        {
            extraWhere += " AND period = @Period ";
            p.Add("Period", period);
        }

        // 본 쿼리는 hitpan_backoffice 신규 reseller_commissions 스키마 가도.
        // 기존 hitpan_erp.reseller_commissions(정책 이력) 와 의미 다름 — 정산 결과 메타.
        var sql = $@"
SELECT period       AS Period,
       rate         AS Rate,
       amount       AS Amount,
       status       AS Status,
       settled_at   AS SettledAt
FROM reseller_commissions
{rlsWhere}
{extraWhere}
ORDER BY period DESC
LIMIT @Limit OFFSET @Offset;";

        try
        {
            var db = _unitOfWork.GetDbConnection();
            var rows = await db.QueryAsync<ResellerCommissionRow>(new CommandDefinition(sql, p, cancellationToken: ct));
            return rows.AsList();
        }
        catch (DbException ex)
        {
            _logger.LogWarning(ex,
                "[ResellerRls] reseller_commissions 조회 실패 (DDL 미반영 가능) reseller_serial={Serial}",
                ctx.ResellerSerial);
            return Array.Empty<ResellerCommissionRow>();
        }
    }

    public async Task<ResellerSalesStatsResponse> GetSalesStatsAsync(CancellationToken ct = default)
    {
        var p = new DynamicParameters();
        var ctx = ApplyRls(p, out var rlsWhere);
        var now = DateTime.UtcNow;
        var soon = now.AddDays(30);
        p.Add("Now", now);
        p.Add("Soon", soon);

        // 헌법 #16: MySqlConnection 단일 흐름 직렬 호출 (Task.WhenAll 금지).
        // 4 KPI 를 UNION ALL 단일 쿼리로 묶어 round-trip 1회로 처리.
        var sqlAgg = $@"
SELECT
    (SELECT COUNT(*) FROM tenants {rlsWhere}) AS TenantCount,
    (SELECT COUNT(*) FROM tenants {rlsWhere} AND activation_status = 'active') AS ActiveCount,
    (SELECT COUNT(*) FROM tenants {rlsWhere}
        AND expires_at IS NOT NULL AND expires_at BETWEEN @Now AND @Soon) AS ExpiringSoonCount,
    (SELECT COUNT(*) FROM tenants {rlsWhere}
        AND (payment_status IN ('expired','suspended') OR activation_status = 'suspended')) AS AnomalyCount;";

        const string sqlExpiring = @"
SELECT serial            AS Serial,
       expires_at        AS ExpiresAt,
       DATEDIFF(expires_at, @Now) AS DaysLeft,
       subscription_tier AS SubscriptionTier
FROM tenants
{0}
AND expires_at IS NOT NULL AND expires_at BETWEEN @Now AND @Soon
ORDER BY expires_at ASC
LIMIT 10;";

        try
        {
            var db = _unitOfWork.GetDbConnection();
            var agg = await db.QueryFirstOrDefaultAsync<(int TenantCount, int ActiveCount, int ExpiringSoonCount, int AnomalyCount)>(
                new CommandDefinition(sqlAgg, p, cancellationToken: ct));

            var expiringSql = string.Format(sqlExpiring, rlsWhere);
            var expiring = await db.QueryAsync<ResellerExpiringTenantMeta>(
                new CommandDefinition(expiringSql, p, cancellationToken: ct));

            return new ResellerSalesStatsResponse
            {
                TenantCount = agg.TenantCount,
                ActiveCount = agg.ActiveCount,
                ExpiringSoonCount = agg.ExpiringSoonCount,
                AnomalyCount = agg.AnomalyCount,
                // MonthlyCommissionEstimate 는 reseller_commissions 결합 후 산출 (가도).
                MonthlyCommissionEstimate = 0m,
                ExpiringTenants = expiring.AsList()
            };
        }
        catch (DbException ex)
        {
            _logger.LogWarning(ex,
                "[ResellerRls] sales-stats 조회 실패 (DDL 미반영 가능) reseller_serial={Serial}",
                ctx.ResellerSerial);
            return new ResellerSalesStatsResponse();
        }
    }
}
