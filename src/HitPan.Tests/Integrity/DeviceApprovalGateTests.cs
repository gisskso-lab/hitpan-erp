using System.Text.RegularExpressions;
using HitPan.Application.Common;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 기기 승인 관문 게이트 (20260816작2 2차 · 사장님 전결).
/// </summary>
/// <remarks>
/// <para>
/// 사장님이 1.2.81 을 백지 실측하시고 <b>3증상이 하나도 안 닫혔다</b>고 하셨다 —
/// ① 메인PC 가 슬롯 2개 ② QR 모바일 등록이 중복으로 잡힘 ③ 로그인과 ERP 사이에 아무것도 없음.
/// 이 게이트는 그 셋을 닫은 것이 <b>되돌아가지 않게</b> 지킨다.
/// </para>
///
/// <para>
/// 🔴 <b>초록불이 어디서 오는지 물었다.</b> 어제(8/16 1차) G-21 이
/// <c>UNIQUE(tenant_id, policy_key)</c> 때문에 <b>코드 가드를 둘 다 빼도 초록불</b>이었다 —
/// 내 코드가 아니라 DB 제약조건을 시험하고 있었다.
/// ⇒ 여기서는 <b>DB 없이 도는 것은 값으로</b>, <b>SQL 이 걸린 것은 그 SQL 의 모양으로</b> 본다.
/// </para>
///
/// <para>
/// ⚠️ <b>글자만 보는 게이트를 만들지 않았다.</b> 다만 "조회 순서" 처럼 값으로 확인할 수 없는 것은
/// 소스를 읽되 <b>주석을 걷어낸 실제 코드</b>에서만 찾는다 — 주석에 적힌 설명이 게이트를 속이지 않게.
/// </para>
/// </remarks>
public class DeviceApprovalGateTests
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

    private static string ReadSource(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray()));

    /// <summary>주석을 걷어낸 실제 코드만 남긴다.</summary>
    private static string CodeOnly(string source)
    {
        var noBlock = Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return string.Join('\n', noBlock
            .Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Where(l => !l.TrimStart().StartsWith("///", StringComparison.Ordinal)));
    }

    private static string DeviceServiceCode()
        => CodeOnly(ReadSource("src", "HitPan.Infrastructure", "Services", "TenantDeviceService.cs"));

    private static string AuthControllerCode()
        => CodeOnly(ReadSource("src", "HitPan.API", "Controllers", "AuthController.cs"));

    // ══════════════════════════════════════════════════════════════════
    // G-24 — 승인 대기는 로그인 거부가 아니다 (🔴 P0 자리)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-24. 승인 대기 기기를 401 로 막지 않는다.</b>
    ///
    /// <para>
    /// [무엇이 났었나] <c>TenantDeviceService</c> 는 승인제가 켜지면 <c>allowed=false</c> 를 주면서
    /// 주석에 <i>"이것은 로그인 거부가 아니다"</i> 라고 적어 뒀는데,
    /// <c>AuthController</c> 는 <c>allowed=false</c> 를 <b>전부 401</b> 로 바꿨다.
    /// ⇒ 승인제를 켜는 순간 <b>새 기기가 로그인조차 못 한다.</b>
    /// </para>
    ///
    /// <para>
    /// [왜 P0 인가] 사장님 결재(20260815 §3) — <i>"401 을 내지 않는다. 로그인은 통과,
    /// 중간 화면에서 제어"</i>. 로그인이 막히면 사장님이 설계하신 [디바이스 인증] 관문에
    /// <b>도달조차 못 한다.</b> 화면을 아무리 잘 만들어도 아무도 볼 수 없다.
    /// </para>
    ///
    /// <para>
    /// [반증] <c>deviceAwaitingApproval</c> 갈래를 지우고 <c>if (!allowed) return Unauthorized</c> 로
    /// 되돌리면 이 시험이 FAIL 한다.
    /// </para>
    /// </summary>
    [Fact]
    public void G24_승인대기는_401로_막지_않는다()
    {
        var code = AuthControllerCode();

        Assert.True(
            code.Contains("deviceAwaitingApproval", StringComparison.Ordinal),
            "AuthController 가 '승인 대기' 를 따로 가르지 않는다. " +
            "allowed=false 를 전부 401 로 바꾸면 승인제를 켜는 순간 새 기기가 로그인조차 못 한다 " +
            "— 그러면 [디바이스 인증] 관문에 도달할 수 없다(사장님 결재 위반).");

        // 🔴 갈래가 있다고 끝이 아니다 — **그 갈래가 실제로 401 을 비껴가는지**를 본다.
        //   `if (!allowed)` 하나만 남아 있으면 갈래를 만들어 놓고 안 쓰는 것이다.
        var blindReject = Regex.IsMatch(code, @"if\s*\(\s*!\s*allowed\s*\)");
        Assert.False(
            blindReject,
            "`if (!allowed)` 로 무조건 거부하는 자리가 남아 있다. " +
            "승인 대기(deviceId 가 발급된 경우)는 통과시켜야 한다.");
    }

    /// <summary>
    /// <b>G-24-b. 그래도 폐기된 기기는 막는다.</b>
    ///
    /// <para>
    /// 승인 대기를 통과시키느라 <b>거부 갈래를 통째로 없애면</b> 폐기한 기기가 다시 들어온다.
    /// 대표가 "이 기기 못 쓰게 해" 라고 눌렀던 것이 무효가 되는 자리다.
    /// </para>
    ///
    /// <para>[반증] <c>Unauthorized</c> 를 지우면 FAIL.</para>
    /// </summary>
    [Fact]
    public void G24b_폐기된_기기는_여전히_거부한다()
    {
        var code = AuthControllerCode();

        Assert.True(
            code.Contains("Unauthorized", StringComparison.Ordinal),
            "로그인 거부 갈래가 아예 사라졌다. 승인 대기를 통과시키는 것과 " +
            "폐기된 기기를 통과시키는 것은 다른 일이다 — 폐기는 계속 막아야 한다.");
    }

    // ══════════════════════════════════════════════════════════════════
    // G-25 — 장비넘버가 1순위 열쇠다 (①②의 뿌리)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-25. 기기를 찾을 때 장비넘버를 먼저 본다.</b>
    ///
    /// <para>
    /// [무엇이 났었나] 조회가 <b>지문으로만</b> 돌았다. 지문은 <c>device-fingerprint.js</c> 의
    /// <c>_envSeed()</c> 가 <b>userAgent 를 씨앗에 넣기</b> 때문에 브라우저가 바뀌면 달라진다.
    /// ⇒ 같은 PC 인데 Edge 와 Chrome 이 <b>서로 다른 기기</b>로 잡혀 슬롯을 두 번 먹었다.
    /// 사장님이 실측하신 증상 ①(메인PC 슬롯 2개)·②(QR 중복)의 <b>공통 뿌리</b>다.
    /// </para>
    ///
    /// <para>
    /// [반증] <c>WHERE tenant_id = @TenantId AND device_id = @DeviceId</c> 조회를 지우면 FAIL.
    /// </para>
    /// </summary>
    [Fact]
    public void G25_장비넘버로_먼저_찾는다()
    {
        var code = DeviceServiceCode();

        // 실제 SQL 의 모양을 본다 — "device_id 라는 낱말이 어딘가 있다" 로는 부족하다.
        //   조회 조건에 실제로 걸려 있어야 브라우저가 바뀌어도 같은 줄로 잡힌다.
        var looksUpByDeviceId = Regex.IsMatch(
            code,
            @"WHERE\s+tenant_id\s*=\s*@TenantId\s+AND\s+device_id\s*=\s*@DeviceId",
            RegexOptions.IgnoreCase);

        Assert.True(
            looksUpByDeviceId,
            "로그인이 장비넘버(device_id)로 기존 기기를 찾지 않는다. " +
            "지문만으로 찾으면 브라우저를 바꿀 때마다 새 슬롯을 먹는다 " +
            "— 사장님이 실측하신 '메인PC 가 슬롯 2개' 가 바로 이것이다.");
    }

    /// <summary>
    /// 🚨 <b>G-25-b. 지문 조회를 없애지 않는다</b> (헌법 #37 · #1).
    ///
    /// <para>
    /// 장비넘버를 1순위로 올렸다고 지문 조회를 지우면, <b>이미 등록된 고객 기기가 전부
    /// 새 기기가 된다</b> — 그 기기들은 장비넘버를 아직 못 받았고 지문으로만 찾을 수 있다.
    /// 슬롯을 처음부터 다시 먹는다. <i>"안 읽힌다 ≠ 잔재"</i>.
    /// </para>
    ///
    /// <para>[반증] 지문 조회 SQL 을 지우면 FAIL.</para>
    /// </summary>
    [Fact]
    public void G25b_옛기기용_지문조회를_남겨둔다()
    {
        var code = DeviceServiceCode();

        var looksUpByFingerprint = Regex.IsMatch(
            code,
            @"WHERE\s+tenant_id\s*=\s*@TenantId\s+AND\s+fingerprint\s*=\s*@Fp",
            RegexOptions.IgnoreCase);

        Assert.True(
            looksUpByFingerprint,
            "지문 조회가 사라졌다. 이미 등록된 고객 기기는 장비넘버가 없어 지문으로만 찾을 수 있다 " +
            "— 지우면 쓰던 사람이 전부 새 기기가 되어 슬롯을 다시 먹는다(헌법 #37).");
    }

    /// <summary>
    /// 🔴 <b>G-25-c. 장비넘버는 같은 회사 안에서만 인정한다</b> (헌법 #2).
    ///
    /// <para>
    /// 남의 회사 장비넘버를 들고 와도 잡히면 안 된다.
    /// 조회 조건에 <c>tenant_id</c> 가 반드시 함께 있어야 한다.
    /// </para>
    /// </summary>
    [Fact]
    public void G25c_장비넘버_조회에_테넌트가_함께_걸린다()
    {
        var code = DeviceServiceCode();

        // device_id 로 찾는 모든 자리에 tenant_id 가 함께 있는지 — 하나라도 빠지면 격리가 뚫린다.
        var deviceIdLookups = Regex.Matches(code, @"AND\s+device_id\s*=\s*@DeviceId", RegexOptions.IgnoreCase);
        Assert.True(deviceIdLookups.Count > 0, "장비넘버 조회 자체가 없다 — G-25 를 먼저 보라.");

        foreach (Match m in deviceIdLookups)
        {
            // 그 조건 앞 200자 안에 tenant_id 절이 있어야 한다.
            var start = Math.Max(0, m.Index - 200);
            var window = code.Substring(start, m.Index - start);
            Assert.True(
                window.Contains("tenant_id", StringComparison.OrdinalIgnoreCase),
                "장비넘버로 기기를 찾는데 tenant_id 가 함께 걸려 있지 않다 — " +
                "남의 회사 기기가 잡힌다(헌법 #2 테넌트 격리).");
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // G-26 — 확인번호 (대표가 눈으로 대조하는 값)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>G-26. 같은 기기는 언제나 같은 확인번호가 나온다.</b>
    ///
    /// <para>
    /// 대표 화면과 신청 기기 화면이 <b>같은 번호</b>를 보여야 대조가 성립한다.
    /// 계산이 흔들리면 번호가 안 맞고, 대표는 다르다고 보고 거절하거나
    /// 더 나쁘게는 <b>번호를 무시하는 습관</b>이 든다.
    /// </para>
    ///
    /// <para>🔴 이것은 <b>값으로</b> 확인한다 — 글자 검사가 아니다.</para>
    /// </summary>
    [Fact]
    public void G26_확인번호는_같은_기기면_항상_같다()
    {
        const string id = "6f1c2f6e-3a44-4a5e-9d2b-71c0d5a9e001";

        var a = DeviceConfirmCode.From(id);
        var b = DeviceConfirmCode.From(id);

        Assert.NotNull(a);
        Assert.Equal(a, b);
    }

    /// <summary>
    /// <b>G-26-b. 항상 4자리다</b> — 앞자리 0 도 지우지 않는다.
    ///
    /// <para>
    /// "0042" 가 "42" 로 보이면 대표와 직원이 서로 다른 것을 읽는다.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    [InlineData("11111111-1111-1111-1111-111111111111")]
    [InlineData("a")]
    [InlineData("메인PC-슬롯1")]
    public void G26b_확인번호는_언제나_네자리다(string deviceId)
    {
        var code = DeviceConfirmCode.From(deviceId);

        Assert.NotNull(code);
        Assert.Equal(4, code!.Length);
        Assert.Matches(@"^\d{4}$", code);
    }

    /// <summary>
    /// <b>G-26-c. 다른 기기는 다른 번호가 나온다</b> (대체로).
    ///
    /// <para>
    /// 4자리라 충돌이 아주 없을 수는 없다. 다만 <b>연달아 만든 기기들이 같은 번호</b>로 쏟아지면
    /// 대조가 무의미해진다. 100개를 만들어 <b>서로 다른 번호가 90개 이상</b> 나오는지 본다.
    /// </para>
    ///
    /// <para>⚠️ 이 번호는 열쇠가 아니라 <b>눈으로 잇는 표식</b>이라 완전한 유일성은 필요 없다.</para>
    /// </summary>
    [Fact]
    public void G26c_다른_기기는_대체로_다른_번호가_나온다()
    {
        var codes = Enumerable.Range(0, 100)
            .Select(i => DeviceConfirmCode.From($"6f1c2f6e-3a44-4a5e-9d2b-71c0d5a9e{i:D3}"))
            .ToList();

        var distinct = codes.Distinct().Count();

        Assert.True(
            distinct >= 90,
            $"기기 100대에서 확인번호가 {distinct}가지밖에 안 나온다. " +
            "번호가 겹치면 대표가 어느 기기를 승인하는지 알 수 없다.");
    }

    /// <summary>
    /// <b>G-26-d. 보여줄 기기가 없으면 번호도 없다.</b>
    /// 빈 값에 억지로 번호를 만들면 <b>있지도 않은 기기의 번호</b>가 화면에 뜬다.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void G26d_기기가_없으면_번호도_없다(string? deviceId)
        => Assert.Null(DeviceConfirmCode.From(deviceId));

    // ══════════════════════════════════════════════════════════════════
    // G-27 — QR 도 같은 관문을 탄다 (사장님 전결 · 1차 결재 뒤집기)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-27. QR 로 들어온 폰도 대표 승인을 거친다.</b>
    ///
    /// <para>
    /// [뒤집은 결재] 1차에서는 <i>"QR 을 띄운 것 자체가 대표의 승인"</i> 으로 결재받아
    /// 바로 <c>approved</c> 로 넣었다. 사장님이 2026-08-16 에 뒤집으셨다 —
    /// <i>"PC환경 절차와 같은 절차가 있어야 함."</i>
    /// </para>
    ///
    /// <para>
    /// [왜] QR 이 화면에 떠 있는 10분 동안 <b>옆 사람 폰이 찍어도 등록</b>된다.
    /// </para>
    ///
    /// <para>
    /// [반증] QR INSERT 를 <c>'approved'</c> 리터럴로 되돌리면 FAIL.
    /// </para>
    /// </summary>
    [Fact]
    public void G27_QR등록도_승인제를_따른다()
    {
        var code = DeviceServiceCode();

        // QR INSERT 가 status 를 **고정 문자열로 박지 않고** 스위치를 보는지.
        Assert.True(
            code.Contains("qrStatus", StringComparison.Ordinal),
            "QR 등록이 승인제 스위치를 보지 않는다. " +
            "QR 이 떠 있는 10분 동안 옆 사람 폰이 찍어도 그대로 등록된다(사장님 전결 위반).");

        Assert.True(
            Regex.IsMatch(code, @"qrStatus\s*=\s*_approvalEnabled\s*\?\s*""pending""\s*:\s*""approved"""),
            "QR 의 status 가 승인제 스위치에 따라 갈리지 않는다.");
    }

    /// <summary>
    /// 🔴 <b>G-27-b. 승인 대기로 들어간 QR 기기는 승인자가 비어 있다.</b>
    ///
    /// <para>
    /// 대기 상태인데 <c>approved_by</c> 에 대표를 적어 넣으면
    /// <b>승인받지 않았는데 승인된 것처럼</b> 기록된다. 나중에 "누가 승인했나" 를 볼 때 거짓이 된다.
    /// </para>
    /// </summary>
    [Fact]
    public void G27b_대기중인_QR기기는_승인자가_비어있다()
    {
        var code = DeviceServiceCode();

        Assert.True(
            Regex.IsMatch(code, @"By\s*=\s*_approvalEnabled\s*\?\s*null\s*:\s*issuedBy"),
            "QR 등록이 승인 대기인데도 승인자를 적어 넣는다 — " +
            "승인받지 않은 기기가 '대표가 승인함' 으로 기록된다.");
    }

    // ══════════════════════════════════════════════════════════════════
    // G-28 — 관문이 실제로 화면에 걸려 있다
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-28. 로그인이 승인 대기를 화면에 알린다.</b>
    ///
    /// <para>
    /// 서버가 <c>DeviceAwaitingApproval</c> 을 실어 보내도 <b>화면이 그것을 안 보면</b>
    /// 대기 중인 기기가 그대로 ERP 로 들어간다 — 관문이 없는 것과 같다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ 이 자리는 1차에서 실제로 당했다 — <c>hardware_id</c> 컬럼을 만들어 놓고
    /// <b>읽고 쓰는 코드를 아무도 안 붙여서</b> 아무것도 안 바뀌었다.
    /// <b>칸을 만드는 것과 배선하는 것은 다른 일이다.</b>
    /// </para>
    /// </summary>
    [Fact]
    public void G28_로그인화면이_승인대기를_받아_관문을_켠다()
    {
        var login = CodeOnly(ReadSource("src", "HitPan.Web", "Pages", "Login.razor"));

        Assert.True(
            login.Contains("DeviceAwaitingApproval", StringComparison.Ordinal),
            "로그인 화면이 '승인 대기' 를 보지 않는다 — 대기 중인 기기가 그대로 ERP 로 들어간다.");

        Assert.True(
            login.Contains("MarkAwaitingApproval", StringComparison.Ordinal),
            "로그인 화면이 관문을 켜지 않는다. 값만 받고 아무것도 안 하면 관문이 없는 것과 같다.");
    }

    /// <summary>
    /// 🔴 <b>G-28-b. 서버가 그 값을 실제로 채운다.</b>
    ///
    /// <para>
    /// 화면이 아무리 잘 봐도 <b>서버가 안 채우면</b> 언제나 <c>false</c> 다.
    /// 양쪽이 다 있어야 관문이 선다.
    /// </para>
    /// </summary>
    [Fact]
    public void G28b_서버가_승인대기_표시를_채운다()
    {
        var code = AuthControllerCode();

        Assert.True(
            Regex.IsMatch(code, @"response\.DeviceAwaitingApproval\s*="),
            "서버가 '승인 대기' 표시를 응답에 채우지 않는다 — 화면은 언제나 false 를 받는다.");
    }

    /// <summary>
    /// 🔴 <b>G-28-c. 로그인이 장비넘버를 서버로 보낸다.</b>
    ///
    /// <para>
    /// 명세서 §4-4 가 <i>"이 설계의 핵심 한 줄"</i> 이라 적은 자리다.
    /// 함수(<c>getDeviceId</c>)는 원래 있었고 <b>부르는 쪽이 0곳</b>이었다 —
    /// 서버가 번호를 내려주고 기기가 저장까지 하는데 <b>도로 보내지 않았다.</b>
    /// </para>
    ///
    /// <para>[반증] <c>AuthService</c> 에서 <c>getDeviceId</c> 호출을 지우면 FAIL.</para>
    /// </summary>
    [Fact]
    public void G28c_로그인이_장비넘버를_보낸다()
    {
        var authService = CodeOnly(ReadSource("src", "HitPan.Web", "Services", "AuthService.cs"));

        Assert.True(
            authService.Contains("hitpanDevice.getDeviceId", StringComparison.Ordinal),
            "로그인이 장비넘버를 읽지 않는다 — 서버는 매번 처음 보는 기기로 여긴다.");

        Assert.True(
            Regex.IsMatch(authService, @"DeviceId\s*=\s*deviceId"),
            "장비넘버를 읽기만 하고 요청에 싣지 않는다. " +
            "읽는 곳과 보내는 곳이 따로 놀면 아무것도 안 바뀐다(1차 hardware_id 가 그랬다).");
    }
}
