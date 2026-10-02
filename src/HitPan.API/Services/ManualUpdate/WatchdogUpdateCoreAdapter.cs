using HitPan.Watchdog.AutoUpdate;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HitPan.API.Services.LocalRollback;

namespace HitPan.API.Services.ManualUpdate;

/// <summary>
/// 2026-09-30 작1 갈래 U — 워치독 업데이트 코어를 수동 모듈 창구(<see cref="IUpdateFeed"/>·<see cref="IPackageFetcher"/>·
/// <see cref="IAutoUpdateLockProbe"/>)로 감싸는 얇은 어댑터(설계 §13-2·§13-7).
///
/// ■ 무엇을 감싸나 — API csproj 가 링크 컴파일한 워치독 원본(파일 복사 0 · 워치독 파일 수정 0)
///   · <c>UpdateClient.GetLatestManifestAsync</c> — 피드 조회 + 서명 검증(가장 먼저 · fail-closed)
///   · <c>UpdateClient.IsNewerVersion</c> — SemVer 비교(다운그레이드·재생 공격의 마지막 방어선)
///   · <c>UpdateClient.DownloadAsync</c> · <c>VerifySha256Async</c> · <c>UpdateDiskSpaceGuard</c> · <c>UpdateLockFile</c>
///   워치독 형식(<c>UpdateManifest</c> 등)은 이 파일 밖으로 나가지 않는다 — API 쪽 다른 파일은 워치독 이름을 모른다.
///
/// ■ 워치독과의 관계(G-U3)
///   · 워치독 '프로세스'를 부르지 않는다 — 같은 소스를 API 프로세스 안에서 돌릴 뿐이다. 워치독 서비스가 멈춰 있어도 동작한다.
///   · 동의 판독기·주 루프는 링크하지도 부르지도 않는다(자동 경로와 고장점 분리 · 설계 §13-3).
///   · 워치독 DI 에 넣지 않고 여기서 직접 만든다 — API DI 에 워치독 형식이 흩어지지 않게.
///
/// ■ 피드 조회에서 '현재 판'을 0.0.0 으로 넘기는 이유
///   원본 <c>GetLatestManifestAsync</c> 는 ① 서명 실패 ② 판이 같거나 낮음 을 둘 다 null 로 돌려준다. 수동 화면은
///   「이미 최신입니다」와 「새 버전을 확인할 수 없습니다」를 달리 말해야 한다. 그래서 원본에는 비교가 늘 통과하는 0.0.0 을 주고
///   (= null 이면 조회 실패 또는 서명·형식 불량뿐), 판 비교는 그 뒤 원본의 같은 함수 <c>IsNewerVersion</c> 으로 여기서 한다.
///   서명 검증 순서(판 값을 믿기 전에 서명)는 원본 안에서 그대로 지켜진다.
/// </summary>
public sealed class WatchdogUpdateCoreAdapter : IUpdateFeed, IPackageFetcher, IAutoUpdateLockProbe, IPreviousPackageFeed
{
    /// <summary>원본 비교를 늘 통과시키는 판(위 설명). 피드 판이 이 값 이하면 원본이 null 을 준다 — 정상 피드에선 없는 일.</summary>
    private const string PassThroughVersion = "0.0.0";

    private readonly UpdateClient _client;
    private readonly UpdateDiskSpaceGuard _disk;
    private readonly UpdateLockFile _lock;
    private readonly ILogger<WatchdogUpdateCoreAdapter> _logger;

    public WatchdogUpdateCoreAdapter(IHttpClientFactory httpFactory, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<WatchdogUpdateCoreAdapter>();
        var verifier = new UpdateSignatureVerifier(loggerFactory.CreateLogger<UpdateSignatureVerifier>());
        _client = new UpdateClient(httpFactory, loggerFactory.CreateLogger<UpdateClient>(), verifier);
        _disk = new UpdateDiskSpaceGuard(loggerFactory.CreateLogger<UpdateDiskSpaceGuard>());
        _lock = new UpdateLockFile(loggerFactory.CreateLogger<UpdateLockFile>());
        _verifier = verifier;
    }

    // ── 20260930작1 1.3.50 확대 갈래 N1 — 서명 안내 파일 남기기·다시 확인(설계 §19-1 조각 A · §19-3) ─────────────────
    //   위 생성자·필드·CheckAsync 기존 줄은 그대로 두고 덧붙이기만 했다(#1).
    //   · 남기기: CheckAsync 가 서명 통과 manifest 를 쥔 자리(null 검사 뒤)에서 받은 값 그대로 직렬화해 저장본으로 넘긴다.
    //     반환값·판 비교·로그는 그대로 · 남기기 실패는 경고 한 줄로 끝(확인 결과 무영향 · #15 · #30).
    //   · 다시 확인(LoadVerified): 저장본을 피드와 같은 읽기 규칙으로 되읽어 **기존 검증기 Verify** 로 서명을 다시 본다(새 암호 코드 0).

    /// <summary>
    /// 저장본 읽기·쓰기 규칙 = 피드 읽기 규칙(<c>UpdateClient.ManifestJsonOptions</c> · <c>UpdateClient.cs:51-54</c>)과 같은 모양 —
    /// Web 기본값 + 글자 채널(<c>"Major"</c>)·숫자 채널 둘 다. 서버 manifest 원문을 사람이 저장본 폴더에 그대로 넣어도 읽힌다(③ 본사·대리점 대응).
    /// (원본 필드는 private 이라 같은 설정으로 한 벌 둔다 · 쓰기도 이 설정이라 채널이 글자로 남는다.)
    /// </summary>
    private static readonly JsonSerializerOptions KeptManifestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>형식 기준 = 수동 업데이트가 피드 값을 믿는 기준과 같다(<c>ManualUpdateService.cs:107-108</c> · <c>:288-289</c>).</summary>
    private static readonly Regex KeptThreePartVersion = new(@"^\d{1,5}\.\d{1,5}\.\d{1,6}$", RegexOptions.CultureInvariant);
    private static readonly Regex KeptSha256Hex = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);

    private readonly UpdateSignatureVerifier _verifier;
    private readonly SignedManifestKeeper? _keeper;

    /// <summary>
    /// 저장본 담당을 받는 생성자(DI 가 고른다 — 등록된 인자를 가장 많이 채우는 생성자).
    /// 기존 두 인자 생성자는 그대로다(남기기 없이 돈다 · 기존 시험 무수정).
    /// </summary>
    public WatchdogUpdateCoreAdapter(IHttpClientFactory httpFactory, ILoggerFactory loggerFactory, SignedManifestKeeper keeper)
        : this(httpFactory, loggerFactory)
    {
        _keeper = keeper;
        // N6(설계 §19-8) — 첫 회 확인표 받기용. 기존 두 인자 생성자·UpdateClient 무접촉(UpdateClient 는 피드 주소가 환경변수 고정).
        _firstRunHttp = httpFactory;
    }

    // ── 20260930작1 1.3.50 확대 갈래 N6 — 첫 회 NCP 확인표 받기(설계 §19-8 판정 2·3·4 · 작업지시서 18-9 X-12) ─────────────
    //   위 기존 줄은 그대로 두고 덧붙이기만 했다(#1). 코드에 판 숫자 0 · 새 설정 키 0 · 새 사유 0 · 새 상수 1(뒷이름).
    //   · 언제: CheckAsync 가 서명 통과 manifest 를 남긴 직후 한 번 — 그 판 == 지금 판 · 판 이력 「막 태어난 상태」 · 저장본 중 지금 판 아래 0개.
    //   · 주소: 손에 든 지금 판 서명 manifest 의 downloadUrl 마지막 조각이 정확히 「hitpan-{지금 판}.zip」 일 때만 그 조각을
    //     「hitpan-{지금 판}.previous-manifest.json」 으로 바꾼다(호스트·폴더 = 서명된 주소 그대로). 다르면 묻지 않는다(추측 0).
    //   · 요청 모양 = 피드 확인과 같다(기본 클라이언트 GET · 30초 · 쿼리·헤더 0 · #18/#22).
    //   · 검사: 기존 검증기 Verify → 안의 판 P < 지금 판 → 기존 KeepSignedManifest(= Keep(P, …)). 실패 = 남기지 않고 경고/정보 한 줄(결과 무영향 · #15).

    /// <summary>NCP 에 놓인 「지금 판 직전 게시본 확인표」 이름의 뒷부분(설계 §19-8 판정 1).</summary>
    private const string FirstPreviousSuffix = ".previous-manifest.json";

    private readonly IHttpClientFactory? _firstRunHttp;

    /// <summary>
    /// 지금 판 서명 manifest 의 <paramref name="downloadUrl"/> 마지막 조각이 정확히 <c>hitpan-{지금 판}.zip</c> 이면
    /// 그 조각만 <c>hitpan-{지금 판}.previous-manifest.json</c> 으로 바꾼 주소. 아니면 null(묻지 않는다).
    /// </summary>
    private static string? FirstPreviousUrl(string? downloadUrl, Version current)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl) || !Uri.TryCreate(downloadUrl, UriKind.Absolute, out _)) return null;
        var cut = downloadUrl.LastIndexOf('/');
        if (cut < 0) return null;
        var stem = "hitpan-" + RollbackMaterialFinder.Format(current);
        if (!string.Equals(downloadUrl[(cut + 1)..], stem + ".zip", StringComparison.Ordinal)) return null;
        return downloadUrl[..(cut + 1)] + stem + FirstPreviousSuffix;
    }

    /// <summary>첫 회 확인표 받기(위 머리말). 어떤 실패도 경고/정보 한 줄로 끝(확인 결과 무영향).</summary>
    private async Task TryFetchFirstPrevious(UpdateManifest manifest, string currentVersion, CancellationToken ct)
    {
        if (_keeper is null || _firstRunHttp is null) return;
        if (!RollbackMaterialFinder.TryParse(currentVersion, out var current)
            || !RollbackMaterialFinder.TryParse(manifest.Version, out var held) || held != current) return;
        string? url = null;
        try
        {
            if (!_keeper.NeedsFirstRunPrevious(current)) return;
            url = FirstPreviousUrl(manifest.DownloadUrl, current);
            if (url is null)
            {
                _logger.LogInformation("[ManualUpdate] 받는 주소 마지막 조각이 hitpan-{Current}.zip 이 아니라 직전 판 확인표를 묻지 않는다", currentVersion);
                return;
            }

            var http = _firstRunHttp.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            var previous = await http.GetFromJsonAsync<UpdateManifest>(url, KeptManifestJsonOptions, ct).ConfigureAwait(false);
            if (previous is null)
            {
                _logger.LogWarning("[ManualUpdate] 직전 판 확인표 응답이 비었다 — 남기지 않는다");
                return;
            }
            // 서명이 먼저 — 판을 믿기 전에(LoadVerified 와 같은 순서). 사유는 Verify 가 이미 남겼다.
            if (!_verifier.Verify(previous))
            {
                _logger.LogWarning("[ManualUpdate] 직전 판 확인표 서명 불량 — 남기지 않는다");
                return;
            }
            if (!RollbackMaterialFinder.TryParse(previous.Version, out var p) || p >= current)
            {
                _logger.LogWarning("[ManualUpdate] 직전 판 확인표 안의 판({Inside})이 지금 판({Current}) 아래가 아니다 — 남기지 않는다", previous.Version, currentVersion);
                return;
            }
            KeepSignedManifest(previous);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation("[ManualUpdate] 직전 판 확인표가 없다(404) — 지금과 같이 되돌릴 이전 버전 없음");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ManualUpdate] 직전 판 확인표를 받지 못했다({Url}) — 확인 결과에는 영향 없음", url);
        }
    }

    /// <summary>서명 통과 manifest 를 저장본으로 남긴다. 어떤 실패도 경고 한 줄로 끝(부르는 쪽 결과 무영향).</summary>
    private void KeepSignedManifest(UpdateManifest manifest)
    {
        if (_keeper is null) return;
        try
        {
            var json = JsonSerializer.Serialize(manifest, KeptManifestJsonOptions);
            _keeper.Keep(manifest.Version, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ManualUpdate] 서명 확인된 안내 파일을 남기지 못했다({Version}) — 확인 결과에는 영향 없음", manifest.Version);
        }
    }

    /// <inheritdoc />
    public FeedPackage? LoadVerified(string version)
    {
        if (_keeper is null)
        {
            _logger.LogWarning("[ManualUpdate] 저장본 담당이 없어 {Version} 판 안내 파일을 확인하지 않는다", version);
            return null;
        }
        if (!RollbackMaterialFinder.TryParse(version, out var wanted))
        {
            _logger.LogWarning("[ManualUpdate] 요청 판({Version})이 M.m.b 로 읽히지 않아 저장본을 보지 않는다", version);
            return null;
        }

        var json = _keeper.Read(version);
        if (json is null)
        {
            _logger.LogWarning("[ManualUpdate] {Version} 판 안내 파일 저장본이 없다", version);
            return null;
        }

        UpdateManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<UpdateManifest>(json, KeptManifestJsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[ManualUpdate] {Version} 판 안내 파일 저장본 형식 불량 — 쓰지 않는다", version);
            return null;
        }
        if (manifest is null)
        {
            _logger.LogWarning("[ManualUpdate] {Version} 판 안내 파일 저장본이 비었다 — 쓰지 않는다", version);
            return null;
        }

        // 서명이 먼저 — 판·주소·해시를 믿기 전에(원본 GetLatestManifestAsync 와 같은 순서). 사유는 Verify 가 이미 남겼다.
        if (!_verifier.Verify(manifest))
        {
            _logger.LogWarning("[ManualUpdate] {Version} 판 안내 파일 저장본 서명 불량 — 쓰지 않는다", version);
            return null;
        }
        if (!RollbackMaterialFinder.TryParse(manifest.Version, out var inside) || inside != wanted)
        {
            _logger.LogWarning("[ManualUpdate] 저장본 안의 판({Inside})이 요청 판({Version})과 다르다 — 쓰지 않는다", manifest.Version, version);
            return null;
        }
        if (manifest.Version is null || !KeptThreePartVersion.IsMatch(manifest.Version)
            || manifest.Sha256 is null || !KeptSha256Hex.IsMatch(manifest.Sha256)
            || string.IsNullOrWhiteSpace(manifest.DownloadUrl))
        {
            _logger.LogWarning("[ManualUpdate] {Version} 판 안내 파일 저장본 값 형식 불량(판·해시·주소) — 쓰지 않는다", version);
            return null;
        }

        return new FeedPackage(
            manifest.Version,
            manifest.Channel.ToString(),
            manifest.DownloadUrl,
            manifest.Sha256,
            manifest.SizeBytes,
            manifest.ReleaseNotes);
    }

    /// <summary>워치독 원본 SemVer 비교 그대로(3자리 정규화 · 판정 불능 = false). 서비스가 한 번 더 확인할 때 쓴다.</summary>
    public static bool IsNewerVersion(string feedVersion, string currentVersion, out string? reason)
        => UpdateClient.IsNewerVersion(feedVersion, currentVersion, out reason);

    public async Task<FeedCheckResult> CheckAsync(string currentVersion, CancellationToken ct)
    {
        var manifest = await _client.GetLatestManifestAsync(PassThroughVersion, ct).ConfigureAwait(false);
        if (manifest is null)
        {
            if (_client.LastFetchFailed)
                return new FeedCheckResult(FeedCheckStatus.Unreachable, null, "피드 조회 실패");

            // 닿았는데 null = 빈 매니페스트 · 서명 없음/불일치 · 판 형식 불량. 사유는 원본이 이미 로그로 남겼다(#15).
            _logger.LogWarning("[ManualUpdate] 피드 매니페스트를 받아들이지 않았다(서명·형식) — 다운로드하지 않는다");
            return new FeedCheckResult(FeedCheckStatus.Invalid, null, "서명·형식 불량");
        }

        // 20260930작1 1.3.50 확대 N1 — 여기 = 서명 통과 manifest 를 쥔 자리. Newer·NotNewer 모두 남긴다(실패는 경고 한 줄 · 결과 무영향).
        KeepSignedManifest(manifest);
        // N6(설계 §19-8) — 첫 회만: 판 이력이 막 태어났고 저장본에 지금 판 아래가 없으면 직전 판 확인표를 한 번 받는다(결과 무영향).
        await TryFetchFirstPrevious(manifest, currentVersion, ct).ConfigureAwait(false);

        if (!UpdateClient.IsNewerVersion(manifest.Version, currentVersion, out var reason))
        {
            _logger.LogInformation("[ManualUpdate] 새 판 아님 — {Reason}", reason);
            return new FeedCheckResult(FeedCheckStatus.NotNewer, null, reason);
        }

        var package = new FeedPackage(
            manifest.Version,
            manifest.Channel.ToString(),
            manifest.DownloadUrl,
            manifest.Sha256,
            manifest.SizeBytes,
            manifest.ReleaseNotes);
        return new FeedCheckResult(FeedCheckStatus.Newer, package, null);
    }

    public bool HasEnoughSpace(long packageSizeBytes, string targetDir)
        => _disk.HasEnoughSpace(packageSizeBytes, targetDir);

    public Task<string> DownloadAsync(FeedPackage package, string targetDir, CancellationToken ct)
    {
        // 원본 DownloadAsync 는 Version(파일 이름)·DownloadUrl 만 쓴다. 값은 같은 요청 안에서 서명 검증을 통과한 매니페스트에서 왔다.
        var manifest = new UpdateManifest(
            package.Version,
            Enum.TryParse<UpdateChannel>(package.Channel, out var ch) ? ch : UpdateChannel.Normal,
            package.DownloadUrl,
            package.Sha256,
            package.SizeBytes,
            DateTime.MinValue,
            package.ReleaseNotes,
            RequiresMigration: false,
            ConsentMessage: null);
        return _client.DownloadAsync(manifest, targetDir, ct);
    }

    public Task<bool> VerifySha256Async(string filePath, string expectedSha256, CancellationToken ct)
        => _client.VerifySha256Async(filePath, expectedSha256, ct);

    public bool IsAutoUpdateInProgress() => _lock.IsUpdateInProgress();
}
