using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.Backoffice.API.Controllers;

// 자격증명 저장 상태 + 테스트 메일 송부 (사장님 결재 2026-06-04, 헌법 #29)
//
// OwnerOnly — 자격증명은 사장님 본인만 확인·테스트
//
// GET   /api/backoffice/credentials/status         (저장 여부 표시, 비밀값은 절대 반환 안 함)
// POST  /api/backoffice/credentials/test-mail      ({to, subject?}) — 테스트 메일 송부
//
// 헌법 정합:
//   #15 — 빈 catch 금지
//   #29 — 비밀값(SMTP 비번·API 토큰) 절대 응답에 노출 안 함, "저장됨/미저장"만
[ApiController]
[Route("api/backoffice/credentials")]
[Authorize]
public class CredentialsStatusController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly IEmailSender _email;
    private readonly ILogger<CredentialsStatusController> _logger;

    public CredentialsStatusController(
        IConfiguration config,
        IEmailSender email,
        ILogger<CredentialsStatusController> logger)
    {
        _config = config;
        _email = email;
        _logger = logger;
    }

    [HttpGet("status")]
    public IActionResult Status()
    {
        if (!IsOwner())
            return StatusCode(403, new { success = false, message = "사장님(Owner)만 접근할 수 있습니다." });

        var smtpHost = _config["Smtp:Host"];
        var smtpUser = _config["Smtp:User"];
        var smtpPass = _config["Smtp:Password"];
        var smtpFrom = _config["Smtp:From"];

        var ntsKey = _config["BizVerify:NtsApiKey"];

        var bizPepper = _config["Backoffice:BizNoPepper"];
        var licensePepper = _config["License:Pepper"];

        return Ok(new
        {
            success = true,
            items = new[]
            {
                new
                {
                    key = "smtp",
                    name = "SMTP (이메일 발송)",
                    category = "메일",
                    configured = !string.IsNullOrWhiteSpace(smtpHost) && !string.IsNullOrWhiteSpace(smtpUser),
                    detail = $"Host: {Mask(smtpHost)} / User: {Mask(smtpUser)} / Password: {(string.IsNullOrEmpty(smtpPass) ? "(미저장)" : "***")} / From: {smtpFrom ?? "(미저장)"}",
                    impact = "미저장 시: 가입·승인·라이선스 키 메일이 발송되지 않고 로그에만 남습니다."
                },
                new
                {
                    key = "nts",
                    name = "국세청 사업자번호 진위확인 API",
                    category = "외부 API",
                    configured = !string.IsNullOrWhiteSpace(ntsKey),
                    detail = string.IsNullOrEmpty(ntsKey) ? "(미저장)" : "토큰 저장됨",
                    impact = "미저장 시: 체크섬 검증만 작동. 폐업·휴업 사업자 거름망 미작동."
                },
                new
                {
                    key = "biz-pepper",
                    name = "사업자번호 해시 Pepper",
                    category = "암호",
                    configured = !string.IsNullOrWhiteSpace(bizPepper) && bizPepper != "dev-pepper-2026",
                    detail = string.IsNullOrEmpty(bizPepper) ? "(미저장 — 기본값 사용 중)"
                            : bizPepper == "dev-pepper-2026" ? "⚠️ 개발 기본값 사용 중 (운영값 적용 필요)"
                            : "운영용 저장됨",
                    impact = "개발 기본값 사용 시: 운영 배포 전 반드시 환경변수로 저장 필요."
                },
                new
                {
                    key = "license-pepper",
                    name = "라이선스 키 해시 Pepper",
                    category = "암호",
                    configured = !string.IsNullOrWhiteSpace(licensePepper) && licensePepper != "dev-pepper-2026",
                    detail = string.IsNullOrEmpty(licensePepper) ? "(미저장 — 기본값 사용 중)"
                            : licensePepper == "dev-pepper-2026" ? "⚠️ 개발 기본값 사용 중 (운영값 적용 필요)"
                            : "운영용 저장됨",
                    impact = "개발 기본값 사용 시: 라이선스 키 검증이 보안 취약. 운영 배포 전 저장 필수."
                }
            }
        });
    }

    [HttpGet("healthz")]
    public IActionResult Healthz()
    {
        if (!IsOwner())
            return StatusCode(403, new { success = false, message = "사장님(Owner)만 접근할 수 있습니다." });

        var checks = new List<CredentialCheck>
        {
            Probe("HITPAN_JWT_SECRET",       "Jwt:Key",                  "JWT (ERP API)"),
            Probe("HITPAN_BO_JWT_SECRET",    "Backoffice:JwtKey",        "JWT (백오피스)"),
            Probe("HITPAN_LICENSE_PEPPER",   "License:Pepper",           "라이선스 Pepper"),
            Probe("HITPAN_BIZNO_PEPPER",     "Backoffice:BizNoPepper",   "사업자번호 Pepper"),
            Probe("HITPAN_NTS_API_KEY",      "BizVerify:NtsApiKey",      "국세청 API"),
            // 🔴 20261015작B1 갈래 A(G1) — 거짓 초록 봉합. 발송기와 **같은 키만** 본다.
            //    봉합 전에는 폐기 이름(아래 DeprecatedSmtpEnvNames)을 env-or-config 판정의 첫 인자로
            //    넘겼고, 그 이름 쪽만 채워도 present=true 가 됐다 ⇒ 화면 초록 · 메일 0통(§4 갈래 A).
            ProbeSmtp("Smtp:Host",     "SMTP Host"),
            ProbeSmtp("Smtp:User",     "SMTP User"),
            ProbeSmtp("Smtp:Password", "SMTP Password"),
            ProbeSmtp("Smtp:From",     "SMTP From"),
            Probe("HITPAN_TOSS_CLIENT_KEY",  "Toss:ClientKey",           "토스 Client Key"),
            Probe("HITPAN_TOSS_SECRET_KEY",  "Toss:SecretKey",           "토스 Secret Key"),
            Probe("HITPAN_BOOTSTRAP_TOKEN_KEY", "Bootstrap:TokenKey",    "부트스트랩 토큰 키 (백오피스↔ERP 동일)"),
        };

        return Ok(new
        {
            success = true,
            allPresent = checks.All(c => c.Present),
            anyDev = checks.Any(c => c.IsDev),
            checkedAt = DateTime.UtcNow,
            // 🔴 20261015작B1 갈래 A(G1) — 폐기된 이름이 환경에 들어와 있으면 조용히 넘기지 않고
            //    "이 이름은 안 먹는다" 를 화면과 로그에 같이 알린다(#15).
            warnings = DeprecatedSmtpEnvWarnings(),
            items = checks
        });
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // 🔴 20261015작B1 갈래 A(G1) — healthz 거짓 초록 봉합
    //
    // [사고] 자격증명 화면이 초록인데 메일은 0통 나갔다. SMTP 4칸을 Probe(envName, configKey, …)
    //        로 재서 **폐기 이름 HITPAN_SMTP_HOST 하나만** 환경에 있어도 present=true 가 됐는데,
    //        발송기(Services/EmailSender.cs:51-56)는 Smtp:Host · Smtp:User · Smtp:Password ·
    //        Smtp:From **만** 읽는다. 화면이 보는 집합 ≠ 발송기가 읽는 집합이었다.
    //
    // [정본 이름] Smtp__* — ASP.NET Core 환경변수 구성이 "__" 를 ":" 로 접는다.
    //             HITPAN_SMTP_* 는 폐기다(매핑 코드 0건이라 아무 데도 연결돼 있지 않았다).
    //
    // 🔴 SMTP 외 칸(JWT·Pepper·NTS·Toss·Bootstrap)의 판정은 범위 밖이다 — 기존 Probe 를
    //    그대로 두고 SMTP 전용 판정을 **더했다**(#1 추가만).
    //
    // 근거: docs/운영기록/20261015작B1_배포준비1차수_작업지시서.md §4 갈래 A · §5 G1 행
    // 게이트: src/HitPan.Tests/Integrity/BackofficeSmtpHealthzParityGateTests.cs
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>폐기된 SMTP 환경변수 이름 — 판정에 쓰지 않고, 들어와 있으면 경고만 낸다.</summary>
    private static readonly string[] DeprecatedSmtpEnvNames =
    {
        "HITPAN_SMTP_HOST", "HITPAN_SMTP_USER", "HITPAN_SMTP_PASS", "HITPAN_SMTP_FROM"
    };

    /// <summary>
    /// SMTP 칸 판정 — 발송기와 **같은 구성키만** 본다. 환경변수 이름으로 우회하는 길을 두지 않는다.
    /// 화면에 내놓는 이름표는 정본 이름(<c>Smtp__Host</c> 꼴)이라, 사장님이 그 이름 그대로 넣으면 바로 먹는다.
    /// </summary>
    private CredentialCheck ProbeSmtp(string configKey, string display)
    {
        var v = _config[configKey];
        var present = !string.IsNullOrWhiteSpace(v);
        var isDev = !present || v!.StartsWith("DEV-", StringComparison.OrdinalIgnoreCase);
        return new CredentialCheck
        {
            Name = display,
            EnvName = configKey.Replace(":", "__"),   // Smtp:Host → Smtp__Host (정본 이름)
            Present = present,
            IsDev = isDev
        };
    }

    /// <summary>
    /// 폐기 이름이 환경에 들어와 있으면 경고 1줄. 값은 절대 담지 않는다 — **이름만** 말한다(#29).
    /// </summary>
    private List<string> DeprecatedSmtpEnvWarnings()
    {
        var hit = DeprecatedSmtpEnvNames
            .Where(n => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(n)))
            .ToList();

        if (hit.Count == 0) return new List<string>();

        _logger.LogWarning(
            "[CredentialsStatus] 폐기된 SMTP 환경변수 이름이 설정돼 있습니다 — 메일 발송에 반영되지 않습니다. " +
            "count={Count} names={Names} / 정본 이름은 Smtp__Host · Smtp__User · Smtp__Password · Smtp__From 입니다.",
            hit.Count, string.Join(", ", hit));

        return new List<string>
        {
            $"환경변수 {string.Join(" · ", hit)} 는 더 이상 쓰이지 않습니다(메일 발송에 반영되지 않습니다). " +
            "SMTP 는 Smtp__Host · Smtp__User · Smtp__Password · Smtp__From 이름으로 저장해야 발송기가 읽습니다."
        };
    }

    private CredentialCheck Probe(string envName, string configKey, string display)
    {
        var env = Environment.GetEnvironmentVariable(envName);
        var cfg = _config[configKey];
        var v = !string.IsNullOrWhiteSpace(env) ? env : cfg;
        var present = !string.IsNullOrWhiteSpace(v);
        var isDev = !present || (v != null && (v.StartsWith("DEV-", StringComparison.OrdinalIgnoreCase) || v == "dev-pepper-2026"));
        return new CredentialCheck
        {
            Name = display,
            EnvName = envName,
            Present = present,
            IsDev = isDev
        };
    }

    public class CredentialCheck
    {
        public string Name { get; set; } = "";
        public string EnvName { get; set; } = "";
        public bool Present { get; set; }
        public bool IsDev { get; set; }
    }

    [HttpPost("test-mail")]
    public async Task<IActionResult> TestMail([FromBody] TestMailRequest req, CancellationToken ct)
    {
        if (!IsOwner())
            return StatusCode(403, new { success = false, message = "사장님(Owner)만 접근할 수 있습니다." });

        if (req is null || string.IsNullOrWhiteSpace(req.To))
            return BadRequest(new { success = false, message = "수신 이메일을 입력해주세요." });

        var subject = string.IsNullOrWhiteSpace(req.Subject) ? "[히트판] SMTP 테스트 메일" : req.Subject;
        var html = @"
<div style='font-family:-apple-system,BlinkMacSystemFont,sans-serif;max-width:560px;margin:0 auto;padding:32px;color:#1A2B4A;'>
  <h2 style='color:#0F6E56;margin:0 0 16px;'>SMTP 테스트 메일</h2>
  <p>이 메일이 정상적으로 도착했다면 히트판 백오피스 SMTP 자격증명이 저장되어 작동 중입니다.</p>
  <p>발송 시각: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + @"</p>
</div>";

        var ok = await _email.SendAsync(req.To, subject, html, ct);
        if (ok)
        {
            _logger.LogInformation("[CredentialsStatus] 테스트 메일 송부 성공 to={To}", req.To);
            return Ok(new { success = true, message = $"테스트 메일이 {req.To}로 발송되었습니다. 메일함을 확인하세요." });
        }
        else
        {
            _logger.LogInformation("[CredentialsStatus] 테스트 메일 미송부 (SMTP 미저장 또는 실패) to={To}", req.To);
            return Ok(new
            {
                success = false,
                message = "SMTP 자격증명이 저장되지 않았거나 송부에 실패했습니다. 로그를 확인하세요."
            });
        }
    }

    private bool IsOwner() =>
        string.Equals(User.FindFirst("role")?.Value, "owner", StringComparison.OrdinalIgnoreCase);

    private static string Mask(string? v)
    {
        if (string.IsNullOrEmpty(v)) return "(미저장)";
        if (v.Length <= 6) return "***";
        return v.Substring(0, 3) + "***" + v.Substring(v.Length - 2);
    }

    public class TestMailRequest
    {
        public string To { get; set; } = "";
        public string? Subject { get; set; }
    }
}
