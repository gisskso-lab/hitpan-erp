using Microsoft.Extensions.Logging;

namespace HitPan.Web.Services;

/// <summary>
/// ?뵶 20260930?? 媛덈옒 F ???뚯턀??踰꾩쟾 ?뺤씤/?낅뜲?댄듃???섎룞 ?낅뜲?댄듃) ?붾㈃??遺瑜대뒗 API.
/// </summary>
/// <remarks>
/// <para>
/// ?ъ옣???⑹뼱(9/30): <b>?먮룞</b> = 濡쒓렇???앹뾽 쨌 <b>?섎룞</b> = 硫붾돱濡?吏곸젒. ???대씪?댁뼵?몃뒗 ?섎룞 履쎌씠??
/// ?먮룞 寃쎈줈(<c>update-consent</c> 쨌 <c>update-consent-local</c>)??<b>遺瑜댁? ?딅뒗??/b>(G-43 쨌 G-L1).
/// </para>
/// <para>
/// 臾??)? ?쒕쾭媛 留됰뒗????<c>TenantAdminOnly</c> + <c>[MainPcOnly]</c>(?ㅺ퀎 짠13-4). 二쇱냼쨌?꾨뱶???좎젙 ??媛덈옒 A쨌U 怨꾩빟 ?議??꾩슂.
/// </para>
/// </remarks>
public sealed class ManualUpdateClient(HttpClient http, ILogger<ManualUpdateClient> logger)
{
    /// <summary>吏湲???쨌 諛쏆쓣 ???덈뒗 理쒖떊 ??쨌 ?쒖옉 媛???щ? 쨌 ???섎㈃ ?ъ쑀 肄붾뱶 쨌 吏?쒕쾲 ?섎룞 ?낅뜲?댄듃 寃곌낵.</summary>
    public Task<LocalSwapCallResult<ManualUpdateStatus>> CheckAsync(CancellationToken ct = default) =>
        LocalSwapCall.GetAsync<ManualUpdateStatus>(http, logger, LocalSwapUiText.ApiUpdateStatus, ct);

    /// <summary>[?? ?????쒕쾭媛 ?쒕챸쨌?댁떆쨌諛깆뾽??嫄곗튇 ?ㅼ뿉留?援먯껜 ?묒뾽??嫄대떎(?ㅺ퀎 짠13-2).</summary>
    /// <remarks>?뵶 [3-V] ?곷컻 04 諛섏쁺 ??踰꾩쟾쨌寃쎈줈瑜?蹂몃Ц???ｌ? ?딅뒗??諛쏆쓣 ?먯? ?쒕쾭媛 ?쇰뱶?먯꽌 怨꾩궛 쨌 蹂몃Ц = 鍮?媛앹껜).</remarks>
    public Task<LocalSwapCallResult<LocalSwapStartResult>> StartAsync(CancellationToken ct = default) =>
        LocalSwapCall.PostAsync<LocalSwapStartResult>(
            http, logger, LocalSwapUiText.ApiUpdateStart, LocalSwapUiText.EmptyBody, ct);
}

/// <summary>?섎룞 ?낅뜲?댄듃 ?곹깭(?좎젙 DTO ??怨꾩빟 ?議??꾩슂).</summary>
public sealed class ManualUpdateStatus
{
    public string? CurrentVersion { get; set; }
    public string? LatestVersion { get; set; }
    public bool UpdateAvailable { get; set; }
    public bool CanStart { get; set; }
    public string? Reason { get; set; }

    /// <summary>吏?쒕쾲 ?섎룞 ?낅뜲?댄듃媛 ?ㅽ뙣???먮옒 ?먯쑝濡??뚮젮 ?먯뿀?쇰㈃ true(?ㅺ퀎 짠13-8 ?뚯뾽?곗씠???ㅽ뙣??臾멸뎄).</summary>
    public bool LastFailedRestored { get; set; }
    public string? LastFromVersion { get; set; }
}
