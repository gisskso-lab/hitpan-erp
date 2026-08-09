using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HitPan.Web.Models;
using Microsoft.Extensions.Logging;

namespace HitPan.Web.Services;

public sealed class SettingsService(HttpClient http, ILogger<SettingsService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 사용환경 설정을 조회한다.
    /// </summary>
    /// <remarks>
    /// 🔴 실패 시 <c>null</c> 을 돌려준다. 호출부는 이를 <b>기본값으로 대체하면 안 된다</b>
    /// (병렬이슈 08 · 작4 ③번). 기본값으로 화면을 그리면 그 값이 저장되어
    /// 서버의 실제 설정 27개 컬럼을 통째로 덮어쓴다.
    /// </remarks>
    public async Task<TenantSettingsModel?> GetAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<TenantSettingsModel>("api/settings", JsonOptions, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 헌법 #15 — 빈 catch 금지. 진단 근거를 남긴다.
            logger.LogWarning(ex, "사용환경 설정 조회 실패");
            return null;
        }
    }

    public async Task<bool> SaveAsync(TenantSettingsModel model, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PutAsJsonAsync("api/settings", model, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "사용환경 설정 저장 실패");
            return false;
        }
    }

    /// <summary>
    /// 사업장 기본정보를 tenants 테이블에 반영한다.
    /// </summary>
    public async Task<bool> SaveCompanyAsync(TenantCompanyModel model, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PutAsJsonAsync("api/settings/company", model, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "사업장 기본정보 저장 실패");
            return false;
        }
    }

    /// <summary>
    /// 사업장 기본정보를 조회한다.
    /// </summary>
    public async Task<TenantCompanyModel?> GetCompanyAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<TenantCompanyModel>("api/settings/company", JsonOptions, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "사업장 기본정보 조회 실패");
            return null;
        }
    }

    public async Task<bool> VerifyForcePasswordAsync(string password, CancellationToken ct = default)
    {
        try
        {
            using var res = await http
                .PostAsJsonAsync("api/settings/verify-force-edit-password", new { password }, ct)
                .ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                return false;
            }

            var body = await res.Content.ReadFromJsonAsync<VerifyForceEditPasswordResponse>(JsonOptions, ct)
                .ConfigureAwait(false);
            return body?.Valid == true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "강제수정 암호 확인 실패");
            return false;
        }
    }

    private sealed class VerifyForceEditPasswordResponse
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }
    }
}
