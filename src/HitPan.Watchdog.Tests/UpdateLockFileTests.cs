using System.Globalization;
using HitPan.Watchdog.AutoUpdate;
using Microsoft.Extensions.Logging.Abstractions;

namespace HitPan.Watchdog.Tests;

/// <summary>
/// 작1 W4-1 게이트 G-1 — 업데이트 진행 표식의 fail-safe 규칙 고정 (2026-07-16, 사장님 결재).
///
/// 왜 이 테스트가 중요한가:
///   이 표식은 "워치독 기동 시 keepalive 를 되살려도 되는가"의 판단 근거다.
///   판정이 잘못 '업데이트 중'으로 굳으면 자가 점검이 keepalive 를 영원히 안 켜고,
///   그러면 ERP 가 영영 뜨지 않는다 = 업데이트가 고객 ERP 를 영구 정지시키는 최악의 사고.
///   그래서 규칙은 "만료됐거나, 말이 안 되면 푼다"(fail-safe)여야 하며, 그 규칙을 여기 고정한다.
///   CTO 는 이 판정이 DB 에 있으면 워치독 사망 시 영구 잔존한다고 지적했고, 그래서 파일+TTL 이 됐다.
/// </summary>
public class UpdateLockFileTests : IDisposable
{
    private readonly UpdateLockFile _sut = new(NullLogger<UpdateLockFile>.Instance);

    public void Dispose()
    {
        try { if (File.Exists(_sut.Path)) File.Delete(_sut.Path); } catch { /* 테스트 정리 */ }
        GC.SuppressFinalize(this);
    }

    private void WriteStamp(DateTime utc, string version = "1.2.34")
        => File.WriteAllText(_sut.Path, $"{utc.ToString("O", CultureInfo.InvariantCulture)}|{version}");

    [Fact]
    public void 표식이_없으면_업데이트_중이_아니다()
    {
        if (File.Exists(_sut.Path)) File.Delete(_sut.Path);
        Assert.False(_sut.IsUpdateInProgress());
    }

    [Fact]
    public void 방금_만든_표식은_업데이트_중이다()
    {
        _sut.Acquire("1.2.34");
        Assert.True(_sut.IsUpdateInProgress());
    }

    [Fact]
    public void 해제하면_업데이트_중이_아니다()
    {
        _sut.Acquire("1.2.34");
        _sut.Release();
        Assert.False(_sut.IsUpdateInProgress());
    }

    /// <summary>
    /// 워치독이 교체 중 죽어 표식이 남은 상황. TTL 이 지나면 '방치된 흔적'으로 보고 풀어야 한다.
    /// 이게 안 되면 keepalive 가 영원히 꺼진 채 남는다.
    /// </summary>
    [Fact]
    public void TTL이_지난_표식은_무시한다_비정상종료_흔적()
    {
        WriteStamp(DateTime.UtcNow - UpdateLockFile.Ttl - TimeSpan.FromMinutes(1));
        Assert.False(_sut.IsUpdateInProgress());
    }

    [Fact]
    public void TTL_직전_표식은_아직_유효하다()
    {
        WriteStamp(DateTime.UtcNow - UpdateLockFile.Ttl + TimeSpan.FromMinutes(1));
        Assert.True(_sut.IsUpdateInProgress());
    }

    /// <summary>
    /// CTO 지적: mtime 으로 TTL 을 재면 시계 변경(NTP 동기화·수동 조정)으로 표식이 미래가 될 때
    /// TTL 이 영원히 만료되지 않아 자가 점검이 무력화된다. "말이 안 되면 푼다"가 규칙이다.
    /// </summary>
    [Fact]
    public void 미래_시각_표식은_손상으로_보고_무시한다_시계변경_대비()
    {
        WriteStamp(DateTime.UtcNow.AddHours(2));
        Assert.False(_sut.IsUpdateInProgress());
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("not-a-date|1.2.34")]
    [InlineData("|||")]
    public void 해석할_수_없는_표식은_무시한다_fail_safe(string content)
    {
        File.WriteAllText(_sut.Path, content);

        // fail-safe: 판정 불능이면 '업데이트 중 아님'. 잘못 판정해도 keepalive 가 켜질 뿐이고,
        // 반대(영영 안 켜짐)는 ERP 영구 정지다. 안전한 쪽으로 기운다.
        Assert.False(_sut.IsUpdateInProgress());
    }

    /// <summary>
    /// %LOCALAPPDATA% 회귀 차단 — 워치독은 SYSTEM 이라 그 경로는 systemprofile 로 숨어
    /// CS 도 고객도 표식을 못 찾는다(CTO 처방으로 {app} 루트로 옮김).
    /// </summary>
    [Fact]
    public void 표식_경로는_사람이_찾을_수_있는_곳이다()
    {
        Assert.EndsWith("update.lock", _sut.Path);
        Assert.DoesNotContain("systemprofile", _sut.Path, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AppData", _sut.Path, StringComparison.OrdinalIgnoreCase);
    }
}
