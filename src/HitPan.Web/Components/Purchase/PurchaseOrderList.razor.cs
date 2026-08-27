using HitPan.Web.Models;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HitPan.Web.Components.Purchase;

/// <summary>
/// 발주서 목록 필터·선택·합계 UI.
/// 수주 목록(SalesOrderList) 패턴을 계승한다.
/// </summary>
public partial class PurchaseOrderList : ComponentBase
{
    // 조회 시작일
    private DateTime? _startDate = DateTime.Today.AddDays(-7);

    // 조회 종료일
    private DateTime? _endDate = DateTime.Today;

    // 거래처 필터
    private PartnerSearchResult? _partner;

    // 상태 필터 (기본값: 전체)
    private string _status = "";

    // 목록 행
    private List<PurchaseOrderListItem> _rows = new();

    // 선택된 행
    private List<PurchaseOrderListItem> _selectedRows = new();

    // 전체 선택 체크
    private bool _allSelected;

    // 선택 합계: 공급가
    private decimal _selectedSupply;

    // 선택 합계: 부가세
    private decimal _selectedVat;

    // 선택 합계: 총액
    private decimal _selectedTotal;

    /// <summary>
    /// 거래처 검색은 기존 배송/수주와 동일하게 DeliveryService 를 사용한다(공급처도 동일 partner API).
    /// </summary>
    [Inject]
    private DeliveryService DeliveryService { get; set; } = default!;

    /// <summary>
    /// 일괄 확정 안내용 스낵바.
    /// </summary>
    [Inject]
    private ISnackbar Snackbar { get; set; } = default!;

    [Inject]
    private IDialogService DialogService { get; set; } = default!;

    /// <summary>
    /// 다이얼로그 호스트. 행 클릭 시 선택 ID를 결과로 Close한다.
    /// 페이지에 직접 임베드될 때는 null이므로 OnOrderSelected 병행.
    /// </summary>
    [CascadingParameter]
    private IMudDialogInstance? MudDialog { get; set; }

    /// <summary>
    /// 행 클릭 시 상위로 발주 Id 를 전달한다(임베드용, 보조 경로).
    /// </summary>
    [Parameter]
    public EventCallback<string> OnOrderSelected { get; set; }

    /// <summary>
    /// 현재 강조할 발주 Id.
    /// </summary>
    [Parameter]
    public string? SelectedOrderId { get; set; }

    /// <summary>
    /// 초기 로드 시 조회를 호출한다.
    /// </summary>
    /// <returns>비동기 초기화</returns>
    protected override async Task OnInitializedAsync()
    {
        await LoadAsync(CancellationToken.None);
    }

    /// <summary>
    /// 시작일 변경.
    /// </summary>
    /// <param name="value">일자</param>
    /// <returns>재조회</returns>
    private async Task OnStartDateChangedAsync(DateTime? value)
    {
        _startDate = value;
        await LoadAsync(CancellationToken.None);
    }

    /// <summary>
    /// 종료일 변경.
    /// </summary>
    /// <param name="value">일자</param>
    /// <returns>재조회</returns>
    private async Task OnEndDateChangedAsync(DateTime? value)
    {
        _endDate = value;
        await LoadAsync(CancellationToken.None);
    }

    /// <summary>
    /// 거래처 필터 변경.
    /// </summary>
    /// <param name="value">선택 거래처</param>
    /// <returns>재조회</returns>
    private async Task OnPartnerChangedAsync(PartnerSearchResult? value)
    {
        _partner = value;
        await LoadAsync(CancellationToken.None);
    }

    /// <summary>
    /// 상태 필터 변경.
    /// </summary>
    /// <param name="value">상태 코드</param>
    /// <returns>재조회</returns>
    private async Task OnStatusChangedAsync(string value)
    {
        _status = value;
        await LoadAsync(CancellationToken.None);
    }

    /// <summary>
    /// 거래처 자동완성 검색.
    /// </summary>
    /// <param name="keyword">검색어</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>후보 목록</returns>
    private async Task<IEnumerable<PartnerSearchResult>> SearchPartnersAsync(string keyword, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return Array.Empty<PartnerSearchResult>();
        }

        return await DeliveryService.SearchPartnersAsync(keyword, ct);
    }

    /// <summary>
    /// 발주 목록을 조회한다.
    /// </summary>
    /// <param name="ct">취소 토큰</param>
    /// <returns>비동기 조회</returns>
    private async Task LoadAsync(CancellationToken ct = default)
    {
        _rows = await DeliveryService.GetPurchaseOrderListAsync(
            from: _startDate,
            to: _endDate,
            status: _status,
            ct: ct);

        foreach (var row in _rows)
        {
            // 조회 직후 체크 상태를 초기화한다.
            row.IsChecked = false;
        }

        _selectedRows.Clear();
        _allSelected = false;
        RecalculateSelectionSummary();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 전체 선택 토글.
    /// </summary>
    /// <param name="value">체크 여부</param>
    /// <returns>완료</returns>
    private async Task ToggleAllAsync(bool value)
    {
        var selectable = _rows.Where(IsSelectable).ToList();
        _allSelected = value && selectable.Count > 0;
        foreach (var row in selectable)
            row.IsChecked = value;

        _selectedRows = _rows.Where(x => x.IsChecked).ToList();
        RecalculateSelectionSummary();
        // 외부 툴바 버튼 Disabled 즉시 갱신.
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 단일 행 선택 토글.
    /// </summary>
    /// <param name="row">행</param>
    /// <param name="value">체크 여부</param>
    /// <returns>완료</returns>
    private async Task ToggleOneAsync(PurchaseOrderListItem row, bool value)
    {
        if (!IsSelectable(row)) return;
        row.IsChecked = value;
        _selectedRows = _rows.Where(x => x.IsChecked).ToList();
        _allSelected = _rows.Count > 0 && _rows.Where(IsSelectable).All(x => x.IsChecked);
        RecalculateSelectionSummary();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>입고완료·취소 발주서는 선택 대상에서 제외.</summary>
    private static bool IsSelectable(PurchaseOrderListItem row) =>
        !string.Equals(row.Status, "received", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(row.Status, "cancelled", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 선택 행 일괄 확정.
    /// </summary>
    /// <returns>비동기 처리</returns>
    private async Task BulkConfirmAsync()
    {
        // 발주는 별도 "확정" 상태가 없다. 워크플로우 §20 기준으로 발주→매입전환이 완결 경로.
        // draft 상태에서 바로 "매입전환" 버튼을 쓰도록 안내.
        Snackbar.Add("발주는 '매입전환' 버튼으로 완결합니다(별도 확정 단계 없음). 오른쪽 매입전환 버튼을 사용하세요.", Severity.Info);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 선택된 발주서를 일괄 매입(입고)으로 전환한다.
    /// </summary>
    private async Task BulkConvertToReceiptAsync()
    {
        var ids = _selectedRows
            .Where(x => !string.IsNullOrWhiteSpace(x.PoId))
            .Select(x => x.PoId)
            .ToList();

        if (ids.Count == 0)
        {
            return;
        }

        var successCount = 0;
        var failures = new List<string>();

        foreach (var poId in ids)
        {
            var (result, err) = await DeliveryService.ConvertOrderToReceiptWithErrorAsync(poId);
            if (result is not null)
            {
                successCount++;
            }
            else
            {
                failures.Add($"{poId}: {err ?? "알 수 없는 오류"}");
            }
        }

        if (successCount > 0)
        {
            Snackbar.Add($"{successCount}건 매입전환 완료", Severity.Success);
        }

        if (failures.Count > 0)
        {
            Snackbar.Add($"{failures.Count}건 매입전환 실패 — {failures[0]}", Severity.Error);
        }

        // 목록 새로고침
        await LoadAsync(CancellationToken.None);
    }

    /// <summary>
    /// 선택 합계를 계산한다.
    /// </summary>
    private void RecalculateSelectionSummary()
    {
        _selectedTotal = _selectedRows.Sum(x => x.TotalAmount);
        _selectedVat = _selectedRows.Sum(x => x.VatAmount);

        // 부가세가 모두 0이면 합계에서 10% 역산한다.
        if (_selectedTotal > 0m && _selectedVat == 0m)
        {
            _selectedVat = Math.Round(_selectedTotal / 11m, 0);
        }

        _selectedSupply = _selectedTotal - _selectedVat;
    }

    /// <summary>
    /// 행 클릭 시 상위에 발주 Id 를 알린다.
    /// </summary>
    /// <param name="row">행</param>
    /// <returns>콜백 호출</returns>
    private async Task SelectRowAsync(PurchaseOrderListItem row)
    {
        if (string.IsNullOrWhiteSpace(row.PoId))
        {
            return;
        }

        if (MudDialog is not null)
        {
            MudDialog.Close(DialogResult.Ok(row.PoId));
            return;
        }

        await OnOrderSelected.InvokeAsync(row.PoId);
    }

    /// <summary>단건 삭제 — draft만.</summary>
    private async Task DeleteOneAsync(PurchaseOrderListItem row)
    {
        if (string.IsNullOrWhiteSpace(row.PoId)) return;

        var confirm = await DialogService.ShowMessageBoxAsync(
            "발주서 삭제",
            $"[{row.PoNo}] 을(를) 삭제하시겠습니까?\n(매입전환된 라인이 있으면 삭제할 수 없습니다.)",
            yesText: "삭제", cancelText: "취소");
        if (confirm != true) return;

        var (ok, error) = await DeliveryService.DeletePurchaseOrderAsync(row.PoId);
        if (ok)
        {
            Snackbar.Add($"[{row.PoNo}] 삭제되었습니다.", Severity.Success);
            await LoadAsync();
        }
        else
        {
            // 🔴 20260827작8 W2 — 서버 문장 그대로(연결된 매입전표 번호가 실려 온다).
            Snackbar.Add($"삭제 불가 — {ApiErrorText.Extract(error)}", Severity.Error,
                cfg => { cfg.RequireInteraction = true; cfg.ShowCloseIcon = true; });
        }
    }

    /// <summary>선택 행 일괄 삭제.</summary>
    /// <remarks>
    /// 🔴 <b>20260827작8 W1 — <c>draft</c> 사전필터를 걷어냈다.</b>
    /// 종전엔 화면이 먼저 걸러 <b>입고된 발주는 DELETE 요청조차 안 나갔고</b>,
    /// 서버 삭제가드(연결된 매입전표 번호를 알려주는)가 실행되지 못했다
    /// (1.3.28 실측 반려 — <i>"삭제에 실패했습니다"</i> 만 떴다).
    /// <b>판정은 서버가 한다.</b>
    /// </remarks>
    private async Task BulkDeleteAsync()
    {
        var targets = _selectedRows
            .Where(x => !string.IsNullOrWhiteSpace(x.PoId))
            .ToList();

        if (targets.Count == 0)
        {
            Snackbar.Add("삭제할 발주서를 선택해 주세요.", Severity.Warning);
            return;
        }

        var confirm = await DialogService.ShowMessageBoxAsync(
            "발주서 일괄 삭제",
            $"선택한 {targets.Count}건을 삭제하시겠습니까? 매입·원장에 연결된 건은 삭제되지 않습니다.",
            yesText: "삭제", cancelText: "취소");
        if (confirm != true) return;

        var success = 0;
        var failed = new List<(string No, string Reason)>();
        foreach (var row in targets)
        {
            var (ok, error) = await DeliveryService.DeletePurchaseOrderAsync(row.PoId);
            if (ok) success++;
            else failed.Add((row.PoNo, ApiErrorText.Extract(error)));
        }

        if (failed.Count == 0)
        {
            Snackbar.Add($"{success}건 삭제 완료.", Severity.Success);
        }
        else
        {
            var head = success > 0 ? $"{success}건 삭제 · " : string.Empty;
            var lines = string.Join(" / ", failed.Select(f => $"[{f.No}] {f.Reason}"));
            Snackbar.Add($"{head}{failed.Count}건 삭제 불가 — {lines}", Severity.Warning,
                cfg => { cfg.RequireInteraction = true; cfg.ShowCloseIcon = true; });
        }

        await LoadAsync();
    }

    /// <summary>
    /// 선택된 행 강조 스타일.
    /// </summary>
    /// <param name="row">행</param>
    /// <returns>CSS 클래스</returns>
    private string GetSelectedClass(PurchaseOrderListItem row)
    {
        return string.Equals(row.PoId, SelectedOrderId, StringComparison.OrdinalIgnoreCase)
            ? "font-weight-bold text-primary"
            : string.Empty;
    }
}
