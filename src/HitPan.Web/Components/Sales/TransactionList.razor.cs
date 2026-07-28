using HitPan.Web.Models;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Components;

namespace HitPan.Web.Components.Sales;

public partial class TransactionList : ComponentBase
{
    private DateTime? _startDate = DateTime.Today.AddDays(-7);
    private DateTime? _endDate = DateTime.Today;
    private PartnerSearchResult? _partner;
    private string _status = "draft";
    private List<DeliveryListDto> _rows = new();
    private List<DeliveryListDto> _selectedRows = new();
    private bool _allSelected;

    private decimal _selectedSupplyAmount;
    private decimal _selectedVatAmount;
    private decimal _selectedTotalAmount;

    [Inject] private DeliveryService DeliveryService { get; set; } = default!;

    [Parameter] public EventCallback<string> OnOrderSelected { get; set; }
    [Parameter] public string? SelectedOrderId { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task OnStartDateChanged(DateTime? value)
    {
        _startDate = value;
        await LoadAsync();
    }

    private async Task OnEndDateChanged(DateTime? value)
    {
        _endDate = value;
        await LoadAsync();
    }

    private async Task OnPartnerChanged(PartnerSearchResult? value)
    {
        _partner = value;
        await LoadAsync();
    }

    private async Task OnStatusChanged(string value)
    {
        _status = value;
        await LoadAsync();
    }

    private async Task<IEnumerable<PartnerSearchResult>> SearchPartnersAsync(string keyword, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return Array.Empty<PartnerSearchResult>();
        }

        var list = await DeliveryService.SearchPartnersAsync(keyword, ct);
        return list;
    }

    private async Task LoadAsync()
    {
        var startDate = _startDate?.ToString("yyyy-MM-dd");
        var endDate = _endDate?.ToString("yyyy-MM-dd");
        var partnerId = _partner?.PartnerId;

        _rows = await DeliveryService.GetListAsync(startDate, endDate, partnerId, _status);

        foreach (var row in _rows)
        {
            row.IsChecked = false;
        }

        _selectedRows.Clear();
        _allSelected = false;
        RecalculateSelectedSummary();
        await InvokeAsync(StateHasChanged);
    }

    private async Task ToggleSelectAllAsync(bool value)
    {
        _allSelected = value;
        foreach (var row in _rows)
        {
            row.IsChecked = value;
        }

        _selectedRows = _rows.Where(x => x.IsChecked).ToList();
        RecalculateSelectedSummary();
        // 외부 툴바 버튼 Disabled 즉시 갱신.
        await InvokeAsync(StateHasChanged);
    }

    private async Task ToggleOneAsync(DeliveryListDto row, bool value)
    {
        row.IsChecked = value;
        _selectedRows = _rows.Where(x => x.IsChecked).ToList();
        _allSelected = _rows.Count > 0 && _rows.All(x => x.IsChecked);
        RecalculateSelectedSummary();
        await InvokeAsync(StateHasChanged);
    }

    private async Task BulkConfirmSelectedAsync()
    {
        var ids = _selectedRows
            .Where(x => !string.IsNullOrWhiteSpace(x.DeliveryId))
            .Select(x => x.DeliveryId)
            .ToList();

        if (ids.Count == 0)
        {
            return;
        }

        var result = await DeliveryService.BulkConfirmAsync(ids);
        var successSet = result.Success.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in _rows)
        {
            if (successSet.Contains(row.DeliveryId))
            {
                row.Status = "confirmed";
                row.IsChecked = false;
            }
        }

        _selectedRows = _rows.Where(x => x.IsChecked).ToList();
        _allSelected = _rows.Count > 0 && _rows.All(x => x.IsChecked);
        RecalculateSelectedSummary();
        await InvokeAsync(StateHasChanged);
    }

    private void RecalculateSelectedSummary()
    {
        _selectedSupplyAmount = _selectedRows.Sum(x => x.SupplyAmount);
        _selectedVatAmount = _selectedRows.Sum(x => x.VatAmount);
        _selectedTotalAmount = _selectedRows.Sum(x => x.TotalAmount);
    }

    private async Task SelectRowAsync(DeliveryListDto row)
    {
        if (string.IsNullOrWhiteSpace(row.DeliveryId))
        {
            return;
        }

        await OnOrderSelected.InvokeAsync(row.DeliveryId);
    }

    private string GetOrderNoClass(DeliveryListDto row)
    {
        return string.Equals(row.DeliveryId, SelectedOrderId, StringComparison.OrdinalIgnoreCase)
            ? "font-weight-bold text-primary"
            : string.Empty;
    }
}
