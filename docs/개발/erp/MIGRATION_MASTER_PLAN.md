# 히트판 ERP 마이그레이션 마스터플랜 — 하브루타판

> **확정:** 2026-05-12 사장님 일괄 결재 8건
> **방식:** 하브루타 60분 토론 (6인) → 사장님 식사 후 결재
> **참석:** CTO 래리 앨리슨, 설계팀장 브라운킴, 본부장 춘식, DB매니저, 보안매니저, 백엔드매니저
> **외부:** PM 닥터스트레인지, ERP매니저

⚠️ **1차 회의록(안전답 버전) 폐기.** 본 하브루타판이 단일 진실 원천.

---

## 0. 한 줄 결론

> **기존 1,755줄 코드 70% 유지 + 30% 신규 작성 + 카카오 인프라 4종 + 테이블별 청크 차등 = 6~8주 (5/13~6/23~7/7).**

---

## 1. 하브루타 토론 결과 — 1차 회의록 폐기 사항

| 영역 | 1차 회의록 (안전답) | 하브루타 폐기 + 보강 |
|---|---|---|
| 청크 크기 | 1,000건 일률 | **테이블별 차등 + 동적 조정** (8개 테이블 매트릭스) |
| 일정 단언 | 6주 확정 | **6~8주 범위 + Week 게이트 6개** |
| 단가 매핑 | 단순 등급 부여 | **옵션 B(컬럼 확인) vs 옵션 D(자동 추론)** |
| ORDER BY | 누락 인지 안 함 | **11개 테이블 ORDER BY 누락 발견 → 보강** |
| last_pk_value | 미정 | **체크포인트 재시작 패턴 채택** |
| 멱등성 키 | 단순 source_id | **SHA256(tenant + table + source_id) 복합 키** |

**하브루타 효과:**
- 1차 단언 폐기 3건
- 신규 함정 발견 4건
- 신규 옵션 발명 1건 (옵션 D 단가 자동 추론)
- 보안매니저 평가: "2개월 사고 예방"

---

## 2. 마이그레이션 아키텍처 (결재 #1, #2)

### 2.1 코드 활용 비율

```
[유지 = 70%]
  ✅ OleDb 연결 관리
  ✅ 23개 테이블 ReadMdbTable
  ✅ FK 매핑 딕셔너리
  ✅ 트랜잭션 패턴
  ✅ 인코딩 처리 (GetStr·GetDec)
  ✅ 9개 메서드 70~95점

[보강 = 20%]
  🟡 MigrateItemsAsync 추가 컬럼
  🟡 MigratePartnersAsync 추가 컬럼
  🟡 MigrateEmployeesAsync 주소·생년
  🟡 MigrateStockLedgerAsync 다창고·멱등
  🟡 11개 테이블 ORDER BY 추가

[신규 = 10%]
  🔴 MigratePartnerSpecialPricesAsync
  🔴 MigrateItemSpecialPricesAsync
  🔴 MigratePurchaseReturnsAsync
  🔴 MigrateSalesReturnsAsync
  🔴 MigrateQuotationsAsync
  🔴 MigrateSalesDeliveriesAsync
  🔴 MigrateCalendarAsync
  🔴 MigrateNoticeAsync
```

### 2.2 5개 클래스 분리 (헌법 #1 추출만)

```
MdbMigrationOrchestrator      200줄 — 총괄·job 관리
MdbReader                      300줄 — OLEDB 청크 읽기
MdbToHitpanMapper              800줄 — 23개 매핑 (기존 추출)
MigrationCheckpointService    150줄 — 진행률 추적
MigrationErrorCollector       100줄 — 실패 수집
```

### 2.3 신규 인프라 3개 테이블

```sql
-- 인프라 #1: migration_jobs
CREATE TABLE migration_jobs (
    job_id            CHAR(36) PRIMARY KEY,
    tenant_id         CHAR(36) NOT NULL,
    status            ENUM('pending','running','paused','completed','failed') NOT NULL,
    total_tables      INT DEFAULT 0,
    total_rows        INT DEFAULT 0,
    processed_rows    INT DEFAULT 0,
    started_at        DATETIME,
    completed_at      DATETIME,
    error_message     TEXT,
    checkpoint_data   JSON,
    created_by        CHAR(36),
    INDEX idx_tenant_status (tenant_id, status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 인프라 #2: migration_checkpoints
CREATE TABLE migration_checkpoints (
    checkpoint_id     CHAR(36) PRIMARY KEY,
    job_id            CHAR(36) NOT NULL,
    table_name        VARCHAR(50) NOT NULL,
    processed_count   INT DEFAULT 0,
    last_pk_value     VARCHAR(255),
    status            ENUM('pending','done','failed') NOT NULL,
    processed_at      DATETIME,
    INDEX idx_job_table (job_id, table_name),
    FOREIGN KEY (job_id) REFERENCES migration_jobs(job_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 인프라 #3: migration_errors
CREATE TABLE migration_errors (
    error_id          CHAR(36) PRIMARY KEY,
    job_id            CHAR(36) NOT NULL,
    table_name        VARCHAR(50) NOT NULL,
    row_pk            VARCHAR(255),
    error_type        ENUM('encoding','fk_missing','duplicate','schema','other') NOT NULL,
    error_message     TEXT NOT NULL,
    raw_data          JSON,
    occurred_at       DATETIME NOT NULL,
    INDEX idx_job (job_id),
    FOREIGN KEY (job_id) REFERENCES migration_jobs(job_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```

### 2.4 신규 인프라 4개 API

```
GET    /api/migration/jobs/{jobId}/progress
GET    /api/migration/jobs/{jobId}/errors
POST   /api/migration/jobs/{jobId}/resume
POST   /api/migration/jobs/{jobId}/cancel
```

### 2.5 테이블별 청크 매트릭스 (결재 #2)

```
DOCF8  (50~500건)         단일 tx (청크 없음)
DOCFS  (200~2,000건)       단일 tx
DOCRT  (10~100건)         단일 tx
DOCSW  (10~50건)          단일 tx
COSTNO (10~50건)          단일 tx (신규)
DOCF2  (1,000~100,000건)  청크 1,000 (헤더-DOCF1 묶음)
DOCF1  (3,000~300,000건)  청크 1,000 (DOCF2와 묶음)
DOCFA  (500~50,000건)     청크 1,000
DOCFO  (500~50,000건)     청크 1,000
DOCFB  (5,000~500,000건)  청크 5,000~10,000 (가장 큼)
DOCF5  (500~10,000건)     청크 1,000
DOCF6  (500~10,000건)     청크 1,000
DOCF7  (200~5,000건)      단일 tx
DOCF4  (500~30,000건)     청크 1,000 (4품목 행분해)
DOCF9·DOCFQ (100~5,000건) 단일 tx
DOCCD·DOCCD1 (100~5,000건) 청크 500 (헤더-상세 묶음)
BANKF  (500~30,000건)     청크 1,000
CALENDAR (365건)          단일 tx
```

**동적 조정:** 첫 100,000건에서 평균 commit < 1초 → 청크 키움, > 1초 → 줄임.

---

## 3. 6~8주 일정 + Week 게이트 (결재 #4)

### 3.1 Week 게이트 6개 객관 기준

| Week | 게이트 | 합격 기준 (객관) |
|---|---|---|
| W1 | 인프라 통과 | 3개 테이블 DDL 적용 + 4개 API 200 응답 + 5개 클래스 분리 완료 + 헌법 위반 0 |
| W2 | P0 4건 통과 | 특별단가·반품·견적·deliveries 변환 코드 + 단위 테스트 100% |
| W3 | 청크·멱등 통과 | 100,000건 시뮬 commit 평균 < 1초 + 멱등 재실행 중복 0 |
| W4 | 양식·신규 메뉴 | 양식 30종 + 이미지 25개 + 명함·AS·배송·메모 메뉴 동작 |
| W5 | 사장님 검증 | 사장님 직접 [미리보기] + [이관] 실행 + EVF ⑥ 3년치 시뮬 통과 |
| W6 | 외부 침투 통과 | 그레이해커 OWASP Top 10 + 멱등 침투 0건 |

**2개 이상 게이트 fail = 7주 자동 발동 / 3개 이상 fail = 8주.**

### 3.2 인력 분배

```
[풀가동 5주]
  - DB매니저 (전담)
  - 백엔드매니저 + 시니어 1명
  - ERP매니저 (도메인 자문)
  - 본부장 (총괄·카카오 노하우)
  - 검증팀 (24h SLA)

[병행 전 기간]
  - 보안매니저 (감사·멱등)
  - 설계팀장 (5개 클래스 분리 검토)
  - CTO (48h SLA)
  - 프론트매니저 + 디자이너 (W4)
```

### 3.3 6주 상세 일정

```
[W1: 5/13~5/19] 인프라
  D1 ⭐ 빈 MDB 실측 (결재 #5) — 사장님 직접 또는 본부장 PowerShell
  D2 PK·ORDER BY·DOCF8 단가등급 컬럼 확인
  D3-4 3개 테이블 DDL + 4개 API
  D5 5개 클래스 분리

[W2: 5/20~5/26] P0 4건
  D1-2 특별단가 마이그 (옵션 B 또는 D 결정 후)
  D3 반품 마이그
  D4 견적 마이그
  D5 판매→deliveries 변환

[W3: 5/27~6/2] 청크·멱등
  D1-2 누락 컬럼 보강 partners·items·employees
  D3-4 다창고·멱등성 + 청크 적용
  D5 BackgroundService 전환

[W4: 6/3~6/9] 양식·이미지·신규 메뉴 (결재 #6)
  D1-2 양식 이미지 30종 + 상품 사진 25개
  D3 CALENDAR 마이그
  D4 명함·AS·배송·메모 메뉴 4개
  D5 통합

[W5: 6/10~6/16] 검증
  D1-2 PowerShell 스키마 실측
  D3-4 사장님 직접 실행
  D5 EVF ⑥ 3년치 시뮬

[W6: 6/17~6/23] 외부 (결재 #7)
  D1-3 그레이해커 침투
  D4 핫픽스
  D5 CTO 종합 판정

[6/30] 마이그 완성
[7/1~14] 전자세금계산서 국세청 연동 (병렬)
[7/15] ⭐ 베타 출시
```

---

## 4. 단가 매핑 옵션 (결재 #3)

```
빈 MDB 실측 (W1 D2):
  ├ DOCF8에 단가등급 컬럼 있음 → 옵션 B 채택
  └ DOCF8에 단가등급 컬럼 없음 → 옵션 D 채택

옵션 B: 컬럼 그대로 매핑
  partners.price_grade = DOCF8.단가등급
  손실: 0

옵션 D: 자동 추론 (W5 배치)
  거래처별 평균 판매 단가 분석
  → 단가 A~E 그룹 자동 부여
  → 거래 이력 100건+ = 자동
  → 100건 미만 = 사장님 수동 (소수)
```

---

## 5. 헌법 준수 점검

| 헌법 | 적용 |
|---|---|
| #1 수정 OK 덮어쓰기 X | ✅ 기존 70% 유지, 5개 클래스 추출 |
| #2 tenant_id JWT만 | ✅ TenantMiddleware |
| #3 INSERT ONLY 원장 | ✅ stock_ledger |
| #4 금액 decimal | ✅ GetDec |
| #5 암호화 컬럼 | ✅ Value Converter |
| #15 빈 catch 금지 | ✅ 5/5 봉합 |
| #16 MySqlConn+Task.WhenAll | ✅ 순차 |
| #17 InnoDB 명시 | ✅ 3 신규 테이블 |
| #18 본사 송신 금지 | ✅ 로컬 처리 |
| #19 errors 0 + warnings 0 | ✅ SupportedOSPlatform |
| #20 워크플로우 끊김 금지 | ✅ 청크 + 멱등 + 재개 |
| #22 본사 데이터 최소주의 | ✅ 메타만 |
| #23 AI 5중 검증 | ✅ |
| #24 책임 분산 + 가르침 | ✅ 검증 UI 추가 |
| #25 쉽게·정확하게·안전하게 | ✅ |

---

## 6. 리스크 5건 (하브루타 발견)

| # | 리스크 | 대응 |
|---|---|---|
| 1 | ACE OLEDB 32bit 의존 | 인스톨러 자동 설치 + 안내 |
| 2 | 단일 tx → 청크 전환 회귀 | 5개 클래스 분리 + 단위 테스트 100% |
| 3 | 멱등 키 충돌 | SHA256 복합 키 |
| 4 | 사용자 검증 UI 미완 | W4에 검증 화면 추가 |
| 5 | CP949 인코딩 변환 실패 | migration_errors raw_data 보관 |

---

## 7. 사장님 결재 8건 — 확정

1. ✅ 기존 70% 유지 + 30% 신규 (새로 짜기 아님)
2. ✅ 테이블별 청크 차등 + 동적 조정
3. ✅ 단가 매핑 — W1 실측 후 옵션 B 또는 D
4. ✅ 6~8주 + Week 게이트 6개
5. ✅ 빈 MDB 즉시 실측 (W1 D1)
6. ✅ 양식 30종 + 이미지 25개 마이그 (W4)
7. ✅ 외부 그레이해커 침투 6/17~19
8. ✅ 헌법 #20·#22·#23 재확인

---

## 8. 미래 세션 인수인계 (Single Source of Truth)

이 문서가 마이그레이션 단일 진실 원천. 미래 모든 작업지시서·리뷰는 본 문서 인용. 1차 회의록(안전답)은 폐기됨.

**다음 단계 (W1 D1):**
- 빈 MDB 실측 방법 결정 (C → A → B 단계별)
- PowerShell 실행 또는 사장님 직접 미리보기
- PK·ORDER BY·단가등급 컬럼 3건 확인 후 W1 D2~D5 작업지시서 발행

---

**서명:**
- 의장: CTO 래리 앨리슨
- 서기: 본부장 춘식
- 결재: 사장님 (2026-05-12)
- 참석: 설계팀장 브라운킴, DB매니저, 보안매니저, 백엔드매니저
- 외부: PM 닥터스트레인지, ERP매니저
