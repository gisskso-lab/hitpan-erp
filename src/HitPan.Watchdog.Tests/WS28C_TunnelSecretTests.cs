using HitPan.Watchdog.Stages;
using Microsoft.Extensions.Logging.Abstractions;

namespace HitPan.Watchdog.Tests;

public class WS28C_TunnelSecretTests
{
    [Fact]
    public void DetectInvalidSecret_NoLog_ReturnsFalse()
    {
        var c = new WS28C_TunnelSecret(NullLogger<WS28C_TunnelSecret>.Instance);
        Assert.False(c.DetectInvalidSecret());
    }

    [Fact]
    public async Task RegenerateAsync_NoEnvVar_ReturnsFalse()
    {
        var saved = Environment.GetEnvironmentVariable("HITPAN_TUNNEL_ID");
        try
        {
            Environment.SetEnvironmentVariable("HITPAN_TUNNEL_ID", null);
            var c = new WS28C_TunnelSecret(NullLogger<WS28C_TunnelSecret>.Instance);
            var ok = await c.RegenerateAsync();
            Assert.False(ok);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HITPAN_TUNNEL_ID", saved);
        }
    }
}
