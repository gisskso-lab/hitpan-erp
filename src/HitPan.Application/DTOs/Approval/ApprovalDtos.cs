namespace HitPan.Application.DTOs.Approval;

// ── 결재 설정 ──

/// <summary>결재 설정 조회 DTO</summary>
public class ApprovalSettingDto
{
    public string SettingId { get; set; } = string.Empty;
    public string DocType { get; set; } = string.Empty;
    public string DocTypeLabel { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public decimal ThresholdAmount { get; set; }
    public bool AutoApproveBelow { get; set; }
    public int MaxLines { get; set; }
}

/// <summary>결재 설정 저장 요청</summary>
public class SaveApprovalSettingRequest
{
    public string DocType { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public decimal ThresholdAmount { get; set; }
    public bool AutoApproveBelow { get; set; }
    public int MaxLines { get; set; } = 3;
}

// ── 결재 라인 ──

/// <summary>결재 라인 조회 DTO</summary>
public class ApprovalLineDto
{
    public string LineId { get; set; } = string.Empty;
    public string DocType { get; set; } = string.Empty;
    public int SeqNo { get; set; }
    public string ApproverId { get; set; } = string.Empty;
    public string ApproverName { get; set; } = string.Empty;
    public string? RoleLabel { get; set; }
    public string? DelegateId { get; set; }
    public string? DelegateName { get; set; }
    public DateTime? DelegateStart { get; set; }
    public DateTime? DelegateEnd { get; set; }
}

/// <summary>결재 라인 저장 요청 (문서유형별 일괄 저장)</summary>
public class SaveApprovalLinesRequest
{
    public string DocType { get; set; } = string.Empty;
    public List<ApprovalLineItem> Lines { get; set; } = new();
}

/// <summary>결재 라인 항목</summary>
public class ApprovalLineItem
{
    public int SeqNo { get; set; }
    public string ApproverId { get; set; } = string.Empty;
    public string ApproverName { get; set; } = string.Empty;
    public string? RoleLabel { get; set; }
    public string? DelegateId { get; set; }
    public string? DelegateName { get; set; }
    public DateTime? DelegateStart { get; set; }
    public DateTime? DelegateEnd { get; set; }
}

// ── 결재 문서 ──

/// <summary>결재 문서 목록 DTO</summary>
public class ApprovalDocumentDto
{
    public string ApprovalId { get; set; } = string.Empty;
    public string DocType { get; set; } = string.Empty;
    public string DocTypeLabel { get; set; } = string.Empty;
    public string RefId { get; set; } = string.Empty;
    public string? RefNo { get; set; }
    public string Title { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public int CurrentSeq { get; set; }
    public int TotalLines { get; set; }
    public string RequesterId { get; set; } = string.Empty;
    public string RequesterName { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Memo { get; set; }
    public string? CurrentApproverName { get; set; }
}

/// <summary>결재 요청 생성</summary>
public class CreateApprovalRequest
{
    public string DocType { get; set; } = string.Empty;
    public string RefId { get; set; } = string.Empty;
    public string? RefNo { get; set; }
    public string Title { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Memo { get; set; }
}

/// <summary>결재 처리 (승인/반려)</summary>
public class ProcessApprovalRequest
{
    public string Action { get; set; } = string.Empty;  // approved, rejected
    public string? Comment { get; set; }
}

// ── 결재 이력 ──

/// <summary>결재 이력 DTO</summary>
public class ApprovalHistoryDto
{
    public string HistoryId { get; set; } = string.Empty;
    public int SeqNo { get; set; }
    public string ApproverId { get; set; } = string.Empty;
    public string ApproverName { get; set; } = string.Empty;
    public bool IsDelegated { get; set; }
    public string? OriginalApproverId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ActionLabel { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public DateTime ActedAt { get; set; }
}

/// <summary>결재 상세 (문서 + 이력)</summary>
public class ApprovalDetailDto
{
    public ApprovalDocumentDto Document { get; set; } = new();
    public List<ApprovalHistoryDto> History { get; set; } = new();
    public List<ApprovalLineDto> Lines { get; set; } = new();
}

/// <summary>
/// 결재관리 <b>필터2</b> 콤보 항목 — 문서종류 하나.
/// </summary>
/// <remarks>
/// 작20260824작1 ②. 목록은 <c>ApprovalService.GetFilterDocTypes()</c> 가
/// <b>라벨 사전을 순회해</b> 만든다 — 화면에 손으로 적지 않는다.
/// </remarks>
public class ApprovalDocTypeDto
{
    /// <summary>문서종류 코드(<c>leave</c> 등). 필터 파라미터로 그대로 나간다.</summary>
    public string DocType { get; set; } = string.Empty;

    /// <summary>화면에 보이는 한글 이름(<c>휴가</c>). 🔴 고객 노출 — 영문 코드가 뜨면 안 된다.</summary>
    public string DocTypeName { get; set; } = string.Empty;
}

// ── 결재 처리 결과 (20260826작6 W3) ──

/// <summary>
/// 결재 처리(<c>ProcessAsync</c>) 결과. <b>이 건으로 문서가 최종 승인까지 갔는지</b> 를 알려준다.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>왜 필요한가</b> — 종전엔 컨트롤러가 <c>"승인되었습니다."</c> 한 문장만 돌려줬다.
/// 그런데 결재선이 2단이면 <b>1단 승인은 아직 승인이 아니다</b>. 부장이 눌러도
/// <c>current_seq++</c> 만 되고 문서는 <c>pending</c> 으로 남는다.
/// 화면은 두 경우를 <b>구분할 방법이 없었다</b>.
/// </para>
/// <para>
/// 🔴 급여명세서 발송 팝업(20260826작6 W3)이 이 구분 위에 선다. 구분 없이 붙이면
/// <b>부장이 승인한 순간 "발송하시겠습니까?" 가 뜨고</b>, 대표이사 결재 전에 급여명세서가
/// 나가는 길이 열린다 — 사장님 ⑤결재(<i>"결재 없이는 안 나간다"</i>) 정면 위반이다.
/// </para>
/// <para>
/// ⚠️ <see cref="IsFinalApproved"/> 판정은 <c>leave</c>·<c>absence</c>·<c>overtime</c> 이
/// 이미 쓰는 <c>request.Action=="approved" &amp;&amp; CurrentSeq &gt;= TotalLines</c> 를
/// <b>그대로</b> 쓴다. 새 규칙을 만들지 않는다 — 만들면 두 판정이 갈린다.
/// </para>
/// </remarks>
public class ProcessApprovalResult
{
    /// <summary>이 처리로 문서가 <b>최종 승인</b>까지 갔는가. 중간 단계 승인·반려는 <c>false</c>.</summary>
    public bool IsFinalApproved { get; set; }

    /// <summary>문서 종류(<c>payslip</c> 등). 화면이 후속 동작을 고를 때 쓴다.</summary>
    public string DocType { get; set; } = string.Empty;

    /// <summary>원본 문서 id(급여명세서면 <c>slip_id</c>).</summary>
    public string RefId { get; set; } = string.Empty;
}
