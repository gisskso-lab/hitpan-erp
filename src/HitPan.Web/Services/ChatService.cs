using System.Net;
using System.Net.Http.Json;
using HitPan.Web.Models;
using Microsoft.Extensions.Logging;

namespace HitPan.Web.Services;

/// <summary>
/// 사내 메신저 클라이언트. 작(2026-08-13) 그룹웨어 단계9.
/// </summary>
/// <remarks>
/// 🔴 실패는 <c>null</c> 로 돌린다. 빈 목록이 아니다 —
/// 실패를 빈 목록으로 뭉개면 화면이 <b>"대화가 없다"</b> 로 보여준다.
/// 상대는 보냈는데 안 왔다고 보이면 <b>업무가 끊긴다</b>(헌법 #20).
/// </remarks>
public sealed class ChatService(HttpClient http, ILogger<ChatService> logger)
{
    // ── 방 ──

    public async Task<List<ChatRoomModel>?> GetRoomsAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<ChatRoomModel>>("api/chat/rooms", ct)
                .ConfigureAwait(false) ?? new List<ChatRoomModel>();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "대화 목록 조회 실패");
            return null;
        }
    }

    /// <summary>방을 만든다. 실패하면 사유를 돌려준다(부서 0건·계정 없음 등).</summary>
    public async Task<(string? RoomId, string? Error)> CreateRoomAsync(CreateChatRoomModel model,
        CancellationToken ct = default)
    {
        try
        {
            var response = await http.PostAsJsonAsync("api/chat/rooms", model, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return (null, await ReadErrorAsync(response, ct).ConfigureAwait(false));

            var created = await response.Content.ReadFromJsonAsync<CreateRoomResponse>(ct)
                .ConfigureAwait(false);
            return (created?.RoomId, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "대화방 만들기 실패");
            return (null, "대화방을 만들지 못했습니다.");
        }
    }

    public async Task<bool> LeaveRoomAsync(string roomId, CancellationToken ct = default)
    {
        try
        {
            var response = await http.PostAsync($"api/chat/rooms/{roomId}/leave", null, ct)
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "대화방 나가기 실패 ({RoomId})", roomId);
            return false;
        }
    }

    // ── 대화 ──

    public async Task<List<ChatMessageModel>?> GetMessagesAsync(string roomId, int limit = 50,
        CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<ChatMessageModel>>(
                $"api/chat/rooms/{roomId}/messages?limit={limit}", ct).ConfigureAwait(false)
                ?? new List<ChatMessageModel>();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "대화 읽기 실패 ({RoomId})", roomId);
            return null;
        }
    }

    public async Task<(ChatMessageModel? Message, string? Error)> SendMessageAsync(string roomId,
        SendChatMessageModel model, CancellationToken ct = default)
    {
        try
        {
            var response = await http.PostAsJsonAsync($"api/chat/rooms/{roomId}/messages", model, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return (null, await ReadErrorAsync(response, ct).ConfigureAwait(false));

            var sent = await response.Content.ReadFromJsonAsync<ChatMessageModel>(ct).ConfigureAwait(false);
            return (sent, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "메시지 보내기 실패 ({RoomId})", roomId);
            return (null, "메시지를 보내지 못했습니다.");
        }
    }

    /// <summary>여기까지 읽었다고 알린다.</summary>
    public async Task MarkReadAsync(string roomId, CancellationToken ct = default)
    {
        try
        {
            await http.PostAsync($"api/chat/rooms/{roomId}/read", null, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 읽음 표시 실패는 업무를 막지 않는다(헌법 #15 — 로그는 남긴다).
            logger.LogWarning(ex, "읽음 표시 실패 ({RoomId})", roomId);
        }
    }

    public async Task<bool> HideMessageAsync(string messageId, CancellationToken ct = default)
    {
        try
        {
            var response = await http.DeleteAsync($"api/chat/messages/{messageId}", ct)
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "메시지 숨기기 실패 ({MessageId})", messageId);
            return false;
        }
    }

    // ── 상대·문서 ──

    public async Task<List<ChatEmployeeModel>?> GetEmployeesAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<ChatEmployeeModel>>("api/chat/employees", ct)
                .ConfigureAwait(false) ?? new List<ChatEmployeeModel>();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "대화 상대 조회 실패");
            return null;
        }
    }

    /// <summary>🔴 붙일 수 있는 문서. <b>고르기만</b> 한다 — 여기서 만들지 않는다.</summary>
    public async Task<List<ChatAttachableDocModel>?> GetDocumentsAsync(string refType,
        CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<ChatAttachableDocModel>>(
                $"api/chat/documents?refType={Uri.EscapeDataString(refType)}", ct).ConfigureAwait(false)
                ?? new List<ChatAttachableDocModel>();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "연결할 문서 조회 실패 ({RefType})", refType);
            return null;
        }
    }

    // ── 파일 ──

    /// <summary>파일 보내기. 🔴 20MB·실행파일 판정은 서버가 한다.</summary>
    public async Task<(ChatMessageModel? Message, string? Error)> SendFileAsync(string roomId,
        Stream content, string fileName, string contentType, long length,
        CancellationToken ct = default)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            var fileContent = new StreamContent(content);
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                fileContent.Headers.TryAddWithoutValidation("Content-Type", contentType);
            }
            form.Add(fileContent, "file", fileName);

            var response = await http.PostAsync($"api/chat/rooms/{roomId}/files", form, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return (null, await ReadErrorAsync(response, ct).ConfigureAwait(false));

            var sent = await response.Content.ReadFromJsonAsync<ChatMessageModel>(ct).ConfigureAwait(false);
            return (sent, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "파일 보내기 실패 ({RoomId}, {FileName})", roomId, fileName);
            return (null, "파일을 보내지 못했습니다.");
        }
    }

    /// <summary>
    /// 안 읽은 메시지 수. 상단바 배지가 쓴다. 작(2026-08-13).
    /// </summary>
    /// <remarks>
    /// 🔴 실패해도 <b>0</b> 을 돌린다. 여기만 <c>null</c> 이 아니다 —
    /// 상단바는 <b>모든 화면에</b> 뜨므로, 실패를 알리면 화면마다 오류가 뜬다.
    /// 배지가 잠깐 안 보이는 것이 화면마다 경고가 뜨는 것보다 낫다.
    /// </remarks>
    public async Task<int> GetUnreadCountAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await http.GetFromJsonAsync<UnreadCountResponse>("api/chat/unread-count", ct)
                .ConfigureAwait(false);
            return result?.Count ?? 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "안 읽은 메시지 수 조회 실패");
            return 0;
        }
    }

    public async Task<ChatStorageModel?> GetStorageAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<ChatStorageModel>("api/chat/storage", ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "메신저 파일 사용량 조회 실패");
            return null;
        }
    }

    // ── 안쪽 ──

    /// <summary>서버가 준 사유를 그대로 보여준다. 개발용어 없이 온다.</summary>
    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden)
            return "권한이 없습니다.";

        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ErrorResponse>(ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(problem?.Message)) return problem!.Message!;
        }
        catch (Exception)
        {
            // 본문이 JSON 이 아닐 수 있다. 아래 기본 문구로 간다.
        }

        return "처리하지 못했습니다. 잠시 후 다시 시도해 주세요.";
    }

    private sealed class CreateRoomResponse
    {
        public string? RoomId { get; set; }
    }

    private sealed class ErrorResponse
    {
        public string? Message { get; set; }
    }

    private sealed class UnreadCountResponse
    {
        public int Count { get; set; }
    }
}
