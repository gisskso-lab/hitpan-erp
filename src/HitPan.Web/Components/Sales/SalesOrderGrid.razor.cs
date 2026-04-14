using HitPan.Web.Models;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace HitPan.Web.Components.Sales;

/// <summary>
/// 수주 라인 입력용 인라인 편집 그리드 컴포넌트.
/// 거래명세서 그리드 패턴을 재사용하여 입력 UX를 동일하게 제공한다.
/// </summary>
/// <remarks>
/// 상위 SalesOrderPage.SaveAsync는 수주 전용 저장 API 연동 전까지 로컬 채번 스텁이다(추후 API 연동 필요).
/// </remarks>
public partial class SalesOrderGrid : ComponentBase
{
    // 현재 선택된 라인 캐시
    private DeliveryLineModel? _selectedLine;

    // 읽기 전용 여부를 판별하기 위한 상태값
    private bool IsReadOnly => string.Equals(Status, "Confirmed", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 품목 자동완성 조회 서비스.
    /// </summary>
    [Inject]
    private ItemMasterService ItemMasterService { get; set; } = default!;

    /// <summary>
    /// 그리드 라인 목록.
    /// </summary>
    [Parameter, EditorRequired]
    public List<DeliveryLineModel> Lines { get; set; } = default!;

    /// <summary>
    /// 밀도(행 높이) 옵션.
    /// </summary>
    [Parameter]
    public DeliveryGridDensity Density { get; set; }

    /// <summary>
    /// 요약 데이터(공급가/부가세/합계).
    /// </summary>
    [Parameter]
    public DeliverySummaryModel Summary { get; set; } = new();

    /// <summary>
    /// 라인 변경 시 상위에 알리는 콜백.
    /// </summary>
    [Parameter]
    public EventCallback OnChanged { get; set; }

    /// <summary>
    /// 선택 라인 변경 콜백.
    /// </summary>
    [Parameter]
    public EventCallback<DeliveryLineModel?> SelectedLineChanged { get; set; }

    /// <summary>
    /// 문서 상태값(Draft/Confirmed).
    /// </summary>
    [Parameter]
    public string Status { get; set; } = "Draft";

    /// <summary>
    /// 파라미터 수신 시 최소 1개 플레이스홀더 라인을 보장한다.
    /// </summary>
    protected override void OnParametersSet()
    {
        if (Lines.Count == 0)
        {
            // 빈 상태에서 입력 유도용 플레이스홀더를 추가한다.
            Lines.Add(new DeliveryLineModel { No = 1, IsPlaceholder = true });
        }
    }

    /// <summary>
    /// 선택 라인 변경을 상위로 전달한다.
    /// </summary>
    /// <param name="line">선택된 라인</param>
    /// <returns>비동기 작업</returns>
    private async Task OnSelectedItemChangedAsync(DeliveryLineModel? line)
    {
        _selectedLine = line;
        await SelectedLineChanged.InvokeAsync(line);
    }

    /// <summary>
    /// 품목 자동완성 목록을 조회한다.
    /// </summary>
    /// <param name="keyword">검색 키워드</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>품목 리스트</returns>
    private async Task<IEnumerable<ItemListModel>> SearchItemsAsync(string keyword, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return Array.Empty<ItemListModel>();
        }

        // ItemMasterService.GetListAsync(search, group, type, ct) 시그니처에 맞추며 TransactionGrid와 동일한 인자 패턴을 쓴다.
        var items = await ItemMasterService.GetListAsync(
            search: keyword,
            group: null,
            type: null,
            ct: cancellationToken);
        // GetListAsync 는 List<ItemListModel>? 를 반환하므로 ?? 우측은 동일 제네릭 타입이어야 한다(배열과 혼용 시 CS0019).
        return items is null ? (IEnumerable<ItemListModel>)Array.Empty<ItemListModel>() : items;
    }

    /// <summary>
    /// 라인 정보를 자동완성 바인딩 모델로 변환한다.
    /// </summary>
    /// <param name="line">대상 라인</param>
    /// <returns>품목 모델 또는 null</returns>
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
    /// 자동완성에서 품목을 선택한 뒤 라인 데이터를 갱신한다.
    /// </summary>
    /// <param name="line">대상 라인</param>
    /// <param name="item">선택 품목</param>
    /// <returns>비동기 작업</returns>
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

        // 금액/부가세는 계산 필드이므로 변경 직후 합계를 재계산한다.
        line.RecalculateAmount();
        await NotifyChangedAsync();
    }

    /// <summary>
    /// 규격 값을 갱신한다.
    /// </summary>
    private async Task UpdateSpecAsync(DeliveryLineModel line, string value)
    {
        line.Spec = value;
        await NotifyChangedAsync();
    }

    /// <summary>
    /// 단위 값을 갱신한다.
    /// </summary>
    private async Task UpdateUnitAsync(DeliveryLineModel line, string value)
    {
        line.Unit = value;
        await NotifyChangedAsync();
    }

    /// <summary>
    /// 수량을 갱신하고 금액/부가세 계산을 반영한다.
    /// </summary>
    private async Task UpdateQtyAsync(DeliveryLineModel line, decimal value)
    {
        line.Qty = value;
        line.RecalculateAmount();
        await NotifyChangedAsync();
    }

    /// <summary>
    /// 단가를 갱신하고 금액/부가세 계산을 반영한다.
    /// </summary>
    private async Task UpdateUnitPriceAsync(DeliveryLineModel line, decimal value)
    {
        line.UnitPrice = value;
        line.RecalculateAmount();
        await NotifyChangedAsync();
    }

    /// <summary>
    /// 비고 값을 갱신한다.
    /// </summary>
    private async Task UpdateNoteAsync(DeliveryLineModel line, string value)
    {
        line.Note = value;
        await NotifyChangedAsync();
    }

    /// <summary>
    /// Enter/Tab 입력을 이용해 다음 셀/다음 행 이동을 처리한다.
    /// </summary>
    /// <param name="args">키 이벤트</param>
    /// <param name="line">현재 라인</param>
    /// <param name="column">현재 컬럼</param>
    private async Task HandleKeyDownAsync(KeyboardEventArgs args, DeliveryLineModel line, GridColumn column)
    {
        if (IsReadOnly)
        {
            // 확정 상태에서는 키 네비게이션도 입력 흐름을 바꾸지 않는다.
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

        // 비고 열 Enter는 다음 행으로 이동한다.
        var nextRow = column == GridColumn.Note
            ? Math.Min(rowIndex + 1, Lines.Count - 1)
            : rowIndex;

        // 마지막 행 비고에서 Enter를 누르면 새 행을 생성한다.
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
    /// 새 입력 행을 추가한다.
    /// </summary>
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
    /// 상위 콜백을 호출하기 전에 라인 번호/요약을 갱신한다.
    /// </summary>
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
    /// 현재 밀도 옵션에 맞는 CSS 클래스를 반환한다.
    /// </summary>
    /// <returns>밀도 클래스 문자열</returns>
    private string GetDensityClass() => Density switch
    {
        DeliveryGridDensity.Compact => "delivery-grid--compact",
        DeliveryGridDensity.Comfortable => "delivery-grid--comfortable",
        _ => "delivery-grid--normal"
    };

    /// <summary>
    /// 행 스타일(높이/폰트) 문자열을 반환한다.
    /// </summary>
    private static string GetRowStyle(DeliveryLineModel item, int index) => "height:36px;font-size:13px;";

    /// <summary>
    /// 그리드 열 위치를 나타내는 내부 열거형.
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
        /// <summary>비고</summary>
        Note
    }
}
