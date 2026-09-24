using System.Data;
using System.Data.Common;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HitPan.Application.Interfaces;
using HitPan.Application.Services.Security;
using HitPan.Application.Services;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-21a ~ G-32 (13건)</b> — 메인PC <b>봉인 공급자 불일치</b> 봉합을 <b>동작으로</b> 묻는다.
/// 20260924작1 절E · 작지서 <c>§5</c> + <c>§9-6</c>(개정본) · PM 재결재 <c>§10</c>.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <b>§5-0 격리 규칙</b> — 이 시험이 만드는 키 이름은 <b>언제나</b>
/// <c>HitPanTest.MainPcSeal.&lt;GUID8&gt;</c> 접두사로 시작한다. 매 시험 <c>finally</c> 에서 지우고
/// <b>지워졌는지 확인</b>한다(잔재 0).
/// </para>
/// <para>
/// 🚫 <c>HitPan.MainPc.SealKey*</c> · <c>HitPan.TaxInvoice.MasterKey</c> 를
/// <b>만들지도 · 열지도 · 지우지도 않는다.</b>
/// 🚫 실물 <see cref="TpmKeyService"/> 를 <b>부르지 않는다</b> —
/// 그 안의 <c>SealKey</c> 는 이 PC 의 세금계산서 마스터키를 덮는다(M-12 · 9/24 확정).
/// </para>
/// <para>
/// ⚠️ <b>여기가 못 하는 것</b> — 브라우저·팝업·터널은 전혀 못 잰다. 실물 실측은 별도다(§4-1 V-1~V-7).
/// </para>
/// </remarks>
public sealed class MainPcSealProviderGateTests
{
    /// <summary>🚨 격리 접두사 — <b>실물 이름과 한 글자도 겹치지 않는다.</b></summary>
    private static string IsolatedPrefix() =>
        "HitPanTest.MainPcSeal." + Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// 🔴 <c>HITPAN_TEST_NO_TPM</c> 이 켜지면 <b>P 갈래를 아예 안 쓴다</b> —
    /// TPM 없는 컴퓨터(= CI 러너 · 고객 PC)를 로컬에서 재현하는 유일한 통로다(20260924작1 절E CI 봉합).
    /// </summary>
    private static MainPcSealService Make(string prefix, bool allowPlatform) =>
        new(NullLogger<MainPcSealService>.Instance, prefix,
            allowPlatform && !PlatformKspEnvironment.ForcedOff);

    private static bool OnWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private const string TenantA = "t-aaa-0001";
    private const string TenantB = "t-bbb-0002";
    private const string DeviceA = "dev-aaaa-1111";

    // ═══════════════════════════════════════════════════════════════
    // G-21a — Software 금고로 **진짜 왕복**. 어느 Windows 에서나 돈다.
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-21a 봉인한 것이 같은 컴퓨터에서 바이트까지 그대로 풀린다 (L 갈래)")]
    public void G21a_RoundTrip_LocalMachine_ReturnsSameBytes()
    {
        if (!OnWindows) return;

        var prefix = IsolatedPrefix();
        var svc = Make(prefix, allowPlatform: false);

        try
        {
            var raw = RandomNumberGenerator.GetBytes(32);

            var envelope = svc.Seal(TenantA, DeviceA, raw);
            var result = svc.Unseal(envelope, TenantA, DeviceA);

            Assert.Equal(MainPcUnsealStatus.Ok, result.Status);
            Assert.NotNull(result.Key);
            // 🔴 "풀렸다" 가 아니라 **바이트가 같다** 를 묻는다.
            Assert.Equal(raw, result.Key!);
        }
        finally
        {
            AssertNoLeftovers(prefix);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // G-21b — TPM 금고로 진짜 왕복 (TPM 이 실제로 쓰이는 컴퓨터에서만)
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-21b TPM 금고로 봉인한 것도 같은 컴퓨터에서 풀린다 (P 갈래)")]
    public void G21b_RoundTrip_Platform_ReturnsSameBytes()
    {
        if (!OnWindows) return;

        // 🔴 TPM 이 없는 컴퓨터(= CI 러너)에서는 **못 잰다.** 🚫 조용히 통과시키지 않는다.
        //   G-21a 가 본질(봉인↔해제 공급자 일치)을 L 갈래에서 이미 묻는다.
        if (!PlatformKspEnvironment.IsUsable)
        {
            PlatformKspEnvironment.SkipLoudly("G-21b", "TPM 금고로의 진짜 왕복");
            return;
        }

        var prefix = IsolatedPrefix();
        var svc = Make(prefix, allowPlatform: true);

        if (!svc.IsPlatformKspUsable())
        {
            PlatformKspEnvironment.SkipLoudly("G-21b", "TPM 금고로의 진짜 왕복(키 생성 불가)");
            return;
        }

        try
        {
            var raw = RandomNumberGenerator.GetBytes(32);

            var envelope = svc.Seal(TenantA, DeviceA, raw);
            Assert.Equal((byte)'P', envelope[4]);

            var result = svc.Unseal(envelope, TenantA, DeviceA);
            Assert.Equal(MainPcUnsealStatus.Ok, result.Status);
            Assert.Equal(raw, result.Key!);
        }
        finally
        {
            AssertNoLeftovers(prefix);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // G-22 — 공급자 분리 **음성 대조군**. R-1 자체를 게이트로 굳힌다.
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-22 소프트웨어 금고에 만든 키는 TPM 금고에서 찾을 수 없다 (공급자 분리)")]
    [SupportedOSPlatform("windows")]
    public void G22_SoftwareKsp_KeyIsNotVisibleInPlatformKsp()
    {
        if (!OnWindows) return;

        var name = IsolatedPrefix() + ".g22";

        // 🔴 TPM 이 없는 컴퓨터에서도 **이 게이트는 빈손으로 끝나지 않는다.**
        //   R-1 의 본질은 *"금고가 다르면 같은 이름이어도 없는 키다"* 이고,
        //   TPM 이 없는 컴퓨터에서는 그 사실이 **「물으면 터진다」는 모양**으로 나타난다.
        //   ⇒ 그것을 **그대로 단언한다.** 「없으니 통과」가 아니다.
        if (!PlatformKspEnvironment.IsUsable)
        {
            PlatformKspEnvironment.SkipLoudly("G-22", "TPM 금고에 같은 이름이 없다는 확인(Exists=False)");

            // ⚠️ 흉내(HITPAN_TEST_NO_TPM)일 때는 **실제로는 TPM 이 있으므로** 이 단언을 하면 거짓이 된다.
            //   흉내는 「제품이 TPM 없이 도는가」를 재려는 것이지 금고 분리를 재려는 것이 아니다.
            if (!PlatformKspEnvironment.ForcedOff) AssertSoftwareOnlyProviderSeparation(name);
            return;
        }
        CngKey? key = null;
        try
        {
            key = CngKey.Create(CngAlgorithm.Rsa, name, new CngKeyCreationParameters
            {
                Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
                KeyCreationOptions = CngKeyCreationOptions.None,
                ExportPolicy = CngExportPolicies.None,
            });

            // 🔴 이것이 선행검증 R-1 의 전부다 — **같은 이름이어도 금고가 다르면 없는 키다.**
            Assert.True(CngKey.Exists(name, CngProvider.MicrosoftSoftwareKeyStorageProvider));
            Assert.False(CngKey.Exists(name, CngProvider.MicrosoftPlatformCryptoProvider));
        }
        finally
        {
            if (key is not null)
            {
                key.Delete();
                Assert.False(CngKey.Exists(name, CngProvider.MicrosoftSoftwareKeyStorageProvider));
            }
        }
    }

    /// <summary>
    /// TPM 없는 컴퓨터판 R-1 — <b>소프트웨어 금고에는 있고, TPM 금고는 물으면 터진다.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 이것도 <b>공급자 분리의 증거</b>다. 두 금고가 같은 공간이었다면 이런 일은 없다.
    /// 그리고 이 단언이 통과한다는 것은 곧 <b>제품 코드가 이 예외를 견뎌야 한다</b>는 뜻이기도 하다
    /// (견디는지는 G-33·G-35 가 등록을 실제로 돌려 확인한다).
    /// </remarks>
    [SupportedOSPlatform("windows")]
    private static void AssertSoftwareOnlyProviderSeparation(string name)
    {
        CngKey? key = null;
        try
        {
            key = CngKey.Create(CngAlgorithm.Rsa, name, new CngKeyCreationParameters
            {
                Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
                KeyCreationOptions = CngKeyCreationOptions.None,
                ExportPolicy = CngExportPolicies.None,
            });

            Assert.True(CngKey.Exists(name, CngProvider.MicrosoftSoftwareKeyStorageProvider));

            // 🔴 TPM 이 없으면 **물음 자체가 던진다.** 조용히 false 가 아니다 — 그것이 이 환경의 사실이다.
            Assert.ThrowsAny<CryptographicException>(
                () => CngKey.Exists(name, CngProvider.MicrosoftPlatformCryptoProvider));
        }
        finally
        {
            if (key is not null)
            {
                key.Delete();
                Assert.False(CngKey.Exists(name, CngProvider.MicrosoftSoftwareKeyStorageProvider));
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // G-23 (개정) — 봉투가 갈래를 말한다 · **U 갈래는 생성되지 않는다**(PM O-3)
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-23 봉투가 갈래를 말하고, 그 글자를 바꾸면 풀리지 않는다 · U 봉인은 만들어지지 않는다")]
    public void G23_EnvelopeDeclaresBranch_AndNeverSealsWithCurrentUser()
    {
        if (!OnWindows) return;

        var prefix = IsolatedPrefix();
        var svc = Make(prefix, allowPlatform: false);

        try
        {
            var envelope = svc.Seal(TenantA, DeviceA, RandomNumberGenerator.GetBytes(32));

            // 봉투 머리 — HPM2 (세대 칸이 있는 형식)
            Assert.Equal("HPM2", Encoding.ASCII.GetString(envelope, 0, 4));

            // 🔴 PM O-3 — 봉인이 만드는 갈래는 P 또는 L 뿐이다. U 는 **절대** 나오지 않는다.
            Assert.Contains(envelope[4], new[] { (byte)'P', (byte)'L' });
            Assert.NotEqual((byte)'U', envelope[4]);

            // 갈래 글자를 바꾸면 그 말대로 열다가 실패한다 — 폴백이 없다는 증거다.
            var tampered = (byte[])envelope.Clone();
            tampered[4] = (byte)'P';
            Assert.Equal(MainPcUnsealStatus.NotThisPc, svc.Unseal(tampered, TenantA, DeviceA).Status);
        }
        finally
        {
            AssertNoLeftovers(prefix);
        }
    }

    /// <summary>
    /// 🔴 <b>U 봉인은 코드에 길 자체가 없다</b> — 여러 번 봉인해도 <c>U</c> 가 한 번도 안 나온다.
    /// </summary>
    [Fact(DisplayName = "G-23b 반복 봉인 20회에 U(CurrentUser) 봉투가 0건이다")]
    public void G23b_RepeatedSeals_NeverProduceCurrentUserBranch()
    {
        if (!OnWindows) return;

        var prefix = IsolatedPrefix();
        var svc = Make(prefix, allowPlatform: false);

        try
        {
            for (var i = 0; i < 20; i++)
            {
                var envelope = svc.Seal(TenantA, DeviceA, RandomNumberGenerator.GetBytes(32));
                Assert.NotEqual((byte)'U', envelope[4]);
            }
        }
        finally
        {
            AssertNoLeftovers(prefix);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // G-24 — 옛 값을 알아본다 · **금고를 열지 않는다**
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-24 봉투 없는 옛 값은 LegacyFormat 이고 금고를 한 번도 열지 않는다")]
    public void G24_LegacyBytes_AreRecognised_WithoutTouchingAnyVault()
    {
        if (!OnWindows) return;

        var prefix = IsolatedPrefix();
        var svc = Make(prefix, allowPlatform: true);

        try
        {
            // 옛 방식으로 봉인된 값 — 우리 봉투가 없다.
            var legacy = RandomNumberGenerator.GetBytes(256);

            var result = svc.Unseal(legacy, TenantA, DeviceA);

            Assert.Equal(MainPcUnsealStatus.LegacyFormat, result.Status);
            Assert.Null(result.Key);

            // 🔴 "열지 않았다" 를 **잔재 0** 으로 증명한다. 이 접두사 어느 세대에도 키가 없다.
        }
        finally
        {
            AssertNoLeftovers(prefix);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // G-25 — 옛 값을 만나면 [다시 인증] 으로 보낸다 (판정 + 화면 매핑)
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-25 LegacyFormat 은 ReRegisterRequired 로 가고, 화면이 그 갈래를 받는다")]
    public void G25_LegacyFormat_MapsToReRegisterRequired_AndScreenHasThatBranch()
    {
        // 🔴 판정은 생산코드의 **그 함수**에 묻는다 — 시험이 표를 베껴 적지 않는다.
        Assert.Equal(
            MainPcProofOutcome.ReRegisterRequired,
            MainPcProofService.MapUnsealStatus(MainPcUnsealStatus.LegacyFormat));

        Assert.Equal(
            MainPcProofOutcome.MainPcConfirmed,
            MainPcProofService.MapUnsealStatus(MainPcUnsealStatus.Ok));

        Assert.Equal(
            MainPcProofOutcome.DifferentPc,
            MainPcProofService.MapUnsealStatus(MainPcUnsealStatus.NotThisPc));

        // 🔴 막다른 방 재현 방지 — 화면이 이 이름을 실제로 받아 적고 있는가.
        //   ⚠️ .razor 는 컴파일된 뒤 이름이 사라지므로 여기서는 **원본 파일**을 본다.
        //     (8/16 P0 와 같은 모양: 판정은 났는데 팝업 갈래가 없어 아무것도 안 뜬다)
        var razor = ReadRepoFile("src/HitPan.Web/Components/Common/MainPcGate.razor");
        Assert.Contains("(\"ReRegisterRequired\", true)", razor);
        Assert.Contains("Prompt.Reissue", razor);
        Assert.Contains("다시 인증", razor);
    }

    // ═══════════════════════════════════════════════════════════════
    // G-26 (개정) — 403 이면 왕복하고 다시 간다 · 동시 3건도 **1회** · 다른 경로는 **0회**
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-26a 403 main_pc_only 면 왕복 한 바퀴 뒤 출입증을 달고 다시 가서 200 이 된다")]
    public async Task G26a_Forbidden_MainPcOnly_RecoversWithPassHeader()
    {
        var server = new FakeMainPcServer();
        var coordinator = new MainPcRetryCoordinator(() => DateTimeOffset.UtcNow);

        using var original = new HttpRequestMessage(HttpMethod.Get, "https://x.test/api/backup/settings");

        var recovered = await coordinator.TryRecoverAsync(
            original,
            server.SendAsync,
            CloneAsync,
            server.DecorateAsync,
            _ => Task.FromResult(true),
            pass => { server.SavedPass = pass; return Task.CompletedTask; },
            () => Task.FromResult(true),
            (_, _) => { },
            CancellationToken.None);

        Assert.NotNull(recovered);
        Assert.Equal(HttpStatusCode.OK, recovered!.StatusCode);
        Assert.Equal(1, coordinator.ProofRoundTrips);

        // 🔴 두 번째 요청에 **출입증이 실려 나갔다** — 이것이 "다시 갔다" 의 증거다.
        Assert.True(server.LastRequestHadPass);
    }

    [Fact(DisplayName = "G-26b 동시에 들어온 403 세 건이 왕복을 단 1회만 돈다 (단일 왕복 잠금)")]
    public async Task G26b_ThreeConcurrentDenials_RunProofExactlyOnce()
    {
        var server = new FakeMainPcServer { ProofDelay = TimeSpan.FromMilliseconds(120) };
        var coordinator = new MainPcRetryCoordinator(() => DateTimeOffset.UtcNow);

        var paths = new[] { "settings", "history", "restore-history" };
        var tasks = paths.Select(async p =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://x.test/api/backup/{p}");
            return await coordinator.TryRecoverAsync(
                req,
                server.SendAsync,
                CloneAsync,
                server.DecorateAsync,
                _ => Task.FromResult(true),
                pass => { server.SavedPass = pass; return Task.CompletedTask; },
                () => Task.FromResult(true),
                (_, _) => { },
                CancellationToken.None);
        }).ToArray();

        var responses = await Task.WhenAll(tasks);

        // 🔴 쿨다운만으로는 여기서 3회가 된다(실패 뒤에만 걸리므로). 잠금이 있어야 1회다.
        Assert.Equal(1, coordinator.ProofRoundTrips);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r!.StatusCode));
    }

    [Fact(DisplayName = "G-26c 자료관리가 아닌 경로(api/employee)의 403 에는 왕복이 0회다")]
    public async Task G26c_NonDataAdminPath_DoesNotRunProof()
    {
        var server = new FakeMainPcServer();
        var coordinator = new MainPcRetryCoordinator(() => DateTimeOffset.UtcNow);

        using var original = new HttpRequestMessage(HttpMethod.Get, "https://x.test/api/employee/list");

        var recovered = await coordinator.TryRecoverAsync(
            original,
            server.SendAsync,
            CloneAsync,
            server.DecorateAsync,
            _ => Task.FromResult(true),
            pass => { server.SavedPass = pass; return Task.CompletedTask; },
            () => Task.FromResult(true),
            (_, _) => { },
            CancellationToken.None);

        // 🔴 표면을 넓히지 않는다 — 다른 403 은 원래 응답 그대로 화면에 간다.
        Assert.Null(recovered);
        Assert.Equal(0, coordinator.ProofRoundTrips);
    }

    // ═══════════════════════════════════════════════════════════════
    // G-27 — 통과 전엔 자료를 부르지 않는다
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-27 막혔을 때 조회 0회 · 통과했을 때 정확히 1회")]
    public async Task G27_OnAllowed_RunsOnlyAfterPass()
    {
        var blockedCalls = 0;
        var blocked = new MainPcAccessState();
        await blocked.ResolveAsync(() => Task.FromResult(false),
            () => { blockedCalls++; return Task.CompletedTask; }, _ => { });

        Assert.False(blocked.IsMainPc);
        Assert.Equal(0, blockedCalls);

        var allowedCalls = 0;
        var allowed = new MainPcAccessState();
        await allowed.ResolveAsync(() => Task.FromResult(true),
            () => { allowedCalls++; return Task.CompletedTask; }, _ => { });

        // 두 번 불러도 두 번 열지 않는다 — 재렌더링 소음 금지.
        await allowed.ResolveAsync(() => Task.FromResult(true),
            () => { allowedCalls++; return Task.CompletedTask; }, _ => { });

        Assert.True(allowed.IsMainPc);
        Assert.Equal(1, allowedCalls);
    }

    // ═══════════════════════════════════════════════════════════════
    // G-28 🆕 — 서버가 **재등록을 받는다** (막다른 방 재현 방지 · S-4)
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-28 판정이 ReRegisterRequired 여도 등록 요청이 받아들여진다 (400 이 아니다)")]
    public async Task G28_ReRegisterRequired_IsAcceptedByRegisterEndpoint()
    {
        foreach (var outcome in new[]
                 {
                     MainPcProofOutcome.NotRegisteredYet,
                     MainPcProofOutcome.DifferentPc,
                     // 🔴 이 줄이 화이트리스트에 없으면 [다시 인증] 이 400 으로 튕긴다(8/16 P0 모양).
                     MainPcProofOutcome.ReRegisterRequired,
                 })
        {
            var result = await CallRegisterAsync(outcome);
            Assert.NotEqual(400, StatusOf(result));
        }

        // 음성 대조군 — 통과한 사람은 등록 절차를 다시 탈 이유가 없다.
        Assert.Equal(400, StatusOf(await CallRegisterAsync(MainPcProofOutcome.MainPcConfirmed)));
        Assert.Equal(400, StatusOf(await CallRegisterAsync(MainPcProofOutcome.NotProven)));
    }

    // ═══════════════════════════════════════════════════════════════
    // G-29 🆕 — 두 번째 봉인이 첫째를 **안 죽인다**
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-29 같은 컴퓨터에서 회사 B 를 봉인해도 회사 A 의 봉투가 그대로 풀린다")]
    public void G29_SecondSeal_DoesNotKillTheFirst()
    {
        if (!OnWindows) return;

        var prefix = IsolatedPrefix();
        var svc = Make(prefix, allowPlatform: true);

        try
        {
            var rawA = RandomNumberGenerator.GetBytes(32);
            var envelopeA = svc.Seal(TenantA, DeviceA, rawA);

            // 🔴 TPM 이 없으면 L 갈래로 내려간다 — L 에는 **이름이 없어** 충돌할 것도 없다.
            //   그 컴퓨터에서 이 시험이 무는 것은 「둘째 봉인이 첫째를 안 죽인다」뿐이고,
            //   **이름 축은 안 잰 것**이다. 🚫 조용히 넘어가지 않는다.
            if (envelopeA[4] != (byte)'P')
            {
                PlatformKspEnvironment.SkipLoudly("G-29", "회사별 키 이름(T16)의 충돌 방지 — P 갈래");
            }

            // 🔴 같은 컴퓨터 · 같은 접두사 · **다른 회사**. 이름이 회사별이 아니면 여기서 A 가 죽는다.
            svc.Seal(TenantB, DeviceA, RandomNumberGenerator.GetBytes(32));

            var again = svc.Unseal(envelopeA, TenantA, DeviceA);
            Assert.Equal(MainPcUnsealStatus.Ok, again.Status);
            Assert.Equal(rawA, again.Key!);
        }
        finally
        {
            AssertNoLeftovers(prefix);
        }
    }

    [Fact(DisplayName = "G-29b 같은 회사를 다시 봉인해도 세대가 올라가고 옛 봉투가 그대로 풀린다")]
    public void G29b_ReSeal_AdvancesGeneration_AndKeepsOldEnvelopeReadable()
    {
        if (!OnWindows) return;

        var prefix = IsolatedPrefix();
        var svc = Make(prefix, allowPlatform: true);

        try
        {
            var rawOld = RandomNumberGenerator.GetBytes(32);
            var oldEnvelope = svc.Seal(TenantA, DeviceA, rawOld);
            Assert.True(svc.TryReadGeneration(oldEnvelope, out var oldGen));

            var newEnvelope = svc.Seal(TenantA, DeviceA, RandomNumberGenerator.GetBytes(32), oldGen);
            Assert.True(svc.TryReadGeneration(newEnvelope, out var newGen));

            // 🚨 세대가 안 올라가면 덮어쓰기와 같은 일이 벌어진다(M-12).
            Assert.NotEqual(oldGen, newGen);

            // 🔴 TPM 이 없으면 L 갈래다 — 세대는 봉투에만 있고 **이름 충돌은 안 쟀다.**
            if (newEnvelope[4] != (byte)'P')
            {
                PlatformKspEnvironment.SkipLoudly("G-29b", "세대별 키 이름의 충돌 방지 — P 갈래");
            }

            var old = svc.Unseal(oldEnvelope, TenantA, DeviceA);
            Assert.Equal(MainPcUnsealStatus.Ok, old.Status);
            Assert.Equal(rawOld, old.Key!);
        }
        finally
        {
            AssertNoLeftovers(prefix);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // G-30 🆕 — 다른 회사의 봉투는 안 통한다 (이름표 대조 · S-1 대안)
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-30 다른 기기의 봉투는 안 통한다 — 이름표 대조 (풀렸다만으로 통과 안 된다)")]
    public void G30_EnvelopeOfAnotherDevice_IsRejected()
    {
        if (!OnWindows) return;

        var prefix = IsolatedPrefix();
        var svc = Make(prefix, allowPlatform: false);

        try
        {
            var envelopeA = svc.Seal(TenantA, DeviceA, RandomNumberGenerator.GetBytes(32));

            // 🔴 여기가 **이름표가 실제로 일하는 축**이다(20260924작1 [4] C-3).
            //   같은 회사·같은 컴퓨터라 금고도 열리고 entropy 도 같다 — 바이트는 풀린다.
            //   그래도 **다른 기기**의 자리에서는 통하면 안 된다. 가릴 것은 이름표뿐이다.
            Assert.Equal(MainPcUnsealStatus.NotThisPc, svc.Unseal(envelopeA, TenantA, "dev-다른기기").Status);

            // 제 자리에서는 그대로 열린다 — 대조가 과하지 않다는 확인.
            Assert.Equal(MainPcUnsealStatus.Ok, svc.Unseal(envelopeA, TenantA, DeviceA).Status);
        }
        finally
        {
            AssertNoLeftovers(prefix);
        }
    }

    /// <summary>
    /// 🔴 <b>G-30b</b> — 회사 축은 <b>이름표가 아니라 키 이름·entropy</b> 가 가른다
    /// (20260924작1 [4] C-3).
    /// </summary>
    /// <remarks>
    /// [왜 따로 세우나] G-30 에서 회사를 바꿔 물으면 <b>두 겹</b>이 동시에 막는다
    /// (entropy 와 이름표). 그러면 어느 쪽을 빼도 초록이라 <b>게이트가 거짓말을 한다</b>
    /// — 검증 K2b 가 실제로 그것을 잡았다. ⇒ 회사 축은 <b>그 축만</b> 여기서 직접 묻는다.
    /// </remarks>
    [Fact(DisplayName = "G-30b 회사가 다르면 키 이름이 다르고, 남의 회사 entropy 로는 바이트가 안 풀린다")]
    [SupportedOSPlatform("windows")]
    public void G30b_TenantAxis_IsSeparatedByKeyNameAndEntropy()
    {
        if (!OnWindows) return;

        var prefix = IsolatedPrefix();
        var svc = Make(prefix, allowPlatform: false);

        try
        {
            // ① P 갈래의 회사 구분 — **이름이 다르다.**
            Assert.NotEqual(svc.KeyNameFor(TenantA, 0), svc.KeyNameFor(TenantB, 0));

            // ② L 갈래의 회사 구분 — **entropy 가 다르다.** 봉투 본문을 남의 회사 값으로 풀면 터진다.
            var envelopeA = svc.Seal(TenantA, DeviceA, RandomNumberGenerator.GetBytes(32));
            Assert.Equal((byte)'L', envelopeA[4]);

            var body = envelopeA[6..];
            Assert.Throws<CryptographicException>(() => ProtectedData.Unprotect(
                body,
                optionalEntropy: MainPcSealService.TenantTagBytes(TenantB),
                scope: DataProtectionScope.LocalMachine));

            // 제 회사 값으로는 풀린다 — 막는 것이 entropy 라는 확인(과하지 않다).
            var plain = ProtectedData.Unprotect(
                body,
                optionalEntropy: MainPcSealService.TenantTagBytes(TenantA),
                scope: DataProtectionScope.LocalMachine);
            Assert.NotEmpty(plain);
        }
        finally
        {
            AssertNoLeftovers(prefix);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // G-31 🆕 — 등록 실패(rows==0)가 옛 값을 **안 죽인다**
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-31 봉인 뒤 DB 가 0행이어도 옛 봉투가 그대로 풀리고 새 세대 키는 안 남는다")]
    public void G31_FailedRegistration_KeepsOldEnvelopeAlive()
    {
        if (!OnWindows) return;

        var prefix = IsolatedPrefix();
        var svc = Make(prefix, allowPlatform: true);

        try
        {
            var rawOld = RandomNumberGenerator.GetBytes(32);
            var oldEnvelope = svc.Seal(TenantA, DeviceA, rawOld);
            Assert.True(svc.TryReadGeneration(oldEnvelope, out var oldGen));

            // ── 등록을 한 바퀴 돌린다: 새 세대 봉인 → **DB 가 0행이었다** → 되돌리기 ──
            var newEnvelope = svc.Seal(TenantA, DeviceA, RandomNumberGenerator.GetBytes(32), oldGen);
            Assert.True(svc.TryReadGeneration(newEnvelope, out var newGen));

            svc.TryDeleteGenerationKey(TenantA, newGen);   // §9-3 5단계 — **새 것만** 지운다

            // 🔴 옛 봉투는 살아 있어야 한다. 이것이 *"등록 실패 1회가 옛 값을 죽인다"* 의 반대말이다.
            var old = svc.Unseal(oldEnvelope, TenantA, DeviceA);
            Assert.Equal(MainPcUnsealStatus.Ok, old.Status);
            Assert.Equal(rawOld, old.Key!);

            // 그리고 새 세대 키는 **안 남는다** — 🔴 이름 있는 키가 있는 `P` 갈래에서만 잰다.
            //   ⚠️ 20260924작1 [4] C-4 — 종전에는 `L` 갈래에서 **상수를 돌려줘** 무엇을 빼도
            //     통과했다(자기충족). 이제 `L` 에서는 **이 단언을 아예 하지 않는다.**
            //     그 갈래의 진실원은 DB 이고, DB 축은 **G-35** 가 따로 묻는다.
            if (newEnvelope[4] == (byte)'P')
            {
                Assert.Equal(MainPcUnsealStatus.NotThisPc, svc.Unseal(newEnvelope, TenantA, DeviceA).Status);
            }
            else
            {
                // 🔴 L 갈래에는 지울 이름이 없다 ⇒ 이 축은 **안 쟀다.** 🚫 조용히 통과시키지 않는다.
                //   그 갈래의 진실원은 DB 이고, DB 축은 G-35 가 TPM 과 무관하게 잰다.
                PlatformKspEnvironment.SkipLoudly("G-31", "되돌린 새 세대 키가 안 남는지 — P 갈래");
            }
        }
        finally
        {
            AssertNoLeftovers(prefix);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // G-32 🆕 — 표면이 안 넓어진다 (비대표는 왕복 0회)
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-32 비대표가 403 main_pc_only 를 받아도 왕복 0회 · 출입증 요청 0건")]
    public async Task G32_NonOwner_NeverRunsProof()
    {
        var server = new FakeMainPcServer();
        var coordinator = new MainPcRetryCoordinator(() => DateTimeOffset.UtcNow);

        using var original = new HttpRequestMessage(HttpMethod.Get, "https://x.test/api/backup/settings");

        var recovered = await coordinator.TryRecoverAsync(
            original,
            server.SendAsync,
            CloneAsync,
            server.DecorateAsync,
            _ => Task.FromResult(true),
            pass => { server.SavedPass = pass; return Task.CompletedTask; },
            () => Task.FromResult(false),   // 🔴 대표가 아니다
            (_, _) => { },
            CancellationToken.None);

        Assert.Null(recovered);
        Assert.Equal(0, coordinator.ProofRoundTrips);
        Assert.Equal(0, server.ChallengeCalls);
        Assert.Equal(0, server.VerifyCalls);
    }

    // ═══════════════════════════════════════════════════════════════
    // G-33 🆕 — 운영 경로는 세금계산서 금고(ITpmKeyService)를 **한 번도 안 부른다**
    //   20260924작1 [4] C-2 (P1-2) · 🚨 이 트랙이 존재하는 이유가 M-12 다.
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-33 등록을 한 바퀴 돌려도 ITpmKeyService.SealKey/UnsealKey 가 0회 불린다")]
    public async Task G33_ProductionPath_NeverSealsThroughTpmKeyService()
    {
        if (!OnWindows) return;

        var prefix = IsolatedPrefix();
        var seal = Make(prefix, allowPlatform: true);

        // 🚨 덫이다 — 부르면 그 자리에서 터진다. 실물이라면 세금계산서 마스터키를 덮었을 자리다.
        var tripwire = new TripwireTpm();
        var db = new FakeDevicesDb();
        db.Rows.Add(new FakeDevicesDb.Row(TenantA, DeviceA) { IsMainPc = false });

        var svc = new MainPcProofService(db, tripwire, seal, NullLogger<MainPcProofService>.Instance);

        try
        {
            Assert.True(await svc.RegisterThisPcAsync(TenantA, DeviceA, CancellationToken.None));

            // 다시 한 바퀴 — 두 번째 등록(세대 올리기)도 그 금고를 안 건드린다.
            Assert.True(await svc.RegisterThisPcAsync(TenantA, DeviceA, CancellationToken.None));

            Assert.Equal(0, tripwire.SealCalls);
            Assert.Equal(0, tripwire.UnsealCalls);
        }
        finally
        {
            AssertNoLeftovers(prefix);
        }
    }

    /// <summary>
    /// 🔴 <b>G-34</b> — 종전 3인자 생성자로는 <b>운영 코드가 컴파일되지 않는다</b>
    /// (20260924작1 [4] C-2).
    /// </summary>
    /// <remarks>
    /// 종전엔 <c>Program.cs</c> 의 팩터리 한 줄만이 방벽이었다 — 그 줄을 누가 건드리면
    /// 봉인이 조용히 <c>TpmKeyService.SealKey</c> 로 돌아간다. 🚫 <b>지우지 않고</b>(#1)
    /// <b>컴파일 오류</b>로 막았다. 이 게이트는 그 자물쇠가 <b>실제로 걸려 있는지</b> 본다.
    /// </remarks>
    [Fact(DisplayName = "G-34 봉인을 ITpmKeyService 로 보내는 생성자는 컴파일 오류로 막혀 있다")]
    public void G34_LegacyConstructor_IsCompileTimeBlocked()
    {
        var legacy = typeof(MainPcProofService).GetConstructors()
            .Single(c => c.GetParameters().Length == 3);

        var obsolete = legacy.GetCustomAttributes(typeof(ObsoleteAttribute), false)
            .Cast<ObsoleteAttribute>()
            .SingleOrDefault();

        Assert.NotNull(obsolete);
        // 🔴 경고로는 부족하다 — 경고는 지나칠 수 있다. **오류**여야 한다.
        Assert.True(obsolete!.IsError);

        // 그리고 안전한 길은 여전히 열려 있다(막기만 하고 길을 안 주면 그것도 막다른 방이다).
        Assert.Contains(typeof(MainPcProofService).GetConstructors(),
            c => c.GetParameters().Length == 4);
    }

    // ═══════════════════════════════════════════════════════════════
    // G-35 🆕 — 등록이 실패해도 그 회사에 **메인PC 줄이 남는다**
    //   20260924작1 [4] C-1 (P1-1) · 🔴 열쇠가 살아도 가리키는 줄이 사라지면 막다른 방이다.
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-35 등록이 0행으로 실패해도 그 회사의 is_main_pc=1 줄이 그대로 있다")]
    public async Task G35_FailedRegistration_KeepsMainPcRowInDb()
    {
        if (!OnWindows) return;

        var prefix = IsolatedPrefix();
        var seal = Make(prefix, allowPlatform: true);
        var db = new FakeDevicesDb();

        // 지금 메인PC 는 옛 기기다.
        db.Rows.Add(new FakeDevicesDb.Row(TenantA, "dev-old-main") { IsMainPc = true });
        // 🔴 등록하려는 기기는 **줄이 없다** — 그래서 ②가 0행이 된다(실제 500 이 나던 그 모양).

        var svc = new MainPcProofService(db, new TripwireTpm(), seal, NullLogger<MainPcProofService>.Instance);

        try
        {
            Assert.False(await svc.RegisterThisPcAsync(TenantA, "dev-missing", CancellationToken.None));

            // 🔴 여기가 C-1 이 닫는 자리다 — 실패했다고 **메인PC 가 없어지면 안 된다.**
            var survivor = Assert.Single(db.Rows, r => r.TenantId == TenantA && r.IsMainPc);
            Assert.Equal("dev-old-main", survivor.DeviceId);
        }
        finally
        {
            AssertNoLeftovers(prefix);
        }
    }

    // ─────────────────────────────────────────────────────────
    // 격리 확인 — 🚨 잔재 0
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 🚨 이 접두사로 만들어진 키가 <b>한 개도 안 남았는지</b> 확인하고, 남았으면 지운다.
    /// </summary>
    /// <remarks>
    /// 🚫 실물 이름(<c>HitPan.MainPc.SealKey*</c> · <c>HitPan.TaxInvoice.MasterKey</c>)은
    /// 이 함수가 <b>만질 수 없다</b> — 접두사가 <c>HitPanTest.</c> 로 시작하지 않으면 즉시 실패시킨다.
    /// </remarks>
    /// <summary>시험이 쓸 수 있는 세대 범위 — 봉인은 늘 <c>00</c> 부근에서 시작한다(넉넉히 본다).</summary>
    private const int SweepGenerations = 16;

    private static void AssertNoLeftovers(string prefix)
    {
        // 🚨 이 한 줄이 안전장치다 — 실물 접두사가 들어오면 **아무것도 지우기 전에** 멈춘다.
        Assert.StartsWith("HitPanTest.MainPcSeal.", prefix, StringComparison.Ordinal);

        // ⚠️ OperatingSystem.IsWindows() 로 묻는다 — 분석기가 알아보는 유일한 형태다(CA1416).
        if (!OperatingSystem.IsWindows()) return;

        // 🔴 20260924작1 절E CI 봉합 — TPM 이 없는 컴퓨터(= CI 러너)에서는
        //   CngKey.Exists(..., Platform) 이 **false 를 주지 않고 던진다.**
        //   이름 있는 키는 P 갈래에만 있으므로, TPM 이 없으면 **만들어진 적도 없다** ⇒ 잔재도 없다.
        //   🚫 그래도 조용히 넘어가지 않는다 — 안 쟀다는 사실을 stderr 에 남긴다.
        if (!PlatformKspEnvironment.IsUsable)
        {
            PlatformKspEnvironment.SkipLoudly("잔재 확인(P 갈래)", "시험 키가 남았는지");
            return;
        }

        var svc = Make(prefix, allowPlatform: true);

        // ① 이 시험이 만든 것을 **전부 지운다**(§5-0).
        for (var g = 0; g < SweepGenerations; g++)
        {
            svc.TryDeleteGenerationKey(TenantA, (byte)g);
            svc.TryDeleteGenerationKey(TenantB, (byte)g);
        }

        // ② 🚨 **지워졌는지 확인한다** — 잔재 0. 지웠다고 믿지 않는다.
        Assert.Empty(CollectLeftovers(svc));
    }

    [SupportedOSPlatform("windows")]
    private static List<string> CollectLeftovers(MainPcSealService svc)
    {
        var leftovers = new List<string>();
        for (var g = 0; g < SweepGenerations; g++)
        {
            CollectIfExists(svc.KeyNameFor(TenantA, (byte)g), leftovers);
            CollectIfExists(svc.KeyNameFor(TenantB, (byte)g), leftovers);
        }

        return leftovers;
    }

    [SupportedOSPlatform("windows")]
    private static void CollectIfExists(string name, List<string> leftovers)
    {
        try
        {
            if (CngKey.Exists(name, CngProvider.MicrosoftPlatformCryptoProvider)) leftovers.Add(name);
        }
        catch (CryptographicException ex)
        {
            // #15 — 삼키지 않는다. TPM 이 도중에 사라지는 경우까지 조용히 넘기면
            //   「잔재 0」이 **재본 적 없는 주장**이 된다. 크게 적고 나간다.
            Console.Error.WriteLine(
                $"[SKIP-TPM] 잔재 확인 — TPM 금고를 열지 못했다: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────
    // 거들기 — 가짜 서버 · 가짜 컨트롤러 배선
    // ─────────────────────────────────────────────────────────

    private static Task<HttpRequestMessage> CloneAsync(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);
        return Task.FromResult(clone);
    }

    /// <summary>
    /// 🔵 「출입증이 없으면 403 <c>main_pc_only</c>, 있으면 200」 하나만 흉내 내는 서버.
    /// </summary>
    private sealed class FakeMainPcServer
    {
        public string? SavedPass { get; set; }
        public bool LastRequestHadPass { get; private set; }
        public int ChallengeCalls { get; private set; }
        public int VerifyCalls { get; private set; }
        public TimeSpan ProofDelay { get; init; } = TimeSpan.Zero;

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            // ⚠️ 왕복 3콜은 **상대 주소**로 나간다(실제 HttpClient BaseAddress 를 쓰는 모양 그대로).
            //   상대 Uri 에 AbsolutePath 를 물으면 터진다 — 문자열로 본다.
            var path = req.RequestUri?.ToString() ?? string.Empty;

            if (path.Contains("mainpc-challenge", StringComparison.OrdinalIgnoreCase))
            {
                ChallengeCalls++;
                if (ProofDelay > TimeSpan.Zero) await Task.Delay(ProofDelay, ct).ConfigureAwait(false);
                return Json(HttpStatusCode.OK, "{\"challenge\":\"c-1\"}");
            }

            if (path.Contains("mainpc-verify", StringComparison.OrdinalIgnoreCase))
            {
                VerifyCalls++;
                return Json(HttpStatusCode.OK, "{\"outcome\":\"MainPcConfirmed\",\"isMainPc\":true,\"pass\":\"p-1\"}");
            }

            LastRequestHadPass = req.Headers.Contains("X-MainPc-Pass");
            return LastRequestHadPass
                ? Json(HttpStatusCode.OK, "{\"ok\":true}")
                : Json(HttpStatusCode.Forbidden, "{\"error\":\"main_pc_only\"}");
        }

        public Task DecorateAsync(HttpRequestMessage req)
        {
            if (!string.IsNullOrEmpty(SavedPass)) req.Headers.TryAddWithoutValidation("X-MainPc-Pass", SavedPass);
            return Task.CompletedTask;
        }

        private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
            new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    // ── G-28 배선 — **진짜 컨트롤러 메서드**를 부른다 (글자검사 아님) ──────────

    /// <summary>
    /// 🔴 판정 하나만 세우고 <b>실제 <c>DeviceController.RegisterMainPc</c></b> 를 부른다.
    /// </summary>
    /// <remarks>
    /// 🚫 DB 도 실물 봉인도 안 쓴다 — 여기서 재는 것은 <b>화이트리스트 한 줄</b>이다(S-4 · §9-4).
    /// </remarks>
    private static async Task<IActionResult> CallRegisterAsync(MainPcProofOutcome outcome)
    {
        var proof = new StagedProof(outcome);
        var controller = new HitPan.API.Controllers.DeviceController(null!, proof);

        var ctx = new DefaultHttpContext();
        ctx.Items["TenantId"] = TenantA;
        ctx.Items["UserId"] = "u-1";
        ctx.Request.Headers["X-HitPan-Device-Id"] = DeviceA;
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("account_type", "tenant_admin") }, "test"));

        controller.ControllerContext = new ControllerContext { HttpContext = ctx };

        return await controller.RegisterMainPc(
            new HitPan.API.Controllers.DeviceController.MainPcChallengeRequest { Challenge = "c-1" },
            CancellationToken.None);
    }

    private static int StatusOf(IActionResult result) => result switch
    {
        BadRequestObjectResult => 400,
        OkObjectResult => 200,
        ObjectResult o => o.StatusCode ?? 200,
        _ => 200,
    };

    /// <summary>판정 하나만 세워 두는 대역 — <b>봉인도 DB 도 건드리지 않는다.</b></summary>
    private sealed class StagedProof(MainPcProofOutcome staged) : IMainPcProofService
    {
        public string IssueChallenge(string tenantId, string sessionKey) => "c-1";

        public Task<MainPcProofOutcome> ConfirmLocalAsync(string challenge, CancellationToken ct) =>
            Task.FromResult(staged);

        public MainPcProofOutcome Consume(string tenantId, string sessionKey, string challenge, out string? pass)
        {
            pass = null;
            return staged;
        }

        public bool IsPassValid(string? pass) => false;

        public bool IsPassValid(string? pass, string? tenantId) => false;

        public Task<bool> RegisterThisPcAsync(string tenantId, string deviceId, CancellationToken ct) =>
            Task.FromResult(true);

        public Task<bool> IsDataEmptyAsync(string tenantId, CancellationToken ct) =>
            Task.FromResult(false);
    }

    // ── G-33·G-35 배선 — **진짜 RegisterThisPcAsync** 를 돌린다 ─────────────

    /// <summary>
    /// 🚨 세금계산서 금고의 <b>덫</b> — 봉인/해제를 부르면 그 자리에서 터진다.
    /// </summary>
    /// <remarks>
    /// 실물 <c>TpmKeyService.SealKey</c> 는 이 PC 의 <c>HitPan.TaxInvoice.MasterKey</c> 를 덮는다(M-12).
    /// 🚫 그래서 시험은 실물을 절대 안 쓴다. 대신 <b>안 불리는지</b>를 이 덫으로 잰다.
    /// </remarks>
    private sealed class TripwireTpm : ITpmKeyService
    {
        public int SealCalls { get; private set; }
        public int UnsealCalls { get; private set; }

        public bool IsTpmAvailable() => false;

        public byte[] SealKey(byte[] masterKey)
        {
            SealCalls++;
            throw new Xunit.Sdk.XunitException(
                "🚨 운영 경로가 ITpmKeyService.SealKey 를 불렀다 — 세금계산서 마스터키를 덮는 그 길이다(M-12).");
        }

        public byte[] UnsealKey(byte[] sealedKey)
        {
            UnsealCalls++;
            throw new Xunit.Sdk.XunitException(
                "🚨 운영 경로가 ITpmKeyService.UnsealKey 를 불렀다 — 이 트랙이 끊어 낸 그 길이다.");
        }

        public bool IsSealedKeyValid(byte[] sealedKey) => true;
    }

    /// <summary>
    /// 🔵 <c>tenant_devices</c> 를 <b>메모리에서 흉내</b> 내는 DB — Dapper 가 그대로 돈다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>왜 흉내인가</b> — 이 개발 PC 는 <c>CREATE DATABASE</c> 가 막혀 있다(권한).
    /// 그렇다고 상수를 단언하면 <b>게이트가 거짓말을 한다.</b> ⇒ 생산코드가 보내는 SQL 을
    /// <b>실제로 받아</b> 줄 상태를 바꾸고, 시험은 <b>바뀐 줄을 세어 본다.</b>
    /// ⚠️ 이것은 SQL 엔진이 아니다. 이 트랙이 쓰는 <b>네 문장만</b> 안다.
    /// </remarks>
    private sealed class FakeDevicesDb : DbConnection
    {
        public sealed class Row(string tenantId, string deviceId)
        {
            public string TenantId { get; } = tenantId;
            public string DeviceId { get; } = deviceId;
            public bool IsMainPc { get; set; }
            public byte[]? SealedKey { get; set; }
        }

        public List<Row> Rows { get; } = new();

        private ConnectionState _state = ConnectionState.Open;

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = "fake";
        public override string Database => "fake";
        public override string DataSource => "fake";
        public override string ServerVersion => "0";
        public override ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName) { }
        public override void Close() => _state = ConnectionState.Closed;
        public override void Open() => _state = ConnectionState.Open;

        protected override DbTransaction BeginDbTransaction(IsolationLevel il) =>
            throw new NotSupportedException("이 시험은 트랜잭션을 쓰지 않는다.");

        protected override DbCommand CreateDbCommand() => new FakeCommand(this);

        /// <summary>생산코드가 보낸 문장 하나를 <b>실제로 적용</b>한다.</summary>
        internal int Apply(string sql, IDataParameterCollection ps)
        {
            var tenant = Param(ps, "TenantId") as string;
            var device = Param(ps, "DeviceId") as string;

            // ② 승격 — 봉투를 함께 심는다. **대상 줄이 없으면 0행**이다(이 시험의 출발점).
            if (sql.Contains("mainpc_sealed_key", StringComparison.Ordinal)
                && sql.Contains("SET", StringComparison.Ordinal))
            {
                var target = Rows.FirstOrDefault(r => r.TenantId == tenant && r.DeviceId == device);
                if (target is null) return 0;

                target.IsMainPc = true;
                target.SealedKey = Param(ps, "SealedKey") as byte[];
                return 1;
            }

            // ① 강등 — 그 회사의 다른 메인PC 줄을 내린다.
            if (sql.Contains("is_main_pc = 0", StringComparison.Ordinal))
            {
                var hit = Rows.Where(r => r.TenantId == tenant && r.IsMainPc && r.DeviceId != device).ToList();
                foreach (var r in hit) r.IsMainPc = false;
                return hit.Count;
            }

            // 되돌리기 — 우리가 내린 그 줄을 다시 올린다(C-1).
            if (sql.Contains("is_main_pc = 1", StringComparison.Ordinal))
            {
                var target = Rows.FirstOrDefault(r => r.TenantId == tenant && r.DeviceId == device);
                if (target is null) return 0;

                target.IsMainPc = true;
                return 1;
            }

            throw new Xunit.Sdk.XunitException($"이 시험이 모르는 문장이다 — 게이트를 함께 고쳐라: {sql}");
        }

        /// <summary>판정·세대 읽기용 SELECT 한 줄.</summary>
        internal Row? SelectMainRow(IDataParameterCollection ps)
        {
            var tenant = Param(ps, "TenantId") as string;
            return Rows.FirstOrDefault(r => r.TenantId == tenant && r.IsMainPc);
        }

        private static object? Param(IDataParameterCollection ps, string name)
        {
            foreach (IDataParameter p in ps)
            {
                if (string.Equals(p.ParameterName, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
            }

            return null;
        }
    }

    private sealed class FakeCommand(FakeDevicesDb owner) : DbCommand
    {
        private readonly FakeParameterCollection _ps = new();

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; } = CommandType.Text;
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection { get; set; } = owner;
        protected override DbParameterCollection DbParameterCollection => _ps;
        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() { }
        public override int ExecuteNonQuery() => owner.Apply(CommandText, _ps);
        public override object? ExecuteScalar() => null;
        public override void Prepare() { }

        protected override DbParameter CreateDbParameter() => new FakeParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            new FakeReader(owner.SelectMainRow(_ps));
    }

    private sealed class FakeParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
        public override bool IsNullable { get; set; }
        // ⚠️ 밑틀이 [AllowNull] 로 선언한 자리다 — 그대로 맞춰야 경고 0 이 된다(#19).
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ParameterName { get; set; } = string.Empty;

        public override int Size { get; set; }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;
        public override bool SourceColumnNullMapping { get; set; }
        public override object? Value { get; set; }

        public override void ResetDbType() { }
    }

    private sealed class FakeParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _items = new();

        public override int Count => _items.Count;
        public override object SyncRoot => _items;

        public override int Add(object value) { _items.Add((DbParameter)value); return _items.Count - 1; }
        public override void AddRange(Array values) { foreach (var v in values) Add(v!); }
        public override void Clear() => _items.Clear();
        public override bool Contains(object value) => _items.Contains((DbParameter)value);
        public override bool Contains(string value) => IndexOf(value) >= 0;
        public override void CopyTo(Array array, int index) => ((System.Collections.ICollection)_items).CopyTo(array, index);
        public override System.Collections.IEnumerator GetEnumerator() => _items.GetEnumerator();
        public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);

        public override int IndexOf(string parameterName) =>
            _items.FindIndex(p => string.Equals(p.ParameterName, parameterName, StringComparison.OrdinalIgnoreCase));

        public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);
        public override void Remove(object value) => _items.Remove((DbParameter)value);
        public override void RemoveAt(int index) => _items.RemoveAt(index);
        public override void RemoveAt(string parameterName) => RemoveAt(IndexOf(parameterName));

        protected override DbParameter GetParameter(int index) => _items[index];
        protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];
        protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value) => _items[IndexOf(parameterName)] = value;
    }

    /// <summary>메인PC 줄 한 개(또는 없음)를 <c>DeviceId</c>·<c>SealedKey</c> 두 칸으로 내놓는다.</summary>
    private sealed class FakeReader(FakeDevicesDb.Row? row) : DbDataReader
    {
        private bool _read;

        public override int FieldCount => 2;
        public override bool HasRows => row is not null;
        public override bool IsClosed => false;
        public override int RecordsAffected => 0;
        public override int Depth => 0;

        public override bool Read()
        {
            if (row is null || _read) return false;
            _read = true;
            return true;
        }

        public override Task<bool> ReadAsync(CancellationToken ct) => Task.FromResult(Read());
        public override bool NextResult() => false;

        public override string GetName(int ordinal) => ordinal == 0 ? "DeviceId" : "SealedKey";
        public override int GetOrdinal(string name) =>
            string.Equals(name, "DeviceId", StringComparison.OrdinalIgnoreCase) ? 0 : 1;

        public override Type GetFieldType(int ordinal) => ordinal == 0 ? typeof(string) : typeof(byte[]);
        public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;

        public override object GetValue(int ordinal) => (ordinal == 0 ? (object?)row!.DeviceId : row!.SealedKey) ?? DBNull.Value;
        public override bool IsDBNull(int ordinal) => GetValue(ordinal) is DBNull;
        public override int GetValues(object[] values)
        {
            values[0] = GetValue(0);
            if (values.Length > 1) values[1] = GetValue(1);
            return Math.Min(2, values.Length);
        }

        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));

        public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
        public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
        {
            var src = (byte[])GetValue(ordinal);
            if (buffer is null) return src.Length;
            var n = Math.Min(length, src.Length - (int)dataOffset);
            Array.Copy(src, dataOffset, buffer, bufferOffset, n);
            return n;
        }

        public override char GetChar(int ordinal) => (char)GetValue(ordinal);
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
        public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
        public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
        public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
        public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
        public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
        public override short GetInt16(int ordinal) => (short)GetValue(ordinal);
        public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
        public override long GetInt64(int ordinal) => (long)GetValue(ordinal);
        public override string GetString(int ordinal) => (string)GetValue(ordinal);
        public override System.Collections.IEnumerator GetEnumerator() => throw new NotSupportedException();
    }

    /// <summary>레포 원본 파일을 읽는다 — <c>.razor</c> 는 컴파일 뒤 이름이 사라지기 때문이다.</summary>
    private static string ReadRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "HitPan.Web")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, relative.Replace('/', Path.DirectorySeparatorChar)));
    }
}
