using HitPan.Watchdog.Stages;
using Microsoft.Extensions.Logging.Abstractions;

namespace HitPan.Watchdog.Tests;

public class WS28D_ServiceReinstallTests
{
    [Fact]
    public void ServiceExists_UnknownService_ReturnsFalse()
    {
        var d = new WS28D_ServiceReinstall(NullLogger<WS28D_ServiceReinstall>.Instance);
        Assert.False(d.ServiceExists("__never_exists_xyz__"));
    }
}
