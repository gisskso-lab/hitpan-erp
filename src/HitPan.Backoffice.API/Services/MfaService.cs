using System.Security.Cryptography;
using System.Text;

namespace HitPan.Backoffice.API.Services;

// MFA TOTP (RFC 6238) — Google Authenticator 호환 (사장님 결재 2026-06-04, W11)
//
// 헌법 정합:
//   #18·#22 — 시크릿은 AES-256 박제, 평문 0건
//   #29 — HITPAN_BO_MFA_KEY 환경변수 (별도 키)
//   #25 — Owner 강제, 일반 admin 선택
public interface IMfaService
{
    string GenerateSecret();
    string BuildOtpAuthUri(string secret, string email, string issuer = "HitPan Backoffice");
    bool Verify(string secret, string code, int windowSteps = 1);
    byte[] Encrypt(string plaintext);
    string Decrypt(byte[] ciphertext);
    string[] GenerateBackupCodes(int count = 10);
}

public class MfaService : IMfaService
{
    private readonly IConfiguration _config;

    public MfaService(IConfiguration config)
    {
        _config = config;
    }

    public string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);
        return Base32Encode(bytes);
    }

    public string BuildOtpAuthUri(string secret, string email, string issuer = "HitPan Backoffice")
    {
        var label = Uri.EscapeDataString($"{issuer}:{email}");
        var iss = Uri.EscapeDataString(issuer);
        return $"otpauth://totp/{label}?secret={secret}&issuer={iss}&algorithm=SHA1&digits=6&period=30";
    }

    public bool Verify(string secret, string code, int windowSteps = 1)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code)) return false;
        if (!int.TryParse(code, out _)) return false;
        var key = Base32Decode(secret);
        var currentStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30L;
        for (var i = -windowSteps; i <= windowSteps; i++)
        {
            var expected = ComputeTotp(key, currentStep + i);
            if (CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(code)))
                return true;
        }
        return false;
    }

    public byte[] Encrypt(string plaintext)
    {
        var key = GetAesKey();
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        using var enc = aes.CreateEncryptor();
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = enc.TransformFinalBlock(plain, 0, plain.Length);
        var result = new byte[aes.IV.Length + cipher.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipher, 0, result, aes.IV.Length, cipher.Length);
        return result;
    }

    public string Decrypt(byte[] ciphertext)
    {
        var key = GetAesKey();
        using var aes = Aes.Create();
        aes.Key = key;
        var iv = new byte[16];
        Buffer.BlockCopy(ciphertext, 0, iv, 0, 16);
        aes.IV = iv;
        using var dec = aes.CreateDecryptor();
        var plain = dec.TransformFinalBlock(ciphertext, 16, ciphertext.Length - 16);
        return Encoding.UTF8.GetString(plain);
    }

    public string[] GenerateBackupCodes(int count = 10)
    {
        var codes = new string[count];
        for (var i = 0; i < count; i++)
        {
            var b = RandomNumberGenerator.GetBytes(5);
            codes[i] = $"{BitConverter.ToString(b).Replace("-", "").ToLowerInvariant()}";
        }
        return codes;
    }

    private byte[] GetAesKey()
    {
        var raw = Environment.GetEnvironmentVariable("HITPAN_BO_MFA_KEY")
                 ?? _config["Bo:MfaKey"]
                 ?? "DEV-bo-mfa-key-change-in-production-32+chars-aaaa";
        using var sha = SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
    }

    private static string ComputeTotp(byte[] key, long step)
    {
        var stepBytes = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian) Array.Reverse(stepBytes);
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(stepBytes);
        var offset = hash[^1] & 0x0F;
        var binCode = ((hash[offset] & 0x7F) << 24)
                    | ((hash[offset + 1] & 0xFF) << 16)
                    | ((hash[offset + 2] & 0xFF) << 8)
                    | (hash[offset + 3] & 0xFF);
        var otp = binCode % 1_000_000;
        return otp.ToString("D6");
    }

    private static readonly char[] Base32Alphabet =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".ToCharArray();

    private static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                var idx = (buffer >> (bits - 5)) & 0x1F;
                sb.Append(Base32Alphabet[idx]);
                bits -= 5;
            }
        }
        if (bits > 0)
        {
            var idx = (buffer << (5 - bits)) & 0x1F;
            sb.Append(Base32Alphabet[idx]);
        }
        return sb.ToString();
    }

    private static byte[] Base32Decode(string input)
    {
        input = input.TrimEnd('=').ToUpperInvariant();
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in input)
        {
            var v = Array.IndexOf(Base32Alphabet, c);
            if (v < 0) continue;
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return bytes.ToArray();
    }
}
