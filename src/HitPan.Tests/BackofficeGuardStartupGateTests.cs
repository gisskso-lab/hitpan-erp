using System.Collections.Concurrent;
using HitPan.Backoffice.API.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace HitPan.Tests;

// 🔴 20261007작10 게시 전 조건 P-2 게이트 (G-14) — 사장님 결재 2026-10-07 · [5] CTO 결재문 §2 P-2
//
// 재는 것: **기동 한 줄이 실제로 나오는가**, 그리고 **등록이 빠지면 OFF 로 바뀌는가.**
//   가드(BoAccessGuard)는 통과할 때 로그가 0줄이다. 그래서 운영에서 「붙어 있나」를 볼 신호가 없었고,
//   [4] 검증에 「가드가 한 인스턴스에서 미실행으로 보인 1회 관측 · 재현 실패」가 남았다.
//
// 🔴 글자 판정 금지: 「등록했다」를 로그에 박으면 등록 줄을 지운 뒤에도 그 글자가 나온다
//   (작11 P0 가 그 모양이었다 — 주석엔 「super_admin 권한」, 다음 줄은 [AllowAnonymous]).
//   그래서 보고는 실제 MvcOptions.Filters 를 읽어 만들고, 이 게이트는 **실물 호스트를 띄워** 그 줄을 받는다.
//
// ── 봉합 전 FAIL 재현 ────────────────────────────────────────────────
//   Program.cs 의 o.Filters.Add<BoAccessGuard>() 를 지우면 G-14 가 FAIL 한다(ON 줄이 안 나오고 OFF 가 나온다).
//   G-14음 은 그 전환을 **지우지 않고도** 재현한다(Inspect 에 그 필터가 없는 목록을 준다).
public class BackofficeGuardStartupGate : IClassFixture<BackofficeGuardStartupGate.Factory>
{
    private readonly Factory _factory;

    public BackofficeGuardStartupGate(Factory factory) => _factory = factory;

    private const string TestSecret = "W10-P2-GATE-TEST-ONLY-SECRET-32CHARS-MIN-abcdef";

    /// <summary>기동 중 나온 로그를 모은다 — 이 게이트가 재는 대상이 바로 그 줄이다.</summary>
    private sealed class CapturingProvider : ILoggerProvider
    {
        public static readonly ConcurrentQueue<string> Lines = new();
        public ILogger CreateLogger(string categoryName) => new Cap(categoryName);
        public void Dispose() { }

        private sealed class Cap(string cat) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex,
                                    Func<TState, Exception?, string> formatter)
                => Lines.Enqueue($"{level}|{cat}|{formatter(state, ex)}");
        }
    }

    public class Factory : WebApplicationFactory<HitPan.Backoffice.API.Program>
    {
        // 🔴 작11 게이트 M-1 과 같은 이유 — 실물 호스트를 띄우면 WebhookDispatcher 도 깨어나 실제 DB·외부 HTTP 를 건드린다.
        //   연결문자열을 닫힌 포트로 덮어 바깥으로 나가는 패킷을 0으로 만든다(#39 · 외부 실호출 0).
        //   🔴 자격증명은 가짜여도 적지 않는다(TruffleHog 가 가짜도 잡는다) — Uid·Pwd 없이 Port=1 이면 TCP 가 먼저 끊긴다.
        internal const string NoDbConnectionString =
            "Server=127.0.0.1;Port=1;Database=hitpan_w10_p2_gate_nonexistent";

        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Program.cs 가 시크릿 미설정·"DEV-" 접두어면 기동을 거부한다 — 게이트 전용 값을 넣는다.
            Environment.SetEnvironmentVariable("HITPAN_BO_JWT_SECRET", TestSecret);
            Environment.SetEnvironmentVariable("HITPAN_BO_AUTO_MIGRATE", "0");
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:BackofficeDb"] = NoDbConnectionString,
                    ["ConnectionStrings:Default"] = NoDbConnectionString
                }));
            builder.ConfigureLogging(l => l.AddProvider(new CapturingProvider()));
        }
    }

    [Fact(DisplayName = "G-14 🔴 기동 한 줄 — 실물 호스트가 「서버 강제 ON」을 실제로 찍는다(가드는 통과 시 로그 0줄이라 이 줄이 유일한 신호)")]
    public void G14_Startup_Line_Is_Actually_Emitted()
    {
        // 호스트를 실제로 세운다(지연 생성이므로 Services 를 만져 기동을 일으킨다).
        var opts = _factory.Services.GetRequiredService<IOptions<MvcOptions>>().Value;

        var startup = CapturingProvider.Lines
            .Where(l => l.Contains("HitPan.Backoffice.Startup") && l.Contains("[BoAccessGuard]"))
            .ToList();

        Assert.True(startup.Count > 0,
            "[G-14] 기동 줄이 0건 — P-2 의 신호가 없다. Program.cs 의 BoGuardStartupReport 호출을 확인하라.\n"
          + "  받은 로그 중 Startup 범주: " + string.Join(" / ",
                CapturingProvider.Lines.Where(l => l.Contains("Startup")).Take(5)));

        Assert.Contains(startup, l => l.Contains("서버 강제 ON"));
        Assert.DoesNotContain(startup, l => l.Contains("서버 강제 OFF"));
        Assert.Contains(startup, l => l.StartsWith("Information|"));

        // 같은 사실을 보고로도 재확인한다 — 로그 글자와 실제 등록이 **같은 출처**에서 나오는지.
        var report = BoGuardStartupReport.Inspect(opts.Filters);
        Assert.True(report.GuardRegistered, "[G-14] 실물 호스트에 BoAccessGuard 가 등록돼 있지 않다.");
        Assert.True(report.ScopeFilterRegistered, "[G-14] 실물 호스트에 ResellerScopeArgumentFilter 가 등록돼 있지 않다.");
        Assert.True(report.AllRegistered);

        Console.WriteLine($"[G-14] 기동 줄 {startup.Count}건 · 전역 필터 {report.TotalGlobalFilters}개 · 둘 다 등록 확인");
    }

    [Fact(DisplayName = "G-14음 🔴 음성 대조군 — 등록이 빠지면 보고가 OFF 로 바뀐다(「등록했다」 글자를 박지 않았다는 증거)")]
    public void G14_Negative_Control_Missing_Registration_Flips_To_Off()
    {
        // ⓐ 아무 필터도 없는 경우
        var none = BoGuardStartupReport.Inspect(Array.Empty<IFilterMetadata>());
        Assert.False(none.AllRegistered);
        Assert.False(none.GuardRegistered);
        Assert.Contains("서버 강제 OFF", none.Message);
        Assert.Contains("**없음**", none.Message);

        // ⓑ 범위필터만 있고 가드가 빠진 경우 — 36엔드포인트가 다시 열리는 바로 그 상태
        var onlyScope = BoGuardStartupReport.Inspect(
            new IFilterMetadata[] { new TypeFilterAttribute(typeof(ResellerScopeArgumentFilter)) });
        Assert.False(onlyScope.GuardRegistered);
        Assert.True(onlyScope.ScopeFilterRegistered);
        Assert.Contains("서버 강제 OFF", onlyScope.Message);

        // ⓒ 둘 다 있으면 ON — 「전부 OFF 로 찍는다」와 구별이 안 되면 이 검사는 쓸모가 없다
        var both = BoGuardStartupReport.Inspect(new IFilterMetadata[]
        {
            new TypeFilterAttribute(typeof(BoAccessGuard)),
            new TypeFilterAttribute(typeof(ResellerScopeArgumentFilter)),
        });
        Assert.True(both.AllRegistered);
        Assert.Contains("서버 강제 ON", both.Message);
        Assert.Equal(2, both.TotalGlobalFilters);

        Console.WriteLine("[G-14음] 없음→OFF · 가드만 빠짐→OFF · 둘 다→ON 전환 확인");
    }

    [Fact(DisplayName = "G-14ㄷ 🔴 Filters.Add<T>() 는 TypeFilterAttribute 로 들어간다 — 겉 타입만 보면 영원히 못 찾는다")]
    public void G14_Resolves_TypeFilter_Wrapper()
    {
        // 실물 등록 경로와 같은 방법으로 넣는다(MvcOptions.Filters.Add<T>()).
        var opts = new MvcOptions();
        opts.Filters.Add<BoAccessGuard>();
        opts.Filters.Add<ResellerScopeArgumentFilter>();

        // 겉 타입은 TypeFilterAttribute 다 — 이 사실을 게이트가 들고 있어야 다음 사람이 판정식을 안 망친다.
        Assert.All(opts.Filters, f => Assert.IsType<TypeFilterAttribute>(f));

        var report = BoGuardStartupReport.Inspect(opts.Filters);
        Assert.True(report.AllRegistered,
            "[G-14ㄷ] 실물 등록 방식(Filters.Add<T>)을 판정식이 못 풀었다 — 구현 타입을 꺼내야 한다.");
        Console.WriteLine("[G-14ㄷ] TypeFilterAttribute 래퍼를 풀어 구현 타입으로 판정함을 확인");
    }
}
