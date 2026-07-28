using System.Globalization;
using System.Text;

namespace HitPan.Watchdog.AutoUpdate;

/// <summary>
/// manifest 서명 대상 문자열 규격 (작업지시서 20260715작1, 사장님 결재 2026-07-16).
///
/// ■ 이 파일이 하는 일
///   "무엇에 서명하는가"를 한 곳에서 정한다. 서명을 만드는 쪽(NCP)과 검증하는 쪽(고객 PC 워치독)이
///   똑같은 문자열을 만들어야 서명이 맞는다. 한 글자라도 다르면 정상 manifest 도 전부 거부돼
///   업데이트가 전면 중단된다 — 그래서 규격을 코드 한 곳에 못박고 테스트로 고정한다.
///
/// ■ 왜 JSON 을 그대로 서명하지 않는가
///   JSON 직렬화는 구현체마다 공백·필드 순서·숫자 표기·이스케이프가 다르다. NCP(openssl·python 등)와
///   .NET 이 같은 바이트를 만든다는 보장이 없다. 그래서 필드를 정해진 순서로 개행 이어붙인
///   단순 문자열에 서명한다 — 어느 언어로 만들어도 같은 결과가 나온다.
///
/// ■ 무엇을 포함하나
///   Version·Channel·DownloadUrl·Sha256·SizeBytes·RequiresMigration.
///   = "어느 버전을, 어디서 받아, 무엇인지 확인하고, DB 를 건드리는가" — 공격자가 바꿔 이득을 볼 값 전부.
///   Sha256 을 포함하는 게 핵심이다. 이래야 "manifest 를 바꾸면 sha256 도 바꾼다"는 자기참조 고리가 끊긴다
///   (sha256 을 바꾸려면 서명을 다시 만들어야 하고, 그러려면 NCP 개인키가 필요하다).
///
/// ■ 무엇을 뺐나
///   ReleasedAt·ReleaseNotes·ConsentMessage — 표시용 문구다. 바뀌어도 무엇이 설치되는지는 안 변한다.
///   시간·문구는 표기 흔들림(타임존·개행)이 잦아 서명에 넣으면 정상 릴리스가 거부될 위험만 키운다.
///   Signature·Kid — 서명 자신은 서명 대상이 될 수 없다.
///
/// 헌법 정합: #22(개인키는 NCP 에만) / #23(AI 협업 코드 5중 검증) / #34(정식 완성도).
/// </summary>
public static class UpdateManifestSigning
{
    /// <summary>워치독이 신뢰하는 서명 키 식별자. 시리얼 증표 키("v1")와 분리한다(권한 분리).</summary>
    public const string DefaultKid = "upd-v1";

    /// <summary>
    /// 서명 대상 문자열을 만든다. 발급(NCP)·검증(워치독) 양쪽이 반드시 이 규격을 쓴다.
    ///
    /// 규격 (v1):
    ///   hitpan-update-v1\n
    ///   version=&lt;Version&gt;\n
    ///   channel=&lt;Channel 소문자&gt;\n
    ///   url=&lt;DownloadUrl&gt;\n
    ///   sha256=&lt;Sha256 소문자&gt;\n
    ///   size=&lt;SizeBytes&gt;\n
    ///   migration=&lt;true|false&gt;
    ///
    /// 첫 줄 "hitpan-update-v1" = 규격 표식. 다른 용도로 만든 서명이 여기 재사용되는 걸 막고,
    ///   나중에 규격을 바꿀 때 옛 서명과 구분한다.
    /// 대소문자·문화권에 흔들리지 않도록 채널은 소문자, sha256 은 소문자, 숫자는 InvariantCulture 로 고정한다.
    /// </summary>
    public static string BuildSigningPayload(UpdateManifest m)
    {
        var sb = new StringBuilder();
        sb.Append("hitpan-update-v1").Append('\n');
        sb.Append("version=").Append(m.Version).Append('\n');
        sb.Append("channel=").Append(m.Channel.ToString().ToLowerInvariant()).Append('\n');
        sb.Append("url=").Append(m.DownloadUrl).Append('\n');
        sb.Append("sha256=").Append((m.Sha256 ?? string.Empty).ToLowerInvariant()).Append('\n');
        sb.Append("size=").Append(m.SizeBytes.ToString(CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("migration=").Append(m.RequiresMigration ? "true" : "false");
        return sb.ToString();
    }
}
