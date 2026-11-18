'use strict';
// W-G1-51 갈래 Z — 인쇄 측정 장치 자체시험 (node 만 · 브라우저 · DB · 네트워크 · 앱 0)
//   덮는 것: 판정기(칸 판정 · 양성 대조 · 음성 무효화 · main 멈춤 · ⑨ 셈 S1 · S2 · S3 · 검출 규칙) · 차림 정의(7종 · N=5 · 5mm 밀기 · 그림 해시)
//            · PNG 만들기(결정적 · 4분면 순서) · 런 계획(plan.json 꼴 · 잡 이름 · 패치 경로) · 워크플로 ui-print 잡 지킴 글자
//   못 덮는 것(미측정): 실제 화면 차림 · 실제 PDF 판독(그건 PrintProbe selftest T1~T6 · CI 런)
//   쓰는 법: node print.selftest.js   — 끝 줄 「인쇄 자체시험 끝 — 통과 n · 실패 m」 · 실패 1 이상이면 끝 코드 1
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');
const J = require('./judge-print');
const F = require('./fixture');
const P = require('./png');
const PLAN = require('./plan');

let pass = 0;
let fail = 0;
function check(id, ok, why) {
  if (ok) pass++; else fail++;
  console.log(`${ok ? '🟢 PASS' : '🔴 FAIL'} ${id} — ${why}`);
}
const C = J.CAT;

// ── 합성 사실 도우미 ──
const word = (t, x, y, extra) => ({ t, x, y, w: 10, h: 3.5, ...extra });
const facts = (pages) => ({ schema: 'print-facts/1', ok: true, pages: pages.map((p, i) => ({ n: i + 1, drawsOk: true, words: [], images: [], draws: [], fonts: [], ...p })) });
const i0 = 0;
const fld = F.fieldsFor(i0);
const at = (key) => fld.find((f) => f.key === key);
function goodDocWords() {
  return [
    word(F.MARK.partner, at('partner_name').x, at('partner_name').y),
    word('2026-11-19', at('doc_date').x, at('doc_date').y),
    word(F.MARK.items[0].name, at('line.item_name').x, at('line.item_name').y),
    word('7', at('line.qty').x + 15, at('line.qty').y, { w: 3 }),
    word('22,000', at('total_amount').x + 30, at('total_amount').y, { w: 15 }),
  ];
}
const bg0 = P.formBgPng(0, 'EST');
const logo = P.logoPng();
const bgImg = { sha: bg0.sha, quad: bg0.quad, ws: bg0.w, hs: bg0.h, w: 210, h: 297 };

// T-J1 칸 값 상자 안 → 초록 · 한 칸 밖 → 빨강
{
  const planned = F.plannedCells({ types: ['estimate'] });
  const k2doc = planned.cells.find((c) => c.id === 'K2.estimate.doc');
  const cell = { ...k2doc, status: 'captured', ui: { warnCount: 0 } };
  const ctx = { board: 'main', pcOk: true, factsById: { [cell.id]: facts([{ words: goodDocWords() }]) } };
  const g = J.judgeCell(cell, ctx);
  const bad = goodDocWords();
  bad[0] = word(F.MARK.partner, at('partner_name').x + 40, at('partner_name').y + 30);
  const r = J.judgeCell(cell, { ...ctx, factsById: { [cell.id]: facts([{ words: bad }]) } });
  check('T-J1 칸 값 상자 안 → 초록 · 거래처 칸 밖 → 빨강', g.cat === C.GREEN && r.cat === C.RED && /partner_name/.test(r.why), `${g.cat} / ${r.cat} · ${r.why.slice(0, 80)}`);
}
// T-J2 음성 기준(그림 없음 · 표준 머리 0)은 양성 대조 실패면 미측정
{
  const planned = F.plannedCells({ types: ['estimate'] });
  const cell = { ...planned.cells.find((c) => c.id === 'K2.estimate.doc'), status: 'captured', ui: { warnCount: 0 } };
  const f = facts([{ words: goodDocWords(), images: [bgImg] }]); // 양식지 전표 = 그림 0 이어야 → 음성 실패
  const a = J.judgeCell(cell, { board: 'main', pcOk: true, factsById: { [cell.id]: f } });
  const b = J.judgeCell(cell, { board: 'main', pcOk: false, factsById: { [cell.id]: f } });
  const c = J.judgeCell(cell, { board: 'main', pcOk: false, factsById: { [cell.id]: facts([{ words: goodDocWords() }]) } });
  check('T-J2 음성 실패 = 빨강(양성 대조 통과) · 미측정(양성 대조 실패) · 음성 통과여도 양성 대조 실패면 미측정',
    a.cat === C.RED && b.cat === C.UNMEASURED && c.cat === C.UNMEASURED, `${a.cat} / ${b.cat} / ${c.cat}`);
}
// T-J3 그림 출처 — 해시 · 4분면 순서 + 종횡비 · 순서 다르면 아님
{
  const ref = { sha: bg0.sha, quad: bg0.quad, aspect: bg0.aspect };
  const h1 = J.imageFrom({ sha: bg0.sha }, ref).hit;
  const h2 = J.imageFrom({ sha: 'x', quad: bg0.quad.map((v) => v * 0.9 + 0.05), ws: 420, hs: 594 }, ref).hit;
  const h3 = J.imageFrom({ sha: 'x', quad: [...bg0.quad].reverse(), ws: 420, hs: 594 }, ref).hit;
  const h4 = J.imageFrom({ sha: 'x', quad: bg0.quad, ws: 420, hs: 420 }, ref).hit;
  check('T-J3 그림 출처 1순위 해시 · 2순위 순서+종횡비 · 순서 뒤집힘 · 종횡비 다름 = 아님', h1 && h2 && !h3 && !h4, `${h1} ${h2} ${h3} ${h4}`);
}
// T-J4 워터마크 객체 확인 — 실효 불투명도 0.12 초록 · 0 빨강 · 빈 투명 묶음(그림 0) 빨강 · 그린 자리 해석 실패 = 미측정
{
  const ref = { sha: logo.sha, quad: logo.quad, aspect: logo.aspect };
  const chk = { kind: 'watermark', ref, alpha: 0.12, tol: 0.02 };
  const img = { sha: 'L', quad: logo.quad, ws: 160, hs: 120, w: 80, h: 60 };
  const ok = J.evalCheck(chk, facts([{ images: [img], draws: [{ sha: 'L', alpha: 0.12, imgSMask: true, smaskMean: 1 }] }]), {}, {}).ok;
  const baked = J.evalCheck(chk, facts([{ images: [img], draws: [{ sha: 'L', alpha: 1, imgSMask: true, smaskMean: 0.12 }] }]), {}, {}).ok;
  const zero = J.evalCheck(chk, facts([{ images: [img], draws: [{ sha: 'L', alpha: 0, imgSMask: false }] }]), {}, {});
  const empty = J.evalCheck(chk, facts([{ images: [], draws: [] }]), {}, {});
  const broken = J.evalCheck(chk, facts([{ drawsOk: false, images: [img], draws: [] }]), {}, {});
  check('T-J4 워터마크 ca 0.12 · 마스크에 구운 0.12 초록 · 0 빨강 · 그림 0 빨강 · 해석 실패 미측정',
    ok && baked && !zero.ok && !zero.unmeasured && !empty.ok && !empty.unmeasured && broken.unmeasured === true,
    `${ok} ${baked} ${zero.ok} ${empty.why} ${broken.why}`);
}
// T-J5 스타일 표지 — 제목 22pt + 명조 = formal 초록 · 20pt = formal 빨강 · basic 회귀 초록
{
  const f22 = facts([{ words: [word('견적서', 80, 15, { fs: 22 }), word('x', 1, 1, { fs: 9 })], fonts: ['ABCDEF+NotoSerifKR-Bold'] }]);
  const f20 = facts([{ words: [word('견적서', 20, 15, { fs: 20 })], fonts: ['MalgunGothic'] }]);
  const formal = F.STYLES.find((s) => s.key === 'formal');
  const basic = F.STYLES.find((s) => s.key === 'basic');
  const a = J.evalCheck({ kind: 'style', ...formal }, f22, {}, {}).ok;
  const b = J.evalCheck({ kind: 'style', ...formal }, f20, {}, {}).ok;
  const c = J.evalCheck({ kind: 'style', ...basic }, f20, {}, {}).ok;
  check('T-J5 스타일 표지(제목 크기 · 명조)', a && !b && c, `${a} ${b} ${c}`);
}
// T-J6 차림 불가 · 문 없음 · 오류가 곧 결함 · 미측정 · 바뀐 모양
{
  const s = J.judgeCell({ id: 'a', status: 'setup-fail', reason: '저장 문 500', expect: [] }, { board: 'main' });
  const n = J.judgeCell({ id: 'b', noDoorAtMain: '문 없음', status: 'captured', expect: [] }, { board: 'main' });
  const d = J.judgeCell({ id: 'c', status: 'capture-fail', errorIsDefect: true, http: { status: 404, firstLine: '{"message":"저장된 전표가 아닙니다."}' }, expect: [] }, { board: 'main' });
  const u = J.judgeCell({ id: 'd', status: 'capture-fail', reason: '시간 넘김', expect: [] }, { board: 'main' });
  const planned = F.plannedCells({ types: ['estimate'] });
  const k4 = { ...planned.cells.find((c) => c.id === 'K4.estimate.doc'), status: 'captured', ui: { warnCount: 0 } };
  const ch = J.judgeCell(k4, { board: 'fixed', pcOk: true, factsById: { [k4.id]: facts([{ words: goodDocWords() }]) } });
  check('T-J6 차림 불가 · 문 없음 · 오류=결함 빨강 · 시간 넘김 미측정 · K4 바뀐 모양',
    s.cat === C.SETUP && n.cat === C.NO_DOOR && d.cat === C.RED && /404/.test(d.why) && u.cat === C.UNMEASURED && ch.cat === C.CHANGED,
    `${s.cat} · ${n.cat} · ${d.cat} · ${u.cat} · ${ch.cat}`);
}
// T-J7 판독기 양성 대조 — 기본 폼 「품명」 · 「공급가액」 + 양식지 그림 · 글자 / 하나 빠지면 실패 / 판독기 자체시험 실패도 실패
{
  const pc = F.POSITIVE_CONTROL;
  const fb = { [pc.basicForm.cell]: facts([{ words: [word('품명', 20, 100), word('공급가액', 120, 100)] }]), [pc.preprint.cell]: facts([{ words: [word('x', 1, 1)], images: [bgImg] }]) };
  const ok = J.positiveControl(pc, fb, true).ok;
  const noWord = J.positiveControl(pc, { ...fb, [pc.basicForm.cell]: facts([{ words: [word('품명', 20, 100)] }]) }, true).ok;
  const probeBad = J.positiveControl(pc, fb, false).ok;
  check('T-J7 판독기 양성 대조(있음 · 하나 빠짐 · 자체시험 실패)', ok && !noWord && !probeBad, `${ok} ${noWord} ${probeBad}`);
}
// T-J8 main 멈춤 ⓐ — 빨강 기대 칸 초록 · K1~K5 전부 초록
{
  const rows = [
    { id: 'K1.e.settings', k: 'K1', mainExpect: '빨강', cat: C.GREEN },
    { id: 'K2.e.settings', k: 'K2', mainExpect: '초록일 수 있음', cat: C.GREEN },
  ];
  const st = J.mainStops(rows, 'main');
  const none = J.mainStops([{ id: 'K1', k: 'K1', mainExpect: '빨강', cat: C.RED }], 'main');
  check('T-J8 main 멈춤 ⓐ(빨강 기대 칸 초록 · K 전부 초록) · 대조(빨강이면 멈춤 0)', st.length === 2 && none.length === 0, `${st.length} / ${none.length}`);
}
// T-J9 ⑨ 셈 — main 문 없음 · P1 실패 = 음성 전부 미측정(S3 · N 포함) · 누락 정의 S1(거절이어도 해석 흔적) · 최소 유효 S2 · 과잉 거절 따로
{
  const valid = (ids) => ({ rows: ids.map((id) => ({ id, validity: '유효' })) });
  const all = ['P1', 'P2', 'P3', 'P4', 'U1', 'U2', 'E1', 'E2', 'E3', 'E4', 'E5', 'E6', 'E7', 'E7a', 'E7b', 'E7c', 'E8'];
  const rej = { decision: '거절', parseStart: 0 };
  const okOut = Object.fromEntries(all.map((id) => [id, id.startsWith('E') || id === 'U1' || id === 'P4' ? rej : { decision: '허용', parseStart: 1, copy: true }]));
  const main = J.nineCount(valid(all), okOut, 'main');
  const green = J.nineCount(valid(all), okOut, 'fixed');
  const p1Bad = J.nineCount(valid(all), { ...okOut, P1: { decision: '거절' } }, 'fixed', { a: { setup: true, underlay: 0, error: 0, native: 0 } });
  const trace = J.nineCount(valid(all), { ...okOut, E4: { decision: '거절', parseStart: 1 } }, 'fixed');
  const allow = J.nineCount(valid(all), { ...okOut, E7: { decision: '허용' } }, 'fixed');
  const s2 = J.nineCount(valid(all.filter((id) => id !== 'E7c')), okOut, 'fixed');
  check('T-J9 ⑨ 셈 main 문 없음 · 초록 · P1 실패 = E·P·U·N 미측정 · 거절+해석 흔적 = 누락 · 허용 = 누락 · E7c 무효 = 부분',
    main.cat === C.NO_DOOR && green.cat === C.GREEN && green.overReject.P4 === 1 && p1Bad.cat === C.UNMEASURED && p1Bad.n1.a === C.UNMEASURED
    && trace.cat === C.RED && trace.omission === 1 && allow.cat === C.RED && s2.cat === '부분',
    `${main.cat} · ${green.cat}(과잉 P4 ${green.overReject.P4}) · ${p1Bad.cat} · ${trace.cat}/${trace.omission} · ${allow.cat} · ${s2.cat}`);
}
// T-J10 검출 규칙(HQA ⑦) — 예상 = 실제 · 다른 사유 빨강 = 검출 아님 · 빨강 아님 = 검출 아님
{
  const a = J.detection('E7 · E7a · E7b 허용', 'E7 · E7a · E7b 허용', C.RED).detected;
  const b = J.detection('E7 · E7a · E7b 허용', 'P1 거절', C.RED).detected;
  const c = J.detection('E7 · E7a · E7b 허용', 'E7 · E7a · E7b 허용', C.GREEN).detected;
  check('T-J10 검출 = 예상 사유 = 실제 사유일 때만', a && !b && !c, `${a} ${b} ${c}`);
}
// T-F1 차림 정의 — 7종 · N=5 · 종마다 5mm 밀기 · 그림 해시 7개 다름 · 종 키 = main 키 · 좌표 JSON 꼴
{
  const shas = new Set(F.TYPES.map((t, i) => P.formBgPng(i, t.mark).sha));
  const shift = F.fieldsFor(1)[0].x - F.fieldsFor(0)[0].x === 5 && F.fieldsFor(6)[0].y - F.fieldsFor(0)[0].y === 30;
  const keys = F.TYPES.map((t) => t.key).join(',');
  const cj = JSON.parse(F.coordsJson(2));
  const inPaper = F.TYPES.every((t, i) => F.fieldsFor(i).every((f) => f.x >= 0 && f.x + f.w <= 210 && f.y >= 0 && f.y <= 297));
  check('T-F1 차림 정의(7종 · N=5 · 5mm 밀기 · 해시 7 · 키 · 좌표 꼴 · 종이 안)',
    F.TYPES.length === 7 && F.FIELDS.length === 5 && shift && shas.size === 7 && inPaper
    && keys === 'estimate,delivery,sales_order,purchase_order,receipt,purchase_return,sales_return'
    && cj.length === 6 && cj[0].key === 'partner_name' && cj[0].x_mm === 35 && cj[5].key === 'row_pitch',
    `종 ${F.TYPES.length} · 칸 ${F.FIELDS.length} · 밀기 ${shift} · 해시 ${shas.size} · 종이 안 ${inPaper}`);
}
// T-F2 판독 칸 수 · 이름 겹침 0 · 양성 대조 칸이 칸 목록에 있음 · sameWords 대조 칸 있음
{
  const pl = F.plannedCells();
  const ids = pl.cells.map((c) => c.id);
  const dup = ids.length - new Set(ids).size;
  const pcIn = ids.includes(F.POSITIVE_CONTROL.basicForm.cell) && ids.includes(F.POSITIVE_CONTROL.preprint.cell);
  const refs = pl.cells.flatMap((c) => (c.expect || []).filter((e) => e.kind === 'sameWords').map((e) => e.as));
  const refsOk = refs.every((r) => ids.includes(r));
  check('T-F2 판독 칸 이름 겹침 0 · 양성 대조 칸 있음 · 회귀 대조 칸 있음', dup === 0 && pcIn && refsOk && refs.length === 8, `칸 ${ids.length} · 겹침 ${dup} · 회귀 대조 ${refs.length}`);
}
// T-P1 PNG — 결정적(두 번 같은 바이트) · 머리 · IHDR 크기 · 다시 풀어 4분면 밝기 = 기록값
{
  const a = P.logoPng();
  const b = P.logoPng();
  const png = a.png;
  const w = png.readUInt32BE(16);
  const h = png.readUInt32BE(20);
  // IDAT 풀어 4분면 다시 셈
  let pos = 8;
  const idat = [];
  while (pos < png.length) {
    const len = png.readUInt32BE(pos);
    const type = png.toString('ascii', pos + 4, pos + 8);
    if (type === 'IDAT') idat.push(png.subarray(pos + 8, pos + 8 + len));
    pos += 12 + len;
  }
  const raw = zlib.inflateSync(Buffer.concat(idat));
  const rgb = Buffer.alloc(w * h * 3);
  for (let y = 0; y < h; y++) raw.copy(rgb, y * w * 3, y * (w * 3 + 1) + 1, (y + 1) * (w * 3 + 1));
  const q = P.quadMeans(rgb, w, h);
  const want = [0.90, 0.15, 0.65, 0.40];
  const close = q.every((v, i) => Math.abs(v - want[i]) < 0.01);
  check('T-P1 로고 PNG 결정적 · 160×120 · 4분면 밝기 0.90/0.15/0.65/0.40', a.sha === b.sha && w === 160 && h === 120 && close, `해시 같음 ${a.sha === b.sha} · ${w}×${h} · ${q.join(',')}`);
}
// T-L1 런 계획 — plan.json 꼴 · 잡 이름 · 패치 경로 · 이번 런 matrix 줄
{
  const plan = JSON.parse(fs.readFileSync(path.join(__dirname, 'plan.json'), 'utf8'));
  const v = PLAN.validate(plan);
  const m = PLAN.matrixFor(plan);
  const bad = PLAN.validate({ ...plan, runs: { ...plan.runs, Z1: { ...plan.runs.Z1, jobs: [{ id: 'x;rm', board: 'main', patch: '../a' }] } } });
  check('T-L1 런 계획 꼴 · 이번 런 matrix · 나쁜 잡 이름 · 패치 경로 거절', v.ok && m && Array.isArray(m.include) && m.include.length >= 1 && !bad.ok,
    `계획 ${v.ok ? '통과' : v.why} · 이번 런 ${plan.run} 잡 ${m ? m.include.length : '-'} · 나쁜 꼴 거절 ${!bad.ok}`);
}
// T-W1 워크플로 ui-print 잡 지킴 글자 — 수동 실행 + 이 가지만 · matrix = 계획 잡 출력 · 업로드 = 누출 검사 통과분 · 기존 두 잡 머리 그대로
{
  const ymlPath = path.join(__dirname, '..', '..', '..', '..', '.github', 'workflows', 'ui-browser.yml');
  const yml = fs.existsSync(ymlPath) ? fs.readFileSync(ymlPath, 'utf8') : '';
  const job = yml.slice(yml.indexOf('\n  ui-print:'));
  const guards = [
    ['수동 실행 + 이 가지', /if: \$\{\{ github\.event_name == 'workflow_dispatch' && startsWith\(github\.ref, 'refs\/heads\/feat\/20261118-WG1-51-'\) \}\}/],
    ['matrix = 계획 잡', /matrix: \$\{\{ fromJSON\(needs\.ui-print-plan\.outputs\.matrix\) \}\}/],
    ['누출 검사 뒤 업로드', /if: always\(\) && steps\.leak\.outcome == 'success'\s*$/m],
    ['qpdf = ⑨ 재는 잡만', /if: \$\{\{ matrix\.qpdf == true \}\}/],
  ];
  const real = guards.map(([, re]) => re.test(job));
  const k35 = yml.indexOf('\n  ui-k35:') > 0 && yml.indexOf('\n  ui-changes:') > 0 && yml.indexOf('\n  ui-selftest:') < yml.indexOf('\n  ui-print-plan:');
  check('T-W1 ui-print 지킴 글자(수동 + 이 가지 · matrix = 계획 · 누출 검사 뒤 업로드 · qpdf 조건) · 잡 차례',
    yml.length > 0 && real.every(Boolean) && k35, `${guards.map(([n], i) => `${n}=${real[i]}`).join(' · ')} · 차례 ${k35}`);
}

console.log(`인쇄 자체시험 끝 — 통과 ${pass} · 실패 ${fail}`);
process.exitCode = fail === 0 ? 0 : 1;
