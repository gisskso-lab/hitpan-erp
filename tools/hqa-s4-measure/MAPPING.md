# S4 관문 ① 측정 장치 — 항목 대응표 (HQA 가 읽는 자리)

> 근거: 설계 `docs/설계/백오피스/20261116_S4관문1_GitHub서버실측_설계서.md` §4-2 · §4-3 · §4-4 · 작업지시서 §2 B-1~B-4.
> 판정 기대값 정본: `16e88c01:docs/검증/외부/20261113_4X대상재결정_S4_A프라임_3인.md` §6 · 11/7 항목 `16e88c01:docs/검증/외부/20261107_4X대상확인_S4_3인.md` §3 · §4 · §5.
> **판정자 = HQA.** 이 장치가 쓰는 글자는 「장치 판독」 일 뿐 판정이 아니다. 판독 글자는 정본 글자만 쓴다(`rules.json` `readingText`).
> 측정 규칙 · 표본 수 · 간격 · 횟수 · 선택자 · 판독 글자는 **`rules.json` 한 곳**에 있다. 장전 뒤 1글자라도 바뀌면 새 회차다(설계 §6-6).

## 1. 항목 대응표

| # | 정본 항목(출처) | 장치 파일 · 함수 | 장치가 하는 입력(신뢰 입력만) | 장치가 읽는 값 | `/before` 대조 | 장치 판독(`judge.js`) | HQA 「S1~S4」 |
|---|---|---|---|---|---|---|---|
| I-1 | 키보드 탭 도달 (11/7 §3-2) | `items/i-items.js` `I-1` | `#before` 마우스 누름 → Tab × `device.i1.tabs` | Tab 마다 초점 요소(태그 · 역할 · 소속 `[data-row]` · 상태 단추 여부 · 열린 목록 안 여부) · 열린 목록 수 | 같은 입력 기록 · 실패 정의 없음 | 판독값 기록 · 판정 = HQA | **HQA 회신 대기** |
| I-2 | Enter/Space(길게 누름 포함) (11/7 §3-2) | `I-2` · 경우 4 = enter-short · space-short · enter-long · space-long | Tab 으로 상태 단추 → 짧게 = press 1회 · 길게 = 같은 키 down 반복(첫 지연 · 횟수 · 간격 = `device.i2.longPress` · 두 번째 down 부터 repeat 표지) → up | 열린 목록 수(끝 · 최대) · `#pick-count` 차이 · `#picks` 새 줄 · 초점 · 바뀐 줄 | 기록 | 판독값 기록 · 판정 = HQA | **HQA 회신 대기** |
| I-3 | 덮임(창 높이 330/200 · 아래쪽 줄) (11/7 §3-2 · 검증팀장 1) | `I-3` · 창 2 = 1280×330 · 1280×200 | 아래쪽 줄(`device.i3.row` = 10 · 첫 쪽 마지막 줄) 상태 칩 locator 누름 | 목록 상자 좌표 · 이웃 줄(`tr`) 좌표 · 겹침 넓이(줄마다 · 합) · 창 밖 잘림 · PNG `shots/I-3.*.cover.png` | 같은 창 · 같은 줄 `/before` 겹침 기록 | 판독값 기록 · 판정 = HQA | **HQA 회신 대기** |
| I-4 | Esc(마우스로 연 뒤 · 키보드로 연 뒤 · 닫힌 뒤 초점) (11/7 §3-2) | `I-4` · 경우 2 = mouse-open · kbd-open | 칩 locator 누름 또는 Tab → Enter 로 열고 → Escape | Esc 앞뒤 열린 목록 수 · 닫힌 뒤 초점 · 같은 줄 상태 단추 여부 | 기록 | 판독값 기록 · 판정 = HQA | **HQA 회신 대기** |
| I-5 | 열린 채 Tab/Shift+Tab (11/7 §3-2) | `I-5` · 경우 2 = tab · shift-tab | Tab → Enter 로 열고 → Tab 또는 Shift+Tab | 키 앞뒤 초점 · 열린 목록 수(끝 · 최대) | 기록 | 판독값 기록 · 판정 = HQA | **HQA 회신 대기** |
| I-6 | A 줄 열고 Tab 으로 B 줄 → Esc/엔터 = 두 목록 동시 열림 0 (11/7 §3-2) | `I-6` · 경우 2 = esc · enter | A 줄(`device.i6.row`) Tab → Enter 로 열고 → 다른 줄 상태 단추에 닿을 때까지 Tab(최대 `device.i6.maxTabs`) → Escape 또는 Enter | 열린 목록 수 **최대값** · B 줄 번호 · 닿은 Tab 수 · 끝 초점 | 2 이상 재현 여부 | 기대값 「두 목록 동시 열림 0」 에 어긋난 `/after` 표본 수 · `/before` 재현 0 이면 「대조 무효」 · 판정 = HQA | **HQA 회신 대기** |
| I-7 | 열린 채 다른 줄 위 바깥 누름 → 행 클릭 0 (11/7 §4 C5) · F10 「연 줄뿐 · 다른 줄 0」 (11/7 §3-3) | `I-7` | A 줄(1) 칩 locator 누름으로 열고 → B 줄(2) 첫 칸 가운데 좌표를 `page.mouse.click` | `#rowclick-count` 차이 · `#rowclicks` · `#detail[data-open]` 앞뒤 · 바뀐 줄 · 다른 줄 바뀜 · 누른 점이 목록 상자 안인지 | 행 클릭 또는 다른 줄 바뀜 재현 여부 | 기대값 「행 클릭 0」 · 「연 줄뿐 · 다른 줄 0」 에 어긋난 `/after` 표본 수 · `/before` 재현 0 이면 「대조 무효」 · 판정 = HQA | **HQA 회신 대기** |
| I-8 | T3 (11/7 §3-2) | 없음 | **정의 미확인 → 장치에 안 넣음** | — | — | **미측정 · 정의 없음** | **HQA 회신 대기** |
| I-9 | 지연 0/150/300 (11/7 §3-2) | `run-items.js` 지연 단 | I-1 ~ I-7 을 세 단에서(단마다 프록시 새로 띄움) | 위와 같음 + 프록시 인자(표본 `proxy.args`) | — | 위 항목별 | **HQA 회신 대기** |
| N-1 | 화살표 빠른 연타 · 0/150/300/500 (11/13 §6) | `items/n-items.js` `N-1` | Tab 으로 상태 단추 → Enter 로 열기 → ↓ · ↑ 번갈아 `device.n1.count` 회 · 간격 `device.n1.intervalMs` | 회로 신호 3종 · 목록 닫힘 · 초점 = 같은 줄 상태 단추 여부 · 다른 줄 `data-status-kind`/칩 글자 바뀜 · `#picks` 다른 줄 새 줄 · PNG `shots/N-1.*.n1-end.png` | 300 · 500 단 회로 죽음 재현 여부 | 정본 §6 글자 그대로: `/before` 300 · 500 재현 0 → **대조 무효** · `/after` 회로 죽음 또는 다른 줄 변화 1회라도 → **🔴** · 초점이 같은 줄 상태 단추 아님 → **🟡** · 셋 다 아니면 「판독값 기록 · 판정 = HQA」 | **HQA 회신 대기** |
| N-2 | 되끌린 단추 Enter 두 번 (11/13 §6 · 관찰) | `N-2`(N-1 같은 문맥 이어서) | N-1 직후 Enter · `device.n2.gapMs` · Enter | `#pick-count` 차이 · `#picks` 새 줄 · 초점 | 기록만 | 관찰 칸 — 합불 기준 아님 · 판독값 기록 · 판정 = HQA | **HQA 회신 대기** |
| — | Chromium 외 WebKit/Firefox | 없음 | 재지 않음 | — | — | **미측정** | — |

## 2. 장치 판독 규칙 (`judge.js` · 설계 §4-3)

- **회로 죽음** = 재연결 표시 요소(`#components-reconnect-modal`) 보임 · 페이지 이동이 아닌 `_blazor` 웹소켓 닫힘 · 회로 종료 꼴 콘솔 오류(`device.circuit.consolePattern`) — 셋을 따로 남기고 하나라도면 죽음. 입력 **전** 신호가 있으면 그 표본은 무효(오류 칸).
- **잰 표본 0 = 빨강**: 계획 칸(항목 × 경우 × 화면 × 지연 × 창)마다 유효 표본 ≥ 1. 0 이면 그 칸 「장치 실패」 + 빨강. 사유 글자는 `/before` 칸이면 「/before 표본 0」, 아니면 「칸 표본 0」 — 「대조 무효」(표본 있음 · 재현 0) 와 따로 적는다.
- 계획 밖 표본 · 읽지 못한 줄 · `samples.jsonl` 없음 · 표본 0 도 빨강.
- 판정 글자(🔴 · 🟡 · 대조 무효)는 N-1 에만, 정본 §6 글자 그대로. 11/7 항목은 기대값 글자가 있는 I-6 · I-7(+F10) 만 「맞음/어긋남 표본 수」 와 대조 무효 여부를 적고, 나머지는 「판독값 기록 · 판정 = HQA」.

## 3. HQA 회신 대기 칸 — 임시 기본값 (정해진 값 아님)

`rules.json` `hqaPending` — status 에 「HQA 회신 대기」 가 하나라도 남아 있으면 `run-items.js` 가 **장전(armed) 측정을 거부**한다(종료 코드 4 · 작업지시서 §9 P3). 리허설은 항목 측정 0 이라 영향 없음.

| 칸 | 회신 대기 내용(#850 댓글 6896955850) | 임시 기본값(장치가 돌게만 둔 값) |
|---|---|---|
| ① S1~S4 | HQA 이름표 ↔ 위 표 행 | 없음(위 표 마지막 칸 비움) |
| ② 원자료 형식 | JSONL · JSON · PNG 안 | `samples.jsonl` · `verdict-device.json` · `shots/*.png`(설계 §5 꼴) |
| ③ 칸별 표본 수 | 칸마다 몇 번 | 기본 3 · N-1(과 N-2) 5 → 계획 표본 332 |
| ④ 「지연 N」 단위 | 프록시 방향당 인자인가 왕복인가 | 방향당 인자 = N(왕복 약 2N 추가) · 지연 0 도 프록시 경유(인자 0) |
| ⑤ 헤드리스 셸 · 창 등 조건 | 실행 조건 | 릴리스 크롬 헤드리스(헤드리스 셸 안 씀) · 창 1280×720(I-3 만 높이 330/200) · 배율 1 · ko-KR |

## 4. 장치 선택값 (정본에 없는 실행 값 — 기대값 아님 · HQA 의심 제기 대상)

| 값 | 어디 | 비고 |
|---|---|---|
| 대상 줄 = 1(I-2 · I-4 · I-5 · I-6 · I-7 A · N-1) · 2(I-7 B) · 10(I-3 아래쪽 줄) | `device.i*` · `device.n1` | 기본 쿼리(rows 24 · 쪽당 10). 1번 = 쪽지 건(상태 5종) |
| 읽기 선택자 `.bo-status-chip` · `.mud-popover.mud-popover-open` · `#components-reconnect-modal` | `device.sel` | **시험 앱 README §5 밖** — MudBlazor 9.3 · blazor 기본 꼴. 나머지 읽기는 README §5 선택자 |
| 상태 단추 판별 = `[data-row]` 안 · `button` 또는 `role=button` · 열린 목록 밖 | `items/_lib.js` `readState` | 고침 전 = 9.3 감싼 div(role=button) · 고친 꼴 = 단추 |
| 길게 누름 = 첫 지연 500ms · 반복 15회 · 간격 33ms | `device.i2.longPress` | 합성 키 반복이 실제 키 반복과 같은지는 HQA 판단(설계 §10) |
| N-1 연타 = ↓ · ↑ 번갈아 24회 · 간격 0 · 연타 뒤 기다림 = 600 + 4×지연 + 1500ms | `device.n1` · `device.settle` | |
| N-2 Enter 사이 120ms | `device.n2` | |
| 회로 연결 기다림 = `_blazor` 웹소켓 첫 프레임 · 그 뒤 600 + 4×지연 ms | `run-items.js` · `device.settle` | 미리 그리기 켬(README §9) — 붙기 전 입력 0 |
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
