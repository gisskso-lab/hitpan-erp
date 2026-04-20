using Microsoft.JSInterop;
using System.Security.Claims;
using HitPan.Web.Components.Common;
using HitPan.Web.Components.Sales;
using HitPan.Web.Models;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;

namespace HitPan.Web.Pages.QuotationUi;

/// <summary>
/// 견적서 메인 페이지를 제공한다.
/// </summary>
public partial class QuotationPage : ComponentBase
{
    // 견적서 서비스다.
    [Inject] private QuotationService QuotationService { get; set; } = default!;

    // 인증 상태 제공자다.
    [Inject] private AuthenticationStateProvider AuthStateProvider { get; set; } = default!;

    // 거래처 캐시다.
    private List<PartnerListRow>? _partnerCache;

    // 품목 캐시다.
    private List<ItemListModel>? _itemCache;

    // 현재 편집 중인 견적 초안이다.
    private QuotationDraftModel? _draft;

    // 합계 정보다.
    private QuotationSummaryModel _summary = new();

    // 현재 선택 라인이다.
    private QuotationLineModel? _selectedLine;

    // 미저장 변경 여부다.
    private bool _hasUnsavedChanges;

    // 신규 문서 여부다.
    private bool _isNew = true;

    // 전환 완료 여부 — 전환된 문서는 수정·삭제 불가 (읽기 전용)
    private bool _isConverted => _draft?.Status == "converted";

    // 워크플로 스텝이다.
    private IReadOnlyList<DeliveryWorkflowStepModel> _workflowSteps = Array.Empty<DeliveryWorkflowStepModel>();

    /// <summary>
    /// 페이지 초기 데이터를 준비한다.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        var state = await AuthStateProvider.GetAuthenticationStateAsync();
        var managerName = state.User.FindFirst("name")?.Value
                          ?? state.User.FindFirst(ClaimTypes.Name)?.Value
                          ?? "담당자";

        _draft = await QuotationService.CreateDraftAsync(managerName);
        _itemCache = await ItemsApi.GetListAsync() ?? new();
        RefreshWorkflow();
        RecalculateSummary();
    }

    /// <summary>
    /// 내부 이동 전 미저장 여부를 확인한다.
    /// </summary>
    private async Task OnBeforeInternalNavigationAsync(LocationChangingContext context)
    {
        if (!_hasUnsavedChanges)
        {
            return;
        }

        var leave = await DialogService.ShowMessageBoxAsync(
            "확인",
            "저장하지 않은 견적 내용이 있습니다. 이동하시겠습니까?",
            yesText: "이동",
            noText: "취소");

        if (leave != true)
        {
            context.PreventNavigation();
        }
    }

    /// <summary>
    /// 견적일자 변경을 반영한다.
    /// </summary>
    private async Task OnQuoteDateChangedAsync(DateTime? value)
    {
        if (_draft is null)
        {
            return;
        }

        _draft.QuoteDate = value ?? DateTime.Today;
        MarkDirty();
        RefreshWorkflow();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 유효기한 변경을 반영한다.
    /// </summary>
    private async Task OnValidUntilChangedAsync(DateTime? value)
    {
        if (_draft is null)
        {
            return;
        }

        _draft.ValidUntil = value;
        MarkDirty();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 거래처 자동완성 검색을 수행한다.
    /// </summary>
    private async Task<IEnumerable<string>> SearchPartnerAsync(string value, CancellationToken ct)
    {
        _partnerCache ??= await PartnersApi.GetListAsync() ?? new();
        if (string.IsNullOrWhiteSpace(value)) return _partnerCache.Select(p => p.PartnerName).Distinct();
        return _partnerCache.Where(p => p.PartnerName.Contains(value, StringComparison.OrdinalIgnoreCase)).Select(p => p.PartnerName).Distinct();
    }

    /// <summary>
    /// 거래처명 변경을 반영한다.
    /// </summary>
    private async Task OnPartnerNameChangedAsync(string value)
    {
        if (_draft is null)
        {
            return;
        }

        _draft.SalesCompany = value;

        // 거래처명으로 PartnerId 매핑
        _partnerCache ??= await PartnersApi.GetListAsync() ?? new();
        var matched = _partnerCache.FirstOrDefault(p => p.PartnerName == value);
        if (matched is not null)
        {
            _draft.PartnerId = matched.PartnerId;
        }

        MarkDirty();
        if (TabService.ActiveTabId is { } tabId)
        {
            TabService.UpdateSubTitle(tabId, value);
        }

        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 메모 변경을 반영한다.
    /// </summary>
    private async Task OnMemoChangedAsync(string value)
    {
        if (_draft is null)
        {
            return;
        }

        _draft.Memo = value;
        MarkDirty();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 그리드 변경 이벤트를 처리한다.
    /// </summary>
    private async Task OnGridChangedAsync()
    {
        RecalculateSummary();
        MarkDirty();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 그리드 선택 라인을 반영한다.
    /// </summary>
    private Task OnSelectedLineChangedAsync(QuotationLineModel? line)
    {
        _selectedLine = line;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 선택 변경 이벤트를 처리한다.
    /// </summary>
    private Task OnSelectionChangedAsync()
    {
        StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 현재 문서를 복사해서 새 문서를 만든다. 원본은 그대로 보존된다.
    /// </summary>
    private async Task CopyAsync()
    {
        if (_draft is null) return;

        // 기존 내용을 유지하면서 새 문서로 전환
        _draft.Id = null;
        _draft.DocumentNumber = null;
        _draft.Status = "draft";
        _draft.QuoteDate = DateTime.Today;
        _isNew = true;
        _hasUnsavedChanges = true;

        RefreshWorkflow();
        Snackbar.Add("문서가 복사되었습니다. 수정 후 저장해주세요.", Severity.Success);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 새 라인을 추가한다.
    /// </summary>
    private async Task AddNewAsync()
    {
        if (_draft is null)
        {
            return;
        }

        _draft.Lines.RemoveAll(x => x.IsPlaceholder);
        var next = _draft.Lines.Count + 1;
        _draft.Lines.Add(new QuotationLineModel { No = next, SortOrder = next });
        MarkDirty();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 견적서를 저장한다.
    /// </summary>
    private async Task SaveAsync()
    {
        if (_draft is null)
        {
            return;
        }

        try
        {
            if (_isNew)
            {
                var createdId = await QuotationService.CreateAsync(_draft);
                if (string.IsNullOrWhiteSpace(createdId))
                {
                    Snackbar.Add("견적서 생성에 실패했습니다.", Severity.Error);
                    return;
                }

                _draft.Id = createdId;
                _isNew = false;
                var reloaded = await QuotationService.GetAsync(createdId);
                if (reloaded is not null)
                {
                    _draft = reloaded;
                }
            }
            else
            {
                var ok = await QuotationService.UpdateAsync(_draft.Id, _draft);
                if (!ok)
                {
                    Snackbar.Add("견적서 저장에 실패했습니다.", Severity.Error);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"저장 중 오류: {ex.Message}", Severity.Error);
            return;
        }

        _hasUnsavedChanges = false;
        if (TabService.ActiveTabId is { } tabId)
        {
            TabService.SetTabDirty(tabId, false);
            TabService.UpdateSubTitle(tabId, _draft.SalesCompany);
        }

        RefreshWorkflow();
        RecalculateSummary();
        Snackbar.Add("견적서 저장이 완료되었습니다.", Severity.Success);
    }

    /// <summary>
    /// 편집을 취소한다.
    /// </summary>
    private async Task CancelAsync()
    {
        if (!_isNew && _draft is not null && !string.IsNullOrWhiteSpace(_draft.Id))
        {
            var reloaded = await QuotationService.GetAsync(_draft.Id);
            if (reloaded is not null)
            {
                _draft = reloaded;
            }
        }
        else
        {
            var state = await AuthStateProvider.GetAuthenticationStateAsync();
            var managerName = state.User.FindFirst("name")?.Value ?? "담당자";
            _draft = await QuotationService.CreateDraftAsync(managerName);
            _isNew = true;
        }

        _hasUnsavedChanges = false;
        if (TabService.ActiveTabId is { } tabId)
        {
            TabService.SetTabDirty(tabId, false);
        }

        RecalculateSummary();
        RefreshWorkflow();
        Snackbar.Add("변경사항을 취소했습니다.", Severity.Info);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 삭제 확인 다이얼로그를 연다.
    /// </summary>
    private async Task DeleteConfirmAsync()
    {
        var ok = await DialogService.ShowMessageBoxAsync(
            "삭제 확인",
            "현재 견적서를 삭제하시겠습니까?",
            yesText: "삭제",
            cancelText: "취소");
        if (ok == true)
        {
            await DeleteAsync();
        }
    }

    /// <summary>
    /// 견적서를 삭제하고 신규 상태로 초기화한다.
    /// </summary>
    private async Task DeleteAsync()
    {
        if (_draft is not null && !string.IsNullOrWhiteSpace(_draft.Id))
        {
            await QuotationService.DeleteAsync(_draft.Id);
        }

        var state = await AuthStateProvider.GetAuthenticationStateAsync();
        var managerName = state.User.FindFirst("name")?.Value
                          ?? state.User.FindFirst(ClaimTypes.Name)?.Value
                          ?? "담당자";
        _draft = await QuotationService.CreateDraftAsync(managerName);
        _isNew = true;
        _selectedLine = null;
        _hasUnsavedChanges = false;
        RecalculateSummary();
        RefreshWorkflow();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 인쇄 기능을 호출한다.
    /// </summary>
    private async Task PrintAsync()
    {
        await Js.InvokeVoidAsync("print");
    }

    /// <summary>
    /// 이메일 발송 다이얼로그를 연다.
    /// </summary>
    private async Task EmailAsync()
    {
        var parameters = new DialogParameters
        {
            ["DocumentType"] = "견적서",
            ["DocumentNo"] = _draft?.DocumentNumber ?? "신규",
            ["PartnerEmail"] = ""
        };
        var options = new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true, CloseButton = true };
        await DialogService.ShowAsync<EmailSendDialog>("이메일 발송", parameters, options);
    }

    /// <summary>
    /// 엑셀 다운로드를 호출한다.
    /// </summary>
    private async Task DownloadExcelAsync()
    {
        if (_draft is null || string.IsNullOrWhiteSpace(_draft.Id))
        {
            Snackbar.Add("저장된 견적서를 먼저 선택해주세요.", Severity.Warning);
            return;
        }

        await DocService.DownloadExcelAsync("quotation", _draft.Id);
    }

    /// <summary>
    /// PDF 다운로드를 호출한다.
    /// </summary>
    private async Task DownloadPdfAsync()
    {
        if (_draft is null || string.IsNullOrWhiteSpace(_draft.Id))
        {
            Snackbar.Add("저장된 견적서를 먼저 선택해주세요.", Severity.Warning);
            return;
        }

        await DocService.DownloadPdfAsync("quotation", _draft.Id);
    }

    /// <summary>
    /// 견적서를 수주서로 전환한다.
    /// </summary>
    private async Task ConvertToSalesOrderAsync()
    {
        if (_draft is null || string.IsNullOrWhiteSpace(_draft.Id))
        {
            Snackbar.Add("저장된 견적서를 먼저 선택해주세요.", Severity.Warning);
            return;
        }

        var converted = await QuotationService.ConvertToSalesOrderAsync(_draft.Id);
        if (string.IsNullOrWhiteSpace(converted))
        {
            Snackbar.Add("수주 전환에 실패했습니다.", Severity.Error);
            return;
        }

        _draft.Status = "converted";
        Snackbar.Add($"수주 전환 완료: {converted}", Severity.Success);
    }

    /// <summary>
    /// 견적서 목록 팝업을 연다.
    /// </summary>
    private async Task OpenListPopupAsync()
    {
        var options = new DialogOptions { MaxWidth = MaxWidth.ExtraLarge, FullWidth = true, CloseButton = true };
        var dialog = await DialogService.ShowAsync<QuotationList>("견적서 목록", options);
        var result = await dialog.Result;
        if (result is null || result.Canceled || result.Data is not string quoteId)
        {
            return;
        }

        var loaded = await QuotationService.GetAsync(quoteId);
        if (loaded is null)
        {
            Snackbar.Add("견적서를 불러오지 못했습니다.", Severity.Warning);
            return;
        }

        _draft = loaded;
        _isNew = false;
        _hasUnsavedChanges = false;
        RecalculateSummary();
        RefreshWorkflow();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 워크플로를 다시 계산한다.
    /// </summary>
    private void RefreshWorkflow()
    {
        if (_draft is null)
        {
            return;
        }

        var bridge = new DeliveryDraftModel
        {
            DocumentType = "수주",
            DocumentNumber = _draft.DocumentNumber,
            LinkedQuoteDocumentNo = _draft.DocumentNumber
        };
        _workflowSteps = DeliveryWorkflowFactory.Build("견적", bridge);
    }

    /// <summary>
    /// 합계를 재계산한다.
    /// </summary>
    private void RecalculateSummary()
    {
        if (_draft is null)
        {
            return;
        }

        var lines = _draft.Lines.Where(x => !x.IsPlaceholder).ToList();
        _summary.SupplyAmount = lines.Sum(x => x.Amount);
        _summary.VatAmount = lines.Sum(x => x.VatAmount);
        _summary.TotalAmount = _summary.SupplyAmount + _summary.VatAmount;
    }

    /// <summary>
    /// 더티 상태를 표시한다.
    /// </summary>
    private void MarkDirty()
    {
        _hasUnsavedChanges = true;
        if (TabService.ActiveTabId is { } tabId)
        {
            TabService.SetTabDirty(tabId, true);
        }
    }
}
