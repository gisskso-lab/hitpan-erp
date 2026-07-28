# 2026-06-04 운영 배포 체크리스트 (11개 차수 통합)

> 헌법 #29 정합 — DB CREATE·환경변수·서비스 재기동은 사장님 영역. PM은 가이드만.
> 본 가이드는 W1·W2·W5 + C·E·F·G·H·N·O·Q 11개 차수의 운영 박제 잔여를 1페이지로 정리.

---

## 1. 사전 점검

- [ ] 백오피스 API 서버 (`HitPan.Backoffice.API`, 포트 5258) 위치 확인
- [ ] ERP API 서버 (`HitPan.API`, 포트 5257) 위치 확인
- [ ] 백오피스 Web (`HitPan.Backoffice`) 실행 환경 확인
- [ ] 랜딩 Web (`HitPan.Landing`) 실행 환경 확인
- [ ] MariaDB 11.4.10 접속 가능 (hitpan / Hitpan2025!)

---

## 2. DB 마이그 실행 (헌법 #29 — 사장님 직접)

**순서 절대 — 1번부터 4번까지 차례로**:

### 2-1. ERP 로컬 회사정보 마스터
```sql
SOURCE db/migrations/20260604_erp_local_company.sql;
```
- `hitpan_erp.local_company` 테이블 생성
- 기존 `tenants`에서 회사정보 컬럼만 이관
- 검증: `SELECT COUNT(*) FROM local_company;` = 기존 `tenants` 건수

### 2-2. ERP 로컬 본사 영역 캐시
```sql
SOURCE db/migrations/20260604_erp_local_subscription.sql;
```
- `hitpan_erp.local_subscription` 테이블 생성
- 기존 `tenants`에서 본사 영역 컬럼(구독·AI·기기·대리점)만 이관

### 2-3. 백오피스 DB 분리
```sql
SOURCE db/migrations/20260604_backoffice_db_split.sql;
```
- `hitpan_backoffice` DB 생성 + 6개 테이블 이관
  - `landing_signups · tenants · tenant_payments · resellers · reseller_applications · bo_permissions`
- 검증 쿼리 (주석) 사용해 두 DB 카운트 일치 확인

### 2-4. 백오피스 자체 인증
```sql
SOURCE db/migrations/20260604_bo_users.sql;
```
- `hitpan_backoffice.bo_users` 테이블 생성
- Owner 시드 INSERT는 사장님 BCrypt 박제 후 (가이드는 SQL 파일 주석 참고)

### 2-5. (선택) 결제 메타 (이전 차수에서 박제)
```sql
SOURCE db/migrations/20260604_tenant_payments.sql;
```

---

## 3. 환경변수 박제 (헌법 #29 — Windows 시스템 영역)

관리자 PowerShell:
```powershell
[Environment]::SetEnvironmentVariable("HITPAN_JWT_SECRET", "32바이트랜덤", "Machine")
[Environment]::SetEnvironmentVariable("HITPAN_BO_JWT_SECRET", "별도32바이트랜덤", "Machine")
[Environment]::SetEnvironmentVariable("HITPAN_LICENSE_PEPPER", "운영용", "Machine")
[Environment]::SetEnvironmentVariable("HITPAN_BIZNO_PEPPER", "운영용", "Machine")
[Environment]::SetEnvironmentVariable("HITPAN_BOOTSTRAP_TOKEN_KEY", "양쪽-동일-32바이트", "Machine")
[Environment]::SetEnvironmentVariable("HITPAN_NTS_API_KEY", "국세청-발급-토큰", "Machine")
[Environment]::SetEnvironmentVariable("HITPAN_SMTP_HOST", "smtp.example.com", "Machine")
[Environment]::SetEnvironmentVariable("HITPAN_SMTP_PORT", "587", "Machine")
[Environment]::SetEnvironmentVariable("HITPAN_SMTP_USER", "no-reply@hitpan.kr", "Machine")
[Environment]::SetEnvironmentVariable("HITPAN_SMTP_PASS", "비밀번호", "Machine")
[Environment]::SetEnvironmentVariable("HITPAN_SMTP_FROM", "no-reply@hitpan.kr", "Machine")
[Environment]::SetEnvironmentVariable("HITPAN_TOSS_CLIENT_KEY", "토스-Client", "Machine")
[Environment]::SetEnvironmentVariable("HITPAN_TOSS_SECRET_KEY", "토스-Secret", "Machine")
```

**중요**:
- `HITPAN_BOOTSTRAP_TOKEN_KEY`: 백오피스 API + ERP API 양쪽 서버에 **동일 값**
- `HITPAN_LICENSE_PEPPER` / `HITPAN_BIZNO_PEPPER`: 회전 금지 (기존 해시 전체 무효화)
- DEV 기본값(`DEV-` 접두사·`dev-pepper-2026`) 절대 운영 금지

랜덤 키 생성:
```powershell
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 } | ForEach-Object { [byte]$_ }))
```

---

## 4. 서비스 재기동 (헌법 #28)

```powershell
# 환경변수 박제 후 반드시 두 서비스 재기동
Restart-Service HitPanApi
Restart-Service HitPanBackofficeApi
```
서비스 미박제 시 일반 프로세스로 재시작.

---

## 5. 헬스체크 (사장님 영역)

### 5-1. 백오피스 자격증명 헬스체크
- 백오피스 로그인 → Owner 메뉴 → 자격증명 상태
- URL: `/owner/credentials-status`
- 확인:
  - [ ] 환경변수 헬스체크: **전체 박제 완료** 칩 녹색
  - [ ] **DEV 기본값 사용 중** 칩 사라짐
  - [ ] SMTP 테스트 메일 발송 → 사장님 메일함 1건 도착

### 5-2. 랜딩 가입 흐름 실측
- [ ] `/signup` 접속 → DEMO 모드 자동 박제 확인
- [ ] `/payment` 접속 → 토스 자격증명 박제 시 "결제 시스템 점검 중" 배너 사라짐
- [ ] `/recover` → 안내 화면 정합

### 5-3. 백오피스 V2 화면 실측
- [ ] `/admin/tenants` 목록 → 코드·회사명 클릭 → 상세 페이지 진입
- [ ] `/admin/resellers` 목록 → 클릭 → 상세 → 영업 고객사 클릭 → 고객사 상세 (양방향)
- [ ] `/admin/reseller-applications` 신청 검토 → 회사명 클릭 → 상세 다이얼로그

---

## 6. 차수별 봉합 요약

| 차수 | 영역 | 핵심 |
|---|---|---|
| W1 | DB 분리 | `hitpan_backoffice` 신설, ERP 로컬 `local_company`·`local_subscription` |
| W2 | 객체 분리 토큰 | HMAC-SHA256 부트스트랩 토큰, ERP는 백오피스 URL 의존 0 |
| W5 | 백오피스 인증 | `bo_users` + BaseUrl 5258 정정 |
| C | 좀비 봉합 | BackofficeService 14개 stub, 좀비 화면 7개 삭제 |
| E | V2 상세 | 고객사 상세 페이지 + payments 메타 |
| F | V2 상세 | 협력업체 상세 페이지 + 영업 고객사 |
| G | 신청 보강 | 신청 상세 다이얼로그 + 검색 |
| H | Owner 안내 | System Config·Platform Users → W11 안내 모드 |
| N | 로그인 보강 | 비밀번호 재설정 안내 |
| O | 진범 봉합 | PaymentPage 빈 catch + silent fail |
| Q | 진범 봉합 | ResellerSignupPage 폐기·리다이렉트, RecoveryPage 404 봉합 |

---

## 7. 잔여 차수 (별도)

- **W9**: 백오피스 ResellerController·RlsService·Settlement·Watchdog·시리얼 발급 신설
- **W10**: 백오피스 → ERP webhook 동기화 (구독·기기 슬롯 변경 즉시 반영)
- **W11**: Owner 영역 백엔드 (`bo_users` CRUD + 4-eyes + 감사로그 + MFA)

---

## 8. 사장님 운영 박제 흐름 (5분 체크리스트)

1. **DB 마이그 4건** — 순서대로 실행 (§2)
2. **환경변수 13개** — Machine 영역 박제 (§3)
3. **양쪽 서버 재기동** (§4)
4. **백오피스 Owner 헬스체크 녹색 확인** (§5-1)
5. **백오피스 로그인 → 고객사 목록 클릭 폭발 0건 확인** (§5-3)

5건 모두 통과 = 11개 차수 운영 활성화 완료.

---

**검증 실패 시**: 본 가이드 §2-§5 순서 다시 점검, 실패 항목 PM에게 보고.
