# 작업지시서 WS-20260518-F — 진범 #9·#12 동시 봉합

> **작성:** 2026-05-18 PM 브라운킴
> **수신:** 백엔드 매니저 + DB 매니저 + ERP 매니저 + 보안 매니저 1
> **사장님 결재:** Q1·Q2·Q3·Q4·Q5 모두 OK (2026-05-18)
> **상위 문서:** `20260518_PM_봉합작업지시서_5건.md` WS-D 확장

---

## 🎯 배경

PM 전수조사 결과 **회계·거래명세서 마이그 코드 0줄** + 사장님 5/14 *"마이그 예외 발언 안 함"* 정정.
→ DOCFB(124,152행)·DOCF7(27,664행)·DOCFE(117,112행)·DOCF6(20,175행) 신규 마이그.

### 사장님 결재 박제 (2026-05-18)
- **Q1:** 우선순위 DOCFB → DOCF7 → DOCFE → DOCF6 ✅
- **Q2:** 마이그 분개 = `source_type='migration'` 격리 ✅
- **Q3:** WS-F 작지서 먼저 작성 후 구현 ✅
- **Q4:** **IJ_BUY 음수값(주민번호 추정) = 그대로 이관** ✅
- **Q5:** **SC_KCODE 계정매핑 = ERP 매니저가 정의** ✅

---

## 📋 4단계 분담

### 🥇 WS-D-1: DOCFB → sales_deliveries · purchase_receipts (진범 #9)
- **MDB:** PANDATA.DOCFB (124,152행)
- **타겟:** `sales_deliveries`(IJ_IO=1) + `purchase_receipts`(IJ_IO=2)
- **담당:** 백엔드 매니저 + DB 매니저
- **예상:** 2~3일

### 🥈 WS-D-2: DOCF7 → journal_entries · journal_lines (진범 #12)
- **MDB:** PANDATA.DOCF7 (27,664행)
- **타겟:** `journal_entries`(헤더) + `journal_lines`(차/대변 라인)
- **담당:** 백엔드 매니저 + DB 매니저 + **ERP 매니저 (계정매핑)**
- **선행:** ERP 매니저 SC_KCODE 4자리 → 한국 표준 5자리 매핑표 정의
- **예상:** 3~4일 (ERP 매핑 대기 포함)

### 🥉 WS-D-3: DOCFE → deliveries 결재 라인 보조
- **MDB:** PANDATA.DOCFE (117,112행)
- **추가 분석 필요:** IJA_LINE·IJA_SSUN·IJA_AMT1~6 의미 확정
- **담당:** 백엔드 + ERP
- **예상:** 1~2일

### 🏅 WS-D-4: DOCF6 → 수금/지급 잔액 보완
- **MDB:** PANDATA.DOCF6 (20,175행)
- **타겟:** `collections` 보완 또는 신규 `cash_balance_ledger`
- **담당:** 백엔드 + DB
- **예상:** 1일

---

# 🥇 WS-D-1 상세 명세 — DOCFB 거래명세서

## MDB 스키마 (PM 실측)

```
IJ_DT       char(8)     — 거래일자 YYYYMMDD (실측 '00000000' 존재 = null 처리)
IJ_IO       smallint    — 1=매출, 2=매입
IJ_SEQ      int         — 거래 시퀀스
IJ_BUY      int         — 거래처 코드 (음수 = 주민번호 변환값, Q4 그대로 이관)
IJ_SAWON    string      — 담당사원
IJ_SUN      int         — 라인 순번
IJ_PUM      string      — 품목명
IJ_KU       string      — 규격
IJ_QTY      decimal     — 수량
IJ_DC       string      — 단가구분
IJ_DAN      decimal     — 단가
IJ_AMT      decimal     — 공급가액
IJ_VAT      decimal     — 부가세
IJ_REM      string      — 적요
IJ_CHANG    string      — 창고
IJ_TAXNO    int         — 세금계산서 번호 (TX_NO 연결 키) ⭐
IJ_DSEQ     int         — 세금계산서 시퀀스
IJ_TAXBUY   int         — 세금계산서 거래처
```

## 무결성 키 발견 ⭐
**IJ_TAXNO ↔ tax_invoices.tax_no** = 거래명세서 ↔ 세금계산서 정합성 키 (5/14 마이그 예외 폐기 근거).

## 타겟 테이블

### sales_deliveries (IJ_IO=1)
헤더 그룹화: IJ_DT + IJ_IO + IJ_SEQ + IJ_BUY 동일 = 헤더 1건 + 라인 N건

### purchase_receipts (IJ_IO=2)
동일 구조, 반대 방향

## 구현 패턴

### 1단계: 헤더 그룹화 + in-memory row 빌드
```csharp
var groups = dt.AsEnumerable().GroupBy(r => new {
    Dt = GetStr(r, "IJ_DT"),
    Io = GetInt(r, "IJ_IO"),
    Seq = GetInt(r, "IJ_SEQ"),
    Buy = GetInt(r, "IJ_BUY")
});
```

### 2단계: BulkCopy 분기 (WS-A 패턴 동형)
- header staging + items staging 2단계
- BulkCopyTimeout = 86400 (헌법 #26 v3)
- INSERT IGNORE SELECT (멱등 source_id)

### 3단계: source_id 멱등 키
```
sourceId = $"mig-docfb-{IJ_DT}-{IJ_IO}-{IJ_SEQ}-{IJ_BUY}"
```

### 4단계: TAXNO 연결 (정합성 복구)
```sql
UPDATE sales_deliveries sd
INNER JOIN tax_invoices ti ON ti.tax_no = sd.legacy_tax_no
SET sd.tax_invoice_id = ti.invoice_id
WHERE sd.source_type = 'migration' AND sd.tax_invoice_id IS NULL;
```

### 5단계: tax_invoices.delivery_id 역참조 (헌법 #20 워크플로우)
5/14 진범 #3 봉합 시 `delivery_id NULL 허용` ALTER — 이제 채워넣음.

## 사전 확인 작업

### A) sales_deliveries · purchase_receipts 스키마 확인 (DB 매니저)
- 컬럼 매핑 표 작성
- legacy_tax_no 컬럼 존재 여부 확인 (없으면 ALTER 필요)
- 인덱스 UNIQUE 정책 확인

### B) source_id 컬럼 존재 확인
없으면 ALTER:
```sql
ALTER TABLE sales_deliveries ADD COLUMN source_type VARCHAR(30) NULL,
                              ADD COLUMN source_id VARCHAR(80) NULL,
                              ADD COLUMN legacy_tax_no INT NULL,
                              ADD UNIQUE KEY uq_sd_source (tenant_id, source_id);
ALTER TABLE purchase_receipts ADD COLUMN source_type VARCHAR(30) NULL,
                              ADD COLUMN source_id VARCHAR(80) NULL,
                              ADD COLUMN legacy_tax_no INT NULL,
                              ADD UNIQUE KEY uq_pr_source (tenant_id, source_id);
```

## 기존 코드 영향
- ✅ 추가만 (MigrateSalesDeliveriesAsync·MigratePurchaseReceiptsAsync 신규)
- ✅ pandataJobs에 2 잡 추가
- ⚠️ ResultDto에 2 필드 추가 (SalesDeliveries·PurchaseReceipts)
- ⚠️ ERP UI 거래명세서 페이지에 마이그 데이터 표시 (검증)
- ⚠️ Q4 그대로 이관 = `IJ_BUY` 음수값 그대로 = **헌법 #22 (데이터 최소주의) 검토 필요**
  - PM 보고: 사장님이 *"그대로 이관"* 결재했으므로 헌법 #22는 본사 전송 차단 영역 (고객 PC 내부 데이터)
  - = 충돌 없음. 본사로 안 보내면 OK.

## 효과
- 진범 #9 거래명세서 0행 → **약 124,152행 마이그**
- 헌법 #20 워크플로우 끊김 0 (수주→거래명세서→세금계산서)
- 사장님 격언 *"끝 숫자"* 4축 (영업·구매 미수금) 정합

## 산출물
- `MigrateSalesDeliveriesAsync` + `MigratePurchaseReceiptsAsync`
- `BulkCopyDeliveriesAsync` (collections·tax_invoices 동형)
- ALTER SQL (legacy_tax_no·source_id)
- TAXNO 연결 봉합 UPDATE
- commit 1~2건

---

# 🥈 WS-D-2 상세 명세 — DOCF7 회계 분개

## MDB 스키마 (PM 실측)

```
SC_KCODE    string(4)   — 계정코드 (1001, ...) — ERP 매니저 매핑표 필요
SC_SAWON    string      — 담당 (대부분 "공통")
SC_DT       char(8)     — 분개일자 YYYYMMDD
SC_SUN      int         — 분개 순번
SC_JEK      string      — 적요
SC_CR       decimal     — 대변 금액
SC_DR       decimal     — 차변 금액
SC_REM      string      — 비고
SC_GU       string      — 구분
SC_BNO      string      — 보조번호
```

## 분개 라인 패턴 분석
**한 행 = 한 라인** (차변 또는 대변 중 하나만 채워짐, 다른 한쪽 = 0).
헤더 그룹화: SC_DT + SC_SUN 동일 = entry 1건 + 차/대 라인 N건.

## 타겟 테이블 (운영 정합)

### journal_entries (헤더)
```
entry_id, tenant_id, entry_no, entry_date, source_type='migration',
source_id, employee_id, memo, created_at, updated_at
```

### journal_lines (차/대변 라인)
```
line_id, entry_id, tenant_id, account_id, side('debit'|'credit'),
amount, partner_id, memo, created_at
```

## Q5 ERP 매니저 작업 (선행 필수)

### SC_KCODE → 한국 표준 계정코드 매핑표 신규 작성

ERP 매니저가 정의해야 할 매핑 (예시 — 실제는 ERP 매니저 결재):

| SC_KCODE (히트판) | 한국 표준 (5자리) | 계정명 | 비고 |
|---|---|---|---|
| 1001 | ? | ? | ERP 매니저 결재 |
| ... | ... | ... | ... |

**매핑 파일 위치:** `sql/migrations/20260519_account_mapping_DOCF7.csv`
**매핑 컬럼:** legacy_code, standard_code, account_name, notes

PM은 ERP 매니저 매핑 완료 전 구현 진입 불가. 5/19~5/20 ERP 매니저 작업 대기.

## 구현 패턴 (ERP 매핑 완료 후)

### 1단계: 매핑 dict 로드
```csharp
var accountMap = await LoadAccountMappingAsync(...); // legacy_code → standard_code
```

### 2단계: 헤더 그룹화 + 라인 빌드
```csharp
var groups = dt.AsEnumerable().GroupBy(r => new {
    Dt = GetStr(r, "SC_DT"),
    Sun = GetInt(r, "SC_SUN")
});
foreach (var g in groups) {
    var entry = new JournalEntryRow { ... };
    foreach (var r in g) {
        var cr = GetDec(r, "SC_CR");
        var dr = GetDec(r, "SC_DR");
        var side = cr > 0 ? "credit" : "debit";
        var amount = cr > 0 ? cr : dr;
        var kcode = GetStr(r, "SC_KCODE");
        if (!accountMap.TryGetValue(kcode, out var stdCode)) continue; // 매핑 실패 skip + 로그
        lines.Add(new JournalLineRow { EntryId = entry.Id, Side = side, Amount = amount, AccountId = stdCode, ... });
    }
    // 차변·대변 균형 검증 (헌법 §회계 정합)
    var debitSum = lines.Where(l => l.EntryId == entry.Id && l.Side == "debit").Sum(l => l.Amount);
    var creditSum = lines.Where(l => l.EntryId == entry.Id && l.Side == "credit").Sum(l => l.Amount);
    if (debitSum != creditSum) _logger.LogWarning(...);
}
```

### 3단계: BulkCopy (WS-A 패턴)
header staging + lines staging 2단계.

### 4단계: source_id 멱등
```
sourceId = $"mig-docf7-{SC_DT}-{SC_SUN}"
```

## AutoJournalHelper 충돌 방지 (Q2 결재 반영)
- 마이그 분개: `source_type = 'migration'`
- 운영 분개: `source_type = 'sales' | 'purchase' | ...`
- → **격리됨, 충돌 없음**
- 단, **운영 자동 분개가 마이그 거래명세서를 또 분개하면 중복** = 운영 거래확정 시 `source_type='migration'` 검사 + skip 로직 신설 필요

### 추가 봉합 (PM 책임)
`AutoJournalHelper.RecordSalesConfirmAsync` · `RecordPurchaseConfirmAsync` 진입 전:
```csharp
if (sourceType == "migration") return; // 마이그된 거래는 분개 자동 중복 방지
```

## 기존 코드 영향
- ✅ 추가만 (MigrateJournalEntriesAsync 신규)
- ⚠️ AutoJournalHelper 2개 메서드에 skip 가드 추가 (운영 코드 변경, 정합성 검증 필수)
- ⚠️ 계정매핑 dict 누락 시 다수 라인 skip → 로그 모니터링 필수
- ⚠️ 차/대변 불균형 시 헌법 §회계 위반 — 경고만 + 강제조정 화면(WS 별도)

## 효과
- 진범 #12 회계 0행 → 약 27,664행 (entries 약 14,000건 추정 / lines 27,664)
- 사장님 격언 *"끝 숫자"* 4축 (회계 금액) 정합
- 회계관리자 화면 = 레거시와 끝숫자 일치 가능

## 산출물
- ERP 매핑 CSV (선행, ERP 매니저)
- `MigrateJournalAsync` + `BulkCopyJournalAsync`
- AutoJournalHelper skip 가드
- 차/대변 불균형 로그
- commit 1건

---

# 🥉 WS-D-3 상세 명세 — DOCFE 결재 라인

## MDB 스키마

```
IJA_DT      char(8)     — 결재일자
IJA_IO      smallint    — 1=매출, 2=매입
IJA_SEQ     int         — 시퀀스
IJA_BUY     int         — 거래처
IJA_SAWON   string      — 담당
IJA_AMT1~6  decimal     — 금액 6분류 (PM 추가 분석 필요)
IJA_LINE    int         — 라인
IJA_SSUN    int         — 결재 순번
IJA_SSUNH, SSUNH1, SSUNC, SSUNE — 결재 단계별 순번
```

## 미해결 영역
**IJA_AMT1~6 의미 확정 필요** — 사장님 또는 ERP 매니저 자문.

추정:
- AMT1 = 공급가액
- AMT2 = 부가세
- AMT3 = 합계 (AMT1+AMT2)
- AMT4·5·6 = 미사용 또는 추가 항목

## 사전 작업
1. **사장님 자문** — IJA_AMT1~6 의미 결재
2. AMT3 = AMT1 + AMT2 검증 (샘플 5행 일치)
3. SSUNH·SSUNC·SSUNE 4단계 결재 의미 추적

## 구현
- 1차: 단순 보조 테이블 `delivery_approval_log` 이관 (정보 보존)
- 2차: ERP 매니저 결재 후 결재 정합성 봉합

## 효과
- 거래명세서 결재 이력 보존 (감사 추적)
- 우선순위 낮음 — WS-D-1·D-2 완료 후

---

# 🏅 WS-D-4 상세 명세 — DOCF6 수금/지급 잔액

## MDB 스키마

```
AC_YMD      char(8)     — 일자
AC_JWASU    int         — 좌수
AC_JEN      smallint    — 종류 (2=수금/지급 추정)
AC_JEK      string      — 적요
AC_AMT      decimal     — 금액 (음수=출금)
AC_SBUY     int         — 관련 거래처 (수금 대상)
AC_SYMD     char(8)     — 원거래일
AC_SGU      string      — 구분
AC_SSUN     int         — 원거래 순번
AC_cheri    string      — 처리
```

## 분석
- collections·bank_transactions와 일부 중복 가능 (이미 마이그됨)
- AC_AMT 음수 → 출금/지급
- AC_SBUY·AC_SYMD·AC_SSUN = collections 원거래 연결

## 구현
**보류** — 기존 collections·bank_transactions와 중복 검증 후 결정.
DOCF6 = 수금/지급의 **잔액 보조 원장**일 가능성 = PM 추가 분석 필요.

---

## 📅 통합 일정 (5/19~5/22)

| 일자 | 작업 | 담당 |
|---|---|---|
| **5/18 (월) 잔여** | WS-D-1 ALTER 스키마 + 코드 골격 작성 | DB + 백엔드 |
| **5/19 (화) AM** | WS-D-1 구현 (DOCFB → deliveries+receipts) | 백엔드 |
| **5/19 (화) AM** | WS-D-2 SC_KCODE 매핑표 작성 (ERP 매니저) | ERP 매니저 |
| **5/19 (화) PM** | WS-D-1 BulkCopy + TAXNO 연결 봉합 | 백엔드 + DB |
| **5/20 (수) AM** | WS-D-1 실측 검증 | 사장님 + PM |
| **5/20 (수) PM** | WS-D-2 구현 (DOCF7 → journal) | 백엔드 + ERP |
| **5/21 (목) AM** | WS-D-2 실측 + AutoJournalHelper skip 가드 | 백엔드 |
| **5/21 (목) PM** | WS-D-3 DOCFE 분석 + IJA_AMT 사장님 자문 | ERP + PM |
| **5/21 (목) PM** | WS-D-4 DOCF6 중복 검증 | DB |
| **5/22 (금)** | 본런 검증 (끝 숫자 4축) | 사장님 + 전팀 |

---

## ⚠️ 헌법 정합 체크

| 헌법 | 정합 여부 |
|---|---|
| #2 tenant_id JWT만 | ✅ 코드에서 tenant_id 파라미터 미수신, JWT 클레임 사용 |
| #3 INSERT ONLY 원장 | ✅ journal_lines INSERT만, UPDATE/DELETE 0 |
| #6 confirmed 시점 원장 | ✅ status='confirmed' 마이그 분개 |
| #13 DESCRIBE 의무 | ✅ sales_deliveries·journal_lines DESCRIBE 선행 |
| #18 본사 전송 금지 | ✅ 본사 미전송 |
| #20 워크플로우 끊김 0 | ✅ 거래명세서·세금계산서·분개 연결 복구 |
| #22 본사 데이터 최소주의 | ✅ Q4 그대로 이관 = 고객 PC만, 본사 0 |
| #26 v3 목표-Timeout 분리 | ✅ BulkCopyTimeout=86400, 정공법 5축+α |

---

**작성: PM 브라운킴 2026-05-18**
**문서 ID:** ws-f-20260518-docfb-docf7-docfe-docf6
**다음 액션:** PM 코드 골격 작성 진입 + ERP 매니저 SC_KCODE 매핑표 작업 의뢰
