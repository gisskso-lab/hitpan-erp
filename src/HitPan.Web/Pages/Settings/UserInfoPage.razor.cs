using System.Net.Http.Json;
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
    [Inject] private SettingsService SettingsSvc { get; set; } = default!;
    [Inject] private BillingService BillingSvc { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] private IHttpClientFactory HttpFactory { get; set; } = default!;

    private bool _loading = true;
    private bool _saving;
    private UserInfoViewModel _model = new();

    // 구독정보는 불러오지 못할 수 있다. 못 불러온 상태(null)와 "0원 요금제"를 구분해야
    // 화면이 거짓 숫자를 지어내지 않는다(사장님 지적 2026-08-09).
    private SubscriptionModel? _subscription;

    private DotNetObjectReference<UserInfoPage>? _dotNetRef;

    // 기기 등록 상태 (사장님 결재 2026-06-08 - 네이버·넷플릭스 방식)
    private bool _deviceRegistered;
    private bool _deviceDialogShown;
    private bool _deviceSubmitting;
    private bool _deviceSuccess;
    private string _deviceMessage = "";
    private int _deviceCount;
    private int _deviceLimit;

    // 🔴 시리얼 인증 제거 (사장님 지적 2026-08-09 — "설치시 진행하기에 사실상 필요 없음").
    //   시리얼 확인은 설치마법사가 맡으므로 이 화면의 입력란·검증 호출을 걷어냈다.
    //
    //   ⚠️ 기기 등록은 지우지 않고 남긴다(헌법 #1 — 확신 없으면 지우지 않는다).
    //      종전 구조는 "시리얼 인증 통과 → 그때 받은 키로 기기 등록" 이었다. 시리얼 입력이
    //      사라지면서 이 화면에서 그 키를 얻을 방법이 없어졌고, 아래 값은 항상 비어 있다.
    //      그 결과 등록 버튼은 안내만 내보내고 실제 등록까지 가지 않는다.
    //      기기 등록을 어디서 할지(설치마법사·로그인 화면 등)는 별도 결재 사항이므로,
    //      되살릴 근거가 사라지지 않도록 등록 본체는 그대로 보존한다.
    private string _lastVerifiedLicenseKey = "";

    private async Task RegisterDeviceAsync()
    {
        if (string.IsNullOrWhiteSpace(_lastVerifiedLicenseKey))
        {
            // 이 화면에는 더 이상 시리얼 입력이 없어 지금은 항상 이 갈래로 온다.
            _deviceSuccess = false;
            _deviceMessage = "이 컴퓨터 등록은 설치 과정에서 함께 진행됩니다. 등록이 필요하면 고객지원으로 문의해주세요.";
            return;
        }

        _deviceSubmitting = true;
        _deviceMessage = "";
        try
        {
            var http = HttpFactory.CreateClient("BackofficeApi");
            var fingerprint = await JSRuntime.InvokeAsync<string>("eval",
                "(navigator.userAgent + '|' + screen.width + 'x' + screen.height + '|' + Intl.DateTimeFormat().resolvedOptions().timeZone)");
            var userAgent = await JSRuntime.InvokeAsync<string>("eval", "navigator.userAgent");
            var osInfo = await JSRuntime.InvokeAsync<string>("eval", "navigator.platform || navigator.userAgentData?.platform || ''");

            var resp = await http.PostAsJsonAsync("api/landing/device/register", new
            {
                licenseKey = _lastVerifiedLicenseKey,
                fingerprint,
                deviceType = "pc",
                deviceName = $"PC ({DateTime.Now:MMdd-HHmm})",
                userAgent,
                osInfo
            });
            var result = await resp.Content.ReadFromJsonAsync<DeviceRegisterResp>();

            if (resp.IsSuccessStatusCode && result?.Success == true && !string.IsNullOrEmpty(result.DeviceToken))
            {
                // device_token 로컬 저장 (브라우저 localStorage - 추후 DPAPI 대체)
                await JSRuntime.InvokeVoidAsync("localStorage.setItem", "hitpan_device_token", result.DeviceToken);
                await JSRuntime.InvokeVoidAsync("localStorage.setItem", "hitpan_device_id", result.DeviceId ?? "");

                _deviceRegistered = true;
                _deviceSuccess = true;
                _deviceCount = result.CurrentCount;
                _deviceLimit = result.DeviceLimit;
                _deviceMessage = "기기 등록 완료 - 인증서가 보안 저장소에 저장되었습니다.";
                _lastVerifiedLicenseKey = "";  // 메모리에서 즉시 제거
                Snackbar.Add($"기기 등록 완료 ({_deviceCount}/{_deviceLimit} 대)", Severity.Success);
            }
            else if (result?.AlreadyRegistered == true)
            {
                _deviceRegistered = true;
                _deviceMessage = "이미 등록된 기기입니다.";
            }
            else if (result?.LimitExceeded == true)
            {
                _deviceDialogShown = true;
                _deviceSuccess = false;
                _deviceCount = result.CurrentCount;
                _deviceLimit = result.DeviceLimit;
                _deviceMessage = result.Message ?? "기기 한도 초과";
                Snackbar.Add(_deviceMessage, Severity.Warning);
            }
            else
            {
                _deviceSuccess = false;
                _deviceMessage = result?.Message ?? "기기 등록 실패";
                Snackbar.Add(_deviceMessage, Severity.Error);
            }
        }
        catch (Exception ex)
        {
            _deviceMessage = $"등록 처리 중 오류: {ex.Message}";
            Snackbar.Add(_deviceMessage, Severity.Error);
        }
        finally
        {
            _deviceSubmitting = false;
            StateHasChanged();
        }
    }

    private class DeviceRegisterResp
    {
        public bool Success { get; set; }
        public bool AlreadyRegistered { get; set; }
        public bool LimitExceeded { get; set; }
        public string? Message { get; set; }
        public string? DeviceId { get; set; }
        public string? DeviceToken { get; set; }
        public int CurrentCount { get; set; }
        public int DeviceLimit { get; set; }
    }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var current = await SettingsSvc.GetAsync().ConfigureAwait(false);
            if (current is not null)
            {
                _model.BusinessType = current.IndustryType;
            }

            // tenants 테이블에서 사업장 정보를 불러와 화면 모델에 반영한다.
            var company = await SettingsSvc.GetCompanyAsync().ConfigureAwait(false);
            if (company is not null)
            {
                _model.CompanyName = company.CompanyName;
                _model.CeoName = company.CeoName;
                _model.BusinessNo = company.BizNo;
                _model.BusinessType = company.BizType ?? string.Empty;
                _model.BusinessCategory = company.BizItem ?? string.Empty;
                _model.Phone = company.Tel ?? string.Empty;
                _model.Fax = company.Fax ?? string.Empty;
                _model.Email = company.Email ?? string.Empty;
                _model.Homepage = company.Homepage ?? string.Empty;
                _model.ZipCode = company.ZipCode ?? string.Empty;
                _model.Address = company.Address ?? string.Empty;
                _model.AddressDetail = company.AddressDetail ?? string.Empty;
                _model.CorporateNo = company.CorpNo ?? string.Empty;
                _model.BranchNo = company.SubsidiaryNo ?? string.Empty;
                _model.IsLockedFromLanding = company.IsLockedFromLanding;

                // 저장해 둔 출력 이미지를 미리보기에 되살린다. 종전에는 이 세 줄이 없어
                // 저장 여부와 무관하게 화면을 다시 열면 그림이 늘 비어 보였다.
                _model.LogoPreviewUrl = company.LogoUrl;
                _model.SealPreviewUrl = company.SealUrl;
                _model.HeaderPreviewUrl = company.HeaderUrl;
            }

            // 실제 구독 정보를 불러온다. 종전에는 "Business / 7·10" 이 코드에 적혀 있어
            // 모든 회사에 같은 거짓 정보가 보였다(사장님 지적 2026-08-09).
            // 못 불러오면 null 로 두고 화면이 "확인할 수 없습니다" 를 띄운다.
            _subscription = await BillingSvc.GetSubscriptionAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"사용자정보를 불러오지 못했습니다: {ex.Message}", Severity.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    // 로고·인장·헤더를 여는 별도 버튼은 제거했다(사장님 지적 2026-08-09 — "등록 안됨").
    // 그 버튼들은 파일선택창을 여는 코드가 없어 눌러도 아무 일이 없었다.
    // 이제 화면의 label 이 파일선택창을 직접 연다(.razor 의 hitpan-file-pick 참고).

    private async Task OpenZipcodeSearch()
    {
        _dotNetRef?.Dispose();
        _dotNetRef = DotNetObjectReference.Create(this);
        await JSRuntime.InvokeVoidAsync("openDaumPostcode", _dotNetRef).ConfigureAwait(false);
    }

    [JSInvokable]
    public async Task OnAddressSelected(string zonecode, string fullAddress)
    {
        _model.ZipCode = zonecode ?? string.Empty;
        _model.Address = fullAddress ?? string.Empty;
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnLogoChanged(InputFileChangeEventArgs e)
    {
        var preview = await BuildPreviewAsync(e.File, allowSvg: true).ConfigureAwait(false);
        if (preview is null) return;
        _model.LogoPreviewUrl = preview;
        Snackbar.Add("로고가 등록되었습니다.", Severity.Success);
    }

    private async Task OnSealChanged(InputFileChangeEventArgs e)
    {
        var preview = await BuildPreviewAsync(e.File, allowSvg: false).ConfigureAwait(false);
        if (preview is null) return;
        _model.SealPreviewUrl = preview;
        Snackbar.Add("인장이 등록되었습니다.", Severity.Success);
    }

    private async Task OnHeaderChanged(InputFileChangeEventArgs e)
    {
        var preview = await BuildPreviewAsync(e.File, allowSvg: false).ConfigureAwait(false);
        if (preview is null) return;
        _model.HeaderPreviewUrl = preview;
        Snackbar.Add("헤더 이미지가 등록되었습니다.", Severity.Success);
    }

    private async Task<string?> BuildPreviewAsync(IBrowserFile? file, bool allowSvg)
    {
        if (file is null)
        {
            Snackbar.Add("파일을 선택해주세요.", Severity.Warning);
            return null;
        }
        const long maxSize = 2 * 1024 * 1024;
        if (file.Size > maxSize)
        {
            Snackbar.Add("파일 크기는 2MB 이하여야 합니다.", Severity.Warning);
            return null;
        }
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

    // 사용자추가·관리 진입은 제거했다(사장님 지적 2026-08-09 — "기능중복으로 사실상 필요없음").
    // 직원 추가·수정은 사원관리 화면이 전담하며, 여기 있던 버튼은 안내 문구만 띄우고
    // 실제로 아무 화면도 열지 못했다.

    private async Task ApplyAsync()
    {
        _saving = true;
        var current = await SettingsSvc.GetAsync().ConfigureAwait(false) ?? new TenantSettingsModel();
        current.IndustryType = _model.BusinessType;
        var okSettings = await SettingsSvc.SaveAsync(current).ConfigureAwait(false);

        // local_company 사업장 컬럼과 동일한 필드를 API로 전달한다(비고는 저장 대상 아님).
        var company = new TenantCompanyModel
        {
            // 출력 이미지 경로. 미리보기가 data: 로 시작하는 그림 데이터일 때는 보내지 않는다.
            // 저장 컬럼이 200자라 그림 데이터를 넣으면 앞 200자만 잘려 들어가 깨진 값이 남는다.
            // 파일을 보관할 자리가 생기기 전까지는 이미 저장돼 있던 경로만 지켜준다.
            LogoUrl = KeepPathOnly(_model.LogoPreviewUrl),
            SealUrl = KeepPathOnly(_model.SealPreviewUrl),
            HeaderUrl = KeepPathOnly(_model.HeaderPreviewUrl),
            CompanyName = _model.CompanyName,
            CeoName = _model.CeoName,
            BizNo = _model.BusinessNo,
            BizType = _model.BusinessType,
            BizItem = _model.BusinessCategory,
            Tel = _model.Phone,
            Fax = _model.Fax,
            Email = _model.Email,
            Homepage = _model.Homepage,
            ZipCode = _model.ZipCode,
            Address = _model.Address,
            AddressDetail = _model.AddressDetail,
            CorpNo = _model.CorporateNo,
            SubsidiaryNo = _model.BranchNo
        };
        var okCompany = await SettingsSvc.SaveCompanyAsync(company).ConfigureAwait(false);

        _saving = false;
        if (okSettings && okCompany)
        {
            Snackbar.Add("사용자정보설정이 적용되었습니다.", Severity.Success);
        }
        else
        {
            Snackbar.Add("적용 실패. 다시 시도해주세요.", Severity.Error);
        }
    }

    /// <summary>
    /// 저장해도 되는 이미지 경로만 남기고, 화면에서 갓 고른 그림 데이터(data:)는 버린다.
    /// </summary>
    /// <remarks>
    /// 저장 컬럼(varchar 200자)에는 그림 자체가 들어갈 수 없다. 그림 데이터를 그대로 보내면
    /// 앞 200자만 잘려 저장돼 다음에 열 때 깨진 그림이 된다. 그럴 바에는 저장하지 않는다.
    /// 파일을 보관할 자리가 마련되면 그때 올린 파일의 경로를 넣는다.
    /// </remarks>
    private static string? KeepPathOnly(string? previewUrl)
    {
        if (string.IsNullOrWhiteSpace(previewUrl))
        {
            return null;
        }

        return previewUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            ? null
            : previewUrl;
    }

    private void Close()
    {
        Navigation.NavigateTo("/settings");
    }

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
    public string CompanyName { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string AddressDetail { get; set; } = string.Empty;
    public string BusinessNo { get; set; } = string.Empty;
    public string BranchNo { get; set; } = string.Empty;
    public string CorporateNo { get; set; } = string.Empty;
    public string BusinessType { get; set; } = string.Empty;
    public string BusinessCategory { get; set; } = string.Empty;
    public string CeoName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Fax { get; set; } = string.Empty;
    public string Homepage { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string? LogoPreviewUrl { get; set; }
    public string? SealPreviewUrl { get; set; }
    public string? HeaderPreviewUrl { get; set; }

    // 헌법 #35 (사장님 결재 2026-06-04) — 랜딩 가입 자동 반영 잠금 플래그
    // 1이면 회사명·사업자번호·대표자명 핵심 3필드 변경 불가
    public bool IsLockedFromLanding { get; set; }
}

// 구독정보 표시용 모델(SubscriptionInfoViewModel)은 제거했다.
// 이 화면 전용으로 따로 두다 보니 실제 구독 API 와 이어지지 않은 채 코드에 적힌 값만 보여줬다.
// 이제 구독결제 화면과 같은 SubscriptionModel(HitPan.Web.Models)을 함께 쓴다.
