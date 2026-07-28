# 사장님 8명제 — 백오피스·랜딩페이지 보안 아키텍처

> 발행: 2026-06-01 / 결재: 사장님 모두결재 / 검증: 매니저 9명 하브루타 만장일치 PASS

---

## 0. 사장님 큰 그림

> *"2개 축 (랜딩·백오피스) 먼저 완성 → 3개 축(ERP까지 포함) 비교하며 잔여 보완"*

- 현재 우선순위 = 트랙 C (랜딩+백오피스 8명제) 즉시 발진
- 트랙 B (ERP 잔여 3건: AI 챗봇·기기등록·전자세금계산서) = 2개 축 완성 후 별도 트랙

---

## 1. 사장님 8명제

| # | 명제 | 영역 |
|---|---|---|
| 1 | 사업자등록증 = KYC (1회 검증 후 즉시 폐기, 해시·검증결과만 보유) | 인증 |
| 2 | 시리얼 = 본사 발급 (HP-YYMM-XXXXXXXX-CRC + 대리점 HR-) | 인증 |
| 3 | 백오피스 평문 0 + 본사 ERP 평문 보유 (물리 분리) | 데이터 |
| 4 | 계정 = 본사 발급 + 이메일·SMS 2채널 + 첫 로그인 강제 변경 | 계정 |
| 5 | 분실 복구 = 사등 재업로드 + 랜딩 2버튼 UI | 계정 |
| 6 | 대리점 = 동일 패턴 + RLS 3조건 (자기 영업분만) | 권한 |
| 7 | 본사 = 시리얼 단위 전권한 + JIT 복호화 (CS 6건만) | 권한 |
| 8 | 생애주기 = 휴폐업·대표자 변경·사업자 양도 흐름 표준화 | 생애주기 |

---

## 2. 매니저 9명 하브루타 결과

### 9명 만장일치 결론
**8명제 골격 견고. P0 7건 + P1 4건 봉합 후 발진.**

### 9명 핵심 함정 (트랙 C에 흡수)

**A축 보안 인프라:** OCR 온프레미스 강제(PaddleOCR/Tesseract) · 사장님 PC SPOF (HSM 2개 + Owner 2/2) · 발급키 HSM · 백업 E2E · 사장님 ERP 망분리

**B축 데이터 정합:** biz_no_hash HMAC + pepper HSM · 평문 0 재정의 (업무데이터 0 + 연락처 envelope 암호화) · 본사 ERP ↔ 백오피스 단방향 메시지 큐(Outbox) · 시리얼 멱등성(idempotency_key 5분) · 헌법 #16 SMS·이메일 독립 connection

**C축 운영·법령:** 세금계산서 발행 평문 → **트랙 B 메이크빌 외주로 이관** · CS 10건 중 6건 JIT 복호화 · 생애주기 9명제 확장 · 신입 3분 룰(검색 평문/화면 마스킹/복사 차단) · 법령 평문 5년 → 본사 ERP가 충족

**D축 영업·마케팅:** 사등 업로드 이탈률 → "느린 가입, 평생 안전" · 대리점 동기 → 메타 대시보드 + 갱신 수수료 인상 + JIT CS 토큰 · 시리얼 발급 SLA 1시간 + 24/7 폴백 · "본사도 못 보는 ERP" 카피 법무 검토

**E축 UX·디자인·프론트:** 그린 64%/오렌지 36% 비대칭 · 시리얼 우편 → QR + 홀로그램 · 3계층 색상(Navy·Teal·Gray) · JWT 클레임 SSR 타이밍 → PersistentComponentState · 시리얼 입력창 자동 하이픈·CRC

---

## 3. 데이터 흐름

### 신규 가입
```
[랜딩 신규등록 버튼]
  → 이메일 + 휴대폰 + 약관 4종 + 사업자등록증 업로드
  → TLS 1.3 + Cloudflare WAF
[본사 메모리 OCR] (PaddleOCR/Tesseract 온프레미스)
  → 사업자번호 추출
[국세청 진위확인 API 1회]
  → True: 결제 / False: 거부 + 메모리 폐기
[결제] (토스 SDK 고객 PC 직접)
[본사 어드민 검토] (백오피스 발급 화면, 2인 결재)
[시리얼·임시비번 발급]
  → Argon2id 해시 → DB 박제
  → 이메일·SMS 2채널 전송 (독립 connection)
  → 메모리 평문 즉시 폐기 (0초)
[고객 활성화]
  → 시리얼 + 임시 비번 입력 → 강제 변경 → ERP 진입
```

### 분실 복구
```
[랜딩 계정분실/문의 버튼]
  → 이메일 또는 휴대폰 + 사업자등록증 재업로드
[메모리 OCR + 국세청 진위확인]
[biz_no_hash 매칭]
  → tenants 매칭 = 고객사 / resellers 매칭 = 대리점
[본사 어드민 검토 + 발급]
  → 동일 화면 재사용 (가입 발급 화면)
[2채널 재전송 + 첫 로그인 강제 변경 + 기존 세션 전체 무효화]
```

---

## 4. DB 컬럼 (백오피스, 평문 0)

```sql
CREATE TABLE tenants (
  serial VARCHAR(24) PRIMARY KEY,
  email_hash CHAR(64) NOT NULL,
  password_hash VARCHAR(255) NOT NULL,
  biz_no_hash CHAR(64) NOT NULL UNIQUE,
  verified BOOLEAN NOT NULL,
  verified_at DATETIME NOT NULL,
  ntsapi_raw_hash CHAR(64),
  subscription_tier ENUM('basic','pro','enterprise'),
  payment_status ENUM('active','pending','expired','suspended'),
  activation_status ENUM('pending','active','suspended','revoked'),
  reseller_serial VARCHAR(24),
  created_at DATETIME,
  expires_at DATETIME,
  ip VARCHAR(45),
  device_fingerprint VARCHAR(64),
  INDEX idx_reseller (reseller_serial)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE resellers (
  serial VARCHAR(24) PRIMARY KEY,
  email_hash CHAR(64) NOT NULL,
  password_hash VARCHAR(255) NOT NULL,
  biz_no_hash CHAR(64) NOT NULL UNIQUE,
  verified_at DATETIME NOT NULL,
  ntsapi_raw_hash CHAR(64),
  reseller_type ENUM('individual','corp'),
  contract_status ENUM('active','expired','terminated'),
  commission_rate DECIMAL(5,2),
  activation_status ENUM('pending','active','suspended','revoked'),
  created_at DATETIME
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE platform_users (
  user_id BIGINT AUTO_INCREMENT PRIMARY KEY,
  email_hash CHAR(64) NOT NULL UNIQUE,
  password_hash VARCHAR(255) NOT NULL,
  role ENUM('owner','platform_manager','platform_staff'),
  full_name VARCHAR(50),
  status ENUM('active','suspended','revoked'),
  mfa_enabled BOOLEAN DEFAULT TRUE,
  ip_whitelist TEXT,
  created_at DATETIME,
  created_by BIGINT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE platform_audit_log (
  audit_id BIGINT AUTO_INCREMENT PRIMARY KEY,
  user_id BIGINT NOT NULL,
  action VARCHAR(100),
  target_serial VARCHAR(24),
  details JSON,
  ip VARCHAR(45),
  requested_at DATETIME,
  INDEX idx_user_time (user_id, requested_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE recovery_log (
  recovery_id BIGINT AUTO_INCREMENT PRIMARY KEY,
  serial VARCHAR(24) NOT NULL,
  serial_type ENUM('tenant','reseller'),
  channel_email BOOLEAN,
  channel_sms BOOLEAN,
  admin_user_id BIGINT,
  ip VARCHAR(45),
  device_fingerprint VARCHAR(64),
  requested_at DATETIME,
  completed_at DATETIME
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```

---

## 5. 권한 4계층

| Layer | Role | 권한 |
|---|---|---|
| 0 | Owner (사장님) | 전권한 + 시스템 설정 + 헌법 변경 + MFA |
| 1 | Platform Manager (운영팀장) | 시리얼 발급·환불·대리점 등록 (2인 결재) + 전 시리얼 조회 |
| 2 | Platform Staff (운영팀 직원) | CS·결제 처리 + 전 시리얼 조회 |
| 3 | Reseller (대리점) | 자기 영업 고객사·CS·실적만 (RLS) |

**공통:** 백오피스에서 평문 사업자 정보 0건 (본사 ERP 경유 + JIT 복호화)

---

## 6. 작업지시서 11종

| # | 작지 | 영역 |
|---|---|---|
| WS-20260601-12 | 랜딩 2버튼 UI + 사등 업로드 + OCR 온프레미스 | 랜딩 |
| WS-20260601-13 | 본사 백오피스 시리얼 발급 화면 (4-eyes) | 백오피스 |
| WS-20260601-14 | 이메일·SMS 2채널 인프라 (독립 connection, 헌법 #16) | 인프라 |
| WS-20260601-15 | 백오피스 DB 컬럼 (평문 0, biz_no_hash HMAC+pepper HSM) | DB |
| WS-20260601-16 | JWT 4계층 권한 정책 + Authorization Policy | 백엔드 |
| WS-20260601-17 | 대리점 RLS API + 화면 3건 (Dashboard·TenantList·CsList) | 백오피스 |
| WS-20260601-18 | 본사 Owner/Manager/Staff 전권한 화면 6건 | 백오피스 |
| WS-20260601-19 | 생애주기 흐름 (휴폐업·대표자 변경·양도) | 백오피스 + 본사 ERP |
| WS-20260601-20 | 본사 ERP ↔ 백오피스 단방향 Outbox 메시지 큐 | 백엔드 |
| WS-20260601-21 | 사장님 PC SPOF 봉합 (HSM 2개 + Owner 2/2) | 보안 |
| WS-20260601-22 | 3시스템 통합 E2E (계정·사업자정보 영역만) | 테스트 |

---

## 7. 발진 순서

**Week 1:** WS-15 (DB) + WS-16 (JWT) + WS-12 (랜딩 2버튼)
**Week 2:** WS-13 (백오피스 발급) + WS-14 (2채널) + WS-20 (Outbox)
**Week 3:** WS-17 (대리점 RLS) + WS-18 (본사 전권한)
**Week 4:** WS-19 (생애주기) + WS-21 (SPOF) + WS-22 (E2E)

---

## 8. 헌법 정합

| 헌법 | 정합 |
|---|---|
| #2 tenant_id JWT 클레임만 | 대리점 reseller_serial도 동일 (파라미터 금지) |
| #11 권한 어드민 직접 설정 | Owner가 본사 직원 권한 부여 |
| #16 MySqlConnection thread-safe | 이메일·SMS 2채널 독립 connection |
| #17 InnoDB 명시 | 5개 신규 테이블 모두 |
| #18 본사로 업무 데이터 전송 금지 | 백오피스 평문 0 정합 |
| #22 데이터 최소주의 | 백오피스 평문·암호화 컬럼 0 |
| #23 5중 검증 | 백오피스 = 분기 1회 DAST + 주요 변경마다 |
| #24 책임 분산 + 가르침 | Owner-Manager-Staff-Reseller 명확 분리 + AI CS 챗봇(트랙 B) |
| #25 3대 원칙 | 쉽게(2버튼) + 정확하게(KYC 4중) + 안전하게(평문 0) |
| #29 인프라 사전결재 | 본사 어드민 2인 결재 + 사장님 결재 |
| #32 점수 받아쓰기 금지 | 9명 하브루타 함정 박제 정직 보고 |

---

## 9. 다음 단계

1. 트랙 C (이 문서) 발진 → 작지 11종 매니저 11명 병렬 박제
2. 2개 축 완성 (랜딩 + 백오피스) → 사장님 검수
3. 3개 축 (ERP까지) 비교 검증
4. ERP 잔여 3건 (트랙 B): AI 챗봇 + 기기등록 + 전자세금계산서 (메이크빌 외주)
5. 통합 검증 + 베타 출시
