using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HitPan.Backoffice.Services;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>20261015작11 — 「백오피스 전체가 죽었잖아」 사고 게이트 (G-W14-1 ~ G-W14-8)</b>
///
/// <para><b>[사고]</b> 2026-10-15, 백오피스 모든 화면의 조회가 한꺼번에 <c>401</c>.
/// 화면은 로그인 상태였다. 진범 = <b>쿠키는 슬라이딩으로 계속 사는데 그 안에 굳혀 둔 JWT 는
/// 8시간에 죽고, 갱신 경로가 0건</b>이라 영영 안 풀린다.</para>
///
/// <para>🔴 <b>이 게이트는 글자를 세지 않는다.</b> <see cref="BackofficeSessionGuard"/> 에
/// <b>진짜 JWT 모양의 토큰을 만들어 넣고</b> 돌아오는 판정을 본다.
/// (게이트가 글자를 세면 코드가 바뀌어도 초록이다 — 누적 30회+ 사고.)</para>
///
/// <para><b>음성 대조군</b>(2026-10-15 실측): <c>ShouldDropCookie</c> 의 본문을
/// <c>=&gt; false</c>(=봉합 제거)로 바꾸면 <b>G-W14-2·4·5 가 빨강</b>.
/// <c>graceSeconds</c> 를 무시하게 바꾸면 <b>G-W14-5 가 빨강</b>.
/// <c>Judge</c> 가 못 읽는 토큰을 <c>Expired</c> 로 보게 바꾸면 <b>G-W14-3 이 빨강</b>.
/// ⇒ 이 게이트는 <b>봉합이 빠지면 실제로 무너진다.</b></para>
/// </summary>
public sealed class BackofficeSessionGuardGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    // ══════════════════════════════════════════════════════════════
    // 토막 — 진짜 JWS 모양(머리.몸.서명)으로 만든다. 서명은 검증 대상이 아니므로 아무 글자나 둔다
    //   (판정은 수명만 본다 — 서명 검증은 API 몫이고 화면은 비밀키를 갖지 않는다 · #29).
    // ══════════════════════════════════════════════════════════════

    private static string Base64Url(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Jwt(string payloadJson)
    {
        var head = Base64Url(Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}"""));
        var body = Base64Url(Encoding.UTF8.GetBytes(payloadJson));
        return $"{head}.{body}.ZmFrZS1zaWduYXR1cmU";
    }

    /// <summary>실물과 같은 모양의 토큰 — 백오피스 API 가 넣는 클레임을 그대로 담는다.</summary>
    private static string TokenExpiringAt(DateTimeOffset exp)
    {
        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["iss"] = "hitpan-backoffice",
            ["aud"] = "backoffice",
            ["account_type"] = "platform_owner",
            ["role"] = "owner",
            ["exp"] = exp.ToUnixTimeSeconds(),
        });
        return Jwt(payload);
    }

    // ══════════════════════════════════════════════════════════════
    // G-W14-1 — 살아 있는 토큰은 건드리지 않는다 (양성 대조군)
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W14-1 살아 있는 세션은 안 끊는다 — 멀쩡한 사람을 내보내면 그게 더 큰 사고 (20261015작11)")]
    public void G_W14_1_살아있는_토큰은_유지된다()
    {
        var token = TokenExpiringAt(Now.AddHours(3));

        Assert.Equal(BackofficeSessionGuard.Verdict.Alive, BackofficeSessionGuard.Judge(token, Now));
        Assert.False(BackofficeSessionGuard.ShouldDropCookie(token, Now),
            "🔴 아직 3시간 남은 토큰인데 쿠키를 버린다 — 일하던 사람이 쫓겨난다");
    }

    // ══════════════════════════════════════════════════════════════
    // G-W14-2 🔴 사고 그 자체 — 8시간 지난 토큰
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W14-2 🔴 8시간 지난 토큰을 든 쿠키는 버린다 — 이게 10/15 사고 그 모양이다 (20261015작11)")]
    public void G_W14_2_만료된_토큰이면_쿠키를_버린다()
    {
        // 2026-10-14 23:30 로그인 + Jwt:ExpiresHours(8) ⇒ 10/15 07:30 만료. 지금은 09:00.
        var loginAt = new DateTimeOffset(2026, 10, 14, 23, 30, 0, TimeSpan.Zero);
        var token = TokenExpiringAt(loginAt.AddHours(8));

        Assert.Equal(BackofficeSessionGuard.Verdict.Expired, BackofficeSessionGuard.Judge(token, Now));
        Assert.True(BackofficeSessionGuard.ShouldDropCookie(token, Now),
            "🔴 죽은 토큰을 든 쿠키를 그냥 둔다 — 화면은 로그인 상태인데 모든 조회가 401 이다. " +
            "이게 사장님이 본 「백오피스 전체가 죽었다」다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-W14-3 🔴 확실할 때만 버린다 — 못 읽는 토큰으로 멀쩡한 세션을 끊지 않는다
    // ══════════════════════════════════════════════════════════════

    [Theory(DisplayName = "G-W14-3 🔴 못 읽는 토큰으로는 쿠키를 안 버린다 — 내 판단 착오로 세션을 끊는 쪽이 더 나쁘다 (20261015작11)")]
    [InlineData(null, BackofficeSessionGuard.Verdict.NoTokenClaim)]
    [InlineData("", BackofficeSessionGuard.Verdict.NoTokenClaim)]
    [InlineData("   ", BackofficeSessionGuard.Verdict.NoTokenClaim)]
    [InlineData("not-a-jwt", BackofficeSessionGuard.Verdict.Unreadable)]
    [InlineData("only.two", BackofficeSessionGuard.Verdict.Unreadable)]
    [InlineData("aaa.!!!!.bbb", BackofficeSessionGuard.Verdict.Unreadable)]
    [InlineData("aaa..bbb", BackofficeSessionGuard.Verdict.Unreadable)]
    public void G_W14_3_판정할수_없으면_그냥_둔다(string? token, BackofficeSessionGuard.Verdict expected)
    {
        Assert.Equal(expected, BackofficeSessionGuard.Judge(token, Now));
        Assert.False(BackofficeSessionGuard.ShouldDropCookie(token, Now),
            $"🔴 「{token ?? "(null)"}」 로 쿠키를 버렸다 — 수명을 모르는 토큰을 죽었다고 단정했다");
    }

    [Fact(DisplayName = "G-W14-3b 🔴 exp 가 없는 토큰은 죽었다고 말하지 않는다 (20261015작11)")]
    public void G_W14_3b_exp_없는_토큰은_판정하지_않는다()
    {
        var token = Jwt("""{"iss":"hitpan-backoffice","role":"owner"}""");

        Assert.Equal(BackofficeSessionGuard.Verdict.NoExpiry, BackofficeSessionGuard.Judge(token, Now));
        Assert.False(BackofficeSessionGuard.ShouldDropCookie(token, Now));
    }

    // ══════════════════════════════════════════════════════════════
    // G-W14-4 🔴 경계 — 「딱 지금 만료」는 죽은 것으로 본다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W14-4 🔴 만료 시각에 딱 닿은 토큰은 죽은 것으로 본다 — 경계에서 401 을 내보내지 않는다 (20261015작11)")]
    public void G_W14_4_경계값은_죽은것으로_본다()
    {
        Assert.True(BackofficeSessionGuard.ShouldDropCookie(TokenExpiringAt(Now), Now, graceSeconds: 0),
            "🔴 exp == 지금 인데 살아 있다고 했다 — 다음 요청이 401 이다");

        Assert.False(BackofficeSessionGuard.ShouldDropCookie(TokenExpiringAt(Now.AddSeconds(1)), Now, graceSeconds: 0),
            "🔴 1초 남았는데 끊었다 — graceSeconds=0 이면 아직 살아 있는 것이다");
    }

    // ══════════════════════════════════════════════════════════════
    // G-W14-5 🔴 유예 — 누르는 순간 만료되는 것을 피한다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W14-5 🔴 만료 직전에 미리 끊는다 — 누르는 그 순간 401 을 보는 것보다 정직하다 (20261015작11)")]
    public void G_W14_5_유예시간만큼_미리_끊는다()
    {
        var almost = TokenExpiringAt(Now.AddSeconds(10));

        Assert.False(BackofficeSessionGuard.ShouldDropCookie(almost, Now, graceSeconds: 0),
            "유예 0 이면 10초 남은 토큰은 살아 있다 — 이 줄은 아래가 **유예 때문에** 끊긴 것임을 증명한다");

        Assert.True(BackofficeSessionGuard.ShouldDropCookie(almost, Now, graceSeconds: 30),
            "🔴 유예 30초인데 10초 남은 토큰을 통과시켰다 — 사용자가 누르는 순간 401 이다");

        // 기본값이 유예를 켜 두는지 — 부르는 쪽이 아무것도 안 넘겼을 때의 행동이 실제 행동이다
        Assert.True(BackofficeSessionGuard.ShouldDropCookie(almost, Now),
            "🔴 기본값에 유예가 없다 — Program.cs 는 기본값으로 부른다");
    }

    // ══════════════════════════════════════════════════════════════
    // G-W14-6 수명 읽기 자체
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W14-6 exp 를 숫자로도 글자로도 읽는다 (20261015작11)")]
    public void G_W14_6_exp_를_읽는다()
    {
        var exp = new DateTimeOffset(2026, 10, 15, 7, 30, 0, TimeSpan.Zero);

        Assert.Equal(exp, BackofficeSessionGuard.ReadExpiryUtc(TokenExpiringAt(exp)));

        var asText = Jwt($"{{\"exp\":\"{exp.ToUnixTimeSeconds()}\"}}");
        Assert.Equal(exp, BackofficeSessionGuard.ReadExpiryUtc(asText));

        Assert.Null(BackofficeSessionGuard.ReadExpiryUtc(Jwt("""{"exp":{"nested":1}}""")));
        Assert.Null(BackofficeSessionGuard.ReadExpiryUtc(Jwt("not json at all")));
    }

    // ══════════════════════════════════════════════════════════════
    // G-W14-7 🔴 Program.cs 가 이 판정을 **실제로 부르나**
    //   — 판정만 맞고 아무도 안 부르면 사고는 그대로다(「고쳤다 ≠ 갔다」 7차 재발).
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W14-7 🔴 백오피스 쿠키 설정이 이 판정을 실제로 불러 쿠키를 버린다 (20261015작11)")]
    public void G_W14_7_Program_이_판정을_부른다()
    {
        var src = StripComments(File.ReadAllText(BackofficeProgramPath()));

        // 🔴 「이름이 들어 있나」로 재면 안 된다 — 대조군 NC-4 가 이 자리를 물었다:
        //    `OnValidatePrincipalDISABLED_NC4` 로 바꿔 배선을 끊었는데 Contains 가 **통과했다**
        //    (앞 글자가 같아 부분일치가 된다). ⇒ **대입문 모양 그대로** 본다.
        Assert.Matches(@"o\.Events\.OnValidatePrincipal\s*=", src);
        Assert.Contains(nameof(BackofficeSessionGuard), src, StringComparison.Ordinal);
        Assert.Contains(nameof(BackofficeSessionGuard.ShouldDropCookie), src, StringComparison.Ordinal);
        Assert.Contains("RejectPrincipal", src, StringComparison.Ordinal);

        // 🔴 쿠키를 거절만 하고 SignOut 을 안 하면 다음 요청에 같은 쿠키가 또 온다.
        Assert.Contains("SignOutAsync", src, StringComparison.Ordinal);

        // 🔴 토큰을 로그에 적지 않는다 (#22 · 자격증명 「모양」은 어디에도 금지)
        //    로그를 부르는 줄에 토큰 변수가 섞여 들어가면 서버 로그에 JWT 가 남는다.
        var logLines = src.Split('\n')
            .Where(l => l.Contains("Log", StringComparison.Ordinal) && l.Contains("token", StringComparison.Ordinal))
            .ToArray();
        Assert.True(logLines.Length == 0,
            "🔴 로그 줄에 토큰이 섞여 있다 — 서버 로그에 JWT 가 남는다:" + Environment.NewLine +
            string.Join(Environment.NewLine, logLines));
    }

    // ══════════════════════════════════════════════════════════════
    // G-W14-8 🔴 전제가 그대로인가 — 수명·갱신경로
    //   이 봉합은 「JWT 수명이 쿠키보다 짧다」는 전제 위에 서 있다. 그 전제가 바뀌면 알아야 한다.
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W14-8 🔴 갱신(refresh) 경로가 아직 0건이다 — 생기면 이 봉합을 다시 보라 (20261015작11 결-M)")]
    public void G_W14_8_갱신경로_유무를_고정한다()
    {
        var auth = File.ReadAllText(Path.Combine(RepoRoot(),
            "src", "HitPan.Backoffice.API", "Controllers", "BackofficeAuthController.cs"));

        var routes = Regex.Matches(auth, @"\[Http(?:Post|Get|Put)\(""(?<r>[^""]+)""\)\]")
            .Select(m => m.Groups["r"].Value).ToArray();

        Assert.Equal(new[] { "admin/login", "reseller/login" }, routes);

        // 🔴 이 줄이 빨강이 되는 날 = 갱신 경로가 생긴 날이다. 그러면 「8시간마다 재로그인」을
        //    없앨 수 있고(결-M), 이 게이트의 기대값도 그때 함께 바꿔야 한다.
    }

    // ══════════════════════════════════════════════════════════════

    private static string BackofficeProgramPath() =>
        Path.Combine(RepoRoot(), "src", "HitPan.Backoffice", "Program.cs");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>🔴 주석을 먼저 벗긴다 — 주석에 적힌 글자로 초록이 되는 게이트를 다섯 번 만들었다.</summary>
    private static string StripComments(string text)
    {
        var s = Regex.Replace(text, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        s = Regex.Replace(s, @"^\s*//.*$", " ", RegexOptions.Multiline);
        return s;
    }
}
