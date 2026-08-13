using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(2026-08-14) 🔴 <b>계정↔사원 연결 게이트.</b>
/// </summary>
/// <remarks>
/// <para>
/// <b>무엇을 겪고서</b> — 1.2.74 실사용에서 사장님이 직원계정을 등록했는데
/// <i>"자식계정은 생성되었으나, 직원계정 관리 외 다른 그 어떤메뉴에도 그 계정직원은 안나옴"</i>.
/// </para>
/// <para>
/// 진범은 <b>두 개가 겹친 것</b>이었다:
/// ① 사번 채번이 <c>emp_no LIKE 'EMP-%'</c> 로 <b>EMP- 형식만</b> 셌다. 실측한 DB 의 사번은
///    <c>0001</c>(부모 백필) · <c>MIG-0001~0010</c>(마이그) 뿐이라 <b>EMP- 가 0건</b> →
///    MAX 가 늘 0 → 채번이 늘 <c>EMP-001</c> → <c>uq_tenant_empno</c> UNIQUE 충돌.
/// ② <b>트랜잭션이 없어</b> <c>employees</c> 가 실패해도 <c>users</c> 는 이미 커밋됐다.
/// </para>
/// <para>
/// 그래서 <b>계정만 있고 사원이 없는 고아</b>가 남았고, 재등록은 이메일 중복으로 막히고
/// 사원관리에서 넣으면 <c>user_id</c> 가 NULL 인 별개 행이 생겨 <b>스스로 복구가 안 됐다.</b>
/// </para>
/// </remarks>
public class AccountEmployeeLinkGuardTests
{
    private static string FindRepoRoot()
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

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray()));

    private static string UserServiceSrc() =>
        ReadSource("src", "HitPan.Application", "Services", "UserService.cs");

    private static string AuthServiceSrc() =>
        ReadSource("src", "HitPan.Application", "Services", "AuthService.cs");

    /// <summary>
    /// 🔴 계정과 사원은 <b>한 트랜잭션</b>이어야 한다.
    /// </summary>
    /// <remarks>
    /// 둘 중 하나만 남으면 <b>반쪽 계정</b>이 되고, 그 상태는 화면에서 되돌릴 길이 없다.
    /// </remarks>
    [Fact]
    public void 계정과_사원은_한_트랜잭션으로_묶인다()
    {
        var src = UserServiceSrc();

        var createIdx = src.IndexOf("public async Task<string> CreateAsync", StringComparison.Ordinal);
        Assert.True(createIdx > 0, "CreateAsync 가 있어야 한다");

        var updateIdx = src.IndexOf("public async Task UpdateAsync", StringComparison.Ordinal);
        var body = updateIdx > createIdx ? src[createIdx..updateIdx] : src[createIdx..];

        Assert.Contains("BeginTransaction", body);
        Assert.Contains("tx.Commit()", body);

        // users·employees 두 INSERT 가 모두 그 트랜잭션을 타야 한다.
        Assert.Contains("INSERT INTO users", body);
        Assert.Contains("INSERT INTO employees", body);

        var inserts = Regex.Matches(body, @"INSERT INTO (users|employees)").Count;
        var onTx = Regex.Matches(body, @"transaction: tx").Count;
        Assert.True(onTx >= inserts,
            $"INSERT {inserts}건이 전부 transaction: tx 를 타야 한다(실측 {onTx}건). "
            + "하나라도 빠지면 그 행만 따로 커밋돼 반쪽 계정이 남는다");
    }

    /// <summary>
    /// 🔴 사번 채번이 <b>접두를 가리지 않아야</b> 한다.
    /// </summary>
    /// <remarks>
    /// <c>0001</c>·<c>MIG-0007</c>·<c>EMP-012</c> 가 한 테넌트에 섞여 있다. 한 형식만 세면
    /// MAX 가 0 이 되고 채번이 고정돼 <b>UNIQUE 충돌로 사원이 안 생긴다.</b>
    /// </remarks>
    [Fact]
    public void 사번_채번이_접두를_가리지_않는다()
    {
        // 🔴 주석은 걸러낸다 — 무엇을 왜 고쳤는지 설명하는 주석에 옛 SQL 이 인용돼 있어,
        //    그대로 검사하면 봉합해 놓고도 빨간불이 뜬다(실제로 이 시험이 그렇게 걸렸다).
        var code = string.Join('\n', UserServiceSrc().Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        // 옛 방식(EMP- 만 세기)이 살아 있으면 안 된다.
        Assert.DoesNotContain("emp_no LIKE 'EMP-%'", code);

        // 끝자리 숫자로 센다.
        Assert.Contains("REGEXP_SUBSTR", code);
    }

    /// <summary>
    /// 🔴 로그인 백필의 채번도 <b>같은 규칙</b>이어야 한다.
    /// </summary>
    /// <remarks>
    /// <c>int.TryParse("MIG-0007")</c> 도 <c>int.TryParse("EMP-001")</c> 도 실패해 0 이 된다.
    /// 여기만 옛 규칙이면 <b>백필이 부모계정 기존 행과 충돌</b>한다.
    /// </remarks>
    [Fact]
    public void 백필_채번도_접두를_가리지_않는다()
    {
        var src = AuthServiceSrc();

        var idx = src.IndexOf("BackfillParentEmployeeAsync", StringComparison.Ordinal);
        Assert.True(idx > 0, "백필 메서드가 있어야 한다");

        var body = src[idx..];
        Assert.Contains("[0-9]+$", body);
    }

    /// <summary>
    /// 🔴 <b>자식계정도 자가치유</b>돼야 한다 — 이미 생긴 고아를 되살리는 유일한 길이다.
    /// </summary>
    /// <remarks>
    /// 종전엔 백필이 <c>tenant_admin</c> 전용이라 자식 고아는 영원히 복구가 안 됐다.
    /// </remarks>
    [Fact]
    public void 자식계정도_사원행이_자가치유된다()
    {
        var src = AuthServiceSrc();

        // 부모 전용 게이트가 남아 있으면 자식은 여전히 못 고친다.
        Assert.DoesNotContain("employee is null && user.AccountType == \"tenant_admin\"", src);
    }

    /// <summary>
    /// 🔴 자가치유가 <b>권한 승격이 되면 안 된다.</b>
    /// </summary>
    /// <remarks>
    /// 백필을 자식에게 열어 준 대가로 생길 수 있는 새 사고다. 자식에게
    /// <c>role = "tenant_admin"</c> 이나 대표 직급이 붙으면 <b>사원 하나 고치려다
    /// 관리자를 만들어 버린다.</b>
    /// </remarks>
    [Fact]
    public void 자가치유가_자식을_관리자로_올리지_않는다()
    {
        var src = AuthServiceSrc();

        var idx = src.IndexOf("BackfillParentEmployeeAsync", StringComparison.Ordinal);
        Assert.True(idx > 0, "백필 메서드가 있어야 한다");

        var body = src[idx..];

        // 부모/자식을 가르는 판정이 있어야 한다.
        Assert.Contains("isOwner", body);

        // 역할·직급이 그 판정에 따라 갈려야 한다(무조건 tenant_admin 금지).
        Assert.Matches(new Regex(@"isOwner\s*\?\s*""tenant_admin""\s*:"), body);
        Assert.Matches(new Regex(@"isOwner\s*\?\s*HitPan\.Domain\.Common\.OrgDefaults\.OwnerPositionName\s*:"), body);
    }
}
