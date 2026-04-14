using HitPan.Web.Models;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace HitPan.Web.Components.Sales;

public partial class TransactionGrid : ComponentBase
{
    private MudDataGrid<DeliveryLineModel>? _grid;
    private DeliveryLineModel? _selectedLine;

    [Inject] private ItemMasterService ItemService { get; set; } = default!;

    [Parameter, EditorRequired] public List<DeliveryLineModel> Lines { get; set; } = default!;
    [Parameter] public DeliveryGridDensity Density { get; set; }
    [Parameter] public DeliverySummaryModel Summary { get; set; } = new();
    [Parameter] public EventCallback OnChanged { get; set; }
    [Parameter] public EventCallback<DeliveryLineModel?> SelectedLineChanged { get; set; }
    [Parameter] public string Status { get; set; } = "Draft";

    private bool IsReadOnly => string.Equals(Status, "Confirmed", StringComparison.OrdinalIgnoreCase);

    protected override void OnParametersSet()
    {
        if (Lines.Count == 0)
        {
            Lines.Add(new DeliveryLineModel { No = 1, IsPlaceholder = true });
        }
    }

    private string GetDensityClass() => Density switch
    {
        DeliveryGridDensity.Compact => "delivery-grid--compact",
        DeliveryGridDensity.Comfortable => "delivery-grid--comfortable",
        _ => "delivery-grid--normal"
    };

    private string RowStyle(DeliveryLineModel item, int index) => "height:36px;font-size:13px;";

    private async Task HandleSelectedItemChanged(DeliveryLineModel? line)
    {
        _selectedLine = line;
        await SelectedLineChanged.InvokeAsync(line);
    }

    private async Task<IEnumerable<ItemListModel>> SearchItemsAsync(string keyword, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return Array.Empty<ItemListModel>();
        }

        var items = await ItemService.GetListAsync(search: keyword, group: null, type: null, ct: cancellationToken);
        return items ?? Array.Empty<ItemListModel>();
    }

    private ItemListModel? GetItemFromLine(DeliveryLineModel line)
    {
        if (string.IsNullOrWhiteSpace(line.ItemName))
        {
            return null;
        }

        return new ItemListModel
        {
            ItemId = line.ItemId,
            ItemName = line.ItemName,
            Spec = line.Spec,
            Unit = line.Unit,
            SalePrice = line.UnitPrice
        };
    }

    private async Task OnItemSelectedAsync(DeliveryLineModel line, ItemListModel? item)
    {
        if (item is null)
        {
            line.ItemId = string.Empty;
            line.ItemName = string.Empty;
            line.Spec = string.Empty;
            line.Unit = "EA";
            line.UnitPrice = 0;
        }
        else
        {
            line.ItemId = item.ItemId;
            line.ItemName = item.ItemName;
            line.Spec = item.Spec ?? string.Empty;
            line.Unit = string.IsNullOrWhiteSpace(item.Unit) ? "EA" : item.Unit;
            line.UnitPrice = item.SalePrice;
        }

        line.RecalculateAmount();
        await OnRowChangedAsync();
    }

    private async Task UpdateTextAsync(DeliveryLineModel line, TransactionGridColumn column, string value)
    {
        if (column == TransactionGridColumn.Spec) line.Spec = value;
        if (column == TransactionGridColumn.Unit) line.Unit = value;
        if (column == TransactionGridColumn.Note) line.Note = value;
        await NotifyChangedAsync();
    }

    private async Task UpdateQuantityAsync(DeliveryLineModel line, decimal value)
    {
        line.Qty = value;
        line.RecalculateAmount();
        await OnRowChangedAsync();
    }

    private async Task UpdateUnitPriceAsync(DeliveryLineModel line, decimal value)
    {
        line.UnitPrice = value;
        line.RecalculateAmount();
        await OnRowChangedAsync();
    }

    private async Task HandleCellKeyDownAsync(KeyboardEventArgs args, DeliveryLineModel line, TransactionGridColumn column)
    {
        if (IsReadOnly)
        {
            return;
        }

        if (!string.Equals(args.Key, "Enter", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(args.Key, "Tab", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var isLastColumn = column == TransactionGridColumn.Note;
        var rowIndex = Lines.IndexOf(line);
        var isLastRow = rowIndex == Lines.Count - 1;

        if (string.Equals(args.Key, "Enter", StringComparison.OrdinalIgnoreCase) && isLastColumn && isLastRow)
        {
            await AddNewRowAsync();
            return;
        }

        await MoveSelectionAsync(line, column, args.Key);
    }

    private async Task MoveSelectionAsync(DeliveryLineModel line, TransactionGridColumn column, string key)
    {
        var currentRowIndex = Lines.IndexOf(line);
        if (currentRowIndex < 0)
        {
            return;
        }

        var nextRowIndex = column == TransactionGridColumn.Note
            ? Math.Min(currentRowIndex + 1, Lines.Count - 1)
            : currentRowIndex;

        _selectedLine = Lines[nextRowIndex];
        await SelectedLineChanged.InvokeAsync(_selectedLine);
        await InvokeAsync(StateHasChanged);
    }

    private static TransactionGridColumn GetNextColumn(TransactionGridColumn column) => column switch
    {
        TransactionGridColumn.ItemName => TransactionGridColumn.Spec,
        TransactionGridColumn.Spec => TransactionGridColumn.Unit,
        TransactionGridColumn.Unit => TransactionGridColumn.Qty,
        TransactionGridColumn.Qty => TransactionGridColumn.UnitPrice,
        TransactionGridColumn.UnitPrice => TransactionGridColumn.Note,
        _ => TransactionGridColumn.ItemName
    };

    private async Task AddNewRowAsync()
    {
        Lines.RemoveAll(x => x.IsPlaceholder);
        var nextNo = Lines.Count + 1;
        var row = new DeliveryLineModel { No = nextNo, RowNo = nextNo };
        Lines.Add(row);
        _selectedLine = row;
        await OnRowChangedAsync();
        await SelectedLineChanged.InvokeAsync(row);
    }

    private async Task OnRowChangedAsync()
    {
        RecalculateSummary();
        await NotifyChangedAsync();
    }

    private void RecalculateSummary()
    {
        var dataLines = Lines.Where(x => !x.IsPlaceholder);
        Summary.SupplyAmount = dataLines.Sum(x => x.Amount);
        Summary.VatAmount = dataLines.Sum(x => x.VatAmount);
        Summary.TotalAmount = Summary.SupplyAmount + Summary.VatAmount;
        Summary.TodaySales = Summary.TotalAmount;
        Summary.ClaimAmount = Summary.TotalAmount;
    }

    private async Task NotifyChangedAsync()
    {
        RenumberRows();
        await OnChanged.InvokeAsync();
    }

    private void RenumberRows()
    {
        for (var i = 0; i < Lines.Count; i++)
        {
            Lines[i].No = i + 1;
            Lines[i].RowNo = i + 1;
        }
    }

    private enum TransactionGridColumn
    {
        ItemName,
        Spec,
        Unit,
        Qty,
        UnitPrice,
        Note
    }
}
