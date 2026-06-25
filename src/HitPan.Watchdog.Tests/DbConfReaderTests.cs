using HitPan.Watchdog;

namespace HitPan.Watchdog.Tests;

/// <summary>
/// 봉합 (2026-06-25, ERP 자가복구 B) 검증: DbConfReader 가 db.conf 의 SLOT_INDEX·API_PORT 를 읽어
/// HitPan.API 엔드포인트의 RestartTask·Url 을 채우는지. RestartTask 가 비면(SLOT_INDEX 부재) 워치독이
/// ERP 를 못 살리므로(2026-06-25 demo 502 구멍의 재발), 이 계약은 자가복구의 핵심이다.
///
/// DbConfReader 는 AppContext.BaseDirectory 기준 ..\db.conf 또는 .\db.conf 를 찾는다(ResolveDbConfPath).
/// 테스트는 실행 폴더(BaseDirectory)에 임시 db.conf 를 만들어 ApplyToOptions 결과를 검증하고, 끝나면 지운다.
/// </summary>
public class DbConfReaderTests : IDisposable
{
    private readonly string _confPath = Path.Combine(AppContext.BaseDirectory, "db.conf");

    private WatchdogOptions NewOptionsWithApiEndpoint()
    {
        var o = new WatchdogOptions();
        o.Processes.HttpEndpoints.Add(new HttpEndpointConfig { Name = "HitPan.API", Url = "http://127.0.0.1:5257/health" });
        o.Processes.HttpEndpoints.Add(new HttpEndpointConfig { Name = "HitPan.Web", Url = "http://127.0.0.1:5234/" });
        return o;
    }

    private void WriteConf(params string[] lines) => File.WriteAllLines(_confPath, lines);

    public void Dispose()
    {
        if (File.Exists(_confPath)) File.Delete(_confPath);
    }

    [Fact]
    public void SlotIndex_SetsRestartTask()
    {
        WriteConf("SLOT_INDEX=1", "API_PORT=5257");
        var o = NewOptionsWithApiEndpoint();

        DbConfReader.ApplyToOptions(o);

        var ep = o.Processes.HttpEndpoints.Single(e => e.Name == "HitPan.API");
        Assert.Equal("HitPan-ERP-API-tenant-1", ep.RestartTask);
        Assert.Equal("http://127.0.0.1:5257/health", ep.Url);
    }

    [Fact]
    public void Slot3_SetsRestartTaskAndPort()
    {
        // 슬롯 3 = 포트 5457 (5257 + 100*(3-1)). RestartTask 와 헬스 포트가 같은 슬롯으로 일관.
        WriteConf("SLOT_INDEX=3", "API_PORT=5457");
        var o = NewOptionsWithApiEndpoint();

        DbConfReader.ApplyToOptions(o);

        var ep = o.Processes.HttpEndpoints.Single(e => e.Name == "HitPan.API");
        Assert.Equal("HitPan-ERP-API-tenant-3", ep.RestartTask);
        Assert.Equal("http://127.0.0.1:5457/health", ep.Url);
    }

    [Fact]
    public void SlotIndex_SetsWebRestartTask()
    {
        // 봉합 (2026-06-25, 배포 전수조사 P0-1): Web(5234)도 RestartTask 배선 — 종전엔 API 만 배선해
        //   Web 죽으면 demo.hitpan.kr(5234) 502 자가복구 0이었다. API·Web 둘 다 같은 슬롯으로 일관.
        WriteConf("SLOT_INDEX=1", "API_PORT=5257");
        var o = NewOptionsWithApiEndpoint();

        DbConfReader.ApplyToOptions(o);

        var web = o.Processes.HttpEndpoints.Single(e => e.Name == "HitPan.Web");
        Assert.Equal("HitPan-ERP-WEB-tenant-1", web.RestartTask);
    }

    [Fact]
    public void NoSlotIndex_LeavesWebRestartTaskEmpty()
    {
        // SLOT_INDEX 부재면 Web 도 RestartTask 빈 값 = 감지만(헛 재기동 차단). API 와 대칭.
        WriteConf("API_PORT=5257", "PRIMARY_DOMAIN=localhost:5234");
        var o = NewOptionsWithApiEndpoint();

        DbConfReader.ApplyToOptions(o);

        var web = o.Processes.HttpEndpoints.Single(e => e.Name == "HitPan.Web");
        Assert.Equal(string.Empty, web.RestartTask);
    }

    [Fact]
    public void NoSlotIndex_LeavesRestartTaskEmpty()
    {
        // 구버전 db.conf·LOCAL: SLOT_INDEX 부재 → RestartTask 빈 값 = 감지만(잘못된 작업명 헛 재기동 차단).
        WriteConf("API_PORT=5257", "PRIMARY_DOMAIN=localhost:5234");
        var o = NewOptionsWithApiEndpoint();

        DbConfReader.ApplyToOptions(o);

        var ep = o.Processes.HttpEndpoints.Single(e => e.Name == "HitPan.API");
        Assert.Equal(string.Empty, ep.RestartTask);
    }

    [Fact]
    public void InvalidSlotIndex_LeavesRestartTaskEmpty()
    {
        // SLOT_INDEX 가 숫자가 아니거나 0 이하면 무시 — 헛 재기동 차단.
        WriteConf("SLOT_INDEX=abc", "API_PORT=5257");
        var o = NewOptionsWithApiEndpoint();

        DbConfReader.ApplyToOptions(o);

        var ep = o.Processes.HttpEndpoints.Single(e => e.Name == "HitPan.API");
        Assert.Equal(string.Empty, ep.RestartTask);
    }
}
