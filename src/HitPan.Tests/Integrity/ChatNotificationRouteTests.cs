using HitPan.Web.Services;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261028작G1-1 (K35) G0 — 알림 → 메신저 팝업 판정을 <b>실제로 불러</b> 잰다(글자 검사 아님 · 동작).
/// </summary>
/// <remarks>
/// <para>
/// 화면 <c>NotificationBell.razor</c> 는 <see cref="ChatNotificationRoute.TryGetPopupRoomId"/> 가 참일 때만 팝업,
/// 거짓이면 기존 <c>Nav.NavigateTo(n.Link)</c> 를 탄다. 그 판정 하나를 여기서 잰다.
/// </para>
/// <para>
/// 🔴 대표 답(작업지시서 §0-4) — <c>chat_approval</c>(채팅방 결재 안내)은 같은 <c>/chat/{방}</c> 링크라도 <b>본 창</b>.
/// ⇒ 대조군: 판정을 「늘 참」으로 바꾸면 거짓 목록 전부 빨강 · 「종류 무시(링크만)」로 바꾸면
/// <c>chat_approval + /chat/R1</c> 줄이 빨강 · 「늘 거짓」이면 참 목록이 빨강. (무력화 실측은 개발명세서 §5)
/// </para>
/// </remarks>
public class ChatNotificationRouteTests
{
    // ── 참: 새 메시지 알림 + /chat/{한 칸} ──────────────────────────────────
    [Theory]
    [InlineData("/chat/R1", "R1")]
    [InlineData("/chat/3f2c9a1e-7b4d-4c2a-9e1f-0a1b2c3d4e5f", "3f2c9a1e-7b4d-4c2a-9e1f-0a1b2c3d4e5f")] // 실제 방 번호 꼴(Guid · 36자)
    public void 새메시지_채팅링크는_팝업_방번호를_돌린다(string link, string expectedRoomId)
    {
        var ok = ChatNotificationRoute.TryGetPopupRoomId("chat_message", link, out var roomId);

        Assert.True(ok);
        Assert.Equal(expectedRoomId, roomId);
        Assert.Equal("/chat-window/" + expectedRoomId, ChatNotificationRoute.PopupUrl(roomId));
    }

    // ── 거짓: §4-2 6종 + §0-4 chat_approval + 끼워넣기·경계 ─────────────────
    [Theory]
    // §4-2 6종 (종류는 chat_message 로 두고 링크만 틀리게 — 링크 판정이 살아 있는지)
    [InlineData("chat_message", "/chat")]
    [InlineData("chat_message", "/chat/a/b")]
    [InlineData("chat_message", "/approval/pending")]
    [InlineData("chat_message", "/settings/devices")]
    [InlineData("chat_message", "")]
    [InlineData("chat_message", null)]
    // 🔴 §0-4 대표 답 — 채팅방 결재 안내는 같은 링크라도 본 창
    [InlineData("chat_approval", "/chat/R1")]
    // 종류가 비거나 다른 알림
    [InlineData(null, "/chat/R1")]
    [InlineData("", "/chat/R1")]
    [InlineData("approval", "/chat/R1")]
    [InlineData("CHAT_MESSAGE", "/chat/R1")]
    // 주소 끼워넣기·경계 ([3-V] 관점)
    [InlineData("chat_message", "/chat/")]
    [InlineData("chat_message", "/chat/R1?x=1")]
    [InlineData("chat_message", "/chat/R1#a")]
    [InlineData("chat_message", "/chat/R%2F1")]
    [InlineData("chat_message", "/chat/R 1")]
    [InlineData("chat_message", "/chat/R1/")]
    [InlineData("chat_message", "//evil.example/chat/R1")]
    [InlineData("chat_message", "https://evil.example/chat/R1")]
    // [3-V] C1 — 보안상무 지정 3종
    [InlineData("chat_message", "//evil/chat/x")]
    [InlineData("chat_message", "http://x/chat/R1")]
    [InlineData("chat_message", "/chat/../x")]
    [InlineData("chat_message", "/chat-window/R1")]
    [InlineData("chat_message", "/Chat/R1")]
    [InlineData("chat_message", "/chat/3f2c9a1e-7b4d-4c2a-9e1f-0a1b2c3d4e5f0")] // 37자 — DDL varchar(36) 초과
    public void 팝업이_아닌_알림은_거짓_본창_그대로(string? type, string? link)
    {
        var ok = ChatNotificationRoute.TryGetPopupRoomId(type, link, out var roomId);

        Assert.False(ok);
        Assert.Equal(string.Empty, roomId);
    }

    // ── 링크 모양 판정 단독 — chat_approval 거짓이 「링크가 틀려서」가 아니라 「종류 때문」임을 대조 ──
    [Fact]
    public void 결재안내_링크_자체는_채팅링크다_거짓은_종류_때문()
    {
        Assert.True(ChatNotificationRoute.TryGetRoomId("/chat/R1", out var roomId));
        Assert.Equal("R1", roomId);
        Assert.False(ChatNotificationRoute.TryGetPopupRoomId("chat_approval", "/chat/R1", out _));
    }
}
