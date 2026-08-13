namespace HitPan.Web.Models;

/// <summary>
/// 사내 메신저 화면 모델. 작(2026-08-13) 그룹웨어 단계9.
/// </summary>
/// <remarks>
/// 🔴 문서를 <b>만들거나 결재하는 모델이 없는 것은 의도다</b>. 사장님(2026-08-13):
/// <i>"연결까지만 해도 충분함"</i> — 메신저는 길만 놓는다.
/// </remarks>
public sealed class ChatRoomModel
{
    public string RoomId { get; set; } = string.Empty;
    public string RoomType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int MemberCount { get; set; }
    public string? LastMessage { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public int UnreadCount { get; set; }
}

public sealed class ChatMessageModel
{
    public string MessageId { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;

    /// <summary>🔴 딱지 — <c>text</c>(메시지) · <c>approval</c>(결재) · <c>file</c>(파일).</summary>
    public string MsgKind { get; set; } = "text";

    public string Body { get; set; } = string.Empty;
    public string? RefType { get; set; }
    public string? RefId { get; set; }
    public string? RefTitle { get; set; }
    public DateTime SentAt { get; set; }

    /// <summary>🔴 <b>읽은</b> 사람 수(사장님: "몇 명이 읽었는지 = 00"). 안 읽은 수가 아니다.</summary>
    public int ReadCount { get; set; }

    public bool IsMine { get; set; }
    public ChatFileModel? File { get; set; }
}

public sealed class ChatFileModel
{
    public string FileId { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string ContentType { get; set; } = string.Empty;
}

public sealed class ChatEmployeeModel
{
    public string EmployeeId { get; set; } = string.Empty;
    public string EmpName { get; set; } = string.Empty;
    public string? DeptName { get; set; }
    public string? Position { get; set; }
}

public sealed class ChatAttachableDocModel
{
    public string RefType { get; set; } = string.Empty;
    public string RefId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Status { get; set; }
    public DateTime? DocDate { get; set; }
}

public sealed class ChatStorageModel
{
    public long UsedBytes { get; set; }
    public int MaxTenantMb { get; set; }
    public int MaxRoomMb { get; set; }
    public int MaxFileMb { get; set; }
    public int FileCount { get; set; }
}

public sealed class CreateChatRoomModel
{
    public string RoomType { get; set; } = "direct";
    public string? RoomName { get; set; }
    public string? DeptId { get; set; }
    public List<string> EmployeeIds { get; set; } = new();
}

public sealed class SendChatMessageModel
{
    public string Body { get; set; } = string.Empty;
    public string? RefType { get; set; }
    public string? RefId { get; set; }
}
