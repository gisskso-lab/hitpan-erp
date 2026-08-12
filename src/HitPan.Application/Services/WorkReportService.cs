using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.WorkReport;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>
/// <c>hr_reports</c> 기반 업무보고서 서비스. 작(2026-08-13) 그룹웨어 단계3.
/// </summary>
/// <remarks>
/// <para>
/// 사장님 지시(2026-08-12): <i>"일일보고서, 주간보고서, 월간보고서, 경위서 메뉴 추가"</i>
/// </para>
/// <para>
/// 🔴 <b>설계 정정</b> — 흐름설계서에 <i>"보고서는 DDL 변경 0"</i> 이라고 썼으나 <b>틀렸다</b>.
/// <c>approval_documents</c> 는 결재 흐름만 담고 본문은 <c>ref_id</c> 로 원본을 가리킨다.
/// 즉 보고서 본문이 들어갈 자리가 없었다(<c>memo</c> 는 500자라 월간보고서·경위서를 못 담는다).
/// ⇒ <c>hr_reports</c> 를 신설했다(DB-92 + 출하 DDL 동시 반영, 헌법 #36).
/// </para>
/// <para>
/// 🔴 <b>사원 기준</b>이다(설계서 §3-5 축 확정). 계정이 없어도 보고서를 쓸 수 있어야 한다 —
/// 실측상 사원 12명 중 계정 보유는 1명뿐이라, 계정 기준으로 잡으면 11명이 사라진다.
/// </para>
/// </remarks>
public sealed class WorkReportService : IWorkReportService
{
    private readonly IDbConnection _db;
    private readonly INotificationService? _notifier;

    public WorkReportService(IDbConnection db, INotificationService? notifier = null)
    {
        _db = db;
        _notifier = notifier;
    }

    public async Task<List<WorkReportListDto>> GetListAsync(string tenantId, string? employeeId,
        string? reportType, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // reject_reason 을 함께 내려준다 — 없으면 반려된 사람이 이유를 모른다
        // (같은 병이 연차에 있었고 6/20 에 봉합됐다. 되풀이하지 않는다).
        const string sql = """
            SELECT
              r.report_id     AS ReportId,
              r.employee_id   AS EmployeeId,
              e.emp_name      AS EmployeeName,
              r.report_type   AS ReportType,
              r.period_start  AS PeriodStart,
              r.period_end    AS PeriodEnd,
              r.title         AS Title,
              r.status        AS Status,
              r.submitted_at  AS SubmittedAt,
              r.created_at    AS CreatedAt,
              r.reject_reason AS RejectReason
            FROM hr_reports r
            LEFT JOIN employees e
              ON e.employee_id = r.employee_id
             AND e.tenant_id = r.tenant_id
            WHERE r.tenant_id = @TenantId
              AND (@EmployeeId IS NULL OR r.employee_id = @EmployeeId)
              AND (@ReportType IS NULL OR r.report_type = @ReportType)
              AND (@From IS NULL OR r.period_end >= @From)
              AND (@To IS NULL OR r.period_start <= @To)
            ORDER BY r.period_start DESC, r.created_at DESC
            """;

        var rows = await _db.QueryAsync<WorkReportListDto>(new CommandDefinition(
            sql,
            new
            {
                TenantId = tenantId,
                EmployeeId = employeeId,
                ReportType = reportType,
                From = from?.Date,
                To = to?.Date
            },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    public async Task<WorkReportDetailDto?> GetAsync(string tenantId, string reportId,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT
              r.report_id     AS ReportId,
              r.employee_id   AS EmployeeId,
              e.emp_name      AS EmployeeName,
              r.report_type   AS ReportType,
              r.period_start  AS PeriodStart,
              r.period_end    AS PeriodEnd,
              r.title         AS Title,
              r.content       AS Content,
              r.cause         AS Cause,
              r.action_plan   AS ActionPlan,
              r.status        AS Status,
              r.submitted_at  AS SubmittedAt,
              r.approved_by   AS ApprovedBy,
              r.approved_at   AS ApprovedAt,
              r.reject_reason AS RejectReason,
              r.created_at    AS CreatedAt,
              r.updated_at    AS UpdatedAt
            FROM hr_reports r
            LEFT JOIN employees e
              ON e.employee_id = r.employee_id
             AND e.tenant_id = r.tenant_id
            WHERE r.tenant_id = @TenantId
              AND r.report_id = @ReportId
            """;

        return await _db.QueryFirstOrDefaultAsync<WorkReportDetailDto>(new CommandDefinition(
            sql,
            new { TenantId = tenantId, ReportId = reportId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<string> CreateAsync(string tenantId, string employeeId, string employeeName,
        SaveWorkReportRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 연차 신청과 같은 가드 — 없는 사원·퇴사자 이름으로 고아 행이 생기지 않게 한다.
        var empExists = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM employees WHERE tenant_id=@TenantId AND employee_id=@EmpId AND is_active=1",
            new { TenantId = tenantId, EmpId = employeeId },
            cancellationToken: ct)).ConfigureAwait(false);
        if (empExists == 0)
        {
            throw new InvalidOperationException("보고서를 작성할 수 없는 사원입니다(미존재 또는 퇴직).");
        }

        var reportId = Guid.NewGuid().ToString();
        var reportType = WorkReportTypes.Normalize(request.ReportType);
        var (start, end) = NormalizePeriod(reportType, request.PeriodStart, request.PeriodEnd);
        var title = BuildTitle(request.Title, reportType, start, end, employeeName);

        const string sql = """
            INSERT INTO hr_reports
              (report_id, tenant_id, employee_id, report_type,
               period_start, period_end, title, content, cause, action_plan,
               status, submitted_at, created_at, updated_at)
            VALUES
              (@ReportId, @TenantId, @EmployeeId, @ReportType,
               @PeriodStart, @PeriodEnd, @Title, @Content, @Cause, @ActionPlan,
               @Status, @SubmittedAt, NOW(6), NOW(6))
            """;

        await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                ReportId = reportId,
                TenantId = tenantId,
                EmployeeId = employeeId,
                ReportType = reportType,
                PeriodStart = start,
                PeriodEnd = end,
                Title = title,
                Content = request.Content ?? string.Empty,
                // 경위서가 아니면 비운다 — 다른 종류에 남아 있으면 화면이 헷갈린다.
                Cause = reportType == WorkReportTypes.Incident ? request.Cause : null,
                ActionPlan = reportType == WorkReportTypes.Incident ? request.ActionPlan : null,
                Status = request.Submit ? WorkReportStatuses.Pending : WorkReportStatuses.Draft,
                SubmittedAt = request.Submit ? DateTime.Now : (DateTime?)null
            },
            cancellationToken: ct)).ConfigureAwait(false);

        if (request.Submit)
        {
            await TriggerApprovalAsync(tenantId, reportId, reportType, employeeId, employeeName, title, ct)
                .ConfigureAwait(false);
        }

        return reportId;
    }

    public async Task<bool> UpdateAsync(string tenantId, string reportId, string employeeId,
        SaveWorkReportRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var reportType = WorkReportTypes.Normalize(request.ReportType);
        var (start, end) = NormalizePeriod(reportType, request.PeriodStart, request.PeriodEnd);
        var title = BuildTitle(request.Title, reportType, start, end, null);

        // 🔴 작성중·반려 상태에서만 고칠 수 있다.
        //    결재중인 보고서를 고치면 결재자가 본 것과 다른 내용이 승인된다.
        //    승인 완료분은 기록이므로 손대지 않는다.
        // 🔴 본인 것만 — 남의 보고서를 고칠 수 없다(WHERE 에 employee_id).
        const string sql = """
            UPDATE hr_reports
            SET report_type   = @ReportType,
                period_start  = @PeriodStart,
                period_end    = @PeriodEnd,
                title         = @Title,
                content       = @Content,
                cause         = @Cause,
                action_plan   = @ActionPlan,
                status        = @Status,
                submitted_at  = CASE WHEN @Submit THEN NOW(6) ELSE submitted_at END,
                reject_reason = CASE WHEN @Submit THEN NULL ELSE reject_reason END,
                updated_at    = NOW(6)
            WHERE tenant_id = @TenantId
              AND report_id = @ReportId
              AND employee_id = @EmployeeId
              AND status IN ('draft', 'rejected')
            """;

        var affected = await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                TenantId = tenantId,
                ReportId = reportId,
                EmployeeId = employeeId,
                ReportType = reportType,
                PeriodStart = start,
                PeriodEnd = end,
                Title = title,
                Content = request.Content ?? string.Empty,
                Cause = reportType == WorkReportTypes.Incident ? request.Cause : null,
                ActionPlan = reportType == WorkReportTypes.Incident ? request.ActionPlan : null,
                Status = request.Submit ? WorkReportStatuses.Pending : WorkReportStatuses.Draft,
                Submit = request.Submit
            },
            cancellationToken: ct)).ConfigureAwait(false);

        if (affected == 0)
        {
            return false;
        }

        if (request.Submit)
        {
            var empName = await GetEmployeeNameAsync(tenantId, employeeId, ct).ConfigureAwait(false);
            await TriggerApprovalAsync(tenantId, reportId, reportType, employeeId, empName, title, ct)
                .ConfigureAwait(false);
        }

        return true;
    }

    public async Task<bool> SubmitAsync(string tenantId, string reportId, string employeeId,
        string employeeName, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            UPDATE hr_reports
            SET status = 'pending',
                submitted_at = NOW(6),
                reject_reason = NULL,
                updated_at = NOW(6)
            WHERE tenant_id = @TenantId
              AND report_id = @ReportId
              AND employee_id = @EmployeeId
              AND status IN ('draft', 'rejected')
            """;

        var affected = await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new { TenantId = tenantId, ReportId = reportId, EmployeeId = employeeId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (affected == 0)
        {
            return false;
        }

        var detail = await GetAsync(tenantId, reportId, ct).ConfigureAwait(false);
        if (detail is not null)
        {
            await TriggerApprovalAsync(tenantId, reportId, detail.ReportType,
                employeeId, employeeName, detail.Title, ct).ConfigureAwait(false);
        }

        return true;
    }

    public async Task<bool> DeleteAsync(string tenantId, string reportId, string employeeId,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 🔴 작성중일 때만 지운다. 결재에 올라간 보고서는 기록이라 지우면 안 된다.
        const string sql = """
            DELETE FROM hr_reports
            WHERE tenant_id = @TenantId
              AND report_id = @ReportId
              AND employee_id = @EmployeeId
              AND status = 'draft'
            """;

        var affected = await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new { TenantId = tenantId, ReportId = reportId, EmployeeId = employeeId },
            cancellationToken: ct)).ConfigureAwait(false);

        return affected > 0;
    }

    // ───────────────────────────────────────────────────────────────
    // 헬퍼
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 결재를 만든다. 결재 설정이 꺼져 있거나 결재선이 없으면 조용히 지나간다.
    /// </summary>
    /// <remarks>
    /// 🔴 결재 트리거 실패가 <b>이미 저장된 보고서를 되돌리면 안 된다.</b>
    /// 판매·매입·연차가 쓰는 원칙과 같다 — 본문은 이미 커밋, 결재는 부가.
    /// 다만 삼키지 않고 로그로 남긴다(헌법 #15).
    /// </remarks>
    private async Task TriggerApprovalAsync(string tenantId, string reportId, string reportType,
        string employeeId, string employeeName, string title, CancellationToken ct)
    {
        try
        {
            await ApprovalTriggerHelper.TryCreateApprovalAsync(
                _db,
                docType: WorkReportTypes.ToDocType(reportType),
                refId: reportId,
                refNo: reportId[..8],
                title: title,
                // 보고서에는 금액이 없다. 기준금액 자동승인에 걸리지 않도록 0 을 넣는다.
                amount: 0m,
                tenantId: tenantId,
                requesterId: employeeId,
                requesterName: employeeName,
                ct: ct,
                notifier: _notifier).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"[ApprovalTrigger] 보고서 {reportId[..8]} 결재 트리거 실패: {ex}");
        }
    }

    private async Task<string> GetEmployeeNameAsync(string tenantId, string employeeId,
        CancellationToken ct)
        => await _db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
               "SELECT emp_name FROM employees WHERE tenant_id=@TenantId AND employee_id=@EmpId",
               new { TenantId = tenantId, EmpId = employeeId },
               cancellationToken: ct)).ConfigureAwait(false) ?? "직원";

    /// <summary>
    /// 기간을 종류에 맞게 다듬는다.
    /// </summary>
    /// <remarks>
    /// 일일보고서·경위서는 하루짜리다 — 시작일로 맞춘다.
    /// 뒤집혀 들어오면(종료 &lt; 시작) 바로잡는다. 날짜를 거꾸로 고르는 일은 흔하다.
    /// </remarks>
    private static (DateTime Start, DateTime End) NormalizePeriod(
        string reportType, DateTime start, DateTime end)
    {
        var s = start.Date;
        var e = end.Date;

        if (reportType is WorkReportTypes.Daily or WorkReportTypes.Incident)
        {
            return (s, s);
        }

        return e < s ? (s, s) : (s, e);
    }

    /// <summary>
    /// 제목이 비면 만들어 준다. 목록에서 제목 없는 줄이 보이면 무엇인지 알 수 없다.
    /// </summary>
    private static string BuildTitle(string? title, string reportType,
        DateTime start, DateTime end, string? employeeName)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            return title.Length > 200 ? title[..200] : title;
        }

        var label = WorkReportTypes.DisplayName(reportType);
        var period = start == end
            ? $"{start:yyyy-MM-dd}"
            : $"{start:yyyy-MM-dd}~{end:yyyy-MM-dd}";

        return string.IsNullOrWhiteSpace(employeeName)
            ? $"{label} {period}"
            : $"{label} {period} {employeeName}";
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State != ConnectionState.Open && _db is DbConnection dbc)
        {
            await dbc.OpenAsync(ct).ConfigureAwait(false);
        }
    }
}
