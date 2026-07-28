using System.Reflection;
using HitPan.Watchdog;

namespace HitPan.Watchdog.Tests;

public class HealthProbeTests
{
    [Fact]
    public void HealthReport_Defaults_AreSafe()
    {
        var r = new HealthProbe.HealthReport();
        Assert.Equal("unknown", r.OverallStatus);
        Assert.NotNull(r.ProcessStatus);
        Assert.Empty(r.ProcessStatus);
        Assert.NotNull(r.Environment);
    }

    [Fact]
    public void EnvSnapshot_Defaults_AreSafe()
    {
        var e = new HealthProbe.EnvSnapshot();
        Assert.False(e.HasTunnelId);
        Assert.False(e.HasLicenseKey);
        Assert.False(e.HasTenantId);
        Assert.False(e.IsElevated);
    }

    [Theory]
    [InlineData(true, false, false, "down")]      // ExternalHealthOk false
    [InlineData(false, true, false, "recovering")] // tunnel invalid
    [InlineData(false, false, true, "recovering")] // cloudflared service missing
    [InlineData(false, false, false, "healthy")]   // all good
    public void ComputeOverall_AppliesRulesInOrder(bool externalDown, bool tunnelInvalid, bool cfMissing, string expected)
    {
        var r = new HealthProbe.HealthReport
        {
            ExternalHealthOk = !externalDown,
            TunnelSecretInvalid = tunnelInvalid,
            CloudflaredServiceExists = !cfMissing,
            ProcessStatus = new Dictionary<string, bool>
            {
                ["MariaDB"] = true,
                ["cloudflared"] = !cfMissing,
                ["HitPan.API"] = true,
                ["HitPan.Web"] = true
            }
        };

        var method = typeof(HealthProbe).GetMethod("ComputeOverall",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = (string)method.Invoke(null, new object[] { r })!;

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ComputeOverall_AnyProcessDown_ReturnsRecovering()
    {
        var r = new HealthProbe.HealthReport
        {
            ExternalHealthOk = true,
            TunnelSecretInvalid = false,
            CloudflaredServiceExists = true,
            ProcessStatus = new Dictionary<string, bool>
            {
                ["MariaDB"] = false,    // ← 1개라도 down
                ["cloudflared"] = true,
                ["HitPan.API"] = true,
                ["HitPan.Web"] = true
            }
        };
        var method = typeof(HealthProbe).GetMethod("ComputeOverall",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Equal("recovering", method.Invoke(null, new object[] { r }));
    }
}
