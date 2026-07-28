# 랜딩페이지 API 명세

> 작성일: 2026-05-26
> 작성: 백엔드 매니저 + 설계팀장 브라운킴
> 정합: DOMAIN_MODEL_LANDING.md / SEQUENCE_3SYSTEMS.md / PAYMENT_INTERFACE.md
> 헌법: #4·#18 v3·#22·#25

---

## 0. 공통 규약

### 0.1 베이스 URL
- 운영: `https://www.hitpan.app/api/landing`
- 베타: `https://beta.hitpan.app/api/landing`

### 0.2 인증
- 가입 가도 영역: 비인증 (rate limit 강제)
- 다운로드 영역: `license_token` 일회용 토큰

### 0.3 Rate Limit
- IP별 분당 60회
- 가입 폼 영역: IP+사업자번호별 시간당 5회
- OTP 발송: 이메일/휴대폰별 시간당 3회

### 0.4 응답 표준
```json
{
  "success": true,
  "data": { ... },
  "error": null,
  "trace_id": "uuid-v7"
}
```

---

## 1. 가입 (Signup) — 4건

### 1.1 POST /signup
- 가입 세션 생성 (1단계: 사업자 정보)
- 요청: `{ company_name, business_number, representative_name, email, phone, industry_category }`
- 응답: `{ session_id, expires_at, next_step: "EMAIL_VERIFY" }`
- 부수효과: SignupSession 생성, business_number 중복 검증

### 1.2 POST /signup/business-license
- 사업자등록증 업로드 (R2 사전서명 URL)
- 요청: `multipart/form-data { session_id, file }`
- 응답: `{ upload_id, ocr_business_number, is_match }`
- 헌법 #22: 파일은 Cloudflare R2 위임, 본사 DB는 메타만

### 1.3 POST /signup/verify-email
- 이메일 OTP 요청·검증
- 요청 (발송): `{ session_id, action: "SEND" }`
- 요청 (검증): `{ session_id, action: "VERIFY", otp_code }`
- 응답: `{ verified: bool, next_step: "PHONE_VERIFY" }`
- 정책: 10분 만료, 5회 시도 한도

### 1.4 POST /signup/complete
- 약관 동의 + 가입 확정
- 요청: `{ session_id, terms_version, consents: {service_terms, privacy_policy, payment_terms, data_handling, marketing} }`
- 응답: `{ tenant_id (pre-created), next_step: "PAYMENT", payment_intent_url }`
- 부수효과: 백오피스 Push (tenants 가도) — SEQUENCE_3SYSTEMS 정합

---

## 2. 휴대폰 본인인증 (Phone Verify) — 2건

### 2.1 POST /verify-phone/start
- PASS / 카카오 인증 가도
- 요청: `{ session_id, provider: "PASS"|"KAKAO" }`
- 응답: `{ redirect_url, callback_token }`

### 2.2 POST /verify-phone/callback
- 인증 콜백 (PASS / 카카오 → 본사)
- 요청: `{ callback_token, provider_token, ci_encrypted }`
- 응답: `{ verified: true, next_step: "TERMS" }`
- 헌법 #25 안전하게: CI는 AES-256 암호화

---

## 3. 베타 모집 (Beta) — 3건

### 3.1 POST /beta-apply
- 베타 30곳 신청
- 요청: `{ company_name, business_number, representative_*, industry_category, expected_device_count, pain_points }`
- 응답: `{ application_id, status: "PENDING", queue_position }`
- 정책: 사업자번호 중복 차단

### 3.2 GET /beta-status
- 실시간 카운터 (Cloudflare Cache 10초 TTL)
- 응답: `{ target: 30, applied: N, approved: M, remaining: K, deadline: datetime }`

### 3.3 POST /beta-waitlist
- 마감 후 대기 등록
- 요청: `{ business_number, company_name, email }`
- 응답: `{ waitlist_id, priority_score }`

---

## 4. 결제 (Payment) — 3건

### 4.1 POST /payment/intent
- 결제 인텐트 생성 (Mock 또는 토스 위젯 가도)
- 요청: `{ session_id, tier_code, billing_period: "MONTHLY"|"ANNUAL", payment_method_type }`
- 응답: `{ intent_id, provider, provider_intent_id, amount: decimal, expires_at, widget_config }`
- 헌법 #4: amount는 decimal

### 4.2 POST /payment/confirm
- 결제 승인 콜백
- 요청: `{ intent_id, approval_token, provider_payment_id }`
- 응답: `{ confirmed: true, license_token, download_url, receipt_url }`
- 부수효과: 백오피스 Push (subscriptions + payments 생성), 라이선스 키 발급

### 4.3 POST /payment/webhook
- 결제사 비동기 webhook (토스·KCP)
- 인증: 서명 검증 (provider별 시크릿 키)
- 요청: provider 페이로드
- 응답: `200 OK` (idempotent 처리)

---

## 5. 라이선스·다운로드 (License & Download) — 4건

### 5.1 GET /license/{token}
- 라이선스 키 정보 조회 (다운로드 페이지 진입 인증)
- 응답: `{ license_id, tenant_id, tier, device_count_max, expires_at, installer: {version, download_url, sha256} }`

### 5.2 GET /download/installer
- 설치 EXE 다운로드 (R2 사전서명 URL)
- 쿼리: `license_token`
- 응답: 302 Redirect → R2 URL (1시간 만료)
- 부수효과: InstallerDownload 로그 기록

### 5.3 POST /download/complete
- 다운로드 완료 신고 (해시 검증 후)
- 요청: `{ license_token, sha256_verified: bool }`
- 응답: `{ acknowledged: true }`

### 5.4 GET /installer/manifest
- 최신 설치 EXE 매니페스트 (공개)
- 응답: `{ version, released_at, file_size, sha256, release_notes_url }`

---

## 6. 정적 콘텐츠 (Public) — 4건

### 6.1 GET /pricing
- 가격 티어 + FAQ + 베타 특가
- 응답: `{ tiers: PricingTier[], beta_offer, faq: PricingFAQ[] }`
- Cache: CDN 5분

### 6.2 GET /features
- 기능 영역 + 비교표
- 응답: `{ features: FeatureBlock[], comparison: ComparisonTable }`
- Cache: CDN 15분

### 6.3 GET /installation-guide
- 설치 매뉴얼 (스크린샷·영상)
- 응답: `{ steps: InstallationGuide[] }`
- Cache: CDN 30분

### 6.4 POST /cta-event
- CTA 클릭 로그
- 요청: `{ cta_label, visitor_session_id, utm: {source, medium, campaign} }`
- 응답: `{ recorded: true }`
- 보관: 90일

---

## 7. 헌법 정합 체크

| 헌법 | 적용 |
|---|---|
| #4 decimal | amount·monthly_price·annual_price 모두 decimal |
| #18 v3 | 가입 메타만, 업무 데이터 0 |
| #22 | 사업자등록증 파일·카드 원본·CI 본사 DB 0건 (R2·결제사·암호화) |
| #25 쉽게 | 가입 5단계 → 가도, 다운로드 1클릭 |

---

## 8. 사장님 결재 영역
- Rate limit 한도 (분당 60·시간당 5) 확정
- OTP 만료 시간 (10분·5회) 확정
- 라이선스 토큰 만료 정책 (1시간 vs 영구) 결재

## 9. W3 가도 예고
- 각 엔드포인트 요청·응답 JSON 박제
- 토스 위젯 v2 통합 시퀀스
- PASS / 카카오 콜백 시퀀스
- Cloudflare R2 사전서명 URL 발급 로직
