# 본사 메타 ping JSON 스키마

> 헌법 #18 v3 + 헌법 #22(데이터 최소주의) 정합.
> 본사가 받는 것 = (tenant_id_hash, timestamp, status, recovery_count, version)만.
> **금지**: tenant_id 원본, 직원명, 거래 데이터, IP 주소, 매출, 상품, 거래처, 일체의 업무 데이터.

---

## 1. 엔드포인트

- **URL**: `https://api.hitpan.kr/watchdog/ping`
- **Method**: POST
- **Content-Type**: `application/json`
- **TLS**: 1.3 강제 (1.2 이하 거부)
- **인증**: Bearer 토큰 (라이선스 키 SHA-256 해시)

---

## 2. 요청 페이로드 (Watchdog → 본사)

```json
{
  "tenant_id_hash": "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
  "timestamp": "2026-05-29T12:34:56Z",
  "status": "healthy",
  "recent_recovery_count": 0,
  "watchdog_version": "1.0.0",
  "process_status": {
    "MariaDB": true,
    "cloudflared": true,
    "HitPan.API": true,
    "HitPan.Web": true
  },
  "last_recovery": {
    "stage": null,
    "timestamp": null
  }
}
```

### 필드 정의

| 필드 | 타입 | 필수 | 설명 |
|---|---|---|---|
| `tenant_id_hash` | string | ✅ | `sha256:` 접두 + 64자 hex. tenant_id 원본 절대 금지 |
| `timestamp` | ISO 8601 UTC | ✅ | 워치독 발신 시각 |
| `status` | enum | ✅ | `healthy` / `recovering` / `down` |
| `recent_recovery_count` | int | ✅ | 최근 1시간 누적 봉합 횟수 |
| `watchdog_version` | string | ✅ | semver |
| `process_status` | object | ✅ | 4개 프로세스 boolean |
| `last_recovery.stage` | string\|null | ✅ | `WS-28-C` 등. 없으면 null |
| `last_recovery.timestamp` | ISO 8601\|null | ✅ | 마지막 봉합 시각 |

### 절대 금지 필드 (검증 미들웨어가 거부)

- `tenant_id` (해시 안 된 원본)
- `tenant_name`, `company_name`
- `user_email`, `user_name`
- `transaction_*`, `invoice_*`, `item_*`, `customer_*`, `employee_*`
- `revenue`, `sales`, `purchase`
- `ip_address`, `mac_address`, `disk_serial`

---

## 3. 응답 (본사 → Watchdog)

```json
{
  "received": true,
  "next_ping_seconds": 300,
  "instructions": []
}
```

| 필드 | 의미 |
|---|---|
| `received` | 본사 처리 성공 |
| `next_ping_seconds` | 다음 ping 권장 주기 (기본 300=5분) |
| `instructions` | 본사가 워치독에게 보낼 지시 배열 (예: `force_update`, `force_restart`). 정식 출시 후 활성 |

---

## 4. 인증 절차

1. 설치 시 `licenseKey` 사용자 입력
2. Watchdog 부팅 시 `Bearer sha256(licenseKey + machine_guid)` 헤더 자동 생성
3. 본사 측 검증: `licenses` 테이블의 (license_key_hash, machine_guid_hash) 일치 확인
4. 불일치 = 401 반환 + 본사 CS 알림

---

## 5. 5회 봉합 초과 시 즉시 알림 (헌법 #28-F)

`recent_recovery_count >= 5` → 메타 ping 외 별도 채널로 즉시 CS 알림:

```http
POST https://api.hitpan.kr/watchdog/emergency
Authorization: Bearer ...
{
  "tenant_id_hash": "sha256:...",
  "reason": "cooldown_exceeded",
  "stage": "WS-28-C",
  "timestamp": "2026-05-29T12:35:00Z"
}
```

본사 CS = 자동 전화 발신 (Twilio Korea / Toast SMS).

---

## 6. 본사 측 저장 스키마 (백오피스 DB)

```sql
CREATE TABLE watchdog_pings (
  id BIGINT AUTO_INCREMENT PRIMARY KEY,
  tenant_id_hash CHAR(71) NOT NULL,    -- sha256: + 64
  received_at DATETIME(3) NOT NULL,
  status ENUM('healthy','recovering','down') NOT NULL,
  recent_recovery_count INT NOT NULL DEFAULT 0,
  watchdog_version VARCHAR(20) NOT NULL,
  process_status_json JSON NOT NULL,
  last_recovery_stage VARCHAR(20) NULL,
  last_recovery_at DATETIME(3) NULL,
  INDEX idx_tenant_received (tenant_id_hash, received_at),
  INDEX idx_status_received (status, received_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE watchdog_emergencies (
  id BIGINT AUTO_INCREMENT PRIMARY KEY,
  tenant_id_hash CHAR(71) NOT NULL,
  received_at DATETIME(3) NOT NULL,
  reason VARCHAR(50) NOT NULL,
  stage VARCHAR(20) NULL,
  cs_notified_at DATETIME(3) NULL,
  cs_resolved_at DATETIME(3) NULL,
  INDEX idx_tenant_received (tenant_id_hash, received_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```

업무 데이터 컬럼 없음. 본사가 가진 정보 = 메타뿐.

---

## 7. 본사 컨트롤러 (의사코드, 백오피스 API)

```csharp
[ApiController]
[Route("watchdog")]
public class WatchdogController : ControllerBase
{
    [HttpPost("ping")]
    public async Task<IActionResult> Ping([FromBody] MetaPingPayload p)
    {
        // 1. 인증 확인 (Bearer 토큰)
        if (!_auth.Verify(Request.Headers.Authorization, out var tenantHash))
            return Unauthorized();

        if (p.TenantIdHash != tenantHash)
            return BadRequest("tenant_id_hash mismatch");

        // 2. 금지 필드 검증 (실행 시 거부)
        if (HasForbiddenField(p))
            return BadRequest("forbidden field detected (헌법 #22)");

        // 3. INSERT (UPDATE 금지 — 헌법 #3 원장)
        await _db.ExecuteAsync(@"
            INSERT INTO watchdog_pings (tenant_id_hash, received_at, status,
                recent_recovery_count, watchdog_version, process_status_json,
                last_recovery_stage, last_recovery_at)
            VALUES (@TenantIdHash, UTC_TIMESTAMP(3), @Status,
                @RecentRecoveryCount, @WatchdogVersion, @ProcessStatusJson,
                @LastRecoveryStage, @LastRecoveryAt)", p);

        return Ok(new { received = true, next_ping_seconds = 300, instructions = Array.Empty<string>() });
    }
}
```

---

## 8. 검증 체크리스트

- [ ] TLS 1.3 외 거부
- [ ] tenant_id 원본 필드 탐지 시 400 + 로그 박제
- [ ] 업무 데이터 필드 명세 위반 시 400
- [ ] 미등록 라이선스 = 401
- [ ] 5회 초과 시 emergency 엔드포인트로 전환
- [ ] 본사 측 저장 컬럼 = 메타 + 카운터만

---

**문서 끝.** 다음: 매니저 4인 작업지시서 + 사장님 결재 5건 1페이지.
