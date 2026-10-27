using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261027작G0S — 랜딩 입구 정리(결재문 <c>docs/운영기록/20261027_결재_베타로드맵_사장님최종_기준선_G0착수.md</c> §7).
/// (G0S-N-A1 · G0S-N-A2 · G0S-N-B1 · 대조군 G0S-C1)
///
/// <para><b>무엇을 재나</b>: ① 결제확인 입구는 서버에 등록되지 않는다(익명·본사 관리자 모두 404 · 표 변화 0 · 메일 0).
/// ② 가입 경로는 DNS 삭제를 부르지 않는다(대역 CF 의 삭제 호출 0회). ③ 정상 가입은 그대로 된다(대조군).
/// 활성·시리얼 발급은 본사 승인(<c>SignupsAdminController</c>) 한 곳에서만 나간다.</para>
///
/// <para><b>글자가 아니라 동작</b>: <c>WebApplicationFactory&lt;Program&gt;</c> 로 백오피스 API 의
/// <b>실제 라우팅·인증·인가 파이프라인</b>을 띄우고 HTTP 로 보낸다. 대역은 바깥으로 나가는 축만 바꾼다
/// (Cloudflare · 메일 · 웹훅 · 계정 판독). 국세청은 키를 주지 않아 컨트롤러의 「키 없음」 분기로 지나간다
/// — 그 분기는 DNS 블록보다 <b>앞</b>에서 끝나고 흐름을 끊지 않는다(개발명세서 Q7).</para>
///
/// <para>DB: 404 판정 2건(<see cref="BackofficeAnonymousEntryRouteGateTests"/>)은 DB 없이 돈다(build 잡).
/// 표 변화·가입 흐름은 격리 DB 가 필요하다 — <c>HITPAN_BO_GATE_DB</c>(이름에 test/gate) 또는 CI 격리 DB
/// <c>bo_g0s_entry_gate</c>. 못 붙으면 <see cref="DbGateEnvironment.SkipOrFail"/>(CI 는 던진다).</para>
/// </summary>
[Collection(BoSignupTenantKeyGateCollection.Name)]
public sealed class BackofficeAnonymousEntrySealGateTests : IDisposable
{
    private const string GateDbDefault = "bo_g0s_entry_gate";

    private readonly string _dbName;
    private readonly bool _overrideDb;
    private bool _createdDb;

    public BackofficeAnonymousEntrySealGateTests()
    {
        var overrideName = Environment.GetEnvironmentVariable("HITPAN_BO_GATE_DB");
        _overrideDb = !string.IsNullOrWhiteSpace(overrideName);
        _dbName = _overrideDb ? overrideName!.Trim() : GateDbDefault;

        if (_overrideDb)
        {
            // 지정 DB 는 안의 표를 전부 비운다 — 시험 성격의 이름만 허용(#39 · 선례 BoSignupTenantKeyGateTests).
            var lower = _dbName.ToLowerInvariant();
            if (lower is "hitpan_erp" or "hitpan_backoffice"
                || (!lower.Contains("test") && !lower.Contains("gate")))
            {
                throw new Xunit.Sdk.XunitException(
                    $"HITPAN_BO_GATE_DB={_dbName} — 시험 DB 가 아니다(이름에 test/gate 필요 · 운영 축 금지).");
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G0S-N-A1 · N-A2 — 결제확인 입구: 익명·본사 관리자 모두 404 · 표 변화 0 · 메일 0
    // ══════════════════════════════════════════════════════════════

    [Theory(DisplayName = "G0S-N-A1·A2 결제확인 입구는 미등록 — 익명·본사 관리자 모두 404 · landing_signups·tenants 변화 0 · 메일 0")]
    [InlineData(null)]
    [InlineData("platform_owner")]
    public async Task G0S_N_A_결제확인_입구는_404_표변화0_메일0(string? accountType)
    {
        const string gate = "G0S-N-A 결제확인 입구 미등록";
        if (!await TrySetUpDbAsync(gate)) return;

        await using var host = new EntryGateFactory(DbConnString());
        using var http = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // 정상 가입 1건으로 실물 신청 토큰을 만든다 — 404 가 「토큰이 틀려서」가 아님을 보장한다.
        var probe = NewSignupProbe();
        var (signupCode, token) = await PostSignupAsync(http, probe);
        Assert.True(signupCode == HttpStatusCode.OK, $"{gate} 준비 가입이 실패했다(HTTP {(int)signupCode}) — 과녁 전에 끊겼다.");
        Assert.False(string.IsNullOrWhiteSpace(token), $"{gate} 가입 응답에 신청 토큰이 없다.");

        var before = await SnapshotAsync();
        var mailBefore = host.Mail.Count;

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/landing/confirm-payment")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { signupToken = token }), Encoding.UTF8, "application/json")
        };
        if (accountType is not null)
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", EntryGateFactory.Jwt(accountType));
        using var res = await http.SendAsync(req);

        Assert.True(res.StatusCode == HttpStatusCode.NotFound,
            $"{gate} ({accountType ?? "익명"}) 기대 404 · 실제 {(int)res.StatusCode} — 입구가 서버에 등록돼 있다.");

        var after = await SnapshotAsync();
        Assert.Equal(before, after);                    // 신청 상태 · 고객사 상태 · 라이선스 해시 칸 그대로
        Assert.Equal(mailBefore, host.Mail.Count);      // 라이선스 메일 0통
        Assert.Equal("submitted", after.SignupStatus);
        Assert.Equal("pending", after.TenantStatus);
        Assert.Equal(0, after.TenantsWithLicense);
    }

    // ══════════════════════════════════════════════════════════════
    // G0S-N-B1 — 가입 경로는 DNS 삭제를 부르지 않는다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G0S-N-B1 가입(희망 주소가 tenants 에 없음 · CF 설정됨) → DNS 삭제 호출 0회")]
    public async Task G0S_N_B1_가입은_DNS_삭제를_부르지_않는다()
    {
        const string gate = "G0S-N-B1 가입 경로 DNS 삭제 0";
        if (!await TrySetUpDbAsync(gate)) return;

        await using var host = new EntryGateFactory(DbConnString());
        using var http = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var probe = NewSignupProbe();
        // 전제 확인 — 희망 주소가 tenants 에 없는 이름이어야 옛 블록의 조건이 참이 된다.
        await using (var db = new MySqlConnection(DbConnString()))
        {
            var hit = await db.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM tenants WHERE domain_alias = @A", new { A = probe.Domain });
            Assert.Equal(0L, hit);
        }
        Assert.True(host.Cf.IsConfigured, $"{gate} 대역 CF 가 「설정됨」이 아니다 — 블록 조건을 못 만든다.");

        var (code, token) = await PostSignupAsync(http, probe);

        // 흐름이 DNS 블록 자리를 **지나갔는지** 먼저 본다 — 앞에서 끊기면 아래 0회는 아무것도 안 잰 것이다.
        Assert.True(code == HttpStatusCode.OK, $"{gate} 가입이 실패했다(HTTP {(int)code}) — 블록 자리 앞에서 끊겼다.");
        Assert.False(string.IsNullOrWhiteSpace(token));

        Assert.True(host.Cf.RevokeByDomainCalls == 0,
            $"{gate} 가입 경로가 DNS 삭제를 {host.Cf.RevokeByDomainCalls}회 불렀다 (대상: {string.Join(",", host.Cf.RevokedNames)}).");
        Assert.Equal(0, host.Cf.OtherCalls);
    }

    // ══════════════════════════════════════════════════════════════
    // G0S-C1 — 대조군: 정상 가입은 그대로 성공 + 신청 1행 · 고객사 대기 1행
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G0S-C1 대조군 — 정상 가입은 200 · 신청 1행(submitted) · 고객사 1행(pending) · 접수 메일 1통")]
    public async Task G0S_C1_정상가입은_그대로_된다()
    {
        const string gate = "G0S-C1 정상 가입 대조군";
        if (!await TrySetUpDbAsync(gate)) return;

        await using var host = new EntryGateFactory(DbConnString());
        using var http = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var probe = NewSignupProbe();
        var (code, token) = await PostSignupAsync(http, probe);

        Assert.True(code == HttpStatusCode.OK, $"{gate} 가입이 실패했다(HTTP {(int)code}).");
        Assert.False(string.IsNullOrWhiteSpace(token));

        await using var db = new MySqlConnection(DbConnString());
        var signups = await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM landing_signups WHERE signup_token = @T AND status = 'submitted' AND desired_domain = @D",
            new { T = token, D = probe.Domain });
        var tenants = await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM tenants WHERE domain_alias = @D AND status = 'pending'",
            new { D = probe.Domain });
        Assert.Equal(1L, signups);
        Assert.Equal(1L, tenants);
        Assert.Equal(1, host.Mail.Count);
    }

    // ══════════════════════════════════════════════════════════════
    // 도구
    // ══════════════════════════════════════════════════════════════

    private sealed record SignupProbe(string Company, string BizNo, string Email, string Domain);

    private static SignupProbe NewSignupProbe()
    {
        var id = Guid.NewGuid().ToString("N");
        // 사업자번호 모양은 런타임 난수 10자리 — 국세청을 부르지 않는 분기라 체크섬과 무관하다.
        var digits = string.Concat(RandomNumberGenerator.GetBytes(10).Select(b => (char)('0' + b % 10)));
        return new SignupProbe($"g0s-gate-{id[..8]}", digits, $"g0s-{id[..8]}@gate.invalid", $"g0s{id[..10]}");
    }

    private static async Task<(HttpStatusCode code, string? token)> PostSignupAsync(HttpClient http, SignupProbe p)
    {
        var body = JsonSerializer.Serialize(new
        {
            companyName = p.Company,
            bizNo = p.BizNo,
            ceoName = "g0s-gate",
            email = p.Email,
            phone = "0",
            planType = "basic",
            desiredDomain = p.Domain,
            agreeTerms = true,
            agreePrivacy = true
        });
        using var res = await http.PostAsync("/api/landing/signup", new StringContent(body, Encoding.UTF8, "application/json"));
        var text = await res.Content.ReadAsStringAsync();
        string? token = null;
        if (res.IsSuccessStatusCode)
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("signupToken", out var t)) token = t.GetString();
        }
        return (res.StatusCode, token);
    }

    private sealed record Snapshot(long Signups, long Tenants, string? SignupStatus, string? TenantStatus, long TenantsWithLicense);

    private async Task<Snapshot> SnapshotAsync()
    {
        await using var db = new MySqlConnection(DbConnString());
        var signups = await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM landing_signups");
        var tenants = await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM tenants");
        var sStatus = await db.ExecuteScalarAsync<string?>("SELECT status FROM landing_signups ORDER BY signup_id DESC LIMIT 1");
        var tStatus = await db.ExecuteScalarAsync<string?>("SELECT status FROM tenants ORDER BY created_at DESC LIMIT 1");
        var licensed = await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM tenants WHERE license_key_hash <> ''");
        return new Snapshot(signups, tenants, sStatus, tStatus, licensed);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — installer/backoffice 를 읽을 수 없다.");
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

    private async Task<bool> TrySetUpDbAsync(string gateName)
    {
        try
        {
            await using (var admin = new MySqlConnection(ServerConnString()))
            {
                await admin.OpenAsync();
                if (_overrideDb)
                {
                    var tables = (await admin.QueryAsync<string>(
                        "SELECT table_name FROM information_schema.tables WHERE table_schema = @Db AND table_type = 'BASE TABLE'",
                        new { Db = _dbName })).ToList();
                    if (tables.Count > 0)
                    {
                        await admin.ExecuteAsync($"USE `{_dbName}`; SET FOREIGN_KEY_CHECKS = 0; "
                            + string.Join(" ", tables.Select(t => $"DROP TABLE IF EXISTS `{t}`;"))
                            + " SET FOREIGN_KEY_CHECKS = 1;");
                    }
                }
                else
                {
                    await admin.ExecuteAsync($"DROP DATABASE IF EXISTS `{_dbName}`; "
                        + $"CREATE DATABASE `{_dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
                    _createdDb = true;
                }
            }

            // 스키마는 실제 SchemaMigrator 가 installer/backoffice/*.sql 을 번호순 적용(백오피스 진실원).
            var migrator = new SchemaMigrator(
                DbConnString(), Path.Combine(RepoRoot(), "installer", "backoffice"), NullLogger<SchemaMigrator>.Instance);
            await migrator.ApplyAsync();
            return true;
        }
        catch (MySqlException ex)
        {
            Console.Error.WriteLine($"[G0S 입구 게이트] DB 준비 실패({ex.Number}): {ex.Message}");
            return DbGateEnvironment.SkipOrFail(gateName) && false;
        }
    }

    public void Dispose()
    {
        if (!_createdDb) return;
        try
        {
            using var admin = new MySqlConnection(ServerConnString());
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{_dbName}`;");
        }
        catch (MySqlException ex)
        {
            // #15 — 뒷정리 실패는 찍는다(다음 실행의 DROP IF EXISTS 가 지운다).
            Console.Error.WriteLine($"[G0S 입구 게이트] 격리 DB 뒷정리 실패: {ex.Message}");
        }
    }
}

/// <summary>
/// G0S 404 판정 — DB 없이 돈다(build 잡 전량 실행에 자동 포함). 연결문자열은 닫힌 포트.
/// 표 변화 0 은 위 DB 클래스가 같은 요청으로 잰다.
/// </summary>
[Collection(BoSignupTenantKeyGateCollection.Name)]
public sealed class BackofficeAnonymousEntryRouteGateTests
{
    private const string NoDb = "Server=127.0.0.1;Port=1;Database=hitpan_g0s_gate_nonexistent";

    [Theory(DisplayName = "G0S-N-A1·A2(DB 없음) 결제확인 입구 — 익명·본사 관리자 모두 404 · 메일 0")]
    [InlineData(null)]
    [InlineData("platform_admin")]
    [InlineData("platform_owner")]
    public async Task G0S_N_A_결제확인_입구는_DB_없이도_404(string? accountType)
    {
        await using var host = new EntryGateFactory(NoDb);
        using var http = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/landing/confirm-payment")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { signupToken = "gate-" + Guid.NewGuid().ToString("N") }),
                Encoding.UTF8, "application/json")
        };
        if (accountType is not null)
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", EntryGateFactory.Jwt(accountType));
        using var res = await http.SendAsync(req);

        Assert.True(res.StatusCode == HttpStatusCode.NotFound,
            $"G0S-N-A ({accountType ?? "익명"}) 기대 404 · 실제 {(int)res.StatusCode} — 입구가 서버에 등록돼 있다.");
        Assert.Equal(0, host.Mail.Count);
    }

    [Fact(DisplayName = "G0S-C0 대조군(DB 없음) — 같은 호스트에서 가입 입구는 등록돼 있다(404 아님 · 인증 미들웨어에 안 막힘)")]
    public async Task G0S_C0_가입_입구는_등록돼_있다()
    {
        // 404 판정이 「호스트가 아예 라우팅을 못 한다」로 초록이 되는 것을 막는 대조군.
        await using var host = new EntryGateFactory(NoDb);
        using var http = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var res = await http.PostAsync("/api/landing/signup", new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.NotEqual(HttpStatusCode.NotFound, res.StatusCode);
        Assert.DoesNotContain(res.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
    }
}

/// <summary>
/// G0S 게이트 호스트 — 실물 <c>Program</c> 파이프라인 + 바깥으로 나가는 축만 대역.
/// </summary>
internal sealed class EntryGateFactory : WebApplicationFactory<HitPan.Backoffice.API.Program>
{
    // 🔴 서명키 「모양」을 소스에 두지 않는다 — 실행할 때마다 그 자리에서 만든다(자격증명 모양 금지).
    private static readonly string Secret =
        "G0S-" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

    private readonly string _connString;

    public CountingCf Cf { get; } = new();
    public CountingMail Mail { get; } = new();

    public EntryGateFactory(string connString) => _connString = connString;

    public static string Jwt(string accountType)
    {
        static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var header = B64(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
        var payload = B64(Encoding.UTF8.GetBytes(
            $"{{\"sub\":\"g0s-gate\",\"account_type\":\"{accountType}\"," +
            $"\"iss\":\"hitpan-backoffice\",\"aud\":\"backoffice\",\"exp\":{exp}}}"));
        using var mac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var sig = B64(mac.ComputeHash(Encoding.UTF8.GetBytes($"{header}.{payload}")));
        return $"{header}.{payload}.{sig}";
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("HITPAN_BO_JWT_SECRET", Secret);
        Environment.SetEnvironmentVariable("HITPAN_BO_AUTO_MIGRATE", "0");
        // 국세청 키가 프로세스에 있으면 가입이 실제 국세청을 부른다 — 실호출 0 을 지키려 시작 전에 멈춘다.
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NTS_API_KEY")))
            throw new Xunit.Sdk.XunitException("NTS_API_KEY 가 설정된 프로세스 — 이 게이트는 국세청 실호출 0 이 전제다. 지우고 다시 돌려라.");
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:BackofficeDb"] = _connString,
            ["ConnectionStrings:Default"] = _connString,
            ["BizVerify:NtsApiKey"] = "",
            ["Backoffice:BizNoPepper"] = "g0s-" + Guid.NewGuid().ToString("N"),
            ["License:Pepper"] = "g0s-" + Guid.NewGuid().ToString("N"),
        }));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ICloudflareDomainService>(Cf);
            services.AddSingleton<IEmailSender>(Mail);
            services.AddScoped<IWebhookOutboundService, NoopWebhook>();
            services.AddSingleton<IBoLiveAccountReader, OwnerAccountStub>();
            // 웹훅 디스패처(BackgroundService)는 이 게이트의 축이 아니다 — 그 하나만 뺀다.
            foreach (var d in services.Where(d => d.ImplementationType == typeof(WebhookDispatcher)).ToList())
                services.Remove(d);
        });
    }

    private sealed class OwnerAccountStub : IBoLiveAccountReader
    {
        public Task<BoLiveAccount> ReadAsync(string sub, CancellationToken ct = default) =>
            Task.FromResult(new BoLiveAccount
            {
                Found = true,
                IsActive = true,
                EffectiveRole = HitPan.Backoffice.API.Security.BoRoles.PlatformOwner,
                ResellerId = null,
            });
    }

    private sealed class NoopWebhook : IWebhookOutboundService
    {
        public Task EmitSubscriptionChangedAsync(string tenantId, CancellationToken ct = default) => Task.CompletedTask;
        public Task EmitDeviceSlotChangedAsync(string tenantId, CancellationToken ct = default) => Task.CompletedTask;
        public Task EmitAccountChangedAsync(string tenantId, CancellationToken ct = default) => Task.CompletedTask;
    }

    internal sealed class CountingMail : IEmailSender
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);

        public Task<bool> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _count);
            return Task.FromResult(false);
        }

        public Task<bool> SendWithAttachmentAsync(string toEmail, string subject, string htmlBody,
            byte[] attachment, string attachmentFileName, string attachmentMimeType, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _count);
            return Task.FromResult(false);
        }
    }

    /// <summary>「설정됨」으로 보이는 대역 CF — 삭제 호출을 세기만 한다. 다른 호출도 센다(바깥 호출 0).</summary>
    internal sealed class CountingCf : ICloudflareDomainService
    {
        private int _revokeByDomain;
        private int _other;
        public System.Collections.Concurrent.ConcurrentQueue<string> RevokedNames { get; } = new();
        public int RevokeByDomainCalls => Volatile.Read(ref _revokeByDomain);
        public int OtherCalls => Volatile.Read(ref _other);

        public bool IsConfigured => true;

        public Task<bool> RevokeByDomainAsync(string subdomain, CancellationToken ct)
        {
            Interlocked.Increment(ref _revokeByDomain);
            RevokedNames.Enqueue(subdomain);
            return Task.FromResult(false);
        }

        public Task<DomainIssueResult> IssueAsync(string tenantId, string tenantCode, CancellationToken ct)
            => Other<DomainIssueResult>();
        public Task<DomainIssueResult> IssueAsync(string tenantId, string tenantCode, string? domainAlias, CancellationToken ct)
            => Other<DomainIssueResult>();
        public Task<TunnelIssueResult> IssueTunnelAsync(string tenantId, string tenantCode, CancellationToken ct)
            => Other<TunnelIssueResult>();
        public Task UpdateDnsTunnelTargetAsync(string recordId, string domain, string tunnelId, CancellationToken ct)
            => Other<bool>();
        public Task UpdateTunnelIngressAsync(string tunnelId, string hostname, string originUrl, CancellationToken ct)
            => Other<bool>();
        public Task<bool> RevokeAsync(string cfZoneId, string cfRecordId, string? cfTunnelId, CancellationToken ct)
            => Other<bool>();

        private Task<T> Other<T>()
        {
            Interlocked.Increment(ref _other);
            throw new NotSupportedException("G0S 게이트에서 CF 발급·변경 호출 금지");
        }
    }
}
