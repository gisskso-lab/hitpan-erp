using HitPan.Watchdog;
using HitPan.Watchdog.Stages;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HitPan.Watchdog.Tests;

public class WS28F_CoolDownTests
{
    private static WS28F_CoolDown Create(int maxPerHour = 5)
    {
        var opts = Options.Create(new WatchdogOptions { CoolDownMaxPerHour = maxPerHour });
        return new WS28F_CoolDown(NullLogger<WS28F_CoolDown>.Instance, opts);
    }

    [Fact]
    public void AllowRecovery_FirstCall_ReturnsTrue()
    {
        var f = Create();
        Assert.True(f.AllowRecovery("test"));
    }

    [Fact]
    public void AllowRecovery_FifthCall_StillAllowed()
    {
        var f = Create(maxPerHour: 5);
        for (int i = 0; i < 5; i++)
            Assert.True(f.AllowRecovery("test"));
    }

    [Fact]
    public void AllowRecovery_SixthCall_Denied()
    {
        var f = Create(maxPerHour: 5);
        for (int i = 0; i < 5; i++) f.AllowRecovery("test");
        Assert.False(f.AllowRecovery("test"));
    }

    [Fact]
    public void AllowRecovery_DifferentKeys_IndependentBuckets()
    {
        var f = Create(maxPerHour: 5);
        for (int i = 0; i < 5; i++) f.AllowRecovery("keyA");
        Assert.False(f.AllowRecovery("keyA"));
        Assert.True(f.AllowRecovery("keyB"));
    }

    [Fact]
    public void RecentRecoveryCount_TracksTotal()
    {
        var f = Create(maxPerHour: 5);
        f.AllowRecovery("a");
        f.AllowRecovery("b");
        f.AllowRecovery("a");
        Assert.Equal(3, f.RecentRecoveryCount);
    }
}
