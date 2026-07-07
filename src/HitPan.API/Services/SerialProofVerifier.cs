using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HitPan.Infrastructure.Configuration;

namespace HitPan.API.Services;

// 부트스트랩 증표 병행 검증기 (작업지시서 20260707작1 ②단계, 사장님 승인 2026-07-07)
//
// 목적 (헌법 #40·#35·#23·#22 정합):
//   백오피스(①단계)가 발급하는 부모계정 온보딩 증표를 ERP(EXE)가 오프라인으로 검증한다.
//   - 3-part(<payloadB64>.<sigB64>.<kid>)  = ECDSA P-256 / SHA-256 / IEEE-P1363 공개키 서명(신규, 정식 완성도)
//   - 2-part(<payloadB64>.<sigB64>)         = HMAC-SHA256 대칭키(기존, 이행기 하위호환 — BLOCKING 게이트)
//   두 경로를 kid 분기로 병행 검증해, 기발급 시리얼(HMAC)이 소진될 때까지 재설치가 전멸하지 않게 한다.
//
// 🔴 이행기 원칙 (CTO 조건 1 — 한 방에 HMAC 제거 금지):
//   HMAC 경로는 이 ②단계에서 절대 제거하지 않는다. ④단계(기존 시리얼 소진 후)에서만 제거한다.
//   2-part 를 수용하는 것은 하위호환 필수이며, 삭제하면 demo T-001 등 기발급 고객 재설치 불가(헌법 #20 위반).
//
// 공개키 EXE 내장 (원칙 C — 롤오버 대비):
//   - 공개키는 노출돼도 위조 불가라 EXE 내장·배포 무해(#23).
//   - kid → 공개키 PEM 맵으로 보관(지금은 1개라도 맵 구조 — 개인키 유출 시 공개키 롤오버·다중 병행검증).
//   - 각 kid 의 공개키는 (1) db.conf/환경변수 override(HITPAN_SERIAL_PUBLIC_KEY__<kid>)가 있으면 그것을,
//     없으면 (2) 아래 내장 상수 EmbeddedPublicKeys 를 쓴다. override 를 두는 이유:
//       정식 배포 시 ops 가 실제 키쌍을 새로 발급해 공개키만 db.conf 로 주입하면 EXE 재빌드 없이 롤오버 가능.
//       (내장 상수는 이행기·개발 기본값. 정식 키는 ops 가 발급·주입 — PM 인계 참조.)
public sealed class SerialProofVerifier
{
    private readonly ILogger<SerialProofVerifier> _logger;

    public SerialProofVerifier(ILogger<SerialProofVerifier> logger)
    {
        _logger = logger;
    }

    // ── EXE 내장 공개키 맵 (kid → SPKI PEM) ─────────────────────────────────────────────
    //   기본 kid = "v1" (백오피스 SerialSignatureService.ActiveKeyId 기본값과 정합).
    //   ⚠️ 아래 상수는 이행기·개발 기본 공개키다. 정식 배포 전 ops 가 백오피스에서 실제 키쌍을 발급하고,
    //      그 공개키를 여기(상수) 또는 db.conf(HITPAN_SERIAL_PUBLIC_KEY__v1) 로 주입한다.
    //      개인키는 ERP 어디에도 존재하지 않는다(공개키만 내장 — #23).
    private static readonly IReadOnlyDictionary<string, string> EmbeddedPublicKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // kid=v1 공개키 — NCP /var/hitpan/serial-keys 에서 생성된 개인키의 짝 공개키(SPKI).
            //   개인키는 NCP 디스크(chmod 600, root)에만 존재. 공개키는 노출 무해라 EXE 내장.
            //   ⚠️ 이 키쌍은 베타 검증용. 정식 배포 전 사장님이 새 키쌍 발급·교체(#23·#29).
            ["v1"] =
                "-----BEGIN PUBLIC KEY-----\n" +
                "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEbBArsSmwG5JApTfss6hATy/N1tSQ\n" +
                "yaeooX0Y/WoJ1obMkh5kRuH0L9yE8UO3nioCFJVGWVTyMvTSMsKhqiJPnw==\n" +
                "-----END PUBLIC KEY-----\n",
        };

    // 지정 kid 의 공개키 PEM 을 얻는다. db.conf/환경변수 override 우선, 없으면 내장 상수.
    private static string? ResolvePublicKeyPem(string kid)
    {
        // db.conf/환경변수 override: HITPAN_SERIAL_PUBLIC_KEY__<kid>
        var overridePem = TenantConfigReader.Get($"HITPAN_SERIAL_PUBLIC_KEY__{kid}");
        if (!string.IsNullOrWhiteSpace(overridePem))
            return NormalizePem(overridePem);

        return EmbeddedPublicKeys.TryGetValue(kid, out var pem) ? pem : null;
    }

    // db.conf 는 개행을 못 담으므로 \n 리터럴을 실제 개행으로 복원한다(override 편의).
    private static string NormalizePem(string raw) =>
        raw.Contains("\\n", StringComparison.Ordinal) ? raw.Replace("\\n", "\n") : raw;

    // 증표 검증 — 3-part(공개키) / 2-part(HMAC) 자동 분기.
    //   allowExpired: create-parent/seed-parent 전용(저장 참사 봉합, 헌법 #40). exp 검사만 완화, 서명·aud 는 강제.
    public (bool ok, ProofPayload? payload, string? error) Verify(string token, bool allowExpired = false)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (false, null, "증표 없음");

        var parts = token.Split('.');
        return parts.Length switch
        {
            3 => VerifyEcdsaProof(parts[0], parts[1], parts[2], allowExpired),
            2 => VerifyHmacProof(parts[0], parts[1], allowExpired),
            _ => (false, null, "증표 형식 오류(2/3분절 아님)")
        };
    }

    // ── 3-part 공개키 증표(ECDSA P-256 / SHA-256 / IEEE-P1363) ──────────────────────────
    private (bool ok, ProofPayload? payload, string? error) VerifyEcdsaProof(
        string payloadB64, string sigB64, string kid, bool allowExpired)
    {
        var pem = ResolvePublicKeyPem(kid);
        if (pem is null)
        {
            _logger.LogWarning("[SerialProof] 공개키 kid={Kid} 미보유 — 검증 불가(EXE 내장/override 확인)", kid);
            return (false, null, $"공개키 검증 실패: 알 수 없는 서명 버전(kid={kid})");
        }

        try
        {
            byte[] sig;
            try
            {
                sig = Base64UrlDecode(sigB64);
            }
            catch (FormatException)
            {
                return (false, null, "공개키 검증 실패: 서명 인코딩 오류");
            }

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(pem);
            // 백오피스 SerialSignatureService 와 정확히 동일: payloadB64 문자열 바이트에 대한 서명, IEEE-P1363 고정길이.
            var verified = ecdsa.VerifyData(
                Encoding.UTF8.GetBytes(payloadB64),
                sig,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            if (!verified)
                return (false, null, "공개키 검증 실패: 서명 불일치(위·변조 의심)");

            return ParseAndValidatePayload(payloadB64, allowExpired);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SerialProof] 공개키 증표 검증 예외 kid={Kid}", kid);
            return (false, null, "공개키 검증 오류");
        }
    }

    // ── 2-part HMAC 증표(기존, 이행기 하위호환 — 절대 제거 금지) ────────────────────────
    private (bool ok, ProofPayload? payload, string? error) VerifyHmacProof(
        string payloadB64, string sigB64, bool allowExpired)
    {
        // 봉합(재설치 P0 2차 벽, 사장님 결재 2026-07-06): Production 에서 서명키 부재 시 명확 거부(DEV 폴백 유출 차단).
        var key = TenantConfigReader.Get("HITPAN_BOOTSTRAP_TOKEN_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            var env = TenantConfigReader.Get("ASPNETCORE_ENVIRONMENT")
                     ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            if (string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("[SerialProof] HITPAN_BOOTSTRAP_TOKEN_KEY 부재(Production) — HMAC 검증 거부. db.conf 키 확인.");
                return (false, null, "설치 구성 오류: 서명키 부재 (관리자 문의)");
            }
            key = "DEV-bootstrap-token-key-change-in-production-32+chars";
        }

        try
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            var expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadB64));
            byte[] actual;
            try
            {
                actual = Base64UrlDecode(sigB64);
            }
            catch (FormatException)
            {
                return (false, null, "HMAC 검증 실패: 서명 인코딩 오류");
            }
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                return (false, null, "HMAC 서명 불일치");

            return ParseAndValidatePayload(payloadB64, allowExpired);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SerialProof] HMAC 증표 검증 예외");
            return (false, null, "HMAC 검증 오류");
        }
    }

    // 서명 검증 통과 후 payload 파싱 + aud/exp/sub 공통 검증(2/3-part 공유).
    private static (bool ok, ProofPayload? payload, string? error) ParseAndValidatePayload(
        string payloadB64, bool allowExpired)
    {
        var json = Encoding.UTF8.GetString(Base64UrlDecode(payloadB64));
        var payload = JsonSerializer.Deserialize<ProofPayload>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (payload is null)
            return (false, null, "페이로드 파싱 실패");

        if (payload.Aud != "erp-bootstrap")
            return (false, null, "audience 불일치");

        if (!allowExpired)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (payload.Exp < now)
                return (false, null, "토큰 만료 (라이선스 검증을 다시 진행해주세요)");
        }

        if (string.IsNullOrEmpty(payload.Sub))
            return (false, null, "tenant 식별 불가");

        return (true, payload, null);
    }

    private static byte[] Base64UrlDecode(string s)
    {
        var pad = s.Length % 4;
        if (pad > 0) s = s.PadRight(s.Length + (4 - pad), '=');
        return Convert.FromBase64String(s.Replace('-', '+').Replace('_', '/'));
    }
}

// 증표 payload — 백오피스 발급 JSON(snake_case) 과 1:1.
//   ⚠️ tenant_code·company_name 은 snake_case 라 대소문자 무시만으로는 바인딩 안 됨 → JsonPropertyName 명시.
//   (기존 HMAC 경로도 동일 payload 라 이 매핑이 정확해야 tenant_code/company_name 이 빈 문자열이 되지 않는다.)
public sealed class ProofPayload
{
    [JsonPropertyName("jti")] public string Jti { get; set; } = "";
    [JsonPropertyName("iss")] public string Iss { get; set; } = "";
    [JsonPropertyName("aud")] public string Aud { get; set; } = "";
    [JsonPropertyName("sub")] public string Sub { get; set; } = "";
    [JsonPropertyName("tenant_code")] public string TenantCode { get; set; } = "";
    [JsonPropertyName("company_name")] public string CompanyName { get; set; } = "";
    [JsonPropertyName("iat")] public long Iat { get; set; }
    [JsonPropertyName("exp")] public long Exp { get; set; }
    [JsonPropertyName("subscription")] public ProofSubscription? Subscription { get; set; }
}

// 백오피스 SubscriptionRow 는 PascalCase 로 직렬화되므로 여기 속성명도 PascalCase 그대로 바인딩된다.
public sealed class ProofSubscription
{
    public string SubscriptionTier { get; set; } = "basic";
    public string Status { get; set; } = "active";
    public string AiMode { get; set; } = "hitpan_pool";
    public int AiTokenMonthlyLimit { get; set; } = 100000;
    public int AiTokenExtra { get; set; }
    public string? AnthropicKeyLast4 { get; set; }
    public string AnthropicKeyStatus { get; set; } = "none";
    public int MaxUsers { get; set; } = 3;
    public int ExtraDeviceSlots { get; set; }
    public string? ResellerId { get; set; }
    public int ResellerTier { get; set; }
    public DateTime? TrialEndsAt { get; set; }
}
