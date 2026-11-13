#!/usr/bin/env bash
# 비밀 탐지 — 고정 엔진 실행 + 판정
# 근거: docs/설계/보안/20261113_비밀탐지설정정상화_설계서.md §3-3(로그 위생) · §4-1(판 고정)
#       docs/운영기록/20261113작_비밀탐지설정정상화_작업지시서.md ㄱ-1 · ㄱ-5
#
# 종료 코드 (둘 다 잡 빨강 · 메시지가 다르다):
#   0 = 깨끗(엔진 0 + 결과 0줄)
#   1 = 찾음(엔진 183 + 결과 1줄 이상)
#   2 = 검사 오류(그 밖의 모든 조합 — 엔진이 끝까지 못 돌았거나 결과와 종료 코드가 어긋남)
#
# 로그 위생(공개 저장소): 엔진 결과(JSON)는 임시 파일로만 받고 끝에 지운다.
#   로그에는 개수 · 탐지기 이름 · 훑은 양(숫자)만 찍는다. 값 · 위치 · 줄은 찍지 않는다.
#
# 입력(환경변수):
#   TRUFFLEHOG_IMAGE  엔진 이미지 — 반드시 @sha256: digest 고정 (필수)
#   REPO_DIR          검사할 저장소 폴더 (필수)
#   BASE HEAD         범위 (둘 다 비면 전체 이력)
#   CONFIG_FILE       탐지 규칙 파일 (비면 안 넘김)
#   EXCLUDE_FILE      제외 목록 파일 (비면 안 넘김)
#   NO_VERIFICATION   1 이면 엔진 외부 검증 호출 끔 + 컨테이너 망 차단(--network none) (자체시험 전용)
#   ANNOTATE          0 이면 ::error 주석 대신 일반 줄 (자체시험 전용 · 기본 1)
#   SUMMARY_FILE      판정 요약(state·count·detectors·engine_exit) 기록 파일 (선택)
set -uo pipefail

IMAGE="${TRUFFLEHOG_IMAGE:-}"
REPO_DIR="${REPO_DIR:-}"
BASE="${BASE:-}"
HEAD="${HEAD:-}"
ANNOTATE="${ANNOTATE:-1}"

say_error() {
  local title="$1" msg="$2"
  if [ "$ANNOTATE" = "1" ]; then
    echo "::error title=${title}::${msg}"
  else
    echo "[${title}] ${msg}"
  fi
}

write_summary() {
  [ -n "${SUMMARY_FILE:-}" ] || return 0
  {
    echo "state=$1"
    echo "count=$2"
    echo "detectors=$3"
    echo "engine_exit=$4"
  } > "$SUMMARY_FILE"
}

scan_error() {
  say_error "검사 오류" "$1 — 「찾음」 이 아니라 검사가 끝나지 않았다"
  write_summary error "${2:-0}" "" "${3:-none}"
  exit 2
}

case "$IMAGE" in
  *@sha256:*) ;;
  *) scan_error "엔진 이미지가 digest 로 고정되지 않았다" ;;
esac
[ -n "$REPO_DIR" ] && [ -d "$REPO_DIR" ] || scan_error "검사할 저장소 폴더가 없다"
REPO_ABS="$(cd "$REPO_DIR" && pwd)"

work="$(mktemp -d "${RUNNER_TEMP:-/tmp}/th-scan.XXXXXX")" || scan_error "임시 폴더를 만들지 못했다"
trap 'rm -rf "$work"' EXIT

is_sha() { [[ "$1" =~ ^[0-9a-f]{40}([0-9a-f]{24})?$ ]]; }
[ -z "$BASE" ] || is_sha "$BASE" || scan_error "BASE 가 SHA 가 아니다"
[ -z "$HEAD" ] || is_sha "$HEAD" || scan_error "HEAD 가 SHA 가 아니다"

# 범위 안 커밋 수 — 「커밋이 있는데 훑은 조각 0」 을 잡는 하한의 기준([3-V] F4)
if [ -n "$BASE" ] && [ -n "$HEAD" ]; then
  commits="$(git -C "$REPO_ABS" rev-list --count "$HEAD" "^$BASE" 2>/dev/null)" || commits=""
elif [ -n "$HEAD" ]; then
  commits="$(git -C "$REPO_ABS" rev-list --count "$HEAD" 2>/dev/null)" || commits=""
else
  commits="$(git -C "$REPO_ABS" rev-list --count --all 2>/dev/null)" || commits=""
fi

# --no-update : 래퍼가 넣어 주던 것 — 빠지면 엔진이 스스로 새 판을 받아 digest 고정이 무력해진다([3-V] F3)
# --fail-on-scan-errors : 비치명 검사 오류를 종료 0 으로 삼키지 않는다([3-V] F4)
mounts=(-v "${REPO_ABS}:/scan")
args=(git file:///scan --fail --fail-on-scan-errors --no-update --json)
[ -n "$BASE" ] && args+=(--since-commit "$BASE")
[ -n "$HEAD" ] && args+=(--branch "$HEAD")
net=()
# [4] 🟡2: 판 고정 플래그가 실제 실행 인자에 들어 있는지 실행 때마다 확인한다(빠지면 검사 오류).
# 자체시험은 망을 끊고 돌아 「갱신 시도」 가 조용히 실패하므로, 인자 자체를 잰다.
for must in --no-update --fail-on-scan-errors; do
  case " ${args[*]} " in *" ${must} "*) ;; *) scan_error "실행 인자에 ${must} 가 없다 — 판 고정·오류 판정이 깨진다" 0 0 ;; esac
done

# 규칙·제외 파일은 「그 파일이 든 폴더」 를 읽기 전용으로 붙이고 이름으로 넘긴다.
# 파일이 없으면 엔진이 스스로 오류를 낸다(자체시험 S6 이 그 길을 잰다).
if [ -n "${CONFIG_FILE:-}" ]; then
  cfg_dir="$(cd "$(dirname "$CONFIG_FILE")" 2>/dev/null && pwd)" || scan_error "규칙 파일 폴더가 없다"
  mounts+=(-v "${cfg_dir}:/th-config:ro")
  args+=("--config=/th-config/$(basename "$CONFIG_FILE")")
fi
if [ -n "${EXCLUDE_FILE:-}" ]; then
  exc_dir="$(cd "$(dirname "$EXCLUDE_FILE")" 2>/dev/null && pwd)" || scan_error "제외 목록 폴더가 없다"
  mounts+=(-v "${exc_dir}:/th-exclude:ro")
  args+=("--exclude-paths=/th-exclude/$(basename "$EXCLUDE_FILE")")
fi
if [ "${NO_VERIFICATION:-0}" = "1" ]; then
  args+=(--no-verification)
  net=(--network none)   # 자체시험: 갱신·외부 호출 시도가 있으면 실패로 드러나게
fi

docker run --rm --pull=never "${net[@]}" "${mounts[@]}" -w /scan "$IMAGE" "${args[@]}" \
  > "$work/results.jsonl" 2> "$work/engine.log"
rc=$?

# 훑은 양(숫자만) — 「아무것도 안 훑고 초록」 을 사람이 볼 수 있게
scanned="$(grep -m1 'finished scanning' "$work/engine.log" | grep -oE '"?(chunks|bytes)"?[:=] ?[0-9]+' | tr -d '"' | tr '\n' ' ')"
chunks="$(printf '%s' "$scanned" | grep -oE 'chunks[:=] ?[0-9]+' | grep -oE '[0-9]+$' || true)"
scanned="${scanned}commits=${commits:-?}"

count="$(grep -c '^{' "$work/results.jsonl" || true)"
count="${count:-0}"
detectors=""
if [ "$count" -gt 0 ]; then
  if ! names="$(jq -r '.DetectorName // "unknown"' "$work/results.jsonl" 2>/dev/null)"; then
    scan_error "엔진 결과를 읽지 못했다(종료 코드 ${rc})" "$count" "$rc"
  fi
  detectors="$(printf '%s\n' "$names" | sort | uniq -c | awk '{printf "%s%s(%s)", (NR>1?" ":""), $2, $1}')"
fi

if [ "$rc" -eq 0 ] && [ "$count" -eq 0 ]; then
  # 하한([3-V] F4): 범위에 커밋이 있는데 훑은 조각이 0(또는 기록 없음)이면 「깨끗」 이 아니라 검사 오류
  [[ "$commits" =~ ^[0-9]+$ ]] || scan_error "범위 커밋 수를 세지 못했다" 0 "$rc"
  if [ "$commits" -gt 0 ] && { [ -z "$chunks" ] || [ "$chunks" -eq 0 ]; }; then
    scan_error "범위 커밋 ${commits}개인데 훑은 조각 ${chunks:-기록 없음}" 0 "$rc"
  fi
  echo "판정: 깨끗 — 결과 0건 · 훑은 양: ${scanned}"
  write_summary clean 0 "" "$rc"
  exit 0
fi

if [ "$rc" -eq 183 ] && [ "$count" -gt 0 ]; then
  say_error "비밀 후보 찾음" "${count}건 · 탐지기: ${detectors} · 위치 확인은 로컬에서만(설계서 §3-4) · 훑은 양: ${scanned:-(기록 없음)}"
  write_summary found "$count" "$detectors" "$rc"
  exit 1
fi

# 그 밖 = 검사 오류. 공개 로그라 엔진 기록의 글자는 한 자도 찍지 않는다(설계서 §3-3 · [4] 🟡1 · 외부 예비 회신 #6846795334).
# msg 칸도 엔진이 판을 올리며 값을 끼워 넣을 수 있어 「고정 문구」 를 장담하지 못한다 → 개수만. 원문 확인은 로컬 재현에서.
# 자체시험 S6 이 「이 길의 출력 = 이 스크립트 자기 문구 두 종류뿐」 을 잰다.
errtotal="$(grep -ciE 'error|fatal' "$work/engine.log" || true)"
echo "엔진 기록 오류 줄 수: ${errtotal:-0} (문구는 찍지 않음 — 로컬 재현으로 확인)"
scan_error "엔진 종료 코드 ${rc} · 결과 ${count}줄" "$count" "$rc"
