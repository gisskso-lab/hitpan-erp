# 15. Blazor Pages 전수 학습서 (107개 .razor)

**작성:** 프론트 매니저
**범위:** `src/HitPan.Web/Pages/` (재귀)

## 0. 페이지 107개 — 6단계 워크플로우 분류

### 1단계 설정 (Settings)
- /login, /employees, /settings, /settings/user-info, /users, /users/permissions, /settings/approval, /settings/approval-lines, /settings/positions, /settings/devices, /settings/email, /settings/mdb-migration, /company

### 2단계 마스터
- /items, /items/{Id}, /items/special-prices, /items/ledger
- /partners, /partners/{Id}, /partners/special-prices, /partners/ledger
- /bom, /bom/new, /bom/{Id}

### 3단계 매입
- /purchase-order-status, /purchase-status, /purchase-receipt-status, /return-status
- /purchase/ranking, /purchase/statistics
- Work Tabs: PurchaseOrder, PurchaseReceipt, Return (WorkTabService)

### 4단계 판매
- /quotation-status, /sales-order-status, /sales/summary, /sales/ranking, /sales/profitability, /sales/statistics
- /tax-invoice, /tax-invoice-stats, /tax/certificate
- Work Tabs: Quotation, SalesOrder, SalesDelivery

### 5단계 현황
- / (Dashboard), /stock, /stock/ledger, /stock/adjust, /stock/transfer, /stock/transfer-status, /stock/warehouse-manage, /stock/warehouse-split
- /more

### 6단계 재무
- /accounting/cashbook, /accounting/purchase-sales, /accounting/vat, /accounting/expenses, /accounting/profit
- /accounting/bills, /accounting/card-payments, /accounting/bank-transactions
- /accounting/accounts, /accounting/monthly-closing, /accounting/export
- /collections, /payments

### HR / 그룹웨어
- /hr/employees, /hr/attendance, /hr/leave, /hr/leave-request, /hr/expense-request, /hr/labor-contracts, /hr/esign-history
- /approval/pending, /approval/sent, /approval/completed

### 백업·로그
- /data/backup, /data/logs

### 백오피스
- /admin/*, /reseller/*

## 1. 주요 페이지 상세

### Login.razor
- @page "/login", AllowAnonymous
- IAuthService.LoginAsync(email, password)

### Dashboard.razor
- @page "/", "/dashboard"
- /api/finance/dashboard (30초 캐시)
- 권한·구독·기기 경고 배너 (옵션 B)
- 미수금 Aging 표시

### Items.razor
- 안전재고 미달 자동발주 배너 (사장님 헌법 2026-04-27)
- excludeBom 필터로 BOM 헤더 제외
- 엑셀 내보내기, 인쇄

### Users.razor
- WO-4 비밀번호 마스킹 토글
- WO-20260430-9 Step-up 인증 (수정·비활성·비밀번호 초기화)
- 엑셀 일괄 업로드 (ClosedXML, FileSecurityHelper.Validate)

### BomDetail.razor
- @page "/bom/new", "/bom/{Id}"
- 사장님 헌법 2026-04-26 3분기:
  1) 반제품 미달 → 즉시 반려
  2) 자재 미설정 → 안내 후 반려
  3) 모두 자동발주 설정 → 자동 사슬 (발주+매입+생산 1회)
- ConfirmAssembleAsync, ConfirmDisassembleAsync (헌법 #20)

### Stock.razor
- _viewType 7종: current/warehouse/optimal/safety/dead-stock/loss-rate/monthly-io
- 동적 컬럼 (조회유형마다 헤더 재구성)
- /api/reports/stock-status?view={viewType}

### SalesSummaryPage.razor
- 조회유형 11종 (월계표/기간별/품목별/업체별/집계표/사원별/견적대비/단가변동/매출반품/종합/신규업체)

### CashbookPage.razor
- /accounting/cashbook
- Window Function 누적 잔액
- 신규 거래 등록 (월마감 체크)

### MdbMigration.razor — 별도 `23_MdbMigration_Razor_전수정독.md`

## 2. 헌법 #14 (Razor raw string 금지) 검증

**결과: 0건 위반 ✅** — 107개 모두 `"""..."""` 미사용.

## 3. 한 화면 완결 원칙 (사장님 격언)

| 패턴 | 페이지 | 평가 |
|---|---|---|
| 목록+다이얼로그 통합 | Users, Permissions, PartnerSpecialPrices | ✅ 한 화면 |
| 목록→상세 페이지 분리 | Items→ItemDetail, Partners→PartnerDetail, Bom→BomDetail | ⚠️ 의도적 (편집 영역 크기) |
| Dashboard 단일 화면 | Dashboard | ✅ 스크롤 없이 KPI 확인 |

## 4. MudBlazor 컴포넌트 사용 빈도

| 컴포넌트 | 사용 페이지 수 |
|---|---|
| MudTable | 90+ |
| MudButton | 105+ |
| MudDialog | 40+ |
| MudTextField/MudSelect/MudDatePicker | 80+ |
| MudPaper/MudGrid/MudItem | 105+ |
| MudIcon | 100+ |
| MudProgressLinear/MudProgressCircular | 30+ |

## 5. API 호출 매트릭스 (페이지 ↔ 서비스 ↔ 컨트롤러)

| 페이지 | 클라이언트 서비스 | 컨트롤러 |
|---|---|---|
| Dashboard | FinanceClientService | FinanceController.GetDashboard |
| Items | ItemMasterService + BomService.GetAlertsAsync | ItemController + BomController |
| Users | UserService | UserController |
| BomDetail | BomService | BomController |
| Stock | (Http 직접) /api/reports/stock-status | ReportController |
| CashbookPage | FinanceClientService | FinanceController |
| MdbMigration | (Http 직접) /api/migration/legacy-mdb/* | MigrationController |

## 6. 보안 패턴

- WO-4 비밀번호 마스킹: 입력 마스킹 기본값
- WO-20260430-9 Step-up: 민감 작업 2차 인증
- HitPanApiAuthHandler: 403 정직한 권한 알림 (헌법 #19)

## 7. 사장님 헌법 페이지 매핑

- 헌법 #19 (그냥 되어야): 모든 페이지 errors 0 + warnings 0
- 헌법 #20 (워크플로우 끊김 금지): BomDetail 자동 사슬, Sales/Purchase 확정 흐름
- 헌법 #25 (쉽게·정확하게·안전하게): UX 직관성·정확한 구현·격리 설계
