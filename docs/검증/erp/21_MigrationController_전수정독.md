# 21. MigrationController + MigrationJobStore 전수 정독서 (세미콜론·괄호까지)

**작성:** 백엔드 매니저 + 보안팀장
**일자:** 2026-05-14 새벽
**대상:** `src/HitPan.API/Controllers/MigrationController.cs` 261줄 + `src/HitPan.Application/Services/MigrationJobStore.cs` 68줄

---

## 1. 클래스 헤더 + DI

```csharp
[ApiController]
[Route("api/migration")]
[Authorize(Policy = "TenantAdminOnly")]
[SupportedOSPlatform("windows")]
public sealed class MigrationController : ControllerBase
```

**XML 주석 원문 (L:11-17):**
```
/// <summary>
/// 레거시 히트판 MDB 데이터 마이그레이션 컨트롤러
/// - 기존 VB + Access(.mdb) 데이터를 신규 ERP DB로 이관
/// - tenant_admin 권한 필수
/// - Windows 전용 (Microsoft.Jet.OLEDB ACE 드라이버 의존) — Linux 컨테이너 배포 시 미지원
///   사장님 헌법 #19 warnings 0 준수: SupportedOSPlatform 어트리뷰트로 명시 → CA1416 해소
/// </summary>
```

**DI (L:24-39):**
- `MdbMigrationService _migrationService`
- `ILogger<MigrationController> _logger`
- `MigrationJobStore _jobStore`
- `IServiceScopeFactory _scopeFactory`

---

## 2. 엔드포인트 4개 표

| 메서드 | 경로 | line | 반환 | Cloudflare 524 |
|---|---|---|---|---|
| GET | /preview | 46-110 | 200/400/403/404/500 | 동기 |
| POST | /legacy-mdb | 116-176 | 200/400/403/404/500 | ⚠ 동기 100s 한계 |
| POST | /legacy-mdb/start | 182-234 | 202/400/403 | ✅ 즉시 응답 |
| GET | /legacy-mdb/status/{jobId} | 239-260 | 200/403/404 | ✅ 폴링 |

---

## 3. PreviewLegacyMdb (L:46-110)

**XML 주석 (L:41-45):**
```
/// MDB 폴더 내 테이블 건수 미리보기 (실제 import 없음)
/// - 마이그레이션 전 데이터 규모를 확인할 때 사용
/// - 핫픽스 2026-05-13: mdbPassword 파라미터 추가 (비번 걸린 레거시 MDB 지원)
```

**시그니처:**
```csharp
public async Task<IActionResult> PreviewLegacyMdb(
    [FromQuery] string folderPath,
    [FromQuery] string? mdbPassword,
    CancellationToken ct)
```

**전처리 (L:52-63):**
```csharp
var tenantId = HttpContext.Items["TenantId"]?.ToString();
if (string.IsNullOrEmpty(tenantId)) return Forbid();
if (string.IsNullOrWhiteSpace(folderPath))
    return BadRequest(new { message = "MDB 폴더 경로를 입력해주세요." });
```

**예외 처리 7종 (L:65-109):**

| # | 예외 | line | HTTP | 메시지 |
|---|---|---|---|---|
| 1 | FileNotFoundException | 71-75 | 404 | `MDB 파일을 찾을 수 없습니다. 폴더 경로를 확인해주세요. ({ex.Message})` |
| 2 | DirectoryNotFoundException | 76-80 | 404 | `폴더가 존재하지 않습니다: {folderPath}` |
| 3 | UnauthorizedAccessException | 81-85 | 403 | `폴더 접근 권한이 없습니다: {folderPath}` |
| 4 | InvalidOperationException | 86-90 | 400 | `{ex.Message}` |
| 5 | OleDbException | 91-98 | 400 | password/암호 포함 시: "MDB 비밀번호가 틀렸거나 비번이 걸려있습니다." 아니면: "MDB 파일을 열 수 없습니다." |
| 6 | Win32Exception | 99-103 | 500 | `MDB 처리 엔진(Microsoft.ACE.OLEDB.12.0)이 설치되지 않았을 가능성이 있습니다.` |
| 7 | Exception | 104-109 | 500 | `미리보기 실행 중 오류: {ex.GetType().Name} - {ex.Message}` (silent swallow 금지 — 헌법 #15) |

---

## 4. MigrateLegacyMdb (L:116-176) — 동기

**XML 주석 (L:112-115):**
```
/// 레거시 MDB 데이터를 신규 ERP DB로 마이그레이션 실행
/// - 업체, 상품, BOM, 사원, 발주, 수주, 재고원장, 세금계산서, 수금, 분개, 현금출납 등 일괄 이관
```

**시그니처:**
```csharp
public async Task<IActionResult> MigrateLegacyMdb([FromBody] MdbMigrationRequest request, CancellationToken ct)
```

**성공 경로 (L:135-136):**
```csharp
var result = await _migrationService.MigrateAsync(request.FolderPath, tenantId, request.MdbPassword, ct).ConfigureAwait(false);
return Ok(result);
```

**예외 처리 7종:** Preview와 동일 패턴 (L:138-175)

---

## 5. StartMigrationJob (L:182-234) — 비동기 ★ Cloudflare 524 회피

**XML 주석 (L:178-181):**
```
/// 2026-05-14: 백그라운드 마이그 시작 — Cloudflare 524 회피용.
/// POST 즉시 JobId 반환 (1초 내). 진행률은 /status/{jobId} GET 폴링.
```

**시그니처:**
```csharp
public IActionResult StartMigrationJob([FromBody] MdbMigrationRequest request)
```

**전처리 (L:185-188):**
```csharp
var tenantId = HttpContext.Items["TenantId"]?.ToString();
if (string.IsNullOrEmpty(tenantId)) return Forbid();
if (string.IsNullOrWhiteSpace(request.FolderPath))
    return BadRequest(new { message = "MDB 폴더 경로를 입력해주세요." });
```

**Jot 생성 (L:190):**
```csharp
var job = _jobStore.Create(tenantId);
```

**Task.Run 백그라운드 패턴 (L:193-231):**
```csharp
_ = Task.Run(async () =>
{
    try
    {
        _jobStore.Update(job.JobId, j => { j.Status = "running"; j.CurrentStep = "초기화"; });

        using var scope = _scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<MdbMigrationService>();

        _jobStore.Update(job.JobId, j => j.CurrentStep = "MDB 읽기 + Bulk INSERT 진행 중...");
        var result = await svc.MigrateAsync(request.FolderPath, tenantId, request.MdbPassword, CancellationToken.None);

        _jobStore.Update(job.JobId, j =>
        {
            j.Status = "completed";
            j.CurrentStep = "완료";
            j.FinishedAt = DateTime.UtcNow;
            j.Result = new MigrationJobResult
            {
                Partners = result.Partners, Items = result.Items, BomHeaders = result.BomHeaders,
                Employees = result.Employees, SalesOrders = result.SalesOrders, PurchaseOrders = result.PurchaseOrders,
                StockLedger = result.StockLedger, Collections = result.Collections, Cashbook = result.Cashbook,
                Expenses = result.Expenses, PurchaseOrdersFromIU = result.PurchaseOrdersFromIU,
                SalesOrdersFromIO = result.SalesOrdersFromIO, TaxInvoices = result.TaxInvoices,
                Bills = result.Bills, CardPayments = result.CardPayments, BankTransactions = result.BankTransactions
            };
        });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "[Migrate-Job] {JobId} 실패", job.JobId);
        _jobStore.Update(job.JobId, j =>
        {
            j.Status = "failed";
            j.ErrorMessage = $"{ex.GetType().Name}: {ex.Message}";
            j.FinishedAt = DateTime.UtcNow;
        });
    }
});
```

**핵심 4개 패턴:**
1. **즉시 202 Accepted** (L:233) — `return Accepted(new { jobId = job.JobId, status = "queued" });`
2. **Task.Run** — HttpContext 끊김 무관
3. **IServiceScopeFactory.CreateScope()** — 새 DI 스코프 (HttpContext 없는 환경)
4. **CancellationToken.None** — 백그라운드는 클라이언트 끊김 무시

---

## 6. GetMigrationJobStatus (L:239-260)

**XML 주석 (L:236-238):**
```
/// 마이그 잡 진행 상태 조회 — Razor가 2초마다 폴링.
```

**시그니처:**
```csharp
public IActionResult GetMigrationJobStatus(string jobId)
```

**검증 (L:242-247):**
```csharp
var tenantId = HttpContext.Items["TenantId"]?.ToString();
if (string.IsNullOrEmpty(tenantId)) return Forbid();
var job = _jobStore.Get(jobId);
if (job is null) return NotFound(new { message = "잡 ID를 찾을 수 없습니다." });
if (job.TenantId != tenantId) return Forbid();
```

⚠️ **테넌트 격리 (L:247):** `job.TenantId != tenantId` 비교 → 다른 테넌트의 잡 조회 차단 (헌법 #1 강화)

**응답 (L:249-259):**
```csharp
return Ok(new
{
    jobId = job.JobId,
    status = job.Status,
    currentStep = job.CurrentStep,
    startedAt = job.StartedAt,
    finishedAt = job.FinishedAt,
    result = job.Result,
    errorMessage = job.ErrorMessage,
    elapsedSeconds = (int)((job.FinishedAt ?? DateTime.UtcNow) - job.StartedAt).TotalSeconds
});
```

---

## 7. MdbMigrationRequest DTO (L:263-279)

```csharp
public record MdbMigrationRequest
{
    /// <summary>레거시 MDB 파일이 위치한 폴더 경로</summary>
    public string FolderPath { get; init; } = string.Empty;

    /// <summary>
    /// MDB 파일 비밀번호 (선택사항, 핫픽스 2026-05-13).
    /// 레거시 히트판 MDB는 비번이 걸려있는 경우가 있다 (예: 7618968).
    /// 비번이 없으면 null 또는 빈 문자열.
    /// </summary>
    public string? MdbPassword { get; init; }
}
```

---

## 8. MigrationJobStore (68줄)

**XML 주석 (L:5-9):**
```
/// 마이그레이션 백그라운드 잡 진행 상태 인메모리 저장소.
/// 2026-05-14 야간: Cloudflare 524(100초 한계) 회피 — POST는 즉시 JobId 반환, 진행률은 GET 폴링.
/// 단일 서버 환경 전제 (베타). 클러스터 환경에서는 Redis로 교체 필요.
```

**저장소 (L:12):**
```csharp
private readonly ConcurrentDictionary<string, MigrationJob> _jobs = new();
```

### Create (L:14-25)
```csharp
public MigrationJob Create(string tenantId)
{
    var job = new MigrationJob
    {
        JobId = Guid.NewGuid().ToString(),
        TenantId = tenantId,
        Status = "queued",
        StartedAt = DateTime.UtcNow
    };
    _jobs[job.JobId] = job;
    return job;
}
```

### Get (L:27-28)
```csharp
public MigrationJob? Get(string jobId)
    => _jobs.TryGetValue(jobId, out var job) ? job : null;
```

### Update (L:30-34)
```csharp
public void Update(string jobId, Action<MigrationJob> update)
{
    if (_jobs.TryGetValue(jobId, out var job))
        update(job);
}
```

### MigrationJob 클래스 (L:37-48)
| 필드 | 타입 | 주석/용도 |
|---|---|---|
| JobId | string | GUID |
| TenantId | string | 격리 키 |
| Status | string | `queued \| running \| completed \| failed` |
| CurrentStep | string | "초기화" / "MDB 읽기 + Bulk INSERT 진행 중..." / "완료" |
| StartedAt | DateTime | UTC |
| FinishedAt | DateTime? | nullable |
| Result | MigrationJobResult? | 완료 시 |
| ErrorMessage | string? | 실패 시 |

### MigrationJobResult 클래스 (L:50-68) — 16개 int 필드
```
Partners, Items, BomHeaders, Employees,
SalesOrders, PurchaseOrders, StockLedger, Collections,
Cashbook, Expenses, PurchaseOrdersFromIU, SalesOrdersFromIO,
TaxInvoices, Bills, CardPayments, BankTransactions
```

---

## 9. 세미콜론·괄호 게이트 (한 글자도 빠뜨리지 말 것)

| 체크 | 위치 | 상태 |
|---|---|---|
| `[Authorize(Policy = "TenantAdminOnly")]` 마침표 | L:20 | ✅ |
| `using var scope = _scopeFactory.CreateScope();` 세미콜론 | L:199 | ✅ |
| `_ = Task.Run(async () => { ... });` 닫는 세미콜론 | L:231 | ✅ |
| `tx.Commit();` 마이그레이션 트랜잭션 닫기 | 서비스 L:202 | ✅ |
| `record MdbMigrationRequest { }` 중괄호 | L:266-279 | ✅ |
| `ConcurrentDictionary<string, MigrationJob> _jobs = new();` | Store L:12 | ✅ |

---

## 10. 헌법 적용 표

| 헌법 | 조항 | 적용 line | 상태 |
|---|---|---|---|
| #1 | tenant_id JWT 클레임 | L:53, 121, 185, 242 | ✅ HttpContext.Items만 |
| #15 | 빈 catch 금지 | L:71-109, 138-175, 220-229 | ✅ 모두 로깅 |
| #19 | warnings 0 | L:21 [SupportedOSPlatform] | ✅ CA1416 해소 |
| #20 | 워크플로우 끊김 금지 | Task.Run + Token.None (L:203) | ⚠️ 단일 거대 tx 진앙은 서비스 측 |
