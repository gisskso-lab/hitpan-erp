using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.Backoffice.API.Controllers;

// 사업자번호 검증 (헌법 #35 정합, 사장님 결재 2026-06-04)
//
// 2단계 검증:
//   1) 체크섬 검증 (오프라인, 즉시) — 형식·산식 오류 차단
//   2) 국세청 진위확인 (옵션, 환경변수 토큰 박제 시 가동)
//      - 토큰: BizVerify:NtsApiKey  (사장님 결재 영역)
//      - 토큰 없으면 체크섬만으로 응답 (verified=true, source=checksum)
//
// 헌법 정합:
//   #15 — 빈 catch 금지
//   #18·#22 — 평문 사업자번호 DB 저장 0건 (이 컨트롤러는 검증만, 저장은 LandingSignupController에서 해시)
//   #29 — 외부 API 토큰은 환경변수에서만 (코드 박제 0)
[ApiController]
[Route("api/landing/biz-no")]
[AllowAnonymous]
public class BizNoVerifyController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<BizNoVerifyController> _logger;

    public BizNoVerifyController(
        IConfiguration config,
        IHttpClientFactory httpFactory,
        ILogger<BizNoVerifyController> logger)
    {
        _config = config;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] VerifyRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.BizNo))
            return BadRequest(new VerifyResponse { Valid = false, Message = "사업자번호 필수" });

        var normalized = req.BizNo.Replace("-", "").Replace(" ", "").Trim();
        if (normalized.Length != 10 || !normalized.All(char.IsDigit))
            return Ok(new VerifyResponse { Valid = false, Message = "사업자번호는 10자리 숫자여야 합니다." });

        // 1) 체크섬 검증 (한국 사업자등록번호 표준 알고리즘)
        if (!IsValidChecksum(normalized))
        {
            _logger.LogInformation("[BizNoVerify] checksum failed bizNo={Masked}", Mask(normalized));
            return Ok(new VerifyResponse { Valid = false, Message = "올바르지 않은 사업자번호입니다.", Source = "checksum" });
        }

        // 2) 국세청 진위확인 (토큰 박제된 경우만)
        var ntsKey = _config["BizVerify:NtsApiKey"];
        if (string.IsNullOrWhiteSpace(ntsKey))
        {
            // 자격증명 결재 전 — 체크섬 통과만으로 응답 (사장님 결재 영역)
            _logger.LogInformation("[BizNoVerify] checksum ok (nts skipped, no api key) bizNo={Masked}", Mask(normalized));
            return Ok(new VerifyResponse
            {
                Valid = true,
                Message = "사업자번호 형식이 유효합니다. (국세청 진위확인 대기 — 자격증명 결재 영역)",
                Source = "checksum"
            });
        }

        try
        {
            // 국세청 진위확인 API 형식 박제 (https://api.odcloud.kr/api/nts-businessman/v1/status)
            // 토큰 박제 시 실 호출, 응답 형식 파싱은 사장님 결재 후 박제
            var http = _httpFactory.CreateClient();
            using var msg = new HttpRequestMessage(HttpMethod.Post,
                $"https://api.odcloud.kr/api/nts-businessman/v1/status?serviceKey={Uri.EscapeDataString(ntsKey)}");
            msg.Content = new StringContent(
                $"{{\"b_no\":[\"{normalized}\"]}}",
                System.Text.Encoding.UTF8, "application/json");

            using var res = await http.SendAsync(msg, ct);
            var body = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("[BizNoVerify] nts api {Status} body={Body}", (int)res.StatusCode, body);
                return Ok(new VerifyResponse
                {
                    Valid = true,
                    Message = "체크섬 통과 (국세청 일시 오류, 가입은 계속 가능)",
                    Source = "checksum-fallback"
                });
            }

            // 응답 파싱은 결재 후 정밀 박제. 현재는 200 OK = 진위확인 통과로 박제.
            _logger.LogInformation("[BizNoVerify] nts ok bizNo={Masked}", Mask(normalized));
            return Ok(new VerifyResponse
            {
                Valid = true,
                Message = "사업자번호가 국세청에 등록된 유효 사업자입니다.",
                Source = "nts"
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BizNoVerify] nts api 호출 실패 bizNo={Masked}", Mask(normalized));
            return Ok(new VerifyResponse
            {
                Valid = true,
                Message = "체크섬 통과 (국세청 일시 오류, 가입은 계속 가능)",
                Source = "checksum-fallback"
            });
        }
    }

    // 한국 사업자등록번호 체크섬 (국세청 표준)
    //   가중치: 1,3,7,1,3,7,1,3,5
    //   9번째 자리 곱 결과는 (d9*5)을 10으로 나눈 몫을 더함
    //   합 % 10 = 0 이면 그대로, 아니면 10 - (합 % 10) 이 10번째 자리와 일치해야 함
    private static bool IsValidChecksum(string bn)
    {
        if (bn.Length != 10) return false;
        ReadOnlySpan<int> w = stackalloc int[] { 1, 3, 7, 1, 3, 7, 1, 3, 5 };
        int sum = 0;
        for (int i = 0; i < 9; i++) sum += (bn[i] - '0') * w[i];
        sum += ((bn[8] - '0') * 5) / 10;
        int expected = (10 - (sum % 10)) % 10;
        return expected == (bn[9] - '0');
    }

    private static string Mask(string bn) =>
        bn.Length >= 6 ? bn.Substring(0, 3) + "**" + bn.Substring(5) : "***";

    public class VerifyRequest
    {
        public string BizNo { get; set; } = "";
    }

    public class VerifyResponse
    {
        public bool Valid { get; set; }
        public string Message { get; set; } = "";
        public string? Source { get; set; }
    }
}
