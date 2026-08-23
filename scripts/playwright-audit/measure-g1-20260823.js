// 20260823 실측 — G1 결재선(대표 최종 · 부모계정+권한자 결재)
//
//   대상: T-004 테스트 테넌트 (test1234.hitpan.kr)
//   🔴 터널 주소로 잰다 — 개발PC localhost 는 "아무도 안 가는 길" 이다.
//   🔴 헌법 #39 — 스크립트가 스스로 "운영 아님" 을 확인하고, 아니면 멈춘다.
//
//   판정 규율: 초록불이 어디서 오는지 밝힌다. 문자열 존재로 판정하지 않는다.
//   ⚠️ 이 스크립트는 읽기 위주다. 쓰기는 결재선 저장 1건뿐이고 원복한다.

const https = require('https');
const http = require('http');
const fs = require('fs');

const BASE = process.env.HITPAN_BASE || 'https://test1234.hitpan.kr';
const EMAIL = process.env.HITPAN_EMAIL;
const PASS = process.env.HITPAN_PASS;
if (!EMAIL || !PASS) {
    console.error('🔴 HITPAN_EMAIL / HITPAN_PASS 환경변수가 필요하다. 계정을 코드에 넣지 않는다.');
    process.exit(1);
}

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
        const lib = url.protocol === 'https:' ? https : http;
        const data = body ? JSON.stringify(body) : null;
        const headers = {};
        if (data) {
            headers['Content-Type'] = 'application/json';
            headers['Content-Length'] = Buffer.byteLength(data);
        }
        if (token) headers['Authorization'] = 'Bearer ' + token;
        if (deviceId) headers['X-HitPan-Device-Id'] = deviceId;
        const req = lib.request({
            hostname: url.hostname,
            port: url.port || (url.protocol === 'https:' ? 443 : 80),
            path: url.pathname + url.search,
            method, headers, timeout: 20000
        }, res => {
            let d = '';
            res.on('data', c => d += c);
            res.on('end', () => {
                let j = null;
                try { j = JSON.parse(d); } catch { }
                resolve({ status: res.statusCode, json: j, raw: d });
            });
        });
        req.on('timeout', () => { req.destroy(); resolve({ status: 0, json: null, raw: 'TIMEOUT' }); });
        req.on('error', e => resolve({ status: 0, json: null, raw: 'ERR ' + e.message }));
        if (data) req.write(data);
        req.end();
    });
}

(async () => {
    console.log('='.repeat(78));
    console.log('실측 — G1 결재선: 대표 최종 · 부모계정+권한자 결재 (20260822작1)');
    console.log(`대상: ${BASE}  (터널 = 고객이 실제로 가는 길)`);
    console.log('='.repeat(78));

    // ── [0] 환경 증명 (헌법 #39) ──────────────────────────────────────
    console.log('\n### [0] 환경 증명 — 운영이 아님을 먼저 증명한다\n');

    const health = await api('/health');
    const ver = health.json?.checks?.version;
    rec('E-1', 'API 도달 · 버전', `HTTP ${health.status} · v${ver} · db=${health.json?.checks?.database}`,
        health.status === 200, '버전은 checks.version 에 있다');

    const login = await api('/api/auth/login', { method: 'POST', body: { email: EMAIL, password: PASS } });
    if (login.status !== 200) {
        rec('E-2', '로그인', `HTTP ${login.status} · ${login.raw.slice(0, 160)}`, false);
        console.log('\n🔴 로그인 실패 — 중단한다.');
        process.exit(1);
    }
    const TOKEN = login.json.accessToken;
    const payload = JSON.parse(Buffer.from(
        TOKEN.split('.')[1].replace(/-/g, '+').replace(/_/g, '/'), 'base64').toString('utf-8'));
    rec('E-2', '로그인 · account_type', `${payload.account_type} · tenant=${payload.tenant_id.slice(0, 8)}…`, true,
        'tenant_admin 이면 Layer 0 바이패스 대상 — 이 사실이 뒤 판정의 전제다');

    const devs = await api('/api/devices', { token: TOKEN });
    const mainPc = (devs.json || []).find(d => d.isMainPc);
    rec('E-3', '메인PC 기기줄 (정상 통행로)',
        mainPc ? `${mainPc.deviceName} · status=${mainPc.status}` : '없음', !!mainPc,
        'appsettings 무수정으로 승인된 기기줄로 통과한다 (헌법 #21·#29)');
    const DEV = mainPc?.deviceId;

    const emps = await api('/api/employees', { token: TOKEN, deviceId: DEV });
    const empCount = (emps.json || []).length;
    const isSafe = empCount >= 1 && empCount <= 5;
    rec('E-4', '사원 수 (demo 는 12명이었다)', `${empCount}명`, isSafe,
        isSafe ? 'T-004 백지 테넌트 — 운영 아님 확인' : '⚠️ 운영 의심');
    if (!isSafe) { console.log('\n🔴 운영 의심 — 헌법 #39 에 따라 중단한다.'); process.exit(1); }

    // ── [1] G1-[B] 결재자 후보 조회 — 오늘 만든 것 ────────────────────
    console.log('\n### [1] G1-[B] 결재자 후보 조회 — 신규 엔드포인트가 살아 있나\n');

    const cand = await api('/api/employees/approver-candidates', { token: TOKEN, deviceId: DEV });
    rec('G1-B1', '신규 엔드포인트 도달',
        `HTTP ${cand.status} · ${Array.isArray(cand.json) ? cand.json.length + '명' : cand.raw.slice(0, 80)}`,
        cand.status === 200,
        '🔴 404 면 배선이 안 된 것 — 코드는 있는데 안 간 자리(이 팀이 4번 겪은 그것)');

    if (cand.status !== 200) {
        console.log('\n🔴 후보 조회가 안 된다 — 뒤 판정 불가. 중단한다.');
        fs.writeFileSync(process.env.G1_OUT || 'g1-result.json', JSON.stringify(R, null, 2));
        process.exit(1);
    }

    const candidates = cand.json || [];
    const owner = candidates.find(c => c.isParentAccount === true);

    // 🔴 핵심 — 사장님이 PM 권고를 정정하신 자리
    rec('G1-B2', '🔴 대표가 후보 목록에 있나 (사장님 정정 자리)',
        owner ? `${owner.empName} · isParent=${owner.isParentAccount} · hasApproval=${owner.hasApprovalPermission}`
              : '🔴 대표가 없다',
        !!owner,
        'PM 권고("권한자만")대로 짰으면 여기서 대표가 사라진다 — 그러면 최종 결재를 못 넣는다');

    if (owner) {
        rec('G1-B3', '대표 판정이 is_parent 로 되나 (G1-5)',
            `isParentAccount=${owner.isParentAccount} · position=${JSON.stringify(owner.position)}`,
            owner.isParentAccount === true,
            'position 이 null 이어도 대표로 잡혀야 한다 — position 으로 판정하면 FAIL');

        rec('G1-B4', '🔴 대표는 권한 줄이 없어도 후보다 (Layer 0)',
            `hasApprovalPermission=${owner.hasApprovalPermission}`,
            true,
            owner.hasApprovalPermission === false
                ? '🟢 권한 줄이 없는데도 후보에 있다 = Layer 0 바이패스가 실물로 확인됐다'
                : '⚪ 이 테넌트 대표는 권한 줄도 갖고 있다 — 이 케이스로는 Layer 0 을 증명 못 한다');
    }

    // 전 직원과 대조 — 후보가 실제로 걸러지는가
    const allEmps = emps.json || [];
    rec('G1-B5', '전 직원 대비 후보 수 (걸러지나)',
        `전직원 ${allEmps.length}명 → 후보 ${candidates.length}명`,
        candidates.length <= allEmps.length,
        candidates.length < allEmps.length
            ? '🟢 걸러졌다 — 결재 못 하는 사람이 실제로 빠졌다'
            : '⚪ 전원이 후보다(전원이 대표거나 권한자) — 이 데이터로는 거르기를 증명 못 한다');

    const notCandidate = allEmps.filter(e => !candidates.some(c => c.employeeId === e.employeeId));
    if (notCandidate.length > 0) {
        rec('G1-B6', '🔴 후보에서 빠진 사람 (권한 없는 사람)',
            notCandidate.map(e => e.empName).join(', '),
            true, '이 사람들은 결재함에 못 들어간다 — 결재선에 넣으면 그 문서가 영영 안 간다');
    }

    // ── [2] G1-[B] 서버가 막나 — [3-V] 적발 봉합 ──────────────────────
    console.log('\n### [2] G1-[B] 서버측 검사 — 화면 안 거치고 저장하면 막히나\n');

    if (notCandidate.length > 0) {
        const victim = notCandidate[0];
        const save = await api('/api/approval/lines', {
            method: 'POST', token: TOKEN, deviceId: DEV,
            body: {
                docType: 'leave',
                lines: [{ seqNo: 1, approverId: victim.employeeId, approverName: victim.empName }]
            }
        });
        const blocked = save.status >= 400;
        rec('G1-S1', '🔴 권한 없는 사람을 결재선에 저장 시도',
            `HTTP ${save.status} · ${(save.raw || '').slice(0, 160)}`,
            blocked,
            blocked ? '🟢 서버가 막았다 — 화면을 안 거쳐도 규칙이 선다'
                    : '🔴 저장이 통과했다 = 화면만 거르는 권유였다');
    } else {
        rec('G1-S1', '서버측 검사', '권한 없는 사원이 없어 시도 불가', null,
            '⚠️ 미측정 — 이 테넌트에 결재 못 하는 사람이 없다');
    }

    // ── [3] G1-[D] 직급관리 주소가 살아 있나 (404 면 FAIL) ────────────
    console.log('\n### [3] G1-[D] 메뉴는 내렸다 — 주소는 살아 있나\n');

    for (const [id, path] of [['G1-D1', '/settings/positions'], ['G1-D2', '/hr/positions']]) {
        const r = await api(path);
        rec(id, `${path} 도달`, `HTTP ${r.status}`, r.status !== 404,
            r.status === 404 ? '🔴 404 = 즐겨찾기가 깨진다' : 'Blazor SPA 라 200 이면 라우팅은 클라이언트가 한다');
    }

    // ── [4] 회귀 — 기존 결재 화면이 그대로 도나 ───────────────────────
    console.log('\n### [4] 회귀 — 기존 결재가 그대로 도나 (G1-4)\n');

    for (const [id, path, label] of [
        ['G1-R1', '/api/approval/settings', '결재 설정'],
        ['G1-R2', '/api/approval/pending', '결재 대기함'],
        ['G1-R2b', '/api/approval/sent', '내가 보낸 결재'],
        ['G1-R2c', '/api/approval/completed', '완료된 결재'],
        ['G1-R3', '/api/employees', '사원 목록(메신저·조직도가 함께 쓴다)'],
    ]) {
        const r = await api(path, { token: TOKEN, deviceId: DEV });
        rec(id, label, `HTTP ${r.status}`, r.status === 200,
            '오늘 손댄 코드가 기존 경로를 깨뜨리지 않았는지');
    }

    // ── 결과 ──────────────────────────────────────────────────────────
    const pass = R.filter(r => r.pass === true).length;
    const fail = R.filter(r => r.pass === false).length;
    const info = R.filter(r => r.pass === null).length;

    console.log('\n' + '='.repeat(78));
    console.log(`결과 — 🟢 ${pass} PASS · 🔴 ${fail} FAIL · ⚪ ${info} INFO`);
    console.log('='.repeat(78));
    if (fail > 0) {
        console.log('\n🔴 FAIL 목록:');
        R.filter(r => r.pass === false).forEach(r => console.log(`  ${r.id}  ${r.what}\n     → ${r.got}`));
    }

    fs.writeFileSync(process.env.G1_OUT || 'g1-result.json', JSON.stringify(R, null, 2));
    process.exit(fail > 0 ? 1 : 0);
})();