'use strict';
// W-G1-51 갈래 Z — 저장 문(API) 차림 (개발명세서 §3 ④ · 설계 §1-2 F2 ⑴)
//   🔴 양식 차림 문 = 양식 저장 문 하나 — main · 고친 판이 같은 문 · 같은 본문(POST/PUT api/settings/form-templates · FormTemplateController.cs:45 · :64)
//   🔴 그림 = POST api/settings/form-templates/background(multipart `file` · :176) → 받은 storedPath 를 본문 backgroundImageUrl 에
//   🔴 로고 · 전표는 여기서 만들지 않는다 — 설계 글자 「화면으로」(PM §6-a 4) → capture.js
//   🔴 거래처 · 품목 = 화면 차림의 재료(전표 화면이 검색해 고른다) — 기초 자료 저장 문(PartnerController.cs:90 · ItemController.cs:115)
//   차림 단계 실패(4xx/5xx · 로그인 실패 · 시간 넘김 · 저장된 칸 수 ≠ N) = 「차림 불가(사유)」 — 빨강에 넣지 않는다(F2 ⑵ · F8)
// 🔴 4차(11/19 · 개발명세서 §4-a ⓐ~ⓓ · Z1 런 44434115123 근본원인 = 시드 부모 약관 미동의 → TermsConsentMiddleware.cs:87~104 가 /api/* 403)
//   ⓐ 로그인 응답 requiresTermsConsent(AuthController.cs:297) 참이면 화면과 같은 문 POST /api/auth/terms/agree
//      (본문 = Terms.razor:117~125 · seed-children.js:55~61 과 같은 꼴) — DB 직접 쓰기 · 설정 끄기 · 헤더 위조 0
//   ⓑ 차림 실패 사유 = 상태 · 본문 첫 줄 160자 · content-type · www-authenticate(있을 때) — 전부 L.sanitize 통과 · call · uploadBackground 둘 다
//   ⓒ 화면 로그인 전 API 로그아웃(한 PC 규칙 409 덫 · seed-children.js:62~66 선례) → 화면 뒤 API 차림 = 화면 세션 토큰(handoffToScreen)
// 🔴 5차(11/19 · 개발명세서 §1-5 · [3-V] 4차 D2 · D4) — failReason message 칸 = 소독 뒤 자르기 · 동의 뒤 상태 문 다시 확인(confirmTerms)
const K = require('../kit35');
const L = require('../lib');

const UA = 'Mozilla/5.0 (X11; Linux x86_64) HitPanCiSeed/1';

class SetupError extends Error {
  constructor(message) {
    super(message);
    this.name = 'SetupError';
  }
}

// ── 🔴 4차 ⓑ 응답 사실 · 실패 사유(순수 · 자체시험 T-Z22 · T-Z23) ──
const CTL = /[\u0000-\u001f\u007f-\u009f\u2028\u2029]/g;
/** 본문 첫 줄 — 소독(등록값 가림) → 제어 글자 → 빈칸(kit35 consoleLine 과 같은 순서) · 상한 160자. */
function firstLine(text, cap = 160) {
  const line = String(text ?? '').split(/\r?\n/)[0].trim();
  return L.sanitize(line, cap).replace(CTL, ' ');
}
/** 응답 사실 — 상태 · 본문(메모리에만) · JSON · 머리 둘. headers = fetch Headers 또는 { 이름: 값 }. */
function responseFacts(status, text, headers) {
  const h = (n) => {
    const v = headers && typeof headers.get === 'function' ? headers.get(n) : (headers ? headers[n] : null);
    return v === null || v === undefined ? '' : String(v);
  };
  const t = String(text ?? '');
  let json = null;
  let jsonErr = null;
  if (t) {
    try {
      json = JSON.parse(t);
    } catch (e) {
      jsonErr = L.errorName(e);
    }
  }
  return { status, text: t, json, jsonErr, ctype: h('content-type'), wwwAuth: h('www-authenticate') };
}
/** 실패 사유 — `{what} 응답 {상태} · {message|title} · 본문 「첫 줄」 · content-type=… · www-authenticate=…(있을 때)`. */
function failReason(what, r) {
  const parts = [`${what} 응답 ${r.status}`];
  const j = r.json;
  // ⬛ [낡은 줄 · 4차] const msg = j && typeof j === 'object' && (j.message || j.title) ? L.sanitize(String(j.message || j.title).slice(0, 120)).replace(CTL, ' ') : '';
  // 🔴 5차 ⓔ4([3-V] 4차 D2) — 자른 **뒤** 소독하면 120자 경계에 걸친 등록값 앞부분이 안 가려진다 → 소독 **뒤** 자르기(L.sanitize 상한 = firstLine 과 같은 순서)
  const msg = j && typeof j === 'object' && (j.message || j.title) ? L.sanitize(String(j.message || j.title), 120).replace(CTL, ' ') : '';
  if (msg) parts.push(msg);
  parts.push(r.text ? `본문 「${firstLine(r.text)}」` : '본문 없음');
  parts.push(`content-type=${r.ctype ? L.sanitize(r.ctype, 60).replace(CTL, ' ') : '없음'}`);
  if (r.wwwAuth) parts.push(`www-authenticate=${L.sanitize(r.wwwAuth, 120).replace(CTL, ' ')}`);
  return parts.join(' · ');
}
/** 원문 응답 호출 — K.apiCall 과 같은 머리(Content-Type · UA · Authorization · 기기 번호) · 다른 점 = 본문 · 머리를 버리지 않음. */
async function rawCall(sess, method, p, body) {
  const headers = { 'Content-Type': 'application/json', 'User-Agent': UA };
  if (sess && sess.token) headers.Authorization = `Bearer ${sess.token}`;
  if (sess && sess.deviceId) headers['X-HitPan-Device-Id'] = sess.deviceId;
  const res = await fetch(`${K.BASE}${p}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  const text = await res.text();
  return responseFacts(res.status, text, res.headers);
}

// ⬛ [낡은 몸통 · 1~3차] login() = K.apiLogin('print', id, pw) — 응답의 requiresTermsConsent 를 버렸다(Z1 403 · §4-a ⓐ)
// async function login() {
//   const id = process.env.UI_LOGIN_PARENT || 'ci-parent';
//   const pw = process.env.UI_PW_PARENT;
//   if (!pw) throw new SetupError('부모 비번 환경값 없음');
//   try {
//     return await K.apiLogin('print', id, pw);
//   } catch (e) {
//     throw new SetupError(`API 로그인 실패 ${L.errorName(e)}`);
//   }
// }
// 🔴 4차 ⓐ — K.apiLogin 과 같은 요청(본문 · 기기 꼴 ci-print · UA) · 같은 가림(받는 즉시 토큰부터 · 이름표 TOKEN_*_K_print) · 더한 것 = 약관 표지
async function login() {
  const id = process.env.UI_LOGIN_PARENT || 'ci-parent';
  const pw = process.env.UI_PW_PARENT;
  if (!pw) throw new SetupError('부모 비번 환경값 없음');
  let r;
  try {
    r = await K.withTimeout(rawCall(null, 'POST', '/api/auth/login', {
      email: id, password: pw, deviceType: 'pc', deviceName: 'ci-print', deviceFingerprint: 'ci-print-fp',
    }), 30000, '로그인');
  } catch (e) {
    throw new SetupError(`API 로그인 실패 ${L.errorName(e)}`);
  }
  return loginSession(r);
}
/** 로그인 응답 → 세션(토큰 가림 먼저) · 약관 표지(terms). 200 아닌 응답만 본문 첫 줄을 사유에(200 본문 = 토큰 · 글자 0). */
function loginSession(r) {
  const j = (r.json && typeof r.json === 'object') ? r.json : {};
  const access = j.accessToken || j.AccessToken;
  const refresh = j.refreshToken || j.RefreshToken;
  if (access) L.maskAndRegister('TOKEN_ACCESS_K_print', access);
  if (refresh) L.maskAndRegister('TOKEN_REFRESH_K_print', refresh);
  if (r.status !== 200) throw new SetupError(`API 로그인 실패 — ${failReason('POST /api/auth/login', r)}`);
  if (!access) throw new SetupError('API 로그인 실패 — 응답 200 · 접속 토큰 없음');
  if (j.deviceAwaitingApproval || j.DeviceAwaitingApproval) throw new SetupError('API 로그인 실패 — 기기 승인 대기');
  return { who: 'print', token: access, deviceId: j.deviceId || j.DeviceId || null, terms: !!(j.requiresTermsConsent || j.RequiresTermsConsent) };
}
// 🔴 4차 ⓐ 화면 Terms.razor:117~125 와 같은 본문(필수 4 참 · 마케팅 거짓 · 판 v2.0.0 = AuthController.cs:13 · TermsConsentMiddleware.cs:13)
const TERMS_BODY = Object.freeze({
  agreeService: true, agreePrivacy: true, agreeSubscription: true, agreeDataOwnership: true, agreeMarketing: false, termsVersion: 'v2.0.0',
});
/**
 * 약관 동의 차림 — 로그인 응답 표지가 참일 때만 화면과 같은 문(POST /api/auth/terms/agree · 200 Ok · 201 Created = TermsConsentController.cs:100 · :109).
 *   rec(= doc.setup.terms)에 required · asked · ok · got/why 를 채운다. 동의 실패 = SetupError(사유 = ⓑ 꼴).
 *   doCall = 자체시험이 가짜 손을 넣는 자리(기본 = call).
 */
async function ensureTerms(sess, rec = {}, doCall = call) {
  rec.required = !!(sess && sess.terms);
  rec.asked = false;
  if (!rec.required) {
    rec.ok = true;
    rec.got = '로그인 응답 requiresTermsConsent 거짓 — 동의 부름 0';
    return rec;
  }
  rec.asked = true;
  try {
    await doCall(sess, 'POST', '/api/auth/terms/agree', { ...TERMS_BODY }, [200, 201]);
  } catch (e) {
    rec.ok = false;
    rec.why = e instanceof SetupError ? e.message : `${L.errorName(e)} ${L.sanitize(e && e.message ? e.message : '', 120)}`;
    throw new SetupError(`약관 동의 실패 — ${rec.why}`);
  }
  rec.ok = true;
  rec.got = '동의 응답 2xx(화면과 같은 문 POST /api/auth/terms/agree · 판 v2.0.0)';
  return rec;
}
/**
 * 🔴 5차 ⓔ6([3-V] 4차 D4) — 동의 **뒤** 제품 문 그대로 다시 확인: GET /api/auth/terms/status(TermsConsentController.cs:20~55).
 *   「아직 필요」 판단 = 화면과 같은 규칙 `HasAgreed != true`(TermsConsentBanner.razor:64~65) → 차림 불가(사유 = 「약관 동의 실패 — …」 = §2 다섯 꼴 ①).
 *   동의를 부르지 않았으면(rec.asked 거짓 = 로그인 표지 거짓) 다시 확인 0 — 동의 뒤 확인이라서.
 *   한계(명세서 §1-5): 상태 문은 현재 요구 판을 돌려주지 않는다(TermsConsentDto.cs:25~31 · 가장 최근 동의 행의 판만) →
 *   제품 판이 v2.0.0 에서 오르면 이 확인은 통과하고 기초 자료 403 `forbidden_terms_consent`(§2 꼴 ⑤ · required_version)로 드러난다.
 *   판 칸이 오면 보낸 판(TERMS_BODY)과 다를 때도 차림 불가. doCall = 자체시험 가짜 손 자리(기본 = call).
 */
async function confirmTerms(sess, rec = {}, doCall = call) {
  if (!rec.asked) {
    rec.confirm = '동의 부름 0 — 다시 확인 안 함';
    return rec;
  }
  let st;
  try {
    st = await doCall(sess, 'GET', '/api/auth/terms/status', undefined, [200]);
  } catch (e) {
    rec.ok = false;
    rec.why = `동의 뒤 확인 ${e instanceof SetupError ? e.message : `${L.errorName(e)} ${L.sanitize(e && e.message ? e.message : '', 120)}`}`;
    throw new SetupError(`약관 동의 실패 — ${rec.why}`);
  }
  const o = st && typeof st === 'object' ? st : {};
  const agreed = o.hasAgreed === true || o.HasAgreed === true;
  const verRaw = o.termsVersion || o.TermsVersion;
  const ver = verRaw ? L.sanitize(String(verRaw), 40).replace(CTL, ' ') : null;
  rec.confirmed = { hasAgreed: agreed, termsVersion: ver };
  if (!agreed) {
    rec.ok = false;
    rec.why = '동의 뒤 GET /api/auth/terms/status 응답 200 · hasAgreed 참 아님(아직 필요 · 화면 규칙 HasAgreed != true)';
    throw new SetupError(`약관 동의 실패 — ${rec.why}`);
  }
  if (ver && ver !== TERMS_BODY.termsVersion) {
    rec.ok = false;
    rec.why = `동의 뒤 GET /api/auth/terms/status 판 ${ver} ≠ 보낸 판 ${TERMS_BODY.termsVersion}`;
    throw new SetupError(`약관 동의 실패 — ${rec.why}`);
  }
  rec.confirm = `동의 뒤 GET /api/auth/terms/status 200 · hasAgreed 참 · 판 ${ver || '칸 없음'}`;
  return rec;
}

// ⬛ [낡은 몸통 · 1~3차] call — K.apiCall 이 본문 · 머리를 버려 사유 = 「응답 403」 뿐(Z1 · §4-a ⓑ)
// async function call(sess, method, p, body, want) {
//   let res;
//   try {
//     res = await K.withTimeout(K.apiCall(method, p, { sess, body }), 30000, `${method} 저장 문`);
//   } catch (e) {
//     throw new SetupError(`${method} ${p.split('?')[0]} 요청 실패 ${L.errorName(e)}`);
//   }
//   if (!want.includes(res.status)) {
//     const msg = res.json && (res.json.message || res.json.title) ? String(res.json.message || res.json.title).slice(0, 120) : '';
//     throw new SetupError(`${method} ${p.split('?')[0]} 응답 ${res.status}${msg ? ` · ${L.sanitize(msg)}` : ''}`);
//   }
//   return res.json;
// }
// 🔴 4차 ⓑ — 원문 응답 호출 · 실패 사유 = failReason(상태 · message|title · 본문 첫 줄 · content-type · www-authenticate)
async function call(sess, method, p, body, want) {
  let r;
  try {
    r = await K.withTimeout(rawCall(sess, method, p, body), 30000, `${method} 저장 문`);
  } catch (e) {
    throw new SetupError(`${method} ${p.split('?')[0]} 요청 실패 ${L.errorName(e)}`);
  }
  if (!want.includes(r.status)) throw new SetupError(failReason(`${method} ${p.split('?')[0]}`, r));
  if (r.jsonErr) console.log(`응답 JSON 아님 ${method} (원문 생략 · ${r.jsonErr})`); // K.apiCall 과 같은 줄
  return r.json;
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
  // ⬛ [낡은 줄 · 2~3차] if (res.status !== 200) throw new SetupError(`그림 올리기 응답 ${res.status}`); — 본문 · 머리 버림(§4-a ⓑ)
  // 🔴 4차 ⓑ — 같은 사유 꼴(상태 · 본문 첫 줄 · content-type · www-authenticate)
  if (res.status !== 200) throw new SetupError(failReason('그림 올리기', responseFacts(res.status, text, res.headers)));
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

// ── 🔴 4차 ⓒ API 세션 → 화면 세션 넘김(개발명세서 §4-a 잠복 원인 · 자체시험 T-Z25 · T-Z26) ──
//   한 PC 규칙(AuthService.cs:419~457): API 로그인(deviceType pc) 세션이 살아 있는 채 화면 로그인 = 409 「다른 PC에서 사용 중」.
//   API 세션은 로그아웃하면 SessionValidityMiddleware.cs:160~170 로 401 → 화면 뒤 API 차림은 화면 세션 토큰(같은 PC 세션 · sid 같음)으로.
/** 화면 세션 — 토큰 · 기기 번호를 부를 때마다 tok 에서 읽는다(화면이 갱신해 watchTokens 가 tok.access 를 바꿔도 따라감). */
function screenSession(tok, who = '화면') {
  return {
    who,
    get token() { return tok.access; },
    get deviceId() { return tok.deviceId; },
  };
}
/** 화면 로그인 판정(순수) — 응답 200 아니면 「화면 로그인 응답 NNN · 본문 「첫 줄」」 · 200 이어도 자리 · 토큰 없으면 차림 불가. */
function screenLoginVerdict({ status = null, firstLine: line = '', where = null, path = null, err = null, access = null } = {}) {
  if (status === null || status === undefined) {
    return { ok: false, why: `화면 로그인 응답 못 봄(POST /api/auth/login 0)${err ? ` · 화면 단계 ${err}` : ''}` };
  }
  if (status !== 200) return { ok: false, why: `화면 로그인 응답 ${status} · 본문 「${line || '없음'}」` };
  if (err) return { ok: false, why: `화면 로그인 응답 200 · 화면 단계 실패 ${err}` };
  if (where !== 'main') return { ok: false, why: `화면 로그인 응답 200 · 자리 ${where || '못 잡음'}@${path || '-'}(본 화면 아님)` };
  if (!access) return { ok: false, why: '화면 로그인 응답 200 · 화면 세션 토큰 못 가림' };
  return { ok: true, why: '화면 로그인 응답 200 · 본 화면 · 화면 세션 토큰 가림' };
}
/** 화면 로그인 응답 지켜보기 — 상태 · (200 아닐 때만) 본문 첫 줄. 200 본문 = 토큰 → 읽지 않음(가림 · 등록은 K.watchTokens 몫). */
function watchLoginResponse(context) {
  const seen = { status: null, firstLine: '', n: 0 };
  let done = null;
  seen.done = new Promise((ok) => { done = ok; });
  context.on('response', async (res) => {
    try {
      if (!/\/api\/auth\/login(\?|$)/i.test(res.url())) return;
      if (res.request().method() !== 'POST') return;
      seen.n += 1;
      seen.status = res.status();
      if (seen.status !== 200) {
        try {
          seen.firstLine = firstLine(await res.text());
        } catch (e) {
          seen.firstLine = `본문 못 읽음 ${L.errorName(e)}`;
        }
      }
      done();
    } catch (e) {
      console.log(`화면 로그인 응답 지켜보기 실패 (원문 생략 · ${L.errorName(e)})`); // 처리 안 된 거절로 프로세스가 죽지 않게
    }
  });
  return seen;
}
/** 지켜본 응답을 상한만큼 기다림(던지지 않음 · 못 보면 status null 그대로 → 판정 「못 봄」). */
async function waitSeen(seen, ms = 10000) {
  let t = null;
  await Promise.race([seen.done, new Promise((ok) => { t = setTimeout(ok, ms); })]);
  clearTimeout(t);
  return seen;
}
function handoffDeps() {
  return {
    apiLogout: K.apiLogout, watchTokens: K.watchTokens, screenLogin: K.screenLogin, waitMasked: K.waitMasked, watchLoginResponse, waitSeen,
  };
}
/**
 * 넘김 순서(고정): ① API 로그아웃 → ② 토큰 가로채기 · 로그인 응답 지켜보기(화면 로그인 전에 건다) → ③ 화면 로그인
 *   → ④ 응답 기다림 · (200 이면) 토큰 가림 기다림 → ⑤ 판정. 통과면 api = 화면 세션.
 *   돌려줌: { apiLogout: {ok, got}, verdict: {ok, why}, tok, api(통과 때만), order(부른 차례 · 자체시험용 이름만) }
 */
async function handoffToScreen({ sess, context, page, loginId, password }, d = handoffDeps()) {
  const order = [];
  order.push('apiLogout');
  const apiLogout = await d.apiLogout(sess);
  const tok = { access: null, masked: 0, deviceId: null };
  order.push('watchTokens');
  d.watchTokens(context, tok);
  order.push('watchLoginResponse');
  const seen = d.watchLoginResponse(context);
  let where = null;
  let atPath = null;
  let err = null;
  order.push('screenLogin');
  try {
    const s = await d.screenLogin(page, loginId, password);
    where = s && s.where !== undefined ? s.where : null;
    atPath = s && s.path ? s.path : null;
  } catch (e) {
    err = L.errorName(e);
  }
  await d.waitSeen(seen);
  if (seen.status === 200) {
    order.push('waitMasked');
    await d.waitMasked(tok);
  }
  const verdict = screenLoginVerdict({ status: seen.status, firstLine: seen.firstLine, where, path: atPath, err, access: tok.access });
  return { apiLogout, verdict, tok, api: verdict.ok ? screenSession(tok) : null, order };
}

// ⬛ [낡은 줄 · 2~3차] module.exports = { SetupError, login, createPartner, createItem, uploadBackground, createTemplate, updateTemplate, savedCoordCount, logoState };
module.exports = {
  SetupError, login, createPartner, createItem, uploadBackground, createTemplate, updateTemplate, savedCoordCount, logoState,
  // 🔴 4차 ⓐ~ⓒ
  firstLine, responseFacts, failReason, rawCall, loginSession, TERMS_BODY, ensureTerms, call,
  screenSession, screenLoginVerdict, watchLoginResponse, waitSeen, handoffToScreen,
  // 🔴 5차 ⓔ6
  confirmTerms,
};
