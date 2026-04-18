using HitPan.Web.Models;
using Microsoft.AspNetCore.Components;

namespace HitPan.Web.Components.Sales;

/// <summary>
/// 견적서 인라인 편집 그리드다.
/// </summary>
public partial class QuotationGrid : ComponentBase
{
    // 헤더 전체선택 체크 상태다.
    private bool _allSelected;

    // 상태값으로 읽기 전용 여부를 판정한다.
    private bool IsReadOnly => string.Equals(Status, "converted", StringComparison.OrdinalIgnoreCase);

    /// <summary>라인 목록이다.</summary>
    [Parameter, EditorRequired] public List<QuotationLineModel> Lines { get; set; } = default!;

    /// <summary>그리드 밀도다.</summary>
    [Parameter] public QuotationGridDensity Density { get; set; } = QuotationGridDensity.Normal;

    /// <summary>요약 정보다.</summary>
    [Parameter] public QuotationSummaryModel Summary { get; set; } = new();

    /// <summary>상태 문자열이다.</summary>
    [Parameter] public string Status { get; set; } = "draft";

    /// <summary>변경 콜백이다.</summary>
    [Parameter] public EventCallback OnChanged { get; set; }

    /// <summary>선택 라인 변경 콜백이다.</summary>
    [Parameter] public EventCallback<QuotationLineModel?> SelectedLineChanged { get; set; }

    /// <summary>선택 상태 변경 콜백이다.</summary>
    [Parameter] public EventCallback OnSelectionChanged { get; set; }

    /// <summary>품목 캐시 목록이다.</summary>
    [Parameter] public List<ItemListModel>? ItemCache { get; set; }

    /// <summary>
    /// 파라미터 수신 시 플레이스홀더를 보장한다.
    /// </summary>
    protected override void OnParametersSet()
    {
        if (Lines.Count == 0)
        {
            Lines.Add(new QuotationLineModel { No = 1, SortOrder = 1, IsPlaceholder = true });
        }
    }

    /// <summary>
    /// 전체 선택을 토글한다.
    /// </summary>
    private async Task ToggleAllAsync(bool value)
    {
        _allSelected = value;
        foreach (var line in Lines.Where(x => !x.IsPlaceholder))
        {
            line.IsSelected = value;
        }

        await OnSelectionChanged.InvokeAsync();
    }

    /// <summary>
    /// 단일 라인 선택을 토글한다.
    /// </summary>
    private async Task ToggleOneAsync(QuotationLineModel line, bool value)
    {
        line.IsSelected = value;
        _allSelected = Lines.Where(x => !x.IsPlaceholder).All(x => x.IsSelected);
        await OnSelectionChanged.InvokeAsync();
    }

    /// <summary>
    /// 라인 선택을 상위로 전달한다.
    /// </summary>
    private async Task SelectRowAsync(QuotationLineModel line)
    {
        await SelectedLineChanged.InvokeAsync(line);
    }

    /// <summary>
    /// 품목 자동완성 검색을 수행한다.
    /// </summary>
    private Task<IEnumerable<string>> SearchItemAsync(string value, CancellationToken ct)
    {
        if (ItemCache is null) return Task.FromResult(Enumerable.Empty<string>());
        if (string.IsNullOrWhiteSpace(value)) return Task.FromResult(ItemCache.Select(i => i.ItemName));
        return Task.FromResult(ItemCache.Where(i => i.ItemName.Contains(value, StringComparison.OrdinalIgnoreCase)).Select(i => i.ItemName));
    }

    /// <summary>
    /// 품목 선택 시 규격·단위·단가를 자동 채운다.
    /// </summary>
    private void AutoFillItem(QuotationLineModel line, string itemName)
    {
        if (ItemCache is null) return;
        var item = ItemCache.FirstOrDefault(i => i.ItemName == itemName);
        if (item is null) return;
        line.ItemId = item.ItemId;
        line.Spec = item.Spec ?? "";
        line.Unit = item.Unit;
        line.UnitPrice = item.SalePrice;
    }

    /// <summary>
    /// 품목명을 갱신한다.
    /// </summary>
    private async Task UpdateItemNameAsync(QuotationLineModel line, string value)
    {
        line.ItemName = value;
        line.IsPlaceholder = false;
        await NotifyChangedAsync();
    }

    /// <summary>
    /// 규격을 갱신한다.
    /// </summary>
    private async Task UpdateSpecAsync(QuotationLineModel line, string value)
    {
        line.Spec = value;
        line.IsPlaceholder = false;
        await NotifyChangedAsync();
    }

    /// <summary>
    /// 단위를 갱신한다.
    /// </summary>
    private async Task UpdateUnitAsync(QuotationLineModel line, string value)
    {
        line.Unit = value;
        line.IsPlaceholder = false;
        await NotifyChangedAsync();
    }

    /// <summary>
    /// 수량을 갱신한다.
    /// </summary>
    private async Task UpdateQtyAsync(QuotationLineModel line, decimal value)
    {
        line.Qty = value;
        line.IsPlaceholder = false;
        line.RecalculateAmount();
        await NotifyChangedAsync();
    }

    /// <summary>
    /// 단가를 갱신한다.
    /// </summary>
    private async Task UpdateUnitPriceAsync(QuotationLineModel line, decimal value)
    {
        line.UnitPrice = value;
        line.IsPlaceholder = false;
        line.RecalculateAmount();
        await NotifyChangedAsync();
    }

    /// <summary>
    /// 할인율을 갱신한다.
    /// </summary>
    private async Task UpdateDiscountRateAsync(QuotationLineModel line, decimal value)
    {
        line.DiscountRate = value;
        line.IsPlaceholder = false;
        line.RecalculateAmount();
        await NotifyChangedAsync();
    }

    /// <summary>
    /// 비고를 갱신한다.
    /// </summary>
    private async Task UpdateNoteAsync(QuotationLineModel line, string value)
    {
        line.Note = value;
        line.IsPlaceholder = false;
        await NotifyChangedAsync();
    }

    /// <summary>
    /// 변경사항을 상위에 전달하고 합계를 갱신한다.
    /// </summary>
    private async Task NotifyChangedAsync()
    {
        for (var i = 0; i < Lines.Count; i++)
        {
            Lines[i].No = i + 1;
            Lines[i].SortOrder = i + 1;
        }

        var data = Lines.Where(x => !x.IsPlaceholder).ToList();
        Summary.SupplyAmount = data.Sum(x => x.Amount);
        Summary.VatAmount = data.Sum(x => x.VatAmount);
        Summary.TotalAmount = Summary.SupplyAmount + Summary.VatAmount;
        await OnChanged.InvokeAsync();
    }
}
