using System.Security.Claims;
using HitPan.Web.Components.Sales; // ShowAsync<SalesOrderList> 등 코드비하인드에서 컴포넌트 타입을 인식한다(_Imports.razor 는 .cs 에 적용되지 않음).
using HitPan.Web.Models;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor; // Severity, DialogOptions, MaxWidth (스낵바·다이얼로그 API)

// SalesOrderPage.razor 의 @namespace 와 동일해야 한다(Sales.razor 의 클래스 Sales 와 네임스페이스 충돌 방지).
namespace HitPan.Web.Pages.SalesOrderUi;


/// <summary>
/// 수주서 메인 페이지를 제공한다.
/// 거래명세서 페이지 구조를 계승하여 브레드크럼/입력영역/그리드/푸터를 조합한다.
/// </summary>
public partial class SalesOrderPage : ComponentBase
{
    // 거래처 캐시
    private List<PartnerListRow>? _partnerCache;

    // 품목 캐시
    private List<ItemListModel>? _itemCache;

    // 화면에서 편집 중인 수주 초안 데이터
    private DeliveryDraftModel? _draft;

    // 하단 합계 요약 정보
    private DeliverySummaryModel _summary = new();

    // 그리드에서 현재 선택된 라인
    private DeliveryLineModel? _selectedLine;

    // 미저장 변경 여부 (탭 dirty 및 이동 경고에 사용)
    private bool _hasUnsavedChanges;

    // 워크플로 브레드크럼 표시 모델
    private IReadOnlyList<DeliveryWorkflowStepModel> _workflowSteps = Array.Empty<DeliveryWorkflowStepModel>();

    // 화면 상태값 (Draft/Confirmed)
    private string _status = "Draft";

    /// <summary>
    /// 페이지 초기 진입 시 수주 초안을 만든다.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        _itemCache = await ItemsApi.GetListAsync() ?? new();
        _draft = new DeliveryDraftModel
        {
            Id = Guid.NewGuid().ToString(),
            DocumentType = "수주",
            SalesDate = DateTime.Today,
            ManagerName = "담당자",
            Lines = new List<DeliveryLineModel>
            {
                // 첫 라인은 플레이스홀더로 시작하여 UX를 통일한다.
                new() { No = 1, IsPlaceholder = true }
            }
        };

        RefreshWorkflow();
        RecalculateSummary();
    }

    /// <summary>
    /// 내부 라우팅 이동 전 미저장 데이터 확인 다이얼로그를 띄운다.
    /// </summary>
    /// <param name="context">라우팅 이동 컨텍스트</param>
    private async Task OnBeforeInternalNavigationAsync(LocationChangingContext context)
    {
        // 저장되지 않은 내용이 없으면 바로 이동 허용
        if (!_hasUnsavedChanges)
        {
            return;
        }

        var leave = await DialogService.ShowMessageBoxAsync(
            "확인",
            "저장하지 않은 수주 내용이 있습니다. 이동하시겠습니까?",
            yesText: "이동",
            noText: "취소");

        // 사용자가 취소를 선택하면 이동을 막는다.
        if (leave != true)
        {
            context.PreventNavigation();
        }
    }

    /// <summary>
    /// 주문일자가 변경될 때 초안 값을 갱신한다.
    /// </summary>
    /// <param name="value">사용자가 선택한 주문일</param>
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
    /// 거래처 자동완성 검색을 수행한다.
    /// </summary>
    private async Task<IEnumerable<string>> SearchPartnerAsync(string value, CancellationToken ct)
    {
        _partnerCache ??= await PartnersApi.GetListAsync() ?? new();
        if (string.IsNullOrWhiteSpace(value)) return _partnerCache.Select(p => p.PartnerName).Distinct();
        return _partnerCache.Where(p => p.PartnerName.Contains(value, StringComparison.OrdinalIgnoreCase)).Select(p => p.PartnerName).Distinct();
    }

    /// <summary>
    /// 거래처명이 변경될 때 탭 부제목과 초안을 갱신한다.
    /// </summary>
    /// <param name="value">거래처명</param>
    private async Task OnSalesCompanyChangedAsync(string value)
    {
        if (_draft is null)
        {
            return;
        }

        _draft.SalesCompany = value;
        MarkDirty();

        // 활성 탭이 있으면 거래처명으로 부제목을 동기화한다.
        if (TabService.ActiveTabId is { } tabId)
        {
            TabService.UpdateSubTitle(tabId, value);
        }

        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 메모 변경을 반영한다.
    /// </summary>
    /// <param name="value">메모 문자열</param>
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
    /// 그리드 변경 이벤트를 받아 합계와 더티 상태를 갱신한다.
    /// </summary>
    private async Task OnGridChangedAsync()
    {
        RecalculateSummary();
        MarkDirty();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 그리드 선택 라인 변경을 반영한다.
    /// </summary>
    /// <param name="line">선택된 라인</param>
    private Task OnSelectedLineChangedAsync(DeliveryLineModel? line)
    {
        _selectedLine = line;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 툴바 "추가" 동작으로 새 빈 라인을 추가한다.
    /// </summary>
    private async Task AddNewAsync()
    {
        if (_draft is null)
        {
            return;
        }

        // 플레이스홀더는 실제 입력 시작 시 제거한다.
        _draft.Lines.RemoveAll(x => x.IsPlaceholder);
        var next = _draft.Lines.Count + 1;
        _draft.Lines.Add(new DeliveryLineModel { No = next, RowNo = next });
        MarkDirty();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 수주서를 저장 처리한다.
    /// </summary>
    private Task SaveAsync()
    {
        if (_draft is null)
        {
            return Task.CompletedTask;
        }

        // 데모 단계에서는 문서번호만 로컬 채번하여 저장 상태를 표현한다.
        _draft.DocumentNumber ??= $"SO-{DateTime.Now:yyyyMMdd}-001";
        _hasUnsavedChanges = false;
        _status = "Draft";

        if (TabService.ActiveTabId is { } tabId)
        {
            TabService.SetTabDirty(tabId, false);
            TabService.UpdateSubTitle(tabId, _draft.SalesCompany);
        }

        Snackbar.Add("수주서 저장이 완료되었습니다.", Severity.Success);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 편집 내용을 취소하고 더티 플래그를 해제한다.
    /// </summary>
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
    /// 삭제 확인 다이얼로그를 띄운다.
    /// </summary>
    private async Task DeleteConfirmAsync()
    {
        var ok = await DialogService.ShowMessageBoxAsync(
            "삭제 확인",
            "현재 수주서를 삭제하시겠습니까?",
            yesText: "삭제",
            cancelText: "취소");

        if (ok == true)
        {
            await DeleteAsync();
        }
    }

    /// <summary>
    /// 현재 수주 데이터를 초기화하여 신규 문서 상태로 되돌린다.
    /// </summary>
    private async Task DeleteAsync()
    {
        _draft = new DeliveryDraftModel
        {
            Id = Guid.NewGuid().ToString(),
            DocumentType = "수주",
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

    /// <summary>
    /// 인쇄 기능을 호출한다. (현재는 안내 토스트만 표시)
    /// </summary>
    private Task PrintAsync()
    {
        Snackbar.Add("인쇄 기능은 다음 단계에서 연동됩니다.", Severity.Info);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 엑셀 다운로드를 수행한다.
    /// </summary>
    private async Task DownloadExcelAsync()
    {
        if (_draft is null || string.IsNullOrWhiteSpace(_draft.Id))
        {
            Snackbar.Add("저장된 수주서를 먼저 선택해주세요.", Severity.Warning);
            return;
        }

        await DocService.DownloadExcelAsync("sales-order", _draft.Id);
    }

    /// <summary>
    /// PDF 다운로드를 수행한다.
    /// </summary>
    private async Task DownloadPdfAsync()
    {
        if (_draft is null || string.IsNullOrWhiteSpace(_draft.Id))
        {
            Snackbar.Add("저장된 수주서를 먼저 선택해주세요.", Severity.Warning);
            return;
        }

        await DocService.DownloadPdfAsync("sales-order", _draft.Id);
    }

    /// <summary>
    /// 목록 팝업을 연다.
    /// </summary>
    private async Task OpenListAsync()
    {
        var options = new DialogOptions { MaxWidth = MaxWidth.ExtraLarge, FullWidth = true, CloseButton = true };
        await DialogService.ShowAsync<SalesOrderList>("수주서 목록", options);
    }

    /// <summary>
    /// 수주 워크플로 브레드크럼 모델을 재생성한다.
    /// </summary>
    private void RefreshWorkflow()
    {
        if (_draft is null)
        {
            return;
        }

        _workflowSteps = DeliveryWorkflowFactory.Build("수주", _draft);
    }

    /// <summary>
    /// 라인 기준 합계 정보를 재계산한다.
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
    /// 더티 상태를 설정하고 탭 dirty 플래그를 동기화한다.
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
