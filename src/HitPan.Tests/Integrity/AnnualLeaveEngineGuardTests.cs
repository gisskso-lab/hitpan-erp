using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 연차 엔진 게이트. 작(2026-08-13) 그룹웨어 단계5.
/// </summary>
/// <remarks>
/// 🔴 이 시험들이 지키는 것은 <b>사장님이 정한 두 원칙</b>이다.
/// <list type="bullet">
/// <item>반자동 — <i>"히트판은 100%자동화는 없어. 무조건 반자동이야"</i>(2026-08-12)</item>
/// <item>법정값은 설정 테이블 — <i>"법이 언제 어떻게 바뀔지 몰라. 계속 모니터링 해야 해"</i></item>
/// </list>
/// </remarks>
public sealed class AnnualLeaveEngineGuardTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.True(dir is not null && Directory.Exists(Path.Combine(dir, "src")),
            "레포 루트를 찾아야 한다");
        return dir!;
    }

    private static string ReadSource(params string[] parts)
    {
        var path = Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"파일이 있어야 한다: {path}");
        return File.ReadAllText(path);
    }

    /// <summary>주석을 걷어낸 코드만. 주석의 설명 문장을 코드로 오인하지 않기 위해.</summary>
    private static string CodeOnly(string source) => string.Join('\n',
        source.Split('\n').Where(l =>
        {
            var t = l.TrimStart();
            return !t.StartsWith("//", StringComparison.Ordinal)
                && !t.StartsWith("///", StringComparison.Ordinal)
                && !t.StartsWith("*", StringComparison.Ordinal);
        }));

    // ───────────────────────────────────────────────────────────────
    // 🔴 법정값을 코드에 넣지 않는다
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>연차 일수·기준 시간을 코드에 쓰지 않는다.</b>
    /// 설계도 §0: <i>"법정값은 전부 설정 테이블. 하드코딩 금지, 상수로 빼는 것도 금지
    /// (재배포가 필요하면 실패다)"</i>
    /// 지금 연차 15일이 화면 코드에 박혀 있어 못 바꾸는 것이 바로 그 실패다.
    /// </summary>
    [Fact]
    public void 연차_엔진에_법정_숫자가_박혀_있지_않다()
    {
        var code = CodeOnly(ReadSource("src", "HitPan.Application", "Services", "AnnualLeaveService.cs"));

        // 기준값은 전부 표에서 읽는다.
        Assert.Contains("labor_policy_settings", code);
        Assert.Contains("annual_leave_base_days", code);
        Assert.Contains("small_business_threshold", code);
        Assert.Contains("short_time_weekly_hours", code);

        // 🔴 법정 숫자를 리터럴로 쓰지 않는다. 특히 폴백 기본값이 위험하다 —
        //    `?? 15m` 같은 걸 넣으면 그게 곧 하드코딩이고, 표를 고쳐도 안 바뀐다.
        foreach (var forbidden in new[] { "15m", "25m", "5m", "= 15", "= 25" })
        {
            Assert.DoesNotContain(forbidden, code);
        }

        // 기준값이 없으면 0 — 조용히 그럴듯한 값이 나오는 것보다 안 나오는 게 낫다.
        Assert.Contains("out var v) ? v : 0m", code);
    }

    /// <summary>
    /// 🔴 <b>값마다 시행일이 있어야 한다</b>(설계도 §0 지침 ②).
    /// 법은 시행일이 있고 <b>과거분은 옛 값으로 계산</b>해야 한다.
    /// 단일 값만 저장하면 작년 연차를 올해 기준으로 다시 계산하게 된다.
    /// </summary>
    [Fact]
    public void 기준값은_시행일별로_따로_산다()
    {
        var mig = ReadSource("src", "HitPan.API", "Migrations", "SQL", "DB-96_labor_policy_settings.sql");

        Assert.Contains("`effective_from` date", mig);
        // 같은 열쇠라도 시행일이 다르면 따로 산다.
        Assert.Contains("uk_policy_tenant_key_from", mig);
        Assert.Contains("`tenant_id`, `policy_key`, `effective_from`", mig);

        var svc = CodeOnly(ReadSource("src", "HitPan.Application", "Services", "AnnualLeaveService.cs"));

        // 조회가 시점을 받아 그때 유효한 값을 고른다.
        Assert.Contains("effective_from <= @AsOf", svc);
        Assert.Contains("MAX(q.effective_from)", svc);

        // 🔴 계산은 "지금" 이 아니라 "그 해" 기준값을 쓴다.
        Assert.Contains("new DateTime(grantYear, 12, 31)", svc);
    }

    /// <summary>
    /// 🔴 <b>기준값을 고칠 때 옛 값을 덮지 않는다.</b>
    /// 덮으면 과거 계산을 설명할 수 없다.
    /// </summary>
    [Fact]
    public void 기준값_수정은_새_행을_추가한다()
    {
        var svc = CodeOnly(ReadSource("src", "HitPan.Application", "Services", "AnnualLeaveService.cs"));

        var idx = svc.IndexOf("SavePolicyAsync", StringComparison.Ordinal);
        Assert.True(idx > 0, "SavePolicyAsync 가 있어야 한다");

        var block = svc.Substring(idx, Math.Min(2200, svc.Length - idx));

        // INSERT 다 — UPDATE 로 덮지 않는다(같은 시행일일 때만 ON DUPLICATE 로 갱신).
        Assert.Contains("INSERT INTO labor_policy_settings", block);
        Assert.Contains("ON DUPLICATE KEY UPDATE", block);

        // 왜 고쳤는지 남긴다.
        Assert.Contains("updated_reason", block);
    }

    // ───────────────────────────────────────────────────────────────
    // 🔴 반자동 3단 — 제안 → 수정 → 확정
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>제안이 곧 반영이 아니다.</b>
    /// 자동 계산 결과가 그대로 잔여에 들어가면 그건 100% 자동이고, 사장님이 금지한 것이다.
    /// </summary>
    [Fact]
    public void 제안은_저장하지_않고_확정해야_반영된다()
    {
        var svc = CodeOnly(ReadSource("src", "HitPan.Application", "Services", "AnnualLeaveService.cs"));

        var suggestIdx = svc.IndexOf("SuggestAsync", StringComparison.Ordinal);
        var confirmIdx = svc.IndexOf("ConfirmAsync", StringComparison.Ordinal);
        Assert.True(suggestIdx > 0 && confirmIdx > suggestIdx, "제안·확정 메서드가 있어야 한다");

        // 🔴 제안 구간에는 INSERT·UPDATE 가 없어야 한다 — 보여주기만 한다.
        var suggestBlock = svc.Substring(suggestIdx, confirmIdx - suggestIdx);
        Assert.DoesNotContain("INSERT INTO annual_leave_grants", suggestBlock);
        Assert.DoesNotContain("UPDATE employees", suggestBlock);

        // 확정에서만 이력과 잔여가 함께 움직인다.
        // ⚠️ 구간을 3500자로 잡았다가 UPDATE 가 그 밖에 있어 헛되이 실패했다.
        //    확정 메서드는 이력 INSERT·잔여 UPDATE·트랜잭션까지라 길다. 넉넉히 본다.
        var confirmBlock = svc.Substring(confirmIdx, Math.Min(5000, svc.Length - confirmIdx));
        Assert.Contains("INSERT INTO annual_leave_grants", confirmBlock);
        Assert.Contains("UPDATE employees", confirmBlock);

        // 🔴 같은 트랜잭션 — 따로 하면 이력만 남고 잔여가 안 늘거나 그 반대가 된다.
        Assert.Contains("BeginTransaction", confirmBlock);
        Assert.Contains("tx.Commit()", confirmBlock);
        Assert.Contains("tx.Rollback()", confirmBlock);
    }

    /// <summary>
    /// 🔴 <b>제안과 다르게 정했으면 이유를 남겨야 한다.</b>
    /// 사장님 반자동 원칙: <i>수정 가능하면 수정 이력 필수(누가·언제·뭘·왜)</i>.
    /// 이력이 없으면 "내 연차가 왜 이거냐" 에 답할 수 없고, 노동청 다툼에서 근거가 없다.
    /// </summary>
    [Fact]
    public void 제안과_다르게_정하면_사유를_받는다()
    {
        var svc = CodeOnly(ReadSource("src", "HitPan.Application", "Services", "AnnualLeaveService.cs"));

        Assert.Contains("isAdjusted", svc);
        Assert.Contains("AdjustReason", svc);
        Assert.Contains("사유를 남겨야 확정할 수 있습니다", svc);

        // 이력 표에 제안값과 확정값이 <b>둘 다</b> 남는다 — 하나만 남기면 무엇이 바뀐 건지 모른다.
        var mig = ReadSource("src", "HitPan.API", "Migrations", "SQL", "DB-97_annual_leave_grants.sql");
        Assert.Contains("`suggested_days`", mig);
        Assert.Contains("`granted_days`", mig);
        Assert.Contains("`adjust_reason`", mig);
        Assert.Contains("`confirmed_by`", mig);

        // 화면도 먼저 막는다.
        var page = ReadSource("src", "HitPan.Web", "Pages", "HR", "AnnualLeaveGrantPage.razor");
        Assert.Contains("이유를 적어주세요", page);
    }

    /// <summary>
    /// 🔴 <b>자동이 판단 못 하는 것을 감추지 않는다.</b>
    /// 감추면 관리자가 모르고 확정해 <b>법정 미달</b>이 될 수 있다.
    /// 다만 계산을 <b>멈추지도 않는다</b> — 멈추면 아무 값도 안 나와서 오히려 못 준다.
    /// </summary>
    [Fact]
    public void 자동이_판단_못하는_것은_경고로_넘긴다()
    {
        var svc = CodeOnly(ReadSource("src", "HitPan.Application", "Services", "AnnualLeaveService.cs"));

        Assert.Contains("Warnings.Add", svc);

        // 어떤 사정을 넘기는지 — 사람이 봐야 하는 자리들.
        Assert.Contains("주당 소정근로시간이 정해지지 않았습니다", svc);
        Assert.Contains("상시근로자수가 정해지지 않았습니다", svc);
        Assert.Contains("개근", svc);

        // 🔴 경고가 있다고 계산을 0 으로 만들지 않는다.
        //    모른다고 연차를 0 으로 주면 법정 미달이다.
        Assert.DoesNotContain("SuggestedDays = 0;\n            dto.Warnings", svc);

        // 화면이 경고를 그대로 보여준다.
        var page = ReadSource("src", "HitPan.Web", "Pages", "HR", "AnnualLeaveGrantPage.razor");
        Assert.Contains("context.Warnings", page);
    }

    /// <summary>
    /// 🔴 <b>계산 근거를 남긴다.</b>
    /// 숫자만 보여주면 관리자가 맞는지 판단할 수 없고,
    /// 법이 바뀐 뒤에 <b>과거 계산을 설명</b>할 수 없다(설계도 §0 지침 ③).
    /// </summary>
    [Fact]
    public void 계산_근거를_남기고_보여준다()
    {
        var svc = CodeOnly(ReadSource("src", "HitPan.Application", "Services", "AnnualLeaveService.cs"));
        Assert.Contains("CalcBasis", svc);

        var mig = ReadSource("src", "HitPan.API", "Migrations", "SQL", "DB-97_annual_leave_grants.sql");
        Assert.Contains("`calc_basis`", mig);

        var page = ReadSource("src", "HitPan.Web", "Pages", "HR", "AnnualLeaveGrantPage.razor");
        Assert.Contains("context.CalcBasis", page);

        // 제안값과 확정값을 나란히 보여준다 — 무엇을 고쳤는지 눈에 보여야 한다.
        Assert.Contains("context.SuggestedDays", page);
        Assert.Contains("context.EditDays", page);
    }

    // ───────────────────────────────────────────────────────────────
    // 격리 · 무결
    // ───────────────────────────────────────────────────────────────

    /// <summary>🔴 테넌트 격리(헌법 #2) — 연차 SQL 전부가 <c>tenant_id</c> 로 갈라야 한다.</summary>
    [Fact]
    public void 연차_SQL은_전부_테넌트로_가른다()
    {
        var svc = CodeOnly(ReadSource("src", "HitPan.Application", "Services", "AnnualLeaveService.cs"));

        foreach (var marker in new[]
                 {
                     "FROM employees", "FROM annual_leave_grants", "FROM labor_policy_settings",
                     "INSERT INTO annual_leave_grants", "UPDATE employees"
                 })
        {
            var idx = svc.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(idx > 0, $"{marker} 가 있어야 한다");

            var len = Math.Min(800, svc.Length - idx);
            Assert.Contains("@TenantId", svc.Substring(idx, len));
        }

        // 컨트롤러는 JWT 에서만 tenant 를 꺼낸다.
        var ctrl = ReadSource("src", "HitPan.API", "Controllers", "AnnualLeaveController.cs");
        Assert.Contains("HttpContext.Items[\"TenantId\"]", ctrl);
        Assert.DoesNotContain("string tenantId,", ctrl);
    }

    /// <summary>🔴 같은 해에 같은 연차를 두 번 주지 않는다.</summary>
    [Fact]
    public void 같은_해_연차를_두_번_주지_않는다()
    {
        var mig = ReadSource("src", "HitPan.API", "Migrations", "SQL", "DB-97_annual_leave_grants.sql");
        Assert.Contains("uk_grant_emp_year_type", mig);

        var svc = CodeOnly(ReadSource("src", "HitPan.Application", "Services", "AnnualLeaveService.cs"));
        Assert.Contains("이미 확정돼 있습니다", svc);
        Assert.Contains("status = 'confirmed'", svc);
    }

    /// <summary>🔴 마이그레이션과 출하 DDL 이 함께 간다(헌법 #36).</summary>
    [Fact]
    public void 연차_표가_출하_DDL에도_있다()
    {
        var ddl = ReadSource("installer", "hitpan_db_clean.sql");

        Assert.Contains("CREATE TABLE `labor_policy_settings`", ddl);
        Assert.Contains("CREATE TABLE `annual_leave_grants`", ddl);
        Assert.Contains("ENGINE=InnoDB", ddl);   // 헌법 #17

        foreach (var id in new[] { "DB-96", "DB-97" })
        {
            Assert.Contains($"('{id}','clean-ddl',1)", ddl);
        }
    }

    /// <summary>🔴 잔여를 덮어쓰지 않고 더한다 — 조정·이월 부여가 따로 들어온다.</summary>
    [Fact]
    public void 잔여는_덮어쓰지_않고_더한다()
    {
        var svc = CodeOnly(ReadSource("src", "HitPan.Application", "Services", "AnnualLeaveService.cs"));

        Assert.Contains("annual_leave_total = annual_leave_total + @Days", svc);
        Assert.DoesNotContain("annual_leave_total = @Days", svc);
    }
}
