using System.Text.RegularExpressions;

namespace HitPan.Application.Services;

/// <summary>
/// 🔴 W-G1-52 갈래 A-2③ — 걸음 ④-끝 · ⑤ 의 SQL 글(한 곳 · 정적 게이트가 반사로 읽음 · C6 <c>!=</c> · <c>&lt;&gt;</c> 0).
/// </summary>
/// <remarks>
/// <para>모든 문장의 회사 = <c>@t</c> 하나(#2) · 맞대는 열쇠마다 <c>tenant_id</c> 함께(C6) · 비교 = <c>&lt;=&gt;</c>.</para>
/// <para>④-끝 문장은 <c>rs_new__</c> 표에만 쓴다(라이브 읽기만 · #3 · #6). ⑤ 의 쓰기 문장은 <see cref="RestoreSwapStatementCheck"/> 를 지난 것만 나간다.</para>
/// <para>보1 덧2-4 의 「② 넣는 식 ↔ ③ 따로 다시 세는 식」 은 <b>서로 다른 글</b>이다(한쪽을 망가뜨린 변이가 다른 쪽에 걸림) — ③ 은 이 파일 안에서도 ② 를 부르지 않는다.</para>
/// </remarks>
public static class RestoreSwapSql
{
    /// <summary>원본 연결 사본(세션 임시 표 · 보1 덧2-4 · <c>rs_new__</c> 울타리 안 · ⑤ 손 0).</summary>
    public const string SnapshotTable = "rs_new__link_snapshot";

    /// <summary>⑤ 세션 sql_mode(본 §15-3 ⑤ R5 · 풀 세션 기대 0).</summary>
    public const string SwapSqlMode = "STRICT_ALL_TABLES,NO_AUTO_VALUE_ON_ZERO,NO_ENGINE_SUBSTITUTION";

    public const string DropSnapshot = "DROP TEMPORARY TABLE IF EXISTS rs_new__link_snapshot";

    public const string CreateSnapshot =
        "CREATE TEMPORARY TABLE rs_new__link_snapshot ENGINE=InnoDB AS SELECT tenant_id, employee_id, user_id FROM rs_new__employees WHERE tenant_id = @t";

    // ══ ④-끝 셈(덧씌우기 전) ══
    public const string CountEmployeeDiffBefore =
        """
        SELECT
          CAST(COALESCE(SUM(NOT (n.role <=> l.role)), 0) AS SIGNED) AS RoleDiff,
          CAST(COALESCE(SUM(NOT (n.is_active <=> l.is_active AND n.is_resigned <=> l.is_resigned
                                 AND n.resign_date <=> l.resign_date AND n.resign_reason <=> l.resign_reason)), 0) AS SIGNED) AS ActiveDiff,
          CAST(COALESCE(SUM(NOT (n.user_id <=> l.user_id AND n.login_id <=> l.login_id)), 0) AS SIGNED) AS LinkDiff
        FROM rs_new__employees n
        JOIN employees l ON l.tenant_id = n.tenant_id AND l.employee_id = n.employee_id
        WHERE n.tenant_id = @t
        """;

    public const string CountChatDiffBefore =
        """
        SELECT CAST(COALESCE(SUM(NOT (n.left_at <=> l.left_at)), 0) AS SIGNED)
        FROM rs_new__chat_room_members n
        JOIN chat_room_members l ON l.tenant_id = n.tenant_id AND l.room_id = n.room_id AND l.employee_id = n.employee_id
        WHERE n.tenant_id = @t
        """;

    // ══ ④-끝 ① 덧씌움 ══

    /// <summary>
    /// ①-가 — 임시 표의 계정 연결을 먼저 비운다(원래 값은 사본에 있음). 두 행이 계정을 맞바꾼 경우 한 문장 UPDATE 도중 유일 키
    /// <c>uq_employees_tenant_user</c> 가 잠깐 겹쳐 멈추는 것을 막음 — 끝 값은 ①-나(둘 다 있는 행 = 라이브) · ②(백업에만 = ⓐ″)가 다시 채움.
    /// </summary>
    public const string ClearStagedLinks =
        "UPDATE rs_new__employees n SET n.user_id = NULL, n.login_id = NULL WHERE n.tenant_id = @t";

    /// <summary>①-나 — 둘 다 있는 사원 행 덧씌움 7칸 = 라이브 값(칸 목록 = <see cref="RestoreOverlayRules.EmployeeOverlayColumns"/>).</summary>
    public static string OverlayEmployees =>
        "UPDATE rs_new__employees n JOIN employees l ON l.tenant_id = n.tenant_id AND l.employee_id = n.employee_id SET "
        + string.Join(", ", RestoreOverlayRules.EmployeeOverlayColumns.Select(c => $"n.{c} = l.{c}"))
        + " WHERE n.tenant_id = @t";

    /// <summary>①-다 — 둘 다 있는 방 참여 줄 <c>left_at</c> = 라이브 값.</summary>
    public static string OverlayChatMembers =>
        "UPDATE rs_new__chat_room_members n JOIN chat_room_members l ON l.tenant_id = n.tenant_id AND l.room_id = n.room_id AND l.employee_id = n.employee_id SET "
        + string.Join(", ", RestoreOverlayRules.ChatMemberOverlayColumns.Select(c => $"n.{c} = l.{c}"))
        + " WHERE n.tenant_id = @t";

    // ══ ④-끝 ② ⓐ″ — 넣는 식(라이브 + 사본으로 봄) ══
    private static string LinkEligible =>
        "u.user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees l2 JOIN rs_new__employees o2"
        + " ON o2.tenant_id = l2.tenant_id AND o2.employee_id = l2.employee_id"
        + " WHERE l2.tenant_id = n.tenant_id AND l2.user_id = s.user_id)";

    /// <summary>
    /// ② — 백업에만 있는 사원 행: 백업 계정 U 가 지금 살아 있고(<see cref="RestoreOverlayRules.ActiveAccount"/>) 둘 다 있는 줄 어느 것도 U 를 갖지 않을 때만 이어 줌
    /// (<c>login_id</c> = 지금 <c>users.email</c> · #40 아이디 방식 같음 · 보1 덧2-5) · 아니면 NULL.
    /// </summary>
    public static string LinkBackupOnly =>
        "UPDATE rs_new__employees n"
        + " JOIN rs_new__link_snapshot s ON s.tenant_id = n.tenant_id AND s.employee_id = n.employee_id"
        + " LEFT JOIN users u ON u.tenant_id = n.tenant_id AND u.user_id = s.user_id AND " + RestoreOverlayRules.ActiveAccount("u")
        + $" SET n.user_id = IF({LinkEligible}, u.user_id, NULL), n.login_id = IF({LinkEligible}, u.email, NULL)"
        + " WHERE n.tenant_id = @t"
        + " AND NOT EXISTS (SELECT 1 FROM employees l WHERE l.tenant_id = n.tenant_id AND l.employee_id = n.employee_id)";

    // ══ ④-끝 ②′ C4 — 좁힌 방 규칙 ══
    public const string NarrowChatLeave =
        """
        UPDATE rs_new__chat_room_members n
        SET n.left_at = COALESCE(n.left_at, @RestoreAt)
        WHERE n.tenant_id = @t
          AND NOT EXISTS (SELECT 1 FROM chat_room_members l
                          WHERE l.tenant_id = n.tenant_id AND l.room_id = n.room_id AND l.employee_id = n.employee_id)
          AND EXISTS (SELECT 1 FROM chat_room_members r WHERE r.tenant_id = n.tenant_id AND r.room_id = n.room_id)
        """;

    // ══ ④-끝 ②″ (가′) · C8 — 대상 R′ ══
    public const string ResignTargets =
        """
        SELECT n.employee_id FROM rs_new__employees n
        WHERE n.tenant_id = @t
          AND EXISTS (SELECT 1 FROM rs_new__approval_doc_lines dl
                      WHERE dl.tenant_id = n.tenant_id AND dl.approver_id = n.employee_id AND dl.is_active = 1)
          AND ( n.is_active = 0
             OR ( NOT EXISTS (SELECT 1 FROM employees l WHERE l.tenant_id = n.tenant_id AND l.employee_id = n.employee_id)
                  AND EXISTS (SELECT 1 FROM rs_new__link_snapshot s
                              WHERE s.tenant_id = n.tenant_id AND s.employee_id = n.employee_id AND s.user_id IS NOT NULL
                                AND NOT EXISTS (SELECT 1 FROM users u
                                                WHERE u.tenant_id = s.tenant_id AND u.user_id = s.user_id AND u.is_deleted = 0)) ) )
        ORDER BY n.employee_id
        """;

    /// <summary>(가′) 첫 문장 — 퇴사 뒤처리(<c>EmployeeService.cs</c> approvalLineSql)와 같은 글 · 표 이름만 <c>rs_new__</c>.</summary>
    public const string ResignLinesOff =
        """
        UPDATE rs_new__approval_doc_lines
        SET is_active = 0,
            updated_at = NOW(6)
        WHERE tenant_id = @TenantId
          AND approver_id = @EmployeeId
          AND is_active = 1
        """;

    /// <summary>(가′) 둘째 문장 — 퇴사 뒤처리(approvalDocSql)와 같은 글 · 같은 순서 · memo 문구 같음.</summary>
    public const string ResignDocsRecall =
        """
        UPDATE rs_new__approval_documents d
        SET d.status = 'cancelled',
            d.completed_at = NOW(6),
            d.updated_at = NOW(6),
            d.memo = CONCAT(COALESCE(d.memo, ''), ' / 결재자 퇴사로 회수')
        WHERE d.tenant_id = @TenantId
          AND d.status = 'pending'
          AND EXISTS (
              SELECT 1 FROM rs_new__approval_doc_lines l
              WHERE l.tenant_id = d.tenant_id
                AND l.doc_type  = d.doc_type
                AND l.approver_id = @EmployeeId
          )
        """;

    // ══ ④-끝 ③ 덧씌운 뒤 검사 ══
    public const string CountLinkOverlap =
        """
        SELECT CAST(COUNT(*) AS SIGNED) FROM (
          SELECT n.user_id FROM rs_new__employees n
          WHERE n.tenant_id = @t AND n.user_id IS NOT NULL
          GROUP BY n.user_id HAVING COUNT(*) > 1) z
        """;

    /// <summary>C3′ — 둘 다 있는 행 7칸 = 라이브(<c>&lt;=&gt;</c>). 칸을 <b>글로 따로</b> 적는다(덧씌움 상수와 독립 · 상수에서 칸을 빼면 여기서 걸림).</summary>
    public const string CountOverlayMismatch =
        """
        SELECT CAST(COUNT(*) AS SIGNED) FROM rs_new__employees n
        JOIN employees l ON l.tenant_id = n.tenant_id AND l.employee_id = n.employee_id
        WHERE n.tenant_id = @t
          AND NOT (l.role <=> n.role AND l.is_active <=> n.is_active AND l.user_id <=> n.user_id
               AND l.login_id <=> n.login_id AND l.is_resigned <=> n.is_resigned
               AND l.resign_date <=> n.resign_date AND l.resign_reason <=> n.resign_reason)
        """;

    /// <summary>C3′ · ⓐ″ ③ — 따로 다시 세는 식(보1 덧2-4 ③ 글 · 덧씌운 뒤 <c>rs_new__employees</c> 끼리로 「U 를 가진 다른 행」 을 봄) = 0.</summary>
    public const string CountBackupOnlyLinkMismatch =
        """
        SELECT CAST(COUNT(*) AS SIGNED) FROM rs_new__employees n
          JOIN rs_new__link_snapshot s ON s.tenant_id = n.tenant_id AND s.employee_id = n.employee_id
         WHERE n.tenant_id = @t
           AND NOT EXISTS (SELECT 1 FROM employees l WHERE l.tenant_id = n.tenant_id AND l.employee_id = n.employee_id)
           AND NOT ( n.user_id <=> (CASE WHEN EXISTS (SELECT 1 FROM users u WHERE u.tenant_id = n.tenant_id AND u.user_id = s.user_id
                                                      AND u.is_deleted = 0 AND u.is_active = 1
                                                      AND u.email NOT LIKE 'resigned+%' AND u.email NOT LIKE 'retired+%')
                                         AND NOT EXISTS (SELECT 1 FROM rs_new__employees o WHERE o.tenant_id = n.tenant_id
                                                         AND NOT (o.employee_id = n.employee_id) AND o.user_id = s.user_id)
                                         THEN s.user_id END)
                 AND n.login_id <=> (SELECT u.email FROM users u WHERE u.tenant_id = n.tenant_id AND u.user_id = n.user_id) )
        """;

    /// <summary>본 §15-5 ③ ⓐ 사원 몫(보1 §2-5 ⓐ) — 덧씌운 뒤 연결된 계정이 지금 <c>users</c> 에 있음.</summary>
    public const string CountLinkToMissingUser =
        """
        SELECT CAST(COUNT(*) AS SIGNED) FROM rs_new__employees n
        WHERE n.tenant_id = @t AND n.user_id IS NOT NULL
          AND NOT EXISTS (SELECT 1 FROM users u WHERE u.tenant_id = n.tenant_id AND u.user_id = n.user_id)
        """;

    /// <summary>④ 「ⓑ 후보」 셈(ⓑ′ 없이 · 적기만).</summary>
    public static string CountBCandidates =>
        "SELECT CAST(COUNT(*) AS SIGNED) FROM employees l WHERE " + RestoreOverlayRules.LiveOnlyActiveLinkedWhere("l");

    /// <summary>④-끝 ③ ⓑ′ 확정 — 남는 멈춤 대상 계정 이름(D1 안내).</summary>
    public static string ListNewerAccountsAfterBPrime =>
        "SELECT COALESCE(u.user_name, u.email) FROM employees l JOIN users u ON u.tenant_id = l.tenant_id AND u.user_id = l.user_id WHERE "
        + RestoreOverlayRules.LiveOnlyActiveLinkedAfterBPrime("l")
        + " ORDER BY u.user_name";

    public const string CountBothEmployees =
        "SELECT CAST(COUNT(*) AS SIGNED) FROM employees l JOIN rs_new__employees n ON n.tenant_id = l.tenant_id AND n.employee_id = l.employee_id WHERE l.tenant_id = @t";

    public const string CountLiveOnlyEmployees =
        "SELECT CAST(COUNT(*) AS SIGNED) FROM employees l WHERE l.tenant_id = @t AND NOT EXISTS (SELECT 1 FROM rs_new__employees n WHERE n.tenant_id = l.tenant_id AND n.employee_id = l.employee_id)";

    public const string CountBothChat =
        "SELECT CAST(COUNT(*) AS SIGNED) FROM chat_room_members l JOIN rs_new__chat_room_members n ON n.tenant_id = l.tenant_id AND n.room_id = l.room_id AND n.employee_id = l.employee_id WHERE l.tenant_id = @t";

    public const string CountLiveOnlyChat =
        "SELECT CAST(COUNT(*) AS SIGNED) FROM chat_room_members l WHERE l.tenant_id = @t AND NOT EXISTS (SELECT 1 FROM rs_new__chat_room_members n WHERE n.tenant_id = l.tenant_id AND n.room_id = l.room_id AND n.employee_id = l.employee_id)";

    // ══ ⑤ C1′ 네 셈(보1 덧2-3 · CTO 재1-1 글 그대로 · 잠그며 읽기 · 쓰기 0) ══
    public const string C1EmployeesDiff =
        """
        SELECT COUNT(*) FROM employees l
          JOIN rs_new__employees n ON n.tenant_id = l.tenant_id AND n.employee_id = l.employee_id
         WHERE l.tenant_id = @t
           AND NOT (l.role <=> n.role AND l.is_active <=> n.is_active AND l.user_id <=> n.user_id
                AND l.login_id <=> n.login_id AND l.is_resigned <=> n.is_resigned
                AND l.resign_date <=> n.resign_date AND l.resign_reason <=> n.resign_reason)
           FOR UPDATE
        """;

    public const string C1EmployeesBoth =
        "SELECT COUNT(*) FROM employees l JOIN rs_new__employees n ON n.tenant_id = l.tenant_id AND n.employee_id = l.employee_id WHERE l.tenant_id = @t FOR UPDATE";

    public const string C1EmployeesLiveOnly =
        "SELECT COUNT(*) FROM employees l WHERE l.tenant_id = @t AND NOT EXISTS (SELECT 1 FROM rs_new__employees n WHERE n.tenant_id = l.tenant_id AND n.employee_id = l.employee_id) FOR UPDATE";

    /// <summary>ⓓ — 라이브에만 · 활성 계정 연결 · 「ⓑ′ 밖」(④-끝 ③ 과 <b>같은 상수</b>).</summary>
    public static string C1EmployeesNewerLinked =>
        "SELECT COUNT(*) FROM employees l WHERE " + RestoreOverlayRules.LiveOnlyActiveLinkedAfterBPrime("l") + " FOR UPDATE";

    public const string C1ChatDiff =
        """
        SELECT COUNT(*) FROM chat_room_members l
          JOIN rs_new__chat_room_members n ON n.tenant_id = l.tenant_id AND n.room_id = l.room_id AND n.employee_id = l.employee_id
         WHERE l.tenant_id = @t AND NOT (l.left_at <=> n.left_at)
           FOR UPDATE
        """;

    public const string C1ChatBoth =
        "SELECT COUNT(*) FROM chat_room_members l JOIN rs_new__chat_room_members n ON n.tenant_id = l.tenant_id AND n.room_id = l.room_id AND n.employee_id = l.employee_id WHERE l.tenant_id = @t FOR UPDATE";

    public const string C1ChatLiveOnly =
        "SELECT COUNT(*) FROM chat_room_members l WHERE l.tenant_id = @t AND NOT EXISTS (SELECT 1 FROM rs_new__chat_room_members n WHERE n.tenant_id = l.tenant_id AND n.room_id = l.room_id AND n.employee_id = l.employee_id) FOR UPDATE";

    /// <summary>D2 — 거래 안에서 읽는 격리 수준(게이트가 이 값으로 잼).</summary>
    public const string ReadIsolation = "SELECT @@SESSION.transaction_isolation";

    /// <summary>⑤ 끝 — 성공 표시(거래 안 · 갱신 줄 수 1 확인 = G7 · 시각 = 서버 지역 시각 D3).</summary>
    public const string MarkHistorySuccess =
        "UPDATE restore_history SET status = 'success', finished_at = @Now WHERE restore_id = @R AND tenant_id = @t AND status = 'running'";

    // ══ restore_history(지금 값 · 정본) — 시각 = 서버 지역 시각(D3 · A-2① 표지가 지역 시각으로 읽음) ══
    public const string InsertHistoryRunning =
        """
        INSERT INTO restore_history (restore_id, tenant_id, started_at, source_file, status, triggered_by_user)
        VALUES (@R, @t, @Now, @Src, 'running', @U)
        """;

    /// <summary>멈춤 기록 — <c>error_message</c> = DenyCode 글자만(경로 · 예외 글 0 · 응답 문구로 옮기지 않음 = P3-9).</summary>
    public const string MarkHistoryFailed =
        "UPDATE restore_history SET status = 'failed', finished_at = @Now, error_message = @Code WHERE restore_id = @R AND tenant_id = @t AND status = 'running'";

    public const string MarkHistoryPreBackup =
        "UPDATE restore_history SET pre_restore_backup = @Pre WHERE restore_id = @R AND tenant_id = @t";

    /// <summary>이름 꼴 — information_schema 에서 온 이름을 글에 넣기 전 검사.</summary>
    public static readonly Regex PlainName = new("^[A-Za-z0-9_]{1,64}$", RegexOptions.CultureInvariant);
}
