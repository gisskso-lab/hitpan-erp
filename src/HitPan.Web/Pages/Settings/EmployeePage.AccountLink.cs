using HitPan.Web.Models;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;

namespace HitPan.Web.Pages.SettingsUi;

/// <summary>
/// 20261005작5 — 사원관리의 계정 몫(설계 §2·§3·§5-4). 기존 코드비하인드(<c>EmployeePage.razor.cs</c>)는 한 줄(단계 읽기 호출)만 더하고,
/// 나머지는 이 파일에 모은다(#1 추가만).
/// </summary>
/// <remarks>
/// <para>사원관리는 <b>보기 + 미등록 사원에게 계정 만들기</b>까지만(C). 사용 안 함·다시 사용은 [직원 계정 관리] 한 곳에서만 바꾼다.</para>
/// <para>[계정 만들기] 노출 = 사원관리 화면 인가(대표·관리자·인사 — 무변경 P2-03) <b>그리고</b> 「직원 계정 관리」 2단계 이상.
/// 화면 숨김은 편의다 — 서버 <c>POST api/users/for-employee</c> 가 <c>[RequireUsersLevel(2)]</c> 로 다시 막는다.</para>
/// </remarks>
public partial class EmployeePage
{
    [Inject] private HttpClient Http { get; set; } = default!;
    [Inject] private ILogger<AccountLinkApi> LinkLog { get; set; } = default!;
    [Inject] private ILogger<AccountSeatApi> SeatLog { get; set; } = default!;

    private AccountLinkApi? _linkApi;
    private AccountSeatApi? _seatApi;

    /// <summary>내 「직원 계정 관리」 단계 0~3. 못 읽으면 0(버튼을 안 보인다).</summary>
    private int _usersLevel;

    // [계정 만들기] 창
    private bool _showAccountCreate;
    private bool _accountBusy;
    private bool _accountShowPassword;
    private EmployeeListItemModel? _accountTarget;
    private CreateForEmployeeModel _accountModel = new();

    // 한도 안내 창(작3 같은 창 · A)
    private bool _showSeatNotice;
    private bool _seatNoticeBlocked;
    private AccountSeatsModel? _seats;

    /// <summary>OnInitializedAsync 에서 한 번 — 단계를 읽는다.</summary>
    private async Task LoadAccountLinkLevelAsync()
    {
        _linkApi ??= new AccountLinkApi(Http, LinkLog);
        _seatApi ??= new AccountSeatApi(Http, SeatLog);
        var me = await _linkApi.GetMyLevelAsync().ConfigureAwait(false);
        _usersLevel = me?.Level ?? 0;
    }

    private static Color AccountStatusColor(string? status) => status switch
    {
        "owner" => Color.Primary,
        "active" => Color.Success,
        "suspended" => Color.Warning,
        _ => Color.Default
    };

    /// <summary>[계정 만들기] — 한도가 찼으면 입력창 대신 안내 창(A · 막는다).</summary>
    private async Task OpenCreateAccountAsync(EmployeeListItemModel emp)
    {
        if (_seatApi is null || _linkApi is null) return;
        _seats = await _seatApi.GetSeatsAsync().ConfigureAwait(false);
        if (_seats is not null && _seats.Active >= _seats.Limit)
        {
            _seatNoticeBlocked = true;
            _showSeatNotice = true;
            StateHasChanged();
            return;
        }
        _accountTarget = emp;
        _accountModel = new CreateForEmployeeModel { EmployeeId = emp.EmployeeId };
        _accountShowPassword = false;
        _showAccountCreate = true;
        StateHasChanged();
    }

    private bool CanSubmitAccount =>
        _accountTarget is not null
        && !string.IsNullOrWhiteSpace(_accountModel.LoginId)
        && _accountModel.Password.Length >= 8;

    private void GenerateAccountPassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#$";
        var rand = new Random();
        _accountModel.Password = new string(Enumerable.Range(0, 12).Select(_ => chars[rand.Next(chars.Length)]).ToArray());
    }

    private static string AccountPasswordHint(string pw) =>
        string.IsNullOrEmpty(pw) ? "8자 이상 · 처음 로그인한 뒤 본인이 바꾸도록 알려 주세요"
        : pw.Length < 8 ? "8자 이상이어야 합니다"
        : "사용할 수 있습니다";

    private async Task SubmitCreateAccountAsync()
    {
        if (_accountTarget is null || _linkApi is null) return;
        _accountBusy = true;
        var name = _accountTarget.EmpName;
        var r = await _linkApi.CreateForEmployeeAsync(new CreateForEmployeeModel
        {
            EmployeeId = _accountTarget.EmployeeId,
            LoginId = _accountModel.LoginId.Trim(),
            Password = _accountModel.Password
        }).ConfigureAwait(false);
        _accountBusy = false;

        if (r.Ok)
        {
            _showAccountCreate = false;
            Snackbar.Add($"{name} 님 계정을 만들었습니다. 볼 수 있는 메뉴는 [권한 설정]에서 정합니다.", Severity.Success);
            await ReloadEmployeesAfterAccountAsync().ConfigureAwait(false);
        }
        else if (r.SeatFull)
        {
            // 서버가 막았다(다른 창에서 먼저 채움) — 같은 안내 창
            _showAccountCreate = false;
            _seats = _seatApi is null ? _seats : await _seatApi.GetSeatsAsync().ConfigureAwait(false);
            _seatNoticeBlocked = true;
            _showSeatNotice = true;
        }
        else
        {
            Snackbar.Add(r.Error ?? "계정을 만들지 못했습니다.", Severity.Error);
        }
        StateHasChanged();
    }

    /// <summary>안내 창의 [구독계정추가](3단계만 보인다) — 3단계 서버 주소로 숫자를 다시 읽고 「안내」 모양으로.</summary>
    private async Task OpenSubscribeFromEmployeeAsync()
    {
        if (_linkApi is null) return;
        var sub = await _linkApi.GetSeatSubscriptionAsync().ConfigureAwait(false);
        if (sub is null)
        {
            Snackbar.Add("구독 정보를 불러오지 못했습니다. 잠시 후 다시 해 주세요.", Severity.Warning);
            return;
        }
        _seats = new AccountSeatsModel { Active = sub.Active, BaseLimit = sub.BaseLimit, Extra = sub.Extra, Limit = sub.Limit, Tier = sub.Tier };
        _seatNoticeBlocked = false;
        _showSeatNotice = true;
        StateHasChanged();
    }

    /// <summary>계정 상태가 바뀌었으니 목록을 다시 받는다(퇴사자 포함 스위치 그대로).</summary>
    private async Task ReloadEmployeesAfterAccountAsync()
    {
        try
        {
            _employees = await EmployeeSvc.GetListAsync(_includeResigned).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // #15 — 만들기는 끝났다. 목록만 못 받았으니 알리고 둔다.
            LinkLog.LogWarning(ex, "계정 만든 뒤 사원 목록 재조회 실패");
            Snackbar.Add("계정은 만들었지만 목록을 새로 불러오지 못했습니다. 화면을 새로 고쳐 주세요.", Severity.Warning);
        }
    }
}
