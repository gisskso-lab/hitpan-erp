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
