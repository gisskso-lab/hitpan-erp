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
    // G-Z1 — 20260930작3 갈래 Z · [4] 2차 F-4: update-consent 는 지금 준비된 그 버전에만 답을 받는다.
    //   같은 파이프라인(라우팅 → 권한 → Tenant → [MainPcOnly] → 컨트롤러) · 메인PC(루프백 직접)로 흘린다.
    //   음성 대조군: (가) 시험 안 — 같은 옛 버전이 「준비된 그 버전」이면 200 · 행 1(대역이 무조건 거절하는 게 아님)
    //                (나) 소스 원복 실험 — 대조 두 줄을 빼면 옛 버전이 200 · 행 1 로 FAIL(개발명세서 Z §4 실측).
    // ══════════════════════════════════════════════════════════════

    private const string StaleText = "이미 더 새 버전이 있거나 업데이트가 끝났습니다. 화면을 새로 고쳐 주세요.";

    [Fact(DisplayName = "G-Z1 🔴 F-4 옛 버전 [예]/[나중에] — 더 새 버전이 준비돼 있으면 400 · 행 0 · 고객 문구")]
    public async Task GZ1_더_새_버전이_준비돼_있으면_옛_버전_답은_400_행0()
    {
        foreach (var action in new[] { "approve", "reject" })
        {
            var r = await SendAsync(viaTunnel: false, pass: null, action,
                requestVersion: NextVersion(1), stagedVersion: NextVersion(2));
            Assert.True(r.Status == StatusCodes.Status400BadRequest, $"옛 버전 {action} → {r.Status} (기대 400) · 본문={r.Body}");
            Assert.Equal(0, r.Inserts);
            Assert.Contains(StaleText, System.Text.RegularExpressions.Regex.Unescape(r.Body), StringComparison.Ordinal);
        }
    }

    [Fact(DisplayName = "G-Z1 🔴 F-4 역행 장면 — 설치 버전이 이미 요청 버전 이상(1.3.51 위 1.3.50 [예]) → 400 · 행 0")]
    public async Task GZ1_이미_설치된_버전_이하_답은_400_행0()
    {
        // 적재 행은 1.3.50 그대로 남았고 설치는 1.3.51 로 올라간 장면 = 적재 버전 ≤ 설치 버전 ⇒ UpdateAvailable=false.
        foreach (var action in new[] { "approve", "reject" })
        {
            var r = await SendAsync(viaTunnel: false, pass: null, action,
                requestVersion: NextVersion(0), stagedVersion: NextVersion(0));
            Assert.True(r.Status == StatusCodes.Status400BadRequest, $"설치 버전 {action} → {r.Status} (기대 400) · 본문={r.Body}");
            Assert.Equal(0, r.Inserts);
        }

        var none = await SendAsync(viaTunnel: false, pass: null, "approve", requestVersion: NextVersion(1), noStagedRow: true);
        Assert.Equal(StatusCodes.Status400BadRequest, none.Status);   // 준비된 새 버전 자체가 없다
        Assert.Equal(0, none.Inserts);
    }

    [Fact(DisplayName = "G-Z1 🟢 최신(준비된) 버전 [예]/[나중에] → 200 · 행 1 (무회귀)")]
    public async Task GZ1_준비된_버전_답은_200_행1()
    {
        foreach (var action in new[] { "approve", "reject" })
        {
            var r = await SendAsync(viaTunnel: false, pass: null, action,
                requestVersion: NextVersion(2), stagedVersion: NextVersion(2));
            Assert.True(r.Status == StatusCodes.Status200OK, $"최신 {action} → {r.Status} (기대 200) · 본문={r.Body}");
            Assert.Equal(1, r.Inserts);
        }
    }

    [Fact(DisplayName = "G-Z1 대조군(가) — 같은 옛 버전이라도 그것이 준비된 버전이면 200 · 행 1 (대역이 무조건 거절하지 않는다)")]
    public async Task GZ1_대조군_같은_버전이_준비돼_있으면_200()
    {
        var r = await SendAsync(viaTunnel: false, pass: null, "approve",
            requestVersion: NextVersion(1), stagedVersion: NextVersion(1));
        Assert.Equal(StatusCodes.Status200OK, r.Status);
        Assert.Equal(1, r.Inserts);
    }

    [Fact(DisplayName = "G-Z1 보조 — 메인PC 아님(터널·출입증 없음)은 버전 대조보다 먼저 403 (필터 순서 불변)")]
    public async Task GZ1_메인PC_아님은_여전히_403()
    {
        var r = await SendAsync(viaTunnel: true, pass: null, "approve",
            requestVersion: NextVersion(1), stagedVersion: NextVersion(2));
        Assert.Equal(StatusCodes.Status403Forbidden, r.Status);
        Assert.Contains("main_pc_only", r.Body, StringComparison.Ordinal);
        Assert.Equal(0, r.Inserts);
    }

    // ══════════════════════════════════════════════════════════════
    // 준비물
    // ══════════════════════════════════════════════════════════════

    private const string TenantId = "GATE-A2-TENANT";

    /// <summary>20260930작3 갈래 Z — API 설치 버전(<c>VersionInfo.Current</c>)에서 Build 를 n 올린 버전. n=0 이면 설치 버전 그대로.</summary>
    private static string NextVersion(int n)
    {
        var p = HitPan.API.VersionInfo.Current.Split('.').Select(int.Parse).ToArray();
        return $"{p[0]}.{p[1]}.{p[2] + n}";
    }
    private const string ValidPass = "pass-issued-by-server";

    private sealed record Outcome(int Status, string Body, int Inserts);

    // ⬛ 20260930작3 갈래 Z (F-4) — 요청 버전·준비된 버전을 고를 수 있게 넓혔다(추가 인자 · 기본값 = 종전 장면).
    //   update-consent 가 이제 ComputeUpdateStatusAsync 로 local_update_status 를 읽으므로, 종전 장면(요청 버전 = 준비된 새 버전)을
    //   대역 DB 가 그대로 돌려준다. 종전 요청 버전 "9.9.9" 는 설치 버전보다 높다는 보장이 없어(CI 가 버전을 9.9.9 로 굽는 잡이 있다)
    //   「설치 버전 + 1」로 바꿨다.
    private static async Task<Outcome> SendAsync(bool viaTunnel, string? pass, string action,
        string? requestVersion = null, string? stagedVersion = null, bool noStagedRow = false)
    {
        requestVersion ??= NextVersion(1);
        var db = new CountingConsentDb { StagedVersion = noStagedRow ? null : (stagedVersion ?? requestVersion) };

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
        var json = Encoding.UTF8.GetBytes($$"""{"updateVersion":"{{requestVersion}}","action":"{{action}}"}""");
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

        /// <summary>20260930작3 갈래 Z — <c>local_update_status</c> 최신 1행의 버전(워치독이 적재해 둔 것). null = 행 없음.</summary>
        public string? StagedVersion { get; init; }

        /// <summary>
        /// 20260930작3 갈래 Z — update-consent 가 부르는 ComputeUpdateStatusAsync 의 두 읽기만 답한다.
        /// ① <c>local_update_status</c> → StagedVersion 1행(없으면 0행) ② 미완료 판정 조회(IssueQuerySql) → 0행.
        /// 그 밖의 읽기는 터진다 — 게이트를 같이 고쳐라.
        /// </summary>
        internal DbDataReader Read(string sql)
        {
            if (sql.Contains("FROM local_update_status", StringComparison.Ordinal))
            {
                var t = new DataTable();
                t.Columns.Add("LatestVersion", typeof(string));
                t.Columns.Add("UpdateChannel", typeof(string));
                t.Columns.Add("ConsentMessage", typeof(string));
                if (StagedVersion is not null) t.Rows.Add(StagedVersion, "major", "gate");
                return t.CreateDataReader();
            }

            if (sql == HitPan.API.Services.UpdateIssueJudge.IssueQuerySql)
            {
                var t = new DataTable();
                t.Columns.Add("ConsentId", typeof(long));
                return t.CreateDataReader();
            }

            throw new Xunit.Sdk.XunitException($"G-A2 가 모르는 읽기다 — 게이트를 함께 고쳐라: {sql}");
        }

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

            // ⬛ 20260930작3 갈래 Z — 종전 「읽기를 하지 않는다」는 낡았다(update-consent 가 준비된 버전을 읽는다).
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => owner.Read(CommandText);
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
