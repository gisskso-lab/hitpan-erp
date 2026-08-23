using System.Data;
using Dapper;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>결재 트리거 + 월마감 체크 공통 헬퍼</summary>
public static class ApprovalTriggerHelper
{
    /// <summary>
    /// 이 문서유형에 결재를 걸 수 있는 상태인지 본다. <b>걸 수 없으면 그 이유를 사용자 말로 돌려준다.</b>
    /// </summary>
    /// <returns>결재를 걸 수 있으면 null. 못 걸면 사용자에게 보여줄 이유.</returns>
    /// <remarks>
    /// 🔴 봉합 (2026-08-13, 단계3 검증 P0-1): <see cref="TryCreateApprovalAsync"/> 는 설정이 꺼져 있거나
    /// 결재선이 없으면 <b>조용히 return</b> 한다. 판매·매입처럼 <i>결재가 부가 기능</i>인 문서는 그래도 되지만,
    /// 업무보고서·연차처럼 <b>결재가 존재 이유인</b> 문서는 그 침묵이 곧 <b>되는 척</b>이 된다 —
    /// 직원은 "올렸다" 를 보고, 문서는 <c>pending</c> 에 갇히고, 결재함엔 안 뜬다.
    ///
    /// 그래서 <b>같은 판정</b>을 미리 물어볼 수 있게 갈라 둔다. 판정 조건이 갈라지면 안 되므로
    /// <see cref="TryCreateApprovalAsync"/> 와 <b>같은 두 조건</b>(설정 ON · 결재선 1행 이상)만 본다.
    /// (게이트: WorkReportGuardTests — 두 곳의 조건이 어긋나면 시험이 잡는다)
    /// </remarks>
    public static async Task<string?> DescribeApprovalBlockerAsync(
        IDbConnection db, string tenantId, string docType, CancellationToken ct)
    {
        // 🔴 작20260823작1 [B] — TryCreateApprovalAsync 와 **같은 판정**을 유지한다.
        //    저쪽이 ERP 를 입구에서 되돌리는데 여기만 안 보면 두 곳의 조건이 갈라진다.
        //    그러면 ERP 문서에서 이걸 물었을 때 "결재 설정이 꺼져 있어요" 라는
        //    **틀린 안내**가 나간다 — 켜라고 해도 안 올라간다(설정 문제가 아니기 때문).
        //    ⇒ ERP 는 애초에 그룹웨어 결재 대상이 아니라고 정직하게 답한다.
        if (ApprovalService.IsErpDocType(docType))
        {
            return "ERP 문서는 그룹웨어 결재 대상이 아닙니다. 확정으로 승인됩니다.";
        }

        var enabled = await db.QueryFirstOrDefaultAsync<bool?>(new CommandDefinition(
            "SELECT is_enabled FROM approval_settings WHERE tenant_id = @TenantId AND doc_type = @DocType",
            new { TenantId = tenantId, DocType = docType }, cancellationToken: ct));

        if (enabled is not true)
        {
            return "결재 설정이 꺼져 있어 결재가 올라가지 않았습니다. 설정 → 결재설정에서 켜주세요.";
        }

        var lineCount = await db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM approval_doc_lines WHERE tenant_id = @TenantId AND doc_type = @DocType AND is_active = 1",
            new { TenantId = tenantId, DocType = docType }, cancellationToken: ct));

        if (lineCount == 0)
        {
            return "결재선이 없어 결재가 올라가지 않았습니다. 설정 → 결재선에서 결재자를 지정해주세요.";
        }

        return null;
    }

    /// <summary>해당 날짜의 월이 마감되었으면 예외 발생</summary>
    public static async Task EnsureNotClosedAsync(IDbConnection db, string tenantId, DateTime date, CancellationToken ct)
    {
        var ym = date.ToString("yyyyMM");
        var status = await db.QueryFirstOrDefaultAsync<string>(
            new CommandDefinition(
                "SELECT status FROM monthly_closing WHERE tenant_id = @TenantId AND `year_month` = @Ym",
                new { TenantId = tenantId, Ym = ym }, cancellationToken: ct));
        if (status == "closed")
            throw new InvalidOperationException($"{ym[..4]}년 {ym[4..]}월은 마감된 기간입니다. 전표를 수정할 수 없습니다.");
    }

    /// <summary>
    /// [Deprecated 작4 P0-4] 매출/매입 확정 시 monthly_summary 갱신.
    /// 멱등 보장이 없어 같은 source 두 번 가산되는 위험. 호출처 0건이지만 안전 차원에서 Obsolete 마킹.
    /// 신규 호출은 <see cref="MonthlySummaryGuard.TryApplyAsync"/>를 사용할 것.
    /// </summary>
    [Obsolete("멱등 보장 없음. MonthlySummaryGuard.TryApplyAsync를 사용하세요. (작4 P0-4)", error: false)]
    public static async Task UpdateMonthlySummaryAsync(IDbConnection db, string tenantId, DateTime date, decimal salesAmount, decimal purchaseAmount, CancellationToken ct)
    {
        var ym = date.ToString("yyyyMM");
        await db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO monthly_summary (summary_id, tenant_id, `year_month`, total_sales, total_purchase, total_receipt, total_payment, last_updated_at)
            VALUES (UUID(), @TenantId, @Ym, @Sales, @Purchase, 0, 0, NOW(6))
            ON DUPLICATE KEY UPDATE
              total_sales = total_sales + @Sales,
              total_purchase = total_purchase + @Purchase,
              last_updated_at = NOW(6)
            """,
            new { TenantId = tenantId, Ym = ym, Sales = salesAmount, Purchase = purchaseAmount },
            cancellationToken: ct));
    }

    /// <summary>결재 설정 ON이고 기준금액 이상이면 결재 문서 자동 생성</summary>
    /// <param name="notifier">
    /// 작(2026-08-13) 단계2: 결재가 만들어지면 <b>첫 결재자에게 알린다</b>.
    /// null 이면 알림 없이 기존과 동일하게 동작한다(호출부를 한꺼번에 고치지 않기 위해 선택 인자).
    /// </param>
    public static async Task TryCreateApprovalAsync(
        IDbConnection db, string docType, string refId, string refNo, string title,
        decimal amount, string tenantId, string requesterId, string requesterName,
        CancellationToken ct,
        INotificationService? notifier = null)
    {
        // ── 🔴 작20260823작1 [B] — ERP 는 그룹웨어 결재로 올리지 않는다 ──
        //
        // 사장님: "erp와 그룹웨어 결재를 묶으면 안됨. 분리해"
        //         "erp 자료는 '확정' 이라는 단계가 이미 있음"
        //         "이거 넣고 결재 돌리면 난리난다. 하루에 수십수백건씩 견적, 발주, 거래가 잡힐텐데"
        //
        // 🔴 왜 호출부 4곳이 아니라 여기서 막나.
        //    트리거는 전부 이 한 곳을 지난다. 여기서 막으면 호출부를 하나 빠뜨려도 안 샌다.
        //    호출부에서 지우는 방식이면 나중에 누가 새 호출을 추가할 때 다시 열린다.
        //    ⇒ 문을 하나로 두고 그 문에서 막는다.
        //
        // 🔴 확정 로직은 건드리지 않았다. 결재 호출만 여기서 되돌린다.
        //    확정이 곧 승인이라는 축은 그대로다.
        //
        // ⚠️ 이미 만들어진 ERP 결재 문서는 그대로 흐른다(승인·반려 경로 무변경).
        //    신규만 막는다 — 갑자기 끊으면 도는 결재가 선다(헌법 #20).
        if (ApprovalService.IsErpDocType(docType)) return;

        // 결재 설정 확인
        var setting = await db.QueryFirstOrDefaultAsync<(bool IsEnabled, decimal Threshold, bool AutoBelow)>(
            new CommandDefinition(
                "SELECT is_enabled AS IsEnabled, threshold_amount AS Threshold, auto_approve_below AS AutoBelow FROM approval_settings WHERE tenant_id = @TenantId AND doc_type = @DocType",
                new { TenantId = tenantId, DocType = docType }, cancellationToken: ct));

        if (!setting.IsEnabled) return;

        // 기준금액 미만 자동승인이면 결재 불요
        if (setting.AutoBelow && setting.Threshold > 0 && amount < setting.Threshold) return;

        // 결재 라인 수 확인
        // 봉합 (2026-06-23, 5차 후속 APPR-TRIGGER P0급): 종전엔 FROM approval_lines 에서 doc_type 으로
        //   COUNT 했다. 그러나 approval_lines 는 named-template 마스터(approval_line_id/name/sort_order
        //   구조)로 재설계되며 doc_type 컬럼이 사라졌고(DB-29), 결재자 행과 doc_type/seq_no 는 별개 테이블
        //   approval_doc_lines 로 분리됐다(DB-30). 따라서 이 쿼리는 결재 설정 ON 고객이 거래명세서·매입·
        //   반품·연차·경비를 확정할 때 "Unknown column 'doc_type'" 런타임 500 을 일으켰다(워크플로우 끊김,
        //   헌법 #20). ProcessAsync·CreateApprovalAsync 가 권한·총라인을 산정하는 정본 테이블인
        //   approval_doc_lines 에서 세도록 교체한다 — total_lines 가 실제 결재 단계 수와 일치해
        //   ProcessAsync 의 current_seq>=total_lines 완료 판정도 정확해진다. (설계팀장 승인·검증팀장 확정)
        var lineCount = await db.QueryFirstOrDefaultAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM approval_doc_lines WHERE tenant_id = @TenantId AND doc_type = @DocType AND is_active = 1",
                new { TenantId = tenantId, DocType = docType }, cancellationToken: ct));
        if (lineCount == 0) return;

        // 중복 방지
        var exists = await db.QueryFirstOrDefaultAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM approval_documents WHERE tenant_id = @TenantId AND doc_type = @DocType AND ref_id = @RefId",
                new { TenantId = tenantId, DocType = docType, RefId = refId }, cancellationToken: ct));
        if (exists > 0) return;

        // 결재 문서 생성
        await db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO approval_documents
              (approval_id, tenant_id, doc_type, ref_id, ref_no, title, amount,
               status, current_seq, total_lines, requester_id, requester_name, memo, created_by)
            VALUES
              (UUID(), @TenantId, @DocType, @RefId, @RefNo, @Title, @Amount,
               'pending', 1, @TotalLines, @RequesterId, @RequesterName, '확정 시 자동 생성', @RequesterId)
            """,
            new
            {
                TenantId = tenantId, DocType = docType, RefId = refId, RefNo = refNo,
                Title = title, Amount = amount, TotalLines = lineCount,
                RequesterId = requesterId, RequesterName = requesterName
            }, cancellationToken: ct));

        // 작(2026-08-13) 단계2: 첫 결재자에게 알린다.
        // 🔴 이 배선이 이 작업의 본체다. 허브·서비스만 만들고 여기서 부르지 않으면
        //    "알림 기능을 만들었는데 아무 알림도 안 오는" 상태가 된다(8/11 AI 연동에서 겪은 그것).
        if (notifier is not null)
        {
            await NotifyApproverAsync(db, docType, tenantId, title, seqNo: 1, notifier, ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 결재선 <paramref name="seqNo"/> 단계 결재자에게 "결재가 올라왔습니다" 를 보낸다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 결재선은 <c>doc_type</c> 단위 전역 마스터라 문서별 결재선이 따로 없다.
    /// 그래서 <c>seq_no</c> 행의 결재자를 읽는다 — <c>ProcessAsync</c> 가 권한을 판정할 때
    /// 쓰는 것과 <b>같은 표·같은 조건</b>이어야 한다. 여기만 다르면 "알림은 왔는데 결재는 못 하는"
    /// 사람이 생긴다.
    /// </para>
    /// <para>
    /// 🔴 <b>검증팀 P0-2 봉합(2026-08-13)</b> — 처음에는 <c>delegate_id</c> 를 무조건 발송 대상에
    /// 넣었다. 그런데 결재 권한 판정은 <c>CURDATE() BETWEEN delegate_start AND delegate_end</c> 로
    /// <b>대결 기간</b>을 본다. 그래서 <b>작년에 휴가 대결을 한 번 걸어둔 직원이 지금도 남의 결재
    /// 알림을 계속 받고</b>, 눌러 들어가면 "결재 권한이 없습니다" 가 떴다. 게다가 타 부서 결재가
    /// 올라온 사실이 새는 정보 노출이기도 하다.
    /// 바로 위 주석에 <i>"같은 조건이어야 한다"</i> 고 적어 놓고 그대로 어겼다.
    /// ⇒ <c>ApprovalService.ProcessAsync</c> 의 조건을 <b>그대로 복제</b>한다.
    /// </para>
    /// <para>
    /// 🔴 금액은 넣지 않는다 — 알림은 화면에 떠 있는 동안 옆 사람에게도 보인다.
    /// </para>
    /// </remarks>
    public static async Task NotifyApproverAsync(
        IDbConnection db, string docType, string tenantId, string title, int seqNo,
        INotificationService notifier, CancellationToken ct)
    {
        var line = await db.QueryFirstOrDefaultAsync<(string? ApproverId, string? DelegateId, bool DelegateActive)>(
            new CommandDefinition(
                """
                SELECT approver_id AS ApproverId, delegate_id AS DelegateId,
                       (delegate_id IS NOT NULL
                        AND delegate_start IS NOT NULL AND delegate_end IS NOT NULL
                        AND CURDATE() BETWEEN delegate_start AND delegate_end) AS DelegateActive
                  FROM approval_doc_lines
                 WHERE tenant_id = @TenantId
                   AND doc_type = @DocType
                   AND seq_no = @SeqNo
                   AND is_active = 1
                 LIMIT 1
                """,
                new { TenantId = tenantId, DocType = docType, SeqNo = seqNo },
                cancellationToken: ct)).ConfigureAwait(false);

        var docLabel = DocTypeLabel(docType);
        var notification = new AppNotification
        {
            Type = "approval_pending",
            Title = "결재가 올라왔습니다",
            Body = string.IsNullOrWhiteSpace(title) ? docLabel : $"{docLabel} — {title}",
            Link = "/approval/pending"
        };

        // 대결이 유효한 기간에만 대결자에게 보낸다(권한 판정과 동일).
        var targets = line.DelegateActive
            ? new[] { line.ApproverId, line.DelegateId }
            : new[] { line.ApproverId };

        foreach (var target in targets)
        {
            if (!string.IsNullOrWhiteSpace(target))
            {
                await notifier.NotifyEmployeeAsync(tenantId, target, notification, ct)
                    .ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 문서유형 라벨. 알림 본문에 쓴다.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>ApprovalService.DocTypeLabels</c> 와 같은 내용이다. 거기는 private 이라 재사용할 수
    /// 없어 옮겨 적었다. 문서유형을 새로 추가할 때 <b>두 곳을 함께 고쳐야</b> 하며,
    /// 빠뜨리면 알림에 영문 코드가 그대로 노출된다(고객 노출 영역 개발용어 금지).
    /// </remarks>
    private static string DocTypeLabel(string docType) => docType switch
    {
        "quotation"       => "견적서",
        "sales_order"     => "수주서",
        "delivery"        => "거래명세서",
        "purchase_order"  => "발주서",
        "receipt"         => "매입",
        "sales_return"    => "매출반품",
        "purchase_return" => "매입반품",
        "expense"         => "경비",
        "leave"           => "연차",
        "overtime"        => "초과근무",
        // 작(2026-08-13) 단계3: 업무보고서 4종.
        "report_daily"    => "일일보고서",
        "report_weekly"   => "주간보고서",
        "report_monthly"  => "월간보고서",
        "report_incident" => "경위서",
        // 작(2026-08-21) P0 봉합: 위 주석이 "두 곳을 함께 고쳐야 한다" 고 경고해 놓고
        // 정작 absence 에서 빠뜨렸다. 없으면 알림이 "결재 문서" 로 뭉개진다.
        "absence"         => "휴직",
        // 작20260823작1 [D] — 창구 통합으로 채운 2종. 알림 라벨도 함께 넣는다.
        //   🔴 빠뜨리면 알림에 영문 코드가 그대로 나간다(가드시험이 잡는 자리).
        "resignation"     => "사직서",
        "labor_contract"  => "전자근로계약서",
        _                 => "결재 문서"
    };
}
