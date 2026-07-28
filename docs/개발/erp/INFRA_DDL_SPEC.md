# 마이그레이션 인프라 3개 테이블 DDL 설계서

> **작성:** 2026-05-12 W1 D3 / DB매니저 + 본부장
> **헌법:** #1(수정 OK), #2(tenant_id JWT), #5(암호화), #17(InnoDB), #18(본사 송신 0), #20(워크플로우 끊김 X)
> **상태:** 설계 완료, 사장님 결재 후 적용

⚠️ **본 문서는 설계서. 실제 DDL 실행은 사장님 결재 후 작업지시서 발행.**

---

## 1. migration_jobs (작업 추적 마스터)

```sql
CREATE TABLE IF NOT EXISTS migration_jobs (
    job_id            CHAR(36) PRIMARY KEY,
    tenant_id         CHAR(36) NOT NULL,
    initiated_by      CHAR(36) NOT NULL,                  -- 누가 시작 (user_id)
    source_folder     VARCHAR(500) NOT NULL,              -- C:\HITWINLAN10 등 (해시·암호화 X, 경로만)
    
    status            ENUM('pending','preview','running','paused','completed','failed','canceled') 
                      NOT NULL DEFAULT 'pending',
    
    -- 진행률 추적
    total_tables      SMALLINT UNSIGNED DEFAULT 0,
    completed_tables  SMALLINT UNSIGNED DEFAULT 0,
    total_rows        INT UNSIGNED DEFAULT 0,
    processed_rows    INT UNSIGNED DEFAULT 0,
    skipped_rows      INT UNSIGNED DEFAULT 0,
    error_rows        INT UNSIGNED DEFAULT 0,
    
    -- 시간
    preview_at        DATETIME NULL,
    started_at        DATETIME NULL,
    paused_at         DATETIME NULL,
    completed_at      DATETIME NULL,
    
    -- 결과
    error_summary     TEXT NULL,                          -- 종합 에러 메시지
    checkpoint_data   JSON NULL,                          -- 재시작 데이터
    
    -- 환경
    client_ip         VARCHAR(45) NULL,
    user_agent        VARCHAR(255) NULL,
    
    -- 감사
    created_at        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    
    INDEX idx_tenant_status (tenant_id, status),
    INDEX idx_tenant_created (tenant_id, created_at DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```

**용도:**
- 사장님이 [이관 시작] 누르면 row 1개 INSERT
- 진행률 실시간 업데이트 (processed_rows)
- 중단 시 status=paused + checkpoint_data 저장
- 재개 시 status=running + checkpoint_data 읽음

**헌법 부합:**
- ✅ #2 tenant_id JWT 클레임 적용
- ✅ #17 InnoDB
- ✅ #18 source_folder = 경로 문자열만 (파일 본문 X)
- ✅ #20 status 명시 = 워크플로우 추적

---

## 2. migration_checkpoints (테이블별 청크 추적)

```sql
CREATE TABLE IF NOT EXISTS migration_checkpoints (
    checkpoint_id     CHAR(36) PRIMARY KEY,
    job_id            CHAR(36) NOT NULL,
    tenant_id         CHAR(36) NOT NULL,                  -- 헌법 #2 - 모든 테이블 tenant_id
    
    -- 대상 테이블
    mdb_file          VARCHAR(50) NOT NULL,               -- 'PYOJUN.MDB' 'PANDATA.mdb' 'POTHER.mdb'
    table_name        VARCHAR(50) NOT NULL,               -- 'DOCF8' 'DOCFS' 등
    table_order       SMALLINT UNSIGNED NOT NULL,         -- 마이그 순서 (의존성)
    
    -- 진행
    status            ENUM('pending','running','done','failed','skipped') 
                      NOT NULL DEFAULT 'pending',
    total_rows        INT UNSIGNED DEFAULT 0,
    processed_count   INT UNSIGNED DEFAULT 0,
    
    -- 체크포인트 (재시작용)
    last_pk_value     JSON NULL,                          -- 5컬럼 복합 PK도 JSON으로 저장
    chunk_size        SMALLINT UNSIGNED DEFAULT 1000,     -- 동적 조정 가능
    
    -- 시간 + 통계
    started_at        DATETIME NULL,
    completed_at      DATETIME NULL,
    avg_commit_ms     INT UNSIGNED DEFAULT 0,             -- 평균 commit 시간 (동적 조정용)
    
    -- 에러
    last_error        TEXT NULL,
    retry_count       TINYINT UNSIGNED DEFAULT 0,
    
    -- 감사
    created_at        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    
    UNIQUE KEY uk_job_table (job_id, table_name),
    INDEX idx_tenant (tenant_id),
    INDEX idx_status (status, table_order),
    CONSTRAINT fk_checkpoint_job FOREIGN KEY (job_id) 
        REFERENCES migration_jobs(job_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```

**핵심 컬럼 설명:**

| 컬럼 | 용도 |
|---|---|
| `last_pk_value` JSON | DOCFB 5컬럼 복합 PK 저장 가능: `{"IJ_DT":"20251231","IJ_IO":"O","IJ_SEQ":99,"IJ_BUY":1234,"IJ_SUN":1}` |
| `chunk_size` | 시작 1,000 → avg_commit_ms < 500이면 청크 키움 → > 1500이면 줄임 |
| `table_order` | 1=COSTNO, 2=SETUP, 3=DOCF8(업체), 4=DOCFS(상품)... 마이그 순서 (FK 의존성) |
| `avg_commit_ms` | 동적 조정 알고리즘 입력 |

**헌법 부합:**
- ✅ #2 tenant_id 컬럼 명시
- ✅ #3 INSERT ONLY 아님 (단 UPDATE는 status·last_pk_value만, 데이터 본문 X)
- ✅ #17 InnoDB + FK

---

## 3. migration_errors (실패 레코드 보관 + 사용자 추적)

```sql
CREATE TABLE IF NOT EXISTS migration_errors (
    error_id          CHAR(36) PRIMARY KEY,
    job_id            CHAR(36) NOT NULL,
    tenant_id         CHAR(36) NOT NULL,                  -- 헌법 #2
    checkpoint_id     CHAR(36) NULL,                      -- 어느 청크에서 발생
    
    -- 발생 위치
    mdb_file          VARCHAR(50) NOT NULL,
    table_name        VARCHAR(50) NOT NULL,
    row_pk_value      JSON NULL,                          -- 실패 레코드 PK (사장님 추적용)
    row_offset        INT UNSIGNED NULL,                  -- 파일 내 행 번호
    
    -- 에러 분류
    error_type        ENUM('encoding','fk_missing','duplicate','schema','constraint','timeout','other') 
                      NOT NULL,
    error_severity    ENUM('warning','error','critical') NOT NULL DEFAULT 'error',
    error_code        VARCHAR(20) NULL,                   -- 'E001' 'E002' 등 분류 코드
    
    -- 메시지
    error_message     TEXT NOT NULL,                      -- 사용자 표시용 (마스킹된)
    error_detail      TEXT NULL,                          -- 개발자용 상세
    raw_data          JSON NULL,                          -- ⚠️ 원본 데이터 — AES-256 암호화 필수
    
    -- 처리 상태
    is_resolved       TINYINT UNSIGNED DEFAULT 0,
    resolved_at       DATETIME NULL,
    resolved_by       CHAR(36) NULL,
    resolution_note   TEXT NULL,
    
    -- 감사
    occurred_at       DATETIME NOT NULL,
    created_at        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    INDEX idx_job (job_id, error_severity, occurred_at),
    INDEX idx_tenant (tenant_id),
    INDEX idx_resolved (is_resolved, occurred_at),
    CONSTRAINT fk_error_job FOREIGN KEY (job_id) 
        REFERENCES migration_jobs(job_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```

**용도 시나리오:**

```
시나리오 1: 한글 깨짐
  error_type = 'encoding'
  error_message = "한글 변환 실패 — '○○○' 컬럼"
  raw_data = AES-256 암호화된 원본
  → 사장님 화면에 표시 + 개발자 추적

시나리오 2: 거래처 FK 누락
  error_type = 'fk_missing'
  error_message = "거래처 코드 1234 매핑 실패"
  → 사장님이 거래처 추가 후 [재실행] 가능

시나리오 3: 중복 PK
  error_type = 'duplicate'
  error_message = "전표번호 X001 이미 존재"
  → 사장님 결정 (덮어쓰기 / 스킵 / 신규)
```

**헌법 부합:**
- ✅ #2 tenant_id
- ✅ #5 raw_data AES-256 암호화 (Value Converter)
- ✅ #15 빈 catch 금지 → 모든 에러 본 테이블에 기록
- ✅ #17 InnoDB
- ✅ #18 raw_data = 고객사 자체 데이터 (본사 송신 X, 로컬 보관)
- ✅ #20 워크플로우 끊김 추적 가능

---

## 4. 인덱스 전략

```sql
-- 진행률 조회 빠르게
CREATE INDEX idx_jobs_status ON migration_jobs(tenant_id, status);
CREATE INDEX idx_jobs_recent ON migration_jobs(tenant_id, created_at DESC);

-- 체크포인트 조회 (MariaDB 11.4.10 부분 인덱스 미지원 → 일반 인덱스로 변경, W1 게이트 2026-05-12)
CREATE INDEX idx_chkpt_pending ON migration_checkpoints(job_id, status, table_order);

-- 에러 화면 빠르게
CREATE INDEX idx_errors_severity ON migration_errors(job_id, error_severity, occurred_at DESC);
```

---

## 5. ENGINE·COLLATION·CHARSET 헌법 점검

| 항목 | 값 | 헌법 |
|---|---|---|
| ENGINE | InnoDB | ✅ #17 |
| CHARSET | utf8mb4 | ✅ 4/22 통일 |
| COLLATION | utf8mb4_unicode_ci | ✅ 5/5 통일 |
| FK 활성화 | ON DELETE CASCADE | ✅ |

---

## 6. 마이그 후 데이터 보관 정책 (헌법 #22)

```
[보관 기간]
  migration_jobs        영구 (감사 이력)
  migration_checkpoints 1년 (재시작 가능성)
  migration_errors      1년 (사고 추적)

[자동 삭제]
  daily batch:
    DELETE FROM migration_checkpoints
      WHERE status='done' AND completed_at < NOW() - INTERVAL 1 YEAR;
    DELETE FROM migration_errors
      WHERE is_resolved=1 AND resolved_at < NOW() - INTERVAL 1 YEAR;
```

---

## 7. 사장님 결재 사항

| # | 사항 | 결재 |
|---|---|---|
| 1 | 3개 테이블 DDL 적용 | ⚠️ 작업지시서 발행 후 적용 |
| 2 | raw_data AES-256 암호화 | ✅ 헌법 #5 |
| 3 | 보관 기간 (jobs 영구, 나머지 1년) | ⚠️ |
| 4 | FK ON DELETE CASCADE | ✅ |

---

**작성:** DB매니저 + 본부장 춘식
**검토:** 보안매니저 (#5·#18) + 백엔드매니저 (#15) + 설계팀장
**최종 검증:** CTO 래리 앨리슨
**적용 시점:** W1 D5 게이트 통과 후
