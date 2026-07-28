using System.Text;
using HitPan.Application.Interfaces;

namespace HitPan.Infrastructure.Security;

/// <summary>
/// IPasswordEncryptor 어댑터 — 기존 IEncryptionService를 Application 레이어에 노출.
/// 짧은 비밀(SMTP 패스워드 등)을 byte[]로 암호화하여 VARBINARY 컬럼에 저장.
/// </summary>
public sealed class PasswordEncryptorAdapter : IPasswordEncryptor
{
    private readonly IEncryptionService _inner;

    public PasswordEncryptorAdapter(IEncryptionService inner)
    {
        _inner = inner;
    }

    public byte[] Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return Array.Empty<byte>();
        return _inner.EncryptBytes(Encoding.UTF8.GetBytes(plainText));
    }

    public string Decrypt(byte[] cipherBytes)
    {
        if (cipherBytes is null || cipherBytes.Length == 0) return "";
        var plain = _inner.DecryptBytes(cipherBytes);
        return Encoding.UTF8.GetString(plain);
    }
}
