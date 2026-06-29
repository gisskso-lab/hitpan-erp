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

    // 봉합 (2026-06-29, 작1 고리2): 워치독이 평가한 "동의 필요(Major)" 업데이트 정보를 본사에 송신한다.
    //   nullable — 펜딩 업데이트가 없으면 null. 구버전 워치독/구버전 본사 API 와 역호환(필드 모르면 무시).
    //   헌법 #22 정합: 버전·채널·안내 문구는 업무 데이터가 아닌 운영 메타데이터다(매출·거래처·직원 0).
    [JsonPropertyName("latest_version")]
    public string? LatestVersion { get; set; }

    [JsonPropertyName("update_channel")]
    public string? UpdateChannel { get; set; }

    [JsonPropertyName("consent_message")]
    public string? ConsentMessage { get; set; }
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
