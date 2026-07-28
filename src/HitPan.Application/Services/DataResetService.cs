using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.DataReset;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// 모든데이터 초기화(히트판 자료 포맷) 서비스 (사장님 결재 2026-06-23).
///
/// 사장님 명세:
///  - 범위: 부모계정·회사정보·로그 제외한 모든 데이터 (원장·장부 포함 = 포맷)
///  - 실행 전 ① 경고(프론트) ② 부모계정 패스워드 재입력 ③ 강제 백업 → 실행
///  - 로그(audit_logs/audit_trail 등)는 초기화 안 함. 초기화 동작 자체도 로그에 남긴다.
///
/// §#3 INSERT ONLY 원장 예외: 평상시엔 stock_ledger·journal_lines UPDATE/DELETE 금지지만,
///   "포맷"은 사장님이 명시 결재한 전체 초기화이므로 원장·장부도 비운다(갓 설치한 빈 장부로 복귀).
/// §#22 본사 미수신: 모든 처리는 고객사 로컬에서만. 본사 전송 0.
/// §#2 멀티테넌트: 모든 DELETE 는 tenant_id 범위로만 — 타 테넌트(있을 수 없으나 방어) 무손상.
/// </summary>
public sealed class DataResetService : IDataResetService
{
    private readonly IDbConnection _db;
    private readonly IBackupService _backupService;
    private readonly IAuditService _audit;
    private readonly ILogger<DataResetService> _logger;

    public DataResetService(
        IDbConnection db,
        IBackupService backupService,
        IAuditService audit,
        ILogger<DataResetService> logger)
    {
        _db = db;
        _backupService = backupService;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>
    /// 초기화에서 보존하는 테이블 화이트리스트 (사장님 결재 2026-06-23 분류).
    /// 여기 없는 테이블은 모두 tenant_id 범위로 비운다.
    /// ① 계정  ② 회사정보  ③ 로그  ④ 구조·설정.
    /// </summary>
    private static readonly HashSet<string> PreservedTables = new(StringComparer.OrdinalIgnoreCase)
    {
        // ① 계정 (부모계정·권한·약관동의·세션 — 로그인 유지)
        "users", "user_permissions", "user_terms_consent", "refresh_tokens", "user_sessions",

        // ② 회사정보 (랜딩 자동반영 회사정보·인증서·전자세금 설정)
        "tenants", "local_company", "local_subscription", "subscriptions",
        "tenant_settings", "tenant_certificates", "tenant_etax_settings", "tenant_devices",

        // ③ 로그 (초기화 동작도 여기 남아야 함 — 사장님 명세)
        "audit_logs", "audit_trail", "login_attempts", "force_edit_logs",
        "device_login_logs", "security_alerts", "ai_usage_logs",
        "stock_adjust_logs", "haccp_logs", "mold_production_log",

        // ④ 구조·설정 (운영 골격 — EF 이력·백업/이메일/결제/공통코드/양식/결재설정 등)
        "__efmigrationshistory",
        "backup_settings", "backup_history", "restore_history",
        "email_settings",
        "billing_settings", "billing_subscriptions", "billing_invoices",
        "billing_payment_methods", "billing_payment_attempts",
        "common_codes", "form_templates",
        "approval_settings", "workflow_settings",
        "idempotency_keys", "sync_tokens",
    };

    public async Task<DataResetResponse> ResetAllAsync(
        DataResetRequest request, string tenantId, string userId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // ── 1) 부모계정 패스워드 재검증 (본인 확인) ──
        var hash = await _db.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            SELECT password_hash FROM users
            WHERE user_id = @UserId AND tenant_id = @TenantId
              AND account_type = 'tenant_admin' AND is_active = 1
            LIMIT 1
            """,
            new { UserId = userId, TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);

        if (string.IsNullOrEmpty(hash) || !BCrypt.Net.BCrypt.Verify(request.Password, hash))
        {
            // 실패도 로그에 남긴다 (시도 추적).
            await _audit.LogAsync("data_reset_denied", "system", null, reason: "패스워드 불일치 또는 부모계정 아님", ct: ct)
                .ConfigureAwait(false);
            return new DataResetResponse { Success = false, Error = "비밀번호가 일치하지 않거나 부모계정이 아닙니다." };
        }

        // ── 2) 강제 백업 (초기화 직전 — 되돌릴 안전망) ──
        var backup = await _backupService.RunBackupAsync(tenantId, "before_data_reset", ct).ConfigureAwait(false);
        if (!backup.Success)
        {
            await _audit.LogAsync("data_reset_aborted", "system", null,
                reason: $"강제 백업 실패로 초기화 중단: {backup.Error}", ct: ct).ConfigureAwait(false);
            return new DataResetResponse
            {
                Success = false,
                BackupId = backup.BackupId,
                Error = "초기화 전 강제 백업에 실패하여 작업을 중단했습니다. 백업 설정을 확인해주세요."
            };
        }

        // ── 3) 보존 화이트리스트 외 테이블을 tenant_id 범위로 삭제 ──
        // 현재 DB 의 실제 테이블 목록을 읽어, 보존 목록에 없고 tenant_id 컬럼이 있는 것만 비운다.
        var allTables = (await _db.QueryAsync<string>(new CommandDefinition(
            """
            SELECT t.table_name
            FROM information_schema.tables t
            WHERE t.table_schema = DATABASE() AND t.table_type = 'BASE TABLE'
            """, cancellationToken: ct)).ConfigureAwait(false)).ToList();

        var targets = allTables
            .Where(t => !PreservedTables.Contains(t))
            .ToList();

        var cleared = 0;
        var conn = (DbConnection)_db;
        using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // FK 순환·자식→부모 순서 의존 회피: 트랜잭션 내에서만 FK 체크 해제.
            await conn.ExecuteAsync(new CommandDefinition("SET FOREIGN_KEY_CHECKS = 0", transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            foreach (var table in targets)
            {
                // tenant_id 컬럼이 있는 테이블만 tenant 범위 삭제. 없으면(공용/구조) 스킵 — 안전.
                var hasTenant = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                    """
                    SELECT COUNT(*) FROM information_schema.columns
                    WHERE table_schema = DATABASE() AND table_name = @Table AND column_name = 'tenant_id'
                    """,
                    new { Table = table }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

                if (hasTenant == 0)
                {
                    continue;
                }

                // 테이블명은 information_schema 에서 온 화이트리스트 외 실제 테이블명이라 인젝션 위험 없음(백틱 감쌈).
                var affected = await conn.ExecuteAsync(new CommandDefinition(
                    $"DELETE FROM `{table}` WHERE tenant_id = @TenantId",
                    new { TenantId = tenantId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

                if (affected > 0)
                {
                    cleared++;
                }
            }

            await conn.ExecuteAsync(new CommandDefinition("SET FOREIGN_KEY_CHECKS = 1", transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            _logger.LogError(ex, "모든데이터 초기화 실패 (tenantId={TenantId})", tenantId);
            await _audit.LogAsync("data_reset_failed", "system", null,
                reason: $"초기화 중 오류, 롤백됨: {ex.Message}", ct: ct).ConfigureAwait(false);
            return new DataResetResponse { Success = false, BackupId = backup.BackupId, Error = "초기화 중 오류가 발생하여 롤백했습니다." };
        }

        // ── 4) 초기화 동작 자체를 로그에 기록 (사장님 명세 — audit_trail 은 보존 + 이 기록도 남음) ──
        await _audit.LogAsync(
            "data_reset",
            "system",
            entityId: tenantId,
            afterJson: $"{{\"clearedTableCount\":{cleared},\"backupId\":\"{backup.BackupId}\"}}",
            reason: "모든데이터 초기화(히트판 자료 포맷) — 부모계정·회사정보·로그 보존",
            ct: ct).ConfigureAwait(false);

        _logger.LogWarning("모든데이터 초기화 완료 (tenantId={TenantId}, 비운 테이블={Cleared}, 백업={BackupId})",
            tenantId, cleared, backup.BackupId);

        return new DataResetResponse
        {
            Success = true,
            BackupId = backup.BackupId,
            ClearedTableCount = cleared
        };
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State != ConnectionState.Open && _db is DbConnection dbc)
        {
            await dbc.OpenAsync(ct).ConfigureAwait(false);
        }
    }
}
