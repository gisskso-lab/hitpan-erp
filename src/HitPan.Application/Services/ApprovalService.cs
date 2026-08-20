using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Approval;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>결재 서비스 — 설정·라인·문서·이력 CRUD</summary>
public class ApprovalService : IApprovalService
{
    private readonly IDbConnection _db;

    // 문서유형 라벨 매핑
    private static readonly Dictionary<string, string> DocTypeLabels = new()
    {
        ["quotation"]        = "견적서",
        ["sales_order"]      = "수주서",
        ["delivery"]         = "거래명세서",
        ["purchase_order"]   = "발주서",
        ["receipt"]          = "매입명세서",
        // 14차 P2 봉합: 종전 "return" 단일 라벨은 결재 트리거가 쓰는 docType(sales_return·purchase_return)과
        //   불일치해, 결재선 설정 화면에서 켜도 트리거 조회(doc_type=@DocType)가 lineCount=0 → 반품 결재가
        //   영영 미생성됐다. 트리거 docType 과 1:1 정합하도록 매출·매입반품으로 분리한다.
        ["sales_return"]     = "매출반품",
        ["purchase_return"]  = "매입반품",
        ["expense"]          = "경비",
        ["leave"]            = "휴가",
        ["overtime"]         = "초과근무",
        // 작(2026-08-13) 단계3: 업무보고서 4종(사장님 지시 2026-08-12).
        // 🔴 라벨을 빠뜨리면 결재함에 "report_daily" 같은 영문 코드가 그대로 뜬다
        //    (고객 노출 영역 개발용어 금지). MapLabels 가 GetValueOrDefault(docType) 로
        //    폴백하기 때문에 500 이 안 나고 조용히 영문이 보인다.
        ["report_daily"]     = "일일보고서",
        ["report_weekly"]    = "주간보고서",
        ["report_monthly"]   = "월간보고서",
        ["report_incident"]  = "경위서",
        // 작(2026-08-21) P0 봉합: 휴직(absence) 이 여기 없어서 결재가 구조적으로 불가능했다.
        // 🔴 AbsenceService 는 처음부터 "absence" 로 상신을 불렀는데, 설정 행은 이 사전을
        //    순회해(GetSettingsAsync) 만들어지므로 화면에 "휴직" 행이 아예 뜨지 않았다
        //    → is_enabled 를 켤 방법이 없다 → TryCreateApprovalAsync 가
        //    'if (!setting.IsEnabled) return;' 로 조용히 종료했다.
        //    화면은 "결재상신" 버튼을 보여주는데 눌러도 아무 일이 없었다.
        // ⚠️ 이 항목을 지우면 휴직 결재가 다시 죽는다. 가드시험이 지킨다
        //    (ApprovalDocTypeWiringGuardTests.휴직_상신_docType_이_설정화면_목록에_있다).
        ["absence"]          = "휴직"
    };

    /// <summary>
    /// 업무보고서 결재 문서유형 접두. <c>ReportTypes.DocTypePrefix</c> 와 같은 값이다.
    /// </summary>
    /// <remarks>
    /// ⚠️ Application 계층 안이라 DTO 를 참조할 수 있지만, 이 클래스는 DTO 의존을 최소로 두고 있어
    /// 문자열만 둔다. <b>둘 중 하나를 바꾸면 다른 쪽도 바꿔야 한다</b>(게이트: ReportGuardTests).
    /// </remarks>
    private const string ReportDocTypePrefix = "report_";

    /// <summary>
    /// 원본 표(<c>leave_requests</c>·<c>hr_reports</c>)의 <c>reject_reason</c> 컬럼 폭.
    /// </summary>
    private const int RejectReasonMaxLength = 200;

    /// <summary>
    /// 휴직 승인 시 사원에게 넣는 <c>work_status</c> 값.
    /// </summary>
    /// <remarks>
    /// 🔴 <c>WorkStatusLabels.Absence</c> 와 <b>같은 값이어야 한다.</b> 문자열을 여기서 지어내면
    /// 휴직 화면 경로와 결재 경로가 서로 다른 값을 넣어 조직도·급여 판정이 갈린다.
    /// (게이트: ApprovalDocTypeWiringGuardTests.휴직_결재승인은_원본과_사원상태를_함께_반영한다)
    /// </remarks>
    private const string AbsenceWorkStatus = DTOs.Leave.WorkStatusLabels.Absence;

    /// <summary>
    /// 휴직 결재 승인 시 원본에서 읽어야 하는 값. 시작일로 <c>active</c>/<c>approved</c> 를 가른다.
    /// </summary>
    private sealed class AbsenceSyncRow
    {
        public string EmployeeId { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
    }

    /// <summary>
    /// 반려 사유를 원본 표 컬럼 폭에 맞게 자른다.
    /// </summary>
    /// <remarks>
    /// 🔴 봉합 (2026-08-13, 단계3 검증 P0-2): 결재 의견은 <c>approval_history.comment</c> 가
    /// <b>varchar(500)</b> 인데 원본 표의 <c>reject_reason</c> 은 <b>varchar(200)</b> 이다.
    /// 결재함 입력칸은 자유서술(<c>Lines="3"</c>)이라 201자가 흔히 들어오고, MariaDB 가
    /// <c>STRICT_TRANS_TABLES</c> 라 <c>ERROR 1406 Data too long</c> 이 난다. 이 UPDATE 는
    /// 결재 트랜잭션 <b>안</b>에 있어서 예외가 나면 결재 이력·상태 전이까지 <b>전부 롤백</b>된다
    /// ⇒ <b>사유를 길게 쓸수록 반려를 못 하는</b> 상태였다(실측 재현: ERROR 1406).
    ///
    /// 사유 전문은 <c>approval_history.comment</c> 에 500자 그대로 남으므로 잃는 것이 없다.
    /// 여기 값은 작성자 화면에 바로 보여주기 위한 사본이다.
    ///
    /// ⚠️ 자르는 기준은 <b>글자 수</b>다 — <c>varchar(200)</c> 은 바이트가 아니라 문자 200개이고
    /// (실측: 한글 200자 = CHAR_LENGTH 200 / LENGTH 600) utf8mb4 에서 안전하다.
    /// 다만 이모지 등 서로게이트 쌍은 <c>string</c> 인덱스가 반쪽을 자를 수 있어
    /// <see cref="System.Globalization.StringInfo"/> 로 문자 단위를 지킨다.
    ///
    /// 🔴 연차(<c>leave</c>)도 같은 결함을 갖고 있었다. 내가 만든 보고서 배선이 그것을
    /// 복제한 것이라, 새 자리만 고치지 않고 <b>두 자리를 같은 헬퍼로</b> 봉합한다.
    /// </remarks>
    internal static string? TruncateRejectReason(string? reason)
    {
        if (string.IsNullOrEmpty(reason))
        {
            return reason;
        }

        var si = new System.Globalization.StringInfo(reason);
        return si.LengthInTextElements <= RejectReasonMaxLength
            ? reason
            : si.SubstringByTextElements(0, RejectReasonMaxLength);
    }

    // 상태 라벨 매핑
    private static readonly Dictionary<string, string> StatusLabels = new()
    {
        ["pending"]   = "대기",
        ["approved"]  = "승인",
        ["rejected"]  = "반려",
        ["cancelled"] = "취소"
    };

    // 결재 액션 라벨
    private static readonly Dictionary<string, string> ActionLabels = new()
    {
        ["approved"] = "승인",
        ["rejected"] = "반려"
    };

    // 수금/지급 수단 라벨
    public static readonly Dictionary<string, string> MethodLabels = new()
    {
        ["cash"]          = "현금",
        ["bank_transfer"] = "계좌이체",
        ["check"]         = "수표",
        ["card"]          = "카드",
        ["note"]          = "어음"
    };

    private readonly IAuditService _audit;

    // 작(2026-08-13) 단계2, 검증팀 P0-1 봉합: 결재 알림.
    // 🔴 처음에는 자동 트리거(ApprovalTriggerHelper)에만 알림을 붙였다. 그런데 결재가 도는 경로는
    //    셋이다 — ①자동 트리거 ②수동 상신(CreateApprovalAsync) ③승인 후 다음 결재자(ProcessAsync).
    //    ②③이 비어 있으면 1단계 결재자만 알림을 받고 2·3단계는 아무 신호도 못 받는다.
    //    사이드바 뱃지가 최초 1회 조회뿐이라(이 작업의 출발점) 새로고침 전엔 아무도 모른다
    //    ⇒ 결재선 2단 이상 고객사에서 첫 단계 이후 결재가 조용히 멈춘다(헌법 #20).
    private readonly INotificationService? _notifier;

    public ApprovalService(IDbConnection db, IAuditService audit, INotificationService? notifier = null)
    {
        _db = db;
        _audit = audit;
        _notifier = notifier;
    }

    // ═══════════════════════════════════════════
    // 결재 설정
    // ═══════════════════════════════════════════

    public async Task<List<ApprovalSettingDto>> GetSettingsAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        // 모든 문서유형에 대해 설정 반환 (없으면 기본값)
        var existing = (await _db.QueryAsync<ApprovalSettingDto>(new CommandDefinition(
            """
            SELECT setting_id AS SettingId, doc_type AS DocType,
                   is_enabled AS IsEnabled, threshold_amount AS ThresholdAmount,
                   auto_approve_below AS AutoApproveBelow, max_lines AS MaxLines
            FROM approval_settings
            WHERE tenant_id = @TenantId
            """,
            new { TenantId = tenantId }, cancellationToken: ct))).ToList();

        var result = new List<ApprovalSettingDto>();
        foreach (var (docType, label) in DocTypeLabels)
        {
            var s = existing.FirstOrDefault(x => x.DocType == docType);
            result.Add(new ApprovalSettingDto
            {
                SettingId = s?.SettingId ?? string.Empty,
                DocType = docType,
                DocTypeLabel = label,
                IsEnabled = s?.IsEnabled ?? false,
                ThresholdAmount = s?.ThresholdAmount ?? 0,
                AutoApproveBelow = s?.AutoApproveBelow ?? false,
                MaxLines = s?.MaxLines ?? 3
            });
        }
        return result;
    }

    public async Task SaveSettingAsync(SaveApprovalSettingRequest request, string tenantId, string userId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        // UPSERT — 있으면 수정, 없으면 생성
        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO approval_settings
              (setting_id, tenant_id, doc_type, is_enabled, threshold_amount, auto_approve_below, max_lines, created_by, updated_by)
            VALUES
              (UUID(), @TenantId, @DocType, @IsEnabled, @ThresholdAmount, @AutoApproveBelow, @MaxLines, @UserId, @UserId)
            ON DUPLICATE KEY UPDATE
              is_enabled = @IsEnabled,
              threshold_amount = @ThresholdAmount,
              auto_approve_below = @AutoApproveBelow,
              max_lines = @MaxLines,
              updated_by = @UserId,
              updated_at = NOW(6)
            """,
            new
            {
                TenantId = tenantId,
                request.DocType,
                IsEnabled = request.IsEnabled ? 1 : 0,
                request.ThresholdAmount,
                AutoApproveBelow = request.AutoApproveBelow ? 1 : 0,
                request.MaxLines,
                UserId = userId
            }, cancellationToken: ct));
    }

    // ═══════════════════════════════════════════
    // 결재 라인
    // ═══════════════════════════════════════════

    public async Task<List<ApprovalLineDto>> GetLinesAsync(string tenantId, string docType, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var rows = await _db.QueryAsync<ApprovalLineDto>(new CommandDefinition(
            """
            SELECT line_id AS LineId, doc_type AS DocType, seq_no AS SeqNo,
                   approver_id AS ApproverId, approver_name AS ApproverName,
                   role_label AS RoleLabel, delegate_id AS DelegateId,
                   delegate_name AS DelegateName, delegate_start AS DelegateStart,
                   delegate_end AS DelegateEnd
            FROM approval_doc_lines
            WHERE tenant_id = @TenantId AND doc_type = @DocType AND is_active = 1
            ORDER BY seq_no
            """,
            new { TenantId = tenantId, DocType = docType }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task SaveLinesAsync(SaveApprovalLinesRequest request, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        // P1-7 봉합(2026-06-20): 결재자 유효성 검증. 종전엔 존재하지 않거나 퇴직한 사원도
        // 결재자/위임자로 설정 가능해, 결재 진행 시 "현재 결재자 없음"으로 막혔다. 저장 전에
        // 모든 결재자·위임자 ID 가 활성 사원인지 확인한다.
        var ids = request.Lines
            .SelectMany(l => new[] { l.ApproverId, l.DelegateId })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct()
            .ToList();

        if (ids.Count > 0)
        {
            var validIds = (await _db.QueryAsync<string>(new CommandDefinition(
                "SELECT employee_id FROM employees WHERE tenant_id = @TenantId AND is_active = 1 AND employee_id IN @Ids",
                new { TenantId = tenantId, Ids = ids }, cancellationToken: ct))).ToHashSet();

            var invalid = ids.Where(id => !validIds.Contains(id)).ToList();
            if (invalid.Count > 0)
                throw new InvalidOperationException(
                    $"결재자/위임자로 지정할 수 없는 사원이 있습니다(미존재 또는 퇴직): {string.Join(", ", invalid)}");
        }

        // 봉합 (2026-06-23, 5차 전수조사 APPR-F2 P1, 사장님 결재 B안):
        //   결재라인(approval_doc_lines)은 doc_type 단위 전역 설정인데, ProcessAsync 는 결재 권한을
        //   이 전역 테이블에서 doc_type+seq_no 로 실시간 조회한다. 따라서 진행 중(pending) 결재가 있는
        //   상태에서 라인을 다시 저장하면 ① 진행 중 문서의 결재자가 소급 변경(원 결재자 결재 불가)되거나
        //   ② 라인 개수를 줄이면 current_seq 가 새 라인 범위를 벗어나 영구 결재 불가(헌법 #20 워크플로우 끊김)가 된다.
        //   B안(즉시 안전 가드): 해당 doc_type 에 진행 중 결재가 1건이라도 있으면 라인 변경을 차단한다.
        //   DDL 무변경. 근본 해결(문서 생성 시 결재라인 스냅샷 동결 = A안)은 출하 DDL 변경·ALTER 마이그 동반이라
        //   헌법 #34에 따라 정식 로드맵으로 분리. (설계팀장 승인·검증팀장 PASS)
        var pendingCount = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM approval_documents WHERE tenant_id = @TenantId AND doc_type = @DocType AND status = 'pending'",
            new { TenantId = tenantId, DocType = request.DocType }, cancellationToken: ct));
        if (pendingCount > 0)
            throw new InvalidOperationException(
                $"진행 중인 결재가 {pendingCount}건 있어 결재선을 변경할 수 없습니다. 모든 결재가 완료·반려된 뒤 변경하세요.");

        // 라인 비활성화 + 신규 추가를 한 트랜잭션으로 묶어, 중간 실패 시 기존 라인이 사라지지 않게 한다.
        using var tx = _db.BeginTransaction();
        try
        {
            await _db.ExecuteAsync(new CommandDefinition(
                "UPDATE approval_doc_lines SET is_active = 0 WHERE tenant_id = @TenantId AND doc_type = @DocType",
                new { TenantId = tenantId, DocType = request.DocType }, transaction: tx, cancellationToken: ct));

            foreach (var line in request.Lines)
            {
                await _db.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO approval_doc_lines
                      (line_id, tenant_id, doc_type, seq_no, approver_id, approver_name,
                       role_label, delegate_id, delegate_name, delegate_start, delegate_end, is_active)
                    VALUES
                      (UUID(), @TenantId, @DocType, @SeqNo, @ApproverId, @ApproverName,
                       @RoleLabel, @DelegateId, @DelegateName, @DelegateStart, @DelegateEnd, 1)
                    """,
                    new
                    {
                        TenantId = tenantId,
                        DocType = request.DocType,
                        line.SeqNo,
                        line.ApproverId,
                        line.ApproverName,
                        line.RoleLabel,
                        line.DelegateId,
                        line.DelegateName,
                        line.DelegateStart,
                        line.DelegateEnd
                    }, transaction: tx, cancellationToken: ct));
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ═══════════════════════════════════════════
    // 결재 문서
    // ═══════════════════════════════════════════

    public async Task<string> CreateApprovalAsync(CreateApprovalRequest request, string tenantId, string userId, string userName, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        // 결재라인 조회
        var lines = await GetLinesAsync(tenantId, request.DocType, ct);
        if (lines.Count == 0)
            throw new InvalidOperationException("결재 라인이 설정되지 않았습니다. 결재 설정에서 라인을 먼저 구성해주세요.");

        // 기준금액 자동승인 체크
        var setting = await _db.QueryFirstOrDefaultAsync<ApprovalSettingDto>(new CommandDefinition(
            "SELECT threshold_amount AS ThresholdAmount, auto_approve_below AS AutoApproveBelow FROM approval_settings WHERE tenant_id = @TenantId AND doc_type = @DocType",
            new { TenantId = tenantId, DocType = request.DocType }, cancellationToken: ct));

        var approvalId = Guid.NewGuid().ToString();
        var status = "pending";

        // 기준금액 미만 자동승인 처리
        if (setting is { AutoApproveBelow: true, ThresholdAmount: > 0 } && request.Amount < setting.ThresholdAmount)
        {
            status = "approved";
        }

        // 봉합 (2026-06-23, 5차 전수조사 APPR-F1 P1):
        //   종전엔 approval_documents INSERT 와 자동승인 approval_history INSERT 가 트랜잭션 없이 분리돼,
        //   문서가 status='approved' 로 커밋된 뒤 이력 INSERT 가 실패하면(연결 끊김·순간 장애) "승인됐다는
        //   이력이 없는 승인 문서"가 남아 감사 추적·결재 무결성이 깨졌다(헌법 #24 책임 추적). 같은 클래스
        //   SaveLinesAsync(178)·ProcessAsync(456)는 이미 트랜잭션으로 묶여 있는데 자동승인 경로만 누락.
        //   두 INSERT 를 한 트랜잭션으로 묶어 원자화한다(SaveLinesAsync 의 검증된 패턴 복제).
        //   pending 경로는 INSERT 1건만 커밋되어 동작 동일 — 회귀 없음. (검증팀장 PASS·설계팀장 승인)
        using var tx = _db.BeginTransaction();
        try
        {
            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO approval_documents
                  (approval_id, tenant_id, doc_type, ref_id, ref_no, title, amount,
                   status, current_seq, total_lines, requester_id, requester_name, memo, created_by)
                VALUES
                  (@ApprovalId, @TenantId, @DocType, @RefId, @RefNo, @Title, @Amount,
                   @Status, 1, @TotalLines, @RequesterId, @RequesterName, @Memo, @UserId)
                """,
                new
                {
                    ApprovalId = approvalId,
                    TenantId = tenantId,
                    request.DocType,
                    request.RefId,
                    request.RefNo,
                    request.Title,
                    request.Amount,
                    Status = status,
                    TotalLines = lines.Count,
                    RequesterId = userId,
                    RequesterName = userName,
                    request.Memo,
                    UserId = userId
                }, transaction: tx, cancellationToken: ct));

            // 자동승인인 경우 이력 기록 (동일 트랜잭션 — 문서·이력 원자화)
            if (status == "approved")
            {
                await _db.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO approval_history
                      (history_id, tenant_id, approval_id, seq_no, approver_id, approver_name, action, comment, acted_at)
                    VALUES
                      (UUID(), @TenantId, @ApprovalId, 0, 'system', '시스템', 'approved', '기준금액 미만 자동승인', NOW(6))
                    """,
                    new { TenantId = tenantId, ApprovalId = approvalId }, transaction: tx, cancellationToken: ct));
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        // 작(2026-08-13) 검증팀 P0-1 봉합: 수동 상신 경로에도 알림을 붙인다.
        // 🔴 자동승인된 건은 보내지 않는다 — 결재할 사람이 없는데 "결재가 올라왔습니다" 가 뜨면
        //    눌러 들어가도 대기함이 비어 있어 고객이 헛걸음한다.
        // 알림 실패가 이미 커밋된 결재를 되돌리면 안 되므로 트랜잭션 밖에서 부른다.
        if (_notifier is not null && status == "pending")
        {
            await ApprovalTriggerHelper.NotifyApproverAsync(
                _db, request.DocType, tenantId, request.Title, seqNo: 1, _notifier, ct)
                .ConfigureAwait(false);
        }

        return approvalId;
    }

    public async Task<List<ApprovalDocumentDto>> GetPendingAsync(string tenantId, string employeeId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        // 현재 결재 순서의 결재자가 나인 문서 (위임결재 포함)
        var rows = await _db.QueryAsync<ApprovalDocumentDto>(new CommandDefinition(
            """
            SELECT ad.approval_id AS ApprovalId, ad.doc_type AS DocType, ad.ref_id AS RefId,
                   ad.ref_no AS RefNo, ad.title AS Title, ad.amount AS Amount,
                   ad.status AS Status, ad.current_seq AS CurrentSeq, ad.total_lines AS TotalLines,
                   ad.requester_id AS RequesterId, ad.requester_name AS RequesterName,
                   ad.requested_at AS RequestedAt, ad.memo AS Memo,
                   al.approver_name AS CurrentApproverName
            FROM approval_documents ad
            INNER JOIN approval_doc_lines al
              ON al.tenant_id = ad.tenant_id
              AND al.doc_type = ad.doc_type
              AND al.seq_no = ad.current_seq
              AND al.is_active = 1
            WHERE ad.tenant_id = @TenantId
              AND ad.status = 'pending'
              AND (al.approver_id = @EmployeeId
                   OR (al.delegate_id = @EmployeeId
                       AND CURDATE() BETWEEN al.delegate_start AND al.delegate_end))
            ORDER BY ad.requested_at DESC
            """,
            new { TenantId = tenantId, EmployeeId = employeeId }, cancellationToken: ct));

        return MapLabels(rows);
    }

    public async Task<List<ApprovalDocumentDto>> GetSentAsync(string tenantId, string employeeId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        // 내가 기안한 결재 문서
        var rows = await _db.QueryAsync<ApprovalDocumentDto>(new CommandDefinition(
            """
            SELECT approval_id AS ApprovalId, doc_type AS DocType, ref_id AS RefId,
                   ref_no AS RefNo, title AS Title, amount AS Amount,
                   status AS Status, current_seq AS CurrentSeq, total_lines AS TotalLines,
                   requester_id AS RequesterId, requester_name AS RequesterName,
                   requested_at AS RequestedAt, completed_at AS CompletedAt, memo AS Memo
            FROM approval_documents
            WHERE tenant_id = @TenantId AND requester_id = @EmployeeId
            ORDER BY requested_at DESC
            """,
            new { TenantId = tenantId, EmployeeId = employeeId }, cancellationToken: ct));

        return MapLabels(rows);
    }

    public async Task<List<ApprovalDocumentDto>> GetCompletedAsync(string tenantId, string employeeId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        // 내가 결재한 문서 (이력에 내 기록이 있는 것)
        var rows = await _db.QueryAsync<ApprovalDocumentDto>(new CommandDefinition(
            """
            SELECT DISTINCT ad.approval_id AS ApprovalId, ad.doc_type AS DocType, ad.ref_id AS RefId,
                   ad.ref_no AS RefNo, ad.title AS Title, ad.amount AS Amount,
                   ad.status AS Status, ad.current_seq AS CurrentSeq, ad.total_lines AS TotalLines,
                   ad.requester_id AS RequesterId, ad.requester_name AS RequesterName,
                   ad.requested_at AS RequestedAt, ad.completed_at AS CompletedAt, ad.memo AS Memo
            FROM approval_documents ad
            INNER JOIN approval_history ah
              ON ah.approval_id = ad.approval_id AND ah.tenant_id = ad.tenant_id
            WHERE ad.tenant_id = @TenantId AND ah.approver_id = @EmployeeId
            ORDER BY ad.requested_at DESC
            """,
            new { TenantId = tenantId, EmployeeId = employeeId }, cancellationToken: ct));

        return MapLabels(rows);
    }

    public async Task<ApprovalDetailDto?> GetDetailAsync(string approvalId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        var doc = await _db.QueryFirstOrDefaultAsync<ApprovalDocumentDto>(new CommandDefinition(
            """
            SELECT approval_id AS ApprovalId, doc_type AS DocType, ref_id AS RefId,
                   ref_no AS RefNo, title AS Title, amount AS Amount,
                   status AS Status, current_seq AS CurrentSeq, total_lines AS TotalLines,
                   requester_id AS RequesterId, requester_name AS RequesterName,
                   requested_at AS RequestedAt, completed_at AS CompletedAt, memo AS Memo
            FROM approval_documents
            WHERE approval_id = @ApprovalId AND tenant_id = @TenantId
            """,
            new { ApprovalId = approvalId, TenantId = tenantId }, cancellationToken: ct));

        if (doc is null) return null;

        doc.DocTypeLabel = DocTypeLabels.GetValueOrDefault(doc.DocType, doc.DocType);
        doc.StatusLabel = StatusLabels.GetValueOrDefault(doc.Status, doc.Status);

        // 결재 이력
        var history = (await _db.QueryAsync<ApprovalHistoryDto>(new CommandDefinition(
            """
            SELECT history_id AS HistoryId, seq_no AS SeqNo, approver_id AS ApproverId,
                   approver_name AS ApproverName, is_delegated AS IsDelegated,
                   original_approver_id AS OriginalApproverId,
                   action AS Action, comment AS Comment, acted_at AS ActedAt
            FROM approval_history
            WHERE approval_id = @ApprovalId AND tenant_id = @TenantId
            -- P1-6 봉합(2026-06-20): 종전 'ORDER BY seq_no'는 기준금액 자동승인 이력(seq_no=0)을
            -- 맨 앞으로 올려 실제 처리 순서와 어긋났다. 이력은 시간순이 정본이므로 acted_at 우선.
            ORDER BY acted_at, seq_no
            """,
            new { ApprovalId = approvalId, TenantId = tenantId }, cancellationToken: ct))).ToList();

        foreach (var h in history)
            h.ActionLabel = ActionLabels.GetValueOrDefault(h.Action, h.Action);

        // 결재 라인
        var lines = await GetLinesAsync(tenantId, doc.DocType, ct);

        return new ApprovalDetailDto { Document = doc, History = history, Lines = lines };
    }

    public async Task ProcessAsync(string approvalId, ProcessApprovalRequest request, string tenantId,
        string employeeId, string employeeName, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        // 현재 문서 조회 (RefId 추가 — NEW-A1: doc_type='leave' 최종처리 시 원본 leave_requests 동기화용)
        // 작(2026-08-13) 단계2: Title 추가 — 다음 결재자 알림 본문에 "무엇에 대한 결재인가" 를 담는다.
        var doc = await _db.QueryFirstOrDefaultAsync<(string Status, int CurrentSeq, int TotalLines, string DocType, string RefId, string Title)>(
            new CommandDefinition(
                "SELECT status AS Status, current_seq AS CurrentSeq, total_lines AS TotalLines, doc_type AS DocType, ref_id AS RefId, title AS Title FROM approval_documents WHERE approval_id = @ApprovalId AND tenant_id = @TenantId",
                new { ApprovalId = approvalId, TenantId = tenantId }, cancellationToken: ct));

        // 봉합 (2026-06-20, APPR-02): 문서 미존재(QueryFirstOrDefault → default 튜플, Status=null)를
        //   "이미 처리됨"으로 뭉뚱그리지 않고 분리 안내(CS 추적성). 그 다음에 상태 가드.
        if (doc.Status is null)
            throw new InvalidOperationException("결재 문서를 찾을 수 없습니다.");
        if (doc.Status != "pending")
            throw new InvalidOperationException("이미 처리된 결재입니다.");

        // 결재 권한 확인 (현재 순서의 결재자 또는 위임자인지)
        // 봉합 (2026-06-23, 19차 P2 위임날짜 시간원 불일치): 종전엔 위임 유효기간을 C# DateTime.Today
        //   로 판정했는데, 목록 조회(GetPendingAsync)는 SQL CURDATE() 로 판정해 두 시간원이 갈렸다.
        //   자정 경계 + 위임 시작/종료 당일에 DB와 .NET 호스트 날짜가 어긋나면 "목록엔 위임 결재가
        //   떴는데 누르면 결재 권한이 없습니다"(또는 그 반대)로 워크플로우가 끊긴다(헌법 #20). 위임
        //   유효 판정을 SQL CURDATE() BETWEEN 으로 옮겨 GetPendingAsync 와 단일 시간원(DB)으로 통일한다.
        var line = await _db.QueryFirstOrDefaultAsync<(string ApproverId, string? DelegateId, bool DelegateActive)>(
            new CommandDefinition(
                """
                SELECT approver_id AS ApproverId, delegate_id AS DelegateId,
                       (delegate_id IS NOT NULL
                        AND delegate_start IS NOT NULL AND delegate_end IS NOT NULL
                        AND CURDATE() BETWEEN delegate_start AND delegate_end) AS DelegateActive
                FROM approval_doc_lines
                WHERE tenant_id = @TenantId AND doc_type = @DocType AND seq_no = @SeqNo AND is_active = 1
                """,
                new { TenantId = tenantId, DocType = doc.DocType, SeqNo = doc.CurrentSeq }, cancellationToken: ct));

        var isDelegated = false;
        string? originalApproverId = null;

        if (line.ApproverId == employeeId)
        {
            // 정상 결재자
        }
        else if (line.DelegateId == employeeId && line.DelegateActive)
        {
            // 위임결재자 (위임 유효기간을 SQL CURDATE() 로 판정 — 목록 조회와 동일 시간원)
            isDelegated = true;
            originalApproverId = line.ApproverId;
        }
        else
        {
            throw new InvalidOperationException("결재 권한이 없습니다.");
        }

        // 트랜잭션으로 이력 INSERT + 상태 UPDATE 원자적 처리
        using var tx = _db.BeginTransaction();
        try
        {
            // 결재 이력 INSERT (INSERT ONLY 원칙)
            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO approval_history
                  (history_id, tenant_id, approval_id, seq_no, approver_id, approver_name,
                   is_delegated, original_approver_id, action, comment)
                VALUES
                  (UUID(), @TenantId, @ApprovalId, @SeqNo, @ApproverId, @ApproverName,
                   @IsDelegated, @OriginalApproverId, @Action, @Comment)
                """,
                new
                {
                    TenantId = tenantId,
                    ApprovalId = approvalId,
                    SeqNo = doc.CurrentSeq,
                    ApproverId = employeeId,
                    ApproverName = employeeName,
                    IsDelegated = isDelegated ? 1 : 0,
                    OriginalApproverId = originalApproverId,
                    Action = request.Action,
                    Comment = request.Comment
                }, transaction: tx, cancellationToken: ct));

            // 상태 업데이트
            // P1-4 봉합(2026-06-20): 동시 중복 승인 방지. 모든 상태 전이 UPDATE 에 'AND status=pending'
            // 과 'AND current_seq=@SeqNo' 를 걸고 affected rows 를 확인한다. 두 결재자가 동시에
            // 같은 건을 처리해도, 먼저 커밋한 쪽만 1행을 갱신하고 늦은 쪽은 0행 → 예외로 차단된다.
            int affected;
            if (request.Action == "rejected")
            {
                affected = await _db.ExecuteAsync(new CommandDefinition(
                    "UPDATE approval_documents SET status = 'rejected', completed_at = NOW(6), updated_at = NOW(6) WHERE approval_id = @ApprovalId AND tenant_id = @TenantId AND status = 'pending' AND current_seq = @SeqNo",
                    new { ApprovalId = approvalId, TenantId = tenantId, SeqNo = doc.CurrentSeq }, transaction: tx, cancellationToken: ct));
            }
            else if (request.Action == "approved")
            {
                if (doc.CurrentSeq >= doc.TotalLines)
                {
                    affected = await _db.ExecuteAsync(new CommandDefinition(
                        "UPDATE approval_documents SET status = 'approved', completed_at = NOW(6), updated_at = NOW(6) WHERE approval_id = @ApprovalId AND tenant_id = @TenantId AND status = 'pending' AND current_seq = @SeqNo",
                        new { ApprovalId = approvalId, TenantId = tenantId, SeqNo = doc.CurrentSeq }, transaction: tx, cancellationToken: ct));
                }
                else
                {
                    affected = await _db.ExecuteAsync(new CommandDefinition(
                        "UPDATE approval_documents SET current_seq = current_seq + 1, updated_at = NOW(6) WHERE approval_id = @ApprovalId AND tenant_id = @TenantId AND status = 'pending' AND current_seq = @SeqNo",
                        new { ApprovalId = approvalId, TenantId = tenantId, SeqNo = doc.CurrentSeq }, transaction: tx, cancellationToken: ct));
                }
            }
            else
            {
                throw new InvalidOperationException($"알 수 없는 결재 동작입니다: {request.Action}");
            }

            if (affected == 0)
            {
                // 다른 결재자가 먼저 처리해 상태/순서가 이미 바뀐 경우. 이력 INSERT 까지 롤백해야 한다.
                // 봉합 (2026-06-20, APPR-01): 여기서 명시 Rollback 하지 않는다 — throw 만 하면 아래 catch 가
                //   단일 롤백을 수행한다. 종전엔 여기서 롤백 후 throw → catch 가 완료된 tx 에 재롤백 시도 →
                //   매 동시충돌마다 무의미한 "rollback failed" 에러 로그로 운영 로그를 오염시켰다.
                throw new InvalidOperationException("이미 처리된 결재입니다. (동시 처리 감지)");
            }

            // 봉합 (2026-06-23, 6차 전수조사 NEW-A1, 사장님 결재 "연차도 결재선 + A·B 둘다"):
            //   연차(doc_type='leave')는 결재선을 타고 결재함에서 처리된다. 결재함 최종 처리 시 원본
            //   leave_requests 와 잔여연차를 ★같은 트랜잭션(tx)★에서 동기화해, "결재함서 승인했는데 연차 미반영"
            //   끊김(헌법 #20)을 제거한다. ApprovalService→LeaveRequestService 의존을 만들지 않고 공통 헬퍼로 처리.
            //   - 최종 단계 승인(approved && current_seq>=total_lines): leave_requests='approved' + 잔여차감.
            //     중간 단계 승인(current_seq++)은 아직 미확정이라 건드리지 않음(헌법 #6 confirmed 시점).
            //   - 반려: leave_requests='rejected'. (status='pending' 가드로 멱등 — HR 화면이 먼저 처리했으면 0행.)
            if (doc.DocType == "leave" && !string.IsNullOrEmpty(doc.RefId))
            {
                if (request.Action == "rejected")
                {
                    await _db.ExecuteAsync(new CommandDefinition(
                        "UPDATE leave_requests SET status='rejected', approved_by=@Who, approved_at=NOW(6), reject_reason=@Reason, updated_at=NOW(6) WHERE tenant_id=@TenantId AND request_id=@RefId AND status='pending'",
                        new { Who = employeeId, Reason = TruncateRejectReason(request.Comment), TenantId = tenantId, RefId = doc.RefId },
                        transaction: tx, cancellationToken: ct));
                }
                else if (request.Action == "approved" && doc.CurrentSeq >= doc.TotalLines)
                {
                    var leaveAffected = await _db.ExecuteAsync(new CommandDefinition(
                        "UPDATE leave_requests SET status='approved', approved_by=@Who, approved_at=NOW(6), reject_reason=NULL, updated_at=NOW(6) WHERE tenant_id=@TenantId AND request_id=@RefId AND status='pending'",
                        new { Who = employeeId, TenantId = tenantId, RefId = doc.RefId },
                        transaction: tx, cancellationToken: ct));
                    // 실제로 pending→approved 전이된 경우만 차감(HR 화면이 먼저 승인했으면 0행 → 이중차감 방지).
                    if (leaveAffected > 0)
                        await LeaveBalanceHelper.DeductAsync(_db, tx, tenantId, doc.RefId, ct);
                }
            }

            // ── 휴직(absence) 원본 반영 ──────────────────────────────────────────
            // 작(2026-08-21) 사장님 지시: "영향 없다면 결재승인 작업해".
            //
            // 🔴 휴직은 다른 문서와 다르다. 원본 표만 바꾸면 부족하고 ★사원 work_status 까지★
            //    함께 바뀌어야 한다. 결재 경로가 그 처리를 안 타고 있어서, 결재함에서 승인하면
            //    휴직 신청서는 '대기중', 사원은 '재직' 으로 남았다
            //    (= 결재는 갔는데 아무것도 안 바뀐 상태).
            //
            // ⚠️ 사장님 못박음(2026-08-21): "급여는 수동입력 원칙이야. 그래서 급여가 휴직을 모르고
            //    사원만 알면 됨" / "사원마스터에 상태처리, 급여는 수동설정으로 고객이 알아서".
            //    ⇒ 여기서 바꾸는 것은 ★사원 상태(work_status)까지★ 다. 급여 금액은 건드리지 않는다.
            //    급여 명세는 고객이 직접 넣는다(PayrollService 는 absence_id 로 '어느 명세가
            //    휴직분인지' 잇기만 하고 금액을 자동 계산하지 않는다 — 그 구조를 그대로 둔다).
            //
            // ⚠️ 시작일 판정을 AbsenceService 와 ★같은 규칙★ 으로 맞춘다. 승인 시점에 이미
            //    시작일이 지났으면 'active'(휴직중), 아직이면 'approved'(시작 전).
            //    두 경로가 다른 값을 넣으면 사원 상태가 경로에 따라 갈린다.
            //
            // ⚠️ status='pending' 가드로 멱등 — 휴직 화면(AbsenceService)이 먼저 처리했으면
            //    0행이라 무해하다. leave 블록이 쓰는 방식과 같다.
            if (doc.DocType == "absence" && !string.IsNullOrEmpty(doc.RefId))
            {
                if (request.Action == "rejected")
                {
                    await _db.ExecuteAsync(new CommandDefinition(
                        "UPDATE employee_leave_of_absence SET status='rejected', approved_by=@Who, approved_at=NOW(6), reject_reason=@Reason, updated_at=NOW(6) WHERE tenant_id=@TenantId AND absence_id=@RefId AND status='pending'",
                        new { Who = employeeId, Reason = TruncateRejectReason(request.Comment), TenantId = tenantId, RefId = doc.RefId },
                        transaction: tx, cancellationToken: ct));
                }
                else if (request.Action == "approved" && doc.CurrentSeq >= doc.TotalLines)
                {
                    // 시작일을 읽어 'active'(이미 시작) / 'approved'(시작 전) 를 가른다.
                    var absence = await _db.QueryFirstOrDefaultAsync<AbsenceSyncRow>(new CommandDefinition(
                        "SELECT employee_id AS EmployeeId, start_date AS StartDate FROM employee_leave_of_absence WHERE tenant_id=@TenantId AND absence_id=@RefId AND status='pending'",
                        new { TenantId = tenantId, RefId = doc.RefId },
                        transaction: tx, cancellationToken: ct));

                    if (absence is not null)
                    {
                        var started = absence.StartDate.Date <= DateTime.Today;
                        var nextStatus = started ? "active" : "approved";

                        var absenceAffected = await _db.ExecuteAsync(new CommandDefinition(
                            "UPDATE employee_leave_of_absence SET status=@Status, approved_by=@Who, approved_at=NOW(6), reject_reason=NULL, updated_at=NOW(6) WHERE tenant_id=@TenantId AND absence_id=@RefId AND status='pending'",
                            new { Status = nextStatus, Who = employeeId, TenantId = tenantId, RefId = doc.RefId },
                            transaction: tx, cancellationToken: ct));

                        // 🔴 실제 전이된 경우에만 사원 상태를 바꾼다. 그리고 시작일이 지난 건만이다 —
                        //    아직 시작 전인 휴직으로 사원을 '휴직' 으로 바꾸면 재직 중인 사람이 빠진다.
                        if (absenceAffected > 0 && started)
                        {
                            await _db.ExecuteAsync(new CommandDefinition(
                                "UPDATE employees SET work_status=@WorkStatus, updated_at=NOW(6) WHERE tenant_id=@TenantId AND employee_id=@EmployeeId",
                                new { WorkStatus = AbsenceWorkStatus, TenantId = tenantId, EmployeeId = absence.EmployeeId },
                                transaction: tx, cancellationToken: ct));
                        }
                    }
                }
            }

            // 작(2026-08-13) 단계3: 업무보고서(일일·주간·월간·경위서) 원본 반영.
            // 🔴 이 배선이 없으면 결재함에서 승인해도 보고서는 "결재중" 에 머문다 — "되는 척" 이다.
            //    연차(위)와 같은 원칙으로 ★같은 트랜잭션★ 에서 동기화한다(헌법 #20 워크플로우 끊김 방지).
            //    - 반려: 즉시 rejected + 사유 기록(사유가 없으면 작성자가 왜 반려됐는지 모른다)
            //    - 승인: 최종 단계에서만 approved. 중간 단계는 아직 미확정이다(헌법 #6)
            //    status='pending' 가드로 멱등 — 두 번 처리돼도 0행이라 무해하다.
            else if (doc.DocType.StartsWith(ReportDocTypePrefix, StringComparison.Ordinal)
                     && !string.IsNullOrEmpty(doc.RefId))
            {
                if (request.Action == "rejected")
                {
                    await _db.ExecuteAsync(new CommandDefinition(
                        "UPDATE hr_reports SET status='rejected', reject_reason=@Reason, updated_at=NOW(6) WHERE tenant_id=@TenantId AND report_id=@RefId AND status='pending'",
                        new { Reason = TruncateRejectReason(request.Comment), TenantId = tenantId, RefId = doc.RefId },
                        transaction: tx, cancellationToken: ct));
                }
                else if (request.Action == "approved" && doc.CurrentSeq >= doc.TotalLines)
                {
                    await _db.ExecuteAsync(new CommandDefinition(
                        "UPDATE hr_reports SET status='approved', approved_by=@Who, approved_at=NOW(6), reject_reason=NULL, updated_at=NOW(6) WHERE tenant_id=@TenantId AND report_id=@RefId AND status='pending'",
                        new { Who = employeeId, TenantId = tenantId, RefId = doc.RefId },
                        transaction: tx, cancellationToken: ct));
                }
            }

            tx.Commit();

            // 감사로그 — 결재 승인/반려
            var afterJson = $"{{\"action\":\"{request.Action}\",\"seq\":{doc.CurrentSeq},\"approver\":\"{employeeName}\"}}";
            await _audit.LogAsync("state_change", "approval", approvalId,
                afterJson: afterJson, reason: request.Comment, ct: ct);

            // 🔴 작(2026-08-13) 검증팀 P0-1 봉합: 다음 결재자에게 알린다.
            //
            // 이 자리가 빠져 있으면 1단계 결재자만 알림을 받고 2·3단계는 아무 신호도 못 받는다.
            // 사이드바 뱃지는 최초 1회 조회뿐이라(이 작업의 출발점) 새로고침 전엔 아무도 모른다
            // ⇒ 결재선이 2단 이상인 고객사에서 첫 단계 이후 결재가 조용히 멈춘다(헌법 #20).
            //
            // 중간 승인일 때만 보낸다:
            //  - 반려면 흐름이 끝났으니 다음 결재자가 없다
            //  - 최종 승인(current_seq >= total_lines)도 다음이 없다
            // current_seq 는 위에서 +1 됐으므로 다음 단계 번호는 doc.CurrentSeq + 1 이다.
            //
            // 알림 실패가 이미 커밋된 결재를 되돌리면 안 되므로 트랜잭션 밖에서 부른다.
            if (_notifier is not null
                && request.Action == "approved"
                && doc.CurrentSeq < doc.TotalLines)
            {
                await ApprovalTriggerHelper.NotifyApproverAsync(
                    _db, doc.DocType, tenantId, doc.Title ?? string.Empty,
                    seqNo: doc.CurrentSeq + 1, _notifier, ct).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            try { tx.Rollback(); } catch (Exception rbex) { Console.Error.WriteLine($"[ApprovalService] rollback failed: {rbex.Message}"); }
            throw;
        }
    }

    // ═══════════════════════════════════════════
    // 헬퍼
    // ═══════════════════════════════════════════

    private static List<ApprovalDocumentDto> MapLabels(IEnumerable<ApprovalDocumentDto> rows)
    {
        var list = rows.ToList();
        foreach (var d in list)
        {
            d.DocTypeLabel = DocTypeLabels.GetValueOrDefault(d.DocType, d.DocType);
            d.StatusLabel = StatusLabels.GetValueOrDefault(d.Status, d.Status);
        }
        return list;
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open) return;
        if (_db is DbConnection c) { await c.OpenAsync(ct); return; }
        _db.Open();
    }
}
