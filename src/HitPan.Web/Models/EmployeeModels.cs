namespace HitPan.Web.Models;

/// <summary>
/// 사원 목록 테이블 행 모델이다.
/// </summary>
public sealed class EmployeeListItemModel
{
    public string EmployeeId { get; set; } = string.Empty;
    public string EmpNo { get; set; } = string.Empty;
    public string EmpName { get; set; } = string.Empty;
    public string? DeptName { get; set; }
    public string? Position { get; set; }
    public string? JobTitle { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string Role { get; set; } = "sales_user";
    public bool IsActive { get; set; }
    public bool HasUserAccount { get; set; }

    // 작20260429 연차 관리 (사장님 결재): 그리드 인라인 편집·저장
    public decimal AnnualLeaveTotal { get; set; }
    public decimal AnnualLeaveUsed { get; set; }
    public decimal AnnualLeaveRemaining => AnnualLeaveTotal - AnnualLeaveUsed;
}

/// <summary>
/// 작20260429 연차 저장 요청 모델 (사장님 결재).
/// </summary>
public sealed class UpdateAnnualLeaveModel
{
    public decimal AnnualLeaveTotal { get; set; }
    public decimal AnnualLeaveUsed { get; set; }
}

/// <summary>
/// 작20260429 (사장님 결재): 대시보드 월간 연차 캘린더 응답 모델.
/// </summary>
public sealed class LeaveCalendarModel
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int DayCount { get; set; }
    public List<LeaveCalendarRowModel> Rows { get; set; } = new();
    public int TotalLeaveCount { get; set; }
    public string? TopUserName { get; set; }
    public decimal TopUserDays { get; set; }
}

public sealed class LeaveCalendarRowModel
{
    public string EmployeeId { get; set; } = string.Empty;
    public string EmpName { get; set; } = string.Empty;
    public string? Position { get; set; }
    public decimal AnnualLeaveTotal { get; set; }
    public decimal AnnualLeaveUsed { get; set; }
    public decimal AnnualLeaveRemaining => AnnualLeaveTotal - AnnualLeaveUsed;
    public List<LeaveCalendarCellModel> Cells { get; set; } = new();
}

public sealed class LeaveCalendarCellModel
{
    public DateTime Date { get; set; }
    public string LeaveType { get; set; } = "annual";
    public decimal LeaveDays { get; set; }
    public string Status { get; set; } = "approved";
}

/// <summary>
/// 사원 입력 그리드 편집 모델이다.
/// </summary>
public sealed class EmployeeEditModel
{
    public string EmpName { get; set; } = string.Empty;
    public string? DeptId { get; set; }
    public string? DeptName { get; set; }
    public string? Position { get; set; }
    public string? JobTitle { get; set; }
    public string EmpType { get; set; } = "regular";

    /// <summary>
    /// 주당 소정근로시간(약정). <c>null</c> = 미정.
    /// </summary>
    /// <remarks>
    /// 🔴 작(2026-08-13) 단계4. 연차·주휴·4대보험이 이 숫자로 갈린다(주 15시간이 갈림길).
    /// 고용형태만으로는 판정이 안 된다 — 같은 알바라도 주 20시간과 10시간은 법적 취급이 다르다.
    /// ⚠️ 기본값을 40 으로 두지 않는다. 모르는 것을 아는 척 채우면 그 값으로 연차가
    /// 계산되고 틀린 줄도 모른 채 법정 미달이 된다(반자동 원칙, 사장님 2026-08-12).
    /// </remarks>
    public decimal? WeeklyHours { get; set; }

    public DateTime JoinDate { get; set; } = DateTime.Today;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string Role { get; set; } = "sales_user";
}

/// <summary>
/// 고용형태 표시 이름. 화면·명세서가 이걸 쓴다.
/// </summary>
/// <remarks>
/// 🔴 사장님 지시(2026-08-12): <i>"정직원이냐, 알바냐, 계약직이냐, 무기계약직이냐 에 따라서도 달라짐"</i>
/// 종전엔 <c>EmployeePage.razor</c> 마크업에 "정규직"·"파트타임" 이 하드코딩돼 있었고
/// <b>무기계약직이 아예 없었다</b>. 한 곳에서만 정해 다른 화면이 생겨도 말이 안 갈리게 한다.
///
/// <para>
/// ⚠️ 값(<c>regular</c>·<c>part</c> …)은 DB 에 저장된 그대로다. <b>값은 안 바꾸고 보이는 말만 바꿨다</b> —
/// 값을 바꾸면 기존 행이 매칭 실패하는데 <c>EmployeeConfiguration.ParseEmpType</c> 이 그걸
/// 조용히 <c>Regular</c> 로 폴백해 <b>화면엔 정직원으로 보이면서 DB엔 딴 값이 남는다</b>.
/// </para>
///
/// ⚠️ 서버(<c>HitPan.Domain.Enums.EmployeeTypeLabels</c>)에도 같은 표가 있다.
/// Web 이 Domain 을 참조하지 않아(계층 경계) 각자 둔다 — <b>한쪽을 고치면 다른 쪽도 고친다</b>.
/// (게이트: EmployeeTypeLabelGuardTests)
/// </remarks>
public static class EmployeeTypeLabels
{
    public const string Regular = "정직원";
    public const string Contract = "계약직";
    public const string Part = "알바(단시간)";
    public const string Dispatch = "파견";
    public const string Permanent = "무기계약직";
    public const string Daily = "일용직";

    /// <summary>DB 값(소문자) → 사람이 읽는 이름.</summary>
    public static string Of(string? empType) => empType?.ToLowerInvariant() switch
    {
        "regular" => Regular,
        "contract" => Contract,
        "part" => Part,
        "dispatch" => Dispatch,
        "permanent" => Permanent,
        "daily" => Daily,
        // 모르는 값은 감추지 않고 그대로 보여준다 — 조용히 "정직원" 으로 보이면
        // 오염을 영영 못 찾는다.
        _ => string.IsNullOrWhiteSpace(empType) ? "-" : empType!
    };
}

/// <summary>
/// 부서 드롭다운 모델이다.
/// </summary>
public sealed class DepartmentModel
{
    public string DeptId { get; set; } = string.Empty;
    public string DeptName { get; set; } = string.Empty;
}

/// <summary>
/// 연차 신청 목록 모델이다.
/// </summary>
public sealed class LeaveRequestModel
{
    public string RequestId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string LeaveType { get; set; } = "annual";
    public decimal LeaveDays { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Status { get; set; } = "pending";
    public string? Reason { get; set; }
    public string? RejectReason { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 사원 상세 패널 표시 모델이다.
/// </summary>
public sealed class EmployeeDetailModel
{
    public string EmployeeId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string EmpNo { get; set; } = string.Empty;
    public string EmpName { get; set; } = string.Empty;
    public string? DeptId { get; set; }
    public string? DeptName { get; set; }
    public string? Position { get; set; }
    public string? JobTitle { get; set; }
    public string EmpType { get; set; } = "regular";

    /// <summary>주당 소정근로시간(약정). <c>null</c> = 미정. 작(2026-08-13) 단계4.</summary>
    public decimal? WeeklyHours { get; set; }

    /// <summary>고용형태를 사람이 읽는 말로. 모르는 값은 감추지 않고 그대로 보여준다.</summary>
    public string EmpTypeLabel => EmployeeTypeLabels.Of(EmpType);

    public DateTime JoinDate { get; set; }
    public DateTime? ResignDate { get; set; }
    public string? BirthDate { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string Role { get; set; } = "sales_user";
    public bool IsActive { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 연차 신청 생성 요청 모델이다.
/// </summary>
public sealed class CreateLeaveRequestModel
{
    public string EmployeeId { get; set; } = string.Empty;
    public string LeaveType { get; set; } = "annual";
    public decimal LeaveDays { get; set; } = 1.0m;
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime EndDate { get; set; } = DateTime.Today;
    public string? Reason { get; set; }
}

/// <summary>
/// 퇴사 처리 결과 모델이다. 작(2026-08-12) 단계0, 검증팀 P1-5 봉합.
/// </summary>
/// <remarks>
/// 화면이 <b>실제로 일어난 일만</b> 말하게 하려고 결과를 받는다.
/// 앞서는 성공하면 무조건 "로그인 계정도 함께 차단됐습니다" 라고 안내했는데,
/// 계정이 없는 사원(실측 12명 중 11명)에게도 같은 문구가 떴다 — 되는 척이다.
/// </remarks>
public sealed class EmployeeResignResultModel
{
    /// <summary>로그인 계정을 실제로 차단했는가. 계정이 없거나 대표계정이면 false.</summary>
    public bool AccountBlocked { get; set; }
}

/// <summary>
/// 퇴사 처리 사전 점검 결과 모델이다. 작(2026-08-12) 그룹웨어 단계0 P0-B.
/// </summary>
/// <remarks>
/// 퇴사를 <b>막지 않는다.</b> 무슨 일이 벌어지는지 알려주고 사람이 판단한다(반자동 원칙).
/// </remarks>
public sealed class EmployeeResignPrecheckModel
{
    /// <summary>이 사원이 결재자·대결자로 걸려 있는 결재선 수.</summary>
    public int ApprovalLineCount { get; set; }

    /// <summary>이 사원이 올린 진행 중 결재 건수.</summary>
    public int PendingRequestCount { get; set; }

    /// <summary>로그인 계정 보유 여부. 있으면 퇴사 시 함께 차단된다.</summary>
    public bool HasUserAccount { get; set; }

    /// <summary>알릴 것이 있는가.</summary>
    public bool HasWarning => ApprovalLineCount > 0 || PendingRequestCount > 0;
}
