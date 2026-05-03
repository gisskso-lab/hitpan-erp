using System.Net.Http.Json;
using HitPan.Web.Models;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HitPan.Web.Components.Purchase;

/// <summary>
/// 매입명세서 목록 필터·선택·합계 UI.
/// 발주 목록(PurchaseOrderList) 패턴을 계승한다.
/// </summary>
public partial class PurchaseReceiptList : ComponentBase
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
    private List<PurchaseReceiptListItem> _rows = new();

    // 선택된 행
    private List<PurchaseReceiptListItem> _selectedRows = new();

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

    [Inject]
    private HttpClient Http { get; set; } = default!;

    /// <summary>
    /// 다이얼로그 호스트 — 행 클릭 시 선택 ID를 결과로 Close한다.
    /// 페이지에 직접 임베드될 때는 null이므로 OnOrderSelected 콜백도 병행한다.
    /// </summary>
    [CascadingParameter]
    private IMudDialogInstance? MudDialog { get; set; }

    /// <summary>
    /// 행 클릭 시 상위로 매입명세 Id 를 전달한다(임베드용, 보조 경로).
    /// </summary>
    [Parameter]
    public EventCallback<string> OnOrderSelected { get; set; }

    /// <summary>
    /// 현재 강조할 매입명세 Id.
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
    /// 매입명세 목록을 조회한다.
    /// </summary>
    /// <param name="ct">취소 토큰</param>
    /// <returns>비동기 조회</returns>
    private async Task LoadAsync(CancellationToken ct = default)
    {
        _rows = await DeliveryService.GetPurchaseReceiptListAsync(
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
        _allSelected = value;
        // 확정된 매입(confirmed)은 일괄확정·반품전환·삭제 대상이 아니므로 전체선택에서 제외.
        foreach (var row in _rows)
        {
            row.IsChecked = value && !IsConfirmed(row);
        }

        _selectedRows = _rows.Where(x => x.IsChecked).ToList();
        RecalculateSelectionSummary();
        // 외부 툴바 버튼 Disabled 즉시 갱신.
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>확정된 매입명세 행 — 체크박스/삭제 비활성화 대상.</summary>
    private static bool IsConfirmed(PurchaseReceiptListItem row)
        => string.Equals(row.Status, "confirmed", StringComparison.OrdinalIgnoreCase);

    /// <summary>확정 행은 흐리게 표시. (MudTable RowStyleFunc 시그니처: (T, int) => string)</summary>
    private static string RowStyleFunc(PurchaseReceiptListItem row, int rowIndex)
        => IsConfirmed(row) ? "opacity:0.55;background:#f7f7f7;" : string.Empty;

    /// <summary>
    /// 단일 행 선택 토글.
    /// </summary>
    /// <param name="row">행</param>
    /// <param name="value">체크 여부</param>
    /// <returns>완료</returns>
    private async Task ToggleOneAsync(PurchaseReceiptListItem row, bool value)
    {
        // 확정 행은 사장님 지시(2026-04-26)대로 조회 전용 — 선택 자체 무시.
        if (IsConfirmed(row)) { row.IsChecked = false; await InvokeAsync(StateHasChanged); return; }
        row.IsChecked = value;
        _selectedRows = _rows.Where(x => x.IsChecked).ToList();
        _allSelected = _rows.Count > 0 && _rows.All(x => x.IsChecked);
        RecalculateSelectionSummary();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 선택 행 일괄 확정.
    /// </summary>
    /// <returns>비동기 처리</returns>
    private async Task BulkConfirmAsync()
    {
        var draftIds = _selectedRows
            .Where(x => !string.IsNullOrWhiteSpace(x.ReceiptId)
                        && string.Equals(x.Status, "draft", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.ReceiptId)
            .ToList();

        if (draftIds.Count == 0)
        {
            Snackbar.Add("확정 가능한 draft 상태 매입이 없습니다.", Severity.Warning);
            return;
        }

        // 단건 confirm 엔드포인트를 순차 호출. 성공/실패 분리 집계 — 헌법 §20에 따라
        // 실패를 성공 Snackbar로 위장하지 않는다(거래명세서 bulk-confirm과 동일 정책).
        var success = new List<string>();
        var failed = new List<(string Id, string Reason)>();

        foreach (var id in draftIds)
        {
            try
            {
                using var resp = await Http.PostAsJsonAsync(
                    $"api/purchase/receipts/{Uri.EscapeDataString(id)}/confirm",
                    new { },
                    CancellationToken.None);
                if (resp.IsSuccessStatusCode)
                {
                    success.Add(id);
                }
                else
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    failed.Add((id, string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)resp.StatusCode}" : body));
                }
            }
            catch (Exception ex)
            {
                failed.Add((id, ex.Message));
            }
        }

        // 성공 행 상태 갱신 — 화면 즉시 반영
        foreach (var row in _rows.Where(r => success.Contains(r.ReceiptId)))
        {
            row.Status = "confirmed";
        }
        _selectedRows.Clear();
        _allSelected = false;
        RecalculateSelectionSummary();

        if (failed.Count == 0)
        {
            Snackbar.Add($"{success.Count}건 매입 확정 완료 (재고 반영됨)", Severity.Success);
        }
        else if (success.Count == 0)
        {
            Snackbar.Add($"전건 확정 실패 ({failed.Count}건): {failed[0].Reason[..Math.Min(200, failed[0].Reason.Length)]}", Severity.Error);
        }
        else
        {
            Snackbar.Add($"성공 {success.Count}건 / 실패 {failed.Count}건. 첫 실패: {failed[0].Reason[..Math.Min(200, failed[0].Reason.Length)]}", Severity.Warning);
        }

        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 선택된 매입명세서를 반품으로 전환한다 (반품 API 추가 전 스텁).
    /// </summary>
    private async Task BulkConvertToReturnAsync()
    {
        var ids = _selectedRows
            .Where(x => !string.IsNullOrWhiteSpace(x.ReceiptId))
            .Select(x => x.ReceiptId)
            .ToList();

        if (ids.Count == 0) return;

        var confirm = await DialogService.ShowMessageBoxAsync(
            "반품 전환",
            $"선택한 {ids.Count}건을 반품으로 전환하시겠습니까?",
            yesText: "전환", cancelText: "취소");
        if (confirm != true) return;

        var success = 0;
        foreach (var id in ids)
        {
            var ok = await DeliveryService.ConvertReceiptToReturnAsync(id);
            if (ok) success++;
        }

        Snackbar.Add($"{success}건 반품 전환 완료", Severity.Success);
        await LoadAsync();
    }

    /// <summary>
    /// 선택 합계를 계산한다.
    /// </summary>
    /// <returns>반환값 없음</returns>
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
    /// 행 클릭 시 상위에 매입명세 Id 를 알린다.
    /// </summary>
    /// <param name="row">행</param>
    /// <returns>콜백 호출</returns>
    private async Task SelectRowAsync(PurchaseReceiptListItem row)
    {
        if (string.IsNullOrWhiteSpace(row.ReceiptId))
        {
            return;
        }

        // 다이얼로그로 열려 있으면 선택 ID를 결과로 닫아 호출자가 편집 화면에 로드.
        if (MudDialog is not null)
        {
            MudDialog.Close(DialogResult.Ok(row.ReceiptId));
            return;
        }

        await OnOrderSelected.InvokeAsync(row.ReceiptId);
    }

    /// <summary>
    /// 단건 삭제 — draft만 허용. confirmed는 UI에서 아이콘이 Disabled.
    /// </summary>
    private async Task DeleteOneAsync(PurchaseReceiptListItem row)
    {
        if (string.IsNullOrWhiteSpace(row.ReceiptId)) return;

        var confirm = await DialogService.ShowMessageBoxAsync(
            "매입명세서 삭제",
            $"[{row.ReceiptNo}] 을(를) 삭제하시겠습니까?\n(확정된 전표는 삭제할 수 없습니다.)",
            yesText: "삭제", cancelText: "취소");
        if (confirm != true) return;

        var (ok, error) = await DeleteReceiptAsync(row.ReceiptId);
        if (ok)
        {
            Snackbar.Add($"[{row.ReceiptNo}] 삭제되었습니다.", Severity.Success);
            await LoadAsync();
        }
        else
        {
            Snackbar.Add($"삭제 실패: {error}", Severity.Error);
        }
    }

    /// <summary>
    /// 선택 행 일괄 삭제 — draft 건만 대상, confirmed는 스킵.
    /// </summary>
    private async Task BulkDeleteAsync()
    {
        var draftTargets = _selectedRows
            .Where(x => !string.IsNullOrWhiteSpace(x.ReceiptId)
                        && string.Equals(x.Status, "draft", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (draftTargets.Count == 0)
        {
            Snackbar.Add("삭제 가능한 draft 상태 매입명세가 없습니다.", Severity.Warning);
            return;
        }

        var confirm = await DialogService.ShowMessageBoxAsync(
            "매입명세서 일괄 삭제",
            $"선택한 draft 상태 {draftTargets.Count}건을 삭제하시겠습니까?",
            yesText: "삭제", cancelText: "취소");
        if (confirm != true) return;

        var success = 0;
        var failed = new List<(string No, string Reason)>();
        foreach (var row in draftTargets)
        {
            var (ok, error) = await DeleteReceiptAsync(row.ReceiptId);
            if (ok) success++;
            else failed.Add((row.ReceiptNo, error ?? "unknown"));
        }

        if (failed.Count == 0)
        {
            Snackbar.Add($"{success}건 삭제 완료.", Severity.Success);
        }
        else
        {
            Snackbar.Add($"성공 {success}건 / 실패 {failed.Count}건. 첫 실패: {failed[0].No} — {failed[0].Reason[..Math.Min(150, failed[0].Reason.Length)]}", Severity.Warning);
        }

        await LoadAsync();
    }

    /// <summary>
    /// 서버 DELETE 호출. 성공 시 (true, null), 실패 시 (false, 서버응답).
    /// </summary>
    private async Task<(bool Success, string? Error)> DeleteReceiptAsync(string receiptId)
    {
        try
        {
            using var resp = await Http.DeleteAsync($"api/purchase/receipts/{Uri.EscapeDataString(receiptId)}");
            if (resp.IsSuccessStatusCode) return (true, null);
            var body = await resp.Content.ReadAsStringAsync();
            return (false, string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)resp.StatusCode}" : body);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// 선택된 행 강조 스타일.
    /// </summary>
    /// <param name="row">행</param>
    /// <returns>CSS 클래스</returns>
    private string GetSelectedClass(PurchaseReceiptListItem row)
    {
        return string.Equals(row.ReceiptId, SelectedOrderId, StringComparison.OrdinalIgnoreCase)
            ? "font-weight-bold text-primary"
            : string.Empty;
    }
}
