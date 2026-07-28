using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HitPan.Web.Services;

public sealed class LogService(HttpClient http)
{
    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web);

    public async Task<List<AuditTrailRow>> GetAuditTrailAsync(
        DateTime? from = null, DateTime? to = null,
        string? entityType = null, string? actionType = null,
        int limit = 200, CancellationToken ct = default)
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
            Console.Error.WriteLine($"[LogService.GetAuditTrail] {ex.Message}");
            return new List<AuditTrailRow>();
        }
    }

    public async Task<List<ApiRequestRow>> GetApiRequestsAsync(
        DateTime? from = null, DateTime? to = null,
        int? statusCode = null,
        int limit = 200, CancellationToken ct = default)
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
            Console.Error.WriteLine($"[LogService.GetApiRequests] {ex.Message}");
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
