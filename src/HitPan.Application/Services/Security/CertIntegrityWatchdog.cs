using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services.Security;

/// <summary>
/// 인증서 무결성 워치독 — 작B v3.0 W2 보안 매니저 2 권고
///
/// 워치독 시나리오:
/// - WS-23: TPM 봉인 무효화 감지 (마더보드 교체·BIOS 리셋·Windows Update)
/// - WS-24: OneDrive·Dropbox·구글드라이브 동기화 폴더에 PFX 존재 감지
/// - WS-25: 인증서 만료 임박 알림 (D-30/D-7/D-Day)
///
/// 헌법 정합:
/// - #28 (Windows Update 자동 복구): TPM 무효화 자동 안내
/// - #30 (고객 PC 자가 회복): 본사 의존 0
/// - #22 (본사 데이터 0): 알림만, 데이터 전송 0
/// </summary>
public interface ICertIntegrityWatchdog
{
    /// <summary>전체 무결성 검증 (WS-23 + WS-24 + WS-25 종합).</summary>
    Task<WatchdogReport> CheckIntegrityAsync(string tenantId);
}

public sealed record WatchdogReport(
    bool TpmSealedValid,           // WS-23
    bool CloudSyncRisk,             // WS-24
    string? CloudSyncDetectedPath,
    int? DaysToExpiry,              // WS-25
    ExpiryAlertLevel ExpiryLevel,
    IReadOnlyList<string> Warnings);

public enum ExpiryAlertLevel
{
    None = 0,
    D30 = 30,     // 만료 30일 전 (정보)
    D7 = 7,       // 만료 7일 전 (경고)
    DDay = 1,     // 만료 당일 (긴급)
    Expired = -1  // 만료됨 (P0)
}

public sealed class CertIntegrityWatchdog : ICertIntegrityWatchdog
{
    // WS-24: 클라우드 동기화 위험 폴더 키워드
    private static readonly string[] CLOUD_KEYWORDS =
    [
        "OneDrive", "Dropbox", "Google Drive", "GoogleDrive",
        "Naver Cloud", "NaverCloud", "iCloud", "MEGA", "Box",
        "pCloud", "Yandex.Disk", "Sync"
    ];

    private readonly ITpmKeyService _tpm;
    private readonly ICertStorageService _storage;
    private readonly ILogger<CertIntegrityWatchdog> _logger;

    public CertIntegrityWatchdog(
        ITpmKeyService tpm,
        ICertStorageService storage,
        ILogger<CertIntegrityWatchdog> logger)
    {
        _tpm = tpm;
        _storage = storage;
        _logger = logger;
    }

    public async Task<WatchdogReport> CheckIntegrityAsync(string tenantId)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        var warnings = new List<string>();
        bool tpmValid = true;
        bool cloudRisk = false;
        string? cloudPath = null;
        int? daysToExpiry = null;
        var expiryLevel = ExpiryAlertLevel.None;

        var data = await _storage.LoadAsync(tenantId);
        if (data is null)
        {
            warnings.Add("등록된 인증서 없음 — 전자세금계산서 발급 불가");
            return new WatchdogReport(false, false, null, null, ExpiryAlertLevel.None, warnings);
        }

        var (cert, metadata) = data.Value;

        // WS-23: TPM 봉인 무효화 감지
        try
        {
            tpmValid = _tpm.IsSealedKeyValid(cert.SealedMasterKey);
            if (!tpmValid)
            {
                warnings.Add("🚨 WS-23: TPM 봉인 무효화 감지 (마더보드 교체·BIOS 리셋 가능성). 사용자 PIN으로 재등록 필요.");
                _logger.LogWarning("WS-23 워치독 트리거 (Tenant: {Tenant})", tenantId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WS-23 TPM 검증 예외");
            warnings.Add("⚠️ TPM 봉인 상태 확인 불가");
        }

        // WS-24: 클라우드 동기화 폴더 감지
        try
        {
            var storageRoot = _storage.GetSecureStorageRoot();
            foreach (var keyword in CLOUD_KEYWORDS)
            {
                if (storageRoot.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    cloudRisk = true;
                    cloudPath = $"{storageRoot} ({keyword})";
                    warnings.Add($"🚨 WS-24: 클라우드 동기화 폴더에 인증서 저장 감지 ({keyword}). 안전한 위치로 즉시 이동 필요.");
                    _logger.LogWarning("WS-24 워치독 트리거: {Keyword} (Tenant: {Tenant})", keyword, tenantId);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WS-24 클라우드 동기화 검증 예외");
        }

        // WS-25: 만료 임박 알림
        var days = (int)(metadata.NotAfter - DateTime.UtcNow).TotalDays;
        daysToExpiry = days;
        if (days < 0)
        {
            expiryLevel = ExpiryAlertLevel.Expired;
            warnings.Add($"🚨 WS-25: 인증서 만료됨 (만료일: {metadata.NotAfter:yyyy-MM-dd}). 즉시 재발급 필요.");
        }
        else if (days <= 1)
        {
            expiryLevel = ExpiryAlertLevel.DDay;
            warnings.Add($"🚨 WS-25: 인증서 오늘 만료 (D-Day). 즉시 갱신 필요.");
        }
        else if (days <= 7)
        {
            expiryLevel = ExpiryAlertLevel.D7;
            warnings.Add($"⚠️ WS-25: 인증서 만료 임박 (D-{days}). 갱신 권고.");
        }
        else if (days <= 30)
        {
            expiryLevel = ExpiryAlertLevel.D30;
            warnings.Add($"ℹ️ WS-25: 인증서 만료 30일 전 알림 (D-{days}). 갱신 준비.");
        }

        return new WatchdogReport(tpmValid, cloudRisk, cloudPath, daysToExpiry, expiryLevel, warnings);
    }
}
