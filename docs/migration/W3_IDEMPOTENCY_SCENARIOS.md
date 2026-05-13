# W3 마이그레이션 멱등성 검증 시나리오

> 문서번호: W3-IDEMPOTENCY-2026-05-13
> 작성: PM(닥터스트레인지) + DB매니저 + 백엔드매니저 + 보안매니저
> 위치: `docs/migration/W3_IDEMPOTENCY_SCENARIOS.md`
> 상태: W3 진입 직전 설계 확정본 (구현은 작18에서)

---

## 0. 문서 목적과 범위

마이그레이션 작업은 본질적으로 **중단·재개·재실행이 빈번**한 위험 영역이다.

- 고객사 PC에서 EXE로 실행 — 정전·블루스크린·네트워크 끊김이 흔하다
- MDB 파일은 수십만 건 — 1회 전체 실행에 30분 ~ 2시간 소요
- 1회 실패하면 처음부터 재실행이 디폴트
- W2까지 만들어진 인프라(`migration_jobs`, `migration_checkpoints`, `migration_errors`)는 재개 가능 설계의 뼈대

**핵심 원칙: 100회 재실행해도 결과가 동일해야 한다.**

본 문서는 W3 진입(5/15) 시점에 검증할 **멱등성 시나리오 12종**과 자동화 권장안, 그리고 작지서 초안(`작18_멱등성_100회`)을 정의한다.

### 헌법 매핑

| 헌법 # | 적용 지점 |
|---|---|
| #3 | INSERT ONLY 원장 — 재처리 시 `stock_ledger`/`journal_lines` UPDATE/DELETE 절대 금지 |
| #5 | 암호화 필수 컬럼 — VARBINARY 5종 (주민·계좌·전화·이메일·주소) AES-256 |
| #13 | 새 SQL 작성 전 `DESCRIBE` 의무 — 재실행 쿼리도 동일 |
| #16 | MySqlConnection 병렬 금지 — 재시도 루프에서 Task.WhenAll 사용 금지 |
| #18 | 본사로 업무 데이터 전송 금지 — 멱등성 테스트도 고객 로컬에서만 수행 |
| #19 | warnings 0 — 재실행 시 경고도 0 유지 |

### W2 완료 시점 인프라

```sql
-- 작업 단위
migration_jobs (job_id PK, tenant_id, source_mdb_hash, status, started_at, ended_at)

-- 체크포인트 (테이블별 마지막 처리 PK)
migration_checkpoints (job_id, table_name, last_processed_id, processed_count, updated_at)

-- 오류 로그
migration_errors (job_id, table_name, source_pk, error_code, error_message, occurred_at)
```

### 대상 테이블 (W2 D2 ALTER 완료분 + W3 신규)

- `partners` (+19 컬럼, VARBINARY 5종 포함)
- `items` (+5 컬럼)
- `employees` (+31 컬럼, 레거시 잔액 10컬럼 포함)
- W3 신규: 거래 데이터(`stock_ledger` 일부, `journal_lines` 일부, 매입·판매 히스토리)

---

## 1. 시나리오 12종

각 시나리오는 **입력 / 실행 / 기대 결과** 3블록 구조로 작성한다.

---

### 시나리오 #1 — 완전 재실행 (Full Re-run)

**의도**: 동일 MDB 파일을 100회 실행했을 때 행 수와 데이터 해시가 동일해야 한다.

#### 입력
- `source.mdb` (PYOJUN.MDB, partners 3건 / items 0건 / employees 0건 — W2 D5 스모크 베이스)
- 빈 `hitpan_erp` 스키마 (`migration_*` 인프라만 존재)
- `job_id = 'JOB-IDEM-001'`

#### 실행
1. `MdbMigrationService.RunAsync(jobId, sourcePath)` 1회 실행 → 정상 종료 확인
2. 동일 `jobId`로 99회 추가 실행 (또는 `jobId`만 변경하고 `source_mdb_hash` 동일)
3. 매 실행 후 검증 SQL 수집

#### 기대 결과
- `partners` 행 수: 항상 3 (재실행으로 늘어나지 않음)
- 컬럼 해시 동일 (암호화 컬럼 제외 — 시나리오 #5 참조)
- `migration_jobs.status = 'completed'` 유지

#### 검증 SQL
```sql
-- A. 행 수 고정
SELECT COUNT(*) AS row_cnt FROM partners WHERE tenant_id = @tid;
-- 기대: 3 (몇 번을 돌려도)

-- B. 비암호화 컬럼 해시 집계
SELECT SHA2(GROUP_CONCAT(legacy_id, '|', partner_name ORDER BY legacy_id SEPARATOR '#'), 256) AS digest
FROM partners WHERE tenant_id = @tid;
-- 기대: 100회 실행 모두 동일 digest
```

---

### 시나리오 #2 — 부분 재실행 (Resume from Checkpoint)

**의도**: 50% 처리 후 중단 → 재개 시 51%부터 시작, 최종 결과는 1회 실행과 동일.

#### 입력
- `source.mdb` (partners 200건으로 확장한 합성 MDB)
- 청크 크기 50, 4청크 예상
- 2번째 청크(101~150) 처리 후 강제 종료 신호(`CancellationToken`)

#### 실행
1. Run 시작 → 청크#1(1~50) 완료 → `migration_checkpoints.last_processed_id = 50`
2. 청크#2(51~100) 완료 → `last_processed_id = 100`
3. 청크#3 진입 직전 프로세스 KILL (또는 `OperationCanceledException`)
4. 동일 `job_id`로 재시작
5. 재시작 시 `WHERE legacy_id > 100`부터 SELECT — 청크#3, #4 처리

#### 기대 결과
- 총 처리 건수 = 200 (중복 없음)
- 청크#1, #2 두 번 처리되지 않음
- `migration_jobs.status` 흐름: `running → interrupted → running → completed`

#### 검증 SQL
```sql
-- A. 최종 행 수 = 1회 실행 시와 동일
SELECT COUNT(*) FROM partners WHERE tenant_id = @tid;  -- 기대: 200

-- B. 체크포인트 단조 증가 확인 (감사 로그)
SELECT table_name, last_processed_id, updated_at
FROM migration_checkpoints WHERE job_id = @jid ORDER BY updated_at;
-- last_processed_id가 단조 증가 (50 → 100 → 150 → 200)
```

---

### 시나리오 #3 — 트랜잭션 롤백 (Mid-chunk Failure)

**의도**: 청크 50% 지점에서 예외 발생 시 청크 시작 시점으로 롤백, 다음 실행 때 해당 청크 재처리.

#### 입력
- `source.mdb` (partners 100건)
- 청크 크기 50
- 청크#1의 25번째 row에서 `THROW new InvalidOperationException("fault-injection")` 의도적 주입

#### 실행
1. 청크#1 트랜잭션 시작
2. row 1~24 `INSERT` 성공 (트랜잭션 내부, 미커밋)
3. row 25에서 예외 → `tx.Rollback()`
4. `migration_errors` 로그 기록
5. `migration_checkpoints.last_processed_id`는 청크#1 진입 전 값(0) 유지
6. 운영자 fault-injection 제거 후 재실행

#### 기대 결과
- 첫 실행 후 `partners` 행 수: 0 (전체 롤백)
- `migration_errors` 1건 (`error_code = 'CHUNK_FAILED'`)
- `migration_checkpoints.last_processed_id = 0` 유지
- 재실행 시 청크#1부터 정상 처리 → 최종 100건

#### 검증 SQL
```sql
-- A. 롤백 직후 행 수 0
SELECT COUNT(*) FROM partners WHERE tenant_id = @tid;  -- 기대: 0

-- B. 체크포인트 미전진
SELECT last_processed_id FROM migration_checkpoints
WHERE job_id = @jid AND table_name = 'partners';  -- 기대: 0

-- C. 오류 로그 기록
SELECT COUNT(*) FROM migration_errors
WHERE job_id = @jid AND error_code = 'CHUNK_FAILED';  -- 기대: 1
```

---

### 시나리오 #4 — 중복 INSERT 방지 (UNIQUE 위반 정책)

**의도**: `partners.legacy_id`에 UNIQUE 제약. 동일 `legacy_id` 재INSERT 시 정책 결정 — **SKIP** (UPSERT 금지, 헌법 #1 덮어쓰기 절대 금지).

#### 입력
- `partners` 테이블에 `UNIQUE KEY uq_partners_legacy (tenant_id, legacy_id)`
- 이미 `legacy_id = 'P-001'` 1건 적재 상태
- 동일 `legacy_id`로 마이그 재실행

#### 실행
1. `INSERT IGNORE INTO partners (...) VALUES (...)` 사용
2. 또는 `INSERT ... ON DUPLICATE KEY UPDATE updated_at = updated_at` (no-op)
3. 매 row마다 `ROW_COUNT()` 확인하여 SKIP/INSERTED 분류

#### 기대 결과
- 행 수 불변 (3건 유지)
- 기존 데이터 컬럼 값 변경 없음 (덮어쓰기 금지)
- `migration_errors`에 `'DUPLICATE_SKIPPED'` 정보 로그 (오류는 아님, 정상 흐름)

#### 검증 SQL
```sql
-- A. INSERT IGNORE 후 동일성
SELECT legacy_id, partner_name, updated_at FROM partners
WHERE tenant_id = @tid AND legacy_id = 'P-001';
-- 기대: 재실행 전후 updated_at 동일

-- B. SKIP 카운트
SELECT COUNT(*) FROM migration_errors
WHERE job_id = @jid AND error_code = 'DUPLICATE_SKIPPED';
-- 기대: 재실행 시 = 기존 행 수
```

**정책 결정**: SKIP 채택. UPDATE는 운영자가 별도 "재매핑 모드"로만 허용 (W4 이후).

---

### 시나리오 #5 — AES 암호화 멱등성 (IV Randomization)

**의도**: AES-256-CBC는 IV가 랜덤 → 동일 평문 100회 암호화 시 ciphertext는 매번 다름. 그러나 **복호화 결과는 동일**해야 한다.

#### 입력
- 평문 주민번호 `'900101-1234567'`
- `IBinaryCryptoService.Encrypt(plain)` 100회 호출

#### 실행
1. 100회 암호화 → 100개의 서로 다른 ciphertext 수집
2. 각각 복호화 → 평문 비교
3. INSERT 시점 시나리오 #1(완전 재실행)과 결합: 동일 MDB 100회 실행하면 매번 새 ciphertext 저장

#### 기대 결과
- 100개의 ciphertext는 서로 다름 (IV 랜덤 검증)
- 100개의 평문 복호화 결과는 모두 `'900101-1234567'`
- **마이그 재실행 시 시나리오 #4(SKIP)와 결합되어 ciphertext가 매번 새로 쓰이지 않음** — DB에 저장된 ciphertext는 최초 1회 INSERT 시점 값으로 고정

#### 검증 SQL
```sql
-- A. ciphertext는 다르지만 동일 평문으로 복호화되는지 (애플리케이션 측 단위테스트)
-- DB측: SKIP 정책 검증 — 재실행 후 동일 row의 BINARY 컬럼이 변하지 않음
SELECT legacy_id, HEX(rrn_enc) AS rrn_hex, HEX(account_enc) AS acct_hex
FROM partners WHERE tenant_id = @tid AND legacy_id = 'P-001';
-- 기대: 재실행 전후 hex 값 동일 (SKIP되었으므로)

-- B. 암호화 컬럼은 SHA256 집계 동일성 검증 불가 → 복호화 후 검증
-- (자동화 테스트에서 IBinaryCryptoService.Decrypt 호출 후 평문 비교)
```

**주의**: 시나리오 #1의 "컬럼 해시 동일" 검증은 비암호화 컬럼만 대상. 암호화 컬럼은 별도 복호화 후 평문 해시로 검증.

---

### 시나리오 #6 — FK CASCADE 안전성 (Parent Re-process)

**의도**: 부모 row(`partners`) 재처리 시 자식 row(`partner_special_prices`, `partner_ledger` 등)에 CASCADE가 의도치 않게 발동하지 않아야 한다.

#### 입력
- `partners` 1건 + 자식 `partner_special_prices` 5건
- FK: `partner_special_prices.partner_id REFERENCES partners(id) ON DELETE CASCADE`
- 재마이그 실행

#### 실행
1. 시나리오 #4(SKIP)에 따라 부모는 재INSERT되지 않음 → CASCADE 미발동
2. 만약 운영자가 강제 `DELETE FROM partners WHERE legacy_id='P-001'` 후 재마이그 → 자식 5건 CASCADE 삭제됨
3. **운영 정책: 부모 강제 삭제 금지. 재매핑 모드는 W4 이후 별도 설계.**

#### 기대 결과
- 정상 재실행 시 자식 데이터 영향 없음
- CASCADE 발동 자체가 비정상 상황의 신호 (감사 로그 필수)

#### 검증 SQL
```sql
-- A. 부모-자식 행 수 보존
SELECT
  (SELECT COUNT(*) FROM partners WHERE tenant_id=@tid) AS parent_cnt,
  (SELECT COUNT(*) FROM partner_special_prices WHERE tenant_id=@tid) AS child_cnt;
-- 기대: 재실행 전후 두 값 동일

-- B. CASCADE 발동 감지 (information_schema가 아닌 애플리케이션 카운터)
SELECT COUNT(*) FROM migration_errors
WHERE job_id=@jid AND error_code='UNEXPECTED_CASCADE';
-- 기대: 0
```

---

### 시나리오 #7 — Checkpoint 유실 시 복구

**의도**: `migration_checkpoints` 행이 사고로 삭제됐을 때, 재실행 시 처음부터 시작해도 UNIQUE/DUP 처리로 안전해야 한다.

#### 입력
- 정상 완료된 `job_id` 1건 (partners 200건 적재됨)
- 운영자 실수로 `DELETE FROM migration_checkpoints WHERE job_id=@jid`
- 동일 MDB로 재실행

#### 실행
1. 체크포인트 없음 → `last_processed_id = 0` 가정
2. 청크#1부터 SELECT → 200건 전체 재INSERT 시도
3. 시나리오 #4 SKIP 정책 발동 → 200건 모두 DUPLICATE_SKIPPED
4. 체크포인트 재구성 → `last_processed_id = 200`

#### 기대 결과
- `partners` 행 수 200 유지
- `migration_errors`에 `DUPLICATE_SKIPPED` 200건 로그 (정상)
- 체크포인트 복원

#### 검증 SQL
```sql
-- A. 체크포인트 삭제 후 재실행 결과
SELECT COUNT(*) FROM partners WHERE tenant_id=@tid;  -- 기대: 200
SELECT COUNT(*) FROM migration_errors
WHERE job_id=@jid AND error_code='DUPLICATE_SKIPPED';  -- 기대: 200
SELECT last_processed_id FROM migration_checkpoints
WHERE job_id=@jid AND table_name='partners';  -- 기대: 200
```

---

### 시나리오 #8 — 동시 실행 차단 (Concurrent Lock)

**의도**: 동일 `tenant_id` + 동일 `job_id`로 2개 프로세스가 동시에 실행되면 두 번째는 즉시 실패.

#### 입력
- 프로세스 A: `MdbMigrationService.RunAsync(jobId='JOB-A', tenant=T1)`
- 프로세스 B: 동일 인자로 1초 후 실행

#### 실행
1. A가 `migration_jobs`에 `INSERT ... status='running'` (UNIQUE on `(tenant_id, job_id, status='running')` 부분 인덱스)
2. B가 동일 INSERT 시도 → UNIQUE 위반 → 즉시 예외
3. 또는 `SELECT ... FOR UPDATE NOWAIT`로 lock 획득 실패 처리

#### 기대 결과
- A는 정상 진행
- B는 `'CONCURRENT_RUN_REJECTED'` 오류로 즉시 종료 (1초 이내)
- 데이터 일관성 깨짐 없음

#### 검증 SQL
```sql
-- A. 동시 running 상태는 최대 1건
SELECT COUNT(*) FROM migration_jobs
WHERE tenant_id=@tid AND status='running';
-- 기대: 0 또는 1, 절대 2 이상 없음

-- B. B의 실패 로그
SELECT error_code FROM migration_errors
WHERE job_id='JOB-A' ORDER BY occurred_at DESC LIMIT 1;
-- 기대: 'CONCURRENT_RUN_REJECTED'
```

**구현 권장**: `migration_jobs`에 `UNIQUE INDEX uq_running (tenant_id, job_id)` + `status` 컬럼에 ENUM. 종료 시 `status='completed'`로 전이.

---

### 시나리오 #9 — VARBINARY NULL 처리 (Placeholder 금지)

**의도**: 원본 MDB의 주민번호 컬럼이 NULL이면 암호화 결과도 NULL. **빈 문자열을 암호화한 placeholder 저장 금지** (저장공간 낭비 + 복호화 시 빈 문자열 반환되어 마스킹 로직 혼란).

#### 입력
- MDB의 `partners.RRN` 컬럼: 3건 중 1건 NULL, 1건 빈문자, 1건 정상 평문
- `IBinaryCryptoService.Encrypt(null)` 호출 시 동작 정의

#### 실행
1. NULL → `Encrypt` 호출 자체를 스킵 → DB에 NULL 저장
2. 빈문자 `""` → NULL로 정규화 후 저장 (정책: 빈문자 = 미입력)
3. 정상 평문 → 정상 암호화

#### 기대 결과
- DB의 `rrn_enc` 컬럼: NULL 2건, 정상 BINARY 1건
- `EncryptedBinaryValueConverter`는 입력 NULL → 출력 NULL을 보장

#### 검증 SQL
```sql
-- A. NULL 보존 카운트
SELECT
  SUM(CASE WHEN rrn_enc IS NULL THEN 1 ELSE 0 END) AS null_cnt,
  SUM(CASE WHEN rrn_enc IS NOT NULL THEN 1 ELSE 0 END) AS enc_cnt
FROM partners WHERE tenant_id=@tid;
-- 기대: null_cnt=2, enc_cnt=1

-- B. 재실행 시 NULL 보존 (placeholder로 변하지 않음)
-- (시나리오 #1과 결합하여 100회 재실행 후도 동일)
```

---

### 시나리오 #10 — 숫자 0 vs NULL 구분 (Legacy Balance)

**의도**: `employees` 레거시 잔액 10컬럼(`legacy_balance_1`~`legacy_balance_10`)에서 **0(영원)** 과 **NULL(미입력)** 은 의미가 다르다. 마이그 시 구분 보존 필수.

#### 입력
- MDB의 `급여1` 컬럼: 0 / NULL / 100000 3가지 값
- Access의 quirk: 숫자 컬럼은 NULL 대신 0이 자주 들어옴

#### 실행
1. 원본 MDB SELECT 시 `IIF(IsNull([급여1]), NULL, [급여1])` 명시
2. C# 측 `decimal?` 사용, `DBNull.Value` 명확히 처리
3. INSERT 시 파라미터 `null` vs `0m` 구분

#### 기대 결과
- DB의 `legacy_balance_1`: NULL 1건, 0 1건, 100000 1건 (3개 구분 보존)
- 100회 재실행 후도 동일

#### 검증 SQL
```sql
-- A. NULL/0/양수 분포
SELECT
  SUM(CASE WHEN legacy_balance_1 IS NULL THEN 1 ELSE 0 END) AS null_cnt,
  SUM(CASE WHEN legacy_balance_1 = 0 THEN 1 ELSE 0 END) AS zero_cnt,
  SUM(CASE WHEN legacy_balance_1 > 0 THEN 1 ELSE 0 END) AS pos_cnt
FROM employees WHERE tenant_id=@tid;
-- 기대: 1 / 1 / 1
```

**ERP매니저 확인 포인트**: 0과 NULL 구분이 잔액 마감, 평균 계산, 세무 신고에 영향. 현장 직원이 "잔액 0원"과 "미입력"을 구분하지 못하면 안 됨.

---

### 시나리오 #11 — 날짜 1899-12-30 처리 (Access Epoch)

**의도**: Microsoft Access의 날짜 기본값은 `1899-12-30` (OLE Automation date 0). 이 값은 의미 없는 placeholder → NULL로 매핑해야 한다.

#### 입력
- MDB의 `입사일`, `퇴사일`, `생년월일` 컬럼
- 일부 row가 `1899-12-30 00:00:00` 값을 가짐 (Access GUI에서 비워둔 흔적)
- 일부는 정상 날짜, 일부는 진짜 NULL

#### 실행
1. SELECT 시 매핑 규칙:
   ```csharp
   DateTime? Normalize(DateTime? src) =>
       (src == null || src.Value <= new DateTime(1900, 1, 1)) ? null : src;
   ```
2. `1899-12-30` → NULL, 정상 날짜 → 그대로, NULL → NULL
3. 일관성: 모든 날짜 컬럼에 동일 헬퍼 적용

#### 기대 결과
- DB의 `hire_date`, `resign_date`, `birth_date` 컬럼: `1899-12-30` 값 0건
- NULL 또는 1900-01-01 이후 날짜만 존재

#### 검증 SQL
```sql
-- A. Access epoch 누출 검증
SELECT COUNT(*) FROM employees
WHERE tenant_id=@tid
  AND (hire_date <= '1900-01-01'
    OR resign_date <= '1900-01-01'
    OR birth_date <= '1900-01-01');
-- 기대: 0

-- B. 100회 재실행 후도 동일
```

---

### 시나리오 #12 — Collation 충돌 (한자/이모지)

**의도**: MDB는 CP949 또는 Latin1 quirk가 잦음. DB는 `utf8mb4_unicode_ci` 통일. 한자(漢字)·이모지(🔥)·특수문자(₩) 입력 시 100회 재실행해도 동일 비교 결과.

#### 입력
- MDB의 `partner_name` 컬럼:
  - `'(주)히트판'`
  - `'株式会社ABC'` (한자)
  - `'테스트🔥'` (이모지)
  - `'단가₩100'` (특수문자)

#### 실행
1. 원본 인코딩 명시 (ODBC: `CHARSET=CP949` 또는 `UTF-8`)
2. C# 측 `string` → UTF-8 → MariaDB `utf8mb4` 파라미터
3. 100회 재실행

#### 기대 결과
- 4건 모두 무손실 저장
- 재실행 시 SKIP (시나리오 #4) — 비교 키(`legacy_id` + `partner_name`)가 collation 일치
- LIKE 검색, ORDER BY 결과 일관

#### 검증 SQL
```sql
-- A. 무손실 저장 확인
SELECT legacy_id, partner_name, HEX(partner_name) AS name_hex
FROM partners WHERE tenant_id=@tid ORDER BY legacy_id;
-- 기대: 4건 모두 정확한 UTF-8 hex (예: '🔥' = 0xF09F94A5)

-- B. 정렬 일관성
SELECT partner_name FROM partners
WHERE tenant_id=@tid ORDER BY partner_name COLLATE utf8mb4_unicode_ci;
-- 기대: 100회 재실행 후도 동일 순서

-- C. collation 검증
SELECT TABLE_NAME, COLUMN_NAME, COLLATION_NAME
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME IN ('partners','items','employees')
  AND DATA_TYPE IN ('varchar','text','char');
-- 기대: 모든 행 COLLATION_NAME='utf8mb4_unicode_ci'
```

---

## 2. 시나리오 매트릭스 요약

| # | 시나리오 | 핵심 검증 | 우선순위 | 자동화 가능 |
|---|---|---|---|---|
| 1 | 완전 재실행 | 행 수·해시 동일 | P0 | Yes |
| 2 | 부분 재실행 | 체크포인트 단조 증가 | P0 | Yes |
| 3 | 트랜잭션 롤백 | fault-injection 후 무흔적 | P0 | Yes |
| 4 | UNIQUE SKIP | INSERT IGNORE 동작 | P0 | Yes |
| 5 | AES 멱등성 | 복호화 동일성 | P0 | Yes (단위) |
| 6 | FK CASCADE 안전 | 자식 행 보존 | P1 | Yes |
| 7 | Checkpoint 유실 | DUP SKIP으로 자동 복구 | P1 | Yes |
| 8 | 동시 실행 차단 | UNIQUE on running | P0 | Yes (멀티 프로세스) |
| 9 | VARBINARY NULL | placeholder 금지 | P0 | Yes |
| 10 | 숫자 0 vs NULL | 의미 보존 | P1 | Yes |
| 11 | Access 1899-12-30 | NULL 매핑 | P1 | Yes |
| 12 | Collation 한자/이모지 | utf8mb4 무손실 | P1 | Yes |

**P0 7건은 베타 출시 절대 게이트.** P1 5건은 W3 종료 전 통과 권장.

---

## 3. 자동화 권장안

### 3.1 옵션 A — xUnit + Testcontainers (1순위 권장)

**구성**:
- `HitPan.Migration.IdempotencyTests` (신규 테스트 프로젝트)
- `Testcontainers.MariaDb` 1회용 컨테이너 — 매 테스트 격리
- `Microsoft.Data.Sqlite` 또는 합성 MDB 픽스처
- `[Theory] [InlineData(100)]` 로 100회 반복

**장점**:
- CI/CD 통합 용이 (GitHub Actions matrix)
- 격리 보장 — 테스트 간 오염 0
- 헌법 #19(warnings 0) 자동 검증 가능

**단점**:
- 컨테이너 기동 5~10초 (100회 × 12시나리오 = 20분 이내 목표)
- Windows EXE 환경과 100% 동일하지는 않음 (Docker on Linux)

**예시 골격**:
```csharp
[Theory]
[InlineData(100)]
public async Task Scenario01_FullRerun_RowCountInvariant(int iterations)
{
    await using var maria = new MariaDbBuilder().Build();
    await maria.StartAsync();
    await using var svc = BuildMigrationService(maria.GetConnectionString());

    for (int i = 0; i < iterations; i++)
        await svc.RunAsync("JOB-IDEM-001", _fixtureMdb);

    var cnt = await CountRows(maria, "partners");
    Assert.Equal(3, cnt);
}
```

### 3.2 옵션 B — 일회용 콘솔 (`HitPan.Tools.IdempotencyRunner`)

**구성**:
- 단일 콘솔 EXE — 12 시나리오 순차 실행
- 로컬 MariaDB 인스턴스 사용 (개발자 PC)
- 결과 JSON 리포트 + 콘솔 컬러 출력

**장점**:
- 현장 환경(Windows EXE) 검증 가능
- 디버깅 용이 (`--scenario 3 --verbose`)
- 베타 체험단 PC에서도 1회 돌려볼 수 있음

**단점**:
- CI 통합 약함
- 격리 위해 매번 스키마 `DROP/CREATE` 필요 → 시간 소요

### 3.3 최종 권장

**병행 채택**:
- **옵션 A**: CI 게이트 (PR merge 전 필수, P0 7건 100회 반복)
- **옵션 B**: 베타 체험단 사전 PC 검증 (수동 1회)

---

## 4. 위험 요소 & 운영 가이드

### 4.1 알려진 함정

1. **`INSERT IGNORE`의 silent 데이터 손실** — UNIQUE가 아닌 다른 오류(타입 mismatch 등)도 무시될 수 있음. → `INSERT ... ON DUPLICATE KEY UPDATE updated_at=updated_at` 권장 + `ROW_COUNT()` 확인.
2. **트랜잭션 크기 폭주** — 청크 50건이 적정. 1000건 트랜잭션은 롤백 비용 폭발.
3. **MySqlConnection 병렬 금지(헌법 #16)** — 재시도 루프에서 `Task.WhenAll` 사용 시 즉시 반려.
4. **AES IV는 매번 새로 생성** — 동일 IV 재사용은 보안 사고. 시나리오 #5 검증 필수.
5. **`migration_jobs.status` ENUM 누락** — `'running'`, `'completed'`, `'failed'`, `'interrupted'`, `'rolled_back'` 5종 명시.

### 4.2 운영 룰

- 재마이그 실행 전 백업 의무 (`mysqldump` 또는 LVM 스냅샷)
- `DELETE FROM partners`로 초기화하는 운영자 작업 금지 — `DROP/CREATE` 또는 별도 tenant로 격리
- 멱등성 깨지면 P0 핫픽스 (헌법 #20 워크플로우 무결성)

---

## 5. W3 진입 작지서 초안 — 작18_멱등성_100회

> 발행 예정: 2026-05-15 (W3 D1 오전)
> 발행자: PM(닥터스트레인지) + AI수석 공동

### 5.1 목표
W3 마이그레이션 거래 데이터 적재 작업에 멱등성 100회 자동화 테스트 도입. P0 7건 시나리오 통과를 베타 출시 절대 게이트로 확정.

### 5.2 산출물
1. `tests/HitPan.Migration.IdempotencyTests/` 프로젝트 생성
   - `Scenario01_FullRerunTests.cs` ~ `Scenario12_CollationTests.cs` 12개 클래스
   - `Fixtures/IdempotencyFixture.cs` (Testcontainers MariaDB)
   - `Helpers/HashCollector.cs` (행 수·SHA256 집계)
2. `tools/IdempotencyRunner/` 콘솔 EXE
   - `--scenario {1..12|all}`, `--iterations 100`, `--report json` 옵션
3. `docs/migration/W3_IDEMPOTENCY_RESULT.md` (실행 리포트)

### 5.3 통과 기준
- P0 시나리오 7건 (#1, #2, #3, #4, #5, #8, #9): 100회 반복 100% PASS
- P1 시나리오 5건 (#6, #7, #10, #11, #12): 10회 반복 100% PASS
- 전체 실행 시간 30분 이내
- 빌드 errors 0 + warnings 0 (헌법 #19)

### 5.4 리뷰 어벤져스 5인
- DB매니저: 시나리오 #1, #2, #4, #7
- 보안매니저: 시나리오 #5, #8, #9
- 백엔드매니저: 시나리오 #3, #6, 트랜잭션 경계
- ERP매니저: 시나리오 #10, #11 현장 의미 검증
- AI수석: 시나리오 #12 + 전체 자동화 구조 리뷰

### 5.5 일정
| 일자 | 작업 |
|---|---|
| 5/15 (W3 D1) | 작18 발행 + 프로젝트 골격 생성 |
| 5/16 (W3 D2) | 시나리오 #1~#4 구현·통과 |
| 5/17 (W3 D3) | 시나리오 #5, #8, #9 구현·통과 (P0 완료) |
| 5/18 (W3 D4) | 시나리오 #6, #7, #10, #11, #12 구현·통과 |
| 5/19 (W3 D5) | 100회 풀런 + 리포트 + 사장님 결재 |

### 5.6 결재 포인트
- 시나리오 #4 UNIQUE 정책 = SKIP 채택 (UPDATE 금지) — 사장님 확인 요청
- 시나리오 #6 부모 강제 삭제 금지 운영 정책 — 사장님 확인 요청
- 자동화 옵션 A+B 병행 — 본부장 결재

---

## 6. 결론

마이그레이션은 "한 번에 성공"이 아니라 **"100번 돌려도 같은 결과"** 가 정답이다.

W2 D5 스모크에서 PYOJUN.MDB 3건이 1회 통과한 것은 시작일 뿐, 본 12 시나리오를 통과하지 못한 상태로 베타 체험단에 EXE를 배포하면 정전 1회, 재실행 1회만에 데이터가 두 배가 되거나(SKIP 미적용), 통째로 사라지거나(롤백 미적용) 한다.

**헌법 #20(워크플로우 무결성)** 의 마이그레이션판 = 멱등성. W3 진입 시 작18로 즉시 구현 착수, 5/19까지 100회 자동화 풀런 통과를 베타 절대 게이트로 확정한다.

— 끝 —
