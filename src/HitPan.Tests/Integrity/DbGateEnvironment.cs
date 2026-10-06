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

    /// <summary>
    /// 🔴 <b>DB 없음을 「선언」했는가</b>를 묻는 환경변수 이름 — 20261007작10 §2-7 교정④ ([4] 리뷰서 §3-④).
    ///
    /// <para><b>사고</b>: 작10 게이트를 DB 없이 돌리면 <c>Failed 0 / Passed 10 / Skipped 0 / 0.97초</c>,
    /// DB 를 물리면 <b>같은 글자</b>에 50초였다(2026-10-07 PM 실측). ⇒ 판별 수단이 <b>소요시간 하나</b>뿐이고,
    /// <c>[SKIP]</c> 는 stderr 로만 나가 <c>dotnet test</c> 기본 출력에 <b>안 보인다</b>.</para>
    ///
    /// <para><b>왜 「Skipped 로 보이게」가 아닌가</b>: xunit 2.9.3 에는 동적 skip 이 없다.
    /// v2 의 <c>$XunitDynamicSkip$</c> 토큰은 이 러너에서 <b>Failed 로 집계된다</b>(실측으로 확인 —
    /// 글자로 믿지 않았다). 그래서 <b>선언 없으면 빨간불</b>로 간다.</para>
    /// </summary>
    public const string SkipDeclareVar = "HITPAN_GATE_SKIP_OK";

    /// <summary>건너뛰기 판정 세 갈래.</summary>
    internal enum Verdict
    {
        /// <summary>DB 를 주기로 한 잡(CI db-gate) — 못 붙으면 실패.</summary>
        FailCi,
        /// <summary>로컬 + 사람이 선언 — 건너뛴다.</summary>
        Skip,
        /// <summary>로컬 + 선언 없음 — 🔴 실패. 조용한 초록을 내지 않는다.</summary>
        FailUndeclared,
    }

    /// <summary>순수 판정 — 환경변수 두 값만 보고 가른다(게이트 G-10 이 이 함수를 직접 문다).</summary>
    internal static Verdict Decide(string? requireDb, string? skipOk) =>
        IsTruthy(requireDb) ? Verdict.FailCi
        : IsTruthy(skipOk) ? Verdict.Skip
        : Verdict.FailUndeclared;

    /// <summary>선언 없는 건너뛰기에 붙는 사유 — 다음 사람이 이 글만 읽고 복구할 수 있어야 한다.</summary>
    internal static string UndeclaredMessage(string gateName) =>
        $"[게이트 미실행 · 선언 없음] {gateName} — MariaDB 에 붙지 못했다.\n"
      + "  이 게이트는 DB 없이 「통과」로 집계되면 안 된다(20261007작10 교정④ · [4] 리뷰서 §3-④).\n"
      + "  · DB 를 물려 실측하려면: HITPAN_BO_GATE_DB=<시험DB 이름> 과 HITPAN_DB_HOST/USER/PASS 를 주고 다시 돌려라.\n"
      + $"  · DB 없이 넘기려면 {SkipDeclareVar}=1 을 **명시 선언**하라. 그때의 초록은 검증이 아니다.\n"
      + "  · CI db-gate 잡(HITPAN_REQUIRE_DB)에서는 선언으로도 못 넘긴다.";

    /// <summary>
    /// <see cref="SkipOrFail"/> 의 <b>엄격판</b> — 선언 없는 건너뛰기를 <b>실패</b>로 만든다.
    /// <para>🔴 종전 <see cref="SkipOrFail"/> 호출 101곳은 <b>한 줄도 건드리지 않았다</b>(헌법 #1).
    /// 그 전환은 2차수 과녁이다 — 지금 한꺼번에 바꾸면 DB 없는 로컬 전체 회귀가 전부 빨간불이 된다.</para>
    /// </summary>
    public static bool SkipOrFailStrict(string gateName)
    {
        switch (Decide(Environment.GetEnvironmentVariable("HITPAN_REQUIRE_DB"),
                       Environment.GetEnvironmentVariable(SkipDeclareVar)))
        {
            case Verdict.FailCi:
                return SkipOrFail(gateName);   // CI 문구·동작은 종전 그대로(던진다)

            case Verdict.FailUndeclared:
                throw new Xunit.Sdk.XunitException(UndeclaredMessage(gateName));

            default:
                var line = $"[SKIP] {gateName} — MariaDB 없음({SkipDeclareVar} 선언됨). "
                         + "이 게이트는 안 돌았다. 초록불을 검증으로 읽지 마라.";
                Console.Out.WriteLine(line);
                Console.Error.WriteLine(line);
                return true;
        }
    }

    private static bool IsTruthy(string? v) =>
        !string.IsNullOrWhiteSpace(v)
        && !v.Equals("false", StringComparison.OrdinalIgnoreCase)
        && !v.Equals("0", StringComparison.Ordinal);
}
