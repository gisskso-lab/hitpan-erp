using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace HitPan.Web.Services;

/// <summary>
/// 🔴 탭 <b>사이</b> 갱신 줄 세우기 (20260928작2 절I · 설계 §13-2 ②).
/// </summary>
/// <remarks>
/// <para>탭마다 WASM 이 따로 돈다 ⇒ <see cref="RefreshGate.Lock"/>(탭 안)만으로는 두 탭이 같은 refresh 를 동시에 쓴다.
/// JS <c>hitpanLock_acquire/_release</c>(<c>storage.js</c> · <c>navigator.locks</c>)로 브라우저 전체에서 한 번에 하나로 줄 세운다.</para>
/// <para>⚠️ <c>navigator.locks</c> 가 없거나(옛 브라우저) 부르기 실패하면 <b>잠금 없이 진행</b>한다 — 탭 안 잠금은 그대로 있다.
/// 막히는 쪽으로 실패하지 않는다(#20). 조용히도 넘기지 않는다(#15 — 로그).</para>
/// </remarks>
public static class RefreshTabLock
{
    /// <summary>잠금을 기다리는 최대 시간(ms). 넘으면 잠금 없이 진행한다(JS 쪽이 판단).</summary>
    private const int WaitMs = 15000;

    public static async Task<IAsyncDisposable> AcquireAsync(IJSRuntime js, ILogger logger)
    {
        var id = 0;
        try
        {
            id = await js.InvokeAsync<int>("hitpanLock_acquire", RefreshGate.TabLockName, WaitMs)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "탭 사이 갱신 잠금을 못 잡았습니다 — 탭 안 잠금만으로 진행합니다.");
        }

        return new Handle(js, id, logger);
    }

    private sealed class Handle(IJSRuntime js, int id, ILogger logger) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            if (id == 0) return;   // 잡은 적 없다(잠금 없이 진행한 경우)
            try
            {
                await js.InvokeVoidAsync("hitpanLock_release", id).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 탭이 닫히면 브라우저가 스스로 푼다. 여기서는 흔적만 남긴다.
                logger.LogWarning(ex, "탭 사이 갱신 잠금 해제 실패(id={Id})", id);
            }
        }
    }
}
