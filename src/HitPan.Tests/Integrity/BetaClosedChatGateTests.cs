using System.Security.Claims;
using HitPan.API.Controllers;
using HitPan.Application.DTOs.Chat;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-S-04 (2026-11-28 · 작업지시서 20261128작 §2 · §5 · 설계 §5) — ERP 채팅 서버 문 닫힘을 잰다. DB 불필요.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>G-S4-1</b> — MVC 실제 탐색(닫힌 2 경로 · 대조군 5 경로)은 같은 장치 <see cref="BetaClosedServerGateTests"/> 목록에 있다.</item>
/// <item><b>G-S4-2</b> — 19·06-9 이전 대화 더 보기: <see cref="ChatController"/> 를 「부르면 던지는」 서비스 가짜로 만들어
/// <c>before</c> 가 오면 계정 확인 · 서비스보다 먼저 404 빈 본문 · 서비스 호출 0 / 대조군(<c>before</c> 없음)은 지금과 같은 길.</item>
/// <item><b>G-S4-3</b> — 웹(<c>src/HitPan.Web</c>)이 닫힌 두 웹 함수를 부르지 않고 대화 읽기 주소에 <c>before</c> 를 붙이지 않는다.
/// 🔴 글자 대조라 우회 꼴(다른 이름으로 감싸 부르기 · 주소를 조각내 잇기)은 못 잡는다 — 한계는 개발명세서에 적었다.</item>
/// </list>
/// 변이 대조(빼면 빨강)는 개발명세서 <c>docs/개발/erp/20261128_W-S-04_서버문닫기_채팅_개발명세서.md</c> 에 런 기록.
/// </remarks>
public sealed class BetaClosedChatGateTests
{
    private const string Tenant = "t-gate";
    private const string Employee = "e-gate";
    private const string Room = "r-gate";

    /// <summary>
    /// 컨트롤러를 가짜로 만든다. 서비스 · 테넌트 둘 다 Strict — 설정하지 않은 것을 부르면 던진다.
    /// <paramref name="employeeId"/> 가 null 이면 「사원으로 안 이어진 계정」(클레임 없음).
    /// </summary>
    private static ChatController NewController(Mock<IChatService> service, Mock<ICurrentTenant> tenant, string? employeeId)
    {
        var claims = new List<Claim>();
        if (employeeId is not null)
        {
            claims.Add(new Claim("employee_id", employeeId));
        }

        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "gate"));
        return new ChatController(service.Object, tenant.Object, NullLogger<ChatController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } },
        };
    }

    // ─────────────────────────────────────────────────────────────
    //  G-S4-2 — 06-9 `before` 거절(컨트롤러 동작)
    // ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "G-S4-2-1 before 가 오면 404 빈 본문 · 서비스 호출 0 · 테넌트 읽기 0")]
    public async Task G_S4_2_1_before_오면_404_서비스_0()
    {
        var service = new Mock<IChatService>(MockBehavior.Strict);
        var tenant = new Mock<ICurrentTenant>(MockBehavior.Strict);
        var controller = NewController(service, tenant, Employee);

        var result = await controller.GetMessages(Room, 50, new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc));

        var notFound = Assert.IsType<NotFoundResult>(result.Result);   // NotFoundObjectResult(본문 있음)이 아니다
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
        Assert.Null(result.Value);
        Assert.True(service.Invocations.Count == 0,
            $"G-S4-2 before 가 왔는데 서비스를 {service.Invocations.Count} 번 불렀다 — 06-9 거절 줄이 빠졌거나 서비스 뒤로 밀렸다(W-S-04).");
        Assert.Empty(tenant.Invocations);
    }

    [Fact(DisplayName = "G-S4-2-2 사원 클레임 없는 계정 + before → 역시 404(거절이 계정 확인보다 먼저)")]
    public async Task G_S4_2_2_계정확인보다_먼저_거절()
    {
        var service = new Mock<IChatService>(MockBehavior.Strict);
        var tenant = new Mock<ICurrentTenant>(MockBehavior.Strict);   // 계정 안내 갈래로 가면 UserId 를 읽다가 던진다
        var controller = NewController(service, tenant, employeeId: null);

        var result = await controller.GetMessages(Room, 50, DateTime.MinValue);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Empty(service.Invocations);
        Assert.Empty(tenant.Invocations);
    }

    [Fact(DisplayName = "G-S4-2-3 대조군: before 없음 → 서비스 1회(before = null) · 200")]
    public async Task G_S4_2_3_대조군_before_없음은_지금과_같은_길()
    {
        var expected = new List<ChatMessageDto>();
        var service = new Mock<IChatService>(MockBehavior.Strict);
        service.Setup(s => s.GetMessagesAsync(Tenant, Employee, Room, 30, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var tenant = new Mock<ICurrentTenant>(MockBehavior.Strict);
        tenant.SetupGet(t => t.TenantId).Returns(Tenant);
        var controller = NewController(service, tenant, Employee);

        var result = await controller.GetMessages(Room, 30);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode ?? StatusCodes.Status200OK);
        Assert.Same(expected, ok.Value);
        service.Verify(s => s.GetMessagesAsync(Tenant, Employee, Room, 30, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "G-S4-2-4 대조군: 사원 클레임 없음 + before 없음 → 지금처럼 403 안내 · 서비스 호출 0")]
    public async Task G_S4_2_4_대조군_계정안내는_그대로()
    {
        var service = new Mock<IChatService>(MockBehavior.Strict);
        var tenant = new Mock<ICurrentTenant>(MockBehavior.Strict);
        tenant.SetupGet(t => t.UserId).Returns("u-gate");
        var controller = NewController(service, tenant, employeeId: null);

        var result = await controller.GetMessages(Room);

        var forbidden = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.Empty(service.Invocations);
    }

    // ─────────────────────────────────────────────────────────────
    //  G-S4-3 — 웹이 닫힌 함수를 부르지 않는다(글자 · 한계 기록)
    // ─────────────────────────────────────────────────────────────

    private static string WebRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Join(dir.FullName, "src", "HitPan.Web")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "레포 뿌리(src/HitPan.Web)를 찾지 못했다");
        return Path.Join(dir!.FullName, "src", "HitPan.Web");
    }

    /// <summary>웹 소스 줄(.cs · .razor · bin · obj 뺌 · 주석 줄 뺌)을 「파일:줄번호 내용」 으로 모은다.</summary>
    private static List<(string Where, string Line)> WebCodeLines()
    {
        var root = WebRoot();
        var sep = Path.DirectorySeparatorChar;
        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{sep}bin{sep}", StringComparison.OrdinalIgnoreCase)
                     && !f.Contains($"{sep}obj{sep}", StringComparison.OrdinalIgnoreCase));

        var lines = new List<(string, string)>();
        foreach (var file in files)
        {
            var all = File.ReadAllLines(file);
            for (var i = 0; i < all.Length; i++)
            {
                var t = all[i].TrimStart();
                if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal)
                    || t.StartsWith("/*", StringComparison.Ordinal) || t.StartsWith("@*", StringComparison.Ordinal))
                {
                    continue;
                }

                lines.Add(($"{Path.GetRelativePath(root, file)}:{i + 1}", all[i]));
            }
        }

        return lines;
    }

    [Fact(DisplayName = "G-S4-3-0 웹 소스 훑기가 실제로 돈다(파일 다수 · 대화 읽기 주소 줄 있음)")]
    public void G_S4_3_0_훑기가_돈다()
    {
        var lines = WebCodeLines();
        Assert.True(lines.Select(l => l.Where.Split(':')[0]).Distinct().Count() >= 50, "G-S4-3 웹 소스 파일을 거의 못 읽었다 — 훑기가 안 돈다.");
        Assert.Contains(lines, l => l.Line.Contains("api/chat/rooms/{roomId}/messages?limit=", StringComparison.Ordinal));
    }

    [Theory(DisplayName = "G-S4-3-1 웹이 닫힌 웹 함수를 정의 줄 밖에서 부르지 않는다")]
    [InlineData("HideMessageAsync(")]   // 06-21 → DELETE api/chat/messages/{id}
    [InlineData("GetStorageAsync(")]    // 06-22 → GET api/chat/storage
    public void G_S4_3_1_닫힌_함수_부르는_곳_0(string call)
    {
        var callers = WebCodeLines()
            .Where(l => l.Line.Contains(call, StringComparison.Ordinal))
            .Where(l => !l.Line.TrimStart().StartsWith("public ", StringComparison.Ordinal))   // 정의 줄
            .Select(l => l.Where)
            .ToList();

        Assert.True(callers.Count == 0,
            $"G-S4-3 웹이 닫힌 서버 문을 부르는 {call} 를 쓴다: {string.Join(", ", callers)} — 베타 판에서 그 문은 닫혀 있다(W-S-04).");
    }

    [Fact(DisplayName = "G-S4-3-2 웹 메신저 주소 줄에 before 가 없다(06-9 거절과 맞물림)")]
    public void G_S4_3_2_대화_읽기에_before_0()
    {
        var hits = WebCodeLines()
            .Where(l => l.Line.Contains("api/chat/rooms/", StringComparison.Ordinal)
                     && l.Line.Contains("before", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Where)
            .ToList();

        Assert.True(hits.Count == 0,
            $"G-S4-3 웹이 메신저 주소에 before 를 붙인다: {string.Join(", ", hits)} — 서버가 404 로 거절해 대화가 안 뜬다(W-S-04 06-9).");
    }
}
