#!/usr/bin/env bash
# [장치] 묶음 · 꾸러미 · 브라우저 · 앱 기동 · run.json 조립 — 설계 §4-1 M1~M4 · §5 · 고침1 F1(소스 ↔ 나무 경로 기준 대조) · F7(고정 머리 2회차 단언)
#   사용: boot.sh <m1|m2|m3|m4|runjson|round> <원자료 폴더> <작업 폴더>
#   고침5 H2: runjson 에 selftest_judge_counts(K-1) · rules_sha256 · judge_sha256 · browser_condition(결-7 · S8) / round = verdict 뒤 회차 줄(결-6 甲)
#   작업 폴더 = 원자료 밖(러너 임시 폴더) · 원자료 폴더 = 공개될 파일만
#   종료 코드: 0 = 통과 · 1 = 그 단계 실패(실패 사실 · 오류 글자는 원자료 파일에 남김)
#   v5 별건 A(작업지시서 20261118작 K2 · 설계 20261118_S4_별건A_측정환경공급망 §5-2 · §5-2-1 · §5-3 · §5-6 C1): 하위 명령 2개 더함(위 사용 줄 글자는 옛 글자 그대로)
#     snap  = M3′ 앞 단면(⑤ 바로 앞 · 작업 폴더 snap/ · 원자료 0) — 옛 설계의 「m2 맨 앞 앞 단면」 을 옮김(m2 에는 앞 단면 저장 0)
#     guard = M3′ 설치 뒤 단언 G1~G6(M3 뒤 · M4 앞) → 원자료 m3g-guard.json(참거짓 · 이름만 · 값 · 해시 0) · 앞 단면 없으면 빨강
#     m2 = 설치 전후 dpkg 차이(m2-installed.txt) · 이번 apt 기록(m2-apt-history.txt) · 앞 환경 기록(m2-env-before.txt) 더함
#     m3 = 잠금 두 파일(pw/) 작업 폴더 복사 → 장치 나무 blob 대조 → 잠금 안 판 = PW_VERSION → npm ci --ignore-scripts · 트리 기록(m3-npm-tree.json) · 설치 출력 원자료로(m3-npm.txt)
#   v5 별건 A K2′([3-V] 병렬검증 P1-1 · P1-2 고침 · 설계 §5-2 「장치 나무 blob 대조」 · §5-2-1 G5 「= 장치 커밋」 글자 안):
#     m3 = 설치 자리를 비우고 시작 · 복사 뒤 그 폴더 안 = 정확히 두 파일 · 트리 기록 ok 거짓이면 m3 빨강
#     장치 커밋 기준 = GITHUB_SHA(로컬 시험만 HEAD · 원자료 dev_ref) — m3 blob 기대값 · G5 대조 · G5 에 HEAD = 장치 커밋 · 색인 표시 전부 H 더함
#   v5 별건 A K2‴(CTO 결-A11 (B) · 조건 A11-1 · A11-2 / PM 지시 m1 커밋 읽기):
#     guard G1 = 장치 이름 3개(HQA_SRC_DIR · HQA_CHROME · HQA_PW_DIR) 값 = 기대값 단언 더함 — 기대값은 M1 · M3 가 to_env 로 쓰는 값과 같은 변수(V_*) · 원자료 칸 env_values_ok(참거짓만)
#     guard 의 저장소 경로 = git 없이(물리 경로) — guard 에서 G2 판정 전 git 호출 = G2 자신의 설정 목록 읽기 하나 · 다른 하위 명령의 REPO 줄 바이트 무변
#     m1 의 장치 커밋 읽기(ls-tree HEAD:…) = 명령 인자 --no-replace-objects(K2″ 와 같은 원칙)
#   v5 별건 A K2⁗([3-V] 최종 재확인 P1 · P3 #5):
#     guard G5 의 작업 나무 대조(diff · 새 파일) = 장치 커밋에서 새로 만든 임시 색인으로 잰다 — 저장소 색인의 stat · 개체 이름을 믿지 않음(색인 stat 위조 길 닫음)
#       임시 색인 = guard 가 방금 비우고 만든 snap-guard 폴더 안(저장소 작업 나무 밖 · 원자료 0) · 명령 앞 GIT_INDEX_FILE(그 git 한 번에만 · 내보내지 않음) · 끝나면 지움
#     m1 이 읽는 커밋 = 장치 커밋(g_dev · m3 · G5 와 같은 출처) — 옛 글자(K2‴) = HEAD
#   v5 별건 A K2⁵([3-V] 차이 재셈 A #1 · [4] F10 — git 속성 변환으로 G5 우회):
#     guard G5 에 작업 나무 원 바이트 대조를 더함 — 장치 커밋 ls-tree -r(--no-replace-objects) 의 경로 · 모드 · blob 이름 ↔ 작업 나무 파일마다 git hash-object --no-filters(속성 변환 없이)
#       종류 대조(장치 커밋 = 일반 파일인데 작업 나무 = 심볼릭 링크 · 폴더 · 없음 = 빨강) · 임시 색인 diff · ls-files --others 는 그대로(둘 다 같아야 초록) · 원자료 칸 무변(tree_changed = 경로 이름만)
#   v5 별건 A K2⁶([3-V] 차이 재셈 A1 · PM 판단 P1 — 작업 나무 안 `.git` 이름 항목으로 G5 초록 + guard 뒤 단계의 저장소가 바뀜):
#     ① 모든 하위 명령의 저장소 경로 = guard 와 같은 식 한 줄(장치 폴더 두 단계 위 · 물리 경로) — git rev-parse --show-toplevel 에 묻지 않음 · 그 뒤 git 호출은 -C "$REPO" 그대로
#     ② guard G5 에 `.git` 이름 항목 단언 — G 범위(두 경로) 안 모든 깊이 + G 범위 경로마다 저장소 뿌리까지 사이 폴더 바로 아래 · 하나라도 있으면(못 읽어도) 빨강
#       git 에 묻지 않고 디렉터리 항목을 직접 읽음(node · 링크를 따라가지 않음) · 원자료 새 칸 dotgit_none(참거짓) · dotgit_found(경로 이름만) · 외부 명령 새 이름 0
#   v5 별건 A K2⁷([3-V] K2⁶ 재셈 A1 · A3 · CTO 결-A12 A12-1 · A12-2 · A12-4 — 장치 폴더 · 그 위 폴더를 통째 링크로 바꾸면 위 ② 가 안으로 안 내려감):
#     ① guard G5 의 같은 node 가 G 범위 두 경로와 그 사슬 폴더(저장소 뿌리 아래 · G_TREE 에서 계산)를 lstat 해 링크면(개발 PC 정션 포함) 빨강 · 못 읽어도 빨강
#       위 ② 「링크를 따라가지 않음」 은 마지막 이름만이다 · 링크 경로 이름 = 기존 tree_changed 칸(K2⁵ 종류 대조와 같은 뜻) · 원자료 새 칸 0 · 외부 명령 새 이름 0
#     ② tree.err 비우기를 그 node 앞으로 — node 가 못 읽어 실패한 사유가 작업 폴더 진단 파일에 남음(판정 · 원자료 · 올림 · 단계 로그 무변)
set -u -o pipefail

STAGE="${1:?usage: boot.sh <m1|m2|m3|m4|runjson|round> <raw> <work>}"
RAW="${2:?raw dir}"
WORK="${3:?work dir}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# v5 별건 A K2‴(CTO A11-2): guard 는 저장소 경로를 git 없이 정한다 — 장치 폴더(tools/hqa-s4-measure)의 두 단계 위 · 물리 경로(pwd -P = git 최상위와 같은 꼴 · 심볼릭 링크 풂)
#   guard 에서 G2 판정 전 git 호출 = G2 자신의 설정 목록 읽기 하나뿐(G1 전 git 0) · 다른 하위 명령은 아래 REPO 줄 바이트 그대로(else 쪽)
# v5 별건 A K2⁶([3-V] 차이 재셈 A1 · PM 판단 P1): guard 밖 모든 하위 명령도 같은 식 — 위 K2‴ 「else 쪽 · REPO 줄 바이트 그대로」 는 이 차수에서 끝남
#   git rev-parse --show-toplevel 은 작업 나무 안 `.git` 이름 항목(장치 폴더 안 폴더 · gitdir: 파일 · 장치 폴더 위 tools/.git)을 저장소로 보아
#   guard 뒤 단계(runjson ls-remote origin · m4 cd)의 저장소를 바꿔 놓았다 → 저장소 경로는 git 에 묻지 않는다 · 그 뒤 git 호출은 -C "$REPO" 그대로
#   guard 경로 = 다른 하위 명령 경로 = 아래 한 줄(K2‴ A11-1 「식 = 한 곳」 과 같은 꼴 · 따로 단언 줄 · 원자료 칸 0)
#   옛 글자(K2‴): if [ "$STAGE" = guard ]; then REPO="$(cd "$HERE/../.." && pwd -P)"; else
#                 REPO="$(git -C "$HERE" rev-parse --show-toplevel)"
#                 fi
REPO="$(cd "$HERE/../.." && pwd -P)"
mkdir -p "$RAW" "$WORK/stages"

# H4-G 값(F″): 릴리스 · 자산 이름 = F″ 앞 8자(옛 …-b967b1b0 대체)
REL_URL="https://github.com/gisskso-lab/hitpan-erp/releases/download/hqa-s4-fixture-88085da1"
APP_TGZ=hqa-s4-fixture-linux-x64-88085da1.tar.gz
SRC_TGZ=hqa-s4-fixture-src-88085da1.tar.gz
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
# v5 별건 A K2‴(CTO A11-1): 장치 이름 3개의 값을 만드는 식 = 이 다섯 줄 한 곳 — M1 · M3 의 to_env 값 · M3 가 띄우는 경로 · guard G1 기대값이 같은 변수에서 나온다(글자 사본 두 벌 0)
#   $WORK = 하위 명령 셋째 인자(워크플로 = "$HQA_WORK" · 창 안에서 바뀌면 G1 이 이름으로 빨강)
V_PW="$WORK/pw"
V_CR="$WORK/chromium"
V_SRC_DIR="$WORK/src/hqa-s4-fixture-src-88085da1/tools/hqa-s4-fixture"
V_CHROME="$V_CR/chrome-linux64/chrome"
V_PW_DIR="$V_PW/node_modules/playwright"
# v5 별건 A K2′([3-V] P1-2): 「장치 커밋」 = GITHUB_SHA — M0 head_is_event 가 창 앞(체크아웃 바로 뒤)에서 HEAD = GITHUB_SHA 를 단언 · G1 허용 밖 이름(창 안에서 바뀌면 G1 빨강)
#   HEAD · 색인은 창 안 단계가 옮기거나 표시(assume-unchanged · skip-worktree)를 걸 수 있어 기준으로 쓰지 않는다 · Actions 밖 로컬 시험만 HEAD(원자료 dev_ref = HEAD)
#   Actions 안인데 GITHUB_SHA 가 비면 기준 없음 = 빨강(M0 head_is_event 와 같은 꼴)
g_dev() {
  if [ -n "${GITHUB_SHA:-}" ]; then printf '%s\n' "$GITHUB_SHA"; return 0; fi
  if [ "${GITHUB_ACTIONS:-}" = "true" ]; then return 1; fi
  printf 'HEAD\n'
}
g_devref() { if [ -n "${GITHUB_SHA:-}" ]; then echo GITHUB_SHA; elif [ "${GITHUB_ACTIONS:-}" = "true" ]; then echo none; else echo HEAD; fi; }
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
  # v5 별건 A K2‴(CTO A11-1): root = V_SRC_DIR(= to_env HQA_SRC_DIR 값 = guard 기대값 · 한 곳) — 옛 글자: local sx="$WORK/src" root="$WORK/src/hqa-s4-fixture-src-88085da1/tools/hqa-s4-fixture"
  local sx="$WORK/src" root="$V_SRC_DIR"
  rm -rf "$sx"; mkdir -p "$sx"
  if [ -f "$dl/$SRC_TGZ" ] && tar -xzf "$dl/$SRC_TGZ" -C "$sx" 2> "$WORK/m1-tar.err"; then
    local outside; outside=$(tar -tzf "$dl/$SRC_TGZ" | grep -v '^hqa-s4-fixture-src-88085da1/' | head -5 | tr '\n' ' ')
    if [ -n "$outside" ]; then printf 'src_prefix\t-\t0\t%s\n' "$outside" >> "$tsv"; fail=1; fi
    # 고침2 I-c: tar 안 정규 파일 · 폴더가 아닌 항목(링크 · 장치 등) 수 — 0 이 아니면 빨강(find -type f 가 조용히 건너뛰던 것)
    local nonreg; nonreg=$(tar -tvzf "$dl/$SRC_TGZ" | awk '{c=substr($1,1,1); if (c!="-" && c!="d") n++} END{print n+0}')
    if [ "$nonreg" != "0" ]; then printf 'src_nonregular\t-\t0\tcount=%s\n' "$nonreg" >> "$tsv"; fail=1; else printf 'src_nonregular\t-\t1\tcount=0\n' >> "$tsv"; fi
    # 고침1 F1: 경로 기준 대조 — 옛 꼴은 나무 목록만 「해시 경로」 줄째 정렬(=해시순) · tar 목록은 경로순이라 diff 가 엇갈렸다
    #   해시 2벌: 원 바이트(--no-filters) · 레포 속성 적용(--path · autocrlf 끔 = 러너와 같은 조건) · 판정 = 속성 적용 해시
    #   파일별 값은 원자료 m1-src-hashes.json(정규화로만 같아진 파일 수 = filter_only_match)
    #   v5 별건 A K2‴(PM 지시 · K2″ 와 같은 원칙): 커밋을 읽는 git 호출 = 명령 인자 --no-replace-objects(환경 변수로 내보내지 않음) — 옛 글자: git -C "$REPO" ls-tree -r HEAD:tools/hqa-s4-fixture
    #   v5 별건 A K2⁗([3-V] 최종 P3 #5): 읽는 커밋 = 장치 커밋(g_dev · m3 · G5 와 같은 출처 · Actions 안인데 GITHUB_SHA 가 비면 기준 없음 = 빨강) — 옛 글자(K2‴): git --no-replace-objects -C "$REPO" ls-tree -r HEAD:tools/hqa-s4-fixture
    local dev; dev=$(g_dev) || dev=""
    if [ -n "$dev" ]; then git --no-replace-objects -C "$REPO" ls-tree -r "$dev:tools/hqa-s4-fixture" | awk -F'\t' '{split($1,a," "); print $2"\t"a[3]}' | LC_ALL=C sort > "$WORK/m1-tree.tsv"
    else : > "$WORK/m1-tree.tsv"; printf 'dev_ref\t-\t0\t%s\n' "$(g_devref)" >> "$tsv"; fail=1; fi
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

# v5 별건 A(설계 §5-3 · §5-4 #1): M2 앞 환경 기록 — git 설정 범위 · 출처 · 키(값은 system · global 만) / 실행 파일 판 글자
m2_env_before() {
  echo "# M2 앞 환경 기록 — v5 별건 A(설계 §5-3 · §5-4 #1) · git 설정 = 범위 · 출처 · 키 · 값(system · global 만) / 실행 파일 판 글자"
  echo "# git config --list --show-origin --show-scope"
  git -C "$REPO" config --list --show-origin --show-scope 2>&1 | awk -F'\t' 'BEGIN { OFS = "\t" } {
    if ($1 == "system" || $1 == "global") { print; next }
    k = $3; i = index(k, "="); if (i > 0) k = substr(k, 1, i - 1); print $1, $2, k }'
  echo "# 실행 파일 판"
  printf 'node\t%s\n' "$(node --version 2>&1 | head -n 1)"
  printf 'git\t%s\n' "$(git --version 2>&1 | head -n 1)"
  printf 'bash\t%s\n' "$(bash --version 2>&1 | head -n 1)"
}

m2() {
  local log="$WORK/m2-apt.txt" ok=0 i
  : > "$log"
  # v5 별건 A(설계 §5-2 · §5-3 · §5-6 C1): 설치 전 dpkg 전체 목록 · apt 기록 줄 수 · 앞 환경 기록 — 앞 단면(M3′) 저장은 snap 으로 옮겨 여기 0
  local hist=/var/log/apt/history.log hb=0
  dpkg-query -W -f='${Package}\t${Version}\t${Architecture}\n' 2> "$WORK/m2-dpkg-before.err" | LC_ALL=C sort > "$WORK/m2-dpkg-before.tsv"
  if [ -r "$hist" ]; then hb=$(wc -l < "$hist" | tr -d ' '); fi
  m2_env_before > "$RAW/m2-env-before.txt"
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
  # v5 별건 A(설계 §5-3): 설치 뒤 목록 → 차이 = 이번에 새로 깔리거나 바뀐 꾸러미 전부(이름 · 판 · 아키텍처) · 이번 apt 기록 줄(설치 전 줄 수 뒤)
  dpkg-query -W -f='${Package}\t${Version}\t${Architecture}\n' 2> "$WORK/m2-dpkg-after.err" | LC_ALL=C sort > "$WORK/m2-dpkg-after.tsv"
  LC_ALL=C comm -13 "$WORK/m2-dpkg-before.tsv" "$WORK/m2-dpkg-after.tsv" > "$WORK/m2-dpkg-diff.tsv"
  {
    echo "# M2 새로 깔리거나 바뀐 꾸러미 — v5 별건 A(설계 §5-3) · 설치 전후 dpkg-query 차이 · 이름 · 판 · 아키텍처"
    echo "# before=$(wc -l < "$WORK/m2-dpkg-before.tsv" | tr -d ' ') after=$(wc -l < "$WORK/m2-dpkg-after.tsv" | tr -d ' ') changed=$(wc -l < "$WORK/m2-dpkg-diff.tsv" | tr -d ' ')"
    if [ -s "$WORK/m2-dpkg-before.err" ]; then echo "# dpkg-query 오류(설치 전): $(head -n 1 "$WORK/m2-dpkg-before.err" | cut -c1-200)"; fi
    if [ -s "$WORK/m2-dpkg-after.err" ]; then echo "# dpkg-query 오류(설치 뒤): $(head -n 1 "$WORK/m2-dpkg-after.err" | cut -c1-200)"; fi
    cat "$WORK/m2-dpkg-diff.tsv"
  } > "$RAW/m2-installed.txt"
  {
    echo "# M2 이번 apt 기록 줄 — v5 별건 A(설계 §5-3) · $hist 의 설치 전 줄 수(${hb}) 뒤"
    if [ -r "$hist" ]; then tail -n +"$((hb + 1))" "$hist"; else echo "# $hist 읽기 실패(없음 또는 권한)"; fi
  } > "$RAW/m2-apt-history.txt"
  stage_json m2 "$([ $ok = 1 ] && echo true || echo false)" "tries=$i"
  [ $ok = 1 ]
}

m3() {
  # v5 별건 A K2‴(CTO A11-1): pw · cr = V_PW · V_CR(아래 HQA_CHROME · HQA_PW_DIR 값의 바닥 · 한 곳) — 옛 글자: local pw="$WORK/pw" cr="$WORK/chromium" ok=0 i
  local pw="$V_PW" cr="$V_CR" ok=0 i
  # v5 별건 A K2′([3-V] P1-1): 설치 자리를 비우고 시작 — 앞 단계가 심어 둔 숨은 잠금(npm-shrinkwrap.json) · 프로젝트 설정(.npmrc)을 남기지 않는다
  rm -rf "$pw"
  mkdir -p "$pw" "$cr"
  # v5 별건 A(설계 §5-2 · §5-3 · 결-A1 甲): 아래 옛 설치(판만 고정 · 잠금 · 무결성 · 스크립트 끔 0)를 잠금 설치로 바꿈 — 옛 글자(dcaf4a3f):
  #   for i in 1 2 3; do
  #     if ( cd "$pw" && PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 timeout 300 npm install --no-save --no-package-lock --no-audit --no-fund "playwright@${PW_VERSION}" > "$WORK/m3-npm.txt" 2>&1 ); then ok=1; break; fi
  #     sleep 10
  #   done
  #   if [ $ok != 1 ]; then stage_json m3 false "npm install failed: $(tail -n 5 "$WORK/m3-npm.txt")"; return 1; fi
  # 새 길: 잠금 두 파일 → 작업 폴더 복사 → 복사본 = 장치 나무(HEAD) blob 대조 → 잠금 안 판 = PW_VERSION(다르면 빨강) → npm ci --ignore-scripts
  #   설치는 잠금만 따른다(PW_VERSION 글자는 판 대조용으로 남김) · 장치 폴더 안 node_modules 0(설치 자리 = 작업 폴더 사본)
  local f got exp lockok=1 lockmsg="" circ=x dev="" devref direx=1 trc=0
  : > "$WORK/m3-lock.tsv"
  # v5 별건 A K2′([3-V] P1-2): blob 기대값 기준 = 장치 커밋(g_dev) — 옛 글자(K2) = "HEAD:tools/hqa-s4-measure/pw/$f"
  # v5 별건 A K2″([3-V] P1 replace 참조): 장치 커밋을 읽는 git 호출 = 명령 인자 --no-replace-objects(refs/replace 로 장치 커밋을 바꿔치면 기대값도 바뀜)
  #   환경 변수(GIT_NO_REPLACE_OBJECTS)로 내보내지 않는다 — guard G1 이 자기 자신을 「바뀜」 으로 잡음 · 옛 글자(K2′) = exp=$(git -C "$REPO" rev-parse --verify -q "$dev:…")
  devref=$(g_devref)
  dev=$(g_dev) || { dev=""; lockok=0; lockmsg="$lockmsg devref"; }
  for f in package.json package-lock.json; do
    cp "$HERE/pw/$f" "$pw/$f" 2>> "$WORK/m3-lock.err" || lockok=0
    got=$(git -C "$REPO" hash-object --no-filters -- "$pw/$f" 2>> "$WORK/m3-lock.err") || got=""
    exp=""; if [ -n "$dev" ]; then exp=$(git --no-replace-objects -C "$REPO" rev-parse --verify -q "$dev:tools/hqa-s4-measure/pw/$f" 2>> "$WORK/m3-lock.err") || exp=""; fi
    if [ -n "$got" ] && [ "$got" = "$exp" ]; then printf '%s\t1\n' "$f" >> "$WORK/m3-lock.tsv"; else printf '%s\t0\n' "$f" >> "$WORK/m3-lock.tsv"; lockok=0; lockmsg="$lockmsg blob:$f"; fi
  done
  # v5 별건 A K2′([3-V] P1-1): 설치 자리 안 = 정확히 잠금 두 파일(숨은 잠금 · 설정 파일 · 그 밖 0) — 아니면 빨강 · npm ci 안 돎
  if [ "$(ls -A "$pw" 2>/dev/null | LC_ALL=C sort | tr '\n' ' ')" != "package-lock.json package.json " ]; then direx=0; lockok=0; lockmsg="$lockmsg extra"; fi
  if ! node -e 'const fs=require("fs");const [p,l,w]=process.argv.slice(1);const P=JSON.parse(fs.readFileSync(p,"utf8")),L=JSON.parse(fs.readFileSync(l,"utf8"));const d=P.dependencies||{},n=(L.packages||{})["node_modules/playwright"]||{};const ok=Object.keys(d).length===1&&d.playwright===w&&L.lockfileVersion===3&&n.version===w;if(!ok){console.error("lock version: deps="+JSON.stringify(d)+" lockfile="+L.lockfileVersion+" locked="+n.version+" want="+w);process.exit(1);}' \
       "$pw/package.json" "$pw/package-lock.json" "$PW_VERSION" 2>> "$WORK/m3-lock.err"; then
    lockok=0; lockmsg="$lockmsg version"
  fi
  if [ $lockok = 1 ]; then
    for i in 1 2 3; do
      if ( cd "$pw" && PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 timeout 300 npm ci --ignore-scripts --no-audit --no-fund > "$WORK/m3-npm.txt" 2>&1 ); then ok=1; circ=0; break; else circ=$?; fi
      sleep 10
    done
  fi
  # v5 별건 A(설계 §5-3): 설치 출력 → 원자료 · 의존 트리(이름 · 판 · 받은 주소 · 잠금 일치 · 스크립트 끔 · rc · npm 판) → 원자료(integrity 글자 0)
  if [ -f "$WORK/m3-npm.txt" ]; then cp "$WORK/m3-npm.txt" "$RAW/m3-npm.txt"; fi
  node - "$pw" "$RAW/m3-npm-tree.json" "$WORK/m3-lock.tsv" "$circ" "$PW_VERSION" "$lockok" "$(npm --version 2>/dev/null | head -n 1)" "$direx" "$devref" <<'NODE'
const fs = require('fs');
const [pw, out, tsv, rc, want, lockok, npmv, direx, devref] = process.argv.slice(2);
const rd = (f) => { try { return JSON.parse(fs.readFileSync(f, 'utf8')); } catch (e) { return null; } };
const lock = rd(pw + '/package-lock.json') || {};
const hidden = rd(pw + '/node_modules/.package-lock.json') || {};
const lp = lock.packages || {}, hp = hidden.packages || {};
const blob = fs.existsSync(tsv) ? fs.readFileSync(tsv, 'utf8').split('\n').filter((l) => l.trim()).map((l) => { const [file, m] = l.split('\t'); return { file, match: m === '1' }; }) : [];
const packages = Object.keys(lp).filter((k) => k !== '').sort().map((k) => {
  const L = lp[k] || {}, H = hp[k] || null, inst = rd(pw + '/' + k + '/package.json');
  return {
    name: k.replace(/^node_modules\//, ''),
    version: inst && typeof inst.version === 'string' ? inst.version : null,
    lock_version: L.version || null,
    resolved: H && typeof H.resolved === 'string' ? H.resolved : null,
    installed: !!inst,
    optional: L.optional === true,
    lock_match: !!inst && inst.version === L.version && !!H && H.resolved === L.resolved,
    install_script: L.hasInstallScript === true,
  };
});
const needed = packages.filter((p) => !p.optional || p.installed);
const doc = {
  stage: 'M3', ok: rc === '0' && lockok === '1' && needed.length > 0 && needed.every((p) => p.lock_match),
  command: 'npm ci --ignore-scripts --no-audit --no-fund', ignore_scripts: true, npm_ci_rc: rc === 'x' ? null : Number(rc),
  npm_version: npmv || null, lockfile_version: lock.lockfileVersion || null, pw_version: want,
  blob_match: blob, lock_ok: lockok === '1',
  install_scripts_n: packages.filter((p) => p.install_script).length,
  packages, lock_file: 'tools/hqa-s4-measure/pw/package-lock.json (device tree)',
  // v5 별건 A K2′([3-V] P1-1 · P1-2): 설치 자리 = 정확히 두 파일인가 · blob 기대값 기준(GITHUB_SHA | HEAD = 로컬 시험 | none)
  pw_dir_exact: direx === '1', dev_ref: devref || null,
};
fs.writeFileSync(out, JSON.stringify(doc, null, 2) + '\n');
// v5 별건 A K2′([3-V] P1-1): ok 거짓 = 종료 1 → m3 빨강(아래 trc)
process.exit(doc.ok ? 0 : 1);
NODE
  trc=$?
  if [ $lockok != 1 ]; then stage_json m3 false "lock check failed:$lockmsg $(tr '\r\n' '  ' < "$WORK/m3-lock.err" 2>/dev/null | cut -c1-300)"; return 1; fi
  if [ $ok != 1 ]; then stage_json m3 false "npm ci failed: $(tail -n 5 "$WORK/m3-npm.txt")"; return 1; fi
  # v5 별건 A K2′([3-V] P1-1): 트리 기록 ok(잠금 일치 · 판 · rc) 거짓 = m3 빨강 — 「설치는 잠금만 따른다」 를 글자가 아니라 동작으로
  if [ $trc != 0 ]; then stage_json m3 false "tree lock_match: m3-npm-tree.json ok=false"; return 1; fi
  rm -rf "$cr"; mkdir -p "$cr"
  if ! unzip -q "$WORK/dl/$CHROME_ZIP" -d "$cr" 2> "$WORK/m3-unzip.txt"; then stage_json m3 false "unzip failed: $(cat "$WORK/m3-unzip.txt")"; return 1; fi
  # v5 별건 A K2‴(CTO A11-1): 띄우는 브라우저 · 자동화 패키지 경로 = to_env 로 쓰는 값과 같은 변수(V_CHROME · V_PW_DIR)
  #   옛 글자: local exe="$cr/chrome-linux64/chrome" missing / PW_DIR="$pw/node_modules/playwright" EXE="$exe" …
  local exe="$V_CHROME" missing
  missing=$(ldd "$exe" 2>&1 | grep 'not found' | awk '{print $1}' | tr '\n' ' ')
  PW_DIR="$V_PW_DIR" EXE="$exe" WANT="$BROWSER_WANT" OUT="$WORK/stages/m3.json" MISSING="$missing" node <<'NODE'
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
  # v5 별건 A K2‴(CTO A11-1): 값 = V_CHROME · V_PW_DIR(guard G1 기대값과 같은 변수) — 옛 글자: to_env HQA_CHROME "$exe" / to_env HQA_PW_DIR "$pw/node_modules/playwright"
  to_env HQA_CHROME "$V_CHROME"
  to_env HQA_PW_DIR "$V_PW_DIR"
  return $rc
}

# ---- v5 별건 A(설계 §5-2-1 · §5-6 C1 · 결-A8 (나)): M3′ 앞 단면(snap · ⑤ 바로 앞) · 설치 뒤 단언(guard · M3 뒤 · M4 앞) ----
#   앞 단면 = 작업 폴더 snap/(원자료 밖) · 환경 변수는 이름별 값 sha256 만(값 0) · 결과 원자료 = m3g-guard.json(참거짓 · 이름만 · 값 · 해시 0)
#   G1 허용 = M1 · M3 가 to_env 로 쓰는 이름 3(HQA_SRC_DIR = C1 로 창이 ⑤ 앞까지 넓어져 더함) + 실행기가 단계마다 바꾸는 이름
#     실행기 몫 8개 확정 — 런 44467400813(GitHub 위 첫 guard) env_changed 실측 = 실행기 주입 이름 GITHUB_ARTIFACTS · GITHUB_ARTIFACTS_LIST 2 더함(6 → 8 · 값 무검사 · CTO 재판정 (가)) · 옛 글자 = MAPPING §12-5
#     PATH · BASH_ENV · NODE_OPTIONS · LD_PRELOAD · GIT_* 는 허용에 넣지 않는다
#   v5 별건 A K2‴(CTO 결-A11 (B) · A11-1): G1 에 장치 이름 3개 값 단언 — 뒤 단면의 HQA_SRC_DIR · HQA_CHROME · HQA_PW_DIR 값 = V_SRC_DIR · V_CHROME · V_PW_DIR(M1 · M3 가 쓴 식 그대로)
#     다르거나 없으면 G1 빨강(first_red=G1 · 새 G 번호 0) · 원자료 = env_values_ok 참거짓 하나(값 · 해시 0) · 실행기 몫 8개는 값 무검사(이름만)
#   순서 = G1(+G6 대조군) → G2 → G3 → G4 → G5 · 하나라도 빨강이면 뒤는 안 돎(설정이 바뀌었으면 git 을 부르기 전에 멈춤)
G_ALLOW=(HQA_SRC_DIR HQA_CHROME HQA_PW_DIR GITHUB_ACTION GITHUB_ENV GITHUB_OUTPUT GITHUB_PATH GITHUB_STATE GITHUB_STEP_SUMMARY GITHUB_ARTIFACTS GITHUB_ARTIFACTS_LIST)
G_BINS=(node git bash)
G_TREE=(tools/hqa-s4-measure .github/workflows/hqa-s4-measure.yml)
g_env() { node -e 'const c=require("crypto"),e=process.env,o={};for(const k of Object.keys(e).sort())o[k]=c.createHash("sha256").update(e[k]).digest("hex");process.stdout.write(JSON.stringify(o)+"\n");' > "$1"; }
g_git() { git -C "$REPO" config --list --show-origin --show-scope > "$1" 2>&1; }
g_bins() {
  local b p h
  for b in "${G_BINS[@]}"; do
    p=$(command -v "$b" 2>/dev/null) && p=$(readlink -f "$p" 2>/dev/null) || p=""
    h="-"; [ -n "$p" ] && [ -f "$p" ] && h=$(sha256sum "$p" 2>/dev/null | cut -d' ' -f1)
    printf '%s\t%s\t%s\n' "$b" "${p:-missing}" "${h:--}"
  done > "$1"
}
g_ld() { if [ -e /etc/ld.so.preload ]; then printf 'present\t%s\n' "$(sha256sum /etc/ld.so.preload 2>/dev/null | cut -d' ' -f1)"; else echo absent; fi > "$1"; }

snap() {
  local s="$WORK/snap" bad=""
  rm -rf "$s"; mkdir -p "$s"
  g_env "$s/env.json" || bad="$bad env"
  g_git "$s/git-config.txt" || bad="$bad git"
  g_bins "$s/bins.tsv" || bad="$bad bins"
  g_ld "$s/ld-preload.txt" || bad="$bad ld"
  if [ -n "$bad" ]; then rm -rf "$s"; echo "[M3′ 앞 단면] 저장 실패:$bad — 앞 단면 없음(guard 빨강)" >&2; return 1; fi
  echo "[M3′ 앞 단면] 저장 — 환경 변수 이름별 해시 · git 설정 · 실행 파일 ${#G_BINS[@]} · ld.so.preload (작업 폴더 · 원자료 0)"
}

guard() {
  local s="$WORK/snap" a="$WORK/snap-guard" f have=1 stop=0 r1="" r2="" r3="" r4="" r5="" t1=0 t2=0
  local dev="" devref="" hd="" dv="" hok="" fok=""   # v5 별건 A K2′([3-V] P1-2) — G5 기준 · HEAD = 장치 커밋 · 색인 표시
  local rok=""   # v5 별건 A K2″([3-V] P1 replace 참조) — G5 refs/replace/ 비어 있음
  local dok=""   # v5 별건 A K2⁶([3-V] 차이 재셈 A1) — G5 `.git` 이름 항목 0(G 범위 안 · 사이 폴더)
  rm -rf "$a"; mkdir -p "$a"
  for f in env.json git-config.txt bins.tsv ld-preload.txt; do [ -f "$s/$f" ] || have=0; done
  [ $have = 1 ] || stop=1
  # G1 환경 변수 이름별 값 해시 · G6 대조군(앞 단면 + 가짜 이름 1개 → 같은 비교가 잡아야 함) — git 을 부르기 전
  #   v5 별건 A K2‴(A11-1): 장치 이름 3개 값 단언 = 뒤 단면(같은 env.json) 의 값 해시 = 기대값(V_*) 해시 · 셋째 인자 뒤 3개 = 기대값(값은 g1.json · 원자료에 안 씀)
  #     옛 글자: node - "$s/env.json" "$a/env.json" "$a/g1.json" "${G_ALLOW[@]}" · const [bf, af, out, ...allow] · const g1 = !err && disallowed.length === 0
  #     옛 글자: fs.writeFileSync(out, JSON.stringify({ g1, g6: caught, changed, disallowed, error: err }) + '\n')
  if [ $stop = 0 ]; then
    if g_env "$a/env.json" && node - "$s/env.json" "$a/env.json" "$a/g1.json" "$V_SRC_DIR" "$V_CHROME" "$V_PW_DIR" "${G_ALLOW[@]}" <<'NODE'
const fs = require('fs');
const crypto = require('crypto');
const [bf, af, out, wantSrc, wantChrome, wantPw, ...allow] = process.argv.slice(2);
const diff = (b, a) => Array.from(new Set(Object.keys(b).concat(Object.keys(a)))).filter((k) => b[k] !== a[k]).sort();
const bad = (names) => names.filter((k) => !allow.includes(k));
let b = null, a = null, err = null;
try { b = JSON.parse(fs.readFileSync(bf, 'utf8')); a = JSON.parse(fs.readFileSync(af, 'utf8')); } catch (e) { err = String(e.message).slice(0, 200); }
const changed = b && a ? diff(b, a) : [];
const disallowed = bad(changed);
const planted = b ? Object.assign({}, b, { HQA_G6_PLANTED: '0' }) : null;
const caught = !!planted && bad(diff(b, planted)).includes('HQA_G6_PLANTED');
// K2‴ A11-1: 장치 이름 3개 — 뒤 단면 값 해시(g_env 와 같은 셈) = 기대값 해시 · 없거나 다르면 거짓
const want = { HQA_SRC_DIR: wantSrc, HQA_CHROME: wantChrome, HQA_PW_DIR: wantPw };
const h = (v) => crypto.createHash('sha256').update(v).digest('hex');
const valuesOk = !!a && Object.keys(want).every((k) => typeof want[k] === 'string' && want[k] !== '' && typeof a[k] === 'string' && a[k] === h(want[k]));
const g1 = !err && disallowed.length === 0 && valuesOk;
fs.writeFileSync(out, JSON.stringify({ g1, g6: caught, changed, disallowed, values_ok: valuesOk, error: err }) + '\n');
process.exit(g1 && caught ? 0 : 1);
NODE
    then r1=1; else r1=0; stop=1; fi
  fi
  # G2 git 설정 출처별 내용
  if [ $stop = 0 ]; then g_git "$a/git-config.txt"; if cmp -s "$s/git-config.txt" "$a/git-config.txt"; then r2=1; else r2=0; stop=1; fi; fi
  # G3 node · git · bash 실행 파일 바이트
  if [ $stop = 0 ]; then g_bins "$a/bins.tsv"; if cmp -s "$s/bins.tsv" "$a/bins.tsv"; then r3=1; else r3=0; stop=1; fi; fi
  # G4 /etc/ld.so.preload 있음 · 내용
  if [ $stop = 0 ]; then g_ld "$a/ld-preload.txt"; if cmp -s "$s/ld-preload.txt" "$a/ld-preload.txt"; then r4=1; else r4=0; stop=1; fi; fi
  # G5 장치 폴더 · 측정 워크플로 작업 나무 = 장치 커밋(바뀐 · 지운 · 새 파일 · 무시 파일 포함)
  #   v5 별건 A K2′([3-V] P1-2): 대조 기준 = 장치 커밋(g_dev) — 옛 글자(K2) = git -C "$REPO" diff --name-only HEAD -- "${G_TREE[@]}"
  #     + HEAD = 장치 커밋(창 안에서 HEAD 를 옮기면 빨강) + 색인 표시 전부 H(assume-unchanged · skip-worktree 가 걸린 파일은 diff 가 안 봄 → 빨강)
  #   v5 별건 A K2″([3-V] P1 replace 참조): 장치 커밋을 읽는 git 호출(diff · HEAD · "$dev^{commit}") = 명령 인자 --no-replace-objects
  #     + refs/replace/ 비어 있음(하나라도 있으면 빨강 · 원자료 = 참거짓만) — 환경 변수로 내보내지 않음(G1 이 자기 자신을 잡음)
  #     옛 글자(K2′) = git -C "$REPO" diff --name-only "$dev" · git -C "$REPO" rev-parse --verify -q HEAD · git -C "$REPO" rev-parse --verify -q "$dev^{commit}"
  #   v5 별건 A K2⁗([3-V] 최종 P1 색인 stat 위조): 작업 나무 대조(diff · 새 파일)는 장치 커밋에서 새로 만든 임시 색인으로 잰다
  #     저장소 색인은 stat 이 맞으면 파일을 다시 해시하지 않고 적힌 개체 이름을 믿는다 → 새 색인(stat 빈 칸)은 모든 파일을 다시 해시해 장치 커밋 blob 과 대조
  #     임시 색인 = "$a"(guard 가 방금 비우고 만든 폴더 · 저장소 작업 나무 밖 · 원자료 0) · GIT_INDEX_FILE 은 명령 앞에만(그 git 한 번 · 내보내지 않음 · G1 은 이미 끝) · 끝나면 지움
  #     색인 표시 단언(ls-files -v) · HEAD 단언 · refs/replace/ 단언은 저장소 색인 · 참조 그대로(증거 칸 유지)
  #     옛 글자(K2″) = if [ -n "$dev" ]; then git --no-replace-objects -C "$REPO" diff --name-only "$dev" -- "${G_TREE[@]}" > "$a/tree.txt" 2> "$a/tree.err" || t1=1; else : > "$a/tree.txt"; fi
  #                   git -C "$REPO" ls-files --others -- "${G_TREE[@]}" >> "$a/tree.txt" 2>> "$a/tree.err" || t2=1
  #   v5 별건 A K2⁵([3-V] 차이 재셈 A #1 · [4] F10): 임시 색인 diff 는 파일을 다시 해시하되 속성(.gitattributes · info/attributes)의 clean 변환 뒤 내용을 해시한다
  #     → 줄 끝 CR · 문자 부호 변환(working-tree-encoding)으로 바이트가 달라도 같다고 본다. 그래서 작업 나무 원 바이트를 따로 대조한다
  #     장치 커밋 ls-tree -r -z(--no-replace-objects) 의 경로 · 모드 · blob 이름 ↔ 작업 나무 파일마다 git hash-object --no-filters(속성 변환 없이 · M1 「원 바이트」 와 같은 꼴)
  #     종류 대조: 장치 커밋 = 일반 파일(100644 · 100755)인데 작업 나무 = 심볼릭 링크 · 폴더 · 없음이면 다름(hash-object 는 링크를 따라가므로 -L 먼저) · 장치 커밋의 다른 모드 = 대조 불가 = 다름
  #     다른 경로 = tree.txt 에 이름만(이미 있으면 한 번) · 장치 커밋 목록을 못 읽거나 비면 t1=1 · 임시 색인 diff · ls-files --others 는 지우지 않고 함께(둘 다 같아야 초록)
  #     외부 명령 = git(G5) · grep(G5) 만(새 이름 0) · 환경 변수 내보내기 0
  #   v5 별건 A K2⁶([3-V] 차이 재셈 A1 · PM 판단 P1): git 은 작업 나무를 훑을 때 `.git` 이름 항목을 늘 건너뛴다 — 임시 색인 diff · ls-files --others · 원 바이트 대조(장치 커밋 목록만 돎) 모두 못 본다
  #     → G5 맨 앞(G5 의 git 호출 전)에 디렉터리 항목을 직접 읽어 `.git` 이름 항목(폴더 · 파일 · 링크 · 종류 무관)을 찾는다 — git 에 묻지 않음 · 링크를 따라가지 않음
  #     ⓐ G 범위(두 경로) 안: 폴더면 모든 깊이(찾은 `.git` 안으로는 안 내려감) ⓑ G 범위 경로마다 저장소 뿌리까지 사이 폴더(tools · .github · .github/workflows) 바로 아래
  #     저장소 뿌리 자신의 .git 은 대조 밖(진짜 저장소) · 하나라도 있으면 빨강 · 못 읽어도 빨강(node 실패 = dok 0)
  #     원자료 = dotgit_none 참거짓 · dotgit_found 경로 이름만(저장소 뿌리 기준 · 50개까지 · 빨강일 때만) · 목록 파일 = "$a"(작업 폴더 · 원자료 0)
  #     외부 명령 = node(G1 · 원자료 쓰기와 같은 이름 · G3 바이트 확인 뒤) — 새 이름 0 · 환경 변수 내보내기 0
  #   v5 별건 A K2⁷([3-V] K2⁶ 재셈 A1 · CTO A12-2): 위 「링크를 따라가지 않음」 은 마지막 이름만이다 — 사이 폴더 · G 범위 뿌리가 링크면 lstat · readdir 은 그 링크를 따라간다
  #     → 같은 node 가 G 범위 경로마다 저장소 뿌리 아래 사슬(tools · tools/hqa-s4-measure · .github · .github/workflows · 워크플로 파일 자신)을 lstat 해 링크면(개발 PC 정션 포함) link.txt(작업 폴더 · 원자료 0)에
  #     그 이름은 git 대조 뒤 tree.txt 에 더함(이미 있으면 한 번 · K2⁵ 종류 대조와 같은 뜻 · 원자료 = tree_changed · dotgit_found 에는 `.git` 이름만) · 없음(ENOENT) = 링크 아님 · 그 밖 못 읽음 = node 실패 = 빨강
  #     외부 명령 = node · grep(이미 G5 셈 안) — 새 이름 0 · 환경 변수 내보내기 0 · 원자료 새 칸 0
  #   v5 별건 A K2⁷([3-V] K2⁶ 재셈 A3 · CTO A12-4): tree.err 비우기를 node 앞으로 옮김(node 실패 사유가 남음) — 판정 · 원자료 · 올림 · 단계 로그 무변
  #     옛 글자(K2⁶) = if node - "$REPO" "$a/dotgit.txt" "${G_TREE[@]}" 2>> "$a/tree.err" <<'NODE' · const [repo, out, ...scope] = process.argv.slice(2);
  #                   local idx; idx="$(cd "$a" && pwd -P)/dev.idx"; rm -f "$idx"; : > "$a/tree.txt"; : > "$a/tree.err"
  if [ $stop = 0 ]; then
    devref=$(g_devref); hok=0; fok=0; rok=0; dok=0
    : > "$a/tree.err"
    if node - "$REPO" "$a/dotgit.txt" "$a/link.txt" "${G_TREE[@]}" 2>> "$a/tree.err" <<'NODE'
const fs = require('fs');
const path = require('path');
const [repo, out, linkOut, ...scope] = process.argv.slice(2);
const found = [];
const links = [];
const has = (rel) => { try { fs.lstatSync(path.join(repo, rel)); return true; } catch (e) { if (e.code === 'ENOENT') return false; throw e; } };
// K2⁷ A12-2: 저장소 뿌리 아래 사슬의 한 이름이 링크인가(lstat = 그 이름 자신 · 개발 PC 정션 포함) — 없음 = 아님 · 그 밖 못 읽음 = 던짐(빨강)
const isLink = (rel) => { try { return fs.lstatSync(path.join(repo, rel)).isSymbolicLink(); } catch (e) { if (e.code === 'ENOENT') return false; throw e; } };
const walk = (rel) => {
  for (const d of fs.readdirSync(path.join(repo, rel), { withFileTypes: true })) {
    const r = rel + '/' + d.name;
    if (d.name === '.git') found.push(r);
    else if (d.isDirectory()) walk(r);
  }
};
for (const p of scope) {
  const parts = p.split('/');
  for (let i = 1; i <= parts.length; i++) { const r = parts.slice(0, i).join('/'); if (isLink(r)) links.push(r); }
  for (let i = 1; i < parts.length; i++) { const r = parts.slice(0, i).join('/') + '/.git'; if (has(r)) found.push(r); }
  let st = null;
  try { st = fs.lstatSync(path.join(repo, p)); } catch (e) { if (e.code !== 'ENOENT') throw e; }
  if (st && st.isDirectory()) walk(p);
}
fs.writeFileSync(linkOut, Array.from(new Set(links)).sort().map((r) => r + '\n').join(''));
fs.writeFileSync(out, Array.from(new Set(found)).sort().map((r) => r + '\n').join(''));
NODE
    then [ -s "$a/dotgit.txt" ] || dok=1; fi
    dev=$(g_dev) || { dev=""; t1=1; }
    local idx; idx="$(cd "$a" && pwd -P)/dev.idx"; rm -f "$idx"; : > "$a/tree.txt"
    if [ -n "$dev" ] && GIT_INDEX_FILE="$idx" git --no-replace-objects -C "$REPO" read-tree "$dev" 2>> "$a/tree.err"; then
      GIT_INDEX_FILE="$idx" git --no-replace-objects -C "$REPO" diff --name-only "$dev" -- "${G_TREE[@]}" > "$a/tree.txt" 2>> "$a/tree.err" || t1=1
      GIT_INDEX_FILE="$idx" git -C "$REPO" ls-files --others -- "${G_TREE[@]}" >> "$a/tree.txt" 2>> "$a/tree.err" || t2=1
    else t1=1; fi
    rm -f "$idx"
    if [ -n "$dev" ]; then
      if git --no-replace-objects -C "$REPO" ls-tree -r -z "$dev" -- "${G_TREE[@]}" > "$a/dev-tree.z" 2>> "$a/tree.err" && [ -s "$a/dev-tree.z" ]; then
        local ent meta p m o w
        while IFS= read -r -d '' ent; do
          meta=${ent%%$'\t'*}; p=${ent#*$'\t'}; m=${meta%% *}; o=${meta##* }; w=""
          case "$m" in
            100644|100755) if [ ! -L "$REPO/$p" ] && [ -f "$REPO/$p" ]; then w=$(git -C "$REPO" hash-object --no-filters -- "$p" 2>> "$a/tree.err") || w=""; fi ;;
          esac
          if [ -z "$w" ] || [ "$w" != "$o" ]; then grep -qxF -- "$p" "$a/tree.txt" || printf '%s\n' "$p" >> "$a/tree.txt"; fi
        done < "$a/dev-tree.z"
      else t1=1; fi
    fi
    # K2⁷ A12-2: 링크 이름을 tree.txt 에 더함(이미 있으면 한 번 · 위 종류 대조와 같은 꼴) — node 가 실패해 link.txt 가 없으면 dok 0 으로 이미 빨강
    if [ -f "$a/link.txt" ]; then while IFS= read -r f; do grep -qxF -- "$f" "$a/tree.txt" || printf '%s\n' "$f" >> "$a/tree.txt"; done < "$a/link.txt"; fi
    hd=$(git --no-replace-objects -C "$REPO" rev-parse --verify -q HEAD 2>> "$a/tree.err") || hd=""
    if [ -n "$dev" ]; then dv=$(git --no-replace-objects -C "$REPO" rev-parse --verify -q "$dev^{commit}" 2>> "$a/tree.err") || dv=""; fi
    if [ -n "$hd" ] && [ "$hd" = "$dv" ]; then hok=1; fi
    git -C "$REPO" ls-files -v -- "${G_TREE[@]}" > "$a/flags.txt" 2>> "$a/tree.err" || t2=1
    if [ -s "$a/flags.txt" ] && ! grep -qv '^H ' "$a/flags.txt"; then fok=1; fi
    if git --no-replace-objects -C "$REPO" for-each-ref --format='%(refname)' refs/replace/ > "$a/replace.txt" 2>> "$a/tree.err"; then [ -s "$a/replace.txt" ] || rok=1; else t2=1; fi
    if [ $t1 = 0 ] && [ $t2 = 0 ] && [ ! -s "$a/tree.txt" ] && [ $hok = 1 ] && [ $fok = 1 ] && [ $rok = 1 ] && [ $dok = 1 ]; then r5=1; else r5=0; fi
  fi
  node - "$s" "$a" "$RAW/m3g-guard.json" "$have" "$r1" "$r2" "$r3" "$r4" "$r5" "$hok" "$fok" "$devref" "$rok" "$dok" "${G_ALLOW[@]}" <<'NODE'
const fs = require('fs');
const [s, a, out, have, r1, r2, r3, r4, r5, hok, fok, devref, rok, dok, ...allow] = process.argv.slice(2);
const rd = (f) => { try { return JSON.parse(fs.readFileSync(f, 'utf8')); } catch (e) { return null; } };
const lines = (f) => (fs.existsSync(f) ? fs.readFileSync(f, 'utf8').split('\n').filter((l) => l.trim()) : []);
const tri = (r) => (r === '1' ? true : r === '0' ? false : null);
const g = rd(a + '/g1.json');
const checks = {
  G1: r1 === '' ? null : !!(g && g.g1 === true),
  G6: r1 === '' ? null : !!(g && g.g6 === true),
  G2: tri(r2), G3: tri(r3), G4: tri(r4), G5: tri(r5),
};
const SCOPES = ['system', 'global', 'local', 'worktree', 'command'];
const byScope = (ls) => { const m = {}; for (const l of ls) { const k = SCOPES.includes(l.split('\t')[0]) ? l.split('\t')[0] : 'other'; (m[k] = m[k] || []).push(l); } return m; };
let gitScopes = [];
if (checks.G2 === false) {
  const x = byScope(lines(s + '/git-config.txt')), y = byScope(lines(a + '/git-config.txt'));
  gitScopes = Array.from(new Set(Object.keys(x).concat(Object.keys(y)))).filter((k) => (x[k] || []).join('\n') !== (y[k] || []).join('\n')).sort();
}
let binsChanged = [];
if (checks.G3 === false) {
  const m = (ls) => Object.fromEntries(ls.map((l) => [l.split('\t')[0], l]));
  const x = m(lines(s + '/bins.tsv')), y = m(lines(a + '/bins.tsv'));
  binsChanged = Array.from(new Set(Object.keys(x).concat(Object.keys(y)))).filter((k) => x[k] !== y[k]).sort();
}
const tree = checks.G5 === false ? lines(a + '/tree.txt') : [];
const order = ['G1', 'G6', 'G2', 'G3', 'G4', 'G5'];
const ok = have === '1' && order.every((k) => checks[k] === true);
const firstRed = have !== '1' ? 'snap' : (order.find((k) => checks[k] === false) || null);
const doc = {
  stage: 'M3prime', ok, snap_present: have === '1', first_red: firstRed,
  order: 'G1(+G6 control) > G2 > G3 > G4 > G5 - stop at first red',
  checks,
  env_changed: g && Array.isArray(g.changed) ? g.changed : [],
  env_disallowed: g && Array.isArray(g.disallowed) ? g.disallowed : [],
  env_allow: allow,
  // v5 별건 A K2‴(CTO A11-1): 장치 이름 3개(HQA_SRC_DIR · HQA_CHROME · HQA_PW_DIR) 값 = 기대값 — 참거짓만(값 · 해시 0) · G1 안 돌면 null(G1 과 같은 꼴)
  env_values_ok: r1 === '' ? null : !!(g && g.values_ok === true),
  git_scopes: gitScopes,
  bins_changed: binsChanged,
  ld_preload: checks.G4 === null ? null : (checks.G4 ? 'same' : 'changed'),
  tree_changed_n: tree.length, tree_changed: tree.slice(0, 50),
  // v5 별건 A K2′([3-V] P1-2): G5 기준(GITHUB_SHA | HEAD = 로컬 시험 | none) · HEAD = 장치 커밋 · 색인 표시 전부 H · 표시 걸린 경로(이름만)
  dev_ref: devref || null, head_is_dev: tri(hok), index_flags_ok: tri(fok),
  index_flagged: fok === '0' ? lines(a + '/flags.txt').filter((l) => !l.startsWith('H ')).map((l) => l.slice(2)).slice(0, 50) : [],
  // v5 별건 A K2″([3-V] P1 replace 참조): refs/replace/ 비어 있음 — 참거짓만(참조 이름 · 개체 이름 0)
  replace_refs_none: tri(rok),
  // v5 별건 A K2⁶([3-V] 차이 재셈 A1): `.git` 이름 항목 0(G 범위 안 · 사이 폴더) — 참거짓 · 빨강이면 경로 이름만(저장소 뿌리 기준 · 50개까지 · 내용 0)
  dotgit_none: tri(dok),
  dotgit_found: dok === '0' ? lines(a + '/dotgit.txt').slice(0, 50) : [],
  values: 'names and true/false only - no values, no hashes (snapshot stays in the work folder)',
};
fs.writeFileSync(out, JSON.stringify(doc, null, 2) + '\n');
if (!ok) { process.stderr.write('[M3′] 설치 뒤 단언 빨강 — first_red=' + firstRed + '\n'); process.exit(1); }
console.log('[M3′] 설치 뒤 단언 통과 — G1~G6');
NODE
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
    # 고침4 G9(CTO C-3 · §9 M4): 점검 목록에 A2 화면 주소 더함(기대 200)
    # H4 §7-3: 비교 화면 주소 2 더함(기대 200 · F″ 시험 앱부터 있음)
    for p in / /before /after /after-a2 /after-e1t3 /after-v2 \
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
  RAWD="$RAW" WORKD="$WORK" HERED="$HERE" node <<'NODE'
const fs = require('fs');
const crypto = require('crypto');
const raw = process.env.RAWD, work = process.env.WORKD, here = process.env.HERED, e = process.env;
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
  // H4-G 값: 시험 앱 머리 = F″(옛 b967b1b0 대체)
  fixture_end_ok: shaOf(ls2, R_FIX) === '88085da1d797b915b8e8be314517af180d0b9939',
  main_same: !!shaOf(ls1, R_MAIN) && shaOf(ls1, R_MAIN) === shaOf(ls2, R_MAIN),
};
headsAssert.ok = ls1.length === 3 && ls2.length === 3 && headsAssert.feat_end_ok && headsAssert.fixture_end_ok && headsAssert.main_same;
const stageNames = ['M0', 'SELFTEST_LEAK', 'SELFTEST_JUDGE', 'M1', 'M2', 'M3', 'M4', 'PROBE', 'ITEMS', 'JUDGE'];
const outcomes = {};
for (const s of stageNames) outcomes[s] = e['OUT_' + s] || null;
// 고침5 H2-1(K-1): 판독기 자체시험 숫자 — selftest-judge.js --out 이 쓴 JSON(작업 폴더) · 꼴이 맞으면 원자료에 사본(selftest-judge.json)
const STJ_KEYS = ['passed', 'failed', 'total', 'plantedRed', 'plantedTotal'];
const stjFile = work + '/selftest-judge.json';
let stjCounts = null;
if (fs.existsSync(stjFile)) {
  const j = readJson(stjFile);
  const okShape = j && !j.parse_error && typeof j === 'object' && !Array.isArray(j) && STJ_KEYS.every((k) => Number.isInteger(j[k]) && j[k] >= 0);
  if (okShape) {
    stjCounts = Object.fromEntries(STJ_KEYS.map((k) => [k, j[k]]));
    stjCounts.sum_ok = j.passed + j.failed === j.total && j.plantedRed <= j.plantedTotal;
    stjCounts.file = 'selftest-judge.json';
    fs.copyFileSync(stjFile, raw + '/selftest-judge.json');
  } else {
    stjCounts = { error: 'shape — 필요 칸 ' + STJ_KEYS.join(' · ') + ' (음 아닌 정수)', file: null };
  }
}
// 고침5 H2-2(결-7 · S8): 판독 규칙 · 판독기 sha256(가림 전 · 나무 파일 바이트 · mask.js knownHex 와 같은 셈) · 브라우저 조건 값 사본
const sha256Of = (f) => (fs.existsSync(f) ? crypto.createHash('sha256').update(fs.readFileSync(f)).digest('hex') : null);
const rulesDoc = readJson(here + '/rules.json') || {};
const bc = rulesDoc.hqaPending && rulesDoc.hqaPending.browserCondition ? rulesDoc.hqaPending.browserCondition : null;
const doc = {
  run_id: e.GITHUB_RUN_ID || null, run_attempt: e.GITHUB_RUN_ATTEMPT || null,
  workflow: e.GITHUB_WORKFLOW || null, job: e.GITHUB_JOB || null, ref: e.GITHUB_REF || null,
  device_commit: e.GITHUB_SHA || null, fixture_tree: '47a909c8e7f7d11c89f455d8ff28ca2b9e8fc8e2', // H4-G 값: F″ 나무(옛 47566f04 대체)
  mode: m0.mode || null,
  // 고침4 G8 ①(D-5 ①): MODE 줄 · github.sha 를 run.json 에도(M0 가 거른 값 그대로)
  mode_line: m0.mode_line || null, github_sha: m0.github_sha || null,
  runner: { image_os: e.ImageOS || null, image_version: e.ImageVersion || null, os: e.RUNNER_OS || null, arch: e.RUNNER_ARCH || null },
  node: process.version, playwright: m3.playwright || null, browser: m3.browser || null,
  headless_shell: 'not used', ldd_missing: m3.ldd_missing || null,
  started_utc: lines(work + '/started_utc.txt')[0] || null, ended_utc: new Date().toISOString(),
  ls_remote_start: ls1, ls_remote_end: ls2, fixed_heads_unchanged: same, fixed_heads_assert: headsAssert,
  stage_outcomes: outcomes,
  stage_results: Object.fromEntries(['m1', 'm2', 'm3', 'm4'].map((s) => [s, readJson(work + '/stages/' + s + '.json')])),
  selftest_leak: readJson(work + '/selftest-leak.json'),
  selftest_judge: lines(work + '/selftest-judge.status.txt').join(' ') || null,
  // 고침5 H2-1: 옛 selftest_judge 상태 줄은 그대로 · 숫자는 새 칸(없으면 null = --out 파일 없음)
  selftest_judge_counts: stjCounts,
  // 고침5 H2-2: 판독 규칙 공개(리허설 포함)
  rules_sha256: sha256Of(here + '/rules.json'),
  judge_sha256: sha256Of(here + '/judge.js'),
  judge_rules_source: 'rules = tools/hqa-s4-measure/rules.json · judge = tools/hqa-s4-measure/judge.js — both in the tree of this raw commit (parent chain = device commit) · sha256 of tree file bytes before masking',
  browser_condition: bc ? (bc.value === undefined ? null : bc.value) : null,
  // 보탬 G-2(PM 결재 11/16): 옛 칸 이름(24자)이 가림 꼴(22자 이상 토큰)에 걸려 열쇠가 가려짐 → 21자 이하 이름으로(값 · 뜻 무변)
  browser_cond_status: bc && typeof bc.status === 'string' ? bc.status : null,
  stage_outcomes_note: 'JUDGE runs after M8/M9 (fix5 order) - see round_line.stage_outcomes_late',
  probe: fs.existsSync(raw + '/probe.json') ? 'see probe.json (rehearsal - judgement-free)' : null,
  items: m0.mode === 'armed' ? 'see samples.jsonl / verdict-device.json' : 'rehearsal: item measurement 0',
  verdict: 'device reading only - judgement = HQA',
};
fs.writeFileSync(raw + '/run.json', JSON.stringify(doc, null, 2) + '\n');
if (!headsAssert.ok) { process.stderr.write('[run.json] 고정 머리 2회차 단언 실패 ' + JSON.stringify(headsAssert) + '\n'); process.exit(1); }
NODE
}

# 고침5 H2-3(결-6 甲): M7 판독 · verdict 만 M8′ · M9′ 뒤 run.json 에 회차 줄 덧붙임(옛 칸 덮어쓰기 0 · 새 칸 round_line 하나)
#   verdict 의 round(판독기 H1-5 칸) · red 를 그대로 옮긴다 · 장치는 판정 글자를 새로 만들지 않는다
#   단 장전인데 verdict 가 없으면(판독기 미실행 · M9′ 보류) device_note 에 「verdict 없음」 사실만 · run.json 이 M9 에 보류됐으면 아무것도 안 씀
#   고침5-B ⓒ(PM 결정 11/16 · HQA Q-B): 장전인데 verdict 가 없으면 round = 「판정 불가/미통과」(rules readingText.fix5.roundUnreadable 글자 그대로 · null 금지) · red = true
#   고침5-B ⓑ: 분모 · 제외 수 · 은폐 판독은 verdict 안 — run.json 에는 verdict 파일 이름 · SHA-256 만 가리킨다
round() {
  RAWD="$RAW" HERED="$HERE" node <<'NODE'
const fs = require('fs');
const crypto = require('crypto');
const raw = process.env.RAWD, e = process.env;
const runJson = raw + '/run.json';
if (!fs.existsSync(runJson)) { console.log('[round] run.json 없음(보류 또는 M0 실패) — 회차 줄 0'); process.exit(0); }
const readJson = (f) => { if (!fs.existsSync(f)) return null; try { return JSON.parse(fs.readFileSync(f, 'utf8')); } catch (err) { return { parse_error: String(err.message).slice(0, 200) }; } };
const doc = readJson(runJson);
if (!doc || doc.parse_error) { console.error('[round] run.json 읽기 실패'); process.exit(1); }
const v = readJson(raw + '/verdict-device.json');
const w = readJson(raw + '/withheld.json');
const wStages = w && w.stages && typeof w.stages === 'object' ? w.stages : {};
const listed = (st) => Array.isArray(wStages[st]) ? wStages[st].map((x) => x && x.name).filter((x) => typeof x === 'string') : [];
const vPresent = !!(v && !v.parse_error);
const vWithheld = listed('verdict').includes('verdict-device.json') || listed('measure').includes('verdict-device.json');
const late = {};
for (const s of ['M8', 'M9', 'JUDGE', 'M8V', 'M9V']) late[s] = e['OUT_' + s] || null;
let note = null;
if (!vPresent) {
  if (doc.mode === 'armed') note = 'armed: verdict-device.json absent (' + (vWithheld ? 'withheld by leak scan' : 'judge not run or no output') + ') - device reading 0 for this round';
  else note = 'rehearsal: judge not run (item measurement 0)';
}
// 고침5-B ⓒ: 장전 · verdict 없음(보류 또는 미실행) → 회차 글자 = rules 의 「판정 불가/미통과」 · 사유 글자도 rules(장치가 새 판정 글자를 만들지 않음)
let F5 = null;
try { F5 = JSON.parse(fs.readFileSync(e.HERED + '/rules.json', 'utf8')).readingText.fix5; } catch (err) { F5 = null; }
let roundVal = vPresent && v.round !== undefined ? v.round : null;
let redVal = vPresent && typeof v.red === 'boolean' ? v.red : null;
let roundFrom = vPresent ? 'verdict-device.json' : null;
if (!vPresent && doc.mode === 'armed') {
  const unreadable = F5 && typeof F5.roundUnreadable === 'string' ? F5.roundUnreadable : 'unreadable (rules fix5 text missing)';
  const why = F5 ? (vWithheld ? F5.roundVerdictWithheld : F5.roundVerdictAbsent) : null;
  roundVal = { state: unreadable, reasons: why ? [why] : [], red: true };
  redVal = true;
  roundFrom = 'boot.sh round (verdict absent - fix5B c)';
}
const vFile = raw + '/verdict-device.json';
const vSha = vPresent && fs.existsSync(vFile) ? crypto.createHash('sha256').update(fs.readFileSync(vFile)).digest('hex') : null;
doc.round_line = {
  source: 'verdict-device.json',
  order: 'measure > run.json > M8 > M9 > M7(reads masked samples) > M8prime/M9prime(verdict only) > round_line > M10',
  verdict_present: vPresent,
  verdict_withheld: vWithheld,
  round: roundVal,
  red: redVal,
  round_from: roundFrom,
  verdict_file: 'verdict-device.json',
  verdict_sha256: vSha,
  verdict_holds: 'denominators (totals.plannedSamples/gotSamples/missingSamples/excluded) · cells[].excluded · exclusionReview · hiddenDefectSuspect = in verdict-device.json only (fix5B b)',
  device_note: note,
  withheld_files: w && Array.isArray(w.files) ? w.files.length : 0,
  stage_outcomes_late: late,
};
fs.writeFileSync(runJson, JSON.stringify(doc, null, 2) + '\n');
console.log('[round] 회차 줄 ' + JSON.stringify({ verdict_present: vPresent, verdict_withheld: vWithheld, has_round: doc.round_line.round !== null }));
NODE
}

case "$STAGE" in
  m1) m1 ;;
  m2) m2 ;;
  m3) m3 ;;
  m4) m4 ;;
  runjson) runjson ;;
  round) round ;;
  # v5 별건 A(설계 §5-6 C1 · §5-2-1)
  snap) snap ;;
  guard) guard ;;
  *) echo "unknown stage: $STAGE" >&2; exit 2 ;;
esac
