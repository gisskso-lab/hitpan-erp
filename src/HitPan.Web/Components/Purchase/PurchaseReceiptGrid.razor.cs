using HitPan.Web.Models;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace HitPan.Web.Components.Purchase;

/// <summary>
/// 매입명세 라인 입력용 MudDataGrid 기반 컴포넌트.
/// 발주 그리드(PurchaseOrderGrid)와 동일한 키보드·자동완성 패턴을 계승하고 입고창고 열을 추가한다.
/// </summary>
public partial class PurchaseReceiptGrid : ComponentBase
{
    // 현재 선택 행
    private DeliveryLineModel? _selectedLine;

    // 확정 상태면 읽기 전용으로 전환한다.
    private bool IsReadOnly => string.Equals(Status, "Confirmed", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 품목 마스터 조회 서비스.
    /// </summary>
    [Inject]
    private ItemMasterService ItemMasterService { get; set; } = default!;

    /// <summary>
    /// 라인 컬렉션(상위 Draft 와 바인딩).
    /// </summary>
    [Parameter, EditorRequired]
    public List<DeliveryLineModel> Lines { get; set; } = default!;

    /// <summary>
    /// 그리드 밀도.
    /// </summary>
    [Parameter]
    public DeliveryGridDensity Density { get; set; }

    /// <summary>
    /// 합계 요약(상위와 동일 인스턴스를 공유).
    /// </summary>
    [Parameter]
    public DeliverySummaryModel Summary { get; set; } = new();

    /// <summary>
    /// 라인 변경 알림.
    /// </summary>
    [Parameter]
    public EventCallback OnChanged { get; set; }

    /// <summary>
    /// 선택 라인 변경 알림.
    /// </summary>
    [Parameter]
    public EventCallback<DeliveryLineModel?> SelectedLineChanged { get; set; }

    /// <summary>
    /// 문서 상태 문자열.
    /// </summary>
    [Parameter]
    public string Status { get; set; } = "Draft";

    /// <summary>
    /// 최소 한 줄 플레이스홀더를 보장한다.
    /// </summary>
    /// <returns>반환값 없음</returns>
    protected override void OnParametersSet()
    {
        if (Lines.Count == 0)
        {
            // 입력 시작을 돕기 위해 플레이스홀더 1행을 둔다.
            Lines.Add(new DeliveryLineModel { No = 1, IsPlaceholder = true });
        }
    }

    /// <summary>
    /// 선택 행 변경을 상위로 전달한다.
    /// </summary>
    /// <param name="line">선택 라인</param>
    /// <returns>비동기 작업</returns>
    private async Task OnSelectedItemChangedAsync(DeliveryLineModel? line)
    {
        _selectedLine = line;
        await SelectedLineChanged.InvokeAsync(line);
    }

    /// <summary>
    /// 품목 자동완성 검색.
    /// </summary>
    /// <param name="keyword">검색어</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>품목 열거</returns>
    private async Task<IEnumerable<ItemListModel>> SearchItemsAsync(string keyword, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return Array.Empty<ItemListModel>();
        }

        // ItemMasterService 시그니처는 (search, group, type, ct) 이다.
        var items = await ItemMasterService.GetListAsync(
            search: keyword,
            group: null,
            type: null,
            ct: cancellationToken);
        // List 와 배열을 ?? 로 합치면 CS0019 가 나므로 null 분기만 사용한다.
        return items is null ? (IEnumerable<ItemListModel>)Array.Empty<ItemListModel>() : items;
    }

    /// <summary>
    /// 라인을 자동완성 값으로 변환한다.
    /// </summary>
    /// <param name="line">라인</param>
    /// <returns>표시용 모델</returns>
    private static ItemListModel? ToItemModel(DeliveryLineModel line)
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

    /// <summary>
    /// 품목 선택 시 라인을 채운다.
    /// </summary>
    /// <param name="line">대상 라인</param>
    /// <param name="item">선택 품목</param>
    /// <returns>갱신 작업</returns>
    private async Task OnItemSelectedAsync(DeliveryLineModel line, ItemListModel? item)
    {
        if (item is null)
        {
            line.ItemId = string.Empty;
            line.ItemName = string.Empty;
            line.Spec = string.Empty;
            line.Unit = "EA";
            line.UnitPrice = 0m;
        }
        else
        {
            line.ItemId = item.ItemId;
            line.ItemName = item.ItemName;
            line.Spec = item.Spec ?? string.Empty;
            line.Unit = string.IsNullOrWhiteSpace(item.Unit) ? "EA" : item.Unit;
            line.UnitPrice = item.SalePrice;
            line.IsPlaceholder = false;
        }

        // 수량·단가 변경과 동일하게 금액·부가세를 다시 계산한다.
        line.RecalculateAmount();
        await NotifyChangedAsync();
    }

    /// <summary>
    /// 규격 갱신.
    /// </summary>
    /// <param name="line">라인</param>
    /// <param name="value">값</param>
    /// <returns>갱신</returns>
    private async Task UpdateSpecAsync(DeliveryLineModel line, string value)
    {
        line.Spec = value;
        await NotifyChangedAsync();
    }

    /// <summary>
    /// 단위 갱신.
    /// </summary>
    /// <param name="line">라인</param>
    /// <param name="value">값</param>
    /// <returns>갱신</returns>
    private async Task UpdateUnitAsync(DeliveryLineModel line, string value)
    {
        line.Unit = value;
        await NotifyChangedAsync();
    }

    /// <summary>
    /// 수량 갱신 및 금액 재계산.
    /// </summary>
    /// <param name="line">라인</param>
    /// <param name="value">수량</param>
    /// <returns>갱신</returns>
    private async Task UpdateQtyAsync(DeliveryLineModel line, decimal value)
    {
        line.Qty = value;
        line.RecalculateAmount();
        await NotifyChangedAsync();
    }

    /// <summary>
    /// 단가 갱신 및 금액 재계산.
    /// </summary>
    /// <param name="line">라인</param>
    /// <param name="value">단가</param>
    /// <returns>갱신</returns>
    private async Task UpdateUnitPriceAsync(DeliveryLineModel line, decimal value)
    {
        line.UnitPrice = value;
        line.RecalculateAmount();
        await NotifyChangedAsync();
    }

    /// <summary>
    /// 입고창고 Id 갱신(CreateReceiptItemRequest.WarehouseId 매핑).
    /// </summary>
    /// <param name="line">라인</param>
    /// <param name="value">창고 Id</param>
    /// <returns>갱신</returns>
    private async Task UpdateWarehouseAsync(DeliveryLineModel line, string value)
    {
        line.Warehouse = value;
        await NotifyChangedAsync();
    }

    /// <summary>
    /// 비고 갱신.
    /// </summary>
    /// <param name="line">라인</param>
    /// <param name="value">비고</param>
    /// <returns>갱신</returns>
    private async Task UpdateNoteAsync(DeliveryLineModel line, string value)
    {
        line.Note = value;
        await NotifyChangedAsync();
    }

    /// <summary>
    /// Enter/Tab 으로 셀 간 이동·마지막 행에서 행 추가를 처리한다.
    /// </summary>
    /// <param name="args">키 입력</param>
    /// <param name="line">현재 라인</param>
    /// <param name="column">열 식별</param>
    /// <returns>포커스 이동 처리</returns>
    private async Task HandleKeyDownAsync(KeyboardEventArgs args, DeliveryLineModel line, GridColumn column)
    {
        if (IsReadOnly)
        {
            // 확정 문서는 키보드로 편집 흐름을 바꾸지 않는다.
            return;
        }

        if (!string.Equals(args.Key, "Enter", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(args.Key, "Tab", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var rowIndex = Lines.IndexOf(line);
        if (rowIndex < 0)
        {
            return;
        }

        var nextRow = column == GridColumn.Note
            ? Math.Min(rowIndex + 1, Lines.Count - 1)
            : rowIndex;

        if (column == GridColumn.Note && rowIndex == Lines.Count - 1 && string.Equals(args.Key, "Enter", StringComparison.OrdinalIgnoreCase))
        {
            await AddNewRowAsync();
            return;
        }

        _selectedLine = Lines[nextRow];
        await SelectedLineChanged.InvokeAsync(_selectedLine);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 새 데이터 행을 추가한다.
    /// </summary>
    /// <returns>갱신</returns>
    private async Task AddNewRowAsync()
    {
        Lines.RemoveAll(x => x.IsPlaceholder);
        var nextNo = Lines.Count + 1;
        var row = new DeliveryLineModel { No = nextNo, RowNo = nextNo };
        Lines.Add(row);
        _selectedLine = row;
        await NotifyChangedAsync();
        await SelectedLineChanged.InvokeAsync(row);
    }

    /// <summary>
    /// 행 번호·요약·상위 콜백을 갱신한다.
    /// </summary>
    /// <returns>이벤트 전파</returns>
    private async Task NotifyChangedAsync()
    {
        for (var i = 0; i < Lines.Count; i++)
        {
            Lines[i].No = i + 1;
            Lines[i].RowNo = i + 1;
        }

        var data = Lines.Where(x => !x.IsPlaceholder).ToList();
        Summary.SupplyAmount = data.Sum(x => x.Amount);
        Summary.VatAmount = data.Sum(x => x.VatAmount);
        Summary.TotalAmount = Summary.SupplyAmount + Summary.VatAmount;
        Summary.TodaySales = Summary.TotalAmount;
        Summary.ClaimAmount = Summary.TotalAmount;

        await OnChanged.InvokeAsync();
    }

    /// <summary>
    /// 밀도 CSS 클래스.
    /// </summary>
    /// <returns>클래스 문자열</returns>
    private string GetDensityClass() => Density switch
    {
        DeliveryGridDensity.Compact => "delivery-grid--compact",
        DeliveryGridDensity.Comfortable => "delivery-grid--comfortable",
        _ => "delivery-grid--normal"
    };

    /// <summary>
    /// 행 높이 스타일.
    /// </summary>
    /// <param name="item">행 데이터</param>
    /// <param name="index">인덱스</param>
    /// <returns>스타일 문자열</returns>
    private static string GetRowStyle(DeliveryLineModel item, int index) => "height:36px;font-size:13px;";

    /// <summary>
    /// 키보드 내비게이션용 열 식별자.
    /// </summary>
    private enum GridColumn
    {
        /// <summary>품명</summary>
        ItemName,
        /// <summary>규격</summary>
        Spec,
        /// <summary>단위</summary>
        Unit,
        /// <summary>수량</summary>
        Qty,
        /// <summary>단가</summary>
        UnitPrice,
        /// <summary>입고창고</summary>
        Warehouse,
        /// <summary>비고</summary>
        Note
    }
}
