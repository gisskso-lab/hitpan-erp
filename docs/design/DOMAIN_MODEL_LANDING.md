# 랜딩페이지 도메인 모델 (P0 6 영역)

> 작성일: 2026-05-26
> 작성: 설계팀장 브라운킴 + 본부장 듀얼 리딩
> 정합: W1 백오피스 도메인 모델 + SEQUENCE_3SYSTEMS + STATE_MACHINE_SUBSCRIPTION
> 헌법: #4·#17·#18 v3·#22·#25 정합

---

## 0. 랜딩 시스템 위치

### 0.1 3시스템 헌법 (2026-05-06 확정)
- **랜딩페이지**: 가입·결제·다운로드만
- **백오피스**: 프로비저닝·고객관리·대리점관리 전담
- **히트판 ERP**: 업무 처리만

### 0.2 랜딩 책임 영역 (헌법 #18 v3 정합)
- 보유 데이터: 가입 신청 단계 메타정보(사업자번호·연락처·이메일·결제 토큰)
- 보유 금지: 업무 데이터·자식계정 비밀번호·카드 원본·주민번호·계좌번호 원본
- 데이터 흐름: 가입 확정 즉시 백오피스 Push → 랜딩 DB 30일 보관 → 폐기

---

## P0-L1: 메인 랜딩

### 1.1 도메인 엔티티

#### LandingPage (정적 콘텐츠)
- `hero_headline` (string, max 60자)
- `hero_subheadline` (string, max 120자)
- `hero_cta_label` (string, "베타 신청" / "가입하기")
- `hero_video_url` (string, 3초 데모)
- `feature_blocks[]` (FeatureBlock 6개)
- `comparison_table` (ComparisonTable)
- `pricing_summary[]` (PricingSummary 3건)

#### FeatureBlock
- `feature_id` (PK)
- `title`, `description`, `icon_url`
- `category` ("통합 캘린더" / "전자세금계산서" / "5중 보안" / "BOM" / "통합 워치독" / "AI CS")
- `display_order` (int)

#### ComparisonTable (더존·이카운트 비교)
- `row_id` (PK)
- `category` (string, "월 비용" / "전자세금계산서" / "BOM" 등)
- `hitpan_value`, `douzone_value`, `ecount_value`
- `is_highlight` (bool, 히트판 우위)

#### CTAEvent (방문→CTA 클릭 로그)
- `event_id` (PK, UUID v7)
- `visitor_session_id` (FK)
- `cta_label`, `clicked_at`
- `utm_source`, `utm_medium`, `utm_campaign`
- 보관: 90일

### 1.2 셀링 포인트 3종 (사장님 헌법 #25 "쉽게")
1. **3초 헤드라인**: "더존·이카운트 보다 쉽고, 5분 안에 가도"
2. **현장 영상**: 30초 데모 (가입→설치→첫 매입 등록)
3. **비교표**: 6 항목 (월 비용·BOM·전자세금계산서·통합 보안·AI CS·마이그)

### 1.3 비즈니스 규칙
- 비회원 방문 추적: 쿠키 동의 후만 (GDPR·정보통신망법)
- 방문 데이터: 본사 보유 0, Cloudflare Analytics 위임 (헌법 #22)

---

## P0-L2: 가격 페이지

### 2.1 도메인 엔티티

#### PricingTier
- `tier_id` (PK)
- `tier_code` (enum: LITE / STANDARD / PRO)
- `tier_name` ("라이트" / "스탠다드" / "프로")
- `monthly_price` (decimal, 29000 / 59000 / 100000)  ← 헌법 #4
- `annual_price` (decimal, 10개월값)
- `device_count_max` (int, 1 / 3 / 10)
- `description` (string)
- `is_beta_special` (bool)

#### TierFeatureMatrix
- `matrix_id` (PK)
- `tier_id` (FK)
- `feature_key` (enum: BOM / E_TAX / AI_CS / MULTI_DEVICE / BACKUP_AUTO / WATCHDOG)
- `is_included` (bool)
- `limit_value` (string, "BOM 100건/월" 등)

#### BetaSpecialOffer
- `offer_id` (PK)
- `target_count` (int, 30)
- `applied_count` (int, 실시간 카운터)
- `discount_percent` (decimal, 50)
- `valid_until` (datetime)

#### PricingFAQ
- `faq_id` (PK)
- `question`, `answer`
- `category` (enum: PAYMENT / REFUND / UPGRADE / CANCEL)
- `display_order`

### 2.2 비즈니스 규칙
- 가격은 decimal (헌법 #4)
- 베타 30곳: applied_count >= target_count 시 마감
- 연간 결제: 10개월값 (2개월 무료)
- 도중 업그레이드: 일할 차액 계산 → 결제 API 위임

---

## P0-L3: 베타 30곳 모집

### 3.1 도메인 엔티티

#### BetaApplication
- `application_id` (PK, UUID v7)
- `company_name`, `business_number` (사업자번호, 검증 의무)
- `representative_name`, `representative_phone`, `representative_email`
- `industry_category` (enum: 서비스업 / 도소매 / 제조 / 기타)
- `expected_device_count` (int)
- `pain_points` (text, 현재 사용 시스템·불만)
- `applied_at` (datetime)
- `status` (enum: PENDING / APPROVED / REJECTED / CANCELLED)
- `approved_by` (FK admin_users, nullable)
- `approved_at` (nullable)
- `rejection_reason` (nullable)

#### BetaCounter (실시간 카운터)
- `counter_id` (PK)
- `target_count` (int, 30)
- `applied_count`, `approved_count`
- `last_updated_at`
- 갱신 주기: 10초 (Cloudflare Cache TTL)

#### BetaWaitlist (마감 후 대기)
- `waitlist_id` (PK)
- `business_number`, `company_name`, `email`
- `priority_score` (int, 산업 적합도)
- `created_at`

### 3.2 상태 전이
```
PENDING ──(영업 확인)──▶ APPROVED ──(가입 가도)──▶ (tenants 생성)
   │                          │
   └─(중복·자격 미달)──▶ REJECTED
   │
   └─(고객 취소)─────────▶ CANCELLED
```

### 3.3 비즈니스 규칙
- 사업자번호 중복: 1 사업자번호 = 1 신청
- 영업팀장 검토 후 APPROVED
- 마감 임박 알림: 잔여 5곳 이하 시 빨간색 배지

---

## P0-L4: 가입 폼

### 4.1 도메인 엔티티

#### SignupSession
- `session_id` (PK, UUID v7)
- `step` (enum: BUSINESS_INFO / EMAIL_VERIFY / PHONE_VERIFY / TERMS / COMPLETE)
- `business_number`, `company_name`
- `representative_name`
- `email`, `email_verified_at`, `email_otp_hash`, `email_otp_expires_at`
- `phone`, `phone_verified_at`, `phone_provider` (enum: PASS / KAKAO)
- `terms_accepted_at`
- `created_at`, `expires_at` (24시간)

#### BusinessLicenseUpload
- `upload_id` (PK)
- `session_id` (FK)
- `file_path` (R2 / S3 경로, 본사 보유 0 — Cloudflare R2 위임)
- `file_hash` (SHA-256)
- `ocr_extracted_business_number` (검증용)
- `uploaded_at`
- 보관: 가입 확정 후 90일 → 폐기

#### EmailOTP
- `otp_id` (PK)
- `session_id` (FK)
- `otp_code_hash` (bcrypt)
- `attempts` (int, max 5)
- `expires_at` (10분)
- `verified_at` (nullable)

#### PhoneVerification (PASS / 카카오 콜백)
- `verification_id` (PK)
- `session_id` (FK)
- `provider` (enum: PASS / KAKAO)
- `provider_token` (외부 토큰)
- `ci` (연계정보, AES-256 암호화) — 헌법 #25 안전하게
- `verified_at`

#### TermsConsent
- `consent_id` (PK)
- `session_id` (FK)
- `terms_version` (string, "v1.0.0-20260526")
- `consents` (JSON):
  - `service_terms` (bool, 필수)
  - `privacy_policy` (bool, 필수)
  - `payment_terms` (bool, 필수)
  - `data_handling` (bool, 필수)
  - `marketing` (bool, 선택)
- `client_ip`, `user_agent`, `consented_at`

### 4.2 비즈니스 규칙
- 사업자등록증 OCR로 사업자번호 추출 → 입력값과 대조
- 이메일 OTP: 10분 만료, 최대 5회 시도
- 휴대폰: PASS / 카카오 양자택일 (공인인증서 없음, 헌법 정합)
- 약관 필수 4건 미동의 시 가도 불가
- 동의 일시·IP·약관 버전 DB 기록 의무

---

## P0-L5: 결제 페이지

### 5.1 도메인 엔티티

#### PaymentIntent
- `intent_id` (PK, UUID v7)
- `session_id` (FK signup_sessions)
- `tier_id` (FK pricing_tiers)
- `billing_period` (enum: MONTHLY / ANNUAL)
- `amount` (decimal) ← 헌법 #4
- `currency` ("KRW")
- `payment_method_type` (enum: CARD / BANK_TRANSFER / TAX_INVOICE)
- `provider` (enum: TOSS / KCP / MOCK)
- `provider_intent_id` (string, 외부 인텐트 ID)
- `status` (enum: PENDING / APPROVED / FAILED / CANCELLED)
- `created_at`, `expires_at` (30분)

#### PaymentConfirmation
- `confirmation_id` (PK)
- `intent_id` (FK)
- `provider_payment_id` (외부 결제 ID)
- `approval_token` (string)
- `card_token` (string, 토큰만 — 카드 원본 0, 헌법 #22)
- `card_last4` (string, 마지막 4자리만 표시용)
- `paid_at`
- `receipt_url` (외부 URL, 본사 보유 0)

#### MockPaymentLog (개발 단계)
- `mock_id` (PK)
- `intent_id` (FK)
- `simulated_result` (enum: SUCCESS / FAIL / TIMEOUT)
- `created_at`
- 7월 토스 실연결 후 폐기

### 5.2 비즈니스 규칙
- Mock 모드: appsettings `PaymentProvider=Mock` 시 즉시 SUCCESS 반환
- 토스 위젯 인터페이스: 7월 실 연결, 위젯 v2 (헌법 결재 5/12)
- B2B 세금계산서 결제: KCP 어댑터 (메이크빌·이세로 연동)
- 카드 원본·CVC: 본사 DB 0건. 토큰만 보관 (헌법 #22)
- 영수증 URL: 결제사 위임, 본사 보유 0

### 5.3 상태 전이 (STATE_MACHINE_SUBSCRIPTION 정합)
```
PENDING ──(provider 승인)──▶ APPROVED ──▶ (subscriptions 생성, 백오피스 Push)
   │
   ├─(provider 실패)──▶ FAILED ──▶ (재시도 1회 허용)
   │
   └─(30분 만료)──▶ CANCELLED
```

---

## P0-L6: 다운로드 페이지

### 6.1 도메인 엔티티

#### LicenseKey
- `license_id` (PK, UUID v7)
- `tenant_id` (FK tenants — 백오피스 마스터 참조 ID만)
- `license_token` (string, 64자 랜덤, AES-256 암호화)
- `tier_id` (FK)
- `device_count_max` (int)
- `issued_at`, `expires_at`
- `revoked_at` (nullable)
- `download_count` (int)

#### InstallerDownload
- `download_id` (PK)
- `license_id` (FK)
- `installer_version` (string, "v1.2.0")
- `download_url` (string, Cloudflare R2 사전 서명 URL, 1시간 유효)
- `client_ip`, `user_agent`
- `started_at`, `completed_at`
- `file_hash_verified` (bool)

#### InstallerArtifact
- `artifact_id` (PK)
- `version` (string)
- `file_path` (R2 경로)
- `file_size` (long)
- `sha256_hash` (string)
- `signed_at` (코드 서명 일시)
- `released_at`
- `is_active` (bool, 최신본만 true)

#### InstallationGuide
- `guide_id` (PK)
- `step_no` (int)
- `title`, `description`, `screenshot_url`
- `video_url` (nullable)
- `level` (enum: BASIC / TROUBLESHOOTING)

### 6.2 비즈니스 규칙
- 라이선스 키 발급: 결제 확정 직후 + 백오피스 Push 성공 후
- EXE 다운로드 URL: 1시간 만료, 토큰 1회용
- 사장님 매뉴얼: "초등학생도 따라하는" — 스크린샷 + 30초 영상 단계마다
- 첫 로그인 안내: 라이선스 키 입력 → MariaDB 자동 설치 → 약관 동의 → 첫 화면

### 6.3 통신 무결성 정합 (헌법 #27·#28·#30)
- 설치 EXE는 cloudflared·MariaDB·dotnet 자동 등록
- Defender·백신 5종 예외 자동 등록 (헌법 #31)
- 워치독 자동 가도 → 본사 메타 ping만 (헌법 #30)

---

## 영역별 핵심 요약

| P0 영역 | 핵심 엔티티 | 헌법 정합 | 사장님 결재 영역 |
|---|---|---|---|
| L1 메인 | LandingPage·FeatureBlock·ComparisonTable | #22·#25 | 비교표 6항목 확정 |
| L2 가격 | PricingTier·TierFeatureMatrix·BetaSpecialOffer | #4·#22 | 베타 30곳 50% 할인율 확정 |
| L3 베타 | BetaApplication·BetaCounter·BetaWaitlist | #18 v3 | 마감 후 대기 정책 |
| L4 가입 | SignupSession·BusinessLicenseUpload·TermsConsent | #18 v3·#22·#25 | 약관 v1.0.0 본문 확정 |
| L5 결제 | PaymentIntent·PaymentConfirmation·MockPaymentLog | #4·#22·#25 | 토스 7월 실연결 일정 |
| L6 다운로드 | LicenseKey·InstallerDownload·InstallerArtifact | #22·#27~#31 | 매뉴얼 영상 30종 발주 |

---

## W3 가도 예고
- 랜딩 시퀀스 다이어그램 (가입→결제→다운로드)
- 랜딩 API 명세 (이미 본 차수에서 박제 → 후속 차수 보강)
- 랜딩 UI/UX 와이어프레임 (수석 웹디자이너)
- 약관 v1.0.0 본문 4건 (법무팀장)
