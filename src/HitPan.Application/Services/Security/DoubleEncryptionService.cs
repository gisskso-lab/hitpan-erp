using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services.Security;

/// <summary>
/// 사장님 2중 암호화 서비스 — 사장님 헌법 #22·#23·#25·#31 통합
///
/// 구조 (3중 보안):
/// - 1차: 사용자 PIN (인증서 자체 비밀번호)
/// - 2차: ERP 마스터 키 (AES-256-GCM, PC별 고유)
/// - 3차: TPM 2.0 봉인 (마스터 키를 TPM 칩에 보관)
///
/// 헌법 정합:
/// - #22: 본사 데이터 0 (모든 키·인증서는 고객 PC만)
/// - #25: 3대 원칙 정합 (쉽게·정확하게·안전하게)
/// - 보안 매니저 1·2 + Red Team 본질 진단 5중 검증
/// </summary>
public interface IDoubleEncryptionService
{
    /// <summary>인증서 등록 (3중 암호화 + TPM 봉인).</summary>
    Task<DoubleEncryptedCert> EncryptAsync(byte[] pfxBytes, string userPin);

    /// <summary>인증서 로딩 (TPM → ERP 키 → 사용자 PIN 순으로 복호화).</summary>
    Task<X509Certificate2> DecryptAsync(DoubleEncryptedCert encrypted, string userPin);
}

public sealed record DoubleEncryptedCert(
    byte[] EncryptedPfx,
    byte[] SealedMasterKey,
    byte[] Nonce,
    byte[] AuthTag);

public sealed class DoubleEncryptionService : IDoubleEncryptionService
{
    private const int MASTER_KEY_SIZE = 32; // AES-256
    private const int NONCE_SIZE = 12;       // GCM 표준
    private const int TAG_SIZE = 16;

    private readonly ITpmKeyService _tpm;
    private readonly ILogger<DoubleEncryptionService> _logger;

    public DoubleEncryptionService(ITpmKeyService tpm, ILogger<DoubleEncryptionService> logger)
    {
        _tpm = tpm;
        _logger = logger;
    }

    public Task<DoubleEncryptedCert> EncryptAsync(byte[] pfxBytes, string userPin)
    {
        ArgumentNullException.ThrowIfNull(pfxBytes);
        ArgumentException.ThrowIfNullOrEmpty(userPin);

        byte[] masterKey = new byte[MASTER_KEY_SIZE];
        byte[] rawPfx = Array.Empty<byte>();

        try
        {
            // 0차 검증: 사용자 PIN으로 PFX 복호화 가능한지 확인
            using (var testCert = new X509Certificate2(pfxBytes, userPin, X509KeyStorageFlags.EphemeralKeySet))
            {
                rawPfx = testCert.Export(X509ContentType.Pfx, userPin);
            }

            // 1차: AES-256-GCM 마스터 키 생성 (CSPRNG)
            RandomNumberGenerator.Fill(masterKey);

            // 2차: 마스터 키로 PFX 암호화 (AES-256-GCM, 인증 암호화)
            var nonce = new byte[NONCE_SIZE];
            var ciphertext = new byte[rawPfx.Length];
            var tag = new byte[TAG_SIZE];
            RandomNumberGenerator.Fill(nonce);

            using (var aes = new AesGcm(masterKey, TAG_SIZE))
            {
                aes.Encrypt(nonce, rawPfx, ciphertext, tag);
            }

            // 3차: 마스터 키를 TPM 2.0 봉인 (CNG NCRYPT_PLATFORM_KEY_FLAG)
            var sealedKey = _tpm.SealKey(masterKey);

            _logger.LogInformation("3중 암호화 완료 (PFX: {Size}B, Sealed: {Sealed}B)", ciphertext.Length, sealedKey.Length);

            return Task.FromResult(new DoubleEncryptedCert(ciphertext, sealedKey, nonce, tag));
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "사용자 PIN 불일치 또는 PFX 파싱 실패");
            throw new InvalidOperationException("인증서 비밀번호가 올바르지 않거나 손상된 파일입니다.", ex);
        }
        finally
        {
            // 메모리 즉시 폐기 (Red Team 권고 정합)
            CryptographicOperations.ZeroMemory(masterKey);
            CryptographicOperations.ZeroMemory(rawPfx);
        }
    }

    public Task<X509Certificate2> DecryptAsync(DoubleEncryptedCert encrypted, string userPin)
    {
        ArgumentNullException.ThrowIfNull(encrypted);
        ArgumentException.ThrowIfNullOrEmpty(userPin);

        byte[] masterKey = Array.Empty<byte>();
        byte[] rawPfx = new byte[encrypted.EncryptedPfx.Length];

        try
        {
            // 3차: TPM → 마스터 키 추출
            masterKey = _tpm.UnsealKey(encrypted.SealedMasterKey);

            // 2차: 마스터 키로 PFX 복호화 (AES-256-GCM 무결성 검증 포함)
            using (var aes = new AesGcm(masterKey, TAG_SIZE))
            {
                aes.Decrypt(encrypted.Nonce, encrypted.EncryptedPfx, encrypted.AuthTag, rawPfx);
            }

            // 1차: 사용자 PIN으로 PFX 복호화 → 원본 인증서
            var cert = new X509Certificate2(rawPfx, userPin, X509KeyStorageFlags.EphemeralKeySet);

            _logger.LogInformation("3중 복호화 성공 (Subject: {Subject}, 만료: {Expiry})",
                cert.Subject, cert.NotAfter.ToString("yyyy-MM-dd"));

            return Task.FromResult(cert);
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "복호화 실패 (PIN 불일치 또는 TPM 무효화 가능성)");
            throw new InvalidOperationException("인증서 복호화 실패: 비밀번호 또는 TPM 봉인 무효화 확인 필요", ex);
        }
        finally
        {
            // Red Team 권고: 즉시 메모리 폐기
            CryptographicOperations.ZeroMemory(masterKey);
            CryptographicOperations.ZeroMemory(rawPfx);
        }
    }
}
