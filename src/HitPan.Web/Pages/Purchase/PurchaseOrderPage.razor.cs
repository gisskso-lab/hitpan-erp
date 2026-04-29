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

namespace HitPan.Web.Pages.PurchaseOrderUi;

/// <summary>
/// 발주서 메인 화면을 제공한다.
/// 수주서(SalesOrderPage) 레이아웃을 계승하고 납기일·발주 저장 흐름을 추가한다.
/// </summary>
public partial class PurchaseOrderPage : ComponentBase
{
    // 거래처 캐시
    private List<PartnerListRow>? _partnerCache;

    // 품목 캐시
    private List<ItemListModel>? _itemCache;

    // 편집 중인 발주 초안(라인·문서번호 등은 거래명세와 동일한 Draft 모델을 재사용한다).
    private DeliveryDraftModel? _draft;

    // 발주 전용 납기일(서버 CreatePurchaseOrderRequest.ExpectedDate 에 매핑). DeliveryDraftModel 은 수정 금지이므로 페이지 필드로 둔다.
    private DateTime? _deliveryDueDate;

    // 푸터 합계
    private DeliverySummaryModel _summary = new();

    // 그리드 선택 라인
    private DeliveryLineModel? _selectedLine;

    // 탭·이탈 경고용 더티 플래그
    private bool _hasUnsavedChanges;

    // 워크플로 단계 표시
    private IReadOnlyList<DeliveryWorkflowStepModel> _workflowSteps = Array.Empty<DeliveryWorkflowStepModel>();

    // Draft / Confirmed 등 그리드 읽기 전용 전환용
    private string _status = "Draft";

    // 신규 문서 여부다.
    private bool _isNew = true;

    /// <summary>
    /// 초기 진입 시 신규 발주 초안을 구성한다.
    /// </summary>
    /// <returns>초기화 작업</returns>
    protected override async Task OnInitializedAsync()
    {
        _itemCache = await ItemsApi.GetListAsync() ?? new();
        _draft = new DeliveryDraftModel
        {
            Id = Guid.NewGuid().ToString(),
            DocumentType = "발주",
            SalesDate = DateTime.Today,
            ManagerName = "담당자",
            Lines = new List<DeliveryLineModel>
            {
                new() { No = 1, IsPlaceholder = true }
            }
        };

        // 납기일 기본값: 발주일 기준 7일 후(사용자가 변경 가능).
        _deliveryDueDate = DateTime.Today.AddDays(7);

        RefreshWorkflow();
        RecalculateSummary();
    }

    /// <summary>
    /// 공급처 자동완성 검색을 수행한다.
    /// </summary>
    private async Task<IEnumerable<string>> SearchPartnerAsync(string value, CancellationToken ct)
    {
        _partnerCache ??= await PartnersApi.GetListAsync() ?? new();
        if (string.IsNullOrWhiteSpace(value)) return _partnerCache.Select(p => p.PartnerName).Distinct();
        return _partnerCache.Where(p => p.PartnerName.Contains(value, StringComparison.OrdinalIgnoreCase)).Select(p => p.PartnerName).Distinct();
    }

    /// <summary>
    /// SPA 내부 라우팅 직전 미저장 여부를 확인한다.
    /// </summary>
    /// <param name="context">라우팅 컨텍스트</param>
    /// <returns>다이얼로그 대기 작업</returns>
    private async Task OnBeforeInternalNavigationAsync(LocationChangingContext context)
    {
        // 더티가 아니면 확인 없이 이동한다.
        if (!_hasUnsavedChanges)
        {
            return;
        }

        var leave = await DialogService.ShowMessageBoxAsync(
            "확인",
            "저장하지 않은 발주 내용이 있습니다. 이동하시겠습니까?",
            yesText: "이동",
            noText: "취소");

        // 취소 시에만 네비게이션을 막는다.
        if (leave != true)
        {
            context.PreventNavigation();
        }
    }

    /// <summary>
    /// 발주일 변경 시 초안과 워크플로를 갱신한다.
    /// </summary>
    /// <param name="value">선택한 일자</param>
    /// <returns>UI 갱신</returns>
    private async Task OnOrderDateChangedAsync(DateTime? value)
    {
        if (_draft is null)
        {
            return;
        }

        _draft.SalesDate = value ?? DateTime.Today;
        MarkDirty();
        RefreshWorkflow();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 납기일 변경 시 페이지 상태만 갱신한다(별도 Draft 필드 없음).
    /// </summary>
    /// <param name="value">납기일</param>
    /// <returns>UI 갱신</returns>
    private async Task OnDeliveryDueDateChangedAsync(DateTime? value)
    {
        // null 이면 당일로 두어 DatePicker 가 비지 않게 한다.
        _deliveryDueDate = value ?? DateTime.Today;
        MarkDirty();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 공급처명 변경 시 탭 부제목과 초안을 맞춘다.
    /// </summary>
    /// <param name="value">공급처명</param>
    /// <returns>UI 갱신</returns>
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
    /// <param name="value">메모</param>
    /// <returns>UI 갱신</returns>
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
    /// 그리드 변경 후 합계·더티를 반영한다.
    /// </summary>
    /// <returns>UI 갱신</returns>
    private async Task OnGridChangedAsync()
    {
        RecalculateSummary();
        MarkDirty();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 그리드에서 선택된 라인을 보관한다.
    /// </summary>
    /// <param name="line">선택 라인</param>
    /// <returns>완료된 작업</returns>
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

    /// <summary>
    /// 툴바에서 빈 라인을 추가한다.
    /// </summary>
    /// <returns>UI 갱신</returns>
    private async Task AddNewAsync()
    {
        if (_draft is null)
        {
            return;
        }

        _draft.Lines.RemoveAll(x => x.IsPlaceholder);
        var next = _draft.Lines.Count + 1;
        _draft.Lines.Add(new DeliveryLineModel { No = next, RowNo = next });
        MarkDirty();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 발주를 저장한다. 서버 PurchaseController POST 가 성공하면 Id 를 반영하고, 실패 시 로컬 채번 스텁으로 폴백한다.
    /// </summary>
    /// <returns>HTTP 또는 스텁 처리</returns>
    private async Task SaveAsync()
    {
        if (_draft is null)
        {
            return;
        }

        var lines = _draft.Lines.Where(x => !x.IsPlaceholder).ToList();
        // 저장 가능한 라인이 없으면 API 호출을 하지 않는다.
        if (lines.Count == 0)
        {
            Snackbar.Add("저장할 라인이 없습니다.", Severity.Warning);
            return;
        }

        // PartnerId 없이는 서버 검증에 걸릴 수 있으므로 먼저 차단한다.
        if (string.IsNullOrWhiteSpace(_draft.PartnerId))
        {
            // 추후 API 연동 필요: 공급처 자동완성으로 PartnerId 를 채우는 UX를 추가하면 본 검증을 완화할 수 있다.
            Snackbar.Add("공급처 PartnerId 가 없습니다. 거래처 마스터 연동·자동완성 추가 후 저장하세요.", Severity.Warning);
            _draft.DocumentNumber ??= $"PO-{DateTime.Now:yyyyMMdd}-LOCAL";
            _hasUnsavedChanges = false;
            if (TabService.ActiveTabId is { } tabIdLocal)
            {
                TabService.SetTabDirty(tabIdLocal, false);
            }

            return;
        }

        var request = new PurchaseOrderCreateJsonRequest
        {
            PartnerId = _draft.PartnerId!,
            EmployeeId = null,
            PoDate = _draft.SalesDate,
            ExpectedDate = _deliveryDueDate,
            Memo = _draft.Memo,
            Items = lines.Select(static x => new PurchaseOrderCreateItemJson
            {
                ItemId = x.ItemId,
                OrderedQty = x.Qty,
                UnitPrice = x.UnitPrice,
                SupplyAmount = x.Amount,
                VatAmount = x.VatAmount,
                WarehouseId = string.IsNullOrWhiteSpace(x.Warehouse) ? null : x.Warehouse
            }).ToList()
        };

        try
        {
            using var resp = await Http.PostAsJsonAsync("api/purchase/orders", request);
            // 성공 시 Created 이며 본문에 id 가 온다.
            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadFromJsonAsync<PurchaseOrderCreateJsonResponse>();
                if (!string.IsNullOrWhiteSpace(body?.Id))
                {
                    _draft.Id = body.Id;
                }

                _draft.DocumentNumber ??= $"PO-{_draft.SalesDate:yyyyMMdd}-API";
                _isNew = false;
                _hasUnsavedChanges = false;
                _status = "Draft";
                if (TabService.ActiveTabId is { } tabId)
                {
                    TabService.SetTabDirty(tabId, false);
                    TabService.UpdateSubTitle(tabId, _draft.SalesCompany);
                }

                Snackbar.Add("발주서가 서버에 저장되었습니다.", Severity.Success);
                return;
            }

            // 403 등은 권한 정책(PurchaseOnly) 문제일 수 있다.
            Snackbar.Add($"발주 API 응답: {(int)resp.StatusCode}. 로컬 채번으로 표시합니다.", Severity.Warning);
        }
        catch (Exception ex)
        {
            // 네트워크·직렬화 오류 시에도 사용자 입력은 잃지 않도록 스텁만 적용한다.
            Snackbar.Add($"발주 API 호출 실패: {ex.Message}", Severity.Warning);
        }

        // 추후 API 연동 필요: 재시도·상세 오류 메시지 표시.
        _draft.DocumentNumber ??= $"PO-{DateTime.Now:yyyyMMdd}-001";
        _hasUnsavedChanges = false;
        _status = "Draft";
        if (TabService.ActiveTabId is { } tabIdFallback)
        {
            TabService.SetTabDirty(tabIdFallback, false);
            TabService.UpdateSubTitle(tabIdFallback, _draft.SalesCompany);
        }

        Snackbar.Add("로컬 채번으로 저장 상태를 표시했습니다.", Severity.Info);
    }

    /// <summary>
    /// 편집을 취소하고 신규이면 초기화, 기존이면 재로드한다.
    /// </summary>
    /// <returns>비동기 작업</returns>
    private async Task CancelAsync()
    {
        if (!_isNew && _draft is not null && !string.IsNullOrWhiteSpace(_draft.Id))
        {
            // TODO: 발주서 상세 조회 API 연동 후 서버에서 다시 로드.
            // 현재는 더티 플래그만 해제한다.
        }
        else
        {
            _draft = new DeliveryDraftModel
            {
                Id = Guid.NewGuid().ToString(),
                DocumentType = "발주",
                SalesDate = DateTime.Today,
                ManagerName = "담당자",
                Lines = new List<DeliveryLineModel> { new() { No = 1, IsPlaceholder = true } }
            };
            _deliveryDueDate = DateTime.Today.AddDays(7);
            _isNew = true;
            RecalculateSummary();
            RefreshWorkflow();
        }

        _hasUnsavedChanges = false;
        if (TabService.ActiveTabId is { } tabId)
        {
            TabService.SetTabDirty(tabId, false);
        }

        Snackbar.Add("변경사항을 취소했습니다.", Severity.Info);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 삭제 확인 후 초기화한다.
    /// </summary>
    /// <returns>다이얼로그 처리</returns>
    private async Task DeleteConfirmAsync()
    {
        var ok = await DialogService.ShowMessageBoxAsync(
            "삭제 확인",
            "현재 발주서를 삭제하고 새 문서로 초기화하시겠습니까?",
            yesText: "삭제",
            cancelText: "취소");

        if (ok == true)
        {
            await DeleteAsync();
        }
    }

    /// <summary>
    /// 발주서를 삭제하고 신규 상태로 재생성한다.
    /// </summary>
    /// <returns>UI 갱신</returns>
    private async Task DeleteAsync()
    {
        if (!_isNew && _draft is not null && !string.IsNullOrWhiteSpace(_draft.Id))
        {
            try
            {
                using var resp = await Http.DeleteAsync($"api/purchase/orders/{Uri.EscapeDataString(_draft.Id)}");
                if (!resp.IsSuccessStatusCode)
                {
                    Snackbar.Add("삭제에 실패했습니다.", Severity.Error);
                    return;
                }
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
            DocumentType = "발주",
            SalesDate = DateTime.Today,
            ManagerName = "담당자",
            Lines = new List<DeliveryLineModel> { new() { No = 1, IsPlaceholder = true } }
        };
        _deliveryDueDate = DateTime.Today.AddDays(7);
        _isNew = true;
        _selectedLine = null;
        _hasUnsavedChanges = false;
        _status = "Draft";
        RecalculateSummary();
        RefreshWorkflow();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 현재 발주서를 매입명세서(입고)로 전환한다.
    /// </summary>
    private async Task ConvertToReceiptAsync()
    {
        if (_draft is null || string.IsNullOrWhiteSpace(_draft.Id) || _isNew)
        {
            Snackbar.Add("저장된 발주서를 먼저 선택해주세요.", Severity.Warning);
            return;
        }

        var confirm = await DialogService.ShowMessageBoxAsync(
            "매입전환 확인",
            "현재 발주서를 매입명세서로 전환하시겠습니까?",
            yesText: "전환",
            cancelText: "취소");

        if (confirm != true)
        {
            return;
        }

        try
        {
            var result = await DeliveryService.ConvertOrderToReceiptAsync(_draft.Id);
            if (result is null)
            {
                Snackbar.Add("매입전환에 실패했습니다.", Severity.Error);
                return;
            }

            Snackbar.Add($"매입명세서 {result.ReceiptNo} 가 생성되었습니다.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"매입전환 중 오류: {ex.Message}", Severity.Error);
        }
    }

    /// <summary>브라우저 인쇄 대화상자를 연다.</summary>
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
            ["DocumentType"] = "발주서",
            ["DocumentTypeKey"] = "purchase_order",
            ["DocumentNo"] = _draft?.DocumentNumber ?? "신규",
            ["DocumentId"] = _draft?.Id ?? "",
            ["PartnerId"] = _draft?.PartnerId ?? ""
        };
        var options = new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true, CloseButton = true };
        await DialogService.ShowAsync<EmailSendDialog>("이메일 발송", parameters, options);
    }

    /// <summary>
    /// 엑셀 다운로드 요청을 문서 서비스에 위임한다.
    /// </summary>
    /// <returns>다운로드 작업</returns>
    private async Task DownloadExcelAsync()
    {
        if (_draft is null || string.IsNullOrWhiteSpace(_draft.Id))
        {
            Snackbar.Add("저장된 발주서를 먼저 선택해주세요.", Severity.Warning);
            return;
        }

        // 추후 API 연동 필요: docType 키는 서버 문서 모듈과 맞춰야 한다.
        await DocService.DownloadExcelAsync("purchase-order", _draft.Id);
    }

    /// <summary>
    /// PDF 다운로드 요청을 문서 서비스에 위임한다.
    /// </summary>
    /// <returns>다운로드 작업</returns>
    private async Task DownloadPdfAsync()
    {
        if (_draft is null || string.IsNullOrWhiteSpace(_draft.Id))
        {
            Snackbar.Add("저장된 발주서를 먼저 선택해주세요.", Severity.Warning);
            return;
        }

        await DocService.DownloadPdfAsync("purchase-order", _draft.Id);
    }

    /// <summary>
    /// 발주 목록 다이얼로그를 연다.
    /// </summary>
    /// <returns>다이얼로그 표시</returns>
    private async Task OpenListAsync()
    {
        var options = new DialogOptions { MaxWidth = MaxWidth.ExtraLarge, FullWidth = true, CloseButton = true };
        var dlg = await DialogService.ShowAsync<PurchaseOrderList>("발주서 목록", options);
        var result = await dlg.Result;
        if (result is null || result.Canceled) return;

        var poId = result.Data as string;
        if (string.IsNullOrWhiteSpace(poId)) return;

        await LoadOrderAsync(poId);
    }

    /// <summary>
    /// 서버에서 발주서 단건을 읽어 편집 화면(_draft)에 주입한다.
    /// </summary>
    private async Task LoadOrderAsync(string poId)
    {
        var detail = await DeliveryService.GetPurchaseOrderDetailAsync(poId);
        if (detail is null)
        {
            Snackbar.Add("발주서를 불러오지 못했습니다.", Severity.Error);
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
                Quantity = it.OrderedQty,
                UnitPrice = it.UnitPrice,
                Warehouse = it.WarehouseId ?? string.Empty,
                IsPlaceholder = false
            });
        }
        lines.Add(new DeliveryLineModel { No = no, IsPlaceholder = true });

        _draft = new DeliveryDraftModel
        {
            Id = detail.PoId,
            DocumentType = "발주",
            DocumentNumber = detail.PoNo,
            SalesDate = detail.PoDate,
            ManagerName = "담당자",
            PartnerId = detail.PartnerId,
            SalesCompany = detail.PartnerName,
            Memo = detail.Memo,
            Status = detail.Status,
            Lines = lines
        };
        _deliveryDueDate = detail.ExpectedDate;
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

        Snackbar.Add($"[{detail.PoNo}] 불러왔습니다.", Severity.Info);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 발주 문맥으로 브레드크럼을 재구성한다.
    /// </summary>
    private void RefreshWorkflow()
    {
        if (_draft is null)
        {
            return;
        }

        _workflowSteps = DeliveryWorkflowFactory.Build("발주", _draft);
    }

    /// <summary>
    /// 라인 합계를 요약 모델에 반영한다.
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
        _summary.TodaySales = _summary.TotalAmount;
        _summary.ClaimAmount = _summary.TotalAmount;
    }

    /// <summary>
    /// 더티 플래그와 탭 서비스를 동기화한다.
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

/// <summary>
/// api/purchase/orders POST 본문(Application 의 CreatePurchaseOrderRequest 와 동일 필드)을 Web 프로젝트에서 직렬화하기 위한 DTO.
/// </summary>
public sealed class PurchaseOrderCreateJsonRequest
{
    // 공급처 Id
    [JsonPropertyName("partnerId")]
    public string PartnerId { get; set; } = string.Empty;

    // 담당 직원 Id
    [JsonPropertyName("employeeId")]
    public string? EmployeeId { get; set; }

    // 발주일
    [JsonPropertyName("poDate")]
    public DateTime PoDate { get; set; }

    // 납기 예정일
    [JsonPropertyName("expectedDate")]
    public DateTime? ExpectedDate { get; set; }

    // 비고
    [JsonPropertyName("memo")]
    public string? Memo { get; set; }

    // 발주 라인 목록
    [JsonPropertyName("items")]
    public List<PurchaseOrderCreateItemJson> Items { get; set; } = new();
}

/// <summary>
/// 발주 라인 항목 JSON DTO(CreatePurchaseOrderItemRequest 대응).
/// </summary>
public sealed class PurchaseOrderCreateItemJson
{
    // 품목 Id
    [JsonPropertyName("itemId")]
    public string ItemId { get; set; } = string.Empty;

    // 발주 수량
    [JsonPropertyName("orderedQty")]
    public decimal OrderedQty { get; set; }

    // 단가
    [JsonPropertyName("unitPrice")]
    public decimal UnitPrice { get; set; }

    // 공급가액
    [JsonPropertyName("supplyAmount")]
    public decimal SupplyAmount { get; set; }

    // 부가세
    [JsonPropertyName("vatAmount")]
    public decimal VatAmount { get; set; }

    // 입고 창고 Id
    [JsonPropertyName("warehouseId")]
    public string? WarehouseId { get; set; }
}

/// <summary>
/// 발주 생성 응답(본문은 { "id": "..." } 형태).
/// </summary>
public sealed class PurchaseOrderCreateJsonResponse
{
    // 생성된 발주 Id
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}
