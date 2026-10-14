using System.Text.RegularExpressions;
using HitPan.API.Services.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// ERP 메일 스텁이 <b>보내지 않고 「보냈다」고 말하지 않는지</b> 지키는 게이트
/// (20261015작B1 갈래 C · G4).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>무엇을 막나</b> — <c>EmailSenderService.SendAsync</c> 의 스텁 경로가
/// 로그만 찍고 <c>NotificationResult(<b>true</b>, "stub-…")</c> 를 돌려줬다.
/// 메일은 한 통도 나가지 않는데 호출자에게는 <b>성공</b>으로 보인다.
/// </para>
/// <para>
/// ⚠️ <b>왜 여태 안 터졌나</b> — <b>호출자가 0곳</b>이다(죽은 통로).
/// <c>Program.cs</c> 의 DI 등록 한 줄뿐이고 주입·호출 전수 0건이라 아무도 안 속았다.
/// 그래서 이 통로를 <b>제거하지 않았다</b>(#37 「안 읽힌다 ≠ 잔재」) — 반환값만 정직하게 바꿨다.
/// 누가 이 통로를 살리는 순간 「성공했다는데 메일이 안 온다」로 터진다.
/// </para>
/// <para>
/// 🔴 <b>음성 대조군</b> — 정직화를 되돌리면(<c>false</c> → <c>true</c>)
/// <see cref="스텁모드_SendAsync_는_보냈다고_말하지_않는다"/> 가 <b>빨강</b>이 된다.
/// 봉합 전 실측 출력은 개발명세서
/// <c>docs/개발/erp/20261015_개발명세서_작B1_갈래C_스텁정직화.md</c> §4 에 있다.
/// </para>
/// <para>
/// ⚠️ <b>이 게이트가 재지 않는 것</b> — <c>EmailSenderService</c> 의 임시비번 차단 필터
/// (낱말 포함만 보고 인접 패턴은 안 본다)는 <b>이 차수 범위 밖</b>이다. 주석으로만 남겼다.
/// </para>
/// <para>근거: <c>docs/운영기록/20261015작B1_배포준비1차수_작업지시서.md</c> §4 갈래 C · §5 G4.</para>
/// </remarks>
public class ErpMailStubHonestGateTests
{
    /// <summary>src 폴더를 찾는다(HitPan.sln 이 src 안에 있다) — 시험은 bin 아래서 돌기 때문에 위로 올라가며 찾는다.</summary>
    private static string FindSrcRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 시험이 소스를 읽을 수 없다.");
    }

    /// <summary>발송기를 조립한다. 🔴 열쇠값은 자격증명 「모양」을 남기지 않도록 런타임 Guid 로 만든다(결재조건 4).</summary>
    private static EmailSenderService Build(string? provider, bool withApiKey)
    {
        var settings = new Dictionary<string, string?>();
        if (provider is not null) settings["Notifications:Email:Provider"] = provider;
        if (withApiKey) settings["Notifications:Email:ApiKey"] = Guid.NewGuid().ToString("N");

        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new EmailSenderService(NullLogger<EmailSenderService>.Instance, config);
    }

    private const string To = "nobody@example.invalid";
    private const string Subject = "게이트 시험";
    private const string Body = "활성화 링크 안내";

    /// <summary>🔴 음성 대조군의 과녁 — 스텁 모드에서 성공이라고 말하면 빨강.</summary>
    [Theory]
    // 열쇠 없음 + provider 기본값(설정 0건) → 미설정
    [InlineData(null, false, "not_configured")]
    // 열쇠 없음 + provider 를 stub 으로 명시 → 미설정 (보낼 수단이 없다)
    [InlineData("stub", false, "not_configured")]
    // 열쇠는 있는데 provider 가 stub → 스텁 모드
    [InlineData("stub", true, "stub_mode")]
    // 열쇠도 provider 도 있으나 벤더 SDK 가 아직 안 붙었다 → 스텁 모드
    [InlineData("ses", true, "stub_mode")]
    [InlineData("sendgrid", true, "stub_mode")]
    public async Task 스텁모드_SendAsync_는_보냈다고_말하지_않는다(string? provider, bool withApiKey, string expectedReason)
    {
        var result = await Build(provider, withApiKey).SendAsync(To, Subject, Body, "token-0000");

        Assert.False(
            result.Success,
            $"메일을 한 통도 보내지 않았는데 Success=true 를 돌려줬다 " +
            $"(provider={provider ?? "(설정없음)"} · 열쇠={(withApiKey ? "있음" : "없음")}). " +
            "보내지 않았으면 보냈다고 하지 않는다.");

        Assert.Equal(expectedReason, result.Error);

        Assert.Null(result.VendorMessageId);
    }

    /// <summary>사유코드는 정확히 2종(미설정 · 스텁)이어야 한다 — §5 G4 숫자.</summary>
    [Fact]
    public async Task 사유코드는_미설정_스텁_2종이다()
    {
        var reasons = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (provider, withKey) in new (string?, bool)[]
                 { (null, false), ("stub", false), ("stub", true), ("ses", true), ("sendgrid", true) })
        {
            var r = await Build(provider, withKey).SendAsync(To, Subject, Body, "token-0000");
            Assert.NotNull(r.Error);
            reasons.Add(r.Error!);
        }

        Assert.Equal(new[] { "not_configured", "stub_mode" }, reasons.ToArray());
    }

    /// <summary>이미 정직했던 경로(잘못된 수신자 · 임시비번 차단)는 그대로 false 여야 한다 — 되돌림 탐지.</summary>
    [Theory]
    [InlineData("주소아님", "활성화 링크 안내", "invalid_recipient")]
    [InlineData(To, "임시비번 안내입니다", "blocked_temp_password_in_email")]
    public async Task 원래_정직했던_거부경로는_유지된다(string to, string body, string expectedReason)
    {
        var result = await Build("ses", true).SendAsync(to, Subject, body, "token-0000");

        Assert.False(result.Success);
        Assert.Equal(expectedReason, result.Error);
    }

    /// <summary>
    /// 🔴 소스에 <c>true</c> 반환 경로가 <b>0개</b>인지 — §5 G4 숫자.
    /// 동작 시험(위)만 두면 새 분기를 하나 더 만들면서 다시 <c>true</c> 를 넣어도 안 잡힌다.
    /// </summary>
    [Fact]
    public void 발송기_소스에_성공반환_경로가_0개다()
    {
        var path = Path.Combine(
            FindSrcRoot(), "HitPan.API", "Services", "Notifications", "EmailSenderService.cs");
        Assert.True(File.Exists(path), $"발송기 소스를 못 찾았다: {path}");

        var offenders = new List<string>();
        var lines = File.ReadAllLines(path);
        for (var i = 0; i < lines.Length; i++)
        {
            var code = lines[i];
            // 주석줄은 뺀다 — 설명 문장에 true 가 나올 수 있다.
            if (code.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
            if (Regex.IsMatch(code, @"new\s+NotificationResult\s*\(\s*true"))
                offenders.Add($"{i + 1}: {code.Trim()}");
        }

        Assert.True(
            offenders.Count == 0,
            "EmailSenderService 는 실제 전송 코드가 0줄이다 — 성공(true)을 돌려주는 경로가 있으면 거짓이다.\n"
            + string.Join("\n", offenders));
    }
}
