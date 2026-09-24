using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services.Security;

/// <summary>
/// 🔴 <b>메인PC 전용 봉인</b> — 20260924작1 절A(<b>§9 개정본</b> · PM 재결재 §10) ·
/// 설계 <c>docs/설계/erp/20260924_설계문서_메인PC봉인_공급자불일치_출입증전달.md</c> <c>§개정1</c>
/// </summary>
/// <remarks>
/// <para>
/// [무엇이 났나] 종전에는 <see cref="ITpmKeyService"/> 하나로 봉인도 하고 해제도 했다.
/// 그런데 봉인은 <b>TPM 금고</b>(<c>MicrosoftPlatformCryptoProvider</c>)에 만들고,
/// 해제는 이름만 주는 1인자 오버로드 — 즉 <b>기본 소프트웨어 금고</b>를 열었다.
/// CNG 는 <b>공급자마다 키 공간이 다르다.</b> 같은 컴퓨터인데 키를 못 찾는다.
/// ⇒ 메인PC 출입증이 <b>발급된 적이 없다.</b> (선행검증 R-1 · PM 동작 실측)
/// </para>
///
/// <para>
/// [어떻게 푸나] 봉인한 값 자신이 <b>어느 금고로 · 몇 세대로</b> 봉인됐는지 말하게 한다(봉투).
/// 해제는 그 말대로 <b>공급자를 명시해</b> 딱 한 번 연다. <b>폴백하지 않는다</b> —
/// 폴백은 "풀렸다" 와 "다른 컴퓨터다" 를 섞어 버린다.
/// </para>
///
/// <para>
/// 🚨 <b>M-12 의 뿌리를 되풀이하지 않는다</b>(작지서 §9-1 · PM 재결재 §10).
/// 이 파일에는 <c>OverwriteExistingKey</c> 가 <b>한 번도 나오지 않는다.</b>
/// 덮어쓰기는 *"만들면 성공"* 처럼 보이지만 <b>같은 이름을 쓰던 값의 봉투를 그 순간 죽인다.</b>
/// 대신 <b>회사별 이름 + 세대</b>로 가른다 — <c>HitPan.MainPc.SealKey.{T16}.{GG}</c>.
/// </para>
///
/// <para>
/// 🚨 <b>세금계산서 키와 완전히 분리한다</b>(사장님 결재 C-1 · 2026-09-24).
/// 이 파일은 <c>HitPan.TaxInvoice.MasterKey</c> 를 <b>참조하지 않는다.</b>
/// <see cref="TpmKeyService"/> 는 <b>한 글자도 고치지 않았다</b>(#1 · 트랙 밖 #33).
/// </para>
///
/// <para>
/// 🔴 <b>PM 결재 O-3 (2026-09-24)</b> — 봉인은 <c>P</c>(TPM) 또는 <c>L</c>(DPAPI LocalMachine) <b>만</b> 쓴다.
/// <c>U</c>(DPAPI CurrentUser)로는 <b>봉인하지 않는다.</b> 그 순간엔 성공으로 보이고
/// <b>프로필이 바뀌는 나중에 조용히 못 푼다</b> — 지금 사고와 같은 모양이다. API 는 SYSTEM 으로 돈다.
/// 해제 쪽은 <b>옛 값 호환</b>을 위해 <c>U</c> 를 <b>읽을 수만</b> 있게 남긴다(#1).
/// </para>
/// </remarks>
public interface IMainPcSealService
{
    /// <summary>
    /// 이 컴퓨터에 봉인한다. 갈래를 <b>재서</b> 고르고, <b>세대</b>를 정하고,
    /// <b>이름표</b>(회사·기기)를 평문에 넣어 봉투로 돌려준다.
    /// </summary>
    /// <exception cref="MainPcSealFailedException">
    /// 🔴 <c>P</c>·<c>L</c> 둘 다 실패했다(또는 빈 세대가 없다). <b>등록을 거절한다</b>(PM O-3) —
    /// 풀 수 없는 키를 심지 않고, 남의 봉투도 죽이지 않는다.
    /// </exception>
    byte[] Seal(string tenantId, string deviceId, byte[] raw);

    /// <summary>
    /// 🔴 세대 절차용 오버로드(작지서 §9-3 2단계) — <b>지금 쓰고 있는 세대</b>를 알려 주면
    /// 그 <b>다음 세대</b>부터 빈 이름을 찾는다. 지금 세대는 <b>건드리지 않는다.</b>
    /// </summary>
    /// <param name="currentGeneration">
    /// 지금 DB 에 있는 봉투의 세대. 없거나(첫 등록) 옛 값이면 <c>null</c>.
    /// </param>
    byte[] Seal(string tenantId, string deviceId, byte[] raw, byte? currentGeneration);

    /// <summary>
    /// 봉투를 읽어 <b>그 이름 그 갈래로만</b> 해제하고, <b>꺼낸 이름표를 인자와 대조</b>한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>예외를 밖으로 던지지 않는다.</b> 상태로 답한다 —
    /// 종전에는 예외가 <c>DifferentPc</c> 로 위장되어, <b>키를 못 찾는 버그</b>와
    /// <b>진짜 컴퓨터 교체</b>가 같은 화면으로 갔다.
    /// </para>
    /// <para>
    /// 🔴 <b>"풀렸다" 만으로 통과시키지 않는다</b>(설계 §개정1 · S-1 대안).
    /// <c>L</c> 갈래는 <b>같은 컴퓨터면 누구의 봉투든 푼다</b> — 그때 이름표가 아니면 가릴 방법이 없다.
    /// </para>
    /// </remarks>
    MainPcUnsealResult Unseal(byte[] envelope, string tenantId, string deviceId);

    /// <summary>봉투에서 <b>세대만</b> 꺼낸다(금고를 열지 않는다). 봉투가 아니면 <c>false</c>.</summary>
    bool TryReadGeneration(byte[]? envelope, out byte generation);

    /// <summary>
    /// 🔴 그 회사·그 세대의 <b>이름 있는 키</b>(=<c>P</c> 갈래)를 지운다 — 되돌리기(§9-3 5단계)와
    /// 옛 세대 뒤처리(§9-3 6단계)용.
    /// </summary>
    /// <remarks>
    /// <para>이름이 없는 <c>L</c> 갈래에는 지울 것이 없다 ⇒ <b>조용히 아무 일도 안 한다.</b></para>
    /// <para>🔴 실패해도 던지지 않는다(뒤처리다). 다만 #15 — <b>흔적은 남긴다.</b></para>
    /// </remarks>
    /// <returns>실제로 지웠으면 <c>true</c>.</returns>
    bool TryDeleteGenerationKey(string tenantId, byte generation);

    /// <summary>
    /// TPM 금고에 키를 <b>실제로 만들어 보고</b> 되는지 답한다(만들었으면 즉시 지운다).
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>글자로 묻지 않는다.</b> <c>CngProvider.MicrosoftPlatformCryptoProvider</c> 개체가
    /// 만들어지는 것은 TPM 이 있다는 뜻이 아니다(M-25). <b>만들어 봐야</b> 안다.
    /// ⚠️ 비싸다(수백 ms~수 초). 매 요청 부르지 않는다 — 게이트와 진단용이다.
    /// </remarks>
    bool IsPlatformKspUsable();
}

/// <summary>해제 결과 — <b>세 갈래</b>. 예외가 아니라 이 값으로 답한다.</summary>
public enum MainPcUnsealStatus
{
    /// <summary>🟢 풀렸다. <b>그리고 이름표가 맞다.</b> 이 컴퓨터가 이 회사·이 기기를 위해 봉인한 값이다.</summary>
    Ok = 0,

    /// <summary>
    /// 🔵 <b>봉투가 없다</b> — 이 서비스가 만든 값이 아니다(옛 방식으로 봉인된 값).
    /// 🔴 <b>해제를 시도조차 하지 않는다.</b> 금고를 여는 일이 0회다.
    /// </summary>
    LegacyFormat = 1,

    /// <summary>
    /// 🔵 봉투가 말한 갈래로 열었는데 안 풀렸다 — 또는 풀렸는데 <b>이름표가 다르다.</b>
    /// 둘 다 <b>등록된 그 컴퓨터(그 회사)가 아니다</b>로 답한다.
    /// </summary>
    NotThisPc = 2,
}

/// <summary>해제 결과와 (풀렸을 때만) 원문.</summary>
public sealed class MainPcUnsealResult
{
    private MainPcUnsealResult(MainPcUnsealStatus status, byte[]? key)
    {
        Status = status;
        Key = key;
    }

    public MainPcUnsealStatus Status { get; }

    /// <summary>🟢 <see cref="MainPcUnsealStatus.Ok"/> 일 때만 채워진다. 쓰고 나면 지운다.</summary>
    public byte[]? Key { get; }

    public static MainPcUnsealResult Ok(byte[] key) => new(MainPcUnsealStatus.Ok, key);
    public static MainPcUnsealResult Legacy() => new(MainPcUnsealStatus.LegacyFormat, null);
    public static MainPcUnsealResult NotThisPc() => new(MainPcUnsealStatus.NotThisPc, null);
}

/// <summary>🔴 봉인이 안 됐다 — 등록을 거절해야 한다(PM O-3).</summary>
public sealed class MainPcSealFailedException : Exception
{
    public MainPcSealFailedException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <inheritdoc cref="IMainPcSealService"/>
public sealed class MainPcSealService : IMainPcSealService
{
    /// <summary>
    /// 🔴 메인PC 전용 키 이름의 <b>접두사</b> (사장님 결재 C-1 · 작지서 §9-1).
    /// 실제 이름은 <c>{접두사}.{T16}.{GG}</c> 다.
    /// 🚨 세금계산서 키(<c>HitPan.TaxInvoice.MasterKey</c>)와 <b>겹치지 않는다.</b>
    /// </summary>
    public const string DefaultKeyPrefix = "HitPan.MainPc.SealKey";

    /// <summary>
    /// 봉투 머리 — 이 4바이트가 없으면 우리가 만든 값이 아니다.
    /// 🔴 <c>HPM2</c> 다(§9-1). 세대 칸이 없던 <c>HPM1</c> 은 <b>출하된 적이 없고</b>,
    /// 만나면 <c>LegacyFormat</c> 으로 간다(= [다시 인증]).
    /// </summary>
    internal static readonly byte[] Magic = { (byte)'H', (byte)'P', (byte)'M', (byte)'2' };

    /// <summary>봉투 5번째 바이트 — <b>어느 금고로 봉인했는지</b>.</summary>
    public const byte BranchPlatform = (byte)'P';
    public const byte BranchLocalMachine = (byte)'L';

    /// <summary>🔴 <b>봉인에는 쓰지 않는다</b>(PM O-3). 옛 값을 <b>읽을 때만</b> 쓴다.</summary>
    public const byte BranchCurrentUser = (byte)'U';

    /// <summary>봉투 머리(4) + 갈래(1) + 세대(1) = 6바이트.</summary>
    internal const int HeaderLength = 6;

    /// <summary>🔴 빈 이름을 찾아 보는 횟수(§9-3 2단계). 세 번 다 차 있으면 <b>거절</b>한다.</summary>
    internal const int GenerationProbeLimit = 3;

    /// <summary>RSA-2048 + OAEP-SHA256 이 한 번에 싸는 최대 길이.</summary>
    private const int PlatformMaxPlaintext = 190;

    private readonly ILogger<MainPcSealService> _logger;
    private readonly string _keyPrefix;
    private readonly bool _allowPlatform;

    /// <param name="keyPrefix">
    /// 🔴 <b>주입할 수 있게 둔다</b>(기본값 <see cref="DefaultKeyPrefix"/>).
    /// 게이트가 <b>실물 키를 안 덮고</b> 진짜 왕복을 도는 유일한 통로다(작지서 §5-0 · §9-6).
    /// </param>
    /// <param name="allowPlatform">
    /// <c>false</c> 면 TPM 갈래를 건너뛰고 <c>L</c> 로 봉인한다 — TPM 없는 컴퓨터와 같은 길이다.
    /// 게이트 G-21a 가 <b>어느 Windows 에서나</b> 진짜 왕복을 돌 수 있게 한다.
    /// </param>
    public MainPcSealService(
        ILogger<MainPcSealService> logger,
        string? keyPrefix = null,
        bool allowPlatform = true)
    {
        _logger = logger;
        _keyPrefix = string.IsNullOrWhiteSpace(keyPrefix) ? DefaultKeyPrefix : keyPrefix;
        _allowPlatform = allowPlatform;
    }

    // ─────────────────────────────────────────────────────────
    // 이름 — 회사(T16) + 세대(GG)
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 회사 식별자 <b>T16</b> — <c>SHA-256(소문자·앞뒤공백제거한 tenant_id)</c> <b>앞 8바이트</b>.
    /// </summary>
    /// <remarks>
    /// 왜 해시인가 — <c>tenant_id</c> 를 그대로 키 이름에 쓰면 <b>레지스트리에 회사 식별자가 남는다.</b>
    /// 그리고 이름에 못 쓰는 글자가 섞일 수 있다.
    /// </remarks>
    public static byte[] TenantTagBytes(string? tenantId)
    {
        var normalized = (tenantId ?? string.Empty).Trim().ToLowerInvariant();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return digest[..8];
    }

    /// <summary>T16 의 16자리 <b>소문자</b> 16진 표기.</summary>
    internal static string TenantTag(string? tenantId) =>
        Convert.ToHexString(TenantTagBytes(tenantId)).ToLowerInvariant();

    /// <summary>그 회사 그 세대의 키 이름 — <c>{접두사}.{T16}.{GG}</c>.</summary>
    public string KeyNameFor(string? tenantId, byte generation) =>
        $"{_keyPrefix}.{TenantTag(tenantId)}.{generation:X2}";

    // ─────────────────────────────────────────────────────────
    // 봉인
    // ─────────────────────────────────────────────────────────

    public byte[] Seal(string tenantId, string deviceId, byte[] raw) =>
        Seal(tenantId, deviceId, raw, currentGeneration: null);

    public byte[] Seal(string tenantId, string deviceId, byte[] raw, byte? currentGeneration)
    {
        ArgumentNullException.ThrowIfNull(raw);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // 🔴 평문으로 돌려주지 않는다. 풀 수 없는(= 지켜지지 않는) 키를 심느니 거절한다.
            throw new MainPcSealFailedException(
                "이 운영체제에서는 자료보관 컴퓨터 봉인을 만들 수 없습니다 (Windows 전용).");
        }

        // 🔴 이름표를 평문에 넣는다 — 해제 때 "풀렸다" 가 아니라 "이 회사 이 기기다" 로 묻기 위해서다.
        var labeled = PackLabel(tenantId, deviceId, raw);

        // 🔴 세대는 **다음 칸부터** 본다. 지금 쓰는 세대는 그대로 살아 있어야 한다(§9-3).
        var firstCandidate = (byte)(((currentGeneration ?? 0xFF) + 1) & 0xFF);

        Exception? platformError = null;

        try
        {
            if (_allowPlatform)
            {
                try
                {
                    return SealWithPlatform(tenantId, labeled, firstCandidate);
                }
                catch (MainPcSealFailedException)
                {
                    // 🔴 「빈 세대가 없다」는 **거절**이다 — L 로 내려가 조용히 다른 길을 타지 않는다.
                    throw;
                }
                catch (Exception ex)
                {
                    // #15 — 삼키지 않는다. TPM 이 없거나 막힌 컴퓨터는 아래 L 갈래가 받는다.
                    platformError = ex;
                    _logger.LogWarning(ex, "TPM 금고 봉인 실패 — DPAPI LocalMachine 갈래로 내려간다.");
                }
            }

            try
            {
                // 🔵 L 갈래는 **이름 있는 키를 만들지 않는다**(§9-1) ⇒ 이름 충돌이 없다.
                //   회사는 optionalEntropy(T16)로 가른다. 실물은 종전에 null 이었다(TpmKeyService.cs:93).
                var body = ProtectedData.Protect(
                    labeled,
                    optionalEntropy: TenantTagBytes(tenantId),
                    scope: DataProtectionScope.LocalMachine);

                _logger.LogInformation(
                    "자료보관 컴퓨터 봉인 성공 — 갈래=L(LocalMachine) 세대={Generation} 길이={Length}",
                    firstCandidate.ToString("X2"), body.Length);

                return Wrap(BranchLocalMachine, firstCandidate, body);
            }
            catch (Exception ex)
            {
                // 🔴 PM 결재 O-3 — 여기서 U(CurrentUser)로 내려가 "성공" 을 만들지 않는다.
                //   그 봉인은 지금 사고와 같은 모양이다 — 지금은 성공으로 보이고
                //   프로필이 바뀌는 나중에 조용히 못 푼다. API 는 SYSTEM 으로 돈다.
                //   ⇒ 등록을 거절하고, 화면이 사유를 말하고, 다시 시도할 수 있게 둔다.
                _logger.LogError(ex, "자료보관 컴퓨터 봉인이 모든 갈래에서 실패했다 — 등록을 거절한다.");
                throw new MainPcSealFailedException(
                    "자료보관 컴퓨터 봉인을 만들지 못했습니다 (P·L 갈래 모두 실패).",
                    platformError ?? ex);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(labeled);
        }
    }

    [SupportedOSPlatform("windows")]
    private byte[] SealWithPlatform(string tenantId, byte[] labeled, byte firstCandidate)
    {
        if (labeled.Length > PlatformMaxPlaintext)
        {
            throw new MainPcSealFailedException(
                $"봉인할 내용이 너무 깁니다 ({labeled.Length}바이트).");
        }

        // 🚨 절대 덮어쓰지 않는다(§9-1 · M-12). **빈 이름**을 찾아 거기에만 만든다.
        //   세 칸이 다 차 있으면 거절이다 — 남의 봉투를 죽이느니 안 하는 쪽을 고른다.
        for (var step = 0; step < GenerationProbeLimit; step++)
        {
            var generation = (byte)((firstCandidate + step) & 0xFF);
            var keyName = KeyNameFor(tenantId, generation);

            if (CngKey.Exists(keyName, CngProvider.MicrosoftPlatformCryptoProvider))
            {
                _logger.LogWarning(
                    "TPM 금고에 이 세대 이름이 이미 있다 — 다음 세대로 넘어간다. 세대={Generation}",
                    generation.ToString("X2"));
                continue;
            }

            CngKey? created = null;
            try
            {
                created = CngKey.Create(CngAlgorithm.Rsa, keyName, new CngKeyCreationParameters
                {
                    Provider = CngProvider.MicrosoftPlatformCryptoProvider,
                    // 🚨 여기에 덮어쓰기 옵션을 쓰지 않는다. 그 줄이 M-12 의 뿌리였다.
                    KeyCreationOptions = CngKeyCreationOptions.None,
                    ExportPolicy = CngExportPolicies.None,
                });

                using var rsa = new RSACng(created);
                var body = rsa.Encrypt(labeled, RSAEncryptionPadding.OaepSHA256);

                _logger.LogInformation(
                    "자료보관 컴퓨터 봉인 성공 — 갈래=P(TPM) 세대={Generation} 길이={Length}",
                    generation.ToString("X2"), body.Length);

                return Wrap(BranchPlatform, generation, body);
            }
            catch (Exception)
            {
                // 🔴 봉인이 깨지면 **방금 만든 새 키만** 지운다(§9-3 4단계). 옛 세대는 손대지 않는다.
                if (created is not null)
                {
                    DeleteQuietly(created, keyName);
                    created = null;
                }

                throw;
            }
            finally
            {
                created?.Dispose();
            }
        }

        throw new MainPcSealFailedException(
            "자료보관 컴퓨터 인증 자리가 모두 차 있습니다. 기다려도 저절로 풀리지 않으니 히트판에 문의해 주세요.");
    }

    private static byte[] Wrap(byte branch, byte generation, byte[] body)
    {
        var envelope = new byte[HeaderLength + body.Length];
        Magic.CopyTo(envelope, 0);
        envelope[4] = branch;
        envelope[5] = generation;
        body.CopyTo(envelope, HeaderLength);
        return envelope;
    }

    // ─────────────────────────────────────────────────────────
    // 이름표 — tenantId ‖ deviceId ‖ 난수32 (설계 §개정1 · S-1 대안)
    // ─────────────────────────────────────────────────────────

    private static byte[] PackLabel(string tenantId, string deviceId, byte[] raw)
    {
        var t = Encoding.UTF8.GetBytes(tenantId ?? string.Empty);
        var d = Encoding.UTF8.GetBytes(deviceId ?? string.Empty);

        var buffer = new byte[2 + t.Length + 2 + d.Length + raw.Length];
        var at = 0;

        buffer[at++] = (byte)(t.Length >> 8);
        buffer[at++] = (byte)(t.Length & 0xFF);
        t.CopyTo(buffer, at);
        at += t.Length;

        buffer[at++] = (byte)(d.Length >> 8);
        buffer[at++] = (byte)(d.Length & 0xFF);
        d.CopyTo(buffer, at);
        at += d.Length;

        raw.CopyTo(buffer, at);
        return buffer;
    }

    /// <summary>이름표를 꺼내 인자와 대조한다. 어긋나면 <c>null</c>(= 이 회사 이 기기가 아니다).</summary>
    private static byte[]? UnpackLabel(byte[] plain, string tenantId, string deviceId)
    {
        if (plain.Length < 4) return null;
        var at = 0;

        var tLen = (plain[at++] << 8) | plain[at++];
        if (at + tLen + 2 > plain.Length) return null;
        var t = Encoding.UTF8.GetString(plain, at, tLen);
        at += tLen;

        var dLen = (plain[at++] << 8) | plain[at++];
        if (at + dLen > plain.Length) return null;
        var d = Encoding.UTF8.GetString(plain, at, dLen);
        at += dLen;

        // 🔴 회사·기기 둘 다 맞아야 한다. 한쪽만 맞으면 그 봉투는 이 자리 것이 아니다.
        if (!string.Equals(t, tenantId ?? string.Empty, StringComparison.Ordinal)) return null;
        if (!string.Equals(d, deviceId ?? string.Empty, StringComparison.Ordinal)) return null;

        return plain[at..];
    }

    // ─────────────────────────────────────────────────────────
    // 해제
    // ─────────────────────────────────────────────────────────

    public bool TryReadGeneration(byte[]? envelope, out byte generation)
    {
        generation = 0;
        if (!LooksLikeEnvelope(envelope)) return false;

        generation = envelope![5];
        return true;
    }

    private static bool LooksLikeEnvelope(byte[]? envelope)
    {
        if (envelope is null || envelope.Length <= HeaderLength) return false;

        for (var i = 0; i < Magic.Length; i++)
        {
            if (envelope[i] != Magic[i]) return false;
        }

        return true;
    }

    public MainPcUnsealResult Unseal(byte[] envelope, string tenantId, string deviceId)
    {
        // 🔴 봉투가 아니면 **금고를 열지 않는다.** 여러 번 덮인 옛 TPM 키를 건드릴 이유가 없다.
        if (!LooksLikeEnvelope(envelope)) return MainPcUnsealResult.Legacy();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _logger.LogWarning("Windows 가 아니다 — 자료보관 컴퓨터 봉인을 풀 수 없다.");
            return MainPcUnsealResult.NotThisPc();
        }

        var branch = envelope[4];
        var generation = envelope[5];
        var body = envelope[HeaderLength..];

        // 🔴 봉투가 말한 갈래 **하나만** 쓴다. 실패해도 다른 갈래로 폴백하지 않는다 —
        //   폴백하면 「풀렸다」와 「다른 컴퓨터다」가 섞여 판정이 거짓말을 한다.
        var plain = branch switch
        {
            BranchPlatform => UnsealWithPlatform(body, tenantId, generation),
            BranchLocalMachine =>
                UnsealWithDpapi(body, TenantTagBytes(tenantId), DataProtectionScope.LocalMachine, "L"),
            // 🔵 옛 값 호환 — **읽기만** 한다(PM O-3). 봉인 쪽은 이 갈래를 만들지 않는다.
            BranchCurrentUser =>
                UnsealWithDpapi(body, TenantTagBytes(tenantId), DataProtectionScope.CurrentUser, "U"),
            _ => LogUnknownBranch(branch),
        };

        if (plain is null) return MainPcUnsealResult.NotThisPc();

        try
        {
            // 🔴 여기가 S-1 대안의 자리다 — **풀린 것만으로는 통과가 아니다.**
            //   한 컴퓨터에 회사가 둘이면 L 갈래는 서로의 봉투를 풀 수 있다.
            var raw = UnpackLabel(plain, tenantId, deviceId);
            if (raw is null)
            {
                _logger.LogWarning(
                    "자료보관 컴퓨터 봉인이 풀렸지만 이름표가 다르다 — 이 회사·이 기기의 값이 아니다. 갈래={Branch} 세대={Generation}",
                    (char)branch, generation.ToString("X2"));
                return MainPcUnsealResult.NotThisPc();
            }

            return MainPcUnsealResult.Ok(raw);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    [SupportedOSPlatform("windows")]
    private byte[]? UnsealWithPlatform(byte[] body, string tenantId, byte generation)
    {
        var keyName = KeyNameFor(tenantId, generation);
        try
        {
            // 🔴 여기가 이번 사고의 자리다 — **공급자를 명시한다.**
            //   1인자 오버로드는 기본 소프트웨어 금고만 본다(PM 동작 실측 R-1).
            if (!CngKey.Exists(keyName, CngProvider.MicrosoftPlatformCryptoProvider))
            {
                _logger.LogWarning(
                    "TPM 금고에 자료보관 컴퓨터 키가 없다 — 등록된 그 컴퓨터가 아니다. 세대={Generation}",
                    generation.ToString("X2"));
                return null;
            }

            using var cngKey = CngKey.Open(keyName, CngProvider.MicrosoftPlatformCryptoProvider);
            using var rsa = new RSACng(cngKey);
            return rsa.Decrypt(body, RSAEncryptionPadding.OaepSHA256);
        }
        catch (Exception ex)
        {
            // #15 — 삼키지 않는다. 다만 Warning 이다: 「컴퓨터가 바뀌었다」는 정상 경로다.
            _logger.LogWarning(ex, "TPM 금고 해제 실패 — 등록된 그 컴퓨터가 아니다. 세대={Generation}",
                generation.ToString("X2"));
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private byte[]? UnsealWithDpapi(byte[] body, byte[] entropy, DataProtectionScope scope, string branchName)
    {
        try
        {
            return ProtectedData.Unprotect(body, optionalEntropy: entropy, scope: scope);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DPAPI 해제 실패 — 등록된 그 컴퓨터가 아니다. 갈래={Branch}", branchName);
            return null;
        }
    }

    private byte[]? LogUnknownBranch(byte branch)
    {
        _logger.LogWarning("알 수 없는 봉인 갈래 — 등록된 그 컴퓨터가 아니다. 갈래={Branch}", (char)branch);
        return null;
    }

    // ─────────────────────────────────────────────────────────
    // 뒤처리 — 되돌리기(rows==0) · 옛 세대 정리
    // ─────────────────────────────────────────────────────────

    public bool TryDeleteGenerationKey(string tenantId, byte generation)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;
        return TryDeleteGenerationKeyCore(tenantId, generation);
    }

    [SupportedOSPlatform("windows")]
    private bool TryDeleteGenerationKeyCore(string tenantId, byte generation)
    {
        var keyName = KeyNameFor(tenantId, generation);
        try
        {
            if (!CngKey.Exists(keyName, CngProvider.MicrosoftPlatformCryptoProvider))
            {
                // 🔵 L 갈래에는 지울 이름이 없다 — 정상이다.
                _logger.LogDebug("지울 자료보관 컴퓨터 키가 없다. 세대={Generation}", generation.ToString("X2"));
                return false;
            }

            var key = CngKey.Open(keyName, CngProvider.MicrosoftPlatformCryptoProvider);
            return DeleteQuietly(key, keyName);
        }
        catch (Exception ex)
        {
            // #15 — 뒤처리 실패는 치명적이지 않다. 다만 흔적은 남긴다.
            _logger.LogWarning(ex, "자료보관 컴퓨터 키를 지우지 못했다. 세대={Generation}", generation.ToString("X2"));
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private bool DeleteQuietly(CngKey key, string keyName)
    {
        try
        {
            // ⚠️ Delete() 가 핸들까지 정리한다 — 성공하면 Dispose 를 또 부르지 않는다.
            key.Delete();
            return true;
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "자료보관 컴퓨터 키를 지우지 못했다. key={KeyName}", keyName);
            key.Dispose();
            return false;
        }
    }

    // ─────────────────────────────────────────────────────────
    // TPM 가용성 — **만들어 보고** 답한다
    // ─────────────────────────────────────────────────────────

    public bool IsPlatformKspUsable()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;

        var probeName = "HitPan.MainPc.KspProbe." + Guid.NewGuid().ToString("N")[..8];
        return TryProbePlatform(probeName);
    }

    [SupportedOSPlatform("windows")]
    private bool TryProbePlatform(string probeName)
    {
        CngKey? key = null;
        try
        {
            key = CngKey.Create(CngAlgorithm.Rsa, probeName, new CngKeyCreationParameters
            {
                Provider = CngProvider.MicrosoftPlatformCryptoProvider,
                KeyCreationOptions = CngKeyCreationOptions.None,
                ExportPolicy = CngExportPolicies.None,
            });
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TPM 금고를 쓸 수 없다 — DPAPI LocalMachine 갈래로 간다.");
            return false;
        }
        finally
        {
            // 🚨 시험용 키를 남기지 않는다. 잔재 0(작지서 §5-0 정신).
            if (key is not null) DeleteQuietly(key, probeName);
        }
    }
}
