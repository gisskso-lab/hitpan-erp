namespace HitPan.API.Services.Cooltime;

/// <summary>
/// 쿨타임 서버 시각 작업 — 15초마다 한 바퀴(20260928작4 §2). 화면 타이머를 쓰지 않는다:
/// 팝업을 못 보는 PC(닫힘·절전·숨은 탭)도 서버가 끝내야 다른 PC 가 풀린다(Q5).
/// </summary>
/// <remarks>
/// 스코프는 <see cref="CsAutoDispatchService"/> 모양(<c>CreateScope</c>) — 연결(<c>IDbConnection</c>)이 Scoped 다.
/// 판단 본체는 <see cref="CooltimeEngine.RunOnceAsync"/>. 시각은 인자로 받는다 ⇒ 게이트가 시계를 주입한다(새 패키지 0).
/// 본사 호출 0 — 고객 PC 로컬 API 안에서만 돈다(#18 · #22 · #30).
/// 비상 끔 = 설정 화면 스위치 → 다음 바퀴부터 물음·끝내기 0(켜진 회사만 고른다).
/// </remarks>
public sealed class CooltimeService : BackgroundService
{
    /// <summary>한 바퀴 간격 — 실제 종료 = 설정 시각 + 60~85초(9/28 PM 결정 · 설계 §9-1 「15초 주기」).</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(15);

    private readonly IServiceProvider _sp;
    private readonly ILogger<CooltimeService> _logger;

    public CooltimeService(IServiceProvider sp, ILogger<CooltimeService> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    /// <summary>
    /// 한 바퀴 — 새 스코프에서 <see cref="CooltimeEngine"/> 을 꺼내 돌린다. 게이트가 직접 부를 수 있게 공개한다.
    /// </summary>
    /// <param name="nowLocal">서버 PC 벽시계(운영 = <c>DateTime.Now</c>).</param>
    /// <param name="ct">취소.</param>
    public async Task<CooltimeTickResult> RunOnceAsync(DateTime nowLocal, CancellationToken ct = default)
    {
        using var scope = _sp.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<CooltimeEngine>();
        return await engine.RunOnceAsync(nowLocal, ct).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var r = await RunOnceAsync(DateTime.Now, stoppingToken).ConfigureAwait(false);
                if (r.Asked + r.EndedNoAnswer + r.EndedMissed + r.Deadlocks > 0)
                {
                    _logger.LogInformation(
                        "[쿨타임] 한 바퀴 — 물음 {Asked} · 무응답 끝냄 {NoAnswer} · 놓침 끝냄 {Missed} · 교착 {Deadlocks}",
                        r.Asked, r.EndedNoAnswer, r.EndedMissed, r.Deadlocks);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // #15 — 한 바퀴 실패가 작업을 멈추지 않는다. 다음 바퀴가 다시 판정한다.
                _logger.LogWarning(ex, "[쿨타임] 한 바퀴 실패 — 다음 바퀴에 다시 판정");
            }

            try { await Task.Delay(TickInterval, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }
}
