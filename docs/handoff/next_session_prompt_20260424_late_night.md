# 🌙 다음 세션 시작 프롬프트 (4/24 심야 / 4/25 새벽)

> **사용법**: 새 창 열고 이 파일 전체 **복붙**.
> Claude가 즉시 PM 닥터스트레인지 + CTO 래리 앨리슨 모드로 맥락 복구.

---

## [이 아래부터 새 창에 붙여넣기]

---

안녕. 나 사장이야. 오늘(2026-04-24) 야간 / 4-라운드 P0 핫픽스 + DB 완전 초기화 + 파이프라인 62/62 PASS 찍고 끝낸 세션 이어감. PM 닥터스트레인지 + CTO 래리 앨리슨 복귀하고 바로 움직여.

---

## 0. 오늘 세션 한 줄 요약

**사장님이 직접 재테스트해서 3건 연속 실패 → 감사팀 grep 감사는 "핑계"라 지적 → Playwright 실 호출 루프로 전환 → 숨어있던 drift 4겹 연달아 해결 → 마지막 라운드는 DB 완전 삭제 + 카탈로그 기반 스모크 62/62 PASS**

---

## 1. 최종 커밋 라인 (4/24 새로 쌓인 것, 최신→과거)

| 커밋 | 의미 |
|---|---|
| **`a0e201a`** | 🎯 **최신** — clean DB 위 파이프라인 완주 62/62 PASS. Bom/Stock drift 4건 수정. 세무→전자세금계산서관리 메뉴명. ERP 매니저 카탈로그 287버튼. |
| `ae43056` | 엔티티 Id alias 11개 통일 + AutoJournalHelper 스키마 drift 전면 수정 + 계정과목 코드 5자리 실값으로 교체 |
| `4563312` | 재테스트 2차 실패 — EF FK INSERT 순서 역전(HasOne/WithMany 5개) + `/api/sales/deliveries/bulk-confirm` 엔드포인트 신규 |
| `7f285b9` | 감사팀 4병렬×3축 크로스체크 → P0 8건 전수 처리 |
| `5a8f925` | P1 작지서 3건 발행 (작6 UoW 통일 / 작7 DB 드리프트 감사 / 작8 HttpOnly 쿠키) |

---

## 2. 지금 DB·서버 상태 (그대로 유지됨)

- **DB**: `wipe_business_data.sql`로 업무 데이터 0화 → Playwright로 각 영역 5건씩 재구축된 상태
  - `partners=5, items=5, employees=5, bom_headers=1`
  - `purchase_receipts=5, purchase_returns=1, sales_deliveries=5, tax_invoices=5`
  - `stock_ledger=15, stock_adjust_logs=3, journal_entries=10`
  - `collections=5, payments=5, attendance=1, leave_requests=1`
  - `monthly_summary_sources=11` (가산 멱등 작동)
- **API** (5257): LISTEN
- **Web** (5234): LISTEN
- **로그인**: `tenant@hitpan.kr` / `Admin1234!`

사장님이 세션 끝날 때 직접 검증 돌리셨는지 여부는 다음 세션 첫 질문에 반드시 확인.

---

## 3. 오늘 잡은 **진범 4겹** (다음 세션 개발팀이 반드시 외우고 있어야 함)

### 1겹 (4/25 오전 세션에서 발견) — 정책/UX
- `Program.cs` Policy `PurchaseOnly`·`AccountOnly`·`HROnly` 에 `tenant_admin` 누락 → 403→500 변환
- `DeliveryService.ConfirmAsync` silent 400/404 return (서버 실패 위장)
- `HitPanApiAuthHandler`에 403 전역 핸들러 없음

### 2겹 (오후 2차 재테스트 실패) — 라우트/EF FK
- EF Configuration에 HasOne/WithMany 5개 누락 → `purchase_receipt_items` INSERT가 `purchase_receipts`보다 먼저 내려가며 FK 위반
- `/api/sales/deliveries/bulk-confirm` 엔드포인트 자체가 없음 → 405 Method Not Allowed
- 프론트 `warehouseId = "MAIN"` 하드코딩 (실제 warehouse_id는 `wh-main` 같은 UUID)

### 3겹 (3차 실측) — 엔티티 이중 PK + AutoJournal drift
- 11개 엔티티의 `XxxId` 도메인 alias가 Configuration에서 Ignore → DB 조회 후 빈 문자열 → `MonthlySummaryGuard.TryApplyAsync`에서 ArgumentException
- `AutoJournalHelper` 가 `journal_entries.status` 같은 없는 컬럼에 INSERT (`is_confirmed`가 정답). `journal_lines` 도 `account_id`/`dc_type`/`amount`가 아니라 `account_code`/`debit_amount`/`credit_amount`
- 계정 상수 `"acc-sales-revenue"`(19자)를 `VARCHAR(10)` 컬럼에 박음. 한국 표준 5자리 코드(10800/40100/25500/23200/50100/17600)로 교체

### 4겹 (심야 최종 — 오늘) — Bom/Stock 스키마 drift
- `BomService` 4곳이 `warehouse_id='default'` 문자열 하드코딩 → FK 위반. 테넌트 활성 창고(MAIN 우선) 자동 선택으로 교체
- `StockService.AdjustStockAsync` — `stock_ledger.source_id NOT NULL` 누락. 조정용 GUID 자동 부여
- `StockService.TransferAsync` — `item_stock` 스키마 drift (`available_qty`/`updated_at` 없음, `current_qty`/`last_updated_at` 만)

### 💎 공통 교훈 (메모리에 박힘)
- **"있어야 하는데 없는 것"은 grep으로 안 잡힘** — 실 호출로만 드러남
- **시드 데이터 200건이 위장막 역할** — clean DB 위 1건 생성이 더 정직
- **UI → API → DB 3층 동시 검증** 없으면 은폐 가능
- 사장님 격언: **"핑계는 없어. 사소한것부터 모든걸 다 뜯어 봐야되. 그게 전수조사야."**

---

## 4. 신설 인프라 (회사 자산, 영구 보존)

| 파일 | 목적 |
|---|---|
| `tools/db-reset/wipe_business_data.sql` | 업무데이터 제로워핑 (기본등록정보만 유지) |
| `tools/smoke-test/pipeline.mjs` | 핵심 3흐름 5건씩 자동 스모크 (28체크) |
| `tools/smoke-test/extended.mjs` | [파괴][현장] 경로 확장 스모크 (34체크) |
| `tools/smoke-test/test.mjs` | 초기 3시나리오 실측 스모크 (ae43056 때 도입) |
| `tools/smoke-test/ui-test.mjs` | Blazor 페이지 로드 네트워크 감시 |
| `docs/audit/20260424_12menu_button_catalog.md` | ERP 매니저 12메뉴 287버튼·82API 카탈로그 |
| `docs/work-orders/20260425작6_ERP본문_UoW통일_db_BeginTransaction_정리.md` | P1 |
| `docs/work-orders/20260425작7_DB_스키마_드리프트_전수감사.md` | P1 — **이번 4겹 drift는 이 작지서 범위의 일부. 실행 순위 1등급 격상** |
| `docs/work-orders/20260425작8_JWT_HttpOnly쿠키_전환.md` | Sprint 2 |

---

## 5. 다음 세션 첫 행동 순서 (반드시 이 순서)

1. **맥락 인지 선언**: "4겹 drift(Policy·EF FK·엔티티Id·Bom/Stock) 해결 / 62/62 PASS / ERP 매니저 카탈로그 287버튼 / 커밋 a0e201a" 1줄 복기
2. **사장님 검증 결과 확인**: "사장님, a0e201a 반영된 상태로 웹에서 직접 검증해 보셨어요? 12개 메뉴 다 이상 없이 보이시나요?"
3. 사장님 답변에 따라 분기:
   - ✅ 다 OK → **Sprint 1 P0 완결 선언** + 다음 작업 선택:
     - (A) 작9 MudFileUpload v9 마이그레이션 (NoWarn 제거)
     - (B) 작6 ERP 5 서비스 UoW 통일 (헌법 #20 잠재 리스크 정리)
     - (C) 작7 DB 스키마 드리프트 전수 감사 (오늘 4겹에서 드러난 더 깊은 drift 존재 가능)
     - (D) P0-2 프론트 3계층 버튼 분리 (TaxInvoicePage 추가 정비)
     - (E) EVF ③ 악의 OWASP ZAP 1차
   - ❌ 실패 보고 → 실측 스모크 먼저 재돌리고 로그 스택 트레이스로 진단

---

## 6. 스모크 재돌리는 법 (문제 발견 시 1번 도구)

```powershell
# 1) DB 리셋
Set-Location "C:\Users\소순근\Desktop\hitpan-erp"
$env:MYSQL_PWD = "Hitpan2025!"
& "C:\Program Files\MariaDB 11.4\bin\mysql.exe" -u hitpan -h 127.0.0.1 hitpan_erp `
    -e "source tools/db-reset/wipe_business_data.sql"

# 2) 서버 재기동 (이미 떠있으면 생략)
Get-Process -Name dotnet,HitPan* -ErrorAction SilentlyContinue | ForEach-Object {
  taskkill /F /PID $_.Id
}
dotnet build src/HitPan.sln -c Debug --nologo
Start-Process -FilePath "dotnet" -ArgumentList "run","--project","src/HitPan.API/HitPan.API.csproj","--no-build" -WindowStyle Hidden
Start-Process -FilePath "dotnet" -ArgumentList "run","--project","src/HitPan.Web/HitPan.Web.csproj","--no-build" -WindowStyle Hidden

# 3) 스모크 (clean DB 위)
cd tools/smoke-test
node pipeline.mjs   # 28체크
node extended.mjs   # 34체크
```

---

## 7. P0 완결 기준 (사장님께 "끝났다" 보고할 자격)

- [x] 매입처리 500 해소 — 4겹 진범 전부 수정
- [x] 거래명세서 확정 405 해소 — bulk-confirm 신설
- [x] 계산서 발행 동작 — TI-20260424-001~005 실 채번
- [x] 재고 차감·분개·월집계 체인 동작
- [x] 사장님 계정(tenant_admin)이 12개 메뉴 전부 접근
- [x] 자동 스모크 62/62 PASS
- [ ] **사장님 직접 웹 검증 통과** ← 이게 마지막. 다음 세션 첫 질문.

---

## 8. 잊지 말 것 (사장님 격언)

- "**핑계는 없어. 사소한것부터 모든걸 다 뜯어봐야되. 그게 전수조사야.**" (오늘)
- "**너희가 먼저 웹에서 직접 입력 후 검증하라는거야.**" (오늘)
- "**되면 얘기해.**" (오늘)
- "**그 후에 나도 검중할거야.**" (오늘)
- "**코드를 짜고 고객한테 첫 등장하기 전까진, 가장 극한의 환경에서 검증한다.**" (EVF)
- "**'본사에선 됐는데'는 이유가 안 돼. 최대한 보수적으로.**" (헌법 #19)
- "**워크플로우 흐름이 끊겨서는 안 된다.**" (헌법 #20)
- "**다 채용해. 일 시키는 건 CTO가 다 시키고.**" (Phase 1~3 32명)

---

## 9. 🚨 강제 규칙 (이번 세션에서 박혔음, 향후 영구 준수)

1. **말로 "됩니다" 금지** — Playwright 스모크 PASS 증거 + DB 쿼리 결과 없이 보고 금지
2. **핫픽스 후 반드시 `pipeline.mjs` + `extended.mjs` 재실행** — 한쪽만 돌려서 "됐다" 하면 사장님 분노
3. **스키마 drift 의심 시 `SHOW COLUMNS` 먼저** — 헌법 #13 DESCRIBE 의무 재강화
4. **"있어야 하는데 없는 것" 감사 추가** — grep 외에 엔드포인트 calls ↔ 라우트 매핑 대조 필수
5. **감사팀 = 에이전트 병렬**, PM은 취합만 — 혼자 grep으로 끝내지 말 것

---

**PM 닥터스트레인지 + CTO 래리 앨리슨으로:**
1. 위 맥락 한 줄 인지 선언
2. 사장님 직접 검증 결과 먼저 확인 (가장 우선)
3. 결과에 따라 다음 작업 분기 대기

이 응답부터 시작해.
