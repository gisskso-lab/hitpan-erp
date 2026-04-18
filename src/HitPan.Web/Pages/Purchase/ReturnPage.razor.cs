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

    private async Task AddNewAsync()
    {
        if (_draft is null) return;
        _draft.Lines.RemoveAll(x => x.IsPlaceholder);
        var next = _draft.Lines.Count + 1;
        _draft.Lines.Add(new DeliveryLineModel { No = next, RowNo = next });
        MarkDirty();
        await InvokeAsync(StateHasChanged);
    }

    private Task SaveAsync()
    {
        if (_draft is null) return Task.CompletedTask;
        _draft.DocumentNumber ??= $"RT-{DateTime.Now:yyyyMMdd}-001";
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

    private Task CancelAsync()
    {
        _hasUnsavedChanges = false;
        if (TabService.ActiveTabId is { } tabId) TabService.SetTabDirty(tabId, false);
        Snackbar.Add("변경사항을 취소했습니다.", Severity.Info);
        return Task.CompletedTask;
    }

    private async Task DeleteConfirmAsync()
    {
        var ok = await DialogService.ShowMessageBoxAsync("삭제 확인", "현재 반품을 삭제하시겠습니까?", yesText: "삭제", cancelText: "취소");
        if (ok == true) await DeleteAsync();
    }

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
        _selectedLine = null;
        _hasUnsavedChanges = false;
        _status = "Draft";
        RecalculateSummary();
        RefreshWorkflow();
        await InvokeAsync(StateHasChanged);
    }

    private Task PrintAsync() { Snackbar.Add("인쇄 기능은 다음 단계에서 연동됩니다.", Severity.Info); return Task.CompletedTask; }
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
        Snackbar.Add("반품 목록 기능은 다음 단계에서 연동됩니다.", Severity.Info);
        await Task.CompletedTask;
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
