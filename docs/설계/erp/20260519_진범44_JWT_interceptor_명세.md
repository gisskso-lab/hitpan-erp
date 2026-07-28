# 🔍 진범 #44 JWT Interceptor 본구현 명세

> **작성**: 2026-05-19 05:00 PM 브라운킴 (CTO 1인)

---

## 🚨 진범 #44 본질

### JWT 수명 박제
- Access Token = **15분**
- Refresh Token = 7일
- 마이그 시간 = 평균 12분 (소형) / 24시간 한계 (대형 2GB)

### 발생 시나리오
| 시점 | Access 상태 | 결과 |
|---|---|---|
| 0분 | 발급 직후 | ✅ |
| 5분 | 활성 | ✅ |
| 14분 | 만료 임박 | ⚠️ |
| **15분+** | **만료** | **🚨 401** |
| 30분 | 만료 | 🚨 (Refresh로 재발급 가능) |
| 7일+ | Refresh도 만료 | 🚨🚨 (로그아웃) |

---

## 🎯 봉합 옵션 A — HttpClient Interceptor (자동 Refresh)

### 파일 위치
`src/HitPan.Web/Services/AuthDelegatingHandler.cs` (신규 또는 기존 확장)

### 흐름
```
HttpClient.SendAsync(request)
    ↓ AuthDelegatingHandler 가로채기
    ↓
1. Storage에서 Access Token 읽기
2. JWT exp 검사 (현재 시간 + 1분 buffer)
   - exp 가까우면 → Refresh 시도
3. request에 Bearer 헤더 추가
4. 실제 API 호출
    ↓
5. 응답이 401이면:
   - Refresh 시도
   - 성공 시 새 토큰으로 1회 재시도
   - 실패 시 로그아웃 + Snackbar
```

### 코드 명세
```csharp
public class AuthDelegatingHandler : DelegatingHandler
{
    private readonly HitPanProtectedLocalStorage _storage;
    private readonly IAuthService _auth;  // RefreshAsync
    private static readonly SemaphoreSlim _refreshSemaphore = new(1, 1);

    public AuthDelegatingHandler(HitPanProtectedLocalStorage storage, IAuthService auth)
    {
        _storage = storage;
        _auth = auth;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        // 1. /api/auth/refresh 자체는 재귀 방지
        if (request.RequestUri?.AbsolutePath.EndsWith("/api/auth/refresh") == true)
        {
            return await base.SendAsync(request, ct);
        }

        // 2. Access Token 부착
        var tokenResult = await _storage.GetAsync<string>(AuthStorageKeys.AccessToken);
        if (tokenResult.Success && !string.IsNullOrEmpty(tokenResult.Value))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Value);
        }

        // 3. 호출
        var response = await base.SendAsync(request, ct);

        // 4. 401 시 1회 Refresh + 재시도
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await _refreshSemaphore.WaitAsync(ct);
            try
            {
                var refreshed = await _auth.RefreshAsync(ct);
                if (refreshed)
                {
                    var newToken = await _storage.GetAsync<string>(AuthStorageKeys.AccessToken);
                    if (newToken.Success && !string.IsNullOrEmpty(newToken.Value))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newToken.Value);
                        response.Dispose();
                        response = await base.SendAsync(request, ct);
                    }
                }
            }
            finally
            {
                _refreshSemaphore.Release();
            }
        }

        return response;
    }
}
```

### Program.cs 등록
```csharp
builder.Services.AddTransient<AuthDelegatingHandler>();
builder.Services.AddScoped(sp =>
{
    var handler = sp.GetRequiredService<AuthDelegatingHandler>();
    handler.InnerHandler = new HttpClientHandler();
    return new HttpClient(handler) { BaseAddress = new Uri(apiUri) };
});
```

### 변경량
- 신규 파일 60줄 + Program.cs 5줄 = **65줄**

---

## 🎯 봉합 옵션 B — 폴링 fallback (SignalR 보완)

### 흐름
SignalR Hub 연결과 무관하게 5초마다 status API 폴링 → 카드 상태 동기화

### 코드 명세 (MdbMigration.razor)
```csharp
private CancellationTokenSource? _pollingCts;

private async Task StartPollingAsync(string jobId)
{
    _pollingCts = new CancellationTokenSource();
    var ct = _pollingCts.Token;
    while (!ct.IsCancellationRequested)
    {
        try
        {
            var resp = await Http.GetAsync($"api/migration/legacy-mdb/status/{jobId}", ct);
            if (resp.IsSuccessStatusCode)
            {
                var status = await resp.Content.ReadFromJsonAsync<JobStatus>(ct);
                if (status?.TableProgress is not null)
                {
                    foreach (var (key, tp) in status.TableProgress)
                    {
                        var card = _tableCards.FirstOrDefault(c => c.Key == key);
                        if (card is not null && card.Status != tp.Status)
                        {
                            card.Status = tp.Status;
                            card.Rows = tp.Rows;
                            card.ElapsedMs = tp.ElapsedMs;
                            card.ErrorMessage = tp.ErrorMessage;
                        }
                    }
                    _completedTables = _tableCards.Count(c => c.Status is "completed" or "failed");
                    await InvokeAsync(StateHasChanged);
                }
                if (status?.Status is "completed" or "failed") break;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Polling] {ex.Message}");
        }
        await Task.Delay(5000, ct);  // 5초 폴링
    }
}
```

### 변경량
- MdbMigration.razor 40줄

### 옵션 A + B 병행 효과
- 진범 #44 (401) 완전 봉합
- 진범 #22·#43 (SignalR 누락) 자동 회복
- 헌법 #27 통신 무결성 정합

---

**작성**: PM 브라운킴 (CTO 1인) 2026-05-19 05:00
**상태**: 사장님 5/20 결재 후 옵션 A+B 동시 봉합 권고. 총 변경량 105줄.
