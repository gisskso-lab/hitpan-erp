namespace HitPan.Watchdog.AutoUpdate;

/// <summary>
/// Phase 2 검토: Velopack 자동 업데이트 통합 PoC stub.
/// 실제 통합 시 NuGet `Velopack` 패키지 추가 + 본사 업데이트 서버(`https://updates.hitpan.kr`) 저장.
///
/// 통합 시퀀스:
/// 1. csproj에 PackageReference Include="Velopack" Version="0.0.x"
/// 2. 본사 빌드 파이프라인: `vpk pack -i HitPan.Watchdog -v 1.1.0 -o ./releases`
/// 3. 본사 S3/Cloudflare R2에 releases/ 업로드
/// 4. 워치독 로컬 = 5분 주기 체크 + delta 패키지 다운로드
///
/// 보안:
/// - 본사 서명 EXE만 적용 (sha256 검증)
/// - 사용자 PC = 검증 후 자동 적용
/// - 롤백: 직전 버전 EXE 보관 + 실패 시 자동 복귀
///
/// 헌법 정합:
/// - #22 본사 데이터 0 — 업데이트 패키지만 다운로드, 고객 데이터 0
/// - #23 5중 검증 — 본사 서명 + sha256 + 다운로드 후 PoC 실행
/// - #29 인프라 사전 승인 — 정식 도입 사장님 결재 후 (Phase 2)
/// </summary>
public sealed class VelopackUpdaterStub
{
    private readonly ILogger<VelopackUpdaterStub> _logger;
    private const string DefaultFeed = "https://updates.hitpan.kr";

    public VelopackUpdaterStub(ILogger<VelopackUpdaterStub> logger)
    {
        _logger = logger;
    }

    /// <summary>현재 stub은 no-op. 정식 통합 시 Velopack API 호출로 교체.</summary>
    public Task<bool> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        var enabled = Environment.GetEnvironmentVariable("HITPAN_AUTO_UPDATE_ENABLED");
        if (enabled != "true")
        {
            _logger.LogDebug("[Velopack] auto-update disabled (Phase 1) — set HITPAN_AUTO_UPDATE_ENABLED=true to enable in Phase 2");
            return Task.FromResult(false);
        }
        _logger.LogWarning("[Velopack] PoC stub — would check {Feed} for new version", DefaultFeed);
        return Task.FromResult(false);
    }

    /// <summary>정식 통합 시 의사코드:</summary>
    public static string IntegrationPseudoCode() => """
        // Phase 2 정식 통합 (사장님 결재 후)
        using Velopack;

        VelopackApp.Build().Run();   // Program.cs 진입점 직후

        var mgr = new UpdateManager("https://updates.hitpan.kr");
        var newVersion = await mgr.CheckForUpdatesAsync();
        if (newVersion != null)
        {
            await mgr.DownloadUpdatesAsync(newVersion);
            mgr.ApplyUpdatesAndRestart(newVersion);   // EXE 갱신 후 자동 재시작
        }
        """;
}
