using HitPan.Web.Models;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HitPan.Web.Pages.SettingsUi;

/// <summary>
/// 권한설정 페이지의 상태와 이벤트를 관리한다.
/// </summary>
public partial class PermissionPage : ComponentBase
{
    // 권한 조회/저장 API 서비스
    [Inject]
    private PermissionService PermSvc { get; set; } = default!;

    // 스낵바 알림 서비스
    [Inject]
    private ISnackbar Snackbar { get; set; } = default!;

    // 로딩 상태
    private bool _loading = true;

    // API에서 조회한 사용자 권한 원본
    private List<UserPermissionModel> _allUsers = new();

    // 권한설정 화면에서 사용하는 직원 목록
    private List<PermissionEmployeeRowModel> _employees = new();

    // 선택된 직원 UserId
    private string? _selectedEmployeeUserId;

    // ERP 기본 메뉴 15개 템플릿
    private static readonly List<MenuPermissionModel> ErpMenuTemplate =
    [
        new() { MenuCode = "DELIVERY", MenuName = "거래명세서" },
        new() { MenuCode = "QUOTATION", MenuName = "견적서" },
        new() { MenuCode = "SALES_ORDER", MenuName = "수주서" },
        new() { MenuCode = "PURCHASE_ORDER", MenuName = "발주서" },
        new() { MenuCode = "PURCHASE_RECEIPT", MenuName = "매입명세서" },
        new() { MenuCode = "RETURN", MenuName = "반품" },
        new() { MenuCode = "ITEM_MASTER", MenuName = "상품마스터" },
        new() { MenuCode = "PARTNER_MASTER", MenuName = "업체마스터" },
        new() { MenuCode = "BOM", MenuName = "BOM자재명세서" },
        new() { MenuCode = "STOCK", MenuName = "재고현황" },
        new() { MenuCode = "LEDGER", MenuName = "원장" },
        new() { MenuCode = "COLLECTION", MenuName = "수금" },
        new() { MenuCode = "PAYMENT", MenuName = "지급" },
        new() { MenuCode = "ACCOUNTING", MenuName = "회계" },
        new() { MenuCode = "DASHBOARD", MenuName = "대시보드" }
    ];

    // 현재 선택된 직원 정보를 계산한다.
    private PermissionEmployeeRowModel? SelectedEmployee
        => string.IsNullOrWhiteSpace(_selectedEmployeeUserId)
            ? null
            : _employees.FirstOrDefault(x => x.UserId == _selectedEmployeeUserId);

    /// <summary>
    /// 초기 진입 시 사용자 권한 목록을 조회하고 직원 목록으로 구성한다.
    /// </summary>
    /// <returns>초기화 작업</returns>
    protected override async Task OnInitializedAsync()
    {
        try
        {
            _loading = true;

            // 기존 권한 API를 재사용하여 회사 직원 권한 원본을 가져온다.
            _allUsers = await PermSvc.GetAllAsync().ConfigureAwait(false) ?? new List<UserPermissionModel>();

            // 각 직원마다 ERP 15개 메뉴가 항상 표시되도록 권한 목록을 정규화한다.
            foreach (var user in _allUsers)
            {
                EnsureErpPermissionSet(user);
            }

            // 권한설정은 고객사 직원만 대상이므로 대리점·플랫폼 계정은 목록에서 제외한다.
            _employees = _allUsers
                .Where(static x =>
                    !x.Role.Contains("reseller", StringComparison.OrdinalIgnoreCase) &&
                    !x.Role.Contains("dealer", StringComparison.OrdinalIgnoreCase) &&
                    !x.Role.Contains("platform", StringComparison.OrdinalIgnoreCase) &&
                    !x.Role.Contains("대리점", StringComparison.OrdinalIgnoreCase) &&
                    !x.Role.Contains("플랫폼", StringComparison.OrdinalIgnoreCase))
                .Select(static x => new PermissionEmployeeRowModel
                {
                    UserId = x.UserId,
                    DisplayName = x.EmpName ?? x.UserName,
                    Email = BuildEmail(x.UserName),
                    Department = BuildDepartment(x.Role),
                    Position = BuildPosition(x.Role),
                    PermissionSource = x
                })
                .ToList();

            _selectedEmployeeUserId = _employees.FirstOrDefault()?.UserId;
        }
        catch (Exception ex)
        {
            // 조회 실패 시에도 화면이 멈추지 않도록 로딩을 종료하고 오류를 안내한다.
            Snackbar.Add($"권한 목록을 불러오지 못했습니다: {ex.Message}", Severity.Error);
        }
        finally
        {
            // 성공/실패와 무관하게 로딩을 해제한다.
            _loading = false;
        }
    }

    /// <summary>
    /// 직원 행을 선택한다.
    /// </summary>
    /// <param name="userId">선택 직원 UserId</param>
    private void SelectEmployee(string userId)
    {
        _selectedEmployeeUserId = userId;
    }

    /// <summary>
    /// 선택된 직원 권한을 저장한다.
    /// </summary>
    /// <param name="user">저장 대상 직원 원본</param>
    /// <returns>저장 작업</returns>
    private async Task SaveEmployeePermissionsAsync(UserPermissionModel user)
    {
        // 기존 권한 저장 API 모델을 그대로 사용한다.
        var ok = await PermSvc.SaveAsync(new SavePermissionsModel
        {
            UserId = user.UserId,
            Permissions = user.Permissions
        }).ConfigureAwait(false);

        if (ok)
        {
            Snackbar.Add($"{user.EmpName ?? user.UserName} 권한이 저장됐습니다.", Severity.Success);
        }
        else
        {
            Snackbar.Add("권한 저장 실패. 다시 시도해주세요.", Severity.Error);
        }
    }

    /// <summary>
    /// 권한 테이블에서 모든 권한을 일괄 토글한다.
    /// </summary>
    /// <param name="user">대상 사용자</param>
    /// <param name="value">설정 값</param>
    private static void SelectAll(UserPermissionModel user, bool value)
    {
        foreach (var p in user.Permissions)
        {
            p.CanView = value;
            p.CanCreate = value;
            p.CanUpdate = value;
            p.CanDelete = value;
            p.CanExport = value;
        }
    }

    /// <summary>
    /// 직원추가 버튼 클릭 시 안내 메시지를 표시한다.
    /// </summary>
    private void OpenAddEmployeeDialog()
    {
        // 추후 API 연동 필요: 실제 직원초대/추가 다이얼로그 연결이 필요하다.
        Snackbar.Add("직원추가 기능은 추후 연동 예정입니다.", Severity.Info);
    }

    /// <summary>
    /// 직원 권한 목록에 ERP 기본 메뉴 15개를 보정한다.
    /// </summary>
    /// <param name="user">권한 보정 대상 직원</param>
    private static void EnsureErpPermissionSet(UserPermissionModel user)
    {
        // 기존 API 데이터의 사용자별 권한을 메뉴코드 기준으로 빠르게 찾는다.
        var existingByCode = user.Permissions.ToDictionary(x => x.MenuCode, StringComparer.OrdinalIgnoreCase);
        var normalized = new List<MenuPermissionModel>();

        // 템플릿 순서를 기준으로 누락 메뉴를 생성하고 기존 값은 유지한다.
        foreach (var template in ErpMenuTemplate)
        {
            if (existingByCode.TryGetValue(template.MenuCode, out var existing))
            {
                existing.MenuName = template.MenuName;
                normalized.Add(existing);
            }
            else
            {
                normalized.Add(new MenuPermissionModel
                {
                    MenuCode = template.MenuCode,
                    MenuName = template.MenuName,
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
    /// 선택된 직원 강조용 클래스를 반환한다.
    /// </summary>
    /// <param name="userId">대상 UserId</param>
    /// <returns>CSS 클래스</returns>
    private string GetSelectedClass(string userId)
    {
        return string.Equals(userId, _selectedEmployeeUserId, StringComparison.OrdinalIgnoreCase)
            ? "font-weight-bold text-primary"
            : string.Empty;
    }

    /// <summary>
    /// 계정명이 이메일 형식이면 그대로 사용하고 아니면 빈 값으로 반환한다.
    /// </summary>
    /// <param name="userName">사용자 계정명</param>
    /// <returns>표시 이메일</returns>
    private static string BuildEmail(string userName)
    {
        return userName.Contains('@') ? userName : "-";
    }

    /// <summary>
    /// 역할 문자열을 부서명으로 매핑한다.
    /// </summary>
    /// <param name="role">역할 코드</param>
    /// <returns>부서명</returns>
    private static string BuildDepartment(string role)
    {
        if (role.Equals("TenantAdmin", StringComparison.OrdinalIgnoreCase))
        {
            return "운영관리";
        }

        if (role.Equals("Manager", StringComparison.OrdinalIgnoreCase))
        {
            return "영업관리";
        }

        if (role.Equals("ResellerAdmin", StringComparison.OrdinalIgnoreCase))
        {
            return "대리점관리";
        }

        if (role.Equals("PlatformAdmin", StringComparison.OrdinalIgnoreCase))
        {
            return "플랫폼관리";
        }

        return "일반부서";
    }

    /// <summary>
    /// 역할 문자열을 직급명으로 매핑한다.
    /// </summary>
    /// <param name="role">역할 코드</param>
    /// <returns>직급명</returns>
    private static string BuildPosition(string role)
    {
        if (role.Equals("TenantAdmin", StringComparison.OrdinalIgnoreCase))
        {
            return "관리자";
        }

        if (role.Equals("Manager", StringComparison.OrdinalIgnoreCase))
        {
            return "매니저";
        }

        if (role.Equals("ResellerAdmin", StringComparison.OrdinalIgnoreCase))
        {
            return "대리점관리자";
        }

        if (role.Equals("PlatformAdmin", StringComparison.OrdinalIgnoreCase))
        {
            return "플랫폼관리자";
        }

        return "사원";
    }
}

/// <summary>
/// 권한설정 화면의 직원 목록 행 모델이다.
/// </summary>
public sealed class PermissionEmployeeRowModel
{
    // 사용자 ID
    public string UserId { get; set; } = string.Empty;

    // 표시 이름
    public string DisplayName { get; set; } = string.Empty;

    // 이메일
    public string Email { get; set; } = string.Empty;

    // 부서
    public string Department { get; set; } = string.Empty;

    // 직급
    public string Position { get; set; } = string.Empty;

    // 실제 권한 저장 대상 원본 모델
    public UserPermissionModel PermissionSource { get; set; } = new();
}
