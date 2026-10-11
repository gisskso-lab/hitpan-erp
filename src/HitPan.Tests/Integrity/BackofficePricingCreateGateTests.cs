using Dapper;
using HitPan.Backoffice.API.Controllers;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261011작2 §B-4 — 요금제 <b>등록</b>(신설 POST)과 <b>제외</b>(toggle-active 노출)를
/// <b>실제 DB 에서</b> 문다 (G-W2-4 · G-W2-5 · G-W2-8).
///
/// <para><b>글자가 아니라 동작을 잰다</b>(누적 26번 사고). 이 시험은 SQL 문자열을 복사해 비교하지 않는다:
/// <list type="bullet">
/// <item>스키마 = 실물 <see cref="SchemaMigrator"/> 가 <c>installer/backoffice/*.sql</c> 번호순 적용.</item>
/// <item>판정 과녁 = 실물 <see cref="PricingAdminController"/> 를 호출해 <b>그가 자기 SQL 을 DB 에 보내게</b> 한다.
///       판정은 <b>HTTP 상태코드 · DB 행·값</b>으로만 한다(한글 글자 단언 금지 — 10/8 사고).</item>
/// </list></para>
///
/// <para><b>왜 이 게이트인가</b>: 운영 <c>pricing_plans</c> 가 0행인데 컨트롤러엔 생성 입구가 없었다.
/// 그래서 화면에서 첫 요금제를 만들 길이 없었다(선행검증 실측). 신설 입구가 생기면
/// 새로 생기는 위험은 <b>중복 등록이 기존 요금제를 덮는 것</b>이다 — 가격 사고다.
/// G-W2-5 가 그 **대조군**이다: 같은 <c>plan_id</c> 재등록은 409 이고 기존 행은 무변경이어야 한다.
/// <c>ON DUPLICATE KEY UPDATE</c> / <c>REPLACE INTO</c> 로 바꿔 놓으면 그 자리에서 빨간불이 된다
/// (2026-10-11 FAIL 대조 실측 — 개발명세서 §측정값).</para>
///
/// <para>DB 자리: CI <c>db-gate</c> 잡(root)은 격리 DB 를 만들었다 지운다 ·
/// 로컬 <c>hitpan</c> 계정은 <c>CREATE DATABASE</c> 거부라 <c>HITPAN_BO_GATE_DB</c> 로 시험 DB 를 지정해 실측한다.
/// 둘 다 안 되면 <see cref="DbGateEnvironment.SkipOrFailStrict"/> — 선언 없는 조용한 초록은 못 낸다.
/// 🔴 로컬 초록은 증거가 아니다. 판별은 <b>소요시간</b>이고, 증거로 올리는 것은 CI <c>db-gate</c> 잡뿐이다.</para>
/// </summary>
public sealed class BackofficePricingCreateGateTests
{
    // ══════════════════════════════════════════════════════════════
    // DB 자리 (선례 BackofficePromotionPricingDbGateTests.GateDb 그대로)
    // ══════════════════════════════════════════════════════════════

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

    private sealed class GateDb : IAsyncDisposable
    {
        private readonly string _name;
        private readonly bool _createdDb;

        private GateDb(string name, bool createdDb) { _name = name; _createdDb = createdDb; }

        public string ConnString => ServerConnString().Replace("User=", $"Database={_name};User=");

        public static async Task<GateDb?> TryCreateAsync(string gateName, string defaultName)
        {
            var overrideName = Environment.GetEnvironmentVariable("HITPAN_BO_GATE_DB");
            var useOverride = !string.IsNullOrWhiteSpace(overrideName);
            var name = useOverride ? overrideName!.Trim() : defaultName;

            if (useOverride)
            {
                // 지정 DB 는 표를 전부 비우므로 시험 성격의 이름만 허용 — 운영·실데이터 축 오폭 차단(#39).
                var lower = name.ToLowerInvariant();
                if (lower is "hitpan_erp" or "hitpan_erp_t004" or "hitpan_backoffice" or "hitpan_e2e"
                    || (!lower.Contains("test") && !lower.Contains("gate")))
                {
                    throw new Xunit.Sdk.XunitException(
                        $"HITPAN_BO_GATE_DB={name} — 시험 DB 가 아니다(이름에 test/gate 필요 · 운영·실데이터 축 금지).");
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

            var db = new GateDb(name, created);

            // 실물 적재 경로 — 번호순 전부. 출하 DDL 이 단일 진실원이다(#36).
            await new SchemaMigrator(db.ConnString, BoSqlDir(), NullLogger<SchemaMigrator>.Instance).ApplyAsync();

            return db;
        }

        public async Task<MySqlConnection> OpenAsync()
        {
            var c = new MySqlConnection(ConnString);
            await c.OpenAsync();
            return c;
        }

        public IConfiguration Config() => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BackofficeDb"] = ConnString,
            })
            .Build();

        public async ValueTask DisposeAsync()
        {
            if (!_createdDb) return;
            try
            {
                await using var admin = new MySqlConnection(ServerConnString());
                await admin.OpenAsync();
                await admin.ExecuteAsync($"DROP DATABASE IF EXISTS `{_name}`;");
            }
            catch (MySqlException ex)
            {
                // #15 — 뒷정리 실패를 삼키지 않는다(다음 실행의 DROP IF EXISTS 가 지운다).
                Console.Error.WriteLine($"[작2게이트] 격리 DB 뒷정리 실패: {ex.Message}");
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    // 응답 읽기 — 익명 객체를 리플렉션으로 읽는다(한글 글자 단언 금지)
    // ══════════════════════════════════════════════════════════════

    private static int StatusOf(IActionResult res) => res switch
    {
        OkObjectResult => 200,
        CreatedResult => 201,
        ConflictObjectResult => 409,
        NotFoundObjectResult => 404,
        BadRequestObjectResult => 400,
        ObjectResult o => o.StatusCode ?? 0,
        _ => 0,
    };

    private static object? ValueOf(IActionResult res) => (res as ObjectResult)?.Value;

    private static T? Field<T>(object? value, string name)
    {
        if (value is null) return default;
        var p = value.GetType().GetProperty(name);
        if (p is null) return default;
        var v = p.GetValue(value);
        return v is null ? default : (T?)v;
    }

    private static string Describe(IActionResult res)
        => $"status={StatusOf(res)} body={{ {string.Join(", ",
            (ValueOf(res)?.GetType().GetProperties() ?? Array.Empty<System.Reflection.PropertyInfo>())
                .Select(p => $"{p.Name}={p.GetValue(ValueOf(res))}"))} }}";

    private static ControllerContext OwnerContext()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        return new ControllerContext { HttpContext = ctx };
    }

    private static PricingAdminController Controller(GateDb env) =>
        new(env.Config(), NullLogger<PricingAdminController>.Instance) { ControllerContext = OwnerContext() };

    /// <summary>등록 요청 한 벌 — 기기 두 칸은 **안 채운다**(미설정 = NULL 이 정상 경로다).</summary>
    private static PricingAdminController.CreatePlanRequest Req(
        string planId, string planName, decimal monthly, decimal yearly) =>
        new()
        {
            PlanId = planId,
            PlanName = planName,
            MonthlyPrice = monthly,
            YearlyPrice = yearly,
            MaxUsers = 5,
            AiTokenMonthly = 100000,
            IsVisible = true,
            DisplayOrder = 1,
            Reason = "게이트 등록",
            ChangedBy = "platform_admin",
            ChangedByName = "본사 마스터",
        };

    // ══════════════════════════════════════════════════════════════
    // G-W2-4 — 등록이 실제로 DB 에 들어가고 이력 1줄이 남는다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W2-4 🔴 요금제 등록(POST)이 실제 DB 에 행을 만들고 조회되며 pricing_history 에 create 1줄")]
    public async Task G_W2_4_요금제_등록이_DB에_들어간다()
    {
        const string gate = "G-W2-4";
        await using var env = await GateDb.TryCreateAsync(gate, "hitpan_gate_w2_create");
        if (env is null) return;

        var c = Controller(env);

        // ── 0) 음성 축 — 등록 전엔 그 요금제가 **없다**(아래 양성이 「원래 있던 것」이 아님을 보인다).
        var before = await c.GetPlan("gatebasic", default);
        Assert.True(StatusOf(before) == 404,
            $"{gate} 등록 전에 이미 요금제가 있다 — 시험 DB 가 깨끗하지 않다.\n  {Describe(before)}");

        // ── 1) 등록 — 실물 컨트롤러가 자기 INSERT 를 DB 에 보낸다.
        var created = await c.CreatePlan(Req("gatebasic", "gate basic", 29000m, 290000m), default);
        Assert.True(StatusOf(created) == 201,
            $"{gate} 등록이 201 이 아니다 — 생성 입구가 없거나 INSERT 가 표/칸과 안 맞는다.\n  {Describe(created)}");
        Assert.True(Field<bool>(ValueOf(created), "success"), $"{gate} 등록 success=false. {Describe(created)}");

        // ── 2) 조회되는가 — PUT 이 쓰는 그 SELECT 로 되읽는다.
        var got = await c.GetPlan("gatebasic", default);
        Assert.True(StatusOf(got) == 200, $"{gate} 등록한 요금제가 조회되지 않는다. {Describe(got)}");
        var plan = Field<PricingAdminController.PlanRow>(ValueOf(got), "data");
        Assert.NotNull(plan);
        Assert.Equal(29000m, plan!.MonthlyPrice);
        Assert.Equal(290000m, plan.YearlyPrice);
        Assert.Equal(5, plan.MaxUsers);
        // price_display 를 안 보냈으면 표시 모드 기본값 'number' 여야 한다(작16 D-4).
        Assert.Equal("number", plan.PriceDisplay);
        // 🔴 기기 두 칸은 NULL 이어야 한다. 0 이면 그 요금제의 기기 등록이 전부 막힌다(작16 결재 D-2).
        Assert.Null(plan.MaxPcDevices);
        Assert.Null(plan.MaxMobileDevices);
        // 새 행은 **포함**(판매중)으로 선다 — 출하 DDL 기본값 1.
        Assert.True(plan.IsActive == 1, $"{gate} 새 요금제가 is_active={plan.IsActive} 다(기대 1 = 포함).");

        await using (var db = await env.OpenAsync())
        {
            // 🔴 max_devices 는 화면이 안 보낸다. 그때 0 이 조용히 들어가면 안 된다(DDL DEFAULT 3).
            var maxDevices = await db.ExecuteScalarAsync<int>(
                "SELECT max_devices FROM pricing_plans WHERE plan_id = 'gatebasic'");
            Assert.True(maxDevices > 0,
                $"{gate} max_devices 가 {maxDevices} 다 — 화면이 안 보낸 칸에 0 이 들어갔다(기대: DDL 기본값).");

            // ── 3) 이력 — 헌법 #3 INSERT ONLY. create 가 **정확히 1줄**이고 before_json 은 없다.
            var createRows = await db.ExecuteScalarAsync<long>(@"
                SELECT COUNT(*) FROM pricing_history
                WHERE entity_type = 'plan' AND entity_id = 'gatebasic' AND action = 'create'");
            Assert.True(createRows == 1,
                $"{gate} pricing_history 의 create 줄이 {createRows} 개다(기대 1) — 감사기록이 안 남거나 중복이다.");

            var hasBefore = await db.ExecuteScalarAsync<long>(@"
                SELECT COUNT(*) FROM pricing_history
                WHERE entity_type = 'plan' AND entity_id = 'gatebasic' AND action = 'create'
                  AND before_json IS NOT NULL");
            Assert.True(hasBefore == 0,
                $"{gate} create 이력에 before_json 이 있다 — 생성 전 상태란 없다(그 값은 무엇을 베낀 것이다).");

            var reason = await db.ExecuteScalarAsync<string>(@"
                SELECT reason FROM pricing_history
                WHERE entity_type = 'plan' AND entity_id = 'gatebasic' AND action = 'create'");
            Assert.False(string.IsNullOrWhiteSpace(reason),
                $"{gate} create 이력의 reason 이 비어 있다 — 사유가 전달되지 않았다.");
        }

        // ── 4) 양성 축 — 목록에도 뜬다(화면이 보는 그 입구).
        var listed = await c.ListPlans(default);
        Assert.True(StatusOf(listed) == 200, $"{gate} 목록이 200 이 아니다. {Describe(listed)}");
        var rows = Field<System.Collections.IEnumerable>(ValueOf(listed), "data");
        Assert.NotNull(rows);
        Assert.Contains("gatebasic", rows!.Cast<object>().Select(r => Field<string>(r, "PlanId")));
    }

    // ══════════════════════════════════════════════════════════════
    // G-W2-5 — 🔴 대조군. 중복 등록은 409 이고 기존 행은 무변경
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W2-5 🔴 대조군 — 같은 plan_id 재등록은 409 이고 기존 행이 한 글자도 안 바뀐다(가격 덮임 차단)")]
    public async Task G_W2_5_중복등록은_409이고_기존행_무변경()
    {
        const string gate = "G-W2-5";
        await using var env = await GateDb.TryCreateAsync(gate, "hitpan_gate_w2_dup");
        if (env is null) return;

        var c = Controller(env);

        var first = await c.CreatePlan(Req("gatedup", "원래 요금제", 29000m, 290000m), default);
        Assert.True(StatusOf(first) == 201, $"{gate} 1차 등록이 201 이 아니다. {Describe(first)}");

        // 기존 행 스냅샷 — updated_at 까지 잰다. ON DUPLICATE KEY UPDATE / REPLACE 면 여기가 바뀐다.
        string beforeName;
        decimal beforePrice;
        int beforeVisible;
        DateTime beforeUpdated;
        await using (var db = await env.OpenAsync())
        {
            var row = await db.QueryFirstAsync(
                "SELECT plan_name, monthly_price, is_visible, updated_at FROM pricing_plans WHERE plan_id = 'gatedup'");
            beforeName = (string)row.plan_name;
            beforePrice = (decimal)row.monthly_price;
            beforeVisible = Convert.ToInt32(row.is_visible);
            beforeUpdated = (DateTime)row.updated_at;
        }

        // 🔴 두 번째 등록은 **전혀 다른 내용**으로 보낸다. 덮이면 그 흔적이 바로 보이게.
        var second = await c.CreatePlan(new PricingAdminController.CreatePlanRequest
        {
            PlanId = "gatedup",
            PlanName = "덮어쓴 요금제",
            MonthlyPrice = 1m,
            YearlyPrice = 2m,
            PriceDisplay = "contact",
            MaxUsers = 99,
            MaxDevices = 99,
            MaxPcDevices = 0,          // 🔴 들어가면 기기 상한 0 사고가 된다
            MaxMobileDevices = 0,
            AiTokenMonthly = 0,
            IsVisible = false,
            DisplayOrder = 99,
            Reason = "중복 시도",
            ChangedBy = "platform_admin",
        }, default);

        Assert.True(StatusOf(second) == 409,
            $"{gate} 같은 plan_id 재등록이 409 가 아니다 — 중복이 거절되지 않았다.\n"
          + "  🔴 ON DUPLICATE KEY UPDATE / REPLACE INTO 로 바꾸면 운영 요금제의 가격이 조용히 덮인다.\n"
          + $"  {Describe(second)}");

        await using (var db = await env.OpenAsync())
        {
            var row = await db.QueryFirstAsync(
                "SELECT plan_name, monthly_price, yearly_price, price_display, max_users, "
              + "max_pc_devices, max_mobile_devices, is_visible, display_order, updated_at "
              + "FROM pricing_plans WHERE plan_id = 'gatedup'");

            Assert.Equal(beforeName, (string)row.plan_name);
            Assert.Equal(beforePrice, (decimal)row.monthly_price);
            Assert.Equal(290000m, (decimal)row.yearly_price);
            Assert.Equal("number", (string)row.price_display);
            Assert.Equal(5, Convert.ToInt32(row.max_users));
            Assert.Equal(1, Convert.ToInt32(row.display_order));
            Assert.Equal(beforeVisible, Convert.ToInt32(row.is_visible));
            // 🔴 0 이 들어왔는지 — 들어왔으면 그 요금제의 기기 등록이 전부 막힌다.
            Assert.Null((int?)row.max_pc_devices);
            Assert.Null((int?)row.max_mobile_devices);
            // updated_at 은 손도 안 닿았음의 증거다(UPDATE 가 돌면 ON UPDATE 로 바뀐다).
            Assert.Equal(beforeUpdated, (DateTime)row.updated_at);

            // 거절된 시도는 이력도 남기지 않는다 — create 는 여전히 1줄.
            var createRows = await db.ExecuteScalarAsync<long>(@"
                SELECT COUNT(*) FROM pricing_history
                WHERE entity_type = 'plan' AND entity_id = 'gatedup' AND action = 'create'");
            Assert.True(createRows == 1,
                $"{gate} create 이력이 {createRows} 줄이다(기대 1) — 거절된 등록이 이력을 남겼다.");

            // 행이 하나뿐이다(두 번째가 다른 모양으로 끼지 않았다).
            var planRows = await db.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM pricing_plans WHERE plan_id = 'gatedup'");
            Assert.True(planRows == 1, $"{gate} pricing_plans 에 gatedup 행이 {planRows} 개다(기대 1).");
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-W2-8 — 제외/포함 왕복(번호 = [4] V-7 봉합 · G-W2-6 은 쪽지 폐기 몫이다). 🔴 행을 지우지 않는다
    //   (§B-3 은 화면 버튼이라 글자로만 두면 아무도 안 잰다. 그 버튼이 부르는 입구를 여기서 문다.)
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W2-8 🔴 제외→포함 왕복이 돌고, 제외해도 행이 지워지지 않는다(이력 deactivate/activate)")]
    public async Task G_W2_6_제외_포함_왕복이_행을_안_지운다()
    {
        const string gate = "G-W2-8";
        await using var env = await GateDb.TryCreateAsync(gate, "hitpan_gate_w2_toggle");
        if (env is null) return;

        var c = Controller(env);
        Assert.True(StatusOf(await c.CreatePlan(Req("gatetog", "토글 요금제", 59000m, 590000m), default)) == 201,
            $"{gate} 사전 등록이 201 이 아니다 — 이 게이트의 전제가 안 섰다.");

        // ── 1) 제외
        var off = await c.ToggleActive("gatetog", new PricingAdminController.ToggleRequest
        { IsActive = false, Reason = "베타 테스트 제외", ChangedBy = "platform_admin" }, default);
        Assert.True(StatusOf(off) == 200, $"{gate} 제외가 200 이 아니다. {Describe(off)}");

        await using (var db = await env.OpenAsync())
        {
            // 🔴 행이 남아 있나 — 제외는 삭제가 아니다.
            var exists = await db.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM pricing_plans WHERE plan_id = 'gatetog'");
            Assert.True(exists == 1, $"{gate} 제외 뒤 행이 {exists} 개다(기대 1) — 🔴 지워졌다.");

            var active = await db.ExecuteScalarAsync<int>(
                "SELECT CAST(is_active AS SIGNED) FROM pricing_plans WHERE plan_id = 'gatetog'");
            Assert.True(active == 0, $"{gate} 제외했는데 is_active={active} 다(기대 0).");

            // 가격·이름은 제외로 바뀌지 않는다(제외는 과금·노출에서 빼는 것뿐이다).
            var price = await db.ExecuteScalarAsync<decimal>(
                "SELECT monthly_price FROM pricing_plans WHERE plan_id = 'gatetog'");
            Assert.Equal(59000m, price);

            var deact = await db.ExecuteScalarAsync<long>(@"
                SELECT COUNT(*) FROM pricing_history
                WHERE entity_type = 'plan' AND entity_id = 'gatetog' AND action = 'deactivate'");
            Assert.True(deact == 1, $"{gate} deactivate 이력이 {deact} 줄이다(기대 1).");
        }

        // ── 2) 되돌리기(포함) — 같은 입구로 돌아온다.
        var on = await c.ToggleActive("gatetog", new PricingAdminController.ToggleRequest
        { IsActive = true, Reason = "다시 판매", ChangedBy = "platform_admin" }, default);
        Assert.True(StatusOf(on) == 200, $"{gate} 포함이 200 이 아니다. {Describe(on)}");

        await using (var db2 = await env.OpenAsync())
        {
            var active = await db2.ExecuteScalarAsync<int>(
                "SELECT CAST(is_active AS SIGNED) FROM pricing_plans WHERE plan_id = 'gatetog'");
            Assert.True(active == 1, $"{gate} 포함했는데 is_active={active} 다(기대 1) — 되돌리기가 안 된다.");

            var act = await db2.ExecuteScalarAsync<long>(@"
                SELECT COUNT(*) FROM pricing_history
                WHERE entity_type = 'plan' AND entity_id = 'gatetog' AND action = 'activate'");
            Assert.True(act == 1, $"{gate} activate 이력이 {act} 줄이다(기대 1).");
        }

        // ── 3) 음성 축 — 없는 요금제를 제외하려 하면 404 다(아무 planId 나 받아 조용히 200 을 내지 않는다).
        var ghost = await c.ToggleActive("gate-no-such-plan", new PricingAdminController.ToggleRequest
        { IsActive = false, ChangedBy = "platform_admin" }, default);
        Assert.True(StatusOf(ghost) == 404,
            $"{gate} 없는 요금제 제외가 404 가 아니다 — 입구가 아무것도 확인하지 않는다. {Describe(ghost)}");
    }

    // ══════════════════════════════════════════════════════════════
    // G-W2-12 — [4] V-5 봉합 · 이력이 안 남으면 **요금제도 안 남는다**(한 거래)
    //
    // 🔴 이 게이트가 막는 사고: 종전엔 `pricing_plans` INSERT 와 `pricing_history` INSERT 가
    //   별건이었다. 이력 쪽이 실패하면 **요금제는 이미 있는데** 응답은 500 「등록 실패」였고,
    //   사람이 다시 누르면 409 「이미 있습니다」 — 화면이 뭐가 참인지 말해 주지 못했다.
    //   그리고 그 요금제는 **이력 0줄**로 남는다(#3 INSERT ONLY 축이 비는 자리).
    //
    // 재현 방법: 시험 DB 에서 `pricing_history.reason` 을 좁혀 **이력 INSERT 만** 실패하게 만든다.
    //   컨트롤러의 길이 검증(500자)은 통과하되 DB 가 거부하는 값을 보낸다 ⇒ 거래가 없으면
    //   요금제 행이 남는다. 있으면 **한 줄도 안 남는다.**
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W2-12 🔴 이력 INSERT 가 실패하면 요금제 행도 안 남는다(거래) + 재시도가 409 가 아니다")]
    public async Task G_W2_12_이력실패면_요금제도_안남는다()
    {
        const string gate = "G-W2-12";
        await using var env = await GateDb.TryCreateAsync(gate, "hitpan_gate_w2_tx");
        if (env is null) return;

        var c = Controller(env);
        await using var db = await env.OpenAsync();

        // 🔴 음성 대조군 먼저 — 좁히기 **전**에는 같은 요청이 201 이어야 한다.
        //   (이게 없으면 「원래 안 되던 것」을 거래 덕분이라고 읽을 수 있다)
        Assert.True(StatusOf(await c.CreatePlan(Req("txok", "tx ok", 1000m, 10000m), default)) == 201,
            $"{gate} 좁히기 전 등록이 201 이 아니다 — 시험 전제가 틀렸다.");

        // 이력 칸을 좁힌다 — 이제 이력 INSERT 만 1406 으로 실패한다.
        // 🔴 `varchar(10)` 이다. 위 음성 대조군이 이미 넣은 행의 사유(6자)가 들어갈 수 있어야
        //   `MODIFY` 자체가 선다 — 더 좁히면 기존 행 때문에 ALTER 가 먼저 실패한다(실측).
        await db.ExecuteAsync("ALTER TABLE pricing_history MODIFY COLUMN reason varchar(10) NULL");

        var req = Req("txfail", "tx fail", 2000m, 20000m);
        req.Reason = "열 글자를 훌쩍 넘기는 긴 사유입니다";   // 컨트롤러 500자 검증은 통과 · DB 는 거부
        var res = await c.CreatePlan(req, default);

        Assert.True(StatusOf(res) == 500,
            $"{gate} 이력 실패가 500 으로 안 왔다 — 시험이 의도한 경로를 안 지났다. {Describe(res)}");

        // 🔴 핵심 — 요금제 행이 **없어야** 한다
        var planRows = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM pricing_plans WHERE plan_id = 'txfail'");
        Assert.True(planRows == 0,
            $"{gate} 이력이 실패했는데 요금제 행이 {planRows}건 남았다 — 거래가 안 묶였다. "
            + "이 상태의 요금제는 이력 0줄이고, 다시 누르면 409 라 사람이 손쓸 수 없다.");

        var histRows = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM pricing_history WHERE entity_id = 'txfail'");
        Assert.True(histRows == 0, $"{gate} 이력이 {histRows}건 남았다(기대 0건).");

        // 🔴 그래서 **다시 누르면 409 가 아니다** — 사람이 고쳐서 재등록할 수 있다
        await db.ExecuteAsync("ALTER TABLE pricing_history MODIFY COLUMN reason varchar(500) NULL");
        var retry = await c.CreatePlan(Req("txfail", "tx fail 재시도", 2000m, 20000m), default);
        Assert.True(StatusOf(retry) == 201,
            $"{gate} 재시도가 201 이 아니다 — 반쪽 상태가 남아 사람이 막혔다. {Describe(retry)}");
    }

    // ══════════════════════════════════════════════════════════════
    // G-W2-13 — 🔴 20261012작3 오더 1 ⓒ · `plan_id` 는 **URL 열쇠**다
    //
    // 사고: 화면은 「영문·숫자 … 이내」라고 약속하는데 서버는 **길이만** 쟀다.
    //   `plan_id` 는 `plans/{planId}`·`…/toggle-active`·`…/history` **네 입구의 URL 조각**이다
    //   ⇒ 공백·`/` 가 든 코드로 등록하면 **등록은 되는데 수정·제외·이력이 전부 404** 인
    //   요금제가 생긴다. 「제외」는 행을 남기고 삭제 입구는 없으니 **손으로 치울 수도 없다.**
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W2-13 🔴 화면이 약속한 plan_id 규칙을 서버가 지킨다 — URL 을 깨는 코드는 400 (+양성 대조)")]
    public async Task G_W2_13_plan_id_규칙이_화면안내와_같다()
    {
        const string gate = "G-W2-13";
        await using var env = await GateDb.TryCreateAsync(gate, "hitpan_gate_w2_planid");
        if (env is null) return;

        var c = Controller(env);

        // ① 🔴 URL 을 깨는 모양들 — 하나라도 통과하면 「되돌릴 수 없는 요금제」가 생긴다
        var bad = new[]
        {
            "pro/basic",     // 경로가 쪼개져 toggle-active 가 404
            "pro basic",     // 공백
            "프로",           // 한글 — 화면이 「영문·숫자」라고 약속했다
            "pro?x=1",       // 질의문자
            "pro#1",         // 조각
            "pro.basic",     // 확장자로 읽히는 모양
        };
        foreach (var id in bad)
        {
            var res = await c.CreatePlan(Req(id, "규칙 위반", 1000m, 10000m), default);
            Assert.True(StatusOf(res) == 400,
                $"{gate} '{id}' 가 400 이 아니다 — 등록되면 수정·제외·이력이 전부 404 인 요금제가 남는다. {Describe(res)}");
        }

        // 🔴 하나도 안 들어갔나 — 400 을 돌려주고도 행을 만들었으면 더 나쁘다
        await using (var db = await env.OpenAsync())
        {
            Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pricing_plans"));
        }

        // ② 양성 대조군 — 화면 안내가 허용한다고 말한 모양은 **실제로 된다**
        //   (음성만 있으면 「전부 막는 코드」와 구별이 안 된다)
        foreach (var id in new[] { "basic", "PRO2", "pro-plus", "pro_plus", "a", "12345678901234567890" })
        {
            var ok = await c.CreatePlan(Req(id, $"{id} 요금제", 1000m, 10000m), default);
            Assert.True(StatusOf(ok) == 201,
                $"{gate} 화면이 허용한다고 말한 '{id}' 가 201 이 아니다 — 안내가 거짓말이 된다. {Describe(ok)}");
        }

        // ③ 🔴 그리고 그 코드로 **되돌릴 수 있나** — 이게 이 게이트의 과녁이다.
        //   등록만 되고 제외가 404 면 오더 1(「요금제 제외 버튼」)이 그 요금제에서 죽는다.
        var toggled = await c.ToggleActive("pro-plus",
            new PricingAdminController.ToggleRequest
            { IsActive = false, Reason = "규칙 확인", ChangedBy = "platform_admin" }, default);
        Assert.True(StatusOf(toggled) == 200,
            $"{gate} 등록된 코드인데 제외가 200 이 아니다. {Describe(toggled)}");
    }
}
