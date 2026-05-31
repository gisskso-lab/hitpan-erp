using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

public class SpecialPriceService
{
    private readonly HttpClient _http;

    public SpecialPriceService(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<SpecialPriceItem>> GetAsync(string partnerId)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<SpecialPriceItem>>(
                       "api/partners/" + partnerId + "/special-prices")
                   ?? new List<SpecialPriceItem>();
        }
        catch (Exception)
        {
            return new List<SpecialPriceItem>();
        }
    }

    public async Task<bool> SaveAsync(string partnerId, SpecialPriceUpsertDto dto)
    {
        try
        {
            var res = await _http.PostAsJsonAsync(
                "api/partners/" + partnerId + "/special-prices", dto);
            return res.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> DeleteAsync(string partnerId, string itemId)
    {
        try
        {
            var res = await _http.DeleteAsync(
                "api/partners/" + partnerId + "/special-prices/" + itemId);
            return res.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
