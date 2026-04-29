# 레거시 히트판 MDB → 신규 ERP 매핑 명세서 (2026-04-29)

명세서가 별도로 없어 직접 작성한 매핑 단일 진실 소스.
근거: `docs/mdb-schema-dump.md` (32 테이블 + 모든 컬럼 추출).
명명 규칙: VB6 시대의 한국어 약어 + Hungarian prefix.

---

## 명명 약어 사전 (역공학)

| 약어 | 의미 (한글) | 영문 | 비고 |
|---|---|---|---|
| BUY | 거래처/업체 | partner | buy_code = 거래처 코드 |
| PUM | 품목/품명 | item name | KA_PUM, IO_PUM |
| KU | 규격 | spec | KA_KU |
| DAN(W) | 단위 | unit | KA_DANW |
| SU(주) / QTY | 수량 | quantity | KA_SU, IO_QTY |
| DAN | 단가 | unit price | KA_DAN |
| KUM / AMT | 금액 | amount | KA_KUM, BK_AMT |
| VAT | 부가세 | VAT | KA_VAT |
| YMD / DT | 날짜 | YYYYMMDD | BK_YMD, AC_YMD |
| TM | 시각 | HHMM | AS_TM |
| GU | 구분 (1자) | type code | TX_GU = '1'/'2'/... |
| GUBUN | 구분 (2자) | sub-type | K2_GUBUN |
| SAWON | 사원 | employee | SC_SAWON |
| SUN / SEQ | 순번 | seq | KA_SUN, IJ_SEQ |
| NO | 전표/문서번호 | doc no | KA_NO, K2_NO |
| REM(1~8) | 비고/메모 | remark | KA_REM, nam_rem1 |
| JEK / JEKYO | 적요 | description | BK_JEK |
| BAL | 잔액 / 급여 | balance/salary | S_BAL, SW_PAY |
| SUK | 회수 | collection | S_SUK |
| HEUN | 흥(완납·정산?) | settled | (modACCOUNT.bas 변수) |
| SON | 손실 | loss | RT_SON |
| ABS | 절대치 | abs | RT_ABS (BOM?) |
| CHANG | 창고 | warehouse | IJ_CHANG (2자 코드) |
| TAX | 세금계산서 | tax invoice | IJ_TAXNO, S_TAX |
| BANK | 은행 | bank | BK_*, EU_BANK |
| CHE(RI) / cheri | 처리 | settlement | BK_cheri |
| HAL | 할인/할부 | discount/installment | CD_HAL, K2_HALY |
| KIDT | 기일/만기일 | due date | CD_KIDT |
| OWNER | 대표자 | top, owner | NAM_OWNER, buy_top |
| TOP | 대표 | CEO | buy_top |
| EUPTAE | 업태 | biz type | buy_euptae |
| EUPJONG | 업종 | biz sector | buy_eupjong |
| TAXNO | 사업자번호 | tax id | buy_taxno |
| YEASIN | 여신/한도 | credit limit | buy_yeasin |
| MAYUL | 매익률 | margin rate | buy_mayul |
| HALYUL | 할인율 | discount rate | buy_halyul |
| CARDYUL | 카드수수료율 | card fee rate | buy_cardyul |
| JUMIN | 주민번호 | resident id | NAM_JUMIN, SW_JUMIN |
| JIK(KUB) | 직급 | rank | nam_jik, SW_JIKKUB |
| JIKCHAK | 직책 | position | SW_JIKCHAK |
| HP | 핸드폰 | mobile | nam_hp |
| BIRTH | 생일 | birthday | nam_birth |
| MARRY | 결혼 | marriage | nam_MARRY |
| IBSA(IL) | 입사일 | join date | SW_IBSAIL |
| TEA(DT/RESON) | 퇴사 | leave | SW_TEA, SW_TEADT |

---

## 파일 용도 (3종 분리 구조)

```
PYOJUN.MDB  → 마스터 데이터 (거래처/품목/사원/표준코드/BOM)  6 테이블
PANDATA.mdb → 거래 데이터 (전표/계산서/수표/은행/원장)        18 테이블
POTHER.mdb  → 기타 데이터 (달력/배송/AS/메모/명함)            8 테이블
POST.mdb    → 우편번호 마스터                                1 테이블 (49,702행 — 무시)
```

레거시 히트판은 데이터 파일을 *논리 분할*해서 보관하는 전형적 VB6+Access 패턴이다.

---

## 신규 ERP 매핑표 (마스터 — PYOJUN.MDB)

| 레거시 테이블 | 컬럼 핵심 | 신규 ERP 테이블 | 매핑 비고 |
|---|---|---|---|
| **DOCF8** (거래처) | buy_code, buy_name, buy_taxno, buy_top, buy_addr, buy_tel, buy_yeasin, buy_mayul, buy_halyul, buy_bank, buy_bankno, buy_sawon, buy_rem1~8, buy_euptae, buy_eupjong | `partners` | buy_code(Long) → partner_code 또는 별도 legacy_code 컬럼 보존. AES 암호화 컬럼: buy_taxno → encrypted_business_no |
| **DOCFS** (품목) | S_PUM, S_KU, S_DANW, S_DESC, S_MAKER, S_IBUY, S_JEK(원가), S_IDAN(매입가), S_PDAN/A/B/C/D/E (판매가 6단계), S_TAX, S_BARCODE | `items` | 단가 6단계: S_IDAN → purchase_price / S_PDAN → sale_price / S_PDANA~E → 등급별 단가 (special_prices 테이블) |
| **DOCSW** (사원) | SW_NAME, SW_BUSEA(부서), SW_JIKKUB(직급), SW_JIKCHAK(직책), SW_IBSAIL(입사일), SW_PAY(급여), SW_JUMIN(주민번호), SW_TEA(퇴사여부), SW_TEADT(퇴사일) | `employees` | SW_JUMIN → encrypted_resident_no (AES). SW_TEA = 1 → status='retired' |
| **DOCRT** (BOM) | RT_PUM(완제품), RT_RPUM(자재), RT_UNIT(소요량), RT_GU(구분), RT_SON(손실율), RT_KUM(원가) | `bom_headers` + `bom_items` | RT_PUM=1행에 자재 N개 들어있는 평면 → header/item 분리 필요 |
| **COSTNO** (원가코드) | CT_CODE, CT_DESC | `cost_codes` 또는 `common_codes` | 33행 (마스터 코드) |
| **SETUP** (시스템 설정) | SET_CODE, SET_DESC | (이관 제외) | 레거시 시스템 설정 — 신규는 다름. 무시. |

---

## 신규 ERP 매핑표 (거래 — PANDATA.mdb)

| 레거시 테이블 | prefix / 추정 용도 | 신규 ERP 테이블 | 매핑 비고 |
|---|---|---|---|
| **DOCF1** (`KA_*`) | 견적/거래명세서 명세 라인 (KA_NO=문서번호, KA_PUM/KU/SU/DAN/KUM/VAT) | `sales_delivery_items` 또는 `quotation_items` | KA_NAB=납기, KA_DC=할인. 문서 헤더는 어디? → DOCF2 |
| **DOCF2** (`K2_*`) | 거래명세서/세금계산서 헤더 (K2_NO, K2_BUY, K2_BUYC=거래처코드, K2_SAWON, K2_AMT/VAT, K2_DT, K2_KIDT=기일, K2_GUBUN=유형, K2_HALY/HALK=할인) | `sales_deliveries` (헤더) | K2_GUBUN으로 견적/명세/계산서 구분 |
| **DOCF4** (`TX_*`) | 세금계산서 (TX_NO, TX_PDT, TX_BUY, TX_PUM1~4, TX_SU1~4, TX_DAN1~4, TX_KUM1~4, TX_VAT1~4, TX_GU=발행구분, TX_SENDDT=발송일, TX_READDT=수신일) | `tax_invoices` + `tax_invoice_items` | 한 행에 품목 4개까지 들어있는 평면 → 4건 row로 분해 |
| **DOCF5** (`S_*`) | 수금/미수금 원장 (S_BUY, S_YMD, S_BAL=잔액, S_SUK=수금액, S_GU=구분, S_REM) | `partner_balance_ledger` 또는 `collections` | INSERT ONLY 원장 (§#3) |
| **DOCF6** (`AC_*`) | 회계 분개 (AC_YMD, AC_JWASU=좌수, AC_JEN=차변/대변, AC_JEK=적요, AC_AMT, AC_SBUY=상대거래처) | `journal_entries` + `journal_lines` | AC_JEN='1'=차변, '2'=대변 추정 |
| **DOCF7** (`SC_*`) | 사원 일자별 정산? (SC_KCODE, SC_SAWON, SC_DT, SC_CR=대변, SC_DR=차변) | `employee_account_ledger` 또는 분개 보조 | 행 수 적을 듯 (사원 정산용) |
| **DOCF9** (`EU_*`) | 어음/수표 발행 (EU_CLA=종류, EU_NO=어음번호, EU_BANK, EU_BAL=발행지, EU_BDT=발행일, EU_MDT=만기일, EU_HDT=할인일, EU_AMT, EU_BUY=수취인) | `bills_received` 또는 `notes_payable` | EU_GU로 받을어음/지급어음 구분 |
| **DOCFA** (`IU_*`) | 매입 발주 (IU_NO, IU_PUM, IU_QTY, IU_DAN, IU_AMT/VAT, IU_BUY=거래처, IU_ODT=주문일, IU_IDT=입고일, IU_IQTY=입고량, IU_GU, IU_JIB=지불) | `purchase_orders` + `purchase_order_items` | IU_GU='1'=발주, '2'=완료 추정 |
| **DOCFB** (`IJ_*`) | 입출고 원장 / 재고 트랜잭션 (IJ_DT, IJ_IO='I'/'O', IJ_BUY, IJ_PUM, IJ_QTY, IJ_DAN, IJ_AMT/VAT, IJ_CHANG=창고, IJ_TAXNO=계산서번호) | `stock_ledger` (INSERT ONLY) | IJ_IO='I'=입고, 'O'=출고 |
| **DOCFC** (`IM_*`) | 월별 재고 집계 (IM_YM, IM_CHANG, IM_pum, IM_BQTY=기초, IM_IQTY=입고, IM_OQTY=출고, IM_CQTY=기말) | (이관 제외 — 신규는 자동 집계) | 월마감 결과는 신규 시스템에서 재계산. |
| **DOCFE** (`IJA_*`) | 비용 분배 6항목 (IJA_DT, IJA_AMT1~6, IJA_REM, IJA_LINE) | `expenses` | 6 카테고리 → 분개 분할 |
| **DOCFO** (`IO_*`) | 매출/판매 (IO_NO, IO_PUM, IO_QTY, IO_DAN, IO_AMT/VAT, IO_BUY, IO_ODT, IO_IDT=출하일) | `sales_orders` + `sales_order_items` | IU와 형제 구조 (매입↔매출 대칭) |
| **DOCFQ** (`EQ_*`) | 어음 만기/회수 (EQ_CLA, EQ_NO, EQ_BANK, EQ_BDT/MDT/CDT, EQ_AMT) | `bills_settlement` 또는 어음 상태 변경 | DOCF9의 후속 이벤트 |
| **DOCCD** (`CD_*`) | 카드 결제 마스터 (CD_CDNO=카드번호, CD_NAME=카드사, CD_KIDT=결제일, CD_MAMT=금액, CD_HAL=할부) | `card_payments` | 카드사·할부 정보 |
| **DOCCD1** (`CD1_*`) | 카드 결제 라인? (CD1_NO 헤더 ID, CD1_AMT, CD1_SBUY=거래처) | `card_payment_lines` | CD와 1:N |
| **BANKF** (`BK_*`) | 은행 거래 (BK_NO=계좌번호, BK_YMD, BK_JEN=차/대, BK_AMT, BK_SBUY) | `bank_transactions` | 통장 입출금 |
| **DOCLT** (`LT_*`) | 락 테이블 / 표시 (LT_MK, LT_TIME) | (이관 제외) | 시스템 락 — 무시 |
| **REMARK1** | 비고 마스터 (REM_CODE, REM_1~4) | (이관 제외 또는 common_codes) | 100자×4 코드별 비고 |

---

## 신규 ERP 매핑표 (기타 — POTHER.mdb)

| 레거시 테이블 | 추정 용도 | 신규 ERP 테이블 | 매핑 비고 |
|---|---|---|---|
| **CALENDAR** (7,305 rows) | 영업일·환율 캘린더 (CALENDAR_YMD, WEEK, WORK=영업일 여부, ERATE_VND/WON=환율) | `calendar_master` (선택) | 신규는 자체 영업일 계산 — 환율은 별도 테이블 |
| **DELIVERY** | 배송 정보 (DEL_DATE, DEL_BUY, DEL_DEST, DEL_PUM, DEL_QTY, DEL_DELSERVICE) | `deliveries` 또는 `sales_deliveries` 보조 | 택배사·배송지 |
| **DOCAS** (`AS_*`) | A/S 접수 (AS_DT, AS_BUY, AS_YO1=요청내용, AS_CHDT=처리일, AS_CH1=처리내용, AS_COST) | `as_requests` (없으면 신설) | 운영 후순위 |
| **DOCAS1** (`AS1_*`) | A/S 추가 비용 (AS1_BUY, AS1_PASS, AS1_AMT, AS1_JIBUL=지불방식, AS1_SDT/EDT) | `as_payment_lines` | DOCAS와 1:N |
| **DOCME** (`ME_*`) | 메모 (ME_DATE/TIME, ME_SAWON, ME_DESC1~5, ME_NOTICE) | `memos` 또는 `notifications` | 사내 메모 |
| **DOCNM** (`nam_*`) | 명함 / 인물 마스터 (NAM_OWNER, NAM_COM=회사, NAM_NAME, nam_jumin, nam_email, nam_birth, nam_buse, nam_jik, ...) | `contacts` (없으면 신설) | 거래처 직원 명함 |
| **DOCSC** (`SC_*`) (252 rows) | 일정 / 스케줄 (SC_DATE, SC_TIME, SC_SAWON, SC_DESC1~4, SC_OPEN) | `schedules` 또는 일정관리 | 캘린더 일정 |
| **LOCK1** | 시스템 락 | (이관 제외) | 무시 |

---

## 핵심 관계도 (외래키 매핑)

```
PYOJUN.DOCF8 (거래처: buy_code Long)
  ↑─ 모든 거래의 외래키
PANDATA.DOCF2.K2_BUYC = buy_code  → 거래명세서 헤더 → partners
PANDATA.DOCFA.IU_BUY = buy_code   → 매입발주 → partners
PANDATA.DOCFO.IO_BUY = buy_code   → 매출 → partners
PANDATA.DOCFB.IJ_BUY = buy_code   → 입출고 → partners

PYOJUN.DOCFS (품목: S_PUM Text)  ← Text 키. 변경되면 어디서든 깨짐.
  ↑
PANDATA.DOCF1.KA_PUM/KU            → 명세서 라인
PANDATA.DOCFA.IU_PUM/KU            → 발주 라인
PANDATA.DOCFO.IO_PUM/KU            → 매출 라인
PANDATA.DOCFB.IJ_PUM/KU            → 재고 트랜잭션
PANDATA.DOCFC.IM_pum/ku            → 월 집계
PYOJUN.DOCRT.RT_PUM/RT_RPUM        → BOM (완제품 ↔ 자재)

PYOJUN.DOCSW (사원: SW_NAME Text)  ← Text 키. (사원ID 별도 없음 — 이름으로 join)
  ↑
PANDATA.DOCFB.IJ_SAWON, DOCFE.IJA_SAWON 등 SAWON 컬럼들
```

---

## 매핑 위험 포인트 (사장님 결재 필요)

1. **품목·사원 키가 Text** — 이름이 같은 품목/사원이 있으면 join 깨짐. 신규는 GUID로 가는데 매핑 시 *이름 → GUID lookup* 필수.
2. **거래처는 Long(int) 키 안전** — DOCF8.buy_code 그대로 신규 partners.legacy_code 보존 권장.
3. **세금계산서 한 행에 4품목** (DOCF4.TX_PUM1~4) — 신규는 N개 무제한이라 *행 분해* 필요.
4. **BOM은 평면** — DOCRT 1행 = 완제품-자재 1쌍. 한 완제품의 N개 자재는 N개 행. 신규의 bom_headers/bom_items 변환 가능.
5. **인코딩 — Access는 시스템 ANSI(CP949)** — 한글 깨짐 방지 위해 OleDb 읽을 때 명시 변환 필수.
6. **NULL 처리** — 모든 컬럼 IS_NULLABLE=Y. 빈 값 ""과 NULL 혼재 → 코드 두 케이스 다 처리.
7. **단가 6단계** (DOCFS.S_PDAN/A/B/C/D/E) — 신규는 등급별 special_prices에 6행 INSERT.
8. **DOCFC 월집계 / DOCLT 락 / SETUP / REMARK1 / LOCK1 / POS01** — 이관 제외 (신규에서 무의미).

---

## 다음 액션

1. ✅ 스키마 덤프 + 매핑 명세서 (이 문서) 작성 완료.
2. ⬜ 기존 `MdbMigrationService.cs` (1236줄) 의 SQL을 이 명세서와 1:1 대조 → 핀포인트 수정.
3. ⬜ 사장님 *진짜* 백업 (BK_2026-02-20-175608, 비번 걸림) 의 비번 확보 또는 사장님이 비번 풀린 사본 제공 시 시연.
4. ⬜ 빈 데이터 MDB로 미리보기 시연 → 32 테이블 모두 0건 표시되는지 확인 (구조 검증).
5. ⬜ 영업 시연용 시나리오: "5분 안에 옮겨드립니다" 데모 동영상.
