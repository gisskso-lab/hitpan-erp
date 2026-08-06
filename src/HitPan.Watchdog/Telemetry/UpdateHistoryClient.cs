using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Authentication;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace HitPan.Watchdog.Telemetry;

/// <summary>
/// 업데이트 결과를 본사 백오피스에 보고한다 (20260806작4 — 사장님 오더 ③).
///
/// ■ 왜 필요한가 (사장님 2026-07-20 §2, CS 첫 도구)
///   "고객한테 '몇 버전 쓰세요?' 물으면 그 확인 자체가 CS 업무가 됨. 고객은 자기 버전 모름."
///   CS팀장 강지원: "로컬 ERP는 '업데이트했다'가 '업데이트됐다'를 보장 안 한다.
///                  실패 여부 없으면 CS가 눈 감고 일한다."
///
/// ■ 🔴 절대 원칙 — 이 클래스의 어떤 실패도 고객 업데이트를 막지 않는다 (헌법 #20·#30)
///   본사가 죽어도, 네트워크가 끊겨도, 인증이 틀려도 고객 PC 의 업데이트는 완주해야 한다.
///   그래서 fire-and-forget 이고, 모든 예외를 삼키되 로그는 남긴다(헌법 #15).
///   ⚠️ 재시도(Polly)를 붙이지 않는다 — 재시도가 업데이트 흐름을 붙잡으면 그 자체가 위반이다.
///     놓친 보고는 다음 업데이트 때 현황(current_version)으로 자연히 따라잡힌다.
///
/// ■ 헌법 #18·#22 — 운영 메타데이터만 보낸다
///   보낸다   : 버전·채널·성공/실패·시각
///   안 보낸다 : 거래처·상품·금액·직원명·DB 내용 등 업무 데이터 일체
///   라이선스 키는 **인증 대조용**으로만 보낸다(TLS 1.2+). 본사도 저장하지 않는다.
/// </summary>
public class UpdateHistoryClient
{
    private readonly ILogger<UpdateHistoryClient> _logger;
    private readonly WatchdogOptions _options;
    private readonly HttpClient _http;

    public UpdateHistoryClient(ILogger<UpdateHistoryClient> logger, IOptions<WatchdogOptions> options)
    {
        _logger = logger;
        _options = options.Value;

        // MetaPingClient 와 동일한 TLS 정책 — 평문·구버전 프로토콜 금지.
        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12
            }
        };
        // 짧게 끊는다. 본사가 느리다고 고객 쪽에서 오래 붙들고 있을 이유가 없다.
        //   ★ [3-V] 병렬검증 P0 봉합으로 10초 → 5초. 호출부가 await 를 걷어내 흐름과 분리됐지만,
        //     그래도 짧은 편이 낫다 — 이 보고는 놓쳐도 다음 업데이트의 현황(current_version)이
        //     따라잡는다. 오래 붙들어서 얻는 것이 없다.
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
    }

    /// <summary>
    /// 업데이트 결과 1건 보고. 실패해도 절대 예외를 밖으로 던지지 않는다.
    /// </summary>
    /// <param name="result">success | failed | rejected</param>
    public async Task ReportAsync(
        string? fromVersion, string? toVersion, string? channel,
        string result, string? failureReason, CancellationToken ct = default)
    {
        var endpoint = _options.UpdateHistoryEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            _logger.LogDebug("[UpdateHistory] 엔드포인트 미설정 — 보고 건너뜀.");
            return;
        }

        try
        {
            var licenseKey = DbConfReader.GetValue("LICENSE_KEY")
                             ?? Environment.GetEnvironmentVariable("HITPAN_LICENSE_KEY", EnvironmentVariableTarget.Machine)
                             ?? Environment.GetEnvironmentVariable("HITPAN_LICENSE_KEY")
                             ?? "";

            if (string.IsNullOrWhiteSpace(licenseKey))
            {
                // 라이선스가 없으면 본사가 신원을 확인할 수 없다. 보내봐야 401 이라 시도하지 않는다.
                _logger.LogWarning("[UpdateHistory] 라이선스 키 없음 — 본사 보고 생략(업데이트에는 영향 없음).");
                return;
            }

            // ★ [3-V] 병렬검증 P1 봉합: 식별자를 모르면 보내지 않는다.
            //   GetTenantId() 는 db.conf·env 를 못 읽으면 "unknown" 을 준다. 그 해시는
            //   **모든 PC 에서 같은 상수**라, 본사 현황 테이블(PK=tenant_id_hash)에서
            //   여러 고객사가 한 행을 서로 덮어쓴다 — CS 가 남의 고객사 버전을 보게 된다.
            //   라이선스는 각자 유효해 401 로도 안 걸러지므로 여기서 막는다.
            var tenantId = MetaPingClient.GetTenantId();
            if (string.Equals(tenantId, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("[UpdateHistory] 고객사 식별자를 읽지 못했습니다(db.conf) — 본사 보고 생략(업데이트에는 영향 없음).");
                return;
            }

            var payload = new UpdateHistoryPayload
            {
                LicenseKey = licenseKey,
                TenantIdHash = MetaPingClient.Sha256(tenantId),
                FromVersion = fromVersion,
                ToVersion = toVersion,
                Channel = channel,
                Result = result,
                FailureReason = failureReason,
                OccurredAt = DateTime.UtcNow
            };

            using var r = await _http.PostAsJsonAsync(endpoint, payload, ct).ConfigureAwait(false);
            if (r.IsSuccessStatusCode)
                _logger.LogInformation("[UpdateHistory] 본사 보고 완료 — {From} → {To} / {Result}", fromVersion, toVersion, result);
            else
                // 실패해도 업데이트는 이미 끝났다. 기록만 남긴다.
                _logger.LogWarning("[UpdateHistory] 본사 보고 실패 HTTP {Code} — 업데이트에는 영향 없습니다.", (int)r.StatusCode);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 서비스 종료 중. 정상 상황이라 경고로 올리지 않는다.
            _logger.LogDebug("[UpdateHistory] 보고 취소됨(서비스 종료).");
        }
        catch (Exception ex)
        {
            // 헌법 #15 — 침묵 금지. 단 절대 던지지 않는다(헌법 #20·#30).
            _logger.LogWarning(ex, "[UpdateHistory] 본사 보고 중 오류 — 업데이트에는 영향 없습니다.");
        }
    }

    /// <summary>
    /// 워치독 → 백오피스 보고 본문.
    /// ⚠️ LicenseKey 는 인증 대조용이다. 본사는 HMAC 대조에만 쓰고 저장하지 않는다(작4 §3-5).
    /// </summary>
    private class UpdateHistoryPayload
    {
        [JsonPropertyName("licenseKey")]
        public string LicenseKey { get; set; } = "";

        [JsonPropertyName("tenantIdHash")]
        public string TenantIdHash { get; set; } = "";

        [JsonPropertyName("fromVersion")]
        public string? FromVersion { get; set; }

        [JsonPropertyName("toVersion")]
        public string? ToVersion { get; set; }

        [JsonPropertyName("channel")]
        public string? Channel { get; set; }

        [JsonPropertyName("result")]
        public string? Result { get; set; }

        [JsonPropertyName("failureReason")]
        public string? FailureReason { get; set; }

        [JsonPropertyName("occurredAt")]
        public DateTime? OccurredAt { get; set; }
    }
}
