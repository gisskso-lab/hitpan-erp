# 마이그레이션 5개 클래스 분리 설계서

> **작성:** 2026-05-12 W1 D4 / 설계팀장 브라운킴 + 본부장 춘식
> **헌법:** #1 (수정 OK 덮어쓰기 X), #16 (MySqlConn+Task.WhenAll), #20 (워크플로우)
> **원칙:** 기존 1,755줄 **추출만** — 새로 짜는 게 아니라 분리

⚠️ **헌법 #1 정확히 적용:** 기존 코드 1,755줄에서 **로직 그대로 추출**만 수행. 변경 시 즉시 반려.

---

## 1. 설계 원칙

### 1.1 단일 책임 (SRP)
각 클래스는 하나의 책임만:
- Orchestrator: 흐름 제어 (가장 위)
- Reader: OLEDB 통신 (가장 아래)
- Mapper: 변환 로직 (핵심)
- CheckpointService: 진행률 추적
- ErrorCollector: 실패 수집

### 1.2 의존성 방향
```
[Controller]
     ↓
[Orchestrator] ───┬──→ [Reader] ──→ OLEDB
                  ├──→ [Mapper] ──→ MariaDB
                  ├──→ [CheckpointService] ──→ migration_checkpoints
                  └──→ [ErrorCollector]    ──→ migration_errors
```

**역방향 참조 금지** (예: Reader가 Orchestrator 호출 X)

### 1.3 헌법 #16 — 순차 처리
모든 청크 처리는 `await foreach` 순차. `Task.WhenAll` 절대 사용 X.

---

## 2. 클래스 1 — `MdbMigrationOrchestrator` (200줄)

### 책임
- 전체 마이그 흐름 제어
- migration_jobs row 생성·업데이트
- 청크 루프 + 체크포인트 호출
- 사용자 [재개]·[취소] 처리

### 인터페이스
```csharp
namespace HitPan.Application.Services.Migration;

public interface IMdbMigrationOrchestrator
{
    /// <summary>마이그 작업 시작 (job_id 반환)</summary>
    Task<string> StartAsync(StartMigrationRequest request, string tenantId, string userId, CancellationToken ct);
    
    /// <summary>중단된 마이그 재개</summary>
    Task ResumeAsync(string jobId, string tenantId, CancellationToken ct);
    
    /// <summary>마이그 취소</summary>
    Task CancelAsync(string jobId, string tenantId, bool rollback, CancellationToken ct);
    
    /// <summary>미리보기 (실제 INSERT 안 함)</summary>
    Task<MigrationPreviewDto> PreviewAsync(string folderPath, string tenantId, CancellationToken ct);
}

public sealed class MdbMigrationOrchestrator : IMdbMigrationOrchestrator
{
    private readonly IMdbReader _reader;
    private readonly IMdbToHitpanMapper _mapper;
    private readonly IMigrationCheckpointService _checkpoints;
    private readonly IMigrationErrorCollector _errors;
    private readonly IDbConnection _db;
    private readonly ILogger<MdbMigrationOrchestrator> _logger;
    
    // ... 구현 약 200줄
}
```

### 핵심 메서드 흐름
```csharp
public async Task<string> StartAsync(...)
{
    // 1) migration_jobs row 생성
    var jobId = await CreateJobAsync(...);
    
    // 2) 23개 테이블 순차 처리 (FK 의존성 순서)
    var tables = GetMigrationOrder();  // PYOJUN → PANDATA → POTHER 순
    
    foreach (var table in tables)
    {
        var checkpoint = await _checkpoints.GetOrCreateAsync(jobId, table, ct);
        if (checkpoint.Status == "done") continue;  // 멱등성
        
        try
        {
            await MigrateTableAsync(jobId, table, checkpoint, ct);
            await _checkpoints.MarkDoneAsync(checkpoint.CheckpointId, ct);
        }
        catch (Exception ex)
        {
            await _errors.CollectAsync(jobId, table, ex, ct);
            // 헌법 #15: 빈 catch 금지
            _logger.LogWarning(ex, "Table {Table} failed", table);
            
            if (IsRecoverable(ex))
            {
                await UpdateJobStatusAsync(jobId, "paused", ct);
                return jobId;  // 재개 가능
            }
            
            await UpdateJobStatusAsync(jobId, "failed", ct);
            throw;
        }
    }
    
    await UpdateJobStatusAsync(jobId, "completed", ct);
    return jobId;
}

private async Task MigrateTableAsync(string jobId, string tableName, Checkpoint cp, CancellationToken ct)
{
    var chunkSize = cp.ChunkSize;
    var lastPk = cp.LastPkValue;
    
    while (true)
    {
        var stopwatch = Stopwatch.StartNew();
        
        // 1) 청크 읽기 (Reader)
        var batch = await _reader.ReadChunkAsync(tableName, lastPk, chunkSize, ct);
        if (batch.Count == 0) break;
        
        // 2) 트랜잭션 시작
        using var tx = _db.BeginTransaction();
        
        // 3) 매핑·INSERT (Mapper) — 헌법 #16: Task.WhenAll 금지, 순차 처리
        foreach (var row in batch)
        {
            try
            {
                await _mapper.MapAndInsertAsync(tableName, row, tx, ct);
            }
            catch (Exception ex)
            {
                await _errors.CollectRowAsync(jobId, tableName, row, ex, ct);
                // 한 행 실패 = 청크 전체 중단 X (단 critical은 throw)
                if (IsCritical(ex)) throw;
            }
        }
        
        // 4) commit + 체크포인트
        tx.Commit();
        lastPk = ExtractLastPk(batch.Last(), tableName);
        await _checkpoints.UpdateAsync(cp.CheckpointId, batch.Count, lastPk, ct);
        
        // 5) 동적 청크 조정
        var commitMs = stopwatch.ElapsedMilliseconds;
        chunkSize = AdjustChunkSize(chunkSize, commitMs);
    }
}

private int AdjustChunkSize(int current, long commitMs)
{
    if (commitMs < 500 && current < 10_000) return Math.Min(current * 2, 10_000);
    if (commitMs > 1500 && current > 100) return Math.Max(current / 2, 100);
    return current;
}
```

### 외부 참조
```csharp
// Program.cs DI 등록
services.AddScoped<IMdbMigrationOrchestrator, MdbMigrationOrchestrator>();
```

---

## 3. 클래스 2 — `MdbReader` (300줄)

### 책임
- OleDbConnection 관리
- 청크 단위 SELECT (last_pk_value 이후)
- 한글 인코딩 처리
- DataTable → IEnumerable<dynamic> 변환

### 인터페이스
```csharp
[SupportedOSPlatform("windows")]
public interface IMdbReader
{
    /// <summary>MDB 파일 연결 (using 권장)</summary>
    Task<IDisposable> OpenAsync(string folderPath, MdbFile file, CancellationToken ct);
    
    /// <summary>테이블 건수만 조회 (미리보기용)</summary>
    Task<int> CountAsync(string tableName, CancellationToken ct);
    
    /// <summary>청크 단위 행 읽기</summary>
    Task<IReadOnlyList<MdbRow>> ReadChunkAsync(
        string tableName, 
        object? lastPkValue, 
        int chunkSize, 
        CancellationToken ct);
    
    /// <summary>테이블 컬럼 메타데이터 조회</summary>
    Task<IReadOnlyList<MdbColumn>> GetColumnsAsync(string tableName, CancellationToken ct);
}

public sealed class MdbReader : IMdbReader, IDisposable
{
    private OleDbConnection? _conn;
    private readonly ILogger<MdbReader> _logger;
    
    private const string ConnTemplate = 
        "Provider=Microsoft.ACE.OLEDB.12.0;Data Source={0};Jet OLEDB:Database Password=;";
    
    // ... 구현 약 300줄
}
```

### 핵심 메서드 — 청크 읽기
```csharp
public async Task<IReadOnlyList<MdbRow>> ReadChunkAsync(
    string tableName, object? lastPkValue, int chunkSize, CancellationToken ct)
{
    if (_conn == null) throw new InvalidOperationException("OpenAsync 먼저 호출");
    
    var pkColumns = GetPkColumns(tableName);  // 32개 테이블 매핑 표 참조
    var orderBy = string.Join(", ", pkColumns);
    var whereClause = BuildWhereClause(pkColumns, lastPkValue);
    
    var sql = $"SELECT TOP {chunkSize} * FROM [{tableName}] {whereClause} ORDER BY {orderBy}";
    // ⚠️ Access SQL은 TOP N 사용. LIMIT 미지원
    
    using var cmd = new OleDbCommand(sql, _conn);
    using var reader = await Task.Run(() => cmd.ExecuteReader(), ct);
    
    var rows = new List<MdbRow>(chunkSize);
    while (reader.Read())
    {
        var row = new MdbRow();
        for (int i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
            row[name] = value;
        }
        rows.Add(row);
    }
    
    return rows;
}

private static string BuildWhereClause(IReadOnlyList<string> pkColumns, object? lastPkValue)
{
    if (lastPkValue == null) return "";  // 첫 청크
    
    // 복합 PK 처리 (DOCFB 5컬럼)
    if (lastPkValue is JsonElement json)
    {
        var conditions = pkColumns.Select(pk => {
            var val = json.GetProperty(pk).GetString();
            return $"[{pk}] > '{EscapeSql(val)}'";
        });
        return "WHERE " + string.Join(" AND ", conditions);
    }
    
    return $"WHERE [{pkColumns[0]}] > {FormatSqlValue(lastPkValue)}";
}
```

### Provider 의존성 처리
```csharp
private bool CheckAceOleDbAvailable()
{
    try
    {
        var test = new OleDbConnection("Provider=Microsoft.ACE.OLEDB.12.0;");
        return true;
    }
    catch
    {
        _logger.LogWarning("ACE OLEDB 12.0 Provider 없음 - Office 64bit 설치 안내 필요");
        return false;
    }
}
```

---

## 4. 클래스 3 — `MdbToHitpanMapper` (800줄)

### 책임
- 23개 테이블 변환 로직 (기존 1,755줄에서 추출)
- FK 매핑 딕셔너리 관리 (partnerMap, itemMap, employeeMap)
- 헬퍼 함수 (GetStr, GetDec, GetInt, ParseLegacyDate, BuildItemKey)
- 단일 행 INSERT (Orchestrator가 청크 루프)

### 인터페이스
```csharp
public interface IMdbToHitpanMapper
{
    /// <summary>단일 행을 신 히트판 테이블로 매핑·INSERT</summary>
    Task MapAndInsertAsync(
        string mdbTableName, 
        MdbRow row, 
        IDbTransaction tx, 
        CancellationToken ct);
    
    /// <summary>FK 매핑 딕셔너리 초기화 (마이그 시작 시)</summary>
    Task InitializeMapsAsync(string tenantId, CancellationToken ct);
    
    /// <summary>FK 매핑 딕셔너리 영속화 (재개 가능하도록)</summary>
    Task PersistMapsAsync(string jobId, CancellationToken ct);
}

public sealed class MdbToHitpanMapper : IMdbToHitpanMapper
{
    // FK 매핑 (기존 코드 그대로)
    private Dictionary<int, string> _partnerMap = new();
    private Dictionary<string, string> _itemMap = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _employeeMap = new(StringComparer.OrdinalIgnoreCase);
    
    private readonly IDbConnection _db;
    private readonly ILogger<MdbToHitpanMapper> _logger;
    
    // 32개 테이블 매핑 라우터
    private readonly Dictionary<string, Func<MdbRow, IDbTransaction, CancellationToken, Task>> _routers;
    
    public MdbToHitpanMapper(IDbConnection db, ILogger<MdbToHitpanMapper> logger)
    {
        _db = db;
        _logger = logger;
        _routers = new Dictionary<string, Func<MdbRow, IDbTransaction, CancellationToken, Task>>
        {
            ["DOCF8"]    = MapPartnerAsync,
            ["DOCFS"]    = MapItemAsync,
            ["DOCRT"]    = MapBomAsync,
            ["DOCSW"]    = MapEmployeeAsync,
            ["COSTNO"]   = MapCostnoAsync,        // 🔴 신규
            ["SETUP"]    = MapSettingAsync,       // 🔴 신규
            ["DOCF1"]    = MapTransactionLineAsync,
            ["DOCF2"]    = MapTransactionHeaderAsync,
            ["DOCFB"]    = MapStockLedgerAsync,
            ["DOCF4"]    = MapTaxInvoiceAsync,
            ["DOCF5"]    = MapCollectionAsync,
            ["DOCF6"]    = MapCashbookAsync,
            ["DOCF7"]    = MapExpenseAsync,
            ["DOCFA"]    = MapPurchaseOrderAsync,
            ["DOCFO"]    = MapSalesOrderAsync,
            ["DOCF9"]    = MapBillIssueAsync,
            ["DOCFQ"]    = MapBillMaturityAsync,
            ["DOCCD"]    = MapCardPaymentAsync,
            ["DOCCD1"]   = MapCardPaymentLineAsync,
            ["BANKF"]    = MapBankTransactionAsync,
            ["DOCFC"]    = MapMonthlyInventoryAsync,  // 🔴 신규
            ["DOCFE"]    = MapTransactionExtraAsync,  // 🔴 신규
            ["CALENDAR"] = MapCalendarAsync,           // 🔴 신규
            // DOCLT, REMARK1, LOCK1 = 마이그 불필요
            // DELIVERY, DOCAS, DOCAS1, DOCME, DOCNM, DOCSC = 베타 후
        };
    }
    
    public Task MapAndInsertAsync(string mdbTableName, MdbRow row, IDbTransaction tx, CancellationToken ct)
    {
        if (!_routers.TryGetValue(mdbTableName, out var router))
        {
            _logger.LogWarning("Unknown table: {Table}", mdbTableName);
            return Task.CompletedTask;
        }
        return router(row, tx, ct);
    }
    
    // ── 기존 1,755줄에서 추출 ──
    // MapPartnerAsync = 기존 MigratePartnersAsync 본문 그대로
    // MapItemAsync = 기존 MigrateItemsAsync 본문 그대로
    // ... (헌법 #1: 변경 0, 추출만)
}
```

### 헬퍼 메서드 (기존 그대로)
```csharp
private static string GetStr(MdbRow row, string key) { ... }
private static decimal GetDec(MdbRow row, string key) { ... }
private static int GetInt(MdbRow row, string key) { ... }
private static DateTime? ParseLegacyDate(string yyyymmdd) { ... }
private static string BuildItemKey(string pum, string ku) { ... }
```

---

## 5. 클래스 4 — `MigrationCheckpointService` (150줄)

### 책임
- migration_checkpoints CRUD
- last_pk_value 직렬화 (단일/복합 PK)
- 동적 청크 크기 추적

### 인터페이스
```csharp
public interface IMigrationCheckpointService
{
    Task<Checkpoint> GetOrCreateAsync(string jobId, string tableName, CancellationToken ct);
    Task UpdateAsync(string checkpointId, int processedCount, object? lastPkValue, CancellationToken ct);
    Task MarkDoneAsync(string checkpointId, CancellationToken ct);
    Task MarkFailedAsync(string checkpointId, string errorMessage, CancellationToken ct);
    Task<IReadOnlyList<Checkpoint>> GetPendingAsync(string jobId, CancellationToken ct);
}

public sealed class MigrationCheckpointService : IMigrationCheckpointService
{
    private readonly IDbConnection _db;
    private readonly ILogger<MigrationCheckpointService> _logger;
    
    public async Task UpdateAsync(string checkpointId, int processedCount, object? lastPkValue, CancellationToken ct)
    {
        var lastPkJson = lastPkValue != null 
            ? JsonSerializer.Serialize(lastPkValue) 
            : null;
        
        const string sql = """
            UPDATE migration_checkpoints
               SET processed_count = processed_count + @Delta,
                   last_pk_value = @LastPk,
                   updated_at = @Now
             WHERE checkpoint_id = @CheckpointId
            """;
        
        await _db.ExecuteAsync(new CommandDefinition(sql, new
        {
            Delta = processedCount,
            LastPk = lastPkJson,
            Now = DateTime.UtcNow,
            CheckpointId = checkpointId
        }, cancellationToken: ct));
    }
    
    // ... 약 150줄
}
```

---

## 6. 클래스 5 — `MigrationErrorCollector` (100줄)

### 책임
- migration_errors INSERT
- raw_data AES-256 암호화
- 사용자 표시용 메시지 마스킹

### 인터페이스
```csharp
public interface IMigrationErrorCollector
{
    Task CollectAsync(string jobId, string tableName, Exception ex, CancellationToken ct);
    Task CollectRowAsync(string jobId, string tableName, MdbRow row, Exception ex, CancellationToken ct);
    Task<int> GetCountAsync(string jobId, string severity, CancellationToken ct);
}

public sealed class MigrationErrorCollector : IMigrationErrorCollector
{
    private readonly IDbConnection _db;
    private readonly IEncryptionService _crypto;  // AES-256 (헌법 #5)
    private readonly ILogger<MigrationErrorCollector> _logger;
    
    public async Task CollectRowAsync(string jobId, string tableName, MdbRow row, Exception ex, CancellationToken ct)
    {
        // 1) 에러 분류
        var errorType = ClassifyError(ex);
        var severity = DetermineSeverity(ex);
        
        // 2) 사용자 표시 메시지 (마스킹)
        var userMessage = MaskUserMessage(ex.Message);
        
        // 3) 원본 데이터 AES-256 암호화 (헌법 #5·#18)
        var rawJson = JsonSerializer.Serialize(row.ToDictionary());
        var encryptedRaw = _crypto.Encrypt(rawJson);
        
        // 4) INSERT
        const string sql = """
            INSERT INTO migration_errors
              (error_id, job_id, tenant_id, mdb_file, table_name, row_pk_value,
               error_type, error_severity, error_message, error_detail, raw_data,
               occurred_at)
            VALUES
              (@Id, @JobId, @TenantId, @MdbFile, @TableName, @PkValue,
               @Type, @Severity, @Message, @Detail, @RawData, @Now)
            """;
        
        await _db.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = Guid.NewGuid().ToString(),
            JobId = jobId,
            // ...
            RawData = encryptedRaw,
            Now = DateTime.UtcNow
        }, cancellationToken: ct));
        
        // 헌법 #15: 로그 의무
        _logger.LogWarning(ex, "Row error: {Table} {ErrorType}", tableName, errorType);
    }
}
```

---

## 7. 기존 `MdbMigrationService.cs` 처리 정책

### 단계 1: 보존 (W1 D5 ~ W2 D1)
- 기존 1,755줄 **삭제 X**
- 새 5개 클래스에 로직 추출
- 기존 클래스는 [Obsolete] 마킹

### 단계 2: 마이그 (W2 D2 ~ D5)
- 컨트롤러가 새 Orchestrator 호출
- 기존 메서드 직접 호출 차단

### 단계 3: 폐기 (W3 D5)
- 단위 테스트 100% 통과 후
- 기존 파일 삭제 또는 `Legacy/` 폴더 이동
- 헌법 #1: "수정 OK 덮어쓰기 X" — 단 충분한 검증 후

---

## 8. 의존성 주입 (Program.cs)

```csharp
// HitPan.API/Program.cs
builder.Services.AddScoped<IMdbMigrationOrchestrator, MdbMigrationOrchestrator>();
builder.Services.AddScoped<IMdbReader, MdbReader>();
builder.Services.AddScoped<IMdbToHitpanMapper, MdbToHitpanMapper>();
builder.Services.AddScoped<IMigrationCheckpointService, MigrationCheckpointService>();
builder.Services.AddScoped<IMigrationErrorCollector, MigrationErrorCollector>();

// 기존
builder.Services.AddScoped<MdbMigrationService>();  // [Obsolete] 단계적 폐기
```

---

## 9. 단위 테스트 매트릭스

| 클래스 | 테스트 | 합격 기준 |
|---|---|---|
| Orchestrator | StartAsync_정상 | 32개 테이블 순차 처리 |
| Orchestrator | ResumeAsync_체크포인트 | last_pk_value 이후만 |
| Orchestrator | CancelAsync_rollback | 마이그 데이터 전부 삭제 |
| Reader | OpenAsync_Provider없음 | 명확한 에러 |
| Reader | ReadChunkAsync_복합PK | DOCFB 5컬럼 OK |
| Reader | ReadChunkAsync_한글 | 깨짐 0건 |
| Mapper | MapPartnerAsync | 22컬럼 매핑 정확 |
| Mapper | MapItemAsync_중복 | 첫 번째만 INSERT |
| CheckpointService | UpdateAsync_JSON | 복합 PK 직렬화 |
| ErrorCollector | CollectRowAsync_암호화 | raw_data AES-256 |

**합격 기준:** 단위 테스트 100% 통과 (W3 D5 게이트).

---

## 10. 헌법 준수 매트릭스

| 헌법 | 적용 |
|---|---|
| #1 수정 OK 덮어쓰기 X | ✅ 기존 1,755줄 추출만, 변경 0 |
| #5 암호화 컬럼 | ✅ raw_data AES-256 |
| #15 빈 catch 금지 | ✅ 모든 catch에 LogWarning |
| #16 MySqlConn+Task.WhenAll | ✅ 청크 순차 처리 |
| #17 InnoDB | ✅ 인프라 3 테이블 적용 |
| #18 본사 송신 0 | ✅ 로컬 처리 |
| #19 errors 0 + warnings 0 | ✅ SupportedOSPlatform |
| #20 워크플로우 끊김 X | ✅ 체크포인트 + 재개 |
| #22 데이터 최소주의 | ✅ raw_data 응답 제외 |
| #23 5중 검증 | ✅ |

---

## 11. 일정

```
[W1 D5] 5개 인터페이스 정의 (시그니처만, 구현 X)
[W2 D1] Orchestrator + Reader 구현 (300줄)
[W2 D2] Mapper 추출 1차 (마스터 5개 테이블)
[W2 D3] Mapper 추출 2차 (거래 14개 테이블)
[W2 D4] CheckpointService + ErrorCollector 구현
[W2 D5] 단위 테스트 100%
```

---

**작성:** 설계팀장 브라운킴 + 본부장 춘식
**검토:** DB매니저 (PK 직렬화) + 보안매니저 (AES-256) + 백엔드매니저 (DI)
**최종 검증:** CTO 래리 앨리슨
**적용:** W1 D5 게이트 통과 후
