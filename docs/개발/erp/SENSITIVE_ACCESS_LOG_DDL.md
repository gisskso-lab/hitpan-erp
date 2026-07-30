# SENSITIVE_ACCESS_LOG — 형사영역 접근 감사 로그 설계서

> **문서번호:** 20260513설1
> **작성자:** PM(닥터스트레인지) + 보안매니저 + DB매니저 + 법무팀장
> **작성일:** 2026-05-13
> **상태:** 설계 초안 (DB 실행 전 사장님 결재 필요)
> **연관 헌법:** #2 (tenant_id JWT) · #3 (INSERT ONLY) · #5 (암호화) · #17 (InnoDB) · #18 v3 (데이터 최소주의) · #22 (사장님 데이터는 사장님 것)
> **연관 문서:**
> - `docs/migration/CRIMINAL_DOMAIN_POLICY.md` — 형사영역 6컬럼 정의
> - `docs/migration/VALUE_CONVERTER_SPEC.md` — AES-256 Value Converter
> - `docs/governance/SENSITIVE_FIELD_MASKING.md` (예정) — 마스킹 헬퍼

---

## 0. 한 줄 요약

> **"누가 / 언제 / 어떤 행의 / 어떤 컬럼을 / 어떤 목적으로 / 마스킹·평문 중 어느 형태로 조회했는가" — 6하원칙을 모두 적재한다.**

평문 데이터 자체는 본 로그에 남지 않는다. **"접근 사실"만 남는다.** (헌법 #22 — 본사 데이터 최소주의 정신을 테넌트 DB 내부에도 동일하게 적용)

---

## 1. 배경 — 왜 지금 만드는가

### 1.1 헌법 위반 리스크
- **헌법 #18 v3 (2026-04-30):** 형사 영역 9개 법령(개보법 §29 / 신정법 §19 / 정통망법 §28 등) — 본사 차단은 완료. **그러나 테넌트 ERP 내부에서도 "누가 평문을 봤는가" 추적은 누락된 상태.**
- **헌법 #22 (2026-05-12):** "본사가 안 알면 본사가 책임질 일 없다." 동일 원리로, 평문 접근은 **반드시 흔적이 남아야** 한다. 흔적이 없으면 — 침해 사고 시 책임자 특정 불가 → 대표(사장님) 형사처벌 위험.
- **현 구조:** `IBinaryCryptoService.DecryptBytes()`는 누구나 호출하면 평문이 떨어진다. 호출자 식별 없음, 목적 없음, 로그 없음. **즉시 사고 영역.**

### 1.2 법령 매핑
| 법령 | 조항 | 요구사항 | 본 설계 대응 |
|---|---|---|---|
| 개인정보보호법 | §29 안전성 확보조치 | 접속기록 1년 이상 보관 (5만 명 이상은 2년) | created_at + user_id + IP 보관 (3년 권장) |
| 개인정보보호법 시행령 | §30 ① 5호 | 개인정보 처리 시스템 접속기록 보관·점검 | access_type + purpose_code |
| 신용정보법 | §19 / §20 | 신용정보 처리내역 기록 | target_column = account 별도 추적 |
| 정보통신망법 | §28 | 접근통제·접속기록·암호화 | client_ip + user_agent + request_id |

### 1.3 사고 시나리오 (이게 없으면 발생)
1. 직원이 퇴사자 주민번호 1만 건 일괄 평문 다운로드 → 흔적 없음 → 외부 유출 → 사장님이 형사 책임
2. CS 담당이 호기심으로 대표이사 주민번호 조회 → 흔적 없음 → 내부 통제 실패
3. 침해 사고 발생 시 KISA 신고 의무 — "접속기록을 제출하라" → 제출 불가 → 과징금

---

## 2. DDL 전문

```sql
-- ============================================================
-- sensitive_access_log
-- 형사영역(주민번호·급여·계좌·CEO주민번호) 접근 감사 로그
-- INSERT ONLY · UPDATE/DELETE 차단 (헌법 #3)
-- ENGINE=InnoDB · utf8mb4_unicode_ci (헌법 #17)
-- ============================================================
CREATE TABLE IF NOT EXISTS sensitive_access_log (
    id              BIGINT          NOT NULL AUTO_INCREMENT,

    -- 테넌트 격리 (헌법 #2 — JWT 클레임에서만)
    tenant_id       VARCHAR(50)     NOT NULL
                    COMMENT 'JWT tenant_id 클레임, 파라미터 수신 금지',

    -- 행위자
    user_id         BIGINT          NOT NULL
                    COMMENT 'JWT subject (employees.id)',
    user_login      VARCHAR(100)    NULL
                    COMMENT '조회 편의용 snapshot, 사원 로그인ID',

    -- 대상
    target_table    VARCHAR(50)     NOT NULL
                    COMMENT 'employees | partners',
    target_row_id   BIGINT          NOT NULL
                    COMMENT '대상 행 PK',
    target_column   VARCHAR(50)     NOT NULL
                    COMMENT 'resident_no | salary | account | ceo_resident_no',

    -- 행위
    access_type     ENUM(
                        'READ_MASKED',      -- 마스킹 조회 (가장 흔함)
                        'READ_PLAIN',       -- 평문 조회 (PAYROLL/CONTRACT 등 제한 목적)
                        'WRITE',            -- 등록·수정 (암호화 저장)
                        'DECRYPT_EXPORT'    -- 평문 외부 반출 (가장 위험)
                    ) NOT NULL,

    -- 목적 (사전 정의 + 자유 메모)
    purpose_code    VARCHAR(30)     NOT NULL
                    COMMENT 'PAYROLL | CONTRACT | AUDIT | CS_REQ | LEGAL_REQ | TAX_FILING | ETC',
    purpose_note    VARCHAR(500)    NULL
                    COMMENT '자유 메모 (예: "2026-05 급여 처리", "노무사 요청")',

    -- 컨텍스트
    client_ip       VARCHAR(45)     NULL
                    COMMENT 'IPv6 대응 (45자)',
    user_agent      VARCHAR(500)    NULL,
    request_id      VARCHAR(50)     NULL
                    COMMENT 'correlation id, 동일 요청 묶음 추적',

    -- 시각
    created_at      DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3)
                    COMMENT '밀리초 정밀도 (1초 10건 알람용)',

    PRIMARY KEY (id),
    KEY idx_sal_target  (tenant_id, target_table, target_row_id),
    KEY idx_sal_user    (tenant_id, user_id, created_at),
    KEY idx_sal_time    (tenant_id, created_at),
    KEY idx_sal_purpose (tenant_id, purpose_code, created_at),
    KEY idx_sal_type    (tenant_id, access_type, created_at)
)
ENGINE=InnoDB
DEFAULT CHARSET=utf8mb4
COLLATE=utf8mb4_unicode_ci
COMMENT='형사영역 접근 감사 로그 — INSERT ONLY · 3년 보관 · 헌법 #18 v3 대응'
-- 파티셔닝 (1년 100만 건 가정 — RANGE BY YEAR)
PARTITION BY RANGE (YEAR(created_at)) (
    PARTITION p2026 VALUES LESS THAN (2027),
    PARTITION p2027 VALUES LESS THAN (2028),
    PARTITION p2028 VALUES LESS THAN (2029),
    PARTITION p2029 VALUES LESS THAN (2030),
    PARTITION p_future VALUES LESS THAN MAXVALUE
);

-- ============================================================
-- INSERT ONLY 강제 — UPDATE/DELETE 차단 트리거
-- (헌법 #3 정신 — 원장 무결성)
-- ============================================================
DELIMITER $$

CREATE TRIGGER trg_sal_no_update
BEFORE UPDATE ON sensitive_access_log
FOR EACH ROW
BEGIN
    SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'sensitive_access_log: UPDATE forbidden (INSERT ONLY)';
END$$

CREATE TRIGGER trg_sal_no_delete
BEFORE DELETE ON sensitive_access_log
FOR EACH ROW
BEGIN
    SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'sensitive_access_log: DELETE forbidden (INSERT ONLY, 3년 보관 의무)';
END$$

DELIMITER ;
```

### 2.1 DDL 설계 근거
- `DATETIME(3)`: 1초 10건 이상 동일 user 알람을 위해 밀리초 정밀도 필수
- `ENUM access_type`: 문자열 자유 입력보다 enum이 인덱스 효율 + 오타 차단
- `VARCHAR(45) client_ip`: IPv6 최대 길이 39자 + 여유 + IPv4 매핑 표기 대응
- 파티셔닝: 3년 보관 × 1테넌트당 1년 100만 건 가정. DROP PARTITION으로 만료 행 일괄 삭제 (논리적으로는 보관 만료라 #3 위배 아님 — 별도 정책 결재 필요)
- 트리거 RAISE: 어플리케이션 우회(직접 SQL Workbench 접속) 대응. **단, DBA root 권한으로 트리거 자체를 DROP하면 무력화** — 따라서 root 패스워드는 본사 보안금고 격리(Phase 2)

---

## 3. 보관·삭제 정책

| 단계 | 기준 | 처리 |
|---|---|---|
| 활성 | 최근 3년 | hot 파티션, 모든 인덱스 활성 |
| 만료 | 3년 초과 | DROP PARTITION p{YYYY} — 별도 결재 후 |
| 침해사고 | 신고 발생 시 | 해당 시점 전후 6개월 별도 cold storage 백업 후 보관 |

- **개보법 §29 + 시행령 §30:** 5만 명 미만 1년, 5만 명 이상 또는 고유식별정보(주민번호) 처리 시 **2년**. 본 설계는 **3년**(안전 마진).
- DROP PARTITION은 ALTER TABLE 이벤트로 audit_log(별도 시스템 감사 로그)에 기록 의무.

---

## 4. 어플리케이션 훅 — IBinaryCryptoService 통합

### 4.1 호출 흐름
```
Controller
  └─ [SensitiveAccess(purpose: "PAYROLL", type: AccessType.ReadPlain)]
       └─ Service.GetEmployeeSalary(id)
            └─ IBinaryCryptoService.DecryptBytes(encryptedSalary)
                 ├─ (전) SensitiveAccessLogger.LogAttempt(...)
                 ├─ AES-256 GCM Decrypt
                 └─ (후) SensitiveAccessLogger.LogSuccess(...)
```

### 4.2 인터셉터 구현 위치
- `src/HitPan.API/Security/SensitiveAccessAttribute.cs` (신규) — MVC ActionFilter
- `src/HitPan.Core/Crypto/AuditedBinaryCryptoService.cs` (신규) — IBinaryCryptoService 데코레이터
  - Decorator 패턴: 기존 BinaryCryptoService를 감싸서 호출 전후 로그 적재
  - DI 등록: `services.Decorate<IBinaryCryptoService, AuditedBinaryCryptoService>();`

### 4.3 누락 방지 — 빌드 단계
- Roslyn Analyzer 신규: `IBinaryCryptoService.DecryptBytes`를 호출하는 메서드는 반드시
  - `[SensitiveAccess]` 속성 보유, **또는**
  - `SensitiveAccessContext.Begin(purpose, ...)` using 블록 내부에서 호출
  - 위반 시 **CS 컴파일 에러** (헌법 #19 — warnings 0)

### 4.4 런타임 거부
- AsyncLocal로 현재 SensitiveAccessContext 보유
- DecryptBytes 진입 시 context == null → `InvalidOperationException("형사영역 접근은 purpose_code 필수 (헌법 #18 v3)")` throw
- 컨트롤러 외부(배치·테스트)에서도 강제

---

## 5. 조회 API — OpenAPI 명세

### 5.1 권한
- `tenant_admin` 전용 (대표·관리자만)
- 일반 사원은 본인 행위만 조회 가능 (별도 엔드포인트, Phase 2)

### 5.2 GET /api/audit/sensitive-access

```yaml
paths:
  /api/audit/sensitive-access:
    get:
      summary: 형사영역 접근 감사 로그 조회
      tags: [Audit]
      security:
        - bearerAuth: []
      x-required-role: tenant_admin
      parameters:
        - name: from
          in: query
          required: true
          schema: { type: string, format: date-time }
          description: 조회 시작 (KST, ISO8601)
        - name: to
          in: query
          required: true
          schema: { type: string, format: date-time }
        - name: user_id
          in: query
          required: false
          schema: { type: integer, format: int64 }
        - name: target_table
          in: query
          required: false
          schema: { type: string, enum: [employees, partners] }
        - name: target_row_id
          in: query
          required: false
          schema: { type: integer, format: int64 }
        - name: target_column
          in: query
          required: false
          schema:
            type: string
            enum: [resident_no, salary, account, ceo_resident_no]
        - name: access_type
          in: query
          required: false
          schema:
            type: string
            enum: [READ_MASKED, READ_PLAIN, WRITE, DECRYPT_EXPORT]
        - name: purpose_code
          in: query
          required: false
          schema: { type: string }
        - name: page
          in: query
          schema: { type: integer, default: 1, minimum: 1 }
        - name: page_size
          in: query
          schema: { type: integer, default: 50, minimum: 1, maximum: 500 }
      responses:
        '200':
          description: OK
          content:
            application/json:
              schema:
                type: object
                properties:
                  total:    { type: integer }
                  page:     { type: integer }
                  page_size: { type: integer }
                  items:
                    type: array
                    items: { $ref: '#/components/schemas/SensitiveAccessLogItem' }
        '403': { description: tenant_admin 권한 필요 }
        '400': { description: from > to · 범위 90일 초과 등 }

components:
  schemas:
    SensitiveAccessLogItem:
      type: object
      properties:
        id:             { type: integer, format: int64 }
        user_id:        { type: integer, format: int64 }
        user_login:     { type: string, example: "kim.payroll" }
        target_table:   { type: string, example: "employees" }
        target_row_id:  { type: integer, format: int64 }
        target_column:  { type: string, example: "salary" }
        access_type:    { type: string, example: "READ_PLAIN" }
        purpose_code:   { type: string, example: "PAYROLL" }
        purpose_note:   { type: string, nullable: true }
        client_ip:      { type: string, example: "203.0.113.42" }
        user_agent:     { type: string }
        request_id:     { type: string }
        created_at:     { type: string, format: date-time }
        # 주의: 평문 데이터는 절대 응답에 포함하지 않음 (접근 사실만)
```

### 5.3 마스킹 정책 (응답 본문)
- `target_column` 값 자체는 평문 표시 가능 (메타데이터일 뿐)
- 평문 데이터 본문(주민번호 13자리 등)은 **본 API에 절대 포함 금지**
- `client_ip`는 평문 표시 (관리자 추적 목적)
- `user_login`은 평문 표시 (snapshot 시점)

### 5.4 알람 API (별도)
- `GET /api/audit/sensitive-access/alerts`
- 룰: 동일 user_id가 1초 내 10건 이상 READ_PLAIN 발생 → 알람 row 생성
- 대표 카카오톡 알림톡 발송 (Phase 2: AI CS 시스템 연동)

---

## 6. 테스트 시나리오

| # | 시나리오 | 기대결과 |
|---|---|---|
| T1 | 정상 — Controller에 [SensitiveAccess(purpose:"PAYROLL")] 부착 후 급여 조회 | sensitive_access_log 1행 적재, access_type=READ_PLAIN |
| T2 | 마스킹 조회 — 일반 사원이 본인 정보 조회 | access_type=READ_MASKED 1행 적재 |
| T3 | 우회 시도 1 — Controller에서 [SensitiveAccess] 누락 후 DecryptBytes 호출 | 컴파일 에러 (Roslyn Analyzer) |
| T4 | 우회 시도 2 — 테스트 코드에서 DecryptBytes 직접 호출 (속성 없음) | 런타임 InvalidOperationException |
| T5 | 우회 시도 3 — 직접 SQL `UPDATE employees SET resident_no = ...` | 평문 SELECT는 가능하나 본 로그 우회됨 — 별도 DB Audit Plugin 필요 (Phase 2) |
| T6 | INSERT ONLY — `UPDATE sensitive_access_log SET ...` 시도 | 트리거 SQLSTATE 45000, 거부 |
| T7 | INSERT ONLY — `DELETE FROM sensitive_access_log` 시도 | 트리거 SQLSTATE 45000, 거부 |
| T8 | 알람 — 동일 user가 1초 내 10건 READ_PLAIN | 알람 row 생성 + 대표 통보 |
| T9 | 멀티테넌트 격리 — tenant A 관리자가 tenant B 로그 조회 시도 | JWT tenant_id 미스매치 → 403 또는 결과 0건 |
| T10 | 파티셔닝 — 2027년 데이터 INSERT | p2027 파티션에 적재 확인 |
| T11 | 동시성 — 100 동시 INSERT | 데드락 없음, 100건 모두 적재 |
| T12 | 시각 정밀도 — 같은 user가 같은 ms에 2건 INSERT | created_at 동일 가능 → id로 정렬 보장 |

### 6.1 우회 잔존 리스크 (T5)
- DBA root 권한 보유자가 직접 SELECT로 평문을 본 경우 → 본 어플리케이션 로그는 적재 안 됨
- **대응 (Phase 2):**
  - MariaDB Audit Plugin 활성화 + 별도 cold log 서버 전송
  - 본사가 아닌 **법무팀 또는 외부 감사인**만 cold log 접근 (헌법 #22 — 본사도 못 본다)

---

## 7. 보안 매니저 체크리스트

- [x] 테넌트 격리 — tenant_id JWT 클레임에서만 (헌법 #2)
- [x] INSERT ONLY — UPDATE/DELETE 트리거 차단 (헌법 #3)
- [x] 평문 미적재 — 본 로그에는 "접근 사실"만 (헌법 #22)
- [x] 보관 3년 — 개보법 §29 + 시행령 §30 충족
- [x] 누락 방지 — Roslyn Analyzer + 런타임 거부 2중
- [x] 권한 분리 — tenant_admin 전용 조회
- [x] 알람 — 1초 10건 이상 자동 탐지
- [ ] DB Audit Plugin (Phase 2) — root 권한 우회 대응
- [ ] Cold log 외부 분리 (Phase 2) — 본사도 접근 불가

---

## 8. DB 매니저 체크리스트

- [x] ENGINE=InnoDB (헌법 #17)
- [x] utf8mb4_unicode_ci
- [x] 인덱스 5종 — (target), (user+time), (time), (purpose+time), (type+time)
- [x] 파티셔닝 RANGE(YEAR) — 만료 행 일괄 처리
- [x] DATETIME(3) — 알람용 정밀도
- [x] BIGINT id — 1테넌트 3년 300만 건 × 1000테넌트 = 30억 건 대응
- [ ] DDL 실행 전 사장님 결재 (본 문서 승인 후 W2 D7 적용)

---

## 9. 영향 범위

### 9.1 코드 신규
- `src/HitPan.Core/Crypto/AuditedBinaryCryptoService.cs`
- `src/HitPan.Core/Crypto/SensitiveAccessContext.cs` (AsyncLocal)
- `src/HitPan.API/Security/SensitiveAccessAttribute.cs`
- `src/HitPan.API/Controllers/AuditController.cs` (GET /api/audit/sensitive-access)
- `src/HitPan.API/Services/SensitiveAccessLogService.cs`
- `src/HitPan.Analyzers/SensitiveAccessAnalyzer.cs` (Roslyn)

### 9.2 코드 수정
- `Program.cs` — Decorator 등록 (Scrutor 패키지)
- 기존 `IBinaryCryptoService` 호출 지점 전수 점검 (헌법 #12 — 인터페이스 확장 시 grep 의무)

### 9.3 DB 신규
- `sensitive_access_log` 테이블 1종 + 트리거 2종

---

## 10. 작업지시서 초안 (작19)

```
═══════════════════════════════════════════════════════
작업지시서 20260513작19 — sensitive_access_log 도입
═══════════════════════════════════════════════════════
발행자: PM(닥터스트레인지)
승인자: 사장님 (결재 대기)
담당: 보안매니저(리드) + DB매니저 + 백엔드 매니저
긴급도: P0 (헌법 #18 v3 미이행 영역)
예상 공수: 3일 (W2 D7~D9)

[목표]
형사영역 6컬럼(employees.resident_no/salary/account, 
partners.ceo_resident_no) 접근 감사 로그를 어플리케이션
+ DB 양단에서 강제 적재.

[산출물]
1) DDL 실행: sensitive_access_log + 트리거 2종 (DB매니저)
2) Decorator: AuditedBinaryCryptoService (백엔드)
3) Attribute: [SensitiveAccess(purpose:..., type:...)] (백엔드)
4) Analyzer: 누락 시 컴파일 에러 (백엔드)
5) Controller: GET /api/audit/sensitive-access (백엔드)
6) 테스트: T1~T12 12케이스 모두 PASS

[금지]
- 평문 데이터를 본 로그에 적재 금지 (헌법 #22)
- tenant_id 파라미터 수신 금지 (헌법 #2)
- 본사 서버 전송 금지 (헌법 #18)

[게이트]
- 5중 검증(헌법 #23) 통과 후 머지
- 테스트 12/12 GREEN
- warnings 0 (헌법 #19)
- 어벤져스: 보안매니저 + DB매니저 + 법무팀장 컨펌

[일정]
W2 D7: DDL 결재·적용 + Decorator 골조
W2 D8: Analyzer + Attribute + Controller
W2 D9: 테스트 12케이스 + 어벤져스 리뷰 + 써밋
═══════════════════════════════════════════════════════
```

---

## 11. 사장님 결재 요청 항목

| # | 항목 | 옵션 | 권고 |
|---|---|---|---|
| Q1 | 보관 기간 | A) 1년 B) 2년 C) 3년 D) 5년 | **C) 3년** (개보법 안전 마진) |
| Q2 | 파티셔닝 만료 처리 | A) DROP PARTITION B) 영구 보관 | A) DROP — 단, 만료 시 별도 결재 |
| Q3 | DB Audit Plugin | A) Phase 1 즉시 B) Phase 2 베타 후 | **B) Phase 2** (W2는 어플 레벨까지) |
| Q4 | Cold log 외부 분리 | A) 본사 cold storage B) 외부 감사인 KMS | **B) 외부** (헌법 #22 정신) |
| Q5 | 알람 임계치 | A) 1초 10건 B) 10초 50건 C) 둘 다 | **C) 둘 다** (단기·중기 이중 감지) |
| Q6 | 작19 발행 | A) 즉시 발행 B) W3로 연기 | **A) 즉시** (헌법 #18 v3 미이행 P0) |

---

**문서 끝.**
사장님 결재 후 W2 D7부터 작19 가동. DB 실행은 결재 후에만.
