using System.Net;
using HitPan.API.Controllers;
using HitPan.API.Services;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-A4</b> — <c>GET /api/auth/update-status</c> 의 조회 SQL 이 <b>출하 DDL 로 만든 DB</b> 위에서 실제로 돈다(#13 · #36).
/// 20260929작3 절A2.
/// </summary>
/// <remarks>
/// <para>
/// 🟢 <b>운영 코드를 태운다</b> — <see cref="AuthController"/> 실물의 <c>GetUpdateStatus</c> 를 부른다.
/// 판정 SQL 은 이 파일에 없다(<see cref="UpdateIssueJudge.IssueQuerySql"/> 를 컨트롤러가 쓴다).
/// 조회가 실패하면 컨트롤러는 <b>조용히 폴백</b>하므로(#20), 이 게이트는 <b>경고 로그 0건</b>과
/// 판정값 둘 다를 본다 — 폴백을 통과로 읽지 않는다.
/// </para>
/// <para>
/// 🟢 <b>표 구조는 출하 DDL 에서 온다</b> — <c>db-gate</c> 잡이 <c>installer/hitpan_db_clean.sql</c> 을 적재한
/// <c>hitpan_e2e</c> 의 세 표를 <c>SHOW CREATE TABLE</c> 로 떠서 <b>같은 이름의 임시 표</b>를 만든다.
/// 임시 표는 이 연결에만 보이고 같은 이름의 실제 표를 가린다 ⇒ 공용 DB 에 행을 남기지 않는다.
/// </para>
/// <para>
/// ⬛ <b>DB-135(갈래 W)가 출하 DDL 에 <c>consent_id</c> 칸을 넣어야 초록</b>이다. 칸이 없으면 조회가
/// <c>Unknown column 'a.consent_id'</c> 로 실패해 경고가 찍히고 FAIL — 그것이 이 게이트의 대조군이다(명세서 §4).
/// </para>
/// <para>
/// 🔴 20260929작3 갈래 Y(9/30 · 설계 §12) — 위 ⬛ 줄은 폐기된 갈래 W 전제다. 이제 A(시도 기록)는 새 표
/// <c>local_update_attempts</c>(DB-135 · 출하 DDL 편입)에서 온다 ⇒ 가림 표 +1 · 시도 행은 그 표에 넣는다.
/// 대조군: ① 출하 DDL 에 시도 표가 없으면 <c>SHOW CREATE TABLE</c> 단언 FAIL
/// ② 옛 출처(apply_status 에만 blocked 행)는 실패 판정을 내면 안 된다(<see cref="GA4_옛출처_apply_status_는_판정에_안_쓴다"/>).
/// </para>
/// <para>🔴 로컬 초록은 DB 가 있을 때만 의미가 있다. 없으면 [SKIP] — <b>유일한 계측 경로는 CI <c>db-gate</c></b>(<c>HITPAN_REQUIRE_DB</c>).</para>
/// </remarks>
public sealed class UpdateStatusDdlGateTests
{
    private static string TestDb =>
        Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    private const string L = "999.0.0";

    [Fact(DisplayName = "G-A4 🔴 출하 DDL 위에서 판정 조회가 돈다 — 디스크 차단은 저장공간 문구로 다시 묻는다")]
    public async Task GA4_출하DDL_위에서_실패판정이_나온다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-A4 출하 DDL 판정 조회")) return;
        await using var db = await OpenWithShadowTablesAsync();

        await ExecAsync(db, $"""
            INSERT INTO local_update_status (latest_version, update_channel, discovered_at) VALUES ('{L}', 'Major', NOW(3));
            INSERT INTO local_update_consents (id, tenant_id, user_id, update_version, action, consented_at)
                 VALUES (3, 't', 'u', '{L}', 'approve', NOW(3));
            INSERT INTO local_update_attempts (consent_id, update_version, result, detail, started_at, ended_at)
                 VALUES (3, '{L}', 'blocked', '디스크 여유공간 부족 — 업데이트 시작 차단', NOW(3), NOW(3));
            """);

        var (dto, warnings) = await CallAsync(db);

        Assert.True(warnings.Count == 0, "조회가 실패해 폴백했다: " + string.Join(" | ", warnings));
        Assert.True(dto.UpdateAvailable);
        Assert.Equal(UpdateIssueJudge.KindFailed, dto.IssueKind);
        Assert.Equal(UpdateIssueJudge.TextBlockedDisk, dto.IssueText);
        Assert.True(dto.NeedsPrompt);
        Assert.True(dto.CanRespond, "루프백 직접 요청은 메인PC 다(IsMainPc 호출 결과)");
    }

    /// <remarks>
    /// 🔴 [3-V] 병렬이슈 02 — 「최신 동의」는 <b>id 순</b>. id 1 [예]는 시계가 앞서 있던 때(2099)라 시각은 더 늦고,
    /// id 2 [나중에]는 시계를 바로잡은 뒤(2020)라 시각은 더 이르다. id 순이면 later, 시각 순이면 requested.
    /// 음성 대조군 — <see cref="UpdateIssueJudge.IssueQuerySql"/> 정렬을 <c>consented_at DESC, id DESC</c> 로 되돌리면 FAIL.
    /// </remarks>
    [Fact(DisplayName = "G-A4 병렬이슈02 🔴 시계가 뒤로 간 뒤의 [나중에]도 최신으로 본다 (id 순)")]
    public async Task GA4_최신동의는_id순()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-A4 병렬이슈02 id 순")) return;
        await using var db = await OpenWithShadowTablesAsync();

        await ExecAsync(db, $"""
            INSERT INTO local_update_status (latest_version, update_channel, discovered_at) VALUES ('{L}', 'Major', NOW(3));
            INSERT INTO local_update_consents (id, tenant_id, user_id, update_version, action, consented_at)
                 VALUES (1, 't', 'u', '{L}', 'approve', '2099-01-01 00:00:00.000'),
                        (2, 't', 'u', '{L}', 'reject',  '2020-01-01 00:00:00.000');
            """);

        var (dto, warnings) = await CallAsync(db);

        Assert.True(warnings.Count == 0, "조회가 실패해 폴백했다: " + string.Join(" | ", warnings));
        Assert.Equal(UpdateIssueJudge.KindLater, dto.IssueKind);
        Assert.True(dto.NeedsPrompt);
    }

    /// <remarks>경과(A)는 DB 시계끼리 잰다 — <c>TIMESTAMPDIFF(SECOND, started_at, NOW(3))</c>(시도 표 · 갈래 Y 재표적) 가 실제로 돈다.</remarks>
    [Fact(DisplayName = "G-A4 🔴 in_progress 가 T2(30분)를 넘으면 중간에 멈춘 것으로 다시 묻는다")]
    public async Task GA4_오래된_진행중은_중단()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-A4 in_progress 경과")) return;
        await using var db = await OpenWithShadowTablesAsync();

        await ExecAsync(db, $"""
            INSERT INTO local_update_status (latest_version, update_channel, discovered_at) VALUES ('{L}', 'Major', NOW(3));
            INSERT INTO local_update_consents (id, tenant_id, user_id, update_version, action, consented_at)
                 VALUES (5, 't', 'u', '{L}', 'approve', NOW(3) - INTERVAL 40 MINUTE);
            INSERT INTO local_update_attempts (consent_id, update_version, result, detail, started_at, ended_at)
                 VALUES (5, '{L}', 'in_progress', NULL, NOW(3) - INTERVAL 31 MINUTE, NULL);
            """);

        var (dto, warnings) = await CallAsync(db);

        Assert.True(warnings.Count == 0, "조회가 실패해 폴백했다: " + string.Join(" | ", warnings));
        Assert.Equal(UpdateIssueJudge.KindInterrupted, dto.IssueKind);
        Assert.Equal(UpdateIssueJudge.TextInterrupted, dto.IssueText);
    }

    /// <remarks>
    /// 🔴 갈래 Y 음성 대조군 — A 의 출처는 시도 표뿐이다. 옛 출처(<c>local_update_apply_status</c>)에만 blocked 행이 있고
    /// 시도 표가 비어 있으면 「아직 시작 안 함(requested)」이어야 한다. 판정 SQL 이 apply_status 를 다시 읽으면 failed ⇒ FAIL.
    /// (apply_status 는 옛 워치독의 보호 표라 칸이 없다 — consent_id 를 안 넣는 INSERT 가 그대로 도는 것도 함께 확인된다.)
    /// </remarks>
    [Fact(DisplayName = "G-A4 대조 🔴 옛 출처 apply_status 의 blocked 는 판정에 안 쓴다 — 시도 표가 비면 requested")]
    public async Task GA4_옛출처_apply_status_는_판정에_안_쓴다()
    {
        if (!ServerAvailable() && DbGateEnvironment.SkipOrFail("G-A4 옛 출처 대조")) return;
        await using var db = await OpenWithShadowTablesAsync();

        await ExecAsync(db, $"""
            INSERT INTO local_update_status (latest_version, update_channel, discovered_at) VALUES ('{L}', 'Major', NOW(3));
            INSERT INTO local_update_consents (id, tenant_id, user_id, update_version, action, consented_at)
                 VALUES (3, 't', 'u', '{L}', 'approve', NOW(3));
            INSERT INTO local_update_apply_status (applied_version, result, detail, applied_at)
                 VALUES ('{L}', 'blocked', '디스크 여유공간 부족 — 업데이트 시작 차단', NOW(3));
            """);

        var (dto, warnings) = await CallAsync(db);

        Assert.True(warnings.Count == 0, "조회가 실패해 폴백했다: " + string.Join(" | ", warnings));
        Assert.Equal(UpdateIssueJudge.KindRequested, dto.IssueKind);
        Assert.NotEqual(UpdateIssueJudge.KindFailed, dto.IssueKind);
    }

    // ══════════════════════════════════════════════════════════════
    // 준비물
    // ══════════════════════════════════════════════════════════════

    // 20260929작3 갈래 Y — 시도 표(local_update_attempts · DB-135) +1. 출하 DDL 에 없으면 아래 SHOW CREATE 단언이 FAIL.
    private static readonly string[] Tables =
        { "local_update_status", "local_update_consents", "local_update_apply_status", "local_update_attempts" };

    /// <summary>출하 DDL 로 적재된 세 표의 정의를 떠서 <b>같은 이름의 임시 표</b>로 가린다(행 0에서 출발).</summary>
    private static async Task<MySqlConnection> OpenWithShadowTablesAsync()
    {
        var db = new MySqlConnection(ConnString());
        await db.OpenAsync();

        foreach (var t in Tables)
        {
            string ddl;
            await using (var cmd = new MySqlCommand($"SHOW CREATE TABLE `{t}`", db))
            await using (var r = await cmd.ExecuteReaderAsync())
            {
                Assert.True(await r.ReadAsync(), $"출하 DDL 에 {t} 표가 없다");
                ddl = r.GetString(1);
            }

            const string head = "CREATE TABLE";
            Assert.StartsWith(head, ddl, StringComparison.Ordinal);
            await ExecAsync(db, "CREATE TEMPORARY TABLE" + ddl.Substring(head.Length));
        }

        return db;
    }

    private static async Task ExecAsync(MySqlConnection db, string sql)
    {
        await using var cmd = new MySqlCommand(sql, db);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<(UpdateStatusDto dto, List<string> warnings)> CallAsync(MySqlConnection db)
    {
        var services = new ServiceCollection();
        services.AddSingleton<System.Data.IDbConnection>(db);
        using var sp = services.BuildServiceProvider();

        var ctx = new DefaultHttpContext { RequestServices = sp };
        ctx.Connection.RemoteIpAddress = IPAddress.Loopback;

        var logger = new CapturingLogger();
        var ctl = new AuthController(
            Mock.Of<IAuthService>(), Mock.Of<IHrService>(), Mock.Of<ITenantDeviceService>(),
            Mock.Of<ITermsConsentService>(), Mock.Of<INotificationService>(), logger)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx },
        };

        var result = await ctl.GetUpdateStatus(CancellationToken.None);
        var dto = Assert.IsType<UpdateStatusDto>(Assert.IsType<OkObjectResult>(result).Value);
        return (dto, logger.Warnings);
    }

    private sealed class CapturingLogger : ILogger<AuthController>
    {
        public List<string> Warnings { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
                Warnings.Add(formatter(state, exception) + (exception is null ? "" : " :: " + exception.Message));
        }
    }

    private static string ConnString()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "hitpan";
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
        return $"Server={host};Port={port};Database={TestDb};User={user};Password={pass};"
             + "DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;";
    }

    private static bool ServerAvailable()
    {
        if (DbGateEnvironment.IsCi) return true;   // CI 는 DB 필수 — 못 붙으면 아래에서 실패로 드러난다
        try { using var c = new MySqlConnection(ConnString()); c.Open(); return true; }
        catch (MySqlException) { return false; }
    }
}
