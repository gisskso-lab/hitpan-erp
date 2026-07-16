using HitPan.Watchdog.AutoUpdate;

namespace HitPan.Watchdog.Tests;

/// <summary>
/// 작1 W4-1 — 정지 대상 예약작업 이름 규칙 고정 (2026-07-16, 사장님 결재).
///
/// 왜 이름을 테스트로 고정하나:
///   이 이름이 인스톨러(.iss)가 만든 실제 작업명과 한 글자라도 다르면, 업데이트는 keepalive 를
///   '껐다고 착각'한 채 파일 교체로 진입한다. 그러면 1분 뒤 ERP 가 되살아나 dll 을 잠그고 교체가
///   깨진다 — 게다가 schtasks 는 없는 작업에도 오류만 낼 뿐이라 조용히 어긋나기 쉽다.
///   .iss 쪽 이름을 바꾸는 사람이 여기서 걸리도록 규칙을 고정한다.
///
/// 대조 원본(installer/HitPan-Universal.iss):
///   HitPan-ERP-API-tenant-{슬롯}      / HitPan-ERP-WEB-tenant-{슬롯}
///   HitPan-ERP-API-keepalive-{슬롯}   / HitPan-ERP-WEB-keepalive-{슬롯}
/// DbConfReader 도 같은 규칙으로 RestartTask 를 만든다(출처 하나여야 한다).
/// </summary>
public class UpdateProcessGateTests
{
    [Theory]
    [InlineData(1, "HitPan-ERP-API-tenant-1")]
    [InlineData(2, "HitPan-ERP-API-tenant-2")]
    [InlineData(5, "HitPan-ERP-API-tenant-5")]
    public void ERP_API_작업명이_인스톨러와_같다(int slot, string expected)
        => Assert.Equal(expected, UpdateProcessGate.ApiTask(slot));

    [Theory]
    [InlineData(1, "HitPan-ERP-WEB-tenant-1")]
    [InlineData(3, "HitPan-ERP-WEB-tenant-3")]
    public void ERP_Web_작업명이_인스톨러와_같다(int slot, string expected)
        => Assert.Equal(expected, UpdateProcessGate.WebTask(slot));

    [Theory]
    [InlineData(1, "HitPan-ERP-API-keepalive-1")]
    [InlineData(4, "HitPan-ERP-API-keepalive-4")]
    public void keepalive_API_작업명이_인스톨러와_같다(int slot, string expected)
        => Assert.Equal(expected, UpdateProcessGate.ApiKeepalive(slot));

    [Theory]
    [InlineData(1, "HitPan-ERP-WEB-keepalive-1")]
    [InlineData(5, "HitPan-ERP-WEB-keepalive-5")]
    public void keepalive_Web_작업명이_인스톨러와_같다(int slot, string expected)
        => Assert.Equal(expected, UpdateProcessGate.WebKeepalive(slot));

    /// <summary>
    /// 부팅 복원 안전망(② 보장)의 이름. 이 이름이 흔들리면 정리(RemoveRestoreSafetyNet)가 엉뚱한 걸
    /// 지우거나 안전망이 중복 등록된다. 상수로 고정돼 있음을 확인한다.
    /// </summary>
    [Fact]
    public void 부팅_복원_안전망_작업명이_고정돼_있다()
        => Assert.Equal("HitPan-ERP-keepalive-restore", UpdateProcessGate.RestoreTaskName);

    // ─── schtasks 출력 파싱 (③ 자가점검의 판정 근거) ────────────────────────────
    //
    // 왜 이 테스트가 필요한가 (검증팀 R-3 적발):
    //   schtasks 출력은 OS 표시 언어를 따르는데, 개발 PC 와 CI 는 영어라 한국어 분기를 아무도 밟지 않는다.
    //   문자열이 틀려도 테스트가 통과해 회귀를 못 잡는다 — 고객 PC 는 대부분 한국어 Windows 다.
    //   틀리면 ③ 자가점검이 "꺼진 keepalive"를 영원히 못 알아보고, ERP 가 안 뜬 채 방치된다.
    //   ※ 아래 한국어 출력은 검증팀이 ko-KR 실제 렌더링으로 확인한 문자열이다(띄어쓰기 포함).

    [Fact]
    public void 영어_Windows에서_사용안함을_알아본다()
    {
        var output = @"
폴더: \
TaskName:                             \HitPan-ERP-API-keepalive-1
Next Run Time:                        N/A
Status:                               Disabled
";
        Assert.True(UpdateProcessGate.ParseIsDisabled(output));
    }

    [Fact]
    public void 한국어_Windows에서_사용안함을_알아본다()
    {
        var output = @"
폴더: \
TaskName:                             \HitPan-ERP-API-keepalive-1
다음 실행 시간:                       N/A
상태:                                 사용 안 함
";
        Assert.True(UpdateProcessGate.ParseIsDisabled(output));
    }

    [Theory]
    [InlineData("Status:  Ready")]           // 영어 — 정상 가동
    [InlineData("상태:    준비")]             // 한국어 — 정상 가동
    [InlineData("Status:  Running")]
    [InlineData("")]
    public void 정상_가동_상태를_사용안함으로_오판하지_않는다(string output)
    {
        // 오판하면 자가점검이 멀쩡한 keepalive 를 건드린다(무해하나 로그 소음).
        // 진짜 위험은 반대 방향이라 이쪽은 오탐만 막으면 된다.
        Assert.False(UpdateProcessGate.ParseIsDisabled(output));
    }
}
