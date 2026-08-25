using HitPan.Web.Models;
using MudBlazor;

namespace HitPan.Web.Services;

public sealed class WorkTabService
{
    public const int MaxTabs = 5;

    private readonly Dictionary<int, WorkTabState> _tabs = new();
    private readonly List<int> _order = new();
    private readonly ISnackbar _snackbar;
    private readonly IDialogService _dialogService;
    private int _nextId = 1;
    private int? _activeTabId;

    /// <summary>탭 전환 시 URL 이동을 위한 콜백</summary>
    public Action<string>? NavigateRequested { get; set; }

    public WorkTabService(ISnackbar snackbar, IDialogService dialogService)
    {
        _snackbar = snackbar;
        _dialogService = dialogService;
    }

    public int? ActiveTabId => _activeTabId;

    public event Action? StateChanged;

    public IReadOnlyList<WorkTabState> GetOrderedTabs() =>
        _order.Select(id => _tabs[id]).ToList();

    /// <summary>
    /// 새 탭을 추가한다. 같은 종류의 빈 탭(새 문서)이 있으면 기존 탭으로 전환한다.
    /// </summary>
    public bool TryAddTab(WorkDocumentKind kind) => TryAddTab(kind, null);

    /// <summary>
    /// 새 탭을 열되, 화면에 넘길 질의문자열을 붙인다 (20260825작7).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 판매목록조회 「반품」 버튼이 <c>?deliveryId=…</c> 로 원 거래를 넘길 때 쓴다.
    /// 사장님 오더: <i>"거래명세서 판매목록조회에도 반품으로 상태변경하는 버튼이 있어야됨 → 당연히 반품확인서에 자동반영"</i>
    /// </para>
    /// <para>
    /// ⚠️ <b>질의가 있으면 빈 탭 재사용을 하지 않는다.</b> 재사용하면 이미 열려 있던 빈 탭으로
    /// 전환만 되고 <b>주소가 안 바뀌어</b> 넘긴 거래가 화면에 안 실린다 — 버튼이 먹통으로 보인다.
    /// </para>
    /// </remarks>
    /// <param name="kind">문서 종류.</param>
    /// <param name="query">앞에 <c>?</c> 를 포함한 질의문자열. 없으면 null.</param>
    public bool TryAddTab(WorkDocumentKind kind, string? query)
    {
        var hasQuery = !string.IsNullOrWhiteSpace(query);

        // 같은 종류의 빈 탭(새 문서)이 있으면 그 탭으로 전환.
        // 단 질의를 넘겨야 하면 전환만으로는 값이 안 실리므로 새 탭을 연다.
        if (!hasQuery)
        {
            var existing = _tabs.Values.FirstOrDefault(t => t.Kind == kind && string.IsNullOrEmpty(t.SubTitle));
            if (existing is not null)
            {
                SwitchTab(existing.Id);
                return true;
            }
        }

        if (_tabs.Count >= MaxTabs)
        {
            _snackbar.Add("탭은 최대 5개까지 열 수 있습니다.", Severity.Warning);
            return false;
        }

        var id = _nextId++;
        var state = CreateState(id, kind);
        if (hasQuery)
        {
            state.Url += query;
        }
        _tabs[id] = state;
        _order.Add(id);
        _activeTabId = id;
        Notify();
        NavigateRequested?.Invoke(state.Url);
        return true;
    }

    public void SwitchTab(int tabId)
    {
        if (!_tabs.TryGetValue(tabId, out var tab))
        {
            return;
        }

        _activeTabId = tabId;
        Notify();
        NavigateRequested?.Invoke(tab.Url);
    }

    public void UpdateTabTitle(int tabId, string documentNumber)
    {
        if (!_tabs.TryGetValue(tabId, out var state))
        {
            return;
        }

        state.Title = documentNumber;
        state.SubTitle = null;
        state.IsDirty = false;
        Notify();
    }

    public Task UpdateTabTitleAsync(int tabId, string documentNumber)
    {
        UpdateTabTitle(tabId, documentNumber);
        return Task.CompletedTask;
    }

    public void UpdateSubTitle(int tabId, string? subTitle)
    {
        if (!_tabs.TryGetValue(tabId, out var state))
        {
            return;
        }

        state.SubTitle = string.IsNullOrWhiteSpace(subTitle) ? null : subTitle.Trim();
        Notify();
    }

    public Task UpdateSubTitleAsync(int tabId, string? subTitle)
    {
        UpdateSubTitle(tabId, subTitle);
        return Task.CompletedTask;
    }

    public void SetTabDirty(int tabId, bool isDirty = true)
    {
        if (!_tabs.TryGetValue(tabId, out var state))
        {
            return;
        }

        state.IsDirty = isDirty;
        Notify();
    }

    public async Task CloseTabAsync(int tabId)
    {
        if (!_tabs.TryGetValue(tabId, out var state))
        {
            return;
        }

        if (state.IsDirty)
        {
            var confirm = await _dialogService.ShowMessageBoxAsync(
                "탭 닫기",
                "저장하지 않은 변경이 있습니다. 닫으시겠습니까?",
                yesText: "닫기",
                noText: null,
                cancelText: "취소");
            if (confirm != true)
            {
                return;
            }
        }

        RemoveTab(tabId);
    }

    private void RemoveTab(int tabId)
    {
        var wasActive = _activeTabId == tabId;
        _order.Remove(tabId);
        _tabs.Remove(tabId);

        if (wasActive)
        {
            _activeTabId = _order.Count > 0 ? _order[^1] : null;
        }

        Notify();
    }

    private void Notify() => StateChanged?.Invoke();

    private static WorkTabState CreateState(int id, WorkDocumentKind kind)
    {
        var (title, url, icon) = kind switch
        {
            WorkDocumentKind.Quotation => ("견적서", "/quotations", Icons.Material.Filled.RequestQuote),
            WorkDocumentKind.SalesDelivery => ("거래명세서", "/deliveries", Icons.Material.Filled.Receipt),
            WorkDocumentKind.SalesOrder => ("수주서", "/sales-orders", Icons.Material.Filled.Assignment),
            WorkDocumentKind.PurchaseOrder => ("발주서", "/purchase-orders", Icons.Material.Filled.AddShoppingCart),
            WorkDocumentKind.PurchaseReceipt => ("매입명세서", "/purchases", Icons.Material.Filled.MoveToInbox),
            WorkDocumentKind.Return => ("반품", "/returns", Icons.Material.Filled.AssignmentReturn),
            // 20260825작6: 매출반품은 매입반품과 방향이 반대인 별개 업무라 탭·경로를 따로 둔다.
            WorkDocumentKind.SalesReturn => ("반품확인서", "/sales-returns", Icons.Material.Filled.AssignmentReturned),
            _ => ("작업", "/", Icons.Material.Filled.Description)
        };

        return new WorkTabState
        {
            Id = id,
            Kind = kind,
            Title = title,
            Url = url,
            Icon = icon,
            IsDirty = true
        };
    }
}
