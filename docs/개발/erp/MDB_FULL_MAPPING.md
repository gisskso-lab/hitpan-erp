# 레거시 히트판 MDB ↔ 신 히트판 ERP 전체 매핑 표

> **작성:** 2026-05-12 W1 D1~D2 PowerShell 실측 결과
> **소스:** `C:\HITWINLAN10\PYOJUN.MDB`, `PANDATA.mdb`, `POTHER.mdb` (사장님 테스트 셋업)
> **검증:** 본부장 + DB매니저 + ERP매니저 공동 정독
> **단일 진실 원천:** 미래 모든 마이그 작업지시서 본 표 인용

---

## 1. 종합 — 32개 테이블 발견

```
PYOJUN.MDB     6개 (마스터·셋업)
PANDATA.mdb   18개 (거래·집계·보조)
POTHER.mdb     8개 (달력·명함·AS·메모·일정·락)
────────────────────────────────
총 32개  (코드 매핑 23개 + 신규 발견 9개)
```

**사장님 테스트 셋업 데이터:**
- COSTNO 33건 (계정코드 마스터)
- SETUP 172건 (시스템 설정)
- CALENDAR 7,305건 (20년치 영업일+환율)
- DOCSC 274건 (사원 일정 샘플)

---

## 2. PYOJUN.MDB — 마스터 (6 테이블)

| # | 테이블 | PK | 행 | 컬럼 | 신 히트판 매핑 | 상태 |
|---|---|---|---|---|---|---|
| 1 | **COSTNO** | CT_CODE | 33 | 3 | `accounts` / `expense_categories` | 🔴 코드 0 — 신규 |
| 2 | **DOCF8** | buy_code | 3 | **41** | `partners` | 🟡 22/41 매핑 — 19개 보강 |
| 3 | **DOCFS** | (S_PUM, S_KU) | 0 | 21 | `items` | 🟡 13/21 매핑 |
| 4 | **DOCRT** | (RT_PUM, RT_KU, RT_SUN) | 0 | 10 | `bom_headers` + `bom_items` | ✅ 작동 |
| 5 | **DOCSW** | SW_NAME | 0 | **36** | `employees` | 🟡 8/36 매핑 — 28개 보강 |
| 6 | **SETUP** | SET_CODE | 172 | 2 | `tenant_settings` | 🔴 코드 0 — 신규 |

### DOCF8 41개 컬럼 — 누락 19개

```
✅ 매핑됨 (22):
  buy_code, buy_gu, buy_name, buy_taxno, buy_top, buy_euptae,
  buy_eupjong, buy_tel, buy_fax, buy_addr, buy_addr1, buy_postno,
  buy_yeasin, buy_bank, buy_bankno, buy_bankname, buy_damdang,
  buy_damdang1, buy_taxgubun, buy_rem~rem6 (7개)

🔴 누락 (19):
  buy_cardyul     Currency   카드 수수료율
  buy_ccode       Text30     분류 코드
  buy_damdangbu   Text20     담당 부서
  buy_DOSCODE     Text5      ⭐ 단가등급 후보 (옵션 B 핵심)
  buy_fil         Text20     예비 필드
  buy_halyul      Currency   할인율
  buy_keybirth    Text4      키맨 생일
  buy_keyname     Text50     키맨 이름
  buy_keytel      Text18     키맨 연락처
  buy_mayul       Currency   마진율
  buy_sawon       Text20     담당 영업사원
  buy_startdt     Text10     거래 시작일
  buy_taxdt       Text10     사업자등록일
  buy_tel1        Text18     전화 2번
  buy_topjumin    Text14     ⚠️ 대표 주민번호 (헌법 #18 형사) AES-256 필수
```

### DOCSW 36개 컬럼 — 누락 28개

```
✅ 매핑됨 (8):
  SW_NAME, SW_JIKKUB(직급), SW_JIKCHAK(직책), SW_HP(휴대폰),
  SW_IBSAIL(입사일), employee_id, emp_no, role

🔴 누락 (28):
  SW_ADDR        Text80     주소
  SW_POSTNO      Text7      우편번호
  SW_BIRTH       Text10     생년월일
  SW_BIRTHgu     TinyInt    양음력
  SW_BIRTHtel    TinyInt    음력 변환
  SW_JUMIN       Text14     ⚠️ 주민번호 (헌법 #18 형사) AES-256 필수
  SW_HONIN       Text1      혼인 여부
  SW_BB          Text40     예비
  SW_BUSEA       Text30     부서
  SW_PAY         Int32      ⚠️ 급여 (헌법 #18 형사)
  SW_PAYgu       TinyInt    급여 구분
  SW_PAYeuy      TinyInt    급여 유형
  SW_PAYkuk      TinyInt    급여 국가?
  SW_PAYoth      Text100    급여 기타
  SW_TEA         TinyInt    퇴사 여부
  SW_TEADT       Text8      퇴사일
  SW_TEARESON    Text50     퇴사 사유
  SW_TEL         Text18     집전화
  SW_TELem       Text20     비상연락
  SW_BAL1~10     Text120    잔액 10개 (용도 미상)
  SW_REM         Text60     비고
  SW_nation      Text20     국적
```

---

## 3. PANDATA.mdb — 거래 (18 테이블)

| # | 테이블 | PK | 행 | 컬럼 | 신 히트판 매핑 | 상태 |
|---|---|---|---|---|---|---|
| 1 | **DOCF1** | (KA_NO, KA_NO1, KA_NO2) | 0 | 14 | `sales/purchase_order_items` | ✅ |
| 2 | **DOCF2** | K2_NO | 0 | 16 | `sales/purchase_orders` | 🟡 status=draft 검토 |
| 3 | **DOCF4** | (TX_IO, TX_NO) | 0 | **35** | `tax_invoices` | 🟡 4품목 + 전송일 |
| 4 | **DOCF5** | (S_BUY, S_YMD, S_SUN, S_GU) | 0 | 12 | `collections` | ✅ |
| 5 | **DOCF6** | (AC_YMD, AC_JWASU, AC_JEN) | 0 | 10 | `cashbook` | ✅ |
| 6 | **DOCF7** | (SC_KCODE, SC_DT, SC_SAWON, SC_SUN) | 0 | 10 | `expenses` / `journal_lines` | ✅ |
| 7 | **DOCF9** | (EU_CLA, EU_NO) | 0 | 15 | `bills` (어음발행) | ✅ |
| 8 | **DOCFA** | (IU_NO, IU_SUN) | 0 | 22 | `purchase_orders` | ✅ |
| 9 | **DOCFB** | (IJ_DT, IJ_IO, IJ_SEQ, IJ_BUY, IJ_SUN) | 0 | 18 | `stock_ledger` | ✅ |
| 10 | **DOCFC** | (IM_YM, IM_CHANG, IM_PUM, IM_KU) | 0 | 17 | `monthly_inventory_summary` | 🔴 코드 0 — 신규 |
| 11 | **DOCFE** | (IJA_DT, IJA_IO, IJA_SEQ, IJA_BUY) | 0 | 18 | `transaction_extras` 추정 | 🔴 코드 0 — 보강 |
| 12 | **DOCFO** | (IO_NO, IO_SUN) | 0 | 22 | `sales_orders` | ✅ |
| 13 | **DOCFQ** | (EQ_CLA, EQ_NO) | 0 | 10 | `bills` (어음만기) | ✅ |
| 14 | **BANKF** | (BK_NO, BK_YMD, BK_JWASU, BK_JEN) | 0 | 12 | `bank_transactions` | ✅ |
| 15 | **DOCCD** | (CD_CLA, CD_CDNO, CD_SNO) | 0 | 17 | `card_payments` | ✅ |
| 16 | **DOCCD1** | (CD1_NO, CD1_YMD, CD1_JWASU, CD1_JEN) | 0 | 12 | `card_payment_lines` | ✅ |
| 17 | **DOCLT** | LT_MK | 0 | 2 | ❌ 마이그 불필요 | ✅ 제외 |
| 18 | **REMARK1** | REM_CODE | 0 | 5 | ❌ memo 통합 | ✅ 제외 |

### DOCF4 (세금계산서) 35컬럼 핵심

```
🌟 발견: 전자세금계산서 발행 이력 컬럼 4개

TX_READDT     Text8    국세청 READ 일자
TX_REPORTDT   Text8    국세청 REPORT 일자
TX_SENDDT     Text8    전송 일자
TX_PDT        Text8    발행 일자

→ 마이그 시 기 발행 이력 그대로 보존 가능
→ 신 히트판 etax_send_history 신설 필요
```

### DOCFC (월별 재고 마감) — 신규

```
IM_YM           년월
IM_CHANG        창고
IM_PUM, IM_KU   품목/규격
IM_BAMT/BQTY    기초 (월초 잔액)
IM_IAMT/IQTY    입고
IM_OAMT/OQTY    출고
IM_CAMT/CQTY    당기?
IM_AMTS/QTYS    잔량 (월말)
IM_ISQTY/OSQTY  추가 수량

→ 월별 재고 마감 = 전기 이월 잔액 추적
→ 신 히트판 자동 집계로 대체 가능 (마이그 0 안 한다)
→ 또는 monthly_inventory_summary 신설
```

---

## 4. POTHER.mdb — 기타 (8 테이블)

| # | 테이블 | PK | 행 | 컬럼 | 신 히트판 매핑 | 상태 |
|---|---|---|---|---|---|---|
| 1 | **CALENDAR** | CALENDAR_YMD | **7,305** | 12 | `tenant_calendars` | 🔴 코드 0 — 신규 (사장님 결정) |
| 2 | **DELIVERY** | (5컬럼 복합) | 0 | 15 | `deliveries` | 🟡 베타 후 (사장님 결정) |
| 3 | **DOCAS** | (AS_DT, AS_TM, AS_BUY) | 0 | 17 | `as_records` | 🟡 베타 후 |
| 4 | **DOCAS1** | AS1_BUY | 0 | 14 | `as_records_details` | 🟡 베타 후 |
| 5 | **DOCME** | (4컬럼 복합) | 0 | 10 | `employee_memos` | 🟡 베타 후 (HTPMEMO.exe) |
| 6 | **DOCNM** | (nam_OWNER, nam_name) | 0 | 34 | `business_cards` | 🟡 베타 후 |
| 7 | **DOCSC** | (SC_DATE, SC_TIME, SC_SAWON) | 274 | 8 | `employee_schedules` | 🟡 베타 후 |
| 8 | **LOCK1** | LOCK_CODE | 0 | 5 | ❌ 마이그 불필요 | ✅ 제외 |

### CALENDAR 7,305건 — 보석 발견

```
CALENDAR_YMD      날짜 (8자리)
CALENDAR_WEEK     요일
CALENDAR_WORK     영업일 여부 ⭐
CALENDAR_DESC     설명
CALENDAR_REM1~5   메모 5개
CALENDAR_ERATE_VND  베트남 환율 ⭐
CALENDAR_ERATE_WON  원화 환율 ⭐

= 20년치(7,305일) 표준 데이터
= 신규 고객에게 즉시 제공 가능
= 환율 데이터까지 보유 (수출 거래처 강점)
```

---

## 5. PK 체크포인트 가능성 — 100% 작동

```
[단일 PK] (10개)
  COSTNO, DOCF8, DOCSW, SETUP, DOCF2,
  REMARK1, DOCLT, LOCK1, CALENDAR, DOCAS1

[복합 PK 2~3컬럼] (12개)
  DOCFS, DOCRT, DOCF1, DOCFA, DOCFO,
  DOCF4, DOCF9, DOCFQ, DOCNM, DOCSC, DOCAS

[복합 PK 4~5컬럼] (10개) ⚠️ JSON 직렬화 필요
  DOCFB(5), DOCF5(4), DOCF6(3), DOCF7(4),
  DOCFC(4), DOCFE(4), BANKF(4),
  DOCCD(3), DOCCD1(4), DOCME(4), DELIVERY(5)
```

**결론:** 모든 32개 테이블 `last_pk_value` 체크포인트 100% 작동 가능.
DOCFB 5컬럼 PK는 JSON 직렬화 보관 (`{"IJ_DT":"20251231","IJ_IO":"O","IJ_SEQ":99,...}`).

---

## 6. 마이그 영역 5등급 분류

### 🟢 A등급 — 즉시 작동 (9개)
DOCRT, DOCF1, DOCF2, DOCF5, DOCF6, DOCF7, DOCF9, DOCFA, DOCFO, DOCFQ, DOCFB, DOCCD, DOCCD1, BANKF

→ 코드 작동, 누락 컬럼 일부만 보강

### 🟡 B등급 — 보강 필요 (3개)
DOCF8 (19컬럼 보강), DOCFS (8컬럼 보강), DOCSW (28컬럼 보강 + 주민번호·급여 AES)

### 🔴 C등급 — 신규 마이그 코드 (5개)
COSTNO, SETUP, DOCFC, DOCFE, CALENDAR

### 🟡 D등급 — 베타 후 신규 (5개)
DELIVERY, DOCAS, DOCAS1, DOCME, DOCNM, DOCSC

### ✅ E등급 — 마이그 불필요 (3개)
DOCLT, REMARK1, LOCK1

### 🔴 F등급 — 신 히트판 신규 작업 (1개)
DOCF4 → tax_invoices + **etax_send_history 신설**

---

## 7. 사장님 결재 사항 — 확정

| # | 사항 | 결재 |
|---|---|---|
| 1 | 9개 신규 테이블 마이그 정책 | ✅ 진행 (사장님 OK) |
| 2 | DOCF8 19개 컬럼 보강 + DOCSW 28개 보강 | ✅ 진행 |
| 3 | 주민번호·급여 AES-256 암호화 필수 | ✅ 헌법 #5·#18 부합 |
| 4 | 단가등급 옵션 B 가능성 (buy_DOSCODE) | W2 실측 후 확정 |
| 5 | etax_send_history 신설 | ✅ DOCF4 전송 이력 보존 |
| 6 | CALENDAR 7,305건 즉시 마이그 | ✅ |
| 7 | DOCFC 월별 재고 마감 → 자동 집계 대체 | 🟡 추후 결정 |

---

## 8. 다음 단계 (W1 D3~D5)

```
[W1 D3 오늘 오후]
  ⭐ 본 매핑 표 작성 ✅ 완료
  - 3개 인프라 테이블 DDL 설계 (migration_jobs/checkpoints/errors)
  - 4개 API 스펙 설계서

[W1 D4 내일]
  - 5개 클래스 분리 설계 (설계팀장)
  - DOCF4 35컬럼 + DOCFS 21컬럼 정독

[W1 D5 모레]
  - 인프라 통과 게이트 점검 (Week 1 게이트)
  - 헌법 위반 0 검증
```

---

**작성자:** 본부장 춘식 + ERP매니저 + DB매니저
**검토:** 설계팀장 브라운킴, 보안매니저, 백엔드매니저
**최종 검증:** CTO 래리 앨리슨
**서명:** 사장님 결재 완료 (2026-05-12)
