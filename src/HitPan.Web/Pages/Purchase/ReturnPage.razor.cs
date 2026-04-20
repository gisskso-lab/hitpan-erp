using Microsoft.JSInterop;
using HitPan.Web.Components.Common;
using HitPan.Web.Components.Purchase;
using HitPan.Web.Models;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;

namespace HitPan.Web.Pages.ReturnUi;

public partial class ReturnPage : ComponentBase
{
    private List<PartnerListRow>? _partnerCache;
    private List<ItemListModel>? _itemCache;
    private DeliveryDraftModel? _draft;
    private DeliverySummaryModel _summary = new();
    private DeliveryLineModel? _selectedLine;
    private bool _hasUnsavedChanges;
    private IReadOnlyList<DeliveryWorkflowStepModel> _workflowSteps = Array.Empty<DeliveryWorkflowStepModel>();
    private string _status = "Draft";
    private string _returnType = "purchase_return";
    private bool _showReturnList;
    private List<PurchaseReturnListItem> _returnList = new();
    private bool _isNew = true;

    protected override async Task OnInitializedAsync()
    {
        _itemCache = await ItemsApi.GetListAsync() ?? new();
        _draft = new DeliveryDraftModel
        {
            Id = Guid.NewGuid().ToString(),
            DocumentType = "반품",
            SalesDate = DateTime.Today,
            ManagerName = "담당자",
            Lines = new List<DeliveryLineModel> { new() { No = 1, IsPlaceholder = true } }
        };
        RefreshWorkflow();
        RecalculateSummary();
    }

    private async Task<IEnumerable<string>> SearchPartnerAsync(string value, CancellationToken ct)
    {
        _partnerCache ??= await PartnersApi.GetListAsync() ?? new();
        if (string.IsNullOrWhiteSpace(value)) return _partnerCache.Select(p => p.PartnerName).Distinct();
        return _partnerCache.Where(p => p.PartnerName.Contains(value, StringComparison.OrdinalIgnoreCase)).Select(p => p.PartnerName).Distinct();
    }

    private async Task OnBeforeInternalNavigationAsync(LocationChangingContext context)
    {
        if (!_hasUnsavedChanges) return;
        var leave = await DialogService.ShowMessageBoxAsync("확인", "저장하지 않은 내용이 있습니다. 이동하시겠습니까?", yesText: "이동", noText: "취소");
        if (leave != true) context.PreventNavigation();
    }

    private async Task OnReturnDateChangedAsync(DateTime? value)
    {
        if (_draft is null) return;
        _draft.SalesDate = value ?? DateTime.Today;
        MarkDirty();
        RefreshWorkflow();
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnPartnerChangedAsync(string value)
    {
        if (_draft is null) return;
        _draft.SalesCompany = value;

        // 거래처명으로 PartnerId 매핑
        _partnerCache ??= await PartnersApi.GetListAsync() ?? new();
        var matched = _partnerCache.FirstOrDefault(p => p.PartnerName == value);
        if (matched is not null)
        {
            _draft.PartnerId = matched.PartnerId;
        }

        MarkDirty();
        if (TabService.ActiveTabId is { } tabId) TabService.UpdateSubTitle(tabId, value);
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnMemoChangedAsync(string value)
    {
        if (_draft is null) return;
        _draft.Memo = value;
        MarkDirty();
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnGridChangedAsync()
    {
        RecalculateSummary();
        MarkDirty();
        await InvokeAsync(StateHasChanged);
    }

    private Task OnSelectedLineChangedAsync(DeliveryLineModel? line)
    {
        _selectedLine = line;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 현재 문서를 복사해서 새 문서를 만든다.
    /// </summary>
    private async Task CopyAsync()
    {
        if (_draft is null) return;
        _draft.Id = Guid.NewGuid().ToString();
        _draft.DocumentNumber = null;
        _draft.SalesDate = DateTime.Today;
        _isNew = true;
        _hasUnsavedChanges = true;
        _status = "Draft";
        RefreshWorkflow();
        Snackbar.Add("문서가 복사되었습니다. 수정 후 저장해주세요.", Severity.Success);
        await InvokeAsync(StateHasChanged);
    }

    private async Task AddNewAsync()
    {
        if (_draft is null) return;
        _draft.Lines.RemoveAll(x => x.IsPlaceholder);
        var next = _draft.Lines.Count + 1;
        _draft.Lines.Add(new DeliveryLineModel { No = next, RowNo = next });
        MarkDirty();
        await InvokeAsync(StateHasChanged);
    }

    // TODO: 반품 백엔드 API가 구현되면 _isNew 플래그로 Create/Update 분기하여 실제 API를 호출한다.
    private Task SaveAsync()
    {
        if (_draft is null) return Task.CompletedTask;
        _draft.DocumentNumber ??= $"RT-{DateTime.Now:yyyyMMdd}-001";
        _isNew = false;
        _hasUnsavedChanges = false;
        _status = "Draft";
        if (TabService.ActiveTabId is { } tabId)
        {
            TabService.SetTabDirty(tabId, false);
            TabService.UpdateSubTitle(tabId, _draft.SalesCompany);
        }
        Snackbar.Add("반품 처리가 저장되었습니다.", Severity.Success);
        return Task.CompletedTask;
    }

    private async Task CancelAsync()
    {
        if (_isNew || _draft is null || string.IsNullOrWhiteSpace(_draft.Id))
        {
            _draft = new DeliveryDraftModel
            {
                Id = Guid.NewGuid().ToString(),
                DocumentType = "반품",
                SalesDate = DateTime.Today,
                ManagerName = "담당자",
                Lines = new List<DeliveryLineModel> { new() { No = 1, IsPlaceholder = true } }
            };
            _isNew = true;
            RecalculateSummary();
            RefreshWorkflow();
        }
        // TODO: 반품 백엔드 API 구현 후, _isNew == false 일 때 서버에서 다시 로드.

        _hasUnsavedChanges = false;
        if (TabService.ActiveTabId is { } tabId) TabService.SetTabDirty(tabId, false);
        Snackbar.Add("변경사항을 취소했습니다.", Severity.Info);
        await InvokeAsync(StateHasChanged);
    }

    private async Task DeleteConfirmAsync()
    {
        var ok = await DialogService.ShowMessageBoxAsync("삭제 확인", "현재 반품을 삭제하시겠습니까?", yesText: "삭제", cancelText: "취소");
        if (ok == true) await DeleteAsync();
    }

    // TODO: 반품 백엔드 API 구현 후, 삭제 API 호출 추가.
    private async Task DeleteAsync()
    {
        _draft = new DeliveryDraftModel
        {
            Id = Guid.NewGuid().ToString(),
            DocumentType = "반품",
            SalesDate = DateTime.Today,
            ManagerName = "담당자",
            Lines = new List<DeliveryLineModel> { new() { No = 1, IsPlaceholder = true } }
        };
        _isNew = true;
        _selectedLine = null;
        _hasUnsavedChanges = false;
        _status = "Draft";
        RecalculateSummary();
        RefreshWorkflow();
        await InvokeAsync(StateHasChanged);
    }

    private async Task PrintAsync() { await Js.InvokeVoidAsync("print"); }

    /// <summary>
    /// 이메일 발송 다이얼로그를 연다.
    /// </summary>
    private async Task EmailAsync()
    {
        var parameters = new DialogParameters
        {
            ["DocumentType"] = "반품처리서",
            ["DocumentNo"] = _draft?.DocumentNumber ?? "신규",
            ["PartnerEmail"] = ""
        };
        var options = new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true, CloseButton = true };
        await DialogService.ShowAsync<EmailSendDialog>("이메일 발송", parameters, options);
    }
    private async Task DownloadExcelAsync()
    {
        if (_draft is null || string.IsNullOrWhiteSpace(_draft.Id)) { Snackbar.Add("저장된 문서를 먼저 선택해주세요.", Severity.Warning); return; }
        await DocService.DownloadExcelAsync("return", _draft.Id);
    }
    private async Task DownloadPdfAsync()
    {
        if (_draft is null || string.IsNullOrWhiteSpace(_draft.Id)) { Snackbar.Add("저장된 문서를 먼저 선택해주세요.", Severity.Warning); return; }
        await DocService.DownloadPdfAsync("return", _draft.Id);
    }

    private async Task OpenListAsync()
    {
        _returnList = await DeliveryService.GetPurchaseReturnListAsync();
        _showReturnList = true;
        await InvokeAsync(StateHasChanged);
    }

    private void CloseReturnList()
    {
        _showReturnList = false;
    }

    private void RefreshWorkflow()
    {
        if (_draft is null) return;
        _workflowSteps = DeliveryWorkflowFactory.Build("반품", _draft);
    }

    private void RecalculateSummary()
    {
        if (_draft is null) return;
        var lines = _draft.Lines.Where(x => !x.IsPlaceholder).ToList();
        _summary.SupplyAmount = lines.Sum(x => x.Amount);
        _summary.VatAmount = lines.Sum(x => x.VatAmount);
        _summary.TotalAmount = _summary.SupplyAmount + _summary.VatAmount;
    }

    private void MarkDirty()
    {
        _hasUnsavedChanges = true;
        if (TabService.ActiveTabId is { } tabId) TabService.SetTabDirty(tabId, true);
    }
}
