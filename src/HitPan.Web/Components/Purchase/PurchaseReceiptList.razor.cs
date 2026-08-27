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

    /// <summary>
    /// 🔴 20260827작1 §8-B — 「반품포함」. 기본 <c>false</c> = 종전 동작 그대로.
    /// </summary>
    private bool _includeReturns;

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
    /// 🔴 20260827작1 §8-B — 「반품포함」 켜고 끄기.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>@bind-Value</c> 를 쓰지 않는다. 그건 값만 담고 <b>서버를 다시 부르지 않아</b>
    /// 체크만 되고 목록은 그대로인 화면이 된다(20260825작16 에서 같은 자리를 틀렸다).
    /// 값을 넣고 <b>곧바로 다시 조회</b>한다.
    /// </remarks>
    private async Task OnIncludeReturnsChangedAsync(bool value)
    {
        _includeReturns = value;
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
            ct: ct,
            includeReturns: _includeReturns);

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
            row.IsChecked = value;
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
    /// <remarks>
    /// 🔴 <b>20260825작16 — 확정 행 선택차단을 풀었다.</b> 사장님 전결:
    /// <i>"반품은 조회전용이 아니라 워크플로우의 한 축이지."</i>
    /// 종전 주석은 2026-04-26 지시(<i>확정 행은 조회 전용</i>)를 근거로 선택 자체를 무시했는데,
    /// 그 결과 <b>확정된 매입은 반품으로 전환할 길이 없었다</b> — 정작 반품이 필요한 건은
    /// 확정된 건이다(확정 전이면 그냥 고치면 된다).
    /// <para>
    /// ⚠️ 선택만 열고 위험한 동작은 그대로 막힌다. 일괄확정은 <c>Status=="draft"</c> 로 거른다.
    /// </para>
    /// <para>
    /// 🔴 <b>20260827작8 — 일괄삭제의 사전필터는 걷어냈다.</b> 종전엔 여기서도
    /// <c>draft</c> 만 남겨 <b>확정 건은 DELETE 요청조차 나가지 않았다.</b> 그 결과
    /// 서버의 삭제가드(연결된 반품전표 번호를 알려주는)가 <b>실행될 기회가 없어</b>
    /// 화면이 <i>"삭제 가능한 draft 상태 매입명세가 없습니다"</i> 라는 엉뚱한 답을 냈다
    /// (1.3.28 사장님 실측 반려). <b>막을지 말지는 서버가 정한다</b> — 화면이 따로
    /// 판정하면 두 기준이 갈리고, 갈린 순간 사고는 조용히 숨는다.
    /// </para>
    /// </remarks>
    /// <param name="row">행</param>
    /// <param name="value">체크 여부</param>
    /// <returns>완료</returns>
    private async Task ToggleOneAsync(PurchaseReceiptListItem row, bool value)
    {
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
    /// <summary>확정 진행 중인 행 — 더블클릭으로 두 번 나가는 것을 막는다.</summary>
    private string? _confirmingId;

    /// <summary>
    /// 🔴 <b>단건 매입확정 (20260825작16).</b> 사장님 지시:
    /// <i>"선택일괄확정버튼 옆에 매입확정 버튼 만들기"</i>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 확정은 <b>재고와 회계를 동시에 움직인다</b> — <c>stock_ledger</c> IN · <c>item_stock</c> 증가 ·
    /// 매입 분개(차변 매입·부가세대급금 / 대변 외상매입금). 그래서 되돌리기가 쉽지 않다.
    /// 눌렀는지 아닌지 헷갈려 두 번 누르는 일이 실제로 있어 <c>_confirmingId</c> 로 잠근다.
    /// </para>
    /// <para>
    /// ⚠️ <c>Idempotency-Key</c> 를 붙인다 — 잠금이 뚫려도 서버가 두 번 반영하지 않는다.
    /// 화면 잠금만 믿지 않는다(네트워크 재시도는 화면을 거치지 않는다).
    /// </para>
    /// </remarks>
    private async Task ConfirmOneAsync(PurchaseReceiptListItem row)
    {
        if (string.IsNullOrWhiteSpace(row.ReceiptId)) return;
        if (!string.Equals(row.Status, "draft", StringComparison.OrdinalIgnoreCase))
        {
            Snackbar.Add("이미 확정된 매입입니다.", Severity.Warning);
            return;
        }
        if (_confirmingId is not null) return;

        var confirm = await DialogService.ShowMessageBoxAsync(
            "매입 확정",
            $"{row.ReceiptNo} 을(를) 확정하시겠습니까? 재고와 회계에 바로 반영됩니다.",
            yesText: "확정", cancelText: "취소");
        if (confirm != true) return;

        _confirmingId = row.ReceiptId;
        await InvokeAsync(StateHasChanged);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"api/purchase/receipts/{Uri.EscapeDataString(row.ReceiptId)}/confirm")
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            };
            req.Headers.TryAddWithoutValidation("Idempotency-Key", row.ReceiptId);
            using var resp = await Http.SendAsync(req, CancellationToken.None);

            if (resp.IsSuccessStatusCode)
            {
                row.Status = "confirmed";
                Snackbar.Add($"{row.ReceiptNo} 매입 확정 완료 (재고 반영됨)", Severity.Success);
            }
            else
            {
                // 실패를 성공으로 위장하지 않는다 — 일괄확정과 같은 정책.
                var body = await resp.Content.ReadAsStringAsync();
                var reason = string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)resp.StatusCode}" : body;
                Snackbar.Add($"확정 실패: {reason[..Math.Min(200, reason.Length)]}", Severity.Error);
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"확정 실패: {ex.Message}", Severity.Error);
        }
        finally
        {
            _confirmingId = null;
            RecalculateSelectionSummary();
            await InvokeAsync(StateHasChanged);
        }
    }

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
                using var req = new HttpRequestMessage(HttpMethod.Post,
                    $"api/purchase/receipts/{Uri.EscapeDataString(id)}/confirm")
                {
                    Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
                };
                req.Headers.TryAddWithoutValidation("Idempotency-Key", id);
                using var resp = await Http.SendAsync(req, CancellationToken.None);
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

        // 20260825작18 — 실패 사유를 삼키지 않는다. 종전엔 성공 건수만 세어,
        //   전환이 왜 안 됐는지 담당자가 알 길이 없었다 (헌법 #15).
        var success = 0;
        var failed = new List<string>();
        foreach (var id in ids)
        {
            var (ok, _, _, error) = await DeliveryService.ConvertReceiptToReturnAsync(id);
            if (ok) success++;
            else failed.Add(string.IsNullOrWhiteSpace(error) ? "알 수 없는 오류" : error);
        }

        if (failed.Count == 0)
        {
            Snackbar.Add($"{success}건 반품 전환 완료", Severity.Success);
        }
        else
        {
            Snackbar.Add($"{success}건 전환 · {failed.Count}건 실패: {failed[0]}", Severity.Warning);
        }

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
            // 🔴 20260827작8 W2 — 서버 문장을 그대로 보여준다(연결된 반품전표 번호가 여기 실려 온다).
            Snackbar.Add($"삭제 불가 — {ApiErrorText.Extract(error)}", Severity.Error,
                cfg => { cfg.RequireInteraction = true; cfg.ShowCloseIcon = true; });
        }
    }

    /// <summary>
    /// 선택 행 일괄 삭제 — draft 건만 대상, confirmed는 스킵.
    /// </summary>
    private async Task BulkDeleteAsync()
    {
        var targets = _selectedRows
            .Where(x => !string.IsNullOrWhiteSpace(x.ReceiptId))
            .ToList();

        if (targets.Count == 0)
        {
            Snackbar.Add("삭제할 매입명세서를 선택해 주세요.", Severity.Warning);
            return;
        }

        var confirm = await DialogService.ShowMessageBoxAsync(
            "매입명세서 일괄 삭제",
            $"선택한 {targets.Count}건을 삭제하시겠습니까? 반품·원장에 연결된 건은 삭제되지 않습니다.",
            yesText: "삭제", cancelText: "취소");
        if (confirm != true) return;

        var success = 0;
        var failed = new List<(string No, string Reason)>();
        foreach (var row in targets)
        {
            var (ok, error) = await DeleteReceiptAsync(row.ReceiptId);
            if (ok) success++;
            else failed.Add((row.ReceiptNo, ApiErrorText.Extract(error)));
        }

        if (failed.Count == 0)
        {
            Snackbar.Add($"{success}건 삭제 완료.", Severity.Success);
        }
        else
        {
            ShowBlockedAsync(success, failed);
        }

        await LoadAsync();
    }

    /// <summary>
    /// 🔴 20260827작8 W1 — 막힌 건을 <b>전표번호와 사유까지</b> 보여준다.
    /// </summary>
    /// <remarks>
    /// 스낵바 한 줄에 첫 건만 잘라 넣으면 <b>나머지가 사라진다.</b>
    /// 사장님이 요구한 건 <i>"몇십만 건에서 틀린 데이터를 빠르게 발견"</i> 이므로
    /// <b>막힌 전표를 전부</b> 나열한다.
    /// </remarks>
    private void ShowBlockedAsync(int success, List<(string No, string Reason)> failed)
    {
        var head = success > 0 ? $"{success}건 삭제 · " : string.Empty;
        var lines = string.Join(" / ", failed.Select(f => $"[{f.No}] {f.Reason}"));
        Snackbar.Add($"{head}{failed.Count}건 삭제 불가 — {lines}", Severity.Warning,
            cfg => { cfg.RequireInteraction = true; cfg.ShowCloseIcon = true; });
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
