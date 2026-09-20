using Dapper;
using HitPan.Application.DTOs.Approval;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Moq;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>위험 서버(STATEMENT) 전용 격리 DB</b> — 20260920작1 갈래 S2 (작지 §5-1 · 설계 §6-1).
///
/// <para>
/// 기존 <see cref="LegacyBalanceMatchDbFixture"/> 와 모양은 같되 <b>포트만</b> <c>HITPAN_DB_STMT_PORT</c> 로 찾는다.
/// 이 인스턴스는 <c>--log-bin --binlog-format=STATEMENT</c> 로 떠 있어야 한다 — 안전 서버(3306)에서는
/// 경로 판정이 RC 로 떨어져 <b>RR 분기를 아예 안 탄다</b>. 즉 여기가 아니면 이 봉합을 재는 곳이 없다(작지 §14).
/// </para>
/// <para>
/// 🔴 <b>SKIP 을 통과로 세지 않는다</b> — 변수가 없거나 못 붙으면 로컬은 건너뛰고 CI(<c>HITPAN_REQUIRE_DB</c>)는 실패한다.
/// 나아가 <b>붙었는데 STATEMENT 가 아니면 실패</b>시킨다. 안전 서버에 잘못 붙어 초록불이 나면 이 게이트는
/// 재야 할 것을 하나도 안 재고 통과한 것이다(「게이트는 글자가 아니라 동작」).
/// </para>
/// </summary>
public sealed class RcStatementDbFixture : IDisposable
{
    public string DbName { get; } = "hitpan_s2_rc_" + Guid.NewGuid().ToString("N")[..8];
    public bool Available { get; }

    /// <summary>못 쓰는 이유(로그·실패 메시지용). 쓸 수 있으면 <c>null</c>.</summary>
    public string? Unavailable { get; }

    private readonly bool _created;

    public RcStatementDbFixture()
    {
        var why = ServerUnavailableReason();
        if (why is not null) { Unavailable = why; return; }

        using (var admin = new MySqlConnection(ServerConnString()))
        {
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{DbName}`; CREATE DATABASE `{DbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        }
        _created = true;

        var psi = new System.Diagnostics.ProcessStartInfo(MysqlExe())
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardInputEncoding = new System.Text.UTF8Encoding(false)
        };
        psi.ArgumentList.Add($"--host={Host}");
        psi.ArgumentList.Add($"--port={StmtPort}");
        psi.ArgumentList.Add($"-u{User}");
        if (!string.IsNullOrEmpty(Pass)) psi.ArgumentList.Add($"-p{Pass}");
        // ⚠️ 없으면 한글이 CP949 로 재져 길이가 틀리게 들어간다(작지 §8 · 기존 관례).
        psi.ArgumentList.Add("--default-character-set=utf8mb4");
        psi.ArgumentList.Add(DbName);

        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.StandardInput.Write(File.ReadAllText(Path.Combine(RepoRoot(), "installer", "hitpan_db_clean.sql")));
        proc.StandardInput.Close();
        var err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0) throw new InvalidOperationException($"출하 DDL import 실패(STATEMENT 인스턴스 {StmtPort}):\n{err}");
        Available = true;
    }

    public static string? StmtPort => Environment.GetEnvironmentVariable("HITPAN_DB_STMT_PORT");
    private static string Host => Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
    private static string User => Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root";
    private static string Pass => Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";

    public string DbConnString() => ServerConnString().Replace("User=", $"Database={DbName};User=");

    private static string ServerConnString() =>
        $"Server={Host};Port={StmtPort};User={User};Password={Pass};DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;";

    private static string MysqlExe() =>
        Environment.GetEnvironmentVariable("HITPAN_MYSQL") ?? @"C:\Program Files\MariaDB 11.4\bin\mysql.exe";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("HitPan.sln 을 못 찾았다.");
    }

    /// <summary>
    /// 쓸 수 있으면 <c>null</c>. 🔴 <b>붙는 것만으로는 부족하다</b> — <c>log_bin=ON</c> + <c>binlog_format=STATEMENT</c> 까지 확인한다.
    /// </summary>
    private static string? ServerUnavailableReason()
    {
        if (string.IsNullOrWhiteSpace(StmtPort))
            return "HITPAN_DB_STMT_PORT 가 없다 (STATEMENT 인스턴스를 안 띄웠다)";
        if (!DbGateEnvironment.IsCi && !File.Exists(MysqlExe()))
            return $"mysql 클라이언트 없음: {MysqlExe()}";
        try
        {
            using var c = new MySqlConnection(ServerConnString());
            c.Open();
            var logBin = c.ExecuteScalar<string>("SELECT CAST(@@log_bin AS CHAR)");
            var fmt = c.ExecuteScalar<string>("SELECT CAST(@@binlog_format AS CHAR)");
            var mode = BinlogSafetyProbe.Decide(logBin, fmt);
            if (mode != LegacyMatchMode.RepeatableReadLocking)
                throw new Xunit.Sdk.XunitException(
                    $"[RcSafePathGate] HITPAN_DB_STMT_PORT={StmtPort} 가 위험 서버가 아니다 (log_bin={logBin} binlog_format={fmt} → {mode}).\n"
                  + "  이 게이트는 STATEMENT 서버에서만 RR 경로를 잰다. 안전 서버에 붙은 초록불은 아무것도 재지 않은 초록불이다.\n"
                  + "  --log-bin --binlog-format=STATEMENT 로 띄운 인스턴스의 포트를 주입하라.");
            return null;
        }
        catch (MySqlException ex)
        {
            if (DbGateEnvironment.IsCi) throw;   // CI 는 반드시 붙어야 한다 — 삼키지 않는다
            return $"STATEMENT 인스턴스({Host}:{StmtPort}) 연결 실패: {ex.Message}";
        }
    }

    public void Dispose()
    {
        if (!_created) return;
        try
        {
            using var admin = new MySqlConnection(ServerConnString());
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{DbName}`");
        }
        catch (MySqlException ex)
        {
            Console.Error.WriteLine($"[RcSafePathGate] 임시 DB 삭제 실패 {DbName}: {ex.Message}");
        }
    }
}

/// <summary>
/// 🔴 <b>G-RC RcSafePathGate</b> — 20260920작1 갈래 S2 (작지 §5-2 · §12-2 · 설계 §6-2 · §11-3).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>이 게이트가 유일한 감시자다</b>(작지 §14 PM 판정). 안전 서버(RC)에서는 봉합을 전부 빼도 기존 40 게이트가 초록이다
/// — 33306 은 RC 경로라 RR 분기에 아예 안 들어가기 때문이다(S1b 명세서 §8-4-2 대조 실험).
/// RR 경로는 STATEMENT 인스턴스에서만 돈다 → 여기서 못 잡으면 아무도 못 잡는다.
/// </para>
/// <para>
/// G-RC1·2·5 는 <b>서비스 진입점</b>(<c>CreateCollectionAsync</c>·<c>CreatePaymentAsync</c>)으로 부른다.
/// raw SQL 로 부르면 재시도 껍질 <c>RunMatchTxAsync</c> 바깥을 재게 되어 다른 것을 재는 게이트가 된다(설계 §6-2).
/// </para>
/// </remarks>
[Collection("DeviceAndKeyGate")]
public sealed class RcSafePathGateTests : IClassFixture<RcStatementDbFixture>
{
    private readonly RcStatementDbFixture _fx;

    private readonly string PA = Guid.NewGuid().ToString();
    private readonly string PB = Guid.NewGuid().ToString();
    private readonly string PC = Guid.NewGuid().ToString();
    private static readonly DateTime BaseDate = new(2026, 2, 28);
    private static readonly DateTime After = new(2026, 3, 10);

    public RcSafePathGateTests(RcStatementDbFixture fx) => _fx = fx;

    private bool Skip(string name)
    {
        if (_fx.Available) return false;
        Console.Error.WriteLine($"[RcSafePathGate] 사유: {_fx.Unavailable}");
        return DbGateEnvironment.SkipOrFail("RcSafePathGate " + name);
    }

    // ────────────────────────────── 사례 (뼈대 · 본문은 다음 커밋) ──────────────────────────────

    [Fact(DisplayName = "G-RC1 위험 서버에서 수금 등록 3경로(이월·명세서·ref 없음) 성공 — 서비스 진입점 경유")]
    public Task GRC1_수금등록_3경로() => 뼈대(nameof(GRC1_수금등록_3경로));

    [Fact(DisplayName = "G-RC2 위험 서버에서 지급 등록 3경로(이월·매입·ref 없음) 성공 — 서비스 진입점 경유")]
    public Task GRC2_지급등록_3경로() => 뼈대(nameof(GRC2_지급등록_3경로));

    [Fact(DisplayName = "G-RC3 G8-u 동등 — 앞선 일반 읽기 뒤에도 최신 R(파생표 아닌 직접읽기) · 최종 R ≥ 0 · 초과 거절 문구")]
    public Task GRC3_잠금읽기_최신R() => 뼈대(nameof(GRC3_잠금읽기_최신R));

    [Fact(DisplayName = "G-RC4 G8-x 동등 — 같은 거래처 동시(각 R 이하 · 합 초과) 뒤 연결이 대기 후 거절 · 최종 R ≥ 0")]
    public Task GRC4_같은거래처_동시() => 뼈대(nameof(GRC4_같은거래처_동시));

    [Fact(DisplayName = "G-RC5 G8-w 동등 — 다른 거래처 동시, 서비스 진입점 두 연결 둘 다 성공 · 시도 횟수 기록")]
    public Task GRC5_다른거래처_동시_진입점() => 뼈대(nameof(GRC5_다른거래처_동시_진입점));

    [Fact(DisplayName = "G-RC6 D1 — 같은 명세서 동시 수금 합 초과 거절 · 매입전표 축 대칭 · 최종 합 ≤ 전표금액")]
    public Task GRC6_전표_D1_동시() => 뼈대(nameof(GRC6_전표_D1_동시));

    [Fact(DisplayName = "G-RC7 판정표 4조합 + 조회 실패 → RR (순수 · DB 불필요)")]
    public Task GRC7_판정표() => 뼈대(nameof(GRC7_판정표));

    [Fact(DisplayName = "G-RC8 대조군 — 안전 서버(HITPAN_DB_PORT)에서 모드 = ReadCommittedFresh")]
    public Task GRC8_대조군_안전서버() => 뼈대(nameof(GRC8_대조군_안전서버));

    [Fact(DisplayName = "G-RC9 캐시 — 2번째 등록은 판정 조회 왕복 0 · TTL 뒤 1회 · Invalidate 뒤 1회")]
    public Task GRC9_판정캐시() => 뼈대(nameof(GRC9_판정캐시));

    [Fact(DisplayName = "G-RC10 동등성 — 공용 식과 RR 재계산의 L0·M·R 이 7경계(E1~E7)에서 모두 같다")]
    public Task GRC10_동등성_7경계() => 뼈대(nameof(GRC10_동등성_7경계));

    [Fact(DisplayName = "G-RC11 앵커 — 공용 식 2개의 SHA-256 이 기록값과 같다 (공용 식을 고치면 RR 식도 같이 고치게 만든다)")]
    public Task GRC11_공용식_앵커() => 뼈대(nameof(GRC11_공용식_앵커));

    /// <summary>
    /// 🔴 뼈대 표시 — 본문이 아직 없다. <b>조용한 초록을 만들지 않는다</b>(작지 §8 「뼈대를 먼저 커밋」 단계).
    /// 본문이 채워지면 이 메서드는 사라진다.
    /// </summary>
    private static Task 뼈대(string name)
    {
        Assert.Fail($"[뼈대] {name} — 20260920작1 갈래 S2 본문 미구현. 이 자리는 통과가 아니다.");
        return Task.CompletedTask;
    }
}
