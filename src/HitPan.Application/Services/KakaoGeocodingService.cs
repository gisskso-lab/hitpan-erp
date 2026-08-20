using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Dapper;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// 카카오 로컬 API 주소검색으로 주소 → 좌표 변환 (사장님 결재 2026-08-21).
///
/// 왜 이 서비스가 필요했나:
///   카카오맵·카카오내비 딥링크는 좌표(lat,lng)를 요구한다. 종전 코드는 좌표 자리에
///   주소 문자열을 넣었고, 파싱이 실패해 지도가 해당 위치가 아닌 기본 위치로 열렸다.
///   (사장님 지적: "맵이 뜨긴 하지만 실제 해당주소 좌표가 안찍힘")
///   우편번호 서비스는 좌표를 주지 않으므로(공식 문서 필드 확인) 별도 변환이 필요하다.
///
/// §#18·#22 고객사 PC 가 직접 호출한다. 본사 서버를 경유하지 않는다.
/// §#20  실패해도 예외를 던지지 않는다. 좌표는 부가정보이고 업체 저장은 되어야 한다.
/// §#5   API 키는 AES256 암호화 보관.
/// </summary>
public sealed class KakaoGeocodingService : IGeocodingService
{
    private const string SearchUrl = "https://dapi.kakao.com/v2/local/search/address.json";

    // HttpClient 는 IHttpClientFactory 가 만든 것을 API 계층에서 주입한다
    //   (Application 프로젝트에 HTTP 패키지 의존을 추가하지 않기 위함 — 소켓 고갈은 팩토리가 막는다)
    private readonly HttpClient _http;
    private readonly IDbConnection _db;
    private readonly IPasswordEncryptor _enc;
    private readonly ICurrentTenant _currentTenant;
    private readonly ILogger<KakaoGeocodingService> _logger;

    public KakaoGeocodingService(
        HttpClient http,
        IDbConnection db,
        IPasswordEncryptor enc,
        ICurrentTenant currentTenant,
        ILogger<KakaoGeocodingService> logger)
    {
        _http = http; _db = db; _enc = enc;
        _currentTenant = currentTenant; _logger = logger;
    }

    /// <summary>
    /// 동기 속성이라 DB 조회를 할 수 없다. GeocodeAsync 안에서 실제 키 유무로 판정하므로
    /// 여기서는 낙관값을 돌려주고, 진짜 판정은 변환 시점의 NotConfigured 로 내려간다.
    /// </summary>
    public bool IsConfigured => true;

    public async Task<GeocodeResult> GeocodeAsync(string address, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(address))
            return new GeocodeResult { Success = false, Error = "주소가 비어 있습니다." };

        var apiKey = await LoadApiKeyAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // 🔴 "못 찾았다" 가 아니라 "찾아볼 수단이 없다" — 구분해서 알린다.
            //    여기서 거짓 좌표(0,0 등)를 만들어내면 지도가 엉뚱한 곳을 가리킨다.
            return new GeocodeResult
            {
                Success = false,
                NotConfigured = true,
                Error = "좌표 변환이 설정되지 않았습니다. 설정 › 지도 설정에서 등록하세요."
            };
        }

        try
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Get, $"{SearchUrl}?query={Uri.EscapeDataString(address)}&size=1");
            req.Headers.Authorization = new AuthenticationHeaderValue("KakaoAK", apiKey);

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Geocode] 카카오 응답 실패 status={Status} addr={Addr}",
                    (int)resp.StatusCode, address);
                return new GeocodeResult
                {
                    Success = false,
                    Error = resp.StatusCode == System.Net.HttpStatusCode.Unauthorized
                        ? "좌표 변환 키가 올바르지 않습니다. 설정을 확인하세요."
                        : "좌표를 가져오지 못했습니다. 잠시 후 다시 시도하세요."
                };
            }

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("documents", out var docs)
                || docs.ValueKind != JsonValueKind.Array
                || docs.GetArrayLength() == 0)
            {
                // 주소는 멀쩡한데 카카오가 못 찾는 경우가 있다 (신축·상세주소 등).
                // 실패로 두고 주소 폴백이 받게 한다.
                return new GeocodeResult { Success = false, Error = "이 주소의 좌표를 찾지 못했습니다." };
            }

            var first = docs[0];
            // 카카오 응답은 x=경도, y=위도 (뒤바뀌기 쉬운 자리다 — 주의)
            if (!first.TryGetProperty("x", out var xEl) || !first.TryGetProperty("y", out var yEl))
                return new GeocodeResult { Success = false, Error = "좌표 정보를 읽지 못했습니다." };

            if (!decimal.TryParse(xEl.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lng)
                || !decimal.TryParse(yEl.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat))
                return new GeocodeResult { Success = false, Error = "좌표 형식을 읽지 못했습니다." };

            string? matched = null;
            if (first.TryGetProperty("road_address", out var road) && road.ValueKind == JsonValueKind.Object
                && road.TryGetProperty("address_name", out var roadName))
                matched = roadName.GetString();
            else if (first.TryGetProperty("address_name", out var addrName))
                matched = addrName.GetString();

            return new GeocodeResult
            {
                Success = true,
                Latitude = lat,
                Longitude = lng,
                MatchedAddress = matched
            };
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("[Geocode] 시간초과 addr={Addr}", address);
            return new GeocodeResult { Success = false, Error = "좌표 조회가 지연되어 중단했습니다." };
        }
        catch (Exception ex)
        {
            // 좌표 실패로 업체 저장이 막히면 안 된다 (§#20). 로그만 남기고 실패 반환 (§#15).
            _logger.LogWarning(ex, "[Geocode] 변환 실패 addr={Addr}", address);
            return new GeocodeResult { Success = false, Error = "좌표를 가져오지 못했습니다." };
        }
    }

    private async Task<string?> LoadApiKeyAsync(CancellationToken ct)
    {
        var tenantId = _currentTenant.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId)) return null;

        if (_db.State != ConnectionState.Open && _db is DbConnection dbc)
            await dbc.OpenAsync(ct).ConfigureAwait(false);

        var enc = await _db.QueryFirstOrDefaultAsync<byte[]?>(new CommandDefinition(
            "SELECT api_key_enc FROM geocoding_settings WHERE tenant_id=@T AND is_active=1",
            new { T = tenantId }, cancellationToken: ct)).ConfigureAwait(false);

        if (enc is not { Length: > 0 }) return null;

        try { return _enc.Decrypt(enc); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Geocode] 키 복호화 실패 tenant={Tenant}", tenantId);
            return null;
        }
    }
}
