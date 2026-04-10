using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;

namespace HitPan.Web.Services;

/// <summary>
/// 브라우저 localStorage에 값을 암호화해 저장합니다.
/// Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage.ProtectedLocalStorage 는
/// 브라우저(WASM) 실행 시 <see cref="PlatformNotSupportedException"/> 을 던집니다.
/// </summary>
public sealed class HitPanProtectedLocalStorage(IJSRuntime jsRuntime)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly byte[] Key = SHA256.HashData(Encoding.UTF8.GetBytes("HitPan.Wasm.ProtectedLocalStorage.v1"));

    public async ValueTask SetAsync(string key, object value)
    {
        var json = JsonSerializer.Serialize(value, value.GetType(), JsonOptions);
        var protectedPayload = ProtectToBase64(json);
        await jsRuntime.InvokeVoidAsync("localStorage.setItem", key, protectedPayload);
    }

    public async ValueTask<HitPanStorageResult<T>> GetAsync<T>(string key)
    {
        var b64 = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", key);
        if (string.IsNullOrEmpty(b64))
        {
            return new HitPanStorageResult<T>(false, default);
        }

        try
        {
            var json = UnprotectFromBase64(b64);
            var deserialized = JsonSerializer.Deserialize<T>(json, JsonOptions);
            return new HitPanStorageResult<T>(true, deserialized);
        }
        catch
        {
            return new HitPanStorageResult<T>(false, default);
        }
    }

    public ValueTask DeleteAsync(string key) =>
        jsRuntime.InvokeVoidAsync("localStorage.removeItem", key);

    private static string ProtectToBase64(string plainText)
    {
        var plain = Encoding.UTF8.GetBytes(plainText);
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Key = Key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV();
        using var enc = aes.CreateEncryptor();
        var cipher = enc.TransformFinalBlock(plain, 0, plain.Length);
        var packed = new byte[aes.IV.Length + cipher.Length];
        Buffer.BlockCopy(aes.IV, 0, packed, 0, aes.IV.Length);
        Buffer.BlockCopy(cipher, 0, packed, aes.IV.Length, cipher.Length);
        return Convert.ToBase64String(packed);
    }

    private static string UnprotectFromBase64(string b64)
    {
        var packed = Convert.FromBase64String(b64);
        if (packed.Length <= 16)
        {
            throw new CryptographicException("Invalid payload.");
        }

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Key = Key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        var iv = packed.AsSpan(0, 16);
        var cipher = packed.AsSpan(16);
        using var dec = aes.CreateDecryptor(aes.Key, iv.ToArray());
        var plain = dec.TransformFinalBlock(cipher.ToArray(), 0, cipher.Length);
        return Encoding.UTF8.GetString(plain);
    }
}

public readonly struct HitPanStorageResult<T>(bool success, T? value)
{
    public bool Success => success;
    public T? Value => value;
}
