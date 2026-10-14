using System.Security.Cryptography;
using System.Text;
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
/// 🔴 20261008작16 §D — 프로모션·요금제 500 봉합을 <b>실제 DB 에서</b> 문다
/// (G-PROMO-1 · G-PRICE-1 · G-MIG-1 · G-MIG-2).
///
/// <para><b>글자가 아니라 동작을 잰다.</b> 이 시험은 SQL 문자열을 복사해 두고 비교하지 않는다
/// (작14 F-2 재발 방지). 무는 것은 <b>실물</b>뿐이다:
/// <list type="bullet">
/// <item>스키마 = 실물 <see cref="SchemaMigrator"/> 가 <c>installer/backoffice/*.sql</c> 번호순 적용
///       (백오피스 DDL 진실원 · 신설 <c>43</c>·<c>44</c> 가 그 흐름에 실제로 끼는지 포함).</item>
/// <item>판정 과녁 = 실물 <see cref="PromotionController"/>·<see cref="PricingAdminController"/>·
///       <see cref="DeviceRegistrationController"/> 를 호출해 <b>그들이 자기 SQL 을 DB 에 보내게</b> 한다.
///       표 이름이 <c>promotions</c> 로 남아 있으면 Unknown column 으로 500 이 되어 FAIL 한다.</item>
/// </list></para>
///
/// <para>🔴 <b>한글 문자열로 단언하지 않는다</b>(10/8 사고 — JSON 직렬화가 한글을 <c>\uXXXX</c> 로 바꿔
/// 음성 단언이 늘 통과했다). 판정은 <b>HTTP 상태코드 · 불리언 · DB 행·값</b>으로만 한다.
/// 양성 축을 함께 둬서 「아무것도 안 하고 초록」을 막는다.</para>
///
/// <para>DB 자리: CI <c>db-gate</c> 잡(root)은 격리 DB 를 만들었다 지운다 ·
/// 로컬 <c>hitpan</c> 계정은 <c>CREATE DATABASE</c> 거부라 <c>HITPAN_BO_GATE_DB</c> 로 시험 DB 를 지정해 실측한다.
/// 둘 다 안 되면 <see cref="DbGateEnvironment.SkipOrFailStrict"/> — 선언 없는 조용한 초록은 못 낸다.
/// 🔴 로컬 초록은 증거가 아니다. 판별은 <b>소요시간</b>이고, 증거로 올리는 것은 CI <c>db-gate</c> 잡뿐이다.</para>
/// </summary>
public sealed class BackofficePromotionPricingDbGateTests
{
    private const string Pepper = "work16-gate-license-pepper";

    // ══════════════════════════════════════════════════════════════
    // DB 자리 (선례 BackofficeRoleGateHarness)
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

    /// <summary>시험 DB 한 벌. <paramref name="applyAll"/> = false 면 번호 31 앞까지만 적재한다(G-MIG-2 가 쓴다).</summary>
    private sealed class GateDb : IAsyncDisposable
    {
        private readonly string _name;
        private readonly bool _createdDb;

        private GateDb(string name, bool createdDb) { _name = name; _createdDb = createdDb; }

        public string ConnString => ServerConnString().Replace("User=", $"Database={_name};User=");

        public static async Task<GateDb?> TryCreateAsync(string gateName, string defaultName,
                                                         Func<string, bool>? fileFilter = null)
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

            if (fileFilter is null)
            {
                // 실물 적재 경로 — 번호순 전부. 43·44 가 그 흐름에 실제로 끼는지도 여기서 드러난다.
                await new SchemaMigrator(db.ConnString, BoSqlDir(), NullLogger<SchemaMigrator>.Instance).ApplyAsync();
            }
            else
            {
                await db.ApplyFilesAsync(fileFilter);
            }

            return db;
        }

        /// <summary>고른 파일만 번호순으로, 파일 내용 그대로 실행한다(적재기가 하는 그 방식 — 한 파일 통째 실행).</summary>
        public async Task ApplyFilesAsync(Func<string, bool> filter)
        {
            await using var db = new MySqlConnection(ConnString);
            await db.OpenAsync();
            foreach (var path in Directory.GetFiles(BoSqlDir(), "*.sql")
                         .OrderBy(Path.GetFileName, StringComparer.Ordinal))
            {
                var file = Path.GetFileName(path);
                if (!filter(file)) continue;
                await using var cmd = new MySqlCommand(await File.ReadAllTextAsync(path), db) { CommandTimeout = 120 };
                await cmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>파일 한 개를 그 내용 그대로 실행한다(멱등 실측 · 고아 1452 실측이 쓴다).</summary>
        public async Task ApplyOneAsync(string fileName)
        {
            await using var db = new MySqlConnection(ConnString);
            await db.OpenAsync();
            var path = Path.Combine(BoSqlDir(), fileName);
            Assert.True(File.Exists(path), $"적재할 파일이 없다: {path}");
            await using var cmd = new MySqlCommand(await File.ReadAllTextAsync(path), db) { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync();
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
                ["License:Pepper"] = Pepper,
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
                Console.Error.WriteLine($"[작16게이트] 격리 DB 뒷정리 실패: {ex.Message}");
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    // 응답 읽기 — 익명 객체를 **리플렉션**으로 읽는다(한글 글자 단언 금지)
    // ══════════════════════════════════════════════════════════════

    private static int StatusOf(IActionResult res) => res switch
    {
        OkObjectResult => 200,
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

    /// <summary>실패했을 때 무엇이 났는지 바로 보이게 — 응답의 message 는 로그로만 쓰고 단언에 쓰지 않는다.</summary>
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

    private static string Hmac(string data, string key)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
    }

    // ══════════════════════════════════════════════════════════════
    // G-PROMO-1
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-PROMO-1 🔴 프로모션 목록·등록·redeem 이 실제 DB 에서 돈다 + 사용이력 1건 (표 이름 봉합)")]
    public async Task G_PROMO_1_프로모션_실제SQL_3개가_돈다()
    {
        const string gate = "G-PROMO-1";
        await using var env = await GateDb.TryCreateAsync(gate, "hitpan_gate_w16_promo");
        if (env is null) return;

        var cfg = env.Config();
        var audit = new BoAuditService(cfg, NullLogger<BoAuditService>.Instance);
        var c = new PromotionController(cfg, NullLogger<PromotionController>.Instance, audit)
        { ControllerContext = OwnerContext() };

        // ── 1) 등록 — INSERT INTO promotions_legacy (…) + LAST_INSERT_ID()
        var created = await c.Create(new PromotionController.CreateRequest(
            PromoCode: "W16GATE",
            Title: "gate promo",
            DiscountType: "percent",
            DiscountValue: 10m,
            StartsAt: DateTime.UtcNow.AddDays(-1),
            EndsAt: DateTime.UtcNow.AddDays(30),
            MaxUses: 5,
            TargetPlan: null), default);

        Assert.True(StatusOf(created) == 200,
            $"{gate} 등록이 200 이 아니다 — 컨트롤러 SQL 과 표가 안 맞는다(2026-10-08 운영 500 과 같은 모양).\n"
          + $"  {Describe(created)}\n"
          + "  표 이름이 promotions(admin 모양)로 남아 있으면 promo_code/title/use_count 가 없어 Unknown column 이다.");
        Assert.True(Field<bool>(ValueOf(created), "success"));
        var promotionId = Field<long>(ValueOf(created), "promotionId");
        Assert.True(promotionId > 0,
            $"{gate} LAST_INSERT_ID() 가 0 이다 — bigint AUTO_INCREMENT PK 표에 안 들어갔다. {Describe(created)}");

        // ── 2) 목록 — SELECT … FROM promotions_legacy (양성 축: 방금 넣은 줄이 그대로 읽힌다)
        var listed = await c.List(status: null, default);
        Assert.True(StatusOf(listed) == 200, $"{gate} 목록이 200 이 아니다. {Describe(listed)}");
        var items = Field<System.Collections.IEnumerable>(ValueOf(listed), "items");
        Assert.NotNull(items);
        var rows = items!.Cast<object>().ToList();
        Assert.True(rows.Count == 1, $"{gate} 목록이 1건이 아니다({rows.Count}건) — 등록과 조회가 같은 표를 안 본다.");
        Assert.Equal("W16GATE", Field<string>(rows[0], "PromoCode"));
        Assert.Equal(0, Field<int>(rows[0], "UseCount"));

        // ── 3) redeem — promotion_usages INSERT + use_count +1 (외래키가 legacy 를 가리키므로 여기서 드러난다)
        var redeemed = await c.Redeem(new PromotionController.RedeemRequest(
            PromoCode: "W16GATE", SignupToken: "w16-token", PlanType: null, BaseAmount: 50000m), default);
        Assert.True(StatusOf(redeemed) == 200, $"{gate} redeem 이 200 이 아니다. {Describe(redeemed)}");
        Assert.True(Field<bool>(ValueOf(redeemed), "valid"),
            $"{gate} redeem 이 valid=false 다 — 조회 표와 등록 표가 어긋났다. {Describe(redeemed)}");

        await using var db = await env.OpenAsync();
        var usages = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM promotion_usages WHERE promotion_id = @Id", new { Id = promotionId });
        Assert.True(usages == 1, $"{gate} promotion_usages 가 {usages} 건이다(기대 1건) — 사용 이력이 안 남았다.");

        var useCount = await db.ExecuteScalarAsync<int>(
            "SELECT use_count FROM promotions_legacy WHERE promotion_id = @Id", new { Id = promotionId });
        Assert.True(useCount == 1, $"{gate} use_count 가 {useCount} 다(기대 1) — 차감 UPDATE 가 다른 표로 갔다.");

        // ── 음성 축: 중복 코드는 거절된다(UNIQUE 가 legacy 표에 실제로 섰다는 증거 · 상태코드로만 판정)
        var dup = await c.Create(new PromotionController.CreateRequest(
            "W16GATE", "dup", "percent", 10m, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30), 5, null), default);
        Assert.True(StatusOf(dup) == 400,
            $"{gate} 같은 promo_code 를 두 번 넣었는데 400 이 아니다 — 중복 차단이 안 선다. {Describe(dup)}");
    }

    // ══════════════════════════════════════════════════════════════
    // G-PRICE-1
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-PRICE-1 🔴 요금제 SELECT 가 돌고 두 칸이 NULL 로 매핑되며 기기 폴백이 5/3 이다(0 사고 차단)")]
    public async Task G_PRICE_1_요금제_세칸과_폴백_5_3()
    {
        const string gate = "G-PRICE-1";
        await using var env = await GateDb.TryCreateAsync(gate, "hitpan_gate_w16_price");
        if (env is null) return;

        var tenantId = Guid.NewGuid().ToString();
        const string licenseKey = "W16-GATE-LICENSE-KEY";

        await using (var seed = await env.OpenAsync())
        {
            // 🔴 두 기기 칸을 **안 적는다** — 운영의 기존 행과 같은 상태(미설정 = NULL)를 만든다.
            //   price_display 도 안 적는다 ⇒ DEFAULT 'number' 가 들어가야 한다(D-4).
            await seed.ExecuteAsync(@"
                INSERT INTO pricing_plans (plan_id, plan_name, monthly_price, yearly_price,
                                           max_users, max_devices, ai_token_monthly, is_active, is_visible, display_order)
                VALUES ('basic', 'gate basic', 29000, 290000, 5, 5, 100000, 1, 1, 1)");

            await seed.ExecuteAsync(@"
                INSERT INTO tenants (tenant_id, tenant_code, company_name, status, license_key_hash, subscription_tier)
                VALUES (@Tid, 'W16GT', 'w16 gate co', 'active', @Hash, 'basic')",
                new { Tid = tenantId, Hash = Hmac(licenseKey, Pepper) });
        }

        // ── 1) 요금제 목록 — PlanRow 가 두 칸을 int? 로 받는다.
        //    🔴 int 였다면 500 이 아니라 **조용한 0** 이 된다(Dapper 가 NULL 을 그냥 안 담는다 · 실측 2026-10-08).
        //    아래 Assert.Null 이 그 조용한 0 을 잡는 자리다 — 500 축만 두면 못 잡는다.
        var pricing = new PricingAdminController(env.Config(), NullLogger<PricingAdminController>.Instance)
        { ControllerContext = OwnerContext() };
        var listed = await pricing.ListPlans(default);
        Assert.True(StatusOf(listed) == 200,
            $"{gate} 요금제 목록이 200 이 아니다 — 세 칸이 표에 없거나 NULL 을 int 에 담으려 했다.\n  {Describe(listed)}");

        var data = Field<System.Collections.IEnumerable>(ValueOf(listed), "data");
        Assert.NotNull(data);
        var plan = data!.Cast<object>().Single();
        Assert.Null(Field<int?>(plan, "MaxPcDevices"));
        Assert.Null(Field<int?>(plan, "MaxMobileDevices"));
        // D-4 — price_display 의 뜻은 표시 모드다. 기본값이 'number' 로 들어가야 한다.
        Assert.Equal("number", Field<string>(plan, "PriceDisplay"));

        // ── 2) 🔴 폴백이 5/3 인가 — 설계 §4-2 ① 의 「기본값 0」 사고를 바로 이 자리에서 문다.
        //    실물 DeviceRegistrationController 가 자기 COALESCE SQL 을 DB 에 보내고,
        //    성공 응답의 deviceLimit 가 그 결과다.
        var devices = new DeviceRegistrationController(
            env.Config(), NullLogger<DeviceRegistrationController>.Instance)
        { ControllerContext = OwnerContext() };

        var pc = await devices.Register(new DeviceRegistrationController.RegisterRequest
        {
            LicenseKey = licenseKey, Fingerprint = "w16-fp-pc", DeviceType = "pc",
        }, default);
        Assert.True(StatusOf(pc) == 200, $"{gate} PC 기기 등록이 200 이 아니다. {Describe(pc)}");
        Assert.True(Field<bool>(ValueOf(pc), "success"), $"{gate} PC 등록 success=false. {Describe(pc)}");
        var pcLimit = Field<int>(ValueOf(pc), "deviceLimit");
        Assert.True(pcLimit == 5,
            $"{gate} PC 기기 상한이 {pcLimit} 다(기대 5 — COALESCE(max_pc_devices, 5) 폴백).\n"
          + "  🔴 0 이면 칸 기본값을 0 으로 준 것이다. 그러면 그 요금제의 PC 등록이 **전부 막힌다**(설계 §4-2 ①).");

        var mobile = await devices.Register(new DeviceRegistrationController.RegisterRequest
        {
            LicenseKey = licenseKey, Fingerprint = "w16-fp-mobile", DeviceType = "mobile",
        }, default);
        Assert.True(StatusOf(mobile) == 200, $"{gate} 모바일 기기 등록이 200 이 아니다. {Describe(mobile)}");
        var mobileLimit = Field<int>(ValueOf(mobile), "deviceLimit");
        Assert.True(mobileLimit == 3,
            $"{gate} 모바일 기기 상한이 {mobileLimit} 다(기대 3 — COALESCE(max_mobile_devices, 3) 폴백).");

        // ── 3) 양성 축 — 값을 **넣으면** 그 값이 쓰인다(폴백이 늘 5/3 을 돌려주는 게 아니라는 증거).
        await using (var set = await env.OpenAsync())
        {
            await set.ExecuteAsync(
                "UPDATE pricing_plans SET max_pc_devices = 9 WHERE plan_id = 'basic'");
        }
        var pc2 = await devices.Register(new DeviceRegistrationController.RegisterRequest
        {
            LicenseKey = licenseKey, Fingerprint = "w16-fp-pc2", DeviceType = "pc",
        }, default);
        Assert.True(StatusOf(pc2) == 200, $"{gate} 두 번째 PC 등록이 200 이 아니다. {Describe(pc2)}");
        Assert.True(Field<int>(ValueOf(pc2), "deviceLimit") == 9,
            $"{gate} 칸에 9 를 넣었는데 상한이 {Field<int>(ValueOf(pc2), "deviceLimit")} 다 — 칸을 아예 안 읽고 있다.");

        // ── 4) UPDATE 가 COALESCE 인가 — 화면이 두 칸을 안 보내도 기존 값(9)이 0 으로 안 덮인다.
        var updated = await pricing.UpdatePlan("basic", new PricingAdminController.UpdatePlanRequest
        {
            PlanName = "gate basic 2", MonthlyPrice = 29000m, YearlyPrice = 290000m,
            PriceDisplay = "number", MaxUsers = 5, MaxDevices = 5,
            MaxPcDevices = null, MaxMobileDevices = null,
            AiTokenMonthly = 100000, IsVisible = true, DisplayOrder = 1, Reason = "gate",
        }, default);
        Assert.True(StatusOf(updated) == 200, $"{gate} 요금제 저장이 200 이 아니다. {Describe(updated)}");

        await using var check = await env.OpenAsync();
        var kept = await check.ExecuteScalarAsync<int?>(
            "SELECT max_pc_devices FROM pricing_plans WHERE plan_id = 'basic'");
        Assert.True(kept == 9,
            $"{gate} 두 칸을 안 보낸 저장 뒤 max_pc_devices 가 {(kept?.ToString() ?? "NULL")} 다(기대 9).\n"
          + "  COALESCE(@MaxPcDevices, max_pc_devices) 가 빠지면 기존 설정이 지워진다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-MIG-1 — 멱등 실측
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-MIG-1 🔴 31·43·44 를 같은 DB 에 2회 적재해도 2회 모두 성공한다(멱등 실측)")]
    public async Task G_MIG_1_두_번_적재해도_성공()
    {
        const string gate = "G-MIG-1";
        await using var env = await GateDb.TryCreateAsync(gate, "hitpan_gate_w16_mig1");
        if (env is null) return;

        var files = new[]
        {
            "31_backoffice_z2_landing_signups_tenant_fk.sql",
            "43_backoffice_promotion_legacy_align.sql",
            "44_backoffice_pricing_plan_columns.sql",
        };

        // 1회차는 TryCreateAsync 의 실물 적재기가 이미 돌렸다. 여기서는 **같은 파일을 한 번 더** 돌린다.
        foreach (var f in files)
        {
            try
            {
                await env.ApplyOneAsync(f);
            }
            catch (MySqlException ex)
            {
                throw new Xunit.Sdk.XunitException(
                    $"{gate} {f} 2회차 적재가 errno {ex.Number} 로 실패했다: {ex.Message}\n"
                  + "  IF NOT EXISTS 가 빠졌으면 1050(표 중복)·1060(칸 중복)·1061(키 중복) 이 난다.\n"
                  + "  적재기는 내용이 바뀐 파일을 다시 돌린다 ⇒ 2회차 실패는 **백오피스 기동 거부**다.");
            }
        }

        // 양성 축 — 2회 돌고 나서도 표·칸이 제대로 서 있다.
        await using var db = await env.OpenAsync();
        var cols = (await db.QueryAsync<string>(@"
            SELECT column_name FROM information_schema.columns
            WHERE table_schema = DATABASE() AND table_name = 'pricing_plans'")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var c in new[] { "price_display", "max_pc_devices", "max_mobile_devices" })
            Assert.True(cols.Contains(c), $"{gate} 2회 적재 뒤에도 pricing_plans.{c} 가 없다.");

        var legacy = await db.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = DATABASE() AND table_name IN ('promotions_legacy','promotion_usages')");
        Assert.True(legacy == 2, $"{gate} promotions_legacy·promotion_usages 가 {legacy}/2 개뿐이다.");

        // 🔴 두 기기 칸의 기본값이 NULL 이어야 한다 — 0 이면 기기 등록 상한 0 사고(설계 §4-2 ①).
        var defaults = (await db.QueryAsync<(string Name, string? Dflt, string Nullable)>(@"
            SELECT column_name AS Name, column_default AS Dflt, is_nullable AS Nullable
            FROM information_schema.columns
            WHERE table_schema = DATABASE() AND table_name = 'pricing_plans'
              AND column_name IN ('max_pc_devices','max_mobile_devices')")).ToList();
        Assert.Equal(2, defaults.Count);
        foreach (var d in defaults)
        {
            Assert.True(d.Nullable.Equals("YES", StringComparison.OrdinalIgnoreCase),
                $"{gate} pricing_plans.{d.Name} 가 NOT NULL 이다 — NULL 이 「미설정」이라는 뜻을 못 쓴다.");
            Assert.True(d.Dflt is null || d.Dflt.Equals("NULL", StringComparison.OrdinalIgnoreCase),
                $"{gate} pricing_plans.{d.Name} 의 기본값이 [{d.Dflt}] 다 — NULL 이어야 한다.\n"
              + "  🔴 0 이면 COALESCE 폴백이 안 돌아 그 요금제의 기기 등록이 전부 막힌다.");
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-MIG-2 — 고아 tenant_id 가 있어도 31 이 선다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-MIG-2 🔴 고아 tenant_id 가 있어도 31 이 적재되고 그 행만 NULL 로 복원된다(1452 기동거부 차단)")]
    public async Task G_MIG_2_고아가_있어도_31이_선다()
    {
        const string gate = "G-MIG-2";
        // 🔴 31 **앞까지만** 적재한다 — 31 을 우리 손으로 돌려야 적재 성패를 잴 수 있다.
        await using var env = await GateDb.TryCreateAsync(
            gate, "hitpan_gate_w16_mig2",
            fileFilter: f => string.CompareOrdinal(f, "31") < 0);
        if (env is null) return;

        var goodTenant = Guid.NewGuid().ToString();
        var orphan = Guid.NewGuid().ToString();   // tenants 에 없는 키

        await using (var seed = await env.OpenAsync())
        {
            // 🔴 「칸이 이미 있고 값이 들어 있는 DB」를 만든다 — 설계 §5-1 이 말한 유일한 1452 위험 상태.
            //   (31 이 처음 칸을 만드는 DB 라면 전 행이 NULL 이라 1452 는 애초에 불가하다.)
            await seed.ExecuteAsync(
                "ALTER TABLE landing_signups ADD COLUMN IF NOT EXISTS tenant_id varchar(36) NULL DEFAULT NULL");

            await seed.ExecuteAsync(@"
                INSERT INTO tenants (tenant_id, tenant_code, company_name, status)
                VALUES (@Tid, 'W16M2', 'mig2 gate co', 'active')", new { Tid = goodTenant });

            await seed.ExecuteAsync(@"
                INSERT INTO landing_signups (signup_token, biz_no_hash, company_name, email, phone, status, tenant_id)
                VALUES ('w16-mig2-orphan', 'h1', 'orphan co', 'o@gate.test', '010', 'approved', @Tid)",
                new { Tid = orphan });

            await seed.ExecuteAsync(@"
                INSERT INTO landing_signups (signup_token, biz_no_hash, company_name, email, phone, status, tenant_id)
                VALUES ('w16-mig2-good', 'h2', 'mig2 gate co', 'g@gate.test', '010', 'approved', @Tid)",
                new { Tid = goodTenant });
        }

        // ── 31 을 적재한다. 고아 복원 줄이 빠지면 여기서 errno 1452 로 **기동 거부**다.
        try
        {
            await env.ApplyOneAsync("31_backoffice_z2_landing_signups_tenant_fk.sql");
        }
        catch (MySqlException ex)
        {
            throw new Xunit.Sdk.XunitException(
                $"{gate} 31 적재가 errno {ex.Number} 로 실패했다: {ex.Message}\n"
              + "  1452 = 고아 tenant_id 때문에 FOREIGN KEY 가 안 선 것이다.\n"
              + "  ⇒ 설계 §5-1 의 고아 복원 UPDATE 가 ADD CONSTRAINT **앞**에 있어야 한다.\n"
              + "  이 실패는 백오피스 기동 거부이고, 뒤 번호(40·41·42·43·44)가 전부 안 들어간다.");
        }

        await using var db = await env.OpenAsync();

        // 음성 축 — 고아였던 행은 NULL 로 복원됐다(행 자체는 안 지웠다).
        var orphanRow = await db.QueryFirstAsync<(string? TenantId, int Cnt)>(@"
            SELECT tenant_id AS TenantId, COUNT(*) AS Cnt FROM landing_signups
            WHERE signup_token = 'w16-mig2-orphan'");
        Assert.True(orphanRow.Cnt == 1, $"{gate} 고아 가입서 행이 사라졌다 — 행은 지우지 않는다(정산 추적 보존).");
        Assert.Null(orphanRow.TenantId);

        // 양성 축 — 멀쩡한 키는 **그대로 남는다**(전부 NULL 로 밀어 버리는 봉합이 아니라는 증거).
        var goodRow = await db.ExecuteScalarAsync<string?>(
            "SELECT tenant_id FROM landing_signups WHERE signup_token = 'w16-mig2-good'");
        Assert.True(goodRow == goodTenant,
            $"{gate} 멀쩡한 tenant_id 까지 지워졌다(값 [{goodRow ?? "NULL"}]) — 복원 조건이 너무 넓다.");

        // 외래키가 실제로 섰는가.
        var fk = await db.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM information_schema.table_constraints
            WHERE table_schema = DATABASE() AND table_name = 'landing_signups'
              AND constraint_type = 'FOREIGN KEY' AND constraint_name = 'fk_landing_signups_tenant'");
        Assert.True(fk == 1, $"{gate} fk_landing_signups_tenant 가 안 섰다 — 31 의 설계 의도가 FK 성립이다.");
    }
}
