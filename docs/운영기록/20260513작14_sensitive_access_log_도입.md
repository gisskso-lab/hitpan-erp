# 작업지시서 20260513작14 — sensitive_access_log 도입 (형사영역 접근 감사)

## 0. 메타

| 항목 | 값 |
|---|---|
| **문서번호** | 20260513작14 |
| **발행일** | 2026-05-13 |
| **발행자** | PM 닥터스트레인지 |
| **A 책임자** | 보안 매니저 |
| **결재 트랙** | **풀** (헌법 #18 v3 형사영역 직격) |
| **민감 영역** | DB 스키마 / 인증 / 암호화 컬럼 / 원장(INSERT ONLY) |
| **Contract-First 대상** | ✅ 신규 감사 조회 API |
| **EVF 영향 영역** | ③ 악의 / ⑤ 무지 / ⑥ 노후 |
| **예상 소요** | 3일 (W3 D7~D9) |
| **Sprint** | W3 (5/13~5/19) |

## 1. 배경 (Why)

형사영역 6컬럼(employees.resident_no/salary/account + partners.ceo_resident_no) AES-256 암호화는 W2 D2~D4에서 완료. 그러나 **누가/언제/왜 접근했는지 감사 로그 부재** → 개인정보보호법 §29 + 신용정보법 §19 안전성 확보조치 미충족.

설계 문서 `docs/migration/SENSITIVE_ACCESS_LOG_DDL.md` 완성(2026-05-13). 본 작지서로 구현 단계 진입.

## 2. 목표 산출물

### DB
- `sensitive_access_log` 테이블 신규 (DDL은 SENSITIVE_ACCESS_LOG_DDL.md §1~§3 그대로)
- RANGE(YEAR(created_at)) 파티셔닝 (2026~2030)
- UPDATE/DELETE 차단 트리거 2종 (SQLSTATE 45000)
- 인덱스 5종

### 어플리케이션 (3중 강제)
- `AuditedBinaryCryptoService` 신규 (Decorator pattern, IBinaryCryptoService 래핑)
- `[SensitiveAccess(purpose="PAYROLL")]` ActionFilter 신규
- Roslyn Analyzer `HP0001_SensitiveAccessRequired` 신규 (컴파일 에러)
- AsyncLocal `SensitiveAccessContext` (purpose 누락 시 InvalidOperationException)

### API
- `GET /api/audit/sensitive-access` (tenant_admin 전용, 페이징, 평문 데이터 응답 금지)
- DTO `SensitiveAccessLogEntry` (Contracts 배치)

### 테스트
- 12 케이스 (T1~T12, SENSITIVE_ACCESS_LOG_DDL.md §6 그대로)

## 3. 비범위

- MariaDB Audit Plugin 도입 — Phase 2 (베타 이후)
- 외부 감사인 cold storage — Phase 2
- 알람 임계치 자동 알림 — W5 별도

## 4. RACI

| 역할 | 담당자 |
|---|---|
| **R** | 보안 개발자 3명 + 백엔드 개발자 1명 |
| **A** | 보안 매니저 |
| **C** | 법무팀장 / DB 매니저 / AI수석 |
| **V** | DV-S(보안) / 법무팀장 / BK |
| **F** | CTO → 사장님 |

## 5. 결재 라인

**풀 트랙**

## 6. 구현 가이드

### 6.1 DB
- DDL 실행 전 백업 의무 (헌법 #19 정신)
- ENGINE=InnoDB, utf8mb4_unicode_ci (헌법 #17)
- DESCRIBE 후 IF NOT EXISTS 멱등

### 6.2 백엔드
- Decorator: `services.AddScoped<IBinaryCryptoService, BinaryCryptoServiceAdapter>(); services.Decorate<IBinaryCryptoService, AuditedBinaryCryptoService>();`
- ActionFilter: 컨트롤러 메서드에 `[SensitiveAccess(purpose: PurposeCodes.Payroll)]` 필수
- AsyncLocal Context: `SensitiveAccessContext.Current` null이면 DecryptBytes 호출 즉시 throw

### 6.3 프론트
- 본 작지서 비범위. 감사 조회 UI는 W4 별도

### 6.4 보안
- tenant_id JWT 클레임 (헌법 #2)
- INSERT ONLY (헌법 #3) — 트리거 2종 SQLSTATE 45000
- 평문 데이터 본 로그에 절대 미적재 (헌법 #22)
- 본사 차단 (헌법 #18 v3)

### 6.5 테스트
- T1~T12 (SENSITIVE_ACCESS_LOG_DDL.md §6)
- 우회 시도: DBA root로 UPDATE/DELETE → 트리거 차단 확인
- 누락 시도: `[SensitiveAccess]` 없이 DecryptBytes → 컴파일 에러 + 런타임 throw

## 7. EVF 검증

| 영역 | 시나리오 | 책임자 | 통과 기준 |
|---|---|---|---|
| ③ 악의 | 다른 tenant의 감사 로그 조회 시도 | DV-S | 403 + 자신도 감사 적재 |
| ⑤ 무지 | 신규 개발자가 DecryptBytes 직접 호출 | AI수석 | Roslyn 컴파일 에러 |
| ⑥ 노후 | 3년치 1000만건 적재 + 파티션 만료 시뮬 | DB | 조회 100ms 이내 |

## 8. 완료 기준

- [ ] 빌드 0/0 (헌법 #19)
- [ ] 테스트 12/12
- [ ] T5(DBA root 우회) 리스크 명시 + Phase 2 일정 명문화
- [ ] 법무팀장 사인 (개보법 §29 매핑 확인)
- [ ] CTO + 사장님 승인 + 써밋

## 9. 리스크 + 백업

| # | 리스크 | 대응 |
|---|---|---|
| 1 | DBA root SQL 직접 우회 | Phase 2 MariaDB Audit Plugin (베타 이후) + 외부 감사인 cold storage |
| 2 | 적재 폭증 (모든 화면 조회 시) | READ_MASKED는 샘플링(1/10) 적재, READ_PLAIN은 100% |
| 3 | Roslyn Analyzer 오탐 | exception attribute 화이트리스트 (마이그 서비스 등) |

## 10. 일정

| 일자 | 작업 | 담당 |
|---|---|---|
| 5/14 D+0 | DDL 실행 + 트리거 | DB |
| 5/15 D+1 | Decorator + ActionFilter + Context | 보안 |
| 5/16 D+2 | Roslyn Analyzer | 보안 + AI수석 |
| 5/17 D+3 | 조회 API + 테스트 12 | 백엔드 |
| 5/18 D+4 | EVF + 법무팀장 사인 | DV-S + 법무 |
| 5/19 D+5 | 사장님 승인 + 써밋 | PM |
