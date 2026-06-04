using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;

namespace HitPan.Backoffice.Services;

// 쿠키 클레임의 access_token을 HttpClient 요청에 자동 첨부 (사장님 결재 2026-06-04)
//
// 흐름:
//   1) 사용자가 백오피스 로그인 → BackofficeAuthController.SignIn
//      → 쿠키 클레임에 access_token 박제
//   2) Blazor 페이지가 HttpClient로 백오피스 API 호출
//      → 본 핸들러가 HttpContext에서 access_token 추출
//      → Authorization: Bearer {token} 헤더 첨부
//   3) 백오피스 API의 [Authorize] / [BoPermission] 통과
//
// 헌법 정합:
//   #15 — 빈 catch 금지 (없음 — 토큰 미박제 시 첨부만 안 함)
//   #29 — JWT는 쿠키에서만, 코드 박제 0
//   #35 — 쿠키 인증(백오피스) ↔ JWT 인증(API) 다리
public class JwtFromCookieHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpCtx;

    public JwtFromCookieHandler(IHttpContextAccessor httpCtx)
    {
        _httpCtx = httpCtx;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = _httpCtx.HttpContext?.User?.FindFirst("access_token")?.Value;
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return base.SendAsync(request, ct);
    }
}
