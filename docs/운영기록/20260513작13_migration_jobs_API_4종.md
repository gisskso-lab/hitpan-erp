# 작업지시서 20260513작13 — migration_jobs API 4종 컨트롤러

## 0. 메타

| 항목 | 값 |
|---|---|
| **문서번호** | 20260513작13 |
| **발행일** | 2026-05-13 |
| **발행자** | PM 닥터스트레인지 |
| **A 책임자** | 백엔드 매니저 |
| **결재 트랙** | **풀** (마이그 기간 제어 권한, 헌법 #18 v3 직격) |
| **민감 영역** | DB 스키마 / API 시그니처 / 인증 / 암호화 컬럼 (raw_data 결재 #2 결과 반영) |
| **Contract-First 대상** | ✅ 신규 API |
| **EVF 영향 영역** | ② 장애 / ③ 악의 / ④ 혼돈 |
| **예상 소요** | 2일 (W3 D1~D2) |
| **Sprint** | W3 (5/13~5/19) |

## 1. 배경 (Why)

W2 D5 실측 스모크(3건) → W3 본격 100만 건 처리 진입. 현재 `MdbMigrationService`는 콘솔(`MigrationSmokeTest`)에서만 호출 가능. 운영자가 웹 UI/REST API로 제어 가능해야 함.

`migration_jobs` / `migration_checkpoints` / `migration_errors` 테이블은 W2 D2에서 신설 완료. 이를 제어하는 API 4종(시작/진행/중단/재개) 부재 → W3 D1 핵심.

## 2. 목표 산출물 (What)

### API 컨트롤러 (4 endpoint)
- `src/HitPan.API/Controllers/MigrationController.cs` 신규
  - `POST /api/migration/jobs` — 작업 시작 (MDB 업로드 + tenant 지정)
  - `GET /api/migration/jobs/{jobId}` — 진행 상황 조회 (rows/sec, error_rate, chunk_size)
  - `POST /api/migration/jobs/{jobId}/pause` — 중단 (체크포인트 보존)
  - `POST /api/migration/jobs/{jobId}/resume` — 재개 (last_pk_value부터)

### DTO (Contract-First, `HitPan.Contracts` 배치)
- `MigrationStartRequest` (tenant_id 미수신 — JWT 클레임만, 헌법 #2)
- `MigrationJobStatusResponse` (job_id, status, progress_percent, eta_seconds, chunk_size_current)
- `MigrationErrorListResponse` (페이징, raw_data는 별도 복호화 API)

### 서비스
- `MigrationOrchestrator` 신규 — Hosted Service vs IBackgroundTaskQueue 결정 (D1 권고)
- 동시 실행 차단: `migration_jobs (tenant_id, status='RUNNING')` UNIQUE 1행

### 테스트
- 통합 테스트 8 케이스 (시작/조회/중단/재개 × 정상/이상)

## 3. 비범위 (What Not)

- DAST(OWASP ZAP) 침투 — W5 별도 작지서
- raw_data 복호화 API — 결재 #2 채택 후 별도 작지서
- 청크 알고리즘 구현 — 작15에서 분리
- migration_errors 조회 UI — W4 별도

## 4. RACI

| 역할 | 담당자 |
|---|---|
| **R** | 백엔드 개발자 3명 |
| **A** | 백엔드 매니저 |
| **C** | DB 매니저 / 보안 매니저 / 본부장 춘식 |
| **V** | DV-S(보안) / DV-D(데이터) / BK(설계) |
| **F** | CTO → 사장님 |

## 5. 결재 라인

**풀 트랙**: PM → 어벤져스 리뷰 → DV-S → DV-D → BK → CTO → 사장님

## 6. 구현 가이드

### 6.1 DB
- 기존 `migration_jobs` 활용 (W2 D2 완료)
- ALTER 1건: `UNIQUE INDEX uk_tenant_running (tenant_id, status)` (RUNNING 상태 1건만)
- DESCRIBE 의무 (헌법 #13)

### 6.2 백엔드
- 컨트롤러: `[Authorize(Roles="tenant_admin")]`
- 비동기 작업: `IBackgroundTaskQueue` + `IHostedService` (Hangfire 도입은 D-63 일정 부담, 표준 패턴 사용)
- MySqlConnection 병렬 금지 (헌법 #16) — 진행 조회는 polling 또는 SignalR
- 모든 API 응답 DTO는 `HitPan.Contracts` 배치

### 6.3 프론트
- 본 작지서 비범위. 작16에서 별도 처리

### 6.4 보안
- tenant_id는 JWT 클레임만 (헌법 #2)
- POST /jobs 시 업로드 MDB 파일은 임시 폴더 → 처리 후 즉시 삭제 (헌법 #22 데이터 최소주의)
- 중단/재개 권한: tenant_admin only
- request_id 모든 응답 헤더에 포함 (감사 추적)

### 6.5 테스트·검증
- 통합 테스트 8 케이스 (Testcontainers MariaDB)
- 동시 실행 차단 시나리오 (멱등성 결재 #2 시나리오 #8과 동일)
- 권한 우회 시도 (tenant_id 파라미터 수신 시도 → 즉시 거부)

## 7. EVF 검증 계획

| 영역 | 시나리오 | 책임자 | 통과 기준 |
|---|---|---|---|
| ② 장애 | API 호출 중 DB 단절 → 재연결 시 작업 상태 복원 | DV-D | 데이터 손실 0 |
| ③ 악의 | 다른 tenant의 jobId 조회 시도 | DV-S | 403 응답 + 감사 로그 |
| ④ 혼돈 | 시작 API 100회 연타 → UNIQUE로 99회 거부 | DV-D | RUNNING 1건만 |

## 8. 완료 기준

- [ ] 빌드 0 errors + 0 warnings (헌법 #19)
- [ ] 통합 테스트 8/8 통과
- [ ] DTO Contracts 배치 완료
- [ ] EVF ②③④ 시나리오 통과
- [ ] DV-S/DV-D/BK 사인
- [ ] CTO 종합 검증
- [ ] 사장님 최종 승인 + 써밋

## 9. 리스크 + 백업

| # | 리스크 | 대응 |
|---|---|---|
| 1 | Hosted Service 충돌 (API 재시작 시 RUNNING 좀비 상태) | 시작 시 자살 자가 진단 routine + 30초 timeout 후 FAILED 마킹 |
| 2 | 대용량 MDB 업로드 timeout | 청크 업로드(Resumable.js) 또는 사전 서버 업로드 |
| 3 | 동시 실행 차단 UNIQUE 시 race condition | INSERT...ON DUPLICATE 패턴 + 재시도 |

## 10. 일정

| 일자 | 작업 | 담당 |
|---|---|---|
| 5/13 D+0 | 작지서 발행 + 어벤져스 리뷰 | PM |
| 5/14 D+1 | DB ALTER UNIQUE + 컨트롤러 스켈레톤 | 백엔드 |
| 5/15 D+2 | 4 endpoint 구현 + DTO + Orchestrator | 백엔드 |
| 5/15 D+2 | 통합 테스트 8 케이스 | 백엔드 + DV-D |
| 5/16 D+3 | EVF 검증 + 검증팀 사인 | DV-S/D/BK |
| 5/16 D+3 | 사장님 결재 + 써밋 | PM |
