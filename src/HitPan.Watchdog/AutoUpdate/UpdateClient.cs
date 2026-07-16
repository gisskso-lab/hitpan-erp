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

            // 봉합 (2026-07-16, 작1 W4-0 — 사장님 결재, CTO 적발): 종전엔 string.Equals 로 "다르면 새 버전"이었다.
            //   그래서 구버전 manifest 를 물리면 다운그레이드가 "새 버전"으로 통과했다(롤백 공격).
            //   서명(고리4)이 붙어도 이 구멍은 남는다 — 과거에 정식 서명된 옛 manifest 를 통째로 재생(replay)하면
            //   서명·sha256 이 전부 유효하기 때문이다. 그 경우 SemVer 비교가 유일한 방어선이다.
            if (!IsNewerVersion(manifest.Version, currentVersion, out var skipReason))
            {
                // 헌법 #15: 침묵 금지. "최신 유지"는 정상이라 Debug, 다운그레이드·형식오류는 Warning 으로 흔적을 남긴다.
                var isRoutine = skipReason?.StartsWith("최신 버전 유지") == true;
                if (isRoutine) _logger.LogDebug("[Update] {Reason}", skipReason);
                else _logger.LogWarning("[Update] 업데이트를 진행하지 않습니다 — {Reason}", skipReason);
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

    /// <summary>
    /// feed 버전이 현재 설치 버전보다 진짜 "더 높은지" 판정한다 (작1 W4-0, 2026-07-16).
    ///
    /// ■ 왜 문자열 비교가 아니라 SemVer 인가
    ///   "다르면 새 버전" 규칙은 다운그레이드를 막지 못한다. 공격자가 feed 를 장악하거나
    ///   (HITPAN_UPDATE_FEED 환경변수로 재지정 가능) 과거에 정식 서명된 옛 manifest 를 통째로
    ///   재생(replay)하면 서명·sha256 이 전부 유효해 서명 검증을 통과한다.
    ///   그때 "옛 버전으로 되돌리는 것"을 막는 유일한 방어선이 이 비교다
    ///   (취약한 구버전으로 내린 뒤 그 취약점을 치는 공격을 차단).
    ///
    /// ■ 판정 불능 시 = false (fail-closed, 업데이트 진입 금지)
    ///   버전 문자열이 파싱 불가면 "새 버전인지 알 수 없다"는 뜻이고, 알 수 없을 때
    ///   코드를 교체하는 쪽으로 기울면 안 된다. 침묵하지 않고 경고를 남긴다(헌법 #15).
    ///
    /// public static 인 이유: 이 판정이 다운그레이드·replay 의 유일한 방어선이라 단위 테스트로 고정해야 한다
    ///   (HTTP 를 태우면 느리고 불안정해 정작 이 규칙이 검증되지 않는다). 순수 함수 — 상태·부수효과 없음.
    /// </summary>
    /// <param name="reason">판정 사유(로그·테스트용). 통과 시 null.</param>
    public static bool IsNewerVersion(string feedVersion, string currentVersion, out string? reason)
    {
        if (!TryParseThreePart(feedVersion, out var feed))
        {
            reason = $"manifest 버전 형식을 해석할 수 없습니다: '{feedVersion}'";
            return false;
        }
        if (!TryParseThreePart(currentVersion, out var current))
        {
            reason = $"현재 설치 버전 형식을 해석할 수 없습니다: '{currentVersion}'";
            return false;
        }

        if (feed < current)
        {
            reason = $"feed 가 더 낮은 버전을 가리킵니다 (현재 {currentVersion} > feed {feedVersion}) — 다운그레이드 차단";
            return false;
        }
        if (feed == current)
        {
            reason = $"최신 버전 유지 ({currentVersion})";
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// 버전 문자열을 Major.Minor.Build 3자리로 정규화해 파싱한다 (2026-07-16, 작1 W4-0 — 검증팀 F-4 적발).
    ///
    /// 왜 정규화가 필요한가:
    ///   Version.TryParse 는 자릿수를 그대로 보존해 "1.2.34" 는 Revision=-1, "1.2.34.0" 은 Revision=0 이 된다.
    ///   그래서 0 > -1 이 되어 같은 버전인 "1.2.34.0"(어셈블리 표기) 이 "1.2.34"(설치 버전)보다
    ///   높다고 판정됐다 — manifest 에 어셈블리 버전을 그대로 복사해 넣으면 이미 최신인 PC 가
    ///   같은 버전을 무한 재적용한다. Directory.Build.props 가 AssemblyVersion 을 x.y.z.0 으로 굽기 때문에
    ///   현실적으로 밟기 쉬운 함정이다.
    ///   우리 버전 체계는 Major.Minor.Build 3자리이므로(VersionInfo.Current), Revision 은 의미 없는 자리다.
    ///   비교 전에 3자리로 잘라 "표기 차이"가 "버전 차이"로 둔갑하지 않게 한다.
    ///
    /// 2자리("1.2")도 Version 은 파싱하지만 Build=-1 이 되므로 0 으로 보정한다("1.2" == "1.2.0").
    /// </summary>
    private static bool TryParseThreePart(string? text, out Version normalized)
    {
        normalized = new Version(0, 0, 0);
        if (!Version.TryParse(text, out var v)) return false;

        // Build 가 미지정(-1)이면 0 으로. Revision 은 버린다.
        normalized = new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        return true;
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
