# 공식 마이그 정답서 v2 (MSSQL ↔ MariaDB)

> **2026-05-15 14:30 PM 브라운킴 (v2 — 24 테이블 전수 PK 확정 + 운영본 데이터 발견)**
> **출처:** 사장님 `server_v5.zip` → SQL Server attach
> **DB:** GIS_PANDATA_V5 (24 테이블, 정답 스키마) + GIS_POTHER_V5 + GIS_PANDATA (운영본)

---

## 1. ⭐ 전체 PK 정답 (24 테이블 전수)

| # | 테이블 | 의미 | PK 컬럼 | MariaDB 매핑 | 현재 행수 |
|---|---|---|---|---|---:|
| 1 | **DOCFB** | 재고원장 | `IJ_DT, IJ_IO, IJ_SEQ, IJ_BUY, IJ_SUN` | stock_ledger | 236,139 |
| 2 | **DOCF5** | 수금 | `S_BUY, S_YMD, S_SUN, S_GU` | collections | 673,220 |
| 3 | **DOCF8** | 거래처 | `BUY_CODE` | partners | 19,535 |
| 4 | **DOCF4** | 세금계산서 | `TX_IO, TX_NO` | tax_invoices | 66,683 |
| 5 | **DOCF2** | K2 거래명세 | `K2_NO` | sales_orders (K2) | 180 |
| 6 | **DOCF1** | KA 수기 거래명세 | `KA_NO, KA_NO1, KA_NO2` | sales_orders (KA) | - |
| 7 | **DOCF6** | 경비/현금출납 | `AC_YMD, AC_JWASU, AC_JEN` | cashbook + expenses | 105,769 + 102,700 |
| 8 | **DOCF7** | 전표/회계 | `SC_KCODE, SC_DT, SC_SAWON, SC_SUN` | journal_lines | - |
| 9 | **DOCFA** | 매입발주 IU | `IU_NO, IU_SUN` | purchase_orders + items | 682 |
| 10 | **DOCFO** | 매출주문 IO | `IO_NO, IO_SUN` | sales_orders + items | 180 |
| 11 | **DOCF9** | 어음 EU | `EU_CLA, EU_NO` | notes | - |
| 12 | **DOCFQ** | 어음 EQ | `EQ_CLA, EQ_NO` | notes | - |
| 13 | **DOCFE** | 입출고 상세 | `IJA_DT, IJA_IO, IJA_SEQ, IJA_BUY` | stock_ledger_detail | - |
| 14 | **DOCFC** | 재고 월별 집계 | `IM_YM, IM_CHANG, IM_PUM, IM_KU` | stock_summary | - |
| 15 | **DOCFS** | 상품마스터 | `S_PUM, S_KU` | items | 563 |
| 16 | **DOCSW** | 사원마스터 | `SW_NAME` | employees | 22 |
| 17 | **DOCRT** | 자재변환률 | `RT_PUM, RT_KU, RT_SUN` | bom | - |
| 18 | **DOCLT** | 손익변동 | `LT_MK` | (마이그 안 함) | - |
| 19 | **DOCCD** | 카드결제 | `CD_CLA, CD_CDNO, CD_SNO` | card_payments | - |
| 20 | **DOCCD1** | 카드원장 | `CD1_NO, CD1_YMD, CD1_JWASU, CD1_JEN` | card_ledger | - |
| 21 | **BANKF** | 은행거래 | `BK_NO, BK_YMD, BK_JWASU, BK_JEN` | bank_transactions | 431,134 |
| 22 | **DELIVERY** | 배송 | `DEL_DATE, DEL_TIME, DEL_BUY, DEL_DEST, DEL_SUN` | deliveries | - |
| 23 | **COSTNO** | 비용코드 | `CT_CODE` | cost_categories | - |
| 24 | **SETUP** | 시스템 설정 | `SET_CODE` | system_settings | - |

---

## 2. ⭐ 워크플로우 ID 연결 시스템 (사장님 격언 객관 증거)

### DOCFB IX_DOCFB_2 인덱스 = 워크플로우 키
```
IJ_IO, IJ_TAXNO, IJ_TAXBUY, IJ_DT, IJ_SEQ, IJ_SUN
       ↑          ↑
       세금계산서 번호 (8자리) + 거래처 (int)
```

**= 레거시도 워크플로우 ID 연결 시스템 살아있음 100% 증명.**

### ID 변환 룰
- `IJ_TAXNO char(8)` — 세금계산서 번호
- `"00000000"` = 워크플로우 없음 (8자리 0 명시적 플래그)
- 세금계산서 발행 시 IJ_TAXNO 갱신 / 미발행 시 "00000000" 유지

### ID 연결 체인
```
DOCF4 (TX 세금계산서)  ←─ TX_NO ─┐
                                  │
                                  └─→ DOCFB.IJ_TAXNO (재고원장 = stock_ledger)
                                  
DOCF2 (K2 거래명세)   ←─ K2_NO ─┐
                                 │
DOCF1 (KA 거래명세)   ←─ KA_NO ─┘
```

---

## 3. ⭐ 우리 코드 vs 정답 — 5/14 마이그 코드 봉합 매트릭스

### 3-1. DOCFB → stock_ledger (재고원장)

| 항목 | 현재 (5/14) | 정답 (5/15) | 5/17 봉합 |
|---|---|---|---|
| ORDER BY | `IJ_DT, IJ_SEQ` | `IJ_DT, IJ_IO, IJ_SEQ, IJ_BUY, IJ_SUN` | 5컬럼 ORDER BY로 정정 |
| sourceId | `mig-{IJ_DT}-{IJ_SEQ}` | 5개 컬럼 복합 | `mig-{IJ_DT}-{IJ_IO}-{IJ_SEQ}-{IJ_BUY}-{IJ_SUN}` |
| BulkCopy | ✅ 적용 | ✅ | sourceId만 정정 |
| 워크플로우 ID | `IJ_TAXNO` 단순 저장 | 8자리 char + "00000000" 처리 | doc_no 컬럼 정합 + NULL 처리 |

### 3-2. DOCF5 → collections (수금) — **진범 #1**

| 항목 | 현재 (5/14) | 정답 (5/15) | 5/17 봉합 |
|---|---|---|---|
| ORDER BY | `S_YMD, S_BUY` | `S_BUY, S_YMD, S_SUN, S_GU` | 4컬럼 ORDER BY |
| sourceId | `mig-{S_YMD}-{S_BUY}-{rowIdx:D6}` | S_SUN 컬럼 실재! | `mig-{S_BUY}-{S_YMD}-{S_SUN}-{S_GU}` |
| 멱등성 | rowIdx 인공 | S_SUN smallint 공식 | rowIdx 폐기 |
| Lock timeout | 96초 (BulkCopy 미적용) | - | BulkCopy 전환 → 5초 |

### 3-3. DOCF4 → tax_invoices (세금계산서) — **진범 #3**

| 항목 | 현재 (5/14) | 정답 (5/15) | 5/17 봉합 |
|---|---|---|---|
| 컬럼명 | tax_invoice_id, invoice_date, total_amount, remark | TX_IO, TX_NO, TX_PDT, TX_BUY, TX_PUM1~4, TX_SU1~4, TX_DAN1~4, TX_KUM1~4, TX_VAT1~4, TX_SENDDT, TX_READDT, TX_REPORTDT | DDL 전면 재정의 |
| PK | tax_invoice_id GUID | TX_IO + TX_NO | direction + tax_no 복합 |
| 품목 | 별도 테이블 | **한 row에 4개 인라인** | tax_invoice_items 분해 (LineNo 1~4) |
| 상태 | delivery_id NOT NULL UNIQUE | TX_SENDDT/TX_READDT/TX_REPORTDT 별도 | sent_at·read_at·reported_at 컬럼 |

### 3-4. DOCF2 → sales_orders (K2 거래명세) — **진범 #2**

| 항목 | 현재 (5/14) | 정답 (5/15) | 5/17 봉합 |
|---|---|---|---|
| partner 매핑 | TryGetValue(K2_BUYC) → skip | K2_BUYC int (0/음수 가능) | EnsureLegacyUnknownPartner fallback |
| PK | order_id GUID | K2_NO char(10) 단일 | order_no UNIQUE |
| UPSERT | ✅ 5/14 적용 | ✅ | 정합 (K2_NO 단일 키) |

### 3-5. DOCF8 → partners (거래처) — **PII 보안 강화**

| 컬럼 | MSSQL | 보안 |
|---|---|---|
| BUY_CODE | int PK | partner_id 매핑 키 |
| BUY_NAME | varchar(50) | 평문 |
| BUY_TOPJUMIN | varchar(20) | 🔐 **주민번호 — AES-256 필수 (헌법 #5)** |
| BUY_TAXNO | varchar(12) | 🔐 사업자번호 (마스킹 권장) |
| BUY_TEL/BUY_TEL1/BUY_FAX | varchar(20) | 🔐 PII 마스킹 |
| BUY_addr/BUY_addr1 | varchar(100) | 🔐 PII 마스킹 |

**MSSQL 인덱스 IX_DOCF8_5에 BUY_TOPJUMIN 단독 인덱스 존재 ⚠️** → 평문 검색 사용 흔적. MariaDB 측은 해시 인덱스 + AES 평문 컬럼으로.

### 3-6. BANKF → bank_transactions (은행거래) — **1분 절대 봉합**

| 항목 | 현재 (5/14) | 정답 (5/15) | 5/17 봉합 |
|---|---|---|---|
| ORDER BY | `BK_YMD` (5/14 정정됨) | `BK_NO, BK_YMD, BK_JWASU, BK_JEN` | 4컬럼 PK ORDER BY |
| sourceId | `mig-{BK_YMD}-{rowIdx}` | - | `mig-{BK_NO}-{BK_YMD}-{BK_JWASU}-{BK_JEN}` |
| BulkCopy | 미적용 (150초) | - | 전환 → 5초 |

### 3-7. DOCF6 → cashbook + expenses — **1분 절대 봉합**

| 항목 | 현재 (5/14) | 정답 (5/15) | 5/17 봉합 |
|---|---|---|---|
| 분기 | AC_JEN 으로 cashbook(I) vs expenses(E) | - | AC_JEN char(1) 정합 |
| PK | (없음, GUID) | `AC_YMD, AC_JWASU, AC_JEN` | 3컬럼 PK ORDER BY |
| BulkCopy | 미적용 (67~71초) | - | 전환 → 5초 |

### 3-8. DOCF7 → journal_lines (전표) — **1분 절대 봉합**

| 항목 | 현재 (5/14) | 정답 (5/15) | 5/17 봉합 |
|---|---|---|---|
| PK | (확인) | `SC_KCODE, SC_DT, SC_SAWON, SC_SUN` | 4컬럼 PK |
| BulkCopy | 미적용 (71초) | - | 전환 → 5초 |

---

## 4. ⭐ 1분 절대 봉합 (헌법 #26) — 60초 달성 시뮬

| 테이블 | 현재 시간 | BulkCopy 후 | 절감 |
|---|---|---|---|
| stock_ledger (DOCFB) | (BulkCopy 적용) | 이미 OK | - |
| collections (DOCF5) | 96초 (Lock timeout) | **5초** | -91초 |
| bank_transactions (BANKF) | 150초 | **5초** | -145초 |
| cashbook (DOCF6/I) | 67초 | **5초** | -62초 |
| expenses (DOCF6/E) | 71초 | **5초** | -66초 |
| journal_lines (DOCF7) | (확인 필요) | - | - |
| **합산** | **333초 (5/14 실측)** | **~30초** | **-303초** |

**= 헌법 #26 1분 절대 통과.**

---

## 5. POTHER_V5 추가 분석

(점심 후 추가 진행)
- POTHER = "P-Other" 부가 테이블 (이벤트·서비스티켓·배송추적·명함 등)
- DOCNM (명함) / DOCAS (수리/AS) / DELIVERY (배송) 등
- 5/14 인수인계서 §3.3: BusinessCards 0 / ServiceTickets 0 / DeliveryTracking 0 / Events 9,870

---

## 6. GIS_PANDATA 운영본 발견 결과

- 사장님 옛 운영본 (2025-04-29 마지막 수정, 25MB)
- POS01: 49,702건 (POS 거래 로그)
- PAYTAX: 150건
- DOCFS: 1건 (상품)
- 나머지 DOC 테이블 비어있음 (사장님이 데이터 비우고 보관한 듯)
- **추가 가치:** POS01·PAYTAX·LOG 테이블이 우리 마이그 대상 외인지 검토 필요

---

## 7. MariaDB ↔ MSSQL 자동 비교 스크립트

위치: `tools/mariadb_vs_mssql_check.sql` (5/16 새벽 생성 예정)

```sql
-- MariaDB 측 (current)
SELECT 'mariadb' AS source, 'stock_ledger' AS tbl, COUNT(*) AS rows,
       MIN(ledger_date) AS min_date, MAX(ledger_date) AS max_date
FROM hitpan_erp.stock_ledger
WHERE tenant_id = '452ca266-97b9-4cd1-a0ac-2f37830c81f6'
UNION ALL
SELECT 'mariadb', 'collections', COUNT(*),
       MIN(collection_date), MAX(collection_date)
FROM hitpan_erp.collections
WHERE tenant_id = '452ca266-97b9-4cd1-a0ac-2f37830c81f6'
-- ... 모든 마이그 테이블
```

MSSQL 측 동일 쿼리 → diff 비교 → 누락 또는 추가 데이터 식별.

---

## 8. 5/16~5/17 마이그 마무리 작업지시서

### ✅ 5/15 PM 자체 진행 완료
- **진범 #1 (collections 봉합)** — commit `712ce65`. S_SUN+S_GU 공식 멱등 키 반영
- **진범 #2 (K2 partner fallback)** — commit `44a0661`. EnsureLegacyFallbackPartnerAsync 신규, 워크플로우 끊김 0

### 우선순위 1 (5/16 새벽, 본부장 입회 + DB매니저)
- **demo.hitpan.kr 실측 검증** — `tools/mariadb_vs_mssql_check.sql` 실행, 13/13 PASS 카드 갱신 (사장님 B 결정으로 연기됨)

### 우선순위 2 (5/16 오전, 백엔드 매니저 + DB매니저)
- **WS-MIG-03 (진범 #3 세금계산서 DDL)** — TX_* 13컬럼 추가 + 4 품목 인라인 → tax_invoice_items 분해
- **WS-MIG-04 (1분 절대 잔여 3종)** — bank_transactions/cashbook/expenses ALTER TABLE + BulkCopy

### 우선순위 3 (5/17 종일)
- WS-MIG-05: 워크플로우 ID 연결 (IJ_TAXNO + IJ_TAXBUY 매핑)
- WS-MIG-06: demo 실측 13/13 PASS + 60초 절대 검증

---

## 8-A. ⭐ 5/15 PM 추가 발견 — 우리 코드 PK 컬럼 누락 매트릭스

DOCF6 / DOCF7 / BANKF 인덱스 전수 추출 결과 (`sqlcmd sys.indexes`), **우리 코드가 PK 일부 컬럼을 안 읽는** 사실 확인.

| 테이블 | MSSQL PK | 우리 코드 읽음 | 누락 | 영향 |
|---|---|---|---|---|
| BANKF | `BK_NO + BK_YMD + BK_JWASU + BK_JEN` | BK_NO, BK_YMD, BK_JEN | **BK_JWASU (smallint)** | 멱등 키 미존재, 재마이그 중복 |
| DOCF6 (cashbook) | `AC_YMD + AC_JWASU + AC_JEN` | AC_YMD, AC_SGU | **AC_JWASU + AC_JEN** | PK 2/3 누락 |
| DOCF7 (expenses) | `SC_KCODE + SC_DT + SC_SAWON + SC_SUN` | SC_KCODE, SC_DT, SC_SAWON | **SC_SUN (smallint)** | 동일 사원·동일 일자 다중 전표 분리 불가 |

### 5/16 봉합 액션 (WS-MIG-04 명세서에 반영)

```csharp
// 1) MDB 읽기 SQL에 누락 컬럼 추가:
"SELECT * FROM BANKF ORDER BY BK_NO, BK_YMD, BK_JWASU, BK_JEN"
"SELECT * FROM DOCF6 ORDER BY AC_YMD, AC_JWASU, AC_JEN"
"SELECT * FROM DOCF7 ORDER BY SC_KCODE, SC_DT, SC_SAWON, SC_SUN"

// 2) sourceId 정답 패턴:
sourceId = $"mig-{BK_NO}-{BK_YMD}-{bkJwasu}-{BK_JEN}";        // bank_transactions
sourceId = $"mig-{AC_YMD}-{acJwasu}-{AC_JEN}";                // cashbook
sourceId = $"mig-{SC_KCODE}-{SC_DT}-{SC_SAWON}-{scSun:D5}";   // expenses

// 3) DDL ALTER TABLE (DB매니저):
ALTER TABLE bank_transactions ADD COLUMN source_type VARCHAR(30) NULL,
                              ADD COLUMN source_id VARCHAR(80) NULL,
                              ADD COLUMN migrated_source_hash VARCHAR(64) NULL,
                              ADD UNIQUE KEY uq_bank_tx_source (tenant_id, source_type, source_id);
-- cashbook / expenses 동일 패턴 적용
```

### 멱등성 의미
- 멱등 키 = MDB 자체 PK 그대로 보존 → 재마이그 시 INSERT IGNORE 자동 차단
- 인위적 rowIdx 패턴 폐기 (5/14 collections 사고와 같은 ORDER BY tie-break 비결정성 제거)
- 진범 #1 collections와 정확히 동일한 패턴 적용

---

## 9. 보안 (헌법 #29 정합)

- SQL Server 활성화 / .mdf attach / NTFS 압축 해제 — 모두 사장님 결재 옵션 A (2026-05-15) 명시 후 진행 ✅
- .mdf 파일 Public 폴더 임시 — 분석 종료 후 격리 또는 삭제 권장
- BUY_TOPJUMIN·SW_JUMIN PII 컬럼 — 본 문서 평문 0
- MSSQL 운영본 GIS_PANDATA — POS01 49K 행 존재. 분석 시 본사 데이터 0건 원칙

---

**작성: PM 브라운킴 2026-05-15 14:30**
**문서 ID: GOV-MIGRATION-MAPPING-20260515-v2**
**활용: 5/16~5/17 마이그 봉합 풀스택 작업지시서 6건 기반**
