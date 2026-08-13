using System.Security.Claims;
using HitPan.Application.DTOs.Chat;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 사내 메신저 API. 작(2026-08-13) 그룹웨어 단계9.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>여기에 문서를 만들거나 결재하는 주소가 없는 것은 의도다.</b> 사장님(2026-08-13):
/// <i>"메신저 채팅창에서 연차신청서 생성해서 만들고 결재까지 기능을 넣을 필요까진 없음.
/// 연결까지만 해도 충분함"</i> / <i>"있는 기능 연결해서 사용하는게 훨씬 효율적임"</i>
/// </para>
/// <para>
/// 🔴 <b><c>[RequirePermission]</c> 을 걸지 않는다.</b> 급여(단계8)와 다른 이유다 —
/// 메신저는 <b>전 직원이 쓰는 물건</b>이고, 권한으로 막으면 직원이 자기 대화도 못 본다.
/// 대신 <b>방 참여 여부</b>로 판정한다(<c>IsMemberAsync</c>) — 안 낀 방은 아예 안 보인다.
/// </para>
/// <para>
/// 🔴 <b>부모계정도 남의 1:1 을 못 본다.</b> 사장님이 <i>"본인 대화만 열람"</i> 을 고르셨다.
/// 급여는 <b>줘야 하니까</b> 사장이 다 보지만, 대화는 <b>줄 것이 없다.</b>
/// </para>
/// </remarks>
[ApiController]
[Route("api/chat")]
[Authorize]
public sealed class ChatController : ControllerBase
{
    private readonly IChatService _service;
    private readonly ICurrentTenant _currentTenant;
    private readonly ILogger<ChatController> _logger;

    public ChatController(IChatService service, ICurrentTenant currentTenant,
        ILogger<ChatController> logger)
    {
        _service = service;
        _currentTenant = currentTenant;
        _logger = logger;
    }

    // ─── 방 ────────────────────────────────────────────────────────

    /// <summary>내 방 목록.</summary>
    [HttpGet("rooms")]
    public async Task<ActionResult<List<ChatRoomDto>>> GetRooms(CancellationToken ct)
    {
        var me = await ResolveMeAsync(ct);
        if (me is null) return NoAccountResult();

        return Ok(await _service.GetMyRoomsAsync(_currentTenant.TenantId, me, ct));
    }

    /// <summary>
    /// 방 만들기. 🔴 부서가 0건이면 부서방만 못 만든다 —
    /// 사장님: <i>"부서는 고객사가 설정할 일"</i>. 1:1·단체는 부서와 무관하게 된다.
    /// </summary>
    [HttpPost("rooms")]
    public async Task<ActionResult<object>> CreateRoom([FromBody] CreateRoomRequest request,
        CancellationToken ct)
    {
        var me = await ResolveMeAsync(ct);
        if (me is null) return NoAccountResult();

        try
        {
            var roomId = await _service.CreateRoomAsync(_currentTenant.TenantId, me, request, ct);
            return Ok(new { roomId });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>방 나가기.</summary>
    [HttpPost("rooms/{roomId}/leave")]
    public async Task<IActionResult> LeaveRoom(string roomId, CancellationToken ct)
    {
        var me = await ResolveMeAsync(ct);
        if (me is null) return NoAccountResult();

        await _service.LeaveRoomAsync(_currentTenant.TenantId, me, roomId, ct);
        return NoContent();
    }

    // ─── 대화 ──────────────────────────────────────────────────────

    /// <summary>대화 읽기.</summary>
    [HttpGet("rooms/{roomId}/messages")]
    public async Task<ActionResult<List<ChatMessageDto>>> GetMessages(string roomId,
        [FromQuery] int limit = 50, [FromQuery] DateTime? before = null,
        CancellationToken ct = default)
    {
        var me = await ResolveMeAsync(ct);
        if (me is null) return NoAccountResult();

        try
        {
            return Ok(await _service.GetMessagesAsync(_currentTenant.TenantId, me, roomId, limit, before, ct));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
    }

    /// <summary>메시지 보내기.</summary>
    [HttpPost("rooms/{roomId}/messages")]
    public async Task<ActionResult<ChatMessageDto>> SendMessage(string roomId,
        [FromBody] SendMessageRequest request, CancellationToken ct)
    {
        var me = await ResolveMeAsync(ct);
        if (me is null) return NoAccountResult();

        try
        {
            return Ok(await _service.SendMessageAsync(_currentTenant.TenantId, me, roomId, request, ct));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>여기까지 읽었다.</summary>
    [HttpPost("rooms/{roomId}/read")]
    public async Task<IActionResult> MarkRead(string roomId, CancellationToken ct)
    {
        var me = await ResolveMeAsync(ct);
        if (me is null) return NoAccountResult();

        await _service.MarkReadAsync(_currentTenant.TenantId, me, roomId, ct);
        return NoContent();
    }

    /// <summary>내 화면에서만 숨긴다. 🔴 상대 화면에는 그대로 있다.</summary>
    [HttpDelete("messages/{messageId}")]
    public async Task<IActionResult> HideMessage(string messageId, CancellationToken ct)
    {
        var me = await ResolveMeAsync(ct);
        if (me is null) return NoAccountResult();

        await _service.HideMessageAsync(_currentTenant.TenantId, me, messageId, ct);
        return NoContent();
    }

    // ─── 상대·문서 ─────────────────────────────────────────────────

    /// <summary>대화 상대 후보. 🔴 계정 있는 사원만.</summary>
    [HttpGet("employees")]
    public async Task<ActionResult<List<ChatEmployeeDto>>> GetEmployees(CancellationToken ct)
    {
        var me = await ResolveMeAsync(ct);
        if (me is null) return NoAccountResult();

        return Ok(await _service.GetChatEmployeesAsync(_currentTenant.TenantId, me, ct));
    }

    /// <summary>
    /// 붙일 수 있는 문서. 🔴 <b>고르기만</b> 한다 — 여기서 만들지 않는다.
    /// </summary>
    [HttpGet("documents")]
    public async Task<ActionResult<List<ChatAttachableDocDto>>> GetDocuments(
        [FromQuery] string refType, CancellationToken ct)
    {
        var me = await ResolveMeAsync(ct);
        if (me is null) return NoAccountResult();

        return Ok(await _service.GetAttachableDocsAsync(_currentTenant.TenantId, me, refType, ct));
    }

    // ─── 파일 ──────────────────────────────────────────────────────

    /// <summary>
    /// 파일 보내기. 🔴 20MB · 실행파일 차단 · 3중 한도.
    /// 사장님: <i>"파일전송은 최소한으로"</i>
    /// </summary>
    [HttpPost("rooms/{roomId}/files")]
    [RequestSizeLimit(25 * 1024 * 1024)]   // 20MB + 폼 여유. 서비스가 실제 한도를 판정한다.
    public async Task<ActionResult<ChatMessageDto>> SendFile(string roomId,
        IFormFile file, CancellationToken ct)
    {
        var me = await ResolveMeAsync(ct);
        if (me is null) return NoAccountResult();

        if (file is null || file.Length == 0)
            return BadRequest(new { message = "보낼 파일을 선택해 주세요." });

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _service.SendFileAsync(_currentTenant.TenantId, me, roomId,
                file.FileName, file.ContentType, stream, file.Length, ct);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 파일 내려받기. 🔴 <b>주소를 알아도 그 방에 낀 사람이 아니면 못 받는다.</b>
    /// </summary>
    [HttpGet("files/{fileId}")]
    public async Task<IActionResult> DownloadFile(string fileId, CancellationToken ct)
    {
        var me = await ResolveMeAsync(ct);
        if (me is null) return NoAccountResult();

        try
        {
            var result = await _service.DownloadFileAsync(_currentTenant.TenantId, me, fileId, ct);
            if (result is null) return NotFound(new { message = "파일을 찾을 수 없습니다." });

            return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
    }

    /// <summary>파일 사용량. 🔴 지우지 않고 보여주기만 한다.</summary>
    [HttpGet("storage")]
    public async Task<ActionResult<ChatStorageDto>> GetStorage(CancellationToken ct)
        => Ok(await _service.GetStorageAsync(_currentTenant.TenantId, ct));

    /// <summary>
    /// 안 읽은 메시지 수. 상단바 <b>메신저 버튼의 숫자 배지</b>가 쓴다. 작(2026-08-13).
    /// </summary>
    /// <remarks>
    /// 🔴 사장님(2026-08-13): <i>"메신저 알림아이콘 옆 메신저 버튼 생성.
    /// 메뉴에 기능이 있어야 하는건 맞지만, <b>메뉴에서만 찾는건 비효율적</b>"</i>
    /// <para>
    /// 계정이 사원으로 안 이어진 사람은 <b>0</b> 을 준다 — 막지 않는다.
    /// 상단바는 모든 화면에 뜨므로 여기서 403 을 내면 <b>화면마다 오류가 뜬다.</b>
    /// </para>
    /// </remarks>
    [HttpGet("unread-count")]
    public async Task<ActionResult<object>> GetUnreadCount(CancellationToken ct)
    {
        var me = await ResolveMeAsync(ct);
        if (me is null) return Ok(new { count = 0 });

        var rooms = await _service.GetMyRoomsAsync(_currentTenant.TenantId, me, ct);
        return Ok(new { count = rooms.Sum(r => r.UnreadCount) });
    }

    // ─── 안쪽 ──────────────────────────────────────────────────────

    /// <summary>
    /// 로그인한 사람의 <b>사원 ID</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 <c>user_id</c> 가 아니라 <c>employee_id</c> 다. <b>둘은 별개 GUID</b> 이고
    /// AuthService 가 두 클레임을 따로 발급한다(단계8 급여와 같은 축을 쓴다 —
    /// 기준이 갈리면 결재 메시지가 엉뚱한 사람에게 간다).
    /// <para>
    /// 클레임이 비어 있으면 <b>계정은 있는데 사원으로 안 이어진 것</b>이다.
    /// 사장님(2026-08-12): <i>"메신저는 계정이 있는 사원에게만 권한을"</i>
    /// </para>
    /// </remarks>
    private Task<string?> ResolveMeAsync(CancellationToken ct)
    {
        _ = ct;
        var employeeId = User.FindFirstValue("employee_id");
        return Task.FromResult(string.IsNullOrWhiteSpace(employeeId) ? null : employeeId);
    }

    /// <summary>
    /// 🔴 계정은 있는데 사원으로 안 이어진 경우. 사장님(2026-08-12):
    /// <i>"메신저는 계정이 있는 사원에게만 권한을"</i> — 안내 문구를 준다.
    /// </summary>
    private ActionResult NoAccountResult()
    {
        _logger.LogWarning("메신저 접근 — 사원으로 이어지지 않은 계정 {UserId}", _currentTenant.UserId);
        return StatusCode(StatusCodes.Status403Forbidden,
            new { message = "사원 정보와 연결된 계정만 메신저를 쓸 수 있습니다. 관리자에게 문의해 주세요." });
    }
}
