using System.Text.RegularExpressions;
using HitPan.Application.Services;
using HitPan.API.Middleware;   // B-4 — 세션 제한 판정을 값으로 대조한다
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 기기 슬롯 계수·한도 게이트 (20260815작3 1차 · P1·P2).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>이 자리는 시험이 0건이었다.</b> 요금이 걸린 계산인데 안전망이 하나도 없었다.
/// 여기 게이트가 <b>처음 생기는 안전망</b>이다.
/// </para>
/// <para>
/// ⚠️ <b>글자만 검사하는 게이트를 만들지 않았다.</b> 작업지시서 §5 가 8/15 메신저 사고를 근거로
/// 못 박은 지적이다 — <c>ChatWindowGuardTests</c> 가 글자만 봐서 통과시키고 사고를 못 잡았다.
/// ⇒ 계수·한도는 <b>실제 판정을 불러 값으로</b> 확인한다. 소스 글자 검사는
/// "복제가 되살아났는가"(G-7) 처럼 <b>글자로만 확인 가능한 것</b>에만 쓴다.
/// </para>
/// <para>
/// ⚠️ <b>시험이 슬롯을 먹지 않는다</b> — 실제 DB 를 쓰지 않는다(P0 실측 ④-D).
/// </para>
/// </remarks>
public class DeviceSlotGuardTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 시험이 소스를 읽을 수 없다.");
    }

    private static string DeviceServiceSource()
        => File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "HitPan.Infrastructure", "Services", "TenantDeviceService.cs"));

    /// <summary>주석을 걷어낸 실제 코드만 남긴다 — 주석에 적힌 예시가 게이트를 속이지 않게.</summary>
    private static string CodeOnly(string source)
    {
        var noBlock = Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return string.Join('\n', noBlock
            .Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
    }

    // ══════════════════════════════════════════════════════════════
    // G-7 — 계수가 단일 메서드를 통과한다 (복제를 되살리면 FAIL)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void G7_슬롯을_세는_SQL은_한_곳뿐이다()
    {
        // 🔴 종전엔 이 SQL 이 네 곳에 복제돼 있었다(:97 · :245 · :410 · :597).
        //   네 곳이 모양까지 달라서 한 곳을 고쳐도 나머지가 옛 규칙으로 돌았다.
        //   [반증] 복제를 하나라도 되살리면 이 시험이 FAIL 한다.
        var code = CodeOnly(DeviceServiceSource());

        var groupByCount = Regex.Matches(code, @"GROUP\s+BY\s+device_type", RegexOptions.IgnoreCase).Count;

        Assert.True(
            groupByCount == 1,
            $"슬롯을 세는 SQL 이 {groupByCount} 곳이다 — 한 곳(CountUsedSlotsAsync)이어야 한다. "
            + "복제가 되살아나면 한 곳만 고쳐지고 나머지가 옛 규칙으로 돌아 요금이 갈린다.");
    }

    [Fact]
    public void G7_한도를_만드는_자리도_한_곳뿐이다()
    {
        // 🔴 계수만 모으고 한도 계산을 안 모으면 여전히 네 곳이 갈린다(P0 실측 D-7).
        //   G-7 이 계수만 보면 그 상태로 통과해 버린다.
        var code = CodeOnly(DeviceServiceSource());

        // 추가슬롯을 한도에 더하는 자리 — ResolveLimits 안에서만 일어나야 한다.
        var extraAdds = Regex.Matches(code, @"(pcLimit|mobileLimit)\s*\+=", RegexOptions.IgnoreCase).Count;

        Assert.True(
            extraAdds == 2,
            $"추가슬롯을 한도에 더하는 자리가 {extraAdds} 곳이다 — ResolveLimits 안의 2줄(PC·모바일)이어야 한다. "
            + "복제가 되살아나면 요금 산식이 갈린다.");
    }

    [Fact]
    public void G7_추가슬롯_모바일은_1배다_1더하기1()
    {
        // 🔴 사장님 확정: "추가슬롯 1+1(PC, 모바일)당 1만원이야"
        //   종전 코드는 mobileLimit += extra * 2 였다(1+2).
        //   [반증] extra * 2 를 되살리면 아래 동작 시험이 FAIL 한다.
        var settings = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["tier.basic.pc_limit"] = 5,
            ["tier.basic.mobile_limit"] = 3,
            ["extra_slot.pc_per_slot"] = 1,
            ["extra_slot.mobile_per_slot"] = 1,
        };

        var (pc, mobile) = TenantDeviceService.ResolveLimits("basic", extraSlots: 3, settings);

        Assert.Equal(5 + 3, pc);        // 5 + 3*1
        Assert.Equal(3 + 3, mobile);    // 🔴 3 + 3*1 = 6. 종전 1+2 였다면 9 가 나온다
    }

    // ══════════════════════════════════════════════════════════════
    // G-8 — 🔴 설정값을 바꾸면 실제 한도가 바뀐다 (동작 검사)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void G8_설정값을_바꾸면_실제_한도가_바뀐다()
    {
        // 🔴 이것이 G-8 의 본체다.
        //   "코드에 리터럴이 없는지" 를 보는 게이트는 **상수를 옮기면 통과하는 가짜**다(작지서 §5).
        //   물어야 할 것은 하나 — 설정값을 바꾸면 실제 한도가 따라 바뀌는가.
        //   [반증] 숫자를 코드 상수로 되돌리면 설정을 바꿔도 값이 안 변해 FAIL 한다.

        var asIs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["tier.basic.pc_limit"] = 5,
            ["tier.basic.mobile_limit"] = 3,
        };
        var (pcBefore, mobileBefore) = TenantDeviceService.ResolveLimits("basic", 0, asIs);
        Assert.Equal(5, pcBefore);
        Assert.Equal(3, mobileBefore);

        // 사장님이 "베이직 컴퓨터를 9대로 올리자" 고 하신 상황 — 코드는 한 줄도 안 고친다.
        var toBe = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["tier.basic.pc_limit"] = 9,
            ["tier.basic.mobile_limit"] = 7,
        };
        var (pcAfter, mobileAfter) = TenantDeviceService.ResolveLimits("basic", 0, toBe);

        Assert.Equal(9, pcAfter);
        Assert.Equal(7, mobileAfter);

        Assert.True(
            pcAfter != pcBefore && mobileAfter != mobileBefore,
            "설정값을 바꿨는데 한도가 그대로다 — 숫자가 아직 코드에 묶여 있다는 뜻이다. "
            + "요금제를 고치려면 재배포가 필요한 상태이며 헌법 #11 위반이다.");
    }

    [Fact]
    public void G8_추가슬롯_배수도_설정에서_읽는다()
    {
        // 산식(1+1)까지 설정이어야 사업이 바뀔 때 코드를 안 고친다.
        var settings = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["tier.pro.pc_limit"] = 10,
            ["tier.pro.mobile_limit"] = 8,
            ["extra_slot.pc_per_slot"] = 2,      // 설정을 흔들어 본다
            ["extra_slot.mobile_per_slot"] = 3,
        };

        var (pc, mobile) = TenantDeviceService.ResolveLimits("pro", extraSlots: 2, settings);

        Assert.Equal(10 + 4, pc);       // 10 + 2*2
        Assert.Equal(8 + 6, mobile);    // 8 + 2*3
    }

    [Fact]
    public void G8_설정이_비어도_종전_숫자로_떨어진다()
    {
        // ⚠️ 마이그레이션이 아직 안 돈 DB 에서도 **로그인이 죽으면 안 된다.**
        //   표가 비면 종전 숫자(FallbackLimits)로 간다 — 한도가 사라지지 않는다.
        var empty = new Dictionary<string, int>();

        Assert.Equal((5, 3), TenantDeviceService.ResolveLimits("basic", 0, empty));
        Assert.Equal((10, 8), TenantDeviceService.ResolveLimits("pro", 0, empty));
        Assert.Equal((10, 5), TenantDeviceService.ResolveLimits("trial", 0, empty));
    }

    [Fact]
    public void G8_enterprise가_basic으로_떨어지지_않는다()
    {
        // 🔴 명세서 §17-4 (PI-14) — 지금도 나고 있던 사고.
        //   코드만 `premium` 이라 쓰고 DDL·시리얼 발급·백오피스는 전부 `enterprise` 라,
        //   최상위 고객이 갈래를 못 찾아 `_ => (5,3)` 으로 떨어졌다.
        //   10만원을 내고 베이직 한도(PC 5)를 쓰고 있었다.
        //   [반증] enterprise 갈래를 지우면 (5,3) 이 나와 FAIL 한다.
        var empty = new Dictionary<string, int>();

        var (pc, mobile) = TenantDeviceService.ResolveLimits("enterprise", 0, empty);

        Assert.True(
            (pc, mobile) != (5, 3),
            "enterprise 가 basic 한도(5,3)로 떨어졌다 — 최상위 고객이 베이직 대수를 받는다.");
        Assert.Equal((100, 80), (pc, mobile));

        // ⚠️ premium(옛 이름)도 살아 있어야 한다 — 옛 데이터에 남아 있을 수 있다(D-16).
        Assert.Equal((100, 80), TenantDeviceService.ResolveLimits("premium", 0, empty));
    }

    [Fact]
    public void G8_모르는_요금제는_종전처럼_기본값을_받는다()
    {
        // 무회귀 — 종전 `_ => (5,3)` 과 같아야 한다.
        var empty = new Dictionary<string, int>();
        Assert.Equal((5, 3), TenantDeviceService.ResolveLimits("아무거나", 0, empty));
        Assert.Equal((5, 3), TenantDeviceService.ResolveLimits(null, 0, empty));
    }

    // ══════════════════════════════════════════════════════════════
    // G-13 — tablet 이 어느 칸에도 안 빠진다
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void G13_태블릿은_휴대기기_칸에_세어진다()
    {
        // 🔴 등호 비교(= 'mobile')로 되돌리면 태블릿이 어느 칸에도 안 잡혀 **공짜로 쓰인다.**
        //   [반증] MobileUsedFrom 에서 tablet 절을 빼면 FAIL 한다.
        var counts = new List<(string t, int c)>
        {
            ("pc", 2), ("mobile", 3), ("tablet", 4)
        };

        Assert.Equal(2, TenantDeviceService.PcUsedFrom(counts));

        var mobileUsed = TenantDeviceService.MobileUsedFrom(counts);
        Assert.True(
            mobileUsed == 7,
            $"휴대기기 칸이 {mobileUsed} 다 — mobile 3 + tablet 4 = 7 이어야 한다. "
            + "태블릿이 빠지면 그 기기가 슬롯을 안 먹고 공짜로 쓰인다.");
    }

    [Fact]
    public void G13_태블릿이_컴퓨터_칸을_먹지_않는다()
    {
        // 반대 방향 — 태블릿을 PC 로 세면 고객이 비싼 칸에 돈을 낸다.
        var counts = new List<(string t, int c)> { ("tablet", 5) };

        Assert.Equal(0, TenantDeviceService.PcUsedFrom(counts));
        Assert.Equal(5, TenantDeviceService.MobileUsedFrom(counts));
    }

    [Fact]
    public void G13_백오피스도_태블릿을_휴대기기로_센다()
    {
        // 🔴 백오피스는 `device_type = @Type` 등호라 tablet 이 어느 칸에도 안 잡혔다(복제 E).
        //   ⚠️ 백오피스는 별 시스템이라 ERP 메서드를 부르지 않는다(D-2). **규칙만** 같게 한다.
        //   [반증] 등호 비교로 되돌리면 IN 절이 사라져 FAIL 한다.
        var src = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "HitPan.Backoffice.API", "Controllers", "DeviceRegistrationController.cs"));
        var code = CodeOnly(src);

        Assert.True(
            Regex.IsMatch(code, @"device_type\s+IN\s*\(\s*'mobile'\s*,\s*'tablet'\s*\)", RegexOptions.IgnoreCase),
            "백오피스 계수에 tablet 이 빠졌다 — 태블릿이 슬롯을 안 먹고 공짜로 쓰인다.");

        Assert.False(
            Regex.IsMatch(code, @"device_type\s*=\s*@Type", RegexOptions.IgnoreCase),
            "백오피스가 아직 등호(device_type = @Type)로 센다 — tablet 이 어느 칸에도 안 잡힌다.");
    }

    // ══════════════════════════════════════════════════════════════
    // I-6 — 모르는 종류는 mobile(싼 칸)로 간다
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void I6_모르는_기기종류는_휴대기기로_본다()
    {
        // 🔴 컴퓨터 칸이 더 비싸다. 애매할 때 컴퓨터로 세면
        //   **고객이 쓰지도 않은 자리에 돈을 낸다.** 애매한 것은 고객에게 유리한 쪽으로.
        //   [반증] `pc` 로 바꾸면 FAIL 한다.
        Assert.Equal("mobile", TenantDeviceService.NormalizeDeviceType("스마트TV"));
        Assert.Equal("mobile", TenantDeviceService.NormalizeDeviceType("watch"));

        // 🔴 2026-08-18 20260818작2 (2-6) — **이 줄의 기대값을 바꿨다. 규칙이 바뀌었기 때문이다.**
        //
        //   [옛 기대] `tablet` → `"mobile"` (저장까지 뭉갰다)
        //   [지금]   `tablet` → `"tablet"` (저장은 가르고, **과금만** 합친다)
        //
        //   🔴 **사장님 결재 3 과 어긋나지 않는다** — *"테블렛,모바일 같이 씀"* 은 **과금** 이야기이고,
        //     과금은 아래 G13 이 지킨다(MobileUsedFrom 이 mobile 과 tablet 을 함께 센다).
        //     칸은 여전히 **둘**이다. 가격표 2칸 구조는 그대로다.
        //
        //   ⚠️ 시험 기대값을 코드에 맞춰 고친 것이 아니라, **규칙이 뒤집혀서 함께 고쳤다.**
        //     근거: docs/운영기록/20260818작2 §2 (2-6) — *"저장은 세밀하게, 과금은 단순하게"*.
        //     저장까지 뭉개면 나중에 *"태블릿은 따로 받자"* 하실 때 **과거 자료가 없어 못 간다.**
        Assert.Equal("tablet", TenantDeviceService.NormalizeDeviceType("tablet"));

        // 아는 값은 그대로
        Assert.Equal("pc", TenantDeviceService.NormalizeDeviceType("pc"));
        Assert.Equal("mobile", TenantDeviceService.NormalizeDeviceType("mobile"));
    }

    [Fact]
    public void I6_종류를_안_보내면_null이다_기존값_보존용()
    {
        // ⚠️ null 은 갱신 경로에서 COALESCE 로 받아 **기존 종류를 지우지 않는다.**
        //   여기에 폴백을 붙이면 종류를 못 보내는 옛 화면이 기존 기기의 칸을 덮어쓴다(D-10).
        Assert.Null(TenantDeviceService.NormalizeDeviceType(null));
        Assert.Null(TenantDeviceService.NormalizeDeviceType("   "));
    }

    [Fact]
    public void I6_pc_폴백이_어디에도_안_남아_있다()
    {
        // 🔴 폴백은 **네 곳**이었다(P0 는 두 곳으로 봤으나 실측하니 DTO 기본값 2개가 더 있었다):
        //     AuthController.cs · TenantDeviceService · RegisterDeviceRequest · 백오피스 RegisterRequest
        //   [반증] 한 곳이라도 되살리면 FAIL 한다. 한 곳만 고치면 아무것도 안 바뀐다(D-9).
        var root = FindRepoRoot();

        var svc = CodeOnly(DeviceServiceSource());
        Assert.False(
            Regex.IsMatch(svc, @"NormalizeDeviceType\s*\([^)]*\)\s*\?\?\s*""pc"""),
            "TenantDeviceService 신규 등록 경로에 ?? \"pc\" 폴백이 살아 있다 — 종류를 안 보내면 비싼 칸으로 간다.");

        var auth = CodeOnly(File.ReadAllText(Path.Combine(
            root, "src", "HitPan.API", "Controllers", "AuthController.cs")));
        Assert.False(
            Regex.IsMatch(auth, @"DeviceType\s*=\s*[^,;]*\?\?\s*""pc"""),
            "AuthController 에 ?? \"pc\" 폴백이 살아 있다 — 서비스의 판정이 도달하지 못한다.");

        var dto = CodeOnly(File.ReadAllText(Path.Combine(
            root, "src", "HitPan.Application", "DTOs", "Device", "DeviceDtos.cs")));
        Assert.False(
            Regex.IsMatch(dto, @"DeviceType\s*\{[^}]*\}\s*=\s*""pc"""),
            "RegisterDeviceRequest 기본값이 \"pc\" 다 — 위 두 곳을 고쳐도 여기가 채워 버린다.");
    }

    // ══════════════════════════════════════════════════════════════
    // I-7 — status='approved' 만 계수한다
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void I7_승인된_기기만_센다()
    {
        // 🔴 pending 을 포함시키면 ApproveAsync 가 **영원히 막힌다** —
        //   승인하려는 그 기기가 자기 자신을 세어 남은 자리가 있어도 한도가 꽉 찬 것으로 보인다(D-4).
        //   [반증] 계수 SQL 에서 status 절을 빼거나 pending 을 넣으면 FAIL 한다.
        var code = CodeOnly(DeviceServiceSource());

        var countSql = Regex.Match(code,
            @"SELECT\s+device_type\s+AS\s+t,\s*COUNT\(\*\)\s+AS\s+c.*?GROUP\s+BY\s+device_type",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        Assert.True(countSql.Success, "계수 SQL(CountUsedSlotsAsync)을 못 찾았다.");

        Assert.True(
            Regex.IsMatch(countSql.Value, @"status\s*=\s*'approved'", RegexOptions.IgnoreCase),
            "계수 SQL 에 status='approved' 절이 없다 — 대기·폐기 기기까지 세어 요금이 틀어진다.");

        Assert.False(
            countSql.Value.Contains("pending", StringComparison.OrdinalIgnoreCase),
            "계수에 pending 이 들어갔다 — 승인하려는 기기가 자기를 세어 승인이 영원히 막힌다(D-4).");
    }

    [Fact]
    public void I7_메인PC를_계수에서_빼지_않는다()
    {
        // 🔴 P0 실측 D-3 — 코드 주석이 "메인PC 는 슬롯을 안 센다" 고 **틀리게** 말하고 있었다.
        //   사장님 확정은 "기기 1대 = 슬롯 1개, **메인PC 포함**".
        //   그 주석을 보고 SQL 에 is_main_pc = 0 을 넣으면 **적게 세어 요금이 샌다.**
        //   [반증] 계수 SQL 에 is_main_pc 제외 절을 넣으면 FAIL 한다.
        var code = CodeOnly(DeviceServiceSource());

        var countSql = Regex.Match(code,
            @"SELECT\s+device_type\s+AS\s+t,\s*COUNT\(\*\)\s+AS\s+c.*?GROUP\s+BY\s+device_type",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        Assert.True(countSql.Success, "계수 SQL 을 못 찾았다.");

        Assert.False(
            Regex.IsMatch(countSql.Value, @"is_main_pc", RegexOptions.IgnoreCase),
            "계수 SQL 이 is_main_pc 를 보고 있다 — 메인PC 를 빼면 적게 세어 요금이 샌다(D-3). "
            + "사장님 확정은 '메인PC 포함' 이다.");
    }

    [Fact]
    public void D3_틀린_주석이_되살아나지_않는다()
    {
        // 🔴 이 사고의 진범은 코드가 아니라 **주석**이었다.
        //   "메인PC 는 슬롯을 안 센다" 는 문장을 읽은 다음 사람이 코드를 주석에 맞추면 요금이 샌다.
        //   그 문장이 **주장으로** 돌아오지 못하게 막는다.
        //
        //   ⚠️ 다만 "이것이 틀린 문장이었다" 고 **인용**하는 것은 정정 기록이라 남아야 한다.
        //     ⇒ [틀린 문장] 표식이 붙은 줄은 인용으로 보고 넘긴다. 그 표식 없이 같은 말이
        //       적혀 있으면 그것은 주장이므로 FAIL 이다.
        var src = DeviceServiceSource();

        var claimed = src.Split('\n')
            .Where(l => l.Contains("메인PC 는 슬롯을 세지 않고", StringComparison.Ordinal))
            .Where(l => !l.Contains("[틀린 문장]", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            claimed.Count == 0,
            "틀린 주석이 주장으로 되살아났다 — 이 문장을 보고 계수에서 메인PC 를 빼면 요금이 샌다(D-3). "
            + $"발견: {string.Join(" / ", claimed.Select(l => l.Trim()))}");

        // 정정이 실제로 적혀 있어야 한다 — 다음 사람이 왜 세는지 알아야 한다.
        Assert.Contains("메인PC 는 **슬롯을 1대로 센다.**", src);
    }

    // ══════════════════════════════════════════════════════════════
    // D-1 — QR 경로는 휴대기기 칸만 본다 (통합하며 깨지기 쉬운 자리)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void D1_QR경로는_컴퓨터_한도를_보지_않는다()
    {
        // 🔴 단일 메서드가 두 값을 다 돌려주므로, "일관성" 을 이유로 QR 경로에서
        //   컴퓨터 한도까지 검사하게 만들면 **종전에 통과하던 QR 등록이 갑자기 막힌다.**
        //   [반증] QR 경로에 pcUsed >= pcLimit 비교를 넣으면 FAIL 한다.
        var code = CodeOnly(DeviceServiceSource());

        var qr = Regex.Match(code,
            @"RegisterMobileByTokenAsync.*?(?=\n\s{4}private\s+async\s+Task\s+MarkTokenUsedAsync)",
            RegexOptions.Singleline);

        Assert.True(qr.Success, "RegisterMobileByTokenAsync 본문을 못 찾았다.");

        Assert.False(
            Regex.IsMatch(qr.Value, @"pcUsed\s*>=\s*pcLimit", RegexOptions.IgnoreCase),
            "QR 경로가 컴퓨터 한도까지 검사한다 — QR 로 들어오는 것은 폰뿐이라 "
            + "컴퓨터 칸이 꽉 찬 회사에서 폰 등록이 막힌다(D-1).");

        Assert.True(
            Regex.IsMatch(qr.Value, @"mobileUsed\s*>=\s*mobileLimit", RegexOptions.IgnoreCase),
            "QR 경로에 휴대기기 한도 검사가 없다 — 슬롯 규칙이 QR 로 우회된다.");
    }

    // ══════════════════════════════════════════════════════════════
    // D-5 — 갱신 경로는 한도를 보지 않는다 (쓰던 사람이 안 막힌다)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void D5_기존기기_갱신경로는_한도검사를_하지_않는다()
    {
        // 🔴 "일관성" 을 이유로 갱신 경로에 한도 검사를 넣으면 **쓰던 사람이 로그인에서 막힌다.**
        //   8/10 에 두 번 무너진 계통이다. 리팩터링이 아니라 동작 변경이다(D-5).
        //   [반증] 갱신 분기에 계수·한도 호출을 넣으면 FAIL 한다.
        var code = CodeOnly(DeviceServiceSource());

        // existing 분기(기존 기기) ~ "2) 신규 기기" 직전까지
        var refresh = Regex.Match(code,
            @"if\s*\(existing\s+is\s+not\s+null\).*?(?=var\s*\(pcLimit,\s*mobileLimit\))",
            RegexOptions.Singleline);

        Assert.True(refresh.Success, "갱신 경로 분기를 못 찾았다.");

        // 🔴 2026-08-18 20260818작2 (2-1) — **무엇을 금지하는지 정확히 했다.**
        //
        //   [옛 기대] 갱신 경로에 `CountUsedSlotsAsync`·`GetLimitsAsync` 가 **한 글자도 없을 것.**
        //   [지금] 2-1 이 `mobile → pc` **승격**에 한도를 다시 본다 — 그래서 계수를 부른다.
        //
        //   🔴 **D-5 의 목적은 조금도 약해지지 않았다.** D-5 가 지키는 것은
        //     *"계수를 부르지 마라"* 가 아니라 **"쓰던 사람을 막지 마라"** 다.
        //     2-1 은 한도를 넘어도 **막지 않는다 — 종류를 안 바꿀 뿐**이고,
        //     그 사람은 계속 종전 칸으로 쓴다(불편 0).
        //     ⇒ 금지해야 할 것은 **계수 호출이 아니라 그 결과로 나가는 거부**다.
        //
        //   ⚠️ 그래서 **정확히 그것**을 검사한다: 갱신 경로에서 한도를 이유로
        //     `LogDeniedAsync("denied_limit")` 를 남기거나 **거부 반환**을 하면 FAIL.
        //   ⚠️ 글자검사의 한계 — **진짜 판정은 표로 한다.**
        //     `DeviceTypeQrGateTests.G13b_모바일에서_PC_승격은_한도가_차면_안_바뀐다` 가
        //     한도를 꽉 채운 격리 DB 에서 실제로 `allowed == true` 를 확인한다.
        Assert.False(
            Regex.IsMatch(refresh.Value, @"denied_limit", RegexOptions.IgnoreCase),
            "기존 기기 갱신 경로가 한도를 이유로 접속을 거부한다 — "
            + "쓰던 사람이 로그인에서 막힌다(D-5 · 2026-08-10 사고 계통).");

        Assert.False(
            Regex.IsMatch(refresh.Value, @"return\s*\(\s*false\s*,\s*LimitExceededMessage"),
            "기존 기기 갱신 경로가 한도초과로 거부를 돌려준다 — "
            + "'막는다' 가 아니라 '안 바꾼다' 여야 한다(20260818작2 §2 2-1).");
    }

    // ══════════════════════════════════════════════════════════════
    // P2.5 — DDL 칸 분리
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void P25_장비넘버_칸이_출하_DDL에_있다()
    {
        // 헌법 #36 — 신규 설치는 clean DDL 한 방이다. 여기 없으면 새 고객에게 칸이 안 생긴다.
        var ddl = File.ReadAllText(Path.Combine(FindRepoRoot(), "installer", "hitpan_db_clean.sql"));

        Assert.Contains("`hardware_id`", ddl);
        Assert.Contains("uq_tenant_hardware", ddl);

        // 🚨 헌법 #37 — fingerprint 를 지우지 않았다. 옛 기기는 이 값으로만 찾을 수 있다.
        Assert.Contains("`fingerprint`", ddl);
        Assert.Contains("uq_tenant_fp", ddl);

        // 🔴 2026-08-16 B-2 봉합 — **여기까지가 글자 검사다. 그것만으론 가짜 게이트다.**
        //
        //   [무엇이 가짜였나] 위 네 줄은 **낱말이 있는지**만 본다. 그래서
        //     `hardware_id` 를 `NOT NULL DEFAULT ''` 로 바꿔도 **네 낱말이 다 그대로 있어**
        //     초록불이 뜬다. 그런데 그 DDL 이면 장비넘버를 아직 못 받은 기기끼리
        //     빈 문자열이 **서로 같은 값**이 되어 `uq_tenant_hardware` 에 걸린다
        //     ⇒ **2번째 기기부터 1062 로 등록이 죽는다.**
        //
        //   ⇒ 낱말 대신 **모양**을 본다. NULL 이 허용돼야 옛 기기가 공존한다.
        //     (진짜 동작 검사는 DeviceSlotSeedGateTests 가 실제 DB 로 한다 — 여기는
        //      DB 없이도 도는 1차 방어선이다.)
        var hwColumn = Regex.Match(ddl, @"`hardware_id`[^,\r\n]*", RegexOptions.IgnoreCase);
        Assert.True(hwColumn.Success, "출하 DDL 에서 hardware_id 칸 정의를 못 찾았다.");

        Assert.False(
            Regex.IsMatch(hwColumn.Value, @"\bNOT\s+NULL\b", RegexOptions.IgnoreCase),
            "🔴 hardware_id 가 NOT NULL 이다 — 장비넘버를 아직 못 받은 옛 기기들이 "
            + "같은 값(빈 문자열)으로 겹쳐 uq_tenant_hardware 에 걸린다. "
            + "**2번째 기기부터 1062 로 등록이 죽는다.** NULL 은 몇 개든 공존하므로 "
            + "이 칸은 반드시 NULL 허용이어야 한다(DB-103 설계). "
            + $"[실측값] {hwColumn.Value.Trim()}");
    }

    [Fact]
    public void P25_마이그레이션이_고객에게_가는_자리에_있고_멱등이다()
    {
        // 🔴 8/12 실사고 — 마이그를 다른 자리에 두면 배포본에 안 실려 고객 DB 가 안 바뀐다.
        var dir = Path.Combine(FindRepoRoot(), "src", "HitPan.API", "Migrations", "SQL");

        var hw = Path.Combine(dir, "DB-103_tenant_devices_hardware_id.sql");
        var policy = Path.Combine(dir, "DB-104_device_slot_policy_settings.sql");

        Assert.True(File.Exists(hw), "DB-103(장비넘버 칸)이 고객에게 가는 폴더에 없다.");
        Assert.True(File.Exists(policy), "DB-104(슬롯 기준값)가 고객에게 가는 폴더에 없다.");

        var hwSql = File.ReadAllText(hw);

        // 멱등 — 업데이트 재시도로 두 번 돌 수 있다
        Assert.Contains("IF NOT EXISTS", hwSql);

        // 🚨 헌법 #37 — fingerprint 를 지우는 문장이 있으면 안 된다
        Assert.False(
            Regex.IsMatch(hwSql, @"DROP\s+COLUMN\s+`?fingerprint", RegexOptions.IgnoreCase),
            "fingerprint 컬럼을 지우고 있다 — 헌법 #37 위반. 등록된 고객 기기를 전부 잃는다.");

        Assert.False(
            Regex.IsMatch(hwSql, @"DROP\s+INDEX\s+`?uq_tenant_fp", RegexOptions.IgnoreCase),
            "옛 기기 UNIQUE(uq_tenant_fp)를 지우고 있다 — 헌법 #37 위반.");

        // 🔴 2026-08-16 B-6 봉합 — **마이그 쪽도 NULL 허용이어야 한다.**
        //
        //   `uq_tenant_hardware` 는 hardware_id 가 **NULL 일 때만** 안전하다.
        //   MariaDB 의 UNIQUE 는 NULL 을 서로 다른 값으로 보므로 옛 기기(장비넘버 미수신)가
        //   몇 대든 공존한다. 그런데 이 칸을 `NOT NULL` 로 바꾸면 미수신 기기들이
        //   같은 빈 문자열로 겹쳐 **2번째부터 1062** 다.
        //
        //   ⚠️ 지금은 `DEFAULT NULL` 로 옳게 돼 있다. 이 게이트는 **앞으로 못 바꾸게** 막는 것이다
        //     (B-6 은 잠복 위험이었다 — 막는 것이 없었다).
        var hwCol = Regex.Match(hwSql, @"ADD\s+COLUMN[^;]*?hardware_id[^,;]*", RegexOptions.IgnoreCase);
        Assert.True(hwCol.Success, "DB-103 에서 hardware_id 칸 추가 문장을 못 찾았다.");

        Assert.False(
            Regex.IsMatch(hwCol.Value, @"\bNOT\s+NULL\b", RegexOptions.IgnoreCase),
            "🔴 DB-103 이 hardware_id 를 NOT NULL 로 만든다 — 장비넘버를 아직 못 받은 "
            + "기존 고객 기기들이 같은 값으로 겹쳐 uq_tenant_hardware 에 걸린다(1062). "
            + "업데이트를 받은 고객의 **2번째 기기부터 등록이 죽는다.** "
            + $"[실측값] {hwCol.Value.Trim()}");
    }

    [Fact]
    public void P25_슬롯_기준값_시드가_현행_숫자_그대로다()
    {
        // ⚠️ 작지서 §6 — 현행 값을 초기값 그대로. 숫자 변경은 이 차수 범위 밖이다.
        //   [반증] 시드 숫자를 바꾸면 FAIL 한다.
        var sql = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "HitPan.API", "Migrations", "SQL", "DB-104_device_slot_policy_settings.sql"));

        Assert.Contains("'tier.basic.pc_limit'", sql);
        Assert.Contains("'tier.enterprise.pc_limit'", sql);

        // 🔴 추가슬롯은 1+1 (사장님 확정)
        Assert.Matches(@"'extra_slot\.mobile_per_slot',\s*1\b", sql);
        Assert.Matches(@"'extra_slot\.pc_per_slot',\s*1\b", sql);

        // 추가슬롯 가격 1만원
        Assert.Matches(@"'extra_slot\.price_krw',\s*10000\b", sql);
    }

    /// <summary>
    /// 🔴 <b>B-4</b> — 동시 세션 제한이 기기 한도보다 먼저 막지 않는가.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>SessionLimitMiddleware</c> 에 <c>enterprise</c> 가 <b>없어서</b> 기본값 50 이 적용됐다.
    /// 그런데 같은 요금제의 기기 한도는 <b>180 대</b>(PC 100 + 휴대기기 80)다
    /// ⇒ 고객이 산 기기를 다 쓰기 전에 <b>429 가 먼저 떠서</b> 막힌다.
    /// </para>
    /// <para>
    /// ⚠️ 이 게이트는 <b>글자가 아니라 값</b>을 본다 — 실제 판정 메서드를 불러 대조한다.
    /// (상수를 다른 파일로 옮기면 통과하던 G-8 의 실패를 되풀이하지 않는다.)
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "B-4 🔴 동시 세션 제한이 기기 한도보다 먼저 막지 않는다")]
    public void B4_세션제한이_기기한도보다_먼저_막지_않는다()
    {
        foreach (var tier in new[] { "basic", "pro", "enterprise", "premium", "trial", "default" })
        {
            var pc = SlotPolicyDefaults.Value($"tier.{tier}.pc_limit", 0);
            var mobile = SlotPolicyDefaults.Value($"tier.{tier}.mobile_limit", 0);
            var devices = pc + mobile;
            if (devices == 0) continue;

            var sessions = SessionLimitMiddleware.TierSessionLimitForTests(tier);

            Assert.True(sessions >= devices,
                $"🔴 '{tier}' 의 동시 세션 제한({sessions})이 기기 한도({devices}대)보다 작다. "
                + "고객이 산 기기를 다 켜기도 전에 429 로 막힌다 — 기기는 등록되는데 "
                + "접속이 안 되는, 원인을 짐작하기 어려운 종류다. "
                + "[반증] SessionLimitMiddleware 에서 enterprise 를 빼면 FAIL 한다.");
        }
    }

    /// <summary>
    /// 🔴 <b>B-3</b> — 백오피스 <c>pricing_plans</c> 시드의 기기 대수가 ERP 정본과 같은가.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 같은 숫자를 아는 곳이 <b>세 곳</b>이 됐다: ①<see cref="SlotPolicyDefaults"/>(ERP 정본) ·
    /// ②마이그 DB-104 · ③백오피스 <c>pricing_plans</c>. ①↔② 는 G-20 이 대조한다.
    /// <b>③이 대조 밖에 있어 갈라져 있었다.</b>
    /// </para>
    /// <para>
    /// 🔴 실측된 어긋남 — <c>basic</c> 의 휴대기기가 <b>0</b> 이었다.
    /// <c>DeviceRegistrationController</c> 가 그 값을 그대로 한도로 쓰므로
    /// <c>typeCount >= 0</c> 이 <b>항상 참</b> ⇒ 베이직 고객은 휴대기기를 한 대도 등록 못 한다.
    /// </para>
    /// <para>
    /// ⚠️ 백오피스는 별 시스템이라 <b>코드는 각자 갖는다</b>(헌법 #35 · D-2).
    /// 그러나 <b>숫자가 어긋나는 것은 분리가 아니라 결함</b>이다 — 그래서 값만 대조한다.
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "B-3 🔴 백오피스 요금제 시드의 기기 대수가 ERP 정본과 같다")]
    public void B3_백오피스_요금제시드가_ERP정본과_같다()
    {
        var path = Path.Combine(FindRepoRoot(), "scripts", "pricing_plans_seed.sql");
        Assert.True(File.Exists(path), $"백오피스 요금제 시드가 없다: {path}");

        var sql = File.ReadAllText(path);

        // INSERT ... VALUES 블록에서 요금제별 (max_pc_devices, max_mobile_devices) 를 뽑는다.
        //   한 줄 형태: 3, 8, 5, 3, 100000,   ← max_users, max_devices, pc, mobile, ai_token
        foreach (var tier in new[] { "basic", "pro", "enterprise" })
        {
            var block = Regex.Match(sql,
                @"\('" + tier + @"',.*?\n\s*\d+,\s*\d+,\s*'[^']*',\s*\n\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),",
                RegexOptions.Singleline);

            Assert.True(block.Success,
                $"'{tier}' 의 기기 대수를 시드에서 못 읽었다 — 이 게이트의 탐지 방식이 깨졌다(형식이 바뀌었는가).");

            var seedPc = int.Parse(block.Groups[3].Value);
            var seedMobile = int.Parse(block.Groups[4].Value);

            var truePc = SlotPolicyDefaults.Value($"tier.{tier}.pc_limit", -1);
            var trueMobile = SlotPolicyDefaults.Value($"tier.{tier}.mobile_limit", -1);

            Assert.True(seedPc == truePc,
                $"🔴 '{tier}' 컴퓨터 대수가 갈렸다 — 백오피스 시드 {seedPc} vs ERP 정본 {truePc}. "
                + "DeviceRegistrationController 가 이 값을 한도로 쓰므로 "
                + "고객이 ERP 에서 보는 대수와 실제로 등록되는 대수가 달라진다.");

            Assert.True(seedMobile == trueMobile,
                $"🔴 '{tier}' 휴대기기 대수가 갈렸다 — 백오피스 시드 {seedMobile} vs ERP 정본 {trueMobile}. "
                + "특히 0 이면 `typeCount >= 0` 이 항상 참이라 **한 대도 등록되지 않는다.**");
        }
    }
}
