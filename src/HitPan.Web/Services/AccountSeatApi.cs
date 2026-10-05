using System.Net;
using System.Net.Http.Json;
using HitPan.Web.Models;
using Microsoft.Extensions.Logging;

namespace HitPan.Web.Services;

/// <summary>/users 계정 한도 카드 숫자 (GET api/users/seats).</summary>
public sealed class AccountSeatsModel
{
    public int Active { get; set; }
    public int BaseLimit { get; set; }
    public int Extra { get; set; }
    public int Limit { get; set; }
    public string Tier { get; set; } = "basic";
}

/// <summary>호출 결과 — 한도가 차서 막혔는지(409 account_seat_full)를 따로 알린다.</summary>
public sealed record AccountSeatCallResult(bool Ok, bool SeatFull, string? Error);

/// <summary>엑셀 일괄 결과 + 한도 사전 판정 결과.</summary>
public sealed class AccountSeatBulkResult : BulkCreateResult
{
    public bool SeatFull { get; set; }
    public int Active { get; set; }
    public int Limit { get; set; }
}

/// <summary>
/// 20261005작3 — /users 계정 과금 API 호출. DI 등록 없이 화면이 주입받은 HttpClient 로 만든다
/// (Web <c>Program.cs</c>·<c>UserService</c> 무접촉 — 한 파일 한 주인).
/// </summary>
public sealed class AccountSeatApi(HttpClient http, ILogger logger)
{
    private const string SeatFullCode = "account_seat_full";

    public async Task<AccountSeatsModel?> GetSeatsAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<AccountSeatsModel>("api/users/seats", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "계정 한도 조회 실패");
            return null;
        }
    }

    public Task<AccountSeatCallResult> CreateAsync(CreateUserModel model, CancellationToken ct = default) =>
        SendAsync(() => http.PostAsJsonAsync("api/users", model, ct), ct);

    public Task<AccountSeatCallResult> UpdateAsync(string userId, UpdateUserModel model, CancellationToken ct = default) =>
        SendAsync(() => http.PutAsJsonAsync($"api/users/{userId}", model, ct), ct);

    public Task<AccountSeatCallResult> SuspendAsync(string userId, CancellationToken ct = default) =>
        SendAsync(() => http.PostAsync($"api/users/{userId}/suspend", null, ct), ct);

    public Task<AccountSeatCallResult> ResumeAsync(string userId, CancellationToken ct = default) =>
        SendAsync(() => http.PostAsync($"api/users/{userId}/resume", null, ct), ct);

    /// <summary>계정폐기 — DELETE 의 뜻이 바뀌었다(10/5 작3).</summary>
    public Task<AccountSeatCallResult> RetireAsync(string userId, CancellationToken ct = default) =>
        SendAsync(() => http.DeleteAsync($"api/users/{userId}", ct), ct);

    public async Task<AccountSeatBulkResult?> BulkUploadAsync(Stream stream, string fileName, CancellationToken ct = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            using var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            content.Add(fileContent, "file", fileName);

            var res = await http.PostAsync("api/users/bulk", content, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return new AccountSeatBulkResult { FailedCount = -1, Errors = new() { new() { Row = 0, Reason = err } } };
            }
            return await res.Content.ReadFromJsonAsync<AccountSeatBulkResult>(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "엑셀 일괄등록 실패");
            return new AccountSeatBulkResult { FailedCount = -1, Errors = new() { new() { Row = 0, Reason = ex.Message } } };
        }
    }

    private async Task<AccountSeatCallResult> SendAsync(Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        try
        {
            using var res = await send().ConfigureAwait(false);
            if (res.IsSuccessStatusCode) return new(true, false, null);
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var seatFull = res.StatusCode == HttpStatusCode.Conflict && body.Contains(SeatFullCode, StringComparison.Ordinal);
            return new(false, seatFull, ReadMessage(body));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "계정 처리 호출 실패");
            return new(false, false, ex.Message);
        }
    }

    /// <summary>서버 JSON 의 message 만 꺼낸다(개발 용어·코드 노출 0).</summary>
    private static string ReadMessage(string body)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && doc.RootElement.TryGetProperty("message", out var m))
                return m.GetString() ?? "처리하지 못했습니다.";
        }
        catch (System.Text.Json.JsonException)
        {
            return string.IsNullOrWhiteSpace(body) ? "처리하지 못했습니다." : body;
        }
        return "처리하지 못했습니다.";
    }
}
