# 07. 검증팀장 V1~V7 시나리오 + EVF 매핑

**작성:** 검증팀장
**일자:** 2026-05-14 새벽

---

## 1. V1~V7 구체화 표

| # | 시나리오 | 입력 | 기대 출력 | 측정법 | 통과 기준 |
|---|---|---|---|---|---|
| V1 | 빈 MDB | 빈 PYOJUN(0건) | partners 0건, 1초 | `result.Partners=0`, sw.Elapsed<1s | ≤1초 AND row=0 |
| V2 | 스모크 3건 | W2 D5 PYOJUN(3건) | partners 3건, 2초 | `SELECT COUNT(*) FROM partners` | ≤2초 AND row=3 |
| V3 | 실 60만+ | 사장님 원본(collections 614K, stock 116K) | 전체 완료 | `migration_jobs.status='completed'`, total≥730K | **≤60분 AND errors=0** |
| V4 | 중간 중단 | V3 10분 경과 → 강제 KILL | 좀비 0, 체크포인트 보존, undo<10초 | `SHOW FULL PROCESSLIST` Killed 없음 + checkpoint 존재 | Killed=0 AND checkpoint 존재 |
| V5 | 재시작 | V4 직후 동일 job_id Resume | 남은 50% 처리, 중복 INSERT=0 | row_count = V3 동일, `error_code='DUPLICATE_SKIPPED'` | row 동일 AND 신규 INSERT=0 |
| V6 | 동시 마이그 | tenant A·B 동시 | 락 충돌 0, 격리 OK | `Task.WhenAll`, 각 tenant row 독립 | <60분 각각 AND 오염 0 |
| V7 | 빌드 | `dotnet build -c Release` | errors 0, warnings 0 | 콘솔+ `<TreatWarningsAsErrors>` | error=0 AND warning=0 |

---

## 2. EVF 6대 영역 매핑

| EVF | 정의 | 대응 V | 코드 검증 |
|---|---|---|---|
| ① 부하 | 동시·대용량 | V3, V6 | BatchSize=2000, 청크 병렬 회피(#16), buffer_pool=2GB |
| ② 장애 | 정전·끊김 | V4, V5 | detachedCts 30분, migration_checkpoints, tx.Rollback 로깅 |
| ③ 악의 | 보안·격리 | V6 | tenant_id JWT(#1), AES 5컬럼(#5), tenant 필수 WHERE |
| ④ 혼돈 | 멱등·중복 | V5 | INSERT IGNORE/UPDATE, source_hash, 99회 재실행 불변 |
| ⑤ 무지 | 사용자 오작동 | V2, V7 | PreviewAsync, status 흐름, warnings 0 |
| ⑥ 노후 | 구식 환경 | V1, V2 | OleDb 32bit, 비번 지원, schema 추적 |

---

## 3. V4 좀비 롤백 재현 (심화)

진입점: `MdbMigrationService.cs:50-65` detachedCts 30분

**재현:**
1. `dotnet run` 시작
2. UI에서 60만+ 마이그 클릭
3. **5분 후** Ctrl+C / `Stop-Process`
4. `SHOW FULL PROCESSLIST`
   - ❌ `ID xxx | Killed | 929s | Rollback` (좀비)
   - ✅ Killed 없음
5. `SELECT FROM migration_checkpoints WHERE job_id=@id` — 5개 행 보존 = 재개 가능

**성공 지표:** Killed=0 AND undo<10초 AND checkpoint 존재

---

## 4. V5 멱등 (source_hash)

```csharp
var r1 = await service.MigrateAsync(mdbPath, tid, ct);
var c1 = db.ExecuteScalar<int>("SELECT COUNT(*) FROM partners WHERE tenant_id=@tid", new{tid});
for(int i=0;i<99;i++) await service.MigrateAsync(mdbPath, tid, ct);
var cF = db.ExecuteScalar<int>(...);
Assert.Equal(c1, cF);  // 100회 후 불변
```

측정 SQL:
```sql
SELECT COUNT(*) FROM migration_errors
WHERE job_id=@jobId AND error_code='DUPLICATE_SKIPPED';
```

---

## 5. V6 격리 검증

```csharp
var taskA = service.MigrateAsync(mdb, tenantA, ct);
var taskB = service.MigrateAsync(mdb, tenantB, ct);
await Task.WhenAll(taskA, taskB);
Assert.Equal(3, db.ExecuteScalar<int>("...WHERE tenant_id=@a"));
Assert.Equal(3, db.ExecuteScalar<int>("...WHERE tenant_id=@b"));
```

격리 SQL:
```sql
SELECT DISTINCT tenant_id FROM partners; -- 2개만
SELECT tenant_id, COUNT(*) FROM partners GROUP BY tenant_id;
```

---

## 6. e2e 자동화 확장 (tools/mdb-migration-e2e.mjs)

추가 항목:
1. V1~V7 자동 스크립트
2. 성능 메트릭 (청크 commit, 메모리 RSS, rows/sec)
3. 검증 SQL 자동화 (`tools/verify-migrations.sql`)

---

## 7. 서브에이전트 분담

| 역할 | 담당 V | 검증 |
|---|---|---|
| 검증팀장 | V1, V7 | 진입점·빌드 |
| DB매니저 | V3·V4·V5 | 대용량·재개·멱등 |
| 백엔드매니저 | V2·V6 | 스모크·동시성·#16 감시 |
| 보안매니저 | V6 | tenant 격리·AES 평문 0 |
| QA 리드 | V1~V7 자동화 | e2e + 성능 |

---

## 8. 일정

- 5/14 14:00~ 본 문서 배포
- 5/14 15:00~ 담당자 V 수행 + 로그
- 5/14 17:00 통과/실패 현황판 사장님 검토
- 5/14 18:00 결재 후 써밋
