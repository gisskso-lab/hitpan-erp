using HitPan.Watchdog;
using HitPan.Watchdog.AutoUpdate;

namespace HitPan.Watchdog.Tests;

/// <summary>
/// 작1 W4-0 게이트 G-0 — 버전 판정 규칙 고정 (2026-07-16, 사장님 결재).
///
/// 왜 이 테스트가 필요한가:
///   종전 UpdateClient 는 "manifest 버전 != 현재 버전 → 새 버전"으로 판정했다(string.Equals).
///   그래서 구버전 manifest 를 물리면 다운그레이드가 통과했다. 서명(고리4)을 붙여도 이 구멍은 남는다 —
///   과거에 정식 서명된 옛 manifest 를 통째로 재생(replay)하면 서명·sha256 이 전부 유효하기 때문이다.
///   즉 이 SemVer 비교가 "취약한 구버전으로 되돌린 뒤 그 취약점을 치는" 공격의 유일한 방어선이다.
///   방어선은 반드시 테스트로 고정한다 — 다음 사람이 "문자열 비교가 간단한데?" 하고 되돌리지 못하게.
/// </summary>
public class UpdateVersionCompareTests
{
    [Theory]
    [InlineData("1.2.34", "1.2.33")] // 패치 상승
    [InlineData("1.3.0", "1.2.99")]  // 마이너 상승
    [InlineData("2.0.0", "1.9.9")]   // 메이저 상승
    public void 더_높은_버전이면_업데이트_진행(string feed, string current)
    {
        Assert.True(UpdateClient.IsNewerVersion(feed, current, out var reason));
        Assert.Null(reason);
    }

    [Theory]
    [InlineData("1.2.32", "1.2.33")] // 패치 하락 = 다운그레이드 공격
    [InlineData("1.1.0", "1.2.33")]  // 마이너 하락
    [InlineData("0.9.9", "1.2.33")]  // 메이저 하락
    public void 더_낮은_버전이면_차단한다_다운그레이드_공격(string feed, string current)
    {
        Assert.False(UpdateClient.IsNewerVersion(feed, current, out var reason));
        Assert.Contains("다운그레이드 차단", reason);
    }

    [Fact]
    public void 같은_버전이면_진행하지_않는다_정상()
    {
        Assert.False(UpdateClient.IsNewerVersion("1.2.34", "1.2.34", out var reason));
        Assert.Contains("최신 버전 유지", reason);
    }

    [Theory]
    [InlineData("latest", "1.2.33")]     // feed 가 형식 위반
    [InlineData("1.2.34", "beta")]       // 현재 버전이 형식 위반
    [InlineData("", "1.2.33")]
    [InlineData("../../etc", "1.2.33")]  // 형식을 벗어난 임의 문자열
    public void 버전_형식을_해석할_수_없으면_진행하지_않는다_fail_closed(string feed, string current)
    {
        // fail-closed: "새 버전인지 알 수 없다"면 코드를 교체하지 않는다.
        Assert.False(UpdateClient.IsNewerVersion(feed, current, out var reason));
        Assert.Contains("해석할 수 없습니다", reason);
    }

    /// <summary>
    /// 표기 자릿수가 달라도 같은 버전은 같게 본다 — 무한 재적용 차단 (검증팀 F-4 적발).
    ///
    /// Version.TryParse 는 "1.2.34" 의 Revision 을 -1, "1.2.34.0" 을 0 으로 둔다. 정규화가 없으면
    /// 0 > -1 이라 같은 버전이 "상위"로 판정돼, 이미 최신인 PC 가 같은 버전을 영원히 재적용한다.
    /// Directory.Build.props 가 AssemblyVersion 을 x.y.z.0 으로 굽기 때문에 실수로 밟기 쉬운 함정이다.
    /// </summary>
    [Theory]
    [InlineData("1.2.34.0", "1.2.34")]   // manifest 에 어셈블리 표기(4자리)를 복사한 경우
    [InlineData("1.2.34", "1.2.34.0")]   // 반대 방향
    [InlineData("1.2.0", "1.2")]         // 2자리 표기 = Build 미지정
    public void 표기만_다른_같은_버전은_진행하지_않는다(string feed, string current)
    {
        Assert.False(UpdateClient.IsNewerVersion(feed, current, out var reason));
        Assert.Contains("최신 버전 유지", reason);
    }

    [Theory]
    [InlineData("1.2.35.0", "1.2.34")]   // 4자리 표기라도 진짜 상위면 진행
    [InlineData("1.3", "1.2.34")]        // 2자리 표기 상위 (1.3.0 > 1.2.34)
    public void 표기가_달라도_진짜_상위면_진행한다(string feed, string current)
    {
        Assert.True(UpdateClient.IsNewerVersion(feed, current, out _));
    }

    /// <summary>
    /// 어셈블리 버전 스탬핑(Directory.Build.props)이 실제로 걸렸는지 — 1.0.0 고정 회귀 차단.
    /// 이게 깨지면 워치독이 자기 버전을 1.0.0 으로 착각해 모든 manifest 를 "새 버전"으로 받아들인다
    /// (= 위 다운그레이드 방어가 통째로 무력화된다. W4-0 과 서명은 곱해진다).
    ///
    /// ⚠️ 이 테스트의 역사 (검증팀 F-3 적발, 2026-07-16)
    ///   초판은 VersionInfo 가 GetEntryAssembly() 를 쓰던 탓에 테스트에서 testhost(15.0.0)를 읽었다.
    ///   그래서 "!=0.0.0, !=1.0.0" 단언이 testhost 버전으로 전부 통과했고, 스탬핑을 통째로 지워도
    ///   초록이었다 — 회귀 차단이라 이름 붙인 보호막이 아무것도 막지 않았다.
    ///   VersionInfo 가 typeof(VersionInfo).Assembly 를 보도록 고친 뒤에야 이 테스트가 실제로 워치독
    ///   어셈블리를 검증한다. 아래 단언이 그 사실 자체를 고정한다(호스트 버전을 읽으면 실패한다).
    /// </summary>
    [Fact]
    public void 워치독_버전이_어셈블리에서_스탬핑된다()
    {
        var v = VersionInfo.Current;

        Assert.NotEqual("0.0.0", v);   // 어셈블리를 못 읽은 상태
        Assert.NotEqual("1.0.0", v);   // 스탬핑 누락 시 .NET 기본값
        Assert.True(Version.TryParse(v, out _), $"버전 형식이 Major.Minor.Build 가 아님: '{v}'");

        // VersionInfo 가 호스트(testhost 등)가 아니라 '워치독 어셈블리'를 읽는지 고정.
        // 이 단언이 없으면 F-3(호스트 버전을 읽어 무의미하게 통과)이 조용히 재발한다.
        var watchdogAsmVer = typeof(VersionInfo).Assembly.GetName().Version!;
        Assert.Equal($"{watchdogAsmVer.Major}.{watchdogAsmVer.Minor}.{watchdogAsmVer.Build}", v);
    }
}
