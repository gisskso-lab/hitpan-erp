# S4 관문 ① 측정 장치 — 항목 대응표 (HQA 가 읽는 자리)

> 근거: 설계 `docs/설계/백오피스/20261116_S4관문1_GitHub서버실측_설계서.md` §4-2 · §4-3 · §4-4 · 작업지시서 §2 B-1~B-4.
> 판정 기대값 정본: `16e88c01:docs/검증/외부/20261113_4X대상재결정_S4_A프라임_3인.md` §6 · 11/7 항목 `16e88c01:docs/검증/외부/20261107_4X대상확인_S4_3인.md` §3 · §4 · §5.
> **판정자 = HQA.** 이 장치가 쓰는 글자는 「장치 판독」 일 뿐 판정이 아니다. 판독 글자는 정본 글자만 쓴다(`rules.json` `readingText`).
> 측정 규칙 · 표본 수 · 간격 · 횟수 · 선택자 · 판독 글자는 **`rules.json` 한 곳**에 있다. 장전 뒤 1글자라도 바뀌면 새 회차다(설계 §6-6).

## 1. 항목 대응표

> 🟡 **고침5(H3 · 2026-11-16) 상태 표시**: 이 표 끝 열 「HQA S1~S4」 는 고침5 부터 「HQA 회신 · 사전 고정 상태」 로 읽는다(결-4 甲 · 결-5 · 옛 열 글자 유지 · 헌법 #1). 정본 32행 대응 · `/before` 실패 꼴 · 노출 칸은 §9 · §10 이 정본이다. I-8(T3) = 「대표 결정 대기 · 매핑 안 됨 = 합격 처리 불가」.

| # | 정본 항목(출처) | 장치 파일 · 함수 | 장치가 하는 입력(신뢰 입력만) | 장치가 읽는 값 | `/before` 대조 | 장치 판독(`judge.js`) | HQA 「S1~S4」 |
|---|---|---|---|---|---|---|---|
| I-1 | 키보드 탭 도달 (11/7 §3-2) | `items/i-items.js` `I-1` | `#before` 마우스 누름 → Tab × `device.i1.tabs` | Tab 마다 초점 요소(태그 · 역할 · 소속 `[data-row]` · 상태 단추 여부 · 열린 목록 안 여부) · 열린 목록 수 | 같은 입력 기록 · 실패 정의 없음 | 판독값 기록 · 판정 = HQA · (고침4 G5 · L-2) `/after` 키보드 미도달 표본은 유효에서 빼는 규칙 그대로 + 칸마다 `afterUnreachable {count · samples[] · tabPath[]}`(`verdict-device.json` · Tab 경로는 판정 칸 스키마 칸만) = **판독값 기록 · 판정 = HQA** | **HQA 회신 대기** |
| I-2 | Enter/Space(길게 누름 포함) (11/7 §3-2) | `I-2` · 경우 4 = enter-short · space-short · enter-long · space-long | Tab 으로 상태 단추 → 짧게 = press 1회 · 길게 = 같은 키 down 반복(첫 지연 · 횟수 · 간격 = `device.i2.longPress` · 두 번째 down 부터 repeat 표지) → up | 열린 목록 수(끝 · 최대) · `#pick-count` 차이 · `#picks` 새 줄 · 초점 · 바뀐 줄 | 기록 | 판독값 기록 · 판정 = HQA | **HQA 회신 대기** |
| I-3 | 덮임(창 높이 330/200 · 아래쪽 줄) (11/7 §3-2 · 검증팀장 1) | `I-3` · 창 2 = 1280×330 · 1280×200 | 아래쪽 줄(`device.i3.row` = 10 · 첫 쪽 마지막 줄) 상태 칩 locator 누름 | 목록 상자 좌표 · 이웃 줄(`tr`) 좌표 · 겹침 넓이(줄마다 · 합) · 창 밖 잘림 · PNG `shots/I-3.*.cover.png` | 같은 창 · 같은 줄 `/before` 겹침 기록 | 판독값 기록 · 판정 = HQA | **HQA 회신 대기** |
| I-4 | Esc(마우스로 연 뒤 · 키보드로 연 뒤 · 닫힌 뒤 초점) (11/7 §3-2) | `I-4` · 경우 2 = mouse-open · kbd-open | ~~칩 locator 누름 또는 Tab → Enter 로 열고 → Escape~~ (고침6 11/16 정정) 칩 locator 누름 또는 Tab → Enter 로 열고 → 열린 메뉴 관측(50ms 간격 · 못 보면 Esc 0 · 장치 실패) → 700ms 유지 → Escape → 메뉴 0 관측 → 최대 3초 polling(HQA #850 댓글 6709803008 L46 · 모든 화면 같은 값) | Esc 앞뒤 열린 목록 수 · 닫힌 뒤 초점 · 같은 줄 상태 단추 여부(고침6: 3초 안 한 번이라도 참 · HQA 원문 식 `hqaFocusRow1Restored` 따로 · 시각 기록) | 기록 | 판독값 기록 · 판정 = HQA | **HQA 회신 대기** |
| I-5 | 열린 채 Tab/Shift+Tab (11/7 §3-2) | `I-5` · 경우 2 = tab · shift-tab | Tab → Enter 로 열고 → Tab 또는 Shift+Tab | 키 앞뒤 초점 · 열린 목록 수(끝 · 최대) | 기록 | 판독값 기록 · 판정 = HQA | **HQA 회신 대기** |
| I-6 | A 줄 열고 Tab 으로 B 줄 → Esc/엔터 = 두 목록 동시 열림 0 (11/7 §3-2) | `I-6` · 경우 2 = esc · enter | A 줄(`device.i6.row`) Tab → Enter 로 열고 → 다른 줄 상태 단추에 닿을 때까지 Tab(최대 `device.i6.maxTabs`) → Escape 또는 Enter | 열린 목록 수 **최대값** · B 줄 번호 · 닿은 Tab 수 · 끝 초점 | 2 이상 재현 여부 | 기대값 「두 목록 동시 열림 0」 에 어긋난 `/after` 표본 수 · `/before` 재현 0 이면 「대조 무효」 · 판정 = HQA | **HQA 회신 대기** |
| I-7 | 열린 채 다른 줄 위 바깥 누름 → 행 클릭 0 (11/7 §4 C5) · F10 「연 줄뿐 · 다른 줄 0」 (11/7 §3-3) | `I-7` | A 줄(1) 칩 locator 누름으로 열고 → B 줄(2) 첫 칸 가운데 좌표를 `page.mouse.click` | `#rowclick-count` 차이 · `#rowclicks` · `#detail[data-open]` 앞뒤 · 바뀐 줄 · 다른 줄 바뀜 · 누른 점이 목록 상자 안인지 | 행 클릭 또는 다른 줄 바뀜 재현 여부 | 기대값 「행 클릭 0」 · 「연 줄뿐 · 다른 줄 0」 에 어긋난 `/after` 표본 수 · `/before` 재현 0 이면 「대조 무효」 · 판정 = HQA | **HQA 회신 대기** |
| I-8 | T3 (11/7 §3-2) | 없음 | **정의 후보 있음(11/7 작업지시서 L306 · 공백 상태 칸) · HQA 확인 대기 → 측정 코드 0** | — | — | **미측정 · 정의 후보 있음 · HQA 확인 대기** (회신 대기 ⑥ T3 · 확정 전 장전 거부) | **HQA 회신 대기** |
| I-9 | 지연 0/150/300 (11/7 §3-2) | `run-items.js` 지연 단 | I-1 ~ I-7 을 세 단에서(단마다 프록시 새로 띄움) | 위와 같음 + 프록시 인자(표본 `proxy.args`) | — | 위 항목별 | **HQA 회신 대기** |
| N-1 | 화살표 빠른 연타 · 0/150/300/500 (11/13 §6) | `items/n-items.js` `N-1` | Tab 으로 상태 단추 → Enter 로 열기 → ↓ · ↑ 번갈아 `device.n1.count` 회 · 간격 `device.n1.intervalMs` | 회로 신호 3종 · 목록 닫힘 · 초점 = 같은 줄 상태 단추 여부 · 다른 줄 `data-status-kind`/칩 글자 바뀜 · `#picks` 다른 줄 새 줄 · PNG `shots/N-1.*.n1-end.png` | 300 · 500 단 회로 죽음 재현 여부 · (고침4 G1 · G3 · D-1 ⓒ) 대조 화면 = `rules.n1Contrast`(㉡ = A2 화면 · 화면 이름 `a2` · 주소 `/after-a2` · 기대 data-variant `a2` · 기대 CSS `after` / ㉠ = `/before`) · ㉡ 이면 `/before` N-1 칸은 계속 재되 **「기록만 · 판정에 안 씀」**(`beforeRecordOnly` · 칸 `n1Role`) · 그 칸의 「장치 실패」 빨강은 그대로 두고 **「장치 건강 · 판정 무관」** 표지(원자료 `recordOnly` · 칸 `healthOnly` · `redReasons` 머리 · `beforeRecordOnly.failTag` — 고침4 보탬 PM F-6 ③) · a2 칸 = 지연 300 · 500 두 단만(PM F-6 ② · 0 · 150 계획 = buildPlan 거부) · a2 미도달 = `/before` 와 같은 U1~U4 · 키보드 도달 0 표본은 재현 셈에서 뺌(사유 「A2 화면 키보드 도달 0」) | 정본 §6 글자 그대로: `/before` 300 · 500 재현 0 → **대조 무효** · `/after` 회로 죽음 또는 다른 줄 변화 1회라도 → **🔴** · 초점이 같은 줄 상태 단추 아님 → **🟡** · 셋 다 아니면 「판독값 기록 · 판정 = HQA」 | **HQA 회신 대기** |
| N-2 | 되끌린 단추 Enter 두 번 (11/13 §6 · 관찰) | `N-2`(N-1 같은 문맥 이어서) | N-1 직후 Enter · `device.n2.gapMs` · Enter | `#pick-count` 차이 · `#picks` 새 줄 · 초점 | 기록만 | 관찰 칸 — 합불 기준 아님 · 판독값 기록 · 판정 = HQA | **HQA 회신 대기** |
| — | Chromium 외 WebKit/Firefox | 없음 | 재지 않음 | — | — | **미측정** | — |

## 2. 장치 판독 규칙 (`judge.js` · 설계 §4-3)

- **회로 죽음** = 재연결 표시 요소(`#components-reconnect-modal`) 보임 · 페이지 이동이 아닌 `_blazor` 웹소켓 닫힘 · 회로 종료 꼴 콘솔 오류(`device.circuit.consolePattern`) — 셋을 따로 남기고 하나라도면 죽음. 입력 **전** 신호가 있으면 그 표본은 무효(오류 칸).
- **정해진 표본 수 모자람 = 빨강**(고침4 G4 · D-3): 계획 칸(항목 × 경우 × 화면 × 지연 × 창)마다 유효 표본 수 < 정해진 표본 수(`hqaPending.samplesPerCell` · HQA 회신 대기 · 지금 값은 임시)면 그 칸 「장치 실패」 + 빨강 · 사유 「정해진 표본 수 모자람 유효/정해진」. 유효 0 이면 `zeroReason` 칸에 `/before` 칸 「/before 표본 0」 · a2 칸 「A2 화면 표본 0」 · 그 밖 「칸 표본 0」 — 「대조 무효」(표본 있음 · 재현 0) 와 따로 적는다. ~~옛 줄(고침3까지): 칸 유효 표본 0 일 때만 「장치 실패」~~ → **대체됨(고침4 G4 · 설계서 §4-3 · §7-2 해당 글자도 대체)**.
- 재는 순서 · 문맥(고침4 G6 · L-5): 같은 지연 단 안 `after → a2 → before`(`plan.screenOrder`) · 표본마다 새 문맥 · 원자료 `order`(전체 순번) · `contextId`(문맥 순번) — 순서 어긋남 · 순서 없음 · 부모 표본 문맥 겹침 · 파생 표본 문맥 ≠ 부모 = 빨강.
- 판정 칸 가림 충돌(고침4 G7): 아래 §7 판정 칸에 가림 꼴이 걸리면 그 표본 `device_failure`(사유 「판정 칸 가림 충돌」) + 빨강.
- 계획 밖 표본 · 읽지 못한 줄 · `samples.jsonl` 없음 · 표본 0 도 빨강.
- 판정 글자(🔴 · 🟡 · 대조 무효)는 N-1 에만, 정본 §6 글자 그대로. 11/7 항목은 기대값 글자가 있는 I-6 · I-7(+F10) 만 「맞음/어긋남 표본 수」 와 대조 무효 여부를 적고, 나머지는 「판독값 기록 · 판정 = HQA」.

## 3. HQA 회신 대기 칸 — 임시 기본값 (정해진 값 아님)

`rules.json` `hqaPending` — **장전(armed) 때 M0 단계에서 거부**한다(고침1 F8 · `hqa-gate.js`): 필수 6칸(①~⑥ · 목록은 `hqa-gate.js` 에 고정)이 모두 status 「HQA 회신 반영 · 댓글 <숫자>」 꼴 · value 비지 않음 · 목록 밖 칸 0 일 때만 통과 — 칸 삭제 · 다른 글자 · 빈 값으로 우회 못 한다. `run-items.js` 도 같은 관문으로 다시 거부(종료 코드 4). 리허설은 항목 측정 0 이라 영향 없음.

| 칸 | 회신 대기 내용(#850 댓글 6896955850) | 임시 기본값(장치가 돌게만 둔 값) |
|---|---|---|
| ① S1~S4 | HQA 이름표 ↔ 위 표 행 | 없음(위 표 마지막 칸 비움) |
| ② 원자료 형식 | JSONL · JSON · PNG 안 | `samples.jsonl` · `verdict-device.json` · `shots/*.png`(설계 §5 꼴) |
| ③ 칸별 표본 수 | 칸마다 몇 번 | 기본 3 · N-1(과 N-2) 5 → 계획 칸 102 · 계획 표본 342(고침4 보탬 PM F-6 ②: a2 N-1 칸 = 대조 단 300 · 500 두 칸 × 5 · ~~고침4 G1 의 352(a2 4칸 × 5) · 칸 104~~ **대체됨** · 그 전 옛 값 332) · 이 수가 G4 「정해진 표본 수」 · 「옛 글자」(값커밋): 위 「계획 칸 102 · 계획 표본 342」 → **지금 값 = 계획 칸 120(자기 칸 112) · 계획 표본 396**(H4 §7-1 · 장전 전 공개 v3 §5-5) |
| ④ 「지연 N」 단위 | 프록시 방향당 인자인가 왕복인가 | 방향당 인자 = N(왕복 약 2N 추가) · 지연 0 도 프록시 경유(인자 0) — ~~고침5 결-2 乙: 왕복 N(인자 N/2)~~ (고침6 11/16: 乙 철회 · 대표 결재 11/16 밤 · 방향당 = HQA 집행 단위 · 개발팀 사전 고정 · 장전 전 공개) |
| ⑤ 헤드리스 셸 · 창 등 조건 | 실행 조건 | 릴리스 크롬 헤드리스(헤드리스 셸 안 씀) · 창 1280×720(I-3 만 높이 330/200) · 배율 1 · ko-KR |
| ⑥ T3 정의 | I-8 T3 가 무엇인지 — 후보: 11/7 작업지시서 L306(공백 상태 칸 → `-`) · 다른 뜻 후보: 11/7 830배포 결과 L23(CS 표시줄) | 없음 · 측정 코드 0(정의 확정 뒤에도 측정은 별도 작업 · 새 회차) · 「옛 글자」(값커밋): 위 「없음 · 측정 코드 0」 → **지금 값 = status 「HQA 회신 반영 · 댓글 6921942407」 · value = rules `hqaPending.T3.value` 글자(11/7 작업지시서 L306 글자 고정 · 항목 단위 · 빈 문자열 · null = 명시적 미판정/관찰 항목 · 정상 간주 금지 · HQA Q-H2 조건) · 측정 코드 = `items/i-items.js` `I-8`(H4)** |
- 값커밋(지금 값): ③④⑤ status = 「개발팀 사전 고정 · 장전 전 공개 · 댓글 6921734895」(HQA 수락 아님 · ③④⑤ 는 `hqa-gate.js` 가 이 꼴도 허용) · ⑥ T3 status = 「HQA 회신 반영 · 댓글 6921942407」(HQA Q-H2 조건부) · 옛 글자 = rules 칸마다 `_옛값_값커밋`. 관문(`hqa-gate.js`) = 지금 rules **통과** — 이제 장전을 막는 것은 「push 0」 순서뿐이다.

## 4. 장치 선택값 (정본에 없는 실행 값 — 기대값 아님 · HQA 의심 제기 대상)

| 값 | 어디 | 비고 |
|---|---|---|
| 대상 줄 = 1(I-2 · I-4 · I-5 · I-6 · I-7 A · N-1) · 2(I-7 B) · 10(I-3 아래쪽 줄) | `device.i*` · `device.n1` | 기본 쿼리(rows 24 · 쪽당 10). 1번 = 쪽지 건(상태 5종) |
| 읽기 선택자 `.bo-status-chip` · `.mud-popover.mud-popover-open` · `#components-reconnect-modal` | `device.sel` | **시험 앱 README §5 밖** — MudBlazor 9.3 · blazor 기본 꼴. 나머지 읽기는 README §5 선택자 |
| 상태 단추 판별 = `[data-row]` 안 · `button` 또는 `role=button` · 열린 목록 밖 | `items/_lib.js` `readState` | 고침 전 = 9.3 감싼 div(role=button) · 고친 꼴 = 단추 |
| 길게 누름 = 첫 지연 500ms · 반복 15회 · 간격 33ms | `device.i2.longPress` | 합성 키 반복이 실제 키 반복과 같은지는 HQA 판단(설계 §10) |
| N-1 연타 = ↓ · ↑ 번갈아 24회 · 간격 0 · 연타 뒤 기다림 = 600 + 4×지연 + 1500ms | `device.n1` · `device.settle` | 고침6 11/16: 지연 = 방향당 ms · settle 식 무변(결-6 · 갈래 B `dd64098b` 방향당 시절 식) |
| I-4 Esc = 열린 메뉴 관측(간격 50ms) → 700ms 유지 → Esc → 메뉴 0 관측 → 최대 3000ms polling(간격 50ms) · 옛 settle 안 씀 | `device.i4` escWaitMs · openPollMs · pollAfterEscMaxMs · menuZeroFailCases | 고침6 11/16(결-2 · 결-3 · 결-5 · 결-7) — 700 = HQA 원문 · 50 = 개발팀 사전 고정 · 메뉴 0 못 봄 = kbd-open 만 장치 실패(구현 판단 · PM 확인 대기) · I-6 esc = HQA 기다림 절차 없음 · 그대로 |
| J1-B 11/17: 700ms 기다림 = 간격 50ms 관측(Esc 직전 읽기 최소 1 · 그 사이 열린 목록 0 = Esc 누름 0 · 장치 실패) · 규칙 값 단언(양의 유한수 · 배열 · 아니면 입력 0) · menuZeroFailCases = kbd-open 만 **PM 승인** | `device.i4` `_openCountAtEsc_표지_J1B` · `_ruleAssert_표지_J1B` · `_menuZeroFailCases_표지_J1B` · `deviceReading` i4OpenNotHeld · i4RuleBad | 위 줄 「PM 확인 대기」 닫힘. mouse-open 판독 창 = 옛 settle(600 + 4×지연) → 메뉴 0 상한(3000 + 2×지연) — HQA 절차(메뉴 0 관측)를 따른 것 · 공개 묶음 「공개 기준 변경 이력」 몫(장치 코드 무변) |
| J1-C 11/17: 위 줄 「간격 50ms 관측」 바뀜 → 700ms 기다림 중 읽기 0 · Esc 직전 한 번 읽기(열린 목록 0 = Esc 누름 0 · 장치 실패) **PM 결정(관찰자 영향 줄이기)** | `device.i4` `_hold_표지_J1C` · `deviceReading` i4OpenNotHeld(그대로) | 까닭 = 기다림 중 약 14회 읽기는 HQA 원 절차에 없는 읽기 · 공개: 기다림 중 순간 닫힘 미관측 · Esc 직전 읽기와 Esc 사이 틈 |
| N-2 Enter 사이 120ms | `device.n2` | |
| 회로 연결 기다림 = `_blazor` 웹소켓 첫 프레임 · 그 뒤 600 + 4×지연 ms | `run-items.js` · `device.settle` | 미리 그리기 켬(README §9) — 붙기 전 입력 0 · 고침6 11/16: 지연 = 방향당 ms(식 무변) |
| 콘솔 꼴 = circuit · 「connection … closed/disconnected/lost」 · 「websocket … closed/failed」 중 하나(대소문자 무시) | `device.circuit.consolePattern` | 신호 정의의 맞음은 `/before` 재현으로 드러남 |

## 5. 입력 · 문맥 규칙 (설계 §4-4)

- 입력 = `page.keyboard` · `page.mouse` · locator `click` 만. `page.evaluate` 는 `items/_lib.js` 의 읽기 2곳뿐(읽기 전용). 스크립트 주입 · 합성 이벤트 0 — `selftest-judge.js` 정적 검사가 확인하고, 일부러 심은 나쁜 원문(대조군 7개)이 걸리는지까지 본다.
- 표본마다 새 브라우저 문맥(`browser.newContext`) · 지연 단마다 프록시 다시 띄움(시험 앱 나무 `proxy/delay-proxy.js` 를 사본 없이).
- 이전 내부 측정 스크립트 · 결과는 읽지도 옮기지도 않았다. 이 장치는 정본 글자 · 시험 앱 README · 설계서만으로 만들었다.

## 6. 출력 (설계 §5 중 갈래 B 몫)

| 파일 | 내용 |
|---|---|
| `samples.jsonl` | 표본 1줄 = item · case · screen · delay · window · cell · sampleNo · ts · inputs(입력 열 · 시각) · readings · circuit(신호 3종 · dead · detail) · shot · errors · valid · proxy(인자 · 단위) · playwrightVersion · browserVersion · rulesVersion |
| `verdict-device.json` | 머리 표지 「장치 판독 — 판정 아님 · 판정 = HQA」 · 규칙 SHA-256 · 칸별 표본 수 · 항목별 장치 판독 · 빨강 사유 · 남은 HQA 회신 대기 칸 · 미측정 목록 |
| `shots/*.png` | I-3 덮임 · N-1 끝 상태(눈 확인용) |

## 4. 리허설 점검(probe) — 고침1 F10 (판정 무관)

- MODE = rehearsal 이고 M4 가 통과한 런에서만 `probe.js` 가 돈다(장전 런 0).
- 남기는 것 = `probe.json` 하나: NODE_PATH 로 playwright 풀림(참/거짓 · 판 · ms) · `/before` · `/after` 마다 README §5 선택자 + 상태 칩 · 재연결 표시의 존재 여부와 찾은 ms · (고침2 N1-a) `/before` 만 · 지연 0 단만: R1 1번 줄 구조(태그 · role · tabindex · display · 사각형 수 · mud-/bo- 클래스만 · 입력 0) — `device.probe.distinguish = true` 인 리허설 3 한 번만 R3 행 클릭 양성 대조 · R2 Tab 경로 · 구분 판정 `k_check`(K-결함 / K-장치 / K-미결 · 글자 = rules.json `kCheck`)를 더한다. 고침1 의 Tab 도달 · 목록 열림 확인 칸은 뺐다(고침2 N2 · 항목 결과와 같은 모양의 정보). 입력 = `#before` 누름 + Tab + 행 클릭 1회 뿐 · `/after` 입력 0.
- 그림 검사 1건(PM 결정): `/after` 첫 화면을 입력 없이 한 장 찍어 F6 검사(PNG 서명 · 텍스트 덩어리 0)만 돌린 결과 = `probe.json` `png_check`(통과/보류 · 덩어리 종류 이름만 · 값 0). 통과면 `shots/probe.after.first.png` 로 원자료에 · 보류면 그림 파일 0. 장전 런 그림이 F6 에 전부 보류되는 위험을 리허설에서 미리 보려는 것.
- 남기지 않는 것 = 항목 판독 · 표본 · 판정 글자 · 회로 신호 · 그 밖의 그림 · 프록시. 항목 결과를 보지 않으므로 설계 §6 「발행 전 고정 · 결과 본 뒤 변경 = 새 회차」 와 충돌하지 않는다. 쓰임은 「선택자가 맞나」 확인뿐.
- (고침4 G10 · S-4 · D-9 ⑦ · D-10) 리허설 4: `/before` R1 · R2 · R3 그대로 · `/after` 칸 무변 · `n1Contrast = a2` 이면 **a2 화면 = R1(입력 0 구조 읽기)만** — 1번 줄 안 `tabIndex ≥ 0` · 사각형 > 0 요소 수(`a2_check.focusableVisible`) · Tab · 키 · 누름 · 선택자 · 그림 · 지연 단 0. 주소는 `plan.screenTable` 값(probe 안 주소 글자 0). 판독: 요소 수 > 0 → 「A2 화면 줄 안 초점 받을 보이는 요소 ≥ 1 → ㉡ 유지」 / 0 → **「되돌림 규칙 해당(㉠)」** (어느 쪽이 나올지는 기대값으로 정하지 않음). 정적 검사 + 심은 위반 11(a2 에 `keyboard.press` · `mouse.click` · `tabTo` · `rowClickControl` · 주소 글자 등) 전부 빨강.
- (고침4 보탬 PM F-6 ④ · §9 M4) a2 R1 은 화면 판 속성 둘 — `#fixture-root[data-variant]` · `link[data-fixture-css]` — 을 `getAttribute` 로 **읽기만**(입력 0) 해서 `a2_check.variant` · `a2_check.css` · `expected` · `device`(ok/red) 에 기록 · 기대(`plan.screenTable.a2` = `a2` · `after`)와 다르면 **장치 빨강**(`deviceReading.a2ProbeVariantDiff` · probe 종료 코드 2 · 되돌림 판단과 별개 · 판정 함수 `judge.js a2VariantCheck`). 정적 검사 허용 = 두 속성 읽기 글자 그대로 각 1회 · 심은 위반 4(읽기 자리에 click 2 · evaluate 로 바꿈 · 기대 대조 뺌) 전부 빨강.
- (H4 §7-3 · H4-G-B · 검증팀장 [4] Y-1) **M4(`boot.sh`) 는 비교 화면 주소 `/after-e1t3` · `/after-v2` 를 HTTP 200 만 본다.** 두 화면의 판 속성 대조(`#fixture-root[data-variant]` = `e1t3` · `v2` · `link[data-fixture-css]` = `after` ↔ `plan.screenTable`)는 **probe 칸**이 한다 — `probe.js contrastVariant`(`getAttribute` 읽기만 · 입력 0 · e1t3 = R1 꼴 · v2 = `variantOnly` 꼴 = 판 속성만 · 줄 읽기 0 · T3 미리 노출 0) → `contrast_checks.{e1t3,v2}.device` · 다르면 장치 빨강(`deviceReading.contrastVariantDiff` · probe 종료 코드 2). 그러므로 「M4 칸 = 200 · data-variant · data-fixture-css」 는 「200(M4) + 변형 · css(probe)」 로 읽는다. 열쇠 이름은 [3-V] P3-3 에 따라 21자 이하로 바꿨다(옛 이름 24자 = 가림 검사에 걸림 · 쓰는 곳 3곳 모두 바꿈).

## 7. 판정 칸 스키마 · 판독 글자 · 읽기 규칙 (고침4 G11 · 발행 전 고정 · 정본 = `rules.json`)

- **판정 칸 스키마** = `rules.json` `judgeSchema`(파일별 칸 경로 꼴). 회로 = 상태(`circuit.dead` 등) · 시각(`circuit.detail.*AtMs[]`) · 재연결 표시 여부 / 초점 = 역할 · 태그 · 줄 번호 · 알려진 data 속성(`data-row`)만 — **자동 id · 회로 ID 원문 0**(콘솔 글자 · `focusAfter.id` · `text` 는 판정 칸 밖 → 보통 가림 대상) / `valid` · 판독 글자 · `github_sha`(`device_commit`) · 런 ID. 이 칸에 가림 꼴이 걸리면 판독기 = 그 표본 `device_failure`(「판정 칸 가림 충돌」) · 가림 = 그 칸을 가리지 않고 `run.json` `mask.judge_field_conflicts` 에 남김 → 누출 검사가 그 파일을 보류. 가림 기록 `mask.records` = 파일 · 칸 경로 · 꼴 종류 · 길이(**값 0**). 합성 대조군 2(`selftest-leak.js` `g7.*`).
- **판독 글자 표**:

| 글자 | 종류 | 어디 |
|---|---|---|
| 대조 무효 · 🔴 · 🟡 | **판정 글자**(정본 · N-1 · I-6 · I-7 만 · 정본 조건일 때 옮겨 적음) | `readingText.N-1` · `I-6` · `I-7` |
| 관찰 칸 — 합불 기준 아님 · 기록만 | 정본 글자(N-2) | `readingText.N-2` |
| 판독값 기록 · 판정 = HQA | 장치 글자 | `readingText.record` |
| 장치 실패 · 정해진 표본 수 모자람 유효/정해진 · /before 표본 0 · A2 화면 표본 0 · 칸 표본 0 | 장치 글자(칸) | `readingText.cellFail` · `cellShortReason` · `*ZeroReason` |
| 키보드 도달 0(측정됨) · /after 키보드 도달 0 · **A2 화면 키보드 도달 0** · A2 화면 키보드로 열기 불가 — 재현 시도 0 | 장치 글자(표본 · 칸) | `deviceReading` |
| 기록만 · 판정에 안 씀 | 장치 글자(`/before` N-1 칸 · ㉡) | `readingText.N-1.recordOnly` |
| 장치 건강 · 판정 무관 | 장치 글자(「기록만」 칸의 장치 실패 표지 · 판정 빨강과 가름) | `readingText.N-1.recordOnlyHealth` |
| A2 화면 판 속성 불일치 — 장치 빨강(되돌림 판단과 별개) | 장치 글자(리허설 probe · a2 판 속성) | `deviceReading.a2ProbeVariantDiff` |
| 판정 칸 가림 충돌 · 재는 순서 어긋남 · 순서 기록 없음 · 문맥 순번 어긋남 | 장치 글자(빨강 사유) | `deviceReading` |
| ㉡ 유지 · 되돌림 규칙 해당(㉠) · A2 화면 1번 줄 없음 | 장치 글자(리허설 probe) | `deviceReading.a2Probe*` |

- a2 표본의 판독 · 사유 글자에는 주소 글자(`/after…`)를 쓰지 않는다 — 「A2 화면 …」 으로만(CTO C-4 · 자체시험 `G4-⑪`). 원자료 표본엔 화면 이름(`screen`) · 주소(`screenPath`) · 실측 판(`variantMeasured` · `cssMeasured`)을 따로 적는다(PM 조건 ②).
- **「대조 불가 ≠ 대조 무효」**: 대조 불가 = 그 칸 「장치 실패」(정해진 표본 수 모자람 · 표본 0 · 판정 칸 가림 충돌 · 순서 어긋남) → 대조 자체를 못 함 · 장치 빨강. 대조 무효 = 표본은 정해진 수만큼 있는데 대조 화면이 결함을 재현 못 함 → 정본 판정 글자 · 통과로 안 셈 · 장치 빨강 아님.
- **「장치 글자 ≠ 판정 글자」**: 장치 글자는 장치가 잰 사실이고, 판정 글자는 정본 조건에 맞을 때 장치가 옮겨 적는 정본 글자다. 판정자 = HQA.
- **D-1 ⓒ 읽기 규칙**: 「N-1 에 한해 `/before` 를 A2 화면(화면 이름 `a2` · 주소 `/after-a2`)으로 읽는다 · 11/7 항목의 `/before` 는 운영 판 그대로」 (`readingText.N-1.contrastReadRule` · 판정 글자 무변 · `verdict-device.json` `items.N-1.contrastScreen`). ㉠ 으로 되돌리면 `n1Contrast = before` 이고 이 규칙은 쓰이지 않는다.
- 화면 가르기: 장치 · 판독기는 화면을 `plan.screenTable` 의 **화면 이름 · data-variant** 로만 가른다 — 주소 앞머리 판단 0(PM 결-1 조건 ① · 자체시험 `G4-C-2`).

## 8. 정본 전 행 대응 (고침4 G12 · D-2) — 11/7 §5 + 11/13 §6

> 🟡 **고침5(H3 · 2026-11-16) 상태 표시**: 이 표(R1~R25)는 그대로 두고 §9 가 정본 32행으로 넓힌다 — 빈 행 #24 · #25 · #26 · #31 · #32 채움 · 1:1 아닌 행 #3 · #9 · #13 · #15 표시. 끝 열 「HQA S1~S4」 → 「HQA 회신 · 사전 고정 상태」(§9 끝 열). R3 의 「I-1 ~ I-5 는 정본에 실패 정의 없음」 은 I-1 · I-3 · I-4 에 틀림(V2 F-2) → **§9 #3 이 대체**. R10 T3 = 「매핑 안 됨 = 합격 처리 불가 · 대표 안건 ①」.

> 출처 SHA 고정: **A** = `16e88c01:docs/검증/외부/20261107_4X대상확인_S4_3인.md` §5(대표 확정 범위 = §4 끝 「대표께 올릴 최종 범위」 단락 · 항목 글자 §3-2 · §3-3) · **B** = `16e88c01:docs/검증/외부/20261113_4X대상재결정_S4_A프라임_3인.md` §6.
> 처리 칸: 장치 칸(파일 · 키) ↔ 기대 글자 출처 / 「미측정」 / 「입력 안 함」(장치가 재지 않고 HQA · 원문 몫) / 「대상 밖」(관문 ① 장치 범위 밖). HQA S1~S4 칸은 전부 **HQA 회신 대기**(값 0 · `hqaPending.S1toS4`).

| # | 정본 행 | 출처 | 장치 칸 | 기대 글자 출처 · 처리 | HQA S1~S4 |
|---|---|---|---|---|---|
| R1 | 관문 ① 은 blob 3개 = PR #850 머리 대조 뒤 시작 | A §5 · §3-1 | `m0-assert.json` `blobs` · `checks` `m0_5_blobs`(시험 앱 ↔ 기능 머리 `16e88c01` 원 경로) | 장치 단언(기대 = blob 같음) · 쌍 수 4 → 6 은 G-값(F′) | HQA 회신 대기 |
| R2 | 새 코드 커밋이 오면 관문 ① 무효 | A §5 | `m0-assert.json` `m0_4_feat_head` · `run.json` `fixed_heads_assert` | 장치 단언(기능 머리 = `16e88c01` · 시작 · 끝 2회) · 무효 판단 = HQA · PM | HQA 회신 대기 |
| R3 | `/before` 가 실패를 재현 못 한 항목은 「대조 무효」 | A §5 · §3-1 | `verdict-device.json` `items.I-6.contrast` · `items.I-7.contrast` · `items.N-1.labels` | 판정 글자 「대조 무효」(정본) · N-1 은 D-1 ⓒ 로 A2 화면 · I-1 ~ I-5 는 정본에 실패 정의 없음 → 판독값 기록 | HQA 회신 대기 |
| R4 | 키보드 탭 도달 | A §3-2 | `I-1` · 표본 `readings` · 칸 `afterUnreachable` | 판독값 기록 · 판정 = HQA(정본에 기대 글자 없음) | HQA 회신 대기 |
| R5 | Enter/Space(길게 누름 포함) | A §3-2 | `I-2` | 판독값 기록 · 판정 = HQA | HQA 회신 대기 |
| R6 | 덮임(창 높이 330/200 · 아래쪽 줄) | A §3-2 | `I-3` · `shots/I-3.*.cover.png` | 판독값 기록 · 판정 = HQA | HQA 회신 대기 |
| R7 | Esc(마우스로 연 뒤 · 키보드로 연 뒤 · 닫힌 뒤 초점) | A §3-2 | `I-4` | 판독값 기록 · 판정 = HQA | HQA 회신 대기 |
| R8 | 열린 채 Tab/Shift+Tab | A §3-2 | `I-5` | 판독값 기록 · 판정 = HQA | HQA 회신 대기 |
| R9 | A 줄 열고 Tab 으로 B 줄 → Esc/엔터(두 목록 동시 열림 0) | A §3-2 | `I-6` `afterMismatchExpected` · `beforeReproduced` | 기대 글자 「두 목록 동시 열림 0」(A §3-2 · `readingText.I-6`) | HQA 회신 대기 |
| R10 | T3 | A §3-2 | 없음(`plan.notMeasured` I-8) | **미측정** · 정의 후보 있음 · HQA 확인 대기(`hqaPending.T3`) | HQA 회신 대기 |
| R11 | 지연 0/150/300 | A §3-2 | `run-items.js` 지연 단 · 표본 `proxy.args` | 위 항목별 · 단위 = ~~HQA 회신 대기~~ 방향당 · 개발팀 사전 고정 · HQA 집행 단위(`hqaPending.delayUnit` · 고침6 11/16) | ~~HQA 회신 대기~~ 개발팀 사전 고정(장전 전 공개 · 고침6 11/16) |
| R12 | Chromium 외 WebKit/Firefox 는 재거나 「미측정」 | A §3-2 · B §6 | 없음(`plan.notMeasured`) | **미측정** | HQA 회신 대기 |
| R13 | F10 — 열린 목록 밑 누름으로 바뀌는 것은 연 줄뿐 · 다른 줄 0 | A §3-3 | `I-7` `changedOtherRows` | 기대 글자 「연 줄뿐 · 다른 줄 0」(`readingText.I-7.expectedF10`) | HQA 회신 대기 |
| R14 | 열린 채 다른 줄 위 바깥 누름 → 행 클릭 0 | A §5(§4 C5) | `I-7` `rowclickDelta` | 기대 글자 「행 클릭 0」(`readingText.I-7.expected`) | HQA 회신 대기 |
| R15 | F6 = 미해결 실패로 따로(F6 트랙 고침 뒤 다시) | A §3-3 | 없음 | **대상 밖**(관문 ① 장치 범위 밖 · 별 트랙) | HQA 회신 대기 |
| R16 | F9 = HQA 판정 | A §3-3 | 없음 | **입력 안 함**(장치 칸 없음 · 판정 = HQA) | HQA 회신 대기 |
| R17 | HQA 가 못 띄우면 원인 회신 → 시험 앱만 고쳐 1회 재발행 → 대표 결정(미회신 기한 24시간) | A §5 | `m4-boot.json`(기동 기록만) | **대상 밖**(절차 · PM) | HQA 회신 대기 |
| R18 | 코드 원문 대조 3줄(A1 덮임 원문 추적 · V2 토글 길 1개 · Esc 원인 문장) | A §5 · §3-4 | 없음 | **입력 안 함**(원문 대조 · 장치가 재지 않음) | HQA 회신 대기 |
| R19 | 관문 ② 운영 허용 동작 규칙(배포 뒤 · 대표 실물 앞) | A §5 | 없음 | **대상 밖**(관문 ② · 운영 접촉 0) | HQA 회신 대기 |
| R20 | 실제 머리 · 옆줄 겹침 · §3-6 두 칸(고른 뒤 목록 새로 그린 다음 위치 · 응답 기다리는 사이 다른 줄 열기) | A §5 · §3-6 | 없음 | **미측정**(대표 실물 표) | HQA 회신 대기 |
| R21 | 서버 · 인가 · 데이터 길 | A §5 | 없음 | **대상 밖** | HQA 회신 대기 |
| R22 | N-1 화살표 빠른 연타(지연 0 · 150 · 300 · 500 · Chromium 필수) | B §6 | `N-1` · `items.N-1`(`contrastScreen` · `labels` · `counts` · `byDelay` · `beforeRecordOnly`) | 기대 글자 B §6 그대로(`readingText.N-1`) · 대조 = D-1 ⓒ(§7) | HQA 회신 대기 |
| R23 | N-2 되끌린 단추 Enter 두 번(관찰) | B §6 | `N-2` · `items.N-2.pickDeltaByScreen` | 「관찰 칸 — 합불 기준 아님」 · 기록만(`readingText.N-2`) · a2 칸 0 | HQA 회신 대기 |
| R24 | 11/7 §5 항목 · 조건 · 기대값 그대로 | B §6 | R1 ~ R21 | 위 행 그대로 | HQA 회신 대기 |
| R25 | 판정자 = HQA · 회신 원문 보존 · 공개 재게시 사본은 경로 · 회로 ID 가림 | B §6 | `judgeSchema` · `mask.js`(러너 경로 · 긴 토큰) · `verdict-device.json` `marker` | 판정 칸에 회로 ID 원문 0(§7) · 회신 원문 보존 · 재게시 = **대상 밖**(PM) | HQA 회신 대기 |

## 9. 정본 32행 ↔ I/N 대응표 (고침5 H3 · §8 확장 · 2026-11-16)

> 근거: 고침5 작업지시서 §9-1(PM 결재 §3-PM 16:40) · [1-V] V2 표 1(정본 32행 · §8 25행 짝 · 1:1 아닌 4행). §8 R1~R25 는 지우지 않고 그대로 두며, 이 절이 빈 행(#24 · #25 · #26 · #31 · #32)을 채우고 1:1 아닌 행(#3 · #9 · #13 · #15)을 표시한다.
> 출처 SHA 고정: **A** = `16e88c01:docs/검증/외부/20261107_4X대상확인_S4_3인.md` · **B** = `16e88c01:docs/검증/외부/20261113_4X대상재결정_S4_A프라임_3인.md` · 줄 번호(L) = 그 나무의 파일 줄.
> 기대 글자 칸 = 정본 원문 그대로 인용(바꿔 쓰기 0). 정본에 기대 글자가 없으면 「정본 기대 글자 없음」 이라고만 적는다. `/before` 실패 꼴 칸 = 작업지시서 §7 H1-4 표 그대로 · 정본 인용을 못 찾은 칸 = **「대조 기준 미정」**(「대조 무효」 와 다른 글자).
> 노출 칸 = §10 번호(노1~노11) 또는 「노출 0」. 판정 칸 상태 = 측정 · 미측정(사유) · 대상 밖 · 입력 안 함 · **매핑 안 됨 = 합격 처리 불가**(HQA 회신 Q-A 글자).
> 끝 열 이름 = 「HQA 회신 · 사전 고정 상태」(옛 「HQA S1~S4」 · 결-4 甲 · 결-5). 값은 rules `hqaPending` 이 정본이고 H1 이 채운다 — 이 표는 칸 이름만 적는다.

| # | 정본 행 · 글자 | 출처 | 장치 칸 | 1:1 | 기대 글자(원문 그대로) | `/before` 실패 꼴 · 출처 | 노출 | 판정 칸 상태 | §8 짝 |
|---|---|---|---|---|---|---|---|---|---|
| 1 | 「blob 3개 = PR #850 머리 대조 뒤 시작」 | A §5 · §4 끝 단락 L70 · §3 L37 | M0 `m0-assert.json` `m0_5_blobs` | ⭕ (B #24 로 4개 · 대체) | 정본 기대 글자 없음 — 장치 단언(blob 같음) | 해당 없음(전제 행) | 해당 없음(항목 칸 아님) | 측정(장치 단언) | R1 |
| 2 | 「새 코드 커밋이 오면 무효」 | A L70 · L37 | M0 `m0_4_feat_head` · `run.json` `fixed_heads_assert` | ⭕ | 정본 기대 글자 없음 — 장치 단언(기능 머리 `16e88c01`) · 무효 판단 = HQA · PM | 해당 없음(전제 행) | 해당 없음 | 측정(장치 단언) | R2 |
| 3 | 「/before 가 실패를 재현 못 한 항목은 「대조 무효」」 · 「같은 조건에서」 | A L70 · L37 · §2 검증팀장 1 L21 | 지금 `items.I-6.contrast` · `items.I-7.contrast` · `items.N-1.labels` → 고침5 H1-3 전 항목(I-1~I-7 · N-1) `contrast{state, reason, cells[]}`(옛 이름 · 작업지시서 글자) → **코드 칸(고침5-B)**: `items.<항목>.contrastState{state, reason, cells[]}` · `d1Reproduced` · `notMeasured` · `attempted` · `deviceFailure` · I-6 · I-7 `beforeFailRepro` — 대응 = §9-3 | △ → ⭕ 인지 HQA 판단 (「옛 글자」: ~~❌ → H1-3 · H1-4 합류 뒤 ⭕ 인지 [4] 확인~~) | 「대조 무효」(A L37 · L70) · 「통과로 안 셈」(B §6 L164 · N-1) | 항목별 = 이 표 #4 ~ #11 · #15 · #18 · #27 칸 | 해당 없음(규칙 행) | 측정(H1 뒤) · 🟡 §8 R3 「I-1 ~ I-5 는 정본에 실패 정의 없음」 은 I-1 · I-3 · I-4 에 틀림(V2 F-2) → 이 행이 대체 | R3 |
| 4 | 「키보드 탭 도달」 | A §3 L38 | I-1 · tab-walk (「옛 글자」: ~~I-1 · tab-walk · before/after · 0/150/300~~) | ⭕ (「옛 글자」: ~~⭕(자극) · 대조 = H1-3 뒤~~ · 값커밋 c-V5 [4] 2회차 P3-C = v3 L127 글자) | 정본 기대 글자 없음 → 「판독값 기록 · 판정 = HQA」 | H1-4 = 「D1 키보드 미도달」(A §3-4 · 11/7 작업지시서 §7-2 M6) · 🟡 부록 I 쟁점 1 | 노1 · 노2 | 측정 | R4 |
| 5 | 「Enter/Space(길게 누름 포함)」 | A L38 | I-2 · enter-short · space-short · enter-long · space-long | ⭕ | 정본 기대 글자 없음 | **대조 기준 미정** — A · B 에 Enter/Space 의 실패 꼴 글자 0(A L41 「스페이스 길게 누름 · 엔터 반복 0」 = 관문 ② 허용 동작 · 고친 꼴 설명) · 11/7 작업지시서 §7-2 M7 「열리지 않음」 = 정본 밖 · V0 를 `/before` 기대로 쓰지 않음(작업지시서 S12) | 노3 · 노7 | 측정 · 대조 = 기준 미정(안건 ③ 재료) | R5 |
| 6 | 「덮임(창 높이 330/200 · 아래쪽 줄)」 | A L38 | I-3 · bottom-row · 창 1280×330 · 1280×200 | ⭕(자극) | 정본 기대 글자 없음(「A1 덮임 원문 추적(옆 자리 겹침 0)」 은 코드 원문 대조 줄 L40) | H1-4 = 「덮임」(A §2 검증팀장 1 L21 「/before 덮임이 실제로 나는 창 높이·줄 위치를 먼저 고정」) | 노출 0 | 측정 | R6 |
| 7 | 「Esc(마우스로 연 뒤 …)」 | A L38 | I-4 · mouse-open | ⭕ | 정본 기대 글자 없음 | H1-4 = 「Esc 안 먹음」(A §3-2) · 글자 근거 줄 = A §3-4 L40 Esc 원인 문장 · 고침6 11/16: Esc = 열림 관측 → 700ms → Esc · 메뉴 안 닫힘 = 열림 남음 판독값 | 노출 0 | 측정 | R7 |
| 8 | 「Esc(… 키보드로 연 뒤 …)」 | A L38 | I-4 · kbd-open | ⭕ | 정본 기대 글자 없음 | #7 과 같음 · 고침6 11/16: 대조 화면 e1t3 · Esc 뒤 3초 polling · 메뉴 0 못 봄 = 장치 실패 · 여는 키 = Enter 만(결-8 4꼴 = 장전 시간 추정 80% 경계로 보류 · Space 미측정) | 노4 · 노7 · 노12(고침6) | 측정 | R7 |
| 9 | 「Esc(… 닫힌 뒤 초점)」 | A L38 | I-4 두 경우의 읽기 → 고침5 H1-8 `focusAfterClose`(두 경우 각각) | △ → H1-8 합류 뒤 ⭕ 인지 [4] 확인 | 정본 기대 글자 없음 | H1-4 에 따로 없음 — #7 · #8 따름 · 고침6 11/16: 초점 = 메뉴 0 뒤 3초 안 한 번이라도 참(지금 식 · HQA 원문 식 따로 기록) | 노4 · 노7 · 노12(kbd-open 만) | 측정(H1-8 뒤 독립 칸) | R7 |
| 10 | 「열린 채 Tab/Shift+Tab」 | A L38 | I-5 · tab · shift-tab | ⭕ | 정본 기대 글자 없음 | **대조 기준 미정** — A §2 검증팀장 3 L23 「(Esc 외 키 다시 그림 → 초점 끌어감 §12-3 ⑤)」 은 실패 꼴 글자이나 그 출처(11/7 작업지시서 §12-3 ⑤)는 고친 꼴(E1)의 위험 판정 → `/before` 실패 꼴이라는 글자 0 · §3 합의 L38 · 대표 확정 단락 L70 엔 괄호 없음 · 🟡 부록 I 쟁점 2 | 노6 · 노7 | 측정 · 대조 = 기준 미정(안건 ③ 재료) | R8 |
| 11 | 「A 줄 열고 Tab 으로 B 줄 → Esc/엔터(두 목록 동시 열림 0)」 | A L38 | I-6 · esc · enter | ⭕ | 「두 목록 동시 열림 0」(A L38) | H1-4 = 「두 목록 동시 열림」 · 🟡 출처 A L23 = #10 과 같은 줄 · 고친 꼴 위험 문맥(V2 #11) → 부록 I 쟁점 2 · 고침6 11/16: I-6 Esc = HQA 기다림 절차 없음 · 그대로(결-7) | 노6 · 노7 | 측정 | R9 |
| 12 | 「T3」 | A L38 · B §6 L167 | I-8 · blank-rows (「옛 글자」: ~~I-8 — 없음(`plan.notMeasured` I-8)~~) | ❌ · HQA Q-H2 조건부 수용(6921942407) · 승격은 HQA 판단 뒤 | **정본 정의 0** → 대표 결정 ⓑ L306 글자(장전 전 공개 v3 §6-3) | — | 노16 (「옛 글자」: ~~노출 0~~) | **매핑 안 됨 = 합격 처리 불가** · v2 · before 칸 = 기록만 (「옛 글자」: ~~정본 정의 0 · 대표 안건 ① · 상태 = 대표 결정 대기~~) | R10 |
| 13 | 「지연 0/150/300」 | A L38 · L24 | I-9 = `run-items.js` 지연 단(조건 행 · 항목 아님) | △ — 조건 행 · 단위 정본에 없음(~~결-2 乙 왕복 ms~~ 고침6 11/16: 방향당 ms · HQA 집행 단위 · 乙 철회 · 「정본 단위 없음 · 개발팀 사전 고정」) | 정본 기대 글자 없음 | 해당 없음(조건 행) | 지연 0 단 = 노1 ~ 노7 · e1t3 kbd-open 0 · 150 · 300 = 노12(고침6) | 조건(위 항목별 측정) | R11 |
| 14 | 「Chromium 외 WebKit/Firefox 는 재거나 「미측정」」 | A L38 · L24 · B §6 L164 | 없음(`plan.notMeasured`) | ⭕(미측정 명기) | 「재거나 「미측정」 명기」(A L24) | 해당 없음 | 노출 0 | 미측정(정본이 허용한 글자) | R12 |
| 15 | 「F10 기대값 고정: 열린 목록 밑 누름으로 바뀌는 것은 연 줄뿐 · 다른 줄 0」 | A §3-3 L39 | I-7 · below-list (「옛 글자」: ~~I-7 `changedOtherRows` + (결-10 甲 · 조건부) F10 전용 경우 1~~ · 값커밋 c-V5 [4] 2회차 P3-C = v3 L138 글자 · 옛 글자의 결-10 = 고침5 결-10) | △ → ⭕ 인지 HQA 판단(F10 전용 경우가 남으면 ⭕ · 빼고 표시만 하면 △ + 합격 처리 불가) (「옛 글자」: ~~△ → 결-10 甲 합류 뒤 ⭕ · 乙 로 되돌리면 △ 유지 + 「합격 처리 불가」~~ · 옛 글자의 결-10 = 고침5 결-10) | 「바뀌는 것은 **연 줄뿐 · 다른 줄 0**」(A L39) | H1-4 = 「행 클릭 · 다른 줄 변화」 | 노8(참고 · 칸 결과 아님) | 측정(고침5 결-10 조건부) | R13 |
| 16 | 「F6 = 미해결 실패로 따로(F6 트랙 고침 뒤 다시)」 | A L39 | 없음 | 대상 밖 | — | — | — | 대상 밖 | R15 |
| 17 | 「F9 = HQA 판정」 | A L39 | 없음 | 입력 안 함 | 「F9 = HQA 판정」 | — | — | 입력 안 함(F9 정의는 A 안에 없음 · 미측정) | R16 |
| 18 | 「열린 채 다른 줄 위 바깥 누름 → 행 클릭 0」 | A §4 C5 L64 · L70 | I-7 · row-cell `rowclickDelta` | ⭕ | 「행 클릭 0」(A L64 · L70) | H1-4 = 「행 클릭 · 다른 줄 변화」 | 노8(참고 · 칸 결과 아님) | 측정 | R14 |
| 19 | 「HQA 가 못 띄우면 원인 회신 → 시험 앱만 고쳐 1회 재발행 → 그래도 안 되면 대표 결정」 | A L70 | `m4-boot.json`(기동 기록만) | 대상 밖 | — | — | — | 대상 밖(절차) | R17 |
| 20 | 「코드 원문 대조 3줄」 | A §3-4 L40 · L70 | 없음 | 장치 밖 | — | — | — | 입력 안 함(원문 대조 · HQA) | R18 |
| 21 | 「관문 ② … 규칙만 결재」 | A §3 L41 · L70 | 없음 | 대상 밖 | — | — | — | 대상 밖(관문 ②) | R19 |
| 22 | 「실제 머리 · 옆줄 겹침과 §3-6 두 칸은 미측정으로 대표 실물 표」 | A L42 · L70 | 없음 | 미측정 | — | — | — | 미측정(대표 실물 표) | R20 |
| 23 | 「서버 · 인가 · 데이터 길은 대상 밖」 | A L70 | 없음 | 대상 밖 | — | — | — | 대상 밖 | R21 |
| 24 | 「blob 4개(razor · css · 새 메뉴 파일(namespace 까지 글자 그대로) · 어휘 파일 무변) = A′ 판 머리 대조 뒤 시작」 | B §4 끝 단락 L148 · §3 L91 · §5 L156 | M0 `m0_5_blobs` **10쌍**(1~4 정본 · 5~10 정본 밖 표시) — 쌍 1~6 = 옛 글자 그대로 · 쌍 7~10 = H4-G 값(`m0-assert.sh` 주석 그대로: E1T3 칸 ↔ `e456e4f6` 원 경로 · `e456e4f6` 원 CSS ↔ F″ css/after · V2 칸 ↔ `74a76c58` 원 경로 · `74a76c58` 원 CSS ↔ F″ css/after) · M0 ③b(값커밋 결-13 · 이 표 아래 줄) (「옛 글자」: ~~M0 `m0_5_blobs` 6쌍 — 쌍 1~4 = 정본 4개(기능 머리 `16e88c01`): After razor `9c358a00` · css `0e922200` · `CsStatusMenu.cs` `1aa0fc6a` · `CsVocab.cs` `0ec65e70` / 쌍 5~6 = A2 razor `49d7502d` · A2 css `0e922200`(`2a6a0ee1` · 정본 밖 · 대표 11/16 N-1 대조판)~~) | ⭕ + 정본 밖 6쌍 표시 (「옛 글자」: ~~⭕(정본 4개 = 6쌍 안 빠짐 0) + 정본 밖 2쌍 표시~~) | 정본 기대 글자 없음 — 장치 단언(blob 같음) | 해당 없음(전제 행) | 해당 없음 | 측정 — 리허설 4 원자료 `b02aad72` `m0-assert.json` 6쌍 `match: true` | R1 에 「4 → 6」 만 · 이 행이 채움 |
| 25 | 「코드 원문 대조는 11/7 3줄을 A′ 판 머리 기준으로 다시 걸고, 4줄째(A′)로 …」 | B L148 · §3 L92~97 | 없음 | 장치 밖 | — | — | — | 입력 안 함(원문 대조 · HQA) | 빈칸 → 이 행 |
| 26 | 「11/7 뒤 새 코드 커밋 5개는 대조 줄 · 항목 대응표로 빈 곳 0 을 보인다」 | B L148 · §1-1 L11 · §4 X-2 L136 | 문서 대응(장치 칸 아님) — 아래 §9-2 커밋 5행 | ⭕(커밋 5 = 5행) · diff 줄 단위 = 미측정 | — | — | — | 판정 칸 아님 · 대응 = §9-2 | 없음 → §9-2 |
| 27 | 「N-1 화살표 빠른 연타 … 지연 0 · 150 · 300 · 500 단 · Chromium 필수」 | B §6 L164 | N-1 · arrows(**방향당**) · **a2**(300 · 500 · 정본 밖 · Q4ⓑ 조건부) · before 칸 = 기록만 (「옛 글자」: ~~N-1 · arrows · after/before/a2 · 0/150/300/500(a2 = 300 · 500)~~) | ⭕(대조판 A2 = 정본 밖 · 대표 11/16 · HQA Q4ⓑ 조건부) | 「회로 삶 · 목록 닫힘 · 초점 = 같은 줄 상태 단추 · 다른 줄 변화 0」 · 판정 글자 B L164 그대로 | H1-4 = B §6 「회로 죽음(재연결 표시)이 1회 이상 재현」 | 노5 · 노7 · 노9 | 측정 | R22 |
| 28 | 「N-2 되끌린 단추 Enter 두 번」 | B §6 L165 | N-2 · enter-twice | ⭕ | 「**관찰 칸 — 합불 기준 아님.** 미리 적은 예상: 「첫 항목 골라짐(9.3 기본 길)」」 | 「기록만」(B L165) | 노출 0 | 측정(관찰) | R23 |
| 29 | 「11/7 §5 항목 · 조건 … 그대로다. 기대값도 11/7 문서 그대로다.」 | B §6 L167 | #1 ~ #23 | ⭕ | 위 행 그대로 | 위 행 그대로 | 위 행 그대로 | 위 행 따름 | R24 |
| 30 | 「판정자 = HQA. PM · 개발팀은 판정을 바꾸지 않는다. 회신 원문은 보존하고, 공개 재게시 사본은 경로 · 회로 ID 를 가린다.」 | B §6 L168 | `judgeSchema` · `mask.js` · `verdict-device.json` `marker` | ⭕ | 원문 그대로(왼쪽) | — | — | 측정 아님(규칙) | R25 |
| 31 | 「관문 ② 는 11/7 규칙 그대로에 「되끌린 단추에서도 엔터 반복 · 항목 누름 0 · 콘솔 상세 오류 글자 복사 0」 을 더한다」 | B L148 · §3 L98 | 없음 | 대상 밖 | — | — | — | 대상 밖(관문 ②) | R19 = A 만 → 이 행 |
| 32 | 「측정 장치 · 분석기 … 대상 밖」 | B L148 · §3 L99 | 없음(표시 행) | 표시 | — | — | — | 표시 — 11/16 대표 결정(「개발팀 장치가 재고, HQA 가 원자료와 장치 원문을 대조해 판정」 · PR #850 댓글 6902200730 §1)과의 관계 = 미측정 · 관문 ① 범위 밖 | 없음 → 이 행 |

**행 수 확인**: 정본 32행 = 표 32행 · 빈 칸(「장치 칸」 · 「1:1」 · 「판정 칸 상태」 중 비어 있는 칸) 0 · 「매핑 안 됨 = 합격 처리 불가」 1행(#12 T3) · 「대조 기준 미정」 2행(#5 I-2 · #10 I-5).

- 값커밋(V4-1~V4-8 · 결-9 乙 · PM 결재 11/17): #3 · #4 · #12 · #15 · #24 · #27 · §9-1 I-8 = 장전 전 공개 v3 §3 글자로 맞춤 · 바꾼 칸마다 「옛 글자」 취소선 보존. △(#3 · #9 · #13 · #15) · ❌(#12) 승격 0.
- M0 ③b(값커밋 결-13 甲 · 작업지시서 §9): M0 ③ 허용 목록 = 측정 워크플로 · `tools/hqa-s4-measure/` + 비밀 검사 4경로(`.github/workflows/trufflehog.yml` · `.github/scripts/trufflehog/range.sh` · `scan.sh` · `self-test.sh` · 정확히 · 와일드카드 0). 새 단언 `m0_3b_secret_blobs` = ~~그 4경로 중 시험 앱 고정 머리 → HEAD diff 에 있는 경로마다 blob = 고정값(main `b0305286` 판) · 4경로가 diff 에 없으면 해당 없음~~(옛 글자 · 결-15 로 대체) → **값커밋 결-15(작업지시서 §9 · [3-V] 2회차 P3-1 · P3-2)**: 4경로 **모두** 그 나무에 있어야 하고(diff 포함 여부와 무관) 경로마다 모드 `100644` · 종류 blob · blob = 고정값(main `b0305286` 판)이어야 초록 — 4파일 없음 · 모드 다름 · blob 다름 = 빨강(대표 결-1 ⓐ 기계 강제). `--diff-only` 셋째 인자가 `-` 로 시작하면 usage 로 멈춤(P3-3). 「M0 blob 10쌍」(`m0_5_blobs`) 셈과 따로.
- M0 ③ · ③b 동작 시험 위치(값커밋 결-18 · 작업지시서 §9): 대조군 2 · 심은 위반 6 은 `tools/hqa-s4-measure/selftest-m0.sh` 로 잰다 — **측정 워크플로는 이 파일을 부르지 않는다**(사람 · 검증자 재실행 · 끝줄 원문은 개발명세 · 검증 리뷰서). 측정 런 안의 판독기 자체시험(`selftest-judge.js`)은 c-V3 판 바이트로 되돌렸다(자식 실행 자리 = 고침7 T34 고정값 13 · c-V4~c-V5c 의 결-13 블록 · 결-15~17 허용 검사는 측정 런 실행 면을 넓혀 되돌림 — 커밋은 남김). 측정 런마다 ③b 는 M0 가 실제 나무로 잰다(`m0-assert.json` `m0_3b_secret_blobs`).
- **v5 의미 변경**(별건 A K5 · 대표 결재 기록 §18(11/18 15:3x) 「둘 다 넣음」 · T9 · T10 「더함」 · 설계 §5-5 ⓕ): 바로 위 줄의 「측정 워크플로는 이 파일을 부르지 않는다」 는 **v4 글자 그대로 보존**한다 — v5 부터 측정 워크플로 ⑤ 단계 `'[장치] 자체시험 — M0 ③ · ③b'`(id `stm0` · 앞 단면 `snap` 바로 뒤 · M1 앞)가 `selftest-m0.sh` 를 **런마다 부른다**. 심은 위반 8(T9 모드 120000 · T10 4경로 자리 나무 040000 더함) · 끝줄 `통과 10/10 · 심은 위반 8/8 · 대조군 2/2` · 결과 = 끝줄 + rc 만 원자료 `negtest/neg-selftest-m0.txt` · ⑤ 빨강이면 판독 · 판정 단계 0(§12-3). `selftest-judge.js` 는 무변(결-13 자리 그대로 · 허용 검사 약속 0) · 경계 = ⑤ 단계 바이트는 장치 커밋으로 고정 · M2 앞.

### 9-1. 장치 칸 → 정본 행 (거꾸로 · 매핑 안 된 장치 칸이 없는지)

| 장치 칸 | 정본 행 # | 비고 |
|---|---|---|
| I-1 | 4 | |
| I-2 | 5 | 대조 기준 미정 |
| I-3 | 6 | |
| I-4 | 7 · 8 · 9 | #9 = H1-8 뒤 독립 칸 · 고침6 11/16: Esc 기다림 700(열림 관측 기준) · 메뉴 0 뒤 3초 polling · 두 식 기록 · 여는 키 = Enter 만(결-8 보류) |
| I-5 | 10 | 대조 기준 미정 |
| I-6 | 11 | |
| I-7 | 15 · 18 | 한 자극이 두 행 겸함 → 고침5 결-10 甲 이면 F10 전용 경우 1 추가 (「옛 글자」: ~~한 자극이 두 행 겸함 → 결-10 甲 이면 F10 전용 경우 1 추가~~ · 값커밋 결-16 = 출처 표기) |
| I-8 | 12 | **매핑 안 됨 = 합격 처리 불가** · HQA Q-H2 조건부 수용(6921942407) · 승격은 HQA 판단 뒤 (「옛 글자」: ~~(정본 정의 0 · 대표 안건 ①)~~) |
| I-9 | 13 | 조건 행 |
| N-1 | 27 | 대조판 A2 = 정본 밖(대표 11/16) |
| N-2 | 28 | 관찰 칸 |
| WebKit/Firefox | 14 | 미측정 |

- HQA 회신 글자 「장치 I-1~I-6 · N-1 · N-2」 보다 장치 칸이 넓다(I-7 · I-8 · I-9 · V2 F-8). 이 표는 장치 칸 전부를 행으로 둔다 — I-7 은 정본 #15 · #18 에 매핑된다.

### 9-2. 11/7 뒤 새 코드 커밋 5개 ↔ 대조 줄 · 관문 ① 항목 (정본 #26)

> 범위: B §1-1 L11 「11/7 확정 뒤 새 코드 커밋은 5개다. F12 Esc · F13 · F12 A2 · F14 1차 · F14 2차 A′(`1a86ca1f`)」. 뽑은 명령(읽기만): `git log 16e88c01 -- <CsStatusCell.razor · CsStatusMenu.cs · backoffice-dashboard.css · CsVocab.cs>` 에서 11/7 대조 머리(T3 고침 `e456e4f6` · 11/7 19:22 · razor blob `644e27eb` = A L37 대조값과 같음 확인) 뒤의 코드 커밋. 대응 기준 = 커밋 제목 · 바뀐 파일 · B §3 4줄째 글자 · A L40 3줄 글자.

| # | 커밋 | 제목 요지 | 바뀐 파일(src) | 대조 줄 | 관문 ① 항목 |
|---|---|---|---|---|---|
| C1 | `b77795cb` (11/8) | F12 Esc 닫기를 `CloseAllMenusAsync` 로 — 키보드로 연 틈의 Esc 뒤 초점이 단추로 | `CsStatusCell.razor` (+7 −1) | 11/7 3줄째 「Esc 원인 문장」(같은 자리 · B X-2 재기준 대상) | I-4(Esc · 닫힌 뒤 초점) |
| C2 | `5c17aa78` (11/8) | F13 키보드로 연 직후 틈의 다시 누름 · Tab 뒤 초점을 단추로 | `CsStatusCell.razor` (+36) | 11/7 2줄째 「V2 토글 길 1개」 | I-2 · I-5 |
| C3 | `2a6a0ee1` (11/8) | F12 A2 — Esc 를 목록이 열렸을 때만 닫기 | `CsStatusCell.razor` (+4 −1) | 11/7 3줄째 「Esc 원인 문장」 | I-4 · N-1 대조판 A2 의 출처 |
| C4 | `1f451132` (11/10) | F14 1차 — 전용 `CsStatusMenu` · 사라진 요소 초점 `JSException` 기록 | `CsStatusCell.razor` (+32 −4) · `CsStatusMenu.cs` (+28) | 4줄째 (a0)(B L93 「F14 1차 `OnAfterRenderAsync` 의 `JSException` 전부 잡기」) | N-1 |
| C5 | `1a86ca1f` (11/10) | F14 2차 A′ — 이벤트 입구(`IHandleEvent`) 재구현 · ↑↓ 키 초점 `JSException` 만 | `CsStatusMenu.cs` (+63 −1) | 4줄째 (a)(b)(c)(B L94~96) | N-1 · N-2 |

- 5행 모두 대조 줄 1개 이상 · 항목 1개 이상 → 커밋 단위 빈 곳 0. **diff 더한 줄 단위 대조(「어디에도 안 걸리는 더한 줄 0」 · B X-2) = 미측정** — 안 걸리는 줄이 나오면 줄을 더하지 않고 3인에 돌린다(B X-2 글자).

### 9-3. 판독 칸 이름 대응 — 옛 이름 · 설계 글자 → 코드 칸 (고침5-B ② · PM 결정 11/16 · [4] 7)

> 공개 묶음 독자는 아래 「코드 칸」 을 읽는다. 왼쪽 옛 이름 · 설계 글자는 지우지 않고 「옛 이름」 으로 남긴다(헌법 #1). 판정 기대 글자 변경 0.

| 옛 이름 · 설계 글자 | 코드 칸(`verdict-device.json` 등) | 만드는 곳 | 뜻 |
|---|---|---|---|
| `contrast{state, reason, cells[]}`(§9 #3 · 작업지시서 H1-3) | `items.<항목>.contrastState{state, reason, cells[]}` | `judge.js` fourCells | 성립 · 대조 무효 · 대조 기준 미정 — 대조 칸이 장치 실패면 state null · 사유 「대조 불가 — …」 |
| `items.I-6.contrast` · `items.I-7.contrast` · `reading` · `labels` · `deviceFail` | 그대로(옛 칸 · 정본 아님 · `readingText.fix5.legacyFields`) | `judge.js` | 옛 꼴 유지 |
| (D1 재현 관찰) | `items.<항목>.d1Reproduced{count, of, text}` | `judge.js` fourCells | `/before` 키보드 도달 0 표본 수 / 받은 표본 수 — 관찰 사실 |
| `notMeasured{flag, reason}` | `items.<항목>.notMeasured{flag, reason}` | `judge.js` fourCells | **고침5-B ⓐ**: 그 항목 칸 전부 시도 기록 0 · 표본 0 일 때만(HQA Q2) |
| (시도 근거 · 고침5-B ⓐ 새 칸) | `items.<항목>.attempted{records, cells, of, text}` · 칸 `cells[].attemptRecords` · `totals.attemptRecords` · `totals.attemptOutOfPlan` · 원자료 `samples.jsonl` 의 `kind: "attempt"` 줄 | `run-items.js`(칸마다 표본보다 먼저 한 줄 · order 없음) → `judge.js`(표본에서 뗌) | 시도 있음 + 표본 0 = 장치 실패만(미측정 표지 0) · 시도 0 = 미측정 · 계획 밖 시도 줄 = 빨강 |
| `deviceFailure{flag, reasons[], cells[]}` | `items.<항목>.deviceFailure{flag, reasons, cells}` | `judge.js` fourCells | 장치 실패 칸 · 사유 — 칸 `excluded.deviceFailure`(표본 수)와 같은 글자 · 다른 뜻 |
| `beforeFailureReproduced`(작업지시서 H1-3) | `items.I-6.beforeFailRepro` · `items.I-7.beforeFailRepro{count, text}` | `judge.js` | 항목 실패 재현 수(옛 「재현됨」 · D1 재현과 다른 뜻) |
| (H1-7 제외 판독) | `/after` 칸 `cells[].exclusionReview{count, ofGot, sameShapeAsD1, reading}` · 머리 `hiddenDefectSuspect` | `judge.js` | 제외 표본이 결함을 숨기는지 판독 도움 · 판정 = HQA |
| `totals.planned`(옛 · 칸 수) | `totals.plannedSamples` · `gotSamples` · `missingSamples` · `excluded{afterUnreachable · deviceFailure · outOfPlan · maskConflict · maskedRead}` · `withheldFiles` · 칸 `missing` · `excluded` | `judge.js` | 분모 · 제외 수(H1-1) |
| `run.json` `round_line.round` · `red`(옛: verdict 보류 · 없음이면 null) | **고침5-B ⓒ**: 장전 · verdict 없음 → `round{state: 「판정 불가/미통과」, reasons: [보류 또는 없음 글자], red: true}` · `red: true` · `round_from` / **ⓑ** `verdict_file` · `verdict_sha256` · `verdict_holds` | `boot.sh round` | 분모 · 제외 수 · 은폐 판독은 verdict 안(SHA 목록 안) — run.json 은 verdict 파일 이름 · SHA 만 가리킴 · 리허설은 null 그대로 |
| `openCountBeforeEsc`(옛 이름 · 그대로) | `cells` 표본 `readings.openCountBeforeEsc` | `items/i-items.js` i4 | **J1-B 정정**: 뜻 = 「열린 메뉴 관측 시점」 의 열린 목록 수(Esc 직전 아님 · 이름만 Before) |
| (J1-B 새 칸) | `readings.openCountAtEsc` · `readings.holdReads` | `items/i-items.js` i4 | Esc 직전 마지막 읽기의 열린 목록 수(≥ 1) · 700ms 기다리는 동안 간격 50ms 로 읽은 수 — 그 사이 0 을 보면 표본 = 장치 실패(`deviceReading.i4OpenNotHeld`) |
| (J1-C 뜻 바뀜) | `readings.holdReads` · `readings.openCountAtEsc` | `items/i-items.js` i4 | **J1-C**: 기다림 중 읽기 0 → `holdReads` = 1(Esc 직전 한 번) · `openCountAtEsc` = 그 한 번 읽기 값(0 이면 표본 = 장치 실패 · Esc 누름 0) · 기다림 중 순간 닫힘은 관측 안 함(공개) — rules `device.i4._hold_표지_J1C` |
| 「Esc 뒤 끝 상태」 · `maxOpenAfterEsc`(3초 끝 값으로 읽힐 수 있던 글자) | `readings.openCountAfterEsc` · `focusAfterEsc` · `hqaFocusRowEnd` · `maxOpenAfterEsc` · `final` | `items/_lib.js` pollAfterEsc | **J1-B 정정([4] ④)**: 「멈춘 시각 값」 — 두 식이 모두 참이면 3초 전에 멈춤 · 아니면 3초 끝 · 메뉴 0 못 봄이면 메뉴 0 상한 끝. `maxOpenAfterEsc` = Esc 뒤 첫 읽기부터 멈춘 시각까지의 최댓값. 이름 · 판정 무변 |
| (J1-B 판독기) | `redReasons` 의 `deviceReading.i4EscWaitMissing` · `i4FocusNotBool` · 표본 `device_failure` | `judge.js` | I-4 칸이 계획에 있는데 머리 `escWait` 없음 · 값 깨짐 = 장치 빨강 · I-4/kbd-open 표본의 `focusOnSameRowBtn` 이 참/거짓 아님 = 그 표본 device_failure(실패 꼴 `=== false` 로 통과로 새는 길 막음) |

## 10. 노출 칸 목록 (고침5 H3 §9-2 · 장전 전 공개 · 판정 · 표본 · 기대값 변경 0)

> 원천(SHA 고정 · 읽기만): 리허설 2 = 런 43748603465 · 원자료 커밋 `0c76c101` `hqa-raw/43748603465-1/probe.json` · 리허설 3 = 런 43753935152 · `f2362cca` `hqa-raw/43753935152-1/probe.json` · 리허설 4 = 런 43812697138 · `b02aad72` `hqa-raw/43812697138-1/probe.json` · PM 질의 PR #850 댓글 6902200730 §2 · Q4ⓒ.
> 칸 꼴 = 항목 · 화면 · 지연 · 경우(창은 전부 1280×720). 「노출 내용」 은 값이 아니라 「무엇이 알려졌나」 다. 이 표의 글자는 rules `exposure[]`(H1-6) 와 한 글자도 다르지 않아야 한다 — 맞춤은 합류 때 PM · [4].
> 🟢 **고침5-B ①(PM 결정 11/16 · [4] 7)**: 맞춤 끝 — rules `exposure[]` 각 행의 `no` · `shapeText` · `rawSource` · `exposedText` · `q4cList` = 아래 표 칸 글자 그대로(`selftest-judge.js` 가 이 표를 읽어 대조). 칸 꼴 하나 = rules 한 행(노6 = 2행 · 노7 = 5행). 노6(I-5 · I-6 `/after`) · 노7 N-1 `/before` 행 더함 · 노4 = `/after` 로 바로잡음(옛 행 = `exposure_옛행_고침5B` · 판독기 안 읽음). 노8 · 노10 · 노11 = 항목 칸이 아님 → rules `exposureNonCell`(판독기 표지 0 · 글자 대조만). rules 의 옛 `source` · `content` = 옛 이름(짧은 출처 · 말 · verdict `exposed.source` 에 씀).

| 번호 | 칸 꼴(항목 · 화면 · 지연 · 경우) | 출처(원자료 필드) | 노출 내용 | 댓글 Q4ⓒ 목록 |
|---|---|---|---|---|
| 노1 | I-1 · before · 0 · tab-walk | 리허설 2 `screens[0].tab_first_row`(reached false · 오류 「Tab 40회 안에 1번 줄 상태 단추에 못 감」) · 리허설 3 · 4 `screens[0].r1` · `r2` · `k_check`(K-결함 「(나) /before 결함 D1 재현」) | `/before` 지연 0 에서 Tab 으로 1번 줄 상태 단추에 닿지 못함(D1) · 1번 줄 안 초점 받을 보이는 요소 0 | 있음(I-1 `/before`) |
| 노2 | I-1 · after · 0 · tab-walk | 리허설 2 `screens[1].tab_first_row`(reached true · tabs 7) | `/after` 지연 0 에서 Tab 으로 1번 줄 상태 단추에 닿음 · 닿은 Tab 수 | 있음(I-1 `/after`) |
| 노3 | I-2 · after · 0 · enter-short | 리허설 2 `screens[1].open_list_after_enter`(found true) | `/after` 지연 0 에서 1번 줄 상태 단추 Enter 뒤 목록 열림 | 있음(I-2 `/after` Enter 뒤 열림) |
| 노4 | I-4 · after · 0 · kbd-open | 리허설 2 `screens[1].open_list_after_enter` | 키보드로 열기 단계(Tab → Enter → 열림)만 · Esc 뒤 결과 노출 0 · **화면 = after**(리허설 2 `/before` 의 같은 필드 = null · 시도 0) | 있음(I-4 키보드 첫 동작 · 댓글엔 화면 표기 없음) |
| 노5 | N-1 · after · 0 · arrows | 리허설 2 `screens[1].open_list_after_enter` | 키보드로 열기 단계만 · 연타 뒤 결과 노출 0 | 있음(N-1 첫 동작) |
| 노6 | I-5 · after · 0 · tab · shift-tab / I-6 · after · 0 · esc · enter | 리허설 2 `screens[1].open_list_after_enter` | 같은 꼴의 열기 단계(1번 줄 · Tab → Enter → 열림)만 · 그 뒤 키 결과 노출 0 | **목록 밖**(같은 필드가 이 칸의 첫 동작과 같음 · 새로 셈) |
| 노7 | I-2 · before · 0 · 4경우 / I-4 · before · 0 · kbd-open / I-5 · before · 0 · tab · shift-tab / I-6 · before · 0 · esc · enter / N-1 · before · 0 · arrows | 리허설 2 `screens[0].tab_first_row`(reached false) · 리허설 3 · 4 `k_check` | `/before` 지연 0 키보드 미도달 → 장치는 키를 누르지 않음(자극 미실행) → 이 칸 결과가 사실상 정해짐 | **목록 밖**(V2 F-7 은 리허설 3 부터로 셈 · 원자료는 **리허설 2 에서 이미** 미도달을 공개) |
| 노8 | I-7 · before · 0 · (목록 닫힌 채 · 칸 꼴 밖) | 리허설 3 · 4 `screens[0].r3`(clicked true · before 0 → after 1) | 목록 닫힌 채 행 누름 1회에 행 클릭 셈이 1 오름(양성 대조) · 열린 목록 상태가 아니므로 I-7 칸(열린 채 B 줄 누름) 결과 아님 | 목록 밖(참고) |
| 노9 | N-1 · a2 · 0 · (구조 읽기 · 칸 꼴 밖 — a2 칸은 300 · 500 뿐) | 리허설 4 `a2_check`(focusableVisible 1 · variant a2 · css after) · `screens[2].r1` | A2 화면 1번 줄에 초점 받을 보이는 요소 1(단추) · 입력 0 | 목록 밖 |
| 노10 | (항목 칸 아님) after 첫 화면 | 리허설 2 · 3 · 4 `shots/probe.after.first.png` · `png_check` | `/after` 첫 화면 그림(입력 0) | 목록 밖(항목 결과 아님) |
| 노11 | (항목 칸 아님) 선택자 존재 | 리허설 2 · 3 · 4 `screens[].selectors`(화면마다 11) | README 선택자 · 상태 칩 · 재연결 표시의 존재 · 찾은 ms | 목록 밖(항목 결과 아님) |
| 노12 | I-4 · e1t3 · 0 · kbd-open / I-4 · e1t3 · 150 · kbd-open / I-4 · e1t3 · 300 · kbd-open | 11/8 근본원인 §2 묶음 A(지금 판 · 시험 앱 `8e7f3208` After = e1t3 · 열림 뒤 700ms · Enter/Space 4꼴 × 3 · 공개 나무 `16e88c01`) — 칸별 알려진 결과: 방향당 0 🟢 12/12 · 150 🟢 12/12 · 300 🔴 0/12 끝 초점 BODY · HQA #850 댓글 6709803008(방향당 300 · 4표본) | e1t3(= `8e7f3208` After) 키보드로 연 뒤 Esc → 초점 복원 — 방향당 0 · 150 = 복원(12/12 · 12/12) · 방향당 300 = 복원 실패(0/12) · HQA 11/7 방향당 300 4표본 실패 — 결과가 알려짐 | **목록 밖**(Q4ⓒ 독립성 제약 — e1t3 대조가 노출 칸 하나로 설 수 있음) |
| 노13 | (항목 칸 아님) a2 · 방향당 0 · 150 · 300 · 500 · 마우스로 연 뒤 Esc | 11/10 내부 F14 측정(Windows · Edge · `16e88c01` `20261110_1V_F14…` §3 · 11/10 리뷰서 L13) | A2 화면에서 마우스로 연 뒤 Esc 결과(개발팀 내부) · N-1 칸(화살표 연타)의 결과는 아님 | 목록 밖(참고 · 같은 화면 · 다른 행동) |
| 노14 | (항목 칸 아님 · 참고) I-4 · after · 방향당 0 · 150 · 300 · kbd-open 의 가족 글자 | 11/8 수정안 A 사본(`644e27eb`+1줄 · 레포 미커밋) HQA 절차 측정 · 기다림 훑기 · `9c358a00`(A′) 글자 대조 | /after kbd-open(I-4 · 방향당 0·150·300) — 같은 칸 노출 없음. 가족 참고: 11/8 수정안 A 사본(`644e27eb`+1줄 · 레포 미커밋) HQA 절차 36/36 🟢 · 기다림 훑기(방향당 300 · 0~1800) 24/24 🟢. `9c358a00`(A′)은 A2 가드 · F13 · F14 를 더한 다른 글자. A′ 자체의 HQA 700ms 칸 측정 = 찾지 못함 | 목록 밖(참고 · 같은 칸 노출 없음 · 가족 글자 결과) |
| 노15 | I-4 · before · mouse-open | 11/7 리뷰서 L51 | V0 마우스로 연 뒤 Esc 무반응(지연 표기 없음) | 목록 밖(6902200730 Q4ⓒ 5칸 = 리허설 2 지연 0 칸 · 이 행 없음) |
| 노16 | I-8 · v2 · blank-rows(공백 꼴) | 11/7 리뷰서 L151 | V2 공백 3칸 숨김 · 0×0 · 탭 정지 0(지연 조건 표기 없음) | 목록 밖(6902200730 Q4ⓒ 5칸 = 리허설 2 지연 0 칸 · 이 행 없음) |
| 노17 | (참고 · 계획 칸 밖) V2 · e456e4f6 판 마우스 Esc | 11/7 리뷰서 L143 · L218 | V2 무반응(「0/150」 · 원문에 단위 표기 없음 · 그 회차(2회차) 장치 = 11/7 리뷰서 L109 `proxy2.js` 150/300 · L210 『편도 75/150 = 왕복 150/300』 → 방향당 75) · e456e4f6 마우스 Esc 5/5 닫힘 | 목록 밖(참고 · 6902200730 Q4ⓒ 5칸 = 리허설 2 지연 0 칸 · 이 행 없음) |
| 노18 | (참고 · 계획 칸 밖) e456e4f6 판 T3 공백 · 빈 · null | 11/7 리뷰서 L235~L242 | 세 꼴 모두 단추 1 · 「-」 · 탭 정지 1 — **v2 화면 결과가 아님**(I-8 칸 꼴 아님) | 목록 밖(참고 · 6902200730 Q4ⓒ 5칸 = 리허설 2 지연 0 칸 · 이 행 없음) |
| 노19 | (참고) e456e4f6 판 키보드로 열기 → Esc | 11/7 리뷰서 L233(3회차 T2 · 장치 L210 「편도 75/150」) | 같은 줄 단추로 돌아옴(방향당 0 · 75 · 150) · Esc 기다림 절차 표기 없음 — 노12 칸과 화면 · 경우가 같고 절차가 다른 기록 | 목록 밖(참고 · 6902200730 Q4ⓒ 5칸 = 리허설 2 지연 0 칸 · 이 행 없음) |
| 노20 | (참고 · 계획 칸 밖) `b77795cb` | 11/8 F12 고침 A 리뷰서 · A2 머리 리뷰서 | 닫힌 상태 Esc 뒤 초점 되끌림 · 틈 안 Tab 5/6 | 목록 밖(참고 · 6902200730 Q4ⓒ 5칸 = 리허설 2 지연 0 칸 · 이 행 없음) |

- 노출 칸 표본 수는 다른 칸과 같다(결-1 · 「노출 결과에 맞춘 표본 조정 금지」). 노출 칸을 판정에 어떻게 쓸지(독립성 대책)는 대표 안건 ② — 이 표는 사실 목록이다.
- ~~지연 150 · 300 · 500 칸 · 창 330/200 칸 · I-3 · I-4 mouse-open · I-7 row-cell · N-2 = 노출 0(위 원천 3곳 기준).~~ (고침6 11/16 정정 — 노12 가 e1t3 kbd-open 150 · 300 칸을 덮음)
- ~~지연 150 · 300 · 500 칸(노12 의 e1t3 kbd-open 150 · 300 빼고) · 창 330/200 칸 · I-3 · I-4 mouse-open · I-7 row-cell · N-2 = 노출 0(위 원천 3곳 + 11/8 근본원인 · HQA 11/7 기준).~~ (값커밋 c-V3 정정 — 노15 가 I-4 mouse-open /before 칸 · 노16 이 I-8 v2 칸을 덮음 · 둘 다 지연 표기 없음 = 모든 지연)
- I-4 mouse-open /before = 노15 · I-8 v2 blank-rows = 노16(모든 지연). 그 밖 지연 150 · 300 · 500 칸(노12 의 e1t3 kbd-open 150 · 300 · 노15 · 노16 빼고) · 창 330/200 칸 · I-3 · I-4 mouse-open /after · I-7 row-cell · N-2 = 노출 0(위 원천 3곳 + 11/8 근본원인 · HQA 11/7 기준).
- 고침6 11/16(J1-6 · 결-9): 노12 = rules `exposure` 3행(지연 0 · 150 · 300 · 여는 키 칸 없음 = 모든 여는 키) · 노13 = rules `exposureNonCell`. `88085da1` `/after` 가 11/8 수정안 A′ blob 인지(U4)는 [4] 확인 뒤 공개 묶음 v3 에 반영 — 이 표엔 아직 안 넣음.
- J1-B 11/17([4] ① · 판정 · 표본 · 기대값 무변): 노12 출처 글자 「§2 A」 → 「11/8 근본원인 §2 묶음 A(지금 판)」 + 칸별 알려진 결과(rules `exposure` 노12 행마다 `knownResult` · 옛 글자 `_source_옛값_J1B` · `_rawSource_옛값_J1B`). 노14 = `/after` kbd-open 참고 행(rules `exposureNonCell` · 판독기 표지 0 · 같은 칸 노출 없음 · 가족 글자 결과만).
- 값커밋 c-V3(PM 결재 11/17 · 결-4 甲 · 결-8 · 결-7 재결재 甲): 노15 · 노16 = rules `exposure` 행(지연 칸 없음 = 모든 지연) · 노17~노20 = rules `exposureNonCell` 참고 행(판독기 표지 0 · 글자 대조만) · 글자 = 장전 전 공개 v3 §6-1 행 그대로. Q4ⓒ 칸 = PR #850 댓글 6902200730 Q4ⓒ 괄호 5칸(I-1 `/before` · `/after` · I-2 `/after` Enter 뒤 열림 · I-4 키보드 첫 동작 · N-1 첫 동작 = 리허설 2 지연 0 칸)과 대조 — 노15~노20 은 그 5칸에 없음 = 목록 밖.
- 노9: 맞는 계획 칸 0 · 표지 0 · 참고 성격(v3 L259) — 옮기지 않음(값 커밋 결-7 재결재)

## 11. HQA 회신 Q-H1~Q-H13 상태 (값커밋 V4-9 · 결-10 甲 · PM 결재 11/17)

> 원문 = PR #850 댓글 6921942407(HQA 공식 회신 · 장전 전 공개 v3 대상). 조건 칸 = PM 대응 §2 줄 그대로 · 불수락 4건 = 3인 → CTO 판정 · 대표 결재(자료 모으는 회차). rules 무변 · 판독 무영향 — 이 표는 상태 글자만 적는다.
> 불수락 4건(Q-H4 · Q-H10 · Q-H11 · Q-H13)에 수락 꼴 글자 0. 조건부 9건도 수락 아님 — 적힌 조건 범위 안에서만.

| Q-H | 상태 | 조건 · 결정 |
|---|---|---|
| Q-H1 | 조건부 수용(적힌 조건 범위 안에서만) · 댓글 6921942407 | I-4 양 경우 모두 성립일 때만 항목 성립 · 한쪽 장치 실패/무효면 성립 금지(장치 `fourCells` 그대로). |
| Q-H2 | 조건부 수용(적힌 조건 범위 안에서만) · 댓글 6921942407 | 빈 문자열 · null 의 After 기대는 삭제 안 됨 — 「명시적 미판정/관찰 항목」 으로 남김(정상 간주 금지). |
| Q-H3 | 조건부 수용(적힌 조건 범위 안에서만) · 댓글 6921942407 | E1T3 = I-4 kbd-open 만 · V2 = I-8 T3 만 · A2 = N-1 만 · 수용 ≠ 대조 성립 · 합격. |
| Q-H4 | HQA 불수락 · 대표 결재 · 자료 모으는 회차 | 노출 3칸 독립성 — 대표 결재 「가 자료 모으는 회차」 · CTO 판정 H4 甲 |
| Q-H5 | 조건부 수용(적힌 조건 범위 안에서만) · 댓글 6921942407 | N-1 500 원래 단위 미확정 → 「완전 동등」 선언 금지 · `run.json` 양방향 실제 기록으로 검증. |
| Q-H6 | 조건부 수용(적힌 조건 범위 안에서만) · 댓글 6921942407 | 입력 · 관측 시각 · `actualWaitMs` 대조. |
| Q-H7 | 조건부 수용(적힌 조건 범위 안에서만) · 댓글 6921942407 | 50ms = 개발팀 사전 고정 실행 선택값 · 해상도 차이 남김 · 결과 뒤 바꾸면 새 회차. |
| Q-H8 | 조건부 수용(적힌 조건 범위 안에서만) · 댓글 6921942407 | 순간 닫힘 · 직전 읽기~Esc 경합 미감지 → 의심 표본 자동 합격 금지 · HQA 판정. |
| Q-H9 | 조건부 수용(적힌 조건 범위 안에서만) · 댓글 6921942407 | 최초 복원 시각 · 끝 상태 보존(「한 번이라도 복원」 ≠ 최종 유지). |
| Q-H10 | HQA 불수락 · 대표 결재 · 자료 모으는 회차 | Space 미측정 — 대표 결재 「가 자료 모으는 회차」 · CTO 판정 H10 甲 |
| Q-H11 | HQA 불수락 · 대표 결재 · 자료 모으는 회차 | 두 판독 식 실행 동등성 — 대표 결재 「가 자료 모으는 회차」 · CTO 판정 H11 甲 |
| Q-H12 | 조건부 수용(적힌 조건 범위 안에서만) · 댓글 6921942407 | Before/After 같은 관측 창 · 「완전 동등」 주장 금지 · 긴 창이 After 실패를 가리는지 따로 점검. |
| Q-H13 | HQA 불수락 · 대표 결재 · 자료 모으는 회차 | 브라우저 · 창 크기 — 대표 결재 「가 자료 모으는 회차」 · CTO 판정 H13 甲 |

## 12. v5 별건 A — 측정 런 환경 · 공급망 (작업지시서 20261118작 K1~K5 · 설계 `docs/설계/백오피스/20261118_S4_별건A_측정환경공급망_설계.md` §5 · CTO 결-A9)

> v5 바이트가 더한 단계 · 원자료 · 조건만 적는다. 판정 · 표본 · 기대값 · 판독 코드(`judge.js` · `mask.js` · `hqa-gate.js` · `rules.json`) 변경 0.
> 합격 판정은 리허설 원자료 · v5 머리 심은빨강 런 · HQA 재판독 대조 뒤다(설계 §6) — 이 절의 어느 글자도 합격 판정이 아니다.

> (별건 A K2‴ · CTO 결-A10 (A) · 조건 A10-1 · A10-3 · A10-4) 아래 「한계(v5)」 한 단락은 v5 공개 문서 머리(합격 근거 주장보다 앞)의 같은 단락과 바이트가 같다 — push 전 PM 이 두 곳을 대조한다.

> **한계(v5).** 설치 뒤 단언(G1~G6)과 판독 직전 고정값 확인(`pin`)은 같은 실행기 안에서 도는 걸림줄이다. G1~G6 은 앞 단면(⑤ 바로 앞)과 M3 뒤 사이에 생긴 **우발** 변화(환경 변수 · git 설정 · 실행 파일 바이트 · 미리 읽는 라이브러리 설정(`/etc/ld.so.preload`) · 장치 나무(HEAD · 색인 표시 · replace 참조 포함))를 잡고, `pin` 은 M7 직전 판독 코드 4파일의 **디스크 위 바이트**가 워크플로 본문 고정값과 같음을 보인다. 둘 다 그 단계 셸이 깨끗하다는 전제 위에 선다. guard(M3 뒤)부터 `pin` 사이 단계(M4 · 점검 · 항목 · run.json · M8 · M9)는 G 항목 밖이다 — 이 단계들은 고정된 코드만 돌리며, 다음 단계로 넘기는 실행기 환경 파일 쓰기는 M4 의 `HQA_BASE_URL` · `HQA_APP_DIR` 두 이름뿐이다(실행 경로 파일 쓰기 0). 표본 수집(항목 단계)도 같은 「셸이 깨끗하다」 전제 위에 있고, 다시 실행할 수 없어 추적성(잠금 무결성 · 설치 기록 · M3′)으로만 받친다. 같은 실행기에서 코드를 돌릴 수 있는 악의 있는 단계(예: 잡의 어느 단계 코드든 — 실행기 사용자는 암호 없이 관리자 권한을 쓴다 · 잡 끝까지 살아 있는 프로세스)는 앞 단면 · 검사 도구 · 셸 시작 변수(스스로 지우는 꼴 포함) · 실행기 환경 파일 · 저장소 내부(색인 · 개체 저장소 · 손으로 만든 팩 위조 = 미측정)를 함께 바꿀 수 있어 이 확인으로 막지 못하며, 실행 중인 판독기가 바뀌지 않았다는 증명도 아니다. guard 가 PATH 로 찾아 돌리는 외부 명령은 `bash` · `dirname` · `mkdir` · `rm`(G1 전 · G5) · `node`(G1 · 원자료 쓰기) · `git`(G2 · G5) · `cmp`(G2~G4) · `readlink`(G3) · `sha256sum` · `cut`(G3 · G4) · `grep`(G5) 열하나다(guard 전체 · 코드 읽기 셈 · 미측정). 이 가운데 바이트를 확인하는 것은 G3 의 `node` · `git` · `bash` 셋뿐이고 그 셋도 자기 바이트 확인(G3) 전에 실행되며, 나머지 여덟은 확인 대상이 아니다. 잡 안에서 도는 코드는 장치 커밋 · 잠금 무결성 · 기대 해시 · 외부 액션 SHA 로 식별되며, 고정하지 못한 것은 실행기 이미지와 배포판 서명 꾸러미의 설치 스크립트(M2 · 판 + 서명 색인 · 설계 §1-1)뿐이다. 판독기 바이트의 독립성은 이 확인이 아니라 공개 원자료를 설치 없는 다른 환경에서 다시 판독해 대조하는 절차(설계 §1-2)가 진다. 판독 결과는 HQA 재판독이 일치한 회차만 합격 근거로 센다(설계 §1-2).

> 덧말: 허용 장치 이름 3개는 (guard 시점) 경로 값까지 확인한다(그 경로의 파일 바이트는 설치 때 대조 · 뒤 재확인 없음).

### 12-1. 새 원자료 파일 8개 (전부 json · txt · 이름 22자 미만 · 값 · 해시 · 무결성 글자 0)

| 원자료 파일 | 무엇 | 쓰는 자리 |
|---|---|---|
| `m2-installed.txt` | M2 가 새로 깔거나 바꾼 꾸러미 이름 · 판 · 아키텍처(설치 전후 `dpkg-query` 차이) | `boot.sh m2` |
| `m2-apt-history.txt` | 이번 설치 기록 줄(명령 · 꾸러미 목록) · 못 읽으면 이름 붙은 줄 | `boot.sh m2` |
| `m2-env-before.txt` | git 설정 범위 · 출처 · 키(값 = system · global 만) · node · git · bash 판 글자 | `boot.sh m2` |
| `m3-npm-tree.json` | 설치 꾸러미 이름 · 판 · 받은 주소 · 잠금 일치 · 설치 스크립트 칸 · `npm ci` rc · npm 판 · 설치 자리 = 정확히 두 파일(`pw_dir_exact`) · blob 기대값 기준(`dev_ref`) — integrity 글자 0 | `boot.sh m3` |
| `m3-npm.txt` | 설치 출력(작업 폴더 → 원자료) | `boot.sh m3` |
| `m3g-guard.json` | M3′ G1~G6 참거짓 · 첫 빨강 · 바뀐 환경 이름 · git 범위 이름 · 실행 파일 이름 · 나무 바뀐 경로 · 장치 커밋 기준(`dev_ref`) · HEAD = 장치 커밋 · 색인 표시 걸린 경로(이름만) · (별건 A K2‴) 장치 이름 3개 값 = 기대값(`env_values_ok` 참거짓만) | `boot.sh guard`(M3′) |
| `m7-pin.json` | 판독 코드 4파일 같음 참거짓 · 대조군 잡음 · ok · 모드 | 워크플로 `pin` 단계 |
| `negtest/neg-selftest-m0.txt` | ⑤ 끝줄 + rc(음성 시험 데이터 · 본 표본과 폴더 · 이름 분리) | 워크플로 `stm0` 단계 |

- 옛 원자료 `m2-packages.txt`(최상위 24개 이름 · 판)는 그대로 남는다.
- 하위 폴더 `negtest/` 는 M8 가림 · M9 누출 검사 · M10 해시 목록 · 발행 재검이 덮는다(설계 §5-6 C3 원문 대조) — 리허설 첫 런 원자료에서 실측으로 닫는다(안 덮이면 고침 0 · 원자료 뿌리 `neg-` 대안 · C4 런 상한 안).

### 12-2. 새 단계 4 (워크플로 `.github/workflows/hqa-s4-measure.yml`)

| 단계 (id) | 자리 | 조건 | 하는 일 |
|---|---|---|---|
| `'[장치] M3′ 앞 단면'` (`snap`) | 판독기 자체시험 뒤 · ⑤ 바로 앞 | 기본 success() | `boot.sh snap` — 앞 단면 저장(작업 폴더 · 원자료 0) |
| `'[장치] 자체시험 — M0 ③ · ③b'` (`stm0`) | `snap` 뒤 · M1 앞 | `if:` 0 · `continue-on-error` 0 | `selftest-m0.sh` — 끝줄 + rc 만 원자료 |
| `'[장치] M3′ 설치 뒤 단언'` (`guard`) | M3 뒤 · M4 앞 | 기본 success() | `boot.sh guard` — G1~G6 · 빨강이면 M4 이하 건너뜀 |
| `'[판독] M7 직전 판독기 고정값'` (`pin`) | M9 뒤 · M7 바로 앞 | `always()` · M0 성공 · 기록 축 | 판독 코드 4파일 sha256 = 단계 본문 기대값 · 같은 단계 대조군(판독기 사본 끝 1바이트 → 「다름」) · 리허설에서도 돎 · 비교 도구 = 절대 경로(`/usr/bin/sha256sum` · `/usr/bin/cut`) |

- `pin` 기대값 4개 = 워크플로 `pin` 단계 본문 글자(디스크 · 환경 변수에서 안 읽음) · 장치 나무 blob 바이트 sha256 · v5 SHA 묶음에 같은 값 — 이 표에는 다시 적지 않는다(한 곳).

### 12-3. 조건 (CTO 결-A9 K-a · K-b)

- 판독 축 3곳(M7 · M8′ · M9′) `if:` 끝에 ⑤ 성공 글자 — ⑤ 초록 아니면 판독 0. M7 은 `pin` 성공 글자도 더함 → 판독 직전 고정값이 다르면 판독 0 · verdict 없음 → 회차 줄 「판정 불가/미통과」 · 원자료는 남음.
- 기록 축 3곳(run.json 조립 · 회차 줄 · `pin`) `if:` 끝에 ⑤ 「성공 또는 건너뜀」 글자 — ⑤ 자신이 빨강일 때만 멈춘다 · 앞 자체시험 빨강으로 ⑤ 가 건너뛴 회차는 지금처럼 run.json · 회차 줄을 남긴다.
- 을 4곳(M8 · M9 · M10 · 원자료 운반) `if:` = `dcaf4a3f` 바이트 그대로 — M0 빨강 · ⑤ 빨강 회차에도 원자료를 운반한다.
- 점검 · 항목 `if:` 끝에 `guard` 성공 글자(상태 함수 0 · 암묵 success() 유지). `stm0` · M1~M4 · 점검 · 항목에 `always()` 0.
- ⑤ 빨강 회차: 조건 글자상 run.json · `verdict-device.json` · `samples.jsonl` · 회차 줄이 생기지 않는다 — 원자료 목록 정적 확인은 [4] 몫(CTO K-c).

### 12-4. 잠금 · 설치 (자동화 패키지)

- 잠금 = `tools/hqa-s4-measure/pw/package.json`(의존 = playwright 1.62.1 한 줄 · 판 범위 기호 0) · `pw/package-lock.json`(lockfileVersion 3 · 꾸러미 3 · integrity sha512 3/3 · `hasInstallScript` 칸 1 = fsevents 2.3.2 · optional · os darwin).
- `boot.sh m3`: 작업 폴더 `pw/` 비움 → 두 파일 복사 → 장치 커밋 blob 대조 → 폴더 안 = 정확히 두 파일 → 잠금 안 판 = `PW_VERSION` → `npm ci --ignore-scripts --no-audit --no-fund` → 트리 기록 ok 거짓이면 m3 빨강. 옛 설치(`npm install` 판만 고정)는 `boot.sh` 주석으로 남김.
- 장치 커밋 기준 = `GITHUB_SHA`(M0 `head_is_event` 가 창 앞에서 HEAD = `GITHUB_SHA` 를 단언 · G1 허용 밖 이름) · Actions 밖 로컬 시험만 HEAD(원자료 `dev_ref`).
- 시스템 꾸러미(M2 `apt-get`)는 판을 고정하지 않는다 — 설치 기록(§12-1)과 M3′ 설치 뒤 단언으로 **잡는다**(막지 않는다).

### 12-5. M3′ 설치 뒤 단언 G1~G6

- 앞 단면 = `snap`(⑤ 바로 앞) · 뒤 = `guard`(M3 뒤). 순서 G1(+G6 대조군) → G2 → G3 → G4 → G5 · 첫 빨강에서 멈춤 · 앞 단면 없으면 빨강(G 판정 전). (별건 A K2‴) guard 는 저장소 경로를 git 없이 정한다(장치 폴더 두 단계 위 · 물리 경로) — guard 에서 G2 판정 전 git 호출 = G2 자신의 설정 목록 읽기 하나(G1 전 git 0).
- G1 환경 변수 이름별 값 해시 — 바뀌거나 생기거나 사라진 이름 ⊄ 허용이면 빨강. 허용 9 = `HQA_SRC_DIR` · `HQA_CHROME` · `HQA_PW_DIR` + 실행기 단계별 이름 6(`GITHUB_ACTION` · `GITHUB_ENV` · `GITHUB_OUTPUT` · `GITHUB_PATH` · `GITHUB_STATE` · `GITHUB_STEP_SUMMARY` · 잠정 — 설계 §7 Q3 리허설 첫 런 뒤 PM 고정). `PATH` · `BASH_ENV` · `NODE_OPTIONS` · `LD_PRELOAD` · `GIT_*` 허용 0. (별건 A K2‴) 허용 이름 중 장치 이름 3개(`HQA_SRC_DIR` · `HQA_CHROME` · `HQA_PW_DIR`)는 값도 본다 — 뒤 단면 값 = M1 · M3 가 to_env 로 쓴 식 그대로(`boot.sh` `V_SRC_DIR` · `V_CHROME` · `V_PW_DIR` 한 곳 · 경로 값만 · 그 경로의 파일 바이트는 설치 때 대조 · 뒤 재확인 없음) · 다르거나 없으면 G1 빨강 · 원자료 `env_values_ok` 참거짓만. 허용 0 이름이 G1 에서 빨강이 되는 것은 guard 셸이 뜬 뒤에도 바뀐 값이 남아 있는 꼴(숨지 않는 꼴)뿐이다 — 셸 시작 때 스스로 지우는 꼴은 §12 머리 「한계(v5)」.
- (별건 A K2‴ · CTO A11-5) 실행기 몫 6개(`GITHUB_ACTION` · `GITHUB_ENV` · `GITHUB_OUTPUT` · `GITHUB_PATH` · `GITHUB_STATE` · `GITHUB_STEP_SUMMARY`) = 값 무검사 · 단계마다 바뀜 예상(실행기가 단계마다 새로 정하는 이름 · 이름만 허용). 리허설 첫 런 `env_changed` 가 이 6개 밖 이름을 내면 목록 고침 = 새 머리(v5 묶음 재발행 · 리허설 처음부터 · 런 상한 9 안 · CTO 재판정).
- G2 git 설정 출처별 내용 · G3 `node` · `git` · `bash` 실행 파일 바이트 · G4 `/etc/ld.so.preload` 있음 · 내용 — 앞뒤 다르면 빨강.
- G5 장치 폴더 · 측정 워크플로 작업 나무 = 장치 커밋(기준 = §12-4) + HEAD = 장치 커밋 + 색인 표시 전부 `H`(assume-unchanged · skip-worktree 0) — 하나라도 어긋나면 빨강. (별건 A K2″) 여기서 「장치 커밋」 은 replace 참조를 끈 장치 커밋이다 — 장치 커밋을 읽는 git 호출(G5 diff · HEAD · 장치 커밋 확인 · M3 잠금 blob 기대값)은 명령 인자 `--no-replace-objects`(환경 변수로 내보내지 않음 · G1 이 자기 자신을 잡지 않게) + `refs/replace/` 참조 0(하나라도 있으면 빨강 · 원자료 `replace_refs_none` = 참거짓만). (별건 A K2‴) M1 소스 ↔ 나무 대조의 `ls-tree HEAD:…` 도 같은 명령 인자 `--no-replace-objects`. (별건 A K2⁗) G5 의 작업 나무 대조(바뀐 · 지운 · 새 파일)는 장치 커밋에서 새로 만든 임시 색인으로 잰다 — 저장소 색인의 stat · 개체 이름을 믿지 않고 작업 나무 바이트를 장치 커밋 blob 과 다시 대조(임시 색인을 만드는 `read-tree` 도 `--no-replace-objects` · 색인 표시 · HEAD 단언은 저장소 색인 · 참조 그대로). M1 이 읽는 커밋도 `HEAD` 가 아니라 장치 커밋(M3 · G5 와 같은 출처)이다.

### 12-6. 음성 시험 ↔ 본 표본 구분 (HQA 사전협의 회신 6970680614)

- 음성 시험 결과 = `negtest/neg-*` 만. 판독기 입력 = M7 `--samples "$HQA_RAW/samples.jsonl"` 한 경로 → 음성 데이터가 판독에 들어갈 길 0.
- 판독기 · 항목 · 가림 · 누출 검사 소스(`judge.js` · `mask.js` · `hqa-gate.js` · `rules.json` · `run-items.js` · `probe.js` · `leak-scan.js` · `publish.sh`)에 `negtest` · `neg-` 글자 0(v5 머리 실측).
