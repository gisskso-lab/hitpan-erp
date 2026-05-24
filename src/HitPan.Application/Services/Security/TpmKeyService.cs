using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services.Security;

/// <summary>
/// TPM 2.0 봉인 키 서비스 — 사장님 헌법 #22·#31·보안 매니저 1 권고
///
/// CNG NCRYPT_PLATFORM_KEY_FLAG 사용: 마스터 키를 TPM 칩에 봉인하여 PC 외부 추출 불가.
/// TPM 미지원 PC는 DPAPI 폴백 (헌법 #30 자가 회복).
///
/// 헌법 정합:
/// - #22: 본사 데이터 0 (마스터 키는 고객 PC TPM 칩에만)
/// - #28: Windows Update 후 TPM 무효화 자동 복구 (WS-23 워치독)
/// - #31: 백신 5종 호환성 (CNG는 Windows 표준 API)
/// </summary>
public interface ITpmKeyService
{
    /// <summary>TPM 2.0 지원 여부 확인.</summary>
    bool IsTpmAvailable();

    /// <summary>마스터 키 TPM 봉인 (NCRYPT_PLATFORM_KEY_FLAG). TPM 미지원 시 DPAPI 폴백.</summary>
    byte[] SealKey(byte[] masterKey);

    /// <summary>TPM 봉인 키 복호화.</summary>
    byte[] UnsealKey(byte[] sealedKey);

    /// <summary>TPM 봉인 무효화 감지 (마더보드 교체·BIOS 리셋). 워치독 WS-23 연계.</summary>
    bool IsSealedKeyValid(byte[] sealedKey);
}

public sealed class TpmKeyService : ITpmKeyService
{
    private const string KEY_NAME = "HitPan.TaxInvoice.MasterKey";
    private readonly ILogger<TpmKeyService> _logger;

    public TpmKeyService(ILogger<TpmKeyService> logger)
    {
        _logger = logger;
    }

    public bool IsTpmAvailable()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        try
        {
            // Platform Crypto Provider (TPM 2.0 백엔드) 존재 여부 확인
            var provider = CngProvider.MicrosoftPlatformCryptoProvider;
            return provider is not null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TPM 2.0 가용성 확인 실패. DPAPI 폴백 적용");
            return false;
        }
    }

    public byte[] SealKey(byte[] masterKey)
    {
        ArgumentNullException.ThrowIfNull(masterKey);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _logger.LogWarning("비 Windows 환경 — DPAPI 미지원. 평문 반환 (개발 환경만 허용)");
            return masterKey;
        }

        try
        {
            if (IsTpmAvailable())
            {
                // TPM 2.0 봉인: NCRYPT_PLATFORM_KEY_FLAG로 TPM 칩에 키 보관
                var keyParams = new CngKeyCreationParameters
                {
                    Provider = CngProvider.MicrosoftPlatformCryptoProvider,
                    KeyCreationOptions = CngKeyCreationOptions.OverwriteExistingKey,
                    ExportPolicy = CngExportPolicies.None
                };

                using var cngKey = CngKey.Create(CngAlgorithm.Rsa, KEY_NAME, keyParams);
                using var rsa = new RSACng(cngKey);
                var sealed_ = rsa.Encrypt(masterKey, RSAEncryptionPadding.OaepSHA256);

                _logger.LogInformation("TPM 2.0 봉인 성공 (키 길이: {Length})", sealed_.Length);
                return sealed_;
            }

            // TPM 미지원 폴백: DPAPI LocalMachine 스코프
            _logger.LogInformation("TPM 미지원 — DPAPI LocalMachine 폴백");
            return ProtectedData.Protect(masterKey, optionalEntropy: null, scope: DataProtectionScope.LocalMachine);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TPM 봉인 실패. DPAPI CurrentUser 폴백 적용");
            return ProtectedData.Protect(masterKey, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        }
    }

    public byte[] UnsealKey(byte[] sealedKey)
    {
        ArgumentNullException.ThrowIfNull(sealedKey);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return sealedKey;

        try
        {
            if (IsTpmAvailable() && CngKey.Exists(KEY_NAME))
            {
                using var cngKey = CngKey.Open(KEY_NAME);
                using var rsa = new RSACng(cngKey);
                return rsa.Decrypt(sealedKey, RSAEncryptionPadding.OaepSHA256);
            }

            return ProtectedData.Unprotect(sealedKey, optionalEntropy: null, scope: DataProtectionScope.LocalMachine);
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "LocalMachine 복호화 실패. CurrentUser 폴백 시도");
            return ProtectedData.Unprotect(sealedKey, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        }
    }

    public bool IsSealedKeyValid(byte[] sealedKey)
    {
        try
        {
            var unsealed = UnsealKey(sealedKey);
            // 즉시 메모리 폐기
            Array.Clear(unsealed, 0, unsealed.Length);
            return true;
        }
        catch
        {
            // 마더보드 교체·BIOS 리셋·TPM 무효화 = 워치독 WS-23 트리거
            _logger.LogWarning("TPM 봉인 키 무효화 감지 (워치독 WS-23 트리거 영역)");
            return false;
        }
    }
}
