using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text;
using HitPan.API.Controllers;
using HitPan.API.Middleware;
using HitPan.Application.Interfaces;
using HitPan.Infrastructure.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🚨 <b>G-A2</b> — <c>POST /api/auth/update-consent</c> 는 <b>메인PC 에서만</b> 기록된다. 20260929작3 절A3.
/// <b>미들웨어·라우팅·권한·필터를 실제로 지나가 본다.</b>
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>왜 파이프라인을 통째로 세우나</b> — 9/22 <c>mainpc-proof</c> P0 는 게이트 18건이 전부 초록인데
/// 실제 요청은 <c>TenantMiddleware</c> 에서 401 로 잘렸다. 함수를 따로 부르는 시험은
/// <i>"그 요청이 그 함수까지 닿는가"</i> 를 못 잰다. ⇒ 이 게이트는 실제 <see cref="TenantMiddleware"/> ·
/// 실제 라우팅(<c>MapControllers</c>) · 실제 권한 미들웨어 · 실제 <c>[MainPcOnly]</c> 필터 ·
/// 실제 <see cref="AuthController"/> 를 한 줄로 이어 <b>HTTP 요청 한 건</b>을 흘린다.
/// </para>
/// <para>
/// ⚠️ <c>WebApplicationFactory</c>·<c>TestServer</c> 가 아니다 — 시험 프로젝트에 그 참조가 없고(선례 <c>SessionRecordConcurrentPcGateTests:547</c>),
/// 갈래 F 와 <c>HitPan.Tests.csproj</c> 를 동시에 고치지 않으려고 패키지를 늘리지 않았다.
/// 대신 <see cref="ApplicationBuilder"/> 로 <b>같은 순서</b>(라우팅 → 인증 → 권한 → Tenant → 엔드포인트)를 세운다.
/// 인증(JWT 검증)만 대역이다 — 검증을 통과한 뒤의 <c>User</c> 를 그대로 꽂는다.
/// </para>
/// <para>
/// 🔴 <b>음성 대조군</b> — <c>AuthController.UpdateConsent</c> 의 <c>[MainPcOnly]</c> 한 줄을 빼면
/// 터널 요청이 <b>200 · 행 1</b> 이 되어 FAIL 한다(개발명세서 §4 실측).
/// </para>
/// <para>⚠️ DB 무접촉 — 연결 자리에 INSERT 수를 세는 대역을 꽂는다. <c>build</c> 잡에서 돈다.</para>
/// </remarks>
public sealed class UpdateConsentMainPcGateTests
{
    private const string ConsentPath = "/api/auth/update-consent";

    [Fact(DisplayName = "G-A2 🚨 터널(도메인) + 출입증 없음 → update-consent 403 main_pc_only · 행 0 ([예]·[나중에] 둘 다)")]
    public async Task GA2_터널_출입증없음은_403_행0()
    {
        foreach (var action in new[] { "approve", "reject" })
        {
            var r = await SendAsync(viaTunnel: true, pass: null, action);
            Assert.True(r.Status == StatusCodes.Status403Forbidden,
                $"터널·출입증 없음 {action} → {r.Status} (기대 403) · 본문={r.Body}");
            Assert.Contains("main_pc_only", r.Body, StringComparison.Ordinal);
            Assert.Equal(0, r.Inserts);
        }
    }

    [Fact(DisplayName = "G-A2 🚨 터널 + 가짜 출입증 → 403 · 행 0 (브라우저 자칭 불가)")]
    public async Task GA2_터널_가짜출입증은_403_행0()
    {
        var r = await SendAsync(viaTunnel: true, pass: "made-up-pass", "approve");
        Assert.Equal(StatusCodes.Status403Forbidden, r.Status);
        Assert.Contains("main_pc_only", r.Body, StringComparison.Ordinal);
        Assert.Equal(0, r.Inserts);
    }

    [Fact(DisplayName = "G-A2 🟢 메인PC(루프백 직접) → update-consent 200 · 행 1 (무회귀)")]
    public async Task GA2_루프백은_200_행1()
    {
        foreach (var action in new[] { "approve", "reject" })
        {
            var r = await SendAsync(viaTunnel: false, pass: null, action);
            Assert.True(r.Status == StatusCodes.Status200OK,
                $"루프백 {action} → {r.Status} (기대 200) · 본문={r.Body}");
            Assert.Equal(1, r.Inserts);
        }
    }

    [Fact(DisplayName = "G-A2 🟢 도메인으로 연 메인PC(유효 출입증) → 200 · 행 1")]
    public async Task GA2_터널_유효출입증은_200_행1()
    {
        var r = await SendAsync(viaTunnel: true, pass: ValidPass, "approve");
        Assert.True(r.Status == StatusCodes.Status200OK, $"유효 출입증 → {r.Status} · 본문={r.Body}");
        Assert.Equal(1, r.Inserts);
    }

    /// <summary>
    /// G-A2 보조 — 필터가 <b>그 액션에</b> 붙어 있고, 익명 <c>update-consent-local</c> 에는 새로 붙지 않았다(무접촉).
    /// </summary>
    /// <remarks>⚠️ 리플렉션 검사다 — 동작 증명은 위 네 게이트(파이프라인 경유)가 한다. 이 줄은 원인 위치를 짚어 주는 보조다.</remarks>
    [Fact(DisplayName = "G-A2 보조 🔴 [MainPcOnly] 는 update-consent 에만 — update-consent-local 무접촉")]
    public void GA2_필터위치()
    {
        var consent = typeof(AuthController).GetMethod(nameof(AuthController.UpdateConsent))!;
        Assert.NotNull(consent.GetCustomAttributes(typeof(HitPan.API.Security.MainPcOnlyAttribute), inherit: true).SingleOrDefault());

        var local = typeof(AuthController).GetMethod("UpdateConsentLocal");
        if (local is not null)
            Assert.Empty(local.GetCustomAttributes(typeof(HitPan.API.Security.MainPcOnlyAttribute), inherit: true));
    }

    // ══════════════════════════════════════════════════════════════
    // 준비물
    // ══════════════════════════════════════════════════════════════

    private const string TenantId = "GATE-A2-TENANT";
    private const string ValidPass = "pass-issued-by-server";

    private sealed record Outcome(int Status, string Body, int Inserts);

    private static async Task<Outcome> SendAsync(bool viaTunnel, string? pass, string action)
    {
        var db = new CountingConsentDb();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new DiagnosticListener("HitPan.Tests.GA2"));
        services.AddSingleton<DiagnosticSource>(sp => sp.GetRequiredService<DiagnosticListener>());
        services.AddSingleton<IWebHostEnvironment>(new GateHostEnvironment());
        services.AddSingleton<IHostEnvironment>(sp => sp.GetRequiredService<IWebHostEnvironment>());
        services.AddRouting();
        services.AddAuthentication();
        services.AddAuthorization(o =>
            // Program.cs 의 TenantOnly 와 같은 조건(부모/자식 계정). 이 게이트의 관심사는 MainPcOnly 다.
            o.AddPolicy("TenantOnly", p => p.RequireAssertion(ctx =>
                ctx.User.HasClaim("account_type", "tenant_admin") ||
                ctx.User.HasClaim("account_type", "tenant_user"))));
        services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);

        services.AddScoped<CurrentTenant>();
        services.AddSingleton<IDbConnection>(db);
        services.AddSingleton(Mock.Of<IAuthService>());
        services.AddSingleton(Mock.Of<IHrService>());
        services.AddSingleton(Mock.Of<ITenantDeviceService>());
        services.AddSingleton(Mock.Of<ITermsConsentService>());
        services.AddSingleton(Mock.Of<INotificationService>());

        // 출입증 판정 — 서버가 발급한 것 하나만 참(회사까지 대조). 그 밖은 전부 거짓.
        var proof = new Mock<IMainPcProofService>();
        proof.Setup(p => p.IsPassValid(It.IsAny<string?>(), It.IsAny<string?>()))
             .Returns((string? p, string? t) => p == ValidPass && t == TenantId);
        services.AddSingleton(proof.Object);

        await using var sp = services.BuildServiceProvider();

        var app = new ApplicationBuilder(sp);
        app.UseRouting();
        // ↓ UseAuthentication(JWT) 대역 — 검증을 통과한 부모계정의 User 를 그대로 꽂는다.
        app.Use(async (ctx, next) =>
        {
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("account_type", "tenant_admin"),
                new Claim("tenant_id", TenantId),
                new Claim("user_id", "gate-a2-owner"),
                new Claim("role", "admin"),
            }, authenticationType: "GateJwt"));
            await next(ctx);
        });
        app.UseAuthorization();
        app.UseMiddleware<TenantMiddleware>();
        app.UseEndpoints(e => e.MapControllers());
        var pipeline = app.Build();

        await using var scope = sp.CreateAsyncScope();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        ctx.Request.Method = "POST";
        ctx.Request.Path = ConsentPath;
        ctx.Request.ContentType = "application/json";
        var json = Encoding.UTF8.GetBytes($$"""{"updateVersion":"9.9.9","action":"{{action}}"}""");
        ctx.Request.Body = new MemoryStream(json);
        ctx.Request.ContentLength = json.Length;
        // cloudflared 는 같은 PC 에서 루프백으로 붙는다 — 소켓은 루프백이고 헤더만 다르다.
        ctx.Connection.RemoteIpAddress = IPAddress.Loopback;
        if (viaTunnel) ctx.Request.Headers["CF-Connecting-IP"] = "203.0.113.7";
        if (pass is not null) ctx.Request.Headers[HitPan.API.Security.MainPcOnlyAttribute.PassHeader] = pass;
        var body = new MemoryStream();
        ctx.Response.Body = body;

        await pipeline(ctx);

        return new Outcome(ctx.Response.StatusCode, Encoding.UTF8.GetString(body.ToArray()), db.Inserts);
    }

    private sealed class GateHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = typeof(AuthController).Assembly.GetName().Name!;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = "Development";
    }

    /// <summary>
    /// 🔵 <c>local_update_consents</c> INSERT 수만 센다. 그 밖의 문장이 오면 <b>터진다</b> — 게이트를 같이 고쳐라.
    /// </summary>
    private sealed class CountingConsentDb : DbConnection
    {
        public int Inserts { get; private set; }

        internal int Apply(string sql)
        {
            if (sql.Contains("INSERT INTO local_update_consents", StringComparison.Ordinal))
            {
                Inserts++;
                return 1;
            }

            throw new Xunit.Sdk.XunitException($"G-A2 가 모르는 문장이다 — 게이트를 함께 고쳐라: {sql}");
        }

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
            throw new NotSupportedException("G-A2 는 트랜잭션을 쓰지 않는다.");

        protected override DbCommand CreateDbCommand() => new Cmd(this);

        private sealed class Cmd(CountingConsentDb owner) : DbCommand
        {
            private readonly Params _ps = new();

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
            public override int ExecuteNonQuery() => owner.Apply(CommandText);
            public override object? ExecuteScalar() => throw new NotSupportedException();
            public override void Prepare() { }

            protected override DbParameter CreateDbParameter() => new Param();

            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
                throw new Xunit.Sdk.XunitException($"G-A2 는 읽기를 하지 않는다: {CommandText}");
        }

        private sealed class Param : DbParameter
        {
            public override DbType DbType { get; set; }
            public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
            public override bool IsNullable { get; set; }
            [System.Diagnostics.CodeAnalysis.AllowNull]
            public override string ParameterName { get; set; } = string.Empty;
            public override int Size { get; set; }
            [System.Diagnostics.CodeAnalysis.AllowNull]
            public override string SourceColumn { get; set; } = string.Empty;
            public override bool SourceColumnNullMapping { get; set; }
            public override object? Value { get; set; }
            public override void ResetDbType() { }
        }

        private sealed class Params : DbParameterCollection
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
    }
}
