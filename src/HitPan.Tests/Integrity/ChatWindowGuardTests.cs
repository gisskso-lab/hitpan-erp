using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(2026-08-14) 🔴 <b>메신저는 진짜 창이어야 한다.</b>
/// </summary>
/// <remarks>
/// <para>
/// 사장님 지시: <i>"메신저를 별도 팝업창으로 구현하자고 했는데, 사장의 요구대로 안됨."</i>
/// <i>"pc카카오톡 사이즈로 팝업을 띄워 채팅운영을 할 것!!!"</i>
/// </para>
/// <para>
/// 종전 구현은 화면 안 오른쪽 아래에 붙는 <c>&lt;div class="hitpan-chat-popup"&gt;</c> 였다.
/// 코드·주석에는 "팝업" 이라 써 있었지만 <c>window.open</c> 이 <b>한 줄도 없었다</b> —
/// <b>말만 팝업</b>이었고 ERP 화면을 가려 멀티태스킹이 안 됐다.
/// </para>
/// <para>
/// 🔴 이 시험은 <b>"팝업이라 부르는가" 가 아니라 "진짜 창을 여는가"</b> 를 본다.
/// 이름만으로 통과시키면 같은 사고가 그대로 재발한다.
/// </para>
/// </remarks>
public class ChatWindowGuardTests
{
    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.True(dir is not null && Directory.Exists(Path.Combine(dir, "src")),
            "레포 루트를 찾아야 한다");
        return dir!;
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray()));

    /// <summary>
    /// 🔴 <b>진짜 브라우저 창</b>을 여는 코드가 있어야 한다.
    /// </summary>
    [Fact]
    public void 메신저는_진짜_창으로_열린다()
    {
        var js = ReadSource("src", "HitPan.Web", "wwwroot", "js", "hitpan-chat-window.js");

        Assert.Contains("window.open", js);

        // 그 파일이 실제로 실려야 한다 — 만들어 놓고 index.html 에 안 걸면 안 돈다.
        var index = ReadSource("src", "HitPan.Web", "wwwroot", "index.html");
        Assert.Contains("hitpan-chat-window.js", index);

        // 상단바 버튼이 그것을 부른다.
        var header = ReadSource("src", "HitPan.Web", "Layout", "TopHeader.razor");
        Assert.Contains("hitpanChat.open", header);
    }

    /// <summary>
    /// 팝업 차단을 <b>조용히 넘기지 않는다.</b>
    /// </summary>
    /// <remarks>
    /// 브라우저가 막으면 <c>window.open</c> 이 <c>null</c> 을 준다. 그걸 무시하면
    /// 사용자는 <b>"눌렀는데 아무 일도 안 난다"</b> 만 겪는다 — 되는 척의 한 형태다.
    /// </remarks>
    [Fact]
    public void 팝업이_막히면_사실대로_알린다()
    {
        var js = ReadSource("src", "HitPan.Web", "wwwroot", "js", "hitpan-chat-window.js");
        var header = ReadSource("src", "HitPan.Web", "Layout", "TopHeader.razor");

        // JS 가 성공/실패를 돌려줘야 한다.
        Assert.Contains("return false", js);

        // 화면이 그 실패를 받아 안내해야 한다.
        Assert.Contains("InvokeAsync<bool>", header);
        Assert.Contains("팝업", header);
    }

    /// <summary>
    /// 팝업창 화면은 <b>메뉴 없이</b> 떠야 한다.
    /// </summary>
    /// <remarks>
    /// 400px 창에 사이드바·상단바가 같이 뜨면 <b>대화가 안 보인다.</b>
    /// </remarks>
    [Fact]
    public void 팝업창_화면은_메뉴없이_뜬다()
    {
        var page = ReadSource("src", "HitPan.Web", "Pages", "HR", "ChatWindowPage.razor");

        Assert.Contains("@page \"/chat-window\"", page);
        Assert.Contains("@layout EmptyLayout", page);

        // 🔴 대화 기능을 옮겨 적지 않고 본체 화면을 그대로 얹어야 한다.
        //    옮겨 적으면 한쪽만 고쳐져 두 화면이 갈라진다.
        Assert.Contains("<ChatPage", page);
    }

    /// <summary>
    /// 🔴 새 말이 오면 <b>새로 온 것만</b> 붙여야 한다.
    /// </summary>
    /// <remarks>
    /// 사장님: <i>"채팅이 너무 느림."</i> 종전엔 알림 한 번에 메시지 30개와 방 목록을
    /// 통째로 다시 읽었다. 한 줄 보여주려고 전부 다시 읽은 것이 느림의 원인이었다.
    /// </remarks>
    [Fact]
    public void 새_메시지만_붙인다()
    {
        var page = ReadSource("src", "HitPan.Web", "Pages", "HR", "ChatPage.razor");

        // 실시간 갱신 구독이 있어야 한다(종전엔 아예 없어 새로고침해야 보였다).
        Assert.Contains("Notify.OnNotification +=", page);
        Assert.Contains("IDisposable", page);

        // 이미 있는 것은 걸러내고 새 것만 더한다.
        Assert.Contains("AppendNewMessagesAsync", page);
        Assert.Contains("_messages.AddRange", page);
    }
}
