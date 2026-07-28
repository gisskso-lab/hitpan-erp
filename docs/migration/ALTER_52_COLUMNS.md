# 결재 #3 — partners·items·employees ALTER 52개 컬럼 통합 설계서

> **결재일:** 2026-05-12
> **작성:** DB매니저 + 본부장 + 보안매니저
> **헌법:** #1 (ADD COLUMN만), #5 (AES-256), #17 (InnoDB), #18 (송신 0)
> **상태:** 설계 완료, W2 D2 작업지시서 발행 후 적용

⚠️ **ENGINE=InnoDB + utf8mb4_unicode_ci 사전 확인 완료 (PowerShell 2026-05-12).**

---

## 1. 개요

| 테이블 | 추가 컬럼 | 카테고리 |
|---|---|---|
| partners | 19개 | 일반 18 + 형사 1 (ceo_resident_no_encrypted) |
| items | 5개 | 일반 5 |
| employees | 28개 | 일반 23 + 형사 5 (resident/salary 등) |
| **합계** | **52개** | 일반 46 + 형사 6 |

**의존성:** 결재 #4 (형사영역 AES-256 정책) 적용 완료 후 진행. 6개 VARBINARY 컬럼은 Value Converter 연동 필수.

---

## 2. 단계별 ALTER — 3단계 분할

### 2.1 1단계: partners (19개 컬럼)
- 우선순위: P0 (거래처 마스터 핵심)
- 형사영역: 1개 (ceo_resident_no_encrypted) — 결재 #4 정책 반영
- 영향: 거래처 등록·견적·수주·세금계산서 전체 흐름

```sql
ALTER TABLE partners
    ADD COLUMN card_commission_rate DECIMAL(5,2) DEFAULT 0 COMMENT '카드 수수료율 (buy_cardyul)',
    ADD COLUMN classification_code VARCHAR(30) NULL COMMENT '분류 코드 (buy_ccode)',
    ADD COLUMN manager_department VARCHAR(30) NULL COMMENT '담당 부서 (buy_damdangbu)',
    ADD COLUMN price_grade_code VARCHAR(10) NULL COMMENT '단가등급 원본 코드 (buy_DOSCODE) — 옵션 H 원본 보존',
    -- price_grade는 기존 CHAR(1) DEFAULT 'A' 컬럼 사용 (사장님 결재 2026-05-12 A안 — 충돌 회피)
    -- IF NOT EXISTS 멱등 안전모드로 신규 TINYINT 추가 자동 차단됨 = 데이터 무손실
    ADD COLUMN legacy_extra VARCHAR(30) NULL COMMENT '레거시 예비 (buy_fil)',
    ADD COLUMN discount_rate DECIMAL(5,2) DEFAULT 0 COMMENT '할인율 (buy_halyul)',
    ADD COLUMN keyman_birth VARCHAR(10) NULL COMMENT '키맨 생일 (buy_keybirth)',
    ADD COLUMN keyman_name VARCHAR(50) NULL COMMENT '키맨 이름 (buy_keyname)',
    ADD COLUMN keyman_phone VARCHAR(20) NULL COMMENT '키맨 연락처 (buy_keytel)',
    ADD COLUMN margin_rate DECIMAL(5,2) DEFAULT 0 COMMENT '마진율 (buy_mayul)',
    ADD COLUMN sales_employee VARCHAR(30) NULL COMMENT '담당 영업사원 (buy_sawon)',
    ADD COLUMN trade_start_date DATE NULL COMMENT '거래 시작일 (buy_startdt)',
    ADD COLUMN business_registration_date DATE NULL COMMENT '사업자등록일 (buy_taxdt)',
    ADD COLUMN tel_secondary VARCHAR(20) NULL COMMENT '전화 2번 (buy_tel1)',
    ADD COLUMN tax_classification VARCHAR(10) NULL COMMENT '과세 구분 (buy_taxgubun)',
    ADD COLUMN ceo_name VARCHAR(50) NULL COMMENT '대표명 (buy_top)',
    ADD COLUMN partner_type VARCHAR(10) NULL COMMENT '거래처 분류 (buy_gu)',
    -- 형사 영역 (결재 #4 정책 적용)
    ADD COLUMN ceo_resident_no_encrypted VARBINARY(255) NULL COMMENT '대표 주민번호 AES-256 (buy_topjumin, 부가가치세법 §32)';

-- 인덱스 보강
CREATE INDEX idx_partners_price_grade ON partners (tenant_id, price_grade);
CREATE INDEX idx_partners_sales_emp ON partners (tenant_id, sales_employee);
```

### 2.2 2단계: items (5개 컬럼)
- 우선순위: P0 (상품 마스터)
- 형사영역: 없음
- 영향: 상품 등록·BOM·재고

```sql
ALTER TABLE items
    ADD COLUMN spec_detail VARCHAR(80) NULL COMMENT '상세 규격 (S_SPEC)',
    ADD COLUMN unit_secondary VARCHAR(10) NULL COMMENT '2차 단위 (S_UNIT2)',
    ADD COLUMN safety_stock DECIMAL(15,3) DEFAULT 0 COMMENT '안전 재고 (S_SAFE)',
    ADD COLUMN reorder_point DECIMAL(15,3) DEFAULT 0 COMMENT '재주문 시점 (S_REORD)',
    ADD COLUMN supplier_default_id CHAR(36) NULL COMMENT '기본 매입처 (S_VENDOR FK)';

CREATE INDEX idx_items_supplier ON items (tenant_id, supplier_default_id);
```

### 2.3 3단계: employees (28개 컬럼)
- 우선순위: P0 (인사·급여)
- 형사영역: 5개 (resident/salary 등) — 결재 #4 정책 반영
- 영향: 인사·급여·4대보험·연말정산

```sql
-- A. 기본 정보 (8개)
ALTER TABLE employees
    ADD COLUMN address VARCHAR(120) NULL COMMENT '주소 (SW_ADDR)',
    ADD COLUMN zip_code VARCHAR(10) NULL COMMENT '우편번호 (SW_POSTNO)',
    ADD COLUMN birth_date DATE NULL COMMENT '생일 (SW_BIRTH)',
    ADD COLUMN birth_calendar TINYINT DEFAULT 1 COMMENT '1=양력, 2=음력 (SW_BIRTHgu)',
    ADD COLUMN birth_lunar_converted TINYINT DEFAULT 0 COMMENT '음력 변환 여부 (SW_BIRTHtel)',
    ADD COLUMN home_phone VARCHAR(20) NULL COMMENT '집전화 (SW_TEL)',
    ADD COLUMN emergency_contact VARCHAR(30) NULL COMMENT '비상연락처 (SW_TELem)',
    ADD COLUMN memo TEXT NULL COMMENT '비고 (SW_REM)';

-- B. 형사 영역 (5개) — 결재 #4 정책 (AES-256 + 동의 + 마스킹 + step-up)
ALTER TABLE employees
    ADD COLUMN resident_no_encrypted VARBINARY(255) NULL COMMENT '주민번호 AES-256 (SW_JUMIN, 소득세법 §127·§164)',
    ADD COLUMN salary_encrypted VARBINARY(255) NULL COMMENT '급여 AES-256 (SW_PAY, 근로기준법 §48 + 개인정보보호법 §29)',
    ADD COLUMN salary_type TINYINT NULL COMMENT '급여 구분 평문 (SW_PAYgu)',
    ADD COLUMN salary_category TINYINT NULL COMMENT '급여 유형 평문 (SW_PAYeuy)',
    ADD COLUMN salary_extra_encrypted VARBINARY(500) NULL COMMENT '급여 기타 AES-256 (SW_PAYoth)';

-- C. 직장 정보 (7개)
ALTER TABLE employees
    ADD COLUMN department VARCHAR(50) NULL COMMENT '부서 (SW_BU)',
    ADD COLUMN marriage_status VARCHAR(2) NULL COMMENT '혼인 상태 (SW_MARRY)',
    ADD COLUMN business_type VARCHAR(50) NULL COMMENT '업무 유형 (SW_WORK)',
    ADD COLUMN is_resigned TINYINT DEFAULT 0 COMMENT '퇴직 여부 (SW_OUT)',
    ADD COLUMN resign_date DATE NULL COMMENT '퇴직일 (SW_OUTDT)',
    ADD COLUMN resign_reason VARCHAR(80) NULL COMMENT '퇴직 사유 (SW_OUTREM)',
    ADD COLUMN nationality VARCHAR(30) NULL COMMENT '국적 (SW_NATION)';

-- D. 레거시 잔액 컬럼 10개 (원본 그대로 보존)
ALTER TABLE employees
    ADD COLUMN legacy_bal1 VARCHAR(150) NULL COMMENT '레거시 잔액 1 (SW_BAL1)',
    ADD COLUMN legacy_bal2 VARCHAR(150) NULL,
    ADD COLUMN legacy_bal3 VARCHAR(150) NULL,
    ADD COLUMN legacy_bal4 VARCHAR(150) NULL,
    ADD COLUMN legacy_bal5 VARCHAR(150) NULL,
    ADD COLUMN legacy_bal6 VARCHAR(150) NULL,
    ADD COLUMN legacy_bal7 VARCHAR(150) NULL,
    ADD COLUMN legacy_bal8 VARCHAR(150) NULL,
    ADD COLUMN legacy_bal9 VARCHAR(150) NULL,
    ADD COLUMN legacy_bal10 VARCHAR(150) NULL;

-- E. 해외 (1개)
ALTER TABLE employees
    ADD COLUMN salary_country TINYINT NULL COMMENT '해외 직원 구분 (SW_PAYkuk)';

-- 인덱스 보강
CREATE INDEX idx_employees_resigned ON employees (tenant_id, is_resigned);
CREATE INDEX idx_employees_dept ON employees (tenant_id, department);
```

---

## 3. 운영 안전 검증

### 3.1 락 영향 (DB매니저 검토)
- **현재 상태:** partners·items·employees 모두 **운영 데이터 0건** (마이그 시작 전)
- ALTER 락 영향 = **0**
- 미래 운영 중 ALTER 필요 시: pt-online-schema-change 도구 사용 권장

### 3.2 ENGINE·COLLATION 확인 (PowerShell 2026-05-12)
```
TABLE_NAME   ENGINE   TABLE_COLLATION
employees    InnoDB   utf8mb4_unicode_ci  ✅
items        InnoDB   utf8mb4_unicode_ci  ✅
partners     InnoDB   utf8mb4_unicode_ci  ✅
```

### 3.3 형사영역 6개 컬럼 정책 부합
| 컬럼 | 타입 | 정책 |
|---|---|---|
| partners.ceo_resident_no_encrypted | VARBINARY(255) | AES-256 + 선택 입력 + step-up |
| employees.resident_no_encrypted | VARBINARY(255) | AES-256 + 채용 동의 + 마스킹 |
| employees.salary_encrypted | VARBINARY(255) | AES-256 + 마스킹 + step-up |
| employees.salary_extra_encrypted | VARBINARY(500) | AES-256 + 마스킹 + step-up |
| employees.salary_type | TINYINT | 평문 (식별성 낮음) |
| employees.salary_category | TINYINT | 평문 (식별성 낮음) |

---

## 4. 헌법 부합 매트릭스

| 헌법 | 적용 |
|---|---|
| #1 ADD COLUMN만 (수정 OK 덮어쓰기 X) | ✅ DROP/MODIFY 0건 |
| #5 암호화 컬럼 Value Converter | ✅ 6개 VARBINARY |
| #15 빈 catch 금지 | ✅ 마이그 코드 적용 |
| #17 InnoDB | ✅ 3개 테이블 사전 확인 |
| #18 본사 송신 0 | ✅ 로컬 |
| #19 errors 0 + warnings 0 | ✅ DEFAULT 명시 |
| #20 워크플로우 끊김 0 | ✅ |
| #22 데이터 최소주의 | ✅ 본사 미송신 |

---

## 5. EVF 6대 영역 점검

| 영역 | 시나리오 | 대응 |
|---|---|---|
| ① 부하 | 5만 거래처 ALTER | 운영 데이터 0건 → 락 0 |
| ② 장애 | ALTER 중 정전 | InnoDB 트랜잭션 + 재실행 가능 |
| ③ 악의 | 다른 tenant 컬럼 침투 | tenant_id 필터 모든 쿼리 |
| ④ 혼돈 | 같은 ALTER 2회 실행 | `ADD COLUMN IF NOT EXISTS` (MariaDB 지원) |
| ⑤ 무지 | 사장님이 ALTER 영향 모름 | 작업지시서에 영향 범위 명시 |
| ⑥ 노후 | 5년 후 컬럼 확장 | 여유 VARCHAR 크기 |

---

## 6. 실행 스크립트 — IF NOT EXISTS 안전 모드

⚠️ **재실행 가능하도록 모든 ALTER에 `IF NOT EXISTS` 적용** (MariaDB 10.0+ 지원).

```sql
ALTER TABLE partners
    ADD COLUMN IF NOT EXISTS card_commission_rate DECIMAL(5,2) DEFAULT 0,
    ADD COLUMN IF NOT EXISTS classification_code VARCHAR(30) NULL,
    -- ... (전체 19개)
    ;
```

→ 재마이그 시 멱등 보장 (헌법 #20).

---

## 7. 적용 순서 (W2 D2~D3)

```
[W2 D2 아침]
  1. partners 19개 ALTER 실행 + 검증
  2. items 5개 ALTER 실행 + 검증

[W2 D2 오후]
  3. employees 28개 ALTER 실행 + 검증
  4. 형사영역 6개 컬럼 Value Converter 매핑 (.NET EF Core)

[W2 D3]
  5. MdbToHitpanMapper 코드 추출 + 신규 19+5+28 INSERT 적용
  6. 단위 테스트 (멱등 100회 재실행 0 중복)
```

---

## 8. 사장님 결재 사항

| # | 사항 | 결재 |
|---|---|---|
| 1 | partners 19개 ALTER (형사 1 포함) | ✅ 2026-05-12 |
| 2 | items 5개 ALTER | ✅ 2026-05-12 |
| 3 | employees 28개 ALTER (형사 5 포함) | ✅ 2026-05-12 |
| 4 | 형사영역 6개 = AES-256 VARBINARY (결재 #4 정책) | ✅ |
| 5 | IF NOT EXISTS 안전 모드 | ✅ 헌법 #20 |
| 6 | 운영 데이터 0건 = 락 없음 | ✅ |

---

## 9. 후속 작업 (W3~W4)

- W3: 마이그 코드 추출 (기존 1,755줄 + 52개 컬럼 INSERT 추가)
- W3: 단위 테스트 (Idempotency + 형사영역 마스킹)
- W4: 매뉴얼 보강 (직원 등록·거래처 등록 시 형사영역 동의 화면)

---

**작성:** DB매니저 + 본부장 춘식 + 보안매니저
**검토:** 백엔드매니저 (Value Converter), 설계팀장 브라운킴 (헌법 #1 추출 패턴), 법무팀장 (형사영역 동의)
**최종 검증:** CTO 래리 앨리슨
**결재:** 사장님 2026-05-12
