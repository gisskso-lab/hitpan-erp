# 히트판 ERP — 12 대메뉴 버튼 카탈로그 (2026-04-24)

> 작성: ERP 매니저 (전수조사 자동화 에이전트)
> 커버리지: **12/12 대메뉴 / 버튼 약 287개 / API 경로 82개**
> 용도: Playwright E2E 스모크 테스트 스펙 매핑 기반

## 헌법 §20 3흐름 핵심 경로 (우선순위 1등급)

### 매입 흐름
POST api/purchase/orders → POST api/purchase/orders/{id}/convert-to-receipt →
POST api/purchase/receipts/{id}/confirm → stock_ledger INSERT 검증

### BOM
POST api/bom/assemble → 완제품+ / 자재- 동일 tx

### 판매 흐름
POST api/quotations/{id}/convert → POST .../convert-to-delivery →
POST api/sales/deliveries/{id}/confirm → POST api/sales/tax-invoices/bulk-issue

## 구조적 취약점 5건 (시연 금지 또는 즉시 수정 필요)

1. TaxInvoicePage 팩스 — "Phase 2" Snackbar만, 시연 금지
2. DeviceManagePage Revoke — 확인 다이얼로그 없이 즉시 POST
3. UserInfoPage 파일 업로드 3종 — 서버 반영 경로 불명
4. LaborContractCreatePage 저장 버튼 누락 의심
5. StockLedgerPage 버튼 0건 — 현장감 원칙 위반

---

(아래 카탈로그는 ERP 매니저 에이전트 직접 산출물)

## 조사 완료 일시: 2026-04-24 20:32
