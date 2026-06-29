using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HitPan.Watchdog.AutoUpdate;

// 사장님 결재 2026-06-09 (결재 3: NCP updates.hitpan.kr 채택)
//
// 본사 업데이트 서버에서 manifest.json + 패키지 다운로드 + sha256 검증
//
// 흐름:
//   1) GET https://updates.hitpan.kr/manifest.json → 최신 매니페스트
//   2) 채널 분기 (Emergency/Normal/Major)
//   3) 패키지 다운로드 + sha256 검증 (헌법 #23 5중 검증)
//   4) Major = 고객 동의 요청, Normal/Emergency = 자동 적용
//
// 헌법 정합:
//   #22 — 본사가 받는 영역 0건 (다운로드만, 고객 데이터 전송 0)
//   #23 — sha256 검증 = 5중 검증 ③ SAST 정합
//   #28 — 자동 봉합 (다운로드 실패 시 재시도 5단계)
public interface IUpdateClient
{
    Task<UpdateManifest?> GetLatestManifestAsync(string currentVersion, CancellationToken ct);
    Task<string> DownloadAsync(UpdateManifest manifest, string targetDir, CancellationToken ct);
    Task<bool> VerifySha256Async(string filePath, string expectedHash, CancellationToken ct);
}

public sealed class UpdateClient : IUpdateClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<UpdateClient> _logger;
    private readonly string _feedUrl;

    // 봉합 (2026-06-29, 작1 고리1 — 마이클 채널 직렬화 발견):
    //   종전 GetFromJsonAsync 는 옵션 없이(JsonSerializerDefaults.Web) 호출돼 manifest 의 channel 이
    //   enum 인덱스 '정수'(0=Emergency/1=Normal/2=Major)로만 역직렬화됐다. 그래서 사람이 읽기 쉬운
    //   문자열("channel":"Normal")로 쓴 installer/updates/manifest-sample.json 은 JsonException 으로 실패했다.
    //   JsonStringEnumConverter 를 등록하면 '문자열'과 '정수'를 둘 다 받는다(numeric value 도 허용 — 실측 호환).
    //   → 운영 manifest-sample.json(문자열)·테스트 update-feed 샘플(정수) 모두 안전하게 동작. 일관성을 위해
    //   샘플은 문자열로 통일 권장하되, 둘 다 받으므로 기존 정수 샘플도 무손상(헌법 #1 추가만).
    private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public UpdateClient(IHttpClientFactory httpFactory, ILogger<UpdateClient> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _feedUrl = Environment.GetEnvironmentVariable("HITPAN_UPDATE_FEED")
                   ?? "https://updates.hitpan.kr";
    }

    public async Task<UpdateManifest?> GetLatestManifestAsync(string currentVersion, CancellationToken ct)
    {
        try
        {
            var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            var manifest = await http.GetFromJsonAsync<UpdateManifest>(
                $"{_feedUrl}/manifest.json", ManifestJsonOptions, ct);

            if (manifest is null)
            {
                _logger.LogWarning("[Update] manifest 응답이 비어있음");
                return null;
            }

            if (string.Equals(manifest.Version, currentVersion, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("[Update] 최신 버전 유지 ({V})", currentVersion);
                return null;
            }

            _logger.LogInformation("[Update] 새 버전 발견: {Cur} → {New} (채널: {Ch})",
                currentVersion, manifest.Version, manifest.Channel);
            return manifest;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Update] manifest 조회 실패 (feed={Feed})", _feedUrl);
            return null;
        }
    }

    public async Task<string> DownloadAsync(UpdateManifest manifest, string targetDir, CancellationToken ct)
    {
        Directory.CreateDirectory(targetDir);
        var fileName = $"hitpan-{manifest.Version}.zip";
        var targetPath = Path.Combine(targetDir, fileName);

        var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(30);

        using var response = await http.GetAsync(manifest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var fs = File.Create(targetPath);
        await response.Content.CopyToAsync(fs, ct);

        _logger.LogInformation("[Update] 다운로드 완료: {Path} ({Size:N0} bytes)", targetPath, new FileInfo(targetPath).Length);
        return targetPath;
    }

    public async Task<bool> VerifySha256Async(string filePath, string expectedHash, CancellationToken ct)
    {
        try
        {
            await using var fs = File.OpenRead(filePath);
            using var sha = SHA256.Create();
            var hash = await sha.ComputeHashAsync(fs, ct);
            var actual = Convert.ToHexString(hash).ToLowerInvariant();
            var expected = expectedHash.ToLowerInvariant();
            var match = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

            if (!match)
                _logger.LogError("[Update] sha256 불일치: expected={Exp} actual={Act}", expected, actual);
            return match;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Update] sha256 검증 실패: {Path}", filePath);
            return false;
        }
    }
}
