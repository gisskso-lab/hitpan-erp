using System.Security.Cryptography;
using System.Text;

namespace HitPan.Backoffice.API.Services;

// 시리얼 공개키 서명 발급기 (작업지시서 20260707작1 ①단계, 사장님 승인 2026-07-07)
//
// 목적 (헌법 #40·#35·#23·#22 정합):
//   부모계정 온보딩 증표를 기존 HMAC 대칭키(LandingPublicController.SignToken)에 더해
//   비대칭 공개키 서명으로 "병행" 발급한다. ERP(EXE)는 EXE 내장 공개키로 오프라인 검증만 하면 되고,
//   개인키는 EXE 어디에도 노출되지 않는다(#23 서명키 EXE 노출 금지). HMAC 경로는 이행기 동안 유지(하위호환).
//
// 알고리즘 선택 = ECDSA P-256 (RSA-2048 대비 근거):
//   1) 서명 길이가 짧다(P-256 raw ≈ 64B vs RSA-2048 ≈ 256B) → 시리얼 증표 문자열이 짧다(설치 응답에 실려감).
//   2) .NET 기본 지원(ECDsa.Create / ImportFromPem / SignData·VerifyData + SHA-256) — 검증측(②) 코드가 단순.
//   3) 공개키가 작아 EXE 내장 부담이 적다.
//   4) 보안강도 P-256 ≈ RSA-3072 (베타·정식 모두 충분).
//
// 개인키 격리 (원칙 C — 2층 분리):
//   - 베타 = 서버 프로세스 격리 + 파일 ACL. 개인키 PEM 은 환경변수 HITPAN_SERIAL_SIGN_PRIVATE_KEY 로만 주입.
//   - 정식 = HSM·KMS (본 서비스 인터페이스 유지, 구현만 교체).
//   - 개인키를 소스·응답·로그에 절대 노출하지 않는다. Production 부재 시 명확히 거부(Program.cs JWT DEV-guard 패턴 정합).
//
// 시리얼 서명은 DB 저장 안 함(즉석 계산, 헌법 #22 · DDL 변경 0건).
//
// 증표 형식(HMAC SignToken 과 구분되는 3분절):
//   <payloadB64>.<sigB64>.<kid>
//   - payloadB64 = payload JSON 을 base64url (payload 에 tenant_id 반드시 포함 — 단일 진실원, 파생 금지)
//   - sigB64     = 개인키 ECDSA SHA-256 서명(payloadB64 바이트에 대한 서명)을 base64url
//   - kid        = key-id(공개키 버전). 개인키 유출 시 공개키 롤오버·다중 병행검증용.
public interface ISerialSignatureService
{
    // 활성 개인키 존재 여부. 미구성 시 병행 발급을 생략(HMAC 단독)하되, Production 에서는 Program 에서 거부.
    bool IsConfigured { get; }

    // 현재 활성 key-id (공개키 롤오버 버전).
    string ActiveKeyId { get; }

    // payload(익명 객체 등)를 개인키로 서명해 <payloadB64>.<sigB64>.<kid> 증표 문자열 반환.
    string Sign(object payload);

    // ②단계(ERP EXE)가 내장할 공개키 PEM. 공개키는 노출돼도 위조 불가라 로그/파일 배포 무해.
    string ExportPublicKeyPem();
}

public class SerialSignatureService : ISerialSignatureService
{
    // 개인키 PEM 환경변수 (베타 = 서버 격리 주입). 소스·응답·로그 노출 금지.
    private const string PrivateKeyEnv = "HITPAN_SERIAL_SIGN_PRIVATE_KEY";
    // key-id 환경변수 (미설정 시 기본 "v1"). 개인키 롤오버 시 증분.
    private const string KeyIdEnv = "HITPAN_SERIAL_SIGN_KID";

    private readonly ILogger<SerialSignatureService> _logger;
    private readonly string? _privateKeyPem;
    private readonly string _kid;

    public SerialSignatureService(ILogger<SerialSignatureService> logger)
    {
        _logger = logger;
        _privateKeyPem = Environment.GetEnvironmentVariable(PrivateKeyEnv);
        _kid = Environment.GetEnvironmentVariable(KeyIdEnv) is { Length: > 0 } k ? k : "v1";
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_privateKeyPem);

    public string ActiveKeyId => _kid;

    public string Sign(object payload)
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                $"{PrivateKeyEnv} 미설정 — 시리얼 공개키 서명 발급 불가(개인키 없이 서명 금지).");

        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var payloadBytes = Encoding.UTF8.GetBytes(json);
        var payloadB64 = Base64UrlEncode(payloadBytes);

        // ECDSA P-256 + SHA-256. IEEE-P1363 고정 길이 서명(검증측 파싱 단순).
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(_privateKeyPem);
        var sig = ecdsa.SignData(
            Encoding.UTF8.GetBytes(payloadB64),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var sigB64 = Base64UrlEncode(sig);

        // 개인키·서명원문은 로그에 남기지 않는다(#23). kid 만 흔적으로 기록.
        _logger.LogInformation("[SerialSignature] 시리얼 공개키 서명 발급 kid={Kid}", _kid);
        return $"{payloadB64}.{sigB64}.{_kid}";
    }

    public string ExportPublicKeyPem()
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                $"{PrivateKeyEnv} 미설정 — 공개키 추출 불가.");

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(_privateKeyPem);
        // SubjectPublicKeyInfo(SPKI) PEM — .NET/OpenSSL 상호 호환. ②단계 EXE 가 ImportFromPem 로 그대로 로드.
        return ecdsa.ExportSubjectPublicKeyInfoPem();
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
