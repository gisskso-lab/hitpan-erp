using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-CS1 ~ G-CS4</b> — CS 수동 조치 도구가 <b>고객 PC 에 실제로 실리는가</b> (20260818작3).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>왜 생겼나</b> — 2026-08-18 실측. 1.2.84 에서 화면이 무한 반복에 빠져
/// 업데이트 팝업이 뜰 수 없었다. <b>봉합은 게시됐는데 그것을 받을 길이 없었다.</b>
/// 사장님: <i>"수동, 강제로 할 수 있는 길을 열어놔야되. 이건 굉장히 중요한거야."</i>
/// </para>
///
/// <para>
/// 🔴 <b>이 게이트가 무엇을 지키나</b> — 도구가 <b>동작하는가</b>가 아니라
/// <b>고객 PC 에 도착하는가</b>다. 그것이 8/18 에 실제로 끊긴 자리였다:
/// 도구를 만들어 저장소에 뒀는데 <b>고객 PC 에는 없어서</b> CS 가 쓸 수 없었다.
/// ⇒ [[project_fixed_vs_delivered_gap]] 과 같은 계통 — <b>"만들었다 ≠ 갔다"</b>.
/// </para>
///
/// <para>
/// 🔴 <b>기존 게이트의 구멍을 피한다</b>(설계팀 적발). <c>UpdateProcessGateTests</c> 는
/// <i>"인스톨러와 같다"</i> 고 하면서 <c>InlineData</c> 하드코딩만 보고 <c>.iss</c> 를 <b>안 읽는다.</b>
/// 그러면 <c>.iss</c> 가 바뀌어도 초록불이다 — 막는 척만 한다.
/// ⇒ 여기서는 <c>MigrationLocationGuardTests</c> 기법으로 <b>출하 파일을 실제로 읽는다.</b>
/// </para>
///
/// <para>
/// 🟢 <b>초록불이 어디서 오나</b> — DB 도 프레임워크도 안 쓴다. 파일 두 개를 읽어 대조할 뿐이다.
/// ⇒ 막는 것도 통과시키는 것도 <b>오직 내 코드와 출하 파일</b>이다.
/// 그래서 MariaDB 가 없는 CI 에서도 <b>반드시 돈다</b>(조용히 건너뛰지 않는다).
/// </para>
/// </remarks>
public class CsToolShippingGuardTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 시험이 출하 파일을 읽을 수 없다.");
    }

    /// <summary>고객에게 실제로 가는 유일한 설치 정의.</summary>
    private static string IssPath()
        => Path.Combine(FindRepoRoot(), "installer", "HitPan-Universal.iss");

    /// <summary>설치본이 퍼가는 자리. 여기 없으면 EXE 에 안 실린다.</summary>
    private static string PayloadScript()
        => Path.Combine(FindRepoRoot(), "installer", "scripts", "force-update.ps1");

    // ══════════════════════════════════════════════════════════════
    // G-CS1 — 설치 정의가 도구를 싣는다  🔴 본체
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-CS1. 설치 정의(.iss)가 CS 도구를 <c>{app}\scripts</c> 로 싣는다.</b>
    ///
    /// <para>
    /// [무엇이 문제였나] 도구가 저장소 <c>scripts/</c> 에만 있으면 <b>고객 PC 에는 없다.</b>
    /// CS 가 파일을 손으로 옮겨야 하고, 급할 때 그 마찰이 곧 사고가 된다.
    /// </para>
    ///
    /// <para>[반증] <c>.iss</c> 의 <c>Source: "scripts\force-update.ps1"</c> 줄을 지우면 FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-CS1 🔴 설치 정의가 CS 수동 조치 도구를 고객 PC 로 싣는다")]
    public void GCS1_설치정의가_CS도구를_싣는다()
    {
        var iss = IssPath();
        Assert.True(File.Exists(iss), $"출하 설치 정의가 없다: {iss}");

        var text = File.ReadAllText(iss);

        // 🔴 글자 하나가 아니라 **세 조각이 한 줄에** 있어야 실제로 실린다:
        //   ① 어느 파일을 ② 어디로 ③ 실을 것인가.
        //   하나라도 빠지면 Inno Setup 이 안 싣거나 엉뚱한 데 둔다.
        var line = text.Split('\n')
            .FirstOrDefault(l => l.Contains("force-update.ps1", StringComparison.OrdinalIgnoreCase)
                              && l.TrimStart().StartsWith("Source:", StringComparison.OrdinalIgnoreCase));

        Assert.True(
            line is not null,
            "🔴 설치 정의에 CS 도구를 싣는 줄이 없다 — 고객 PC 에 도구가 안 간다. " +
            "화면이 죽어 팝업이 못 뜨는 상황에서 CS 가 손을 쓸 수 없다(2026-08-18 실측 사고). " +
            $"고쳐야 할 파일: {iss}");

        Assert.True(
            line!.Contains(@"{app}\scripts", StringComparison.OrdinalIgnoreCase),
            $"🔴 CS 도구를 싣기는 하는데 자리가 다르다: {line.Trim()}\n" +
            @"다른 PS1 들과 같은 {app}\scripts 여야 CS 가 한 자리만 기억한다.");
    }

    /// <summary>
    /// 🔴 <b>G-CS2. 실을 파일이 실제로 있다.</b>
    ///
    /// <para>
    /// [왜 따로 보나] <c>.iss</c> 에 줄만 있고 파일이 없으면 <b>설치 EXE 빌드가 깨진다.</b>
    /// 그것을 게시 워크플로에서 만나면 그때는 이미 늦다 — 여기서 세운다.
    /// </para>
    ///
    /// <para>[반증] <c>installer/scripts/force-update.ps1</c> 을 지우면 FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-CS2 실을 파일이 installer/scripts 에 실제로 있다")]
    public void GCS2_실을_파일이_있다()
    {
        var p = PayloadScript();
        Assert.True(
            File.Exists(p),
            $"🔴 설치 정의는 이 파일을 싣는다고 하는데 파일이 없다: {p}\n" +
            "설치 EXE 빌드가 깨진다. 저장소 scripts/ 에만 두면 고객에게 안 간다.");

        var len = new FileInfo(p).Length;
        Assert.True(len > 500,
            $"🔴 파일이 너무 작다({len} bytes) — 빈 껍데기가 실려 나갈 수 있다.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-CS3 — 두 벌이 갈리지 않는다  🔴 8/18 교훈
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-CS3. 저장소 사본과 출하 사본이 같다.</b>
    ///
    /// <para>
    /// [왜 필요한가] 같은 도구가 <c>scripts/</c> 와 <c>installer/scripts/</c> 두 자리에 있다.
    /// 🔴 <b>한쪽만 고치는 사고</b>가 히트판에서 여러 번 났다 —
    /// 8/18 에도 같은 봉합이 두 자리에 있고 게이트는 한 자리만 봐서 놓쳤다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ 고친 사람은 대개 <b>저장소 쪽</b>을 고친다(거기서 개발하니까).
    /// 그러면 <b>고객에게는 옛 도구가 간다.</b> 고친 줄 알고 안 간 것이다.
    /// </para>
    ///
    /// <para>[반증] 한쪽만 한 글자 고치면 FAIL.</para>
    /// </summary>
    [Fact(DisplayName = "G-CS3 🔴 저장소 사본과 출하 사본이 갈리지 않는다")]
    public void GCS3_두_사본이_같다()
    {
        var repo = Path.Combine(FindRepoRoot(), "scripts", "force-update.ps1");
        var ship = PayloadScript();

        Assert.True(File.Exists(repo), $"저장소 사본이 없다: {repo}");
        Assert.True(File.Exists(ship), $"출하 사본이 없다: {ship}");

        var a = File.ReadAllBytes(repo);
        var b = File.ReadAllBytes(ship);

        Assert.True(
            a.Length == b.Length && a.SequenceEqual(b),
            "🔴 저장소 사본과 출하 사본이 다르다 — **고친 쪽과 고객에게 가는 쪽이 갈렸다.**\n" +
            $"  저장소: {repo} ({a.Length} bytes)\n" +
            $"  출하  : {ship} ({b.Length} bytes)\n" +
            "고친 사람은 대개 저장소 쪽을 고친다. 그러면 고객에게는 옛 도구가 간다 — " +
            "고친 줄 알고 안 간 것이다. 한쪽을 고쳤으면 다른 쪽에 복사하라.");
    }

    // ══════════════════════════════════════════════════════════════
    // G-CS4 — 도구가 비밀번호를 묻지 않는다  🔴 사장님 결재 1(CS)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>G-CS4. 도구가 DB 비밀번호를 코드에 적어 두지 않고 <c>db.conf</c> 에서 읽는다.</b>
    ///
    /// <para>
    /// [무엇이 문제였나] 2026-08-18 실측: PM 이 개발 PC 값(<c>hitpan</c>/고정 비밀번호)을
    /// 고객 PC 에 그대로 쓰라고 안내했다가 <b>Access denied</b> 를 만났다.
    /// 🔴 <b>CS 는 고객 DB 비밀번호를 모른다. 알아서도 안 된다</b>(헌법 #24 책임 경계).
    /// </para>
    ///
    /// <para>
    /// [반증] 도구에서 <c>db.conf</c> 읽기를 걷어내고 값을 적어 두면 FAIL.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>한계를 정직하게 적는다</b> — 이것은 <b>없어야 할 것이 없는가</b>를 보는 시험이다.
    /// 도구가 <b>실제로 잘 읽는지</b>는 db.conf 가 있는 PC 에서만 알 수 있다(실측 몫).
    /// </para>
    /// </summary>
    [Fact(DisplayName = "G-CS4 🔴 CS 도구가 비밀번호를 적어 두지 않고 db.conf 에서 읽는다")]
    public void GCS4_비밀번호를_적어두지_않는다()
    {
        var text = File.ReadAllText(PayloadScript());

        Assert.True(
            text.Contains("db.conf", StringComparison.OrdinalIgnoreCase),
            "🔴 도구가 db.conf 를 안 읽는다 — 그러면 CS 가 고객 DB 비밀번호를 알아야 한다. " +
            "CS 는 그것을 모르고 알아서도 안 된다(헌법 #24). " +
            "2026-08-18 에 개발 PC 값을 고객 PC 에 쓰라고 안내했다가 Access denied 를 만났다.");

        Assert.True(
            text.Contains("DB_PASSWORD", StringComparison.Ordinal),
            "🔴 db.conf 에서 DB_PASSWORD 를 읽는 코드가 없다 — 값을 어디선가 다른 데서 가져온다는 뜻이다.");
    }
}
