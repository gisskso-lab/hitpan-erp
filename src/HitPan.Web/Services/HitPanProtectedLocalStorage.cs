using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;

namespace HitPan.Web.Services;

/// <summary>
/// Blazor WASM 브라우저에서는 대칭키(AES 등) API가 지원되지 않을 수 있어,
/// JSON 직렬화 후 Base64 인코딩만 적용하고 <see cref="IJSRuntime"/>으로
/// <c>window.hitpanStorage</c>에 위임합니다. (래퍼: <c>hitpanStorage_set</c> 등)
/// </summary>
public sealed class HitPanProtectedLocalStorage(IJSRuntime jsRuntime)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async ValueTask SetAsync(string key, object value)
    {
        var json = JsonSerializer.Serialize(value, value.GetType(), JsonOptions);
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        await jsRuntime.InvokeVoidAsync("hitpanStorage_set", key, b64);
    }

    public async ValueTask<HitPanStorageResult<T>> GetAsync<T>(string key)
    {
        var b64 = await jsRuntime.InvokeAsync<string?>("hitpanStorage_get", key);
        if (string.IsNullOrEmpty(b64))
        {
            return new HitPanStorageResult<T>(false, default);
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            var deserialized = JsonSerializer.Deserialize<T>(json, JsonOptions);
            return new HitPanStorageResult<T>(true, deserialized);
        }
        catch (Exception)
        {
            return new HitPanStorageResult<T>(false, default);
        }
    }

    public ValueTask DeleteAsync(string key) =>
        jsRuntime.InvokeVoidAsync("hitpanStorage_remove", key);

    /// <summary>
    /// 🔴 20260928작2 개정3 절S(설계 §14-4 · R-2) — <b>저장소의 refresh 가 보낸 값과 같을 때만</b> 로그인 칸을 지운다.
    /// </summary>
    /// <remarks>
    /// 비교와 지움을 <b>한 번의 JS 호출</b> 안에서 한다(<c>hitpanStorage_removeAuthIfRefreshIs</c>) —
    /// 「읽고 → C# 에서 비교 → 지움」이면 그 사이에 다른 탭이 새 토큰을 쓸 수 있다.
    /// 비교값은 <see cref="SetAsync"/> 와 <b>같은 인코딩</b>(JSON → Base64)으로 만든다(저장된 글자 그대로 비교).
    /// ⚠️ 탭 사이 저장소 원자성은 웹 규격에 없다 — 창을 ms 로 좁힐 뿐 0 은 아니다(설계 §14-4 기록).
    /// </remarks>
    /// <param name="sentRefresh">이번에 서버로 보낸 refresh. <c>null</c> = 보낸 것이 없다(저장소가 비었을 때만 지운다).</param>
    public async ValueTask<AuthRemoveResult> DeleteAuthIfRefreshIsAsync(string? sentRefresh)
    {
        string? expected = null;
        if (!string.IsNullOrEmpty(sentRefresh))
        {
            var json = JsonSerializer.Serialize(sentRefresh, sentRefresh.GetType(), JsonOptions);
            expected = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        }

        var answer = await jsRuntime.InvokeAsync<string?>("hitpanStorage_removeAuthIfRefreshIs", expected);
        return answer switch
        {
            "removed" => AuthRemoveResult.Removed,
            "rotated" => AuthRemoveResult.Rotated,
            _ => AuthRemoveResult.Empty
        };
    }
}

/// <summary><see cref="HitPanProtectedLocalStorage.DeleteAuthIfRefreshIsAsync"/> 의 답 (개정3 절S).</summary>
public enum AuthRemoveResult
{
    /// <summary>같았다 — 지웠다.</summary>
    Removed,

    /// <summary>달랐다 — 그새 다른 탭이 새 토큰을 썼다(지우지 않았다 · 성공으로 본다).</summary>
    Rotated,

    /// <summary>저장소에 refresh 가 없었다 — 남은 칸을 치웠다.</summary>
    Empty
}

public readonly struct HitPanStorageResult<T>(bool success, T? value)
{
    public bool Success => success;
    public T? Value => value;
}
