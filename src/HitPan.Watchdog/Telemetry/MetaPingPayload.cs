using System.Text.Json.Serialization;

namespace HitPan.Watchdog.Telemetry;

public class MetaPingPayload
{
    [JsonPropertyName("tenant_id_hash")]
    public string TenantIdHash { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "healthy";

    [JsonPropertyName("recent_recovery_count")]
    public int RecentRecoveryCount { get; set; }

    [JsonPropertyName("watchdog_version")]
    public string WatchdogVersion { get; set; } = "1.0.0";

    [JsonPropertyName("process_status")]
    public Dictionary<string, bool> ProcessStatus { get; set; } = new();

    [JsonPropertyName("last_recovery")]
    public LastRecoveryInfo LastRecovery { get; set; } = new();
}

public class LastRecoveryInfo
{
    [JsonPropertyName("stage")]
    public string? Stage { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; set; }
}

public class EmergencyPayload
{
    [JsonPropertyName("tenant_id_hash")]
    public string TenantIdHash { get; set; } = "";

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";

    [JsonPropertyName("stage")]
    public string? Stage { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
}
