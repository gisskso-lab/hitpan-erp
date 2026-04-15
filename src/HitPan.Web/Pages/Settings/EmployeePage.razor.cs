using HitPan.Web.Models;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HitPan.Web.Pages.SettingsUi;

/// <summary>
/// 사원관리 페이지 코드비하인드이다.
/// </summary>
public partial class EmployeePage : ComponentBase
{
    [Inject] private EmployeeService EmployeeSvc { get; set; } = default!;
    [Inject] private LeaveRequestService LeaveSvc { get; set; } = default!;
    [Inject] private PermissionService PermSvc { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private bool _loading = true;
    private List<EmployeeListItemModel> _employees = new();
    private List<LeaveRequestModel> _leaveRequests = new();
    private List<UserPermissionModel> _permissionUsers = new();
    private UserPermissionModel? _selectedPermUser;
    private EmployeeDetailModel? _selectedDetail;
    private string? _selectedEmployeeId;
    private string _selectedEmpNoPreview = "EMP-자동채번";
    private EmployeeEditModel _edit = new();
    private bool _isCreateMode = true;
    private bool _showLeaveForm;
    private CreateLeaveRequestModel _leaveForm = new();

    private decimal _annualTotal = 15m;
    private decimal _annualUsed;
    private decimal _annualRemain = 15m;
    private string? _selectedPendingLeaveId;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _loading = true;
            await ReloadAllAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"사원관리 초기화 실패: {ex.Message}", Severity.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// 초기 진입/저장 후 공통 데이터를 재조회한다.
    /// </summary>
    private async Task ReloadAllAsync()
    {
        _employees = await EmployeeSvc.GetListAsync().ConfigureAwait(false);
        _permissionUsers = await PermSvc.GetAllAsync().ConfigureAwait(false) ?? new List<UserPermissionModel>();

        if (_employees.Count > 0)
        {
            await SelectEmployeeAsync(_employees[0].EmployeeId).ConfigureAwait(false);
        }
        else
        {
            StartCreateMode();
        }
    }

    private void StartCreateMode()
    {
        _isCreateMode = true;
        _selectedEmployeeId = null;
        _selectedDetail = null;
        _selectedPermUser = null;
        _leaveRequests.Clear();
        _selectedPendingLeaveId = null;
        _selectedEmpNoPreview = BuildNextEmpNoPreview();
        _edit = new EmployeeEditModel
        {
            EmpType = "regular",
            Role = "sales_user",
            JoinDate = DateTime.Today
        };
    }

    private void CancelEdit()
    {
        if (_selectedEmployeeId is null)
        {
            StartCreateMode();
            return;
        }

        _ = SelectEmployeeAsync(_selectedEmployeeId);
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_edit.EmpName))
        {
            Snackbar.Add("사원 이름은 필수입니다.", Severity.Warning);
            return;
        }

        var ok = _isCreateMode
            ? await EmployeeSvc.CreateAsync(_edit).ConfigureAwait(false)
            : await EmployeeSvc.UpdateAsync(_selectedEmployeeId!, _edit).ConfigureAwait(false);

        if (!ok)
        {
            Snackbar.Add("저장 실패. 다시 시도해주세요.", Severity.Error);
            return;
        }

        Snackbar.Add(_isCreateMode ? "사원을 등록했습니다." : "사원 정보를 수정했습니다.", Severity.Success);
        await ReloadAllAsync().ConfigureAwait(false);
    }

    private async Task DeleteAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedEmployeeId))
        {
            Snackbar.Add("삭제할 사원을 선택하세요.", Severity.Warning);
            return;
        }

        var ok = await EmployeeSvc.DeleteAsync(_selectedEmployeeId).ConfigureAwait(false);
        if (!ok)
        {
            Snackbar.Add("삭제(비활성화) 실패.", Severity.Error);
            return;
        }

        Snackbar.Add("사원을 비활성화했습니다.", Severity.Success);
        await ReloadAllAsync().ConfigureAwait(false);
    }

    private async Task SelectEmployeeAsync(string employeeId)
    {
        var detail = await EmployeeSvc.GetAsync(employeeId).ConfigureAwait(false);
        if (detail is null)
        {
            Snackbar.Add("사원 상세를 불러오지 못했습니다.", Severity.Warning);
            return;
        }

        _isCreateMode = false;
        _selectedEmployeeId = employeeId;
        _selectedDetail = detail;
        _selectedEmpNoPreview = detail.EmpNo;
        _edit = new EmployeeEditModel
        {
            EmpName = detail.EmpName,
            DeptId = detail.DeptId,
            DeptName = detail.DeptName,
            Position = detail.Position,
            JobTitle = detail.JobTitle,
            EmpType = string.IsNullOrWhiteSpace(detail.EmpType) ? "regular" : detail.EmpType,
            JoinDate = detail.JoinDate,
            Phone = detail.Phone,
            Email = detail.Email,
            Role = string.IsNullOrWhiteSpace(detail.Role) ? "sales_user" : detail.Role
        };

        await ReloadLeaveAsync(employeeId).ConfigureAwait(false);
        // 연결 계정(user_id)이 있는 사원만 권한 패널과 연결한다.
        _selectedPermUser = string.IsNullOrWhiteSpace(detail.UserId)
            ? null
            : _permissionUsers.FirstOrDefault(x => x.UserId == detail.UserId);
        EnsurePermissionMenus(_selectedPermUser);
    }

    private async Task ReloadLeaveAsync(string employeeId)
    {
        _leaveRequests = await LeaveSvc.GetListAsync(employeeId).ConfigureAwait(false);
        _annualUsed = _leaveRequests
            .Where(x => x.Status.Equals("approved", StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.LeaveDays);
        _annualRemain = Math.Max(0, _annualTotal - _annualUsed);
        _selectedPendingLeaveId = _leaveRequests
            .FirstOrDefault(x => x.Status.Equals("pending", StringComparison.OrdinalIgnoreCase))
            ?.RequestId;
    }

    private async Task SavePermissionsAsync()
    {
        if (_selectedPermUser is null)
        {
            Snackbar.Add("연결된 사용자 계정이 없어 권한 저장이 불가합니다.", Severity.Info);
            return;
        }

        var ok = await PermSvc.SaveAsync(new SavePermissionsModel
        {
            UserId = _selectedPermUser.UserId,
            Permissions = _selectedPermUser.Permissions
        }).ConfigureAwait(false);

        Snackbar.Add(ok ? "권한이 저장되었습니다." : "권한 저장 실패.", ok ? Severity.Success : Severity.Error);
    }

    private void ToggleLeaveForm()
    {
        _showLeaveForm = !_showLeaveForm;
        if (_showLeaveForm && _selectedEmployeeId is not null)
        {
            _leaveForm = new CreateLeaveRequestModel
            {
                EmployeeId = _selectedEmployeeId,
                LeaveType = "annual",
                LeaveDays = 1m,
                StartDate = DateTime.Today,
                EndDate = DateTime.Today
            };
        }
    }

    private async Task CreateLeaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedEmployeeId))
        {
            Snackbar.Add("사원을 먼저 선택하세요.", Severity.Warning);
            return;
        }

        _leaveForm.EmployeeId = _selectedEmployeeId;
        var ok = await LeaveSvc.CreateAsync(_leaveForm).ConfigureAwait(false);
        if (!ok)
        {
            Snackbar.Add("연차 신청 실패.", Severity.Error);
            return;
        }

        Snackbar.Add("연차 신청을 등록했습니다.", Severity.Success);
        _showLeaveForm = false;
        await ReloadLeaveAsync(_selectedEmployeeId).ConfigureAwait(false);
    }

    private async Task ApproveLeaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedPendingLeaveId))
        {
            Snackbar.Add("승인할 대기 건이 없습니다.", Severity.Info);
            return;
        }

        var ok = await LeaveSvc.ApproveAsync(_selectedPendingLeaveId).ConfigureAwait(false);
        if (!ok)
        {
            Snackbar.Add("승인 처리 실패.", Severity.Error);
            return;
        }

        Snackbar.Add("연차 신청을 승인했습니다.", Severity.Success);
        if (_selectedEmployeeId is not null)
        {
            await ReloadLeaveAsync(_selectedEmployeeId).ConfigureAwait(false);
        }
    }

    private async Task RejectLeaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedPendingLeaveId))
        {
            Snackbar.Add("반려할 대기 건이 없습니다.", Severity.Info);
            return;
        }

        var ok = await LeaveSvc.RejectAsync(_selectedPendingLeaveId, "관리자 반려").ConfigureAwait(false);
        if (!ok)
        {
            Snackbar.Add("반려 처리 실패.", Severity.Error);
            return;
        }

        Snackbar.Add("연차 신청을 반려했습니다.", Severity.Warning);
        if (_selectedEmployeeId is not null)
        {
            await ReloadLeaveAsync(_selectedEmployeeId).ConfigureAwait(false);
        }
    }

    private string BuildNextEmpNoPreview()
    {
        var max = _employees
            .Select(x =>
            {
                var digits = new string(x.EmpNo.Where(char.IsDigit).ToArray());
                return int.TryParse(digits, out var n) ? n : 0;
            })
            .DefaultIfEmpty(0)
            .Max() + 1;
        return $"EMP-{max:000}";
    }

    private static string GetLeaveLabel(string status) => status.ToLowerInvariant() switch
    {
        "approved" => "승인",
        "rejected" => "반려",
        _ => "대기"
    };

    private static Color GetLeaveColor(string status) => status.ToLowerInvariant() switch
    {
        "approved" => Color.Success,
        "rejected" => Color.Error,
        _ => Color.Warning
    };

    private Task OnJoinDateChanged(DateTime? date)
    {
        _edit.JoinDate = date ?? DateTime.Today;
        return Task.CompletedTask;
    }

    private Task OnLeaveStartChanged(DateTime? date)
    {
        _leaveForm.StartDate = date ?? DateTime.Today;
        return Task.CompletedTask;
    }

    private Task OnLeaveEndChanged(DateTime? date)
    {
        _leaveForm.EndDate = date ?? DateTime.Today;
        return Task.CompletedTask;
    }

    private static void EnsurePermissionMenus(UserPermissionModel? user)
    {
        if (user is null)
        {
            return;
        }

        var menuSeed = new (string Code, string Name)[]
        {
            ("DASHBOARD", "대시보드"),
            ("ITEM_MASTER", "상품마스터"),
            ("PARTNER_MASTER", "업체마스터"),
            ("QUOTATION", "견적서"),
            ("SALES_ORDER", "수주서"),
            ("DELIVERY", "거래명세서"),
            ("PURCHASE_ORDER", "발주서"),
            ("PURCHASE_RECEIPT", "매입명세서"),
            ("STOCK", "재고현황"),
            ("ACCOUNTING", "회계")
        };

        var existing = user.Permissions.ToDictionary(x => x.MenuCode, StringComparer.OrdinalIgnoreCase);
        var normalized = new List<MenuPermissionModel>();
        foreach (var (code, name) in menuSeed)
        {
            if (existing.TryGetValue(code, out var found))
            {
                found.MenuName = name;
                normalized.Add(found);
            }
            else
            {
                normalized.Add(new MenuPermissionModel { MenuCode = code, MenuName = name });
            }
        }
        user.Permissions = normalized;
    }
}
