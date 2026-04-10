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
        catch
        {
            return new HitPanStorageResult<T>(false, default);
        }
    }

    public ValueTask DeleteAsync(string key) =>
        jsRuntime.InvokeVoidAsync("hitpanStorage_remove", key);
}

public readonly struct HitPanStorageResult<T>(bool success, T? value)
{
    public bool Success => success;
    public T? Value => value;
}
