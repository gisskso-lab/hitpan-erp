# 단위 테스트 시나리오 명세서 — W2 D2~D4 마이그 인프라

> **작성:** 2026-05-12 야간 / 검증팀장 데이비드 박 + 본부장 춘식
> **헌법:** #19 errors 0 + warnings 0, #20 멱등, #23 5중 검증
> **참조:** ALTER_52_COLUMNS.md, VALUE_CONVERTER_SPEC.md, CLASS_SEPARATION_SPEC.md
> **EVF:** 6대 영역 전수 검증

⚠️ **모든 테스트는 풀스택 검증 — DB·백엔드·프론트 흐름 끊김 0 (feedback_real_validation.md).**

---

## 1. 테스트 영역 6대 (EVF 매핑)

| # | 영역 | 테스트 카테고리 |
|---|---|---|
| ① 부하 | LoadTest | 동시·대용량 |
| ② 장애 | ResilienceTest | 정전·끊김·재시작 |
| ③ 악의 | SecurityTest | 침투·권한 |
| ④ 혼돈 | IdempotencyTest | 중복·재실행 |
| ⑤ 무지 | UsabilityTest | 사용자 오작동 |
| ⑥ 노후 | LongevityTest | 1년·3년 후 |

---

## 2. 영역 ④ 혼돈 — 멱등성 (Idempotency, 최우선)

### 2.1 ALTER 멱등성
```csharp
[Fact]
public async Task Alter_Run_100_Times_No_Duplicate_Columns()
{
    for (int i = 0; i < 100; i++)
    {
        await _db.ExecuteAsync(@"
            ALTER TABLE partners
                ADD COLUMN IF NOT EXISTS card_commission_rate DECIMAL(5,2) DEFAULT 0");
    }

    var count = await _db.ExecuteScalarAsync<int>(@"
        SELECT COUNT(*) FROM information_schema.COLUMNS
        WHERE TABLE_NAME='partners' AND COLUMN_NAME='card_commission_rate'");

    Assert.Equal(1, count);  // 100번 실행해도 1개 컬럼만
}
```

### 2.2 마이그 INSERT 멱등성
```csharp
[Fact]
public async Task Migrate_DOCFB_Run_100_Times_No_Duplicate_Rows()
{
    var sourceRow = CreateMockDocfbRow();

    for (int i = 0; i < 100; i++)
    {
        await _mapper.MigrateDocfbAsync(sourceRow, _tenantId, _ct);
    }

    var count = await _db.ExecuteScalarAsync<int>(@"
        SELECT COUNT(*) FROM stock_ledger
        WHERE tenant_id=@TenantId
          AND JSON_EXTRACT(legacy_pk_json, '$.IJ_DT') = @Dt
          AND JSON_EXTRACT(legacy_pk_json, '$.IJ_IO') = @Io
          AND JSON_EXTRACT(legacy_pk_json, '$.IJ_SEQ') = @Seq
          AND JSON_EXTRACT(legacy_pk_json, '$.IJ_BUY') = @Buy
          AND JSON_EXTRACT(legacy_pk_json, '$.IJ_SUN') = @Sun",
        new { TenantId = _tenantId, /* 5컬럼 PK */ });

    Assert.Equal(1, count);  // UK uk_stock_legacy_pk
}
```

### 2.3 재시작 시나리오
```csharp
[Fact]
public async Task Resume_After_Crash_Continues_From_Last_PK()
{
    // 1. 1,000행 중 500행 처리 후 강제 종료
    var jobId = await _orchestrator.StartAsync(_tenantId, _ct);
    await SimulateCrashAfterRows(500);

    // 2. checkpoints 확인
    var checkpoint = await _db.QuerySingleAsync<MigrationCheckpoint>(
        "SELECT * FROM migration_checkpoints WHERE job_id=@JobId", new { jobId });
    Assert.Equal(500, checkpoint.ProcessedCount);
    Assert.NotNull(checkpoint.LastPkValue);

    // 3. resume → 501행부터 계속
    await _orchestrator.ResumeAsync(jobId, _tenantId, _ct);

    // 4. 최종 1,000행 + 중복 0
    var total = await _db.ExecuteScalarAsync<int>(
        "SELECT COUNT(*) FROM stock_ledger WHERE tenant_id=@TenantId", new { _tenantId });
    Assert.Equal(1000, total);
}
```

---

## 3. 영역 ③ 악의 — 보안 (Security)

### 3.1 테넌트 격리
```csharp
[Fact]
public async Task Other_Tenant_Cannot_Access_Migration_Job()
{
    var tenantA = "tenant-a-uuid";
    var tenantB = "tenant-b-uuid";

    var jobId = await _orchestrator.StartAsync(tenantA, _ct);

    // tenantB가 tenantA의 job 조회 시도
    var result = await _progressService.GetProgressAsync(jobId, tenantB, _ct);
    Assert.Null(result);  // 다른 tenant = 조회 불가
}
```

### 3.2 AES-256 평문 노출 차단
```csharp
[Fact]
public async Task Resident_No_Stored_As_Encrypted_Not_Plaintext()
{
    var plain = "880101-1234567";
    await _employeeService.CreateAsync(new Employee {
        TenantId = _tenantId,
        ResidentNo = plain,
        // ...
    });

    // DB 직접 조회 = 평문 없음 확인
    var raw = await _db.ExecuteScalarAsync<byte[]>(
        "SELECT resident_no_encrypted FROM employees WHERE tenant_id=@TenantId",
        new { _tenantId });

    Assert.NotNull(raw);
    Assert.NotEqual(Encoding.UTF8.GetBytes(plain), raw);
    Assert.True(raw.Length > 16);  // IV 16바이트 + 암호문
}
```

### 3.3 raw_data 응답 노출 차단
```csharp
[Fact]
public async Task Error_API_Response_Does_Not_Include_Raw_Data()
{
    var errorId = await _errorCollector.AddAsync(new MigrationError {
        TenantId = _tenantId,
        RawData = """{"sensitive":"880101-1234567"}"""
    });

    var response = await _errorService.GetErrorsAsync(_jobId, _tenantId, null, null, null, 1, 20, _ct);

    var json = JsonSerializer.Serialize(response);
    Assert.DoesNotContain("880101", json);
    Assert.DoesNotContain("raw_data", json);
    Assert.DoesNotContain("sensitive", json);
}
```

### 3.4 step-up 인증 없이 평문 조회 불가
```csharp
[Fact]
public async Task View_Resident_No_Without_Stepup_Returns_Masked()
{
    var employee = await _employeeService.GetAsync(_employeeId, _tenantId);

    Assert.StartsWith("8801", employee.ResidentNoMasked);
    Assert.EndsWith("*******", employee.ResidentNoMasked);
    Assert.Null(employee.ResidentNoPlain);  // step-up 없으면 NULL
}
```

---

## 4. 영역 ② 장애 — Resilience

### 4.1 DB 끊김 시 graceful degradation
```csharp
[Fact]
public async Task DB_Down_During_Migration_Pauses_Job()
{
    var jobId = await _orchestrator.StartAsync(_tenantId, _ct);
    await SimulateDbDisconnect();

    await Task.Delay(2000);

    var job = await _db.QuerySingleAsync<MigrationJob>(
        "SELECT * FROM migration_jobs WHERE job_id=@JobId", new { jobId });
    Assert.Equal("paused", job.Status);
    Assert.NotNull(job.ErrorSummary);
}
```

### 4.2 OLEDB 끊김 처리
```csharp
[Fact]
public async Task Mdb_File_Locked_Returns_Specific_Error()
{
    // 다른 프로세스가 MDB 열고 있을 때
    using var lockHandle = LockMdbFile();

    var ex = await Assert.ThrowsAsync<MigrationException>(
        () => _orchestrator.StartAsync(_tenantId, _ct));

    Assert.Equal("MDB_LOCKED", ex.Code);
    Assert.Contains("다른 프로그램에서 사용 중", ex.UserMessage);
}
```

---

## 5. 영역 ① 부하 — LoadTest

### 5.1 100만 행 마이그 (3년치 시뮬)
```csharp
[Fact(Skip = "Long running — run on demand")]
public async Task Migrate_1M_Rows_Within_30_Minutes()
{
    await SeedMockMdb(rowCount: 1_000_000);

    var sw = Stopwatch.StartNew();
    var jobId = await _orchestrator.StartAsync(_tenantId, _ct);
    await WaitForCompletion(jobId, timeout: TimeSpan.FromMinutes(30));
    sw.Stop();

    Assert.True(sw.Elapsed < TimeSpan.FromMinutes(30));
    var rows = await _db.ExecuteScalarAsync<long>(
        "SELECT COUNT(*) FROM stock_ledger WHERE tenant_id=@TenantId", new { _tenantId });
    Assert.Equal(1_000_000, rows);
}
```

### 5.2 동시 progress polling 100건
```csharp
[Fact]
public async Task Concurrent_100_Progress_Polls_All_Succeed()
{
    var jobId = await _orchestrator.StartAsync(_tenantId, _ct);

    var tasks = Enumerable.Range(0, 100)
        .Select(_ => _progressService.GetProgressAsync(jobId, _tenantId, _ct))
        .ToArray();

    var results = await Task.WhenAll(tasks);
    Assert.All(results, r => Assert.NotNull(r));
}
```

---

## 6. 영역 ⑤ 무지 — Usability

### 6.1 사장님이 cancel 후 resume 시도
```csharp
[Fact]
public async Task Resume_Canceled_Job_Returns_409_With_Helpful_Message()
{
    var jobId = await _orchestrator.StartAsync(_tenantId, _ct);
    await _orchestrator.CancelAsync(jobId, _tenantId, "test", new CancelRequest());

    var ex = await Assert.ThrowsAsync<MigrationException>(
        () => _orchestrator.ResumeAsync(jobId, _tenantId, "key-1", _ct));

    Assert.Equal("INVALID_STATUS", ex.Code);
    Assert.Contains("이미 취소된", ex.UserMessage);  // 사용자 친화 메시지
}
```

### 6.2 draft 데이터 stock_ledger 진입 차단 (헌법 #6)
```csharp
[Fact]
public async Task Draft_Row_Not_Inserted_Into_Stock_Ledger()
{
    var draftRow = CreateMockDocfbRow(ijOk: "0");  // 미확정

    await _mapper.MigrateDocfbAsync(draftRow, _tenantId, _ct);

    var count = await _db.ExecuteScalarAsync<int>(
        "SELECT COUNT(*) FROM stock_ledger WHERE tenant_id=@TenantId", new { _tenantId });
    Assert.Equal(0, count);  // draft = 진입 차단

    // 단, migration_errors에 warning 기록
    var warn = await _db.ExecuteScalarAsync<int>(@"
        SELECT COUNT(*) FROM migration_errors
        WHERE tenant_id=@TenantId AND error_severity='warning' AND error_message LIKE '%draft%'",
        new { _tenantId });
    Assert.Equal(1, warn);
}
```

---

## 7. 영역 ⑥ 노후 — Longevity

### 7.1 1년 후 마이그 이력 조회
```csharp
[Fact]
public async Task Job_Created_1_Year_Ago_Still_Queryable()
{
    var jobId = await SeedMigrationJob(createdAt: DateTime.UtcNow.AddYears(-1));

    var sw = Stopwatch.StartNew();
    var result = await _progressService.GetProgressAsync(jobId, _tenantId, _ct);
    sw.Stop();

    Assert.NotNull(result);
    Assert.True(sw.ElapsedMilliseconds < 500);  // 인덱스 작동
}
```

### 7.2 자동 파기 배치 (검증)
```csharp
[Fact]
public async Task Resolved_Errors_Older_Than_1_Year_Are_Purged()
{
    await SeedErrors(resolved: true, resolvedAt: DateTime.UtcNow.AddYears(-2));

    await _purgeBatch.RunAsync();

    var remaining = await _db.ExecuteScalarAsync<int>(@"
        SELECT COUNT(*) FROM migration_errors
        WHERE is_resolved=1 AND resolved_at < NOW() - INTERVAL 1 YEAR");
    Assert.Equal(0, remaining);
}
```

---

## 8. 풀스택 검증 시나리오 (feedback_real_validation.md)

### 8.1 DB → API → Web 흐름 끊김 0
```csharp
[Fact]
public async Task Full_Stack_Employee_Creation_Roundtrip()
{
    // 1. API POST → DB INSERT (AES 암호화)
    var response = await _httpClient.PostAsJsonAsync("/api/employees", new {
        emp_name = "테스트 직원",
        resident_no = "880101-1234567",
        salary = 5_000_000
    });
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);

    var created = await response.Content.ReadFromJsonAsync<EmployeeDto>();

    // 2. API GET → DB SELECT (마스킹 + 복호화 차단)
    var getResp = await _httpClient.GetAsync($"/api/employees/{created.Id}");
    var got = await getResp.Content.ReadFromJsonAsync<EmployeeDto>();

    Assert.Equal("880101-*******", got.ResidentNoMasked);
    Assert.Null(got.ResidentNoPlain);  // step-up 없이는 NULL
    Assert.Equal("●●●", got.SalaryMasked);

    // 3. step-up 후 평문 조회
    var stepUpToken = await StepUpAuthAsync();
    var fullResp = await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/employees/{created.Id}/sensitive")
    {
        Headers = { { "X-StepUp-Token", stepUpToken } }
    });
    var full = await fullResp.Content.ReadFromJsonAsync<EmployeeDto>();
    Assert.Equal("880101-1234567", full.ResidentNoPlain);

    // 4. 감사로그 INSERT 확인
    var log = await _db.QuerySingleAsync<SensitiveAccessLog>(@"
        SELECT * FROM sensitive_access_log
        WHERE target_table='employees' AND target_id=@Id ORDER BY accessed_at DESC LIMIT 1",
        new { created.Id });
    Assert.Equal("view", log.Action);
}
```

---

## 9. 헌법 부합 매트릭스

| 헌법 | 테스트 |
|---|---|
| #2 tenant_id JWT | §3.1 다른 tenant 차단 |
| #3 INSERT ONLY | §6.2 stock_ledger UPDATE 0 |
| #5 AES-256 | §3.2 평문 X |
| #6 confirmed 시점만 원장 | §6.2 draft 차단 |
| #15 빈 catch 금지 | 모든 catch 검증 |
| #18 본사 송신 0 | §3.3 raw_data 응답 X |
| #19 errors 0 + warnings 0 | 빌드 + CI 검증 |
| #20 멱등성 | §2.1·§2.2·§2.3 |
| #22 데이터 최소주의 | §3.3·§3.4 |
| #23 5중 검증 | 전체 자동화 |

---

## 10. 실행 인프라

### 10.1 테스트 DB 격리
- xUnit + Testcontainers (MariaDB 11.4.10 Docker)
- 각 테스트마다 트랜잭션 롤백 또는 별도 DB

### 10.2 CI 통합
- GitHub Actions 또는 로컬 `dotnet test`
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` (헌법 #19)
- 커버리지 목표 80%+

### 10.3 성과 지표
- 단위 테스트: 100건 (W2 완료 시점)
- 통합 테스트: 30건
- E2E 풀스택: 10건 (8.1 같은 시나리오)

---

## 11. 사장님 결재 사항

| # | 사항 | 결재 |
|---|---|---|
| 1 | 단위 테스트 100건 + 통합 30건 + E2E 10건 | ✅ |
| 2 | TreatWarningsAsErrors=true CI | ✅ 헌법 #19 |
| 3 | step-up 5분 평문 노출 정책 | ✅ |
| 4 | sensitive_access_log 신규 (별도 작업지시서) | ⚠️ |
| 5 | LongRunning 테스트는 nightly로 분리 | ⚠️ |

---

**작성:** 검증팀장 데이비드 박 + 본부장 춘식
**검토:** CTO 래리 앨리슨, 보안매니저, 백엔드매니저
**적용 시점:** W2 D3~D5 코드 추출 시 동시 작성
