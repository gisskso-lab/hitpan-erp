using HitPan.API.Services.LocalSwap;
using HitPan.API.Services.ManualUpdate;

namespace HitPan.API.Services.LocalRollback;

/// <summary>
/// 20260930작1 1.3.50 확대 갈래 N1 — API 기동 때 **한 번** 피드를 확인해, 지금 판이 최신인 동안 그 판의 서명 안내 파일이
/// PC 에 남게 한다(설계 §19-1 조각 A 「언제 남나」 X-2 (나) · 작업지시서 18-5 PM 조건부 승인).
///
/// ■ PM 조건(18-5 X-2) 과 지킨 자리
///   · 한 번만 — <see cref="ExecuteAsync"/> 가 <see cref="IUpdateFeed.CheckAsync"/> 를 한 번 부르고 끝난다. 반복·재시도·주기 0.
///   · 기동을 막지 않음 — <see cref="ExecuteAsync"/> 첫 줄 <c>await Task.Yield()</c> 로 곧바로 돌려준다
///     ⇒ <c>BackgroundService.StartAsync</c> 가 피드(최대 30초)를 기다리지 않고 끝난다([3-V] 병렬이슈 22).
///   · 실패 = 경고 한 줄 — 예외든 「닿지 못함·서명 불량」이든 <c>LogWarning</c> 한 줄. 결과는 버린다.
///   · 요청 모양 = 기존 피드 호출과 같음 — 업데이트 화면이 부르는 바로 그 <see cref="IUpdateFeed"/>(같은 어댑터·같은 주소·헤더·쿼리 무변경).
///     남기기는 어댑터 <c>CheckAsync</c> 안에서 일어난다(이 클래스는 파일을 모른다).
///   · 취소 토큰 존중 — 호스트가 멈추면 요청을 끊고 정보 로그 한 줄로 끝.
///
/// ■ 건너뛰는 때 — 윈도우가 아니거나 설치 루트가 없으면(개발 실행·통합 시험 호스트) 바깥 요청 자체를 하지 않는다.
///   남길 곳이 없는데 본사에 묻는 것은 쓸모가 없다 · 시험 호스트가 실제 피드에 닿지 않게 한다.
/// </summary>
public sealed class SignedManifestStartupCheck : BackgroundService
{
    private readonly IUpdateFeed _feed;
    private readonly ILocalSwapEnvironment _env;
    private readonly ILogger<SignedManifestStartupCheck> _logger;

    public SignedManifestStartupCheck(IUpdateFeed feed, ILocalSwapEnvironment env, ILogger<SignedManifestStartupCheck> logger)
    {
        _feed = feed;
        _env = env;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 기동 비차단 — 여기서 곧바로 돌려준다. 아래는 뒤에서 한 번만 돈다.
        await Task.Yield();

        if (!_env.IsWindows || _env.AppRoot is null)
        {
            _logger.LogInformation("[ManifestStartup] 설치 루트가 없어 기동 피드 확인을 건너뜁니다(개발 실행 등).");
            return;
        }

        try
        {
            var result = await _feed.CheckAsync(_env.CurrentVersion, stoppingToken).ConfigureAwait(false);
            if (result.Status is FeedCheckStatus.Unreachable or FeedCheckStatus.Invalid)
                _logger.LogWarning("[ManifestStartup] 기동 피드 확인이 안내 파일을 받지 못했습니다({Status}) — 다시 시도하지 않습니다.", result.Status);
            else
                _logger.LogInformation("[ManifestStartup] 기동 피드 확인 끝({Status}).", result.Status);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("[ManifestStartup] 프로그램이 멈추는 중이라 기동 피드 확인을 끝냅니다.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ManifestStartup] 기동 피드 확인 중 오류 — 다시 시도하지 않습니다.");
        }
    }
}
