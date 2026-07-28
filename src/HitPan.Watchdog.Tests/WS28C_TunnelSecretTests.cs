using HitPan.Watchdog.Stages;
using Microsoft.Extensions.Logging.Abstractions;

namespace HitPan.Watchdog.Tests;

public class WS28C_TunnelSecretTests
{
    // 봉합 (2026-06-20, WD-04): WS28C 가 로그 부재 시 DetectViaServiceFallback 으로 분기하고,
    //   폴백·RegenerateAsync 모두 HITPAN_TUNNEL_ID 를 Machine ?? Process 로 읽는다(WD-01).
    //   테스트가 이 환경변수를 격리하지 않으면 CI/운영 머신에 변수가 있을 때 비결정적으로 깨진다.
    //   Process·Machine 두 타깃 모두 null 로 격리한 뒤 실행하고, 끝나면 원복한다.
    private static async Task WithTunnelIdCleared(Func<Task> body)
    {
        var savedProcess = Environment.GetEnvironmentVariable("HITPAN_TUNNEL_ID", EnvironmentVariableTarget.Process);
        string? savedMachine = null;
        var machineReadable = false;
        try
        {
            try
            {
                savedMachine = Environment.GetEnvironmentVariable("HITPAN_TUNNEL_ID", EnvironmentVariableTarget.Machine);
                machineReadable = true;
            }
            catch
            {
                // Machine 범위 접근 불가(권한)면 Process 격리만으로 진행 — 폴백은 Machine null 시 Process 로 떨어짐.
                machineReadable = false;
            }

            Environment.SetEnvironmentVariable("HITPAN_TUNNEL_ID", null, EnvironmentVariableTarget.Process);
            await body().ConfigureAwait(false);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HITPAN_TUNNEL_ID", savedProcess, EnvironmentVariableTarget.Process);
            if (machineReadable)
            {
                try { Environment.SetEnvironmentVariable("HITPAN_TUNNEL_ID", savedMachine, EnvironmentVariableTarget.Machine); }
                catch { /* 원복 실패는 테스트 환경 한정 — 무시 */ }
            }
        }
    }

    [Fact]
    public async Task DetectInvalidSecret_NoLog_ReturnsFalse()
    {
        await WithTunnelIdCleared(() =>
        {
            var c = new WS28C_TunnelSecret(NullLogger<WS28C_TunnelSecret>.Instance);
            Assert.False(c.DetectInvalidSecret());
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task RegenerateAsync_NoEnvVar_ReturnsFalse()
    {
        await WithTunnelIdCleared(async () =>
        {
            var c = new WS28C_TunnelSecret(NullLogger<WS28C_TunnelSecret>.Instance);
            var ok = await c.RegenerateAsync();
            Assert.False(ok);
        });
    }
}
