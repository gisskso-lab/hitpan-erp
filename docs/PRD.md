# 히트판 ERP — Product Requirements Document (PRD)
> 버전: 2.0 | 기준일: 2026-05-05 | 현재 코드베이스 기반 작성

---

## 1. 제품 비전

### 1.1 존재 이유
> "레거시 히트판이 VB + Access + 낡은 UI로도 살아남은 이유는 딱 하나. 쓰기가 겁나 쉬웠기 때문이다."

히트판은 기술로 이긴 게 아니다. **쉬움으로 이겼다.**
새 히트판도 이 정신을 이어야 한다.

### 1.2 핵심 통과 기준
- **3분 룰**: 현장 직원(경리·구매·창고)이 처음 앉아서 3분 안에 핵심 동작 완료
- **한 화면 완결**: 스크롤·탭 전환 없이 한 화면에 핵심 정보 표시
- **30초 셀링**: "이게 왜 좋은지" 30초 안에 설명 가능

---

## 2. 제품 구성 (3분할 SaaS)

```
┌─────────────────────────────────────────────────────────────────┐
│  1. ERP (현재 완성)                                              │
│     고객사 업무 기능 — 판매·매입·재고·회계·그룹웨어               │
│     계정: tenant_admin / tenant_user                            │
├─────────────────────────────────────────────────────────────────┤
│  2. 백오피스 (베타 배포 전 완성 필수) ← 지금 여기               │
│     하나의 앱 — 로그인 계정 타입으로 뷰 분기                     │
│     계정 A: 본사 관리자 → 전체 고객사 + 대리점 + 수수료 관리     │
│     계정 B: 대리점 계정 → 본인 담당 고객사 + 본인 실적만 조회    │
├─────────────────────────────────────────────────────────────────┤
│  3. 홈페이지 (MVP 이후)                                          │
│     구독·결제·대리점 포털                                        │
└─────────────────────────────────────────────────────────────────┘
```

---

## 3. 사용자 & 계정 타입

### 3.1 ERP 계정 (고객사)

| 계정 타입 | 설명 | 주요 기능 |
|---------|------|---------|
| `tenant_admin` | 고객사 관리자 | 전체 ERP 기능 + 구독/결제 관리 |
| `tenant_user` | 고객사 직원 | 권한 설정 범위 내 ERP 기능 |

### 3.2 백오피스 계정 (히트판 본사 + 대리점)

| 계정 타입 | 설명 | 접근 범위 |
|---------|------|---------|
| `platform_admin` | 본사 관리자 | **전체** 고객사 + **전체** 대리점 + 수수료 정산 + CS |
| `reseller_admin` | 대리점 계정 | **본인 담당** 고객사만 + **본인** 실적/수수료만 |

> **핵심 설계**: 백오피스는 URL 하나(`/backoffice`). 로그인하면 JWT의 `account_type` 클레임으로 뷰 자동 분기.

---

## 4. ERP 기능 명세 (완성)

### 4.1 6단계 워크플로우

```
1단계 설정    → 회사정보·직원·권한·결재·직급·기기·환경
2단계 마스터  → 업체·상품·BOM·특별단가·원장
3단계 매입    → 발주→매입확정(재고↑ + 분개) → 반품(재고↓ + 역분개)
4단계 판매    → 견적→수주→거래명세서확정(재고↓ + 분개) → 세금계산서
               → 취소(재고↑ + 역분개) / 계산서취소(역분개)
5단계 현황    → 재고현황·수불부·매입/판매/발주/반품 현황·순위·통계
6단계 재무    → 수금·지급·출납·부가세·손익·월마감·세무사자료
```

### 4.2 워크플로우 3흐름 무결성 원칙

```
흐름1 — 매입:   발주→매입확정(stock↑ + journal) → 반품확정(stock↓ + 역journal)
흐름2 — BOM:    조립확정(완제품↑ + 자재↓, 동일 tx)
흐름3 — 판매:   거래명세서확정(stock↓ + journal) → 취소(stock↑ + 역journal)
                 → 세금계산서취소(역journal)
```

**원칙**: 한 단계라도 끊기면 즉시 P0 핫픽스.

### 4.3 구현 완료 기능 목록

#### 설정 (1단계)
| 기능 | 경로 |
|------|------|
| 회사 정보 | /company |
| 직원 계정 관리 | /users |
| 권한 설정 | /users/permissions |
| 결재 설정 | /settings/approval |
| 결재라인 설정 | /settings/approval-lines |
| 직급 관리 | /settings/positions |
| 등록 기기 관리 | /settings/devices |
| 사용환경 설정 | /settings |

#### 마스터 (2단계)
| 기능 | 경로 |
|------|------|
| 업체 마스터 | /partners |
| 업체 특별단가 | /partners/special-prices |
| 업체별 원장 | /partners/ledger |
| 상품 마스터 | /items |
| BOM 자재명세서 | /bom |
| 상품 특별단가 | /items/special-prices |
| 상품별 원장 | /items/ledger |

#### 매입 (3단계)
| 기능 | 경로 |
|------|------|
| 발주서 | /purchase (WorkDoc) |
| 발주현황 | /purchase-order-status |
| 매입 처리 | /purchase (WorkDoc) |
| 매입현황 | /purchase-status |
| 반품 처리 | /purchase (WorkDoc) |
| 반품현황 | /return-status |
| 매입순위표 | /purchase/ranking |
| 매입통계 | /purchase/statistics |

#### 판매 (4단계)
| 기능 | 경로 |
|------|------|
| 견적서 | /sales (WorkDoc) |
| 견적현황 | /quotation-status |
| 수주서 | /sales (WorkDoc) |
| 수주현황 | /sales-order-status |
| 거래명세서 | /sales (WorkDoc) |
| 판매현황 | /sales/summary |
| 판매순위표 | /sales/ranking |
| 판매수익성분석 | /sales/profitability |
| 판매통계 | /sales/statistics |
| 세금계산서 발행 | /tax-invoice |
| 세금계산서 통계 | /tax-invoice-stats |
| 범용인증서 관리 | /tax/certificate |

#### 재고 (5단계)
| 기능 | 경로 |
|------|------|
| 재고 현황 | /stock |
| 수불부 | /stock/ledger |
| 재고 실사·조정 | /stock/adjust |
| 재고 이송 | /stock/transfer |
| 재고 이송 현황 | /stock/transfer-status |
| 창고 관리 | /stock/warehouse-manage |
| 창고분리 | /stock/warehouse-split |

#### 회계 (6단계)
| 기능 | 경로 |
|------|------|
| 수금 | /collections |
| 지급 | /payments |
| 현금출납장 | /accounting/cashbook |
| 매입매출장 | /accounting/purchase-sales |
| 부가세 신고자료 | /accounting/vat |
| 경비 처리 | /accounting/expenses |
| 손익 현황 | /accounting/profit |
| 어음 관리 | /accounting/bills |
| 카드 결제 | /accounting/card-payments |
| 은행 거래내역 | /accounting/bank-transactions |
| 월마감 | /accounting/monthly-closing |
| 세무사 자료 보내기 | /accounting/export |

#### 그룹웨어
| 기능 | 경로 |
|------|------|
| 결재 (대기·발송·완료) | /approval/* |
| 사원관리 | /employees |
| 근태 관리 | /hr/attendance |
| 휴가·연차 | /hr/leave |
| 경비 신청 | /hr/expense-request |
| 전자근로계약서 | /hr/labor-contracts |
| 전자서명 이력 | /hr/esign-history |

#### 자료관리
| 기능 | 경로 |
|------|------|
| 자료 백업 | /data/backup |
| 구히트판 MDB 이관 | /settings/mdb-migration |
| 양식(인쇄) 설정 | /print-settings |
| 이메일(SMTP) 설정 | /settings/email |
| ERP 로그 기록 | /data/logs |

### 4.4 ERP 미완성 기능
| 기능 | 상태 | 우선순위 |
|------|------|---------|
| 홈택스 연동 세금계산서 관리 | 외주 검토 중 | P2 |
| AI 챗봇 (CS 사용법 안내) | 설계 완료, 미구현 | P3 |

---

## 5. 백오피스 기능 명세 (베타 배포 전 필수)

> 하나의 앱, 하나의 URL. 계정 타입(`platform_admin` / `reseller_admin`)으로 뷰 자동 분기.

### 5.1 공통 — 로그인 & 인증

| 기능 | 설명 |
|------|------|
| 로그인 | 이메일 + 비밀번호. JWT 발급 (`account_type` 클레임 포함) |
| 자동 뷰 분기 | `platform_admin` → 본사 뷰 / `reseller_admin` → 대리점 뷰 |
| 세션 관리 | Refresh Token (7일), Access Token (15분) |

---

### 5.2 본사 뷰 (`platform_admin`)

#### 5.2.1 대시보드
| 지표 | 설명 |
|------|------|
| 전체 고객사 수 | Active / Trial / Suspended 분류 |
| 이번달 신규 가입 | 이번 달 신규 테넌트 수 |
| 이번달 매출 | 구독 결제 합계 (MRR) |
| 미수금 | 연체 고객사 미납 합계 |
| 대리점별 실적 순위 | 이번달 신규 고객사 수 기준 TOP 5 |
| 최근 가입 고객사 | 최근 5건 |

#### 5.2.2 고객사 관리 (전체)
| 기능 | 설명 |
|------|------|
| 고객사 목록 | 전체 조회. 검색(회사명) + 필터(구독상태·대리점·플랜) |
| 고객사 상세 | 기본정보 / 구독·결제 / 결제이력 / 접속로그 탭 |
| 고객사 수동 생성 | 베타 온보딩용 (회사명·사업자번호·관리자 이메일·대리점 배정) |
| 구독 상태 변경 | Trial → Active → Suspended → Expired |
| 담당 대리점 변경 | 고객사 ↔ 대리점 재배정 |
| CS 로그 조회 | 해당 고객사 감사 로그 (로그인·주요 액션) |

#### 5.2.3 대리점 관리
| 기능 | 설명 |
|------|------|
| 대리점 목록 | 대리점명·담당고객사수·이번달실적·수수료율 |
| 대리점 상세 | 기본정보 / 담당 고객사 목록 / 수수료 정책 / 정산 이력 |
| 대리점 신규 등록 | 회사명·사업자번호·로그인 계정 생성 |
| 수수료 정책 설정 | 대리점별 플랜별 수수료율 (%) 설정·이력 관리 |

#### 5.2.4 수수료 정산 관리
| 기능 | 설명 |
|------|------|
| 월별 정산 현황 | 전체 대리점 × 월 정산 상태 (Draft / 승인 / 지급완료) |
| 정산 상세 | 대리점별 — 담당 고객사 구독료 합계 × 수수료율 = 정산액 명세 |
| 정산 승인 처리 | 본사 관리자가 확인 후 승인 → 지급완료 처리 |

---

### 5.3 대리점 뷰 (`reseller_admin`)

> 모든 조회는 JWT의 `reseller_id` 클레임 기준으로 자동 필터. 타 대리점 데이터 접근 불가.

#### 5.3.1 대시보드
| 지표 | 설명 |
|------|------|
| 담당 고객사 수 | Active / Trial 분류 |
| 이번달 신규 | 내가 유치한 이번달 신규 고객사 |
| 이번달 예상 수수료 | 현재까지 누적 예상액 |
| 누적 수수료 | 전체 기간 지급 완료 수수료 합계 |
| 담당 고객사 현황 | 구독 상태별 목록 (만료 임박 강조) |

#### 5.3.2 담당 고객사 관리
| 기능 | 설명 |
|------|------|
| 고객사 목록 | 본인 담당 고객사만. 검색 + 구독상태 필터 |
| 고객사 상태 조회 | 구독 현황·다음 결제일·미납 인보이스·마지막 로그인 |
| CS 지원 | 고객사 문의 대응용 상태 확인 (데이터 수정 불가) |

#### 5.3.3 영업실적 조회
| 기능 | 설명 |
|------|------|
| 월별 실적 | 신규 고객사 수·이탈 수·활성 고객사 수·MRR 추이 |
| 고객사별 계약 현황 | 담당 고객사 × 플랜 × 구독료 목록 |

#### 5.3.4 수수료·정산 조회
| 기능 | 설명 |
|------|------|
| 월별 수수료 내역 | 담당 고객사 구독료 합계 × 수수료율 = 수수료액 |
| 정산 현황 | Draft / 승인대기 / 지급완료 상태 조회 |
| 정산 상세 | 고객사별 구독료·수수료율·수수료액 명세 |

---

## 6. 백오피스 기술 설계

### 6.1 DB 신규 테이블

| 테이블 | 설명 |
|--------|------|
| `platform_admins` | 본사 관리자 계정 (email, password_hash, role) |
| `resellers` | 대리점 마스터 (회사명, 사업자번호, 연락처, 은행계좌) |
| `reseller_accounts` | 대리점 로그인 계정 (대리점당 N명 가능) |
| `reseller_commissions` | 수수료 정책 (대리점별·플랜별·기간별 요율) |
| `commission_settlements` | 월별 정산 내역 (draft→approved→paid) |

### 6.2 JWT 클레임 구조

```json
// 본사 관리자
{
  "account_type": "platform_admin",
  "admin_id": "uuid",
  "role": "super_admin"
}

// 대리점 계정
{
  "account_type": "reseller_admin",
  "account_id": "uuid",
  "reseller_id": "uuid"
}
```

### 6.3 API 구조

```
/api/backoffice/auth/login          POST  — 공통 로그인
/api/backoffice/auth/refresh        POST  — 토큰 갱신

/api/admin/dashboard                GET   — 본사 대시보드 KPI
/api/admin/tenants                  GET   — 전체 고객사 목록
/api/admin/tenants/{id}             GET   — 고객사 상세
/api/admin/tenants                  POST  — 고객사 수동 생성
/api/admin/tenants/{id}/status      PATCH — 구독 상태 변경
/api/admin/tenants/{id}/logs        GET   — CS 로그 조회
/api/admin/resellers                GET   — 대리점 목록
/api/admin/resellers                POST  — 대리점 등록
/api/admin/resellers/{id}           GET   — 대리점 상세
/api/admin/resellers/{id}           PUT   — 대리점 수정
/api/admin/commissions              GET   — 수수료 정책 목록
/api/admin/commissions              POST  — 수수료 정책 설정
/api/admin/settlements              GET   — 전체 정산 현황
/api/admin/settlements/{id}/approve POST  — 정산 승인

/api/reseller/dashboard             GET   — 대리점 대시보드 KPI
/api/reseller/tenants               GET   — 담당 고객사 목록
/api/reseller/tenants/{id}/status   GET   — 고객사 상태 조회
/api/reseller/performance           GET   — 영업실적 (월별)
/api/reseller/settlements           GET   — 본인 정산 내역
/api/reseller/settlements/{id}      GET   — 정산 상세
```

### 6.4 권한 Policy

```
PlatformAdmin  — account_type == "platform_admin"
ResellerAdmin  — account_type == "reseller_admin"
BackofficeAny  — platform_admin OR reseller_admin
```

### 6.5 프론트엔드 구조

```
/backoffice/login          — 공통 로그인 페이지
/backoffice/dashboard      — 로그인 후 account_type으로 자동 분기

BackofficeLayout (공통)
  ├─ platform_admin → PlatformSidebar
  │    ├─ 대시보드
  │    ├─ 고객사 관리
  │    ├─ 대리점 관리
  │    └─ 수수료 정산
  └─ reseller_admin → ResellerSidebar
       ├─ 대시보드
       ├─ 담당 고객사
       ├─ 영업실적
       └─ 수수료·정산
```

---

## 7. 데이터 원칙

### 7.1 ERP 업무 데이터 경계 (헌법 #18)

```
백오피스가 다루는 데이터 (O):
  - 테넌트 메타정보 (회사명·상태·플랜·가입일)
  - 구독·결제·인보이스 데이터
  - 접속 로그 (로그인 시각·IP) — 감사 목적
  - 대리점 정보·수수료·정산 데이터

백오피스가 절대 조회하면 안 되는 데이터 (X):
  - 고객사 매출/매입/원장/거래처/직원/상품/재고
  - 세금계산서 내용·결재 문서
  → 고객사 업무 데이터는 고객사 DB에만 존재
```

### 7.2 멀티테넌트 격리

- ERP API: `tenant_id` = JWT 클레임에서만
- 백오피스 Admin API: `tenant_id` = URL 파라미터 허용 (본사 관리자 권한 전제)
- 백오피스 Reseller API: `reseller_id` = JWT 클레임에서만 (타 대리점 차단)

---

## 8. 보안 요구사항

| 항목 | ERP | 백오피스 |
|------|-----|---------|
| 인증 | JWT 15분 + Refresh 7일 | JWT 15분 + Refresh 7일 |
| 테넌트 격리 | TenantMiddleware 강제 | ResellerId 클레임 강제 |
| 암호화 | AES-256 (사업자번호·계좌·연락처) | AES-256 (대리점 계좌) |
| Rate Limiting | ✅ 적용 | ✅ 동일 적용 |
| 계정 잠금 | 5회 실패 → 15분 | 5회 실패 → 15분 |

---

## 9. 배포 아키텍처

### 9.1 로컬 설치형 (ERP 전용)

```
고객사 PC (Windows)
  └─ HitPan EXE v1.0.7 (Inno Setup)
      ├─ MariaDB 11.4
      ├─ ASP.NET Core 8 API (localhost:5257)
      ├─ Blazor Web ERP (localhost:5234)
      └─ cloudflared → *.prov.hitpan.app
```

### 9.2 클라우드형 (백오피스 포함)

```
Docker Compose
  ├─ hitpan-api      (ASP.NET Core 8 — ERP + 백오피스 API 통합)
  ├─ hitpan-erp-web  (Blazor WASM — ERP)
  ├─ hitpan-bo-web   (Blazor WASM — 백오피스)
  └─ mariadb         (MariaDB 11.4)
```

---

## 10. 베타 출시 게이트

### 10.1 현재 상태 (2026-05-05)

| 게이트 | 상태 |
|--------|------|
| ERP 6단계 워크플로우 완성 | ✅ |
| 전수조사 100점 | ✅ |
| EVF 6대 영역 Fail 0건 | ✅ |
| appsettings.Production.json | ✅ |
| CHANGELOG.md | ✅ |
| **백오피스 — 고객사 온보딩 관리 도구** | ❌ 미완성 |
| **백오피스 — 대리점 영업 관리 도구** | ❌ 미완성 |
| develop → main 병합 (PR #1) | 대기 중 |

### 10.2 배포 불가 사유

> "고객사 온보딩 관리도구, 대리점 영업관리도구 없이 배포는 없어" — 사장님

- 본사 관리자가 베타 고객사를 수동 등록할 도구가 없음
- 대리점이 본인 실적·수수료를 확인할 수 없음
- 본사가 전체 고객사 현황을 모니터링할 수 없음

---

## 11. 백오피스 개발 스코프 (베타 배포 전 필수)

### Phase 1 — 백엔드 (1주)

**DB**
- [ ] `platform_admins` 테이블
- [ ] `resellers` 테이블
- [ ] `reseller_accounts` 테이블
- [ ] `reseller_commissions` 테이블
- [ ] `commission_settlements` 테이블
- [ ] `tenants.reseller_id` FK → `resellers.reseller_id`

**API**
- [ ] `/api/backoffice/auth/login` — 공통 로그인 (계정 타입 판별)
- [ ] `/api/admin/*` — 본사 관리자 API 7종
- [ ] `/api/reseller/*` — 대리점 API 5종
- [ ] `PlatformAdmin` / `ResellerAdmin` Policy 등록

### Phase 2 — 프론트엔드 (1주)

**공통**
- [ ] `BackofficeLayout` (사이드바 계정 타입 분기)
- [ ] `/backoffice/login` 페이지

**본사 뷰**
- [ ] `/backoffice/dashboard` — KPI 5개 + 순위 + 최근 가입
- [ ] `/backoffice/tenants` — 고객사 목록 (검색·필터·생성)
- [ ] `/backoffice/tenants/{id}` — 고객사 상세 (4탭)
- [ ] `/backoffice/resellers` — 대리점 목록 + 등록
- [ ] `/backoffice/settlements` — 수수료 정산 승인

**대리점 뷰**
- [ ] `/backoffice/dashboard` — KPI 4개 + 담당 고객사 현황
- [ ] `/backoffice/tenants` — 담당 고객사 목록
- [ ] `/backoffice/performance` — 영업실적 (월별)
- [ ] `/backoffice/settlements` — 수수료·정산 조회

---

## 12. 전체 로드맵

| 단계 | 내용 | 목표 |
|------|------|------|
| **지금** | 백오피스 Phase 1 백엔드 | 1주 |
| **다음** | 백오피스 Phase 2 프론트엔드 | 1주 |
| **Beta 1.0** | ERP + 백오피스 통합 배포 (9곳) | 5/17~22 |
| **Beta 1.1** | 피드백 반영 + 홈택스 연동 | 6월 |
| **MVP 1.0** | 정식 론칭 + 결제 자동화 | 5/23 목표 |
| **Phase 2** | 모바일 앱 + AI 챗봇 | MVP 이후 |

---

## 13. 용어 정리

| 용어 | 정의 |
|------|------|
| 테넌트 | 히트판 ERP를 구독하는 고객사 1개 단위 |
| 원장 | stock_ledger, journal_lines — INSERT ONLY 불변 기록 |
| 역분개 | 취소·반품 시 원분개 차/대변을 완전 반전한 새 분개 INSERT |
| 확정 | Draft → Confirmed 상태 전환. 이 시점에 원장 반영 |
| 월마감 | 해당 월 데이터 수정 잠금. 이후 수정 즉시 예외 |
| MRR | Monthly Recurring Revenue — 월간 반복 구독 매출 |
| EVF | Extreme Validation Framework — 6대 영역 극한 검증 체계 |
| reseller_id | 대리점 고유 ID. Reseller API에서 자동 필터 기준 |

---

*이 PRD는 현재 코드베이스 + 사장님 지시를 100% 기반으로 작성됐습니다.*
*백오피스 = 하나의 앱, account_type으로 뷰 분기.*
