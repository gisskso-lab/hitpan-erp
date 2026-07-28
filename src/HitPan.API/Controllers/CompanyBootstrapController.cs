using HitPan.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

// ERP 첫 설치 자동 반영 — 헌법 #35 객체 완전 분리 (사장님 결재 2026-06-04, W2)
//
// 흐름:
//   1) ERP Web /setup/license Step 1 → 백오피스 API /api/landing/license/claim
//      → 응답에 BootstrapToken(HMAC, 이행기) + SignedProof(ECDSA 공개키 증표, 신규) + SignedProofKid
//   2) ERP Web/설치마법사 → 본 API POST /api/setup/bootstrap (증표 + 회사정보)
//   3) 본 API: 증표 병행 검증(2-part HMAC / 3-part 공개키, 백오피스 URL 호출 0건) → local_company + local_subscription
//   4) is_locked_from_landing=1 + bootstrap_at
//   5) POST /api/setup/create-parent (증표 재검증 + 부모계정 생성) 또는 오프라인 seed-parent 서브커맨드
//
// ②단계 (작업지시서 20260707작1, 사장님 승인 2026-07-07):
//   - 증표 검증은 SerialProofVerifier(2-part HMAC / 3-part 공개키 병행)로 분리. HMAC 은 이행기 하위호환(제거 금지).
//   - DB 트랜잭션은 CompanyBootstrapProvisioner 로 분리 — create-parent 와 seed-parent 서브커맨드가 공유(복붙 금지).
//
// 헌법 정합:
//   #15 빈 catch 금지 / #18·#22 평문 사업자번호는 ERP 로컬만 / #20 온보딩 끊김 0
//   #23 서명키(개인키) EXE 노출 금지 → 공개키 오프라인 검증 / #35 객체 완전 분리 / #40 부모계정=아이디 방식·본사 비번 0
[ApiController]
[Route("api/setup")]
[AllowAnonymous]
public class CompanyBootstrapController : ControllerBase
{
    private readonly ILogger<CompanyBootstrapController> _logger;
    private readonly SerialProofVerifier _verifier;
    private readonly CompanyBootstrapProvisioner _provisioner;

    public CompanyBootstrapController(
        ILogger<CompanyBootstrapController> logger,
        SerialProofVerifier verifier,
        CompanyBootstrapProvisioner provisioner)
    {
        _logger = logger;
        _verifier = verifier;
        _provisioner = provisioner;
    }

    [HttpPost("bootstrap")]
    public async Task<IActionResult> Bootstrap([FromBody] BootstrapRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.BootstrapToken) || string.IsNullOrWhiteSpace(req.BizNo))
            return BadRequest(new { success = false, message = "부트스트랩 증표와 사업자번호가 필요합니다." });

        // bootstrap 은 신선한 증표를 요구(allowExpired: false) — 최초 회사정보 반영.
        var (ok, payload, error) = _verifier.Verify(req.BootstrapToken);
        if (!ok || payload is null)
            return Unauthorized(new { success = false, message = error ?? "유효하지 않은 부트스트랩 증표입니다." });

        try
        {
            var (outcome, message) = await _provisioner.BootstrapCompanyAsync(payload, new CompanyBootstrapInput
            {
                BizNo = req.BizNo,
                CeoName = req.CeoName,
                Tel = req.Tel,
                Address = req.Address,
                Email = req.Email,
                BizType = req.BizType,
                BizItem = req.BizItem,
                ZipCode = req.ZipCode,
                CorpNo = req.CorpNo,
                SyncSource = "bootstrap"
            }, ct);

            return outcome switch
            {
                CompanyBootstrapProvisioner.BootstrapOutcome.Ok => Ok(new
                {
                    success = true,
                    message = "회사 정보가 ERP에 자동 반영되었습니다.",
                    tenantCode = payload.TenantCode,
                    companyName = payload.CompanyName
                }),
                CompanyBootstrapProvisioner.BootstrapOutcome.AlreadyLocked
                    => BadRequest(new { success = false, message }),
                _ => BadRequest(new { success = false, message })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CompanyBootstrap] 자동 반영 실패");
            return StatusCode(500, new { success = false, message = "자동 반영 중 서버 오류가 발생했습니다." });
        }
    }

    // 부모계정 자동 생성 — 증표 재검증 (allowExpired: true — 저장 참사 봉합, 헌법 #40)
    [HttpPost("create-parent")]
    public async Task<IActionResult> CreateParent([FromBody] CreateParentRequest req, CancellationToken ct)
    {
        if (req is null
            || string.IsNullOrWhiteSpace(req.BootstrapToken)
            || string.IsNullOrWhiteSpace(req.Email)
            || string.IsNullOrWhiteSpace(req.Password)
            || string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { success = false, message = "부트스트랩 증표·아이디·비밀번호·이름 필수" });

        // 저장 참사 봉합(사장님 결재 2026-07-06·#40): create-parent 는 순수 로컬 작업.
        //   bootstrap 이 is_locked_from_landing=1 로 이 PC 의 tenant 정체를 durable 하게 확정했으므로 토큰 "만료"와 무관.
        //   단 서명·audience 검증(위·변조·익명 차단)은 그대로 유지(allowExpired 는 exp 검사만 완화 → 백도어 아님).
        var (ok, payload, error) = _verifier.Verify(req.BootstrapToken, allowExpired: true);
        if (!ok || payload is null)
            return Unauthorized(new { success = false, message = error ?? "유효하지 않은 부트스트랩 증표입니다." });

        try
        {
            var (outcome, message, userId) = await _provisioner.CreateParentAsync(payload, new CreateParentInput
            {
                // #40: users.email 컬럼을 아이디로 재사용(이메일 형식 강제 금지). req.Email = 아이디(형식무관).
                LoginId = req.Email,
                Password = req.Password,
                Name = req.Name
            }, ct);

            return outcome switch
            {
                CompanyBootstrapProvisioner.CreateParentOutcome.Ok => Ok(new
                {
                    success = true,
                    message = "부모 계정이 생성되었습니다. 로그인 화면으로 이동해주세요.",
                    userId,
                    email = req.Email.Trim(),
                    tenantCode = payload.TenantCode
                }),
                CompanyBootstrapProvisioner.CreateParentOutcome.BootstrapMissing
                    => BadRequest(new { success = false, message }),
                CompanyBootstrapProvisioner.CreateParentOutcome.ParentExists
                    => BadRequest(new { success = false, message }),
                CompanyBootstrapProvisioner.CreateParentOutcome.DuplicateLogin
                    => BadRequest(new { success = false, message }),
                _ => BadRequest(new { success = false, message })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CompanyBootstrap] 부모계정 생성 실패");
            return StatusCode(500, new { success = false, message = "부모계정 생성 중 서버 오류가 발생했습니다." });
        }
    }

    public class BootstrapRequest
    {
        public string BootstrapToken { get; set; } = "";
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

    public class CreateParentRequest
    {
        public string BootstrapToken { get; set; } = "";
        // Email = 부모계정 아이디(형식무관, users.email 재사용 — 헌법 #40).
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
        public string Name { get; set; } = "";
    }
}
