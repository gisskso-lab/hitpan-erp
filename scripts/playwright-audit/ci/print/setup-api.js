'use strict';
// W-G1-51 갈래 Z — 저장 문(API) 차림 (개발명세서 §3 ④ · 설계 §1-2 F2 ⑴)
//   🔴 양식 차림 문 = 양식 저장 문 하나 — main · 고친 판이 같은 문 · 같은 본문(POST/PUT api/settings/form-templates · FormTemplateController.cs:45 · :64)
//   🔴 그림 = POST api/settings/form-templates/background(multipart `file` · :176) → 받은 storedPath 를 본문 backgroundImageUrl 에
//   🔴 로고 · 전표는 여기서 만들지 않는다 — 설계 글자 「화면으로」(PM §6-a 4) → capture.js
//   🔴 거래처 · 품목 = 화면 차림의 재료(전표 화면이 검색해 고른다) — 기초 자료 저장 문(PartnerController.cs:90 · ItemController.cs:115)
//   차림 단계 실패(4xx/5xx · 로그인 실패 · 시간 넘김 · 저장된 칸 수 ≠ N) = 「차림 불가(사유)」 — 빨강에 넣지 않는다(F2 ⑵ · F8)
const K = require('../kit35');
const L = require('../lib');

const UA = 'Mozilla/5.0 (X11; Linux x86_64) HitPanCiSeed/1';

class SetupError extends Error {
  constructor(message) {
    super(message);
    this.name = 'SetupError';
  }
}

async function login() {
  const id = process.env.UI_LOGIN_PARENT || 'ci-parent';
  const pw = process.env.UI_PW_PARENT;
  if (!pw) throw new SetupError('부모 비번 환경값 없음');
  try {
    return await K.apiLogin('print', id, pw);
  } catch (e) {
    throw new SetupError(`API 로그인 실패 ${L.errorName(e)}`);
  }
}

async function call(sess, method, p, body, want) {
  let res;
  try {
    res = await K.withTimeout(K.apiCall(method, p, { sess, body }), 30000, `${method} 저장 문`);
  } catch (e) {
    throw new SetupError(`${method} ${p.split('?')[0]} 요청 실패 ${L.errorName(e)}`);
  }
  if (!want.includes(res.status)) {
    const msg = res.json && (res.json.message || res.json.title) ? String(res.json.message || res.json.title).slice(0, 120) : '';
    throw new SetupError(`${method} ${p.split('?')[0]} 응답 ${res.status}${msg ? ` · ${L.sanitize(msg)}` : ''}`);
  }
  return res.json;
}

async function createPartner(sess, name) {
  const j = await call(sess, 'POST', '/api/partners', { partnerName: name, partnerType: 'both' }, [200, 201]);
  return j && (j.partnerId || j.PartnerId || j.id);
}
async function createItem(sess, name, price) {
  const j = await call(sess, 'POST', '/api/items', {
    itemName: name, unit: 'EA', salePrice: price, purchasePrice: price, taxType: 'taxable', itemType: 'product',
  }, [200, 201]);
  return j && (j.id || j.itemId);
}

/** 양식 그림 올리기 — multipart `file` · 받은 storedPath(저장은 안 됨 · 본문에 넣어 저장). */
async function uploadBackground(sess, png, fileName) {
  const fd = new FormData();
  fd.append('file', new Blob([png], { type: 'image/png' }), fileName);
  const headers = { 'User-Agent': UA, Authorization: `Bearer ${sess.token}` };
  if (sess.deviceId) headers['X-HitPan-Device-Id'] = sess.deviceId;
  let res;
  try {
    res = await K.withTimeout(fetch(`${K.BASE}/api/settings/form-templates/background`, { method: 'POST', headers, body: fd }), 30000, '그림 올리기');
  } catch (e) {
    throw new SetupError(`그림 올리기 요청 실패 ${L.errorName(e)}`);
  }
  const text = await res.text();
  if (res.status !== 200) throw new SetupError(`그림 올리기 응답 ${res.status}`);
  let j = null;
  try {
    j = JSON.parse(text);
  } catch (e) {
    throw new SetupError(`그림 올리기 응답 JSON 아님 ${L.errorName(e)}`);
  }
  const p = j && (j.storedPath || j.StoredPath);
  if (!p) throw new SetupError('그림 올리기 응답에 storedPath 없음');
  return p;
}

async function createTemplate(sess, body) {
  const j = await call(sess, 'POST', '/api/settings/form-templates', body, [200, 201]);
  const id = j && (j.templateId || j.TemplateId);
  if (!id) throw new SetupError('양식 저장 응답에 templateId 없음');
  return id;
}
async function updateTemplate(sess, id, body) {
  await call(sess, 'PUT', `/api/settings/form-templates/${encodeURIComponent(id)}`, { ...body, isActive: true }, [200, 204]);
}
/** 저장된 칸 수 확인(F8) — 판독은 저장된 칸 목록을 세지 않지만 차림 성립은 확인한다. */
async function savedCoordCount(sess, formType, templateId) {
  const list = await call(sess, 'GET', `/api/settings/form-templates?formType=${encodeURIComponent(formType)}&activeOnly=true`, undefined, [200]);
  const row = (Array.isArray(list) ? list : []).find((t) => (t.templateId || t.TemplateId) === templateId);
  if (!row) throw new SetupError('저장한 양식이 목록에 없음');
  let coords = [];
  try {
    coords = row.fieldCoordsJson ? JSON.parse(row.fieldCoordsJson) : [];
  } catch (e) {
    throw new SetupError(`저장된 좌표 JSON 해석 실패 ${L.errorName(e)}`);
  }
  return { n: coords.filter((c) => c && c.key && !['row_pitch', 'max_rows'].includes(c.key)).length, row };
}
/** 로고 상태 — 화면 차림이 섰는지 확인만(차림 문 아님 · SettingsController.cs:277). */
async function logoState(sess) {
  const j = await call(sess, 'GET', '/api/settings/company-image-state', undefined, [200]);
  return j;
}

module.exports = { SetupError, login, createPartner, createItem, uploadBackground, createTemplate, updateTemplate, savedCoordCount, logoState };
