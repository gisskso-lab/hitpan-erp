using System.Net.Http.Json;
using HitPan.Web.Models;
using Microsoft.Extensions.Logging;

namespace HitPan.Web.Services;

public sealed class PermissionService(HttpClient http, ILogger<PermissionService> logger)
{
    /// <summary>
    /// 직원별 권한 목록을 조회한다.
    /// </summary>
    /// <remarks>
    /// 🔴 실패 시 <c>null</c> 을 돌려준다. 빈 리스트가 아니다 — 호출부는 반드시 둘을 구분해야 한다.
    /// 종전에는 화면이 <c>?? new()</c> 로 뭉개서, 못 불러온 것을 "직원 0명" 처럼 보여줬다.
    /// </remarks>
    public async Task<List<UserPermissionModel>?> GetAllAsync(CancellationToken ct = default)
    {
        try
        {
            return await http
                .GetFromJsonAsync<List<UserPermissionModel>>("api/permissions", ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 헌법 #15 — 빈 catch 금지. 진단 근거를 남긴다.
            logger.LogWarning(ex, "권한 목록 조회 실패");
            return null;
        }
    }

    public async Task<UserPermissionModel?> GetAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            return await http
                .GetFromJsonAsync<UserPermissionModel>($"api/permissions/{Uri.EscapeDataString(userId)}", ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "권한 조회 실패 (userId={UserId})", userId);
            return null;
        }
    }

    public async Task<bool> SaveAsync(SavePermissionsModel model, CancellationToken ct = default)
    {
        try
        {
            using var res = await http
                .PostAsJsonAsync("api/permissions", model, ct)
                .ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "권한 저장 실패 (userId={UserId})", model.UserId);
            return false;
        }
    }
}
