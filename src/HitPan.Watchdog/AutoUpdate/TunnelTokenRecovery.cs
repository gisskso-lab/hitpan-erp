using System.Net.Http.Json;
using System.Text.Json;

namespace HitPan.Watchdog.AutoUpdate;

/// <summary>
/// 터널 토큰 자력 재발급 (작업지시서 20260730작8 P0-4, 사장님 결재 2026-07-30).
///
/// ■ 왜 필요한가 — 토큰을 잃으면 통신이 영구 복구 불가였다
///   WS-28-D(ServiceReinstall)는 db.conf 의 TUNNEL_TOKEN 으로만 터널을 재설치한다.
///   그런데 그 값이 없거나(수동 구성 PC·db.conf 유실) **본사에서 터널을 삭제·재발급해
///   죽은 토큰이 되면**, 종전 코드는 경고 한 줄만 남기고 포기했다:
///     "db.conf 에 TUNNEL_TOKEN 부재 — 토큰 없이 재설치 시도(관리형 터널이면 미복구)"
///   → 관리형 터널(config_src=cloudflare)은 토큰 없이는 절대 안 붙는다.
///   → 고객 PC 는 영구히 1033/502. 워치독이 매 사이클 실패만 반복(헌법 #27·#28 무력).
///   실측 2026-07-30: demo PC 에 cloudflared 서비스 미등록 + db.conf 부재 →
///     토큰이 어디에도 없어 사람이 대시보드를 열지 않으면 복구 불가능한 상태였다.
///
/// ■ 무엇을 하나
///   시리얼(LICENSE_KEY)로 백오피스 installer/bootstrap 을 호출해 **새 터널 토큰을 받는다.**
///   백오피스는 같은 테넌트면 기존 터널을 찾아 토큰을 재발급한다(CloudflareDomainService
///   멱등 처리 — "already exists" 시 name 으로 조회해 같은 tunnelId 재사용).
///   즉 **몇 번 호출해도 터널은 하나로 유지되고 토큰만 새로 나온다.**
///
/// ■ 토큰 만료에 대한 사실 정정 (사장님 지시 "토큰기간을 늘리던지")
///   cloudflared 터널 토큰(관리형)은 **유효기간이 없다.** 그래서 "기간을 늘리는" 대상이 아니다.
///   실제 위험은 만료가 아니라 (a) 값 유실 (b) 본사 재발급으로 옛 값이 무효화 — 두 가지다.
///   ⇒ 기간 연장 대신 **① 영구 보존(db.conf 갱신) + ② 유실·무효 시 자력 재발급** 으로 봉합한다.
///   이게 "자동갱신"의 실질이다.
///
/// ■ 헌법 정합
///   #18·#22 — 본사로 **업무 데이터 0건** 전송. 보내는 것은 시리얼·기기지문뿐(설치 때와 동일).
///   #28·#30 — 고객 손 0번 자가회복. 본사 의존은 '발급'뿐이고 판단·복구는 로컬이 한다.
///   #15 — 실패를 침묵하지 않는다. 모든 분기에 로그를 남긴다.
///   #29 — 인프라를 바꾸지 않는다. 토큰을 '받아오는' 것뿐(서비스 조작은 WS-28-D 가 한다).
/// </summary>
public interface ITunnelTokenRecovery
{
    /// <summary>
    /// 본사에서 터널 토큰을 재발급받아 db.conf 에 저장한다.
    /// 성공 시 새 토큰, 실패 시 null. 실패해도 예외를 던지지 않는다(워치독 사이클 보호).
    /// </summary>
    Task<string?> RecoverAsync(CancellationToken ct);
}

public sealed class TunnelTokenRecovery : ITunnelTokenRecovery
{
    private readonly ILogger<TunnelTokenRecovery> _logger;
    private readonly HttpClient _http;

    public TunnelTokenRecovery(ILogger<TunnelTokenRecovery> logger, IHttpClientFactory httpFactory)
    {
        _logger = logger;
        _http = httpFactory.CreateClient();
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// 백오피스 주소. db.conf 우선(설치 시 기록), 없으면 운영 기본값.
    /// 환경변수로도 덮을 수 있게 둔다(테스트·검증 환경 정합).
    /// </summary>
    private static string BackofficeBaseUrl()
    {
        var v = DbConfReader.GetValue("BACKOFFICE_URL");
        if (string.IsNullOrWhiteSpace(v))
            v = Environment.GetEnvironmentVariable("HITPAN_BACKOFFICE_URL");
        if (string.IsNullOrWhiteSpace(v))
            v = "https://back.hitpan.kr";
        return v.TrimEnd('/');
    }

    public async Task<string?> RecoverAsync(CancellationToken ct)
    {
        // 시리얼이 없으면 재발급 자체가 불가능하다. 이건 설치가 온전하지 않다는 뜻이므로
        //   조용히 넘기지 않고 명시한다(헌법 #15).
        var licenseKey = DbConfReader.GetValue("LICENSE_KEY");
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            _logger.LogWarning(
                "[TunnelRecovery] db.conf 에 LICENSE_KEY 가 없어 토큰 재발급을 할 수 없습니다 — " +
                "설치가 온전하지 않습니다(재설치 필요). 관리형 터널은 토큰 없이 붙지 않습니다.");
            return null;
        }

        var url = $"{BackofficeBaseUrl()}/api/installer/bootstrap";
        try
        {
            // 기기지문: 설치 때와 같은 규칙(컴퓨터명-사용자명)을 쓴다. 기기 슬롯 판정 정합.
            var payload = new
            {
                licenseKey,
                machineFingerprint = $"{Environment.MachineName}-{Environment.UserName}",
                hostname = Environment.MachineName,
                installerVersion = VersionInfo.Current
            };

            using var res = await _http.PostAsJsonAsync(url, payload, ct);
            var body = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
            {
                // 401 = 시리얼 무효/미승인. 그 외 = 본사 장애. 둘을 구분해 남긴다.
                _logger.LogWarning(
                    "[TunnelRecovery] 토큰 재발급 실패 status={Status} — {Hint}",
                    (int)res.StatusCode,
                    res.StatusCode == System.Net.HttpStatusCode.Unauthorized
                        ? "시리얼이 유효하지 않거나 승인되지 않은 계정입니다"
                        : "본사 응답 오류(일시 장애면 다음 사이클에 재시도)");
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("domain", out var domain))
            {
                _logger.LogWarning("[TunnelRecovery] 응답에 domain 이 없습니다 — 백오피스 계약 변경 의심");
                return null;
            }

            var token = domain.TryGetProperty("tunnelToken", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(token))
            {
                // tunnelTokenIssued=false 인 경우. 본사 Cloudflare 설정이 꺼져 있으면 여기로 온다.
                _logger.LogWarning(
                    "[TunnelRecovery] 본사가 터널 토큰을 발급하지 않았습니다(tunnelToken 빈 값) — " +
                    "본사 Cloudflare 설정 확인이 필요합니다. 고객 PC 조작으로는 해결되지 않습니다.");
                return null;
            }

            // 받은 즉시 저장한다 — 저장하지 않으면 다음 사이클에 또 재발급을 요청하게 되고,
            //   WS-28-D 도 여전히 옛 값을 읽는다(오늘 인스톨러에서 같은 사각지대를 봉합했다).
            DbConfWriter.SetValue("TUNNEL_TOKEN", token!);
            if (domain.TryGetProperty("tunnelId", out var tid))
            {
                var tunnelId = tid.GetString();
                if (!string.IsNullOrWhiteSpace(tunnelId))
                    DbConfWriter.SetValue("TUNNEL_ID", tunnelId!);
            }

            _logger.LogInformation(
                "[TunnelRecovery] ✅ 터널 토큰 재발급·저장 완료 (길이 {Len}) — WS-28-D 가 이 토큰으로 재설치합니다.",
                token!.Length);
            return token;
        }
        catch (Exception ex)
        {
            // 네트워크·타임아웃·JSON 오류. 워치독 사이클을 죽이지 않는다(다음 사이클 재시도).
            _logger.LogWarning(ex, "[TunnelRecovery] 토큰 재발급 중 오류 (url={Url})", url);
            return null;
        }
    }
}
