using HitPan.Watchdog;
using HitPan.Watchdog.Stages;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HitPan.Watchdog.Tests;

public class WS28E_HealthCheckTests
{
    private sealed class StubHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private static WS28E_ExternalHealthCheck Create(int threshold = 3) =>
        new(NullLogger<WS28E_ExternalHealthCheck>.Instance,
            new StubHttpFactory(),
            Options.Create(new WatchdogOptions { HealthCheckFailThreshold = threshold }));

    [Fact]
    public void IncrementFailure_ReturnsMonotonic()
    {
        var e = Create();
        Assert.Equal(1, e.IncrementFailure());
        Assert.Equal(2, e.IncrementFailure());
        Assert.Equal(3, e.IncrementFailure());
    }

    [Fact]
    public void ShouldTriggerRecovery_BelowThreshold_False()
    {
        var e = Create(threshold: 3);
        e.IncrementFailure();
        e.IncrementFailure();
        Assert.False(e.ShouldTriggerRecovery());
    }

    [Fact]
    public void ShouldTriggerRecovery_AtThreshold_True()
    {
        var e = Create(threshold: 3);
        e.IncrementFailure();
        e.IncrementFailure();
        e.IncrementFailure();
        Assert.True(e.ShouldTriggerRecovery());
    }

    [Fact]
    public void ResetFailure_ClearsStreak()
    {
        var e = Create(threshold: 3);
        e.IncrementFailure();
        e.IncrementFailure();
        e.ResetFailure();
        Assert.Equal(0, e.FailStreak);
        Assert.False(e.ShouldTriggerRecovery());
    }
}
