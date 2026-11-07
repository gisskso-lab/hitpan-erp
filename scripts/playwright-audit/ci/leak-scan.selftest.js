// G1-3 갈래 H — S10 누출 검사 단독 시험 (DB·API·브라우저 없이 · 가짜 폴더만)
//
// 사용: node leak-scan.selftest.js <빈 작업 폴더>
// ⬛ [낡은 줄 · 1~5차] 경우 7개 — 기대 종료 코드·출력으로 판정(대조군 포함 · 「항상 빨강」/「항상 초록」 아님을 같이 본다).
// 🔴 6차(10/29 · 작업지시서 §0-11-5) — 경우 C1~C11 · 기대 종료 코드·출력으로 판정(대조군 포함 · 「항상 빨강」/「항상 초록」 아님을 같이 본다).
//   C1 깨끗한 폴더 → 0(초록)                         ← 대조군
//   C2 S0 값 하나 비움 → 1 · 「대상 비었음」           ← 보안상무 ⑩
//   C3 원문 누출(main) → 1 · main 지워짐
//   C4 base64 누출(시작 자리 1·2 밀림) → 1
//   C5 URL 인코딩 누출 → 1
//   C6 trace zip 안 resources/ 의 원문 누출 → 1 · trace 지워짐 · main 남음
//   ⬛ [낡은 줄 · 1~5차] C7 심은 값 3꼴(UI_LEAK_PLANT=1) → 1 · 꼴마다 「잡음」 3줄 · 「못 잡음」 0
//   C7 심은 값 4개(UI_LEAK_PLANT=1 · 6차 ⑤ 이름에 심은 값 추가) → 1 · 「잡음」 4줄 · 「못 잡음」 0
//   C8 망가진 zip → 1 · 「못 푼 zip」
//   🔴 6차 — 아래는 lib 을 직접(또는 자식 프로세스로) 잰다. 고치기 전 lib·leak-scan 에 붙이면 빨강이어야 한다(대조군).
//   C9  ① sanitize·describeError 가 알고 있는 값의 3꼴(원문 · URL 대·소문자·+공백 · base64 자리 0·1·2 · url 안전)을 다 지움
//       └ 「원문」 줄은 고치기 전에도 초록 = 대조 줄(이것도 빨강이면 시험이 틀렸다)
//   C9b ① 인코딩 꼴이 자르기 경계(200자)에 걸쳐도 반쪽 0
//   C9c ① 값 한가운데 색 제어 글자 → 지운 뒤 이어 붙은 값 0
//   C10 ② rec 의 err 칸 — 예외 글자는 결과표 err 칸에만 · 콘솔은 「예외=<이름>」 · 콘솔 줄 수·순서 = 결과표
//       └ 「옛 꼴」(got 에 describeError) 줄은 콘솔에 표지가 보여야 한다 = 대조 줄(검사가 볼 수 있음)
//   C11 ⑤ 내용 깨끗한 파일의 이름(하위 폴더명 원문 · 파일명 URL 꼴 · zip 항목명 base64 꼴) → 「이름」 잡힘 · 폴더 지움 · 콘솔에 꼴 0
// 🔴 7차(10/29 · 작업지시서 §0-11-11-5) — 경우 7개 더함(26 → 33). 고치기 전 lib 에 붙이면 C13b 만 초록(대조 줄) · 나머지 6 빨강.
//   C9d  ⓓ 값 안 공백을 줄바꿈+두 칸으로 바꾼 메시지 → 공백 접기 뒤에도 원문 0 · *** 있음
//   C12  M1 maskProbeLines — 꼴 글자를 시험이 따로 계산해 대조(맨 글자 줄 정확히 한 줄씩 · url 대·소문자 둘 · 원문은 [원문] 줄에만 · 대조 값 그대로 · 문맥 줄 4)
//   C13a M3 자식 프로세스 CI 밖 L.mask → 고정 문구 1줄 · 값 0
//   C13b M3 자식 프로세스 GITHUB_ACTIONS=true → `::add-mask::<값>\n` 바이트 그대로 = 대조 줄(빨강이면 시험이 틀렸다)
// ⬛ 7차 원문: //   C14a M4 심은 워크플로 글(비밀값·입력 칸·주소 각 1 + 미끼 통째 주석) → 잡힘 정확히 3 · 줄 번호 맞음 · 출력에 심은 글자 0
//   C14a M4 심은 워크플로 글(비밀값·입력 칸·주소 각 1 + `$(…)` 주소 1 + 미끼 통째 주석 + 맨 변수 `$이름` 1) → 잡힘 정확히 4 · 줄 번호 맞음 · 출력에 심은 글자 0
//       └ V7-F1(10/29 [3-V]): 「`$` 로 시작하면 건너뜀」이 `$(…)`·`${X:-…}` 까지 놓쳤다 → 맨 변수만 건너뜀. 고치기 전 lib 에 붙이면 C14a 빨강(대조군)
//   C14b M4 레포 .github/workflows 지금 그대로 → 대상 1(ui-browser.yml) · 잡힘 0(「항상 빨강」 아님)
//   C14c M4 장치 경로 없는 워크플로만 → 대상 0 = 빨강
// 🔴 8차(10/29 · 작업지시서 §0-12-6 + PM 조건 ②) — 경우 9개 더함(33 → 42). 외부 차단 처리기의 닫힌 창 처리 거절 판별(lib guardRoute).
//   고치기 전 lib 에 붙이면 C15g1 만 초록(대조 줄) · 나머지 8 빨강(함수 없음 → FAIL 로 세고 다음 경우로).
//   C15a 닫힘·continue → 이행 · continue 1 · 창 상태는 거절된 그때 한 번만 읽음   C15b 닫힘·abort → 이행 · abort 1
//   C15c 충돌(창 닫힘 false) → 같은 객체로 거절 · 셈 0                         C15d 다른 거절(Route is already handled!) → 거절 · 셈 0
//   C15e 글자 변형 셋(다른 API 이름 · 고쳐 쓴 꼴 · 같은 줄 끝 덧붙임) → 셋 다 거절 · 셈 0
//   C15f Error 아님(같은 글자 문자열 · undefined) → 거절 · 셈 0 · 판별 함수가 안 터짐
//   C15h 닫힘 + 여러 줄 꼬리(`\nCall log:` …) → 이행 · continue 1 (PM 조건 ② · 첫 줄만 비교)
//   C15g1 자식 프로세스 · 거절을 받는 곳 없이 → unhandledRejection 1 = 대조 줄(빨강이면 시험이 틀렸다)
//   C15g2 자식 프로세스 · 같은 거절을 guardRoute 로 감쌈 → unhandledRejection 0 · 종료 0
//   거절 글자는 Playwright 문구뿐(런 40818528074 결과표 err 칸의 1.60.0 실물) — 자격증명 모양 0.
// 🔴 9차(10/29 · 작업지시서 §0-13-5 + §0-13-PM 조건 ①) — 경우 6개 더함(42 → 48) · C15e 에 V8-R1 2꼴(개수 그대로).
//   C16 = Playwright 1.60.0 원본 순서(F1~F9)를 옮긴 오프라인 모형(브라우저·네트워크 0) · 창 상태는 모형 메시지가 배달된 결과로만 정한다.
//   C16a 대조 줄 · 옛 흐름(열자마자 화면 안 닫기 · lib 안 씀 · 처리기 = continue 그대로) → 서버 차례 S1·S3·S4 셋 다 받는 곳 없는 거절 1
//   C16b 고침 흐름(lib settlePopup · 팝업 하위 요청 2 날던 중 닫음) → 거절 0 · 녹음 1(hitpanChat) · 알림·적재·닫음 참 · 닫기 전 첫 요청 continue 이행
//   C16c 모형 민감도(C16b 와 같되 서버 순서만 실패 응답 → Close) → 거절 ≥1 이 나야 PASS
//   C16d 8차 전제 반증(옛 흐름 + guardRoute · 창 상태 읽기) → S1 다시 던짐 · S3 다시 던짐(읽기 실패) · S4 삼킴
//   C16e PM 조건 ① open 이 던짐(동기·비동기) → 같은 객체로 거절 · 'popup' 처리기 0 · 받는 곳 없는 거절 0 · 상한 안 기다림
//   C16f PM 조건 ① 안 열림(window.open 이 null) → opened 거짓 · 셋 다 거짓 · 처리기 0 · 거절 0 · 상한 안 기다림 · 시계 남김 0
//   고치기 전 lib(1a142dd3) = C16a 만 초록 · 8차 lib = C16a·C16d 초록.
// 🔴 10차(10/30 · 작업지시서 §0-14-5 + §0-14-PM 조건 ①~③) — 경우 16개 더함(48 → 64) · 고치기 전 lib(0b2f9343) 예상 53/64.
//   V9-R2: 받는 곳 없는 거절 그물을 **첫 경우 전**으로 옮겨 전 구간을 센다(C16 각 창 셈은 그대로 · 끝에 C16z).
//   C16g1 open 이 영영 안 끝남(HQA 합성 A) · ms=300 → 상한 예외(단계 `창 열기`) · 걸린 시간 ∈ [299, 400) · 처리기 0 · 시계 원래대로 · 거절 0
//   C16g2 open 이 500ms 뒤 열림 + 늦은 popup → 위와 같음 + 결과 = 거절 1회 그대로 · 늦은 사건 받는 곳 0 · 늦은 팝업 닫기 0
//   C16g3 열렸는데 popup 영영 안 옴(HQA 합성 B) → 상한 예외(단계 `팝업 알림`) = 대조 줄(고치기 전에도 초록)
//   C16g4 알림 왔는데 적재 영영 안 끝남(닫기 이행 · 닫기 거절 두 벌) → 상한 예외(단계 `첫 문서 적재`) · 받은 팝업 close() 1회 · 거절 0
//   C16z 창 밖 받는 곳 없는 거절 = 전체 − 창 안 합 = 0 = 대조 줄
//   C17a 함수 단독 · 이름에 등록값 원문·URL 대·소문자·base64 자리 0/1/2(HQA 합성) → 이름 부분에 꼴 0 · *** 있음
//   C17b 함수 단독 · 이름에 인증 머리글 꼴·점 셋 꼴(실행 중 16진으로 만듦) → 가려짐
//   C17c 함수 단독 · 긴 이름 → 이름 부분 = 앞 40자 + `…`
//   C17d rec 결과표 err 칸 · 이름에 등록값 3꼴 → 가려짐 = 대조 줄 · C17e 같은 자리 · 인증 머리글·점 셋 꼴 → 가려짐
//   C17f rec 콘솔 `예외=` · 이름에 등록값·기호·긴 글자 → `Error` 고정 = 대조 줄
//   C18a 토큰 꼴(HQA 합성 + 띄어쓰기 · 대괄호 두 따옴표 · GITHUB_TOKEN 글자 · toJSON(github)) 한 줄씩 → 줄마다 종류=토큰 · 줄 글자 0
//   C18b 저장소 변수(`vars.` 식 · `vars[...]` · if: 암묵 식) → 종류=저장소 변수
//   C18c 트리거(키 둘 · on: 한 줄 목록) → 종류=트리거 · 머리 `M4 유입 길` · `M4 실값 유입` 머리 0
//   C18d 정상 줄(실물 꼴 · toJSON(github.event) · github.repository·sha · $GITHUB_ENV · 통째 주석 속 낱말) → 잡힘 0 · 통과 줄 1 = 대조 줄
//   C18e 알고 받는 오탐(조건식 속 트리거 낱말 1줄) → 잡힘 정확히 1(트리거)
//   합성 값(인증 머리글 꼴·점 셋 꼴·식 안 이름)은 전부 실행 중 16진·조각 이어 붙이기로 만든다 — 파일 글자에 자격증명 모양 0.
// ⬛ [낡은 줄 · F-PR 첫 꼴] 경우 20개 더함(64 → 84) · 고치기 전 69/84.
// 🔴 F-PR(11/6 · 작업지시서 20261106작 F-PR §6 + [3-V] 01~03 PM 승인) — 경우 22개 더함(64 → 86) · 칸 이름 머리 `F-`(10차 C18a~e 와 겹치지 않게).
//   고치기 전 장치(main)에 붙이면 F-C18d · F-C19b · F-C20c · F-C21c · F-C21d 다섯만 초록(대조 줄) · 나머지 17 빨강 = 69/86.
//   [3-V] 반영: F-C21d 양성 = 장치가 실제로 만드는 약관 고친 뒤 꼴 · F-C20d 에 경계 걸침 입력 · 새 칸 F-C20e(표지 줄 소독) · F-C21i(제어 글자).
//   F-C18a~d N1 K 본문 이름 — 선택(실행기)과 계수(판정기)가 한 규칙 · 경계 꼴은 이상 쪽(사본 폴더 · 함수 단독 · 실행기 --dir)
//   F-C19a~d N2 기록 함수 결과 칸 계약 — 참거짓 아니면 줄 넣기 전에 던짐 · 흐름 밖 종료 코드 ≠ 0 · 흐름 잡이 안 끊김 줄 1
//   F-C20a~d N3 메타 칸 소독 — 콘솔 · 결과표 · 판정기 좋은 줄 · 시드 · 외부 호스트 · 키 칸 상한 경계
//   F-C21a~h 상신-L14 판정기 H 기대 키 틀 — 누락 · 중복 · 모르는 키 · 갈래 · G 빈 자리(양성 두 벌 = 관찰 갈래 · 일반 갈래)
//   H 결과표 생성기는 판정기 상수를 읽지 않고 참고 런 키를 따로 적는다(자기 증명 0).
// 🔴 F-PR 푸시 전 고침(11/6 · [4] P1 + [3-V] 2차 P2 PM 결재) — 경우 3개 더함(86 → 89)
//   F-C20f 판정기 관찰 결과 칸 · 외부 호스트 줄 · 줄 꼴 이상 줄 안 제어 글자 → 줄 갈라짐 0 · 제어 글자 0
//   F-C21j 양성 · 첫 브라우저 늦은 팝업 정상 꼴(같은 키 2줄) → rc 0
//   F-C21k 같은 키 3줄(첫 브라우저) · 둘째 브라우저 2줄 · 둘째 줄 실패 → 셋 다 rc 1
// ⬛ [낡은 줄 · 갈래 A 첫 꼴] 🔴 K 연결 1차(…) — 89 → 102(2차 도우미 3칸 뒤 105) · 88 + 새 14
// 🔴 K 연결 1차(11/6 · 작업지시서 20261106작 #691 재기반 §5-1 · PM C1~C6 · PM 결정 3V-1 ⑤) — 89 → 105(2차 도우미 3칸 뒤 108)
//   F-C21h 「G 자리 비었음」 = ⬛ 낡은 칸(G 자리를 채워 전제가 사라짐 · 뜻은 K-C22b 가 잇는다) → 88 + 새 17(K-C22a~k 11 · K-C24a~f 6).
//   PM 결정 3V-1 — 거르기 규칙 = 닫힌 쪽(전부 문서일 때만 안 돈다) · K-C24e 비ASCII 이름 · K-C24f 이동(실물 git) · K-C22k 기대 키 수 16 고정.
//   장치 · 워크플로 폴더가 바뀌면 스크립트 판단과 무관하게 돈다(워크플로 단계) — 자체시험으로 못 잰다 = 미측정(워크플로 읽기 확인).
//   판정기를 부르는 기존 칸은 G 기대 키 전부를 입력에 더하고 **사본 폴더**(K 본문 꼴 1개)에서 돈다 — 칸 이름 · 기대 결과 그대로.
//   K-C22a~j 판정기 G 기대 키 필수(양성 1 · 음성 · 대조) — 규칙 고치기 전(G 자리만 채운) 판정기에서는 음성 6칸이 빨강(대조)
//   K-C24a~d 거르기 규칙(ui-changes.js · 순수 함수 + 실행 껍데기) — 문서만 → 안 돈다 · 화면 → 돈다 · 장치·워크플로·DDL → 돈다 · 빈 입력·오류 → 돈다
//   G 기대 키 생성기는 판정기 상수를 읽지 않고 작업지시서 §4-2 표에서 따로 적는다(자기 증명 0).
// 🔴 K 연결 2차(11/6 · 갈래 B · PM 결정 4V-1) — 105 → 111(새 6 · 바뀐 칸 3)
//   K-C23a~c K 도우미(kit35.js) 순수 함수 · K-C24g 줄바꿈 든 이름(NUL 로만 나눔) · K-C24h 워크플로 세 지킴 글자 잠금 · K-C22l N4 거짓 끝 줄
//   바뀐 칸: K-C24a · K-C24c(문서 = docs/ 아래만 · 4V-1 ①) · K-C22k(A7 G4 토스트 길 2 → 기대 키 18)
// 🔴 K 연결 3차(11/6 · PM 결정 3V-2 · 4V-2) — 111 → 119(새 8 · 바뀐 칸 1)
//   K-C25a 그림 도우미 6길 · K-C25h 찍은 직후 죽음 → 업로드 자리 0 · K-C25b N4 판정·표지 먼저·강제 거짓 · K-C25c 판정기 끝 줄 둘
//   K-C25d 초기 스크립트 한 덩어리 순서 · K-C25e G4 되돌림 판단 · K-C25f 콘솔 쌓기 · K-C25g 시간 상한
//   바뀐 칸: K-C24h(워크플로 지킴 셋 → 넷 · 업로드 조건 표지 줄)
// 가짜 값은 SELFTEST_ 머리 + 16진(+ 비번 꼴 기호)만 — 자격증명 모양 0.
'use strict';

const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');
const crypto = require('crypto'); // 6차 — C9 가짜 값 16진
const { makeZip, plantValues } = require('./leak-scan');
const L = require('./lib'); // 6차 — C9 는 lib 의 sanitize·describeError 를 직접 잰다

const work = process.argv[2];
if (!work) { console.log('사용: node leak-scan.selftest.js <빈 작업 폴더>'); process.exit(2); }
const SCAN = path.join(__dirname, 'leak-scan.js');

function fakeEnv() {
  return {
    UI_DB_PASSWORD: 'SELFTEST_DBPW_7f3a9c2e',
    UI_JWT_SECRET: 'SELFTEST_JWT_1b8d4e6f',
    UI_ENC_KEY: 'SELFTEST_ENC_9e2c7a1d',
    UI_BOOT_KEY: 'SELFTEST_BOOT_4a6f8b3c',
    UI_PW_PARENT: 'SELFTEST#PWP/5d2e+9a',
    UI_PW_GA: 'SELFTEST_PWGA_3c7e1f8b',
    UI_PW_NA: 'SELFTEST_PWNA_8a1d5c2e',
    UI_SECRET_TOKEN_ACCESS_NA_1_ABC123: 'SELFTEST_TOKEN_e4b7c9a2d1',
  };
}
function fresh(name) {
  const root = path.join(work, name);
  fs.rmSync(root, { recursive: true, force: true });
  fs.mkdirSync(path.join(root, 'main', 'shots'), { recursive: true });
  fs.mkdirSync(path.join(root, 'trace'), { recursive: true });
  fs.writeFileSync(path.join(root, 'main', 'ui-k35-result.json'), '{"rows":[]}');
  fs.writeFileSync(path.join(root, 'main', 'shots', 'a.png'), Buffer.from([0x89, 0x50, 0x4e, 0x47, 1, 2, 3]));
  return root;
}
function run(root, envPatch = {}) {
  const base = { PATH: process.env.PATH, SystemRoot: process.env.SystemRoot };
  const env = { ...base, ...fakeEnv(), ...envPatch };
  for (const [k, v] of Object.entries(env)) if (v === undefined) delete env[k];
  const r = spawnSync(process.execPath, [SCAN, root], { env, encoding: 'utf8' });
  return { code: r.status, out: r.stdout + r.stderr };
}

const results = [];
function check(name, cond, detail) {
  results.push({ name, ok: !!cond });
  console.log(`${cond ? 'PASS' : 'FAIL'} ${name}${detail ? ` — ${detail}` : ''}`);
}
// 🔴 10차 V9-R2 — 받는 곳 없는 거절 그물을 첫 경우 **전**에 건다(전 구간 셈 · 셈만 · 글자 0).
//   urAll = 전체 · urInWin = C16 각 창에서 잰 셈의 합 → 끝의 C16z 가 urAll − urInWin = 0 을 본다.
//   c16UR(9차 창 셈 · 아래 `let`)도 여기서 같이 올린다 — 거절 사건은 맨 위 동기 코드가 다 돈 뒤에야 오므로 그때는 이미 선언돼 있다.
let urAll = 0;
let urInWin = 0;
process.on('unhandledRejection', () => {
  urAll += 1;
  c16UR += 1;
});

const E = fakeEnv();
{ // C1
  const root = fresh('c1');
  const r = run(root);
  check('C1 깨끗한 폴더 = 초록', r.code === 0 && /PASS 누출 0/.test(r.out), `code=${r.code}`);
}
{ // C2
  const root = fresh('c2');
  const r = run(root, { UI_PW_NA: undefined });
  check('C2 S0 값 비면 빨강(⑩)', r.code === 1 && /대상 비었음: UI_PW_NA/.test(r.out), `code=${r.code}`);
}
{ // C3
  const root = fresh('c3');
  fs.writeFileSync(path.join(root, 'main', 'api.log'), `conn pwd=${E.UI_DB_PASSWORD};`);
  const r = run(root);
  check('C3 원문 누출 잡음 · main 지움', r.code === 1 && /UI_DB_PASSWORD · 꼴=raw/.test(r.out) && !fs.existsSync(path.join(root, 'main')), `code=${r.code}`);
}
{ // C4
  const root = fresh('c4');
  const b1 = Buffer.from(`x${E.UI_JWT_SECRET}`).toString('base64');
  const b2 = Buffer.from(`xy${E.UI_ENC_KEY}z`).toString('base64').replace(/\+/g, '-').replace(/\//g, '_');
  fs.writeFileSync(path.join(root, 'main', 'b.txt'), `${b1}\n${b2}`);
  const r = run(root);
  check('C4 base64 누출(자리 1·2 · url 안전) 잡음', r.code === 1 && /UI_JWT_SECRET · 꼴=base64/.test(r.out) && /UI_ENC_KEY · 꼴=base64/.test(r.out), `code=${r.code}`);
}
{ // C5
  const root = fresh('c5');
  fs.writeFileSync(path.join(root, 'main', 'u.txt'), `GET /a?p=${encodeURIComponent(E.UI_PW_PARENT)}`);
  const r = run(root);
  check('C5 URL 인코딩 누출 잡음', r.code === 1 && /UI_PW_PARENT · 꼴=url/.test(r.out) && !/UI_PW_PARENT · 꼴=raw/.test(r.out), `code=${r.code}`);
}
{ // C6
  const root = fresh('c6');
  fs.writeFileSync(path.join(root, 'trace', 't.zip'), makeZip([
    { name: 'trace.trace', data: Buffer.from('{"type":"before"}') },
    { name: 'resources/abc.json', data: Buffer.from(`{"accessToken":"${E.UI_SECRET_TOKEN_ACCESS_NA_1_ABC123}"}`) },
  ]));
  const r = run(root);
  check('C6 trace zip 안 resources 잡음 · trace 만 지움', r.code === 1 && /resources\/abc\.json/.test(r.out)
    && !fs.existsSync(path.join(root, 'trace')) && fs.existsSync(path.join(root, 'main')), `code=${r.code}`);
}
{ // C7
  const root = fresh('c7');
  const r = run(root, { UI_LEAK_PLANT: '1' });
  const caught = (r.out.match(/심은 값 PLANT_[A-Z0-9]+: 꼴 [a-z0-9]+ 잡음/g) || []).length;
  // ⬛ [낡은 줄 · 1~5차] check('C7 심은 값 3꼴 꼴마다 잡음', r.code === 1 && caught === 3 && !/못 잡음/.test(r.out) && /의도된 심은 빨강/.test(r.out), `code=${r.code} 잡음=${caught}`);
  // ⬛ [낡은 줄 · 1~5차] check('C7b 출력에 심은 값 원문 0(가림 줄 제외)', !r.out.split('\n').some((l) => !l.startsWith('::add-mask::') && /FAKE_LEAK_[AB]_|FAKE LEAK\/C\+/.test(l)));
  // 🔴 6차 ⑤ — 심은 값 4개(PLANT_NAME = 내용 깨끗한 파일의 이름 · 꼴 raw · 자리 이름).
  check('C7 심은 값 4개 꼴마다 잡음(이름 포함)', r.code === 1 && caught === 4 && /심은 값 PLANT_NAME: 꼴 raw 잡음 · 자리=이름/.test(r.out)
    && !/못 잡음/.test(r.out) && /의도된 심은 빨강/.test(r.out), `code=${r.code} 잡음=${caught}`);
  check('C7b 출력에 심은 값 원문 0(가림 줄 제외)', !r.out.split('\n').some((l) => !l.startsWith('::add-mask::') && /FAKE_LEAK_[ABD]_|FAKE LEAK\/C\+/.test(l)));
}
{ // C8
  const root = fresh('c8');
  fs.writeFileSync(path.join(root, 'trace', 'broken.zip'), Buffer.from('PK not a zip'));
  const r = run(root);
  check('C8 못 푼 zip = 빨강', r.code === 1 && /못 푼 zip/.test(r.out), `code=${r.code}`);
}
{ // ⑪ 서로 부분 문자열 아님 — 100회
  let ok = true;
  for (let i = 0; i < 100; i += 1) {
    const v = Object.values(plantValues());
    for (const a of v) for (const b of v) if (a !== b && (a.includes(b) || b.includes(a))) ok = false;
  }
  // ⬛ [낡은 줄 · 1~5차] check('⑪ 심은 값 3개 서로 부분 문자열 아님(100회)', ok);
  check('⑪ 심은 값끼리 서로 부분 문자열 아님(100회)', ok);
}

// ── 🔴 6차(10/29 · §0-11-5) C9~C11 ─────────────────────────────────────────────
// 시험 안에서 꼴을 따로 계산한다(lib 의 함수로 lib 을 재지 않는다 — 독립 잣대).
function b64Mid(value, k) {
  const src = Buffer.from(value, 'utf8');
  const enc = Buffer.concat([Buffer.alloc(k), src]).toString('base64').replace(/=+$/, '');
  return { full: enc, mid: enc.slice(Math.ceil((k * 8) / 6), Math.floor(((k + src.length) * 8) / 6)) };
}
const hex = () => crypto.randomBytes(4).toString('hex');
// C9 값 — 비번 꼴 기호(공백 · # · / · + · ~) 포함. 「~」 는 base64 자리 하나에서 「+」 를 만든다(url 안전 꼴이 달라지게).
const C9_VALUE = `SELFTEST_C9 #${hex()}/${hex()}~${hex()}+${hex()}`;
process.env.UI_SECRET_SELFTEST_C9_1_ABCDEF = C9_VALUE; // 이 프로세스 안에서만(자식 run() 은 env 를 따로 만든다)

function c9Check(name, msg, needle) {
  const d = L.describeError(new Error(msg));
  const s = L.sanitize(msg, Number.MAX_SAFE_INTEGER);
  const gone = !d.includes(needle) && !s.includes(needle);
  check(name, gone && d.includes('***') && s.includes('***'), `describeError 남음=${d.includes(needle)} · sanitize 남음=${s.includes(needle)}`);
}
{ // C9 ① 꼴마다
  const enc = encodeURIComponent(C9_VALUE);
  const encLower = enc.replace(/%[0-9A-F]{2}/g, (m) => m.toLowerCase());
  const encPlus = enc.replace(/%20/g, '+');
  c9Check('C9 원문(대조 줄 · 고치기 전에도 초록)', `note ${C9_VALUE} end`, C9_VALUE);
  c9Check('C9 URL 대문자', `GET /x?q=${enc}&z=1`, enc);
  c9Check('C9 URL 소문자', `GET /x?q=${encLower}&z=1`, encLower);
  c9Check('C9 URL +공백', `GET /x?q=${encPlus}&z=1`, encPlus);
  for (let k = 0; k < 3; k += 1) {
    const { full, mid } = b64Mid(C9_VALUE, k);
    c9Check(`C9 base64 자리 ${k}`, `note ${full} end`, mid);
  }
  const ks = [0, 1, 2].find((k) => /[+/]/.test(b64Mid(C9_VALUE, k).mid));
  if (ks === undefined) {
    check('C9 base64 url 안전', false, '시험 값이 url 안전 꼴을 못 만듦 — 시험이 틀렸다');
  } else {
    const { full, mid } = b64Mid(C9_VALUE, ks);
    const safe = (t) => t.replace(/\+/g, '-').replace(/\//g, '_');
    c9Check(`C9 base64 url 안전(자리 ${ks})`, `note ${safe(full)} end`, safe(mid));
  }
}
{ // C9b ① 자르기 경계 — describeError 기본 200자 경계에 인코딩 꼴 앞 6자가 걸치게
  const forms = [
    ['URL', encodeURIComponent(C9_VALUE)],
    ['base64', b64Mid(C9_VALUE, 0).mid],
  ];
  for (const [label, needle] of forms) {
    const msg = `${'x'.repeat(193)} ${needle} end`; // needle 이 194번째 글자부터 → 200자에서 앞 6자만 남는 자리
    const d = L.describeError(new Error(msg));
    const half = needle.slice(0, 6);
    check(`C9b 자르기 경계 ${label} 반쪽 0`, !d.includes(half), `앞 6자 남음=${d.includes(half)}`);
  }
}
{ // C9c ① 색 제어 글자가 값 한가운데
  const cut = Math.floor(C9_VALUE.length / 2);
  const msg = `note ${C9_VALUE.slice(0, cut)}\u001b[31m${C9_VALUE.slice(cut)} end`;
  const d = L.describeError(new Error(msg));
  check('C9c 색 글자 사이 값 이어 붙음 0', !d.includes(C9_VALUE) && d.includes('***'), `이어 붙은 값 남음=${d.includes(C9_VALUE)}`);
}
{ // C10 ② 자식 프로세스 — 결과표(UI_OUT 임시) · 콘솔 줄 대조
  const out = path.join(work, 'c10');
  fs.rmSync(out, { recursive: true, force: true });
  fs.mkdirSync(out, { recursive: true });
  const M = { OLD: `SELFTEST_MARK_${hex()}`, NEW: `SELFTEST_MARK_${hex()}`, TWO_A: `SELFTEST_MARK_${hex()}`, TWO_B: `SELFTEST_MARK_${hex()}` };
  const child = [
    "const L = require(process.env.SELFTEST_LIB);",
    "const r = L.loadResult(); L.bindAutosave(r);",
    "L.rec(r, { id: 'C10-OLD', what: '옛 꼴', got: L.describeError(new Error('note ' + process.env.SELFTEST_M_OLD)), pass: false, kind: 'DEVICE' });",
    "L.rec(r, { id: 'C10-NEW', what: '새 꼴', got: '끊김', pass: false, kind: 'DEVICE', err: new Error('note ' + process.env.SELFTEST_M_NEW) });",
    "const e2 = new TypeError('note ' + process.env.SELFTEST_M_TWO_B);",
    "L.rec(r, { id: 'C10-TWO', what: '새 꼴 둘', got: '끊김', pass: false, kind: 'DEVICE', err: [new Error('note ' + process.env.SELFTEST_M_TWO_A), e2, null] });",
  ].join('\n');
  const env = {
    PATH: process.env.PATH, SystemRoot: process.env.SystemRoot, UI_OUT: out,
    SELFTEST_LIB: path.join(__dirname, 'lib.js'),
    SELFTEST_M_OLD: M.OLD, SELFTEST_M_NEW: M.NEW, SELFTEST_M_TWO_A: M.TWO_A, SELFTEST_M_TWO_B: M.TWO_B,
  };
  const c = spawnSync(process.execPath, ['-e', child], { env, encoding: 'utf8' });
  const lines = (c.stdout || '').split('\n').filter((l) => /^(🟢 PASS|🔴 FAIL|⚫ FAIL|🟡 OBS) /.test(l));
  const consoleIds = lines.map((l) => (l.match(/ (C10-[A-Z]+) /) || [])[1]);
  let rows = [];
  const resFile = path.join(out, 'main', 'ui-k35-result.json');
  if (fs.existsSync(resFile)) rows = JSON.parse(fs.readFileSync(resFile, 'utf8')).rows || [];
  const oldLine = lines.find((l) => l.includes('C10-OLD')) || '';
  check('C10 옛 꼴 = 콘솔에 표지 보임(대조 줄 · 검사가 볼 수 있음)', c.status === 0 && oldLine.includes(M.OLD), `code=${c.status}`);
  const newLine = lines.find((l) => l.includes('C10-NEW')) || '';
  const twoLine = lines.find((l) => l.includes('C10-TWO')) || '';
  const errOf = (id) => String((rows.find((x) => x.id === id) || {}).err || '');
  const sameOrder = lines.length === rows.length && consoleIds.join(',') === rows.map((x) => x.id).join(',');
  const okNew = c.status === 0
    && !newLine.includes(M.NEW) && /예외=Error$/.test(newLine.trimEnd())
    && !twoLine.includes(M.TWO_A) && !twoLine.includes(M.TWO_B) && /예외=Error,TypeError$/.test(twoLine.trimEnd())
    && errOf('C10-NEW').includes(M.NEW) && errOf('C10-TWO').includes(M.TWO_A) && errOf('C10-TWO').includes(M.TWO_B)
    && !(c.stdout || '').includes(M.NEW) && sameOrder;
  check('C10 새 꼴 = 예외 글자 콘솔 0 · 결과표 err 칸에 있음 · 콘솔 줄 수·순서 = 결과표', okNew,
    `code=${c.status} 콘솔 ${lines.length}줄 · 결과표 ${rows.length}줄 · 순서 같음=${sameOrder} · err 칸=${errOf('C10-NEW') !== ''}`);
}
{ // C11 ⑤ 이름에 든 값(내용은 깨끗)
  const root = fresh('c11');
  const raw = E.UI_DB_PASSWORD; // 하위 폴더명에 원문
  const url = encodeURIComponent(E.UI_PW_PARENT); // 파일명에 URL 꼴
  const b64 = b64Mid(E.UI_JWT_SECRET, 1); // zip 항목명에 base64 꼴(자리 1)
  fs.mkdirSync(path.join(root, 'main', raw), { recursive: true });
  fs.writeFileSync(path.join(root, 'main', raw, 'clean.txt'), 'clean');
  fs.writeFileSync(path.join(root, 'main', `u-${url}.txt`), 'clean');
  fs.writeFileSync(path.join(root, 'trace', 'n.zip'), makeZip([
    { name: `resources/x${b64.full}.json`, data: Buffer.from('{"clean":1}') },
  ]));
  const r = run(root);
  const mainGone = !fs.existsSync(path.join(root, 'main'));
  const traceGone = !fs.existsSync(path.join(root, 'trace'));
  check('C11 이름 원문(하위 폴더명) 잡힘 · main 지움 · 콘솔 0', r.code === 1 && /UI_DB_PASSWORD · 꼴=raw · 자리=이름/.test(r.out) && mainGone && !r.out.includes(raw), `code=${r.code}`);
  check('C11 이름 URL 꼴(파일명) 잡힘 · main 지움 · 콘솔 0', r.code === 1 && /UI_PW_PARENT · 꼴=url · 자리=이름/.test(r.out) && mainGone && !r.out.includes(url), `code=${r.code}`);
  check('C11 이름 base64 꼴(zip 항목명) 잡힘 · trace 지움 · 콘솔 0', r.code === 1 && /UI_JWT_SECRET · 꼴=base64 · 자리=이름/.test(r.out) && traceGone && !r.out.includes(b64.mid), `code=${r.code}`);
}

// ── 🔴 7차(10/29 · §0-11-11-5) C9d · C12 · C13a·b · C14a·b·c ─────────────────────────
{ // C9d ⓓ 값 안 공백(1칸)을 줄바꿈+두 칸으로 — 첫 소독은 못 보고, 접기가 값을 다시 만든다
  const msg = `note ${C9_VALUE.replace(/ /g, '\n  ')} end`;
  const d = L.describeError(new Error(msg));
  check('C9d 접은 뒤 원문 0(값 공백 → 줄바꿈+두 칸)', !d.includes(C9_VALUE) && d.includes('***'), `접힌 원문 남음=${d.includes(C9_VALUE)}`);
}
{ // C12 M1 maskProbeLines — 시험이 꼴 글자를 따로 계산(b64Mid · encodeURIComponent)
  const probe = `FAKE_MASK_M1_${crypto.randomBytes(6).toString('hex')}#/${hex()}`;
  const control = `FAKE_MASK_CTRL_${crypto.randomBytes(6).toString('hex')}#/${hex()}`;
  let lines = null;
  let why = '';
  if (typeof L.maskProbeLines !== 'function') {
    why = '함수 없음';
  } else {
    try {
      lines = L.maskProbeLines(probe, control);
    } catch (e) {
      why = `throw ${e && e.name ? e.name : 'Error'}`;
    }
  }
  let ok = Array.isArray(lines);
  if (ok) {
    const enc = encodeURIComponent(probe);
    const encLower = enc.replace(/%[0-9A-F]{2}/g, (m) => m.toLowerCase());
    const expect = new Set([probe, enc, encLower, enc.replace(/%20/g, '+')]);
    const mids = [];
    for (let k = 0; k < 3; k += 1) {
      const { mid } = b64Mid(probe, k);
      mids.push(mid);
      if (mid.length >= 8) {
        expect.add(mid);
        expect.add(mid.replace(/\+/g, '-').replace(/\//g, '_'));
      }
    }
    const ctrlLine = `가림 측정 M1 [대조·안 가림] ${control}`;
    const bare = lines.filter((l) => l.startsWith('가림 측정 M1 [') && l !== ctrlLine);
    const glyphs = bare.map((l) => l.slice(l.indexOf('] ') + 2));
    const ctx = lines.filter((l) => l.startsWith('가림 측정 M1-문맥 ['));
    const oneEach = glyphs.length === expect.size && [...expect].every((s) => glyphs.filter((g) => g === s).length === 1);
    const urlTwo = enc !== encLower && glyphs.includes(enc) && glyphs.includes(encLower);
    const rawLines = lines.filter((l) => l.includes(probe));
    const rawOnly = rawLines.length === 1 && rawLines[0] === `가림 측정 M1 [원문] ${probe}`;
    const ctrlOk = lines.filter((l) => l === ctrlLine).length === 1;
    const ctxOk = ctx.length === 4 && [0, 1, 2].every((k) => ctx.some((l) => l.includes(`[base64-자리${k}-`) && l.includes(mids[k])))
      && ctx.some((l) => l.includes(`GET /x?q=${enc}&n=1`));
    const allKnown = lines.length === bare.length + ctx.length + 1;
    ok = oneEach && urlTwo && rawOnly && ctrlOk && ctxOk && allKnown;
    why = `꼴 ${expect.size}개 · 맨 글자 줄 ${bare.length} · 한 줄씩=${oneEach} · url 둘=${urlTwo} · 원문 [원문]만=${rawOnly} · 대조=${ctrlOk} · 문맥 4=${ctxOk}`;
  }
  check('C12 M1 꼴 글자마다 한 줄 · url 대·소문자 둘 · 원문은 [원문] 줄에만 · 대조 값 그대로 · 문맥 줄 4', ok, why);
}
{ // C13a·C13b M3 — 자식 프로세스(env 를 새로 만든다 · GITHUB_ENV 0 → 파일 쓰기 0)
  const v = `SELFTEST_${crypto.randomBytes(6).toString('hex')}`;
  const child = 'require(process.env.SELFTEST_LIB).mask(process.env.SELFTEST_V);';
  const env = { PATH: process.env.PATH, SystemRoot: process.env.SystemRoot, SELFTEST_LIB: path.join(__dirname, 'lib.js'), SELFTEST_V: v };
  const a = spawnSync(process.execPath, ['-e', child], { env, encoding: 'utf8' });
  const aOut = a.stdout || '';
  const aLines = aOut.split('\n').filter((l) => l !== '');
  check('C13a M3 CI 밖 가림 줄 = 고정 문구 1줄 · 값 0', a.status === 0 && aLines.length === 1 && !aOut.includes(v) && !aOut.includes('::add-mask::'),
    `code=${a.status} 줄=${aLines.length} 값 보임=${aOut.includes(v)}`);
  const b = spawnSync(process.execPath, ['-e', child], { env: { ...env, GITHUB_ACTIONS: 'true' }, encoding: 'utf8' });
  check('C13b M3 CI 안 가림 줄 바이트 그대로(대조 줄 · 고치기 전에도 초록)', b.status === 0 && b.stdout === `::add-mask::${v}\n`, `code=${b.status}`);
}
// C14 — 임시 폴더만(레포 .github/ 쓰기 0). 워크플로 글은 따옴표 문자열(백틱이면 `${{` 가 보간으로 읽힌다).
function c14Dir(name) {
  const d = path.join(work, name);
  fs.rmSync(d, { recursive: true, force: true });
  fs.mkdirSync(d, { recursive: true });
  return d;
}
const hasIntake = typeof L.intakeScan === 'function';
{ // C14a M4 심은 1건씩
  const d = c14Dir('c14a');
  const plantHex = hex();
  const yml = [
    'name: m4-plant',
    'on:',
    '  workflow_dispatch:',
    '    inputs:',
    '# secrets inputs http://plant.invalid',
    'jobs:',
    '  plant:',
    '    runs-on: ubuntu-latest',
    '    steps:',
    '      - run: node scripts/playwright-audit/ci/mask-probe.js',
    '      - run: echo "${{ secrets.FAKE_M4_PLANT_' + plantHex + ' }}"',
    '      - run: echo "UI_BASE=http://plant.invalid"',
    // V7-F1 대조 2줄 — `$(` 는 실행 중에 이어 붙인다(원문에 그 꼴 0). 빨강 기대 1 · 건너뜀 기대 1(맨 변수)
    '      - run: echo "ALT_BASE=' + '$' + '(cat FAKE_M4_SUB_' + plantHex + '.invalid)"',
    '      - run: echo "VAR_BASE=$FAKE_M4_VAR_' + plantHex + '"',
  ];
  fs.writeFileSync(path.join(d, 'm4-plant.yml'), yml.join('\n'));
  const want = [
    `m4-plant.yml:${yml.indexOf('    inputs:') + 1}:입력 칸`,
    `m4-plant.yml:${yml.findIndex((l) => l.includes('secrets.FAKE_M4_PLANT_')) + 1}:비밀값`,
    `m4-plant.yml:${yml.findIndex((l) => l.includes('UI_BASE=')) + 1}:주소`,
    `m4-plant.yml:${yml.findIndex((l) => l.includes('ALT_BASE=')) + 1}:주소`,
  ].sort();
  let ok = false;
  let why = '함수 없음';
  if (hasIntake) {
    const r = L.intakeScan(d);
    const got = r.hits.map((h) => `${h.file}:${h.line}:${h.kind}`).sort();
    const out = r.lines.join('\n');
    // ⬛ 7차: const noLeak = !out.includes('plant.invalid') && !out.includes('FAKE_M4_PLANT');
    const noLeak = !out.includes('plant.invalid') && !out.includes('FAKE_M4_PLANT')
      && !out.includes('FAKE_M4_SUB') && !out.includes('FAKE_M4_VAR') && !out.includes(plantHex);
    // ⬛ 7차: ok = … && r.lines.filter(…).length === 3;
    ok = !r.ok && r.targets.length === 1 && got.join('|') === want.join('|') && noLeak
      && r.lines.filter((l) => l.startsWith('⚫ FAIL [장치] M4 실값 유입 — m4-plant.yml:')).length === 4;
    why = `잡힘 ${r.hits.length} · 기대와 같음=${got.join('|') === want.join('|')} · 출력에 심은 글자 0=${noLeak}`;
  }
  // ⬛ 7차: check('C14a M4 심은 비밀값·입력 칸·주소 정확히 3 · 미끼 주석 0 · 출력에 심은 글자 0', ok, why);
  check('C14a M4 심은 비밀값·입력 칸·주소(+`$(…)`) 정확히 4 · 미끼 주석·맨 변수 0 · 출력에 심은 글자 0', ok, why);
}
{ // C14b M4 레포 워크플로 지금 그대로
  let ok = false;
  let why = '함수 없음';
  if (hasIntake) {
    const r = L.intakeScan(path.resolve(__dirname, '../../../.github/workflows'));
    ok = r.ok && r.targets.length === 1 && r.targets[0] === 'ui-browser.yml' && r.hits.length === 0
      && r.lines.length === 1 && r.lines[0] === '🟢 PASS [장치] M4 실값 유입 0 — 대상 1개';
    why = `대상=${r.targets.join(',')} · 잡힘 ${r.hits.length}${r.hits.length ? ` (${r.hits.map((h) => `${h.file}:${h.line}:${h.kind}`).join(' ')})` : ''}`;
  }
  check('C14b M4 실물 워크플로 = 대상 1(ui-browser.yml) · 잡힘 0', ok, why);
}
{ // C14c M4 대상 0
  const d = c14Dir('c14c');
  fs.writeFileSync(path.join(d, 'other.yml'), ['name: other', 'on: push', 'jobs:', '  a:', '    runs-on: ubuntu-latest'].join('\n'));
  let ok = false;
  let why = '함수 없음';
  if (hasIntake) {
    const r = L.intakeScan(d);
    ok = !r.ok && r.targets.length === 0 && r.lines.some((l) => l.startsWith('⚫ FAIL [장치] M4 실값 유입 — 대상 0개'));
    why = `ok=${r.ok} · 대상 ${r.targets.length}`;
  }
  check('C14c M4 대상 0 = 빨강(빈 검색은 거짓 통과)', ok, why);
}

// ── 🔴 10차(10/30 · §0-14-5) C17 — 예외 **이름** 소독(층 ① 함수 단독 · 층 ② 결과표 err 칸 · 층 ③ 콘솔 `예외=`) ─────────
//   메시지는 고정 'note' · 이름 칸만 바꾼다. 꼴 글자는 시험이 따로 계산(b64Mid · encodeURIComponent).
//   인증 머리글 꼴·점 셋 꼴은 실행 중에 조각 이어 붙이기 + 16진으로만 만든다(파일 글자에 그 꼴 0).
function c17Err(name) {
  const e = new Error('note');
  e.name = name;
  return e;
}
const c17NamePart = (d) => { const k = d.lastIndexOf(': note'); return k >= 0 ? d.slice(0, k) : d; };
const c17AuthWord = ['Bea', 'rer'].join('');
const c17Auth = () => `${c17AuthWord} ${hex()}${hex()}${hex()}`;
const c17Dots = () => [hex(), hex(), hex()].join('.');
// rec 1회 — 콘솔 줄은 잡아 두고(selftest 출력에 안 섞임) 결과표 줄과 함께 돌려준다
function c17Rec(err) {
  const r = { rows: [] };
  const logs = [];
  const orig = console.log;
  console.log = (...a) => { logs.push(a.join(' ')); };
  try {
    L.rec(r, { id: 'C17-REC', what: '이름 소독', got: '끊김', pass: true, kind: 'DEVICE', err });
  } finally {
    console.log = orig;
  }
  return { row: r.rows[0] || {}, line: logs.join('\n') };
}
{ // C17a HQA 합성 — 이름에 등록값 3꼴
  const enc = encodeURIComponent(C9_VALUE);
  const forms = [
    ['원문', C9_VALUE, C9_VALUE],
    ['URL 대문자', enc, enc],
    ['URL 소문자', enc.replace(/%[0-9A-F]{2}/g, (m) => m.toLowerCase()), enc.replace(/%[0-9A-F]{2}/g, (m) => m.toLowerCase())],
  ];
  for (let k = 0; k < 3; k += 1) {
    const { full, mid } = b64Mid(C9_VALUE, k);
    forms.push([`base64 자리 ${k}`, full, mid]);
  }
  const xs = forms.map(([label, put, needle]) => {
    const part = c17NamePart(L.describeError(c17Err(`pre ${put} post`)));
    return { label, ok: !part.includes(needle) && part.includes('***') };
  });
  check('C17a 이름에 등록값 3꼴(원문 · URL 대·소문자 · base64 자리 0/1/2) → 이름 부분에 꼴 0 · *** 있음', xs.every((x) => x.ok),
    xs.map((x) => `${x.label}:${x.ok ? '가림' : '남음'}`).join(' '));
}
{ // C17b 이름에 인증 머리글 꼴·점 셋 꼴
  const auth = c17Auth();
  const dots = c17Dots();
  const authTail = auth.slice(c17AuthWord.length + 1);
  const pa = c17NamePart(L.describeError(c17Err(`pre ${auth} post`)));
  const pd = c17NamePart(L.describeError(c17Err(`pre ${dots} post`)));
  const okA = !pa.includes(authTail) && pa.includes('***');
  const okD = !pd.includes(dots) && pd.includes('***');
  check('C17b 이름에 인증 머리글 꼴·점 셋 꼴 → 가려짐', okA && okD, `인증 머리글:${okA ? '가림' : '남음'} · 점 셋:${okD ? '가림' : '남음'}`);
}
{ // C17c 긴 이름
  const long = `N${'x'.repeat(400)}`;
  const part = c17NamePart(L.describeError(c17Err(long)));
  const ok = part === `${long.slice(0, 40)}…`;
  check('C17c 긴 이름 → 이름 부분 = 앞 40자 + …', ok, `이름 부분 ${part.length}자`);
}
{ // C17d rec 결과표 err 칸 · 이름에 등록값 3꼴(대조 줄)
  const enc = encodeURIComponent(C9_VALUE);
  const encLower = enc.replace(/%[0-9A-F]{2}/g, (m) => m.toLowerCase());
  const b = b64Mid(C9_VALUE, 1);
  const { row } = c17Rec(c17Err(`pre ${C9_VALUE} | ${enc} | ${encLower} | ${b.full} post`));
  const err = String(row.err || '');
  const ok = err !== '' && ![C9_VALUE, enc, encLower, b.mid].some((n) => err.includes(n)) && err.includes('***');
  check('C17d rec 결과표 err 칸 · 이름에 등록값 3꼴 → 가려짐(대조 줄 · 고치기 전에도 초록)', ok, `err 칸 ${err.length}자`);
}
{ // C17e rec 결과표 err 칸 · 이름에 인증 머리글 꼴·점 셋 꼴
  const auth = c17Auth();
  const dots = c17Dots();
  const authTail = auth.slice(c17AuthWord.length + 1);
  const { row } = c17Rec([c17Err(`pre ${auth} post`), c17Err(`pre ${dots} post`)]);
  const err = String(row.err || '');
  const okA = !err.includes(authTail);
  const okD = !err.includes(dots);
  check('C17e rec 결과표 err 칸 · 이름에 인증 머리글 꼴·점 셋 꼴 → 가려짐', err !== '' && okA && okD, `인증 머리글:${okA ? '가림' : '남음'} · 점 셋:${okD ? '가림' : '남음'}`);
}
{ // C17f rec 콘솔 `예외=` · 이름에 등록값·기호·긴 글자(대조 줄)
  const { line } = c17Rec(c17Err(`${C9_VALUE}!@#${'y'.repeat(60)}`));
  const ok = /예외=Error$/.test(line.trimEnd()) && !line.includes(C9_VALUE) && !line.includes('y'.repeat(60));
  check('C17f rec 콘솔 예외= · 이름에 등록값·기호·긴 글자 → Error 고정(대조 줄 · 고치기 전에도 초록)', ok, `콘솔 끝=${(line.match(/예외=\S*$/) || ['없음'])[0]}`);
}

// ── 🔴 10차(10/30 · §0-14-5) C18 — M4 유입 검사 낱말 넷(A2 · K-11 ㉠ · K-15 ㉠) ─────────
//   임시 폴더만(레포 .github/ 쓰기 0). 워크플로 글은 따옴표 문자열(`${{` 가 보간으로 읽히지 않게). 식 안 이름은 16진.
const C18_MARK_LINE = '      - run: node scripts/playwright-audit/ci/mask-probe.js';
const c18Head = ['name: c18', 'on: workflow_dispatch', 'jobs:', '  a:', '    runs-on: ubuntu-latest', '    steps:', C18_MARK_LINE];
// 한 파일 검사 → { r, got(파일:줄:종류 정렬), out(출력 글자) }
function c18Scan(dir) {
  const r = L.intakeScan(dir);
  return { r, got: r.hits.map((h) => `${h.file}:${h.line}:${h.kind}`).sort(), out: r.lines.join('\n') };
}
const c18Want = (file, yml, picks, kind) => picks.map((s) => `${file}:${yml.indexOf(s) + 1}:${kind}`).sort();
{ // C18a 토큰 꼴(HQA 합성 + 변형) — 한 줄씩
  const d = c14Dir('c18a');
  const plant = hex();
  const picks = [
    '          GITHUB_TOKEN: ${{ github.token }}', // HQA 합성
    '          T1_' + plant + ': ${{ github .  token }}',
    "          T2_" + plant + ": ${{ github['token'] }}",
    '          T3_' + plant + ': ${{ github[ "token" ] }}',
    '      - run: test -n "$GITHUB_TOKEN"',
    '          T4_' + plant + ': ${{ toJSON( github ) }}',
  ];
  const yml = [...c18Head, '      - run: echo ok', '        env:', ...picks];
  fs.writeFileSync(path.join(d, 'c18a.yml'), yml.join('\n'));
  const { r, got, out } = c18Scan(d);
  const want = c18Want('c18a.yml', yml, picks, '토큰');
  const noText = !/github|token|tojson/i.test(out) && !out.includes(plant) && !out.includes('${{');
  const ok = !r.ok && got.join('|') === want.join('|') && noText;
  check('C18a 토큰 꼴(HQA 합성 · 띄어쓰기 · 대괄호 두 따옴표 · GITHUB_TOKEN 글자 · toJSON(github)) → 줄마다 종류=토큰 · 줄 번호 맞음 · 출력에 줄 글자 0', ok,
    `잡힘 ${r.hits.length}/${picks.length} · 기대와 같음=${got.join('|') === want.join('|')} · 줄 글자 0=${noText}`);
}
{ // C18b 저장소 변수(HQA 합성 `vars.` 식 + 대괄호 + if: 암묵 식)
  const d = c14Dir('c18b');
  const plant = hex();
  const picks = [
    '          V1_' + plant + ': ${{ vars.FAKE_' + plant + ' }}', // HQA 합성
    "          V2_" + plant + ": ${{ vars['FAKE_" + plant + "'] }}",
    "        if: vars.FAKE_" + plant + " == 'on'",
  ];
  const yml = [...c18Head, '      - run: echo ok', '        env:', ...picks];
  fs.writeFileSync(path.join(d, 'c18b.yml'), yml.join('\n'));
  const { r, got, out } = c18Scan(d);
  const want = c18Want('c18b.yml', yml, picks, '저장소 변수');
  const ok = !r.ok && got.join('|') === want.join('|') && !out.includes(plant) && !/vars/i.test(out);
  check('C18b 저장소 변수(`vars.` 식 · `vars[...]` · if: 암묵 식) → 종류=저장소 변수', ok, `잡힘 ${r.hits.length}/${picks.length} · 기대와 같음=${got.join('|') === want.join('|')}`);
}
{ // C18c 트리거 — 키 둘 · on: 한 줄 목록
  const d = c14Dir('c18c');
  const ya = ['name: c18c-a', 'on:', '  pull_request_target:', '  workflow_call:', 'jobs:', '  a:', '    runs-on: ubuntu-latest', '    steps:', C18_MARK_LINE];
  const yb = ['name: c18c-b', 'on: [push, workflow_call, pull_request_target]', 'jobs:', '  a:', '    runs-on: ubuntu-latest', '    steps:', C18_MARK_LINE];
  fs.writeFileSync(path.join(d, 'c18c-a.yml'), ya.join('\n'));
  fs.writeFileSync(path.join(d, 'c18c-b.yml'), yb.join('\n'));
  const { r, got } = c18Scan(d);
  const want = [
    ...c18Want('c18c-a.yml', ya, ['  pull_request_target:', '  workflow_call:'], '트리거'),
    ...c18Want('c18c-b.yml', yb, ['on: [push, workflow_call, pull_request_target]'], '트리거'),
  ].sort();
  const heads = r.lines.filter((l) => l.startsWith('⚫ FAIL [장치] M4 유입 길 — ') && l.endsWith(' · 종류=트리거')).length;
  const oldHead = r.lines.filter((l) => l.startsWith('⚫ FAIL [장치] M4 실값 유입 — ')).length;
  const ok = !r.ok && r.targets.length === 2 && got.join('|') === want.join('|') && heads === 3 && oldHead === 0;
  check('C18c 트리거(키 둘 · on: 한 줄 목록) → 종류=트리거 · 머리 M4 유입 길 · M4 실값 유입 머리 0', ok,
    `잡힘 ${r.hits.length}/3 · 기대와 같음=${got.join('|') === want.join('|')} · 유입 길 머리 ${heads} · 실값 유입 머리 ${oldHead}`);
}
{ // C18d 정상 줄(대조 줄) — 실물 ui-browser.yml 꼴 + 오탐 기대값 ②
  const d = c14Dir('c18d');
  const yml = [
    'name: c18d',
    'on:',
    '  pull_request:',
    '    branches: [develop, main]',
    '  workflow_dispatch:',
    'permissions:',
    '  contents: read',
    'concurrency:',
    '  group: ui-browser-${{ github.event.pull_request.number || github.ref }}',
    '  cancel-in-progress: true',
    'jobs:',
    '  ui:',
    "    if: github.event_name == 'workflow_dispatch' || startsWith(github.head_ref, 'feat/c18d-')",
    '    runs-on: ubuntu-latest',
    '    steps:',
    '      # 통째 주석(식 없음): vars. vars[ github.token GITHUB_TOKEN toJSON(github) workflow_call pull_request_target',
    '      - run: |',
    '          put() { echo "::add-mask::$2"; echo "$1=$2" >> "$GITHUB_ENV"; }',
    '          {',
    '            echo "UI_OUT=$RUNNER_TEMP/ui-out"',
    '          } >> "$GITHUB_ENV"',
    "      - run: echo '${{ toJSON(github.event) }}' > ev.json",
    '      - run: echo "${{ github.repository }} ${{ github.sha }}"',
    C18_MARK_LINE,
  ];
  fs.writeFileSync(path.join(d, 'c18d.yml'), yml.join('\n'));
  const { r, got } = c18Scan(d);
  const ok = r.ok && r.targets.length === 1 && r.hits.length === 0
    && r.lines.length === 1 && r.lines[0] === '🟢 PASS [장치] M4 실값 유입 0 — 대상 1개';
  check('C18d 정상 줄(실물 꼴 · toJSON(github.event) · github.repository·sha · $GITHUB_ENV · 통째 주석 속 낱말) → 잡힘 0 · 통과 줄 1(대조 줄)', ok,
    `잡힘 ${r.hits.length}${got.length ? ` (${got.join(' ')})` : ''}`);
}
{ // C18e 알고 받는 오탐 — 조건식 속 트리거 낱말(정책 기대값 고정)
  const d = c14Dir('c18e');
  const pick = "    if: github.event_name != 'pull_request_target'";
  const yml = ['name: c18e', 'on: workflow_dispatch', 'jobs:', '  a:', pick, '    runs-on: ubuntu-latest', '    steps:', C18_MARK_LINE];
  fs.writeFileSync(path.join(d, 'c18e.yml'), yml.join('\n'));
  const { r, got } = c18Scan(d);
  const want = c18Want('c18e.yml', yml, [pick], '트리거');
  const ok = !r.ok && got.join('|') === want.join('|');
  check('C18e 알고 받는 오탐(조건식 속 트리거 낱말 1줄) → 잡힘 정확히 1(트리거)', ok, `잡힘 ${r.hits.length} · 기대와 같음=${got.join('|') === want.join('|')}`);
}

// ── 🔴 8차(10/29 · §0-12-6 + PM 조건 ②) C15 — 외부 차단 처리기 닫힌 창 처리 거절 ─────────────────
//   Promise 를 기다려야 하므로 합계 줄은 C15 뒤(아래 async 묶음 끝)로 옮긴다.
// ⬛ [낡은 줄 · 6~7차] const bad = results.filter((x) => !x.ok).length;
// ⬛ [낡은 줄 · 6~7차] console.log(`합계 ${results.length - bad}/${results.length} PASS`);
// ⬛ [낡은 줄 · 6~7차] process.exit(bad === 0 ? 0 : 1);
const C15_CLOSED_C = 'route.continue: Target page, context or browser has been closed';
const C15_CLOSED_A = 'route.abort: Target page, context or browser has been closed';
const hasGuard = typeof L.guardRoute === 'function' && typeof L.isClosedRouteReject === 'function';
async function c15Settle(p) {
  try {
    return { ok: true, v: await p };
  } catch (e) {
    return { ok: false, e };
  }
}
// 거절을 나중에 일으켜 「창 상태는 거절된 그때 한 번만 읽는다(결정 때 0)」 를 같이 잰다.
async function c15Guard(reason, closed, kind = 'continue') {
  const tally = { continue: 0, abort: 0 };
  let reads = 0;
  let reject = null;
  const p = new Promise((_, rej) => { reject = rej; });
  const g = L.guardRoute(p, () => { reads += 1; return closed; }, tally, kind);
  const readsBefore = reads;
  reject(reason);
  const s = await c15Settle(g);
  return { s, tally, reads, readsBefore };
}
const c15Zero = (t) => t.continue === 0 && t.abort === 0;
const c15Why = (x) => `이행=${x.s.ok} · continue ${x.tally.continue} · abort ${x.tally.abort} · 읽음 ${x.readsBefore}→${x.reads}`;
function c15Child(useGuard) {
  // Playwright 1.60.0 RouteHandler 흉내 — 처리기가 돌려준 Promise 를 기다렸다가 거절이면 고쳐 쓴 글로 다시 던진다(받는 곳 0).
  const child = [
    'let n = 0;',
    "process.on('unhandledRejection', () => { n += 1; });",
    'const err = new Error(process.env.SELFTEST_MSG);',
    "async function handle(p) { try { await p; } catch (e) { throw new Error('\"' + e.message + '\" while running route callback.'); } }",
    'const tally = { continue: 0, abort: 0 };',
    useGuard
      ? "handle(require(process.env.SELFTEST_LIB).guardRoute(Promise.reject(err), () => true, tally, 'continue'));"
      : 'handle(Promise.reject(err));',
    "setTimeout(() => { console.log('UR=' + n + ' T=' + tally.continue); }, 50);",
  ].join('\n');
  const env = { PATH: process.env.PATH, SystemRoot: process.env.SystemRoot, SELFTEST_LIB: path.join(__dirname, 'lib.js'), SELFTEST_MSG: C15_CLOSED_C };
  const c = spawnSync(process.execPath, ['-e', child], { env, encoding: 'utf8', timeout: 20000 });
  return { code: c.status, out: (c.stdout || '').trim() };
}

// ── 🔴 9차(10/29 · §0-13-5 + PM 조건 ①) C16 — Playwright 1.60.0 순서 모형 ─────────────────────────────
//   원본(읽기만) = scripts/playwright-audit/node_modules/playwright-core/lib/coreBundle.js (package.json 1.60.0 · CI 와 같은 판).
//   아래 각 부분 위의 `:줄` = 그 원본의 줄. 레포 뿌리 node_modules(1.62.1)는 근거 아님. 실물 브라우저가 아니다(한계 = 작업지시서 §0-13-5).
const EventEmitter = require('events');
const hasSettle = typeof L.settlePopup === 'function';
const c16Tick = () => new Promise((ok) => setTimeout(ok, 30));
let c16UR = 0; // 받는 곳 없는 거절 셈(C16 들어갈 때 그물을 건다)

// F1 — :3308~3354 LongStandingScope · :3334~3336 safeRace · :3337~3353 _race
class C16Scope {
  constructor() {
    this.closed = false;
    this.waiters = new Set();
  }
  // :3319~3324 close — 기다리던 경주 전부 풀림
  close() {
    this.closed = true;
    for (const w of this.waiters) w();
  }
  // :3342·3343 이미 닫힌 범위면 곧바로 · :3347 safe → defaultValue(undefined) 로 **이행**(거절 아님)
  safeRace(p) {
    let done = null;
    const term = new Promise((ok) => { done = ok; });
    if (this.closed) done();
    this.waiters.add(done);
    return Promise.race([term.then(() => undefined), p]).finally(() => this.waiters.delete(done));
  }
}

// flow 'old' = 화면 안에서 열자마자 닫음(1~8차 H-T1 꼴) · 'new' = 우리 쪽이 기다렸다 닫음(9차)
// closeOrder = 알려진 창이 닫힐 때 서버 차례('close-first' 원본 · 'fail-first' 민감도용) · unknownOrder = 알리기 전 닫힘 차례(S1·S3·S4)
// engine 'webkit'(기본 · F4 늦은 알림) · 'chromium'(:35886~35889 세션 준비 뒤 곧바로 reportAsNew — 첫 이동을 안 기다림 ·
//   모형은 evaluate 응답보다 먼저 알림 = PM 조건 ①「open() 전에 받을 준비」 를 재는 차례 · 실물 차례 ⚠️미측정)
function c16World({ flow, closeOrder = 'close-first', unknownOrder = 'S3', handler, engine = 'webkit' }) {
  const w = { log: [], readFail: 0, tally: { continue: 0, abort: 0 } };
  // F6 — :58937·58938 in-process 연결: 메시지 하나마다 setImmediate(차례 그대로)
  const toClient = (fn) => setImmediate(fn);
  const toServer = (fn) => setImmediate(fn);
  const calls = new Map();
  let callNo = 0;
  const command = (name, arg) => new Promise((ok, no) => {
    callNo += 1;
    const id = callNo;
    calls.set(id, { ok, no });
    toServer(() => srv.onCommand(id, name, arg));
  });
  const reply = (id, err, value) => toClient(() => {
    const c = calls.get(id);
    calls.delete(id);
    if (err) c.no(new Error(err)); else c.ok(value);
  });

  // ── 손님 쪽 ──
  class C16Page extends EventEmitter {
    constructor(id) {
      super();
      this.id = id;
      this.closedNow = false;
      this.closeCalled = false;
      this.loaded = false;
      this.scope = new C16Scope();
      this.once('close', () => this.scope.close()); // F7 :56438 Close → 범위 close
    }
    isClosed() { return this.closedNow; }
    waitForLoadState(state) {
      const closedErr = () => new Error('page.waitForLoadState: Target page, context or browser has been closed');
      if (state !== 'domcontentloaded') return Promise.reject(new Error(`모형: 적재 상태 ${state} 모름`));
      if (this.loaded) return Promise.resolve();
      if (this.closedNow) return Promise.reject(closedErr());
      return new Promise((ok, no) => {
        const onLoad = () => { this.off('close', onClose); ok(); };
        const onClose = () => { this.off('domcontentloaded', onLoad); no(closedErr()); };
        this.once('domcontentloaded', onLoad);
        this.once('close', onClose);
      });
    }
    // F8 :56850~56853 page.close() — 부르면 _closeWasCalled → 이후 그 창의 경로 사건은 처리기를 안 부름(:57365)
    close() {
      this.closeCalled = true;
      w.log.push('close-called');
      return command('close', this.id);
    }
  }
  const opener = new C16Page('opener');
  const frames = new Map(); // F3 :50304~50309 — 창이 알려지기 전에도 프레임은 문맥 아래 먼저 붙는다
  const pages = new Map();
  const frameOf = (fid) => {
    if (!frames.has(fid)) frames.set(fid, { pageObj: null });
    return frames.get(fid);
  };
  const client = {
    // F3 :56412 창 생성(알림) 때 mainFrame._page = this · 문맥 Page 사건 → 연 창에 popup
    onPage(pid, fid) {
      const pg = new C16Page(pid);
      pages.set(pid, pg);
      frameOf(fid).pageObj = pg;
      w.log.push('announced');
      opener.emit('popup', pg);
    },
    // F7 :56514~56519 손님 _onClose — 그 매크로태스크에서 _closed=true → Close 사건(→ 범위 close)
    onClose(pid) {
      const pg = pages.get(pid);
      pg.closedNow = true;
      w.log.push('close-delivered');
      pg.emit('close', pg);
    },
    onDcl(pid) {
      const pg = pages.get(pid);
      pg.loaded = true;
      pg.emit('domcontentloaded');
    },
    onRoute(rid, fid) {
      const frame = frameOf(fid);
      const req = {
        id: rid,
        // F3 :54789~54802 Request.frame() — 창이 손님에 아직 안 알려졌으면 던진다
        frame() {
          if (!frame.pageObj) throw new Error('Frame for this navigation request is not available, because the request was issued before the frame is created.');
          return { page: () => frame.pageObj };
        },
      };
      const route = {
        request: () => req,
        // F2 :54969~54977 continue → :54987~ _innerContinue → :54876~54878 _raceWithTargetClose → :54860~54862 _targetClosedScope
        //   부르는 **그때** 범위를 잡는다 — 손님 쪽 창 객체가 없으면 새 범위(영영 안 닫힘) · 서버 실패 응답이면 그대로 거절
        continue() {
          const scope = frame.pageObj ? frame.pageObj.scope : new C16Scope();
          const ch = command('continue', rid);
          ch.then(() => w.log.push(`ok:${rid}`), () => w.log.push(`fail:${rid}`));
          return scope.safeRace(ch);
        },
      };
      // F8 :57360~57366 문맥 _onRoute — 우리 쪽 close() 를 부른 창이면 처리기를 안 부르고 건너뜀
      if (frame.pageObj && frame.pageObj.closeCalled) {
        w.log.push(`skip:${rid}`);
        return;
      }
      // F9 :55307~55322 RouteHandler — 처리기 거절은 고쳐 다시 던져지고 받는 곳 0(→ 받는 곳 없는 거절)
      (async () => {
        try {
          await handler(route, w);
        } catch (e) {
          throw new Error(`"${e && e.message}" while running route callback.`);
        }
      })();
    },
  };

  // ── 서버 쪽 ──
  let nextReq = 0;
  const srv = {
    popup: null,
    onCommand(id, name, arg) {
      if (name === 'open') return this.open(id);
      if (name === 'continue') return this.onContinue(id, arg);
      if (name === 'close') return this.close(id);
      return reply(id, `모형: 모르는 명령 ${name}`);
    },
    route(p, kind) {
      nextReq += 1;
      const rid = `${kind}${nextReq}`;
      toClient(() => client.onRoute(rid, p.fid));
    },
    // 화면 안 window.open 1회 — 첫 문서 요청을 가로채 손님에 보내고 evaluate 응답(녹음기 목록)
    open(id) {
      const p = { id: 'popup1', fid: 'f1', open: true, announced: false, pending: new Map() };
      this.popup = p;
      if (engine === 'chromium') { // :35886~35889 — 첫 이동 전에 알림
        p.announced = true;
        toClient(() => client.onPage(p.id, p.fid));
      }
      this.route(p, 'doc');
      reply(id, null, [{ name: 'hitpanChat' }]);
    },
    onContinue(id, rid) {
      const p = this.popup;
      if (!p.open) return reply(id, C15_CLOSED_C); // 닫힌 뒤 온 명령
      p.pending.set(id, rid);
      if (rid.startsWith('doc')) {
        if (flow === 'old') return this.closeTarget(); // 1~8차: 열자마자 닫음 → 첫 이동 확정 **전에** 닫힘(F4)
        setImmediate(() => this.commitDoc(p, id)); // 브라우저가 첫 문서 요청을 마침
      }
      return undefined; // 하위 요청 = 브라우저 진행 중(닫힐 때까지 응답 없음) = 「닫는 때 날던 중」
    },
    // F4 :45261~45273 WebKit 팝업은 **첫 이동 확정 뒤에야** 손님에 알린다(reportAsNew) → 하위 요청 2 → domcontentloaded
    commitDoc(p, id) {
      if (!p.open) return;
      p.pending.delete(id);
      reply(id, null);
      if (!p.announced) {
        p.announced = true;
        toClient(() => client.onPage(p.id, p.fid));
      }
      this.route(p, 'sub');
      this.route(p, 'sub');
      toClient(() => client.onDcl(p.id));
    },
    close(id) {
      this.closeTarget();
      reply(id, null);
    },
    // F5 :45199~45211 didClose · :44313 WKSession.dispose · :49016~49018 PageDispatcher
    closeTarget() {
      const p = this.popup;
      if (!p.open) return;
      p.open = false;
      const pend = [...p.pending.keys()];
      p.pending.clear();
      const fail = () => { for (const id of pend) reply(id, C15_CLOSED_C); };
      const closeMsg = () => toClient(() => client.onClose(p.id));
      if (p.announced) {
        if (closeOrder === 'fail-first') { // C16c 민감도 — 순서만 뒤집음(원본 아님)
          fail();
          closeMsg();
          return;
        }
        Promise.resolve().then(fail); // :44313 dispose — 대기 명령 거절 → 실패 응답은 microtask 뒤
        closeMsg(); // :45211 _page._didClose() 동기 → :49016~49018 Close 동기 송신(실패 응답보다 먼저)
        return;
      }
      // F4 :45210·:45266~45273 · :19780~19803 — 알리기 전 닫힘: reportAsNew(오류) → 창 알림 + 이어 Close(:19800·19801).
      //   실패 응답과의 차례는 서버 microtask 깊이에 달림(실물 ⚠️미측정) → 셋: S1 알림·실패·Close / S3 실패·알림·Close / S4 알림·Close·실패
      const announce = () => {
        p.announced = true;
        toClient(() => client.onPage(p.id, p.fid));
      };
      const seq = { S1: [announce, fail, closeMsg], S3: [fail, announce, closeMsg], S4: [announce, closeMsg, fail] }[unknownOrder];
      Promise.resolve().then(() => { for (const f of seq) f(); });
    },
  };
  w.opener = opener;
  w.command = command;
  return w;
}
// 처리기 — 1~7차 꼴(continue 그대로)
const c16Plain = async (route) => { await route.continue(); };
// 처리기 — 8차 꼴(guardRoute + 거절된 그때 창 상태 읽기 · device-check.js routePageClosed 와 같은 읽기)
const c16Guarded = (route, w) => {
  const read = () => {
    try {
      return route.request().frame().page().isClosed();
    } catch (e) {
      w.readFail += 1;
      w.log.push(`read-fail:${L.errorName(e)}`);
      return false;
    }
  };
  return L.guardRoute(route.continue(), read, w.tally, 'continue');
};
async function c16Old(order, handler) {
  const w = c16World({ flow: 'old', unknownOrder: order, handler });
  const ur0 = c16UR;
  await w.command('open');
  await c16Tick();
  w.ur = c16UR - ur0;
  urInWin += w.ur; // 🔴 10차 V9-R2 — 창 안 셈 합
  return w;
}
async function c16New(closeOrder, engine = 'webkit') {
  const w = c16World({ flow: 'new', closeOrder, handler: c16Plain, engine });
  const ur0 = c16UR;
  try {
    w.s = await L.settlePopup(w.opener, async () => ({ value: await w.command('open'), opened: true }), 2000);
  } catch (e) {
    w.err = e;
  }
  await c16Tick();
  w.ur = c16UR - ur0;
  urInWin += w.ur; // 🔴 10차 V9-R2 — 창 안 셈 합
  w.listeners = w.opener.listenerCount('popup');
  return w;
}
const c16Timers = () => (typeof process.getActiveResourcesInfo === 'function' ? process.getActiveResourcesInfo().filter((x) => x === 'Timeout').length : null);

(async () => {
  { // C15a 닫힘·continue
    let ok = false;
    let why = '함수 없음';
    if (hasGuard) {
      const x = await c15Guard(new Error(C15_CLOSED_C), true, 'continue');
      ok = x.s.ok && x.tally.continue === 1 && x.tally.abort === 0 && x.readsBefore === 0 && x.reads === 1;
      why = c15Why(x);
    }
    check('C15a 닫힘·continue → 이행 · continue 1 · 창 상태는 거절 때 한 번', ok, why);
  }
  { // C15b 닫힘·abort
    let ok = false;
    let why = '함수 없음';
    if (hasGuard) {
      const x = await c15Guard(new Error(C15_CLOSED_A), true, 'abort');
      ok = x.s.ok && x.tally.abort === 1 && x.tally.continue === 0 && x.readsBefore === 0 && x.reads === 1;
      why = c15Why(x);
    }
    check('C15b 닫힘·abort → 이행 · abort 1', ok, why);
  }
  { // C15c 충돌 — 같은 글자 · 창 닫힘 false
    let ok = false;
    let why = '함수 없음';
    if (hasGuard) {
      const err = new Error(C15_CLOSED_C);
      const x = await c15Guard(err, false);
      ok = !x.s.ok && x.s.e === err && c15Zero(x.tally);
      why = `${c15Why(x)} · 같은 객체=${x.s.e === err}`;
    }
    check('C15c 충돌(창 닫힘 false) → 같은 객체로 거절 · 셈 0', ok, why);
  }
  { // C15d 다른 거절
    let ok = false;
    let why = '함수 없음';
    if (hasGuard) {
      const err = new Error('route.continue: Route is already handled!');
      const x = await c15Guard(err, true);
      ok = !x.s.ok && x.s.e === err && c15Zero(x.tally);
      why = `${c15Why(x)} · 같은 객체=${x.s.e === err}`;
    }
    check('C15d 다른 거절 → 같은 객체로 거절 · 셈 0', ok, why);
  }
  { // C15e 글자 변형 셋(앞뒤 고정 확인)
    let ok = false;
    let why = '함수 없음';
    if (hasGuard) {
      const errs = [
        new Error('route.fulfill: Target page, context or browser has been closed'),
        new Error(`"${C15_CLOSED_C}" while running route callback.`),
        new Error(`${C15_CLOSED_C}!`),
        // 🔴 9차 V8-R1(§0-13-5 · [4] V8-4b) — 앞만 다른 꼴 · 대소문자 바꾼 꼴(같은 경우 안 · 셈 그대로) → `^` 뺌·`/i` 변이를 잡는다
        new Error(`x${C15_CLOSED_C}`),
        new Error('Route.continue: Target page, context or browser has been closed'),
      ];
      const xs = [];
      for (const err of errs) xs.push(await c15Guard(err, true));
      ok = xs.every((x, k) => !x.s.ok && x.s.e === errs[k] && c15Zero(x.tally));
      why = xs.map((x, k) => `${k + 1}:거절=${!x.s.ok}·셈0=${c15Zero(x.tally)}`).join(' ');
    }
    // ⬛ [낡은 줄 · 8차] check('C15e 글자 변형 셋(다른 API · 고쳐 쓴 꼴 · 끝 덧붙임) → 셋 다 거절 · 셈 0', ok, why);
    check('C15e 글자 변형 다섯(다른 API · 고쳐 쓴 꼴 · 끝 덧붙임 · 앞 덧붙임 · 대소문자) → 다섯 다 거절 · 셈 0', ok, why);
  }
  { // C15f Error 아님
    let ok = false;
    let why = '함수 없음';
    if (hasGuard) {
      const vals = [C15_CLOSED_C, undefined];
      const xs = [];
      for (const v of vals) xs.push(await c15Guard(v, true));
      let pureOk = false;
      why = '판별 함수가 Error 아닌 값을 참으로 봄';
      try {
        pureOk = L.isClosedRouteReject(C15_CLOSED_C, true) === false && L.isClosedRouteReject(undefined, true) === false
          && L.isClosedRouteReject(null, true) === false;
      } catch (e) {
        why = `판별 함수 예외 ${L.errorName(e)}`;
      }
      ok = pureOk && xs.every((x, k) => !x.s.ok && x.s.e === vals[k] && c15Zero(x.tally));
      if (pureOk) why = xs.map((x, k) => `${k + 1}:거절=${!x.s.ok}·셈0=${c15Zero(x.tally)}`).join(' ');
    }
    check('C15f Error 아님(문자열 · undefined) → 거절 · 셈 0 · 판별 함수 안 터짐', ok, why);
  }
  { // C15h 닫힘 + 여러 줄 꼬리(PM 조건 ②)
    let ok = false;
    let why = '함수 없음';
    if (hasGuard) {
      const x = await c15Guard(new Error(`${C15_CLOSED_C}\nCall log:\n  - route handler running`), true, 'continue');
      ok = x.s.ok && x.tally.continue === 1 && x.tally.abort === 0;
      why = c15Why(x);
    }
    check('C15h 닫힘 + 여러 줄 꼬리 → 이행 · continue 1(첫 줄만 비교)', ok, why);
  }
  { // C15g1 기제 재현(대조 줄 · lib 안 씀)
    const c = c15Child(false);
    check('C15g1 받는 곳 없는 닫힘 거절 = unhandledRejection 1(대조 줄 · 고치기 전에도 초록)', c.code === 0 && c.out === 'UR=1 T=0', `code=${c.code} · ${c.out}`);
  }
  { // C15g2 기제 고침
    const c = hasGuard ? c15Child(true) : null;
    check('C15g2 guardRoute 로 감쌈 = unhandledRejection 0 · 종료 0', !!c && c.code === 0 && c.out === 'UR=0 T=1', c ? `code=${c.code} · ${c.out}` : '함수 없음');
  }
  // ── 🔴 9차 C16 — 받는 곳 없는 거절 그물(셈만 · 이 뒤 끝까지 둔다) ──
  // ⬛ [낡은 줄 · 9차] process.on('unhandledRejection', () => { c16UR += 1; }); — 10차 V9-R2: 맨 위(첫 경우 전) 그물로 옮김(셈은 같은 c16UR)
  { // C16a 대조 줄 · 옛 흐름(lib 안 씀)
    const ws = [];
    for (const o of ['S1', 'S3', 'S4']) ws.push(await c16Old(o, c16Plain));
    const ok = ws.every((x) => x.ur === 1);
    check('C16a 옛 흐름(열자마자 닫기 · continue 그대로) = 받는 곳 없는 거절 1(S1·S3·S4 · 대조 줄 · 고치기 전에도 초록)', ok, ws.map((x, k) => `${['S1', 'S3', 'S4'][k]}:UR=${x.ur}`).join(' '));
  }
  { // C16b 고침 흐름
    let ok = false;
    let why = '함수 없음';
    if (hasSettle) {
      // WebKit(지시서 모형) + Chromium 차례(알림이 evaluate 응답보다 먼저 · 「open() 전에 받을 준비」 를 잰다) — 둘 다 맞아야 PASS
      const parts = [];
      for (const engine of ['webkit', 'chromium']) {
        const w = await c16New('close-first', engine);
        const s = w.s || {};
        const iOk = w.log.indexOf('ok:doc1');
        const iClose = w.log.indexOf('close-called');
        const subInFlight = w.log.includes('fail:sub2') && w.log.includes('fail:sub3'); // 닫을 때 날던 중이었다(서버 실패 응답이 왔다)
        const one = !w.err && w.ur === 0 && Array.isArray(s.value) && s.value.length === 1 && s.value[0].name === 'hitpanChat'
          && s.opened === true && s.reported === true && s.loaded === true && s.closed === true
          && iOk >= 0 && iClose > iOk && subInFlight && w.listeners === 0;
        parts.push({ one, txt: `${engine}: 예외=${w.err ? L.errorName(w.err) : '없음'} · UR=${w.ur} · 알림·적재·닫음=${s.reported}·${s.loaded}·${s.closed} · 첫 요청 이행→닫기=${iOk}→${iClose} · 하위 2 날던 중=${subInFlight} · 처리기=${w.listeners}` });
      }
      ok = parts.every((x) => x.one);
      why = parts.map((x) => x.txt).join(' | ');
    }
    check('C16b 고침 흐름(settlePopup) → 거절 0 · 녹음 1 · 알림·적재·닫음 참 · 닫기 전 첫 요청 continue 이행', ok, why);
  }
  { // C16c 모형 민감도 — 서버 순서만 뒤집음
    let ok = false;
    let why = '함수 없음';
    if (hasSettle) {
      const w = await c16New('fail-first');
      ok = w.ur >= 1;
      why = `UR=${w.ur}`;
    }
    check('C16c 모형 민감도(실패 응답 → Close) → 거절 ≥1(결과가 서버 순서에 따라 바뀐다)', ok, why);
  }
  { // C16d 8차 전제 반증 — 옛 흐름 + guardRoute
    let ok = false;
    let why = '함수 없음';
    if (hasGuard) {
      const s1 = await c16Old('S1', c16Guarded);
      const s3 = await c16Old('S3', c16Guarded);
      const s4 = await c16Old('S4', c16Guarded);
      ok = s1.ur === 1 && s1.tally.continue === 0 && s1.readFail === 0
        && s3.ur === 1 && s3.tally.continue === 0 && s3.readFail === 1
        && s4.ur === 0 && s4.tally.continue === 1 && s4.readFail === 0;
      why = [['S1', s1], ['S3', s3], ['S4', s4]].map(([n, x]) => `${n}:UR=${x.ur}·삼킴=${x.tally.continue}·읽기실패=${x.readFail}`).join(' ');
    }
    check('C16d 8차 전제 반증 → S1 다시 던짐 · S3 다시 던짐(읽기 실패) · S4 삼킴(8차는 S4 만 덮는다)', ok, why);
  }
  { // C16e PM 조건 ① — open 이 던짐(동기 · 비동기)
    let ok = false;
    let why = '함수 없음';
    if (hasSettle) {
      const forms = [(boom) => () => { throw boom; }, (boom) => () => Promise.reject(boom)];
      const xs = [];
      for (const make of forms) {
        const w = c16World({ flow: 'new', handler: c16Plain });
        const boom = new Error('모형: open 던짐');
        const ur0 = c16UR;
        const t0 = Date.now();
        let got = null;
        try {
          await L.settlePopup(w.opener, make(boom), 5000);
        } catch (e) {
          got = e;
        }
        const ms = Date.now() - t0;
        await c16Tick();
        xs.push({ same: got === boom, listeners: w.opener.listenerCount('popup'), ur: c16UR - ur0, ms });
        urInWin += xs[xs.length - 1].ur; // 🔴 10차 V9-R2 — 창 안 셈 합
      }
      ok = xs.every((x) => x.same && x.listeners === 0 && x.ur === 0 && x.ms < 1000);
      why = xs.map((x, k) => `${k ? '비동기' : '동기'}:같은 객체=${x.same}·처리기=${x.listeners}·UR=${x.ur}·${x.ms < 1000 ? '안 기다림' : `${x.ms}ms 기다림`}`).join(' ');
    }
    check('C16e open 이 던짐(동기·비동기) → 같은 객체로 거절 · popup 처리기 0 · 받는 곳 없는 거절 0 · 상한 안 기다림', ok, why);
  }
  { // C16f PM 조건 ① — 안 열림(window.open 이 null)
    let ok = false;
    let why = '함수 없음';
    if (hasSettle) {
      const w = c16World({ flow: 'new', handler: c16Plain });
      const ur0 = c16UR;
      const timers0 = c16Timers();
      const t0 = Date.now();
      let s = null;
      let err = null;
      try {
        s = await L.settlePopup(w.opener, async () => ({ value: [{ name: 'hitpanChat' }], opened: false }), 5000);
      } catch (e) {
        err = e;
      }
      const ms = Date.now() - t0;
      const timers1 = c16Timers();
      const listeners = w.opener.listenerCount('popup');
      w.opener.emit('popup', {}); // 늦게 온 popup — 거둔 뒤라 아무 일 없어야 한다
      await c16Tick();
      const ur = c16UR - ur0;
      urInWin += ur; // 🔴 10차 V9-R2 — 창 안 셈 합
      const timersOk = timers0 === null || timers1 === timers0;
      ok = !err && !!s && s.opened === false && s.reported === false && s.loaded === false && s.closed === false
        && Array.isArray(s.value) && listeners === 0 && ur === 0 && ms < 1000 && timersOk;
      why = `예외=${err ? L.errorName(err) : '없음'} · opened=${s && s.opened} · 셋=${s && s.reported}·${s && s.loaded}·${s && s.closed} · 처리기=${listeners} · UR=${ur} · ${ms < 1000 ? '안 기다림' : `${ms}ms`} · 시계 ${timers0 === null ? '못 셈' : `${timers0}→${timers1}`}`;
    }
    check('C16f 안 열림(window.open null) → opened 거짓 · 셋 다 거짓 · 처리기 0 · 거절 0 · 상한 안 기다림 · 시계 남김 0', ok, why);
  }
  // ── 🔴 10차(10/30 · §0-14-5 · K-10 ㉠ · K-13 ㉠) C16g — settlePopup 상한 경로(V9-R3) ──
  //   ms = 300 · 걸린 시간 ∈ [299, 400)(여유 100ms · [4] 9차 D1~D3 초과 3~16ms 와 D4 501ms 사이) · 시험 감시 시계 1000ms(끝나지 않으면 PENDING = FAIL)
  //   공통 단언: 예외 머리 `settlePopup:` + 단계 이름 + `상한 300ms` · 처리기 0 · Timeout 자원 수 전후 같음 · 창 안 받는 곳 없는 거절 0
  const C16G_MS = 300;
  const C16G_SLACK = 100;
  const C16G_WATCH = 1000;
  const c16gWait = (n) => new Promise((ok) => setTimeout(ok, n));
  const c16gPopup = (closeRejects = false) => {
    const pp = { closeCalls: 0, closed: false };
    pp.waitForLoadState = () => new Promise(() => {}); // 적재 영영 안 끝남
    pp.close = () => {
      pp.closeCalls += 1;
      if (closeRejects) return Promise.reject(new Error('모형: 닫기 거절'));
      pp.closed = true;
      return Promise.resolve();
    };
    pp.isClosed = () => pp.closed;
    return pp;
  };
  // settlePopup 1회 + 감시 시계 · settled = 이행/거절 횟수(늦은 결과가 결과를 바꾸지 않나)
  async function c16gRun(page, open, settleAfter = 0) {
    const ur0 = c16UR;
    const timers0 = c16Timers();
    const settled = { res: 0, rej: 0 };
    const PENDING = {};
    let wd = null;
    const watch = new Promise((ok) => { wd = setTimeout(() => ok(PENDING), C16G_WATCH); });
    const t0 = Date.now();
    const tracked = L.settlePopup(page, open, C16G_MS).then((v) => { settled.res += 1; return { v }; }, (e) => { settled.rej += 1; return { e }; });
    const out = await Promise.race([tracked, watch]);
    const ms = Date.now() - t0;
    clearTimeout(wd);
    if (settleAfter > 0) await c16gWait(settleAfter); // 늦은 결과·사건이 다 지나가게
    await c16Tick();
    const timers1 = c16Timers();
    const ur = c16UR - ur0;
    urInWin += ur;
    return { out, pending: out === PENDING, ms, settled, ur, listeners: page.listenerCount('popup'), timersOk: timers0 === null || timers0 === timers1, timers0, timers1 };
  }
  const c16gLimitErr = (x, step) => !x.pending && 'e' in x.out && x.out.e instanceof Error
    && x.out.e.message.startsWith('settlePopup:') && x.out.e.message.includes(step) && x.out.e.message.includes(`상한 ${C16G_MS}ms`);
  const c16gCommon = (x) => x.ms >= C16G_MS - 1 && x.ms < C16G_MS + C16G_SLACK && x.listeners === 0 && x.timersOk && x.ur === 0;
  const c16gWhy = (x) => (x.pending ? `PENDING(감시 ${C16G_WATCH}ms)` : `예외=${'e' in x.out ? `${L.errorName(x.out.e)}:${String(x.out.e.message).slice(0, 40)}` : '없음(이행)'}`)
    + ` · ${x.ms}ms · 처리기=${x.listeners} · 시계 ${x.timers0 === null ? '못 셈' : `${x.timers0}→${x.timers1}`} · UR=${x.ur}`;
  { // C16g1 HQA 합성 A — open 이 영영 안 끝남
    const page = new EventEmitter();
    const x = await c16gRun(page, () => new Promise(() => {}));
    const ok = c16gLimitErr(x, '창 열기') && c16gCommon(x);
    check('C16g1 open 이 영영 안 끝남(HQA 합성 A) → 상한 예외(창 열기) · 시간 ∈ [299, 400) · 처리기 0 · 시계 원래대로 · 거절 0', ok, c16gWhy(x));
  }
  { // C16g2 D4 꼴 — open 이 500ms 뒤 열림 + 그 뒤 늦은 popup 사건
    const page = new EventEmitter();
    const late = c16gPopup();
    let lateHeard = null;
    const open = () => new Promise((ok) => {
      setTimeout(() => {
        ok({ value: [{ name: 'hitpanChat' }], opened: true });
        setImmediate(() => { lateHeard = page.emit('popup', late); });
      }, 500);
    });
    const x = await c16gRun(page, open, 400);
    const ok = c16gLimitErr(x, '창 열기') && c16gCommon(x)
      && x.settled.res === 0 && x.settled.rej === 1 && lateHeard === false && late.closeCalls === 0;
    check('C16g2 open 이 500ms 뒤 열림 + 늦은 popup → 상한 예외(창 열기) · 거절 1회 그대로 · 늦은 사건 받는 곳 0 · 늦은 팝업 닫기 0 · 시계·처리기 0', ok,
      `${c16gWhy(x)} · 이행 ${x.settled.res} 거절 ${x.settled.rej} · 늦은 사건 들음=${lateHeard} · 늦은 팝업 닫기 ${late.closeCalls}`);
  }
  { // C16g3 HQA 합성 B — 열렸는데 popup 영영 안 옴(대조 줄)
    const page = new EventEmitter();
    const x = await c16gRun(page, async () => ({ value: [{ name: 'hitpanChat' }], opened: true }));
    const ok = c16gLimitErr(x, '팝업 알림') && c16gCommon(x);
    check('C16g3 열렸는데 popup 영영 안 옴(HQA 합성 B) → 상한 예외(팝업 알림)(대조 줄 · 고치기 전에도 초록)', ok, c16gWhy(x));
  }
  { // C16g4 알림 왔는데 적재 영영 안 끝남 — 닫기 이행 · 닫기 거절 두 벌
    const parts = [];
    for (const closeRejects of [false, true]) {
      const page = new EventEmitter();
      const pp = c16gPopup(closeRejects);
      const open = () => {
        page.emit('popup', pp); // 알림이 open 응답보다 먼저(Chromium 차례)
        return Promise.resolve({ value: [{ name: 'hitpanChat' }], opened: true });
      };
      const x = await c16gRun(page, open);
      const one = c16gLimitErr(x, '첫 문서 적재') && c16gCommon(x) && pp.closeCalls === 1;
      parts.push({ one, txt: `${closeRejects ? '닫기 거절' : '닫기 이행'}: ${c16gWhy(x)} · 받은 팝업 닫기 ${pp.closeCalls}` });
    }
    check('C16g4 알림 왔는데 적재 영영 안 끝남(닫기 이행·거절) → 상한 예외(첫 문서 적재) · 받은 팝업 close() 1회 · 거절 0 · 처리기·시계 0',
      parts.every((p) => p.one), parts.map((p) => p.txt).join(' | '));
  }
  { // C16z V9-R2 — 창 밖 받는 곳 없는 거절 = 전체 − 창 안 합 = 0(대조 줄)
    await c16Tick();
    const outside = urAll - urInWin;
    check('C16z 창 밖 받는 곳 없는 거절 = 전체 − 창 안 합 = 0(V9-R2 · 대조 줄)', outside === 0, `전체 ${urAll} · 창 안 ${urInWin} · 창 밖 ${outside}`);
  }
  // ── 🔴 F-PR(11/6 · 작업지시서 20261106작 F-PR §6) 새 20칸 — 칸 이름 머리 `F-`(위 10차 C18a~e 와 겹치지 않게) ──
  //   전부 동기 · 자식 프로세스 env 는 run() 처럼 새로 만든다(GITHUB_ACTIONS · GITHUB_ENV 0) · 작업 폴더 = work/fpr 아래.
  //   H 결과표 생성기는 판정기 상수를 읽지 않는다(같은 상수로 만들면 틀이 틀려도 초록 = 자기 증명) — 참고 런 키를 여기 따로 적는다.
  {
    const F_CI = __dirname;
    const F_LIB = path.join(F_CI, 'lib.js');
    const F_JUDGE = path.join(F_CI, 'judge.js');
    const F_RUNNER = path.join(F_CI, 'run-k-bodies.js');
    const fK = (mid) => ['k35', '-', mid, '.js'].join(''); // K 본문 꼴 이름(작업 폴더 안에서만 만든다)
    const fEnvBase = () => ({ PATH: process.env.PATH, SystemRoot: process.env.SystemRoot });
    const fNode = (args, envPatch = {}) => {
      const x = spawnSync(process.execPath, args, { env: { ...fEnvBase(), ...envPatch }, encoding: 'utf8' });
      return { code: x.status, out: `${x.stdout || ''}${x.stderr || ''}` };
    };
    const fDir = (name) => {
      const d = path.join(work, 'fpr', name);
      fs.rmSync(d, { recursive: true, force: true });
      fs.mkdirSync(d, { recursive: true });
      return d;
    };
    const fTable = (name, rows, extra = {}) => {
      const o = fDir(name);
      fs.mkdirSync(path.join(o, 'main'), { recursive: true });
      fs.writeFileSync(path.join(o, 'main', 'ui-k35-result.json'),
        JSON.stringify({ schema: 'ui-k35-result/1', seed: null, external_blocked: 0, external_hosts: [], rows, ...extra }));
      return o;
    };
    const fReadRows = (o) => JSON.parse(fs.readFileSync(path.join(o, 'main', 'ui-k35-result.json'), 'utf8')).rows;
    const fCopy = (name, kFiles = {}) => {
      const d = fDir(name);
      for (const f of ['judge.js', 'lib.js', 'leak-scan.js']) fs.copyFileSync(path.join(F_CI, f), path.join(d, f));
      for (const [n, body] of Object.entries(kFiles)) fs.writeFileSync(path.join(d, n), body);
      return d;
    };
    const fChild = (name, code, envPatch = {}) => {
      const o = fDir(name);
      const x = fNode(['-e', code], { F_LIB, UI_OUT: o, ...envPatch });
      return { ...x, o };
    };
    // 참고 런 24행 키(범위 H · 브라우저 없음 5 + 두 브라우저 7×2 + 첫 브라우저만 2 + 갈래 묶음) — 판정기 상수와 따로 적는다.
    const fRow = (id, kind, browser = null, pass = true) => ({ id, g: null, path: null, browser, what: 'f', got: 'f', pass, kind, scope: 'H' });
    const fBranch = (b, how) => {
      if (how === 'obs') return [fRow('H-OBS-TERMS', 'OBS', b, false), fRow('H-D2b', 'DEVICE', b)];
      if (how === 'normal') return [fRow('H-D2', 'DEVICE', b)]; // 이미 동의된 길
      // 🔴 [3-V] 01 — 약관 화면 길(장치가 통과 경로에서 실제로 적는 줄 · 약관 결함이 고쳐진 뒤 첫 브라우저 꼴)
      if (how === 'terms') return [fRow('H-D2', 'DEVICE', b), fRow('H-D2a', 'DEVICE', b), fRow('H-D2b', 'DEVICE', b)];
      if (how === 'both') return [fRow('H-OBS-TERMS', 'OBS', b, false), fRow('H-D2b', 'DEVICE', b), fRow('H-D2', 'DEVICE', b)];
      return []; // 'none'
    };
    const fHRows = (cHow = 'obs', wHow = 'normal') => [
      fRow('H-S5a', 'DEVICE'), fRow('H-S5b', 'DEVICE'), fRow('H-S5c', 'DEVICE'),
      fRow('H-D1', 'DEVICE', 'chromium'), ...fBranch('chromium', cHow), fRow('H-D2c', 'DEVICE', 'chromium'),
      fRow('H-BRIEF', 'DEVICE', 'chromium'), fRow('H-D3', 'DEVICE', 'chromium'), fRow('H-T1', 'TEST', 'chromium'),
      fRow('H-T1w', 'DEVICE', 'chromium'), fRow('H-T2r', 'TEST', 'chromium'), fRow('H-T2', 'TEST', 'chromium'), fRow('H-D4', 'DEVICE', 'chromium'),
      fRow('H-D1', 'DEVICE', 'webkit'), ...fBranch('webkit', wHow), fRow('H-D2c', 'DEVICE', 'webkit'),
      fRow('H-BRIEF', 'DEVICE', 'webkit'), fRow('H-D3', 'DEVICE', 'webkit'), fRow('H-T1', 'TEST', 'webkit'),
      fRow('H-T1w', 'DEVICE', 'webkit'), fRow('H-D4', 'DEVICE', 'webkit'),
      fRow('H-RC', 'DEVICE'), fRow('H-NA-END', 'DEVICE'),
    ];
    const K_OK_BODY = "process.stdout.write('F18-RAN-OK\\n');\n";
    const K_ODD_BODY = "process.stdout.write('F18-RAN-ODD\\n');\n";
    // 🔴 K 연결 1차 — G 기대 키 16(G 14 · N4 2 · PM C2). 판정기 상수를 읽지 않고 작업지시서 §4-2 표에서 따로 적는다.
    //   G 줄 통과 = TEST · N4 줄 = DEVICE(§4-2 ②).
    const fG = (id, g, p, browser, kind = 'TEST', pass = true) => ({ id, g, path: p, browser, what: 'k', got: 'k', pass, kind, scope: 'K' });
    const fGRows = () => [
      fG('K35-G1', 'G1', 'bell', 'chromium'), fG('K35-G1', 'G1', 'bell', 'webkit'),
      fG('K35-G1', 'G1', 'toast', 'chromium'), fG('K35-G1', 'G1', 'toast', 'webkit'),
      fG('K35-G2', 'G2', 'bell', 'chromium'), fG('K35-G2', 'G2', 'bell', 'webkit'),
      fG('K35-G2', 'G2', 'toast', 'chromium'), fG('K35-G2', 'G2', 'toast', 'webkit'),
      fG('K35-G3', 'G3', 'bell', 'chromium'),
      fG('K35-G3-SELF', 'G3', 'self', 'chromium'),
      fG('K35-G4-APPR', 'G4', 'bell', 'chromium'),
      fG('K35-G4-CHATAPPR', 'G4', 'bell', 'chromium'),
      // 🔴 K 연결 2차(A7) — G4 토스트 길 2(작업지시서 §4-2 표 「확인 필요」 칸이 실제 흐름으로 섬 · 개발 세부 2차)
      fG('K35-G4-APPR', 'G4', 'toast', 'chromium'),
      fG('K35-G4-CHATAPPR', 'G4', 'toast', 'chromium'),
      fG('K35-G5', 'G5', 'bell', 'chromium'), fG('K35-G5', 'G5', 'toast', 'chromium'),
      fG('K35-N4', 'N4', null, 'chromium', 'DEVICE'), fG('K35-N4', 'N4', null, 'webkit', 'DEVICE'),
    ];
    const F_G_N = fGRows().length;
    // 🔴 K 연결 1차 — 판정기를 부르는 칸은 사본 폴더(K 본문 꼴 1개 기본)에서 돈다 · 실물 폴더의 K 본문 수에 묶이지 않게(§5-1).
    const fJudge = (name, outDir, envPatch = {}, kFiles = { [fK('ok')]: K_OK_BODY }) => {
      const d = fCopy(`${name}-ci`, kFiles);
      return fNode([path.join(d, 'judge.js')], { UI_OUT: outDir, ...envPatch });
    };

    // N1 — 선택(S8)과 계수(판정기)가 한 규칙인가
    { // F-C18a 이상한 이름(사본 폴더) + H 전 키 → 사본 판정기 빨강
      const d = fCopy('c18a', { [fK('')]: K_ODD_BODY });
      // ⬛ [낡은 줄 · F-PR] const x = fNode([path.join(d, 'judge.js')], { UI_OUT: fTable('c18a-out', fHRows()) });
      const x = fNode([path.join(d, 'judge.js')], { UI_OUT: fTable('c18a-out', [...fHRows(), ...fGRows()]) }); // K 연결 1차 — G 전 키
      check('F-C18a N1 K 본문 이름 꼴 이상(사본 폴더) + H 전 키 → 사본 판정기 rc 1 · 「이름 꼴 이상」', x.code === 1 && /이름 꼴 이상/.test(x.out), `rc=${x.code}`);
    }
    { // F-C18b 공용 분류 함수 단독 — 경계 꼴 둘(하나는 리눅스에서만 만들 수 있는 이름) → 이상 쪽
      let ok = false;
      let why = '함수 없음';
      const J = require(F_JUDGE);
      if (typeof J.classifyKNames === 'function') {
        const c = J.classifyKNames([fK(''), fK('a\nb'), fK('ok'), 'x.js']);
        ok = Array.isArray(c.odd) && Array.isArray(c.run) && c.odd.length === 2 && c.run.length === 1 && c.run[0] === fK('ok');
        why = `실행 ${c.run.length} · 이상 ${c.odd.length}`;
      }
      check('F-C18b N1 공용 분류 함수 단독 · 경계 꼴 둘 → 이상 2 · 실행 1', ok, why);
    }
    { // F-C18c 실행기 — 이상 이름이 있으면 아무것도 안 돌고 실패 · 정상 이름만이면 1개 실행
      let ok = false;
      let why = '실행기 없음';
      if (fs.existsSync(F_RUNNER)) {
        const d1 = fDir('c18c-1');
        fs.writeFileSync(path.join(d1, fK('')), K_ODD_BODY);
        fs.writeFileSync(path.join(d1, fK('ok')), K_OK_BODY);
        const a = fNode([F_RUNNER, '--dir', d1]);
        const d2 = fDir('c18c-2');
        fs.writeFileSync(path.join(d2, fK('ok')), K_OK_BODY);
        const b = fNode([F_RUNNER, '--dir', d2]);
        const ranA = (a.out.match(/F18-RAN-/g) || []).length;
        const ranB = (b.out.match(/F18-RAN-OK/g) || []).length;
        ok = a.code === 1 && ranA === 0 && b.code === 0 && ranB === 1 && !/F18-RAN-ODD/.test(b.out);
        why = `이상 섞임 rc=${a.code} 실행 ${ranA} · 정상만 rc=${b.code} 실행 ${ranB}`;
      }
      check('F-C18c N1 실행기 · 이상 이름 → 실행 0 · rc 1 / 정상 이름만 → 1개 실행 · rc 0', ok, why);
    }
    { // F-C18d 양성 대조 — 정상 꼴 이름 1개가 판정기에 K 본문 1개로 세어짐(종료 코드는 안 본다)
      const d = fCopy('c18d', { [fK('ok')]: K_OK_BODY });
      // ⬛ [낡은 줄 · F-PR] const x = fNode([path.join(d, 'judge.js')], { UI_OUT: fTable('c18d-out', fHRows()) });
      const x = fNode([path.join(d, 'judge.js')], { UI_OUT: fTable('c18d-out', [...fHRows(), ...fGRows()]) }); // K 연결 1차 — G 전 키
      check('F-C18d N1 양성 · 정상 꼴 이름 1개 → 판정기 「K 본문 파일 1」(종료 코드 무관 · 대조 줄)', /K 본문 파일 1\)/.test(x.out), `rc=${x.code}`);
    }

    // N2 — 기록 함수 입력 계약(결과 칸 = 참거짓만 · 아니면 줄 넣기 전에 던짐)
    { // F-C19a 참거짓 아닌 11꼴 → 전부 던짐 · 결과표 줄 0 · 콘솔 PASS 0 · 메시지 = 이름표 + 형 이름만
      const code = [
        'const L = require(process.env.F_LIB); const r = L.loadResult(); L.bindAutosave(r);',
        "const vals = ['false', 1, 0, {}, [], '0', 'no', '', null, undefined, Promise.resolve(true)];",
        'const out = [];',
        "for (const v of vals) { try { L.rec(r, { id: 'H-F19A', what: 'w', got: 'g', pass: v, kind: 'TEST' }); out.push({ t: false }); }",
        '  catch (e) { out.push({ t: true, n: String(e && e.name), m: String(e && e.message) }); } }',
        "process.stdout.write('F19A-OUT ' + JSON.stringify(out) + '\\n');",
      ].join('\n');
      const x = fChild('c19a', code);
      const line = x.out.split('\n').find((s) => s.startsWith('F19A-OUT '));
      const got = line ? JSON.parse(line.slice('F19A-OUT '.length)) : [];
      const types = ['string', 'number', 'number', 'object', 'array', 'string', 'string', 'string', 'null', 'undefined', 'object'];
      const msgOk = got.length === 11 && got.every((g, i) => g.t === true && g.n === 'RecContractError'
        && g.m === `rec: pass 는 참거짓만 (id=H-F19A · 형=${types[i]})`);
      const rows = fs.existsSync(path.join(x.o, 'main', 'ui-k35-result.json')) ? fReadRows(x.o).length : -1;
      const rest = x.out.split('\n').filter((s) => !s.startsWith('F19A-OUT ')).join('\n');
      check('F-C19a N2 참거짓 아닌 11꼴 → 전부 던짐(RecContractError · 메시지 = 이름표+형만) · 결과표 줄 0 · 콘솔 PASS 0',
        msgOk && rows === 0 && !/PASS/.test(rest), `던짐 ${got.filter((g) => g.t).length}/11 · 꼴 맞음=${msgOk} · 결과표 줄 ${rows}`);
    }
    { // F-C19b 양성 — 참·거짓은 그대로 · 콘솔 표지 그대로
      const capt = [];
      const so = console.log;
      let rows = [];
      let why = '';
      console.log = (s) => capt.push(String(s));
      try {
        const r = { rows: [] };
        L.rec(r, { id: 'H-F19B', what: 'w', got: 'g', pass: true, kind: 'TEST' });
        L.rec(r, { id: 'H-F19B', what: 'w', got: 'g', pass: false, kind: 'TEST' });
        rows = r.rows;
      } catch (e) {
        why = `예외=${L.errorName(e)}`;
      } finally {
        console.log = so;
      }
      const ok = rows.length === 2 && rows[0].pass === true && rows[1].pass === false
        && capt.length === 2 && capt[0].startsWith('🟢 PASS H-F19B') && capt[1].startsWith('🔴 FAIL [시험] H-F19B');
      check('F-C19b N2 양성 · 참·거짓 그대로 · 콘솔 🟢/🔴(대조 줄)', ok, why || `줄 ${rows.length} · 콘솔 ${capt.length}`);
    }
    { // F-C19c 흐름 밖(처리기 없음) 위반 → 종료 코드 ≠ 0
      const code = [
        'const L = require(process.env.F_LIB); const r = L.loadResult(); L.bindAutosave(r);',
        "L.rec(r, { id: 'H-F19C', what: 'w', got: 'g', pass: 'false', kind: 'TEST' });",
      ].join('\n');
      const x = fChild('c19c', code);
      check('F-C19c N2 흐름 밖 위반(처리기 없음) → 종료 코드 ≠ 0 · 콘솔 PASS 0', x.code !== 0 && x.code !== null && !/PASS/.test(x.out), `rc=${x.code}`);
    }
    { // F-C19d 흐름 잡이 꼴 안 위반 → 끊김 줄 1 · 판정기 rc 1
      const code = [
        'const L = require(process.env.F_LIB); const r = L.loadResult(); L.bindAutosave(r);',
        "const tag = { scope: 'H', browser: 'chromium' };",
        "try { L.rec(r, { ...tag, id: 'H-T1', what: 'w', got: 'g', pass: 'false', kind: 'TEST' }); }",
        "catch (e) { L.rec(r, { ...tag, id: 'H-DX', what: '[장치] 흐름 끊김', got: '@', pass: false, kind: 'DEVICE', err: e }); }",
      ].join('\n');
      const x = fChild('c19d', code);
      const rows = fs.existsSync(path.join(x.o, 'main', 'ui-k35-result.json')) ? fReadRows(x.o) : [];
      const dx = rows.filter((w) => w.id === 'H-DX').length;
      const t1 = rows.filter((w) => w.id === 'H-T1').length;
      // ⬛ [낡은 줄 · F-PR] const j = fNode([F_JUDGE], { UI_OUT: x.o });
      const j = fJudge('c19d', x.o); // K 연결 1차 — 사본 폴더(결과표는 흐름 잡이가 적은 그대로 · 기대 rc 1 그대로)
      check('F-C19d N2 흐름 잡이 꼴 안 위반 → 끊김 줄 1 · 위반 줄 0 · 판정기 rc 1', dx === 1 && t1 === 0 && j.code === 1, `끊김 ${dx} · 위반 줄 ${t1} · 판정 rc=${j.code}`);
    }

    // N3 — 메타 칸 소독(경로 1 콘솔 · 2 결과표 · 3 판정기 좋은 줄 · 5 시드 · 6 외부 호스트)
    const F20_LABEL = 'UI_SECRET_SELFTEST_C20_1_ABCDEF';
    const F20_VALUE = `SELFTEST_C20 #${hex()}/${hex()}~${hex()}+${hex()}`;
    const f20Enc = encodeURIComponent(F20_VALUE);
    const f20Needles = [F20_VALUE, f20Enc, f20Enc.replace(/%[0-9A-F]{2}/g, (m) => m.toLowerCase()), b64Mid(F20_VALUE, 0).mid];
    const f20Mix = (head) => `${head}-${F20_VALUE}|${f20Enc}|${Buffer.from(F20_VALUE, 'utf8').toString('base64')}`;
    const f20Clean = (s) => f20Needles.every((n) => !s.includes(n));
    const f20Payload = { id: f20Mix('H'), what: f20Mix('w'), browser: f20Mix('b'), path: f20Mix('p'), g: f20Mix('G'), scope: f20Mix('s'), got: f20Mix('got') };
    let f20Out = null;
    { // F-C20a 가짜 등록값을 메타 6칸에 → 콘솔 · 결과표에 꼴 0 · 가림 표지 있음
      const code = [
        'const L = require(process.env.F_LIB); const r = L.loadResult(); L.bindAutosave(r);',
        'const P = JSON.parse(process.env.F_PAYLOAD);',
        "L.rec(r, { id: P.id, what: P.what, got: P.got, pass: true, kind: 'TEST', browser: P.browser, path: P.path, g: P.g, scope: P.scope });",
      ].join('\n');
      const x = fChild('c20a', code, { [F20_LABEL]: F20_VALUE, F_PAYLOAD: JSON.stringify(f20Payload) });
      f20Out = x.o;
      const file = fs.existsSync(path.join(x.o, 'main', 'ui-k35-result.json')) ? fs.readFileSync(path.join(x.o, 'main', 'ui-k35-result.json'), 'utf8') : '';
      const ok = x.code === 0 && file.length > 0 && f20Clean(x.out) && f20Clean(file) && x.out.includes('***') && file.includes('***');
      check('F-C20a N3 가짜 등록값 → 메타 6칸 · 콘솔 · 결과표에 원문·URL·base64 꼴 0 · 가림 표지 있음', ok,
        `rc=${x.code} · 콘솔 꼴 0=${f20Clean(x.out)} · 결과표 꼴 0=${f20Clean(file)}`);
    }
    { // F-C20b 같은 값을 결과표 줄(직접 기록) · 시드 두 칸 · 외부 호스트에 → 판정기 출력 꼴 0
      const raw = { id: f20Mix('H'), g: f20Mix('G'), path: f20Mix('p'), browser: f20Mix('b'), scope: f20Mix('s'), what: 'w', got: 'g', pass: true, kind: 'TEST' };
      const rows = [...(f20Out && fs.existsSync(path.join(f20Out, 'main', 'ui-k35-result.json')) ? fReadRows(f20Out) : []), raw];
      // ⬛ [낡은 줄 · F-PR] const o = fTable('c20b', rows, { seed: … }) · const x = fNode([F_JUDGE], { UI_OUT: o, [F20_LABEL]: F20_VALUE });
      const o = fTable('c20b', [...rows, ...fGRows()], { seed: { mode: f20Mix('m'), path: f20Mix('sp') }, external_blocked: 1, external_hosts: [f20Mix('h1'), f20Mix('h2')] });
      const x = fJudge('c20b', o, { [F20_LABEL]: F20_VALUE }); // K 연결 1차 — G 전 키 · 사본 폴더
      check('F-C20b N3 판정기 좋은 줄 · 시드 줄 · 외부 호스트 줄 → 꼴 0', f20Clean(x.out) && x.out.includes('***'), `rc=${x.code} · 꼴 0=${f20Clean(x.out)}`);
    }
    { // F-C20c 대조 — 결과 칸(got)은 지금도 가림
      const rows = f20Out && fs.existsSync(path.join(f20Out, 'main', 'ui-k35-result.json')) ? fReadRows(f20Out) : [];
      const g = rows.length === 1 ? String(rows[0].got) : '';
      check('F-C20c N3 결과 칸 = 가림(대조 줄)', g.length > 0 && f20Clean(g) && g.includes('***'), `줄 ${rows.length}`);
    }
    { // F-C20d 상한 경계 — 키 칸 120 그대로 · 121 잘림 표지
      const id120 = `H${'a'.repeat(119)}`;
      const id121 = `H${'a'.repeat(120)}`;
      const so = console.log;
      let rows = [];
      let why = '';
      console.log = () => {};
      try {
        const r = { rows: [] };
        L.rec(r, { id: id120, what: 'w', got: 'g', pass: true, kind: 'DEVICE' });
        L.rec(r, { id: id121, what: 'w', got: 'g', pass: true, kind: 'DEVICE' });
        // 🔴 [3-V] 02 — 등록값이 상한 경계에 걸치는 입력(앞 반쪽만 상한 안) → 지운 뒤 자르기면 반쪽 0 · 자르고 지우기면 반쪽이 남는다
        L.rec(r, { id: `H${'a'.repeat(110)}${C9_VALUE}`, what: 'w', got: 'g', pass: true, kind: 'DEVICE' });
        rows = r.rows;
      } catch (e) {
        why = `예외=${L.errorName(e)}`;
      } finally {
        console.log = so;
      }
      const straddle = rows.length === 3 ? String(rows[2].id) : '';
      const straddleOk = straddle.length > 0 && !straddle.includes(C9_VALUE.slice(0, 8)) && straddle.includes('***');
      // ⬛ [낡은 줄 · F-PR 첫 꼴] const ok = rows.length === 2 && rows[0].id === id120 && …(경계 걸침 입력 없음)
      const ok = rows.length === 3 && rows[0].id === id120 && rows[1].id !== id121
        && String(rows[1].id).startsWith(id121.slice(0, 120)) && String(rows[1].id).includes('…') && straddleOk;
      check('F-C20d N3 키 칸 상한 경계 · 120자 그대로 · 121자 잘림 표지 · 경계에 걸친 등록값 반쪽 0(지운 뒤 자르기)', ok, why || `줄 ${rows.length} · 걸침 반쪽 0=${straddleOk}`);
    }
    { // F-C20e [3-V] 03 — 판정기 측정 커밋 표지 줄 → 꼴 0
      // ⬛ [낡은 줄 · F-PR] const o = fTable('c20e', fHRows(), { plant: [f20Mix('pl')] });
      // ⬛ [낡은 줄 · F-PR] const x = fNode([F_JUDGE], { UI_OUT: o, [F20_LABEL]: F20_VALUE });
      const o = fTable('c20e', [...fHRows(), ...fGRows()], { plant: [f20Mix('pl')] }); // K 연결 1차 — G 전 키
      const x = fJudge('c20e', o, { [F20_LABEL]: F20_VALUE }); // K 연결 1차 — 사본 폴더
      const line = x.out.split('\n').find((s) => s.includes('측정 커밋 런')) || '';
      check('F-C20e N3 판정기 측정 커밋 표지 줄 → 원문·URL·base64 꼴 0 · 가림 표지 있음', line.length > 0 && f20Clean(x.out) && line.includes('***'),
        `표지 줄 ${line.length > 0} · 꼴 0=${f20Clean(x.out)}`);
    }
    { // F-C20f [3-V] 2차 P2 — 관찰 결과 칸 · 외부 호스트 · 줄 꼴 이상 줄 안 제어 글자(줄바꿈 · 색 제어)
      const nl = (tag) => ['F20F-', tag, '\n', `F20F-${tag}-NEXT`, '\u001b', '[31m'].join('');
      // ⬛ [낡은 줄 · F-PR] const rows = fHRows().map(…) — G 키 없음
      const rows = [...fHRows(), ...fGRows()].map((w) => (w.id === 'H-OBS-TERMS' ? { ...w, got: nl('OBS') } : w)); // K 연결 1차 — G 전 키
      rows.push({ ...fRow(nl('BAD'), 'DEVICE'), kind: 'obs' });
      // ⬛ [낡은 줄 · F-PR] const x = fNode([F_JUDGE], { UI_OUT: fTable('c20f', rows, { external_blocked: 1, external_hosts: [nl('HOST')] }) });
      const x = fJudge('c20f', fTable('c20f', rows, { external_blocked: 1, external_hosts: [nl('HOST')] })); // K 연결 1차 — 사본 폴더
      const lines = x.out.split('\n');
      const part = (tag, head) => ({
        tag,
        seen: lines.some((s) => s.includes(head) && s.includes(`F20F-${tag}`)),
        split: lines.some((s) => s.startsWith(`F20F-${tag}-NEXT`)),
      });
      const ps = [part('OBS', '🟡 OBS'), part('HOST', '외부 요청 끊음'), part('BAD', '줄 꼴 이상')];
      const esc = x.out.includes('\u001b');
      const ok = x.code === 1 && ps.every((p) => p.seen && !p.split) && !esc && x.out.includes('\\u000a');
      check('F-C20f N3 판정기 관찰 결과 칸 · 외부 호스트 줄 · 줄 꼴 이상 줄 제어 글자 → 줄 갈라짐 0 · 제어 글자 0 · 가린 글자 있음', ok,
        `rc=${x.code} · ${ps.map((p) => `${p.tag} 찍힘=${p.seen} 갈라짐=${p.split}`).join(' · ')} · 제어 글자=${esc}`);
    }

    // 상신-L14 — 판정기 H 기대 키 틀(누락 · 중복 · 모르는 키 · 갈래 · G 빈 자리)
    // ⬛ [낡은 줄 · F-PR] const fJ = (name, rows) => fNode([F_JUDGE], { UI_OUT: fTable(name, rows) });
    const fJ = (name, rows) => fJudge(name, fTable(name, [...rows, ...fGRows()])); // K 연결 1차 — G 전 키 · 사본 폴더(칸 이름 · 기대 그대로)
    { // F-C21a H 한 행
      const x = fJ('c21a', [fRow('H-T1', 'TEST', 'chromium')]);
      check('F-C21a L14 H 한 행 → rc 1 · 누락 > 0', x.code === 1 && /키 틀: 누락 [1-9]/.test(x.out), `rc=${x.code}`);
    }
    { // F-C21b 같은 행 두 번
      const x = fJ('c21b', [...fHRows(), fRow('H-T1', 'TEST', 'chromium')]);
      check('F-C21b L14 전 키 + 한 행 중복 → rc 1 · 중복 1', x.code === 1 && /중복 1 /.test(x.out), `rc=${x.code}`);
    }
    { // F-C21c 양성 — 전 키(첫 브라우저 관찰 갈래 · 둘째 일반 갈래)
      const x = fJ('c21c', fHRows('obs', 'normal'));
      check('F-C21c L14 양성 · 전 키(관찰 갈래 + 일반 갈래) → rc 0(대조 줄)', x.code === 0 && /🟢 PASS 전부/.test(x.out), `rc=${x.code}`);
    }
    // ⬛ [낡은 칸 · F-PR 첫 꼴] F-C21d 양성 = 두 브라우저 다 「이미 동의된 길」 · 관찰 0 — 장치가 실제로 만들 수 없는 꼴([3-V] 01)
    { // F-C21d 양성 — 약관 고친 뒤 장치가 실제로 만드는 꼴(첫 브라우저 약관 화면 길 · 둘째 이미 동의된 길 · 관찰 0)
      const x = fJ('c21d', fHRows('terms', 'normal'));
      check('F-C21d L14 양성 · 약관 고친 뒤 꼴(첫 브라우저 약관 화면 길 · 둘째 동의 끝 길 · 관찰 0) → rc 0(대조 줄)', x.code === 0 && /🟢 PASS 전부/.test(x.out), `rc=${x.code}`);
    }
    { // F-C21e 브라우저별 필수 하나 뺌
      const rows = fHRows().filter((w) => !(w.id === 'H-D3' && w.browser === 'webkit'));
      const x = fJ('c21e', rows);
      check('F-C21e L14 둘째 브라우저 필수 하나 뺌 → rc 1 · 누락 1', x.code === 1 && /키 틀: 누락 1 /.test(x.out), `rc=${x.code}`);
    }
    { // F-C21f 한 브라우저 두 갈래 / 갈래 없음 — 두 벌
      const a = fJ('c21f-1', fHRows('both', 'normal'));
      const b = fJ('c21f-2', fHRows('obs', 'none'));
      const ok = a.code === 1 && /갈래 [1-9]/.test(a.out) && b.code === 1 && /갈래 [1-9]/.test(b.out);
      check('F-C21f L14 한 브라우저 두 갈래 / 갈래 없음 → 둘 다 rc 1 · 갈래', ok, `두 갈래 rc=${a.code} · 갈래 없음 rc=${b.code}`);
    }
    { // F-C21g 모르는 H 키
      const x = fJ('c21g', [...fHRows(), fRow('H-ZZ', 'DEVICE')]);
      check('F-C21g L14 모르는 H 키 → rc 1 · 모르는 1', x.code === 1 && /모르는 1 /.test(x.out), `rc=${x.code}`);
    }
    // ⬛ [낡은 칸 · F-PR] F-C21h — G 자리를 채워(K 연결 1차) 전제 「G 자리 빈 틀」 이 사라졌다. 뜻(G 키 없이 초록 0)은 K-C22b 가 잇는다.
    // { // F-C21h K 본문(사본) + G 칸 빈 틀 + G 시험 줄 → 빨강
    //   const d = fCopy('c21h', { [fK('ok')]: K_OK_BODY });
    //   const gRow = { id: 'G-F21H', g: 'G-F21H', path: null, browser: 'chromium', what: 'f', got: 'f', pass: true, kind: 'TEST', scope: 'G' };
    //   const x = fNode([path.join(d, 'judge.js')], { UI_OUT: fTable('c21h-out', [...fHRows(), gRow]) });
    //   check('F-C21h L14 K 본문(사본) + G 칸 빈 틀 + G 시험 줄 → rc 1 · G 자리 비었음', x.code === 1 && /G 자리 비었음/.test(x.out), `rc=${x.code}`);
    // }
    { // F-C21i [3-V] 03 — 모르는 키 이름표 안 제어 글자(줄바꿈 · 색 제어) → 출력 줄이 갈라지지 않음 · 가린 글자로
      const odd = fRow(['H-ZZ', '\n', 'F21I-NEXT', '\u001b', '[31m'].join(''), 'DEVICE');
      const x = fJ('c21i', [...fHRows(), odd]);
      const split = x.out.split('\n').some((s) => s.startsWith('F21I-NEXT'));
      const esc = x.out.includes('\u001b');
      check('F-C21i L14 모르는 키 이름표 안 제어 글자 → rc 1 · 줄 갈라짐 0 · 제어 글자 0 · 가린 글자 있음', x.code === 1 && !split && !esc && x.out.includes('\\u000a'),
        `rc=${x.code} · 갈라짐=${split} · 제어 글자=${esc}`);
    }
    // 🔴 [4] P1 — 장치가 정상 경로에서 같은 키를 두 줄 적는 자리(첫 브라우저 · 늦게 뜬 팝업을 결재 길 클릭 직전 닫음)
    { // F-C21j 양성 — 늦은 팝업 정상 꼴
      const x = fJ('c21j', [...fHRows(), fRow('H-BRIEF', 'DEVICE', 'chromium')]);
      check('F-C21j L14 양성 · 첫 브라우저 늦은 팝업 정상 꼴(같은 키 2줄 · 둘 다 통과) → rc 0 · 중복 0', x.code === 0 && /🟢 PASS 전부/.test(x.out) && /중복 0 /.test(x.out),
        `rc=${x.code}`);
    }
    { // F-C21k 허용 넓힘 0 — 3줄 · 둘째 브라우저 2줄 · 둘째 줄 실패
      const extra = (b, pass = true) => fRow('H-BRIEF', 'DEVICE', b, pass);
      const a = fJ('c21k-1', [...fHRows(), extra('chromium'), extra('chromium')]);
      const b = fJ('c21k-2', [...fHRows(), extra('webkit')]);
      const c = fJ('c21k-3', [...fHRows(), extra('chromium', false)]);
      const ok = a.code === 1 && /중복 1 /.test(a.out) && b.code === 1 && /중복 1 /.test(b.out) && c.code === 1 && /⚫ FAIL \[장치\]/.test(c.out);
      check('F-C21k L14 같은 키 3줄(첫 브라우저) · 둘째 브라우저 2줄 → 중복 빨강 · 둘째 줄 실패 → [장치] 빨강', ok,
        `3줄 rc=${a.code} · 둘째 2줄 rc=${b.code} · 실패 줄 rc=${c.code}`);
    }

    // ── 🔴 K 연결 1차(11/6 · 작업지시서 20261106작 #691 재기반 §4-2 · §5-1) — 판정기 G 기대 키 필수 ──
    //   전부 사본 폴더. 규칙 고치기 전(G 자리만 채운) 판정기에서는 b · c · d · e · g · h 여섯 칸이 빨강이어야 한다(대조).
    const kJ = (name, rows, kFiles) => fJudge(name, fTable(name, rows), {}, kFiles);
    const kAll = () => [...fHRows(), ...fGRows()];
    const kOutside = ['x', fK('ok')].join(''); // K 본문 머리로 시작하지 않는 이름(판정기·실행기가 보지 않는다)
    const kLast = (out) => out.split('\n').map((s) => s.trim()).filter((s) => s.length > 0).pop() || '';
    { // K-C22a 양성 — H 전 키 + G 전 키 + K 본문 1(사본)
      const x = kJ('k22a', kAll());
      check('K-C22a G 양성 · H 전 키 + G 전 키 + K 본문 1 → rc 0', x.code === 0 && /🟢 PASS 전부/.test(x.out), `rc=${x.code}`);
    }
    { // K-C22b 음성(A) — G 한 키만
      const x = kJ('k22b', [...fHRows(), fGRows()[0]]);
      check(`K-C22b G 음성 · G 한 키만 → rc 1 · 누락 ${F_G_N - 1}`, x.code === 1 && x.out.includes(`키 틀: 누락 ${F_G_N - 1} `), `rc=${x.code}`);
    }
    { // K-C22c 음성(B) — K 본문 0 · G 0
      const x = kJ('k22c', fHRows(), {});
      check(`K-C22c G 음성 · K 본문 0 · G 0 → rc 1 · 누락 ${F_G_N}`, x.code === 1 && x.out.includes(`키 틀: 누락 ${F_G_N} `), `rc=${x.code}`);
    }
    { // K-C22d 음성(E) — 머리 밖 이름 파일 · G 0
      const x = kJ('k22d', fHRows(), { [kOutside]: K_OK_BODY });
      check(`K-C22d G 음성 · 머리 밖 이름 파일 · G 0 → rc 1 · 누락 ${F_G_N}`, x.code === 1 && x.out.includes(`키 틀: 누락 ${F_G_N} `), `rc=${x.code}`);
    }
    { // K-C22e 음성 — G 전 키 있으나 K 본문 0 → 이유 줄
      const x = kJ('k22e', kAll(), {});
      check(`K-C22e G 음성 · G 전 키 · K 본문 0 → rc 1 · 「K 본문 0 · G 기대 ${F_G_N}」`, x.code === 1 && x.out.includes(`K 본문 0 · G 기대 ${F_G_N}`), `rc=${x.code}`);
    }
    { // K-C22f 음성 — G 줄 중복
      const x = kJ('k22f', [...kAll(), fGRows()[0]]);
      check('K-C22f G 음성 · G 줄 중복 → rc 1 · 중복 1', x.code === 1 && /중복 1 /.test(x.out), `rc=${x.code}`);
    }
    { // K-C22g 음성 두 벌 — 한 브라우저만 키에 둘째 브라우저 줄 더함 / 두 브라우저 키에서 둘째 브라우저 줄 뺌
      const extra = fGRows().filter((w) => w.browser === 'chromium' && /^G[3-5]$/.test(w.g)).map((w) => ({ ...w, browser: 'webkit' }));
      const a = kJ('k22g-1', [...kAll(), ...extra]);
      const cut = kAll().filter((w) => !(w.scope === 'K' && /^G[12]$/.test(w.g) && w.browser === 'webkit'));
      const b = kJ('k22g-2', cut);
      const ok = a.code === 1 && /모르는 [1-9]/.test(a.out) && b.code === 1 && b.out.includes('키 틀: 누락 4 ');
      check('K-C22g G 음성 두 벌 · 한 브라우저 키에 둘째 브라우저 줄 / 두 브라우저 키에서 둘째 브라우저 줄 빠짐 → 둘 다 rc 1', ok,
        `더함 rc=${a.code} · 빠짐 rc=${b.code}`);
    }
    { // K-C22h 음성 두 벌 — G 키 하나를 DEVICE 통과 줄로 채움 / N4 키를 TEST 줄로 채움
      const g = fGRows().map((w) => (w.id === 'K35-G2' && w.path === 'toast' && w.browser === 'webkit' ? { ...w, kind: 'DEVICE' } : w));
      const a = kJ('k22h-1', [...fHRows(), ...g]);
      const n = fGRows().map((w) => (w.g === 'N4' && w.browser === 'webkit' ? { ...w, kind: 'TEST' } : w));
      const b = kJ('k22h-2', [...fHRows(), ...n]);
      const ok = a.code === 1 && /G 통과 꼴 이상/.test(a.out) && b.code === 1 && /G 통과 꼴 이상/.test(b.out);
      check('K-C22h G 음성 두 벌 · G 키를 DEVICE 통과 줄로 / N4 키를 TEST 줄로 → 둘 다 rc 1 · G 통과 꼴 이상', ok, `DEVICE 채움 rc=${a.code} · N4 TEST rc=${b.code}`);
    }
    { // K-C22i 대조(D) — G 줄을 범위 H 로 적음(기록 함수 범위 기본값) → 모르는 H 키
      const x = kJ('k22i', [...fHRows(), ...fGRows().map((w) => ({ ...w, scope: 'H' }))]);
      check('K-C22i G 대조 · G 줄 범위 H → rc 1 · 모르는 키', x.code === 1 && /모르는 [1-9]/.test(x.out), `rc=${x.code}`);
    }
    { // K-C22j 이유 줄(측정 런 ⓐ 꼴) — G 전 키 · 일부 TEST 실패 → 끝 줄 [시험] · 판정표에 실패 G 줄만 🔴
      const fail = (w) => (w.id === 'K35-G1' && w.path === 'bell' && w.browser === 'chromium') || (w.id === 'K35-G5' && w.path === 'toast');
      const x = kJ('k22j', [...fHRows(), ...fGRows().map((w) => (fail(w) ? { ...w, pass: false } : w))]);
      const lines = x.out.split('\n');
      const red = lines.filter((s) => s.startsWith('🔴 FAIL [시험] K K35-'));
      const green = lines.filter((s) => s.startsWith('🟢 PASS K K35-'));
      const ok = x.code === 1 && kLast(x.out).startsWith('🔴 FAIL [시험]') && red.length === 2
        && red.some((s) => s.includes('K35-G1 G1 bell (chromium)')) && red.some((s) => s.includes('K35-G5 G5 toast (chromium)'))
        && green.length === F_G_N - 2;
      check('K-C22j G 이유 줄 · G 전 키 · 일부 TEST 실패 → 끝 줄 [시험] · 판정표 실패 G 줄만 🔴', ok,
        `rc=${x.code} · 🔴 ${red.length} · 🟢 ${green.length} · 끝 줄 [시험]=${kLast(x.out).startsWith('🔴 FAIL [시험]')}`);
    }

    // ── 🔴 K 연결 1차(§5-4 · 대표 Q1) — 거르기 규칙(ui-changes.js · 순수 함수 + 실행 껍데기) ──
    //   함수 없음 = 빨강. 실행 껍데기는 NUL 구분 목록을 표준입력으로 받는다(잡과 같은 꼴).
    const F_PICK = path.join(F_CI, 'ui-changes.js');
    const kPick = () => {
      if (!fs.existsSync(F_PICK)) return null;
      const P = require(F_PICK);
      return typeof P.decideRun === 'function' ? P : null;
    };
    const kPickCli = (names) => {
      const x = spawnSync(process.execPath, [F_PICK], { input: names.join('\0'), env: fEnvBase(), encoding: 'utf8' });
      const out = `${x.stdout || ''}${x.stderr || ''}`;
      const runs = out.split('\n').filter((s) => /^run=/.test(s));
      return { code: x.status, out, runs };
    };
    { // K-C24a 문서만 → 안 돈다(함수 · 실행 껍데기)
      const P = kPick();
      let ok = false;
      let why = '함수 없음';
      if (P) {
        // ⬛ [낡은 줄 · 3V-1] const docs = ['docs/a.md', 'docs/개발/b.md', 'README.md'];
        // 🔴 PM 결정 4V-1 ① — 문서 = docs/ 아래만 · 뿌리 README.md 는 문서 아님(대조 = 돈다)
        const docs = ['docs/a.md', 'docs/개발/b.md', 'docs/c.png'];
        const f = P.decideRun(docs);
        const c = kPickCli(docs);
        const outside = P.decideRun(['docs/a.md', 'README.md']).run;
        // ⬛ [낡은 줄 · 3V-1] ok = f.run === false && c.code === 0 && c.runs.length === 1 && c.runs[0] === 'run=false';
        ok = f.run === false && c.code === 0 && c.runs.length === 1 && c.runs[0] === 'run=false' && outside === true;
        why = `함수 ${f.run} · 껍데기 rc=${c.code} ${c.runs.join(',')} · docs 밖 .md 섞임 ${outside}`;
      }
      // ⬛ [낡은 이름 · 3V-1] 'K-C24a 거르기 · 문서만 → 안 돈다(함수 · 실행 껍데기)'
      check('K-C24a 거르기 · docs/ 아래만 → 안 돈다(함수 · 실행 껍데기) · docs 밖 .md 섞임 → 돈다', ok, why);
    }
    { // K-C24b 화면 파일 1 → 돈다(이름에 한글 · 껍데기는 NUL 구분 원문)
      const P = kPick();
      let ok = false;
      let why = '함수 없음';
      if (P) {
        const names = ['docs/a.md', 'src/HitPan.Web/Pages/가나.razor'];
        const f = P.decideRun(names);
        const c = kPickCli(names);
        // ⬛ [낡은 줄 · 갈래 A 첫 꼴] const near = P.decideRun(['src/HitPan.WebX/a.cs', 'src/HitPan.Web']); — 허용 목록 머리 경계(폐기)
        // 🔴 PM 결정 3V-1 — 문서 머리 경계: 문서 폴더 머리와 글자만 비슷한 폴더 · 대문자 확장자 → 문서 아님 = 돈다
        const near = P.decideRun(['docsx/a.txt', 'doc/a.txt', 'notes/a.MD']);
        ok = f.run === true && c.code === 0 && c.runs.length === 1 && c.runs[0] === 'run=true' && near.run === true;
        why = `함수 ${f.run} · 껍데기 ${c.runs.join(',')} · 경계 대조 ${near.run}`;
      }
      // ⬛ [낡은 이름 · 갈래 A 첫 꼴] 'K-C24b 거르기 · 화면 파일 1 → 돈다 · 폴더 머리 경계 밖 → 안 돈다(대조)'
      check('K-C24b 거르기 · 화면 파일 1 → 돈다 · 문서 머리 경계 밖(비슷한 폴더 · 대문자 확장자) → 돈다', ok, why);
    }
    { // K-C24c 장치 · 워크플로 · API 참조 · 출하 DDL → 돈다 / 다른 워크플로만 → 안 돈다(대조)
      const P = kPick();
      let ok = false;
      let why = '함수 없음';
      if (P) {
        const each = [
          ['scripts/playwright-audit/ci/judge.js'], ['.github/workflows/ui-browser.yml'], ['src/HitPan.API/Program.cs'],
          ['src/HitPan.Contracts/a.cs'], ['Directory.Build.props'], ['installer/hitpan_db_clean.sql'],
        ].map((n) => P.decideRun(n).run);
        // ⬛ [낡은 줄 · 갈래 A 첫 꼴] const other = P.decideRun(['.github/workflows/build-and-test.yml']).run; — 다른 워크플로만 → 안 돈다(폐기)
        // 🔴 PM 결정 3V-1 — 목록 밖(다른 워크플로 · 다른 프로젝트 · 패키지 목록)도 돈다 / 문서 폴더 + 다른 자리 .md 만 → 안 돈다(대조)
        const more = [
          ['.github/workflows/build-and-test.yml'], ['src/HitPan.Watchdog/a.cs'], ['scripts/playwright-audit/package.json'],
        ].map((n) => P.decideRun(n).run);
        // ⬛ [낡은 줄 · 3V-1] const docsOnly = P.decideRun(['docs/a.png', 'installer/README.md', 'docs/b/c.md']).run;
        // 🔴 PM 결정 4V-1 ① — docs/ 아래만 → 안 돈다(대조) · docs 밖 .md(설치 폴더 README) 만 → 돈다
        const docsOnly = P.decideRun(['docs/a.png', 'docs/b/c.md']).run;
        const mdOutside = P.decideRun(['installer/README.md']).run;
        // ⬛ [낡은 줄 · 3V-1] ok = each.every((v) => v === true) && more.every((v) => v === true) && docsOnly === false;
        ok = each.every((v) => v === true) && more.every((v) => v === true) && docsOnly === false && mdOutside === true;
        why = `돈다 ${each.filter((v) => v === true).length}/${each.length} · 목록 밖 돈다 ${more.filter((v) => v === true).length}/${more.length} · 문서만 ${docsOnly} · docs 밖 .md ${mdOutside}`;
      }
      // ⬛ [낡은 이름 · 갈래 A 첫 꼴] 'K-C24c 거르기 · 장치 · 워크플로 · API 참조 · 출하 DDL → 돈다 / 다른 워크플로만 → 안 돈다(대조)'
      check('K-C24c 거르기 · 장치 · 워크플로 · API · 출하 DDL · 목록 밖 파일 → 돈다 / 문서만 → 안 돈다(대조)', ok, why);
    }
    { // K-C24d 목록 못 구함(빈 입력 · 꼴 이상) → 돈다(닫힌 쪽 = 도는 쪽)
      const P = kPick();
      let ok = false;
      let why = '함수 없음';
      if (P) {
        const a = P.decideRun([]).run;
        const b = P.decideRun(null).run;
        const c = P.decideRun(undefined).run;
        const e = kPickCli([]);
        ok = a === true && b === true && c === true && e.code === 0 && e.runs.length === 1 && e.runs[0] === 'run=true';
        why = `빈 ${a} · null ${b} · undefined ${c} · 껍데기 빈 입력 ${e.runs.join(',')}`;
      }
      check('K-C24d 거르기 · 빈 입력 · 꼴 이상 → 돈다', ok, why);
    }
    // 🔴 PM 결정 3V-1 ⑤ — 거르기 칸 +2(비ASCII 이름 · 이동) · 실물 git 으로 목록을 만든다(작업 폴더 안 임시 저장소 · 사용자 설정 안 읽음).
    //   목록 명령 글자는 워크플로 거르기 단계와 같다(K-C24f 가 워크플로 글자와 대조).
    const kGitArgs = ['-c', 'core.quotePath=false', 'diff', '-z', '--no-renames', '--name-only', 'HEAD^1', 'HEAD'];
    const kRepo = (name) => {
      const d = fDir(name);
      const cfg = path.join(fDir(`${name}-cfg`), 'empty.gitconfig');
      fs.writeFileSync(cfg, '');
      const env = {
        ...fEnvBase(), HOME: path.dirname(cfg), USERPROFILE: path.dirname(cfg), GIT_CONFIG_NOSYSTEM: '1', GIT_CONFIG_GLOBAL: cfg,
        GIT_AUTHOR_NAME: 'selftest', GIT_AUTHOR_EMAIL: 'selftest@invalid', GIT_COMMITTER_NAME: 'selftest', GIT_COMMITTER_EMAIL: 'selftest@invalid',
      };
      const git = (args) => {
        const x = spawnSync('git', args, { cwd: d, env, encoding: 'utf8' });
        return { code: x.status, out: x.stdout || '' };
      };
      const put = (rel, body = 'x\n') => { fs.mkdirSync(path.dirname(path.join(d, rel)), { recursive: true }); fs.writeFileSync(path.join(d, rel), body); };
      const commit = (msg) => git(['add', '-A']).code === 0 && git(['commit', '-q', '-m', msg]).code === 0;
      const ok = git(['init', '-q']).code === 0;
      return { d, git, put, commit, ok };
    };
    const kList = (r, args = kGitArgs) => { const x = r.git(args); return x.code === 0 ? x.out.split('\0').filter((s) => s.length > 0) : null; };
    { // K-C24e 비ASCII 이름 — 문서 폴더 한글 이름만 → 안 돈다 · 화면 폴더 한글 이름 → 돈다 · 따옴표 꼴 목록이면 문서로 못 알아봄(대조 = 돈다 쪽)
      const P = kPick();
      let ok = false;
      let why = '함수 없음';
      if (P) {
        const r = kRepo('k24e');
        let a = null; let aCli = null; let quoted = null; let b = null; let bCli = null;
        if (r.ok) {
          r.put('README.txt');
          if (r.commit('c1')) {
            r.put('docs/가나 다.md');
            if (r.commit('c2')) {
              a = kList(r);
              aCli = a ? kPickCli(a) : null;
              const q = r.git(['-c', 'core.quotePath=true', 'diff', '--no-renames', '--name-only', 'HEAD^1', 'HEAD']);
              quoted = q.code === 0 ? P.decideRun(q.out.split('\n')).run : null;
              r.put('src/HitPan.Web/Pages/가나.razor');
              if (r.commit('c3')) { b = kList(r); bCli = b ? kPickCli(b) : null; }
            }
          }
        }
        ok = !!a && a.length === 1 && a[0] === 'docs/가나 다.md' && aCli && aCli.runs.length === 1 && aCli.runs[0] === 'run=false'
          && quoted === true && !!b && bCli && bCli.runs.length === 1 && bCli.runs[0] === 'run=true';
        why = `git=${r.ok} · 문서 한글 ${aCli ? aCli.runs.join(',') : '-'} · 따옴표 꼴 ${quoted} · 화면 한글 ${bCli ? bCli.runs.join(',') : '-'}`;
      }
      check('K-C24e 거르기 · 비ASCII 이름(실물 git) · 문서만 → 안 돈다 · 화면 → 돈다 · 따옴표 꼴 목록 → 돈다(대조)', ok, why);
    }
    { // K-C24f 이동 — 화면 파일을 문서 폴더로 옮김 → 옛 자리도 목록에 남아 돈다 · 옮김 짝 맞추기를 켜면 새 자리만 남아 안 돈다(대조)
      const P = kPick();
      let ok = false;
      let why = '함수 없음';
      if (P) {
        const r = kRepo('k24f');
        let off = null; let on = null;
        if (r.ok) {
          r.put('src/HitPan.Web/Pages/a.razor', '@page "/a"\n<p>selftest</p>\n');
          fs.mkdirSync(path.join(r.d, 'docs'), { recursive: true }); // 옮길 자리 폴더(git mv 는 없는 폴더로 못 옮긴다)
          if (r.commit('c1') && r.git(['mv', 'src/HitPan.Web/Pages/a.razor', 'docs/a.md']).code === 0 && r.commit('c2')) {
            const l1 = kList(r);
            off = l1 ? kPickCli(l1).runs.join(',') : null;
            const l2 = kList(r, ['-c', 'core.quotePath=false', '-c', 'diff.renames=true', 'diff', '-z', '--find-renames', '--name-only', 'HEAD^1', 'HEAD']);
            on = l2 ? kPickCli(l2).runs.join(',') : null;
          }
        }
        // 워크플로 글자 대조(동작 아님 · 고정 「돈다」 동작은 미측정 — 워크플로 읽기 확인)
        const yml = fs.readFileSync(path.join(F_CI, '..', '..', '..', '.github', 'workflows', 'ui-browser.yml'), 'utf8');
        const same = yml.includes(`git ${kGitArgs.join(' ')} > "$lst"`);
        ok = off === 'run=true' && on === 'run=false' && same;
        why = `git=${r.ok} · 짝 끔 ${off} · 짝 켬 ${on} · 워크플로 목록 명령 글자 같음=${same}`;
      }
      check('K-C24f 거르기 · 이동(화면 → 문서 폴더 · 실물 git) → 돈다 · 짝 맞추기 켜면 안 돈다(대조) · 워크플로 명령 글자 같음', ok, why);
    }
    { // K-C22k G 기대 키 수 16 고정 · 판정기 목록 = 작업지시서 표(여기 따로 적은 목록 · 키 글자도 여기서 따로 만든다)
      const J = require(F_JUDGE);
      const mine = fGRows().map((w) => [w.scope, w.id, w.g ?? '-', w.path ?? '-', w.browser ?? '-'].join('|')).sort();
      const theirs = Array.isArray(J.EXPECT_G) ? [...J.EXPECT_G].sort() : [];
      // ⬛ [낡은 줄 · 1차] const ok = theirs.length === 16 && mine.length === 16 && new Set(theirs).size === 16 && mine.every((k, i) => k === theirs[i]);
      // ⬛ [낡은 줄 · 1차] check('K-C22k G 기대 키 수 16 고정(G 14 · N4 2) · 판정기 목록 = 따로 적은 목록', ok, `판정기 ${theirs.length} · 따로 ${mine.length}`);
      // 🔴 2차(A7) — G4 토스트 길 2 추가로 18(판정기 · 이 목록 · 이 숫자 세 곳을 같은 커밋에서)
      const ok = theirs.length === 18 && mine.length === 18 && new Set(theirs).size === 18 && mine.every((k, i) => k === theirs[i]);
      check('K-C22k G 기대 키 수 18 고정(G 16 · N4 2) · 판정기 목록 = 따로 적은 목록', ok, `판정기 ${theirs.length} · 따로 ${mine.length}`);
    }
    // ── 🔴 K 연결 2차(PM 결정 4V-1 ②③④) ──
    { // K-C24g 줄바꿈 든 이름(NUL 로만 나눔) → 문서 아님 = 돈다 · 껍데기도 같음
      const P = kPick();
      let ok = false;
      let why = '함수 없음';
      if (P) {
        const nl = ['docs/a', 'src/HitPan.Web/x.razor'].join('\n'); // 이름 하나(줄바꿈이 이름의 일부)
        const f = P.decideRun([nl]).run;
        const parsed = typeof P.parseList === 'function' ? P.parseList(['docs/a.md', nl].join('\0')) : null;
        const c = kPickCli(['docs/a.md', nl]);
        const docOnly = kPickCli(['docs/a.md', 'docs/b.md']);
        ok = f === true && Array.isArray(parsed) && parsed.length === 2 && c.code === 0 && c.runs.length === 1 && c.runs[0] === 'run=true'
          && docOnly.runs.length === 1 && docOnly.runs[0] === 'run=false';
        why = `함수 ${f} · 나눈 수 ${parsed ? parsed.length : '-'} · 껍데기 ${c.runs.join(',')} · 대조(문서만) ${docOnly.runs.join(',')}`;
      }
      check('K-C24g 거르기 · 줄바꿈 든 이름 → NUL 로만 나눔 · 돈다(대조 = 문서만 안 돈다)', ok, why);
    }
    { // K-C24h 워크플로 세 지킴 글자 잠금 — 취소 아님 · 장치·워크플로 폴더 고정 「돈다」 · run=false 정확히 1줄 확인
      const ymlPath = path.join(F_CI, '..', '..', '..', '.github', 'workflows', 'ui-browser.yml');
      const guards = [
        ['취소 아님', /^\s*if: \$\{\{ !cancelled\(\) && /m],
        ['고정 돈다', /grep -qzE '\^\(scripts\/playwright-audit\/ci\/\|\\\.github\/workflows\/\)'/],
        ['run=false 정확히 1줄', /\[ "\$\(grep -c -x 'run=false' "\$out"\)" = "1" \] && \[ "\$\(grep -c '\^run=' "\$out"\)" = "1" \]/],
        // 🔴 3차(PM 결정 3V-2 · 3V2-03) — 넷째: 업로드 조건의 N4 표지 줄
        ['업로드 표지 조건', /^\s*if: always\(\) && steps\.leak\.outcome == 'success' && env\.UI_N4_HIT != '1'\s*$/m],
      ];
      const lock = (text) => guards.map(([, re]) => re.test(text));
      const yml = fs.existsSync(ymlPath) ? fs.readFileSync(ymlPath, 'utf8') : '';
      const real = lock(yml);
      // 대조 — 사본 글자에서 지킴 하나씩 지우면 그 칸이 거짓
      const cut = [
        yml.replace(/!cancelled\(\) && /g, ''),
        yml.replace(/scripts\/playwright-audit\/ci\/\|\\\.github\/workflows\//g, 'scripts/x/'),
        yml.replace(/grep -c -x 'run=false'/g, "grep -c 'run=false'"),
        yml.replace(/ && env\.UI_N4_HIT != '1'/g, ''),
      ].map((t, i) => lock(t)[i] === false);
      const ok = yml.length > 0 && real.every((v) => v === true) && cut.every((v) => v === true);
      // ⬛ [낡은 줄 · 2차] check('K-C24h 워크플로 세 지킴 글자 잠금(취소 아님 · 고정 돈다 · run=false 1줄) · 하나씩 지운 사본 → 그 칸 거짓(대조)', ok,
      // ⬛ [낡은 줄 · 2차]   `실물 ${guards.map(([n], i) => `${n}=${real[i]}`).join(' · ')} · 지운 사본 대조 ${cut.filter((v) => v).length}/3`);
      check('K-C24h 워크플로 네 지킴 글자 잠금(취소 아님 · 고정 돈다 · run=false 1줄 · 업로드 표지 조건) · 하나씩 지운 사본 → 그 칸 거짓(대조)', ok,
        `실물 ${guards.map(([n], i) => `${n}=${real[i]}`).join(' · ')} · 지운 사본 대조 ${cut.filter((v) => v).length}/${guards.length}`);
    }
    { // K-C22l N4 거짓 끝 줄 = 누출 의심 · 재실행 금지(빨강) · 다른 [장치] 실패와 섞여도 같음 / 대조 = N4 밖 [장치] 실패 → 재실행 1회 끝 줄
      const n4Off = (w) => (w.g === 'N4' && w.browser === 'webkit' ? { ...w, pass: false } : w);
      const devOff = (w) => (w.id === 'K35-G1' && w.path === 'bell' && w.browser === 'webkit' ? { ...w, kind: 'DEVICE', pass: false } : w);
      const a = kJ('k22l-1', [...fHRows(), ...fGRows().map(n4Off)]);
      const b = kJ('k22l-2', [...fHRows(), ...fGRows().map((w) => devOff(n4Off(w)))]);
      const c = kJ('k22l-3', [...fHRows(), ...fGRows().map(devOff)]);
      const leak = (x) => x.code === 1 && kLast(x.out).includes('누출 의심 · 재실행 금지') && !kLast(x.out).includes('재실행 1회');
      const ok = leak(a) && leak(b) && c.code === 1 && kLast(c.out).includes('재실행 1회') && !c.out.includes('누출 의심');
      check('K-C22l N4 거짓 → 끝 줄 누출 의심 · 재실행 금지(섞여도 같음) · 대조 = N4 밖 [장치] → 재실행 1회', ok,
        `N4 rc=${a.code} · 섞임 rc=${b.code} · 대조 rc=${c.code}`);
    }
    // ── 🔴 K 연결 2차(갈래 B · §5-1 K-C23a~c) — K 도우미(kit35.js) 순수 함수 ──
    const F_KIT = path.join(F_CI, 'kit35.js');
    const kKit = () => {
      if (!fs.existsSync(F_KIT)) return null;
      try {
        const K = require(F_KIT);
        return typeof K.sameWindowName === 'function' && typeof K.domLeakHits === 'function' && typeof K.recK === 'function' ? K : null;
      } catch (e) {
        console.log(`K 도우미 불러오기 실패 ${L.errorName(e)}`);
        return null;
      }
    };
    { // K-C23a G3 비교식 — 같은 이름 통과 · 바꾼 이름 가짜 기록 빨강 · 자체 점검이 망가진 비교식을 잡음
      const K = kKit();
      let ok = false;
      let why = '함수 없음';
      if (K) {
        const same = K.sameWindowName('hitpanChat', 'hitpanChat');
        const renamed = K.sameWindowName('hitpanChat', 'hitpanChatX');
        const empty = K.sameWindowName('', '');
        const self = K.g3SelfCheck();
        const broken = K.g3SelfCheck(() => true); // 늘 참인 비교식 → 자체 점검 거짓이어야
        ok = same === true && renamed === false && empty === false && self.pass === true && broken.pass === false;
        why = `같음 ${same} · 바꿈 ${renamed} · 빈 ${empty} · 자체 점검 ${self.pass} · 망가진 비교식 ${broken.pass}`;
      }
      check('K-C23a G3 비교식 · 같은 이름 통과 · 바꾼 이름 빨강 · 망가진 비교식을 자체 점검이 잡음', ok, why);
    }
    { // K-C23b N4 대조 — 등록값(원문 · 대문자 · base64 · url 꼴) 든 글자 잡힘 · 없는 글자 통과
      const K = kKit();
      let ok = false;
      let why = '함수 없음';
      if (K) {
        const v = `SELFTEST_N4 #${hex()}/${hex()}+${hex()}`;
        const reg = [v];
        const raw = K.domLeakHits(`화면 ${v} 끝`, reg);
        const upper = K.domLeakHits(`화면 ${v.toUpperCase()} 끝`, reg);
        const b64 = K.domLeakHits(`x ${Buffer.from(`a${v}b`, 'utf8').toString('base64')} y`, reg);
        const url = K.domLeakHits(`q=${encodeURIComponent(v)}&n=1`, reg);
        const none = K.domLeakHits('시험 가 · 새 메시지 · K35 G1 bell', reg);
        const short = K.domLeakHits('abc', ['abc']); // 소독 하한(4) 밑 = 안 봄
        ok = raw === 1 && upper === 1 && b64 === 1 && url === 1 && none === 0 && short === 0;
        why = `원문 ${raw} · 대문자 ${upper} · base64 ${b64} · url ${url} · 없음 ${none} · 짧은 값 ${short}`;
      }
      check('K-C23b N4 대조 · 등록값 3꼴·대소문자 잡힘 · 없는 글자 통과', ok, why);
    }
    { // K-C23c K 기록 도우미 — 범위 K 고정 · 목록 밖 id 던짐 · G 키 DEVICE 통과 던짐 · N4 TEST 던짐 · 같은 키 두 번 던짐 · N4 길 칸 빈 글자 던짐(자식 프로세스)
      let ok = false;
      let why = '함수 없음';
      if (kKit()) {
        const code = [
          "const K = require(process.env.F_KIT);",
          "const r = { rows: [] };",
          "const t = (f) => { try { f(); return 'ok'; } catch (e) { return e.name; } };",
          "const g1 = { id: 'K35-G1', g: 'G1', path: 'bell', browser: 'chromium' };",
          "const out = {",
          "  a: t(() => K.recK(r, { key: g1, what: 'k', got: 'k', pass: true, kind: 'TEST' })),",
          "  scope: r.rows.length === 1 ? r.rows[0].scope : '-',",
          "  out: t(() => K.recK(r, { key: { id: 'K35-ZZ', g: 'G1', path: 'bell', browser: 'chromium' }, what: 'k', got: 'k', pass: true, kind: 'TEST' })),",
          "  devPass: t(() => K.recK(r, { key: { id: 'K35-G1', g: 'G1', path: 'toast', browser: 'chromium' }, what: 'k', got: 'k', pass: true, kind: 'DEVICE' })),",
          "  devFail: t(() => K.recK(r, { key: { id: 'K35-G1', g: 'G1', path: 'toast', browser: 'webkit' }, what: 'k', got: 'k', pass: false, kind: 'DEVICE' })),",
          "  n4Test: t(() => K.recK(r, { key: { id: 'K35-N4', g: 'N4', path: null, browser: 'chromium' }, what: 'k', got: 'k', pass: true, kind: 'TEST' })),",
          "  n4Empty: t(() => K.recK(r, { key: { id: 'K35-N4', g: 'N4', path: '', browser: 'chromium' }, what: 'k', got: 'k', pass: true, kind: 'DEVICE' })),",
          "  n4Upper: t(() => K.recK(r, { key: { id: 'K35-N4', g: 'N4', path: null, browser: 'Chromium' }, what: 'k', got: 'k', pass: true, kind: 'DEVICE' })),",
          "  n4: t(() => K.recK(r, { key: { id: 'K35-N4', g: 'N4', path: null, browser: 'chromium' }, what: 'k', got: 'k', pass: true, kind: 'DEVICE' })),",
          "  dup: t(() => K.recK(r, { key: g1, what: 'k', got: 'k', pass: true, kind: 'TEST' })),",
          "  rows: r.rows.length,",
          "};",
          "process.stdout.write('K23C=' + JSON.stringify(out) + '\\n');",
        ].join('\n');
        const x = fNode(['-e', code], { F_KIT, UI_OUT: fDir('k23c-out') });
        const line = x.out.split('\n').find((s) => s.startsWith('K23C=')) || '';
        let o = null;
        try { o = JSON.parse(line.slice(5)); } catch (e) { o = null; }
        const C = 'KitContractError';
        ok = !!o && o.a === 'ok' && o.scope === 'K' && o.out === C && o.devPass === C && o.devFail === 'ok' && o.n4Test === C
          && o.n4Empty === C && o.n4Upper === C && o.n4 === 'ok' && o.dup === C && o.rows === 3;
        why = o ? `통과 ${o.a} · 범위 ${o.scope} · 목록 밖 ${o.out} · G DEVICE 통과 ${o.devPass} · G DEVICE 실패 ${o.devFail} · N4 TEST ${o.n4Test} · N4 빈 길 ${o.n4Empty} · 대문자 ${o.n4Upper} · N4 ${o.n4} · 중복 ${o.dup} · 줄 ${o.rows}` : `rc=${x.code} · 결과 줄 없음`;
      }
      check('K-C23c K 기록 도우미 · 범위 K 고정 · 목록 밖 · 꼴 이상 · 빈 길 칸 · 대문자 · 중복 → 던짐', ok, why);
    }
    // ── 🔴 K 연결 3차(11/6 · PM 결정 3V-2 · 4V-2) — 그림 임시 자리 · N4 이유 둘 · 초기 스크립트 순서 · G4 되돌림 · 콘솔 쌓기 · 시간 상한 ──
    //   K-C25a 그림 도우미 6길(가짜 창 · 가짜 그림 · 실제 파일계) · K-C25h 찍은 직후 죽음 → 업로드 자리 0
    //   K-C25b N4 판정 · 표지 먼저 · 예외 길 강제 거짓 · K-C25c 판정기 끝 줄 둘 · K-C25d 초기 스크립트 한 덩어리 순서(대조 = 뒤집으면 기록 0)
    //   K-C25e G4 되돌림 판단(순수) · K-C25f 콘솔 쌓기 → 찍을 때 소독 · K-C25g 시간 상한 도우미 · 상한 값 잠금
    //   못 재는 것(미측정): 실제 브라우저 창 · 러너 신호 · 러너 단계 시간초과 뒤 단계 조건 · 실제 Blazor 앱 안 이동.
    const kChild = (name, fn, envPatch = {}) => {
      const o = fDir(name);
      const code = `(${fn.toString()})(require(process.env.F_KIT)).then((v) => process.stdout.write('KOUT=' + JSON.stringify(v) + '\\n'), (e) => process.stdout.write('KERR=' + e.name + '\\n'));`;
      const x = fNode(['-e', code], { F_KIT, UI_OUT: path.join(o, 'out'), ...envPatch });
      const line = x.out.split('\n').find((s) => s.startsWith('KOUT=')) || '';
      let v = null;
      try { v = JSON.parse(line.slice(5)); } catch (e) { v = null; }
      const err = (x.out.split('\n').find((s) => s.startsWith('KERR=')) || '').slice(5);
      return { v, o, code: x.code, err };
    };
    const K_SECRET = `SELFTEST_K25_${hex()}${hex()}`;
    // 가짜 창 · 그림 — 자식 프로세스 안에서 쓴다(함수 글자로 넘김). collect(n) = n 번째 대조 때 화면 글자.
    const kShotWorld = async (K) => {
      const fs = require('fs');
      const path = require('path');
      const SECRET = process.env.UI_PW_GA;
      const clean = () => ({ text: '시험 나 · 새 메시지', vals: [], ph: [], pw: 0, pwOpen: 0 });
      const dirty = () => ({ text: `화면 ${SECRET}`, vals: [], ph: [], pw: 0, pwOpen: 0 });
      const mkPage = (st) => ({
        isClosed: () => false,
        frames: () => [{ evaluate: async () => { st.calls += 1; return st.collect(st.calls); } }],
        screenshot: async ({ path: f }) => st.shot(f),
      });
      const writePng = (f) => fs.writeFileSync(f, Buffer.from([0x89, 0x50, 0x4e, 0x47, 9]));
      const mkKb = (st, pagesFn) => {
        const page = mkPage(st);
        st.page = page;
        return {
          browser: 'chromium', plantN4: false, sealed: false, page,
          context: { pages: pagesFn ? () => pagesFn(page) : () => [page] },
          n4: { verified: 0, shots: 0, blocked: 0, removed: 0, delFail: 0, hits: 0, errs: 0, winDiff: 0, moveFail: 0, planted: false },
        };
      };
      const up = K.shotsDir();
      const stage = K.stageDir('chromium');
      const has = (d, n) => fs.existsSync(path.join(d, n));
      const out = {};
      { // ① 깨끗 → 옮김
        const st = { calls: 0, collect: clean, shot: writePng };
        const kb = mkKb(st);
        const ret = await K.guardedShot(kb, 'c1');
        out.clean = ret === true && has(up, 'c1-chromium.png') && !has(stage, 'c1-chromium.png') && kb.n4.verified === 1 && kb.n4.shots === 1;
      }
      { // ② 찍기 직전 걸림 → 안 찍음
        const st = { calls: 0, collect: dirty, shot: writePng };
        const kb = mkKb(st);
        const ret = await K.guardedShot(kb, 'c2');
        out.pre = ret === false && !has(up, 'c2-chromium.png') && !has(stage, 'c2-chromium.png') && kb.n4.blocked === 1 && kb.n4.hits === 1;
      }
      { // ③ 찍은 직후 걸림 → 임시 자리에서 지움 · 업로드 자리 0
        const st = { calls: 0, collect: (n) => (n >= 2 ? dirty() : clean()), shot: writePng };
        const kb = mkKb(st);
        const ret = await K.guardedShot(kb, 'c3');
        out.post = ret === false && !has(up, 'c3-chromium.png') && !has(stage, 'c3-chromium.png') && kb.n4.removed === 1 && kb.n4.hits === 1 && kb.n4.delFail === 0;
      }
      { // ④ 지우기 실패(그림 자리에 폴더) → 지우기 실패 셈 · 임시 폴더 통째 지움 · 업로드 자리 0
        const st = { calls: 0, collect: (n) => (n >= 2 ? dirty() : clean()), shot: (f) => { fs.mkdirSync(f, { recursive: true }); fs.writeFileSync(path.join(f, 'x'), 'x'); } };
        const kb = mkKb(st);
        const ret = await K.guardedShot(kb, 'c4');
        out.delFail = ret === false && !has(up, 'c4-chromium.png') && !fs.existsSync(stage) && kb.n4.delFail === 1;
      }
      { // ⑤ 창 집합 다름(찍기 직전 1개 → 찍을 때 2개) → 옮기지 않음
        let n = 0;
        const st = { calls: 0, collect: clean, shot: writePng };
        const extra = mkPage({ calls: 0, collect: clean, shot: writePng });
        const kb = mkKb(st, (page) => { n += 1; return n === 1 ? [page] : [page, extra]; });
        const ret = await K.guardedShot(kb, 'c5');
        out.win = ret === false && !has(up, 'c5-chromium.png') && !has(up, 'c5-chromium-popup1.png') && !has(stage, 'c5-chromium.png') && kb.n4.winDiff === 1;
      }
      { // ⑥ 봉인 뒤 → 안 찍음
        const st = { calls: 0, collect: clean, shot: writePng };
        const kb = mkKb(st);
        kb.sealed = true;
        const ret = await K.guardedShot(kb, 'c6');
        out.sealed = ret === false && st.calls === 0 && !has(up, 'c6-chromium.png') && !has(stage, 'c6-chromium.png');
      }
      out.stageOutside = K.stageOutside() === true && K.stageOutside(path.join(path.resolve(process.env.UI_OUT), 'main', 'x')) === false;
      return out;
    };
    { // K-C25a 그림 도우미 6길
      const K = kKit();
      let ok = false;
      let why = '함수 없음';
      if (K && typeof K.stageDir === 'function') {
        const x = kChild('k25a', kShotWorld, { UI_PW_GA: K_SECRET });
        const v = x.v || {};
        const names = ['clean', 'pre', 'post', 'delFail', 'win', 'sealed', 'stageOutside'];
        ok = !!x.v && names.every((n) => v[n] === true);
        why = x.v ? names.map((n) => `${n} ${v[n]}`).join(' · ') : `rc=${x.code} · 결과 줄 없음 ${x.err}`;
      }
      check('K-C25a 그림 도우미 · 깨끗 → 옮김 · 직전 걸림 → 안 찍음 · 직후 걸림 → 지움 · 지우기 실패 → 폴더 통째 · 창 집합 다름 → 안 옮김 · 봉인 → 안 찍음 · 임시 자리 = 업로드 밖', ok, why);
    }
    { // K-C25h 찍은 직후 죽음(대조 전 강제 종료 흉내) → 업로드 자리 0 · 임시 자리에만 남음
      const K = kKit();
      let ok = false;
      let why = '함수 없음';
      if (K && typeof K.stageDir === 'function') {
        const die = async (K2) => {
          const fs = require('fs');
          const kb = {
            browser: 'chromium', plantN4: false, sealed: false,
            n4: { verified: 0, shots: 0, blocked: 0, removed: 0, delFail: 0, hits: 0, errs: 0, winDiff: 0, moveFail: 0, planted: false },
          };
          const page = {
            isClosed: () => false,
            frames: () => [{ evaluate: async () => ({ text: '시험', vals: [], ph: [], pw: 0, pwOpen: 0 }) }],
            screenshot: async ({ path: f }) => { fs.writeFileSync(f, Buffer.from([0x89, 0x50])); process.exit(9); },
          };
          kb.page = page;
          kb.context = { pages: () => [page] };
          await K2.guardedShot(kb, 'kill');
          return { reached: true };
        };
        const x = kChild('k25h', die, { UI_PW_GA: K_SECRET });
        const up = path.join(x.o, 'out', 'main', 'shots', 'k35');
        const stage = path.join(x.o, 'ui-k35-stage', 'chromium');
        const upN = fs.existsSync(up) ? fs.readdirSync(up).length : 0;
        const stN = fs.existsSync(stage) ? fs.readdirSync(stage).length : 0;
        ok = x.code === 9 && upN === 0 && stN === 1;
        why = `rc=${x.code} · 업로드 자리 ${upN} · 임시 자리 ${stN}`;
      }
      check('K-C25h 찍은 직후 대조 전 죽음 → 업로드 자리 그림 0(임시 자리에만 1)', ok, why);
    }
    { // K-C25b N4 판정(순수) · 표지 먼저 · 예외 길 강제 거짓
      const K = kKit();
      let ok = false;
      let why = '함수 없음';
      if (K && typeof K.n4Verdict === 'function') {
        const fn = async (K2) => {
          const fs = require('fs');
          const path = require('path');
          const J = require(path.join(path.dirname(process.env.F_KIT), 'judge.js'));
          const envf = process.env.GITHUB_ENV;
          const read = () => (fs.existsSync(envf) ? fs.readFileSync(envf, 'utf8') : '');
          const base = () => ({ verified: 2, shots: 2, blocked: 0, removed: 0, delFail: 0, hits: 0, errs: 0, winDiff: 0, moveFail: 0, planted: false });
          const vv = {
            ok: K2.n4Verdict(base(), 2),
            leakHit: K2.n4Verdict({ ...base(), hits: 1 }, 2),
            leakDel: K2.n4Verdict({ ...base(), delFail: 1 }, 2),
            devTok: K2.n4Verdict(base(), 0),
            devNone: K2.n4Verdict({ ...base(), verified: 0 }, 2),
            devErr: K2.n4Verdict({ ...base(), errs: 1 }, 2),
            devWin: K2.n4Verdict({ ...base(), winDiff: 1 }, 2),
            mixed: K2.n4Verdict({ ...base(), hits: 1, errs: 1 }, 0),
            forced: K2.n4Verdict(base(), 2, true),
          };
          const r = { rows: [] };
          const kb = (b, n4, masked) => ({ browser: b, n4, tok: { masked }, sealed: false });
          const a = kb('chromium', base(), 2);
          K2.recN4(r, a);
          const m1 = read();
          const b = kb('webkit', base(), 2);
          K2.recN4(r, b, '시험');
          const m2 = read();
          fs.writeFileSync(envf, '');
          const c = kb('webkit', { ...base(), hits: 1 }, 2);
          let threw = null;
          try { K2.recN4(r, c); } catch (e) { threw = e.name; }
          const m3 = read();
          return {
            verdict: Object.fromEntries(Object.entries(vv).map(([k, x]) => [k, `${x.pass}/${x.why}`])),
            passNoMark: !/UI_N4_HIT=1/.test(m1) && r.rows[0].pass === true && r.rows[0].got.startsWith('N4 통과') && a.sealed === true,
            forced: /UI_N4_HIT=1/.test(m2) && r.rows[1].pass === false && r.rows[1].got.startsWith(J.N4_WHY.device) && b.sealed === true,
            markBeforeRec: threw === 'KitContractError' && /UI_N4_HIT=1/.test(m3),
          };
        };
        const envDir = fDir('k25b-env');
        const envFile = path.join(envDir, 'github_env');
        fs.writeFileSync(envFile, '');
        const x = kChild('k25b', fn, { GITHUB_ENV: envFile });
        const v = x.v || {};
        const want = { ok: 'true/null', leakHit: 'false/leak', leakDel: 'false/leak', devTok: 'false/device', devNone: 'false/device', devErr: 'false/device', devWin: 'false/device', mixed: 'false/leak', forced: 'false/device' };
        const vOk = !!v.verdict && Object.entries(want).every(([k, w]) => v.verdict[k] === w);
        ok = vOk && v.passNoMark === true && v.forced === true && v.markBeforeRec === true;
        why = x.v ? `판정 ${vOk ? '9/9' : JSON.stringify(v.verdict)} · 통과 = 표지 없음 ${v.passNoMark} · 강제 거짓 ${v.forced} · 표지가 기록보다 먼저 ${v.markBeforeRec}` : `rc=${x.code} · 결과 줄 없음 ${x.err}`;
      }
      check('K-C25b N4 판정 · 누출 신호 / 대조 못 함 · 예외 길 강제 거짓 · 표지 쓰기가 결과표 기록보다 먼저(기록이 던져도 표지 남음)', ok, why);
    }
    { // K-C25c 판정기 끝 줄 둘 — 대조 못 함만 → 장치 이유 끝 줄 / 섞이면 누출 신호 먼저 / 다른 [장치] 와 섞여도 재실행 1회 아님
      const DEV = 'N4 이유=대조 못 함(장치)'; // 판정기 상수와 따로 적는다(글자 잠금)
      const LEAK = 'N4 이유=누출 신호';
      const n4 = (b, got) => (w) => (w.g === 'N4' && w.browser === b ? { ...w, pass: false, got } : w);
      const devOff = (w) => (w.id === 'K35-G1' && w.path === 'bell' && w.browser === 'webkit' ? { ...w, kind: 'DEVICE', pass: false } : w);
      const a = kJ('k25c-1', [...fHRows(), ...fGRows().map(n4('webkit', `${DEV} · 가린 K 토큰 0`))]);
      const b = kJ('k25c-2', [...fHRows(), ...fGRows().map(n4('webkit', `${DEV} · x`)).map(n4('chromium', `${LEAK} · 걸림 1`))]);
      const c = kJ('k25c-3', [...fHRows(), ...fGRows().map(n4('webkit', `${DEV} · x`)).map(devOff)]);
      const d = kJ('k25c-4', [...fHRows(), ...fGRows().map(n4('webkit', `${LEAK} · x`))]);
      const la = kLast(a.out);
      const lb = kLast(b.out);
      const lc = kLast(c.out);
      const ld = kLast(d.out);
      const devLine = (l) => l.includes('대조 못 함') && !l.includes('누출 의심') && !l.includes('재실행 1회');
      const ok = a.code === 1 && devLine(la) && a.out.includes('[장치 · 대조 못 함]')
        && b.code === 1 && lb.includes('누출 의심 · 재실행 금지') && lb.includes('누출 신호 1') && !lb.includes('재실행 1회')
        && c.code === 1 && devLine(lc)
        && d.code === 1 && ld.includes('누출 의심 · 재실행 금지') && d.out.includes('[장치 · 누출 의심]');
      check('K-C25c N4 끝 줄 둘 · 대조 못 함만 → 장치 이유 · 섞이면 누출 신호 먼저 · 다른 [장치] 와 섞여도 재실행 1회 아님 · 누출 신호 표지', ok,
        `대조 못 함 rc=${a.code} · 섞임 rc=${b.code} · 장치 섞임 rc=${c.code} · 누출 rc=${d.code}`);
    }
    { // K-C25d 초기 스크립트 한 덩어리 — 막기 먼저 → 녹음기(막힌 호출도 기록) · 대조 = 뒤집으면 막힌 호출 기록 0 · K 본문 초기 스크립트 거는 줄 1
      const K = kKit();
      let ok = false;
      let why = '함수 없음';
      if (K && typeof K.INIT_SCRIPT === 'string') {
        const vm = require('vm');
        const DC = require(path.join(F_CI, 'device-check.js'));
        const runInit = (code) => {
          const store = new Map();
          const calls = [];
          const win = {
            open: (u, n) => { calls.push(n); return { fake: true }; },
            sessionStorage: { getItem: (k) => (store.has(k) ? store.get(k) : null), setItem: (k, v) => { store.set(k, String(v)); }, removeItem: (k) => { store.delete(k); } },
          };
          win.window = win;
          vm.runInNewContext(code, win);
          store.set('__k35block', '1');
          const blocked = win.open('/chat-window/a', 'hitpanChat');
          const listBlocked = win.__k35 ? win.__k35.list().length : -1;
          store.delete('__k35block');
          const opened = win.open('/chat-window/a', 'hitpanChat');
          return { blockedNull: blocked === null, listBlocked, origCalls: calls.length, opened: !!opened, list: win.__k35 ? win.__k35.list().length : -1 };
        };
        const good = runInit(K.INIT_SCRIPT);
        const rev = runInit(`${DC.RECORDER}\n${K.BLOCKER}`);
        const src = fs.readFileSync(path.join(F_CI, 'k35-popup.js'), 'utf8');
        const hooks = src.split('\n').filter((s) => /^\s*await kb\.context\.addInitScript\(/.test(s)).length;
        ok = good.blockedNull && good.listBlocked === 1 && good.origCalls === 1 && good.opened && good.list === 2
          && rev.blockedNull && rev.listBlocked === 0 && K.INIT_SCRIPT.indexOf(K.BLOCKER) === 0 && hooks === 1;
        why = `한 덩어리: 막힘 ${good.blockedNull} · 막힌 호출 기록 ${good.listBlocked} · 원래 열기 ${good.origCalls} · 기록 ${good.list} / 뒤집은 대조: 막힌 호출 기록 ${rev.listBlocked} · 거는 줄 ${hooks}`;
      }
      check('K-C25d 초기 스크립트 한 덩어리 · 막기 먼저 → 녹음기(막힌 호출 기록 1) · 뒤집으면 기록 0(대조) · 거는 줄 1', ok, why);
    }
    { // K-C25e G4 되돌림 판단(순수) · g4 가 상신 전에 되돌림을 부른다(글자 순서)
      const K = kKit();
      let ok = false;
      let why = '함수 없음';
      if (K && typeof K.g4Reset === 'function') {
        const h = '/home';
        const A = '/approval?scope=pending';
        const C = '/chat/r1';
        const r = [
          K.g4Reset(A, null, A).ok === false,
          K.g4Reset('/x', A, A).ok === false,
          (() => { const x = K.g4Reset(A, h, A); return x.ok && x.nav; })(), // 앞 경우가 목표에 남김(ⓑ 꼴) → 되돌림
          (() => { const x = K.g4Reset(h, h, A); return x.ok && !x.nav; })(),
          (() => { const x = K.g4Reset(C, h, A); return x.ok && x.nav; })(),
          K.g4StartOk(A, h, A) === false,
          K.g4StartOk(h, h, A) === true,
          K.g4StartOk('/y', h, A) === false,
          K.g4StartOk(h, h, h) === false,
        ];
        const src = fs.readFileSync(path.join(F_CI, 'k35-popup.js'), 'utf8');
        const body = src.slice(src.indexOf('async function g4('), src.indexOf('// ── G5'));
        const live = body.split('\n').filter((s) => !/^\s*\/\//.test(s)).join('\n');
        const order = live.indexOf('toNeutral(') > 0 && live.indexOf('toNeutral(') < live.indexOf('submitApproval(');
        ok = r.every((x) => x === true) && order;
        why = `판단 ${r.filter((x) => x).length}/${r.length} · 상신 전 되돌림 ${order}`;
      }
      check('K-C25e G4 되돌림 판단 · 중립 모름/중립 = 목표 → 진행 안 함 · 목표에 남은 시작 → 되돌림 · 시작 = 목표 → 거짓 · 상신 전 되돌림', ok, why);
    }
    { // K-C25f 콘솔 줄 쌓기 → 찍을 때 그 시점 등록 목록으로 소독(대조 = 등록 전 소독은 못 가림) · 받는 자리에선 안 찍음
      const K = kKit();
      let ok = false;
      let why = '함수 없음';
      if (K && typeof K.flushConsole === 'function') {
        const fn = async (K2) => {
          const path = require('path');
          const L2 = require(path.join(path.dirname(process.env.F_KIT), 'lib.js'));
          const v = process.env.K25F_VALUE;
          const lines = [];
          const orig = console.log;
          console.log = (s) => { lines.push(String(s)); };
          const handlers = {};
          const fakePage = { on: (ev, cb) => { handlers[ev] = cb; } };
          const kb = { browser: 'chromium', consoleLines: 0 };
          K2.watchConsole({ pages: () => [fakePage], on: () => {} }, kb);
          handlers.console({ type: () => 'error', text: () => `tok ${v} end` });
          const printedOnReceive = lines.length;
          const before = K2.consoleLine(`x ${v} y`);
          L2.maskAndRegister('TOKEN_ACCESS_K_BROWSER', v);
          const n = K2.flushConsole(kb);
          console.log = orig;
          return { printedOnReceive, n, beforeHas: before.includes(v), outHas: lines.some((s) => s.includes(v)), masked: lines.some((s) => s.includes('***')), empty: kb.consoleBuf.length === 0 };
        };
        const x = kChild('k25f', fn, { K25F_VALUE: `SELFTEST_K25F_${hex()}${hex()}` });
        const v = x.v || {};
        ok = !!x.v && v.printedOnReceive === 0 && v.n === 1 && v.beforeHas === true && v.outHas === false && v.masked === true && v.empty === true;
        why = x.v ? `받을 때 찍음 ${v.printedOnReceive} · 쌓인 줄 ${v.n} · 등록 전 소독에 값(대조) ${v.beforeHas} · 찍은 줄에 값 ${v.outHas} · 가림 ${v.masked} · 비움 ${v.empty}` : `rc=${x.code} · 결과 줄 없음 ${x.err}`;
      }
      check('K-C25f 콘솔 줄 쌓기 → 찍을 때 소독(뒤에 등록한 토큰도 가림) · 받을 때 안 찍음 · 대조 = 등록 전 소독은 못 가림', ok, why);
    }
    { // K-C25g 시간 상한 도우미 · 상한 값 잠금(K 전체 < S8 단계 < 잡 · 화면 글자 모으기에 상한)
      const K = kKit();
      let ok = false;
      let why = '함수 없음';
      if (K && typeof K.withTimeout === 'function') {
        const t1 = await K.withTimeout(new Promise(() => {}), 50, '시험').then(() => 'resolved', (e) => `${e.name}:${/시간 상한/.test(e.message)}`);
        const t2 = await K.withTimeout(Promise.resolve(7), 1000, '시험').catch((e) => e.name);
        const t3 = await K.withTimeout(Promise.reject(new TypeError('z')), 1000, '시험').catch((e) => e.name);
        const pop = fs.readFileSync(path.join(F_CI, 'k35-popup.js'), 'utf8');
        const kit = fs.readFileSync(path.join(F_CI, 'kit35.js'), 'utf8');
        const yml = fs.readFileSync(path.join(F_CI, '..', '..', '..', '.github', 'workflows', 'ui-browser.yml'), 'utf8');
        const kMin = Number((pop.match(/^const K_TOTAL_MS = (\d+) \* 60 \* 1000;/m) || [])[1] || NaN);
        const s8 = yml.slice(yml.indexOf('[시험] S8'), yml.indexOf('[시험] S9'));
        const s8Min = Number((s8.match(/^\s*timeout-minutes: (\d+)\s*$/m) || [])[1] || NaN);
        const job = yml.slice(yml.indexOf('  ui-k35:'), yml.indexOf('  ui-selftest:'));
        const jobMin = Number((job.match(/^ {4}timeout-minutes: (\d+)\s*$/m) || [])[1] || NaN);
        const scanCap = /withTimeout\(f\.evaluate\(COLLECT\), EVAL_MS/.test(kit);
        ok = t1 === 'KitStepError:true' && t2 === 7 && t3 === 'TypeError' && kMin < s8Min && s8Min < jobMin && scanCap;
        why = `상한 ${t1} · 통과 ${t2} · 거절 그대로 ${t3} · K 전체 ${kMin}분 < S8 ${s8Min}분 < 잡 ${jobMin}분 · 화면 글자 모으기 상한 ${scanCap}`;
      }
      check('K-C25g 시간 상한 도우미(상한 → KitStepError · 통과 · 거절 그대로) · K 전체 < S8 단계 < 잡 · 화면 평가 상한', ok, why);
    }
    // ── 🔴 R1 · R2(11/7 · 작업지시서 20261107작 §6 · §9 P2 · P6) — 그림 전후 대조 등록 목록 시점 · 등록 대기 · 화면 예외 이름 찍는 시점 재소독 ──
    //   K-C25i R1 칸 C0 · C1(대조군) · C2a · C2b · C3 · 오탐 대조군 · C5(찍는 중 대기 → 지움 · 대기 중 시작 → 안 찍음) · C5 대조군(대기 0 → 옮김) · n4Verdict regGrow
    //   K-C25j R2 칸 D0(대조군) · D1 · D2 · 오탐 대조군 TypeError
    //   자체시험 수 = 바닥 88fa8abb 로컬 실측 121 → 123(C5 칸은 K-C25i 안 · 새 검사 2). 못 재는 것(미측정): 실제 브라우저 촬영 창 안 갱신 응답 빈도 · 그림 픽셀 글자.
    const kRegWorld = async (K) => {
      const fs = require('fs');
      const path = require('path');
      const L2 = require(path.join(path.dirname(process.env.F_KIT), 'lib.js'));
      const val = (tag) => `${process.env.K25I_VALUE}_${tag}`;
      const clean = () => ({ text: '시험 나 · 새 메시지', vals: [], ph: [], pw: 0, pwOpen: 0 });
      const show = (v) => ({ text: `화면 ${v}`, vals: [], ph: [], pw: 0, pwOpen: 0 });
      const mkPage = (st) => ({
        isClosed: () => false,
        frames: () => [{ evaluate: async () => { st.calls += 1; return st.collect(st.calls); } }],
        screenshot: async ({ path: f }) => st.shot(f),
      });
      const writePng = (f) => fs.writeFileSync(f, Buffer.from([0x89, 0x50, 0x4e, 0x47, 9]));
      const mkKb = (st) => {
        const page = mkPage(st);
        return {
          browser: 'chromium', plantN4: false, sealed: false, page,
          context: { pages: () => [page] },
          n4: { verified: 0, shots: 0, blocked: 0, removed: 0, delFail: 0, hits: 0, errs: 0, winDiff: 0, moveFail: 0, planted: false },
        };
      };
      const up = K.shotsDir();
      const stage = K.stageDir('chromium');
      const has = (d, n) => fs.existsSync(path.join(d, n));
      const rg = (kb) => kb.n4.regGrow || 0;
      const moved = (ret, kb, n) => ret === true && has(up, `${n}-chromium.png`) && !has(stage, `${n}-chromium.png`) && kb.n4.verified === 1 && rg(kb) === 0;
      const gone = (ret, kb, n) => ret === false && !has(up, `${n}-chromium.png`) && !has(stage, `${n}-chromium.png`) && kb.n4.removed === 1 && kb.n4.verified === 0;
      const run = async (n, st, before) => {
        const kb = mkKb(st);
        if (before) before();
        const ret = await K.guardedShot(kb, n);
        return { ret, kb };
      };
      const out = {};
      { // C0 깨끗 · 등록 없음 → 옮김
        const { ret, kb } = await run('i0', { calls: 0, collect: clean, shot: writePng });
        out.c0 = moved(ret, kb, 'i0');
      }
      { // C1 시작 전 등록 + 화면 → 안 찍음(대조군)
        const v = val('C1');
        const st = { calls: 0, collect: () => show(v), shot: writePng };
        const { ret, kb } = await run('i1', st, () => L2.maskAndRegister('TOKEN_ACCESS_K_BROWSER', v));
        out.c1 = ret === false && !has(up, 'i1-chromium.png') && !has(stage, 'i1-chromium.png') && kb.n4.blocked === 1 && kb.n4.hits === 1 && rg(kb) === 0;
      }
      { // C2a 찍는 도중 등록 + 직후 화면에 남음 → 지움(다시 모은 목록으로 걸림)
        const v = val('C2A');
        const st = { calls: 0, collect: (k) => (k >= 2 ? show(v) : clean()), shot: (f) => { writePng(f); L2.maskAndRegister('TOKEN_ACCESS_K_BROWSER', v); } };
        const { ret, kb } = await run('i2a', st);
        out.c2a = gone(ret, kb, 'i2a') && kb.n4.hits === 1;
      }
      { // C2b 찍는 도중 등록 + 직후 화면에서 사라짐 → 지움(등록 늘어남)
        const v = val('C2B');
        const st = { calls: 0, collect: clean, shot: (f) => { writePng(f); L2.maskAndRegister('TOKEN_REFRESH_K_BROWSER', v); } };
        const { ret, kb } = await run('i2b', st);
        out.c2b = gone(ret, kb, 'i2b') && kb.n4.hits === 0 && rg(kb) === 1;
      }
      { // C3 목록 만든 뒤 · 직전 대조 첫 evaluate 안에서 등록 → 지움(등록 늘어남)
        const v = val('C3');
        const st = { calls: 0, collect: (k) => { if (k === 1) L2.maskAndRegister('TOKEN_ACCESS_K_BROWSER', v); return clean(); }, shot: writePng };
        const { ret, kb } = await run('i3', st);
        out.c3 = gone(ret, kb, 'i3') && rg(kb) === 1;
      }
      { // 오탐 대조군 — 등록 없이 도중 화면 글자만 바뀜 → 옮김 유지
        const st = { calls: 0, collect: (k) => ({ ...clean(), text: `시험 화면 ${k}` }), shot: writePng };
        const { ret, kb } = await run('i4', st);
        out.noFalse = moved(ret, kb, 'i4');
      }
      // C5(P2) — 응답 손잡이(watchTokens)가 본문 읽기 await 중
      const handlers = [];
      K.watchTokens({ on: (ev, cb) => { if (ev === 'response') handlers.push(cb); } }, { access: null, masked: 0, deviceId: null });
      const hold = () => {
        let done = null;
        const p = new Promise((r) => { done = r; });
        return { p, done: () => done({}) };
      };
      const fire = (h) => handlers[0]({ url: () => 'http://127.0.0.1:18080/api/auth/refresh', json: () => h.p });
      const settle = () => new Promise((r) => setTimeout(r, 20));
      { // C5a 찍는 중 응답 도착(대기 중) → 직후 대조 뒤 대기 수 1 → 지움
        const h = hold();
        const st = { calls: 0, collect: clean, shot: (f) => { writePng(f); fire(h); } };
        const { ret, kb } = await run('i5', st);
        h.done();
        await settle();
        out.c5shot = gone(ret, kb, 'i5') && rg(kb) === 1;
      }
      { // C5b 대기 중 시작 → 직전 대조 앞에서 안 찍음
        const h = hold();
        fire(h);
        const st = { calls: 0, collect: clean, shot: writePng };
        const { ret, kb } = await run('i6', st);
        h.done();
        await settle();
        out.c5pre = ret === false && st.calls === 0 && !has(up, 'i6-chromium.png') && !has(stage, 'i6-chromium.png') && kb.n4.blocked === 1 && rg(kb) === 1;
      }
      { // C5 대조군 — 대기 0(위 응답 끝난 뒤) → 옮김
        const { ret, kb } = await run('i7', { calls: 0, collect: clean, shot: writePng });
        out.c5zero = moved(ret, kb, 'i7');
      }
      const base = () => ({ verified: 2, shots: 2, blocked: 0, removed: 0, delFail: 0, hits: 0, errs: 0, winDiff: 0, moveFail: 0, planted: false });
      const a = K.n4Verdict({ ...base(), regGrow: 1 }, 2);
      const b = K.n4Verdict(base(), 2);
      out.verdict = `${a.pass}/${a.why} ${b.pass}/${b.why}`;
      return out;
    };
    { // K-C25i R1 그림 전후 대조 등록 목록 시점 · 등록 대기(C5)
      const K = kKit();
      let ok = false;
      let why = '함수 없음';
      if (K && typeof K.guardedShot === 'function' && typeof K.watchTokens === 'function') {
        const x = kChild('k25i', kRegWorld, { K25I_VALUE: `SELFTEST_K25I_${hex()}${hex()}` });
        const v = x.v || {};
        const names = ['c0', 'c1', 'c2a', 'c2b', 'c3', 'noFalse', 'c5shot', 'c5pre', 'c5zero'];
        ok = !!x.v && names.every((n) => v[n] === true) && v.verdict === 'false/device true/null';
        why = x.v ? `${names.map((n) => `${n} ${v[n]}`).join(' · ')} · 판정 ${v.verdict}` : `rc=${x.code} · 결과 줄 없음 ${x.err}`;
      }
      check('K-C25i R1 깨끗 → 옮김 · 시작 전 등록 → 안 찍음(대조군) · 찍는 도중 등록(화면 남음 · 사라짐) → 지움 · 직전 대조 중 등록 → 지움 · 등록 없는 화면 변화 → 옮김 · 응답 읽는 중 → 지움/안 찍음 · 대기 0 → 옮김 · regGrow → 대조 못 함', ok, why);
    }
    { // K-C25j R2 화면 예외 이름 — 받을 때 + 찍는 그 시점 재소독
      const K = kKit();
      let ok = false;
      let why = '함수 없음';
      if (K && typeof K.flushConsole === 'function') {
        const fn = async (K2) => {
          const path = require('path');
          const L2 = require(path.join(path.dirname(process.env.F_KIT), 'lib.js'));
          const b = process.env.K25J_VALUE;
          const v0 = `${b}D0`;
          const v1 = `${b}D1`;
          const v2 = `${b}D2`;
          const lines = [];
          const orig = console.log;
          console.log = (s) => { lines.push(String(s)); };
          const handlers = {};
          const fakePage = { on: (ev, cb) => { handlers[ev] = cb; } };
          const kb = { browser: 'chromium', consoleLines: 0 };
          K2.watchConsole({ pages: () => [fakePage], on: () => {} }, kb);
          const mkErr = (n) => { const e = new Error('x'); e.name = n; return e; };
          const tail = (a) => {
            if (a.length !== 1) return `줄 ${a.length}`;
            if (a[0].endsWith(' TypeError')) return 'TypeError';
            return a[0].endsWith(' Error') ? 'Error' : '기타';
          };
          L2.maskAndRegister('TOKEN_ACCESS_K_BROWSER', v0); // D0 등록 뒤 예외
          handlers.pageerror(mkErr(v0));
          K2.flushConsole(kb);
          const d0 = lines.splice(0);
          handlers.pageerror(mkErr(v1)); // D1 예외 뒤 등록 → 찍기
          const heldRaw = kb.consoleBuf.length === 1 && kb.consoleBuf[0].name === v1; // 받을 때는 아직 모르는 값(대조)
          L2.maskAndRegister('TOKEN_ACCESS_K_BROWSER', v1);
          K2.flushConsole(kb);
          const d1 = lines.splice(0);
          handlers.console({ type: () => 'error', text: () => `tok ${v2} end` }); // D2 콘솔 본문 같은 순서
          L2.maskAndRegister('TOKEN_REFRESH_K_BROWSER', v2);
          K2.flushConsole(kb);
          const d2 = lines.splice(0);
          handlers.pageerror(mkErr('TypeError')); // 오탐 대조군
          K2.flushConsole(kb);
          const d3 = lines.splice(0);
          console.log = orig;
          return {
            d0: tail(d0), d0Has: d0.some((s) => s.includes(v0)), heldRaw,
            d1: tail(d1), d1Has: d1.some((s) => s.includes(v1)),
            d2Masked: d2.some((s) => s.includes('***')), d2Has: d2.some((s) => s.includes(v2)), d3: tail(d3),
          };
        };
        const x = kChild('k25j', fn, { K25J_VALUE: `SELFTEST_K25J_${hex()}` });
        const v = x.v || {};
        ok = !!x.v && v.d0 === 'Error' && v.d0Has === false && v.heldRaw === true && v.d1 === 'Error' && v.d1Has === false
          && v.d2Masked === true && v.d2Has === false && v.d3 === 'TypeError';
        why = x.v ? `D0 ${v.d0} · 값 ${v.d0Has} / D1 받을 때 원래 이름(대조) ${v.heldRaw} · 찍음 ${v.d1} · 값 ${v.d1Has} / D2 가림 ${v.d2Masked} · 값 ${v.d2Has} / 대조군 ${v.d3}` : `rc=${x.code} · 결과 줄 없음 ${x.err}`;
      }
      check('K-C25j R2 화면 예외 이름 · 등록 뒤 예외 → Error(대조군) · 예외 뒤 등록 → 찍을 때 Error · 콘솔 본문 같은 순서 → 가림 · 정상 이름 TypeError 그대로', ok, why);
    }
    // ── 🔴 K 연결 4차(첫 K 런 뒤) — 종 길 진단 글(순수) ──
    //   K-C26a 사유 = 고정 낱말 + 가로막은 요소 태그·클래스만(원문 줄 0 · 제어 글자 0 · 길이 상한) · 숫자 = 정수만(글자 값 → ?)
    //   K-C26b 사유 소독 = 등록값 가림(자식 프로세스) · 항목 클릭 실패 뒤 다시 누르지 않음(글자 · 주석 뺀 줄)
    //   못 재는 것(미측정): 실제 MudBlazor 메뉴 열림 · 실제 Playwright 오류 글 꼴(다음 K 런 got 으로 확인).
    { // K-C26a 종 길 사유 · 숫자 글(순수)
      const K = kKit();
      let ok = false;
      let why = '함수 없음';
      if (K && typeof K.bellWhy === 'function' && typeof K.bellStepMessage === 'function') {
        const ctlRe = /[\u0000-\u001f\u007f-\u009f\u2028\u2029]/;
        const icp = 'locator.click: Timeout 8000ms exceeded.\nCall log:\n  - waiting for locator(\'x\')\n  -   <div tabindex="-1" class="mud-overlay mud-overlay-dialog q\u0007z bad<>">…</div> intercepts pointer events\n';
        const w1 = K.bellWhy(icp);
        const w2 = K.bellWhy('locator.waitFor: Timeout 6000ms exceeded.\n  - element is not visible');
        const w3 = K.bellWhy('zzz');
        const w4 = K.bellWhy(`TimeoutError <span class="${'a'.repeat(300)} b c d e f">x</span> intercepts pointer events`, 20);
        const w5 = K.bellWhy(`${'\u2028'}TimeoutError hasText "SELFTEST_ITEM_${hex()}"\u0000`);
        const m1 = K.bellStepMessage('항목 보임', w2, 2, { bell: 1, act: 1, menus: 1, items: '새 메시지 원문', allItems: 3, overlays: 0, dialogs: 0, top: 1 });
        const m2 = K.bellStepMessage('아무거나', w3, 'x', { err: 'not a name!' });
        const r = [
          /^시간초과·가로막음|^가로막음·시간초과/.test(w1), w1.includes('div.mud-overlay.mud-overlay-dialog'), !ctlRe.test(w1), !/[<>"']/.test(w1), !w1.includes('locator'),
          w2 === '안 보임·시간초과', w3 === '기타',
          w4.length <= 20 + '…(이하 생략)'.length, !w4.includes('a'.repeat(41)),
          !ctlRe.test(w5), !w5.includes('SELFTEST_ITEM_'),
          m1.includes('하위=항목 보임') && m1.includes('항목 ?') && !m1.includes('새 메시지 원문') && m1.includes('열기 2번') && m1.includes('종맨위 1'),
          m2.includes('하위=?') && m2.includes('열기 ?번') && m2.includes('숫자 못 셈(Error)'),
        ];
        ok = r.every((x) => x === true);
        why = `통과 ${r.filter((x) => x).length}/${r.length} · 가로막음 꼴 ${JSON.stringify(w1)} · 길이 상한 ${w4.length}`;
      }
      check('K-C26a 종 길 사유 = 고정 낱말 + 가로막은 요소 태그·클래스만(원문 0 · 제어 글자 0 · 길이 상한) · 숫자 = 정수만 · 하위 단계 이름 고정', ok, why);
    }
    { // K-C26b 사유 소독(등록값) · 항목 클릭 실패 뒤 다시 누르지 않음
      const K = kKit();
      let ok = false;
      let why = '함수 없음';
      if (K && typeof K.bellWhy === 'function') {
        const fn = async (K2) => {
          const path = require('path');
          const L2 = require(path.join(path.dirname(process.env.F_KIT), 'lib.js'));
          const v = process.env.K26B_VALUE;
          const msg = `TimeoutError\n  - <div class="mud-x ${v}">…</div> intercepts pointer events`;
          const before = K2.bellWhy(msg, 200);
          L2.maskAndRegister('TOKEN_ACCESS_K_BROWSER', v);
          const after = K2.bellWhy(msg, 200);
          const m = K2.bellStepMessage('종 클릭', after, 1, null);
          return { beforeHas: before.includes(v), afterHas: after.includes(v), masked: after.includes('***'), msgHas: m.includes(v), noCount: m.includes('숫자 못 셈') };
        };
        const x = kChild('k26b', fn, { K26B_VALUE: `SELFTEST_K26B_${hex()}${hex()}` });
        const v = x.v || {};
        const src = fs.readFileSync(F_KIT, 'utf8');
        const body = src.slice(src.indexOf('async function clickBellItem('), src.indexOf('async function toastTexts('));
        const live = body.split('\n').filter((s) => !/^\s*\/\//.test(s)).join('\n');
        const noRepeat = /if \(sub === '항목 클릭'\) break;/.test(live) && live.indexOf('arm()') > 0 && live.indexOf('arm()') < live.indexOf('item.click(');
        ok = !!x.v && v.beforeHas === true && v.afterHas === false && v.masked === true && v.msgHas === false && v.noCount === true && noRepeat;
        why = x.v ? `등록 전 값(대조) ${v.beforeHas} · 등록 뒤 값 ${v.afterHas} · 가림 ${v.masked} · 글에 값 ${v.msgHas} · 숫자 없음 표시 ${v.noCount} · 항목 클릭 뒤 다시 안 함 · 팝업 기다림 클릭 바로 전 ${noRepeat}` : `rc=${x.code} · 결과 줄 없음 ${x.err}`;
      }
      check('K-C26b 종 길 사유 소독(등록값 가림 · 대조 = 등록 전엔 보임) · 항목 클릭 실패 뒤 다시 안 누름 · 팝업 기다림은 항목 클릭 바로 전', ok, why);
    }
  }
  const bad = results.filter((x) => !x.ok).length;
  console.log(`합계 ${results.length - bad}/${results.length} PASS`);
  process.exit(bad === 0 ? 0 : 1);
})().catch((e) => {
  console.log(`FAIL C15 시험 자체 예외 — ${L.errorName(e)}`);
  process.exit(1);
});
