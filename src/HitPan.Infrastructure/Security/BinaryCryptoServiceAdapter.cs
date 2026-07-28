using System.Text;
using HitPan.Application.Interfaces;

namespace HitPan.Infrastructure.Security;

/// <summary>
/// IBinaryCryptoService 어댑터 — Application 레이어 추상화를 기존 IEncryptionService에 위임.
///
/// W2 D3 (2026-05-12 사장님 결재 A-2):
/// - 기존 IEncryptionService(EncryptBytes/DecryptBytes) 그대로 활용 (헌법 #1 추출 정신)
/// - 9개 사용처 손대지 않음 (회귀 위험 0)
/// - MdbMigrationService 등 Application 레이어에서 본 인터페이스 주입받아 사용
/// </summary>
public sealed class BinaryCryptoServiceAdapter : IBinaryCryptoService
{
    private readonly IEncryptionService _encryption;

    public BinaryCryptoServiceAdapter(IEncryptionService encryption)
    {
        _encryption = encryption;
    }

    public byte[]? EncryptToBytes(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return null;
        return _encryption.EncryptBytes(Encoding.UTF8.GetBytes(plaintext));
    }

    public string? DecryptFromBytes(byte[]? ciphertext)
    {
        if (ciphertext is null || ciphertext.Length == 0) return null;
        return Encoding.UTF8.GetString(_encryption.DecryptBytes(ciphertext));
    }
}
