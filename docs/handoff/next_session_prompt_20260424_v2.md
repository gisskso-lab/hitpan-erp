# 🌙 다음 세션 시작 프롬프트 (4/24 심야 v2 — 매입 확정 & BOM 흐름 재설계 이후)

> **사용법**: 새 창 열고 이 파일 전체 **복붙**.
> Claude가 즉시 PM 닥터스트레인지 + CTO 래리 앨리슨 모드로 맥락 복구.

---

## [이 아래부터 새 창에 붙여넣기]

---

안녕. 나 사장이야. 4/24 심야 세션에서 P0 6버그 + 매입 일괄확정 + BOM 흐름 재설계까지 처리하고 이어감. 여전히 매입 확정 시 journal_lines CHECK 제약 위반 버그가 남아있어서 다음 세션 최우선 과제야. PM 닥터스트레인지 + CTO 래리 앨리슨 복귀하고 바로 움직여.

---

## 0. 오늘 세션 한 줄 요약

**DB 완전 삭제 → 전수조사(287버튼 카탈로그) → 파이프라인 5건씩 자동 생성 → 사장님 직접 수동 테스트로 8버그 발견 → 루프 돌려 6+2개 수정 → 매입 확정에서 journal_lines CHECK 제약 위반 발견 (다음 세션 최우선)**

---

## 1. 최종 커밋 라인 (이번 세션 전체, 최신→과거)

| 커밋 | 의미 |
|---|---|
| **`355ed51`** | 🎯 **최신** — 매입 일괄확정 API 연결 + BOM 흐름 재설계 (완제품명 입력 + 확인 다이얼로그 + items 자동 INSERT) |
| `c28ee85` | 사장님 수동 테스트 6버그 처리 (BOM UI·자동발주·휴가 결재 트리거) |
| `bc65e36` | 이전 인수인계서 |
| `a0e201a` | clean DB 위 파이프라인 5건씩 62/62 PASS |
| `ae43056` | 엔티티 Id alias 11개 + AutoJournalHelper drift 전면 수정 |
| `4563312` | EF FK INSERT 순서 + bulk-confirm 라우트 신설 |
| `7f285b9` | 감사팀 4병렬×3축 P0 8건 처리 |
| `5a8f925` | P1 작지서 3건 (작6·7·8) |

---

## 2. 현재 DB·서버 상태

- **DB**: `wipe_business_data.sql` 실행된 상태 (업무데이터 0, 기본등록정보만 유지)
- 스모크 돌린 후라 파이프라인 5건씩 있음 (매번 사장님 수동 테스트 전 재실행 필요)
- **API** (5257): LISTEN
- **Web** (5234): LISTEN
- **로그인**: `tenant@hitpan.kr` / `Admin1234!`

---

## 3. 🚨 최우선 미해결 버그 (다음 세션 첫 과제)

### 매입 일괄 확정 시 journal_lines CHECK 제약 위반
**사장님 스크린샷 증거**: "성공 1건 / 실패 1건. 첫 실패: ..." Snackbar
**서버 로그**:
```
[23:27:34 ERR] Unhandled error: CONSTRAINT `chk_jl_debit_or_credit` failed for `hitpan_erp`.`journal_lines`
```

**원인 가설**: `AutoJournalHelper.RecordPurchaseConfirmAsync`(또는 Sales)가 분개 라인 INSERT 시
debit_amount와 credit_amount 중 정확히 하나만 > 0 이어야 하는 CHECK 제약을 위반.
vatAmount=0인 경우 `vat != 0` 분기로 빠져서 0원짜리 debit/credit row 안 넣는 것 같지만,
드물게 supply/vat 둘 다 0 이거나 하는 엣지 케이스로 실패.

**재현 경로**:
1. `/purchases` 진입
2. 매입 저장 (단가=1,000, 수량=1, 공급가액=1,000, 부가세=100 — 정상값)
3. 목록 → 선택 → "선택 일괄 확정"
4. Snackbar: "성공 1건 / 실패 1건" (2건 중 하나만 성공)

**해결 방향**:
- `src/HitPan.Application/Services/AutoJournalHelper.cs` InsertLineAsync 직전에
  `if (amount == 0) return;` 조기 반환 넣어 0원 row INSERT 자체 차단
- 또는 CHECK 제약 내용을 `SHOW CREATE TABLE journal_lines` 로 확인 후 맞추기
- 재현 후 로그 + DB 상태로 진짜 원인 정밀 타격 (grep 금지, 실 호출로)

---

## 4. 이번 세션 처리한 버그 8건

### 사장님 수동 테스트에서 발견 (6건)
1. **BOM 등록 400** (fk_bh_item FK 위반) — 완제품 선택 UI 누락 → `c28ee85`
2. **매입 재고 미반영** — 실제는 반영됨. "저장만 하고 확정 안 누른 draft" UX 이슈
3. **계산서 발행** — 정상 동작 (TI-20260424-001~005 채번 확인)
4. **자동발주 미반영** — OrderAlertAsync가 라벨만 바꿨음 → 실제 purchase_orders INSERT로 재작성 → `c28ee85`
5. **매입 후 지급 미표시** — 미구현 기능(설계 누락). 별도 작지서 필요
6. **휴가 → 결재 미반영** — ApprovalTriggerHelper 누락 → 추가 → `c28ee85`

### 사장님 수동 테스트 2차 (2건)
7. **매입 일괄 확정 "추후 API 연동 필요"** — 실 API 순차 호출로 연결 → `355ed51`
8. **BOM 생성 흐름 잘못** — "왜 기존상품명 불러와" 지적 → MudTextField 완제품명 입력 + 확인 다이얼로그 + items 자동 INSERT로 재설계 → `355ed51`

---

## 5. 진범 5겹 (이번 세션 전체 누적)

### 1겹 (초반) — Policy/UX
- Program.cs Policy tenant_admin 누락 (Purchase·Account·HR)
- DeliveryService.ConfirmAsync silent 400/404 return
- 403 전역 핸들러 없음

### 2겹 — EF FK / 라우트
- EF Configuration HasOne/WithMany 5개 누락 → 자식이 부모보다 먼저 INSERT
- /api/sales/deliveries/bulk-confirm 엔드포인트 없음 → 405
- 프론트 warehouseId "MAIN" 하드코딩

### 3겹 — 엔티티 이중 PK alias + AutoJournal drift
- 11개 엔티티 XxxId alias가 Ignore되어 DB 조회 시 빈 값
- journal_entries/lines 컬럼명 drift (status/account_id → is_confirmed/account_code)
- 계정 상수 "acc-sales-revenue"(19자)가 VARCHAR(10) 초과 → 한국 표준 5자리 코드로 교체

### 4겹 — Bom/Stock 스키마 drift
- BomService `'default'` warehouse_id 하드코딩 4곳
- StockService source_id NOT NULL 누락
- item_stock available_qty/updated_at 없음

### 5겹 (이번 세션 신규)
- BOM 프론트 완제품 선택 UI 누락 (→ 빈 ProductItemId로 FK 위반)
- BOM 흐름이 "기존 상품 선택" 전제 → 사장님 지시 흐름("새 상품명 입력 → 자동 등록")으로 재설계
- 매입 일괄 확정 Snackbar 스텁
- 자동발주 라벨만 바꾸고 실 PO 안 만듦
- LeaveRequest에 ApprovalTrigger 누락
- **(미해결)** 매입 확정 시 journal_lines CHECK 제약 위반

### 💎 교훈
- "있어야 하는데 없는 것"은 grep으로 안 잡힘 — 실 호출만 진실
- 시드 데이터가 위장막 — clean DB 위 1건 생성이 가장 정직
- **UI → API → DB 3층 동시 검증** 필수
- 사장님 격언: **"핑계는 없어. 사소한것부터 모든걸 다 뜯어 봐야되."**
- **"말로 '됩니다' 금지"** — Playwright 스모크 + DB 쿼리 증거 없이 보고 금지

---

## 6. 신설 인프라 (회사 자산)

| 파일 | 목적 |
|---|---|
| `tools/db-reset/wipe_business_data.sql` | 업무데이터 제로워핑 |
| `tools/smoke-test/pipeline.mjs` | 핵심 3흐름 자동 스모크 28체크 |
| `tools/smoke-test/extended.mjs` | [파괴][현장] 경로 확장 스모크 34체크 |
| `tools/smoke-test/click-test.mjs` | Playwright 브라우저 순회 + 스크린샷 + 네트워크 감시 |
| `tools/smoke-test/test.mjs` | 초기 3시나리오 스모크 |
| `tools/smoke-test/ui-test.mjs` | Blazor 로드 네트워크 감시 |
| `docs/audit/20260424_12menu_button_catalog.md` | ERP 매니저 287버튼·82API 카탈로그 |
| `docs/work-orders/20260425작6·7·8.md` | P1 3작지서 (UoW 통일 / DB 드리프트 / HttpOnly 쿠키) |

---

## 7. 다음 세션 첫 행동 순서 (반드시 이 순서)

1. **맥락 인지 선언**: "진범 5겹(Policy·EF FK·엔티티Id·Bom·5번째) 처리 / 62/62 PASS 유지 / **매입 확정 journal_lines CHECK 미해결** / 커밋 355ed51" 1줄 복기

2. **🚨 최우선 버그 진단 루프 착수**:
   ```
   1) 서버·DB 상태 먼저 확인 (API:5257 / Web:5234 LISTEN?)
   2) SHOW CREATE TABLE journal_lines — chk_jl_debit_or_credit 정확히 확인
   3) Playwright click-test 돌려 매입 일괄확정 재현
   4) 실 에러 스택 → AutoJournalHelper.InsertLineAsync 근처 수정
   5) 스모크 62/62 + 사장님이 찍은 버그 재현 시나리오 PASS 확인
   6) 커밋 + 다음 사장님 검증 요청
   ```

3. 매입 확정 버그 해결 뒤 다음 분기:
   - (A) 사장님이 찍는 새 버그 대응
   - (B) 버그 #5 매입 후 지급 자동 연계 (미구현 기능 작지서 발행)
   - (C) 작9 MudFileUpload v9
   - (D) 작6 ERP 5 서비스 UoW 통일
   - (E) 작7 DB 드리프트 전수 감사 (이번 5겹 근본 제거)

---

## 8. 스모크 & 재기동 one-liner

```powershell
# DB 리셋
Set-Location "C:\Users\소순근\Desktop\hitpan-erp"
$env:MYSQL_PWD = "Hitpan2025!"
& "C:\Program Files\MariaDB 11.4\bin\mysql.exe" -u hitpan -h 127.0.0.1 hitpan_erp `
    -e "source tools/db-reset/wipe_business_data.sql"

# 서버 재기동
Get-Process -Name dotnet,HitPan* -ErrorAction SilentlyContinue | ForEach-Object {
  taskkill /F /PID $_.Id
}
dotnet build src/HitPan.sln -c Debug --nologo
Start-Process -FilePath "dotnet" -ArgumentList "run","--project","src/HitPan.API/HitPan.API.csproj","--no-build" -WindowStyle Hidden
Start-Process -FilePath "dotnet" -ArgumentList "run","--project","src/HitPan.Web/HitPan.Web.csproj","--no-build" -WindowStyle Hidden

# 스모크 (약 2분)
cd tools/smoke-test
node pipeline.mjs   # 28체크
node extended.mjs   # 34체크
node click-test.mjs # Playwright 화면 + 스크린샷
```

---

## 9. P0 완결 체크리스트 (사장님께 보고 자격)

- [x] 매입처리 500 해소
- [x] 거래명세서 확정 405 해소 (bulk-confirm 신설)
- [x] 계산서 발행 (TI-20260424-001~005 실 채번)
- [x] 재고 차감·분개·월집계 체인 (스모크 OK)
- [x] 사장님 계정(tenant_admin) 12개 메뉴 접근
- [x] 자동 스모크 62/62 PASS (pipeline 28 + extended 34)
- [x] BOM 등록 완제품 선택 UI
- [x] BOM 흐름 사장님 지시대로 (텍스트 입력 + 확인 다이얼로그 + items 자동)
- [x] 자동발주 실 PO 생성
- [x] 휴가 → 결재함 연동
- [x] 매입 일괄확정 API 연결
- [ ] **매입 확정 journal_lines CHECK 위반** ← 다음 세션 최우선
- [ ] **사장님 직접 웹 전 기능 검증 통과** ← 마지막 관문

---

## 10. 사장님 격언 (이번 세션 박힌 것 포함)

- "**핑계는 없어. 사소한것부터 모든걸 다 뜯어 봐야되.**"
- "**너희가 먼저 웹에서 직접 입력 후 검증하라는거야.**"
- "**내가 하나하나 웹에서 버튼 직접 눌러보고 테스트 하라고 했지??**"
- "**되면 얘기해.**"
- "**그 후에 나도 검중할거야.**"
- "**이번단계만 모든 승인은 그냥 CTO가 해.**"
- "**최종검증때만 나 찾아. 그리고 일에 집중.**"
- "**이 로직으로 가야지**" (BOM 흐름 재설계 지시)
- EVF 격언: "코드를 짜고 고객한테 첫 등장하기 전까진, 가장 극한의 환경에서 검증한다."
- 헌법 #19: "본사에선 됐는데는 이유가 안 돼."
- 헌법 #20: "워크플로우 흐름이 끊겨서는 안 된다."

---

## 11. 강제 규칙 (영구 준수)

1. **말로 "됩니다" 금지** — Playwright 스모크 PASS + DB 쿼리 결과 증거 필수
2. **핫픽스 후 반드시 `pipeline.mjs` + `extended.mjs` + `click-test.mjs` 재실행**
3. **스키마 의심 시 `SHOW COLUMNS` 먼저** (헌법 #13)
4. **"있어야 하는데 없는 것" 감사 필수** — grep 외에 calls ↔ routes 대조
5. **감사팀 = 에이전트 병렬**, PM은 취합만. 혼자 grep으로 끝내지 말 것
6. **사장님이 수동 테스트로 찍은 버그는 스모크 테스트에도 반드시 반영** (회귀 방지)

---

**PM 닥터스트레인지 + CTO 래리 앨리슨으로:**
1. 위 맥락 한 줄 인지 선언
2. **최우선 버그 journal_lines CHECK 제약 진단 루프 즉시 착수**
3. 해결 증명 후 사장님 다음 검증 요청 대기

이 응답부터 시작해.
