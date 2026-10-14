using Dapper;
using HitPan.Backoffice.API.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261015작B1 갈래 B · G2 — 「메일 실패를 아무도 모르는 자리」를 없앴는지 잰다.
/// (G-B1-B-1 ~ G-B1-B-5)
///
/// <para><b>선행검증 사실(재측정 불필요)</b>: 발송기 <see cref="EmailSender"/> 는 자격증명 미저장이면
/// <b>정직하게 <c>false</c></b> 를 돌려준다. 그런데 호출자 4곳이 <c>_ = _email.SendAsync(…)</c> 로
/// 그 <c>false</c> 를 <b>버렸다</b>. 게다가 백오피스에는 발송 기록 표·칸이 <b>0개</b>였다
/// ⇒ 「안 갔다」를 아무도 모르고 「진짜 1통 갔다」를 조회로 증명할 자리도 없었다.</para>
///
/// <para><b>글자가 아니라 동작을 잰다.</b> 무는 것은 실물뿐이다:
/// <list type="bullet">
/// <item>스키마 = 실물 <see cref="SchemaMigrator"/> 가 <c>installer/backoffice/*.sql</c> 번호순 적용
///       (신설 <c>46</c> 번이 그 흐름에 실제로 끼는지도 여기서 드러난다).</item>
/// <item>적재 = 실물 <see cref="EmailSender"/> 의 <c>SendAsync</c> 를 호출해 <b>그가 자기 INSERT 를 DB 에 보내게</b> 한다.</item>
/// </list></para>
///
/// <para>🔴 <b>성공 행만은 2단이다</b> — SMTP 자격증명 <b>값</b>이 결재 대기(G5)이고 메일 실발송은
/// 금지이므로 <c>result='sent'</c> 를 실경로로 만들 길이 이 차수에 <b>없다</b>.
/// §6 ① 이 승인한 2단 합격선 그대로: <b>1단</b>(지금) = 실패 행 두 종류를 <b>실경로</b>로 ·
/// 성공 행은 <b>모양</b>만(<see cref="EmailSender.RecordAsync"/> 직접 호출) +
/// 성공 분기가 그 함수를 부른다는 <b>배선</b>을 G-B1-B-4 가 문다.
/// <b>2단</b>(G5 결재 후) = 값 투입 → <c>test-mail</c> 1회 → 표 1행 <c>결과=성공</c> + 수신 확인.</para>
///
/// <para>DB 자리: CI <c>db-gate</c> 잡(root)은 격리 DB 를 만들었다 지운다 ·
/// 로컬 <c>hitpan</c> 계정은 <c>CREATE DATABASE</c> 거부라 <c>HITPAN_BO_GATE_DB</c> 로 지정해 실측한다.
/// 둘 다 안 되면 <see cref="DbGateEnvironment.SkipOrFailStrict"/> — 조용한 초록은 못 낸다.
/// (선례·같은 방식: <c>BackofficeCsOutboundGateTests.CsGateDb</c>)</para>
/// </summary>
public sealed class BackofficeMailHistoryGateTests
{
    private const string Table = "bo_email_send_history";

    // ══════════════════════════════════════════════════════════════
    // G-B1-B-1 — 표가 적재기 흐름에 실제로 끼고, 칸 6종이 다 있다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-B1-B-1 46번이 적재기 흐름에 끼어 bo_email_send_history 가 생긴다 · 칸 6종 · InnoDB (#17)")]
    public async Task G_B1_B_1_표가_생긴다()
    {
        const string gate = "G-B1-B-1 발송기록 표";
        await using var env = await MailGateDb.TryCreateAsync(gate, "hitpan_bo_b1_mail_1");
        if (env is null) return;

        await using var db = await env.OpenAsync();

        // 🔴 적재기(SchemaMigrator)를 실제로 돌린 뒤에 묻는다 — SQL 파일을 읽어 글자를 비교하지 않는다.
        var engine = await db.QueryFirstOrDefaultAsync<string?>(
            "SELECT engine FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = @T",
            new { T = Table });
        Assert.True(engine is not null,
            $"{gate} 표 {Table} 가 없다 — 46_backoffice_email_send_history.sql 이 적재기 흐름에 안 끼었다.");
        Assert.True(string.Equals(engine, "InnoDB", StringComparison.OrdinalIgnoreCase),
            $"{gate} ENGINE 이 InnoDB 가 아니다(#17). 실측값={engine}");

        // 작업지시서 §4 B-1 이 지정한 칸 = 받는이·제목·결과·사유·발송시각·호출출처
        var cols = (await db.QueryAsync<string>(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = @T",
            new { T = Table })).Select(c => c.ToLowerInvariant()).ToHashSet();

        foreach (var want in new[] { "send_id", "to_email", "subject", "result", "reason_code", "reason", "sent_at", "caller" })
        {
            Assert.True(cols.Contains(want), $"{gate} 칸 {want} 가 없다. 있는 칸=[{string.Join(",", cols.OrderBy(x => x))}]");
        }

        // 🔴 음성 대조 — #22. 본문(htmlBody)을 담는 칸이 생기면 임시비번·라이선스 키가 표에 눕는다.
        foreach (var banned in new[] { "body", "html_body", "password", "smtp_password", "temp_password" })
        {
            Assert.False(cols.Contains(banned),
                $"{gate} 금지 칸 {banned} 가 생겼다 — 메일 본문·자격증명은 이 표에 담지 않는다(#22·#29).");
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-B1-B-2 — 자격증명 미설정 발송 1회 → 실패 1행 (음성 대조군 ⓐ-1 의 과녁)
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-B1-B-2 자격증명 미설정으로 1회 발송 → false + 표에 실패 1행(not_configured) · 호출출처 기록")]
    public async Task G_B1_B_2_미설정_실패가_1행_남는다()
    {
        const string gate = "G-B1-B-2 미설정 실패행";
        await using var env = await MailGateDb.TryCreateAsync(gate, "hitpan_bo_b1_mail_2");
        if (env is null) return;

        // Smtp:* 를 한 칸도 주지 않는다 = 자격증명 결재 대기 상태 그대로.
        var sender = new EmailSender(env.Config(), NullLogger<EmailSender>.Instance);

        var ok = await sender.SendAsync("mail-gate-2@example.invalid", "게이트 미설정 경로", "<p>본문</p>");
        Assert.False(ok, $"{gate} 발송기가 미설정인데 true 를 돌려줬다 — 정직한 false 가 깨졌다.");

        await using var db = await env.OpenAsync();
        var rows = (await db.QueryAsync<HistRow>($@"
            SELECT to_email AS ToEmail, subject AS Subject, result AS Result,
                   reason_code AS ReasonCode, reason AS Reason, caller AS Caller
            FROM {Table}")).ToList();

        // ⓐ-1 의 숫자: 발송 1회 = 표 1행. 적재 코드를 빼면 여기가 0행이 되어 FAIL 한다.
        Assert.True(rows.Count == 1,
            $"{gate} 발송 1회당 1행이어야 한다(실측 {rows.Count}행). 0행이면 실패를 아무도 모르는 그 자리가 그대로다.");

        var row = rows[0];
        Assert.Equal("failed", row.Result);
        Assert.Equal("not_configured", row.ReasonCode);
        Assert.Equal("mail-gate-2@example.invalid", row.ToEmail);
        Assert.Equal("게이트 미설정 경로", row.Subject);
        Assert.False(string.IsNullOrWhiteSpace(row.Reason), $"{gate} 사유가 비었다 — 왜 안 갔는지 읽을 수 없다.");

        // 호출출처 — 못 가려도 행은 남아야 하지만, 가린 값이 있으면 사람이 읽을 모양이어야 한다.
        Assert.False(string.IsNullOrWhiteSpace(row.Caller), $"{gate} 호출출처 칸이 비었다(최소 unknown).");

        // 🔴 음성 대조 — 자격증명 값·모양이 표에 섞여 들어가지 않는다(#29).
        var dump = string.Join(" | ", rows.Select(r => $"{r.Reason} {r.Caller}"));
        Assert.DoesNotContain("Smtp:Password", dump, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password=", dump, StringComparison.OrdinalIgnoreCase);
    }

    // ══════════════════════════════════════════════════════════════
    // G-B1-B-3 — SMTP 가 죽어 있을 때(예외 경로) 도 1행 남는다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-B1-B-3 자격증명이 있는데 SMTP 가 안 붙으면 → false + 표에 실패 1행(smtp_error)")]
    public async Task G_B1_B_3_예외경로도_1행_남는다()
    {
        const string gate = "G-B1-B-3 예외 실패행";
        await using var env = await MailGateDb.TryCreateAsync(gate, "hitpan_bo_b1_mail_3");
        if (env is null) return;

        // 🔴 자격증명 「값」이 아니다 — 아무도 쓰지 않는 자리표시 문자열이다(#29 · 조건4).
        //   호스트는 RFC 2606 예약 TLD(.invalid)라 DNS 가 영영 안 풀린다 ⇒ 반드시 예외 경로로 간다.
        //   포트는 닫힌 자리. 바깥으로 나가는 연결이 0 이라 「메일 실발송」이 원리적으로 불가하다.
        var sender = new EmailSender(env.Config(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "smtp.gate-unreachable.invalid",
            ["Smtp:User"] = "gate-placeholder-user",
            ["Smtp:Port"] = "2525"
        }), NullLogger<EmailSender>.Instance);

        var ok = await sender.SendAsync("mail-gate-3@example.invalid", "게이트 예외 경로", "<p>본문</p>");
        Assert.False(ok, $"{gate} 붙지도 않은 SMTP 에 true 를 돌려줬다.");

        await using var db = await env.OpenAsync();
        var rows = (await db.QueryAsync<HistRow>($@"
            SELECT to_email AS ToEmail, subject AS Subject, result AS Result,
                   reason_code AS ReasonCode, reason AS Reason, caller AS Caller
            FROM {Table}")).ToList();

        Assert.True(rows.Count == 1, $"{gate} 예외 경로도 1행이어야 한다(실측 {rows.Count}행).");
        Assert.Equal("failed", rows[0].Result);
        Assert.Equal("smtp_error", rows[0].ReasonCode);
        Assert.False(string.IsNullOrWhiteSpace(rows[0].Reason),
            $"{gate} 예외 사유가 비었다 — 「왜 못 붙었나」가 없으면 기록이 쓸모없다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-B1-B-4 — 성공 행: 모양(실적재) + 배선(성공 분기가 적재를 부른다)
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-B1-B-4 성공 행 모양 = result 'sent' · reason_code 'ok' (실적재) + 성공 분기 배선 (2단 중 1단)")]
    public async Task G_B1_B_4_성공행_모양과_배선()
    {
        const string gate = "G-B1-B-4 성공행";
        await using var env = await MailGateDb.TryCreateAsync(gate, "hitpan_bo_b1_mail_4");
        if (env is null) return;

        // ── ① 모양: 적재 함수를 직접 불러 실제로 INSERT 가 되는지 잰다(글자 비교 아님) ──
        //   🔴 실경로로 성공을 만들 길이 이 차수에 없다(자격증명 값 = G5 결재 대기 · 실발송 금지).
        //      그래서 §6 ① 이 승인한 2단 중 1단만 여기서 잰다.
        var sender = new EmailSender(env.Config(), NullLogger<EmailSender>.Instance);
        await sender.RecordAsync("mail-gate-4@example.invalid", "게이트 성공 모양", sent: true,
            "ok", null, "BackofficeMailHistoryGateTests.G_B1_B_4", hasAttachment: false, CancellationToken.None);

        await using var db = await env.OpenAsync();
        var row = await db.QueryFirstOrDefaultAsync<HistRow>($@"
            SELECT to_email AS ToEmail, subject AS Subject, result AS Result,
                   reason_code AS ReasonCode, reason AS Reason, caller AS Caller
            FROM {Table}");
        Assert.True(row is not null, $"{gate} 성공 행이 적재되지 않았다 — 적재 경로 자체가 막혀 있다.");
        Assert.Equal("sent", row!.Result);
        Assert.Equal("ok", row.ReasonCode);

        // sent_at 이 비거나 0 이면 「언제 갔나」를 못 센다.
        var sentAt = await db.QueryFirstOrDefaultAsync<DateTime?>($"SELECT sent_at FROM {Table} LIMIT 1");
        Assert.True(sentAt.HasValue && sentAt.Value.Year >= 2026, $"{gate} sent_at 이 비정상이다(실측 {sentAt}).");

        // ── ② 배선: 성공 분기가 적재를 부르는가 ──
        //   🔴 이것만 글자로 잰다. 이유를 숨기지 않는다: 실경로 성공이 G5 결재에 막혀 있어서다.
        //      2단(값 투입 후)에서 이 줄은 실경로 조회로 대체된다.
        var src = ReadSource("src/HitPan.Backoffice.API/Services/EmailSender.cs");
        Assert.Contains("RecordAsync(toEmail, subject, sent: true", src.Replace("\r\n", "\n").Replace("\n", " "));
        // 세 경로 모두 적재한다 — 호출 3곳(미설정·성공·예외) + 선언 1곳.
        var calls = System.Text.RegularExpressions.Regex.Matches(src, @"RecordAsync\(toEmail, subject, sent:").Count;
        Assert.True(calls == 3,
            $"{gate} 세 경로(미설정·성공·예외) 모두 적재해야 한다. 실측 호출 {calls}곳.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-B1-B-5 — `_ = _email.SendAsync(…)` 버림 잔존 0 (음성 대조군 ⓑ 의 과녁)
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-B1-B-5 버려지는 발송 결과 0건 — `_ = _email` 잔존 수 = 0 (대상 2파일)")]
    public void G_B1_B_5_버림이_0건이다()
    {
        const string gate = "G-B1-B-5 버림 0건";

        var targets = new[]
        {
            "src/HitPan.Backoffice.API/Controllers/LandingSignupController.cs",
            "src/HitPan.Backoffice.API/Controllers/ResellerApplicationsAdminController.cs"
        };

        foreach (var rel in targets)
        {
            var src = ReadSource(rel);

            // 🔴 주석을 먼저 떼어낸다. 작업지시서 §5 G2 ⓒ 의 `grep -c "_ = _email"` 를 글자 그대로 쓰면
            //   「`_ = _email.SendAsync(…)` 로 버려서 아무도 몰랐다」고 **설명하는 주석**까지 세어
            //   봉합을 끝낸 코드가 빨간불이 된다(2026-10-15 로컬 실측: 1곳 FAIL — 그 1곳이 주석이었다).
            //   재는 것은 **실행되는 코드**다.
            var code = System.Text.RegularExpressions.Regex.Replace(src, @"//[^\n]*", "");

            // 작업지시서 §5 G2 ⓒ 의 숫자 그대로: 실행코드에서 `_ = _email.` == 0
            var discarded = System.Text.RegularExpressions.Regex.Matches(code, @"_\s*=\s*_email\s*\.").Count;
            Assert.True(discarded == 0,
                $"{gate} {rel} 에 버려지는 발송 결과가 {discarded}곳 남았다 — 되살리면 메일 실패를 다시 아무도 모른다.");

            // 🔴 버림을 없앴으면 결과를 **쓰는** 자리가 있어야 한다. 그냥 지운 것과 구별한다.
            Assert.Contains("ReportMailAsync", src);
        }

        // 결과를 받는 쪽이 실패를 경고로 남기는가 — 「조용히 성공처럼」을 막는다.
        var landing = ReadSource(targets[0]);
        Assert.Contains("메일 발송 실패", landing);
        var reseller = ReadSource(targets[1]);
        Assert.Contains("메일 발송 실패", reseller);
    }

    // ══════════════════════════════════════════════════════════════
    // 도구
    // ══════════════════════════════════════════════════════════════

    private sealed class HistRow
    {
        public string ToEmail { get; set; } = "";
        public string? Subject { get; set; }
        public string Result { get; set; } = "";
        public string? ReasonCode { get; set; }
        public string? Reason { get; set; }
        public string? Caller { get; set; }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 레포 루트를 모른다.");
    }

    private static string ReadSource(string relative)
    {
        var path = Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"원본 파일이 없다: {relative}");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// 격리 DB 1개 + 실물 <see cref="SchemaMigrator"/> 적용.
    /// 선례·같은 방식: <c>BackofficeCsOutboundGateTests.CsGateDb</c> (CI <c>db-gate</c> 잡에서 그대로 돈다).
    /// </summary>
    private sealed class MailGateDb : IAsyncDisposable
    {
        private readonly string _name;
        private readonly bool _created;

        private MailGateDb(string name, bool created) { _name = name; _created = created; }

        private static string BoSqlDir() => Path.Combine(RepoRoot(), "installer", "backoffice");

        private static string ServerConnString()
        {
            var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
            var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
            var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root";
            var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
            return $"Server={host};Port={port};User={user};Password={pass};"
                 + "DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;";
        }

        public string ConnString => ServerConnString().Replace("User=", $"Database={_name};User=");

        public IConfiguration Config(Dictionary<string, string?>? extra = null)
        {
            var values = new Dictionary<string, string?> { ["ConnectionStrings:BackofficeDb"] = ConnString };
            if (extra is not null)
            {
                foreach (var kv in extra) values[kv.Key] = kv.Value;
            }
            return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        }

        public static async Task<MailGateDb?> TryCreateAsync(string gateName, string defaultName)
        {
            var overrideName = Environment.GetEnvironmentVariable("HITPAN_BO_GATE_DB");
            var useOverride = !string.IsNullOrWhiteSpace(overrideName);
            var name = useOverride ? overrideName!.Trim() : defaultName;

            if (useOverride)
            {
                // 지정 DB 는 표를 비우므로 시험 성격의 이름만 허용 — 운영·실데이터 축 오폭 차단(#39).
                var lower = name.ToLowerInvariant();
                if (lower is "hitpan_erp" or "hitpan_erp_t004" or "hitpan_backoffice" or "hitpan_e2e"
                    || (!lower.Contains("test") && !lower.Contains("gate")))
                {
                    throw new Xunit.Sdk.XunitException(
                        $"HITPAN_BO_GATE_DB={name} — 시험 DB 가 아니다(이름에 test/gate 필요 · 운영 축 금지).");
                }
            }

            var created = false;
            try
            {
                await using var admin = new MySqlConnection(ServerConnString());
                await admin.OpenAsync();
                if (useOverride)
                {
                    var tables = (await admin.QueryAsync<string>(
                        "SELECT table_name FROM information_schema.tables WHERE table_schema = @Db AND table_type = 'BASE TABLE'",
                        new { Db = name })).ToList();
                    if (tables.Count > 0)
                    {
                        await admin.ExecuteAsync($"USE `{name}`; SET FOREIGN_KEY_CHECKS = 0; "
                            + string.Join(" ", tables.Select(t => $"DROP TABLE IF EXISTS `{t}`;"))
                            + " SET FOREIGN_KEY_CHECKS = 1;");
                    }
                }
                else
                {
                    await admin.ExecuteAsync($"DROP DATABASE IF EXISTS `{name}`; "
                        + $"CREATE DATABASE `{name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
                    created = true;
                }
            }
            catch (MySqlException ex)
            {
                Console.Error.WriteLine($"[{gateName}] DB 준비 실패({ex.Number}): {ex.Message}");
                DbGateEnvironment.SkipOrFailStrict(gateName);
                return null;
            }

            var env = new MailGateDb(name, created);
            await new SchemaMigrator(env.ConnString, BoSqlDir(), NullLogger<SchemaMigrator>.Instance).ApplyAsync();
            return env;
        }

        public async Task<MySqlConnection> OpenAsync()
        {
            var c = new MySqlConnection(ConnString);
            await c.OpenAsync();
            return c;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_created) return;
            try
            {
                await using var admin = new MySqlConnection(ServerConnString());
                await admin.OpenAsync();
                await admin.ExecuteAsync($"DROP DATABASE IF EXISTS `{_name}`;");
            }
            catch (MySqlException ex)
            {
                // #15 — 뒷정리 실패를 삼키지 않는다(다음 실행의 DROP IF EXISTS 가 지운다).
                Console.Error.WriteLine($"[작B1 갈래B 게이트] 격리 DB 뒷정리 실패: {ex.Message}");
            }
        }
    }
}
