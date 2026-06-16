using System.Security.Cryptography;
using System.Text;
using HitPan.Infrastructure.Configuration;

namespace HitPan.Infrastructure.Security;

public interface IEncryptionService
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
    byte[] EncryptBytes(byte[] plainBytes);
    byte[] DecryptBytes(byte[] cipherBytes);
}

public sealed class EncryptionService : IEncryptionService
{
    private const int AesKeySizeInBytes = 32; // AES-256
    private const int IvSizeInBytes = 16; // AES block size
    private readonly byte[] _key;

    public EncryptionService()
    {
        // 봉합 2026-06-17 1.2.12
        var rawKey = TenantConfigReader.GetRequired("ERP_ENCRYPTION_KEY");
        _key = ParseKey(rawKey);
    }

    public string Encrypt(string plainText)
    {
        if (plainText is null)
        {
            throw new ArgumentNullException(nameof(plainText));
        }

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.KeySize = 256;
        aes.Key = _key;
        aes.GenerateIV(); // Random IV for every encryption call

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        using var encryptor = aes.CreateEncryptor();
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var result = new byte[IvSizeInBytes + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, IvSizeInBytes);
        Buffer.BlockCopy(cipherBytes, 0, result, IvSizeInBytes, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        if (cipherText is null)
        {
            throw new ArgumentNullException(nameof(cipherText));
        }

        var data = Convert.FromBase64String(cipherText);
        if (data.Length <= IvSizeInBytes)
        {
            throw new CryptographicException("Cipher text payload is invalid.");
        }

        var iv = new byte[IvSizeInBytes];
        var cipherBytes = new byte[data.Length - IvSizeInBytes];
        Buffer.BlockCopy(data, 0, iv, 0, IvSizeInBytes);
        Buffer.BlockCopy(data, IvSizeInBytes, cipherBytes, 0, cipherBytes.Length);

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.KeySize = 256;
        aes.Key = _key;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    public byte[] EncryptBytes(byte[] plainBytes)
    {
        if (plainBytes is null) throw new ArgumentNullException(nameof(plainBytes));

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.KeySize = 256;
        aes.Key = _key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var cipher = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var result = new byte[IvSizeInBytes + cipher.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, IvSizeInBytes);
        Buffer.BlockCopy(cipher, 0, result, IvSizeInBytes, cipher.Length);
        return result;
    }

    public byte[] DecryptBytes(byte[] cipherBytes)
    {
        if (cipherBytes is null) throw new ArgumentNullException(nameof(cipherBytes));
        if (cipherBytes.Length <= IvSizeInBytes)
            throw new CryptographicException("Cipher payload invalid.");

        var iv = new byte[IvSizeInBytes];
        var ct = new byte[cipherBytes.Length - IvSizeInBytes];
        Buffer.BlockCopy(cipherBytes, 0, iv, 0, IvSizeInBytes);
        Buffer.BlockCopy(cipherBytes, IvSizeInBytes, ct, 0, ct.Length);

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.KeySize = 256;
        aes.Key = _key;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(ct, 0, ct.Length);
    }

    private static byte[] ParseKey(string rawKey)
    {
        // Prefer Base64-encoded 32-byte key. Fallback to raw UTF8 32-byte key.
        try
        {
            var decoded = Convert.FromBase64String(rawKey);
            if (decoded.Length == AesKeySizeInBytes)
            {
                return decoded;
            }
        }
        catch (FormatException)
        {
            // Fall back to UTF8 path below.
        }

        var utf8Key = Encoding.UTF8.GetBytes(rawKey);
        if (utf8Key.Length == AesKeySizeInBytes)
        {
            return utf8Key;
        }

        throw new InvalidOperationException("ERP_ENCRYPTION_KEY must be a Base64 or UTF8 value with exactly 32 bytes.");
    }
}
