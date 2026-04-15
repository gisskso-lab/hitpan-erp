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

    private bool _loading = true;
    private bool _saving;
    private UserInfoViewModel _model = new();
    private SubscriptionInfoViewModel _subscription = new();
    private InputFile? _logoInput;
    private InputFile? _sealInput;
    private InputFile? _headerInput;
    private DotNetObjectReference<UserInfoPage>? _dotNetRef;

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
