using System.Net.Http.Json;
using HitPan.Web.Models;
using Microsoft.Extensions.Logging;

namespace HitPan.Web.Services;

/// <summary>
/// 전자 퇴직서(사직서) API 클라이언트 — 작20260824작2 [4].
/// </summary>
/// <remarks>
/// 🔴 빈 catch 금지(헌법 #15). 퇴사는 되돌리기 어려운 일이라 실패를 삼키면 안 된다.
/// </remarks>
public sealed class ResignationService(HttpClient http, ILogger<ResignationService> logger)
{
    /// <summary>
    /// 목록. 실패는 <c>null</c> 로 알린다.
    /// </summary>
    /// <remarks>
    /// 🔴 빈 목록("없다")과 실패("못 불러왔다")를 가른다 — 둘 다 빈 표로 보이면
    /// 낸 사직서가 사라진 줄 안다.
    /// </remarks>
    public async Task<List<ResignationLetterModel>?> GetListAsync(CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<List<ResignationLetterModel>>("api/resignations", ct) ?? new(); }
        catch (Exception ex) { logger.LogWarning(ex, "사직서 목록 조회 실패"); return null; }
    }

    public async Task<ResignationLetterModel?> GetAsync(string id, CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<ResignationLetterModel>($"api/resignations/{Uri.EscapeDataString(id)}", ct); }
        catch (Exception ex) { logger.LogWarning(ex, "사직서 조회 실패 id={Id}", id); return null; }
    }

    /// <summary>작성·수정. 실패 사유를 그대로 돌려준다.</summary>
    public async Task<(bool Ok, string? Message)> SaveAsync(
        SaveResignationModel model, CancellationToken ct = default)
    {
        try
        {
            using var r = await http.PostAsJsonAsync("api/resignations", model, ct);
            if (r.IsSuccessStatusCode) return (true, null);
            return (false, await ReadMessageAsync(r, ct));
        }
        catch (Exception ex) { logger.LogWarning(ex, "사직서 저장 실패"); return (false, "저장에 실패했습니다."); }
    }

    /// <summary>
    /// 제출 — 결재를 올린다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>실패 사유를 반드시 화면에 띄운다.</b> "결재 설정이 꺼져 있다" 같은 이유를
    /// 삼키면 직원은 냈다고 여기는데 결재함엔 안 뜬다(8/21 휴직 P0 와 같은 자리).
    /// </remarks>
    public async Task<(bool Ok, string? Message)> SubmitAsync(string id, CancellationToken ct = default)
        => await PostAsync($"api/resignations/{Uri.EscapeDataString(id)}/submit", ct);

    public async Task<(bool Ok, string? Message)> WithdrawAsync(string id, CancellationToken ct = default)
        => await PostAsync($"api/resignations/{Uri.EscapeDataString(id)}/withdraw", ct);

    /// <summary>수리 — 회사가 실제 퇴사일을 정해 확정한다.</summary>
    public async Task<(bool Ok, string? Message)> AcceptAsync(
        string id, DateTime actualDate, string? comment, CancellationToken ct = default)
    {
        try
        {
            using var r = await http.PostAsJsonAsync(
                $"api/resignations/{Uri.EscapeDataString(id)}/accept",
                new { actualDate, comment }, ct);
            if (r.IsSuccessStatusCode) return (true, null);
            return (false, await ReadMessageAsync(r, ct));
        }
        catch (Exception ex) { logger.LogWarning(ex, "사직서 수리 실패 id={Id}", id); return (false, "수리에 실패했습니다."); }
    }

    private async Task<(bool Ok, string? Message)> PostAsync(string url, CancellationToken ct)
    {
        try
        {
            using var r = await http.PostAsync(url, null, ct);
            if (r.IsSuccessStatusCode) return (true, null);
            return (false, await ReadMessageAsync(r, ct));
        }
        catch (Exception ex) { logger.LogWarning(ex, "요청 실패 url={Url}", url); return (false, "처리에 실패했습니다."); }
    }

    /// <summary>서버가 준 사유를 꺼낸다. 못 꺼내면 일반 문구.</summary>
    private static async Task<string> ReadMessageAsync(HttpResponseMessage r, CancellationToken ct)
    {
        try
        {
            var body = await r.Content.ReadFromJsonAsync<Dictionary<string, string>>(ct);
            if (body is not null && body.TryGetValue("message", out var m) && !string.IsNullOrWhiteSpace(m))
            {
                return m;
            }
        }
        catch
        {
            // 사유를 못 읽은 것은 사고가 아니다 — 아래 일반 문구로 알린다.
        }

        return "처리에 실패했습니다.";
    }
}
