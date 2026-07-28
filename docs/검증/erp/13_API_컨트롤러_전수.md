# 13. API 컨트롤러 전수 학습서 (47개)

**작성:** 백엔드 매니저
**일자:** 2026-05-14
**범위:** `src/HitPan.API/Controllers/` 전체 + `src/HitPan.Backoffice/Controllers/`

## 0. 컨트롤러 파일 47개

### 메인 (35)
CompanyController, PermissionController, QuotationController, SettingsController, WarehouseController, StockController, ApprovalController, HrController, MonthlyClosingController, UserController, CertificateController, DeviceController, ESignController, LaborContractController, ChatbotController, HealthController, ReportController, TenantsController, DeliveryBatchController, DocumentController, TaxInvoiceController, ItemController, BomController, EmployeeController, LeaveRequestController, PositionController, ApprovalLineController, BackupController, BillsCardsBankController, EmailController, BillingController, LogController, SalesController, AuthController, LandingController

### 데이터 중심 (4)
PurchaseController, FinanceController, PartnerController, CollectionController

### 마이그레이션 (1)
MigrationController

### 기반 (1)
HitPanControllerBase

### Admin (5)
BackofficeAuthController, AdminDashboardController, AdminTenantController, AdminResellerController, AdminSettlementController

### Reseller (1)
ResellerPortalController

## 1. 총 엔드포인트 ~347개

평균 7.4개/컨트롤러. 최대 DocumentController, 최소 HealthController(1).

## 2. Policy별 분류

| Policy | 엔드포인트 수 | 용도 |
|---|---|---|
| AllowAnonymous | 10 | Auth, Landing, Health, Document 다운로드 |
| TenantAdminOnly | 47 | 설정·관리자 |
| TenantOnly | 35 | 조회·입력 일반 |
| SalesOnly | 15 | 견적/수주/매출 |
| SalesManager | 4 | 배송 수정·삭제 |
| PurchaseOnly | 15 | 발주/매입 |
| PlatformOnly | 10 | 본사 Admin |
| ResellerOnly | 5 | 대리점 |
| TenantProfile | 1 | 테넌트 정보 |

## 3. 헌법 #1 (tenant_id JWT 클레임) 검증

**전수 결과: 0건 위반 ✅** — 모든 컨트롤러가 `HttpContext.Items["TenantId"]?.ToString()` 또는 JWT 클레임 직접 검사.

검증 패턴 4종:
1. `HttpContext.Items["TenantId"]?.ToString()` — 대부분
2. `User.FindFirst("sub")?.Value` — BomController (userId)
3. `User.FindFirst("reseller_id")?.Value` — ResellerPortalController
4. `HitPanControllerBase` 상속 — Backup/BillsCardsBank/Email/Log

## 4. 헌법 #15 (빈 catch 금지) 검증

**전수 결과: 0건 ✅** — 모든 catch에 로깅.

우수 사례: MigrationController L:65-109 (7종 예외 + 사용자 친화 메시지)

## 5. [IdempotencyKey] 적용 엔드포인트 3개

| 컨트롤러 | 엔드포인트 |
|---|---|
| TaxInvoiceController | POST /api/sales/tax-invoices |
| SalesController | POST /api/sales/deliveries/{id}/confirm |
| PurchaseController | POST /api/purchase/receipts/{id}/confirm |

## 6. 핵심 컨트롤러 상세

### MigrationController (이미 정독 — `21_MigrationController_전수정독.md`)
- Route: api/migration
- Policy: TenantAdminOnly + [SupportedOSPlatform("windows")]
- 4 엔드포인트 (Preview/Migrate/Start/Status)

### SalesController (254줄)
- Route: api/sales
- Policy: SalesOnly
- 15 엔드포인트 (orders·deliveries·auto-orders·bulk-confirm·cancel)

### PurchaseController (221줄)
- Route: api/purchase
- Policy: PurchaseOnly
- 15 엔드포인트 (orders·receipts·returns + 변환·확정)

### PartnerController (268줄)
- Route: api/partners
- Policy: Authorize (방식 혼용)
- 15 엔드포인트
- `GetListPaged` — 2026-05-13 신규 페이지네이션 (헌법 #25)

### FinanceController (182줄)
- Route: api/finance
- Policy: SalesOnly + [RequirePermission ACCOUNTING:*]
- 15 엔드포인트 (cashbook·VAT·expenses·integrity-check·profit·dashboard·accounts)

### CollectionController (138줄)
- Route: api
- Policy: SalesOnly + [RequirePermission COLLECTION/PAYMENT:*]
- 9 엔드포인트

### AuthController (182줄)
- Route: api/auth
- AllowAnonymous + Authorize 혼용
- Login → 기기 등록·자동출근 / Logout → 자동퇴근 / Refresh / VerifyPassword(Step-up)

### DocumentController (653줄)
- Route: api/documents
- 다운로드 토큰 (HmacSha256, 2시간 유효)
- type별 switch: quotation, sales-order, delivery, tax-invoice, purchase-order, purchase-receipt, return

## 7. HitPanControllerBase (49줄)

```csharp
public abstract class HitPanControllerBase : ControllerBase
{
    protected string? TenantId => HttpContext.Items["TenantId"]?.ToString();
    protected string? UserId => HttpContext.Items["UserId"]?.ToString();
    protected string? AccountType => HttpContext.Items["AccountType"]?.ToString();
    protected bool IsPlatformAdmin => AccountType == "platform_admin";
    protected bool IsTenantAdmin => AccountType == "tenant_admin";

    protected IActionResult? EnsureTenant() { /* 401 if null */ }
}
```

사용 컨트롤러 4종: BackupController, BillsCardsBankController, EmailController, LogController

## 8. Backoffice 전용 Policy 검증 (PlatformOnly + 역할 체크)

| 컨트롤러 | Policy | 추가 역할 체크 |
|---|---|---|
| AdminTenantController | PlatformOnly | (없음) |
| AdminResellerController | PlatformOnly | role="super_admin" 필요한 메서드 8개 |
| AdminSettlementController | PlatformOnly | role in ("super_admin","billing_admin") 4개 |
| ResellerPortalController | ResellerOnly | reseller_id JWT 클레임 격리 |
