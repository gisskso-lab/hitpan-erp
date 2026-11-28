// K 본문 — W-S-03 서버 문 닫기 · 첫 로그인 · 휴대폰 화면 회귀 (W-S-03 고침 1 · [4] P2-1 · 개발명세서 docs/개발/erp/20261128_W-S-03_서버문닫기_온보딩모바일_개발명세서.md 「고침 1」)
//
// 🔴 이 파일의 규칙(k35-popup.js · 쿨타임 몸과 같은 꼴)
//   ① 기록은 kit35.recK 하나로만 — 키 = judge.js EXPECT_S3_ROWS(기대 키 7 · 전부 시험 줄). 밖 = 던짐.
//   ② 판정은 주소(문서 이동 기록) · 로그인 응답 칸(참거짓만) · 화면 글자 · 서버 응답 코드 · 시험 DB 행 수로만.
//      단언 전 실패(새 직원 만들기 · 로그인 · 첫 로그인 상태 재현 · 시간초과) = 그 키에 DEVICE 거짓.
//   ③ 그림을 찍지 않는다(N4 줄 없음) — 증거 = 결과표 결과 칸.
//   ④ 닫힌 두 서버 문(POST api/devices/mobile-token · mobile-register)의 상태 숫자 · 본문 꼴은 **기록만** 한다(단정 0 · §321 · W-S-01 405 선례).
//      판정 = 처리기 도달 0 — 시험 DB 의 등록 토큰 표 · 기기 표 행 변화 0 · 심어 둔 토큰이 안 쓰임 · 처리기 안내 글 0.
//   ⑤ 첫 로그인 상태는 시험 DB(CI 컨테이너 · 실물 아님)에서 새 직원의 마지막 로그인 칸을 비워 다시 만든다 — 약관은 미리 동의해 둔다
//      (약관 미동의 첫 로그인 → /terms 는 기존 장면 H-D2 약관 화면 길이 잰다 · 이 몸은 중복으로 만들지 않는다).
//   ⑥ 연 세션은 장면 끝 finally 에서 반드시 로그아웃(§255 · 계정 잠김 사고 선례) · 브라우저를 닫는다.
//   ⑦ 이 PC 에서 돌리지 않는다(이 PC DB = 실물) — CI 의 S8 에서만. 이름 = 다른 K 몸 뒤에 돈다(알파벳 순).
//   ⑧ 고침 3 — 알려진 결함 대장(judge.js KNOWN_DEFECT_ROWS · 첫 화면 2 장면) 꼴일 때만 결과 칸 머리에 표지 · 끝 코드는 판정기 면제 함수를 쓴다(endCode).
//   ⑨ 🔴 20261128작 로그인 경합 · 첫 화면 403 띠 갈래 T(설계 docs/설계/erp/로그인경합_첫화면403띠_설계_20261128.md §10 · §7-1-7 ·
//      작업지시서 docs/운영기록/20261128작_로그인경합_첫화면403띠_작업지시서.md §2 갈래 T · §8-2 ~ §8-5 · 개발명세서 …_갈래T_개발명세서.md)
//      — 대장 2 키 닫음(판정기 목록 0 · 두 장면은 면제 없이 판정). 시험 8 더함(기대 키 7 → 15). 판정은 순수 함수 JUDGE(자체시험 K-C28a)로만.
//      문서 이동 = 주 프레임 문서 요청(경로=끝) · 흰 띠 = 착지 +3초 · +10초 두 번 · 앱 표준오류 꼴 = 「[」 로 시작하는 console.error.
//      관찰 장면 꼴(측정 M 754b03ff 의 O1 · T1 · D1)은 판정 장면으로 다시 씀(로그 줄 진단은 옮기지 않음).
//   ⑩ 🔴 20261128작 갈래 B2 · 시험 고침(설계 §7-2-3 · §7-2-4 · 작업지시서 §8-8 D17 · D18) — 「보이는 403 알림 0」 을 세 겹으로 잰다:
//      ⓐ 403 응답 수 f403n(목록 20 상한과 별개) · ⓑ 처리기 평소 403 갈래 경고 줄(「403 Forbidden on」 · console.warn) h403 ·
//      ⓒ 뜬 적 있는 알림 글 전부(초기 스크립트 MutationObserver · 찍기 사이에 사라진 것까지) 중 처리기 글 forbidSeen.
//      직원 4장면(first-home · first-update · device-dialog-20s · sales-home) 판정에 셋 다 0 · 같은 런 양성 대조 = 새 키 owner-json(대표 창 ·
//      결재 대기 주소만 403 가로챔 → h403 ≥ 1 · forbidSeen ≥ 1). 기대 키 15 → 16(판정기 33 → 34). 기록만(판정 아님) = 빈 시간 · Login 진행 그림.
//   ⑪ 🔴 HQA 보류 고침 · 물러남(작업지시서 §8-13 · 설계 docs/설계/erp/로그인경합_고침2_정적칸남음_설계_20261129.md §7-2 · PM 결재 E4 + 덧)
//      — 시험 3 더함(기대 키 16 → 19 · 판정기 41 → 44): K35-S3-10 stop-after-login(길 A2 · CDP Page.stopLoading) · back-restore(길 B · 복원 캐시 켠
//      브라우저 + 뒤로 가기) · K35-S3-5 terms-slow-prog(기존 terms-slow-doc 의 「늦춤 중 +5초 진행 그림」 기록을 판정으로 올림 — 변이 Y2 가 CI 에서도 빨강).
//      두 새 장면의 꼴 = 측정 가지 meas/20261129-1032-정적칸남음 의 m1032-measure.js(holdDocs · sceneStop · sceneBack)를 정규 K 장면으로.
//      끝 = 고치기 전 꼴(빈 로그인 입력 화면 · 막대 0 · 아이디 칸 있음 · 아이디 · 비밀번호 넣으면 「로그인」 단추 켜짐 — 누르지 않음) · 새 문서 0(덮어쓰기 0).
//      장치 불성립(중지 응답 실패 · 붙잡은 문서 취소 안 됨 · 복원 안 됨 · pageshow 못 봄) = 그 키 DEVICE 거짓(통과로 안 셈).
//      계정 자리: 새 계정 0 — stop-after-login = 약관 미동의 직원(TERMS_ID) · back-restore = 준비 직원(prep · relogin-home 과 같은 직원 · 치우기 전에 돈다).
'use strict';

const crypto = require('crypto');
const { spawnSync } = require('child_process');
const L = require('./lib');
const K = require('./kit35');
const DC = require('./device-check'); // 머리 조건(require.main) 때문에 불러도 장치 본문은 안 돈다 — 내보냄만 쓴다
// 🔴 고침 3(작업지시서 20261128작_W-S-03_고침3_알려진결함대장 §4 ②) — 알려진 결함 목록 · 표지 · 면제 함수는 판정기 것 그대로(머리 조건 때문에 판정 본문은 안 돈다)
const J = require('./judge');

const env = process.env;
const BROWSER = 'chromium';
const ID = { parent: env.UI_LOGIN_PARENT || 'ci-parent', fresh: 'ci-s3new' };
const PW = { parent: env.UI_PW_PARENT || '' };
const FRESH_NAME = '시험 새직원';
// kit35 의 UA 와 같은 글자(kit35 가 내보내지 않아 옮겨 적음 · 시드 · K 몸 API 로그인과 같은 기기 꼴)
const API_UA = 'Mozilla/5.0 (X11; Linux x86_64) HitPanCiSeed/1';
const PHONE_UA = 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148 HitPanCiPhone/1';
// 제품 글자(따로 적어 잠금) — 바뀌면 이 몸이 빨강이 되어 알린다
const NOTICE_TITLE = '이 화면은 정식 판에서 열립니다'; // BetaClosedRoutes.NoticeTitle
const WELCOME_OLD = '온보딩(Phase 2'; // Welcome.razor 옛 글 — 보이면 옛 환영 화면이 살아남
const PHONE_NOTICE = '이제 등록 없이 휴대폰에서 로그인하시면 됩니다.'; // MobileDeviceRegisterPage.razor 안내
const PHONE_OLD_ASK = '모바일 기기 등록을 하시겠습니까?'; // 옛 QR 등록 화면 물음 — 보이면 옛 화면이 살아남
const UPDATE_NOTICE = '「최신 버전 확인/업데이트」 화면으로 바로 이동합니다'; // LocalSwapUiText.LoginButtonNotice 일부
const UPDATE_BTN = /^최신버젼업데이트/; // Login.razor 수동 업데이트 단추(문구가 상태에 따라 꼬리만 바뀜)
const HOME_PATH = '/'; // LocalSwapUiText.PostLoginPath(false)
const UPDATE_PATH = '/data/update'; // LocalSwapUiText.UpdatePath = PostLoginPath(true)
const WELCOME_PATH = '/welcome'; // BetaClosedRoutes.Welcome
// 처리기가 돌았다는 표지(DeviceController 의 두 액션이 내는 응답 글자 · 칸 이름)
const HANDLER_WORDS = ['등록 정보가 올바르지 않습니다', '만료되었거나 이미 사용된 코드', 'qrImage', 'expiresInMinutes'];
// ⬛ [낡은 줄 · 고침 3] const S3_TOTAL_MS = 7 * 60 * 1000; // 이 몸 전체 상한(워크플로 S8 단계 상한 안)
// 🔴 갈래 T — 화면 로그인 장면 7 더함(그중 20초 기다림 2) → 16분(워크플로 S8 27 → 36 · 잡 37 → 46 · K-C25g 순서 그대로)
const S3_TOTAL_MS = 16 * 60 * 1000; // 이 몸 전체 상한(워크플로 S8 단계 상한 안)
const TEXT_MS = 30000;
// ── 🔴 갈래 T 상수(제품 글자는 따로 적어 잠금 — 바뀌면 이 몸이 빨강이 되어 알린다) ──
const TERMS_ID = 'ci-s3terms'; // 약관 미동의 직원(측정 M T1 과 같은 만들기 길 · 약관 동의만 건너뜀)
const TERMS_NAME = '시험 약관미동의';
const TERMS_PATH = '/terms';
const LOGIN_PATH = '/login';
const DEVICE_NOTICE_TITLE = '기기 등록 안내'; // Login.razor 새 기기 알림 제목(device-check.js 와 같은 글자)
const DASH_FAIL_TEXT = '대시보드 정보를 불러오지 못했습니다'; // Dashboard.razor LoadDashboardAsync 쉬운 알림(갈래 B5)
const DASH_WARN_TAG = '[Dashboard.LoadDashboardAsync]'; // 같은 자리 ILogger 경고 글 머리(갈래 B2 · 띠 안 켬 · 흔적은 남음 = #15)
const OWNER_LABELS = ['재고 자산', '이번달 매입', '미수금']; // Dashboard.razor KPI 보조 카드 이름(판매·재무 묶음)
const SALES_FIN_PATHS = ['/api/finance/dashboard', '/api/partners/aging']; // 판매·재무 카드가 부르는 두 주소(SalesOnly)
const SALES_ROLE = 'sales_user'; // 서버 SalesOnly · 화면 거름 목록 안 역할(EmployeeService 기본 역할과 같은 값)
const HOLD_MS = 20000; // 장면 G 줄 둘 — 알림 열어 둠 · 문서 요청 늦춤(안전망 15초보다 길게)
// 새 기기 꼴 — 지문 재료(device-fingerprint.js _envSeed = 화면 크기 · 언어 · 시간대 · UA 숫자 뺀 글자)가 앞 장면들과 다른 창(측정 M D1 그대로 · 새 기기=참 실측)
const D1_UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36 HitPanCiNewDevice/1';
const D1_CTX = { viewport: { width: 1366, height: 700 }, screen: { width: 1366, height: 768 }, userAgent: D1_UA, locale: 'ko-KR', timezoneId: 'Asia/Seoul' };
// 보이는 알림 글의 날 예외 꼴(G7 — 「Exception」 · 영문 예외 이름)
// ⬛ [낡은 줄 · 갈래 B2 이전] const SNACK_BAD = /Exception|\b[A-Z][A-Za-z]*Error\b/;
// 🔴 갈래 B2(D18 · 설계 §7-2-4 R-③) — 영문 상태 글 · JSON 해석 글 꼴 · 영어 낱말 4개 이상 잇달음도 날 글로 본다(자체시험 K-C29a)
const SNACK_BAD = /Exception|\b[A-Z][A-Za-z]*Error\b|does not indicate success|status code|Path: \$|LineNumber|BytePosition|invalid start|[A-Za-z][A-Za-z']*(?:[\s,.:;()-]+[A-Za-z][A-Za-z']*){3,}/;
// ── 🔴 갈래 B2 상수(제품 글자는 따로 적어 잠금 — 바뀌면 이 몸이 빨강이 되어 알린다) ──
const FORBID_TEXT = '이 기능에 접근할 권한이 없습니다'; // HitPanApiAuthHandler 평소 403 갈래 알림 글 머리
const H403_TAG = '403 Forbidden on'; // 같은 갈래 ILogger 경고 글 머리(알림 바로 앞 줄 · console.warn)
const APPROVAL_PENDING_PATH = '/api/approval/pending'; // 사이드바 결재 대기 수(양성 대조 가로챔 자리)
const UPD_TITLE = '최신 버전 확인/업데이트'; // LocalSwapUiText.MenuUpdate — 수동 업데이트 화면 본문 h6(G2 고유 글)
// 🔴 B2 고침 1 ①(N1 · 3V P3-1) — 메인PC 확인 표 요청(MainPcProofRunner 첫 부름). 대표 = 1 이상 · 직원 = 0 을 직접 셈(서버 403 과 독립)
const MAINPC_CHALLENGE_PATH = '/api/devices/mainpc-challenge';
// 🔴 B2 고침 1 ⑥(N6) — Login 진행 그림을 평가(page.evaluate) 없이 보기: 누름 직전 그 문서에 0.5초 간격 console.log 를 건다.
//   문서 요청이 붙잡힌 동안 평가는 10초 상한에 걸렸다(CI 사본 :2174 셋 다 null). 옛 문서의 타이머 · 콘솔은 새 문서가 들어설 때까지 돈다(가설 · 기록만).
//   글 꼴 = 「[hp-prog] t=ms bar=참거짓 id=수 btn=참거짓|null」 또는 「[hp-prog] t=ms err=이름」 — 값 · 토큰 0.
const PROG_TAG = '[hp-prog]';
const PROG_TICKS = 24; // 0.5초 × 24 = 12초(늦춤 20초 안)
const PROG_WATCH = `(() => {
  try {
    if (window.__hpProgOn) return;
    window.__hpProgOn = true;
    const t0 = Date.now();
    let n = 0;
    let h = null;
    const vis = (e) => !!e && e.getClientRects().length > 0 && getComputedStyle(e).visibility !== 'hidden';
    const flat = (e) => String((e && (e.innerText || e.textContent)) || '').replace(/\\s+/g, ' ').trim();
    const tick = () => {
      n += 1;
      try {
        const bar = Array.from(document.querySelectorAll('.mud-progress-linear')).some(vis);
        const id = Array.from(document.querySelectorAll('label')).filter((e) => vis(e) && flat(e) === '아이디').length;
        const btn = Array.from(document.querySelectorAll('button')).find((e) => vis(e) && flat(e) === '로그인');
        console.log('${PROG_TAG} t=' + (Date.now() - t0) + ' bar=' + bar + ' id=' + id + ' btn=' + (btn ? btn.disabled === true : 'null'));
      } catch (e) {
        console.log('${PROG_TAG} t=' + (Date.now() - t0) + ' err=' + String((e && e.name) || 'Error').slice(0, 40));
      }
      if (n >= ${PROG_TICKS} && h !== null) clearInterval(h);
    };
    h = setInterval(tick, 500);
  } catch (e) {
    console.log('${PROG_TAG} t=0 err=' + String((e && e.name) || 'Error').slice(0, 40));
  }
})();`;
// 진행 그림 글 한 줄 해석(순수 · 자체시험 K-C29i) — 꼴 밖이면 null
function progParse(line) {
  const m = /^\[hp-prog\] t=(\d+) (?:bar=(true|false) id=(\d+) btn=(true|false|null)|err=([A-Za-z]{1,40}))$/.exec(String(line || ''));
  if (!m) return null;
  if (m[5]) return { t: Number(m[1]), err: m[5] };
  return { t: Number(m[1]), bar: m[2] === 'true', id: Number(m[3]), btn: m[4] === 'null' ? null : m[4] === 'true' };
}
// 진행 그림 요약(순수 · 자체시험 K-C29i) — 표본 수 · 막대 보인 수 · 단추 꺼진 수 · 아이디 칸 0 인 수 · +5초 이후 첫 표본 · 실패 수
function progSum(lines) {
  const xs = (Array.isArray(lines) ? lines : []).map(progParse).filter((x) => x !== null);
  const ok = xs.filter((x) => !x.err);
  const at5 = ok.find((x) => x.t >= 5000) || null;
  return { n: xs.length, bar: ok.filter((x) => x.bar).length, btnOff: ok.filter((x) => x.btn === true).length,
    idGone: ok.filter((x) => x.id === 0).length, errs: xs.length - ok.length, at5 };
}
const progSumShow = (p) => (p.n === 0 ? '못 봄(진행 그림 글 0줄)'
  : `표본 ${p.n} · 막대 보임 ${p.bar} · 단추 꺼짐 ${p.btnOff} · 아이디 칸 없음 ${p.idGone} · 실패 ${p.errs}`
    + ` · +5초 표본 ${p.at5 ? `t=${p.at5.t} 막대 ${p.at5.bar} 아이디 칸 ${p.at5.id} 단추 꺼짐 ${p.at5.btn}` : '없음'}`);
// ── 🔴 HQA 보류 고침 · 물러남 장면 둘(⑪) — 측정 m1032-measure.js 의 표본기 꼴(값 · 토큰 0 · 경로 · 막대 · 아이디 칸 수 · 단추 꺼짐만) ──
//   window.__hpStallStart(tag, ticks) = 0.5초 간격 console.log · pageshow / pagehide 한 줄씩(persisted · 경로만) ·
//   sessionStorage 표지(__hpStall_sample)가 있으면 pageshow 때 표본기를 스스로 켠다(뒤로 가기 뒤 복원 문서).
//   글 꼴 = 「[hp-stall] s tag=A2 t=ms path=/login bar=참거짓 id=수 btn=참거짓|null」 · 「[hp-stall] s tag=… t=ms err=이름」 · 「[hp-stall] pageshow persisted=참거짓 path=…」
const STALL_TAG = '[hp-stall]';
const STALL_FLAG = '__hpStall_sample';
const STALL_OBS_MS = 45000; // 중지 · 복원 뒤 관찰(물러남 30초보다 길게 · 설계 §7-2)
const STALL_MIN_SAMPLES = 60; // 0.5초 × 90 중 최소(표본기가 실제로 돌았나)
const STALL_TICKS = 160; // 0.5초 × 160 = 80초(누름 ~ 관찰 끝)
const STOP_SETTLE_MS = 3000; // 첫 붙잡음 뒤 다시 만들어진 화면이 대기 그림에 들어설 때까지
const STOP_RELEASE_MS = 1000; // 중지 뒤 붙잡은 처리기를 풀어 주는 때(취소됐으면 넘김이 실패한다)
const BACK_LAND_MS = 4000; // 착지 뒤 뒤로 가기 전 기다림
const TOKEN_KEY = 'hitpan_access_token'; // AuthStorageKeys.AccessToken(있을 때만 읽어 가림 등록 · 출력 0)
const STALL_INIT = `(() => {
  try {
    if (window.top !== window) return;
    if (window.__hpStallInit) return;
    window.__hpStallInit = true;
    const vis = (e) => !!e && e.getClientRects().length > 0 && getComputedStyle(e).visibility !== 'hidden';
    const flat = (e) => String((e && (e.innerText || e.textContent)) || '').replace(/\\s+/g, ' ').trim();
    window.__hpStallStart = (tag, ticks) => {
      if (window.__hpStallOn) return 'already';
      window.__hpStallOn = true;
      const t0 = Date.now();
      let n = 0;
      let h = null;
      const tick = () => {
        n += 1;
        try {
          const bar = Array.from(document.querySelectorAll('.mud-progress-linear')).some(vis);
          const id = Array.from(document.querySelectorAll('label')).filter((e) => vis(e) && flat(e) === '아이디').length;
          const btn = Array.from(document.querySelectorAll('button')).find((e) => vis(e) && flat(e) === '로그인');
          console.log('${STALL_TAG} s tag=' + tag + ' t=' + (Date.now() - t0) + ' path=' + location.pathname
            + ' bar=' + bar + ' id=' + id + ' btn=' + (btn ? btn.disabled === true : 'null'));
        } catch (e) {
          console.log('${STALL_TAG} s tag=' + tag + ' t=' + (Date.now() - t0) + ' err=' + String((e && e.name) || 'Error').slice(0, 40));
        }
        if (n >= ticks && h !== null) clearInterval(h);
      };
      h = setInterval(tick, 500);
      return 'ok';
    };
    window.addEventListener('pageshow', (e) => {
      console.log('${STALL_TAG} pageshow persisted=' + (e.persisted === true) + ' path=' + location.pathname);
      let flag = null;
      try { flag = sessionStorage.getItem('${STALL_FLAG}'); } catch (err) { flag = null; }
      if (flag && /^[A-Za-z0-9]{1,8}$/.test(flag)) window.__hpStallStart(flag, ${STALL_TICKS});
    });
    window.addEventListener('pagehide', (e) => {
      console.log('${STALL_TAG} pagehide persisted=' + (e.persisted === true) + ' path=' + location.pathname);
    });
  } catch (e) {
    console.log('${STALL_TAG} init-err ' + String((e && e.name) || 'Error').slice(0, 40));
  }
})();`;
// 표본 줄 해석(순수 · 자체시험 K-C30a) — 꼴 밖이면 null
function stallParse(line) {
  const m = /^\[hp-stall\] s tag=([A-Za-z0-9]{1,8}) t=(\d+) (?:path=(\S{0,80}) bar=(true|false) id=(\d+) btn=(true|false|null)|err=([A-Za-z]{1,40}))$/.exec(String(line || ''));
  if (!m) return null;
  if (m[7]) return { tag: m[1], t: Number(m[2]), err: m[7] };
  return { tag: m[1], t: Number(m[2]), path: m[3], bar: m[4] === 'true', id: Number(m[5]), btn: m[6] === 'null' ? null : m[6] === 'true' };
}
// 고치기 전 끝 꼴(순수 · 자체시험 K-C30a) — /login · 막대 0 · 아이디 칸 있음(빈 로그인 입력 화면)
const formBack = (x) => !!x && !x.err && x.path === '/login' && x.bar === false && x.id >= 1;
// 머묾 꼴(순수 · 기록) — /login · 막대 보임 · 아이디 칸 없음(진행 그림)
const stallShape = (x) => !!x && !x.err && x.path === '/login' && x.bar === true && x.id === 0;
const stallShow = (x) => (x ? (x.err ? `t=${x.t} 실패 ${x.err}` : `t=${x.t} 경로=${x.path} 막대=${x.bar} 아이디칸=${x.id} 단추꺼짐=${x.btn}`) : '없음');
// ⓒ 뜬 적 있는 알림 글 — 문서마다 새로 돎(로그인 누름 · 다시 열기 = 새 문서 = 빈 목록). 글자 · 요소 클래스(색 가름) 둘만 · 50 상한.
//   보기 실패는 셈(window.__hpSnackSeenErr)으로 남김 — 조용히 삼키지 않는다.
const SNACK_SEEN_INIT = `(() => {
  try {
    if (window.__hpSnackSeen) return;
    const seen = [];
    const done = new WeakSet();
    window.__hpSnackSeen = seen;
    window.__hpSnackSeenErr = 0;
    const scan = () => {
      try {
        const els = document.querySelectorAll('.mud-snackbar');
        for (let i = 0; i < els.length; i += 1) {
          const el = els[i];
          if (done.has(el)) continue;
          const t = String(el.innerText || el.textContent || '').replace(/\\s+/g, ' ').trim();
          if (!t) continue;
          done.add(el);
          // ⬛ [낡은 줄 · B2 고침 1 이전] if (seen.length < 50) seen.push({ t: t.slice(0, 300), c: String(el.className || '').slice(0, 200) });
          // 🔴 B2 고침 1 ②(3V P2-1) — 50 상한 넘침도 「못 봄」 으로 셈(넘친 알림을 0 으로 세지 않음 · no403 의 seenErr === 0 이 받음)
          if (seen.length < 50) seen.push({ t: t.slice(0, 300), c: String(el.className || '').slice(0, 200) });
          else window.__hpSnackSeenErr += 1;
        }
      } catch (e) {
        window.__hpSnackSeenErr += 1;
      }
    };
    new MutationObserver(scan).observe(document, { childList: true, subtree: true, characterData: true });
  } catch (e) {
    window.__hpSnackSeenErr = (window.__hpSnackSeenErr || 0) + 1;
  }
})();`;

const key = (id, g, p) => ({ id, g, path: p, browser: BROWSER });
const KEY = {
  home: key('K35-S3-1', 'S3', 'first-home'),
  update: key('K35-S3-1', 'S3', 'first-update'),
  welcome: key('K35-S3-2', 'S3', 'direct-welcome'),
  phone: key('K35-S3-2', 'S3', 'phone-register'),
  gate: key('K35-S3-3', 'S3', 'gate-mainpc'),
  token: key('K35-S3-4', 'S3', 'mobile-token'),
  register: key('K35-S3-4', 'S3', 'mobile-register'),
  // 🔴 갈래 T — 시험 8(judge.js EXPECT_S3_ROWS 와 같은 줄)
  termsFirst: key('K35-S3-5', 'S3', 'terms-first'),
  termsSlow: key('K35-S3-5', 'S3', 'terms-slow-doc'),
  relogin: key('K35-S3-6', 'S3', 'relogin-home'),
  loginDirect: key('K35-S3-6', 'S3', 'login-direct'),
  dialog: key('K35-S3-7', 'S3', 'device-dialog-20s'),
  owner: key('K35-S3-8', 'S3', 'owner-home'),
  owner503: key('K35-S3-8', 'S3', 'owner-503'),
  sales: key('K35-S3-9', 'S3', 'sales-home'),
  // 🔴 갈래 B2(D18 R-③ · D17 양성 대조) — judge.js EXPECT_S3_ROWS 와 같은 줄
  ownerJson: key('K35-S3-8', 'S3', 'owner-json'),
  // 🔴 HQA 보류 고침 · 물러남(⑪ · 작업지시서 §8-13 · PM 결재 E4 + 덧) — judge.js EXPECT_S3_ROWS 와 같은 줄
  termsSlowProg: key('K35-S3-5', 'S3', 'terms-slow-prog'),
  stopAfterLogin: key('K35-S3-10', 'S3', 'stop-after-login'),
  backRestore: key('K35-S3-10', 'S3', 'back-restore'),
};
const WHAT = {
  // ⬛ [낡은 줄 · 고침 3] home: '새 직원 첫 로그인(로그인 응답 첫 로그인=참 · 약관 동의됨) → 첫 화면 / · 환영 화면(/welcome) 이동 0 · 본 화면 자리 잡음 · 흰 띠 0',
  // ⬛ [낡은 줄 · 고침 3] update: '새 직원 첫 로그인 + [최신버젼업데이트] 누름 → 수동 업데이트 화면(/data/update)으로 이동 · 환영 화면 이동 0 · 흰 띠 0',
  // 🔴 갈래 T — 대장 닫음 · 설계 G1 · G7 · G10 / G2 · G10 을 동작으로
  home: '권한 0 새 직원 첫 로그인(약관 동의됨) → 문서 이동 정확히 1건 /=200 · 본 화면 / · /welcome 0 · 흰 띠 0(+3초 · +10초) · 앱 표준오류 꼴 0 · 판매·재무 403 0 · 알림 글 예외 꼴 0 · 안내 한 장 보임 · 판매·재무 카드 0',
  update: '새 직원 첫 로그인 + [최신버젼업데이트] → 문서 이동 정확히 1건 /data/update=200 · 수동 업데이트 화면 자리 잡음 · /welcome 0 · 흰 띠 0(+3초 · +10초) · 앱 표준오류 꼴 0 · 알림 글 예외 꼴 0',
  termsFirst: '약관 미동의 직원 첫 로그인 → 문서 이동 정확히 1건 /terms=200 · 약관 화면 자리 잡음 · 흰 띠 0(+3초 · +10초) · 앱 표준오류 꼴 0 · 알림 글 예외 꼴 0',
  termsSlow: '약관 미동의 직원 로그인 · 로그인 누른 뒤 문서 요청만 20초 늦춤(시험 장치) → 끝 = 약관 화면 /terms · 문서 이동 정확히 1건 /terms=200(안전망이 앞 이동을 덮어쓰지 않음)',
  relogin: '기존 직원 재로그인(첫 로그인 지남 · 약관 동의됨) → 문서 이동 정확히 1건 /=200 · 본 화면 /',
  loginDirect: '로그인한 채 주소창에 /login 직접 → 본 화면 / 로 감(기존 동작 보존) · 마지막 문서 /=200',
  dialog: '새 기기 꼴 창 + [최신버젼업데이트] → 새 기기 알림 20초 열어 둠(그동안 알림 보임 · 문서 이동 0) → [확인] → 문서 이동 정확히 1건 /data/update=200 · 흰 띠 0 · 앱 표준오류 꼴 0',
  owner: '대표 화면 로그인 첫 화면 → 판매·재무 카드 전부 보임(큰 카드 · 보조 3) · 안내 한 장 0 · 흰 띠 0(+3초 · +10초) · 앱 표준오류 꼴 0 · 「권한 설정 필요」 안내는 기록만',
  owner503: '대표 첫 화면 다시 열기 · 대시보드 조회만 503(시험 장치 가로챔 · 앱 손 0) → 쉬운 알림 보임 · 흰 띠 0 · 경고 기록 [Dashboard.LoadDashboardAsync] 1 이상 · 앱 표준오류 꼴 0 · 알림 글 예외 꼴 0',
  sales: '판매 역할 직원(사원 역할 sales_user · 시험 DB) 로그인 → 출입증 역할 sales_user · 판매·재무 카드 보임 · 판매·재무 403 0 · 안내 한 장 0',
  // 🔴 갈래 B2(D17) — 직원 4장면(home · update · dialog · sales) 판정에 「403 알림 0」 세 겹(ⓐ 403 응답 0 · ⓑ 처리기 경고 줄 0 · ⓒ 뜬 처리기 알림 0)을 더함 ·
  //   update 는 G2 고유 글(본문 h6 「최신 버전 확인/업데이트」 · 직원 경고 알림 1)도 더함(D18 R-④). 위 글은 그대로 두고 여기 덧붙여 적음(#1).
  ownerJson: '대표 첫 화면 다시 열기 · 대시보드 조회만 200 + JSON 아닌 본문(일반 catch) · 결재 대기만 403(양성 대조 · 시험 장치 가로챔 · 앱 손 0) → 쉬운 알림 보임 · 뜬 알림 전부 허용 목록 안 · 날 글 0 · 흰 띠 0 · 경고 기록 [Dashboard.LoadDashboardAsync] 1 이상 · 처리기 경고 줄 1 이상 · 처리기 알림 1 이상',
  // 🔴 HQA 보류 고침 · 물러남(⑪) — 고침2 설계 §7-2 · PM 결재 덧(terms-slow-doc 기록 → 판정)
  termsSlowProg: '약관 미동의 직원 로그인 · 문서 요청만 20초 늦춤(terms-slow-doc 과 같은 창) → 누름 +5초 표본에 진행 막대 보임 · 아이디 칸 0(다시 만들어진 화면이 30초 전엔 입력 칸으로 물러나지 않음)',
  stopAfterLogin: '약관 미동의 직원 로그인 · 첫 문서(/terms) 붙잡음 → 브라우저 중지(CDP Page.stopLoading = 중지 단추 · Esc) → 45초 → 끝 = /login 빈 로그인 입력 화면(막대 0 · 아이디 칸 있음 · 아이디 · 비밀번호 넣으면 「로그인」 단추 켜짐 · 누르지 않음) · 중지 뒤 새 문서 0(덮어쓰기 0)',
  backRestore: '기존 직원 로그인 → / 착지 → 뒤로 가기(복원 캐시 켠 브라우저 · 마지막 pageshow persisted=참 · 복원 뒤 문서 요청 0) → 45초 → 끝 = /login 빈 로그인 입력 화면(막대 0 · 아이디 칸 있음 · 아이디 · 비밀번호 넣으면 「로그인」 단추 켜짐 · 누르지 않음)',
  welcome: '로그인한 채 주소창에 /welcome 직접 → 「이 화면은 정식 판에서 열립니다」 안내 · 옛 환영 글 0 · 주소 그대로 · 흰 띠 0',
  // ⬛ [낡은 줄 · 고침 1] phone: '휴대폰 화면 크기 · 로그인 없이 /m/device-register(옛 QR 주소) → 「등록 없이 로그인」 안내 · 옛 등록 물음 0 · 옛 등록 요청 0 · 흰 띠 0',
  //   🔴 고침 2(런 46118135957 · 개발명세서 「고침 2」 ③) — 설계 §4 「안내 + [로그인]」 은 화면 손 0 이다. 그 화면은 기본 틀 MainLayout
  //   (MainLayout.razor:2 [Authorize] · :106 ~ :108 NotAuthorized → RedirectToLogin)을 써서 로그인 없으면 /login 으로 간다(main 과 같음).
  //   ⇒ 로그인 없음 = /login 착지 · 로그인한 휴대폰 = 안내 — 둘 다 잰다(키 그대로 · 약하게 하지 않음).
  phone: '휴대폰 화면 크기 · /m/device-register(옛 QR 주소) — 로그인 없음 → /login 착지(MainLayout 문 · 화면 손 0) · 로그인한 휴대폰 → 「등록 없이 로그인」 안내 · 두 번 다 옛 등록 물음 0 · 옛 등록 요청 0 · 흰 띠 0',
  gate: '기기 관문 · 메인PC 확인 길 그대로 — 새 직원 브라우저 세션으로 GET api/devices/gate-status · is-main-pc 둘 다 200',
  token: '닫힌 문 POST api/devices/mobile-token(대표 세션) → 처리기 도달 0(등록 토큰 표 · 기기 표 행 변화 0 · 처리기 글 0) · 상태 숫자 · 본문 꼴은 기록만',
  register: '닫힌 문 POST api/devices/mobile-register(대표 세션 · 시험 DB 에 심은 쓸 수 있는 토큰) → 처리기 도달 0(심은 토큰 안 쓰임 · 기기 표 행 변화 0 · 처리기 글 0) · 로그인 없는 호출 숫자도 기록만',
};

// ── 시험 DB(CI 컨테이너 · 127.0.0.1) — 장치 S2 에서 깐 mysql 클라이언트 · 비번은 환경으로만 넘김(인자 · 출력 0) ──
//   표 · 칸은 출하 DDL(installer/hitpan_db_clean.sql — users :4380 · device_register_tokens :3884 · tenant_devices :4050)에서 확인(#13).
const SAFE_ID = /^[A-Za-z0-9-]{1,64}$/;
const SAFE_HEX = /^[0-9a-f]{64}$/;
function idOk(v, what) {
  const s = String(v ?? '');
  if (!SAFE_ID.test(s)) throw new K.KitStepError(`${what} 꼴 이상(질의에 넣지 않음)`);
  return s;
}
function dbq(sqlText) {
  const dbEnv = { ...process.env, MYSQL_PWD: env.UI_DB_PASSWORD || env.DB_PASSWORD || '' };
  const args = ['-h', env.DB_HOST || '127.0.0.1', '-P', env.DB_PORT || '13306', '-u', env.DB_USER || 'root',
    '-N', '-B', '--default-character-set=utf8mb4', env.DB_NAME || 'hitpan_ui', '-e', sqlText];
  const run = spawnSync('mysql', args, { env: dbEnv, encoding: 'utf8', timeout: 30000 });
  if (run.error) throw new K.KitStepError(`시험 DB 질의 실행 실패 ${run.error.code || L.errorName(run.error)}`);
  if (run.status !== 0) throw new K.KitStepError(`시험 DB 질의 실패 exit=${run.status} · ${L.sanitize(String(run.stderr || '').trim(), 120)}`);
  return String(run.stdout || '').split(/\r?\n/).filter((x) => x.length > 0).map((x) => x.split('\t'));
}
function dbCount(sqlText) {
  const rows = dbq(sqlText);
  const n = rows.length > 0 ? Number(rows[rows.length - 1][0]) : NaN;
  if (!Number.isFinite(n)) throw new K.KitStepError('시험 DB 셈 결과 꼴 이상');
  return n;
}
function counts(tid) {
  const t = idOk(tid, '회사 번호');
  return {
    tokens: dbCount(`SELECT COUNT(*) FROM device_register_tokens WHERE tenant_id = '${t}';`),
    devices: dbCount(`SELECT COUNT(*) FROM tenant_devices WHERE tenant_id = '${t}';`),
  };
}
// 첫 로그인 상태 다시 만들기 — 서버 판정(AuthService: 마지막 로그인 칸이 비면 첫 로그인)과 같은 칸만 비운다.
function resetFirstLogin(st) {
  const uid = idOk(st.freshUid, '새 직원 번호');
  const t = idOk(st.tid, '회사 번호');
  const n = dbCount(`UPDATE users SET last_login_at = NULL WHERE user_id = '${uid}' AND tenant_id = '${t}'; SELECT ROW_COUNT();`);
  if (n !== 1) throw new K.KitStepError(`첫 로그인 상태 재현 — 바뀐 행 ${n}(1 이어야 함)`);
}

// 출입증 가운데 토막에서 클레임 하나(값은 찍지 않는다 · 질의에만 쓴다 · 쿨타임 몸과 같은 꼴)
function jwtClaim(token, name) {
  try {
    const mid = String(token || '').split('.')[1] || '';
    const json = Buffer.from(mid.replace(/-/g, '+').replace(/_/g, '/'), 'base64').toString('utf8');
    const v = JSON.parse(json)[name];
    return typeof v === 'string' && v ? v : null;
  } catch (e) {
    console.log(`K 닫힌문 출입증 클레임 읽기 실패 (${L.errorName(e)})`);
    return null;
  }
}

// 🔴 갈래 T — 출입증 역할 칸(「role」 · 글자 하나 또는 목록) 안에 그 역할이 있나(값은 찍지 않는다 · 참거짓만)
function jwtHasRole(token, roleName) {
  try {
    const mid = String(token || '').split('.')[1] || '';
    const j = JSON.parse(Buffer.from(mid.replace(/-/g, '+').replace(/_/g, '/'), 'base64').toString('utf8'));
    const v = j.role ?? j['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'];
    return Array.isArray(v) ? v.includes(roleName) : v === roleName;
  } catch (e) {
    console.log(`K 닫힌문 출입증 역할 읽기 실패 (${L.errorName(e)})`);
    return false;
  }
}

// 본문 꼴까지 남기는 요청(kit35.apiCall 은 JSON 아닌 본문을 버린다) — 같은 머리(기기 꼴 UA · 출입증 · 기기 번호)
async function rawCall(method, p, sess, body) {
  const headers = { 'Content-Type': 'application/json', 'User-Agent': API_UA };
  if (sess && sess.token) headers.Authorization = `Bearer ${sess.token}`;
  if (sess && sess.deviceId) headers['X-HitPan-Device-Id'] = sess.deviceId;
  const res = await fetch(`${K.BASE}${p}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  const text = await res.text();
  return {
    status: res.status,
    contentType: res.headers.get('content-type') || null,
    allow: res.headers.get('allow') || null,
    bodyLength: text.length,
    bodyHead: text ? L.sanitize(text.replace(/\s+/g, ' ').trim(), 60) : '',
    handlerWords: HANDLER_WORDS.filter((w) => text.includes(w)).length,
  };
}

// ── 줄 쓰기 ──
// ⬛ [낡은 몸 · 고침 2] function rec(st, name, res) {
// ⬛   K.recK(st.r, { key: KEY[name], what: WHAT[name], got: res.got, pass: res.pass, kind: 'TEST' });
// ⬛ }
// 🔴 고침 3 — res.known 이 참이고(장면이 알려진 꼴을 따로 계산 · 판정 식은 그대로) 그 키가 판정기 대장에 있으면
//   결과 칸 머리에 J.KNOWN_WHY + 대장 이름. 결과 칸은 L.rec 가 글자로 저장한다(객체는 JSON 글자) → 머리 글자를 붙여 글자로 넘긴다.
//   아니면 머리 없음 → 판정기가 종전대로 빨강.
function knownName(name) {
  const k = KEY[name];
  const w = J.KNOWN_DEFECT_ROWS.find((x) => x[0] === k.id && x[1] === k.g && x[2] === k.path && x[3] === k.browser);
  return w ? w[4] : null;
}
function rec(st, name, res) {
  const kn = res.pass === false && res.known === true ? knownName(name) : null;
  const got = kn ? `${J.KNOWN_WHY}${kn} · ${typeof res.got === 'string' ? res.got : JSON.stringify(res.got)}` : res.got;
  K.recK(st.r, { key: KEY[name], what: WHAT[name], got, pass: res.pass, kind: 'TEST' });
}
// 🔴 고침 3 — 알려진 꼴(장면 판정 식이 거짓일 때만 따로 계산 · W-S-03 자기 조건이 참일 때만 참)
//   첫 화면: 본 화면 · 주소 / · 이동 기록에 / · /welcome 이동 0 은 모두 참이고 흰 띠만 참(선행검증서 §2).
function homeKnown(s, navs, band) {
  return s.where === 'main' && s.path === HOME_PATH && navs.includes(HOME_PATH) && !navs.some(isWelcome) && band === true;
}
//   업데이트: /welcome 이동 0 · 문서 이동 기록에 /data/update 시도 있음(갈 곳 계산은 맞았다) · 실패 사유는
//   「/data/update 끊김(실패) 뒤 / 」 또는 흰 띠뿐(선행검증서 §1 · §2). 흰 띠 읽기 실패(null) = 꼴 아님.
// ⬛ [낡은 몸 · 고침 3] function updateKnown(navs, docNavs, band) {
// ⬛     if (navs.some(isWelcome)) return false;
// ⬛     const ui = docNavs.findIndex((d) => d.startsWith(`${UPDATE_PATH}=`));
// ⬛     if (ui < 0) return false;
// ⬛     if (band !== true && band !== false) return false;
// ⬛     const navOk = navs.includes(UPDATE_PATH);
// ⬛     const cutThenHome = !navOk && docNavs[ui].startsWith(`${UPDATE_PATH}=실패`)
// ⬛       && docNavs.slice(ui + 1).some((d) => d.startsWith(`${HOME_PATH}=`)) && navs.includes(HOME_PATH);
// ⬛     return (navOk || cutThenHome) && !(navOk && band === false);
// ⬛   }
// 🔴 고침 3-2(작업지시서 §9 P2-1) — 최종 착지 조건 추가(homeKnown 과 같은 결 · 장면 s 를 받음):
//   마지막 자리가 본 화면 `/` 또는 `/data/update` 일 때만 알려진 꼴. 끊김 뒤 `/login` · `/terms` 등에 앉으면 꼴 아님(반례 U7 · U8).
function updateKnown(s, navs, docNavs, band) {
  if (!s || s.where !== 'main' || (s.path !== HOME_PATH && s.path !== UPDATE_PATH)) return false;
  if (navs.some(isWelcome)) return false;
  const ui = docNavs.findIndex((d) => d.startsWith(`${UPDATE_PATH}=`));
  if (ui < 0) return false;
  if (band !== true && band !== false) return false;
  const navOk = navs.includes(UPDATE_PATH);
  const cutThenHome = !navOk && docNavs[ui].startsWith(`${UPDATE_PATH}=실패`)
    && docNavs.slice(ui + 1).some((d) => d.startsWith(`${HOME_PATH}=`)) && navs.includes(HOME_PATH);
  return (navOk || cutThenHome) && !(navOk && band === false);
}
function dev(st, name, step, e) {
  const why = e instanceof K.KitStepError ? L.sanitize(e.message, 200) : `예외 ${L.errorName(e)}`;
  console.log(`K 닫힌문 ${name} 단언 전 실패 — 단계=${step.now} · ${why}`);
  if (!K.isWritten(st.r, KEY[name])) {
    K.recK(st.r, { key: KEY[name], what: WHAT[name], got: `단언 전 실패 — 단계=${step.now} · ${why}`, pass: false, kind: 'DEVICE', err: e instanceof K.KitStepError ? null : e });
  }
}

// ── API 세션(연 것은 st.apiSess 에 적어 끝에 반드시 놓는다) ──
async function apiOpen(st, who, loginId, password) {
  const s = await K.apiLogin(who, loginId, password);
  st.apiSess.push(s);
  return s;
}
async function apiClose(st, s) {
  const i = st.apiSess.indexOf(s);
  if (i < 0) return;
  st.apiSess.splice(i, 1);
  const lo = await K.apiLogout(s);
  console.log(`K 닫힌문 API 로그아웃 ${lo.got}`);
  if (!lo.ok) st.logoutFail += 1;
}

// ── 준비: 새 직원 만들기(대표 · 화면과 같은 POST api/users) · 약관 미리 동의 ──
async function prep(st) {
  const step = { now: '대표 API 로그인' };
  const p = await apiOpen(st, 'PARENT', ID.parent, PW.parent);
  try {
    st.tid = idOk(jwtClaim(p.token, 'tenant_id'), '회사 번호');
    st.parentUid = idOk(jwtClaim(p.token, 'user_id'), '대표 번호');
    step.now = '새 직원 만들기';
    st.freshPw = `Ui#${crypto.randomBytes(16).toString('hex')}`;
    L.maskAndRegister('PW_S3_FRESH', st.freshPw);
    const res = await K.apiCall('POST', '/api/users', {
      sess: p,
      body: { email: ID.fresh, password: st.freshPw, userName: FRESH_NAME, empName: FRESH_NAME, role: 'User', confirmDifferentPerson: true },
    });
    if (res.status !== 201 && res.status !== 200) {
      const j = res.json || {};
      throw new K.KitStepError(`새 직원 만들기 status=${res.status} code=${L.sanitize(String(j.code || j.message || ''), 80)}`);
    }
  } finally {
    await apiClose(st, p);
  }
  step.now = '새 직원 API 로그인 · 약관 동의';
  const f = await apiOpen(st, 'S3FRESH', ID.fresh, st.freshPw);
  try {
    st.freshUid = idOk(jwtClaim(f.token, 'user_id'), '새 직원 번호');
    const ag = await K.apiCall('POST', '/api/auth/terms/agree', {
      sess: f,
      body: { agreeService: true, agreePrivacy: true, agreeSubscription: true, agreeDataOwnership: true, agreeMarketing: false, termsVersion: 'v2.0.0' },
    });
    if (ag.status < 200 || ag.status >= 300) throw new K.KitStepError(`새 직원 약관 동의 status=${ag.status}`);
  } finally {
    await apiClose(st, f);
  }
}

// 🔴 갈래 T — 약관 미동의 직원 만들기(준비 prep 과 같은 길 · 약관 동의만 건너뜀 · 측정 M T1 에서 만듦 실측). 실패는 약관 두 장면만 장치 거짓.
async function prepTerms(st) {
  const p = await apiOpen(st, 'PARENT', ID.parent, PW.parent);
  try {
    st.termsPw = `Ui#${crypto.randomBytes(16).toString('hex')}`;
    L.maskAndRegister('PW_S3_TERMS', st.termsPw);
    const res = await K.apiCall('POST', '/api/users', {
      sess: p,
      body: { email: TERMS_ID, password: st.termsPw, userName: TERMS_NAME, empName: TERMS_NAME, role: 'User', confirmDifferentPerson: true },
    });
    if (res.status !== 201 && res.status !== 200) {
      const j = res.json || {};
      throw new K.KitStepError(`못 만듦 — 약관 미동의 직원 만들기 status=${res.status} code=${L.sanitize(String(j.code || j.message || ''), 80)}`);
    }
  } finally {
    await apiClose(st, p);
  }
}
// 🔴 main 합침 뒤 계정 자리(작업지시서 §8-12 (가) 「만든 몸이 치운다」) — 준비 직원(prep)을 쓰는 장면이 다 끝난 뒤,
//   사원·계정 관리 화면 [사용 안 함](Users.razor → AccountSeatApi.SuspendAsync)과 같은 POST /api/users/{id}/suspend 로 그 계정을 끈다
//   (UserService.SuspendAsync = is_active 0 · 출입증 끊기 ⇒ 계정 한도 셈 `is_active = 1 AND is_deleted = 0` 에서 빠짐 · AccountSeatGuard).
//   까닭: 한도 바닥 5 · 쿨타임 시드가 자식 「다」 를 더해 활성 4 → prep 5번째 → 약관 미동의 직원 6번째 = 409 account_seat_full.
//   실패(2xx 아님 · 예외)는 조용히 넘기지 않는다 — K.KitStepError 를 던져 부르는 자리가 약관 두 장면을 장치 거짓(치우기 실패)으로 적는다.
//   prep 이 계정을 만들기 전에 섰으면(번호 없음) 치울 것이 없다 — 한 줄 남기고 넘어간다(자리가 정말 모자라면 prepTerms 의 409 가 장치 거짓으로 드러남).
async function releasePrep(st) {
  if (!st.freshUid) {
    console.log(`K 닫힌문 준비 직원 치우기 건너뜀 — 만든 계정 번호 없음(준비 ${st.prepOk ? '통과' : `실패 · ${st.prepWhy}`})`);
    return;
  }
  const uid = idOk(st.freshUid, '새 직원 번호');
  const p = await apiOpen(st, 'PARENT', ID.parent, PW.parent);
  try {
    const res = await K.apiCall('POST', `/api/users/${encodeURIComponent(uid)}/suspend`, { sess: p });
    if (res.status < 200 || res.status >= 300) {
      const j = res.json || {};
      throw new K.KitStepError(`치우기 실패 — 준비 직원 사용 안 함 status=${res.status} code=${L.sanitize(String(j.code || j.message || ''), 80)}`);
    }
    console.log(`K 닫힌문 준비 직원 사용 안 함(자리 돌려줌) status=${res.status}`);
  } finally {
    await apiClose(st, p);
  }
}
// 🔴 갈래 T — G12 판매 역할 직원: 새 직원의 사원 역할 칸만 sales_user 로(시험 DB · CI 컨테이너 · 실물 0).
//   출입증 역할 = 사원 역할 칸(Application AuthService 출입증 만들기 · employees.role) · 칸은 출하 DDL employees `role` varchar(30)(#13).
//   제품이 만드는 꼴과 같은 값(EmployeeService 사원 등록 기본 역할 sales_user). /api/users 는 역할을 User · Manager 등 enum 으로만 받아 이 값을 못 만든다.
//   계정 하나를 더 만들지 않는다(계정 한도) — 새 직원 장면이 다 끝난 뒤 마지막에 부른다.
function setSalesRole(st) {
  const uid = idOk(st.freshUid, '새 직원 번호');
  const t = idOk(st.tid, '회사 번호');
  const n = dbCount(`UPDATE employees SET role = '${SALES_ROLE}' WHERE user_id = '${uid}' AND tenant_id = '${t}'; SELECT ROW_COUNT();`);
  if (n !== 1) throw new K.KitStepError(`못 만듦 — 판매 역할 바꿈 바뀐 행 ${n}(1 이어야 함)`);
}

// ── 브라우저 한 벌(컨텍스트) — 외부 끊음 · 토큰 가림 · 막기/녹음기 초기 스크립트(앞 몸과 같은 꼴) ──
async function openCtx(st, who, extra = {}) {
  const tally = { continue: 0, abort: 0 };
  const context = await st.br.newContext({ baseURL: K.BASE, ...extra });
  await K.blockExternal(context, st.r, tally);
  const tok = { access: null, deviceId: null, masked: 0 };
  K.watchTokens(context, tok);
  await context.addInitScript(K.INIT_SCRIPT);
  // 🔴 갈래 B2(D17 ⓒ) — 뜬 적 있는 알림 글 모으기(문서마다 · 제품 손 0)
  await context.addInitScript(SNACK_SEEN_INIT);
  const kb = {
    browser: BROWSER, who, context, page: null, tok, tally, closed: false,
    br: { seen: 0, closed: 0, errs: 0, lastErr: null }, consoleLines: 0, consoleBuf: [],
    navs: [], login: { seen: 0, redirect: null, terms: null, deviceNew: null, status: null, err: null }, oldReq: 0,
    // 🔴 고침 2 — 실패 때 got 에 싣는 진단(값 · 토큰 · 그림 0 · 주소는 쿼리 뗀 경로만 · 글자는 싣는 그 시점 소독)
    dx: { fails: [], failCount: 0, docs: [], unhandled: [] },
  };
  K.watchConsole(context, kb);
  st.kbs.push(kb);
  kb.page = await context.newPage();
  const page = kb.page;
  page.on('framenavigated', (fr) => {
    if (fr !== page.mainFrame()) return;
    const p = K.pathOf(fr.url()).split('?')[0];
    if (kb.navs.length < 40) kb.navs.push(p);
  });
  // 로그인 응답 — 참거짓 칸 둘만 읽는다(토큰은 watchTokens 가 가림 · 여기서 찍지 않음)
  page.on('response', async (res) => {
    if (!/\/api\/auth\/login(\?|$)/i.test(res.url()) || res.request().method() !== 'POST') return;
    try {
      const j = await res.json();
      kb.login.status = res.status();
      kb.login.redirect = !!(j && (j.redirectToWelcome ?? j.RedirectToWelcome));
      kb.login.terms = !!(j && (j.requiresTermsConsent ?? j.RequiresTermsConsent));
      // 🔴 고침 2 ② — 새 기기 알림 창(Login.razor :427 ~ :445)이 뜨는 로그인인가(참거짓만)
      kb.login.deviceNew = !!(j && (j.deviceNewlyRegistered ?? j.DeviceNewlyRegistered));
      kb.login.seen += 1;
    } catch (e) {
      kb.login.err = L.errorName(e);
    }
  });
  page.on('request', (req) => {
    if (/\/api\/devices\/mobile-(register|token)(\?|$)/i.test(req.url())) kb.oldReq += 1;
    // 🔴 고침 2 ② — 주 프레임 문서 이동 요청(경로만 · 끝 = 완료 상태 / 실패 사유). 서비스 워커 요청은 frame() 이 던진다 → 센 것 아님.
    let mainDoc = false;
    try {
      mainDoc = req.isNavigationRequest() && req.frame() === page.mainFrame();
    } catch (e) {
      mainDoc = false;
    }
    if (mainDoc && kb.dx.docs.length < 12) kb.dx.docs.push({ req, path: pathOnly(req.url()), end: '대기' });
  });
  page.on('requestfinished', (req) => {
    const x = kb.dx.docs.find((d) => d.req === req);
    if (!x) return;
    req.response().then((r) => { x.end = r ? String(r.status()) : '응답 없음'; }, (e) => { x.end = `응답 읽기 실패 ${L.errorName(e)}`; });
  });
  page.on('requestfailed', (req) => {
    const x = kb.dx.docs.find((d) => d.req === req);
    if (!x) return;
    const f = req.failure();
    x.end = `실패 ${L.sanitize(f && f.errorText ? f.errorText : '-', 40)}`;
  });
  // 🔴 고침 2 ① — 4xx · 5xx 응답(방법 · 쿼리 뗀 경로 · 숫자). 외부 차단(ERR_BLOCKED_BY_CLIENT)은 응답이 아니라 여기 안 옴.
  page.on('response', (res) => {
    const s = res.status();
    if (s < 400) return;
    kb.dx.failCount += 1;
    if (kb.dx.fails.length < 15) kb.dx.fails.push(`${s} ${res.request().method()} ${pathOnly(res.url())}`);
  });
  // 🔴 고침 2 ① — 흰 띠를 띄우는 Blazor 처리 안 된 예외 글(kit35.watchConsole 은 20줄 상한이라 런 46118135957 에서 잘렸다).
  //   원문은 메모리에만 · got 에 실을 때 K.consoleLine(등록 목록 소독 + 제어문자)으로 다시 거른다.
  page.on('console', (msg) => {
    if (msg.type() !== 'error' || kb.dx.unhandled.length >= 6) return;
    const t = String(msg.text());
    if (/unhandled|crit:|exception/i.test(t)) kb.dx.unhandled.push(t);
  });
  page.on('pageerror', (e) => {
    if (kb.dx.unhandled.length < 6) kb.dx.unhandled.push(`화면 예외 ${L.errorName(e)}`);
  });
  // 🔴 갈래 T — 판정 재료(수만 · 글 원문은 안 쌓음 · 403 은 방법 + 쿼리 뗀 경로만). 로그인 누름 · 다시 열기 때 gtReset 으로 비운다.
  gtReset(kb);
  page.on('console', (msg) => {
    const t = String(msg.text());
    // 앱 표준오류 꼴(Console.Error.WriteLine 은 「[부품.자리]」 로 시작 · 흰 띠를 켬 — 설계 §7-1-2) · 브라우저 자원 오류(Failed to load …)는 셈 밖
    if (msg.type() === 'error' && t.trimStart().startsWith('[')) kb.gt.appErr += 1;
    // ILogger 경고(WebAssemblyConsoleLogger → console.warn) 중 대시보드 조회 자리(G11 흔적)
    if (msg.type() === 'warning' && t.includes(DASH_WARN_TAG)) kb.gt.warnDash += 1;
    // 🔴 갈래 B2(D17 ⓑ) — 처리기 평소 403 갈래 경고 줄(알림 바로 앞 줄) · 일반 catch 표(경고 글 안 JsonException · 기록 겸 판정 보조)
    if (msg.type() === 'warning' && t.includes(H403_TAG)) kb.gt.h403 += 1;
    if (msg.type() === 'warning' && t.includes(DASH_WARN_TAG) && t.includes('JsonException')) kb.gt.warnJson += 1;
  });
  page.on('request', (req) => {
    if (SALES_FIN_PATHS.includes(pathOnly(req.url()))) kb.gt.sfReq += 1;
    // 🔴 B2 고침 1 ①(N1) — 메인PC 확인 표 요청 수(대표 왕복이 실제로 도는지 · 직원은 0) · ⑦(N7) 결재 대기 부름 수(G13 기록)
    if (req.method() === 'POST' && pathOnly(req.url()) === MAINPC_CHALLENGE_PATH) kb.gt.mcReq += 1;
    if (req.method() === 'GET' && pathOnly(req.url()) === APPROVAL_PENDING_PATH) kb.gt.pendReq += 1;
  });
  page.on('response', (res) => {
    if (res.status() !== 403) return;
    const p = pathOnly(res.url());
    if (SALES_FIN_PATHS.includes(p)) kb.gt.sf403 += 1;
    if (kb.gt.f403.length < 20) kb.gt.f403.push(`${res.request().method()} ${p}`);
    kb.gt.f403n += 1; // 🔴 갈래 B2(D17 ⓐ) — 수 전체(목록 20 상한과 별개)
    if (p === APPROVAL_PENDING_PATH) kb.gt.pend403 += 1; // 🔴 B2 고침 1 ⑦(G13 기록)
  });
  return kb;
}
// 🔴 갈래 T — 판정 재료 비우기
function gtReset(kb) {
  // ⬛ [낡은 줄 · 갈래 B2 이전] kb.gt = { appErr: 0, warnDash: 0, sfReq: 0, sf403: 0, f403: [] };
  // ⬛ [낡은 줄 · B2 고침 1 이전] kb.gt = { appErr: 0, warnDash: 0, sfReq: 0, sf403: 0, f403: [], f403n: 0, h403: 0, warnJson: 0 };
  // 🔴 B2 고침 1 ①(mcReq · 메인PC 확인 표 요청) · ⑦(pendReq · pend403 · 결재 대기 부름 · 그 403)
  kb.gt = { appErr: 0, warnDash: 0, sfReq: 0, sf403: 0, f403: [], f403n: 0, h403: 0, warnJson: 0, mcReq: 0, pendReq: 0, pend403: 0 };
}
// 🔴 갈래 B2(D17 ⓒ) — 이 문서에서 뜬 적 있는 알림 글(초기 스크립트가 모은 것 · 원문은 메모리에만 · 찍을 때 snackShow 로 소독)
async function seenSnacks(page) {
  return K.withTimeout(page.evaluate(() => ({
    list: Array.isArray(window.__hpSnackSeen) ? window.__hpSnackSeen.map((x) => ({ t: String(x.t), c: String(x.c) })) : null,
    err: Number(window.__hpSnackSeenErr || 0),
  })), K.EVAL_MS, '뜬 알림 읽기').catch((e) => {
    console.log(`K 닫힌문 뜬 알림 읽기 실패 ${e instanceof K.KitStepError ? L.sanitize(e.message, 80) : L.errorName(e)}`);
    return { list: null, err: null };
  });
}
// 뜬 알림 셈(순수 · 자체시험 K-C29b) — 목록을 못 읽으면 셈 칸 null(판정 거짓 쪽)
function snackTally(seen) {
  const list = seen && Array.isArray(seen.list) ? seen.list : null;
  if (!list) return { seenSnacks: [], forbidSeen: null, failSnackErr: null, outsideAllow: null, seenErr: seen ? seen.err : null };
  const texts = list.map((x) => x.t);
  return {
    seenSnacks: texts,
    forbidSeen: texts.filter((t) => t.includes(FORBID_TEXT)).length,
    // 일반 catch 표(알림 색 Error — HttpRequestException catch 는 Warning)
    failSnackErr: list.filter((x) => x.t.includes(DASH_FAIL_TEXT) && /error/i.test(x.c)).length,
    outsideAllow: texts.filter((t) => !t.includes(DASH_FAIL_TEXT) && !t.includes(FORBID_TEXT)).length,
    seenErr: seen.err,
  };
}
// 쿼리 · 조각을 뗀 경로(토큰 · 열쇠 꼴 값이 쿼리로 실려도 got 에 안 들어감)
function pathOnly(u) {
  try {
    return new URL(u).pathname;
  } catch (e) {
    return `(주소 해석 실패 ${L.errorName(e)})`;
  }
}
// 실패 때만 got 에 싣는 진단 묶음(그림 · 비번 · 토큰 0)
function diag(kb, extra = {}) {
  return {
    deviceNew: kb.login.deviceNew,
    docNavs: kb.dx.docs.map((d) => `${d.path}=${d.end}`),
    fails4xx5xx: kb.dx.failCount,
    fails: [...kb.dx.fails],
    unhandled: kb.dx.unhandled.map((t) => K.consoleLine(t, 200)),
    ...extra,
  };
}
async function closeKb(st, kb) {
  if (kb.closed) return;
  kb.closed = true;
  K.flushConsole(kb);
  if (kb.tok.access) {
    const lo = await K.apiLogout({ who: kb.who, token: kb.tok.access, deviceId: kb.tok.deviceId });
    console.log(`K 닫힌문 ${kb.who} 로그아웃 ${lo.got}`);
    if (!lo.ok) st.logoutFail += 1;
  }
  await kb.context.close().catch((e) => console.log(`K 닫힌문 컨텍스트 닫기 실패 ${L.errorName(e)}`));
}
// 흰 띠(화면 아래 오류 띠 #blazor-error-ui) 보임 여부
async function errBand(page) {
  return page.evaluate(() => {
    const e = document.getElementById('blazor-error-ui');
    return !!e && getComputedStyle(e).display !== 'none';
  }).catch((e) => {
    console.log(`K 닫힌문 흰 띠 읽기 실패 ${L.errorName(e)}`);
    return null;
  });
}
async function seen(page, text, ms = TEXT_MS) {
  return page.getByText(text, { exact: false }).first().waitFor({ state: 'visible', timeout: ms }).then(() => true, () => false);
}
const isWelcome = (p) => String(p || '').toLowerCase().replace(/\/+$/, '') === WELCOME_PATH;

// ── 🔴 갈래 T 판정 — 순수 함수(자체시험 K-C28a 가 참 · 거짓 반례로 부른다 · 화면 · 브라우저 0) ──
//   o = 장면이 모은 재료(값 · 토큰 0). docs = 「경로=끝」 글자 목록(로그인 누름 뒤 주 프레임 문서 요청 · 끝 = 상태 숫자 / 실패 사유).
const oneDoc = (docs, p) => Array.isArray(docs) && docs.length === 1 && docs[0] === `${p}=200`;
const snackBadCount = (snacks) => (Array.isArray(snacks) ? snacks : []).filter((t) => SNACK_BAD.test(String(t))).length;
const bandsOff = (o) => o.band3 === false && o.band10 === false;
// G10 — 흰 띠 +3초 · +10초 둘 다 0 · 앱 표준오류 꼴 0 · 보이는 알림 글에 날 예외 꼴 0
const cleanScreen = (o) => bandsOff(o) && o.appErr === 0 && snackBadCount(o.snacks) === 0;
const navsOk = (o) => Array.isArray(o.navs) && !o.navs.some(isWelcome);
// 🔴 갈래 B2(D17 · 설계 §7-2-3) — 「보이는 403 알림 0」 세 겹: ⓐ 403 응답 0 · ⓑ 처리기 경고 줄 0 · ⓒ 뜬 처리기 알림 0(못 읽으면 null = 거짓)
// ⬛ [낡은 줄 · B2 고침 1 이전] const no403 = (o) => o.f403n === 0 && o.h403 === 0 && o.forbidSeen === 0;
// 🔴 B2 고침 1 ②(3V P2-1 · N2) — 뜬 알림 보기 실패(관찰자 던짐 · 50 상한 넘침)가 1 이라도 있으면 「못 봄」 = 거짓(0 봄으로 세지 않음)
const no403 = (o) => o.f403n === 0 && o.h403 === 0 && o.forbidSeen === 0 && o.seenErr === 0;
// 🔴 B2 고침 1 ①(N1) — 직원은 메인PC 확인 표 요청 0(서버 403 과 독립인 직접 셈) · 대표는 1 이상(owner 판정)
const noMc = (o) => o.mcReq === 0;
const JUDGE = Object.freeze({
  // G1 · G7 · G10
  // ⬛ [낡은 줄 · 갈래 B2 이전] home: (o) => o.where === 'main' && o.path === HOME_PATH && oneDoc(o.docs, HOME_PATH) && navsOk(o) && cleanScreen(o)
  // ⬛ [낡은 줄 · 갈래 B2 이전]   && o.sf403 === 0 && o.notice === true && o.hero === 0,
  // ⬛ [낡은 줄 · B2 고침 1 이전] home: (o) => o.where === 'main' && o.path === HOME_PATH && oneDoc(o.docs, HOME_PATH) && navsOk(o) && cleanScreen(o)
  // ⬛ [낡은 줄 · B2 고침 1 이전]   && o.sf403 === 0 && o.notice === true && o.hero === 0 && no403(o),
  //   🔴 B2 고침 1 ①(N1) — 직원 메인PC 확인 표 요청 0
  home: (o) => o.where === 'main' && o.path === HOME_PATH && oneDoc(o.docs, HOME_PATH) && navsOk(o) && cleanScreen(o)
    && o.sf403 === 0 && o.notice === true && o.hero === 0 && no403(o) && noMc(o),
  // G2 · G10
  // ⬛ [낡은 줄 · 갈래 B2 이전] update: (o) => o.where === 'main' && o.path === UPDATE_PATH && oneDoc(o.docs, UPDATE_PATH) && navsOk(o) && cleanScreen(o),
  //   🔴 갈래 B2(D18 R-④) — G2 화면 고유 글(본문 h6 · 직원 경고 알림 1) · (D17) 403 알림 0
  // ⬛ [낡은 줄 · B2 고침 1 이전] update: (o) => o.where === 'main' && o.path === UPDATE_PATH && oneDoc(o.docs, UPDATE_PATH) && navsOk(o) && cleanScreen(o)
  // ⬛ [낡은 줄 · B2 고침 1 이전]   && o.updTitle === true && o.updWarn >= 1 && no403(o),
  //   🔴 B2 고침 1 ①(N1) — 직원 메인PC 확인 표 요청 0
  update: (o) => o.where === 'main' && o.path === UPDATE_PATH && oneDoc(o.docs, UPDATE_PATH) && navsOk(o) && cleanScreen(o)
    && o.updTitle === true && o.updWarn >= 1 && no403(o) && noMc(o),
  // G3 · G10(§7-1-6 약관 화면 흰 띠 0)
  termsFirst: (o) => o.where === 'terms' && o.path === TERMS_PATH && oneDoc(o.docs, TERMS_PATH) && cleanScreen(o),
  // §8-3 3V-P2 장면 G 줄 — 늦춤이 실제로 걸렸고(1 이상) 끝 = /terms · 문서 이동 1건
  termsSlow: (o) => o.slowHits >= 1 && o.where === 'terms' && o.path === TERMS_PATH && oneDoc(o.docs, TERMS_PATH),
  // G4
  // ⬛ [낡은 줄 · B2 고침 1 이전] relogin: (o) => o.where === 'main' && o.path === HOME_PATH && oneDoc(o.docs, HOME_PATH),
  //   🔴 B2 고침 1 ④(3V P2-3 · N4) — 매일 쓰는 재로그인 첫 화면도 「403 알림 0」 세 겹(+3초 뒤 잼 · 키 수 그대로)
  relogin: (o) => o.where === 'main' && o.path === HOME_PATH && oneDoc(o.docs, HOME_PATH) && no403(o),
  // G5 — /login 을 연 문서 + 그 화면이 낸 / (기존 동작). 마지막 문서 /=200 · 끊긴 문서 0
  // ⬛ [낡은 줄 · B2 고침 1 이전] loginDirect: (o) => o.where === 'main' && o.path === HOME_PATH && Array.isArray(o.docs) && o.docs.length >= 1
  // ⬛ [낡은 줄 · B2 고침 1 이전]   && o.docs[o.docs.length - 1] === `${HOME_PATH}=200` && o.docs.every((d) => /=200$/.test(d)),
  //   🔴 B2 고침 1 ④(N4 「loginDirect 검토」) — 로그인한 채 /login → / 도 새 문서 = 사이드바 다시 돎 ⇒ 같은 세 겹(+3초 뒤 잼)
  loginDirect: (o) => o.where === 'main' && o.path === HOME_PATH && Array.isArray(o.docs) && o.docs.length >= 1
    && o.docs[o.docs.length - 1] === `${HOME_PATH}=200` && o.docs.every((d) => /=200$/.test(d)) && no403(o),
  // §8-2 A-① 장면 G 줄 — 알림 20초 내내 보임 · 그동안 문서 이동 0 · [확인] 뒤 갈 곳 그대로 1건 · G10(D1 꼴 창)
  // ⬛ [낡은 줄 · 갈래 B2 이전] dialog: (o) => o.dialogShown === true && o.lostAtMs === null && o.heldMs >= HOLD_MS && o.docsDuringHold === 0 && o.clicked === true
  // ⬛ [낡은 줄 · 갈래 B2 이전]   && o.where === 'main' && o.path === UPDATE_PATH && oneDoc(o.docs, UPDATE_PATH) && cleanScreen(o),
  // ⬛ [낡은 줄 · B2 고침 1 이전] dialog: (o) => o.dialogShown === true && o.lostAtMs === null && o.heldMs >= HOLD_MS && o.docsDuringHold === 0 && o.clicked === true
  // ⬛ [낡은 줄 · B2 고침 1 이전]   && o.where === 'main' && o.path === UPDATE_PATH && oneDoc(o.docs, UPDATE_PATH) && cleanScreen(o) && no403(o),
  //   🔴 B2 고침 1 ①(N1) — 직원 메인PC 확인 표 요청 0
  dialog: (o) => o.dialogShown === true && o.lostAtMs === null && o.heldMs >= HOLD_MS && o.docsDuringHold === 0 && o.clicked === true
    && o.where === 'main' && o.path === UPDATE_PATH && oneDoc(o.docs, UPDATE_PATH) && cleanScreen(o) && no403(o) && noMc(o),
  // G8 · G8+ — 카드 전부(큰 카드 1 이상 · 보조 3) · 안내 한 장 0 · 흰 띠 0 · 앱 표준오류 꼴 0(「권한 설정 필요」 는 기록만)
  // ⬛ [낡은 줄 · B2 고침 1 이전] owner: (o) => o.where === 'main' && o.path === HOME_PATH && o.hero >= 1 && o.labels === OWNER_LABELS.length && o.notice === false
  // ⬛ [낡은 줄 · B2 고침 1 이전]   && bandsOff(o) && o.appErr === 0,
  //   🔴 B2 고침 1 ①(N1 · 3V P3-1) — 대표 왕복이 실제로 돎(메인PC 확인 표 요청 1 이상). 거름이 넓어져 대표도 생략하면 빨강(변이 V16)
  owner: (o) => o.where === 'main' && o.path === HOME_PATH && o.hero >= 1 && o.labels === OWNER_LABELS.length && o.notice === false
    && bandsOff(o) && o.appErr === 0 && o.mcReq >= 1,
  // G11 — 가로챔이 걸렸고 · 쉬운 알림 1 이상 · 흰 띠 0 · 경고 흔적 1 이상 · 앱 표준오류 꼴 0 · 알림 글 예외 꼴 0
  owner503: (o) => o.routeHits >= 1 && o.failSnack >= 1 && bandsOff(o) && o.warnDash >= 1 && o.appErr === 0 && snackBadCount(o.snacks) === 0,
  // G12 — 카드 보임 · 판매·재무 403 0 · 안내 한 장 0
  // ⬛ [낡은 줄 · 갈래 B2 이전] sales: (o) => o.where === 'main' && o.path === HOME_PATH && o.hero >= 1 && o.labels === OWNER_LABELS.length && o.notice === false && o.sf403 === 0,
  // ⬛ [낡은 줄 · B2 고침 1 이전] sales: (o) => o.where === 'main' && o.path === HOME_PATH && o.hero >= 1 && o.labels === OWNER_LABELS.length && o.notice === false && o.sf403 === 0
  // ⬛ [낡은 줄 · B2 고침 1 이전]   && no403(o),
  //   🔴 B2 고침 1 ①(N1) — 직원 메인PC 확인 표 요청 0
  sales: (o) => o.where === 'main' && o.path === HOME_PATH && o.hero >= 1 && o.labels === OWNER_LABELS.length && o.notice === false && o.sf403 === 0
    && no403(o) && noMc(o),
  // 🔴 갈래 B2(D18 R-③ · D17 양성 대조) — 두 가로챔이 다 걸림 · 쉬운 알림 1 이상 · 일반 catch 를 지난 표(경고 글 안 JsonException 또는 알림 색 Error) ·
  //   셈의 눈이 살아 있음(처리기 경고 줄 · 처리기 알림 1 이상) · 뜬 알림 전부 허용 목록 안 · 날 글 0 · 흰 띠 0 · 경고 흔적 1 이상 · 앱 표준오류 꼴 0
  // ⬛ [낡은 줄 · B2 고침 1 이전] ownerJson: (o) => o.routeHits >= 1 && o.ctlHits >= 1 && o.failSnack >= 1 && (o.warnJson >= 1 || o.failSnackErr >= 1)
  // ⬛ [낡은 줄 · B2 고침 1 이전]   && o.h403 >= 1 && o.forbidSeen >= 1 && o.outsideAllow === 0 && snackBadCount(o.snacks) === 0 && snackBadCount(o.seenSnacks) === 0
  // ⬛ [낡은 줄 · B2 고침 1 이전]   && bandsOff(o) && o.warnDash >= 1 && o.appErr === 0,
  //   🔴 B2 고침 1 ③(3V P2-2 · N3) — 세 겹 모두의 같은 런 대조: ⓐ 403 응답 셈(page.on response)도 1 이상이어야 함
  ownerJson: (o) => o.routeHits >= 1 && o.ctlHits >= 1 && o.failSnack >= 1 && (o.warnJson >= 1 || o.failSnackErr >= 1)
    && o.f403n >= 1 && o.h403 >= 1 && o.forbidSeen >= 1 && o.outsideAllow === 0 && snackBadCount(o.snacks) === 0 && snackBadCount(o.seenSnacks) === 0
    && bandsOff(o) && o.warnDash >= 1 && o.appErr === 0,
  // 🔴 HQA 보류 고침 · 물러남(⑪ · PM 결재 덧) — 늦춤이 걸렸고 · 진행 그림을 걸었고 · 누름 +5초 표본 = 막대 보임 · 아이디 칸 0
  //   (변이 Y2 「물러남 30초 → 0」 이면 +5초에 이미 입력 칸 = 거짓). terms-slow-doc 판정(문서 정확히 1건)은 손 0.
  termsSlowProg: (o) => o.slowHits >= 1 && o.progArm === 'ok' && !!o.prog && !!o.prog.at5 && !o.prog.at5.err
    && o.prog.at5.bar === true && o.prog.at5.id === 0,
  // 🔴 HQA 보류 고침 · 길 A2(⑪ · 고침2 설계 §7-2) — 첫 붙잡은 문서 = /terms · 표본기가 돌았고 · 끝 표본 = 고치기 전 꼴(빈 로그인 입력 화면) ·
  //   아이디 · 비밀번호 넣으면 단추 켜짐 · 중지 뒤 새 문서 요청 0 · 새 문서 적재 0(`/` 덮어쓰기 0). 성립(중지 돎 · 취소됨)은 장면이 먼저 거른다(불성립 = DEVICE).
  stopAfterLogin: (o) => o.valid === true && o.firstHeld === TERMS_PATH && o.samples >= STALL_MIN_SAMPLES && formBack(o.last) && o.btnOn === true
    && o.docsAfter === 0 && o.dclAfter === 0,
  // 🔴 HQA 보류 고침 · 길 B(⑪) — 복원 성립(마지막 pageshow persisted=참 · 복원 뒤 /login 문서 요청 0) · 표본기가 돌았고 · 끝 표본 = 고치기 전 꼴 ·
  //   단추 켜짐 · 복원 뒤 새 문서 요청 0. 복원 불성립은 장면이 먼저 거른다(DEVICE).
  backRestore: (o) => o.persisted === true && o.loginDocAfter === false && o.samples >= STALL_MIN_SAMPLES && formBack(o.last) && o.btnOn === true
    && o.docsAfter === 0,
});

// 문서 이동 기록(끝이 「대기」 인 것은 5초까지 기다림 — requestfinished 뒤 응답 읽기가 비동기)
async function docsNow(kb, ms = 5000) {
  const end = Date.now() + ms;
  while (kb.dx.docs.some((d) => d.end === '대기') && Date.now() < end) await K.sleep(200);
  return kb.dx.docs.map((d) => `${d.path}=${d.end}`);
}
// 화면 한 번 보기 — 흰 띠 보임 · 보이는 알림(스낵바) 글(원문은 메모리에만 · 찍을 때 snackShow 로 소독)
async function look(page) {
  return K.withTimeout(page.evaluate(() => {
    const flat = (e) => String((e && (e.innerText || e.textContent)) || '').replace(/\s+/g, ' ').trim();
    const b = document.getElementById('blazor-error-ui');
    const band = !!b && getComputedStyle(b).display !== 'none';
    const snacks = Array.from(document.querySelectorAll('.mud-snackbar')).map(flat).filter((x) => x.length > 0).slice(0, 10);
    return { band, snacks };
  }), K.EVAL_MS, '화면 보기').catch((e) => {
    console.log(`K 닫힌문 화면 보기 실패 ${e instanceof K.KitStepError ? L.sanitize(e.message, 80) : L.errorName(e)}`);
    return { band: null, snacks: [] };
  });
}
// G10 — 착지 +3초 · +10초 두 번(흰 띠 둘 · 알림 글 합침)
async function landLook(kb, landAt) {
  await K.sleep(Math.max(0, landAt + 3000 - Date.now()));
  const a = await look(kb.page);
  await K.sleep(Math.max(0, landAt + 10000 - Date.now()));
  const b = await look(kb.page);
  return { band3: a.band, band10: b.band, snacks: [...new Set([...a.snacks, ...b.snacks])] };
}
const snackShow = (snacks) => (snacks.length === 0 ? '없음' : snacks.slice(0, 4).map((t) => K.consoleLine(t, 60)).join(' | '));
// 첫 화면 카드 — 판매·재무 묶음(큰 카드 · 보조 카드 이름) · 안내 한 장 · 「권한 설정 필요」 안내(기록만). 거름이 풀릴 때까지 15초 기다림.
async function cards(page) {
  await page.locator('.hp-kpi-hero, .hp-no-work-notice').first().waitFor({ state: 'visible', timeout: 15000 })
    .catch((e) => console.log(`K 닫힌문 첫 화면 카드 · 안내 둘 다 안 보임(15초) ${L.errorName(e)}`));
  return K.withTimeout(page.evaluate((labels) => {
    const vis = (e) => !!e && e.getClientRects().length > 0 && getComputedStyle(e).visibility !== 'hidden';
    const hero = Array.from(document.querySelectorAll('.hp-kpi-hero')).filter(vis).length;
    const seenLabels = Array.from(document.querySelectorAll('.hp-kpi-label')).filter(vis).map((e) => String(e.textContent || '').trim());
    const notice = Array.from(document.querySelectorAll('.hp-no-work-notice')).some(vis);
    const permAlert = Array.from(document.querySelectorAll('.hp-alert-title')).some((e) => String(e.textContent || '').includes('권한 설정이 필요합니다'));
    return { hero, labels: labels.filter((t) => seenLabels.includes(t)).length, notice, permAlert };
  }, OWNER_LABELS), K.EVAL_MS, '카드 보기').catch((e) => {
    console.log(`K 닫힌문 카드 보기 실패 ${e instanceof K.KitStepError ? L.sanitize(e.message, 80) : L.errorName(e)}`);
    return { hero: null, labels: null, notice: null, permAlert: null };
  });
}
// 장면 로그 한 줄(D10 재료 — 403 목록 · 보이는 알림 글 · 값 · 토큰 0)
function sceneLog(kb, label, o) {
  console.log(`K 닫힌문 장면 ${label} 판정=${o.pass} · 착지 ${o.where}@${L.sanitize(String(o.path), 80)} · 문서 ${L.sanitize((o.docs || []).join(' > ') || '없음', 300)}`
    + ` · 흰 띠 +3초 ${o.band3} +10초 ${o.band10} · 앱 표준오류 ${o.appErr} · 판매·재무 부름 ${kb.gt.sfReq} 403 ${kb.gt.sf403}`
    + ` · 403 ${kb.gt.f403.length}건 ${L.sanitize(kb.gt.f403.join(', ') || '없음', 400)} · 알림 글 ${snackShow(o.snacks || [])}`
    // 🔴 갈래 B2(D17) — 세 겹 셈 · 뜬 알림 글(찍기 사이에 사라진 것까지)
    // ⬛ [낡은 줄 · B2 고침 1 이전] + ` · 403 수 ${kb.gt.f403n} · 처리기 경고 ${kb.gt.h403} · 처리기 알림 ${o.forbidSeen} · 뜬 알림 ${snackShow(o.seenSnacks || [])}`);
    + ` · 403 수 ${kb.gt.f403n} · 처리기 경고 ${kb.gt.h403} · 처리기 알림 ${o.forbidSeen} · 뜬 알림 ${snackShow(o.seenSnacks || [])}`
    // 🔴 B2 고침 1 ①(메인PC 확인 표 요청) · ②(뜬 알림 보기 실패)
    + ` · 메인PC 표 요청 ${kb.gt.mcReq} · 뜬 알림 보기 실패 ${o.seenErr}`);
}
// 장면 공통 재료 묶기(판정 함수 입력 · got 앞부분)
function gather(kb, s, lk, docs, extra = {}) {
  return {
    where: s.where, path: s.path, docs, navs: [...kb.navs], band3: lk.band3, band10: lk.band10, snacks: lk.snacks,
    appErr: kb.gt.appErr, sf403: kb.gt.sf403, sfReq: kb.gt.sfReq, warnDash: kb.gt.warnDash,
    // 🔴 갈래 B2(D17 ⓐ ⓑ) — 403 응답 수 · 처리기 경고 줄 · 일반 catch 경고 표(ⓒ 는 장면이 snackTally 로 extra 에 넣음)
    // ⬛ [낡은 줄 · B2 고침 1 이전] f403n: kb.gt.f403n, h403: kb.gt.h403, warnJson: kb.gt.warnJson, f403list: [...kb.gt.f403], ...extra,
    // 🔴 B2 고침 1 ①(mcReq) · ⑦(pendReq · pend403)
    f403n: kb.gt.f403n, h403: kb.gt.h403, warnJson: kb.gt.warnJson, f403list: [...kb.gt.f403], mcReq: kb.gt.mcReq, pendReq: kb.gt.pendReq, pend403: kb.gt.pend403, ...extra,
  };
}
// 🔴 갈래 B2(D18 R-④) — 수동 업데이트 화면 고유 글(본문 h6) · 직원 경고 알림(MudAlert 경고 색) 수 · 기록 겸 G2 판정 재료
async function updLook(page) {
  return K.withTimeout(page.evaluate((title) => {
    const vis = (e) => !!e && e.getClientRects().length > 0 && getComputedStyle(e).visibility !== 'hidden';
    const flat = (e) => String((e && (e.innerText || e.textContent)) || '').replace(/\s+/g, ' ').trim();
    const updTitle = Array.from(document.querySelectorAll('.mud-typography-h6')).some((e) => vis(e) && flat(e) === title);
    const updWarn = Array.from(document.querySelectorAll('.mud-alert')).filter((e) => vis(e) && /warning/i.test(String(e.className || ''))).length;
    return { updTitle, updWarn };
  }, UPD_TITLE), K.EVAL_MS, '수동 업데이트 화면 보기').catch((e) => {
    console.log(`K 닫힌문 수동 업데이트 화면 보기 실패 ${e instanceof K.KitStepError ? L.sanitize(e.message, 80) : L.errorName(e)}`);
    return { updTitle: null, updWarn: null };
  });
}
// 🔴 갈래 B2(D18 R-④ · 기록만 · 판정 아님) — 로그인 누름 뒤 문서 요청이 늦춰진 동안 Login 진행 그림
//   (진행 막대 보임 · 아이디 칸 수 · 로그인 단추 꺼짐). 가설 ⓐ 직접 관찰 재료 — ⓐ 가 틀려도 고침은 맞음(V1 · V2 가 판별).
const PROG_LOOK_MS = 5000; // 누름 + 5초(늦춤 20초 안)
async function progLook(page) {
  return K.withTimeout(page.evaluate(() => {
    const vis = (e) => !!e && e.getClientRects().length > 0 && getComputedStyle(e).visibility !== 'hidden';
    const flat = (e) => String((e && (e.innerText || e.textContent)) || '').replace(/\s+/g, ' ').trim();
    const bar = Array.from(document.querySelectorAll('.mud-progress-linear')).some(vis);
    const idField = Array.from(document.querySelectorAll('label')).filter((e) => vis(e) && flat(e) === '아이디').length;
    const btn = Array.from(document.querySelectorAll('button')).find((e) => vis(e) && flat(e) === '로그인');
    return { bar, idField, btnDisabled: btn ? btn.disabled === true : null };
  }), K.EVAL_MS, '진행 그림 보기').catch((e) => ({ bar: null, idField: null, btnDisabled: null, err: e instanceof K.KitStepError ? L.sanitize(e.message, 80) : L.errorName(e) }));
}
const progShow = (p) => (p ? `진행 막대 ${p.bar} · 아이디 칸 ${p.idField} · 로그인 단추 꺼짐 ${p.btnDisabled}${p.err ? ` · 실패 ${p.err}` : ''}` : '못 봄');
// got 꼴(300자 상한 — 판정 칸 먼저 · 알림 글은 소독 글자로)
function gotOf(o, keys) {
  const g = {};
  // ⬛ [낡은 줄 · 갈래 B2 이전] for (const k of keys) g[k] = k === 'snacks' ? snackShow(o.snacks || []) : o[k];
  for (const k of keys) g[k] = k === 'snacks' || k === 'seenSnacks' ? snackShow(o[k] || []) : o[k];
  return g;
}

// 화면 로그인 — kit35.screenLogin 과 같은 동작 + [최신버젼업데이트] 를 고를 수 있게
// ⬛ [낡은 줄 · 고침 3] async function loginScreen(kb, loginId, password, { update = false } = {}) {
async function loginScreen(kb, loginId, password, { update = false, beforeClick = null } = {}) {
  const page = kb.page;
  await page.goto(`${K.BASE}/login`, { waitUntil: 'domcontentloaded', timeout: 60000 });
  await page.getByLabel('아이디', { exact: true }).fill(loginId, { timeout: 60000 });
  await page.getByLabel('비밀번호', { exact: true }).fill(password);
  await page.getByLabel('비밀번호', { exact: true }).press('Tab');
  if (update) {
    await page.getByRole('button', { name: UPDATE_BTN }).first().click({ timeout: 60000 });
    if (!(await seen(page, UPDATE_NOTICE, 10000))) throw new K.KitStepError('[최신버젼업데이트] 누름 안내가 안 뜸(단추가 안 눌림)');
  }
  kb.navs.length = 0; // 로그인 누름 뒤 이동만 센다
  // 🔴 고침 2 — 진단도 로그인 누름 뒤만 센다
  kb.dx.docs.length = 0; kb.dx.fails.length = 0; kb.dx.failCount = 0; kb.dx.unhandled.length = 0;
  gtReset(kb); // 🔴 갈래 T — 판정 재료도 로그인 누름 뒤만
  // 🔴 갈래 T — 누름 직전 끼울 자리(문서 요청 늦춤 장치 · 장면 G 줄 「문서 요청만 20초 늦춤」). 제품 손 0.
  if (typeof beforeClick === 'function') await beforeClick(kb);
  // ⬛ [낡은 줄 · B2 고침 1 이전] await page.getByRole('button', { name: '로그인', exact: true }).click();
  // 🔴 B2 고침 1 ⑤(N5) — 누름 직전 시각(빈 시간 기록의 0점 · 판정 아님)
  const clickAt = Date.now();
  await page.getByRole('button', { name: '로그인', exact: true }).click();
  const note = { closed: 0, errs: 0 };
  const s = await DC.settleScreen(page, 90000, note);
  const landAt = Date.now(); // 🔴 갈래 T — 착지(자리 잡기 끝) · +3초 · +10초 보기의 0점
  const end = Date.now() + 10000;
  while (kb.login.seen === 0 && Date.now() < end) await K.sleep(250);
  await K.waitMasked(kb.tok);
  // ⬛ [낡은 줄 · 고침 1] return s;  — 🔴 고침 2 ② 새 기기 알림 창을 장치가 닫은 횟수(note)를 같이 돌려준다(진단)
  // ⬛ [낡은 줄 · 고침 3] return { ...s, noticeClosed: note.closed, noticeCloseErrs: note.errs };
  // ⬛ [낡은 줄 · B2 고침 1 이전] return { ...s, noticeClosed: note.closed, noticeCloseErrs: note.errs, landAt };
  return { ...s, noticeClosed: note.closed, noticeCloseErrs: note.errs, landAt, clickAt };
}

// ── 장면 1 · 2 · 3: 첫 로그인 → 첫 화면 · 직접 /welcome · 기기 관문 ──
async function sceneHome(st) {
  const step = { now: '첫 로그인 상태 재현' };
  let kb = null;
  try {
    if (!st.prepOk) throw new K.KitStepError(`준비 실패 — ${st.prepWhy}`);
    resetFirstLogin(st);
    step.now = '브라우저 열기 · 화면 로그인';
    kb = await openCtx(st, 'S3HOME');
    const s = await loginScreen(kb, ID.fresh, st.freshPw);
    if (kb.login.seen === 0 || kb.login.redirect !== true || kb.login.terms !== false) {
      throw new K.KitStepError(`첫 로그인 상태 아님(로그인 응답 ${kb.login.seen}회 · 첫 로그인=${kb.login.redirect} · 약관 필요=${kb.login.terms} · status=${kb.login.status})`);
    }
    await DC.closeBriefing(kb.page, kb.br, 3000);
    // ⬛ [낡은 줄 · 고침 3] const band = await errBand(kb.page);
    // ⬛ [낡은 줄 · 고침 3] const navs = [...kb.navs];
    // ⬛ [낡은 줄 · 고침 3] const pass = s.where === 'main' && s.path === HOME_PATH && navs.includes(HOME_PATH) && !navs.some(isWelcome) && band === false;
    // ⬛ [낡은 줄 · 고침 3] const got = { where: s.where, path: s.path, navs, loginRedirectToWelcome: kb.login.redirect, loginTerms: kb.login.terms, errBand: band };
    // 🔴 갈래 T — 대장 닫음 · G1 · G7 · G10 을 동작으로(문서 이동 1건 · 흰 띠 두 번 · 표준오류 꼴 · 판매·재무 403 · 알림 글 · 안내 한 장)
    const lk = await landLook(kb, s.landAt);
    const cd = await cards(kb.page);
    const docs = await docsNow(kb);
    // ⬛ [낡은 줄 · 갈래 B2 이전] const o = gather(kb, s, lk, docs, { notice: cd.notice, hero: cd.hero });
    // 🔴 갈래 B2(D17 ⓒ) — 뜬 적 있는 알림 셈을 판정 재료에
    const sn = snackTally(await seenSnacks(kb.page));
    const o = gather(kb, s, lk, docs, { notice: cd.notice, hero: cd.hero, ...sn });
    const band = lk.band3 === true || lk.band10 === true ? true : (bandsOff(lk) ? false : null);
    const navs = o.navs;
    const pass = JUDGE.home(o);
    sceneLog(kb, 'first-home', { ...o, pass });
    // ⬛ [낡은 줄 · 갈래 B2 이전] const got = { ...gotOf(o, ['where', 'path', 'docs', 'band3', 'band10', 'appErr', 'sf403', 'notice', 'hero', 'snacks']), loginRedirectToWelcome: kb.login.redirect };
    // ⬛ [낡은 줄 · B2 고침 1 이전] const got = { ...gotOf(o, ['where', 'path', 'docs', 'band3', 'band10', 'appErr', 'sf403', 'notice', 'hero', 'f403n', 'h403', 'forbidSeen', 'snacks', 'f403list']), loginRedirectToWelcome: kb.login.redirect };
    const got = { ...gotOf(o, ['where', 'path', 'docs', 'band3', 'band10', 'appErr', 'sf403', 'notice', 'hero', 'f403n', 'h403', 'forbidSeen', 'seenErr', 'mcReq', 'snacks', 'f403list']), loginRedirectToWelcome: kb.login.redirect };
    // ⬛ [낡은 줄 · 고침 1] rec(st, 'home', { pass, got });  — 🔴 고침 2 실패 때만 진단을 싣는다(판정 칸 그대로)
    // ⬛ [낡은 줄 · 고침 2] rec(st, 'home', { pass, got: pass ? got : { ...got, dx: diag(kb, { noticeClosed: s.noticeClosed, noticeCloseErrs: s.noticeCloseErrs }) } });
    // 🔴 고침 3 — 판정 식 그대로 · 거짓일 때만 알려진 꼴 계산(머리 글자는 rec 가 붙임)
    //   갈래 T — 대장 목록 0 이라 knownName 이 null → 머리 0(면제 0). 꼴 계산 장치는 남김(W-S-03 고침 3 §8-4).
    const known = !pass && homeKnown(s, navs, band);
    rec(st, 'home', { pass, known, got: pass ? got : { ...got, dx: diag(kb, { noticeClosed: s.noticeClosed, noticeCloseErrs: s.noticeCloseErrs }) } });
  } catch (e) {
    dev(st, 'home', step, e);
  }
  // 장면 2 — 같은 세션 그대로 주소창에 /welcome
  const step2 = { now: '직접 /welcome' };
  try {
    if (!kb || !kb.tok.access) throw new K.KitStepError(`로그인 세션 없음 — 장면 1 이 앞에서 섰다(${step.now})`);
    kb.navs.length = 0;
    await kb.page.goto(`${K.BASE}${WELCOME_PATH}`, { waitUntil: 'domcontentloaded', timeout: 60000 });
    const notice = await seen(kb.page, NOTICE_TITLE);
    await DC.closeBriefing(kb.page, kb.br, 3000);
    const old = await kb.page.getByText(WELCOME_OLD, { exact: false }).count();
    const at = K.pathOf(kb.page.url()).split('?')[0];
    const band = await errBand(kb.page);
    rec(st, 'welcome', { pass: notice && old === 0 && isWelcome(at) && band === false, got: { notice, oldWelcomeText: old, path: at, navs: [...kb.navs], errBand: band } });
  } catch (e) {
    dev(st, 'welcome', step2, e);
  }
  // 장면 3 — 기기 관문 · 메인PC 확인 길(같은 세션 출입증)
  const step3 = { now: '기기 관문 · 메인PC 확인 요청' };
  try {
    if (!kb || !kb.tok.access) throw new K.KitStepError('로그인 세션 없음 — 장면 1 이 앞에서 섰다');
    const sess = { who: kb.who, token: kb.tok.access, deviceId: kb.tok.deviceId };
    const g = await K.apiCall('GET', '/api/devices/gate-status', { sess });
    const m = await K.apiCall('GET', '/api/devices/is-main-pc', { sess });
    const isMain = m.json && typeof m.json === 'object' ? (m.json.isMainPc ?? m.json.IsMainPc ?? null) : null;
    const pass = g.status === 200 && !!g.json && m.status === 200 && typeof isMain === 'boolean';
    rec(st, 'gate', { pass, got: { gateStatus: g.status, gateBody: g.json ? 'JSON' : '없음', isMainPcStatus: m.status, isMainPc: isMain } });
  } catch (e) {
    dev(st, 'gate', step3, e);
  } finally {
    if (kb) await closeKb(st, kb);
  }
}

// ── 장면 4: 첫 로그인 + [최신버젼업데이트] → 수동 업데이트 화면 ──
async function sceneUpdate(st) {
  const step = { now: '첫 로그인 상태 재현' };
  let kb = null;
  try {
    if (!st.prepOk) throw new K.KitStepError(`준비 실패 — ${st.prepWhy}`);
    resetFirstLogin(st);
    step.now = '브라우저 열기 · [최신버젼업데이트] · 화면 로그인';
    kb = await openCtx(st, 'S3UPD');
    const s = await loginScreen(kb, ID.fresh, st.freshPw, { update: true });
    if (kb.login.seen === 0 || kb.login.redirect !== true || kb.login.terms !== false) {
      throw new K.KitStepError(`첫 로그인 상태 아님(로그인 응답 ${kb.login.seen}회 · 첫 로그인=${kb.login.redirect} · 약관 필요=${kb.login.terms} · status=${kb.login.status})`);
    }
    // ⬛ [낡은 줄 · 고침 3] const band = await errBand(kb.page);
    // ⬛ [낡은 줄 · 고침 3] const navs = [...kb.navs];
    // ⬛ [낡은 줄 · 고침 3] const pass = navs.includes(UPDATE_PATH) && !navs.some(isWelcome) && band === false;
    // ⬛ [낡은 줄 · 고침 3] const got = { navs, settled: `${s.where}@${s.path}`, loginRedirectToWelcome: kb.login.redirect, errBand: band };
    // 🔴 갈래 T — 대장 닫음 · G2 · G10 을 동작으로(문서 이동 1건 /data/update=200 · 수동 업데이트 화면 자리 잡음 · 흰 띠 두 번 · 표준오류 꼴 · 알림 글)
    const lk = await landLook(kb, s.landAt);
    const docs = await docsNow(kb);
    // ⬛ [낡은 줄 · 갈래 B2 이전] const o = gather(kb, s, lk, docs);
    // 🔴 갈래 B2(D17 ⓒ · D18 R-④) — 뜬 알림 셈 · 수동 업데이트 화면 고유 글
    const sn = snackTally(await seenSnacks(kb.page));
    const ul = await updLook(kb.page);
    const o = gather(kb, s, lk, docs, { ...sn, ...ul });
    const band = lk.band3 === true || lk.band10 === true ? true : (bandsOff(lk) ? false : null);
    const navs = o.navs;
    const pass = JUDGE.update(o);
    sceneLog(kb, 'first-update', { ...o, pass });
    // ⬛ [낡은 줄 · 갈래 B2 이전] const got = { ...gotOf(o, ['where', 'path', 'docs', 'band3', 'band10', 'appErr', 'snacks']), loginRedirectToWelcome: kb.login.redirect };
    // ⬛ [낡은 줄 · B2 고침 1 이전] const got = { ...gotOf(o, ['where', 'path', 'docs', 'band3', 'band10', 'appErr', 'updTitle', 'updWarn', 'f403n', 'h403', 'forbidSeen', 'snacks', 'f403list']), loginRedirectToWelcome: kb.login.redirect };
    const got = { ...gotOf(o, ['where', 'path', 'docs', 'band3', 'band10', 'appErr', 'updTitle', 'updWarn', 'f403n', 'h403', 'forbidSeen', 'seenErr', 'mcReq', 'snacks', 'f403list']), loginRedirectToWelcome: kb.login.redirect };
    // ⬛ [낡은 줄 · 고침 1] rec(st, 'update', { pass, got });  — 🔴 고침 2 실패 때만 진단을 싣는다(판정 칸 그대로)
    // ⬛ [낡은 줄 · 고침 2] rec(st, 'update', { pass, got: pass ? got : { ...got, dx: diag(kb, { noticeClosed: s.noticeClosed, noticeCloseErrs: s.noticeCloseErrs }) } });
    // 🔴 고침 3 — 판정 식 그대로 · 거짓일 때만 알려진 꼴 계산(문서 이동 기록 = diag 와 같은 글자 꼴)
    // ⬛ [낡은 줄 · 고침 3] const known = !pass && updateKnown(navs, kb.dx.docs.map((d) => `${d.path}=${d.end}`), band);
    // 🔴 고침 3-2 — 장면 s(최종 착지)를 같이 넘김
    //   main 합침(c50ca6cf · 갈래 T) — 대장 목록 0 이라 knownName 이 null → 머리 0(면제 0). 최종 착지 조건 · K-C27i 는 함수 자체를 재는 장치로 남김.
    const known = !pass && updateKnown(s, navs, kb.dx.docs.map((d) => `${d.path}=${d.end}`), band);
    rec(st, 'update', { pass, known, got: pass ? got : { ...got, dx: diag(kb, { noticeClosed: s.noticeClosed, noticeCloseErrs: s.noticeCloseErrs }) } });
  } catch (e) {
    dev(st, 'update', step, e);
  } finally {
    if (kb) await closeKb(st, kb);
  }
}

// ── 🔴 갈래 T 장면 — 약관 미동의 직원 G3 · 문서 요청 늦춤(장면 G 줄 · §8-3 3V-P2) ──
// 문서 요청 늦춤 장치(시험 몫 · 앱 손 0) — 주 프레임 문서 요청만 ms 기다린 뒤 다음 처리기(외부 차단)로 넘김. 나머지 요청은 바로 넘김.
async function slowDocs(kb, ms) {
  const page = kb.page;
  const slow = { hits: 0, passErrs: 0, handler: null };
  slow.handler = async (route) => {
    let main = false;
    try {
      main = route.request().isNavigationRequest() && route.request().frame() === page.mainFrame();
    } catch (e) {
      main = false; // 서비스 워커 요청 등 frame() 이 던지는 꼴 — 문서 아님
    }
    if (main) {
      slow.hits += 1;
      await K.sleep(ms);
    }
    await route.fallback().catch((e) => {
      slow.passErrs += 1;
      console.log(`K 닫힌문 문서 늦춤 넘김 실패 ${L.errorName(e)}(앞 이동이 취소됐으면 생김 · 판정은 문서 이동 기록)`);
    });
  };
  await page.route('**/*', slow.handler);
  return slow;
}
async function sceneTerms(st) {
  const step = { now: '약관 미동의 직원 확인' };
  let kb = null;
  try {
    if (!st.termsOk) throw new K.KitStepError(`준비 실패 — ${st.termsWhy}`);
    step.now = '브라우저 열기 · 화면 로그인';
    kb = await openCtx(st, 'S3T1');
    const s = await loginScreen(kb, TERMS_ID, st.termsPw);
    if (kb.login.seen === 0 || kb.login.terms !== true) {
      throw new K.KitStepError(`약관 필요 상태 아님(로그인 응답 ${kb.login.seen}회 · 약관 필요=${kb.login.terms} · status=${kb.login.status})`);
    }
    const lk = await landLook(kb, s.landAt);
    const docs = await docsNow(kb);
    // ⬛ [낡은 줄 · 갈래 B2 이전] const o = gather(kb, s, lk, docs);
    // 🔴 갈래 B2 — 세 겹 셈은 기록만(판정 그대로 · V12 때 대조 장면)
    const o = gather(kb, s, lk, docs, snackTally(await seenSnacks(kb.page)));
    const pass = JUDGE.termsFirst(o);
    sceneLog(kb, 'terms-first', { ...o, pass });
    // ⬛ [낡은 줄 · 갈래 B2 이전] const got = gotOf(o, ['where', 'path', 'docs', 'band3', 'band10', 'appErr', 'snacks']);
    const got = gotOf(o, ['where', 'path', 'docs', 'band3', 'band10', 'appErr', 'f403n', 'h403', 'forbidSeen', 'snacks']);
    rec(st, 'termsFirst', { pass, got: pass ? got : { ...got, dx: diag(kb, { noticeClosed: s.noticeClosed }) } });
  } catch (e) {
    dev(st, 'termsFirst', step, e);
  } finally {
    if (kb) await closeKb(st, kb);
  }
  // 장면 G 줄 — 같은 직원(아직 약관 미동의) · 새 창 · 로그인 누른 뒤 문서 요청만 20초 늦춤
  const step2 = { now: '약관 미동의 직원 확인' };
  let kb2 = null;
  try {
    if (!st.termsOk) throw new K.KitStepError(`준비 실패 — ${st.termsWhy}`);
    step2.now = '브라우저 열기 · 문서 요청 늦춤 · 화면 로그인';
    kb2 = await openCtx(st, 'S3TSLOW');
    let slow = null;
    // ⬛ [낡은 줄 · 갈래 B2 이전] const s = await loginScreen(kb2, TERMS_ID, st.termsPw, { beforeClick: async (k) => { slow = await slowDocs(k, HOLD_MS); } });
    // 🔴 갈래 B2(D18 R-④ · 기록만 · 판정 아님) — 늦춤 중 1회(누름 + 5초) Login 진행 그림 보기(가설 ⓐ 직접 관찰 재료)
    // ⬛ [낡은 몸 · B2 고침 1 이전] 누름 + 5초에 page.evaluate(progLook) 1회 — CI 사본 :2174 셋 다 null(평가 10초 상한).
    // ⬛ let progP = null;
    // ⬛ const s = await loginScreen(kb2, TERMS_ID, st.termsPw, {
    // ⬛   beforeClick: async (k) => {
    // ⬛     slow = await slowDocs(k, HOLD_MS);
    // ⬛     progP = K.sleep(PROG_LOOK_MS).then(() => progLook(k.page));
    // ⬛   },
    // ⬛ });
    // ⬛ const prog = progP ? await progP : null;
    // 🔴 B2 고침 1 ⑥(N6) — 누름 직전 그 문서에 진행 그림 console.log 를 걸고(평가는 누름 전 1번뿐) 콘솔로 받는다. 기록만 · 판정 아님.
    const progLines = [];
    const onProg = (msg) => {
      const t = String(msg.text());
      if (t.startsWith(PROG_TAG) && progLines.length < PROG_TICKS + 6) progLines.push(t);
    };
    kb2.page.on('console', onProg);
    let progArm = null;
    const s = await loginScreen(kb2, TERMS_ID, st.termsPw, {
      beforeClick: async (k) => {
        slow = await slowDocs(k, HOLD_MS);
        progArm = await K.withTimeout(k.page.evaluate(PROG_WATCH), K.EVAL_MS, '진행 그림 걸기').then(() => 'ok',
          (e) => (e instanceof K.KitStepError ? L.sanitize(e.message, 80) : L.errorName(e)));
      },
    });
    kb2.page.off('console', onProg);
    const prog = progSum(progLines);
    if (slow) await kb2.page.unroute('**/*', slow.handler).catch((e) => console.log(`K 닫힌문 문서 늦춤 풀기 실패 ${L.errorName(e)}`));
    if (kb2.login.seen === 0 || kb2.login.terms !== true) {
      throw new K.KitStepError(`약관 필요 상태 아님(로그인 응답 ${kb2.login.seen}회 · 약관 필요=${kb2.login.terms} · status=${kb2.login.status})`);
    }
    const docs = await docsNow(kb2);
    // ⬛ [낡은 줄 · 갈래 B2 이전] const o = { where: s.where, path: s.path, docs, slowHits: slow ? slow.hits : 0, slowPassErrs: slow ? slow.passErrs : null };
    // ⬛ [낡은 줄 · B2 고침 1 이전] const o = { where: s.where, path: s.path, docs, slowHits: slow ? slow.hits : 0, slowPassErrs: slow ? slow.passErrs : null, prog };
    const o = { where: s.where, path: s.path, docs, slowHits: slow ? slow.hits : 0, slowPassErrs: slow ? slow.passErrs : null, prog, progArm };
    const pass = JUDGE.termsSlow(o);
    // ⬛ [낡은 줄 · B2 고침 1 이전] console.log(`K 닫힌문 장면 terms-slow-doc 진행 그림(기록) ${progShow(prog)}`);
    console.log(`K 닫힌문 장면 terms-slow-doc 진행 그림(기록 · 콘솔 꼴) 걸기 ${progArm} · ${progSumShow(prog)}`);
    console.log(`K 닫힌문 장면 terms-slow-doc 판정=${pass} · 착지 ${o.where}@${L.sanitize(String(o.path), 80)} · 문서 ${L.sanitize(docs.join(' > ') || '없음', 300)} · 늦춘 문서 ${o.slowHits} · 넘김 실패 ${o.slowPassErrs}`);
    rec(st, 'termsSlow', { pass, got: pass ? o : { ...o, dx: diag(kb2) } });
    // 🔴 HQA 보류 고침 · PM 결재 덧(⑪) — 위 「진행 그림(기록)」 을 판정 키로 올림(같은 창 · 같은 표본 · terms-slow-doc 판정 손 0)
    const passProg = JUDGE.termsSlowProg(o);
    console.log(`K 닫힌문 장면 terms-slow-prog 판정=${passProg} · 걸기 ${progArm} · 늦춘 문서 ${o.slowHits} · ${progSumShow(prog)}`);
    rec(st, 'termsSlowProg', { pass: passProg, got: { slowHits: o.slowHits, progArm, at5: prog.at5, n: prog.n, bar: prog.bar, idGone: prog.idGone, errs: prog.errs } });
  } catch (e) {
    dev(st, 'termsSlow', step2, e);
    dev(st, 'termsSlowProg', step2, e); // 🔴 HQA 보류 고침 — 같은 창이라 같이 장치 거짓
  } finally {
    if (kb2) await closeKb(st, kb2);
  }
}

// ── 🔴 HQA 보류 고침 · 물러남 장면 둘(⑪ · 고침2 설계 §7-2 · 작업지시서 §8-13 · PM 결재 E4) ──
//   꼴 = 측정 가지 meas/20261129-1032-정적칸남음 의 m1032-measure.js(holdDocs :286 · sceneStop :315 · sceneBack :402 · closeKb 저장소 출입증 :196).
// 새 기기 안내 창이 떠 있으면 사람처럼 [확인](붙잡음 전 · 착지 전이라 문서 이동 대기 없음)
async function closeDeviceNotice(page) {
  try {
    const dlg = page.locator('.mud-dialog', { hasText: DEVICE_NOTICE_TITLE });
    if ((await dlg.count()) === 0) return false;
    await dlg.first().getByRole('button', { name: '확인', exact: true }).click({ timeout: 5000 });
    return true;
  } catch (e) {
    console.log(`K 닫힌문 기기 등록 안내 닫기 실패 ${L.errorName(e)}`);
    return false;
  }
}
// 표본 줄 모으기(그 창 콘솔 · 꼴 맞는 줄만 · 값 · 토큰 0)
function stallWatch(kb) {
  const lines = [];
  kb.page.on('console', (msg) => {
    const t = String(msg.text());
    if (t.startsWith(STALL_TAG) && lines.length < 400) lines.push({ at: Date.now(), t: t.slice(0, 200) });
  });
  return lines;
}
const stallSamples = (lines, tag, fromAt) => lines.filter((x) => x.at >= fromAt).map((x) => ({ at: x.at, ...(stallParse(x.t) || {}) }))
  .filter((x) => x.tag === tag);
// 주 프레임 문서 요청 붙잡음(시험 몫 · 앱 손 0) — mode 가 'free' 가 되면 새 요청은 바로 넘긴다
async function holdDocs(kb) {
  const page = kb.page;
  const h = { mode: 'hold', held: [], hits: 0, handler: null, releaseAll: null };
  let open = null;
  const gate = new Promise((ok) => { open = ok; });
  h.releaseAll = () => open();
  h.handler = async (route) => {
    let main = false;
    try {
      main = route.request().isNavigationRequest() && route.request().frame() === page.mainFrame();
    } catch (e) {
      main = false; // 서비스 워커 요청 등 frame() 이 던지는 꼴 — 문서 아님
    }
    if (main && h.mode === 'hold') {
      h.hits += 1;
      const x = { path: pathOnly(route.request().url()), req: route.request(), pass: '붙잡힘' };
      h.held.push(x);
      await gate;
      await route.fallback().then(() => { x.pass = '넘김 성공'; }, (e) => { x.pass = `넘김 실패 ${L.errorName(e)}`; });
      return;
    }
    await route.fallback().catch((e) => console.log(`K 닫힌문 붙잡음 밖 넘김 실패 ${L.errorName(e)}`));
  };
  await page.route('**/*', h.handler);
  return h;
}
// 쓸 수 있나 — 아이디 · 비밀번호 칸에 넣고(Tab) 「로그인」 단추가 켜지는지만 본다. **누르지 않는다**(두 번째 로그인 0).
//   칸이 없으면(진행 그림에 머묾) 거짓 · 까닭 한 줄.
async function formUsable(page, loginId, password) {
  try {
    await page.getByLabel('아이디', { exact: true }).fill(loginId, { timeout: 5000 });
    await page.getByLabel('비밀번호', { exact: true }).fill(password, { timeout: 5000 });
    await page.getByLabel('비밀번호', { exact: true }).press('Tab', { timeout: 5000 });
    const btn = page.getByRole('button', { name: '로그인', exact: true }).first();
    const end = Date.now() + 5000;
    while (Date.now() < end) {
      if (await btn.isEnabled({ timeout: 1000 }).catch(() => false)) return { on: true, why: '켜짐' };
      await K.sleep(250);
    }
    return { on: false, why: '5초 안에 안 켜짐' };
  } catch (e) {
    return { on: false, why: `칸 넣기 실패 ${e instanceof K.KitStepError ? L.sanitize(e.message, 60) : L.errorName(e)}` };
  }
}
// 로그인 응답 본문을 못 읽은 창 — 화면 저장소의 출입증으로 로그아웃(값은 가림 등록 · 출력 0 · 측정 m1032 2회째 고침 꼴)
async function tokFromStore(kb) {
  if (kb.closed || kb.tok.access) return;
  const t = await K.withTimeout(kb.page.evaluate((k) => localStorage.getItem(k), TOKEN_KEY), 5000, '저장소 출입증')
    .then((v) => v, (e) => {
      console.log(`K 닫힌문 ${kb.who} 저장소 출입증 읽기 실패 ${e instanceof K.KitStepError ? L.sanitize(e.message, 60) : L.errorName(e)}`);
      return null;
    });
  if (t) {
    L.maskAndRegister(`TOKEN_ACCESS_K_STORE_${kb.who}`, t);
    kb.tok.access = t;
    console.log(`K 닫힌문 ${kb.who} 출입증 = 화면 저장소에서(응답 본문 못 읽음)`);
  }
}
// 불성립 = DEVICE 거짓(통과로 안 셈 · 설계 §7-2 「불성립 = ⚫ 장치」)
function recDevice(st, name, got) {
  K.recK(st.r, { key: KEY[name], what: WHAT[name], got: `장치 불성립 — ${typeof got === 'string' ? got : JSON.stringify(got)}`, pass: false, kind: 'DEVICE' });
}

// 길 A2 — 약관 미동의 직원 · 첫 문서(/terms) 붙잡음 → CDP Page.stopLoading(중지 단추 · Esc 와 같은 길) → 45초 관찰
async function sceneStopAfterLogin(st) {
  const step = { now: '약관 미동의 직원 확인' };
  let kb = null;
  let hold = null;
  try {
    if (!st.termsOk) throw new K.KitStepError(`준비 실패 — ${st.termsWhy}`);
    step.now = '브라우저 열기 · 로그인 칸 채우기';
    kb = await openCtx(st, 'S3STOP');
    await kb.context.addInitScript(STALL_INIT);
    const lines = stallWatch(kb);
    const page = kb.page;
    const cdp = await kb.context.newCDPSession(page);
    await page.goto(`${K.BASE}${LOGIN_PATH}`, { waitUntil: 'domcontentloaded', timeout: 60000 });
    await page.getByLabel('아이디', { exact: true }).fill(TERMS_ID, { timeout: 60000 });
    await page.getByLabel('비밀번호', { exact: true }).fill(st.termsPw);
    await page.getByLabel('비밀번호', { exact: true }).press('Tab');
    kb.navs.length = 0;
    kb.dx.docs.length = 0; kb.dx.fails.length = 0; kb.dx.failCount = 0; kb.dx.unhandled.length = 0;
    gtReset(kb);
    step.now = '문서 붙잡음 · 표본기 걸기 · 로그인 누름';
    hold = await holdDocs(kb);
    const arm = await K.withTimeout(page.evaluate(`window.__hpStallStart ? window.__hpStallStart('A2', ${STALL_TICKS}) : 'no-init'`), K.EVAL_MS, '표본기 걸기')
      .then((v) => String(v), (e) => (e instanceof K.KitStepError ? L.sanitize(e.message, 80) : L.errorName(e)));
    const clickAt = Date.now();
    await page.getByRole('button', { name: '로그인', exact: true }).click();
    let notices = 0;
    const end = Date.now() + 45000;
    while (hold.hits === 0 && Date.now() < end) {
      if (await closeDeviceNotice(page)) notices += 1;
      await K.sleep(300);
    }
    if (hold.hits === 0) throw new K.KitStepError(`붙잡음 적중 0(45초) · 로그인 응답 ${kb.login.seen}회 status=${kb.login.status} · 안내 닫음 ${notices}`);
    const firstHitMs = Date.now() - clickAt;
    const endL = Date.now() + 10000;
    while (kb.login.seen === 0 && Date.now() < endL) await K.sleep(250);
    if (kb.login.seen === 0 || kb.login.terms !== true) {
      throw new K.KitStepError(`약관 필요 상태 아님(로그인 응답 ${kb.login.seen}회 · 약관 필요=${kb.login.terms} · status=${kb.login.status})`);
    }
    await K.sleep(STOP_SETTLE_MS);
    const pendAtStop = hold.held.filter((x) => {
      const d = kb.dx.docs.find((z) => z.req === x.req);
      return d && d.end === '대기';
    }).map((x) => x.req);
    const docsAtStop = kb.dx.docs.length;
    let dclAfter = 0;
    const onDcl = () => { dclAfter += 1; };
    page.on('domcontentloaded', onDcl);
    hold.mode = 'free'; // 중지 뒤 새 문서 요청은 붙잡지 않는다(제품이 `/` 를 내면 그대로 보이게)
    step.now = '중지(CDP Page.stopLoading) · 45초 관찰';
    const stopAt = Date.now();
    const stopRes = await K.withTimeout(cdp.send('Page.stopLoading'), 5000, 'CDP Page.stopLoading')
      .then(() => '보냄', (e) => `실패 ${e instanceof K.KitStepError ? L.sanitize(e.message, 60) : L.errorName(e)}`);
    await K.sleep(STOP_RELEASE_MS);
    hold.releaseAll();
    await K.sleep(Math.max(0, stopAt + STALL_OBS_MS - Date.now()));
    page.off('domcontentloaded', onDcl);
    const docs = await docsNow(kb, 3000);
    const pend = pendAtStop.map((r) => kb.dx.docs.find((z) => z.req === r)).filter(Boolean);
    const cancelled = pend.length >= 1 && pend.every((d) => /^실패/.test(d.end));
    const samples = stallSamples(lines, 'A2', stopAt);
    const last = samples[samples.length - 1] || null;
    const fb = samples.find(formBack) || null;
    step.now = '아이디 · 비밀번호 넣기(누르지 않음)';
    const usable = await formUsable(page, TERMS_ID, st.termsPw);
    const o = {
      valid: stopRes === '보냄' && cancelled, firstHeld: hold.held[0] ? hold.held[0].path : null, samples: samples.length, last,
      btnOn: usable.on, docsAfter: docs.length - docsAtStop, dclAfter,
      // 기록(판정 아님) — 물러남 시각(누름부터) · 중지 뒤 20초 안 진행 그림 표본 수(물러남이 일찍 오지 않음 · 설계 §7-2)
      fallBackMs: fb ? fb.at - clickAt : null, early: samples.filter((x) => x.at - stopAt < 20000 && stallShape(x)).length,
    };
    console.log(`K 닫힌문 장면 stop-after-login 표본기 ${arm} · 첫 붙잡음 +${firstHitMs}ms ${o.firstHeld} · 안내 닫음 ${notices} · 중지 ${L.sanitize(stopRes, 80)}`
      + ` · 붙잡은 요청 ${L.sanitize(hold.held.map((x) => `${x.path} ${x.pass}`).join(' | ') || '없음', 200)} · 취소됨 ${cancelled} · 문서 ${L.sanitize(docs.join(' > ') || '없음', 200)}`);
    console.log(`K 닫힌문 장면 stop-after-login 표본 ${o.samples} · 중지 뒤 20초 안 진행 그림 ${o.early} · 물러남 누름 +${o.fallBackMs}ms · 끝 ${stallShow(last)} · 단추 ${usable.why}`
      + ` · 새 문서 요청 ${o.docsAfter} · 새 문서 적재 ${o.dclAfter}`);
    const got = { firstHeld: o.firstHeld, samples: o.samples, last: stallShow(last), btnOn: o.btnOn, docsAfter: o.docsAfter, dclAfter, fallBackMs: o.fallBackMs, early: o.early };
    if (!o.valid) {
      recDevice(st, 'stopAfterLogin', { stopRes, pend: pend.length, cancelled });
    } else {
      const pass = JUDGE.stopAfterLogin(o);
      console.log(`K 닫힌문 장면 stop-after-login 판정=${pass}`);
      rec(st, 'stopAfterLogin', { pass, got: pass ? got : { ...got, dx: diag(kb) } });
    }
  } catch (e) {
    dev(st, 'stopAfterLogin', step, e);
  } finally {
    if (kb && hold) await kb.page.unroute('**/*', hold.handler).catch((e) => console.log(`K 닫힌문 붙잡음 풀기 실패 ${L.errorName(e)}`));
    if (kb) {
      await tokFromStore(kb);
      await closeKb(st, kb);
    }
  }
}

// 길 B — 복원 캐시(bfcache) 켠 브라우저 · 기존 직원 로그인 → / 착지 → 뒤로 가기 → 45초 관찰
//   경로 가로채기(Fetch)를 켜지 않는다 — 가로채기가 복원을 막을 수 있다(측정 m1032 :146). 외부는 기동 인자 이름 풀이 규칙으로 끊는다(127.0.0.1 만 남김).
//   K 초기 스크립트(막기 · 녹음기)도 걸지 않는다(측정과 같은 꼴 · 표본기 하나만).
async function openBfCtx(st, who) {
  if (!st.brB) {
    const pw = require('playwright');
    const optB = { ignoreDefaultArgs: ['--disable-back-forward-cache'], args: ['--host-resolver-rules=MAP * ~NOTFOUND , EXCLUDE 127.0.0.1'] };
    try {
      st.brB = await pw.chromium.launch({ channel: 'chromium', ...optB });
      st.brBNote = 'channel=chromium';
    } catch (e) {
      console.log(`K 닫힌문 복원 캐시 브라우저 channel=chromium 기동 실패 ${L.errorName(e)} — 기본 실행 파일로`);
      st.brB = await pw.chromium.launch(optB);
      st.brBNote = '기본 실행 파일';
    }
  }
  const context = await st.brB.newContext({ baseURL: K.BASE, viewport: { width: 1366, height: 768 }, locale: 'ko-KR', timezoneId: 'Asia/Seoul' });
  const tok = { access: null, deviceId: null, masked: 0 };
  K.watchTokens(context, tok);
  await context.addInitScript(STALL_INIT);
  const kb = {
    browser: BROWSER, who, context, page: null, tok, closed: false, consoleBuf: [],
    login: { seen: 0, status: null }, docs: [],
  };
  st.kbs.push(kb);
  kb.page = await context.newPage();
  kb.page.on('response', (res) => {
    if (/\/api\/auth\/login(\?|$)/i.test(res.url()) && res.request().method() === 'POST') {
      kb.login.status = res.status();
      kb.login.seen += 1;
    }
  });
  kb.page.on('request', (req) => {
    let main = false;
    try {
      main = req.isNavigationRequest() && req.frame() === kb.page.mainFrame();
    } catch (e) {
      main = false;
    }
    if (main && kb.docs.length < 20) kb.docs.push({ req, path: pathOnly(req.url()), end: '대기' });
  });
  kb.page.on('requestfinished', (req) => {
    const x = kb.docs.find((d) => d.req === req);
    if (x) req.response().then((r) => { x.end = r ? String(r.status()) : '응답 없음'; }, (e) => { x.end = `응답 읽기 실패 ${L.errorName(e)}`; });
  });
  kb.page.on('requestfailed', (req) => {
    const x = kb.docs.find((d) => d.req === req);
    if (x) x.end = `실패 ${L.sanitize((req.failure() || {}).errorText || '-', 40)}`;
  });
  return kb;
}
async function sceneBackRestore(st) {
  const step = { now: '준비 확인' };
  let kb = null;
  try {
    if (!st.prepOk) throw new K.KitStepError(`준비 실패 — ${st.prepWhy}`);
    step.now = '복원 캐시 브라우저 열기 · 로그인 칸 채우기';
    kb = await openBfCtx(st, 'S3BACK');
    const lines = stallWatch(kb);
    const page = kb.page;
    await page.goto(`${K.BASE}${LOGIN_PATH}`, { waitUntil: 'domcontentloaded', timeout: 60000 });
    await page.getByLabel('아이디', { exact: true }).fill(ID.fresh, { timeout: 60000 });
    await page.getByLabel('비밀번호', { exact: true }).fill(st.freshPw);
    await page.getByLabel('비밀번호', { exact: true }).press('Tab');
    step.now = '로그인 누름 · 착지';
    const clickAt = Date.now();
    await page.getByRole('button', { name: '로그인', exact: true }).click();
    let notices = 0;
    let landed = null;
    const end = Date.now() + 60000;
    while (Date.now() < end) {
      if (await closeDeviceNotice(page)) notices += 1;
      const p = pathOnly(page.url());
      if (p !== LOGIN_PATH && kb.docs.some((d) => d.path === p && d.end === '200')) { landed = p; break; }
      await K.sleep(500);
    }
    if (!landed) throw new K.KitStepError(`착지 못 함(60초) · 지금 ${pathOnly(page.url())} · 로그인 응답 ${kb.login.seen}회 status=${kb.login.status}`);
    const landMs = Date.now() - clickAt;
    await K.sleep(BACK_LAND_MS);
    const docsBefore = kb.docs.length;
    step.now = '표지 두기 · 뒤로 가기 · 45초 관찰';
    const flag = await K.withTimeout(page.evaluate((k) => { sessionStorage.setItem(k, 'B'); return 'ok'; }, STALL_FLAG), 5000, '표지 두기')
      .then((v) => v, (e) => (e instanceof K.KitStepError ? L.sanitize(e.message, 60) : L.errorName(e)));
    const backAt = Date.now();
    const back = await page.goBack({ waitUntil: 'commit', timeout: 20000 })
      .then((r) => (r ? `응답 ${r.status()}` : '응답 null(복원 또는 같은 문서)'), (e) => `실패 ${L.errorName(e)}`);
    await K.sleep(Math.max(0, backAt + STALL_OBS_MS - Date.now()));
    const newDocs = kb.docs.slice(docsBefore);
    // 복원 때 앞 문서의 첫 pageshow(persisted=거짓) 줄이 뒤늦게 같이 들어올 수 있어 마지막 pageshow 를 본다(측정 m1032 1회째 고침 꼴)
    const showAfter = lines.filter((x) => x.at >= backAt - 50 && x.t.startsWith(`${STALL_TAG} pageshow`)).pop();
    const persisted = showAfter ? /persisted=true/.test(showAfter.t) : null;
    const loginDocAfter = newDocs.some((d) => d.path === LOGIN_PATH);
    const samples = stallSamples(lines, 'B', backAt);
    const last = samples[samples.length - 1] || null;
    const fb = samples.find(formBack) || null;
    step.now = '아이디 · 비밀번호 넣기(누르지 않음)';
    const usable = persisted === true ? await formUsable(page, ID.fresh, st.freshPw) : { on: false, why: '복원 안 됨 — 안 넣음' };
    const o = {
      persisted, loginDocAfter, samples: samples.length, last, btnOn: usable.on, docsAfter: newDocs.length,
      fallBackMs: fb ? fb.at - backAt : null, // 기록 — 뒤로 가기부터 물러남까지
    };
    console.log(`K 닫힌문 장면 back-restore 브라우저 ${st.brBNote} · 착지 ${landed} +${landMs}ms · 안내 닫음 ${notices} · 표지 ${flag} · goBack ${L.sanitize(back, 60)}`
      + ` · 뒤 문서 ${L.sanitize(newDocs.map((d) => `${d.path}=${d.end}`).join(' > ') || '없음', 200)}`
      + ` · 마지막 pageshow ${showAfter ? L.sanitize(showAfter.t.replace(STALL_TAG, '').trim(), 80) : '못 봄'}`);
    console.log(`K 닫힌문 장면 back-restore 표본 ${o.samples} · 물러남 뒤로 가기 +${o.fallBackMs}ms · 끝 ${stallShow(last)} · 단추 ${usable.why} · 복원 뒤 문서 요청 ${o.docsAfter}`);
    const got = { persisted, loginDocAfter, samples: o.samples, last: stallShow(last), btnOn: o.btnOn, docsAfter: o.docsAfter, fallBackMs: o.fallBackMs };
    if (persisted !== true || loginDocAfter) {
      recDevice(st, 'backRestore', { persisted, loginDocAfter, browser: st.brBNote });
    } else {
      const pass = JUDGE.backRestore(o);
      console.log(`K 닫힌문 장면 back-restore 판정=${pass}`);
      rec(st, 'backRestore', { pass, got });
    }
  } catch (e) {
    dev(st, 'backRestore', step, e);
  } finally {
    if (kb) {
      await tokFromStore(kb);
      await closeKb(st, kb);
    }
  }
}

// ── 🔴 갈래 T 장면 — 기존 직원 재로그인 G4 · 로그인 상태로 /login 직접 G5 ──
async function sceneRelogin(st) {
  const step = { now: '준비 확인' };
  let kb = null;
  try {
    if (!st.prepOk) throw new K.KitStepError(`준비 실패 — ${st.prepWhy}`);
    step.now = '브라우저 열기 · 화면 로그인';
    kb = await openCtx(st, 'S3RELOG');
    const s = await loginScreen(kb, ID.fresh, st.freshPw);
    if (kb.login.seen === 0 || kb.login.terms !== false) {
      throw new K.KitStepError(`재로그인 상태 아님(로그인 응답 ${kb.login.seen}회 · 약관 필요=${kb.login.terms} · status=${kb.login.status})`);
    }
    const docs = await docsNow(kb);
    // ⬛ [낡은 줄 · B2 고침 1 이전] const o = { where: s.where, path: s.path, docs, loginRedirectToWelcome: kb.login.redirect, deviceNew: kb.login.deviceNew };
    // 🔴 B2 고침 1 ④(N4) — 착지 +3초 기다린 뒤 세 겹을 잰다(사이드바 부름보다 먼저 재면 공짜 초록)
    await K.sleep(Math.max(0, s.landAt + 3000 - Date.now()));
    const sn = snackTally(await seenSnacks(kb.page));
    const o = { where: s.where, path: s.path, docs, loginRedirectToWelcome: kb.login.redirect, deviceNew: kb.login.deviceNew,
      f403n: kb.gt.f403n, h403: kb.gt.h403, f403list: [...kb.gt.f403], mcReq: kb.gt.mcReq, ...sn };
    const pass = JUDGE.relogin(o);
    // ⬛ [낡은 줄 · B2 고침 1 이전] console.log(`K 닫힌문 장면 relogin-home 판정=${pass} · 착지 ${o.where}@${L.sanitize(String(o.path), 80)} · 문서 ${L.sanitize(docs.join(' > ') || '없음', 300)} · 첫 로그인=${o.loginRedirectToWelcome} · 새 기기=${o.deviceNew}`);
    console.log(`K 닫힌문 장면 relogin-home 판정=${pass} · 착지 ${o.where}@${L.sanitize(String(o.path), 80)} · 문서 ${L.sanitize(docs.join(' > ') || '없음', 300)} · 첫 로그인=${o.loginRedirectToWelcome} · 새 기기=${o.deviceNew}`
      + ` · 403 수 ${o.f403n} · 처리기 경고 ${o.h403} · 처리기 알림 ${o.forbidSeen} · 뜬 알림 보기 실패 ${o.seenErr} · 메인PC 표 요청 ${o.mcReq} · 403 ${L.sanitize(o.f403list.join(', ') || '없음', 200)}`);
    // ⬛ [낡은 줄 · B2 고침 1 이전] rec(st, 'relogin', { pass, got: pass ? o : { ...o, dx: diag(kb, { noticeClosed: s.noticeClosed }) } });
    //   🔴 B2 고침 1 ④ — got 은 판정 칸 먼저 · 알림 글은 소독 글자로(gotOf)
    const got = gotOf(o, ['where', 'path', 'docs', 'f403n', 'h403', 'forbidSeen', 'seenErr', 'mcReq', 'loginRedirectToWelcome', 'deviceNew', 'seenSnacks', 'f403list']);
    rec(st, 'relogin', { pass, got: pass ? got : { ...got, dx: diag(kb, { noticeClosed: s.noticeClosed }) } });
  } catch (e) {
    dev(st, 'relogin', step, e);
  }
  const step2 = { now: '직접 /login' };
  try {
    if (!kb || !kb.tok.access) throw new K.KitStepError(`로그인 세션 없음 — 재로그인 장면이 앞에서 섰다(${step.now})`);
    await DC.closeBriefing(kb.page, kb.br, 3000);
    kb.navs.length = 0;
    kb.dx.docs.length = 0;
    gtReset(kb);
    await kb.page.goto(`${K.BASE}${LOGIN_PATH}`, { waitUntil: 'domcontentloaded', timeout: 60000 });
    const s = await DC.settleScreen(kb.page, 60000);
    // 🔴 B2 고침 1 ④(N4 「loginDirect 검토」) — 착지 +3초 뒤 세 겹
    const landAt = Date.now();
    const docs = await docsNow(kb);
    // ⬛ [낡은 줄 · B2 고침 1 이전] const o = { where: s.where, path: s.path, docs };
    await K.sleep(Math.max(0, landAt + 3000 - Date.now()));
    const sn = snackTally(await seenSnacks(kb.page));
    const o = { where: s.where, path: s.path, docs, f403n: kb.gt.f403n, h403: kb.gt.h403, f403list: [...kb.gt.f403], ...sn };
    const pass = JUDGE.loginDirect(o);
    // ⬛ [낡은 줄 · B2 고침 1 이전] console.log(`K 닫힌문 장면 login-direct 판정=${pass} · 착지 ${o.where}@${L.sanitize(String(o.path), 80)} · 문서 ${L.sanitize(docs.join(' > ') || '없음', 300)}`);
    console.log(`K 닫힌문 장면 login-direct 판정=${pass} · 착지 ${o.where}@${L.sanitize(String(o.path), 80)} · 문서 ${L.sanitize(docs.join(' > ') || '없음', 300)}`
      + ` · 403 수 ${o.f403n} · 처리기 경고 ${o.h403} · 처리기 알림 ${o.forbidSeen} · 뜬 알림 보기 실패 ${o.seenErr}`);
    // ⬛ [낡은 줄 · B2 고침 1 이전] rec(st, 'loginDirect', { pass, got: pass ? o : { ...o, dx: diag(kb) } });
    const got = gotOf(o, ['where', 'path', 'docs', 'f403n', 'h403', 'forbidSeen', 'seenErr', 'seenSnacks', 'f403list']);
    rec(st, 'loginDirect', { pass, got: pass ? got : { ...got, dx: diag(kb) } });
  } catch (e) {
    dev(st, 'loginDirect', step2, e);
  } finally {
    if (kb) await closeKb(st, kb);
  }
}

// ── 🔴 갈래 T 장면 — 새 기기 알림 20초 열어 둠(장면 G 줄 · §8-2 A-①) ──
//   [최신버젼업데이트] 로 갈 곳을 /data/update 로 둔다 — 안전망이 15초에 / 로 보내면 갈 곳이 바뀌어 빨강(종전 꼴 = 알림 사라짐 · /).
//   장치 자리 잡기(settleScreen)는 알림을 바로 닫으므로 20초 동안은 쓰지 않고 직접 본다.
async function sceneDialog(st) {
  const step = { now: '준비 확인' };
  let kb = null;
  try {
    if (!st.prepOk) throw new K.KitStepError(`준비 실패 — ${st.prepWhy}`);
    step.now = '새 기기 꼴 창 열기 · [최신버젼업데이트]';
    kb = await openCtx(st, 'S3DLG', D1_CTX);
    const page = kb.page;
    await page.goto(`${K.BASE}${LOGIN_PATH}`, { waitUntil: 'domcontentloaded', timeout: 60000 });
    await page.getByLabel('아이디', { exact: true }).fill(ID.fresh, { timeout: 60000 });
    await page.getByLabel('비밀번호', { exact: true }).fill(st.freshPw);
    await page.getByLabel('비밀번호', { exact: true }).press('Tab');
    await page.getByRole('button', { name: UPDATE_BTN }).first().click({ timeout: 60000 });
    if (!(await seen(page, UPDATE_NOTICE, 10000))) throw new K.KitStepError('[최신버젼업데이트] 누름 안내가 안 뜸(단추가 안 눌림)');
    kb.navs.length = 0;
    kb.dx.docs.length = 0; kb.dx.fails.length = 0; kb.dx.failCount = 0; kb.dx.unhandled.length = 0;
    gtReset(kb);
    step.now = '로그인 누름 · 새 기기 알림 기다림';
    await page.getByRole('button', { name: '로그인', exact: true }).click();
    const dlg = page.locator('.mud-dialog', { hasText: DEVICE_NOTICE_TITLE }).first();
    const shown = await dlg.waitFor({ state: 'visible', timeout: TEXT_MS }).then(() => true, () => false);
    const lend = Date.now() + 10000;
    while (kb.login.seen === 0 && Date.now() < lend) await K.sleep(250);
    await K.waitMasked(kb.tok);
    if (!shown && kb.login.deviceNew !== true) {
      throw new K.KitStepError(`못 만듦 — 새 기기 꼴 아님(로그인 응답 ${kb.login.seen}회 · 새 기기=${kb.login.deviceNew} · status=${kb.login.status})`);
    }
    step.now = '알림 20초 열어 둠';
    const t0 = Date.now();
    let lostAtMs = shown ? null : 0;
    let docsDuringHold = 0;
    while (shown && Date.now() - t0 < HOLD_MS) {
      await K.sleep(500);
      const vis = await dlg.isVisible().catch(() => false);
      if (!vis && lostAtMs === null) lostAtMs = Date.now() - t0;
      docsDuringHold = Math.max(docsDuringHold, kb.dx.docs.length);
    }
    const heldMs = shown ? Date.now() - t0 : 0;
    step.now = '알림 [확인]';
    let clicked = false;
    if (shown && lostAtMs === null) {
      await dlg.getByRole('button', { name: '확인', exact: true }).click({ timeout: 10000 });
      clicked = true;
    }
    const s = await DC.settleScreen(page, 90000);
    const lk = await landLook(kb, Date.now());
    const docs = await docsNow(kb);
    // ⬛ [낡은 줄 · 갈래 B2 이전] const o = gather(kb, s, lk, docs, { dialogShown: shown, lostAtMs, heldMs, docsDuringHold, clicked, deviceNew: kb.login.deviceNew });
    const sn = snackTally(await seenSnacks(kb.page)); // 🔴 갈래 B2(D17 ⓒ)
    const o = gather(kb, s, lk, docs, { dialogShown: shown, lostAtMs, heldMs, docsDuringHold, clicked, deviceNew: kb.login.deviceNew, ...sn });
    const pass = JUDGE.dialog(o);
    sceneLog(kb, 'device-dialog-20s', { ...o, pass });
    console.log(`K 닫힌문 장면 device-dialog-20s 알림 보임=${shown} · 사라짐 ${lostAtMs === null ? '없음' : `+${lostAtMs}ms`} · 열어 둔 ${heldMs}ms · 그동안 문서 ${docsDuringHold} · [확인] ${clicked}`);
    // ⬛ [낡은 줄 · 갈래 B2 이전] const got = gotOf(o, ['dialogShown', 'lostAtMs', 'heldMs', 'docsDuringHold', 'clicked', 'where', 'path', 'docs', 'band3', 'band10', 'appErr']);
    // ⬛ [낡은 줄 · B2 고침 1 이전] const got = gotOf(o, ['dialogShown', 'lostAtMs', 'heldMs', 'docsDuringHold', 'clicked', 'where', 'path', 'docs', 'band3', 'band10', 'appErr', 'f403n', 'h403', 'forbidSeen', 'f403list']);
    const got = gotOf(o, ['dialogShown', 'lostAtMs', 'heldMs', 'docsDuringHold', 'clicked', 'where', 'path', 'docs', 'band3', 'band10', 'appErr', 'f403n', 'h403', 'forbidSeen', 'seenErr', 'mcReq', 'f403list']);
    rec(st, 'dialog', { pass, got: pass ? got : { ...got, dx: diag(kb) } });
  } catch (e) {
    dev(st, 'dialog', step, e);
  } finally {
    if (kb) await closeKb(st, kb);
  }
}

// 대시보드 조회 주소만(가로챔 걸기 · 풀기에 같은 판별 함수 — unroute 는 같은 것이어야 풂)
const IS_DASH = (u) => u.pathname === SALES_FIN_PATHS[0];
// 🔴 갈래 B2 — 결재 대기 주소만(양성 대조 가로챔 · 걸기 · 풀기에 같은 함수)
const IS_APPROVAL_PENDING = (u) => u.pathname === APPROVAL_PENDING_PATH;
// ── 🔴 갈래 T 장면 — 대표 첫 화면 G8 · G8+ · 처리한 예외 G11(대시보드 조회 503 가로챔) ──
async function sceneOwner(st) {
  const step = { now: '대표 비번 확인' };
  let kb = null;
  try {
    if (!PW.parent) throw new K.KitStepError('못 만듦 — 대표 비번 환경 칸 비어 있음');
    step.now = '브라우저 열기 · 대표 화면 로그인';
    kb = await openCtx(st, 'S3O1');
    // ⬛ [낡은 줄 · B2 고침 1 이전] const s = await loginScreen(kb, ID.parent, PW.parent);
    // 🔴 B2 고침 1 ⑤(N5) — 큰 카드 기다림을 누름 전부터 걸어 둠(locator 기다림은 문서 이동을 건너 이어짐) · 보인 시각만 받음
    let heroP = null;
    const s = await loginScreen(kb, ID.parent, PW.parent, {
      beforeClick: async (k) => {
        heroP = k.page.locator('.hp-kpi-hero').first().waitFor({ state: 'visible', timeout: 60000 })
          .then(() => Date.now(), (e) => { console.log(`K 닫힌문 큰 카드 안 보임(누름 + 60초) ${L.errorName(e)}`); return null; });
      },
    });
    if (kb.login.seen === 0 || kb.login.terms !== false) {
      throw new K.KitStepError(`대표 로그인 상태 이상(로그인 응답 ${kb.login.seen}회 · 약관 필요=${kb.login.terms} · status=${kb.login.status})`);
    }
    // 🔴 갈래 B2(D18 R-④ B-① 빈 시간 · 기록만 · 판정 아님) — 착지 → 큰 카드 보임 ms. 흰 띠 보기(+3 · +10초) 시각을 밀지 않게 같이 돌린다.
    // ⬛ [낡은 줄 · B2 고침 1 이전] const heroP = kb.page.locator('.hp-kpi-hero').first().waitFor({ state: 'visible', timeout: 15000 })
    // ⬛ [낡은 줄 · B2 고침 1 이전]   .then(() => Date.now() - s.landAt, (e) => { console.log(`K 닫힌문 큰 카드 안 보임(15초) ${L.errorName(e)}`); return null; });
    //   🔴 B2 고침 1 ⑤ — 0점 = 착지(자리 잡기 끝) → 로그인 누름(위 beforeClick). CI 3ms = 잴 것이 없던 0점이었다.
    await DC.closeBriefing(kb.page, kb.br, 3000);
    const lk = await landLook(kb, s.landAt);
    const cd = await cards(kb.page);
    const docs = await docsNow(kb);
    // ⬛ [낡은 줄 · B2 고침 1 이전] const heroMs = await heroP;
    const heroAt = heroP ? await heroP : null;
    const heroMs = heroAt === null ? null : heroAt - s.clickAt; // 누름 → 큰 카드 보임(ms · 기록만)
    console.log(`K 닫힌문 장면 owner-home 빈 시간(기록) 누름 → 큰 카드 ${heroMs === null ? '못 봄' : `${heroMs}ms`} · 누름 → 착지 ${s.landAt - s.clickAt}ms`);
    // ⬛ [낡은 줄 · 갈래 B2 이전] const o = gather(kb, s, lk, docs, { hero: cd.hero, labels: cd.labels, notice: cd.notice, permAlert: cd.permAlert });
    const o = gather(kb, s, lk, docs, { hero: cd.hero, labels: cd.labels, notice: cd.notice, permAlert: cd.permAlert, heroMs, ...snackTally(await seenSnacks(kb.page)) });
    const pass = JUDGE.owner(o);
    sceneLog(kb, 'owner-home', { ...o, pass });
    // ⬛ [낡은 줄 · 갈래 B2 이전] const got = gotOf(o, ['where', 'path', 'hero', 'labels', 'notice', 'band3', 'band10', 'appErr', 'permAlert', 'docs']);
    // ⬛ [낡은 줄 · B2 고침 1 이전] const got = gotOf(o, ['where', 'path', 'hero', 'labels', 'notice', 'band3', 'band10', 'appErr', 'permAlert', 'heroMs', 'docs']);
    const got = gotOf(o, ['where', 'path', 'hero', 'labels', 'notice', 'band3', 'band10', 'appErr', 'mcReq', 'permAlert', 'heroMs', 'docs']);
    rec(st, 'owner', { pass, got: pass ? got : { ...got, dx: diag(kb) } });
  } catch (e) {
    dev(st, 'owner', step, e);
  }
  const step2 = { now: '대시보드 조회 503 가로챔 · 첫 화면 다시 열기' };
  let h503 = null;
  try {
    if (!kb || !kb.tok.access) throw new K.KitStepError(`로그인 세션 없음 — 대표 장면이 앞에서 섰다(${step.now})`);
    const hit = { n: 0 };
    h503 = (route) => {
      hit.n += 1;
      return route.fulfill({ status: 503, contentType: 'application/json', body: '{}' });
    };
    await kb.page.route(IS_DASH, h503);
    kb.navs.length = 0;
    kb.dx.docs.length = 0; kb.dx.fails.length = 0; kb.dx.failCount = 0; kb.dx.unhandled.length = 0;
    gtReset(kb);
    await kb.page.goto(`${K.BASE}${HOME_PATH}`, { waitUntil: 'domcontentloaded', timeout: 60000 });
    const s = await DC.settleScreen(kb.page, 60000);
    const landAt = Date.now();
    await DC.closeBriefing(kb.page, kb.br, 3000);
    const lk = await landLook(kb, landAt);
    const failSnack = lk.snacks.filter((t) => String(t).includes(DASH_FAIL_TEXT)).length;
    const o = gather(kb, s, lk, await docsNow(kb), { routeHits: hit.n, failSnack });
    const pass = JUDGE.owner503(o);
    sceneLog(kb, 'owner-503', { ...o, pass });
    const got = gotOf(o, ['routeHits', 'failSnack', 'band3', 'band10', 'warnDash', 'appErr', 'snacks', 'where', 'path']);
    rec(st, 'owner503', { pass, got: pass ? got : { ...got, dx: diag(kb) } });
  } catch (e) {
    dev(st, 'owner503', step2, e);
  } finally {
    if (kb && h503) await kb.page.unroute(IS_DASH, h503).catch((e) => console.log(`K 닫힌문 503 가로챔 풀기 실패 ${L.errorName(e)}`));
    // ⬛ [낡은 줄 · 갈래 B2 이전] if (kb) await closeKb(st, kb);  — 🔴 갈래 B2 세션 닫기는 셋째 장면(owner-json) 끝으로
  }
  // 🔴 갈래 B2(D18 R-③ · D17 양성 대조) — 같은 대표 세션 · 대시보드 조회만 200 + JSON 아닌 본문(일반 catch) · 결재 대기만 403
  const step3 = { now: '대시보드 JSON 아닌 본문 · 결재 대기 403 가로챔 · 첫 화면 다시 열기' };
  let hJson = null;
  let hCtl = null;
  try {
    if (!kb || !kb.tok.access) throw new K.KitStepError(`로그인 세션 없음 — 대표 장면이 앞에서 섰다(${step.now})`);
    const hit = { dash: 0, ctl: 0 };
    hJson = (route) => {
      hit.dash += 1;
      return route.fulfill({ status: 200, contentType: 'text/html; charset=utf-8', body: '<!doctype html><title>x</title>' });
    };
    hCtl = (route) => {
      hit.ctl += 1;
      return route.fulfill({ status: 403, contentType: 'application/json', body: '{}' });
    };
    await kb.page.route(IS_DASH, hJson);
    await kb.page.route(IS_APPROVAL_PENDING, hCtl);
    kb.navs.length = 0;
    kb.dx.docs.length = 0; kb.dx.fails.length = 0; kb.dx.failCount = 0; kb.dx.unhandled.length = 0;
    gtReset(kb);
    await kb.page.goto(`${K.BASE}${HOME_PATH}`, { waitUntil: 'domcontentloaded', timeout: 60000 });
    const s = await DC.settleScreen(kb.page, 60000);
    const landAt = Date.now();
    await DC.closeBriefing(kb.page, kb.br, 3000);
    const lk = await landLook(kb, landAt);
    const failSnack = lk.snacks.filter((t) => String(t).includes(DASH_FAIL_TEXT)).length;
    const sn = snackTally(await seenSnacks(kb.page));
    const o = gather(kb, s, lk, await docsNow(kb), { routeHits: hit.dash, ctlHits: hit.ctl, failSnack: Math.max(failSnack, sn.seenSnacks.filter((t) => t.includes(DASH_FAIL_TEXT)).length), ...sn });
    const pass = JUDGE.ownerJson(o);
    sceneLog(kb, 'owner-json', { ...o, pass });
    // ⬛ [낡은 줄 · B2 고침 1 이전] const got = gotOf(o, ['routeHits', 'ctlHits', 'failSnack', 'failSnackErr', 'warnJson', 'h403', 'forbidSeen', 'outsideAllow', 'band3', 'band10', 'warnDash', 'appErr', 'seenSnacks', 'where', 'path']);
    //   🔴 B2 고침 1 ③(N3) — 판정에 든 f403n 을 got 에도
    const got = gotOf(o, ['routeHits', 'ctlHits', 'failSnack', 'failSnackErr', 'warnJson', 'f403n', 'h403', 'forbidSeen', 'outsideAllow', 'band3', 'band10', 'warnDash', 'appErr', 'seenSnacks', 'where', 'path']);
    rec(st, 'ownerJson', { pass, got: pass ? got : { ...got, dx: diag(kb) } });
  } catch (e) {
    dev(st, 'ownerJson', step3, e);
  } finally {
    if (kb && hJson) await kb.page.unroute(IS_DASH, hJson).catch((e) => console.log(`K 닫힌문 JSON 아닌 본문 가로챔 풀기 실패 ${L.errorName(e)}`));
    if (kb && hCtl) await kb.page.unroute(IS_APPROVAL_PENDING, hCtl).catch((e) => console.log(`K 닫힌문 결재 대기 403 가로챔 풀기 실패 ${L.errorName(e)}`));
    if (kb) await closeKb(st, kb);
  }
}

// ── 🔴 갈래 T 장면 — 판매 역할 직원 첫 화면 G12(새 직원 장면이 다 끝난 뒤 · 사원 역할 칸만 바꿈) ──
async function sceneSales(st) {
  const step = { now: '준비 확인' };
  let kb = null;
  try {
    if (!st.prepOk) throw new K.KitStepError(`준비 실패 — ${st.prepWhy}`);
    step.now = '판매 역할로 바꿈(시험 DB 사원 역할 칸)';
    setSalesRole(st);
    step.now = '브라우저 열기 · 화면 로그인';
    kb = await openCtx(st, 'S3SALES');
    const s = await loginScreen(kb, ID.fresh, st.freshPw);
    const salesTok = jwtHasRole(kb.tok.access, SALES_ROLE);
    if (kb.login.seen === 0 || kb.login.terms !== false || !salesTok) {
      throw new K.KitStepError(`못 만듦 — 판매 역할 로그인 아님(로그인 응답 ${kb.login.seen}회 · 약관 필요=${kb.login.terms} · 출입증 역할 판매=${salesTok})`);
    }
    await DC.closeBriefing(kb.page, kb.br, 3000);
    const lk = await landLook(kb, s.landAt);
    const cd = await cards(kb.page);
    // ⬛ [낡은 줄 · 갈래 B2 이전] const o = gather(kb, s, lk, await docsNow(kb), { hero: cd.hero, labels: cd.labels, notice: cd.notice, role: SALES_ROLE });
    const docs = await docsNow(kb);
    const sn = snackTally(await seenSnacks(kb.page)); // 🔴 갈래 B2(D17 ⓒ)
    const o = gather(kb, s, lk, docs, { hero: cd.hero, labels: cd.labels, notice: cd.notice, role: SALES_ROLE, ...sn });
    const pass = JUDGE.sales(o);
    sceneLog(kb, 'sales-home', { ...o, pass });
    // ⬛ [낡은 줄 · 갈래 B2 이전] const got = gotOf(o, ['where', 'path', 'hero', 'labels', 'notice', 'sf403', 'sfReq', 'band3', 'band10', 'appErr']);
    // ⬛ [낡은 줄 · B2 고침 1 이전] const got = gotOf(o, ['where', 'path', 'hero', 'labels', 'notice', 'sf403', 'sfReq', 'band3', 'band10', 'appErr', 'f403n', 'h403', 'forbidSeen', 'f403list']);
    const got = gotOf(o, ['where', 'path', 'hero', 'labels', 'notice', 'sf403', 'sfReq', 'band3', 'band10', 'appErr', 'f403n', 'h403', 'forbidSeen', 'seenErr', 'mcReq', 'f403list']);
    rec(st, 'sales', { pass, got: pass ? got : { ...got, dx: diag(kb) } });
  } catch (e) {
    dev(st, 'sales', step, e);
  } finally {
    if (kb) await closeKb(st, kb);
  }
}

// ── 🔴 B2 고침 1 ⑦(N7 · 설계 7-2-8 가설 G13) — 「결재 보기」 받은 직원 첫 화면(기록만 · 키 아님 · 판정기 기대 키 틀 그대로) ──
//   시험 DB 에 권한 1행(user_permissions APPROVAL can_view=1 · 출하 DDL installer/hitpan_db_clean.sql :4302 칸 확인 #13)을 넣고
//   같은 새 직원으로 로그인 → 사이드바가 결재 대기를 부르는지(GET /api/approval/pending 1 이상) · 그 403 0 · 「403 알림 0」 세 겹 · 메인PC 표 요청 0.
//   끝에 그 행을 지운다(뒤 장면 영향 0 · 이 장면은 판매 역할 장면 뒤 맨 끝). 실패해도 다른 키 판정 손 0 — 기록 줄만 남김.
const G13_MENU = 'APPROVAL';
// G13 기대 꼴(순수 · 자체시험 K-C29j) — 기록 줄의 「기대대로」 칸
const g13Ok = (o) => o.pendReq >= 1 && o.pend403 === 0 && no403(o) && noMc(o);
async function sceneApprovalView(st) {
  let kb = null;
  let granted = false;
  let uid = null;
  let t = null;
  try {
    if (!st.prepOk) throw new K.KitStepError(`준비 실패 — ${st.prepWhy}`);
    uid = idOk(st.freshUid, '새 직원 번호');
    t = idOk(st.tid, '회사 번호');
    const before = dbCount(`SELECT COUNT(*) FROM user_permissions WHERE tenant_id = '${t}' AND user_id = '${uid}' AND menu_code = '${G13_MENU}';`);
    if (before !== 0) throw new K.KitStepError(`못 만듦 — 결재 권한 행이 이미 ${before}개(0 이어야 함)`);
    dbq(`INSERT INTO user_permissions (perm_id, tenant_id, user_id, menu_code, can_view) VALUES (UUID(), '${t}', '${uid}', '${G13_MENU}', 1);`);
    granted = true;
    kb = await openCtx(st, 'S3APPR');
    const s = await loginScreen(kb, ID.fresh, st.freshPw);
    if (kb.login.seen === 0 || kb.login.terms !== false) {
      throw new K.KitStepError(`로그인 상태 이상(로그인 응답 ${kb.login.seen}회 · 약관 필요=${kb.login.terms} · status=${kb.login.status})`);
    }
    await DC.closeBriefing(kb.page, kb.br, 3000);
    await K.sleep(Math.max(0, s.landAt + 10000 - Date.now())); // 사이드바 my-level → 결재 대기 부름 몫(+10초 · 첫 화면 장면과 같은 끝 시각)
    const sn = snackTally(await seenSnacks(kb.page));
    const o = { where: s.where, path: s.path, pendReq: kb.gt.pendReq, pend403: kb.gt.pend403, f403n: kb.gt.f403n, h403: kb.gt.h403, mcReq: kb.gt.mcReq, ...sn };
    console.log(`K 닫힌문 장면 approval-view-home(기록 · 키 아님) 기대대로=${g13Ok(o)} · 착지 ${o.where}@${L.sanitize(String(o.path), 80)}`
      + ` · 결재 대기 부름 ${o.pendReq} · 그중 403 ${o.pend403} · 403 수 ${o.f403n} · 처리기 경고 ${o.h403} · 처리기 알림 ${o.forbidSeen}`
      + ` · 뜬 알림 보기 실패 ${o.seenErr} · 메인PC 표 요청 ${o.mcReq} · 403 ${L.sanitize(kb.gt.f403.join(', ') || '없음', 200)}`);
  } catch (e) {
    console.log(`K 닫힌문 장면 approval-view-home(기록 · 키 아님) 못 봄 — ${e instanceof K.KitStepError ? L.sanitize(e.message, 160) : `예외 ${L.errorName(e)}`}`);
  } finally {
    if (kb) await closeKb(st, kb);
    if (granted) {
      try {
        const n = dbCount(`DELETE FROM user_permissions WHERE tenant_id = '${t}' AND user_id = '${uid}' AND menu_code = '${G13_MENU}'; SELECT ROW_COUNT();`);
        console.log(`K 닫힌문 장면 approval-view-home 결재 권한 행 지움 ${n}`);
      } catch (e) {
        console.log(`K 닫힌문 결재 권한 행 지우기 실패 ${e instanceof K.KitStepError ? L.sanitize(e.message, 120) : L.errorName(e)}`);
      }
    }
  }
}

// ── 장면 5: 휴대폰 /m/device-register(옛 QR 주소 · 로그인 없음) ──
async function scenePhone(st) {
  const step = { now: '휴대폰 화면 열기' };
  let kb = null;
  try {
    kb = await openCtx(st, 'S3PHONE', { viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true, userAgent: PHONE_UA });
    const t = crypto.randomBytes(32).toString('hex'); // 어디에도 없는 값(서버 표에 없음 · 비밀 아님)
    // ⬛ [낡은 몸 · 고침 1] 로그인 없이 열고 안내 글을 기다렸다(notice 참이어야 통과) — 런 46118135957 got path=/login.
    //   사유: 그 화면은 [AllowAnonymous] 지만 기본 틀 MainLayout(:2 [Authorize] · :106 ~ :108 RedirectToLogin)이 로그인 창으로 보낸다(main 같음 · 설계 화면 손 0).
    // ⬛ await kb.page.goto(`${K.BASE}/m/device-register?t=${t}`, { waitUntil: 'domcontentloaded', timeout: 60000 });
    // ⬛ const notice = await seen(kb.page, PHONE_NOTICE);
    // ⬛ await K.sleep(2000);
    // ⬛ const ask = await kb.page.getByText(PHONE_OLD_ASK, { exact: false }).count();
    // ⬛ const band = await errBand(kb.page);
    // ⬛ const at = K.pathOf(kb.page.url()).split('?')[0];
    // ⬛ rec(st, 'phone', { pass: notice && ask === 0 && kb.oldReq === 0 && band === false, got: { notice, oldAsk: ask, oldRequests: kb.oldReq, path: at, errBand: band } });
    // 🔴 고침 2 ③ — ㉮ 로그인 없음 → /login 착지(로그인 창 칸이 보임) ㉯ 같은 휴대폰으로 로그인 → 같은 옛 주소 → 안내. 둘 다 옛 물음 0 · 옛 요청 0 · 흰 띠 0.
    await kb.page.goto(`${K.BASE}/m/device-register?t=${t}`, { waitUntil: 'domcontentloaded', timeout: 60000 });
    const loginField = await kb.page.getByLabel('아이디', { exact: true }).first()
      .waitFor({ state: 'visible', timeout: TEXT_MS }).then(() => true, () => false);
    await K.sleep(2000); // 옛 화면이었다면 등록 요청이 나갈 몫
    const anon = {
      path: K.pathOf(kb.page.url()).split('?')[0],
      loginField,
      notice: (await kb.page.getByText(PHONE_NOTICE, { exact: false }).count()) > 0,
      oldAsk: await kb.page.getByText(PHONE_OLD_ASK, { exact: false }).count(),
      oldRequests: kb.oldReq,
      errBand: await errBand(kb.page),
    };
    const anonPass = anon.path === '/login' && anon.loginField && anon.oldAsk === 0 && anon.oldRequests === 0 && anon.errBand === false;
    // ㉯ 로그인한 휴대폰(새 직원 · 이미 첫 로그인 지남) — 갈 곳은 판정하지 않는다(새 기기 알림 경합은 장면 4 진단 몫)
    step.now = '휴대폰 로그인';
    if (!st.prepOk) throw new K.KitStepError(`준비 실패 — ${st.prepWhy}`);
    await kb.page.getByLabel('아이디', { exact: true }).fill(ID.fresh, { timeout: 60000 });
    await kb.page.getByLabel('비밀번호', { exact: true }).fill(st.freshPw);
    await kb.page.getByLabel('비밀번호', { exact: true }).press('Tab');
    await kb.page.getByRole('button', { name: '로그인', exact: true }).click();
    const lend = Date.now() + 30000;
    while (kb.login.seen === 0 && Date.now() < lend) await K.sleep(250);
    await K.waitMasked(kb.tok);
    if (kb.login.seen === 0 || !kb.tok.access) throw new K.KitStepError(`휴대폰 로그인 안 됨(로그인 응답 ${kb.login.seen}회 · status=${kb.login.status})`);
    await K.sleep(3000); // 로그인 뒤 이동이 끝날 몫(갈 곳 무판정)
    step.now = '로그인한 휴대폰 옛 주소';
    const before = kb.oldReq;
    await kb.page.goto(`${K.BASE}/m/device-register?t=${t}`, { waitUntil: 'domcontentloaded', timeout: 60000 });
    const notice = await seen(kb.page, PHONE_NOTICE);
    await K.sleep(2000);
    const signed = {
      path: K.pathOf(kb.page.url()).split('?')[0],
      notice,
      oldAsk: await kb.page.getByText(PHONE_OLD_ASK, { exact: false }).count(),
      oldRequests: kb.oldReq - before,
      errBand: await errBand(kb.page),
    };
    const signedPass = signed.notice && signed.oldAsk === 0 && signed.oldRequests === 0 && signed.errBand === false;
    const pass = anonPass && signedPass;
    rec(st, 'phone', { pass, got: pass ? { anon, signed } : { anon, signed, dx: diag(kb) } });
  } catch (e) {
    dev(st, 'phone', step, e);
  } finally {
    if (kb) await closeKb(st, kb); // 로그인 없음 — 컨텍스트만 닫힌다
  }
}

// ── 장면 6 · 7: 닫힌 두 서버 문 — 처리기 도달 0(시험 DB 행 변화 0) ──
async function probes(st) {
  const step = { now: '대표 API 로그인' };
  let p = null;
  try {
    p = await apiOpen(st, 'PARENT', ID.parent, PW.parent);
    const tid = idOk(jwtClaim(p.token, 'tenant_id'), '회사 번호');
    step.now = 'mobile-token 앞 셈';
    const before = counts(tid);
    step.now = 'POST mobile-token';
    const r = await rawCall('POST', '/api/devices/mobile-token', p, {});
    const after = counts(tid);
    console.log(`K 닫힌문 mobile-token status=${r.status} · 꼴 ${r.contentType || '-'} · 본문 ${r.bodyLength}자 · 토큰 행 ${before.tokens}→${after.tokens} · 기기 행 ${before.devices}→${after.devices}`);
    const pass = after.tokens === before.tokens && after.devices === before.devices && r.handlerWords === 0;
    rec(st, 'token', { pass, got: { ...r, tokenRows: `${before.tokens}→${after.tokens}`, deviceRows: `${before.devices}→${after.devices}` } });
  } catch (e) {
    dev(st, 'token', step, e);
  }
  const step2 = { now: '쓸 수 있는 토큰 심기' };
  let hash = null;
  try {
    if (!p) throw new K.KitStepError('대표 세션 없음 — 앞에서 섰다');
    const tid = idOk(jwtClaim(p.token, 'tenant_id'), '회사 번호');
    const by = idOk(jwtClaim(p.token, 'user_id'), '대표 번호');
    const plain = crypto.randomBytes(32).toString('hex');
    L.maskAndRegister('S3_QR_PLAIN', plain); // 열쇠 꼴 값 — 가리고 누출 검사 대상
    hash = crypto.createHash('sha256').update(plain, 'utf8').digest('hex'); // TenantDeviceService.Sha256Hex 와 같은 꼴(소문자 16진)
    if (!SAFE_HEX.test(hash)) throw new K.KitStepError('토큰 해시 꼴 이상');
    // 서비스 발급(IssueMobileRegisterTokenAsync)과 같은 칸 · 같은 10분 만료
    dbq(`INSERT INTO device_register_tokens (token_id, tenant_id, token_hash, issued_by, issued_at, expires_at) VALUES (UUID(), '${tid}', '${hash}', '${by}', NOW(6), DATE_ADD(NOW(6), INTERVAL 10 MINUTE));`);
    const before = counts(tid);
    const body = { token: plain, deviceName: 'ci-s3-phone', fingerprint: `ci-s3-fp-${crypto.randomBytes(6).toString('hex')}` };
    step2.now = 'POST mobile-register(대표 세션)';
    const r = await rawCall('POST', '/api/devices/mobile-register', p, body);
    step2.now = 'POST mobile-register(로그인 없음 · 기록만)';
    const anon = await rawCall('POST', '/api/devices/mobile-register', null, body);
    const after = counts(tid);
    const row = dbq(`SELECT (used_at IS NULL) + 0, (used_device_id IS NULL) + 0 FROM device_register_tokens WHERE token_hash = '${hash}';`);
    const unused = row.length === 1 && row[0][0] === '1' && row[0][1] === '1';
    console.log(`K 닫힌문 mobile-register status=${r.status} · 꼴 ${r.contentType || '-'} · 본문 ${r.bodyLength}자 · 로그인 없음 status=${anon.status} · 심은 토큰 안 쓰임=${unused} · 기기 행 ${before.devices}→${after.devices}`);
    const pass = unused && after.devices === before.devices && r.handlerWords === 0 && anon.handlerWords === 0;
    rec(st, 'register', {
      pass,
      got: { ...r, anonStatus: anon.status, anonBodyLength: anon.bodyLength, anonHandlerWords: anon.handlerWords, plantedUnused: unused, deviceRows: `${before.devices}→${after.devices}`, tokenRows: `${before.tokens}→${after.tokens}` },
    });
  } catch (e) {
    dev(st, 'register', step2, e);
  } finally {
    if (hash) {
      try {
        dbq(`DELETE FROM device_register_tokens WHERE token_hash = '${hash}';`);
      } catch (e) {
        console.log(`K 닫힌문 심은 토큰 지우기 실패 ${e instanceof K.KitStepError ? L.sanitize(e.message, 120) : L.errorName(e)}`);
      }
    }
    if (p) await apiClose(st, p);
  }
}

// 끝 정리 — 연 브라우저 세션 · API 세션 모두 로그아웃 · 브라우저 닫기. 실패는 수만 센다(던지지 않음).
async function cleanup(st) {
  if (st.cleaned) return;
  st.cleaned = true;
  for (const kb of st.kbs) await closeKb(st, kb);
  for (const s of [...st.apiSess]) await apiClose(st, s);
  if (st.br) await st.br.close().catch((e) => console.log(`K 닫힌문 브라우저 닫기 실패 ${L.errorName(e)}`));
  // 🔴 HQA 보류 고침 · 길 B — 복원 캐시 켠 브라우저(장면이 처음 열 때만 생김)
  if (st.brB) await st.brB.close().catch((e) => console.log(`K 닫힌문 복원 캐시 브라우저 닫기 실패 ${L.errorName(e)}`));
}

const st0 = {
  r: null, br: null, kbs: [], apiSess: [], logoutFail: 0, cleaned: false,
  tid: null, parentUid: null, freshUid: null, freshPw: null, prepOk: false, prepWhy: null,
  termsPw: null, termsOk: false, termsWhy: null, // 🔴 갈래 T — 약관 미동의 직원
  brB: null, brBNote: '-', // 🔴 HQA 보류 고침 · 길 B — 복원 캐시 켠 브라우저
};

async function main() {
  const st = st0;
  st.r = L.loadResult();
  L.bindAutosave(st.r);
  const allKeys = Object.values(KEY);
  let fatalOnce = false;
  const fatal = (where) => (e) => {
    if (fatalOnce) return;
    fatalOnce = true;
    try {
      console.log(`K 닫힌문 프로세스 밖 예외(${where}) ${L.errorName(e)}`);
      K.fillDevice(st.r, allKeys, `단언 전 실패 — 프로세스 밖 예외(${where})`, e);
      L.saveResult(st.r);
    } catch (e2) {
      console.log(`⚫ K 닫힌문 끊긴 자리 기록 실패 (${L.errorName(e2)})`);
    }
    // 세션 놓기 — 10초 상한 뒤 무조건 종료
    setTimeout(() => process.exit(1), 10000).unref();
    cleanup(st).then(() => process.exit(1), () => process.exit(1));
  };
  process.on('unhandledRejection', fatal('unhandledRejection'));
  process.on('uncaughtException', fatal('uncaughtException'));
  for (const sig of ['SIGTERM', 'SIGINT']) process.on(sig, () => fatal(`신호 ${sig}`)(new K.KitStepError(`신호 ${sig}`)));
  setTimeout(() => fatal('닫힌문 전체 시간 상한')(new K.KitStepError(`닫힌문 전체 시간 상한 ${S3_TOTAL_MS / 60000}분`)), S3_TOTAL_MS).unref();

  let pre = null;
  try {
    const pw = require('playwright');
    st.br = await pw[BROWSER].launch();
  } catch (e) {
    pre = `브라우저 기동 실패 ${L.errorName(e)}`;
  }
  try {
    await prep(st);
    st.prepOk = true;
  } catch (e) {
    st.prepWhy = e instanceof K.KitStepError ? L.sanitize(e.message, 160) : `예외 ${L.errorName(e)}`;
    console.log(`K 닫힌문 준비 실패 — ${st.prepWhy}`);
  }
  // ⬛ [낡은 줄 · §8-12 main 합침 뒤 계정 자리 — 약관 미동의 직원을 prep 바로 뒤(활성 6번째)에 만들어 409. 아래 장면 묶음 끝(prep 치운 뒤)으로 옮김]
  // ⬛ // 🔴 갈래 T — 약관 미동의 직원(실패는 약관 두 장면만 장치 거짓)
  // ⬛ try {
  // ⬛   await prepTerms(st);
  // ⬛   st.termsOk = true;
  // ⬛ } catch (e) {
  // ⬛   st.termsWhy = e instanceof K.KitStepError ? L.sanitize(e.message, 160) : `예외 ${L.errorName(e)}`;
  // ⬛   console.log(`K 닫힌문 약관 미동의 직원 준비 실패 — ${st.termsWhy}`);
  // ⬛ }
  if (pre) {
    console.log(`K 닫힌문 시작 전 실패 — ${pre}`);
    // ⬛ [낡은 줄 · 고침 3] K.fillDevice(st.r, [KEY.home, KEY.update, KEY.welcome, KEY.phone, KEY.gate], `단언 전 실패 — ${pre}`);
    K.fillDevice(st.r, [KEY.home, KEY.update, KEY.welcome, KEY.phone, KEY.gate,
      KEY.termsFirst, KEY.termsSlow, KEY.relogin, KEY.loginDirect, KEY.dialog, KEY.owner, KEY.owner503, KEY.sales,
      // ⬛ [낡은 줄 · HQA 보류 고침 전]   KEY.ownerJson], `단언 전 실패 — ${pre}`); // 🔴 갈래 B2 — 새 키 owner-json
      KEY.ownerJson, KEY.termsSlowProg, KEY.stopAfterLogin, KEY.backRestore], `단언 전 실패 — ${pre}`); // 🔴 갈래 B2 — 새 키 owner-json · 🔴 HQA 보류 고침 — 새 키 셋
  } else {
    await sceneHome(st);
    await sceneUpdate(st);
    await scenePhone(st);
    // 🔴 갈래 T — 판정 장면 7(새 직원 장면은 판매 역할 바꿈 전에 · 판매 역할은 맨 끝)
    // ⬛ [낡은 줄 · §8-12 — 약관 두 장면은 prep 직원을 쓰는 장면이 다 끝나고 prep 을 치운 뒤로 옮김] await sceneTerms(st);
    await sceneRelogin(st);
    await sceneDialog(st);
    await sceneOwner(st);
    await sceneSales(st);
    await sceneApprovalView(st); // 🔴 B2 고침 1 ⑦ — 기록만(키 아님) · 맨 끝(권한 행 넣고 지움)
    // 🔴 HQA 보류 고침 · 길 B(⑪) — prep 직원(relogin-home 과 같은 직원)을 쓰는 마지막 장면 · 치우기 전 · 새 계정 0(계정 자리 5 한도 · §8-12)
    await sceneBackRestore(st);
    // 🔴 §8-12 (가) 만든 몸이 치운다 — 여기서부터 prep 직원을 쓰는 장면 0(장면 끝마다 화면 세션 로그아웃 · API 세션도 닫힘).
    //   prep 계정 사용 안 함(자리 돌려줌) → 약관 미동의 직원 만들기 → 약관 두 장면. 치우기 · 만들기 실패 = 약관 두 장면만 장치 거짓(종전 꼴 그대로).
    //   약관 두 장면은 다른 직원 · 새 창이라 앞 장면에 기대는 것 없음(기기 한도 판정은 10/5 슬롯 폐기로 없음 · 판매 역할 · 결재 권한 행은 prep 직원 것).
    try {
      await releasePrep(st);
      await prepTerms(st);
      st.termsOk = true;
    } catch (e) {
      st.termsWhy = e instanceof K.KitStepError ? L.sanitize(e.message, 160) : `예외 ${L.errorName(e)}`;
      console.log(`K 닫힌문 약관 미동의 직원 준비 실패 — ${st.termsWhy}`);
    }
    await sceneTerms(st);
    // 🔴 HQA 보류 고침 · 길 A2(⑪) — 약관 미동의 직원 그대로(약관 두 장면 뒤 · 새 계정 0)
    await sceneStopAfterLogin(st);
  }
  await probes(st); // 브라우저 없이도 돈다
  K.fillDevice(st.r, allKeys, '경우 끝 뒤 남은 키(있으면 장치 오류)');
  await cleanup(st);
  // ⬛ [낡은 줄 · 고침 2] const mine = st.r.rows.filter((x) => x.scope === 'K' && String(x.id || '').startsWith('K35-S3'));
  // ⬛ [낡은 줄 · 고침 2] const bad = mine.filter((x) => x.pass !== true).length;
  // ⬛ [낡은 줄 · 고침 2] console.log(`K 닫힌문 본문 끝: 줄 ${mine.length} · 거짓 ${bad} · 로그아웃 실패 ${st.logoutFail}`);
  // ⬛ [낡은 줄 · 고침 2] L.saveResult(st.r);
  // ⬛ [낡은 줄 · 고침 2] process.exit(bad === 0 && st.logoutFail === 0 ? 0 : 1);
  // 🔴 고침 3 — 끝 코드 = endCode(판정기 면제 함수 그대로) · 끝 줄에 알려진 결함 수
  const mine = myRows(st.r.rows);
  const bad = mine.filter((x) => x.pass !== true).length;
  const known = mine.filter((x) => J.knownDefectFail(x)).length;
  console.log(`K 닫힌문 본문 끝: 줄 ${mine.length} · 거짓 ${bad} · 로그아웃 실패 ${st.logoutFail} · 알려진 결함 ${known}(거짓 수 안 · 끝 코드에서 뺌)`);
  L.saveResult(st.r);
  process.exit(endCode(st.r.rows, st.logoutFail));
}

// 🔴 고침 3 — 이 몸의 줄 · 끝 코드(순수 함수 · 자체시험 K-C27h). 거짓 줄 중 판정기 knownDefectFail 참인 줄은 뺀다 · 로그아웃 실패는 그대로 1.
function myRows(rows) {
  return (Array.isArray(rows) ? rows : []).filter((x) => x && x.scope === 'K' && String(x.id || '').startsWith('K35-S3'));
}
function endCode(rows, logoutFail) {
  const bad = myRows(rows).filter((x) => x.pass !== true && !J.knownDefectFail(x)).length;
  return bad === 0 && logoutFail === 0 ? 0 : 1;
}

if (require.main === module) main();
// ⬛ [낡은 줄 · 고침 2] module.exports = { KEY, HANDLER_WORDS, isWelcome };
// ⬛ [낡은 줄 · 고침 3] module.exports = { KEY, HANDLER_WORDS, isWelcome, endCode, homeKnown, updateKnown }; // 고침 3 — endCode · 알려진 꼴 두 식
// 🔴 갈래 T — 판정 함수 JUDGE · 문서 이동 1건 · 알림 글 예외 꼴(자체시험 K-C28a)
// ⬛ [낡은 줄 · 갈래 B2 이전] module.exports = { KEY, HANDLER_WORDS, isWelcome, endCode, homeKnown, updateKnown, JUDGE, oneDoc, snackBadCount, HOLD_MS, OWNER_LABELS };
// 🔴 갈래 B2 — 뜬 알림 셈 · 초기 스크립트 · 처리기 글 머리(자체시험 K-C29a ~ e)
// ⬛ [낡은 줄 · B2 고침 1 이전] module.exports = { KEY, HANDLER_WORDS, isWelcome, endCode, homeKnown, updateKnown, JUDGE, oneDoc, snackBadCount, HOLD_MS, OWNER_LABELS,
// ⬛ [낡은 줄 · B2 고침 1 이전]   snackTally, SNACK_SEEN_INIT, FORBID_TEXT, H403_TAG, DASH_FAIL_TEXT };
// 🔴 B2 고침 1 — 진행 그림 콘솔 꼴(⑥ K-C29i) · G13 기대 꼴(⑦ K-C29j)
module.exports = { KEY, HANDLER_WORDS, isWelcome, endCode, homeKnown, updateKnown, JUDGE, oneDoc, snackBadCount, HOLD_MS, OWNER_LABELS,
  snackTally, SNACK_SEEN_INIT, FORBID_TEXT, H403_TAG, DASH_FAIL_TEXT,
  // ⬛ [낡은 줄 · HQA 보류 고침 전]   PROG_WATCH, PROG_TAG, PROG_TICKS, progParse, progSum, g13Ok };
  PROG_WATCH, PROG_TAG, PROG_TICKS, progParse, progSum, g13Ok,
  // 🔴 HQA 보류 고침 · 물러남(⑪ · 자체시험 K-C30a) — 표본기 · 해석 · 끝 꼴
  STALL_INIT, STALL_TAG, STALL_MIN_SAMPLES, stallParse, formBack, stallShape };
