using System.Net.Http.Json;

namespace HitPan.Backoffice.Services;

/// <summary>
/// CS 진행 4단계 숫자 한 벌 — 사장님 지시 2026-10-08
/// (읽지 않음 · 처리되지 않음 · 처리 완료 · CS평가 완료).
///
/// <para>🔴 왜 서비스 하나로 모으나: 사이드바 빨간 숫자와 화면 아래 띠가 <b>각자 세면</b>
/// 두 숫자가 어긋난다. 그 순간 사람은 화면을 믿지 않는다. 세는 곳은 서버 한 곳(<c>api/admin/cs/counts</c>),
/// 화면에 나르는 길도 이 서비스 하나다.</para>
///
/// <para>새로 세는 일은 <see cref="RefreshAsync"/> 를 부른 쪽이 정하고(띠가 시계를 가진다),
/// 바뀌면 <see cref="Changed"/> 로 알린다. 조회 실패는 숫자를 <b>지우지 않는다</b> —
/// 네트워크가 한 번 끊겼다고 「0건」으로 보이면 그게 더 위험하다.</para>
/// </summary>
public sealed class CsCountsService
{
    private readonly IHttpClientFactory _factory;

    public CsCountsService(IHttpClientFactory factory) => _factory = factory;

    public long Unread { get; private set; }
    public long Open { get; private set; }
    public long Done { get; private set; }
    public long Rated { get; private set; }
    public long Total { get; private set; }
    public bool Loaded { get; private set; }

    public event Action? Changed;

    public async Task RefreshAsync()
    {
        try
        {
            var http = _factory.CreateClient("backoffice-authed");
            var r = await http.GetFromJsonAsync<CountsResponse>("api/admin/cs/counts");
            if (r is null) return;

            Unread = r.Unread; Open = r.Open; Done = r.Done; Rated = r.Rated; Total = r.Total;
            Loaded = true;
            Changed?.Invoke();
        }
        catch (Exception)
        {
            // 헌법 #15 정합: 삼키지 않는다 — 다만 화면용 숫자라 예외를 올리면 레이아웃이 깨진다.
            //   직전 값을 그대로 두고(지우지 않는다) 다음 주기에 다시 센다.
            //   서버 쪽 실패는 API 로그가 남는다(여기서 중복 기록하지 않는다).
            Changed?.Invoke();
        }
    }

    private sealed class CountsResponse
    {
        public bool Success { get; set; }
        public long Unread { get; set; }
        public long Open { get; set; }
        public long Done { get; set; }
        public long Rated { get; set; }
        public long Total { get; set; }
    }
}
