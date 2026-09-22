using System.Collections.Concurrent;
using System.Data;
using System.Security.Cryptography;
using Dapper;
using HitPan.Application.Interfaces;
using HitPan.Application.Services.Security;
using Microsoft.Extensions.Logging;

// ⚠️ 폴더는 Infrastructure/Services 인데 네임스페이스는 Application.Services 다.
//   같은 폴더의 TenantDeviceService 가 그렇게 돼 있어 그 관례를 따른다 — 혼자 다르면
//   Program.cs 의 using 이 하나 더 필요해지고, 다음 사람이 어느 쪽이 맞는지 헷갈린다.
namespace HitPan.Application.Services;

/// <summary>
/// 🔴 메인PC 「왕복 증명」 구현 — 설계 <c>docs/설계/erp/20260922_설계_메인PC물리식별_왕복증명.md</c>
/// </summary>
/// <remarks>
/// <para>
/// 사장님 오더 2026-09-22 (전결). 작업지시서 <c>20260922작2</c> 절A.
/// </para>
///
/// <para>
/// 🔴 <b>표는 메모리에만 둔다.</b> ②(로컬 직결)를 받는 것도 ③(도메인)에 답하는 것도
/// <b>같은 프로세스</b>이기 때문이다. DB 를 쓸 이유가 없고, 쓰면 오히려 느려지고 지저분해진다.
/// API 가 재시작되면 표가 사라지지만 <b>다시 왕복하면 그만</b>이다 — 고객은 그 일을 모른다.
/// </para>
///
/// <para>
/// ⚠️ <b>봉인키는 DB 에 둔다</b>(표와 다르다 · DB-126). 자료와 함께 이사해야
/// <i>"컴퓨터가 바뀌었다"</i> 를 알 수 있기 때문이다 — 새 PC 에서 백업을 복원하면 옛 봉인키가
/// 따라 들어오고, 그것이 <b>안 풀리는 것</b>이 곧 신호가 된다.
/// </para>
/// </remarks>
public sealed class MainPcProofService : IMainPcProofService
{
    /// <summary>표가 살아 있는 시간. 사장님 결재 D-2 — 60초.</summary>
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromSeconds(60);

    /// <summary>표가 너무 쌓이지 않게 하는 상한. 이 수를 넘으면 만료분을 먼저 털어낸다.</summary>
    private const int SweepThreshold = 256;

    private readonly IDbConnection _db;
    private readonly ITpmKeyService _tpm;
    private readonly ILogger<MainPcProofService> _logger;

    private static readonly ConcurrentDictionary<string, Entry> _challenges = new(StringComparer.Ordinal);

    public MainPcProofService(IDbConnection db, ITpmKeyService tpm, ILogger<MainPcProofService> logger)
    {
        _db = db;
        _tpm = tpm;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────
    // ① 표 발급 (도메인 경유)
    // ─────────────────────────────────────────────────────────

    public string IssueChallenge(string tenantId, string sessionKey)
    {
        if (_challenges.Count >= SweepThreshold) Sweep();

        // 표는 추측할 수 없어야 한다 — 맞히면 왕복을 건너뛸 수 있기 때문이다.
        var challenge = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        _challenges[challenge] = new Entry
        {
            TenantId  = tenantId,
            SessionKey = sessionKey,
            ExpiresAt = DateTimeOffset.UtcNow.Add(ChallengeLifetime),
            Outcome   = MainPcProofOutcome.NotProven,
        };

        return challenge;
    }

    // ─────────────────────────────────────────────────────────
    // ② 로컬 직결 요청 — 이 컴퓨터 안에 본체가 있다는 뜻
    // ─────────────────────────────────────────────────────────

    public async Task<MainPcProofOutcome> ConfirmLocalAsync(string challenge, CancellationToken ct)
    {
        // 표가 없거나 만료됐다.
        //   🔴 회사 식별자는 **표에서 꺼낸다.** 이 길은 로그인 토큰 없이 지나가므로
        //     밖에서 받은 값으로 회사를 정하면 남의 회사 판정을 건드릴 수 있다.
        if (string.IsNullOrWhiteSpace(challenge)
            || !_challenges.TryGetValue(challenge, out var entry)
            || entry.IsExpired)
        {
            return MainPcProofOutcome.NotProven;
        }

        var tenantId = entry.TenantId;

        // 🔴 여기 도달했다 = 이 요청이 「진짜 로컬」이라는 것을 부르는 쪽이 이미 확인했다.
        //   (DeviceController 가 MainPcOnlyAttribute.IsMainPc 로 먼저 거른다 —
        //    그 확인 없이 여기 오면 터널 접속도 로컬로 인정되어 설계 전체가 무너진다)
        //   ⇒ 남은 질문은 하나. 「이 컴퓨터가 **등록된 그** 컴퓨터인가」
        var outcome = await JudgeRegistrationAsync(tenantId, ct);

        entry.Outcome = outcome;
        entry.ConfirmedAt = DateTimeOffset.UtcNow;
        return outcome;
    }

    /// <summary>
    /// 봉인된 키가 <b>이 PC 에서 풀리는가</b> 로 등록 상태를 가른다.
    /// </summary>
    private async Task<MainPcProofOutcome> JudgeRegistrationAsync(string tenantId, CancellationToken ct)
    {
        // 메인PC 줄은 회사당 1개다 — DB 가 보장한다(DB-120 uq_tenant_main_pc).
        var sealedKey = await _db.QueryFirstOrDefaultAsync<byte[]?>(new CommandDefinition(
            @"SELECT mainpc_sealed_key
                FROM tenant_devices
               WHERE tenant_id = @TenantId AND is_main_pc = 1
               LIMIT 1",
            new { TenantId = tenantId }, cancellationToken: ct));

        // 아직 아무도 메인PC 로 등록되지 않았다 → [메인PC 등록] 팝업으로 간다.
        //   ⚠️ 옛 방식으로 is_main_pc=1 만 서 있고 봉인키가 없는 줄도 여기로 온다.
        //     그 줄은 등록 절차를 거친 적이 없으므로 「미등록」이 맞다.
        if (sealedKey is null || sealedKey.Length == 0)
            return MainPcProofOutcome.NotRegisteredYet;

        try
        {
            var key = _tpm.UnsealKey(sealedKey);
            if (key is null || key.Length == 0)
            {
                _logger.LogWarning("메인PC 봉인키가 비어 있다 — 컴퓨터가 바뀐 것으로 본다. tenant={TenantId}", tenantId);
                return MainPcProofOutcome.DifferentPc;
            }

            CryptographicOperations.ZeroMemory(key);
            return MainPcProofOutcome.MainPcConfirmed;
        }
        catch (Exception ex)
        {
            // 🔴 이것이 「컴퓨터가 바뀌었다」 신호다 — 오류가 아니라 **정상 경로**다.
            //   봉인은 그 PC 에서만 풀린다. 안 풀린다 = 등록된 그 컴퓨터가 아니다.
            //   (새로 산 PC 에 백업을 복원한 경우 · 마더보드 교체 · BIOS 리셋)
            //   ⚠️ 헌법 #15 — 조용히 삼키지 않는다. 다만 Warning 이다(고객 장애가 아니다).
            _logger.LogWarning(ex,
                "메인PC 봉인키가 이 컴퓨터에서 풀리지 않는다 — 메인PC 변경 안내로 보낸다. tenant={TenantId}", tenantId);
            return MainPcProofOutcome.DifferentPc;
        }
    }

    // ─────────────────────────────────────────────────────────
    // ③ 결과 확인 (도메인 경유) — 쓰고 나면 버린다
    // ─────────────────────────────────────────────────────────

    public MainPcProofOutcome Consume(string tenantId, string sessionKey, string challenge, out string? pass)
    {
        pass = null;

        if (string.IsNullOrWhiteSpace(challenge) || !_challenges.TryRemove(challenge, out var entry))
            return MainPcProofOutcome.NotProven;

        // 표를 주운 사람이 쓰지 못하게 — 회사와 세션이 둘 다 맞아야 한다.
        if (entry.IsExpired
            || !string.Equals(entry.TenantId, tenantId, StringComparison.Ordinal)
            || !string.Equals(entry.SessionKey, sessionKey, StringComparison.Ordinal))
        {
            return MainPcProofOutcome.NotProven;
        }

        // 🟢 통과한 경우에만 출입증을 준다.
        //   ⚠️ 등록 전(NotRegisteredYet)·컴퓨터가 바뀐 경우(DifferentPc)에는 주지 않는다 —
        //     아직 이 회사의 메인PC 가 아니기 때문이다. 그 둘은 팝업으로 간다.
        if (entry.Outcome == MainPcProofOutcome.MainPcConfirmed)
        {
            pass = IssuePass();
        }

        return entry.Outcome;
    }

    // ─────────────────────────────────────────────────────────
    // 🔴 출입증 — 자료관리 관문이 매 요청 묻는다
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 출입증이 사는 시간. <b>일부러 짧다.</b>
    /// </summary>
    /// <remarks>
    /// 이 값을 길게 잡으면, 메인PC 브라우저에서 이 비밀을 빼낸 사람이 그만큼 오래 쓸 수 있다.
    /// 짧게 잡으면 만료될 뿐이고, 화면이 <b>조용히 왕복을 다시 돌아</b> 새로 받는다 —
    /// 메인PC 면 저절로 갱신되고, 아니면 그때 닫힌다. <b>고객은 이 일을 모른다.</b>
    /// </remarks>
    private static readonly TimeSpan PassLifetime = TimeSpan.FromMinutes(30);

    private static readonly ConcurrentDictionary<string, DateTimeOffset> _passes = new(StringComparer.Ordinal);

    private static string IssuePass()
    {
        if (_passes.Count >= SweepThreshold)
        {
            foreach (var kv in _passes)
            {
                if (DateTimeOffset.UtcNow > kv.Value) _passes.TryRemove(kv.Key, out _);
            }
        }

        var pass = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _passes[pass] = DateTimeOffset.UtcNow.Add(PassLifetime);
        return pass;
    }

    public bool IsPassValid(string? pass)
    {
        if (string.IsNullOrWhiteSpace(pass)) return false;
        if (!_passes.TryGetValue(pass, out var expiresAt)) return false;

        if (DateTimeOffset.UtcNow > expiresAt)
        {
            _passes.TryRemove(pass, out _);
            return false;
        }

        return true;
    }

    // ─────────────────────────────────────────────────────────
    // 🔵 메인PC 등록 / 변경
    // ─────────────────────────────────────────────────────────

    public async Task<bool> RegisterThisPcAsync(string tenantId, string deviceId, CancellationToken ct)
    {
        // 새 비밀을 만들어 **이 컴퓨터에** 봉인한다. 원문은 어디에도 남기지 않는다.
        var raw = RandomNumberGenerator.GetBytes(32);
        byte[] sealedKey;
        try
        {
            sealedKey = _tpm.SealKey(raw);
        }
        catch (Exception ex)
        {
            // 봉인 자체가 안 되면 등록하지 않는다 — 풀 수 없는 키를 심으면
            // 다음 접속부터 「컴퓨터가 바뀌었다」가 계속 뜬다.
            _logger.LogError(ex, "메인PC 인증키 봉인 실패 — 등록을 중단한다. tenant={TenantId}", tenantId);
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
        }

        // 🔴 옛 메인PC 줄을 **먼저** 내려놓는다.
        //   DB-120 의 uq_tenant_main_pc 는 회사당 is_main_pc=1 을 1줄만 허용한다.
        //   순서를 바꾸면 UNIQUE 충돌(1062)로 등록이 통째로 실패한다.
        //   ⚠️ 같은 줄이면 이 UPDATE 가 0행이거나 자기 자신을 내렸다가 아래에서 다시 올린다 — 둘 다 무해하다.
        await _db.ExecuteAsync(new CommandDefinition(
            @"UPDATE tenant_devices
                 SET is_main_pc = 0
               WHERE tenant_id = @TenantId AND is_main_pc = 1 AND device_id <> @DeviceId",
            new { TenantId = tenantId, DeviceId = deviceId }, cancellationToken: ct));

        var rows = await _db.ExecuteAsync(new CommandDefinition(
            @"UPDATE tenant_devices
                 SET is_main_pc            = 1,
                     mainpc_sealed_key     = @SealedKey,
                     mainpc_key_issued_at  = UTC_TIMESTAMP(6)
               WHERE tenant_id = @TenantId AND device_id = @DeviceId",
            new { TenantId = tenantId, DeviceId = deviceId, SealedKey = sealedKey }, cancellationToken: ct));

        if (rows == 0)
        {
            _logger.LogWarning(
                "메인PC 등록 대상 기기를 찾지 못했다. tenant={TenantId} device={DeviceId}", tenantId, deviceId);
            return false;
        }

        _logger.LogInformation("메인PC 등록 완료. tenant={TenantId} device={DeviceId}", tenantId, deviceId);
        return true;
    }

    // ─────────────────────────────────────────────────────────

    /// <summary>만료된 표를 털어낸다. 표는 짧게 살고, 수가 적어 비용이 없다.</summary>
    private static void Sweep()
    {
        foreach (var kv in _challenges)
        {
            if (kv.Value.IsExpired) _challenges.TryRemove(kv.Key, out _);
        }
    }

    private sealed class Entry
    {
        public required string TenantId { get; init; }
        public required string SessionKey { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
        public MainPcProofOutcome Outcome { get; set; }
        public DateTimeOffset? ConfirmedAt { get; set; }

        public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
    }
}
