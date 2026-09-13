#!/usr/bin/env node
// PreToolUse(Read) 가드 — docs/ 아래 큰 .md 를 limit 없이 통째로 읽으면 거부하고 목차를 돌려준다.
// 근거: docs/헌법/AI협업_토큰절약_에이전트투입_매뉴얼.md §3-3 · §6 P2
'use strict';
const fs = require('fs');
const path = require('path');

const DOC_LIMIT_BYTES = 20000;
const INDEX_LIMIT_BYTES = 10000;
const TOC_MAX = 30;

let raw = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', chunk => { raw += chunk; });
process.stdin.on('end', () => {
  let input;
  try { input = JSON.parse(raw); } catch { process.exit(0); }

  const toolInput = input.tool_input || {};
  const filePath = String(toolInput.file_path || '');
  if (!/\.md$/i.test(filePath)) process.exit(0);
  // limit 이 없으면 Read 는 기본 2000줄까지 읽는다 — offset 만 준 것도 통째 읽기로 본다 (병렬이슈21)
  if (toolInput.limit != null) process.exit(0);
  if (!/[\\/]docs[\\/]/i.test(filePath)) process.exit(0);

  let stat;
  try { stat = fs.statSync(filePath); } catch { process.exit(0); }

  const isIndex = /^INDEX(_ALL)?\.md$/i.test(path.basename(filePath));
  const limit = isIndex ? INDEX_LIMIT_BYTES : DOC_LIMIT_BYTES;
  if (stat.size <= limit) process.exit(0);

  const toc = [];
  try {
    const lines = fs.readFileSync(filePath, 'utf8').split('\n');
    for (let i = 0; i < lines.length && toc.length < TOC_MAX; i++) {
      if (/^#{1,3} /.test(lines[i])) toc.push(`  ${i + 1}: ${lines[i].slice(0, 80)}`);
    }
    toc.push(`  (전체 ${lines.length}줄)`);
  } catch {
    toc.push('  (목차를 읽지 못함)');
  }

  const reason = [
    `[토큰규칙 P2] ${path.basename(filePath)} (${Math.round(stat.size / 1024)}KB) 통째 읽기 차단 — 기준 ${limit / 1000}KB.`,
    '카드: Read limit=40 · 필요한 절만: Read offset={시작줄} limit={길이} — offset 만 주면 통째로 읽히니 limit 필수.',
    '목차(줄 번호):',
    ...toc,
    '전체가 꼭 필요하면 offset=1 limit=2000 을 명시하라. 근거: docs/헌법/AI협업_토큰절약_에이전트투입_매뉴얼.md §3-3',
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
