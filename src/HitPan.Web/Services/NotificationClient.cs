using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using HitPan.Web.Models;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace HitPan.Web.Services;

/// <summary>
/// 앱 내 실시간 알림 수신기. 작(2026-08-13) 그룹웨어 단계2.
/// </summary>
/// <remarks>
/// <para>
/// 사장님: <i>"히트판ERP내 알림되도록(인스타나 페이스북처럼)"</i>.
/// 김삼성 상무: <i>"'결재가 올라왔습니다' 알림부터 붙이세요."</i>
/// </para>
/// <para>
/// 🔴 <b>화면 하나가 아니라 앱 전체에 떠야 하므로</b> 서비스로 만들어 레이아웃이 한 번만 연결한다.
/// 페이지마다 각자 연결하면 화면을 옮길 때마다 끊기고, 알림이 그 순간에만 안 온다.
/// </para>
/// <para>
/// 🟢 배관은 마이그레이션 진행률에서 검증된 패턴을 그대로 복제했다
/// (WebSocket 인증은 <c>AccessTokenProvider</c> 로 붙인다 — 이미 봉합된 자리).
/// </para>
/// <para>
/// 🔴 <b>알림이 안 와도 업무는 굴러가야 한다.</b> 연결 실패는 로그만 남기고 삼킨다.
/// 알림은 편의이지 업무의 전제가 아니다 — 터널이 불안정한 고객사에서 알림 때문에
/// 화면이 멈추면 그게 더 큰 사고다(헌법 #27).
/// </para>
/// </remarks>
public sealed class NotificationClient : IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly HitPanProtectedLocalStorage _storage;
    private readonly ILogger<NotificationClient> _logger;

    private HubConnection? _hub;

    // 🆕 20260928작4 쿨타임 §E-1 (리뷰서 F1) — 토큰 갱신 길 · 로그인 바뀜 알림 · 다시 시작 멈춤 장치.
    private readonly IAuthTokenRefresher _refresher;
    private readonly AuthenticationStateProvider _authState;
    private CancellationTokenSource _lifetime = new();
    private string? _connectedSid;
    private bool _disposed;
    private bool _starting;

    // ⬛ [낡은 생성자 · 20260928작4 §E-1 이전]
    //   public NotificationClient(HttpClient http, HitPanProtectedLocalStorage storage, ILogger<NotificationClient> logger)
    //   — 토큰을 연결 때 한 번 읽어 붙잡았다(F1). 인자 둘을 뒤에 더했다(DI 가 채운다 · 직접 new 하는 곳 0).
    public NotificationClient(HttpClient http, HitPanProtectedLocalStorage storage,
        ILogger<NotificationClient> logger,
        IAuthTokenRefresher refresher,
        AuthenticationStateProvider authState)
    {
        _http = http;
        _storage = storage;
        _logger = logger;
        _refresher = refresher;
        _authState = authState;
        _authState.AuthenticationStateChanged += OnAuthenticationStateChanged;
    }

    // ── 🆕 20260928작4 쿨타임 §5-2 — 물음 · 끝남 (추가 · #1) ──────────────────────────

    /// <summary>서버가 「계속 일할지」 물었다(남은 초). 연결이 성공할 때마다 상태를 물어 놓친 물음도 여기로 온다.</summary>
    public event Action<int>? OnCooltimePrompt;

    /// <summary>서버가 이 로그인을 끝냈다(까닭: no_answer · missed · user_choice · countdown).</summary>
    public event Action<string>? OnCooltimeEnded;

    /// <summary>
    /// 아직 팝업이 받기 전에 온 물음(받은 때 · 남은 초). 팝업이 늦게 붙어도 이것을 보고 띄운다.
    /// 팝업이 처리하면 <see cref="ClearPendingCooltimePrompt"/> 로 지운다.
    /// </summary>
    public (DateTime ReceivedUtc, int SecondsLeft)? PendingCooltimePrompt { get; private set; }

    /// <summary>끝남을 받았나(팝업이 늦게 붙어도 로그인 화면으로 보낼 수 있게).</summary>
    public string? PendingCooltimeEnded { get; private set; }

    public void ClearPendingCooltimePrompt() => PendingCooltimePrompt = null;

    /// <summary>새 알림이 도착했을 때 화면이 반응하도록 알린다.</summary>
    public event Action<AppNotificationModel>? OnNotification;

    /// <summary>지금까지 받은 알림(최근 것이 앞). 화면 새로고침 전까지만 유지된다.</summary>
    public List<AppNotificationModel> Recent { get; } = new();

    /// <summary>읽지 않은 알림 수.</summary>
    public int UnreadCount => Recent.Count(n => !n.IsRead);

    /// <summary>
    /// 연결한다. 이미 연결돼 있으면 아무것도 하지 않는다(레이아웃이 여러 번 불러도 안전).
    /// </summary>
    public async Task StartAsync()
    {
        if (_hub is not null)
        {
            return;
        }

        // 🆕 §E-1 — 부르는 자리가 셋(종 · 자동 로그아웃 팝업 · 로그인 바뀜)이라, 저장소를 읽는 사이
        //   두 번째 호출이 연결을 하나 더 만들지 않게 막는다.
        if (_starting)
        {
            return;
        }
        _starting = true;

        try
        {
            var tokenResult = await _storage.GetAsync<string>(AuthStorageKeys.AccessToken)
                .ConfigureAwait(false);
            var token = tokenResult.Success ? tokenResult.Value : null;

            if (string.IsNullOrWhiteSpace(token))
            {
                // 로그인 전에는 연결하지 않는다. 로그인 후 레이아웃이 다시 부른다.
                return;
            }

            var hubUrl = new Uri(_http.BaseAddress!, "hubs/notify").ToString();

            _hub = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    // ⬛ [낡은 줄 · 20260928작4 §E-1 이전] options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                    //   — 처음 읽은 토큰(15분)을 붙잡아 갱신 뒤 다시 연결하면 401 이 됐다(리뷰서 F1 P1).
                    // 🆕 §E-1 — 부를 때마다 저장소의 지금 토큰 · 만료(곧 만료)면 기존 갱신 길로 바꾼 뒤 준다.
                    options.AccessTokenProvider = GetHubTokenAsync;
                })
                .WithAutomaticReconnect()
                .Build();

            _connectedSid = ReadSid(token);
            AttachCooltimeHandlers(_hub);

            _hub.On<AppNotificationModel>("Notify", notification =>
            {
                Recent.Insert(0, notification);

                // 오래된 것은 버린다. 알림함이 아니라 "지금 뜨는 알림"이기 때문이다.
                // (보관이 필요해지면 서버에 표를 두는 별도 작업 — 지금은 만들지 않는다.)
                if (Recent.Count > 50)
                {
                    Recent.RemoveRange(50, Recent.Count - 50);
                }

                OnNotification?.Invoke(notification);
            });

            await _hub.StartAsync().ConfigureAwait(false);

            // 🆕 §E-1 — 연결이 성공할 때마다 놓친 물음을 메운다(첫 시작).
            await CheckCooltimeStateAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 🔴 알림이 안 와도 업무는 굴러가야 한다. 다만 삼키지는 않는다(헌법 #15).
            _logger.LogWarning(ex, "알림 연결 실패 — 알림 없이 계속 진행한다");
            // ⬛ [낡은 줄 · §E-1 이전] _hub = null; — 만든 연결을 버리지 않고 놓아 두었다.
            // 🆕 §E-1 — 실패한 연결은 멈추고 버린다(다음 StartAsync 가 새로 만든다).
            await DropHubAsync().ConfigureAwait(false);
        }
        finally
        {
            _starting = false;
        }
    }

    // ═══ 🆕 20260928작4 쿨타임 §E-1 · §5-2 — 연결 지키기 · 물음 받기 (추가 · #1) ═══════════
    //
    // 계약(설계 §E-1 · 갈래 ① 명세서 §1-2):
    //   ① 토큰은 부를 때마다 지금 것(만료면 AuthTokenRefresher) — GetHubTokenAsync
    //   ② 연결이 Closed(자동 재연결 포기 포함) 면 간격을 두고 다시 시작 — OnHubClosedAsync
    //   ③ 로그인·로그아웃 때 옛 연결을 멈추고 버린다 — OnAuthenticationStateChanged · StopAsync
    //   ④ 연결 성공(첫 시작 · Reconnected · 다시 시작)마다 GET state — CheckCooltimeStateAsync

    /// <summary>푸시 연결이 부를 때마다 쓰는 토큰(§E-1). 게이트가 직접 두 번 불러 잴 수 있게 공개.</summary>
    public Task<string?> GetHubTokenAsync()
        => CooltimeScreenRules.ResolveHubTokenAsync(ReadStoredTokenAsync, () => _refresher.TryRefreshAsync(), IsExpiringSoon);

    private async Task<string?> ReadStoredTokenAsync()
    {
        var r = await _storage.GetAsync<string>(AuthStorageKeys.AccessToken).ConfigureAwait(false);
        return r.Success ? r.Value : null;
    }

    /// <summary>만료됐거나 30초 안에 만료(<c>HitPanAuthStateProvider</c> 와 같은 여유).</summary>
    private bool IsExpiringSoon(string token)
    {
        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            return jwt.ValidTo <= DateTime.UtcNow.AddSeconds(30);
        }
        catch (Exception ex)
        {
            // 읽을 수 없는 토큰 = 갱신을 시도한다(붙잡지 않는 쪽).
            _logger.LogWarning(ex, "알림 연결 토큰을 읽지 못했다 — 갱신을 시도한다");
            return true;
        }
    }

    private string? ReadSid(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            return new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
                .FirstOrDefault(c => c.Type == "sid")?.Value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "알림 연결 토큰의 로그인 번호를 읽지 못했다");
            return null;
        }
    }

    private void AttachCooltimeHandlers(HubConnection hub)
    {
        hub.On<CooltimePromptMessage>(CooltimeScreenRules.PromptEvent, m =>
        {
            RaiseCooltimePrompt(m?.SecondsLeft ?? CooltimeScreenRules.PromptSeconds);
        });

        hub.On<CooltimeEndedMessage>(CooltimeScreenRules.EndedEvent, m =>
        {
            var cause = m?.Cause ?? string.Empty;
            PendingCooltimeEnded = cause;
            OnCooltimeEnded?.Invoke(cause);
        });

        // ④ 자동 재연결 성공 — 끊긴 사이 온 물음을 메운다.
        hub.Reconnected += async _ => await CheckCooltimeStateAsync().ConfigureAwait(false);

        // ② 닫힘(자동 재연결 4회 포기 · 서버가 끊음) — 간격을 두고 다시 시작한다.
        hub.Closed += ex => OnHubClosedAsync(hub, ex);
    }

    private void RaiseCooltimePrompt(int secondsLeft)
    {
        PendingCooltimePrompt = (DateTime.UtcNow, secondsLeft);
        OnCooltimePrompt?.Invoke(secondsLeft);
    }

    /// <summary>
    /// ④ 상태를 물어 서버가 이미 물었으면(<c>asked:true</c>) 남은 초로 팝업을 띄운다.
    /// 실패는 흔적만 남긴다 — 서버가 시각 작업으로 끝내므로 화면이 놓쳐도 「끝나지 않는」 길은 없다.
    /// </summary>
    public async Task CheckCooltimeStateAsync()
    {
        try
        {
            var state = await _http.GetFromJsonAsync<CooltimeStateMessage>(CooltimeScreenRules.StatePath)
                .ConfigureAwait(false);
            if (state is { Asked: true })
            {
                RaiseCooltimePrompt(state.SecondsLeft);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "자동 로그아웃 상태를 묻지 못했다 — 다음 연결 때 다시 묻는다");
        }
    }

    private async Task OnHubClosedAsync(HubConnection hub, Exception? error)
    {
        if (_disposed || !ReferenceEquals(hub, _hub))
        {
            return;   // 일부러 멈췄거나 이미 새 연결로 바뀌었다
        }

        _logger.LogWarning(error, "알림 연결이 닫혔다 — 간격을 두고 다시 연결한다");
        var ct = _lifetime.Token;
        for (var attempt = 0; !ct.IsCancellationRequested && ReferenceEquals(hub, _hub); attempt++)
        {
            try
            {
                await Task.Delay(CooltimeScreenRules.RestartDelay(attempt), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("알림 다시 연결을 멈춘다(로그아웃 · 화면 닫힘)");
                return;
            }

            var token = await ReadStoredTokenAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token))
            {
                // 로그인이 없어졌다 — 옛 연결을 버리고 멈춘다(③).
                await DropHubAsync().ConfigureAwait(false);
                return;
            }

            try
            {
                await hub.StartAsync(ct).ConfigureAwait(false);
                await CheckCooltimeStateAsync().ConfigureAwait(false);   // ④ 다시 시작 성공
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "알림 다시 연결 실패({Attempt}회) — 조금 뒤 또 시도한다", attempt + 1);
            }
        }
    }

    /// <summary>
    /// ③ 로그인이 바뀌면(로그아웃 · 다른 로그인) 옛 연결을 버리고, 로그인이 있으면 새로 연결한다.
    /// 같은 로그인의 토큰 갱신(로그인 번호 같음)은 연결을 그대로 둔다.
    /// </summary>
    private void OnAuthenticationStateChanged(Task<AuthenticationState> task)
    {
        _ = HandleAuthChangedAsync(task);
    }

    private async Task HandleAuthChangedAsync(Task<AuthenticationState> task)
    {
        try
        {
            await task.ConfigureAwait(false);
            var token = await ReadStoredTokenAsync().ConfigureAwait(false);
            var sid = ReadSid(token);
            if (_hub is not null && (string.IsNullOrWhiteSpace(token) || !string.Equals(sid, _connectedSid, StringComparison.Ordinal)))
            {
                await StopAsync().ConfigureAwait(false);
            }
            if (!string.IsNullOrWhiteSpace(token) && _hub is null && !_disposed)
            {
                await StartAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "로그인 바뀜에 맞춰 알림 연결을 정리하지 못했다");
        }
    }

    /// <summary>
    /// 연결을 멈추고 버린다(③ · 자동 로그아웃 끝남 때 화면이 부른다). 다시 시작 대기도 멈춘다.
    /// </summary>
    public async Task StopAsync()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        _lifetime = new CancellationTokenSource();
        PendingCooltimePrompt = null;
        await DropHubAsync().ConfigureAwait(false);
    }

    private async Task DropHubAsync()
    {
        var hub = _hub;
        _hub = null;
        _connectedSid = null;
        if (hub is null) return;
        try
        {
            await hub.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "알림 연결을 닫다가 오류 — 버리고 계속한다");
        }
    }

    /// <summary>서버 푸시 본문 <c>{ secondsLeft }</c>.</summary>
    private sealed class CooltimePromptMessage
    {
        public int SecondsLeft { get; set; }
    }

    /// <summary>서버 푸시 본문 <c>{ cause }</c>.</summary>
    private sealed class CooltimeEndedMessage
    {
        public string? Cause { get; set; }
    }

    /// <summary><c>GET api/auth/cooltime/state</c> 응답.</summary>
    private sealed class CooltimeStateMessage
    {
        public bool Asked { get; set; }
        public int SecondsLeft { get; set; }
        public DateTime? SnoozeUntilUtc { get; set; }
    }

    /// <summary>전부 읽음 처리한다.</summary>
    public void MarkAllRead()
    {
        foreach (var n in Recent)
        {
            n.IsRead = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        // 🆕 §E-1 — 다시 시작 대기 · 로그인 바뀜 구독을 먼저 끊는다.
        _disposed = true;
        _authState.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();

        if (_hub is not null)
        {
            await _hub.DisposeAsync().ConfigureAwait(false);
            _hub = null;
        }
    }
}
