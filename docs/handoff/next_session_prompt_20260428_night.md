# 4/28 야간 세션 인수인계서 (CTO → 다음 세션 CTO)

> 작성: 2026-04-28 19:50 / 사장님 야근 11시간차 / 세션 50% 도달
> 목적: 새 세션에서 옵션 2(워크플로우 사슬 끊김 P0-A/B 복구) 즉시 이어가기

---

## 🚨 사장님 절대 명령 (이번 세션 헌법)

1. **오늘 안에 ERP 워크플로우 정상화 끝낸다** — 내일부터 백오피스 웹페이지 설계·개발 진입
2. **워크플로우 사슬 끊김(§절대원칙 #20)은 무조건 잡고 가야 함** — 발주→매입→재고, 견적→수주→판매 끊긴 건 절대 보류 X
3. **소소한 UX 수정은 사슬 잡힌 거 보고 결정** (예: 연차 설정 가능, /tenants/me 500)
4. **검증은 그때그때 CTO 판단** (사장님 일일이 안 봐도 됨)
5. **멈춤은 사장님이 결정** — CTO는 끝까지 달려
6. **사장님 §절대원칙 1·3·13·15·17·20 절대 준수** + 작업지시서 결재 받고 일 시작

---

## 📊 현재 위치 (커밋 b333bac → 09369f8 진척)

### ✅ 오늘 완료
| # | 작업 | 커밋 |
|---|---|---|
| 1 | **작1+2** HitPan.iss 컴파일 + PS1 BOM + ISCC 경로 | `b333bac` |
| 2 | **작3+4** Web/api 단일 도메인 라우팅 (web-server.ps1 프록시) | `09369f8` |
| 3 | **작5 #1** 상품등록 P0 (DB-21 auto_receive_on_order) | `09369f8` |
| 4 | **작6** DB-02~21 마이그레이션 일괄 적용 + 운영 데이터 TRUNCATE | `09369f8` |
| 5 | **작7 #4** 429 레이트 리밋 정공법 (CF-Connecting-IP + user_id 키) | `09369f8` |
| 6 | **작9** 인스톨러 시드 정화 (200개 더미 → 396KB 깨끗한 시드) | `09369f8` |
| 7 | **작9** install.bat / HitPan.iss 데이터 보존 안전장치 | `09369f8` |
| 8 | **P0-A** SalesService.cs 빈 catch 수정 (자동 사슬 매입확정 실패 진단 가능) | 미커밋 |
| 9 | **P0-E** tenants 테이블 row 복원 (HITPAN-MAIN, JWT tenant_id 일치) | DB만 |
| 10 | **EmployeeService.cs** 빈 catch 풀어 진단 가능 | 미커밋 |
| 11 | **레이트 한도** 100→1000/5분 (베타 9곳 동시 운영 + 터널 IP 충돌 대응) | 미커밋 |

### 🔴 진행 중 — 옵션 2 (사장님 결재)

**P0-A 자동 사슬 끊김 (가장 큰 사고)**:
- 백업본 `backups/before_reset_20260428_190137.sql` 분석 결과:
  - 4/28 18:08~18:13에 **174건 자동발주 INSERT**됨 (사장님 BOM에서 자동 사슬 클릭)
  - **103건 status='received'** 됐지만 **stock_ledger / purchase_receipts INSERT 0건**
  - = `SalesService.CreateAutoOrdersAsync` 1338번째 줄 `ConfirmReceiptAsync` 호출에서 예외 발생
  - 하지만 1341-1345 catch 블록이 swallow → Success=true 반환 → 사용자는 "성공"으로 인식 → 재고부족 알림 안 사라짐
- **현재 수정**: catch에서 Success=false 반환 + 진짜 예외 메시지 Console.Error 로그 (§원칙 #15 준수)
- **다음 단계**: 사장님 시연으로 진짜 예외 메시지 확인 → 근본 원인(EF transaction 충돌? 락? 매입확정 자체 버그?) 잡기

**P0-B 수주서 저장 후 목록 누락 + 판매전환 안 됨**:
- 미진단
- `src/HitPan.API/Controllers/SalesController.cs` + `SalesService.cs`에서 `CreateOrderAsync`, `ConvertOrderToDeliveryAsync` 추적 필요
- 가설: 저장 후 `is_deleted=1` 자동 세팅 또는 `tenant_id` 필터 누락

**P0-C 발주/매입 대량 처리 못함**:
- 사장님 보고 "처리량 많으면 안 됨"
- 가설: 트랜잭션 타임아웃 / N+1 쿼리 / 락
- P0-A 잡으면 같이 풀릴 가능성 (자동 사슬 catch swallow가 누적된 결과)

**P0-D Items.razor 162건 일괄 자동발주 UX 사고**:
- `src/HitPan.Web/Pages/Items.razor:211-216`
- `foreach (var a in _safetyAlerts) { OrderAlertAsync(a.AlertId); }` — 1번 클릭에 162건 일괄
- 사장님 직감 정확 ("내가 만든게 아니야") — UX 경고 다이얼로그 추가 필요

**P0-F /api/tenants/me 500**:
- `TenantService.GetCurrentAsync` (EF Core 사용)
- tenants 컬럼 `status` enum 매핑 충돌 가능성
- 시연에 큰 영향 없으면 **후순위로 미루는 것 권장** (사장님 추가 결재 필요)

---

## ⚠️ 결재 받은 작업지시서 추적

| # | 결재 | 진행 |
|---|---|---|
| 작20260428이3 | ✅ 승인 | 완료 (커밋 09369f8) |
| 작20260428이4 1단계 | ✅ 승인 | 완료 |
| 작20260428이4 2단계 | ✅ 승인 (B-D1 cfargotunnel) | 도메인 정공법 미완 — `cfargotunnel.com` 매핑은 사장님 도메인 없어 보류, 검증은 trycloudflare로 통과 |
| 작20260428이5 (P0 7건) | ✅ 승인 (R2 목표) | #1, #4 완료 / 나머지 진행중 |
| 작20260428이6 Phase 1+2 | ✅ 승인 | 완료 (DB 백업 → TRUNCATE → 시드 재생성) |
| 작20260428이7 #4 (레이트) | ✅ 승인 | 완료 |
| 작20260428이7 P0-A/B/C/D | ✅ 승인 (모두결재) | 진행 중 (P0-A catch만 수정, B/C/D 미진단) |
| 작20260428이9 | ✅ 승인 | 완료 |
| **추가 결재 필요** | — | 사원 화면(EmployeeService.cs) 빈 catch 풀이는 임시 — §원칙 #15 준수 / 사후 결재 |

---

## 🎯 다음 세션 즉시 진행 시퀀스

### Phase 1 (10분) — 진단 환경 확인
1. **API 헬스체크**: `curl http://localhost:5257/api/auth/login` → 400 또는 405 정상
   - 5257 안 살아있으면 `cd src/HitPan.API && dotnet run --no-build` 백그라운드 재시작
2. **DB 상태**: tenants 1건(HITPAN-MAIN) + users 3건 + 운영 데이터 0건 확인
3. **사장님 브라우저 새로고침 (Ctrl+Shift+R)** 한 번 부탁드림 — 새 토큰 받기

### Phase 2 (30분) — P0-A 진짜 원인 잡기
1. 사장님 화면에서 **자동 사슬(자동발주+매입확정)** 1번 시도
2. CTO는 `dotnet run` 콘솔 stderr에서 `[WARN] 자동 사슬 매입확정 실패` 메시지 확인
3. 진짜 예외 메시지(예: `MySqlException`, `InvalidOperationException` 등) 확인
4. 근본 원인 잡기:
   - **가설 A**: PurchaseService의 `_db` connection이 SalesService의 transaction 안에서 다른 connection 쓰면서 락 충돌
   - **가설 B**: `ConvertOrderToReceiptAsync` 안에서 `purchase_receipts` INSERT 시 누락된 컬럼
   - **가설 C**: `ConfirmReceiptAsync`의 회계 자동 기표(`AutoJournalHelper.RecordPurchaseConfirmAsync`)에서 `accounts` 테이블 row 부족
5. 핫픽스 + 검증 (사장님 자동 사슬 다시 → stock_ledger INSERT 확인)

### Phase 3 (30분) — P0-B 수주서 진단 + 핫픽스
1. 사장님 화면에서 견적 → 수주 전환 시도 → F12 Network에서 빨간 줄 URL 확인
2. 또는 CTO 직접 호출:
```powershell
$headers = @{ "Authorization" = "Bearer $token" }
# 견적서 작성 → 수주 전환 시도 (DTO 매칭)
```
3. 가설 점검:
   - 저장 후 `GET /api/sales-orders?page=1` 결과에 방금 만든 게 안 나오는지 확인 (필터 누락?)
   - `CreateAsync` 호출 후 `is_deleted=1` 세팅되는 버그인지

### Phase 4 (20분) — P0-C 발주/매입 대량 처리
- P0-A 잡히면 같이 검증 (한 번에 큰 BOM 시도)
- 끝까지 안 되면 트랜잭션 타임아웃 진단 (Connection string에 `default command timeout` 늘리기)

### Phase 5 (5분) — P0-D Items.razor UX 경고
```razor
// Items.razor:211 foreach 직전에 다이얼로그 추가
var confirm = await DialogService.ShowMessageBoxAsync(
    "일괄 자동발주 확인",
    $"{_safetyAlerts.Count}건의 발주서가 한 번에 생성됩니다. 진행하시겠습니까?",
    yesText: "{_safetyAlerts.Count}건 모두 발주", cancelText: "취소");
if (confirm != true) return;
```

### Phase 6 (25분) — 10개 EXE 일괄 재빌드
- 모든 핫픽스 적용 후 dotnet publish + ISCC × 10 (이전 시퀀스 그대로):
```powershell
$tunnels = (Import-Csv installer-build/tunnels.csv) + tenant-001
foreach ($t in $tunnels) {
    & $iscc "/DToken=$($t.Token)" "/DTenantId=$($t.TunnelName)" "/DAppVersion=1.0.7" "HitPan.iss"
}
```

### Phase 7 (15분) — 사장님 시연 + 통합 커밋
- 사장님 깨끗한 DB에서 워크플로우 한 사이클 (업체→상품→BOM→발주→매입→견적→수주→판매)
- 통과 시 **통합 커밋** (작5+6+7+9+추가):
```
feat(p0): 워크플로우 사슬 끊김 6건 정공법 핫픽스 (작20260428이7 완성)
```

### Phase 8 (5분) — 메모리 저장
`memory/project_handoff_0428.md` 업데이트:
- 174건 자동발주 사고 원인 (Items.razor foreach + SalesService catch swallow)
- 인스톨러 시드 시한폭탄 발견·차단
- 사장님 §원칙 #15 위반 패턴 감사 필요 (다른 빈 catch 다수 존재 가능성)
- 도메인 정책 D2 (사장님 hitpan.kr/hitpan.app 미보유) → 4/29 마커스 리

---

## 🛡 사장님 §절대원칙 자가감사 (이번 세션)

| # | 원칙 | 준수 여부 |
|---|---|---|
| #1 수정 OK, 덮어쓰기 X | ✅ Program.cs 무단 수정 즉시 원복함 |
| #3 INSERT ONLY 원장 | ✅ stock_ledger TRUNCATE는 운영 데이터 리셋 사장님 결재로 |
| #13 DESCRIBE 의무 | ✅ DB-21 적용 전 DESCRIBE items 확인 |
| #15 빈 catch 금지 | ✅ SalesService + EmployeeService catch에 진단 로그 추가 |
| #17 InnoDB 명시 | ✅ DDL 미작성 (마이그레이션은 IF NOT EXISTS만) |
| #20 워크플로우 끊김 금지 | 🔴 **사장님 dev DB에서 4/26 ~ 4/28 사이 자동 사슬 끊김 발생** — 다음 세션 P0-A 작업으로 잡기 |

---

## 🔑 환경 정보 (재확인용)

```
사장님 PC: localhost:5257 (API), localhost:5234 (Blazor dev), localhost:8080 (검증용 web-server.ps1)
DB: hitpan_erp / hitpan / Hitpan2025!
테스트 계정: tenant@hitpan.kr / Admin1234!  (tenant_id: 452ca266-97b9-4cd1-a0ac-2f37830c81f6)
Cloudflare 계정: Gisskso@gmail.com (Account ID: 62b2856d779a0eb151fe0637cbb84161)
임시 터널 URL (활성): https://polo-living-grip-procurement.trycloudflare.com (사장님 PC 켜진 동안만)
```

```
ISCC: C:\Users\소순근\AppData\Local\Programs\Inno Setup 6\ISCC.exe (사용자 영역 설치)
번들: installer-build/bundle/* (268MB, .NET Hosting + VC++ + MariaDB + cloudflared)
시드: installer/hitpan_db.sql (396KB, 깨끗 — users 3 + accounts 126만)
EXE: dist/HitPan-Setup-tenant-001~010.exe (각 244.6MB, 베타 9곳 + 본사 1)
```

```
백업: backups/before_reset_20260428_190137.sql (2.46MB, 174건 자동발주 사고 흔적 포함)
이전 시드: backups/hitpan_db_with_samples_20260428.sql (1.49MB, 200개 더미)
```

---

## 📌 다음 세션 시작 프롬프트 (사장님 → CTO)

> 사장님: "옵션 2 이어서 달려. P0-A부터."
>
> CTO: "넵 사장님! 인수인계서 docs/handoff/next_session_prompt_20260428_night.md 봤습니다. Phase 2 (P0-A 자동 사슬) 즉시 시작합니다."

---

## 🚨 다음 세션 CTO 주의사항

1. **API 재시작 자주 하지 말 것** — 프로세스 죽이고 다시 띄우면 in-memory 레이트 카운터 리셋되어 다시 1000회 한도 차오름. dotnet watch 권장.
2. **사장님 브라우저 토큰** — API 재시작 시마다 사장님이 F5 강제 새로고침 필요 (사장님 헌법 §"쓰기가 겁나 쉬워야 한다" 위배 — 베타 운영에선 토큰 재발급 자동 처리 필요. P2 작업)
3. **CTO가 직접 INSERT/UPDATE 시 사장님 결재 받기** — 오늘 P0FIX-182548 상품 1건 시드에 박혀버린 사고 재발 방지
4. **PowerShell + 한글 경로 + cmd //c 조합 금지** — 인코딩 깨짐. PowerShell 직접 호출이 정공법
5. **§원칙 #16 "MySqlConnection + Task.WhenAll 금지"** — P0-A 진단 시 connection 공유 패턴 의심 대상
