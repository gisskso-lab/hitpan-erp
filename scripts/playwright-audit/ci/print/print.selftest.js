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
// ⬛ [낡은 입력 · 2차] f22 = 제목 낱말 + 명조만 · f20 = 제목 낱말만 — 3차 스타일 기준(배치 · 선)이 쪽 폭 · 선 사실을 요구 → 입력에 더함
{
  const f22 = facts([{ wMm: 210, hMm: 297, words: [word('견적서', 95, 15, { fs: 22, w: 20 }), word('x', 1, 1, { fs: 9 })], fonts: ['ABCDEF+NotoSerifKR-Bold'], rules: [{ x: 20, y: 30, w: 170, t: 1.5 }, { x: 20, y: 31, w: 170, t: 0.5 }] }]);
  const f20 = facts([{ wMm: 210, hMm: 297, words: [word('견적서', 20, 15, { fs: 20, w: 20 })], fonts: ['MalgunGothic'], rules: [{ x: 20, y: 30, w: 170, t: 1 }] }]);
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
  // ⬛ [낡은 입력 · 2차] ctx = { board: 'main' } — 3차 F1: 양성 대조 값 없음 = 그 런 전체 미측정 → 양성 대조 통과(pcOk) 를 입력에 명시
  const d = J.judgeCell({ id: 'c', status: 'capture-fail', errorIsDefect: true, http: { status: 404, firstLine: '{"message":"저장된 전표가 아닙니다."}' }, expect: [] }, { board: 'main', pcOk: true });
  const u = J.judgeCell({ id: 'd', status: 'capture-fail', reason: '시간 넘김', expect: [] }, { board: 'main', pcOk: true });
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
  // ⬛ [낡은 입력 · 2차] 양식지 = 글자 'x' 1개 + 그림 — 3차 F4: 양식지 칸 값(맞춘 칸 5 상자 안)까지 봄 → 칸 자리 글자로
  const fb = { [pc.basicForm.cell]: facts([{ words: [word('품명', 20, 100), word('공급가액', 120, 100)] }]), [pc.preprint.cell]: facts([{ words: goodDocWords(), images: [bgImg] }]) };
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
    // 🔴 3차 — [3-V] A5 패치 적용 전 · 뒤 검사 · A11 자격 증명 안 남김 · [4] F9 중간 출력 나무 밖
    ['패치 적용 전 검사', /node scripts\/playwright-audit\/ci\/print\/plan\.js --check-patch "\$PRINT_PATCH"/],
    ['패치 적용 뒤 검사', /plan\.js --check-paths/],
    ['판독기 중간 출력 나무 밖', /PrintProbe\.csproj -c Release --artifacts-path "\$RUNNER_TEMP\/probe-art" -o "\$RUNNER_TEMP\/probe"/],
  ];
  const real = guards.map(([, re]) => re.test(job));
  const planJob = yml.slice(yml.indexOf('\n  ui-print-plan:'));
  const noCred = (planJob.match(/persist-credentials: false/g) || []).length === 2;
  guards.push(['체크아웃 자격 증명 안 남김 2', /./]);
  real.push(noCred);
  const k35 = yml.indexOf('\n  ui-k35:') > 0 && yml.indexOf('\n  ui-changes:') > 0 && yml.indexOf('\n  ui-selftest:') < yml.indexOf('\n  ui-print-plan:');
  check('T-W1 ui-print 지킴 글자(수동 + 이 가지 · matrix = 계획 · 누출 검사 뒤 업로드 · qpdf 조건) · 잡 차례',
    yml.length > 0 && real.every(Boolean) && k35, `${guards.map(([n], i) => `${n}=${real[i]}`).join(' · ')} · 차례 ${k35}`);
}

// ════ 3차(11/19) — [3-V] · [4] 지적 경우. 각 경우 = 「고치기 전 판정기에서 실패 · 고친 판에서 통과」(명세서 §6-c) ════
// 고치기 전 판에 없는 함수를 부르면 던진다 → 그 경우 실패로 센다(멈추지 않음).
function guard(id, fn) {
  try {
    fn();
  } catch (e) {
    check(id, false, `예외 ${e.name}: ${String(e.message).slice(0, 80)}`);
  }
}
// 양성 대조가 통과하는 사실 묶음(고친 판 F4 기준) — 다른 경우를 그 경우만으로 가르려고
const basicGood = facts([{ wMm: 210, hMm: 297, words: [word('품명', 40, 100), word('수량', 90, 100), word('공급가액', 130, 100)] }]);
const preGood = facts([{ wMm: 210, hMm: 297, words: goodDocWords(), images: [bgImg] }]);
const pcFacts = { [F.POSITIVE_CONTROL.basicForm.cell]: basicGood, [F.POSITIVE_CONTROL.preprint.cell]: preGood };
const styleGood = (st) => facts([{
  wMm: 210, hMm: 297,
  words: [word(F.MARK.partner, 30, 60, { fs: 10 }), word('견적서', st.align === 'center' ? 95 : 20, 15, { fs: st.titlePt, w: 20, h: 7 }),
    word(F.MARK.items[0].name, 30, 90, { fs: 10 }), word(F.MARK.items[1].name, 30, 98, { fs: 10 })],
  fonts: st.serif ? ['ABCDEF+NotoSerifKR-Bold'] : ['MalgunGothic'],
  // 고치기 전 STYLES 엔 rules 가 없다 — 기본 1pt 로 채워 「판정기 판정」 으로 실패가 드러나게(도우미 예외로 실패하지 않게)
  rules: (st.rules || [1]).map((t, k) => ({ x: 20, y: 35 + k, w: 170, t })),
}]);

// T-Z1 [4] F1 — 양성 대조 실패 = 그 런 전체 미측정(빨강도 남기지 않음) · pass 가 양성 대조를 봄(판독기 자체시험 실패 런 합격 0)
guard('T-Z1 [4] F1 양성 대조 실패 = 전체 미측정 · pass 가 양성 대조 봄', () => {
  const planned = F.plannedCells({ types: ['estimate'] });
  const k1 = { ...planned.cells.find((c) => c.id === 'K1.estimate.settings'), status: 'captured' };
  // 판독기 고장 꼴: 기본 폼 · 양식지 글자 0 → 2차 판정기 = K1 「기대한 빨강」(공허 빨강) · 멈춤 0
  const broken = facts([{ words: [], images: [bgImg] }]);
  const runMain = J.judgeRun({ cells: [k1], board: 'main', factsById: { [k1.id]: broken, [F.POSITIVE_CONTROL.basicForm.cell]: broken, [F.POSITIVE_CONTROL.preprint.cell]: broken }, positive: F.POSITIVE_CONTROL, probeSelftestOk: true, groups: [] });
  // 고친 판 · 판독기 자체시험 실패 · 양성 기준만 있는 칸(ST-basic) 초록 꼴 → 2차 판정기 = pass 참
  const st = { ...planned.cells.find((c) => c.id === 'ST-basic.estimate.doc'), status: 'captured' };
  const runFixed = J.judgeRun({ cells: [st], board: 'merged', factsById: { ...pcFacts, [st.id]: styleGood(F.STYLES[0]) }, positive: F.POSITIVE_CONTROL, probeSelftestOk: false, groups: [] });
  check('T-Z1 [4] F1 양성 대조 실패 = 전체 미측정 · pass 가 양성 대조 봄',
    runMain.rows[0].cat === C.UNMEASURED && runMain.counts[C.RED] === 0 && runFixed.rows[0].cat === C.UNMEASURED && runFixed.pass === false,
    `main K1 ${runMain.rows[0].cat} · 빨강 ${runMain.counts[C.RED]} / 고친 판 ST ${runFixed.rows[0].cat} · pass ${runFixed.pass}`);
});
// T-Z2 [3-V] A2 — 기준 0개 칸(merged 판 K5 꼴)이 열리기만 하면 초록 · pass 참이던 길
guard('T-Z2 [3-V] A2 기준 0개 칸 = 미측정 · pass 거짓', () => {
  const cell = { id: 'K5.estimate.settings', group: '②', k: 'K5', type: 'estimate', path: 'settings', noDoorAtMain: '문 없음', status: 'captured', expect: [] };
  const run = J.judgeRun({ cells: [cell], board: 'merged', factsById: { ...pcFacts, [cell.id]: facts([{ words: [] }]) }, positive: F.POSITIVE_CONTROL, probeSelftestOk: true, groups: [] });
  check('T-Z2 [3-V] A2 기준 0개 칸 = 미측정 · pass 거짓', run.positive.ok === true && run.rows[0].cat === C.UNMEASURED && run.pass === false,
    `양성 대조 ${run.positive.ok} · ${run.rows[0].cat} · pass ${run.pass}`);
});
// T-Z3 [3-V] A3 — 두 PDF 글자 0 = '' = '' 「같음」 초록이던 길 · 양성 대조 실패여도 초록이던 길
guard('T-Z3 [3-V] A3 회귀 대조 글자 0 · 양성 대조 실패 = 미측정', () => {
  const chk = { kind: 'sameWords', as: 'K2.estimate.doc' };
  const a = J.evalCheck(chk, facts([{ words: [] }]), {}, { pcOk: true, factsById: { 'K2.estimate.doc': facts([{ words: [] }]) } });
  const b = J.evalCheck(chk, facts([{ words: goodDocWords() }]), {}, { pcOk: false, factsById: { 'K2.estimate.doc': facts([{ words: goodDocWords() }]) } });
  check('T-Z3 [3-V] A3 회귀 대조 글자 0 · 양성 대조 실패 = 미측정', !a.ok && a.unmeasured === true && !b.ok && b.unmeasured === true, `${a.ok}/${a.unmeasured} · ${b.ok}/${b.unmeasured}`);
});
// T-Z4 [3-V] A1 — 유효 E 행인데 제품 결과 없음 → 2차 = 「누락 0 · 유효 E 4」 초록
// ⬛ [낡은 경우 · 3차] 기대 = 「누락으로 셈」(PM 글자 · 빨강 · 누락 4 · code 누락:E1,E4,E7,E7c) — PM 정정(11/19 · 설계 :89 「PDF 못 받음 · … · 4xx/5xx = 미측정(빨강 아님)」)으로 기대를 바꿈
// ⬛   guard('T-Z4 [3-V] A1 유효 E 결과 없음 = 누락으로 셈 · 초록 0', () => {
// ⬛     const mf = { rows: ['P1', 'E1', 'E4', 'E7', 'E7c'].map((id) => ({ id, validity: '유효' })) };
// ⬛     const r = J.nineCount(mf, { P1: { decision: '허용', parseStart: 1, copy: true } }, 'merged');
// ⬛     check('T-Z4 [3-V] A1 유효 E 결과 없음 = 누락으로 셈 · 초록 0', r.cat !== C.GREEN && r.cat === C.RED && r.omission === 4 && r.noResult === 4 && r.code === '누락:E1,E4,E7,E7c',
// ⬛       `${r.cat} · 누락 ${r.omission} · 결과 없음 ${r.noResult} · ${r.code} · ${r.why}`);
// ⬛   });
// 🔴 3차-a(PM 정정 11/19 · 설계 :89 · :263) — 결과 없음 행 = 미측정(초록 0 · 빨강 아님) · 분모 제외 · 결과 없음 n 따로
//   · 결과 있는 E 행 0 = ⑨ 미측정 · S2(E4 · E5 / E7 계열 / E7c 「유효 + 결과 있음」) 못 채움 = 부분 · 채움 = 초록(결과 없음 n 적음)
//   고치기 전(3차) 판정기 = 셋 다 빨강(누락) · 2차 판정기 = 첫 경우 초록 → 이 경우 실패
guard('T-Z4 [3-V] A1 · PM 정정 유효 E 결과 없음 = 그 행 미측정 · 분모 제외 · 초록 0', () => {
  const mf = { rows: ['P1', 'E1', 'E4', 'E7', 'E7c'].map((id) => ({ id, validity: '유효' })) };
  const r = J.nineCount(mf, { P1: { decision: '허용', parseStart: 1, copy: true } }, 'merged');
  const eAll = ['E1', 'E2', 'E3', 'E4', 'E5', 'E6', 'E7', 'E7a', 'E7b', 'E7c', 'E8'];
  const mfAll = { rows: ['P1', ...eAll].map((id) => ({ id, validity: '유효' })) };
  const base = Object.fromEntries(eAll.map((id) => [id, { decision: '거절', parseStart: 0 }]));
  base.P1 = { decision: '허용', parseStart: 1, copy: true };
  const partOut = { ...base };
  delete partOut.E4;
  delete partOut.E5;
  const part = J.nineCount(mfAll, partOut, 'merged');
  const oneOut = { ...base };
  delete oneOut.E1;
  const one = J.nineCount(mfAll, oneOut, 'merged');
  const unm = `${C.UNMEASURED}(결과 없음)`;
  check('T-Z4 [3-V] A1 · PM 정정 유효 E 결과 없음 = 그 행 미측정 · 분모 제외 · 초록 0',
    r.cat === C.UNMEASURED && r.omission === 0 && r.noResult === 4 && r.code === '' && ['E1', 'E4', 'E7', 'E7c'].every((id) => r.rows[id] === unm)
    && part.cat === '부분' && part.omission === 0 && part.noResult === 2 && part.rows.E4 === unm && /E4 · E5/.test(part.why)
    // ⬛ [낡은 기대 · 3차-a] && one.cat === C.GREEN && one.omission === 0 && one.noResult === 1 && one.rows.E1 === unm && /결과 없음 1/.test(one.why),
    //   — PM 확인 ⓐ 반려(유효 E 행 결과 없음 ≥1 = ⑨ 부분) → 3차-b 기대 = 부분(T-Z18 이 따로 봄)
    && one.cat === '부분' && one.omission === 0 && one.noResult === 1 && one.rows.E1 === unm && /결과 없음 1/.test(one.why),
    `전부 결과 없음 ${r.cat} · 누락 ${r.omission} · 결과 없음 ${r.noResult} · code '${r.code}' / E4 · E5 결과 없음 ${part.cat} · 누락 ${part.omission} / E1 만 결과 없음 ${one.cat} · ${one.why}`);
});
// T-Z5 [3-V] A7 — 판독기 자체시험 값 없음(undefined) = 실패
guard('T-Z5 [3-V] A7 판독기 자체시험 값 없음 = 양성 대조 실패', () => {
  const r = J.positiveControl(F.POSITIVE_CONTROL, pcFacts, undefined);
  const ok = J.positiveControl(F.POSITIVE_CONTROL, pcFacts, true);
  check('T-Z5 [3-V] A7 판독기 자체시험 값 없음 = 양성 대조 실패', r.ok === false && ok.ok === true, `없음 ${r.ok} · 참 ${ok.ok} · ${r.why.slice(0, 60)}`);
});
// T-Z6 [3-V] A8 — 묶음 칸이 전부 해당 없음 · 판정 제외면 묶음 초록이던 길
guard('T-Z6 [3-V] A8 묶음 전부 해당 없음 = 미측정', () => {
  const g = J.composite({ id: 'g', cells: ['a', 'b'] }, { a: { cat: C.NA }, b: { cat: C.EXCLUDED } });
  const green = J.composite({ id: 'g', cells: ['a', 'b'] }, { a: { cat: C.NA }, b: { cat: C.GREEN } });
  check('T-Z6 [3-V] A8 묶음 전부 해당 없음 = 미측정', g.cat === C.UNMEASURED && green.cat === C.GREEN, `${g.cat} / ${green.cat}`);
});
// T-Z7 [4] F4 — 양성 대조 깊이: 기본 폼 좌표(차례 · 한 줄) · 양식지 그림 해시 · 칸 값
guard('T-Z7 [4] F4 양성 대조 = 좌표 · 그림 해시 · 칸 값', () => {
  const pc = F.POSITIVE_CONTROL;
  const swapped = J.positiveControl(pc, { ...pcFacts, [pc.basicForm.cell]: facts([{ words: [word('품명', 130, 100), word('공급가액', 40, 100)] }]) }, true).ok;
  const twoRows = J.positiveControl(pc, { ...pcFacts, [pc.basicForm.cell]: facts([{ words: [word('품명', 40, 100), word('공급가액', 130, 160)] }]) }, true).ok;
  const otherImg = J.positiveControl(pc, { ...pcFacts, [pc.preprint.cell]: facts([{ words: goodDocWords(), images: [{ sha: 'x', quad: [...bg0.quad].reverse(), ws: 420, hs: 594 }] }]) }, true).ok;
  const noFields = J.positiveControl(pc, { ...pcFacts, [pc.preprint.cell]: facts([{ words: [word('x', 1, 1)], images: [bgImg] }]) }, true).ok;
  const good = J.positiveControl(pc, pcFacts, true).ok;
  check('T-Z7 [4] F4 양성 대조 = 좌표 · 그림 해시 · 칸 값', good && !swapped && !twoRows && !otherImg && !noFields, `좋음 ${good} · 차례 바뀜 ${swapped} · 두 줄 ${twoRows} · 다른 그림 ${otherImg} · 칸 값 0 ${noFields}`);
});
// T-Z8 [4] F3 — 스타일 표지: 머리 배치 · 선 굵기(이중선) · 품명 잘림 0 · 「4종 중 둘이 같은 PDF」
guard('T-Z8 [4] F3 스타일 표지 배치 · 선 · 잘림 · 같은 PDF', () => {
  const S = Object.fromEntries(F.STYLES.map((s) => [s.key, s]));
  const chk = (st) => ({ kind: 'style', ...st, noCut: st.noCut ? F.MARK.items.map((x) => x.name) : undefined });
  const formalOk = J.evalCheck(chk(S.formal), styleGood(S.formal), {}, {});
  const formalLeft = J.evalCheck(chk(S.formal), styleGood({ ...S.formal, align: 'left' }), {}, {});
  const formalOneLine = J.evalCheck(chk(S.formal), styleGood({ ...S.formal, rules: [1.5] }), {}, {});
  const cutF = styleGood(S.large);
  cutF.pages[0].words = cutF.pages[0].words.map((w) => (w.t === F.MARK.items[1].name ? { ...w, t: '표지품목' } : w));
  const largeCut = J.evalCheck(chk(S.large), cutF, {}, {});
  const same = J.evalCheck({ kind: 'distinctFrom', ids: ['ST-basic.e.doc'] }, styleGood(S.basic), {}, { factsById: { 'ST-basic.e.doc': styleGood(S.basic) } });
  const diff = J.evalCheck({ kind: 'distinctFrom', ids: ['ST-basic.e.doc'] }, styleGood(S.formal), {}, { factsById: { 'ST-basic.e.doc': styleGood(S.basic) } });
  check('T-Z8 [4] F3 스타일 표지 배치 · 선 · 잘림 · 같은 PDF',
    formalOk.ok && !formalLeft.ok && /배치/.test(formalLeft.code || '') && !formalOneLine.ok && /선/.test(formalOneLine.code || '')
    && !largeCut.ok && /잘림/.test(largeCut.code || '') && !same.ok && !same.unmeasured && diff.ok,
    `격식 ${formalOk.ok} · 왼쪽 ${formalLeft.code} · 한 줄 ${formalOneLine.code} · 잘림 ${largeCut.code} · 같은 PDF ${same.ok}/${same.unmeasured} · 다름 ${diff.ok}`);
});
// T-Z9 [4] F2 — 워터마크 ① = 7종(작업지시서 :121 · :131) · 판매 반품 입구 둘
guard('T-Z9 [4] F2 워터마크 7종', () => {
  const pl = F.plannedCells();
  const onSettings = F.TYPES.filter((t) => pl.cells.some((c) => c.id === `WM-가-on.${t.key}.settings`)).length;
  const srt = ['doc-i', 'doc-j'].every((p) => pl.cells.some((c) => c.id === `WM-나-off.sales_return.${p}`));
  const groups = pl.groups.filter((g) => /^①\(가\)/.test(g.id)).length;
  check('T-Z9 [4] F2 워터마크 7종', onSettings === 7 && srt && groups === 7, `켬 설정 미리보기 종 ${onSettings} · 판매 반품 두 입구 ${srt} · ①(가) 묶음 ${groups}`);
});
// T-Z10 [4] F6 — 검출 규칙 연결: 예상 code = 실제 code 일 때만 · 미고정 · 예상 0 인 심은 결함 잡 = 계획 거절
guard('T-Z10 [4] F6 검출 연결 · 예상 사유 꼴 고정', () => {
  const plan = JSON.parse(fs.readFileSync(path.join(__dirname, 'plan.json'), 'utf8'));
  const allValid = { rows: ['P1', 'E1', 'E2', 'E3', 'E4', 'E5', 'E6', 'E7', 'E7a', 'E7b', 'E7c', 'E8'].map((id) => ({ id, validity: '유효' })) };
  const rej = { decision: '거절', parseStart: 0 };
  const base = Object.fromEntries(['E1', 'E2', 'E3', 'E4', 'E5', 'E6', 'E7', 'E7a', 'E7b', 'E7c', 'E8'].map((id) => [id, rej]));
  base.P1 = { decision: '허용', parseStart: 1, copy: true };
  const allow = { decision: '허용' };
  const testPlan = { ...plan, runs: { ...plan.runs, R3: { jobs: [{ id: 'r3-nine-b', board: 'merged', tenant: 'A', phase: 'nine', patch: 'scripts/playwright-audit/ci/print/patches/r3-nine-b.patch', qpdf: true, expect: ['nine-b'] }] } } };
  const ex = J.expectsFromPlan(testPlan, 'R3', 'r3-nine-b');
  const hit = J.detectionsFor(ex, { rows: [] }, J.nineCount(allValid, { ...base, E7: allow, E7a: allow, E7b: allow }, 'merged'))[0];
  const other = J.detectionsFor(ex, { rows: [] }, J.nineCount(allValid, { ...base, E7: allow }, 'merged'))[0];
  const okPlan = PLAN.validate(testPlan).ok;
  const noExpect = PLAN.validate({ ...testPlan, runs: { ...testPlan.runs, R3: { jobs: [{ ...testPlan.runs.R3.jobs[0], expect: undefined }] } } }).ok;
  const unfixed = PLAN.validate({ ...testPlan, runs: { ...testPlan.runs, R3: { jobs: [{ ...testPlan.runs.R3.jobs[0], expect: ['nine-c'] }] } } }).ok;
  check('T-Z10 [4] F6 검출 연결 · 예상 사유 꼴 고정', hit.detected === true && other.detected === false && okPlan && !noExpect && !unfixed,
    `같은 사유 ${hit.detected}(${hit.actual}) · 다른 사유 ${other.detected}(${other.actual}) · 계획 ${okPlan} · 예상 0 거절 ${!noExpect} · 미고정 거절 ${!unfixed}`);
});
// T-Z11 [3-V] A5 — 패치가 건드리는 파일 검사(측정 장치 · 워크플로 = 거절 · 덩이 본문 「--- …」 오독 0)
guard('T-Z11 [3-V] A5 패치 건드리는 파일 검사', () => {
  const judgeP = ['diff --git a/scripts/playwright-audit/ci/print/judge-print.js b/scripts/playwright-audit/ci/print/judge-print.js', '--- a/scripts/playwright-audit/ci/print/judge-print.js', '+++ b/scripts/playwright-audit/ci/print/judge-print.js', '@@ -1,1 +1,1 @@', '-a', '+b', ''].join('\n');
  const srcP = ['diff --git a/src/HitPan.API/Migrations/x.sql b/src/HitPan.API/Migrations/x.sql', '--- a/src/HitPan.API/Migrations/x.sql', '+++ b/src/HitPan.API/Migrations/x.sql', '@@ -1,2 +1,1 @@', '--- /* 지운 SQL 주석 */', ' SELECT 1;', ''].join('\n');
  const renameP = ['diff --git a/src/a.cs b/.github/workflows/x.yml', 'similarity index 100%', 'rename from src/a.cs', 'rename to .github/workflows/x.yml', ''].join('\n');
  const a = PLAN.checkPatchText(judgeP).ok;
  const b = PLAN.checkPatchText(srcP).ok;
  const c = PLAN.checkPatchText(renameP).ok;
  const d = PLAN.checkPaths('src/a.cs\nscripts/playwright-audit/ci/leak-scan.js').ok;
  const e = PLAN.checkPaths('src/a.cs\n').ok;
  check('T-Z11 [3-V] A5 패치 건드리는 파일 검사', !a && b && !c && !d && e, `판정기 패치 거절 ${!a} · src 패치 통과 ${b} · 워크플로로 이름 바꿈 거절 ${!c} · 적용 뒤 누출 검사 바뀜 거절 ${!d} · src 만 ${e}`);
});
// T-Z12 [4] F5 — 거래명세서 입구 = 반품 줄 뺌(정렬 무관) · ⓙ = 반품 줄만 · 그 밖 그대로
guard('T-Z12 [4] F5 거래명세서 입구 반품 줄 거름', () => {
  const CAP = require('./capture');
  const fake = { filter: (o) => ({ by: o }) };
  const del = CAP.entryPick('delivery', 'doc')(fake);
  const j = CAP.entryPick('sales_return', 'doc-j')(fake);
  const est = CAP.entryPick('estimate', 'doc')(fake);
  check('T-Z12 [4] F5 거래명세서 입구 반품 줄 거름', del.by && del.by.hasNotText === '반품' && j.by && j.by.hasText === '반품' && est === fake,
    `거래명세서 ${JSON.stringify(del.by)} · ⓙ ${JSON.stringify(j.by)} · 견적서 그대로 ${est === fake}`);
});

// ════ 3차-a(11/19 · PM 판단 1~3) — 각 경우 = 「고치기 전(76688452) 판정기에서 실패 · 고친 판에서 통과」 ════
const nineAll = ['P1', 'P2', 'P3', 'P4', 'U1', 'U2', 'E1', 'E2', 'E3', 'E4', 'E5', 'E6', 'E7', 'E7a', 'E7b', 'E7c', 'E8'];
const nineValid = { rows: nineAll.map((id) => ({ id, validity: '유효' })) };
const nineRej = { decision: '거절', parseStart: 0 };
const nineOk = { decision: '허용', parseStart: 1, copy: true };
// E 전부 정상 거절 · P1~P3 · U2 허용 · U1 해석 0 거절 · P4 허용 = ⑨ 초록 바탕
const nineBase = Object.fromEntries(nineAll.map((id) => [id, id.startsWith('E') || id === 'U1' ? nineRej : nineOk]));

// T-Z13 PM 판단 2(설계 :263 「P1~P3 · U2 거절 = 결함」 · U1 빨강) — ⑨ 전체 색에 P · U 행. 고치기 전 = E 만 봐서 초록
guard('T-Z13 PM 판단 2 ⑨ 전체 색에 P · U 결함 · 과잉 거절 수 따로', () => {
  // 경우 1: P2 · P3 · U2 거절 + P4 거절 → ⑨ 빨강(P · U 결함 P2 · P3 · U2) · 과잉 거절 P2 1 · P4 1(P4 = 받아들임 · 결함 아님) · 누락 code 무변('')
  const c1 = J.nineCount(nineValid, { ...nineBase, P2: nineRej, P3: nineRej, U2: nineRej, P4: nineRej }, 'merged');
  // 경우 2: U1 이 해석 시작 1(구조 못 읽음인데 해석기로 감) → U1 빨강 → ⑨ 빨강 / 대조: P4 만 거절 = ⑨ 초록(과잉 거절 P4 1 · 받아들임)
  const c2 = J.nineCount(nineValid, { ...nineBase, U1: { decision: '거절', parseStart: 1 } }, 'merged');
  const p4 = J.nineCount(nineValid, { ...nineBase, P4: nineRej }, 'merged');
  // P1 거절 = S3 그대로(같은 런 양성 대조 실패 = 미측정 · 설계 :254)
  const p1 = J.nineCount(nineValid, { ...nineBase, P1: nineRej }, 'merged');
  check('T-Z13 PM 판단 2 ⑨ 전체 색에 P · U 결함 · 과잉 거절 수 따로',
    c1.cat === C.RED && c1.omission === 0 && Array.isArray(c1.puDefect) && c1.puDefect.join(',') === 'P2,P3,U2' && c1.overReject.P2 === 1 && c1.overReject.P4 === 1 && c1.code === ''
    && c2.cat === C.RED && Array.isArray(c2.puDefect) && c2.puDefect.join(',') === 'U1' && c2.rows.U1 === C.RED
    && p4.cat === C.GREEN && p4.overReject.P4 === 1 && p4.puDefect.length === 0 && p1.cat === C.UNMEASURED,
    `P2 · P3 · U2 거절 ${c1.cat}(${c1.puDefect}) · 과잉 P2 ${c1.overReject.P2} · P4 ${c1.overReject.P4} / U1 해석 1 ${c2.cat}(${c2.puDefect}) / P4 만 거절 ${p4.cat} / P1 거절 ${p1.cat}`);
});

// T-Z14 PM 판단 3(CTO 조건 7 「같은 계열 · 판 기록」) — ⒞ 명령 판 · ⒜ QuestPDF 동봉 판 둘 다 결과표에 · 동봉 판 못 읽음 = 패키지 판 + 「동봉 qpdf 판 미측정」
guard('T-Z14 PM 판단 3 qpdf 판 두 칸(⒞ 명령 · ⒜ 동봉)', () => {
  const mfRead = { ...nineValid, qpdfVersion: 'qpdf version 11.9.0', qpdfMissing: false, questPdfVersion: '2026.2.4', qpdfBundledVersion: '12.2.0' };
  const mfNot = { ...nineValid, qpdfVersion: null, qpdfMissing: true, questPdfVersion: '2026.2.4', qpdfBundledVersion: null, qpdfBundledWhy: '동봉 qpdf 판 미측정(DllNotFoundException)' };
  const a = J.nineCount(mfRead, nineBase, 'merged');
  const m = J.nineCount(mfNot, nineBase, 'main');
  const res = { positive: { ok: true, why: '-' }, rows: [], composite: [], counts: Object.fromEntries(Object.values(C).map((v) => [v, 0])), stops: [], denomOut: 0 };
  const md = J.toMarkdown({ run: 'R1', job: 'x', board: 'merged' }, res, a);
  const mdMeta = J.toMarkdown({ run: 'R1', job: 'x', board: 'merged', nineQpdf: J.qpdfOf(mfNot), nineQpdfBundled: J.qpdfBundledOf(mfNot) }, res, null);
  const okRead = /11\.9\.0/.test(a.qpdf) && /12\.2\.0/.test(a.qpdfBundled || '') && /2026\.2\.4/.test(a.qpdfBundled || '');
  const okNot = /동봉 qpdf 판 미측정/.test(m.qpdfBundled || '') && /QuestPDF 2026\.2\.4/.test(m.qpdfBundled || '') && m.cat === C.NO_DOOR;
  const okMd = /⑨ qpdf 판 ⒞ 명령.*11\.9\.0/.test(md) && /⑨ qpdf 판 ⒜ QuestPDF 동봉.*12\.2\.0/.test(md)
    && /⑨ 표본 qpdf 판 ⒞ 명령/.test(mdMeta) && /⑨ 표본 qpdf 판 ⒜ QuestPDF 동봉.*동봉 qpdf 판 미측정/.test(mdMeta);
  check('T-Z14 PM 판단 3 qpdf 판 두 칸(⒞ 명령 · ⒜ 동봉)', okRead && okNot && okMd,
    `읽음 ⒞ ${a.qpdf} · ⒜ ${a.qpdfBundled} / 못 읽음(main) ⒜ ${m.qpdfBundled} / 결과표 두 줄 ${okMd}`);
});

// ════ 3차-b(11/19 · PM 판단 「3차 재셈」 · [3-V] B1 · B3 · B4 · [4] F11 · F12 · PM 확인 ⓐ · 과잉 거절 수 md) ════
// 각 경우 = 「고치기 전(e134ce11) 판정기 · 차림 · 계획에서 실패 · 고친 판에서 통과」(명세서 §6-c 3차-b 행)
const titled = (title, extra) => facts([{ wMm: 210, hMm: 297, words: [word(title, 20, 15, { fs: 20, w: 20, h: 7 }), word('문서번호:', 20, 24, { fs: 9 }), ...(extra || [])] }]);
const partnerWord = () => word(F.MARK.partner, 30, 60, { fs: 10 });
// 고치기 전 차림(DOC_TITLES 없음)에서도 도우미 예외가 아니라 판정기 판정으로 실패가 드러나게
const firstTitle = (ty) => ((F.DOC_TITLES || {})[ty] || ['?'])[0];

// T-Z15 [3-V] B1 — 「없음」 기준만 있던 45칸(①(가) 끔 · ①(나) 켬/끔) = 빈 쪽 · 오류 안내 · 엉뚱한 전표에서 초록이던 길(3-V 측정 45/45)
guard('T-Z15 [3-V] B1 「없음」 기준만 있던 칸 = 문서 확인 먼저 · 빈 쪽 · 오류 안내 · 엉뚱한 전표 = 초록 0', () => {
  const pl = F.plannedCells();
  // 칸 고르기 = 이름(고치기 전 · 뒤 같은 45칸) · 고친 판에 「없음」 만 남은 칸 수 = 0 이어야
  const target = pl.cells.filter((c) => /^WM-(가-off|나-on|나-off)\./.test(c.id));
  const negOnlyLeft = pl.cells.filter((c) => Array.isArray(c.expect) && c.expect.length && c.expect.every((e) => e.neg === true)).length;
  const defectN = target.filter((c) => c.errorIsDefect).length;
  const blank = facts([{ wMm: 210, hMm: 297 }]);
  const errPdf = facts([{ wMm: 210, hMm: 297, words: [word('저장된', 10, 10, { fs: 10 }), word('전표가', 22, 10, { fs: 10 }), word('아닙니다.', 34, 10, { fs: 10 })] }]);
  const judgeAll = (mk, board) => {
    const cats = {};
    for (const c of target) {
      const r = J.judgeCell({ ...c, status: 'captured', ui: { warnCount: 0 } }, { board, pcOk: true, factsById: { [c.id]: mk(c) } });
      cats[r.cat] = (cats[r.cat] || 0) + 1;
    }
    return cats;
  };
  const g = (cats) => cats[C.GREEN] || 0;
  const bMain = judgeAll(() => blank, 'main');
  const bMerged = judgeAll(() => blank, 'merged');
  const eMain = judgeAll(() => errPdf, 'main');
  // 엉뚱한 전표 = 다른 종 제목(+ 표지 거래처) → 차림 불가 · 오류가 곧 결함인 칸(판매 반품 전표 입구) = 빨강
  const wMain = judgeAll((c) => titled(c.type === 'delivery' ? '견적서' : '거래명세서', [partnerWord()]), 'main');
  // 맞는 종 제목인데 표지 거래처 없음(전표 입구) = 엉뚱한 전표(같은 종 다른 전표)
  const noMark = judgeAll((c) => titled(firstTitle(c.type)), 'main');
  // 대조 — 맞는 문서(제목 + 전표 입구면 표지) = 초록 되살아남(기준이 살아 있나)
  const goodFor = (c) => titled(firstTitle(c.type), c.path === 'settings' ? [] : [partnerWord()]);
  const gMain = judgeAll(goodFor, 'main');
  // 반품 둘 = main 제목 「문서」 받아들임
  const docTitle = J.judgeCell({ ...target.find((c) => c.id === 'WM-나-off.purchase_return.doc'), status: 'captured', ui: { warnCount: 0 } },
    { board: 'main', pcOk: true, factsById: { 'WM-나-off.purchase_return.doc': titled('문서', [partnerWord()]) } }).cat;
  // 판정기 지킴 — 차림 정의가 빠뜨린 「없음」 만 칸 = 미측정
  const negCell = { id: 'X.neg', status: 'captured', expect: [{ kind: 'image', ref: { sha: 'z' }, want: 'absent', neg: true }] };
  const guardCat = J.judgeCell(negCell, { board: 'main', pcOk: true, factsById: { 'X.neg': goodFor({ type: 'estimate', path: 'settings' }) } }).cat;
  const notSettingsN = target.filter((c) => c.path !== 'settings').length;
  check('T-Z15 [3-V] B1 「없음」 기준만 있던 칸 = 문서 확인 먼저 · 빈 쪽 · 오류 안내 · 엉뚱한 전표 = 초록 0',
    target.length === 45 && negOnlyLeft === 0 && defectN === 6
    && g(bMain) === 0 && bMain[C.UNMEASURED] === 45 && g(bMerged) === 0
    && g(eMain) === 0 && eMain[C.UNMEASURED] === 45 - defectN && eMain[C.RED] === defectN
    && g(wMain) === 0 && wMain[C.SETUP] === 45 - defectN && wMain[C.RED] === defectN
    && g(noMark) === 45 - notSettingsN && (noMark[C.SETUP] || 0) === notSettingsN - defectN && (noMark[C.RED] || 0) === defectN
    && gMain[C.GREEN] === 45 && docTitle === C.GREEN && guardCat === C.UNMEASURED,
    `45칸 ${target.length} · 「없음」 만 남음 ${negOnlyLeft} · 빈 쪽 main ${JSON.stringify(bMain)} · merged 초록 ${g(bMerged)} · 오류 안내 ${JSON.stringify(eMain)} · 엉뚱한 종 ${JSON.stringify(wMain)} · 표지 없음 ${JSON.stringify(noMark)} · 맞는 문서 초록 ${gMain[C.GREEN]} · 반품 「문서」 ${docTitle} · 지킴 ${guardCat}`);
});

// T-Z16 [4] F11 — 초록(회귀) 기대 칸(ST-basic · ST-ignore 16칸)이 main 빨강이어도 멈춤 0 이던 길
guard('T-Z16 [4] F11 초록(회귀) 기대 칸 main 빨강 = 멈춤', () => {
  const reg = F.plannedCells().cells.filter((c) => c.mainExpect === '초록(회귀)');
  const rows = reg.map((c, k) => ({ id: c.id, k: c.k, type: c.type, group: c.group, mainExpect: c.mainExpect, cat: k === 0 ? C.RED : C.GREEN }));
  const st = J.mainStops(rows, 'main');
  const none = J.mainStops(rows.map((r) => ({ ...r, cat: C.GREEN })), 'main');
  const unm = J.mainStops(rows.map((r) => ({ ...r, cat: C.UNMEASURED })), 'main');
  const merged = J.mainStops(rows, 'merged');
  check('T-Z16 [4] F11 초록(회귀) 기대 칸 main 빨강 = 멈춤',
    reg.length === 16 && st.length === 1 && st[0].startsWith(`${reg[0].id} main 빨강`) && none.length === 0 && unm.length === 0 && merged.length === 0,
    `회귀 칸 ${reg.length} · 한 칸 빨강 멈춤 ${st.length}(${String(st[0] || '-').slice(0, 50)}) · 전부 초록 ${none.length} · 미측정 ${unm.length} · merged ${merged.length}`);
});

// T-Z17 [4] F12 — ①(가) 멈춤이 7종 합쳐 every 라 한 종만 main 초록이면 안 걸리던 길(설계 :86)
guard('T-Z17 [4] F12 ①(가) 멈춤 = 종마다', () => {
  const wmOn = F.plannedCells().cells.filter((c) => c.group === '①(가)' && /-on\./.test(c.id));
  const row = (c, cat) => ({ id: c.id, k: c.k, type: c.type, group: c.group, mainExpect: c.mainExpect, cat });
  const wm = (s) => s.filter((x) => /^①\(가\)/.test(x));
  const one = wm(J.mainStops(wmOn.map((c) => row(c, c.type === 'estimate' ? C.GREEN : C.RED)), 'main'));
  const allRed = wm(J.mainStops(wmOn.map((c) => row(c, C.RED)), 'main'));
  const half = wm(J.mainStops(wmOn.map((c) => row(c, c.type === 'estimate' && c.path === 'settings' ? C.GREEN : C.RED)), 'main'));
  const allGreen = wm(J.mainStops(wmOn.map((c) => row(c, C.GREEN)), 'main'));
  check('T-Z17 [4] F12 ①(가) 멈춤 = 종마다',
    one.length === 1 && /estimate/.test(one[0]) && allRed.length === 0 && half.length === 0 && allGreen.length === 7,
    `견적서만 초록 ${one.length}(${String(one[0] || '-').slice(0, 40)}) · 전부 빨강 ${allRed.length} · 견적서 설정만 초록 ${half.length} · 전부 초록 ${allGreen.length}`);
});

// T-Z18 PM 확인 ⓐ — S2 를 채워도 유효 E 행 결과 없음 ≥1 = ⑨ 부분(합격 0 · 「결과 없음 n」) · 3차-a = 초록
guard('T-Z18 PM 확인 ⓐ 유효 E 결과 없음 ≥1 = ⑨ 부분', () => {
  const out = { ...nineBase };
  delete out.E1;
  const r = J.nineCount(nineValid, out, 'merged');
  const full = J.nineCount(nineValid, nineBase, 'merged');
  const two = { ...nineBase };
  delete two.E2;
  delete two.E8;
  const r2 = J.nineCount(nineValid, two, 'merged');
  check('T-Z18 PM 확인 ⓐ 유효 E 결과 없음 ≥1 = ⑨ 부분',
    r.cat === '부분' && r.noResult === 1 && /결과 없음 1\(E1/.test(r.why) && /합격 0/.test(r.why) && full.cat === C.GREEN && r2.cat === '부분' && r2.noResult === 2,
    `E1 결과 없음 ${r.cat} · ${r.why.slice(0, 70)} / 전부 결과 ${full.cat} / E2 · E8 결과 없음 ${r2.cat}(${r2.noResult})`);
});

// T-Z19 PM 「범위 밖 발견」 — 과잉 거절 수(P4 · P2)가 print-result.md 에 없던 것(json 에만)
guard('T-Z19 과잉 거절 수 P4 · P2 결과표 md 에', () => {
  const res = { positive: { ok: true, why: '-' }, rows: [], composite: [], counts: Object.fromEntries(Object.values(C).map((v) => [v, 0])), stops: [], denomOut: 0 };
  const n = J.nineCount(nineValid, { ...nineBase, P4: nineRej, P2: nineRej }, 'merged');
  const md = J.toMarkdown({ run: 'R1', job: 'x', board: 'merged' }, res, n);
  const md0 = J.toMarkdown({ run: 'R1', job: 'x', board: 'merged' }, res, J.nineCount(nineValid, nineBase, 'merged'));
  const mdMain = J.toMarkdown({ run: 'Z1', job: 'x', board: 'main' }, res, J.nineCount(nineValid, nineBase, 'main'));
  check('T-Z19 과잉 거절 수 P4 · P2 결과표 md 에',
    /⑨ 과잉 거절 수[^\n]*P4 1 · P2 1/.test(md) && /⑨ 과잉 거절 수[^\n]*P4 0 · P2 0/.test(md0) && /⑨ 과잉 거절 수[^\n]*셈 없음/.test(mdMain),
    `P4 · P2 거절 ${/P4 1 · P2 1/.test(md)} · 0 ${/P4 0 · P2 0/.test(md0)} · main 셈 없음 ${/셈 없음/.test(mdMain)}`);
});

// T-Z20 [3-V] B3 — 적용 뒤 검사가 git 따옴표 경로(한글 · `"scripts/\355…"`)를 못 거르던 길 · -z 목록은 한글 그대로
guard('T-Z20 [3-V] B3 적용 뒤 검사 따옴표 경로 · -z 목록', () => {
  const q = PLAN.checkPaths('src/a.cs\n"scripts/\\355\\225\\234.js"\n');
  const qSrc = PLAN.checkPaths('"src/\\355\\225\\234\\352\\270\\200.cs"\n');
  const odd = PLAN.checkPaths('"scripts/\\9zz.js"\n');
  // 고치기 전 계획(함수 없음)에서도 따옴표 경로 판정(위 q)이 실패 사유로 보이게 — 함수 없음 = 그 줄 실패
  const has = typeof PLAN.unquoteGitPath === 'function' && typeof PLAN.checkPathsZ === 'function';
  const none = { ok: null, why: '함수 없음' };
  const un = has ? PLAN.unquoteGitPath('"scripts/\\355\\225\\234.js"') : '함수 없음';
  const z = has ? PLAN.checkPathsZ(Buffer.from('src/a.cs\0scripts/한.js\0', 'utf8')) : none;
  const zSrc = has ? PLAN.checkPathsZ(Buffer.from('src/한글.cs\0', 'utf8')) : none;
  const zGh = has ? PLAN.checkPathsZ(Buffer.from('.github/workflows/x.yml\0', 'utf8')) : none;
  check('T-Z20 [3-V] B3 적용 뒤 검사 따옴표 경로 · -z 목록',
    !q.ok && qSrc.ok && !odd.ok && un === 'scripts/한.js' && z.ok === false && zSrc.ok === true && zGh.ok === false,
    `따옴표 scripts 거절 ${!q.ok} · 따옴표 src 통과 ${qSrc.ok} · 꼴 모름 거절 ${!odd.ok} · 풀기 ${un} · -z scripts 거절 ${z.ok === false} · -z src 통과 ${zSrc.ok === true} · -z .github 거절 ${zGh.ok === false}`);
});

// T-Z21 [3-V] B4 — 기록 칸(S)이 recorded 아닌 채 판정 길로 오면 「기록 없음」 이어도 초록이던 길
guard('T-Z21 [3-V] B4 uiFacts 기본값 = 미측정', () => {
  const sCell = { ...F.plannedCells({ types: ['estimate'] }).cells.find((c) => c.id === 'S.estimate.editor'), status: 'captured' };
  const r = J.judgeCell(sCell, { board: 'main', pcOk: true, factsById: { [sCell.id]: facts([{ words: [] }]) } });
  const rec = J.judgeCell({ ...sCell, status: 'recorded', ui: { coordTableVisible: false, pdfAccept: false } }, { board: 'main', pcOk: true, factsById: {} });
  check('T-Z21 [3-V] B4 uiFacts 기본값 = 미측정', r.cat === C.UNMEASURED && rec.cat === C.NA && /coordTableVisible/.test(rec.why),
    `판정 길 S ${r.cat} · 기록 S ${rec.cat}`);
});

console.log(`인쇄 자체시험 끝 — 통과 ${pass} · 실패 ${fail}`);
process.exitCode = fail === 0 ? 0 : 1;
