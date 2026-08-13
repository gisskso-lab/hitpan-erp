namespace HitPan.Application.DTOs.Chat;

/// <summary>
/// 사내 메신저 DTO. 작(2026-08-13) 그룹웨어 단계9.
/// </summary>
/// <remarks>
/// 🔴 <b>메신저는 길만 놓는다.</b> 사장님(2026-08-13):
/// <i>"그룹웨어 문서 전송은 그룹웨어에 이미 있는 기능들 연결 정도만 해도 됨"</i> /
/// <i>"메신저 채팅창에서 연차신청서 생성해서 만들고 결재까지 기능을 넣을 필요까진 없음.
/// 연결까지만 해도 충분함"</i> / <i>"있는 기능 연결해서 사용하는게 훨씬 효율적임"</i>
///
/// ⇒ 문서를 <b>만들거나 결재하는 DTO 가 여기에 없는 것은 의도다.</b>
///   연차신청 화면은 이미 잔여연차 확인·결재선·이중차감 방지를 한다(6/23 봉합).
///   메신저에 또 만들면 규칙이 두 벌이 되고, 두 벌은 반드시 갈라진다.
/// </remarks>
public static class ChatKind
{
    /// <summary>사람이 쓴 말. 화면에 <c>[메시지]</c> 로 뜬다.</summary>
    public const string Text = "text";

    /// <summary>결재 안내(상신·승인·반려). 화면에 <c>[결재]</c> 로 뜬다.</summary>
    public const string Approval = "approval";

    /// <summary>파일.</summary>
    public const string File = "file";
}

/// <summary>방 종류.</summary>
public static class ChatRoomType
{
    /// <summary>1:1 — 두 사람. A→B 와 B→A 가 같은 방이다.</summary>
    public const string Direct = "direct";

    /// <summary>부서방 — 그 부서 사원 중 <b>계정 있는 사람</b>.</summary>
    public const string Dept = "dept";

    /// <summary>단체방 — 고른 사람들. 이름을 직접 입력한다.</summary>
    public const string Group = "group";
}

/// <summary>내 방 목록 한 줄.</summary>
public sealed class ChatRoomDto
{
    public string RoomId { get; init; } = string.Empty;
    public string RoomType { get; init; } = string.Empty;

    /// <summary>
    /// 화면에 뜨는 이름. 단체방은 저장된 이름, 부서방은 부서명,
    /// 🔴 1:1 은 <b>상대 이름</b>(저장하지 않고 조회 때 붙인다 — 이름이 바뀌면 따라가야 한다).
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    public int MemberCount { get; init; }

    /// <summary>마지막 메시지 미리보기. 없으면 빈 방.</summary>
    public string? LastMessage { get; init; }

    public DateTime? LastMessageAt { get; init; }

    /// <summary>내가 아직 안 읽은 수.</summary>
    public int UnreadCount { get; init; }
}

/// <summary>대화 한 줄.</summary>
public sealed class ChatMessageDto
{
    public string MessageId { get; init; } = string.Empty;
    public string SenderId { get; init; } = string.Empty;
    public string SenderName { get; init; } = string.Empty;

    /// <summary>
    /// 🔴 딱지의 근거 — <c>text</c>(메시지) · <c>approval</c>(결재) · <c>file</c>(파일).
    /// 사장님: <i>"결재봇, 메시지봇 공유해도 될듯. 인스타나 페이스북 처럼.
    /// 다만 메시지인지, 결재안내인지는 안내"</i>
    /// </summary>
    public string MsgKind { get; init; } = ChatKind.Text;

    public string Body { get; init; } = string.Empty;

    /// <summary>연결 문서 종류. 누르면 갈 곳을 정한다.</summary>
    public string? RefType { get; init; }

    public string? RefId { get; init; }

    /// <summary>🔴 <b>제목만.</b> 문서 내용을 여기에 복사하지 않는다(§4-2).</summary>
    public string? RefTitle { get; init; }

    public DateTime SentAt { get; init; }

    /// <summary>
    /// 🔴 <b>읽은 사람 수.</b> 사장님: <i>"단체·부서방은 몇 명이 읽었는지 = 00"</i>
    /// 안 읽은 수가 아니다. 보낸 사람은 뺀다.
    /// </summary>
    public int ReadCount { get; init; }

    /// <summary>내가 보낸 것인가. 화면 좌우를 가른다.</summary>
    public bool IsMine { get; init; }

    /// <summary>파일이면 채워진다.</summary>
    public ChatFileDto? File { get; init; }
}

/// <summary>보낸 파일.</summary>
public sealed class ChatFileDto
{
    public string FileId { get; init; } = string.Empty;

    /// <summary>보여주기용 이름. 🔴 저장 경로에는 쓰지 않는다.</summary>
    public string OriginalName { get; init; } = string.Empty;

    public long FileSize { get; init; }
    public string ContentType { get; init; } = string.Empty;
}

/// <summary>대화 상대 후보 — 🔴 계정 있는 사원만.</summary>
public sealed class ChatEmployeeDto
{
    public string EmployeeId { get; init; } = string.Empty;
    public string EmpName { get; init; } = string.Empty;
    public string? DeptName { get; init; }
    public string? Position { get; init; }
}

/// <summary>방 만들기.</summary>
public sealed class CreateRoomRequest
{
    /// <summary><c>direct</c> · <c>dept</c> · <c>group</c>.</summary>
    public string RoomType { get; set; } = string.Empty;

    /// <summary>단체방 이름. 단체방만 쓴다.</summary>
    public string? RoomName { get; set; }

    /// <summary>부서방일 때 부서. 🔴 부서가 0건이면 부서방을 못 만든다(정상).</summary>
    public string? DeptId { get; set; }

    /// <summary>초대할 사원. 1:1 은 1명, 단체는 여럿. 부서방은 비워도 부서에서 채운다.</summary>
    public List<string> EmployeeIds { get; set; } = new();
}

/// <summary>메시지 보내기.</summary>
public sealed class SendMessageRequest
{
    public string Body { get; set; } = string.Empty;

    /// <summary>문서를 붙일 때만. 🔴 <b>있는 문서를 고르는 것</b>이지 만드는 게 아니다.</summary>
    public string? RefType { get; set; }

    public string? RefId { get; set; }
}

/// <summary>붙일 수 있는 문서 한 줄 — 내가 볼 수 있는 것만 나온다.</summary>
public sealed class ChatAttachableDocDto
{
    public string RefType { get; init; } = string.Empty;
    public string RefId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Status { get; init; }
    public DateTime? DocDate { get; init; }
}

/// <summary>파일 사용량 — 🔴 지우지 않고 보여주기만 한다(사장님 결재).</summary>
public sealed class ChatStorageDto
{
    public long UsedBytes { get; init; }
    public int MaxTenantMb { get; init; }
    public int MaxRoomMb { get; init; }
    public int MaxFileMb { get; init; }
    public int FileCount { get; init; }
}
