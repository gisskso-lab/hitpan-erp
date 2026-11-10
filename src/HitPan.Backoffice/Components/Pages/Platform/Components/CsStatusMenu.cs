using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;

namespace HitPan.Backoffice.Components.Pages.Platform.Components;

/// <summary>
/// 🔴 20261109작 S4 F14 — CS 상태 칸(CsStatusCell) 전용 목록. 9.3 MudMenu 를 그대로 쓰고,
/// 그린 뒤 초점(9.3 MudMenu.razor.cs L1020 래퍼 · L1026 첫 항목)이 이미 지워진 요소로 가서 나는 JSException 만 받아 기록한다.
/// 그 밖의 예외는 잡지 않는다. 다른 화면(DataGrid 등)에 쓰지 않는다 — 넓히려면 별건 결재.
/// 9.3 판 올림 때 재검: 작업지시서 20261109작 §9.
/// 🔴 20261110작 S4 F14 2차(A′) — 이 메뉴의 이벤트 입구(IHandleEvent)를 다시 구현한다. .NET 8 ComponentBase 처리를 그대로 옮기고,
/// ↑ · ↓ 키 처리에서 난 JSException(사라진 항목으로 가는 초점)만 받아 기록한 뒤 목록을 닫는다. 그 밖의 이벤트 · 예외는 고침 전과 같다.
/// .NET · 9.3 판 올림 때 재검: 작업지시서 20261110작 §9.
/// </summary>
// ⬛ 20261110작 S4 F14 2차 — 옛 줄(이벤트 입구 재구현 전):
// public class CsStatusMenu : MudMenu
public class CsStatusMenu : MudMenu, IHandleEvent
{
    [Inject] private ILogger<CsStatusMenu> FocusSkipLog { get; set; } = default!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            await base.OnAfterRenderAsync(firstRender);
        }
        catch (JSException ex)
        {
            FocusSkipLog.LogWarning(ex, "CS 상태 칸 목록 — 사라진 요소로 가는 초점 한 번 건너뜀");
        }
    }

    // 🔴 20261110작 S4 F14 2차(A′) — .NET 8 ComponentBase 의 IHandleEvent 명시 구현을 그대로 옮김(작업지시서 §3-4).
    //    · 다른 점은 아래 AwaitEventThenRenderAsync 의 ↑ · ↓ JSException 한 칸뿐 — 그 밖 이벤트 · 예외는 원문과 같은 길.
    Task IHandleEvent.HandleEventAsync(EventCallbackWorkItem item, object? arg)
    {
        var task = item.InvokeAsync(arg);
        var shouldAwaitTask = task.Status != TaskStatus.RanToCompletion &&
            task.Status != TaskStatus.Canceled;

        StateHasChanged();

        return shouldAwaitTask ?
            AwaitEventThenRenderAsync(task, arg) :
            Task.CompletedTask;
    }

    private async Task AwaitEventThenRenderAsync(Task task, object? arg)
    {
        try
        {
            await task;
        }
        catch (JSException ex) when (IsArrowUpOrDown(arg))
        {
            // 9.3 L872 → L1046 → L1062 초점이 버려진 항목으로 감 — 기록 후 목록을 닫아 버려진 항목을 걷어낸다(대표 A′).
            FocusSkipLog.LogWarning(ex, "CS 상태 칸 목록 — 화살표 키 초점이 사라진 항목으로 가서 목록을 닫음");
            await CloseAfterArrowFocusSkipAsync();
            return;
        }
        catch
        {
            if (task.IsCanceled)
            {
                return;
            }

            throw;
        }

        StateHasChanged();
    }

    // 🔴 20261110작 S4 F14 2차 — 닫기 안의 단추 초점 되돌림이 회로 끊김 · JS 시한으로 못 끝날 때만 기록(칸 CloseAllMenusLoggedAsync 와 같은 두 예외).
    private async Task CloseAfterArrowFocusSkipAsync()
    {
        try
        {
            await CloseAllMenusAsync();
        }
        catch (Exception ex) when (ex is JSDisconnectedException or OperationCanceledException)
        {
            FocusSkipLog.LogWarning(ex, "CS 상태 칸 목록 — 화살표 키 초점 건너뜀 뒤 목록 닫기를 마치지 못함");
        }
    }

    private static bool IsArrowUpOrDown(object? arg) =>
        arg is Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key: "ArrowUp" or "ArrowDown" };
}
