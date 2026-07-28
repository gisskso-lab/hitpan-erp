# API 미들웨어 약관 동의 차단 로직

> **작성일**: 2026-05-26 (W4 가도)
> **헌법 정합**: #24 (책임 분산 + 가르침 의무) + #18 v3 (데이터 경계) + #25 (3대 원칙)
> **상위 문서**: `약관_v1.0.0_4건_법무팀장박제.md` (W3)
> **결재**: 사장님 "응 모두결재" (2026-05-26)

---

## 0. 본 미들웨어의 존재 이유

> 헌법 #24: *"책임 분산 ≠ 방치. 가르치지 않고 책임을 넘기는 것은 거짓말이다."*

약관 동의 = 책임 분산의 출발점. 동의 안 받고 운영 시작 = 본사가 책임 떠넘기는 거짓말.
첫 로그인 시점에 4건 강제 동의 + 버전 변경 시 재동의 강제. **미들웨어가 잊지 않는다.**

---

## 1. 약관 4건 정합 (W3 박제 v1.0.0)

| # | 약관 | 필수 | 비고 |
|---|---|---|---|
| 1 | 서비스 이용약관 | 필수 | 헌법 #24 책임 분산 명시 |
| 2 | 개인정보 처리방침 | 필수 | 개인정보보호법 §15·§17 |
| 3 | 위치정보 이용약관 | 선택→필수 | 디바이스 텔레메트리 5분 주기 |
| 4 | 마케팅 정보 수신 동의 | 선택 | 마케팅팀장 영역, 미동의 시에도 운영 가능 |

미들웨어 차단 대상: 1·2·3 미동의 시 차단 (4는 선택이라 차단 안 함).

---

## 2. 차단 흐름 (Sequence)

```
[Client] ─ HTTP Request ─→ [TermsEnforcementMiddleware]
                                     │
                                     ├─ JWT 클레임에서 tenant_id + user_id 추출
                                     ├─ tenant_terms_consent 조회
                                     ├─ 미동의 / 버전 불일치 발견
                                     │       │
                                     │       └─→ 401 + { redirect: "/terms/consent" }
                                     │
                                     └─ 정상 동의 확인 → next()
```

**예외 경로 (차단 면제)**:
- `/api/auth/login`
- `/api/auth/refresh`
- `/api/terms/current` (약관 본문 조회)
- `/api/terms/consent` (동의 POST)
- `/api/health`
- `/api/version`

---

## 3. 미들웨어 코드 영역 (`TermsEnforcementMiddleware.cs`)

위치: `src/HitPan.API/Middleware/TermsEnforcementMiddleware.cs` (신규)

```csharp
public class TermsEnforcementMiddleware
{
    private static readonly HashSet<string> ExemptPaths = new()
    {
        "/api/auth/login", "/api/auth/refresh",
        "/api/terms/current", "/api/terms/consent",
        "/api/health", "/api/version"
    };

    public async Task InvokeAsync(HttpContext ctx, ITermsService terms, ILogger<TermsEnforcementMiddleware> log)
    {
        var path = ctx.Request.Path.Value?.ToLowerInvariant() ?? "";
        if (ExemptPaths.Any(p => path.StartsWith(p)))
        {
            await _next(ctx);
            return;
        }

        var userId = ctx.User.FindFirst("user_id")?.Value;
        var tenantId = ctx.User.FindFirst("tenant_id")?.Value;
        if (userId is null || tenantId is null)
        {
            await _next(ctx);  // 인증 미들웨어가 처리
            return;
        }

        var consent = await terms.GetConsentAsync(tenantId, userId);
        var current = await terms.GetCurrentVersionAsync();

        var blocked =
            consent is null ||
            consent.ServiceTermsVersion != current.ServiceTermsVersion ||
            consent.PrivacyVersion != current.PrivacyVersion ||
            consent.LocationVersion != current.LocationVersion;

        if (blocked)
        {
            log.LogInformation("Terms consent missing/outdated: tenant={Tenant} user={User}", tenantId, userId);
            ctx.Response.StatusCode = 401;
            ctx.Response.Headers["X-Redirect"] = "/terms/consent";
            await ctx.Response.WriteAsJsonAsync(new {
                error = "terms_consent_required",
                redirect = "/terms/consent",
                current_versions = current
            });
            return;
        }

        await _next(ctx);
    }
}
```

**등록**: `Program.cs`에서 인증 미들웨어 직후, 컨트롤러 라우팅 직전:
```csharp
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TermsEnforcementMiddleware>();   // 신규
app.MapControllers();
```

---

## 4. DB 스키마 (`tenant_terms_consent`)

```sql
CREATE TABLE tenant_terms_consent (
    consent_id              BIGINT AUTO_INCREMENT PRIMARY KEY,
    tenant_id               VARCHAR(50)  NOT NULL,
    user_id                 VARCHAR(50)  NOT NULL,
    service_terms_version   VARCHAR(20)  NOT NULL,
    privacy_version         VARCHAR(20)  NOT NULL,
    location_version        VARCHAR(20)  NOT NULL,
    marketing_opt_in        BOOLEAN      NOT NULL DEFAULT FALSE,
    consented_at            DATETIME(6)  NOT NULL,
    consent_ip              VARCHAR(45)  NOT NULL,
    consent_user_agent      VARCHAR(500) NULL,
    revoked_at              DATETIME(6)  NULL,
    UNIQUE KEY uk_tenant_user (tenant_id, user_id),
    KEY ix_consented_at (consented_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE terms_versions (
    version_id              VARCHAR(20)  PRIMARY KEY,   -- '1.0.0'
    service_terms_version   VARCHAR(20)  NOT NULL,
    privacy_version         VARCHAR(20)  NOT NULL,
    location_version        VARCHAR(20)  NOT NULL,
    marketing_version       VARCHAR(20)  NOT NULL,
    force_reconsent         BOOLEAN      NOT NULL DEFAULT FALSE,
    published_at            DATETIME(6)  NOT NULL,
    is_current              BOOLEAN      NOT NULL DEFAULT FALSE,
    KEY ix_is_current (is_current)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE consent_audit_log (
    audit_id        BIGINT AUTO_INCREMENT PRIMARY KEY,
    tenant_id       VARCHAR(50)  NOT NULL,
    user_id         VARCHAR(50)  NOT NULL,
    action          VARCHAR(30)  NOT NULL,   -- 'consent','revoke','reconsent','blocked'
    version_id      VARCHAR(20)  NULL,
    occurred_at     DATETIME(6)  NOT NULL,
    ip_address      VARCHAR(45)  NOT NULL,
    user_agent      VARCHAR(500) NULL,
    KEY ix_tenant_user (tenant_id, user_id),
    KEY ix_occurred_at (occurred_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```

InnoDB 명시 (헌법 #17). utf8mb4_unicode_ci 통일.

---

## 5. 동의 화면 컴포넌트 영역

위치: `src/HitPan.Web/Pages/Terms/Consent.razor` (신규)

요구사항:
- 4건 약관을 탭 또는 아코디언으로 본문 표시
- 1·2·3은 체크박스 필수, 4는 선택
- "전체 동의" 버튼 + 개별 체크박스
- 동의 시 `POST /api/terms/consent` → 미들웨어 통과
- 동의 후 원래 가려던 페이지로 redirect (returnUrl 보존)

UX 헌법 정합: 사장님 정신 "처음 보는 사람이 혼자 쓸 수 있냐?" — 약관 본문은 평이한 한국어 + 핵심 5줄 요약을 상단 박스에 표시.

---

## 6. 버전 변경 시 재동의 흐름

1. 법무팀장이 약관 v1.0.0 → v1.1.0 게시
2. `terms_versions` 신규 row + `is_current=TRUE`, 이전 row `is_current=FALSE`
3. `force_reconsent=TRUE`일 경우 미들웨어가 모든 사용자 차단
4. 사용자 다음 로그인 시 동의 화면 강제 노출
5. 동의 완료 시 `tenant_terms_consent` UPDATE + `consent_audit_log` INSERT (action='reconsent')

---

## 7. 헌법 #24 (책임 분산) 정합 박제

본사가 가르치는 것:
- 약관 본문 평이한 한국어
- 핵심 5줄 요약
- AI 챗봇이 약관 질문 받음
- 콜센터 약관 안내 매뉴얼

고객이 책임지는 것:
- 동의 후 자기 데이터 백업
- 자식 계정 관리
- 고객 PC 내부 망

본사가 안 받는 것:
- 매출/매입/원장/거래처/직원 (헌법 #18 v3 + #22)
- 동의 자체는 받지만 동의 후 업무 데이터는 본사로 안 옴

---

## 8. 테스트 시나리오 (W5 가도)

- T1: 신규 가입 후 첫 로그인 → 동의 화면 강제
- T2: 동의 완료 → 정상 API 호출
- T3: 약관 버전 변경 + force_reconsent → 재동의 강제
- T4: marketing만 미동의 → 정상 통과 (선택 항목)
- T5: 동의 취소(revoke) → 다음 호출부터 차단
- T6: ExemptPaths 호출 → 미동의 상태에서도 통과
- T7: JWT 없음 → 인증 미들웨어가 처리 (terms 미들웨어 통과)

---

**박제자**: PM 브라운킴
**검증**: 보안 매니저 1 + 법무팀장 + 백엔드 매니저
**상태**: 결재 완료, W4 D4부터 DDL + 미들웨어 코드 박제 가도
