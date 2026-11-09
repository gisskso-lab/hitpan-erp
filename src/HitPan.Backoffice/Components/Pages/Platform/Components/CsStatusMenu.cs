using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;

namespace HitPan.Backoffice.Components.Pages.Platform.Components;

/// <summary>
/// 🔴 20261109작 S4 F14 — CS 상태 칸(CsStatusCell) 전용 목록. 9.3 MudMenu 를 그대로 쓰고,
/// 그린 뒤 초점(9.3 MudMenu.razor.cs L1020 래퍼 · L1026 첫 항목)이 이미 지워진 요소로 가서 나는 JSException 만 받아 기록한다.
/// 그 밖의 예외는 잡지 않는다. 다른 화면(DataGrid 등)에 쓰지 않는다 — 넓히려면 별건 결재.
/// 9.3 판 올림 때 재검: 작업지시서 20261109작 §9.
/// </summary>
public class CsStatusMenu : MudMenu
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
}
