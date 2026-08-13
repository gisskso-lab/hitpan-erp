using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 그룹웨어 단계4 토대 게이트. 작(2026-08-13).
/// </summary>
/// <remarks>
/// 직급·부서·고용형태·사업장설정은 <b>단계5~9 전부의 선행조건</b>이다.
/// 연차는 고용형태와 주당 근로시간으로 갈리고, 메신저 부서방은 부서 마스터가 있어야 생기고,
/// 급여·퇴직금은 사업장 조건을 본다. 이 토대가 무너지면 그 위가 전부 무너진다.
/// </remarks>
public sealed class GroupwareStage4GuardTests
{
    /// <remarks>
    /// ⚠️ <c>HitPan.sln</c> 은 레포 루트가 아니라 <c>src/</c> 안에 있다.
    /// 이 레포의 다른 게이트 시험들과 <b>같은 방식</b>(src 폴더 찾기)을 쓴다.
    /// </remarks>
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

    /// <summary>
    /// SQL 에 쓰인 <c>@파라미터</c> 마다 <b>Dapper 파라미터 객체에 짝이 있는지</b> 본다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>이 헬퍼가 이 파일에서 가장 중요하다.</b>
    ///
    /// 단계4 검증에서 P0 가 나왔다 — SQL 에 <c>@WeeklyHours</c> 가 있는데
    /// 파라미터 객체에 <c>WeeklyHours</c> 가 없어 <b>신규 등록 시 값이 조용히 NULL</b> 로 들어갔다.
    /// 그런데 <b>가드 15개가 전부 통과</b>했다. <c>Assert.Contains("@WeeklyHours", svc)</c> 로
    /// <b>문자열이 있는지만</b> 봤기 때문이다.
    ///
    /// 검증팀 지적 그대로다: <i>"문자열 존재 확인은 배선 확인이 아니다. 바꾸지 않으면 다섯 번째가 온다."</i>
    ///
    /// ⚠️ 예외도 안 났다. 연결문자열의 <c>AllowUserVariables=true</c> 때문에 MySqlConnector 가
    /// 바인딩 안 된 <c>@WeeklyHours</c> 를 MySQL 사용자변수(=NULL)로 재해석한다.
    /// 그 옵션이 증상을 가려 <b>영영 안 드러날 수 있었다</b>.
    /// </remarks>
    /// <param name="expectedAtLeast">이 파라미터가 쓰이는 SQL 문 수의 최소치(예: INSERT + UPDATE = 2).</param>
    private static void AssertDapperParametersBound(string source, string sqlParam, int expectedAtLeast)
    {
        Assert.StartsWith("@", sqlParam);
        var propName = sqlParam[1..];

        // SQL 안에서 몇 번 쓰였나(주석 줄은 뺀다).
        var code = string.Join('\n', source.Split('\n').Where(l =>
        {
            var t = l.TrimStart();
            return !t.StartsWith("//", StringComparison.Ordinal)
                && !t.StartsWith("///", StringComparison.Ordinal)
                && !t.StartsWith("--", StringComparison.Ordinal);
        }));

        var sqlUses = Regex.Matches(code, Regex.Escape(sqlParam) + @"\b").Count;
        Assert.True(sqlUses >= expectedAtLeast,
            $"{sqlParam} 가 SQL 에서 {expectedAtLeast}번 이상 쓰여야 한다(실제 {sqlUses}).");

        // 파라미터 객체에 같은 이름으로 값을 넘기는 자리가 그만큼 있어야 한다.
        //   `WeeklyHours = ...` 또는 축약형 `request.WeeklyHours`(Dapper 는 속성명을 쓴다).
        var bindings = Regex.Matches(code, @"\b" + Regex.Escape(propName) + @"\s*=").Count;

        Assert.True(bindings >= expectedAtLeast,
            $"🔴 {sqlParam} 가 SQL 에 {sqlUses}번 쓰였는데 파라미터 바인딩은 {bindings}곳뿐이다. "
            + "SQL 에만 있고 파라미터 객체에 없으면 값이 조용히 NULL 로 들어간다"
            + "(AllowUserVariables=true 라 예외도 안 난다). 단계4 P0-1 과 같은 병이다.");
    }

    // ───────────────────────────────────────────────────────────────
    // 부서 — 만들 수 있어야 한다
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>부서를 만들 방법이 있어야 한다.</b>
    /// 종전엔 <c>src/</c> 전체에 <c>INSERT INTO departments</c> 가 0건이라
    /// 부서가 0행이었고, 사원 부서 드롭다운 선택지가 0개였고, 사원 전원이 부서 없음이었다.
    /// 버그가 아니라 <b>만들 수 없어서 생긴 필연</b>이었다.
    /// </summary>
    [Fact]
    public void 부서를_만들고_고치고_지울_수_있다()
    {
        var svc = ReadSource("src", "HitPan.Application", "Services", "DepartmentService.cs");

        Assert.Contains("INSERT INTO departments", svc);
        Assert.Contains("UPDATE departments", svc);
        Assert.Contains("DELETE FROM departments", svc);

        // 컨트롤러가 실제로 열려 있어야 한다(서비스만 만들고 안 부르면 소용없다).
        var ctrl = ReadSource("src", "HitPan.API", "Controllers", "DepartmentController.cs");
        Assert.Contains("[HttpPost]", ctrl);
        Assert.Contains("[HttpPut(\"{id}\")]", ctrl);
        Assert.Contains("[HttpDelete(\"{id}\")]", ctrl);

        // 화면에도 추가·수정·삭제가 있어야 한다.
        var page = ReadSource("src", "HitPan.Web", "Pages", "HR", "HrDepartmentsPage.razor");
        Assert.Contains("부서 추가", page);
        Assert.Contains("DeptSvc.CreateAsync", page);
        Assert.Contains("DeptSvc.UpdateAsync", page);
        Assert.Contains("DeptSvc.DeleteAsync", page);
    }

    /// <summary>
    /// 🔴 <b>테넌트 격리</b>(헌법 #2) — 부서 SQL 전부가 <c>tenant_id</c> 로 갈라야 한다.
    /// 하나라도 빠지면 남의 회사 부서가 보이거나 지워진다.
    /// </summary>
    [Fact]
    public void 부서_SQL은_전부_테넌트로_가른다()
    {
        var svc = ReadSource("src", "HitPan.Application", "Services", "DepartmentService.cs");

        // ⚠️ 주석에도 같은 문구가 나온다("INSERT INTO departments 가 0건이었다").
        //    주석 줄을 걷어내고 <b>코드만</b> 본다 — 처음엔 IndexOf 로 잡았다가
        //    XML 주석의 설명 문장을 SQL 로 오인해 헛되이 실패했다.
        var codeOnly = string.Join('\n',
            svc.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)
                                    && !l.TrimStart().StartsWith("///", StringComparison.Ordinal)));

        foreach (var marker in new[]
                 {
                     "INSERT INTO departments", "UPDATE departments", "DELETE FROM departments"
                 })
        {
            var idx = codeOnly.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(idx > 0, $"{marker} 가 코드에 있어야 한다");

            // 해당 SQL 문 뒤쪽(WHERE 절이 오는 자리)에 @TenantId 가 있어야 한다.
            var len = Math.Min(700, codeOnly.Length - idx);
            Assert.Contains("@TenantId", codeOnly.Substring(idx, len));
        }

        // 컨트롤러도 JWT 에서만 tenant 를 꺼낸다 — 파라미터로 받으면 즉시 반려(헌법 #2).
        var ctrl = ReadSource("src", "HitPan.API", "Controllers", "DepartmentController.cs");
        Assert.Contains("HttpContext.Items[\"TenantId\"]", ctrl);
        Assert.DoesNotContain("string tenantId,", ctrl);
    }

    /// <summary>
    /// 🔴 <b>사원이 물린 부서를 지우면 안 된다.</b>
    /// 지우면 그 사원들의 <c>dept_id</c> 가 유령을 가리켜 부서칸이 빈칸이 되고,
    /// 어느 부서였는지 되돌릴 수 없다. 직급이 쓰는 원칙과 같다 — 사용 중이면 비활성.
    /// </summary>
    [Fact]
    public void 사원이_물린_부서는_지우지_않고_비활성으로_돌린다()
    {
        var svc = ReadSource("src", "HitPan.Application", "Services", "DepartmentService.cs");

        // 삭제 전에 사원 수와 하위 부서 수를 센다.
        //
        // ⚠️ 이 시험은 한 번 <b>훼손을 놓친 적이 있다</b> — 파라미터 이름을 @DeptId_BROKEN 으로
        //    바꿨는데도 통과했다. 조각(`parent_dept_id = @DeptId`)만 봐서 앞쪽 SQL 의 손상을
        //    못 본 것이다. 이제 <b>SQL 문자열 전문</b>을 정확히 대조한다.
        Assert.Contains(
            "\"SELECT COUNT(*) FROM employees WHERE tenant_id = @TenantId AND dept_id = @DeptId\"",
            svc);
        Assert.Contains(
            "\"SELECT COUNT(*) FROM departments WHERE tenant_id = @TenantId AND parent_dept_id = @DeptId\"",
            svc);

        // 비활성 전환도 전문으로 본다.
        Assert.Contains(
            "\"UPDATE departments SET is_active = 0, updated_at = NOW(6) WHERE tenant_id = @TenantId AND dept_id = @DeptId\"",
            svc);

        // 🔴 파라미터 이름이 SQL 과 어긋나면 Dapper 가 값을 못 넘겨 판정이 통째로 무너진다.
        //    실제로 그렇게 훼손했을 때 이 시험이 잡아야 한다.
        Assert.DoesNotContain("@DeptId_", svc);

        // 🔴 화면이 "삭제했습니다" 와 "사용 안 함으로 돌렸습니다" 를 갈라 말해야 한다.
        //    같은 말을 하면 목록에 그대로 남아 되는 척이 된다(단계3 P0 와 같은 병).
        var page = ReadSource("src", "HitPan.Web", "Pages", "HR", "HrDepartmentsPage.razor");
        Assert.Contains("result.Deleted", page);
        Assert.Contains("result.Deactivated", page);
    }

    /// <summary>
    /// 🔴 <b>부서 계층이 고리를 이루면 안 된다.</b>
    /// 자기 자신이나 자기 하위를 상위로 두면 조직도·부서방을 그릴 때 무한히 돈다.
    /// </summary>
    [Fact]
    public void 부서_계층은_고리를_만들지_않는다()
    {
        var svc = ReadSource("src", "HitPan.Application", "Services", "DepartmentService.cs");

        Assert.Contains("자기 자신을 상위 부서로 지정할 수 없습니다", svc);
        Assert.Contains("IsDescendantAsync", svc);
        Assert.Contains("하위 부서를 상위 부서로 지정할 수 없습니다", svc);

        // 이미 고리가 있는 데이터에서도 멎지 않아야 한다(방문 기억 + 깊이 제한).
        Assert.Contains("HashSet<string>", svc);
        Assert.Matches(new Regex(@"depth\s*<\s*\d+"), svc);
    }

    // ───────────────────────────────────────────────────────────────
    // 직급 — 마스터와 이어져야 한다
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>직급을 아무거나 칠 수 없어야 한다.</b>
    /// 자유 텍스트였던 탓에 12명 중 8명이 직급 없음이었다(NULL·공백·"0").
    /// </summary>
    [Fact]
    public void 사원_직급은_마스터에서_고른다()
    {
        var page = ReadSource("src", "HitPan.Web", "Pages", "Settings", "EmployeePage.razor");

        // 직급 자리가 MudSelect 여야 한다. MudTextField 로 남아 있으면 자유 입력이다.
        var posIdx = page.IndexOf("Label=\"직급\"", StringComparison.Ordinal);
        Assert.True(posIdx > 0, "직급 입력칸이 있어야 한다");

        var around = page.Substring(Math.Max(0, posIdx - 300), Math.Min(400, page.Length - Math.Max(0, posIdx - 300)));
        Assert.Contains("MudSelect", around);
        Assert.DoesNotContain("MudTextField T=\"string\" Label=\"직급\"", page);

        // 선택지는 직급 마스터에서 온다.
        Assert.Contains("PositionOptions", page);
    }

    /// <summary>
    /// 🔴 <b>지금 가진 직급 값이 마스터에 없어도 사라지면 안 된다.</b>
    /// 안 남기면 그 사원을 열었을 때 빈칸으로 보이고, 다른 항목만 고쳐 저장해도
    /// 직급이 조용히 지워진다. 실측으로 "과장"·"사원" 4명이 그 상태였다.
    /// </summary>
    [Fact]
    public void 마스터에_없는_기존_직급값도_선택지에_남긴다()
    {
        var code = ReadSource("src", "HitPan.Web", "Pages", "Settings", "EmployeePage.razor.cs");

        var idx = code.IndexOf("PositionOptions", StringComparison.Ordinal);
        Assert.True(idx > 0, "PositionOptions 가 있어야 한다");

        var block = code.Substring(idx, Math.Min(1400, code.Length - idx));
        Assert.Contains("_edit.Position", block);
        Assert.Contains("names.Add", block);
    }

    /// <summary>
    /// 🔴 <b>직급 시드가 실제 고객에게 가야 한다.</b>
    /// DB-22 는 <c>tenant_id='tenant-001'</c> 하드코딩이라 실제 테넌트에 안 갔고
    /// (실측: positions 0행), 프로비저너에도 시드가 없어 신규 고객사도 0개로 시작했다.
    /// </summary>
    [Fact]
    public void 직급_시드가_실제_테넌트와_신규가입에_모두_깔린다()
    {
        // 기존 고객 — 마이그레이션이 메운다.
        var mig = ReadSource("src", "HitPan.API", "Migrations", "SQL", "DB-93_seed_positions_all_tenants.sql");
        Assert.Contains("SELECT DISTINCT tenant_id FROM employees", mig);

        // ⚠️ 주석에는 'tenant-001' 이 나온다(왜 이 마이그가 필요한지 설명하느라).
        //    SQL 줄에만 없으면 된다 — 주석까지 금지하면 경위를 못 적는다.
        var sqlOnly = string.Join('\n',
            mig.Split('\n').Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal)));
        Assert.DoesNotContain("'tenant-001'", sqlOnly);

        // 이미 직급을 짠 회사는 건드리지 않는다(헌법 #1 덮어쓰기 금지 · #11).
        Assert.Contains("NOT EXISTS", mig);

        // 신규 고객사 — 프로비저너가 깐다(마이그는 사원이 생기기 전에 돌아 안 걸린다).
        var prov = ReadSource("src", "HitPan.API", "Services", "CompanyBootstrapProvisioner.cs");
        Assert.Contains("INSERT INTO positions", prov);
        Assert.Contains("NOT EXISTS", prov);
    }

    // ───────────────────────────────────────────────────────────────
    // 고용형태 · 주당 근로시간
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>사장님이 짚은 4형태가 다 있어야 한다.</b>
    /// <i>"정직원이냐, 알바냐, 계약직이냐, 무기계약직이냐 에 따라서도 달라짐"</i>(2026-08-12)
    /// 종전엔 무기계약직이 아예 없었고 "정규직"·"파트타임" 은 현장 말과 달랐다.
    /// </summary>
    [Fact]
    public void 고용형태에_사장님이_짚은_4형태가_다_있다()
    {
        foreach (var path in new[]
                 {
                     Path.Combine("src", "HitPan.Domain", "Enums", "EmployeeType.cs"),
                     Path.Combine("src", "HitPan.Web", "Models", "EmployeeModels.cs")
                 })
        {
            var code = ReadSource(path.Split(Path.DirectorySeparatorChar));

            Assert.Contains("정직원", code);
            Assert.Contains("무기계약직", code);
            Assert.Contains("계약직", code);
            Assert.Contains("알바", code);
        }

        // 화면 선택지에도 나와야 한다.
        var page = ReadSource("src", "HitPan.Web", "Pages", "Settings", "EmployeePage.razor");
        Assert.Contains("permanent", page);
        Assert.Contains("EmployeeTypeLabels.Permanent", page);
    }

    /// <summary>
    /// 🔴 <b>DB 값을 바꾸면 안 된다.</b>
    /// <c>ParseEmpType</c> 이 모르는 값을 조용히 <c>Regular</c> 로 폴백해서,
    /// 값을 바꾸면 <b>화면엔 정직원으로 보이면서 DB엔 딴 값이 남는다</b>. 오염이 안 보인다.
    /// </summary>
    [Fact]
    public void 고용형태_DB값은_그대로_두고_보이는_말만_바꾼다()
    {
        var en = ReadSource("src", "HitPan.Domain", "Enums", "EmployeeType.cs");

        // 기존 4개는 값도 번호도 그대로.
        Assert.Contains("Regular = 1", en);
        Assert.Contains("Contract = 2", en);
        Assert.Contains("Part = 3", en);
        Assert.Contains("Dispatch = 4", en);

        // 새 것은 뒤에 붙인다.
        Assert.Contains("Permanent = 5", en);

        // 모르는 값을 감추지 않는다 — 라벨은 그대로 보여줘야 오염을 찾는다.
        foreach (var path in new[]
                 {
                     Path.Combine("src", "HitPan.Domain", "Enums", "EmployeeType.cs"),
                     Path.Combine("src", "HitPan.Web", "Models", "EmployeeModels.cs")
                 })
        {
            var code = ReadSource(path.Split(Path.DirectorySeparatorChar));
            var idx = code.IndexOf("public static string Of(", StringComparison.Ordinal);
            Assert.True(idx > 0, $"Of() 가 있어야 한다: {path}");

            var block = code.Substring(idx, Math.Min(900, code.Length - idx));
            // 알 수 없는 값을 Regular 로 바꾸지 않는다.
            Assert.DoesNotContain("_ => Regular", block);
        }
    }

    /// <summary>
    /// 🔴 <b>주당 소정근로시간이 저장돼야 한다.</b>
    /// 연차·주휴·4대보험이 이 숫자로 갈린다(주 15시간이 갈림길).
    /// 고용형태만으로는 판정이 안 된다 — 같은 알바라도 주 20시간과 10시간은 다르다.
    /// 화면·DTO·SQL 중 하나라도 빠지면 화면만 있고 저장이 안 된다.
    /// </summary>
    [Fact]
    public void 주당_소정근로시간이_화면부터_DB까지_이어진다()
    {
        var page = ReadSource("src", "HitPan.Web", "Pages", "Settings", "EmployeePage.razor");
        Assert.Contains("주당 소정근로시간", page);
        Assert.Contains("_edit.WeeklyHours", page);

        var webModel = ReadSource("src", "HitPan.Web", "Models", "EmployeeModels.cs");
        Assert.Contains("decimal? WeeklyHours", webModel);

        var apiDto = ReadSource("src", "HitPan.Application", "DTOs", "Employee", "EmployeeDtos.cs");
        Assert.Contains("decimal? WeeklyHours", apiDto);

        var svc = ReadSource("src", "HitPan.Application", "Services", "EmployeeService.cs");
        Assert.Contains("weekly_hours AS WeeklyHours", svc);   // 조회
        Assert.Contains("@WeeklyHours", svc);                   // SQL 에 있나
        Assert.Contains("weekly_hours = @WeeklyHours", svc);    // 수정

        // 🔴 봉합 (2026-08-13, 검증 P0-1): <b>SQL 에 있는 것만으로는 저장되지 않는다.</b>
        //    Dapper 는 파라미터 객체에 같은 이름이 있어야 값을 넘긴다.
        //    실제로 INSERT 쪽 파라미터 객체에서 WeeklyHours 가 빠져 있었고,
        //    신규 등록 시 입력값이 조용히 NULL 로 들어갔다(수정은 정상이라 더 안 보였다).
        //    ⚠️ 예외조차 안 났다 — 연결문자열의 AllowUserVariables=true 때문에
        //    MySqlConnector 가 바인딩 안 된 @WeeklyHours 를 사용자변수(NULL)로 읽었다.
        //
        //    ⇒ "문자열이 있나" 가 아니라 <b>"SQL 의 @파라미터마다 객체에 짝이 있나"</b> 를 본다.
        AssertDapperParametersBound(svc, "@WeeklyHours", expectedAtLeast: 2);

        // 🔴 상세를 열 때 값을 되돌려 넣어야 한다.
        //    빠뜨리면 다른 항목만 고쳐 저장해도 이 값이 null 로 덮여 사라진다.
        var codeBehind = ReadSource("src", "HitPan.Web", "Pages", "Settings", "EmployeePage.razor.cs");
        Assert.Contains("WeeklyHours = detail.WeeklyHours", codeBehind);
    }

    /// <summary>
    /// 🔴 <b>반자동 원칙</b>(사장님 2026-08-12: <i>"히트판은 100%자동화는 없어. 무조건 반자동이야"</i>).
    /// 주당 근로시간·상시근로자수를 임의로 채우지 않는다. 모르는 것을 아는 척 채우면
    /// 그 값으로 연차가 계산되고 <b>틀린 줄도 모른 채 법정 미달</b>이 된다.
    /// </summary>
    [Fact]
    public void 법정판정에_쓰는_값을_임의로_채우지_않는다()
    {
        // 주당 근로시간 — nullable 이고 기본값이 없다.
        var apiDto = ReadSource("src", "HitPan.Application", "DTOs", "Employee", "EmployeeDtos.cs");
        Assert.DoesNotContain("WeeklyHours { get; set; } = 40", apiDto);

        var mig94 = ReadSource("src", "HitPan.API", "Migrations", "SQL", "DB-94_employee_weekly_hours.sql");
        Assert.Contains("DEFAULT NULL", mig94);
        Assert.DoesNotContain("DEFAULT 40", mig94);

        // 상시근로자수 — 사원 수로 자동 계산하지 않는다.
        var mig95 = ReadSource("src", "HitPan.API", "Migrations", "SQL", "DB-95_workplace_labor_settings.sql");
        Assert.Contains("DEFAULT NULL", mig95);
        Assert.DoesNotContain("SELECT COUNT(*) FROM employees", mig95);

        // 화면은 '제안' 만 한다 — 값을 넣지 않고 참고로 알려준다.
        var codeBehind = ReadSource("src", "HitPan.Web", "Pages", "Settings", "UserInfoPage.razor.cs");
        Assert.Contains("RegularEmployeeCountHint", codeBehind);
        Assert.Contains("참고", codeBehind);
    }

    // ───────────────────────────────────────────────────────────────
    // 사업장 설정
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>죽어 있던 <c>tax_type</c> 을 살린다.</b>
    /// 컬럼은 처음부터 있었는데 조회·저장·화면 어디에도 없어 값을 넣을 방법이 없었다
    /// (늘 기본값 <c>taxable</c>). 면세사업장은 계산이 달라지는데 표시할 방법이 없던 셈이다.
    /// </summary>
    [Fact]
    public void 사업장_노무정보가_화면부터_DB까지_이어진다()
    {
        var svc = ReadSource("src", "HitPan.Application", "Services", "SettingsService.cs");

        // 조회
        Assert.Contains("tax_type AS TaxType", svc);
        Assert.Contains("regular_employee_count AS RegularEmployeeCount", svc);
        Assert.Contains("business_entity_type AS BusinessEntityType", svc);

        // 저장
        Assert.Contains("tax_type = @TaxType", svc);
        Assert.Contains("regular_employee_count = @RegularEmployeeCount", svc);
        Assert.Contains("business_entity_type = @BusinessEntityType", svc);

        // 화면
        var page = ReadSource("src", "HitPan.Web", "Pages", "Settings", "UserInfoPage.razor");
        Assert.Contains("과세 유형", page);
        Assert.Contains("법인 / 개인", page);
        Assert.Contains("상시근로자수", page);

        // 🔴 조회 결과를 화면에 되돌려 넣어야 한다 — 빠뜨리면 저장해도 늘 비어 보인다.
        var codeBehind = ReadSource("src", "HitPan.Web", "Pages", "Settings", "UserInfoPage.razor.cs");
        Assert.Contains("_model.TaxType = company.TaxType", codeBehind);
        Assert.Contains("_model.RegularEmployeeCount = company.RegularEmployeeCount", codeBehind);
    }

    /// <summary>
    /// 🔴 <b>정해진 값만 저장한다.</b>
    /// 아무 문자열이나 들어가면, 나중에 이 값으로 연차·보험을 판정하는 쪽이
    /// 모르는 값을 만나 조용히 기본값으로 흘러간다(<c>emp_type</c> 이 겪는 그 병).
    /// </summary>
    [Fact]
    public void 사업장_선택값은_정해진_것만_받는다()
    {
        var svc = ReadSource("src", "HitPan.Application", "Services", "SettingsService.cs");

        Assert.Contains("NormalizeChoice", svc);
        Assert.Contains("\"taxable\", \"tax_free\"", svc);
        Assert.Contains("\"corporate\", \"individual\"", svc);

        // 음수 상시근로자수를 막는다.
        Assert.Contains("RegularEmployeeCount is > 0", svc);
    }

    /// <summary>
    /// 🔴 <b>마이그레이션과 출하 DDL 이 함께 가야 한다</b>(헌법 #36).
    /// 한쪽만 하면 기존 고객과 신규 설치가 서로 다른 스키마가 된다.
    /// </summary>
    [Fact]
    public void 신설_컬럼이_출하_DDL에도_있다()
    {
        var cleanDdl = ReadSource("installer", "hitpan_db_clean.sql");

        Assert.Contains("`weekly_hours`", cleanDdl);
        Assert.Contains("`regular_employee_count`", cleanDdl);
        Assert.Contains("`business_entity_type`", cleanDdl);
        Assert.Contains("`employee_count_asof`", cleanDdl);

        // 시드 기록도 맞아야 한다(ddl-smoke 게이트가 파일 수와 대조한다).
        foreach (var id in new[] { "DB-93", "DB-94", "DB-95" })
        {
            Assert.Contains($"('{id}','clean-ddl',1)", cleanDdl);
        }
    }

    /// <summary>
    /// 🔴 <b>마이그레이션은 두 번 돌아도 안전해야 한다.</b>
    /// 워치독 자동 업데이트가 재시도하거나, 고객이 재설치하면 같은 마이그가 다시 돈다.
    /// </summary>
    [Fact]
    public void 신설_마이그레이션은_멱등이다()
    {
        var mig93 = ReadSource("src", "HitPan.API", "Migrations", "SQL", "DB-93_seed_positions_all_tenants.sql");
        Assert.Contains("NOT EXISTS", mig93);

        foreach (var name in new[]
                 {
                     "DB-94_employee_weekly_hours.sql",
                     "DB-95_workplace_labor_settings.sql"
                 })
        {
            var mig = ReadSource("src", "HitPan.API", "Migrations", "SQL", name);
            // 컬럼이 이미 있으면 건너뛴다.
            Assert.Contains("information_schema.columns", mig);
            Assert.Contains("PREPARE", mig);
        }
    }
}
