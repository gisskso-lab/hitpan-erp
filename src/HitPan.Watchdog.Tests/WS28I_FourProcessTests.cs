using HitPan.Watchdog;
using HitPan.Watchdog.Stages;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HitPan.Watchdog.Tests;

public class WS28I_FourProcessTests
{
    private sealed class StubHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    [Fact]
    public async Task CheckAllAsync_ReturnsAllConfiguredKeys()
    {
        var opts = Options.Create(new WatchdogOptions
        {
            Processes = new ProcessesConfig
            {
                Services = new List<string> { "__never_exists_xyz__" },
                HttpEndpoints = new List<HttpEndpointConfig>
                {
                    new() { Name = "Stub", Url = "http://127.0.0.1:1/health" }
                }
            }
        });
        var i = new WS28I_FourProcess(NullLogger<WS28I_FourProcess>.Instance, new StubHttpFactory(), opts);
        var result = await i.CheckAllAsync();
        Assert.True(result.ContainsKey("__never_exists_xyz__"));
        Assert.True(result.ContainsKey("Stub"));
        Assert.False(result["__never_exists_xyz__"]);
        Assert.False(result["Stub"]);
    }

    [Fact]
    public void TryRestartService_UnknownService_ReturnsFalse()
    {
        var opts = Options.Create(new WatchdogOptions());
        var i = new WS28I_FourProcess(NullLogger<WS28I_FourProcess>.Instance, new StubHttpFactory(), opts);
        Assert.False(i.TryRestartService("__never_exists_xyz__"));
    }
}
