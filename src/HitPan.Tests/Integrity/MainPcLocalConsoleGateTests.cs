using Dapper;
using HitPan.Application.DTOs.Device;
using HitPan.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-40 ~ G-44</b> — 메인PC 가 <b>자기 컴퓨터에서</b> 막히지 않는다 (20260818작4).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>왜 생겼나</b> — 2026-08-18 사장님 실측:
/// <i>"모바일, 외부클라이언트 인증번호 요청화면으로 막힌건 ok 봉합됨. 하지만 <b>메인pc도 막힘</b>"</i>
/// </para>
///
/// <para>
/// 🔴 <b>무엇이 진짜 원인이었나</b> — 메인PC 는 <b>반드시 두 줄</b>이 된다.
/// 서버는 <c>MAINPC-…</c>(컴퓨터 이름), 브라우저는 <c>HFPv2-…</c>(userAgent) 로
/// <b>서로 만들 수 없는 지문</b>을 쓴다 — <c>MainPcRegistrationService.BuildServerFingerprint</c> 가
/// <i>"네임스페이스가 겹치면 안 된다"</i> 며 <b>일부러</b> 갈라 놨다.
/// 그런데 <c>is_main_pc</c> 표식은 <b>서버 지문 줄에만</b> 붙는다.
/// ⇒ 사장님이 <b>그 컴퓨터에 앉아 계신데</b> 브라우저 줄은 <c>is_main_pc=0 · pending</c> 이라 막혔고,
/// 승인할 수 있는 유일한 사람이 <b>자기가 갇혔다.</b>
/// </para>
///
/// <para>
/// 🔴 <b>세 번째 재발이다.</b> 8/11 <c>revoked</c> 구제 · 8/16 P0(커밋 <c>30e3873</c>) · 오늘.
/// 매번 <b>"막히면 되살릴 화면에 못 들어간다"</b> 라는 같은 모양이었고, 매번 <b>다른 자리</b>였다.
/// ⇒ 그래서 이 게이트는 <b>증상이 아니라 그 축</b>을 지킨다.
/// </para>
///
/// <para>
/// 🔴 <b>8/16 봉합이 왜 이걸 못 막았나</b> — 그때 장비넘버를 1순위 열쇠로 올린 것은
/// <b>브라우저끼리 갈리는 것</b>(Edge↔Chrome)만 막았다.
/// <c>is_main_pc</c> 가 서버 지문 줄에만 붙는다는 사실은 <b>손대지 않았다.</b>
/// ⇒ <b>고쳤는데 안 갔다</b> 계통이다([[project_fixed_vs_delivered_gap]]).
/// </para>
///
/// <para>
/// 🟢 <b>초록불이 어디서 오나</b> — 격리 DB 에 <b>출하 DDL</b> 을 넣고
/// <b>실제 <see cref="TenantDeviceService"/></b> 를 불러 <b>표를 읽는다.</b>
/// 글자를 안 본다. 반환값도 판정 근거로 쓰지 않는다(함수가 정하는 값이다 — 8/18 가짜게이트 교훈).
/// </para>
///
/// <para>
/// ⚠️ <b>운영 무접촉</b>(헌법 #39) — 임시 DB(<c>hitpan_mainpc_gate_*</c>)만 만들고 반드시 지운다.
/// ⚠️ MariaDB 가 없으면 건너뛴다. <b>그 환경에서 이 게이트는 아무것도 검사하지 않는다</b> —
/// 초록불이 곧 안전이 아니다.
/// </para>
/// </remarks>
[Collection("DeviceAndKeyGate")]
public sealed class MainPcLocalConsoleGateTests : IDisposable
{
    private readonly string _dbName = "hitpan_mainpc_gate_" + Guid.NewGuid().ToString("N")[..8];
    private bool _created;

    private const string TenantId = "11111111-1111-1111-1111-111111111111";
    private const string UserId = "22222222-2222-2222-2222-222222222222";

    // ══════════════════════════════════════════════════════════════
    // 준비물 — DeviceAndKeyGateTests 와 같은 방식(격리 DB · 출하 DDL · 실제 서비스)
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

    /// <summary>🔴 신규 설치 그대로 — 빈 DB 에 출하 DDL 한 방(헌법 #36).</summary>
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

        SeedLoginUser();
    }

    /// <summary>
    /// 🔴 로그인하는 사람 한 줄을 심는다.
    ///
    /// <para>
    /// <c>tenant_devices.user_id</c> 에 <c>fk_device_user → users(user_id)</c> 가 걸려 있다.
    /// 없는 사람으로 기기를 등록하면 <b>DDL 이 막는다</b> — 실물에서는 로그인한 사람이 이미 있으므로
    /// 그 상태를 그대로 재현하는 것이다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ 이 자리를 <b>대충 우회하면</b>(예: <c>user_id</c> 를 NULL 로 넣게 시험을 바꾸면)
    /// 시험이 <b>실물과 다른 경로</b>를 검사하게 된다. 실물은 반드시 사람과 함께 온다.
    /// </para>
    /// </summary>
    private void SeedLoginUser()
    {
        using var db = new MySqlConnection(DbConnString());
        db.Open();

        // 컬럼 구성은 출하 DDL 을 따른다 — 필수값만 채운다.
        db.Execute(
            """
            INSERT INTO users
              (user_id, tenant_id, email, password_hash, user_name, role,
               account_type, is_active, created_at, updated_at)
            VALUES
              (@Uid, @Tid, 'gate-tester@hitpan.kr', 'x', '게이트시험자', 'tenant_admin',
               'tenant_admin', 1, NOW(6), NOW(6))
            """,
            new { Uid = UserId, Tid = TenantId });
    }

    /// <summary>🔴 실제 서비스다 — 흉내가 아니다. 승인제는 <b>켠 채로</b> 시험한다(그래야 막히는 자리가 산다).</summary>
    private TenantDeviceService NewService(MySqlConnection db, bool approvalEnabled = true)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DeviceApproval:Enabled"] = approvalEnabled ? "true" : "false"
            })
            .Build();

        return new TenantDeviceService(
            db, new NoOpAudit(), config, NullLogger<TenantDeviceService>.Instance);
    }

    private sealed class NoOpAudit : HitPan.Application.Interfaces.IAuditService
    {
        public Task LogAsync(string actionType, string entityType, string? entityId = null,
            string? beforeJson = null, string? afterJson = null, string? reason = null,
            System.Data.IDbTransaction? tx = null,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>
    /// 🔴 <b>서버가 만든 메인PC 줄</b>을 그대로 재현한다.
    ///
    /// <para>
    /// <c>MainPcRegistrationService</c> 가 실제로 넣는 모양 그대로다 —
    /// 지문 <c>MAINPC-…</c>, <c>is_main_pc=1</c>, <c>status='approved'</c>, <c>user_id=NULL</c>.
    /// ⚠️ 이 모양을 바꾸면 시험이 <b>실물과 다른 것</b>을 검사하게 된다.
    /// </para>
    /// </summary>
    private static Task InsertServerMainPcRowAsync(MySqlConnection db, string deviceId) =>
        db.ExecuteAsync(
            """
            INSERT INTO tenant_devices
              (device_id, tenant_id, user_id, device_type, device_name, fingerprint,
               ip_address, status, is_main_pc, registered_at, approved_at, last_seen_at)
            VALUES
              (@Id, @Tid, NULL, 'pc', '메인PC', @Fp,
               NULL, 'approved', 1, NOW(6), NOW(6), NOW(6))
            """,
            new { Id = deviceId, Tid = TenantId, Fp = "MAINPC-" + Guid.NewGuid().ToString("N")[..16] });

    /// <summary>🔴 <b>표를 읽는다.</b> 반환값이 아니라 표가 사실이다(8/18 가짜게이트 교훈).</summary>
    private static async Task<(string status, bool isMainPc)?> ReadRowAsync(MySqlConnection db, string deviceId)
    {
        var row = await db.QueryFirstOrDefaultAsync<(string status, bool isMainPc)?>(
            "SELECT status AS status, COALESCE(is_main_pc,0) AS isMainPc FROM tenant_devices WHERE device_id = @Id",
            new { Id = deviceId });
        return row;
    }

    /// <summary>브라우저에서 온 로그인 한 번. <paramref name="local"/> 이 <b>그 컴퓨터에서 열었나</b>이다.</summary>
    private static RegisterDeviceRequest BrowserLogin(bool local, string? deviceId = null) => new()
    {
        Fingerprint = "HFPv2-" + Guid.NewGuid().ToString("N")[..16],
        DeviceType = "pc",
        DeviceName = "브라우저화면",
        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120",
        DeviceId = deviceId,
        IsLocalConsole = local
    };

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

    // ══════════════════════════════════════════════════════════════
    // G-40 — 그 컴퓨터의 첫 화면은 막히지 않는다  🔴 본체
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-40. 서버가 도는 컴퓨터에서 처음 연 화면은 승인 대기에 갇히지 않는다.</b>
    ///
    /// <para>
    /// [이것이 사장님 증상이다] 설치 직후 대표가 자기 컴퓨터에서 로그인했는데
    /// <b>승인 대기 화면</b>이 떴다. 승인해 줄 사람이 자기인데 그 화면에 못 들어간다.
    /// </para>
    ///
    /// <para>
    /// [반증] <c>TenantDeviceService</c> 의 <c>newStatus</c> 에서
    /// <c>&amp;&amp; !req.IsLocalConsole</c> 을 빼면 FAIL.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "G-40 🔴 서버가 도는 컴퓨터의 첫 화면은 승인 대기에 갇히지 않는다")]
    public async Task GC40_그컴퓨터_첫화면은_안갇힌다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        var svc = NewService(db, approvalEnabled: true);

        var (allowed, _, deviceId, _) = await svc.RegisterOrRefreshAsync(
            TenantId, UserId, BrowserLogin(local: true), "127.0.0.1");

        Assert.NotNull(deviceId);

        // 🔴 표를 본다 — 반환값이 아니라.
        var row = await ReadRowAsync(db, deviceId!);
        Assert.True(row is not null, "그 줄이 표에 없다 — 등록 자체가 안 됐다.");

        Assert.True(
            row!.Value.status == "approved",
            $"🔴 서버가 도는 그 컴퓨터에서 연 첫 화면이 '{row.Value.status}' 다 — **대표가 자기 화면에서 갇힌다.** " +
            "승인해 줄 수 있는 유일한 사람이 승인 화면에 못 들어간다(2026-08-18 사장님 실측 · 8/16 P0 재발).");

        // 🔴 화면이 읽는 값도 함께 봐야 한다 — 표는 approved 인데 화면에 "대기" 라 답하면
        //   결국 관문에 갇힌다. 두 값이 갈리는 것이 8/18 증상의 정체였다.
        Assert.True(allowed,
            "🔴 표는 승인인데 **화면에는 '승인 대기' 라고 답했다** — 관문이 그 답을 보고 막는다. " +
            "표와 답이 갈리면 고친 것이 아니다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-41 — 서버 줄과 화면 줄이 갈려도 그 컴퓨터는 열린다  🔴 진짜 원인
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-41. 서버가 만든 메인PC 줄이 이미 있어도, 그 컴퓨터의 화면 줄이 메인PC 로 인정된다.</b>
    ///
    /// <para>
    /// [이것이 진짜 원인이다] 두 지문은 <b>서로 만들 수 없어</b> 반드시 두 줄이 된다.
    /// 표식이 서버 줄에만 있으면 화면 줄은 영원히 남의 기기다.
    /// </para>
    ///
    /// <para>[반증] 갱신 경로의 <c>if (req.IsLocalConsole &amp;&amp; !isMainPc)</c> 갈래를 지우면 FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-41 🔴 서버 줄이 따로 있어도 그 컴퓨터의 화면 줄이 메인PC 로 인정된다")]
    public async Task GC41_화면줄이_메인PC로_인정된다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        // ① 서버가 자기 지문으로 메인PC 줄을 먼저 만들어 둔 상태 — 실제 기동 순서 그대로다.
        var serverRow = Guid.NewGuid().ToString();
        await InsertServerMainPcRowAsync(db, serverRow);

        var svc = NewService(db, approvalEnabled: true);

        // ② 그 컴퓨터에서 브라우저로 로그인 — 지문이 달라 **새 줄**이 된다.
        var first = await svc.RegisterOrRefreshAsync(
            TenantId, UserId, BrowserLogin(local: true), "127.0.0.1");
        Assert.NotNull(first.deviceId);

        // ③ 같은 기기가 다시 온다(=평소 두 번째 접속). 여기서 표식이 제자리를 찾아야 한다.
        var again = BrowserLogin(local: true, deviceId: first.deviceId);
        await svc.RegisterOrRefreshAsync(TenantId, UserId, again, "127.0.0.1");

        var row = await ReadRowAsync(db, first.deviceId!);
        Assert.True(row is not null, "화면 줄이 사라졌다.");

        Assert.True(
            row!.Value.isMainPc,
            "🔴 그 컴퓨터에서 직접 연 화면인데 **메인PC 로 인정되지 않았다.** " +
            "표식이 서버 지문 줄에만 남아 있으면 화면 줄은 영원히 남의 기기다 — " +
            "미들웨어의 메인PC 구제책(is_main_pc=1)이 그 줄을 못 알아본다.");

        Assert.True(
            row.Value.status == "approved",
            $"🔴 메인PC 표식은 붙었는데 상태가 '{row.Value.status}' 다 — 표식만 있고 여전히 막힌다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-42 — 슬롯이 두 번 새지 않는다  🔴 요금
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-42. 표식을 옮겨도 <c>is_main_pc=1</c> 은 <b>한 줄</b>뿐이고, 슬롯이 두 번 세어지지 않는다.</b>
    ///
    /// <para>
    /// [무엇이 무서운가] 화면 줄을 승인하면서 서버 줄을 그대로 두면
    /// <b>같은 컴퓨터가 슬롯 2개</b>를 먹는다. 히트판은 기기 수로 요금을 매긴다 —
    /// 고객이 <b>쓰지도 않은 자리에 돈을 낸다.</b>
    /// </para>
    ///
    /// <para>[반증] 갱신 경로 ①(옛 서버 줄 내리는 UPDATE)을 지우면 FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-42 🔴 표식을 옮겨도 메인PC 는 한 줄이고 슬롯이 두 번 안 세어진다")]
    public async Task GC42_슬롯이_두번_안세어진다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var serverRow = Guid.NewGuid().ToString();
        await InsertServerMainPcRowAsync(db, serverRow);

        var svc = NewService(db, approvalEnabled: true);

        var first = await svc.RegisterOrRefreshAsync(
            TenantId, UserId, BrowserLogin(local: true), "127.0.0.1");
        await svc.RegisterOrRefreshAsync(
            TenantId, UserId, BrowserLogin(local: true, deviceId: first.deviceId), "127.0.0.1");

        var mainCount = await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM tenant_devices WHERE tenant_id=@Tid AND is_main_pc=1",
            new { Tid = TenantId });

        Assert.True(mainCount == 1,
            $"🔴 메인PC 표식이 {mainCount} 줄이다 — 한 줄이어야 한다. " +
            "둘이 되면 어느 것이 그 컴퓨터인지 CS 가 식별할 수 없다.");

        // 🔴 슬롯 계수는 approved 로 센다 — 옛 서버 줄이 approved 로 남으면 그대로 요금이 샌다.
        var approvedCount = await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM tenant_devices WHERE tenant_id=@Tid AND status='approved'",
            new { Tid = TenantId });

        Assert.True(approvedCount == 1,
            $"🔴 같은 컴퓨터가 승인 줄 {approvedCount} 개를 차지한다 — **슬롯이 두 번 세어져 요금이 샌다.** " +
            "표식을 옮길 때 옛 서버 줄을 내리지 않은 것이다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-43 — 바깥에서는 절대 메인PC 가 못 된다  🔴 보안 본체
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-43. 그 컴퓨터가 아닌 곳에서 온 접속은 메인PC 가 되지 못하고, 종전대로 승인 대기다.</b>
    ///
    /// <para>
    /// [왜 이것이 본체인가] 이 봉합은 <b>문을 하나 여는 일</b>이다.
    /// 그 문이 바깥으로도 열리면 <b>아무나 메인PC 를 자칭</b>해 승인제 전체가 무너진다.
    /// ⇒ 봉합의 값어치는 <b>열린 쪽</b>이 아니라 <b>닫힌 쪽</b>이 증명한다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>정직하게 적는다</b> — 이 시험은 <c>IsLocalConsole=false</c> 일 때 안 열리는 것만 본다.
    /// 그 값을 <b>서버가 제대로 채우는지</b>(터널 헤더 배제)는 <c>AuthController</c> 몫이고
    /// <b>G-44</b> 가 그 자리를 따로 지킨다.
    /// </para>
    ///
    /// <para>[반증] <c>req.IsLocalConsole</c> 조건을 <c>true</c> 로 바꾸면 FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-43 🔴 바깥에서 온 접속은 메인PC 가 되지 못하고 승인 대기로 남는다")]
    public async Task GC43_바깥에서는_메인PC가_못된다()
    {
        if (!ServerAvailable()) return;
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var serverRow = Guid.NewGuid().ToString();
        await InsertServerMainPcRowAsync(db, serverRow);

        var svc = NewService(db, approvalEnabled: true);

        // 바깥(터널·다른 기기)에서 온 접속 — 두 번 와도 표식이 붙으면 안 된다.
        var first = await svc.RegisterOrRefreshAsync(
            TenantId, UserId, BrowserLogin(local: false), "10.0.0.5");
        Assert.NotNull(first.deviceId);

        await svc.RegisterOrRefreshAsync(
            TenantId, UserId, BrowserLogin(local: false, deviceId: first.deviceId), "10.0.0.5");

        var row = await ReadRowAsync(db, first.deviceId!);
        Assert.True(row is not null, "그 줄이 표에 없다.");

        Assert.False(
            row!.Value.isMainPc,
            "🔴 **바깥에서 온 기기가 메인PC 표식을 가져갔다.** " +
            "이러면 아무나 메인PC 를 자칭해 승인제가 통째로 무너진다.");

        Assert.True(
            row.Value.status == "pending",
            $"🔴 바깥 기기가 '{row.Value.status}' 다 — 대표의 승인 없이 문이 열렸다. " +
            "이 봉합은 그 컴퓨터의 화면만 구하는 것이고, 바깥은 종전대로 대표가 문지기여야 한다.");

        // 🔴 서버 줄은 그대로 살아 있어야 한다 — 바깥 접속이 진짜 메인PC 를 끌어내리면 안 된다.
        var serverStill = await ReadRowAsync(db, serverRow);
        Assert.True(serverStill is not null && serverStill.Value.isMainPc,
            "🔴 바깥에서 온 접속이 **진짜 메인PC 줄을 끌어내렸다.** " +
            "그 컴퓨터가 자기 표식을 잃으면 다음에 자기가 막힌다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-44 — 그 판정을 클라이언트가 못 정한다  🔴 위조 차단
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-44. <c>IsLocalConsole</c> 은 서버가 채운다 — 요청 본문에서 읽지 않는다.</b>
    ///
    /// <para>
    /// [무엇을 막나] 로그인 요청 본문에 <c>"isLocalConsole": true</c> 를 적어 보내는 것만으로
    /// 메인PC 가 된다면 <b>봉합이 곧 뒷문</b>이다.
    /// ⇒ <c>AuthController</c> 가 그 값을 <b>소켓 주소와 터널 헤더</b>로 직접 만들어야 한다.
    /// </para>
    ///
    /// <para>
    /// 🔴 <b>2026-08-10 에 걷어낸 판정과 갈라야 한다.</b> 그때는 loopback 을
    /// <b>배제절 없이</b> 써서 <i>"고객사에서는 항상 참"</i> 이 됐다(터널이 안에서 localhost 를 다시 부른다).
    /// ⇒ 그래서 <b>터널 헤더 두 개를 함께 보는지</b>까지 확인한다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>한계를 정직하게 적는다</b> — 이것은 <b>소스에 그 배선이 있는가</b>를 보는 시험이다.
    /// 위 G-40~43 과 달리 값을 굽지 않는다(HTTP 파이프라인이 필요하다).
    /// 🔴 그래도 <b>없으면 반드시 FAIL</b> 하므로, 누가 <c>request</c> 에서 읽도록 바꾸면 그 자리에서 걸린다.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "G-44 🔴 메인PC 판정을 클라이언트가 못 정한다 (서버가 소켓·터널헤더로 채운다)")]
    public void GC44_클라이언트가_메인PC를_자칭못한다()
    {
        var auth = Path.Combine(RepoRoot(), "src", "HitPan.API", "Controllers", "AuthController.cs");
        Assert.True(File.Exists(auth), $"로그인 컨트롤러가 없다: {auth}");

        var text = File.ReadAllText(auth);

        var idx = text.IndexOf("IsLocalConsole", StringComparison.Ordinal);
        Assert.True(idx >= 0,
            "🔴 로그인 경로가 IsLocalConsole 을 아예 안 채운다 — 그러면 그 컴퓨터의 화면이 " +
            "**영원히 남의 기기**이고 대표가 자기 화면에서 갇힌다(2026-08-18 실측).");

        // 그 값을 만드는 식(대입 이후 한 문단)만 잘라 본다.
        var seg = text.Substring(idx, Math.Min(600, text.Length - idx));

        Assert.True(
            seg.Contains("IsLoopback", StringComparison.Ordinal),
            "🔴 IsLocalConsole 을 소켓 주소로 판정하지 않는다 — 무엇을 근거로 그 컴퓨터라고 하는가?");

        Assert.True(
            seg.Contains("CF-Connecting-IP", StringComparison.Ordinal)
            && seg.Contains("X-Forwarded-For", StringComparison.Ordinal),
            "🔴 **터널 헤더 배제절이 없다.** 이것이 빠지면 2026-08-10 에 걷어낸 그 판정 그대로다 — " +
            "터널이 고객 PC 안에서 localhost 를 다시 부르므로 *바깥에서 온 접속도 참*이 되어 " +
            "**모든 기기가 메인PC 가 된다.**");

        Assert.False(
            seg.Contains("request.IsLocalConsole", StringComparison.Ordinal),
            "🔴 **클라이언트가 보낸 값을 그대로 쓴다.** 요청 본문에 true 라 적어 보내면 메인PC 가 된다 — " +
            "봉합이 곧 뒷문이 된다.");
    }
}
