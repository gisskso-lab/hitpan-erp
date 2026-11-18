'use strict';
// W-G1-51 갈래 Z — 런 계획 읽기 (개발명세서 §3 ⑥ · ⑧ · 작업지시서 §4-1)
//   plan.json 의 `run`(이번에 쏠 런 이름)과 그 런의 잡 목록 → matrix JSON 한 줄.
//   🔴 워크플로는 이 출력만 받는다 — 런마다 yml 을 고치지 않고 plan.json 만 커밋으로 바꾼다(바뀐 줄 = 리뷰 · 대표 push 직전 보고).
//   🔴 잡 이름 · 판 · 패치 경로는 좁은 꼴만 받는다(셸 · 식 글자로 새는 자리 0). 꼴이 틀리면 matrix 를 내지 않고 실패.
//   🔴 PM §6-a 3 — qpdf 는 ⑨ 를 재는 잡(고침 R1 ㉮ · 고침 R3 ⑨ⓐⓑⓒ)에만. Z 런 잡에 qpdf 참이면 계획 거절.
//   쓰는 법: node plan.js [GITHUB_OUTPUT 파일]   — 파일이 있으면 `matrix=…` 한 줄을 덧붙인다.
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process'); // 3차-b([3-V] B3) — --check-paths 가 바뀐 파일을 `-z` 로 스스로 받음

const RUN_NAMES = ['Z1', 'Z2', 'Z3', 'Z4', 'R1', 'R2', 'R3', 'R4', 'R5', 'R6'];
const ID_RE = /^[a-z0-9][a-z0-9-]{0,39}$/;
const PATCH_RE = /^scripts\/playwright-audit\/ci\/print\/patches\/[a-z0-9][a-z0-9-]{0,59}\.patch$/;
const BOARDS = ['main', 'merged'];
const TENANTS = ['A', 'AB'];
const PHASES = ['read', 'old-rows', 'nine', 'retry'];

// 🔴 3차(11/19 · [3-V] A5) — 패치가 **건드리는 파일**을 본다(2차는 패치 파일 경로만 좁힘). 측정 장치(판정기 · 차림 · 누출 검사 ·
//   mask-probe · plan) · 워크플로 · git 내부를 건드리는 패치 = 거절 → 그 잡 전 칸 「차림 불가」(워크플로 패치 단계 · 적용 전 · 적용 뒤 두 번).
const PATCH_DENY = [/^scripts\//, /^\.github\//, /^\.git\//, /(^|\/)\.\.(\/|$)/, /^\//, /^[A-Za-z]:/];
function deniedPath(p) {
  return PATCH_DENY.some((re) => re.test(p));
}
/** 패치 글자 → { ok, why, paths } — 머리 줄(diff --git · --- · +++ · rename · copy)마다 경로를 모아 막힌 자리를 거른다. */
function checkPatchText(text) {
  const paths = new Set();
  const why = [];
  const strip = (s) => {
    let p = String(s).trim();
    if (p.startsWith('"')) {
      if (!p.endsWith('"') || p.length < 2) return null;
      p = p.slice(1, -1);
      if (/\\/.test(p)) return null; // 따옴표 안 탈출 글자 = 꼴 모름(거절)
    }
    if (p === '/dev/null') return '';
    return p.replace(/^[ab]\//, '');
  };
  let headers = 0;
  // 덩이(@@) 본문 줄은 머리로 읽지 않는다 — 덩이 머리의 줄 수를 세어 본문을 건너뜀(SQL 주석 「-- …」 을 지운 줄 = 「--- …」 오독 방지)
  let oldLeft = 0;
  let newLeft = 0;
  for (const line of String(text).split(/\r?\n/)) {
    let m;
    if (oldLeft > 0 || newLeft > 0) {
      if (line.startsWith('\\')) continue;
      const c = line[0];
      if (c === ' ' || line === '') { oldLeft--; newLeft--; continue; }
      if (c === '-') { oldLeft--; continue; }
      if (c === '+') { newLeft--; continue; }
      why.push(`덩이 줄 수 어긋남: ${line.slice(0, 40)}`);
      oldLeft = 0; newLeft = 0;
    }
    if ((m = /^@@ -\d+(?:,(\d+))? \+\d+(?:,(\d+))? @@/.exec(line))) {
      oldLeft = m[1] === undefined ? 1 : Number(m[1]);
      newLeft = m[2] === undefined ? 1 : Number(m[2]);
      continue;
    }
    if ((m = /^diff --git (\S+) (\S+)$/.exec(line))) {
      headers++;
      for (const x of [m[1], m[2]]) { const p = strip(x); if (p === null) why.push(`머리 꼴 모름: ${line.slice(0, 80)}`); else if (p) paths.add(p); }
    } else if (/^diff --git /.test(line)) {
      why.push(`머리 꼴 모름(공백 · 따옴표): ${line.slice(0, 80)}`);
    } else if ((m = /^(?:---|\+\+\+) (.+?)(\t.*)?$/.exec(line))) {
      const p = strip(m[1]);
      if (p === null) why.push(`머리 꼴 모름: ${line.slice(0, 80)}`); else if (p) paths.add(p);
    } else if ((m = /^(?:rename|copy) (?:from|to) (.+)$/.exec(line))) {
      const p = strip(m[1]);
      if (p === null) why.push(`머리 꼴 모름: ${line.slice(0, 80)}`); else if (p) paths.add(p);
    } else if (/^(?:new file mode|old mode|new mode|deleted file mode) 120000/.test(line)) {
      why.push('심볼릭 링크 패치 거절');
    } else if (/^GIT binary patch|^Binary files /.test(line)) {
      why.push('바이너리 패치 거절');
    }
  }
  if (headers === 0 && paths.size === 0) why.push('패치 머리 0 — 건드리는 파일을 못 셈');
  const denied = [...paths].filter(deniedPath);
  if (denied.length) why.push(`측정 장치 · 워크플로 자리 건드림: ${denied.join(' · ')}`);
  return { ok: why.length === 0, why: why.join(' · '), paths: [...paths] };
}
/**
 * 🔴 3차-b([3-V] B3) — git 이 따옴표로 감싼 경로(core.quotepath 기본 · 한글 등 `"scripts/\355\225\234.js"`)를 원래 경로로 푼다.
 *   C 탈출 \ooo(8진 바이트) · \\ · \" · \a \b \t \n \v \f \r 만 받고, 꼴이 다르면 null(= 거절 · fail-closed).
 */
function unquoteGitPath(s) {
  const p = String(s);
  if (!p.startsWith('"')) return p;
  if (p.length < 2 || !p.endsWith('"')) return null;
  const cs = Array.from(p.slice(1, -1));
  const esc = { a: 7, b: 8, t: 9, n: 10, v: 11, f: 12, r: 13, '"': 34, '\\': 92 };
  const bytes = [];
  for (let i = 0; i < cs.length; i++) {
    if (cs[i] !== '\\') { bytes.push(...Buffer.from(cs[i], 'utf8')); continue; }
    const oct = cs.slice(i + 1, i + 4).join('');
    if (/^[0-3][0-7]{2}$/.test(oct)) { bytes.push(parseInt(oct, 8)); i += 3; continue; }
    if (esc[cs[i + 1]] === undefined) return null;
    bytes.push(esc[cs[i + 1]]);
    i += 1;
  }
  return Buffer.from(bytes).toString('utf8');
}
/** 적용 뒤 바뀐 파일 목록(줄마다 하나)에 막힌 자리가 있나. */
function checkPaths(lines) {
  // ⬛ [낡은 줄 · 3차] const denied = String(lines).split(/\r?\n/).map((s) => s.trim()).filter(Boolean).filter(deniedPath);
  // ⬛ [낡은 줄 · 3차] return { ok: denied.length === 0, why: denied.length ? `적용 뒤 측정 장치 · 워크플로 자리 바뀜: ${denied.join(' · ')}` : '' };
  //   — 따옴표로 감싼 경로(`"scripts/\355…"`)는 /^scripts\// 에 안 걸려 통과했다([3-V] B3 측정)
  // 🔴 3차-b([3-V] B3) — 따옴표 꼴은 풀어서 본다 · 못 풀면 거절
  const raw = String(lines).split(/\r?\n/).map((s) => s.trim()).filter(Boolean);
  const odd = raw.filter((s) => unquoteGitPath(s) === null);
  const denied = raw.map(unquoteGitPath).filter((x) => x !== null && deniedPath(x));
  const why = [
    denied.length ? `적용 뒤 측정 장치 · 워크플로 자리 바뀜: ${denied.join(' · ')}` : '',
    odd.length ? `적용 뒤 경로 꼴 모름 ${odd.length}: ${odd.map((s) => s.slice(0, 60)).join(' · ')}` : '',
  ].filter(Boolean).join(' · ');
  return { ok: denied.length === 0 && odd.length === 0, why };
}
/** 🔴 3차-b([3-V] B3) — NUL 로 끊은 목록(`git … -z`) — 경로를 따옴표 · 탈출 없이 그대로 받는다(한글 경로 그대로). */
function checkPathsZ(buf) {
  const list = (Buffer.isBuffer(buf) ? buf.toString('utf8') : String(buf)).split('\0').filter(Boolean);
  const denied = list.filter(deniedPath);
  return { ok: denied.length === 0, why: denied.length ? `적용 뒤(-z) 측정 장치 · 워크플로 자리 바뀜: ${denied.join(' · ')}` : '', n: list.length };
}
/** 🔴 3차-b([3-V] B3) — --check-paths 가 스스로 받는 바뀐 파일 목록(워크플로 줄 무변 · 표준 입력 목록과 둘 다 본다). */
function gitChangedZ() {
  const run = (args) => execFileSync('git', ['-c', 'core.quotepath=false', ...args], { maxBuffer: 64 * 1024 * 1024 });
  return Buffer.concat([run(['diff', '--name-only', '-z', 'HEAD']), run(['ls-files', '-z', '--others', '--exclude-standard'])]);
}

// 🔴 3차([4] F6 · HQA ⑦) — 예상 사유 code 꼴(런 전 고정). target = 칸 id 또는 nine · code = 판정기가 내는 꼴 그대로(null = 미고정)
const EXPECT_KEY_RE = /^[a-z0-9][a-z0-9-]{0,39}$/;
function validateExpected(plan, why) {
  const codes = (plan && plan.expectedCodes) || {};
  const prose = (plan && plan.expectedReasons) || {};
  for (const [k, v] of Object.entries(codes)) {
    if (k === 'about') continue;
    if (!EXPECT_KEY_RE.test(k)) why.push(`예상 사유 키 꼴 다름 ${k}`);
    if (!v || typeof v.target !== 'string' || !/^[^\s|]{1,80}$/.test(v.target)) why.push(`예상 사유 ${k} target 꼴 다름`);
    if (v && v.code !== null && typeof v.code !== 'string') why.push(`예상 사유 ${k} code 꼴 다름`);
    if (typeof prose[k] !== 'string') why.push(`예상 사유 ${k} 산문 고정 글자 없음(expectedReasons)`);
  }
  return codes;
}

function validate(plan) {
  const why = [];
  if (!plan || plan.schema !== 'print-plan/1') why.push('schema 다름');
  if (!plan || !RUN_NAMES.includes(plan.run)) why.push(`run 이름 다름(${plan && plan.run})`);
  const runs = (plan && plan.runs) || {};
  const codes = validateExpected(plan, why);
  for (const [name, r] of Object.entries(runs)) {
    if (!RUN_NAMES.includes(name)) { why.push(`모르는 런 ${name}`); continue; }
    if (!Array.isArray(r.jobs)) { why.push(`${name} jobs 없음`); continue; }
    const seen = new Set();
    for (const j of r.jobs) {
      if (!ID_RE.test(String(j.id))) why.push(`${name} 잡 이름 꼴 다름`);
      if (seen.has(j.id)) why.push(`${name} 잡 이름 겹침 ${j.id}`);
      seen.add(j.id);
      if (!BOARDS.includes(j.board)) why.push(`${name}/${j.id} 판 다름`);
      if (!TENANTS.includes(j.tenant)) why.push(`${name}/${j.id} 테넌트 꼴 다름`);
      if (!PHASES.includes(j.phase)) why.push(`${name}/${j.id} 단계 다름`);
      if (j.patch !== '' && !PATCH_RE.test(String(j.patch))) why.push(`${name}/${j.id} 패치 경로 꼴 다름`);
      if (typeof j.qpdf !== 'boolean') why.push(`${name}/${j.id} qpdf 칸 참/거짓 아님`);
      if (name.startsWith('Z') && j.qpdf === true) why.push(`${name}/${j.id} Z 런에 qpdf(PM §6-a 3 · CTO §4-2 조건 7)`);
      if (j.qpdf === true && j.phase !== 'nine') why.push(`${name}/${j.id} qpdf 는 ⑨ 재는 잡만`);
      // 🔴 3차([4] F6 · HQA ⑦) — 잡의 예상 사유는 고정된 code(미고정 null 금지)만 가리킨다 · 심은 결함 런(R2 · R3)의 패치 잡은 예상 사유 필수
      if (j.expect !== undefined && !Array.isArray(j.expect)) why.push(`${name}/${j.id} expect 꼴 다름`);
      for (const key of Array.isArray(j.expect) ? j.expect : []) {
        if (!codes[key]) why.push(`${name}/${j.id} 모르는 예상 사유 ${key}`);
        else if (typeof codes[key].code !== 'string' || codes[key].code === '') why.push(`${name}/${j.id} 예상 사유 ${key} 미고정 — 발사 금지(HQA ⑦)`);
      }
      if ((name === 'R2' || name === 'R3') && j.patch !== '' && !(Array.isArray(j.expect) && j.expect.length)) why.push(`${name}/${j.id} 심은 결함 잡에 예상 사유 0 — 발사 금지(HQA ⑦)`);
    }
  }
  return { ok: why.length === 0, why: why.join(' · ') };
}

function matrixFor(plan) {
  const r = plan.runs && plan.runs[plan.run];
  if (!r || !Array.isArray(r.jobs) || r.jobs.length === 0) return null;
  return {
    include: r.jobs.map((j) => ({ id: j.id, board: j.board, tenant: j.tenant, phase: j.phase, patch: j.patch, qpdf: j.qpdf, run: plan.run })),
  };
}

function main(outFile) {
  // 🔴 3차([3-V] A5) — 워크플로 패치 단계가 부르는 두 꼴: 적용 전 `--check-patch <파일>` · 적용 뒤 `--check-paths`(표준 입력 = 바뀐 파일 목록)
  if (outFile === '--check-patch') {
    const file = process.argv[3] || '';
    if (!PATCH_RE.test(file) || !fs.existsSync(file)) {
      console.log(`⚫ FAIL [장치] 패치 파일 꼴 · 자리 다름(${file.slice(0, 80)})`);
      return 1;
    }
    const r = checkPatchText(fs.readFileSync(file, 'utf8'));
    console.log(r.ok ? `패치 검사 통과 — 건드리는 파일 ${r.paths.length}` : `⚫ FAIL [장치] 패치 거절 — ${r.why}`);
    return r.ok ? 0 : 1;
  }
  if (outFile === '--check-paths') {
    const r = checkPaths(fs.readFileSync(0, 'utf8'));
    // ⬛ [낡은 줄 · 3차] console.log(r.ok ? '적용 뒤 검사 통과' : `⚫ FAIL [장치] ${r.why}`);
    // ⬛ [낡은 줄 · 3차] return r.ok ? 0 : 1;
    // 🔴 3차-b([3-V] B3) — 표준 입력(워크플로 줄 그대로) + 스스로 받은 `git -c core.quotepath=false … -z` 목록 둘 다 통과해야
    let z;
    try {
      z = checkPathsZ(gitChangedZ());
    } catch (e) {
      z = { ok: false, why: `git -z 목록 못 받음(${e.name})` };
    }
    const ok = r.ok && z.ok;
    console.log(ok ? `적용 뒤 검사 통과(-z ${z.n})` : `⚫ FAIL [장치] ${[r.why, z.why].filter(Boolean).join(' · ')}`);
    return ok ? 0 : 1;
  }
  const plan = JSON.parse(fs.readFileSync(path.join(__dirname, 'plan.json'), 'utf8'));
  const v = validate(plan);
  if (!v.ok) {
    console.log(`⚫ FAIL [장치] 런 계획 꼴 — ${v.why}`);
    return 1;
  }
  const m = matrixFor(plan);
  if (!m) {
    console.log(`⚫ FAIL [장치] 런 ${plan.run} 잡 0 — 발사 금지(${(plan.runs[plan.run] || {}).blocked || '계획 없음'})`);
    return 1;
  }
  // 🔴 3차([3-V] A5) — 계획 때도 이번 런 패치를 미리 검사(파일 없음 · 막힌 자리 = 계획 거절 · 발사 0)
  for (const j of m.include) {
    if (!j.patch) continue;
    if (!fs.existsSync(path.join(__dirname, '..', '..', '..', '..', j.patch))) {
      console.log(`⚫ FAIL [장치] ${j.id} 패치 파일 없음 ${j.patch}`);
      return 1;
    }
    const r = checkPatchText(fs.readFileSync(path.join(__dirname, '..', '..', '..', '..', j.patch), 'utf8'));
    if (!r.ok) {
      console.log(`⚫ FAIL [장치] ${j.id} 패치 거절 — ${r.why}`);
      return 1;
    }
  }
  const line = `matrix=${JSON.stringify(m)}`;
  if (outFile) fs.appendFileSync(outFile, `${line}\n`);
  console.log(`런 계획: ${plan.run} · 잡 ${m.include.length} · ${m.include.map((j) => `${j.id}(${j.board}${j.patch ? ' + 패치' : ''}${j.qpdf ? ' + qpdf' : ''})`).join(' · ')}`);
  return 0;
}

if (require.main === module) process.exitCode = main(process.argv[2]);

// ⬛ [낡은 줄 · 3차] module.exports = { validate, matrixFor, RUN_NAMES, checkPatchText, checkPaths, deniedPath };
// 🔴 3차-b([3-V] B3) — 따옴표 풀기 · -z 목록 검사(자체시험 T-Z20)
module.exports = { validate, matrixFor, RUN_NAMES, checkPatchText, checkPaths, deniedPath, unquoteGitPath, checkPathsZ, gitChangedZ };
