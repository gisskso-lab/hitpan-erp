using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// 🔴 W-G1-52 갈래 C — 대표 비번 되찾기 API 몫(설계 §8-4 3 · 덧 지시서 §1 H1 · H3 · §2).
/// </summary>
/// <remarks>
/// <para>🔴 <b>순서(H1)</b> — ① 입력 검사(두 칸 같음 · 규칙)는 DB · 감사 손 0 ② 표 읽기(읽기만) ③ 거절이면 감사 「실패」 줄(못 써도 바뀐 것 0)
/// ④ <b>감사 「시작」 줄 — 못 쓰면 여기서 멈춤</b>(DB 를 하나도 안 바꿈) ⑤ 한 트랜잭션 교체(표 소비 · 비번 · 끊기 · DB 감사 줄)
/// ⑥ 감사 「끝」 줄(성공 / 실패).</para>
/// <para>🔴 감사 덧말 = 고정 까닭 코드만(<see cref="Reasons"/>). 비번 · 표 값 · 아이디는 싣지 않는다(#40 · 계약 주석).</para>
/// <para>🔴 H3 — 감사 파일은 이 서비스만 쓴다. 오프라인 명령(<c>ParentResetCommand</c>)은 표 행만 만든다.</para>
/// </remarks>
public sealed class ParentPasswordResetService : IParentPasswordResetService
{
    private readonly IParentResetStore _store;
    private readonly ILocalAuditFile _audit;
    private readonly ILogger<ParentPasswordResetService> _logger;

    /// <summary>만든다.</summary>
    public ParentPasswordResetService(IParentResetStore store, ILocalAuditFile audit, ILogger<ParentPasswordResetService> logger)
    {
        _store = store;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>감사 덧말 · 결과 까닭 코드(고정 낱말 · 감사 덧말 글자 규칙 안).</summary>
    public static class Reasons
    {
        /// <summary>표 꼴 틀림 · 비어 있음.</summary>
        public const string TokenMalformed = "token_malformed";
        /// <summary>그런 표 없음.</summary>
        public const string TokenNotFound = "token_not_found";
        /// <summary>10분 지남.</summary>
        public const string TokenExpired = "token_expired";
        /// <summary>이미 씀.</summary>
        public const string TokenUsed = "token_used";
        /// <summary>대상이 대표 행이 아님(자식 · 지워짐 · 다른 회사).</summary>
        public const string NotParent = "not_parent";
        /// <summary>교체 때 표 조건 갱신 0줄(동시 요청에서 짐 등).</summary>
        public const string TokenRace = "token_race";
        /// <summary>교체 중 DB 오류(되돌려짐).</summary>
        public const string DbError = "db_error";
        /// <summary>두 칸 다름.</summary>
        public const string PasswordMismatch = "password_mismatch";
        /// <summary>규칙 밖.</summary>
        public const string PasswordRule = "password_rule";
        /// <summary>감사 「시작」 줄 못 씀(H1).</summary>
        public const string AuditUnavailable = "audit_unavailable";
        /// <summary>성공.</summary>
        public const string Ok = "ok";
    }

    /// <summary>화면 문구(개발 용어 0 · 어느 까닭인지 알려 주지 않는 한 문구).</summary>
    public const string TokenRejectedMessage =
        "이 안내는 더 쓸 수 없습니다(10분이 지났거나 이미 쓰였습니다). 메인PC 에서 「대표 비밀번호 다시 정하기」 를 다시 눌러 주세요.";

    /// <summary>H1 화면 문구.</summary>
    public const string AuditUnavailableMessage =
        "지금은 기록을 남길 수 없어 비밀번호를 바꾸지 않았습니다. 히트판을 다시 켠 뒤 처음부터 다시 해 주세요.";

    /// <summary>두 칸 다름 문구.</summary>
    public const string MismatchMessage = "두 칸에 넣은 새 비밀번호가 서로 다릅니다.";

    /// <summary>성공 문구.</summary>
    public const string SucceededMessage = "비밀번호가 바뀌었습니다. 새 비밀번호로 로그인해 주세요.";

    /// <inheritdoc />
    public async Task<ParentResetPeekResult> PeekAsync(string? token, CancellationToken ct = default)
    {
        if (!ParentResetToken.IsWellFormed(token)) return new ParentResetPeekResult(false, null);
        var row = await _store.FindTokenAsync(ParentResetToken.Hash(token!), ct).ConfigureAwait(false);
        if (row is null || row.Expired || row.Used || !row.TargetIsParent) return new ParentResetPeekResult(false, null);
        return new ParentResetPeekResult(true, row.LoginId);
    }

    /// <inheritdoc />
    public async Task<ParentResetResult> ResetAsync(ParentResetRequest request, string? ipAddress, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ① 입력 — DB · 감사 손 0.
        var newPassword = request.NewPassword ?? string.Empty;
        if (!string.Equals(newPassword, request.ConfirmPassword ?? string.Empty, StringComparison.Ordinal))
            return new ParentResetResult(ParentResetStatus.PasswordMismatch, MismatchMessage, Reasons.PasswordMismatch);
        var ruleError = ParentPasswordRule.Check(newPassword);
        if (ruleError is not null)
            return new ParentResetResult(ParentResetStatus.PasswordRule, ruleError, Reasons.PasswordRule);

        if (!ParentResetToken.IsWellFormed(request.Token))
        {
            await TryAuditRejectAsync(LocalAuditWords.UnknownUser, LocalAuditWords.UnknownUser, NewOpId(), Reasons.TokenMalformed, ct).ConfigureAwait(false);
            return Rejected(Reasons.TokenMalformed);
        }

        // ② 표 읽기 — 읽기만.
        var tokenHash = ParentResetToken.Hash(request.Token!);
        var row = await _store.FindTokenAsync(tokenHash, ct).ConfigureAwait(false);
        var reason = row is null ? Reasons.TokenNotFound
            : row.Used ? Reasons.TokenUsed
            : row.Expired ? Reasons.TokenExpired
            : !row.TargetIsParent ? Reasons.NotParent
            : null;
        if (reason is not null)
        {
            // ③ 거절 감사 — 바뀐 것 0 이라 못 써도 거절은 그대로(로그 경고만).
            await TryAuditRejectAsync(row?.TenantId ?? LocalAuditWords.UnknownUser, row?.UserId ?? LocalAuditWords.UnknownUser, NewOpId(), reason, ct).ConfigureAwait(false);
            return Rejected(reason);
        }

        var opId = NewOpId();

        // ④ 🔴 H1 — 「시작」 줄을 못 쓰면 멈춤. 이 줄보다 앞에서 DB 를 바꾸는 줄은 없다.
        try
        {
            await _audit.AppendAsync(new LocalAuditRecord(row!.TenantId, row.UserId, LocalAuditAction.ParentPasswordReset, LocalAuditResult.Started, opId), ct).ConfigureAwait(false);
        }
        catch (LocalAuditWriteException ex)
        {
            _logger.LogError(ex, "[ParentReset] 감사 시작 줄을 못 써 멈춤(바뀐 것 0) — 까닭 {Reason} · 줄 남음 {LineWritten}", ex.Reason, ex.LineWritten);
            return new ParentResetResult(ParentResetStatus.AuditUnavailable, AuditUnavailableMessage, Reasons.AuditUnavailable);
        }

        // ⑤ 한 트랜잭션 교체.
        var newHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        ParentResetCommitOutcome outcome;
        try
        {
            outcome = await _store.ConsumeAndResetAsync(new ParentResetCommit(tokenHash, row.TenantId, row.UserId, newHash, ipAddress), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[ParentReset] 교체 중 오류(되돌려짐) op={OpId}", opId);
            await TryAuditEndAsync(row.TenantId, row.UserId, opId, LocalAuditAction.ParentPasswordResetFailed, LocalAuditResult.Failed, Reasons.DbError, ct).ConfigureAwait(false);
            throw;
        }

        if (outcome != ParentResetCommitOutcome.Committed)
        {
            var why = outcome == ParentResetCommitOutcome.NotParent ? Reasons.NotParent : Reasons.TokenRace;
            await TryAuditEndAsync(row.TenantId, row.UserId, opId, LocalAuditAction.ParentPasswordResetFailed, LocalAuditResult.Failed, why, ct).ConfigureAwait(false);
            return Rejected(why);
        }

        // ⑥ 「끝」 줄 — 비번은 이미 바뀜(DB 감사 줄은 같은 트랜잭션). 못 쓰면 로그 오류 + 표시만.
        var tailOk = await TryAuditEndAsync(row.TenantId, row.UserId, opId, LocalAuditAction.ParentPasswordReset, LocalAuditResult.Succeeded, null, ct).ConfigureAwait(false);
        return new ParentResetResult(ParentResetStatus.Succeeded, SucceededMessage, Reasons.Ok) { AuditTailMissing = !tailOk };
    }

    private static ParentResetResult Rejected(string reason) =>
        new(ParentResetStatus.TokenRejected, TokenRejectedMessage, reason);

    private static string NewOpId() => Guid.NewGuid().ToString("N");

    private async Task TryAuditRejectAsync(string tenantId, string userId, string opId, string reason, CancellationToken ct)
    {
        try
        {
            await _audit.AppendAsync(new LocalAuditRecord(tenantId, userId, LocalAuditAction.ParentPasswordResetFailed, LocalAuditResult.Failed, opId, reason), ct).ConfigureAwait(false);
        }
        catch (LocalAuditWriteException ex)
        {
            // 거절 길은 바뀐 것이 없다 — 거절은 그대로 돌려주고 경고만(#15).
            _logger.LogWarning(ex, "[ParentReset] 거절 감사 줄을 못 씀 — 까닭 {Reason} · 거절 {Why}", ex.Reason, reason);
        }
    }

    private async Task<bool> TryAuditEndAsync(string tenantId, string userId, string opId, LocalAuditAction action, LocalAuditResult result, string? detail, CancellationToken ct)
    {
        try
        {
            await _audit.AppendAsync(new LocalAuditRecord(tenantId, userId, action, result, opId, detail), ct).ConfigureAwait(false);
            return true;
        }
        catch (LocalAuditWriteException ex)
        {
            // 「시작」 줄은 이미 남았다(같은 OpId) — 끝 줄만 빠짐을 오류로 남긴다(#15). 줄이 남았으면(LineWritten) 다시 쓰지 않는다.
            _logger.LogError(ex, "[ParentReset] 감사 끝 줄을 못 씀 op={OpId} · 동작 {Action} · 까닭 {Reason} · 줄 남음 {LineWritten}", opId, action, ex.Reason, ex.LineWritten);
            return false;
        }
    }
}

/// <summary>
/// 대표 새 비번 규칙 — 기존 계정 만들기 규칙(<c>UserService.ValidatePassword</c> · private)과 <b>같은 규칙</b>.
/// </summary>
/// <remarks>
/// UserService 파일을 고치지 않으려고(갈래 C 파일 범위) 같은 넷을 여기 둔다. 갈라지지 않게
/// 비DB 시험 <c>ParentResetGateTests.PR_RuleParity_SameAsUserServiceValidatePassword</c> 가 두 함수를 같은 입력으로 돌려 대 본다(다르면 빨강).
/// </remarks>
public static class ParentPasswordRule
{
    /// <summary>규칙 밖이면 쉬운 문구 · 맞으면 null.</summary>
    public static string? Check(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8) return "비밀번호는 최소 8자 이상이어야 합니다.";
        if (!password.Any(char.IsUpper)) return "비밀번호에 대문자가 1개 이상 포함되어야 합니다.";
        if (!password.Any(char.IsDigit)) return "비밀번호에 숫자가 1개 이상 포함되어야 합니다.";
        if (!password.Any(c => !char.IsLetterOrDigit(c))) return "비밀번호에 특수문자가 1개 이상 포함되어야 합니다.";
        return null;
    }
}
