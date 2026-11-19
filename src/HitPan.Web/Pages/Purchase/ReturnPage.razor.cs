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

    /// <summary>
    /// 열려는 반품서 ID — 매입에서 「반품전환」 직후 그 문서로 데려올 때 쓴다 (20260825작18).
    /// </summary>
    /// <remarks>
    /// 🔴 사장님 결재: <i>"전환 후 반품서 화면으로 이동하는 방향으로 잡어."</i>
    /// 전환 직후 문서는 매입 품목을 전량 복사한 미완성품이라 사람이 수량·사유를 고쳐야 한다.
    /// 목록으로 보내면 담당자가 자기 문서를 다시 찾아야 하므로, 그 문서 앞에 바로 앉힌다.
    /// </remarks>
    [Parameter, SupplyParameterFromQuery(Name = "id")]
    public string? ReturnIdFromQuery { get; set; }

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
        ClearLoadedReturn(); // 🔴 W-G1-51 갈래 E 고침1 — 첫 새 전표 = 붙잡은 전표 없음(G7)
        RefreshWorkflow();
        RecalculateSummary();

        // 쿼리로 반품서 ID 가 오면 그 문서를 연다. 매입반품 경로라 isSalesReturn=false.
        if (!string.IsNullOrWhiteSpace(ReturnIdFromQuery))
        {
            _loadedFromQueryId = ReturnIdFromQuery;
            await LoadReturnAsync(ReturnIdFromQuery, isSalesReturn: false);
        }
    }

    /// <summary>쿼리로 열어둔 반품서 ID — 같은 화면에서 다른 문서로 갈아탈 때 비교 기준이다.</summary>
    private string? _loadedFromQueryId;

    /// <summary>
    /// 🔴 두 번째 전환부터 화면이 안 바뀌던 것을 막는다 (20260825작18 · 검증팀 적발).
    /// </summary>
    /// <remarks>
    /// <c>@page "/returns"</c> 는 단일 라우트이고 <c>WorkTabService</c> 가 반품 탭을 그 URL 하나로
    /// <b>재사용</b>한다. 그래서 반품 화면이 이미 열린 상태에서 매입→전환을 다시 하면
    /// Blazor 가 <b>같은 컴포넌트를 재사용</b>해 <c>OnInitializedAsync</c> 가 돌지 않고
    /// <b>이전 반품서가 그대로 남는다</b> — 사장님이 반려하신 <i>"전환했는데 안 보인다"</i> 가
    /// 두 번째부터 그대로 재발한다.
    /// <para>
    /// ⚠️ <b>끊기는 자리만 옮겨간 것</b>이다. 첫 전환만 보고 "고쳤다" 하면 안 된다.
    /// </para>
    /// </remarks>
    protected override async Task OnParametersSetAsync()
    {
        if (string.IsNullOrWhiteSpace(ReturnIdFromQuery)) return;
        if (string.Equals(ReturnIdFromQuery, _loadedFromQueryId, StringComparison.Ordinal)) return;

        _loadedFromQueryId = ReturnIdFromQuery;
        await LoadReturnAsync(ReturnIdFromQuery, isSalesReturn: false);
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
        ClearLoadedReturn(); // 🔴 W-G1-51 갈래 E 고침1 — 복사본 = 새 id · 저장 전 새 전표(G7)
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
                vatAmount = l.VatAmount
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
                // 🔴 W-G1-51 갈래 E 고침1 — 저장 길에서 붙잡은 값을 끄고(G7), 방금 저장한 전표(서버 id · 저장한 종)로 다시 붙잡는다.
                ClearLoadedReturn();
                if (created is not null)
                {
                    _draft.Id = created.ReturnId;
                    _draft.DocumentNumber = created.ReturnNo;
                    _isNew = false;
                    BindLoadedReturn(created.ReturnId, isSalesReturn);
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
            ClearLoadedReturn(); // 🔴 W-G1-51 갈래 E 고침1 — 새 전표로 초기화(G7)
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
                    // 🔴 20260827작8 W3 — 표시 방식을 5개 화면 공용 헬퍼로 통일.
                    //    (여기가 원래 유일하게 body 를 읽던 자리 = 1-3 만 통과한 이유)
                    Snackbar.Add($"삭제 불가 — {ApiErrorText.Extract(body, (int)resp.StatusCode)}",
                        Severity.Error,
                        cfg => { cfg.RequireInteraction = true; cfg.ShowCloseIcon = true; });
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
        ClearLoadedReturn(); // 🔴 W-G1-51 갈래 E 고침1 — 삭제 뒤 새 전표로 초기화(G7)
        _isNew = true;
        _selectedLine = null;
        _hasUnsavedChanges = false;
        _status = "Draft";
        RecalculateSummary();
        RefreshWorkflow();
        await InvokeAsync(StateHasChanged);
    }

    // ─────────────────────────────────────────────────────────────────
    // 🔴 W-G1-51 갈래 E⑴ (설계 C8 · 대표 11/18 Q3 · PM Q9) — 판매반품 유형은 판매반품 양식 종으로 보낸다.
    //   종전: 미리보기·PDF 가 "return" 고정 → 서버가 purchase_return 으로 바꿔
    //         **판매반품 id 를 매입반품 표에서 찾았다**(못 찾음). 이메일 키도 purchase_receipt 고정.
    //   지금: 반품유형이 「판매반품」 일 때만 아래 두 키로 가지를 나눈다.
    //         매입반품은 종전 줄 그대로다(#1 덮어쓰기 금지 — 더하기·가지 나누기만).
    //   키 근거(#13 정적 대조): 미리보기 = DocumentController.PrintTypeToDocumentType "sales-return" → "sales_return"
    //         · 이메일 = PdfRenderService.LoadDocumentAsync "sales_return" · 양식 종 = FormTemplateService
    //         DefaultFormTypes "sales_return" · 출하 DDL form_templates.form_type 주석 "sales_return".
    // ─────────────────────────────────────────────────────────────────

    /// <summary>판매반품 화면 키(하이픈 꼴) — PDF 문이 받는 이름. 미리보기는 같은 글자를 직접 적는다(게이트 축⑥).</summary>
    private const string SalesReturnPrintType = "sales-return";

    /// <summary>판매반품 렌더 키(밑줄 꼴) — 이메일은 이 키를 렌더러에 그대로 넘긴다.</summary>
    private const string SalesReturnEmailTypeKey = "sales_return";

    // ─────────────────────────────────────────────────────────────────
    // 🔴 W-G1-51 갈래 E 고침1 ([3-V] P2-E1 · [4] P3 E-3 · PM 판단 11/19) — 인쇄·PDF·이메일 종은 「불러온 전표」 에 묶는다.
    //   종전(고침 전): 인쇄·PDF·이메일이 선택칸 `_returnType` 으로 종을 갈랐다.
    //         선택칸은 전표를 불러온 뒤에도 바뀐다 → 매입반품을 불러온 뒤 「판매반품」 으로 바꾸고 [이메일]
    //         = 판매반품 키 + 매입반품 id → 「원본 문서를 찾을 수 없습니다」 종이가 거래처로 나갔다.
    //   지금: 서버에서 읽어 온(또는 그 종으로 저장한) 전표의 **id 와 종을 따로 붙잡는다**.
    //         `/deliveries` 의 `_openedSalesReturnIdForPrint`(G7)와 같은 원칙 — 판정은 「붙잡은 id == 지금 _draft.Id」 일 때만.
    //         선택칸은 잠그지 않는다 — [목록] 이 선택칸으로 매입/판매 목록을 고르므로, 잠그면 불러온 뒤
    //         다른 종 목록으로 못 넘어간다(새 전표 단추가 없는 화면). 선택칸의 뜻 = 「목록 종류 · 새 전표의 종」.
    //   켬: LoadReturnAsync 읽기 성공 · SaveAsync 새로 저장 성공(그 종 · 서버 id).
    //   끔: 첫 화면 · 복사(새 id) · 취소(새 전표 초기화) · 삭제 뒤 초기화 · 새로 저장 길(끈 뒤 저장한 전표로 다시 켬).
    //   id 로 묶었으므로 끄는 자리를 하나 빠뜨려도 _draft.Id 가 바뀌면 판정이 저절로 거짓이 된다.
    //   고침1-a(PM 불수용): 저장·삭제·확정·확정취소는 이 판정을 쓰지 않는다 — 원장·문서 상태 길이라
    //         이 갈래 근거 밖. 선택칸 기준 그대로(종전 · main 과 같음) · 불일치 위험은 PM 별건.
    // ─────────────────────────────────────────────────────────────────

    /// <summary>불러온(또는 방금 새로 저장한) 반품 전표의 서버 id. 비면 「붙잡은 전표 없음」.</summary>
    private string? _loadedReturnId;

    /// <summary>위 전표가 판매반품인가 — 읽어 온 목록 종(또는 저장한 경로)에서 정한다. 선택칸을 바꿔도 안 바뀐다.</summary>
    private bool _loadedReturnIsSales;

    /// <summary>붙잡은 전표가 지금 화면의 전표인가(서수 비교).</summary>
    private bool IsLoadedReturnOnScreen =>
        !string.IsNullOrWhiteSpace(_loadedReturnId)
        && string.Equals(_loadedReturnId, _draft?.Id, StringComparison.Ordinal);

    /// <summary>
    /// [인쇄] · [PDF] · [이메일] 판정 — 화면의 전표가 **불러온 판매반품**일 때만 참.
    /// 그 밖(매입반품 · 저장 안 한 새 전표)은 종전 매입반품 키 그대로(#1 · `/deliveries` G7 과 같은 꼴).
    /// </summary>
    private bool IsLoadedSalesReturnForPrint => IsLoadedReturnOnScreen && _loadedReturnIsSales;

    private void BindLoadedReturn(string returnId, bool isSales)
    {
        _loadedReturnId = returnId;
        _loadedReturnIsSales = isSales;
    }

    private void ClearLoadedReturn()
    {
        _loadedReturnId = null;
        _loadedReturnIsSales = false;
    }

    private async Task PrintAsync()
    {
        if (IsLoadedSalesReturnForPrint)
        {
            // ⚠️ 글자 그대로 둔다(상수 아님) — 인쇄 게이트 축⑥ 이 `PrintPreview.ShowAsync("…")` 글자를 긁어
            //   서버 문·로더가 그 종을 아는지 본다(DocumentPrintPreviewGateTests.WiredDocTypes).
            // 🔴 고침1 E-2 — 창 제목 = 양식 종 이름 「판매반품」(FormTemplateService · `/deliveries` 와 같게).
            //   매입반품 쪽 「반품서」 는 아래 종전 줄 그대로(#1).
            await PrintPreview.ShowAsync("sales-return", _draft?.Id, "판매반품");
            return;
        }

        await PrintPreview.ShowAsync("return", _draft?.Id, "반품서");
    }

    /// <summary>
    /// 이메일 발송 다이얼로그를 연다.
    /// </summary>
    private async Task EmailAsync()
    {
        var parameters = new DialogParameters
        {
            ["DocumentType"] = "반품처리서",
            // 🔴 W-G1-51 갈래 E⑴ — 판매반품만 판매반품 키 · 매입반품은 종전 "purchase_receipt" 그대로(#1).
            //   고침1 — 판정은 불러온 전표의 종 · id(선택칸 아님 · P2-E1).
            ["DocumentTypeKey"] = IsLoadedSalesReturnForPrint ? SalesReturnEmailTypeKey : "purchase_receipt",
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
        // 🔴 W-G1-51 갈래 E⑴ — 판매반품만 판매반품 키. ⚠️ 이 단추는 옛 /pdf 길(DocumentController
        //   LoadDocumentAsync)을 탄다 — 거기엔 아직 "sales-return" 적재가 없어 빈 문서가 나온다(설계 C8 ⑷).
        //   그 파일은 착수준비표 P-2 로 Z5 뒤다. 키만 먼저 맞춰 둔다.
        //   고침1 — 판정은 불러온 전표의 종 · id(선택칸 아님 · P2-E1).
        if (IsLoadedSalesReturnForPrint)
        {
            await DocService.DownloadPdfAsync(SalesReturnPrintType, _draft.Id);
            return;
        }

        await DocService.DownloadPdfAsync("return", _draft.Id);
    }

    /// <summary>진행 중 재진입 잠금 — 더블클릭으로 품목이 두 번 들어오는 것을 막는다.</summary>
    private bool _isLoadingReceipt;

    /// <summary>
    /// 「매입불러오기」 — 매입명세서 한 건의 품목을 반품 줄로 옮긴다 (20260825작18).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>사장님 결재</b>: <i>"매입불러오기 화면을 따로 만들게 아니라 그 버튼 클릭시,
    /// 매입처리의 매입목록으로 바로 화면을 보내면 되잖아."</i>
    /// <para>
    /// 그래서 새 다이얼로그를 만들지 않고 기존 <c>PurchaseReceiptList</c> 를 그대로 연다.
    /// 그 컴포넌트는 이미 행 클릭 시 <c>MudDialog.Close(Ok(ReceiptId))</c> 로 선택을 돌려준다.
    /// </para>
    /// </remarks>
    private async Task LoadFromReceiptAsync()
    {
        if (_isLoadingReceipt || _draft is null) return;

        var options = new DialogOptions { MaxWidth = MaxWidth.ExtraLarge, FullWidth = true, CloseButton = true };
        var dlg = await DialogService.ShowAsync<PurchaseReceiptList>("매입 목록에서 불러오기", options);
        var result = await dlg.Result;
        if (result is null || result.Canceled) return;
        if (result.Data is not string receiptId || string.IsNullOrWhiteSpace(receiptId)) return;

        _isLoadingReceipt = true;
        try
        {
            var detail = await DeliveryService.GetPurchaseReceiptDetailAsync(receiptId);
            if (detail is null)
            {
                Snackbar.Add("매입명세서를 불러오지 못했습니다.", Severity.Error);
                return;
            }

            // 헌법 #6 — 받은 적 없는 물건은 반품 대상이 아니다.
            //   판매쪽과 같은 가드다(SalesReturnPage "판매확정 전 거래는 반품할 수 없습니다").
            //   서버도 전환 경로에서 같은 검사를 하지만, 화면에서 먼저 막아야 헛걸음이 없다.
            if (!string.Equals(detail.Status, "confirmed", StringComparison.OrdinalIgnoreCase))
            {
                Snackbar.Add("매입확정 전 명세서는 반품할 수 없습니다. 먼저 매입확정 해주세요.", Severity.Warning);
                return;
            }

            if (detail.Items.Count == 0)
            {
                Snackbar.Add($"[{detail.ReceiptNo}] 에 품목이 없습니다.", Severity.Warning);
                return;
            }

            // 헌법 #1 — 이미 입력한 줄을 말없이 덮어쓰지 않는다.
            var existing = _draft.Lines.Count(l => !l.IsPlaceholder && !string.IsNullOrWhiteSpace(l.ItemId));
            if (existing > 0)
            {
                var ok = await DialogService.ShowMessageBoxAsync(
                    "품목 교체",
                    $"이미 입력된 품목 {existing}건이 있습니다.\n\n"
                    + $"[{detail.ReceiptNo}] 의 품목으로 바꾸시겠습니까?",
                    yesText: "바꾸기", cancelText: "취소");
                if (ok != true) return;
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
                    // 반품은 산 값으로 돌려준다 — 여기서 단가를 다시 계산하면 반품액이 어긋난다.
                    UnitPrice = it.UnitPrice,
                    Warehouse = it.WarehouseId ?? string.Empty,
                    IsPlaceholder = false
                });
                no++;
            }
            lines.Add(new DeliveryLineModel { No = no, RowNo = no, IsPlaceholder = true });

            _draft.Lines = lines;
            _draft.PartnerId = detail.PartnerId;
            _draft.SalesCompany = detail.PartnerName;

            // 🔴 작8 사고 이식 — 저장부가 거래처를 _partnerCache 에서 "이름으로" 찾는다.
            //   그 캐시는 사람이 자동완성에 타이핑해야 처음 적재되므로, 불러오기로 들어오면
            //   거래처가 화면에 멀쩡히 보이는데도 저장이 "거래처를 선택해주세요" 로 막힌다.
            //   판매쪽에서 이미 겪은 사고다 — 매입에서 되풀이하지 않는다.
            _partnerCache ??= await PartnersApi.GetListAsync() ?? new();

            _selectedLine = null;
            MarkDirty();
            RecalculateSummary();
            RefreshWorkflow();

            Snackbar.Add($"[{detail.ReceiptNo}] 품목 {detail.Items.Count}건을 불러왔습니다. 반품할 수량으로 고쳐주세요.", Severity.Success);
        }
        finally
        {
            _isLoadingReceipt = false;
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

    /// <summary>서버에서 반품 단건을 읽어 편집 화면에 주입한다. 14차 P0 봉합(B안): 매출/매입 분기.</summary>
    private async Task LoadReturnAsync(string returnId, bool isSalesReturn = false)
    {
        var detail = isSalesReturn
            ? await DeliveryService.GetSalesReturnDetailAsync(returnId)
            : await DeliveryService.GetPurchaseReturnDetailAsync(returnId);
        if (detail is null)
        {
            Snackbar.Add("반품 문서를 불러오지 못했습니다.", Severity.Error);
            return;
        }
        // 로드한 문서 종류에 맞춰 반품유형을 설정(저장·확정·삭제가 올바른 경로로 가도록).
        _returnType = isSalesReturn ? "sales_return" : "purchase_return";

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
        // 🔴 W-G1-51 갈래 E 고침1 — 켬(읽기 성공 · _draft 를 그 전표로 바꿔 넣은 뒤에만).
        //   종 = 읽어 온 목록 종(isSalesReturn) · id = 서버가 돌려준 ReturnId. 실패하면 위에서 빠져 종전 값 유지.
        BindLoadedReturn(detail.ReturnId, isSalesReturn);
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
