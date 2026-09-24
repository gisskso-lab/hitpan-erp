using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>TPM 금고(Platform KSP)가 이 컴퓨터에 있는가</b> — 20260924작1 절E · CI 봉합.
/// </summary>
/// <remarks>
/// <para>
/// [무엇이 났나] CI 러너(<c>windows-latest</c>)에는 TPM 이 없다. 그런데
/// <c>CngKey.Exists(name, MicrosoftPlatformCryptoProvider)</c> 는 <b>false 를 돌려주지 않고
/// 그 자리에서 던진다</b> — <c>"The device that is required by this cryptographic provider
/// is not ready for use."</c>
/// ⇒ 시험의 <b>잔재 확인</b> 단계가 죽어, <b>TPM 과 무관한 L 갈래 시험까지</b> 빨간불이 됐다
/// (PR #411 · run 36013178091 · 12건).
/// </para>
/// <para>
/// 🚨 <b>가장 나쁜 답은 "TPM 없으면 그냥 통과"</b> 다. 이 레포는 *"막는 척만 하는 게이트"* 에
/// 여러 번 당했다(가짜게이트 8건 · 게이트 101곳 조용한 SKIP). ⇒ <see cref="DbGateEnvironment"/> 와
/// <b>같은 방식</b>으로, 건너뛴 사실을 <b>stderr 에 찍는다.</b> 초록불을 검증으로 읽지 못하게 한다.
/// </para>
/// <para>
/// ⚠️ <b>DB 게이트와 반대</b>다 — DB 는 CI 에 <b>반드시 있어야</b> 하지만, TPM 은 CI 에
/// <b>없는 것이 정상</b>이다. 그래서 여기서는 던지지 않고 <b>크게 적고</b> 넘어간다.
/// 🟢 오히려 그 환경이 <b>TPM 없는 고객 PC</b> 를 대신 재준다.
/// </para>
/// </remarks>
internal static class PlatformKspEnvironment
{
    private static readonly object Gate = new();
    private static bool _probed;
    private static bool _usable;
    private static string? _reason;

    /// <summary>
    /// 🔴 <b>글자로 묻지 않는다</b> — 실제로 <c>Exists</c> 를 한 번 불러 본다.
    /// 그 호출이 CI 에서 죽는 바로 그 호출이다. 키를 <b>만들지 않는다.</b>
    /// </summary>
    public static bool IsUsable
    {
        get
        {
            lock (Gate)
            {
                if (_probed) return _usable;
                _probed = true;

                // 🔴 개발 PC 에는 TPM 이 **있다.** 그래서 CI(= TPM 없는 고객 PC) 모양을
                //   로컬에서 재현할 방법이 없으면, 우리는 그 환경을 **영영 못 재본다.**
                //   ⇒ 이 스위치로 「TPM 없는 컴퓨터」를 흉내 낸다.
                //   🚫 CI 는 이 값을 주지 않는다. 혹시 켜져 있으면 아래 사유가 로그에 그대로 찍혀
                //     「진짜 없음」과 「흉내」가 섞이지 않는다.
                if (ForcedOff)
                {
                    _usable = false;
                    _reason = "강제 꺼둠(HITPAN_TEST_NO_TPM) — TPM 없는 컴퓨터 흉내";
                    return _usable;
                }

                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    _usable = false;
                    _reason = "Windows 가 아니다";
                    return _usable;
                }

                _usable = Probe(out _reason);
                return _usable;
            }
        }
    }

    /// <summary>왜 못 쓰는지 — 로그에 그대로 적는다.</summary>
    public static string Reason => _reason ?? "(사유 없음)";

    /// <summary>
    /// 🔵 <b>TPM 없는 컴퓨터를 흉내 내는 스위치</b>(<c>HITPAN_TEST_NO_TPM</c>).
    /// </summary>
    /// <remarks>
    /// 이것이 켜지면 시험은 제품의 <b>P 갈래를 아예 안 쓴다</b>(<c>allowPlatform:false</c>) —
    /// CI 러너가 겪는 모양 그대로다. ⇒ *"TPM 없는 고객 PC 에서도 도는가"* 를 <b>여기서 재볼 수 있다.</b>
    /// </remarks>
    public static bool ForcedOff =>
        IsTruthy(Environment.GetEnvironmentVariable("HITPAN_TEST_NO_TPM"));

    private static bool IsTruthy(string? v) =>
        !string.IsNullOrWhiteSpace(v)
        && !v.Equals("false", StringComparison.OrdinalIgnoreCase)
        && !v.Equals("0", StringComparison.Ordinal);

    [SupportedOSPlatform("windows")]
    private static bool Probe(out string? reason)
    {
        // 🚫 실물 이름을 묻지 않는다 — HitPan.MainPc.SealKey* · HitPan.TaxInvoice.MasterKey 무접촉.
        var probeName = "HitPanTest.KspProbe." + Guid.NewGuid().ToString("N")[..8];
        try
        {
            // 있든 없든 상관없다. **던지지 않고 답하는가**만 본다.
            CngKey.Exists(probeName, CngProvider.MicrosoftPlatformCryptoProvider);
            reason = null;
            return true;
        }
        catch (Exception ex)
        {
            // #15 — 삼키지 않는다. 사유를 들고 나가 아래에서 찍는다.
            reason = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// TPM 이 없어 어떤 단언을 못 할 때 부른다. <b>stderr 에 크게 적고</b> <c>true</c> 를 준다.
    /// </summary>
    /// <remarks>🔴 이 줄이 로그에 없으면 그 게이트는 <b>실제로 잰 것</b>이다. 있으면 <b>안 잰 것</b>이다.</remarks>
    public static bool SkipLoudly(string gateName, string whatWasNotMeasured)
    {
        var line =
            $"[SKIP-TPM] {gateName} — 이 컴퓨터에 TPM 금고가 없다. 「{whatWasNotMeasured}」를 **안 쟀다.** "
          + $"사유: {Reason} / 🔴 초록불을 그 축의 검증으로 읽지 마라 (20260924작1 절E · CI 봉합).";

        Console.Error.WriteLine(line);
        Record(line);
        return true;
    }

    /// <summary>
    /// 🔴 <b>지나가는 로그로는 부족하다</b> — VSTest 는 <b>통과한 시험의 콘솔 출력을 안 보여준다</b>(실측).
    /// 그래서 <b>파일로도 남긴다.</b> 이 파일이 있으면 그 판은 <b>그 축을 안 잰 판</b>이다.
    /// </summary>
    /// <remarks>
    /// 파일 위치: 시험 어셈블리 옆 <c>tpm-skips.log</c>.
    /// ⚠️ 기록 자체가 실패해도 시험을 깨지 않는다 — 다만 <b>#15</b> 로 콘솔에는 남긴다.
    /// </remarks>
    public static string LogPath => System.IO.Path.Combine(AppContext.BaseDirectory, "tpm-skips.log");

    private static void Record(string line)
    {
        try
        {
            lock (Gate)
            {
                System.IO.File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O}\t{line}{Environment.NewLine}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SKIP-TPM] 기록 파일을 쓰지 못했다: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
