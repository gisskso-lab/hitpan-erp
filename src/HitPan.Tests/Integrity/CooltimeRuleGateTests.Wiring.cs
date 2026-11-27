using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using HitPan.API.Services.Cooltime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🆕 11/28 쿨타임 다음 묶음(결재 기록 §288 ③) — 리뷰 P3-2 「서버 작업 배선 게이트 0」 · E-6 W 상수.
/// </summary>
/// <remarks>
/// <para>🔴 <b>글자가 아니라 동작</b>(헌법 메모 「게이트는 글자가 아니라 동작」): <c>Program.cs</c> 를 grep 하지 않는다.
/// ERP API 어셈블리의 <b>진짜 진입점</b>(<see cref="Assembly.EntryPoint"/>)을 이 프로세스에서 부르고, 호스트가
/// 서비스 공급자를 만들기 <b>직전</b>(<c>HostBuilding</c> 진단 이벤트 → <c>ConfigureServices</c> 콜백)에 실제 서비스 목록을
/// 복사한 뒤 예외로 멈춘다. ⇒ <c>builder.Build()</c> 앞의 등록 코드는 실물 그대로 돌고, 그 뒤(마이그 적용 · DB 연결 ·
/// <c>app.Run()</c>)는 한 줄도 안 돈다. <c>WebApplicationFactory</c> 가 최소 호스팅에서 쓰는 길(<c>HostFactoryResolver</c>)과 같은 원리다.</para>
/// <para>왜 <c>WebApplicationFactory&lt;Program&gt;</c> 가 아닌가: ① ERP API 의 <c>Program</c> 은 최상위 문장이라 <c>internal</c> 이고
/// 레포에 <c>InternalsVisibleTo</c> 가 없다 ② 그 장치는 호스트를 <b>시작</b>한다 — 서버 작업(쿨타임 바퀴 · 마이그 적용 등)이
/// 실제로 돌며 DB 에 붙는다. 이 PC 기본 DB 는 실물이라 그 길은 쓸 수 없다. 여기서는 공급자를 만들기 전에 멈추므로
/// 서버 작업 객체가 <b>하나도 만들어지지 않는다</b>.</para>
/// <para>안전장치: 진입점을 부르기 전에 같은 가로채기를 빈 <see cref="HostApplicationBuilder"/> 로 먼저 시험한다(가로채기가 안 먹는
/// 런타임이면 진입점을 부르지 않고 FAIL). 그래도 <c>HostBuilding</c> 을 못 잡으면 <c>HostBuilt</c> 에서 다시 멈춘다(<c>Build()</c> 안 ·
/// 마이그 적용보다 앞).</para>
/// </remarks>
public sealed partial class CooltimeRuleGateTests
{
    [Fact(DisplayName = "G-C24 서버 작업 배선(리뷰 P3-2) — ERP API 진입점을 실제로 돌려 Build 직전 서비스 목록: IHostedService = CooltimeService 1개 · CooltimeEngine Scoped · ICooltimePusher = SignalRCooltimePusher · 대조(다른 서버 작업도 보임)")]
    public void G_C24_진입점_서비스목록에_쿨타임_서버작업이_있다()
    {
        var services = ErpApiHostCapture.CaptureServices();

        // 양성 대조 — 「무엇을 세고 있는지」 먼저: 진짜 Program.cs 목록이면 수백 줄 · 다른 서버 작업도 보인다.
        Assert.True(services.Count > 100, $"[G-C24] 잡은 서비스가 {services.Count}줄뿐 — 진입점 목록이 아니다");
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService)
                                    && d.ImplementationType?.FullName == "HitPan.API.Services.CsAutoDispatchService");

        // 본체 — 배선 세 줄(Program.cs 「20260928작4 쿨타임」 덩이)
        var hosted = services.Where(d => d.ServiceType == typeof(IHostedService)
                                      && d.ImplementationType == typeof(CooltimeService)).ToList();
        Assert.True(hosted.Count == 1,
            $"[G-C24] IHostedService = CooltimeService 등록이 {hosted.Count}개 — 0 이면 쿨타임 바퀴가 조용히 안 돈다(대조군 ⓨ)");
        Assert.Equal(ServiceLifetime.Singleton, hosted[0].Lifetime);

        Assert.Contains(services, d => d.ServiceType == typeof(CooltimeEngine) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d => d.ServiceType == typeof(ICooltimePusher)
                                    && d.ImplementationType == typeof(SignalRCooltimePusher)
                                    && d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact(DisplayName = "G-C22 상수 — 규칙 ④ 폭 W = 4시간(대표 결재 §287 E-6 (가)) · 설정 칸 아님(상수)")]
    public void G_C22_규칙4_폭은_4시간()
    {
        Assert.Equal(4, CooltimeWire.RuleFourWindowHours);
        var f = typeof(CooltimeWire).GetField(nameof(CooltimeWire.RuleFourWindowHours), BindingFlags.Public | BindingFlags.Static);
        Assert.True(f is { IsLiteral: true }, "W 는 const 여야 한다(설정 칸·화면 0 · 설계 §E :297)");
    }
}

/// <summary>
/// ERP API 진입점을 돌려 <c>builder.Build()</c> 직전의 서비스 목록을 복사하고 멈추는 장치(판정 0 · 복사만).
/// </summary>
internal static class ErpApiHostCapture
{
    private const string HostingListener = "Microsoft.Extensions.Hosting";
    private static readonly AsyncLocal<bool> Active = new();
    private static readonly object OneAtATime = new();

    private sealed class StopBeforeProvider : Exception
    {
        public StopBeforeProvider(string where) : base("[G-C24] 서비스 공급자 만들기 전에 멈춤 — " + where) { }
    }

    public static IReadOnlyList<ServiceDescriptor> CaptureServices()
    {
        lock (OneAtATime)
        {
            // 진입점 등록 코드가 요구하는 값(AuthExtensions — JWT_SECRET 없으면 예외). 레포에 값을 남기지 않는다(매 실행 무작위 · CooltimeDbGateTests 와 같은 이유).
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JWT_SECRET")))
                Environment.SetEnvironmentVariable("JWT_SECRET", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));

            // ① 안전장치 — 가로채기가 이 런타임에서 먹는지 빈 호스트로 먼저 본다. 안 먹으면 진입점을 부르지 않는다.
            var probe = Run(() => Host.CreateApplicationBuilder(Array.Empty<string>()).Build());
            Assert.True(probe.Captured is not null,
                "[G-C24] HostBuilding 가로채기가 빈 호스트에서도 안 먹는다 — 진입점을 부르면 Build 뒤(마이그 · DB · Run)까지 갈 수 있어 부르지 않았다");

            // ② 본체 — ERP API 진짜 진입점(<Main>)
            var entry = typeof(CooltimeService).Assembly.EntryPoint;
            Assert.True(entry is not null, "[G-C24] HitPan.API 어셈블리에 진입점이 없다");

            // 등록 코드가 요구하는 DB 설정 키(AddInfrastructure — 연결 문자열을 「만들기만」 한다 · 여는 곳은 Build 뒤).
            //   비어 있을 때만 채우고 끝나면 되돌린다. 호스트 = `.invalid`(RFC 2606 — 절대 안 풀리는 이름) ⇒ 혹시 열려도 어디에도 못 붙는다.
            var filled = new List<string>();
            foreach (var (key, value) in new[]
                     {
                         ("DB_HOST", "gate-never-connect.invalid"), ("DB_NAME", "gate_never_connect"),
                         ("DB_USER", "gate_never_connect"), ("DB_PASSWORD", Convert.ToHexString(RandomNumberGenerator.GetBytes(16))),
                     })
            {
                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key))) continue;
                Environment.SetEnvironmentVariable(key, value);
                filled.Add(key);
            }
            try
            {
                var real = Run(() => entry!.Invoke(null, new object[] { Array.Empty<string>() }));
                Assert.True(real.Captured is not null,
                    $"[G-C24] 진입점이 HostBuilding 전에 끝났거나 다른 예외로 멈췄다: {Describe(real.Other)}");
                return real.Captured!;
            }
            finally
            {
                foreach (var key in filled) Environment.SetEnvironmentVariable(key, null);
                // 진입점이 바꾼 전역 로거(Serilog)를 닫는다 — 파일 손잡이를 남기지 않는다.
                Serilog.Log.CloseAndFlush();
            }
        }
    }

    private sealed record RunResult(List<ServiceDescriptor>? Captured, Exception? Other);

    private static RunResult Run(Action body)
    {
        List<ServiceDescriptor>? captured = null;
        var subs = new List<IDisposable>();
        var kv = new KvObserver(
            onBuilding: hb => hb.ConfigureServices((_, services) =>
            {
                captured = services.ToList();
                throw new StopBeforeProvider("HostBuilding");
            }),
            onBuilt: () => throw new StopBeforeProvider("HostBuilt(뒷문)"));
        subs.Add(DiagnosticListener.AllListeners.Subscribe(new AllObserver(l =>
        {
            if (l.Name == HostingListener) lock (subs) subs.Add(l.Subscribe(kv));
        })));

        Exception? other = null;
        var stopped = false;
        Active.Value = true;
        try { body(); }
        catch (Exception ex) when (FindStop(ex))
        {
            stopped = true;   // 기대한 멈춤 — Build() 안에서 우리가 던졌다(#15 — 삼키지 않고 결과에 싣는다)
        }
        catch (Exception ex)
        {
            other = ex;
        }
        finally
        {
            Active.Value = false;
            lock (subs) foreach (var s in subs) s.Dispose();
        }
        return new RunResult(stopped ? captured : null, other);
    }

    private static string Describe(Exception? ex)
    {
        var parts = new List<string>();
        for (; ex is not null; ex = ex.InnerException) parts.Add($"{ex.GetType().Name}: {ex.Message}");
        return parts.Count == 0 ? "(예외 없음 — 진입점이 그냥 끝났다)" : string.Join(" → ", parts);
    }

    private static bool FindStop(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is StopBeforeProvider) return true;
            if (ex is AggregateException agg && agg.InnerExceptions.Any(FindStop)) return true;
        }
        return false;
    }

    private sealed class AllObserver : IObserver<DiagnosticListener>
    {
        private readonly Action<DiagnosticListener> _onNext;
        public AllObserver(Action<DiagnosticListener> onNext) => _onNext = onNext;
        public void OnNext(DiagnosticListener value) => _onNext(value);
        public void OnCompleted() { /* 끝 신호 — 할 일 없음 */ }
        public void OnError(Exception error) { /* 진단 채널 오류 — 판정은 RunResult 가 한다 */ }
    }

    private sealed class KvObserver : IObserver<KeyValuePair<string, object?>>
    {
        private readonly Action<IHostBuilder> _onBuilding;
        private readonly Action _onBuilt;
        public KvObserver(Action<IHostBuilder> onBuilding, Action onBuilt) { _onBuilding = onBuilding; _onBuilt = onBuilt; }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (!Active.Value) return;   // 다른 시험(다른 흐름)의 호스트는 건드리지 않는다
            if (value.Key == "HostBuilding" && value.Value is IHostBuilder hb) _onBuilding(hb);
            else if (value.Key == "HostBuilt") _onBuilt();
        }

        public void OnCompleted() { /* 끝 신호 — 할 일 없음 */ }
        public void OnError(Exception error) { /* 진단 채널 오류 — 판정은 RunResult 가 한다 */ }
    }
}
