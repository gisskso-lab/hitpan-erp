using HitPan.Web.Models;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Components;

namespace HitPan.Web.Components.Sales;

// 참고: 지시된 SalesModels.cs는 웹 프로젝트에 없으며, SalesListItem·IsChecked·VatAmount는 DeliveryModels.cs에 정의되어 있다.

/// <summary>
/// 수주서 목록 필터/선택/일괄확정 UI를 제공한다.
/// TransactionList 패턴을 수주 문맥으로 맞춘 컴포넌트다.
/// </summary>
/// <remarks>
/// 상위 SalesOrderPage.SaveAsync는 현재 로컬 채번 스텁이며, 수주 전용 POST API가 생기면 해당 페이지에서 연동해야 한다(추후 API 연동 필요). 이 컴포넌트는 목록 조회·일괄 확정만 담당한다.
/// </remarks>
public partial class SalesOrderList : ComponentBase
{
    // 조회 시작일 (기본: 7일 전)
    private DateTime? _startDate = DateTime.Today.AddDays(-7);

    // 조회 종료일 (기본: 오늘)
    private DateTime? _endDate = DateTime.Today;

    // 선택된 거래처 필터
    private PartnerSearchResult? _partner;

    // 상태 필터 (draft/confirmed/cancelled)
    private string _status = "draft";

    // 목록 행 데이터
    private List<SalesListItem> _rows = new();

    // 체크된 행 컬렉션
    private List<SalesListItem> _selectedRows = new();

    // 헤더 전체선택 체크 상태
    private bool _allSelected;

    // 선택된 행의 공급가 합계
    private decimal _selectedSupply;

    // 선택된 행의 부가세 합계
    private decimal _selectedVat;

    // 선택된 행의 총합계
    private decimal _selectedTotal;

    /// <summary>
    /// 거래명세서 웹 서비스.
    /// 현재 프로젝트에서 수주/거래명세 목록 조회 패턴을 공유한다.
    /// </summary>
    [Inject]
    private DeliveryService DeliveryService { get; set; } = default!;

    /// <summary>
    /// 사용자가 행을 클릭했을 때 선택된 주문 ID를 전달한다.
    /// </summary>
    [Parameter]
    public EventCallback<string> OnOrderSelected { get; set; }

    /// <summary>
    /// 외부에서 전달되는 현재 선택 주문 ID.
    /// </summary>
    [Parameter]
    public string? SelectedOrderId { get; set; }

    /// <summary>
    /// 초기 렌더링 시 기본 조건으로 목록을 조회한다.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        // 초기 로드 시 취소 토큰은 기본값을 사용한다.
        await LoadAsync(CancellationToken.None);
    }

    /// <summary>
    /// 시작일 변경 이벤트.
    /// </summary>
    private async Task OnStartDateChangedAsync(DateTime? value)
    {
        _startDate = value;
        await LoadAsync(CancellationToken.None);
    }

    /// <summary>
    /// 종료일 변경 이벤트.
    /// </summary>
    private async Task OnEndDateChangedAsync(DateTime? value)
    {
        _endDate = value;
        await LoadAsync(CancellationToken.None);
    }

    /// <summary>
    /// 거래처 변경 이벤트.
    /// </summary>
    private async Task OnPartnerChangedAsync(PartnerSearchResult? value)
    {
        _partner = value;
        await LoadAsync(CancellationToken.None);
    }

    /// <summary>
    /// 상태 변경 이벤트.
    /// </summary>
    private async Task OnStatusChangedAsync(string value)
    {
        _status = value;
        await LoadAsync(CancellationToken.None);
    }

    /// <summary>
    /// 거래처 자동완성 검색.
    /// </summary>
    /// <param name="keyword">검색어</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>거래처 후보 목록</returns>
    private async Task<IEnumerable<PartnerSearchResult>> SearchPartnersAsync(string keyword, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return Array.Empty<PartnerSearchResult>();
        }

        return await DeliveryService.SearchPartnersAsync(keyword, ct);
    }

    /// <summary>
    /// 필터 조건으로 목록을 조회한다.
    /// </summary>
    /// <param name="ct">HTTP 요청 취소용 토큰</param>
    private async Task LoadAsync(CancellationToken ct = default)
    {
        // DeliveryService에 정의된 GetSalesListItemsAsync(listType, from, to, partnerName, ct)를 그대로 사용한다.
        _rows = await DeliveryService.GetSalesListItemsAsync(
            listType: _status,
            from: _startDate,
            to: _endDate,
            partnerName: _partner?.PartnerName ?? string.Empty,
            ct: ct);

        foreach (var row in _rows)
        {
            // 조회 직후 체크 상태를 초기화해 의도치 않은 일괄 작업을 방지한다.
            row.IsChecked = false;
        }

        _selectedRows.Clear();
        _allSelected = false;
        RecalculateSelectionSummary();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 전체선택 체크 토글.
    /// </summary>
    private async Task ToggleAllAsync(bool value)
    {
        _allSelected = value;
        foreach (var row in _rows)
        {
            row.IsChecked = value;
        }

        _selectedRows = _rows.Where(x => x.IsChecked).ToList();
        RecalculateSelectionSummary();
        await Task.CompletedTask;
    }

    /// <summary>
    /// 단일 행 체크 토글.
    /// </summary>
    private async Task ToggleOneAsync(SalesListItem row, bool value)
    {
        row.IsChecked = value;
        _selectedRows = _rows.Where(x => x.IsChecked).ToList();
        _allSelected = _rows.Count > 0 && _rows.All(x => x.IsChecked);
        RecalculateSelectionSummary();
        await Task.CompletedTask;
    }

    /// <summary>
    /// 선택 행을 일괄 확정 처리한다.
    /// </summary>
    private async Task BulkConfirmAsync()
    {
        var ids = _selectedRows
            .Where(x => !string.IsNullOrWhiteSpace(x.OrderId))
            .Select(x => x.OrderId)
            .ToList();

        if (ids.Count == 0)
        {
            return;
        }

        // 배치 확정 API 응답에서 성공 ID를 기준으로 상태를 갱신한다.
        var result = await DeliveryService.BulkConfirmAsync(ids);
        var successSet = result.Success.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in _rows)
        {
            if (successSet.Contains(row.OrderId))
            {
                row.Status = "confirmed";
                row.IsChecked = false;
            }
        }

        _selectedRows = _rows.Where(x => x.IsChecked).ToList();
        _allSelected = _rows.Count > 0 && _rows.All(x => x.IsChecked);
        RecalculateSelectionSummary();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 선택된 행 합계를 재계산한다.
    /// </summary>
    private void RecalculateSelectionSummary()
    {
        // SalesListItem은 TotalAmount·VatAmount를 제공한다. 공급가는 합계에서 부가세를 뺀 값으로 산출한다.
        _selectedTotal = _selectedRows.Sum(x => x.TotalAmount);
        _selectedVat = _selectedRows.Sum(x => x.VatAmount);

        // API가 부가세 합계를 0으로만 내려줄 때(또는 미제공에 가까울 때) 합계액 기준으로 부가세를 역산한다.
        if (_selectedTotal > 0m && _selectedVat == 0m)
        {
            _selectedVat = Math.Round(_selectedTotal / 11m, 0);
        }

        _selectedSupply = _selectedTotal - _selectedVat;
    }

    /// <summary>
    /// 행 클릭 시 상위 페이지에 주문 ID를 전달한다.
    /// </summary>
    private async Task SelectRowAsync(SalesListItem row)
    {
        if (string.IsNullOrWhiteSpace(row.OrderId))
        {
            return;
        }

        await OnOrderSelected.InvokeAsync(row.OrderId);
    }

    /// <summary>
    /// 선택 상태인 주문번호에 강조 CSS를 부여한다.
    /// </summary>
    private string GetSelectedClass(SalesListItem row)
    {
        return string.Equals(row.OrderId, SelectedOrderId, StringComparison.OrdinalIgnoreCase)
            ? "font-weight-bold text-primary"
            : string.Empty;
    }
}
