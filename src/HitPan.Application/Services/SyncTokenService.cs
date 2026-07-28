using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// Sync 토큰 발급·검증 서비스 (사장님 결재 2026-06-01)
/// 헌법 #5 (해시만 저장) · #7 (역할 분리) · #18 (백오피스 Pull 전용) · #23 (5중 검증)
/// 정책: 24h 만료 + 회전 + 읽기 전용
/// </summary>
public class SyncTokenService : ISyncTokenService
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<SyncTokenService> _logger;
    private const int TokenLifetimeHours = 24;
    private const int TokenLengthBytes = 32; // 256-bit

    public SyncTokenService(IUnitOfWork uow, ILogger<SyncTokenService> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public async Task<SyncTokenResult> IssueAsync(string tenantId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("tenantId required", nameof(tenantId));

        // 평문 토큰 생성 (URL-safe base64, 32바이트 = 43자)
        var bytes = RandomNumberGenerator.GetBytes(TokenLengthBytes);
        var plain = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var hash = HashToken(plain);
        var now = DateTime.UtcNow;
        var expires = now.AddHours(TokenLifetimeHours);

        var db = _uow.GetDbConnection();
        await EnsureOpenAsync(db, ct).ConfigureAwait(false);

        // 회전: 이전 활성 토큰 즉시 무효화
        await db.ExecuteAsync(
            "UPDATE sync_tokens SET revoked_at = UTC_TIMESTAMP() WHERE tenant_id = @TenantId AND revoked_at IS NULL",
            new { TenantId = tenantId });

        // 신규 토큰 INSERT
        await db.ExecuteAsync(@"
            INSERT INTO sync_tokens (token_id, tenant_id, token_hash, issued_at, expires_at)
            VALUES (@TokenId, @TenantId, @TokenHash, UTC_TIMESTAMP(), @ExpiresAt)",
            new
            {
                TokenId = Guid.NewGuid().ToString(),
                TenantId = tenantId,
                TokenHash = hash,
                ExpiresAt = expires
            });

        _logger.LogInformation("Sync token issued for tenant {TenantId}, expires {Expires}", tenantId, expires);
        return new SyncTokenResult(plain, expires);
    }

    public async Task<string?> ValidateAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var hash = HashToken(token);
        var db = _uow.GetDbConnection();
        await EnsureOpenAsync(db, ct).ConfigureAwait(false);

        var row = await db.QueryFirstOrDefaultAsync<(string TenantId, DateTime ExpiresAt, DateTime? RevokedAt)>(@"
            SELECT tenant_id AS TenantId, expires_at AS ExpiresAt, revoked_at AS RevokedAt
            FROM sync_tokens
            WHERE token_hash = @Hash",
            new { Hash = hash });

        if (row.TenantId is null) return null;
        if (row.RevokedAt is not null) return null;
        if (row.ExpiresAt <= DateTime.UtcNow) return null;

        // last_used_at 갱신 (best-effort, 실패해도 검증 통과)
        try
        {
            await db.ExecuteAsync(
                "UPDATE sync_tokens SET last_used_at = UTC_TIMESTAMP() WHERE token_hash = @Hash",
                new { Hash = hash });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update last_used_at for sync token");
        }

        return row.TenantId;
    }

    private static string HashToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static async Task EnsureOpenAsync(IDbConnection db, CancellationToken ct)
    {
        if (db.State == ConnectionState.Open) return;
        if (db is DbConnection c)
            await c.OpenAsync(ct).ConfigureAwait(false);
        else
            db.Open();
    }
}
