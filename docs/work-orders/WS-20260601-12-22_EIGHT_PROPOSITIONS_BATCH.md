# 작업지시서 묶음 — WS-20260601-12 ~ 22 (8명제 트랙 C)

> 발행: 2026-06-01 야간 / 결재: 사장님 모두결재 / 마감: Week 4 (4주)
> 모 문서: `docs/설계/랜딩/EIGHT_PROPOSITIONS_BACKOFFICE_LANDING.md`

---

## WS-20260601-12: 랜딩 2버튼 UI + 사등 업로드

**담당:** 프론트 매니저 + 수석 웹디자이너
**시점:** Week 1

**산출물:**
- `src/HitPan.Web/Pages/Landing/HomePage.razor` 갱신 (2버튼: 신규등록 64% 그린 / 계정분실 36% 오렌지)
- `src/HitPan.Web/Pages/Landing/SignupPage.razor` — 사등 업로드 컴포넌트 + 약관 4종
- `src/HitPan.Web/Pages/Landing/RecoveryPage.razor` — 분실 복구 (사등 재업로드)
- `src/HitPan.Web/Shared/BizLicenseUploader.razor` — 드래그앤드롭 + 미리보기 + OCR 진행 칩 3단계
- OCR 온프레미스: PaddleOCR/Tesseract 자체 (외부 API 금지, AI수석 P0)
- 시리얼 입력창 자동 하이픈·CRC 즉시 검증

**헌법 정합:** #25 쉽게 · #29 인프라 사전결재 · #32 받아쓰기 금지

---

## WS-20260601-13: 본사 백오피스 시리얼 발급 화면 (4-eyes)

**담당:** 백엔드 매니저 + 프론트 매니저
**시점:** Week 2

**산출물:**
- `src/HitPan.Web/Pages/Admin/AdminTenantIssuePage.razor` — 가입 대기 목록 + 4-eyes 결재 UI
- `src/HitPan.Web/Pages/Admin/AdminResellerIssuePage.razor` — 대리점 발급 (사장님 결재)
- `src/HitPan.API/Controllers/Admin/AdminIssueController.cs` — POST /api/admin/issue/tenant + /reseller
- `src/HitPan.API/Services/SerialIssueService.cs` — HP-YYMM-XXXXXXXX-CRC 생성 + 멱등성 (idempotency_key 5분 UNIQUE)
- 임시 비번 메모리 1초 + Argon2id 해시 → DB → 이메일·SMS 전송 → 즉시 zeroing

**헌법 정합:** #11 권한 어드민 직접 설정 · #29 2인 결재

---

## WS-20260601-14: 이메일·SMS 2채널 인프라 (헌법 #16 정합)

**담당:** 백엔드 매니저 + 보안 매니저 2
**시점:** Week 2

**산출물:**
- `src/HitPan.API/Services/NotificationService.cs` — IEmailSender + ISmsSender 각자 독립 MySqlConnection
- AWS SES 또는 SendGrid (DKIM·SPF·DMARC)
- SMS: 알리고 또는 NHN Cloud (KISA)
- 재시도 지수백오프 (1m·5m·30m) + DLQ 24h
- 이메일 본문 = 활성화 링크 (평문 비번 X) / SMS = 임시 비번 평문 (2채널 분리)
- 헌법 #16 위반 방지: Task.WhenAll(SendEmail, SendSms) 절대 금지, 순차 처리 또는 독립 connection

---

## WS-20260601-15: 백오피스 DB 컬럼 (평문 0)

**담당:** DB 매니저
**시점:** Week 1

**산출물:**
- DDL 5종 (tenants, resellers, platform_users, platform_audit_log, recovery_log)
- 모두 `ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci` (헌법 #17)
- `biz_no_hash` = HMAC-SHA256(사업자번호, pepper HSM) + UNIQUE (DB 매니저 결정)
- 평문 사업자번호·상호·대표자·주소 컬럼 0건
- DESCRIBE 의무 (헌법 #13) 후 SQL 작성
- `recovery_log` 파티셔닝 (월 단위)

---

## WS-20260601-16: JWT 4계층 권한 정책

**담당:** 백엔드 매니저 + 보안 매니저 1
**시점:** Week 1

**산출물:**
- `Program.cs(API)` Authorization Policy 4종:
  - `OwnerOnly` (사장님 전용)
  - `PlatformManagerOrAbove` (Manager 이상)
  - `PlatformOnly` (Owner+Manager+Staff)
  - `ResellerSelfOnly` (자기 영업분만)
- JWT 클레임: role + reseller_serial(대리점) + hq_scope(본사 RLS 우회 방지)
- 파라미터 reseller_id·tenant_id 수신 즉시 반려 (헌법 #2 확장)
- 모든 SELECT에 ApplyRlsAsync() 강제 베이스 Repository

---

## WS-20260601-17: 대리점 RLS API + 화면 3건

**담당:** 프론트 매니저 + 백엔드 매니저
**시점:** Week 3

**산출물:**
- `src/HitPan.Web/Pages/Reseller/ResellerDashboardPage.razor` — 영업 실적 + 메타 대시보드(매출 규모·이상징후·갱신예정일)
- `src/HitPan.Web/Pages/Reseller/ResellerTenantListPage.razor` — 자기 영업 고객사 시리얼 목록
- `src/HitPan.Web/Pages/Reseller/ResellerCsListPage.razor` — 자기 영업 고객사 CS 티켓
- `src/HitPan.API/Controllers/ResellerController.cs` — JWT 클레임 reseller_serial 강제 필터
- 권한 위반 시도 시 403 + reseller_audit_log 박제

---

## WS-20260601-18: 본사 Owner/Manager/Staff 전권한 화면

**담당:** 프론트 매니저 + 백엔드 매니저
**시점:** Week 3

**산출물:**
- `Pages/Admin/AdminTenantListPage.razor` — 전 고객사 시리얼 + 검색·필터·페이징
- `Pages/Admin/AdminResellerListPage.razor` — 전 대리점 시리얼
- `Pages/Admin/AdminCsListPage.razor` — 전 CS 티켓
- `Pages/Admin/AdminBillingPage.razor` — 결제·환불 (Manager 이상)
- `Pages/Admin/AdminStatsDashboardPage.razor` — 전체 통계
- `Pages/Owner/OwnerSystemConfigPage.razor` — 시스템 설정 (Owner 전용)
- 사이드바 3분할 (본사·대리점·고객사) — Layout/ 3종

---

## WS-20260601-19: 생애주기 흐름 (휴폐업·대표자 변경·양도)

**담당:** ERP 매니저 + 백엔드 매니저
**시점:** Week 4

**산출물:**
- 국세청 휴폐업 조회 API 백그라운드 6개월 주기
- 대표자 변경 흐름: 고객사가 ERP에서 자가 갱신 → 본사 어드민 승인 → biz_no_hash 갱신 (변경 없으면 갱신 0)
- 사업자 양도: 신규 시리얼 발급 + 구 시리얼 폐기 + 데이터 이관 매뉴얼 (헌법 #24)
- 휴폐업 사업자 자동 차단 → 구독 만료 처리 + 본사 ERP 회계팀 통보 (시리얼만)
- JIT 복호화 토큰 발급 (CS 6건: 환불·재발행·대표자 변경·폐업·명의도용·압류) — 사유·승인자·15분 만료 + 감사로그

---

## WS-20260601-20: 본사 ERP ↔ 백오피스 단방향 Outbox 메시지 큐

**담당:** 백엔드 매니저 + DB 매니저
**시점:** Week 2

**산출물:**
- `messaging_outbox` 테이블 (백오피스 측)
- Hangfire poll 5분 주기 — 본사 ERP가 백오피스 outbox SELECT (Pull 0, Push 0)
- 메시지: 시리얼·발급일·결제 상태·구독 등급 메타만 (평문 사업자 정보 0)
- 역방향 (백오피스 → 본사 ERP) 절대 금지 (헌법 #18·#22)
- 본사 회계팀이 신규 구독 발생 시 시리얼만 받아 본사 ERP에 거래처 수동 등록 (사등 별도 채널 수령)

---

## WS-20260601-21: 사장님 PC SPOF 봉합 (HSM 2개)

**담당:** 보안 매니저 2 (인프라·OS)
**시점:** Week 4

**산출물:**
- HSM (YubiKey) 2개 구매: 본사 금고 1개 + 사장님 댁 1개
- Owner 권한 2/2 분리 (사장님 + 본부장 m-of-n)
- 시리얼 발급 서명키 HSM 강제 (사장님 PC 평문 보유 금지)
- 백업 E2E 암호화 + 본사 복호화 불가 (헌법 #22 정합)
- 사장님 ERP 망분리 (본사망과도 분리, 별도 백신·EDR·일일 백업)
- 콜드백업 주1회 외장 LTO
- WORM(append-only) 로그 스토리지 — 발급 행위·복호화 행위 전수 박제

---

## WS-20260601-22: 3시스템 통합 E2E (계정·사업자정보 영역만)

**담당:** AI수석 + ERP 매니저
**시점:** Week 4

**산출물:**
- `tests/e2e/eight-propositions-e2e.spec.ts` — Playwright 12 시나리오
  1. 랜딩 신규가입 → 결제 → 시리얼 발급 → 2채널 수신
  2. ERP 첫 로그인 → 강제 변경 → 약관 동의 → 6단계 진입
  3. 백오피스 운영팀 신규 검토 → 4-eyes → 발급
  4. 분실 복구 (사등 재업로드 → biz_no_hash 매칭 → 재발급)
  5. 환불 처리 (CS JIT 복호화)
  6. 대리점 로그인 → 자기 영업분만 조회 (평문 0)
  7. 대리점 권한 위반 시도 → 403 + 감사로그
  8. 본사 Owner 전체 통계
  9. CS 응대 → JIT 토큰 발급 → 사유·승인자 박제
  10. 휴폐업 사업자 자동 탐지 → 구독 만료
  11. 대표자 변경 흐름
  12. 사업자 양도 흐름
- 12 시나리오 100% PASS = 트랙 C 완료 → 트랙 B 진입 자격

---

## P0 결재 7건 (작업 진행 중 사장님 결재 필요 시점)

1. **평문 0 재정의**: "업무데이터 0 + 연락처 envelope 암호화 + 발급 직후 파기" (DB)
2. **biz_no_hash UNIQUE**: HMAC + pepper HSM 유지 vs 충돌 검사만 (DB)
3. **본사 ERP ↔ 백오피스 = 단방향 Outbox만**, Pull 금지 (DB·백엔드)
4. **JIT 복호화 + 감사로그 (CS 6건)** — 환불·재발행·대표자 변경·폐업·명의도용·압류 (ERP·보안 1)
5. **생애주기 흐름**: 휴폐업·대표자 변경·사업자 양도 (ERP)
6. **사장님 PC SPOF 봉합**: HSM 2개 + Owner 2/2 분리 + 백업 키 분리 (보안 2)
7. **시리얼 발급 SLA 1시간** + 24/7 자동발급 폴백 (영업·마케팅)

## P1 결재 4건

8. 랜딩 라우트: 풀페이지 (`/signup`·`/recover`) — 결재 완료 (작지 #12 풀페이지로 박제)
9. 사이드바 3분할 — 결재 완료 (작지 #18에 포함)
10. 시리얼 우편 vs QR + 홀로그램 스티커 (웹디자이너) — 추후 결재
11. OCR 자체 vs 외부 — 결재 완료 (작지 #12 자체 PaddleOCR/Tesseract)
