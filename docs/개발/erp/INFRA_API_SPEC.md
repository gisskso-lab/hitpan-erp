# 마이그레이션 인프라 API 4개 스펙 설계서

> **작성:** 2026-05-12 W1 D3 / 백엔드매니저 + 본부장 + 보안매니저
> **헌법:** #2 (JWT), #15 (catch), #18 (송신), #20 (워크플로우), #23 (5중 검증)
> **상태:** 설계 완료, 사장님 결재 후 구현

---

## 1. API 4종 개요

```
GET    /api/migration/jobs/{jobId}/progress    진행률 조회
GET    /api/migration/jobs/{jobId}/errors      에러 목록
POST   /api/migration/jobs/{jobId}/resume      중단된 마이그 재개
POST   /api/migration/jobs/{jobId}/cancel      마이그 취소

기존 (이미 구현):
GET    /api/migration/legacy-mdb/preview       미리보기
POST   /api/migration/legacy-mdb                마이그 시작
```

---

## 2. 공통 사양

### 인증·권한
```
[Authorize(Policy = "TenantAdminOnly")]
[SupportedOSPlatform("windows")]
```

- 헌법 #2: tenant_id JWT 클레임에서만
- 헌법 #7: tenant_admin 역할 필수
- Rate Limit: 분당 10회 (남용 방지)

### 응답 표준
```json
{
  "success": true,
  "data": { ... },
  "errors": [],
  "trace_id": "uuid"
}
```

### 헌법 #15 (빈 catch 금지)
모든 catch에 `_logger.LogWarning(ex, "{Endpoint} failed: {Message}", ...)` 의무.

### 헌법 #23 (5중 검증)
모든 API PR에 다음 적용:
1. 작업지시서 보안 요구사항 명시
2. 매니저 리뷰 (절대원칙 25개)
3. CodeQL SAST 통과
4. OWASP ZAP DAST (베타 전)
5. 데이터 최소주의 검증

---

## 3. API #1 — 진행률 조회

### GET /api/migration/jobs/{jobId}/progress

**Request:**
```http
GET /api/migration/jobs/abc-123/progress
Authorization: Bearer <JWT>
```

**Response 200:**
```json
{
  "success": true,
  "data": {
    "job_id": "abc-123",
    "status": "running",
    "overall": {
      "total_tables": 32,
      "completed_tables": 8,
      "total_rows": 7616,
      "processed_rows": 3500,
      "error_rows": 12,
      "percent": 45.96
    },
    "current_table": {
      "name": "DOCFB",
      "mdb_file": "PANDATA.mdb",
      "processed_count": 3000,
      "total_count": 5000,
      "percent": 60.0,
      "chunk_size": 1000,
      "avg_commit_ms": 320
    },
    "eta": {
      "remaining_seconds": 480,
      "remaining_text": "약 8분",
      "estimated_completion": "2026-05-13T15:30:00Z"
    },
    "started_at": "2026-05-13T15:00:00Z",
    "elapsed_seconds": 1500
  }
}
```

**Response 404:**
```json
{
  "success": false,
  "errors": [{"code": "JOB_NOT_FOUND", "message": "작업을 찾을 수 없습니다."}]
}
```

**구현 요약:**
```csharp
[HttpGet("jobs/{jobId}/progress")]
public async Task<IActionResult> GetProgress(string jobId, CancellationToken ct)
{
    var tenantId = HttpContext.Items["TenantId"]?.ToString();
    if (string.IsNullOrEmpty(tenantId)) return Forbid();
    
    try
    {
        var progress = await _migrationProgressService.GetProgressAsync(jobId, tenantId, ct);
        if (progress == null) return NotFound(new { code = "JOB_NOT_FOUND" });
        return Ok(new { success = true, data = progress });
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "GetProgress failed: jobId={JobId} tenantId={TenantId}", jobId, tenantId);
        return StatusCode(500, new { success = false, errors = new[] { new { code = "INTERNAL", message = "조회 실패" } } });
    }
}
```

**EVF 영역:**
- ① 부하: 동시 100 polling 견딤 (캐시 5초)
- ⑥ 노후: 1년 후에도 조회 가능 (인덱스)

---

## 4. API #2 — 에러 목록 조회

### GET /api/migration/jobs/{jobId}/errors

**Request:**
```http
GET /api/migration/jobs/abc-123/errors?severity=error&unresolved=true&page=1&size=20
```

**Query Parameters:**
- `severity`: warning | error | critical (default: all)
- `unresolved`: true | false (default: all)
- `table`: 특정 테이블만 (예: DOCF8)
- `page`, `size`: 페이징 (default: 1, 20, max: 100)

**Response 200:**
```json
{
  "success": true,
  "data": {
    "total": 12,
    "page": 1,
    "size": 20,
    "errors": [
      {
        "error_id": "err-456",
        "table_name": "DOCF8",
        "row_pk_value": {"buy_code": 1234},
        "error_type": "encoding",
        "error_severity": "warning",
        "error_message": "한글 변환 실패 — buy_name",
        "occurred_at": "2026-05-13T15:10:00Z",
        "is_resolved": false
      }
    ],
    "summary": {
      "by_severity": { "warning": 8, "error": 3, "critical": 1 },
      "by_type": { "encoding": 6, "fk_missing": 4, "duplicate": 2 }
    }
  }
}
```

**보안 (헌법 #18 v3):**
- `raw_data` 컬럼은 응답에 **포함하지 않음**
- 응답은 마스킹된 `error_message`만 (사장님 화면 표시용)
- 개발자가 raw_data 보려면 별도 권한 (super_admin)

**구현 요약:**
```csharp
[HttpGet("jobs/{jobId}/errors")]
public async Task<IActionResult> GetErrors(
    string jobId,
    [FromQuery] string? severity,
    [FromQuery] bool? unresolved,
    [FromQuery] string? table,
    [FromQuery] int page = 1,
    [FromQuery] int size = 20,
    CancellationToken ct = default)
{
    var tenantId = HttpContext.Items["TenantId"]?.ToString();
    if (string.IsNullOrEmpty(tenantId)) return Forbid();
    
    if (size > 100) size = 100;  // 남용 방지
    
    try
    {
        var result = await _migrationErrorService.GetErrorsAsync(
            jobId, tenantId, severity, unresolved, table, page, size, ct);
        // raw_data 제외하고 응답
        return Ok(new { success = true, data = result });
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "GetErrors failed");
        return StatusCode(500, new { success = false });
    }
}
```

---

## 5. API #3 — 재개

### POST /api/migration/jobs/{jobId}/resume

**Request:**
```http
POST /api/migration/jobs/abc-123/resume
Idempotency-Key: <UUID>
```

**Response 200:**
```json
{
  "success": true,
  "data": {
    "job_id": "abc-123",
    "status": "running",
    "resumed_at": "2026-05-13T16:00:00Z",
    "resume_from_table": "DOCFB",
    "resume_from_pk": {"IJ_DT":"20251231","IJ_IO":"O","IJ_SEQ":99}
  }
}
```

**Response 409 (재개 불가):**
```json
{
  "success": false,
  "errors": [{"code": "INVALID_STATUS", "message": "현재 상태에서 재개 불가: completed"}]
}
```

**비즈니스 규칙:**
- status가 `paused` 또는 `failed`인 경우만 재개 가능
- `running` 중복 호출 = 409 반환
- `completed` 재개 시도 = 409
- 헌법 #20 멱등성: Idempotency-Key 헤더 필수

**보안:**
- tenant_admin 권한 + 본인 tenant_id의 job만 재개 가능
- 감사 로그 INSERT (누가 언제 재개)

---

## 6. API #4 — 취소

### POST /api/migration/jobs/{jobId}/cancel

**Request:**
```http
POST /api/migration/jobs/abc-123/cancel
Idempotency-Key: <UUID>
Content-Type: application/json

{
  "reason": "사장님 요청",
  "rollback": false
}
```

**Body:**
- `reason`: 취소 사유 (옵션, 감사로그용)
- `rollback`: true = 마이그된 데이터 전부 삭제, false = 현 상태 유지

**Response 200:**
```json
{
  "success": true,
  "data": {
    "job_id": "abc-123",
    "status": "canceled",
    "canceled_at": "2026-05-13T16:30:00Z",
    "rollback_executed": false,
    "processed_rows_before_cancel": 3500,
    "note": "마이그된 3,500건은 그대로 유지됩니다. 롤백 원하시면 별도 요청."
  }
}
```

**Response 200 (rollback=true):**
```json
{
  "success": true,
  "data": {
    "job_id": "abc-123",
    "status": "canceled",
    "rollback_executed": true,
    "deleted_rows": 3500,
    "deleted_tables": ["partners", "items", "employees", ...]
  }
}
```

**비즈니스 규칙:**
- `running` 또는 `paused` 상태만 취소 가능
- `completed` 취소 = 409 (이미 끝났음)
- `rollback=true`는 위험 작업 = 사장님 추가 확인 (별도 절차)
- 헌법 #18: rollback 시 본사 알림 (메타정보만)

**보안 강화:**
- `rollback=true`는 super_admin 또는 step-up 인증 필요
- 모든 cancel 감사로그 (헌법 #18 v3)

---

## 7. 미들웨어·필터 적용

### 7.1 IdempotencyMiddleware (이미 구현)
- POST /resume, /cancel 모두 적용
- Idempotency-Key 헤더 캐싱

### 7.2 RateLimitFilter
- 분당 10회 (남용 방지)
- 마이그은 일상 작업 아님

### 7.3 TenantLockFilter (신규 추가)
- 마이그 진행 중 = 같은 tenant의 다른 사용자 차단
- `tenant_locks` 테이블 활용
- 헌법 #20 워크플로우 끊김 방지

---

## 8. 신규 서비스 클래스 4개

```csharp
namespace HitPan.Application.Services.Migration;

public interface IMigrationProgressService
{
    Task<MigrationProgressDto?> GetProgressAsync(string jobId, string tenantId, CancellationToken ct);
}

public interface IMigrationErrorService
{
    Task<PagedResult<MigrationErrorDto>> GetErrorsAsync(
        string jobId, string tenantId,
        string? severity, bool? unresolved, string? table,
        int page, int size, CancellationToken ct);
}

public interface IMigrationControlService
{
    Task<MigrationJobDto> ResumeAsync(string jobId, string tenantId, string idempotencyKey, CancellationToken ct);
    Task<MigrationJobDto> CancelAsync(string jobId, string tenantId, string idempotencyKey, 
        CancelMigrationRequest request, CancellationToken ct);
}
```

---

## 9. 헌법 준수 매트릭스

| 헌법 | 적용 |
|---|---|
| #1 수정 OK 덮어쓰기 X | ✅ 신규 컨트롤러 추가만 |
| #2 tenant_id JWT만 | ✅ HttpContext.Items["TenantId"] |
| #5 암호화 | ✅ raw_data AES-256 (DDL에 정의) |
| #15 빈 catch 금지 | ✅ 모든 catch에 _logger.LogWarning |
| #18 본사 송신 0 | ✅ raw_data 응답 제외 |
| #19 errors 0 + warnings 0 | ✅ [SupportedOSPlatform("windows")] |
| #20 워크플로우 끊김 X | ✅ Idempotency + TenantLock |
| #22 데이터 최소주의 | ✅ raw_data 별도 권한 |
| #23 5중 검증 | ✅ 모든 PR 적용 |
| #24 책임 분산 | ✅ 401·403·404·409·500 명확 |
| #25 쉽게·정확하게·안전 | ✅ |

---

## 10. EVF 6대 영역 점검

| 영역 | 시나리오 | 대응 |
|---|---|---|
| ① 부하 | 동시 100 polling | 캐시 5초 + 인덱스 |
| ② 장애 | DB 끊김 중 progress 조회 | 503 + 재시도 안내 |
| ③ 악의 | 다른 tenant의 jobId 침투 | tenant_id 검증 + 403 |
| ④ 혼돈 | resume 100회 연타 | Idempotency-Key 멱등 |
| ⑤ 무지 | 사장님 cancel 후 resume | 409 + 안내 메시지 |
| ⑥ 노후 | 1년 전 job progress | 인덱스 유지 |

---

## 11. 사장님 결재 사항

| # | 사항 | 결재 |
|---|---|---|
| 1 | API 4종 스펙 적용 | ⚠️ 작업지시서 발행 후 구현 |
| 2 | rollback=true는 step-up 인증 | ✅ 헌법 #5 |
| 3 | raw_data 응답 제외 (별도 권한) | ✅ 헌법 #18 |
| 4 | TenantLockFilter 신규 도입 | ⚠️ 영향 범위 검토 후 |

---

## 12. 구현 일정 (W1 D5 ~ W2)

```
[W1 D5] 인터페이스 정의 (4개 서비스 인터페이스)
[W2 D1] IMigrationProgressService 구현
[W2 D2] IMigrationErrorService 구현
[W2 D3] IMigrationControlService 구현 (resume)
[W2 D4] IMigrationControlService 구현 (cancel + rollback)
[W2 D5] 컨트롤러 + 단위 테스트 100%
```

---

**작성:** 백엔드매니저 + 본부장 + 보안매니저
**검토:** 설계팀장 브라운킴 (Result<T,Error> 패턴)
**최종 검증:** CTO 래리 앨리슨
**적용 시점:** W1 D5 게이트 통과 후 작업지시서 발행
