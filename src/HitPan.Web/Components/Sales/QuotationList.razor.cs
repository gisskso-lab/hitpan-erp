using HitPan.Web.Models;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HitPan.Web.Components.Sales;

/// <summary>
/// 견적서 목록 조회 다이얼로그다.
/// </summary>
public partial class QuotationList : ComponentBase
{
    // 다이얼로그 인스턴스다.
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;

    // 견적 서비스다.
    [Inject] private QuotationService QuotationService { get; set; } = default!;

    // 스낵바 서비스다.
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    // 시작일 필터다.
    private DateTime? _startDate = DateTime.Today.AddDays(-30);

    // 종료일 필터다.
    private DateTime? _endDate = DateTime.Today;

    // 거래처명 필터다.
    private string _partnerName = string.Empty;

    // 상태 필터다.
    private string _status = "draft";

    // 조회 목록 데이터다.
    private List<QuotationListItem> _rows = new();

    // 체크된 행 컬렉션이다.
    private List<QuotationListItem> _selectedRows = new();

    // 헤더 전체선택 체크 상태다.
    private bool _allSelected;

    /// <summary>
    /// 초기 목록을 조회한다.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    /// <summary>
    /// 시작일 변경 이벤트다.
    /// </summary>
    private async Task OnStartDateChangedAsync(DateTime? value)
    {
        _startDate = value;
        await LoadAsync();
    }

    /// <summary>
    /// 종료일 변경 이벤트다.
    /// </summary>
    private async Task OnEndDateChangedAsync(DateTime? value)
    {
        _endDate = value;
        await LoadAsync();
    }

    /// <summary>
    /// 거래처명 변경 이벤트다.
    /// </summary>
    private async Task OnPartnerNameChangedAsync(string value)
    {
        _partnerName = value;
        await LoadAsync();
    }

    /// <summary>
    /// 상태 변경 이벤트다.
    /// </summary>
    private async Task OnStatusChangedAsync(string value)
    {
        _status = value;
        await LoadAsync();
    }

    /// <summary>
    /// 목록을 조회한다.
    /// </summary>
    private async Task LoadAsync()
    {
        _rows = await QuotationService.GetListAsync(_startDate, _endDate, _partnerName, _status);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 전체선택 체크 토글이다.
    /// </summary>
    private Task ToggleAllAsync(bool value)
    {
        _allSelected = value;
        foreach (var row in _rows)
        {
            row.IsChecked = value;
        }

        _selectedRows = _rows.Where(x => x.IsChecked).ToList();
        // 외부 툴바 버튼 Disabled 즉시 갱신.
        return InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 단일 행 체크 토글이다.
    /// </summary>
    private Task ToggleOneAsync(QuotationListItem row, bool value)
    {
        row.IsChecked = value;
        _selectedRows = _rows.Where(x => x.IsChecked).ToList();
        _allSelected = _rows.Count > 0 && _rows.All(x => x.IsChecked);
        return InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 선택된 견적서를 일괄 수주서로 전환한다.
    /// </summary>
    private async Task BulkConvertToOrderAsync()
    {
        var ids = _selectedRows
            .Where(x => !string.IsNullOrWhiteSpace(x.QuoteId))
            .Select(x => x.QuoteId)
            .ToList();

        if (ids.Count == 0)
        {
            return;
        }

        var successCount = 0;
        var failCount = 0;

        foreach (var quoteId in ids)
        {
            var orderId = await QuotationService.ConvertToSalesOrderAsync(quoteId);
            if (!string.IsNullOrWhiteSpace(orderId))
            {
                successCount++;
            }
            else
            {
                failCount++;
            }
        }

        if (successCount > 0)
        {
            Snackbar.Add($"{successCount}건 수주전환 완료", Severity.Success);
        }

        if (failCount > 0)
        {
            Snackbar.Add($"{failCount}건 수주전환 실패", Severity.Error);
        }

        // 목록 새로고침
        await LoadAsync();
    }

    /// <summary>
    /// 행 선택 결과를 상위로 반환한다.
    /// </summary>
    private Task SelectAsync(QuotationListItem item)
    {
        MudDialog.Close(DialogResult.Ok(item.QuoteId));
        return Task.CompletedTask;
    }

    /// <summary>
    /// 다이얼로그를 닫는다.
    /// </summary>
    private void Close()
    {
        MudDialog.Close(DialogResult.Cancel());
    }
}
