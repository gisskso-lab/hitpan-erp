using System.Text;
using HitPan.Application.Common;

namespace HitPan.API.Services.Headquarters;

/// <summary>
/// 🔴 작14 B-3 — ERP → 본사(백오피스) **한 송신 모듈** (아키텍처명세서 §5 · CS 쪽지가 첫 사용자).
///
/// <para>클라이언트·인증·재시도 구분을 여기 한 곳에 둔다 — 줄기마다 복제하면 봉합도 두 번
/// (「고쳤다 ≠ 갔다」 자리). 다음 사용자(S-2 대표 아이디 첫 보고 등)도 이 모듈을 쓴다.</para>
///
/// <para>■ 인증 재료 = **테넌트넘버 + 시리얼넘버 + 부모계정** 셋 (사장님 10/8 「3중 일치」 · 교정③)
///   — 백오피스 수신(B-5)이 셋 전부 대조한다. 하나만 맞으면 거부.
///   · 테넌트넘버 = db.conf <c>TENANT_CODE</c>
///   · 시리얼 = db.conf <c>LICENSE_KEY</c> — ⚠️ **이미 발급된 값을 싣기만 한다**(발급 로직 무접촉 ·
///     워치독 MetaPingClient 와 같은 출처). 대조에만 쓰이고 본사는 저장·로깅하지 않는다(TelemetryController 선례).
///   · 부모계정 = 로컬 users 의 is_parent=1 행 아이디(#40 아이디 방식 · tenant당 1명) — 호출자가 넘긴다.</para>
///
/// <para>■ HTTPS API 만 — 본사 DB 직결 금지(기존 OutboxPollerWorker 길을 넓히지 않는다 · 설계 §2-3).
/// ■ 주소: 환경변수/db.conf <c>HITPAN_HQ_API_URL</c> → 기본 back.hitpan.kr (워치독과 같은 집 ·
///   #21 appsettings 무접촉이라 파일 설정을 새로 만들지 않는다).</para>
/// </summary>
public interface IHeadquartersClient
{
    /// <summary>3중 인증 재료를 감싼 봉투로 POST. (상태코드, 응답 본문 일부)를 돌려준다 — 판정은 호출자 몫.</summary>
    Task<(int StatusCode, string Body)> PostAsync(string path, string dataJson, string ownerAccountId, CancellationToken ct);

    /// <summary>3중 인증 재료를 질의문자열 없이 봉투 POST 로 보내는 Pull(답 수신 · B-9). 본문은 수신 목록 JSON.</summary>
    Task<(int StatusCode, string Body)> PullAsync(string path, string ownerAccountId, CancellationToken ct);
}

public sealed class HeadquartersClient : IHeadquartersClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<HeadquartersClient> _logger;

    public HeadquartersClient(IHttpClientFactory httpFactory, ILogger<HeadquartersClient> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    private static string BaseUrl()
        => TenantConfigReader.Get("HITPAN_HQ_API_URL") ?? "https://back.hitpan.kr";

    /// <summary>봉투 — data 는 이미 화이트리스트로 완성된 JSON 문자열(여기서 더 싣지 않는다 · #22).</summary>
    private static string Envelope(string dataJson, string ownerAccountId)
    {
        var tenantCode = TenantConfigReader.Get("TENANT_CODE") ?? "";
        var licenseKey = TenantConfigReader.Get("LICENSE_KEY") ?? "";
        return "{\"tenantCode\":" + System.Text.Json.JsonSerializer.Serialize(tenantCode)
             + ",\"licenseKey\":" + System.Text.Json.JsonSerializer.Serialize(licenseKey)
             + ",\"ownerAccountId\":" + System.Text.Json.JsonSerializer.Serialize(ownerAccountId)
             + ",\"data\":" + dataJson + "}";
    }

    public async Task<(int, string)> PostAsync(string path, string dataJson, string ownerAccountId, CancellationToken ct)
        => await SendAsync(path, Envelope(dataJson, ownerAccountId), ct);

    public async Task<(int, string)> PullAsync(string path, string ownerAccountId, CancellationToken ct)
        => await SendAsync(path, Envelope("{}", ownerAccountId), ct);

    private async Task<(int, string)> SendAsync(string path, string envelopeJson, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient("headquarters");
        http.Timeout = TimeSpan.FromSeconds(15); // 목표(빠른 전달)와 한도(워커 블로킹 방지)는 별개 — 실패는 큐가 품는다(#26 정신)

        using var content = new StringContent(envelopeJson, Encoding.UTF8, "application/json");
        var url = BaseUrl().TrimEnd('/') + path;

        // 🔴 타임아웃·연결 실패는 "5xx 성격"(재시도 대상)으로 돌려준다 — 예외가 워커를 죽이면 안 된다(#15).
        try
        {
            using var res = await http.PostAsync(url, content, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            return ((int)res.StatusCode, body.Length <= 2000 ? body : body[..2000]);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("[HQ] 송신 타임아웃 path={Path}", path);
            return (0, "timeout");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("[HQ] 송신 연결 실패 path={Path} msg={Msg}", path, ex.Message);
            return (0, "connect-fail");
        }
    }
}
