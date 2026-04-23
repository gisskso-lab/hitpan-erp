using HitPan.Web.Models;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HitPan.Web.Components.Purchase;

/// <summary>
/// 매입명세서 목록 필터·선택·합계 UI.
/// 발주 목록(PurchaseOrderList) 패턴을 계승한다.
/// </summary>
public partial class PurchaseReceiptList : ComponentBase
{
    // 조회 시작일
    private DateTime? _startDate = DateTime.Today.AddDays(-7);

    // 조회 종료일
    private DateTime? _endDate = DateTime.Today;

    // 거래처 필터
    private PartnerSearchResult? _partner;

    // 상태 필터
    private string _status = "draft";

    // 목록 행
    private List<PurchaseReceiptListItem> _rows = new();

    // 선택된 행
    private List<PurchaseReceiptListItem> _selectedRows = new();

    // 전체 선택 체크
    private bool _allSelected;

    // 선택 합계: 공급가
    private decimal _selectedSupply;

    // 선택 합계: 부가세
    private decimal _selectedVat;

    // 선택 합계: 총액
    private decimal _selectedTotal;

    /// <summary>
    /// 거래처 검색은 기존 배송/수주와 동일하게 DeliveryService 를 사용한다(공급처도 동일 partner API).
    /// </summary>
    [Inject]
    private DeliveryService DeliveryService { get; set; } = default!;

    /// <summary>
    /// 일괄 확정 안내용 스낵바.
    /// </summary>
    [Inject]
    private ISnackbar Snackbar { get; set; } = default!;

    [Inject]
    private IDialogService DialogService { get; set; } = default!;

    /// <summary>
    /// 행 클릭 시 상위로 매입명세 Id 를 전달한다.
    /// </summary>
    [Parameter]
    public EventCallback<string> OnOrderSelected { get; set; }

    /// <summary>
    /// 현재 강조할 매입명세 Id.
    /// </summary>
    [Parameter]
    public string? SelectedOrderId { get; set; }

    /// <summary>
    /// 초기 로드 시 조회를 호출한다.
    /// </summary>
    /// <returns>비동기 초기화</returns>
    protected override async Task OnInitializedAsync()
    {
        await LoadAsync(CancellationToken.None);
    }

    /// <summary>
    /// 시작일 변경.
    /// </summary>
    /// <param name="value">일자</param>
    /// <returns>재조회</returns>
    private async Task OnStartDateChangedAsync(DateTime? value)
    {
        _startDate = value;
        await LoadAsync(CancellationToken.None);
    }

    /// <summary>
    /// 종료일 변경.
    /// </summary>
    /// <param name="value">일자</param>
    /// <returns>재조회</returns>
    private async Task OnEndDateChangedAsync(DateTime? value)
    {
        _endDate = value;
        await LoadAsync(CancellationToken.None);
    }

    /// <summary>
    /// 거래처 필터 변경.
    /// </summary>
    /// <param name="value">선택 거래처</param>
    /// <returns>재조회</returns>
    private async Task OnPartnerChangedAsync(PartnerSearchResult? value)
    {
        _partner = value;
        await LoadAsync(CancellationToken.None);
    }

    /// <summary>
    /// 상태 필터 변경.
    /// </summary>
    /// <param name="value">상태 코드</param>
    /// <returns>재조회</returns>
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
    /// <returns>후보 목록</returns>
    private async Task<IEnumerable<PartnerSearchResult>> SearchPartnersAsync(string keyword, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return Array.Empty<PartnerSearchResult>();
        }

        return await DeliveryService.SearchPartnersAsync(keyword, ct);
    }

    /// <summary>
    /// 매입명세 목록을 조회한다.
    /// </summary>
    /// <param name="ct">취소 토큰</param>
    /// <returns>비동기 조회</returns>
    private async Task LoadAsync(CancellationToken ct = default)
    {
        _rows = await DeliveryService.GetPurchaseReceiptListAsync(
            from: _startDate,
            to: _endDate,
            status: _status,
            ct: ct);

        foreach (var row in _rows)
        {
            // 조회 직후 체크 상태를 초기화한다.
            row.IsChecked = false;
        }

        _selectedRows.Clear();
        _allSelected = false;
        RecalculateSelectionSummary();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 전체 선택 토글.
    /// </summary>
    /// <param name="value">체크 여부</param>
    /// <returns>완료</returns>
    private async Task ToggleAllAsync(bool value)
    {
        _allSelected = value;
        foreach (var row in _rows)
        {
            row.IsChecked = value;
        }

        _selectedRows = _rows.Where(x => x.IsChecked).ToList();
        RecalculateSelectionSummary();
        // 외부 툴바 버튼 Disabled 즉시 갱신.
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 단일 행 선택 토글.
    /// </summary>
    /// <param name="row">행</param>
    /// <param name="value">체크 여부</param>
    /// <returns>완료</returns>
    private async Task ToggleOneAsync(PurchaseReceiptListItem row, bool value)
    {
        row.IsChecked = value;
        _selectedRows = _rows.Where(x => x.IsChecked).ToList();
        _allSelected = _rows.Count > 0 && _rows.All(x => x.IsChecked);
        RecalculateSelectionSummary();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 선택 행 일괄 확정.
    /// </summary>
    /// <returns>비동기 처리</returns>
    private async Task BulkConfirmAsync()
    {
        var ids = _selectedRows
            .Where(x => !string.IsNullOrWhiteSpace(x.ReceiptId))
            .Select(x => x.ReceiptId)
            .ToList();

        // 선택이 없으면 API 를 호출하지 않는다.
        if (ids.Count == 0)
        {
            return;
        }

        // 추후 API 연동 필요: POST api/purchase/receipts/{id}/confirm 를 일괄 호출하거나 bulk 엔드포인트를 추가한다.
        // 추후 PurchaseService 연동 필요: ConfirmReceiptAsync 를 클라이언트에서 반복 호출하는 방식은 정책 협의 후 적용한다.
        Snackbar.Add("매입명세 일괄 확정은 추후 API 연동 필요(현재 단건 확정 엔드포인트만 존재).", Severity.Info);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 선택된 매입명세서를 반품으로 전환한다 (반품 API 추가 전 스텁).
    /// </summary>
    private async Task BulkConvertToReturnAsync()
    {
        var ids = _selectedRows
            .Where(x => !string.IsNullOrWhiteSpace(x.ReceiptId))
            .Select(x => x.ReceiptId)
            .ToList();

        if (ids.Count == 0) return;

        var confirm = await DialogService.ShowMessageBoxAsync(
            "반품 전환",
            $"선택한 {ids.Count}건을 반품으로 전환하시겠습니까?",
            yesText: "전환", cancelText: "취소");
        if (confirm != true) return;

        var success = 0;
        foreach (var id in ids)
        {
            var ok = await DeliveryService.ConvertReceiptToReturnAsync(id);
            if (ok) success++;
        }

        Snackbar.Add($"{success}건 반품 전환 완료", Severity.Success);
        await LoadAsync();
    }

    /// <summary>
    /// 선택 합계를 계산한다.
    /// </summary>
    /// <returns>반환값 없음</returns>
    private void RecalculateSelectionSummary()
    {
        _selectedTotal = _selectedRows.Sum(x => x.TotalAmount);
        _selectedVat = _selectedRows.Sum(x => x.VatAmount);

        // 부가세가 모두 0이면 합계에서 10% 역산한다.
        if (_selectedTotal > 0m && _selectedVat == 0m)
        {
            _selectedVat = Math.Round(_selectedTotal / 11m, 0);
        }

        _selectedSupply = _selectedTotal - _selectedVat;
    }

    /// <summary>
    /// 행 클릭 시 상위에 매입명세 Id 를 알린다.
    /// </summary>
    /// <param name="row">행</param>
    /// <returns>콜백 호출</returns>
    private async Task SelectRowAsync(PurchaseReceiptListItem row)
    {
        if (string.IsNullOrWhiteSpace(row.ReceiptId))
        {
            return;
        }

        await OnOrderSelected.InvokeAsync(row.ReceiptId);
    }

    /// <summary>
    /// 선택된 행 강조 스타일.
    /// </summary>
    /// <param name="row">행</param>
    /// <returns>CSS 클래스</returns>
    private string GetSelectedClass(PurchaseReceiptListItem row)
    {
        return string.Equals(row.ReceiptId, SelectedOrderId, StringComparison.OrdinalIgnoreCase)
            ? "font-weight-bold text-primary"
            : string.Empty;
    }
}
