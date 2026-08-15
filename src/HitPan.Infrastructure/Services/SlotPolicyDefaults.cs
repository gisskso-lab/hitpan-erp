namespace HitPan.Application.Services;

/// <summary>
/// 기기 슬롯 기준값(DB-104)의 <b>초기값 정의 — 코드 쪽 단일 진실원</b>.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>왜 이 파일이 생겼나</b> — 20260816작1 R-1 봉합.
/// </para>
/// <para>
/// DB-104 시드는 <c>SELECT DISTINCT tenant_id FROM local_subscription</c> 을 기준으로 도는데,
/// <b>신규 설치 시점에는 그 표가 0행</b>이다(회사가 아직 안 만들어졌다).
/// 게다가 출하 DDL 이 <c>('DB-104','clean-ddl',1)</c> 로 <b>이미 성공 표시</b>를 해서
/// 회사가 생긴 뒤에도 <see cref="T:HitPan.API.Migrations.MigrationRunner"/> 가 다시 돌리지 않는다.
/// ⇒ <b>신규 고객은 정책 표가 영원히 비어</b> 언제나 안전망(<c>FallbackLimits</c>)으로만 돌았다.
/// 요금 한도를 설정으로 꺼낸다는 이 작업의 목적이 <b>신규 고객에게 무력</b>이었다.
/// </para>
/// <para>
/// ⇒ 봉합은 <b>회사가 만들어지는 시점에 시드</b>하는 것이다
/// (<c>CompanyBootstrapProvisioner</c> — 노무 기준값 DB-96 이 같은 문제를 이미 그렇게 풀었다).
/// 그 시점에는 테넌트가 존재하므로 <c>CROSS JOIN</c> 문제가 없다.
/// </para>
///
/// <para>
/// 🔴 <b>왜 값을 여기 한 곳에 모았나 — 두 벌로 적으면 반드시 갈라진다</b>
/// </para>
/// <para>
/// 이제 같은 숫자를 <b>세 곳</b>이 알아야 한다:
/// ①마이그 DB-104(기존 고객) ②회사 생성 시드(신규 고객) ③안전망 <c>FallbackLimits</c>.
/// 세 곳에 숫자를 따로 적으면 <b>하나를 고칠 때 나머지가 옛 숫자로 남는다</b> —
/// 이 차수가 계수 8곳을 2곳으로 모으며 없앤 바로 그 병이다.
/// </para>
/// <para>
/// ⚠️ <b>완전한 단일 정의는 불가능하다.</b> DB-104 는 <c>MigrationRunner</c> 가 실행하는
/// <b>순수 .sql 파일</b>이라 C# 값을 주입할 수 없고, <b>이미 적용된 고객이 있을 수 있어
/// 고쳐서도 안 된다</b>(마이그는 되돌리지 않는다 · 작지서 §2 "하지 않는 것").
/// </para>
/// <para>
/// ⇒ <b>차선을 정확히 택했다</b>: 코드가 쓰는 값은 <b>이 파일 하나</b>가 정의하고
/// (②시드와 ③안전망이 <b>둘 다 여기서</b> 읽는다),
/// ①DB-104 와의 일치는 <b>게이트가 실제로 대조</b>한다(G-20 · <c>DeviceSlotGuardTests</c>).
/// 갈라지는 순간 <b>시험이 빨간불</b>이 되므로, 조용히 어긋날 수 없다.
/// 즉 <b>값이 두 벌인 것이 아니라, 한 벌을 두 표현으로 적고 기계가 대조</b>한다.
/// </para>
///
/// <para>
/// ⚠️ <b>여기 숫자를 고쳐서 요금제를 바꾸면 안 된다</b>(헌법 #11).
/// 이 값은 <b>회사가 처음 만들어질 때 한 번 깔리는 출발점</b>일 뿐이고,
/// 그 뒤로는 대표가 화면에서 고친다. 이미 고친 회사는 <b>덮어쓰지 않는다</b>(G-22).
/// 요금제 자체를 바꾸려면 이 표의 <b>값</b>을 갈아끼우는 것이지 코드를 고치는 게 아니다.
/// </para>
/// <para>
/// ⚠️ <c>public</c> 인 이유는 <b>회사 생성 시드가 다른 어셈블리에 있기 때문</b>이다
/// (<c>HitPan.API</c> 의 <c>CompanyBootstrapProvisioner</c>). 값을 그쪽에 또 적지 않으려면
/// 여기 정의가 그쪽에서 보여야 한다 — <b>단일 정의를 위해 여는 것</b>이지 확장점이 아니다.
/// </para>
/// </remarks>
public static class SlotPolicyDefaults
{
    /// <summary>기준값 한 줄 — 열쇠·값·단위·이름·설명.</summary>
    /// <param name="Key">설정 열쇠. 예: <c>tier.basic.pc_limit</c></param>
    /// <param name="Value">값. 대수·금액 모두 정수 하나로 담는다.</param>
    /// <param name="Unit"><c>count</c>(대) 또는 <c>krw</c>(원).</param>
    /// <param name="Label">화면에 그대로 보여주는 사람이 읽는 이름.</param>
    /// <param name="Description">무엇을 뜻하는 값인지.</param>
    public readonly record struct Row(
        string Key, int Value, string Unit, string Label, string Description);

    /// <summary>
    /// 회사가 처음 만들어질 때 깔리는 기준값 15줄 — <b>DB-104 시드와 한 글자도 다르지 않다.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 순서·문구까지 DB-104 를 그대로 따른다. 게이트가 <b>열쇠와 값을 전수 대조</b>하므로
    /// 여기를 고치면 DB-104 도 같이 고쳐야 하고, 안 그러면 <b>시험이 막는다</b>.
    /// </remarks>
    public static readonly IReadOnlyList<Row> Rows = new Row[]
    {
        // ── 티어별 기본 제공 대수 ──
        new("tier.basic.pc_limit", 5, "count", "베이직 · 컴퓨터 대수",
            "베이직 요금제가 기본 제공하는 컴퓨터 대수"),
        new("tier.basic.mobile_limit", 3, "count", "베이직 · 휴대기기 대수",
            "베이직 요금제가 기본 제공하는 휴대기기 대수"),
        new("tier.pro.pc_limit", 10, "count", "프로 · 컴퓨터 대수",
            "프로 요금제가 기본 제공하는 컴퓨터 대수"),
        new("tier.pro.mobile_limit", 8, "count", "프로 · 휴대기기 대수",
            "프로 요금제가 기본 제공하는 휴대기기 대수"),

        // 🔴 enterprise = 실제로 발급되는 최상위 값. 없어서 basic 으로 떨어지던 것을 정정한 갈래다.
        new("tier.enterprise.pc_limit", 100, "count", "엔터프라이즈 · 컴퓨터 대수",
            "엔터프라이즈 요금제가 기본 제공하는 컴퓨터 대수"),
        new("tier.enterprise.mobile_limit", 80, "count", "엔터프라이즈 · 휴대기기 대수",
            "엔터프라이즈 요금제가 기본 제공하는 휴대기기 대수"),

        // ⚠️ premium = 옛 이름. enterprise 와 같은 한도로 남긴다(지우면 그 고객이 basic 으로 떨어진다).
        new("tier.premium.pc_limit", 100, "count", "프리미엄(옛 이름) · 컴퓨터 대수",
            "엔터프라이즈의 옛 이름. 같은 한도를 준다"),
        new("tier.premium.mobile_limit", 80, "count", "프리미엄(옛 이름) · 휴대기기 대수",
            "엔터프라이즈의 옛 이름. 같은 한도를 준다"),

        new("tier.trial.pc_limit", 10, "count", "체험 · 컴퓨터 대수",
            "체험 기간에 제공하는 컴퓨터 대수"),
        new("tier.trial.mobile_limit", 5, "count", "체험 · 휴대기기 대수",
            "체험 기간에 제공하는 휴대기기 대수"),

        // ── 모르는 요금제가 왔을 때 ──
        new("tier.default.pc_limit", 5, "count", "기본값 · 컴퓨터 대수",
            "모르는 요금제가 들어왔을 때 주는 컴퓨터 대수"),
        new("tier.default.mobile_limit", 3, "count", "기본값 · 휴대기기 대수",
            "모르는 요금제가 들어왔을 때 주는 휴대기기 대수"),

        // ── 추가슬롯 1개를 사면 몇 대가 늘어나나 — 🔴 1+1 (사장님 확정) ──
        new("extra_slot.pc_per_slot", 1, "count", "추가슬롯 1개당 컴퓨터",
            "추가슬롯 하나를 사면 늘어나는 컴퓨터 대수"),
        new("extra_slot.mobile_per_slot", 1, "count", "추가슬롯 1개당 휴대기기",
            "추가슬롯 하나를 사면 늘어나는 휴대기기 대수. 사장님 확정 1+1"),

        // ── 추가슬롯 가격 ──
        new("extra_slot.price_krw", 10000, "krw", "추가슬롯 1개 가격(원)",
            "컴퓨터 1대 + 휴대기기 1대를 늘리는 값. 사장님 확정 1만원"),
    };

    private static readonly Dictionary<string, int> ByKey =
        Rows.ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>열쇠 하나의 초기값. 모르는 열쇠는 <paramref name="fallback"/>.</summary>
    /// <remarks>
    /// 🔴 <b>여기서 던지면 안 된다.</b> 이 값은 <c>TenantDeviceService</c> 의 안전망이 쓰고,
    /// 그 안전망은 <b>로그인 경로</b>에서 불린다. 모르는 열쇠에 예외를 던지면
    /// <b>고객이 로그인을 못 한다</b> — 안전망이 사고의 원인이 되는 것이 가장 나쁘다.
    /// <para>
    /// ⚠️ 기본 <paramref name="fallback"/> 이 0 이 아니라 <b>안전한 최소값</b>인 이유:
    /// 한도가 0 이면 <b>기기를 한 대도 등록 못 해</b> 회사 전체가 멈춘다.
    /// 열쇠가 없는 상황은 <c>tier.default.*</c> 로 이미 덮이므로 실제로는 도달하지 않지만,
    /// 도달하더라도 <b>업무가 멈추지 않는 쪽</b>으로 떨어뜨린다.
    /// </para>
    /// </remarks>
    public static int Value(string key, int fallback = 1)
        => ByKey.TryGetValue(key, out var v) ? v : fallback;
}
