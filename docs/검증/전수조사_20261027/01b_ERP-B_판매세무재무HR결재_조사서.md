# 조사서 01b — ERP-B (판매·세무·재무·HR·결재·지원) · 전수조사 20261027

## §0 [카드]

| 칸 | 값 |
|---|---|
| 조사 범위 | **조사 39 / 분모 39 · 미조사 0** — 단, 대부분 행은 「화면→API 컨트롤러」까지만 직접 읽었고 서비스 SQL(DB 계층)은 미독 ⇒ ②🟡(미독) 처리(§E-1) |
| ⓐ 사장 확인 | **통과 3 / 39** (`/approval` · `/hr/payroll` · `/accounting/payroll` — 1.3.23 실측 · 셋 다 「이후 변경 있음/미확인」) |
| ⓑ 코드리뷰 | **30.0** (①⬜ 35행 기준 · 조사된 35행) |
| ⓒ 종합 | **29.2** (전체 39행 기준 · 합 1140) |
| 한 줄 결론 | 베타에 가장 먼저 막히는 것 = **판매 흐름 인쇄 반려 미봉합**(10/26 작1 반려 ③ — 견적서·수주서·거래명세서 인쇄가 양식을 안 탄다 · 봉합 차수 작3 미착수). 데이터 흐름(견적→수주→명세서 확정→재고·회계→계산서)은 코드로 이어져 있다 |
| 두 번째 | **전자세금계산서 국세청 전송 고리 0** — 발행은 내부 기록뿐(`etax_status='pending'` 고정 · 전송 제공자 소비처 0) |
| 기준판 · 조사자 · 읽은 파일 수 | `b9f34ccd` · hp-verifier 01b · 약 70개(페이지 38 머리·주입 줄 · 클라이언트 서비스 14 · 컨트롤러 18 · SalesService·QuotationService·TaxInvoiceService·DirectHometaxProvider 부분 · 출하 DDL 2구간 · DB-115 · 문서 6) |

## §A [기능목록]

점수 = §4 + §11 순서(나쁜 쪽 먼저) 기계 계산. ②🟡(미독) = 화면→컨트롤러까지 읽음·서비스 SQL 미독(반쪽 확인이 아니다 · §E-1).

| 기능 | ① 사장 실측 (근거 주소) | ② 구현 (파일:줄) | ③ 연결 (파일:줄 · 다음 칸까지 흐르나) | 점수 | 출처 |
|---|---|---|---|---|---|
| **흐름 · 판매** 견적→수주→명세서→계산서 (+재고·원장·회계) | 🔴 10/26 작1 반려 ③ 견적서·수주서·거래명세서 인쇄가 양식 미적용 · 미봉합(`docs/운영기록/반려대장.md` §1 · 작2 도 반려 R-1~R-5) | 🟢 견적→수주 `QuotationService.cs:294`·INSERT sales_orders `:328` · 수주→명세서 `SalesService.cs:1481` · 확정은 draft 만 `SalesService.cs:403-406` · 0원 확정 금지 `:408` · 재고 `:507` · 회계 `:665` RecordSalesConfirmAsync · 계산서는 confirmed 만 `TaxInvoiceService.cs:78` · INSERT `:149` | 🟢 ERP 안에서는 흐른다(위 줄 순서) · 🔴 국세청 전송은 끊김(아래 `/tax-invoice` 행) · 메뉴 견적서·수주서·거래명세서는 Href 없는 탭 열기(Sidebar.razor 판매관리 묶음) | **0** | 🟢 |
| `/quotation-status` 견적현황 | ⬜ | 🟡(미독) `QuotationStatusPage.razor:117` api/reports/quotations → `ReportController.cs:22` | 🟡 | 30 | 🟢 |
| `/sales-order-status` 수주현황 | ⬜ | 🟡(미독) `SalesOrderStatusPage.razor:117` → `ReportController.cs:45` | 🟡 | 30 | 🟢 |
| `/sales/summary` 판매현황 | ⬜ | 🟡(미독) `SalesSummaryPage.razor:132` api/reports/sales → ReportController(해당 메서드 줄 미확인) | 🟡 | 30 | 🟡 |
| `/sales/ranking` 판매순위표 | ⬜ | 🟡(미독) `SalesRankingPage.razor:118` → `ReportController.cs:187` | 🟡 | 30 | 🟢 |
| `/sales/profitability` 판매수익성분석 | ⬜ | 🟡(미독) `SalesProfitabilityPage.razor:116` → `ReportController.cs:210` | 🟡 | 30 | 🟢 |
| `/sales/statistics` 판매통계 | ⬜ | 🟡(미독) `SalesStatisticsPage.razor:147` → `ReportController.cs:233` | 🟡 | 30 | 🟢 |
| `/sales/tax-invoice-history` 세금계산서 발행이력 | ⬜ | 🟢 `TaxInvoiceHistoryPage.razor:107` api/sales/tax-invoices → `TaxInvoiceController.cs:105` → `TaxInvoiceService.cs:230` ListAsync(SQL `:247-248`) | 🟡 etax_status 는 늘 pending 으로 보일 것(전송 고리 0 · `TaxInvoiceService.cs:156`) | 50 | 🟢 |
| `/tax-invoice` 전자세금계산서 발행 | ⬜ | 🟢 페이지 주입 TaxInvoiceApiService(api/sales/tax-invoices) → `TaxInvoiceController.cs:27` → `TaxInvoiceService.cs:46` IssueAsync · INSERT tax_invoices `:149-156`(status issued · etax_status pending) | 🔴 국세청 전송 끊김 — `ITaxInvoiceProvider` 등록 `Program.cs:211` 뿐 · 소비처 0(구현 `DirectHometaxProvider.cs:27` 만) · Sidebar 「전자세금계산서 관리」·「홈택스 연동 설정」 Disabled 「준비 중」 | 10 | 🟢 |
| `/tax/certificate` 범용인증서 관리 | ⬜ | 🟡(미독) `CertificatePage.razor:136` api/certificates → `CertificateController.cs:12` ITenantCertificateService | 🟡 #5 암호화 미확인(§D-5) | 30 | 🟢 |
| `/tax-invoice-stats` 세금계산서 통계 | ⬜ | 🟡 `TaxInvoiceStatsPage.razor:172` 이 `GetPurchaseSalesLedgerAsync` 를 부른다 → `FinanceController.cs:53` purchase-sales-ledger — **tax_invoices 표를 안 읽는 것으로 보임**(§D-1) | 🟡 | 30 | 🟢 |
| `/accounting/ledger` 회계장부 | ⬜ | 🟡(미독) `TrialBalancePage` → AccountService api/finance/trial-balance → `FinanceController.cs:156` | 🟡 출하 DDL 에 DB-35 키 없음(§B-3) · DB-115 라벨 UPDATE(§D-3) | 30 | 🟢 |
| `/accounting/integrity` 정합성 검사 | ⬜ | 🟡(미독) `IntegrityCheckPage.razor.cs:57` api/finance/integrity-check → `FinanceController.cs:115` | 🟡 | 30 | 🟢 |
| `/collections` 수금 | ⬜ | 🟡(미독) CollectionPaymentService api/collections → `CollectionController.cs:26·59` · 수금 취소 역분개 `SalesService.cs:1330` | 🟡 | 30 | 🟢 |
| `/payments` 지급 | ⬜ | 🟡(미독) api/payments → `CollectionController.cs:84·95` | 🟡 | 30 | 🟢 |
| `/accounting/cashbook` 현금출납장 | ⬜ | 🟡(미독) FinanceService api/finance/cashbook → `FinanceController.cs:21·30` | 🟡 | 30 | 🟢 |
| `/accounting/purchase-sales` 매입매출장 | ⬜ | 🟡(미독) → `FinanceController.cs:53` | 🟡 | 30 | 🟢 |
| `/accounting/vat` 부가세 신고자료 | ⬜ | 🟡(미독) → `FinanceController.cs:64` | 🟡 | 30 | 🟢 |
| `/accounting/payroll` 급여 관리 | 🟢 1.3.23 실측 항목 2·4 원문 인용(`docs/운영기록/20260826인계2_급여화면분리_1323게시_실측통과_인수인계서.md` §5) · 이후 변경 **있음**(`PayrollManagePage.razor` 1커밋 · `209f225f..b9f34ccd`) | 🟡(미독) PayrollService api/payroll/slips·context → `PayrollController.cs:119·171·185` | 🟡 ⇒ ⬜ 줄로 | 30 | 🟢 |
| `/accounting/expenses` 경비 처리 | ⬜ | 🟡(미독) → `FinanceController.cs:77·87` | 🟡 | 30 | 🟢 |
| `/accounting/profit` 손익 현황 | ⬜ | 🟡(미독) → `FinanceController.cs:126` | 🟡 | 30 | 🟢 |
| `/accounting/bills` 어음 관리 | ⬜ | 🟡(미독) BillsCardsBankService → `BillsCardsBankController.cs:25·36` | 🟡 | 30 | 🟢 |
| `/accounting/card-payments` 카드 결제 | ⬜ | 🟡(미독) → `BillsCardsBankController.cs:53·70` | 🟡 | 30 | 🟢 |
| `/accounting/bank-transactions` 은행 거래내역 | ⬜ | 🟡(미독) → `BillsCardsBankController.cs:79·92` | 🟡 | 30 | 🟢 |
| `/accounting/accounts` 계정과목 | ⬜ | 🟡(미독) AccountService → `FinanceController.cs:166·175` | 🟡 | 30 | 🟢 |
| `/accounting/monthly-closing` 월마감 | ⬜ | 🟡(미독) MonthlyClosingService → `MonthlyClosingController.cs:11·31` | 🟡 | 30 | 🟢 |
| `/accounting/export` 세무사 자료 보내기 | ⬜ | 🟡 반쪽 확인 — 보내기 미구현 안내 `TaxExportPage.razor:305-331` · 세무사 정보·이력 브라우저 저장 `:257-260`·`:335-353` · 내려받기 경로 줄 미확인 | 🟡 | 30 | 🟢 |
| `/hr/departments` 부서 관리 (관리자만 노출) | ⬜ | 🟡(미독) DepartmentService api/departments → `DepartmentController.cs:17` | 🟡 | 30 | 🟢 |
| `/hr/payroll` 급여 조회 | 🟢 1.3.23 항목 1 「본인 것만」(같은 인계2 §5) · 이후 변경 **있음**(`PayrollPage.razor` 1커밋) | 🟡(미독) PayrollService → `PayrollController.cs:119` | 🟡 ⇒ ⬜ 줄로 | 30 | 🟢 |
| `/hr/attendance` 근태 관리 | ⬜ | 🟡(미독) HrService api/hr/attendance → `HrController.cs:20` | 🟡 | 30 | 🟢 |
| `/hr/overtime` 초과근무 | ⬜ | 🟡(미독) → `HrController.cs:99·108` | 🟡 | 30 | 🟢 |
| `/hr/leave` 연차신청 | ⬜ | 🟡(미독) LeaveRequestService → `LeaveRequestController.cs:12` | 🟡 | 30 | 🟢 |
| `/hr/absence` 휴직신청 | ⬜ | 🟡(미독) AbsenceService → `AbsenceController.cs:21` | 🟡 | 30 | 🟢 |
| `/hr/reports` 보고서관리 | ⬜ | 🟡(미독) WorkReportService → `WorkReportController.cs:23` | 🟡 | 30 | 🟢 |
| `/hr/expense-request` 경비처리(신청) | ⬜ | 🟡(미독) → `HrController.cs:167·192` | 🟡 회계 경비(`/accounting/expenses`)와 잇는 고리 미확인 | 30 | 🟢 |
| `/hr/labor-contracts` 입사/퇴사 | ⬜ | 🟡(미독) LaborContractService → `LaborContractController.cs:15` | 🟡 | 30 | 🟢 |
| `/approval` 결재관리 | 🟢 1.3.23 항목 3 「결재 승인 팝업 → 회계 화면」(급여 결재 한정 · 같은 인계2 §5) · 이후 변경: 페이지 0커밋 · API·서비스 미확인 ⇒ 나쁜 쪽(변경 있음) | 🟡(미독) ApprovalService api/approval/* → `ApprovalController.cs:83·104·159` | 🟡 ⇒ ⬜ 줄로 | 30 | 🟢 |
| `/support/cs` 고객지원 요청 | ⬜ 10/14 「합격!!!」 은 백오피스 고객지원센터·A/S창구 판정(`docs/운영기록/20261014인계1_작1작2_고객지원센터_AS창구_합격_다음ERP수정_인수인계서.md:16`) · ERP 화면 판 SHA 미확정 ⇒ ⬜(§4) | 🟡(미독) `CsRequestPage.razor:148` api/cs/requests → `CsRequestController.cs:174·270`(IDbConnection 직접) | 🟡 | 30 | 🟢 |
| `/support/notice` 본사공지사항 | ⬜ 위와 같음 | 🟡(미독) `CsNoticePage.razor:180` → `CsRequestController.cs:270` | 🟡 | 30 | 🟢 |

## §B [베타작업]

| 항목 | 종류 | 근거 | 규모 | 선후 |
|---|---|---|---|---|
| B-1 판매 문서(견적서·수주서·거래명세서) 인쇄가 양식을 타게 | 막는 반려(미봉합) | §A 흐름 행 · 반려대장 §1 작1 ③ · R-5(main 에 작2 코드 0) | M | 01a 인쇄양식 · 작3([1-V]부터 · 바닥 `29d12280`) 먼저 |
| B-2 전자세금계산서 국세청 전송 — 베타에 넣나 결재 | 막는 결재 · 끊긴 고리 | §A `/tax-invoice` · `Program.cs:211` · `TaxInvoiceService.cs:156` | L(넣으면) / S(빼면 화면 문구 정리) | 사장님 결재 → 법무팀장(전자세금계산서 법령) |
| B-3 출하 DDL 에 DB-35 분개 라인 중복방지 키가 없는데 시드는 적용됨으로 기록 | 반쪽 | `installer/hitpan_db_clean.sql:1949-1969`(키 없음) · `:3663`(DB-35 clean-ddl 1) · `DB-35_journal_lines_unique.sql:7` | S | #36 · 04 ERP 온보딩(신규설치)과 함께 · 의도된 대체(`uq_jl_source`)인지 먼저(§D-2) |
| B-4 ①⬜ 35행 사장님 실측 0 | 기록 없음 | §A ①⬜ 행 | M | B-1 뒤 판매 흐름부터 고객 루틴(터널)으로 |
| B-5 ②(미독) 33행 서비스 SQL 재조사 | 기록 없음 | §E-1 | M | hp-judge 반증 때 |

## §C [제안] (전부 🔵 · 채택은 사장님)

**C-1 수정보완**
- ERP 매니저 「3분 안에 쓰나」 — 계산서관리에 Disabled 「준비 중」 2줄이 발행 메뉴 바로 밑에 있다 ⇒ 처음 쓰는 직원이 「발행은 됐는데 국세청에 갔나」를 모른다. 발행 화면에 「국세청 전송 전」 표시 한 줄 제안.
- ERP 매니저 — 세무사 자료 보내기의 [보내기]가 「준비 중」 안내만 띄운다(`TaxExportPage.razor:323-325`). 버튼 대신 [내려받기]를 앞에 세우는 안.
- 김삼성 상무 「실제 인사팀이 이렇게 일하나」 — 그룹웨어 급여는 조회만(사장님 메모 「지금 하지마」 · 인계2 §6) — **착수 금지 상태 그대로 둔다** · 등재만.
- 김삼성 상무 — 직원 경비신청(`/hr/expense-request` 「경비처리」)과 회계(`/accounting/expenses` 「경비 처리」) 이름이 거의 같다 ⇒ 직원 메뉴를 「경비신청」으로 구분하는 안.
- CS팀장 강지원 — 세금계산서 통계가 계산서 표가 아닌 매입매출장을 읽는다면 숫자가 발행이력과 달라 문의가 생긴다(§D-1 확인 뒤).
- 개발팀 — 세무사 연락처·보낸 이력이 브라우저 저장소(`TaxExportPage.razor:257-260`)라 PC·브라우저가 바뀌면 사라진다.

**C-2 추가기능**
- 기술영업 「이게 팔리냐」 — 국세청 전송(또는 외주 연동)이 판매 포인트. 범위는 B-2 결재.
- 개발팀 — 정합성 검사(`FinanceController.cs:115`)에 출하 DDL 키 유무 점검 항목 추가.

## §D [GPT]

| # | §A 행 | 파일:줄 (`b9f34ccd`) | 못 정한 이유 | GPT 에게 묻고 싶은 것 |
|---|---|---|---|---|
| D-1 | 세금계산서 통계 | `src/HitPan.Web/Pages/Sales/TaxInvoiceStatsPage.razor:172` | 통계를 tax_invoices 가 아니라 매입매출장에서 만드는 것으로 보이나 그 뒤 계산을 안 읽었다 | 「발행된 계산서」 통계가 미발행 명세서까지 세는가? |
| D-2 | 회계장부 | `installer/hitpan_db_clean.sql:1949-1969` · `:3663` · `src/HitPan.API/Migrations/SQL/DB-35_journal_lines_unique.sql:7` | `uk_journal_line_dedup` 이 출하본에 없고 `uq_jl_source` 만 있다 — 의도된 대체인지 누락인지 | 신규 설치 DB 에서 같은 분개 라인이 두 번 들어갈 길이 있는가? |
| D-3 | 회계장부 | `src/HitPan.API/Migrations/SQL/DB-115_sales_return_journal_source_type.sql:40` | journal_entries 라벨 UPDATE — 주석은 「lines 무접촉이라 #3 밖」 | #3 INSERT ONLY 를 entries 머리까지로 읽어야 하나? |
| D-4 | 전자세금계산서 발행 | `src/HitPan.API/Program.cs:211` · `src/HitPan.Application/Services/TaxInvoiceService.cs:149-156` | 전송 제공자는 등록됐는데 부르는 코드 0 — 다른 경로(배치·백오피스)가 있는지 다 못 셌다 | etax_status 를 pending 밖으로 바꾸는 코드가 어디든 있는가? |
| D-5 | 범용인증서 관리 | `src/HitPan.API/Controllers/CertificateController.cs:12` | 인증서·비밀번호 저장 SQL 을 안 읽었다 | 인증서 비밀번호가 #5 암호화 컬럼으로 저장되는가? |

## §E [한계]

1. **DB 계층 미독** — 33행은 페이지 주입·호출 줄 → 클라이언트 서비스 api 경로 → 컨트롤러 Route/Http 줄 → 주입 인터페이스 이름까지만 읽었다. 서비스 구현 SQL 은 SalesService·QuotationService·TaxInvoiceService 일부만. 그래서 ②🟡(미독) = 30점이며 「반쪽 확인」이 아니다. 조사 시간 상한으로 끝냄.
2. 테넌트: 읽은 컨트롤러는 `HttpContext.Items["TenantId"]`(예 `FinanceController.cs:25` · `CollectionController.cs:31`) · tenant 쿼리 파라미터 수신 grep 0 — 미들웨어가 JWT 에서만 채우는지는 안 읽었다(#2).
3. #4: 금액 칸 double/float grep 0(차트 표시용 캐스트 2건만 · AI 도구 파일). #6: 판매 확정만 원장 반영 위치 확인(위 줄) · 매출반품·수금은 줄 위치만.
4. 사장님 기록 원천은 반려대장·진실원 grep·운영기록 grep 만. 75개 파일 전부 대조하지 않았다 ⇒ ①⬜ 중 실제 통과 기록이 있는 행이 있을 수 있다.
5. 「판 뒤 변경」은 1.3.23 머지 `209f225f`(인계2 표) 기준으로 페이지·PayrollController 만 셌다. 고객지원은 10/14 판 SHA 를 못 찾아 ①⬜(§4).
6. 분모에 넣지 않은 것(등재만): Href 없는 탭 메뉴 견적서·수주서·거래명세서(흐름 행에 포함) · Disabled 「전자세금계산서 관리」·「홈택스 연동 설정」 · `/settings/approval`·`/settings/tax-invoice-cert`(첫 조각 settings ⇒ 01a) · `/data/update`·`/data/rollback`(지원센터 묶음 안이지만 첫 조각 data ⇒ 01a) · `/chat`(06) · 반품확인서·반품확인현황 메뉴 제거분(Sidebar 판매관리 주석 · 화면·API 잔존).
7. 7영역 밖: 없음.
8. 순서 가정: §11 조건 1 「나쁜 쪽이 먼저」 — 흐름 행은 데이터 흐름 ②🟢③🟢(80 상당)이나 ①🔴(인쇄 반려)이 이겨 0 · `/approval` 은 일부 파일 변경 미확인을 「변경 있음」으로 계산.
