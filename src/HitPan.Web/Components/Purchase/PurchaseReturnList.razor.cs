using HitPan.Web.Models;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HitPan.Web.Components.Purchase;

/// <summary>
/// 매입반품 목록 다이얼로그 — PurchaseReceiptList 패턴을 계승한다.
/// 행 클릭 시 다이얼로그를 ReturnId 결과로 닫아 호출자(ReturnPage)가 편집 화면에 로드.
/// </summary>
public partial class PurchaseReturnList : ComponentBase
{
    private DateTime? _startDate = DateTime.Today.AddDays(-30);
    private DateTime? _endDate = DateTime.Today;

    private List<PurchaseReturnListItem> _rows = new();
    private List<PurchaseReturnListItem> _selectedRows = new();
    private bool _allSelected;

    [Inject] private DeliveryService DeliveryService { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    [CascadingParameter]
    private IMudDialogInstance? MudDialog { get; set; }

    [Parameter]
    public EventCallback<string> OnReturnSelected { get; set; }

    /// <summary>14차 P0 봉합(B안): true 면 매출반품(api/sales/returns) 목록·삭제를 사용한다.</summary>
    [Parameter]
    public bool IsSalesReturn { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task OnStartDateChangedAsync(DateTime? value)
    {
        _startDate = value;
        await LoadAsync();
    }

    private async Task OnEndDateChangedAsync(DateTime? value)
    {
        _endDate = value;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _rows = IsSalesReturn
            ? await DeliveryService.GetSalesReturnListAsync(_startDate, _endDate)
            : await DeliveryService.GetPurchaseReturnListAsync(_startDate, _endDate);
        foreach (var row in _rows)
        {
            row.IsChecked = false;
        }
        _selectedRows.Clear();
        _allSelected = false;
        await InvokeAsync(StateHasChanged);
    }

    private async Task ToggleAllAsync(bool value)
    {
        _allSelected = value;
        foreach (var row in _rows) row.IsChecked = value;
        _selectedRows = _rows.Where(x => x.IsChecked).ToList();
        await InvokeAsync(StateHasChanged);
    }

    private async Task ToggleOneAsync(PurchaseReturnListItem row, bool value)
    {
        row.IsChecked = value;
        _selectedRows = _rows.Where(x => x.IsChecked).ToList();
        _allSelected = _rows.Count > 0 && _rows.All(x => x.IsChecked);
        await InvokeAsync(StateHasChanged);
    }

    private async Task SelectRowAsync(PurchaseReturnListItem row)
    {
        if (string.IsNullOrWhiteSpace(row.ReturnId)) return;

        if (MudDialog is not null)
        {
            MudDialog.Close(DialogResult.Ok(row.ReturnId));
            return;
        }

        await OnReturnSelected.InvokeAsync(row.ReturnId);
    }

    private async Task DeleteOneAsync(PurchaseReturnListItem row)
    {
        if (string.IsNullOrWhiteSpace(row.ReturnId)) return;

        var confirm = await DialogService.ShowMessageBoxAsync(
            "반품 삭제",
            $"[{row.ReturnNo}] 을(를) 삭제하시겠습니까?\n(확정된 반품은 삭제할 수 없습니다.)",
            yesText: "삭제", cancelText: "취소");
        if (confirm != true) return;

        var (ok, error) = IsSalesReturn
            ? await DeliveryService.DeleteSalesReturnAsync(row.ReturnId)
            : await DeliveryService.DeletePurchaseReturnAsync(row.ReturnId);
        if (ok)
        {
            Snackbar.Add($"[{row.ReturnNo}] 삭제되었습니다.", Severity.Success);
            await LoadAsync();
        }
        else
        {
            Snackbar.Add($"삭제 실패: {error}", Severity.Error);
        }
    }

    /// <summary>
    /// 목록에서 바로 반품확정한다 (20260825작10).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 사장님 실측 반려: <i>"목록에는 없고, 반품확인서 전표작성에는 반품확정버튼 있음"</i>.
    /// 작9 에서 전표 화면에만 넣고 목록을 빠뜨렸다.
    /// </para>
    /// <para>
    /// 🔴 <b>서버가 준 사유를 그대로 보여준다.</b> 종전에는 응답 본문(JSON)을 통째로 스낵바에 넣어
    /// <c>{"error":"서버..."}</c> 같은 글자가 사용자에게 그대로 떴다 — 사장님이 받은 그 화면이다.
    /// 사람이 읽을 수 있는 문장만 꺼낸다.
    /// </para>
    /// </remarks>
    private async Task ConfirmOneAsync(PurchaseReturnListItem row)
    {
        if (string.IsNullOrWhiteSpace(row.ReturnId)) return;

        var confirm = await DialogService.ShowMessageBoxAsync(
            "반품확정",
            $"[{row.ReturnNo}] 을(를) 확정하시겠습니까?\n\n"
            + "확정하면 반품 수량이 재고에 반영되고 매출·미수에서 차감됩니다.\n"
            + "확정 후에는 반품확인현황에 집계됩니다.",
            yesText: "확정", cancelText: "취소");
        if (confirm != true) return;

        var (ok, error) = await DeliveryService.ConfirmSalesReturnAsync(row.ReturnId);
        if (ok)
        {
            Snackbar.Add($"[{row.ReturnNo}] 반품확정 되었습니다.", Severity.Success);
            await LoadAsync();
            return;
        }

        Snackbar.Add($"반품확정 실패: {ExtractMessage(error)}", Severity.Error);
    }

    /// <summary>
    /// 서버 응답에서 사람이 읽을 문장만 꺼낸다 (20260825작10).
    /// </summary>
    /// <remarks>
    /// 서버는 <c>{"message":"…"}</c> 또는 <c>{"error":"…"}</c> 로 준다.
    /// 파싱에 실패하면 원문을 짧게 자른다 — 아무것도 안 보여주는 것보다 낫다(헌법 #15).
    /// </remarks>
    private static string ExtractMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "알 수 없는 오류";
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            foreach (var key in new[] { "message", "error" })
            {
                if (doc.RootElement.TryGetProperty(key, out var v)
                    && v.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var s = v.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s!;
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // JSON 이 아니면 원문을 쓴다.
        }
        return body.Length > 200 ? body[..200] : body;
    }

    private async Task BulkDeleteAsync()
    {
        var targets = _selectedRows
            .Where(x => !string.IsNullOrWhiteSpace(x.ReturnId)
                        && string.Equals(x.Status, "draft", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (targets.Count == 0)
        {
            Snackbar.Add("삭제 가능한 draft 상태 반품이 없습니다.", Severity.Warning);
            return;
        }

        var confirm = await DialogService.ShowMessageBoxAsync(
            "반품 일괄 삭제",
            $"선택한 draft 상태 {targets.Count}건을 삭제하시겠습니까?",
            yesText: "삭제", cancelText: "취소");
        if (confirm != true) return;

        var success = 0;
        var failed = new List<(string No, string Reason)>();
        foreach (var row in targets)
        {
            var (ok, error) = IsSalesReturn
                ? await DeliveryService.DeleteSalesReturnAsync(row.ReturnId)
                : await DeliveryService.DeletePurchaseReturnAsync(row.ReturnId);
            if (ok) success++;
            else failed.Add((row.ReturnNo, error ?? "unknown"));
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
}
