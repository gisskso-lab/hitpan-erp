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
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] private IHttpClientFactory HttpFactory { get; set; } = default!;

    private bool _loading = true;
    private bool _saving;
    private UserInfoViewModel _model = new();
    private SubscriptionInfoViewModel _subscription = new();
    private InputFile? _logoInput;
    private InputFile? _sealInput;
    private InputFile? _headerInput;
    private DotNetObjectReference<UserInfoPage>? _dotNetRef;

    // 시리얼 인증 상태 (브라운킴 PM 2026-06-08, 사장님 결재)
    private string _serialKey = "";
    private string _serialMessage = "";
    private bool _serialSuccess;
    private bool _serialSubmitting;
    private bool _serialVerified;
    private bool _serialLocked;
    private string? _serialVerifiedAt;
    private string _lastVerifiedLicenseKey = "";  // 기기 등록 시 재사용 (메모리만, 저장 X)

    // 기기 등록 상태 (사장님 결재 2026-06-08 - 네이버·넷플릭스 방식)
    private bool _deviceRegistered;
    private bool _deviceDialogShown;
    private bool _deviceSubmitting;
    private bool _deviceSuccess;
    private string _deviceMessage = "";
    private int _deviceCount;
    private int _deviceLimit;

    private async Task VerifySerialAsync()
    {
        if (string.IsNullOrWhiteSpace(_serialKey)) return;
        _serialSubmitting = true;
        _serialMessage = "";
        try
        {
            // 백오피스 API 호출 (헌법 #35 — 본사 백오피스가 시리얼 발급·검증 권한)
            var http = HttpFactory.CreateClient("BackofficeApi");
            var fingerprint = await JSRuntime.InvokeAsync<string>("eval",
                "(navigator.userAgent + '|' + screen.width + 'x' + screen.height + '|' + Intl.DateTimeFormat().resolvedOptions().timeZone)");

            var resp = await http.PostAsJsonAsync("api/landing/serial/verify",
                new { licenseKey = _serialKey.Trim(), clientFingerprint = fingerprint });

            var result = await resp.Content.ReadFromJsonAsync<VerifyResp>();
            if (resp.IsSuccessStatusCode && result?.Success == true)
            {
                _serialVerified = true;
                _serialSuccess = true;
                _serialMessage = result.Message ?? "시리얼 인증 완료";
                _serialVerifiedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                _lastVerifiedLicenseKey = _serialKey.Trim();  // 기기 등록 시 재사용 (메모리만)
                _serialKey = "";

                // 길 B (사장님 결재 2026-06-18, 헌법 #22) — 백오피스는 사업자번호·대표자명·주소 평문을
                //   보유·전달하지 않는다. 따라서 시리얼 인증 응답으로 자동입력 가능한 건 회사명·연락처·이메일뿐.
                //   사업자번호·대표자명·주소는 ERP 첫 설치 화면(/setup/license)에서 사용자가 입력해 로컬에만 저장.
                if (result.CompanyInfo is not null)
                {
                    if (!string.IsNullOrWhiteSpace(result.CompanyInfo.CompanyName))
                        _model.CompanyName = result.CompanyInfo.CompanyName;
                    if (!string.IsNullOrWhiteSpace(result.CompanyInfo.Tel))
                        _model.Phone = result.CompanyInfo.Tel;
                    if (!string.IsNullOrWhiteSpace(result.CompanyInfo.Email))
                        _model.Email = result.CompanyInfo.Email;
                }

                Snackbar.Add("시리얼 인증 완료 — 회사 정보가 자동 입력되었습니다", Severity.Success);
            }
            else if ((int)resp.StatusCode == 423 || result?.Locked == true)
            {
                _serialLocked = true;
                _serialMessage = result?.Message ?? "5회 실패로 사용이 중지되었습니다.";
                Snackbar.Add("시리얼 5회 실패 — 사용 중지", Severity.Error);
            }
            else
            {
                _serialSuccess = false;
                _serialMessage = result?.Message ?? "올바르지 않은 시리얼입니다.";
                Snackbar.Add(_serialMessage, Severity.Warning);
            }
        }
        catch (Exception ex)
        {
            _serialMessage = $"검증 처리 중 오류: {ex.Message}";
            Snackbar.Add(_serialMessage, Severity.Error);
        }
        finally
        {
            _serialSubmitting = false;
            StateHasChanged();
        }
    }

    private class VerifyResp
    {
        public bool Success { get; set; }
        public bool Locked { get; set; }
        public int? RemainingAttempts { get; set; }
        public string? Message { get; set; }
        public string? TenantId { get; set; }
        public string? TenantCode { get; set; }
        public CompanyInfoDto? CompanyInfo { get; set; }
    }

    // 길 B (사장님 결재 2026-06-18, 헌법 #22): SerialVerify 응답 DTO에서 BizNo·CeoName·Address 제거.
    //   백오피스는 이 평문을 보내지 않는다. 회사명·연락처·이메일만 자동입력에 사용.
    private class CompanyInfoDto
    {
        public string? TenantCode { get; set; }
        public string? CompanyName { get; set; }
        public string? Tel { get; set; }
        public string? Status { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? PlanType { get; set; }
    }

    // 기기 등록 (사장님 결재 2026-06-08 - 네이버·넷플릭스 방식)
    // 흐름: 시리얼 인증 통과 → "이 PC 등록?" Y → 본사에서 device_token 발급
    //       → 로컬 저장소(Browser localStorage, 추후 DPAPI)에 저장
    private async Task RegisterDeviceAsync()
    {
        if (string.IsNullOrWhiteSpace(_lastVerifiedLicenseKey))
        {
            _deviceMessage = "시리얼 인증을 먼저 완료해주세요.";
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
            }

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
            Snackbar.Add($"사용자정보를 불러오지 못했습니다: {ex.Message}", Severity.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private Task OpenLogoUpload()
    {
        _ = _logoInput;
        Snackbar.Add("로고 파일을 선택하세요.", Severity.Info);
        return Task.CompletedTask;
    }

    private Task OpenSealUpload()
    {
        _ = _sealInput;
        Snackbar.Add("인장 파일을 선택하세요.", Severity.Info);
        return Task.CompletedTask;
    }

    private Task OpenHeaderUpload()
    {
        _ = _headerInput;
        Snackbar.Add("헤더 파일을 선택하세요.", Severity.Info);
        return Task.CompletedTask;
    }

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

    private Task OpenUserListDialog()
    {
        Snackbar.Add("사용자 목록 조회는 권한설정 페이지의 직원 목록을 사용합니다.", Severity.Info);
        return Task.CompletedTask;
    }

    private async Task ApplyAsync()
    {
        _saving = true;
        var current = await SettingsSvc.GetAsync().ConfigureAwait(false) ?? new TenantSettingsModel();
        current.IndustryType = _model.BusinessType;
        var okSettings = await SettingsSvc.SaveAsync(current).ConfigureAwait(false);

        // tenants 사업장 컬럼과 동일한 필드만 API로 전달한다(비고·이미지 미리보기 등은 별도 저장 대상 아님).
        var company = new TenantCompanyModel
        {
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

/// <summary>
/// 구독정보를 표시하기 위한 읽기 전용 모델이다.
/// </summary>
public sealed class SubscriptionInfoViewModel
{
    public string PlanName { get; set; } = string.Empty;
    public DateTime ExpireDate { get; set; }
    public int UsedLicense { get; set; }
    public int TotalLicense { get; set; }
}
