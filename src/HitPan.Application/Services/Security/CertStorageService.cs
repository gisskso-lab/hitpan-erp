using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services.Security;

/// <summary>
/// 인증서 격리 저장소 — 사장님 헌법 #22·#30 정합 + 보안 매니저 2 권고
///
/// 저장 위치 강제: %LOCALAPPDATA%\HitPan\Secure\
/// - OneDrive·Dropbox·구글드라이브 동기화 폴더 밖 (보안 매니저 2 권고)
/// - 워치독 WS-24 (PFX 클라우드 동기화 감지) 연계
///
/// 헌법 정합:
/// - #22: 본사 데이터 0 (모든 인증서는 고객 PC만)
/// - #30: 고객 PC 자가 회복 (워치독 22 시나리오 정합)
/// </summary>
public interface ICertStorageService
{
    Task<string> SaveAsync(string tenantId, DoubleEncryptedCert encrypted, CertMetadata metadata);
    Task<(DoubleEncryptedCert Cert, CertMetadata Metadata)?> LoadAsync(string tenantId);
    Task<bool> ExistsAsync(string tenantId);
    Task<bool> DeleteAsync(string tenantId);
    string GetSecureStorageRoot();
}

public sealed record CertMetadata(
    string Subject,
    string Issuer,
    string SerialNumber,
    DateTime NotBefore,
    DateTime NotAfter,
    DateTime RegisteredAt);

public sealed class CertStorageService : ICertStorageService
{
    private const string SECURE_DIR_NAME = "Secure";
    private const string CERT_FILE = "tax-invoice.enc";
    private const string META_FILE = "tax-invoice.meta.json";

    private readonly ILogger<CertStorageService> _logger;

    public CertStorageService(ILogger<CertStorageService> logger)
    {
        _logger = logger;
    }

    public string GetSecureStorageRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "HitPan", SECURE_DIR_NAME);
    }

    private string GetTenantDir(string tenantId)
    {
        var root = GetSecureStorageRoot();
        var tenantDir = Path.Combine(root, tenantId);
        Directory.CreateDirectory(tenantDir);
        return tenantDir;
    }

    public async Task<string> SaveAsync(string tenantId, DoubleEncryptedCert encrypted, CertMetadata metadata)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentNullException.ThrowIfNull(encrypted);
        ArgumentNullException.ThrowIfNull(metadata);

        var tenantDir = GetTenantDir(tenantId);
        var certPath = Path.Combine(tenantDir, CERT_FILE);
        var metaPath = Path.Combine(tenantDir, META_FILE);

        // 인증서 바이너리 (4섹션 직렬화)
        using (var fs = new FileStream(certPath, FileMode.Create, FileAccess.Write))
        {
            await WriteSectionAsync(fs, encrypted.EncryptedPfx);
            await WriteSectionAsync(fs, encrypted.SealedMasterKey);
            await WriteSectionAsync(fs, encrypted.Nonce);
            await WriteSectionAsync(fs, encrypted.AuthTag);
        }

        // 메타데이터 (만료 알림용)
        var metaJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(metaPath, metaJson);

        // OneDrive·Dropbox 동기화 폴더 경고 (워치독 WS-24)
        CheckCloudSyncFolder(certPath);

        _logger.LogInformation("인증서 저장 완료 (Tenant: {Tenant}, Path: {Path})", tenantId, certPath);
        return certPath;
    }

    public async Task<(DoubleEncryptedCert Cert, CertMetadata Metadata)?> LoadAsync(string tenantId)
    {
        var tenantDir = GetTenantDir(tenantId);
        var certPath = Path.Combine(tenantDir, CERT_FILE);
        var metaPath = Path.Combine(tenantDir, META_FILE);

        if (!File.Exists(certPath) || !File.Exists(metaPath))
            return null;

        DoubleEncryptedCert cert;
        using (var fs = new FileStream(certPath, FileMode.Open, FileAccess.Read))
        {
            var encryptedPfx = await ReadSectionAsync(fs);
            var sealedKey = await ReadSectionAsync(fs);
            var nonce = await ReadSectionAsync(fs);
            var tag = await ReadSectionAsync(fs);
            cert = new DoubleEncryptedCert(encryptedPfx, sealedKey, nonce, tag);
        }

        var metaJson = await File.ReadAllTextAsync(metaPath);
        var metadata = JsonSerializer.Deserialize<CertMetadata>(metaJson)
            ?? throw new InvalidOperationException("인증서 메타데이터 파싱 실패");

        return (cert, metadata);
    }

    public Task<bool> ExistsAsync(string tenantId)
    {
        var certPath = Path.Combine(GetTenantDir(tenantId), CERT_FILE);
        return Task.FromResult(File.Exists(certPath));
    }

    public Task<bool> DeleteAsync(string tenantId)
    {
        var tenantDir = GetTenantDir(tenantId);
        if (Directory.Exists(tenantDir))
        {
            Directory.Delete(tenantDir, recursive: true);
            _logger.LogInformation("인증서 삭제 완료 (Tenant: {Tenant})", tenantId);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    private void CheckCloudSyncFolder(string path)
    {
        var cloudKeywords = new[] { "OneDrive", "Dropbox", "Google Drive", "Naver Cloud", "iCloud" };
        foreach (var keyword in cloudKeywords)
        {
            if (path.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("⚠️ 워치독 WS-24: 클라우드 동기화 폴더에 인증서 저장 감지! ({Keyword})", keyword);
                return;
            }
        }
    }

    private static async Task WriteSectionAsync(Stream stream, byte[] data)
    {
        var lenBytes = BitConverter.GetBytes(data.Length);
        await stream.WriteAsync(lenBytes);
        await stream.WriteAsync(data);
    }

    private static async Task<byte[]> ReadSectionAsync(Stream stream)
    {
        var lenBytes = new byte[4];
        await stream.ReadExactlyAsync(lenBytes);
        var len = BitConverter.ToInt32(lenBytes);
        var data = new byte[len];
        await stream.ReadExactlyAsync(data);
        return data;
    }
}
