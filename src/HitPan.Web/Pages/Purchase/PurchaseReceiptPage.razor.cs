using System.Net.Http.Json;
using System.Text.Json.Serialization;
using HitPan.Web.Components.Purchase;
using HitPan.Web.Models;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;

namespace HitPan.Web.Pages.PurchaseReceiptUi;

/// <summary>
/// 매입명세서 메인 화면을 제공한다.
/// 발주서(PurchaseOrderPage) 레이아웃을 계승하고 입고일·입고창고·입고 저장(CreateReceiptRequest) 흐름을 추가한다.
/// </summary>
public partial class PurchaseReceiptPage : ComponentBase
{
    // 거래처 캐시
    private List<PartnerListRow>? _partnerCache;

    // 품목 캐시
    private List<ItemListModel>? _itemCache;

    // 편집 중인 매입명세 초안(라인 모델은 발주와 동일한 DeliveryLineModel 을 재사용한다).
    private DeliveryDraftModel? _draft;

    // 서버 CreateReceiptRequest.ReceiptDate 에 매핑하는 입고일(DeliveryDraftModel 은 수정 금지이므로 페이지 필드).
    private DateTime? _receiptDate;

    // 라인에 창고가 비어 있을 때 CreateReceiptItemRequest.WarehouseId 에 넣을 헤더 기본 창고 Id.
    private string _headerWarehouseId = string.Empty;

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

    /// <summary>
    /// 초기 진입 시 신규 매입명세 초안을 구성한다.
    /// </summary>
    /// <returns>초기화 작업</returns>
    protected override async Task OnInitializedAsync()
    {
        _itemCache = await ItemsApi.GetListAsync() ?? new();
        _draft = new DeliveryDraftModel
        {
            Id = Guid.NewGuid().ToString(),
            DocumentType = "매입",
            SalesDate = DateTime.Today,
            ManagerName = "담당자",
            Lines = new List<DeliveryLineModel>
            {
                new() { No = 1, IsPlaceholder = true }
            }
        };

        // 입고일은 당일로 시작한다.
        _receiptDate = DateTime.Today;
        _draft.SalesDate = _receiptDate.Value;

        // 스텁 기본 창고 Id(추후 창고 마스터 API 로 대체).
        _headerWarehouseId = "MAIN";

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
            "저장하지 않은 매입명세 내용이 있습니다. 이동하시겠습니까?",
            yesText: "이동",
            noText: "취소");

        // 취소 시에만 네비게이션을 막는다.
        if (leave != true)
        {
            context.PreventNavigation();
        }
    }

    /// <summary>
    /// 입고일 변경 시 초안의 표시용 일자와 워크플로를 맞춘다.
    /// </summary>
    /// <param name="value">선택한 입고일</param>
    /// <returns>UI 갱신</returns>
    private async Task OnReceiptDateChangedAsync(DateTime? value)
    {
        // null 이면 당일로 두어 DatePicker 가 비지 않게 한다.
        _receiptDate = value ?? DateTime.Today;
        if (_draft is not null)
        {
            _draft.SalesDate = _receiptDate.Value;
        }

        MarkDirty();
        RefreshWorkflow();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 헤더 기본 입고창고 Id 변경을 반영한다.
    /// </summary>
    /// <param name="value">창고 Id</param>
    /// <returns>UI 갱신</returns>
    private async Task OnHeaderWarehouseChangedAsync(string value)
    {
        _headerWarehouseId = value ?? string.Empty;
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
        var row = new DeliveryLineModel { No = next, RowNo = next };
        // 헤더 창고가 있으면 신규 행에 미리 채워 저장 검증을 줄인다.
        if (!string.IsNullOrWhiteSpace(_headerWarehouseId))
        {
            row.Warehouse = _headerWarehouseId;
        }

        _draft.Lines.Add(row);
        MarkDirty();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 매입명세를 저장한다. PurchaseService 는 Web 프로젝트에 DI 되지 않으므로 HttpClient 로 동일 계약을 호출한다.
    /// </summary>
    /// <returns>HTTP 또는 스텁 처리</returns>
    private async Task SaveAsync()
    {
        if (_draft is null || _receiptDate is null)
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
            _draft.DocumentNumber ??= $"PR-{DateTime.Now:yyyyMMdd}-LOCAL";
            _hasUnsavedChanges = false;
            if (TabService.ActiveTabId is { } tabIdLocal)
            {
                TabService.SetTabDirty(tabIdLocal, false);
            }

            return;
        }

        // 각 라인은 서버에서 WarehouseId 필수이므로 라인 또는 헤더 기본값이 있어야 한다.
        foreach (var line in lines)
        {
            var wh = string.IsNullOrWhiteSpace(line.Warehouse) ? _headerWarehouseId : line.Warehouse;
            if (string.IsNullOrWhiteSpace(wh))
            {
                Snackbar.Add("모든 라인에 입고창고 Id 가 필요합니다. 헤더 입고창고 또는 라인 창고를 입력하세요.", Severity.Warning);
                return;
            }
        }

        // 추후 PurchaseService 연동 필요: Blazor WebAssembly 가 Application 을 참조하지 않으므로 현재는 JSON DTO 로만 직렬화한다.
        var request = new PurchaseReceiptCreateJsonRequest
        {
            PoId = null,
            PartnerId = _draft.PartnerId!,
            ReceiptDate = _receiptDate.Value,
            Memo = _draft.Memo,
            Items = lines.Select(x =>
            {
                var warehouseId = string.IsNullOrWhiteSpace(x.Warehouse) ? _headerWarehouseId : x.Warehouse;
                return new PurchaseReceiptCreateItemJson
                {
                    PoItemId = null,
                    ItemId = x.ItemId,
                    WarehouseId = warehouseId,
                    Qty = x.Qty,
                    UnitPrice = x.UnitPrice,
                    SupplyAmount = x.Amount,
                    VatAmount = x.VatAmount
                };
            }).ToList()
        };

        try
        {
            using var resp = await Http.PostAsJsonAsync("api/purchase/receipts", request);
            // 성공 시 Created 이며 본문에 id 가 온다.
            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadFromJsonAsync<PurchaseReceiptCreateJsonResponse>();
                if (!string.IsNullOrWhiteSpace(body?.Id))
                {
                    _draft.Id = body.Id;
                }

                _draft.DocumentNumber ??= $"PR-{_receiptDate:yyyyMMdd}-API";
                _hasUnsavedChanges = false;
                _status = "Draft";
                if (TabService.ActiveTabId is { } tabId)
                {
                    TabService.SetTabDirty(tabId, false);
                    TabService.UpdateSubTitle(tabId, _draft.SalesCompany);
                }

                Snackbar.Add("매입명세서가 서버에 저장되었습니다.", Severity.Success);
                return;
            }

            // 403 등은 권한 정책(PurchaseOnly) 문제일 수 있다.
            Snackbar.Add($"매입명세 API 응답: {(int)resp.StatusCode}. 로컬 채번으로 표시합니다.", Severity.Warning);
        }
        catch (Exception ex)
        {
            // 네트워크·직렬화 오류 시에도 사용자 입력은 잃지 않도록 스텁만 적용한다.
            Snackbar.Add($"매입명세 API 호출 실패: {ex.Message}", Severity.Warning);
        }

        // 추후 API 연동 필요: 재시도·상세 오류 메시지 표시.
        _draft.DocumentNumber ??= $"PR-{DateTime.Now:yyyyMMdd}-001";
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
    /// 더티 플래그만 해제한다.
    /// </summary>
    /// <returns>완료</returns>
    private Task CancelAsync()
    {
        _hasUnsavedChanges = false;
        if (TabService.ActiveTabId is { } tabId)
        {
            TabService.SetTabDirty(tabId, false);
        }

        Snackbar.Add("변경사항을 취소했습니다.", Severity.Info);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 삭제 확인 후 초기화한다.
    /// </summary>
    /// <returns>다이얼로그 처리</returns>
    private async Task DeleteConfirmAsync()
    {
        var ok = await DialogService.ShowMessageBoxAsync(
            "삭제 확인",
            "현재 매입명세서를 삭제하고 새 문서로 초기화하시겠습니까?",
            yesText: "삭제",
            cancelText: "취소");

        if (ok == true)
        {
            await DeleteAsync();
        }
    }

    /// <summary>
    /// 초안을 신규 상태로 재생성한다.
    /// </summary>
    /// <returns>UI 갱신</returns>
    private async Task DeleteAsync()
    {
        _draft = new DeliveryDraftModel
        {
            Id = Guid.NewGuid().ToString(),
            DocumentType = "매입",
            SalesDate = DateTime.Today,
            ManagerName = "담당자",
            Lines = new List<DeliveryLineModel> { new() { No = 1, IsPlaceholder = true } }
        };
        _receiptDate = DateTime.Today;
        _draft.SalesDate = _receiptDate.Value;
        _headerWarehouseId = "MAIN";
        _selectedLine = null;
        _hasUnsavedChanges = false;
        _status = "Draft";
        RecalculateSummary();
        RefreshWorkflow();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 인쇄(추후 연동).
    /// </summary>
    /// <returns>완료</returns>
    private Task PrintAsync()
    {
        // 추후 API 연동 필요: 브라우저 인쇄 또는 서버 PDF URL.
        Snackbar.Add("인쇄 기능은 다음 단계에서 연동됩니다.", Severity.Info);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 엑셀 다운로드 요청을 문서 서비스에 위임한다.
    /// </summary>
    /// <returns>다운로드 작업</returns>
    private async Task DownloadExcelAsync()
    {
        if (_draft is null || string.IsNullOrWhiteSpace(_draft.Id))
        {
            Snackbar.Add("저장된 매입명세서를 먼저 선택해주세요.", Severity.Warning);
            return;
        }

        // 추후 API 연동 필요: docType 키는 서버 문서 모듈과 맞춰야 한다.
        await DocService.DownloadExcelAsync("purchase-receipt", _draft.Id);
    }

    /// <summary>
    /// PDF 다운로드 요청을 문서 서비스에 위임한다.
    /// </summary>
    /// <returns>다운로드 작업</returns>
    private async Task DownloadPdfAsync()
    {
        if (_draft is null || string.IsNullOrWhiteSpace(_draft.Id))
        {
            Snackbar.Add("저장된 매입명세서를 먼저 선택해주세요.", Severity.Warning);
            return;
        }

        await DocService.DownloadPdfAsync("purchase-receipt", _draft.Id);
    }

    /// <summary>
    /// 매입명세 목록 다이얼로그를 연다.
    /// </summary>
    /// <returns>다이얼로그 표시</returns>
    private async Task OpenListAsync()
    {
        var options = new DialogOptions { MaxWidth = MaxWidth.ExtraLarge, FullWidth = true, CloseButton = true };
        await DialogService.ShowAsync<PurchaseReceiptList>("매입명세서 목록", options);
    }

    /// <summary>
    /// 매입 문맥으로 브레드크럼을 재구성한다.
    /// </summary>
    /// <returns>반환값 없음</returns>
    private void RefreshWorkflow()
    {
        if (_draft is null)
        {
            return;
        }

        _workflowSteps = DeliveryWorkflowFactory.Build("매입", _draft);
    }

    /// <summary>
    /// 라인 합계를 요약 모델에 반영한다.
    /// </summary>
    /// <returns>반환값 없음</returns>
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
    /// <returns>반환값 없음</returns>
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
/// api/purchase/receipts POST 본문(Application 의 CreateReceiptRequest 와 동일 필드)을 Web 프로젝트에서 직렬화하기 위한 DTO.
/// </summary>
public sealed class PurchaseReceiptCreateJsonRequest
{
    // 연동 발주 Id(없으면 직접 입고)
    [JsonPropertyName("poId")]
    public string? PoId { get; set; }

    // 공급처 Id
    [JsonPropertyName("partnerId")]
    public string PartnerId { get; set; } = string.Empty;

    // 입고일
    [JsonPropertyName("receiptDate")]
    public DateTime ReceiptDate { get; set; }

    // 비고
    [JsonPropertyName("memo")]
    public string? Memo { get; set; }

    // 입고 라인 목록
    [JsonPropertyName("items")]
    public List<PurchaseReceiptCreateItemJson> Items { get; set; } = new();
}

/// <summary>
/// 입고 라인 항목 JSON DTO(CreateReceiptItemRequest 대응).
/// </summary>
public sealed class PurchaseReceiptCreateItemJson
{
    // 발주 라인 Id(직접 입고 시 null)
    [JsonPropertyName("poItemId")]
    public string? PoItemId { get; set; }

    // 품목 Id
    [JsonPropertyName("itemId")]
    public string ItemId { get; set; } = string.Empty;

    // 입고 창고 Id
    [JsonPropertyName("warehouseId")]
    public string WarehouseId { get; set; } = string.Empty;

    // 입고 수량
    [JsonPropertyName("qty")]
    public decimal Qty { get; set; }

    // 단가
    [JsonPropertyName("unitPrice")]
    public decimal UnitPrice { get; set; }

    // 공급가액
    [JsonPropertyName("supplyAmount")]
    public decimal SupplyAmount { get; set; }

    // 부가세
    [JsonPropertyName("vatAmount")]
    public decimal VatAmount { get; set; }
}

/// <summary>
/// 매입명세 생성 응답(본문은 { "id": "..." } 형태).
/// </summary>
public sealed class PurchaseReceiptCreateJsonResponse
{
    // 생성된 매입명세 Id
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}
