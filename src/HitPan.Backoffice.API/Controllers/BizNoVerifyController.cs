using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.Backoffice.API.Controllers;

// 사업자번호 검증 (헌법 #35 정합, 사장님 결재 2026-06-04)
//
// 2단계 검증:
//   1) 체크섬 검증 (오프라인, 즉시) — 형식·산식 오류 차단
//   2) 국세청 진위확인 (옵션, 환경변수 토큰 저장 시 가동)
//      - 토큰: BizVerify:NtsApiKey  (사장님 결재 영역)
//      - 토큰 없으면 체크섬만으로 응답 (verified=true, source=checksum)
//
// 헌법 정합:
//   #15 — 빈 catch 금지
//   #18·#22 — 평문 사업자번호 DB 저장 0건 (이 컨트롤러는 검증만, 저장은 LandingSignupController에서 해시)
//   #29 — 외부 API 토큰은 환경변수에서만 (코드 저장 0)
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

        // 2) 국세청 진위확인 (토큰 저장된 경우만)
        //
        // 🔴 P0 봉합 (2026-08-02, 샌드박스 실측에서 사장님이 막힘):
        //   종전엔 여기서 _config["BizVerify:NtsApiKey"] 만 읽었다.
        //   그런데 실제 가입 처리(LandingSignupController:83)는 환경변수 NTS_API_KEY 를 '먼저' 본다.
        //   운영 NCP 는 환경변수에만 키가 있고 appsettings 는 빈 문자열이다.
        //   ⇒ 같은 사업자번호인데 두 경로의 결과가 갈렸다:
        //        [검증] 버튼 → 키를 못 봄 → 체크섬만 통과 → "유효합니다" ✅
        //        [가입] 제출 → 환경변수 키로 실호출 → 실패 → 400 ❌
        //   고객은 "검증까지 됐는데 가입이 안 된다"는 상태에 빠진다. 원인 안내도 불가능하다.
        //
        //   봉합: 두 경로가 '같은 키'를 같은 순서로 보게 통일한다(환경변수 우선 → 설정).
        //   검증과 실제 처리가 다른 값을 보면 검증은 검증이 아니다.
        var ntsKey = Environment.GetEnvironmentVariable("NTS_API_KEY");
        if (string.IsNullOrWhiteSpace(ntsKey))
            ntsKey = _config["BizVerify:NtsApiKey"];
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
            // 국세청 진위확인 API 형식 저장 (https://api.odcloud.kr/api/nts-businessman/v1/status)
            // 토큰 저장 시 실 호출, 응답 형식 파싱은 사장님 결재 후 저장
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
                // 헌법 #22 정합 — 국세청 장애 시 가입 거부 (사장님 결재 2026-06-08)
                return Ok(new VerifyResponse
                {
                    Valid = false,
                    Message = "국세청 서비스 일시 장애입니다. 잠시 후 다시 시도해주세요.",
                    Source = "nts-error"
                });
            }

            // 응답 본문 정밀 파싱 (b_stt_cd로 사업 상태 판단)
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("data", out var dataArr) || dataArr.GetArrayLength() == 0)
            {
                _logger.LogWarning("[BizNoVerify] nts 응답 형식 이상 bizNo={Masked} body={Body}", Mask(normalized), body);
                return Ok(new VerifyResponse
                {
                    Valid = false,
                    Message = "국세청 응답을 확인할 수 없습니다. 잠시 후 다시 시도해주세요.",
                    Source = "nts-parse-error"
                });
            }

            var item = dataArr[0];
            var bStt = item.TryGetProperty("b_stt", out var bSttEl) ? bSttEl.GetString() ?? "" : "";
            var bSttCd = item.TryGetProperty("b_stt_cd", out var bSttCdEl) ? bSttCdEl.GetString() ?? "" : "";
            var taxType = item.TryGetProperty("tax_type", out var taxEl) ? taxEl.GetString() ?? "" : "";

            // b_stt_cd 영역:
            //   "01" = 계속사업자 (정상)
            //   "02" = 휴업자
            //   "03" = 폐업자
            //   ""   = 등록되지 않은 사업자
            if (bSttCd == "01")
            {
                _logger.LogInformation("[BizNoVerify] nts ok bizNo={Masked} bStt={BStt} taxType={Tax}",
                    Mask(normalized), bStt, taxType);
                return Ok(new VerifyResponse
                {
                    Valid = true,
                    Message = $"국세청 등록 확인 — {bStt} ({taxType})",
                    Source = "nts"
                });
            }

            if (bSttCd == "02")
            {
                _logger.LogInformation("[BizNoVerify] nts 휴업 bizNo={Masked}", Mask(normalized));
                return Ok(new VerifyResponse
                {
                    Valid = false,
                    Message = "국세청 등록상 휴업 상태입니다. 정상 사업자만 가입 가능합니다.",
                    Source = "nts"
                });
            }

            if (bSttCd == "03")
            {
                _logger.LogInformation("[BizNoVerify] nts 폐업 bizNo={Masked}", Mask(normalized));
                return Ok(new VerifyResponse
                {
                    Valid = false,
                    Message = "국세청 등록상 폐업 상태입니다. 정상 사업자만 가입 가능합니다.",
                    Source = "nts"
                });
            }

            // 등록되지 않은 사업자
            _logger.LogInformation("[BizNoVerify] nts 미등록 bizNo={Masked} taxType={Tax}",
                Mask(normalized), taxType);
            return Ok(new VerifyResponse
            {
                Valid = false,
                Message = "국세청에 등록되지 않은 사업자번호입니다. 정확한 번호를 다시 확인해주세요.",
                Source = "nts"
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BizNoVerify] nts api 호출 실패 bizNo={Masked}", Mask(normalized));
            // 헌법 #22 정합 — 장애 시 가입 거부 (보안 우선)
            return Ok(new VerifyResponse
            {
                Valid = false,
                Message = "국세청 서비스 일시 장애입니다. 잠시 후 다시 시도해주세요.",
                Source = "nts-error"
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
