'use strict';
// M1 [장치] 소스 tar ↔ 시험 앱 나무 — 고침1 F1
//   사용: node m1-compare.js <나무.tsv> <tar.tsv> <출력 JSON>
//     나무.tsv = 「경로 \t blob」 (git ls-tree -r <장치 커밋>:tools/hqa-s4-fixture · 장치 커밋 = boot.sh g_dev · v5 별건 A K2⁗ 뒤 · 옛 글자: HEAD:tools/hqa-s4-fixture)
//     tar.tsv  = 「경로 \t 원 바이트 blob(--no-filters) \t 속성 적용 blob(--path)」
//   경로로 짝을 맞춘다(정렬 순서와 무관). 판정 = 속성 적용 blob 이 나무 blob 과 전부 같음 · 파일 집합 같음.
//   원 바이트 blob 도 함께 남긴다 — filter_only_match = 속성 적용으로만 같아진 파일 수(줄 끝 등 정규화 차이만 있는 파일).
//   종료 코드: 0 = 일치 · 1 = 다름 · 2 = 입력 오류
const fs = require('fs');

function rows(f) {
  return fs.readFileSync(f, 'utf8').split('\n').filter((l) => l !== '').map((l) => l.split('\t'));
}

function compare(treeRows, tarRows) {
  const tree = new Map(treeRows.map(([p, b]) => [p, b]));
  const files = [];
  let normEq = 0;
  let rawEq = 0;
  let filterOnly = 0;
  for (const [p, raw, norm] of tarRows) {
    const tb = tree.has(p) ? tree.get(p) : null;
    const nm = tb !== null && norm === tb;
    const rm = tb !== null && raw === tb;
    if (nm) normEq++;
    if (rm) rawEq++;
    if (nm && !rm) filterOnly++;
    files.push({ path: p, tree_blob: tb, raw_blob: raw || null, norm_blob: norm || null, norm_match: nm, raw_match: rm });
  }
  const tarSet = new Set(tarRows.map((r) => r[0]));
  const treeOnly = [...tree.keys()].filter((p) => !tarSet.has(p));
  const tarOnly = tarRows.filter((r) => !tree.has(r[0])).map((r) => r[0]);
  const ok = tree.size > 0 && tarRows.length === tree.size && treeOnly.length === 0 && tarOnly.length === 0 && normEq === tree.size;
  return {
    stage: 'M1', check: 'src_vs_tree', ok,
    judged_by: 'norm_blob = git hash-object --path (repo attributes · core.autocrlf=false)',
    tree_files: tree.size, tar_files: tarRows.length, norm_match: normEq, raw_match: rawEq, filter_only_match: filterOnly,
    tree_only: treeOnly, tar_only: tarOnly, files,
  };
}

module.exports = { compare };

if (require.main === module) {
  const [treeF, tarF, out] = process.argv.slice(2);
  let rc;
  try {
    if (!treeF || !tarF || !out) throw new Error('usage: node m1-compare.js <tree.tsv> <tar.tsv> <out.json>');
    const doc = compare(rows(treeF), rows(tarF));
    fs.writeFileSync(out, JSON.stringify(doc, null, 2) + '\n');
    process.stdout.write(`tree_files=${doc.tree_files} tar_files=${doc.tar_files} norm_match=${doc.norm_match} raw_match=${doc.raw_match} filter_only_match=${doc.filter_only_match} tree_only=${doc.tree_only.length} tar_only=${doc.tar_only.length}`);
    rc = doc.ok ? 0 : 1;
  } catch (e) {
    process.stdout.write('m1-compare error: ' + String(e && e.message ? e.message : e).slice(0, 160));
    rc = 2;
  }
  process.exit(rc);
}
