#!/usr/bin/env bash
# 비밀 탐지 — 고정 엔진 실행 + 판정
# 근거: docs/설계/보안/20261113_비밀탐지설정정상화_설계서.md §3-3(로그 위생) · §4-1(판 고정)
#       docs/운영기록/20261113작_비밀탐지설정정상화_작업지시서.md ㄱ-1 · ㄱ-5
#
# 종료 코드 (둘 다 잡 빨강 · 메시지가 다르다):
#   0 = 깨끗(엔진 0 + 공백 아닌 결과 줄 0)
#   1 = 찾음(엔진 183 + 결과 객체 1줄 이상 + 객체 아닌 줄 0)
#   2 = 검사 오류(그 밖의 모든 조합 — 엔진이 끝까지 못 돌았거나 결과와 종료 코드가 어긋남 ·
#       공백 아닌 결과 줄 가운데 JSON 객체가 아닌 줄이 하나라도 있음)
#
# 탐지기 이름(#859 후속 ㄱ′-1): 원문은 임시 파일로만. 로그·요약에는 아래 허용 목록 안의 이름만,
#   목록 밖·모양 밖은 모두 「기타」 로 센다(개수 · 판정은 무변). 목록을 늘리려면 PR(사람 검토).
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

# 탐지기 이름 허용 목록 — 초기 값 = 관측 3개(작업지시서 §9 K1 · PM 승인 · 자체시험 S1 진짜 엔진 PrivateKey 포함 · P-6). 늘리려면 PR.
ALLOWED_DETECTORS=(CustomRegex SQLServer PrivateKey)
# 표준 입력 = @json 한 줄씩(names.raw) → 표준 출력 = 허용 이름 또는 「기타」 한 줄씩. 원문은 내보내지 않는다.
# 모양 검사는 C 지역(범위 [A-Z] 가 지역 글자로 넓어지지 않게) · 목록 대조는 글자 그대로 같음(대소문자·비슷한 글자 불허).
map_detector_names() {
  local LC_ALL=C q v n ok
  while IFS= read -r q || [ -n "$q" ]; do
    v="${q#\"}"; v="${v%\"}"
    ok=0
    if [[ "$v" =~ ^[A-Za-z0-9_]{1,40}$ ]]; then
      for n in "${ALLOWED_DETECTORS[@]}"; do [ "$v" = "$n" ] && ok=1; done
    fi
    if [ "$ok" -eq 1 ]; then printf '%s\n' "$v"; else printf '%s\n' "기타"; fi
  done
}

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
# 하한 확인용 셈(C5-N1 보완 · HQA Q2) — 기록 줄 = 엔진 JSON 객체 한 줄. 엔진 기록 글자는 작업 폴더 파일(rec.*)에만 두고
#   변수 · 출력에는 숫자와 이 스크립트 고정 낱말(obj · bad · false)만 남긴다. 셈 실패(읽기 · 해석 오류) = 빈 값 → 1층에서 닫힌다.
#   jq 표준 오류는 버린다(오류 문구에 입력 글자가 섞일 수 있다 · [3-V] P2-2).
#   records = 기록 줄 수 · vals/objn = 해석한 값 수 / 그중 msg 가 글자 그대로 같은 객체 수 · msgn = 맨 글자 "msg": 수
#   keyn/keyq/keyv = 흐름 해석 최상위 chunks 잎 수 / 그중 0~999999999 정수 수 / 그 숫자(ⓔ 숫자와 같아야 함 · [3-V] P1-1)
#   t1 = 맨 글자 chunks 수 · t2 = 경계 맞은 조각 수(둘 다 grep -ao 로 뽑은 조각 줄 수) · bsn = 역빗금 수 · ccn = 제어 글자 수(줄 끝 CR 1개 뺌)
rec_rc=0
LC_ALL=C grep -a 'finished scanning' "$work/engine.log" > "$work/rec.raw" 2>/dev/null || rec_rc=$?
records=""
if [ "$rec_rc" -le 1 ]; then records="$(LC_ALL=C grep -ac '' "$work/rec.raw" 2>/dev/null)" || true; fi
vals="" objn=""
if jq -r 'if type == "object" and .msg == "finished scanning" then "obj" else "bad" end' \
    "$work/rec.raw" > "$work/rec.kind" 2>/dev/null; then
  vals="$(grep -c '' "$work/rec.kind")" || true
  objn="$(grep -cx 'obj' "$work/rec.kind")" || true
fi
msgn="" msg_rc=0
LC_ALL=C grep -ao '"msg":' "$work/rec.raw" > "$work/rec.msg" 2>/dev/null || msg_rc=$?
if [ "$msg_rc" -le 1 ]; then msgn="$(grep -c '' "$work/rec.msg")" || true; fi
keyn="" keyq="" keyv=""
if jq -c --stream 'select(length == 2 and .[0] == ["chunks"]) | .[1]
    | if type == "number" then (if . == floor and . >= 0 and . <= 999999999 then . else false end) else false end' \
    "$work/rec.raw" > "$work/rec.key" 2>/dev/null; then
  keyn="$(grep -c '' "$work/rec.key")" || true
  keyq="$(LC_ALL=C grep -cxE '0|[123456789][0123456789]{0,8}' "$work/rec.key")" || true
  keyv="$(LC_ALL=C grep -xE '0|[123456789][0123456789]{0,8}' "$work/rec.key")" || true
fi
t1="" t1_rc=0
LC_ALL=C grep -ao 'chunks' "$work/rec.raw" > "$work/rec.t1" 2>/dev/null || t1_rc=$?
if [ "$t1_rc" -le 1 ]; then t1="$(grep -c '' "$work/rec.t1")" || true; fi
t2="" t2_rc=0 chunks=""
LC_ALL=C grep -aoE '"chunks":(0|[123456789][0123456789]{0,8})[,}]' "$work/rec.raw" > "$work/rec.t2" 2>/dev/null || t2_rc=$?
if [ "$t2_rc" -le 1 ]; then
  t2="$(grep -c '' "$work/rec.t2")" || true
  chunks="$(LC_ALL=C grep -oE '[0123456789]+' "$work/rec.t2")" || true
fi
bsn="" bs_rc=0
LC_ALL=C grep -ao '\\' "$work/rec.raw" > "$work/rec.bs" 2>/dev/null || bs_rc=$?
if [ "$bs_rc" -le 1 ]; then bsn="$(grep -c '' "$work/rec.bs")" || true; fi
ccn=""
if LC_ALL=C sed 's/\r$//' "$work/rec.raw" > "$work/rec.cc" 2>/dev/null \
    && LC_ALL=C tr -cd '\000-\011\013-\037\177' < "$work/rec.cc" > "$work/rec.cc2" 2>/dev/null; then
  ccn="$(wc -c < "$work/rec.cc2")" || true
  ccn="${ccn//[!0123456789]/}"
fi
same=0
[ -n "$keyv" ] && [ "$keyv" = "$chunks" ] && same=1
scanned="${scanned}commits=${commits:-?}"

# ── 결과 파일 엄격 해석(ㄱ′-3) ─────────────────────────────────────────
# 공백 아닌 모든 줄이 JSON 객체여야 한다. 하나라도 아니면 검사 오류(줄 내용은 찍지 않는다 · 숫자만).
# 공백 판정은 grep(C 지역) 과 jq 가 같은 ASCII 공백 6종을 쓴다 — 어긋나면 objects 가 모자라 검사 오류 쪽으로 닫힌다.
# -a = NUL 이 든 줄도 글자로 본다(보안상무 P2-1 · P-6) — 없으면 이진 판정으로 NUL 줄이 공백 줄로 셈에서 빠져 「깨끗」 으로 새는 방향을 닫는다(S10f).
nonempty="$(LC_ALL=C grep -acv '^[[:space:]]*$' "$work/results.jsonl" 2>/dev/null)" || true
[[ "$nonempty" =~ ^[0-9]+$ ]] || scan_error "엔진 결과 줄 수를 세지 못했다(종료 코드 ${rc})" 0 "$rc"
objects=0
bad=0
detectors=""
if [ "$nonempty" -gt 0 ]; then
  # 공백 아닌 줄마다 true(객체) / false(그 밖 · 해석 실패) 한 줄
  if ! jq -R 'select(test("^[ \t\n\r\f\u000b]*$") | not) | (try (fromjson | type == "object") catch false)' \
      "$work/results.jsonl" > "$work/kinds.raw" 2>/dev/null; then
    scan_error "엔진 결과를 읽지 못했다(종료 코드 ${rc})" 0 "$rc"
  fi
  objects="$(grep -cx 'true' "$work/kinds.raw")" || true
  objects="${objects:-0}"
  bad=$(( nonempty - objects ))
  [ "$bad" -eq 0 ] || scan_error "결과 ${bad}줄 해석 실패" "$objects" "$rc"

  # ── 탐지기 이름 허용 목록(ㄱ′-1) ──────────────────────────────────────
  # 객체마다 정확히 한 줄: 문자열이면 그 값, 아니면 빈 값 — @json 으로 줄바꿈·제어문자까지 한 줄 안에 가둔다.
  if ! jq -r '(.DetectorName | if type == "string" then . else "" end) | @json' \
      "$work/results.jsonl" > "$work/names.raw" 2>/dev/null; then
    scan_error "엔진 결과를 읽지 못했다(종료 코드 ${rc})" "$objects" "$rc"
  fi
  name_lines="$(grep -c '' "$work/names.raw")" || true
  [ "${name_lines:-0}" -eq "$objects" ] || scan_error "탐지기 이름 줄 수 불일치(이름 ${name_lines:-0}줄 · 객체 ${objects}줄)" "$objects" "$rc"
  detectors="$(map_detector_names < "$work/names.raw" | LC_ALL=C sort | uniq -c | awk '{printf "%s%s(%s)", (NR>1?" ":""), $2, $1}')"
fi
count="$objects"

if [ "$rc" -eq 0 ] && [ "$nonempty" -eq 0 ]; then
  # 하한([3-V] F4): 범위에 커밋이 있는데 훑은 조각이 0(또는 기록 없음)이면 「깨끗」 이 아니라 검사 오류
  [[ "$commits" =~ ^[0-9]+$ ]] || scan_error "범위 커밋 수를 세지 못했다" 0 "$rc"
  if [ "$commits" -gt 0 ]; then
    # C5-N1: 값이 정수 하나가 아니면(여러 줄 값 · 넘침) 정수 비교가 실패(상태 2)해 「거짓」 이 되고 깨끗으로 새던 꼴을 닫는다.
    # 1층(보완 · HQA Q2 · [3-V] 반영) = 비교 전에 기록 줄을 통째로 확인
    #   ⓐ 줄 1 · ⓑ 해석 값 1 · msg 같은 객체 1 · 맨 글자 "msg": 1 · ⓒ 최상위 chunks 잎 1 · 그중 범위 정수 1 · 그 값 = ⓔ 숫자
    #   · ⓓ 맨 글자 chunks 1 · ⓔ 콜론 바로 뒤 0 또는 1~9자리 · 바로 뒤 , 또는 } 인 조각 1 · ⓕ 역빗금 0 · ⓖ 제어 글자 0(줄 끝 CR 1개만 허용)
    #   · 값 = ⓔ 조각 숫자. 해석(ⓑⓒ)과 글자(ⓓ~ⓖ)가 서로를 메운다. 비교는 글자로(빈 값 · 깨진 값 = 닫힘). 2층 = 1층이 사라졌을 때의 예비.
    if [ "${records:-}" != "1" ] || [ "${vals:-}" != "1" ] || [ "${objn:-}" != "1" ] || [ "${msgn:-}" != "1" ] \
        || [ "${keyn:-}" != "1" ] || [ "${keyq:-}" != "1" ] || [ "$same" != "1" ] || [ "${t1:-}" != "1" ] || [ "${t2:-}" != "1" ] \
        || [ "${bsn:-}" != "0" ] || [ "${ccn:-}" != "0" ] || [[ ! "$chunks" =~ ^(0|[123456789][0123456789]{0,8})$ ]]; then
      scan_error "훑은 양 기록 모양 이상(기록 줄 ${records:-?}개 · 값 ${vals:-?} · 객체 ${objn:-?} · msg ${msgn:-?} · 조각 열쇠 ${keyn:-?}/${keyq:-?} · 값 일치 ${same} · 글자 ${t1:-?}/${t2:-?} · 역빗금 ${bsn:-?} · 제어 ${ccn:-?} · 범위 커밋 ${commits}개)" 0 "$rc"
    fi
    [ "$chunks" -gt 0 ] 2>/dev/null || scan_error "범위 커밋 ${commits}개인데 훑은 조각 0(또는 비교 불가)" 0 "$rc"
  fi
  echo "판정: 깨끗 — 결과 0건 · 훑은 양: ${scanned} · records=${records:-?}"
  write_summary clean 0 "" "$rc"
  exit 0
fi

if [ "$rc" -eq 183 ] && [ "$objects" -ge 1 ] && [ "$bad" -eq 0 ]; then
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
