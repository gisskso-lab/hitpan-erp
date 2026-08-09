using System.Net.Http.Json;
using HitPan.Web.Models;
using Microsoft.Extensions.Logging;

namespace HitPan.Web.Services;

public sealed class UserService(HttpClient http, ILogger<UserService> logger)
{
    /// <summary>
    /// 사용자 목록을 조회한다.
    /// </summary>
    /// <remarks>
    /// 🔴 실패 시 <c>null</c> 을 돌려준다. 빈 리스트가 아니다 — 호출부는 반드시 둘을 구분해야 한다
    /// (20260809작4 ②번 봉합). 종전에는 실패를 빈 리스트처럼 다뤄
    /// 직원 12명이 있는 회사에서 "아직 등록된 사용자가 없습니다" 가 떴다.
    /// </remarks>
    public async Task<List<UserListModel>?> GetAllAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<UserListModel>>("api/users", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 헌법 #15 — 빈 catch 금지. 진단 근거를 남긴다.
            logger.LogWarning(ex, "사용자 목록 조회 실패");
            return null;
        }
    }

    public async Task<UserListModel?> GetAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<UserListModel>($"api/users/{userId}", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "사용자 조회 실패 (userId={UserId})", userId);
            return null;
        }
    }

    public async Task<(bool ok, string? error)> CreateAsync(CreateUserModel model, CancellationToken ct = default)
    {
        try
        {
            var res = await http.PostAsJsonAsync("api/users", model, ct).ConfigureAwait(false);
            if (res.IsSuccessStatusCode) return (true, null);
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return (false, body);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<bool> UpdateAsync(string userId, UpdateUserModel model, CancellationToken ct = default)
    {
        try
        {
            var res = await http.PutAsJsonAsync($"api/users/{userId}", model, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "사용자 수정 실패 (userId={UserId})", userId);
            return false;
        }
    }

    public async Task<bool> DeactivateAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var res = await http.DeleteAsync($"api/users/{userId}", ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "사용자 비활성화 실패 (userId={UserId})", userId);
            return false;
        }
    }

    // 헌법 #35 (사장님 결재 2026-06-04) — 부모계정 삭제 차단 메시지 받기
    public async Task<(bool ok, string? error)> DeactivateWithMessageAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var res = await http.DeleteAsync($"api/users/{userId}", ct).ConfigureAwait(false);
            if (res.IsSuccessStatusCode) return (true, null);
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return (false, body);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<ResetPasswordResponse?> ResetPasswordAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var res = await http.PostAsync($"api/users/{userId}/reset-password", null, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;
            return await res.Content.ReadFromJsonAsync<ResetPasswordResponse>(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "비밀번호 초기화 실패 (userId={UserId})", userId);
            return null;
        }
    }

    public async Task<byte[]?> GetBulkTemplateAsync(CancellationToken ct = default)
    {
        try
        {
            var res = await http.GetAsync("api/users/bulk/template", ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;
            return await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "일괄등록 양식 내려받기 실패");
            return null;
        }
    }

    public async Task<BulkCreateResult?> BulkUploadAsync(Stream stream, string fileName, CancellationToken ct = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            using var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            content.Add(fileContent, "file", fileName);

            var res = await http.PostAsync("api/users/bulk", content, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return new BulkCreateResult { FailedCount = -1, Errors = new() { new() { Row = 0, Reason = err } } };
            }
            return await res.Content.ReadFromJsonAsync<BulkCreateResult>(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new BulkCreateResult { FailedCount = -1, Errors = new() { new() { Row = 0, Reason = ex.Message } } };
        }
    }
}
