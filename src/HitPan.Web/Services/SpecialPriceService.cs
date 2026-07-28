using System.Net.Http.Json;
using HitPan.Web.Models;
using Microsoft.Extensions.Logging;

namespace HitPan.Web.Services;

public class SpecialPriceService
{
    private readonly HttpClient _http;
    private readonly ILogger<SpecialPriceService> _logger;

    public SpecialPriceService(HttpClient http, ILogger<SpecialPriceService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<List<SpecialPriceItem>> GetAsync(string partnerId)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<SpecialPriceItem>>(
                       "api/partners/" + partnerId + "/special-prices")
                   ?? new List<SpecialPriceItem>();
        }
        catch (Exception ex)
        {
            // 봉합 (2026-06-23, 17차 #15): silent swallow 금지 — 통신 실패 추적용 로그.
            _logger.LogWarning(ex, "특별단가 조회 실패 (partnerId={PartnerId})", partnerId);
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "특별단가 저장 실패 (partnerId={PartnerId})", partnerId);
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "특별단가 삭제 실패 (partnerId={PartnerId}, itemId={ItemId})", partnerId, itemId);
            return false;
        }
    }
}
