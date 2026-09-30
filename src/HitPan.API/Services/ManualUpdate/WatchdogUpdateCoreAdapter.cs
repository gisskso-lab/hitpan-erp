using HitPan.Watchdog.AutoUpdate;

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
public sealed class WatchdogUpdateCoreAdapter : IUpdateFeed, IPackageFetcher, IAutoUpdateLockProbe
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
