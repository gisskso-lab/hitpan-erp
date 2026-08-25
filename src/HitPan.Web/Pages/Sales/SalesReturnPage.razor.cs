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

namespace HitPan.Web.Pages.SalesReturnUi;

public partial class SalesReturnPage : ComponentBase
{
    private List<PartnerListRow>? _partnerCache;
    private List<ItemListModel>? _itemCache;
    private DeliveryDraftModel? _draft;
    private DeliverySummaryModel _summary = new();
    private DeliveryLineModel? _selectedLine;
    private bool _hasUnsavedChanges;
    private IReadOnlyList<DeliveryWorkflowStepModel> _workflowSteps = Array.Empty<DeliveryWorkflowStepModel>();
    private string _status = "Draft";
    // 20260825작6: 매출반품 전용 화면이다. 콤보를 없앴으니 값도 고정이다.
    private readonly string _returnType = "sales_return";

    /// <summary>화면에 표시할 문서유형 — 고정값이라 사용자가 못 바꾼다 (20260825작6).</summary>
    private readonly string _documentTypeLabel = "반품확인서 (고객 반품)";
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
        if (_isSaving) return;
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
                vatAmount = l.VatAmount,
                // 20260825작6: 파손 로스 — 확정 시 재고 반영 여부를 가른다.
                isLoss = l.IsLoss
            })
            .ToList();

        if (items.Count == 0)
        {
            Snackbar.Add("반품 품목을 1건 이상 입력해주세요.", Severity.Warning);
            return;
        }

        // 봉합 (2026-06-22, 10차 재조사 NEW-1 P2): 종전엔 화면에서 입력받은 반품사유(_returnReason)·
        //   사유메모(_returnReasonMemo)·헤더메모가 payload 에서 누락(memo=null 하드코딩)돼 silent 유실됐다.
        //   백엔드 DTO(CreatePurchaseReturnRequest)는 ReturnReason·ReturnReasonMemo·Memo 를 모두 수용하므로
        //   화면 입력값을 그대로 전송한다(워크플로우는 정상이었고 사유 주석만 유실되던 결함).
        var payload = new
        {
            partnerId = partner.PartnerId,
            returnDate = _draft.SalesDate,
            memo = _draft.Memo,
            returnReason = _returnReason,
            returnReasonMemo = _returnReasonMemo,
            items
        };

        // 14차 P0 봉합(B안): 반품유형에 따라 매출반품(api/sales/returns)·매입반품(api/purchase/returns)
        //   경로로 분기한다. 종전엔 _returnType 무시하고 항상 매입반품으로 저장해, 판매반품 선택 시
        //   재고·잔액·회계가 3중 역방향으로 오염됐다(헌법 #20). 백엔드는 13차에 양쪽 다 완비됨.
        var isSalesReturn = _returnType == "sales_return";
        var basePath = isSalesReturn ? "api/sales/returns" : "api/purchase/returns";
        var docLabel = isSalesReturn ? "매출반품" : "매입반품";

        _isSaving = true;
        try
        {
            if (_isNew)
            {
                // 신규 반품 작성
                var resp = await Http.PostAsJsonAsync(basePath, payload);
                if (!resp.IsSuccessStatusCode)
                {
                    Snackbar.Add($"{docLabel} 저장 실패: {resp.StatusCode}", Severity.Error);
                    return;
                }
                var created = await resp.Content.ReadFromJsonAsync<ReturnCreatedResponse>();
                if (created is not null)
                {
                    _draft.Id = created.ReturnId;
                    _draft.DocumentNumber = created.ReturnNo;
                    _isNew = false;
                }
                Snackbar.Add($"{docLabel}을 저장했습니다.", Severity.Success);
            }
            else
            {
                // draft 반품 수정
                var resp = await Http.PutAsJsonAsync($"{basePath}/{_draft.Id}", payload);
                if (!resp.IsSuccessStatusCode)
                {
                    Snackbar.Add($"{docLabel} 수정 실패: {resp.StatusCode}", Severity.Error);
                    return;
                }
                Snackbar.Add($"{docLabel}을 수정했습니다.", Severity.Success);
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
        finally
        {
            _isSaving = false;
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
        if (_isDeleting) return;

        if (!_isNew && _draft is not null && !string.IsNullOrWhiteSpace(_draft.Id))
        {
            _isDeleting = true;
            try
            {
                // 14차 P0 봉합(B안): 반품유형에 따라 삭제 경로 분기.
                var deletePath = _returnType == "sales_return" ? "api/sales/returns" : "api/purchase/returns";
                using var resp = await Http.DeleteAsync($"{deletePath}/{Uri.EscapeDataString(_draft.Id)}");
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
            finally
            {
                _isDeleting = false;
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
        // 14차 P0 봉합(B안): 반품유형에 따라 매출반품·매입반품 목록을 분기해 연다.
        var isSalesReturn = _returnType == "sales_return";
        var options = new DialogOptions { MaxWidth = MaxWidth.ExtraLarge, FullWidth = true, CloseButton = true };
        var parameters = new DialogParameters { ["IsSalesReturn"] = isSalesReturn };
        var title = isSalesReturn ? "매출반품 목록" : "매입반품 목록";
        var dlg = await DialogService.ShowAsync<PurchaseReturnList>(title, parameters, options);
        var result = await dlg.Result;
        if (result is null || result.Canceled) return;

        var returnId = result.Data as string;
        if (string.IsNullOrWhiteSpace(returnId)) return;

        await LoadReturnAsync(returnId, isSalesReturn);
    }

    /// <summary>서버에서 매출반품 단건을 읽어 편집 화면에 주입한다 (20260825작6).</summary>
    /// <remarks>매출 전용 화면이라 분기가 없다 — 로스 표시를 살리려 전용 메서드를 쓴다.</remarks>
    private async Task LoadReturnAsync(string returnId, bool isSalesReturn = true)
    {
        var detail = await DeliveryService.GetSalesReturnDetailForSalesAsync(returnId);
        if (detail is null)
        {
            Snackbar.Add("반품 문서를 불러오지 못했습니다.", Severity.Error);
            return;
        }
        // 20260825작6: 이 화면은 매출반품 전용이라 재설정이 없다 — _returnType 은 고정값이다.

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
                // 20260825작6: 저장된 로스 표시를 되살린다 — 안 하면 다시 열 때 체크가 사라진다.
                IsLoss = it.IsLoss,
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

    /// <summary>삭제 연타 차단 (20260825작4) — 같은 문서에 삭제 요청이 두 번 간다.</summary>
    private bool _isDeleting;

    /// <summary>저장 연타 차단 (20260825작4) — 연타하면 반품 전표가 중복 생성된다.</summary>
    private bool _isSaving;

    /// <summary>확정·확정취소 연타 차단. 취소는 진입 가드가 빠져 있었다 (20260825작4).</summary>
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

        // 14차 P0 봉합(B안): 매출반품은 재고가 증가(Reverse IN), 매입반품은 차감(Reverse OUT).
        //   확정 다이얼로그 문구·확정 API를 반품유형에 맞게 분기한다.
        var isSalesReturn = _returnType == "sales_return";
        var confirmTitle = isSalesReturn ? "⚠ 매출반품 확정 (Reverse IN)" : "⚠ 매입반품 확정 (Reverse OUT)";
        var stockLine = isSalesReturn
            ? $"→ 재고 {totalQty:N1}개 증가 (고객 반품 입고)\n→ 재고원장에 Reverse IN 기록\n"
            : $"→ 재고 {totalQty:N1}개 차감 (공급처로 반환)\n→ 재고원장에 Reverse OUT 기록\n";

        var ok = await DialogService.ShowMessageBoxAsync(
            confirmTitle,
            $"거래처: {_draft.SalesCompany}\n" +
            $"문서번호: {_draft.DocumentNumber}\n" +
            $"품목 수: {itemCount}개 · 총 수량: {totalQty:N1}\n" +
            $"반품 금액: {_summary.TotalAmount:N0}원\n\n" +
            stockLine + "\n" +
            $"확정하시겠습니까?",
            yesText: "반품확정", cancelText: "닫기");
        if (ok != true) return;

        _isConfirming = true;
        try
        {
            var (success, err) = isSalesReturn
                ? await DeliveryService.ConfirmSalesReturnAsync(_draft.Id)
                : await DeliveryService.ConfirmPurchaseReturnAsync(_draft.Id);
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

    // 반품 취소 — 확정(confirmed)된 반품을 되돌린다(15차 적대검증 15-P1 봉합).
    //   확정의 정확한 역행: 매출반품 취소=재고 다시 차감(Reverse OUT), 매입반품 취소=재고 다시 증가(Reverse IN).
    //   잘못 확정한 반품을 원장 무결성 유지하며 되돌리는 유일한 경로 — 삭제(draft 전용)와 구분된다.
    private async Task CancelReturnAsync()
    {
        if (_isConfirming) return;
        if (_draft is null || string.IsNullOrEmpty(_draft.Id))
        {
            Snackbar.Add("저장된 반품 문서를 먼저 선택해주세요.", Severity.Warning);
            return;
        }
        if (_status != "Confirmed")
        {
            Snackbar.Add("확정된 반품만 취소할 수 있습니다.", Severity.Warning);
            return;
        }

        var isSalesReturn = _returnType == "sales_return";
        var totalQty = _draft.Lines.Where(l => !l.IsPlaceholder).Sum(l => l.Quantity);
        var cancelTitle = isSalesReturn ? "⚠ 매출반품 취소 (확정 되돌림)" : "⚠ 매입반품 취소 (확정 되돌림)";
        var stockLine = isSalesReturn
            ? $"→ 재고 {totalQty:N1}개 차감 (반품 입고를 되돌림)\n→ 재고원장에 Reverse OUT 기록\n"
            : $"→ 재고 {totalQty:N1}개 증가 (반환을 되돌림)\n→ 재고원장에 Reverse IN 기록\n";

        var ok = await DialogService.ShowMessageBoxAsync(
            cancelTitle,
            $"거래처: {_draft.SalesCompany}\n" +
            $"문서번호: {_draft.DocumentNumber}\n" +
            $"반품 금액: {_summary.TotalAmount:N0}원\n\n" +
            stockLine + "\n" +
            $"확정을 취소하시겠습니까? 재고·잔액·회계가 확정 전으로 복원됩니다.",
            yesText: "반품취소", cancelText: "닫기");
        if (ok != true) return;

        _isConfirming = true;
        try
        {
            var (success, err) = isSalesReturn
                ? await DeliveryService.CancelSalesReturnAsync(_draft.Id)
                : await DeliveryService.CancelPurchaseReturnAsync(_draft.Id);
            if (success)
            {
                Snackbar.Add("반품 취소 완료 — 확정 원장이 복원(역행)되었습니다.", Severity.Success);
                _status = "Canceled";
                await InvokeAsync(StateHasChanged);
            }
            else
            {
                Snackbar.Add($"반품 취소 실패: {err}", Severity.Error);
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
