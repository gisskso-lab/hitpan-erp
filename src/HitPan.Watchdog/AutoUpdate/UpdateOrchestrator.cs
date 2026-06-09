namespace HitPan.Watchdog.AutoUpdate;

// 사장님 결재 2026-06-09 (Plan cicd-velvety-reef Day 11~12)
//
// 채널별 적용 정책 (결재 4 — 메이저 업데이트 A안):
//   Emergency: 5분 안내 → 즉시 다운로드 + 적용
//   Normal:    매일 새벽 3시 자동 다운로드 + 적용 (서비스 재기동)
//   Major:     ERP 화면에 동의 요청 → 동의 후 영업시간 외 예약
//              동의 무응답 시 90일 옛 버전 유지 → 30일 추가 알림 → CS 직접 연락
//
// 헌법 정합:
//   #25 — 쉽게·정확하게·안전하게
//   #28·#30 — 고객 손 0번
//   #34 — 베타부터 정식 완성도
public sealed class UpdateOrchestrator
{
    private readonly IUpdateClient _client;
    private readonly ILogger<UpdateOrchestrator> _logger;
    private readonly string _stagingDir;

    public UpdateOrchestrator(IUpdateClient client, ILogger<UpdateOrchestrator> logger)
    {
        _client = client;
        _logger = logger;
        _stagingDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HitPan", "Updates", "staging");
    }

    public async Task<UpdateDecision> EvaluateAsync(string currentVersion, CancellationToken ct)
    {
        var manifest = await _client.GetLatestManifestAsync(currentVersion, ct);
        if (manifest is null)
            return new UpdateDecision(UpdateAction.None, null, null);

        return manifest.Channel switch
        {
            UpdateChannel.Emergency => new UpdateDecision(UpdateAction.AnnounceThenApply, manifest, TimeSpan.FromMinutes(5)),
            UpdateChannel.Normal => new UpdateDecision(UpdateAction.ApplyAtNight, manifest, null),
            UpdateChannel.Major => new UpdateDecision(UpdateAction.RequireConsent, manifest, null),
            _ => new UpdateDecision(UpdateAction.None, null, null)
        };
    }

    public async Task<bool> DownloadAndVerifyAsync(UpdateManifest manifest, CancellationToken ct)
    {
        try
        {
            var path = await _client.DownloadAsync(manifest, _stagingDir, ct);
            var ok = await _client.VerifySha256Async(path, manifest.Sha256, ct);
            if (!ok)
            {
                File.Delete(path);
                _logger.LogError("[Update] 검증 실패 — 다운로드 파일 폐기 ({V})", manifest.Version);
                return false;
            }
            _logger.LogInformation("[Update] 다운로드+검증 완료: {V}", manifest.Version);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Update] 다운로드 실패: {V}", manifest.Version);
            return false;
        }
    }

    public bool IsNightWindow(DateTime now)
        => now.Hour >= 3 && now.Hour < 4;

    public bool IsBusinessHour(DateTime now)
        => now.Hour >= 9 && now.Hour < 18 && now.DayOfWeek != DayOfWeek.Saturday && now.DayOfWeek != DayOfWeek.Sunday;
}

public enum UpdateAction
{
    None,
    AnnounceThenApply,   // Emergency
    ApplyAtNight,        // Normal
    RequireConsent       // Major
}

public sealed record UpdateDecision(UpdateAction Action, UpdateManifest? Manifest, TimeSpan? AnnounceDelay);
