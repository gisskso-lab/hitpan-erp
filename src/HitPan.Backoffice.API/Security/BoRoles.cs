namespace HitPan.Backoffice.API.Security;

/// <summary>
/// 🔴 백오피스 역할 어휘 — <b>유일 진실원</b> (20261007작10 ①사이클 갈래 ㄱ · 설계 §2-1·§3-1).
///
/// <para><b>역할 enum 4값이 유일 진실원이다.</b> 다른 어떤 값도 권한을 넓히거나 좁히지 못한다.
/// 사다리(등급)는 <see cref="Rank"/> 하나로만 정해지고, 「좁은 쪽 우선」 판정(설계 §3-3)이 그 등급을 쓴다.</para>
///
/// <para>🔴🔴 <b><see cref="DeriveAccountType"/> 는 역호환 <u>파생값</u>이다 — 판정축이 아니다.</b>
/// PM 결재 조건(2026-10-07): 다음 사람이 이 파생값을 고쳐 권한을 바꾸려 들면 축이 또 둘로 갈라진다.
/// <c>account_type</c> 은 작11 이 먼저 세워 둔 방어가 올라타 있는 <b>옛 축</b>이고(설계 C-4 판정),
/// 이 사이클부터는 <b>역할 4값에서 기계적으로 파생</b>될 뿐이다.
/// ⇒ 권한을 바꾸려면 <b>역할(role)</b> 을 바꿔라. <c>account_type</c> 을 손대도 서버 강제는 1mm도 안 움직인다
///   (<c>BoAccessGuard</c> 는 <c>account_type</c> 을 읽지 않는다 — 읽는 곳이 0 인 것이 설계값이다).</para>
///
/// <para>⚠️ 이번 차수(1차수)는 <b>4값 전환을 하지 않는다</b> — 토큰 발급은 2차수(갈래 ㄴ) 몫이다.
/// 그래서 지금 DB 에 들어 있는 값은 아직 옛 어휘다(<c>platform_admins.role</c> =
/// <c>super_admin·billing_admin·cs_admin·readonly</c> · <c>reseller_accounts.role</c> =
/// <c>reseller_admin·reseller_user·reseller_readonly</c> — 실측 DESCRIBE 2026-10-07).
/// <see cref="Normalize"/> 가 그 옛 값을 4값 사다리로 <b>좁은 쪽으로</b> 접어 준다(fail-closed).
/// 2차수가 발급을 4값으로 바꾸면 이 접기표만 줄어들고 판정식은 그대로다.</para>
/// </summary>
public static class BoRoles
{
    // ── 4값 (유일 진실원) ───────────────────────────────────────────
    public const string PlatformOwner = "platform_owner";
    public const string PlatformAdmin = "platform_admin";
    public const string ResellerAdmin = "reseller_admin";
    public const string ResellerUser = "reseller_user";

    /// <summary>역호환 파생 <c>account_type</c> 값 — 파생 결과일 뿐 판정에 쓰지 않는다.</summary>
    public const string AccountTypePlatform = "platform_admin";
    public const string AccountTypeReseller = "reseller_admin";

    public static readonly string[] All4 = { PlatformOwner, PlatformAdmin, ResellerAdmin, ResellerUser };

    /// <summary>대리점 갈래인가(= 고객사 행을 자기 것만 봐야 하는 쪽).</summary>
    public static bool IsReseller(string? role4) =>
        role4 is ResellerAdmin or ResellerUser;

    /// <summary>본사 갈래인가.</summary>
    public static bool IsPlatform(string? role4) =>
        role4 is PlatformOwner or PlatformAdmin;

    /// <summary>
    /// 사다리 등급. <b>갈래 안에서만 비교한다</b>(본사 2 와 대리점 2 를 견주지 않는다 —
    /// 갈래가 다르면 비교가 아니라 403 이다 · 설계 §3-3).
    /// 모르는 값은 0 = 가장 좁음(fail-closed).
    /// </summary>
    public static int Rank(string? role4) => role4 switch
    {
        PlatformOwner => 2,
        PlatformAdmin => 1,
        ResellerAdmin => 2,
        ResellerUser => 1,
        _ => 0,
    };

    /// <summary>
    /// 옛 어휘·대소문자 섞인 값을 4값으로 접는다. <b>모르는 값은 null</b> — 넓은 쪽으로 추측하지 않는다.
    /// <para>⚠️ <c>reseller_readonly</c> → <see cref="ResellerUser"/>: 설계 §8 이 「㉢ 로 보류」라 둔 값이라
    /// 이 차수에서는 사다리의 <b>가장 좁은 칸</b>에 넣는다(읽기 전용을 넓게 풀지 않는다).</para>
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.Trim().ToLowerInvariant() switch
        {
            PlatformOwner => PlatformOwner,
            "owner" => PlatformOwner,          // 옛 Web 정책값(HitPan.Backoffice/Program.cs:61) — 갈래 ㄷ 가 교정
            "super_admin" => PlatformOwner,    // 설계 §3-1 매핑
            PlatformAdmin => PlatformAdmin,
            "admin" => PlatformAdmin,
            "billing_admin" => PlatformAdmin,
            "cs_admin" => PlatformAdmin,
            "readonly" => PlatformAdmin,       // 본사 사다리 최하 칸 — 행 범위는 본사(전건)가 맞고 등급만 낮다
            "platform_manager" => PlatformAdmin,
            "platform_staff" => PlatformAdmin,
            ResellerAdmin => ResellerAdmin,
            ResellerUser => ResellerUser,
            "reseller_readonly" => ResellerUser,
            "reseller" => ResellerUser,        // 옛 account_type 값이 role 자리에 오는 경우 — 좁은 쪽
            _ => null,
        };
    }

    /// <summary>
    /// 🔴 <b>역호환 파생</b> — 역할 4값 → <c>account_type</c> 클레임값. 설계 §3-1 규칙 그대로.
    /// <b>이 함수를 고쳐 권한을 바꾸려 하지 마라</b>(클래스 머리말). 권한은 <c>role</c> 에서만 바뀐다.
    /// </summary>
    public static string? DeriveAccountType(string? role4) =>
        IsPlatform(role4) ? AccountTypePlatform
        : IsReseller(role4) ? AccountTypeReseller
        : null;
}
