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
        ["quotation"]      = "견적서",
        ["sales_order"]    = "수주서",
        ["delivery"]       = "거래명세서",
        ["purchase_order"] = "발주서",
        ["receipt"]        = "매입명세서",
        ["return"]         = "반품",
        ["expense"]        = "경비",
        ["leave"]          = "휴가",
        ["overtime"]       = "초과근무"
    };

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

    public ApprovalService(IDbConnection db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
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
        // 기존 라인 비활성화 후 새로 추가
        await _db.ExecuteAsync(new CommandDefinition(
            "UPDATE approval_doc_lines SET is_active = 0 WHERE tenant_id = @TenantId AND doc_type = @DocType",
            new { TenantId = tenantId, DocType = request.DocType }, cancellationToken: ct));

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
                }, cancellationToken: ct));
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
            }, cancellationToken: ct));

        // 자동승인인 경우 이력 기록
        if (status == "approved")
        {
            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO approval_history
                  (history_id, tenant_id, approval_id, seq_no, approver_id, approver_name, action, comment, acted_at)
                VALUES
                  (UUID(), @TenantId, @ApprovalId, 0, 'system', '시스템', 'approved', '기준금액 미만 자동승인', NOW(6))
                """,
                new { TenantId = tenantId, ApprovalId = approvalId }, cancellationToken: ct));
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
            ORDER BY seq_no, acted_at
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

        // 현재 문서 조회
        var doc = await _db.QueryFirstOrDefaultAsync<(string Status, int CurrentSeq, int TotalLines, string DocType)>(
            new CommandDefinition(
                "SELECT status AS Status, current_seq AS CurrentSeq, total_lines AS TotalLines, doc_type AS DocType FROM approval_documents WHERE approval_id = @ApprovalId AND tenant_id = @TenantId",
                new { ApprovalId = approvalId, TenantId = tenantId }, cancellationToken: ct));

        if (doc.Status != "pending")
            throw new InvalidOperationException("이미 처리된 결재입니다.");

        // 결재 권한 확인 (현재 순서의 결재자 또는 위임자인지)
        var line = await _db.QueryFirstOrDefaultAsync<(string ApproverId, string? DelegateId, DateTime? DelegateStart, DateTime? DelegateEnd)>(
            new CommandDefinition(
                """
                SELECT approver_id AS ApproverId, delegate_id AS DelegateId,
                       delegate_start AS DelegateStart, delegate_end AS DelegateEnd
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
        else if (line.DelegateId == employeeId
                 && line.DelegateStart.HasValue && line.DelegateEnd.HasValue
                 && DateTime.Today >= line.DelegateStart.Value && DateTime.Today <= line.DelegateEnd.Value)
        {
            // 위임결재자
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
            if (request.Action == "rejected")
            {
                await _db.ExecuteAsync(new CommandDefinition(
                    "UPDATE approval_documents SET status = 'rejected', completed_at = NOW(6), updated_at = NOW(6) WHERE approval_id = @ApprovalId AND tenant_id = @TenantId",
                    new { ApprovalId = approvalId, TenantId = tenantId }, transaction: tx, cancellationToken: ct));
            }
            else if (request.Action == "approved")
            {
                if (doc.CurrentSeq >= doc.TotalLines)
                {
                    await _db.ExecuteAsync(new CommandDefinition(
                        "UPDATE approval_documents SET status = 'approved', completed_at = NOW(6), updated_at = NOW(6) WHERE approval_id = @ApprovalId AND tenant_id = @TenantId",
                        new { ApprovalId = approvalId, TenantId = tenantId }, transaction: tx, cancellationToken: ct));
                }
                else
                {
                    await _db.ExecuteAsync(new CommandDefinition(
                        "UPDATE approval_documents SET current_seq = current_seq + 1, updated_at = NOW(6) WHERE approval_id = @ApprovalId AND tenant_id = @TenantId",
                        new { ApprovalId = approvalId, TenantId = tenantId }, transaction: tx, cancellationToken: ct));
                }
            }

            tx.Commit();

            // 감사로그 — 결재 승인/반려
            var afterJson = $"{{\"action\":\"{request.Action}\",\"seq\":{doc.CurrentSeq},\"approver\":\"{employeeName}\"}}";
            await _audit.LogAsync("state_change", "approval", approvalId,
                afterJson: afterJson, reason: request.Comment, ct: ct);
        }
        catch
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
