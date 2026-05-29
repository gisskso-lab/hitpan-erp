using HitPan.Watchdog.Telemetry;

namespace HitPan.Watchdog.Tests;

public class MetaPingClientTests
{
    [Fact]
    public void Sha256_Prefixed_LowerHex()
    {
        var h = MetaPingClient.Sha256("hello");
        Assert.StartsWith("sha256:", h);
        Assert.Equal(7 + 64, h.Length);
        Assert.Equal(h.ToLowerInvariant(), h);
    }

    [Fact]
    public void Sha256_StableForSameInput()
    {
        Assert.Equal(MetaPingClient.Sha256("tenant-001"), MetaPingClient.Sha256("tenant-001"));
    }

    [Fact]
    public void Sha256_DifferentForDifferentInputs()
    {
        Assert.NotEqual(MetaPingClient.Sha256("a"), MetaPingClient.Sha256("b"));
    }

    [Fact]
    public void GetTenantId_FallsBackToUnknown_WhenEnvMissing()
    {
        Environment.SetEnvironmentVariable("HITPAN_TENANT_ID", null);
        var id = MetaPingClient.GetTenantId();
        Assert.False(string.IsNullOrEmpty(id));
    }
}
