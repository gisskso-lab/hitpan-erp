using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using HitPan.API.Controllers;
using HitPan.Backoffice.API.Services;
using HitPan.Infrastructure.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-Z5</b> — 계정 과금 어휘 전환, 보내는 쪽(20261006작8 갈래 다 · 게이트 G-3).
/// 웹훅 송신 payload 에 <c>ExtraAccounts</c> 가 <b>실제로 실리고</b>, <c>ExtraDeviceSlots</c>·구 라우트가
/// <b>병행 보존</b>되고(#37), 그 payload 를 ERP 수신부(<see cref="WebhookInboundController"/> — 무접촉)가
/// <b>실제로 읽는지</b>를 잰다. 대조군: 키 이름이 틀린 payload 는 수신부가 안 읽는다.
/// </summary>
/// <remarks>
/// <para>격리 백오피스 DB 는 실물 <see cref="SchemaMigrator"/> 로 <c>installer/backoffice/00·30</c> 을 적용해 만든다
/// (= 운영 적용 경로 그대로 — 30 번 DDL 이 실제로 적용되는지도 이 게이트가 잰다).
/// 격리 ERP DB 는 출하 DDL(#36)로 만든다. 판정 SQL 은 결과 확인용 SELECT 뿐.</para>
/// <para>⚠️ DB 게이트는 개발 PC 에서 SKIP 이 정상(<c>hitpan</c> 은 CREATE DATABASE 거부) —
/// CI <c>db-gate</c>(<c>HITPAN_REQUIRE_DB</c>)가 계측 경로. 운영 무접촉(#39) — 임시 DB 만 만들고 지운다.</para>
/// <para>⚠️ G-Z5b 는 수신부 <c>BuildConnectionString()</c> 이 읽는 <c>DB_*</c> 환경변수를 잠시 격리 ERP DB 로
/// 돌린다(BackupCredentialGate 선례) — 끝나면 원복. 같은 이유로 전용 컬렉션(직렬)이다.</para>
/// </remarks>
[Collection("WebhookAccountVocabGate")]
public sealed class WebhookAccountVocabGateTests : IDisposable
{
    private readonly string _boDb = "hitpan_wavo_bo_" + Guid.NewGuid().ToString("N")[..8];
    private readonly string _erpDb = "hitpan_wavo_erp_" + Guid.NewGuid().ToString("N")[..8];
    private readonly string _tenantId = Guid.NewGuid().ToString();
    private readonly string _tenantCode = "WV" + Guid.NewGuid().ToString("N")[..6];
    private bool _boCreated;
    private bool _erpCreated;
    private string? _scriptsTempDir;

    private const string TestKey = "test-webhook-vocab-key-0123456789abcdef";

    // ══ 준비물 — AccountSeatGateTests 와 같은 방식 ══

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다.");
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

    // 작6 선례 — 격리 DB 연결은 풀을 끈다(닫으면 바로 서버 접속이 끊긴다).
    private string BoConnString() => ServerConnString().Replace("User=", $"Database={_boDb};User=") + "Pooling=false;";
    private string ErpConnString() => ServerConnString().Replace("User=", $"Database={_erpDb};User=") + "Pooling=false;";

    private static string MysqlExe() =>
        Environment.GetEnvironmentVariable("HITPAN_MYSQL") ?? @"C:\Program Files\MariaDB 11.4\bin\mysql.exe";

    private bool Probe(string gate)
    {
        if (DbGateEnvironment.IsCi) return true;
        var ok = File.Exists(MysqlExe());
        if (ok)
        {
            try
            {
                using var c = new MySqlConnection(ServerConnString());
                c.Open();
                c.Execute($"CREATE DATABASE IF NOT EXISTS `{_boDb}`");
                c.Execute($"DROP DATABASE IF EXISTS `{_boDb}`");
            }
            catch (MySqlException ex)
            {
                Console.Error.WriteLine($"[{gate}] 서버 접속·권한 없음: {ex.Message}");
                ok = false;
            }
        }
        if (!ok) return !DbGateEnvironment.SkipOrFail(gate);
        return true;
    }

    /// <summary>백오피스 격리 DB — 실물 SchemaMigrator 로 00(핵심)·30(Z5 extra_accounts) 적용.</summary>
    private async Task SetUpBackofficeDbAsync()
    {
        using (var admin = new MySqlConnection(ServerConnString()))
        {
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{_boDb}`; "
                        + $"CREATE DATABASE `{_boDb}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        }
        _boCreated = true;

        // 운영 적용 경로 그대로(SchemaMigrator) — 단, 90·91 시드는 env 주입(${VAR}) 전용이라 밖에 둔다.
        _scriptsTempDir = Path.Combine(Path.GetTempPath(), "hitpan_wavo_sql_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_scriptsTempDir);
        var src = Path.Combine(RepoRoot(), "installer", "backoffice");
        foreach (var f in new[] { "00_backoffice_core.sql", "30_backoffice_tenants_extra_accounts.sql" })
            File.Copy(Path.Combine(src, f), Path.Combine(_scriptsTempDir, f));

        var migrator = new SchemaMigrator(BoConnString(), _scriptsTempDir, NullLogger<SchemaMigrator>.Instance);
        await migrator.ApplyAsync();
    }

    /// <summary>ERP 격리 DB — 출하 DDL(#36) 한 방 적재 (AccountSeatGateTests.SetUpFreshInstall 과 같은 방식).</summary>
    private void SetUpErpDb()
    {
        var ddlPath = Path.Combine(RepoRoot(), "installer", "hitpan_db_clean.sql");
        using (var admin = new MySqlConnection(ServerConnString()))
        {
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{_erpDb}`; "
                        + $"CREATE DATABASE `{_erpDb}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        }
        _erpCreated = true;

        var psi = new System.Diagnostics.ProcessStartInfo(MysqlExe())
        {
            RedirectStandardInput = true, RedirectStandardError = true, UseShellExecute = false
        };
        psi.ArgumentList.Add($"--host={Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost"}");
        psi.ArgumentList.Add($"--port={Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306"}");
        psi.ArgumentList.Add($"-u{Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root"}");
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS");
        if (!string.IsNullOrEmpty(pass)) psi.ArgumentList.Add($"-p{pass}");
        psi.ArgumentList.Add("--default-character-set=utf8mb4");
        psi.ArgumentList.Add(_erpDb);
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.StandardInput.Write(File.ReadAllText(ddlPath));
        proc.StandardInput.Close();
        var err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        Assert.True(proc.ExitCode == 0, $"출하 DDL import 실패:\n{err}");
    }

    public void Dispose()
    {
        try
        {
            if (_boCreated || _erpCreated)
            {
                using var admin = new MySqlConnection(ServerConnString());
                admin.Open();
                if (_boCreated) admin.Execute($"DROP DATABASE IF EXISTS `{_boDb}`;");
                if (_erpCreated) admin.Execute($"DROP DATABASE IF EXISTS `{_erpDb}`;");
            }
            if (_scriptsTempDir is not null && Directory.Exists(_scriptsTempDir))
                Directory.Delete(_scriptsTempDir, recursive: true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WebhookAccountVocab] 임시 DB·폴더 정리 실패 — 손으로 지워라: {_boDb} · {_erpDb} ({ex.Message})");
        }
    }

    // ══ 송신(실물 WebhookOutboundService) ══

    private WebhookOutboundService OutboundService()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:BackofficeDb"] = BoConnString(),
            ["Bootstrap:TokenKey"] = TestKey
        }).Build();
        return new WebhookOutboundService(cfg, NullLogger<WebhookOutboundService>.Instance);
    }

    private async Task SeedBoTenantAsync(int extraAccounts, int extraDeviceSlots)
    {
        await using var db = new MySqlConnection(BoConnString());
        await db.OpenAsync();
        await db.ExecuteAsync(@"
            INSERT INTO tenants (tenant_id, tenant_code, company_name, subscription_tier, status,
                                 max_users, extra_device_slots, extra_accounts)
            VALUES (@T, @C, '계정과금게이트', 'basic', 'active', 5, @S, @A)",
            new { T = _tenantId, C = _tenantCode, S = extraDeviceSlots, A = extraAccounts });
    }

    private sealed record OutboxRow(string EventType, string TargetUrl, string PayloadJson, string Signature, string Nonce);

    private async Task<List<OutboxRow>> OutboxAsync()
    {
        await using var db = new MySqlConnection(BoConnString());
        await db.OpenAsync();
        return (await db.QueryAsync<OutboxRow>(@"
            SELECT event_type AS EventType, target_url AS TargetUrl, payload_json AS PayloadJson,
                   signature AS Signature, nonce AS Nonce
            FROM webhook_outbox WHERE tenant_id = @T ORDER BY outbox_id",
            new { T = _tenantId })).ToList();
    }

    [Fact(DisplayName = "G-Z5a 🔴 송신 payload 캡처 — ExtraAccounts 실림 + ExtraDeviceSlots·device-slot 라우트 병행(#37) + 계정 과금 라우트 = subscription")]
    public async Task Z5a_Outbound_Payload_Carries_Both_Keys_And_Routes()
    {
        if (!Probe("G-Z5a")) return;
        await SetUpBackofficeDbAsync();
        await SeedBoTenantAsync(extraAccounts: 2, extraDeviceSlots: 1);

        var svc = OutboundService();
        await svc.EmitSubscriptionChangedAsync(_tenantId);
        await svc.EmitDeviceSlotChangedAsync(_tenantId);   // #37 — 구 이벤트 1 + 계정 과금 1 병행

        var rows = await OutboxAsync();
        Assert.Equal(3, rows.Count);

        foreach (var row in rows)
        {
            using var doc = JsonDocument.Parse(row.PayloadJson);
            Assert.True(doc.RootElement.TryGetProperty("ExtraAccounts", out var ea),
                $"payload 에 ExtraAccounts 가 없다 ({row.EventType}): {row.PayloadJson}");
            Assert.Equal(2, ea.GetInt32());
            Assert.True(doc.RootElement.TryGetProperty("ExtraDeviceSlots", out var es),
                $"구 키 ExtraDeviceSlots 가 빠졌다(#37 위반 · {row.EventType}): {row.PayloadJson}");
            Assert.Equal(1, es.GetInt32());
        }

        // 라우트 — ERP 수신부 실측 대조: ExtraAccounts 를 읽는 라우트는 subscription 뿐.
        var byEvent = rows.ToDictionary(r => r.EventType, r => r.TargetUrl);
        Assert.EndsWith("/api/internal/webhook/subscription", byEvent["subscription_changed"]);
        Assert.EndsWith("/api/internal/webhook/subscription", byEvent["account_changed"]);
        Assert.EndsWith("/api/internal/webhook/device-slot", byEvent["device_slot_changed"]);   // 구 라우트 병행(#37)
    }

    // ══ 수신 대조(실물 WebhookInboundController — 코드 무접촉 · 불러서 본다) ══

    private static readonly string[] DbEnvKeys = { "DB_HOST", "DB_PORT", "DB_NAME", "DB_USER", "DB_PASSWORD" };

    private static async Task<int> PostSubscriptionAsync(string body, string signature, string nonce)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bootstrap:TokenKey"] = TestKey
        }).Build();
        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        ctx.Request.Headers["X-Hitpan-Signature"] = signature;
        ctx.Request.Headers["X-Hitpan-Nonce"] = nonce;
        var ctl = new WebhookInboundController(cfg, NullLogger<WebhookInboundController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx }
        };
        var result = await ctl.Subscription(default);
        return (result as IStatusCodeActionResult)?.StatusCode ?? 0;
    }

    /// <summary>수신부(VerifySignature)와 같은 순서로 키를 골라 같은 모양(base64url)으로 서명한다 — 대조군 재서명용.</summary>
    private static string Sign(string body)
    {
        var key = TenantConfigReader.Get("HITPAN_BOOTSTRAP_TOKEN_KEY") ?? TestKey;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private async Task<int> ErpExtraAccountsAsync()
    {
        await using var db = new MySqlConnection(ErpConnString());
        await db.OpenAsync();
        return await db.ExecuteScalarAsync<int>(
            "SELECT extra_accounts FROM local_subscription WHERE tenant_id = @T", new { T = _tenantId });
    }

    [Fact(DisplayName = "G-Z5b 🔴 수신부가 실제로 읽는 키와 일치 — 송신 payload 그대로 → extra_accounts 반영 · 대조군: 키 이름 틀리면 안 읽힘")]
    public async Task Z5b_Inbound_Reads_Captured_Payload_And_Rejects_Wrong_Key()
    {
        if (!Probe("G-Z5b")) return;
        await SetUpBackofficeDbAsync();
        SetUpErpDb();
        await SeedBoTenantAsync(extraAccounts: 2, extraDeviceSlots: 1);

        var svc = OutboundService();

        // 수신부 BuildConnectionString() 은 TenantConfigReader(DB_*) 를 읽는다 — 잠시 격리 ERP DB 로 돌린다(원복 보장).
        var saved = DbEnvKeys.ToDictionary(k => k, k => Environment.GetEnvironmentVariable(k));
        try
        {
            Environment.SetEnvironmentVariable("DB_HOST", Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost");
            Environment.SetEnvironmentVariable("DB_PORT", Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306");
            Environment.SetEnvironmentVariable("DB_NAME", _erpDb);
            Environment.SetEnvironmentVariable("DB_USER", Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root");
            Environment.SetEnvironmentVariable("DB_PASSWORD", Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "");

            if (TenantConfigReader.Get("DB_NAME") != _erpDb)
            {
                // 이 프로세스에 db.conf 가 잡혀 env 폴백이 안 통한다 — 로컬 한정 상황. CI 에선 db.conf 가 없다.
                if (DbGateEnvironment.SkipOrFail("G-Z5b (db.conf 우선순위에 가림)")) return;
            }

            // ① 양성 — subscription_changed payload 그대로 → ERP 가 ExtraAccounts(2) 를 읽는다.
            await svc.EmitSubscriptionChangedAsync(_tenantId);
            var sub = (await OutboxAsync()).Single(r => r.EventType == "subscription_changed");
            Assert.Equal(200, await PostSubscriptionAsync(sub.PayloadJson, sub.Signature, sub.Nonce));
            Assert.Equal(2, await ErpExtraAccountsAsync());

            // ② 양성 — 계정 과금 송신 길(EmitAccountChangedAsync → subscription 라우트)로 값 변경이 실제로 간다.
            await using (var bo = new MySqlConnection(BoConnString()))
            {
                await bo.OpenAsync();
                await bo.ExecuteAsync("UPDATE tenants SET extra_accounts = 3 WHERE tenant_id = @T", new { T = _tenantId });
            }
            await svc.EmitAccountChangedAsync(_tenantId);
            var acc = (await OutboxAsync()).Single(r => r.EventType == "account_changed");
            Assert.EndsWith("/api/internal/webhook/subscription", acc.TargetUrl);
            Assert.Equal(200, await PostSubscriptionAsync(acc.PayloadJson, acc.Signature, acc.Nonce));
            Assert.Equal(3, await ErpExtraAccountsAsync());

            // ③ 대조군 — 키 이름이 틀린 payload(값 9)는 수신부가 **받지만 안 읽는다**(200 인데 값 무변).
            await using (var bo = new MySqlConnection(BoConnString()))
            {
                await bo.OpenAsync();
                await bo.ExecuteAsync("UPDATE tenants SET extra_accounts = 9 WHERE tenant_id = @T", new { T = _tenantId });
            }
            await svc.EmitAccountChangedAsync(_tenantId);
            var acc9 = (await OutboxAsync()).Last(r => r.EventType == "account_changed");   // outbox_id 순 — 마지막 = 방금 발행분
            Assert.NotEqual(acc.Nonce, acc9.Nonce);
            var node = JsonNode.Parse(acc9.PayloadJson)!.AsObject();
            Assert.Equal(9, (int)node["ExtraAccounts"]!);
            node.Remove("ExtraAccounts");
            node["ExtraAccountsTypo"] = 9;                       // 키 이름만 틀리게 — 형·값 동일
            var mutated = node.ToJsonString();
            Assert.Equal(200, await PostSubscriptionAsync(mutated, Sign(mutated), acc9.Nonce));
            Assert.Equal(3, await ErpExtraAccountsAsync());       // 9 가 안 들어왔다 — 틀린 키는 안 읽힌다
        }
        finally
        {
            foreach (var k in DbEnvKeys) Environment.SetEnvironmentVariable(k, saved[k]);
        }
    }
}
