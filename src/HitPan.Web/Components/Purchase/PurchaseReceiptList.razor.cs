using HitPan.Web.Models;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HitPan.Web.Components.Purchase;

/// <summary>
/// 매입명세 목록 행(웹 전용). 서버 매입명세 목록 DTO 가 생기면 필드를 맞춰 교체한다.
/// </summary>
public sealed class PurchaseReceiptListRowModel
{
    // 서버 매입명세 Id(EventCallback 과의 호환을 위해 OrderId 명명을 유지한다).
    public string OrderId { get; set; } = string.Empty;

    // 입고일(표시용)
    public DateTime OrderDate { get; set; }

    // 전표번호(입고번호)
    public string OrderNo { get; set; } = string.Empty;

    // 공급처명
    public string PartnerName { get; set; } = string.Empty;

    // 합계 금액
    public decimal TotalAmount { get; set; }

    // 부가세
    public decimal VatAmount { get; set; }

    // 상태 문자열
    public string Status { get; set; } = string.Empty;

    // 목록 체크박스용 UI 상태
    public bool IsChecked { get; set; }
}

/// <summary>
/// 매입명세서 목록 필터·선택·합계 UI.
/// 발주 목록(PurchaseOrderList) 패턴을 계승하되 매입명세 GET API 가 없어 조회는 빈 결과로 둔다.
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
    private List<PurchaseReceiptListRowModel> _rows = new();

    // 선택된 행
    private List<PurchaseReceiptListRowModel> _selectedRows = new();

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
        // 추후 API 연동 필요: GET api/purchase/receipts?from&to&partner&status 가 생기면 여기서 HttpClient 로 채운다.
        // 추후 PurchaseService 연동 필요: 서버 목록 조회 메서드가 생기면 동일 데이터로 바인딩한다.
        // 현재 PurchaseController 에 목록 GET 이 없으므로 빈 목록으로 UI 만 검증한다.
        _ = ct;
        _rows = new List<PurchaseReceiptListRowModel>();

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
        await Task.CompletedTask;
    }

    /// <summary>
    /// 단일 행 선택 토글.
    /// </summary>
    /// <param name="row">행</param>
    /// <param name="value">체크 여부</param>
    /// <returns>완료</returns>
    private async Task ToggleOneAsync(PurchaseReceiptListRowModel row, bool value)
    {
        row.IsChecked = value;
        _selectedRows = _rows.Where(x => x.IsChecked).ToList();
        _allSelected = _rows.Count > 0 && _rows.All(x => x.IsChecked);
        RecalculateSelectionSummary();
        await Task.CompletedTask;
    }

    /// <summary>
    /// 선택 행 일괄 확정.
    /// </summary>
    /// <returns>비동기 처리</returns>
    private async Task BulkConfirmAsync()
    {
        var ids = _selectedRows
            .Where(x => !string.IsNullOrWhiteSpace(x.OrderId))
            .Select(x => x.OrderId)
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
    private async Task SelectRowAsync(PurchaseReceiptListRowModel row)
    {
        if (string.IsNullOrWhiteSpace(row.OrderId))
        {
            return;
        }

        await OnOrderSelected.InvokeAsync(row.OrderId);
    }

    /// <summary>
    /// 선택된 행 강조 스타일.
    /// </summary>
    /// <param name="row">행</param>
    /// <returns>CSS 클래스</returns>
    private string GetSelectedClass(PurchaseReceiptListRowModel row)
    {
        return string.Equals(row.OrderId, SelectedOrderId, StringComparison.OrdinalIgnoreCase)
            ? "font-weight-bold text-primary"
            : string.Empty;
    }
}
