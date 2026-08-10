using System.Security.Cryptography;
using System.Text;
using Dapper;
using HitPan.Infrastructure.Configuration;
using MySqlConnector;

namespace HitPan.API.HostedServices;

/// <summary>
/// 메인PC(히트판 본체·DB 를 가진 그 PC)를 등록 기기 목록에 스스로 넣는다.
/// 작업지시서: docs/운영기록/20260810작3_메인PC_설치시_자동등록_작업지시서.md
///
/// ■ 왜 필요한가 — 원래 하기로 했는데 빠져 있었다
///   슬롯을 계정이 아니라 **등록 기기**로 세기로 처음부터 정했고, 메인PC 와 클라이언트를
///   구분하기로도 정해 두었다. 그런데 설치마법사에 tenant_devices 를 넣는 코드가 0건이었다.
///   그러면서 코드 주석과 고객 화면은 "설치 과정에서 함께 등록됩니다" 라고 적고 있었다.
///
/// ■ CS 축 (사장님 지적 2026-08-10)
///   고객이 "히트판이 안 돼요" 라고 전화하면 CS 는 먼저 물어야 한다 — 그 PC 가 메인PC 인가.
///     · 메인PC 면    → 본체·DB·터널이 그 PC 에 있다. 그 PC 를 살려야 한다
///     · 클라이언트면 → 본체는 멀쩡하고 메인PC 가 꺼졌을 수 있다
///   메인PC 는 **24시간 켜두기 좋은 PC** 로 본사가 추천하는 자리라, 대표자나 담당자가 쓰는
///   PC 라는 보장이 없다. 설치를 맡았던 직원이 퇴사하면 **아무도 어느 PC 인지 모른다.**
///   ⇒ 목록에 표식이 남아야 누구든 보고 안다.
///
/// ■ 🔴 왜 로그인이 아니라 서버 기동 시점인가
///   메인PC 는 창고 구석에서 무인으로 돌 수 있다 — **아무도 그 PC 에서 로그인하지 않을 수 있다.**
///   그리고 사장님: *"부모계정을 반드시 메인PC에서 쓴다는 보장이 없거든. 계정은 계정일 뿐이니까."*
///   계정 축(누가 로그인하나)과 기기 축(어느 컴퓨터인가)은 별개다.
///   ⇒ 등록을 로그인에 묶으면 **정상 상태가 등록 실패**가 된다. 서버가 스스로 등록한다.
///
/// ■ 🔴 왜 접속 경로(loopback·Host·XFF)로 판정하지 않는가 — 2026-08-10 두 번 무너진 축
///   · 터널(cloudflared)은 고객 PC 안에서 돌며 자기 PC 의 localhost 를 다시 부른다
///     (HitPan-Universal.iss:1134 ingress origin = http://localhost:5257)
///   · 히트판은 --urls http://127.0.0.1 로 loopback 에만 귀를 연다(같은 파일 :2359)
///     ⇒ 도달한 요청은 100% loopback. 판정이 **항상 참**이 된다
///   · 사내 다른 PC 경로(web-server.ps1:98)는 Host 를 버리고 X-Forwarded-For 를 안 붙인다
///     ⇒ 프록시 헤더로도 못 거른다
///   근거: docs/검증/병렬이슈/20260810_병렬이슈_09_작2봉합_독립반증.md
///   ⇒ 이 서비스는 접속 경로를 **아예 보지 않는다.**
///     "요청이 어디서 왔나" 가 아니라 **"내가 어디서 도나"** 를 본다. 이 프로세스는 메인PC 에만 있다.
///
/// ■ 🔴 기존 실측 성공분을 하나도 건드리지 않는다 (사장님 지시 2026-08-10)
///   *"기존의 마이그레이션·워치독·업데이트·설치연결 관련 이슈 등 건들지 않고, 슬롯관리만 추가로."*
///   *"기존의 실측 성공한 건 단 하나라도 건들면 안 돼."*
///   ⇒ 설치마법사(.iss) 무변경 · 워치독 무변경 · 로그인/인증 경로 무변경 ·
///     TenantDeviceService.RegisterOrRefreshAsync 무변경(한도·폐기 검사 그대로).
///     추가되는 것은 이 파일과 DB 컬럼(DB-86) 하나뿐이다.
///
/// ⚠️ 실패해도 API 를 죽이지 않는다. DB 가 아직 안 올라왔을 수 있고, 그때는 다음 기동에
///   다시 시도하면 된다(멱등). 여기서 죽이면 고객이 화면조차 못 본다(헌법 #30).
/// </summary>
public sealed class MainPcRegistrationService : BackgroundService
{
    private readonly ILogger<MainPcRegistrationService> _logger;

    /// <summary>메인PC 기기 이름. 고객·CS 가 목록에서 이것만 보고 알아볼 수 있어야 한다.</summary>
    private const string MainPcDeviceName = "회사 서버 (자료 보관 컴퓨터)";

    public MainPcRegistrationService(ILogger<MainPcRegistrationService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 🔴 판정이 필요 없다 — **이 코드가 돌고 있다는 것 자체가 메인PC 라는 증거다.**
        //
        //   설치마법사가 API 를 자기 PC 에 서비스로 등록하고(HitPan-Universal.iss:2359 schtasks),
        //   DB 도 같은 PC 에 둔다(:1825 DB_HOST=localhost, :1641 로컬 MariaDB 설치).
        //   클라이언트PC 에는 히트판이 설치되지 않는다 — 브라우저로 메인PC 에 접속할 뿐이다.
        //   ⇒ 이 프로세스가 존재하는 PC = 히트판 본체·DB 를 가진 PC = 메인PC.
        //
        //   ⭐ 그래서 설치마법사에 새 표시를 남길 필요가 없다. 사장님 지시(2026-08-10):
        //     *"기존의 마이그레이션·워치독·업데이트·설치연결 관련 이슈 등 건들지 않고,
        //       슬롯관리만 추가로 붙일 수 있는 방법을 써."*
        //     *"기존의 실측 성공한 건 단 하나라도 건들면 안 돼."*
        //   ⇒ .iss 무변경. 이 파일과 DB 컬럼 하나만 추가한다.
        //
        //   ⚠️ 접속 경로(loopback·Host·X-Forwarded-For)는 **보지 않는다.** 위 클래스 주석 참조 —
        //     그 축은 고객사에서 항상 참이 되어 2026-08-10 에 두 번 무너졌다.
        //     여기서 보는 것은 "요청이 어디서 왔나" 가 아니라 "내가 어디서 도나" 다.
        var connStr = BuildConnectionString();
        if (string.IsNullOrEmpty(connStr))
        {
            _logger.LogWarning("[MainPc] DB 설정 누락 → 메인PC 등록 비활성.");
            return;
        }

        // ⚠️ 마이그레이션(DB-86)은 Program.cs 기동 경로에서 app.Run() 전에 끝난다.
        //   BackgroundService 는 그 뒤에 도므로 is_main_pc 컬럼은 이미 존재한다.
        //   그래도 DB 기동이 늦는 경우가 있어 실패 시 재시도한다.
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts && !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                await RegisterAsync(connStr, stoppingToken);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                // 헌법 #15 — 빈 catch 금지.
                _logger.LogWarning(ex,
                    "[MainPc] 메인PC 등록 실패 ({Attempt}/{Max}) — 잠시 후 재시도.", attempt, maxAttempts);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10 * attempt), stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                // 마지막 시도까지 실패 — API 는 계속 돈다. 다음 기동에 다시 시도한다(멱등).
                _logger.LogError(ex,
                    "[MainPc] 메인PC 등록이 {Max}회 모두 실패했다. 다음 기동에 다시 시도한다. " +
                    "이 상태에서는 등록 기기 목록에 메인PC 표식이 없어 고객지원이 메인PC 를 식별하지 못한다.",
                    maxAttempts);
                return;
            }
        }
    }

    private async Task RegisterAsync(string connStr, CancellationToken ct)
    {
        await using var conn = new MySqlConnection(connStr);
        await conn.OpenAsync(ct);

        // 이 ERP 로컬 DB 의 테넌트는 하나다(고객 PC 로컬 구조 — 헌법 #30).
        var tenantId = await conn.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT tenant_id FROM local_company ORDER BY bootstrap_at LIMIT 1",
            cancellationToken: ct));

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            // 아직 회사 정보가 만들어지기 전이다(설치 직후 부트스트랩 이전).
            // 다음 기동에 다시 시도한다 — 그때는 있다.
            _logger.LogInformation("[MainPc] 회사 정보가 아직 없어 메인PC 등록을 미룬다.");
            return;
        }

        var fingerprint = BuildServerFingerprint();

        // ① 같은 지문이 이미 있나 — 재설치·업데이트로 이 서비스가 다시 도는 경우.
        //   지문이 MachineName 기반이라 재설치해도 같은 값이 나온다 ⇒ 중복 행이 생기지 않는다.
        var existingId = await conn.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT device_id FROM tenant_devices WHERE tenant_id = @TenantId AND fingerprint = @Fp LIMIT 1",
            new { TenantId = tenantId, Fp = fingerprint }, cancellationToken: ct));

        if (existingId is not null)
        {
            // 이미 있다 — 표식만 확실히 해 둔다(업데이트로 컬럼이 새로 생긴 기존 고객 대응).
            var updated = await conn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE tenant_devices
                SET is_main_pc   = 1,
                    device_name  = COALESCE(device_name, @Name),
                    last_seen_at = NOW()
                WHERE device_id = @Id AND is_main_pc = 0
                """,
                new { Id = existingId, Name = MainPcDeviceName }, cancellationToken: ct));

            if (updated > 0)
            {
                _logger.LogWarning("[MainPc] 기존 기기를 메인PC 로 표시했다 (device_id={Id}).", existingId);
            }
            return;
        }

        // ② 🔴 테넌트당 메인PC 는 1대다.
        //   MariaDB 에는 부분 UNIQUE 인덱스가 없어 DB 제약으로 못 건다(DB-86 주석 참조).
        //   ⇒ 여기서 보장한다. 이미 다른 메인PC 가 있으면 새로 만들지 않는다.
        //   ⚠️ 지문이 바뀌는 경우(컴퓨터 이름 변경 등)에 옛 메인PC 행이 남아 있을 수 있다.
        //     그때는 옛 행을 그대로 두고 새로 만들지 않는다 — 슬롯을 두 번 먹지 않기 위해서다.
        //     사람이 목록에서 옛 기기를 폐기하면 다음 기동에 새로 잡힌다.
        var alreadyHasMain = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM tenant_devices WHERE tenant_id = @TenantId AND is_main_pc = 1",
            new { TenantId = tenantId }, cancellationToken: ct));

        if (alreadyHasMain > 0)
        {
            _logger.LogInformation(
                "[MainPc] 이미 메인PC 가 등록돼 있어 새로 만들지 않는다 (컴퓨터 이름이 바뀌었을 수 있다). " +
                "바꾸려면 등록 기기 관리에서 옛 기기를 폐기한다.");
            return;
        }

        // ③ 신규 등록.
        //   ⚠️ 한도를 넘어도 이 등록은 막지 않는다. 이것은 로그인 차단이 아니라 **사실 기록**이다 —
        //     그 PC 는 실제로 존재하고 실제로 히트판을 돌리고 있다. 목록에 안 보이면 고객지원이
        //     메인PC 를 식별할 수 없다(이 서비스의 존재 이유).
        //   🔴 단, 사람 로그인 경로(TenantDeviceService.RegisterOrRefreshAsync)의 한도 검사는
        //     그대로 둔다. 거기를 열면 요금정책이 무너진다(병렬이슈 09 V9-1).
        var deviceId = Guid.NewGuid().ToString();
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO tenant_devices
              (device_id, tenant_id, user_id, device_type, device_name,
               fingerprint, ip_address, user_agent, status, is_main_pc,
               registered_at, approved_by, approved_at, last_seen_at)
            VALUES
              (@Id, @TenantId, NULL, 'pc', @Name,
               @Fp, NULL, NULL, 'approved', 1,
               NOW(), NULL, NOW(), NOW())
            """,
            new { Id = deviceId, TenantId = tenantId, Name = MainPcDeviceName, Fp = fingerprint },
            cancellationToken: ct));

        _logger.LogWarning(
            "[MainPc] 메인PC 를 등록 기기로 추가했다 (device_id={Id}). 슬롯 1대를 사용한다.", deviceId);
    }

    /// <summary>
    /// 서버 전용 기기 지문.
    ///
    /// 🔴 브라우저 지문(device-fingerprint.js 의 "HFPv2-…")과 네임스페이스가 겹치면 안 된다.
    ///   그쪽은 화면 해상도·브라우저 종류로 만들어지고 서버는 그 값을 만들 수 없다.
    ///   접두어를 달리해 **같은 테이블에서 절대 충돌하지 않게** 한다.
    ///
    /// MachineName 을 쓰는 것은 워치독의 기존 패턴이다(TunnelTokenRecovery.cs:92).
    /// SLOT_INDEX 를 섞는 이유는 한 PC 에 여러 슬롯이 설치될 수 있기 때문이다 —
    /// 슬롯마다 다른 테넌트이므로 지문도 갈려야 한다.
    ///
    /// ⚠️ 컴퓨터 이름을 바꾸면 지문이 바뀐다. 그때는 기존 메인PC 행이 남아 있어
    ///   새로 만들지 않는다(위 ② 분기). 슬롯을 두 번 먹는 것보다 낫다.
    /// </summary>
    private static string BuildServerFingerprint()
    {
        var seed = string.Join('|',
            Environment.MachineName,
            TenantConfigReader.Get("TENANT_CODE") ?? "LOCAL",
            TenantConfigReader.Get("SLOT_INDEX") ?? "1");

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return "MAINPC-" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static string BuildConnectionString()
    {
        var host = TenantConfigReader.Get("DB_HOST") ?? "localhost";
        var port = TenantConfigReader.Get("DB_PORT") ?? "3306";
        var db = TenantConfigReader.Get("DB_NAME");
        var user = TenantConfigReader.Get("DB_USER");
        var pwd = TenantConfigReader.Get("DB_PASSWORD");
        if (string.IsNullOrEmpty(db) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pwd))
        {
            return string.Empty;
        }
        return $"Server={host};Port={port};Database={db};User={user};Password={pwd};DefaultCommandTimeout=30;";
    }
}
