using HitPan.Web.Components.Common;
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
    // 목록을 못 불러왔는지 여부. 🔴 "직원 0명" 과 반드시 구분한다(20260809작4 ②번 봉합).
    //   종전에는 GetAllAsync() 가 null 을 줘도 `?? new()` 로 뭉개서,
    //   좌측 직원목록이 빈 카드가 되고 우측은 "좌측에서 직원을 선택하세요" 로 교착됐다.
    //   화면만 보면 직원이 다 사라진 것처럼 보인다 — "없다" 와 "못 불러왔다" 는 다른 사실이다.
    private bool _loadFailed;
    // 직원 권한 목록
    private List<UserPermissionModel> _users = new();
    // 선택된 직원 ID
    private string? _selectedUserId;

    // ERP 메뉴 템플릿 — 🔴 백엔드 PermissionService.MenuList 와 코드가 100% 같아야 한다.
    //
    // 2026-08-09 봉합 (사장님 결재 · 작4 ①번):
    //   종전 프론트 15개 중 4개가 백엔드와 코드가 달라 권한이 영원히 안 먹었다.
    //     ITEM_MASTER→ITEM · PARTNER_MASTER→PARTNER · PURCHASE_RECEIPT→PURCHASE · RETURN(백엔드 없음)
    //   체크하고 저장하면 "저장됐습니다"가 뜨는데 실제 권한 조회는 menu_code='ITEM' 로 하므로 항상 0이었다.
    //   또 백엔드에만 있던 5개(APPROVAL·HR·MONTHLY_CLOSING·CERTIFICATE·SETTINGS·USERS)는 화면에서 사라져
    //   부여할 방법 자체가 없었다. USERS·APPROVAL 은 컨트롤러가 실제로 강제하는 코드다.
    //
    // ⚠️ 이 목록을 고칠 때는 반드시 백엔드
    //    src/HitPan.Application/Services/PermissionService.cs MenuList 를 함께 고친다.
    //    한쪽만 고치면 같은 사고가 재발한다. (헌법 #12 — 인터페이스 확장 시 모든 구현체 확인)
    private static readonly List<(string Code, string Name)> ErpMenus = new()
    {
        ("DELIVERY", "거래명세서"),
        ("QUOTATION", "견적서"),
        ("SALES_ORDER", "수주서"),
        ("PURCHASE_ORDER", "발주서"),
        ("PURCHASE", "매입명세서"),
        ("ITEM", "상품마스터"),
        ("PARTNER", "업체마스터"),
        ("BOM", "BOM 자재명세서"),
        ("STOCK", "재고현황"),
        ("LEDGER", "원장"),
        ("COLLECTION", "수금"),
        ("PAYMENT", "지급"),
        ("ACCOUNTING", "회계"),
        ("APPROVAL", "결재"),
        ("HR", "인사"),
        ("HR_PROXY", "근태 대리입력"),
        // 작(2026-08-24) 작2 [3][4] — 백엔드 MenuList 와 같은 코드여야 한다(CI 정합 검사).
        ("ANNUAL_LEAVE_GRANT", "연차 부여"),
        ("RESIGNATION", "입사/퇴사"),
        ("MONTHLY_CLOSING", "월마감"),
        ("CERTIFICATE", "범용인증서"),
        ("DASHBOARD", "대시보드"),
        ("SETTINGS", "사용환경설정"),
        ("USERS", "사용자관리")
    };

    /// <summary>
    /// 🔴 <b>서버가 실제로 강제하는 메뉴</b>. 작(2026-08-14, 1.2.74 실사용 P0).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 사장님: <i>"권한설정으로 모든걸 풀었지만 ... 아무것도 못함."</i>
    /// 실측하니 위 20개 중 <b>서버에 <c>[RequirePermission]</c> 이 붙은 것은 8개뿐</b>이고,
    /// 나머지 12개는 <b>켜도 꺼도 서버 동작이 같았다</b> — 아무 데도 안 붙은 체크박스였다.
    /// </para>
    /// <para>
    /// 🔴 <b>안 먹는 체크박스를 보여주는 것이 제일 나쁘다.</b> 사장님이 전부 켜 놓고
    /// "다 풀었다" 고 여기신 것이 바로 이것 때문이다 — 되는 척의 한 형태다.
    /// ⇒ 강제되지 않는 항목은 <b>화면에서 감춘다.</b> 목록에서 지우지는 않는다 —
    /// 지우면 백엔드 <c>MenuList</c> 와 어긋나 CI 정합 검사가 깨진다(헌법 #12).
    /// </para>
    /// <para>
    /// ⚠️ 이건 <b>임시 조치</b>다. 나머지 12개에 실제 권한 강제를 붙이는 것이 정답이고,
    /// 그때 이 집합에 하나씩 옮겨 담는다(사장님 결재 2026-08-14 — 지금은 감추기).
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> EnforcedMenus = new(StringComparer.Ordinal)
    {
        "ACCOUNTING", "APPROVAL", "CERTIFICATE", "COLLECTION",
        "HR", "HR_PROXY", "MONTHLY_CLOSING", "PAYMENT", "USERS",
        // 작(2026-08-24) 작2 [3][4] — 서버가 실제로 강제한다. 그래서 여기 넣을 자격이 있다.
        //   ANNUAL_LEAVE_GRANT → AnnualLeaveController 클래스 [RequirePermission(view)]
        //                        + confirm 만 update (보는 것과 주는 것을 가른다)
        //   RESIGNATION        → ResignationController [RequirePermission]
        // 🔴 강제 없이 여기 넣으면 "체크는 되는데 안 먹는" 되는 척이 된다(8/14 사장님 지적).
        "ANNUAL_LEAVE_GRANT", "RESIGNATION"
    };

    /// <summary>화면에 보여줄 메뉴 — 실제로 먹는 것만.</summary>
    private static IEnumerable<(string Code, string Name)> VisibleMenus =>
        ErpMenus.Where(m => EnforcedMenus.Contains(m.Code));

    // 현재 선택된 직원
    private UserPermissionModel? _selectedUser
        => _users.FirstOrDefault(u => u.UserId == _selectedUserId);

    /// <summary>
    /// 초기 진입 시 직원 권한 목록을 조회한다.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        await LoadAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 직원 권한 목록을 조회한다. "다시 시도" 버튼도 이 메서드를 그대로 부른다.
    /// </summary>
    private async Task LoadAsync()
    {
        _loading = true;
        _loadFailed = false;
        try
        {
            var list = await PermSvc.GetAllAsync().ConfigureAwait(false);
            if (list is null)
            {
                // null = 실패. 정상 0건과 다른 사실이므로 화면에서 갈라 보여준다.
                _loadFailed = true;
                _users = new();
                _selectedUserId = null;
                return;
            }

            _users = list;

            // ERP 메뉴 보정 (코드는 백엔드 MenuList 와 동일해야 한다 — ErpMenus 주석 참조)
            foreach (var user in _users)
            {
                EnsureErpMenus(user);
            }

            _selectedUserId = _users.FirstOrDefault()?.UserId;
        }
        catch (Exception ex)
        {
            // 서비스가 삼키지 못한 예외까지 여기서 받아 "못 불러왔다" 로 확정한다.
            _loadFailed = true;
            _users = new();
            _selectedUserId = null;
            Snackbar.Add($"권한 목록을 불러오지 못했습니다: {ex.Message}", Severity.Error);
        }
        finally
        {
            // 예외가 나도 진행바가 영원히 돌지 않게 한다.
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
    /// 선택된 직원 권한을 저장한다. WO-20260430-9 Step-up 보호.
    /// </summary>
    private async Task SaveAsync()
    {
        if (_selectedUser is null) return;

        // 권한 변경 = 민감 작업 (사장님 헌법 #18 + WO-9)
        var stepUp = await StepUpDialog.RequestAsync(DialogService,
            $"{_selectedUser.EmpName ?? _selectedUser.UserName} 권한 변경").ConfigureAwait(false);
        if (!stepUp) return;

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
    /// 직원 권한 목록을 ERP 메뉴 템플릿(<see cref="ErpMenus"/>)에 맞춰 보정한다.
    /// 백엔드가 내려준 권한 중 템플릿에 없는 코드는 화면에서 사라지므로,
    /// 템플릿은 백엔드 MenuList 와 항상 같아야 한다.
    /// </summary>
    /// <remarks>
    /// 🔴 작(2026-08-14): <see cref="VisibleMenus"/> 만 돈다 — <b>서버가 실제로 강제하는 것만</b>
    /// 보여준다. 안 먹는 체크박스를 보여주면 "다 풀었는데 안 된다" 가 된다(사장님 1.2.74 지적).
    /// 이미 저장된 값은 <b>지우지 않는다</b> — 감추기만 한다. 나중에 강제를 붙이면 그대로 살아난다.
    /// </remarks>
    private static void EnsureErpMenus(UserPermissionModel user)
    {
        var existing = user.Permissions.ToDictionary(p => p.MenuCode, StringComparer.OrdinalIgnoreCase);
        var normalized = new List<MenuPermissionModel>();

        foreach (var (code, name) in VisibleMenus)
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

    /// <summary>
    /// 이름 첫 글자(이니셜)를 반환. 둘 다 비어있으면 "·" 반환 (Substring 폭발 방지).
    /// </summary>
    private static string GetInitial(string? primary, string? fallback)
    {
        var name = !string.IsNullOrWhiteSpace(primary) ? primary
                 : !string.IsNullOrWhiteSpace(fallback) ? fallback
                 : null;
        return string.IsNullOrEmpty(name) ? "·" : name[..1];
    }
}
