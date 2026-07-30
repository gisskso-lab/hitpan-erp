# W3 D1 사전 분석 — 기존 MigrationController vs 작13 명세 Gap

> **작성:** 2026-05-13 PM 닥터스트레인지
> **목적:** 5/14 D+1 백엔드 매니저 실착수 전 사전 분석
> **결과:** Gap 5건 식별 → 작13 명세 조정 권고

---

## 1. 기존 자산 (이미 존재)

`src/HitPan.API/Controllers/MigrationController.cs` 115줄, endpoint 2개:

| Endpoint | 메서드 | 상태 |
|---|---|---|
| `GET /api/migration/legacy-mdb/preview` | PreviewAsync | 동기, MDB 폴더 건수 미리보기 |
| `POST /api/migration/legacy-mdb` | MigrateAsync | 동기, 일괄 처리 |

특징:
- `[Authorize(Policy = "TenantAdminOnly")]` ✅ 헌법 #2 준수
- `tenant_id`는 `HttpContext.Items["TenantId"]` 클레임 ✅
- `[SupportedOSPlatform("windows")]` (ACE 드라이버 의존) — Linux 컨테이너 미지원
- **동기 실행** — 100만건 처리 시 HTTP timeout 직격

## 2. 작13 명세 4 endpoint

| Endpoint | 작13 명세 | 기존 | Gap |
|---|---|---|---|
| `POST /api/migration/jobs` | 작업 시작 (비동기) | 없음 (동기 `POST legacy-mdb`만) | **Gap 1: 비동기 전환 필요** |
| `GET /api/migration/jobs/{jobId}` | 진행 조회 | 없음 | **Gap 2: 신설** |
| `POST /api/migration/jobs/{jobId}/pause` | 중단 | 없음 | **Gap 3: 신설 + Cancellation 인프라** |
| `POST /api/migration/jobs/{jobId}/resume` | 재개 | 없음 | **Gap 4: 신설 + 체크포인트 통합** |
| `GET /api/migration/legacy-mdb/preview` | (작13 비범위) | 있음 | **유지 권고** |

## 3. 추가 Gap

### Gap 5: DTO Contract-First 배치
- 기존 `MdbMigrationRequest`는 컨트롤러 파일 내 record로 정의
- 작13 명세: `HitPan.Contracts/Migration/` 폴더로 이전 + `MigrationStartRequest`, `MigrationJobStatusResponse`, `MigrationErrorListResponse` 신설

### Gap 6: Hosted Service / BackgroundTaskQueue
- 현재 동기 `Task<IActionResult>` 직접 await
- 작13 명세: `IBackgroundTaskQueue` + `IHostedService` 패턴
- → API 재시작 시 RUNNING 좀비 처리 자가진단 routine 필요 (작13 §9 리스크 #1)

### Gap 7: UNIQUE 제약
- 작13 명세: `UNIQUE INDEX uk_tenant_running (tenant_id, status)` (RUNNING 1건만)
- 기존 `migration_jobs` 테이블에 미적용 → D1에 ALTER 1건 추가 의무

## 4. 권고 — 작13 명세 조정

### 4.1 기존 endpoint 보존
- `GET legacy-mdb/preview` → 작13 비범위로 유지 (현장에서 이미 사용 중일 가능성)
- `POST legacy-mdb` → **deprecated** 표시 + 6개월 후 제거 예정 명시

### 4.2 D1 작업 분할 (5/14 백엔드 매니저)
- D1-A: `migration_jobs` UNIQUE ALTER (DB 매니저 30분)
- D1-B: DTO 3종 `HitPan.Contracts/Migration/` 이전 (백엔드 1시간)
- D1-C: 새 endpoint 4종 스켈레톤 + `IBackgroundTaskQueue` DI (백엔드 3시간)
- D1 완료 시점: 5/14 18:00

### 4.3 비범위 명확화
- ACE 드라이버 Linux 미지원 → 클라우드 배포 시 Windows Server 컨테이너 또는 별도 마이그 워커 (W5 결정)

## 5. 위험 신호 (D1 진입 전 확인)

| # | 위험 | 확인 방법 |
|---|---|---|
| 1 | `MdbMigrationService.MigrateAsync` 100만건 동기 실행 시 메모리 폭증 | W2 D5 스모크 3건 → W3 D1 1만건 시뮬 측정 |
| 2 | 기존 `POST legacy-mdb` 사용 중인 EXE 클라이언트 호환성 | tools/installer 코드 그레프 필요 |
| 3 | `[SupportedOSPlatform("windows")]` warnings 0 유지 | CI 빌드 시 platform 분기 확인 |

## 6. 5/14 D+1 액션 (백엔드 매니저)

1. 본 Gap 분석 어벤져스 리뷰 (백엔드 + DB + 보안 매니저 30분)
2. 작13 명세 조정안 확정 (PM 결재 후 작13 v2 발행)
3. D1-A/B/C 병렬 착수

---

## 결론

기존 자산 보존 + 신규 4 endpoint 추가 + Hosted Service 인프라 도입. 5/14 D+1 즉시 착수 가능.
