using System.Net.Http.Json;
using System.Text.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

public sealed class PartnerMasterService(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<List<PartnerListRow>> GetListAsync(
        string? search = null,
        string? type = null,
        CancellationToken ct = default)
    {
        try
        {
            var qs = new List<string>();
            if (!string.IsNullOrWhiteSpace(search))
            {
                qs.Add("search=" + Uri.EscapeDataString(search.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(type))
            {
                qs.Add("type=" + Uri.EscapeDataString(type.Trim()));
            }

            var path = "api/partners" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
            var list = await http.GetFromJsonAsync<List<PartnerListRow>>(path, JsonOptions, ct);
            return list ?? new List<PartnerListRow>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PartnerMasterService.GetListAsync] Error: {ex.Message}");
            return new List<PartnerListRow>();
        }
    }

    public async Task<PartnerDetailModel?> GetAsync(string id, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<PartnerDetailModel>($"api/partners/{Uri.EscapeDataString(id)}", JsonOptions, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PartnerMasterService.GetAsync] Error: {ex.Message}");
            return null;
        }
    }

    public async Task<(bool ok, string? errorMessage)> CreateAsync(PartnerDetailModel model, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/partners", model, ct);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct);
                Console.WriteLine($"[PartnerMasterService.CreateAsync] {(int)res.StatusCode}: {body}");
                var msg = ExtractErrorMessage(body);
                return (false, msg);
            }
            return (true, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PartnerMasterService.CreateAsync] Error: {ex.Message}");
            return (false, null);
        }
    }

    public async Task<(bool ok, string? errorMessage)> UpdateAsync(string id, PartnerDetailModel model, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PutAsJsonAsync($"api/partners/{Uri.EscapeDataString(id)}", model, ct);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct);
                Console.WriteLine($"[PartnerMasterService.UpdateAsync] {(int)res.StatusCode}: {body}");
                var msg = ExtractErrorMessage(body);
                return (false, msg);
            }
            return (true, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PartnerMasterService.UpdateAsync] Error: {ex.Message}");
            return (false, null);
        }
    }

    private static string? ExtractErrorMessage(string body)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var msgEl))
                return msgEl.GetString();
        }
        catch { /* JSON이 아닌 응답 — 의도된 무시, 호출자가 기본 메시지 사용 */ }
        return null;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.DeleteAsync($"api/partners/{Uri.EscapeDataString(id)}", ct);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct);
                Console.WriteLine($"[PartnerMasterService.DeleteAsync] {(int)res.StatusCode}: {body}");
            }
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PartnerMasterService.DeleteAsync] Error: {ex.Message}");
            return false;
        }
    }

    public async Task<List<PartnerListRow>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<PartnerListRow>();
        }

        return await GetListAsync(query, null, ct);
    }

    /// <summary>
    /// AR Aging 연체 버킷 — 전 매출처 연체 현황 (`v_partner_aging_buckets` 뷰).
    /// 매출처 리스트 페이지 연체 컬럼·배지 표시용.
    /// </summary>
    public async Task<List<PartnerAgingRow>> GetAgingAsync(CancellationToken ct = default)
    {
        try
        {
            var rows = await http.GetFromJsonAsync<List<PartnerAgingRow>>("api/partners/aging", JsonOptions, ct);
            return rows ?? new();
        }
        catch
        {
            return new();
        }
    }
}

public sealed class PartnerAgingRow
{
    public string PartnerId { get; set; } = "";
    public string PartnerName { get; set; } = "";
    public int OpenInvoices { get; set; }
    public decimal Bucket0_30 { get; set; }
    public decimal Bucket31_60 { get; set; }
    public decimal Bucket61_90 { get; set; }
    public decimal Bucket90Plus { get; set; }
    public decimal TotalUnpaid { get; set; }
}
