using System.Globalization;
using HitPan.Watchdog.AutoUpdate;
using Microsoft.Extensions.Logging.Abstractions;

namespace HitPan.Watchdog.Tests;

/// <summary>
/// 20260807작2 N-10 검증리뷰서 §6 미시험 C·D 해소 (2026-08-09, 사장님 결재 "1번" — C·D 먼저).
///
/// ■ 왜 이 테스트가 지금 생겼나
///   검증리뷰서가 채점표를 "통과 5 / 미시험 4 / 실패 0" 으로 냈고, 미시험 4건 중
///   C(파일 손상 시 안전측)·D(재시작 폭주 차단)는 **백지환경 없이도 확인 가능**하다고 적었다.
///   그런데 실측 결과 <see cref="UpdateCheckStampFile"/> 전용 테스트가 **0건**이었다 —
///   그래서 미시험으로 남아 있던 것이다. 코드 독해로는 "성립한다"까지만 말할 수 있다.
///
/// ■ 이 테스트가 지키는 것 — fail-open 방향
///   이 파일의 판정은 **틀리면 "더 확인"하는 쪽으로** 무너져야 한다(설계서 A-2).
///   반대 방향(판정 실패 → 확인 안 함)으로 무너지면 고객이 옛 버전에 영구히 고정되고,
///   화면·로그·경보 어디에도 안 나타난다. 그 침묵이 N-10 사고의 본체였다.
///   ⚠️ manifest **서명 검증**은 정반대(fail-closed — UpdateClient)다. 두 방향을 혼동하지 말 것.
///
/// ■ 여기서 시험하지 않는 것
///   A(재시작 없이 새 버전 발견)·B(N시간 경과 후 재확인)는 실제 게시본과 시간 경과가 필요해
///   백지환경 실측으로 남는다. 이 테스트는 그것을 대신하지 않는다.
///   개발 PC 정상작동은 검증이 아니다(사장님 2026-07-06) — C·D 는 **파일 조작 판정**이라
///   환경 의존이 없어 여기서 확정할 수 있는 범위다.
/// </summary>
public class UpdateCheckStampFileTests : IDisposable
{
    private readonly UpdateCheckStampFile _sut = new(NullLogger<UpdateCheckStampFile>.Instance);

    public void Dispose()
    {
        try { if (File.Exists(_sut.Path)) File.Delete(_sut.Path); } catch { /* 테스트 정리 */ }
        GC.SuppressFinalize(this);
    }

    private void WriteRaw(string content) => File.WriteAllText(_sut.Path, content);

    // ── 미시험 C — 파일 손상 시 안전측으로 도는가 ────────────────────────────────

    [Fact]
    public void C_파일이_없으면_확인한적_없음이다()
    {
        // 첫 기동·파일 삭제. 사고가 아니라 정상 상태 중 하나이며, 즉시 확인해야 한다.
        if (File.Exists(_sut.Path)) File.Delete(_sut.Path);
        Assert.Null(_sut.ReadLastCheckedUtc());
    }

    [Theory]
    [InlineData("쓰레기문자열")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2026-13-45T99:99:99")]   // 형식은 날짜 같지만 실재하지 않는 값
    [InlineData("\0\0\0")]                 // 널 바이트 — 디스크 손상 흉내
    public void C_해석할수_없으면_확인한적_없음이다(string garbage)
    {
        // 🔴 이것이 fail-open 의 핵심이다. 깨진 파일이 업데이트를 영영 막으면 안 된다.
        WriteRaw(garbage);
        Assert.Null(_sut.ReadLastCheckedUtc());
    }

    [Fact]
    public void C_미래_시각이_적혀있으면_확인한적_없음이다()
    {
        // 시계 변경·NTP 보정·손상. 여기서 안 걸러내면 그 시각이 지날 때까지 업데이트가 영구 정지한다
        //   (설계서 A-3 가 지적한 함정 — 검증리뷰서 반증 2 가 "가장 잘 만들어졌다"고 평가한 방어).
        WriteRaw(DateTime.UtcNow.AddDays(30).ToString("O", CultureInfo.InvariantCulture));
        Assert.Null(_sut.ReadLastCheckedUtc());
    }

    [Fact]
    public void C_손상된_파일을_읽어도_예외를_던지지_않는다()
    {
        // 판정 실패가 워치독 루프를 죽이면 자가 회복 전체가 멈춘다(헌법 #30).
        WriteRaw("깨진값");
        var ex = Record.Exception(() => _sut.ReadLastCheckedUtc());
        Assert.Null(ex);
    }

    [Fact]
    public void C_쓰기가_실패해도_예외를_던지지_않는다()
    {
        // 🔴 작업지시서 §8-1: "파일 쓰기 실패가 업데이트를 막으면 P0."
        //   경로를 디렉터리로 만들어 쓰기를 물리적으로 실패시킨다.
        var path = _sut.Path;
        if (File.Exists(path)) File.Delete(path);
        Directory.CreateDirectory(path);   // 같은 이름의 폴더 → File.WriteAllText 는 반드시 실패한다
        try
        {
            var ex = Record.Exception(() => _sut.WriteCheckedNow(DateTime.UtcNow));
            Assert.Null(ex);   // 삼키고 로그만 남겨야 한다
        }
        finally
        {
            try { Directory.Delete(path, recursive: true); } catch { /* 테스트 정리 */ }
        }
    }

    // ── 미시험 D — 재시작 폭주 차단 (기록이 재시작을 건너 살아남는가) ──────────────

    [Fact]
    public void D_기록한_시각을_다시_읽을수_있다()
    {
        // D 의 전제. 이것이 깨지면 재시작마다 "확인한 적 없음"이 되어 폭주 상한이 사라진다.
        var now = DateTime.UtcNow;
        _sut.WriteCheckedNow(now);

        var read = _sut.ReadLastCheckedUtc();
        Assert.NotNull(read);
        // "O" round-trip 이므로 초 단위 이하까지 보존된다. 1초 오차만 허용한다.
        Assert.True(Math.Abs((read!.Value - now).TotalSeconds) < 1,
            $"기록/판독 오차가 큽니다: wrote={now:o} read={read:o}");
    }

    [Fact]
    public void D_새_인스턴스가_같은_기록을_읽는다_재시작_모사()
    {
        // 🔴 이것이 D 의 본체다. 프로세스 재시작 = 새 인스턴스가 디스크에서 다시 읽는 것.
        //   종전 결함(_lastUpdateCheckDate)은 **인메모리**라 재시작하면 통째로 날아갔고,
        //   그래서 `sc stop`/`sc start` 가 "우연히" 게이트를 풀었다.
        var now = DateTime.UtcNow;
        _sut.WriteCheckedNow(now);

        // 같은 경로를 보는 별도 인스턴스 — 재시작 후 기동을 모사한다.
        var afterRestart = new UpdateCheckStampFile(NullLogger<UpdateCheckStampFile>.Instance);
        Assert.Equal(_sut.Path, afterRestart.Path);   // 경로 결정이 결정론적이어야 한다

        var read = afterRestart.ReadLastCheckedUtc();
        Assert.NotNull(read);
        Assert.True(Math.Abs((read!.Value - now).TotalSeconds) < 1,
            "재시작을 건너 기록이 살아남지 않으면 재시작 폭주 상한이 없다.");
    }

    [Fact]
    public void D_연속_재기동_5회에도_기록이_유지된다()
    {
        // 검증리뷰서 §6-D: "sc stop/start 연속 5회 → manifest 조회가 0회인지".
        //   조회 여부는 Worker 게이트가 판정하고, 그 판정의 근거가 이 기록이다.
        //   여기서는 **근거가 5회 재기동을 견디는가**를 확정한다.
        var now = DateTime.UtcNow;
        _sut.WriteCheckedNow(now);

        for (var i = 0; i < 5; i++)
        {
            var restarted = new UpdateCheckStampFile(NullLogger<UpdateCheckStampFile>.Instance);
            var read = restarted.ReadLastCheckedUtc();
            Assert.NotNull(read);
            Assert.True(Math.Abs((read!.Value - now).TotalSeconds) < 1,
                $"{i + 1}회차 재기동에서 기록이 흔들렸다.");
        }
    }

    [Fact]
    public void D_로컬시각으로_적혀도_UTC로_정규화된다()
    {
        // 파일을 시간대가 다른 PC 로 옮기거나 구버전이 로컬로 적었을 때, 해석이 흔들리면
        //   경과 계산이 통째로 틀어져 게이트가 몇 시간씩 어긋난다.
        var localNow = DateTime.Now;
        WriteRaw(localNow.ToString("O", CultureInfo.InvariantCulture));

        var read = _sut.ReadLastCheckedUtc();
        Assert.NotNull(read);
        Assert.Equal(DateTimeKind.Utc, read!.Value.Kind);
        Assert.True(Math.Abs((read.Value - localNow.ToUniversalTime()).TotalSeconds) < 1,
            "로컬 시각이 UTC 로 정규화되지 않았다.");
    }

    // ── 경계 — 검증리뷰서 반증 1(경계값)을 실행으로 고정 ──────────────────────────

    [Fact]
    public void 과거_시각은_그대로_읽힌다()
    {
        // 정상 경로. 미래 방어(위 C)가 과잉 동작해 정상 기록까지 버리면 매 루프 재조회가 된다.
        var past = DateTime.UtcNow.AddHours(-3);
        _sut.WriteCheckedNow(past);

        var read = _sut.ReadLastCheckedUtc();
        Assert.NotNull(read);
        Assert.True(Math.Abs((read!.Value - past).TotalSeconds) < 1);
    }
}
