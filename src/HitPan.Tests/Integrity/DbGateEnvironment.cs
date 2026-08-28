using System;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 DB 게이트가 조용히 통과하는 것을 막는다 (20260828 작14 W1).
///
/// 사고: DB 가 없으면 게이트가 <c>return</c> 으로 빠져나왔다. xUnit 에게 그건 PASS 다.
/// 그 패턴이 게이트 파일 16개에 깔려 있었고, CI 의 <c>build</c> 잡엔 DB 가 아예 없었다.
/// ⇒ 진짜 게이트마저 한 번도 안 돌았고, "1100개 통과" 는 사실상 글자검사만의 통과였다.
///   8/28 P0 4건이 그 1100개를 뚫고 나온 진짜 이유가 이것이다.
///
/// 봉합: 같은 <c>return</c> 이라도 <b>어디서 도느냐</b>로 판정을 가른다.
///   · 개발 PC (DB 없음)  → 종전대로 건너뛴다. 로컬 개발을 깨뜨리지 않는다.
///   · CI (DB 반드시 있음) → 건너뛰는 것 자체가 실패다.
///
/// 🔴 <c>return</c> 을 무조건 <c>Assert</c> 로 바꾸면 안 된다 — DB 없는 로컬이 전부 깨진다.
///    가를 것은 "건너뛰느냐" 가 아니라 "건너뛰어도 되는 자리냐" 다.
/// </summary>
internal static class DbGateEnvironment
{
    /// <summary>
    /// <b>DB 가 반드시 있어야 하는 자리인가.</b>
    ///
    /// 🔴 <c>CI</c> 환경변수로 판정하면 안 된다 — GitHub Actions 는 <b>모든 잡</b>에 <c>CI=true</c> 를 준다.
    ///   그러면 DB 를 안 띄우는 <c>build</c> 잡까지 DB 를 요구해 애먼 곳이 빨간불이 된다
    ///   (실제로 이 봉합의 1차 시도가 그렇게 깨졌다).
    ///
    /// ⇒ 판정 기준은 <b>"DB 를 주기로 한 잡인가"</b> 다. 그 약속이 <c>HITPAN_REQUIRE_DB</c> 이고,
    ///   <c>db-gate</c> 잡만 이 값을 주입한다. 약속한 잡에서 못 붙으면 그것은 실패다.
    /// </summary>
    public static bool IsCi =>
        IsTruthy(Environment.GetEnvironmentVariable("HITPAN_REQUIRE_DB"));

    /// <summary>
    /// DB 가 없어 게이트를 건너뛰려 할 때 호출한다.
    /// 로컬이면 사유를 찍고 <c>true</c>(건너뛰어도 좋다)를 준다.
    /// CI 면 <b>던진다</b> — 초록불이 될 기회를 주지 않는다.
    /// </summary>
    /// <param name="gateName">어느 게이트가 안 돌았는지 로그에 남긴다.</param>
    public static bool SkipOrFail(string gateName)
    {
        if (IsCi)
        {
            throw new Xunit.Sdk.XunitException(
                $"[게이트 미실행] {gateName} — CI 에서 MariaDB 에 붙지 못했다.\n"
              + "  CI 는 DB 가 반드시 있어야 한다. 건너뛴 게이트는 통과가 아니다.\n"
              + "  · 서비스 컨테이너(mariadb)가 떴는지\n"
              + "  · HITPAN_DB_HOST/PORT/USER/PASS · HITPAN_MYSQL 이 주입됐는지\n"
              + "  확인하라. (20260828 작14 W1 — 게이트 101곳이 조용히 SKIP 되던 사고 봉합)");
        }

        Console.Error.WriteLine(
            $"[SKIP] {gateName} — MariaDB 없음. 이 게이트는 안 돌았다. 초록불을 검증으로 읽지 마라.");
        return true;
    }

    private static bool IsTruthy(string? v) =>
        !string.IsNullOrWhiteSpace(v)
        && !v.Equals("false", StringComparison.OrdinalIgnoreCase)
        && !v.Equals("0", StringComparison.Ordinal);
}
