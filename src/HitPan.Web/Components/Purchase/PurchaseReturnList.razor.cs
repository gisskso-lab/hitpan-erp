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
        _rows = await DeliveryService.GetPurchaseReturnListAsync(_startDate, _endDate);
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

        var (ok, error) = await DeliveryService.DeletePurchaseReturnAsync(row.ReturnId);
        if (ok)
        {
            Snackbar.Add($"[{row.ReturnNo}] 삭제되었습니다.", Severity.Success);
            await LoadAsync();
        }
        else
        {
            Snackbar.Add($"삭제 실패: {error}", Severity.Error);
        }
    }

    private async Task BulkDeleteAsync()
    {
        var targets = _selectedRows
            .Where(x => !string.IsNullOrWhiteSpace(x.ReturnId)
                        && string.Equals(x.Status, "draft", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (targets.Count == 0)
        {
            Snackbar.Add("삭제 가능한 draft 상태 반품이 없습니다.", Severity.Warning);
            return;
        }

        var confirm = await DialogService.ShowMessageBoxAsync(
            "반품 일괄 삭제",
            $"선택한 draft 상태 {targets.Count}건을 삭제하시겠습니까?",
            yesText: "삭제", cancelText: "취소");
        if (confirm != true) return;

        var success = 0;
        var failed = new List<(string No, string Reason)>();
        foreach (var row in targets)
        {
            var (ok, error) = await DeliveryService.DeletePurchaseReturnAsync(row.ReturnId);
            if (ok) success++;
            else failed.Add((row.ReturnNo, error ?? "unknown"));
        }

        if (failed.Count == 0)
        {
            Snackbar.Add($"{success}건 삭제 완료.", Severity.Success);
        }
        else
        {
            Snackbar.Add($"성공 {success}건 / 실패 {failed.Count}건. 첫 실패: {failed[0].No} — {failed[0].Reason[..Math.Min(150, failed[0].Reason.Length)]}", Severity.Warning);
        }

        await LoadAsync();
    }
}
