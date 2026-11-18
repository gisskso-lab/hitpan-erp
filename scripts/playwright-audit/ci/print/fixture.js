'use strict';
// W-G1-51 갈래 Z — 차림 정의 (개발명세서 §3 ③ · 설계 §1-2 · §1-3 · 작업지시서 §4-1 슬롯 ㉠)
//   여기는 「무엇을 차리고 무엇을 기대하나」 만 적는다 — 차림 실행 = setup-api.js · capture.js / 판정 = judge-print.js.
//   🔴 이름 · 경로 · 키는 main 코드 실측(줄 번호는 개발명세서 §3 ③ 표):
//     양식 종 키 = FormTemplateService.cs:25~33(`estimate` · `receipt` 꼴) · 칸 키 = PdfRenderService.cs:2996~3025
//     좌표 = 종이 왼쪽 위 원점 mm · 글자 상자 왼쪽 위 = (x_mm, y_mm) · 폭 width_mm(PdfRenderService.cs:2975~2993 PlaceText)
//     좌표는 main 에서 `paperMode == preprint` 일 때만 탄다(PdfRenderService.cs:93 · :97) — 그래서 K1 · K3 · K4 가 main 빨강 기대
const PNG = require('./png');

/** 7종(정정 · 대표 11/18 Q3) — 화면 · 저장 문 · 목록 창 제목 · 거래처 칸 이름 · 그림 표지 글자. */
const TYPES = [
  { key: 'estimate', label: '견적서', route: '/quotations', partnerLabel: '거래처', listTitle: '견적서 목록', mark: 'EST' },
  { key: 'delivery', label: '거래명세서', route: '/deliveries', partnerLabel: '거래처', listTitle: '판매 목록 조회', mark: 'DEL' },
  { key: 'sales_order', label: '수주서', route: '/sales-orders', partnerLabel: '거래처', listTitle: '수주서 목록', mark: 'SO' },
  { key: 'purchase_order', label: '발주서', route: '/purchase-orders', partnerLabel: '공급처', listTitle: '발주서 목록', mark: 'PO' },
  { key: 'receipt', label: '매입처리', route: '/purchases', partnerLabel: '공급처', listTitle: '매입명세서 목록', mark: 'RCV' },
  { key: 'purchase_return', label: '매입 반품', route: '/returns', partnerLabel: '거래처', listTitle: '매입반품 목록', mark: 'PRT', returnType: '매입반품' },
  // 판매 반품 = 거래명세서 확정 → [반품하기] → 저장(화면) · 측정 입구 둘(ⓘ /returns 「판매반품」 · ⓙ /deliveries 반품 줄) — 설계 §1-2 F10
  { key: 'sales_return', label: '판매 반품', route: '/returns', partnerLabel: '거래처', listTitle: '매출반품 목록', mark: 'SRT', returnType: '판매반품', entries: ['i', 'j'] },
];

/** 칸 N = 5(F8 하한) — 거래처명 · 일자 · 품명 1행 · 수량 1행 · 합계. 기준점(mm) · 폭(mm) · 글자(pt). */
const FIELDS = [
  { key: 'partner_name', x: 25, y: 40, w: 70, pt: 10 },
  { key: 'doc_date', x: 110, y: 40, w: 40, pt: 10 },
  { key: 'line.item_name', x: 25, y: 80, w: 55, pt: 10 },
  { key: 'line.qty', x: 85, y: 80, w: 20, pt: 10, align: 'right' },
  { key: 'total_amount', x: 110, y: 190, w: 50, pt: 10, align: 'right' },
];
const ROW_PITCH_MM = 8;
const SHIFT_MM = 5; // 종마다 칸 기준점을 5mm 씩(F3 ⓑ)
const BOX_TOL_MM = 2; // ±2mm(설계 §1-3 ②)
const PT_MM = 25.4 / 72;

/** 종 i 의 칸 좌표 — 저장 문 본문(`fieldCoordsJson`) 꼴 · 판독 상자. */
function fieldsFor(i) {
  return FIELDS.map((f) => ({ ...f, x: f.x + SHIFT_MM * i, y: f.y + SHIFT_MM * i }));
}
function coordsJson(i) {
  const rows = fieldsFor(i).map((f) => ({ key: f.key, x_mm: f.x, y_mm: f.y, font_pt: f.pt, align: f.align || 'left', width_mm: f.w }));
  rows.push({ key: 'row_pitch', x_mm: 0, y_mm: ROW_PITCH_MM });
  return JSON.stringify(rows);
}
/** 판독 상자 — 글자 상자 왼쪽 위 (x, y) · 폭 w · 높이 = 글자 1줄(pt × 1.6) · 사방 ±2mm. */
function boxOf(f) {
  return { x: f.x - BOX_TOL_MM, y: f.y - BOX_TOL_MM, w: f.w + 2 * BOX_TOL_MM, h: f.pt * PT_MM * 1.6 + 2 * BOX_TOL_MM };
}

/** 표지 값 — 화면으로 저장하는 전표 1건(거래처 1 · 품목 2줄). 값 칸 판독에 쓴다. */
const MARK = {
  partner: '인쇄표지상사',
  items: [{ name: '표지품목가', qty: 7, price: 1000 }, { name: '표지품목나', qty: 13, price: 2000 }],
  competitorPrefix: '0-경쟁', // 이름순 앞섬(F6)
  testPrefix: '1-시험',
};

/** 칸마다 기대 글자 — 전표 미리보기 = 표지 값 · 설정 미리보기 = 견본(서버 예시 자료 · 값 모름 → 「자리표 아닌 글자 있음」). */
function fieldWants(i, path) {
  return fieldsFor(i).map((f) => {
    let want = '*any';
    if (path !== 'settings') {
      if (f.key === 'partner_name') want = MARK.partner;
      else if (f.key === 'line.item_name') want = MARK.items[0].name;
      else if (f.key === 'line.qty') want = `*num=${MARK.items[0].qty}`;
      else if (f.key === 'total_amount') want = '*num';
    }
    return { key: f.key, box: boxOf(f), want };
  });
}

const STD_HEAD = ['품명', '수량', '공급가액']; // PdfRenderService.cs:342~346
/** 스타일 4종 표지(시안 §2-2 · §3-3 M2) — 제목 글자 크기 · 격식 = 명조 글꼴. */
const STYLES = [
  { key: 'basic', titlePt: 20, serif: false },
  { key: 'formal', titlePt: 22, serif: true },
  { key: 'simple', titlePt: 18, serif: false },
  { key: 'large', titlePt: 24, serif: false },
];

/** K 경우(설계 §1-2 F2 ⑶ · F12 · 대표 08시②) — 저장 문 본문의 바뀌는 칸. K5 = PDF 양식(C9 · main 문 없음). */
const K_CASES = [
  { k: 'K1', paperMode: 'plain', bg: true, coords: true, printBackground: true, mainExpect: '빨강' },
  { k: 'K2', paperMode: 'preprint', bg: true, coords: true, printBackground: false, mainExpect: '초록일 수 있음' },
  { k: 'K3', paperMode: 'plain', bg: true, coords: false, printBackground: true, mainExpect: '빨강' },
  // K4 = 양식지에서 칸 맞춘 뒤 빈 A4 로 되돌림(저장 2번 · 좌표 남음) · 「바뀐 모양」 칸(대표 Q12)
  { k: 'K4', paperMode: 'plain', bg: true, coords: true, printBackground: false, via: 'preprint', changedShape: true, mainExpect: '빨강' },
  { k: 'K5', noDoorAtMain: 'PDF 양식 올리기 문 없음(C9 · 갈래 F 뒤)', mainExpect: '문 없음' },
];

/** 저장 문 본문(같은 문 · 같은 본문 = main · 고친 판 공통 · F2 ⑴). */
function templateBody(t, i, over) {
  const o = over || {};
  return {
    formType: t.key,
    templateName: `${MARK.testPrefix} ${t.label}`,
    paperMode: o.paperMode || 'plain',
    paperSize: 'A4',
    orientation: 'portrait',
    marginTopMm: 0,
    marginLeftMm: 0,
    marginRightMm: 0,
    marginBottomMm: 0,
    fieldCoordsJson: o.coords ? coordsJson(i) : '',
    backgroundImageUrl: o.bgPath || '',
    showCompanyLogo: true,
    showCompanySeal: false,
    showBorder: true,
    printCopyMode: 'both',
    styleKey: o.styleKey || 'basic',
    showTearLine: false,
    printBackground: !!o.printBackground,
    showWatermark: o.showWatermark === undefined ? false : o.showWatermark,
    watermarkOpacity: 0.12, // WatermarkDefaults.cs:25 · 차림값(설계 §1-2 G11)
    isDefault: true,
  };
}
function competitorBody(t) {
  return { ...templateBody(t, 0, {}), templateName: `${MARK.competitorPrefix} ${t.label}`, isDefault: false };
}

/**
 * 판독 칸 목록 — 런 Z1(main 대조판 · 슬롯 ㉠) 차례대로.
 *   path: settings = 양식정보설정 미리보기(FormTemplateController.cs:126) · doc = 전표 화면 [인쇄](DocumentController.cs:816)
 *         doc-i · doc-j = 판매 반품 두 입구(F10)
 *   expect = 판독 기준(judge-print.js 의 기본 판정 꼴) · neg = 「없음」 기준(양성 대조 통과 뒤에만 유효 · F4 ⑴)
 */
function plannedCells(opts) {
  const o = opts || {};
  const types = o.types ? TYPES.filter((t) => o.types.includes(t.key)) : TYPES;
  const cells = [];
  const logo = PNG.logoPng();
  types.forEach((t) => {
    const i = TYPES.indexOf(t);
    const bg = PNG.formBgPng(i, t.mark);
    const bgRef = { sha: bg.sha, quad: bg.quad, aspect: bg.aspect };
    const docPaths = t.entries ? t.entries.map((e) => `doc-${e}`) : ['doc'];
    for (const kc of K_CASES) {
      for (const p of ['settings', ...docPaths]) {
        const id = `${kc.k}.${t.key}.${p}`;
        if (kc.noDoorAtMain) {
          cells.push({ id, group: p === 'settings' ? '②' : '③(가)', k: kc.k, type: t.key, path: p, noDoorAtMain: kc.noDoorAtMain, mainExpect: kc.mainExpect, expect: [] });
          continue;
        }
        const expect = [];
        if (kc.coords) expect.push({ kind: 'fields', fields: fieldWants(i, p) });
        expect.push({ kind: 'noPlaceholder', neg: true });
        if (kc.coords) expect.push({ kind: 'noStdHeaderOutside', words: STD_HEAD, boxes: fieldWants(i, p).map((f) => f.box), neg: true });
        // 그림: 설정 미리보기 = 겹침(그림 있음) · 전표 미리보기 = 양식지면 데이터만(그림 0 · Q11) · 빈 A4 + 켬이면 그림 있음
        if (p === 'settings') expect.push({ kind: 'image', ref: bgRef, want: 'present' });
        else if (kc.paperMode === 'preprint' || !kc.printBackground) expect.push({ kind: 'image', ref: bgRef, want: 'absent', neg: true });
        else expect.push({ kind: 'image', ref: bgRef, want: 'present' });
        if (p !== 'settings') expect.push({ kind: 'uiNoWarn', neg: true });
        cells.push({
          id, group: p === 'settings' ? '②' : '③(가)', k: kc.k, type: t.key, path: p,
          tpl: { paperMode: kc.paperMode, coords: kc.coords, bg: kc.bg, printBackground: kc.printBackground, via: kc.via },
          changedShape: !!kc.changedShape, mainExpect: p === 'settings' || kc.k !== 'K2' ? kc.mainExpect : '초록일 수 있음',
          // 판매 반품은 오류가 곧 결함(C8 · F4 ⑶) — 응답 코드 · 본문 첫 줄을 사유로 빨강
          errorIsDefect: t.key === 'sales_return' && p !== 'settings',
          expect,
        });
      }
    }
    // ③(나) 스타일 4종 — 빈 A4 · 그림 0 · 좌표 0 양식 한 행의 styleKey 를 바꿔 가며(G1)
    for (const st of STYLES) {
      for (const p of docPaths) {
        cells.push({
          id: `ST-${st.key}.${t.key}.${p}`, group: '③(나)', k: `ST-${st.key}`, type: t.key, path: p,
          tpl: { paperMode: 'plain', coords: false, bg: false, printBackground: false, styleKey: st.key },
          mainExpect: st.key === 'basic' ? '초록(회귀)' : '빨강',
          errorIsDefect: t.key === 'sales_return',
          expect: [{ kind: 'fields', fields: [{ key: 'partner_name', box: null, want: MARK.partner }] }, { kind: 'style', ...st }],
        });
      }
    }
    // ⓙ 스타일 무시 회귀 — 양식지 · 좌표 · 그림 양식에 styleKey large → K2 전표 미리보기와 같은 글자 · 자리(대표 08시)
    for (const p of docPaths) {
      cells.push({
        id: `ST-ignore.${t.key}.${p}`, group: '③(나) 회귀', k: 'ST-ignore', type: t.key, path: p,
        tpl: { paperMode: 'preprint', coords: true, bg: true, printBackground: false, styleKey: 'large' },
        mainExpect: '초록(회귀)', errorIsDefect: t.key === 'sales_return',
        expect: [{ kind: 'sameWords', as: `K2.${t.key}.${p}` }],
      });
    }
  });
  // ① 워터마크 — 견적서 한 종(설정 미리보기 · 전표 미리보기 둘 다) · 켬(A) / 끔(B) · 로고 있음(가) / 지운 뒤(나)
  const wmT = TYPES[0];
  const logoRef = { sha: logo.sha, quad: logo.quad, aspect: logo.aspect };
  for (const phase of ['가', '나']) {
    for (const on of [true, false]) {
      for (const p of ['settings', 'doc']) {
        const id = `WM-${phase}-${on ? 'on' : 'off'}.${wmT.key}.${p}`;
        const expect = [];
        if (phase === '가' && on) expect.push({ kind: 'watermark', ref: logoRef, alpha: 0.12, tol: 0.02 });
        else expect.push({ kind: 'image', ref: logoRef, want: 'absent', neg: true });
        if (phase === '나') expect.push({ kind: 'noWords', words: ['로고', '워터마크'], neg: true });
        cells.push({
          id, group: `①(${phase})`, k: `WM-${phase}-${on ? 'on' : 'off'}`, type: wmT.key, path: p,
          tpl: { paperMode: 'plain', coords: false, bg: false, printBackground: false, showWatermark: on },
          logoPhase: phase, mainExpect: phase === '가' ? '미확정' : '초록(정답)', expect,
        });
      }
    }
  }
  // S 칸 — 편집창(빈 A4)에 좌표표 · PDF 받기가 보이나(main 기대 = 안 보임 · 판독 결과 · 차림 실패 아님)
  cells.push({ id: 'S.estimate.editor', group: 'S', k: 'S', type: 'estimate', path: 'editor', mainExpect: '안 보임(기록)', expect: [{ kind: 'uiFacts' }] });
  // YN1 — 양식지 PDF 올리기 문이 main 에 없다 → 문 없음 · 판정 제외(개발명세서 §2 · HQA ⑧)
  cells.push({ id: 'YN1.estimate.editor', group: 'Y/N', k: 'YN1', type: 'estimate', path: 'editor', noDoorAtMain: 'PDF 양식 올리기 문 없음(C9)', mainExpect: '문 없음', expect: [] });
  return { cells, logo, groups: compositeGroups(cells) };
}

/** 묶음 판정(F9 칸 쪼개기 그대로 · 묶음은 보기용 — 칸 판정을 바꾸지 않는다). */
function compositeGroups(cells) {
  const ids = (pred) => cells.filter(pred).map((c) => c.id);
  return [
    { id: '①(가) 객체 확인 — 켬 A 두 길 있음 · 끔 B 두 길 없음', cells: ids((c) => c.group === '①(가)') },
    { id: '①(나) 로고 없음 + 켬 — 아무것도 안 보임(회귀 기준)', cells: ids((c) => c.group === '①(나)') },
  ];
}

/** 런 전 고정 — 판독기 양성 대조 2개(F4 ⑴): main 기본 폼 견적서 · K2 양식지 설정 미리보기. */
const POSITIVE_CONTROL = {
  basicForm: { cell: 'WM-가-off.estimate.doc', words: ['품명', '공급가액'] },
  preprint: { cell: 'K2.estimate.settings' },
};

module.exports = {
  TYPES, FIELDS, MARK, STYLES, K_CASES, STD_HEAD, ROW_PITCH_MM, SHIFT_MM, BOX_TOL_MM,
  fieldsFor, coordsJson, boxOf, fieldWants, templateBody, competitorBody, plannedCells, POSITIVE_CONTROL,
};
