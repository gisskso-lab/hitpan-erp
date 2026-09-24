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
    private readonly IMainPcSealService _seal;
    private readonly ILogger<MainPcProofService> _logger;

    private static readonly ConcurrentDictionary<string, Entry> _challenges = new(StringComparer.Ordinal);

    /// <summary>
    /// 종전 생성자 — <b>지우지 않는다</b>(#1). 기존 시험이 세운 봉인 대역이 그대로 돈다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 이 길로 들어오면 봉인/해제는 <b>종전과 똑같이</b> <see cref="ITpmKeyService"/> 로 간다
    /// (<see cref="LegacyTpmSealAdapter"/>). 판정도 종전 세 갈래 그대로다 —
    /// <c>LegacyFormat</c> 은 여기서 나오지 않는다.
    /// </para>
    /// <para>
    /// 🚨 <b>20260924작1 [4] C-2 (P1-2) — 운영 코드는 이 길로 들어올 수 없다.</b>
    /// 실물 <see cref="ITpmKeyService"/> 가 꽂히면 봉인이 <c>TpmKeyService.SealKey</c> 로 가고,
    /// 그것은 <b>세금계산서 마스터키를 덮는 그 경로</b>다(M-12 · 2026-09-24 확정).
    /// 종전엔 <c>Program.cs</c> 의 팩터리 한 줄만이 방벽이었다 — 누가 그 줄을 건드리면 조용히 재발한다.
    /// ⇒ <b>컴파일 오류</b>로 막는다. 🚫 <b>지우지는 않는다</b>(#1).
    /// 🔴 <c>#pragma warning disable</c> 로는 <b>열리지 않는다</b> — <c>error: true</c> 는 경고가 아니라
    /// 오류이고, pragma 는 오류를 못 끈다(20260924작1 [4] 실측 · 검증자 재확인).
    /// 종전 동작이 필요한 시험은 <c>ITpmKeyService</c> 를 그대로 통과시키는 대역을
    /// <see cref="IMainPcSealService"/> 로 만들어 <b>4인자 길</b>로 넣는다
    /// (보기: <c>MainPcProofRoundTripGateTests.PassThroughSeal</c>) — 그러면 판정이 한 글자도 안 바뀐다.
    /// </para>
    /// </remarks>
    [Obsolete(
        "🚨 쓰지 마라 — 이 생성자는 봉인을 ITpmKeyService 로 보낸다(M-12 재발 경로). " +
        "🔴 #pragma warning disable 로는 열리지 않는다(error 는 경고가 아니다 — 실측). " +
        "대신 4인자 생성자에 IMainPcSealService 를 넘겨라. 종전 동작이 필요한 시험은 " +
        "ITpmKeyService 를 그대로 통과시키는 대역을 IMainPcSealService 로 만들어 넣어라 " +
        "(보기: MainPcProofRoundTripGateTests.PassThroughSeal · 20260924작1 [4] C-2).",
        error: true)]
    public MainPcProofService(IDbConnection db, ITpmKeyService tpm, ILogger<MainPcProofService> logger)
        : this(db, tpm, new LegacyTpmSealAdapter(tpm), logger)
    {
    }

    /// <summary>
    /// 🔴 20260924작1 절A — <b>메인PC 전용 봉인 서비스</b>를 받는 생성자. 운영(DI)은 이 길로 온다.
    /// </summary>
    public MainPcProofService(
        IDbConnection db,
        ITpmKeyService tpm,
        IMainPcSealService seal,
        ILogger<MainPcProofService> logger)
    {
        _db = db;
        _tpm = tpm;
        _seal = seal;
        _logger = logger;
    }

    /// <summary>
    /// 🔵 종전 <see cref="ITpmKeyService"/> 를 <see cref="IMainPcSealService"/> 모양으로 감싼다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>봉투를 씌우지 않는다.</b> 종전 시험(G-14·G-15·G-19a)의 판정이 한 글자도 안 바뀌게
    /// 하려는 것이 이 대역의 전부다 — 새 갈래(<c>LegacyFormat</c>)를 만들지 않는다.
    /// </remarks>
    private sealed class LegacyTpmSealAdapter(ITpmKeyService tpm) : IMainPcSealService
    {
        public byte[] Seal(string tenantId, string deviceId, byte[] raw) => tpm.SealKey(raw);

        public byte[] Seal(string tenantId, string deviceId, byte[] raw, byte? currentGeneration) =>
            tpm.SealKey(raw);

        public MainPcUnsealResult Unseal(byte[] envelope, string tenantId, string deviceId)
        {
            try
            {
                return MainPcUnsealResult.Ok(tpm.UnsealKey(envelope));
            }
            catch (Exception)
            {
                // 종전 흐름 그대로 — 안 풀리면 「등록된 그 컴퓨터가 아니다」다.
                //   ⚠️ #15 — 삼키는 것이 아니다. 부른 쪽(JudgeRegistrationAsync)이 로그를 남긴다.
                return MainPcUnsealResult.NotThisPc();
            }
        }

        /// <summary>종전 값에는 세대 칸이 없다 — <b>늘 모른다</b>고 답한다.</summary>
        public bool TryReadGeneration(byte[]? envelope, out byte generation)
        {
            generation = 0;
            return false;
        }

        /// <summary>종전 경로는 이름 있는 키를 <b>이 트랙이 관리하지 않는다</b> — 아무것도 지우지 않는다.</summary>
        public bool TryDeleteGenerationKey(string tenantId, byte generation) => false;

        public bool IsPlatformKspUsable() => tpm.IsTpmAvailable();
    }

    /// <summary>
    /// 🔴 해제 결과 → 화면 판정. <b>순수 함수</b>로 떼어 둔다 — 게이트 G-25 가 DB 없이 이것을 직접 잰다.
    /// </summary>
    public static MainPcProofOutcome MapUnsealStatus(MainPcUnsealStatus status) => status switch
    {
        MainPcUnsealStatus.Ok => MainPcProofOutcome.MainPcConfirmed,
        // 🔵 옛 봉인값 — 컴퓨터가 바뀐 것이 아니다. [다시 인증] 으로 보낸다(20260924작1 절B).
        MainPcUnsealStatus.LegacyFormat => MainPcProofOutcome.ReRegisterRequired,
        _ => MainPcProofOutcome.DifferentPc,
    };

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
        //   🔴 20260924작1 절A(§9-2) — **device_id 를 같이 읽는다.** 봉투 안 이름표와 대조해야
        //     「풀렸다」가 아니라 「이 회사 이 기기다」로 물을 수 있다.
        //     ⚠️ #13 DESCRIBE 확인 — device_id varchar(36) PRI · tenant_id varchar(36) · is_main_pc tinyint(1).
        var row = await _db.QueryFirstOrDefaultAsync<MainPcRow>(new CommandDefinition(
            @"SELECT device_id AS DeviceId, mainpc_sealed_key AS SealedKey
                FROM tenant_devices
               WHERE tenant_id = @TenantId AND is_main_pc = 1
               LIMIT 1",
            new { TenantId = tenantId }, cancellationToken: ct));

        var sealedKey = row?.SealedKey;

        // 아직 아무도 메인PC 로 등록되지 않았다 → [메인PC 등록] 팝업으로 간다.
        //   ⚠️ 옛 방식으로 is_main_pc=1 만 서 있고 봉인키가 없는 줄도 여기로 온다.
        //     그 줄은 등록 절차를 거친 적이 없으므로 「미등록」이 맞다.
        if (sealedKey is null || sealedKey.Length == 0)
            return MainPcProofOutcome.NotRegisteredYet;

        try
        {
            // 🔴 20260924작1 절A — 봉투가 말한 갈래로만 푼다. 예외가 아니라 **상태**로 답한다.
            //   🔴 §9-2 — 이름표(회사·기기)를 함께 준다. 풀리기만 해서는 통과가 아니다.
            var result = _seal.Unseal(sealedKey, tenantId, row?.DeviceId ?? string.Empty);

            // 🔴 판정은 **한 자리**에서만 만든다(MapUnsealStatus). 여기서 다시 갈라 적으면
            //   게이트가 재는 표와 실제로 도는 코드가 조용히 달라진다.
            var outcome = MapUnsealStatus(result.Status);

            switch (result.Status)
            {
                case MainPcUnsealStatus.LegacyFormat:
                    // 🔵 봉투가 없다 = 옛 방식으로 봉인된 값이다. 아무도 못 푼다(선행검증 R-1).
                    //   컴퓨터가 바뀐 것이 아니므로 [변경] 이 아니라 [다시 인증] 으로 보낸다.
                    _logger.LogWarning(
                        "메인PC 봉인키가 옛 형식이다 — 재등록([다시 인증])으로 보낸다. tenant={TenantId}", tenantId);
                    return outcome;

                case MainPcUnsealStatus.NotThisPc:
                    _logger.LogWarning(
                        "메인PC 봉인키가 이 컴퓨터에서 풀리지 않는다 — 메인PC 변경 안내로 보낸다. tenant={TenantId}",
                        tenantId);
                    return outcome;
            }

            var key = result.Key;
            if (key is null || key.Length == 0)
            {
                _logger.LogWarning("메인PC 봉인키가 비어 있다 — 컴퓨터가 바뀐 것으로 본다. tenant={TenantId}", tenantId);
                return MainPcProofOutcome.DifferentPc;
            }

            CryptographicOperations.ZeroMemory(key);
            return outcome;
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
            // 🔴 Q-2 (PM 결재 2026-09-24) — 출입증을 **그 회사에** 붙여 발급한다.
            //   ⚠️ 기기·사용자 축은 여전히 열려 있다(별건 O-2). 여기서 닫히는 것은 **회사 하나**다.
            pass = IssuePass(tenantId);
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

    /// <summary>
    /// 출입증 → <b>만료 시각 + 그 출입증이 속한 회사</b>(20260924작1 · PM 결재 Q-2).
    /// </summary>
    private sealed record PassEntry(DateTimeOffset ExpiresAt, string TenantId);

    private static readonly ConcurrentDictionary<string, PassEntry> _passes = new(StringComparer.Ordinal);

    private static string IssuePass(string tenantId)
    {
        if (_passes.Count >= SweepThreshold)
        {
            foreach (var kv in _passes)
            {
                if (DateTimeOffset.UtcNow > kv.Value.ExpiresAt) _passes.TryRemove(kv.Key, out _);
            }
        }

        var pass = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _passes[pass] = new PassEntry(DateTimeOffset.UtcNow.Add(PassLifetime), tenantId ?? string.Empty);
        return pass;
    }

    /// <summary>
    /// 종전 1인자 판정 — <b>지우지 않는다</b>(#1). 회사를 모르는 자리에서만 쓴다.
    /// </summary>
    public bool IsPassValid(string? pass) => IsPassValid(pass, tenantId: null);

    /// <summary>
    /// 🔴 Q-2 — <b>회사까지 대조</b>한다(PM 결재 2026-09-24).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>"출입증을 묶었다" 고 말하지 않는다.</b> 여기서 닫히는 축은 <b>회사 하나</b>다.
    /// 같은 회사 안의 다른 기기·다른 사용자에게 건네진 출입증은 <b>여전히 통한다</b> —
    /// 그 축은 별건 <b>O-2</b> 로 열려 있다(거짓봉합 금지 · PM 재결재 §10).
    /// </remarks>
    public bool IsPassValid(string? pass, string? tenantId)
    {
        if (string.IsNullOrWhiteSpace(pass)) return false;
        if (!_passes.TryGetValue(pass, out var entry)) return false;

        if (DateTimeOffset.UtcNow > entry.ExpiresAt)
        {
            _passes.TryRemove(pass, out _);
            return false;
        }

        // 회사를 모르는 부름(종전 오버로드)은 종전과 똑같이 판정한다 — 무회귀(#1).
        if (tenantId is null) return true;

        if (!string.Equals(entry.TenantId, tenantId, StringComparison.Ordinal))
        {
            // #15 — 조용히 false 로 끝내지 않는다. 이건 흔한 일이 아니다.
            _logger.LogWarning("다른 회사의 출입증이다 — 막는다. tenant={TenantId}", tenantId);
            return false;
        }

        return true;
    }

    // ─────────────────────────────────────────────────────────
    // 🔵 메인PC 등록 / 변경
    // ─────────────────────────────────────────────────────────

    public async Task<bool> RegisterThisPcAsync(string tenantId, string deviceId, CancellationToken ct)
    {
        // ══ §9-3 세대 절차 1단계 — 지금 봉투의 세대를 먼저 안다 ══════════════════
        //   🔴 읽기가 실패하면 **아무것도 만들지 않고** 중단한다. 모르는 채로 새 키를 만들면
        //     어느 세대를 지워도 되는지 알 수 없다.
        byte? currentGeneration;
        string? previousMainDeviceId;
        try
        {
            // 🔴 20260924작1 [4] C-1 — **누가 지금 메인PC 줄인지**도 같이 읽는다.
            //   아래 ①에서 그 줄을 내리는데, ②가 0행이면 **되돌려 놓아야** 하기 때문이다.
            var existing = await _db.QueryFirstOrDefaultAsync<MainPcRow>(new CommandDefinition(
                @"SELECT device_id AS DeviceId, mainpc_sealed_key AS SealedKey
                    FROM tenant_devices
                   WHERE tenant_id = @TenantId AND is_main_pc = 1
                   LIMIT 1",
                new { TenantId = tenantId }, cancellationToken: ct));

            previousMainDeviceId = existing?.DeviceId;
            currentGeneration = _seal.TryReadGeneration(existing?.SealedKey, out var g) ? g : null;
        }
        catch (Exception ex)
        {
            // #15 — 조용히 넘어가지 않는다. 여기서 멈추는 것이 옛 봉투를 지키는 길이다.
            _logger.LogError(ex, "지금 봉인값을 읽지 못했다 — 등록을 중단한다. tenant={TenantId}", tenantId);
            return false;
        }

        // 새 비밀을 만들어 **이 컴퓨터에** 봉인한다. 원문은 어디에도 남기지 않는다.
        var raw = RandomNumberGenerator.GetBytes(32);
        byte[] sealedKey;
        try
        {
            // ══ §9-3 2~4단계 — 다음 세대 이름을 찾아 **덮어쓰기 없이** 만들고 봉인한다 ══
            //   🚨 OverwriteExistingKey 를 쓰지 않는다(M-12 의 뿌리). 옛 세대 키는 그대로 산다.
            //   🔴 이름표(회사·기기)가 평문에 들어간다 — 해제가 "풀렸다"로 통과하지 못하게.
            sealedKey = _seal.Seal(tenantId, deviceId, raw, currentGeneration);
        }
        catch (Exception ex)
        {
            // 봉인 자체가 안 되면 등록하지 않는다 — 풀 수 없는 키를 심으면
            // 다음 접속부터 「컴퓨터가 바뀌었다」가 계속 뜬다.
            //   🔴 PM 결재 O-3 — 여기서 CurrentUser 로 내려가 "성공" 을 만들지 않는다.
            //     화면이 사유를 말하고 **다시 시도할 수 있게** 둔다(막다른 방 금지).
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

        // 🔴 방금 만든 봉투의 세대 — 되돌릴 때도, 옛 것을 지울 때도 이 값이 기준이다.
        var newGeneration = _seal.TryReadGeneration(sealedKey, out var ng) ? ng : (byte?)null;

        if (rows == 0)
        {
            // ══ §9-3 5단계 — 🔴 **새 세대 키만** 지운다. 옛 세대는 손대지 않는다 ══
            //   이 한 줄이 *"등록 실패 1회가 옛 값을 죽인다"* 를 닫는다(PM 재결재 §10).
            if (newGeneration is { } toRollback)
            {
                var removed = _seal.TryDeleteGenerationKey(tenantId, toRollback);
                _logger.LogDebug(
                    "등록이 DB 에 안 걸렸다 — 새 세대 키를 되돌렸다. 세대={Generation} 지움={Removed}",
                    toRollback.ToString("X2"), removed);
            }

            // ══ 🔴 20260924작1 [4] C-1 (P1-1) — **DB 축도 되돌린다** ══════════════
            //   [무엇이 났나] 위 ①이 옛 메인PC 줄을 이미 내려놨다. 여기서 그대로 나가면
            //     그 회사에 is_main_pc=1 줄이 **0개**가 된다. 열쇠(봉투)는 살아 있어도
            //     **가리키는 줄이 사라져** 판정 SELECT 가 「미등록」으로 떨어진다 = 막다른 방.
            //   [왜 트랜잭션이 아닌가] 이 IDbConnection 은 요청 범위에서 공유되고 열림 상태를
            //     보장하지 않는다. 여기서 트랜잭션을 열면 **이 트랙 밖의 호출자까지** 영향을 받는다.
            //     ⇒ 우리가 내린 그 줄 하나만 **정확히 되돌린다**(보상).
            //   ⚠️ DB-120 uq_tenant_main_pc 와 충돌하지 않는다 — 지금 그 회사엔 1인 줄이 없다.
            //   ⚠️ 🔴 **단, 동시 등록이 끼어들면 다르다.** 우리가 내리고 되돌리는 사이에 다른 등록이
            //     같은 회사에 1인 줄을 세우면, 이 UPDATE 는 **1062(UNIQUE)** 로 터질 수 있다.
            //     그때는 아래 catch 로 떨어진다 — 자동 복구는 없고 LogError 한 줄이다(남은 누수).
            //     ⚠️ 막다른 방은 아니다: 줄이 0개면 판정이 NotRegisteredYet 이라 화면이 [등록] 을 띄우고,
            //       대표가 한 번 누르면 스스로 복구된다([4] 검증자 경로 추적 · 등급 P2).
            if (!string.IsNullOrEmpty(previousMainDeviceId)
                && !string.Equals(previousMainDeviceId, deviceId, StringComparison.Ordinal))
            {
                try
                {
                    await _db.ExecuteAsync(new CommandDefinition(
                        @"UPDATE tenant_devices
                             SET is_main_pc = 1
                           WHERE tenant_id = @TenantId AND device_id = @DeviceId",
                        new { TenantId = tenantId, DeviceId = previousMainDeviceId },
                        cancellationToken: ct));

                    _logger.LogWarning(
                        "메인PC 등록이 DB 에 안 걸려 옛 메인PC 줄을 되돌렸다. tenant={TenantId} device={DeviceId}",
                        tenantId, previousMainDeviceId);
                }
                catch (Exception ex)
                {
                    // #15 — 삼키지 않는다. 되돌리기까지 실패하면 **사람이 봐야 한다.**
                    _logger.LogError(ex,
                        "🚨 메인PC 줄 되돌리기에 실패했다 — 이 회사에 메인PC 줄이 없을 수 있다. tenant={TenantId} device={DeviceId}",
                        tenantId, previousMainDeviceId);
                }
            }

            _logger.LogWarning(
                "메인PC 등록 대상 기기를 찾지 못했다. tenant={TenantId} device={DeviceId}", tenantId, deviceId);
            return false;
        }

        // ══ §9-3 6단계 — DB 가 새 봉투를 받은 뒤에야 옛 세대를 정리한다(best-effort) ══
        //   ⚠️ 실패해도 무해하다 — 아무도 그 이름을 다시 쓰지 않는다(세대가 앞으로만 간다).
        if (currentGeneration is { } oldGeneration && oldGeneration != newGeneration)
        {
            var removed = _seal.TryDeleteGenerationKey(tenantId, oldGeneration);
            _logger.LogDebug("옛 세대 키를 정리했다. 세대={Generation} 지움={Removed}",
                oldGeneration.ToString("X2"), removed);
        }

        // ⚠️ TPM 가용 여부를 **같이 남긴다** — 나중에 "이 고객은 어느 갈래로 봉인됐나" 를
        //   로그만으로 가릴 수 있어야 한다(TPM 없는 컴퓨터는 L 갈래로 간다).
        _logger.LogInformation(
            "메인PC 등록 완료. tenant={TenantId} device={DeviceId} 세대={Generation} tpm={TpmAvailable}",
            tenantId, deviceId, newGeneration?.ToString("X2") ?? "-", _tpm.IsTpmAvailable());
        return true;
    }

    /// <summary>
    /// 🔵 메인PC 줄에서 <b>이름표 대조에 필요한 것만</b> 꺼내는 자리(20260924작1 절A · §9-2).
    /// </summary>
    private sealed class MainPcRow
    {
        public string? DeviceId { get; set; }
        public byte[]? SealedKey { get; set; }
    }

    public async Task<bool> IsDataEmptyAsync(string tenantId, CancellationToken ct)
    {
        // 🔵 사장님 결재 D-11 — *"팝업으로 복구하라고 안내"*
        //
        //   [왜 필요한가] 새 컴퓨터에 히트판을 깔면 DB 가 비어 있다. 그때 아무 말도 없으면
        //     고객은 **빈 화면을 보고 자료가 날아간 줄 안다.** 갈 곳을 알려 주지 않는 안내는
        //     흐름이 끊긴 것이다(헌법 #20).
        //
        //   [무엇으로 「비었다」를 판정하나] 거래처와 상품 **둘 다** 0 일 때만이다.
        //     ⚠️ 한쪽만 보면 안 된다 — 상품만 쓰는 회사도, 거래처만 먼저 넣는 회사도 있다.
        //     둘 다 비었다면 아직 아무것도 시작하지 않은 것이 거의 확실하다.
        //   ⚠️ 전표를 세지 않는 이유: 마스터만 옮겨 놓고 전표는 나중에 넣는 경우가 있다.
        //     그때 "자료가 없다" 고 말하면 틀린 안내가 된다.
        //
        //   ⚠️ 헌법 #16 — 연결 하나에 UNION ALL 로 한 번에 묻는다(병렬 조회 금지).
        // ⚠️ ValueTuple 로 받지 않는다 — Dapper 는 튜플을 **이름이 아니라 순서**로 채운다.
        //   컬럼 순서를 나중에 누가 바꾸면 값이 조용히 뒤바뀌고, 빌드는 멀쩡하다.
        //   이름으로 매핑되는 형태로 받는다.
        var counts = await _db.QueryFirstOrDefaultAsync<EmptyCheck>(new CommandDefinition(
            @"SELECT
                 (SELECT COUNT(*) FROM partners WHERE tenant_id = @TenantId) AS PartnerCount,
                 (SELECT COUNT(*) FROM items    WHERE tenant_id = @TenantId) AS ItemCount",
            new { TenantId = tenantId }, cancellationToken: ct));

        return counts is not null && counts.PartnerCount == 0 && counts.ItemCount == 0;
    }

    private sealed class EmptyCheck
    {
        public long PartnerCount { get; set; }
        public long ItemCount { get; set; }
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
