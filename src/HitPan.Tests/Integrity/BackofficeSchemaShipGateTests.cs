using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 백오피스 스키마 **동봉** 게이트 — 「스위치만 켜면 표가 생긴다」가 거짓이던 사고를 막는다.
///
/// <para><b>왜 생겼나(2026-10-08 실사고)</b>: 운영 백오피스에서 CS 창구·CS 백과사전이 500 이었다.
/// 처방은 <c>HITPAN_BO_AUTO_MIGRATE=1</c> 로 한 번 재기동하는 것이었는데, 그렇게 해도
/// <b>표가 안 생길 수 있었다</b>. 적재기는 <c>HITPAN_BO_SCHEMA_DIR</c> 또는
/// <c>AppContext.BaseDirectory/installer/backoffice</c> 를 보는데(<c>Program.cs:166-167</c>),
/// 그 폴더가 <b>없으면 예외를 던지지 않고 「적용할 게 없음」으로 조용히 통과</b>한다
/// (<c>SchemaMigrator.cs:43</c>). 그리고 <c>HitPan.Backoffice.API.csproj</c> 에는 SQL 을
/// 산출물로 복사하는 줄이 <b>0개</b>였고, <c>HITPAN_BO_SCHEMA_DIR</c> 를 설정하는 곳도
/// 레포 전체에 <b>0곳</b>이었다. 실측 확정: 그날 NCP 배포 로그 전문에 <c>.sql</c> 이 <b>0줄</b>
/// — rsync 가 올린 것은 DLL 뿐이었다. 즉 <b>SQL 은 main 에만 있고 서버에는 가지 않았다.</b></para>
///
/// <para><b>이 게이트가 무는 것(글자가 아니라 동작)</b>: 「csproj 에 그 줄이 있나」를 읽지 않는다.
/// <b>빌드 산출물 폴더에 SQL 파일이 실제로 있는지</b>를 파일 시스템으로 본다.
/// 복사 규칙을 지우면 산출물에서 파일이 사라져 이 게이트가 **FAIL** 한다(봉합 전 FAIL 확인 완료).
/// <c>Content</c> 항목은 빌드 산출물과 <c>publish</c> 산출물에 같이 들어가므로,
/// 빌드 산출물에서의 존재가 배포 동봉의 대리 측정이 된다.</para>
///
/// <para>DB 가 없어도 돈다 — <c>build</c> 잡에서 먼저 소리를 낸다.</para>
/// </summary>
public sealed class BackofficeSchemaShipGateTests
{
    /// <summary>레포 루트 — <c>src/HitPan.sln</c> 의 부모.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 대조할 레포 쪽 SQL 을 읽을 수 없다.");
    }

    /// <summary>
    /// 적재기가 보는 기본 경로를 <b>같은 식으로</b> 만든다 — <c>Program.cs:167</c> 와 한 글자도 다르면 안 된다.
    /// (시험 산출물 폴더 = 백오피스 API 어셈블리가 함께 놓이는 폴더이므로 같은 규칙이 적용된다.)
    /// </summary>
    private static string ShippedSchemaDir()
        => Path.Combine(AppContext.BaseDirectory, "installer", "backoffice");

    [Fact(DisplayName = "G-BO-SHIP-1 🔴 빌드 산출물에 백오피스 SQL 폴더가 실제로 있다(없으면 재기동이 조용히 헛돈다)")]
    public void G_BO_SHIP_1_산출물에_스키마_폴더가_있다()
    {
        var dir = ShippedSchemaDir();

        Assert.True(Directory.Exists(dir),
            $"산출물에 백오피스 스키마 폴더가 없다: {dir}\n" +
            "이러면 HITPAN_BO_AUTO_MIGRATE=1 로 재기동해도 SchemaMigrator 가 「적용할 게 없음」으로 " +
            "조용히 통과하고 표는 안 생긴다(2026-10-08 운영 CS 500 의 뿌리). " +
            "HitPan.Backoffice.API.csproj 의 SQL 복사 항목을 지웠는지 확인하라.");

        Assert.NotEmpty(Directory.GetFiles(dir, "*.sql"));
    }

    [Fact(DisplayName = "G-BO-SHIP-2 🔴 레포의 SQL 이 하나도 빠지지 않고 산출물에 실린다(새 번호를 더해도 자동으로 따라온다)")]
    public void G_BO_SHIP_2_레포와_산출물의_SQL_집합이_같다()
    {
        var repoDir = Path.Combine(RepoRoot(), "installer", "backoffice");
        Assert.True(Directory.Exists(repoDir), $"레포 쪽 폴더가 없다: {repoDir}");

        var expected = Directory.GetFiles(repoDir, "*.sql")
            .Select(Path.GetFileName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        var shippedDir = ShippedSchemaDir();
        var actual = Directory.Exists(shippedDir)
            ? Directory.GetFiles(shippedDir, "*.sql")
                .Select(Path.GetFileName)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray()
            : Array.Empty<string?>();

        var missing = expected.Except(actual, StringComparer.Ordinal).ToArray();

        Assert.True(missing.Length == 0,
            "레포에는 있는데 산출물에 안 실린 SQL 이 있다 — 배포가 그 파일을 서버에 안 보낸다:\n  " +
            string.Join("\n  ", missing) +
            "\n(와일드카드 복사 항목이 깨졌는지 확인하라. 번호순 적재라 중간 하나가 빠지면 뒤가 전부 안 들어간다.)");
    }

    [Fact(DisplayName = "G-BO-SHIP-3 🔴 CS 3종(40·41·42)이 이름까지 그대로 실린다 — 500 을 푼 바로 그 파일들")]
    public void G_BO_SHIP_3_CS_세_파일이_실린다()
    {
        var dir = ShippedSchemaDir();
        string[] must =
        {
            "40_backoffice_cs.sql",
            "41_backoffice_cs_kb.sql",
            "42_backoffice_cs_stages.sql",
        };

        var absent = must.Where(f => !File.Exists(Path.Combine(dir, f))).ToArray();

        Assert.True(absent.Length == 0,
            "CS 표를 만드는 SQL 이 산출물에 없다:\n  " + string.Join("\n  ", absent) +
            "\n이 셋이 안 가면 운영 백오피스의 CS 창구·CS 백과사전은 계속 500 이다.");
    }
}
