'use strict';
// W-G1-51 갈래 Z 7차 — 사슬 시험(CTO 재판정 §5-2 C1 · C2 · C3) · 시험 쪽 장치 · 제품 코드 · DB · 앱 · 브라우저 · 네트워크 0
//   C1 같은 입구 = 워크플로가 부르는 명령줄 그대로 프로세스로 부른다
//        판독 = `dotnet <PrintProbe.dll> read <pdf> <out.json>`(ui-browser.yml 「[시험] PDF 사실 뽑기」 와 같은 꼴)
//        판정 = `node judge-print.js <dir>`(「[시험] 판정」 과 같은 진입점) — 내부 함수 직접 호출로 통과를 세지 않는다
//   C2 사슬 끝까지 = 「PDF → read → 사실 파일 쓰기 → 판정기가 그 파일 읽기 → 양성 대조 통과」 + 깨뜨린 표본 2(빨강/미측정 대조)
//   C3 실물 모양 입력 = PDF 는 `dotnet <dll> render <spec> <pdf>`(ProductLikeRender · 제품과 같은 QuestPDF 판 · 같은 글꼴 이름 「Malgun Gothic」 · 같은 그리기 꼴)
//        양성 대조 정의 = fixture.js POSITIVE_CONTROL(실제 런 cells.json 에 실리는 것과 같은 값) · 그림 = png.js formBgPng(실제 K2 차림과 같은 바이트)
//   쓰는 법: node chain.js <PrintProbe.dll> <workdir>   — 끝 줄 「사슬 시험 끝 — 통과 n · 실패 m」 · 실패 1 이상이면 끝 코드 1
//   부르는 곳: PrintProbe `selftest` T8(워크플로 「판독기 짓기 · 자체시험 먼저」 단계 안 · 워크플로 줄 무변)
const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');
const F = require('./fixture');
const P = require('./png');

const JUDGE = path.join(__dirname, 'judge-print.js');
let pass = 0;
let fail = 0;
function check(id, ok, why) {
  if (ok) pass++; else fail++;
  console.log(`  ${ok ? '🟢 PASS' : '🔴 FAIL'} ${id} — ${why}`);
}
function run(cmd, args) {
  const r = spawnSync(cmd, args, { encoding: 'utf8', timeout: 180000 });
  return { code: r.status, out: r.stdout || '', err: `${r.stderr || ''}${r.error ? ` ${r.error.name}: ${r.error.message}` : ''}` };
}
const tail = (s) => String(s).split(/\r?\n/).map((x) => x.trim()).filter(Boolean).slice(-2).join(' | ').slice(0, 240);

/** 양성 대조 두 쪽의 그리기 명세 — 기본 폼(표 머리 「품명」 · 「공급가액」 한 줄) · K2 양식지(그림 + 맞춘 칸 5 · 설정 미리보기 = 견본 값). */
const SAMPLE = { partner_name: '견본상사', doc_date: '2026-11-19', 'line.item_name': '견본품목', 'line.qty': '7', total_amount: '22,000' };
function specs(dir, opt) {
  const o = opt || {};
  const bg = P.formBgPng(0, F.TYPES[0].mark); // fixture POSITIVE_CONTROL 의 ref 와 같은 그림
  fs.writeFileSync(path.join(dir, 'k2-bg.png'), bg.png);
  const head = ['No', '품명', '규격', '수량', '단가', o.dropWord ? '합계' : '공급가액'];
  const basic = { pages: [{ bg: null, table: head, tableY: 70, texts: [{ x: 15, y: 20, w: 120, pt: 20, align: 'left', t: '견적서' }] }] };
  const pre = {
    pages: [{
      bg: 'k2-bg.png', table: null,
      texts: F.fieldsFor(0).map((f) => ({ x: f.x, y: f.y, w: f.w, pt: f.pt, align: f.align || 'left', t: SAMPLE[f.key] || '견본' })),
    }],
  };
  return { basic, pre, bg };
}

function writeCells(dir, files) {
  const plan = F.plannedCells();
  const pc = F.POSITIVE_CONTROL;
  const cells = plan.cells.map((c) => {
    if (c.id === pc.basicForm.cell) return { ...c, status: 'captured', pdf: `pdfs/${files.basic}` };
    if (c.id === pc.preprint.cell) return { ...c, status: 'captured', pdf: `pdfs/${files.pre}` };
    return { ...c, status: 'setup-fail', reason: '사슬 시험 — 판독 대상 아님(양성 대조 두 칸만 지남)' };
  });
  const doc = { schema: 'print-cells/1', run: 'chain', job: 'chain', board: 'main', probeSelftestOk: true, positive: pc, groups: plan.groups, setup: {}, cells };
  fs.writeFileSync(path.join(dir, 'cells.json'), JSON.stringify(doc, null, 2));
  return doc;
}

/** 한 벌: 그리기 → read(프로세스) → cells.json → 판정기(프로세스). */
function oneSet(dll, dir, opt) {
  fs.rmSync(dir, { recursive: true, force: true });
  fs.mkdirSync(path.join(dir, 'pdfs'), { recursive: true });
  const sp = specs(dir, opt);
  const files = { basic: 'pc-basic.pdf', pre: 'pc-preprint.pdf' };
  const out = { render: [], read: [], facts: {} };
  for (const [k, s] of [['basic', sp.basic], ['pre', sp.pre]]) {
    const specPath = path.join(dir, `${k}.spec.json`);
    fs.writeFileSync(specPath, JSON.stringify(s));
    const pdf = path.join(dir, 'pdfs', files[k]);
    const r = run('dotnet', [dll, 'render', specPath, pdf]);
    out.render.push({ k, code: r.code, err: tail(r.err) });
    if (r.code === 0 && opt && opt.truncate === k) {
      const b = fs.readFileSync(pdf);
      fs.writeFileSync(pdf, b.subarray(0, Math.min(400, b.length))); // 깨뜨린 표본 ① 잘린 PDF
    }
  }
  for (const k of ['basic', 'pre']) {
    const pdf = path.join(dir, 'pdfs', files[k]);
    const json = pdf.replace(/\.pdf$/i, '.json');
    const r = run('dotnet', [dll, 'read', pdf, json]); // ← 워크플로와 같은 명령줄
    let facts = null;
    let firstOk = false;
    if (fs.existsSync(json)) {
      const txt = fs.readFileSync(json, 'utf8');
      firstOk = /^ {2}"ok": true/m.test(txt); // 워크플로 「업로드 제외」 검사와 같은 글자 꼴
      try {
        facts = JSON.parse(txt);
      } catch (e) {
        out.read.push({ k, parse: e.name });
      }
    }
    out.read.push({ k, code: r.code, exists: fs.existsSync(json), firstOk, err: tail(r.err) });
    out.facts[k] = facts;
  }
  writeCells(dir, files);
  const j = run(process.execPath, [JUDGE, dir]); // ← 워크플로와 같은 진입점
  const end = String(j.out).split(/\r?\n/).filter((l) => l.startsWith('판정 끝')).pop() || '판정 끝 줄 없음';
  let res = null;
  try {
    res = JSON.parse(fs.readFileSync(path.join(dir, 'print-result.json'), 'utf8'));
  } catch (e) {
    res = { readError: e.name };
  }
  return { ...out, judge: { code: j.code, end, err: tail(j.err) }, res, bg: sp.bg };
}

function main(dll, work) {
  if (!dll || !work) {
    console.log('쓰는 법: node chain.js <PrintProbe.dll> <workdir>');
    return 2;
  }
  fs.mkdirSync(work, { recursive: true });

  // ── 정상 사슬 ──
  const g = oneSet(dll, path.join(work, 'good'));
  const rOk = g.render.every((x) => x.code === 0);
  check('C3 render — 제품 라이브러리 · 글꼴 이름으로 두 PDF', rOk, g.render.map((x) => `${x.k} 끝 코드 ${x.code}${x.code === 0 ? '' : ` · ${x.err}`}`).join(' · '));
  const readOk = g.read.every((x) => x.code === 0 && x.exists && x.firstOk);
  check('C1 · C2 read 프로세스 — 끝 코드 0 · 사실 파일 씀 · 「  \"ok\": true」 줄', readOk,
    g.read.map((x) => `${x.k} 끝 코드 ${x.code} · 파일 ${x.exists} · ok 줄 ${x.firstOk}${x.err ? ` · ${x.err}` : ''}`).join(' · '));
  const bw = ((g.facts.basic && g.facts.basic.pages && g.facts.basic.pages[0] && g.facts.basic.pages[0].words) || []).map((w) => w.t);
  const fonts = (g.facts.basic && g.facts.basic.pages && g.facts.basic.pages[0] && g.facts.basic.pages[0].fonts) || [];
  const preP = (g.facts.pre && g.facts.pre.pages && g.facts.pre.pages[0]) || {};
  // 🔴 11차 D2′(고침 1 §8-3 2 · 고침 2 §5) — T8 글꼴 칸 기록(판정 = judge-print.js fontGate · 이 사슬 셈은 무변) · 보통 = 두 표본 글꼴 칸 그대로(접두 포함)
  //   · 굵게 = 이 그리기에 굵게 견본 없음(PrintProbe 0줄 결재) → null · 한글 = 「품명」 · 「공급가액」 읽힘 · 자리 = $PRINT_OUT(워크플로) 또는 작업 폴더
  const t8 = { schema: 't8-font/1', regular: [...new Set([...fonts, ...(preP.fonts || [])])], bold: null, hangul: bw.includes('품명') && bw.includes('공급가액') };
  try {
    fs.writeFileSync(path.join(process.env.PRINT_OUT || work, 't8-font.json'), JSON.stringify(t8));
  } catch (e) {
    console.log(`  ⚫ T8 글꼴 칸 기록 못 씀(${e.code || e.name}) — 판정기 「T8 기록 없음」 = 판정 0`);
  }
  const imgs = preP.images || [];
  const J = require('./judge-print');
  const hit = imgs.map((im) => J.imageFrom(im, F.POSITIVE_CONTROL.preprint.ref)).find((h) => h.hit);
  const fr = J.evalCheck({ kind: 'fields', fields: F.POSITIVE_CONTROL.preprint.fields }, g.facts.pre || {}, {}, {});
  check('C3 실물 모양 — 「품명」 · 「공급가액」 낱말 · 그림(해시 또는 4분면) · 칸 5',
    bw.includes('품명') && bw.includes('공급가액') && !!hit && fr.ok === true,
    `품명 ${bw.includes('품명')} · 공급가액 ${bw.includes('공급가액')} · 글꼴 [${fonts.join(', ')}] · 그림 ${imgs.length}(${hit ? hit.how : '안 나옴'} · decode ${imgs.map((x) => x.decode).join('/')}) · ${fr.why}`);
  const pcOk = g.judge.code === 0 && /양성 대조 통과/.test(g.judge.end) && g.res && g.res.positive && g.res.positive.ok === true;
  check('C2 판정기 프로세스 — 사실 파일 읽기 → 양성 대조 통과 · 끝 코드 0', pcOk,
    `끝 코드 ${g.judge.code} · ${g.judge.end}${g.res && g.res.positive ? ` · ${String(g.res.positive.why).slice(0, 120)}` : ''}${g.judge.err ? ` · ${g.judge.err}` : ''}`);

  // ── 깨뜨린 표본 ① 잘린 양식지 PDF → 판독 = ok 아님 → 양성 대조 실패 → 그 런 전체 미측정(빨강 0) ──
  const b1 = oneSet(dll, path.join(work, 'broken-truncated'), { truncate: 'pre' });
  const b1rows = (b1.res && b1.res.rows) || [];
  const b1cap = b1rows.filter((r) => r.id === F.POSITIVE_CONTROL.basicForm.cell || r.id === F.POSITIVE_CONTROL.preprint.cell);
  const b1read = b1.read.find((x) => x.k === 'pre') || {};
  check('C2 대조 ① 잘린 PDF → 양성 대조 실패 · 끝 코드 1 · 빨강 0',
    b1.judge.code === 1 && /양성 대조 실패/.test(b1.judge.end) && !b1read.firstOk && b1.res && b1.res.counts && (b1.res.counts['빨강'] || 0) === 0,
    `read(잘린 쪽) 끝 코드 ${b1read.code} · ok 줄 ${b1read.firstOk} · 판정 끝 코드 ${b1.judge.code} · ${b1.judge.end} · 양성 칸 ${b1cap.map((r) => r.cat).join('/')}`);

  // ── 깨뜨린 표본 ② 기본 폼에 「공급가액」 없음 → 양성 대조 실패(사유에 그 낱말) ──
  const b2 = oneSet(dll, path.join(work, 'broken-noword'), { dropWord: true });
  const b2why = (b2.res && b2.res.positive && b2.res.positive.why) || '';
  check('C2 대조 ② 「공급가액」 뺀 기본 폼 → 양성 대조 실패 · 사유 낱말',
    b2.judge.code === 1 && /양성 대조 실패/.test(b2.judge.end) && /「공급가액」 못 읽음/.test(b2why),
    `판정 끝 코드 ${b2.judge.code} · ${b2.judge.end} · 사유 ${b2why.slice(0, 80)}`);

  console.log(`사슬 시험 끝 — 통과 ${pass} · 실패 ${fail}`);
  return fail === 0 ? 0 : 1;
}

if (require.main === module) process.exitCode = main(process.argv[2], process.argv[3]);
module.exports = { main, specs, SAMPLE };
