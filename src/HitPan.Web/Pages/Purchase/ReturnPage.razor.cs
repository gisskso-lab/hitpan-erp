using Microsoft.JSInterop;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
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
    private bool _isNew = true;

    // 작지 #3 반품 전용 컬럼 (사장님 작업지시 2026-05-31)
    private string? _returnReason;
    private string? _returnReasonMemo;

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

    /// <summary>
    /// 반품은 매입명세서에서 "반품전환" 버튼으로만 생성되는 설계다.
    /// 신규 저장 시 사용자를 매입명세서 화면으로 유도하고, 기존 반품 편집은
    /// 아직 수정 API가 없으므로 확정/삭제만 가능함을 알린다.
    /// </summary>
    private async Task SaveAsync()
    {
        if (_draft is null) return;

        var partnerName = _draft.SalesCompany;
        var partner = _partnerCache?.FirstOrDefault(p => string.Equals(p.PartnerName, partnerName, StringComparison.Ordinal));
        if (partner is null)
        {
            Snackbar.Add("거래처를 선택해주세요.", Severity.Warning);
            return;
        }

        var items = _draft.Lines
            .Where(l => !l.IsPlaceholder && !string.IsNullOrWhiteSpace(l.ItemId))
            .Select(l => new
            {
                itemId = l.ItemId,
                warehouseId = string.IsNullOrWhiteSpace(l.Warehouse) ? null : l.Warehouse,
                qty = l.Quantity,
                unitPrice = l.UnitPrice,
                supplyAmount = l.Amount,
                vatAmount = l.VatAmount
            })
            .ToList();

        if (items.Count == 0)
        {
            Snackbar.Add("반품 품목을 1건 이상 입력해주세요.", Severity.Warning);
            return;
        }

        var payload = new
        {
            partnerId = partner.PartnerId,
            returnDate = _draft.SalesDate,
            memo = (string?)null,
            items
        };

        try
        {
            if (_isNew)
            {
                // P0 #1 — 신규 반품 작성
                var resp = await Http.PostAsJsonAsync("api/purchase/returns", payload);
                if (!resp.IsSuccessStatusCode)
                {
                    Snackbar.Add($"반품 저장 실패: {resp.StatusCode}", Severity.Error);
                    return;
                }
                var created = await resp.Content.ReadFromJsonAsync<ReturnCreatedResponse>();
                if (created is not null)
                {
                    _draft.Id = created.ReturnId;
                    _draft.DocumentNumber = created.ReturnNo;
                    _isNew = false;
                }
                Snackbar.Add("반품을 저장했습니다.", Severity.Success);
            }
            else
            {
                // P0 #1 — draft 반품 수정
                var resp = await Http.PutAsJsonAsync($"api/purchase/returns/{_draft.Id}", payload);
                if (!resp.IsSuccessStatusCode)
                {
                    Snackbar.Add($"반품 수정 실패: {resp.StatusCode}", Severity.Error);
                    return;
                }
                Snackbar.Add("반품을 수정했습니다.", Severity.Success);
            }

            _hasUnsavedChanges = false;
            if (TabService.ActiveTabId is { } tabId) TabService.SetTabDirty(tabId, false);
            RecalculateSummary();
            RefreshWorkflow();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"반품 저장 오류: {ex.Message}", Severity.Error);
        }
    }

    private class ReturnCreatedResponse
    {
        [JsonPropertyName("returnId")] public string ReturnId { get; set; } = string.Empty;
        [JsonPropertyName("returnNo")] public string ReturnNo { get; set; } = string.Empty;
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
        // P0 #1 — 신규 작성 모드는 클라이언트 reset만으로 충분. 기존 문서 편집 시
        // GetReturnDetail API로 재로드(이미 페이지 진입 시 로드되므로 cancel은 reset만).

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

    /// <summary>
    /// 반품 삭제 — draft 상태만 서버에 DELETE 요청. 서버 성공 시 새 빈 초안으로 교체.
    /// 신규 문서(_isNew)거나 id 없으면 클라이언트 reset만 수행.
    /// </summary>
    private async Task DeleteAsync()
    {
        if (!_isNew && _draft is not null && !string.IsNullOrWhiteSpace(_draft.Id))
        {
            try
            {
                using var resp = await Http.DeleteAsync($"api/purchase/returns/{Uri.EscapeDataString(_draft.Id)}");
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    Snackbar.Add($"삭제 실패: {(string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)resp.StatusCode}" : body)}", Severity.Error);
                    return;
                }
                Snackbar.Add("반품 문서가 삭제되었습니다.", Severity.Success);
            }
            catch (Exception ex)
            {
                Snackbar.Add($"삭제 중 오류: {ex.Message}", Severity.Error);
                return;
            }
        }

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
            ["DocumentTypeKey"] = "purchase_receipt",
            ["DocumentNo"] = _draft?.DocumentNumber ?? "신규",
            ["DocumentId"] = _draft?.Id ?? "",
            ["PartnerId"] = _draft?.PartnerId ?? ""
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
        var options = new DialogOptions { MaxWidth = MaxWidth.ExtraLarge, FullWidth = true, CloseButton = true };
        var dlg = await DialogService.ShowAsync<PurchaseReturnList>("매입반품 목록", options);
        var result = await dlg.Result;
        if (result is null || result.Canceled) return;

        var returnId = result.Data as string;
        if (string.IsNullOrWhiteSpace(returnId)) return;

        await LoadReturnAsync(returnId);
    }

    /// <summary>서버에서 매입반품 단건을 읽어 편집 화면에 주입한다.</summary>
    private async Task LoadReturnAsync(string returnId)
    {
        var detail = await DeliveryService.GetPurchaseReturnDetailAsync(returnId);
        if (detail is null)
        {
            Snackbar.Add("반품 문서를 불러오지 못했습니다.", Severity.Error);
            return;
        }

        _itemCache ??= await ItemsApi.GetListAsync() ?? new();

        var lines = new List<DeliveryLineModel>();
        var no = 1;
        foreach (var it in detail.Items)
        {
            lines.Add(new DeliveryLineModel
            {
                No = no++,
                ItemId = it.ItemId,
                ItemName = it.ItemName,
                Spec = it.Spec ?? string.Empty,
                Unit = string.IsNullOrWhiteSpace(it.Unit) ? "EA" : it.Unit!,
                Quantity = it.Qty,
                UnitPrice = it.UnitPrice,
                Warehouse = it.WarehouseId ?? string.Empty,
                IsPlaceholder = false
            });
        }
        lines.Add(new DeliveryLineModel { No = no, IsPlaceholder = true });

        _draft = new DeliveryDraftModel
        {
            Id = detail.ReturnId,
            DocumentType = "반품",
            DocumentNumber = detail.ReturnNo,
            SalesDate = detail.ReturnDate,
            ManagerName = "담당자",
            PartnerId = detail.PartnerId,
            SalesCompany = detail.PartnerName,
            Memo = detail.Memo,
            Status = detail.Status,
            Lines = lines
        };
        _status = string.Equals(detail.Status, "confirmed", StringComparison.OrdinalIgnoreCase)
            ? "Confirmed" : "Draft";
        _isNew = false;
        _hasUnsavedChanges = false;
        _selectedLine = null;

        RecalculateSummary();
        RefreshWorkflow();

        if (TabService.ActiveTabId is { } tabId)
        {
            TabService.SetTabDirty(tabId, false);
            TabService.UpdateSubTitle(tabId, _draft.SalesCompany);
        }

        Snackbar.Add($"[{detail.ReturnNo}] 불러왔습니다.", Severity.Info);
        await InvokeAsync(StateHasChanged);
    }

    private bool _isConfirming;
    private async Task ConfirmReturnAsync()
    {
        if (_isConfirming) return;
        if (_draft is null || string.IsNullOrWhiteSpace(_draft.Id))
        {
            Snackbar.Add("저장된 반품 문서를 먼저 선택해주세요.", Severity.Warning);
            return;
        }
        if (_status != "Draft")
        {
            Snackbar.Add("draft 상태만 확정할 수 있습니다.", Severity.Warning);
            return;
        }

        // 구체적 영향 범위 표시 — UX/UI 팀 제안
        var itemCount = _draft.Lines.Count(l => !l.IsPlaceholder);
        var totalQty = _draft.Lines.Where(l => !l.IsPlaceholder).Sum(l => l.Quantity);

        var ok = await DialogService.ShowMessageBoxAsync(
            "⚠ 매입반품 확정 (Reverse OUT)",
            $"거래처: {_draft.SalesCompany}\n" +
            $"문서번호: {_draft.DocumentNumber}\n" +
            $"품목 수: {itemCount}개 · 총 수량: {totalQty:N1}\n" +
            $"반품 금액: {_summary.TotalAmount:N0}원\n\n" +
            $"→ 재고 {totalQty:N1}개 차감 (공급처로 반환)\n" +
            $"→ 재고원장에 Reverse OUT 기록\n\n" +
            $"확정하시겠습니까?",
            yesText: "반품확정", cancelText: "닫기");
        if (ok != true) return;

        _isConfirming = true;
        try
        {
            var (success, err) = await DeliveryService.ConfirmPurchaseReturnAsync(_draft.Id);
            if (success)
            {
                Snackbar.Add("반품 확정 완료 — Reverse 원장이 발행되었습니다.", Severity.Success);
                _status = "Confirmed";
                await InvokeAsync(StateHasChanged);
            }
            else
            {
                Snackbar.Add($"반품 확정 실패: {err}", Severity.Error);
            }
        }
        finally
        {
            _isConfirming = false;
        }
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
