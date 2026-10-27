# 조사서 01a — ERP-A (설정·마스터·매입·재고) · 전수조사 20261027

## §0 [카드]

| 칸 | 값 |
|---|---|
| ⓐ 사장 확인 | 통과 **6 / 40** (①🟢 행 6 · ①🔴 행 1 · 나머지 ⬜) |
| ⓑ 코드리뷰 | **61.7** — 조사된 행 중 ①⬜ **6행** 기준(80·80·80·30·50·50) |
| ⓒ 종합 | **74.6** — 조사된 **13행** 기준(합 970 / 13) · 미조사 27행은 점수 없음(분모에 남김) |
| 조사 범위 | **조사 13 / 분모 40 · 미조사 27** |
| 한 줄 결론 | 베타에 가장 먼저 막히는 것 = **양식(인쇄)정보 설정** — 10/26 사장님 「모두 반려」 3건 미봉합 · 봉합하려던 작2 코드는 main 에 0건(반려대장 R-5) ⇒ 발주서·매입처리·반품처리 인쇄도 같이 막힌다 |
| 기준판 · 조사자 · 읽은 파일 수 | `b9f34ccd` · hp-verifier 01a · 직접 읽은 파일 약 30개(Sidebar · 화면 .razor 다수의 api 줄 · 컨트롤러 라우트 · `ReportService`·`PurchaseService`·`BomService` 일부 · 운영기록 6) |
| 시간 | 상한 도달로 중단 — DB 층(서비스 SQL)을 못 읽은 행은 「미조사」 |

## §A [기능목록]

분모 출발점 = `src/HitPan.Web/Layout/Sidebar.razor`(`b9f34ccd`) 의 `Href=` 중 첫 조각이 루트·settings·users·company·employees·print-settings·data·partners·items·bom·purchase·purchase-status·purchase-order-status·return-status·stock 인 것 38행 + 흐름 2행. `/users` 는 48·75줄 두 곳이라 한 행. 루트(`/`)는 Href 가 아니라 브랜드 클릭(`Sidebar.razor:31` → `/dashboard`, `Dashboard.razor:1-2` 가 `/`·`/dashboard` 둘 다 받음)이라 한 행으로 셌다.

| 기능 | ① 사장 실측 (근거 주소) | ② 구현 (파일:줄) | ③ 연결 (파일:줄) | 점수 | 출처 |
|---|---|---|---|---|---|
| 1 대시보드 `/` | ⬜ | 화면 `Dashboard.razor:538` → `api/finance/dashboard` → `FinanceController.cs:11` · 서비스 SQL 미독 | — | 미조사 | 🟡 |
| 2 회사 정보 `/company` (Sidebar:47) | ⬜ | `UserInfoPage.razor.cs:205,212` → `SettingsService.cs:70,110` → `SettingsController.cs:10` · SQL 미독 · 기기등록 `UserInfoPage.razor.cs:122` 받는 문 미확인(§D-4) | — | 미조사 | 🟡 |
| 3 직원 계정 관리 `/users` (:48·:75) | 🟢 1.3.53 — `운영기록/20261005결8_작5_사원계정연결_CTO결재.md:82` 사장님 「모두 통과」(10/6) | 판 뒤 변경 0 — `git log 0e4207bc..b9f34ccd` (Users.razor · UserController · UserService) | — | **100** | 🟢 |
| 4 권한 설정 `/users/permissions` (:53) | 🟢 1.3.53 — 결8:82 · G-8 ③ 「권한설정 4선택」(결8:48) | 판 뒤 변경 0 (PermissionPage · PermissionController · PermissionService) | — | **100** | 🟢 |
| 5 접속기기 확인 `/settings/devices` (:57) | 🟢 1.3.52 — `운영기록/20261005결7_작3작4_계정과금_슬롯폐기_CTO결재.md` §5-1 G-6 사장님 「오케이 통과」 | 판 뒤 변경 0 — `git log 761e1ffe..b9f34ccd` (DeviceManagePage · DeviceController · AccessStatusController · TenantDeviceService) | — | **100** | 🟢 |
| 6 사용환경 설정 `/settings` (:58) | ⬜ | `Settings.razor` → `SettingsService.cs:28,43` → `SettingsController.cs:10` · SQL 미독 | — | 미조사 | 🟡 |
| 7 양식(인쇄)정보 설정 `/print-settings` (:61) | 🔴 반려 — `운영기록/반려대장.md` 10/26 작1 「모두 반려」 ①②③ · 봉합 차수 작2 도 ❌(R-1~R-5) · main 에 작2 코드 0 | `PrintSettingsPage.razor:336,347,389,512` (main 판) | 반려 ③: 여섯 화면 인쇄가 양식을 안 탐 | **0** | 🟢(①) |
| 8 양식 템플릿 `/settings/form-templates` (:62) | ⬜ | `FormTemplatesPage.razor:130,148,185` → `FormTemplateController.cs:11` · SQL 미독 | — | 미조사 | 🟡 |
| 9 이메일(SMTP) 설정 `/settings/email` (:63) | ⬜ | `EmailClientService.cs:10,16` → `EmailController.cs:13` · SQL 미독 | — | 미조사 | 🟡 |
| 10 AI 도우미 연동 `/settings/ai-assistant` (:67) | ⬜ | `ChatbotService.cs:117,141` → `AiSettingsController.cs:14` · SQL 미독 | — | 미조사 | 🟡 |
| 11 사원관리 `/employees` (:98) | 🟢 1.3.53 — 결8:82 · G-8 ② 사원관리 「계정」 칩 | 판 뒤 변경 0 (EmployeePage .razor·.cs·AccountLink · EmployeeController · EmployeeService) | — | **100** | 🟢 |
| 12 결재 설정 `/settings/approval` (:126) | ⬜ | `ApprovalSettingsPage.razor` → `ApprovalService.cs:43,49` → `ApprovalController.cs:11` · SQL 미독 | — | 미조사 | 🟡 |
| 13 업체 마스터 `/partners` (:228) | ⬜ | `PartnerMasterService.cs:32,65` → `PartnerController.cs:11` · SQL 미독 | — | 미조사 | 🟡 |
| 14 업체 특별단가 `/partners/special-prices` (:229) | ⬜ | `SpecialPriceService.cs:23,39` → `PartnerController.cs:11` · SQL 미독 | — | 미조사 | 🟡 |
| 15 업체별 원장 `/partners/ledger` (:230) | ⬜ | 🟡 `PartnerLedgerPage.razor:115` → `ReportController.cs:302-322` → `ReportService.cs:2540-2555` — 업체마다 **합계 1줄**(GROUP BY) · 거래 이력 행 0 · 화면 부제 「입출고·잔액 이력」(`PartnerLedgerPage.razor:14`)과 다름 | 🟡 업체 필터가 이름 LIKE(`ReportService.cs:2552`) | **30** | 🟢 |
| 16 상품 마스터 `/items` (:235) | ⬜ | `ItemMasterService.cs:44,71` → `ItemController.cs:9` · SQL 미독 | — | 미조사 | 🟡 |
| 17 BOM 자재명세서 `/bom` (:236) | ⬜ | `BomService.cs`(Web):13 → `BomController.cs:10` → `BomService.cs`(App):40 머리만 · SQL 미독 | 흐름은 40행 | 미조사 | 🟡 |
| 18 상품 특별단가 `/items/special-prices` (:237) | ⬜ | 화면 주입 `ItemMasterService`·`PartnerMasterService` 만 확인 | — | 미조사 | 🟡 |
| 19 상품별 원장 `/items/ledger` (:238) | ⬜ | 🟢 `ItemLedgerPage.razor:132` → `ReportController.cs:302-322`(tenant = `HttpContext.Items`) → `ReportService.cs:2557-2571` `stock_ledger` | 🟢 `stock_ledger` 를 매입확정(`PurchaseService.cs:250-275`)·BOM 생산(`BomService.cs:707,722`)이 씀 | **80** | 🟢 |
| 20 발주현황 `/purchase-order-status` (:262) | ⬜ | `PurchaseOrderStatusPage.razor:121` → `ReportController.cs:91` → `ReportService.cs:601` 머리 · 본문 SQL 미독 | — | 미조사 | 🟡 |
| 21 매입현황 `/purchase-status` (:264) | ⬜ | `PurchaseStatusPage.razor:211` → `ReportController.cs:114` → `ReportService.cs:794` 머리 | — | 미조사 | 🟡 |
| 22 반품현황 `/return-status` (:266) | ⬜ | `ReturnStatusPage.razor:229` → `ReportController.cs:137` → `ReportService.cs:1127` 머리 | — | 미조사 | 🟡 |
| 23 매입순위표 `/purchase/ranking` (:267) | ⬜ | `PurchaseRankingPage.razor:119` → `ReportController.cs:256` → `ReportService.cs:1839` 머리 | — | 미조사 | 🟡 |
| 24 매입통계 `/purchase/statistics` (:268) | ⬜ | `PurchaseStatisticsPage.razor:148` → `ReportController.cs:279` → `ReportService.cs:2103` 머리 | — | 미조사 | 🟡 |
| 25 전자세금계산서 인증서 `/settings/tax-invoice-cert` (:286) | ⬜ | `TaxInvoiceCertClientService.cs:15,43` → `TaxInvoiceCertController.cs:17` · 미독 | — | 미조사 | 🟡 |
| 26 재고 현황 `/stock` (:293) | ⬜ | 🟢 `Stock.razor:124` → `ReportController.cs:324-330` → `ReportService.cs:2593,2640` `item_stock` | 🟢 `item_stock` 를 매입확정 `PurchaseService.cs:324-331` 이 올린다 | **80** | 🟢 |
| 27 각종 수불부 `/stock/ledger` (:294) | ⬜ | 🟢 `StockLedgerPage.razor:148` → 19행과 같은 문 | 🟢 19행과 같음 | **80** | 🟢 |
| 28 재고 실사·조정 `/stock/adjust` (:295) | ⬜ | 화면 `api/stock/adjust` → `StockController.cs:43,55` · 서비스 미독 | — | 미조사 | 🟡 |
| 29 재고 이송 `/stock/transfer` (:296) | ⬜ | `StockController.cs:20,68` · 미독 | — | 미조사 | 🟡 |
| 30 재고 이송 현황 `/stock/transfer-status` (:297) | ⬜ | `StockController.cs:80` · 미독 | — | 미조사 | 🟡 |
| 31 창고 관리 `/stock/warehouse-manage` (:298) | ⬜ | `api/warehouses` → `WarehouseController.cs:12`(IDbConnection 직접) · 미독 | — | 미조사 | 🟡 |
| 32 창고분리 `/stock/warehouse-split` (:299) | ⬜ | `WarehouseSplitPage.razor:92` → `StockController.cs:93` · 미독 | — | 미조사 | 🟡 |
| 33 자료 백업·복구 `/data/backup` (:338) | ⬜ — 9/13 「🟢 완료」(`운영기록/20260913인계1_…:28`)는 PM 표기 · 사장님 원문 인용 없음 | `BackupService.cs:12,54,71` → `BackupController.cs:15` · 미독 | — | 미조사 | 🟡 |
| 34 MDB 자료이관 `/settings/mdb-migration` (:339) | ⬜ | `MdbMigration.razor` `api/migration/legacy-mdb/*` → `MigrationController.cs:20` · 미독 | — | 미조사 | 🟡 |
| 35 ERP 로그 기록 `/data/logs` (:343) | ⬜ | `LogService.cs:21,40` → `LogController.cs:14` · 미독 | — | 미조사 | 🟡 |
| 36 모든데이터 초기화 `/data/reset` (:345) | ⬜ | `DataResetService.cs:35` → `DataResetController.cs:15` · 미독 | — | 미조사 | 🟡 |
| 37 수동 업데이트 `/data/update` (:400 · `LocalSwapUiText.cs:29`) | 🟢 1.3.51 — `운영기록/20261005결6_작2_되돌리기화면전개_CTO결재.md:37` *"히트판 업데이트, 롤백 =최종실측결과 모두 통과."* | 판 뒤 변경 0 — `git log db8b7181..b9f34ccd` (ManualUpdatePage · ManualUpdateController · ManualUpdateClient) | — | **100** | 🟢 |
| 38 되돌리기 `/data/rollback` (:401 · `LocalSwapUiText.cs:30`) | 🟢 1.3.51 — 결6:37 같은 원문 | 판 뒤 변경 0 (LocalRollbackPage · LocalRollbackController · LocalRollbackService · LocalRollbackClient) | — | **100** | 🟢 |
| 39 흐름 **매입** 발주→매입/반품→재고 | ⬜ — 8/27 1.3.25 반려 2건(`20260827인계2…:19-24`)·1.3.29 「9/9 통과」(`20260827인계5…:195`)는 PM 표 · 원문 인용 없음 | 🟢 메뉴 탭 `Sidebar.razor:261,263,265`(Href 없음 · TabService) · 발주→매입 `PurchaseService.cs:699` · 확정 `:193` → `stock_ledger` `:250-275` · `item_stock` `:324-331` · 분개 `:422` · 매입→반품 `:924` · 반품확정 draft 만 `:1272-1274` → `stock_ledger` `:1356` · `item_stock` `:1384` | 🟡 매입확정 수불은 **품목 단위로 합쳐 첫 줄 창고**에 기록(`:250-260`) · 재고는 **품목+창고 단위**(`:324`) ⇒ 한 전표에 같은 품목·다른 창고면 창고별 수불≠재고 | **50** | 🟢 |
| 40 흐름 **BOM** | ⬜ | 🟢 생산 `BomService.cs:514-532` 자재 부족이면 완제품 반영 금지 · 창고 3단 `:535-562` · 재고 `:684` · 수불 `:707,722` · 분개 `:743` · 분해 `:788,879,952,970` | 🟡 생산 경로에 `item_type`(반제품) 검사가 안 보인다(파일 내 `item_type` 은 `:212,263,407-429` 뿐) · 다단 BOM 처리 미확인 | **50** | 🟢 |

## §B [베타작업]

| 항목 | 종류 | 근거 | 규모 | 선후 |
|---|---|---|---|---|
| 양식(인쇄)정보 반려 3건 봉합 — 워터마크 반영 · 미리보기 값 바인딩 · 여섯 화면 인쇄가 양식을 탐 | 막는 반려 | §A 7 · 반려대장 R-1~R-5 | L | 작3 [1-V] → 바닥 = wip `29d12280`(main 아님) · 01b 견적서·수주서·거래명세서 인쇄와 같은 봉합 |
| 매입확정 수불의 창고 차원을 재고와 맞춘다(또는 의도라면 근거 기록) | 반쪽(#20 매입흐름 ③) | §A 39 · `PurchaseService.cs:250-260` vs `:324` | S | 창고 규칙(사용자지정→상품마스터→MAIN) 결재와 대조 |
| BOM 반제품 생산 사슬 판정 | 막는 결재 | §A 40 · `BomService.cs:514-562` | M | 사장님 결재(외주가공이면 정당 여부) → 설계 |
| 미조사 27행 DB 층 판독 | 기록 없음 | §A 1·2·6·8~10·12~14·16~18·20~25·28~36 | M | 01a 2차 조사 |
| ①⬜ 행 사장님 실측 대본(매입현황·재고·수불부·백업·초기화·자료이관) | 기록 없음 | §A ①칸 ⬜ 33행 | M | 고객 루틴 그대로(설치파일→라이선스키→터널) |

## §C [제안] (전부 🔵 · 채택은 사장님)

**C-1 수정보완**
- ERP 매니저 「현장 직원이 3분 안에 쓰나」 — 업체별 원장이 업체마다 합계 한 줄이라 「이 거래처 이번 달 뭐 들어왔지」를 못 본다 → 거래 한 줄씩 + 누적 잔액 보기(§A 15).
- ERP 매니저 — 발주서·매입 처리·반품 처리는 탭으로 열리고 현황은 별 화면 → 현황 행에서 원 전표 바로 열기가 세 화면 모두 같은 동작인지 점검.
- 김삼성 상무 「실제 인사팀이 이렇게 일하나」 — 사원↔계정 연결은 사장님 통과 · 퇴사 1건 시나리오를 베타 대본에 고정 유지.
- 기술영업 「이게 팔리냐」 — 상품 특별단가는 8/20 사장님 *"상품 특별단가는 존재 자체가 큰 의미가 없네"*(`운영기록/20260820_인수인계서_PC버전3건…:59`) → 메뉴 유지 여부 결재 상신.
- CS팀장 「문의가 줄어드나」 — 수불부 조회가 실패하면 빈 표로 보인다(`PartnerLedgerPage.razor:118-122` catch → 빈 목록 · 화면 안내 0) → 「조회하지 못했습니다」 문구.
- 개발팀 — 수불부 업체 필터를 이름 LIKE 대신 업체 ID 로(이름이 겹치면 섞인다 · `ReportService.cs:2552`).

**C-2 추가기능**
- ERP 매니저 — BOM 생산 부족 안내(`BomService.cs:524-530`)에서 부족 자재로 발주서 바로 만들기(반자동 · 제안값→수정→확정).
- 개발팀 — 매입·반품 확정 직후 수불·재고 합계 자기검산(같은 전표 기준) 표시.

## §D [GPT]

| §A 행 | 파일:줄 (`b9f34ccd`) | 못 정한 이유 | GPT 에게 묻고 싶은 것 |
|---|---|---|---|
| 39 매입 흐름 창고 차원 | `PurchaseService.cs:250-260` · `:324-331` | UNIQUE 키 회피(주석 `:245-248`)를 위한 의도인지 결함인지 코드만으로 못 가름 | 같은 품목을 한 매입에 두 창고로 넣고 확정하면 창고별 수불부와 재고가 어긋나는가? |
| 15 업체별 원장 | `ReportService.cs:2540-2555` · `PartnerLedgerPage.razor:14` | 6단계 「업체별원장」 요구 정의 문서를 이번에 안 읽음 | 합계표(GROUP BY 업체)가 「원장」 요구를 채우는가, 거래 이력·잔액이 빠졌는가? |
| 40 BOM 반제품 | `BomService.cs:514-562` · `:684-743` | 다단 BOM·반제품 검사 경로를 끝까지 못 읽음 | 반제품을 자재로 쓰는 BOM 생산에서 재고·분개가 이중 또는 누락되는 경로가 있는가? |
| 2 회사 정보 기기등록 | `UserInfoPage.razor.cs:122` | 컨트롤러 `[Route]` 목록에 `api/landing` 이 없다 · 액션 단위 경로는 안 셌다 | `api/landing/device/register` 를 받는 문이 `b9f34ccd` 에 있는가? |
| 3·4·5·11·37·38 「판 뒤 변경 0」 | 판 SHA `0e4207bc`·`761e1ffe`·`db8b7181` | 태그 없음 · 판 SHA 를 운영기록에서 가져옴 · 공용 미들웨어·DB 마이그는 안 셌다 | 이 SHA 가 게시 판과 같은가, 그 뒤 이 화면이 쓰는 공용 코드가 바뀌었는가? |

## §E [한계]

- **미조사 27행**: §A 1·2·6·8·9·10·12·13·14·16·17·18·20~25·28~36 — 화면→API→컨트롤러 라우트까지만 읽고 서비스 SQL 을 못 읽었다(시간 상한). 점수 없음 · 분모에 남김(지시서 §11 조건 4).
- **순서 가정**: 지시서 §11-1 「나쁜 쪽이 먼저 이긴다」 그대로 · 반려 뒤 재실측 없는 행은 0(§11-2).
- **① 판정 보류**: 1.3.29 매입 「9/9 통과」·1.3.40 백업 「🟢 완료」는 PM 이 쓴 표이고 사장님 원문 인용이 없어 ⬜ 로 셌다 — 인정하면 39·33행이 올라간다 · **확인 필요(PM)**.
- 「판 뒤 변경」은 §A 에 적은 파일 묶음만 `git log` 했다(공용 미들웨어·마이그 제외).
- **메뉴에 없는 라우트**(분모 밖): `/settings/approval-lines` · `/settings/positions` · `/settings/subscription-billing` · `/settings/user-info` · `/users/permissions-old` · `/purchase` · `/purchase-orders` · `/purchases` · `/purchases/summary` · `/settings/mdb-migration/unposted` · 상세(`/items/new`·`/bom/new`·`/partners/new` 등).
- **P-2**: 인쇄양식은 main 판으로만 판정 · 작2 작업본 `583e1cbb`·`29d12280` 은 「반려된 판 · 미머지」 — 이번에 안 열었다.
- **영역 밖 등재만**: `chat` 메뉴(06) · AI 도우미 연동의 외부 AI 호출은 본사 데이터 경계(#18) 점검 대상이나 미조사 · 작업 트리 `installer/payload/HitPan.Watchdog/appsettings.json` 삭제 표시는 무접촉.
- 새 실측 0 · DB 조회 0 · 코드 수정 0 · git 쓰기 0.
