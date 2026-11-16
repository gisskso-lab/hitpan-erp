#!/usr/bin/env bash
# 비밀 탐지 — 자체시험 (러너 임시 저장소 · 매 실행 첫 단계 · 하나라도 어긋나면 빨강)
# 근거: docs/설계/보안/20261113_비밀탐지설정정상화_설계서.md §5-1 · §5-2 (S1~S4 · S6)
#       docs/운영기록/20261113작_비밀탐지설정정상화_작업지시서.md ㄱ-4
#       [3-V] 병렬이슈 F1(S8) · F2(S7) · F3(망 차단) · F4(S6 비치명·하한) · F5(가짜 사건 파일)
#
# 원칙
#  - 실제 저장소에는 아무것도 심지 않는다. 러너 임시 폴더에 일회용 저장소(원격 + 복제본)를 만든다.
#  - 가짜 비밀은 **실행 때** 키 생성 도구(openssl)로 만든다. 이 파일에는 완성된 모양이 없다.
#    가짜 비밀 내용은 어디에도 출력하지 않는다(끝에 로그 대조로 0회 확인). 엔진은 검증 끔 · 망 차단.
#  - 실제 검사와 **같은** 엔진 digest · 규칙 파일 · 제외 목록 · range.sh · scan.sh 를 쓴다.
#    범위는 「모드」 를 넘기지 않고 **가짜 사건 JSON** 을 실제와 같은 입력(GITHUB_EVENT_NAME·PATH)으로 먹인다.
#  - S5(기준선)는 갈래 ㄴ 몫 — 여기 없다.
#
# 입력(환경변수): TRUFFLEHOG_IMAGE · CONFIG_FILE · EXCLUDE_FILE (scan.sh 와 같음)
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RANGE="${HERE}/range.sh"
SCAN="${HERE}/scan.sh"
ZERO="0000000000000000000000000000000000000000"
: "${CONFIG_FILE:?CONFIG_FILE 필요}"
: "${EXCLUDE_FILE:?EXCLUDE_FILE 필요}"

root="$(mktemp -d "${RUNNER_TEMP:-/tmp}/th-selftest.XXXXXX")"
trap 'rm -rf "$root"' EXIT
ALL_LOG="${root}/all.log"   # 하위 스크립트 출력 전부 — 끝에 가짜 값 0회 대조
: > "$ALL_LOG"

# 러너의 git 전역 설정과 떨어뜨린다
export GIT_CONFIG_GLOBAL="${root}/gitconfig"
export GIT_CONFIG_NOSYSTEM=1
export GIT_AUTHOR_NAME="self-test" GIT_AUTHOR_EMAIL="self-test@example.invalid"
export GIT_COMMITTER_NAME="self-test" GIT_COMMITTER_EMAIL="self-test@example.invalid"
: > "$GIT_CONFIG_GLOBAL"

fails=0
pass() { echo "  통과  $1"; }
miss() { echo "::error title=비밀 탐지 자체시험 실패::$1"; fails=$((fails + 1)); }

# 일회용 가짜 비밀 — 키 생성 도구 출력을 파일로만 (내용 출력 0)
FAKES=()
make_fake() {
  openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 -out "$1" 2> /dev/null
  cp "$1" "${root}/fake.$(( ${#FAKES[@]} + 1 ))"
  FAKES+=("${root}/fake.$(( ${#FAKES[@]} + 1 ))")
}

# ── 꾸밈 ────────────────────────────────────────────────────────────────
origin="${root}/origin.git"
work="${root}/work"
scan_repo="${root}/scan"
git init -q --bare -b main "$origin"
git init -q -b main "$work"
w() { git -C "$work" "$@"; }

echo "readme" > "${work}/readme.txt"
w add readme.txt; w commit -q -m "m1"
make_fake "${work}/a.pem"                    # 가짜 비밀 A — main 과거 커밋
w add a.pem; w commit -q -m "m2"
echo "more" > "${work}/notes.txt"
w add notes.txt; w commit -q -m "m3"
M3="$(w rev-parse HEAD)"
w remote add origin "$origin"
w push -q origin main

w checkout -q -b feat-clean "$M3"            # 깨끗한 가지
echo "clean change" > "${work}/clean.txt"
w add clean.txt; w commit -q -m "f1"
F1="$(w rev-parse HEAD)"
w push -q origin feat-clean

w checkout -q -b feat-leak "$M3"             # 가짜 비밀 B 가 든 가지 — 첫 판
make_fake "${work}/b.pem"
w add b.pem; w commit -q -m "l1"
L1="$(w rev-parse HEAD)"
w push -q origin feat-leak
w checkout -q -B feat-leak "$M3"             # 이력 재작성 → 강제 푸시 (L1 은 어떤 참조에도 안 남는다)
make_fake "${work}/b.pem"
w add b.pem; w commit -q -m "l1 rewritten"
L1R="$(w rev-parse HEAD)"
w push -q -f origin feat-leak

w push -q origin "${M3}:refs/heads/feat-same" # main 과 같은 커밋만 든 가지

w checkout -q -b feat-excl "$M3"             # 제외 목록 경로만 고친 커밋(훑을 조각 0)
mkdir -p "${work}/.github"
echo "# self-test" > "${work}/.github/trufflehog-config.yaml"
w add .github/trufflehog-config.yaml; w commit -q -m "excl only"
EXCL="$(w rev-parse HEAD)"
w push -q origin feat-excl

w checkout -q -b feat-merge "$M3"            # S7: 이른 날짜의 가지 커밋 → main 전진 → main 을 merge
make_fake "${work}/c.pem"
w add c.pem
GIT_AUTHOR_DATE="2001-01-01T00:00:00Z" GIT_COMMITTER_DATE="2001-01-01T00:00:00Z" w commit -q -m "early"
w checkout -q main
echo "main moves" > "${work}/main2.txt"
w add main2.txt; w commit -q -m "m4"
M4="$(w rev-parse HEAD)"
w push -q origin main
w checkout -q feat-merge
w merge -q --no-ff --no-edit main > /dev/null
MERGE="$(w rev-parse HEAD)"
w push -q origin feat-merge

# S8: 기준 참조와 같은 짧은 이름의 태그를 유출 커밋에 단다(주석 태그 → after 가 태그 개체일 수 있음)
w tag -a -m "trap" "origin/main" "$L1R"
TAGOBJ="$(w rev-parse "refs/tags/origin/main")"
w push -q origin "refs/tags/origin/main"

w checkout -q --orphan lone                  # 이력이 이어지지 않는 가지
w rm -q -r --cached . > /dev/null
echo "lone" > "${work}/lone.txt"
w add lone.txt; w commit -q -m "lone"
LONE="$(w rev-parse HEAD)"
w push -q origin lone

# CI checkout 과 같은 꼴(origin/* 참조 · 태그). --no-local: 개체를 통째 복사하지 않고 전송으로 받아
# 어떤 참조에도 안 남은 커밋(강제 푸시로 떨어진 L1)은 실전처럼 복제본에 없다.
git clone -q --no-local "file://${origin}" "$scan_repo" 2>> "$ALL_LOG"

# ── 도우미 ──────────────────────────────────────────────────────────────
# 가짜 사건 파일 → range.sh (실제 경로와 같은 입력: GITHUB_EVENT_NAME · GITHUB_EVENT_PATH)
EVF="${root}/event.json"
# default_branch: 생략 = main · 빈 값("") = 빈 값 그대로 · __none__ = 키 자체 없음(S4f·S4g)
push_event() {   # ref before after [deleted] [default_branch]
  local db="${5-main}" nokey=0
  [ "$db" = "__none__" ] && nokey=1
  jq -n --arg ref "$1" --arg before "$2" --arg after "$3" --arg del "${4:-false}" --arg db "$db" --arg nokey "$nokey" \
    '{ref:$ref, before:$before, after:$after, deleted:($del=="true"),
      repository:(if $nokey == "1" then {} else {default_branch:$db} end)}' > "$EVF"
}
pr_event() {     # base head
  jq -n --arg b "$1" --arg h "$2" \
    '{pull_request:{base:{sha:$b}, head:{sha:$h}}, repository:{default_branch:"main"}}' > "$EVF"
}

r_mode="" r_base="" r_head="" r_rc=0
run_range() {    # event_name  (사건 JSON 은 바로 앞 push_event/pr_event 가 쓴 파일)
  local out="${root}/range.out" evf="$EVF"
  : > "$out"
  set +e
  REPO_DIR="$scan_repo" GITHUB_OUTPUT="$out" GITHUB_EVENT_NAME="$1" GITHUB_EVENT_PATH="$evf" \
    bash "$RANGE" >> "$ALL_LOG" 2>&1
  r_rc=$?
  set -e
  r_mode="$(sed -n 's/^mode=//p' "$out")"
  r_base="$(sed -n 's/^base=//p' "$out")"
  r_head="$(sed -n 's/^head=//p' "$out")"
}

s_state="" s_count=0 s_det="" s_rc=0 s_eng=""
run_scan() {     # BASE HEAD [CONFIG_FILE 대체]
  local sum="${root}/scan.sum"
  : > "$sum"
  set +e
  BASE="$1" HEAD="$2" REPO_DIR="$scan_repo" CONFIG_FILE="${3:-$CONFIG_FILE}" \
    NO_VERIFICATION=1 ANNOTATE=0 SUMMARY_FILE="$sum" bash "$SCAN" > "${root}/scan.last" 2>&1
  s_rc=$?
  set -e
  cat "${root}/scan.last" >> "$ALL_LOG"   # 마지막 실행 출력은 따로 남긴다(S6 출력 위생)
  s_state="$(sed -n 's/^state=//p' "$sum")"
  s_count="$(sed -n 's/^count=//p' "$sum")"
  s_count="${s_count:-0}"
  s_det="$(sed -n 's/^detectors=//p' "$sum")"
  s_eng="$(sed -n 's/^engine_exit=//p' "$sum")"
}

# 범위 계산 → (range 면) 검사. 결과: e_found=1 이면 찾음
e_found=0
event_then_scan() {   # event_name (사건 JSON 은 표준 입력)
  e_found=0
  run_range "$1"
  if [ "$r_rc" -eq 0 ] && [ "$r_mode" = "range" ]; then
    run_scan "$r_base" "$r_head"
    [ "$s_rc" -eq 1 ] && [ "$s_state" = "found" ] && [ "$s_count" -ge 1 ] && e_found=1
  fi
  return 0
}

# 규칙 파일이 실제로 실렸는지 — 자체 규칙(CustomRegex) 이름이 나와야 한다
has_custom() { case "$1" in *CustomRegex*) return 0 ;; *) return 1 ;; esac; }

echo "비밀 탐지 자체시험 — 러너 임시 저장소"

# ── S1 양성: 첫 푸시 · 가지 커밋에 가짜 비밀 B (+ 같은 꾸밈의 PR 사건) ───────
push_event refs/heads/feat-leak "$ZERO" "$L1R" ; event_then_scan push
s1_base="$r_base" s1_det="$s_det" s1_count="$s_count" s1_found="$e_found"
pr_event "$M4" "$L1R" ; event_then_scan pull_request
s1_pr="$e_found"
if [ "$s1_found" -eq 1 ] && [ "$s1_base" = "$M3" ] && has_custom "$s1_det" && [ "$s1_pr" -eq 1 ]; then
  pass "S1 양성 — 첫 푸시 찾음 ${s1_count}건 · 탐지기 ${s1_det} · PR 사건도 찾음"
else
  miss "S1 양성 — 기대 「합류점 기준 · 찾음 ≥1 · 자체 규칙 포함 · PR 도 찾음」 · 실제 찾음=${s1_found} 합류점일치=$([ "$s1_base" = "$M3" ] && echo 1 || echo 0) 탐지기=${s1_det:-없음} PR=${s1_pr}"
fi

# ── S2 음성(범위): 첫 푸시 · main 과거 커밋에 A · 가지 커밋 깨끗 ─────────
push_event refs/heads/feat-clean "$ZERO" "$F1" ; run_range push
if [ "$r_rc" -eq 0 ] && [ "$r_mode" = "range" ] && [ "$r_base" = "$M3" ]; then
  run_scan "$r_base" "$r_head"
  new_rc=$s_rc new_count=$s_count new_state=$s_state
  # 대조군: 현행 로직(첫 푸시면 BASE 비움 = 전체 조상)으로 같은 꾸밈
  run_scan "" "$F1"
  ctl_rc=$s_rc ctl_count=$s_count ctl_state=$s_state
  echo "  S2 대조 — 새 로직: ${new_state:-?} ${new_count}건 / 현행 로직(BASE 비움): ${ctl_state:-?} ${ctl_count}건"
  if [ "$new_rc" -eq 0 ] && [ "$new_state" = "clean" ] && [ "$ctl_rc" -eq 1 ] && [ "$ctl_count" -ge 1 ]; then
    pass "S2 음성 — 새 로직 0건 · 대조군 ${ctl_count}건"
  else
    miss "S2 음성 — 기대 「새 로직 깨끗 0 · 대조군 찾음 ≥1」 · 실제 새=${new_state:-?}/${new_count} 대조=${ctl_state:-?}/${ctl_count}"
  fi
else
  miss "S2 음성 — 범위 계산 어긋남 mode=${r_mode:-?} 종료=${r_rc}"
fi

# ── S3 강제 푸시: before = 저장소에 없는 커밋(실전 꼴 — 떨어진 커밋은 fetch 되지 않는다) ──
s3_absent=1
git -C "$scan_repo" cat-file -e "${L1}^{commit}" 2> /dev/null && s3_absent=0
push_event refs/heads/feat-leak "$L1" "$L1R" ; event_then_scan push
if [ "$s3_absent" -eq 1 ] && [ "$e_found" -eq 1 ] && [ "$r_base" = "$M3" ]; then
  pass "S3 강제 푸시 — 없는 before → 합류점 기준으로 B 찾음"
else
  miss "S3 강제 푸시 — 기대 「before 없음 · 합류점 기준 · 찾음」 · 실제 before없음=${s3_absent} 찾음=${e_found} 범위종료=${r_rc}"
fi

# ── S4 범위 0: 같은 커밋·삭제 = 초록 skip / 계산 불가 = 빨강 ─────────────
push_event refs/heads/feat-same "$ZERO" "$M3" ; run_range push
s4a="${r_mode:-?}/${r_rc}"; s4a_ok=0; [ "$r_rc" -eq 0 ] && [ "$r_mode" = "skip" ] && s4a_ok=1
push_event refs/heads/gone "$L1R" "$ZERO" true ; run_range push
s4b="${r_mode:-?}/${r_rc}"; s4b_ok=0; [ "$r_rc" -eq 0 ] && [ "$r_mode" = "skip" ] && s4b_ok=1
push_event refs/heads/lone "$ZERO" "$LONE" ; run_range push
s4c="${r_rc}"; s4c_ok=0; [ "$r_rc" -ne 0 ] && s4c_ok=1
push_event refs/heads/feat-clean "$ZERO" "$F1" false no-such-branch ; run_range push
s4d="${r_rc}"; s4d_ok=0; [ "$r_rc" -ne 0 ] && s4d_ok=1
push_event refs/heads/main "$L1" "$M4" ; run_range push
s4e="${r_rc}"; s4e_ok=0; [ "$r_rc" -ne 0 ] && s4e_ok=1
if [ $((s4a_ok + s4b_ok + s4c_ok + s4d_ok + s4e_ok)) -eq 5 ]; then
  pass "S4 범위 0 — 같은 커밋·삭제 skip · 이력 단절·기준 가지 없음·기본 가지 강제 푸시 = 빨강"
else
  miss "S4 범위 0 — 같은커밋(${s4a}) 삭제(${s4b}) 이력단절(${s4c}) 기준가지없음(${s4d}) 기본가지강제(${s4e})"
fi

# ── S4f·S4g 기본 가지 이름 없음(#859 후속 ㄱ′-4): 키 없음 / 빈 값 → 빨강 ───────
# 같은 사건에 기본 가지 main 을 주면 S2 처럼 범위가 계산된다 = 대조군(S1·S2·S4a~e 무후퇴).
push_event refs/heads/feat-clean "$ZERO" "$F1" false __none__ ; run_range push
s4f="${r_rc}"; s4f_ok=0; [ "$r_rc" -ne 0 ] && s4f_ok=1
push_event refs/heads/feat-clean "$ZERO" "$F1" false "" ; run_range push
s4g="${r_rc}"; s4g_ok=0; [ "$r_rc" -ne 0 ] && s4g_ok=1
if [ $((s4f_ok + s4g_ok)) -eq 2 ]; then
  pass "S4f·S4g 기본 가지 이름 없음 — 키 없음·빈 값 모두 빨강(main 으로 대신하지 않음)"
else
  miss "S4f·S4g 기본 가지 이름 없음 — 기대 「둘 다 종료 ≠0」 · 실제 키없음(${s4f}) 빈값(${s4g})"
fi

# ── S6 엔진 오류: 「검사 오류」 는 「0건」 과 구분된다 ────────────────────
# 오류 길의 출력은 scan.sh 자기 문구 두 줄과 「글자 그대로」 같아야 한다(엔진 기록 글자 0 · 외부 예비 회신 #6846795334)
# [4] 재검증 V2-1: 줄 머리만 보면 꼬리·문구 안에 엔진 글자가 붙어도 통과한다 → 문구 끝까지 `$` 로 맞춘다.
S6_LINE_COUNT='엔진 기록 오류 줄 수: [0-9]+ \(문구는 찍지 않음 — 로컬 재현으로 확인\)'
S6_LINE_ERROR='\[검사 오류\] 엔진 종료 코드 [0-9]+ · 결과 [0-9]+줄 — 「찾음」 이 아니라 검사가 끝나지 않았다'
# 셋 다 파일을 못 읽으면 실패 쪽 값(V2-5)
s6_other()      { local n; n="$(grep -cvE "^(${S6_LINE_COUNT}|${S6_LINE_ERROR})\$" "${root}/scan.last" 2>/dev/null)" || true; echo "${n:-1}"; }
s6_count_line() { local n; n="$(grep -cE "^${S6_LINE_COUNT}\$" "${root}/scan.last" 2>/dev/null)" || true; echo "${n:-0}"; }
# 카나리(보조): 엔진 오류 기록에 든 글자가 출력에 0회여야 한다. 막는 힘의 본체는 위 「문구 끝까지 맞추기」.
#   (a) 없는 규칙 파일 이름 — 엔진 치명 오류 평문에 경로째 들어간다(재검증 3: 그 줄을 찍는 변형에서 1 로 오름 = 동작)
#   (b) 없는 기준 커밋 값 — 엔진 3.99.8 기록에는 들어가지 않는다(재검증 3 V3-1 실측 0회) → 지금 판에서 잡는 힘 0 · 판 올림 대비로만 둔다
s6_canary()     { local n; n="$(grep -cF -e "$1" "${root}/scan.last" 2>/dev/null)" || true; echo "${n:-1}"; }
run_scan "$M3" "$F1" "${root}/no-such-config.yaml"          # (a) 시작 단계 치명 오류
s6a="${s_state:-?}/${s_rc}"; s6a_ok=0; [ "$s_rc" -eq 2 ] && [ "$s_state" = "error" ] && s6a_ok=1
s6_out="$(s6_other)"; s6_cnt="$(s6_count_line)"; s6_can="$(s6_canary no-such-config)"
s6b_base="$(openssl rand -hex 20)"
run_scan "$s6b_base" "$F1"                                   # (b) 검사 중 오류(없는 기준 커밋)
# 엔진 자신이 비 0 으로 끝나야 한다(--fail-on-scan-errors) — 바깥 하한이 대신 잡은 것과 구분
s6b="${s_state:-?}/${s_rc}/엔진${s_eng:-?}"; s6b_ok=0
[ "$s_rc" -eq 2 ] && [ "$s_state" = "error" ] && [[ "$s_eng" =~ ^[0-9]+$ ]] && [ "$s_eng" -ne 0 ] && [ "$s_eng" -ne 183 ] && s6b_ok=1
s6_out=$(( s6_out + $(s6_other) )); s6_cnt=$(( s6_cnt + $(s6_count_line) )); s6_can=$(( s6_can + $(s6_canary "$s6b_base") ))
# (d) 출력 위생 — 두 오류 실행 모두 「오류 줄 수」 한 줄씩 + 문구가 글자 그대로 다른 줄 0 + 카나리 0
#     (대조: 옛 msg 출력 · 허용 줄 꼬리/문구 안 끼워 넣기 모두 빨강)
s6d_ok=0; [ "$s6_out" -eq 0 ] && [ "$s6_cnt" -eq 2 ] && [ "$s6_can" -eq 0 ] && s6d_ok=1
# (c) 음성 대조 — 제외 목록 경로만 고친 커밋: 엔진은 커밋마다 메타 조각을 훑으므로 「조각 0 하한」 이
#     거짓 경보를 내지 않아야 한다(깨끗 · 종료 0). 하한이 서는 곳은 「엔진이 커밋을 하나도 안 걸은」 경우뿐.
run_scan "$M3" "$EXCL"
s6c="${s_state:-?}/${s_rc}"; s6c_ok=0; [ "$s_rc" -eq 0 ] && [ "$s_state" = "clean" ] && s6c_ok=1
if [ $((s6a_ok + s6b_ok + s6c_ok + s6d_ok)) -eq 4 ]; then
  pass "S6 엔진 오류 — 치명·비치명 모두 검사 오류(종료 2) · 제외 경로만 고친 커밋은 깨끗(하한 거짓 경보 0) · 오류 길 출력 = 자기 문구 그대로 · 카나리 0"
else
  miss "S6 엔진 오류 — 기대 「치명·비치명 error/2 · 제외만 clean/0 · 오류 길 다른 줄 0 · 오류 줄 수 2 · 카나리 0」 · 실제 치명(${s6a}) 비치명(${s6b}) 제외만(${s6c}) 다른 줄(${s6_out}) 오류 줄 수(${s6_cnt}) 카나리(${s6_can})"
fi

# ── S7 merge 뒤 첫 푸시: 이른 날짜의 가지 커밋을 놓치지 않는다([3-V] F2 · 엔진 범위 의미) ──
push_event refs/heads/feat-merge "$ZERO" "$MERGE" ; event_then_scan push
if [ "$e_found" -eq 1 ] && [ "$r_base" = "$M4" ]; then
  pass "S7 merge 뒤 첫 푸시 — 합류점(main 최신) 기준으로 가지 자체 커밋 찾음"
else
  miss "S7 merge 뒤 첫 푸시 — 기대 「합류점 = main 최신 · 찾음」 · 실제 찾음=${e_found} 범위종료=${r_rc}"
fi

# ── S8 같은 이름 태그: 짧은 이름 origin/main 이 태그로 가로채여도 기준은 안 바뀐다([3-V] F1) ──
s8_trap=0
[ "$(git -C "$scan_repo" rev-parse --quiet --verify "origin/main^{commit}" 2> /dev/null || true)" = "$L1R" ] && s8_trap=1
push_event refs/heads/feat-leak "$ZERO" "$L1R" ; event_then_scan push
s8_branch="$e_found"
push_event refs/tags/origin/main "$ZERO" "$TAGOBJ" ; event_then_scan push
s8_tag="$e_found"
if [ "$s8_trap" -eq 1 ] && [ "$s8_branch" -eq 1 ] && [ "$s8_tag" -eq 1 ]; then
  pass "S8 같은 이름 태그 — 함정 확인(짧은 이름=유출 커밋) · 가지·태그 첫 푸시 모두 찾음"
else
  miss "S8 같은 이름 태그 — 기대 「함정 걸림 · 가지·태그 모두 찾음」 · 실제 함정=${s8_trap} 가지=${s8_branch} 태그=${s8_tag}"
fi

# ── S9·S10 가짜 엔진(#859 후속 ㄱ′-2) ──────────────────────────────────────
# 실행 때 임시 폴더에 `docker` 실행 파일을 만들고, **그 scan.sh 호출에만** PATH 앞에 끼운다(새 환경 변수 0 ·
# scan.sh 에 시험용 손잡이 0). 가짜 엔진 = 지정 결과 줄(표준 출력) + 훑은 양 숫자 줄(오류 출력) + 지정 종료 코드.
# 🔴 출력 격리: S9 는 ANNOTATE=1(운영 꼴)이라 출력이 그대로 잡 로그에 가면 진짜 주석이 된다
#    → 출력은 F_OUT 파일로만 받고 ALL_LOG 에도 붙이지 않는다. miss 문구에는 숫자·0/1 만 넣는다.
fake_bin="${root}/fake-bin"
mkdir -p "$fake_bin"
cat > "${fake_bin}/docker" <<EOF
#!/usr/bin/env bash
cat "${root}/fake.out"
echo 'level=info msg="finished scanning" {"chunks": 3, "bytes": 120}' >&2
exit "\$(cat "${root}/fake.rc")"
EOF
chmod +x "${fake_bin}/docker"

F_OUT="${root}/fake.last"
f_rc=0 f_state="" f_det=""
run_fake() {     # 엔진_종료코드 결과_파일 ANNOTATE
  local sum="${root}/fake.sum"
  : > "$sum"
  cp "$2" "${root}/fake.out"
  echo "$1" > "${root}/fake.rc"
  set +e
  PATH="${fake_bin}:${PATH}" BASE="$M3" HEAD="$F1" REPO_DIR="$scan_repo" CONFIG_FILE="" EXCLUDE_FILE="" \
    NO_VERIFICATION=1 ANNOTATE="$3" SUMMARY_FILE="$sum" bash "$SCAN" > "$F_OUT" 2>&1
  f_rc=$?
  set -e
  f_state="$(sed -n 's/^state=//p' "$sum")"
  f_det="$(sed -n 's/^detectors=//p' "$sum")"
}
# F_OUT 줄 수 세기 — 첫 인자 = 파일을 못 읽을 때의 값(「0 이어야 함」 단언은 1 을 줘 실패 쪽으로)
fcnt() { local d="$1" n; shift; n="$(grep -c "$@" "$F_OUT" 2> /dev/null)" || true; echo "${n:-$d}"; }
# 결과 한 줄 = 탐지기 이름 하나를 단 객체(jq 가 줄바꿈·제어문자를 한 줄 안으로 이스케이프)
obj() { jq -cn --arg n "$1" '{DetectorName:$n}'; }
empty_in="${root}/empty.jsonl"
: > "$empty_in"

# S9a 양성 — 허용 3 + 주입 이름 5(줄바꿈 · ::warning · %0A · 제어문자 BEL·ESC · 41자) · 주입마다 실행 때 만든 카나리
canary="$(openssl rand -hex 8)"
s9a_in="${root}/s9a.jsonl"
{
  obj CustomRegex; obj CustomRegex; obj SQLServer
  obj "n${canary}"$'\n'"::warning title=inj::${canary}"
  obj "::warning title=inj::${canary}"
  obj "p%0A${canary}%0D%25"
  obj "c"$'\a'"${canary}"$'\e'"[31m"
  obj "${canary}${canary}ABCDEFGHI"
} > "$s9a_in"
S9A_DET="CustomRegex(2) SQLServer(1) 기타(5)"
run_fake 183 "$s9a_in" 1
s9a_lines="$(fcnt 0 -e '')"
s9a_head="$(fcnt 0 -E '^::error title=비밀 후보 찾음::8건 · 탐지기: CustomRegex\(2\) SQLServer\(1\) 기타\(5\) · ')"
s9a_colon="$(grep -o '::' "$F_OUT" 2> /dev/null | wc -l | tr -d ' ')" || true
s9a_pct="$(fcnt 1 -iE '%0A|%0D|%25')"
s9a_ctl="$(LC_ALL=C grep -c '[[:cntrl:]]' "$F_OUT" 2> /dev/null)" || true
s9a_can="$(fcnt 1 -F -e "$canary")"
s9a_sum=0; [ "$f_det" = "$S9A_DET" ] && s9a_sum=1
s9a_ok=0
[ "$f_rc" -eq 1 ] && [ "$s9a_lines" -eq 1 ] && [ "$s9a_head" -eq 1 ] && [ "${s9a_colon:-0}" -eq 2 ] \
  && [ "$s9a_pct" -eq 0 ] && [ "${s9a_ctl:-1}" -eq 0 ] && [ "$s9a_can" -eq 0 ] && [ "$s9a_sum" -eq 1 ] && s9a_ok=1
s9a="종료${f_rc}/줄${s9a_lines}/머리${s9a_head}/::${s9a_colon:-?}/%${s9a_pct}/제어${s9a_ctl:-?}/카나리${s9a_can}/요약${s9a_sum}"

# S9b 양성 대조 — 허용 이름만 3줄: 「다 기타 로 뭉개서 통과」 를 잡는다
s9b_in="${root}/s9b.jsonl"
{ obj CustomRegex; obj CustomRegex; obj SQLServer; } > "$s9b_in"
run_fake 183 "$s9b_in" 1
s9b_etc="$(fcnt 1 -F -e '기타')"
s9b_sum=0; [ "$f_det" = "CustomRegex(2) SQLServer(1)" ] && s9b_sum=1
s9b_ok=0; [ "$f_rc" -eq 1 ] && [ "$s9b_etc" -eq 0 ] && [ "$s9b_sum" -eq 1 ] && s9b_ok=1
s9b="종료${f_rc}/기타${s9b_etc}/요약${s9b_sum}"

# S9c 음성 — 종료 0 · 결과 0줄: 깨끗 한 줄 · 주석 줄 0
run_fake 0 "$empty_in" 1
s9c_lines="$(fcnt 0 -e '')"; s9c_clean="$(fcnt 0 -E '^판정: 깨끗 — ')"; s9c_ann="$(fcnt 1 -E '^::')"
s9c_ok=0; [ "$f_rc" -eq 0 ] && [ "$s9c_lines" -eq 1 ] && [ "$s9c_clean" -eq 1 ] && [ "$s9c_ann" -eq 0 ] && s9c_ok=1
s9c="종료${f_rc}/줄${s9c_lines}/깨끗${s9c_clean}/주석${s9c_ann}"

# S9d 오류 길 — 종료 2 · 결과 0줄: 오류 줄 수 한 줄 + 고정 문구 주석 한 줄(글자 그대로)
run_fake 2 "$empty_in" 1
s9d_lines="$(fcnt 0 -e '')"; s9d_cnt="$(fcnt 0 -E "^${S6_LINE_COUNT}\$")"
s9d_err="$(fcnt 0 -xF -e '::error title=검사 오류::엔진 종료 코드 2 · 결과 0줄 — 「찾음」 이 아니라 검사가 끝나지 않았다')"
s9d_ok=0; [ "$f_rc" -eq 2 ] && [ "$s9d_lines" -eq 2 ] && [ "$s9d_cnt" -eq 1 ] && [ "$s9d_err" -eq 1 ] && s9d_ok=1
s9d="종료${f_rc}/줄${s9d_lines}/오류줄수${s9d_cnt}/문구${s9d_err}"

if [ $((s9a_ok + s9b_ok + s9c_ok + s9d_ok)) -eq 4 ]; then
  pass "S9 운영 꼴 출력 위생 — 양성 한 줄(허용 이름만 · 주입 5건 = 기타(5) · 카나리 0) · 대조 기타 0 · 음성 깨끗 · 오류 길 고정 문구"
else
  miss "S9 운영 꼴 출력 위생 — 실제 양성(${s9a}) 대조(${s9b}) 음성(${s9c}) 오류길(${s9d})"
fi

# ── S10 결과 파일 엄격 해석(#859 후속 ㄱ′-3): 공백 아닌 줄은 전부 JSON 객체여야 한다 ──────
s10_in="${root}/s10.jsonl"
s10_one() {      # 이름 엔진_종료코드 기대_종료 기대_상태 (결과는 s10_in)
  run_fake "$2" "$s10_in" 0
  if [ "$f_rc" -eq "$3" ] && [ "$f_state" = "$4" ]; then s10_ok=$((s10_ok + 1)); fi
  s10="${s10}${1}(${f_state:-?}/${f_rc}) "
}
s10_ok=0 s10=""
printf '%s\n' "not a json line" > "$s10_in"; s10_one a 0 2 error          # 지금 코드라면 깨끗 0 — 대조군
printf '%s\n' "null" > "$s10_in"; s10_one b 0 2 error
{ obj CustomRegex; obj SQLServer; printf '%s\n' "[1,2]"; } > "$s10_in"; s10_one c 183 2 error
canary2="$(openssl rand -hex 8)"
{ obj CustomRegex; obj SQLServer; printf '%s\n' "{\"DetectorName\":\"${canary2}"; } > "$s10_in"; s10_one d 183 2 error
s10d_can="$(fcnt 1 -F -e "$canary2")"
printf '   \n\t\n' > "$s10_in"; s10_one e 0 0 clean                         # 빈 줄 허용 — 과잉 차단 대조
if [ "$s10_ok" -eq 5 ] && [ "$s10d_can" -eq 0 ]; then
  pass "S10 엄격 해석 — 글자·null·배열·깨진 줄 = 검사 오류(종료 2) · 깨진 줄 글자 0 · 공백 줄만 = 깨끗"
else
  miss "S10 엄격 해석 — 기대 「a~d error/2 · e clean/0 · 깨진 줄 글자 0」 · 실제 ${s10}깨진줄글자(${s10d_can})"
fi

# ── 로그 위생: 가짜 비밀 내용이 하위 출력 어디에도 0회 ─────────────────────
body="${root}/fake.body"
: > "$body"
for f in "${FAKES[@]}"; do grep -v -- '-----' "$f" >> "$body" || true; done
leaks="$(grep -c -F -f "$body" "$ALL_LOG" || true)"
if [ "${leaks:-0}" -eq 0 ] && [ -s "$body" ]; then
  pass "로그 위생 — 가짜 비밀 ${#FAKES[@]}개의 내용이 출력에 0회"
else
  miss "로그 위생 — 가짜 비밀 내용이 출력에 ${leaks:-?}줄 (대조 재료 있음=$([ -s "$body" ] && echo 1 || echo 0))"
fi

if [ "$fails" -ne 0 ]; then
  echo "::error title=비밀 탐지 자체시험::${fails}개 실패 — 검사 장치를 믿을 수 없으므로 잡을 빨강으로 끝낸다"
  exit 1
fi
echo "자체시험 S1~S4 · S4f·S4g · S6 · S7 · S8 · S9 · S10 · 로그 위생 모두 통과"
