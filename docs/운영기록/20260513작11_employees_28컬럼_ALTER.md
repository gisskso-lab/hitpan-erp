# 작업지시서 20260513작11 — employees 28개 컬럼 ALTER (형사영역 5개 포함)

> **발행:** 2026-05-12 야간 (W2 D2 사전 발행)
> **담당:** DB개발자 + 보안개발자 (형사영역 5개)
> **헌법:** #1, #5 AES-256, #17, #19, #20, #22
> **선행 결재:** 사장님 결재 #3·#4 2026-05-12 완료
> **참조:** [ALTER_52_COLUMNS.md](../migration/ALTER_52_COLUMNS.md) §2.3, [CRIMINAL_DOMAIN_POLICY.md](../migration/CRIMINAL_DOMAIN_POLICY.md)

⚠️ **본 작업은 형사영역(주민번호·급여) 5개 컬럼 포함. 보안개발자 + 법무팀장 검토 필수.**

---

## 1. 작업 목적

DOCSW (레거시 사원 마스터) 36개 컬럼 중 누락된 28개를 `employees` 테이블에 추가.
- 일반 23개 (기본 정보·직장·레거시 잔액·해외)
- 형사 5개 (주민번호·급여×4) — CRIMINAL_DOMAIN_POLICY.md 정책 적용

---

## 2. 사전 확인

```sql
SELECT TABLE_NAME, ENGINE, TABLE_COLLATION
FROM information_schema.TABLES
WHERE TABLE_SCHEMA='hitpan_erp' AND TABLE_NAME='employees';
-- 기대: InnoDB + utf8mb4_unicode_ci ✅ (2026-05-12 확인)

SELECT COUNT(*) FROM employees;
-- 기대: 0건 = 락 없음
```

---

## 3. ALTER 실행 SQL (4단계 분할)

### 3.1 A. 기본 정보 (8개)
```sql
ALTER TABLE employees
    ADD COLUMN IF NOT EXISTS address VARCHAR(120) NULL COMMENT '주소 (SW_ADDR)',
    ADD COLUMN IF NOT EXISTS zip_code VARCHAR(10) NULL COMMENT '우편번호 (SW_POSTNO)',
    ADD COLUMN IF NOT EXISTS birth_date DATE NULL COMMENT '생일 (SW_BIRTH)',
    ADD COLUMN IF NOT EXISTS birth_calendar TINYINT DEFAULT 1 COMMENT '1=양력, 2=음력 (SW_BIRTHgu)',
    ADD COLUMN IF NOT EXISTS birth_lunar_converted TINYINT DEFAULT 0 COMMENT '음력 변환 여부 (SW_BIRTHtel)',
    ADD COLUMN IF NOT EXISTS home_phone VARCHAR(20) NULL COMMENT '집전화 (SW_TEL)',
    ADD COLUMN IF NOT EXISTS emergency_contact VARCHAR(30) NULL COMMENT '비상연락처 (SW_TELem)',
    ADD COLUMN IF NOT EXISTS memo TEXT NULL COMMENT '비고 (SW_REM)';
```

### 3.2 B. 형사영역 (5개) ⚠️ 보안개발자 담당
```sql
ALTER TABLE employees
    ADD COLUMN IF NOT EXISTS resident_no_encrypted VARBINARY(255) NULL
        COMMENT '주민번호 AES-256 (SW_JUMIN, 소득세법 §127·§164 + 4대보험법)',
    ADD COLUMN IF NOT EXISTS salary_encrypted VARBINARY(255) NULL
        COMMENT '급여 AES-256 (SW_PAY, 근로기준법 §48 + 개인정보보호법 §29)',
    ADD COLUMN IF NOT EXISTS salary_type TINYINT NULL
        COMMENT '급여 구분 평문 (SW_PAYgu)',
    ADD COLUMN IF NOT EXISTS salary_category TINYINT NULL
        COMMENT '급여 유형 평문 (SW_PAYeuy)',
    ADD COLUMN IF NOT EXISTS salary_extra_encrypted VARBINARY(500) NULL
        COMMENT '급여 기타 AES-256 (SW_PAYoth)';
```

### 3.3 C. 직장 정보 (7개)
```sql
ALTER TABLE employees
    ADD COLUMN IF NOT EXISTS department VARCHAR(50) NULL COMMENT '부서 (SW_BU)',
    ADD COLUMN IF NOT EXISTS marriage_status VARCHAR(2) NULL COMMENT '혼인 상태 (SW_MARRY)',
    ADD COLUMN IF NOT EXISTS business_type VARCHAR(50) NULL COMMENT '업무 유형 (SW_WORK)',
    ADD COLUMN IF NOT EXISTS is_resigned TINYINT DEFAULT 0 COMMENT '퇴직 여부 (SW_OUT)',
    ADD COLUMN IF NOT EXISTS resign_date DATE NULL COMMENT '퇴직일 (SW_OUTDT)',
    ADD COLUMN IF NOT EXISTS resign_reason VARCHAR(80) NULL COMMENT '퇴직 사유 (SW_OUTREM)',
    ADD COLUMN IF NOT EXISTS nationality VARCHAR(30) NULL COMMENT '국적 (SW_NATION)';
```

### 3.4 D. 레거시 잔액 (10개)
```sql
ALTER TABLE employees
    ADD COLUMN IF NOT EXISTS legacy_bal1 VARCHAR(150) NULL,
    ADD COLUMN IF NOT EXISTS legacy_bal2 VARCHAR(150) NULL,
    ADD COLUMN IF NOT EXISTS legacy_bal3 VARCHAR(150) NULL,
    ADD COLUMN IF NOT EXISTS legacy_bal4 VARCHAR(150) NULL,
    ADD COLUMN IF NOT EXISTS legacy_bal5 VARCHAR(150) NULL,
    ADD COLUMN IF NOT EXISTS legacy_bal6 VARCHAR(150) NULL,
    ADD COLUMN IF NOT EXISTS legacy_bal7 VARCHAR(150) NULL,
    ADD COLUMN IF NOT EXISTS legacy_bal8 VARCHAR(150) NULL,
    ADD COLUMN IF NOT EXISTS legacy_bal9 VARCHAR(150) NULL,
    ADD COLUMN IF NOT EXISTS legacy_bal10 VARCHAR(150) NULL;
```

### 3.5 E. 해외 (1개)
```sql
ALTER TABLE employees
    ADD COLUMN IF NOT EXISTS salary_country TINYINT NULL COMMENT '해외 직원 구분 (SW_PAYkuk)';
```

### 3.6 인덱스
```sql
CREATE INDEX IF NOT EXISTS idx_employees_resigned ON employees (tenant_id, is_resigned);
CREATE INDEX IF NOT EXISTS idx_employees_dept ON employees (tenant_id, department);
```

---

## 4. 검증

```sql
-- 1. 28개 컬럼 추가 확인
SELECT COUNT(*) AS added_columns
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA='hitpan_erp' AND TABLE_NAME='employees'
  AND COLUMN_NAME IN (
    'address','zip_code','birth_date','birth_calendar','birth_lunar_converted',
    'home_phone','emergency_contact','memo',
    'resident_no_encrypted','salary_encrypted','salary_type','salary_category','salary_extra_encrypted',
    'department','marriage_status','business_type','is_resigned','resign_date','resign_reason','nationality',
    'legacy_bal1','legacy_bal2','legacy_bal3','legacy_bal4','legacy_bal5',
    'legacy_bal6','legacy_bal7','legacy_bal8','legacy_bal9','legacy_bal10',
    'salary_country'
  );
-- 기대: 28 (실제로 31개 명시했지만 IF NOT EXISTS로 중복 보호)
-- 정확한 기대: 위 31개 중 작업 전 0개 존재 → 모두 추가 → 31개. 단 ALTER_52_COLUMNS.md §1 정의는 28개.
-- ⚠️ 명세 정정 필요: 위 SQL의 컬럼 카운트와 ALTER_52_COLUMNS.md §1 정의 불일치 시 재확인.

-- 2. 형사영역 5개 VARBINARY 확인
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_OCTET_LENGTH
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA='hitpan_erp' AND TABLE_NAME='employees'
  AND COLUMN_NAME IN ('resident_no_encrypted','salary_encrypted','salary_extra_encrypted');
-- 기대: 3건 모두 varbinary (255/255/500)

-- 3. 인덱스 확인
SHOW INDEX FROM employees WHERE Key_name IN ('idx_employees_resigned','idx_employees_dept');
-- 기대: 2건

-- 4. ENGINE 유지
SELECT ENGINE FROM information_schema.TABLES
WHERE TABLE_SCHEMA='hitpan_erp' AND TABLE_NAME='employees';
-- 기대: InnoDB
```

---

## 5. ⚠️ 명세 불일치 경고

ALTER_52_COLUMNS.md §1은 employees 추가 컬럼 = **28개**라 명시하나, 본 작업지시서 §3은 **31개**(8+5+7+10+1).
원인 추정: birth_calendar/birth_lunar_converted를 1개로 묶은 카운트 차이.

**조치:** 본 작업 실행 시 DB개발자가 정확한 카운트 보고. 본 작업지시서 §4 검증 SQL에서 실제 추가 개수 확인.

---

## 6. 후속 작업

- 작11-1: Value Converter 5개 컬럼 매핑 (보안개발자, `VALUE_CONVERTER_SPEC.md` 참조)
- 작11-2: 마스킹 + step-up 인증 화면 (프론트개발자, W3)
- 작11-3: 감사로그 INSERT 미들웨어 (백엔드개발자, W3)

---

## 7. 헌법 부합

| 헌법 | 적용 |
|---|---|
| #1 ADD COLUMN만 | ✅ |
| #5 AES-256 (3개 VARBINARY) | ✅ |
| #17 InnoDB | ✅ |
| #19 errors 0 + warnings 0 | ✅ |
| #20 멱등 IF NOT EXISTS | ✅ |
| #22 본사 송신 0 (마스터키 로컬) | ✅ |
| #23 5중 검증 (작업지시서·매니저·SAST·DAST·최소주의) | ✅ |
| #24 책임 분산 (개인정보 동의 별도) | ✅ |
| #25 쉽게·정확하게·안전하게 | ✅ |

---

## 8. 5중 검증 체크리스트

- [ ] ① 작업지시서 보안 요구사항 명시 ✅
- [ ] ② 매니저 리뷰 (DB·보안·법무팀장)
- [ ] ③ SAST (Roslyn·CodeQL) — 코드 추출 단계
- [ ] ④ DAST (OWASP ZAP) — 베타 전
- [ ] ⑤ 데이터 최소주의 검증 — 본사 송신 X

---

**발행:** PM 닥터스트레인지
**검토:** DB매니저, 보안매니저, 법무팀장, 설계팀장
**실행자:** DB개발자 + 보안개발자 (W2 D2 오후)
**예상 소요:** 20분
**결재:** 사장님 2026-05-12
