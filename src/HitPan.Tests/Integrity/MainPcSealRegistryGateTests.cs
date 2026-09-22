using System.Security.Cryptography;
using Dapper;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using HitPan.Application.Services.Security;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-11a · G-14 · G-15 · G-16 · G-18 · G-19a</b> —
/// 메인PC <b>봉인키와 기기 줄</b>이 DB 에서 실제로 어떻게 움직이는가 (20260922작3 · 설계 §8).
/// </summary>
/// <remarks>
/// <para>
/// 🟢 <b>초록불이 어디서 오나</b> — 격리 DB 에 <b>출하 DDL</b>(<c>installer/hitpan_db_clean.sql</c> · #36)을
/// 한 방 넣고 <b>실제 <see cref="MainPcProofService"/></b> 를 불러 <b>표를 읽는다.</b>
/// 글자를 안 본다. 반환값만으로 판정하지 않는다 — <c>tenant_devices</c> 를 직접 세어 본다.
/// </para>
///
/// <para>
/// 🔴 <b>봉인은 가짜로 한다 — 이유가 있다.</b>
/// 실물 <see cref="TpmKeyService.SealKey"/> 는
/// <c>CngKey.Create(..., "HitPan.TaxInvoice.MasterKey", OverwriteExistingKey)</c> 다.
/// ⇒ <b>부르면 이 PC 의 그 TPM 키를 덮어쓴다.</b> 이 PC 는 정식설치 실물이다(#39 · 인계3 §7-6).
/// 그래서 <c>LocalSealTpm</c>(같은 PC 에서만 풀리는 표식 방식)으로 <b>대역</b>을 세운다.
/// ⇒ 🔴 <b>실물 TPM 봉인 경로는 이 시험으로 단 한 번도 돌지 않는다.</b> 그것은 실물 실측(G-13b)이다.
/// </para>
///
/// <para>
/// ⚠️ <b>이 시험이 못 하는 것</b> — 화면·팝업·브라우저는 전혀 못 잰다(G-11b·G-12·G-19b·G-20).
/// 실제 백업/복원도 안 돌린다 — 재는 것은 <b>복원이 만들어 놓은 DB 상태의 전이</b>다.
/// 🔴 <b>여기가 전부 초록이어도 실물 실측(M-1)은 여전히 0회다.</b>
/// </para>
///
/// <para>
/// ⚠️ <b>운영 무접촉</b>(#39) — 임시 DB(<c>hitpan_mainpc_seal_*</c>)만 만들고 반드시 지운다.
/// ⚠️ MariaDB 가 없으면 <c>DbGateEnvironment.SkipOrFail</c> 로 <b>안 돌았다는 사실을 로그에 남긴다.</b>
/// CI(<c>HITPAN_REQUIRE_DB</c>)에서는 건너뛰는 것 자체가 실패다 — 초록불이 곧 안전이 아니다.
/// </para>
/// </remarks>
[Collection("DeviceAndKeyGate")]
public sealed class MainPcSealRegistryGateTests : IDisposable
{
    private readonly string _dbName = "hitpan_mainpc_seal_" + Guid.NewGuid().ToString("N")[..8];
    private bool _created;

    private readonly string _tenantId = Guid.NewGuid().ToString();
    private const string DeviceOld = "device-old-0000-0000-000000000001";
    private const string DeviceNew = "device-new-0000-0000-000000000002";

    // ══════════════════════════════════════════════════════════════
    // 준비물 — 형제 MainPcLocalConsoleGateTests 와 같은 방식
    // ══════════════════════════════════════════════════════════════

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }

        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 시험이 출하 DDL 을 읽을 수 없다.");
    }

    private static string ServerConnString()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root";
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
        return $"Server={host};Port={port};User={user};Password={pass};"
             + "DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;";
    }

    private string DbConnString() =>
        ServerConnString().Replace("User=", $"Database={_dbName};User=");

    private static string MysqlExe() =>
        Environment.GetEnvironmentVariable("HITPAN_MYSQL")
        ?? @"C:\Program Files\MariaDB 11.4\bin\mysql.exe";

    private static bool ServerAvailable()
    {
        if (DbGateEnvironment.IsCi) return true;   // CI 는 DB 필수 — 못 붙으면 아래에서 실패로 드러난다 (작14 W1)
        if (!File.Exists(MysqlExe())) return false;
        try
        {
            using var c = new MySqlConnection(ServerConnString());
            c.Open();
            return true;
        }
        catch (MySqlException)
        {
            return false;
        }
    }

    /// <summary>🔴 신규 설치 그대로 — 빈 DB 에 출하 DDL 한 방(#36). 손 DDL 로 짜맞추지 않는다.</summary>
    private void SetUpFreshInstall()
    {
        var ddlPath = Path.Combine(RepoRoot(), "installer", "hitpan_db_clean.sql");
        Assert.True(File.Exists(ddlPath), $"출하 DDL 이 없다: {ddlPath}");

        using (var admin = new MySqlConnection(ServerConnString()))
        {
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{_dbName}`; "
                        + $"CREATE DATABASE `{_dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        }

        _created = true;

        var psi = new System.Diagnostics.ProcessStartInfo(MysqlExe())
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add($"--host={Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost"}");
        psi.ArgumentList.Add($"--port={Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306"}");
        psi.ArgumentList.Add($"-u{Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root"}");
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS");
        if (!string.IsNullOrEmpty(pass)) psi.ArgumentList.Add($"-p{pass}");
        psi.ArgumentList.Add(_dbName);

        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.StandardInput.Write(File.ReadAllText(ddlPath));
        proc.StandardInput.Close();
        var err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        Assert.True(proc.ExitCode == 0,
            $"출하 DDL import 가 실패했다 — 신규 설치가 같은 자리에서 죽는다:\n{err}");
    }

    public void Dispose()
    {
        if (!_created) return;
        try
        {
            using var admin = new MySqlConnection(ServerConnString());
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{_dbName}`;");
        }
        catch (MySqlException)
        {
            // 지우기 실패가 시험 결과를 뒤집으면 안 된다 — 이름에 표식이 있어 사람이 찾는다.
        }
    }

    // ── 봉인 대역 ────────────────────────────────────────────────

    /// <summary>
    /// 🔵 <b>봉인 대역</b> — "이 컴퓨터가 봉인한 것만 풀린다" 는 성질만 흉내 낸다.
    /// </summary>
    /// <remarks>
    /// 실물 TPM/DPAPI 를 쓸 수 없는 이유는 파일 머리 주석에 있다(#39 — 이 PC 의 TPM 키를 덮어쓴다).
    /// ⚠️ 그러므로 이 대역이 통과시키는 것은 <b>봉인의 암호학적 강도가 아니라 판정 흐름</b>이다.
    /// 강도는 <c>MainPcProofRoundTripGateTests.G13a</c>(실물 <c>UnsealKey</c>)와 실측 G-13b 의 몫이다.
    /// </remarks>
    private sealed class LocalSealTpm : ITpmKeyService
    {
        /// <summary>"이 PC 가 봉인했다" 표식. 다른 PC 의 봉인에는 이 표식이 없다.</summary>
        private const byte ThisPcMark = 0xA7;

        public bool IsTpmAvailable() => true;

        public byte[] SealKey(byte[] masterKey)
        {
            var sealedKey = new byte[masterKey.Length + 1];
            sealedKey[0] = ThisPcMark;
            masterKey.CopyTo(sealedKey, 1);
            return sealedKey;
        }

        public byte[] UnsealKey(byte[] sealedKey)
        {
            if (sealedKey.Length < 2 || sealedKey[0] != ThisPcMark)
            {
                // 🔴 이것이 「컴퓨터가 바뀌었다」 신호다 — 오류가 아니라 정상 경로다.
                throw new CryptographicException("이 컴퓨터가 봉인한 키가 아니다.");
            }

            return sealedKey[1..];
        }

        public bool IsSealedKeyValid(byte[] sealedKey)
        {
            try
            {
                UnsealKey(sealedKey);
                return true;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }
    }

    private MainPcProofService NewService(MySqlConnection db) =>
        new(db, new LocalSealTpm(), NullLogger<MainPcProofService>.Instance);

    /// <summary>
    /// 실물 모양의 기기 줄 한 개. <c>user_id</c> 는 <b>NULL</b> 로 둔다 —
    /// <c>fk_device_user → users(user_id)</c> 가 걸려 있어 없는 사람을 적으면 DDL 이 막는다.
    /// </summary>
    private async Task SeedDeviceAsync(MySqlConnection db, string deviceId, bool isMainPc, byte[]? sealedKey)
    {
        await db.ExecuteAsync(
            @"INSERT INTO tenant_devices
                  (device_id, tenant_id, user_id, device_type, device_name, fingerprint,
                   status, registered_at, is_main_pc, mainpc_sealed_key, mainpc_key_issued_at)
              VALUES
                  (@DeviceId, @TenantId, NULL, 'pc', @DeviceName, @Fingerprint,
                   'approved', UTC_TIMESTAMP(6), @IsMainPc, @SealedKey,
                   CASE WHEN @SealedKey IS NULL THEN NULL ELSE UTC_TIMESTAMP(6) END)",
            new
            {
                DeviceId = deviceId,
                TenantId = _tenantId,
                DeviceName = deviceId,
                Fingerprint = "MAINPC-" + deviceId,
                IsMainPc = isMainPc ? 1 : 0,
                SealedKey = sealedKey
            });
    }

    private Task<int> MainPcRowCountAsync(MySqlConnection db) =>
        db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM tenant_devices WHERE tenant_id = @TenantId AND is_main_pc = 1",
            new { TenantId = _tenantId });

    /// <summary>표를 하나 발급해 ②(로컬 직결)를 실제로 돌린다.</summary>
    private static Task<MainPcProofOutcome> RoundTripAsync(MainPcProofService svc, string tenantId)
    {
        var challenge = svc.IssueChallenge(tenantId, "session-" + Guid.NewGuid().ToString("N"));
        return svc.ConfirmLocalAsync(challenge, CancellationToken.None);
    }

    private static byte[] ForeignSealedKey()
    {
        // 다른 PC 가 봉인한 것 — 표식이 없으므로 이 PC 에서 안 풀린다.
        var bytes = new byte[33];
        RandomNumberGenerator.Fill(bytes);
        bytes[0] = 0x00;
        return bytes;
    }

    // ══════════════════════════════════════════════════════════════
    // G-14 — 세 갈래를 정확히 가른다 (미등록 / 다른 PC / 등록된 그 PC)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-14 — 「컴퓨터가 바뀌었다」를 안다.</b> 봉인이 안 풀리는 것이 그 신호다.
    /// </summary>
    /// <remarks>
    /// 세 갈래가 <b>각각 다른 화면</b>으로 간다 — 미등록은 [등록] 팝업, 다른 PC 는 [변경] 팝업,
    /// 등록된 그 PC 는 자료관리 개방이다. 하나라도 섞이면 고객이 <b>갈 곳을 잃는다</b>(#20).
    /// ⇒ 키 없음 → <c>NotRegisteredYet</c> 갈래를 지우면 FAIL (대조군 C-13).
    /// </remarks>
    [Fact(DisplayName = "G-14 🔴 미등록·다른PC·등록된그PC 세 갈래를 정확히 가른다")]
    public async Task G14_세_갈래를_가른다()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(G14_세_갈래를_가른다)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        var svc = NewService(db);

        // ① 기기 줄이 아예 없다 — 새로 깐 컴퓨터
        Assert.Equal(MainPcProofOutcome.NotRegisteredYet, await RoundTripAsync(svc, _tenantId));

        // ② 옛 방식으로 is_main_pc 만 서 있고 봉인키가 없다 — 등록 절차를 거친 적이 없다
        await SeedDeviceAsync(db, DeviceOld, isMainPc: true, sealedKey: null);
        Assert.Equal(MainPcProofOutcome.NotRegisteredYet, await RoundTripAsync(svc, _tenantId));

        // ③ 봉인키가 있는데 이 컴퓨터에서 안 풀린다 — 컴퓨터가 바뀐 것이다
        await db.ExecuteAsync(
            "UPDATE tenant_devices SET mainpc_sealed_key = @Key WHERE device_id = @DeviceId",
            new { Key = ForeignSealedKey(), DeviceId = DeviceOld });

        Assert.Equal(MainPcProofOutcome.DifferentPc, await RoundTripAsync(svc, _tenantId));
    }

    // ══════════════════════════════════════════════════════════════
    // G-11a · G-15 · G-16 — 등록하면 알아본다 · 줄은 늘지 않는다 · 옛 PC 가 죽어도 된다
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-15 — 변경은 줄을 늘리지 않는다.</b> 2026-09-13 사장님 실측 사고와 같은 자리다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>G-16 을 함께 잰다</b> — 옛 PC 는 <b>꺼져 있다고 본다.</b> 이 시험은 옛 PC 와
    /// 한 마디도 주고받지 않는다. 그래도 이전이 되는 것이 설계다(고장 난 컴퓨터에서 옮겨야 하므로).
    /// </para>
    /// <para>
    /// 🔴 <b>G-11a 도 함께 잰다</b> — 등록 뒤 같은 왕복이 <c>MainPcConfirmed</c> 를 내면
    /// 팝업 조건이 꺼진다(팝업은 다시 안 뜬다 · 결재 D-7).
    /// </para>
    /// ⇒ 옛 줄 내리기 UPDATE 를 지우면 FAIL (대조군 C-10) · <c>rows == 0</c> 검사를 지우면 FAIL (C-11).
    /// </remarks>
    [Fact(DisplayName = "G-15·G-16·G-11a 🔴 메인PC 를 옮겨도 줄은 1개 · 옛 PC 없이 된다 · 그 뒤 알아본다")]
    public async Task G15_G16_G11a_이전해도_줄은_하나다()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(G15_G16_G11a_이전해도_줄은_하나다)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        var svc = NewService(db);

        // 옛 메인PC — 남의 봉인키를 들고 있다(백업 복원으로 따라 들어온 모양)
        await SeedDeviceAsync(db, DeviceOld, isMainPc: true, sealedKey: ForeignSealedKey());
        // 새 컴퓨터 — 아직 메인PC 가 아니다
        await SeedDeviceAsync(db, DeviceNew, isMainPc: false, sealedKey: null);

        Assert.Equal(1, await MainPcRowCountAsync(db));

        var registered = await svc.RegisterThisPcAsync(_tenantId, DeviceNew, CancellationToken.None);
        Assert.True(registered, "등록이 실패했다 — 옛 PC 가 꺼져 있으면 메인PC 를 영영 못 옮긴다는 뜻이다.");

        // 🔴 반환값만 믿지 않는다 — 표를 직접 센다.
        Assert.Equal(1, await MainPcRowCountAsync(db));

        var mainDevice = await db.ExecuteScalarAsync<string>(
            "SELECT device_id FROM tenant_devices WHERE tenant_id = @TenantId AND is_main_pc = 1",
            new { TenantId = _tenantId });
        Assert.Equal(DeviceNew, mainDevice);

        // G-11a — 이제 같은 왕복이 이 컴퓨터를 알아본다
        Assert.Equal(MainPcProofOutcome.MainPcConfirmed, await RoundTripAsync(svc, _tenantId));
    }

    /// <summary>
    /// 🔴 <b>G-16b — 없는 기기로는 등록되지 않는다.</b> 안 바뀐 것을 성공으로 보고하지 않는다.
    /// </summary>
    /// <remarks>⇒ <c>rows == 0 → false</c> 를 지우면 FAIL (대조군 C-11).</remarks>
    [Fact(DisplayName = "G-16b 🔴 없는 기기로 등록하면 실패로 답한다 (조용히 성공 금지)")]
    public async Task G16b_없는_기기는_등록되지_않는다()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(G16b_없는_기기는_등록되지_않는다)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        var svc = NewService(db);

        var registered = await svc.RegisterThisPcAsync(_tenantId, "그런-기기-없음", CancellationToken.None);

        Assert.False(registered, "없는 기기를 등록했다고 답했다 — 화면은 끝난 줄 알고 넘어간다.");
        Assert.Equal(0, await MainPcRowCountAsync(db));
    }

    // ══════════════════════════════════════════════════════════════
    // G-19a — 복원 → 봉인 불일치 → 재등록. 끊기지 않는다
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-19a — 팝업이 두 번 뜨는 것은 정상이다.</b> 그 전이가 끊기지 않는지 잰다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 새 PC 설치(빈 DB) → [등록] 1회차 → 자료복구 → <b>복원이 옛 봉인키를 되살린다</b> →
    /// 봉인 불일치 → [변경] 2회차 → 재등록. 인계3 §6-3 —
    /// <i>"왜 두 번 뜨지?" 하고 2회차를 없애면 자동 감지가 통째로 죽는다.</i>
    /// </para>
    /// <para>
    /// ⚠️ <b>실제 백업/복원을 돌리지 않는다</b>(G-19b). 재는 것은 <b>복원이 만들어 놓은 상태의 전이</b>다.
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "G-19a 🔴 복원 → 봉인 불일치 → 재등록까지 전이가 끊기지 않는다")]
    public async Task G19a_복원후_재등록까지_끊기지_않는다()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(G19a_복원후_재등록까지_끊기지_않는다)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        var svc = NewService(db);

        // ① 새 PC 에서 등록 (1회차)
        await SeedDeviceAsync(db, DeviceNew, isMainPc: false, sealedKey: null);
        Assert.Equal(MainPcProofOutcome.NotRegisteredYet, await RoundTripAsync(svc, _tenantId));
        Assert.True(await svc.RegisterThisPcAsync(_tenantId, DeviceNew, CancellationToken.None));
        Assert.Equal(MainPcProofOutcome.MainPcConfirmed, await RoundTripAsync(svc, _tenantId));

        // ② 자료 복구 — 복원이 옛 DB 를 덮어써 **옛 봉인키가 되살아난다**
        await db.ExecuteAsync(
            "UPDATE tenant_devices SET mainpc_sealed_key = @Key WHERE device_id = @DeviceId",
            new { Key = ForeignSealedKey(), DeviceId = DeviceNew });

        // ③ 봉인이 안 풀린다 → [변경] 팝업으로 간다 (2회차 · 정상)
        Assert.Equal(MainPcProofOutcome.DifferentPc, await RoundTripAsync(svc, _tenantId));

        // ④ 재등록하면 다시 알아본다 — 여기서 끊기면 고객이 자료관리에 영영 못 들어간다
        Assert.True(await svc.RegisterThisPcAsync(_tenantId, DeviceNew, CancellationToken.None));
        Assert.Equal(MainPcProofOutcome.MainPcConfirmed, await RoundTripAsync(svc, _tenantId));
        Assert.Equal(1, await MainPcRowCountAsync(db));
    }

    // ══════════════════════════════════════════════════════════════
    // G-18 — 빈 DB 면 복구를 안내한다
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔵 <b>G-18 — 자료가 비었는지 정확히 가른다</b> (사장님 결재 D-11).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 새 컴퓨터에 히트판을 깔면 DB 가 비어 있다. 그때 아무 말도 없으면 고객은
    /// <b>빈 화면을 보고 자료가 날아간 줄 안다.</b> 갈 곳을 알려 주지 않는 안내는 흐름이 끊긴 것이다(#20).
    /// </para>
    /// <para>
    /// 🔴 <b>한쪽만 보면 안 된다</b> — 상품만 쓰는 회사도, 거래처만 먼저 넣는 회사도 있다.
    /// ⇒ 한쪽만 세도록 바꾸면 FAIL (대조군 C-12).
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "G-18 🔵 거래처·상품이 둘 다 0 일 때만 「자료 없음」으로 본다")]
    public async Task G18_둘_다_비었을_때만_비었다고_본다()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(G18_둘_다_비었을_때만_비었다고_본다)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        var svc = NewService(db);

        // ① 아무것도 없다 → 복구를 안내해야 한다
        Assert.True(await svc.IsDataEmptyAsync(_tenantId, CancellationToken.None),
            "빈 DB 인데 「자료 있음」으로 봤다 — 고객이 빈 화면만 보고 자료가 날아간 줄 안다.");

        // ② 거래처만 있다 → 비어 있지 않다
        await db.ExecuteAsync(
            @"INSERT INTO partners
                  (partner_id, tenant_id, partner_code, partner_name, partner_type,
                   is_active, created_at, updated_at)
              VALUES (@Id, @TenantId, 'P001', '거래처하나', 'customer',
                      1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))",
            new { Id = Guid.NewGuid().ToString(), TenantId = _tenantId });

        Assert.False(await svc.IsDataEmptyAsync(_tenantId, CancellationToken.None),
            "거래처가 있는데 「자료 없음」으로 봤다 — 복구하라는 안내가 잘못 뜬다.");

        // ③ 상품만 있다 → 역시 비어 있지 않다
        await db.ExecuteAsync("DELETE FROM partners WHERE tenant_id = @TenantId",
            new { TenantId = _tenantId });
        await db.ExecuteAsync(
            @"INSERT INTO items
                  (item_id, tenant_id, item_code, item_name, item_type, unit,
                   is_active, created_at, updated_at)
              VALUES (@Id, @TenantId, 'I001', '상품하나', 'product', 'EA',
                      1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))",
            new { Id = Guid.NewGuid().ToString(), TenantId = _tenantId });

        Assert.False(await svc.IsDataEmptyAsync(_tenantId, CancellationToken.None),
            "상품이 있는데 「자료 없음」으로 봤다 — 상품만 쓰는 회사에 잘못된 안내가 간다.");

        // ④ 🔴 다른 회사의 자료는 세지 않는다 (#2 계통 — 회사 경계)
        await db.ExecuteAsync(
            @"INSERT INTO items
                  (item_id, tenant_id, item_code, item_name, item_type, unit,
                   is_active, created_at, updated_at)
              VALUES (@Id, @Other, 'I001', '남의상품', 'product', 'EA',
                      1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))",
            new { Id = Guid.NewGuid().ToString(), Other = Guid.NewGuid().ToString() });

        await db.ExecuteAsync("DELETE FROM items WHERE tenant_id = @TenantId",
            new { TenantId = _tenantId });

        Assert.True(await svc.IsDataEmptyAsync(_tenantId, CancellationToken.None),
            "남의 회사 자료를 우리 것으로 셌다 — 회사 경계가 새고 있다.");
    }
}
