using System.Net.Http.Headers;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

public sealed class HitPanApiAuthHandler(HitPanProtectedLocalStorage storage) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        var skipBearer = path.Contains("/api/auth/login", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/api/auth/refresh", StringComparison.OrdinalIgnoreCase);

        if (!skipBearer)
        {
            var token = await storage.GetAsync<string>(AuthStorageKeys.AccessToken);
            if (token.Success && !string.IsNullOrEmpty(token.Value))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
