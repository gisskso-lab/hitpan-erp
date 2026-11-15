# S4 관문 ① 측정 장치 — 항목 대응표 (HQA 가 읽는 자리)

> 근거: 설계 `docs/설계/백오피스/20261116_S4관문1_GitHub서버실측_설계서.md` §4-2 · §4-3 · §4-4 · 작업지시서 §2 B-1~B-4.
> 판정 기대값 정본: `16e88c01:docs/검증/외부/20261113_4X대상재결정_S4_A프라임_3인.md` §6 · 11/7 항목 `16e88c01:docs/검증/외부/20261107_4X대상확인_S4_3인.md` §3 · §4 · §5.
> **판정자 = HQA.** 이 장치가 쓰는 글자는 「장치 판독」 일 뿐 판정이 아니다. 판독 글자는 정본 글자만 쓴다(`rules.json` `readingText`).
> 측정 규칙 · 표본 수 · 간격 · 횟수 · 선택자 · 판독 글자는 **`rules.json` 한 곳**에 있다. 장전 뒤 1글자라도 바뀌면 새 회차다(설계 §6-6).

## 1. 항목 대응표

| # | 정본 항목(출처) | 장치 파일 · 함수 | 장치가 하는 입력(신뢰 입력만) | 장치가 읽는 값 | `/before` 대조 | 장치 판독(`judge.js`) | HQA 「S1~S4」 |
|---|---|---|---|---|---|---|---|
| I-1 | 키보드 탭 도달 (11/7 §3-2) | `items/i-items.js` `I-1` | `#before` 마우스 누름 → Tab × `device.i1.tabs` | Tab 마다 초점 요소(태그 · 역할 · 소속 `[data-row]` · 상태 단추 여부 · 열린 목록 안 여부) · 열린 목록 수 | 같은 입력 기록 · 실패 정의 없음 | 판독값 기록 · 판정 = HQA · (고침4 G5 · L-2) `/after` 키보드 미도달 표본은 유효에서 빼는 규칙 그대로 + 칸마다 `afterUnreachable {count · samples[] · tabPath[]}`(`verdict-device.json` · Tab 경로는 판정 칸 스키마 칸만) = **판독값 기록 · 판정 = HQA** | **HQA 회신 대기** |
| I-2 | Enter/Space(길게 누름 포함) (11/7 §3-2) | `I-2` · 경우 4 = enter-short · space-short · enter-long · space-long | Tab 으로 상태 단추 → 짧게 = press 1회 · 길게 = 같은 키 down 반복(첫 지연 · 횟수 · 간격 = `device.i2.longPress` · 두 번째 down 부터 repeat 표지) → up | 열린 목록 수(끝 · 최대) · `#pick-count` 차이 · `#picks` 새 줄 · 초점 · 바뀐 줄 | 기록 | 판독값 기록 · 판정 = HQA | **HQA 회신 대기** |
| I-3 | 덮임(창 높이 330/200 · 아래쪽 줄) (11/7 §3-2 · 검증팀장 1) | `I-3` · 창 2 = 1280×330 · 1280×200 | 아래쪽 줄(`device.i3.row` = 10 · 첫 쪽 마지막 줄) 상태 칩 locator 누름 | 목록 상자 좌표 · 이웃 줄(`tr`) 좌표 · 겹침 넓이(줄마다 · 합) · 창 밖 잘림 · PNG `shots/I-3.*.cover.png` | 같은 창 · 같은 줄 `/before` 겹침 기록 | 판독값 기록 · 판정 = HQA | **HQA 회신 대기** |
| I-4 | Esc(마우스로 연 뒤 · 키보드로 연 뒤 · 닫힌 뒤 초점) (11/7 §3-2) | `I-4` · 경우 2 = mouse-open · kbd-open | 칩 locator 누름 또는 Tab → Enter 로 열고 → Escape | Esc 앞뒤 열린 목록 수 · 닫힌 뒤 초점 · 같은 줄 상태 단추 여부 | 기록 | 판독값 기록 · 판정 = HQA | **HQA 회신 대기** |
| I-5 | 열린 채 Tab/Shift+Tab (11/7 §3-2) | `I-5` · 경우 2 = tab · shift-tab | Tab → Enter 로 열고 → Tab 또는 Shift+Tab | 키 앞뒤 초점 · 열린 목록 수(끝 · 최대) | 기록 | 판독값 기록 · 판정 = HQA | **HQA 회신 대기** |
| I-6 | A 줄 열고 Tab 으로 B 줄 → Esc/엔터 = 두 목록 동시 열림 0 (11/7 §3-2) | `I-6` · 경우 2 = esc · enter | A 줄(`device.i6.row`) Tab → Enter 로 열고 → 다른 줄 상태 단추에 닿을 때까지 Tab(최대 `device.i6.maxTabs`) → Escape 또는 Enter | 열린 목록 수 **최대값** · B 줄 번호 · 닿은 Tab 수 · 끝 초점 | 2 이상 재현 여부 | 기대값 「두 목록 동시 열림 0」 에 어긋난 `/after` 표본 수 · `/before` 재현 0 이면 「대조 무효」 · 판정 = HQA | **HQA 회신 대기** |
| I-7 | 열린 채 다른 줄 위 바깥 누름 → 행 클릭 0 (11/7 §4 C5) · F10 「연 줄뿐 · 다른 줄 0」 (11/7 §3-3) | `I-7` | A 줄(1) 칩 locator 누름으로 열고 → B 줄(2) 첫 칸 가운데 좌표를 `page.mouse.click` | `#rowclick-count` 차이 · `#rowclicks` · `#detail[data-open]` 앞뒤 · 바뀐 줄 · 다른 줄 바뀜 · 누른 점이 목록 상자 안인지 | 행 클릭 또는 다른 줄 바뀜 재현 여부 | 기대값 「행 클릭 0」 · 「연 줄뿐 · 다른 줄 0」 에 어긋난 `/after` 표본 수 · `/before` 재현 0 이면 「대조 무효」 · 판정 = HQA | **HQA 회신 대기** |
| I-8 | T3 (11/7 §3-2) | 없음 | **정의 후보 있음(11/7 작업지시서 L306 · 공백 상태 칸) · HQA 확인 대기 → 측정 코드 0** | — | — | **미측정 · 정의 후보 있음 · HQA 확인 대기** (회신 대기 ⑥ T3 · 확정 전 장전 거부) | **HQA 회신 대기** |
| I-9 | 지연 0/150/300 (11/7 §3-2) | `run-items.js` 지연 단 | I-1 ~ I-7 을 세 단에서(단마다 프록시 새로 띄움) | 위와 같음 + 프록시 인자(표본 `proxy.args`) | — | 위 항목별 | **HQA 회신 대기** |
| N-1 | 화살표 빠른 연타 · 0/150/300/500 (11/13 §6) | `items/n-items.js` `N-1` | Tab 으로 상태 단추 → Enter 로 열기 → ↓ · ↑ 번갈아 `device.n1.count` 회 · 간격 `device.n1.intervalMs` | 회로 신호 3종 · 목록 닫힘 · 초점 = 같은 줄 상태 단추 여부 · 다른 줄 `data-status-kind`/칩 글자 바뀜 · `#picks` 다른 줄 새 줄 · PNG `shots/N-1.*.n1-end.png` | 300 · 500 단 회로 죽음 재현 여부 · (고침4 G1 · G3 · D-1 ⓒ) 대조 화면 = `rules.n1Contrast`(㉡ = A2 화면 · 화면 이름 `a2` · 주소 `/after-a2` · 기대 data-variant `a2` · 기대 CSS `after` / ㉠ = `/before`) · ㉡ 이면 `/before` N-1 칸은 계속 재되 **「기록만 · 판정에 안 씀」**(`beforeRecordOnly` · 칸 `n1Role`) · a2 미도달 = `/before` 와 같은 U1~U4 · 키보드 도달 0 표본은 재현 셈에서 뺌(사유 「A2 화면 키보드 도달 0」) | 정본 §6 글자 그대로: `/before` 300 · 500 재현 0 → **대조 무효** · `/after` 회로 죽음 또는 다른 줄 변화 1회라도 → **🔴** · 초점이 같은 줄 상태 단추 아님 → **🟡** · 셋 다 아니면 「판독값 기록 · 판정 = HQA」 | **HQA 회신 대기** |
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
| ③ 칸별 표본 수 | 칸마다 몇 번 | 기본 3 · N-1(과 N-2) 5 → 계획 표본 352(고침4 G1: a2 N-1 4칸 × 5 더함 · 옛 값 332 대체) · 이 수가 G4 「정해진 표본 수」 |
| ④ 「지연 N」 단위 | 프록시 방향당 인자인가 왕복인가 | 방향당 인자 = N(왕복 약 2N 추가) · 지연 0 도 프록시 경유(인자 0) |
| ⑤ 헤드리스 셸 · 창 등 조건 | 실행 조건 | 릴리스 크롬 헤드리스(헤드리스 셸 안 씀) · 창 1280×720(I-3 만 높이 330/200) · 배율 1 · ko-KR |
| ⑥ T3 정의 | I-8 T3 가 무엇인지 — 후보: 11/7 작업지시서 L306(공백 상태 칸 → `-`) · 다른 뜻 후보: 11/7 830배포 결과 L23(CS 표시줄) | 없음 · 측정 코드 0(정의 확정 뒤에도 측정은 별도 작업 · 새 회차) |

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

## 4. 리허설 점검(probe) — 고침1 F10 (판정 무관)

- MODE = rehearsal 이고 M4 가 통과한 런에서만 `probe.js` 가 돈다(장전 런 0).
- 남기는 것 = `probe.json` 하나: NODE_PATH 로 playwright 풀림(참/거짓 · 판 · ms) · `/before` · `/after` 마다 README §5 선택자 + 상태 칩 · 재연결 표시의 존재 여부와 찾은 ms · (고침2 N1-a) `/before` 만 · 지연 0 단만: R1 1번 줄 구조(태그 · role · tabindex · display · 사각형 수 · mud-/bo- 클래스만 · 입력 0) — `device.probe.distinguish = true` 인 리허설 3 한 번만 R3 행 클릭 양성 대조 · R2 Tab 경로 · 구분 판정 `k_check`(K-결함 / K-장치 / K-미결 · 글자 = rules.json `kCheck`)를 더한다. 고침1 의 Tab 도달 · 목록 열림 확인 칸은 뺐다(고침2 N2 · 항목 결과와 같은 모양의 정보). 입력 = `#before` 누름 + Tab + 행 클릭 1회 뿐 · `/after` 입력 0.
- 그림 검사 1건(PM 결정): `/after` 첫 화면을 입력 없이 한 장 찍어 F6 검사(PNG 서명 · 텍스트 덩어리 0)만 돌린 결과 = `probe.json` `png_check`(통과/보류 · 덩어리 종류 이름만 · 값 0). 통과면 `shots/probe.after.first.png` 로 원자료에 · 보류면 그림 파일 0. 장전 런 그림이 F6 에 전부 보류되는 위험을 리허설에서 미리 보려는 것.
- 남기지 않는 것 = 항목 판독 · 표본 · 판정 글자 · 회로 신호 · 그 밖의 그림 · 프록시. 항목 결과를 보지 않으므로 설계 §6 「발행 전 고정 · 결과 본 뒤 변경 = 새 회차」 와 충돌하지 않는다. 쓰임은 「선택자가 맞나」 확인뿐.
- (고침4 G10 · S-4 · D-9 ⑦ · D-10) 리허설 4: `/before` R1 · R2 · R3 그대로 · `/after` 칸 무변 · `n1Contrast = a2` 이면 **a2 화면 = R1(입력 0 구조 읽기)만** — 1번 줄 안 `tabIndex ≥ 0` · 사각형 > 0 요소 수(`a2_check.focusableVisible`) · Tab · 키 · 누름 · 선택자 · 그림 · 지연 단 0. 주소는 `plan.screenTable` 값(probe 안 주소 글자 0). 판독: 요소 수 > 0 → 「A2 화면 줄 안 초점 받을 보이는 요소 ≥ 1 → ㉡ 유지」 / 0 → **「되돌림 규칙 해당(㉠)」** (어느 쪽이 나올지는 기대값으로 정하지 않음). 정적 검사 + 심은 위반 11(a2 에 `keyboard.press` · `mouse.click` · `tabTo` · `rowClickControl` · 주소 글자 등) 전부 빨강.

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
| 판정 칸 가림 충돌 · 재는 순서 어긋남 · 순서 기록 없음 · 문맥 순번 어긋남 | 장치 글자(빨강 사유) | `deviceReading` |
| ㉡ 유지 · 되돌림 규칙 해당(㉠) · A2 화면 1번 줄 없음 | 장치 글자(리허설 probe) | `deviceReading.a2Probe*` |

- a2 표본의 판독 · 사유 글자에는 주소 글자(`/after…`)를 쓰지 않는다 — 「A2 화면 …」 으로만(CTO C-4 · 자체시험 `G4-⑪`). 원자료 표본엔 화면 이름(`screen`) · 주소(`screenPath`) · 실측 판(`variantMeasured` · `cssMeasured`)을 따로 적는다(PM 조건 ②).
- **「대조 불가 ≠ 대조 무효」**: 대조 불가 = 그 칸 「장치 실패」(정해진 표본 수 모자람 · 표본 0 · 판정 칸 가림 충돌 · 순서 어긋남) → 대조 자체를 못 함 · 장치 빨강. 대조 무효 = 표본은 정해진 수만큼 있는데 대조 화면이 결함을 재현 못 함 → 정본 판정 글자 · 통과로 안 셈 · 장치 빨강 아님.
- **「장치 글자 ≠ 판정 글자」**: 장치 글자는 장치가 잰 사실이고, 판정 글자는 정본 조건에 맞을 때 장치가 옮겨 적는 정본 글자다. 판정자 = HQA.
- **D-1 ⓒ 읽기 규칙**: 「N-1 에 한해 `/before` 를 A2 화면(화면 이름 `a2` · 주소 `/after-a2`)으로 읽는다 · 11/7 항목의 `/before` 는 운영 판 그대로」 (`readingText.N-1.contrastReadRule` · 판정 글자 무변 · `verdict-device.json` `items.N-1.contrastScreen`). ㉠ 으로 되돌리면 `n1Contrast = before` 이고 이 규칙은 쓰이지 않는다.
- 화면 가르기: 장치 · 판독기는 화면을 `plan.screenTable` 의 **화면 이름 · data-variant** 로만 가른다 — 주소 앞머리 판단 0(PM 결-1 조건 ① · 자체시험 `G4-C-2`).

## 8. 정본 전 행 대응 (고침4 G12 · D-2) — 11/7 §5 + 11/13 §6

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
| R11 | 지연 0/150/300 | A §3-2 | `run-items.js` 지연 단 · 표본 `proxy.args` | 위 항목별 · 단위 = HQA 회신 대기(`hqaPending.delayUnit`) | HQA 회신 대기 |
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
