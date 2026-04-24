using System.Net;
using System.Net.Http.Headers;
using HitPan.Web.Models;
using Microsoft.Extensions.Logging;
using MudBlazor;

namespace HitPan.Web.Services;

public sealed class HitPanApiAuthHandler(
    HitPanProtectedLocalStorage storage,
    ISnackbar snackbar,
    ILogger<HitPanApiAuthHandler> logger) : DelegatingHandler
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

        var response = await base.SendAsync(request, cancellationToken);

        // §절대원칙 #19 — 403은 "서버 오류"가 아닌 "권한 없음"으로 사용자에게 정직하게 알린다.
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            logger.LogWarning("403 Forbidden on {Method} {Path}", request.Method, path);
            snackbar.Add("이 기능에 접근할 권한이 없습니다. 관리자에게 문의하세요.", Severity.Warning);
        }

        return response;
    }
}
