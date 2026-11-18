'use strict';
// W-G1-51 갈래 Z — 인쇄 판정기 (의존 0 · 판정은 여기 한 곳 · 판독기 PrintProbe 는 사실만)
//   근거: 설계 §1-3(된다 / 안 된다 · F4 양성 대조 · 칸 쪼개기 F9) · §7-2 ⑨ 셈(S1 누락 정의 · S2 최소 유효 · S3 N 제외)
//         작업지시서 §4-2(HQA ② 양성 대조 실패 = 음성 제외 · ⑦ 예상 = 실제 사유일 때만 검출 · ⑧ 문 없음 = 분모 제외)
//   쓰는 법: node judge-print.js <out 폴더>   — <out>/cells.json(capture.js 가 씀) + 칸마다 PDF 옆 사실 JSON(PrintProbe read)
//            → <out>/print-result.json · <out>/print-result.md · 끝 줄 1개
//   끝 코드: 0 = 판정표를 냄(빨강이 있어도 — main 대조판은 빨강이 기대) · 1 = 장치 고장(판독기 양성 대조 실패 · 입력 꼴 깨짐)
const fs = require('fs');
const path = require('path');

const CAT = {
  GREEN: '초록', RED: '빨강', UNMEASURED: '미측정', SETUP: '차림 불가', NO_DOOR: '문 없음 · 판정 제외',
  EXCLUDED: '판정 제외', NA: '해당 없음', CHANGED: '바뀐 모양',
};

// ── 사실 읽기 도우미 ──
const pagesOf = (f) => (f && Array.isArray(f.pages) ? f.pages : []);
const wordsOf = (f) => pagesOf(f).flatMap((p) => (Array.isArray(p.words) ? p.words : []));
const imagesOf = (f) => pagesOf(f).flatMap((p) => (Array.isArray(p.images) ? p.images : []));
const drawsOf = (f) => pagesOf(f).flatMap((p) => (Array.isArray(p.draws) ? p.draws : []));
const fontsOf = (f) => pagesOf(f).flatMap((p) => (Array.isArray(p.fonts) ? p.fonts : []));

function inBox(w, b) {
  if (!b) return true;
  return w.x >= b.x && w.y >= b.y && w.x + w.w <= b.x + b.w && w.y + w.h <= b.y + b.h;
}
const isPlaceholder = (t) => /[{}]/.test(String(t));
function numOf(t) {
  const s = String(t).replace(/,/g, '');
  return /^-?\d+(\.\d+)?$/.test(s) ? Number(s) : NaN;
}
function wordMatches(t, want) {
  if (isPlaceholder(t) || String(t).trim() === '') return false;
  if (want === '*any') return true;
  if (want === '*num') return !Number.isNaN(numOf(t));
  const m = /^\*num=(-?[\d.]+)$/.exec(want);
  if (m) return numOf(t) === Number(m[1]);
  return String(t) === String(want);
}

/** 그림이 차림 그림에서 나왔나 — 1순위 해시 · 2순위 4분면 밝기 순서 + 종횡비 ±2%(설계 §1-3 ① ⓑ · G11). */
function imageFrom(img, ref) {
  if (!img || !ref) return { hit: false, how: '-' };
  if (ref.sha && img.sha === ref.sha) return { hit: true, how: '해시' };
  if (Array.isArray(img.quad) && img.quad.length === 4 && Array.isArray(ref.quad) && ref.quad.length === 4) {
    const order = (q) => [0, 1, 2, 3].sort((a, b) => q[b] - q[a]).join('');
    const sorted = [...img.quad].sort((a, b) => b - a);
    // 허용 = 순서 일치 + 이웃 밝기 차 ≥ 0.02(설계 §1-2 G11) · 종횡비 = 그림 표본 수(없으면 놓인 상자) ±2%
    const neighbor = sorted.every((v, i) => i === 0 || sorted[i - 1] - v >= 0.02);
    const ratio = img.ws > 0 && img.hs > 0 ? img.ws / img.hs : (img.w > 0 && img.h > 0 ? img.w / img.h : NaN);
    const aspectOk = ref.aspect > 0 && Number.isFinite(ratio) && Math.abs(ratio / ref.aspect - 1) <= 0.02;
    if (order(img.quad) === order(ref.quad) && neighbor && aspectOk) return { hit: true, how: '4분면 순서 · 종횡비' };
  }
  return { hit: false, how: '-' };
}

/** 그림을 그린 자리의 실효 불투명도 = ExtGState ca × 묶음 ca × (그림 소프트 마스크 평균 · 있을 때). */
function effectiveAlpha(draw) {
  if (!draw || typeof draw.alpha !== 'number') return null;
  let a = draw.alpha;
  if (draw.imgSMask === true) {
    if (typeof draw.smaskMean !== 'number') return null;
    a *= draw.smaskMean;
  }
  return a;
}

// 🔴 3차(11/19 · [4] F6 · HQA ⑦) — 판정 사유를 산문(why)과 따로 **꼴이 고정된 사유 글자(code)** 로도 낸다.
//   검출 = 런 전 고정한 예상 code 와 실제 code 가 글자 그대로 같을 때만(산문끼리 비교하면 늘 「검출 아님」 이 됨 · [4] F6).
//   code 꼴: 칸 = 실패한 기준 code 를 「 · 」 로 이은 것(기준 정의 차례) · ⑨ = `누락:<행,…>`(E_ROWS 차례) · 응답 = `응답:<코드>`.
const failCode = (r, code) => (r.ok ? r : { ...r, code });

/** 스타일 비교 열쇠(F3 「4종 중 둘이 같은 PDF」) — 숫자 든 낱말(일자 · 번호 · 쪽 번호 · 시각)은 뺀다(설계 :75 「날짜 · 쪽번호 같은 아무 차이는 초록 아님」). */
function styleKey(f) {
  const words = wordsOf(f).filter((w) => !/\d/.test(String(w.t)));
  const rules = pagesOf(f).flatMap((p) => (Array.isArray(p.rules) ? p.rules : []));
  const wk = words.map((w) => `${w.t}@${Math.round(w.x * 2) / 2},${Math.round(w.y * 2) / 2},${typeof w.fs === 'number' ? Math.round(w.fs * 2) / 2 : '-'}`).sort();
  const rk = rules.map((r) => `${Math.round(r.y * 2) / 2}:${Math.round(r.t * 10) / 10}`).sort();
  return { n: words.length, key: `${wk.join('|')}#${rk.join('|')}` };
}

/** 첫 쪽 제목 줄 — 가장 큰 글자 낱말들 중 가장 위 줄(격식 = 자간이 넓어 낱말이 쪼개질 수 있음 · 시안 §2-2). */
function titleLine(p0) {
  const words = (p0 && Array.isArray(p0.words) ? p0.words : []).filter((w) => typeof w.fs === 'number');
  if (words.length === 0) return null;
  const top = Math.max(...words.map((w) => w.fs));
  const big = words.filter((w) => Math.abs(w.fs - top) <= 0.25);
  const y0 = Math.min(...big.map((w) => w.y));
  const line = big.filter((w) => Math.abs(w.y - y0) <= 2);
  const x1 = Math.min(...line.map((w) => w.x));
  const x2 = Math.max(...line.map((w) => w.x + w.w));
  return { top, x1, x2, cx: (x1 + x2) / 2, bottom: Math.max(...line.map((w) => w.y + w.h)), text: line.map((w) => w.t).join('') };
}

// ── 기준 하나 재기 → { ok, why, code? } (neg = 「없음」 기준) ──
function evalCheck(chk, facts, cell, ctx) {
  switch (chk.kind) {
    case 'fields': {
      const words = wordsOf(facts);
      const miss = [];
      for (const f of chk.fields) {
        const hit = words.find((w) => inBox(w, f.box) && wordMatches(w.t, f.want));
        if (!hit) miss.push(f.key);
      }
      return failCode({ ok: miss.length === 0, why: miss.length ? `칸 값 없음 또는 상자 밖: ${miss.join(' · ')}` : `칸 ${chk.fields.length}/${chk.fields.length} 상자 안` }, `칸밖:${miss.join(',')}`);
    }
    case 'noPlaceholder': {
      const bad = wordsOf(facts).filter((w) => isPlaceholder(w.t));
      return failCode({ ok: bad.length === 0, why: bad.length ? `자리표 글자 ${bad.length}` : '자리표 0' }, '자리표');
    }
    case 'noStdHeaderOutside': {
      const bad = wordsOf(facts).filter((w) => chk.words.includes(w.t) && !chk.boxes.some((b) => inBox(w, b)));
      return failCode({ ok: bad.length === 0, why: bad.length ? `표준 표 머리 칸 밖 ${bad.map((w) => w.t).join(' · ')}` : '표준 표 머리 칸 밖 0' }, '표준머리');
    }
    case 'image': {
      const hits = imagesOf(facts).map((im) => imageFrom(im, chk.ref)).filter((r) => r.hit);
      if (chk.want === 'present') return failCode({ ok: hits.length > 0, why: hits.length ? `양식 그림 있음(${hits[0].how})` : '양식 그림 0' }, '그림없음');
      return failCode({ ok: hits.length === 0, why: hits.length ? `그림 있음(${hits[0].how}) — 없어야 함` : '그 그림 0' }, '그림있음');
    }
    case 'watermark': {
      const imgs = imagesOf(facts);
      const fromLogo = imgs.filter((im) => imageFrom(im, chk.ref).hit);
      if (fromLogo.length === 0) {
        const groupsOnly = drawsOf(facts).length === 0 && pagesOf(facts).some((p) => p.drawsOk === true);
        return { ok: false, why: imgs.length === 0 ? `그림 객체 0${groupsOnly ? '(빈 투명 묶음 꼴 포함)' : ''}` : '그림이 로고에서 안 나옴' };
      }
      const shas = new Set(fromLogo.map((im) => im.sha));
      const draws = drawsOf(facts).filter((d) => d.sha && shas.has(d.sha));
      if (pagesOf(facts).some((p) => p.drawsOk === false)) return { ok: false, unmeasured: true, why: '판독기: 내용 흐름 해석 실패(실효 불투명도 미측정)' };
      if (draws.length === 0) return { ok: false, unmeasured: true, why: '로고 그림을 그린 자리를 못 찾음(판독기)' };
      const alphas = draws.map(effectiveAlpha);
      if (alphas.some((a) => a === null)) return { ok: false, unmeasured: true, why: '소프트 마스크 평균 못 구함' };
      const good = alphas.filter((a) => a > 0 && Math.abs(a - chk.alpha) <= chk.tol);
      return failCode({ ok: good.length > 0, why: `로고 그림 ${fromLogo.length} · 실효 불투명도 ${alphas.map((a) => a.toFixed(3)).join(',')} · 기준 ${chk.alpha}±${chk.tol}` }, '워터마크');
    }
    case 'style': {
      // ⬛ [낡은 줄 · 2차] 제목 크기 + 명조 글꼴만 — [4] F3 「머리 배치 · 선 굵기 · 품명 잘림 0 빠짐」 → 아래 3차
      // const words = wordsOf(facts).filter((w) => typeof w.fs === 'number');
      // const top = Math.max(...words.map((w) => w.fs));
      // const ok = Math.abs(top - chk.titlePt) <= 0.5 && (!chk.serif || serif);
      // 🔴 3차(11/19 · [4] F3 · 시안 §2-1 · §2-2 · 설계 :75) — 표지 = ⓐ 제목 크기 ⓑ 격식 명조 ⓒ 머리 배치(제목 왼쪽 / 가운데)
      //   ⓓ 머리 아래 선 굵기(이중선이면 두 줄) ⓔ 큰글씨 품명 잘림 0. 하나라도 틀리면 빨강 · 판독기 사실이 없으면 미측정.
      const p0 = pagesOf(facts)[0];
      const tl = titleLine(p0);
      if (!tl) return { ok: false, unmeasured: true, why: '글자 크기 사실 없음(판독기)' };
      if (!(p0.wMm > 0)) return { ok: false, unmeasured: true, why: '쪽 폭 사실 없음(판독기)' };
      if (!Array.isArray(p0.rules)) return { ok: false, unmeasured: true, why: '선 사실 없음(판독기 · rules)' };
      const bad = [];
      const parts = [];
      const sizeOk = Math.abs(tl.top - chk.titlePt) <= 0.5;
      parts.push(`제목 크기 ${tl.top}pt(기준 ${chk.titlePt})`);
      if (!sizeOk) bad.push('크기');
      const serif = fontsOf(facts).some((n) => /NotoSerifKR|Noto Serif KR/i.test(String(n).replace(/^[A-Z]{6}\+/, '')));
      parts.push(`명조 글꼴 ${serif}${chk.serif ? '(필요)' : ''}`);
      if (chk.serif && !serif) bad.push('명조');
      const centered = Math.abs(tl.cx - p0.wMm / 2) <= 10;
      const left = tl.x1 < p0.wMm * 0.25 && tl.cx < p0.wMm / 2 - 10;
      const alignOk = chk.align === 'center' ? centered : left;
      parts.push(`제목 자리 ${tl.x1.toFixed(1)}~${tl.x2.toFixed(1)}mm(기준 ${chk.align === 'center' ? '가운데' : '왼쪽'})`);
      if (!alignOk) bad.push('배치');
      if (p0.rules.length === 0) return { ok: false, unmeasured: true, why: `${parts.join(' · ')} · 쪽 선 0(판독기가 선을 못 봄)` };
      const under = p0.rules.filter((r) => r.y >= tl.bottom - 0.5 && r.y <= tl.bottom + 60 && r.w >= p0.wMm * 0.4).sort((a, b) => a.y - b.y);
      const want = chk.rules || [];
      let ruleOk = under.length > 0 && Math.abs(under[0].t - want[0]) <= 0.15;
      if (ruleOk && want.length > 1) {
        const second = under.slice(1).find((r) => r.y - under[0].y >= 0.3 && r.y - under[0].y <= 4);
        ruleOk = !!second && Math.abs(second.t - want[1]) <= 0.15;
        parts.push(`머리 아래 선 ${under[0].t}pt + ${second ? `${second.t}pt` : '둘째 줄 없음'}(기준 ${want.join(' + ')})`);
      } else parts.push(`머리 아래 선 ${under.length ? `${under[0].t}pt` : '없음'}(기준 ${want.join(' + ')})`);
      if (!ruleOk) bad.push('선');
      if (Array.isArray(chk.noCut) && chk.noCut.length) {
        const joined = wordsOf(facts).map((w) => String(w.t)).join('');
        const cut = chk.noCut.filter((name) => !joined.includes(name));
        parts.push(cut.length ? `품명 잘림 ${cut.join(' · ')}` : '품명 잘림 0');
        if (cut.length) bad.push('잘림');
      }
      return failCode({ ok: bad.length === 0, why: parts.join(' · ') }, `스타일:${bad.join(',')}`);
    }
    case 'sameWords': {
      // 🔴 3차([3-V] A3) — 두 PDF 글자가 0 이면 열쇠 '' = '' 로 「같음」 초록이 섰다 → 한쪽이라도 글자 0 · 양성 대조 실패 = 미측정
      if (ctx.pcOk !== true) return { ok: false, unmeasured: true, why: '양성 대조 실패 — 회귀 대조 무효' };
      const other = ctx.factsById[chk.as];
      if (!other || other.ok !== true) return { ok: false, unmeasured: true, why: `대조 칸 ${chk.as} 사실 없음` };
      if (wordsOf(facts).length === 0 || wordsOf(other).length === 0) return { ok: false, unmeasured: true, why: `글자 0 — ${chk.as} 와 대조 불가(판독기)` };
      const key = (f) => wordsOf(f).map((w) => `${w.t}@${Math.round(w.x * 2) / 2},${Math.round(w.y * 2) / 2}`).sort().join('|');
      const same = key(facts) === key(other);
      return failCode({ ok: same, why: same ? `${chk.as} 와 글자 · 자리 같음` : `${chk.as} 와 글자 · 자리 다름(스타일이 먹음)` }, '회귀다름');
    }
    case 'distinctFrom': {
      // 🔴 3차([4] F3 · 설계 :75 「4종 중 둘이 같은 PDF」 = 안 됨) — 숫자 든 낱말 뺀 글자 · 자리 · 크기 · 선 열쇠가 같으면 빨강
      const mine = styleKey(facts);
      if (mine.n === 0) return { ok: false, unmeasured: true, why: '글자 0 — 스타일 대조 불가(판독기)' };
      const same = [];
      const miss = [];
      for (const id of chk.ids) {
        const o = ctx.factsById[id];
        const k = o && o.ok === true ? styleKey(o) : null;
        if (!k || k.n === 0) miss.push(id);
        else if (k.key === mine.key) same.push(id);
      }
      if (same.length) return { ok: false, why: `같은 PDF(글자 · 자리 · 크기 · 선): ${same.join(' · ')}`, code: '스타일:같은PDF' };
      if (miss.length) return { ok: false, unmeasured: true, why: `스타일 대조 칸 사실 없음 ${miss.join(' · ')}` };
      return { ok: true, why: `다른 ${chk.ids.length}종과 다름` };
    }
    case 'uiNoWarn': {
      const n = cell.ui && typeof cell.ui.warnCount === 'number' ? cell.ui.warnCount : null;
      if (n === null) return { ok: false, unmeasured: true, why: '미리보기 창 경고 수 기록 없음' };
      return failCode({ ok: n === 0, why: `미리보기 창 경고 ${n}` }, '경고');
    }
    case 'noWords': {
      const bad = wordsOf(facts).filter((w) => chk.words.some((x) => String(w.t).includes(x)));
      return failCode({ ok: bad.length === 0, why: bad.length ? `글자 있음 ${bad.map((w) => w.t).join(' · ')}` : '안내 · 경고 글자 0' }, '글자있음');
    }
    case 'uiFacts':
      return { ok: true, record: true, why: cell.ui ? JSON.stringify(cell.ui) : '기록 없음' };
    default:
      return { ok: false, unmeasured: true, why: `모르는 기준 ${chk.kind}` };
  }
}

/** 판독기 양성 대조(F4 ⑴) — 「있음」 을 내야 하는 PDF 2개. 하나라도 못 읽으면 음성 기준 전부 무효. */
function positiveControl(pc, factsById, probeSelftestOk) {
  const why = [];
  // ⬛ [낡은 줄 · 2차] if (probeSelftestOk === false) why.push('판독기 자체시험(T5 포함) 실패');
  // 🔴 3차([3-V] A7) — 값이 비어도(기록 없음) 실패로 센다.
  if (probeSelftestOk !== true) why.push(`판독기 자체시험(T5 포함) 실패 또는 기록 없음(${probeSelftestOk})`);
  if (!pc || !pc.basicForm || !pc.preprint) return { ok: false, why: '양성 대조 정의 없음' };
  const basic = factsById[pc.basicForm.cell];
  if (!basic || basic.ok !== true) why.push(`기본 폼 PDF 없음(${pc.basicForm.cell})`);
  else {
    // ⬛ [낡은 줄 · 2차] const ws = wordsOf(basic).map((w) => w.t); — 낱말 있음만 봄
    // 🔴 3차([4] F4 · 설계 :88 「글자와 좌표」) — 낱말 있음 + 좌표: 쪽 안 · 상자 > 0 · 같은 줄(표 머리) · 왼쪽 → 오른쪽 차례
    const ws = wordsOf(basic);
    const found = pc.basicForm.words.map((t) => ws.find((w) => w.t === t));
    pc.basicForm.words.forEach((t, k) => { if (!found[k]) why.push(`기본 폼에서 「${t}」 못 읽음`); });
    if (found.every(Boolean)) {
      const p0 = pagesOf(basic)[0] || {};
      const inPage = (w) => w.w > 0 && w.h > 0 && w.x >= 0 && w.y >= 0
        && (!(p0.wMm > 0) || w.x + w.w <= p0.wMm) && (!(p0.hMm > 0) || w.y + w.h <= p0.hMm);
      if (!found.every(inPage)) why.push('기본 폼 글자 좌표 쪽 밖 또는 상자 0');
      const ys = found.map((w) => w.y);
      if (Math.max(...ys) - Math.min(...ys) > (pc.basicForm.sameRowMm || 1.5)) why.push(`기본 폼 「${pc.basicForm.words.join(' · ')}」 한 줄 아님(y ${ys.join(',')})`);
      if (!found.every((w, k) => k === 0 || found[k - 1].x < w.x)) why.push(`기본 폼 「${pc.basicForm.words.join(' → ')}」 왼쪽 → 오른쪽 차례 아님`);
    }
  }
  const pre = factsById[pc.preprint.cell];
  if (!pre || pre.ok !== true) why.push(`양식지 PDF 없음(${pc.preprint.cell})`);
  else {
    // 🔴 3차([4] F4 · 설계 :88 「K2 양식지 = 그림 해시 · 칸 값」 · 하위 기준마다 「있음」)
    if (imagesOf(pre).length === 0) why.push('양식지 미리보기에서 그림 못 읽음');
    else if (pc.preprint.ref && !imagesOf(pre).some((im) => imageFrom(im, pc.preprint.ref).hit)) why.push('양식지 그림이 차림 그림에서 안 나옴(해시 · 4분면 순서)');
    if (wordsOf(pre).length === 0) why.push('양식지 미리보기에서 글자 못 읽음');
    if (Array.isArray(pc.preprint.fields) && pc.preprint.fields.length) {
      const r = evalCheck({ kind: 'fields', fields: pc.preprint.fields }, pre, {}, {});
      if (!r.ok) why.push(`양식지 ${r.why}`);
    }
  }
  return { ok: why.length === 0, why: why.length ? why.join(' · ') : '기본 폼 글자 · 좌표 · 양식지 그림 해시 · 칸 값 있음 · 판독기 자체시험 통과' };
}

/** 칸 하나 판정. */
function judgeCell(cell, ctx) {
  const board = ctx.board;
  if (cell.noDoorAtMain && board === 'main') return { cat: CAT.NO_DOOR, why: cell.noDoorAtMain };
  if (cell.status === 'setup-fail') return { cat: CAT.SETUP, why: cell.reason || '차림 실패' };
  if (cell.status === 'na') return { cat: CAT.NA, why: cell.reason || '' };
  if (cell.status === 'excluded') return { cat: CAT.EXCLUDED, why: cell.reason || '' };
  if (cell.status === 'unmeasured') return { cat: CAT.UNMEASURED, why: cell.reason || '' };
  // 🔴 3차 — 기록 칸(S)은 판정이 아니라 기록이라 양성 대조 앞에서 처리(자리만 옮김 · 내용 무변)
  if (cell.status === 'recorded') {
    const r = (cell.expect || []).map((c) => evalCheck(c, null, cell, ctx));
    return { cat: CAT.NA, why: `기록: ${r.map((x) => x.why).join(' · ')}` };
  }
  // 🔴 3차([4] F1 · 설계 :88 「하나라도 못 읽으면 판독기 고장 = 그 런 전체 「미측정」」) — 양성 대조 실패(또는 값 없음)면
  //   판정 칸은 빨강도 남기지 않고 전부 미측정(2차는 음성 기준만 미측정 → main 「기대한 빨강」 이 공허하게 남았다).
  if (ctx.pcOk !== true) return { cat: CAT.UNMEASURED, why: '양성 대조 실패 — 그 런 전체 미측정(설계 :88 · 빨강도 남기지 않음)' };
  if (cell.status === 'capture-fail') {
    if (cell.errorIsDefect && cell.http && cell.http.status) {
      return { cat: CAT.RED, why: `응답 ${cell.http.status} · ${String(cell.http.firstLine || '').slice(0, 120)}`, code: `응답:${cell.http.status}` };
    }
    return { cat: CAT.UNMEASURED, why: cell.reason || 'PDF 못 받음' };
  }
  // ⬛ [낡은 줄 · 2차] if (cell.status === 'recorded') { … } — 위로 옮김
  // 🔴 3차([3-V] A2) — 기준이 0개인 칸(K5 · YN1 의 merged 판 등)은 열리기만 해도 초록이 섰다 → 「미측정(기준 없음)」
  if (!Array.isArray(cell.expect) || cell.expect.length === 0) return { cat: CAT.UNMEASURED, why: '기준 없음 — 미측정(기준 0개 칸 · [3-V] A2 · R 런 전 merged 판 기준 정의)' };
  const facts = ctx.factsById[cell.id];
  if (!facts) return { cat: CAT.UNMEASURED, why: '사실 JSON 없음(판독기 미실행)' };
  if (facts.ok !== true) return { cat: CAT.UNMEASURED, why: `판독기 열기 실패 ${facts.error || ''}` };
  const res = cell.expect.map((c) => ({ c, r: evalCheck(c, facts, cell, ctx) }));
  const posFail = res.filter((x) => !x.c.neg && !x.r.ok && !x.r.unmeasured);
  const unm = res.filter((x) => x.r.unmeasured);
  const negFail = res.filter((x) => x.c.neg && !x.r.ok && !x.r.unmeasured);
  const why = res.map((x) => `${x.r.ok ? '○' : '×'} ${x.r.why}`).join(' · ');
  const code = res.filter((x) => !x.r.ok && !x.r.unmeasured).map((x) => x.r.code || x.c.kind).join(' · ');
  if (posFail.length) return { cat: CAT.RED, why, code };
  if (unm.length) return { cat: CAT.UNMEASURED, why: `판독기 · ${unm.map((x) => x.r.why).join(' · ')}` };
  // ⬛ [낡은 줄 · 2차] negFail · hasNeg 를 ctx.pcOk 로 갈랐다 — 3차는 위에서 양성 대조 실패를 통째로 미측정으로 돌림
  if (negFail.length) return { cat: CAT.RED, why, code };
  return { cat: cell.changedShape ? CAT.CHANGED : CAT.GREEN, why };
}

/** main 대조 규칙(설계 §1-3 대조군 · F2 ⑶) — main 에서 빨강 기대인데 초록 = 그 측정이 실패를 못 잡음 → 멈춤 ⓐ. */
function mainStops(rows, board) {
  if (board !== 'main') return [];
  const stops = [];
  for (const r of rows) {
    if (r.mainExpect === '빨강' && (r.cat === CAT.GREEN || r.cat === CAT.CHANGED)) stops.push(`${r.id} main 초록 — 측정 재설계 · PM 보고`);
  }
  const kRows = rows.filter((r) => /^K[1-5]$/.test(r.k || ''));
  const kJudged = kRows.filter((r) => r.cat === CAT.GREEN || r.cat === CAT.RED || r.cat === CAT.CHANGED);
  if (kJudged.length > 0 && kJudged.every((r) => r.cat !== CAT.RED)) stops.push('K1~K5 판정 칸 전부 main 초록 — 대표 실패를 못 잡음(멈춤 ⓐ)');
  const wmA = rows.filter((r) => r.group === '①(가)' && /-on\./.test(r.id));
  if (wmA.length && wmA.every((r) => r.cat === CAT.GREEN)) stops.push('①(가) main 초록 — 차림이 대표 화면 조건을 못 만듦 · 멈춤 · 측정 재설계');
  return stops;
}

function composite(group, byId) {
  const cats = group.cells.map((id) => (byId[id] ? byId[id].cat : CAT.UNMEASURED));
  let cat = CAT.GREEN;
  if (cats.includes(CAT.SETUP)) cat = CAT.SETUP;
  else if (cats.includes(CAT.UNMEASURED)) cat = CAT.UNMEASURED;
  else if (cats.includes(CAT.RED)) cat = CAT.RED;
  else if (cats.every((c) => c === CAT.NO_DOOR)) cat = CAT.NO_DOOR;
  // 🔴 3차([3-V] A8) — 칸이 전부 해당 없음 · 판정 제외 · 바뀐 모양이면 묶음 초록이 섰다 → 초록 칸 ≥1 일 때만 묶음 초록
  else if (!cats.includes(CAT.GREEN)) cat = CAT.UNMEASURED;
  return { id: group.id, cat, cells: group.cells.length };
}

// ── ⑨ 셈(설계 §7-2 · 보안상무 S1 · S2 · S3 · CTO §4 조건 5 · 7) ──
const E_ROWS = ['E1', 'E2', 'E3', 'E4', 'E5', 'E6', 'E7', 'E7a', 'E7b', 'E7c', 'E8'];
/**
 * @param manifest PrintProbe samples 의 manifest.json(rows[].validity)
 * @param outcomes { [id]: { decision: '허용'|'거절', parseStart: n, marker: bool, deepWarn: bool, copy: bool } } — 제품이 낸 결과(고침 R1 · R3)
 * @param board 'main' 이면 문 없음 · 판정 제외(C9 · HQA ⑧)
 */
function nineCount(manifest, outcomes, board, n1) {
  // 🔴 3차([3-V] A9 · CTO §4-2 조건 7) — qpdf 판을 결과표에 싣도록 셈 결과에 붙인다(manifest 의 qpdfVersion)
  const qpdf = qpdfOf(manifest);
  // 🔴 3차-a(PM 11/19 판단 3 · CTO 조건 7 「같은 계열 · 판 기록」) — ⒜ 암호 걸기(QuestPDF DocumentOperation)가 쓰는 동봉 qpdf 판도 붙인다
  const qpdfBundled = qpdfBundledOf(manifest);
  // ⬛ [낡은 줄 · 2차] if (board === 'main') return { cat: CAT.NO_DOOR, why: …, omission: null };
  // ⬛ [낡은 줄 · 3차] if (board === 'main') return { cat: CAT.NO_DOOR, why: 'main = PDF 받기 0(C9) · 분모 밖(HQA ⑧)', omission: null, qpdf, code: '' };
  if (board === 'main') return { cat: CAT.NO_DOOR, why: 'main = PDF 받기 0(C9) · 분모 밖(HQA ⑧)', omission: null, qpdf, qpdfBundled, code: '' };
  const r9 = nineCountCore(manifest, outcomes, n1);
  // ⬛ [낡은 줄 · 3차] return { ...r9, qpdf };
  return { ...r9, qpdf, qpdfBundled };
}
/** ⒞ = 시험 잡 qpdf 명령의 `qpdf --version`(manifest.qpdfVersion). */
function qpdfOf(manifest) {
  if (!manifest) return '매니페스트 없음';
  if (manifest.qpdfVersion) return String(manifest.qpdfVersion);
  return manifest.qpdfMissing ? '없음(이 잡 qpdf 0 또는 설치 실패)' : '기록 없음';
}
/**
 * 🔴 3차-a(PM 11/19 판단 3 · CTO 조건 7) — ⒜ = QuestPDF 동봉 qpdf 판(PrintProbe samples 가 런 안에서 읽어 manifest.qpdfBundledVersion 에 씀).
 *   못 읽었으면 QuestPDF 패키지 판 + 「동봉 qpdf 판 미측정」(manifest.qpdfBundledWhy = 사유).
 */
function qpdfBundledOf(manifest) {
  if (!manifest) return '매니페스트 없음';
  const pkg = manifest.questPdfVersion ? `QuestPDF ${manifest.questPdfVersion}` : 'QuestPDF 판 기록 없음';
  if (manifest.qpdfBundledVersion) return `qpdf ${manifest.qpdfBundledVersion}(${pkg} 동봉)`;
  const why = manifest.qpdfBundledWhy && /동봉 qpdf 판 미측정/.test(manifest.qpdfBundledWhy) ? manifest.qpdfBundledWhy : '동봉 qpdf 판 미측정';
  return `${why} · ${pkg}`;
}
function nineCountCore(manifest, outcomes, n1) {
  const rows = Object.fromEntries((manifest && Array.isArray(manifest.rows) ? manifest.rows : []).map((r) => [r.id, r]));
  const valid = (id) => rows[id] && rows[id].validity === '유효';
  const o = outcomes || {};
  const p1 = o.P1;
  const p1Ok = valid('P1') && p1 && p1.decision === '허용' && (p1.copy === true) && (p1.parseStart || 0) === 1;
  const negIds = [...E_ROWS, 'P2', 'P3', 'P4', 'U1', 'U2'];
  if (!p1Ok) {
    return {
      cat: CAT.UNMEASURED, why: '같은 런 P1 양성 대조 실패 — E · P · U · N 음성 결과 전부 제외(HQA ② · S3)', omission: null,
      rows: Object.fromEntries(negIds.map((id) => [id, CAT.UNMEASURED])), n1: n1 ? Object.fromEntries(Object.keys(n1).map((k) => [k, CAT.UNMEASURED])) : {},
    };
  }
  const per = {};
  let omission = 0;
  let eValid = 0;
  let eMeasured = 0;
  let noResult = 0;
  const omitted = [];
  const noResultIds = [];
  for (const id of E_ROWS) {
    if (!valid(id)) { per[id] = `${CAT.UNMEASURED}(표본 ${rows[id] ? rows[id].validity : '없음'})`; continue; }
    eValid++;
    const r = o[id];
    // ⬛ [낡은 줄 · 2차] if (!r || !r.decision) { per[id] = `${CAT.UNMEASURED}(결과 없음)`; continue; } — 누락에도 막힘에도 안 셈 → ⑨ 초록([3-V] A1)
    // ⬛ [낡은 줄 · 3차] // 🔴 3차([3-V] A1 · PM 11/19 「유효 E 행 + 제품 결과 없음 = 누락으로 셈 · 초록 0」) — 결과 없음 수를 따로 적고 누락에 더한다
    // ⬛ [낡은 줄 · 3차] if (!r || !r.decision) { omission++; noResult++; omitted.push(id); per[id] = `${CAT.RED}(누락 · 결과 없음)`; continue; }
    //   — 3차 PM 글자 「유효 E 행 + 제품 결과 없음 = 누락으로 셈」 은 설계 :89(PM 지시 L88) 「PDF 못 받음 · 로그인 실패 · 시간 넘김 · 4xx/5xx = 미측정(빨강 아님)」 과 어긋남 → PM 정정(11/19)
    // 🔴 3차-a(PM 정정 11/19 · 설계 :89 · :263) — 그 행 = 미측정(초록 0 · 빨강 아님) · 분모 제외 · 「결과 없음 n」 따로 적음
    //   · ⑨ 전체 = 아래 S2(「유효 + 결과 있음」)를 못 채우면 부분 · 결과 있는 E 행 0 이면 미측정(둘 다 합격 0)
    if (!r || !r.decision) { noResult++; noResultIds.push(id); per[id] = `${CAT.UNMEASURED}(결과 없음)`; continue; }
    eMeasured++;
    const trace = (r.parseStart || 0) > 0 || r.marker === true || r.deepWarn === true || r.copy === true;
    const miss = r.decision === '허용' || trace;
    if (miss) { omission++; omitted.push(id); }
    per[id] = miss ? `${CAT.RED}(누락${trace ? ' · 해석 흔적' : ''})` : CAT.GREEN;
  }
  const overReject = { P4: o.P4 && o.P4.decision === '거절' ? 1 : 0, P2: o.P2 && o.P2.decision === '거절' ? 1 : 0 };
  // 🔴 3차-a(PM 11/19 판단 2 · 설계 :263 「P1~P3 · U2 거절 = 결함」 · U1 빨강) — P · U 결함 행을 모아 ⑨ 전체 색에 넣는다(아래)
  //   P1 거절 = 위 S3(같은 런 양성 대조 실패 = E · P · U · N 전부 미측정 · 설계 :254) 그대로 · 과잉 거절 수(P4 · P2) 따로 셈 그대로
  const puDefect = [];
  for (const id of ['P2', 'P3', 'U2']) {
    if (!valid(id)) per[id] = CAT.UNMEASURED;
    else if (!o[id] || !o[id].decision) per[id] = `${CAT.UNMEASURED}(결과 없음)`;
    else per[id] = o[id].decision === '허용' ? CAT.GREEN : `${CAT.RED}(${id === 'P2' ? '과잉 거절 · 따로 셈' : '정상 거절 = 결함'})`;
    if (valid(id) && o[id] && o[id].decision && o[id].decision !== '허용') puDefect.push(id);
  }
  // ⬛ [낡은 줄 · 3차] per.P4 = valid('P4') && o.P4 ? (o.P4.decision === '거절' ? `${CAT.GREEN}(과잉 거절 · 받아들임 · 따로 셈)` : CAT.GREEN) : CAT.UNMEASURED; — 결과 꼴만 있고 decision 없으면 초록
  // ⬛ [낡은 줄 · 3차] per.U1 = valid('U1') && o.U1 ? (o.U1.decision === '거절' && (o.U1.parseStart || 0) === 0 ? CAT.GREEN : CAT.RED) : CAT.UNMEASURED; — decision 없으면 빨강
  // 🔴 3차-a(PM 정정 11/19 · 설계 :89) — 결과 없음(decision 없음) = 미측정(초록 0 · 빨강 아님) — U1 이 ⑨ 전체 색에 들어가므로 같은 규칙으로
  const decided = (id) => valid(id) && o[id] && o[id].decision;
  per.P4 = !valid('P4') ? CAT.UNMEASURED : !decided('P4') ? `${CAT.UNMEASURED}(결과 없음)`
    : (o.P4.decision === '거절' ? `${CAT.GREEN}(과잉 거절 · 받아들임 · 따로 셈)` : CAT.GREEN);
  per.U1 = !valid('U1') ? CAT.UNMEASURED : !decided('U1') ? `${CAT.UNMEASURED}(결과 없음)`
    : (o.U1.decision === '거절' && (o.U1.parseStart || 0) === 0 ? CAT.GREEN : CAT.RED);
  if (per.U1 === CAT.RED) puDefect.push('U1');
  per.U3 = '미측정 · 정적만';
  // ⬛ [낡은 줄 · 2차] S2 = 유효만 봄 → 3차([3-V] A1): 「유효 + 결과 있음」 기준
  const has = (id) => valid(id) && o[id] && o[id].decision;
  const s2 = [];
  if (!(has('E4') || has('E5'))) s2.push('E4 · E5 유효 · 결과 0');
  if (!(has('E7') || has('E7a') || has('E7b'))) s2.push('E7 · E7a · E7b 유효 · 결과 0');
  if (!has('E7c')) s2.push('E7c 무효 또는 결과 없음');
  let cat;
  let why;
  // ⬛ [낡은 줄 · 3차] if (eValid === 0) { cat = CAT.UNMEASURED; why = 'E 행 전부 무효 — ⑨ 미측정 · 합격 0'; }
  // ⬛ [낡은 줄 · 3차] else if (omission > 0) { cat = CAT.RED; why = `누락 ${omission}${noResult ? `(결과 없음 ${noResult} 포함)` : ''} — ㉮ 멈춤 · PM · CTO`; }
  // ⬛ [낡은 줄 · 3차] else if (s2.length) { cat = '부분'; why = `최소 유효 미달(S2): ${s2.join(' · ')} · 합격 0`; }
  // ⬛ [낡은 줄 · 3차] else { cat = CAT.GREEN; why = `누락 0 · 유효 E ${eValid} · 결과 있음 ${eMeasured}`; }
  //   — ⑨ 전체 색이 E 행만 봄(P · U 결함이 행에만 남음 · 명세서 §6-c 범위 밖 발견) · 결과 없음을 누락에 셈(PM 정정)
  // 🔴 3차-a(PM 11/19 판단 1 · 2) — 차례: 빨강(누락 또는 P · U 결함) → 미측정(결과 있는 E 행 0) → 부분(S2) → 초록 · 결과 없음 n 은 늘 따로 적음
  const nr = noResult ? ` · 결과 없음 ${noResult}(${noResultIds.join(',')} · 미측정 · 분모 제외)` : '';
  if (omission > 0 || puDefect.length > 0) {
    cat = CAT.RED;
    why = [
      omission > 0 ? `누락 ${omission} — ㉮ 멈춤 · PM · CTO` : '',
      puDefect.length > 0 ? `P · U 결함 ${puDefect.length}(${puDefect.join(',')} · P1~P3 · U2 거절 = 결함 · U1 빨강) — 합격 0` : '',
    ].filter(Boolean).join(' · ') + nr;
  } else if (eValid === 0) { cat = CAT.UNMEASURED; why = 'E 행 전부 무효 — ⑨ 미측정 · 합격 0'; }
  else if (eMeasured === 0) { cat = CAT.UNMEASURED; why = `E 행 유효 ${eValid} · 결과 있음 0 — ⑨ 미측정 · 합격 0${nr}`; }
  else if (s2.length) { cat = '부분'; why = `최소 유효 미달(S2): ${s2.join(' · ')} · 합격 0${nr}`; }
  else { cat = CAT.GREEN; why = `누락 0 · 유효 E ${eValid} · 결과 있음 ${eMeasured}${nr}`; }
  const n1Rows = {};
  for (const [k, v] of Object.entries(n1 || {})) {
    if (!v || v.setup !== true) n1Rows[k] = `${CAT.UNMEASURED}(차림 못 세움)`;
    else n1Rows[k] = v.underlay === 0 && v.error === 0 && v.native === 0 ? CAT.GREEN : CAT.RED;
  }
  // ⬛ [낡은 줄 · 3차] return { cat, why, omission, noResult, eValid, eMeasured, overReject, rows: per, n1: n1Rows, code: omitted.length ? `누락:${omitted.join(',')}` : '' };
  //   code = 누락 행만(꼴 고정 · plan.json expectedCodes 「누락:<행,…>」 그대로) — P · U 결함은 puDefect · why 에 따로
  return { cat, why, omission, noResult, noResultIds, puDefect, eValid, eMeasured, overReject, rows: per, n1: n1Rows, code: omitted.length ? `누락:${omitted.join(',')}` : '' };
}

/** 심은 결함 검출(HQA ⑦) — 예상 사유(런 전 고정) = 실제 사유일 때만 검출 · 다른 사유 빨강 = 검출 아님. */
function detection(expectReason, actualReason, cat) {
  if (cat !== CAT.RED) return { detected: false, why: `빨강 아님(${cat})` };
  if (!expectReason) return { detected: false, why: '예상 사유 미고정 — 검출 0 으로 셈' };
  const same = String(expectReason).trim() === String(actualReason || '').trim();
  return { detected: same, why: same ? '예상 = 실제' : `다른 사유 빨강 = 검출 아님(실제: ${actualReason})` };
}

/**
 * 🔴 3차([4] F6 · HQA ⑦) — 검출 규칙을 판정 흐름에 연결. expects = plan.json 이번 잡 `expect` 가 가리키는
 *   `expectedCodes` 들({ key, target, code }) · target = 칸 id 또는 'nine'. 실제 = 그 칸(또는 ⑨)의 code(꼴 고정).
 */
function detectionsFor(expects, res, nine) {
  return (expects || []).map((e) => {
    if (!e || typeof e.target !== 'string') return { ...(e || {}), detected: false, why: '예상 꼴 깨짐' };
    if (e.target === 'nine') {
      if (!nine) return { ...e, detected: false, why: '⑨ 결과 없음' };
      return { ...e, actual: nine.code, cat: nine.cat, ...detection(e.code, nine.code, nine.cat) };
    }
    const row = res.rows.find((r) => r.id === e.target);
    if (!row) return { ...e, detected: false, why: `칸 없음 ${e.target}` };
    return { ...e, actual: row.code || '', cat: row.cat, ...detection(e.code, row.code, row.cat) };
  });
}

/** plan.json 에서 이번 런 · 이번 잡의 예상 사유 목록 꺼내기(없으면 빈 목록 · 꼴 깨지면 미고정으로 남김). */
function expectsFromPlan(plan, run, job) {
  const r = plan && plan.runs && plan.runs[run];
  const j = r && Array.isArray(r.jobs) ? r.jobs.find((x) => x.id === job) : null;
  if (!j || !Array.isArray(j.expect)) return [];
  const codes = (plan && plan.expectedCodes) || {};
  return j.expect.map((key) => (codes[key] ? { key, target: codes[key].target, code: codes[key].code } : { key, target: '?', code: null }));
}

function judgeRun(input) {
  const { cells, board, factsById, positive, probeSelftestOk, groups } = input;
  const pc = positiveControl(positive, factsById, probeSelftestOk);
  const ctx = { board, factsById, pcOk: pc.ok };
  const rows = cells.map((c) => ({ id: c.id, group: c.group, k: c.k, type: c.type, path: c.path, mainExpect: c.mainExpect, ...judgeCell(c, ctx) }));
  const byId = Object.fromEntries(rows.map((r) => [r.id, r]));
  const comp = (groups || []).map((g) => composite(g, byId));
  const counts = {};
  for (const v of Object.values(CAT)) counts[v] = 0;
  for (const r of rows) counts[r.cat] = (counts[r.cat] || 0) + 1;
  const stops = mainStops(rows, board);
  const denomOut = counts[CAT.NO_DOOR] + counts[CAT.EXCLUDED] + counts[CAT.NA];
  // ⬛ [낡은 줄 · 2차] pass 가 양성 대조 결과를 안 봤다([4] F1) → 3차: pc.ok 필수
  const pass = pc.ok === true && board !== 'main' && rows.every((r) => [CAT.GREEN, CAT.NO_DOOR, CAT.NA, CAT.EXCLUDED, CAT.CHANGED].includes(r.cat))
    && counts[CAT.GREEN] > 0;
  return { positive: pc, rows, composite: comp, counts, stops, denomOut, pass };
}

function endLine(res) {
  const c = res.counts;
  const det = Array.isArray(res.detections) && res.detections.length
    ? ` · 검출 ${res.detections.filter((d) => d.detected).length}/${res.detections.length}` : '';
  return `판정 끝 — 양성 대조 ${res.positive.ok ? '통과' : '실패'} · 초록 ${c[CAT.GREEN]} · 빨강 ${c[CAT.RED]} · 미측정 ${c[CAT.UNMEASURED]} · 차림 불가 ${c[CAT.SETUP]}`
    + ` · 문 없음 ${c[CAT.NO_DOOR]} · 판정 제외 ${c[CAT.EXCLUDED]} · 해당 없음 ${c[CAT.NA]} · 바뀐 모양 ${c[CAT.CHANGED]} · 분모 밖 ${res.denomOut} · 멈춤 ${res.stops.length}${det}`;
}

function toMarkdown(meta, res, nine) {
  const L = [];
  L.push(`# 인쇄 측정 결과 — 런 ${meta.run || '?'} · 잡 ${meta.job || '?'} · 판 ${meta.board || '?'}`);
  L.push('');
  L.push(`- 판독기 양성 대조(먼저 · F4 ⑴ · HQA ②): **${res.positive.ok ? '통과' : '실패'}** — ${res.positive.why}`);
  L.push('- 대표 DB 조건 재현 = 미측정(고정 줄 · 작업지시서 §4-2 HQA ④)');
  if (res.stops.length) L.push(`- 🔴 멈춤: ${res.stops.join(' / ')}`);
  L.push('');
  L.push('| 칸 | 묶음 | main 기대 | 판정 | 사유 |');
  L.push('|---|---|---|---|---|');
  for (const r of res.rows) L.push(`| ${r.id} | ${r.group} | ${r.mainExpect || '-'} | ${r.cat} | ${String(r.why).replace(/\|/g, '/').slice(0, 300)} |`);
  L.push('');
  L.push('| 묶음 판정 | 판정 | 칸 수 |');
  L.push('|---|---|---|');
  for (const g of res.composite) L.push(`| ${g.id} | ${g.cat} | ${g.cells} |`);
  if (nine) {
    L.push('');
    L.push(`- ⑨ 셈: **${nine.cat}** — ${nine.why}`);
    // 🔴 3차([3-V] A9 · CTO §4-2 조건 7 「qpdf --version 을 결과표에」)
    // ⬛ [낡은 줄 · 3차] L.push(`- ⑨ qpdf 판: ${nine.qpdf || '기록 없음'}`);
    // 🔴 3차-a(PM 11/19 판단 3 · CTO 조건 7 「같은 계열 · 판 기록」) — ⒞ 명령 판 · ⒜ QuestPDF 동봉 판 둘 다
    L.push(`- ⑨ qpdf 판 ⒞ 명령(\`qpdf --version\`): ${nine.qpdf || '기록 없음'}`);
    L.push(`- ⑨ qpdf 판 ⒜ QuestPDF 동봉(DocumentOperation): ${nine.qpdfBundled || '기록 없음'}`);
  } else if (meta.nineQpdf) {
    L.push('');
    // ⬛ [낡은 줄 · 3차] L.push(`- ⑨ 표본 qpdf 판: ${meta.nineQpdf}(⑨ 셈 입력 없음)`);
    L.push(`- ⑨ 표본 qpdf 판 ⒞ 명령(\`qpdf --version\`): ${meta.nineQpdf}(⑨ 셈 입력 없음)`);
    L.push(`- ⑨ 표본 qpdf 판 ⒜ QuestPDF 동봉(DocumentOperation): ${meta.nineQpdfBundled || '기록 없음'}`);
  }
  if (Array.isArray(res.detections) && res.detections.length) {
    // 🔴 3차([4] F6 · HQA ⑦) — 예상 사유(런 전 고정 code) = 실제 code 일 때만 검출
    L.push('');
    L.push('| 예상 사유 | 대상 | 예상 code | 실제 code | 판정 | 검출 |');
    L.push('|---|---|---|---|---|---|');
    for (const d of res.detections) L.push(`| ${d.key || '-'} | ${d.target || '-'} | ${d.code || '미고정'} | ${d.actual || '-'} | ${d.cat || '-'} | ${d.detected ? '검출' : `아님 — ${d.why}`} |`);
  }
  L.push('');
  L.push(endLine(res));
  return L.join('\n');
}

function main(outDir) {
  const cellsPath = path.join(outDir, 'cells.json');
  if (!fs.existsSync(cellsPath)) {
    console.log('⚫ FAIL [장치] 판정기 입력 없음 — cells.json');
    return 1;
  }
  const doc = JSON.parse(fs.readFileSync(cellsPath, 'utf8'));
  if (!doc || doc.schema !== 'print-cells/1' || !Array.isArray(doc.cells)) {
    console.log('⚫ FAIL [장치] 판정기 입력 꼴 다름(print-cells/1)');
    return 1;
  }
  const factsById = {};
  for (const c of doc.cells) {
    if (!c.pdf) continue;
    const fp = path.join(outDir, c.pdf.replace(/\.pdf$/i, '.json'));
    if (fs.existsSync(fp)) factsById[c.id] = JSON.parse(fs.readFileSync(fp, 'utf8'));
  }
  const res = judgeRun({
    cells: doc.cells, board: doc.board, factsById, positive: doc.positive, groups: doc.groups,
    probeSelftestOk: doc.probeSelftestOk,
  });
  let nine = null;
  if (doc.nine) {
    const mf = doc.nine.manifest && fs.existsSync(path.join(outDir, doc.nine.manifest))
      ? JSON.parse(fs.readFileSync(path.join(outDir, doc.nine.manifest), 'utf8')) : null;
    nine = nineCount(mf, doc.nine.outcomes, doc.board, doc.nine.n1);
  }
  // 🔴 3차([3-V] A9) — ⑨ 셈 입력이 없어도 표본 manifest(워크플로 ⑨ 단계가 `nine/` 에 씀)가 있으면 qpdf 판을 결과표에
  const nineMf = path.join(outDir, 'nine', 'manifest.json');
  // ⬛ [낡은 줄 · 3차] const nineQpdf = !nine && fs.existsSync(nineMf) ? qpdfOf(JSON.parse(fs.readFileSync(nineMf, 'utf8'))) : null;
  // 🔴 3차-a(PM 11/19 판단 3) — 같은 manifest 에서 ⒞ 명령 판 · ⒜ 동봉 판 둘 다
  const nineMfDoc = !nine && fs.existsSync(nineMf) ? JSON.parse(fs.readFileSync(nineMf, 'utf8')) : null;
  const nineQpdf = nineMfDoc ? qpdfOf(nineMfDoc) : null;
  const nineQpdfBundled = nineMfDoc ? qpdfBundledOf(nineMfDoc) : null;
  // 🔴 3차([4] F6) — 이번 잡의 예상 사유(plan.json · 런 전 고정)를 판정 흐름에 연결
  let plan = null;
  try {
    plan = JSON.parse(fs.readFileSync(path.join(__dirname, 'plan.json'), 'utf8'));
  } catch (e) {
    console.log(`⚫ [장치] plan.json 못 읽음(${e.name}) — 예상 사유 연결 0`);
  }
  res.detections = detectionsFor(expectsFromPlan(plan, doc.run, doc.job), res, nine);
  fs.writeFileSync(path.join(outDir, 'print-result.json'), JSON.stringify({ meta: { run: doc.run, job: doc.job, board: doc.board }, ...res, nine }, null, 2));
  // ⬛ [낡은 줄 · 3차] fs.writeFileSync(path.join(outDir, 'print-result.md'), toMarkdown({ ...doc, nineQpdf }, res, nine));
  fs.writeFileSync(path.join(outDir, 'print-result.md'), toMarkdown({ ...doc, nineQpdf, nineQpdfBundled }, res, nine));
  for (const s of res.stops) console.log(`🔴 멈춤 ${s}`);
  console.log(endLine(res));
  return res.positive.ok ? 0 : 1;
}

if (require.main === module) process.exitCode = main(process.argv[2] || process.env.PRINT_OUT || '.');

module.exports = {
  CAT, evalCheck, judgeCell, judgeRun, positiveControl, nineCount, detection, imageFrom, effectiveAlpha, mainStops, composite, endLine, toMarkdown, main, inBox, wordMatches,
  detectionsFor, expectsFromPlan, styleKey, titleLine,
  // 🔴 3차-a(PM 11/19 판단 3) — 판 기록 두 칸(자체시험 T-Z14)
  qpdfOf, qpdfBundledOf,
};
