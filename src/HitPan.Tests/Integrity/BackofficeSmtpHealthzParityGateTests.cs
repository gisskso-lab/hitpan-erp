using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HitPan.Backoffice.API.Controllers;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 이 게이트는 <b>프로세스 전역 환경변수</b>를 바꾼다 — <c>Probe()</c> 가
/// <c>Environment.GetEnvironmentVariable</c> 로 전역을 직접 읽기 때문이다
/// (<c>CredentialsStatusController.cs</c> SMTP 외 칸). 다른 시험과 동시에 돌면 서로를 오염시킨다.
/// 선례: <c>BackupCredentialGateCollection</c>.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BackofficeSmtpHealthzParityGateCollection
{
    public const string Name = "BackofficeSmtpHealthzParityGate";
}

/// <summary>
/// 🔴 <b>20261015작B1 갈래 A — G1 「healthz 거짓 초록」 게이트 (G-B1A-1 ~ G-B1A-6)</b>
///
/// <para><b>[사고]</b> 자격증명 화면(<c>/api/backoffice/credentials/healthz</c>)이 <b>초록</b>인데
/// 메일이 <b>0통</b> 나갔다. 진범 = healthz 의 SMTP 4칸이 <c>Probe(envName, configKey, …)</c> 로
/// <b>환경변수 이름 또는 구성키 둘 중 아무거나</b> 보고 초록을 만들었고, 발송기
/// (<c>EmailSender.cs</c>)는 <c>Smtp:Host</c>·<c>Smtp:User</c>·<c>Smtp:Password</c>·<c>Smtp:From</c>
/// <b>만</b> 읽었다. 두 집합이 달랐다 ⇒ 화면 초록 · 발송 0통.</para>
///
/// <para><b>[정본 이름]</b> <c>Smtp__*</c> (ASP.NET Core 가 <c>__</c> 를 <c>:</c> 로 접는다).
/// <c>HITPAN_SMTP_*</c> 는 <b>폐기</b>다. 폐기 이름이 환경에 들어와 있으면 healthz 는
/// 초록을 주지 않고 <b>경고 1줄</b>을 돌려준다(#15 — 조용히 삼키지 않는다).</para>
///
/// <para>🔴 <b>이 게이트는 글자를 세지 않는다.</b> 실제
/// <see cref="CredentialsStatusController.Healthz"/> 를 <b>환경변수로 조립한 진짜
/// <see cref="IConfiguration"/></b>(<c>AddEnvironmentVariables()</c> — 운영과 같은 조립)로 돌려
/// 돌아오는 <c>allPresent</c> 를 본다.</para>
///
/// <para><b>음성 대조군</b>(2026-10-15 실측 · 봉합 전 코드에 대고 측정) — 12칸 중 11칸을 정본
/// 이름으로 채우고 SMTP 4칸만 <b>폐기 이름</b>(<c>HITPAN_SMTP_*</c>)으로 채웠을 때:
/// <list type="bullet">
///   <item><b>봉합 전</b>: <c>allPresent=true</c> (거짓 초록) · <c>warnings</c> 칸 없음
///     ⇒ G-B1A-1·2·5 <b>빨강</b></item>
///   <item><b>봉합 후</b>: <c>allPresent=false</c> + 경고 1줄 ⇒ 전건 초록</item>
/// </list>
/// ⇒ 봉합을 되돌리면 이 게이트는 실제로 무너진다.</para>
///
/// <para>🔴 <b>시험 값은 전부 런타임 <see cref="Guid"/></b> 다 — 코드·주석·문서에 자격증명
/// 모양 0건(#23 · 작지서 §13 조건4). 메일 실발송 0회(발송기는 토막으로 바꿔 끼운다).</para>
///
/// <para>🔴 <b>DB 의존 0</b> — <c>DbGateEnvironment.SkipOrFailStrict</c> 를 타지 않는다.
/// <c>build</c> 잡에 <c>HITPAN_GATE_SKIP_OK=1</c> 이 선언돼 있어 그 경로를 타면 <c>[SKIP]</c> 한 줄
/// 찍고 <b>PASS</b> 가 되는 「조용한 초록」이 된다(8/28 전례). 이 게이트는 구성 조립 단위시험이라
/// <b>어느 잡에서든 진짜로 돈다</b> — 증거는 「<c>[SKIP]</c> 0건 + 이름 + 소요시간」.</para>
///
/// <para><b>숫자식 3개</b>(PM 교정 2026-10-15 · [3-V] 적발 — 발송기가 읽는 키는 <b>6개</b>인데
/// healthz 판정은 <b>4칸</b>이라 「완전일치」는 성립할 수 없다):
/// <list type="number">
///   <item>healthz 의 SMTP 판정 키 <b>⊆ 발송기가 읽는 키 6개</b> — 초과 <b>0개</b></item>
///   <item>발송기의 <b>차단 키 <c>Smtp:Host</c>·<c>Smtp:User</c></b> 가 healthz 판정에 반드시 포함</item>
///   <item>폐기 이름만 채웠을 때 healthz 초록 <b>0건</b> ← 음성 대조군 본체</item>
/// </list></para>
///
/// <para>근거: <c>docs/운영기록/20261015작B1_배포준비1차수_작업지시서.md</c> §4 갈래 A · §5 G1 행</para>
/// </summary>
[Collection(BackofficeSmtpHealthzParityGateCollection.Name)]
public sealed class BackofficeSmtpHealthzParityGateTests
{
    // ══════════════════════════════════════════════════════════════════════
    // 이름표 — 값은 하나도 없다. 전부 런타임 Guid 로 채운다.
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 발송기(<c>EmailSender.SendInternalAsync</c> · <c>:51-56</c>)가 **읽는** 구성키 전수 = <b>6개</b>.
    /// healthz 가 판정하는 칸은 이 집합의 <b>부분집합</b>이어야 한다(숫자식 ①).
    /// </summary>
    private static readonly string[] SenderReadKeys =
    {
        "Smtp:Host", "Smtp:User", "Smtp:Password", "Smtp:From", "Smtp:FromName", "Smtp:Port"
    };

    /// <summary>
    /// 🔴 발송기가 **차단 판정에 쓰는** 키 = <b>2개</b>(<c>EmailSender.cs:58</c> 의 early-return).
    /// 이 둘이 비면 메일은 확실히 0통이다 ⇒ healthz 판정에 반드시 들어 있어야 한다(숫자식 ②).
    /// (<c>From</c>·<c>FromName</c>·<c>Port</c> 는 코드에 기본값이 있어 없어도 발송은 돈다.)
    /// </summary>
    private static readonly string[] SenderBlockingKeys = { "Smtp:Host", "Smtp:User" };

    /// <summary>healthz 가 실제로 판정하는 SMTP 칸 — 양성 대조군에서 채워야 하는 집합.</summary>
    private static readonly string[] SenderRequiredKeys =
    {
        "Smtp:Host", "Smtp:User", "Smtp:Password", "Smtp:From"
    };

    /// <summary>
    /// SMTP 를 뺀 나머지 칸의 구성키 — healthz 가 묻는 그대로.
    /// <para>🔴 20261019작P0-2·3 교정. 종전 목록은 지우지 않는다(헌법 1번):</para>
    /// <code>
    ///   "Jwt:Key", "Backoffice:JwtKey", "License:Pepper", "Backoffice:BizNoPepper",
    ///   "BizVerify:NtsApiKey", "Toss:ClientKey", "Toss:SecretKey", "Bootstrap:TokenKey"
    /// </code>
    /// <para>🔴 그 목록에 틀린 것이 둘 있었다 — 이 게이트는 <b>초록이면서 덜 재고 있었다</b>:</para>
    /// <list type="bullet">
    ///   <item><c>Jwt:Key</c> — 읽는 소비자가 레포 전체에 0건. 유령 칸이었고 사장님 결재로 <b>삭제됐다</b>.</item>
    ///   <item><c>Backoffice:JwtKey</c> — 읽는 소비자가 0건. 실제 백오피스 JWT 는
    ///     <c>HITPAN_BO_JWT_SECRET</c> → <c>Jwt:Secret</c> 이다(<c>Program.cs:74-76</c>).</item>
    /// </list>
    /// <para>⇒ 채워도 아무 칸을 못 움직이는 키를 채우고 있었다. 정본으로 바꿨다.</para>
    /// </summary>
    private static readonly string[] NonSmtpKeys =
    {
        "Jwt:Secret", "License:Pepper", "Backoffice:BizNoPepper",
        "BizVerify:NtsApiKey", "Toss:ClientKey", "Toss:SecretKey", "Bootstrap:TokenKey"
    };

    /// <summary>폐기된 이름 — 이 이름만 채워도 초록이 되던 자리.</summary>
    private static readonly string[] DeprecatedSmtpEnvNames =
    {
        "HITPAN_SMTP_HOST", "HITPAN_SMTP_USER", "HITPAN_SMTP_PASS", "HITPAN_SMTP_FROM"
    };

    /// <summary>구성키 → 환경변수 정본 이름 (ASP.NET Core 표준 접힘의 역방향).</summary>
    private static string EnvNameOf(string configKey) => configKey.Replace(":", "__");

    // ══════════════════════════════════════════════════════════════════════
    // 대역 — 환경변수를 만지고 반드시 제자리에 돌려놓는다(다른 시험 오염 0).
    // ══════════════════════════════════════════════════════════════════════

    private sealed class EnvScope : IDisposable
    {
        private readonly Dictionary<string, string?> _saved = new(StringComparer.OrdinalIgnoreCase);

        public EnvScope Set(string name, string? value)
        {
            if (!_saved.ContainsKey(name))
                _saved[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
            return this;
        }

        /// <summary>이 시험이 쓰는 이름 전부를 먼저 비운다 — 개발 PC 의 기존 값이 결과를 흔들지 않게.</summary>
        public EnvScope ClearAll()
        {
            foreach (var n in DeprecatedSmtpEnvNames) Set(n, null);
            foreach (var k in SenderReadKeys) Set(EnvNameOf(k), null);   // 6개 전수 — FromName·Port 까지
            foreach (var k in NonSmtpKeys) Set(EnvNameOf(k), null);
            return this;
        }

        /// <summary>SMTP 를 뺀 11칸을 정본 이름 + 런타임 Guid 로 채운다.</summary>
        public EnvScope FillNonSmtp()
        {
            foreach (var k in NonSmtpKeys) Set(EnvNameOf(k), Guid.NewGuid().ToString("N"));
            return this;
        }

        /// <summary>
        /// 🔴 원래 값 복원 — <c>using</c> 이 try/finally 로 감싸므로 시험이 빨강으로 터져도 반드시 돈다.
        /// 없던 이름은 <c>null</c> 로 되돌아가 **없던 상태**가 된다(빈 문자열로 남기지 않는다).
        /// </summary>
        public void Dispose()
        {
            foreach (var kv in _saved)
                Environment.SetEnvironmentVariable(kv.Key, kv.Value);
        }
    }

    private sealed class NoopEmailSender : IEmailSender
    {
        // 메일 실발송 0회 — 이 게이트는 발송을 재지 않는다(§13 조건5).
        public Task<bool> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> SendWithAttachmentAsync(
            string toEmail, string subject, string htmlBody,
            byte[] attachment, string attachmentFileName, string attachmentMimeType,
            CancellationToken ct = default)
            => Task.FromResult(false);
    }

    private sealed class CapturingLogger : ILogger<CredentialsStatusController>
    {
        public readonly List<(LogLevel Level, string Message)> Entries = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class HealthzResult
    {
        public bool AllPresent { get; init; }
        public bool HasWarningsProperty { get; init; }
        public List<string> Warnings { get; init; } = new();
        public List<CredentialsStatusController.CredentialCheck> Items { get; init; } = new();
    }

    /// <summary>실제 컨트롤러를 운영과 같은 구성 조립(AddEnvironmentVariables)으로 돌린다.</summary>
    private static HealthzResult AskHealthz(out CapturingLogger log)
    {
        var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        log = new CapturingLogger();

        var owner = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("role", "owner") }, "test"));

        var controller = new CredentialsStatusController(config, new NoopEmailSender(), log)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = owner }
            }
        };

        var ok = Assert.IsType<OkObjectResult>(controller.Healthz());
        var value = ok.Value!;
        var t = value.GetType();

        // 🔴 reflection 으로 읽는 이유: 응답이 익명형이고, 이 게이트는 **봉합 전 코드에도 컴파일돼야**
        //    한다(음성 대조군을 재려면 봉합 전 판에 대고 돌려야 하므로). warnings 칸은 봉합이 더한다.
        var warnProp = t.GetProperty("warnings");
        var warnValue = warnProp?.GetValue(value) as System.Collections.IEnumerable;

        return new HealthzResult
        {
            AllPresent = (bool)t.GetProperty("allPresent")!.GetValue(value)!,
            HasWarningsProperty = warnProp is not null,
            Warnings = warnValue?.Cast<object>().Select(o => o?.ToString() ?? "").ToList() ?? new List<string>(),
            Items = ((System.Collections.IEnumerable)t.GetProperty("items")!.GetValue(value)!)
                .Cast<CredentialsStatusController.CredentialCheck>().ToList()
        };
    }

    private static List<CredentialsStatusController.CredentialCheck> SmtpItems(HealthzResult r) =>
        r.Items.Where(i => i.Name.StartsWith("SMTP", StringComparison.OrdinalIgnoreCase)).ToList();

    // ══════════════════════════════════════════════════════════════════════
    // G-B1A-1 — 🔴 핵심. 폐기 이름만 채웠을 때 **초록 0건**
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void G_B1A_1_폐기이름만_채우면_healthz_는_초록이_아니다()
    {
        using var env = new EnvScope().ClearAll().FillNonSmtp();
        foreach (var n in DeprecatedSmtpEnvNames)
            env.Set(n, Guid.NewGuid().ToString("N"));   // 값은 런타임 Guid — 자격증명 모양 0건

        var r = AskHealthz(out _);

        Assert.False(r.AllPresent,
            "🔴 거짓 초록. 폐기된 HITPAN_SMTP_* 만 채웠는데 allPresent=true 다. " +
            "발송기는 Smtp:Host/User/Password/From 만 읽으므로 메일은 0통 나간다. " +
            $"SMTP 칸 판정: {string.Join(" / ", SmtpItems(r).Select(i => $"{i.Name}={i.Present}"))}");

        Assert.All(SmtpItems(r), i => Assert.False(i.Present,
            $"🔴 {i.Name} 이 폐기 이름으로 초록이 됐다 — 환경변수 이름 우회 경로가 아직 살아 있다."));
    }

    // ══════════════════════════════════════════════════════════════════════
    // G-B1A-2 — 폐기 이름이 들어와 있으면 **경고 1줄** (#15 조용히 삼키지 않는다)
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void G_B1A_2_폐기이름이_들어와있으면_경고_1줄과_정본이름_안내가_나온다()
    {
        using var env = new EnvScope().ClearAll().FillNonSmtp();
        foreach (var n in DeprecatedSmtpEnvNames)
            env.Set(n, Guid.NewGuid().ToString("N"));

        var r = AskHealthz(out var log);

        Assert.True(r.HasWarningsProperty,
            "🔴 응답에 warnings 칸이 없다 — 폐기 이름이 들어와 있는데 화면이 그 사실을 모른다.");
        Assert.NotEmpty(r.Warnings);

        var all = string.Join("\n", r.Warnings);
        Assert.Contains("Smtp__", all);   // 정본 이름을 알려주는가
        Assert.Contains("HITPAN_SMTP_", all);   // 어느 이름이 안 먹는지 알려주는가

        // 로그에도 남는가 — 화면을 안 보는 사람에게도 보이게(#15)
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Smtp__"));

        // 🔴 #29 — 경고에 값이 새면 안 된다. 값은 Guid("N") = 32자 소문자 16진. 그 모양이 0건이어야 한다.
        Assert.False(Regex.IsMatch(all, "[0-9a-f]{32}"),
            "🔴 경고 문구에 자격증명 값이 섞였다 — 이름만 말해야 한다(#29).");
    }

    // ══════════════════════════════════════════════════════════════════════
    // G-B1A-3 — 양성 대조군. 정본 이름(Smtp__*)으로 채우면 초록이 된다
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void G_B1A_3_정본이름_Smtp__으로_채우면_초록이_되고_경고는_없다()
    {
        using var env = new EnvScope().ClearAll().FillNonSmtp();
        foreach (var k in SenderRequiredKeys)
            env.Set(EnvNameOf(k), Guid.NewGuid().ToString("N"));

        var r = AskHealthz(out _);

        Assert.True(r.AllPresent,
            "🔴 정본 이름으로 다 채웠는데 초록이 아니다 — 봉합이 과하게 막았다(양성 대조군 실패). " +
            $"미충족: {string.Join(", ", r.Items.Where(i => !i.Present).Select(i => i.Name))}");
        Assert.Empty(r.Warnings);
    }

    // ══════════════════════════════════════════════════════════════════════
    // G-B1A-4 — 숫자식 ①② (PM 교정). 「완전일치」가 아니라 **부분집합 + 차단키 포함**.
    //   왜 완전일치가 아닌가: 발송기가 읽는 키는 6개(Host·User·Password·From·FromName·Port)인데
    //   뒤 셋은 기본값이 있어 없어도 돈다. healthz 가 6칸을 다 물면 「Port 안 넣었다」로 빨강이 되어
    //   메일이 가는데도 화면이 빨강인 **반대쪽 거짓말**이 생긴다.
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void G_B1A_4_healthz_판정키는_발송기가_읽는키의_부분집합이고_차단키를_반드시_문다()
    {
        using var env = new EnvScope().ClearAll().FillNonSmtp();
        foreach (var k in SenderRequiredKeys)
            env.Set(EnvNameOf(k), Guid.NewGuid().ToString("N"));

        var r = AskHealthz(out _);

        var judged = SmtpItems(r).Select(i => i.EnvName).ToHashSet(StringComparer.Ordinal);
        var senderRead = SenderReadKeys.Select(EnvNameOf).ToHashSet(StringComparer.Ordinal);

        // ① 초과 0개 — healthz 가 발송기가 **안 읽는** 이름을 보면 그게 거짓 초록의 뿌리다.
        var excess = judged.Except(senderRead).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Empty(excess);

        // ② 차단키 2개는 반드시 포함 — 이게 빠지면 「메일 0통인데 초록」이 그대로 재발한다.
        foreach (var blocking in SenderBlockingKeys)
            Assert.Contains(EnvNameOf(blocking), judged);
    }

    [Fact]
    public void G_B1A_5_발송기_필수키_하나라도_비면_healthz_는_초록이_아니다()
    {
        // 🔴 「넷 중 하나만 빠져도」 — 발송기가 읽는 키마다 healthz 가 1:1 로 움직이는지 전수로 본다.
        foreach (var missing in SenderRequiredKeys)
        {
            using var env = new EnvScope().ClearAll().FillNonSmtp();
            foreach (var k in SenderRequiredKeys)
                env.Set(EnvNameOf(k), k == missing ? null : Guid.NewGuid().ToString("N"));

            var r = AskHealthz(out _);

            Assert.False(r.AllPresent,
                $"🔴 발송기가 읽는 {missing} 가 비었는데 healthz 가 초록이다 — 1:1 이 깨졌다.");
            Assert.Contains(SmtpItems(r), i =>
                i.EnvName == EnvNameOf(missing) && !i.Present);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // G-B1A-6 — 폐기 이름이 코드에서 **판정에 쓰이지 않는다** (되살아나는 것 차단)
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void G_B1A_6_발송기는_Smtp_구성키만_읽고_폐기이름은_판정에_안_쓰인다()
    {
        var root = RepoRoot();

        // 🔴 2026-10-15 [4] 적발 (조건2): 원문을 세면 `_config["Smtp:Host"]` 라고 **설명하는 주석**만 있어도
        //   통과한다(= 발송기가 그 키를 안 읽어도 초록). 주석을 벗기고 본다.
        //   여기선 `StripComments` 다 — 키 이름이 **문자열 리터럴 안**에 있어야 하므로 리터럴은 남겨야 한다.
        // 🔴 왜 소스를 보나: 「발송기가 어느 구성 키를 읽나」는 위 G-B1A-4·5 가 env 1:1 로 재지만,
        //   `GetEnvironmentVariable` 직독이 없다·폐기 이름이 Probe 판정에 안 쓰인다는 **부재(不在)** 는
        //   돌려서 잴 수 없다(없는 코드는 호출되지 않는다). 그래서 이 한 자리는 소스 판독으로 남는다.
        var sender = SourceText.StripComments(File.ReadAllText(Path.Combine(root,
            "src", "HitPan.Backoffice.API", "Services", "EmailSender.cs")));

        // 발송기가 읽는 키 6개가 전부 그 파일에 있다 — 위 기대 집합이 실물과 틀어지면 여기서 터진다.
        foreach (var k in SenderReadKeys)
            Assert.Contains($"_config[\"{k}\"]", sender);

        // 차단키 2개가 발송기의 early-return 조건에 실제로 쓰인다 — 숫자식 ② 의 전제를 고정한다.
        Assert.Matches(
            @"if\s*\(string\.IsNullOrWhiteSpace\(host\)\s*\|\|\s*string\.IsNullOrWhiteSpace\(user\)\)",
            sender);

        // 발송기는 환경변수를 직접 읽지 않는다(구성 한 통로).
        Assert.DoesNotContain("GetEnvironmentVariable", sender);

        // 주석 안의 `Probe("SMTP_HOST"…)` 같은 설명문을 위반으로 세지 않게 — 리터럴은 남긴다(이름을 봐야 한다).
        var controller = SourceText.StripComments(File.ReadAllText(Path.Combine(root,
            "src", "HitPan.Backoffice.API", "Controllers", "CredentialsStatusController.cs")));

        // 폐기 이름이 **env-or-config 판정**(Probe 의 첫 인자)으로 다시 들어가면 거짓 초록이 재발한다.
        foreach (var dead in DeprecatedSmtpEnvNames)
            Assert.False(Regex.IsMatch(controller, @"Probe\(\s*""" + Regex.Escape(dead) + @"""")
                , $"🔴 {dead} 가 다시 Probe 의 판정 이름으로 쓰였다 — G1 봉합이 되돌려졌다.");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))
                               && !Directory.Exists(Path.Combine(dir.FullName, "src", "HitPan.Backoffice.API")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Directory.Exists(Path.Combine(dir!.FullName, "src"))
            ? dir.FullName
            : dir.Parent!.FullName;
    }
}
