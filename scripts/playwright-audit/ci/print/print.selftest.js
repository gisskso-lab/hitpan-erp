'use strict';
// W-G1-51 갈래 Z — 인쇄 측정 장치 자체시험 (node 만 · 브라우저 · DB · 네트워크 · 앱 0)
//   덮는 것: 판정기(칸 판정 · 양성 대조 · 음성 무효화 · main 멈춤 · ⑨ 셈 S1 · S2 · S3 · 검출 규칙) · 차림 정의(7종 · N=5 · 5mm 밀기 · 그림 해시)
//            · PNG 만들기(결정적 · 4분면 순서) · 런 계획(plan.json 꼴 · 잡 이름 · 패치 경로) · 워크플로 ui-print 잡 지킴 글자
//   못 덮는 것(미측정): 실제 화면 차림 · 실제 PDF 판독(그건 PrintProbe selftest T1~T6 · CI 런)
//   쓰는 법: node print.selftest.js   — 끝 줄 「인쇄 자체시험 끝 — 통과 n · 실패 m」 · 실패 1 이상이면 끝 코드 1
//   🔴 8차(명세서 §1-8) — T-C1a · T-C1b 는 capture.js 를 자식 프로세스로 돌린다: 나가는 연결 = 127.0.0.1 의 닫힌 포트 시도만(밖 네트워크 · 브라우저 · DB · 앱 0 · 관찰 손이 셈)
//   🔴 9차(명세서 §1-9) — 관찰 손은 허용 주소(그 닫힌 포트) 밖 연결을 기록한 뒤 던진다(연결 0) · T-C1d 는 이 시험이 127.0.0.1 에 연 포트로 막힘을 확인(제품 포트 · DB 아님)
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
    // 🔴 11차 P2-1(고침 1 PM 결재 L2) — 재고 선행 문(opts.stock 일 때만 · 활성 창고 1 · 조정 본문 기록 · 잔량 = 조정 값) · 없으면 옛 꼴 그대로(404)
    if (opts.stock && p === '/api/warehouses' && m === 'GET') return done(json(200, [{ warehouseId: 'wh-wire-1', isActive: true }]));
    if (opts.stock && p === '/api/stock/adjust' && m === 'POST') { (st.adj = st.adj || []).push(body); return done(json(200, { itemId: body && body.itemId })); }
    if (opts.stock && p === '/api/stock/balance' && m === 'GET') return done(json(200, (st.adj || []).map((b) => ({ itemId: b.itemId, warehouseId: b.warehouseId, currentQty: b.actualQty }))));
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
  if (opts.uiOverride) opts.uiOverride(ui, st); // 🔴 7차 C7(T-Z37) — 심은 실패 손
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

// T-Z28e 11차 P2-1(고침 1 L2) — main 재고 선행 배선: 창고 = 목록 id · 조정 수량 = 표지 최대 × 10 · doc.setup.stock.ok · 판매 반품 손이 받은 값 = doc.setup.stock
//   살아남았던 변이 셋(창고 '' · 수량 × 0 · 넘김 값 { ok: true })을 잡는다(리뷰서 §2-2 V-D8⑥ · X5 · X6)
const TZ28E = 'T-Z28e 11차 P2-1 배선 재고 선행 — 창고 = 목록 id · 수량 = 표지 최대 × 10 · stock.ok · 판매 반품 손이 받은 값 = doc.setup.stock';
guardAsync(TZ28E, async () => {
  const got = { n: 0, stock: undefined };
  const r = await runWire({ stock: true, uiOverride: (ui) => { const f = ui.makeSalesReturn; ui.makeSalesReturn = async (page, nos, stock) => { got.n += 1; got.stock = stock; return f(); }; } });
  const stock = (r.doc && r.doc.setup && r.doc.setup.stock) || {};
  const want = Math.max(...F.MARK.items.map((x) => Number(x.qty) || 0)) * 10;
  const adj = r.st.adj || [];
  const bodyOk = adj.length === F.MARK.items.length && adj.every((b) => b.warehouseId === 'wh-wire-1' && b.actualQty === want && !!b.itemId);
  const passOk = got.n === 1 && JSON.stringify(got.stock) === JSON.stringify(stock);
  const ok = r.code === 0 && stock.ok === true && stock.need === want && bodyOk && passOk;
  check(TZ28E, ok, `stock.ok ${stock.ok}(${String(stock.why || '').slice(0, 50)}) · need ${stock.need}/${want} · 조정 ${adj.map((b) => `${b.warehouseId}×${b.actualQty}`).join(' · ') || '0'} · 판매 반품 손 ${got.n}회 · 받은 값 같음 ${passOk}`);
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

// ════ 7차(11/19 · CTO 재판정 §5 · R-2 · R-3 · R-4 · C4 · C5 · C7) ════
const CP = require('child_process');
const OS = require('os');
const REPO7 = path.join(__dirname, '..', '..', '..', '..');
const YML7 = path.join(REPO7, '.github', 'workflows', 'ui-browser.yml');
function printJob7() {
  const yml = fs.existsSync(YML7) ? fs.readFileSync(YML7, 'utf8').split('\r\n').join('\n') : '';
  const a = yml.indexOf('\n  ui-print:');
  return a < 0 ? '' : yml.slice(a);
}
/** 단계 이름으로 run: | 덩이를 꺼냄(들여쓰기 걷음). */
function stepRun7(job, name) {
  const L7 = job.split('\n');
  const i = L7.findIndex((l) => l.includes(`- name: "${name}`));
  if (i < 0) return null;
  const j = L7.findIndex((l, k) => k > i && /^\s+run: \|\s*$/.test(l));
  if (j < 0) return null;
  const out = [];
  let ind = null;
  for (let k = j + 1; k < L7.length; k++) {
    const l = L7[k];
    if (l.trim() === '') { out.push(''); continue; }
    const n = l.length - l.trimStart().length;
    if (ind === null) ind = n;
    if (n < ind) break;
    out.push(l.slice(ind));
  }
  return out.join('\n');
}
function bash7() {
  if (process.env.PRINT_BASH) return process.env.PRINT_BASH;
  if (process.platform === 'win32') {
    const g = 'C:\\Program Files\\Git\\bin\\bash.exe';
    if (fs.existsSync(g)) return g; // WSL bash.exe(System32) 를 피함
  }
  return 'bash';
}
/** 실제 단계 덩이를 GitHub 기본 셸 꼴(bash -e)로 돌림 · dotnet = 가짜(이름이 bad 로 시작하면 끝 코드 3). */
function runReadStep7(block, names) {
  const root = fs.mkdtempSync(path.join(OS.tmpdir(), 'w51-s7-'));
  const out = path.join(root, 'out');
  fs.mkdirSync(path.join(out, 'pdfs'), { recursive: true });
  for (const n of names) fs.writeFileSync(path.join(out, 'pdfs', n), '%PDF-1.7 selftest');
  const bin = path.join(root, 'bin');
  fs.mkdirSync(bin);
  const fake = '#!/usr/bin/env bash\n# 가짜 dotnet — <dll> read <pdf> <out>\necho "$3" >> "$FAKE_LOG"\ncase "$(basename "$3")" in bad*) echo "가짜 판독기 예외" >&2; exit 3;; esac\nprintf \'{\\n  "ok": true\\n}\\n\' > "$4"\n';
  fs.writeFileSync(path.join(bin, 'dotnet'), fake, { mode: 0o755 });
  const script = path.join(root, 'step.sh');
  fs.writeFileSync(script, block + '\n');
  const log = path.join(root, 'calls.log');
  const env = { ...process.env, PATH: `${bin}${path.delimiter}${process.env.PATH}`, RUNNER_TEMP: root, PRINT_OUT: out, FAKE_LOG: log };
  const r = CP.spawnSync(bash7(), ['--noprofile', '--norc', '-e', script], { env, encoding: 'utf8', timeout: 60000 });
  const calls = fs.existsSync(log) ? fs.readFileSync(log, 'utf8').split('\n').filter(Boolean).length : 0;
  const moved = fs.existsSync(path.join(root, 'print-unread')) ? fs.readdirSync(path.join(root, 'print-unread')) : [];
  const res = { code: r.status, out: `${r.stdout || ''}`, err: `${r.stderr || ''}${r.error ? r.error.message : ''}`, calls, moved };
  fs.rmSync(root, { recursive: true, force: true });
  return res;
}

// T-W2 C4 글자 셈 — ui-print 잡(주석 줄 뺌)에 `|| echo` · `|| true` · `|| :` · continue-on-error 0 · PrintProbe.dll 부르는 줄에 `||` 0
//   · 사실 뽑기 단계 = 실패 수 셈 + 1 이상이면 exit 1 꼴
{
  const job = printJob7();
  const live = job.split('\n').filter((l) => l.trim() !== '' && !l.trim().startsWith('#'));
  // ⬛ [낡은 줄 · 7차] const swallow = live.filter((l) => /\|\|\s*(echo|true|:)(\s|$|;|\))/.test(l) || /continue-on-error/.test(l));
  // 🔴 11차(고침 2 PM 결재 ① · L5) — 글꼴 단계 안의 continue-on-error 1줄만 뺌(그 단계 실패는 판정기가 outcome · 사유로 드러냄 = T-FG1 · 잡 전체 1개 = T-FG3) · 다른 자리는 그대로 셈
  const jl = job.split('\n');
  const fi = jl.findIndex((l) => l.includes('- name: "[장치] 측정 글꼴'));
  const fontCoe = fi < 0 ? null : jl.slice(fi, jl.findIndex((l, k) => k > fi && /^\s+- name: /.test(l))).find((l) => /^\s+continue-on-error: true\s*$/.test(l));
  let allowCoe = fontCoe ? 1 : 0;
  const swallow = live.filter((l) => {
    if (allowCoe && l === fontCoe) { allowCoe = 0; return false; }
    return /\|\|\s*(echo|true|:)(\s|$|;|\))/.test(l) || /continue-on-error/.test(l);
  });
  const probeLines = live.filter((l) => /PrintProbe\.dll/.test(l));
  const probeOr = probeLines.filter((l) => /\|\|/.test(l));
  const read = stepRun7(job, '[시험] PDF 사실 뽑기') || '';
  const readShape = /if ! dotnet "\$RUNNER_TEMP\/probe\/PrintProbe\.dll" read "\$f" "\$\{f%\.pdf\}\.json"; then bad=\$\(\(bad \+ 1\)\); echo "⚫ FAIL \[장치\] 판독기 실패/.test(read)
    && /if \[ "\$bad" -gt 0 \]; then .*⚫ FAIL \[장치\] 사실 뽑기 실패 \$\{bad\}\/\$\{n\}"; exit 1; fi/.test(read);
  check('T-W2 7차 C4 ui-print 잡 조용한 실패 글자 0 · 판독기 줄 || 0 · 사실 뽑기 = 실패 수 → exit 1 꼴',
    job.length > 0 && swallow.length === 0 && probeLines.length >= 3 && probeOr.length === 0 && readShape,
    `잡 ${job.length > 0} · 삼킴 꼴 ${swallow.length}${swallow.length ? `(${swallow.map((l) => l.trim().slice(0, 50)).join(' / ')})` : ''} · 판독기 줄 ${probeLines.length}(|| ${probeOr.length}) · 사실 뽑기 꼴 ${readShape}`);
}

// T-W3 C4 동작 — 실제 「[시험] PDF 사실 뽑기」 덩이를 bash -e 로: 판독기 실패 1 심으면 단계 실패 · 3장 다 시도 · ⚫ 줄 · 그 PDF 만 업로드 제외 / 대조(실패 0) = 성공
//   + 고치기 전 꼴(6차 줄 · echo 대체)을 같은 셸로 돌려 「삼킴」 재현(단계 성공) — 시험이 그 꼴을 가르는지 보임
{
  const block = stepRun7(printJob7(), '[시험] PDF 사실 뽑기');
  if (!block) {
    check('T-W3 7차 C4 판독기 실패 심음 → 단계 실패(동작)', false, '단계 덩이 못 찾음');
  } else {
    const bad = runReadStep7(block, ['a.pdf', 'bad-1.pdf', 'c.pdf']);
    const good = runReadStep7(block, ['a.pdf', 'c.pdf']);
    const old = ['set -uo pipefail', 'shopt -s nullglob', 'mkdir -p "$RUNNER_TEMP/print-unread"', 'for f in "$PRINT_OUT"/pdfs/*.pdf; do',
      '  dotnet "$RUNNER_TEMP/probe/PrintProbe.dll" read "$f" "${f%.pdf}.json" || echo "판독기 실패 $(basename "$f")"',
      '  if ! grep -q \'^  "ok": true\' "${f%.pdf}.json" 2>/dev/null; then mv "$f" "$RUNNER_TEMP/print-unread/"; echo "못 읽은 PDF 업로드 제외 $(basename "$f")"; fi', 'done'].join('\n');
    const oldBad = runReadStep7(old, ['a.pdf', 'bad-1.pdf', 'c.pdf']);
    const ok = bad.code !== 0 && bad.code !== null && bad.calls === 3 && /⚫ FAIL \[장치\] 판독기 실패 bad-1\.pdf/.test(bad.out) && /⚫ FAIL \[장치\] 사실 뽑기 실패 1\/3/.test(bad.out)
      && bad.moved.length === 1 && bad.moved[0] === 'bad-1.pdf'
      && good.code === 0 && good.calls === 2 && !/⚫/.test(good.out) && /사실 뽑기 끝 — PDF 2 · 판독기 실패 0/.test(good.out)
      && oldBad.code === 0;
    check('T-W3 7차 C4 판독기 실패 심음 → 단계 실패(동작)', ok,
      `심음: 끝 코드 ${bad.code} · 시도 ${bad.calls}/3 · ⚫ 판독기 줄 ${/⚫ FAIL \[장치\] 판독기 실패 bad-1/.test(bad.out)} · ⚫ 수 줄 ${/사실 뽑기 실패 1\/3/.test(bad.out)} · 업로드 제외 [${bad.moved.join(',')}] / 대조: 끝 코드 ${good.code} · 시도 ${good.calls} · ⚫ ${/⚫/.test(good.out)} / 고치기 전 꼴: 끝 코드 ${oldBad.code}(0 = 삼킴 재현)${bad.err && bad.code === null ? ` · ${bad.err.slice(0, 80)}` : ''}`);
  }
}

// T-Z35 7차 R-3 · C5 — 장치가 찾는 수량 머리 ↔ 제품 .razor MudTh 원문 정적 대조(경로 = 화면 주소 → @page 쪽 → 그 쪽의 …Grid 부품 → MudTh)
//   fillLines 를 지나는 종 = F.TYPES 중 판매 반품(=[반품하기] 길) 뺀 전부. 옛 맞춤(「수량」 정확 일치)은 매입 반품에서 -1 = 빨강이어야 함
guard('T-Z35 7차 R-3 · C5 수량 머리 ↔ .razor MudTh 정적 대조 · 옛 맞춤 매입 반품 빨강', () => {
  const CAPR = require('./capture');
  const web = path.join(REPO7, 'src', 'HitPan.Web');
  const files = [];
  const walk = (d) => {
    if (!fs.existsSync(d)) return;
    for (const e of fs.readdirSync(d, { withFileTypes: true })) {
      if (e.isDirectory()) { if (e.name !== 'obj' && e.name !== 'bin') walk(path.join(d, e.name)); } else if (e.name.endsWith('.razor')) files.push(path.join(d, e.name));
    }
  };
  walk(web);
  const text = new Map(files.map((f) => [f, fs.readFileSync(f, 'utf8')]));
  const pageOf = (route) => files.find((f) => text.get(f).includes(`@page "${route}"`));
  const comp = (name) => files.find((f) => path.basename(f) === `${name}.razor`);
  const heads = (src) => [...src.matchAll(/<MudTh[^>]*>([^<]*)<\/MudTh>/g)].map((m) => m[1].trim()).concat([...src.matchAll(/<th[^>]*>([^<]*)<\/th>/g)].map((m) => m[1].trim()));
  const rows = F.TYPES.filter((t) => t.key !== 'sales_return').map((t) => {
    const pg = pageOf(t.route);
    const grids = pg ? [...new Set([...text.get(pg).matchAll(/<([A-Z]\w*Grid)\b/g)].map((m) => m[1]).filter((n) => n !== 'MudGrid'))] : [];
    const hs = grids.flatMap((g) => (comp(g) ? heads(text.get(comp(g))) : []));
    return { key: t.key, grids, hs, now: CAPR.qtyColumn(hs), old: hs.findIndex((h) => h.trim() === '수량') };
  });
  const allNow = rows.length >= 6 && rows.every((r) => r.grids.length > 0 && r.now >= 0);
  const pr = rows.find((r) => r.key === 'purchase_return') || {};
  const oldRed = pr.old === -1 && pr.now >= 0 && pr.hs && pr.hs[pr.now] === '반품수량';
  const mutate = CAPR.qtyColumn(['No', '품명', '반품 수량']) === -1 && CAPR.qtyColumn(['No', '수량합계']) === -1; // 띄어쓰기 · 부분 일치는 안 잡음(정확 일치)
  check('T-Z35 7차 R-3 · C5 수량 머리 ↔ .razor MudTh 정적 대조 · 옛 맞춤 매입 반품 빨강',
    files.length > 0 && allNow && oldRed && mutate,
    `.razor ${files.length} · ${rows.map((r) => `${r.key}=${r.grids.join('+') || '부품 없음'}:${r.now >= 0 ? r.hs[r.now] : '없음'}`).join(' · ')} · 옛 맞춤 매입 반품 ${pr.old}(빨강 ${oldRed}) · 띄어쓰기·부분 일치 거름 ${mutate}`);
});

// T-Z36 7차 R-4 — 목록 줄 기다림 · 못 찾으면 글자 진단(줄 수 · 기간 값 · 시간대 · 창 글 소독) · 사진 0 · 기다림 상한 = LIST_WAIT
guardAsync('T-Z36 7차 R-4 목록 줄 기다림 · 글자 진단 · 사진 0', async () => {
  const CAPR = require('./capture');
  const t = F.TYPES.find((x) => x.key === 'delivery');
  const mk = (appear) => {
    const st = { shots: 0, timeout: null, waited: 0 };
    let seen = false;
    const marked = {
      first: () => ({ waitFor: async ({ timeout }) => { st.timeout = timeout; st.waited += 1; if (!appear) { const e = new Error('Timeout 10000ms exceeded'); e.name = 'TimeoutError'; throw e; } seen = true; } }),
      count: async () => (appear && seen ? 1 : 0),
    };
    const trs = { filter: () => marked, count: async () => 3 };
    const dlg = {
      locator: (sel) => (sel === 'tr' ? trs : { evaluateAll: async (fn) => fn([{ value: '2026-11-01' }, { value: '2026-11-19' }, { value: '' }]) }),
      innerText: async () => '판매 목록 조회\n기간\t2026-11-01 ~ 2026-11-19\n조회 결과가 없습니다',
      screenshot: async () => { st.shots += 1; },
    };
    const page = { evaluate: async () => 'UTC · 분 차 0', screenshot: async () => { st.shots += 1; } };
    return { st, dlg, page };
  };
  const a = mk(true);
  const rowsA = await CAPR.waitListRows(a.page, a.dlg, t, (r) => r);
  const okA = (await rowsA.count()) === 1 && a.st.waited === 1 && a.st.timeout === CAPR.LIST_WAIT;
  const b = mk(false);
  let msg = '';
  let isSetup = false;
  try {
    await CAPR.waitListRows(b.page, b.dlg, t, (r) => r);
  } catch (e) {
    msg = String(e.message);
    isSetup = e instanceof S.SetupError;
  }
  const okB = isSetup && msg.includes('판매 목록 조회 에 표지 거래처 줄 없음') && msg.includes('줄 수 3(표지 줄 0)') && msg.includes('기간 값 2026-11-01 ~ 2026-11-19')
    && msg.includes('브라우저 시간대 UTC · 분 차 0') && msg.includes('창 글 「판매 목록 조회 기간 2026-11-01 ~ 2026-11-19 조회 결과가 없습니다」') && !/[\u0000-\u001f]/.test(msg);
  check('T-Z36 7차 R-4 목록 줄 기다림 · 글자 진단 · 사진 0', okA && okB && a.st.shots === 0 && b.st.shots === 0 && CAPR.LIST_WAIT <= 10000,
    `나타남: 셈 1 · 기다림 ${a.st.waited} · 상한 ${a.st.timeout} / 안 나타남: 차림 불가 ${isSetup} · 진단 「${msg.slice(0, 160)}」 · 사진 ${a.st.shots + b.st.shots}`);
});

// T-Z37 7차 C7 폭발 반경 — 매입 반품 전표 차림 실패(R-3 꼴) + 거래명세서 목록 실패(R-4 꼴 · 거래명세서 · 판매 반품 ⓘⓙ)를 심어 capture main(가짜 손)을 돌리고
//   그 결과 cells.json 을 실제 판정기 진입점(node judge-print.js <dir>)으로 판정 → 그 칸만 차림 불가 · 양성 대조 두 칸 captured · 양성 대조 통과
guardAsync('T-Z37 7차 C7 폭발 반경 — R-3 · R-4 실패 = 그 칸만 차림 불가 · 양성 대조 무사', async () => {
  const r = await runWire({
    uiOverride: (ui) => {
      const md = ui.makeDoc;
      ui.makeDoc = async (page, t) => { if (t && t.key === 'purchase_return') throw new S.SetupError('품목표에 수량 머리(수량 · 반품수량) 없음 — 심은 R-3 꼴'); return md(page, t); };
      const cd = ui.captureDoc;
      ui.captureDoc = async (page, t, entry) => {
        if (t && (t.key === 'delivery' || t.key === 'sales_return')) throw new S.SetupError('판매 목록 조회 에 표지 거래처 줄 없음 — 줄 수 3(표지 줄 0) · 심은 R-4 꼴');
        return cd(page, t, entry);
      };
    },
  });
  const pc = F.POSITIVE_CONTROL;
  const cells = r.cells || [];
  const pcCells = cells.filter((c) => c.id === pc.basicForm.cell || c.id === pc.preprint.cell);
  const failed = cells.filter((c) => c.status === 'setup-fail');
  const wrongFail = failed.filter((c) => !['purchase_return', 'delivery', 'sales_return'].includes(c.type));
  const r3 = failed.filter((c) => c.type === 'purchase_return');
  const r4 = failed.filter((c) => c.type === 'delivery' || c.type === 'sales_return');
  // 실제 판정기 진입점으로(디스크 임시 폴더 · 양성 대조 두 칸만 사실 파일)
  const dir = fs.mkdtempSync(path.join(OS.tmpdir(), 'w51-c7-'));
  let judge = { code: null, end: '' };
  let res = null;
  try {
    fs.mkdirSync(path.join(dir, 'pdfs'), { recursive: true });
    for (const c of pcCells) {
      if (!c.pdf) continue;
      const fp = path.join(dir, c.pdf.replace(/\.pdf$/i, '.json'));
      fs.mkdirSync(path.dirname(fp), { recursive: true });
      fs.writeFileSync(fp, JSON.stringify(pcFacts[c.id]));
    }
    fs.writeFileSync(path.join(dir, 'cells.json'), JSON.stringify({ ...r.doc, probeSelftestOk: true }));
    // ⬛ [낡은 줄 · 8차] const j = CP.spawnSync(process.execPath, [path.join(__dirname, 'judge-print.js'), dir], { encoding: 'utf8', timeout: 60000 });
    const jEnv = { ...process.env }; // 🔴 11차 PM 판단 ② — CI 에서 돌아도 이 PC 와 같은 뜻(글꼴 관문 끔 = 이 시험 범위 밖)
    delete jEnv.GITHUB_ACTIONS;
    delete jEnv.PRINT_FONT_OUTCOME;
    const j = CP.spawnSync(process.execPath, [path.join(__dirname, 'judge-print.js'), dir], { encoding: 'utf8', timeout: 60000, env: jEnv });
    judge = { code: j.status, end: String(j.stdout || '').split(/\r?\n/).filter((l) => l.startsWith('판정 끝')).pop() || '' };
    res = JSON.parse(fs.readFileSync(path.join(dir, 'print-result.json'), 'utf8'));
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
  const rowOf = (id) => ((res && res.rows) || []).find((x) => x.id === id) || {};
  const setupRows = ((res && res.rows) || []).filter((x) => x.cat === C.SETUP).length;
  const ok = r.code === 0 && pcCells.length === 2 && pcCells.every((c) => c.status === 'captured') && wrongFail.length === 0 && r3.length > 0 && r4.length > 0
    && judge.code === 0 && /양성 대조 통과/.test(judge.end) && rowOf(pc.basicForm.cell).cat !== C.SETUP && rowOf(pc.preprint.cell).cat !== C.SETUP && setupRows === failed.length;
  check('T-Z37 7차 C7 폭발 반경 — R-3 · R-4 실패 = 그 칸만 차림 불가 · 양성 대조 무사', ok,
    `capture 끝 코드 ${r.code} · 차림 불가 ${failed.length}(매입 반품 ${r3.length} · 거래명세서·판매 반품 ${r4.length} · 그 밖 ${wrongFail.length}) · 양성 칸 ${pcCells.map((c) => c.status).join('/')} · 판정 끝 코드 ${judge.code} · ${judge.end.slice(0, 60)} · 판정 차림 불가 ${setupRows}`);
});

// ════ 8차(11/19 · CTO 8차 판정 §4-1 Z8-1~Z8-4 · 워크플로 무변 · 명세서 §1-8) ════
const NET8 = require('net');
const sleep8 = (ms) => new Promise((ok) => setTimeout(ok, ms));
/** 워크플로 「[시험] 차림 · 판독 입구」 단계의 run: 한 줄 그대로. */
function captureCmd8() {
  const L8 = printJob7().split('\n');
  const i = L8.findIndex((l) => l.includes('- name: "[시험] 차림 · 판독 입구'));
  if (i < 0) return null;
  const r = L8.slice(i + 1, i + 6).find((l) => /^\s+run:\s+\S/.test(l));
  return r ? r.replace(/^\s+run:\s+/, '').trim() : null;
}
/** 127.0.0.1 의 안 쓰는 포트 — 열었다 닫은 뒤 연결이 거절되는지 먼저 확인. */
async function closedPort8() {
  const srv = NET8.createServer();
  await new Promise((ok, no) => { srv.once('error', no); srv.listen(0, '127.0.0.1', ok); });
  const { port } = srv.address();
  await new Promise((ok) => srv.close(ok));
  const refused = await new Promise((ok) => {
    const s = NET8.connect(port, '127.0.0.1');
    s.once('connect', () => { s.destroy(); ok(false); });
    s.once('error', (e) => ok(e.code === 'ECONNREFUSED'));
  });
  return { port, refused };
}
/** 관찰 손 — 자식 프로세스에 NODE_OPTIONS --require 로 실음 · 기록만(동작 무변): 나가는 TCP 연결 · playwright 불러오기 · 자식 프로세스. */
function observer8() {
  const fs0 = require('fs');
  const net0 = require('net');
  const Module0 = require('module');
  const cp0 = require('child_process');
  const put = (s) => fs0.appendFileSync(process.env.W51_TC1_REC, `${s}\n`);
  const con = net0.Socket.prototype.connect;
  // 🔴 9차 [3-V] P3-2 — 기록 뒤 던짐: 허용 주소(W51_TC1_ALLOW = 127.0.0.1:{닫힌 포트} · 비면 전부 막음) 밖 연결은 기록하고 연결 안 함
  //   (장치가 깨져 다른 주소로 새도 이 PC 실물 5257 · 3306 등에 가짜 로그인 시도 0)
  const allow = String(process.env.W51_TC1_ALLOW || '');
  net0.Socket.prototype.connect = function connect8(...a) {
    const o = Array.isArray(a[0]) ? a[0][0] : a[0];
    put(o && typeof o === 'object' ? `connect ${o.host || o.path || '?'}:${o.port || ''}` : `connect ${typeof a[1] === 'string' ? a[1] : '?'}:${o}`);
    // ⬛ [낡은 줄 · 8차] return con.apply(this, a);
    const where = o && typeof o === 'object' ? `${o.host || o.path || '?'}:${o.port || ''}` : `${typeof a[1] === 'string' ? a[1] : '?'}:${o}`;
    if (where !== allow) {
      put(`blocked ${where}`);
      throw Object.assign(new Error(`T-C1 관찰 손 막음 ${where}`), { code: 'W51_TC1_BLOCKED' });
    }
    return con.apply(this, a);
  };
  const load = Module0._load;
  Module0._load = function load8(req, ...a) {
    if (/^(playwright|playwright-core|@playwright\/)/.test(String(req))) put(`require ${req}`);
    return load.call(this, req, ...a);
  };
  for (const k of ['spawn', 'spawnSync', 'exec', 'execSync', 'execFile', 'execFileSync', 'fork']) {
    const f = cp0[k];
    cp0[k] = function child8(...a) { put(`child ${k}`); return f.apply(this, a); };
  }
}
/** capture.js 를 워크플로 명령줄 그대로 자식 프로세스로(작업 폴더 = 레포 뿌리) — 처음부터 새로 짠 최소 환경(실 비밀 · GITHUB_ENV 0) + 시험 값. */
function runCapture8(cmd, extra) {
  const root = fs.mkdtempSync(path.join(OS.tmpdir(), 'w51-tc1-'));
  const out = path.join(root, 'out');
  const rec = path.join(root, 'rec.log');
  const obs = path.join(root, 'observer.js');
  try {
    fs.writeFileSync(obs, `'use strict';\n(${observer8.toString()})();\n`);
    fs.writeFileSync(rec, '');
    const env = {};
    for (const k of ['PATH', 'SystemRoot', 'TEMP', 'TMP', 'TMPDIR']) if (process.env[k]) env[k] = process.env[k];
    Object.assign(env, {
      PRINT_OUT: out, PRINT_RUN: 'selftest', PRINT_JOB: 'tc1', PRINT_BOARD: 'main',
      UI_LOGIN_PARENT: 'selftest-parent', UI_PW_PARENT: 'selftest-tc1-pw',
      NODE_OPTIONS: `--require "${obs.split(path.sep).join('/')}"`, W51_TC1_REC: rec,
    }, extra);
    // 🔴 9차 [3-V] P3-2 — 관찰 손이 허용하는 연결 = UI_BASE(닫힌 포트) 하나 · 그 밖 = 기록 뒤 던짐
    if (!env.W51_TC1_ALLOW) { const u = /^http:\/\/(127\.0\.0\.1:\d+)\/?$/.exec(String(env.UI_BASE || '')); env.W51_TC1_ALLOW = u ? u[1] : ''; }
    const parts = String(cmd || '').split(/\s+/).filter(Boolean);
    const r = parts[0] === 'node' && parts.length === 2
      ? CP.spawnSync(process.execPath, parts.slice(1), { cwd: REPO7, env, encoding: 'utf8', timeout: 90000 })
      : { status: null, stdout: '', stderr: `명령줄 꼴 다름: ${cmd}` };
    const at = path.join(out, 'cells.json');
    const doc = fs.existsSync(at) ? JSON.parse(fs.readFileSync(at, 'utf8')) : null;
    const lines = fs.readFileSync(rec, 'utf8').split('\n').filter(Boolean);
    return { code: r.status, text: `${r.stdout || ''}${r.stderr || ''}${r.error ? r.error.message : ''}`, doc, lines };
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
}
const TC1_B_CODE = 0; // 🔴 명세서 §1-8-0 — ⓑ 끝 코드 기대값(돌리기 전에 적은 값)
const PRODUCT_PORTS8 = [5234, 5257, 3306];
function tc1Facts(r, port) {
  const cells = (r.doc && r.doc.cells) || [];
  const connects = r.lines.filter((l) => l.startsWith('connect '));
  return {
    cells,
    planned: F.plannedCells().cells.length,
    pending: cells.filter((c) => c.status === 'pending').length,
    noReason: cells.filter((c) => !(c.reason || (c.status === 'no-door' && c.noDoorAtMain))).length,
    connects,
    product: connects.filter((l) => PRODUCT_PORTS8.some((p) => l.endsWith(`:${p}`))).length,
    other: connects.filter((l) => !l.endsWith(`127.0.0.1:${port}`)).length,
    pw: r.lines.filter((l) => l.startsWith('require ')).length,
    child: r.lines.filter((l) => l.startsWith('child ')).length,
    dev: /⚫ FAIL \[장치\]/.test(r.text),
  };
}

// T-C1a 8차 Z8-1 ⓐ — capture.js 프로세스 진입점(C1 · C6 ①) · PRINT_PATCH_FAIL=1 → 끝 코드 0 · OUT 에 cells.json · 칸 수 = 계획 · 전 칸 「패치 적용 실패」 · TCP · 브라우저 0
guardAsync('T-C1a 8차 Z8-1 ⓐ capture.js 프로세스(워크플로 명령줄) · PRINT_PATCH_FAIL=1', async () => {
  const cmd = captureCmd8();
  const cp = await closedPort8(); // ⓐ 도 UI_BASE = 닫힌 포트(새는 길이 있어도 기본 주소로 안 감)
  const r = runCapture8(cmd, { PRINT_PATCH_FAIL: '1', UI_BASE: `http://127.0.0.1:${cp.port}` });
  const f = tc1Facts(r, cp.port);
  const allPatch = f.cells.length > 0 && f.cells.every((c) => c.status === 'setup-fail' && c.reason === '패치 적용 실패(git apply)');
  const ok = cmd === 'node scripts/playwright-audit/ci/print/capture.js' && r.code === 0 && r.doc !== null && f.cells.length === f.planned && allPatch && f.pending === 0
    && f.connects.length === 0 && f.pw === 0 && f.child === 0 && !f.dev;
  check('T-C1a 8차 Z8-1 ⓐ capture.js 프로세스(워크플로 명령줄) · PRINT_PATCH_FAIL=1', ok,
    `명령줄 「${cmd}」 · 끝 코드 ${r.code} · OUT 의 cells.json ${r.doc !== null} · 칸 ${f.cells.length}/${f.planned} · 전 칸 패치 사유 ${allPatch} · pending ${f.pending} · TCP ${f.connects.length} · playwright ${f.pw} · 자식 ${f.child} · ⚫ ${f.dev}${r.doc === null ? ` · ${LIB.sanitize(r.text, 160)}` : ''}`);
});

// T-C1b 8차 Z8-1 ⓑ — 닫힌 포트 → 끝 코드 = TC1_B_CODE · cells.json · pending 0 · 모든 칸 사유 · ⚫ 0 · 브라우저 0 · 나가는 TCP = 닫힌 포트로만(제품 포트 · DB 0)
guardAsync('T-C1b 8차 Z8-1 ⓑ capture.js 프로세스 · 닫힌 포트 → 차림 불가 · 브라우저 0 · 제품 포트 0', async () => {
  const cmd = captureCmd8();
  const cp = await closedPort8();
  const r = runCapture8(cmd, { UI_BASE: `http://127.0.0.1:${cp.port}` });
  const f = tc1Facts(r, cp.port);
  const noDoor = f.cells.filter((c) => c.status === 'no-door').length;
  const loginFail = f.cells.filter((c) => c.status === 'setup-fail' && /^기초 자료 차림 실패 — API 로그인 실패/.test(c.reason || '')).length;
  const why1 = (f.cells.find((c) => c.status === 'setup-fail') || {}).reason || '';
  const ok = cp.refused && !PRODUCT_PORTS8.includes(cp.port) && r.code === TC1_B_CODE && r.doc !== null && f.cells.length === f.planned && f.pending === 0 && f.noReason === 0
    && loginFail > 0 && noDoor + loginFail === f.cells.length && !f.dev && f.connects.length >= 1 && f.other === 0 && f.product === 0 && f.pw === 0 && f.child === 0;
  check('T-C1b 8차 Z8-1 ⓑ capture.js 프로세스 · 닫힌 포트 → 차림 불가 · 브라우저 0 · 제품 포트 0', ok,
    `닫힘 확인 ${cp.refused} · 끝 코드 ${r.code}(기대 ${TC1_B_CODE}) · OUT 의 cells.json ${r.doc !== null} · 칸 ${f.cells.length}/${f.planned} · pending ${f.pending} · 사유 없음 ${f.noReason} · 로그인 실패 ${loginFail} + 문 없음 ${noDoor} · 사유 「${LIB.sanitize(why1, 80)}」 · ⚫ ${f.dev} · TCP ${f.connects.length}(닫힌 포트 밖 ${f.other} · 제품 포트 · DB ${f.product}) · playwright ${f.pw} · 자식 ${f.child}`);
});

// ════ 9차(11/19 · [4] 8차 좁은 재셈 리뷰서 끝 「PM 판단」 「수용 · 9차」 · 명세서 §1-9 · 시험 파일만) ════
const STEP9 = '- name: "[시험] 차림 · 판독 입구';
/** T-C1 대상 단계 머리 칸(들여쓰기 = 단계 + 2) · 그 잡 defaults · 워크플로 defaults. */
function stepShape9(job, yml) {
  const L9 = job.split('\n');
  const i = L9.findIndex((l) => l.includes(STEP9));
  if (i < 0) return null;
  const ind = L9[i].indexOf('- ');
  const keys = {};
  for (let k = i + 1; k < L9.length; k++) {
    const l = L9[k];
    if (l.trim() === '' || /^\s*#/.test(l)) continue;
    const d = l.length - l.trimStart().length;
    if (d <= ind) break; // 다음 단계(같은 들여쓰기 「- 」) · 잡 끝
    const m = d === ind + 2 ? /^\s*([\w-]+):\s*(.*)$/.exec(l) : null;
    if (m) keys[m[1]] = m[2].trim().replace(/^["']|["']$/g, '');
  }
  const next = job.slice(1).search(/\n {2}[\w-]+:\s*(\n|$)/);
  const jobOnly = next < 0 ? job : job.slice(0, next + 1);
  return { keys, jobDefaults: /\n {4}defaults:/.test(jobOnly), wfDefaults: /(^|\n)defaults:/.test(yml) };
}
/** 시험(runCapture8 — cwd 레포 뿌리 · 새로 짠 환경 · 셸 없이 node)과 다른 칸 목록 · 빈 배열 = 같음. */
function diffFromTest9(s) {
  if (!s) return ['단계 못 찾음'];
  const d = [];
  const wd = s.keys['working-directory'];
  if (wd !== undefined && !['.', './', '${{ github.workspace }}'].includes(wd)) d.push(`working-directory ${wd}`);
  if (s.keys.shell !== undefined && s.keys.shell !== 'bash') d.push(`shell ${s.keys.shell}`);
  if (s.keys.env !== undefined) d.push('단계 env 있음');
  if (s.jobDefaults) d.push('잡 defaults 있음');
  if (s.wfDefaults) d.push('워크플로 defaults 있음');
  return d;
}

// T-C1c 9차 F8-2 — T-C1 은 run: 한 줄만 봤다(단계에 working-directory: scripts 를 끼운 변이 X2 생존) → 대상 단계의 working-directory · shell · env 와 잡 · 워크플로 defaults 가 없음/같음
//   시험 안 대조(원본 무변 · 글자 사본에 한 줄 끼움): working-directory: scripts · env: · shell: pwsh = 다름 / working-directory: . · shell: bash = 같음
const TC1C = 'T-C1c 9차 F8-2 T-C1 대상 단계 working-directory · shell · env · defaults = 없음/같음(시험 = cwd 레포 뿌리 · 새 환경 · 셸 없이 node)';
guard(TC1C, () => {
  const yml = fs.existsSync(YML7) ? fs.readFileSync(YML7, 'utf8').split('\r\n').join('\n') : '';
  const job = printJob7();
  const real = diffFromTest9(stepShape9(job, yml));
  const ins = (line) => {
    const L = job.split('\n');
    const i = L.findIndex((l) => l.includes(STEP9));
    if (i < 0) return job;
    const pad = ' '.repeat(L[i].indexOf('- ') + 2);
    L.splice(i + 1, 0, ...line.split('\n').map((x) => pad + x));
    return L.join('\n');
  };
  const differ = [['working-directory: scripts', 'working-directory scripts'], ['env:\n  W51_X: "1"', '단계 env 있음'], ['shell: pwsh', 'shell pwsh']]
    .map(([line, want]) => diffFromTest9(stepShape9(ins(line), yml)).includes(want));
  const same = ['working-directory: .', 'shell: bash'].map((line) => diffFromTest9(stepShape9(ins(line), yml)).length === 0);
  const ok = yml.length > 0 && real.length === 0 && differ.every(Boolean) && same.every(Boolean);
  check(TC1C, ok,
    `실제 단계 다름 [${real.join(',')}] · 대조 다름 가름(wd scripts ${differ[0]} · env ${differ[1]} · shell pwsh ${differ[2]}) · 대조 같음 가름(wd . ${same[0]} · shell bash ${same[1]})`);
});

/** 탐침(관찰 손을 실은 자식 안에서 돎) — 허용 밖 열린 포트로 net.connect · fetch · 허용 닫힌 포트로 net.connect → 결과 JSON 한 줄. */
function probe9() {
  const net = require('net');
  const open = Number(process.env.W51_OPEN);
  const closed = Number(process.env.W51_CLOSED);
  const tryNet = (port) => new Promise((ok) => {
    let s;
    try { s = net.connect(port, '127.0.0.1'); } catch (e) { ok(`던짐 ${e.code || e.name}`); return; }
    s.once('connect', () => { s.destroy(); ok('연결됨'); });
    s.once('error', (e) => ok(`오류 ${e.code || e.name}`));
  });
  (async () => {
    const out = {};
    out.net = await tryNet(open);
    try { await fetch(`http://127.0.0.1:${open}/`); out.fetch = '응답'; } catch (e) { out.fetch = `거절 ${(e.cause && (e.cause.code || e.cause.name)) || e.name}`; }
    out.allow = await tryNet(closed);
    process.stdout.write(JSON.stringify(out));
  })();
}
/** observer8 을 실은 node 자식으로 probe9 실행 · 비동기(부모 쪽 열린 포트가 연결을 받을 수 있게) · 30초 상한 · 임시 폴더 지움. */
function runObserved9(extra) {
  const root = fs.mkdtempSync(path.join(OS.tmpdir(), 'w51-tc1d-'));
  const rec = path.join(root, 'rec.log');
  const obs = path.join(root, 'observer.js');
  const probe = path.join(root, 'probe.js');
  fs.writeFileSync(obs, `'use strict';\n(${observer8.toString()})();\n`);
  fs.writeFileSync(probe, `'use strict';\n(${probe9.toString()})();\n`);
  fs.writeFileSync(rec, '');
  const env = {};
  for (const k of ['PATH', 'SystemRoot', 'TEMP', 'TMP', 'TMPDIR']) if (process.env[k]) env[k] = process.env[k];
  Object.assign(env, { NODE_OPTIONS: `--require "${obs.split(path.sep).join('/')}"`, W51_TC1_REC: rec }, extra);
  return new Promise((ok) => {
    let so = '';
    let se = '';
    const c = CP.spawn(process.execPath, [probe], { cwd: root, env, stdio: ['ignore', 'pipe', 'pipe'] });
    const t = setTimeout(() => c.kill(), 30000);
    c.stdout.on('data', (d) => { so += d; });
    c.stderr.on('data', (d) => { se += d; });
    c.on('error', (e) => { clearTimeout(t); ok({ code: null, so, se: `${se}${e.name}: ${e.message}` }); });
    c.on('close', (code) => { clearTimeout(t); ok({ code, so, se }); });
  }).then((r) => {
    try {
      return { ...r, lines: fs.readFileSync(rec, 'utf8').split('\n').filter(Boolean) };
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });
}

// T-C1d 9차 [3-V] P3-2 — 관찰 손 = 기록 뒤 막음 양성 대조: 허용 밖 주소(이 시험이 연 127.0.0.1 열린 포트 · 제품 포트 · DB 아님)로 net.connect · fetch
//   → 둘 다 기록 + 막음 · 그 포트가 받은 연결 0 / 허용 주소(닫힌 포트) = 기록 · 안 막음(ECONNREFUSED) — 관찰 손을 8차 꼴(기록만)로 되돌리면 빨강
const TC1D = 'T-C1d 9차 [3-V] P3-2 T-C1 관찰 손 = 기록 뒤 막음(허용 밖 연결 0 · 양성 대조)';
guardAsync(TC1D, async () => {
  const cp = await closedPort8();
  let accepted = 0;
  const srv = NET8.createServer((s) => { accepted += 1; s.destroy(); });
  await new Promise((ok, no) => { srv.once('error', no); srv.listen(0, '127.0.0.1', ok); });
  const open = srv.address().port;
  let r;
  try {
    r = await runObserved9({ W51_TC1_ALLOW: `127.0.0.1:${cp.port}`, W51_OPEN: String(open), W51_CLOSED: String(cp.port) });
    await sleep8(200); // 늦게 닿은 연결도 셈
  } finally {
    await new Promise((ok) => srv.close(ok));
  }
  let out = {};
  try {
    out = JSON.parse(r.so);
  } catch (e) {
    out = { parse: e.name };
  }
  const at = (p, kind) => r.lines.filter((l) => l === `${kind} 127.0.0.1:${p}`).length;
  const ok = cp.refused && ![cp.port, open].some((p) => PRODUCT_PORTS8.includes(p)) && r.code === 0
    && accepted === 0 && out.net === '던짐 W51_TC1_BLOCKED' && out.fetch === '거절 W51_TC1_BLOCKED' && out.allow === '오류 ECONNREFUSED'
    && at(open, 'connect') >= 2 && at(open, 'blocked') >= 2 && at(cp.port, 'connect') === 1 && at(cp.port, 'blocked') === 0;
  check(TC1D, ok,
    `열린 포트 받은 연결 ${accepted} · net 「${out.net}」 · fetch 「${out.fetch}」 · 허용(닫힌 포트) 「${out.allow}」 · 기록 열린 포트 connect ${at(open, 'connect')} / blocked ${at(open, 'blocked')} · 닫힌 포트 connect ${at(cp.port, 'connect')} / blocked ${at(cp.port, 'blocked')} · 자식 끝 코드 ${r.code}${r.code !== 0 ? ` · ${LIB.sanitize(r.se, 120)}` : ''}`);
});

// T-Z35b 8차 Z8-2 ⓐ — qtyColumn 동치: T-Z35 가 읽는 그 .razor MudTh 원문의 「수량」 5종 × (원문 · 공백 둘러쌈 · 앞에 부분 일치 미끼 머리) = 옛 규칙(정확 일치 「수량」) 위치
//   · PurchaseReturnGrid 만 다름(옛 -1 · 새 = 「반품수량」) · 대조 변이(부분 일치 · trim 제거)는 이 시험이 가름
guard('T-Z35b 8차 Z8-2 ⓐ qtyColumn = 옛 규칙 위치(수량 5종 × 원문 · 공백 · 미끼) · 매입 반품만 다름', () => {
  const CAPR = require('./capture');
  const web = path.join(REPO7, 'src', 'HitPan.Web');
  const files = [];
  const walk = (d) => {
    if (!fs.existsSync(d)) return;
    for (const e of fs.readdirSync(d, { withFileTypes: true })) {
      if (e.isDirectory()) { if (e.name !== 'obj' && e.name !== 'bin') walk(path.join(d, e.name)); } else if (e.name.endsWith('.razor')) files.push(path.join(d, e.name));
    }
  };
  walk(web);
  const text = new Map(files.map((f) => [f, fs.readFileSync(f, 'utf8')]));
  const pageOf = (route) => files.find((f) => text.get(f).includes(`@page "${route}"`));
  const comp = (name) => files.find((f) => path.basename(f) === `${name}.razor`);
  const rawHeads = (src) => [...src.matchAll(/<MudTh[^>]*>([^<]*)<\/MudTh>/g)].map((m) => m[1]); // 원문(자르기 0)
  const found = new Map();
  for (const t of F.TYPES.filter((x) => x.key !== 'sales_return')) {
    const pg = pageOf(t.route);
    const grids = pg ? [...new Set([...text.get(pg).matchAll(/<([A-Z]\w*Grid)\b/g)].map((m) => m[1]).filter((n) => n !== 'MudGrid'))] : [];
    for (const g of grids) if (comp(g)) found.set(g, rawHeads(text.get(comp(g))));
  }
  const oldCol = (hs) => hs.findIndex((h) => h.trim() === '수량'); // ⬛ 1~6차 fillLines 옛 줄 그대로
  const variants = (hs) => [['원문', hs], ['공백', hs.map((h) => `\n ${h}\t `)], ['미끼', [hs[0], '재고수량', ...hs.slice(1)]]];
  const QTY5 = ['QuotationGrid', 'DeliveryGrid', 'SalesOrderGrid', 'PurchaseOrderGrid', 'PurchaseReceiptGrid'];
  const same = (fn) => QTY5.every((g) => found.has(g) && variants(found.get(g)).every(([, v]) => oldCol(v) >= 0 && fn(v) === oldCol(v)));
  const prOk = (fn) => found.has('PurchaseReturnGrid') && variants(found.get('PurchaseReturnGrid')).every(([, v]) => oldCol(v) === -1 && fn(v) >= 0 && String(v[fn(v)]).trim() === '반품수량');
  const now = (hs) => CAPR.qtyColumn(hs);
  const partial = (hs) => hs.findIndex((h) => CAPR.QTY_HEADERS.some((q) => String(h).includes(q))); // 대조 변이(원본 무변)
  const noTrim = (hs) => hs.findIndex((h) => CAPR.QTY_HEADERS.includes(String(h))); // 대조 변이(원본 무변)
  const pc = found.get('QuotationGrid') || [];
  const ok = same(now) && prOk(now) && !same(partial) && !same(noTrim);
  check('T-Z35b 8차 Z8-2 ⓐ qtyColumn = 옛 규칙 위치(수량 5종 × 원문 · 공백 · 미끼) · 매입 반품만 다름', ok,
    `부품 ${[...found.keys()].join('+')} · 5종 × 3꼴 같음 ${same(now)} · 양성 대조 QuotationGrid 위치 새 ${now(pc)} = 옛 ${oldCol(pc)} · 매입 반품 옛 -1 · 새 반품수량 ${prOk(now)} · 대조 변이 거름(부분 일치 ${!same(partial)} · trim 제거 ${!same(noTrim)})`);
});

/** 가짜 목록 창(8차) — rows: [{ text, at(ms 뒤 나타남), head }] · st.diag = 진단에서만 쓰는 손이 불린 수 · spec.hang = 'outside' | 'rows' 면 그 읽기가 끝나지 않음. */
function fakeList8(spec = {}) {
  const t0 = Date.now();
  const st = { diag: 0, shots: 0, waits: [] };
  const rows = spec.rows || [];
  const shown = () => rows.filter((r) => Date.now() - t0 >= (r.at || 0));
  const hang = (k) => (spec.hang === k ? new Promise(() => {}) : null);
  const loc = (ops) => {
    const match = () => shown().filter((r) => !r.head && ops.every((o) => (o.hasText === undefined || r.text.includes(o.hasText)) && (o.hasNotText === undefined || !r.text.includes(o.hasNotText))));
    return {
      filter: (o) => loc([...ops, o]),
      count: async () => { if (ops.length === 0) st.diag += 1; return ops.length === 0 ? shown().length : match().length; },
      first: () => ({
        waitFor: async ({ timeout }) => {
          st.waits.push(timeout);
          const end = Date.now() + timeout;
          for (;;) {
            if (match().length > 0) return;
            if (Date.now() >= end) { const e = new Error(`Timeout ${timeout}ms exceeded`); e.name = 'TimeoutError'; throw e; }
            await sleep8(20);
          }
        },
      }),
      evaluateAll: async (fn) => { st.diag += 1; return hang('rows') || fn(shown().map((r) => ({ querySelector: (s) => (s === 'th' && r.head ? {} : null), innerText: r.text }))); },
      texts: () => match().map((r) => r.text),
    };
  };
  const dlg = {
    locator: (sel) => {
      if (sel === 'tr') return loc([]);
      if (sel === 'input') return { evaluateAll: async (fn) => { st.diag += 1; return fn((spec.dlgInputs || []).map((value) => ({ value }))); } };
      return { allInnerTexts: async () => { st.diag += 1; return spec.pager || []; } };
    },
    innerText: async () => { st.diag += 1; return spec.dlgText || ''; },
    screenshot: async () => { st.shots += 1; },
  };
  const page = {
    evaluate: async () => { st.diag += 1; return 'UTC · 분 차 0'; },
    locator: () => ({
      evaluateAll: async (fn) => {
        st.diag += 1;
        return hang('outside') || fn([...(spec.pageInputs || []).map((value) => ({ value, closest: () => null })), ...(spec.dlgInputs || []).map((value) => ({ value, closest: () => ({}) }))]);
      },
    }),
    screenshot: async () => { st.shots += 1; },
  };
  return { st, dlg, page };
}
/** ⬛ 1~6차 openFromList 옛 줄 그대로(0.8초 쉬고 한 번 셈) — 동치 비교용. */
async function oldListRule8(dlg, pick) {
  await sleep8(800);
  const rows = pick(dlg.locator('tr').filter({ hasText: F.MARK.partner }));
  if ((await rows.count()) === 0) throw new S.SetupError('표지 거래처 줄 없음');
  return rows;
}

// T-Z36b 8차 Z8-2 ⓑ — waitListRows 성공 길: 표지 줄이 처음부터 있음 / 0.5초 뒤 나타남 × 줄 고르기 3(견적 = 양성 대조 · 거래명세서 · 판매 반품 ⓙ)
//   → 돌려준 줄 = 옛 규칙(0.8초 쉬고 셈)과 같음 · 진단 손 부름 0 · 사진 0 · 기다림 상한 = LIST_WAIT · 처음부터 있음 = 1초 안
guardAsync('T-Z36b 8차 Z8-2 ⓑ waitListRows 성공 길 = 옛 규칙 줄 · 진단 0 · 처음부터 있음 1초 안', async () => {
  const CAPR = require('./capture');
  const P0 = F.MARK.partner;
  const rowsAt = (at) => [{ head: true, text: '일자 전표번호 업체' }, { text: `2026-11-19 D-1 ${P0} 확정`, at }, { text: `2026-11-19 D-2 ${P0} 반품`, at }, { text: '2026-11-19 D-3 다른거래처', at: 0 }];
  const runs = [];
  for (const at of [0, 500]) {
    for (const [typeKey, entry] of [['estimate', 'doc'], ['delivery', 'doc'], ['delivery', 'doc-j']]) {
      const t = F.TYPES.find((x) => x.key === typeKey);
      const pick = CAPR.entryPick(typeKey, entry);
      const a = fakeList8({ rows: rowsAt(at) });
      const b = fakeList8({ rows: rowsAt(at) });
      runs.push(Promise.all([
        (async () => { const t1 = Date.now(); const got = await CAPR.waitListRows(a.page, a.dlg, t, pick); return { got, ms: Date.now() - t1 }; })(),
        oldListRule8(b.dlg, pick),
      ]).then(([n, old]) => ({
        at, typeKey, entry, ms: n.ms, diag: a.st.diag, shots: a.st.shots, waitOk: a.st.waits.length === 1 && a.st.waits[0] === CAPR.LIST_WAIT,
        same: n.got.texts().length > 0 && JSON.stringify(n.got.texts()) === JSON.stringify(old.texts()),
      })));
    }
  }
  const cases = await Promise.all(runs);
  const ok = cases.length === 6 && cases.every((c) => c.same && c.diag === 0 && c.shots === 0 && c.waitOk) && cases.filter((c) => c.at === 0).every((c) => c.ms < 1000);
  check('T-Z36b 8차 Z8-2 ⓑ waitListRows 성공 길 = 옛 규칙 줄 · 진단 0 · 처음부터 있음 1초 안', ok,
    cases.map((c) => `${c.at}ms·${c.typeKey}/${c.entry}=${c.same ? '같음' : '다름'}·${c.ms}ms·진단 ${c.diag}`).join(' · '));
});

// T-Z36c 8차 Z8-3 — 진단 소독: 환경 비밀값(S0 가림과 같은 3꼴 = 평문 · base64 · url 조각) + 제어 글자 + 긴 글을 창 글 · 기간 입력 · 새 칸(창 밖 날짜 · 머리 줄 · 첫 3줄 · 쪽 글)에 심음
//   → 진단 글에 3꼴 0 · 제어 글자 0 · 칸별 길이 상한(창 글 200 · 줄 80 · 날짜 · 쪽 40) · 칸마다 글이 실제로 지남(빈 통과 0)
//   제어 글자로 쪼갠 값(빈칸 자리 = BEL) = 빈칸으로 바꾸면 비밀값이 다시 이어지는 길(7차 창 글 차례 · 7차 기간 값 제어 글자 안 거름)
guardAsync('T-Z36c 8차 Z8-3 진단 소독 — 3꼴 비밀 0 · 제어 글자 0 · 칸별 길이 상한', async () => {
  const CAPR = require('./capture');
  const SEC = 'selftest-w51 진단값'; // 빈칸 든 가짜 값(자격증명 꼴 아님)
  const NAME = `${LIB.SECRET_PREFIX}W51DIAG`;
  const b64 = Buffer.from(SEC, 'utf8').toString('base64');
  const url = encodeURIComponent(SEC);
  const split = SEC.replace(' ', '\u0007');
  const C0 = '\u0001\u001b[31m\u007f';
  const fill = (ch, n) => ch.repeat(n);
  const spec = {
    rows: [
      { head: true, text: `${b64} ${SEC}${C0}${fill('Ａ', 300)}` },
      { text: `${SEC}${C0}${fill('Ｃ', 300)}` },
      { text: `${url}\t${fill('Ｃ', 300)}` },
      { text: `${split}${fill('Ｃ', 300)}` },
    ],
    dlgInputs: [`2026-11-01 ${SEC}${C0}${fill('Ｅ', 300)}`, `2026-11-19 ${split}${fill('Ｅ', 300)}`],
    pageInputs: [`2026-11-19 ${split}${C0}${fill('Ｄ', 300)}`, `2026-11-18 ${SEC}${fill('Ｄ', 300)}`],
    pager: [`1-3 ${SEC}${C0}${fill('Ｇ', 300)}`],
    dlgText: `판매 목록 조회\n${SEC}\u0000${b64}\u0007${url}\n${split}${C0}${fill('Ｆ', 500)}`,
  };
  let msg = '';
  process.env[NAME] = SEC;
  try {
    const w = fakeList8(spec);
    msg = await CAPR.listDiag(w.page, w.dlg);
  } finally {
    delete process.env[NAME];
  }
  const forms = LIB.needleForms(SEC).flatMap((x) => x.strs).filter(Boolean);
  const leaked = forms.filter((n) => msg.includes(n)).length;
  const ctl = /[\u0000-\u001f\u007f]/.test(msg);
  const run = (ch) => Math.max(0, ...(msg.match(new RegExp(`${ch}+`, 'g')) || []).map((s) => s.length));
  const caps = { 'Ａ': 80, 'Ｃ': 80, 'Ｅ': 40, 'Ｄ': 40, 'Ｇ': 40, 'Ｆ': 200 };
  const over = Object.entries(caps).filter(([ch, cap]) => run(ch) === 0 || run(ch) > cap).map(([ch, cap]) => `${ch}${run(ch)}/${cap}`);
  const labels = ['기간 값 2026-11-01', '창 글 「', '창 밖 날짜 2026-11-19', '창 머리 줄 「', '첫 3줄 1「', '쪽 글 「'];
  const missing = labels.filter((l) => !msg.includes(l));
  const unread = (msg.match(/못 읽음/g) || []).length;
  const ok = forms.length >= 3 && leaked === 0 && !ctl && over.length === 0 && missing.length === 0 && unread === 0;
  check('T-Z36c 8차 Z8-3 진단 소독 — 3꼴 비밀 0 · 제어 글자 0 · 칸별 길이 상한', ok,
    `꼴 ${forms.length} 중 샘 ${leaked} · 제어 글자 ${ctl} · 상한 넘음/빈 칸 [${over.join(',')}] · 빠진 칸 [${missing.join(',')}] · 못 읽음 ${unread} · 글 ${msg.length}자`);
});

/** 표지 줄 가름(명세서 §2 Z5-n · CTO §6) — waitListRows 결과 → H1~H4 · 미분류. */
function classify8(r) {
  if (r.rows) return 'H1';
  const m = r.msg || '';
  const per = /기간 값 (\d{4}-\d{2}-\d{2})[^~·]*~ (\d{4}-\d{2}-\d{2})/.exec(m);
  const out = /창 밖 날짜 ([^·]*)/.exec(m);
  const outs = out ? [...out[1].matchAll(/\d{4}-\d{2}-\d{2}/g)].map((x) => x[0]) : [];
  const zero = /표지 줄 0\)/.test(m);
  if (per && outs.length && outs.some((d) => d < per[1] || d > per[2])) return 'H2';
  if (per && outs.length && /결과 없음 문구 참/.test(m) && zero) return 'H3';
  const first = /첫 3줄 (1「[^」]*」(?: \d「[^」]*」)*)/.exec(m);
  if (/결과 없음 문구 거짓/.test(m) && zero && first && !first[1].includes(F.MARK.partner)) return 'H4';
  return '미분류';
}

// T-Z36d 8차 Z8-4 — listDiag 읽기 3칸 · 가짜 창 4꼴이 서로 다르게 가름: H1 늦게 나타남 → 줄 돌려줌 · 진단 0 / H2 창 밖 날짜 ∉ 기간 → 두 값 다 보임
//   / H3 빈 목록 + 결과 없음 문구 → 결과 없음 참 · 표지 줄 0 / H4 줄 있음 · 표지 0 → 첫 줄 글에 다른 거래처 · 진단 ≤ 5초 · 사진 0 · 읽기가 안 끝나는 칸 = 시간 상한 「못 읽음」
guardAsync('T-Z36d 8차 Z8-4 listDiag 읽기 3칸 · 가짜 창 H1~H4 가름 · 성공 길 진단 0 · 시간 상한', async () => {
  const CAPR = require('./capture');
  const P0 = F.MARK.partner;
  const t = F.TYPES.find((x) => x.key === 'delivery');
  const pick = CAPR.entryPick('delivery', 'doc');
  const head = { head: true, text: '일자\t전표번호\t업체\t작성자\t상태\t금액' };
  const period = ['2026-11-01', '2026-11-19'];
  const none = '조회된 거래명세서가 없습니다.';
  const H = {
    H1: { rows: [head, { text: `2026-11-19 D-1 ${P0} 확정`, at: 500 }], dlgInputs: period, pageInputs: ['2026-11-19'] },
    H2: { rows: [head, { text: none }], dlgInputs: period, pageInputs: ['2026-11-20'], dlgText: `판매 목록 조회\n${none}` },
    H3: { rows: [head, { text: none }], dlgInputs: period, pageInputs: ['2026-11-19'], dlgText: `판매 목록 조회\n${none}` },
    H4: { rows: [head, { text: '2026-11-19 D-7 다른거래처 확정' }, { text: '2026-11-18 D-6 다른거래처 확정' }], dlgInputs: period, pageInputs: ['2026-11-19'], dlgText: '판매 목록 조회\n2026-11-19 D-7 다른거래처', pager: ['1-2 / 2'] },
  };
  const res = {};
  for (const [k, spec] of Object.entries(H)) {
    const w = fakeList8(spec);
    const t1 = Date.now();
    try {
      const rows = k === 'H1' ? await CAPR.waitListRows(w.page, w.dlg, t, pick) : await CAPR.waitListRows(w.page, w.dlg, t, pick, 300);
      res[k] = { rows, ms: Date.now() - t1, st: w.st };
    } catch (e) {
      res[k] = { msg: String(e.message), setup: e instanceof S.SetupError, ms: Date.now() - t1, st: w.st };
    }
    res[k].cls = classify8(res[k]);
  }
  const hw = fakeList8({ ...H.H3, hang: 'outside' });
  const th = Date.now();
  const hangMsg = await CAPR.listDiag(hw.page, hw.dlg);
  const hangMs = Date.now() - th;
  const cls = ['H1', 'H2', 'H3', 'H4'].map((k) => res[k].cls);
  const m = (k) => res[k].msg || '';
  const ok = cls.join(',') === 'H1,H2,H3,H4' && res.H1.st.diag === 0
    && m('H2').includes('기간 값 2026-11-01 ~ 2026-11-19') && m('H2').includes('창 밖 날짜 2026-11-20')
    && m('H3').includes('결과 없음 문구 참') && m('H3').includes('표지 줄 0)')
    && m('H4').includes('첫 3줄 1「2026-11-19 D-7 다른거래처 확정」') && m('H4').includes('결과 없음 문구 거짓') && m('H4').includes('쪽 글 「1-2 / 2」')
    && ['H2', 'H3', 'H4'].every((k) => res[k].setup && res[k].ms <= 300 + 5000)
    && Object.values(res).every((x) => x.st.shots === 0)
    && /창 밖 날짜 못 읽음\(DiagTimeout\)/.test(hangMsg) && hangMs <= 5000 && hw.st.shots === 0;
  check('T-Z36d 8차 Z8-4 listDiag 읽기 3칸 · 가짜 창 H1~H4 가름 · 성공 길 진단 0 · 시간 상한', ok,
    `가름 ${cls.join('/')} · H1 진단 ${res.H1.st.diag} · 시간 ${['H2', 'H3', 'H4'].map((k) => `${k} ${res[k].ms}ms`).join(' ')} · 안 끝나는 칸 ${hangMs}ms 「${(/창 밖 날짜 [^·]*/.exec(hangMsg) || [''])[0].trim()}」 · H4 「${(/첫 3줄 [^·]*/.exec(m('H4')) || [''])[0].trim().slice(0, 60)}」`);
});

// T-Z36e 9차 F8-3 — 창 밖 날짜 = .mud-dialog 밖 입력만(창 밖 거르기를 걷은 변이 X5 생존): 가짜 창 H2 · H3 은 창 안 날짜가 섞여도 같은 가름이 났다
//   → 창 안 **범위 밖** 날짜(2026-10-31) · 창 밖 **범위 안** 날짜(2026-11-19) · 입력 차례 = 실물 문서 차례(창 먼저 — App.razor <MudDialogProvider /> 가 Router 앞)
//   → 가름 H3 · 창 밖 날짜 칸 = 2026-11-19 하나 · 거르기를 걷으면 창 안 값이 섞여 H2 로 잘못 가름 = 빨강 · fakeList8 원본 무변(이 경우 안에서 page.locator 만 갈아 끼움)
const TZ36E = 'T-Z36e 9차 F8-3 창 밖 날짜 = 창 밖 입력만(창 안 범위 밖 · 창 밖 범위 안 · 창 먼저 차례) → H3';
guardAsync(TZ36E, async () => {
  const CAPR = require('./capture');
  const t = F.TYPES.find((x) => x.key === 'delivery');
  const pick = CAPR.entryPick('delivery', 'doc');
  const none = '조회된 거래명세서가 없습니다.';
  const dlgIn = ['2026-11-01', '2026-11-19', '2026-10-31'];
  const pageIn = ['2026-11-19'];
  const w = fakeList8({ rows: [{ head: true, text: '일자\t전표번호\t업체\t작성자\t상태\t금액' }, { text: none }], dlgInputs: dlgIn, pageInputs: pageIn, dlgText: `판매 목록 조회\n${none}` });
  w.page.locator = () => ({
    evaluateAll: async (fn) => {
      w.st.diag += 1;
      return fn([...dlgIn.map((value) => ({ value, closest: () => ({}) })), ...pageIn.map((value) => ({ value, closest: () => null }))]);
    },
  });
  let r;
  try {
    await CAPR.waitListRows(w.page, w.dlg, t, pick, 300);
    r = { rows: true };
  } catch (e) {
    r = { msg: String(e.message), setup: e instanceof S.SetupError };
  }
  const cls = classify8(r);
  const outField = ((/창 밖 날짜 ([^·]*)/.exec(r.msg || '') || [])[1] || '').trim();
  const ok = r.setup === true && cls === 'H3' && outField === '2026-11-19' && (r.msg || '').includes('기간 값 2026-11-01 ~ 2026-11-19') && w.st.shots === 0;
  check(TZ36E, ok, `가름 ${cls} · 창 밖 날짜 「${outField}」 · 「${((/기간 값 [^·]*/.exec(r.msg || '') || [''])[0]).trim()}」 · 사진 ${w.st.shots}`);
});

// ── 🔴 10차(명세서 §1-10) — D4 저장 성공 · D8 판매 차림 · D5 창 밖 날짜 실물 꼴 후보 · D7 꼬리표 (node 만 · 제품 원문 읽기만 · 화면 · DB · 앱 0)
const CAP10 = require('./capture');
const REPO10 = path.resolve(__dirname, '../../../..');
const readSrc10 = (rel) => fs.readFileSync(path.join(REPO10, rel), 'utf8');
function webText10() {
  const out = [];
  const walk = (d) => {
    for (const e of fs.readdirSync(d, { withFileTypes: true })) {
      const p = path.join(d, e.name);
      if (e.isDirectory()) walk(p);
      else if (/\.(cs|razor)$/.test(e.name)) out.push(fs.readFileSync(p, 'utf8'));
    }
  };
  for (const d of ['src/HitPan.Web/Pages', 'src/HitPan.Web/Services', 'src/HitPan.Web/Components']) walk(path.join(REPO10, d));
  return out.join('\n');
}
/** 가짜 저장 손 — sc: { toast · body · status · needYes(창 「저장」 을 눌러야 응답) · skipYes(창이 떴는데 안 누름) · noResp · err · url } */
function fakeSaveIo10(sc) {
  const st = { clicks: 0, yes: [] };
  let fire;
  const respP = new Promise((ok) => { fire = ok; });
  const resp = () => (sc.noResp ? null : { status: sc.status || 200, body: sc.body });
  const io = {
    errors: async () => (st.clicks ? sc.err || 0 : 0),
    clickSave: async () => { st.clicks += 1; if (!sc.needYes) fire(resp()); },
    waitResp: (u) => { st.url = u; return Promise.race([respP, new Promise((ok) => setTimeout(() => ok(null), 200))]); },
    confirmDialog: async (c) => {
      if (sc.skipYes) return { seen: true, text: c.title };
      st.yes.push(c.yes);
      if (sc.needYes) fire(resp());
      return { seen: true, text: `${c.title} ${c.text}` };
    },
    dialogLeft: async () => (sc.needYes && (sc.skipYes || st.yes.length === 0) ? '수주서 자동 생성' : null),
    toasts: async () => sc.toast || '',
    waitUrl: async () => undefined,
    url: () => sc.url || 'http://127.0.0.1:5234/x',
  };
  return { io, st };
}
const SAVE10 = {
  estimate: { toast: '견적서 저장이 완료되었습니다.', body: { id: 'q-1' }, want: 'q-1' },
  delivery: { needYes: true, toast: '저장 완료: 명-20261119-001 | 수주서 수-20261119-002 가 자동 생성되었습니다.', body: { id: 'd-1', documentNumber: '명-20261119-001', autoCreatedOrderNo: '수-20261119-002' }, want: '명-20261119-001', auto: '수-20261119-002' },
  sales_order: { toast: '수주서 저장이 완료되었습니다.', body: { id: 's-1', documentNumber: '수-20261119-003' }, want: '수-20261119-003' },
  purchase_order: { toast: '발주서가 서버에 저장되었습니다.', body: { id: 'p-1' }, want: 'p-1' },
  receipt: { toast: '매입명세서가 서버에 저장되었습니다.', body: { Id: 'r-1' }, want: 'r-1' },
  purchase_return: { toast: '매입반품을 저장했습니다.', body: { returnId: 'rt-1', returnNo: '반-20261119-001' }, want: '반-20261119-001' },
  sales_return: { url: 'http://127.0.0.1:5234/deliveries', body: { returnId: 'sr-1', returnNo: '반-20261119-002' }, want: '반-20261119-002' },
};

// T-D4a 10차 D4 — 7종 저장 성공 = 창 · 오류 · 응답 · 성공 알림 · 번호(견적 양성 대조 칸 길 포함) · 거래명세서 = 창 안 「저장」 을 눌러야 응답
const TD4A = 'T-D4a 10차 D4 저장 성공 = 확인 창 · 오류 · 응답 · 성공 알림 · 번호 — 7종(견적 양성 대조 길 · 거래명세서 창 안 「저장」)';
guardAsync(TD4A, async () => {
  const got = [];
  let good = true;
  for (const t of F.TYPES) {
    const s = SAVE10[t.key];
    const { io, st } = fakeSaveIo10(s);
    let v;
    try {
      v = await CAP10.saveDoc({}, t.label, t.key, io);
    } catch (e) {
      v = { ok: false, why: e.message };
    }
    const ok = v.ok === true && v.no === s.want && (!s.auto || v.auto === s.auto) && st.url === CAP10.SAVE_OK[t.key].url
      && (t.key === 'delivery' ? st.yes.join() === '저장' && v.pick === s.want : st.yes.length === 0);
    if (!ok) good = false;
    got.push(`${t.key} ${ok ? v.no : `✗ ${v.why || v.no}`}`);
  }
  check(TD4A, good && got.length === 7, got.join(' · '));
});

// T-D4b 10차 D4 변이 꼴 3(+2) — 성공 알림 없음 · 번호 빈 값 · 확인 창 안 누름 · 응답 없음 · 응답 500 → 전부 이름 붙은 차림 불가(오류 알림 0 이어도)
// ⬛ [낡은 줄 · 10차 1단] const TD4B = 'T-D4b 10차 D4 성공 알림 없음 · 번호 빈 값 · 확인 창 안 누름 · 응답 없음 · 500 → 전부 빨강(오류 알림 0 ≠ 성공)';
// 🔴 11차 P2-3 — 「응답 2xx · 번호 있음 · 오류 알림 1」 꼴 더함(판정 차례 2번째 오류 알림 걷음 변이 X9 를 잡음)
const TD4B = 'T-D4b 10차 D4 성공 알림 없음 · 번호 빈 값 · 확인 창 안 누름 · 응답 없음 · 500 · (11차) 2xx·번호 있음·오류 알림 1 → 전부 빨강(오류 알림 0 ≠ 성공)';
guardAsync(TD4B, async () => {
  const est = F.TYPES.find((x) => x.key === 'estimate');
  const del = F.TYPES.find((x) => x.key === 'delivery');
  const cases = [
    ['성공 알림 없음', est, { toast: '', body: { id: 'q-1' } }, /견적서 저장 성공 알림 없음/],
    ['번호 빈 값', est, { toast: '견적서 저장이 완료되었습니다.', body: { id: '' } }, /견적서 전표 번호 없음/],
    ['확인 창 안 누름', del, { needYes: true, skipYes: true, toast: '', body: { id: 'd-1', documentNumber: 'x' } }, /거래명세서 확인 창 남음/],
    ['응답 없음', est, { noResp: true, toast: '견적서 저장이 완료되었습니다.' }, /견적서 저장 요청 응답 없음/],
    ['응답 500', est, { status: 500, toast: '견적서 저장이 완료되었습니다.', body: { id: 'q' } }, /견적서 저장 응답 500/],
    ['2xx·번호 있음·오류 알림 1', est, { err: 1, toast: '견적서 저장이 완료되었습니다.', body: { id: 'q-1' } }, /견적서 저장 뒤 경고 · 오류 알림 1/], // 🔴 11차 P2-3
  ];
  const got = [];
  let good = true;
  for (const [name, ty, s, re] of cases) {
    const { io } = fakeSaveIo10(s);
    let why;
    try {
      await CAP10.saveDoc({}, ty.label, ty.key, io);
      why = '통과(거짓 초록)';
    } catch (e) {
      why = e instanceof S.SetupError ? e.message : `다른 예외 ${e.name}`;
    }
    const ok = re.test(why);
    if (!ok) good = false;
    got.push(`${name} ${ok ? '빨강' : `✗ ${why}`}`);
  }
  check(TD4B, good, got.join(' · '));
});

// T-D4c 10차 D4 — 성공 알림 · 저장 주소 = 제품 Web 원문 대조(6종 알림 · 7종 주소) · 판매 반품 = 알림 대신 이동(forceLoad 원문 확인)
// ⬛ [낡은 줄 · 10차 1단] const TD4C = 'T-D4c 10차 D4 성공 알림 6종 · 저장 주소 7종 = 제품 원문 · 판매 반품 forceLoad 이동';
// 🔴 11차 P2-2 — 판매 반품 저장 전 주소(?returnOf=) 원문 확인 + 그 주소 그대로면 「이동 없음」 빨강(옛 꼴 공회전 · 변이 X7 · X7b 를 잡음)
const TD4C = 'T-D4c 10차 D4 성공 알림 6종 · 저장 주소 7종 = 제품 원문 · 판매 반품 forceLoad 이동 · (11차) 저장 전 주소 ?returnOf= 그대로 → 빨강';
guardAsync(TD4C, async () => {
  const web = webText10();
  const del = readSrc10('src/HitPan.Web/Pages/Sales/DeliveryPage.razor');
  const got = [];
  let good = true;
  for (const t of F.TYPES) {
    const ok10 = CAP10.SAVE_OK[t.key];
    const urlOk = web.includes(`"${ok10.url.replace(/^\//, '')}"`);
    const toastOk = ok10.toast ? ok10.toast.test(web) : (/반품 저장 완료: \{result\.DocumentNumber\}/.test(del) && /NavigateTo\("\/deliveries", forceLoad: true\)/.test(del));
    if (!(urlOk && toastOk)) good = false;
    got.push(`${t.key} 주소 ${urlOk ? '○' : '×'} 알림 ${toastOk ? '○' : '×'}`);
  }
  // 🔴 11차 P2-2 — 제품 저장 전 주소 원문(NavigateTo $"/deliveries?returnOf=…") · 그 주소 그대로 = 「저장 뒤 화면 이동 없음」
  const sr = F.TYPES.find((x) => x.key === 'sales_return');
  const preOk = /NavigateTo\(\$"\/deliveries\?returnOf=/.test(del);
  let stay = '';
  try { await CAP10.saveDoc({}, sr.label, sr.key, fakeSaveIo10({ ...SAVE10.sales_return, url: 'http://127.0.0.1:5234/deliveries?returnOf=d-1' }).io); stay = '통과(거짓 초록)'; } catch (e) { stay = e instanceof S.SetupError ? e.message : `다른 예외 ${e.name}`; }
  if (!(preOk && /저장 뒤 화면 이동 없음/.test(stay))) good = false;
  got.push(`저장 전 주소 원문 ${preOk ? '○' : '×'} · ?returnOf= 그대로 「${stay.slice(0, 40)}」`);
  check(TD4C, good, got.join(' · '));
});

// T-D8a 10차 D8 ⓐ — 「수주서 자동 생성」 창 글자(제목 · 문구 · 단추) = DeliveryPage.razor 원문 · 판매확정 창 · 알림 = SalesListDialog.razor 원문 · 동작 = 창 안 「저장」 뒤에만 저장 요청
const TD8A = 'T-D8a 10차 D8 ⓐ 확인 창 글자 = 제품 원문(거래명세서 · 판매확정) · 창 안 「저장」 뒤에만 저장 요청';
guardAsync(TD8A, async () => {
  const del = readSrc10('src/HitPan.Web/Pages/Sales/DeliveryPage.razor');
  const lst = readSrc10('src/HitPan.Web/Components/Sales/SalesListDialog.razor');
  const c = CAP10.SAVE_OK.delivery.confirm;
  const boxes = [...del.matchAll(/ShowMessageBoxAsync\(\s*"([^"]*)",\s*"([^"]*)",\s*yesText:\s*"([^"]*)",\s*noText:\s*"([^"]*)"\s*\)/g)].map((m) => m.slice(1, 5));
  const box = boxes.find((b) => b[0] === c.title);
  const textOk = !!box && box[1] === c.text && box[2] === c.yes && box[3] === c.no;
  const k = CAP10.CONFIRM_OK;
  const listOk = lst.includes(`>${k.button}</MudButton>`) && k.ask.test(lst) && lst.includes(`yesText: "${k.yes}"`) && /건 확정 완료/.test(lst) && lst.includes('건 확정 실패 — ');
  const s = SAVE10.delivery;
  const { io, st } = fakeSaveIo10(s);
  const v = await CAP10.saveDoc({}, '거래명세서', 'delivery', io);
  const actOk = v.ok === true && st.clicks === 1 && st.yes.join() === '저장' && /수주서 자동 생성/.test(v.dialog.text);
  check(TD8A, textOk && listOk && actOk, `창 ${box ? `「${box.join(' / ')}」` : '못 찾음'} · 판매확정 원문 ${listOk ? '○' : '×'} · 동작 클릭 ${st.clicks} · 창 단추 ${st.yes.join() || '0'} · 번호 ${v.no}`);
});

// T-D8b 10차 D8 ⓑ — 확정 판정(완료 · 실패 사유 · 알림 없음) · 판매 반품 차례 = 확정 → 번호 줄 → [반품하기] → 사유 → 저장 · 확정을 빼면 [반품하기] 꺼짐 · 재고 선행 실패 · 번호 없음 = 이름 붙은 사유
const TD8B = 'T-D8b 10차 D8 ⓑ 확정 판정 · 판매 반품 차례(확정 먼저) · 확정 0 → [반품하기] 꺼짐 · 재고 선행 실패 · 번호 없음';
guardAsync(TD8B, async () => {
  const cv = [CAP10.confirmVerdict('1건 확정 완료'), CAP10.confirmVerdict('1건 확정 실패 — 재고가 부족합니다.'), CAP10.confirmVerdict('')];
  const nos = { delivery: { pick: '명-20261119-001', auto: '수-20261119-002' } };
  const mk = (opt = {}) => {
    const st = { confirmed: false, order: [] };
    return {
      st,
      steps: {
        confirm: async (t, n) => { st.order.push(`확정 ${n}`); if (!opt.noConfirm) st.confirmed = true; },
        open: async (t, pick) => { st.order.push(`열기 ${typeof pick}`); },
        returnEnabled: async () => st.confirmed,
        clickReturn: async () => { st.order.push('반품하기'); },
        pickReason: async () => { st.order.push('사유'); },
        save: async () => { st.order.push('저장'); return { ok: true, no: '반-20261119-002', pick: '반-20261119-002' }; },
      },
    };
  };
  const msg = async (fn) => { try { await fn(); return '통과(거짓 초록)'; } catch (e) { return e instanceof S.SetupError ? e.message : `다른 예외 ${e.name}`; } };
  const a = mk();
  const ra = await CAP10.makeSalesReturn({}, nos, { ok: true }, a.steps);
  const rb = await msg(() => CAP10.makeSalesReturn({}, nos, { ok: true }, mk({ noConfirm: true }).steps));
  const rc = await msg(() => CAP10.makeSalesReturn({}, nos, { ok: false, why: '재고 선행 — 창고 id 없음' }, mk().steps));
  const rd = await msg(() => CAP10.makeSalesReturn({}, {}, { ok: true }, mk().steps));
  const ok = cv[0].ok === true && cv[0].n === 1 && cv[1].ok === false && /재고가 부족합니다/.test(cv[1].why) && cv[2].ok === false && /확정 알림 없음/.test(cv[2].why)
    && ra.no === '반-20261119-002' && a.st.order.join('>') === '확정 명-20261119-001>열기 function>반품하기>사유>저장'
    && /\[반품하기\] 꺼짐/.test(rb) && /재고 선행 실패 — 재고 선행 — 창고 id 없음/.test(rc) && /거래명세서 번호 없음/.test(rd);
  check(TD8B, ok, `판정 ${cv.map((x) => (x.ok ? '완료' : x.why)).join(' / ')} · 차례 ${a.st.order.join('>')} · 확정 0 「${rb}」 · 재고 「${rc}」 · 번호 「${rd}」`);
});

// T-D8c 10차 D8 ⓒ — 재고 선행: 창고 id 없음 = 부르기 0 · 품목마다 조정 → 잔량 확인 · 잔량 모자람 = 이름 붙은 사유(가짜 저장 문 · 잔량 = 조정 값)
const TD8C = 'T-D8c 10차 D8 ⓒ 재고 선행 = 조정 → 잔량 값 확인 · 창고 id 없음 → 부르기 0 · 모자람 → 빨강';
guardAsync(TD8C, async () => {
  const items = F.MARK.items.map((it, k) => ({ name: it.name, id: `it-${k}` }));
  const need = 70;
  const mkCall = (opt = {}) => {
    const st = { calls: [], adj: new Map() };
    return {
      st,
      call: async (sess, m, p, body) => {
        st.calls.push(`${m} ${p}`);
        if (m === 'POST' && p === '/api/stock/adjust') { st.adj.set(body.itemId, opt.half ? body.actualQty / 2 : body.actualQty); return { itemId: body.itemId }; }
        if (m === 'GET' && p === '/api/stock/balance') return [...st.adj.entries()].map(([itemId, q]) => ({ itemId, warehouseId: 'w-1', currentQty: q }));
        throw new Error(`모르는 문 ${m} ${p}`);
      },
    };
  };
  const msg = async (fn) => { try { await fn(); return '통과(거짓 초록)'; } catch (e) { return e instanceof S.SetupError ? e.message : `다른 예외 ${e.name}`; } };
  const a = mkCall();
  const ra = await S.ensureStock({}, items, need, 'w-1', a.call);
  const b = mkCall();
  const rb = await msg(() => S.ensureStock({}, items, need, '', b.call));
  const c = mkCall({ half: true });
  const rc = await msg(() => S.ensureStock({}, items, need, 'w-1', c.call));
  const ok = ra.ok === true && ra.items.every((x) => x.qty >= need) && a.st.calls.length === items.length + 1 && /창고 id 없음/.test(rb) && b.st.calls.length === 0 && /잔량 모자람/.test(rc);
  check(TD8C, ok, `조정 ${a.st.calls.join(' · ')} · 잔량 ${ra.items.map((x) => x.qty).join('/')} · 창고 없음 「${rb}」(부르기 ${b.st.calls.length}) · 모자람 「${rc.slice(0, 60)}」`);
});

// T-D8d 10차 D8 ⓓ — 번호 고르기: 수주서 = 제 번호(자동 생성 수주가 첫 줄이어도) · 제 번호 없으면 자동 생성 뺌 · 둘 다 없으면 번호 없음 · 거래명세서 = 제 번호 + 반품 뺌 · ⓙ = 반품 줄 · 견적 = id 뿐이면 표지 줄
const TD8D = 'T-D8d 10차 D8 ⓓ 입구 번호 고르기(수주서 자동 생성 첫 줄 피함 · 거래명세서 · ⓙ · 견적)';
guardAsync(TD8D, async () => {
  const fakeRows = (arr) => ({
    filter: (o) => fakeRows(arr.filter((x) => (o.hasText === undefined || x.includes(o.hasText)) && (o.hasNotText === undefined || !x.includes(o.hasNotText)))),
    first: () => arr[0],
    all: arr,
  });
  const so = fakeRows(['2026-11-19 수-20261119-002 ' + F.MARK.partner, '2026-11-19 수-20261119-003 ' + F.MARK.partner]);
  const nos = { sales_order: { pick: '수-20261119-003' }, delivery: { pick: '명-20261119-001', auto: '수-20261119-002' }, sales_return: { pick: '반-20261119-002' } };
  const p1 = CAP10.entryPick('sales_order', 'doc', nos)(so).first();
  const p2 = CAP10.entryPick('sales_order', 'doc', { delivery: nos.delivery })(so).first();
  let p3 = '';
  try { CAP10.entryPick('sales_order', 'doc', {}); p3 = '통과(거짓 초록)'; } catch (e) { p3 = e instanceof S.SetupError ? e.message : `다른 예외 ${e.name}`; }
  const dl = fakeRows(['명-20261119-001 ' + F.MARK.partner, '└ 반-20261119-002 명-20261119-001 의 반품 ' + F.MARK.partner]);
  const p4 = CAP10.entryPick('delivery', 'doc', nos)(dl).all;
  const p5 = CAP10.entryPick('sales_return', 'doc-j', nos)(dl).all;
  const p6 = CAP10.entryPick('estimate', 'doc', {})(fakeRows(['견적 ' + F.MARK.partner])).all.length;
  const p7 = CAP10.entryPick('delivery', 'doc', nos)(fakeRows(['명-20261119-009 ' + F.MARK.partner, ...dl.all])).all; // 🔴 11차 P3-1 — 반품 아닌 표지 줄 2 · 번호로만 가름(X13)
  // ⬛ [낡은 줄 · 10차 1단] const ok = String(p1).includes('-003') && String(p2).includes('-003') && /번호 없음/.test(p3) && p4.length === 1 && !p4[0].includes('반품') && p5.length === 1 && p5[0].includes('반품') && p6 === 1;
  const ok = p7.length === 1 && p7[0].includes('-001') && String(p1).includes('-003') && String(p2).includes('-003') && /번호 없음/.test(p3) && p4.length === 1 && !p4[0].includes('반품') && p5.length === 1 && p5[0].includes('반품') && p6 === 1;
  check(TD8D, ok, `수주서 「${String(p1).slice(11, 26)}」 · 자동 뺌 「${String(p2).slice(11, 26)}」 · 둘 다 없음 「${p3.slice(0, 30)}」 · 거래명세서 ${p4.length} · ⓙ ${p5.length} · 견적 ${p6}`);
});

// T-D5 10차 D5(F9-1) — 판매일자 선언 = DateFormat · Culture 없음(정적으로 꼴 못 닫음 → 후보 꼴 2) · 목록 창 기간 = DateFormat yyyy-MM-dd 2 · 창 밖 날짜가 후보 꼴 둘 다 잡음
const TD5 = 'T-D5 10차 D5 F9-1 판매일자 선언 원문 · 창 밖 날짜 = 후보 꼴 2(yyyy-MM-dd · yyyy. M. d.) 모두 기록';
guardAsync(TD5, async () => {
  const del = readSrc10('src/HitPan.Web/Pages/Sales/DeliveryPage.razor');
  const lst = readSrc10('src/HitPan.Web/Components/Sales/SalesListDialog.razor');
  const decl = (/<MudDatePicker[^>]*Label="판매일자"[^>]*\/>/.exec(del) || [''])[0];
  const listFmt = (lst.match(/DateFormat="yyyy-MM-dd"/g) || []).length;
  const t = F.TYPES.find((x) => x.key === 'delivery');
  const pick = CAP10.entryPick('delivery', 'doc');
  const none = '조회된 거래명세서가 없습니다.';
  const forms = ['2026-11-19', '2026. 11. 19.'];
  const out = [];
  for (const v of forms) {
    const dlgIn = ['2026-10-20', '2026-11-19'];
    const w = fakeList8({ rows: [{ head: true, text: '일자\t전표번호\t업체\t작성자\t상태\t금액' }, { text: none }], dlgInputs: dlgIn, pageInputs: [v], dlgText: `판매 목록 조회\n${none}` });
    w.page.locator = () => ({ evaluateAll: async (fn) => fn([...dlgIn.map((value) => ({ value, closest: () => ({}) })), { value: v, closest: () => null }]) });
    let m = '';
    try { await CAP10.waitListRows(w.page, w.dlg, t, pick, 300); } catch (e) { m = String(e.message); }
    out.push(((/창 밖 날짜 ([^·]*)/.exec(m) || [])[1] || '').trim());
  }
  const ok = decl !== '' && !/DateFormat|Culture/.test(decl) && listFmt === 2 && out[0] === forms[0] && out[1] === forms[1];
  check(TD5, ok, `판매일자 「${decl.slice(0, 90)}」 · 목록 기간 DateFormat ${listFmt} · 창 밖 날짜 ${out.map((x) => `「${x}」`).join(' / ')}`);
});

// T-D7 10차 D7 — 좌표 · 잘림 기준(fields · noStdHeaderOutside · style) 판정 행 = 꼬리표 · 그 밖 · 차림 불가 = 꼬리표 0 · judgeRun 행 · 결과표 글에 꼬리표 · 꼬리표 없는 좌표 판정 0
const TD7 = 'T-D7 10차 D7 「대체 글꼴 기준」 꼬리표 — 좌표 판정 행 100% · 그 밖 0 · 결과표 글';
guardAsync(TD7, async () => {
  const red = J.fontTag({ expect: [{ kind: 'fields' }] }, { cat: C.RED, why: 'x' });
  const grn = J.fontTag({ expect: [{ kind: 'style' }, { kind: 'docIs' }] }, { cat: C.GREEN, why: 'x' });
  const img = J.fontTag({ expect: [{ kind: 'image' }] }, { cat: C.RED, why: 'x' });
  const setup = J.fontTag({ expect: [{ kind: 'fields' }] }, { cat: C.SETUP, why: 'x' });
  const cell = { id: 'd7-a', group: 'g', status: 'captured', expect: [{ kind: 'fields', fields: [{ key: 'partner_name', box: null, want: F.MARK.partner }] }] };
  const pcf = typeof pcFacts !== 'undefined' ? pcFacts : {};
  const run = J.judgeRun({ cells: [cell], board: 'main', factsById: { ...pcf, [cell.id]: facts([{ words: [word(F.MARK.partner, 30, 40)] }]) }, positive: F.POSITIVE_CONTROL, probeSelftestOk: true, groups: [] });
  const coordRows = run.rows.filter((r) => [C.RED, C.GREEN, C.CHANGED].includes(r.cat));
  const untagged = coordRows.filter((r) => r.tag !== J.FONT_TAG).length;
  const md = J.toMarkdown({ run: 'R?', job: 'd7', board: 'main' }, run, undefined);
  const ok = red.tag === J.FONT_TAG && grn.tag === J.FONT_TAG && !img.tag && !setup.tag && coordRows.length === 1 && untagged === 0 && md.includes(`〔${J.FONT_TAG}〕`);
  check(TD7, ok, `빨강 ${red.tag || '-'} · 초록 ${grn.tag || '-'} · 그림 ${img.tag || '-'} · 차림 불가 ${setup.tag || '-'} · 런 행 ${run.rows.map((r) => r.cat).join('/')} · 좌표 행 ${coordRows.length} · 꼬리표 없음 ${untagged} · 결과표 ${md.includes(`〔${J.FONT_TAG}〕`) ? '○' : '×'}`);
});

// T-D8e 10차 2단 D8 ⓒ(PM 결재 9번) — 창고 id = GET /api/warehouses 첫 활성 창고(읽기 1회 · 쓰기 0 · 경로에 테넌트 0) · 0개 = 「창고 없음」 · 비활성만 = 「창고 없음」 · 고른 id 로 재고 선행 끝까지
const TD8E = 'T-D8e 10차 2단 D8 ⓒ 창고 id = 활성 창고 목록 첫 id(읽기만) · 0개 · 비활성만 → 「창고 없음」 · 재고 선행 끝까지';
guardAsync(TD8E, async () => {
  const mk = (list) => {
    const st = { calls: [], adj: new Map() };
    return {
      st,
      call: async (sess, m, p, body) => {
        st.calls.push(`${m} ${p}${body === undefined ? '' : ' +본문'}`);
        if (m === 'GET' && p === '/api/warehouses') return list;
        if (m === 'POST' && p === '/api/stock/adjust') { st.adj.set(body.itemId, body.warehouseId === 'w-2' ? body.actualQty : 0); return {}; }
        if (m === 'GET' && p === '/api/stock/balance') return [...st.adj.entries()].map(([itemId, q]) => ({ itemId, currentQty: q }));
        throw new Error(`모르는 문 ${m} ${p}`);
      },
    };
  };
  const msg = async (fn) => { try { return `값 ${await fn()}`; } catch (e) { return e instanceof S.SetupError ? e.message : `다른 예외 ${e.name}`; } };
  const a = mk([{ warehouseId: 'w-0', isActive: false }, { WarehouseId: 'w-2', IsActive: 1 }]);
  const id = await S.pickWarehouse({}, a.call);
  const items = F.MARK.items.map((it, k) => ({ name: it.name, id: `it-${k}` }));
  const end = await msg(async () => (await S.ensureStock({}, items, 70, await S.pickWarehouse({}, a.call), a.call)).ok);
  const b = mk([]);
  const rb = await msg(() => S.pickWarehouse({}, b.call));
  const c = mk([{ warehouseId: 'w-9', isActive: 0 }]);
  const rc = await msg(() => S.pickWarehouse({}, c.call));
  const readOnly = [...a.st.calls, ...b.st.calls, ...c.st.calls].filter((x) => x.startsWith('GET /api/warehouses')).every((x) => x === 'GET /api/warehouses');
  const ok = id === 'w-2' && end === '값 true' && /재고 선행 — 창고 없음/.test(rb) && b.st.calls.length === 1 && /재고 선행 — 창고 없음/.test(rc) && readOnly
    && !a.st.calls.some((x) => /tenant/i.test(x));
  check(TD8E, ok, `고른 창고 ${id} · 재고 선행 「${end}」 · 0개 「${rb}」 · 비활성만 「${rc}」 · 읽기 꼴 ${readOnly ? '○' : '×'} · 부름 ${a.st.calls.join(' · ')}`);
});

// T-FG3 11차 D1′(고침 1 §8-1 · 고침 2 §1 · PM ① · L5) — ui-print 글꼴 단계 꼴(정적 · 이 PC 에서 단계는 못 돎 = C6 G1′ · G5): S5 뒤 · S6 앞 · id font · 25분 · continue-on-error = 이 단계 하나만
//   · apt 3회 꼴 · --no-install-recommends 꾸러미 2 · pip 0 · /usr/bin/python3 · 맨 위 · 작업 폴더 지움 · 업로드 뿌리 글꼴 0 확인 · 판정 단계 env outcome · fc-match · fc-cache 0 · font-rename.py ≤ 90줄 · 보존 8 ID · 중립 이름
const TF3 = 'T-FG3 11차 D1′ 글꼴 단계 꼴(S5 뒤 · S6 앞 · id · 25분 · continue-on-error 하나 · 꾸러미 2 · 절대경로 python · 맨 위) · 판정 단계 outcome env · font-rename.py ≤90줄';
guardAsync(TF3, async () => {
  const job = printJob7();
  const ls = job.split('\n');
  const at = (s) => ls.findIndex((l) => l.includes(s));
  const iFont = at('- name: "[장치] 측정 글꼴');
  const iJudge = at('- name: "[시험] 판정');
  const run = stepRun7(job, '[장치] 측정 글꼴') || '';
  const head = ls.slice(iFont, iFont + 5).join('\n');
  const coe = (fs.readFileSync(YML7, 'utf8').match(/continue-on-error:/g) || []).length;
  const py = fs.readFileSync(path.join(__dirname, 'font-rename.py'), 'utf8');
  const pyLines = py.replace(/\r?\n$/, '').split(/\r?\n/).length;
  const keep = ((/KEEP = \{([^}]*)\}/.exec(py) || [])[1] || '').replace(/\s/g, '');
  const ck = {
    차례: iFont > at('S5 시드 부모계정') && at('S5 시드 부모계정') >= 0 && iFont < at('S6 API 기동'), id: /\n\s+id: font\n/.test(head), 시간25: /\n\s+timeout-minutes: 25\n/.test(head),
    coe하나: coe === 1 && /continue-on-error: true/.test(head), apt: /for i in 1 2 3/.test(run) && run.includes('sudo timeout 240 apt-get install -y --no-install-recommends fonts-nanum python3-fonttools') && !/\bpip3? /.test(run),
    python: run.includes('/usr/bin/python3 scripts/playwright-audit/ci/print/font-rename.py'), 맨위: run.includes(' /usr/share/fonts/;'), 지움: run.includes('rm -rf "$W"'),
    업로드0: run.includes('find "$UI_OUT"'), 사유: run.includes('PRINT_FONT_WHY='), 판정env: ls.slice(iJudge, iJudge + 5).join('\n').includes('PRINT_FONT_OUTCOME: ${{ steps.font.outcome }}'),
    G0없음: !/fc-match|fc-cache/.test(job), 줄수: pyLines <= 90, 보존8: keep === '0,7,8,9,11,12,13,14', 중립: py.includes("'PrintSubstitute-Regular'") && py.includes("'PrintSubstitute-Bold'"),
  };
  // 🔴 12차 S11-3 · S11-4 · S11-5 · P3-D — 주석(⬛) 뺀 줄로 셈(옛 줄 주석이 대신 맞는 것 막음) · 맨위 도 주석 뺀 줄로 다시 셈
  const live12 = run.split('\n').filter((l) => !/^\s*#/.test(l)).join('\n');
  const iTrap = live12.indexOf(`trap 'rm -rf "$W"' EXIT`);
  Object.assign(ck, {
    맨위: live12.includes(' /usr/share/fonts/ || fail "글꼴 단계 — 시스템 폴더 복사 실패"; else fail "${why:-글꼴 단계 — 이름 고침 실패}"; fi'),
    trap: iTrap > 0 && iTrap < live12.indexOf('mkdir -p "$W"'),
    한줄: live12.includes(`w=$(printf '%s' "$1" | tr '\\r\\n' '  '); echo "PRINT_FONT_WHY=$w" >> "$GITHUB_ENV"`) && !live12.includes('PRINT_FONT_WHY=$1'),
  });
  const bad = Object.entries(ck).filter(([, v]) => !v).map(([k]) => k);
  check(TF3, bad.length === 0, `어긋남 ${bad.join(' · ') || '0'} · 단계 줄 ${iFont} · continue-on-error ${coe} · python ${pyLines}줄`);
});

// T-FG5 12차 S11-4 · S11-5(동작) — 실제 글꼴 단계 줄(set · fail · W · trap)을 bash -e 로: 여러 줄 사유 → GITHUB_ENV 1줄(끼어든 이름 0) · 일찍 exit 1 → 작업 폴더 지워짐
const TF5 = 'T-FG5 12차 글꼴 단계 동작 — 여러 줄 사유 = env 1줄 · 일찍 exit 1 에도 작업 폴더 지움(trap)';
guard(TF5, () => {
  const run = stepRun7(printJob7(), '[장치] 측정 글꼴') || '';
  const live = run.split('\n').filter((l) => l.trim() !== '' && !l.trim().startsWith('#'));
  const pick = (re) => live.find((l) => re.test(l)) || '';
  const lines = [pick(/^set -uo pipefail/), pick(/^bad=0; fail\(\)/), pick(/^W="\$RUNNER_TEMP\/font-work"/), pick(/^trap /)].filter(Boolean);
  const root = fs.mkdtempSync(path.join(OS.tmpdir(), 'tf5-'));
  let r = null;
  let envLines = [];
  let left = null;
  try {
    const script = path.join(root, 'step.sh');
    fs.writeFileSync(script, [...lines, 'mkdir -p "$W"; : > "$W/regular.ttf"', 'fail "$(printf \'line1\\r\\nline2\\nPRINT_X=injected\')"', 'exit 1', ''].join('\n'));
    const envFile = path.join(root, 'env.txt');
    const env = { ...process.env, RUNNER_TEMP: root, UI_OUT: path.join(root, 'out'), GITHUB_ENV: envFile };
    r = CP.spawnSync(bash7(), ['--noprofile', '--norc', '-e', script], { env, encoding: 'utf8', timeout: 60000 });
    envLines = fs.existsSync(envFile) ? fs.readFileSync(envFile, 'utf8').split(/\r?\n/).filter(Boolean) : [];
    left = fs.existsSync(path.join(root, 'font-work'));
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
  const one = envLines.length === 1 && /^PRINT_FONT_WHY=line1 +line2 PRINT_X=injected$/.test(envLines[0]) && !envLines.some((l) => /^PRINT_X=/.test(l));
  check(TF5, lines.length === 4 && r && r.status === 1 && one && left === false,
    `줄 ${lines.length}/4 · 끝 코드 ${r ? r.status : '없음'} · env 줄 ${envLines.length}(${envLines.map((l) => l.slice(0, 40)).join(' / ')}) · 작업 폴더 남음 ${left}${r && r.stderr ? ` · ${r.stderr.slice(0, 80)}` : ''}`);
});

// T-FG1 11차 D2′(고침 1 §8-3 · §1-11-0 · §1-11-8 · 고침 2 §2 · §3 · §5 · PM L5) — 글꼴 관문(순수): ⑷ 만 통과 · ⑴⑵⑶ · ⓓ · 다시 셈 · 변이 V-G1 · V-G2 · V-G3 · V-L5
const TF1 = 'T-FG1 11차 D2′ 글꼴 관문 — ⑷ 만 통과 · ⑴⑵⑶ · ⓓ(T8 기록 없음 · 사실 파일 없음 · 단계 사실 실패인데 T8 초록) · ID 20 다시 셈 · ID 21 · 굵게 다른 이름 · L5';
guard(TF1, () => {
  const N = J.FONT_NEUTRAL;
  const rec = (role) => ({
    role, weight: role === 'bold' ? 600 : 400, banned: 0, id21_22: 0, preserveSame: true,
    names: [{ id: 0, p: 3, l: 1033, v: 'Copyright (c) 2011 NHN Corporation.' }, { id: 7, p: 3, l: 1033, v: 'NanumGothic Regular is a registered trademark of NHN Corporation.' },
      { id: 1, p: 3, l: 1033, v: 'Malgun Gothic' }, { id: 16, p: 3, l: 1033, v: 'Malgun Gothic' }, { id: 6, p: 3, l: 1033, v: N[role] }],
  });
  const facts = (mut) => { const f = { schema: 'font-facts/1', why: [], files: [rec('regular'), rec('bold')] }; if (mut) mut(f); return f; };
  const t8 = (o) => ({ schema: 't8-font/1', regular: [`ABCDEF+${N.regular}`], bold: [`GHIJKL+${N.bold}`], hangul: true, ...(o || {}) });
  const run = (o) => J.fontGate({ t8: t8(), facts: facts(), stepWhy: '', outcome: 'success', probeOk: true, basicHangul: true, ...o });
  const cases = [
    ['좋음 = ⑷(ID 7 예약 이름 = 보존 · 셈 밖)', run({}), (r) => r.ok && r.cell === '⑷ 글꼴 섬'],
    ['V-G1 ID 20 만 남김(banned 칸 0 거짓)', run({ facts: facts((f) => f.files[0].names.push({ id: 20, p: 1, l: 65535, v: 'NanumGothic-KSCpc-EUC-H' })) }), (r) => !r.ok && r.cell === 'ⓓ 글꼴 단계 사실 실패인데 T8 초록' && /regular 금지 낱말 1/.test(r.why)],
    ['V-G2 ID 21 있음', run({ facts: facts((f) => f.files[1].names.push({ id: 21, p: 3, l: 1033, v: 'Print Substitute' })) }), (r) => !r.ok && /이름 고친 뒤 ID 21 또는 22 있음\(bold\)/.test(r.why)],
    ['V-G3 굵게 칸 다른 이름(기본 폼 한글 있음 = ⑵)', run({ t8: t8({ bold: ['XYZABC+DejaVuSans-Bold'] }) }), (r) => !r.ok && r.cell === '⑵ T8 하네스 결함' && /굵게 \[DejaVuSans-Bold\]/.test(r.why)],
    ['보통 다른 이름 · 기본 폼 한글 없음 = ⑴', run({ t8: t8({ regular: ['ABCDEF+Lato-Regular'] }), basicHangul: false }), (r) => !r.ok && r.cell === '⑴ 이름 바꾼 글꼴이 렌더에 안 잡힘'],
    ['V-L5 outcome failure · 사유 없음', run({ outcome: 'failure' }), (r) => !r.ok && /글꼴 단계 중단\(시간 초과 등 · outcome failure\)/.test(r.why) && /^ⓓ/.test(r.cell)],
    ['outcome 빈 값', run({ outcome: '' }), (r) => !r.ok && /outcome 없음/.test(r.why)],
    ['단계 사유 있음 + T8 초록 = ⓓ(L5 줄 안 겹침)', run({ stepWhy: '글꼴 단계 — 꾸러미 설치 실패', outcome: 'failure' }), (r) => !r.ok && r.cell === 'ⓓ 글꼴 단계 사실 실패인데 T8 초록' && /꾸러미 설치 실패/.test(r.why) && !/글꼴 단계 중단/.test(r.why)],
    ['T8 기록 없음 = ⓓ', run({ t8: null }), (r) => !r.ok && r.cell === 'ⓓ T8 기록 없음'],
    ['사실 파일 없음 = ⓓ', run({ facts: null }), (r) => !r.ok && r.cell === 'ⓓ 사실 파일 없음'],
    ['T8 한글 없음 = T8 빨강', run({ t8: t8({ hangul: false }) }), (r) => !r.ok && /T8 한글 낱말 글리프 없음/.test(r.why)],
    ['판독기 자체시험 실패 = T8 빨강', run({ probeOk: false }), (r) => !r.ok && /T8 빨강/.test(r.why)],
    ['기본 폼 한글 없음 · T8 초록 = ⑶', run({ basicHangul: false }), (r) => !r.ok && r.cell === '⑶ 단계 차례 · 제품만의 글꼴 길'],
    ['family 다름', run({ facts: facts((f) => { f.files[0].names[2].v = 'Other Gothic'; }) }), (r) => !r.ok && /regular family ≠ Malgun Gothic/.test(r.why)],
    ['보존 다름', run({ facts: facts((f) => { f.files[1].preserveSame = false; }) }), (r) => !r.ok && /bold 보존 기록 다름/.test(r.why)],
    ['굵게 견본 없음 = 보통으로만 통과 + 미측정 표시', run({ t8: t8({ bold: null }) }), (r) => r.ok && /굵게 견본 없음/.test(r.boldNote)],
    // 🔴 12차 [4] P2-A — 보통 칸 = 중립 이름 + 다른 이름 1 → 초록 아님(조임 `some` 을 지킴 · `every` 로 약하게 하면 이 경우가 빨강)
    ['P2-A 보통 칸 = 중립 + 다른 이름 1 → 초록 아님', run({ t8: t8({ regular: [`ABCDEF+${N.regular}`, 'GHIJKL+DejaVuSans'] }) }), (r) => !r.ok && r.cell === '⑵ T8 하네스 결함' && /보통 \[PrintSubstitute-Regular, DejaVuSans\]/.test(r.why)],
    // 🔴 12차 [4] P3-C — T8 빨강 + 글꼴 단계 사유 있음 → 칸 이름 = 그 첫 사유(⑴/⑵ 아님) · 한글 있음 · 없음 둘 다
    ['P3-C 단계 사유 + T8 빨강(한글 있음) → 칸 = 사유', run({ stepWhy: '글꼴 단계 — 파일 사실 불일치(regular 금지 낱말 1)', outcome: 'failure', t8: t8({ regular: ['ABCDEF+NanumGothic'] }) }), (r) => !r.ok && r.cell === '글꼴 단계 — 파일 사실 불일치(regular 금지 낱말 1)'],
    ['P3-C 다시 셈 사유 + T8 빨강(한글 없음) → 칸 = 사유', run({ facts: facts((f) => { f.files[1].preserveSame = false; }), t8: t8({ regular: ['ABCDEF+Lato-Regular'] }), basicHangul: false }), (r) => !r.ok && r.cell === '글꼴 단계 — 파일 사실 불일치(bold 보존 기록 다름)'],
  ];
  const bad = cases.filter(([, r, f]) => !f(r)).map(([n, r]) => `${n} 「${r.cell} · ${String(r.why).slice(0, 80)}」`);
  check(TF1, bad.length === 0 && N.regular === 'PrintSubstitute-Regular' && N.bold === 'PrintSubstitute-Bold' && J.FONT_KEEP_IDS.join() === '0,7,8,9,11,12,13,14', bad.join(' / ') || `경우 ${cases.length} 모두 맞음`);
});

// T-FG2 11차 D2′(PM L5) — 판정기 프로세스(워크플로 명령줄): env PRINT_FONT_OUTCOME 칸 있으면 관문 켬(T8 · 사실 파일 없음 → 판정 0 · 결과표 줄 · 끝 코드 1) · 칸 없으면 끔 · judgeRun 관문 통과 = 양성 대조 그대로 · 실패 = 사유 앞머리
const TF2 = 'T-FG2 11차 D2′ 판정기 프로세스 — outcome 칸 있으면 글꼴 관문 켬(판정 0 · 결과표 줄) · 없으면 끔 · 관문 통과 = 양성 대조 그대로';
guardAsync(TF2, async () => {
  const dir = fs.mkdtempSync(path.join(require('os').tmpdir(), 'tf2-'));
  const out = {};
  try {
    const doc = { schema: 'print-cells/1', run: 'tf2', job: 'tf2', board: 'main', probeSelftestOk: true, positive: F.POSITIVE_CONTROL, groups: [], setup: {}, cells: [] };
    fs.writeFileSync(path.join(dir, 'cells.json'), JSON.stringify(doc));
    const spawn = (extra) => {
      const env = { ...process.env, ...extra };
      if (!extra.PRINT_FONT_OUTCOME && extra.PRINT_FONT_OUTCOME !== '') delete env.PRINT_FONT_OUTCOME;
      if (!extra.GITHUB_ACTIONS) delete env.GITHUB_ACTIONS; // 🔴 PM 판단 ② — CI 에서 돌아도 같은 뜻
      delete env.PRINT_FONT_WHY;
      const j = CP.spawnSync(process.execPath, [path.join(__dirname, 'judge-print.js'), dir], { encoding: 'utf8', env, timeout: 60000 });
      return { code: j.status, out: j.stdout || '', md: fs.readFileSync(path.join(dir, 'print-result.md'), 'utf8') };
    };
    out.off = spawn({});
    out.on = spawn({ PRINT_FONT_OUTCOME: 'failure' });
    out.ci = spawn({ GITHUB_ACTIONS: 'true' }); // 🔴 PM 판단 ② — 워크플로 env 줄 지움 꼴(CI 인데 칸 없음)
    fs.writeFileSync(path.join(dir, 'cells.json'), JSON.stringify({ ...doc, run: 'chain' }));
    out.chain = spawn({ GITHUB_ACTIONS: 'true' }); // 사슬 시험 안 판정기 = 뺌
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
  const base = { cells: [], board: 'main', factsById: {}, positive: F.POSITIVE_CONTROL, probeSelftestOk: true, groups: [] };
  const r0 = J.judgeRun(base);
  const rOk = J.judgeRun({ ...base, fontGate: { ok: true, why: '글꼴 관문 통과 — 2×2 ⑷ 글꼴 섬' } });
  const rBad = J.judgeRun({ ...base, fontGate: { ok: false, why: '글꼴 관문 실패 — 2×2 ⓓ T8 기록 없음' } });
  const onLine = (out.on.md.match(/- 글꼴 관문[^\n]*/) || [''])[0];
  const ciLine = (out.ci.md.match(/- 글꼴 관문[^\n]*/) || [''])[0];
  const pm2 = /\*\*실패 — 판정 0\*\*/.test(ciLine) && /글꼴 관문 입력 없음/.test(ciLine) && out.ci.code === 1 && !/글꼴 관문/.test(out.chain.md) && /\(기록만 · 판정 아님\) 런 PDF 글꼴 이름/.test(out.on.md);
  if (!pm2) console.log(`  · T-FG2 PM 판단 ② · ① — CI 칸 없음 「${ciLine.slice(0, 100)}」 끝 코드 ${out.ci.code} · 사슬 ${/글꼴 관문/.test(out.chain.md) ? '켜짐(틀림)' : '꺼짐'}`);
  // ⬛ [낡은 줄 · 11차 D2′] const ok = !/글꼴 관문/.test(out.off.md) && /\*\*실패 — 판정 0\*\*/.test(onLine) …(아래 줄 앞머리에 pm2 더함)
  const ok = pm2 && !/글꼴 관문/.test(out.off.md) && /\*\*실패 — 판정 0\*\*/.test(onLine) && /ⓓ T8 기록 없음/.test(onLine) && /글꼴 단계 중단\(시간 초과 등 · outcome failure\)/.test(onLine)
    && out.on.code === 1 && /글꼴 관문 실패/.test(out.on.out) && rOk.positive.why === r0.positive.why && rBad.positive.ok === false && /^글꼴 관문 실패 — 2×2 ⓓ/.test(rBad.positive.why);
  check(TF2, ok, `끔 ${/글꼴 관문/.test(out.off.md) ? '줄 있음(틀림)' : '줄 없음'} · 켬 끝 코드 ${out.on.code} · 「${onLine.slice(0, 120)}」 · 통과 = 그대로 ${rOk.positive.why === r0.positive.why} · 실패 앞머리 ${String(rBad.positive.why).slice(0, 30)}`);
});

// T-FG4 11차 D7′ · D6(고침 1 §8-4 · 고침 2 §5 · §1-10-0 (8)) — 꼬리표 글자(법무 ㉣) · 결과표 머리 · ㉤ 두 칸 · 굵게 요청 → 600 파일 · R6 계획 글자
const TFG4 = 'T-FG4 11차 D7′ 꼬리표 글자 · 결과표 머리 · ㉤ 두 칸 · 굵게 600 칸 · D6 R6 계획(run R6 · 잡 r6-main-read · qpdf false)';
guard(TFG4, () => {
  const base = J.judgeRun({ cells: [], board: 'main', factsById: {}, positive: F.POSITIVE_CONTROL, probeSelftestOk: true, groups: [], fontGate: { ok: true, why: '글꼴 관문 통과 — 2×2 ⑷ 글꼴 섬', pdfFonts: ['ABCDEF+PrintSubstitute-Regular'], boldWeight: 600, runFonts: { names: ['PrintSubstitute-Regular', 'PrintSubstitute-Bold'], bold: true } } });
  const md = J.toMarkdown({ run: 'R6', job: 'r6-main-read', board: 'main' }, base, null);
  const mdNoGate = J.toMarkdown({ run: 'R6', job: 'x', board: 'main' }, J.judgeRun({ cells: [], board: 'main', factsById: {}, positive: F.POSITIVE_CONTROL, probeSelftestOk: true, groups: [] }), null);
  const plan = JSON.parse(fs.readFileSync(path.join(__dirname, 'plan.json'), 'utf8'));
  const r6 = plan.runs.R6 || {};
  const job = (r6.jobs || [])[0] || {};
  const tagOk = J.FONT_TAG === '대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1, © NAVER Corp.)';
  const ok = tagOk && md.includes(`- 〔${J.FONT_TAG}〕`) && mdNoGate.includes(`- 〔${J.FONT_TAG}〕`)
    && md.includes('- 요청 이름 = Malgun Gothic · PDF 실제 글꼴 칸(사실 파일 값 그대로) = ABCDEF+PrintSubstitute-Regular') && md.includes('- 굵게 요청 → 600 파일: 굵게 파일 usWeightClass = 600')
    && md.includes('- (기록만 · 판정 아님) 런 PDF 글꼴 이름(접두 뗀 값) = PrintSubstitute-Regular, PrintSubstitute-Bold · 굵게 이름 있음 = 예') // 🔴 PM 판단 ①
    && plan.run === 'R6' && r6.jobs.length === 1 && job.id === 'r6-main-read' && job.board === 'main' && job.tenant === 'A' && job.phase === 'read' && job.patch === '' && job.qpdf === false && !r6.blocked;
  check(TFG4, ok, `꼬리표 ${tagOk ? '○' : '×'} · 머리 ${md.includes(`〔${J.FONT_TAG}〕`) ? '○' : '×'} · ㉤ ${md.includes('요청 이름 = Malgun Gothic') ? '○' : '×'} · 600 ${md.includes('usWeightClass = 600') ? '○' : '×'} · 계획 ${plan.run} · ${job.id || '잡 없음'} · qpdf ${job.qpdf}`);
});

// T-PD1 12차 PM 보충(법무 재확인 3줄 질문 3 「아니오」 · S11-2) — 판정기(워크플로 진입점)가 사실 뒤 업로드 뿌리 PDF 지움(하위 폴더 · 대문자 확장자) · 사실 파일 그대로 ·
//   결과표 · json 에 지운 수 · 같은 뿌리에 S10(누출 검사 프로세스) = 누출 0 · 못 지움 = 이름 붙은 사유 · ㉤ 행 글자(PM 보충 ⓑ)
const TPD1 = 'T-PD1 12차 PDF 업로드 0 — 판정기가 사실 뒤 PDF 지움 · 사실 그대로 · 결과표 지운 수 · S10 누출 0 · 못 지움 사유 · ㉤ 행 글자';
guard(TPD1, () => {
  const root = fs.mkdtempSync(path.join(OS.tmpdir(), 'tpd1-'));
  const dir = path.join(root, 'main');
  const o = { code: null, md: '', drop: null, left: -1, facts: false, s10: null };
  try {
    fs.mkdirSync(path.join(dir, 'pdfs'), { recursive: true });
    fs.mkdirSync(path.join(dir, 'nine'), { recursive: true });
    fs.writeFileSync(path.join(dir, 'pdfs', 'a.pdf'), '%PDF-1.7 selftest');
    fs.writeFileSync(path.join(dir, 'pdfs', 'a.json'), '{"ok":true}');
    fs.writeFileSync(path.join(dir, 'nine', 'B.PDF'), '%PDF-1.4 selftest');
    fs.writeFileSync(path.join(dir, 'cells.json'), JSON.stringify({ schema: 'print-cells/1', run: 'tpd1', job: 'tpd1', board: 'main', probeSelftestOk: true, positive: F.POSITIVE_CONTROL, groups: [], setup: {}, cells: [] }));
    const env = { ...process.env };
    delete env.GITHUB_ACTIONS;
    delete env.PRINT_FONT_OUTCOME;
    delete env.PRINT_FONT_WHY;
    const j = CP.spawnSync(process.execPath, [path.join(__dirname, 'judge-print.js'), dir], { encoding: 'utf8', env, timeout: 60000 });
    o.code = j.status;
    o.md = fs.readFileSync(path.join(dir, 'print-result.md'), 'utf8');
    o.drop = JSON.parse(fs.readFileSync(path.join(dir, 'print-result.json'), 'utf8')).pdfDrop || null;
    o.left = ['pdfs/a.pdf', 'nine/B.PDF'].filter((p) => fs.existsSync(path.join(dir, p))).length;
    o.facts = fs.existsSync(path.join(dir, 'pdfs', 'a.json'));
    const s0 = Object.fromEntries(require('../lib').S0_NAMES.map((n, i) => [n, `TPD1_DUMMY_VALUE_${i}`]));
    const sr = CP.spawnSync(process.execPath, [path.join(__dirname, '..', 'leak-scan.js'), root], { encoding: 'utf8', env: { PATH: process.env.PATH, SystemRoot: process.env.SystemRoot, ...s0 }, timeout: 60000 });
    o.s10 = { code: sr.status, out: `${sr.stdout || ''}` };
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
  // 못 지움 = 이름 붙은 사유(지우기 함수 주입 · 실제 잠김 파일은 이 PC 에서 못 만듦)
  const d2 = fs.mkdtempSync(path.join(OS.tmpdir(), 'tpd1b-'));
  let bad2 = null;
  try {
    fs.writeFileSync(path.join(d2, 'x.pdf'), '%PDF-1.7 selftest');
    bad2 = J.dropPdfs(d2, () => { const e = new Error('잠김'); e.code = 'EPERM'; throw e; });
  } finally {
    fs.rmSync(d2, { recursive: true, force: true });
  }
  const base = J.judgeRun({ cells: [], board: 'main', factsById: {}, positive: F.POSITIVE_CONTROL, probeSelftestOk: true, groups: [], fontGate: { ok: true, why: '글꼴 관문 통과 — 2×2 ⑷ 글꼴 섬', pdfFonts: [], boldWeight: 600 } });
  const md2 = J.toMarkdown({ run: 'R6', job: 'r6-main-read', board: 'main' }, { ...base, pdfDrop: bad2 }, null);
  const e5 = J.FONT_E5 === 'FontName = 측정 · FontFamily = 원문상 이 경로에서 안 씀 · 런 실측 미측정 · 내장본 이름 표 ID 1 「Malgun Gothic」 남음(원문)';
  const ok = o.code !== null && o.left === 0 && o.facts && o.drop && o.drop.removed === 2 && o.drop.failed.length === 0
    && o.md.includes('- PDF 업로드 0(법무 재확인 3 · 결과표 · 사실 파일만): 사실 뽑은 뒤 지운 PDF 2 · 못 지움 0')
    && o.s10 && o.s10.code === 0 && /🟢 PASS 누출 0/.test(o.s10.out) && !/PDF 파일/.test(o.s10.out)
    && bad2 && bad2.removed === 0 && bad2.failed.join() === 'x.pdf(EPERM)' && md2.includes('못 지움 1 — PDF 못 지움(x.pdf(EPERM)) · S10 이 막음(업로드 0)')
    && e5 && md2.includes(`- ㉤ ${J.FONT_E5}`);
  check(TPD1, ok, `판정기 끝 코드 ${o.code} · 남은 PDF ${o.left} · 사실 그대로 ${o.facts} · 지움 ${o.drop ? `${o.drop.removed}/${o.drop.failed.length}` : '기록 없음'} · S10 끝 코드 ${o.s10 ? o.s10.code : '없음'}${o.s10 && o.s10.code !== 0 ? ` 「${o.s10.out.split('\n').filter((l) => /잡힘|FAIL/.test(l)).join(' / ').slice(0, 160)}」` : ''} · 못 지움 ${bad2 ? bad2.failed.join() : '없음'} · ㉤ 글자 ${e5}`);
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
