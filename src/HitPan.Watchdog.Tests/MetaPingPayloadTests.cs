using System.Text.Json;
using HitPan.Watchdog.Telemetry;

namespace HitPan.Watchdog.Tests;

public class MetaPingPayloadTests
{
    [Fact]
    public void Serialize_UsesSnakeCaseKeys()
    {
        var p = new MetaPingPayload
        {
            TenantIdHash = "sha256:abc",
            Status = "healthy",
            RecentRecoveryCount = 0,
            WatchdogVersion = "1.0.0",
            ProcessStatus = new() { ["MariaDB"] = true }
        };
        var json = JsonSerializer.Serialize(p);
        Assert.Contains("\"tenant_id_hash\"", json);
        Assert.Contains("\"recent_recovery_count\"", json);
        Assert.Contains("\"watchdog_version\"", json);
        Assert.Contains("\"process_status\"", json);
        Assert.DoesNotContain("\"TenantIdHash\"", json);
    }

    [Fact]
    public void EmergencyPayload_Serialize_UsesSnakeCase()
    {
        var p = new EmergencyPayload
        {
            TenantIdHash = "sha256:abc",
            Reason = "cooldown_exceeded",
            Stage = "WS-28-C",
            Timestamp = DateTime.UtcNow
        };
        var json = JsonSerializer.Serialize(p);
        Assert.Contains("\"tenant_id_hash\"", json);
        Assert.Contains("\"reason\"", json);
        Assert.Contains("\"stage\"", json);
    }

    [Fact]
    public void LastRecoveryInfo_NullableFields_SerializeAsNull()
    {
        var r = new LastRecoveryInfo();
        var json = JsonSerializer.Serialize(r);
        Assert.Contains("\"stage\":null", json);
        Assert.Contains("\"timestamp\":null", json);
    }
}
