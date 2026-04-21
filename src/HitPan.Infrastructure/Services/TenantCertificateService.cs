using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Certificate;
using HitPan.Application.Interfaces;
using HitPan.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;

namespace HitPan.Application.Services;

/// <summary>
/// 범용인증서 관리 서비스.
/// - PFX 파일 바이너리: AES-256 (IEncryptionService.EncryptBytes)
/// - PFX 비밀번호: DPAPI (IDataProtector) — 키 외부 의존 없음
/// - 감사로그: 업로드/폐기/기본설정 변경 전건
/// </summary>
public sealed class TenantCertificateService : ITenantCertificateService
{
    private readonly IDbConnection _db;
    private readonly IEncryptionService _aes;
    private readonly IDataProtector _passwordProtector;
    private readonly IAuditService _audit;

    private const string PasswordProtectorPurpose = "HitPan.TenantCertificate.Password.v1";

    public TenantCertificateService(
        IDbConnection db,
        IEncryptionService aes,
        IDataProtectionProvider dataProtection,
        IAuditService audit)
    {
        _db = db;
        _aes = aes;
        _passwordProtector = dataProtection.CreateProtector(PasswordProtectorPurpose);
        _audit = audit;
    }

    public async Task<List<CertificateListDto>> GetAllAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var rows = (await _db.QueryAsync<CertificateListDto>(new CommandDefinition(
            """
            SELECT cert_id AS CertId, cert_type AS CertType, subject_name AS SubjectName,
                   issuer_name AS IssuerName, serial_no AS SerialNo,
                   valid_from AS ValidFrom, valid_until AS ValidUntil,
                   is_primary AS IsPrimary, last_used_at AS LastUsedAt,
                   status AS Status
            FROM tenant_certificates
            WHERE tenant_id = @TenantId
            ORDER BY is_primary DESC, valid_until ASC
            """,
            new { TenantId = tenantId }, cancellationToken: ct))).ToList();

        foreach (var r in rows)
        {
            r.DaysUntilExpiry = (int)(r.ValidUntil - DateTime.Today).TotalDays;
            if (r.DaysUntilExpiry < 0 && r.Status == "active") r.Status = "expired";
        }
        return rows;
    }

    public async Task<CertificateListDto?> GetAsync(string certId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var row = await _db.QueryFirstOrDefaultAsync<CertificateListDto>(new CommandDefinition(
            """
            SELECT cert_id AS CertId, cert_type AS CertType, subject_name AS SubjectName,
                   issuer_name AS IssuerName, serial_no AS SerialNo,
                   valid_from AS ValidFrom, valid_until AS ValidUntil,
                   is_primary AS IsPrimary, last_used_at AS LastUsedAt,
                   status AS Status
            FROM tenant_certificates
            WHERE cert_id = @CertId AND tenant_id = @TenantId
            """,
            new { CertId = certId, TenantId = tenantId }, cancellationToken: ct));

        if (row != null)
            row.DaysUntilExpiry = (int)(row.ValidUntil - DateTime.Today).TotalDays;
        return row;
    }

    public async Task<string> UploadAsync(
        UploadCertificateRequest request, byte[] pfxBytes, string tenantId, string userId, CancellationToken ct = default)
    {
        if (pfxBytes is null || pfxBytes.Length == 0)
            throw new InvalidOperationException("인증서 파일이 비어있습니다.");
        if (string.IsNullOrWhiteSpace(request.Password))
            throw new InvalidOperationException("인증서 비밀번호는 필수입니다.");
        if (string.IsNullOrWhiteSpace(request.SubjectName))
            throw new InvalidOperationException("주체명(사업자명)은 필수입니다.");
        if (request.ValidUntil <= DateTime.Today)
            throw new InvalidOperationException("유효기간이 이미 만료된 인증서입니다.");

        await EnsureOpenAsync(ct);
        using var tx = _db.BeginTransaction();
        var certId = Guid.NewGuid().ToString();
        try
        {
            // 파일은 AES-256, 비밀번호는 DPAPI
            var encryptedFile = _aes.EncryptBytes(pfxBytes);
            var protectedPassword = _passwordProtector.Protect(request.Password);

            // 기본 인증서 설정 시 기존 기본 해제
            if (request.IsPrimary)
            {
                await _db.ExecuteAsync(new CommandDefinition(
                    "UPDATE tenant_certificates SET is_primary = 0 WHERE tenant_id = @TenantId AND is_primary = 1",
                    new { TenantId = tenantId }, transaction: tx, cancellationToken: ct));
            }

            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO tenant_certificates
                  (cert_id, tenant_id, cert_type, subject_name, issuer_name, serial_no,
                   valid_from, valid_until, cert_file_encrypted, password_ref,
                   is_primary, status, uploaded_by)
                VALUES
                  (@CertId, @TenantId, @CertType, @SubjectName, @IssuerName, @SerialNo,
                   @ValidFrom, @ValidUntil, @FileEnc, @PwdRef,
                   @IsPrimary, 'active', @UploadedBy)
                """,
                new
                {
                    CertId = certId, TenantId = tenantId,
                    request.CertType, request.SubjectName, request.IssuerName, request.SerialNo,
                    request.ValidFrom, request.ValidUntil,
                    FileEnc = encryptedFile,
                    PwdRef = protectedPassword,
                    IsPrimary = request.IsPrimary ? 1 : 0,
                    UploadedBy = userId
                }, transaction: tx, cancellationToken: ct));

            tx.Commit();

            // 감사로그 — 민감 정보는 기록 금지, 메타만
            var afterJson = $"{{\"subject\":\"{request.SubjectName}\",\"type\":\"{request.CertType}\",\"valid_until\":\"{request.ValidUntil:yyyy-MM-dd}\",\"is_primary\":{(request.IsPrimary ? "true" : "false")}}}";
            await _audit.LogAsync("create", "certificate", certId, afterJson: afterJson, ct: ct);

            return certId;
        }
        catch
        {
            try { tx.Rollback(); } catch { /* 이미 닫힌 tx */ }
            throw;
        }
    }

    public async Task UpdateStatusAsync(string certId, UpdateCertificateStatusRequest request, string tenantId, string userId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        await _db.ExecuteAsync(new CommandDefinition(
            "UPDATE tenant_certificates SET status = @Status WHERE cert_id = @CertId AND tenant_id = @TenantId",
            new { Status = request.Status, CertId = certId, TenantId = tenantId },
            cancellationToken: ct));

        await _audit.LogAsync("state_change", "certificate", certId,
            afterJson: $"{{\"status\":\"{request.Status}\"}}", reason: request.Reason, ct: ct);
    }

    public async Task SetPrimaryAsync(string certId, string tenantId, string userId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        using var tx = _db.BeginTransaction();
        try
        {
            await _db.ExecuteAsync(new CommandDefinition(
                "UPDATE tenant_certificates SET is_primary = 0 WHERE tenant_id = @TenantId AND is_primary = 1",
                new { TenantId = tenantId }, transaction: tx, cancellationToken: ct));

            var affected = await _db.ExecuteAsync(new CommandDefinition(
                "UPDATE tenant_certificates SET is_primary = 1 WHERE cert_id = @CertId AND tenant_id = @TenantId AND status = 'active'",
                new { CertId = certId, TenantId = tenantId }, transaction: tx, cancellationToken: ct));

            if (affected == 0)
                throw new InvalidOperationException("활성 상태의 인증서만 기본으로 설정할 수 있습니다.");

            tx.Commit();

            await _audit.LogAsync("update", "certificate", certId, afterJson: "{\"is_primary\":true}", ct: ct);
        }
        catch
        {
            try { tx.Rollback(); } catch { /* 이미 닫힌 tx */ }
            throw;
        }
    }

    public async Task DeleteAsync(string certId, string tenantId, string userId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        await _db.ExecuteAsync(new CommandDefinition(
            "UPDATE tenant_certificates SET status = 'revoked', is_primary = 0 WHERE cert_id = @CertId AND tenant_id = @TenantId",
            new { CertId = certId, TenantId = tenantId }, cancellationToken: ct));

        await _audit.LogAsync("delete", "certificate", certId, ct: ct);
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open) return;
        if (_db is DbConnection dbc) { await dbc.OpenAsync(ct); return; }
        _db.Open();
    }
}
