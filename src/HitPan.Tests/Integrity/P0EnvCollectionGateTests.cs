using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>20261019작B2 절 A — 게이트 신뢰 (V-4 · 구조 봉합) · G-P0-A-1</b>
///
/// <para><b>[사고의 씨앗]</b> <c>HITPAN_BO_JWT_SECRET</c> 는 <b>프로세스 전역</b> 환경변수다.
/// 그 값을 세우고 지우는 시험 클래스가 <b>11개</b>인데 xUnit 은 클래스를 <b>병렬로</b> 돌린다.
/// 한쪽이 값을 지우는 사이 다른 쪽이 읽으면, 읽은 쪽은 「값이 없다」로 판정한다 —
/// <b>아무도 안 건드린 코드가 빨강</b>이 되거나, 더 나쁘게는 <b>안 재고 초록</b>이 된다.</para>
///
/// <para>🔴 <b>「간헐 빨강을 봉합했다」가 아니다.</b> 5회 반복 36/36 미재현 · CI 797건 미재현이다
/// (작업지시서 §2-3). 이 봉합은 <b>잠재 제거</b>다 — 아직 안 터진 길을 닫는다.</para>
///
/// <para>🔴 <b>이 게이트는 소스 글자를 grep 하지 않는다.</b> 리플렉션으로 <b>실물 속성</b>을 읽는다 —
/// xUnit 이 보는 것과 같은 자리다. 글자 grep 은 <c>[Collection(XxxCollection.Name)]</c> 처럼
/// <b>상수를 참조</b>한 줄을 놓친다(PM 자기오류 13-a 가 정확히 그 사고였다).
/// 선례: <c>ErpMigrationApplyGateTests.G_W13_6</c>.</para>
///
/// <para>🔴 <b>Collection 을 왜 새로 만들지 않고 <see cref="BoSignupTenantKeyGateCollection"/> 을 쓰나</b> —
/// 작업지시서 §9-2 는 「11개 전부 현재 <c>[Collection]</c> 0건」이라 적었으나 <b>실측은 다르다</b>:
/// <c>BackofficeRoleScopeGateTests</c>(:21) 와 <c>BackofficeRoleDowngradeGateTests</c>(:54) 는
/// <b>이미</b> <c>BoSignupTenantKeyGateCollection</c> 에 들어 있다. xUnit 은 클래스당
/// <c>[Collection]</c> 이 <b>하나뿐</b>이라(<c>AllowMultiple=false</c>) 새 Collection 으로 묶으려면
/// 그 두 줄을 <b>지워야</b> 한다 ⇒ §4-3(기존 줄 삭제 0 · 원칙 #1) 정면 충돌.
/// §9-2 가 <c>BackofficeSmtpHealthzParityGateTests</c> 를 목록에서 뺀 것과 <b>같은 이유</b>로,
/// 이미 선 Collection(<c>DisableParallelization = true</c> 보유)에 나머지를 <b>더한다</b>.
/// 삭제 0 · 새 타입 0 · 무나는 그대로 성립한다.</para>
/// </summary>
public class P0EnvCollectionGateTests
{
    /// <summary>
    /// 🔴 <c>HITPAN_BO_JWT_SECRET</c> 전역을 세우거나 지우는 시험 클래스 — 작업지시서 §9-2 PM 실측 11개.
    /// <para><b>직접 사용 3</b> + <b>하네스(<see cref="BackofficeRoleGateHarness"/>) 경유 7</b>
    /// + <b>같은 자원(격리 DB·전역) 경유 1</b>.</para>
    /// <para>🟡 <b>PM 재실측 전제오류 ②</b> — §9-2 는 「하네스 경유 8」로 적었으나 실측 <b>7</b>이다.
    /// <c>BackofficePromotionPricingDbGateTests</c>(:45)는 하네스를 <b>주석에서만</b> 언급한다.
    /// 🟢 그래도 목록에 남긴다 — <b>같은 격리 DB·전역을 만지므로</b> 직렬화 대상이다.
    /// 근거가 「하네스 경유」가 아니라 「같은 자원 경유」로 바뀐 것뿐이고, 수는 11 그대로다.</para>
    /// <para>🔴 <c>typeof</c> 로 적는다 — 클래스가 지워지거나 이름이 바뀌면 <b>빌드가 깨진다</b>.
    /// 문자열로 적으면 이름이 바뀐 날 게이트가 <b>조용히 0건을 세고 초록</b>이 된다.</para>
    /// <para>⬛ <b>제외 2건</b>(§9-2) — <c>BackofficeSmtpHealthzParityGateTests</c> = env 미사용(주석만) ·
    /// 이미 자기 Collection 보유 / <c>BackofficeRoleGateHarness</c> = <c>[Fact]</c> 0 · <c>internal</c> 헬퍼.</para>
    /// </summary>
    private static readonly Type[] EnvUsingGateClasses =
    {
        // ── env 직접 사용 3 ──
        typeof(BackofficeAnonymousAdminApiGate),          // Fact/Theory 12
        typeof(BackofficeGuardStartupGate),               // Fact/Theory  3
        typeof(P0DeliveryPathGateTests),                  // Fact/Theory 13
        // ── 하네스 경유 8 ──
        typeof(BackofficeRoleScopeGateTests),             // Fact/Theory  9 (이미 보유)
        typeof(CsInboundGateTests),                       // Fact/Theory  9
        typeof(CsKbGateTests),                            // Fact/Theory  9
        typeof(BackofficeS2OwnerReportGateTests),         // Fact/Theory  5
        typeof(CsStageGateTests),                         // Fact/Theory  5
        typeof(BackofficePromotionPricingDbGateTests),    // Fact/Theory  4 (하네스 아님 · 같은 자원)
        typeof(BackofficeRoleDowngradeGateTests),         // Fact/Theory  4 (이미 보유)
        typeof(CsAdminGateTests),                         // Fact/Theory  3
    };

    // ══════════════════════════════════════════════════════════════
    // G-P0-A-1 — 🔴 전역 env 를 쓰는 시험 클래스 11개가 한 Collection 에서 직렬화된다
    //
    //   [무나]  11개 전부가 **같은 Collection** 이고, 그 CollectionDefinition 에
    //           **DisableParallelization = true** 가 있다.
    //   [음성 대조군 1] 한 클래스를 Collection 에서 빼면 FAIL
    //   [음성 대조군 2] DisableParallelization 을 지우면 FAIL
    //   [판정 방식] 🔴 글자 grep 금지 — 리플렉션으로 실물 속성을 읽는다.
    //              🔴 [Fact]/[Theory] 가 1개 이상인 클래스만 센다(헬퍼·주석 제외).
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-P0-A-1 🔴 HITPAN_BO_JWT_SECRET 전역을 쓰는 시험 클래스 11개가 한 Collection 에서 직렬화된다 — 서로의 env 를 지워 거짓 빨강·안 재고 초록이 되는 길을 닫는다")]
    public void G_P0_A_1_전역env_클래스들이_직렬화된다()
    {
        // 🔴 ① 목록 자체를 먼저 검사한다 — 「시험이 아닌 것」을 세고 초록이 되면 게이트가 가짜다.
        //    헬퍼(internal · [Fact] 0)나 주석만 있는 클래스가 섞이면 여기서 터진다.
        var notTestClasses = new List<string>();
        foreach (var t in EnvUsingGateClasses)
        {
            var facts = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Count(m => m.GetCustomAttributesData().Any(a =>
                       a.AttributeType == typeof(FactAttribute)
                    || a.AttributeType == typeof(TheoryAttribute)
                    || typeof(FactAttribute).IsAssignableFrom(a.AttributeType)));
            if (facts == 0) notTestClasses.Add(t.Name);
        }

        Assert.True(notTestClasses.Count == 0,
            "🔴 [Fact]/[Theory] 가 0건인 클래스가 목록에 섞였다 — 직렬화해도 효과 0이다: "
          + string.Join(", ", notTestClasses)
          + "\n  §9-2 가 BackofficeRoleGateHarness 를 목록에서 뺀 이유가 이것이다.");

        // 🔴 ② 목록 수가 §9-2 PM 실측과 같은지 본다 — 누가 조용히 지우면 여기서 터진다.
        Assert.Equal(11, EnvUsingGateClasses.Length);

        // 🔴 ③ [Collection] 을 **실물 속성**에서 읽는다.
        //    CollectionAttribute 는 xunit 2.9.3 에서 이름을 속성으로 내주지 않는다 ⇒
        //    생성자 인수를 메타데이터에서 읽는다. 상수 참조(XxxCollection.Name)도 여기서는
        //    **이미 값으로 구워져** 있으므로 문자열 리터럴만 찾던 grep 의 맹점이 없다.
        var missing = new List<string>();
        var wrong = new List<string>();

        foreach (var t in EnvUsingGateClasses)
        {
            var data = t.GetCustomAttributesData()
                .FirstOrDefault(a => a.AttributeType == typeof(CollectionAttribute));
            if (data is null || data.ConstructorArguments.Count == 0) { missing.Add(t.Name); continue; }

            var name = data.ConstructorArguments[0].Value as string;
            if (!string.Equals(name, BoSignupTenantKeyGateCollection.Name, StringComparison.Ordinal))
                wrong.Add($"{t.Name} → \"{name}\"");
        }

        Assert.True(missing.Count == 0,
            "🔴 HITPAN_BO_JWT_SECRET 전역을 쓰는데 [Collection] 이 없는 시험 클래스: "
          + string.Join(", ", missing)
          + $"\n  `[Collection({nameof(BoSignupTenantKeyGateCollection)}.Name)]` 을 붙여라. "
          + "안 붙이면 xUnit 이 병렬로 돌려 서로의 전역 환경변수를 지운다 — "
          + "한쪽이 지우는 사이 읽은 쪽은 「값이 없다」로 판정하고, "
          + "그 판정은 **아무도 안 건드린 코드를 빨강**으로 만들거나 **안 재고 초록**으로 만든다.");

        Assert.True(wrong.Count == 0,
            "🔴 다른 Collection 에 들어간 클래스가 있다(직렬화가 깨진다 — 이름이 다르면 xUnit 은 남남으로 본다): "
          + string.Join(", ", wrong));

        // 🔴 ④ 그 Collection 이 **실제로 병렬을 끄는지**까지 본다 — 이름만 같고 꺼져 있으면 봉합이 아니다.
        //    이게 종전 게이트에 없었다(§9-2 음성 대조군 2 신설 근거).
        var def = typeof(BoSignupTenantKeyGateCollection)
            .GetCustomAttributes(typeof(CollectionDefinitionAttribute), inherit: false)
            .Cast<CollectionDefinitionAttribute>().FirstOrDefault();

        Assert.NotNull(def);
        Assert.True(def!.DisableParallelization,
            "🔴 Collection 정의에 DisableParallelization 가 꺼져 있다 — "
          + "11개가 같은 이름을 달고도 병렬로 돈다. 이름만 같은 봉합은 봉합이 아니다.");
    }
}
