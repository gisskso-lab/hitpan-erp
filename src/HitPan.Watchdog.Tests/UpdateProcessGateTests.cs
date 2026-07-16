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
}
