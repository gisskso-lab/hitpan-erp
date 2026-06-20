using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Employee;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>
/// leave_requests 테이블 기반 연차 신청/결재 서비스 구현체이다.
/// </summary>
public sealed class LeaveRequestService : ILeaveRequestService
{
    private readonly IDbConnection _db;

    public LeaveRequestService(IDbConnection db)
    {
        _db = db;
    }

    public async Task<List<LeaveRequestListDto>> GetListAsync(string tenantId, string? employeeId = null, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT
              lr.request_id AS RequestId,
              e.emp_name AS EmployeeName,
              lr.leave_type AS LeaveType,
              lr.leave_days AS LeaveDays,
              lr.start_date AS StartDate,
              lr.end_date AS EndDate,
              lr.status AS Status,
              lr.created_at AS CreatedAt,
              lr.reject_reason AS RejectReason
            FROM leave_requests lr
            INNER JOIN employees e
              ON e.employee_id = lr.employee_id
             AND e.tenant_id = lr.tenant_id
            WHERE lr.tenant_id = @TenantId
              AND (@EmployeeId IS NULL OR lr.employee_id = @EmployeeId)
            ORDER BY lr.created_at DESC
            """;

        var rows = await _db.QueryAsync<LeaveRequestListDto>(new CommandDefinition(
            sql,
            new { TenantId = tenantId, EmployeeId = employeeId },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    public async Task<string> CreateAsync(string tenantId, CreateLeaveRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 봉합 (2026-06-23, 5차 전수조사 LV-W-03 P2): 종전엔 request.EmployeeId 를 활성사원 검증 없이
        //   INSERT 해, 존재하지 않거나 퇴직한 사번으로도 연차 신청 레코드(고아 행)가 생성됐다. 신청 전에
        //   활성 사원인지 확인한다(컨트롤러의 본인성/권한 검증과 2중). leave_requests 에 FK 가 없어 DB 차단도
        //   없으므로 애플리케이션에서 막는다. (설계팀장 승인)
        var empExists = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM employees WHERE tenant_id=@TenantId AND employee_id=@EmpId AND is_active=1",
            new { TenantId = tenantId, EmpId = request.EmployeeId },
            cancellationToken: ct)).ConfigureAwait(false);
        if (empExists == 0)
            throw new InvalidOperationException("연차를 신청할 수 없는 사원입니다(미존재 또는 퇴직).");

        var requestId = Guid.NewGuid().ToString();

        const string sql = """
            INSERT INTO leave_requests (
              request_id, tenant_id, employee_id,
              leave_type, leave_days,
              start_date, end_date,
              reason, status,
              created_at, updated_at)
            VALUES (
              @RequestId, @TenantId, @EmployeeId,
              @LeaveType, @LeaveDays,
              @StartDate, @EndDate,
              @Reason, 'pending',
              NOW(6), NOW(6))
            """;

        await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                RequestId = requestId,
                TenantId = tenantId,
                EmployeeId = request.EmployeeId,
                LeaveType = string.IsNullOrWhiteSpace(request.LeaveType) ? "annual" : request.LeaveType,
                LeaveDays = request.LeaveDays <= 0 ? 1.0m : request.LeaveDays,
                StartDate = request.StartDate.Date,
                EndDate = request.EndDate.Date,
                Reason = request.Reason
            },
            cancellationToken: ct)).ConfigureAwait(false);

        // 결재 트리거 — 연차도 결재 라인이 설정돼 있으면 approval_documents 자동 생성(발신함 노출).
        // 금액은 연차일수 기반(레거시 금액 기반 결재 로직 재활용).
        var empName = await _db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT emp_name FROM employees WHERE tenant_id=@TenantId AND employee_id=@EmpId",
            new { TenantId = tenantId, EmpId = request.EmployeeId },
            cancellationToken: ct)).ConfigureAwait(false) ?? "직원";

        var title = $"연차 신청: {empName} {request.StartDate:yyyy-MM-dd}~{request.EndDate:yyyy-MM-dd} ({request.LeaveDays}일)";
        // 봉합 (2026-06-23, 5차 후속 APPR-TRIGGER P2): 결재 트리거 실패가 호출자로 전파되면 이미 커밋된
        //   연차 신청은 남는데 화면은 500 으로 보이는 불일치(고객 재시도→중복 신청)가 났다. 판매·매입과 동일하게
        //   "신청 레코드는 이미 커밋, 결재는 부가"라는 원칙을 적용해 트리거 실패를 삼키되, 헌법 #15 에 따라
        //   예외 전체를 로그로 남긴다(silent swallow 아님).
        try
        {
            await ApprovalTriggerHelper.TryCreateApprovalAsync(
                _db, docType: "leave", refId: requestId, refNo: requestId[..8],
                title: title, amount: request.LeaveDays,
                tenantId: tenantId, requesterId: request.EmployeeId, requesterName: empName,
                ct: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"[ApprovalTrigger] 연차 {requestId[..8]} 결재 트리거 실패: {ex}");
        }

        return requestId;
    }

    public async Task<bool> ApproveAsync(string tenantId, string approverId, ApproveLeaveRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // P1-3 봉합(2026-06-20): 실제 처리한 결재자(@ApproverId)를 기록한다.
        // LV-01 봉합(2026-06-20): WHERE 에 status='pending' 가드 추가 — 이미 처리된 건 재처리(상태 역전) 차단.
        //   ExecuteAsync 의 affected rows 로 실제 처리 여부 반환(0이면 미존재/이미 처리 → 컨트롤러가 정직하게 응답).
        // 봉합 (2026-06-23, 5차 전수조사 LV-W-01 P2): 종전엔 status 만 바꾸고 employees.annual_leave_used
        //   가산을 안 해, 캘린더가 사용연차를 표시하는데 승인해도 잔여가 안 줄었다(헌법 #20 워크플로우 끊김).
        //   승인(헌법 #6 confirmed 시점)에 잔여를 차감하되, ① 'annual' 연차만(병가·경조사는 연차잔여와 무관)
        //   ② status 전이와 차감을 단일 트랜잭션으로 원자화 ③ 한도 초과는 현장 운영을 막지 않도록 차단하지 않고
        //   경고만 남긴다(재고 음수허용 정신·헌법 #20). 승인취소 경로가 코드에 없어 복원 로직은 불필요(반려는
        //   pending 에서만 → 차감된 적 없음). (설계팀장 승인)
        const string sql = """
            UPDATE leave_requests
            SET status = 'approved',
                approved_by = @ApproverId,
                approved_at = NOW(6),
                reject_reason = NULL,
                updated_at = NOW(6)
            WHERE tenant_id = @TenantId
              AND request_id = @RequestId
              AND status = 'pending'
            """;

        using var tx = (_db as DbConnection)?.BeginTransaction()
                       ?? throw new InvalidOperationException("DbConnection 트랜잭션 사용 불가");
        try
        {
            var affected = await _db.ExecuteAsync(new CommandDefinition(
                sql,
                new { TenantId = tenantId, ApproverId = approverId, RequestId = request.RequestId },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            if (affected == 0)
            {
                tx.Rollback();
                return false; // 미존재 또는 이미 처리 — 차감하지 않음
            }

            // 승인된 신청의 사원·일수·유형 조회 후 'annual' 연차만 잔여 차감.
            var info = await _db.QueryFirstOrDefaultAsync<(string EmployeeId, decimal LeaveDays, string LeaveType)>(
                new CommandDefinition(
                    "SELECT employee_id AS EmployeeId, leave_days AS LeaveDays, leave_type AS LeaveType FROM leave_requests WHERE tenant_id=@TenantId AND request_id=@RequestId",
                    new { TenantId = tenantId, RequestId = request.RequestId },
                    transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            if (info.LeaveType == "annual" && info.LeaveDays > 0m && !string.IsNullOrEmpty(info.EmployeeId))
            {
                await _db.ExecuteAsync(new CommandDefinition(
                    "UPDATE employees SET annual_leave_used = annual_leave_used + @Days, updated_at = NOW(6) WHERE tenant_id=@TenantId AND employee_id=@EmpId",
                    new { Days = info.LeaveDays, TenantId = tenantId, EmpId = info.EmployeeId },
                    transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

                // 한도 초과는 차단하지 않고 경고만 — 현장 연차 선사용·이월 관행 수용(헌법 #20·#15).
                var over = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT CASE WHEN annual_leave_used > annual_leave_total THEN 1 ELSE 0 END FROM employees WHERE tenant_id=@TenantId AND employee_id=@EmpId",
                    new { TenantId = tenantId, EmpId = info.EmployeeId },
                    transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
                if (over == 1)
                    System.Diagnostics.Trace.TraceWarning($"[Leave] 사원 {info.EmployeeId} 연차 한도 초과 사용(선사용/이월 가능) — request {request.RequestId}");
            }

            tx.Commit();
            return true;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<bool> RejectAsync(string tenantId, string approverId, ApproveLeaveRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // P1-3 봉합(2026-06-20): 실제 처리한 결재자(@ApproverId)를 기록한다.
        // LV-01 봉합(2026-06-20): status='pending' 가드 + affected rows 반환(상태 역전·무음 통과 차단).
        const string sql = """
            UPDATE leave_requests
            SET status = 'rejected',
                approved_by = @ApproverId,
                approved_at = NOW(6),
                reject_reason = @RejectReason,
                updated_at = NOW(6)
            WHERE tenant_id = @TenantId
              AND request_id = @RequestId
              AND status = 'pending'
            """;

        var affected = await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                TenantId = tenantId,
                ApproverId = approverId,
                RequestId = request.RequestId,
                RejectReason = request.RejectReason
            },
            cancellationToken: ct)).ConfigureAwait(false);
        return affected > 0;
    }

    /// <summary>
    /// 작20260429 (사장님 결재): 월간 직원 연차 캘린더 조회.
    /// 활성 사원 + 해당 월에 걸치는 휴가 (status approved/pending) 매트릭스로 반환.
    /// 한 사원이 여러 일에 걸친 휴가는 일자별로 셀 펼쳐서 반환.
    /// </summary>
    public async Task<LeaveCalendarDto> GetCalendarAsync(string tenantId, int year, int month, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var monthStart = new DateTime(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        // 1) 활성 사원 + 연차 부여/사용
        const string empSql = """
            SELECT
              employee_id        AS EmployeeId,
              emp_name           AS EmpName,
              position           AS Position,
              annual_leave_total AS AnnualLeaveTotal,
              annual_leave_used  AS AnnualLeaveUsed
            FROM employees
            WHERE tenant_id = @TenantId AND is_active = 1
            ORDER BY emp_no
            """;
        var emps = (await _db.QueryAsync<LeaveCalendarRow>(new CommandDefinition(
            empSql, new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false)).ToList();

        // 2) 이번달과 겹치는 휴가 (start <= monthEnd AND end >= monthStart)
        const string leaveSql = """
            SELECT employee_id, leave_type, leave_days, start_date, end_date, status
            FROM leave_requests
            WHERE tenant_id = @TenantId
              AND status IN ('approved','pending')
              AND start_date <= @MonthEnd
              AND end_date   >= @MonthStart
            """;
        var leaves = (await _db.QueryAsync<(
            string EmployeeId, string LeaveType, decimal LeaveDays, DateTime StartDate, DateTime EndDate, string Status)>(
            new CommandDefinition(leaveSql,
                new { TenantId = tenantId, MonthStart = monthStart, MonthEnd = monthEnd },
                cancellationToken: ct)).ConfigureAwait(false)).ToList();

        // 3) 사원별 셀 분포 채우기 — 일자별로 펼침
        var byEmp = leaves.GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var row in emps)
        {
            if (!byEmp.TryGetValue(row.EmployeeId, out var list)) continue;
            foreach (var lv in list)
            {
                var dStart = lv.StartDate < monthStart ? monthStart : lv.StartDate;
                var dEnd   = lv.EndDate   > monthEnd   ? monthEnd   : lv.EndDate;
                for (var d = dStart; d <= dEnd; d = d.AddDays(1))
                {
                    row.Cells.Add(new LeaveCalendarCell
                    {
                        Date = d,
                        LeaveType = lv.LeaveType,
                        LeaveDays = lv.LeaveDays,
                        Status = lv.Status
                    });
                }
            }
        }

        // 4) 통계
        var totalCount = emps.Sum(r => r.Cells.Count);
        var top = emps.OrderByDescending(r => r.Cells.Count).FirstOrDefault();

        return new LeaveCalendarDto
        {
            Year = year,
            Month = month,
            DayCount = DateTime.DaysInMonth(year, month),
            Rows = emps,
            TotalLeaveCount = totalCount,
            TopUserName = top?.Cells.Count > 0 ? top.EmpName : null,
            TopUserDays = top?.Cells.Count > 0 ? top.Cells.Count : 0m
        };
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open)
        {
            return;
        }

        if (_db is DbConnection dbConnection)
        {
            await dbConnection.OpenAsync(ct).ConfigureAwait(false);
            return;
        }

        _db.Open();
    }
}
