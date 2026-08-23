namespace HitPan.Application.DTOs.Employee;

/// <summary>
/// 사원 목록 조회 응답 DTO이다.
/// </summary>
public sealed class EmployeeListDto
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

    // ── 재직 상태 (작 2026-08-14, 사장님 지시 "퇴사직원 숨김처리") ──
    // 🔴 종전엔 IsActive 하나뿐이라 화면이 **퇴사·휴직·단순비활성을 구분할 수 없었다.**
    //    셋 다 "비활성" 한 마디로 뭉개져, 퇴사자인지 휴직자인지 보고도 알 수 없었다.
    //    ⇒ 사실을 나눠 내려 준다. 어떻게 보여줄지는 화면이 정한다.

    /// <summary>퇴사자인가. 퇴사 처리 시 <c>is_resigned=1</c> 이 함께 기록된다.</summary>
    public bool IsResigned { get; set; }

    /// <summary>퇴사일. 소급 퇴사가 가능하므로 오늘과 다를 수 있다.</summary>
    public DateTime? ResignDate { get; set; }

    /// <summary><c>active</c>(재직) · <c>absence</c>(휴직) · <c>leave</c>(연차) — 재직 중의 세부 상태.</summary>
    public string? WorkStatus { get; set; }

    // 작20260429 연차 관리 (사장님 결재)
    public decimal AnnualLeaveTotal { get; set; }
    public decimal AnnualLeaveUsed { get; set; }
    public decimal AnnualLeaveRemaining => AnnualLeaveTotal - AnnualLeaveUsed;

    // ── 작20260822작1 G1 결재자 후보 판정 (사장님 결재 2026-08-23) ──
    // 결재자 후보 조회(GetApproverCandidatesAsync)에서만 채워진다.
    // 일반 사원 목록에서는 둘 다 false 다 — 그 조회는 이 값을 안 읽는다.

    /// <summary>
    /// 부모계정(대표이사)인가. <c>users.is_parent = 1</c> 로 판정한다.
    /// 🔴 <c>position</c> 으로 판정하면 안 된다 — T-004 실측에서 대표인데도 <c>null</c> 이었다.
    /// </summary>
    public bool IsParentAccount { get; set; }

    /// <summary>
    /// APPROVAL 권한(<c>can_view</c>)을 실제로 가졌는가.
    /// 🔴 부모계정은 이 값이 <c>false</c> 여도 결재할 수 있다 —
    ///    권한검사가 user_permissions 를 보기 전에 통과시키기 때문이다.
    ///    그래서 "결재 가능" 을 이 값 하나로 판정하면 안 된다.
    /// </summary>
    public bool HasApprovalPermission { get; set; }
}

/// <summary>
/// 사원 상세 조회 응답 DTO이다.
/// </summary>
public sealed class EmployeeDetailDto
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

    /// <summary>
    /// 주당 소정근로시간(약정). <c>null</c> = 미정. 작(2026-08-13) 단계4.
    /// </summary>
    /// <remarks>
    /// 🔴 연차·주휴·4대보험이 이 숫자로 갈린다(주 15시간이 갈림길).
    /// ⚠️ 기본값을 40 으로 두지 않는다 — 모르는 것을 채우면 그 값으로 연차가 계산돼
    /// 법정 미달이 될 수 있다(반자동 원칙).
    /// </remarks>
    public decimal? WeeklyHours { get; set; }
    public DateTime JoinDate { get; set; }
    public DateTime? ResignDate { get; set; }
    public string? BirthDate { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string Role { get; set; } = "sales_user";
    public bool IsActive { get; set; }
    public decimal AnnualLeaveTotal { get; set; }
    public decimal AnnualLeaveUsed { get; set; }
    public decimal AnnualLeaveRemaining => AnnualLeaveTotal - AnnualLeaveUsed;
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 작20260429 연차 저장 요청 (사장님 결재).
/// 사원관리 그리드에서 부여·사용 일수만 단독 저장한다.
/// </summary>
public sealed class UpdateAnnualLeaveRequest
{
    public decimal AnnualLeaveTotal { get; set; }
    public decimal AnnualLeaveUsed { get; set; }
}

/// <summary>
/// 봉합 (2026-06-22, 10차 P1-1): 부서 드롭다운 응답 DTO이다.
/// 사원 부서는 departments 마스터의 dept_id 로 저장되므로,
/// 화면 부서 선택은 (DeptId, DeptName) 목록으로 채운다.
/// </summary>
public sealed class DepartmentDto
{
    public string DeptId { get; set; } = string.Empty;
    public string DeptName { get; set; } = string.Empty;
}

/// <summary>
/// 사원 생성 요청 DTO이다.
/// </summary>
public sealed class CreateEmployeeRequest
{
    public string EmpName { get; set; } = string.Empty;
    public string? DeptId { get; set; }

    /// <summary>
    /// 부서를 <b>이름으로</b> 지정한다. 작(2026-08-13) — 사장님 지시:
    /// <i>"부서를 설정하면 자동으로 그 부서로 묶으면 되는거니"</i>.
    /// </summary>
    /// <remarks>
    /// <see cref="DeptId"/> 가 있으면 그쪽이 이긴다(목록에서 고른 경우).
    /// 비어 있고 이 이름만 오면 서버가 <b>같은 이름을 찾고, 없으면 만든다.</b>
    /// 🔴 표(<c>departments</c>)는 그대로다 — 채우는 방법만 바뀐 것이다.
    /// 메신저 부서방이 그 위에 서므로 표를 없애면 안 된다(사장님 지시 5).
    /// </remarks>
    public string? DeptName { get; set; }

    public string? Position { get; set; }
    public string? JobTitle { get; set; }
    public string EmpType { get; set; } = "regular";

    /// <summary>
    /// 주당 소정근로시간(약정). <c>null</c> = 미정. 작(2026-08-13) 단계4.
    /// </summary>
    /// <remarks>
    /// 🔴 연차·주휴·4대보험이 이 숫자로 갈린다(주 15시간이 갈림길).
    /// ⚠️ 기본값을 40 으로 두지 않는다 — 모르는 것을 채우면 그 값으로 연차가 계산돼
    /// 법정 미달이 될 수 있다(반자동 원칙).
    /// </remarks>
    public decimal? WeeklyHours { get; set; }
    public DateTime JoinDate { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string Role { get; set; } = "sales_user";
}

/// <summary>
/// 사원 수정 요청 DTO이다.
/// </summary>
public sealed class UpdateEmployeeRequest
{
    public string EmpName { get; set; } = string.Empty;
    public string? DeptId { get; set; }

    /// <summary>
    /// 부서를 <b>이름으로</b> 지정한다. 규칙은 <see cref="CreateEmployeeRequest.DeptName"/> 과 같다.
    /// </summary>
    public string? DeptName { get; set; }

    public string? Position { get; set; }
    public string? JobTitle { get; set; }
    public string EmpType { get; set; } = "regular";

    /// <summary>
    /// 주당 소정근로시간(약정). <c>null</c> = 미정. 작(2026-08-13) 단계4.
    /// </summary>
    /// <remarks>
    /// 🔴 연차·주휴·4대보험이 이 숫자로 갈린다(주 15시간이 갈림길).
    /// ⚠️ 기본값을 40 으로 두지 않는다 — 모르는 것을 채우면 그 값으로 연차가 계산돼
    /// 법정 미달이 될 수 있다(반자동 원칙).
    /// </remarks>
    public decimal? WeeklyHours { get; set; }
    public DateTime JoinDate { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string Role { get; set; } = "sales_user";
}

/// <summary>
/// 퇴사 처리 요청 DTO이다. 작(2026-08-12) 그룹웨어 단계0 P0-C.
/// </summary>
/// <remarks>
/// 반자동 원칙(사장님 2026-08-12 "히트판은 100% 자동화는 없어") — 퇴사일을 시스템이
/// 단정하지 않고 <b>사람에게 받는다</b>. 봉합 전에는 <c>NOW()</c> 를 넣어 소급 퇴사 처리가
/// 불가능했다. 실제 퇴사일과 처리한 날은 다를 수 있다.
/// </remarks>
public sealed class ResignEmployeeRequest
{
    /// <summary>실제 퇴사일. 미지정이면 오늘로 본다(기존 동작 보존).</summary>
    public DateTime? ResignDate { get; set; }

    /// <summary>퇴사 사유. 컬럼(<c>resign_reason</c>)은 원래 있었으나 ERP 가 채운 적이 없다.</summary>
    public string? ResignReason { get; set; }
}

/// <summary>
/// 퇴사 처리 사전 점검 결과이다. 작(2026-08-12) 그룹웨어 단계0 P0-B.
/// </summary>
/// <remarks>
/// 퇴사를 <b>막지 않는다.</b> 무슨 일이 벌어지는지 알려주고 사람이 판단한다(반자동 원칙).
/// 결재선이 사람 기반이라 결재자가 퇴사하면 그 차례에서 결재가 멈춘다(헌법 #20).
/// 결재선 직급 정본화는 별도 차수 — 지금은 경고까지 한다.
/// </remarks>
public sealed class EmployeeResignPrecheckDto
{
    /// <summary>이 사원이 결재자·대결자로 걸려 있는 결재선 수. 0보다 크면 결재가 멈출 수 있다.</summary>
    public int ApprovalLineCount { get; set; }

    /// <summary>이 사원이 올린 진행 중(pending) 결재 건수.</summary>
    public int PendingRequestCount { get; set; }

    /// <summary>로그인 계정 보유 여부. 있으면 퇴사 처리 시 함께 차단된다.</summary>
    public bool HasUserAccount { get; set; }

    /// <summary>알릴 것이 있는가.</summary>
    public bool HasWarning => ApprovalLineCount > 0 || PendingRequestCount > 0;
}
