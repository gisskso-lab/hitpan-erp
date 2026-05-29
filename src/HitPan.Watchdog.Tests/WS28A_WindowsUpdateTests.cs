using HitPan.Watchdog.Stages;
using Microsoft.Extensions.Logging.Abstractions;

namespace HitPan.Watchdog.Tests;

public class WS28A_WindowsUpdateTests
{
    [Fact]
    public void DetectImminentReboot_DoesNotThrow()
    {
        var a = new WS28A_WindowsUpdate(NullLogger<WS28A_WindowsUpdate>.Instance);
        var ex = Record.Exception(() => a.DetectImminentReboot());
        Assert.Null(ex);
    }
}
