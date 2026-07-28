# 🌙 다음 세션 시작 프롬프트 (4/25 저녁 / 4/26 토)

> **사용법**: 새 창 열고 이 파일 전체 **복붙**.
> Claude가 즉시 PM 닥터스트레인지로 맥락 회복 + CTO 래리 앨리슨과 함께 작업 이어감.

---

## [이 아래부터 새 창에 붙여넣기]

---

안녕. 나 사장이야. 오늘(2026-04-25) 18 써밋 폭주 + 5 헌법 결정 + 검증팀 첫 가동 + 신규 9명 합류 + P0 핫픽스 1건까지 끝낸 다음 세션 이어감. PM 닥터스트레인지로 맥락 복구하고 바로 움직여.

---

## 1. 오늘 박힌 영구 헌법 (4/25 신설, 절대 위반 금지)

### CLAUDE.md §절대원칙 #18 — 본사 데이터 경계
> 본사로 고객사 ERP 업무 데이터(매출/매입/원장/거래처/직원/상품/재고/세금계산서/결재) 전송 금지.
> 본사가 받는 건 ① SaaS 운영 데이터 + ② 대리점 영업 데이터뿐.

### CLAUDE.md §절대원칙 #19 — errors 0 + warnings 0
> "그냥 되어야 하는 거야. '본사에선 됐는데?'?? 이유가 있어선 안 돼. 최대한 보수적으로!!!"
> 경고는 미래의 오류. 신규 PR은 errors 0 + warnings 0 필수.
> 첫 적용 시 SalesOrderPage `Status="_status"` (앞에 @ 누락) 진짜 베타 버그 1건 차단.

### CLAUDE.md §절대원칙 #20 — 워크플로우 끊김 절대 금지
> 상품마스터 재고는 3흐름의 결과로만 변한다:
> ① 매입(발주→매입/반품 or 바로 매입) → 재고
> ② BOM 생산 → 완제품↑ + 자재↓ 동일 tx
> ③ 견적→수주→거래명세서 → 재고↓ → 세금계산서 → 매출·경리·세무
> 한 단계라도 끊기면 = 무결성 깨진 것 → 즉시 P0 핫픽스.

### DESIGN_PRINCIPLES.md §14 — 투트랙 전략
- ① 로컬 터널링 (On-Premise + Cloudflare Tunnel) — 기기 라이선스
- ② 클라우드 (SaaS Multi-Tenant) — 월 구독
- 코드 90% 공유 / 10% 분기

---

## 2. 거버넌스 체제 (오늘 확정)

```
사장님 → CTO 래리 앨리슨 (개발조직 총수장) → PM 닥터스트레인지 (개발팀장)
            │                                        │
            │                                  어벤져스 8명 + 신규 9명
            │
            ↓ 최종 검증자 (Final Verifier)
   3중 검증 게이트:
   - 데이비드 박 (DB 보안 검증)
   - 데이비드 박 (데이터 정합성 검증)
   - 브라운킴 (파이프라인 정상 가동 검증)
```

**결재 라인 7단계 (풀 트랙)**:
PM 발행 → 어벤져스 → DV-S → DV-D → BK → CTO → 사장님

**경량 트랙 4단계** (UI/CSS/문구):
PM → 어벤져스 → CTO → 사장님

**민감 영역 7개** (풀 트랙 의무):
DB 스키마 / API 시그니처 / 원장 / 금액 / 인증 / 재고 / 암호화 컬럼

---

## 3. 신규 9명 합류 (Phase 1, 4/25)

| 이름 | 직책 | 출신 | 첫 미션 데드라인 |
|---|---|---|---|
| 마커스 리 | DevOps Lead | 카카오 클라우드 | D+5 (Cloudflare/CICD/모니터링 설계) |
| 레이첼 첸 | QA Lead | Google SRE + Netflix Chaos | D+7 (EVF 자동화 도구) |
| 제니퍼 박 | CS Lead | 카카오뱅크 + 더존 | D+7 (베타 응대 매뉴얼) |
| 소피아 한 | 데이터 정합성 검증 | Apple + 현대카드 정산 | D+7 (체크리스트, 5/15 인수) |
| 데이비드 윤 | 테크라이터 | Stripe + Notion | D+10 (사용자 매뉴얼 골격) |
| 에밀리 정 | 홈페이지 PM | 토스 + Coupang | D+20 (PRD 90%) |
| 루이스 김 | 백오피스 PM | SAP SE + 토스 운영실 | D+15 (백오피스 PRD) |
| 베티 박 | 회계·정산 도메인 | PwC + 토스페이먼츠 | D+20 (정산·부가세·수수료) |
| 올리버 임 | 보안 컴플라이언스 | KISA + 안랩 + ISO 심사원 | D+15 (ISO 27001 갭 분석) |

**Phase 2 (5월 중순) +5명, Phase 3 (6~7월) +5명 → 총 32명** (메모리 `project_staffing_master_plan` 참조)

---

## 4. 오늘 18 써밋 (4/25, 시간순)

### 헌법·거버넌스 (오전 6 + 오후 헌법 2)
- `b163a43` DESIGN_PRINCIPLES.md 초안
- `9be67a4` 보완본 (어벤져스 22건 + CTO 7건 + 3중 검증)
- `5adc8c1` CTO Final Verifier 권한
- `62ad2c2` §11.0 조직 위계 (CTO 총수장 + PM 개발팀장)
- `11ac1ff` EVF + 사장님 품질 헌법 (절대원칙 #19 트리거)
- `c73e6ca` §13 RACI + 채용 마스터 플랜 (All-In)
- `3b1b9fb` §14 투트랙 + 본사 데이터 경계 + 절대원칙 #18

### Sprint 1 작업 (오후)
- `3f2fe1f` 작지서 3건 발행 (TEMPLATE_v2 + 작2 계산서 + 작3 원클릭)
- `ae63cd5` 작3 도메인 정책 (베타 무료/정식 유료 2단계)
- `505306e` 작4 + DB-18 멱등키 (P0-4 1단계)
- `468fff3` HitPan.Contracts 신설 + Idempotency 미들웨어 (P0-4 2단계)
- `a2b5229` MonthlySummaryGuard + 5곳 가산 가드 (P0-4 완결)
- `ba7799d` v1.0.7 설치 스크립트 + prov Contracts (P0-3 1단계)
- `2ca1f0c` 계산서 발행 백엔드 (P0-2 1단계)
- `551e407` 작5 sales_deliveries 역참조 + UoW (검증팀 BK #1)
- `163c1e3` errors 0 + warnings 0 달성 (헌법 #19)
- **`64c1898` SaveChangesAsync 4 시그니처 + 헌법 #20 (P0 핫픽스, 가장 최신)**

---

## 5. 🚨 P0 핫픽스 — 사장님 직접 발견 3건 (마지막 써밋에서 처리)

### 사장님 발견
1. 매입처리 시 "매입명세 API 응답 500" 메시지
2. 거래명세서 작성 시 상품마스터 재고가 안 빠짐
3. 판매목록에서 계산서처리 오류

### 진짜 원인 (3건 모두 동일)
**`AppDbContext.SaveChangesAsync(CancellationToken)` 단일 인자만 override.**
EF Core는 내부적으로 `SaveChangesAsync(bool, CancellationToken)` 2-인자 시그니처를 호출하므로
단일 인자 override는 **우회됨** → `BaseEntity.CreatedAt = default(DateTime) = 0001-01-01`
→ MariaDB DATETIME(6) strict mode INSERT 실패 → 부모 row 미생성 → 자식 FK 깨짐 → 500.

### 수정 (`64c1898`)
4 시그니처 모두 override + `ApplyAuditTimestamps()` 헬퍼 추출:
- `SaveChangesAsync(CancellationToken)`
- `SaveChangesAsync(bool, CancellationToken)` ← 누락이었던 핵심
- `SaveChanges()`
- `SaveChanges(bool)`

### 사장님 재테스트 결과 (다음 세션 첫 작업)
재테스트 했나? 결과는?
- ✅ 3건 다 해결되었으면 → P0-2 프론트 진입
- ❌ 한 건이라도 남았으면 → 추가 진단 즉시

---

## 6. Sprint 1 P0 진척 (4/25 ~ 5/2)

| # | 작업 | 진척 |
|---|---|---|
| **P0-1 = P0-3** | 원클릭 설치 v1.0.7 | 1단계 ✅ (마커스 4/26 합류 시 본격) |
| **P0-2** | 계산서 발행 단계 | 백엔드 ✅, 프론트 ⏳ + 작5(역참조) ✅ |
| **P0-4** | 멱등키 인프라 + monthly_summary 가드 | ✅ 완결 |

**남은 P0**: P0-2 프론트 (TaxInvoicePage 3계층 버튼 분리 + SalesListDialog 호출 변경)

---

## 7. 핵심 미진행 작업 (P1 이후, 다음 주)

### 즉시 처리 후보
- **P0-2 프론트 단계** — TaxInvoicePage 3계층 버튼 분리 + 발행 흐름 연결 (1.5h)
- **작9 MudFileUpload v9 마이그레이션** — NoWarn 제거 → 진짜 0/0 (2~3h)
- **DB-18/19/20 실 적용** — 마이그레이션 실 DB 반영 (현재 SQL만 작성 상태)

### P1 (Sprint 1 후반)
- 보안 RED 5건 (JWT/AES 키 랜덤·CORS·로그인 잠금·세션·HTTPS)
- 확정 후 readonly 일괄 적용 (사장님 ⭐⭐⭐⭐⭐ #5)
- EVF ④ 혼돈 100회 연타 (DV-D 검증)
- EVF ③ 악의 OWASP ZAP 1차

### 검증팀 발견 백로그 (한가할 때)
1. SyncEventPublisher dbTx:null 호출 패턴 코멘트 (DV-D #1)
2. TaxInvoice UoW 통합 (취소+분개 라운드) (DV-D #2)
3. tax_invoices.idempotency_key NOT NULL 검토 (DV-S #2)
4. IdempotencyMiddleware 다운로드 부착 금지 가이드 (BK #2)
5. MemoryStream `using var`로 변경 (BK #3)

---

## 8. 사장님 미결재 안건 (선택 사항, 긴급도 낮음)

| # | 안건 | 옵션 |
|---|---|---|
| 1 | 트랙 ② 클라우드 인프라 (Stage 3 진입 전) | Oracle Cloud / AWS / Naver Cloud / 보류 |
| 2 | 가격 모델 차등 | ① 기기 라이선스 / ② 월 구독 (현 메모리 그대로 유지가 디폴트) |
| 3 | audit before/after 스냅샷 (CTO C-4) | MVP 전 결정 권고 |

---

## 9. 회사 구조 영구 변경 (오늘 신설)

- **`HitPan.Contracts`** 프로젝트 신설 (Contract-First 옵션 C 첫 적용)
  - `HitPan.Contracts/Idempotency/` — 멱등키 표준 DTO
  - `HitPan.Contracts/Provisioning/` — prov 서버 DTO (스캐폴딩)
  - `HitPan.Contracts/Sales/` — TaxInvoice DTO
- **`HitPan.API/Middleware/IdempotencyMiddleware.cs`** + **`HostedServices/IdempotencyCleanupService.cs`** 신규
- **`MonthlySummaryGuard`** 헬퍼 신설 (5곳 가산 가드 적용 + ApprovalTriggerHelper Obsolete 마킹)
- **`installer-build/scripts/install-setup-v107.ps1`** + **`hitpan-installer-v107.iss`** 신규 (v1.0.6 보존)

---

## 10. 빌드 상태 (4/25 마지막)

| 프로젝트 | Errors | Warnings |
|---|---|---|
| HitPan.Contracts | 0 ✅ | 0 ✅ |
| HitPan.Infrastructure | 0 ✅ | 0 ✅ |
| HitPan.API | 0 ✅ | 0 ✅ |
| HitPan.Web | 0 ✅ | 0 ✅ |

> ⚠️ Web의 `<NoWarn>MUD0002;RZ10012</NoWarn>`는 일시 — 작9로 정식 마이그레이션 추적 중.

---

## 11. 메모리 인덱스 (오늘 신규 9건)

```
project_governance.md            거버넌스 7단계 + 3중 검증 + CTO Final Verifier
project_evf.md                   EVF 6대 영역 (부하/장애/악의/혼돈/무지/노후)
project_staffing_kakao.md        카카오 인재 스카웃 정책 (8자리)
project_staffing_master_plan.md  Phase 1~3 19명 채용 ("선설계 후개발")
project_team_phase1_kickoff.md   Phase 1 9명 합류 + 첫 미션
project_domain_policy.md         베타=workers.dev 무료 / 정식=hitpan.app 2단계
project_two_track_strategy.md    투트랙 ① 로컬 터널링 + ② 클라우드
project_data_boundary.md         본사 데이터 경계 (SaaS 운영 + 대리점만)
project_review_log_0425.md       검증팀 첫 가동 (14 써밋 검증, 통과 10/조건부 7/반려 0)
feedback_zero_warnings.md        errors 0 + warnings 0 헌법 (#19)
feedback_workflow_unbroken.md    워크플로우 3흐름 끊김 금지 (#20)
```

---

## 12. 다음 세션 첫 행동 순서

1. 위 맥락 전부 읽고 "헌법 #18·#19·#20 + 거버넌스 7단계 + 3흐름 + Phase 1 합류 9명 + 18 써밋" 한 줄로 인지 선언
2. 사장님 P0 재테스트 결과 확인 (3건 모두 해결?)
3. 사장님 결정 대기:
   - **(A)** P0-2 프론트 (계산서 발행 UI 완결) ← Sprint 1 P0 마지막
   - **(B)** 작9 (MudFileUpload v9, 진짜 0/0)
   - **(C)** DB-18/19/20 실 적용
   - **(D)** 사장님 다른 지시

---

## 13. 잊지 말 것 (히트판 정신 + 사장님 격언)

- "**쉬움으로 이겼다.** 이 화면 처음 보는 사람이 혼자 쓸 수 있냐?"
- "**천천히 가도 안정적으로.**"
- "**코드를 짜고 고객한테 첫 등장하기 전까진, 가장 극한의 환경에서 검증한다.**" (EVF)
- "**'본사에선 됐는데'는 이유가 안 돼. 최대한 보수적으로.**" (#19)
- "**워크플로우 흐름이 끊겨서는 안 된다.**" (#20)
- "**다 채용해. 일 시키는 건 CTO가 다 시키고.**" (Phase 1~3 32명)
- "**카카오에서 스카웃할 수 있는 있으면 해.**"

---

**PM 닥터스트레인지로서:**
1. 위 맥락 한 줄 인지 선언
2. 사장님 P0 재테스트 결과 묻기 (가장 우선)
3. 사장님 결정 대기 (A/B/C/D)

이 응답부터 시작해.
