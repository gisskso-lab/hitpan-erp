using System.Text;
using HitPan.Application.Interfaces;
using HitPan.Infrastructure.Security;

namespace HitPan.Tests.Security;

/// <summary>
/// W2 D4 (2026-05-12) — BinaryCryptoServiceAdapter 단위 테스트.
///
/// 검증 범위:
/// - 평문 ↔ VARBINARY byte[] 라운드트립 (CRIMINAL_DOMAIN_POLICY.md §6.1)
/// - NULL/빈 문자열 graceful 처리
/// - IV 랜덤 — 동일 평문 → 다른 암호문 (CBC + GenerateIV)
/// - 헌법 #5 AES-256, 헌법 #18·#22 본사 송신 0
///
/// 테스트 키: 32바이트 (256bit) — 마스터키 분실 시나리오 검증 별도.
/// </summary>
public class BinaryCryptoServiceAdapterTests : IDisposable
{
    private readonly string? _originalKey;
    private readonly IBinaryCryptoService _crypto;

    public BinaryCryptoServiceAdapterTests()
    {
        _originalKey = Environment.GetEnvironmentVariable("ERP_ENCRYPTION_KEY");

        // 테스트용 32바이트 키 (Base64)
        var testKey = new byte[32];
        for (int i = 0; i < 32; i++) testKey[i] = (byte)(i + 1);
        Environment.SetEnvironmentVariable("ERP_ENCRYPTION_KEY", Convert.ToBase64String(testKey));

        var encryption = new EncryptionService();
        _crypto = new BinaryCryptoServiceAdapter(encryption);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ERP_ENCRYPTION_KEY", _originalKey);
    }

    [Fact(DisplayName = "BC-01: 평문 → byte[] → 평문 라운드트립 — 주민번호 형식")]
    public void Encrypt_Then_Decrypt_Roundtrip_ResidentNo()
    {
        var plain = "880101-1234567";

        var cipher = _crypto.EncryptToBytes(plain);
        Assert.NotNull(cipher);
        Assert.True(cipher!.Length > 16, "암호문은 IV(16바이트) 포함하여 16바이트보다 커야 한다");

        // 암호문에 평문 바이트 시퀀스가 그대로 노출되지 않아야 한다
        var plainBytes = Encoding.UTF8.GetBytes(plain);
        Assert.False(ContainsSubsequence(cipher, plainBytes), "암호문에 평문이 노출되면 안 된다");

        var decrypted = _crypto.DecryptFromBytes(cipher);
        Assert.Equal(plain, decrypted);
    }

    [Fact(DisplayName = "BC-02: 동일 평문 → 다른 암호문 (IV 랜덤)")]
    public void Encrypt_SameInput_ProducesDifferentCipher()
    {
        var plain = "5000000";

        var cipher1 = _crypto.EncryptToBytes(plain);
        var cipher2 = _crypto.EncryptToBytes(plain);

        Assert.NotNull(cipher1);
        Assert.NotNull(cipher2);
        Assert.False(cipher1!.SequenceEqual(cipher2!),
            "IV가 랜덤이므로 동일 평문이라도 매번 다른 암호문이 나와야 한다");

        // 그러나 둘 다 같은 평문으로 복호화되어야 한다
        Assert.Equal(_crypto.DecryptFromBytes(cipher1), _crypto.DecryptFromBytes(cipher2));
    }

    [Theory(DisplayName = "BC-03: NULL/빈 입력 → NULL 반환 (graceful)")]
    [InlineData(null)]
    [InlineData("")]
    public void Encrypt_NullOrEmpty_ReturnsNull(string? input)
    {
        var cipher = _crypto.EncryptToBytes(input);
        Assert.Null(cipher);
    }

    [Fact(DisplayName = "BC-04: NULL/빈 byte[] 복호화 → NULL 반환 (graceful)")]
    public void Decrypt_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(_crypto.DecryptFromBytes(null));
        Assert.Null(_crypto.DecryptFromBytes(Array.Empty<byte>()));
    }

    private static bool ContainsSubsequence(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length) return false;
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match) return true;
        }
        return false;
    }
}
