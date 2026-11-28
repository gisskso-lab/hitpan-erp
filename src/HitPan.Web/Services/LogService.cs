using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HitPan.Web.Services;

// ⬛ [낡은 줄 · 화면오류기록띠] public sealed class LogService(HttpClient http)
public sealed class LogService(HttpClient http, ILogger<LogService> logger)
{
    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web);

    // 화면오류기록띠 넓힘X1(작업지시서 §8-3 대표 ④) — 로그 화면 조회 2자리에 onLoadFailed 를 더했다.
    //   빈 표 · 오류 수 0 만 남아 「로그가 없다」 로 오해하지 않게 화면이 쉬운 알림을 띄운다 · 돌려주는 값은 그대로.
    // ⬛ [낡은 줄 · 화면오류기록띠 넓힘X1]     int limit = 200, CancellationToken ct = default)
    public async Task<List<AuditTrailRow>> GetAuditTrailAsync(
        DateTime? from = null, DateTime? to = null,
        string? entityType = null, string? actionType = null,
        int limit = 200, CancellationToken ct = default, Action<Exception>? onLoadFailed = null)
    {
        try
        {
            var q = BuildQuery(from, to, limit);
            if (!string.IsNullOrWhiteSpace(entityType)) q += $"&entityType={Uri.EscapeDataString(entityType)}";
            if (!string.IsNullOrWhiteSpace(actionType)) q += $"&actionType={Uri.EscapeDataString(actionType)}";
            return await http.GetFromJsonAsync<List<AuditTrailRow>>($"api/logs/audit-trail{q}", _opts, ct)
                   ?? new List<AuditTrailRow>();
        }
        catch (Exception ex)
        {
            // ⬛ [낡은 줄 · 화면오류기록띠] Console.Error.WriteLine($"[LogService.GetAuditTrail] {ex.Message}");
            logger.LogWarning(ex, "[LogService.GetAuditTrail] 조회 실패");
            onLoadFailed?.Invoke(ex);
            return new List<AuditTrailRow>();
        }
    }

    public async Task<List<ApiRequestRow>> GetApiRequestsAsync(
        DateTime? from = null, DateTime? to = null,
        int? statusCode = null,
        // ⬛ [낡은 줄 · 화면오류기록띠 넓힘X1] int limit = 200, CancellationToken ct = default)
        int limit = 200, CancellationToken ct = default, Action<Exception>? onLoadFailed = null)
    {
        try
        {
            var q = BuildQuery(from, to, limit);
            if (statusCode.HasValue) q += $"&statusCode={statusCode}";
            return await http.GetFromJsonAsync<List<ApiRequestRow>>($"api/logs/api-requests{q}", _opts, ct)
                   ?? new List<ApiRequestRow>();
        }
        catch (Exception ex)
        {
            // ⬛ [낡은 줄 · 화면오류기록띠] Console.Error.WriteLine($"[LogService.GetApiRequests] {ex.Message}");
            logger.LogWarning(ex, "[LogService.GetApiRequests] 조회 실패");
            onLoadFailed?.Invoke(ex);
            return new List<ApiRequestRow>();
        }
    }

    private static string BuildQuery(DateTime? from, DateTime? to, int limit)
    {
        var parts = new List<string> { $"limit={limit}" };
        if (from.HasValue) parts.Add($"from={from.Value:yyyy-MM-dd}");
        if (to.HasValue)   parts.Add($"to={to.Value:yyyy-MM-dd}");
        return "?" + string.Join("&", parts);
    }
}

public sealed class AuditTrailRow
{
    public string LogId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? Reason { get; set; }
    public string? BeforeValue { get; set; }
    public string? AfterValue { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class ApiRequestRow
{
    public string LogId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? Method { get; set; }
    public string? Endpoint { get; set; }
    public int StatusCode { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
}
