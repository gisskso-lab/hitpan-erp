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
            // ["upd-v1"] = "-----BEGIN PUBLIC KEY-----\n...\n-----END PUBLIC KEY-----\n",
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

        try
        {
            byte[] sig;
            try
            {
                sig = Base64UrlDecode(manifest.Signature);
            }
            catch (FormatException)
            {
                _logger.LogError("[Update/Sign] 서명 인코딩 오류({V}) — 설치하지 않습니다.", manifest.Version);
                return false;
            }

            var payload = UpdateManifestSigning.BuildSigningPayload(manifest);

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(pem);
            var ok = ecdsa.VerifyData(
                Encoding.UTF8.GetBytes(payload),
                sig,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

            if (!ok)
            {
                // 서명 불일치 = 누군가 manifest 를 바꿨거나(위·변조), feed 가 바뀌었거나, 서명 규격이 어긋났다.
                // 어느 쪽이든 코드를 교체하면 안 된다.
                _logger.LogError("[Update/Sign] 🛑 서명이 맞지 않습니다({V}, kid={Kid}) — 위·변조된 manifest 로 보고 설치를 차단합니다.",
                    manifest.Version, manifest.Kid);
                return false;
            }

            _logger.LogInformation("[Update/Sign] 서명 확인 완료({V}, kid={Kid})", manifest.Version, manifest.Kid);
            return true;
        }
        catch (Exception ex)
        {
            // 헌법 #15: 침묵 금지. 검증 중 예외 = 판정 불능 = 설치 금지.
            _logger.LogError(ex, "[Update/Sign] 서명 검증 중 오류({V}, kid={Kid}) — 설치하지 않습니다.",
                manifest.Version, manifest.Kid);
            return false;
        }
    }

    /// <summary>
    /// Base64Url(RFC 4648 §5) 디코드. SerialProofVerifier 와 동일 규격 — 백오피스 발급기가 그 형식을 쓴다.
    /// URL·JSON 에 안전하도록 '+/' 대신 '-_' 를 쓰고 패딩('=')을 생략한 형식이다.
    /// </summary>
    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
            case 1: throw new FormatException("Base64Url 길이 오류");
        }
        return Convert.FromBase64String(s);
    }
}
