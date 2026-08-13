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
    // 작(2026-08-13) 단계4 토대: 직급 마스터 드롭다운.
    [Inject] private PositionService PositionSvc { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    // 작(2026-08-12) 단계0: 퇴사 처리 확인 대화상자용.
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private bool _loading = true;
    private List<EmployeeListItemModel> _employees = new();
    // 봉합 (2026-06-22, 10차 P1-1): 부서 드롭다운 데이터. 사원 부서는 dept_id 로 저장되므로 선택지를 채운다.
    private List<DepartmentModel> _departments = new();

    /// <summary>
    /// 직급 드롭다운 선택지. 작(2026-08-13) 단계4 토대.
    /// </summary>
    /// <remarks>
    /// 🔴 종전엔 직급이 <b>자유 텍스트</b>였다. 그래서 12명 중 8명이 직급 없음이고
    /// (NULL 2 · 공백 5 · <c>"0"</c> 1), 마스터(<c>positions</c>)와 아무 연결이 없었다.
    /// 부서는 6/22 에 이미 드롭다운으로 봉합했는데 직급만 남아 있었다.
    ///
    /// ⚠️ <c>employees.position</c> 은 <b>이름 문자열</b>을 담는다(FK 아님).
    /// 그래서 선택지도 <b>이름</b>으로 고른다 — 기존 "과장"·"사원" 값이 그대로 살아난다.
    /// ID 로 바꾸면 기존 12명 값이 전부 매칭 실패로 날아간다(오염값 마이그가 선행돼야 함).
    /// </remarks>
    private List<PositionListItemModel> _positions = new();

    /// <summary>
    /// 직급 드롭다운에 보여줄 이름들.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>지금 이 사원이 가진 값이 마스터에 없어도 선택지에 남긴다.</b>
    /// 안 남기면 그 사원을 열었을 때 드롭다운이 빈칸으로 보이고, 다른 항목만 고쳐 저장해도
    /// <b>직급이 조용히 지워진다</b>. 실측으로 "과장"·"사원" 값을 가진 사원이 4명 있는데
    /// <c>positions</c> 는 0행이라, 이 처리가 없으면 저장 한 번에 그 값들이 날아간다.
    ///
    /// 비활성 직급도 같은 이유로 남긴다 — 이미 그 직급인 사람이 있기 때문이다.
    /// 다만 <b>새로 고를 수 있는 것</b>은 활성 직급뿐이라, 활성분을 앞에 둔다.
    /// </remarks>
    private IEnumerable<string> PositionOptions
    {
        get
        {
            var names = _positions
                .Where(p => p.IsActive)
                .Select(p => p.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();

            // 이 사원이 지금 가진 값이 목록에 없으면 끝에 덧붙인다(값 유실 방지).
            var current = _edit.Position;
            if (!string.IsNullOrWhiteSpace(current)
                && !names.Contains(current, StringComparer.Ordinal))
            {
                names.Add(current!);
            }

            return names;
        }
    }
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

    /// <summary>
    /// 흐름 연결 (사장님 오더 2026-08-10, 20260810작4).
    /// HR직원현황에서 어느 직원을 눌렀는지 받는다 — <c>/employees?empId=EMP-002</c>.
    ///
    /// ■ 왜 필요한가
    ///   종전에는 12명 중 김대리를 눌러도 파라미터 없이 <c>/employees</c> 로만 보내
    ///   목록 맨 위(첫 사원)가 열렸다. 누른 사람과 열린 사람이 다르다.
    ///   값이 없으면 기존대로 첫 사원을 연다 — 사이드바로 직접 들어오는 경로가 안 깨진다.
    /// </summary>
    [Parameter, SupplyParameterFromQuery(Name = "empId")]
    public string? EmpIdFromQuery { get; set; }

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
        // 봉합 (2026-06-22, 10차 P1-1): 부서 선택지 로드 (읽기 전용 마스터).
        _departments = await EmployeeSvc.GetDepartmentsAsync().ConfigureAwait(false);
        // 작(2026-08-13) 단계4: 직급 선택지 로드.
        // ⚠️ 조회 실패(null)와 "직급 0개"(빈 목록)를 가른다 — PositionService 가 그렇게 돌려준다.
        //    실패를 빈 목록으로 뭉개면 직급이 등록된 회사에서 드롭다운이 비어 보인다.
        _positions = await PositionSvc.GetListAsync().ConfigureAwait(false) ?? new List<PositionListItemModel>();
        _permissionUsers = await PermSvc.GetAllAsync().ConfigureAwait(false) ?? new List<UserPermissionModel>();

        if (_employees.Count > 0)
        {
            // 흐름 연결 (20260810작4): HR직원현황에서 지목한 사원이 있으면 그 사람을 연다.
            //   목록에 없는 값(퇴직·삭제·주소 직접 입력)이면 조용히 첫 사원으로 되돌린다 —
            //   화면이 비거나 오류를 띄우는 것보다 낫다.
            var target = !string.IsNullOrWhiteSpace(EmpIdFromQuery)
                && _employees.Any(e => e.EmployeeId == EmpIdFromQuery)
                    ? EmpIdFromQuery!
                    : _employees[0].EmployeeId;

            await SelectEmployeeAsync(target).ConfigureAwait(false);
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

    /// <summary>
    /// 작(2026-08-12) 그룹웨어 단계0 P0-A·B·C: 퇴사 처리.
    /// </summary>
    /// <remarks>
    /// 기존 <see cref="DeleteAsync"/> 는 남겨 둔다(헌법 #1). 다만 화면 버튼은 이 경로를 쓴다.
    /// 차이: ① 로그인 계정도 함께 차단 ② 결재선 영향 사전 경고 ③ 실제 퇴사일·사유 기록.
    /// </remarks>
    private async Task ResignAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedEmployeeId))
        {
            Snackbar.Add("퇴사 처리할 사원을 선택하세요.", Severity.Warning);
            return;
        }

        var parameters = new DialogParameters
        {
            { nameof(EmployeeResignDialog.EmployeeId), _selectedEmployeeId },
            { nameof(EmployeeResignDialog.EmployeeName), _edit.EmpName }
        };

        var dialog = await DialogService.ShowAsync<EmployeeResignDialog>(
            "퇴사 처리", parameters).ConfigureAwait(false);
        var result = await dialog.Result.ConfigureAwait(false);

        if (result is null || result.Canceled)
        {
            return;
        }

        // 작(2026-08-12) 검증팀 P1-5 봉합: 실제로 일어난 일만 말한다.
        // 앞서는 성공하면 무조건 "계정도 차단됐습니다" 라고 했는데, 계정이 없는 사원
        // (실측 12명 중 11명)에게도 같은 문구가 떴다 — 되는 척이다.
        var accountBlocked = result.Data is true;

        Snackbar.Add(
            accountBlocked
                ? "퇴사 처리했습니다. 로그인 계정도 함께 차단됐습니다."
                : "퇴사 처리했습니다.",
            Severity.Success);

        await ReloadAllAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 작20260429 연차 관리 (사장님 결재): 그리드 한 행의 연차만 단독 저장.
    /// 사용자 실수 방지: 사용 > 부여 시 경고만 띄우고 저장은 진행한다 (마이너스 잔여 허용 — 가불 개념).
    /// </summary>
    private async Task SaveAnnualLeaveAsync(EmployeeListItemModel row)
    {
        if (row.AnnualLeaveTotal < 0 || row.AnnualLeaveUsed < 0)
        {
            Snackbar.Add("연차는 0 이상이어야 합니다.", Severity.Warning);
            return;
        }

        var ok = await EmployeeSvc.UpdateAnnualLeaveAsync(
            row.EmployeeId, row.AnnualLeaveTotal, row.AnnualLeaveUsed).ConfigureAwait(false);
        if (!ok)
        {
            Snackbar.Add($"{row.EmpName} 연차 저장 실패.", Severity.Error);
            return;
        }

        var msg = row.AnnualLeaveUsed > row.AnnualLeaveTotal
            ? $"{row.EmpName} 연차 저장 완료 (사용량이 부여를 초과했습니다)"
            : $"{row.EmpName} 연차 저장 완료 (잔여 {row.AnnualLeaveRemaining:0.#}일)";
        Snackbar.Add(msg, Severity.Success);
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
            // 작(2026-08-13) 단계4: 주당 소정근로시간을 폼에 되돌려 넣는다.
            // 🔴 이걸 빠뜨리면 다른 항목만 고쳐 저장해도 이 값이 null 로 덮여 사라진다.
            //    null 은 '미정' 이라는 뜻이므로 그대로 실어 보낸다(40 으로 채우지 않는다).
            WeeklyHours = detail.WeeklyHours,
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
