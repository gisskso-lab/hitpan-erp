# 4/29 세션 인수인계서 — UX/UI 토스 B 컨셉 + 사원 연차 풀스택

> 작성: 2026-04-29 / 사장님 야근 12시간차 / 세션 거의 풀
> 목적: 다음 세션이 UX/UI 작업과 P0 백로그를 끊김 없이 이어가게 한다
> 미커밋 변경 다수 — 다음 세션 첫 작업 = 사장님 시연 통과 후 통합 커밋

---

## 🚨 사장님 절대 명령 (이번 세션 헌법)

1. **UX/UI 작업 외 영역에는 §원칙 #1 (덮어쓰기 X) 그대로 유지** — 사장님 명시 결재
2. **연차 관리는 기능 추가라 새 결재로 풀스택 진행** — 사장님 명시 결재
3. **SaaS 권한(본사·대리점)은 히트판 웹페이지에서 완전 배제** — 백오피스 분리, /admin/* /reseller/* 페이지 코드는 보존
4. **메뉴 11그룹 구조 그대로** — 사장님 직접 짜신 안

---

## 📊 이번 세션 처리 사항 (시간순)

### Phase 1 — P0 핫픽스 (이전 세션 이어받음)
- ✅ **P0-A 자동 사슬 채번 충돌** — `DocumentNumberHelper` 신설, EF.Count 패턴 → DB MAX 직조회로 4곳 일괄 교체 (PO/PR/SO/SD)
- ✅ **P0-B 수주 헤더 §20 위반** — `SalesService.ConfirmDeliveryAsync`에 sales_orders 헤더 동기화 SQL 추가 (PurchaseService PO 헤더와 동일 패턴)
- ✅ **P0-B UX** — 수주서 목록 기본 필터 'draft' → 'all', '전체' 옵션 추가
- ✅ **§15 빈 catch 봉합** — RateLimit 1000회/5분, EmployeeService 진단 로그
- ✅ **커밋 1951caf** 통합 푸시됨 (push는 안 함, 사장님 결재로 별도)
- ✅ **DB 백업** — `backups/before_status_alter_20260429_070710.sql` (5.5MB / 91 테이블)

### Phase 2 — 사장님 거래 정리 (4/28~29 사고 봉합)
- ✅ DB UPDATE: SO-20260428-001 status='confirmed' (사장님 거래 복구)
- ✅ 사장님이 화면에서 직접 SD-20260428-002 cancelled 처리
- ✅ status 컬럼 longtext 진단 끝남 — 진범은 `delivered_qty=10/10 closed` 잔량 0이라 두 번째 변환 시도가 막힌 것 (코드는 정상)
- 🟡 **ALTER TABLE longtext→enum 미진행** — 사장님 시연으로 EF 매핑 정상 동작 확인. 베타 후 정식 운영 시 작지서로 분리

### Phase 3 — 디자인 시스템 (사장님 결재: B 토스 + 1번 키보드 우선 믹스)
- ✅ **design-tokens.css 신설** — 18 카테고리 토큰 (컬러/타이포/간격/Breakpoint/포커스링 등)
- ✅ **Pretendard 폰트** 도입 (Noto Sans KR 폴백)
- ✅ **hp-toss.css 신설** — 글로벌 토스 감성 (사이드바·카드·인풋·테이블·다이얼로그·포커스링)
- ✅ **App.razor MudTheme** Pretendard 동기화
- ✅ **미리보기 2개 작성** — `preview-design.html`(Stripe+Linear+Toss 믹스) / `preview-design-toss.html`(토스 감성 강화) — 사장님이 토스 강화안 선택
- ✅ **키보드 네비 JS** — `hitpan-keyboard-nav.js` (Enter→다음 칸, Shift+Enter→이전, ↑↓ 그리드 행 이동, 마지막 칸 Enter → submit 자동 클릭)

### Phase 4 — 사이드바 11그룹 재구성 (사장님 직접 결재)
| # | 그룹 | 핵심 |
|---|---|---|
| 1 | 홈 | 단일 NavLink (4/29 변경: 메뉴 제거 → 헤더 로고 클릭으로 대체) |
| 2 | 계정관리 | 회사 정보 / 직원 계정 관리 / 권한 / 결재설정 / 기기 / 사용환경 |
| 3 | 그룹웨어 | 결재함 3개 + 사원 + 근태/휴가/경비신청/근로계약/서명이력 |
| 4 | 업체관리 | 마스터/특별단가/원장 |
| 5 | 상품관리 | 마스터/BOM/특별단가/원장 |
| 6 | 판매관리 | 견적·수주·거래명세서·통계 9개 |
| 7 | 매입관리 | 발주·매입·반품·통계 8개 |
| 8 | 계산서관리 | 전자세금계산서 발행 + 통계 |
| 9 | 재고관리 | 7개 |
| 10 | 회계관리 | 수금·지급·장부·부가세·경비처리·손익·월마감·세무 9개 |
| 11 | 자료관리 | 백업 / 자료이관(**구 히트판 MDB**) / 양식설정 |

- ✅ **본사·대리점 메뉴 사이드바 제거** — `/admin/* /reseller/*` 페이지 코드는 백오피스 분리용 자산으로 보존
- ✅ 메뉴명 다듬기: "사용자정보 설정" → "회사 정보" / "사용자 추가·관리" → "직원 계정 관리"

### Phase 5 — 사원 연차 관리 풀스택 (기능 추가, 사장님 새 결재)
- ✅ **DB**: `ALTER TABLE employees ADD COLUMN annual_leave_total/used DECIMAL(5,1) DEFAULT 0` (12명 데이터 보존)
- ✅ **Domain**: `Employee.AnnualLeaveTotal/Used`
- ✅ **EF Configuration**: decimal(5,1) 매핑
- ✅ **DTO**: EmployeeListDto/DetailDto에 연차 + UpdateAnnualLeaveRequest
- ✅ **Service**: `UpdateAnnualLeaveAsync` 단독 메서드
- ✅ **API**: `PUT /api/employees/{id}/annual-leave` (TenantAdminOnly)
- ✅ **Web 모델/서비스/페이지**: 사원관리 그리드에 4컬럼 추가 (부여/사용/잔여/저장 버튼)
- ✅ 잔여 색상 코드 (음수=빨강, 0=회색, 양수=초록)
- ✅ 0.5일 단위, 가불 허용 (사용 > 부여 시 경고만)

### Phase 6 — 대시보드 토스 감성 + 월간 연차 달력 (사장님 결재 + 이미지)
- ✅ **Hero**: "안녕하세요, 사장님 · 4월 29일 수요일" + 38px 큰 매출 + "+8.4% 늘었어요 👏"
- ✅ **Hero 우측 경고 알림 카드 (옵션 B)** — 권한 미설정 자동 노출, 추후 구독·기기도 `_alerts` 리스트에 추가만 하면 자동
- ✅ **Quick Action 4카드** — 새수주(그린) / 발주(블루) / 세금계산서(핑크) / 미수금(오렌지)
- ✅ **KPI Hero** — 그린 그라디언트 큰 카드 + 보조 2장 (재고 자산 / 미수금)
- ✅ **AI 챗봇** — 본문 카드 제거, 상단 헤더 "히트판에게 물어보세요"만 유지 (중복 제거)
- ✅ **월간 연차 달력 v2** — 사원 매트릭스 → **구글캘린더 스타일** (7×5~6주, 일자 셀에 직원 칩, 휴가 종류별 색상)
- ✅ **헤더 로고 이동** — TopHeader 좌측에 그라디언트 마크 + 텍스트 (사이드바에서 제거)
- ✅ **기존 차트·KPI 5장·외상연체·최근거래·재고알림 컴포넌트 제거** (Dashboard에서만 — 컴포넌트 코드는 보존, 다른 페이지 활용 가능)

### 빌드 상태
- ✅ **errors 0 + warnings 0** (§원칙 #19)
- ✅ API + Web 양 서버 살아있음 (5257/5234)

---

## 📦 미커밋 변경 (다음 세션 첫 작업)

### 백엔드 / 도메인 / DB
- `src/HitPan.Domain/Entities/Employee.cs` — 연차 속성 2개
- `src/HitPan.Infrastructure/Persistence/Configurations/EmployeeConfiguration.cs` — decimal(5,1) 매핑
- `src/HitPan.Application/DTOs/Employee/EmployeeDtos.cs` — 연차 필드 + UpdateAnnualLeaveRequest
- `src/HitPan.Application/DTOs/Employee/LeaveRequestDtos.cs` — LeaveCalendarDto/Row/Cell
- `src/HitPan.Application/Interfaces/IEmployeeService.cs` — UpdateAnnualLeaveAsync
- `src/HitPan.Application/Interfaces/ILeaveRequestService.cs` — GetCalendarAsync
- `src/HitPan.Application/Services/EmployeeService.cs` — SELECT/UPDATE/UpdateAnnualLeaveAsync
- `src/HitPan.Application/Services/LeaveRequestService.cs` — GetCalendarAsync
- `src/HitPan.API/Controllers/EmployeeController.cs` — PUT /annual-leave
- `src/HitPan.API/Controllers/LeaveRequestController.cs` — GET /calendar
- DB 변경: `employees` ALTER TABLE 2개 컬럼 추가 (이미 적용됨, 마이그레이션 X — 직접 SQL)

### 프론트
- `src/HitPan.Web/App.razor` — Pretendard MudTheme
- `src/HitPan.Web/Layout/Sidebar.razor` — 11그룹 재구성, 본사·대리점 제거, 메뉴명 다듬기
- `src/HitPan.Web/Layout/TopHeader.razor` — 좌측 로고 추가
- `src/HitPan.Web/Pages/Dashboard.razor` — Hero+Quick+KPI Hero+캘린더, 옛날 컴포넌트 제거, 알림 스택
- `src/HitPan.Web/Pages/Settings/EmployeePage.razor[.cs]` — 사원관리 그리드 연차 컬럼
- `src/HitPan.Web/Models/EmployeeModels.cs` — 연차 + LeaveCalendar 모델
- `src/HitPan.Web/Services/EmployeeService.cs` — UpdateAnnualLeaveAsync
- `src/HitPan.Web/Services/LeaveRequestService.cs` — GetCalendarAsync
- `src/HitPan.Web/wwwroot/index.html` — Pretendard CDN, design-tokens.css, hp-toss.css, hitpan-keyboard-nav.js
- **신규** `src/HitPan.Web/Components/Common/LeaveCalendar.razor` — 월간 달력
- **신규** `src/HitPan.Web/wwwroot/css/design-tokens.css`
- **신규** `src/HitPan.Web/wwwroot/css/hp-toss.css`
- **신규** `src/HitPan.Web/wwwroot/js/hitpan-keyboard-nav.js`
- **신규** `src/HitPan.Web/wwwroot/preview-design.html` (Stripe+Linear+Toss)
- **신규** `src/HitPan.Web/wwwroot/preview-design-toss.html` (토스 감성 강화)

### 백업
- `src/HitPan.Web/Layout/Sidebar.razor.bak_20260429`
- `src/HitPan.Web/Pages/Dashboard.razor.bak_20260429`
- `backups/before_status_alter_20260429_070710.sql` (DB 5.5MB)

### Untracked (커밋 대상 아님)
- `.claude/`, `.cursor/`, `logs/`, `tools/smoke-test/screenshots/`

---

## 🧠 다음 세션이 알아야 할 메모리

| 메모리 | 핵심 |
|---|---|
| `project_domain_policy.md` | hitpan.kr 22,000원/년 단일 도메인 결재 (4/29) — 사장님 미구매 상태 |
| `project_backoffice_separation.md` | 본사·대리점 메뉴 사이드바 제거 / /admin /reseller 코드 보존 |
| `project_mobile_ux_vision.md` | Phase 5 모바일 = 아이폰 홈 화면 스타일 (베타 후) |
| `feedback_workflow_unbroken.md` | §20 워크플로우 끊김 절대 금지 |
| `feedback_zero_warnings.md` | errors 0 + warnings 0 = 정합성·무결성 |

---

## 🎯 다음 세션 즉시 진행 시퀀스

### Phase A (5분) — 사장님 시연 통과 확인
1. 사장님 Ctrl+Shift+R → 5234 접속
2. 다음 5개 검증:
   - [ ] **헤더 로고** 좌측 표시 + 클릭 시 대시보드 이동
   - [ ] **사이드바 11그룹** + 본사/대리점 안 보임
   - [ ] **Hero 좌우 분할** + 권한 알림 카드 우측 표시 (어드민 첫 진입 시)
   - [ ] **챗봇** 헤더 1개만, 본문 챗봇 카드 없음
   - [ ] **월간 달력** 일자 셀에 직원 칩 (사장님 dev DB 데이터 거의 없으므로 빈 달력 + 4/28 1건만 보일 것)

### Phase B (10분) — 통합 커밋
사장님 시연 OK 떨어지면:
```
fix(ux): 토스 B 디자인 시스템 + 사이드바 11그룹 + 사원 연차 풀스택 + 대시보드 정리

- 디자인 시스템: design-tokens.css + hp-toss.css + Pretendard
- 사이드바 11그룹 재구성 (본사/대리점 사이드바 제거, 코드 보존)
- 헤더 로고 이동, 사이드바 로고 제거
- 사원 연차 풀스택 (employees ALTER + 백엔드 + 프론트 그리드)
- 대시보드 토스 Hero + Quick + KPI Hero + 월간 연차 달력 + 알림 스택
- 챗봇 통합 (헤더 1개만 유지)
- 메뉴명: 레거시 히트판 → 구 히트판
- 키보드 네비 JS (Enter→다음 칸, ↑↓ 그리드)
- 기능 추가 외 영역 헌법 준수 (백엔드/DB/워크플로우 0건 영향)
```

### Phase C (해당 시) — 사장님 추가 피드백
디자인 시연 결과 사장님 추가 수정 요청 가능. 예상:
- 알림 카드 색상·크기 조정
- 캘린더 직원 칩 표시 방식
- 사이드바 그룹 expanded 기본값
- 폰트 크기

### Phase D (다음 작업) — 우선순위 백로그
1. **P0-C 발주/매입 대량 처리** — P0-A로 봉합됐을 가능성 큼. 사장님 시연으로 확인
2. **P0-D Items.razor 162건 일괄 자동발주 UX 경고** — `Items.razor:211 foreach` 직전에 다이얼로그
3. **P0-F /api/tenants/me 500** — `TenantService.GetCurrentAsync` EF enum 매핑 충돌 가능성
4. **마커스 리 인계** (`docs/개발/erp/marcus_lee_20260429_domain_tunnel.md`) — 사장님 hitpan.kr 구매 후 진행
5. **베타 9곳 EXE 재빌드** — 새 디자인 + 새 채번 + 도메인 매핑 반영

---

## 🔑 환경 정보

```
사장님 PC: localhost:5257 (API), localhost:5234 (Blazor dev)
DB: hitpan_erp / hitpan / Hitpan2025!
테스트 계정: tenant@hitpan.kr / Admin1234!
임시 터널: https://dem-book-typing-blair.trycloudflare.com (사장님 PC 켜진 동안만)
```

```
ISCC: C:\Users\소순근\AppData\Local\Programs\Inno Setup 6\ISCC.exe
번들: installer-build/bundle/* (268MB)
시드: installer/hitpan_db.sql (396KB, 깨끗)
EXE: dist/HitPan-Setup-tenant-001~010.exe (재빌드 대기 — 새 UX/UI 반영 필요)
```

---

## ⚠️ 다음 세션 CTO 주의사항

1. **미커밋 변경 다수 — 통합 커밋부터** (Phase B). 사장님 시연 OK 받기 전 잘게 쪼개 커밋 X.
2. **백업본 보존** — `Sidebar.razor.bak_20260429`, `Dashboard.razor.bak_20260429`. 사장님 롤백 요청 가능성 대비.
3. **§원칙 #1 (덮어쓰기 X)** 사장님이 명시 강조하셨음. 사원 연차 외 기능은 백엔드/DB/워크플로우 절대 건드리지 말 것. 디자인·CSS·Razor 마크업만 OK.
4. **API 재시작 자주 X** — RateLimit in-memory 카운터 리셋, 사장님 토큰 만료. dotnet watch 권장.
5. **사장님 보시는 화면이 다른 PC면 5234 직접 접속 안 됨** — 임시 터널 URL 사용. 마커스 리 도메인 작업 끝나면 영구 URL.
6. **PowerShell + 한글 경로 + cmd //c 조합 금지** — 인코딩 깨짐.

---

## 📌 다음 세션 시작 프롬프트

> 사장님: "이어서 가자. 어제 인수인계서 봐."
>
> CTO: "넵. `docs/개발/erp/next_session_prompt_20260429_uxui.md` 봤습니다. Phase A (사장님 시연 통과 확인) 즉시 들어갑니다. 5234 살아있는지 헬스체크부터 하겠습니다."

---

## 🛡 §절대원칙 자가감사 (이번 세션)

| # | 원칙 | 준수 여부 |
|---|---|---|
| #1 수정 OK, 덮어쓰기 X | ✅ 사장님 명시 결재로 일부 컴포넌트 제거(KpiCard·차트 등)는 컴포넌트 코드 보존 + 페이지에서만 제거 |
| #7 SaaS ↔ ERP 권한 혼용 금지 | ✅ 본사·대리점 사이드바 제거, /admin /reseller 페이지 보존 |
| #8 6단계 워크플로우 순서 | ✅ 메뉴 11그룹에 정신 유지 (마스터→매입→판매→...) |
| #15 빈 catch 금지 | ✅ EmployeeService.UpdateAnnualLeaveAsync, Dashboard.LoadUserNameAsync 진단 로그 |
| #17 InnoDB 명시 | ✅ ALTER TABLE이라 무관 (테이블 이미 InnoDB) |
| #19 errors 0 + warnings 0 | ✅ 빌드 통과 |
| #20 워크플로우 끊김 금지 | ✅ 연차는 워크플로우와 분리, 사원 컬럼 추가는 영향 0건 |
