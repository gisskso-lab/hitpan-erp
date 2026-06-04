using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.API.Controllers;

// ERP 첫 설치 자동 반영 (사장님 결재 2026-06-04, 헌법 #35)
//
// 흐름:
//   1) ERP Web /setup/license에서 사용자 입력 받음 (라이선스 키 + 사업자번호 + 기타 회사정보)
//   2) ERP Web → 백오피스 API /api/landing/license/claim 검증 통과 후
//   3) ERP Web → 본 API POST /api/setup/bootstrap 호출
//   4) 본 API: 라이선스 해시로 tenants 조회 + 회사정보 박제 + is_locked_from_landing=1 + bootstrap_at
//   5) 이후 회사정보 수정은 CompanyService에서 잠금 검사 (별도 가드)
//
// 헌법 정합:
//   #15 — 빈 catch 금지
//   #18·#22 — 평문 사업자번호는 ERP 로컬 DB(고객사 PC)에만 박제. 본사는 해시만
//   #20 — 가입 → 결제 → 라이선스 → 설치 → 자동 반영 끊김 0
//   #35 — 부모계정 부여(백오피스) + 자식계정 ERP 내 관리 + 회사정보 자동 반영 + 수정 금지
[ApiController]
[Route("api/setup")]
[AllowAnonymous]
public class CompanyBootstrapController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<CompanyBootstrapController> _logger;

    public CompanyBootstrapController(IConfiguration config, ILogger<CompanyBootstrapController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpPost("bootstrap")]
    public async Task<IActionResult> Bootstrap([FromBody] BootstrapRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.LicenseKey) || string.IsNullOrWhiteSpace(req.BizNo))
            return BadRequest(new { success = false, message = "라이선스 키와 사업자번호가 필요합니다." });

        var bizNoNormalized = req.BizNo.Replace("-", "").Replace(" ", "").Trim();
        if (bizNoNormalized.Length != 10 || !bizNoNormalized.All(char.IsDigit))
            return BadRequest(new { success = false, message = "사업자번호 형식 오류 (10자리 숫자)" });

        var licensePepper = _config["License:Pepper"] ?? "dev-pepper-2026";
        var licenseHash = ComputeHmacSha256(req.LicenseKey.Trim(), licensePepper);

        try
        {
            var cs = _config.GetConnectionString("DefaultConnection")
                     ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection 미설정");
            await using var db = new MySqlConnection(cs);
            await db.OpenAsync(ct);

            var tenant = await db.QueryFirstOrDefaultAsync<TenantRow>(@"
                SELECT CAST(tenant_id AS CHAR) AS TenantId,
                       tenant_code AS TenantCode,
                       company_name AS CompanyName,
                       status AS Status,
                       is_locked_from_landing AS IsLocked
                FROM tenants
                WHERE license_key_hash = @Hash AND status = 'active'
                LIMIT 1",
                new { Hash = licenseHash });

            if (tenant is null)
                return BadRequest(new { success = false, message = "라이선스가 유효하지 않거나 활성 상태가 아닙니다." });

            if (tenant.IsLocked == 1)
                return BadRequest(new { success = false, message = "이미 설치가 완료된 라이선스입니다. 회사정보 변경은 랜딩에서 사업자등록증 재등록이 필요합니다." });

            // tenants 박제 — 사업자번호 + 대표·연락처·이메일·주소·업태·종목·법인번호·우편번호
            await db.ExecuteAsync(@"
                UPDATE tenants SET
                    biz_no = @BizNo,
                    ceo_name = @CeoName,
                    tel = @Tel,
                    address = @Address,
                    email = @Email,
                    biz_type = @BizType,
                    biz_item = @BizItem,
                    zip_code = @ZipCode,
                    corp_no = @CorpNo,
                    is_locked_from_landing = 1,
                    bootstrap_at = UTC_TIMESTAMP(),
                    updated_at = UTC_TIMESTAMP()
                WHERE tenant_id = @TenantId",
                new
                {
                    tenant.TenantId,
                    BizNo = bizNoNormalized,
                    CeoName = req.CeoName ?? "",
                    Tel = req.Tel,
                    Address = req.Address,
                    Email = req.Email,
                    BizType = req.BizType,
                    BizItem = req.BizItem,
                    ZipCode = req.ZipCode,
                    CorpNo = req.CorpNo
                });

            _logger.LogInformation("[CompanyBootstrap] 자동 반영 완료 tenant={Code} biz_no={Mask}",
                tenant.TenantCode, MaskBizNo(bizNoNormalized));

            return Ok(new
            {
                success = true,
                message = "회사 정보가 ERP에 자동 반영되었습니다.",
                tenantCode = tenant.TenantCode,
                companyName = tenant.CompanyName
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CompanyBootstrap] 자동 반영 실패");
            return StatusCode(500, new { success = false, message = "자동 반영 중 서버 오류가 발생했습니다." });
        }
    }

    private static string ComputeHmacSha256(string data, string key)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string MaskBizNo(string bn) =>
        bn.Length >= 6 ? bn.Substring(0, 3) + "**" + bn.Substring(5) : "***";

    public class BootstrapRequest
    {
        public string LicenseKey { get; set; } = "";
        public string BizNo { get; set; } = "";
        public string? CeoName { get; set; }
        public string? Tel { get; set; }
        public string? Address { get; set; }
        public string? Email { get; set; }
        public string? BizType { get; set; }
        public string? BizItem { get; set; }
        public string? ZipCode { get; set; }
        public string? CorpNo { get; set; }
    }

    private class TenantRow
    {
        public string TenantId { get; set; } = "";
        public string TenantCode { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string Status { get; set; } = "";
        public int IsLocked { get; set; }
    }
}
