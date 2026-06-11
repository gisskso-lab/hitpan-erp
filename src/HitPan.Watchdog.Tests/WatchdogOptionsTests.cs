using HitPan.Watchdog;

namespace HitPan.Watchdog.Tests;

public class WatchdogOptionsTests
{
    [Fact]
    public void Defaults_MatchConstitution()
    {
        var o = new WatchdogOptions();
        Assert.Equal(60, o.LoopIntervalSeconds);
        Assert.Equal(10, o.HealthCheckTimeoutSeconds);
        Assert.Equal(3, o.HealthCheckFailThreshold);
        Assert.Equal(5, o.MetaPingIntervalMinutes);
        Assert.Equal(5, o.CoolDownMaxPerHour);  // 헌법 #28-F
        Assert.Equal("https://back.hitpan.kr/watchdog/ping", o.MetaPingEndpoint);
        Assert.Equal("https://back.hitpan.kr/watchdog/emergency", o.MetaPingEmergencyEndpoint);
    }

    [Fact]
    public void ProcessesConfig_DefaultsToEmpty()
    {
        var p = new ProcessesConfig();
        Assert.NotNull(p.Services);
        Assert.NotNull(p.HttpEndpoints);
        Assert.Empty(p.Services);
        Assert.Empty(p.HttpEndpoints);
    }
}
