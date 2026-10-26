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
        var result = await SaveCompanyDetailedAsync(model, ct).ConfigureAwait(false);
        return result.Saved;
    }

    /// <summary>
    /// 사업장 기본정보를 저장하고, 잠겨 있어 반영되지 않은 항목까지 함께 돌려준다.
    /// 잠금 위반을 조용히 넘기면 고객은 "고쳤는데 안 바뀐다" 는 상태에 빠진다(2026-08-10).
    /// </summary>
    public async Task<SaveCompanyResult> SaveCompanyDetailedAsync(TenantCompanyModel model, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PutAsJsonAsync("api/settings/company", model, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                return new SaveCompanyResult(false, Array.Empty<string>(), null);
            }

            var body = await res.Content.ReadFromJsonAsync<SaveCompanyResponse>(cancellationToken: ct)
                .ConfigureAwait(false);

            return new SaveCompanyResult(
                true,
                (IReadOnlyList<string>?)body?.RejectedFields ?? Array.Empty<string>(),
                body?.Message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "사업장 기본정보 저장 실패");
            return new SaveCompanyResult(false, Array.Empty<string>(), null);
        }
    }

    /// <summary>저장 결과. RejectedFields 가 비어 있지 않으면 그 항목은 반영되지 않았다.</summary>
    public sealed record SaveCompanyResult(bool Saved, IReadOnlyList<string> RejectedFields, string? Message);

    private sealed class SaveCompanyResponse
    {
        public bool Saved { get; set; }

        public List<string>? RejectedFields { get; set; }

        public string? Message { get; set; }
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

    /// <summary>
    /// 🔴 회사 그림(로고·인장·머리)을 보관소로 올린다 — 20261026작1 §B-4 (결-H).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>왜 이 메서드가 생겼나</b> — 10/25 실측: 화면은 「등록되었습니다」라 말하고
    ///   서버에는 아무것도 가지 않았다. 올리는 통로가 <b>0건</b>이었다.
    /// 🔴 서버가 올린 뒤 <b>회사 정보 칸까지 적는다</b> — 이 화면에는 그림만 위한
    ///   저장 단추가 없으니 올리는 걸음에 끝나야 「등록되었습니다」가 참이 된다.
    /// </remarks>
    /// <returns>성공하면 <c>(경로, null)</c>, 실패하면 <c>(null, 고객에게 보일 까닭)</c>.</returns>
    public async Task<(string? StoredPath, string? Rejection)> UploadCompanyImageAsync(
        string kind, Stream content, string fileName, CancellationToken ct = default)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StreamContent(content), "file", fileName);
            form.Add(new StringContent(kind), "kind");

            using var res = await http.PostAsync("api/settings/company-image", form, ct)
                .ConfigureAwait(false);

            if (res.IsSuccessStatusCode)
            {
                var ok = await res.Content
                    .ReadFromJsonAsync<UploadedCompanyImage>(JsonOptions, ct).ConfigureAwait(false);
                return (ok?.StoredPath, null);
            }

            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return (null, ExtractMessage(body));
        }
        catch (Exception ex)
        {
            // 🔴 빈 catch 금지(#15). 고객에게는 사람 말로 돌려준다.
            logger.LogWarning(ex, "회사 그림 올리기 실패 — 종류 {Kind}", kind);
            return (null, "그림을 올리지 못했습니다. 잠시 뒤 다시 시도해 주십시오.");
        }
    }

    /// <summary>보관소가 돌려준 경로.</summary>
    private sealed class UploadedCompanyImage
    {
        public string? StoredPath { get; set; }
    }

    /// <summary>서버가 돌려준 까닭을 사람 말로 꺼낸다.</summary>
    private static string ExtractMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "그림을 올리지 못했습니다.";
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var m))
            {
                var text = m.GetString();
                if (!string.IsNullOrWhiteSpace(text)) return text!;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // 🔴 JSON 이 아니면 일반 안내로 떨어진다 — 고객 화면에 날 문자열을 쏟지 않는다.
        }
        return "그림을 올리지 못했습니다.";
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
