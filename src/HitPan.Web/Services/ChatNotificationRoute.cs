namespace HitPan.Web.Services;

/// <summary>
/// 🔴 20261028작G1-1 (K35) — 알림을 눌렀을 때 「메신저 팝업창으로 열 알림인가」를 판정하는 한 곳.
/// </summary>
/// <remarks>
/// <para>
/// [오더] 대표 10/28 — 새 메시지 알림을 누르면 상단바 메신저 버튼과 <b>같은 팝업창</b>에서 그 대화방이 열린다.
/// 🔴 대표 답(작업지시서 §0-4): 채팅방 결재 안내(<c>chat_approval</c>)는 <b>제외 — 본 창 그대로</b>.
/// ⇒ 판정 = 종류 <c>Type == "chat_message"</c> <b>그리고</b> 링크 <c>/chat/{한 칸}</c> 둘 다 참일 때만.
/// 링크 모양만 보면 <c>chat_approval</c> 도 같은 링크(<c>ChatService.cs</c> <c>Link = $"/chat/{roomId}"</c>)라 팝업으로 새어 나간다.
/// </para>
/// <para>
/// [방 번호 글자 범위] 출하 DDL <c>installer/hitpan_db_clean.sql</c> <c>chat_rooms.room_id varchar(36)</c> ·
/// 방을 만드는 곳은 <c>ChatService.cs</c> 두 군데뿐이고 둘 다 <c>Guid.NewGuid().ToString()</c>(영소문자 16진수 + <c>-</c> · 36자).
/// ⇒ 영문자·숫자·<c>-</c> 만, 1~36자. <c>/</c>·<c>.</c>·<c>?</c>·<c>#</c>·<c>%</c>·공백이 끼면 거짓 — 주소 끼워넣기를 막는다.
/// 방 열람 권한은 지금처럼 <b>서버가 판정</b>한다. 이 함수는 길(팝업/본 창)을 고를 뿐이다.
/// </para>
/// <para>
/// 거짓이면 화면은 기존 <c>Nav.NavigateTo(n.Link)</c> 를 그대로 탄다(결재·기기 알림 · 채팅방 결재 안내).
/// Blazor 비의존 순수 C# — <c>HitPan.Tests</c> 가 <c>Compile Include … Link=</c> 로 끌어가 실제로 불러 잰다(G0).
/// </para>
/// </remarks>
public static class ChatNotificationRoute
{
    /// <summary>팝업으로 여는 알림 종류 — 서버 <c>ChatService.cs</c> 「새 메시지」 알림의 Type.</summary>
    public const string PopupType = "chat_message";

    /// <summary>본 창 대화방 링크 머리. 서버가 보내는 링크 형식(B안 · 서버 0줄).</summary>
    public const string ChatLinkPrefix = "/chat/";

    /// <summary>팝업창 주소 머리 — <c>ChatWindowPage.razor</c> <c>@page "/chat-window/{RoomId}"</c>.</summary>
    public const string PopupPathPrefix = "/chat-window/";

    /// <summary>방 번호 최대 길이 — 출하 DDL <c>chat_rooms.room_id varchar(36)</c>.</summary>
    public const int MaxRoomIdLength = 36;

    /// <summary>
    /// 링크 모양만 본다. <c>/chat/{한 칸}</c> 이고 한 칸이 방 번호 글자 범위 안이면 참.
    /// </summary>
    public static bool TryGetRoomId(string? link, out string roomId)
    {
        roomId = string.Empty;

        if (string.IsNullOrEmpty(link)) return false;
        if (!link.StartsWith(ChatLinkPrefix, StringComparison.Ordinal)) return false;

        var candidate = link.Substring(ChatLinkPrefix.Length);
        if (candidate.Length == 0 || candidate.Length > MaxRoomIdLength) return false;

        foreach (var c in candidate)
        {
            var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-';
            if (!ok) return false;
        }

        roomId = candidate;
        return true;
    }

    /// <summary>
    /// 🔴 화면이 부르는 판정(§0-4). 종류가 <see cref="PopupType"/> <b>그리고</b> 링크가 <c>/chat/{한 칸}</c> 일 때만 참.
    /// </summary>
    public static bool TryGetPopupRoomId(string? type, string? link, out string roomId)
    {
        roomId = string.Empty;

        if (!string.Equals(type, PopupType, StringComparison.Ordinal)) return false;

        return TryGetRoomId(link, out roomId);
    }

    /// <summary>방 번호 → 팝업창 주소. 일반 문자열 결합만(#14).</summary>
    public static string PopupUrl(string roomId) => PopupPathPrefix + roomId;
}
