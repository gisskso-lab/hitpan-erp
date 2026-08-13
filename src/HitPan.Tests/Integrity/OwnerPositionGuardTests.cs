using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(2026-08-14) 🔴 <b>부모계정 직급 자동 등록 게이트.</b>
/// 사장님 지시: <i>"부모계정 = 직급은 자동으로 대표.등록"</i>
/// </summary>
/// <remarks>
/// <para>
/// <b>무엇을 겪고서</b> — 1.2.74 실사용에서 부모계정의 <c>position</c> 이 <b>NULL</b> 이었다
/// (DB 실측: <c>emp_no=0001, emp_name=마스터, position=NULL</c>). 사원관리·직원현황에서
/// 대표의 직급이 빈칸이고, <b>직급으로 짜는 결재선에서 대표를 고를 수 없었다.</b>
/// </para>
/// <para>
/// 🔴 <b>왜 시험이 필요한가</b> — 부모계정의 사원 행을 만드는 자리가 <b>둘</b>이다:
/// ① 신규설치 프로비저닝(<c>CompanyBootstrapProvisioner</c>)
/// ② 로그인 시 백필(<c>AuthService.BackfillParentEmployeeAsync</c>)
/// 한쪽만 고치면 <b>설치 경로에 따라 직급이 있기도 없기도 하다</b> —
/// 실제로 종전엔 두 곳 다 빠져 있었다. 그래서 상수 하나를 두 곳이 함께 쓰는지 본다.
/// </para>
/// </remarks>
public class OwnerPositionGuardTests
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

    /// <summary>
    /// 대표 직급 이름은 <b>한 곳에서만</b> 정한다.
    /// </summary>
    [Fact]
    public void 대표_직급명은_상수_한_곳에서_온다()
    {
        var src = ReadSource("src", "HitPan.Domain", "Common", "OrgDefaults.cs");

        Assert.Contains("OwnerPositionName", src);
        Assert.Contains("\"대표이사\"", src);
    }

    /// <summary>
    /// 🔴 부모계정 사원 행을 만드는 <b>두 자리 모두</b> 직급을 넣어야 한다.
    /// </summary>
    /// <remarks>
    /// 한쪽만 넣으면 <b>신규설치로 들어온 고객</b>과 <b>옛 계정으로 로그인한 고객</b> 의
    /// 직급이 서로 달라진다. 둘 다 같은 상수를 쓰는지 확인한다.
    /// </remarks>
    [Fact]
    public void 부모계정_사원행을_만드는_두_자리가_모두_직급을_넣는다()
    {
        // ① 신규설치 프로비저닝
        var provisioner = ReadSource("src", "HitPan.API", "Services", "CompanyBootstrapProvisioner.cs");
        Assert.Contains("OwnerPositionName", provisioner);
        Assert.Contains("position", provisioner);

        // ② 로그인 백필
        var auth = ReadSource("src", "HitPan.Application", "Services", "AuthService.cs");
        Assert.Contains("OwnerPositionName", auth);
    }

    /// <summary>
    /// 🔴 대표 직급명이 <b>직급 마스터 시드에 실재</b>해야 한다.
    /// </summary>
    /// <remarks>
    /// 마스터에 없는 이름을 사원 행에 적으면 글자는 남지만 <b>직급 목록에 없어
    /// 결재선에서 고를 수 없다.</b> "대표"와 "대표이사" 가 갈리는 사고를 막는 자리다
    /// (사장님 결재 2026-08-14: 기존 "대표이사" 를 쓴다).
    /// </remarks>
    [Fact]
    public void 대표_직급이_직급마스터_시드에_있다()
    {
        var defaults = ReadSource("src", "HitPan.Domain", "Common", "OrgDefaults.cs");
        var provisioner = ReadSource("src", "HitPan.API", "Services", "CompanyBootstrapProvisioner.cs");

        // OrgDefaults 가 정한 이름을 시드가 그대로 갖고 있어야 한다.
        var start = defaults.IndexOf("OwnerPositionName", StringComparison.Ordinal);
        Assert.True(start > 0, "상수가 있어야 한다");

        var quoteOpen = defaults.IndexOf('"', start);
        var quoteClose = defaults.IndexOf('"', quoteOpen + 1);
        var ownerName = defaults[(quoteOpen + 1)..quoteClose];

        Assert.False(string.IsNullOrWhiteSpace(ownerName), "대표 직급명이 비면 안 된다");
        Assert.True(provisioner.Contains($"\"{ownerName}\"", StringComparison.Ordinal),
            $"대표 직급 「{ownerName}」 이 직급 마스터 시드(stdPositions)에 있어야 한다 — "
            + "없으면 결재선에서 대표를 고를 수 없다");
    }
}
