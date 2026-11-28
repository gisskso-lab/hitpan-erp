// 화면 오류 기록 띠 — 갈래 T 장면 · 판정기 (20261128작 화면오류기록띠 PR-1 · 설계 docs/설계/erp/화면오류기록띠_설계_20261128.md §5-1 · §5-2 ·
//   작업지시서 docs/운영기록/20261128작_화면오류기록띠_작업지시서.md §2 갈래 T · §4 · §8-1 · 개발명세서 docs/개발/erp/20261128_화면오류기록띠_갈래T_개발명세서.md)
//
// 🔴 이 파일의 규칙
//   ① 이름이 K 본문 머리(k35-)로 시작하지 않는다 — 실행기(run-k-bodies.js)·판정기(judge.js)가 돌리지 않고 키 틀에도 안 들어간다.
//      결과표도 따로(ui-out/main/console-error-band-result.json) — judge.js · k35-ws03-closed.js 판정 줄 손 0(#1032 · #1011 겹침 줄임).
//      판정은 이 파일의 순수 함수 judgeScene 하나 · 끝 코드는 endCode 하나(자체시험 console-error-band.selftest.js 가 참 · 거짓 반례로 부른다).
//   ② 모드 스위치 = 환경 칸 UI_BAND_MODE — `control`(대조군 · 고치기 전 판 · 장면마다 흰 띠 1 + 그 줄 표지의 표준오류 1 이상 = 장면이 그 줄에 닿는다)
//      · `fixed`(고친 뒤 · 흰 띠 0(+3초 · +10초) · 그 줄 표지의 console.warn 1 이상 · 앱 표준오류 꼴 0). 그 밖 글자 · 빈 칸 = 아무것도 안 돌리고 실패.
//      두 모드 공통 = 실패 주입이 실제로 걸림(가로챔 1 이상 · 진짜 403 은 서버 403 응답 1 이상) · 원래 있던 알림 글 1 이상(있는 장면만).
//      넓힘(갈래 X · 지시서 §8-3 대표 ④) 장면 = fixedOnly(고친 뒤 모드에서만 돎) · block 칸이 있으면 그 단추가 막혀 있어야 통과.
//   ③ 실패 주입은 시험 장치의 가로채기(page.route)로만 — 앱 손 0. ⬛ 옛 줄(대조군 1회째 c3b5240b): 「진짜 403 장면(G-S1)은 가로채기 0 · 서버 판정 그대로.」
//      → 고침1(지시서 §8-2 · 런 46151366436 G-S1 = 서버 200 · 403 0): G-S1 도 가로채기 403 으로 바꿈 · 진짜 403 주입 꼴(real403)은 틀만 남김(쓰는 장면 0).
//      `api/auth/update-status` 는 가로채지 않는다(R 과 U(#43) 가 같이 부름 · U 는 PR-2 몫 · 설계 §5-2 ⚠ 줄) — 장면 목록 꼴 검사가 막는다.
//   ④ 연 세션은 끝에 반드시 놓는다(로그아웃 · 11/27 계정 잠김 사고 선례) — 정상 · 예외 · 시간 상한 · 신호 모든 길에서 cleanup.
//   ⑤ 그림을 찍지 않는다 · 콘솔 원문은 메모리에서 수만 센다(표지 낱말 포함 여부) · 찍는 글자는 소독 뒤(K.consoleLine · L.sanitize).
//   ⑥ 이 PC 에서 돌리지 않는다(이 PC DB = 실물) — CI ui-browser.yml 「[시험] S9b」 단계에서만.
'use strict';

const fs = require('fs');
const path = require('path');
const L = require('./lib');
const K = require('./kit35');
const DC = require('./device-check'); // 머리 조건(require.main) 때문에 불러도 장치 본문은 안 돈다 — 내보냄만 쓴다

const env = process.env;
const BROWSER = 'chromium';
const SCOPE = 'BAND';
const MODES = Object.freeze(['control', 'fixed']);
// ⬛ 옛 줄(고침1 §8-5 전 · 장면 13): const BAND_TOTAL_MS = 8 * 60 * 1000; // 이 몸 전체 상한(워크플로 S9b 단계 상한 10분 안)
// 고침1(지시서 §8-5 #2 · #3 · #4) — 장면 13 → 16(G-S1b · G-P5 · G-X1o) · 8분 × 16/13 ≈ 9.9 → 10분(워크플로 S9b 단계 상한 12분 안)
const BAND_TOTAL_MS = 10 * 60 * 1000;
const PAGE_MS = 60000;
const FORBID_TEXT = '이 기능에 접근할 권한이 없습니다'; // HitPanApiAuthHandler 평소 403 갈래 알림 글 머리(k35-ws03-closed.js 와 같은 글자 · 따로 둠)
// 고침1(지시서 §8-5 #2 · 대표 ③ · ④) — 쉬운 알림 글 전문(앱 LoadFailedNotice.Text 와 글자까지 같음 · 자체시험 B14 가 원문과 맞춤).
//   알림 맞춤은 앞머리 고정(startsWith) — 「대시보드 정보를 불러오지 못했습니다」 같은 남의 알림은 안 맞는다([3-V] P3-3).
const NOTICE_TEXT = '정보를 불러오지 못했습니다. 잠시 후 다시 시도해 주세요.';
// 알림 칸(고침1 · 고친 뒤 모드에서만 잼) — once = 사람이 연 화면(그 알림 글 정확히 1 · 원래 알림이 쉬운 알림이 아니면 쉬운 알림 0 = 같은 실패에 두 번 아님)
//   · quiet = 상주 부품 · 정상 짝(그 장면 알림 글 밖의 알림 0 — 쉬운 알림 포함)
const NOTICES = Object.freeze(['once', 'quiet']);
// 닫힌 주소(BetaClosedRoutes.All · 2026-11-28 판) — 장면이 닫힌 화면을 고르면 안내 화면만 떠 그 줄에 안 닿는다(꼴 검사)
const CLOSED_PATHS = Object.freeze(['/settings', '/settings/ai-assistant', '/sales/profitability', '/tax-invoice-stats', '/accounting/export', '/welcome']);
// 가로채면 안 되는 주소 — U(#43) 공유 줄(설계 §5-2 ⚠) · 로그인 · 갱신 · 로그아웃(세션 놓기 길)
const NO_INTERCEPT = Object.freeze(['/api/auth/update-status', '/api/auth/login', '/api/auth/refresh', '/api/auth/logout']);
const INJECTS = Object.freeze({
  503: { status: 503 },
  404: { status: 404 },
  403: { status: 403 },
  abort: { abort: 'failed' }, // 네트워크 끊김
  real403: { real: true }, // 가로채기 0 · 서버가 진짜 403
  // 고침1(§8-5 #3) — 가로채기 0 · 정상 짝 장면(그 주소 2xx 응답 1 이상이어야 · 단추 열림 · 경고 흔적 0)
  none: { none: true },
});

// ── 장면 표(설계 §5-2 · 키 = 결과표 path 칸 · 줄 = 고치기 전 판 dc49c2d0 기준) ──
//   tags = 그 줄의 표지(옛 줄 글자 앞머리 · 설계 §2-2 「표지 그대로」) — 대조군은 console.error 에서 · 고친 뒤는 console.warn 에서 센다.
//   snack = 그 자리에 원래 있던 알림 글(없으면 null) — 두 모드 다 1 이상이어야 함(사라지면 빨강 · 설계 §5-1 알림 칸).
//   고침1(지시서 §8-5 #2) — 칸 둘 더함: notice = 'once'(연 화면) · 'quiet'(상주 · 정상 짝) · newSnack = 그 알림이 고친 뒤 판에서 새로 붙은 쉬운 알림
//   (대조군 판엔 없음 → 대조군 모드에선 알림 칸을 안 잼). ⬛ 옛 칸: G-S2 · S3 · P1 · C1 snack: null(알림을 안 잼 = [4] P1-2) · G-X1 · G-P4 snack: '정보를 불러오지 못했습니다'(앞머리 부분 일치).
const SCENES = Object.freeze([
  Object.freeze({ key: 'G-R1a', branch: 'R', who: 'owner', page: '/', match: { path: '/api/cs/replies/unread-count' }, inject: '503',
    tags: ['[CS] 안 읽은 답 수 조회 실패', '[Sidebar] CS 안읽음 수 조회 실패'], snack: null, notice: 'quiet',
    lines: 'CsReplyBadge.razor:88 · Sidebar.razor:660', what: '상주 · CS 안 읽은 수 503(배너 · 사이드바 둘 다 · 알림 0)' }),
  Object.freeze({ key: 'G-R1b', branch: 'R', who: 'owner', page: '/', match: { path: '/api/cs/replies/unread-count' }, inject: 'abort',
    tags: ['[CS] 안 읽은 답 수 조회 실패', '[Sidebar] CS 안읽음 수 조회 실패'], snack: null, notice: 'quiet',
    lines: 'CsReplyBadge.razor:88 · Sidebar.razor:660', what: '상주 · CS 안 읽은 수 끊김(배너 · 사이드바 둘 다 · 알림 0)' }),
  Object.freeze({ key: 'G-R2', branch: 'R', who: 'owner', page: '/', match: { path: '/api/auth/terms/status' }, inject: '403',
    tags: ['[HP15]'], snack: FORBID_TEXT, notice: 'quiet',
    lines: 'TermsConsentBanner.razor:69', what: '상주 · 약관 상태 403(처리기 알림 그대로 · 그 밖 알림 0)' }),
  // ⬛ 옛 줄(대조군 1회째 c3b5240b · 런 46151366436 = 서버 200 · 403 0 · 시험 직원 기본 역할 sales_user 라 SalesOnly 에 안 걸림):
  //   inject: 'real403' · what: '공용 서비스 · 직원 창 어음 목록 서버 403(SalesOnly · 가로채기 0)'
  // 고침1(지시서 §8-2 PM 결재): 서버 403 에 기대지 않고 시험 장치가 어음 목록 조회를 403 으로 가로챔(앱 손 0)
  //   → 처리기 평소 403 갈래 알림 그대로 + GetFromJsonAsync 예외 → [Bills.List] 줄에 닿음(역할과 무관).
  Object.freeze({ key: 'G-S1', branch: 'S', who: 'staff', page: '/accounting/bills', match: { path: '/api/finance/bills' }, inject: '403',
    tags: ['[Bills.List]'], snack: FORBID_TEXT, notice: 'once',
    lines: 'BillsCardsBankService.cs:21', what: '공용 서비스 · 직원 창 어음 목록 403(시험 장치 가로채기 · 처리기 알림 한 번 · 쉬운 알림 0 = 403 거름)' }),
  // 고침1(지시서 §8-5 #2 · [4] P1-2) — 대표 ③ 첫 항목(어음 · 카드 · 통장 503 = 쉬운 알림 한 번)을 재는 장면.
  //   카드 · 통장 목록은 같은 부품(BillsCardsBankService · 같은 NotifyLoadFailed 한 자리)이라 어음 하나로 둔다(개발명세서 고침1 §2).
  Object.freeze({ key: 'G-S1b', branch: 'S', who: 'owner', page: '/accounting/bills', match: { path: '/api/finance/bills' }, inject: '503',
    tags: ['[Bills.List] 어음 목록 조회 실패'], snack: NOTICE_TEXT, notice: 'once', newSnack: true, fixedOnly: true,
    lines: 'BillsCardsBankService.cs:28', what: '공용 서비스 · 어음 목록 503(쉬운 알림 한 번)' }),
  Object.freeze({ key: 'G-S2', branch: 'S', who: 'owner', page: '/', match: { path: '/api/dashboard/unified-calendar' }, inject: 'abort',
    tags: ['[UnifiedCalendarService.'], snack: NOTICE_TEXT, notice: 'once', newSnack: true,
    lines: 'UnifiedCalendarService.cs:55', what: '공용 서비스 · 달력 월 조회 끊김(404 · 503 은 조용히 null — 줄에 안 닿음 · 쉬운 알림 한 번)' }),
  Object.freeze({ key: 'G-S3', branch: 'S', who: 'owner', page: '/return-status', match: { path: '/api/purchase/returns' }, inject: '503',
    tags: ['[작18] 매입반품 목록 조회 실패'], snack: NOTICE_TEXT, notice: 'once', newSnack: true,
    lines: 'DeliveryService.cs:593', what: '공용 서비스 · 매입 반품 목록 503(반품현황 전표별 기본 보기 · 쉬운 알림 한 번)' }),
  Object.freeze({ key: 'G-P1', branch: 'P', who: 'owner', page: '/purchase/statistics', match: { path: '/api/reports/purchase-statistics' }, inject: '503',
    tags: ['[HP15]'], snack: NOTICE_TEXT, notice: 'once', newSnack: true,
    lines: 'PurchaseStatisticsPage.razor:155', what: '업무 현황 · 매입 통계 503(쉬운 알림 한 번)' }),
  Object.freeze({ key: 'G-P2', branch: 'P', who: 'owner', page: '/sales/statistics', match: { path: '/api/reports/sales-statistics' }, inject: '503',
    tags: ['[HP15]'], snack: '판매현황 조회에 실패했습니다', notice: 'once',
    lines: 'SalesStatisticsPage.razor:155', what: '업무 현황 · 판매 통계 503(알림 그대로 한 번)' }),
  Object.freeze({ key: 'G-P3', branch: 'P', who: 'owner', page: '/stock', match: { path: '/api/reports/stock-status' }, inject: '503',
    tags: ['[HP15]'], snack: '재고현황 조회에 실패했습니다', notice: 'once',
    lines: 'Stock.razor:132', what: '업무 현황 · 재고현황 503(알림 그대로 한 번)' }),
  Object.freeze({ key: 'G-C1', branch: 'C', who: 'owner', page: '/support/cs/ci-band-none', match: { re: '^/api/cs/requests/[^/]+/replies$' }, inject: '503',
    tags: ['[CS] 답 목록 조회 실패'], snack: NOTICE_TEXT, notice: 'once', newSnack: true,
    lines: 'CsInboxPage.razor:179', what: '그 밖 · CS 상세 답 목록 503(쉬운 알림 한 번)' }),
  Object.freeze({ key: 'G-C2', branch: 'C', who: 'owner', page: '/print-settings', match: { path: '/api/settings/print-common' }, inject: '404',
    tags: ['[PrintSettings] 공통 설정을 못 읽었다'], snack: '워터마크 설정을 불러오지 못했습니다', notice: 'once',
    lines: 'PrintSettingsPage.razor:395', what: '설정 · 인쇄 공통 설정 404(알림 그대로 한 번)' }),
  // 넓힘X1(지시서 §8-3 대표 ④ · 개발명세서 docs/개발/erp/20261128_화면오류기록띠_넓힘X1_개발명세서.md §5) — 고친 뒤에만 도는 장면(fixedOnly).
  //   대조군 판(고치기 전)엔 쉬운 알림 · 저장 막음이 없으므로 대조군 모드에선 목록에서 빠진다(scenesFor).
  //   snack = LoadFailedNotice.Text 앞머리(자체시험 B12 가 원문 글자와 맞춤) · block = 막혀야 할 단추 이름(빈 값 덮어쓰기 방지).
  Object.freeze({ key: 'G-X1', branch: 'X', who: 'owner', page: '/data/backup', match: { path: '/api/backup/settings' }, inject: '503',
    tags: ['[BackupService.GetSettingsAsync]'], snack: NOTICE_TEXT, notice: 'once', newSnack: true, block: '설정 저장', fixedOnly: true,
    lines: 'BackupService.cs:13 · BackupPage.razor:171', what: '넓힘X1 · 백업 설정 조회 503(쉬운 알림 한 번 · [설정 저장] 막힘)' }),
  // 고침1(지시서 §8-5 #3 · [3-V] P2-1) — G-X1 의 정상 짝: 가로채기 없는 같은 화면 = [설정 저장] 열림 · 그 조회 2xx 1 이상 · 경고 흔적 0 · 알림 0.
  //   「성공 갈래에서 막힘 표지를 안 끔」 회귀(백업 설정을 영영 저장 못 함)를 잡는다.
  Object.freeze({ key: 'G-X1o', branch: 'X', who: 'owner', page: '/data/backup', match: { path: '/api/backup/settings' }, inject: 'none',
    tags: ['[BackupService.GetSettingsAsync]'], snack: null, notice: 'quiet', open: '설정 저장', fixedOnly: true,
    lines: 'BackupPage.razor:73 · :171', what: '넓힘X1 짝 · 백업 설정 조회 정상([설정 저장] 열림 · 알림 0)' }),
  // 넓힘 X2(지시서 §8-3 대표 ④ · 개발명세서 docs/개발/erp/20261128_화면오류기록띠_넓힘X2_개발명세서.md) — 무음 화면 넓힘 대표 1(발주 현황).
  //   snack = 이번에 새로 붙인 쉬운 알림 글(LoadFailedNotice.Text 앞머리) — 고친 뒤 판(fixed) 장면이다. 대조군(control)으로 돌리면 이 글이 없어 거짓이 맞다
  //   (대조군 런은 §8-3 에서 끝남 · 판정 함수 손 0 — X1 장면과 겹침 줄임). 키 꼴 검사(G-[RSPC]n)에 맞춰 P 갈래 다음 번호.
  Object.freeze({ key: 'G-P4', branch: 'P', who: 'owner', page: '/purchase-order-status', match: { path: '/api/reports/purchase-orders' }, inject: '503',
    tags: ['[HP15] 발주현황 조회 실패'], snack: NOTICE_TEXT, notice: 'once', newSnack: true,
    lines: 'PurchaseOrderStatusPage.razor:130', what: '넓힘 X2 · 발주 현황 503(띠 0 · 경고 흔적 · 쉬운 알림 한 번)' }),
  // 고침1(지시서 §8-5 #4 · [4] P2-1) — 설계 §6 VP 자리(PurchaseStatusPage :221 「[HP15] 매입현황 조회 실패」)를 여는 장면. 원래 알림(Error) 한 번 · 쉬운 알림 0.
  Object.freeze({ key: 'G-P5', branch: 'P', who: 'owner', page: '/purchase-status', match: { path: '/api/reports/purchases' }, inject: '503',
    tags: ['[HP15] 매입현황 조회 실패'], snack: '매입현황 조회에 실패했습니다', notice: 'once', fixedOnly: true,
    lines: 'PurchaseStatusPage.razor:221', what: '업무 현황 · 매입현황 503(알림 그대로 한 번)' }),
]);

// 모드에 도는 장면 — 대조군은 고치기 전 판에 있던 자리만(fixedOnly 뺌) · 고친 뒤는 전부
function scenesFor(mode, list = SCENES) {
  return (Array.isArray(list) ? list : []).filter((s) => mode === 'fixed' || !(s && s.fixedOnly === true));
}

// ── 순수 함수(자체시험 대상 · 화면 · 브라우저 0) ──
function modeOf(v) {
  return typeof v === 'string' && MODES.includes(v) ? v : null;
}
function urlPath(u) {
  try {
    return (u instanceof URL ? u : new URL(String(u))).pathname;
  } catch (e) {
    console.log(`띠 장면 주소 해석 실패 (${L.errorName(e)})`);
    return null;
  }
}
// 장면 주소 맞춤 — 경로만(쿼리 뗌) · 정확히 같음 또는 정규식(앞뒤 고정)
function routeMatch(scene, u) {
  const p = urlPath(u);
  if (p === null || !scene || !scene.match) return false;
  if (typeof scene.match.path === 'string') return p === scene.match.path;
  if (typeof scene.match.re === 'string') return new RegExp(scene.match.re).test(p);
  return false;
}
// 장면 목록 꼴 — 문제 글자 목록(빈 목록 = 정상). 판정 시작 전에 부르고, 문제가 있으면 아무것도 안 돌린다.
function sceneListProblems(list = SCENES) {
  const out = [];
  if (!Array.isArray(list) || list.length === 0) return ['장면 0'];
  const seen = new Set();
  list.forEach((s, i) => {
    const n = `${i + 1}번`;
    if (!s || typeof s !== 'object') { out.push(`${n} 꼴 아님`); return; }
    // ⬛ 옛 줄(넓힘X1 전): if (!/^G-[RSPC][0-9]+[a-z]?$/.test(String(s.key))) out.push(`${n} 키 꼴`);
    if (!/^G-[RSPCX][0-9]+[a-z]?$/.test(String(s.key))) out.push(`${n} 키 꼴`);
    else if (s.key.charAt(2) !== s.branch) out.push(`${n} 갈래 어긋남`);
    if (seen.has(s.key)) out.push(`${n} 키 중복`);
    seen.add(s.key);
    // ⬛ 옛 줄(넓힘X1 전): if (!['R', 'S', 'P', 'C'].includes(s.branch)) out.push(`${n} 갈래 밖(R · S · P · C 만 — U 는 PR-2)`);
    if (!['R', 'S', 'P', 'C', 'X'].includes(s.branch)) out.push(`${n} 갈래 밖(R · S · P · C · X(넓힘) 만 — U 는 PR-2)`);
    // 넓힘(X) 장면 = 고친 뒤에만(대조군 판엔 그 알림 · 막음이 없다) · 막을 단추 이름은 비지 않은 글자
    if (s.branch === 'X' && s.fixedOnly !== true) out.push(`${n} 넓힘 장면은 고친 뒤에만(fixedOnly)`);
    if (!(s.fixedOnly === undefined || typeof s.fixedOnly === 'boolean')) out.push(`${n} fixedOnly 꼴`);
    if (!(s.block === undefined || (typeof s.block === 'string' && s.block.trim().length > 0))) out.push(`${n} 막을 단추 꼴`);
    if (!['owner', 'staff'].includes(s.who)) out.push(`${n} 창 주인 꼴`);
    if (!Object.prototype.hasOwnProperty.call(INJECTS, s.inject)) out.push(`${n} 주입 꼴`);
    if (typeof s.page !== 'string' || !s.page.startsWith('/')) out.push(`${n} 화면 주소 꼴`);
    else if (CLOSED_PATHS.includes(s.page.replace(/\/+$/, '') || '/')) out.push(`${n} 닫힌 화면`);
    const m = s.match || {};
    const hasPath = typeof m.path === 'string' && m.path.startsWith('/api/');
    const hasRe = typeof m.re === 'string' && m.re.startsWith('^/api/') && m.re.endsWith('$');
    if (hasPath === hasRe) out.push(`${n} 주소 맞춤 꼴(경로 하나 또는 앞뒤 고정 정규식 하나)`);
    for (const bad of NO_INTERCEPT) {
      if (routeMatch(s, `http://127.0.0.1${bad}`)) out.push(`${n} 가로채면 안 되는 주소(${bad})`);
    }
    if (!Array.isArray(s.tags) || s.tags.length === 0 || s.tags.some((t) => typeof t !== 'string' || !t.startsWith('[') || t.length < 3)) out.push(`${n} 표지 꼴`);
    if (!(s.snack === null || (typeof s.snack === 'string' && s.snack.length > 0))) out.push(`${n} 알림 글 꼴`);
    if (s.inject === 'real403' && s.who !== 'staff') out.push(`${n} 진짜 403 은 직원 창만`);
    // 고침1(§8-5 #2 · #3) — 알림 칸 · 새 알림 · 정상 짝 꼴
    if (!NOTICES.includes(s.notice)) out.push(`${n} 알림 칸 꼴(once · quiet 만)`);
    if (s.notice === 'once' && s.snack === null) out.push(`${n} 연 화면 장면인데 알림 글 없음`);
    if (s.notice === 'quiet' && s.snack === NOTICE_TEXT) out.push(`${n} 조용한 장면에 쉬운 알림 글`);
    if (!(s.newSnack === undefined || s.newSnack === true)) out.push(`${n} newSnack 꼴`);
    if (s.newSnack === true && !(s.snack === NOTICE_TEXT && s.notice === 'once')) out.push(`${n} 새 알림은 쉬운 알림 글 · once 만`);
    if (!(s.open === undefined || (typeof s.open === 'string' && s.open.trim().length > 0))) out.push(`${n} 열릴 단추 꼴`);
    if (s.open !== undefined && s.block !== undefined) out.push(`${n} 막힘 · 열림 칸 같이`);
    const isNone = s.inject === 'none';
    if (isNone && !(typeof s.open === 'string' && s.fixedOnly === true && s.notice === 'quiet' && s.snack === null)) out.push(`${n} 정상 짝은 열릴 단추 · 고친 뒤에만 · quiet · 알림 글 없음`);
    if (!isNone && s.open !== undefined) out.push(`${n} 열릴 단추는 정상 짝(가로채기 없음)만`);
  });
  return out;
}
// 콘솔 한 줄 셈(원문은 넘겨받기만 · 저장 0). cur = { err: {표지: 수}, warn: {표지: 수}, appErr }
function newTally(tags) {
  const err = {};
  const warn = {};
  for (const t of tags) { err[t] = 0; warn[t] = 0; }
  return { err, warn, appErr: 0 };
}
function tallyLine(cur, type, text, tags) {
  const t = String(text ?? '');
  if (type === 'error' && t.trimStart().startsWith('[')) cur.appErr += 1; // 앱 표준오류 꼴(브라우저 자원 오류 「Failed to load …」 는 셈 밖 · 선례 G10)
  for (const tag of tags) {
    if (!t.includes(tag)) continue;
    if (type === 'error') cur.err[tag] += 1;
    else if (type === 'warning') cur.warn[tag] += 1;
  }
  return cur;
}
const cnt = (m, k) => (m && Object.prototype.hasOwnProperty.call(m, k) && Number.isInteger(m[k]) ? m[k] : 0);
// 알림 글 맞춤 — 앞머리 고정(고침1 · [3-V] P3-3 「부분 일치」 → 남의 알림 「대시보드 정보를 …」 이 안 맞게)
function snackMatch(t, want) {
  return typeof t === 'string' && typeof want === 'string' && want.length > 0 && t.startsWith(want);
}
// 알림 셈(고침1 · [4] P1-2) — 원목록 그대로 셈(⬛ 옛: Set 으로 모아 같은 글 2번을 1로 셈) · 원목록 못 읽음(null) = 셋 다 null(판정 거짓)
//   snackHit = 그 장면 알림 글 수 · other = 그 밖 알림 수 · easy = 쉬운 알림 글 수
function countSnacks(scene, raw) {
  if (!Array.isArray(raw)) return { snackHit: null, other: null, easy: null };
  const mine = (t) => scene.snack !== null && snackMatch(t, scene.snack);
  return {
    snackHit: scene.snack === null ? 0 : raw.filter(mine).length,
    other: raw.filter((t) => !mine(t)).length,
    easy: raw.filter((t) => snackMatch(t, NOTICE_TEXT)).length,
  };
}
// ⬛ 옛 줄(고침1 전): // 장면 판정 — o = { routeHits, real403, band3, band10, err, warn, appErr, snackHit }
// 장면 판정 — o = { routeHits, real403, ok2xx, band3, band10, err, warn, appErr, snackHit, other, easy, blocked, opened }
function judgeScene(mode, scene, o) {
  const why = [];
  if (!scene || !o) return { pass: false, why: ['재료 없음'] };
  const real = scene.inject === 'real403';
  // 고침1(§8-5 #3) — 정상 짝(none) = 가로챔 0 이어야 · 그 주소 2xx 응답 1 이상(화면이 그 주소를 실제로 부르고 성공)
  const none = scene.inject === 'none';
  if (none) {
    if (o.routeHits !== 0) why.push(`정상 짝인데 가로챔 ${o.routeHits}`);
    if (!(Number.isInteger(o.ok2xx) && o.ok2xx >= 1)) why.push(`그 주소 2xx 응답 0(${o.ok2xx} · 화면이 안 부르거나 실패 · 장면 무효)`);
  } else {
    const hits = real ? o.real403 : o.routeHits;
    if (!(Number.isInteger(hits) && hits >= 1)) why.push(real ? '서버 403 응답 0(직원 역할 · 장면 무효)' : '가로챔 0(화면이 그 주소를 안 부름 · 장면 무효)');
  }
  // ⬛ 옛 줄(고침1 전): if (scene.snack !== null && !(Number.isInteger(o.snackHit) && o.snackHit >= 1)) why.push('원래 알림 글 0');
  //   고침1 — 새 알림(newSnack)은 대조군 판엔 없으므로 대조군에선 안 잼 · 고친 뒤 once 는 아래에서 「정확히 1」 로 잼
  if (scene.snack !== null && !(mode === 'control' && scene.newSnack === true) && !(Number.isInteger(o.snackHit) && o.snackHit >= 1)) why.push('원래 알림 글 0');
  // 넓힘X1 — 막혀야 할 단추가 있는 장면은 그 단추가 막혀 있어야(못 읽음 · 못 찾음 = 거짓)
  if (typeof scene.block === 'string' && o.blocked !== true) why.push(`단추 안 막힘(${scene.block} · ${o.blocked})`);
  // 고침1(§8-5 #3) — 열려 있어야 할 단추(정상 짝) — 못 읽음 · 못 찾음 = 거짓
  if (typeof scene.open === 'string' && o.opened !== true) why.push(`단추 안 열림(${scene.open} · ${o.opened})`);
  if (mode === 'control') {
    if (!(o.band3 === true || o.band10 === true)) why.push('흰 띠 0(대조군은 1 이어야 — 장면이 그 줄에 안 닿음)');
    for (const t of scene.tags) if (cnt(o.err, t) < 1) why.push(`표준오류 표지 0 ${t}`);
  } else if (mode === 'fixed') {
    if (o.band3 !== false || o.band10 !== false) why.push(`흰 띠 +3초 ${o.band3} · +10초 ${o.band10}(둘 다 거짓이어야)`);
    // ⬛ 옛 줄(고침1 전): for (const t of scene.tags) if (cnt(o.warn, t) < 1) why.push(`경고 흔적 0 ${t}`);
    //   고침1 — 정상 짝은 실패 길을 안 지나야(경고 흔적 0) · 그 밖은 그대로 1 이상
    if (none) { for (const t of scene.tags) if (cnt(o.warn, t) !== 0) why.push(`정상 짝인데 경고 흔적 ${cnt(o.warn, t)} ${t}`); }
    else { for (const t of scene.tags) if (cnt(o.warn, t) < 1) why.push(`경고 흔적 0 ${t}`); }
    if (o.appErr !== 0) why.push(`앱 표준오류 꼴 ${o.appErr}`);
    // 고침1(§8-5 #2 · 대표 ③ · ④) — 알림 칸: 연 화면 = 그 알림 정확히 1(쉬운 알림이 아니면 쉬운 알림 0 · 같은 실패에 두 번 아님) · 상주 · 짝 = 그 밖 알림 0
    if (scene.notice === 'once') {
      if (o.snackHit !== 1) why.push(`연 화면 알림 ${o.snackHit}(정확히 1 이어야)`);
      if (scene.snack !== NOTICE_TEXT && o.easy !== 0) why.push(`쉬운 알림 ${o.easy}(원래 알림과 겹침 · 0 이어야)`);
    } else if (scene.notice === 'quiet') {
      if (o.other !== 0) why.push(`조용해야 할 장면 알림 ${o.other}(0 이어야)`);
    } else {
      why.push('알림 칸 꼴 이상');
    }
  } else {
    why.push('모드 꼴 이상');
  }
  return { pass: why.length === 0, why };
}
// 끝 코드 — 장면 키마다 정확히 한 줄 · 시험 줄 · 통과 · 로그아웃 실패 0 일 때만 0
function endCode(rows, keys = SCENES.map((s) => s.key), logoutFail = 0) {
  const mine = (Array.isArray(rows) ? rows : []).filter((x) => x && x.scope === SCOPE);
  for (const k of keys) {
    const r = mine.filter((x) => x.path === k);
    if (r.length !== 1 || r[0].kind !== 'TEST' || r[0].pass !== true) return 1;
  }
  if (mine.some((x) => !keys.includes(x.path))) return 1;
  return logoutFail === 0 ? 0 : 1;
}
function bandResultPath() {
  return path.join(L.outDir(), 'main', 'console-error-band-result.json');
}
function saveBand(r) {
  const p = bandResultPath();
  fs.mkdirSync(path.dirname(p), { recursive: true });
  const tmp = `${p}.tmp`;
  fs.writeFileSync(tmp, JSON.stringify(r, null, 2));
  fs.renameSync(tmp, p);
}

// ── 뜬 적 있는 알림 글(문서마다 새로 · 글자만 · 50 상한 · 보기 실패는 셈으로) ──
const SNACK_INIT = `(() => {
  try {
    if (window.__bandSnack) return;
    const seen = [];
    const done = new WeakSet();
    window.__bandSnack = seen;
    window.__bandSnackErr = 0;
    const scan = () => {
      try {
        const els = document.querySelectorAll('.mud-snackbar');
        for (let i = 0; i < els.length; i += 1) {
          const el = els[i];
          if (done.has(el)) continue;
          const t = String(el.innerText || el.textContent || '').replace(/\\s+/g, ' ').trim();
          if (!t) continue;
          done.add(el);
          if (seen.length < 50) seen.push(t.slice(0, 300));
        }
      } catch (e) {
        window.__bandSnackErr += 1;
      }
    };
    new MutationObserver(scan).observe(document, { childList: true, subtree: true, characterData: true });
  } catch (e) {
    window.__bandSnackErr = (window.__bandSnackErr || 0) + 1;
  }
})();`;

// ── 기록 ──
function recScene(st, scene, { pass, got, kind = 'TEST', err = null }) {
  if (st.r.rows.some((x) => x.scope === SCOPE && x.path === scene.key)) return; // 한 키 한 줄(먼저 쓴 줄이 이긴다)
  L.rec(st.r, { id: 'BAND', g: scene.branch, path: scene.key, browser: BROWSER, what: `${scene.what} · 모드 ${st.mode}`, got, pass, kind, scope: SCOPE, err });
  saveBand(st.r);
}
function devScene(st, scene, step, e) {
  const why = e instanceof K.KitStepError ? L.sanitize(e.message, 200) : `예외 ${L.errorName(e)}`;
  console.log(`띠 장면 ${scene.key} 단언 전 실패 — 단계=${step} · ${why}`);
  recScene(st, scene, { pass: false, got: `단언 전 실패 — 단계=${step} · ${why}`, kind: 'DEVICE', err: e instanceof K.KitStepError ? null : e });
}

// ── 브라우저 한 벌(창 주인 하나 · 로그인 한 번 · 장면마다 새 탭 = 새 문서) ──
async function openWho(st, who) {
  const tally = { continue: 0, abort: 0 };
  const context = await st.br.newContext({ baseURL: K.BASE });
  await K.blockExternal(context, st.r, tally);
  const tok = { access: null, deviceId: null, masked: 0 };
  K.watchTokens(context, tok);
  await context.addInitScript(SNACK_INIT);
  const kb = { who, context, tok, tally, closed: false, login: { seen: 0, status: null, terms: null, err: null } };
  st.kbs.push(kb);
  return kb;
}
async function loginWho(kb, loginId, password) {
  const page = await kb.context.newPage();
  page.on('response', async (res) => {
    if (!/\/api\/auth\/login(\?|$)/i.test(res.url()) || res.request().method() !== 'POST') return;
    try {
      const j = await res.json();
      kb.login.status = res.status();
      kb.login.terms = !!(j && (j.requiresTermsConsent ?? j.RequiresTermsConsent));
      kb.login.seen += 1;
    } catch (e) {
      kb.login.err = L.errorName(e);
    }
  });
  try {
    await page.goto(`${K.BASE}/login`, { waitUntil: 'domcontentloaded', timeout: PAGE_MS });
    await page.getByLabel('아이디', { exact: true }).fill(loginId, { timeout: PAGE_MS });
    await page.getByLabel('비밀번호', { exact: true }).fill(password);
    await page.getByLabel('비밀번호', { exact: true }).press('Tab');
    await page.getByRole('button', { name: '로그인', exact: true }).click();
    const s = await DC.settleScreen(page, 90000);
    const end = Date.now() + 10000;
    while (kb.login.seen === 0 && Date.now() < end) await K.sleep(250);
    await K.waitMasked(kb.tok);
    if (kb.login.seen === 0 || kb.login.status !== 200 || kb.login.terms !== false || !kb.tok.access || s.where !== 'main') {
      throw new K.KitStepError(`못 만듦 — ${kb.who} 로그인 상태 이상(응답 ${kb.login.seen}회 · status=${kb.login.status} · 약관 필요=${kb.login.terms} · 출입증 ${kb.tok.access ? '있음' : '없음'} · 착지 ${s.where}@${L.sanitize(String(s.path), 60)})`);
    }
  } finally {
    await page.close().catch((e) => console.log(`띠 장면 로그인 탭 닫기 실패 ${L.errorName(e)}`));
  }
}
async function closeWho(st, kb) {
  if (kb.closed) return;
  kb.closed = true;
  if (kb.tok.access) {
    const lo = await K.apiLogout({ who: `BAND-${kb.who}`, token: kb.tok.access, deviceId: kb.tok.deviceId });
    console.log(`띠 장면 ${kb.who} 로그아웃 ${lo.got}`);
    if (!lo.ok) st.logoutFail += 1;
  }
  await kb.context.close().catch((e) => console.log(`띠 장면 컨텍스트 닫기 실패 ${L.errorName(e)}`));
}

async function look(page) {
  return K.withTimeout(page.evaluate(() => {
    const b = document.getElementById('blazor-error-ui');
    const band = !!b && getComputedStyle(b).display !== 'none';
    const snacks = Array.isArray(window.__bandSnack) ? window.__bandSnack.slice(0, 50) : null;
    return { band, snacks, snackErr: Number(window.__bandSnackErr || 0) };
  }), K.EVAL_MS, '화면 보기').catch((e) => {
    console.log(`띠 장면 화면 보기 실패 ${e instanceof K.KitStepError ? L.sanitize(e.message, 80) : L.errorName(e)}`);
    return { band: null, snacks: null, snackErr: null };
  });
}

// 장면 하나 — 새 탭 · 가로채기 걸기 · 화면 열기 · 착지 +3초 · +10초 보기 · 판정 · 탭 닫기
async function runScene(st, kb, scene) {
  const step = { now: '새 탭' };
  let page = null;
  try {
    page = await kb.context.newPage();
    const cur = newTally(scene.tags);
    // ⬛ 옛 줄(고침1 전): const hit = { route: 0, real403: 0, statuses: [] };
    const hit = { route: 0, real403: 0, ok2xx: 0, statuses: [] };
    page.on('console', (msg) => tallyLine(cur, msg.type(), msg.text(), scene.tags));
    page.on('response', (res) => {
      if (!routeMatch(scene, res.url())) return;
      if (res.status() === 403) hit.real403 += 1;
      if (res.status() >= 200 && res.status() < 300) hit.ok2xx += 1; // 고침1 — 정상 짝(none)용
      if (hit.statuses.length < 6) hit.statuses.push(res.status());
    });
    const inj = INJECTS[scene.inject];
    // ⬛ 옛 줄(고침1 전): if (!inj.real) {
    if (!inj.real && !inj.none) {
      step.now = '가로채기 걸기';
      await page.route((u) => routeMatch(scene, u), (route) => {
        hit.route += 1;
        // 탭을 닫을 때 날던 요청의 처리 거절은 이름만 찍고 받는다(받는 곳 없는 거절이 프로세스 밖 예외로 번지지 않게 · 빈 catch 0)
        const done = inj.abort ? route.abort(inj.abort) : route.fulfill({ status: inj.status, contentType: 'application/json', body: '{}' });
        return done.catch((e) => console.log(`띠 장면 ${scene.key} 가로채기 처리 거절 ${L.errorName(e)}`));
      });
    }
    step.now = '화면 열기';
    await page.goto(`${K.BASE}${scene.page}`, { waitUntil: 'domcontentloaded', timeout: PAGE_MS });
    const s = await DC.settleScreen(page, PAGE_MS);
    const landAt = Date.now();
    step.now = '브리핑 덮개 · 착지 보기';
    await DC.closeBriefing(page, { seen: 0, closed: 0, errs: 0, lastErr: null }, 3000);
    await K.sleep(Math.max(0, landAt + 3000 - Date.now()));
    const a = await look(page);
    await K.sleep(Math.max(0, landAt + 10000 - Date.now()));
    const b = await look(page);
    // 넓힘X1 — 막혀야 할 단추(있는 장면만 · +10초 뒤 · 못 찾음 · 못 읽음 = null → 판정 거짓)
    let blocked = null;
    if (typeof scene.block === 'string') {
      step.now = '단추 막힘 보기';
      blocked = await K.withTimeout(page.getByRole('button', { name: scene.block, exact: true }).first().isDisabled({ timeout: 5000 }), K.EVAL_MS, '단추 막힘 보기')
        .catch((e) => {
          console.log(`띠 장면 ${scene.key} 단추 막힘 보기 실패 ${e instanceof K.KitStepError ? L.sanitize(e.message, 80) : L.errorName(e)}`);
          return null;
        });
    }
    // 고침1(§8-5 #3) — 열려 있어야 할 단추(정상 짝만 · +10초 뒤 · 못 찾음 · 못 읽음 = null → 판정 거짓)
    let opened = null;
    if (typeof scene.open === 'string') {
      step.now = '단추 열림 보기';
      opened = await K.withTimeout(page.getByRole('button', { name: scene.open, exact: true }).first().isEnabled({ timeout: 5000 }), K.EVAL_MS, '단추 열림 보기')
        .catch((e) => {
          console.log(`띠 장면 ${scene.key} 단추 열림 보기 실패 ${e instanceof K.KitStepError ? L.sanitize(e.message, 80) : L.errorName(e)}`);
          return null;
        });
    }
    // ⬛ 옛 줄(고침1 전 · Set 으로 같은 글 2번을 1로 셈 · 부분 일치 = [4] P1-2 · [3-V] P3-3):
    //   const snacks = [...new Set([...(a.snacks || []), ...(b.snacks || [])])];
    //   const snackHit = b.snacks === null && a.snacks === null ? null : (scene.snack === null ? 0 : snacks.filter((t) => t.includes(scene.snack)).length);
    //   const other = snacks.filter((t) => scene.snack === null || !t.includes(scene.snack));
    // 고침1 — 원목록 그대로: 창의 기록(window.__bandSnack)은 문서 안에서 쌓이기만 하므로 +10초 목록이 +3초 목록을 품는다 → +10초 것(못 읽으면 +3초 것)
    const raw = Array.isArray(b.snacks) ? b.snacks : (Array.isArray(a.snacks) ? a.snacks : null);
    const sc = countSnacks(scene, raw);
    const snackHit = sc.snackHit;
    const other = (raw || []).filter((t) => !(scene.snack !== null && snackMatch(t, scene.snack)));
    // ⬛ 옛 줄(넓힘X1 전): const o = { routeHits: hit.route, real403: hit.real403, band3: a.band, band10: b.band, err: cur.err, warn: cur.warn, appErr: cur.appErr, snackHit };
    // ⬛ 옛 줄(고침1 전): const o = { routeHits: hit.route, real403: hit.real403, band3: a.band, band10: b.band, err: cur.err, warn: cur.warn, appErr: cur.appErr, snackHit, blocked };
    const o = { routeHits: hit.route, real403: hit.real403, ok2xx: hit.ok2xx, band3: a.band, band10: b.band, err: cur.err, warn: cur.warn, appErr: cur.appErr,
      snackHit, other: sc.other, easy: sc.easy, blocked, opened };
    const v = judgeScene(st.mode, scene, o);
    const showMap = (m) => scene.tags.map((t) => cnt(m, t)).join('/');
    // 기록만(판정 아님) — 다른 알림 글 수 · 첫 셋(소독) · 착지 · 응답 숫자
    console.log(`띠 장면 ${scene.key} 판정=${v.pass} · 모드 ${st.mode} · 착지 ${s.where}@${L.sanitize(String(s.path), 60)} · 가로챔 ${hit.route} · 서버403 ${hit.real403}`
      + ` · 응답 ${hit.statuses.join(',') || '없음'} · 흰 띠 +3초 ${a.band} +10초 ${b.band} · 표준오류 표지 ${showMap(cur.err)} · 경고 표지 ${showMap(cur.warn)}`
      + ` · 앱 표준오류 ${cur.appErr} · 원래 알림 ${snackHit} · 다른 알림 ${other.length} ${other.slice(0, 3).map((t) => K.consoleLine(t, 50)).join(' | ') || '-'}`
      + `${typeof scene.block === 'string' ? ` · 단추 막힘 ${blocked}` : ''}`
      + `${typeof scene.open === 'string' ? ` · 단추 열림 ${opened} · 2xx ${hit.ok2xx}` : ''} · 쉬운 알림 ${sc.easy} · 알림 칸 ${scene.notice}`
      + `${v.pass ? '' : ` · 이유 ${v.why.join(' · ')}`}`);
    // ⬛ 옛 줄(넓힘X1 전): const got = { m: st.mode, hit: …, sn: snackHit, other: other.length, at: `${s.where}@${s.path}` };
    const got = { m: st.mode, hit: scene.inject === 'real403' ? hit.real403 : hit.route, b3: a.band, b10: b.band, e: showMap(cur.err), w: showMap(cur.warn), app: cur.appErr, sn: snackHit, other: other.length, at: `${s.where}@${s.path}`,
      ...(typeof scene.block === 'string' ? { blk: blocked } : {}),
      ...(typeof scene.open === 'string' ? { opn: opened, ok: hit.ok2xx } : {}), easy: sc.easy }; // 고침1 — 열림 · 2xx · 쉬운 알림 수
    recScene(st, scene, { pass: v.pass, got: v.pass ? got : { ...got, why: v.why } });
  } catch (e) {
    devScene(st, scene, step.now, e);
  } finally {
    if (page) {
      await page.unrouteAll({ behavior: 'ignoreErrors' }).catch((e) => console.log(`띠 장면 가로채기 풀기 실패 ${L.errorName(e)}`));
      await page.close().catch((e) => console.log(`띠 장면 탭 닫기 실패 ${L.errorName(e)}`));
    }
  }
}

async function runWho(st, who, loginId, password) {
  // ⬛ 옛 줄(넓힘X1 전): const list = SCENES.filter((s) => s.who === who);
  const list = st.scenes.filter((s) => s.who === who);
  if (list.length === 0) return;
  let kb = null;
  try {
    if (!loginId || !password) throw new K.KitStepError(`못 만듦 — ${who} 로그인 환경 칸 비어 있음`);
    kb = await openWho(st, who);
    await loginWho(kb, loginId, password);
  } catch (e) {
    for (const s of list) devScene(st, s, `${who} 로그인`, e);
    if (kb) await closeWho(st, kb);
    return;
  }
  try {
    for (const s of list) await runScene(st, kb, s);
  } finally {
    await closeWho(st, kb);
  }
}

async function cleanup(st) {
  if (st.cleaned) return;
  st.cleaned = true;
  for (const kb of st.kbs) await closeWho(st, kb);
  if (st.br) await st.br.close().catch((e) => console.log(`띠 장면 브라우저 닫기 실패 ${L.errorName(e)}`));
}
function fillLeft(st, why) {
  // ⬛ 옛 줄(넓힘X1 전): for (const s of SCENES) recScene(st, s, { pass: false, got: why, kind: 'DEVICE' });
  for (const s of st.scenes) recScene(st, s, { pass: false, got: why, kind: 'DEVICE' });
}

async function main() {
  const mode = modeOf(env.UI_BAND_MODE);
  // ⬛ 옛 줄(넓힘X1 전): const st = { mode, r: { … }, br: null, kbs: [], logoutFail: 0, cleaned: false };
  const st = { mode, scenes: scenesFor(mode), r: { schema: 'console-error-band/1', mode, external_blocked: 0, external_hosts: [], rows: [] }, br: null, kbs: [], logoutFail: 0, cleaned: false };
  if (!mode) {
    console.log(`⚫ FAIL [장치] 띠 장면 — 모드 칸 UI_BAND_MODE 꼴 이상(${MODES.join(' · ')} 만) · 아무것도 안 돌린다`);
    process.exit(1);
  }
  const bad = sceneListProblems();
  if (bad.length > 0) {
    console.log(`⚫ FAIL [장치] 띠 장면 목록 꼴 이상 — ${bad.join(' · ')} · 아무것도 안 돌린다`);
    process.exit(1);
  }
  saveBand(st.r);
  // ⬛ 옛 줄(넓힘X1 전): console.log(`띠 장면 시작: 모드 ${mode} · 장면 ${SCENES.length}(${SCENES.map((s) => s.key).join(' ')}) · …`);
  console.log(`띠 장면 시작: 모드 ${mode} · 장면 ${st.scenes.length}(${st.scenes.map((s) => s.key).join(' ')}) · 결과표 따로(판정기 judge.js 무접촉)`);
  let fatalOnce = false;
  const fatal = (where) => (e) => {
    if (fatalOnce) return;
    fatalOnce = true;
    try {
      console.log(`띠 장면 프로세스 밖 예외(${where}) ${L.errorName(e)}`);
      fillLeft(st, `단언 전 실패 — 프로세스 밖 예외(${where})`);
    } catch (e2) {
      console.log(`⚫ 띠 장면 끊긴 자리 기록 실패 (${L.errorName(e2)})`);
    }
    setTimeout(() => process.exit(1), 10000).unref(); // 세션 놓기 — 10초 상한 뒤 무조건 종료
    cleanup(st).then(() => process.exit(1), () => process.exit(1));
  };
  process.on('unhandledRejection', fatal('unhandledRejection'));
  process.on('uncaughtException', fatal('uncaughtException'));
  for (const sig of ['SIGTERM', 'SIGINT']) process.on(sig, () => fatal(`신호 ${sig}`)(new K.KitStepError(`신호 ${sig}`)));
  setTimeout(() => fatal('띠 장면 전체 시간 상한')(new K.KitStepError(`띠 장면 전체 시간 상한 ${BAND_TOTAL_MS / 60000}분`)), BAND_TOTAL_MS).unref();

  let pre = null;
  try {
    const pw = require('playwright');
    st.br = await pw[BROWSER].launch();
  } catch (e) {
    pre = `브라우저 기동 실패 ${L.errorName(e)}`;
  }
  if (pre) {
    console.log(`띠 장면 시작 전 실패 — ${pre}`);
    fillLeft(st, `단언 전 실패 — ${pre}`);
  } else {
    await runWho(st, 'owner', env.UI_LOGIN_PARENT || 'ci-parent', env.UI_PW_PARENT || '');
    await runWho(st, 'staff', env.UI_LOGIN_NA || 'ci-na', env.UI_PW_NA || '');
  }
  fillLeft(st, '장면 끝 뒤 남은 키(있으면 장치 오류)');
  await cleanup(st);
  const mine = st.r.rows.filter((x) => x.scope === SCOPE);
  // ⬛ 옛 줄(넓힘X1 전): const code = endCode(st.r.rows, SCENES.map((s) => s.key), st.logoutFail); · 줄 ${mine.length}/${SCENES.length}
  const code = endCode(st.r.rows, st.scenes.map((s) => s.key), st.logoutFail);
  console.log(`띠 장면 끝: 모드 ${mode} · 줄 ${mine.length}/${st.scenes.length} · 통과 ${mine.filter((x) => x.pass === true).length} · 거짓 ${mine.filter((x) => x.pass !== true).length}`
    + ` · 로그아웃 실패 ${st.logoutFail} · 외부 차단 ${st.r.external_blocked} · 끝 코드 ${code}`);
  saveBand(st.r);
  process.exit(code);
}

if (require.main === module) main();
module.exports = {
  SCENES, MODES, SCOPE, INJECTS, CLOSED_PATHS, NO_INTERCEPT, FORBID_TEXT,
  modeOf, routeMatch, sceneListProblems, newTally, tallyLine, judgeScene, endCode, bandResultPath,
  scenesFor, // 넓힘X1
  NOTICE_TEXT, NOTICES, snackMatch, countSnacks, // 고침1
};
