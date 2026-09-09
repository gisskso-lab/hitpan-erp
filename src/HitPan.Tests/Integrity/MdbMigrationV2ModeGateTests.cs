using System.Reflection;
using HitPan.API.Controllers;
using HitPan.API.Security;
using HitPan.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>MdbMigrationV2ModeGate</b> — 작22 (2026-09-09) 갈래 A·B: POST 이동 · 두 모드 · 1/2단계 · 찾아보기 경로 규칙.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>왜 이 게이트를 짰나</b><br/>
/// ① 병렬이슈 11: <c>preview</c>·<c>reconcile</c> 이 GET 이라 MDB 비번이 URL·서버 로그에 남았다.
///    GET 을 남긴 채 POST 만 더하면 봉합이 아니다 — 그래서 <b>GET 의 부재</b>까지 본다(W13).<br/>
/// ② 병합 모드는 "있는 건 두고 빈칸만 채운다" 인데, 종전 UPSERT 절은 레거시가 ERP 를 덮었다(G-MH).
///    갱신 절 조립을 순수 함수로 빼고 <b>값으로 불러</b> 모드에 따라 정말 갈리는지 본다(W16).<br/>
/// ③ 찾아보기는 서버가 파일시스템을 여는 자리다. <c>..</c>·UNC·상대경로·<c>C:\Windows</c> 를 <b>값으로 넣어</b> 막히는지 본다(W17).
/// </para>
///
/// <para>
/// 🔴 <b>층이 둘이다</b> — 값 검사(리플렉션·순수함수)가 본체이고, 배선 검사(소스 글자)는 "함수는 맞는데 서비스가 안 부른다"
/// (8/27 작7) 를 막는 보조다. 배선 층의 한계(<c>if (false &amp;&amp; …)</c>)는 안다 — 그래서 종전 코드 모양의 <b>부재</b>도 같이 본다.
/// </para>
///
/// <para>
/// ⚠️ <b>이 시험이 못 하는 것</b> — 체크포인트 건너뛰기(G-CK)·빈 DB 병합=덮어쓰기(G-M1)·ERP 수정값 유지(G-MH)는
/// <c>hitpan_e2e</c> 실측의 몫이다(작업지시서 §5). 여기서는 그 규칙의 순수 부분(<see cref="MdbMigrationCheckpointPolicy"/>·<see cref="MdbMigrationModes"/>)만 잰다.
/// </para>
/// </remarks>
public sealed class MdbMigrationV2ModeGateTests
{
    // ────────────────────────────────────────────────────────────────────────────
    //  W13 — 엔드포인트 리플렉션: preview/reconcile 은 POST 뿐 · continue/current/browse 존재 · 정책
    // ────────────────────────────────────────────────────────────────────────────

    private static IReadOnlyList<(MethodInfo Method, string Verb, string Template)> Routes()
        => typeof(MigrationController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>(inherit: true)
                .SelectMany(a => a.HttpMethods.Select(v => (m, v.ToUpperInvariant(), a.Template ?? string.Empty))))
            .ToList();

    /// <summary>
    /// 🔴 W13-a — <c>legacy-mdb/preview</c>·<c>legacy-mdb/reconcile</c> 은 <b>POST 가 있고 GET 이 없다</b>.
    /// <para>무력화: 컨트롤러에 <c>[HttpGet("legacy-mdb/preview")]</c> 를 남겨 두면(또는 POST 를 안 만들면) 빨간불.</para>
    /// </summary>
    [Theory]
    [InlineData("legacy-mdb/preview")]
    [InlineData("legacy-mdb/reconcile")]
    public void W13a_preview_reconcile_은_POST_만(string template)
    {
        var verbs = Routes().Where(r => r.Template == template).Select(r => r.Verb).ToList();
        Assert.Contains("POST", verbs);
        Assert.DoesNotContain("GET", verbs);
    }

    /// <summary>
    /// 🔴 W13-b — POST 로 옮긴 두 액션은 비번을 <b>바디</b>(<see cref="MdbMigrationRequest"/>)로 받고, 쿼리스트링 <c>mdbPassword</c> 파라미터가 없다.
    /// 비번이 URL 에 남지 않게 옮긴 것이 목적이므로 파라미터 모양까지 본다.
    /// </summary>
    [Theory]
    [InlineData("legacy-mdb/preview")]
    [InlineData("legacy-mdb/reconcile")]
    public void W13b_POST_액션은_바디로_받고_쿼리_비번이_없다(string template)
    {
        var post = Routes().FirstOrDefault(r => r.Template == template && r.Verb == "POST");
        Assert.NotNull(post.Method);
        var ps = post.Method!.GetParameters();
        Assert.Contains(ps, p => p.ParameterType == typeof(MdbMigrationRequest) && p.GetCustomAttribute<FromBodyAttribute>() is not null);
        Assert.DoesNotContain(ps, p => string.Equals(p.Name, "mdbPassword", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(ps, p => p.GetCustomAttribute<FromQueryAttribute>() is not null && p.ParameterType == typeof(string));
    }

    /// <summary>
    /// 🔴 W13-c — 새 엔드포인트 3개가 정해진 동사·경로로 있다.
    /// <para>무력화: 셋 중 하나를 지우거나 동사를 바꾸면 빨간불.</para>
    /// </summary>
    [Theory]
    [InlineData("POST", "legacy-mdb/{jobId}/continue")]
    [InlineData("GET", "legacy-mdb/current")]
    [InlineData("GET", "legacy-mdb/browse")]
    public void W13c_continue_current_browse_가_있다(string verb, string template)
        => Assert.Contains(Routes(), r => r.Verb == verb && r.Template == template);

    /// <summary>🔴 W13-d — 클래스 정책은 <c>TenantAdminOnly</c> 그대로(헌법 #2 · 새 엔드포인트도 이 정책 아래).</summary>
    [Fact]
    public void W13d_클래스_정책은_TenantAdminOnly()
    {
        var auth = typeof(MigrationController).GetCustomAttribute<AuthorizeAttribute>(inherit: false);
        Assert.NotNull(auth);
        Assert.Equal("TenantAdminOnly", auth!.Policy);
    }

    /// <summary>
    /// 🔴 W13-e — <c>browse</c> 는 서버측 <see cref="MainPcOnlyAttribute"/> 를 단다 — 자료가 든 그 PC 에서만(8/11 사장님 지시 · 화면 감춤은 차단이 아니다).
    /// <c>continue</c> 는 바디로 <see cref="MdbMigrationRequest"/> 를 받는다(비번을 다시 받고 저장하지 않는다).
    /// </summary>
    [Fact]
    public void W13e_browse_는_MainPcOnly_continue_는_바디()
    {
        var browse = Routes().FirstOrDefault(r => r.Template == "legacy-mdb/browse" && r.Verb == "GET");
        Assert.NotNull(browse.Method);
        Assert.NotNull(browse.Method!.GetCustomAttribute<MainPcOnlyAttribute>(inherit: true));

        var cont = Routes().FirstOrDefault(r => r.Template == "legacy-mdb/{jobId}/continue" && r.Verb == "POST");
        Assert.NotNull(cont.Method);
        Assert.Contains(cont.Method!.GetParameters(),
            p => p.ParameterType == typeof(MdbMigrationRequest) && p.GetCustomAttribute<FromBodyAttribute>() is not null);
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  W14 — DTO 필드 · MigrateAsync overload (기존 시그니처 보존 + phase/mode 추가)
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 W14-a — <see cref="MdbMigrationRequest"/> 에 <c>Mode</c>(기본 "merge")·<c>Password</c>·<c>ConfirmText</c> 가 있다.
    /// <para>무력화: 필드를 지우거나 기본값을 바꾸면 빨간불.</para>
    /// </summary>
    [Fact]
    public void W14a_요청_DTO_에_Mode_Password_ConfirmText()
    {
        var t = typeof(MdbMigrationRequest);
        var mode = t.GetProperty("Mode");
        Assert.NotNull(mode);
        Assert.Equal(typeof(string), mode!.PropertyType);
        Assert.NotNull(t.GetProperty("Password"));
        Assert.NotNull(t.GetProperty("ConfirmText"));
        Assert.Equal(typeof(string), t.GetProperty("Password")!.PropertyType);
        Assert.Equal(typeof(string), t.GetProperty("ConfirmText")!.PropertyType);

        // 리플렉션으로 읽는다 — 필드가 없는 상태(봉합 전)에서도 컴파일돼 빨간불을 볼 수 있게.
        var dto = new MdbMigrationRequest();
        Assert.Equal("merge", mode.GetValue(dto));
    }

    /// <summary>
    /// 🔴 W14-b — <c>MigrateAsync</c> 에 <see cref="MdbMigrationPhase"/> 를 받는 overload 가 있고, 종전 overload 4개도 그대로 있다
    /// (baseline 도구 · 동기 엔드포인트 · MigrationSmokeTest 가 그 시그니처를 부른다).
    /// </summary>
    [Fact]
    public void W14b_MigrateAsync_overload_보존과_추가()
    {
        var methods = typeof(MdbMigrationService).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "MigrateAsync").ToList();

        static bool Sig(MethodInfo m, params Type[] types)
            => m.GetParameters().Select(p => p.ParameterType).SequenceEqual(types);

        var cb = typeof(Action<string, string, int, long, string?>);
        Assert.Contains(methods, m => Sig(m, typeof(string), typeof(string), typeof(CancellationToken)));
        Assert.Contains(methods, m => Sig(m, typeof(string), typeof(string), typeof(string), typeof(CancellationToken)));
        Assert.Contains(methods, m => Sig(m, typeof(string), typeof(string), typeof(string), typeof(string), typeof(CancellationToken)));
        Assert.Contains(methods, m => Sig(m, typeof(string), typeof(string), typeof(string), typeof(string), cb, typeof(CancellationToken)));
        Assert.Contains(methods, m => m.GetParameters().Any(p => p.ParameterType == typeof(MdbMigrationPhase))
                                      && m.GetParameters().Any(p => p.ParameterType == typeof(string) && p.Name == "mode"));
    }

    /// <summary>
    /// 🔴 W14-c — 모드 어휘: 병합만 <c>unique_checks=1</c>, 덮어쓰기·미지정은 0(선행검증 §2-8). <c>overwrite</c> 는 어휘상 유효(400 은 컨트롤러가 낸다).
    /// </summary>
    [Theory]
    [InlineData("merge", 1, true)]
    [InlineData("MERGE", 1, true)]
    [InlineData("overwrite", 0, true)]
    [InlineData(null, 0, true)]
    [InlineData("", 0, true)]
    [InlineData("delete", 0, false)]
    public void W14c_모드_어휘와_unique_checks(string? mode, int uniqueChecks, bool valid)
    {
        Assert.Equal(uniqueChecks, MdbMigrationModes.UniqueChecksFor(mode));
        Assert.Equal(valid, MdbMigrationModes.IsValid(mode));
    }

    /// <summary>🔴 W14-d — 체크포인트 예외 표: 리빌드·창고·마스터(map-only)는 done 이어도 다시 돈다, 거래 표는 건너뛴다.</summary>
    [Theory]
    [InlineData("item_stock_rebuild", true)]
    [InlineData("warehouse_migration", true)]
    [InlineData("pyojun_master", true)]
    [InlineData("stock_ledger", false)]
    [InlineData("tax_invoices", false)]
    [InlineData("sales_deliveries", false)]
    public void W14d_체크포인트_항상재실행_표(string table, bool rerun)
        => Assert.Equal(rerun, MdbMigrationCheckpointPolicy.AlwaysRerun(table));

    // ────────────────────────────────────────────────────────────────────────────
    //  W16 — 마스터 UPSERT 갱신 절: 병합 = 빈칸만 채움 · 덮어쓰기/미지정 = 종전
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 W16-a — 병합 모드는 문자 컬럼을 <c>COALESCE(NULLIF(col, ''), VALUES(col))</c> 로, 덮어쓰기·미지정은 <c>VALUES(col)</c> 로 낸다.
    /// <para>무력화: <see cref="MdbMasterMergeClause.Assignment"/> 가 모드를 안 보고 종전 절만 내면 빨간불.</para>
    /// </summary>
    [Theory]
    [InlineData("merge", "partner_name = COALESCE(NULLIF(partner_name, ''), VALUES(partner_name))")]
    [InlineData("overwrite", "partner_name = VALUES(partner_name)")]
    [InlineData(null, "partner_name = VALUES(partner_name)")]
    public void W16a_문자_컬럼_절이_모드에_따라_갈린다(string? mode, string expected)
        => Assert.Equal(expected, MdbMasterMergeClause.Assignment(mode, new MdbMergeColumn("partner_name", MdbMergeBlank.Text)));

    /// <summary>
    /// 🔴 W16-b — 숫자 컬럼(decimal NOT NULL DEFAULT 0.00)은 0 이 빈칸: <c>NULLIF(col, 0)</c>. 날짜·플래그·이진은 NULL 만 빈칸.
    /// </summary>
    [Fact]
    public void W16b_숫자는_0_이_빈칸_날짜플래그는_NULL_만()
    {
        Assert.Equal("sale_price = COALESCE(NULLIF(sale_price, 0), VALUES(sale_price))",
            MdbMasterMergeClause.Assignment("merge", new MdbMergeColumn("sale_price", MdbMergeBlank.Number)));
        Assert.Equal("join_date = COALESCE(join_date, VALUES(join_date))",
            MdbMasterMergeClause.Assignment("merge", new MdbMergeColumn("join_date", MdbMergeBlank.NullOnly)));
        // 덮어쓰기는 종류와 무관하게 종전 절
        Assert.Equal("sale_price = VALUES(sale_price)",
            MdbMasterMergeClause.Assignment("overwrite", new MdbMergeColumn("sale_price", MdbMergeBlank.Number)));
    }

    /// <summary>
    /// 🔴 W16-c — 전체 절: 항상-갱신 컬럼(<c>updated_at</c>·<c>migrated_source_hash</c>)은 모드와 무관하게 <c>VALUES</c>,
    /// 사용자 컬럼만 모드를 탄다. 미지정 모드의 절에는 COALESCE 가 한 글자도 없다(종전과 동일).
    /// </summary>
    [Fact]
    public void W16c_전체_절_조립()
    {
        var cols = new[] { new MdbMergeColumn("partner_name", MdbMergeBlank.Text), new MdbMergeColumn("credit_limit", MdbMergeBlank.Number) };
        var always = new[] { "updated_at", "migrated_source_hash" };

        var merge = MdbMasterMergeClause.Build("merge", cols, always);
        Assert.Contains("partner_name = COALESCE(NULLIF(partner_name, ''), VALUES(partner_name))", merge);
        Assert.Contains("credit_limit = COALESCE(NULLIF(credit_limit, 0), VALUES(credit_limit))", merge);
        Assert.Contains("updated_at = VALUES(updated_at)", merge);
        Assert.Contains("migrated_source_hash = VALUES(migrated_source_hash)", merge);
        Assert.DoesNotContain("COALESCE(NULLIF(updated_at", merge);

        var legacy = MdbMasterMergeClause.Build(null, cols, always);
        Assert.DoesNotContain("COALESCE", legacy);
        Assert.Contains("partner_name = VALUES(partner_name)", legacy);
        Assert.Contains("credit_limit = VALUES(credit_limit)", legacy);
        Assert.Contains("updated_at = VALUES(updated_at)", legacy);
    }

    /// <summary>🔴 W16-d — 어휘 밖 모드는 조용히 한쪽으로 몰지 않고 throw 한다.</summary>
    [Fact]
    public void W16d_어휘_밖_모드는_throw()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            MdbMasterMergeClause.Assignment("delete", new MdbMergeColumn("partner_name", MdbMergeBlank.Text)));

    /// <summary>
    /// 🔴 W16-w — 배선: 서비스가 업체·상품·사원 세 자리에서 <see cref="MdbMasterMergeClause.Build"/> 를 부르고,
    /// 종전 고정 절(<c>partner_name = VALUES(partner_name),</c> 등)이 소스 리터럴에 남아 있지 않다.
    /// <para>한계를 안다 — 글자검사다. 값 검사(W16-a~d)가 본체이고 이것은 "함수는 맞는데 안 부른다" 를 막는 보조다.</para>
    /// </summary>
    [Fact]
    public void W16w_서비스_배선_세_자리()
    {
        var src = StripComments(ReadSource("src", "HitPan.Application", "Services", "MdbMigrationService.cs"));
        var calls = CountOf(src, "MdbMasterMergeClause.Build(");
        Assert.True(calls >= 3, $"업체·상품·사원 세 자리에서 Build 를 불러야 한다 (지금 {calls}곳)");
        Assert.DoesNotContain("partner_name = VALUES(partner_name),", src);
        Assert.DoesNotContain("item_name = VALUES(item_name), unit = VALUES(unit)", src);
        Assert.DoesNotContain("emp_name = VALUES(emp_name), position = VALUES(position)", src);
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  W17 — 찾아보기 경로 규칙 (값으로)
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 W17-a — <c>..</c> 포함 · UNC(<c>\\srv\share</c>) · 상대경로는 거부, 정상 절대경로는 통과.
    /// <para>무력화: <see cref="MdbFolderBrowsePolicy.TryValidatePath"/> 가 전부 통과시키면 빨간불.</para>
    /// </summary>
    [Theory]
    [InlineData(@"C:\HITWIN\..\Windows", false)]
    [InlineData(@"C:\..", false)]
    [InlineData(@"\\srv\share", false)]
    [InlineData(@"\\srv\share\HITWIN", false)]
    [InlineData(@"//srv/share", false)]
    [InlineData(@"HITWIN", false)]
    [InlineData(@"C:HITWIN", false)]
    [InlineData(@".\HITWIN", false)]
    [InlineData(@"", false)]
    [InlineData(@"C:\HITWIN", true)]
    [InlineData(@"D:\자료\HITWIN", true)]
    [InlineData(@"C:\", true)]
    public void W17a_경로_검증(string path, bool ok)
    {
        var result = MdbFolderBrowsePolicy.TryValidatePath(path, out var error);
        Assert.Equal(ok, result);
        if (!ok) Assert.False(string.IsNullOrWhiteSpace(error), "거부 사유 문구가 있어야 한다");
        else Assert.Null(error);
    }

    /// <summary>🔴 W17-b — <c>C:\Windows</c> 와 그 아래는 목록에서 제외 판정, <c>C:\HITWIN</c> 은 아니다.</summary>
    [Theory]
    [InlineData(@"C:\Windows", true)]
    [InlineData(@"C:\windows\System32", true)]
    [InlineData(@"C:\Program Files", true)]
    [InlineData(@"C:\Program Files (x86)\x", true)]
    [InlineData(@"C:\$Recycle.Bin", true)]
    [InlineData(@"C:\System Volume Information", true)]
    [InlineData(@"C:\HITWIN", false)]
    [InlineData(@"C:\HITWIN\Windows", false)]
    [InlineData(@"C:\", false)]
    public void W17b_시스템_폴더_제외_판정(string path, bool excluded)
        => Assert.Equal(excluded, MdbFolderBrowsePolicy.IsExcludedPath(path));

    /// <summary>🔴 W17-c — 폴더 이름 단위 제외(대소문자 무시) + ★ 조건(MDB 3개 다 있어야).</summary>
    [Fact]
    public void W17c_폴더이름_제외와_MDB_3개_판정()
    {
        Assert.True(MdbFolderBrowsePolicy.IsExcludedFolderName("Windows"));
        Assert.True(MdbFolderBrowsePolicy.IsExcludedFolderName("$recycle.bin"));
        Assert.False(MdbFolderBrowsePolicy.IsExcludedFolderName("HITWIN"));
        Assert.False(MdbFolderBrowsePolicy.IsExcludedFolderName("Windows Backup"));

        Assert.True(MdbFolderBrowsePolicy.HasAllMdbFiles(new[] { "pyojun.mdb", "PANDATA.MDB", "Pother.mdb", "readme.txt" }));
        Assert.False(MdbFolderBrowsePolicy.HasAllMdbFiles(new[] { "PYOJUN.MDB", "PANDATA.mdb" }));
        Assert.False(MdbFolderBrowsePolicy.HasAllMdbFiles(Array.Empty<string>()));
    }

    /// <summary>
    /// 🔴 W17-w — 배선: 컨트롤러 browse 액션이 <see cref="MdbFolderBrowsePolicy"/> 를 부른다(검증·제외·★ 세 자리).
    /// </summary>
    [Fact]
    public void W17w_컨트롤러_배선()
    {
        var src = StripComments(ReadSource("src", "HitPan.API", "Controllers", "MigrationController.cs"));
        Assert.Contains("MdbFolderBrowsePolicy.TryValidatePath(", src);
        Assert.Contains("MdbFolderBrowsePolicy.IsExcludedPath(", src);
        Assert.Contains("MdbFolderBrowsePolicy.IsExcludedFolderName(", src);
        Assert.Contains("MdbFolderBrowsePolicy.HasAllMdbFiles(", src);
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  helpers
    // ────────────────────────────────────────────────────────────────────────────

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }
        Assert.True(dir is not null && Directory.Exists(Path.Combine(dir, "src")), "레포 루트를 찾아야 한다");
        return dir!;
    }

    private static string ReadSource(params string[] parts)
    {
        var path = Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"파일이 있어야 한다: {path}");
        return File.ReadAllText(path);
    }

    /// <summary>주석 줄을 걷어낸 코드만 남긴다 — 설명문에 적힌 종전 코드 모양에 걸려 헛통과·헛실패하지 않게.</summary>
    private static string StripComments(string source)
        => string.Join('\n', source.Split('\n').Where(l =>
        {
            var t = l.TrimStart();
            return !(t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("///", StringComparison.Ordinal));
        }));

    private static int CountOf(string haystack, string needle)
    {
        var count = 0;
        var idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }
}
