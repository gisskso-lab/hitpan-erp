using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Chat;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// 사내 메신저 구현. 작(2026-08-13) 그룹웨어 단계9.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>이 클래스는 문서를 만들지 않는다.</b> 사장님(2026-08-13):
/// <i>"연결까지만 해도 충분함"</i> / <i>"있는 기능 연결해서 사용하는게 훨씬 효율적임"</i>
/// </para>
/// <para>
/// 왜 이게 옳은가 — 연차신청 화면은 이미 <b>잔여연차 확인·결재선·이중차감 방지</b>를 한다
/// (6/23 봉합). 메신저에 신청 기능을 또 만들면 그 규칙이 <b>두 벌</b>이 되고,
/// 두 벌은 반드시 갈라진다. 링크로 보내면 규칙이 <b>한 벌</b>이다.
/// </para>
/// <para>
/// 🔴 <b>모든 조회의 첫 줄은 "내가 이 방에 낀 사람인가"</b> 다(<see cref="IsMemberAsync"/>).
/// 부모계정도 예외가 아니다 — 사장님이 <i>"본인 대화만 열람"</i> 을 고르셨다.
/// </para>
/// </remarks>
public sealed class ChatService : IChatService
{
    private readonly IDbConnection _db;
    private readonly IChatFileStore _fileStore;

    /// <summary>
    /// 🔴 <b>새 Hub 를 만들지 않는다.</b> 단계2에서 만든 <c>/hubs/notify</c> 배관이
    /// 이미 <b>사원 단위 · 테넌트 격리 · JWT 그룹 배정</b>까지 돼 있다.
    /// 메신저는 알림 <b>종류만</b> 더한다(헌법 #12 — 인터페이스를 확장하지 않는다).
    /// </summary>
    private readonly INotificationService? _notifications;
    private readonly ILogger<ChatService>? _logger;

    public ChatService(IDbConnection db, IChatFileStore fileStore,
        INotificationService? notifications = null, ILogger<ChatService>? logger = null)
    {
        _db = db;
        _fileStore = fileStore;
        _notifications = notifications;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════
    // 방
    // ═══════════════════════════════════════════════════════════════

    /// <remarks>
    /// 🔴 1:1 방 이름은 <b>저장하지 않고 조회 때 붙인다.</b>
    /// 저장해 두면 상대가 이름을 바꿔도 옛 이름이 남는다.
    /// </remarks>
    public async Task<List<ChatRoomDto>> GetMyRoomsAsync(string tenantId, string employeeId,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql =
            """
            SELECT
              r.room_id   AS RoomId,
              r.room_type AS RoomType,
              CASE r.room_type
                WHEN 'group' THEN COALESCE(r.room_name, '단체 대화')
                WHEN 'dept'  THEN COALESCE(d.dept_name, '부서 대화')
                ELSE COALESCE((
                  SELECT e2.emp_name
                  FROM chat_room_members m2
                  JOIN employees e2 ON e2.employee_id = m2.employee_id AND e2.tenant_id = m2.tenant_id
                  WHERE m2.tenant_id = r.tenant_id AND m2.room_id = r.room_id
                    AND m2.employee_id <> @employeeId
                  LIMIT 1), '(대화 상대 없음)')
              END AS DisplayName,
              (SELECT COUNT(*) FROM chat_room_members mc
                WHERE mc.tenant_id = r.tenant_id AND mc.room_id = r.room_id
                  AND mc.left_at IS NULL) AS MemberCount,
              (SELECT msg.body FROM chat_messages msg
                WHERE msg.tenant_id = r.tenant_id AND msg.room_id = r.room_id
                  AND msg.deleted_at IS NULL
                ORDER BY msg.sent_at DESC LIMIT 1) AS LastMessage,
              (SELECT msg.sent_at FROM chat_messages msg
                WHERE msg.tenant_id = r.tenant_id AND msg.room_id = r.room_id
                  AND msg.deleted_at IS NULL
                ORDER BY msg.sent_at DESC LIMIT 1) AS LastMessageAt,
              (SELECT COUNT(*) FROM chat_messages msg
                WHERE msg.tenant_id = r.tenant_id AND msg.room_id = r.room_id
                  AND msg.deleted_at IS NULL
                  AND msg.sender_id <> @employeeId
                  AND (m.last_read_at IS NULL OR msg.sent_at > m.last_read_at)) AS UnreadCount
            FROM chat_room_members m
            JOIN chat_rooms r       ON r.room_id = m.room_id AND r.tenant_id = m.tenant_id
            LEFT JOIN departments d ON d.dept_id = r.dept_id AND d.tenant_id = r.tenant_id
            WHERE m.tenant_id = @tenantId
              AND m.employee_id = @employeeId
              AND m.left_at IS NULL
            ORDER BY LastMessageAt IS NULL, LastMessageAt DESC, r.created_at DESC
            """;

        var rooms = await _db.QueryAsync<ChatRoomDto>(new CommandDefinition(
            sql, new { tenantId, employeeId }, cancellationToken: ct)).ConfigureAwait(false);

        return rooms.ToList();
    }

    /// <remarks>
    /// 🔴 <b>1:1 은 두 번 생기지 않는다.</b> 사원 ID 두 개를 정렬해 <c>direct_key</c> 로 잡고
    /// UNIQUE 를 걸었다 — A→B 와 B→A 가 같은 키가 된다.
    /// <para>
    /// 🔴 <b>부서방을 자동 생성하지 않는 이유</b>([[feedback_semi_auto_principle]] "무조건 반자동"):
    /// 부서를 만들 때 방을 자동으로 파면 안 쓰는 방이 부서 수만큼 쌓이고,
    /// 부서 개편 때 그 방을 어떻게 할지 아무도 모른다. <b>사람이 만든다.</b>
    /// </para>
    /// </remarks>
    public async Task<string> CreateRoomAsync(string tenantId, string employeeId,
        CreateRoomRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var roomType = (request.RoomType ?? string.Empty).Trim().ToLowerInvariant();
        if (roomType != ChatRoomType.Direct && roomType != ChatRoomType.Dept && roomType != ChatRoomType.Group)
            throw new InvalidOperationException("대화 종류가 올바르지 않습니다.");

        // 초대 대상을 정한다. 🔴 계정 있는 사원만 남긴다.
        var targets = new List<string>();

        if (roomType == ChatRoomType.Dept)
        {
            if (string.IsNullOrWhiteSpace(request.DeptId))
                throw new InvalidOperationException("부서를 선택해 주세요.");

            // 🔴 부서 사원 중 계정 있는 사람만. 부서 전원이 아니다.
            var deptMembers = await _db.QueryAsync<string>(new CommandDefinition(
                """
                SELECT employee_id FROM employees
                WHERE tenant_id = @tenantId AND dept_id = @deptId
                  AND user_id IS NOT NULL AND user_id <> ''
                """,
                new { tenantId, deptId = request.DeptId }, cancellationToken: ct)).ConfigureAwait(false);

            targets.AddRange(deptMembers);
        }
        else
        {
            var requested = (request.EmployeeIds ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            if (requested.Count == 0)
                throw new InvalidOperationException("대화 상대를 선택해 주세요.");

            // 🔴 계정 없는 사원은 걸러낸다(사장님 2026-08-12).
            var allowed = await _db.QueryAsync<string>(new CommandDefinition(
                """
                SELECT employee_id FROM employees
                WHERE tenant_id = @tenantId AND employee_id IN @ids
                  AND user_id IS NOT NULL AND user_id <> ''
                """,
                new { tenantId, ids = requested }, cancellationToken: ct)).ConfigureAwait(false);

            targets.AddRange(allowed);
        }

        // 나를 넣는다.
        if (!targets.Contains(employeeId)) targets.Add(employeeId);
        targets = targets.Distinct().ToList();

        if (roomType == ChatRoomType.Direct && targets.Count != 2)
            throw new InvalidOperationException("1:1 대화는 상대 한 명만 선택할 수 있습니다.");

        if (targets.Count < 2)
            throw new InvalidOperationException("대화할 수 있는 상대가 없습니다. 직원 계정을 먼저 만들어 주세요.");

        // 🔴 1:1 은 이미 있으면 그 방을 돌려준다.
        string? directKey = null;
        if (roomType == ChatRoomType.Direct)
        {
            directKey = BuildDirectKey(targets);

            var existing = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
                "SELECT room_id FROM chat_rooms WHERE tenant_id = @tenantId AND direct_key = @directKey",
                new { tenantId, directKey }, cancellationToken: ct)).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(existing))
            {
                // 예전에 나갔던 사람이 다시 열면 되살린다.
                // 봉합 (2026-08-13, 20260813검2): tenant_id 가 빠져 있었다(헌법 #2).
                await _db.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE chat_room_members SET left_at = NULL
                    WHERE tenant_id = @tenantId AND room_id = @roomId AND employee_id = @employeeId
                    """,
                    new { tenantId, roomId = existing, employeeId }, cancellationToken: ct)).ConfigureAwait(false);
                return existing;
            }
        }

        var roomId = Guid.NewGuid().ToString();

        using var tx = _db.BeginTransaction();
        try
        {
            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO chat_rooms (room_id, tenant_id, room_type, room_name, dept_id, direct_key, created_by)
                VALUES (@roomId, @tenantId, @roomType, @roomName, @deptId, @directKey, @createdBy)
                """,
                new
                {
                    roomId,
                    tenantId,
                    roomType,
                    roomName = roomType == ChatRoomType.Group
                        ? (string.IsNullOrWhiteSpace(request.RoomName) ? "단체 대화" : request.RoomName!.Trim())
                        : null,
                    deptId = roomType == ChatRoomType.Dept ? request.DeptId : null,
                    directKey,
                    createdBy = employeeId
                }, tx, cancellationToken: ct)).ConfigureAwait(false);

            foreach (var target in targets)
            {
                await _db.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO chat_room_members (member_id, tenant_id, room_id, employee_id)
                    VALUES (@memberId, @tenantId, @roomId, @employeeId)
                    """,
                    new { memberId = Guid.NewGuid().ToString(), tenantId, roomId, employeeId = target },
                    tx, cancellationToken: ct)).ConfigureAwait(false);
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        return roomId;
    }

    /// <remarks>🔴 줄을 지우지 않는다 — 나가기 전 대화는 계속 보여야 한다.</remarks>
    public async Task LeaveRoomAsync(string tenantId, string employeeId, string roomId,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE chat_room_members SET left_at = NOW(6)
            WHERE tenant_id = @tenantId AND room_id = @roomId AND employee_id = @employeeId
              AND left_at IS NULL
            """,
            new { tenantId, roomId, employeeId }, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <remarks>
    /// 🔴 <b>모든 접근의 관문.</b> 방 ID 를 주소창에 넣어도 여기서 걸린다.
    /// 부모계정 여부를 보지 않는 것이 의도다 — 사장님이 <i>"본인 대화만 열람"</i> 을 고르셨다.
    /// </remarks>
    public async Task<bool> IsMemberAsync(string tenantId, string employeeId, string roomId,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var count = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM chat_room_members
            WHERE tenant_id = @tenantId AND room_id = @roomId
              AND employee_id = @employeeId AND left_at IS NULL
            """,
            new { tenantId, roomId, employeeId }, cancellationToken: ct)).ConfigureAwait(false);

        return count > 0;
    }

    // ═══════════════════════════════════════════════════════════════
    // 대화
    // ═══════════════════════════════════════════════════════════════

    /// <remarks>
    /// 🔴 <b>읽은 사람 수</b>는 <c>last_read_at</c> 으로 센다(사장님: <i>"몇 명이 읽었는지 = 00"</i>).
    /// 보낸 사람은 뺀다 — 안 그러면 아무도 안 읽어도 <c>1</c> 이 뜬다.
    /// </remarks>
    public async Task<List<ChatMessageDto>> GetMessagesAsync(string tenantId, string employeeId,
        string roomId, int limit, DateTime? before, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        if (!await IsMemberAsync(tenantId, employeeId, roomId, ct).ConfigureAwait(false))
            throw new UnauthorizedAccessException("이 대화를 볼 수 있는 권한이 없습니다.");

        if (limit <= 0 || limit > 200) limit = 50;

        const string sql =
            """
            SELECT
              msg.message_id AS MessageId,
              msg.sender_id  AS SenderId,
              COALESCE(e.emp_name, '(퇴사자)') AS SenderName,
              msg.msg_kind   AS MsgKind,
              msg.body       AS Body,
              msg.ref_type   AS RefType,
              msg.ref_id     AS RefId,
              msg.ref_title  AS RefTitle,
              msg.sent_at    AS SentAt,
              (SELECT COUNT(*) FROM chat_room_members rm
                WHERE rm.tenant_id = msg.tenant_id
                  AND rm.room_id = msg.room_id
                  AND rm.employee_id <> msg.sender_id
                  AND rm.last_read_at IS NOT NULL
                  AND rm.last_read_at >= msg.sent_at) AS ReadCount,
              (msg.sender_id = @employeeId) AS IsMine,
              f.file_id       AS FileId,
              f.original_name AS OriginalName,
              f.file_size     AS FileSize,
              f.content_type  AS ContentType
            FROM chat_messages msg
            LEFT JOIN employees e ON e.employee_id = msg.sender_id AND e.tenant_id = msg.tenant_id
            LEFT JOIN chat_files f ON f.message_id = msg.message_id AND f.tenant_id = msg.tenant_id
            WHERE msg.tenant_id = @tenantId
              AND msg.room_id = @roomId
              AND msg.deleted_at IS NULL
              AND (@before IS NULL OR msg.sent_at < @before)
            ORDER BY msg.sent_at DESC
            LIMIT @limit
            """;

        var rows = await _db.QueryAsync<ChatMessageRow>(new CommandDefinition(
            sql, new { tenantId, roomId, employeeId, before, limit }, cancellationToken: ct))
            .ConfigureAwait(false);

        return rows.Reverse().Select(r => new ChatMessageDto
        {
            MessageId = r.MessageId,
            SenderId = r.SenderId,
            SenderName = r.SenderName,
            MsgKind = r.MsgKind,
            Body = r.Body,
            RefType = r.RefType,
            RefId = r.RefId,
            RefTitle = r.RefTitle,
            SentAt = r.SentAt,
            ReadCount = r.ReadCount,
            IsMine = r.IsMine,
            File = string.IsNullOrEmpty(r.FileId) ? null : new ChatFileDto
            {
                FileId = r.FileId!,
                OriginalName = r.OriginalName ?? string.Empty,
                FileSize = r.FileSize,
                ContentType = r.ContentType ?? string.Empty
            }
        }).ToList();
    }

    /// <remarks>
    /// 🔴 문서를 붙일 때 <b>제목만</b> 가져온다. 본문을 복사하지 않는 이유:
    /// ① 원본이 바뀌면 사본은 틀린 내용이 된다
    /// ② 급여·경비 금액이 대화방에 복사본으로 남으면 <b>권한을 우회한다.</b>
    /// </remarks>
    public async Task<ChatMessageDto> SendMessageAsync(string tenantId, string employeeId,
        string roomId, SendMessageRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        if (!await IsMemberAsync(tenantId, employeeId, roomId, ct).ConfigureAwait(false))
            throw new UnauthorizedAccessException("이 대화에 글을 쓸 수 있는 권한이 없습니다.");

        var body = (request.Body ?? string.Empty).Trim();
        var hasRef = !string.IsNullOrWhiteSpace(request.RefType) && !string.IsNullOrWhiteSpace(request.RefId);

        if (body.Length == 0 && !hasRef)
            throw new InvalidOperationException("보낼 내용을 입력해 주세요.");

        if (body.Length > 2000)
            throw new InvalidOperationException("메시지는 2000자까지 보낼 수 있습니다.");

        string? refTitle = null;
        if (hasRef)
        {
            // 🔴 있는 문서인지 확인하고 제목만 가져온다. 없으면 막는다.
            refTitle = await ResolveDocTitleAsync(tenantId, employeeId,
                request.RefType!, request.RefId!, ct).ConfigureAwait(false);

            if (refTitle is null)
                throw new InvalidOperationException("연결할 문서를 찾을 수 없습니다.");

            if (body.Length == 0)
                body = $"\"{refTitle}\" 를 전송하였습니다.";
        }

        var messageId = Guid.NewGuid().ToString();

        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO chat_messages
              (message_id, tenant_id, room_id, sender_id, msg_kind, body, ref_type, ref_id, ref_title)
            VALUES
              (@messageId, @tenantId, @roomId, @senderId, @msgKind, @body, @refType, @refId, @refTitle)
            """,
            new
            {
                messageId,
                tenantId,
                roomId,
                senderId = employeeId,
                msgKind = ChatKind.Text,
                body,
                refType = hasRef ? request.RefType : null,
                refId = hasRef ? request.RefId : null,
                refTitle
            }, cancellationToken: ct)).ConfigureAwait(false);

        var saved = await LoadOneAsync(tenantId, employeeId, messageId, ct).ConfigureAwait(false);

        await PushToRoomAsync(tenantId, roomId, employeeId, saved.SenderName, body,
            ChatKind.Text, ct).ConfigureAwait(false);

        return saved;
    }

    /// <remarks>
    /// 🔴 <b>알림이 아니라 메시지다.</b> 사장님(2026-08-13):
    /// <i>"그룹웨어 문서 승인 혹은 반려시, 최초 발신인(신청자)에게 메시지 보내야됨"</i> /
    /// <i>"반려되었습니다. 혹은 승인되었습니다."</i>
    /// <para>
    /// 방은 <b>결재자↔신청자 1:1</b>(없으면 연다), 보내는 이는 <b>결재한 사람</b>이다.
    /// 봇 이름으로 가리지 않는 이유 — 반려되면 <b>그 방에서 바로 되물을 수 있어야</b> 한다.
    /// </para>
    /// <para>
    /// 🔴 자기 자신에게는 보내지 않는다(스스로 결재한 경우).
    /// </para>
    /// </remarks>
    public async Task SendApprovalMessageAsync(string tenantId, string actorEmployeeId,
        string targetEmployeeId, string body, string refType, string refId, string refTitle,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(targetEmployeeId) ||
            string.Equals(actorEmployeeId, targetEmployeeId, StringComparison.Ordinal))
            return;

        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 둘 다 계정이 있어야 메신저가 성립한다(사장님 2026-08-12).
        var accountCount = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM employees
            WHERE tenant_id = @tenantId AND employee_id IN @ids
              AND user_id IS NOT NULL AND user_id <> ''
            """,
            new { tenantId, ids = new[] { actorEmployeeId, targetEmployeeId } },
            cancellationToken: ct)).ConfigureAwait(false);

        if (accountCount < 2) return;   // 계정 없는 결재자·기안자는 메신저로 못 받는다.

        var roomId = await EnsureDirectRoomAsync(tenantId, actorEmployeeId, targetEmployeeId, ct)
            .ConfigureAwait(false);

        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO chat_messages
              (message_id, tenant_id, room_id, sender_id, msg_kind, body, ref_type, ref_id, ref_title)
            VALUES
              (@messageId, @tenantId, @roomId, @senderId, @msgKind, @body, @refType, @refId, @refTitle)
            """,
            new
            {
                messageId = Guid.NewGuid().ToString(),
                tenantId,
                roomId,
                senderId = actorEmployeeId,
                msgKind = ChatKind.Approval,     // 🔴 [결재] 딱지의 근거
                body,
                refType,
                refId,
                refTitle
            }, cancellationToken: ct)).ConfigureAwait(false);

        await PushToRoomAsync(tenantId, roomId, actorEmployeeId, null, body,
            ChatKind.Approval, ct).ConfigureAwait(false);
    }

    public async Task MarkReadAsync(string tenantId, string employeeId, string roomId,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE chat_room_members SET last_read_at = NOW(6)
            WHERE tenant_id = @tenantId AND room_id = @roomId AND employee_id = @employeeId
            """,
            new { tenantId, roomId, employeeId }, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <remarks>
    /// 🔴 <b>내가 보낸 것만</b> 숨길 수 있고, 숨겨도 <b>원문은 남는다.</b>
    /// 헌법 #3(원장 INSERT ONLY) 정신과 같은 자리 — 주고받은 기록을 한쪽이 없앨 수 없다.
    /// </remarks>
    public async Task HideMessageAsync(string tenantId, string employeeId, string messageId,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE chat_messages SET deleted_at = NOW(6)
            WHERE tenant_id = @tenantId AND message_id = @messageId
              AND sender_id = @employeeId AND deleted_at IS NULL
            """,
            new { tenantId, messageId, employeeId }, cancellationToken: ct)).ConfigureAwait(false);
    }

    // ═══════════════════════════════════════════════════════════════
    // 상대·문서
    // ═══════════════════════════════════════════════════════════════

    /// <remarks>🔴 계정 없는 사원은 목록에 아예 안 뜬다 — 초대할 수 없다.</remarks>
    public async Task<List<ChatEmployeeDto>> GetChatEmployeesAsync(string tenantId, string employeeId,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var rows = await _db.QueryAsync<ChatEmployeeDto>(new CommandDefinition(
            """
            SELECT e.employee_id AS EmployeeId, e.emp_name AS EmpName,
                   d.dept_name AS DeptName, e.position AS Position
            FROM employees e
            LEFT JOIN departments d ON d.dept_id = e.dept_id AND d.tenant_id = e.tenant_id
            -- 🔴 작(2026-08-14) — 샌드박스 실측 적발: 계정을 **실제로 확인**한다.
            --   종전엔 `e.user_id` 칸이 비었는지만 봤다. 계정이 지워졌거나 아예 없어도
            --   그 값이 남아 있으면 **대화 상대로 떴다.**
            --   말을 걸어도 상대는 로그인을 못 하니 **영영 못 읽는다** — 보낸 사람은
            --   읽씹당한 줄 안다. 없는 사람에게 말 걸 길을 아예 막는다.
            JOIN users u
              ON u.user_id = e.user_id
             AND u.tenant_id = e.tenant_id
             AND u.is_deleted = 0
             AND u.is_active = 1
            WHERE e.tenant_id = @tenantId
              AND e.employee_id <> @employeeId   -- 나 자신은 뺀다(나에게 말 걸 일은 없다)
              AND e.is_active = 1                -- 퇴사자는 상대로 안 뜬다
            ORDER BY d.sort_order, d.dept_name, e.emp_name
            """,
            new { tenantId, employeeId }, cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    /// <remarks>
    /// 🔴 <b>내가 볼 수 있는 것만</b> 나온다. 급여는 본인 것만(단계8 권한과 같은 판정).
    /// 목록에 없으면 붙일 수 없고, 붙여도 상대는 그 화면에서 막힌다.
    /// </remarks>
    public async Task<List<ChatAttachableDocDto>> GetAttachableDocsAsync(string tenantId,
        string employeeId, string refType, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var sql = refType switch
        {
            "approval" =>
                """
                SELECT 'approval' AS RefType, approval_id AS RefId, title AS Title,
                       status AS Status, requested_at AS DocDate
                FROM approval_documents
                WHERE tenant_id = @tenantId AND requester_id = @employeeId
                ORDER BY requested_at DESC LIMIT 50
                """,
            "leave" =>
                """
                SELECT 'leave' AS RefType, request_id AS RefId,
                       CONCAT('휴가 신청 ', DATE_FORMAT(start_date, '%m/%d'), '~', DATE_FORMAT(end_date, '%m/%d')) AS Title,
                       status AS Status, start_date AS DocDate
                FROM leave_requests
                WHERE tenant_id = @tenantId AND employee_id = @employeeId
                ORDER BY start_date DESC LIMIT 50
                """,
            // 🔴 봉합 (2026-08-13, 20260813검2) — 두 가지가 한꺼번에 틀려 있었다:
            //   ① `status` 컬럼이 없다. 실제 이름은 `approval_status`
            //      ⇒ 경비를 고르면 MariaDB 1054 로 **500**. 이 기능은 한 번도 돈 적이 없다.
            //   ② `employee_id` 필터가 빠져 있었다(다른 5개 갈래에는 전부 있다)
            //      ⇒ 일반 직원이 **회사 전체 경비**를 보고, 남의 경비를 대화방에 붙일 수 있었다.
            //        이 목록이 ResolveDocTitleAsync 의 권한 판정도 겸하므로 그대로 뚫린다.
            //   헌법 #13(새 SQL 전 DESCRIBE 의무)을 지키지 않아 난 결함이다.
            "expense" =>
                """
                SELECT 'expense' AS RefType, expense_id AS RefId,
                       COALESCE(description, '경비') AS Title,
                       approval_status AS Status, expense_date AS DocDate
                FROM expenses
                WHERE tenant_id = @tenantId AND employee_id = @employeeId
                ORDER BY expense_date DESC LIMIT 50
                """,
            "payroll" =>
                // 🔴 본인 것만. 단계8 "자기 급여만 본다" 와 같은 판정.
                """
                SELECT 'payroll' AS RefType, slip_id AS RefId,
                       CONCAT(pay_year, '년 ', pay_month, '월 급여명세서') AS Title,
                       status AS Status, pay_date AS DocDate
                FROM payroll_slips
                WHERE tenant_id = @tenantId AND employee_id = @employeeId
                ORDER BY pay_year DESC, pay_month DESC LIMIT 24
                """,
            "contract" =>
                """
                SELECT 'contract' AS RefType, contract_id AS RefId,
                       '근로계약서' AS Title, status AS Status, start_date AS DocDate
                FROM labor_contracts
                WHERE tenant_id = @tenantId AND employee_id = @employeeId
                ORDER BY start_date DESC LIMIT 20
                """,
            // 🔴 봉합 (2026-08-13, 20260813검2): `report_date` 컬럼이 없다.
            //    실제 날짜 칸은 period_start · period_end · submitted_at 이다.
            //    ⇒ 보고서를 고르면 1054 로 **500**. 이 기능도 한 번도 돈 적이 없다.
            "report" =>
                """
                SELECT 'report' AS RefType, report_id AS RefId,
                       COALESCE(title, '업무보고서') AS Title, status AS Status,
                       COALESCE(submitted_at, period_start) AS DocDate
                FROM hr_reports
                WHERE tenant_id = @tenantId AND employee_id = @employeeId
                ORDER BY period_start DESC LIMIT 50
                """,
            _ => null
        };

        if (sql is null) return new List<ChatAttachableDocDto>();

        var rows = await _db.QueryAsync<ChatAttachableDocDto>(new CommandDefinition(
            sql, new { tenantId, employeeId }, cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    // ═══════════════════════════════════════════════════════════════
    // 파일
    // ═══════════════════════════════════════════════════════════════

    /// <remarks>
    /// 🔴 사장님(2026-08-13): <i>"히트판 ERP 데이터양이 많으면 과부화가 올 수 있어.
    /// 파일전송은 최소한으로"</i> — 한도 3중으로 막는다.
    /// <para>
    /// 파일이 쌓이면 디스크만 차는 게 아니라 <b>백업이 느려지고 → 업데이트 전 백업이 실패하고
    /// → 업데이트가 차단된다.</b> 메신저 파일 때문에 ERP 업데이트가 멈추는 것이 최악이다.
    /// </para>
    /// <para>
    /// 🔴 업무 데이터(매입·판매·재고)는 이 한도와 <b>무관하다.</b> 파일만 막힌다.
    /// </para>
    /// </remarks>
    public async Task<ChatMessageDto> SendFileAsync(string tenantId, string employeeId, string roomId,
        string originalName, string contentType, Stream content, long length,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        if (!await IsMemberAsync(tenantId, employeeId, roomId, ct).ConfigureAwait(false))
            throw new UnauthorizedAccessException("이 대화에 파일을 보낼 수 있는 권한이 없습니다.");

        var settings = await GetSettingsAsync(tenantId, ct).ConfigureAwait(false);

        // ① 파일 한 개 한도
        var maxFileBytes = (long)settings.MaxFileMb * 1024 * 1024;
        if (length <= 0)
            throw new InvalidOperationException("빈 파일은 보낼 수 없습니다.");
        if (length > maxFileBytes)
            throw new InvalidOperationException($"파일은 {settings.MaxFileMb}MB 까지 보낼 수 있습니다.");

        // ② 방 한도
        var roomUsed = await _db.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COALESCE(SUM(file_size), 0) FROM chat_files WHERE tenant_id = @tenantId AND room_id = @roomId",
            new { tenantId, roomId }, cancellationToken: ct)).ConfigureAwait(false);

        if (roomUsed + length > (long)settings.MaxRoomMb * 1024 * 1024)
            throw new InvalidOperationException("이 대화방의 파일이 가득 찼습니다. 오래된 파일을 정리해 주세요.");

        // ③ 회사 전체 한도
        var tenantUsed = await _db.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COALESCE(SUM(file_size), 0) FROM chat_files WHERE tenant_id = @tenantId",
            new { tenantId }, cancellationToken: ct)).ConfigureAwait(false);

        if (tenantUsed + length > (long)settings.MaxTenantMb * 1024 * 1024)
            throw new InvalidOperationException("회사 전체 메신저 파일 용량이 가득 찼습니다. 설정에서 확인해 주세요.");

        // 🔴 확장자·시그니처 검사는 파일 저장소가 한다(실행파일 차단).
        var stored = await _fileStore.SaveAsync(tenantId, originalName, content, ct).ConfigureAwait(false);

        var messageId = Guid.NewGuid().ToString();

        using var tx = _db.BeginTransaction();
        try
        {
            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO chat_messages
                  (message_id, tenant_id, room_id, sender_id, msg_kind, body)
                VALUES
                  (@messageId, @tenantId, @roomId, @senderId, @msgKind, @body)
                """,
                new
                {
                    messageId, tenantId, roomId, senderId = employeeId,
                    msgKind = ChatKind.File,
                    body = originalName
                }, tx, cancellationToken: ct)).ConfigureAwait(false);

            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO chat_files
                  (file_id, tenant_id, room_id, message_id, original_name, stored_path,
                   content_type, file_size, uploaded_by)
                VALUES
                  (@fileId, @tenantId, @roomId, @messageId, @originalName, @storedPath,
                   @contentType, @fileSize, @uploadedBy)
                """,
                new
                {
                    fileId = stored.FileId, tenantId, roomId, messageId,
                    originalName, storedPath = stored.RelativePath,
                    contentType = string.IsNullOrWhiteSpace(contentType)
                        ? "application/octet-stream" : contentType,
                    fileSize = length, uploadedBy = employeeId
                }, tx, cancellationToken: ct)).ConfigureAwait(false);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            _fileStore.TryDelete(stored.RelativePath);   // 저장은 됐는데 DB 가 실패하면 파일이 남는다.
            throw;
        }

        var saved = await LoadOneAsync(tenantId, employeeId, messageId, ct).ConfigureAwait(false);

        await PushToRoomAsync(tenantId, roomId, employeeId, saved.SenderName,
            $"파일: {originalName}", ChatKind.File, ct).ConfigureAwait(false);

        return saved;
    }

    /// <remarks>
    /// 🔴 <b>주소를 알아도 그 방에 낀 사람이 아니면 못 받는다.</b>
    /// 업로드만 막고 내려받기를 열어두면 "본인 대화만 열람"이 통째로 무너진다.
    /// </remarks>
    public async Task<(Stream Content, string FileName, string ContentType)?> DownloadFileAsync(
        string tenantId, string employeeId, string fileId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var row = await _db.QueryFirstOrDefaultAsync<ChatFileRow>(new CommandDefinition(
            """
            SELECT room_id AS RoomId, original_name AS OriginalName,
                   stored_path AS StoredPath, content_type AS ContentType
            FROM chat_files
            WHERE tenant_id = @tenantId AND file_id = @fileId
            """,
            new { tenantId, fileId }, cancellationToken: ct)).ConfigureAwait(false);

        if (row is null) return null;

        // 🔴 방 참여 검사 — 여기가 핵심이다.
        if (!await IsMemberAsync(tenantId, employeeId, row.RoomId, ct).ConfigureAwait(false))
            throw new UnauthorizedAccessException("이 파일을 받을 수 있는 권한이 없습니다.");

        var stream = _fileStore.OpenRead(row.StoredPath);
        if (stream is null) return null;

        return (stream, row.OriginalName, row.ContentType);
    }

    /// <remarks>🔴 자동 삭제하지 않는다(사장님 결재) — 차오르기 전에 보이게만 한다.</remarks>
    public async Task<ChatStorageDto> GetStorageAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var settings = await GetSettingsAsync(tenantId, ct).ConfigureAwait(false);

        var used = await _db.QueryFirstOrDefaultAsync<(long Bytes, int Count)>(new CommandDefinition(
            "SELECT COALESCE(SUM(file_size),0) AS Bytes, COUNT(*) AS Count FROM chat_files WHERE tenant_id = @tenantId",
            new { tenantId }, cancellationToken: ct)).ConfigureAwait(false);

        return new ChatStorageDto
        {
            UsedBytes = used.Bytes,
            FileCount = used.Count,
            MaxFileMb = settings.MaxFileMb,
            MaxRoomMb = settings.MaxRoomMb,
            MaxTenantMb = settings.MaxTenantMb
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // 안쪽
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 방에 있는 <b>나 말고 다른 사람들</b> 에게 실시간으로 알린다. 작(2026-08-13) 단계9.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>새 Hub 를 만들지 않는다</b> — 단계2의 <c>/hubs/notify</c> 를 그대로 쓴다.
    /// 그 배관은 이미 <b>연결 시 서버가 JWT 로 그룹을 정하므로</b>
    /// 남의 사원 ID 를 넣어 남의 메시지를 받아볼 수 없다.
    /// </para>
    /// <para>
    /// 🔴 <b>본문을 그대로 싣지 않는다.</b> 알림은 화면에 떠 있는 동안 <b>옆 사람에게도 보인다</b>
    /// (<c>AppNotification</c> 주석과 같은 판단). 미리보기는 짧게 자르고,
    /// 자세한 내용은 눌러서 방에서 본다.
    /// </para>
    /// <para>
    /// 🔴 <b>알림 실패가 메시지 전송을 막으면 안 된다.</b> 메시지는 이미 저장됐다.
    /// 구현체가 예외를 삼키고 로그만 남기지만(헌법 #15), 여기서도 한 겹 더 감싼다 —
    /// 알림 서비스가 아예 없을 수도 있기 때문이다(시험 환경).
    /// </para>
    /// </remarks>
    private async Task PushToRoomAsync(string tenantId, string roomId, string senderId,
        string? senderName, string preview, string kind, CancellationToken ct)
    {
        if (_notifications is null) return;

        try
        {
            var receivers = await _db.QueryAsync<string>(new CommandDefinition(
                """
                SELECT employee_id FROM chat_room_members
                WHERE tenant_id = @tenantId AND room_id = @roomId
                  AND employee_id <> @senderId AND left_at IS NULL
                """,
                new { tenantId, roomId, senderId }, cancellationToken: ct)).ConfigureAwait(false);

            var title = kind == ChatKind.Approval ? "결재 안내" : "새 메시지";
            var body = string.IsNullOrWhiteSpace(senderName)
                ? Shorten(preview)
                : $"{senderName}: {Shorten(preview)}";

            foreach (var receiver in receivers)
            {
                await _notifications.NotifyEmployeeAsync(tenantId, receiver, new AppNotification
                {
                    Type = kind == ChatKind.Approval ? "chat_approval" : "chat_message",
                    Title = title,
                    Body = body,
                    Link = $"/chat/{roomId}"
                }, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // 메시지는 이미 저장됐다. 알림이 안 갔다고 되돌리면 그게 더 큰 사고다.
            _logger?.LogWarning(ex,
                "메신저 실시간 알림 실패 — 메시지는 저장됨 (방 {RoomId})", roomId);
        }
    }

    /// <summary>알림에 실을 미리보기. 🔴 옆 사람에게도 보이므로 짧게 자른다.</summary>
    private static string Shorten(string text)
    {
        var oneLine = text.Replace('\n', ' ').Trim();
        return oneLine.Length <= 40 ? oneLine : oneLine[..40] + "…";
    }

    /// <summary>1:1 방 키 — 사원 ID 를 정렬해 붙인다. A→B 와 B→A 가 같아야 한다.</summary>
    private static string BuildDirectKey(IEnumerable<string> employeeIds)
    {
        var ordered = employeeIds.OrderBy(x => x, StringComparer.Ordinal).ToList();
        return string.Join("|", ordered);
    }

    /// <summary>1:1 방을 찾고, 없으면 만든다(결재 메시지가 갈 자리).</summary>
    private async Task<string> EnsureDirectRoomAsync(string tenantId, string a, string b,
        CancellationToken ct)
    {
        var directKey = BuildDirectKey(new[] { a, b });

        var existing = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            "SELECT room_id FROM chat_rooms WHERE tenant_id = @tenantId AND direct_key = @directKey",
            new { tenantId, directKey }, cancellationToken: ct)).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(existing))
        {
            // 🔴 봉합 (2026-08-13, 20260813검2): 종전에 조건이 `room_id` 하나뿐이라
            //    **그 방 사람 전부**의 left_at 을 지웠다. 나가기로 한 사람이
            //    남의 결재 때문에 말없이 다시 끌려 들어온다. tenant_id 도 빠져 있었다.
            //    ⇒ 결재를 받아야 할 **두 사람만** 되살린다.
            await _db.ExecuteAsync(new CommandDefinition(
                """
                UPDATE chat_room_members SET left_at = NULL
                WHERE tenant_id = @tenantId AND room_id = @roomId
                  AND employee_id IN @ids AND left_at IS NOT NULL
                """,
                new { tenantId, roomId = existing, ids = new[] { a, b } },
                cancellationToken: ct)).ConfigureAwait(false);
            return existing;
        }

        var roomId = Guid.NewGuid().ToString();

        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO chat_rooms (room_id, tenant_id, room_type, direct_key, created_by)
            VALUES (@roomId, @tenantId, 'direct', @directKey, @createdBy)
            """,
            new { roomId, tenantId, directKey, createdBy = a }, cancellationToken: ct)).ConfigureAwait(false);

        foreach (var member in new[] { a, b })
        {
            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO chat_room_members (member_id, tenant_id, room_id, employee_id)
                VALUES (@memberId, @tenantId, @roomId, @employeeId)
                """,
                new { memberId = Guid.NewGuid().ToString(), tenantId, roomId, employeeId = member },
                cancellationToken: ct)).ConfigureAwait(false);
        }

        return roomId;
    }

    /// <summary>
    /// 붙일 문서의 제목을 가져온다. 🔴 <b>없으면 null</b> — 없는 문서는 붙일 수 없다.
    /// </summary>
    private async Task<string?> ResolveDocTitleAsync(string tenantId, string employeeId,
        string refType, string refId, CancellationToken ct)
    {
        var docs = await GetAttachableDocsAsync(tenantId, employeeId, refType, ct).ConfigureAwait(false);
        return docs.FirstOrDefault(d => string.Equals(d.RefId, refId, StringComparison.Ordinal))?.Title;
    }

    private async Task<ChatMessageDto> LoadOneAsync(string tenantId, string employeeId,
        string messageId, CancellationToken ct)
    {
        var row = await _db.QueryFirstAsync<ChatMessageRow>(new CommandDefinition(
            """
            SELECT
              msg.message_id AS MessageId, msg.sender_id AS SenderId,
              COALESCE(e.emp_name, '(퇴사자)') AS SenderName,
              msg.msg_kind AS MsgKind, msg.body AS Body,
              msg.ref_type AS RefType, msg.ref_id AS RefId, msg.ref_title AS RefTitle,
              msg.sent_at AS SentAt, 0 AS ReadCount,
              (msg.sender_id = @employeeId) AS IsMine,
              f.file_id AS FileId, f.original_name AS OriginalName,
              f.file_size AS FileSize, f.content_type AS ContentType
            FROM chat_messages msg
            LEFT JOIN employees e ON e.employee_id = msg.sender_id AND e.tenant_id = msg.tenant_id
            LEFT JOIN chat_files f ON f.message_id = msg.message_id AND f.tenant_id = msg.tenant_id
            WHERE msg.tenant_id = @tenantId AND msg.message_id = @messageId
            """,
            new { tenantId, messageId, employeeId }, cancellationToken: ct)).ConfigureAwait(false);

        return new ChatMessageDto
        {
            MessageId = row.MessageId,
            SenderId = row.SenderId,
            SenderName = row.SenderName,
            MsgKind = row.MsgKind,
            Body = row.Body,
            RefType = row.RefType,
            RefId = row.RefId,
            RefTitle = row.RefTitle,
            SentAt = row.SentAt,
            ReadCount = row.ReadCount,
            IsMine = row.IsMine,
            File = string.IsNullOrEmpty(row.FileId) ? null : new ChatFileDto
            {
                FileId = row.FileId!,
                OriginalName = row.OriginalName ?? string.Empty,
                FileSize = row.FileSize,
                ContentType = row.ContentType ?? string.Empty
            }
        };
    }

    /// <summary>
    /// 한도 설정. 🔴 코드 상수가 아니라 <b>설정 테이블</b>에서 읽는다
    /// ([[feedback_semi_auto_principle]] — <i>"기준값은 코드상수 금지"</i>).
    /// 없으면 기본값으로 한 줄 만든다.
    /// </summary>
    private async Task<ChatFileSettings> GetSettingsAsync(string tenantId, CancellationToken ct)
    {
        var settings = await _db.QueryFirstOrDefaultAsync<ChatFileSettings>(new CommandDefinition(
            """
            SELECT max_file_mb AS MaxFileMb, max_room_mb AS MaxRoomMb, max_tenant_mb AS MaxTenantMb
            FROM chat_file_settings WHERE tenant_id = @tenantId
            """,
            new { tenantId }, cancellationToken: ct)).ConfigureAwait(false);

        if (settings is not null) return settings;

        await _db.ExecuteAsync(new CommandDefinition(
            "INSERT IGNORE INTO chat_file_settings (tenant_id) VALUES (@tenantId)",
            new { tenantId }, cancellationToken: ct)).ConfigureAwait(false);

        // 🔴 봉합 (2026-08-13, 20260813검2): 종전에 `new ChatFileSettings()` 를 돌려줘
        //    **코드에 적힌 20/500/5120 을 그대로 썼다.** INSERT IGNORE 는 이미 줄이 있으면
        //    아무 일도 안 하므로, 그 줄에 고객사가 정한 다른 값이 들어 있어도 무시된다
        //    ⇒ 5MB 로 정해둔 회사에 20MB 가 올라간다.
        //    사장님 기조 그대로다 — **고객사가 넣은 값을 받는다.** 코드 값은 첫 줄을 만들 때만 쓴다.
        var ensured = await _db.QueryFirstOrDefaultAsync<ChatFileSettings>(new CommandDefinition(
            """
            SELECT max_file_mb AS MaxFileMb, max_room_mb AS MaxRoomMb, max_tenant_mb AS MaxTenantMb
            FROM chat_file_settings WHERE tenant_id = @tenantId
            """,
            new { tenantId }, cancellationToken: ct)).ConfigureAwait(false);

        return ensured ?? new ChatFileSettings();
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open) return;
        if (_db is DbConnection dbConnection)
        {
            await dbConnection.OpenAsync(ct).ConfigureAwait(false);
            return;
        }
        _db.Open();
    }

    // Dapper 매핑용 평면 행.
    private sealed class ChatMessageRow
    {
        public string MessageId { get; init; } = string.Empty;
        public string SenderId { get; init; } = string.Empty;
        public string SenderName { get; init; } = string.Empty;
        public string MsgKind { get; init; } = ChatKind.Text;
        public string Body { get; init; } = string.Empty;
        public string? RefType { get; init; }
        public string? RefId { get; init; }
        public string? RefTitle { get; init; }
        public DateTime SentAt { get; init; }
        public int ReadCount { get; init; }
        public bool IsMine { get; init; }
        public string? FileId { get; init; }
        public string? OriginalName { get; init; }
        public long FileSize { get; init; }
        public string? ContentType { get; init; }
    }

    private sealed class ChatFileRow
    {
        public string RoomId { get; init; } = string.Empty;
        public string OriginalName { get; init; } = string.Empty;
        public string StoredPath { get; init; } = string.Empty;
        public string ContentType { get; init; } = string.Empty;
    }

    private sealed class ChatFileSettings
    {
        public int MaxFileMb { get; init; } = 20;      // 사장님 확정
        public int MaxRoomMb { get; init; } = 500;
        public int MaxTenantMb { get; init; } = 5120;  // 5GB
    }
}
