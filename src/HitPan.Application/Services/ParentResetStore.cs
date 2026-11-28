using System.Data;
using System.Data.Common;
using Dapper;

namespace HitPan.Application.Services;

/// <summary>
/// 🔴 W-G1-52 갈래 C — 대표 비번 되찾기의 <b>DB 몫</b>만 모은 자리(감사 · 비번 규칙 · 순서는 <see cref="ParentPasswordResetService"/>).
/// </summary>
/// <remarks>
/// 나눈 까닭: H1(감사 「시작」 줄 실패 = DB 손 0) 순서를 DB 없이도 <b>동작으로</b> 재려고(비DB 시험이 가짜 저장소로 「불린 적 없음」 을 셈).
/// SQL 자체(조건 갱신 줄 수 1 · 대표 행만 · 동시 2요청)는 DB 게이트(<c>BackupCredentialGateParentResetDbTests</c> · CI)가 잰다.
/// </remarks>
public interface IParentResetStore
{
    /// <summary>표 지문으로 행을 찾는다(DB 시계로 만료 · 사용 여부 판정). 없으면 null. 읽기만.</summary>
    Task<ParentResetTokenRow?> FindTokenAsync(string tokenHash, CancellationToken ct);

    /// <summary>
    /// 한 트랜잭션: ① 표 소비(조건 갱신 · 줄 수 1 이어야) ② 대표 행 비번 교체 · 실패 칸 0 · 잠금 비움(대표 행 · 줄 수 1 이어야)
    /// ③ 갱신 토큰 폐기 ④ 세션 행 지움 ⑤ DB 감사 줄(<c>audit_logs</c>) → 커밋. ① · ② 가 1 이 아니면 되돌림.
    /// </summary>
    Task<ParentResetCommitOutcome> ConsumeAndResetAsync(ParentResetCommit commit, CancellationToken ct);
}

/// <summary>표 행 + 대상 판정(DB 시계).</summary>
/// <param name="TenantId">표 행의 회사 id(서버 DB 값 · #2).</param>
/// <param name="UserId">표 행의 대표 user_id.</param>
/// <param name="Expired">만료 시각이 지금(DB 시계) 이하.</param>
/// <param name="Used">이미 쓰임.</param>
/// <param name="TargetIsParent">대상 행이 지금도 같은 회사의 지워지지 않은 대표(<c>is_parent = 1</c>)인가.</param>
/// <param name="LoginId">대표 아이디(<c>users.email</c> — 아이디 방식 · #40). 대표가 아니면 null.</param>
public sealed record ParentResetTokenRow(string TenantId, string UserId, bool Expired, bool Used, bool TargetIsParent, string? LoginId);

/// <summary>교체 묶음 입력. 비번 평문은 없다(해시만).</summary>
public sealed record ParentResetCommit(string TokenHash, string TenantId, string UserId, string NewPasswordHash, string? IpAddress);

/// <summary>교체 묶음 결과.</summary>
public enum ParentResetCommitOutcome
{
    /// <summary>커밋됨.</summary>
    Committed = 1,
    /// <summary>표 조건 갱신이 0줄(동시 요청에서 짐 · 그 사이 만료 · 이미 씀) → 되돌림.</summary>
    TokenNotConsumed = 2,
    /// <summary>대표 행 갱신이 0줄(그 사이 대표가 아니게 됨 · 지워짐) → 되돌림.</summary>
    NotParent = 3,
}

/// <summary>Dapper 구현 — 요청 범위 <see cref="IDbConnection"/>(Program.cs AddInfrastructure 등록)을 그대로 쓴다.</summary>
public sealed class DapperParentResetStore : IParentResetStore
{
    private readonly IDbConnection _db;

    /// <summary>만든다.</summary>
    public DapperParentResetStore(IDbConnection db) => _db = db;

    /// <inheritdoc />
    public async Task<ParentResetTokenRow?> FindTokenAsync(string tokenHash, CancellationToken ct)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        // #13 — 칸 = installer/hitpan_db_clean.sql `parent_reset_tokens`(이 갈래 · DB-151) · `users`(user_id · tenant_id · email · is_parent · is_deleted).
        var raw = await _db.QueryFirstOrDefaultAsync<TokenRaw>(new CommandDefinition(@"
            SELECT t.tenant_id AS TenantId,
                   t.user_id   AS UserId,
                   CASE WHEN t.expires_at <= UTC_TIMESTAMP(6) THEN 1 ELSE 0 END AS ExpiredFlag,
                   CASE WHEN t.used_at IS NOT NULL THEN 1 ELSE 0 END             AS UsedFlag,
                   CASE WHEN u.user_id IS NOT NULL THEN 1 ELSE 0 END             AS ParentFlag,
                   u.email     AS LoginId
            FROM parent_reset_tokens t
            LEFT JOIN users u
                   ON u.user_id = t.user_id
                  AND u.tenant_id = t.tenant_id
                  AND u.is_parent = 1
                  AND u.is_deleted = 0
            WHERE t.token_hash = @Hash
            LIMIT 1",
            new { Hash = tokenHash }, cancellationToken: ct)).ConfigureAwait(false);
        if (raw is null) return null;
        var parent = raw.ParentFlag == 1;
        return new ParentResetTokenRow(raw.TenantId, raw.UserId, raw.ExpiredFlag == 1, raw.UsedFlag == 1, parent, parent ? raw.LoginId : null);
    }

    /// <inheritdoc />
    public async Task<ParentResetCommitOutcome> ConsumeAndResetAsync(ParentResetCommit commit, CancellationToken ct)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        using var tx = _db.BeginTransaction();

        // ① 1회 소비 = 조건 갱신 줄 수 1(K7 · G10 동시 2요청). 「읽고 나서 쓰기」 로 바꾸면 둘 다 성공한다 — 변이 대상.
        var consumed = await _db.ExecuteAsync(new CommandDefinition(@"
            UPDATE parent_reset_tokens
               SET used_at = UTC_TIMESTAMP(6)
             WHERE token_hash = @Hash
               AND tenant_id = @TenantId
               AND user_id = @UserId
               AND used_at IS NULL
               AND expires_at > UTC_TIMESTAMP(6)",
            new { Hash = commit.TokenHash, commit.TenantId, commit.UserId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        if (consumed != 1)
        {
            tx.Rollback();
            return ParentResetCommitOutcome.TokenNotConsumed;
        }

        // ② 대표 행만(자식 0 · 아이디 · is_parent · 권한 칸 손 0 — 설계 §8-4 4).
        var changed = await _db.ExecuteAsync(new CommandDefinition(@"
            UPDATE users SET
                   password_hash = @Hash,
                   failed_login_count = 0,
                   lockout_end = NULL,
                   password_changed_at = UTC_TIMESTAMP(6),
                   updated_at = UTC_TIMESTAMP(6)
             WHERE user_id = @UserId
               AND tenant_id = @TenantId
               AND is_parent = 1
               AND is_deleted = 0",
            new { Hash = commit.NewPasswordHash, commit.UserId, commit.TenantId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        if (changed != 1)
        {
            tx.Rollback();
            return ParentResetCommitOutcome.NotParent;
        }

        // ③ ④ 출입증 끊기 — UserService.CutAccessAsync 와 같은 두 줄(로그아웃 선례 순서 · 그 계정 전부).
        await _db.ExecuteAsync(new CommandDefinition(
            "UPDATE refresh_tokens SET is_revoked = 1 WHERE user_id = @UserId AND is_revoked = 0",
            new { commit.UserId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        await _db.ExecuteAsync(new CommandDefinition(
            "DELETE FROM user_sessions WHERE user_id = @UserId",
            new { commit.UserId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        // ⑤ 감사 둘째 벌(DB 보조 · 설계 §6) — 같은 트랜잭션이라 「바뀜」 과 한 몸. 비번 · 표 값 0.
        await _db.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO audit_logs (log_id, tenant_id, user_id, account_type, ip_address, method, endpoint, status_code, user_agent, created_at)
            VALUES (@LogId, @TenantId, @UserId, 'tenant_admin', @Ip, 'POST', @Endpoint, 200, NULL, UTC_TIMESTAMP(6))",
            new
            {
                LogId = Guid.NewGuid().ToString(),
                commit.TenantId,
                commit.UserId,
                Ip = Truncate(commit.IpAddress, 50),
                Endpoint = AuditEndpoint,
            }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        tx.Commit();
        return ParentResetCommitOutcome.Committed;
    }

    /// <summary>DB 감사 줄의 endpoint 칸 값(고정 낱말 · 시험이 이 값으로 셈).</summary>
    public const string AuditEndpoint = "/api/parent-reset#parent-password-reset";

    private static string? Truncate(string? s, int max) => s is null ? null : (s.Length <= max ? s : s[..max]);

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open) return;
        if (_db is DbConnection dbc) await dbc.OpenAsync(ct).ConfigureAwait(false);
        else _db.Open();
    }

    private sealed class TokenRaw
    {
        public string TenantId { get; set; } = "";
        public string UserId { get; set; } = "";
        public long ExpiredFlag { get; set; }
        public long UsedFlag { get; set; }
        public long ParentFlag { get; set; }
        public string? LoginId { get; set; }
    }
}

/// <summary>
/// 🔴 W-G1-52 갈래 C — 오프라인 명령 <c>parent-reset</c> 이 부르는 1회용 표 만들기(DB 행만 · 감사 파일 손 0 = H3).
/// </summary>
public static class ParentResetTokenIssuer
{
    /// <summary>표 수명(분) — 설계 §8-4 2 「10분 · 1회」.</summary>
    public const int LifetimeMinutes = 10;

    /// <summary>만들기 결과.</summary>
    public enum Outcome
    {
        /// <summary>만듦.</summary>
        Issued = 1,
        /// <summary>대표(<c>is_parent = 1</c> · 안 지워짐) 행 0 → 멈춤.</summary>
        NoParent = 2,
        /// <summary>대표 행 2개 이상 → 멈춤(어느 쪽인지 정하지 않음).</summary>
        MultipleParents = 3,
    }

    /// <summary>
    /// 이 DB 의 대표 행이 <b>정확히 1개</b>일 때만 1회용 표를 만든다. DB 엔 지문(SHA-256)만 · 표 값은 돌려주기만 한다(저장 · 로그 0).
    /// </summary>
    public static async Task<(Outcome Outcome, string? Token)> IssueAsync(IDbConnection db, CancellationToken ct)
    {
        if (db.State != ConnectionState.Open)
        {
            if (db is DbConnection dbc) await dbc.OpenAsync(ct).ConfigureAwait(false);
            else db.Open();
        }

        // 한 DB = 한 회사(슬롯마다 db.conf · DB) — 회사 조건 없이 이 DB 전체에서 센다(2 회사 대표가 섞여 있어도 멈춤).
        var parents = (await db.QueryAsync<ParentRow>(new CommandDefinition(@"
            SELECT user_id AS UserId, tenant_id AS TenantId
            FROM users
            WHERE is_parent = 1 AND is_deleted = 0
            LIMIT 2", cancellationToken: ct)).ConfigureAwait(false)).ToList();
        if (parents.Count == 0) return (Outcome.NoParent, null);
        if (parents.Count > 1) return (Outcome.MultipleParents, null);

        var token = ParentResetToken.NewToken();
        await db.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO parent_reset_tokens (token_id, tenant_id, user_id, token_hash, created_at, expires_at, used_at)
            VALUES (@Id, @TenantId, @UserId, @Hash, UTC_TIMESTAMP(6), DATE_ADD(UTC_TIMESTAMP(6), INTERVAL @Minutes MINUTE), NULL)",
            new
            {
                Id = Guid.NewGuid().ToString(),
                parents[0].TenantId,
                parents[0].UserId,
                Hash = ParentResetToken.Hash(token),
                Minutes = LifetimeMinutes,
            }, cancellationToken: ct)).ConfigureAwait(false);
        return (Outcome.Issued, token);
    }

    private sealed class ParentRow
    {
        public string UserId { get; set; } = "";
        public string TenantId { get; set; } = "";
    }
}

/// <summary>1회용 표 값 · 지문 규칙(명령 · API · 시험이 같은 한 벌을 씀).</summary>
public static class ParentResetToken
{
    /// <summary>표 글자 수 — 32 바이트 무작위의 base64url(채움 없음).</summary>
    public const int Length = 43;

    /// <summary>새 표(32 바이트 암호 난수 · base64url).</summary>
    public static string NewToken()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>표 꼴이 맞나(글자 수 · base64url 글자만) — 틀리면 DB 에 묻지 않는다.</summary>
    public static bool IsWellFormed(string? token)
    {
        if (token is null || token.Length != Length) return false;
        foreach (var c in token)
        {
            var ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>DB 에 넣는 지문 = SHA-256(표 UTF-8) 소문자 16진 64자.</summary>
    public static string Hash(string token) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
