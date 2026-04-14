using HitPan.Web.Models;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using MudBlazor;

namespace HitPan.Web.Pages.SettingsUi;

/// <summary>
/// 사용자정보설정 페이지의 상태와 이벤트를 관리한다.
/// </summary>
public partial class UserInfoPage : ComponentBase, IDisposable
{
    // 설정 조회/저장 API 서비스
    [Inject]
    private SettingsService SettingsSvc { get; set; } = default!;

    // 스낵바 알림 서비스
    [Inject]
    private ISnackbar Snackbar { get; set; } = default!;

    // 네비게이션 서비스
    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    // JavaScript 상호운용 서비스
    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    // 초기 로딩 상태
    private bool _loading = true;

    // 저장 진행 상태
    private bool _saving;

    // 사용자정보설정 화면 모델
    private UserInfoViewModel _model = new();

    // 구독정보 읽기 전용 모델
    private SubscriptionInfoViewModel _subscription = new();

    // 로고 파일 입력 참조
    private InputFile? _logoInput;

    // 인장 파일 입력 참조
    private InputFile? _sealInput;

    // 헤더 파일 입력 참조
    private InputFile? _headerInput;

    // JS에서 콜백을 받기 위한 .NET 객체 참조
    private DotNetObjectReference<UserInfoPage>? _dotNetRef;

    /// <summary>
    /// 초기 진입 시 기존 설정을 불러오고 화면 모델을 구성한다.
    /// </summary>
    /// <returns>초기화 작업</returns>
    protected override async Task OnInitializedAsync()
    {
        try
        {
            // 기존 SettingsService 모델에서 매핑 가능한 값만 기본 반영한다.
            var current = await SettingsSvc.GetAsync().ConfigureAwait(false);
            if (current is not null)
            {
                _model.BusinessType = current.IndustryType;
            }

            // 추후 API 연동 필요: 구독정보 전용 API가 생기면 서버 값을 사용한다.
            _subscription = new SubscriptionInfoViewModel
            {
                PlanName = "Business",
                ExpireDate = DateTime.Today.AddMonths(1),
                UsedLicense = 7,
                TotalLicense = 10
            };
        }
        catch (Exception ex)
        {
            // 초기 데이터 로드 실패 시에도 로딩을 해제해 무한 로딩을 방지한다.
            Snackbar.Add($"사용자정보를 불러오지 못했습니다: {ex.Message}", Severity.Error);
        }
        finally
        {
            // 성공/실패와 무관하게 로딩 상태를 종료한다.
            _loading = false;
        }
    }

    /// <summary>
    /// 로고 파일 선택 대화상자를 연다.
    /// </summary>
    /// <returns>완료된 작업</returns>
    private Task OpenLogoUpload()
    {
        // 브라우저 기본 파일 선택은 InputFile 클릭으로 유도한다.
        _ = _logoInput;
        Snackbar.Add("로고 파일을 선택하세요.", Severity.Info);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 인장 파일 선택 대화상자를 연다.
    /// </summary>
    /// <returns>완료된 작업</returns>
    private Task OpenSealUpload()
    {
        // 브라우저 기본 파일 선택은 InputFile 클릭으로 유도한다.
        _ = _sealInput;
        Snackbar.Add("인장 파일을 선택하세요.", Severity.Info);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 헤더 파일 선택 대화상자를 연다.
    /// </summary>
    /// <returns>완료된 작업</returns>
    private Task OpenHeaderUpload()
    {
        // 브라우저 기본 파일 선택은 InputFile 클릭으로 유도한다.
        _ = _headerInput;
        Snackbar.Add("헤더 파일을 선택하세요.", Severity.Info);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 카카오 우편번호 찾기 팝업을 연다.
    /// </summary>
    /// <returns>팝업 호출 작업</returns>
    private async Task OpenZipcodeSearch()
    {
        // JS 콜백용 객체 참조를 새로 생성해 우편번호 선택 결과를 수신한다.
        _dotNetRef?.Dispose();
        _dotNetRef = DotNetObjectReference.Create(this);
        await JSRuntime.InvokeVoidAsync("openDaumPostcode", _dotNetRef).ConfigureAwait(false);
    }

    /// <summary>
    /// 카카오 우편번호 API에서 전달한 주소 선택 결과를 반영한다.
    /// </summary>
    /// <param name="zonecode">선택한 우편번호</param>
    /// <param name="fullAddress">선택한 기본 주소</param>
    /// <returns>UI 갱신 작업</returns>
    [JSInvokable]
    public async Task OnAddressSelected(string zonecode, string fullAddress)
    {
        // 우편번호와 기본주소를 자동 입력해 사용자가 상세주소만 추가 입력하도록 한다.
        _model.ZipCode = zonecode ?? string.Empty;
        _model.Address = fullAddress ?? string.Empty;
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 로고 파일 변경 이벤트를 처리한다.
    /// </summary>
    /// <param name="e">파일 변경 이벤트</param>
    /// <returns>비동기 처리</returns>
    private async Task OnLogoChanged(InputFileChangeEventArgs e)
    {
        // 로고는 PNG/JPG/SVG, 최대 2MB를 허용한다.
        var preview = await BuildPreviewAsync(e.File, allowSvg: true).ConfigureAwait(false);
        if (preview is null)
        {
            return;
        }

        _model.LogoPreviewUrl = preview;
        // 출력 시 문서 헤더에 자동 삽입
        Snackbar.Add("로고가 등록되었습니다.", Severity.Success);
    }

    /// <summary>
    /// 인장 파일 변경 이벤트를 처리한다.
    /// </summary>
    /// <param name="e">파일 변경 이벤트</param>
    /// <returns>비동기 처리</returns>
    private async Task OnSealChanged(InputFileChangeEventArgs e)
    {
        // 인장은 PNG/JPG, 최대 2MB를 허용한다.
        var preview = await BuildPreviewAsync(e.File, allowSvg: false).ConfigureAwait(false);
        if (preview is null)
        {
            return;
        }

        _model.SealPreviewUrl = preview;
        // 출력 시 공급자란에 자동 삽입
        Snackbar.Add("인장이 등록되었습니다.", Severity.Success);
    }

    /// <summary>
    /// 헤더 파일 변경 이벤트를 처리한다.
    /// </summary>
    /// <param name="e">파일 변경 이벤트</param>
    /// <returns>비동기 처리</returns>
    private async Task OnHeaderChanged(InputFileChangeEventArgs e)
    {
        // 헤더는 PNG/JPG, 최대 2MB를 허용한다.
        var preview = await BuildPreviewAsync(e.File, allowSvg: false).ConfigureAwait(false);
        if (preview is null)
        {
            return;
        }

        _model.HeaderPreviewUrl = preview;
        // 출력 시 문서 상단 헤더에 자동 삽입
        Snackbar.Add("헤더 이미지가 등록되었습니다.", Severity.Success);
    }

    /// <summary>
    /// 파일을 검증하고 미리보기용 Data URL 문자열로 변환한다.
    /// </summary>
    /// <param name="file">선택 파일</param>
    /// <param name="allowSvg">SVG 허용 여부</param>
    /// <returns>미리보기 URL 또는 null</returns>
    private async Task<string?> BuildPreviewAsync(IBrowserFile? file, bool allowSvg)
    {
        // 파일이 없으면 추가 처리를 하지 않는다.
        if (file is null)
        {
            Snackbar.Add("파일을 선택해주세요.", Severity.Warning);
            return null;
        }

        // 업로드 상한은 2MB로 고정한다.
        const long maxSize = 2 * 1024 * 1024;
        if (file.Size > maxSize)
        {
            Snackbar.Add("파일 크기는 2MB 이하여야 합니다.", Severity.Warning);
            return null;
        }

        // 허용 확장자만 통과시켜 악성 업로드 위험을 줄인다.
        var ext = Path.GetExtension(file.Name).ToLowerInvariant();
        var allowed = allowSvg
            ? ext is ".png" or ".jpg" or ".jpeg" or ".svg"
            : ext is ".png" or ".jpg" or ".jpeg";
        if (!allowed)
        {
            Snackbar.Add("허용되지 않는 파일 형식입니다.", Severity.Warning);
            return null;
        }

        await using var stream = file.OpenReadStream(maxAllowedSize: maxSize);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms).ConfigureAwait(false);
        var base64 = Convert.ToBase64String(ms.ToArray());
        var mime = file.ContentType;
        return $"data:{mime};base64,{base64}";
    }

    /// <summary>
    /// 사용자추가·관리 다이얼로그를 연다.
    /// </summary>
    /// <returns>완료된 작업</returns>
    private Task OpenUserListDialog()
    {
        // 추후 API 연동 필요: 현재 테넌트 소속 직원 상세 목록 다이얼로그로 대체한다.
        Snackbar.Add("사용자 목록 조회는 권한설정 페이지의 직원 목록을 사용합니다.", Severity.Info);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 사용자정보설정을 적용한다.
    /// </summary>
    /// <returns>저장 작업</returns>
    private async Task ApplyAsync()
    {
        _saving = true;

        // 기존 SettingsService에 매핑 가능한 최소 항목만 반영한다.
        var current = await SettingsSvc.GetAsync().ConfigureAwait(false) ?? new TenantSettingsModel();
        current.IndustryType = _model.BusinessType;

        // 추후 API 연동 필요: 사업장 상세 필드/이미지 저장용 전용 API가 필요하다.
        var ok = await SettingsSvc.SaveAsync(current).ConfigureAwait(false);
        _saving = false;

        if (ok)
        {
            Snackbar.Add("사용자정보설정이 적용되었습니다.", Severity.Success);
        }
        else
        {
            Snackbar.Add("적용 실패. 다시 시도해주세요.", Severity.Error);
        }
    }

    /// <summary>
    /// 화면을 닫고 설정 메인으로 이동한다.
    /// </summary>
    private void Close()
    {
        Navigation.NavigateTo("/settings");
    }

    /// <summary>
    /// JS 상호운용 리소스를 해제한다.
    /// </summary>
    public void Dispose()
    {
        _dotNetRef?.Dispose();
    }
}

/// <summary>
/// 사용자정보설정 화면에서 편집하는 뷰 모델이다.
/// </summary>
public sealed class UserInfoViewModel
{
    // 사용업체명
    public string CompanyName { get; set; } = string.Empty;

    // 우편번호
    public string ZipCode { get; set; } = string.Empty;

    // 주소
    public string Address { get; set; } = string.Empty;

    // 상세주소
    public string AddressDetail { get; set; } = string.Empty;

    // 사업자번호
    public string BusinessNo { get; set; } = string.Empty;

    // 종사업장번호
    public string BranchNo { get; set; } = string.Empty;

    // 법인등록번호
    public string CorporateNo { get; set; } = string.Empty;

    // 업태
    public string BusinessType { get; set; } = string.Empty;

    // 업종
    public string BusinessCategory { get; set; } = string.Empty;

    // 대표자명
    public string CeoName { get; set; } = string.Empty;

    // 전화번호
    public string Phone { get; set; } = string.Empty;

    // 팩스번호
    public string Fax { get; set; } = string.Empty;

    // 홈페이지
    public string Homepage { get; set; } = string.Empty;

    // 이메일
    public string Email { get; set; } = string.Empty;

    // 비고
    public string Note { get; set; } = string.Empty;

    // 로고 미리보기 URL
    public string? LogoPreviewUrl { get; set; }

    // 인장 미리보기 URL
    public string? SealPreviewUrl { get; set; }

    // 헤더 미리보기 URL
    public string? HeaderPreviewUrl { get; set; }
}

/// <summary>
/// 구독정보를 표시하기 위한 읽기 전용 모델이다.
/// </summary>
public sealed class SubscriptionInfoViewModel
{
    // 플랜명
    public string PlanName { get; set; } = string.Empty;

    // 만료일
    public DateTime ExpireDate { get; set; }

    // 사용 라이선스 수
    public int UsedLicense { get; set; }

    // 전체 라이선스 수
    public int TotalLicense { get; set; }
}
