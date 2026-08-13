using HitPan.Application.DTOs.Chat;

namespace HitPan.Application.Interfaces;

/// <summary>
/// 사내 메신저. 작(2026-08-13) 그룹웨어 단계9.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>메신저가 하는 일은 셋뿐이다</b> — ① 있는 문서를 고른다 ② 링크를 보낸다 ③ 누르면 이동한다.
/// 문서 <b>생성</b>·결재 <b>상신</b>·<b>승인/반려</b> 는 하지 않는다. 사장님(2026-08-13):
/// <i>"메신저 채팅창에서 연차신청서 생성해서 만들고 결재까지 기능을 넣을 필요까진 없음.
/// 연결까지만 해도 충분함"</i> / <i>"있는 기능 연결해서 사용하는게 훨씬 효율적임"</i>
/// </para>
/// <para>
/// 🔴 <b>열람은 본인 대화만.</b> 부모계정(사장)도 예외가 아니다 —
/// 급여(단계8)와 반대인 것이 맞다. 급여는 <b>줘야 하니까</b> 사장이 다 본다.
/// 대화는 <b>줄 것이 없다.</b> 사장이 다 보면 아무도 안 쓴다.
/// </para>
/// <para>
/// 🔴 <b>참여자는 계정 있는 사원만</b>(사장님 2026-08-12:
/// <i>"메신저는 계정이 있는 사원에게만 권한을. 물리적으로 그렇게 될 수밖에 없으니"</i>).
/// 근태·휴가·경비·급여·계약은 계정 없어도 전부 된다(대행) — 막지 않는다는 뜻은
/// <b>업무에서 배제하지 않는다</b>이지, 로그인이 필요한 기능까지 준다는 뜻이 아니다.
/// </para>
/// </remarks>
public interface IChatService
{
    // ─── 방 ────────────────────────────────────────────────────────

    /// <summary>내가 낀 방 목록. 🔴 안 낀 방은 애초에 나오지 않는다.</summary>
    Task<List<ChatRoomDto>> GetMyRoomsAsync(string tenantId, string employeeId,
        CancellationToken ct = default);

    /// <summary>
    /// 방 만들기. 1:1 은 이미 있으면 <b>그 방을 돌려준다</b>(A→B 와 B→A 가 같은 방).
    /// </summary>
    Task<string> CreateRoomAsync(string tenantId, string employeeId,
        CreateRoomRequest request, CancellationToken ct = default);

    /// <summary>방을 나간다. 🔴 줄을 지우지 않고 <c>left_at</c> 만 찍는다 — 나가기 전 대화는 계속 보인다.</summary>
    Task LeaveRoomAsync(string tenantId, string employeeId, string roomId,
        CancellationToken ct = default);

    /// <summary>🔴 내가 이 방에 낀 사람인가. 모든 접근의 관문.</summary>
    Task<bool> IsMemberAsync(string tenantId, string employeeId, string roomId,
        CancellationToken ct = default);

    // ─── 대화 ──────────────────────────────────────────────────────

    /// <summary>대화 읽기. 🔴 내가 숨긴 메시지는 빠지고, 상대 화면에는 그대로 있다.</summary>
    Task<List<ChatMessageDto>> GetMessagesAsync(string tenantId, string employeeId,
        string roomId, int limit, DateTime? before, CancellationToken ct = default);

    /// <summary>메시지 보내기. 문서를 붙이면 <b>제목만</b> 저장한다(내용 복사 금지).</summary>
    Task<ChatMessageDto> SendMessageAsync(string tenantId, string employeeId,
        string roomId, SendMessageRequest request, CancellationToken ct = default);

    /// <summary>
    /// 결재 결과를 신청자에게 <b>메시지로</b> 보낸다. 사장님(2026-08-13):
    /// <i>"그룹웨어 문서 승인 혹은 반려시, 최초 발신인(신청자)에게 메시지 보내야됨"</i>
    /// <para>
    /// 🔴 알림(종)이 아니라 <b>메시지</b>다 — 대화방에 남고 읽음이 찍힌다.
    /// 보내는 이는 <b>결재한 사람 이름</b>이고, 방은 <b>결재자↔신청자 1:1</b>(없으면 연다).
    /// </para>
    /// </summary>
    Task SendApprovalMessageAsync(string tenantId, string actorEmployeeId,
        string targetEmployeeId, string body, string refType, string refId, string refTitle,
        CancellationToken ct = default);

    /// <summary>여기까지 읽었다. 🔴 읽음의 유일한 근거(<c>last_read_at</c>).</summary>
    Task MarkReadAsync(string tenantId, string employeeId, string roomId,
        CancellationToken ct = default);

    /// <summary>내 화면에서만 숨긴다. 🔴 원문은 남고 상대 화면에도 그대로 있다.</summary>
    Task HideMessageAsync(string tenantId, string employeeId, string messageId,
        CancellationToken ct = default);

    // ─── 상대·문서 ─────────────────────────────────────────────────

    /// <summary>대화 상대 후보. 🔴 <c>employees.user_id IS NOT NULL</c> 인 사원만.</summary>
    Task<List<ChatEmployeeDto>> GetChatEmployeesAsync(string tenantId, string employeeId,
        CancellationToken ct = default);

    /// <summary>
    /// 붙일 수 있는 문서. 🔴 <b>내가 볼 수 있는 것만</b> 나온다 — 남의 급여는 목록에 없다.
    /// </summary>
    Task<List<ChatAttachableDocDto>> GetAttachableDocsAsync(string tenantId, string employeeId,
        string refType, CancellationToken ct = default);

    // ─── 파일 ──────────────────────────────────────────────────────

    /// <summary>
    /// 파일을 보낸다. 🔴 한도·확장자·시그니처를 모두 통과해야 저장된다.
    /// 사장님: <i>"파일전송은 최소한으로"</i> — 파일이 ERP 를 넘어뜨리면 안 된다.
    /// </summary>
    Task<ChatMessageDto> SendFileAsync(string tenantId, string employeeId, string roomId,
        string originalName, string contentType, Stream content, long length,
        CancellationToken ct = default);

    /// <summary>
    /// 파일 내려받기. 🔴 <b>주소를 알아도 그 방에 낀 사람이 아니면 못 받는다</b> —
    /// 업로드만 막고 내려받기를 열어두면 "본인 대화만 열람"이 무너진다.
    /// </summary>
    Task<(Stream Content, string FileName, string ContentType)?> DownloadFileAsync(
        string tenantId, string employeeId, string fileId, CancellationToken ct = default);

    /// <summary>파일 사용량. 🔴 자동 삭제하지 않고 <b>보여주기만</b> 한다(사장님 결재).</summary>
    Task<ChatStorageDto> GetStorageAsync(string tenantId, CancellationToken ct = default);
}
