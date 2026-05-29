using HitPan.Watchdog.Stages;
using Microsoft.Extensions.Logging.Abstractions;

namespace HitPan.Watchdog.Tests;

public class WS28B_PostRebootCheckTests
{
    [Fact]
    public void MarkFlag_ThenClearFlag_NoCrash()
    {
        var b = new WS28B_PostRebootCheck(NullLogger<WS28B_PostRebootCheck>.Instance);
        b.MarkPostRebootCheck();
        b.ClearFlag();
        Assert.False(b.ShouldRunPostRebootCheck());
    }

    [Fact]
    public void NoFlag_ShouldRun_ReturnsFalse()
    {
        var b = new WS28B_PostRebootCheck(NullLogger<WS28B_PostRebootCheck>.Instance);
        b.ClearFlag();
        Assert.False(b.ShouldRunPostRebootCheck());
    }
}
