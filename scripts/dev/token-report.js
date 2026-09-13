#!/usr/bin/env node
// 하루치 토큰 리포트 — 로컬 대화기록(~/.claude/projects)을 읽기만 한다.
// 사용: node scripts/dev/token-report.js [YYYY-MM-DD(KST), 기본 오늘] [--project <대화기록 폴더>]
// 근거: docs/헌법/AI협업_토큰절약_에이전트투입_매뉴얼.md §6 P6 · [7] 일일작업보고서 한 줄
'use strict';
const fs = require('fs');
const path = require('path');
const os = require('os');

const args = process.argv.slice(2);
const day = args.find(a => /^\d{4}-\d{2}-\d{2}$/.test(a))
  || new Date(Date.now() + 9 * 3600e3).toISOString().slice(0, 10);
const projectsRoot = path.join(os.homedir(), '.claude', 'projects');

function resolveProjectDir() {
  const i = args.indexOf('--project');
  if (i >= 0 && args[i + 1]) return args[i + 1];
  const want = process.cwd().replace(/[^a-zA-Z0-9]/g, '-').toLowerCase();
  const dirs = fs.readdirSync(projectsRoot);
  const hit = dirs.find(d => d.toLowerCase() === want) || dirs.find(d => /hitpan-erp$/i.test(d));
  return path.join(projectsRoot, hit || want);
}

// 환산 토큰(Opus 입력 1 기준): 캐시쓰기 1.25(5분)/2(1시간) · 캐시읽기 0.1 · 출력 5 · Sonnet 0.6 · Haiku 0.2
const modelFactor = m => (/haiku/i.test(m) ? 0.2 : /sonnet/i.test(m) ? 0.6 : 1);
const estimateTokens = s => {
  let hangul = 0;
  for (const ch of s) { const c = ch.codePointAt(0); if (c >= 0xAC00 && c <= 0xD7A3) hangul++; }
  return hangul + (s.length - hangul) / 3.5;
};
const walk = (dir, out) => {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p, out); else if (e.name.endsWith('.jsonl')) out.push(p);
  }
  return out;
};

const projectDir = resolveProjectDir();
const dayStart = Date.parse(`${day}T00:00:00+09:00`);
const dayEnd = dayStart + 86400e3;
const rows = [];
let docReadTokens = 0;

for (const file of walk(projectDir, [])) {
  let stat;
  try { stat = fs.statSync(file); } catch { continue; }
  if (stat.mtimeMs < dayStart) continue;

  const rel = path.relative(projectDir, file);
  const isMain = !rel.includes(path.sep);
  let desc = 'main';
  if (!isMain) {
    try { desc = JSON.parse(fs.readFileSync(file.replace(/\.jsonl$/, '.meta.json'), 'utf8')).description || 'subagent'; } catch { desc = 'subagent'; }
  }

  const calls = new Map();
  const readInputs = new Map();
  for (const line of fs.readFileSync(file, 'utf8').split('\n')) {
    if (!line) continue;
    let d;
    try { d = JSON.parse(line); } catch { continue; }
    const t = d.timestamp ? Date.parse(d.timestamp) : 0;
    const inDay = t >= dayStart && t < dayEnd;

    if (d.type === 'assistant' && d.message) {
      for (const b of d.message.content || []) {
        if (b.type === 'tool_use' && b.name === 'Read') readInputs.set(b.id, b.input || {});
      }
      const u = d.message.usage;
      if (!u || d.message.model === '<synthetic>' || !inDay) continue;
      const id = d.message.id || d.requestId || d.uuid;
      const cacheWrite = u.cache_creation_input_tokens || 0;
      const cacheWrite1h = (u.cache_creation || {}).ephemeral_1h_input_tokens || 0;
      const rec = { t, model: d.message.model || '', input: u.input_tokens || 0, cacheWrite, cacheWrite1h, cacheRead: u.cache_read_input_tokens || 0, output: u.output_tokens || 0 };
      const prev = calls.get(id);
      if (!prev || rec.output >= prev.output) calls.set(id, rec);
    } else if (d.type === 'user' && inDay && d.message && Array.isArray(d.message.content)) {
      for (const b of d.message.content) {
        if (!b || b.type !== 'tool_result') continue;
        const readInput = readInputs.get(b.tool_use_id);
        if (!readInput || !/[\\/]docs[\\/].*\.md$/i.test(readInput.file_path || '')) continue;
        const text = typeof b.content === 'string' ? b.content
          : Array.isArray(b.content) ? b.content.map(x => (x && x.text) || '').join('') : '';
        docReadTokens += estimateTokens(text);
      }
    }
  }

  const list = [...calls.values()].sort((a, b) => a.t - b.t);
  if (!list.length) continue;
  const row = { isMain, desc, sid: rel.split(path.sep)[0].slice(0, 8), calls: list.length, oie: 0, rewrites: 0, rewriteOie: 0, staleResumes: 0, over300k: 0, maxCtx: 0 };
  let prev = null;
  for (const r of list) {
    const f = modelFactor(r.model);
    const writeOie = f * ((r.cacheWrite - r.cacheWrite1h) * 1.25 + r.cacheWrite1h * 2);
    row.oie += writeOie + f * (r.input + r.cacheRead * 0.1 + r.output * 5);
    const ctx = r.input + r.cacheWrite + r.cacheRead;
    row.maxCtx = Math.max(row.maxCtx, ctx);
    if (ctx > 300e3) row.over300k++;
    if (r.cacheWrite >= 100e3) {
      row.rewrites++;
      row.rewriteOie += writeOie;
      if (!isMain && prev && r.t - prev.t > 5 * 60e3) row.staleResumes++;
    }
    prev = r;
  }
  rows.push(row);
}

const M = x => (x / 1e6).toFixed(2);
const sum = (list, key) => list.reduce((s, r) => s + r[key], 0);
const mains = rows.filter(r => r.isMain);
const subs = rows.filter(r => !r.isMain);
const total = sum(rows, 'oie');

console.log(`# 토큰 리포트 ${day} (KST) — 환산 토큰, 근사치`);
console.log(`합계 ${M(total)}M · 메인 ${M(sum(mains, 'oie'))}M (${mains.length}세션) · 서브에이전트 ${M(sum(subs, 'oie'))}M (${subs.length}개)`);
console.log(`캐시 재기록(10만+) ${sum(rows, 'rewrites')}회 ${M(sum(rows, 'rewriteOie'))}M · 쉰 에이전트 재개 ${sum(subs, 'staleResumes')}회 · 30만 초과 호출 ${sum(rows, 'over300k')}회 · docs 문서 읽기 약 ${Math.round(docReadTokens / 1e3)}k 토큰`);
if (rows.length) {
  console.log('\n상위 소비처');
  [...rows].sort((a, b) => b.oie - a.oie).slice(0, 8).forEach(r => {
    console.log(`  ${M(r.oie).padStart(6)}M  ${r.isMain ? '메인' : '서브'} ${r.sid} ${String(r.desc).slice(0, 30)} · 호출 ${r.calls} · 최대 ${Math.round(r.maxCtx / 1e3)}k${r.staleResumes ? ` · 쉰 재개 ${r.staleResumes}` : ''}`);
  });
}
console.log(`\n[7] 일일보고서 한 줄: 토큰 ${M(total)}M (서브 ${total ? Math.round((100 * sum(subs, 'oie')) / total) : 0}%) · 에이전트 ${subs.length}개 · 재기록 ${sum(rows, 'rewrites')}회 · 30만 초과 ${sum(rows, 'over300k')}회`);
