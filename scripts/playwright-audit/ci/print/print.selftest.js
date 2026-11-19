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

// ════ 4차(11/19 · 개발명세서 §4-a ⓐ~ⓓ · Z1 런 44434115123) — 차림(setup-api.js) 경우 ════
//   네트워크 0 — fetch 는 가짜 손(경우 끝나면 되돌림) · 화면 손(K.screenLogin 등)도 가짜 · 브라우저 · DB · 앱 0
//   고치기 전 setup-api.js(aea9b892)엔 이 함수들이 없다 → 부르면 던짐 → 그 경우 실패로 센다(guard · guardAsync)
//   가림 등록(L.maskAndRegister)은 GITHUB_ENV 를 잠시 비운 채 부른다 — 자체시험 가짜 값이 CI 다음 단계 검색 대상에 섞이지 않게
const S = require('./setup-api');
const LIB = require('../lib');
const asyncCases = [];
function guardAsync(id, fn) {
  asyncCases.push([id, fn]);
}
async function withFetch(fake, fn) {
  const real = global.fetch;
  global.fetch = fake;
  try {
    return await fn();
  } finally {
    global.fetch = real;
  }
}
function noEnvFile(fn) {
  const had = Object.prototype.hasOwnProperty.call(process.env, 'GITHUB_ENV');
  const old = process.env.GITHUB_ENV;
  delete process.env.GITHUB_ENV;
  try {
    return fn();
  } finally {
    if (had) process.env.GITHUB_ENV = old;
  }
}
const TERMS_403 = '{"error":"forbidden_terms_consent","required_version":"v2.0.0","redirect":"/terms"}'; // TermsConsentMiddleware.cs:100~102 꼴
const k2Main = () => ({ ...F.plannedCells({ types: ['estimate'] }).cells.find((c) => c.id === 'K2.estimate.doc') });
function mdRowFor(reason) {
  const cell = { ...k2Main(), status: 'setup-fail', reason };
  const run = J.judgeRun({ cells: [cell], board: 'main', factsById: {}, positive: F.POSITIVE_CONTROL, probeSelftestOk: true, groups: [] });
  const md = J.toMarkdown({ run: 'Z3', job: 'z1-main-read', board: 'main' }, run, null);
  return { cat: run.rows[0].cat, row: md.split('\n').find((l) => l.startsWith(`| ${cell.id} |`)) || '' };
}

// T-Z22 ⓑ — Z1 사유 「POST /api/partners 응답 403」 뿐(본문 버림) → 403 forbidden_terms_consent 본문이 결과표 md 까지 가나
guardAsync('T-Z22 ⓑ 403 forbidden_terms_consent 본문 → 차림 사유 → 결과표 md', async () => {
  let msg = '';
  await withFetch(async () => new Response(TERMS_403, { status: 403, headers: { 'content-type': 'application/json' } }), async () => {
    try {
      await S.createPartner({ who: 'x', token: null, deviceId: null }, '시험 거래처');
    } catch (e) {
      msg = e instanceof S.SetupError ? e.message : `다른 예외 ${e.name}`;
    }
  });
  // capture.js ① 과 같은 사유 꼴 → 칸(차림 불가) → 판정 → md
  const m = mdRowFor(`기초 자료 차림 실패 — ${msg}`);
  check('T-Z22 ⓑ 403 forbidden_terms_consent 본문 → 차림 사유 → 결과표 md',
    // ⬛ [낡은 줄 · 4차~5차] /^POST \/api\/partners 응답 403 · 본문 「\{"error":"forbidden_terms_consent"/.test(msg) && /content-type=application\/json/.test(msg)
    // 🔴 6차 F4 — 사유 둘째 칸 = 본문 error 칸(forbidden_terms_consent) · 그 뒤 본문 첫 줄 그대로
    /^POST \/api\/partners 응답 403 · forbidden_terms_consent · 본문 「\{"error":"forbidden_terms_consent"/.test(msg) && /content-type=application\/json/.test(msg)
      && m.cat === C.SETUP && /forbidden_terms_consent/.test(m.row) && /content-type=application\/json/.test(m.row),
    // ⬛ [낡은 줄 · 4차~5차] `사유 꼴 ${/응답 403 · 본문/.test(msg)} · md 칸 ${m.cat} · md 에 forbidden_terms_consent ${/forbidden_terms_consent/.test(m.row)} · content-type ${/content-type=/.test(m.row)}`);
    `사유 꼴 ${/응답 403 · forbidden_terms_consent · 본문/.test(msg)} · md 칸 ${m.cat} · md 에 forbidden_terms_consent ${/forbidden_terms_consent/.test(m.row)} · content-type ${/content-type=/.test(m.row)}`); // 🔴 6차 F4
});

// T-Z23 ⓑ — 그림 올리기(사유 = 「응답 401」 뿐이던 길) · www-authenticate · 본문 첫 줄만 · 160자 · 등록값 가림
guardAsync('T-Z23 ⓑ 그림 올리기 401 · www-authenticate · 첫 줄 160자 · 등록값 가림', async () => {
  const planted = 'selftest-z23-planted-value';
  noEnvFile(() => LIB.maskAndRegister('SELFTEST_Z23', planted));
  const text = `{"message":"x ${planted} ${'가'.repeat(300)}"}\n둘째 줄`;
  let msg = '';
  await withFetch(async () => new Response(text, { status: 401, headers: { 'content-type': 'application/json; charset=utf-8', 'www-authenticate': 'Bearer error="invalid_token"' } }), async () => {
    try {
      await S.uploadBackground({ who: 'x', token: 'x', deviceId: null }, Buffer.from([1, 2, 3]), 'a.png');
    } catch (e) {
      msg = e instanceof S.SetupError ? e.message : `다른 예외 ${e.name}`;
    }
  });
  const body = (msg.match(/본문 「([^」]*)」/) || [])[1] || '';
  const noBody = S.failReason('GET /x', S.responseFacts(500, '', {}));
  check('T-Z23 ⓑ 그림 올리기 401 · www-authenticate · 첫 줄 160자 · 등록값 가림',
    /^그림 올리기 응답 401/.test(msg) && /www-authenticate=Bearer error="invalid_token"/.test(msg) && /content-type=application\/json; charset=utf-8/.test(msg)
      && !msg.includes(planted) && body.includes('***') && !msg.includes('둘째 줄') && body.length <= 160 + '…(이하 생략)'.length
      && noBody === 'GET /x 응답 500 · 본문 없음 · content-type=없음',
    `401 ${/응답 401/.test(msg)} · www-authenticate ${/www-authenticate=/.test(msg)} · 가림 ${!msg.includes(planted)} · 둘째 줄 0 ${!msg.includes('둘째 줄')} · 본문 ${body.length}자 · 빈 본문 ${noBody === 'GET /x 응답 500 · 본문 없음 · content-type=없음'}`);
});

// T-Z24 ⓐ — 로그인 응답 표지 → 화면과 같은 문 · 같은 본문(Terms.razor:117~125) · 표지 거짓 = 부름 0 · 동의 실패 = 차림 사유 · 실제 call 길(가짜 fetch)
guardAsync('T-Z24 ⓐ 약관 표지 → 동의 문 · 본문 · 표지 거짓 부름 0 · 실패 사유', async () => {
  const lr = (o) => S.responseFacts(200, JSON.stringify(o), { 'content-type': 'application/json' });
  const s1 = noEnvFile(() => S.loginSession(lr({ accessToken: 'selftest-a1', refreshToken: 'selftest-r1', deviceId: 'dev-1', requiresTermsConsent: true })));
  const s0 = noEnvFile(() => S.loginSession(lr({ accessToken: 'selftest-a0', deviceId: 'dev-0', requiresTermsConsent: false })));
  const sNo = noEnvFile(() => S.loginSession(lr({ accessToken: 'selftest-a2', deviceId: 'dev-2' })));
  let l409 = '';
  try {
    noEnvFile(() => S.loginSession(S.responseFacts(409, '{"message":"busy"}', { 'content-type': 'application/json' })));
  } catch (e) {
    l409 = e instanceof S.SetupError ? e.message : `다른 예외 ${e.name}`;
  }
  const want = { agreeService: true, agreePrivacy: true, agreeSubscription: true, agreeDataOwnership: true, agreeMarketing: false, termsVersion: 'v2.0.0' };
  const calls = [];
  const fake = async (sess, method, p, body, ok) => { calls.push({ method, p, body, ok }); return { consentId: 'c' }; };
  const r1 = await S.ensureTerms(s1, {}, fake);
  const r0 = await S.ensureTerms(s0, {}, fake);
  const one = calls.length === 1 && calls[0].method === 'POST' && calls[0].p === '/api/auth/terms/agree'
    && JSON.stringify(calls[0].body) === JSON.stringify(want) && calls[0].ok.includes(200) && calls[0].ok.includes(201);
  const failRec = {};
  let failMsg = '';
  try {
    await S.ensureTerms(s1, failRec, async () => { throw new S.SetupError('POST /api/auth/terms/agree 응답 400 · 필수 약관'); });
  } catch (e) {
    failMsg = e instanceof S.SetupError ? e.message : `다른 예외 ${e.name}`;
  }
  // 실제 call 길 — 가짜 fetch 가 받은 주소 · 방법 · 본문 · 머리(값은 찍지 않음 · 같은지만)
  let seen = null;
  const rec2 = {};
  await withFetch(async (url, init) => { seen = { url, init }; return new Response('{"consentId":"c"}', { status: 201, headers: { 'content-type': 'application/json' } }); },
    () => S.ensureTerms(s1, rec2));
  const realOk = seen && /\/api\/auth\/terms\/agree$/.test(seen.url) && seen.init.method === 'POST' && JSON.stringify(JSON.parse(seen.init.body)) === JSON.stringify(want)
    && seen.init.headers.Authorization === `Bearer ${s1.token}` && seen.init.headers['X-HitPan-Device-Id'] === 'dev-1' && rec2.ok === true;
  check('T-Z24 ⓐ 약관 표지 → 동의 문 · 본문 · 표지 거짓 부름 0 · 실패 사유',
    s1.terms === true && s0.terms === false && sNo.terms === false && /응답 409 · busy · 본문/.test(l409)
      && one && r1.required === true && r1.asked === true && r1.ok === true && r0.required === false && r0.asked === false && r0.ok === true
      && failRec.ok === false && /^약관 동의 실패 — POST \/api\/auth\/terms\/agree 응답 400/.test(failMsg) && realOk,
    `표지 참 ${s1.terms} · 거짓 ${s0.terms} · 없음 ${sNo.terms} · 로그인 409 사유 ${/응답 409/.test(l409)} · 부름 ${calls.length}(문 · 본문 ${one}) · 실패 rec ${failRec.ok} · 실제 call 길 ${!!realOk}`);
});

// T-Z25 ⓒ — 화면 로그인 판정(순수) · 화면 세션 토큰이 갱신을 따라감 · 응답 지켜보기 = 200 본문 안 읽음 · 409 첫 줄
guardAsync('T-Z25 ⓒ 화면 로그인 판정 · 화면 세션 따라감 · 응답 지켜보기', async () => {
  const v409 = S.screenLoginVerdict({ status: 409, firstLine: '{"message":"busy"}' });
  const vNone = S.screenLoginVerdict({ status: null, err: 'TimeoutError' });
  const vTerms = S.screenLoginVerdict({ status: 200, where: 'terms', path: '/terms', access: 'a' });
  const vNoTok = S.screenLoginVerdict({ status: 200, where: 'main', path: '/', access: null });
  const vOk = S.screenLoginVerdict({ status: 200, where: 'main', path: '/', access: 'a' });
  const tok = { access: 'a1', deviceId: 'd1', masked: 1 };
  const scr = S.screenSession(tok);
  const t1 = scr.token === 'a1' && scr.deviceId === 'd1';
  tok.access = 'a2';
  const t2 = scr.token === 'a2';
  // 응답 지켜보기 — 가짜 문맥 · 가짜 응답(200 은 text() 를 부르면 표지)
  const handlers = [];
  const ctx = { on: (ev, cb) => { if (ev === 'response') handlers.push(cb); } };
  const seen = S.watchLoginResponse(ctx);
  let read200 = 0;
  const res = (url, method, status, text) => ({
    url: () => url, request: () => ({ method: () => method }), status: () => status,
    text: async () => { if (status === 200) read200 += 1; return text; },
  });
  for (const h of handlers) await h(res('http://127.0.0.1:18080/api/auth/me', 'GET', 200, '{}'));
  for (const h of handlers) await h(res('http://127.0.0.1:18080/api/auth/login', 'GET', 405, ''));
  const before = seen.status;
  for (const h of handlers) await h(res('http://127.0.0.1:18080/api/auth/login', 'POST', 409, '{"message":"busy"}\nx'));
  const s409 = seen.status === 409 && seen.firstLine === '{"message":"busy"}';
  const seen2 = S.watchLoginResponse(ctx);
  for (const h of handlers.slice(1)) await h(res('http://127.0.0.1:18080/api/auth/login', 'POST', 200, '{"accessToken":"t"}'));
  await S.waitSeen(seen2, 1000);
  check('T-Z25 ⓒ 화면 로그인 판정 · 화면 세션 따라감 · 응답 지켜보기',
    !v409.ok && v409.why === '화면 로그인 응답 409 · 본문 「{"message":"busy"}」' && !vNone.ok && /못 봄/.test(vNone.why) && !vTerms.ok && /자리 terms@\/terms/.test(vTerms.why)
      && !vNoTok.ok && vOk.ok && t1 && t2 && before === null && s409 && seen2.status === 200 && seen2.firstLine === '' && read200 === 0,
    `409 ${v409.ok} · 못 봄 ${vNone.ok} · 약관 자리 ${vTerms.ok} · 토큰 0 ${vNoTok.ok} · 정상 ${vOk.ok} · 갱신 따라감 ${t2} · 딴 응답 거름 ${before === null} · 409 첫 줄 ${s409} · 200 본문 읽음 ${read200}`);
});

// T-Z26 ⓒ — 넘김 순서(API 로그아웃 → 토큰 · 응답 지켜보기 → 화면 로그인) · 409 = 차림 불가 사유(md) · 통과 = 뒤 차림이 화면 세션 토큰
guardAsync('T-Z26 ⓒ 넘김 순서 · 409 차림 불가 · 뒤 차림 = 화면 세션', async () => {
  const sess = { who: 'print', token: 'selftest-api', deviceId: 'dev-api' };
  const run = async (status, firstLine, access) => {
    const log = [];
    const ctx = {};
    const d = {
      apiLogout: async (s) => { log.push(`apiLogout:${s.who}`); return { ok: true, got: `${s.who} status=200` }; },
      watchTokens: (c, tok) => { log.push('watchTokens'); c.tok = tok; },
      watchLoginResponse: (c) => { log.push('watchLoginResponse'); c.seen = { status: null, firstLine: '', done: Promise.resolve() }; return c.seen; },
      screenLogin: async () => {
        log.push('screenLogin');
        ctx.seen.status = status;
        ctx.seen.firstLine = firstLine;
        if (access) { ctx.tok.access = access; ctx.tok.deviceId = 'dev-scr'; ctx.tok.masked = 1; }
        return status === 200 ? { where: 'main', path: '/' } : { where: null, path: '/login' };
      },
      waitMasked: async () => { log.push('waitMasked'); return 1; },
      waitSeen: S.waitSeen,
    };
    const h = await S.handoffToScreen({ sess, context: ctx, page: {}, loginId: 'ci-parent', password: 'x' }, d);
    return { h, log, ctx };
  };
  const bad = await run(409, '{"message":"busy"}', null);
  const badOrder = bad.log.join('>') === 'apiLogout:print>watchTokens>watchLoginResponse>screenLogin';
  const m = mdRowFor(bad.h.verdict.why);
  const good = await run(200, '', 'selftest-scr');
  const goodOrder = good.log.join('>') === 'apiLogout:print>watchTokens>watchLoginResponse>screenLogin>waitMasked';
  // 뒤 차림(로고 상태 확인)이 화면 세션 토큰 · 기기 번호로 나가나 — 가짜 fetch 가 받은 머리(값은 같은지만)
  let hdr = null;
  await withFetch(async (url, init) => { hdr = init.headers; return new Response('{"hasLogo":true}', { status: 200, headers: { 'content-type': 'application/json' } }); },
    () => S.logoState(good.h.api));
  const viaScreen = hdr && hdr.Authorization === `Bearer ${good.ctx.tok.access}` && hdr.Authorization !== `Bearer ${sess.token}` && hdr['X-HitPan-Device-Id'] === 'dev-scr';
  check('T-Z26 ⓒ 넘김 순서 · 409 차림 불가 · 뒤 차림 = 화면 세션',
    badOrder && !bad.h.verdict.ok && bad.h.api === null && m.cat === C.SETUP && /화면 로그인 응답 409 · 본문 「\{"message":"busy"\}」/.test(m.row)
      && goodOrder && good.h.verdict.ok && good.h.api !== null && good.h.apiLogout.ok === true && !!viaScreen,
    `409 순서 ${badOrder} · 409 md ${m.cat}(${/화면 로그인 응답 409/.test(m.row)}) · 200 순서 ${goodOrder} · 판정 ${good.h.verdict.ok} · 뒤 차림 화면 세션 ${!!viaScreen}`);
});

// ════ 5차(11/19 · 개발명세서 §1-5 ⓔ1~ⓔ6 · Z3 런 44444478932) ════
//   Z3 = 161칸 차림 불가 「양식 차림 실패 — POST /api/settings/form-templates 응답 500」 · API 로그 `CONSTRAINT form_templates.field_coords_json failed`
//   원인 = 장치 본문 fieldCoordsJson '' (화면은 null · FormTemplateEditorDialog.razor:812 · :827) → 출하 DDL CHECK(json_valid) 위반
//   각 경우 = 「고치기 전 장치(f50a1c05)에서 실패 · 고친 판에서 통과」 · 네트워크 · 브라우저 · DB · 앱 0

// T-Z27 ⓔ2 — 차림이 보낼 수 있는 양식 본문 전수(경쟁 · 시험 처음 · 칸 차림마다 · 양식지 거침 · 편집창): JSON 칸 = null 또는 해석되는 배열 · 빈 글자 칸 0
guard('T-Z27 ⓔ2 양식 본문 JSON 칸 = null 또는 배열 · 빈 글자 칸 0', () => {
  const bodies = [];
  const planned = F.plannedCells();
  F.TYPES.forEach((t, i) => {
    bodies.push(['경쟁', F.competitorBody(t)]);
    bodies.push(['시험 처음 · 편집창', F.templateBody(t, i, { paperMode: 'plain' })]);
    const seen = new Set();
    for (const c of planned.cells.filter((x) => x.type === t.key && x.tpl)) {
      const key = JSON.stringify(c.tpl);
      if (seen.has(key)) continue;
      seen.add(key);
      const over = { ...c.tpl, bgPath: c.tpl.bg ? 'form-bg/selftest.png' : '' }; // capture.js ⑥ 과 같은 꼴(그림 없음 = '')
      if (c.tpl.via === 'preprint') bodies.push([`${c.k} 양식지 거침`, F.templateBody(t, i, { ...over, paperMode: 'preprint' })]);
      bodies.push([c.k, F.templateBody(t, i, over)]);
    }
  });
  const bad = [];
  for (const [what, b] of bodies) {
    const empty = Object.keys(b).filter((k) => b[k] === '');
    const fc = b.fieldCoordsJson;
    let fcOk = fc === null;
    if (typeof fc === 'string') {
      try {
        fcOk = Array.isArray(JSON.parse(fc));
      } catch (e) {
        fcOk = false;
        bad.push(`${b.formType}/${what} 좌표 해석 실패 ${e.name}`);
      }
    }
    const bgOk = b.backgroundImageUrl === null || (typeof b.backgroundImageUrl === 'string' && b.backgroundImageUrl.length > 0);
    if (empty.length || !fcOk || !bgOk) bad.push(`${b.formType}/${what}: 빈 글자 칸 ${empty.join(',') || '0'} · 좌표 ${fcOk} · 그림 ${bgOk}`);
  }
  const withCoords = bodies.filter(([, b]) => typeof b.fieldCoordsJson === 'string' && b.fieldCoordsJson.length > 0).length;
  check('T-Z27 ⓔ2 양식 본문 JSON 칸 = null 또는 배열 · 빈 글자 칸 0',
    bodies.length > 0 && withCoords > 0 && bad.length === 0,
    `본문 ${bodies.length} · 좌표 실음 ${withCoords} · 어긋남 ${bad.length}${bad.length ? ` (${bad.slice(0, 2).join(' | ')})` : ''}`);
});

// ── T-Z28 ⓔ3 배선 시험 — capture.js main 을 그대로 돌린다(가짜 K 응답 = 가짜 fetch · 가짜 화면 손 · 메모리 fs · 가짜 브라우저) ──
//   가짜 문(제품 꼴만 흉내): 로그인(약관 표지) · 약관 동의 · 상태 · 세션(로그아웃 토큰 = 401 · SessionValidityMiddleware 꼴)
//   · 약관 미들웨어(미동의 = 403 forbidden_terms_consent) · 양식 저장(CHECK json_valid · uk_form_templates_default · 기본 내림)
//   · 화면 로그인(API PC 세션이 살아 있으면 409 · 한 PC 규칙) · 로고 상태. 기록 = 요청 차례 · 쓴 세션 · 죽은 토큰 사용 수.
const CAPW = require('./capture');
const K35 = require('../kit35');
const WIRE_OUT = path.join(path.sep, 'selftest-wire-out');
function wireWorld(opts = {}) {
  const CUR = 'v2.0.0'; // TermsConsentMiddleware.cs:13
  const st = { n: 0, reqs: [], events: [], sessions: new Map(), agreedVer: null, logo: false, tpls: [], bodies: [], tplPostN: 0, dead: 0, ui: {}, seen: null, tok: null };
  const json = (status, o, extra) => new Response(o === undefined ? '' : JSON.stringify(o), { status, headers: { 'content-type': 'application/json', ...(extra || {}) } });
  const issue = (kind) => {
    st.n += 1;
    const t = `selftest-wire-${kind}-${st.n}`;
    st.sessions.set(t, { kind, alive: true });
    return t;
  };
  const z3 = () => json(500, { error: '서버 오류가 발생했습니다. 관리자에게 아래 번호를 알려주세요.', traceId: 'selftest' });
  const saveTpl = (id, b) => {
    st.bodies.push({ m: id ? 'PUT' : 'POST', body: b });
    if (!b || !b.formType || !b.templateName || !b.paperMode) return json(400, { title: 'One or more validation errors occurred.' });
    const fc = b.fieldCoordsJson;
    if (fc !== null && fc !== undefined) {
      try {
        JSON.parse(fc); // MariaDB json_valid 꼴 — '' = 무효(hitpan_db_clean.sql:1428 CHECK)
      } catch (e) {
        st.events.push(`json_valid 실패 ${e.name}`);
        return z3();
      }
    }
    const row = id ? st.tpls.find((x) => x.templateId === id) : { templateId: `tpl-${st.tpls.length + 1}` };
    if (!row) return json(400, { error: '양식 템플릿을 찾을 수 없습니다.' });
    const next = { ...row, formType: b.formType, templateName: b.templateName, paperMode: b.paperMode, fieldCoordsJson: fc ?? null, backgroundImageUrl: b.backgroundImageUrl ?? null, isDefault: !!b.isDefault, isActive: b.isActive === undefined ? true : !!b.isActive };
    const others = st.tpls.filter((x) => x.templateId !== next.templateId);
    if (next.isDefault) others.filter((x) => x.formType === next.formType).forEach((x) => { x.isDefault = false; }); // FormTemplateService.cs:126~131 · :236~244
    if (others.some((x) => x.formType === next.formType && x.isDefault === next.isDefault && x.isActive === next.isActive)) return z3(); // uk_form_templates_default
    if (id) Object.assign(row, next); else st.tpls.push(next);
    return json(id ? 200 : 201, next);
  };
  const fetchFake = async (url, init = {}) => {
    const u = new URL(url);
    const p = u.pathname;
    const m = String(init.method || 'GET').toUpperCase();
    const h = init.headers || {};
    const auth = h.Authorization || h.authorization || '';
    const tok = auth.startsWith('Bearer ') ? auth.slice(7) : null;
    const s = tok ? st.sessions.get(tok) : null;
    const who = !tok ? 'none' : !s ? 'unknown' : s.alive ? s.kind : `dead-${s.kind}`;
    const rec = { m, p, who, status: null };
    st.reqs.push(rec);
    st.events.push(`${m} ${p} ${who}`);
    const done = (r) => { rec.status = r.status; return r; };
    const body = typeof init.body === 'string' && init.body ? JSON.parse(init.body) : null;
    if (p === '/api/auth/login' && m === 'POST') {
      const t = issue('api');
      return done(json(200, { accessToken: t, refreshToken: `${t}-r`, deviceId: 'dev-api', requiresTermsConsent: st.agreedVer !== CUR }));
    }
    if (!s || !s.alive) {
      if (s) st.dead += 1;
      return done(json(401, undefined, { 'www-authenticate': 'Bearer' }));
    }
    if (p === '/api/auth/logout' && m === 'POST') { s.alive = false; return done(json(200, {})); }
    if (p === '/api/auth/terms/agree' && m === 'POST') { st.agreedVer = body && body.termsVersion; return done(json(201, { consentId: 'c' })); }
    if (p === '/api/auth/terms/status' && m === 'GET') {
      return done(json(200, { hasAgreed: opts.statusSaysNo ? false : st.agreedVer !== null, termsVersion: st.agreedVer, required: ['service', 'privacy', 'subscription', 'data_ownership'] }));
    }
    if (st.agreedVer !== CUR) return done(new Response(TERMS_403, { status: 403, headers: { 'content-type': 'application/json' } }));
    if (p === '/api/partners' && m === 'POST') return done(json(201, { partnerId: 'p-1' }));
    if (p === '/api/items' && m === 'POST') return done(json(201, { id: `item-${st.reqs.length}` }));
    if (p === '/api/settings/form-templates/background' && m === 'POST') return done(json(200, { storedPath: `form-bg/selftest-${st.reqs.length}.png` }));
    if (p === '/api/settings/form-templates' && m === 'POST') {
      st.tplPostN += 1;
      if (opts.firstTplPost500 && st.tplPostN === 1) return done(z3());
      return done(saveTpl(null, body));
    }
    const put = p.match(/^\/api\/settings\/form-templates\/([^/]+)$/);
    if (put && m === 'PUT') return done(saveTpl(decodeURIComponent(put[1]), body));
    if (p === '/api/settings/form-templates' && m === 'GET') {
      const ft = u.searchParams.get('formType');
      return done(json(200, st.tpls.filter((x) => x.formType === ft && x.isActive)));
    }
    if (p === '/api/settings/company-image-state' && m === 'GET') return done(json(200, { logo: st.logo ? 'present' : 'missing', seal: 'missing', header: 'missing' }));
    return done(json(404, { message: `모르는 문 ${m} ${p}` }));
  };
  // 넘김 손(setup-api handoffToScreen 의 d) — API 로그아웃 = 실제 K.apiLogout(가짜 fetch 로 나감) · 화면 로그인 = 한 PC 규칙 흉내
  const handoffD = {
    apiLogout: K35.apiLogout,
    watchTokens: (c, tok) => { st.tok = tok; },
    watchLoginResponse: () => { st.seen = { status: null, firstLine: '', n: 0, done: Promise.resolve() }; return st.seen; },
    screenLogin: async () => {
      st.events.push('SCREEN login');
      const apiAlive = [...st.sessions.values()].some((x) => x.kind === 'api' && x.alive);
      if (opts.screen409 || apiAlive) {
        st.seen.status = 409;
        st.seen.firstLine = '{"message":"다른 PC에서 사용 중입니다."}';
        return { where: null, path: '/login' };
      }
      st.tok.access = issue('screen');
      st.tok.deviceId = 'dev-scr';
      st.tok.masked = 2;
      st.seen.status = 200;
      return st.agreedVer === CUR ? { where: 'main', path: '/' } : { where: 'terms', path: '/terms' };
    },
    waitMasked: async (tok) => tok.masked,
    waitSeen: S.waitSeen,
  };
  const uiStep = (name, ret) => async () => { st.ui[name] = (st.ui[name] || 0) + 1; st.events.push(`UI ${name}`); return typeof ret === 'function' ? ret() : ret; };
  const pdf = () => Buffer.from('%PDF-1.7 selftest');
  const ui = {
    uploadLogo: uiStep('uploadLogo', () => { st.logo = true; }),
    deleteLogo: uiStep('deleteLogo', () => { st.logo = false; }),
    makeDoc: uiStep('makeDoc'),
    makeSalesReturn: uiStep('makeSalesReturn'),
    captureSettings: uiStep('captureSettings', () => ({ status: 200, body: pdf(), ui: {} })),
    captureDoc: uiStep('captureDoc', () => ({ status: 200, body: pdf(), ui: { warnCount: 0 } })),
    editorFacts: uiStep('editorFacts', () => ({ coordTableVisible: false, pdfAccept: false })),
    closeDialogs: uiStep('closeDialogs'),
  };
  const files = new Map();
  const memFs = { mkdirSync: () => undefined, writeFileSync: (f, data) => { files.set(path.normalize(f), data); } };
  const context = { route: async () => undefined, newPage: async () => ({}), on: () => undefined };
  const browser = { newContext: async () => context, close: async () => { st.events.push('browser close'); } };
  const logs = [];
  const deps = {
    K: K35, S: { ...S, handoffToScreen: (a) => S.handoffToScreen(a, handoffD) }, F, PNG: P, fs: memFs, OUT: WIRE_OUT,
    log: (x) => { logs.push(String(x)); }, loadPlaywright: () => ({ chromium: { launch: async () => browser } }), ui,
  };
  return { st, deps, files, logs, fetchFake };
}
const WIRE_ENV = ['GITHUB_ENV', 'PRINT_PATCH_FAIL', 'UI_PW_PARENT', 'UI_LOGIN_PARENT', 'PRINT_RUN', 'PRINT_JOB', 'PRINT_BOARD', 'PRINT_PROBE_OK'];
async function runWire(opts) {
  const w = wireWorld(opts);
  const kept = WIRE_ENV.map((k) => [k, Object.prototype.hasOwnProperty.call(process.env, k), process.env[k]]);
  for (const k of WIRE_ENV) delete process.env[k]; // GITHUB_ENV 비움 = 가짜 토큰이 CI 다음 단계 검색 대상에 안 섞임(noEnvFile 과 같은 까닭)
  process.env.UI_PW_PARENT = 'selftest-wire-pw';
  let code = null;
  try {
    code = await withFetch(w.fetchFake, () => CAPW.main(w.deps));
  } finally {
    for (const [k, had, v] of kept) {
      if (had) process.env[k] = v;
      else delete process.env[k];
    }
  }
  const raw = w.files.get(path.normalize(path.join(WIRE_OUT, 'cells.json')));
  const doc = raw ? JSON.parse(raw) : null;
  const cells = doc ? doc.cells : [];
  const by = (s) => cells.filter((c) => c.status === s).length;
  const ix = (e) => w.st.events.indexOf(e);
  const screenAt = ix('SCREEN login');
  const afterScreen = screenAt < 0 ? [] : w.st.events.slice(screenAt + 1).filter((e) => /^(GET|POST|PUT|DELETE) /.test(e));
  return { ...w, code, doc, cells, by, ix, screenAt, afterScreen };
}

// T-Z28a 정상 — 차례(로그인 → 동의 → 상태 확인 → 기초 자료 → 양식 → API 로그아웃 → 화면 로그인 → 화면 세션만 → 끝 로그아웃 = 화면 세션) · 차림 불가 0 · 본문 = 화면 꼴
guardAsync('T-Z28a ⓔ3 배선 정상 — 차례 · 화면 세션만 · 끝 로그아웃 · 차림 불가 0', async () => {
  const r = await runWire({});
  const e = r.st.events;
  const seq = r.ix('POST /api/auth/login none') >= 0 && r.ix('POST /api/auth/login none') < r.ix('POST /api/auth/terms/agree api')
    && r.ix('POST /api/auth/terms/agree api') < r.ix('GET /api/auth/terms/status api') && r.ix('GET /api/auth/terms/status api') < r.ix('POST /api/partners api')
    && r.ix('POST /api/auth/logout api') > r.ix('POST /api/settings/form-templates api') && r.ix('POST /api/auth/logout api') < r.screenAt;
  const onlyScreen = r.afterScreen.length > 0 && r.afterScreen.every((x) => x.endsWith(' screen'));
  const lastReq = [...e].reverse().find((x) => /^(GET|POST|PUT|DELETE) /.test(x));
  const su = (r.doc && r.doc.setup) || {};
  const t = su.terms || {};
  const noDoor = r.by('no-door');
  const wantCap = r.cells.length - noDoor - 1;
  const badBody = r.st.bodies.filter(({ body }) => Object.values(body).some((v) => v === '') || (typeof body.fieldCoordsJson === 'string' && !Array.isArray(JSON.parse(body.fieldCoordsJson))));
  const ok = r.code === 0 && !!r.doc && seq && onlyScreen && r.st.dead === 0 && lastReq === 'POST /api/auth/logout screen'
    && t.required === true && t.asked === true && t.ok === true && !!t.confirmed && t.confirmed.hasAgreed === true
    && su.apiLogout && su.apiLogout.ok === true && su.screenLogin && su.screenLogin.ok === true && su.logoutEnd && su.logoutEnd.ok === true && /^화면 /.test(su.logoutEnd.got)
    && r.by('setup-fail') === 0 && r.by('capture-fail') === 0 && r.by('unmeasured') === 0 && r.by('pending') === 0
    && r.by('captured') === wantCap && r.by('recorded') === 1 && noDoor === 16 && r.st.bodies.length > 0 && badBody.length === 0;
  const firstFail = r.cells.find((c) => c.status === 'setup-fail' || c.status === 'capture-fail');
  check('T-Z28a ⓔ3 배선 정상 — 차례 · 화면 세션만 · 끝 로그아웃 · 차림 불가 0', ok,
    `차례 ${seq} · 화면 뒤 화면 세션만 ${onlyScreen}(${r.afterScreen.length}) · 죽은 토큰 ${r.st.dead} · 끝 「${lastReq}」 · 약관 ${t.ok}/${!!t.confirmed} · 차림 불가 ${r.by('setup-fail')} · 못 받음 ${r.by('capture-fail')} · PDF ${r.by('captured')}/${wantCap} · 기록 ${r.by('recorded')} · 문 없음 ${noDoor} · 양식 본문 ${r.st.bodies.length}(어긋남 ${badBody.length})${firstFail ? ` · 첫 사유 「${String(firstFail.reason).slice(0, 70)}」` : ''}`);
});

// T-Z28b 화면 로그인 409 — 전 칸 차림 불가(사유 = 판정 글) · 화면 손 0 · 화면 로그인 뒤 요청 0 · 끝 로그아웃 = 연 세션 없음(API 세션은 넘김에서 닫음)
guardAsync('T-Z28b ⓔ3 배선 화면 409 — 차림 불가 · 화면 손 0 · 뒤 요청 0', async () => {
  const r = await runWire({ screen409: true });
  const su = (r.doc && r.doc.setup) || {};
  const live = r.cells.filter((c) => c.status !== 'no-door');
  const allWhy = live.length > 0 && live.every((c) => c.status === 'setup-fail' && /^화면 로그인 응답 409 · 본문 「\{"message":"다른 PC/.test(c.reason || ''));
  const uiN = Object.entries(r.st.ui).filter(([k]) => k !== 'closeDialogs').reduce((a, [, v]) => a + v, 0);
  const ok = r.code === 0 && allWhy && uiN === 0 && r.afterScreen.length === 0 && r.st.dead === 0
    && r.ix('POST /api/auth/logout api') >= 0 && r.ix('POST /api/auth/logout api') < r.screenAt
    && su.logoutEnd && su.logoutEnd.ok === true && su.logoutEnd.got === '연 세션 없음';
  check('T-Z28b ⓔ3 배선 화면 409 — 차림 불가 · 화면 손 0 · 뒤 요청 0', ok,
    `전 칸 사유 409 ${allWhy}(${live.length}) · 화면 손 ${uiN} · 화면 로그인 뒤 요청 ${r.afterScreen.length} · 죽은 토큰 ${r.st.dead} · 끝 「${su.logoutEnd ? su.logoutEnd.got : '-'}」`);
});

// T-Z28c 첫 양식 저장 500 — 그 종 칸 전부 차림 불가(사유 = 「양식 차림 실패 — POST … 응답 500 · 본문 「…」 · content-type」) · 다른 종은 그대로
guardAsync('T-Z28c ⓔ3 배선 첫 양식 저장 500 = 그 종 차림 불가 사유', async () => {
  const r = await runWire({ firstTplPost500: true });
  const su = (r.doc && r.doc.setup) || {};
  const est = r.cells.filter((c) => c.type === 'estimate' && c.status !== 'no-door');
  const rest = r.cells.filter((c) => c.type !== 'estimate' && c.status !== 'no-door');
  // ⬛ [낡은 줄 · 5차] const why = /^양식 차림 실패 — POST \/api\/settings\/form-templates 응답 500 · 본문 「\{"error":"서버 오류/;
  // 🔴 6차 F4 — 사유 둘째 칸 = 본문 error 칸(해석한 한글) · 그 뒤 본문 첫 줄 그대로
  const why = /^양식 차림 실패 — POST \/api\/settings\/form-templates 응답 500 · 서버 오류가 발생했습니다\. 관리자에게 아래 번호를 알려주세요\. · 본문 「\{"error":"서버 오류/;
  const estOk = est.length > 0 && est.every((c) => c.status === 'setup-fail' && why.test(c.reason || '') && /content-type=application\/json/.test(c.reason || ''));
  const restOk = rest.length > 0 && rest.every((c) => c.status === 'captured' || c.status === 'recorded');
  const m = mdRowFor((est[0] && est[0].reason) || '');
  const ok = r.code === 0 && estOk && restOk && su['tpl.estimate'] && su['tpl.estimate'].ok === false && su['tpl.delivery'] && su['tpl.delivery'].ok === true
    // ⬛ [낡은 줄 · 5차] && m.cat === C.SETUP && /응답 500 · 본문/.test(m.row);
    && m.cat === C.SETUP && /응답 500 · 서버 오류가 발생했습니다\. 관리자에게 아래 번호를 알려주세요\. · 본문/.test(m.row); // 🔴 6차 F4
  check('T-Z28c ⓔ3 배선 첫 양식 저장 500 = 그 종 차림 불가 사유', ok,
    `견적서 칸 사유 ${estOk}(${est.length}) · 다른 종 그대로 ${restOk}(${rest.length}) · tpl.estimate ${su['tpl.estimate'] ? su['tpl.estimate'].ok : '-'} · md ${m.cat}`);
});

// T-Z28d ⓔ6 — 동의 2xx 뒤 상태 문이 아직 필요(hasAgreed 거짓) = 전 칸 차림 불가(사유 = 약관 동의 실패 · §2 꼴 ①) · 기초 자료 부름 0
guardAsync('T-Z28d ⓔ6 배선 동의 뒤 상태 아직 필요 = 차림 불가 · 기초 자료 0', async () => {
  const r = await runWire({ statusSaysNo: true });
  const su = (r.doc && r.doc.setup) || {};
  const live = r.cells.filter((c) => c.status !== 'no-door');
  const allWhy = live.length > 0 && live.every((c) => c.status === 'setup-fail' && /^기초 자료 차림 실패 — 약관 동의 실패 — 동의 뒤 GET \/api\/auth\/terms\/status 응답 200 · hasAgreed 참 아님/.test(c.reason || ''));
  const ok = r.code === 0 && allWhy && r.ix('POST /api/partners api') < 0 && su.terms && su.terms.ok === false && su.terms.confirmed && su.terms.confirmed.hasAgreed === false;
  check('T-Z28d ⓔ6 배선 동의 뒤 상태 아직 필요 = 차림 불가 · 기초 자료 0', ok,
    `전 칸 사유 ${allWhy}(${live.length}) · 거래처 부름 ${r.ix('POST /api/partners api') >= 0} · terms.ok ${su.terms ? su.terms.ok : '-'}`);
});

// T-Z29 ⓔ4([3-V] 4차 D2) — message 칸 = 소독 뒤 자르기(120자 경계에 걸친 등록값 앞부분 0) · 상한 · www-authenticate 소독
guardAsync('T-Z29 ⓔ4 D2 message 소독 뒤 자르기 · www-authenticate 소독', async () => {
  const p1 = 'selftest-d2-planted-message-value';
  const p2 = 'selftest-d2-planted-authhdr-value';
  noEnvFile(() => { LIB.maskAndRegister('SELFTEST_D2A', p1); LIB.maskAndRegister('SELFTEST_D2B', p2); });
  // 앞 110자 + 심은 값 → 자른 뒤 소독(4차)이면 120자 안 앞 10자 「selftest-d」 가 안 가려진 채 남는다
  const r1 = S.failReason('POST /x', S.responseFacts(400, JSON.stringify({ message: `${'가'.repeat(110)}${p1} 꼬리` }), { 'content-type': 'application/json' }));
  const long = S.failReason('POST /x', S.responseFacts(400, JSON.stringify({ title: 'y'.repeat(300) }), { 'content-type': 'application/json' }));
  const longMsg = long.split(' · ')[1] || '';
  const r2 = S.failReason('GET /y', S.responseFacts(401, '', { 'www-authenticate': `Bearer error="invalid_token", error_description="${p2}"` }));
  const msgOk = !r1.includes(p1.slice(0, 10)) && (r1.split(' · ')[1] || '').includes('***');
  const capOk = longMsg.startsWith('y'.repeat(120)) && longMsg.length <= 120 + '…(이하 생략)'.length;
  const authOk = !r2.includes(p2) && /www-authenticate=Bearer error="invalid_token", error_description="\*\*\*"/.test(r2);
  check('T-Z29 ⓔ4 D2 message 소독 뒤 자르기 · www-authenticate 소독', msgOk && capOk && authOk,
    `경계 걸친 값 앞부분 0 ${msgOk} · message 상한 ${capOk}(${longMsg.length}자) · www-authenticate 가림 ${authOk}`);
});

// T-Z30 ⓔ5([3-V] 4차 D3) — 로그인 토큰 가림이 상태 검사 **앞**(200 아닌 응답 본문에 토큰이 실려도 사유에 원문 0) · 기기 승인 대기 길도 등록됨
guardAsync('T-Z30 ⓔ5 D3 로그인 토큰 가림 = 상태 검사 앞', async () => {
  const a1 = 'selftest-d3-access-value-0001';
  const r1 = 'selftest-d3-refresh-value-0001';
  const a2 = 'selftest-d3-access-value-0002';
  let m409 = '';
  try {
    noEnvFile(() => S.loginSession(S.responseFacts(409, JSON.stringify({ accessToken: a1, refreshToken: r1, message: 'busy' }), { 'content-type': 'application/json' })));
  } catch (e) {
    m409 = e instanceof S.SetupError ? e.message : `다른 예외 ${e.name}`;
  }
  let mDev = '';
  try {
    noEnvFile(() => S.loginSession(S.responseFacts(200, JSON.stringify({ accessToken: a2, deviceAwaitingApproval: true }), { 'content-type': 'application/json' })));
  } catch (e) {
    mDev = e instanceof S.SetupError ? e.message : `다른 예외 ${e.name}`;
  }
  const ok = /응답 409/.test(m409) && !m409.includes(a1) && !m409.includes(r1) && m409.includes('***')
    && /기기 승인 대기/.test(mDev) && LIB.sanitize(a1) === '***' && LIB.sanitize(r1) === '***' && LIB.sanitize(a2) === '***';
  check('T-Z30 ⓔ5 D3 로그인 토큰 가림 = 상태 검사 앞', ok,
    `409 사유 ${/응답 409/.test(m409)} · 사유에 토큰 원문 0 ${!m409.includes(a1) && !m409.includes(r1)} · 기기 승인 대기 ${/기기 승인 대기/.test(mDev)} · 등록 ${LIB.sanitize(a1) === '***'}/${LIB.sanitize(a2) === '***'}`);
});

// T-Z31 ⓔ6([3-V] 4차 D4) — confirmTerms 단위: 동의 안 불렀으면 부름 0 · 제품 문(GET /api/auth/terms/status) · 아직 필요 · 판 다름 · 문 실패 = 「약관 동의 실패 — …」 · 실제 call 길
guardAsync('T-Z31 ⓔ6 D4 동의 뒤 상태 문 다시 확인', async () => {
  const sess = { who: 'print', token: 'selftest-d4-tok', deviceId: 'dev-d4' };
  const calls = [];
  const fakeWith = (ret) => async (s, method, p, body, okc) => { calls.push({ method, p, body, okc }); if (ret instanceof Error) throw ret; return ret; };
  const r0 = await S.confirmTerms(sess, { asked: false }, fakeWith({ hasAgreed: true }));
  const n0 = calls.length;
  const rOk = await S.confirmTerms(sess, { asked: true, ok: true }, fakeWith({ hasAgreed: true, termsVersion: 'v2.0.0' }));
  const callOk = calls.length === 1 && calls[0].method === 'GET' && calls[0].p === '/api/auth/terms/status' && calls[0].body === undefined && calls[0].okc.length === 1 && calls[0].okc[0] === 200;
  const thrown = async (rec, fake) => {
    try {
      await S.confirmTerms(sess, rec, fake);
      return '던지지 않음';
    } catch (e) {
      return e instanceof S.SetupError ? e.message : `다른 예외 ${e.name}`;
    }
  };
  const recNo = { asked: true };
  const mNo = await thrown(recNo, fakeWith({ hasAgreed: false }));
  const mVer = await thrown({ asked: true }, fakeWith({ hasAgreed: true, termsVersion: 'v2.1.0' }));
  const mDoor = await thrown({ asked: true }, fakeWith(new S.SetupError('GET /api/auth/terms/status 응답 401 · 본문 없음 · content-type=없음')));
  let seen = null;
  const recReal = { asked: true };
  await withFetch(async (url, init) => { seen = { url, init }; return new Response('{"hasAgreed":true,"termsVersion":"v2.0.0"}', { status: 200, headers: { 'content-type': 'application/json' } }); },
    () => S.confirmTerms(sess, recReal));
  const realOk = seen && /\/api\/auth\/terms\/status$/.test(seen.url) && seen.init.method === 'GET' && seen.init.headers.Authorization === `Bearer ${sess.token}` && recReal.confirmed && recReal.confirmed.hasAgreed === true;
  const md = mdRowFor(`기초 자료 차림 실패 — ${mNo}`);
  const ok = n0 === 0 && r0.confirm === '동의 부름 0 — 다시 확인 안 함' && rOk.confirmed.hasAgreed === true && callOk
    && /^약관 동의 실패 — 동의 뒤 GET \/api\/auth\/terms\/status 응답 200 · hasAgreed 참 아님/.test(mNo) && recNo.ok === false
    && /^약관 동의 실패 — 동의 뒤 GET \/api\/auth\/terms\/status 판 v2\.1\.0 ≠ 보낸 판 v2\.0\.0/.test(mVer)
    && /^약관 동의 실패 — 동의 뒤 확인 GET \/api\/auth\/terms\/status 응답 401/.test(mDoor) && !!realOk && md.cat === C.SETUP && /hasAgreed 참 아님/.test(md.row);
  check('T-Z31 ⓔ6 D4 동의 뒤 상태 문 다시 확인', ok,
    `부름 0(안 불렀을 때) ${n0 === 0} · 문 ${callOk} · 아직 필요 ${/hasAgreed 참 아님/.test(mNo)} · 판 다름 ${/판 v2\.1\.0/.test(mVer)} · 문 실패 ${/응답 401/.test(mDoor)} · 실제 call 길 ${!!realOk} · md ${md.cat}`);
});

// ════ 6차(11/19 · 개발명세서 §1-6) — [4] 5차 F4 · [4] 5차 미측정 · [3-V] 5차 E3. 각 경우 = 고치기 전 장치(12972928)에서 빨강 · 고친 판에서 초록 ════
// 제품 JSON 꼴 흉내 — 서버 기본 인코더처럼 ASCII 밖 글자를 `\uXXXX` 로 탈출(Z3 결과표 사유가 탈출 글자로 찍힌 꼴 · 명세서 §1-5-4 6)
const escJson = (o) => JSON.stringify(o).replace(/[\u007f-\uffff]/g, (ch) => `\\u${ch.charCodeAt(0).toString(16).toUpperCase().padStart(4, '0')}`);

// T-Z32 F4 — 제품 오류 본문 `{"error":"…"}`(탈출 꼴) → 사유 둘째 칸 = 해석한 한글 원문 · 등록값 가림 · 소독 뒤 자르기 · message/title 우선 · 글 아닌 error 무시 · 실제 call 길 → 결과표 md
guardAsync('T-Z32 6차 F4 error 칸 읽기 — 한글 원문 · 가림 · 소독 뒤 자르기', async () => {
  const p1 = 'selftest-f4-planted-error-value';
  const p2 = 'selftest-f4-planted-boundary-value';
  noEnvFile(() => { LIB.maskAndRegister('SELFTEST_F4A', p1); LIB.maskAndRegister('SELFTEST_F4B', p2); });
  const ko = '가짜 거절 사유 — 한글 원문';
  const ct = { 'content-type': 'application/json; charset=utf-8' };
  const body1 = escJson({ error: `${ko} ${p1}`, traceId: 'selftest-f4' });
  const r1 = S.failReason('POST /api/settings/form-templates', S.responseFacts(400, body1, ct));
  const seg1 = r1.split(' · ')[1] || '';
  const koOk = body1.includes('\\uAC00') && !body1.includes(ko) && seg1.startsWith(ko) && seg1.includes('***') && !r1.includes(p1);
  // 앞 110자 + 심은 값 → 자른 뒤 소독이면 120자 안 앞 10자가 안 가려진 채 남는다(T-Z29 와 같은 꼴 · error 칸)
  const r2 = S.failReason('POST /x', S.responseFacts(400, escJson({ error: `${'가'.repeat(110)}${p2} 꼬리` }), ct));
  const seg2 = r2.split(' · ')[1] || '';
  const r3 = S.failReason('POST /x', S.responseFacts(400, escJson({ error: '나'.repeat(300) }), ct));
  const seg3 = r3.split(' · ')[1] || '';
  const cutOk = !r2.includes(p2.slice(0, 10)) && seg2.includes('***') && seg3.startsWith('나'.repeat(120)) && seg3.length <= 120 + '…(이하 생략)'.length;
  const r4 = S.failReason('POST /x', S.responseFacts(400, JSON.stringify({ message: 'm-first', error: 'e-second' }), ct));
  const r5 = S.failReason('POST /x', S.responseFacts(400, JSON.stringify({ error: { code: 7 } }), ct));
  const orderOk = r4.split(' · ')[1] === 'm-first' && (r4.split(' · ')[2] || '').startsWith('본문 「') && (r5.split(' · ')[1] || '').startsWith('본문 「') && !r5.includes('[object Object]');
  let msg = '';
  await withFetch(async () => new Response(escJson({ error: '서버 오류가 발생했습니다. 관리자에게 아래 번호를 알려주세요.', traceId: 'selftest' }), { status: 500, headers: { 'content-type': 'application/json' } }), async () => {
    try {
      await S.createPartner({ who: 'x', token: null, deviceId: null }, '시험 거래처');
    } catch (e) {
      msg = e instanceof S.SetupError ? e.message : `다른 예외 ${e.name}`;
    }
  });
  const md = mdRowFor(`기초 자료 차림 실패 — ${msg}`);
  const realOk = /^POST \/api\/partners 응답 500 · 서버 오류가 발생했습니다\. 관리자에게 아래 번호를 알려주세요\. · 본문 「\{"error":"\\uC11C/.test(msg)
    && md.cat === C.SETUP && md.row.includes('서버 오류가 발생했습니다');
  check('T-Z32 6차 F4 error 칸 읽기 — 한글 원문 · 가림 · 소독 뒤 자르기', koOk && cutOk && orderOk && realOk,
    `한글 원문 · 가림 ${koOk}(「${seg1.slice(0, 24)}」) · 소독 뒤 자르기 · 상한 ${cutOk}(${seg3.length}자) · message 우선 · 글 아닌 error 무시 ${orderOk} · 실제 call 길 → md ${realOk}(${md.cat})`);
});

// T-Z33 미측정 — 캡처 단계가 시간 상한에서 끊겨 남은 pending 칸 = 「미측정(시간 상한 · 차림 순서 못 끝냄)」 · 초록 · 빨강 0 · 끝 줄(md 끝 줄 = 콘솔 끝 줄)에 수 · 합격 불가 · 양성 대조 실패여도 같은 셈
guard('T-Z33 6차 남은 pending = 미측정(시간 상한) · 끝 줄 수 · 합격 불가', () => {
  const planned = F.plannedCells({ types: ['estimate'] });
  const st = { ...planned.cells.find((c) => c.id === 'ST-basic.estimate.doc'), status: 'captured' };
  const left = planned.cells.filter((c) => c.id !== st.id && !c.noDoorAtMain && c.path !== 'editor').slice(0, 3).map((c) => ({ ...c, status: 'pending' }));
  const fb = { ...pcFacts, [st.id]: styleGood(F.STYLES[0]) };
  const base = { board: 'merged', positive: F.POSITIVE_CONTROL, groups: [] };
  const only = J.judgeRun({ ...base, cells: [st], factsById: fb, probeSelftestOk: true });
  const run = J.judgeRun({ ...base, cells: [st, ...left], factsById: fb, probeSelftestOk: true });
  const runPc = J.judgeRun({ ...base, cells: [st, ...left], factsById: fb, probeSelftestOk: false });
  const pRows = run.rows.filter((r) => left.some((c) => c.id === r.id));
  const line = J.endLine(run);
  const mdLast = J.toMarkdown({ run: 'Z4', job: 'x', board: 'merged' }, run, null).split('\n').pop();
  const want = `미측정 ${left.length}(그중 시간 상한 · 차림 순서 못 끝냄 ${left.length})`;
  const cntOk = left.length === 3 && pRows.length === 3 && pRows.every((r) => r.cat === C.UNMEASURED && /시간 상한 · 차림 순서 못 끝냄/.test(r.why))
    && run.counts[C.UNMEASURED] === 3 && run.counts[C.GREEN] === 1 && run.counts[C.RED] === 0 && run.pendingLeft === 3;
  const ok = only.pass === true && cntOk && run.pass === false && line.includes(want) && mdLast === line
    && runPc.pendingLeft === 3 && runPc.pass === false && /그중 시간 상한 · 차림 순서 못 끝냄 0\)/.test(J.endLine(only));
  check('T-Z33 6차 남은 pending = 미측정(시간 상한) · 끝 줄 수 · 합격 불가', ok,
    `대조(pending 0) 합격 ${only.pass} · pending ${left.length} → 미측정 ${run.counts[C.UNMEASURED]} · 초록 ${run.counts[C.GREEN]} · 빨강 ${run.counts[C.RED]} · 셈 ${run.pendingLeft} · 합격 ${run.pass} · 끝 줄 수 ${line.includes(want)} · md 끝 줄 = 끝 줄 ${mdLast === line} · 양성 대조 실패 셈 ${runPc.pendingLeft}`);
});

// T-Z34 E3 — 동의 뒤 확인 기록 글 = 「동의 행 기록 확인 · 기록된 판 vX(요구 판 대조 아님)」 · 동작 무변(통과 · 기록 칸 그대로 · 던짐 길 = T-Z31)
guardAsync('T-Z34 6차 E3 동의 뒤 확인 기록 글 = 요구 판 대조 아님', async () => {
  const sess = { who: 'print', token: 'selftest-e3-tok', deviceId: 'dev-e3' };
  const rec = await S.confirmTerms(sess, { asked: true, ok: true }, async () => ({ hasAgreed: true, termsVersion: 'v2.0.0' }));
  const recNoVer = await S.confirmTerms(sess, { asked: true, ok: true }, async () => ({ hasAgreed: true }));
  const ok = rec.confirm === '동의 뒤 GET /api/auth/terms/status 200 · 동의 행 기록 확인 · 기록된 판 v2.0.0(요구 판 대조 아님)'
    && !/hasAgreed 참 · 판/.test(rec.confirm) && rec.ok === true && rec.confirmed.hasAgreed === true && rec.confirmed.termsVersion === 'v2.0.0'
    && recNoVer.confirm === '동의 뒤 GET /api/auth/terms/status 200 · 동의 행 기록 확인 · 기록된 판 칸 없음(요구 판 대조 아님)' && recNoVer.ok === true;
  check('T-Z34 6차 E3 동의 뒤 확인 기록 글 = 요구 판 대조 아님', ok, `글 「${String(rec.confirm).slice(0, 80)}」 · 판 없음 글 ${recNoVer.confirm.endsWith('칸 없음(요구 판 대조 아님)')} · 통과 그대로 ${rec.ok === true && recNoVer.ok === true}`);
});

// ⬛ [낡은 끝 줄 2 · 1~3차-b] console.log(`인쇄 자체시험 끝 — 통과 ${pass} · 실패 ${fail}`); process.exitCode = fail === 0 ? 0 : 1;
// 🔴 4차 — 비동기 경우(가짜 fetch)를 차례로 끝낸 뒤 같은 끝 줄
(async () => {
  for (const [id, fn] of asyncCases) {
    try {
      await fn();
    } catch (e) {
      check(id, false, `예외 ${e.name}: ${String(e.message).slice(0, 80)}`);
    }
  }
  console.log(`인쇄 자체시험 끝 — 통과 ${pass} · 실패 ${fail}`);
  process.exitCode = fail === 0 ? 0 : 1;
})();
