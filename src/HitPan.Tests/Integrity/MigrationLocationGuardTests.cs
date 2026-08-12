using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 마이그레이션 SQL 이 <b>실제로 고객에게 가는 자리</b>에 있는지 지키는 게이트
/// (사장님 실측 적발 2026-08-12 — AI 도우미 연동 화면 죽음).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>무엇을 겪고서</b> — AI 3사 확장(1.2.71)을 게시하고 샌드박스가 업데이트까지 받았는데
/// <b>AI 도우미 연동 화면이 "연동 상태를 불러오지 못했습니다" 로 죽었다.</b> 키 입력칸도 안 나왔다.
/// 원인은 마이그레이션 SQL 을 <c>installer/migrations/</c> 에 둔 것이었다. <b>틀린 자리다.</b>
/// </para>
/// <para>
/// 고객 PC 에 실제로 적용되는 마이그레이션은 <c>src/HitPan.API/Migrations/SQL/DB-NN_*.sql</c> 뿐이고,
/// <c>HitPan.API.csproj</c> 가 그 폴더를 게시물(payload)로 복사한다.
/// 다른 자리에 두면 <b>배포본에 안 실려 컬럼이 안 생기고</b>, 새 컬럼을 읽는 SQL 이 그대로 500 을 낸다.
/// </para>
/// <para>
/// ⚠️ <b>왜 아무도 못 잡았나</b> — 빌드 0/0 통과, 시험 286건 통과, ddl-smoke PASS,
/// 게시 워크플로도 success 였다. <b>개발 PC 에서는 내가 손으로 마이그를 돌려놔서 멀쩡했다.</b>
/// 결함은 <b>고객이 화면을 열어야만</b> 드러났다.
/// ⇒ "만들었다 ≠ 고객에게 갔다" 가 또 한 번, <b>이번엔 마이그레이션 경로</b>에서 재발했다.
/// </para>
/// </remarks>
public class MigrationLocationGuardTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 시험이 소스를 읽을 수 없다.");
    }

    /// <summary>고객에게 실제로 가는 유일한 마이그레이션 폴더.</summary>
    private static string RealMigrationDir()
        => Path.Combine(FindRepoRoot(), "src", "HitPan.API", "Migrations", "SQL");

    [Fact]
    public void 마이그레이션은_고객에게_가는_폴더에만_둔다()
    {
        // 🔴 installer/migrations/ 는 배포본에 안 실린다. 여기 SQL 을 두면 고객 DB 는 안 바뀐다.
        var stray = Path.Combine(FindRepoRoot(), "installer", "migrations");
        if (!Directory.Exists(stray)) return;   // 폴더 자체가 없으면 통과

        var strayFiles = Directory.GetFiles(stray, "*.sql", SearchOption.AllDirectories);

        Assert.True(
            strayFiles.Length == 0,
            "installer/migrations/ 에 SQL 이 있다 — 이 폴더는 **배포본에 안 실린다**. "
            + "고객 PC 는 이 SQL 을 영영 못 받아 새 컬럼이 안 생기고 화면이 500 으로 죽는다. "
            + "src/HitPan.API/Migrations/SQL/DB-NN_*.sql 로 옮겨라. "
            + $"발견: {string.Join(" / ", strayFiles.Select(Path.GetFileName))}");
    }

    [Fact]
    public void 마이그레이션_파일명은_DB번호_규칙을_지킨다()
    {
        // 워크플로·ddl-smoke 가 파일명에서 DB-NN 을 뽑아 clean DDL 시드와 대조한다.
        // 이름이 어긋나면 집계에서 빠져 "시드 == 파일" 게이트가 조용히 어긋난다.
        var files = Directory.GetFiles(RealMigrationDir(), "*.sql");
        Assert.NotEmpty(files);

        // DB-08b 처럼 숫자 뒤에 접미문자가 붙는 형태도 정당하다(같은 번호의 후속 마이그).
        var bad = files
            .Select(Path.GetFileName)
            .Where(n => n is not null && !Regex.IsMatch(n!, @"^DB-\d+[a-z]?_.+\.sql$", RegexOptions.IgnoreCase))
            .ToList();

        Assert.True(
            bad.Count == 0,
            "DB-NN_ 규칙을 안 지킨 마이그레이션이 있다 — 시드 대조 집계에서 빠진다. "
            + $"발견: {string.Join(" / ", bad)}");
    }

    [Fact]
    public void 모든_마이그레이션이_clean_DDL_시드에_등록돼_있다()
    {
        // 🔴 신규 설치는 clean DDL 한 방으로 끝난다(헌법 #36). 그때 schema_migrations 에
        //    'DB-NN 이미 적용됨' 시드가 없으면, 설치 직후 자동업데이트가 옛 마이그를 다시 돌린다.
        //    ddl-smoke 5)번 게이트와 같은 취지를 시험으로도 고정한다.
        var files = Directory.GetFiles(RealMigrationDir(), "*.sql")
            .Select(Path.GetFileName)
            .Select(n => Regex.Match(n!, @"^(DB-\d+[a-z]?)_", RegexOptions.IgnoreCase))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value.ToUpperInvariant())
            .Distinct()
            .ToList();

        var ddl = File.ReadAllText(Path.Combine(FindRepoRoot(), "installer", "hitpan_db_clean.sql"));

        var missing = files.Where(db => !ddl.Contains($"'{db}'", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.True(
            missing.Count == 0,
            "clean DDL 의 schema_migrations 시드에 빠진 마이그레이션이 있다 — "
            + "신규 설치 고객이 이미 반영된 마이그를 다시 돌리게 된다. "
            + $"빠진 것: {string.Join(" / ", missing)}");
    }

    [Fact]
    public void DB91_AI3사_마이그레이션이_실제로_있다()
    {
        // 2026-08-12 사고 재발 방지 — 이 건이 다시 엉뚱한 자리로 가지 않게 이름으로 고정한다.
        var path = Path.Combine(RealMigrationDir(), "DB-91_ai_provider_3way.sql");
        Assert.True(File.Exists(path),
            "DB-91(AI 3사 확장) 마이그레이션이 고객에게 가는 폴더에 없다. "
            + "이게 없으면 AI 도우미 연동 화면이 500 으로 죽는다(2026-08-12 실제 발생).");

        var sql = File.ReadAllText(path);

        // 컬럼 11개가 실제로 들어 있나
        foreach (var col in new[] { "openai_api_key_encrypted", "google_api_key_encrypted", "ai_provider" })
        {
            Assert.Contains(col, sql);
        }

        // 무회귀 — 기본값이 anthropic 이어야 기존 고객이 종전대로 동작한다
        Assert.Contains("DEFAULT 'anthropic'", sql);

        // 멱등 — 업데이트 재시도로 두 번 돌 수 있다
        var addLines = sql.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith("--", StringComparison.Ordinal))
            .Where(l => l.Contains("ADD COLUMN", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(addLines);
        foreach (var line in addLines)
        {
            Assert.True(
                line.Contains("IF NOT EXISTS", StringComparison.OrdinalIgnoreCase),
                $"멱등하지 않은 ADD COLUMN 이 있다 — 두 번째 실행에서 죽는다: {line}");
        }

        // 🚨 헌법 #37 — 기존 클로드 컬럼을 지우는 문장이 있으면 안 된다
        Assert.False(
            Regex.IsMatch(sql, @"DROP\s+COLUMN\s+`?anthropic", RegexOptions.IgnoreCase),
            "기존 anthropic_* 컬럼을 지우고 있다 — 헌법 #37 위반. 기존 고객 키가 사라진다.");
    }
}
