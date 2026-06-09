using System.Text.RegularExpressions;
using Dapper;
using MySqlConnector;

namespace HitPan.Backoffice.API.Services;

// 도메인 별칭 검증 Service (브라운킴 PM 2026-06-09, 사장님 결재 정합)
//
// 사장님 명세:
//   "랜딩페이지로 고객사한테 히트판ERP주소 받고, 중복검사"
//   "테넌트 넘버가 노출되는건 아닌거같아. PK인데"
//
// Controller 양쪽에서 박힐 영역:
//   - DomainAliasController: 실시간 검증 (랜딩 가입 폼 입력 시)
//   - LandingSignupController: 가입 시점 재검증 (race condition 차단)
public interface IDomainAliasService
{
    Task<DomainCheckResult> ValidateAsync(string? alias, CancellationToken ct);
}

public record DomainCheckResult(
    bool Available,
    string Code,
    string Message,
    List<string>? Suggestions);

public class DomainAliasService : IDomainAliasService
{
    private readonly IConfiguration _config;
    private readonly ILogger<DomainAliasService> _logger;

    public DomainAliasService(IConfiguration config, ILogger<DomainAliasService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<DomainCheckResult> ValidateAsync(string? alias, CancellationToken ct)
    {
        var normalized = (alias ?? "").Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(normalized))
            return new(false, "required", "원하시는 ERP 주소를 입력해주세요.", null);

        if (normalized.Length < 3 || normalized.Length > 30)
            return new(false, "length", "3~30자 사이로 입력해주세요.", null);

        if (!Regex.IsMatch(normalized, @"^[a-z0-9]([a-z0-9-]{1,28}[a-z0-9])?$"))
            return new(false, "format",
                "영문 소문자·숫자·하이픈만 사용 가능합니다. 하이픈은 가운데에만 박을 수 있습니다.", null);

        if (normalized.Contains("--"))
            return new(false, "format", "하이픈을 연속으로 사용할 수 없습니다.", null);

        if (Regex.IsMatch(normalized, @"^\d+$"))
            return new(false, "format", "숫자만으로는 사용할 수 없습니다.", null);

        if (ReservedWords.Contains(normalized))
            return new(false, "reserved",
                $"'{normalized}'은(는) 시스템 예약어입니다. 다른 주소를 시도해주세요.", null);

        foreach (var prefix in ReservedPrefixes)
        {
            if (normalized.StartsWith(prefix))
                return new(false, "reserved",
                    $"'{prefix}'로 시작하는 주소는 사용할 수 없습니다.", null);
        }

        foreach (var bad in BadWords)
        {
            if (normalized.Contains(bad))
                return new(false, "inappropriate",
                    "부적절한 단어가 포함되어 있습니다.", null);
        }

        await using var db = await OpenAsync(ct);

        var tenantHit = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM tenants WHERE domain_alias = @A", new { A = normalized });
        if (tenantHit > 0)
            return new(false, "duplicate",
                $"'{normalized}.hitpan.kr'은(는) 이미 사용 중입니다.",
                SuggestAlternatives(normalized));

        var signupHit = await db.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM landing_signups
              WHERE desired_domain = @A AND status NOT IN ('rejected')",
            new { A = normalized });
        if (signupHit > 0)
            return new(false, "pending",
                $"'{normalized}.hitpan.kr'은(는) 다른 가입 신청에서 사용 중입니다.",
                SuggestAlternatives(normalized));

        return new(true, "ok", $"✅ {normalized}.hitpan.kr 사용 가능", null);
    }

    private List<string> SuggestAlternatives(string baseName)
    {
        var candidates = new List<string>
        {
            $"{baseName}-erp", $"{baseName}-co", $"{baseName}-kr",
            $"{baseName}1", $"{baseName}2",
        };
        return candidates.Where(c => c.Length <= 30).ToList();
    }

    private async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var cs = _config.GetConnectionString("BackofficeDb")
                 ?? throw new InvalidOperationException("ConnectionStrings:BackofficeDb 미설정");
        var c = new MySqlConnection(cs);
        await c.OpenAsync(ct);
        return c;
    }

    private static readonly HashSet<string> ReservedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "hitpan", "back", "backoffice", "landing", "admin", "support", "help",
        "api", "www", "demo", "test", "staging", "dev", "prod", "production",
        "app", "mail", "smtp", "imap", "pop", "ftp", "ssh", "vpn",
        "cdn", "static", "assets", "media", "img", "image", "images",
        "root", "system", "sysadmin", "administrator", "owner",
        "register", "signup", "signin", "login", "logout", "auth",
        "billing", "payment", "checkout", "subscribe", "unsubscribe",
        "blog", "news", "press", "about", "contact", "career", "jobs",
        "status", "health", "ping", "echo", "metrics", "monitor",
        "api-demo", "api-back", "api-landing",
        "null", "undefined", "true", "false", "void", "error",
    };

    private static readonly string[] ReservedPrefixes =
    {
        "api-", "back-", "admin-", "www-", "mail-", "ftp-", "ssh-", "smtp-",
        "cdn-", "static-", "assets-",
    };

    private static readonly string[] BadWords =
    {
        "fuck", "shit", "bitch", "asshole", "dick", "porn", "sex", "xxx",
        "hack", "crack", "warez", "spam", "phish", "scam",
        "시발", "씨발", "병신", "개새", "좆",
    };
}
