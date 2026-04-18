using HitPan.Web.Models;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HitPan.Web.Pages.SettingsUi;

/// <summary>
/// 권한설정 페이지 — 직원 목록 + 메뉴별 권한 체크박스 구조.
/// 탭 구조 없음. 인수인계 원칙 준수.
/// </summary>
public partial class PermissionPage : ComponentBase
{
    // 로딩 상태
    private bool _loading = true;
    // 직원 권한 목록
    private List<UserPermissionModel> _users = new();
    // 선택된 직원 ID
    private string? _selectedUserId;

    // ERP 15개 메뉴 템플릿
    private static readonly List<(string Code, string Name)> ErpMenus = new()
    {
        ("DELIVERY", "거래명세서"),
        ("QUOTATION", "견적서"),
        ("SALES_ORDER", "수주서"),
        ("PURCHASE_ORDER", "발주서"),
        ("PURCHASE_RECEIPT", "매입명세서"),
        ("RETURN", "반품"),
        ("ITEM_MASTER", "상품마스터"),
        ("PARTNER_MASTER", "업체마스터"),
        ("BOM", "BOM자재명세서"),
        ("STOCK", "재고현황"),
        ("LEDGER", "원장"),
        ("COLLECTION", "수금"),
        ("PAYMENT", "지급"),
        ("ACCOUNTING", "회계"),
        ("DASHBOARD", "대시보드")
    };

    // 현재 선택된 직원
    private UserPermissionModel? _selectedUser
        => _users.FirstOrDefault(u => u.UserId == _selectedUserId);

    /// <summary>
    /// 초기 진입 시 직원 권한 목록을 조회한다.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        try
        {
            _loading = true;
            _users = await PermSvc.GetAllAsync().ConfigureAwait(false) ?? new();

            // ERP 15개 메뉴 보정
            foreach (var user in _users)
            {
                EnsureErpMenus(user);
            }

            _selectedUserId = _users.FirstOrDefault()?.UserId;
        }
        catch (Exception ex)
        {
            Snackbar.Add($"권한 목록을 불러오지 못했습니다: {ex.Message}", Severity.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// 직원을 선택한다.
    /// </summary>
    private void SelectEmployee(string userId)
    {
        _selectedUserId = userId;
    }

    /// <summary>
    /// 전체 권한을 일괄 토글한다.
    /// </summary>
    private void ToggleAll(bool value)
    {
        if (_selectedUser is null) return;
        foreach (var p in _selectedUser.Permissions)
        {
            p.CanView = value;
            p.CanCreate = value;
            p.CanUpdate = value;
            p.CanDelete = value;
            p.CanExport = value;
        }
    }

    /// <summary>
    /// 선택된 직원 권한을 저장한다.
    /// </summary>
    private async Task SaveAsync()
    {
        if (_selectedUser is null) return;
        var ok = await PermSvc.SaveAsync(new SavePermissionsModel
        {
            UserId = _selectedUser.UserId,
            Permissions = _selectedUser.Permissions
        }).ConfigureAwait(false);

        if (ok)
        {
            Snackbar.Add($"{_selectedUser.EmpName ?? _selectedUser.UserName} 권한이 저장됐습니다.", Severity.Success);
        }
        else
        {
            Snackbar.Add("권한 저장 실패. 다시 시도해주세요.", Severity.Error);
        }
    }

    /// <summary>
    /// 직원 권한에 ERP 15개 메뉴를 보정한다.
    /// </summary>
    private static void EnsureErpMenus(UserPermissionModel user)
    {
        var existing = user.Permissions.ToDictionary(p => p.MenuCode, StringComparer.OrdinalIgnoreCase);
        var normalized = new List<MenuPermissionModel>();

        foreach (var (code, name) in ErpMenus)
        {
            if (existing.TryGetValue(code, out var found))
            {
                found.MenuName = name;
                normalized.Add(found);
            }
            else
            {
                normalized.Add(new MenuPermissionModel
                {
                    MenuCode = code,
                    MenuName = name,
                    CanView = false,
                    CanCreate = false,
                    CanUpdate = false,
                    CanDelete = false,
                    CanExport = false
                });
            }
        }
        user.Permissions = normalized;
    }

    /// <summary>
    /// 역할 라벨을 반환한다.
    /// </summary>
    private static string GetRoleLabel(string role) => role switch
    {
        "TenantAdmin" => "관리자",
        "Manager" => "매니저",
        "sales_user" => "영업사원",
        _ => "일반사원"
    };
}
