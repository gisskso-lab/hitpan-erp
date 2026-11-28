using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using HitPan.API.Controllers;
using HitPan.API.Security;
using HitPan.API.Services;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HitPan.Tests.Security;

/// <summary>
/// 🔴 W-G1-52 갈래 C — 대표 비번 되찾기 비DB 게이트(설계 §12 G10 · G11 · 덧 지시서 §1 H1 · H3).
/// </summary>
/// <remarks>
/// 가짜 저장소 · 가짜 감사로 <b>순서와 거절을 동작으로</b> 잰다(불린 횟수 · 넘어간 값).
/// SQL 자체(조건 갱신 줄 수 1 · 동시 2요청 · 대표 행만)는 DB 게이트 <c>ParentResetGateDbTests</c>(시험 DB · CI 몫)가 잰다.
/// </remarks>
public sealed class ParentResetGateTests
{
    private const string Tenant = "t-0001";
    private const string Parent = "u-parent-0001";
    private const string GoodPassword = "NewPass1!";

    private sealed class FakeStore : IParentResetStore
    {
        public ParentResetTokenRow? Row { get; set; }
        public ParentResetCommitOutcome Outcome { get; set; } = ParentResetCommitOutcome.Committed;
        public int FindCalls { get; private set; }
        public List<ParentResetCommit> Commits { get; } = new();

        public Task<ParentResetTokenRow?> FindTokenAsync(string tokenHash, CancellationToken ct)
        {
            FindCalls++;
            return Task.FromResult(Row);
        }

        public Task<ParentResetCommitOutcome> ConsumeAndResetAsync(ParentResetCommit commit, CancellationToken ct)
        {
            Commits.Add(commit);
            return Task.FromResult(Outcome);
        }

        // 🔴 고침 1 X2 — 알림 1줄(넘어간 id 를 셈).
        public DateTime? NoticeAt { get; set; }
        public List<(string Tenant, string User)> NoticeCalls { get; } = new();

        public Task<DateTime?> TakeResetNoticeAsync(string tenantId, string userId, CancellationToken ct)
        {
            NoticeCalls.Add((tenantId, userId));
            return Task.FromResult(NoticeAt);
        }
    }

    private sealed class Harness
    {
        public FakeStore Store { get; } = new();
        public Mock<ILocalAuditFile> Audit { get; } = new(MockBehavior.Strict);
        public List<LocalAuditRecord> Lines { get; } = new();
        public bool FailStarted { get; set; }
        public ParentPasswordResetService Service { get; }

        public Harness()
        {
            Audit.Setup(a => a.AppendAsync(It.IsAny<LocalAuditRecord>(), It.IsAny<CancellationToken>()))
                .Returns<LocalAuditRecord, CancellationToken>((r, _) =>
                {
                    if (FailStarted && r.Result == LocalAuditResult.Started)
                        throw new LocalAuditWriteException("io", "시험 — 감사 파일 못 씀");
                    Lines.Add(r);
                    return Task.FromResult(new LocalAuditEntry(Lines.Count, DateTime.UtcNow, r, new string('0', 64), new string('a', 64)));
                });
            Service = new ParentPasswordResetService(Store, Audit.Object, NullLogger<ParentPasswordResetService>.Instance);
        }
    }

    private static ParentResetTokenRow GoodRow() => new(Tenant, Parent, Expired: false, Used: false, TargetIsParent: true, LoginId: "boss");

    private static ParentResetRequest Req(string? token) => new(token, GoodPassword, GoodPassword);

    // ── G10 · 정상 ────────────────────────────────────────────────

    [Fact]
    public async Task G10_Good_Token_Changes_Parent_Once_With_Bcrypt_And_Audit_Start_Then_End()
    {
        var h = new Harness { Store = { Row = GoodRow() } };
        var token = ParentResetToken.NewToken();

        var r = await h.Service.ResetAsync(Req(token), "127.0.0.1");

        Assert.Equal(ParentResetStatus.Succeeded, r.Status);
        Assert.False(r.AuditTailMissing);
        var c = Assert.Single(h.Store.Commits);
        Assert.Equal(ParentResetToken.Hash(token), c.TokenHash);
        Assert.Equal(Tenant, c.TenantId);           // #2 — 표 행에서 나온 값
        Assert.Equal(Parent, c.UserId);
        Assert.True(BCrypt.Net.BCrypt.Verify(GoodPassword, c.NewPasswordHash));
        Assert.DoesNotContain(GoodPassword, c.NewPasswordHash, StringComparison.Ordinal);

        Assert.Equal(2, h.Lines.Count);
        Assert.Equal(LocalAuditResult.Started, h.Lines[0].Result);
        Assert.Equal(LocalAuditResult.Succeeded, h.Lines[1].Result);
        Assert.All(h.Lines, l => Assert.Equal(LocalAuditAction.ParentPasswordReset, l.Action));
        Assert.Equal(h.Lines[0].OpId, h.Lines[1].OpId);
        // #40 — 감사 줄에 비번 · 표 값 0
        foreach (var l in h.Lines)
        {
            var all = string.Join("|", l.TenantId, l.ParentUserId, l.OpId, l.Detail ?? "");
            Assert.DoesNotContain(GoodPassword, all, StringComparison.Ordinal);
            Assert.DoesNotContain(token, all, StringComparison.Ordinal);
        }
    }

    // ── G10 · 거절 넷(표 없음 · 만료 · 재사용 · 자식 행) — 바뀐 것 0 ──────────────

    public static IEnumerable<object?[]> RejectRows() => new[]
    {
        new object?[] { null, ParentPasswordResetService.Reasons.TokenNotFound },
        new object?[] { new ParentResetTokenRow(Tenant, Parent, Expired: true, Used: false, TargetIsParent: true, "boss"), ParentPasswordResetService.Reasons.TokenExpired },
        new object?[] { new ParentResetTokenRow(Tenant, Parent, Expired: false, Used: true, TargetIsParent: true, "boss"), ParentPasswordResetService.Reasons.TokenUsed },
        new object?[] { new ParentResetTokenRow(Tenant, "u-child", Expired: false, Used: false, TargetIsParent: false, null), ParentPasswordResetService.Reasons.NotParent },
    };

    [Theory]
    [MemberData(nameof(RejectRows))]
    public async Task G10_Rejects_Missing_Expired_Used_Child_Without_Commit(ParentResetTokenRow? row, string reason)
    {
        var h = new Harness { Store = { Row = row } };

        var r = await h.Service.ResetAsync(Req(ParentResetToken.NewToken()), null);

        Assert.Equal(ParentResetStatus.TokenRejected, r.Status);
        Assert.Equal(reason, r.ReasonCode);
        Assert.Empty(h.Store.Commits);
        var line = Assert.Single(h.Lines);
        Assert.Equal(LocalAuditAction.ParentPasswordResetFailed, line.Action);
        Assert.Equal(reason, line.Detail);
        // 화면 문구는 한 가지(어느 까닭인지 알리지 않음)
        Assert.Equal(ParentPasswordResetService.TokenRejectedMessage, r.Message);
    }

    [Fact]
    public async Task G10_Peek_Rejects_Same_Four_And_Shows_LoginId_Only_For_Good()
    {
        var token = ParentResetToken.NewToken();
        foreach (var row in RejectRows().Select(o => (ParentResetTokenRow?)o[0]))
        {
            var h = new Harness { Store = { Row = row } };
            var p = await h.Service.PeekAsync(token);
            Assert.False(p.Usable);
            Assert.Null(p.LoginId);
            Assert.Empty(h.Lines);   // 보기 = 감사 0 · 소비 0
            Assert.Empty(h.Store.Commits);
        }
        var good = new Harness { Store = { Row = GoodRow() } };
        var ok = await good.Service.PeekAsync(token);
        Assert.True(ok.Usable);
        Assert.Equal("boss", ok.LoginId);
        Assert.Empty(good.Store.Commits);
    }

    [Theory]
    [InlineData(ParentResetCommitOutcome.TokenNotConsumed, "token_race")]
    [InlineData(ParentResetCommitOutcome.NotParent, "not_parent")]
    public async Task G10_Commit_Lost_Race_Is_Rejected_And_Audited_As_Failed(ParentResetCommitOutcome outcome, string reason)
    {
        var h = new Harness { Store = { Row = GoodRow(), Outcome = outcome } };

        var r = await h.Service.ResetAsync(Req(ParentResetToken.NewToken()), null);

        Assert.Equal(ParentResetStatus.TokenRejected, r.Status);
        Assert.Equal(reason, r.ReasonCode);
        Assert.Equal(2, h.Lines.Count);
        Assert.Equal(LocalAuditResult.Started, h.Lines[0].Result);
        Assert.Equal(LocalAuditResult.Failed, h.Lines[1].Result);
        Assert.Equal(reason, h.Lines[1].Detail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]  // 43자 · base64url 밖 글자
    public async Task G10_Malformed_Token_Never_Reaches_Store(string? token)
    {
        var h = new Harness { Store = { Row = GoodRow() } };

        var r = await h.Service.ResetAsync(Req(token), null);

        Assert.Equal(ParentResetStatus.TokenRejected, r.Status);
        Assert.Equal(0, h.Store.FindCalls);
        Assert.Empty(h.Store.Commits);
    }

    // ── H1 — 감사 「시작」 줄 실패 = DB 손 0 ─────────────────────────────

    [Fact]
    public async Task H1_Audit_Start_Failure_Changes_Nothing()
    {
        var h = new Harness { Store = { Row = GoodRow() }, FailStarted = true };

        var r = await h.Service.ResetAsync(Req(ParentResetToken.NewToken()), null);

        Assert.Equal(ParentResetStatus.AuditUnavailable, r.Status);
        Assert.Empty(h.Store.Commits);   // 교체 묶음이 한 번도 불리지 않음
    }

    [Theory]
    [InlineData(GoodPassword, "Different1!", ParentResetStatus.PasswordMismatch)]
    [InlineData("short1!", "short1!", ParentResetStatus.PasswordRule)]
    public async Task Input_Errors_Touch_Neither_Store_Nor_Audit(string pw, string confirm, ParentResetStatus expected)
    {
        var h = new Harness { Store = { Row = GoodRow() } };

        var r = await h.Service.ResetAsync(new ParentResetRequest(ParentResetToken.NewToken(), pw, confirm), null);

        Assert.Equal(expected, r.Status);
        Assert.Equal(0, h.Store.FindCalls);
        Assert.Empty(h.Store.Commits);
        Assert.Empty(h.Lines);
    }

    // ── 비번 규칙 = 기존 계정 만들기 규칙(UserService.ValidatePassword · private) ──────

    [Theory]
    [InlineData("")]
    [InlineData("Ab1!")]
    [InlineData("abcdefg1!")]
    [InlineData("ABCDEFGH!")]
    [InlineData("Abcdefgh1")]
    [InlineData("Abcdefg1!")]
    [InlineData("한글대문자A1!")]
    public void PR_RuleParity_SameAsUserServiceValidatePassword(string pw)
    {
        var m = typeof(UserService).GetMethod("ValidatePassword", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(m);
        string? userServiceError = null;
        try { m!.Invoke(null, new object[] { pw }); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException inner) { userServiceError = inner.Message; }

        Assert.Equal(userServiceError, ParentPasswordRule.Check(pw));
    }

    // ── 1회용 표 꼴 · 지문 ────────────────────────────────────────────

    [Fact]
    public void Token_Is_Random_WellFormed_And_Stored_Only_As_Sha256()
    {
        var a = ParentResetToken.NewToken();
        var b = ParentResetToken.NewToken();
        Assert.NotEqual(a, b);
        Assert.Equal(ParentResetToken.Length, a.Length);
        Assert.True(ParentResetToken.IsWellFormed(a));
        var h = ParentResetToken.Hash(a);
        Assert.Equal(64, h.Length);
        Assert.Matches("^[0-9a-f]{64}$", h);
        Assert.DoesNotContain(a, h, StringComparison.Ordinal);
        Assert.Equal(10, ParentResetTokenIssuer.LifetimeMinutes);
    }

    // ── 오프라인 명령 인자(R16 ③ ④) · 주소 ──────────────────────────────

    [Theory]
    [InlineData(new[] { "parent-reset" }, false)]
    [InlineData(new[] { "parent-reset", "--elevated" }, true)]
    public void Command_Accepts_Only_Fixed_Args(string[] args, bool elevated)
        => Assert.Equal(elevated, ParentResetCommand.ParseArgs(args));

    [Theory]
    [InlineData("parent-reset --password x")]
    [InlineData("parent-reset --ELEVATED")]
    [InlineData("parent-reset --elevated extra")]
    [InlineData("seed-parent")]
    public void Command_Unknown_Args_Stop_Before_Host(string argLine)
        => Assert.Null(ParentResetCommand.ParseArgs(argLine.Split(' ')));

    [Fact]
    public void G11_Reset_Url_Is_Localhost_And_Token_After_Hash()
    {
        var token = ParentResetToken.NewToken();
        var url = new Uri(ParentResetCommand.BuildResetUrl(5234, token));
        Assert.True(url.IsLoopback);
        Assert.Equal(ParentResetCommand.ResetPagePath, url.AbsolutePath);
        Assert.DoesNotContain(token, url.PathAndQuery, StringComparison.Ordinal); // 서버로 가는 부분엔 표 0
        Assert.Equal("#t=" + token, url.Fragment);
    }

    // ── G11 — 외부 주소 호출 0(동작: 재설정 한 바퀴 동안 나가는 HTTP 0) ─────────────

    [Fact]
    public async Task G11_Reset_Round_Makes_No_Outgoing_Http()
    {
        var seen = new List<string>();
        InThisTest.Value = true;   // 다른 시험(병렬)이 내는 HTTP 는 세지 않는다 — 이 흐름만
        using var sub = DiagnosticListener.AllListeners.Subscribe(new ListenerObserver(seen));
        var h = new Harness { Store = { Row = GoodRow() } };

        await h.Service.PeekAsync(ParentResetToken.NewToken());
        var r = await h.Service.ResetAsync(Req(ParentResetToken.NewToken()), null);
        await h.Service.TakeResetNoticeAsync(Tenant, Parent);   // 🔴 고침 1 X2 — 알림 길도 나가는 HTTP 0

        Assert.Equal(ParentResetStatus.Succeeded, r.Status);
        Assert.Empty(seen);
    }

    [Fact]
    public void G11_Reset_Types_Take_No_Network_Dependency()
    {
        foreach (var t in new[] { typeof(ParentPasswordResetService), typeof(DapperParentResetStore), typeof(ParentResetController) })
        {
            foreach (var ctor in t.GetConstructors())
            foreach (var p in ctor.GetParameters())
            {
                var ns = p.ParameterType.Namespace ?? "";
                Assert.False(ns.StartsWith("System.Net", StringComparison.Ordinal), $"{t.Name} 생성자에 네트워크 의존성 {p.ParameterType}");
                Assert.NotEqual("IHttpClientFactory", p.ParameterType.Name);
            }
        }
    }

    // ── 로그인 전 길 · 메인PC 만 ──────────────────────────────────────

    [Fact]
    public void Controller_Is_Anonymous_But_MainPcOnly()
    {
        var t = typeof(ParentResetController);
        Assert.NotNull(t.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.NotNull(t.GetCustomAttribute<MainPcOnlyAttribute>());
    }

    // ══ 고침 1(2026-11-28 · 지시서 20261128작_W-G1-52_갈래C_고침1) ═══════════════════════════════

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "installer", "HitPan-Universal.iss"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    // ── X1 — 안내 글 ↔ 설치기 · 실행 글 실제 이름 글자 대조([4] P1-1 · P1-2 · P2-2) ─────────

    [Fact]
    public void X1_Guide_Names_Match_Installer_And_Command_File()
    {
        var root = RepoRoot();
        var iss = File.ReadAllText(Path.Combine(root, "installer", "HitPan-Universal.iss"));
        var group = Regex.Match(iss, @"^DefaultGroupName=(.+?)\s*$", RegexOptions.Multiline).Groups[1].Value;
        Assert.Equal(ParentResetGuideText.StartMenuGroup, group);
        var dir = Regex.Match(iss, @"^DefaultDirName=\{autopf\}\\(.+?)\s*$", RegexOptions.Multiline).Groups[1].Value;
        Assert.Equal(ParentResetGuideText.UsualInstallFolder, @"C:\Program Files\" + dir);

        var cmdFile = ParentResetGuideText.CommandFileStem + ".cmd";
        Assert.Contains($"Name: \"{{group}}\\{ParentResetGuideText.ShortcutName}\"; Filename: \"{{app}}\\{ParentResetGuideText.ApiFolderName}\\{cmdFile}\"", iss, StringComparison.Ordinal);
        Assert.Contains($"Name: \"{{commondesktop}}\\{ParentResetGuideText.DesktopIconName}\"; Filename: \"{{app}}\\hitpan-start.bat\"", iss, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "src", "HitPan.API", cmdFile)), "실행 글 파일 이름이 안내와 다름");
        var csproj = File.ReadAllText(Path.Combine(root, "src", "HitPan.API", "HitPan.API.csproj"));
        Assert.Contains($"<None Update=\"{cmdFile}\" CopyToOutputDirectory=\"PreserveNewest\" CopyToPublishDirectory=\"PreserveNewest\" />", csproj, StringComparison.Ordinal);

        // 화면이 이 글을 그대로 쓴다(손으로 쓴 안내로 돌아가면 빨강).
        var login = File.ReadAllText(Path.Combine(root, "src", "HitPan.Web", "Pages", "Login.razor"));
        Assert.Contains("@foreach (var step in ParentResetGuideText.NumberedSteps())", login, StringComparison.Ordinal);
        Assert.Contains("@ParentResetGuideText.FallbackLine", login, StringComparison.Ordinal);
    }

    [Fact]
    public void X1_Guide_Has_No_Screen_Position_Words_And_Has_Fallback_Path()
    {
        var all = string.Join("\n", ParentResetGuideText.NumberedSteps()) + "\n" + ParentResetGuideText.FallbackLine;
        Assert.DoesNotContain("왼쪽 아래", all, StringComparison.Ordinal);
        Assert.Contains("작업 표시줄", all, StringComparison.Ordinal);
        Assert.StartsWith("1. ", ParentResetGuideText.NumberedSteps().First(), StringComparison.Ordinal);
        Assert.Contains($"「{ParentResetGuideText.StartMenuGroup}」 → 「{ParentResetGuideText.ShortcutName}」", ParentResetGuideText.Steps[0], StringComparison.Ordinal);
        Assert.Contains("파일 위치 열기", ParentResetGuideText.FallbackLine, StringComparison.Ordinal);
        Assert.Contains($"「{ParentResetGuideText.CommandFileStem}」 를 두 번 누릅니다", ParentResetGuideText.FallbackLine, StringComparison.Ordinal);
        Assert.Contains(ParentResetGuideText.UsualInstallFolder + "\\" + ParentResetGuideText.ApiFolderName, ParentResetGuideText.FallbackLine, StringComparison.Ordinal);
    }

    // ── X2 — 다음 로그인 1줄(설계 R16 ⑤ · [3-V] P2-1) ─────────────────────────────

    [Fact]
    public void X2_Notice_Line_Has_Date_And_Time()
        => Assert.Equal("대표 비밀번호가 11월 28일 14시 05분에 메인PC 에서 다시 정해졌습니다.",
            ParentResetGuideText.NoticeLine(new DateTime(2026, 11, 28, 14, 5, 0, DateTimeKind.Local)));

    [Fact]
    public async Task X2_Notice_Uses_Given_Ids_And_Blank_Ids_Ask_Nothing()
    {
        var h = new Harness();
        h.Store.NoticeAt = new DateTime(2026, 11, 28, 5, 5, 0, DateTimeKind.Utc);
        Assert.Equal(h.Store.NoticeAt, await h.Service.TakeResetNoticeAsync(Tenant, Parent));
        Assert.Equal((Tenant, Parent), Assert.Single(h.Store.NoticeCalls));
        Assert.Null(await h.Service.TakeResetNoticeAsync("", Parent));
        Assert.Null(await h.Service.TakeResetNoticeAsync(Tenant, " "));
        Assert.Single(h.Store.NoticeCalls);   // 빈 id 는 저장소에 묻지 않음
        Assert.Empty(h.Lines);                // 알림 = 감사 손 0
        Assert.Empty(h.Store.Commits);        // 알림 = 비번 손 0
    }

    [Fact]
    public void X2_Notice_Endpoint_Is_Logged_In_Only_And_Not_On_The_Anonymous_Path()
    {
        var t = typeof(ParentResetNoticeController);
        Assert.Null(t.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(t.GetCustomAttribute<MainPcOnlyAttribute>());
        Assert.Equal("TenantOnly", t.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
        var route = t.GetCustomAttribute<RouteAttribute>()!.Template;
        // TenantMiddleware 토큰 없는 길 비교(StartsWithSegments)에 걸리지 않아야 JWT 대조를 지난다 — 같은 함수로 잼.
        Assert.False(new PathString("/" + route + "/take").StartsWithSegments("/api/parent-reset"));
        Assert.True(new PathString("/api/parent-reset/peek").StartsWithSegments("/api/parent-reset"));
    }

    // ── X4 — MainPcOnly 동작 시험(특성 붙음이 아니라 재설정 끝점의 필터를 실제로 돌림 · [3-V] P2-2) ─────

    public static TheoryData<string, string?, string?, bool> MainPcCases => new()
    {
        { "127.0.0.1", null, null, true },
        { "::1", null, null, true },
        { "127.0.0.1", "CF-Connecting-IP", "203.0.113.5", false },
        { "127.0.0.1", "X-Forwarded-For", "192.168.0.20", false },
        { "192.168.0.20", null, null, false },
        { "192.168.0.20", MainPcOnlyAttribute.PassHeader, "pass-from-elsewhere", false }, // 출입증 서비스 없음 = 막음(열어 두는 쪽 실패 0)
    };

    [Theory]
    [MemberData(nameof(MainPcCases))]
    public void X4_MainPcOnly_On_Reset_Endpoint_Rejects_Tunnel_Forwarded_And_Remote(string remote, string? header, string? value, bool allowed)
    {
        var filter = typeof(ParentResetController).GetCustomAttribute<MainPcOnlyAttribute>();
        Assert.NotNull(filter);
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remote);
        http.Request.Method = "POST";
        http.Request.Path = "/api/parent-reset";
        if (header is not null) http.Request.Headers[header] = value;
        var ctx = new ActionExecutingContext(
            new ActionContext(http, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()),
            new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());

        filter!.OnActionExecuting(ctx);

        if (allowed) Assert.Null(ctx.Result);
        else Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(ctx.Result).StatusCode);
    }

    // ── X6 — 화면이 표를 읽은 뒤 주소에서 지움([3-V] P3-2) ─────────────────────────

    [Theory]
    [InlineData("http://localhost:5234/parent-reset#t=AAAA", "/parent-reset")]
    [InlineData("http://localhost:5234/parent-reset?x=1#t=AAAA&y=2", "/parent-reset?x=1")]
    [InlineData("http://localhost:5234/parent-reset", "/parent-reset")]
    public void X6_Address_After_Reading_Has_No_Token(string uri, string expected)
    {
        var cleaned = ParentResetGuideText.PathWithoutFragment(uri);
        Assert.Equal(expected, cleaned);
        Assert.DoesNotContain("AAAA", cleaned, StringComparison.Ordinal);
    }

    [Fact]
    public void X6_Screen_Reads_Token_Then_Clears_Address()
    {
        var token = ParentResetToken.NewToken();
        Assert.Equal(token, ParentResetGuideText.ReadToken(ParentResetCommand.BuildResetUrl(5234, token)));
        // 화면 실행은 브라우저 몫(화면 실측) — 여기서는 「읽은 뒤에 지운다」 순서를 화면 소스에서 잰다.
        var razor = File.ReadAllText(Path.Combine(RepoRoot(), "src", "HitPan.Web", "Pages", "ParentReset.razor"));
        var read = razor.IndexOf("_token = ParentResetGuideText.ReadToken(Navigation.Uri);", StringComparison.Ordinal);
        var clear = razor.IndexOf("JS.InvokeVoidAsync(\"history.replaceState\", null, \"\", ParentResetGuideText.PathWithoutFragment(Navigation.Uri))", StringComparison.Ordinal);
        Assert.True(read >= 0 && clear > read, "화면이 표를 읽은 뒤 주소에서 지워야 함");
    }

    private static readonly AsyncLocal<bool> InThisTest = new();

    private sealed class ListenerObserver : IObserver<DiagnosticListener>
    {
        private readonly List<string> _seen;
        public ListenerObserver(List<string> seen) => _seen = seen;
        public void OnCompleted() { /* 끝 알림 — 셀 것 없음 */ }
        public void OnError(Exception error) => _seen.Add("listener-error:" + error.GetType().Name);
        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == "HttpHandlerDiagnosticListener")
                value.Subscribe(new EventObserver(_seen));
        }
    }

    private sealed class EventObserver : IObserver<KeyValuePair<string, object?>>
    {
        private readonly List<string> _seen;
        public EventObserver(List<string> seen) => _seen = seen;
        public void OnCompleted() { /* 끝 알림 — 셀 것 없음 */ }
        public void OnError(Exception error) => _seen.Add("event-error:" + error.GetType().Name);
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (!InThisTest.Value) return;
            lock (_seen) _seen.Add(value.Key);
        }
    }
}
