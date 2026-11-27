using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Employee;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>인사·근태 통합 서비스 — 출퇴근, 초과근무, HR경비</summary>
public class HrService : IHrService
{
    private readonly IDbConnection _db;
    private readonly INotificationService? _notifier;

    /// <summary>
    /// 감사로그. 작(2026-08-13) 단계7 — 경비는 돈이 오가므로 누가 언제 올렸는지 남아야 한다.
    /// </summary>
    /// <remarks>
    /// ⚠️ 선택(nullable)으로 둔다. <see cref="HrService"/> 는 근태·초과근무도 함께 보는데,
    /// 필수로 바꾸면 이 서비스를 쓰는 모든 자리가 한꺼번에 깨진다(헌법 #12 —
    /// 인터페이스를 넓힐 땐 구현체를 전부 본다). 등록돼 있으면 남기고, 없으면 건너뛴다.
    /// </remarks>
    private readonly IAuditService? _audit;

    public HrService(IDbConnection db, IAuditService? audit = null, INotificationService? notifier = null)
    {
        _db = db;
        _audit = audit;
        _notifier = notifier;
    }

    // ═══ 출퇴근 ═══

    public async Task<List<AttendanceDto>> GetAttendanceAsync(string tenantId, DateTime? from, DateTime? to, string? employeeId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        // ⬛ [낡은 줄 · 20260928작4 쿨타임 이전] `a.work_hours AS WorkHours, a.status AS Status, a.memo AS Memo`
        // 🆕 20260928작4 §6 — 칸 1개만 덧붙였다: `a.check_out_source AS CheckOutSource`(DB-134 · 화면 「자동」 · 「고침」 표시).
        var sql = """
            SELECT a.attendance_id AS AttendanceId, a.employee_id AS EmployeeId, e.emp_name AS EmployeeName,
                   a.work_date AS WorkDate, a.check_in AS CheckIn, a.check_out AS CheckOut,
                   a.work_hours AS WorkHours, a.status AS Status, a.memo AS Memo,
                   a.check_out_source AS CheckOutSource
            FROM attendance a
            LEFT JOIN employees e ON e.employee_id = a.employee_id
            WHERE a.tenant_id = @TenantId
            """;
        if (from.HasValue) sql += " AND a.work_date >= @From";
        if (to.HasValue) sql += " AND a.work_date <= @To";
        if (!string.IsNullOrEmpty(employeeId)) sql += " AND a.employee_id = @EmpId";
        sql += " ORDER BY a.work_date DESC, e.emp_name";

        return (await _db.QueryAsync<AttendanceDto>(new CommandDefinition(
            sql, new { TenantId = tenantId, From = from, To = to, EmpId = employeeId }, cancellationToken: ct))).ToList();
    }

    public async Task<string> CheckInAsync(string tenantId, string employeeId, CheckInOutRequest req, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var id = Guid.NewGuid().ToString();
        var now = DateTime.Now;
        // 오늘 이미 출근했는지 확인
        var existing = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            "SELECT attendance_id FROM attendance WHERE tenant_id = @TenantId AND employee_id = @EmpId AND work_date = @Today",
            new { TenantId = tenantId, EmpId = employeeId, Today = now.Date }, cancellationToken: ct));
        if (existing is not null) throw new InvalidOperationException("오늘 이미 출근 처리되었습니다.");

        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO attendance (attendance_id, tenant_id, employee_id, work_date, check_in, status, memo)
            VALUES (@Id, @TenantId, @EmpId, @Today, @Now, 'normal', @Memo)
            """,
            new { Id = id, TenantId = tenantId, EmpId = employeeId, Today = now.Date, Now = now, Memo = req.Memo }, cancellationToken: ct));
        return id;
    }

    public async Task CheckOutAsync(string tenantId, string employeeId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var now = DateTime.Now;
        var att = await _db.QueryFirstOrDefaultAsync<(string Id, DateTime CheckIn)>(new CommandDefinition(
            "SELECT attendance_id AS Id, check_in AS CheckIn FROM attendance WHERE tenant_id = @TenantId AND employee_id = @EmpId AND work_date = @Today AND check_out IS NULL",
            new { TenantId = tenantId, EmpId = employeeId, Today = now.Date }, cancellationToken: ct));
        if (string.IsNullOrEmpty(att.Id)) throw new InvalidOperationException("출근 기록이 없거나 이미 퇴근 처리되었습니다.");

        var hours = Math.Round((now - att.CheckIn).TotalHours, 1);
        // ⬛ [낡은 문장 · 20260928작4 §D-2 이전]
        //   "UPDATE attendance SET check_out = @Now, work_hours = @Hours WHERE attendance_id = @Id"
        // 🔴 20260928작4 쿨타임 §D-2 안 A(대표 결재 11/27) — 시각 규칙은 그대로, SET 칸 1개만 덧붙였다.
        //   자동 로그아웃이 퇴근을 비워 둔 행(check_out_source='cooltime_blank')을 같은 날 손 퇴근이 채우면
        //   그 순간이 사람이 만든 퇴근이다 ⇒ 「자동」 표시를 지운다(NULL). 다른 값(cooltime·edited·NULL)은 그대로.
        //   ⚠️ 이 CASE 를 빼면 G-C16 이 FAIL 한다(음성 대조군 ⓓ).
        await _db.ExecuteAsync(new CommandDefinition(
            @"UPDATE attendance
                 SET check_out = @Now, work_hours = @Hours,
                     check_out_source = CASE WHEN check_out_source = 'cooltime_blank' THEN NULL ELSE check_out_source END
               WHERE attendance_id = @Id",
            new { Now = now, Hours = hours, Id = att.Id }, cancellationToken: ct));
    }

    /// <summary>
    /// 🔴 <b>대리 출근</b> — 계정 없는 직원의 근태를 권한 있는 사람이 대신 넣는다. 작(2026-08-21) 작10 A.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 필요한가</b> — 종전 <c>CheckInAsync</c> 는 호출자가 늘 JWT 의 <c>employee_id</c> 를
    /// 넘겼다(<c>HrController</c>·<c>AuthController</c> 둘 다). 그래서
    /// <b>계정이 없는 직원은 근태를 남길 방법이 아예 없었다.</b>
    /// 실측(그룹웨어 흐름설계서): <b>사원 12명 중 계정 보유 1명.</b>
    /// </para>
    /// <para>
    /// 🚨 <b>헌법 #2</b> — <c>employeeId</c> 를 <b>파라미터로 받는 첫 경로</b>다.
    /// <c>tenantId</c> 는 여전히 JWT 에서만 오지만, 대상 사원이 <b>정말 내 테넌트 소속인지</b>
    /// 확인하지 않으면 남의 회사 사원 번호를 넣어 근태를 만들 수 있다.
    /// ⇒ <b>소속 검증을 먼저 한다.</b> 이 검사를 지우면 테넌트 격리가 뚫린다.
    /// </para>
    /// <para>
    /// ⚠️ <b>누가 대신 넣었는지 남긴다</b> — <c>memo</c> 에 대리 처리자를 기록한다.
    /// 남의 근태를 만드는 행위라 흔적이 없으면 나중에 다툼이 난다.
    /// </para>
    /// <para>
    /// 🔴 <b>자동 근퇴 경로(<c>CheckInAsync</c>)는 건드리지 않는다.</b> 이미 돌고 있고
    /// 6/22 봉합이 <c>employee_id</c> 키를 통일해 뒀다(자동/수동 이중행 방지).
    /// </para>
    /// </remarks>
    public async Task<string> CheckInProxyAsync(string tenantId, string targetEmployeeId,
        string actorEmployeeId, CheckInOutRequest req, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        await EnsureSameTenantEmployeeAsync(tenantId, targetEmployeeId, ct);

        var actorNote = $"대리입력({actorEmployeeId})";
        var memo = string.IsNullOrWhiteSpace(req.Memo) ? actorNote : $"{req.Memo} / {actorNote}";
        return await CheckInAsync(tenantId, targetEmployeeId, new CheckInOutRequest { Memo = memo }, ct);
    }

    /// <summary>🔴 <b>대리 퇴근</b>. 소속 검증은 <see cref="CheckInProxyAsync"/> 와 같은 이유로 필수다.</summary>
    public async Task CheckOutProxyAsync(string tenantId, string targetEmployeeId,
        string actorEmployeeId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        await EnsureSameTenantEmployeeAsync(tenantId, targetEmployeeId, ct);
        await CheckOutAsync(tenantId, targetEmployeeId, ct);
    }

    /// <summary>
    /// 🚨 <b>테넌트 격리 수문장</b>(헌법 #2). 대상 사원이 내 테넌트 소속일 때만 통과한다.
    /// </summary>
    /// <remarks>
    /// <c>employee_id</c> 는 GUID 라 추측이 어렵지만 <b>비밀이 아니다</b> —
    /// 화면·API 응답에 실려 나간다. 번호를 알아낸 사람이 남의 회사 근태를 만드는 것을 막는 것은
    /// <b>이 검사 하나뿐</b>이다.
    /// </remarks>
    private async Task EnsureSameTenantEmployeeAsync(string tenantId, string employeeId, CancellationToken ct)
    {
        var exists = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            "SELECT employee_id FROM employees WHERE tenant_id = @TenantId AND employee_id = @EmpId AND is_active = 1",
            new { TenantId = tenantId, EmpId = employeeId }, cancellationToken: ct));

        if (string.IsNullOrEmpty(exists))
            throw new InvalidOperationException("대상 직원을 찾을 수 없습니다.");
    }

    // ═══ 초과근무 ═══

    public async Task<List<OvertimeDto>> GetOvertimeAsync(string tenantId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var sql = """
            SELECT o.overtime_id AS OvertimeId, o.employee_id AS EmployeeId, e.emp_name AS EmployeeName,
                   o.work_date AS WorkDate, o.start_time AS StartTime, o.end_time AS EndTime,
                   o.hours AS Hours, o.overtime_type AS OvertimeType, o.reason AS Reason, o.status AS Status
            FROM overtime o
            LEFT JOIN employees e ON e.employee_id = o.employee_id
            WHERE o.tenant_id = @TenantId
            """;
        if (from.HasValue) sql += " AND o.work_date >= @From";
        if (to.HasValue) sql += " AND o.work_date <= @To";
        sql += " ORDER BY o.work_date DESC";

        return (await _db.QueryAsync<OvertimeDto>(new CommandDefinition(
            sql, new { TenantId = tenantId, From = from, To = to }, cancellationToken: ct))).ToList();
    }

    public async Task<string> CreateOvertimeAsync(CreateOvertimeRequest req, string tenantId, string employeeId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var id = Guid.NewGuid().ToString();
        var hours = Math.Round((req.EndTime - req.StartTime).TotalHours, 1);
        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO overtime (overtime_id, tenant_id, employee_id, work_date, start_time, end_time, hours, overtime_type, reason, status)
            VALUES (@Id, @TenantId, @EmpId, @WorkDate, @Start, @End, @Hours, @Type, @Reason, 'pending')
            """,
            new { Id = id, TenantId = tenantId, EmpId = employeeId, req.WorkDate,
                  Start = req.StartTime, End = req.EndTime, Hours = hours, Type = req.OvertimeType, req.Reason }, cancellationToken: ct));

        // 작(2026-08-21) V2 — 사장님 결재: "결재로 일원화" / 오더 7번 "초과근무신청서 작성시 결재메뉴로".
        //
        // 🔴 종전에는 INSERT 만 하고 끝났다. 그런데 결재설정 화면에는 "초과근무" 가 떠서
        //    고객이 켜고 결재선까지 짤 수 있었다 — 켜도 아무 일이 안 일어나는 상태였다.
        //    (기능이 없는 것보다 나쁘다. 했다고 믿게 만든다.)
        //
        // ⚠️ amount 자리에 hours 를 넣는다. 연차가 LeaveDays 를 넣는 것과 같은 방식이다
        //    (LeaveRequestService.cs:120). 이 자리는 '금액' 이 아니라 '기준값' 으로 쓰인다 —
        //    고객이 결재설정에서 자동승인 기준을 시간으로 잡을 수 있다(예: 2시간 미만은 결재 불요).
        //
        // ⚠️ 예외를 삼키지 않는다(헌법 #15). 결재 트리거가 실패해도 초과근무 신청 자체는
        //    이미 저장됐으므로 되돌리지 않는다 — 연차·보고서가 쓰는 방식과 같다.
        var empName = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            "SELECT emp_name FROM employees WHERE tenant_id = @TenantId AND employee_id = @EmpId",
            new { TenantId = tenantId, EmpId = employeeId }, cancellationToken: ct)) ?? string.Empty;

        try
        {
            await ApprovalTriggerHelper.TryCreateApprovalAsync(
                _db, docType: "overtime", refId: id, refNo: id[..8],
                title: $"초과근무 신청: {req.WorkDate:yyyy-MM-dd} {hours}시간",
                amount: (decimal)hours,
                tenantId: tenantId, requesterId: employeeId, requesterName: empName,
                ct: ct, notifier: _notifier).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"[ApprovalTrigger] 초과근무 {id[..8]} 결재 트리거 실패: {ex}");
        }

        return id;
    }

    /// <summary>
    /// 초과근무 승인/반려 (사장님 결재 2026-06-23 — 신청만 되고 승인 경로가 없던 워크플로우 끊김 봉합).
    /// status='pending' 일 때만 변경(멱등) — 이미 처리된 건은 무손상.
    /// </summary>
    public async Task<bool> ApproveOvertimeAsync(string overtimeId, string tenantId, string action, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var status = action == "approved" ? "approved" : "rejected";
        var affected = await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE overtime SET status = @Status
            WHERE overtime_id = @Id AND tenant_id = @TenantId AND status = 'pending'
            """,
            new { Id = overtimeId, TenantId = tenantId, Status = status }, cancellationToken: ct));
        return affected > 0;
    }

    // ═══ HR 경비신청 ═══

    public async Task<List<HrExpenseRequestDto>> GetHrExpensesAsync(string tenantId, string? employeeId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var sql = """
            SELECT r.request_id AS RequestId, r.employee_id AS EmployeeId, e.emp_name AS EmployeeName,
                   r.request_date AS RequestDate, r.category AS Category, r.description AS Description,
                   r.amount AS Amount, r.status AS Status
            FROM hr_expense_requests r
            LEFT JOIN employees e ON e.employee_id = r.employee_id
            WHERE r.tenant_id = @TenantId
            """;
        if (!string.IsNullOrEmpty(employeeId)) sql += " AND r.employee_id = @EmpId";
        sql += " ORDER BY r.request_date DESC";

        return (await _db.QueryAsync<HrExpenseRequestDto>(new CommandDefinition(
            sql, new { TenantId = tenantId, EmpId = employeeId }, cancellationToken: ct))).ToList();
    }

    /// <summary>
    /// 직원이 경비를 올린다. 작(2026-08-13) 그룹웨어 단계7 — <b>회계에 연결한다.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 <b>사장님이 정한 범위</b>(2026-08-13):
    /// <i>"연결만 해두고 비워놔. 그리고 수불부(원장)는 따로 점검해야되 그때, 경비처리하고 원장을 연결하면 될듯."</i>
    ///
    /// ⇒ <b>기표(분개)는 하지 않는다.</b> 경비 항목별 차변 계정과목을 회계 전체를 보고 정해야 하는데,
    ///    경비만 먼저 원장에 올리면 장부가 어긋난다. 원장 전수점검 때 함께 붙인다.
    ///
    /// 🔴 <b>실측으로 잡은 끊긴 자리 3개</b> — 회계 쪽 <c>FinanceService.CreateExpenseAsync</c> 와
    /// 나란히 놓으니 여기에 없는 것이 드러났다:
    /// <list type="number">
    ///   <item><b>결재가 안 올라갔다.</b> 직원은 올린 줄 알고, 결재함엔 안 뜬다 —
    ///         단계3 P0-1 과 같은 "되는 척" 이다.</item>
    ///   <item><b>감사로그가 없었다.</b> 돈이 오가는 신청인데 누가 언제 올렸는지 안 남았다.</item>
    ///   <item><b>월마감을 안 봤다.</b> 마감한 달에 경비가 들어와 결산이 뒤집힐 수 있었다.</item>
    /// </list>
    ///
    /// ⚠️ 두 표가 갈려 있는 것은 <b>지금 합치지 않는다.</b> 실측:
    /// <c>expenses</c> 27,639행(회계 정본·부가세·source_type 완비) /
    /// <c>hr_expense_requests</c> 0행(껍데기). 조회는 <c>FinanceService</c> 가 UNION 으로 이미 붙여
    /// 경리가 직원 신청분을 본다. 표를 합치는 것은 원장 점검 때 회계 전체를 보고 판단할 일이다.
    /// </remarks>
    public async Task<string> CreateHrExpenseAsync(CreateHrExpenseRequest req, string tenantId, string employeeId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        // 🔴 ③ 마감한 달에는 못 넣는다. 회계 쪽과 같은 규칙을 쓴다 —
        //    한쪽만 막으면 경리는 못 넣는데 직원은 넣어지는 어긋남이 난다.
        await ApprovalTriggerHelper.EnsureNotClosedAsync(_db, tenantId, req.RequestDate, ct);

        var id = Guid.NewGuid().ToString();
        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO hr_expense_requests (request_id, tenant_id, employee_id, request_date, category, description, amount, status)
            VALUES (@Id, @TenantId, @EmpId, @Date, @Category, @Description, @Amount, 'pending')
            """,
            new { Id = id, TenantId = tenantId, EmpId = employeeId, Date = req.RequestDate,
                  req.Category, req.Description, req.Amount }, cancellationToken: ct));

        // 🔴 ② 감사로그. 돈이 오가는 신청인데 종전엔 누가 언제 올렸는지 안 남았다.
        //    회계 쪽 CreateExpenseAsync 는 남기고 있었다 — 같은 경비인데 기준이 갈려 있었다.
        if (_audit is not null)
        {
            var auditJson =
                $"{{\"request_date\":\"{req.RequestDate:yyyy-MM-dd}\",\"category\":\"{req.Category}\",\"amount\":{req.Amount}}}";
            await _audit.LogAsync("create", "hr_expense", id, afterJson: auditJson, ct: ct);
        }

        // 🔴 ① 결재에 올린다. 종전엔 이 자리가 통째로 없어서 pending 에 갇혔다.
        //    문서유형은 회계 경비와 **같은 'expense'** 를 쓴다 — 결재선을 두 번 짜게 하면
        //    관리자가 하나만 짜고 나머지는 안 도는 사고가 난다.
        var docNo = $"EXP-{req.RequestDate:yyyyMMdd}-{id[..6]}";
        var title = $"경비 승인 요청: {req.Category} / {req.Amount:N0}원";
        try
        {
            await ApprovalTriggerHelper.TryCreateApprovalAsync(_db,
                "expense", id, docNo, title, req.Amount,
                tenantId, employeeId, "경비신청자", ct, _notifier);
        }
        catch (Exception ex)
        {
            // 경비 행은 이미 커밋됐다. 결재 실패로 500 을 내면 화면은 실패인데 데이터는 남는다
            // (회계 쪽 CreateExpenseAsync 가 같은 판단을 한다). 헌법 #15 — 삼키되 남긴다.
            System.Diagnostics.Trace.TraceWarning($"[ApprovalTrigger] 경비신청 {docNo} 결재 트리거 실패: {ex}");
        }

        return id;
    }

    /// <summary>
    /// 이 경비 신청이 <b>실제로 결재에 올라갔는지</b> 본다.
    /// </summary>
    /// <remarks>
    /// 🔴 단계3 P0-1 교훈: <c>TryCreateApprovalAsync</c> 는 결재 설정이 꺼져 있거나 결재선이 없으면
    /// <b>조용히 아무것도 안 한다</b>. 그때 화면이 "신청했습니다" 만 띄우면 직원은 올라간 줄 알고
    /// 문서는 <c>pending</c> 에 갇힌다. 그래서 <b>세어 보고 사실대로 돌려준다.</b>
    /// </remarks>
    public async Task<(bool Created, string? SkipReason)> CheckHrExpenseApprovalAsync(
        string tenantId, string requestId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        var created = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM approval_documents
            WHERE tenant_id = @TenantId AND doc_type = 'expense' AND ref_id = @RefId
            """,
            new { TenantId = tenantId, RefId = requestId }, cancellationToken: ct));

        if (created > 0) return (true, null);

        // 왜 안 올라갔는지를 사용자 말로 돌려준다 — "설정을 켜세요" 를 알아야 다음 행동을 한다.
        var blocker = await ApprovalTriggerHelper
            .DescribeApprovalBlockerAsync(_db, tenantId, "expense", ct);

        return (false, blocker ?? "결재 문서가 만들어지지 않았습니다. 설정 → 결재설정을 확인해주세요.");
    }

    // ═══ 🆕 20260928작4 쿨타임 §6 · C-5 — 퇴근 고치기 · 고치기 이력 (추가 · #1) ═══
    //
    // 대상 = 자동 로그아웃이 적은 퇴근만(`check_out_source IN ('cooltime','cooltime_blank')` · 설계 §D-2 안 A · C-5 (가)).
    // 권한 = 컨트롤러 `[RequirePermission("HR_PROXY","update")]`(C-5 ㉮ · 누구에게 줄지는 고객사 · #11).
    // 🔴 경주 조건은 UPDATE 한 문장의 WHERE 에 둔다(작업지시서 §4) — 읽은 뒤 쓰는 두 문장으로 판정하지 않는다.
    //    앞 SELECT … FOR UPDATE 는 「이력에 남길 고치기 전 값」 과 「출근 시각」 을 읽는 자리일 뿐이다.
    // #13 — DB 접속 금지 PC 라 출하 DDL 인용으로 갈음: attendance :301-317(check_out_source :313) · audit_trail :353-368 · users :4386-.
    //       [4] 가 CI db-gate 에서 DESCRIBE 대조.

    /// <summary>퇴근 고치기 대상 출처 — 자동 로그아웃이 적은 것만.</summary>
    public static readonly string[] EditableCheckOutSources = ["cooltime", "cooltime_blank"];

    /// <summary>고친 퇴근이 출근 뒤 이 시간 안이어야 한다(야간 근무 포함 · 엉뚱한 날짜 입력 막기).</summary>
    public const int EditCheckOutMaxHoursAfterCheckIn = 24;

    /// <summary>사유 칸 길이(<c>audit_trail.reason varchar(500)</c>).</summary>
    public const int EditCheckOutReasonMax = 500;

    private const string AuditEntityAttendance = "attendance";

    public async Task<EditCheckOutResult> EditCheckOutAsync(
        string tenantId, string attendanceId, string editorUserId, EditCheckOutRequest req, CancellationToken ct = default)
    {
        var reason = (req.Reason ?? string.Empty).Trim();
        if (req.CheckOut is null)
            return new EditCheckOutResult(EditCheckOutOutcome.Invalid, "퇴근 시각을 정해 주세요.");
        if (reason.Length == 0)
            return new EditCheckOutResult(EditCheckOutOutcome.Invalid, "고친 까닭을 적어 주세요.");
        if (reason.Length > EditCheckOutReasonMax) reason = reason[..EditCheckOutReasonMax];
        if (string.IsNullOrWhiteSpace(attendanceId))
            return new EditCheckOutResult(EditCheckOutOutcome.NotFound, "근태 기록을 찾을 수 없습니다.");

        await EnsureOpenAsync(ct);
        using var tx = _db is DbConnection dbc
            ? await dbc.BeginTransactionAsync(ct)
            : _db.BeginTransaction();

        var row = await _db.QueryFirstOrDefaultAsync<(string Id, DateTime? CheckIn, DateTime? CheckOut, string? Source)>(
            new CommandDefinition(
                @"SELECT attendance_id AS Id, check_in AS CheckIn, check_out AS CheckOut, check_out_source AS Source
                    FROM attendance
                   WHERE tenant_id = @TenantId AND attendance_id = @Id
                   FOR UPDATE",
                new { TenantId = tenantId, Id = attendanceId }, tx, cancellationToken: ct));

        // 다른 회사 행 = 없는 행과 같다(있는지조차 알리지 않는다 · G-C12 → 404).
        if (string.IsNullOrEmpty(row.Id))
            return new EditCheckOutResult(EditCheckOutOutcome.NotFound, "근태 기록을 찾을 수 없습니다.");

        if (row.Source is null || !EditableCheckOutSources.Contains(row.Source, StringComparer.Ordinal))
            return new EditCheckOutResult(EditCheckOutOutcome.NotEditable, "자동 로그아웃으로 적힌 퇴근만 고칠 수 있습니다.");

        if (row.CheckIn is null)
            return new EditCheckOutResult(EditCheckOutOutcome.Invalid, "출근 시각이 없는 기록이라 퇴근을 고칠 수 없습니다.");

        var newOut = req.CheckOut.Value;
        if (newOut <= row.CheckIn.Value)
            return new EditCheckOutResult(EditCheckOutOutcome.Invalid, "퇴근 시각은 출근 시각보다 뒤여야 합니다.");
        if (newOut > row.CheckIn.Value.AddHours(EditCheckOutMaxHoursAfterCheckIn))
            return new EditCheckOutResult(EditCheckOutOutcome.Invalid, "퇴근 시각은 출근한 때부터 24시간 안으로 정해 주세요.");

        // 기존 식과 같다(`CheckOutAsync` — 소수 1자리). 금액이 아니지만 칸이 decimal(4,1) 이라 decimal 로 넘긴다.
        var hours = (decimal)Math.Round((newOut - row.CheckIn.Value).TotalHours, 1);

        var changed = await _db.ExecuteAsync(new CommandDefinition(
            @"UPDATE attendance
                 SET check_out = @CheckOut, work_hours = @Hours, check_out_source = 'edited'
               WHERE tenant_id = @TenantId AND attendance_id = @Id
                 AND check_out_source IN ('cooltime', 'cooltime_blank')",
            new { CheckOut = newOut, Hours = hours, TenantId = tenantId, Id = attendanceId }, tx, cancellationToken: ct));
        if (changed != 1)
            return new EditCheckOutResult(EditCheckOutOutcome.NotEditable, "자동 로그아웃으로 적힌 퇴근만 고칠 수 있습니다.");

        // 🔴 JSON 키 고정 — 이력 조회(`GetCheckOutHistoryAsync`)가 같은 키로 읽는다. 설계 §6: {"check_out","source"}.
        var beforeValue = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["check_out"] = row.CheckOut?.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            ["source"] = row.Source
        });
        var afterValue = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["check_out"] = newOut.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            ["source"] = "edited"
        });

        await _db.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO audit_trail
                  (log_id, tenant_id, user_id, action_type, entity_type, entity_id, before_value, after_value, reason)
              VALUES (@LogId, @TenantId, @UserId, 'update', @EntityType, @EntityId, @Before, @After, @Reason)",
            new
            {
                LogId = Guid.NewGuid().ToString(),
                TenantId = tenantId,
                UserId = editorUserId,
                EntityType = AuditEntityAttendance,
                EntityId = attendanceId,
                Before = beforeValue,
                After = afterValue,
                Reason = reason
            }, tx, cancellationToken: ct));

        tx.Commit();
        return new EditCheckOutResult(EditCheckOutOutcome.Saved, "퇴근 시각을 고쳤습니다.");
    }

    public async Task<List<AttendanceEditHistoryDto>?> GetCheckOutHistoryAsync(
        string tenantId, string attendanceId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(attendanceId)) return null;
        await EnsureOpenAsync(ct);

        // 우리 회사 행인지 먼저 — 다른 회사 행이면 404(이력 0 과 가른다).
        var exists = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM attendance WHERE tenant_id = @TenantId AND attendance_id = @Id",
            new { TenantId = tenantId, Id = attendanceId }, cancellationToken: ct));
        if (exists == 0) return null;

        // 기존 색인 (tenant_id, entity_type, entity_id) 그대로(설계 §6).
        var rows = await _db.QueryAsync<(DateTime ChangedAt, string? ChangedByName, string? BeforeValue, string? AfterValue, string? Reason)>(
            new CommandDefinition(
                @"SELECT t.created_at AS ChangedAt, u.user_name AS ChangedByName,
                         t.before_value AS BeforeValue, t.after_value AS AfterValue, t.reason AS Reason
                    FROM audit_trail t
                    LEFT JOIN users u ON u.user_id = t.user_id AND u.tenant_id = t.tenant_id
                   WHERE t.tenant_id = @TenantId AND t.entity_type = @EntityType AND t.entity_id = @Id
                     AND t.action_type = 'update'
                   ORDER BY t.created_at DESC
                   LIMIT 50",
                new { TenantId = tenantId, EntityType = AuditEntityAttendance, Id = attendanceId }, cancellationToken: ct));

        var list = new List<AttendanceEditHistoryDto>();
        foreach (var r in rows)
        {
            var before = ReadCheckOutJson(r.BeforeValue);
            var after = ReadCheckOutJson(r.AfterValue);
            list.Add(new AttendanceEditHistoryDto
            {
                ChangedAt = r.ChangedAt,
                ChangedByName = r.ChangedByName,
                BeforeCheckOut = before.CheckOut,
                AfterCheckOut = after.CheckOut,
                BeforeSource = before.Source,
                Reason = r.Reason
            });
        }
        return list;
    }

    /// <summary>이력 JSON <c>{"check_out","source"}</c> 을 읽는다. 모양이 다르면 빈 값(화면은 「-」).</summary>
    public static (DateTime? CheckOut, string? Source) ReadCheckOutJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, null);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return (null, null);
            DateTime? at = null;
            string? source = null;
            if (doc.RootElement.TryGetProperty("check_out", out var c)
                && c.ValueKind == System.Text.Json.JsonValueKind.String
                && DateTime.TryParse(c.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsed))
            {
                at = parsed;
            }
            if (doc.RootElement.TryGetProperty("source", out var s) && s.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                source = s.GetString();
            }
            return (at, source);
        }
        catch (System.Text.Json.JsonException ex)
        {
            // #15 — 정적 함수라 주입 로거가 없어 Trace 로 남긴다. 모양이 틀린 줄은 화면에 「-」 로 보이고 조회는 계속된다.
            System.Diagnostics.Trace.TraceWarning("근태 고치기 이력 JSON 모양이 다릅니다: {0}", ex.Message);
            return (null, null);
        }
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open) return;
        if (_db is DbConnection c) { await c.OpenAsync(ct); return; }
        _db.Open();
    }
}
