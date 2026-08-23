// 20260823 실측 — ① 결재 창구 일원화 (ERP 빼고 · 그룹웨어 채우기)
//
//   대상: T-004 (test1234.hitpan.kr) · 🔴 터널로 잰다
//   판정 규율: 초록불이 어디서 오는지 밝힌다. 문자열 존재로 판정하지 않는다.
//   🔴 HTTP 코드로 라우트 생존을 못 잰다 — SPA 라 없는 주소도 200 이다. 본문으로 가른다.

const https = require('https');
const fs = require('fs');

const BASE = process.env.HITPAN_BASE || 'https://test1234.hitpan.kr';
const EMAIL = process.env.HITPAN_EMAIL;
const PASS = process.env.HITPAN_PASS;
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
const GW  = ['expense', 'leave', 'absence', 'overtime', 'report_daily', 'report_weekly', 'report_monthly', 'report_incident'];
const NEW = ['resignation', 'labor_contract'];

(async () => {
    console.log('='.repeat(78));
    console.log('실측 — ① 결재 창구 일원화 (20260823작1)');
    console.log(`대상: ${BASE}`);
    console.log('='.repeat(78));

    console.log('\n### [0] 환경 증명\n');
    const health = await api('/health');
    const ver = health.json?.checks?.version;
    rec('E-1', 'API · 버전', `HTTP ${health.status} · v${ver}`, health.status === 200 && ver === '1.3.3',
        '🔴 1.3.3 이 아니면 옛 코드를 재는 것 — 판정 무효');
    if (ver !== '1.3.3') { console.log('\n🔴 배포본이 1.3.3 이 아니다 — 중단한다.'); process.exit(1); }

    const login = await api('/api/auth/login', { method: 'POST', body: { email: EMAIL, password: PASS } });
    if (login.status !== 200) { rec('E-2', '로그인', `HTTP ${login.status}`, false); process.exit(1); }
    const TOKEN = login.json.accessToken;
    rec('E-2', '로그인', 'HTTP 200', true);

    const devs = await api('/api/devices', { token: TOKEN });
    const DEV = (devs.json || []).find(d => d.isMainPc)?.deviceId;
    rec('E-3', '메인PC 기기줄', DEV ? '있음' : '없음', !!DEV);

    const emps = await api('/api/employees', { token: TOKEN, deviceId: DEV });
    const n = (emps.json || []).length;
    rec('E-4', `사원 수 (운영 아님 확인)`, `${n}명`, n >= 1 && n <= 5);
    if (n > 5) { console.log('\n🔴 운영 의심 — 중단(#39).'); process.exit(1); }

    // ── [1] 결재 설정 목록 ──
    console.log('\n### [1] 결재 설정 목록 — ERP 는 빠지고 그룹웨어는 남았나\n');

    const st = await api('/api/approval/settings', { token: TOKEN, deviceId: DEV });
    rec('E1-0', '결재 설정 조회', `HTTP ${st.status} · ${Array.isArray(st.json) ? st.json.length + '종' : '?'}`,
        st.status === 200 && Array.isArray(st.json));
    if (!Array.isArray(st.json)) { fs.writeFileSync(process.env.OUT || 'e1.json', JSON.stringify(R, null, 2)); process.exit(1); }

    const types = st.json.map(x => x.docType);
    const labels = Object.fromEntries(st.json.map(x => [x.docType, x.docTypeLabel]));

    const erpLeft = ERP.filter(t => types.includes(t));
    rec('E1-1', '🔴 ERP 7종이 목록에서 빠졌나',
        erpLeft.length ? `🔴 아직 있다: ${erpLeft.join(', ')}` : '7종 전부 빠짐',
        erpLeft.length === 0,
        '남아 있으면 거래명세서가 결재함을 덮는다');

    const gwMissing = GW.filter(t => !types.includes(t));
    rec('E1-2', '그룹웨어 종류는 그대로 있나',
        gwMissing.length ? `🔴 사라짐: ${gwMissing.join(', ')}` : `${GW.length}종 전부 있음`,
        gwMissing.length === 0,
        '🔴 expense 가 사라지면 경비 결재가 죽는다 (8/21 P0 자리)');

    const newMissing = NEW.filter(t => !types.includes(t));
    rec('E1-8', '🔴 채운 2종이 화면에 뜨나',
        newMissing.length ? `🔴 안 뜬다: ${newMissing.join(', ')}`
            : NEW.map(t => `${t}=${labels[t]}`).join(' · '),
        newMissing.length === 0,
        '안 뜨면 8/21 휴직 P0 재발 — 켤 방법이 없어 조용히 죽는다');

    const engLabel = NEW.filter(t => labels[t] === t);
    rec('E1-8b', '채운 2종의 라벨이 한글인가',
        engLabel.length ? `🔴 영문 코드: ${engLabel.join(', ')}` : '한글 라벨 정상',
        engLabel.length === 0, '영문이면 고객에게 개발용어가 노출된다');

    // ── [2] 결재선을 짤 수 있나 ──
    console.log('\n### [2] 채운 2종의 결재선을 짤 수 있나 (등재만 하고 못 짜면 반쪽)\n');

    const cand = await api('/api/employees/approver-candidates', { token: TOKEN, deviceId: DEV });
    const approver = (cand.json || [])[0];
    if (approver) {
        for (const t of NEW) {
            const save = await api('/api/approval/lines', {
                method: 'POST', token: TOKEN, deviceId: DEV,
                body: { docType: t, lines: [{ seqNo: 1, approverId: approver.employeeId, approverName: approver.empName }] }
            });
            const ok = save.status === 200;
            rec(`E1-9-${t}`, `${labels[t] || t} 결재선 저장`, `HTTP ${save.status}`, ok);
            if (ok) {
                // 되돌린다 — 실측이 흔적을 남기지 않는다
                await api('/api/approval/lines', { method: 'POST', token: TOKEN, deviceId: DEV, body: { docType: t, lines: [] } });
            }
        }
    } else {
        rec('E1-9', '결재선 저장', '결재자 후보가 없어 시도 불가', null);
    }

    // ── [3] ERP 라벨이 살아 있나 ──
    console.log('\n### [3] ERP 를 뺐어도 라벨은 살아 있나 (옛 문서 보호)\n');
    const done = await api('/api/approval/completed', { token: TOKEN, deviceId: DEV });
    const erpDocs = (done.json || []).filter(d => ERP.includes(d.docType));
    if (erpDocs.length) {
        const eng = erpDocs.filter(d => d.docTypeLabel === d.docType);
        rec('E1-5', 'ERP 결재 문서 라벨', eng.length ? `🔴 영문: ${eng.map(d => d.docType).join(', ')}` : '한글 정상',
            eng.length === 0);
    } else {
        rec('E1-5', 'ERP 결재 문서 라벨', '이 테넌트에 ERP 결재 문서가 0건 — 시험이 대신 지킨다', null,
            'ErpApprovalSeparationGateTests.ERP_를_뺐어도_옛문서_라벨은_한글로_뜬다');
    }

    // ── [4] 회귀 ──
    console.log('\n### [4] 회귀 — 기존 경로가 그대로 도나\n');
    for (const [id, p, label] of [
        ['E1-R1', '/api/approval/pending', '결재 대기함'],
        ['E1-R2', '/api/approval/sent', '내가 보낸 결재'],
        ['E1-R3', '/api/approval/completed', '완료된 결재'],
        ['E1-R4', '/api/employees', '사원 목록'],
    ]) {
        const r = await api(p, { token: TOKEN, deviceId: DEV });
        // 🔴 본문으로 가른다 — SPA 라 없는 주소도 200 이다
        const isJson = Array.isArray(r.json);
        rec(id, label, `HTTP ${r.status} · ${isJson ? 'JSON' : 'HTML(SPA 폴백)'}`, r.status === 200 && isJson,
            'JSON 이어야 진짜 API 다');
    }

    const pass = R.filter(r => r.pass === true).length;
    const fail = R.filter(r => r.pass === false).length;
    const info = R.filter(r => r.pass === null).length;
    console.log('\n' + '='.repeat(78));
    console.log(`결과 — 🟢 ${pass} PASS · 🔴 ${fail} FAIL · ⚪ ${info} INFO`);
    console.log('='.repeat(78));
    if (fail) { console.log('\n🔴 FAIL:'); R.filter(r => r.pass === false).forEach(r => console.log(`  ${r.id}  ${r.what}\n     → ${r.got}`)); }

    fs.writeFileSync(process.env.OUT || 'e1.json', JSON.stringify(R, null, 2));
    process.exit(fail > 0 ? 1 : 0);
})();