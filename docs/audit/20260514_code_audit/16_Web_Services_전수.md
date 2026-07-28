# 16. HitPan.Web 클라이언트 전수 학습서 (39개 Services)

**작성:** 프론트 매니저
**범위:** `src/HitPan.Web/Services/` + `Layout/` + `Models/` + `wwwroot/index.html`

## 0. Services 파일 39개

IAuthService, AuthService, IAuthTokenRefresher, AuthTokenRefresher, HitPanProtectedLocalStorage, HitPanApiAuthHandler, HitPanRoleHelper, TenantProfileService, SpecialPriceService, PermissionService, QuotationService, SettingsService, WorkTabService, MonthlyClosingService, HrClientService, FinanceClientService, UserService, ESignService, LaborContractService, ChatbotService, TaxInvoiceApiService, ApprovalService, ItemMasterService, BomService, EmployeeService, LeaveRequestService, PositionService, ApprovalLineService, BackupService, BillsCardsBankService, EmailClientService, BillingService, LogService, DocumentService, DeliveryService, BackofficeService, AccountService, PartnerMasterService, CollectionPaymentService.

## 1. 핵심 서비스

### AuthService
- LoginAsync → api/auth/login (기기 지문 + 디바이스 등록)
- RefreshAsync → api/auth/refresh
- LogoutAsync → api/auth/logout (자동 퇴근)
- 세션 저장: HitPanProtectedLocalStorage

### HitPanProtectedLocalStorage
- AES 미지원 (Blazor WASM) → JSON + Base64 + JS interop
- 키: hitpan_access_token, hitpan_refresh_token, hitpan_user_name

### HitPanApiAuthHandler (DelegatingHandler)
- Bearer 토큰 자동 주입 (login/refresh 제외)
- 403 응답 → "이 기능에 접근할 권한이 없습니다" (헌법 #19 정직)

### WorkTabService
- 최대 5개 탭 (다중 작업 동시)
- 문서 종류: Quotation, SalesOrder, SalesDelivery, PurchaseOrder, PurchaseReceipt, Return

### DeliveryService (27개 메서드 — 최대)
- 견적·수주·거래명세서·매입·발주·반품 통합
- DailyCounter (로컬 일일 카운터)
- BulkConfirm: 성공/실패 건별 분리
- CancelConfirmed: 역원장 (헌법 #20)

### BackofficeService (22 메서드)
- 인증·본사 대시보드·고객사·대리점·정산·대리점 포털
- BuildQs(params tuple) 쿼리스트링 헬퍼

## 2. DI 등록 38개 (Program.cs L32-L84, Scoped)

(전수: 1)MudServices 2)AuthorizationCore 3)HitPanProtectedLocalStorage 4)IAuthTokenRefresher 5)HitPanAuthStateProvider 6)AuthenticationStateProvider 7)IAuthService 8)WorkTabService 9)DeliveryService 10)QuotationService 11)SettingsService 12)PartnerMasterService 13)ItemMasterService 14)BomService 15)PermissionService 16)UserService 17)EmployeeService 18)PositionService 19)ApprovalLineService 20)BillingService 21)BackupService 22)LogService 23)BillsCardsBankService 24)EmailClientService 25)LeaveRequestService 26)AccountService 27)DocumentService 28)SpecialPriceService 29)ApprovalService 30)TaxInvoiceApiService 31)CollectionPaymentService 32)MonthlyClosingService 33)FinanceClientService 34)HrClientService 35)ESignService 36)LaborContractService 37)ChatbotService 38)BackofficeService + 39)HitPanApiAuthHandler(Transient) + HttpClient(Scoped 팩토리) + TenantProfileService.

## 3. Sidebar 메뉴 구조 — 11 그룹 / 100+ 항목

(상세 메뉴는 보고서 원본 §2 참조)

- 1. 로고 → /dashboard
- 2. 계정관리: 회사·직원·권한·결재·직급·기기·환경
- 3. 그룹웨어: 결재(대기·발송·완료)·사원·HR현황·근태·휴가·연차·경비·근로계약·전자서명
- 4. 업체관리: 마스터·특별단가·원장
- 5. 상품관리: 마스터·BOM·특별단가·원장
- 6. 판매관리: 견적·수주·거래명세서·판매현황·순위·수익성·통계
- 7. 매입관리: 발주·매입·반품·각종 현황·순위·통계
- 8. 계산서관리: 전자세금계산서 발행·인증서·통계
- 9. 재고관리: 현황·수불부·실사·이송·창고·분리
- 10. 회계관리: 수금·지급·현금출납·매입매출·VAT·경비·손익·어음·카드·은행·계정과목·월마감·세무사
- 11. 자료관리: 백업·MDB이관·양식·이메일·로그

## 4. 결재 대기 카운트 배지 (실시간)

```csharp
_approvalPendingCount = await ApprovalService.GetPendingAsync().Count
// MudChip Color.Error 표시
```

## 5. index.html (헌법 #21)

### ApiBaseUrl 우선순위
1. builder.Configuration["ApiBaseUrl"] (appsettings.json)
2. 환경변수 HitPan__ApiBaseUrl
3. 환경변수 ApiBaseUrl
4. 기본값 http://localhost:5257

### HttpClient
- BaseAddress = apiUri
- Timeout = 10분 (2026-05-13 핫픽스, MDB 마이그 대응)

### CSS 로드 순서
1. Pretendard (cdn.jsdelivr.net)
2. JetBrains Mono / Noto Sans KR / Material Icons
3. MudBlazor.min.css
4. design-tokens.css → hp-toss.css → app.css → hitpan.css (사장님 결재 2026-04-29 토스 B)
5. HitPan.Web.styles.css

### JS 모듈
- storage.js (hitpanStorage_set/get/remove)
- device-fingerprint.js (hitpanDevice.getFingerprint/getDeviceType/setDeviceId)
- hitpan-shortcuts.js (단축키)
- chatbot-drag.js (챗봇 드래그)
- hitpan-keyboard-nav.js (Enter→다음 칸, 그리드 ↑↓ 행 이동)

### 카카오 우편번호 API
- daum.Postcode oncomplete → dotNetRef.invokeMethodAsync('OnAddressSelected', zonecode, address)

### Service Worker 강제 해제 (핫픽스 2026-05-13)
```js
if ('serviceWorker' in navigator) {
  navigator.serviceWorker.getRegistrations().then(rs => rs.forEach(r => r.unregister()));
}
if (window.caches) { caches.keys().then(keys => keys.forEach(k => caches.delete(k))); }
```

## 6. Models 25개 파일 — 주요

- AuthModels (LoginRequestDto, LoginApiResponse, AuthStorageKeys)
- PermissionModels (UserPermissionModel, MenuPermissionModel)
- WorkTabModels (WorkDocumentKind enum, WorkTabState)
- TenantMeClientDto, SettingsModels, PartnerModels, QuotationModels, HrModels, UserModels, ESignModels, ChatbotModels, BomModels, ItemModels, ApprovalModels, EmployeeModels, PositionModels, ApprovalLineModels, BillingModels, BackupModels, BillsCardsBankModels, EmailModels, DeliveryModels, FinanceModels
- Backoffice/BackofficeModels (AdminDashboardData, AdminTenantListItem, ResellerDetail, SettlementListItem)
- PagedResponse<T> (TotalCount, TotalPages, Items)

## 7. 핵심 패턴 검증

| 항목 | 표준 | 검증 |
|---|---|---|
| HttpClient 타임아웃 | 10분 | ✅ |
| 토큰 저장 | HitPanProtectedLocalStorage | ✅ |
| 기기 지문 | JS interop (선택) | ✅ |
| 에러 처리 | 빈 catch 금지 (#15) | ✅ |
| 403 처리 | 정직한 권한 알림 (#19) | ✅ |
| 세션 관리 | AuthTokenRefresher + AuthStateProvider | ✅ |
| 문서 다운로드 | 일시 토큰 (2시간 유효) | ✅ |
| 결재 로깅 | ILogger 사용 | ✅ |
