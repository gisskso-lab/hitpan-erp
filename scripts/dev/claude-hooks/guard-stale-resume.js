#!/usr/bin/env node
// PreToolUse(SendMessage) 가드 — 캐시가 만료된(오래 쉰) 서브에이전트를 이어 부르면 거부한다.
// 이어 부르면 그 에이전트 대화 전체를 캐시에 다시 쓰기 때문이다.
// 근거: docs/헌법/AI협업_토큰절약_에이전트투입_매뉴얼.md §4-5 · §6 P3
'use strict';
const fs = require('fs');
const path = require('path');

const DEFAULT_TTL_MIN = 5;
const LONG_TTL_MIN = 55;
const LONG_TTL_AGENT_TYPES = new Set(['hp-verifier', 'hp-judge']);
const BYPASS_TOKEN = '#재개승인';

function readJson(file) {
  try { return JSON.parse(fs.readFileSync(file, 'utf8')); } catch { return null; }
}

let raw = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', chunk => { raw += chunk; });
process.stdin.on('end', () => {
  let input;
  try { input = JSON.parse(raw); } catch { process.exit(0); }

  const toolInput = input.tool_input || {};
  const target = String(toolInput.to || '');
  if (!target || String(toolInput.message || '').includes(BYPASS_TOKEN)) process.exit(0);

  const transcriptPath = String(input.transcript_path || '');
  if (!transcriptPath) process.exit(0);
  const subagentDir = path.join(transcriptPath.replace(/\.jsonl$/i, ''), 'subagents');

  let agentFile = path.join(subagentDir, `agent-${target}.jsonl`);
  let meta = readJson(agentFile.replace(/\.jsonl$/, '.meta.json')) || {};

  if (!fs.existsSync(agentFile)) {
    // 이름으로 부른 경우: meta.json 의 name/description 으로 찾는다. 못 찾으면 막지 않는다.
    let names;
    try { names = fs.readdirSync(subagentDir).filter(f => f.endsWith('.meta.json')); } catch { process.exit(0); }
    const hit = names.find(f => {
      const m = readJson(path.join(subagentDir, f));
      return m && (m.name === target || m.description === target);
    });
    if (!hit) process.exit(0);
    agentFile = path.join(subagentDir, hit.replace(/\.meta\.json$/, '.jsonl'));
    meta = readJson(path.join(subagentDir, hit)) || {};
    if (!fs.existsSync(agentFile)) process.exit(0);
  }

  const idleMin = (Date.now() - fs.statSync(agentFile).mtimeMs) / 60000;
  const ttlMin = LONG_TTL_AGENT_TYPES.has(meta.agentType) ? LONG_TTL_MIN : DEFAULT_TTL_MIN;
  if (idleMin <= ttlMin) process.exit(0);

  const label = meta.description ? `${target} (${meta.description})` : target;
  const reason = [
    `[토큰규칙 P3] 에이전트 ${label} 가 ${Math.round(idleMin)}분 쉬었다 — 캐시 ${ttlMin}분 만료. 이어 부르면 대화 전체를 다시 쓴다.`,
    '→ 새 에이전트를 띄우고 "이전 결과: {산출물 주소}" 만 넘겨라.',
    `꼭 이어야 하면 메시지에 ${BYPASS_TOKEN} 를 넣어라. 근거: docs/헌법/AI협업_토큰절약_에이전트투입_매뉴얼.md §4-5`,
  ].join('\n');

  process.stdout.write(JSON.stringify({
    hookSpecificOutput: {
      hookEventName: 'PreToolUse',
      permissionDecision: 'deny',
      permissionDecisionReason: reason,
    },
  }));
  process.exit(0);
});
