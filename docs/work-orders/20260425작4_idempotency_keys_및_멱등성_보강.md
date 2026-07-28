# 작업지시서 20260425작4 — `idempotency_keys` 신설 + `monthly_summary` 멱등성 보강

## 0. 메타

| 항목 | 값 |
|---|---|
| **문서번호** | 20260425작4 |
| **발행일** | 2026-04-25 |
| **발행자** | PM 닥터스트레인지 |
| **A 책임자** | DB 매니저 |
| **결재 트랙** | **풀** |
| **민감 영역** | DB 스키마 (신규 테이블 + 기존 테이블 컬럼 추가) / 원장 (간접 — 멱등성으로 분개·summary 보호) |
| **Contract-First 대상** | ✅ 멱등키 미들웨어 + 표준 응답 DTO에 적용 |
| **EVF 영향 영역** | ④ 혼돈 (저장 연타·재시도) / ② 장애 (네트워크 재시도) |
| **예상 소요** | 0.5d (DDL 0.1 + 미들웨어 0.2 + 검증 0.2) |
| **Sprint** | Sprint 1 (4/25~5/2) — P0-4 |
| **선행 의존성** | 없음 (다른 P0의 선행 인프라) |
| **후행 의존성** | P0-2 작2(계산서 발행) / P0-1 작3(원클릭 설치 prov 서버) 모두 본 작업의 산출물 사용 |

## 1. 배경 (Why)

### 1.1 사장님 ⭐⭐⭐⭐⭐ #4 — `monthly_summary` 멱등성

4/24 핸드오프 §83: **"monthly_summary 멱등성"** — 남은 5건 중 4번째.

### 1.2 코드 검증으로 확인된 위험 (4/25 PM 직접 확인)

`src/HitPan.Application/Services/SalesService.cs:354~369`:
```sql
INSERT INTO monthly_summary (...) VALUES (..., @Sales, ...)
ON DUPLICATE KEY UPDATE total_sales = total_sales + @Sales
```
→ **같은 delivery가 두 번 확정되면 매출이 두 번 더해진다.**
→ 같은 패턴이 `PurchaseService:280, 688`, `ApprovalTriggerHelper:27`, `SyncEventPublisher:110, 159, 224, 248`에 동일하게 존재 (총 8곳).

### 1.3 EVF ④ 혼돈 영역 베타 출시 절대 게이트

DESIGN_PRINCIPLES §12.3: "저장 연타 100회 → 원장에 1건만 (멱등성 작동) ✅" — **현재 미통과.**

### 1.4 작2·작3 공통 인프라

- 작2(계산서 발행): `Idempotency-Key` 헤더로 같은 발행 1회만 보장
- 작3(원클릭 설치): prov 서버 토큰 발급도 멱등 처리 필요
- 본 작4가 **공통 인프라**를 제공해야 두 작업이 쉽게 가져다 씀.

### 1.5 CTO 발견 사항 (스키마 검증)

> *"PM이 4/24 핸드오프 표현을 그대로 받아쓰면 안 된다. 핸드오프엔 'monthly_summary.source_id UNIQUE'라고 적혀있는데, **`source_id` 컬럼 자체가 없다.** 컬럼 추가가 선행되어야 UNIQUE 인덱스도 생긴다. 스키마 검증의 본보기다."*

## 2. 목표 산출물 (What)

### 2.1 DB — DB-18 마이그레이션

#### A. `idempotency_keys` 신설 테이블

```sql
CREATE TABLE IF NOT EXISTS idempotency_keys (
  id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  tenant_id       VARCHAR(36)     NOT NULL,
  idempotency_key VARCHAR(64)     NOT NULL,
  endpoint        VARCHAR(128)    NOT NULL COMMENT 'METHOD + path, e.g. POST /api/sales/tax-invoices',
  request_hash    CHAR(64)        NOT NULL COMMENT 'SHA-256 of request body, validates same key+same body',
  status_code     INT             NOT NULL COMMENT 'cached HTTP status',
  response_body   MEDIUMTEXT      NOT NULL COMMENT 'cached response JSON',
  created_at      DATETIME(6)     NOT NULL DEFAULT NOW(6),
  expires_at      DATETIME(6)     NOT NULL COMMENT 'TTL 24h (DESIGN_PRINCIPLES §5.3)',
  PRIMARY KEY (id),
  UNIQUE KEY uk_tenant_key (tenant_id, idempotency_key),
  KEY idx_expires (expires_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Idempotency-Key 헤더 멱등 처리 (DESIGN_PRINCIPLES §5.3)';
```

#### B. `monthly_summary` 멱등성 보강 — `source` 추적 테이블 신설

> ⚠️ CTO 결정: `monthly_summary`에 `source_id` 컬럼 추가하는 방식은 **불가능**. 한 row에 여러 source 누적되는 구조라서. 대신 **별도 추적 테이블**로 멱등 보장.

```sql
CREATE TABLE IF NOT EXISTS monthly_summary_sources (
  id            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  tenant_id     VARCHAR(36)     NOT NULL,
  `year_month`  CHAR(6)         NOT NULL,
  source_type   VARCHAR(32)     NOT NULL COMMENT 'sales_delivery_confirm | purchase_receipt_confirm | etc',
  source_id     VARCHAR(64)     NOT NULL COMMENT 'delivery_id / receipt_id / etc',
  applied_at    DATETIME(6)     NOT NULL DEFAULT NOW(6),
  amount_delta  DECIMAL(15,2)   NOT NULL COMMENT '실제 가산된 금액 (감사 추적)',
  field_name    VARCHAR(32)     NOT NULL COMMENT 'total_sales | total_purchase | ...',
  PRIMARY KEY (id),
  UNIQUE KEY uk_source (tenant_id, source_type, source_id, field_name),
  KEY idx_tenant_month (tenant_id, `year_month`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='monthly_summary 멱등 추적 (같은 source 두 번 가산 차단)';
```

**사용 패턴 (8곳 코드 수정에 적용)**:
```sql
-- 1) source 가산 시도 (UNIQUE 위반이면 이미 가산됨 → 무시)
INSERT IGNORE INTO monthly_summary_sources
  (tenant_id, year_month, source_type, source_id, amount_delta, field_name)
VALUES (@TenantId, @Ym, @SrcType, @SrcId, @Amount, @Field);

-- 2) ROW_COUNT() = 1 일 때만 monthly_summary 가산 (= 신규 source)
-- ROW_COUNT() = 0 이면 이미 처리된 source → 가산 스킵
```

#### C. 인덱스 (운영용)

```sql
-- 만료된 idempotency 키 일괄 정리용
-- (이미 idx_expires로 커버)
```

#### D. 마이그레이션 파일

`src/HitPan.API/Migrations/SQL/DB-18_idempotency_and_summary_sources.sql`
- 위 A + B + 롤백 스크립트 동봉
- §절대원칙 #17: ENGINE=InnoDB ✅
- §절대원칙 #13: 기존 `monthly_summary` DESCRIBE 결과 첨부

### 2.2 백엔드 — Idempotency 미들웨어 (Contract-First 적용)

#### A. `HitPan.Contracts/Idempotency/`

```
HitPan.Contracts/
└── Idempotency/
    ├── IdempotencyKeyAttribute.cs      // 컨트롤러/액션 데코레이터
    ├── IdempotencyResult.cs            // record (status, body, fromCache)
    └── IdempotencyConstants.cs         // 헤더명 "Idempotency-Key", TTL 24h
```

#### B. 미들웨어 동작

```
1. 요청 수신 → Idempotency-Key 헤더 추출
2. 헤더 없으면 정상 처리 (옵트인이지만 [IdempotencyKey] 속성 있는 액션은 필수)
3. 헤더 있으면:
   a. tenant_id + key로 idempotency_keys 조회
   b. 있고 request_hash 일치 → 캐시된 응답 그대로 반환 (status + body)
   c. 있고 hash 불일치 → 409 Conflict (같은 키 다른 본문)
   d. 없으면 → 정상 처리 후 응답을 캐싱 (TTL 24h)
4. 만료 정리: 백그라운드 Hosted Service 1h 주기로 expires_at < NOW() DELETE
```

#### C. 파일 배치

- `src/HitPan.API/Middleware/IdempotencyMiddleware.cs` (신규)
- `src/HitPan.API/HostedServices/IdempotencyCleanupService.cs` (신규)
- `src/HitPan.API/Program.cs` (DI + Middleware 등록)
- `src/HitPan.Contracts/Idempotency/*.cs` (신규 프로젝트는 작2·작3과 공유 — 같이 셋업)

### 2.3 기존 8곳 코드 수정 — `monthly_summary` 멱등 가드

**파일별 수정 포인트:**
- `SalesService.cs:354` — sales_delivery_confirm
- `PurchaseService.cs:280` — purchase_receipt_confirm
- `PurchaseService.cs:688` — purchase_return_confirm (감산 시에도 멱등)
- `ApprovalTriggerHelper.cs:27`
- `SyncEventPublisher.cs:110, 159, 224, 248` (4곳, 각 source_type 명시)

각 8곳에서 동일 패턴 적용:
1. `INSERT IGNORE monthly_summary_sources` 먼저
2. `ROW_COUNT() = 1` 확인 후 기존 `monthly_summary INSERT...ON DUPLICATE KEY` 실행
3. 모두 같은 트랜잭션(`dbTx`) 안에서

### 2.4 테스트

- 단위: `IdempotencyMiddleware` — same key+body 캐시 / same key+different body 409 / no key 통과
- 통합:
  - `POST /api/sales/deliveries/{id}/confirm` 같은 키로 100회 → DB 1번만 가산
  - 같은 키로 다른 본문 → 409
  - 키 없이 호출 100회 → 100번 가산 (옵트인 동작 확인)
- EVF ④ 혼돈: 저장 연타 100회 시나리오 데이비드 박 사인

## 3. 비범위 (What Not)

- **EF+Dapper UoW 통합** — 별도 작지서(P0-3 브라운킴)
- **`tax_invoices` 테이블** — 작2 작업
- **prov 서버 토큰 멱등** — 작3에서 본 작4의 미들웨어를 사용만 함 (구현은 작3)
- **기존 8곳 외 추가 발견되는 멱등 누락** — 본 작업 후 데이비드 박 검증에서 추가 발견 시 별도 작지서

## 4. RACI

| 역할 | 담당자 |
|---|---|
| **R** (실행) | DB 개발팀(DDL) + 백엔드 개발팀(미들웨어) |
| **A** (책임) | **DB 매니저** |
| **C** (협의) | 백엔드 매니저 / 마커스 리(Contract 프로젝트 셋업) / 브라운킴(UoW 영향) / 보안 매니저(키 충돌 시나리오) |
| **V** (검증) | DV-S(키 보안: 충돌 시 정보 누출 없음) / DV-D(8곳 멱등 동작 확인 + EVF ④) / BK(트랜잭션 정합성) |
| **F** (결재) | CTO → 사장님 |

## 5. 결재 라인

**풀 트랙** 7단계.

## 6. 구현 가이드

### 6.1 DB
- DB-18 마이그레이션 적용 전 `mariadb -u hitpan -p hitpan_erp -e "DESCRIBE monthly_summary;"` 실행 결과 캡처
- 롤백 스크립트는 `DROP TABLE` 2개 + 컬럼 추가 없음 (안전)
- ANALYZE TABLE은 추후 (현 시점 데이터 적음)

### 6.2 백엔드
- §절대원칙 #16: `MySqlConnection + Task.WhenAll` 금지 — 미들웨어는 단일 쿼리로 충분
- §절대원칙 #15: `catch` 블록 silent swallow 금지 — 캐시 미스 시 `_logger.LogInformation` 1줄
- 멱등키 충돌(409)은 사용자 메시지: "동일 요청이 다른 내용으로 재시도되었습니다. 새로 시도해주세요."

### 6.3 기존 코드 수정
- 8곳 모두 같은 패턴 — **PM이 1곳 수정 후 Pull Request로 어벤져스 패턴 승인 받고 나머지 7곳 일괄 적용** (4프로토콜 #4 쪼개기 위반 아님: 같은 패턴 반복)
- 각 수정마다 `source_type` 정확히 — 기존 분개·재고 source_id와 키 충돌 없는 네이밍

### 6.4 보안
- `idempotency_keys.response_body`에 민감 정보(JWT·비번) 포함 시 **캐시 제외 액션** 마킹 ([IdempotencyKey(SkipCacheBody = true)])
- request_hash로 같은 키+다른 본문 공격 차단

## 7. EVF 검증 계획

| 영역 | 시나리오 | 책임자 | 통과 기준 |
|---|---|---|---|
| ④ 혼돈 | 같은 delivery confirm 100회 동시 클릭 → monthly_summary 1번만 가산 / journal_lines 1쌍만 / idempotency_keys 1행만 | **DV-D** | 100회 모두 같은 응답, DB 1회 효과 |
| ② 장애 | 미들웨어 동작 중 DB 끊김 → 캐시 실패 시 fail-open(정상 처리) vs fail-closed(503) — fail-open 선택 | DV-S | 끊김 시 멱등 보장은 깨지되 서비스는 계속 |

## 8. 완료 기준 (DoD)

- [ ] DB-18 마이그레이션 정상 (롤백 검증)
- [ ] `HitPan.Contracts/Idempotency/` 배치 (작2·작3과 공유)
- [ ] `IdempotencyMiddleware` + `IdempotencyCleanupService` 동작
- [ ] 8곳 코드 수정 — `monthly_summary_sources` 가드 적용
- [ ] 빌드 0 errors (API + Web)
- [ ] 단위·통합 테스트 통과
- [ ] EVF ④ 시나리오 100회 연타 → DB 1회 가산 검증 (DV-D 사인)
- [ ] CTO 종합
- [ ] 사장님 최종 승인
- [ ] 써밋

## 9. 리스크 + 백업 플랜

| # | 리스크 | 대응 |
|---|---|---|
| 1 | 8곳 코드 수정 중 회귀 발생 | 1곳 패턴 검증 후 7곳 일괄 — 각 수정마다 `dotnet build` |
| 2 | `HitPan.Contracts` 프로젝트 신설 첫 사례 — 솔루션 꼬임 | 작2·작3과 셋업 동기화. 마커스 리 협업 |
| 3 | 미들웨어 fail-open vs fail-closed 정책 충돌 | 본 작업 fail-open 채택. 보안 매니저 의견 반영 후 사장님 결재 |
| 4 | `response_body MEDIUMTEXT` 사이즈 폭주 | 24h TTL + 1h 주기 cleanup으로 통제. 모니터링 추가 (Phase 2) |
| 5 | DV-D 검증 시 8곳 외 추가 발견 | 별도 작지서로 분리 (4프로토콜 쪼개기) |

## 10. 일정

| 일자 | 작업 | 담당 |
|---|---|---|
| **4/25 금 오후** | 작지서 발행 + DB-18 작성 + DESCRIBE 결과 첨부 | PM + DB 개발팀 |
| **4/25 금 오후** | `HitPan.Contracts/Idempotency` 배치 + 미들웨어 구현 | 백엔드 개발팀 |
| **4/25 금 저녁** | 8곳 코드 수정 (1곳 → 어벤져스 승인 → 7곳) | 백엔드 개발팀 |
| **4/25 금 저녁** | 빌드 + 단위 테스트 + EVF ④ 시나리오 | DV-D |
| **4/25 금 야간** | CTO 종합 + 사장님 승인 + 써밋 | CTO + 사장님 |

→ **오늘 안에 완결 목표** (반나절 작업).
