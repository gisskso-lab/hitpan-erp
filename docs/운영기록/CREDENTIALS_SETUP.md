# 히트판 자격증명 박제 가이드 (Credentials Setup)

> 사장님 결재 박제 2026-06-04 — 헌법 #18·#22·#29 정합
> **이 문서는 본사 운영자(사장님)만 보관·갱신. 외부 공개 금지.**

---

## 1. 자격증명 보관 원칙 (헌법 #29)

- **코드에 박제 금지**: 모든 비밀값은 환경변수 또는 운영 서버 `appsettings.Production.json`에만.
- **DEV 기본값은 운영 절대 금지**: `DEV-` 접두사 또는 `dev-pepper-2026` 등 식별 가능한 기본값은 운영 배포 전 반드시 교체.
- **백오피스 헬스체크로 사전 게이트**: `/owner/credentials-status` 페이지에서 박제 여부·DEV 사용 여부 확인 후 배포.
- **응답에 값 자체는 절대 노출 안 됨**: 헬스체크 API는 `present` / `isDev` 불리언만 반환.

---

## 2. 환경변수 목록

| 이름 | 용도 | 미박제 시 영향 |
|---|---|---|
| `HITPAN_JWT_SECRET` | ERP API JWT 서명 키 | 로그인·세션 동작 불가 |
| `HITPAN_BO_JWT_SECRET` | 백오피스 API JWT 서명 키 | 백오피스 로그인 동작 불가 |
| `HITPAN_LICENSE_PEPPER` | 라이선스 키 HMAC-SHA256 pepper | 라이선스 검증 보안 취약 |
| `HITPAN_BIZNO_PEPPER` | 사업자번호 해시 pepper | 사업자번호 해시 보안 취약 |
| `HITPAN_NTS_API_KEY` | 국세청 사업자 진위확인 API 토큰 | 폐업·휴업 거름망 미작동 (체크섬만) |
| `HITPAN_SMTP_HOST` | SMTP 서버 호스트 | 메일 발송 불가 (로그만) |
| `HITPAN_SMTP_PORT` | SMTP 포트 (기본 587) | 메일 발송 불가 |
| `HITPAN_SMTP_USER` | SMTP 인증 ID | 메일 발송 불가 |
| `HITPAN_SMTP_PASS` | SMTP 인증 비밀번호 | 메일 발송 불가 |
| `HITPAN_SMTP_FROM` | 발신 이메일 주소 | 메일 발송 불가 |
| `HITPAN_TOSS_CLIENT_KEY` | 토스페이먼츠 Client Key | 결제 위젯 미작동 |
| `HITPAN_TOSS_SECRET_KEY` | 토스페이먼츠 Secret Key | 결제 승인·취소 미작동 |
| `HITPAN_BOOTSTRAP_TOKEN_KEY` | 백오피스↔ERP 부트스트랩 서명 키 (양쪽 동일) | ERP 첫 설치 자동 반영 불가 |

---

## 3. Windows 환경변수 박제 절차

### 영구 박제 (운영 권장)
```powershell
# 관리자 PowerShell
[Environment]::SetEnvironmentVariable("HITPAN_JWT_SECRET", "여기에-32바이트-이상-랜덤문자열", "Machine")
[Environment]::SetEnvironmentVariable("HITPAN_BO_JWT_SECRET", "여기에-별도-32바이트-이상-랜덤문자열", "Machine")
# … 나머지 환경변수도 동일
```

박제 후 **반드시 `HitPan.API` / `HitPan.Backoffice.API` 서비스 재기동** (헌법 #28). PowerShell 세션만 갱신하면 서비스가 못 읽음.

### 일회용 박제 (개발 PC)
```powershell
setx HITPAN_JWT_SECRET "값"
```

### 랜덤 키 생성 권장
```powershell
# 32바이트 base64
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 } | ForEach-Object { [byte]$_ }))
```

---

## 4. 회전 주기 권고

| 항목 | 주기 | 비고 |
|---|---|---|
| JWT 시크릿 2종 | 분기 1회 | 회전 시 전 사용자 재로그인 필요 |
| Pepper 2종 | 회전 금지 | 회전 시 기존 해시 전체 무효화됨 |
| SMTP 비밀번호 | 월 1회 또는 사고 즉시 | |
| 국세청·토스 토큰 | 발급처 정책 준수 | 만료 알림 미리 박제 |

---

## 5. 헬스체크 사용법

### 화면
- 백오피스 로그인 (Owner 계정) → **자격증명 상태** 메뉴
- 또는 직접 이동: `/owner/credentials-status`
- 상단 두 칩(`전체 박제 완료` / `DEV 기본값 사용 중`) 모두 녹색이어야 운영 배포 가능

### API (자동화·CS 점검용)
```http
GET /api/backoffice/credentials/healthz
Authorization: Bearer {owner-jwt}
```

응답:
```json
{
  "success": true,
  "allPresent": true,
  "anyDev": false,
  "checkedAt": "2026-06-04T12:34:56Z",
  "items": [
    { "name": "JWT (ERP API)", "envName": "HITPAN_JWT_SECRET", "present": true, "isDev": false },
    …
  ]
}
```

**보안**: 값 자체는 절대 응답에 포함되지 않음. `present` / `isDev` 불리언만.

---

## 6.1 부트스트랩 토큰 키 (W2 객체 완전 분리)

헌법 #35 정합 — 백오피스가 ERP 첫 설치용 서명 토큰을 발급, ERP는 동일 키로 검증.

1. 32바이트 이상 랜덤 키 생성 (PowerShell)
   ```powershell
   [Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 } | ForEach-Object { [byte]$_ }))
   ```
2. **백오피스 API 서버** 환경변수 `HITPAN_BOOTSTRAP_TOKEN_KEY` 박제
3. **ERP API 서버 (고객사 PC 또는 본사 데모)** 환경변수 `HITPAN_BOOTSTRAP_TOKEN_KEY` **동일 값** 박제
4. 두 서버 재기동 (헌법 #28)
5. 백오피스 헬스체크 확인 → ERP `/setup/license` 흐름 실측

**보안**:
- 양쪽 서버가 같은 비밀 보유 = 대칭 키. 한쪽 유출 시 양쪽 동시 회전 필수
- 토큰 자체엔 만료 10분, 1회용 jti, audience 검증
- 회전 주기 분기 1회

---

## 6. 토스페이먼츠 자격증명 발급

1. https://app.tosspayments.com 접속 → 사장님 계정 로그인
2. **상점 관리 → API 키** 메뉴
3. **Client Key** 복사 → `HITPAN_TOSS_CLIENT_KEY` 환경변수 박제
4. **Secret Key** 복사 → `HITPAN_TOSS_SECRET_KEY` 환경변수 박제
5. `HitPan.Backoffice.API` 서비스 재기동
6. 백오피스 헬스체크에서 두 키 박제·DEV 표식 없음 확인
7. 랜딩 `/payment` 페이지 접속 → "결제 시스템 점검 중" 배너 사라짐 확인

**보안 절대 원칙**:
- Secret Key는 서버 환경변수만, 코드·git·로그·응답 0건
- 결제 박제 시 본사 DB 보관 = `orderId · paymentKey · amount · method · approvedAt`만 (카드번호·CVC·CVV 0건, 헌법 #22)
- 토스 대시보드에서 Secret Key 노출 시 즉시 회전

---

## 7. 운영 배포 전 체크리스트

- [ ] 위 표 12개 환경변수 모두 박제됨 (`allPresent: true`)
- [ ] DEV 기본값 0건 (`anyDev: false`)
- [ ] SMTP 테스트 메일 실측 1건 도착 확인
- [ ] 국세청 API 토큰으로 임의 사업자번호 1건 진위확인 정상 응답
- [ ] HitPan.API · HitPan.Backoffice.API 서비스 재기동 후 헬스체크 재실행

체크리스트 1건이라도 실패 시 운영 배포 금지 (헌법 #19·#23·#25 정합).
