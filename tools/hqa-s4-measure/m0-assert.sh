#!/usr/bin/env bash
# M0 [장치] 고정 단언 — 설계 §4-1 M0 ①~⑤ · §6-5 장전(MODE) 단언 · 고침1 F7(고정 머리 값 단언) · F8(장전 때 HQA 회신 칸 확정 관문)
#   세부 글자의 SHA 는 key=값 꼴로만 쓴다(가림 F5 — 알려진 칸만 16진 허용)
#   사용: m0-assert.sh <원자료 폴더> [--guard-only]
#   결과: <원자료 폴더>/m0-assert.json · GITHUB_OUTPUT 에 mode=rehearsal|armed|invalid
#   종료 코드: 0 = 전부 통과 · 1 = 하나라도 다름(이후 단계 0 · 원자료는 이 파일만)
#   고침4 G8(D-5 ① ⑤ ⑥ ⑦): ⑤ github.sha ≠ 체크아웃 HEAD → 측정 0(Actions 안이면 github.sha 없음도 0 · HEAD^ ≠ MODE 적힌 SHA 는 아래 장전 단언)
#     ⑥ run_attempt > 1 → 측정 0(재실행 = 새 회차 아님 · 설계서 §6-6 「[장치] 빨강은 같은 커밋 재실행 1회」 대체됨)
#     ⑦ 장전(armed) 때 원자료 가지가 이미 있으면 측정 0(이 잡 첫머리 ls-remote)
#     ① 원자료(m0-assert.json)에 github_sha · run_id · run_attempt · mode_line(MODE 줄)
#     --guard-only = ⑤ ⑥ 만 재고 끝(망 접속 0 · 파일 0 — selftest-judge.js 가 attempt 2 → 측정 0 을 동작으로 확인)
set -u -o pipefail

RAW="${1:?usage: m0-assert.sh <raw-dir> [--guard-only]}"
GUARD_ONLY=0; [ "${2:-}" = "--guard-only" ] && GUARD_ONLY=1
mkdir -p "$RAW"

# 고침4 G9(G-값 · F′): 옛 값 FIX_TREE=0b31f869… · BASE=01161edb… 대체 → F′(b967b1b0 · A2 화면 더한 판 · 부모 01161edb)
# H4-G 값(F″): 옛 값 FIX_TREE=47566f04… · BASE=b967b1b0… 대체 → F″(88085da1 · 비교 화면 e1t3 · v2 더한 판 · 부모 b967b1b0 · 그 부모 01161edb)
FIX_TREE=47a909c8e7f7d11c89f455d8ff28ca2b9e8fc8e2   # 시험 앱 나무(tools/hqa-s4-fixture) — F″ 나무(파일 41)
BASE=88085da1d797b915b8e8be314517af180d0b9939       # 시험 앱 가지 고정 머리 — F″
BASE_PARENT=b967b1b007fb519d9a91537fbef911e818e950de # F″ 의 부모(F′ · 옛 고정 머리) — 조상 단언(H4-G 더함)
BASE_ROOT=01161edb5618b5e34ea2c5e655dcaaf2333d3a68   # F′ 의 부모(첫 고정 머리) — 조상 단언 유지
FEAT=16e88c01092d33eb643c6cac454703f00efa02ac       # 기능 가지 고정 머리(고친 꼴 A')
A2SRC=2a6a0ee10fcc3c79791f49747070009b25290fec      # A2 원 커밋(⑤ 쌍 ⑤ · ⑥ 의 원 경로 쪽 · 고침4 G9 결-4)
E1T3SRC=e456e4f6a0b2dc36f14ef53f64106d455f363ecb    # 비교 화면 e1t3 원 커밋(⑤ 쌍 ⑦ · ⑧ · 결-H2 · 결-H9)
V2SRC=74a76c58f3005dcc92be0a2043918dcc483e6220      # 비교 화면 v2 원 커밋(⑤ 쌍 ⑨ · ⑩ · 결-H9)
WF=.github/workflows/hqa-s4-measure.yml
MDIR=tools/hqa-s4-measure/
MODE_PATH=tools/hqa-s4-measure/MODE
HEADS=("refs/heads/main" "refs/heads/feat/20261107-백오피스S4닫기" "refs/heads/hqa/20261107-S4-시험앱")
# ⑤ blob 6쌍 — 시험 앱 경로 ↔ 원 커밋 원 경로 (README_BUNDLE §1-2 표 · 1~4 = 16e88c01 이 원 커밋인 고친 꼴 파일)
#   고침4 G9(결-4): 옛 4쌍 → 6쌍 — 쌍 5 = A2 칸 ↔ 2a6a0ee1 원 경로(49d7502d…) · 쌍 6 = 2a6a0ee1 원 CSS ↔ F′ css/after(둘 다 0e922200… · 乙 전제를 기계로)
#   H4-G 값(결-H9): 6쌍 → 10쌍 — 쌍 7 = E1T3 칸 ↔ e456e4f6 원 경로(644e27eb…) · 쌍 8 = e456e4f6 원 CSS ↔ F″ css/after(0e922200…)
#     · 쌍 9 = V2 칸 ↔ 74a76c58 원 경로(8830faf1…) · 쌍 10 = 74a76c58 원 CSS ↔ F″ css/after(0e922200…) · 쌍 1~6 그대로
BLOB_FIX=(
  "tools/hqa-s4-fixture/Components/After/CsStatusCell.razor"
  "tools/hqa-s4-fixture/wwwroot/css/after/backoffice-dashboard.css"
  "tools/hqa-s4-fixture/Components/After/CsStatusMenu.cs"
  "tools/hqa-s4-fixture/Vocab/CsVocab.cs"
  "tools/hqa-s4-fixture/Components/A2/CsStatusCell.razor"
  "tools/hqa-s4-fixture/wwwroot/css/after/backoffice-dashboard.css"
  "tools/hqa-s4-fixture/Components/E1T3/CsStatusCell.razor"
  "tools/hqa-s4-fixture/wwwroot/css/after/backoffice-dashboard.css"
  "tools/hqa-s4-fixture/Components/V2/CsStatusCell.razor"
  "tools/hqa-s4-fixture/wwwroot/css/after/backoffice-dashboard.css"
)
BLOB_SRC=(
  "src/HitPan.Backoffice/Components/Pages/Platform/Components/CsStatusCell.razor"
  "src/HitPan.Backoffice/wwwroot/css/backoffice-dashboard.css"
  "src/HitPan.Backoffice/Components/Pages/Platform/Components/CsStatusMenu.cs"
  "src/HitPan.Backoffice/Components/Pages/Platform/Components/CsVocab.cs"
  "src/HitPan.Backoffice/Components/Pages/Platform/Components/CsStatusCell.razor"
  "src/HitPan.Backoffice/wwwroot/css/backoffice-dashboard.css"
  "src/HitPan.Backoffice/Components/Pages/Platform/Components/CsStatusCell.razor"
  "src/HitPan.Backoffice/wwwroot/css/backoffice-dashboard.css"
  "src/HitPan.Backoffice/Components/Pages/Platform/Components/CsStatusCell.razor"
  "src/HitPan.Backoffice/wwwroot/css/backoffice-dashboard.css"
)
# 쌍마다 원 경로를 읽을 커밋(SHA 로만 · 태그 0)
BLOB_REV=("$FEAT" "$FEAT" "$FEAT" "$FEAT" "$A2SRC" "$A2SRC" "$E1T3SRC" "$E1T3SRC" "$V2SRC" "$V2SRC")

TMPD=$(mktemp -d)
TSV="$TMPD/checks.tsv"; LS="$TMPD/ls1.txt"; BL="$TMPD/blobs.tsv"
: > "$TSV"; : > "$LS"; : > "$BL"
fail=0

one_line() { tr '\t\r\n' '   ' | cut -c1-400; }
rec() { # 이름 · 통과(1/0) · 세부
  printf '%s\t%s\t%s\n' "$1" "$2" "$(printf '%s' "$3" | one_line)" >> "$TSV"
  if [ "$2" != "1" ]; then fail=1; fi
}

HEAD_SHA=$(git rev-parse HEAD 2>&1)
IN_ACTIONS=0; [ "${GITHUB_ACTIONS:-}" = "true" ] && IN_ACTIONS=1
# 고침4 G8 ⑤: 옛 꼴 = GITHUB_SHA 가 있을 때만 대조 → Actions 안이면 반드시 대조(없으면 다름)
if [ -n "${GITHUB_SHA:-}" ] || [ "$IN_ACTIONS" = "1" ]; then
  if [ -n "${GITHUB_SHA:-}" ] && [ "$HEAD_SHA" = "$GITHUB_SHA" ]; then rec head_is_event 1 "HEAD=$HEAD_SHA"; else rec head_is_event 0 "HEAD=$HEAD_SHA event=${GITHUB_SHA:-none}"; fi
fi
# 고침4 G8 ⑥: 재실행(run_attempt > 1) = 측정 0
if [ -n "${GITHUB_RUN_ATTEMPT:-}" ] || [ "$IN_ACTIONS" = "1" ]; then
  if [ "${GITHUB_RUN_ATTEMPT:-}" = "1" ]; then rec run_attempt_one 1 "run_attempt=1"; else rec run_attempt_one 0 "run_attempt=${GITHUB_RUN_ATTEMPT:-none} want=1"; fi
fi
if [ "$GUARD_ONLY" = "1" ]; then
  cut -f1,2 "$TSV" | sed 's/\t1$/ 통과/; s/\t0$/ 다름/'
  rm -rf "$TMPD"
  if [ "$fail" != "0" ]; then echo "[M0] 실행 사슬 단언 실패(--guard-only)" >&2; exit 1; fi
  echo "[M0] 실행 사슬 통과(--guard-only)"
  exit 0
fi

# ① 시험 앱 나무 해시
got=$(git rev-parse "HEAD:tools/hqa-s4-fixture" 2>&1)
if [ "$got" = "$FIX_TREE" ]; then rec m0_1_fixture_tree 1 "tree=$got"; else rec m0_1_fixture_tree 0 "got=$got want=$FIX_TREE"; fi

# ② 시험 앱 고정 머리(F″) 가 HEAD 조상 · F″ 의 부모 b967b1b0 · 그 부모 01161edb 도 HEAD 조상(H4-G 값 — 셋 다 · 옛 둘은 유지)
if git merge-base --is-ancestor "$BASE" HEAD 2>"$TMPD/e2"; then rec m0_2_base_ancestor 1 "base=$BASE"
else rec m0_2_base_ancestor 0 "not ancestor: $(cat "$TMPD/e2")"; fi
if git merge-base --is-ancestor "$BASE_PARENT" HEAD 2>"$TMPD/e2p"; then rec m0_2_parent_ancestor 1 "parent=$BASE_PARENT"
else rec m0_2_parent_ancestor 0 "not ancestor: $(cat "$TMPD/e2p")"; fi
if git merge-base --is-ancestor "$BASE_ROOT" HEAD 2>"$TMPD/e2r"; then rec m0_2_root_ancestor 1 "parent=$BASE_ROOT"
else rec m0_2_root_ancestor 0 "not ancestor: $(cat "$TMPD/e2r")"; fi

# ③ diff ⊆ 허용 목록 (이름 바꾸기 감지 끔 — 옮긴 파일의 옛 경로도 드러나게)
bad=""; n=0
if git diff --no-renames --name-only "$BASE" HEAD > "$TMPD/d3" 2>"$TMPD/e3"; then
  while IFS= read -r p; do
    [ -z "$p" ] && continue
    n=$((n+1))
    if [ "$p" = "$WF" ]; then continue; fi
    case "$p" in "$MDIR"*) continue ;; esac
    bad="$bad $p"
  done < "$TMPD/d3"
  if [ -z "$bad" ]; then rec m0_3_diff_allowed 1 "changed=$n"; else rec m0_3_diff_allowed 0 "outside allowed:$bad"; fi
else
  rec m0_3_diff_allowed 0 "diff error: $(cat "$TMPD/e3")"
fi

# ④ 고정 머리 3개 ls-remote (1회차 · 2회차는 run.json 조립 때)
#   고침1 F7: 기록만이 아니라 단언 — 줄 수 = 3 · 기능 머리 = FEAT · 시험 앱 머리 = BASE · main 은 값 기록(시작/끝 같음은 runjson 이 단언)
if git ls-remote origin "${HEADS[@]}" > "$LS" 2>"$TMPD/e4"; then
  nl=$(grep -c . "$LS")
  if [ "$nl" = "3" ]; then rec m0_4_ls_remote_lines 1 "lines=3"; else rec m0_4_ls_remote_lines 0 "lines=$nl want=3"; fi
  gm=$(awk -F'\t' -v r="${HEADS[0]}" '$2==r {print $1}' "$LS")
  gf=$(awk -F'\t' -v r="${HEADS[1]}" '$2==r {print $1}' "$LS")
  gb=$(awk -F'\t' -v r="${HEADS[2]}" '$2==r {print $1}' "$LS")
  if [ "$gf" = "$FEAT" ]; then rec m0_4_feat_head 1 "feat=$gf"; else rec m0_4_feat_head 0 "feat=${gf:-none} want=$FEAT"; fi
  if [ "$gb" = "$BASE" ]; then rec m0_4_fixture_head 1 "fixture=$gb"; else rec m0_4_fixture_head 0 "fixture=${gb:-none} want=$BASE"; fi
  if [[ "$gm" =~ ^[0-9a-f]{40}$ ]]; then rec m0_4_main_recorded 1 "main=$gm"; else rec m0_4_main_recorded 0 "main=${gm:-none}"; fi
else
  rec m0_4_ls_remote 0 "ls-remote error: $(cat "$TMPD/e4")"
fi

# ⑤ blob 10쌍 — 원 커밋(기능 머리 · A2 · E1T3 · V2 원 커밋)이 없으면 SHA 로만 받는다(태그 0)
for rv in "$FEAT" "$A2SRC" "$E1T3SRC" "$V2SRC"; do
  if ! git cat-file -e "${rv}^{commit}" 2>/dev/null; then
    git fetch --no-tags --depth=1 origin "$rv" >> "$TMPD/f5" 2>&1 || true
  fi
done
b_ok=1
for i in 0 1 2 3 4 5 6 7 8 9; do
  a=$(git rev-parse "HEAD:${BLOB_FIX[$i]}" 2>&1)
  s=$(git rev-parse "${BLOB_REV[$i]}:${BLOB_SRC[$i]}" 2>&1)
  if [ "$a" = "$s" ]; then m=1; else m=0; b_ok=0; fi
  printf '%s\t%s\t%s\t%s\t%s\t%s\n' "${BLOB_FIX[$i]}" "$a" "${BLOB_SRC[$i]}" "$s" "$m" "${BLOB_REV[$i]}" | one_line >> "$BL"
  printf '\n' >> "$BL"
done
rec m0_5_blobs "$b_ok" "10 pairs"

# 장전 단언 (설계 §6-5)
mode=invalid
mtxt=$(git show "HEAD:$MODE_PATH" 2>&1 | tr -d '\r')
mfirst=$(printf '%s\n' "$mtxt" | head -n1)
mlines=$(printf '%s\n' "$mtxt" | grep -c .)
if [ "$mfirst" = "rehearsal" ] && [ "$mlines" = "1" ]; then
  mode=rehearsal; rec mode_value 1 "rehearsal"
elif [[ "$mfirst" =~ ^armed:([0-9a-f]{40}):r([1-9][0-9]*)$ ]] && [ "$mlines" = "1" ]; then
  mode=armed
  want_parent="${BASH_REMATCH[1]}"
  rec mode_value 1 "$mfirst"
  parents=$(git rev-list --parents -n1 HEAD | wc -w)
  if [ "$parents" = "2" ]; then rec armed_one_parent 1 "1"; else rec armed_one_parent 0 "parents=$((parents-1))"; fi
  par=$(git rev-parse HEAD^ 2>&1)
  if [ "$par" = "$want_parent" ]; then rec armed_parent_is_D 1 "parent=$par"; else rec armed_parent_is_D 0 "parent=$par mode=$want_parent"; fi
  dn=$(git diff --no-renames --name-only HEAD^ HEAD 2>&1)
  if [ "$dn" = "$MODE_PATH" ]; then rec armed_diff_only_mode 1 "$dn"; else rec armed_diff_only_mode 0 "diff=$dn"; fi
  ns=$(git diff --no-renames --numstat HEAD^ HEAD -- "$MODE_PATH" 2>&1 | cut -f1,2)
  if [ "$ns" = "$(printf '1\t1')" ]; then rec armed_one_line 1 "1/1"; else rec armed_one_line 0 "numstat=$ns"; fi
  pm=$(git show "HEAD^:$MODE_PATH" 2>&1 | tr -d '\r')
  if [ "$pm" = "rehearsal" ]; then rec armed_prev_mode 1 "rehearsal"; else rec armed_prev_mode 0 "parent MODE=$pm"; fi
  # 고침4 G8 ⑦: 장전 때 원자료 가지(publish.sh 와 같은 이름)가 이미 있으면 측정 0 — ls-remote 오류도 0
  RAW_BRANCH="hqa/20261116-S4-gh실측-원자료-${HEAD_SHA:0:8}"
  if git ls-remote origin "refs/heads/$RAW_BRANCH" > "$TMPD/rb" 2>"$TMPD/erb"; then
    if [ -s "$TMPD/rb" ]; then rec armed_raw_branch_absent 0 "raw branch exists"; else rec armed_raw_branch_absent 1 "absent"; fi
  else
    rec armed_raw_branch_absent 0 "ls-remote error: $(cat "$TMPD/erb")"
  fi
  # 고침1 F8: HQA 회신 대기 칸이 하나라도 확정 꼴이 아니면 여기(M0)서 장전 거부 — 필수 칸 목록은 hqa-gate.js 에 고정
  if git show "HEAD:tools/hqa-s4-measure/rules.json" > "$TMPD/rules.json" 2>"$TMPD/eg" \
     && git show "HEAD:tools/hqa-s4-measure/hqa-gate.js" > "$TMPD/hqa-gate.js" 2>>"$TMPD/eg"; then
    gate=$(node "$TMPD/hqa-gate.js" "$TMPD/rules.json" 2>&1); grc=$?
    if [ "$grc" = "0" ]; then rec armed_hqa_confirmed 1 "$gate"; else rec armed_hqa_confirmed 0 "rc=$grc $gate"; fi
  else
    rec armed_hqa_confirmed 0 "read error: $(cat "$TMPD/eg")"
  fi
else
  rec mode_value 0 "unknown MODE: $mfirst (lines=$mlines)"
fi

node - "$TSV" "$LS" "$BL" "$RAW/m0-assert.json" "$mode" "$HEAD_SHA" "$mfirst" <<'NODE'
const fs = require('fs');
const [tsv, ls, bl, out, mode, head, modeLine] = process.argv.slice(2);
const rows = (f) => fs.readFileSync(f, 'utf8').split('\n').filter((l) => l.trim() !== '');
const checks = rows(tsv).map((l) => { const [check, ok, detail] = l.split('\t'); return { check, ok: ok === '1', detail: detail || '' }; });
const lsRemote = rows(ls).map((l) => { const [sha, ref] = l.split('\t'); return { ref, sha }; });
// 고침4 G9 · H4-G 값: 쌍마다 원 경로 커밋(commit · 16e88c01 · 2a6a0ee1 · e456e4f6 · 74a76c58)
const blobs = rows(bl).map((l) => { const c = l.split(/\s+/); return { fixture_path: c[0], fixture_blob: c[1], src_path: c[2], src_blob: c[3], match: c[4] === '1', commit: c[5] || null }; });
const doc = {
  stage: 'M0',
  ok: checks.every((c) => c.ok),
  mode,
  head,
  // 고침4 G8 ①: github.sha · MODE 줄(첫 줄만 · 꼴 = rehearsal 또는 armed:<40자>:r<n> — 그 밖이면 앞 40자만)
  github_sha: process.env.GITHUB_SHA || null,
  mode_line: /^(rehearsal|armed:[0-9a-f]{40}:r[1-9][0-9]*)$/.test(modeLine || '') ? modeLine : (modeLine ? 'other:' + String(modeLine).slice(0, 40).replace(/[^A-Za-z0-9:._-]/g, '?') : null),
  run_id: process.env.GITHUB_RUN_ID || null,
  run_attempt: process.env.GITHUB_RUN_ATTEMPT || null,
  ref: process.env.GITHUB_REF || null,
  // H4-G 값: F″ 값(옛 47566f04… · b967b1b0… 대체 · 조상 단언은 checks m0_2_parent_ancestor(b967b1b0) · m0_2_root_ancestor(01161edb))
  fixture_tree_expected: '47a909c8e7f7d11c89f455d8ff28ca2b9e8fc8e2',
  base_expected: '88085da1d797b915b8e8be314517af180d0b9939',
  feat_expected: '16e88c01092d33eb643c6cac454703f00efa02ac',
  checks,
  ls_remote_start: lsRemote,
  blobs,
  generated_utc: new Date().toISOString(),
};
fs.writeFileSync(out, JSON.stringify(doc, null, 2) + '\n');
NODE
node_rc=$?
if [ "$node_rc" != "0" ]; then echo "[M0] m0-assert.json 쓰기 실패 rc=$node_rc" >&2; fail=1; fi

if [ -n "${GITHUB_OUTPUT:-}" ]; then printf 'mode=%s\n' "$mode" >> "$GITHUB_OUTPUT"; fi
cut -f1,2 "$TSV" | sed 's/\t1$/ 통과/; s/\t0$/ 다름/'
rm -rf "$TMPD"
if [ "$fail" != "0" ]; then echo "[M0] 단언 실패 — 이후 단계 0" >&2; exit 1; fi
echo "[M0] 통과 · mode=$mode"
exit 0
