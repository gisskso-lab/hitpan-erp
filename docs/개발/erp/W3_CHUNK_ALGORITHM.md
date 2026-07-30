# W3 청크 알고리즘 설계서 — 대량 마이그레이션 (100만 건 / 1시간)

> **작성일:** 2026-05-13 (W2 D5 야간, W3 진입 직전)
> **작성자:** DB매니저 + 백엔드매니저 + 본부장 (사전학습 산출물)
> **선행 문서:** MIGRATION_MASTER_PLAN.md, INFRA_DDL_SPEC.md, VALUE_CONVERTER_SPEC.md, ALTER_52_COLUMNS.md
> **헌법 게이트:** #1 (수정 OK), #3 (INSERT ONLY 원장), #4 (decimal), #5 (암호화), #15 (catch 로깅), #16 (MySqlConn + Task.WhenAll 금지), #17 (InnoDB), #19 (warnings 0), #20 (워크플로우 끊김 0)
> **상태:** 조사·설계 문서. 코드 변경 없음. W3 작업지시서 발행 전 사장님 결재 대상.

---

## 0. 한 줄 결론

> **테이블별 차등 초기 청크 + AIMD 동적 조정 + 청크 단위 트랜잭션 + last_pk_value JSON 체크포인트 + raw_data AES-256 JSON 에러 적재 — 100만 건 / 1시간(280 rows/sec 평균, 피크 800 rows/sec) 달성 가능.**

---

## 1. 배경·범위·전제

### 1.1 W1·W2 완료 상태
- W1: 인프라 3테이블 DDL + 4 API 골격 + 5클래스 분리 (게이트 6/6 PASS, 5/12 야간).
- W2: D2 ALTER 52컬럼, D3 IBinaryCryptoService 추상화, D4 단위 테스트 18케이스, D5 PYOJUN.MDB 3건 스모크 PASS.
- 현재 코드 진입점: `src/HitPan.Application/Services/MdbMigrationService.cs` (1,886줄, OleDb + Dapper). W3에서 청크·체크포인트·동적 조정만 추가, 기존 로직 70% 유지(헌법 #1).

### 1.2 W3 목표
| 항목 | 목표 |
|---|---|
| 처리량 | 100만 건 / 60분 = **평균 280 rows/sec** |
| 피크 처리량 | DOCFB 1청크(5,000건) commit < 7초 = **700~800 rows/sec** |
| 메모리 | 워커 프로세스 RSS **< 800 MB** (1청크 메모리 < 200 MB) |
| 중단 재개 | `last_pk_value` JSON 기반, 재실행 시 중복 0 |
| 에러율 | 행 단위 < 0.5%, 청크 단위 fail 0건 (재시도 3회 내 흡수) |

### 1.3 전제·제약
- 단일 MariaDB 11.4.10 인스턴스 (로컬 PC 또는 NUC). 본사 송신 금지(#18·#22).
- AES-256 암호화 5컬럼: `partners.ceo_resident_no_encrypted`, `employees.resident_no_encrypted`, `employees.salary_encrypted`, `employees.salary_extra_encrypted`, `migration_errors.raw_data` (사실상 6번째).
- ORM: Dapper(주력). `MySqlConnector` 9.x. **`Task.WhenAll` + 단일 `MySqlConnection` 금지** (#16).
- INSERT ONLY 원장(#3): `stock_ledger`, `journal_lines`는 마이그 시에도 UPDATE/DELETE 금지. 멱등성은 PK가 아니라 `source_hash`로 보장.

---

## 2. 청크 크기 결정 알고리즘

### 2.1 테이블별 초기 청크 매트릭스 (MIGRATION_MASTER_PLAN §2.5 채택)

| 테이블 | 예상 행수 | 초기 청크 | 트랜잭션 | 비고 |
|---|---|---|---|---|
| DOCF8 (업체) | 50~500 | 단일 tx | 전체 1tx | 마스터, FK 루트 |
| DOCFS (상품) | 200~2,000 | 단일 tx | 전체 1tx | 마스터 |
| DOCSW (사원) | 10~50 | 단일 tx | 전체 1tx | AES 3컬럼 |
| COSTNO | 10~50 | 단일 tx | 전체 1tx | |
| DOCRT (BOM) | 10~100 | 단일 tx | 전체 1tx | |
| DOCF2 (헤더) | 1k~100k | **1,000** | 청크 tx | DOCF1과 헤더-상세 묶음 |
| DOCF1 (상세) | 3k~300k | **1,000(헤더 묶음)** | 청크 tx | DOCF2 묶어 commit |
| DOCFA (발주) | 500~50k | **1,000** | 청크 tx | |
| DOCFO (수주) | 500~50k | **1,000** | 청크 tx | |
| **DOCFB (입출고)** | **5k~500k** | **5,000** | 청크 tx | **최대 부하**, 동적 1k~10k |
| DOCF5 (수금) | 500~10k | 1,000 | 청크 tx | |
| DOCF6 (경비) | 500~10k | 1,000 | 청크 tx | |
| DOCF7 (전표) | 200~5k | 단일 tx | 전체 1tx | journal_lines INSERT ONLY |
| DOCF4 (세금계산서) | 500~30k | 1,000 | 청크 tx | 4품목 행분해 → 청크 내 4배 INSERT |
| DOCF9·DOCFQ (어음) | 100~5k | 단일 tx | 전체 1tx | |
| DOCCD·DOCCD1 (카드) | 100~5k | 500 | 청크 tx | 헤더-상세 묶음 |
| BANKF (은행) | 500~30k | 1,000 | 청크 tx | |
| CALENDAR | 365 | 단일 tx | 전체 1tx | |

> **단일 tx** = 행수 < 5,000인 마스터/소형 테이블. 청크 분할 비용 > 이득.

### 2.2 AIMD (Additive Increase, Multiplicative Decrease) 동적 조정

**입력 지표** (청크 commit 직후 측정):
- `commit_ms` — 청크 트랜잭션 commit 소요 ms
- `mem_delta_mb` — 청크 전후 GC 후 working set 증가
- `error_rate` — 청크 내 실패 행 / 청크 행수

**조정 규칙:**

```
목표 commit_ms 대역: [TARGET_LO, TARGET_HI] = [500, 1500] (단위 ms)
한계: MIN_CHUNK = 100, MAX_CHUNK = 10,000

if error_rate >= 0.05 OR commit_ms > 3000 OR mem_delta_mb > 250:
    chunk_size = max(MIN_CHUNK, chunk_size / 2)         # MD: 절반
    cooldown(1초)                                        # MariaDB 안정화
elif commit_ms < TARGET_LO AND error_rate < 0.001:
    chunk_size = min(MAX_CHUNK, chunk_size + 500)        # AI: +500
elif commit_ms > TARGET_HI:
    chunk_size = max(MIN_CHUNK, chunk_size - 250)        # 선형 감소
else:
    유지
```

**근거:**
- AIMD는 TCP 혼잡 제어에서 검증된 안정 수렴 알고리즘. 데이터 마이그 처리량 제어에 동일 원리 적용 가능.
- 절반 감소(MD)는 메모리·락 폭주 시 빠른 회복. 선형 증가(AI)는 안정 구간에서 과도한 진폭 방지.
- `chunk_size`는 매 청크 commit 후 `migration_checkpoints.chunk_size`에 UPSERT (감사·재개 동일 크기로 시작).

### 2.3 MariaDB 서버 한계 반영

| 파라미터 | 기본값 | W3 권장 | 청크 영향 |
|---|---|---|---|
| `max_allowed_packet` | 16 MB | **64 MB** | 청크 1만 + raw 1KB = 10 MB. 64MB 권장 |
| `bulk_insert_buffer_size` | 8 MB | **64 MB** | 멀티 VALUES INSERT 가속 |
| `innodb_buffer_pool_size` | 128 MB | **2 GB** (PC 8GB 기준) | 인덱스 캐시. 청크 LOOKUP FK 조회 가속 |
| `innodb_flush_log_at_trx_commit` | 1 | **2 (마이그 중만)** | 트랜잭션 fsync 완화 → 5~10배 가속. 마이그 종료 후 1로 복귀 |
| `innodb_log_file_size` | 96 MB | **512 MB** | 큰 청크 redo log 수용 |
| `net_read_timeout` | 30 | **180** | 큰 INSERT 타임아웃 방지 |

> ⚠️ `innodb_flush_log_at_trx_commit=2`는 마이그 도중 OS crash 시 마지막 1초 commit 손실 위험. 마이그는 멱등 재실행 가능하므로 허용. **마이그 종료 후 즉시 1로 복귀** (체크리스트 필수).

### 2.4 청크 SQL 패턴 (다중 VALUES INSERT)

```sql
-- Dapper Execute에 List<dynamic> 전달 시 ADO.NET이 N round-trip.
-- 대신 수동 멀티 VALUES INSERT 생성 (1 round-trip).
INSERT INTO partners (partner_id, tenant_id, name, ceo_resident_no_encrypted, ...)
VALUES
  (@p0_id, @p0_tenant, @p0_name, @p0_rrn, ...),
  (@p1_id, @p1_tenant, @p1_name, @p1_rrn, ...),
  ...
  (@p999_id, ...);
```

- 청크 1,000건 = 파라미터 ~50,000개. MariaDB 한계 65,535. **컬럼 64개 초과 테이블은 청크 500 강제**.
- 헌법 #16 준수: 단일 connection, 순차 청크.

---

## 3. 체크포인트 설계

### 3.1 데이터 모델 (INFRA_DDL_SPEC §2 채택)

```
migration_checkpoints
  ├ job_id          FK → migration_jobs
  ├ table_name      'DOCFB'
  ├ status          pending|running|done|failed|skipped
  ├ total_rows      MDB SELECT COUNT(*)
  ├ processed_count 누적 성공 행
  ├ last_pk_value   JSON — 5컬럼 복합 PK 저장
  ├ chunk_size      현재 청크 크기 (AIMD 결과)
  ├ avg_commit_ms   이동평균 (지수 가중, α=0.3)
  └ retry_count
```

### 3.2 last_pk_value JSON 포맷

```json
// DOCFB 5컬럼 복합 PK
{
  "IJ_DT":  "20251231",
  "IJ_IO":  "O",
  "IJ_SEQ": 99,
  "IJ_BUY": 1234,
  "IJ_SUN": 1
}

// DOCF2 단일 PK
{ "DOC_NO": "P-20251231-007" }
```

### 3.3 재개 흐름

```
1. 재개 API 호출: POST /api/migration/jobs/{jobId}/resume
2. migration_jobs.status = 'running' 업데이트 (단일 row UPDATE, 메타만)
3. 각 table_name별 checkpoint 로드:
   if status = 'done'   → 스킵
   if status = 'failed' → retry_count < 3이면 last_pk_value+1부터 재개
   if status = 'running' → 비정상 종료. last_pk_value+1부터 재개
4. OLEDB SELECT에 WHERE PK > last_pk_value ORDER BY PK 강제
5. 첫 청크는 저장된 chunk_size로 시작 (AIMD 상태 보존)
```

### 3.4 ORDER BY 보강 (MASTER PLAN §1에서 발견된 함정)

- 11개 테이블 OLEDB SELECT에 ORDER BY 누락. 청크 마이그 시 행 순서 비결정 → 재개 시 중복·누락 위험.
- W3 D1 작업: 23개 ReadMdbTable 전수 검사 후 ORDER BY 명시(복합 PK 전체).

### 3.5 멱등성 키 (재실행 안전)

```
source_hash = SHA256(tenant_id || mdb_file || table_name || pk_json)
              저장: 신규 컬럼 (옵션) 또는 application-level 중복 체크
```

- DOCFB 같은 거래/원장 테이블은 신 PK가 UUID이므로 단순 PK 충돌 검사 불가.
- application-level: 청크 적재 전 `source_hash IN (...)` 일괄 조회 → 이미 있으면 스킵.
- 헌법 #3 충돌 없음: 원장은 INSERT만, 중복 차단으로 무결성 보장.

---

## 4. 에러 처리

### 4.1 정책 트리

```
청크 commit 실패 (전체 ROLLBACK)
  ├ SQL 에러 유형 분석:
  │   ├ Deadlock (1213) / Lock timeout (1205)
  │   │   → 청크 size 절반 + 1초 대기 + 재시도 (최대 3회)
  │   ├ Duplicate (1062)
  │   │   → 행 단위 모드로 전환 (4.2 참조)
  │   ├ FK 위반 (1452)
  │   │   → 청크 전체 fail. migration_errors 적재 + 다음 청크 진행
  │   ├ Packet too large (1153)
  │   │   → 청크 size 1/4 + 재시도
  │   └ Connection lost (2013)
  │       → 5초 대기 + 재연결 + 동일 청크 재시도
  └ 3회 재시도 실패
      → checkpoint.status = 'failed', retry_count++
      → job 계속 진행 (다음 테이블)
      → 사장님 화면에 P0 경고
```

### 4.2 행 단위 폴백 모드

청크 fail이 duplicate/constraint류일 때:
1. 동일 청크를 1건씩 별도 트랜잭션으로 INSERT.
2. 실패 행 → `migration_errors` 적재 + 청크 계속.
3. 청크 종료 후 다시 청크 모드 복귀(`chunk_size` 유지).

### 4.3 migration_errors.raw_data 포맷 결정

| 옵션 | JSON | VARBINARY |
|---|---|---|
| 가독성 | ✅ 사장님 화면에 그대로 표시 가능 | ❌ 복호화 필요 |
| 검색 | ✅ JSON_EXTRACT로 컬럼 검색 | ❌ 불가 |
| 크기 | 중간 (텍스트 + UTF-8) | 작음 (바이너리) |
| 헌법 #5·#18 | ⚠️ JSON 평문이면 위반 | ✅ AES-256 |
| 헌법 #22 (최소주의) | ⚠️ | ✅ |
| 무결성 | 인코딩 깨짐 위험 | ✅ 원본 그대로 |

**결정안:** `JSON` 타입 + Value Converter로 **암호화된 JSON 문자열** 저장 (VALUE_CONVERTER_SPEC §1.2 일치).
- 사장님 화면 표시는 `migration_errors.error_message` (마스킹 평문) 사용.
- 개발자 추적 시에만 `raw_data` 복호화 → JSON 파싱.
- VARBINARY 단점(검색 불가) 회피 + 헌법 #5 준수.

```csharp
// 적재 예시
var raw = JsonSerializer.Serialize(mdbRowDict);            // 원본 JSON 문자열
var encrypted = _crypto.Encrypt(raw);                       // byte[]
// 컬럼은 JSON 타입이지만 실제 저장은 base64(encrypted) 문자열 (혹은 VARBINARY 변경)
```

> ⚠️ **권고:** 스키마 검토 시 `raw_data`를 `VARBINARY(8192)`로 변경하는 안도 고려(JSON 검색 불필요·암호화 자연). W3 D1 사장님 결재 필요.

### 4.4 에러 분류 코드

| code | type | severity | 행동 |
|---|---|---|---|
| E001 | encoding | warning | 한글 깨짐. CP949→UTF-8 폴백 시도 |
| E002 | fk_missing | error | 거래처/상품 매핑 실패. 사장님 추가 후 재실행 |
| E003 | duplicate | warning | source_hash 중복. 스킵 |
| E004 | schema | critical | 컬럼 누락. 작업 중단 |
| E005 | constraint | error | NOT NULL 위반 |
| E006 | timeout | error | 청크 size 자동 축소 |
| E007 | other | error | 미분류 |

---

## 5. 성능 목표·계산

### 5.1 100만 건 / 60분 검증

```
평균 처리량 = 1,000,000 / 3,600 = 278 rows/sec
DOCFB 청크 5,000 × commit 6초 = 833 rows/sec (피크)
DOCFB 50만 건 / 833 = 600초 = 10분
나머지 50만 건 / 평균 600 rows/sec = 833초 = 14분
오버헤드(체크포인트 UPDATE, FK lookup, AES) = 30%
총: (10 + 14) × 1.3 = 31분 → 60분 여유 49%
```

### 5.2 AES-256 오버헤드 추정

- 5컬럼 × 평균 50 bytes × ~10,000 employees + 100,000 partners + 0(원장) ≈ 50만 회 암호화.
- AES-256-CBC: 단일 코어 ~150 MB/s. 50만 회 × 64 bytes = 32 MB → **~0.2초** 총.
- raw_data 에러 적재 시: 청크 1,000 중 5% fail 가정 → 50회/청크 × 1KB = 50KB/청크. 무시 가능.

**결론:** AES 오버헤드 < 1% — 병목 아님.

### 5.3 병목 후보·대응

| 병목 | 발생 조건 | 대응 |
|---|---|---|
| FK lookup (partner_id, item_id) | DOCF1 청크당 1,000회 SELECT | 마이그 전 ID 매핑 딕셔너리 메모리 캐시 (현재 코드 이미 적용) |
| OLEDB 읽기 | MDB 인덱스 부재 | ORDER BY PK 강제 + Jet 인덱스 활용. 측정 후 청크 조정 |
| AES key 로드 | 매 컬럼마다 환경변수 호출 | DI singleton (현재 적용) |
| GC pressure | 청크 1만 string boxing | Span<char> 활용은 W3에서는 보류, 측정 후 W3 D5 검토 |

---

## 6. 트랜잭션 경계

### 6.1 결정: 청크 단위 트랜잭션

| 경계 | 장점 | 단점 | 채택 |
|---|---|---|---|
| 전체 1 tx | 원자성 완벽 | 100만 행 redo log 폭발, 락 폭주 | ❌ |
| **청크 tx** | redo log 관리 가능, 부분 진행 OK, 재개 자연 | 청크간 부분 일관성 (해결: source_hash 멱등) | ✅ |
| 행 tx | 디버깅 쉬움 | 100만 commit = 너무 느림 | ❌ (폴백용만) |
| Auto-commit | 가장 빠름 | 일관성 0 | ❌ |

### 6.2 청크 tx 본체

```csharp
foreach (var chunk in ReadMdbInChunks(table, chunkSize, lastPk))
{
    using var tx = _db.BeginTransaction();
    try
    {
        var sw = Stopwatch.StartNew();
        await BulkInsertAsync(chunk, tx, ct);
        await UpdateCheckpointAsync(jobId, table, chunk.LastPk, tx, ct);
        tx.Commit();
        sw.Stop();

        AdjustChunkSize(sw.ElapsedMilliseconds, chunk.MemoryDelta, chunk.ErrorRate);
    }
    catch (MySqlException ex) when (IsRetryable(ex))
    {
        tx.Rollback();
        _logger.LogWarning(ex, "Chunk failed (retryable), shrinking. table={Table}", table);
        chunkSize = Math.Max(MIN_CHUNK, chunkSize / 2);
        // 동일 lastPk로 재시도
    }
    catch (Exception ex)
    {
        tx.Rollback();
        _logger.LogError(ex, "Chunk failed (terminal). table={Table}", table);
        await RecordErrorAsync(jobId, table, chunk, ex, ct);
        // 다음 청크 진행
    }
}
```

### 6.3 INSERT ONLY 원장(#3) 충돌 검증

- `stock_ledger`, `journal_lines` 청크 INSERT만. UPDATE/DELETE 없음.
- 청크 fail 시 ROLLBACK은 INSERT 취소(미존재 → 미존재)일 뿐, UPDATE/DELETE 아님. ✅ 헌법 위반 없음.
- 단, `migration_checkpoints.last_pk_value`는 UPDATE — 메타 테이블이므로 #3 적용 범위 밖. (INFRA_DDL_SPEC §2 명시)

### 6.4 헌법 #16 (Task.WhenAll + MySqlConnection 금지)

- **순차 처리만**. 23개 테이블을 직렬로 순회.
- 병렬화하려면 **별도 MySqlConnection 인스턴스 + 별도 IDbConnection DI scope**가 필요 — W3에서는 도입 안 함 (복잡도·디버깅 비용 > 이득).
- 측정 결과 60분 목표 미달 시 W4에서 검토.

---

## 7. 모니터링 지표

### 7.1 청크 단위 기록 (in-memory 슬라이딩 윈도 + 주기 flush)

| 지표 | 단위 | 저장 위치 |
|---|---|---|
| `rows_per_sec` | EWMA α=0.3 | migration_jobs.checkpoint_data JSON |
| `commit_ms` | last 100 청크 평균 | migration_checkpoints.avg_commit_ms |
| `chunk_size_now` | 현재값 | migration_checkpoints.chunk_size |
| `error_rate` | last 1,000행 | migration_jobs.error_rows / processed_rows |
| `mem_rss_mb` | 청크 commit 직후 | 로그만 (DB 미저장, 헌법 #22) |
| `aes_ops_per_sec` | 디버그 모드만 | 로그만 |

### 7.2 사장님 진행률 API (W1 완료)

```
GET /api/migration/jobs/{jobId}/progress
→ {
    status: "running",
    overall_pct: 47.3,
    current_table: "DOCFB",
    rows_per_sec: 612,
    eta_sec: 1850,
    chunk_size_now: 5000,
    errors: { warning: 12, error: 2, critical: 0 }
  }
```

- Blazor 페이지에서 5초마다 polling.
- ETA = `(total_rows - processed_rows) / rows_per_sec`.

### 7.3 알람 임계

| 조건 | 행동 |
|---|---|
| `rows_per_sec` < 50 (10초 연속) | 사장님 화면 노란 경고 |
| `error_rate` > 5% (청크 단위) | 빨간 경고 + 일시정지 옵션 표시 |
| `chunk_size` == MIN_CHUNK 5회 연속 | "리소스 부족 가능성" 알람 |
| `critical` 에러 1건 | 즉시 일시정지 + 사장님 결정 대기 |

---

## 8. 리스크·완화

| # | 리스크 | 가능성 | 영향 | 완화 |
|---|---|---|---|---|
| R1 | OLEDB 32bit 충돌 (ACE 64bit 미설치) | 중 | 치명 | W3 D1 사장님 PC 사전 점검 (이미 W1 게이트) |
| R2 | DOCFB 500k 청크 5k 메모리 폭발 | 중 | 높 | AIMD 자동 축소 + max 메모리 250MB 가드 |
| R3 | innodb_flush=2 마이그 종료 후 복귀 누락 | 낮 | 높 | 체크리스트 + try/finally로 SET GLOBAL 복원 |
| R4 | source_hash 충돌 (해시 32바이트 → 사실상 0) | 매우 낮 | 중 | 무시 |
| R5 | 재개 시 last_pk_value 파싱 실패 | 낮 | 중 | JSON schema 검증 + 실패 시 D1부터 재실행(멱등) |
| R6 | AES key 미설정 → 마이그 중단 | 낮 | 치명 | W3 D1 시작 시 1회 self-test |
| R7 | Task.WhenAll 우회 시도 | 매우 낮 | 치명 | 코드리뷰 + Roslyn analyzer 추가 검토 |

---

## 9. W3 D1~D5 작업 분해 (작업지시서 초안)

```
[W3 D1] 5/27 — ORDER BY 보강 + AES self-test
  - 23개 ReadMdbTable ORDER BY 전수 추가
  - AES key self-test 메서드 (시작 시 1회)
  - migration_errors.raw_data 타입 결재 (JSON vs VARBINARY)
  - 산출물: PR + 단위 테스트

[W3 D2] 5/28 — 청크 매트릭스 + AIMD 코어
  - ChunkPolicy 클래스 (테이블별 초기 청크 매트릭스)
  - ChunkSizeAdjuster 클래스 (AIMD)
  - MIN/MAX/TARGET 상수 헌법 #19 (warnings 0)
  - 산출물: 단위 테스트 30케이스+

[W3 D3] 5/29 — 청크 실행 + 체크포인트
  - MdbReader.ReadInChunksAsync(table, lastPk, chunkSize)
  - MigrationCheckpointService.SaveAsync
  - 청크 트랜잭션 + INSERT ONLY 가드
  - 산출물: 10만건 시뮬 통과

[W3 D4] 5/30 — 에러 처리 + 멱등성
  - MySqlException 분류 (E001~E007)
  - 행 단위 폴백 모드
  - source_hash 중복 차단
  - migration_errors 적재 (AES JSON)
  - 산출물: 의도 fail 케이스 5종 모두 PASS

[W3 D5] 5/31 — 100만건 시뮬 + 게이트
  - PYOJUN.MDB 100만건 합성 → 1시간 측정
  - 중단 재개 시나리오 3종
  - 메모리 RSS 측정
  - 산출물: W3 게이트 합격 보고서
```

---

## 10. W3 권장안 1페이지 요약 ⭐

### 핵심 결정 (사장님 결재 대상)

1. **테이블별 초기 청크 매트릭스 채택** (MASTER PLAN §2.5)
   - 마스터(<5k): 단일 tx
   - 거래(1k~100k): 1,000
   - DOCFB(5k~500k): **5,000** (동적 1k~10k)

2. **AIMD 동적 조정**
   - 목표 commit_ms 대역 [500, 1500]
   - 위반 시 절반 감소, 안정 시 +500 증가
   - MIN_CHUNK=100, MAX_CHUNK=10,000

3. **청크 단위 트랜잭션**
   - 헌법 #3 INSERT ONLY 충돌 없음
   - 헌법 #16 순차 처리 (Task.WhenAll 금지 유지)
   - 재시도 가능 에러는 청크 축소 후 3회 재시도

4. **last_pk_value JSON 체크포인트**
   - 복합 PK 5컬럼까지 표현
   - 재개 시 `WHERE PK > last_pk_value ORDER BY PK`
   - chunk_size도 보존 (AIMD 상태 유지)

5. **migration_errors.raw_data**
   - 결재 #1: JSON + AES 암호화 (Value Converter) — 권장
   - 결재 #2 (대안): VARBINARY(8192) — 더 자연스러우나 검색 불가

6. **MariaDB 튜닝**
   - `innodb_flush_log_at_trx_commit=2` (마이그 한정, **복귀 의무**)
   - `max_allowed_packet=64MB`, `bulk_insert_buffer_size=64MB`

7. **성능 목표**
   - 100만 건 / 60분 (평균 280, 피크 800 rows/sec)
   - AES 오버헤드 < 1% (병목 아님)

### 작업지시서로 옮길 핵심 명세 bullet

- [ ] ChunkPolicy 클래스: 23개 테이블 초기 청크 매트릭스 상수 정의
- [ ] ChunkSizeAdjuster 클래스: AIMD 알고리즘 (단위 테스트 30+)
- [ ] MdbReader.ReadInChunksAsync(table, lastPk, chunkSize, ct) — ORDER BY 강제
- [ ] MigrationCheckpointService.SaveAsync / LoadAsync — JSON last_pk_value
- [ ] BulkInsertBuilder — 멀티 VALUES INSERT 생성기, 파라미터 65,535 가드
- [ ] MySqlExceptionClassifier — 1213/1205/1062/1452/1153/2013 분류
- [ ] RowLevelFallback — 청크 fail 시 1건씩 INSERT (max 1청크)
- [ ] SourceHashGenerator — SHA256(tenant + table + pk_json) 멱등 키
- [ ] migration_errors 적재기 — Value Converter 통한 raw_data AES 저장
- [ ] MariaDB 튜닝 SET GLOBAL 스크립트 + try/finally 복원 가드
- [ ] 진행률 API 응답 모델 확장 (rows_per_sec, chunk_size_now, eta_sec)
- [ ] 단위 테스트 케이스 60+ (청크 산정·재개·에러분류·멱등)
- [ ] 통합 테스트: 100만건 합성 MDB 1시간 내 완주
- [ ] 헌법 #16 Roslyn analyzer 검토 (Task.WhenAll + MySqlConnection 금지)
- [ ] errors 0 + warnings 0 (#19) — TreatWarningsAsErrors 빌드 통과
- [ ] 사장님 결재 2건: raw_data 포맷(JSON vs VARBINARY) + MariaDB 튜닝 일시 변경 승인

---

**참고 문서:**
- `docs/migration/MIGRATION_MASTER_PLAN.md` §2.5 (테이블별 청크 매트릭스)
- `docs/migration/INFRA_DDL_SPEC.md` §2 (migration_checkpoints DDL)
- `docs/migration/VALUE_CONVERTER_SPEC.md` §1.2 (raw_data AES)
- `docs/migration/ALTER_52_COLUMNS.md` (52컬럼 ALTER 명세)
- `src/HitPan.Application/Services/MdbMigrationService.cs` (W3 리팩토링 대상)

**최종 검증 예정:** 설계팀장 브라운킴 + CTO 래리 앨리슨 (W3 D1 착수 전)
