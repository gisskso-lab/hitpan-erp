using System.Net.Http.Json;
using System.Text.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

/// <summary>
/// 단가 참고값 4종을 읽어 온다 — 명세서 화면 말풍선용 (20260820작4 · 설계2 C안).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>사장님 설계 (2026-08-20)</b>: <i>"단가는 모든 워크플로우 명세서 작성시
/// (발주,판매,반품,견적,수주,판매) <b>직접 작성이 가능하되</b>, 마우스 커서 갖다대면,
/// 업체특별단가·최종단가·표준단가·혹은 상품특별단가를 고객이 볼 수 있도록"</i>
/// </para>
///
/// <para>
/// 🔴 <b>한 줄 캐시를 둔다.</b> 그리드에서 <b>줄마다 커서를 올릴 때마다</b> 부르는 자리라
/// 같은 (업체·상품)을 다시 묻지 않는다. 캐시가 없으면 마우스를 몇 번만 움직여도 요청이 쌓인다.
/// ⚠️ 캐시 열쇠에 <c>isPurchase</c> 를 <b>반드시 넣는다</b> — 안 넣으면 매입 화면에서 본 값이
/// 판매 화면에 그대로 나온다(<b>판 값과 산 값은 다른 금액이다</b>).
/// </para>
///
/// <para>
/// ⚠️ <b>업체가 안 정해졌으면 부르지 않는다.</b> 명세서는 업체를 고르기 전에도 줄을 만들 수 있고,
/// 그때 부르면 서버가 <c>null</c> 을 주거나 헛일이 된다.
/// </para>
/// </remarks>
public sealed class PriceHintService(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>(업체·상품·매입여부) → 참고값. 화면을 벗어나면 같이 사라지는 짧은 캐시다.</summary>
    private readonly Dictionary<string, PriceHint?> _cache = new();

    /// <summary>
    /// 참고값을 읽는다. 업체나 상품이 비어 있으면 <c>null</c>.
    /// </summary>
    /// <param name="isPurchase">
    /// 🔴 발주·매입·반품이면 <c>true</c>(산 값), 견적·수주·판매면 <c>false</c>(판 값).
    /// <b>최종단가의 출처가 갈린다.</b>
    /// </param>
    public async Task<PriceHint?> GetAsync(
        string? partnerId, string? itemId, bool isPurchase, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(partnerId) || string.IsNullOrWhiteSpace(itemId))
        {
            return null;
        }

        var key = $"{partnerId}|{itemId}|{isPurchase}";
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        try
        {
            var path = $"api/partners/{Uri.EscapeDataString(partnerId)}"
                + $"/price-hint/{Uri.EscapeDataString(itemId)}?purchase={(isPurchase ? "true" : "false")}";
            var hint = await http.GetFromJsonAsync<PriceHint>(path, JsonOptions, ct);
            _cache[key] = hint;
            return hint;
        }
        catch (Exception ex)
        {
            // 헌법 #15 — 빈 catch 금지. 참고값을 못 읽었다고 명세서 작성이 막히면 안 된다.
            //   말풍선이 안 뜰 뿐이고, 사람은 여전히 단가를 직접 칠 수 있다.
            Console.WriteLine($"[PriceHintService.GetAsync] Error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 업체가 바뀌면 캐시를 버린다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>안 버리면 앞 업체의 특별단가가 새 업체 줄에 그대로 뜬다.</b>
    /// 명세서 화면은 업체를 도중에 바꿀 수 있는 자리다(<c>OnPartnerNameChangedAsync</c>).
    /// </remarks>
    public void Clear() => _cache.Clear();
}
