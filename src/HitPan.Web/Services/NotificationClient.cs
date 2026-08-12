using HitPan.Web.Models;
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

    public NotificationClient(HttpClient http, HitPanProtectedLocalStorage storage,
        ILogger<NotificationClient> logger)
    {
        _http = http;
        _storage = storage;
        _logger = logger;
    }

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
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                })
                .WithAutomaticReconnect()
                .Build();

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
        }
        catch (Exception ex)
        {
            // 🔴 알림이 안 와도 업무는 굴러가야 한다. 다만 삼키지는 않는다(헌법 #15).
            _logger.LogWarning(ex, "알림 연결 실패 — 알림 없이 계속 진행한다");
            _hub = null;
        }
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
        if (_hub is not null)
        {
            await _hub.DisposeAsync().ConfigureAwait(false);
            _hub = null;
        }
    }
}
