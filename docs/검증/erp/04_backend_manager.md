# 04. 백엔드 매니저 정독 보고서

**작성:** 백엔드 매니저 (Harvard 석사, Oracle 30년)
**일자:** 2026-05-14 새벽

---

## 1. API 흐름도

```
[Blazor]
    ↓
[MigrationController]
    ├ GET  /preview   → MdbMigrationService.PreviewAsync()
    ├ POST /          → MigrateAsync() ⚠ Cloudflare 524 위험 (동기 100s)
    └ POST /start     → JobStore.Create() → Task.Run(scope) ✅
        ├ scope.GetRequiredService<MdbMigrationService>()
        ├ MigrateAsync()
        │   ├ SET SESSION (unique=0, fk=0, lock_wait=600 …)
        │   ├ BeginTransaction
        │   ├ 16개 Migrate*Async → BulkInsertAsync (2000행/배치)
        │   ├ Commit / Rollback
        │   └ SET SESSION 복원 (finally)
        └ JobStore.Update (상태·결과)

    └ GET /status/{jobId} → JobStore.Get (2초 폴링)
```

---

## 2. 발견한 백엔드 함정 3개

### 함정 #1: AsyncLocal 안전성 미흡 (L:35, 81)
```csharp
private static readonly AsyncLocal<string?> _mdbPasswordContext = new();
```
- PreviewCoreAsync(L:85-130)에 try/finally 누락
- 예외 발생 시 컨텍스트 오염 → 다음 요청 누수
- **권고:** PreviewCoreAsync에 try/finally 추가, finally에서 `= null`

### 함정 #2: commandTimeout 일관성 부재 (L:294-331)
- ✅ EnsureMigrationWarehouseAsync: commandTimeout=600
- ✅ EnsureMigrationEmployeeAsync: commandTimeout=600
- ✅ BulkInsertAsync: BulkCopyTimeout=0
- ❌ **PreviewCoreAsync `_db.ExecuteScalarAsync`** — 기본 90초 사용 (마이그 중 갑작스런 Timeout 위험)
- **권고:** Preview도 commandTimeout=600 명시

### 함정 #3: Task.Run 백그라운드 예외 먹힘 (L:193-231)
```csharp
_ = Task.Run(async () => { ... });
```
- Task 반환값 버림 → UnobservedTaskException 위험
- 예외는 `_logger.LogError`(L:223)로 저장되지만 ThreadPool에서 호스트 강제 종료 가능
- Cloudflare 524는 회피했지만 서버 안정성 해침
- **권고:** HostedService(BackgroundService) 전환 + Channel<JobId> 큐

---

## 3. 옵션 B 구현 권고

### Step 1) Controller 분리
```
[MigrationJobController]   ← 새로 추가
  POST /start
  GET  /status/{jobId}

[MigrationController]     ← 기존 유지
  GET  /preview
  POST /                  ← deprecated 경고
```

### Step 2) HostedService 추가
```csharp
public sealed class MigrationBackgroundWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Channel<string> 큐 폴링 + 예외 catch + 재시도 정책
        // AppLifetime graceful shutdown 지원
    }
}
```

### Step 3) 재시도 정책 (Polly)
```csharp
Policy.Handle<OleDbException>().Or<InvalidOperationException>()
      .WaitAndRetryAsync(3, retry => TimeSpan.FromSeconds(Math.Pow(2, retry)),
                         (ex, ts, retry) => _logger.LogWarning(ex, "재시도 {Retry}/3", retry));
```

### Step 4) DI 등록 (Program.cs)
```csharp
builder.Services.AddHostedService<MigrationBackgroundWorker>();
builder.Services.AddScoped<IMigrationJobQueue, MigrationJobQueue>();
```

---

## 4. 헌법 준수 검증

| 헌법 | 상태 | 비고 |
|---|---|---|
| #1 (구조 변경 금지) | ✅ | 기존 패턴 |
| #15 (빈 catch 금지) | ✅ | 모두 로깅 |
| #16 (MySqlConn + Task.WhenAll 금지) | ⚠️ | MySqlBulkCopy 캐스팅 정상, WhenAll 미사용 |
| #19 (warnings 0) | ✅ | CA1416 pragma 명시 |
| #20 (워크플로우 끊김) | ✅ | tx.Rollback + finally 완벽 |

---

## 5. 서브에이전트(백엔드 4명) 분담

| 담당 | 작업 | 일정 |
|---|---|---|
| 개발자 A (시니어) | AsyncLocal 안전성 + Preview commandTimeout 일관성 | 1일 |
| 개발자 B (미들) | HostedService 마이그 + Polly 재시도 | 2일 |
| 개발자 C (주니어) | 테스트 케이스 (OleDb/InvalidOp/Timeout 시뮬) | 1일 |
| QA | Blazor UI 폴링 + 100만+행 E2E | 2일 |

---

## 결론

즉시 개선:
1. AsyncLocal 누수 (함정 #1) → 안전성↑
2. commandTimeout 일관성 (함정 #2) → 예측 가능
3. Task.Run → HostedService (함정 #3) → 서버 안정성↑↑

현 상태: Cloudflare 524 회피 OK, 단일 서버 합격. 클러스터 시 JobStore Redis 전환.
