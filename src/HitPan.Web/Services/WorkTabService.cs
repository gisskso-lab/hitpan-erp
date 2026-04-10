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

    public WorkTabService(ISnackbar snackbar, IDialogService dialogService)
    {
        _snackbar = snackbar;
        _dialogService = dialogService;
    }

    public int? ActiveTabId => _activeTabId;

    public event Action? StateChanged;

    public IReadOnlyList<WorkTabState> GetOrderedTabs() =>
        _order.Select(id => _tabs[id]).ToList();

    public bool TryAddTab(WorkDocumentKind kind)
    {
        if (_tabs.Count >= MaxTabs)
        {
            _snackbar.Add("탭은 최대 5개까지 열 수 있습니다.", Severity.Warning);
            return false;
        }

        var id = _nextId++;
        var state = new WorkTabState
        {
            Id = id,
            Kind = kind,
            IsDirty = true
        };
        _tabs[id] = state;
        _order.Add(id);
        _activeTabId = id;
        Notify();
        return true;
    }

    public void SwitchTab(int tabId)
    {
        if (!_tabs.ContainsKey(tabId))
        {
            return;
        }

        _activeTabId = tabId;
        Notify();
    }

    public void UpdateTabTitle(int tabId, string documentNumber)
    {
        if (!_tabs.TryGetValue(tabId, out var state))
        {
            return;
        }

        state.DocumentNumber = documentNumber;
        state.IsDirty = false;
        Notify();
    }

    public Task UpdateTabTitleAsync(int tabId, string documentNumber)
    {
        UpdateTabTitle(tabId, documentNumber);
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
}
