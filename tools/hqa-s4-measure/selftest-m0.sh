#!/usr/bin/env bash
# 자체시험 M0 ③ · ③b — m0-assert.sh --diff-only 를 대조군 2 · 심은 위반 6 으로 동작으로 잰다(값커밋 결-18 · 작업지시서 §9)
#   출처: 결-13 甲(③ 허용 4경로 · ③b 고정 blob · 대조군 (다) · 심은 위반 (가)(나)) · 결-15(P3-1 모드 (마) · P3-2 4파일 없음 (라) · P3-3 usage) · [4] 2회차 P3-D (바)
#     · 결-18(판독기 자체시험 안에 있던 결-13 동작 시험을 이 파일로 옮김 · 대조군 (현) = 장치 머리 나무 · 심은 위반 (사) = --diff-only 셋째 인자 '-' 시작)
#   측정 워크플로는 이 파일을 부르지 않는다 — 사람 · 검증자가 돌린다: bash tools/hqa-s4-measure/selftest-m0.sh
#   실행 허용 검사 없음: 경계 = 「측정 런 밖」 하나 · 돌리는 사람의 환경(git 설정 · 셸 환경)을 믿는다
#   개체 격리(결-18-5): 임시 색인 · 임시 개체 폴더(GIT_OBJECT_DIRECTORY) · 저장소 개체 폴더는 대체 경로로 읽기만 → 저장소 개체 · 참조 · 색인 쓰기 0
#   원자료 파일 0: 경우마다 임시 하위 폴더를 원자료 폴더로 넘기고 파일 수 0 을 잰다 · 끝나면 임시 폴더 통째로 지움
#   끝줄: [자체시험 M0 ③ · ③b] 통과 8/8 · 심은 위반 6/6 · 대조군 2/2 — 하나라도 어긋나면 이름 붙은 줄 + 종료 1
#   대조군(T1 · T2)이 먼저 초록이어야 심은 위반 판정을 센다(대조군 실패 = 시험 전체 빨강 · 「늘 빨강」 을 가른다)
#   v5 별건 A(작업지시서 20261118작 K4 · 설계 §5-5 · 대표 결재 기록 §18 「더함」 · CTO R6 J2-빈칸): 심은 위반 2 더함
#     T9 = scan.sh 같은 blob · 모드 120000 → ③ 초록 · ③b 빨강(120000) / T10 = scan.sh 자리에 나무(scan.sh/x) → ③ 빨강(outside allowed) · ③b 빨강(040000)
#     v5 끝줄: [자체시험 M0 ③ · ③b] 통과 10/10 · 심은 위반 8/8 · 대조군 2/2 (위 끝줄 · 위 「측정 워크플로는 이 파일을 부르지 않는다」 는 v4 글자 그대로 보존)
#     v5 의미 변경: 측정 워크플로 ⑤ 단계(stm0 · M1 앞)가 이 파일을 부른다 · 경계 = 그 단계 바이트는 장치 커밋으로 고정 · M2 앞 · 허용 검사 약속 0 · 끝줄 + rc 만 원자료 negtest/
set -u -o pipefail

TAG="[자체시험 M0 ③ · ③b]"
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
M0="$HERE/m0-assert.sh"
ROOT=$(git -C "$HERE" rev-parse --show-toplevel) || { echo "$TAG 다름 — 저장소 뿌리 못 찾음" >&2; exit 1; }
cd "$ROOT" || { echo "$TAG 다름 — 저장소 뿌리로 못 감" >&2; exit 1; }

# 바깥 실행기 값은 넘기지 않는다(옛 결-13 블록과 같은 꼴)
unset GITHUB_SHA GITHUB_RUN_ATTEMPT GITHUB_ACTIONS GITHUB_OUTPUT GITHUB_RUN_ID GIT_INDEX_FILE GIT_OBJECT_DIRECTORY GIT_ALTERNATE_OBJECT_DIRECTORIES

SP4=(".github/workflows/trufflehog.yml" ".github/scripts/trufflehog/range.sh" ".github/scripts/trufflehog/scan.sh" ".github/scripts/trufflehog/self-test.sh")
SB4=("50e4e57daefdb325c110430887405dea4267d34b" "7e6534064235927f80c42df7fc7d455f210a27bd" "b44a24645cf52acdd4a634dc8a232d90096de84a" "0a369de4ab45362a088d7003f83d5a27e4710244")
BASE0=88085da1d797b915b8e8be314517af180d0b9939   # 시험 앱 고정 머리(F″) — 옛 결-13 블록 글자 그대로

# 개체 격리 — 경로는 git 이 읽는 꼴로(있으면 cygpath -m)
gpath() { if command -v cygpath >/dev/null 2>&1; then cygpath -m "$1"; else printf '%s' "$1"; fi; }
REPO_OBJ=$(git rev-parse --path-format=absolute --git-path objects) || { echo "$TAG 다름 — 저장소 개체 폴더 못 찾음" >&2; exit 1; }
T=$(mktemp -d) || { echo "$TAG 다름 — 임시 폴더 못 만듦" >&2; exit 1; }
trap 'rm -rf "$T"' EXIT
mkdir -p "$T/objects"
IDX="$(gpath "$T")/index"
export GIT_OBJECT_DIRECTORY; GIT_OBJECT_DIRECTORY=$(gpath "$T/objects")
export GIT_ALTERNATE_OBJECT_DIRECTORIES; GIT_ALTERNATE_OBJECT_DIRECTORIES="$REPO_OBJ"

# 나무 짓기 = 옛 블록 꼴(read-tree HEAD → update-index → write-tree) · 인자 = "+<모드>,<blob>,<경로>" 덧붙이기 · "-<경로>" 지우기
mktree() {
  rm -f "$IDX"
  GIT_INDEX_FILE="$IDX" git read-tree HEAD || return 1
  local e
  for e in "$@"; do
    case "$e" in
      +*) GIT_INDEX_FILE="$IDX" git update-index --add --cacheinfo "${e#+}" || return 1 ;;
      -*) GIT_INDEX_FILE="$IDX" git update-index --force-remove -- "${e#-}" || return 1 ;;
      *) return 1 ;;
    esac
  done
  GIT_INDEX_FILE="$IDX" git write-tree
}
# 4파일 = 고정 blob(모드 100644) · 인자 둘을 주면 그 번호 경로 한 곳만 "<모드>,<blob>" 로 바꿈
fix4() { local i; for i in 0 1 2 3; do if [ "${1:-x}" = "$i" ]; then printf '+%s,%s\n' "$2" "${SP4[$i]}"; else printf '+100644,%s,%s\n' "${SB4[$i]}" "${SP4[$i]}"; fi; done; }

# 한 경우 실행 — OUT · RC · NF(원자료 폴더 파일 수) · A3 · B3(1 = 통과 · 0 = 다름 · x = 글자 없음)
st() { case "$OUT" in *"$1 통과"*) echo 1 ;; *"$1 다름"*) echo 0 ;; *) echo x ;; esac; }
reset3() { OUT=""; RC=x; NF=x; A3=x; B3=x; }
run3() { # $1 = 경우 이름 · 나머지 = m0-assert.sh 둘째 인자부터
  local nm="$1"; shift
  local d="$T/m0d-$nm"; mkdir "$d"
  OUT=$(bash "$M0" "$d" "$@" 2>&1); RC=$?
  NF=$(ls -A "$d" | wc -l | tr -d ' ')
  A3=$(st m0_3_diff_allowed); B3=$(st m0_3b_secret_blobs)
}
has() { case "$OUT" in *"$1"*) return 0 ;; *) return 1 ;; esac; }

pass=0; ctl=0; pl=0
judge() { # $1 = 번호 · $2 = ctl|plant · $3 = 이름 · $4 = 0 이면 맞음
  if [ "$4" = "0" ]; then
    pass=$((pass+1)); if [ "$2" = "ctl" ]; then ctl=$((ctl+1)); else pl=$((pl+1)); fi
    echo "OK $1 $3"
  else
    echo "다름 $1 $3 — rc=$RC ③=$A3 ③b=$B3 files=$NF · $(printf '%s' "$OUT" | tr '\r\n' '  ' | cut -c1-400)"
  fi
}
red3b() { [ "$RC" = 1 ] && [ "$A3" = 1 ] && [ "$B3" = 0 ] && [ "$NF" = 0 ]; }  # ③ 초록 · ③b 빨강 · 원자료 파일 0

# 고정 blob 4개 · 바닥 판 yml 이 저장소에 있어야 함(없으면 대조군 빨강)
have=0; for b in "${SB4[@]}"; do git cat-file -e "$b" 2>/dev/null || have=1; done
baseWf=$(git rev-parse --verify "$BASE0:${SP4[0]}" 2>/dev/null) || baseWf=""
mapfile -t F4 < <(fix4)

# T1 대조군 (다): 4파일 = 고정 blob 그대로
reset3; tC=$(mktree "${F4[@]}") && run3 T1 --diff-only "$tC"
judge T1 ctl "대조군 (다): 4파일 = 고정 blob → ③ · ③b 초록(checked=4) · 원자료 파일 0" "$( [ "$have" = 0 ] && [ -n "${tC:-}" ] && [ "$RC" = 0 ] && [ "$A3" = 1 ] && [ "$B3" = 1 ] && has "checked=4" && [ "$NF" = 0 ] && echo 0 || echo 1)"

# T2 대조군 (현): 장치 머리 나무 그대로
reset3; tH=$(git rev-parse --verify "HEAD^{tree}") && run3 T2 --diff-only "$tH"
judge T2 ctl "대조군 (현): 장치 머리 나무 그대로 → ③ · ③b 초록(checked=4) · 원자료 파일 0" "$( [ -n "${tH:-}" ] && [ "$RC" = 0 ] && [ "$A3" = 1 ] && [ "$B3" = 1 ] && has "checked=4" && [ "$NF" = 0 ] && echo 0 || echo 1)"

# T3 심은 위반 (가): 4경로 밖 .github 경로 1개(허용 경로와 같은 폴더 — 와일드카드 0 확인)
reset3; xB=$(printf '# 값커밋 결-13 자체시험 심은 위반 (가)\n' | git hash-object -w --stdin) \
  && tA=$(mktree "${F4[@]}" "+100644,$xB,.github/scripts/trufflehog/extra.sh") && run3 T3 --diff-only "$tA"
judge T3 plant "심은 위반 (가): .github/scripts/trufflehog/extra.sh → ③ 빨강(extra.sh 이름)" "$( [ "$RC" = 1 ] && [ "$A3" = 0 ] && has "extra.sh" && [ "$NF" = 0 ] && echo 0 || echo 1)"

# T4 심은 위반 (나): scan.sh 끝 1바이트 바꿈(XOR 1)
reset3; nB=""
if git cat-file blob "${SB4[2]}" > "$T/scan" 2>/dev/null && [ -s "$T/scan" ]; then
  sz=$(wc -c < "$T/scan" | tr -d ' '); lb=$(tail -c 1 "$T/scan" | od -An -tu1 | tr -d ' \n')
  head -c $((sz-1)) "$T/scan" > "$T/scan2"; printf "\\$(printf '%03o' $((lb ^ 1)))" >> "$T/scan2"
  nB=$(git hash-object -w --stdin < "$T/scan2")   # 표준 입력 = 줄끝 변환 등 거름 0(옛 블록 hashW 와 같은 꼴)
fi
[ -n "$nB" ] && tB=$(mktree $(fix4 2 "100644,$nB")) && run3 T4 --diff-only "$tB"
judge T4 plant "심은 위반 (나): scan.sh 끝 1바이트 → ③ 초록 · ③b 빨강(scan.sh)" "$( [ -n "$nB" ] && [ "$nB" != "${SB4[2]}" ] && red3b && has "scan.sh" && echo 0 || echo 1)"

# T5 심은 위반 (라): 4파일 없음(87cb635c 꼴 — trufflehog.yml = 바닥 판 · scripts 3 없음)
reset3; [ -n "$baseWf" ] && tD=$(mktree "+100644,$baseWf,${SP4[0]}" "-${SP4[1]}" "-${SP4[2]}" "-${SP4[3]}") && run3 T5 --diff-only "$tD"
judge T5 plant "심은 위반 (라): 4파일 없음 → ③ 초록 · ③b 빨강(got=none · trufflehog.yml)" "$( red3b && has "got=none" && has "trufflehog.yml" && echo 0 || echo 1)"

# T6 심은 위반 (마): scan.sh 같은 blob · 모드 100755
reset3; tE=$(mktree $(fix4 2 "100755,${SB4[2]}")) && run3 T6 --diff-only "$tE"
judge T6 plant "심은 위반 (마): scan.sh 같은 blob · 100755 → ③b 빨강(100755)" "$( red3b && has "100755" && echo 0 || echo 1)"

# T7 심은 위반 (바): 바닥 판 yml + 새 scripts 3(고정 blob) 섞인 나무
reset3; [ -n "$baseWf" ] && tF=$(mktree $(fix4 0 "100644,$baseWf")) && run3 T7 --diff-only "$tF"
judge T7 plant "심은 위반 (바): 바닥 판 yml + 새 scripts 3 → ③b 빨강(trufflehog.yml · got=none 아님)" "$( red3b && has "trufflehog.yml" && ! has "got=none" && echo 0 || echo 1)"

# T8 심은 위반 (사): --diff-only 셋째 인자가 '-' 로 시작(--cached) → usage · 종료 1 · 원자료 파일 0
reset3; run3 T8 --diff-only --cached
judge T8 plant "심은 위반 (사): --diff-only --cached → rc 1 · usage · 원자료 파일 0" "$( [ "$RC" = 1 ] && has "usage" && [ "$NF" = 0 ] && [ "$A3" = x ] && [ "$B3" = x ] && echo 0 || echo 1)"

# v5 별건 A(K4) T9 심은 위반 (아): scan.sh 같은 blob · 모드 120000(링크 꼴)
reset3; tG=$(mktree $(fix4 2 "120000,${SB4[2]}")) && run3 T9 --diff-only "$tG"
judge T9 plant "심은 위반 (아): scan.sh 같은 blob · 120000 → ③ 초록 · ③b 빨강(120000)" "$( red3b && has "120000" && echo 0 || echo 1)"

# v5 별건 A(K4) T10 심은 위반 (자): 4경로 중 scan.sh 자리에 나무(scan.sh/x · 040000) — ③ · ③b 두 겹 다 잡아야 함
reset3; yB=$(printf '# v5 별건 A 자체시험 심은 위반 (자)\n' | git hash-object -w --stdin) \
  && tJ=$(mktree "${F4[@]}" "-${SP4[2]}" "+100644,$yB,${SP4[2]}/x") && run3 T10 --diff-only "$tJ"
judge T10 plant "심은 위반 (자): scan.sh 자리 나무(scan.sh/x) → ③ 빨강(scan.sh/x) · ③b 빨강(040000)" "$( [ "$RC" = 1 ] && [ "$A3" = 0 ] && [ "$B3" = 0 ] && has "scan.sh/x" && has "040000" && [ "$NF" = 0 ] && echo 0 || echo 1)"

if [ "$ctl" != 2 ]; then
  echo "$TAG 다름 — 대조군 $ctl/2 · 심은 위반 판정 안 셈(대조군 실패 = 시험 전체 빨강)"
  exit 1
fi
# v5 별건 A(K4): 수 8 → 10 · 심은 위반 6 → 8 — 옛 글자(v4):
#   if [ "$pass" != 8 ]; then
#     echo "$TAG 다름 — 통과 $pass/8 · 심은 위반 $pl/6 · 대조군 $ctl/2"
#   echo "$TAG 통과 8/8 · 심은 위반 6/6 · 대조군 2/2"
if [ "$pass" != 10 ]; then
  echo "$TAG 다름 — 통과 $pass/10 · 심은 위반 $pl/8 · 대조군 $ctl/2"
  exit 1
fi
echo "$TAG 통과 10/10 · 심은 위반 8/8 · 대조군 2/2"
exit 0
