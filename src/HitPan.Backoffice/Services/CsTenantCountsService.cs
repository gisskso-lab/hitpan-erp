using System.Net.Http.Json;

namespace HitPan.Backoffice.Services;

/// <summary>
/// CS 진행 4단계 숫자 — **고객사 한 곳** 한 벌 · 20261107작 갈래 B(D5 · C1 · C2 · C7).
///
/// <para>🔴 전체 한 벌(<see cref="CsCountsService"/>)은 손대지 않는다 — 사이드바 빨간 숫자와
/// 평소 표시줄은 언제나 전체다. 이 한 벌은 「고객 상세」 와 「그 고객사로 걸러진 고객A/S창구」 에서만
/// 아래 표시줄이 쓴다. 세는 곳은 서버 한 곳(<c>api/admin/cs/counts/tenant</c>).</para>
///
/// <para>🔴 늦게 온 답 버리기: A → B 로 빨리 옮기면 A 의 답이 B 이름 밑에 붙을 수 있다.
/// 답이 왔을 때 지금 고객사와 다르면 버린다.</para>
///
/// <para>실패: 같은 고객사 다시 세기 실패는 직전 값 유지(「0 으로 지우지 않기」) ·
/// 고객사가 바뀐 직후 실패는 「불러오는 중」 그대로(앞 고객사 숫자를 쓰지 않는다).</para>
/// </summary>
public sealed class CsTenantCountsService
{
    private readonly IHttpClientFactory _factory;
    private readonly ILogger<CsTenantCountsService> _logger;

    public CsTenantCountsService(IHttpClientFactory factory, ILogger<CsTenantCountsService> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    /// <summary>지금 고객사(null = 고객사 화면 아님 → 표시줄은 전체 한 벌).</summary>
    public string? TenantId { get; private set; }
    public string? CompanyName { get; private set; }
    /// <summary>서버가 그 고객사를 찾았나. 못 찾으면 숫자를 보이지 않는다(0건 표기 금지).</summary>
    public bool Found { get; private set; }
    public long Unread { get; private set; }
    public long Open { get; private set; }
    public long Done { get; private set; }
    public long Rated { get; private set; }
    public long Total { get; private set; }
    /// <summary>지금 고객사의 답을 한 번이라도 받았나. false = 「불러오는 중」.</summary>
    public bool Loaded { get; private set; }

    public event Action? Changed;

    // 고객사가 바뀔 때마다 1 씩 오른다. 🔴 이 서비스의 쓰기는 화면 문맥(InvokeAsync)에서만 부른다
    //   — 표시줄 60초 시계도 InvokeAsync 로 감싸 부른다(3V-B2 · 스레드풀 동시 쓰기 방지).
    private int _generation;

    /// <summary>
    /// 고객사를 정한다. 값이 바뀌면 숫자·회사명을 비우고(불러오는 중) 다시 센다 ·
    /// 같으면 다시 세기만 · null 이면 비운다.
    /// </summary>
    public async Task SetTenantAsync(string? tenantId)
    {
        var next = string.IsNullOrWhiteSpace(tenantId) ? null : tenantId;
        if (!string.Equals(next, TenantId, StringComparison.Ordinal))
        {
            _generation++;   // 3V-B2 — 앞 고객사로 나간 요청의 답은 이 번호가 달라 버려진다
            TenantId = next;
            Clear();
            Changed?.Invoke();
        }
        if (next is not null) await RefreshAsync();
    }

    /// <summary>지금 고객사를 다시 센다. 고객사가 없으면 아무것도 안 한다.</summary>
    public async Task RefreshAsync()
    {
        var asked = TenantId;
        var gen = _generation;
        if (asked is null) return;

        // 🔴 20261107작 I-1a — 서버가 400 으로 돌려줄 길이(Trim 뒤 36자 초과)면 **서버를 부르지 않는다.**
        //   부르면 매 주기 400 · 경고 로그 반복 · 표시줄이 「불러오는 중」 에 머문다.
        //   기존 「고객사를 찾지 못했습니다」 상태(Loaded=true · Found=false)로 둔다 — 새 표시 0 · 숫자 0 표기 0.
        //   상한 값과 판정은 해석기 파일 한 곳(CsBarScope.ExceedsServerTenantIdLength · 서버와 같은 36).
        if (CsBarScope.ExceedsServerTenantIdLength(asked))
        {
            Found = false;
            CompanyName = null;
            Loaded = true;
            Changed?.Invoke();
            return;
        }

        try
        {
            var http = _factory.CreateClient("backoffice-authed");
            var r = await http.GetFromJsonAsync<TenantCountsResponse>(
                $"api/admin/cs/counts/tenant?tenantId={Uri.EscapeDataString(asked)}");
            if (r is null) return;

            // 🔴 답이 오는 사이 고객사가 바뀌었으면 버린다(앞 고객사 숫자가 뒤 이름에 붙는 꼴 차단).
            //   세대 번호와 고객사값 둘 다 견준다(A → B → A 처럼 값이 되돌아와도 앞 요청은 버린다).
            if (gen != _generation || !string.Equals(asked, TenantId, StringComparison.Ordinal)) return;

            Found = r.Found;
            CompanyName = r.Found ? r.CompanyName : null;
            Unread = r.Unread ?? 0; Open = r.Open ?? 0; Done = r.Done ?? 0;
            Rated = r.Rated ?? 0; Total = r.Total ?? 0;
            Loaded = true;
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            // 헌법 #15 — 삼키지 않는다. 화면용 숫자라 예외를 올리면 레이아웃이 깨진다.
            //   같은 고객사면 직전 값 유지 · 바뀐 직후면 Loaded=false 그대로(불러오는 중).
            _logger.LogWarning(ex, "CS 고객사 숫자 조회 실패 — 직전 값을 유지하고 다음 주기에 다시 센다");
            Changed?.Invoke();
        }
    }

    private void Clear()
    {
        CompanyName = null; Found = false;
        Unread = 0; Open = 0; Done = 0; Rated = 0; Total = 0;
        Loaded = false;
    }

    private sealed class TenantCountsResponse
    {
        public bool Success { get; set; }
        public bool Found { get; set; }
        public string? CompanyName { get; set; }
        public long? Unread { get; set; }
        public long? Open { get; set; }
        public long? Done { get; set; }
        public long? Rated { get; set; }
        public long? Total { get; set; }
    }
}
