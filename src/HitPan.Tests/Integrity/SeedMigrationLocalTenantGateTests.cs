using System;
using System.IO;
using System.Linq;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20260827작5 — <b>시드 마이그가 고객 PC 에서 0건으로 조용히 지나가는 것</b>을 막는 게이트.
///
/// <para>
/// 🔴 <b>무엇을 겪고서</b> — 1.3.26 사장님 실측에서 <b>수금·지급이 409</b> 로 반려됐다.
/// 409 는 <c>GlobalExceptionMiddleware</c> 가 <b>FK 1452</b>(부모 없음)를 바꾼 것이고,
/// 부모는 <c>accounts</c> 였다. 즉 DB-111 이 심었어야 할 계정과목이 <b>고객 PC 에 없었다.</b>
/// </para>
///
/// <para>
/// 🔴 <b>왜 없었나</b> — DB-111(과 그 원본인 DB-32)이 <c>FROM tenants</c> 를 조인한다.
/// 그런데 <b>ERP 로컬 DB 의 <c>tenants</c> 는 빈 표다</b> — 전 소스에 <c>INSERT INTO tenants</c>
/// 가 0건이고, 로컬은 테넌트 상태를 <c>local_subscription</c> 에 쓴다.
/// ⇒ 조인 0행 ⇒ INSERT 0건 ⇒ <b>마이그는 "성공"으로 기록되고 아무 일도 안 했다.</b>
/// </para>
///
/// <para>
/// 🔴 <b>왜 아무도 못 잡았나</b> — 조용히 실패했기 때문이다. 마이그 러너는 SQL 이 예외 없이
/// 끝나면 성공으로 적는다. <b>0건 INSERT 는 예외가 아니다.</b> 그리고 PM 의 시험은
/// <c>hitpan_e2e</c> 에 <b>tenants 행을 손수 넣고</b> 돌려서 24계정이 나왔다 —
/// <b>고객 PC 에는 그 행이 없다는 전제를 안 봤다.</b> 가짜 전제로 만든 초록불이다.
/// </para>
///
/// <para>
/// ⚠️ <b>이 게이트가 하는 일</b> — 시드성 마이그(계정과목 등)가 <c>tenants</c> <b>에만</b>
/// 의존하지 않는지 <b>글자로</b> 본다. 글자검사는 원래 약한 수단이지만, 여기서는
/// <b>"어느 표를 테넌트 출처로 삼았나"</b> 라는 <b>구조적 선택</b>을 재는 것이라 유효하다.
/// 동작 검증은 <see cref="MoneyFlowJournalGateTests"/> 가 실 DB 로 따로 한다.
/// </para>
/// </summary>
public sealed class SeedMigrationLocalTenantGateTests
{
    /// <summary>
    /// 🔴 G-SM1 — <b>계정과목 시드 마이그는 로컬에 행이 있는 표에서 테넌트를 찾아야 한다.</b>
    /// <c>tenants</c> 만 보면 고객 PC 에서 0건이 된다.
    /// </summary>
    [Fact]
    public void GSM1_계정과목_시드는_tenants_에만_의존하지_않는다()
    {
        var dir = MigrationsDir();

        // 계정과목을 심는 마이그를 **번호순**으로 모은다 (INSERT ... INTO accounts)
        var seeds = Directory.GetFiles(dir, "DB-*.sql")
            .Select(p => (Num: Num(p), Path: p, Text: File.ReadAllText(p)))
            .Where(f => f.Text.Contains("INTO accounts"))
            .OrderBy(f => f.Num)
            .ToList();

        Assert.NotEmpty(seeds);   // 대조군 — 하나도 없으면 이 게이트가 아무것도 안 보는 것이다

        // 🔴 **마지막(=가장 최근) 시드만 본다.**
        //   "아무거나 하나 로컬 표를 보면 통과" 로 짜면, 옛 DB-32 가 남아 있는 한
        //   새 마이그를 통째로 망가뜨려도 초록불이 된다 — 실제로 그렇게 짰다가
        //   봉합을 되돌렸는데도 통과해서 **가짜였음이 드러났다**(누적 20번째).
        //   고객 DB 에 마지막으로 도착하는 건 가장 최근 시드다. 그것이 건강해야 한다.
        var latest = seeds[^1];

        // 🔴 **`tenant_id 를 뽑아오는 자리`** 만 본다.
        //   그냥 "FROM accounts 가 파일에 있나" 로 재면, 맨 아래 멱등 가드
        //   (`NOT EXISTS (SELECT 1 FROM accounts a ...)`) 가 걸려 **봉합을 통째로
        //   되돌려도 초록불**이 된다 — 실제로 그렇게 짰다가 반증에서 드러났다.
        //   같은 낱말이 **다른 목적**으로 파일 안에 산다(가짜 게이트 20번째 자리).
        var tenantSourcePattern = new System.Text.RegularExpressions.Regex(
            @"SELECT\s+tenant_id\s+FROM\s+(local_subscription|users|accounts)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        Assert.True(
            tenantSourcePattern.IsMatch(latest.Text),
            $"가장 최근 계정과목 시드({Path.GetFileName(latest.Path)})가 `tenants` 에만 의존한다 — " +
            "고객 PC 의 tenants 는 **빈 표**라 0건으로 조용히 지나간다(1.3.26 수금·지급 409 의 원인). " +
            "`SELECT tenant_id FROM local_subscription|users|accounts` 로 테넌트를 잡아라.");
    }

    /// <summary>
    /// 🔴 G-SM2 — <b>기표에 쓰는 계정이 봉합 마이그에 전부 들어 있다.</b>
    /// 하나라도 빠지면 그 업무만 FK 1452(=화면엔 409)로 죽는다.
    /// </summary>
    [Fact]
    public void GSM2_기표계정_전부가_봉합마이그에_있다()
    {
        var sql = File.ReadAllText(Path.Combine(
            MigrationsDir(), "DB-112_chart_of_accounts_local_tenant_fix.sql"));

        // AutoJournalHelper 가 실제로 쓰는 코드 전량
        string[] used =
        {
            "10100", "10300", "10800", "14600", "16900", "17600",   // 자산
            "23200", "25300", "25400", "25500",                     // 부채
            "40100",                                                 // 수익
            "50100",                                                 // 매출원가
            "80100", "81100", "81200", "81300", "81400", "82500", "84100", // 비용
        };

        foreach (var code in used)
        {
            Assert.True(sql.Contains($"'{code}'"),
                $"계정 {code} 이 DB-112 에 없다 — 이 계정을 쓰는 기표가 FK 1452(화면 409)로 죽는다.");
        }
    }

    /// <summary>
    /// 🔴 G-SM3 — <b>대조군.</b> 있지도 않은 계정을 넣으면 G-SM2 가 FAIL 해야 한다.
    /// 이게 없으면 "Contains 가 늘 참"인 엉터리 검사도 통과한다.
    /// </summary>
    [Fact]
    public void GSM3_대조군_없는계정은_검출된다()
    {
        var sql = File.ReadAllText(Path.Combine(
            MigrationsDir(), "DB-112_chart_of_accounts_local_tenant_fix.sql"));

        Assert.False(sql.Contains("'99999'"),
            "존재하지 않아야 할 계정이 잡혔다 — 이 검사가 무의미하다는 뜻이다.");
    }

    /// <summary>DB-NN_*.sql 파일명에서 번호 NN 을 뽑는다(정렬용).</summary>
    private static int Num(string path)
    {
        var name = Path.GetFileName(path);
        var m = System.Text.RegularExpressions.Regex.Match(name, @"^DB-(\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : -1;
    }

    private static string MigrationsDir()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !Directory.Exists(Path.Combine(dir, "src")); i++)
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        return Path.Combine(dir, "src", "HitPan.API", "Migrations", "SQL");
    }
}
