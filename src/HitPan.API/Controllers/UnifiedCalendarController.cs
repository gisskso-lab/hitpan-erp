using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.API.Controllers;

/// <summary>
/// 통합 캘린더 API — 4축(수금/입금/연차/이슈) 통합 조회 컨트롤러.
/// 사장님 결재 2026-05-26 (통합 캘린더 + 카드 모달 가도).
/// §#2 tenant_id JWT 클레임 전용 / §#4 decimal / §#15 빈 catch 금지 / §#16 QueryMultipleAsync.
/// </summary>
[ApiController]
[Route("api/dashboard")]
[Authorize]
public sealed class UnifiedCalendarController : ControllerBase
{
    private readonly string _connStr;
    private readonly ILogger<UnifiedCalendarController> _logger;

    public UnifiedCalendarController(IConfiguration config, ILogger<UnifiedCalendarController> logger)
    {
        _connStr = config.GetConnectionString("DefaultConnection")
                   ?? throw new InvalidOperationException("DB 연결 문자열이 없습니다.");
        _logger = logger;
    }

    /// <summary>월간 4축 통합 데이터(셀별 카운트).</summary>
    [HttpGet("unified-calendar")]
    public async Task<IActionResult> GetMonthly([FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }
        if (month < 1 || month > 12) return BadRequest(new { message = "month must be 1~12" });

        var monthStart = new DateTime(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        // §#16: MySqlConnection + Task.WhenAll 금지 → QueryMultipleAsync 단일 커넥션 직렬 실행.
        const string sql = @"
            -- 1) 수금 (collections)
            SELECT collection_date AS Day, COUNT(*) AS Cnt, SUM(amount) AS Amt
            FROM collections
            WHERE tenant_id=@TenantId AND is_active=1
              AND collection_date BETWEEN @MonthStart AND @MonthEnd
            GROUP BY collection_date;

            -- 2) 입금/지급 (bank_transactions tx_type='2' 출금 = 거래처 입금)
            SELECT tx_date AS Day, COUNT(*) AS Cnt, SUM(amount) AS Amt
            FROM bank_transactions
            WHERE tenant_id=@TenantId AND tx_type='2'
              AND tx_date BETWEEN @MonthStart AND @MonthEnd
            GROUP BY tx_date;

            -- 3) 연차 (leave_requests, approved/pending) — 시작일 기준 집계
            SELECT start_date AS Day, COUNT(*) AS Cnt, 0 AS Amt
            FROM leave_requests
            WHERE tenant_id=@TenantId
              AND status IN ('approved','pending')
              AND start_date <= @MonthEnd AND end_date >= @MonthStart
            GROUP BY start_date;
        ";

        var dayMap = new Dictionary<int, DayBuckets>();
        for (int d = 1; d <= DateTime.DaysInMonth(year, month); d++)
            dayMap[d] = new DayBuckets();

        try
        {
            await using var db = new MySqlConnection(_connStr);
            await db.OpenAsync(ct).ConfigureAwait(false);

            using var multi = await db.QueryMultipleAsync(new CommandDefinition(
                sql,
                new { TenantId = tenantId, MonthStart = monthStart.Date, MonthEnd = monthEnd.Date },
                cancellationToken: ct)).ConfigureAwait(false);

            foreach (var row in await multi.ReadAsync<DayAgg>().ConfigureAwait(false))
            {
                if (dayMap.TryGetValue(row.Day.Day, out var b)) { b.Collection = row.Cnt; b.CollectionAmount = row.Amt; }
            }
            foreach (var row in await multi.ReadAsync<DayAgg>().ConfigureAwait(false))
            {
                if (dayMap.TryGetValue(row.Day.Day, out var b)) { b.Payment = row.Cnt; b.PaymentAmount = row.Amt; }
            }
            foreach (var row in await multi.ReadAsync<DayAgg>().ConfigureAwait(false))
            {
                if (dayMap.TryGetValue(row.Day.Day, out var b)) { b.Leave = row.Cnt; }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[UnifiedCalendar.GetMonthly] 통합 캘린더 조회 실패 tenant={TenantId} {Y}-{M}", tenantId, year, month);
            // 일부 테이블 부재 시(예: schedules ALTER 전) 빈 결과로 graceful degrade.
        }

        // 4) 이슈(schedules) — 별도 try (테이블 미존재 가능)
        try
        {
            await using var db2 = new MySqlConnection(_connStr);
            await db2.OpenAsync(ct).ConfigureAwait(false);
            const string issueSql = @"
                SELECT DATE(start_at) AS Day, COUNT(*) AS Cnt, 0 AS Amt
                FROM schedules
                WHERE tenant_id=@TenantId
                  AND start_at >= @MonthStart AND start_at < @MonthEndExclusive
                GROUP BY DATE(start_at)";
            var rows = await db2.QueryAsync<DayAgg>(new CommandDefinition(
                issueSql,
                new { TenantId = tenantId, MonthStart = monthStart, MonthEndExclusive = monthStart.AddMonths(1) },
                cancellationToken: ct)).ConfigureAwait(false);
            foreach (var row in rows)
            {
                if (dayMap.TryGetValue(row.Day.Day, out var b)) { b.Issue = row.Cnt; }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[UnifiedCalendar.GetMonthly] schedules 테이블 조회 실패(미생성 가능) tenant={TenantId}", tenantId);
        }

        var days = dayMap
            .OrderBy(kv => kv.Key)
            .Select(kv => new
            {
                day = kv.Key,
                collection = kv.Value.Collection,
                payment = kv.Value.Payment,
                leave = kv.Value.Leave,
                issue = kv.Value.Issue,
                collectionAmount = kv.Value.CollectionAmount,
                paymentAmount = kv.Value.PaymentAmount
            })
            .ToList();

        return Ok(new
        {
            year,
            month,
            dayCount = DateTime.DaysInMonth(year, month),
            days,
            totals = new
            {
                collection = days.Sum(x => x.collection),
                payment = days.Sum(x => x.payment),
                leave = days.Sum(x => x.leave),
                issue = days.Sum(x => x.issue)
            }
        });
    }

    /// <summary>특정 날짜 4축 상세 — 카드 모달용.</summary>
    [HttpGet("day-detail")]
    public async Task<IActionResult> GetDayDetail([FromQuery] DateTime date, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var dayStart = date.Date;
        var dayEnd = dayStart.AddDays(1);

        var collections = new List<object>();
        var payments = new List<object>();
        var leaves = new List<object>();
        var issues = new List<object>();

        const string sql = @"
            SELECT c.collection_id AS Id, c.partner_id AS PartnerId, p.partner_name AS PartnerName,
                   c.amount AS Amount, c.collection_method AS Method, c.memo AS Memo,
                   c.ref_doc_type AS RefDocType, c.ref_doc_id AS RefDocId
            FROM collections c
            LEFT JOIN partners p ON p.partner_id = c.partner_id AND p.tenant_id = c.tenant_id
            WHERE c.tenant_id=@TenantId AND c.is_active=1 AND c.collection_date=@Day;

            SELECT bt.bank_tx_id AS Id, bt.partner_id AS PartnerId,
                   COALESCE(p.partner_name, bt.partner_name_legacy) AS PartnerName,
                   bt.amount AS Amount, bt.account_no AS AccountNo, bt.bank_name AS BankName,
                   bt.description AS Description
            FROM bank_transactions bt
            LEFT JOIN partners p ON p.partner_id = bt.partner_id AND p.tenant_id = bt.tenant_id
            WHERE bt.tenant_id=@TenantId AND bt.tx_type='2' AND bt.tx_date=@Day;

            SELECT lr.request_id AS Id, lr.employee_id AS EmployeeId, e.emp_name AS EmpName,
                   lr.leave_type AS LeaveType, lr.leave_days AS LeaveDays,
                   lr.start_date AS StartDate, lr.end_date AS EndDate, lr.status AS Status,
                   lr.reason AS Reason
            FROM leave_requests lr
            LEFT JOIN employees e ON e.employee_id = lr.employee_id AND e.tenant_id = lr.tenant_id
            WHERE lr.tenant_id=@TenantId
              AND lr.status IN ('approved','pending')
              AND lr.start_date <= @Day AND lr.end_date >= @Day;
        ";

        try
        {
            await using var db = new MySqlConnection(_connStr);
            await db.OpenAsync(ct).ConfigureAwait(false);
            using var multi = await db.QueryMultipleAsync(new CommandDefinition(
                sql, new { TenantId = tenantId, Day = dayStart }, cancellationToken: ct)).ConfigureAwait(false);

            collections.AddRange((await multi.ReadAsync().ConfigureAwait(false)).Cast<object>());
            payments.AddRange((await multi.ReadAsync().ConfigureAwait(false)).Cast<object>());
            leaves.AddRange((await multi.ReadAsync().ConfigureAwait(false)).Cast<object>());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[UnifiedCalendar.GetDayDetail] 4축 조회 실패 tenant={TenantId} date={Date}", tenantId, date);
        }

        // schedules는 미생성 가능 → 별도 try
        try
        {
            await using var db2 = new MySqlConnection(_connStr);
            await db2.OpenAsync(ct).ConfigureAwait(false);
            const string issueSql = @"
                SELECT schedule_id AS Id, title AS Title, schedule_type AS Type,
                       start_at AS StartAt, end_at AS EndAt,
                       partner_id AS PartnerId, partner_name AS PartnerName,
                       participant_id AS ParticipantId, participant_name AS ParticipantName,
                       memo AS Memo, is_completed AS IsCompleted
                FROM schedules
                WHERE tenant_id=@TenantId
                  AND start_at >= @DayStart AND start_at < @DayEnd";
            var rows = await db2.QueryAsync(new CommandDefinition(
                issueSql,
                new { TenantId = tenantId, DayStart = dayStart, DayEnd = dayEnd },
                cancellationToken: ct)).ConfigureAwait(false);
            issues.AddRange(rows.Cast<object>());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[UnifiedCalendar.GetDayDetail] schedules 조회 실패(미생성 가능) tenant={TenantId}", tenantId);
        }

        return Ok(new
        {
            date = dayStart,
            collections,
            payments,
            leaves,
            issues
        });
    }

    private sealed class DayBuckets
    {
        public int Collection { get; set; }
        public int Payment { get; set; }
        public int Leave { get; set; }
        public int Issue { get; set; }
        public decimal CollectionAmount { get; set; }
        public decimal PaymentAmount { get; set; }
    }

    private sealed class DayAgg
    {
        public DateTime Day { get; set; }
        public int Cnt { get; set; }
        public decimal Amt { get; set; }
    }
}
