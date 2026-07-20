using System.Security.Cryptography;
using System.Text;

namespace HitPan.Watchdog.AutoUpdate;

/// <summary>
/// manifest 서명 검증기 (작업지시서 20260715작1, 사장님 결재 2026-07-16).
///
/// ■ 왜 필요한가 — 고리4를 채우는 행위 자체가 구멍을 연다
///   업데이트 feed 주소는 HITPAN_UPDATE_FEED 환경변수로 바뀔 수 있고(UpdateClient), manifest 에는
///   서명이 없었다. sha256 은 manifest 안에 있어 자기참조(manifest 를 바꾸는 자가 sha256 도 바꾼다).
///   지금은 실제 파일 교체 코드가 없어 공격 표면이 "다운로드까지"로 막혀 있을 뿐이다.
///   **W4-2(실제 교체)가 붙는 순간 "환경변수 하나 → SYSTEM 권한 임의 코드 실행"이 완성된다.**
///   그래서 서명은 W4-2 의 선행 조건이다.
///
/// ■ 무엇을 재사용했나
///   HitPan.API 의 SerialProofVerifier(ECDSA P-256 / SHA-256 / IEEE-P1363, kid→공개키 맵, db.conf override)
///   가 이미 검증된 자산이다. 워치독은 API 를 참조하지 않으므로 그 '검증 로직'만 이식했다
///   (System.Security.Cryptography = BCL, 신규 패키지 0).
///
/// ■ 무엇을 일부러 안 가져왔나 — HMAC 대칭키 경로 (CTO 지시)
///   시리얼 증표는 기발급분(2-part HMAC) 하위호환 때문에 대칭키 경로를 못 버린다.
///   manifest 는 신규 스키마라 그 부채가 0이다 — 여기서만은 "대칭키 금지"가 공짜다.
///   대칭키는 검증하는 쪽(고객 PC)도 서명을 만들 수 있다는 뜻이고, 그건 곧 전 고객 PC 에
///   코드 실행 열쇠를 뿌리는 것이다. 절대 추가하지 말 것.
///
/// ■ 키 분리 — 시리얼 키를 재사용하지 않는 이유
///   시리얼 개인키 유출 = 가짜 라이선스. 업데이트 개인키 유출 = 전 고객 PC 임의 코드 실행.
///   위력이 다르므로 kid 를 분리한다("upd-v1"). 한쪽이 새도 다른 쪽으로 번지지 않는다.
///
/// ■ 개인키는 어디에도 없다
///   개인키 = NCP /var/hitpan/update-keys/ (600 root). CI(GitHub)·레포·EXE 어디에도 없다.
///   CI 는 zip·sha256·manifest 본문까지만 만들고, 서명은 NCP 에서 사람이 결재 후 1회 수행한다.
///   EXE 에는 공개키만 담긴다 — 공개키는 노출돼도 위조가 불가능하다.
///
/// 헌법 정합: #15(침묵 금지) / #22(본사 최소 보유·개인키 격리) / #23(5중 검증) / #29(키 조작 사전 결재) / #34.
/// </summary>
public sealed class UpdateSignatureVerifier
{
    private readonly ILogger<UpdateSignatureVerifier> _logger;

    public UpdateSignatureVerifier(ILogger<UpdateSignatureVerifier> logger) => _logger = logger;

    /// <summary>
    /// EXE 내장 공개키 맵 (kid → SPKI PEM).
    ///
    /// ⚠️ 아직 비어 있다 — 열쇠 생성은 NCP 작업이라 사장님 결재·실행이 필요하다(헌법 #29).
    ///    키가 없으면 아래 Verify 는 모든 manifest 를 거부한다(fail-closed).
    ///    그게 맞는 동작이다: 서명을 확인할 수 없는데 코드를 교체하느니, 업데이트를 안 하는 편이 안전하다.
    ///
    /// 키가 준비되면 여기 PEM 을 넣거나(EXE 내장), db.conf 로 주입한다(재빌드 없이 롤오버).
    /// 개인키는 절대 여기 두지 않는다 — 이 파일은 고객 PC 로 배포된다.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> EmbeddedPublicKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // upd-v1 (베타) — NCP /var/hitpan/update-keys/update_private.pem 로 서명한다(개인키는 NCP 에만).
            //   생성: 2026-07-20, 사장님 NCP 실행(헌법 #29). 이 공개키는 노출돼도 위조 불가.
            //   정식 전 테넌트별/HSM 키로 롤오버 시 kid 를 upd-v2 로 올리고 db.conf override 로 무중단 교체.
            ["upd-v1"] =
                "-----BEGIN PUBLIC KEY-----\n" +
                "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEKG/T7LlzfeNIOiL8CtCV2eHPVH4k\n" +
                "Xp8MqEZEwVst1xjnJcRSN9QRC/TIaQXBem6t5X/37NldMyZzQKRTa1O23Q==\n" +
                "-----END PUBLIC KEY-----\n",
        };

    /// <summary>
    /// kid 의 공개키 PEM 을 얻는다. db.conf override 우선(재빌드 없이 롤오버), 없으면 EXE 내장 상수.
    /// SerialProofVerifier.ResolvePublicKeyPem 과 동일한 정신 — 다만 워치독은 db.conf 를 직접 읽는다.
    /// </summary>
    private static string? ResolvePublicKeyPem(string kid)
    {
        var overridePem = DbConfReader.GetValue($"HITPAN_UPDATE_PUBLIC_KEY__{kid}");
        if (!string.IsNullOrWhiteSpace(overridePem))
            return NormalizePem(overridePem);

        return EmbeddedPublicKeys.TryGetValue(kid, out var pem) ? pem : null;
    }

    /// <summary>
    /// db.conf 는 한 줄 값이라 PEM 개행이 "\n" 문자열로 들어온다. 실제 개행으로 되돌린다.
    /// (SerialProofVerifier 가 같은 문제를 겪는다 — db.conf 단일출처의 구조적 제약.)
    /// </summary>
    private static string NormalizePem(string raw)
        => raw.Replace("\\n", "\n", StringComparison.Ordinal).Trim() + "\n";

    /// <summary>
    /// manifest 서명을 검증한다.
    ///
    /// 거부(false)하는 경우 — 전부 "확인할 수 없으면 설치하지 않는다"(fail-closed):
    ///   · Signature·Kid 가 없음 (서명 없는 manifest = 위조 가능)
    ///   · kid 의 공개키를 모름 (폐기된 키 / 알 수 없는 발급자)
    ///   · 서명 형식 오류
    ///   · 서명 불일치 (위·변조)
    /// </summary>
    public bool Verify(UpdateManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Signature) || string.IsNullOrWhiteSpace(manifest.Kid))
        {
            _logger.LogError("[Update/Sign] manifest 에 서명이 없습니다({V}) — 위조를 구분할 수 없어 설치하지 않습니다.",
                manifest.Version);
            return false;
        }

        var pem = ResolvePublicKeyPem(manifest.Kid);
        if (pem is null)
        {
            _logger.LogError("[Update/Sign] 알 수 없는 서명 키입니다(kid={Kid}, {V}) — 설치하지 않습니다. " +
                             "키가 아직 배포되지 않았거나(초기 설정 미완), 폐기된 키로 서명된 manifest 입니다.",
                manifest.Kid, manifest.Version);
            return false;
        }

        var result = VerifyWithPem(manifest, pem);
        switch (result)
        {
            case VerifyResult.BadEncoding:
                _logger.LogError("[Update/Sign] 서명 인코딩 오류({V}) — 설치하지 않습니다.", manifest.Version);
                return false;
            case VerifyResult.SignatureMismatch:
                // 서명 불일치 = 누군가 manifest 를 바꿨거나(위·변조), feed 가 바뀌었거나, 서명 규격이 어긋났다.
                // 어느 쪽이든 코드를 교체하면 안 된다.
                _logger.LogError("[Update/Sign] 🛑 서명이 맞지 않습니다({V}, kid={Kid}) — 위·변조된 manifest 로 보고 설치를 차단합니다.",
                    manifest.Version, manifest.Kid);
                return false;
            case VerifyResult.Error:
                // 헌법 #15: 침묵 금지. 검증 중 예외 = 판정 불능 = 설치 금지.
                _logger.LogError("[Update/Sign] 서명 검증 중 오류({V}, kid={Kid}) — 설치하지 않습니다(공개키·서명 형식 확인).",
                    manifest.Version, manifest.Kid);
                return false;
            default:
                _logger.LogInformation("[Update/Sign] 서명 확인 완료({V}, kid={Kid})", manifest.Version, manifest.Kid);
                return true;
        }
    }

    internal enum VerifyResult { Ok, BadEncoding, SignatureMismatch, Error }

    /// <summary>
    /// 순수 암호 검증 코어 — kid→공개키 해석(내장/db.conf)을 뺀 "서명·규격 확인"만 담당한다.
    ///
    /// internal 인 이유: 검증팀 SoD 반증 F-3 — 종전 테스트는 bare ECDsa 자기 왕복만 해서 정작 이 검증기가
    ///   유효한 서명을 통과시키는지 한 번도 확인하지 않았다(검증기를 지워도 테스트가 초록이었다).
    ///   실제 openssl 서명(DER + 표준 Base64)과 .NET 서명(P1363 + Base64Url)이 이 코어를 그대로 통과하는지
    ///   테스트가 직접 밟게 하려고 PEM 을 인자로 받는 형태로 노출한다(kid 맵·db.conf 는 파일 의존이라 분리).
    /// </summary>
    internal static VerifyResult VerifyWithPem(UpdateManifest manifest, string pem)
    {
        byte[] sig;
        try
        {
            sig = Base64UrlDecode(manifest.Signature ?? string.Empty);
        }
        catch (FormatException)
        {
            return VerifyResult.BadEncoding;
        }

        try
        {
            var payloadBytes = Encoding.UTF8.GetBytes(UpdateManifestSigning.BuildSigningPayload(manifest));

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(pem);

            // 봉합 (2026-07-16, 검증팀 SoD 반증 P0): 서명 '형식'을 두 가지 다 받는다.
            //   왜 — 서명을 만드는 주체가 다르다:
            //     · 시리얼 증표는 백오피스(.NET)가 서명한다 → IEEE-P1363(raw r‖s, 64B).
            //     · 업데이트 manifest 는 NCP 에서 사람이 openssl 로 서명한다(헌법 #29 사람 결재).
            //       openssl `dgst -sign` 은 DER(0x30… SEQUENCE, 가변 71B 안팎)을 낸다.
            //   검증기가 P1363 만 받으면, 사장님이 절차서대로 openssl 로 서명한 정상 manifest 가
            //   전부 거부돼 **전 고객 업데이트가 전면 중단**된다(검증팀이 실측으로 적발).
            //   그래서 여기서 두 형식을 순서대로 시도한다 — 발급자는 openssl 한 줄이면 끝나고,
            //   .NET 이 서명하는 미래 경로(자동화)도 P1363 로 그대로 통과한다.
            var ok = ecdsa.VerifyData(payloadBytes, sig, HashAlgorithmName.SHA256,
                         DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
                     || ecdsa.VerifyData(payloadBytes, sig, HashAlgorithmName.SHA256,
                         DSASignatureFormat.Rfc3279DerSequence);

            return ok ? VerifyResult.Ok : VerifyResult.SignatureMismatch;
        }
        catch (Exception)
        {
            // 공개키 PEM 형식 오류·서명 바이트 손상 등 — 판정 불능. 호출부가 로그를 남긴다.
            return VerifyResult.Error;
        }
    }

    /// <summary>
    /// 서명 문자열을 바이트로 되돌린다. 두 인코딩을 모두 받는다:
    ///   · Base64Url(RFC 4648 §5, '-_' + 패딩 생략) — 백오피스(.NET) 발급기가 쓰는 형식.
    ///   · 표준 Base64('+/' + '=' 패딩) — NCP 에서 openssl `base64` 로 만든 서명이 이 형식이다.
    /// 봉합 (2026-07-16, 검증팀 SoD 반증 P0): openssl 서명(표준 Base64, 줄바꿈 포함 가능)도 통과해야
    ///   전 고객 업데이트 중단을 피한다. '-_'→'+/' 치환은 표준 Base64 에는 무해하고, 공백·개행은
    ///   openssl `base64` 가 76자마다 넣을 수 있어 미리 제거한다.
    /// </summary>
    private static byte[] Base64UrlDecode(string input)
    {
        var s = input
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace('-', '+').Replace('_', '/');

        // 이미 패딩이 있으면(표준 Base64) 그대로, 없으면(Base64Url) 길이에 맞춰 채운다.
        var rem = s.Length % 4;
        switch (rem)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
            case 1: throw new FormatException("서명 길이 오류");
        }
        return Convert.FromBase64String(s);
    }
}
