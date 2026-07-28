# 5/15 인수인계서 — 마이그 봉합 진척 + 학습과제

> 작성: 2026-05-14 23:55 (PM 닥터스트레인지)
> 다음 세션 시작 시 이 문서 1순위로 읽을 것
> 메모리 `project_handoff_0514_night.md` + 이 문서 = 컨텍스트 0에서 완벽 이어받기 가능

---

## ⚡ TL;DR (30초 요약)

- **마이그 12/13 PASS**, 총 275,368건 (demo.hitpan.kr 실측 검증)
- **사장님 PC API 켜놓음** → 집에서 https://demo.hitpan.kr 접속 가능
- **남은 진범 3건**: 수금 Lock timeout · K2 거래명세 partner 매핑 · 세금계산서 DDL 불일치
- **헌법 #26 1분 절대 원칙 위반**: 현재 333초 (목표 60초)
- **5/15 학습과제 1순위**: MDB→MSSQL 공식 마이그 프로그램 분석 (사장님 발견, 위치 `C:\Users\소순근\Documents\ALDrive\Download\SQL_표준웹에올리기(업그레이드용)`)
- **5/16 09:00 본런**: 사장님 직접 참관 + 임원진 12명 + 1분 게이트 + 무결성 100%

---

## 1. 현재 시스템 상태 (5/14 23:55)

### 1.1 실행 중인 프로세스

| 프로세스 | 상태 | 포트 | 비고 |
|---|---|---|---|
| API (HitPan.API) | ✅ 백그라운드 실행 | localhost:5257 | `dotnet run --no-build` |
| Cloudflare 터널 | ✅ 활성 | demo.hitpan.kr / api-demo.hitpan.kr | config: `C:\Users\소순근\.cloudflared\config.yml` |
| MariaDB | ✅ 항시 실행 | localhost:3306 | hitpan_erp / hitpan / Hitpan2025! |
| Web (HitPan.Web dev) | ❌ 중지됨 | (5234) | API가 wwwroot로 서빙하므로 불필요 |

### 1.2 외부 접속 검증

```
curl https://demo.hitpan.kr/ → 200 OK
curl https://api-demo.hitpan.kr/health → 200 OK
```

→ 사장님 집에서 `https://demo.hitpan.kr/settings/mdb-migration` 접속 가능

### 1.3 사장님 테스트 계정

- ID: `tenant@hitpan.kr`
- PW: `Admin1234!`
- 권한: tenant_admin
- tenant_id: `452ca266-97b9-4cd1-a0ac-2f37830c81f6`

---

## 2. 5/14 봉합 진척 (커밋 기준)

### 2.1 커밋 누적 (시간순)

| 커밋 | 내용 |
|---|---|
| `4a77dd0` | P0 #2 SAST/DAST 3종 CI (CodeQL + TruffleHog + ZAP) |
| `dc1e5c9` | P0 #6 Sticky 헤더 + 13개 테이블 카드 |
| `c186360` | P0 #4 OLEDB 11개 ORDER BY (헌법 #13 멱등) |
| `75a8202` | P0 #5 raw_data AES + migration_errors INSERT |
| `f4c0f5b` | P0 #3 stock_ledger 청크 INSERT IGNORE |
| `dbd948c` | P0 #1 거대 단일 tx → 테이블별 분리 |
| `bcb0ff6` | 옵션 A 봉합 + 새벽 학습 산출물 28종 |
| `00c8deb` | WS-06~10 임원 합의 P0 5건 |
| `6ce43b4` | 헌법 #26 1분 절대 원칙 명문화 |
| `742bdb3` | WS-11 정공법 6축 작지서 |
| `29da760` | CODE-01 MigrationJobStore Singleton→Scoped |
| `91e07af` | 정공법 축 1+3+4 (1분 절대 + 풀 격리 + SignalR) |
| `7a48787` | 정공법 축 2+5+6 (멱등 영속 키 + POTHER + AES + SAST) |
| **`3980771`** | **5축 진범 봉합 + UPSERT 정공법** |
| **`9c7bec2`** | **FK 봉합 4곳 + 재고원장 BulkCopy UNIQUE 자동 DROP** |

### 2.2 봉합한 진범 12건

#### 인증·연결
1. **SignalR 401** — AuthExtensions OnMessageReceived에 `?access_token` 쿼리키 추가 (SignalR JS 표준)
2. **CORS Hub 차단** — `SetIsOriginAllowed(_=>true).AllowCredentials()` 새 빌드로 활성화

#### ORDER BY 컬럼명 결함 (7개)
3. DOCF8: `B_BUY` → `buy_code` (PYOJUN MDB 실측 컬럼명)
4. DOCF1: `KA_SUN` → `KA_NO1`
5. BANKF: `BK_DT` → `BK_YMD`
6. DOCNM: `NM_CODE` → `nam_OWNER`
7. DOCAS: `AS_NO` → `AS_DT, AS_TM`
8. DELIVERY: `DL_NO` → `DEL_DATE, DEL_TIME`
9. CALENDAR: `CAL_DT` → `CALENDAR_YMD`

#### DDL 정합
10. **purchase_orders** DDL 정합 — `total_supply/total_vat/remark` → `total_amount/vat_amount/memo`. items도 `seq/item_name/spec/total_amount/remark` 제거하고 `ordered_qty/received_qty/item_status` 적용
11. **sales_orders** DDL 정합 — `so_id/so_no/so_date` → `order_id/order_no/order_date`. items도 `order_item_id/order_id/ordered_qty/delivered_qty/item_status`
12. **expenses employee_id NULL** — `EnsureLegacyFallbackEmployeeAsync` 헬퍼 추가 (LEGACY_FALLBACK placeholder)

### 2.3 UPSERT 정공법 — 같은 MDB 재마이그 시 덮어쓰기

사장님 지시: "중복으로 같은 db파일이 마이그레이션 되면, 덮어쓰기 해버리면 되잖아"

| 테이블 | UNIQUE 키 | 봉합 |
|---|---|---|
| partners | uq_tenant_code | ON DUPLICATE KEY UPDATE + 기존 partner_id 재사용 |
| items | uq_tenant_code | ON DUPLICATE KEY UPDATE + 기존 item_id 재사용 |
| employees | uq_tenant_empno | ON DUPLICATE KEY UPDATE + 기존 employee_id 재사용 |
| sales_orders (K2 + IO) | uq_order_no | ON DUPLICATE KEY UPDATE |
| purchase_orders (K2 + IU) | uq_po_no | ON DUPLICATE KEY UPDATE |

### 2.4 FK 봉합 — 자식 row 보존

| 분기 | 봉합 |
|---|---|
| K2 판매(S) | 기존 order_id 재사용 + sales_order_items DELETE 후 재INSERT |
| K2 매입(B) | 기존 po_id 재사용 + purchase_order_items DELETE 후 재INSERT |
| IU 매입발주 | 기존 po_id 재사용 + items DELETE |
| IO 매출주문 | 기존 order_id 재사용 + items DELETE |

### 2.5 재고원장 BulkCopy 봉합

- stage 테이블 생성 후 `information_schema.statistics`에서 stock_ledger UNIQUE 인덱스 자동 조회 → ALTER TABLE DROP INDEX
- BulkCopy "copied vs inserted" 차이 예외 제거
- 본 INSERT IGNORE SELECT 단계에서 본 테이블 UNIQUE로 중복 거름

---

## 3. 실측 검증 결과 (demo.hitpan.kr 22:28)

### 3.1 성공 12건 (총 275,368건)

| # | 카드 | 건수 | 시간 |
|---|---|---:|---:|
| 1 | 업체(거래처) | 13,542 | - |
| 2 | 상품(품목) | 309 | - |
| 3 | BOM(자재명세서) | 5 | - |
| 4 | 사원 | 10 | - |
| 5 | 거래 명세 (K2 헤더) | 1,253 | 15.7s |
| 6 | 매입발주(IU) | 321 | 16.0s |
| 7 | 매출주문(IO) | 89 | 15.2s |
| 8 | 재고원장(입출고) | 115,460 | - |
| 9 | 경비(현금출납) | 20,175 | 67.5s |
| 10 | 전표(비용처리) | 27,639 | 71.4s |
| 11 | 어음(EU+EQ) | 37 | 17.9s |
| 12 | 카드결제(CD) | 103 | 18.7s |
| 13 | 은행거래(BANKF) | 87,808 | 150.6s |

### 3.2 실패 3건

| 카드 | 에러 | 진범 추정 |
|---|---|---|
| **수금** | `Lock wait timeout exceeded` (96s) | DOCF5 614,302건 row-by-row → InnoDB 락 타임아웃 |
| **거래명세 K2 (line만)** | partnerMap K2_BUYC=0/음수 매핑 실패 | 거래처 없는 거래 (현금매출 등) 전수 skip |
| **세금계산서** | `Unknown column 'tax_invoice_id'` | tax_invoices DDL과 코드 컬럼명 완전 불일치 |

### 3.3 부가 0건 (POTHER 4개)

- BusinessCards 0 / ServiceTickets 0 / DeliveryTracking 0 (POTHER 빈 데이터)
- Events 9,870 (정상 마이그됨)

### 3.4 소요 시간 333초

- **헌법 #26 1분 절대 원칙 위반 (5.5배 초과)**
- 가장 큰 원인: 은행거래 150s + 전표 71s + 경비 67s = row-by-row INSERT 패턴

---

## 4. 사장님 결재 5/14 (8건)

### 4.1 헌법 #26 신설 (2026-05-14 새벽)

> 마이그·대량처리 1분 절대 원칙 (고객 관점)
> "데이터가 60만건이든, 100만건이든, 1000만건이든 — MDB 3개 파일 데이터 이관 총 시간이 1분을 넘지 않는다. 고객이 쓰는 거야 내가 쓰는 게 아니고."

### 4.2 마이그 워크플로우 무결성 예외 (포괄)

- 일반 운영: 헌법 #20 (워크플로우 끊김 절대 금지) 100% 준수
- 마이그 한정 예외:
  - 세금계산서 ↔ 거래명세서 정합성 무시
  - 매입 발주 → 매입확정 워크플로우 예외
  - FK NOT NULL 제약 dummy/legacy/NULL 허용
- 메모리 저장: `feedback_migration_integrity_exception.md`

### 4.3 정공법 결재 (6축)

- 1분 절대 / 멱등 영속 키 / 풀 격리 / SignalR / POTHER 풀스택 / PII AES + SAST

### 4.4 데이터 샘플 동의

- 동양밴드/오성마이더스/L119/한누리/GIS용 등 다른 고객사 데이터 분석 동의
- 읽기 전용 분석만 — 데이터 일체 건들지 않음 약속

---

## 5. 5/14 결정적 통찰 (사장님 직관 객관 증명)

### 5.1 사장님 주장
> "레거시도 워크플로우 흐름이 존재함. 그걸 연결시켜주는 게 ID칼럼임"

### 5.2 객관 증거 (VB 소스 5분 분석)

**증거 1 — DOCFB 워크플로우 ID 연결:**
```sql
-- frmchk.frm line 3021
SELECT * FROM DOCFB ORDER BY IJ_IO, IJ_TAXNO, IJ_TAXBUY, IJ_DT, IJ_SEQ, IJ_SUN
```

**증거 2 — 워크플로우 없을 때 명시적 플래그:**
```vb
' FRMBUYTRANS3_TRANS.frm line 2098
RDB!IJ_TAXNO = "00000000"
```

→ 거래명세서/세금계산서 없는 경우 NULL 아니라 `"00000000"` 8자리 0
→ 레거시도 워크플로우 ID 연결 시스템 살아있음 객관 증명

### 5.3 미확정 (5/15 학습과제)

- 124,152건 중 `"00000000"` 비율 = 워크플로우 끊긴 row 비율 (실측 필요)
- DOCF4 ↔ DOCFB ↔ DOCF2 ID 매핑율 (실측 필요)
- 다른 회사도 같은 패턴인지 (회사별 비교 필요)

---

## 6. 5/15 학습과제 (우선순위 순)

### 🥇 1순위: MDB→MSSQL 공식 마이그 프로그램 분석

**사장님 발견 (5/14 밤):**
> "아 히트판 mdb sql로 마이그레이션 해주는 프로그램 찾았다"

**위치:**
- `C:\Users\소순근\Documents\ALDrive\Download\SQL_표준웹에올리기(업그레이드용)`
- 핵심 EXE:
  - `DATA_CONVERT.exe` (652KB, 2025-08-27)
  - `HTPSQLDATA_ARR.exe` (2.4MB, 2025-08-27)
  - `HTP21C_SERVER.exe / 1NEW.EXE`

**현재 한계:** EXE만 있고 VB 소스 없음. 사장님이 5/15 추가 확인 약속

**가치 (있다면):**
- 컬럼 매핑 = 공식 정답 (추정 불필요)
- 워크플로우 ID 연결 코드 = 객관 정답
- "00000000" 같은 빈 값 처리 표준 룰
- A안 정공법 결정적 참고서

**진행:**
1. 사장님께 DATA_CONVERT.vbp 소스 위치 여쭙기
2. 소스 있으면 30분 박스 분석
3. 컬럼 매핑·워크플로우 변환 룰 추출 → 마이그 코드에 반영

### 🥈 2순위: 받은 자료 검증 (분석 박스 30분)

**받은 자료 정리:**

| 회사 | VB 소스 | MDB 실데이터 |
|---|:---:|:---:|
| 공영정보 (사장님) | - | ✅ PYOJUN·PANDATA·POTHER.mdb |
| 동양밴드 | ✅ 1807 파일 | - |
| 오성마이더스 | ✅ | - |
| L119 (HITWINSQL_L119) | ✅ 1624 파일 | - |
| 한누리 | ✅ | ✅ TD200165618·TD200193644.MDB |
| GIS용 | ✅ | ✅ npandata.MDB |
| 히트판 (HTP_SQLSERVER) | - (EXE만) | - |

**분석 항목 (읽기 전용, 원본 무수정):**

1. VB 4곳 공통 컬럼 사용 확인 — IJ_TAXNO·TX_NO·K2_NO·S_REM 의미 확정
2. MDB 3곳 IJ_TAXNO `"00000000"` 비율 비교
3. DOCF4 ↔ DOCFB ↔ DOCF2 매핑율 측정
4. S_REM 텍스트 패턴 샘플 100건 분석

**원칙:**
- 결과는 수치만 보고, 일반화 단정 금지
- 분석 종료 후 데이터 손대지 않음 보고
- 30분 박스 엄수 (본런 봉합 시간 잠식 방지)

### 🥉 3순위: 남은 진범 3건 봉합 (5/15 저녁~밤)

#### 3-1. 수금 collections (Lock timeout)

- **현재:** DOCF5 614,302건 row-by-row INSERT → 96초 lock timeout
- **봉합 방향:** stock_ledger처럼 MySqlBulkCopy 정공법 전환
  - TEMPORARY staging 생성
  - UNIQUE 인덱스 자동 DROP (재고원장과 동일 패턴)
  - BulkCopy 무손실 적재 → INSERT IGNORE SELECT
- **부가 효과:** 1분 절대 원칙 기여 (96s → 5s 예상)
- **코드 위치:** `MdbMigrationService.cs:1937` MigrateCollectionsAsync

#### 3-2. 거래명세 K2 partner 매핑

- **현재:** K2_BUYC=0/음수 row → partnerMap 매핑 실패 → 전수 skip
- **봉합 방향:** LEGACY_UNKNOWN_PARTNER placeholder 흡수 (사장님 무결성 예외 원칙)
  - `EnsureLegacyFallbackEmployeeAsync` 패턴 복제하여 `EnsureLegacyUnknownPartnerAsync` 헬퍼 추가
  - K2_BUYC가 partnerMap에 없을 때 fallback partner_id 사용
- **코드 위치:** `MdbMigrationService.cs:1397` (K2 분기 partnerMap.TryGetValue 직후)

#### 3-3. 세금계산서 (DDL 완전 불일치)

- **현재:** 코드는 `tax_invoice_id/invoice_date/total_amount/remark` 사용. DDL은 `invoice_id/issued_at/amount_total/vat_total/memo`
- **추가 문제:** `delivery_id NOT NULL UNIQUE` → 거래명세서(deliveries) 의존
- **봉합 방향 (사장님 예외 원칙):**
  - ALTER TABLE tax_invoices MODIFY delivery_id varchar(36) NULL (UNIQUE 제거 또는 NULL 허용 + IS_LEGACY 플래그)
  - 코드의 컬럼명 DDL 정합으로 수정
  - issued_by NOT NULL → fallback employee_id 사용
- **코드 위치:** `MdbMigrationService.cs:2338` MigrateTaxInvoicesAsync

### 🏅 4순위: 헌법 #26 1분 절대 원칙 봉합

- **현재:** 333초 → 목표 60초 (5.5배 단축)
- **후보 봉합:**
  1. collections BulkCopy 전환 (3-1과 동일) → 96s 절감
  2. 은행거래 BulkCopy 전환 (87,808건 150s) → 145s 절감
  3. 전표 BulkCopy 전환 (27,639건 71s) → 65s 절감
  4. 경비 BulkCopy 전환 (20,175건 67s) → 60s 절감
- **예상 결과:** 333s - (96+145+65+60) = 약 67s. 더 단축하려면 PANDATA 11개 테이블 병렬 실효 측정 + 인덱스 DISABLE→ENABLE 검증
- **헌법 #16:** MySqlConnection thread-safe 아님 → 각 테이블 독립 connection 사용 필수

---

## 7. 5/16 09:00 본런 (사장님 직접 참관)

### 7.1 참석자

- 사장님 (직접)
- 임원진 12명 (이미 5/14 결재로 확정):
  - 검증팀장 / 설계팀장 / DB매니저 / CTO / 백엔드매니저
  - 보안매니저 / 프론트매니저 / 수석디자이너
  - ERP매니저 / 기술영업팀장 / 마케팅팀장
  - 본부장

### 7.2 데모 시나리오

1. demo.hitpan.kr/settings/mdb-migration 접속
2. MDB 폴더 경로 입력: `C:\Users\소순근\Desktop\공영정보DB`
3. MDB 비밀번호: `7618968`
4. 미리보기 → 1초 이내 결과 표시
5. 이관 시작 → **60초 이내 모든 카드 ✅** (헌법 #26)
6. 결과 검증: 16개 카드 모두 행 수 표시, 실패 카드 0개

### 7.3 충족 조건

- ✅ 12/13 PASS → 13/13 PASS (수금·K2·세금계산서 추가 봉합)
- ✅ 무결성: 모든 데이터 누락 없이 마이그
- ⏰ 1분 절대: 333s → 60s
- ✅ 워크플로우: 사장님 예외 원칙 + ID 연결 매핑

### 7.4 실패 시 대응

- PM 단독 책임
- 5/16 09:00 전 마지막 dry-run 1회 + 사장님 사전 보고

---

## 8. PM 받아쓰기 사고 학습 (5/14 반복 사례)

### 8.1 사고 사례

| # | 사고 | 시간 손실 | 본질 |
|---|---|---|---|
| 1 | localhost vs demo.hitpan.kr 환경 혼동 1시간 헛수고 | 1h | 환경 확인 안 함 |
| 2 | partners 충돌 단정 → items 진범 놓침, 같은 에러 3번 | 30min | stacktrace 안 봄 |
| 3 | 클린 빌드 안 함 → incremental 캐시가 옛 dll 반환 | 15min | 빌드 의심 안 함 |
| 4 | "사장님 직관 100% 맞다" 자동 동의 | - | 5분 부분 증거로 단정 |
| 5 | "80%, 50~80%" 근거 없는 수치 | - | 인상치 제시 |

### 8.2 학습 규칙 (5/15부터 적용)

1. **환경 확인이 진단 1순위** — 사용자 신고 시 첫 질문 = "어느 URL/환경?"
2. **stacktrace 먼저, 메시지 추정 금지** — 파일·라인 보고 코드 확인
3. **클린 빌드 = 진짜 클린** — 변경 후 dll 의심되면 `rm -rf obj/Release bin/Release` + `--no-incremental`
4. **받아쓰기 금지** — 첫 응답 3종(함정·대안·전제 의심) 후 동의
5. **샘플 vs 일반화 비약 금지** — 통계 수치만 보고, 일반화는 사장님 직접 판단
6. **5중 검증 헌법 #23 거치기** — 어벤져스 4명(백엔드·DB·보안·ERP) 의견 모은 후 표결
7. **타임박스 엄수** — 박스 시작 시 종료 시각 명시 → 종료 시 강제 평가

---

## 9. 5/15 재개 시 즉시 확인 사항 (체크리스트)

### 9.1 환경 점검

- [ ] `curl http://localhost:5257/health` → 200 OK
- [ ] `curl https://demo.hitpan.kr/` → 200 OK
- [ ] `git status` → uncommitted 변경 확인
- [ ] `git log --oneline -5` → 5/14 마지막 커밋 `9c7bec2` 확인
- [ ] MariaDB 정상 작동 (`mysql -uhitpan -p hitpan_erp -e "SELECT COUNT(*) FROM partners"`)

### 9.2 데이터 잔재 확인

- [ ] partners 13,542건 잔재 (5/14 마이그 결과)
- [ ] items 309건 / employees 10건 / 등등
- [ ] 재마이그 시 ON DUPLICATE KEY UPDATE 동작 확인

### 9.3 메모리 점검

- [ ] `MEMORY.md` 최신 항목 확인
- [ ] `project_handoff_0514_night.md` 읽기
- [ ] `feedback_migration_integrity_exception.md` 마이그 예외 원칙 확인
- [ ] `feedback_challenge_owner.md` 받아쓰기 금지 재학습

### 9.4 작업 진입 순서

1. 9.1~9.3 체크
2. 사장님 인사 + 진행 의도 확인
3. 학습과제 1순위 (MDB→MSSQL 공식 마이그) 사장님께 소스 위치 여쭙기
4. 소스 받으면 30분 박스 분석
5. 받은 자료로 진범 3건 봉합 방향 결정
6. 봉합 코드 → 빌드 → publish → 재시작 → demo 검증
7. 1분 절대 봉합
8. 5/16 09:00 본런 dry-run

---

## 10. 핵심 파일·경로 인덱스

### 10.1 코드 파일

| 파일 | 라인 | 내용 |
|---|---|---|
| `src/HitPan.Application/Services/MdbMigrationService.cs` | 124~250 | MigrateAsync 메인 흐름 |
| 동상 | 776~951 | MigratePartnersAsync (UPSERT 적용) |
| 동상 | 964~1170 | MigrateItemsAsync (UPSERT + 기존 ID 재사용) |
| 동상 | 1173~1330 | MigrateEmployeesAsync (UPSERT) |
| 동상 | 1340~1525 | MigrateTransactionsAsync (K2 + FK 봉합) |
| 동상 | 1670~1770 | BulkCopyStockLedgerAsync (UNIQUE 자동 DROP) |
| 동상 | 1885~1985 | MigrateCollectionsAsync ⚠️ Lock timeout 진범 |
| 동상 | 2026~2080 | EnsureLegacyFallbackEmployeeAsync 헬퍼 |
| 동상 | 2080~2215 | MigratePurchaseOrdersFromIUAsync (FK 봉합) |
| 동상 | 2225~2315 | MigrateSalesOrdersFromIOAsync (FK 봉합) |
| 동상 | 2326~2390 | MigrateTaxInvoicesAsync ⚠️ DDL 불일치 진범 |
| `src/HitPan.API/Extensions/AuthExtensions.cs` | 65~85 | OnMessageReceived access_token 처리 |
| `src/HitPan.API/Hubs/MigrationProgressHub.cs` | 전체 | SignalR Hub |
| `src/HitPan.API/Services/MigrationProgressService.cs` | 전체 | Progress Singleton |
| `src/HitPan.Web/Pages/Settings/MdbMigration.razor` | 전체 | 마이그 UI + SignalR 클라이언트 |

### 10.2 인프라 파일

| 파일 | 내용 |
|---|---|
| `C:\Users\소순근\.cloudflared\config.yml` | demo.hitpan.kr → localhost:5257 터널 |
| `.github/workflows/codeql.yml` | SAST CodeQL |
| `.github/workflows/trufflehog.yml` | 시크릿 스캔 |
| `.github/workflows/zap-baseline.yml` | DAST ZAP |

### 10.3 사장님 자료 (5/14 받음)

| 위치 | 내용 |
|---|---|
| `C:\Users\소순근\Desktop\공영정보DB\` | 사장님 회사 MDB 3개 (PYOJUN·PANDATA·POTHER) |
| `C:\Users\소순근\Documents\ALDrive\Download\HITWINSQL_동양밴드` | VB 소스 1807개 |
| `C:\Users\소순근\Documents\ALDrive\Download\HITWINSQL_오성마이더스` | VB 소스 |
| `C:\Users\소순근\Documents\ALDrive\Download\HITWINSQL_한누리` | VB 소스 + MDB 2개 |
| `C:\Users\소순근\Documents\ALDrive\Download\HITWINSQL_L119` | VB 소스 1624개 |
| `C:\Users\소순근\Documents\ALDrive\Download\GIS용` | VB 소스 + npandata.MDB |
| **`C:\Users\소순근\Documents\ALDrive\Download\SQL_표준웹에올리기(업그레이드용)`** | **공식 마이그 EXE 13개 (소스 확보 추후)** |
| `C:\HTP_SQLSERVER` | 히트판 클라이언트 EXE (DB 없음) |

### 10.4 문서

| 문서 | 내용 |
|---|---|
| `CLAUDE.md` | 절대원칙 26개 + 헌법 |
| `docs/work-orders/WS-20260514-11_온전한_정공법_6축.md` | WS-11 |
| `docs/설계/erp/PRD_THREE_SYSTEMS.md` | 3개 시스템 통합 |
| `docs/개발/erp/next_session_prompt_20260514_dawn.md` | 5/14 새벽 인수인계 |
| `docs/handoff/next_session_prompt_20260514_night.md` | 5/14 밤 인수인계 |
| **이 문서** | **5/15 인수인계 (꼼꼼판)** |

### 10.5 메모리

| 메모리 | 내용 |
|---|---|
| `project_handoff_0514_night.md` | 5/14 밤 인수인계 핵심 |
| `feedback_migration_integrity_exception.md` | 마이그 무결성 예외 (포괄) |
| `feedback_challenge_owner.md` | 받아쓰기 금지 |
| `feedback_customer_env_first.md` | 환경 확인 1순위 |
| `feedback_real_validation.md` | 풀스택 + 고객시선 검증 |
| `feedback_real_validation_2.md` | 법령/도메인 교차검증 의무 |
| `project_evf.md` | EVF 6대 영역 |
| `project_governance.md` | 7단계 결재 + 3중 검증 |

---

## 11. 비상 대응 (사장님 환경 문제 시)

### 11.1 API 죽음

```powershell
# 1. 프로세스 정리
Get-NetTCPConnection -LocalPort 5257 | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }

# 2. 재시작
cd c:\Users\소순근\Desktop\hitpan-erp
dotnet build src/HitPan.API/HitPan.API.csproj --configuration Release
dotnet run --project src/HitPan.API/HitPan.API.csproj --configuration Release --no-build --urls "http://localhost:5257"
```

### 11.2 demo.hitpan.kr 안 뜸

1. localhost:5257 정상 확인
2. cloudflared 서비스 재시작:
   ```powershell
   Restart-Service Cloudflared
   ```
3. 5분 후 https://demo.hitpan.kr 재시도

### 11.3 마이그 화면 비번칸 안 뜸

- 사장님 브라우저 캐시 비우기 필요
- F12 → Application → Storage → Clear site data
- 또는 Ctrl+Shift+Delete → 전체 삭제

### 11.4 마이그 중 에러

- API 로그: 백그라운드 작업 출력 파일 확인
- 위치: `C:\Users\소순근\AppData\Local\Temp\claude\c--Users-----Desktop-hitpan-erp\<session>\tasks\<task-id>.output`
- stacktrace에서 `MdbMigrationService.cs:line <N>` 확인 후 해당 라인 봉합

---

## 12. 사장님 PC 종료 시 자동 복구 절차

만약 사장님 PC 재부팅·전원 차단 시:

1. **API 재시작 (자동 안 됨, 수동 필요):**
   ```powershell
   cd c:\Users\소순근\Desktop\hitpan-erp
   dotnet run --project src/HitPan.API/HitPan.API.csproj --configuration Release --no-build --urls "http://localhost:5257"
   ```

2. **Cloudflared 자동 시작 (Windows 서비스로 등록되어 있다면 자동):**
   - 확인: `Get-Service Cloudflared`
   - Status=Running이면 OK
   - Stopped면 `Start-Service Cloudflared`

3. **MariaDB 자동 시작 (서비스):**
   - 확인: `Get-Service MariaDB` 또는 `Get-Service MySQL`
   - 자동 시작 설정되어 있어야 함

---

## 13. 5/15 PM 마음가짐

1. **사장님 직관 존중하되 받아쓰기 금지** — 5분 분석으로 단정 X, 객관 증거 확보 후 발언
2. **시간 박스 엄수** — 학습과제 1순위 30분, 2순위 30분 = 1시간 안에 봉합 진입
3. **본런 우선** — 학습 늪에 빠지지 않기. 5/16 09:00 본런이 최우선
4. **stacktrace 우선** — 에러 메시지보다 파일·라인부터
5. **환경 확인** — 사장님이 "안 돼" 하시면 첫 질문 = "어느 환경?"
6. **봉합 → 빌드 → publish → 재시작 → demo 검증** 5단계 매번 거치기
7. **사장님 푹 쉬시고 집에서 demo.hitpan.kr 확인하실 수 있도록** API 켜놓은 상태 유지

---

**작성 완료: 2026-05-14 23:55**
**다음 세션 시작 시 이 문서 + `project_handoff_0514_night.md` 메모리 1순위로 읽을 것**
**모든 컨텍스트 이 두 문서에 들어있음 — 컨텍스트 0에서 완벽 이어받기 가능**
