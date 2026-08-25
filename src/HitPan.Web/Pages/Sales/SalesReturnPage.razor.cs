using Microsoft.JSInterop;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using HitPan.Web.Components.Common;
using HitPan.Web.Components.Purchase;
// 20260825작7: 「판매불러오기」가 판매 목록 다이얼로그를 연다.
using HitPan.Web.Components.Sales;
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

    // ─────────────────────────────────────────────────────────────────
    // 20260825작7 — 원 거래명세서 연결
    // 사장님 오더: "판매목록 불러오는 버튼이 있어야 됨 → 당연히 반품확인서에 자동반영"
    // ─────────────────────────────────────────────────────────────────

    /// <summary>이 반품이 어느 거래명세서에서 왔는지. 단독 반품이면 null.</summary>
    private string? _linkedDeliveryId;

    /// <summary>화면에 보여줄 원 거래명세서 번호. 불러온 직후에만 채워진다.</summary>
    private string? _linkedDeliveryNo;

    /// <summary>판매 불러오기 연타 차단 — 두 번 누르면 품목이 두 배로 들어간다.</summary>
    private bool _isLoadingDelivery;

    /// <summary>
    /// 판매목록조회 「반품」 버튼이 넘겨주는 거래명세서 (20260825작7).
    /// </summary>
    /// <remarks>
    /// 사장님 오더: <i>"거래명세서 판매목록조회에도 반품으로 상태변경하는 버튼이 있어야됨.
    /// → 당연히 반품확인서에 자동반영"</i>
    /// 값이 있으면 화면이 열리면서 그 거래의 품목을 바로 채운다.
    /// </remarks>
    [SupplyParameterFromQuery(Name = "deliveryId")]
    public string? DeliveryIdParam { get; set; }

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

        // 20260825작7: 판매목록에서 「반품」으로 들어온 경우 바로 품목을 채운다.
        //   다이얼로그를 다시 띄우지 않는다 — 사용자는 이미 거래를 골랐다.
        if (!string.IsNullOrWhiteSpace(DeliveryIdParam))
        {
            await FillFromDeliveryAsync(DeliveryIdParam!, null);
        }
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
                isLoss = l.IsLoss,
                // 20260825작7: 원 판매 줄 연결. 직접 입력한 줄은 null 이다.
                //   종전엔 payload 에 이 항목이 아예 없어서, 백엔드가 받을 준비를 다 해놓고도
                //   delivery_item_id 가 항상 NULL 로 들어갔다.
                deliveryItemId = string.IsNullOrWhiteSpace(l.DeliveryItemId) ? null : l.DeliveryItemId
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
            // 20260825작7: 원 거래명세서 연결을 함께 보낸다.
            //   백엔드 INSERT/UPDATE 는 @DeliveryId 를 정상 처리하는데 화면이 안 보내고 있었다.
            //   그래서 지금까지 만들어진 반품확인서는 전부 delivery_id 가 NULL 이다.
            deliveryId = string.IsNullOrWhiteSpace(_linkedDeliveryId) ? null : _linkedDeliveryId,
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

    /// <summary>
    /// 판매 불러오기 (20260825작7) — 거래명세서를 골라 그 품목을 반품확인서로 옮긴다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 사장님 오더: <i>"판매목록 불러오는 버튼이 있어야 됨 → 당연히 반품확인서에 자동반영"</i>
    /// </para>
    /// <para>
    /// 🔴 <b>확정은 사람이 한다.</b> 여기서는 초안까지만 채운다 —
    /// 실제 반품 수량은 판 수량보다 적은 게 보통이고, 파손 판정도 사람이 봐야 한다.
    /// </para>
    /// <para>
    /// <b>로스는 전부 해제 상태로 시작한다.</b> 사장님: <i>"로스판정 기준은 고객사가 정하는거지, 너가 왜 정해."</i>
    /// 반품사유로 파손을 추측하지 않는다.
    /// </para>
    /// </remarks>
    private async Task LoadFromDeliveryAsync()
    {
        // 연타하면 같은 품목이 두 번 들어간다.
        if (_isLoadingDelivery) return;
        if (_draft is null) return;

        if (!string.Equals(_status, "Draft", StringComparison.OrdinalIgnoreCase))
        {
            Snackbar.Add("확정된 반품확인서는 품목을 바꿀 수 없습니다.", Severity.Warning);
            return;
        }

        var options = new DialogOptions { MaxWidth = MaxWidth.ExtraLarge, FullWidth = true, CloseButton = true };
        var dlg = await DialogService.ShowAsync<SalesListDialog>("판매 목록에서 불러오기", new DialogParameters(), options);
        var result = await dlg.Result;
        if (result is null || result.Canceled) return;

        if (result.Data is not SalesListItem picked || string.IsNullOrWhiteSpace(picked.OrderId)) return;

        // 헌법 #6 — 판 적 없는 건은 반품 대상이 아니다.
        if (string.Equals(picked.Status, "draft", StringComparison.OrdinalIgnoreCase))
        {
            Snackbar.Add("판매확정 전 거래는 반품할 수 없습니다. 먼저 판매확정 해주세요.", Severity.Warning);
            return;
        }

        // 헌법 #1 — 이미 입력한 줄을 말없이 덮어쓰지 않는다.
        var existing = _draft.Lines.Count(l => !l.IsPlaceholder && !string.IsNullOrWhiteSpace(l.ItemId));
        if (existing > 0)
        {
            var ok = await DialogService.ShowMessageBoxAsync(
                "품목 교체",
                $"이미 입력된 품목 {existing}건이 있습니다.\n\n" +
                $"[{picked.OrderNo}] 의 품목으로 바꾸시겠습니까?",
                yesText: "바꾸기", cancelText: "취소");
            if (ok != true) return;
        }

        await FillFromDeliveryAsync(picked.OrderId, picked.OrderNo);
    }

    /// <summary>
    /// 거래명세서 한 건의 품목을 반품확인서 줄로 옮긴다 (20260825작7).
    /// </summary>
    /// <remarks>
    /// 「판매불러오기」 버튼과 판매목록 「반품」 버튼이 <b>같은 이 경로</b>를 쓴다 —
    /// 두 벌로 두면 한쪽만 고쳐지는 날이 온다.
    /// </remarks>
    /// <param name="deliveryId">원 거래명세서 ID.</param>
    /// <param name="deliveryNo">화면에 보여줄 전표번호. 모르면 null(서버 값으로 채운다).</param>
    private async Task FillFromDeliveryAsync(string deliveryId, string? deliveryNo)
    {
        if (_isLoadingDelivery) return;
        if (_draft is null) return;

        _isLoadingDelivery = true;
        try
        {
            var detail = await DeliveryService.GetAsync(deliveryId);
            if (detail is null)
            {
                Snackbar.Add("거래명세서를 불러오지 못했습니다.", Severity.Error);
                return;
            }

            // 전표번호를 못 받아 왔으면 서버가 준 값을 쓴다 — 지어내지 않는다(20260825작5 계승).
            var docNo = string.IsNullOrWhiteSpace(deliveryNo) ? detail.DeliveryNo : deliveryNo!;

            if (detail.Items.Count == 0)
            {
                Snackbar.Add($"[{docNo}] 에 품목이 없습니다.", Severity.Warning);
                return;
            }

            var lines = new List<DeliveryLineModel>();
            var no = 1;
            foreach (var it in detail.Items)
            {
                lines.Add(new DeliveryLineModel
                {
                    No = no,
                    RowNo = no,
                    ItemId = it.ItemId,
                    ItemName = it.ItemName,
                    Spec = it.Spec ?? string.Empty,
                    Unit = string.IsNullOrWhiteSpace(it.Unit) ? "EA" : it.Unit!,
                    Quantity = it.Qty,
                    // 반품은 판 값으로 돌려준다 — 여기서 단가를 다시 계산하면 환불액이 어긋난다.
                    UnitPrice = it.UnitPrice,
                    Warehouse = it.WarehouseId ?? string.Empty,
                    // 어느 판매 줄에서 왔는지 — 원단가 추적의 근거다.
                    DeliveryItemId = it.DeliveryItemId,
                    // 파손 판정은 고객사가 한다. 우리가 미리 켜두지 않는다.
                    IsLoss = false,
                    IsPlaceholder = false
                });
                no++;
            }
            lines.Add(new DeliveryLineModel { No = no, RowNo = no, IsPlaceholder = true });

            _draft.Lines = lines;
            _draft.PartnerId = detail.PartnerId;
            _draft.SalesCompany = detail.PartnerName;
            _linkedDeliveryId = deliveryId;
            _linkedDeliveryNo = docNo;
            _selectedLine = null;

            MarkDirty();
            RecalculateSummary();
            RefreshWorkflow();

            if (TabService.ActiveTabId is { } tabId)
            {
                TabService.UpdateSubTitle(tabId, _draft.SalesCompany);
            }

            Snackbar.Add(
                $"[{docNo}] 품목 {detail.Items.Count}건을 불러왔습니다. " +
                "반품 수량과 파손 여부를 확인한 뒤 저장해주세요.",
                Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"판매 불러오기 오류: {ex.Message}", Severity.Error);
        }
        finally
        {
            _isLoadingDelivery = false;
            await InvokeAsync(StateHasChanged);
        }
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
                // 20260825작7: 원 판매 줄 연결을 되살린다 — 안 하면 다시 저장할 때 줄 링크가 끊긴다.
                DeliveryItemId = it.DeliveryItemId,
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

        // 20260825작7: 원 거래명세서 연결을 되살린다.
        //   이걸 안 하면 저장된 반품확인서를 다시 열어 고치는 순간 링크가 빈 값으로 저장돼
        //   사장님 오더 "당연히 반품확인서에 자동반영" 이 두 번째 저장에서 깨진다.
        _linkedDeliveryId = detail.DeliveryId;
        _linkedDeliveryNo = null;
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
