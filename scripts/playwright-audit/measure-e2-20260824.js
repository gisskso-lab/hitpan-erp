// 20260824 실측 — ② 결재관리 필터 (콤보박스 2개)
//
//   대상: T-004 (test1234.hitpan.kr) · 🔴 터널로 잰다
//   작업지시서: docs/운영기록/20260824작1_결재관리_필터일원화_작업지시서.md
//
//   판정 규율 (8/23 교훈):
//     🔴 HTTP 코드로 판정하지 않는다 — SPA 라 없는 주소도 200 이다. 본문으로 가른다.
//     🔴 인증 없이 재면 전부 401 이라 판정 근거가 안 된다. 로그인 후 잰다.
//     🔴 반환값으로 판정하지 않는다 — 함수가 정하는 값은 게이트가 아니다.
//     🔴 초록불이 어디서 오는지 밝힌다. 문자열 존재로 판정하지 않는다.

const https = require('https');
const fs = require('fs');

const BASE = process.env.HITPAN_BASE || 'https://test1234.hitpan.kr';
const EMAIL = process.env.HITPAN_EMAIL;
const PASS = process.env.HITPAN_PASS;
const WANT_VER = process.env.HITPAN_VER || '1.3.4';
if (!EMAIL || !PASS) { console.error('🔴 HITPAN_EMAIL / HITPAN_PASS 필요'); process.exit(1); }

const R = [];
const rec = (id, what, got, pass, note) => {
    R.push({ id, what, got, pass, note: note || '' });
    const tag = pass === true ? '🟢 PASS' : pass === false ? '🔴 FAIL' : '⚪ INFO';
    console.log(`${tag}  ${id}  ${what}`);
    console.log(`         → ${got}`);
    if (note) console.log(`         · ${note}`);
};

function api(path, { method = 'GET', token, deviceId, body } = {}) {
    return new Promise((resolve) => {
        const url = new URL(BASE + path);
        const data = body ? JSON.stringify(body) : null;
        const headers = {};
        if (data) { headers['Content-Type'] = 'application/json'; headers['Content-Length'] = Buffer.byteLength(data); }
        if (token) headers['Authorization'] = 'Bearer ' + token;
        if (deviceId) headers['X-HitPan-Device-Id'] = deviceId;
        const req = https.request({
            hostname: url.hostname, port: 443, path: url.pathname + url.search,
            method, headers, timeout: 20000
        }, res => {
            let d = ''; res.on('data', c => d += c);
            res.on('end', () => { let j = null; try { j = JSON.parse(d); } catch { } resolve({ status: res.statusCode, json: j, raw: d }); });
        });
        req.on('timeout', () => { req.destroy(); resolve({ status: 0, json: null, raw: 'TIMEOUT' }); });
        req.on('error', e => resolve({ status: 0, json: null, raw: 'ERR ' + e.message }));
        if (data) req.write(data);
        req.end();
    });
}

const ERP = ['quotation', 'sales_order', 'delivery', 'purchase_order', 'receipt', 'sales_return', 'purchase_return'];

// 필터2 에 있어야 할 10종 (① 이 확정한 목록)
const GW10 = [
    'expense', 'leave', 'absence', 'overtime',
    'report_daily', 'report_weekly', 'report_monthly', 'report_incident',
    'resignation', 'labor_contract'
];

// 필터1 5개 — 사장님 결재 2026-08-24
const SCOPES = ['pending', 'completed', 'rejected', 'sent', 'all'];

(async () => {
    console.log('='.repeat(78));
    console.log('실측 — ② 결재관리 필터 (20260824작1)');
    console.log(`대상: ${BASE}`);
    console.log('='.repeat(78));

    // ── [0] 환경 증명 ──
    console.log('\n### [0] 환경 증명\n');

    const health = await api('/health');
    const ver = health.json?.checks?.version;
    rec('E-1', 'API · 버전', `HTTP ${health.status} · v${ver}`, health.status === 200 && ver === WANT_VER,
        `🔴 ${WANT_VER} 가 아니면 옛 코드를 재는 것 — 판정 무효`);
    if (ver !== WANT_VER) { console.log(`\n🔴 배포본이 ${WANT_VER} 가 아니다 — 중단한다.`); process.exit(1); }

    const login = await api('/api/auth/login', { method: 'POST', body: { email: EMAIL, password: PASS } });
    if (login.status !== 200) { rec('E-2', '로그인', `HTTP ${login.status}`, false); process.exit(1); }
    const TOKEN = login.json.accessToken;
    rec('E-2', '로그인', 'HTTP 200', true);

    const devs = await api('/api/devices', { token: TOKEN });
    const DEV = (devs.json || []).find(d => d.isMainPc)?.deviceId;
    rec('E-3', '메인PC 기기줄', DEV ? '있음' : '없음', !!DEV);

    const emps = await api('/api/employees', { token: TOKEN, deviceId: DEV });
    const n = (emps.json || []).length;
    rec('E-4', '사원 수 (운영 아님 확인)', `${n}명`, n >= 1 && n <= 5);
    if (n > 5) { console.log('\n🔴 운영 의심 — 중단(#39).'); process.exit(1); }

    const H = { token: TOKEN, deviceId: DEV };

    // ── [V-1] 필터2 목록 ──
    console.log('\n### [V-1] 필터2 — 문서종류 목록\n');

    const dt = await api('/api/approval/doc-types', H);
    rec('V-1a', '필터2 목록 조회', `HTTP ${dt.status} · ${Array.isArray(dt.json) ? dt.json.length + '종' : '?'}`,
        dt.status === 200 && Array.isArray(dt.json),
        '🔴 이 주소가 없으면 화면 콤보가 빈다');
    if (!Array.isArray(dt.json)) { save(); process.exit(1); }

    const codes = dt.json.map(x => x.docType);
    const missing = GW10.filter(t => !codes.includes(t));
    rec('V-1b', '그룹웨어 10종이 다 있나',
        missing.length ? `🔴 빠짐: ${missing.join(', ')}` : `10종 전부 있음 (${codes.length}종)`,
        missing.length === 0);

    // 🔴 사장님 결재: 보고서 4종을 "펼친다". 묶여 있으면 FAIL.
    const reports = codes.filter(c => c.startsWith('report_'));
    rec('V-1c', '🔴 보고서 4종이 펼쳐졌나 (묶기 금지)',
        `${reports.length}종 — ${reports.join(', ')}`,
        reports.length === 4,
        'PM 권고("묶기")는 반려됐다. 일일·주간·월간·경위서가 따로 보여야 한다');

    // 라벨이 영문 코드로 새는지 — 고객 노출 개발용어 금지
    const rawLabel = dt.json.filter(x => !x.docTypeName || x.docTypeName === x.docType);
    rec('V-1d', '한글 라벨이 다 붙었나',
        rawLabel.length ? `🔴 영문 노출: ${rawLabel.map(x => x.docType).join(', ')}` : '전부 한글',
        rawLabel.length === 0);

    // ── [V-2] ERP 7종 제외 ──
    console.log('\n### [V-2] ERP 7종이 필터2에서 빠졌나\n');

    const erpLeft = ERP.filter(t => codes.includes(t));
    rec('V-2', '🔴 ERP 7종 제외',
        erpLeft.length ? `🔴 아직 있다: ${erpLeft.join(', ')}` : '7종 전부 빠짐',
        erpLeft.length === 0,
        '① 이 뺀 것이 필터2 에서 되살아나면 안 된다');

    // ── [V-1e] 필터1 5개가 다 도나 ──
    console.log('\n### [V-1e] 필터1 — 5개 구분이 다 도나\n');

    const byScope = {};
    for (const s of SCOPES) {
        const r = await api(`/api/approval/documents?scope=${s}`, H);
        byScope[s] = r;
        rec(`V-1e-${s}`, `필터1 「${s}」`,
            `HTTP ${r.status} · ${Array.isArray(r.json) ? r.json.length + '건' : r.raw.slice(0, 60)}`,
            r.status === 200 && Array.isArray(r.json));
    }

    // 🔴 알 수 없는 필터는 400 이어야 한다. 200 이면 조건 없이 다 내주는 것일 수 있다.
    const bogus = await api('/api/approval/documents?scope=everything', H);
    rec('V-1f', '🔴 알 수 없는 필터는 막히나',
        `HTTP ${bogus.status}`,
        bogus.status === 400,
        '200 이면 조건 없이 목록이 새는 길이 열린 것이다');

    // ── [V-3] 반려가 완료에 안 섞이나 ──
    console.log('\n### [V-3] 🔴 반려 분리 — 완료함에 반려·진행중이 섞이나\n');

    const completed = byScope['completed'].json || [];
    const badInCompleted = completed.filter(d => d.status !== 'approved');
    rec('V-3a', '🔴 완료함에 approved 만 있나',
        badInCompleted.length
            ? `🔴 섞임: ${badInCompleted.map(d => `${d.title}(${d.status})`).join(', ')}`
            : `${completed.length}건 전부 approved`,
        badInCompleted.length === 0,
        '종전 GetCompleted 는 ad.status 를 안 봐서 반려·진행중이 완료함에 떴다');

    const rejected = byScope['rejected'].json || [];
    const badInRejected = rejected.filter(d => d.status !== 'rejected');
    rec('V-3b', '🔴 반려함에 rejected 만 있나',
        badInRejected.length
            ? `🔴 섞임: ${badInRejected.map(d => `${d.title}(${d.status})`).join(', ')}`
            : `${rejected.length}건 전부 rejected`,
        badInRejected.length === 0);

    // 🔴 옛 주소(/api/approval/completed)와 대조한다 — 이게 봉합의 증거다.
    const oldCompleted = await api('/api/approval/completed', H);
    const oldList = Array.isArray(oldCompleted.json) ? oldCompleted.json : [];
    const oldBad = oldList.filter(d => d.status !== 'approved');
    rec('V-3c', '⚪ 대조 — 옛 완료함(살아있는 주소)',
        `${oldList.length}건 중 approved 아닌 것 ${oldBad.length}건`,
        null,
        oldBad.length
            ? '🔴 옛 주소에는 여전히 섞인다 = 새 필터가 실제로 거른 것이 맞다'
            : '데이터에 반려·진행중이 없어 대조가 안 된다 — 아래 [보강] 참조');

    // ── [V-5] 필터 교차 ──
    console.log('\n### [V-5] 필터1 × 필터2 교차\n');

    const allDocs = byScope['all'].json || [];
    if (allDocs.length === 0) {
        rec('V-5', '필터 교차', '⚪ 결재 데이터 0건 — 교차를 못 잰다', null,
            '🔴 이건 PASS 가 아니다. 데이터를 넣고 다시 재야 한다');
    } else {
        const pick = allDocs[0].docType;
        const crossed = await api(`/api/approval/documents?scope=all&docType=${pick}`, H);
        const cl = crossed.json || [];
        const wrong = cl.filter(d => d.docType !== pick);
        rec('V-5', `필터2=「${pick}」 로 좁히면 그것만 나오나`,
            wrong.length ? `🔴 다른 종류 ${wrong.length}건 섞임` : `${cl.length}건 전부 ${pick}`,
            wrong.length === 0 && cl.length <= allDocs.length);
    }

    // 🔴 없는 문서종류로 조회하면 빈 목록이어야 한다 (조건이 통째로 빠지면 안 된다)
    const bogusType = await api('/api/approval/documents?scope=all&docType=nonexistent_kind', H);
    const btl = Array.isArray(bogusType.json) ? bogusType.json.length : -1;
    rec('V-5b', '🔴 없는 문서종류는 빈 목록인가',
        `HTTP ${bogusType.status} · ${btl}건`,
        bogusType.status === 200 && btl === 0,
        `전체(${allDocs.length}건)가 나오면 docType 조건이 통째로 빠진 것이다`);

    // ── [V-2b] ERP 문서가 목록에 새는지 ──
    const erpInList = allDocs.filter(d => ERP.includes(d.docType));
    rec('V-2b', '🔴 목록에 ERP 문서가 새나',
        erpInList.length ? `🔴 ${erpInList.length}건 샘: ${erpInList.map(d => d.docType).join(', ')}` : '없음',
        erpInList.length === 0);

    // ── [V-6] 옛 주소 3개 ──
    console.log('\n### [V-6] 옛 결재 주소가 살아 있나\n');

    // 🔴 SPA 라 HTTP 200 으로는 못 잰다. API 3개가 사는지로 잰다(배지가 그걸 쓴다).
    for (const [p, label] of [['pending', '대기'], ['sent', '내가보낸'], ['completed', '완료']]) {
        const r = await api(`/api/approval/${p}`, H);
        rec(`V-7-${p}`, `옛 API 「${label}」 생존 (배지·스크립트가 쓴다)`,
            `HTTP ${r.status} · ${Array.isArray(r.json) ? r.json.length + '건' : '?'}`,
            r.status === 200 && Array.isArray(r.json),
            '🔴 지우면 사이드바 대기 건수 배지가 죽는다');
    }

    // ── [V-7] 배지 숫자 대조 ──
    console.log('\n### [V-7] 배지 숫자가 종전과 같나\n');

    const oldPending = await api('/api/approval/pending', H);
    const oldN = Array.isArray(oldPending.json) ? oldPending.json.length : -1;
    const newN = Array.isArray(byScope['pending'].json) ? byScope['pending'].json.length : -2;
    rec('V-7', '🔴 옛 pending 과 새 scope=pending 이 같나',
        `옛 ${oldN}건 · 새 ${newN}건`,
        oldN === newN,
        '다르면 둘 중 하나가 틀린 것이다 — 배지와 목록이 어긋난다');

    // ── [V-8] 격리 ──
    console.log('\n### [V-8] 남의 결재가 보이나\n');

    const me = login.json?.user?.employeeId || login.json?.employeeId;
    if (!me) {
        rec('V-8', '격리 판정', '⚪ 로그인 응답에 employeeId 가 없어 못 가른다', null,
            '🔴 이건 PASS 가 아니다. 계정 2개로 재는 것이 정본이다');
    } else {
        rec('V-8', '⚪ 내 employeeId', me, null,
            '대표 계정이면 회사 전체가 보이는 게 맞다(V-9). 격리는 일반 직원 계정으로 재야 한다');
    }

    save();
    const fail = R.filter(x => x.pass === false).length;
    const pass = R.filter(x => x.pass === true).length;
    const info = R.filter(x => x.pass === null).length;
    console.log('\n' + '='.repeat(78));
    console.log(`결과 — 🟢 ${pass} PASS · 🔴 ${fail} FAIL · ⚪ ${info} INFO`);
    console.log('='.repeat(78));
    if (info) console.log('⚠️ INFO 는 통과가 아니다. 못 잰 것이다 — 무엇을 못 쟀는지 보고에 남긴다.');
    process.exit(fail ? 1 : 0);
})();

function save() {
    const out = process.env.OUT || 'tests/scenarios/reports/measure-e2-20260824.json';
    try { fs.writeFileSync(out, JSON.stringify(R, null, 2)); console.log(`\n📄 ${out}`); }
    catch (e) { console.log(`\n⚠️ 저장 실패: ${e.message}`); }
}
