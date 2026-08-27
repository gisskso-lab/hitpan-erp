using HitPan.Web.Models;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HitPan.Web.Components.Purchase;

/// <summary>
/// 매입반품 목록 다이얼로그 — PurchaseReceiptList 패턴을 계승한다.
/// 행 클릭 시 다이얼로그를 ReturnId 결과로 닫아 호출자(ReturnPage)가 편집 화면에 로드.
/// </summary>
public partial class PurchaseReturnList : ComponentBase
{
    private DateTime? _startDate = DateTime.Today.AddDays(-30);
    private DateTime? _endDate = DateTime.Today;

    private List<PurchaseReturnListItem> _rows = new();
    private List<PurchaseReturnListItem> _selectedRows = new();
    private bool _allSelected;

    [Inject] private DeliveryService DeliveryService { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    [CascadingParameter]
    private IMudDialogInstance? MudDialog { get; set; }

    [Parameter]
    public EventCallback<string> OnReturnSelected { get; set; }

    /// <summary>14차 P0 봉합(B안): true 면 매출반품(api/sales/returns) 목록·삭제를 사용한다.</summary>
    [Parameter]
    public bool IsSalesReturn { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task OnStartDateChangedAsync(DateTime? value)
    {
        _startDate = value;
        await LoadAsync();
    }

    private async Task OnEndDateChangedAsync(DateTime? value)
    {
        _endDate = value;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _rows = IsSalesReturn
            ? await DeliveryService.GetSalesReturnListAsync(_startDate, _endDate)
            : await DeliveryService.GetPurchaseReturnListAsync(_startDate, _endDate);
        foreach (var row in _rows)
        {
            row.IsChecked = false;
        }
        _selectedRows.Clear();
        _allSelected = false;
        await InvokeAsync(StateHasChanged);
    }

    private async Task ToggleAllAsync(bool value)
    {
        _allSelected = value;
        foreach (var row in _rows) row.IsChecked = value;
        _selectedRows = _rows.Where(x => x.IsChecked).ToList();
        await InvokeAsync(StateHasChanged);
    }

    private async Task ToggleOneAsync(PurchaseReturnListItem row, bool value)
    {
        row.IsChecked = value;
        _selectedRows = _rows.Where(x => x.IsChecked).ToList();
        _allSelected = _rows.Count > 0 && _rows.All(x => x.IsChecked);
        await InvokeAsync(StateHasChanged);
    }

    private async Task SelectRowAsync(PurchaseReturnListItem row)
    {
        if (string.IsNullOrWhiteSpace(row.ReturnId)) return;

        if (MudDialog is not null)
        {
            MudDialog.Close(DialogResult.Ok(row.ReturnId));
            return;
        }

        await OnReturnSelected.InvokeAsync(row.ReturnId);
    }

    private async Task DeleteOneAsync(PurchaseReturnListItem row)
    {
        if (string.IsNullOrWhiteSpace(row.ReturnId)) return;

        var confirm = await DialogService.ShowMessageBoxAsync(
            "반품 삭제",
            $"[{row.ReturnNo}] 을(를) 삭제하시겠습니까?\n(확정된 반품은 삭제할 수 없습니다.)",
            yesText: "삭제", cancelText: "취소");
        if (confirm != true) return;

        var (ok, error) = IsSalesReturn
            ? await DeliveryService.DeleteSalesReturnAsync(row.ReturnId)
            : await DeliveryService.DeletePurchaseReturnAsync(row.ReturnId);
        if (ok)
        {
            Snackbar.Add($"[{row.ReturnNo}] 삭제되었습니다.", Severity.Success);
            await LoadAsync();
        }
        else
        {
            // 🔴 20260827작8 W2 — 서버 문장 그대로(확정 반품이면 그 사유가 실려 온다).
            Snackbar.Add($"삭제 불가 — {ApiErrorText.Extract(error)}", Severity.Error,
                cfg => { cfg.RequireInteraction = true; cfg.ShowCloseIcon = true; });
        }
    }

    /// <summary>
    /// 목록에서 바로 반품확정한다 (20260825작10).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 사장님 실측 반려: <i>"목록에는 없고, 반품확인서 전표작성에는 반품확정버튼 있음"</i>.
    /// 작9 에서 전표 화면에만 넣고 목록을 빠뜨렸다.
    /// </para>
    /// <para>
    /// 🔴 <b>서버가 준 사유를 그대로 보여준다.</b> 종전에는 응답 본문(JSON)을 통째로 스낵바에 넣어
    /// <c>{"error":"서버..."}</c> 같은 글자가 사용자에게 그대로 떴다 — 사장님이 받은 그 화면이다.
    /// 사람이 읽을 수 있는 문장만 꺼낸다.
    /// </para>
    /// </remarks>
    private async Task ConfirmOneAsync(PurchaseReturnListItem row)
    {
        if (string.IsNullOrWhiteSpace(row.ReturnId)) return;

        // 🔴 20260825작18 P0 — 확정만 IsSalesReturn 분기가 빠져 있었다.
        //   같은 파일의 삭제(DeleteOneAsync)·일괄삭제는 진작 분기하는데 확정만 무조건
        //   매출 경로로 갔다. 매입반품 ID 는 purchase_returns 에 있어 sales_returns 조회가
        //   반드시 0건 → "반품 문서를 찾을 수 없습니다". 즉 목록에서 매입반품은
        //   확정할 길이 아예 없었고, 그래서 반품현황(=confirmed 집계)도 영영 0건이었다.
        //   ⇒ 사장님 증상 "반품처리가 작동이 안되니 당연(현황도 안 보임)" 의 실제 자리.
        //   안내 문구도 갈라야 한다 — 매입반품은 매출·미수가 아니라 매입·미지급금을 건드린다.
        var effectText = IsSalesReturn
            ? "확정하면 반품 수량이 재고에 반영되고 매출·미수에서 차감됩니다."
            : "확정하면 반품 수량이 재고에서 차감되고 매입·미지급금에서 차감됩니다.";

        var confirm = await DialogService.ShowMessageBoxAsync(
            "반품확정",
            $"[{row.ReturnNo}] 을(를) 확정하시겠습니까?\n\n"
            + effectText + "\n"
            + "확정 후에는 반품현황에 집계됩니다.",
            yesText: "확정", cancelText: "취소");
        if (confirm != true) return;

        var (ok, error) = IsSalesReturn
            ? await DeliveryService.ConfirmSalesReturnAsync(row.ReturnId)
            : await DeliveryService.ConfirmPurchaseReturnAsync(row.ReturnId);
        if (ok)
        {
            Snackbar.Add($"[{row.ReturnNo}] 반품확정 되었습니다.", Severity.Success);
            await LoadAsync();
            return;
        }

        Snackbar.Add($"반품확정 실패: {ApiErrorText.Extract(error)}", Severity.Error);
    }

    // 🔴 20260827작8 W3 — private ExtractMessage 는 ApiErrorText 로 승격했다.
    //    한 화면에만 있으니 나머지 화면이 각자 다르게 처리했고, 그게 1.3.28 반려의 원인이다.

    /// <summary>선택 행 일괄 삭제.</summary>
    /// <remarks>
    /// 🔴 <b>20260827작8 W1 — <c>draft</c> 사전필터를 걷어냈다.</b>
    /// 사장님 지시: <i>"반품확정되서 이미 반품을 보낸 건에 대해선 반품전표에도 …
    /// 삭제가 안되는 거지"</i> — <b>막되, 왜 막혔는지는 알려야 한다.</b>
    /// 화면이 먼저 걸러내면 <b>"없다"</b> 는 엉뚱한 답이 나가고 사유가 사라진다.
    /// <para>
    /// ⚠️ 반품 <b>임시저장</b> 건은 원장이 안 움직였으므로 그대로 삭제된다
    /// (사장님: <i>"반품이 임시확정된 건을 삭제하면?? 그건 반품전표만 삭제지"</i>).
    /// 그 판정도 서버가 한다.
    /// </para>
    /// </remarks>
    private async Task BulkDeleteAsync()
    {
        var targets = _selectedRows
            .Where(x => !string.IsNullOrWhiteSpace(x.ReturnId))
            .ToList();

        if (targets.Count == 0)
        {
            Snackbar.Add("삭제할 반품을 선택해 주세요.", Severity.Warning);
            return;
        }

        var confirm = await DialogService.ShowMessageBoxAsync(
            "반품 일괄 삭제",
            $"선택한 {targets.Count}건을 삭제하시겠습니까? 확정된 반품은 삭제되지 않습니다.",
            yesText: "삭제", cancelText: "취소");
        if (confirm != true) return;

        var success = 0;
        var failed = new List<(string No, string Reason)>();
        foreach (var row in targets)
        {
            var (ok, error) = IsSalesReturn
                ? await DeliveryService.DeleteSalesReturnAsync(row.ReturnId)
                : await DeliveryService.DeletePurchaseReturnAsync(row.ReturnId);
            if (ok) success++;
            else failed.Add((row.ReturnNo, ApiErrorText.Extract(error)));
        }

        if (failed.Count == 0)
        {
            Snackbar.Add($"{success}건 삭제 완료.", Severity.Success);
        }
        else
        {
            var head = success > 0 ? $"{success}건 삭제 · " : string.Empty;
            var lines = string.Join(" / ", failed.Select(f => $"[{f.No}] {f.Reason}"));
            Snackbar.Add($"{head}{failed.Count}건 삭제 불가 — {lines}", Severity.Warning,
                cfg => { cfg.RequireInteraction = true; cfg.ShowCloseIcon = true; });
        }

        await LoadAsync();
    }
}
