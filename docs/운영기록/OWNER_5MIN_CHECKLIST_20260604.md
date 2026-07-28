# 사장님 운영 필드 체크리스트 — 5분 단독 점검 (P 차수)

> 사장님 결재 2026-06-04
> 사용처: 운영 PC에서 11+2개 차수(W1+W2+W5+C+E+F+G+H+N+O+Q+K+S+W9+W10+W11) 일괄 박제 후 사장님 단독 5분 통과 점검
> 13개 차수 모두 운영 활성화 확인용 단일 체크리스트
> DEPLOY_CHECKLIST_20260604.md §5-3·5-4와 일관

---

## ✅ 사전 준비 (1분)

- [ ] 백오피스 API 서버 5258 가동 확인 (`/healthz` 200 OK)
- [ ] ERP API 서버 5257 가동 확인 (`/health` 200 OK)
- [ ] MariaDB 11.4.10 접속 가능 (hitpan / Hitpan2025!)

**불통과 시**: DEPLOY_CHECKLIST §1 → §4 다시 점검.

---

## 1️⃣ 랜딩 가입 → ERP 자동 반영 (1분)

| 점검 항목 | 통과 기준 |
|---|---|
| `/signup` 접속 | DEMO 모드 라디오 박제 정상 |
| 사업자등록증 업로드 | 미리보기 OK |
| 결제 페이지 진입 | 토스 자격증명 박제 시 배너 사라짐 |
| 라이선스 발급 메일 | (운영 환경) SMTP 도착 |
| 다운로드 EXE 설치 | 클라이언트 PC `/setup/license` Step 1·2 통과 |
| ERP 자동 반영 | local_company.is_locked_from_landing = 1 |

**진범 봉합 확인**: PaymentPage·RecoveryPage 빈 catch 0건 (S 차수).

---

## 2️⃣ 백오피스 V2 5화면 클릭 폭발 (1분)

| 화면 | 점검 |
|---|---|
| `/admin/tenants` | 목록 → 코드 클릭 → 상세 진입 |
| `/admin/tenants/{id}` | 정지/복구 버튼 동작 → Snackbar |
| `/admin/resellers` | 목록 → 회사명 클릭 → 상세 |
| `/admin/resellers/{id}` | 영업 고객사 클릭 → 고객사 상세 (양방향) |
| `/admin/reseller-applications` | 회사명 클릭 → 상세 Dialog |

**클릭 폭발 0건 확인**: L 차수 보고서 §5 참조.

---

## 3️⃣ Owner 영역 — W11 기능 (1분)

| 점검 항목 | 통과 기준 |
|---|---|
| Owner 로그인 → 자격증명 상태 (`/owner/credentials-status`) | 환경변수 헬스체크 녹색 |
| bo_users 목록 (`/admin/bo-users` 또는 API 직접) | 사장님 1명 표시 |
| MFA 등록 (`enroll-start` → `enroll-confirm`) | QR 스캔 후 6자리 코드 통과 |
| 4-eyes 승인 큐 (`/admin/approvals`) | 빈 큐 또는 pending 표시 |
| 감사 로그 (`bo_audit_log`) | mfa.enroll 1건 박제 |

**위반 시**: `bo_user_mfa` 테이블 박제 안 됐을 가능성 → DDL §2-4 + `20260604_bo_audit_log.sql` 재실행.

---

## 4️⃣ 대리점 정산·시리얼 — W9 기능 (1분)

| 점검 항목 | 통과 기준 |
|---|---|
| 정산 산출 (POST `/api/backoffice/reseller-settlements/calculate`) | resellerId + month → settlementId 응답 |
| 정산 목록 | draft 1건 표시 |
| 정산 확정 (`/{id}/confirm`) | status: draft → confirmed |
| 시리얼 발급 (POST `/reseller-serials/issue`) | 평문 시리얼 N개 응답 (1회만) |
| 시리얼 목록 | available 상태 표시, serial_prefix(8자)만 노출 |

**위반 시**: `20260604_reseller_settlement.sql` 미실행 가능성.

---

## 5️⃣ ERP ↔ 백오피스 webhook — W10 (1분)

| 점검 항목 | 통과 기준 |
|---|---|
| 백오피스에서 고객사 정지 | `webhook_outbox` 1건 INSERT (status=pending) |
| 1분 대기 | dispatcher 발송 → status=sent |
| ERP `local_subscription.status` | suspended로 갱신, sync_source=`webhook:{nonce}` |
| 백오피스에서 복구 | 동일 흐름으로 active 복귀 |

**위반 시**:
- `webhook_outbox` 미생성 → DDL `20260604_backoffice_webhook_outbox.sql` 실행
- 발송 실패 → `last_error` 컬럼 확인, ERP `/api/internal/webhook/subscription` 도달 여부 점검
- HMAC 서명 불일치 → 양쪽 `HITPAN_BOOTSTRAP_TOKEN_KEY` 동일값 박제 확인

---

## 6️⃣ 5분 통과 종합 판정

5개 영역(랜딩+V2+W11+W9+W10) 모두 통과 = **13개 차수 운영 활성화 완료**.

1개 영역 실패 시:
- 백엔드 API 미가동 → DEPLOY §4 서비스 재기동
- DB 박제 누락 → DEPLOY §2 마이그 순서대로 재실행
- 환경변수 누락 → DEPLOY §3 13개 + `HITPAN_BO_MFA_KEY` 추가 박제
- 폭발 0건이 아님 → L 차수 V2_FIELD_AUDIT_20260604.md §5 점검 + 본 PM 보고

---

## 7️⃣ 사장님 영역 (헌법 #29) 잔여 박제

| 항목 | 박제 위치 | 명령 |
|---|---|---|
| W10 DDL | hitpan_backoffice | `SOURCE db/migrations/20260604_backoffice_webhook_outbox.sql;` |
| W11 DDL | hitpan_backoffice | `SOURCE db/migrations/20260604_bo_audit_log.sql;` |
| W9 DDL | hitpan_backoffice | `SOURCE db/migrations/20260604_reseller_settlement.sql;` |
| W11 MFA 키 | Machine 환경변수 | `[Environment]::SetEnvironmentVariable("HITPAN_BO_MFA_KEY", "32바이트랜덤", "Machine")` |
| 양쪽 서버 재기동 | PowerShell | `Restart-Service HitPanApi; Restart-Service HitPanBackofficeApi` |

5건 모두 사장님 직접. PM은 가이드만.

---

**다음 PM 작업**: 사장님 박제 후 본 체크리스트 5분 점검 → 1개 영역이라도 폭발 시 PM 보고. 모두 통과 = 13개 차수 클로즈.
