#!/usr/bin/env bash
# [장치] 묶음 · 꾸러미 · 브라우저 · 앱 기동 · run.json 조립 — 설계 §4-1 M1~M4 · §5 · 고침1 F1(소스 ↔ 나무 경로 기준 대조) · F7(고정 머리 2회차 단언)
#   사용: boot.sh <m1|m2|m3|m4|runjson> <원자료 폴더> <작업 폴더>
#   작업 폴더 = 원자료 밖(러너 임시 폴더) · 원자료 폴더 = 공개될 파일만
#   종료 코드: 0 = 통과 · 1 = 그 단계 실패(실패 사실 · 오류 글자는 원자료 파일에 남김)
set -u -o pipefail

STAGE="${1:?usage: boot.sh <m1|m2|m3|m4|runjson> <raw> <work>}"
RAW="${2:?raw dir}"
WORK="${3:?work dir}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(git -C "$HERE" rev-parse --show-toplevel)"
mkdir -p "$RAW" "$WORK/stages"

REL_URL="https://github.com/gisskso-lab/hitpan-erp/releases/download/hqa-s4-fixture-01161edb"
APP_TGZ=hqa-s4-fixture-linux-x64-01161edb.tar.gz
SRC_TGZ=hqa-s4-fixture-src-01161edb.tar.gz
CHROME_ZIP=chrome-linux64.zip
ASSETS=(chrome-headless-shell-linux64.zip "$CHROME_ZIP" "$APP_TGZ" "$SRC_TGZ")
PORT=5799
BASE_URL="http://127.0.0.1:${PORT}"
BROWSER_WANT=151.0.7922.34
PW_VERSION=1.62.1
PKGS=(libasound2t64 libatk-bridge2.0-0t64 libatk1.0-0t64 libatspi2.0-0t64 libcairo2 libcups2t64 libdbus-1-3
  libdrm2 libgbm1 libglib2.0-0t64 libnspr4 libnss3 libpango-1.0-0 libx11-6 libxcb1 libxcomposite1
  libxdamage1 libxext6 libxfixes3 libxkbcommon0 libxrandr2
  fonts-liberation fonts-wqy-zenhei fonts-noto-color-emoji)

to_env() { if [ -n "${GITHUB_ENV:-}" ]; then printf '%s=%s\n' "$1" "$2" >> "$GITHUB_ENV"; fi; }
stage_json() { # 단계 이름 · 통과(true/false) · 세부(JSON 문자열 아님 · 한 줄)
  node -e 'const fs=require("fs");const [f,s,ok,d]=process.argv.slice(1);fs.writeFileSync(f,JSON.stringify({stage:s,ok:ok==="true",detail:d})+"\n");' \
    "$WORK/stages/$1.json" "$1" "$2" "$(printf '%s' "$3" | tr '\t\r\n' '   ' | cut -c1-600)"
}

m1() {
  local dl="$WORK/dl" tsv="$WORK/m1.tsv" fail=0 f got exp rel
  mkdir -p "$dl"; : > "$tsv"
  for f in SHA256SUMS "${ASSETS[@]}"; do
    if ! curl -fsSL --retry 3 --retry-delay 5 -o "$dl/$f" "$REL_URL/$f" 2> "$WORK/m1-curl-$f.err"; then
      printf 'download\t%s\t0\t%s\n' "$f" "$(tr '\t\r\n' '   ' < "$WORK/m1-curl-$f.err" | cut -c1-300)" >> "$tsv"; fail=1
    fi
  done
  local nexp; nexp=$(grep -c . "$HERE/expected-sums.txt")
  if [ "$nexp" != "4" ]; then printf 'expected_lines\t-\t0\t%s\n' "$nexp" >> "$tsv"; fail=1; fi
  for f in "${ASSETS[@]}"; do
    got=""; [ -f "$dl/$f" ] && got=$(sha256sum "$dl/$f" | cut -d' ' -f1)
    exp=$(awk -v n="$f" '$2==n || $2=="*"n {print $1}' "$HERE/expected-sums.txt")
    rel=""; [ -f "$dl/SHA256SUMS" ] && rel=$(awk -v n="$f" '$2==n || $2=="*"n {print $1}' "$dl/SHA256SUMS")
    if [ -n "$got" ] && [ "$got" = "$exp" ] && [ "$got" = "$rel" ]; then m=1; else m=0; fail=1; fi
    printf 'sha256\t%s\t%s\tgot=%s expected=%s release=%s\n' "$f" "$m" "$got" "$exp" "$rel" >> "$tsv"
  done
  # 소스 tar ↔ 나무 blob (파일별 git blob 해시)
  local sx="$WORK/src" root="$WORK/src/hqa-s4-fixture-src-01161edb/tools/hqa-s4-fixture"
  rm -rf "$sx"; mkdir -p "$sx"
  if [ -f "$dl/$SRC_TGZ" ] && tar -xzf "$dl/$SRC_TGZ" -C "$sx" 2> "$WORK/m1-tar.err"; then
    local outside; outside=$(tar -tzf "$dl/$SRC_TGZ" | grep -v '^hqa-s4-fixture-src-01161edb/' | head -5 | tr '\n' ' ')
    if [ -n "$outside" ]; then printf 'src_prefix\t-\t0\t%s\n' "$outside" >> "$tsv"; fail=1; fi
    # 고침2 I-c: tar 안 정규 파일 · 폴더가 아닌 항목(링크 · 장치 등) 수 — 0 이 아니면 빨강(find -type f 가 조용히 건너뛰던 것)
    local nonreg; nonreg=$(tar -tvzf "$dl/$SRC_TGZ" | awk '{c=substr($1,1,1); if (c!="-" && c!="d") n++} END{print n+0}')
    if [ "$nonreg" != "0" ]; then printf 'src_nonregular\t-\t0\tcount=%s\n' "$nonreg" >> "$tsv"; fail=1; else printf 'src_nonregular\t-\t1\tcount=0\n' >> "$tsv"; fi
    # 고침1 F1: 경로 기준 대조 — 옛 꼴은 나무 목록만 「해시 경로」 줄째 정렬(=해시순) · tar 목록은 경로순이라 diff 가 엇갈렸다
    #   해시 2벌: 원 바이트(--no-filters) · 레포 속성 적용(--path · autocrlf 끔 = 러너와 같은 조건) · 판정 = 속성 적용 해시
    #   파일별 값은 원자료 m1-src-hashes.json(정규화로만 같아진 파일 수 = filter_only_match)
    git -C "$REPO" ls-tree -r HEAD:tools/hqa-s4-fixture | awk -F'\t' '{split($1,a," "); print $2"\t"a[3]}' | LC_ALL=C sort > "$WORK/m1-tree.tsv"
    ( cd "$root" && find . -type f -printf '%P\n' | LC_ALL=C sort | while IFS= read -r p; do
        printf '%s\t%s\t%s\n' "$p" "$(git hash-object --no-filters -- "$p")" \
          "$(git -c core.autocrlf=false -C "$REPO" hash-object --path="tools/hqa-s4-fixture/$p" -- "$root/$p")"; done ) > "$WORK/m1-tar.tsv"
    local crc
    node "$HERE/m1-compare.js" "$WORK/m1-tree.tsv" "$WORK/m1-tar.tsv" "$RAW/m1-src-hashes.json" > "$WORK/m1-cmp.txt" 2>&1; crc=$?
    if [ "$crc" = "0" ]; then m=1; else m=0; fail=1; fi
    printf 'src_vs_tree\t-\t%s\t%s\n' "$m" "$(tr '\t\r\n' '   ' < "$WORK/m1-cmp.txt" | cut -c1-400)" >> "$tsv"
    to_env HQA_SRC_DIR "$root"
  else
    printf 'src_extract\t-\t0\t%s\n' "$(tr '\t\r\n' '   ' < "$WORK/m1-tar.err" 2>/dev/null | cut -c1-300)" >> "$tsv"; fail=1
  fi
  node - "$tsv" "$RAW/m1-bundle.json" "$REL_URL" <<'NODE'
const fs = require('fs');
const [tsv, out, rel] = process.argv.slice(2);
const checks = fs.readFileSync(tsv, 'utf8').split('\n').filter((l) => l.trim()).map((l) => {
  const [check, name, ok, detail] = l.split('\t');
  return { check, name, ok: ok === '1', detail: detail || '' };
});
fs.writeFileSync(out, JSON.stringify({ stage: 'M1', ok: checks.length > 0 && checks.every((c) => c.ok), release: rel, checks }, null, 2) + '\n');
NODE
  stage_json m1 "$([ $fail = 0 ] && echo true || echo false)" "checks=$(wc -l < "$tsv")"
  return $fail
}

m2() {
  local log="$WORK/m2-apt.txt" ok=0 i
  : > "$log"
  for i in 1 2 3; do
    echo "--- try $i" >> "$log"
    if sudo timeout 300 apt-get update >> "$log" 2>&1 \
       && sudo DEBIAN_FRONTEND=noninteractive timeout 600 apt-get install -y --no-install-recommends "${PKGS[@]}" >> "$log" 2>&1; then
      ok=1; break
    fi
    sleep 10
  done
  {
    echo "# M2 꾸러미 — README_BUNDLE §4-1 목록 + 글꼴 · 시도 ${i}회 · 결과 $([ $ok = 1 ] && echo 통과 || echo 실패)"
    dpkg-query -W -f='${Package}\t${Version}\n' "${PKGS[@]}" 2>&1
    if [ $ok != 1 ]; then echo "# apt 마지막 80줄"; tail -n 80 "$log"; fi
  } > "$RAW/m2-packages.txt"
  stage_json m2 "$([ $ok = 1 ] && echo true || echo false)" "tries=$i"
  [ $ok = 1 ]
}

m3() {
  local pw="$WORK/pw" cr="$WORK/chromium" ok=0 i
  mkdir -p "$pw" "$cr"
  for i in 1 2 3; do
    if ( cd "$pw" && PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 timeout 300 npm install --no-save --no-package-lock --no-audit --no-fund "playwright@${PW_VERSION}" > "$WORK/m3-npm.txt" 2>&1 ); then ok=1; break; fi
    sleep 10
  done
  if [ $ok != 1 ]; then stage_json m3 false "npm install failed: $(tail -n 5 "$WORK/m3-npm.txt")"; return 1; fi
  rm -rf "$cr"; mkdir -p "$cr"
  if ! unzip -q "$WORK/dl/$CHROME_ZIP" -d "$cr" 2> "$WORK/m3-unzip.txt"; then stage_json m3 false "unzip failed: $(cat "$WORK/m3-unzip.txt")"; return 1; fi
  local exe="$cr/chrome-linux64/chrome" missing
  missing=$(ldd "$exe" 2>&1 | grep 'not found' | awk '{print $1}' | tr '\n' ' ')
  PW_DIR="$pw/node_modules/playwright" EXE="$exe" WANT="$BROWSER_WANT" OUT="$WORK/stages/m3.json" MISSING="$missing" node <<'NODE'
const fs = require('fs');
const out = process.env.OUT;
const doc = { stage: 'm3', ok: false, playwright: null, browser: null, headless_shell: 'not used', ldd_missing: process.env.MISSING.trim() ? process.env.MISSING.trim().split(' ') : [], detail: '' };
(async () => {
  try {
    doc.playwright = JSON.parse(fs.readFileSync(process.env.PW_DIR + '/package.json', 'utf8')).version;
    const { chromium } = require(process.env.PW_DIR);
    const b = await chromium.launch({ executablePath: process.env.EXE, headless: true });
    doc.browser = b.version();
    await b.close();
    doc.ok = doc.browser === process.env.WANT && doc.playwright === '1.62.1';
    if (!doc.ok) doc.detail = 'version mismatch';
  } catch (e) {
    doc.detail = String(e && e.message ? e.message : e).slice(0, 600);
  }
  fs.writeFileSync(out, JSON.stringify(doc) + '\n');
  process.exit(doc.ok ? 0 : 1);
})();
NODE
  local rc=$?
  to_env HQA_CHROME "$exe"
  to_env HQA_PW_DIR "$pw/node_modules/playwright"
  return $rc
}

m4() {
  local ax="$WORK/app" out="$WORK/app-out.txt" tsv="$WORK/m4.tsv" failed="" ec="" waited=0 pid appdir
  : > "$tsv"; rm -rf "$ax"; mkdir -p "$ax"
  if ! tar -xzf "$WORK/dl/$APP_TGZ" -C "$ax" 2> "$WORK/m4-tar.txt"; then failed=extract; fi
  if [ -z "$failed" ]; then
    appdir=$(dirname "$(find "$ax" -maxdepth 3 -type f -name HqaS4Fixture | head -n1)")
    if [ -z "$appdir" ] || [ ! -f "$appdir/HqaS4Fixture" ]; then failed=find_exe; fi
  fi
  if [ -z "$failed" ]; then
    chmod +x "$appdir/HqaS4Fixture"; [ -f "$appdir/createdump" ] && chmod +x "$appdir/createdump"
    # 앱 폴더 안에서 띄운다(README_BUNDLE §5) · 127.0.0.1 만
    cd "$appdir" || failed=cd
  fi
  if [ -z "$failed" ]; then
    ASPNETCORE_ENVIRONMENT=Production nohup ./HqaS4Fixture --urls "$BASE_URL" > "$out" 2>&1 &
    pid=$!
    cd "$REPO" || true
    echo "$pid" > "$WORK/app.pid"
    while [ $waited -lt 60 ]; do
      if grep -q "Now listening on: ${BASE_URL}" "$out" 2>/dev/null; then break; fi
      if ! kill -0 "$pid" 2>/dev/null; then wait "$pid"; ec=$?; failed=process_exited; break; fi
      sleep 1; waited=$((waited+1))
    done
    if [ -z "$failed" ] && ! grep -q "Now listening on: ${BASE_URL}" "$out"; then failed=listen_timeout; fi
  fi
  local croot_ok=false
  if [ -z "$failed" ]; then
    if grep -Eq "Content root path: ${appdir}/?\s*$" "$out"; then croot_ok=true; else failed=content_root; fi
  fi
  if [ -z "$failed" ]; then
    local p code bad=0
    for p in / /before /after \
      /css/common/app.css /css/common/backoffice-v2.css /css/common/bootstrap.min.css \
      /css/before/backoffice-dashboard.css /css/after/backoffice-dashboard.css \
      /_content/MudBlazor/MudBlazor.min.css /_content/MudBlazor/MudBlazor.min.js /_framework/blazor.web.js; do
      code=$(curl -s -o /dev/null -w '%{http_code} %{size_download}' --max-time 20 "${BASE_URL}${p}")
      printf '%s\t%s\n' "$p" "$code" >> "$tsv"
      case "$code" in "200 "*) ;; *) bad=1 ;; esac
    done
    if [ $bad = 1 ]; then failed=http; fi
  fi
  if [ -f "$out" ]; then tail -n 200 "$out" > "$RAW/m4-stdout.txt"; else : > "$RAW/m4-stdout.txt"; fi
  node - "$tsv" "$RAW/m4-boot.json" "$failed" "$ec" "$waited" "$croot_ok" "$(wc -l < "$out" 2>/dev/null || echo 0)" "$BASE_URL" <<'NODE'
const fs = require('fs');
const [tsv, out, failed, ec, waited, croot, lines, base] = process.argv.slice(2);
const urls = fs.readFileSync(tsv, 'utf8').split('\n').filter((l) => l.trim()).map((l) => {
  const [path, r] = l.split('\t'); const [code, bytes] = (r || '').split(' ');
  return { path, code: Number(code), bytes: Number(bytes) };
});
fs.writeFileSync(out, JSON.stringify({
  stage: 'M4', ok: failed === '', failed_step: failed || null, exit_code: ec === '' ? null : Number(ec),
  base_url: base, environment: 'Production', waited_seconds: Number(waited), content_root_ok: croot === 'true',
  urls, stdout_lines_total: Number(lines), stdout_tail_file: 'm4-stdout.txt', stdout_tail_max: 200,
}, null, 2) + '\n');
NODE
  stage_json m4 "$([ -z "$failed" ] && echo true || echo false)" "failed_step=${failed:-none}"
  to_env HQA_BASE_URL "$BASE_URL"
  [ -n "${appdir:-}" ] && to_env HQA_APP_DIR "$appdir"
  [ -z "$failed" ]
}

runjson() {
  local ls2="$WORK/ls2.txt"
  git -C "$REPO" ls-remote origin "refs/heads/main" "refs/heads/feat/20261107-백오피스S4닫기" "refs/heads/hqa/20261107-S4-시험앱" > "$ls2" 2> "$WORK/ls2.err" || echo "ls-remote error" >> "$WORK/ls2.err"
  RAWD="$RAW" WORKD="$WORK" node <<'NODE'
const fs = require('fs');
const raw = process.env.RAWD, work = process.env.WORKD, e = process.env;
const readJson = (f) => { if (!fs.existsSync(f)) return null; try { return JSON.parse(fs.readFileSync(f, 'utf8')); } catch (err) { return { parse_error: String(err.message).slice(0, 200) }; } };
const lines = (f) => fs.existsSync(f) ? fs.readFileSync(f, 'utf8').split('\n').filter((l) => l.trim()) : [];
const m0 = readJson(raw + '/m0-assert.json') || {};
const m3 = readJson(work + '/stages/m3.json') || {};
const ls2 = lines(work + '/ls2.txt').map((l) => { const [sha, ref] = l.split('\t'); return { ref, sha }; });
const ls1 = m0.ls_remote_start || [];
const same = ls1.length === 3 && ls2.length === 3 && ls1.every((a) => ls2.some((b) => b.ref === a.ref && b.sha === a.sha));
// 고침1 F7: 2회차 단언 — 기능 · 시험 앱 머리 = 고정값 · main = 시작과 같음 · 줄 수 3/3 (다르면 이 단계 빨강)
const shaOf = (l, r) => { const x = l.find((a) => a.ref === r); return x ? x.sha : null; };
const R_MAIN = 'refs/heads/main', R_FEAT = 'refs/heads/feat/20261107-백오피스S4닫기', R_FIX = 'refs/heads/hqa/20261107-S4-시험앱';
const headsAssert = {
  lines_start: ls1.length, lines_end: ls2.length,
  feat_end_ok: shaOf(ls2, R_FEAT) === '16e88c01092d33eb643c6cac454703f00efa02ac',
  fixture_end_ok: shaOf(ls2, R_FIX) === '01161edb5618b5e34ea2c5e655dcaaf2333d3a68',
  main_same: !!shaOf(ls1, R_MAIN) && shaOf(ls1, R_MAIN) === shaOf(ls2, R_MAIN),
};
headsAssert.ok = ls1.length === 3 && ls2.length === 3 && headsAssert.feat_end_ok && headsAssert.fixture_end_ok && headsAssert.main_same;
const stageNames = ['M0', 'SELFTEST_LEAK', 'SELFTEST_JUDGE', 'M1', 'M2', 'M3', 'M4', 'PROBE', 'ITEMS', 'JUDGE'];
const outcomes = {};
for (const s of stageNames) outcomes[s] = e['OUT_' + s] || null;
const doc = {
  run_id: e.GITHUB_RUN_ID || null, run_attempt: e.GITHUB_RUN_ATTEMPT || null,
  workflow: e.GITHUB_WORKFLOW || null, job: e.GITHUB_JOB || null, ref: e.GITHUB_REF || null,
  device_commit: e.GITHUB_SHA || null, fixture_tree: '0b31f8692367ae28a6fac255d8eae5c931066d7b',
  mode: m0.mode || null,
  runner: { image_os: e.ImageOS || null, image_version: e.ImageVersion || null, os: e.RUNNER_OS || null, arch: e.RUNNER_ARCH || null },
  node: process.version, playwright: m3.playwright || null, browser: m3.browser || null,
  headless_shell: 'not used', ldd_missing: m3.ldd_missing || null,
  started_utc: lines(work + '/started_utc.txt')[0] || null, ended_utc: new Date().toISOString(),
  ls_remote_start: ls1, ls_remote_end: ls2, fixed_heads_unchanged: same, fixed_heads_assert: headsAssert,
  stage_outcomes: outcomes,
  stage_results: Object.fromEntries(['m1', 'm2', 'm3', 'm4'].map((s) => [s, readJson(work + '/stages/' + s + '.json')])),
  selftest_leak: readJson(work + '/selftest-leak.json'),
  selftest_judge: lines(work + '/selftest-judge.status.txt').join(' ') || null,
  probe: fs.existsSync(raw + '/probe.json') ? 'see probe.json (rehearsal - judgement-free)' : null,
  items: m0.mode === 'armed' ? 'see samples.jsonl / verdict-device.json' : 'rehearsal: item measurement 0',
  verdict: 'device reading only - judgement = HQA',
};
fs.writeFileSync(raw + '/run.json', JSON.stringify(doc, null, 2) + '\n');
if (!headsAssert.ok) { process.stderr.write('[run.json] 고정 머리 2회차 단언 실패 ' + JSON.stringify(headsAssert) + '\n'); process.exit(1); }
NODE
}

case "$STAGE" in
  m1) m1 ;;
  m2) m2 ;;
  m3) m3 ;;
  m4) m4 ;;
  runjson) runjson ;;
  *) echo "unknown stage: $STAGE" >&2; exit 2 ;;
esac
